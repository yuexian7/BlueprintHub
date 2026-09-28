using System;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using UnityEngine;

namespace BlueprintHub
{
    /// <summary>
    /// 官方设置框架（ModSetting）。Playbook §3.2 的硬约束逐条照做：
    ///  · LoadSettings 必须先于 RegisterKeyBindings（否则玩家改键被特性默认值顶掉）
    ///  · 任何 public string 选项都不得返回 null（会炸 SceneFlow）
    ///  · 热路径读 volatile 静态镜像，不读属性链
    ///  · 「没绑键的 action 不要 shouldBeEnabled=true」（否则碰一下键盘就触发）
    /// </summary>
    [FileLocation("BlueprintHub")]
    [SettingsUITabOrder(new string[] { BlueprintHubSetting.kTabMod, BlueprintHubSetting.kTabAbout })]
    [SettingsUIKeyboardAction(kActionTogglePanel)]      // 动作声明在**类**上；绑定属性各自挂 SettingsUIKeyboardBinding
    public class BlueprintHubSetting : ModSetting
    {
        public const string kTabMod = "Mod";
        public const string kTabAbout = "About";
        public const string kGroupPanel = "Panel";
        public const string kGroupApply = "Apply";
        public const string kGroupKeys = "Keys";

        public const string kActionTogglePanel = "TogglePanel";

        public const string kAboutGroup = "about";

        public static BlueprintHubSetting Instance;

        /// <summary>面板透明度热路径镜像（0~1；UI 每帧读它，不走属性链 —— Playbook 硬规则 15）。</summary>
        public static volatile float s_PanelOpacity = 0.8f;

        /// <summary>
        /// 入口按钮上显示的那个键名（「蓝图工坊  K」这种）。没设键就是空串，前端就不画那一格。
        /// 只由 Mod 入口在绑定注册/改键后写一次，不在热路径读。
        /// </summary>
        public static string BoundKeyText = string.Empty;

        private float m_PanelOpacity = 0.8f;

#nullable enable
        private ProxyBinding m_TogglePanel;
#nullable disable

        public BlueprintHubSetting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // ---------- 模组 ----------
        /// <summary>真值属性（0~1）：选项页上出现的只有下面那根滑条（一个设置出现两行是常见的抄错误）。</summary>
        public float PanelOpacity
        {
            get { return m_PanelOpacity; }
            set
            {
                float v = ClampOpacity(value);
                if (Math.Abs(v - m_PanelOpacity) < 0.0001f) return;
                m_PanelOpacity = v;
                s_PanelOpacity = v;
            }
        }

        [SettingsUISection(kTabMod, kGroupPanel)]
        // 机器证据（0.2.0 那次「透明度完全没生效」的真因）：
        //   Game.UI.Menu.AutomaticSettings 给 FloatSlider 装的 accessor 是
        //     getter:  property * scalarMultiplier   ← 交给界面显示
        //     setter:  property = uiValue / scalarMultiplier
        //   而 min / max / step 是**原样**塞给界面的（不乘 scalarMultiplier）。
        //   上一版写的是 min=0.2,max=1,step=0.05 配 scalarMultiplier=100 —— 界面看到「值 20~100、量程 0.2~1」，
        //   滑块直接顶到量程外，拖到哪都只回写 [0.002,0.01]，再被下限夹住 → 数值永远不变。
        // 现在与游戏自己的 InterfaceSettings.interfaceTransparency 逐字段一致（decompiled/Game/Settings/InterfaceSettings.cs:123）。
        [SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentage", scalarMultiplier = 100f)]
        public float PanelOpacitySlider
        {
            get { return PanelOpacity; }
            set { PanelOpacity = value; }
        }

        // 需求 7 目前只点名了两件事：透明度 + 快捷键。
        // 「下载中退出要二次确认」那类设置在 M4（真的会边下边套）之前不放上来 —— 摆了不做用的设置比没有更糟。

        // ---------- 快捷键（需求 7：默认空，玩家自己设）----------
        [SettingsUISection(kTabMod, kGroupKeys)]
        [SettingsUIKeyboardBinding(BindingKeyboard.None, kActionTogglePanel)]
        public ProxyBinding TogglePanelBinding
        {
            get { return m_TogglePanel; }
            set { m_TogglePanel = value; }
        }

        /// <summary>注册完动作后由 Mod 入口取（GetAction 是 ModSetting 的成员，这里开个公开口）。</summary>
        public ProxyAction TogglePanelAction()
        {
            try { return GetAction(kActionTogglePanel); }
            catch { return null; }
        }

        // ---------- 关于（需求 7：版本 / 作者 / 三个跳转）----------
        [SettingsUISection(kTabAbout, "")]
        public string AboutVersion { get { return BlueprintHubMod.kVersion; } }

        [SettingsUISection(kTabAbout, "")]
        public string AboutAuthor { get { return "yuexian"; } }

        [SettingsUIButton]
        [SettingsUIButtonGroup(kAboutGroup)]
        [SettingsUISection(kTabAbout, kAboutGroup)]
        public bool LinkKofi
        {
            set { TryOpenUrl(ref s_Kofi, "https://ko-fi.com/yuexian7"); }
        }

        [SettingsUIButton]
        [SettingsUIButtonGroup(kAboutGroup)]
        [SettingsUISection(kTabAbout, kAboutGroup)]
        public bool LinkForum
        {
            set { TryOpenUrl(ref s_Forum, "https://forum.paradoxplaza.com/forum/threads/blueprint-hub.1942863/"); }
        }

        [SettingsUIButton]
        [SettingsUIButtonGroup(kAboutGroup)]
        [SettingsUISection(kTabAbout, kAboutGroup)]
        public bool LinkRainbow
        {
            set { TryOpenUrl(ref s_Rainbow, "https://rainbow-series-hvpma89wi25.qoder.zone/#top"); }
        }

        private static bool s_Kofi, s_Forum, s_Rainbow;

        /// <summary>
        /// 按钮属性是 setter-only bool → 必须立刻把「按下」翻译成一次性动作。
        /// 框架会在属性 setter 里把 true 再写回 false 之前调用我们，所以这里用 ref 标志做「同帧只触发一次」。
        /// </summary>
        private static void TryOpenUrl(ref bool edge, string url)
        {
            if (edge) return;
            edge = false;                       // 属性立刻复位：按钮语义 = 一次性动作，不留 true
            // UnityEngine.CoreModule 被裁剪过，Application.DelayCall 编译期不存在（Playbook §3.1 那条坑的第 N 次复现）
            // → 框架在主线程调属性 setter，这里直接开链接即可
            try { Application.OpenURL(url); }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("OpenURL failed (" + ex.GetType().Name + "), 手动访问: " + url);
            }
        }

        public override void SetDefaults()
        {
            m_PanelOpacity = 0.8f;              // 需求 2：50% 太透，默认改 80%
            s_PanelOpacity = 0.8f;
            // 目前没有需要重置的二次确认类设置（见上面 M4 那条注释）
        }

        /// <summary>
        /// 0~1，NaN/无穷回默认值。读旧存档时顺手认一次「显示刻度」：
        /// 0.2.0 那版滑条量程算错过（见 PanelOpacitySlider 上方注释），万一盘里存的是 20~100 这种值，
        /// 直接夹会变成 100%，除以 100 才是玩家当时想调的那一档。
        /// </summary>
        private static float ClampOpacity(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.8f;
            if (v > 1.01f) v /= 100f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
