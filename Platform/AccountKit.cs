using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Colossal.PSI.Common;
using Colossal.PSI.PdxSdk;

namespace BlueprintHub.Platform
{
    /// <summary>
    /// 官方登录态的只读门面（0.4.0 需求 4：面板右上角那颗按钮 = 个人资料）。
    ///
    /// 三条纪律，都是之前定死的：
    ///  · **不碰凭据**：登录一律交给游戏自己的登录流程（`PlatformManager.SignIn(SignInOptions.WithUI, …)`），
    ///    本模组不采集、不传输、不保存任何账号密码或 token（v0.1.0 需求 1 的那处偏离，理由：收密码等于自建钓鱼面）。
    ///  · **头像走官方通道**：`PlatformManager.GetAvatar(AvatarSize)` 拿回 PNG 字节，写进已经挂成
    ///    coui 主机的临时目录（`coui://blueprinthubcovers/…`），不发任何外部请求、不新建主机；
    ///    文件名固定、退出与登出时删除 —— 临时文件不留垃圾（作者 0.4.0 明确点名的要求）。
    ///  · **任何异常都只进日志**：账号面不可用最多让按钮退回显示「登录」，不许把面板带崩。
    ///
    /// FACT（取证见 research/api-PlatformManager.txt）：
    ///  · `PlatformManager.instance.isUserSignedIn`（:190）、`GetAvatar(AvatarSize)`（:1153）、
    ///    `SignIn(SignInOptions, Action&lt;Task&gt;)`（:1181）、`OpenPlayerProfile(ulong)`（:1158）
    ///  · `AvatarSize { Auto, Small, Medium, Large, ExtraLarge }`、`SignInOptions { None, Silent, AllowGuests, WithUI }`
    ///    （ilspycmd Colossal.PSI.Common.dll）
    /// </summary>
    public static class AccountKit
    {
        /// <summary>头像在临时目录里的固定文件名（同时也是清理目标）。</summary>
        public const string AvatarFile = "account-avatar.png";

        /// <summary>头像字节上限：官方给的是小图，超过这个数说明拿错了东西，直接丢弃。</summary>
        const long MAX_AVATAR_BYTES = 2L * 1024L * 1024L;

        public static bool LoggedIn { get; private set; }

        /// <summary>coui 地址；未登录或还没拿到头像时是空串（前端据此显示「登录」圆角框）。</summary>
        public static string AvatarUrl { get; private set; }

        static float s_NextCheck = -1f;
        static bool s_Fetching;
        static int s_FailCount;

        /// <summary>
        /// 由桥层 getter 每帧带一次，内部限流到 2 秒一次：登录状态是外部事件，读它要花钱（跨程序集属性），
        /// 而 getter 是**每帧**跑的，不能每帧去读。
        /// </summary>
        public static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (s_NextCheck > 0f && now < s_NextCheck) return;
            s_NextCheck = now + 2f;

            bool on = false;
            try { on = PlatformManager.instance != null && PlatformManager.instance.isUserSignedIn; }
            catch (Exception ex) { BlueprintHubMod.log.Warn("isUserSignedIn: " + ex.GetType().Name); }

            if (on != LoggedIn)
            {
                LoggedIn = on;
                BlueprintHubMod.log.Info("账号状态：" + (on ? "已登录" : "未登录"));
                if (!on) { AvatarUrl = string.Empty; DeleteAvatar(); }
                s_FailCount = 0;
                Systems.UI.BlueprintHubUISystem.BumpIfAny();
            }

            if (on && string.IsNullOrEmpty(AvatarUrl) && !s_Fetching && s_FailCount < 3) FetchAvatar();
        }

        /// <summary>点「登录」：把玩家交给游戏自己的登录流程，我们只负责调用与观察结果。</summary>
        public static void SignIn()
        {
            try
            {
                if (PlatformManager.instance == null) return;
                if (LoggedIn) { OpenProfile(); return; }
                var t = PlatformManager.instance.SignIn(SignInOptions.WithUI, null);
                if (t != null) Observe(t);
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("SignIn: " + ex.GetType().Name + " " + ex.Message); }
        }

        /// <summary>已登录时点按钮 = 打开游戏的个人资料页（模组的 ModsUI 那一屏），不自己画账号页。</summary>
        public static void OpenProfile()
        {
            try
            {
                PdxSdkPlatform psi = null;
                try { psi = PlatformManager.instance != null ? PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk") : null; }
                catch (Exception ex) { BlueprintHubMod.log.Warn("GetPSI<PdxSdkPlatform>: " + ex.GetType().Name); }
                if (psi != null) { psi.ShowModsUIProfilePage(); return; }
                BlueprintHubMod.log.Warn("没有 PdxSdk 平台实例：个人资料页打不开");
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("OpenProfile: " + ex.GetType().Name); }
        }

        static void Observe(Task<SignInFlags> task)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted) BlueprintHubMod.log.Warn("SignIn 任务异常");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // ---------------- 头像 ----------------

        static void FetchAvatar()
        {
            s_Fetching = true;
            try
            {
                var task = PlatformManager.instance.GetAvatar(AvatarSize.Medium);
                if (task == null) { s_Fetching = false; return; }
                SynchronizationContext ctx = SynchronizationContext.Current;
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled) { s_Fetching = false; s_FailCount++; return; }
                    if (ctx != null) ctx.Post(_ => Apply(t.Result), null);
                    else Apply(t.Result);
                });
            }
            catch (Exception ex)
            {
                s_Fetching = false; s_FailCount++;
                BlueprintHubMod.log.Warn("GetAvatar: " + ex.GetType().Name);
            }
        }

        static void Apply((int width, int height, byte[] data) avatar)
        {
            try
            {
                byte[] data = avatar.data;
                if (data == null || data.LongLength == 0L || data.LongLength > MAX_AVATAR_BYTES)
                {
                    s_FailCount++;
                    return;
                }
                LocalLibrary.EnsureDirs();
                string file = Path.Combine(LocalLibrary.TempDir, AvatarFile);
                if (!LocalLibrary.WriteBytes(file, data)) { s_FailCount++; return; }
                AvatarUrl = "coui://" + LocalLibrary.CoverHost + "/" + AvatarFile;
                BlueprintHubMod.log.Info("头像已就绪 " + avatar.width + "×" + avatar.height);
                Systems.UI.BlueprintHubUISystem.BumpIfAny();
            }
            catch (Exception ex) { s_FailCount++; BlueprintHubMod.log.Warn("Apply avatar: " + ex.GetType().Name); }
            finally { s_Fetching = false; }
        }

        /// <summary>关面板不删（下次开还要用）；登出、模组卸载、游戏退出时删。</summary>
        public static void DeleteAvatar()
        {
            try
            {
                string file = Path.Combine(LocalLibrary.TempDir, AvatarFile);
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("delete avatar: " + ex.GetType().Name); }
            AvatarUrl = string.Empty;
        }
    }
}
