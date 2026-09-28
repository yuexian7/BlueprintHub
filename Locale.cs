using System;
using System.Collections.Generic;
using BlueprintHub.Bpc;
using Colossal;
using Game.Settings;

namespace BlueprintHub
{
    /// <summary>
    /// 全部玩家可见文案的唯一出处。两条注册路径（键名口径都有机器证据，不是自造的）：
    ///
    ///  ① <see cref="LocaleSource"/> —— 选项页。FACT：Game.Modding.ModSetting 提供
    ///     GetSettingsLocaleID / GetOptionTabLocaleID / GetOptionGroupLocaleID /
    ///     GetOptionLabelLocaleID / GetOptionDescLocaleID（decompiled/Game/Modding/ModSetting.cs:303-328）。
    ///  ② <see cref="PanelLocaleSource"/> —— 面板。键形如
    ///     Options.BLUEPRINT_HUB.&lt;SLUG&gt;[BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.&lt;SLUG&gt;]，
    ///     前端用 cs2/l10n 的 useLocalization / &lt;Localized&gt; 渲染 —— 只有走这条路，
    ///     BridgeTheLanguageGap 才能 Hook Translate 把面板翻成玩家自己的语言（与已上架的 BusLineAutoStops 同一套）。
    ///
    /// 语言范围（本版如实交代）：zh-HANS / zh-HANT / en-US 由我逐条对过官方语言包用词，
    /// 其余 9 个官方语言回退 en-US，交给玩家的 BLG 机翻。
    /// 用词对照见 research/official-terms.md（从游戏本地化导出里取的：UI透明度 / 排序方式 / 区块 / 市辖区 / 功能区 / 设施 …）。
    /// </summary>
    internal static class LocaleTable
    {
        private static readonly string[] kLocales =
        {
            "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP",
            "ko-KR", "pl-PL", "pt-BR", "ru-RU", "zh-HANS", "zh-HANT"
        };

        public static string[] Locales { get { return kLocales; } }

        private static string s_Active = "en-US";

