using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using BlueprintHub.Systems.UI;
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
        public const string kVersion = "0.3.1";

        public const string MOD_NAME = nameof(BlueprintHub);

        public static ILog log = LogManager.GetLogger(MOD_NAME).SetShowsErrorsInUI(false);

        public static BlueprintHubMod Instance { get; private set; }

        public static bool PanelVisible { get; set; }

        private BlueprintHubSetting m_Setting;
        private ProxyAction m_TogglePanelAction;
        private bool m_QuitHooked;
        private bool m_OptionsRegistered;
        private bool m_HasUi;
        private static string s_BoundPath;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("=== BlueprintHub v" + kVersion + " (local build) ===");
            Instance = this;

            m_Setting = new BlueprintHubSetting(this);
            BlueprintHubSetting.Instance = m_Setting;

            // 词条必须先注册：没有它，自动生成的设置页只能显示英文属性名，面板更是只能拿到键名。
            // 两份源分开：① 选项页（键名要 setting 实例算）② 面板（键名走 BridgeTheLanguageGap 认得的形状）。
            try
            {
                string active = "en-US";
                try { active = GameManager.instance.localizationManager.activeLocaleId ?? "en-US"; }
                catch (Exception ex) { log.Warn("activeLocaleId: " + ex.GetType().Name); }
                LocaleTable.SetActiveLocale(active);

                string[] locales = LocaleTable.Locales;
                for (int i = 0; i < locales.Length; i++)
                {
                    GameManager.instance.localizationManager.AddSource(locales[i], new LocaleSource(m_Setting, locales[i]));
                    GameManager.instance.localizationManager.AddSource(locales[i], new PanelLocaleSource(locales[i]));
                }
                log.Info("词条已注册：" + locales.Length + " 个语言 × 2 份源，当前语言 " + active);
            }
            catch (Exception ex) { log.Warn("Locale register failed: " + ex.GetType().Name); }

            // 硬约束顺序：先读盘，再绑键（Playbook §3.2 —— 反序会让玩家改的键被特性默认值顶掉）
            try { AssetDatabase.global.LoadSettings(MOD_NAME, m_Setting, new BlueprintHubSetting(this)); }
            catch (Exception ex) { log.Warn("LoadSettings failed, using defaults: " + ex.GetType().Name); }
            BlueprintHubSetting.s_PanelOpacity = m_Setting.PanelOpacity;

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
                        BlueprintHubSetting.BoundKeyText = KeyDisplay(bound);
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

            // 需求 7 的三块设置（透明度 / 快捷键 / 关于）注册进选项页。
            // FACT：Game.Settings.Setting.RegisterInOptionsUI(...) 只在 DefaultGameObjectInjectionWorld
            //   为 null 时 return false，而它对模组是 void（internal 返回值拿不到）—— 失败不打招呼。
            //   判据只能自己拿：世界没就绪就留到 UISystem.OnCreate（那时世界必然在）再补一次。
            EnsureOptionsRegistered("OnLoad");

            // 面板本体 + 绑定桥。注册阶段：FACT 游戏自己的 *UISystem 全在 SystemUpdatePhase.UIUpdate
            // （Game/Common/SystemOrder.cs:898-991）。
            try
            {
                if (World.DefaultGameObjectInjectionWorld != null)
                {
                    updateSystem.UpdateAt<BlueprintHubUISystem>(SystemUpdatePhase.UIUpdate);
                    World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<BlueprintHubUISystem>();
                    m_HasUi = true;
                }
                else log.Warn("世界未就绪：UI 系统未注册，面板不会出现。");
            }
            catch (Exception ex) { log.Error("UI 系统注册失败：" + ex.GetType().Name + " " + ex.Message); }

            HookQuit();

            log.Info("BlueprintHub v" + kVersion + " 载入完成：透明度=" +
                ((int)(BlueprintHubSetting.s_PanelOpacity * 100f)) + "%，面板快捷键=" + (s_BoundPath ?? "无"));
        }

        /// <summary>&lt;Keyboard&gt;/leftShift 这种路径换成能塞进按钮的短名。</summary>
        internal static string KeyDisplay(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int slash = path.LastIndexOf('/');
            string key = slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
            key = key.Replace("<", "").Replace(">", "");
            switch (key.ToLowerInvariant())
            {
                case "": return string.Empty;
                case "space": return "空格";
                case "enter": case "return": return "Enter";
                case "escape": return "Esc";
                case "leftshift": return "LShift";
                case "rightshift": return "RShift";
                case "leftctrl": case "rightctrl": return key.StartsWith("left") ? "LCtrl" : "RCtrl";
                case "leftalt": case "rightalt": return key.StartsWith("left") ? "LAlt" : "RAlt";
                case "tab": return "Tab";
                case "backspace": return "Backspace";
                case "leftbutton": return "鼠标左键";
                case "rightbutton": return "鼠标右键";
                case "middlebutton": return "鼠标中键";
            }
            if (key.Length == 1) return key.ToUpperInvariant();
            return char.ToUpperInvariant(key[0]) + key.Substring(1);
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

        /// <summary>选项页注册只做一次；世界未就绪时留到下一次（UISystem.OnCreate 会再调一遍）。</summary>
        internal void EnsureOptionsRegistered(string from)
        {
            if (m_OptionsRegistered) return;
            if (m_Setting == null) return;
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                log.Warn("世界未就绪，选项页留到 " + from + " 之后再补注册。");
                return;
            }
            try
            {
                m_Setting.RegisterInOptionsUI();
                m_OptionsRegistered = true;
                log.Info("选项页已注册（" + from + "）。");
            }
            catch (Exception ex) { log.Warn("RegisterInOptionsUI: " + ex.GetType().Name + " " + ex.Message); }
        }

        public static void TogglePanel()
        {
            if (Instance != null && Instance.m_HasUi)
            {
                BlueprintHubUISystem.Toggle();
                return;
            }
            PanelVisible = !PanelVisible;      // UI 系统没起来（理论上不该发生）：至少让状态自洽
            log.Warn("UI 系统未注册，快捷键只翻转了内部开关。");
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
            try { if (m_Setting != null && m_OptionsRegistered) m_Setting.UnregisterInOptionsUI(); }
            catch (Exception ex) { log.Warn("UnregisterInOptionsUI: " + ex.GetType().Name); }
            m_OptionsRegistered = false;
            m_HasUi = false;
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
