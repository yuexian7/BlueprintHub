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

            // ---- 类型（0.4.0 作者重划：全部 + 7 类；措辞逐字照作者给的定义，别自作主张改「工业区」「教育区」）----
            { "TYPE_TITLE", "类型" },
            { "CAT_all", "全部" },
            { "CAT_residential", "住宅区" },
            { "CAT_commercial", "商业区" },
            { "CAT_industrial", "产业区" },
            { "CAT_park", "公园区" },
            { "CAT_education", "文教区" },
            { "CAT_transit", "交通枢纽区" },
            { "CAT_public_service", "公共服务区" },
            { "DESC_residential", "以居民住宅为主体的社区，可以混合商业/办公等" },
            { "DESC_commercial", "以商业街区、中大型商场或商业/办公高楼等为主体的社区" },
            { "DESC_industrial", "以工业设施、资源设施、科技园区或仓储等为主体的社区" },
            { "DESC_park", "以公园绿化、开放空间或景点地标等为主体的社区" },
            { "DESC_education", "以教育建筑、科研机构、校园设施等为主体的社区" },
            { "DESC_transit", "以客运设施、货运设施、大型立交或复杂路网组成的区域" },
            { "DESC_public_service", "以政府机构、公共服务设施等为主体的社区" },

            // ---- 面积档（0.4.0 起一律按 u：1u = 一个可划分单元格 = 8m×8m）----
            { "AREA_TITLE", "面积" },
            { "AREA_all", "全部" },
            { "AREA_small", "小 · <1000u" },
            { "AREA_medium", "中 · 1000~4000u" },
            { "AREA_large", "大 · >4000u" },
            { "SORT_TITLE", "排序方式" },
            { "SORT_weekly", "最热门" },
            { "SORT_uploadTime", "最近更新" },
            { "SORT_createdDesc", "最晚创建" },
            { "SORT_createdAsc", "最早创建" },
            { "SORT_areaDesc", "面积最大" },
            { "SORT_areaAsc", "面积最小" },
            { "SORT_nameAsc", "名称 A→Z" },
            { "SORT_nameDesc", "名称 Z→A" },

            { "TILE_HINT", "1u = 8m × 8m（单元格面积）　1 地图区块 ≈ {tileu}u" },
            { "SEARCH_PLACEHOLDER", "搜索……" },

            // ---- 顶栏：0.4.0 起右上角是账号按钮，上传改到市辖区面板 ----
            { "UPLOAD_HINT", "分享蓝图请进入市辖区面板上传！" },
            { "ACCOUNT_LOGIN", "登录" },
            { "ACCOUNT_PROFILE_TIP", "个人资料" },
            { "ACCOUNT_LOGIN_TIP", "登录 Paradox 账号后才能上传蓝图" },

            { "DUPLOAD_BTN", "上传蓝图" },
            { "DUPLOAD_TITLE", "上传蓝图" },
            { "DUPLOAD_NAME_LABEL", "蓝图名称" },
            { "DUPLOAD_CAT_LABEL", "社区类型" },
            { "DUPLOAD_DESC_LABEL", "简介（可留空）" },
            { "DUPLOAD_SUBMIT", "生成蓝图" },
            { "DUPLOAD_CANCEL", "取消" },
            { "DUPLOAD_NONE", "先选中一个市辖区再上传。" },
            { "DUPLOAD_BUSY", "正在采集这个市辖区……" },
            { "DUPLOAD_OK", "蓝图已生成，在草稿箱里：{path}" },
            { "DUPLOAD_PARTIAL", "蓝图已生成，但有 {n} 项没采到。" },
            { "DUPLOAD_FAIL", "生成失败：{err}" },
            { "DUPLOAD_TOO_BIG", "这个市辖区内容太多（{n}，单张上限 {max}），请拆成几个小区再上传。" },
            { "DUPLOAD_OUTBOX", "投递方法：把整个文件夹放进工坊仓库的 blueprints/ 再提交。" },
            { "DUPLOAD_COPY", "选中路径" },

            { "BTN_CLOSE", "关闭" },
            { "BTN_CLEAR", "清空筛选" },
            { "BTN_RETRY", "重试" },
            { "HOTKEY_TIP", "{key} 打开 / 关闭" },
            { "SOON_DETAIL", "蓝图详情与套用在下个版本开放。" },

            { "CARD_AREA", "{u}u · {tiles} 区块" },
            { "CARD_AREA_TINY", "不足 1u" },
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
            { "CAT_all", "全部" },
            { "CAT_residential", "住宅區" },
            { "CAT_commercial", "商業區" },
            { "CAT_industrial", "產業區" },
            { "CAT_park", "公園區" },
            { "CAT_education", "文教區" },
            { "CAT_transit", "交通樞紐區" },
            { "CAT_public_service", "公共服務區" },
            { "DESC_residential", "以居民住宅為主體的社區，可以混合商業/辦公等" },
            { "DESC_commercial", "以商業街區、中大型商場或商業/辦公高樓等為主體的社區" },
            { "DESC_industrial", "以工業設施、資源設施、科技園區或倉儲等為主體的社區" },
            { "DESC_park", "以公園綠化、開放空間或景點地標等為主體的社區" },
            { "DESC_education", "以教育建築、科研機構、校園設施等為主體的社區" },
            { "DESC_transit", "以客運設施、貨運設施、大型立交或複雜路網組成的區域" },
            { "DESC_public_service", "以政府機構、公共服務設施等為主體的社區" },

            { "AREA_TITLE", "面積" },
            { "AREA_all", "全部" },
            { "AREA_small", "小 · <1000u" },
            { "AREA_medium", "中 · 1000~4000u" },
            { "AREA_large", "大 · >4000u" },
            { "SORT_TITLE", "排序方式" },
            { "SORT_weekly", "最熱門" },
            { "SORT_uploadTime", "最近更新" },
            { "SORT_createdDesc", "最晚建立" },
            { "SORT_createdAsc", "最早建立" },
            { "SORT_areaDesc", "面積最大" },
            { "SORT_areaAsc", "面積最小" },
            { "SORT_nameAsc", "名稱 A→Z" },
            { "SORT_nameDesc", "名稱 Z→A" },

            { "TILE_HINT", "1u = 8m × 8m（單元格面積）　1 地圖區塊 ≈ {tileu}u" },
            { "SEARCH_PLACEHOLDER", "搜尋……" },

            { "UPLOAD_HINT", "分享藍圖請進入市轄區面板上傳！" },
            { "ACCOUNT_LOGIN", "登入" },
            { "ACCOUNT_PROFILE_TIP", "個人資料" },
            { "ACCOUNT_LOGIN_TIP", "登入 Paradox 帳號後才能上傳藍圖" },

            { "DUPLOAD_BTN", "上傳藍圖" },
            { "DUPLOAD_TITLE", "上傳藍圖" },
            { "DUPLOAD_NAME_LABEL", "藍圖名稱" },
            { "DUPLOAD_CAT_LABEL", "社區類型" },
            { "DUPLOAD_DESC_LABEL", "簡介（可留空）" },
            { "DUPLOAD_SUBMIT", "產生藍圖" },
            { "DUPLOAD_CANCEL", "取消" },
            { "DUPLOAD_NONE", "先選取一個市轄區再上傳。" },
            { "DUPLOAD_BUSY", "正在擷取這個市轄區……" },
            { "DUPLOAD_OK", "藍圖已產生，在草稿箱裡：{path}" },
            { "DUPLOAD_PARTIAL", "藍圖已產生，但有 {n} 項沒擷取到。" },
            { "DUPLOAD_FAIL", "產生失敗：{err}" },
            { "DUPLOAD_TOO_BIG", "這個市轄區內容太多（{n}，單張上限 {max}），請拆成幾個小區再上傳。" },
            { "DUPLOAD_OUTBOX", "投遞方法：把整個資料夾放進工坊倉庫的 blueprints/ 再提交。" },
            { "DUPLOAD_COPY", "選取路徑" },

            { "BTN_CLOSE", "關閉" },
            { "BTN_CLEAR", "清除篩選" },
            { "BTN_RETRY", "重試" },
            { "HOTKEY_TIP", "{key} 開啟 / 關閉" },
            { "SOON_DETAIL", "藍圖詳情與套用在下個版本開放。" },

            { "CARD_AREA", "{u}u · {tiles} 區塊" },
            { "CARD_AREA_TINY", "不足 1u" },
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
            { "CAT_all", "All" },
            { "CAT_residential", "Residential" },
            { "CAT_commercial", "Commercial" },
            { "CAT_industrial", "Industrial" },
            { "CAT_park", "Park" },
            { "CAT_education", "Education & Research" },
            { "CAT_transit", "Transit Hub" },
            { "CAT_public_service", "Public Services" },
            { "DESC_residential", "Neighborhoods centered on housing, which may mix in shops or offices" },
            { "DESC_commercial", "Neighborhoods centered on retail streets, mid-to-large malls or commercial and office towers" },
            { "DESC_industrial", "Neighborhoods centered on industry, resource facilities, tech parks or warehousing" },
            { "DESC_park", "Neighborhoods centered on parks and greenery, open space or landmark attractions" },
            { "DESC_education", "Neighborhoods centered on schools, research institutions or campus facilities" },
            { "DESC_transit", "Areas built around passenger or freight facilities, major interchanges or complex road networks" },
            { "DESC_public_service", "Neighborhoods centered on government offices and public service facilities" },

            { "AREA_TITLE", "Size" },
            { "AREA_all", "All" },
            { "AREA_small", "Small · <1000u" },
            { "AREA_medium", "Medium · 1000-4000u" },
            { "AREA_large", "Large · >4000u" },
            { "SORT_TITLE", "Sort by" },
            { "SORT_weekly", "Most popular" },
            { "SORT_uploadTime", "Recently updated" },
            { "SORT_createdDesc", "Newest created" },
            { "SORT_createdAsc", "Oldest created" },
            { "SORT_areaDesc", "Largest size" },
            { "SORT_areaAsc", "Smallest size" },
            { "SORT_nameAsc", "Name A→Z" },
            { "SORT_nameDesc", "Name Z→A" },

            { "TILE_HINT", "1u = one zoning cell, 8m × 8m · 1 map tile ≈ {tileu}u" },
            { "SEARCH_PLACEHOLDER", "Search…" },

            { "UPLOAD_HINT", "To share a blueprint, upload it from the district panel!" },
            { "ACCOUNT_LOGIN", "Sign in" },
            { "ACCOUNT_PROFILE_TIP", "Profile" },
            { "ACCOUNT_LOGIN_TIP", "Sign in to your Paradox account to upload blueprints" },

            { "DUPLOAD_BTN", "Upload blueprint" },
            { "DUPLOAD_TITLE", "Upload blueprint" },
            { "DUPLOAD_NAME_LABEL", "Blueprint name" },
            { "DUPLOAD_CAT_LABEL", "Community type" },
            { "DUPLOAD_DESC_LABEL", "Description (optional)" },
            { "DUPLOAD_SUBMIT", "Create blueprint" },
            { "DUPLOAD_CANCEL", "Cancel" },
            { "DUPLOAD_NONE", "Select a district first." },
            { "DUPLOAD_BUSY", "Capturing this district…" },
            { "DUPLOAD_OK", "Blueprint created, in your drafts: {path}" },
            { "DUPLOAD_PARTIAL", "Blueprint created, {n} item(s) could not be captured." },
            { "DUPLOAD_FAIL", "Failed to create: {err}" },
            { "DUPLOAD_TOO_BIG", "This district has too much content ({n}, limit {max} per blueprint). Split it into smaller districts." },
            { "DUPLOAD_OUTBOX", "To submit: drop the whole folder into blueprints/ in the workshop repo and commit." },
            { "DUPLOAD_COPY", "Select path" },

            { "BTN_CLOSE", "Close" },
            { "BTN_CLEAR", "Clear filters" },
            { "BTN_RETRY", "Retry" },
            { "HOTKEY_TIP", "{key} to open or close" },
            { "SOON_DETAIL", "Blueprint details and applying arrive in the next version." },

            { "CARD_AREA", "{u}u · {tiles} tiles" },
            { "CARD_AREA_TINY", "Under 1u" },
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
