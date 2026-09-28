namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 面板词条键（纯函数，tests/t3 离线钉死）。
    ///
    /// 形状不是自造的，是照抄已上架的 BusLineAutoStops 里那套被实机验证过的规则：
    ///  · 面板文案必须进游戏本地化词典（AddSource），前端用 cs2/l10n 渲染；
    ///  · BridgeTheLanguageGap 只 Hook UILocalizationManager.Translate，并且把
    ///    `Options.*` / `Common.ACTION[…]` 且方括号里的 identifier 以「模组 setting.id」开头的词条
    ///    归入「模组选项及说明」→ 于是玩家侧的十种语言由 BLG 机翻，我们只需交 zh-HANS / zh-HANT / en-US。
    ///  · identifier = BLG 取 key 里第一个 '[' 到最后一个 ']'。
    /// 前端**禁止**硬编码文案直出（TrafficToolEssentials 的面板翻不动就是这个原因）。
    /// </summary>
    public static class PanelKeyKit
    {
        /// <summary>与 ModSetting 的 setting.id 同形：程序集.命名空间.Mod 类。</summary>
        public const string SettingId = "BlueprintHub.BlueprintHub.BlueprintHubMod";

        public const string OptionsPrefix = "Options.BLUEPRINT_HUB.";

        /// <summary>面板词条主键：Options.BLUEPRINT_HUB.&lt;SLUG&gt;[setting.id.Panel.&lt;SLUG&gt;]</summary>
        public static string Key(string slug)
        {
            return OptionsPrefix + slug + "[" + SettingId + ".Panel." + slug + "]";
        }

        /// <summary>同一份文案的 Common.ACTION 形态（BLG 对这个前缀同样归类）。</summary>
        public static string ActionKey(string slug)
        {
            return "Common.ACTION[" + SettingId + ".Panel." + slug + "]";
        }

        /// <summary>BLG Scope.ExtractIdentifier：第一个 '[' 到最后一个 ']'；没有就返回 null。</summary>
        public static string ExtractIdentifier(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int a = key.IndexOf('[');
            int b = key.LastIndexOf(']');
            if (a < 0 || b <= a) return null;
            return key.Substring(a + 1, b - a - 1);
        }

        /// <summary>BLG 会不会把它当成模组词条。</summary>
        public static bool IsModKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool prefix = key.StartsWith("Options.", System.StringComparison.Ordinal)
                       || key.StartsWith("Common.ACTION[", System.StringComparison.Ordinal);
            string ident = ExtractIdentifier(key);
            return prefix && ident != null && ident.StartsWith(SettingId, System.StringComparison.Ordinal);
        }

        /// <summary>模板里的占位符（值由 C# 算好、前端按词典模板填入）。</summary>
        public static string Ph(string name)
        {
            return "{" + name + "}";
        }
    }
}
