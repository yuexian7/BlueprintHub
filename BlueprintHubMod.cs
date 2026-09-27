using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;      // InputActionPhase 在这里（ProxyAction / ProxyBinding 在 Game.Input）

namespace BlueprintHub
{
    /// <summary>
    /// BlueprintHub 入口。
    /// 分工：本文件只做「生命周期 + 顺序正确的初始化」，功能都在 Systems/ 与 Bpc/（纯逻辑）。
    /// 零 Harmony（v0.1.0 若必须给市辖区面板加上传按钮，再按 Playbook 步骤 7 的方式引入，见 csproj 注释）。
    /// </summary>
    public class BlueprintHubMod : IMod
    {
        /// <summary>与 Properties/PublishConfiguration.xml 的 &lt;ModVersion&gt; 一致，由 scripts/verify.mjs 钉死。</summary>
        public const string kVersion = "0.1.0";

        public const string MOD_NAME = nameof(BlueprintHub);

        public static ILog log = LogManager.GetLogger(MOD_NAME).SetShowsErrorsInUI(false);

        public static BlueprintHubMod Instance { get; private set; }

        public static bool PanelVisible { get; set; }

        private BlueprintHubSetting m_Setting;
        private ProxyAction m_TogglePanelAction;
        private bool m_QuitHooked;
        private static string s_BoundPath;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("=== BlueprintHub v" + kVersion + " (local build) ===");
            Instance = this;

            m_Setting = new BlueprintHubSetting(this);
            BlueprintHubSetting.Instance = m_Setting;

            // 需求 7 的三块设置（透明度 / 快捷键 / 关于）先注册进选项页。
            // 注意：RegisterInOptionsUI 在世界未就绪时会**静默返回 false**，先判世界。
            try
            {
                if (World.DefaultGameObjectInjectionWorld != null) m_Setting.RegisterInOptionsUI();
                else log.Warn("World not ready: options page not registered (M1 里要补重试)。");
            }
            catch (Exception ex) { log.Warn("RegisterInOptionsUI: " + ex.GetType().Name + " " + ex.Message); }

            // 硬约束顺序：先读盘，再绑键（Playbook §3.2）
            try { AssetDatabase.global.LoadSettings(MOD_NAME, m_Setting, new BlueprintHubSetting(this)); }
            catch (Exception ex) { log.Warn("LoadSettings failed, using defaults: " + ex.GetType().Name); }

            try
            {
                m_Setting.RegisterKeyBindings();
                m_TogglePanelAction = m_Setting.TogglePanelAction();
                if (m_TogglePanelAction != null)
                {
                    // 只在玩家真的设了键时才启用（否则框架给的默认路径只到设备级 → 碰一下键盘就开面板，Playbook §3.2）
                    string bound = FirstBindablePath(m_TogglePanelAction);
                    if (bound != null)
                    {
                        m_TogglePanelAction.shouldBeEnabled = true;
                        m_TogglePanelAction.onInteraction += OnTogglePanelInteraction;
                        s_BoundPath = bound;
                    }
                    else
                    {
                        m_TogglePanelAction.shouldBeEnabled = false;
                        log.Info("面板快捷键未设置（默认空，符合需求 7）。");
                    }
                }
                else log.Warn("找不到 TogglePanel 动作：快捷键不可用。");
            }
            catch (Exception ex) { log.Warn("Keybinding register: " + ex.GetType().Name + " " + ex.Message); }

            HookQuit();

            // M0 到此为止：M1 会在这里挂 UISystem（面板）+ 数据面客户端 + 存档内嵌组件。
            log.Info("BlueprintHub v" + kVersion + " 载入完成：透明度=" +
                ((int)(BlueprintHubSetting.s_PanelOpacity * 100f)) + "%，面板快捷键=" + (s_BoundPath ?? "无"));
        }

        /// <summary>验证只认 ProxyAction.bindings（真值），属性里的 path 只当参考（Playbook §3.2 的 ProxyBinding 教训）。</summary>
        private static string FirstBindablePath(ProxyAction action)
        {
            try
            {
                foreach (ProxyBinding b in action.bindings)
                {
                    string p = b.path;
                    if (InputKeyKit.IsBindablePath(p)) return p;
                }
            }
            catch (Exception ex) { log.Warn("read bindings: " + ex.GetType().Name); }
            return null;
        }

        private void OnTogglePanelInteraction(ProxyAction action, InputActionPhase phase)
        {
            // 按下沿才切，避免 Started/Performed/Canceled 连触三次
            if (phase != InputActionPhase.Started) return;
            TogglePanel();
        }

        public static void TogglePanel()
        {
            PanelVisible = !PanelVisible;
            log.Info("PanelVisible -> " + PanelVisible);
            // M1：这里通知 UI 系统刷新（CreateBinding 的 .Value 赋值）
        }

        private void HookQuit()
        {
            if (m_QuitHooked) return;
            try
            {
                // 插件 MonoBehaviour 的 OnApplicationQuit 永不触发（Playbook §3.1）→ 挂与对象生命周期无关的静态入口
                Application.quitting += OnQuitting;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                m_QuitHooked = true;
            }
            catch (Exception ex) { log.Warn("HookQuit failed: " + ex.GetType().Name); }
        }

        private void OnQuitting() { Flush("quitting"); }
        private void OnProcessExit(object sender, EventArgs e) { Flush("processExit"); }

        private void Flush(string reason)
        {
            try
            {
                // 设置落盘交给框架自己（退出时回写已有记录，Playbook §3.2）；这里只冲我们自管的状态
                Platform.LocalLibrary.Flush(reason);
                log.Info("Flush on " + reason + " done.");
            }
            catch (Exception ex) { log.Warn("Flush on " + reason + " failed: " + ex.GetType().Name); }
        }

        public void OnDispose()
        {
            log.Info("BlueprintHub OnDispose");
            try
            {
                if (m_TogglePanelAction != null) m_TogglePanelAction.onInteraction -= OnTogglePanelInteraction;
            }
            catch { }
            try
            {
                if (m_QuitHooked)
                {
                    Application.quitting -= OnQuitting;
                    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                    m_QuitHooked = false;
                }
            }
            catch { }
            Flush("dispose");
            try { if (m_Setting != null) m_Setting.UnregisterInOptionsUI(); }
            catch (Exception ex) { log.Warn("UnregisterInOptionsUI: " + ex.GetType().Name); }
            BlueprintHubSetting.Instance = null;
            Instance = null;
        }
    }

    /// <summary>键位合法性判据（纯函数，T3 离线钉死）。</summary>
    internal static class InputKeyKit
    {
        /// <summary>
        /// 只有形如 &lt;Keyboard&gt;/a、&lt;Mouse&gt;/leftButton 这种**到按键级**的路径才算已设置。
        /// 框架给未绑键 action 的默认路径只到设备级（"&lt;Keyboard&gt;/"），按前缀匹配整块设备。
        /// </summary>
        public static bool IsBindablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            int close = path.IndexOf('>');
            if (close < 0) return false;
            string rest = path.Substring(close + 1);
            return rest.Length > 0 && rest != "/";
        }
    }
}