        /// <summary>C# 侧运行时取词用（面板不再自己拼文案，但日志与降级提示还要）。禁用全局 activeLocale 的老毛病：这里只由 Mod 入口显式设一次。</summary>
        public static void SetActiveLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return;
            for (int i = 0; i < kLocales.Length; i++)
                if (string.Equals(kLocales[i], locale, StringComparison.OrdinalIgnoreCase))
                {
                    s_Active = kLocales[i];
                    return;
                }
            // 官方语言之外的 locale（例如自定义语言包）：认前缀，zh-* 走繁体/简体的决定交给上面，其余落英文
            if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) s_Active = "zh-HANS";
            else s_Active = "en-US";
        }

        public static string ActiveLocale { get { return s_Active; } }

        private static bool IsZh(string locale)
        {
            return !string.IsNullOrEmpty(locale) && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        // ==================== ① 选项页 ====================

        /// <summary>选项页词条（dot-slug）。非中英语言由 BuildEntries 合并英文兜底。</summary>
        private static Dictionary<string, string> Option(string locale)
        {
            if (IsZh(locale)) return locale.IndexOf("HANT", StringComparison.OrdinalIgnoreCase) >= 0 ? ZhTw() : Zh();
            return En();
        }

        private static Dictionary<string, string> Zh() => new Dictionary<string, string>
        {
            { "mod.name", "蓝图工坊" },
            { "tab.mod", "模组" },
            { "tab.about", "关于" },
            { "group.panel", "面板" },
            { "group.keys", "快捷键" },
            { "group.about", "链接" },

            { "opacity.label", "面板透明度" },
            { "opacity.desc", "调整面板背景的透明度。" },
            { "key.label", "打开 / 关闭蓝图工坊" },
            { "key.desc", "也可以点击游戏画面左上角的蓝图工坊按钮。" },

            { "version.label", "模组版本" },
            { "version.desc", "有新版本时会在模组页面更新。" },
            { "author.label", "作者" },
            { "kofi.label", "请我喝杯咖啡" },
            { "kofi.desc", "支持后续开发。" },
            { "forum.label", "论坛页面" },
            { "forum.desc", "反馈问题与建议。" },
            { "rainbow.label", "RAINBOW 官网" },
            { "rainbow.desc", "打开 RAINBOW 系列作品主页。" },
        };

        private static Dictionary<string, string> ZhTw() => new Dictionary<string, string>
        {
            { "mod.name", "藍圖工坊" },
            { "tab.mod", "模組" },
            { "tab.about", "關於" },
            { "group.panel", "面板" },
            { "group.keys", "快捷鍵" },
            { "group.about", "連結" },

            { "opacity.label", "面板透明度" },
            { "opacity.desc", "調整面板背景的透明度。" },
            { "key.label", "開啟 / 關閉藍圖工坊" },
            { "key.desc", "也可以點擊遊戲畫面左上角的藍圖工坊按鈕。" },

            { "version.label", "模組版本" },
            { "version.desc", "有新版本時會在模組頁面更新。" },
            { "author.label", "作者" },
            { "kofi.label", "請我喝杯咖啡" },
            { "kofi.desc", "支持後續開發。" },
            { "forum.label", "論壇頁面" },
            { "forum.desc", "回饋問題與建議。" },
            { "rainbow.label", "RAINBOW 官網" },
            { "rainbow.desc", "開啟 RAINBOW 系列作品首頁。" },
        };

        private static Dictionary<string, string> En() => new Dictionary<string, string>
        {
            { "mod.name", "Blueprint Hub" },
            { "tab.mod", "Mods" },
            { "tab.about", "About" },
            { "group.panel", "Panel" },
            { "group.keys", "Shortcuts" },
            { "group.about", "Links" },

            { "opacity.label", "Panel Transparency" },
            { "opacity.desc", "Adjusts the transparency of the panel background." },
            { "key.label", "Open or close Blueprint Hub" },
            { "key.desc", "The Blueprint Hub button at the top left works too." },

            { "version.label", "Mod Version" },
            { "version.desc", "New versions are listed on the mod page." },
            { "author.label", "Author" },
            { "kofi.label", "Buy me a coffee" },
            { "kofi.desc", "Supports future development." },
            { "forum.label", "Forum Thread" },
            { "forum.desc", "Bug reports and suggestions." },
            { "rainbow.label", "RAINBOW Site" },
            { "rainbow.desc", "Open the RAINBOW series page." },
        };

        /// <summary>
        /// 缺键一律先补英文再交出去：字典里少一个键，框架会走 KeyNotFound（已上架的兄弟模组为此专门做了合并兜底）。
        /// </summary>
        private static Dictionary<string, string> MergeEn(Dictionary<string, string> d)
        {
            Dictionary<string, string> en = En();
            Dictionary<string, string> o = new Dictionary<string, string>(en.Count + d.Count);
            foreach (KeyValuePair<string, string> kv in en) o[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, string> kv in d) o[kv.Key] = kv.Value;
            return o;
        }

        public static Dictionary<string, string> BuildEntries(BlueprintHubSetting setting, string locale)
        {
            Dictionary<string, string> d = MergeEn(Option(locale));
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

        // ==================== ② 面板 ====================

        /// <summary>面板 slug 表（中英繁三份；其余语言回退 en）。</summary>
        private static Dictionary<string, string> Panel(string locale)
        {
            if (IsZh(locale)) return locale.IndexOf("HANT", StringComparison.OrdinalIgnoreCase) >= 0 ? PanelZhTw() : PanelZh();
            return PanelEn();
        }

        private static Dictionary<string, string> PanelZh() => new Dictionary<string, string>
        {
            { "PANEL_TITLE", "蓝图工坊" },
            { "DEV_TAG", "测试数据" },

            { "TYPE_TITLE", "类型" },
            { "CAT_residential", "住宅" },
            { "CAT_commercial", "商业" },
            { "CAT_industrial", "工业" },
            { "CAT_park", "公园" },
            { "CAT_education", "教育" },
            { "CAT_public", "公共" },
            { "CAT_mixed", "混合" },
            { "DESC_residential", "以住宅建筑为主的市辖区" },
            { "DESC_commercial", "以商业街、商场或办公楼为主的市辖区" },
            { "DESC_industrial", "以工业、货运或仓储设施为主的市辖区" },
            { "DESC_park", "以公园绿地和开放空间为主的市辖区" },
            { "DESC_education", "以学校和研究机构为主的市辖区" },
            { "DESC_public", "以客运、政务和公共服务为主的市辖区" },
            { "DESC_mixed", "功能混合、没有明显主体的市辖区" },

            { "AREA_TITLE", "面积" },
            { "AREA_all", "全部" },
            { "AREA_small", "小 · ≤2 区块" },
            { "AREA_medium", "中 · 3~9 区块" },
            { "AREA_large", "大 · >9 区块" },
            { "SORT_TITLE", "排序方式" },
            { "SORT_weekly", "最近热门" },
            { "SORT_total", "历史最热" },
            { "SORT_uploadTime", "最近更新" },
            { "SORT_area", "面积" },
            { "SORT_name", "名称" },

            { "TILE_HINT", "1 区块 ≈ {tilem2} ㎡" },
            { "SEARCH_PLACEHOLDER", "搜索……" },

            { "BTN_UPLOAD", "上传蓝图" },
            { "BTN_CLOSE", "关闭" },
            { "BTN_CLEAR", "清空筛选" },
            { "BTN_RETRY", "重试" },
            { "HOTKEY_TIP", "{key} 打开 / 关闭" },
            { "SOON_UPLOAD", "上传蓝图在下个版本开放。" },
            { "SOON_DETAIL", "蓝图详情与套用在下个版本开放。" },

            { "CARD_AREA", "{tiles} 区块 · {wan}万㎡" },
            { "CARD_AREA_TINY", "{m2}㎡" },
            { "CARD_LIKE_TIP", "点赞" },
            { "CARD_USE_TIP", "套用次数" },
            { "CARD_LIKED_TIP", "本机已经点过" },
            { "CARD_USED_TIP", "本机已经记过" },
            { "ANONYMOUS", "匿名玩家" },

            { "AGO_NOW", "刚刚" },
            { "AGO_MIN", "{n} 分钟前" },
            { "AGO_HOUR", "{n} 小时前" },
            { "AGO_DAY", "{n} 天前" },
            { "AGO_MONTH", "{n} 个月前" },
            { "AGO_YEAR", "{n} 年前" },

            { "STATUS_LOADING", "正在加载蓝图……" },
            { "ERR_TITLE", "无法连接工坊" },
            { "EMPTY_TITLE", "工坊还没有蓝图" },
            { "EMPTY_HINT", "有人上传并通过校验后，蓝图就会出现在这里。" },
            { "NORESULT_TITLE", "没有符合条件的蓝图" },
            { "NORESULT_HINT", "换个关键词，或者把面积改回「全部」。" },
            { "PAGER_TOTAL", "共 {total} 张" },
            { "TRUNC_NOTE", "工坊条目较多，仅显示前 {max} 张。" },

            { "VOTE_LIKE_DONE", "已点赞（本机计数）。" },
            { "VOTE_USE_DONE", "已记录一次套用（本机计数）。" },
            { "VOTE_DUP", "同一张蓝图只能加一次。" },
        };

        private static Dictionary<string, string> PanelZhTw() => new Dictionary<string, string>
        {
            { "PANEL_TITLE", "藍圖工坊" },
            { "DEV_TAG", "測試資料" },

            { "TYPE_TITLE", "類型" },
            { "CAT_residential", "住宅" },
            { "CAT_commercial", "商業" },
            { "CAT_industrial", "工業" },
            { "CAT_park", "公園" },
            { "CAT_education", "教育" },
            { "CAT_public", "公共" },
            { "CAT_mixed", "混合" },
            { "DESC_residential", "以住宅建築為主的市轄區" },
            { "DESC_commercial", "以商業街、商場或辦公樓為主的市轄區" },
            { "DESC_industrial", "以工業、貨運或倉儲設施為主的市轄區" },
            { "DESC_park", "以公園綠地和開放空間為主的市轄區" },
            { "DESC_education", "以學校和研究機構為主的市轄區" },
            { "DESC_public", "以客運、政務和公共服務為主的市轄區" },
            { "DESC_mixed", "功能混合、沒有明顯主體的市轄區" },

            { "AREA_TITLE", "面積" },
            { "AREA_all", "全部" },
            { "AREA_small", "小 · ≤2 區塊" },
            { "AREA_medium", "中 · 3~9 區塊" },
            { "AREA_large", "大 · >9 區塊" },
            { "SORT_TITLE", "排序方式" },
            { "SORT_weekly", "最近熱門" },
            { "SORT_total", "歷史最熱" },
            { "SORT_uploadTime", "最近更新" },
            { "SORT_area", "面積" },
            { "SORT_name", "名稱" },

            { "TILE_HINT", "1 區塊 ≈ {tilem2} ㎡" },
            { "SEARCH_PLACEHOLDER", "搜尋……" },

            { "BTN_UPLOAD", "上傳藍圖" },
            { "BTN_CLOSE", "關閉" },
            { "BTN_CLEAR", "清除篩選" },
            { "BTN_RETRY", "重試" },
            { "HOTKEY_TIP", "{key} 開啟 / 關閉" },
            { "SOON_UPLOAD", "上傳藍圖在下個版本開放。" },
            { "SOON_DETAIL", "藍圖詳情與套用在下個版本開放。" },

            { "CARD_AREA", "{tiles} 區塊 · {wan}萬㎡" },
            { "CARD_AREA_TINY", "{m2}㎡" },
            { "CARD_LIKE_TIP", "點讚" },
            { "CARD_USE_TIP", "套用次數" },
            { "CARD_LIKED_TIP", "本機已經點過" },
            { "CARD_USED_TIP", "本機已經記過" },
            { "ANONYMOUS", "匿名玩家" },

            { "AGO_NOW", "剛剛" },
            { "AGO_MIN", "{n} 分鐘前" },
            { "AGO_HOUR", "{n} 小時前" },
            { "AGO_DAY", "{n} 天前" },
            { "AGO_MONTH", "{n} 個月前" },
            { "AGO_YEAR", "{n} 年前" },

            { "STATUS_LOADING", "正在載入藍圖……" },
            { "ERR_TITLE", "無法連線工坊" },
            { "EMPTY_TITLE", "工坊還沒有藍圖" },
            { "EMPTY_HINT", "有人上傳並通過驗證後，藍圖就會出現在這裡。" },
            { "NORESULT_TITLE", "沒有符合條件的藍圖" },
            { "NORESULT_HINT", "換個關鍵字，或把面積改回「全部」。" },
            { "PAGER_TOTAL", "共 {total} 張" },
            { "TRUNC_NOTE", "工坊項目較多，僅顯示前 {max} 張。" },

            { "VOTE_LIKE_DONE", "已點讚（本機計數）。" },
            { "VOTE_USE_DONE", "已記錄一次套用（本機計數）。" },
            { "VOTE_DUP", "同一張藍圖只能加一次。" },
        };

        private static Dictionary<string, string> PanelEn() => new Dictionary<string, string>
        {
            { "PANEL_TITLE", "Blueprint Hub" },
            { "DEV_TAG", "Test data" },

            { "TYPE_TITLE", "Type" },
            { "CAT_residential", "Residential" },
            { "CAT_commercial", "Commercial" },
            { "CAT_industrial", "Industrial" },
            { "CAT_park", "Park" },
            { "CAT_education", "Education" },
            { "CAT_public", "Public" },
            { "CAT_mixed", "Mixed" },
            { "DESC_residential", "Districts dominated by housing" },
            { "DESC_commercial", "Districts dominated by shops, malls or offices" },
            { "DESC_industrial", "Districts dominated by industry, freight or warehouses" },
            { "DESC_park", "Districts dominated by parks and open space" },
            { "DESC_education", "Districts dominated by schools and research" },
            { "DESC_public", "Districts dominated by transit, civic and public services" },
            { "DESC_mixed", "Mixed use with no dominant type" },

            { "AREA_TITLE", "Size" },
            { "AREA_all", "All" },
            { "AREA_small", "Small · ≤2 tiles" },
            { "AREA_medium", "Medium · 3-9 tiles" },
            { "AREA_large", "Large · >9 tiles" },
            { "SORT_TITLE", "Sort by" },
            { "SORT_weekly", "Trending" },
            { "SORT_total", "All-time popular" },
            { "SORT_uploadTime", "Recently updated" },
            { "SORT_area", "Size" },
            { "SORT_name", "Name" },

            { "TILE_HINT", "1 tile ≈ {tilem2} m²" },
            { "SEARCH_PLACEHOLDER", "Search…" },

            { "BTN_UPLOAD", "Upload blueprint" },
            { "BTN_CLOSE", "Close" },
            { "BTN_CLEAR", "Clear filters" },
            { "BTN_RETRY", "Retry" },
            { "HOTKEY_TIP", "{key} to open or close" },
            { "SOON_UPLOAD", "Blueprint upload arrives in the next version." },
            { "SOON_DETAIL", "Blueprint details and applying arrive in the next version." },

            { "CARD_AREA", "{tiles} tiles · {km2} km²" },
            { "CARD_AREA_TINY", "{m2} m²" },
            { "CARD_LIKE_TIP", "Like" },
            { "CARD_USE_TIP", "Times applied" },
            { "CARD_LIKED_TIP", "Already liked on this device" },
            { "CARD_USED_TIP", "Already counted on this device" },
            { "ANONYMOUS", "Anonymous" },

            { "AGO_NOW", "Just now" },
            { "AGO_MIN", "{n} min ago" },
            { "AGO_HOUR", "{n} h ago" },
            { "AGO_DAY", "{n} d ago" },
            { "AGO_MONTH", "{n} mo ago" },
            { "AGO_YEAR", "{n} y ago" },

            { "STATUS_LOADING", "Loading blueprints…" },
            { "ERR_TITLE", "Can't reach the workshop" },
            { "EMPTY_TITLE", "No blueprints yet" },
            { "EMPTY_HINT", "Blueprints appear here once someone uploads and they pass validation." },
            { "NORESULT_TITLE", "No matching blueprints" },
            { "NORESULT_HINT", "Try another keyword, or set Size to All." },
            { "PAGER_TOTAL", "{total} blueprints" },
            { "TRUNC_NOTE", "Large workshop: showing the first {max} only." },

            { "VOTE_LIKE_DONE", "Liked (counted on this device)." },
            { "VOTE_USE_DONE", "Apply recorded (this device)." },
            { "VOTE_DUP", "Each blueprint can only be counted once." },
        };

        /// <summary>C# 侧取一条面板文案（日志、异常降级用；正常渲染走前端 cs2/l10n）。</summary>
        public static string T(string slug)
        {
            Dictionary<string, string> d = Panel(s_Active);
            string v;
            if (d.TryGetValue(slug, out v) && !string.IsNullOrEmpty(v)) return v;
            return PanelEn().TryGetValue(slug, out v) ? v : slug;
        }

        /// <summary>
        /// 面板词条字典：主键与 Common.ACTION 形态各注册一次（BLG 两条 Hook 路径都认）。
        /// 与选项页同理，缺键先用英文补，避免 KeyNotFound。
        /// </summary>
        public static Dictionary<string, string> BuildPanelMap(string locale)
        {
            Dictionary<string, string> d = Panel(locale);
            Dictionary<string, string> en = PanelEn();
            Dictionary<string, string> map = new Dictionary<string, string>(d.Count * 2 + en.Count * 2);
            foreach (KeyValuePair<string, string> kv in en)
            {
                if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<string, string> kv in d)
            {
                map[PanelKeyKit.Key(kv.Key)] = kv.Value;
                map[PanelKeyKit.ActionKey(kv.Key)] = kv.Value;
            }
            return map;
        }

        /// <summary>面板 slug 清单（T3 用它钉「三份表键集必须一致」）。</summary>
        public static List<string> PanelSlugs(string locale)
        {
            return new List<string>(Panel(locale).Keys);
        }
    }

    /// <summary>喂给 GameManager.instance.localizationManager.AddSource(...) 的那一层：选项页。</summary>
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

        public void Unload() { }
    }

    /// <summary>同一层第二个源：面板词条。分开注册是因为设置页那一份需要 setting 实例算键名。</summary>
    internal sealed class PanelLocaleSource : IDictionarySource
    {
        private readonly Dictionary<string, string> m_Entries;

        public PanelLocaleSource(string locale)
        {
            m_Entries = LocaleTable.BuildPanelMap(locale);
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return m_Entries;
        }

        public void Unload() { }
    }
}
