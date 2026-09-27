using System.Collections.Generic;
using Colossal;
using Game.Settings;

namespace BlueprintHub
{
    /// <summary>
    /// 选项页词条（需求 7 那一页）。M5 才做 12 语言的完整词条表，这里先把
    /// 「中文玩家看得见中文、其他语言看得见英文」这一条做到，其余语言回退 en-US。
    ///
    /// 键名口径不是猜的：FACT Game.Modding.ModSetting 提供
    ///   GetSettingsLocaleID() / GetOptionTabLocaleID(tab) / GetOptionGroupLocaleID(group)
    ///   GetOptionLabelLocaleID(prop) / GetOptionDescLocaleID(prop) / GetOptionWarningLocaleID(prop)
    /// （decompiled/Game/Modding/ModSetting.cs:303-328），ToolModeMemory 的 LocaleTable 就是这么喂给
    ///   IDictionarySource 的 —— 自造键名游戏不会读。
    /// </summary>
    internal static class LocaleTable
    {
        private static readonly string[] kLocales =
        {
            "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP",
            "ko-KR", "pl-PL", "pt-BR", "ru-RU", "zh-HANS", "zh-HANT"
        };

        public static string[] Locales { get { return kLocales; } }

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            { "mod.name", "蓝图工坊" },
            { "tab.mod", "模组" },
            { "tab.about", "关于" },
            { "group.panel", "面板" },
            { "group.keys", "按键" },
            { "group.about", "链接" },

            { "opacity.label", "面板背景不透明度" },
            { "opacity.desc", "半透明便于边选蓝图边看城市。默认 50%。" },
            { "key.label", "打开 / 关闭蓝图工坊" },
            { "key.desc", "默认不设键。也可以直接点游戏左上角的「蓝图工坊」按钮。" },

            { "version.label", "模组版本" },
            { "version.desc", "与工坊数据面格式无关：格式版本写在仓库的 schemaVersion 里。" },
            { "author.label", "作者" },
            { "kofi.label", "请我喝杯咖啡" },
            { "kofi.desc", "用爱发电的另一种写法。" },
            { "forum.label", "论坛页面" },
            { "forum.desc", "反馈问题、提建议都在这里。" },
            { "rainbow.label", "RAINBOW 官网" },
            { "rainbow.desc", "打开 RAINBOW 系列作品主页。" },
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "mod.name", "BlueprintHub" },
            { "tab.mod", "Mod" },
            { "tab.about", "About" },
            { "group.panel", "Panel" },
            { "group.keys", "Keys" },
            { "group.about", "Links" },

            { "opacity.label", "Panel background opacity" },
            { "opacity.desc", "Keep the city visible while you pick a blueprint. Default 50%." },
            { "key.label", "Open / close BlueprintHub" },
            { "key.desc", "Unbound by default. The BlueprintHub button at the top left works too." },

            { "version.label", "Mod version" },
            { "version.desc", "Independent from the workshop data format (schemaVersion in the repo)." },
            { "author.label", "Author" },
            { "kofi.label", "Buy me a coffee" },
            { "kofi.desc", "Another way to say thanks." },
            { "forum.label", "Forum thread" },
            { "forum.desc", "Bugs and suggestions." },
            { "rainbow.label", "RAINBOW site" },
            { "rainbow.desc", "Open the RAINBOW series page." },
        };

        private static Dictionary<string, string> Pick(string locale)
        {
            if (!string.IsNullOrEmpty(locale) && locale.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
                return Zh;
            return En;
        }

        public static Dictionary<string, string> BuildEntries(BlueprintHubSetting setting, string locale)
        {
            Dictionary<string, string> d = Pick(locale);
            Dictionary<string, string> o = new Dictionary<string, string>();
            o[setting.GetSettingsLocaleID()] = d["mod.name"];
            o[setting.GetOptionTabLocaleID(BlueprintHubSetting.kTabMod)] = d["tab.mod"];
            o[setting.GetOptionTabLocaleID(BlueprintHubSetting.kTabAbout)] = d["tab.about"];
            o[setting.GetOptionGroupLocaleID(BlueprintHubSetting.kGroupPanel)] = d["group.panel"];
            o[setting.GetOptionGroupLocaleID(BlueprintHubSetting.kGroupKeys)] = d["group.keys"];
            o[setting.GetOptionGroupLocaleID(BlueprintHubSetting.kAboutGroup)] = d["group.about"];

            o[setting.GetOptionLabelLocaleID("PanelOpacitySlider")] = d["opacity.label"];
            o[setting.GetOptionDescLocaleID("PanelOpacitySlider")] = d["opacity.desc"];
            o[setting.GetOptionLabelLocaleID("TogglePanelBinding")] = d["key.label"];
            o[setting.GetOptionDescLocaleID("TogglePanelBinding")] = d["key.desc"];

            o[setting.GetOptionLabelLocaleID("AboutVersion")] = d["version.label"];
            o[setting.GetOptionDescLocaleID("AboutVersion")] = d["version.desc"];
            o[setting.GetOptionLabelLocaleID("AboutAuthor")] = d["author.label"];
            o[setting.GetOptionLabelLocaleID("LinkKofi")] = d["kofi.label"];
            o[setting.GetOptionDescLocaleID("LinkKofi")] = d["kofi.desc"];
            o[setting.GetOptionLabelLocaleID("LinkForum")] = d["forum.label"];
            o[setting.GetOptionDescLocaleID("LinkForum")] = d["forum.desc"];
            o[setting.GetOptionLabelLocaleID("LinkRainbow")] = d["rainbow.label"];
            o[setting.GetOptionDescLocaleID("LinkRainbow")] = d["rainbow.desc"];
            return o;
        }
    }

    /// <summary>喂给 GameManager.instance.localizationManager.AddSource(...) 的那一层。</summary>
    internal sealed class LocaleSource : IDictionarySource
    {
        private readonly Dictionary<string, string> m_Entries;

        public LocaleSource(BlueprintHubSetting setting, string locale)
        {
            m_Entries = LocaleTable.BuildEntries(setting, locale);
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return m_Entries;
        }

        public void Unload()
        {
        }
    }
}
