using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.UI;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using BlueprintHub.Bpc;
using BlueprintHub.Platform;
using BlueprintHub.Workshop;
using UnityEngine;
using JsonWriter = BlueprintHub.Bpc.JsonWriter;   // Colossal.UI.Binding 里也有个 JsonWriter（FACT：CS0104）

namespace BlueprintHub.Systems.UI
{
    /// <summary>
    /// 面板与 C# 的唯一桥。两条口径定死了：
    ///  · **数据口径**：C# 是唯一真值源，整块状态压成一条 GetState（JSON 字符串），前端只有一条命令通道 Cmd。
    ///  · **文案口径**：本文件不再产出任何句子，只产出「词条 slug + 数字」。句子在游戏本地化词典里
    ///    （Locale.BuildPanelMap），前端用 cs2/l10n 渲染 —— 这样 BridgeTheLanguageGap 才翻得动面板。
    ///
    /// 线程口径（机器证据，不是猜的）：
    ///  · FACT：Game.UI.UISystemBase 有 AddUpdateBinding(IUpdateBinding)，OnUpdate 在主线程逐个调用 Update()；
    ///  · FACT：GetterValueBinding&lt;T&gt;.Update() 只在「视图 active」时调 getter，值与上次不同才推给 JS。
    ///  → getter 每帧都跑，必须便宜：seq 没变就返回**同一个字符串引用**，比较器用引用相等，零拷贝。
    ///  → 后台线程一律不碰绑定，只 bump CatalogService.Seq。
    /// </summary>
    public sealed partial class BlueprintHubUISystem : UISystemBase
    {
        public const string kGroup = "BlueprintHub";
        public const string kState = "GetState";
        public const string kCmd = "Cmd";

        public static BlueprintHubUISystem Instance { get; private set; }

        private GetterValueBinding<string> m_State;
        private int m_CacheSeq = -1;
        private bool m_CacheVisible;
        private int m_CacheOpacity = -1;
        private string m_CacheJson = string.Empty;

        private string m_ToastSlug = string.Empty;
        private string m_ToastKind = "info";
        private float m_ToastUntil;
        private int m_ToastId;

        public bool Visible { get; private set; }

        // ---------------- 生命周期 ----------------

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            LocalLibrary.EnsureDirs();

            try
            {
                // 封面是仓库里的 SVG，Cohtml 不吃 http 地址：下载到 TempDir 后挂虚拟主机给 <img src> 用
                // FACT：Colossal.UI.UISystem.AddHostLocation(string hostName, string path, bool shouldWatch = true, int priority = 0)
                UIManager.defaultUISystem.AddHostLocation(LocalLibrary.CoverHost, LocalLibrary.TempDir, false);
                BlueprintHubMod.log.Info("已挂载封面虚拟主机 coui://" + LocalLibrary.CoverHost + "/ -> " + LocalLibrary.TempDir);
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("AddHostLocation: " + ex.GetType().Name + " " + ex.Message); }

            try
            {
                m_State = new GetterValueBinding<string>(kGroup, kState, GetState, null, ReferenceStringComparer.Instance);
                AddUpdateBinding(m_State);
            }
            catch (Exception ex) { BlueprintHubMod.log.Error("AddUpdateBinding(GetState): " + ex.GetType().Name + " " + ex.Message); }

            try
            {
                AddBinding(new TriggerBinding<string>(kGroup, kCmd, OnCmd));
            }
            catch (Exception ex) { BlueprintHubMod.log.Error("AddBinding(Cmd): " + ex.GetType().Name + " " + ex.Message); }

            // 世界到这里必然已就绪（本系统就是世界里的一个 System）：M0 漏掉的选项页注册在这里补上。
            try
            {
                BlueprintHubMod mod = BlueprintHubMod.Instance;
                if (mod != null) mod.EnsureOptionsRegistered("UISystem.OnCreate");
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("补注册选项页: " + ex.GetType().Name); }

            CatalogService svc = CatalogService.Ensure();
            try
            {
                // 身份在主线程解析一次并缓存：平台未就绪时它会退回设备 ID（本机去重够用，见 IdentityKit）
                svc.PlayerKey = IdentityKit.PlayerKey();
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("PlayerKey: " + ex.GetType().Name); }

            BlueprintHubMod.log.Info("UI 绑定已注册：" + kGroup + "." + kState + " / " + kGroup + "." + kCmd);
        }

        protected override void OnDestroy()
        {
            try
            {
                if (UIManager.defaultUISystem != null)
                    UIManager.defaultUISystem.RemoveHostLocation(LocalLibrary.CoverHost);
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("RemoveHostLocation: " + ex.GetType().Name); }
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ---------------- 面板开关 ----------------

        public static void Toggle() { BlueprintHubUISystem s = Instance; if (s != null) s.SetVisible(!s.Visible); }
        public static void Hide() { BlueprintHubUISystem s = Instance; if (s != null) s.SetVisible(false); }

        public void SetVisible(bool on)
        {
            if (Visible == on) return;
            Visible = on;
            BlueprintHubMod.PanelVisible = on;
            if (on) CatalogService.Ensure().Refresh(false);
            Bump();
            BlueprintHubMod.log.Info("面板 " + (on ? "打开" : "关闭"));
        }

        /// <summary>状态变了就叫一次：seq +1，主线程下一次 OnUpdate 会把新 JSON 推给前端。</summary>
        public void Bump()
        {
            BlueprintHubUISystem s = Instance;
            if (s == null) s = this;
            s.m_CacheSeq = -1;            // 强制重出 JSON（seq 由服务层提供，这里只保证不会被缓存挡住）
        }

        /// <summary>浮层提示：只交 slug，句子由前端从词典取。</summary>
        private void Toast(string slug, string kind)
        {
            m_ToastSlug = slug ?? string.Empty;
            m_ToastKind = string.IsNullOrEmpty(kind) ? "info" : kind;
            m_ToastUntil = UnityEngine.Time.unscaledTime + 3.2f;
            m_ToastId++;
            Bump();
        }

        // ---------------- 前端 → C# ----------------

        private void OnCmd(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return;
                int bar = raw.IndexOf('|');
                string kind = bar < 0 ? raw : raw.Substring(0, bar);
                string arg = bar < 0 ? string.Empty : raw.Substring(bar + 1);
                CatalogService svc = CatalogService.Ensure();

                switch (kind)
                {
                    case "open": SetVisible(true); break;
                    case "close": SetVisible(false); break;
                    case "toggle": SetVisible(!Visible); break;
                    case "cat": svc.SetCategory(arg); break;
                    case "search": svc.SetSearch(arg); break;
                    case "area": svc.SetArea(arg); break;
                    case "sort": svc.SetSort(arg); break;
                    case "page":
                        int p;
                        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out p)) svc.SetPage(p);
                        break;
                    case "retry": svc.Refresh(true); break;
                    case "like": Vote(svc, arg, "likes"); break;
                    case "dl": Vote(svc, arg, "downloads"); break;
                    case "detail": Toast("SOON_DETAIL", "info"); break;
                    case "upload": Toast("SOON_UPLOAD", "info"); break;
                    default: BlueprintHubMod.log.Warn("未知命令 " + kind); break;
                }
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("OnCmd: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void Vote(CatalogService svc, string bpId, string kind)
        {
            if (string.IsNullOrEmpty(bpId)) return;
            if (svc.TryVote(bpId, kind))
            {
                Toast(kind == "likes" ? "VOTE_LIKE_DONE" : "VOTE_USE_DONE", "ok");
                Bump();
            }
            else Toast("VOTE_DUP", "warn");
        }

        // ---------------- C# → 前端 ----------------

        private string GetState()
        {
            CatalogService svc = CatalogService.Instance;
            int seq = (svc == null ? 0 : svc.Seq) * 4 + (Visible ? 2 : 0);
            int opacity = (int)Math.Round(BlueprintHubSetting.s_PanelOpacity * 100f);
            seq += opacity;

            // 只有 toast 在倒计时这一种情况会「seq 没变但内容变了」：用 toastId 参与哈希
            if (m_ToastUntil > 0f && UnityEngine.Time.unscaledTime > m_ToastUntil)
            {
                m_ToastUntil = 0f;
                m_ToastSlug = string.Empty;
                m_CacheSeq = -1;
            }

            if (seq == m_CacheSeq && Visible == m_CacheVisible && opacity == m_CacheOpacity && m_CacheJson.Length > 0)
                return m_CacheJson;

            m_CacheSeq = seq;
            m_CacheVisible = Visible;
            m_CacheOpacity = opacity;
            try { m_CacheJson = Build(svc, opacity); }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("Build state: " + ex.GetType().Name + " " + ex.Message);
                m_CacheJson = "{\"seq\":" + seq + ",\"visible\":" + (Visible ? "true" : "false")
                    + ",\"status\":\"error\",\"statusSlug\":\"ERR_TITLE\",\"statusDetail\":\""
                    + Json.Quote(ex.GetType().Name).Trim('"') + "\"}";
            }
            return m_CacheJson;
        }

        private string Build(CatalogService svc, int opacityPercent)
        {
            JsonWriter w = new JsonWriter();
            w.BeginObj();
            w.Num("seq", m_CacheSeq);
            w.Bool("visible", Visible);
            w.Num("opacity", opacityPercent / 100d);
            w.Str("lang", LocaleTable.ActiveLocale);
            w.Bool("dev", WorkshopClient.UsingDevCatalog);
            w.Str("modVersion", BlueprintHubMod.kVersion);
            w.Str("titleSlug", "PANEL_TITLE");
            w.Str("hotkey", BlueprintHubSetting.BoundKeyText);

            bool toastLive = m_ToastUntil > 0f && UnityEngine.Time.unscaledTime <= m_ToastUntil;
            if (toastLive)
            {
                w.BeginObj("toast");
                w.Num("id", m_ToastId);
                w.Str("slug", m_ToastSlug);
                w.Str("kind", m_ToastKind);
                w.End();
            }

            if (svc == null)
            {
                w.Str("status", "loading");
                w.Str("statusSlug", "STATUS_LOADING");
                w.End();
                return w.Finish();
            }

            BrowseKit.Meta meta = svc.Meta;
            BrowseKit.Result res = svc.Last ?? new BrowseKit.Result();
            string status = svc.Status ?? "loading";
            // 查询条件把「已就绪但这一屏没结果」和「库里真没东西」分开：前者要显示清空筛选的引导
            if (status == "ready" && res.Slice.Count == 0) status = "noresult";
            w.Str("status", status);
            w.Str("statusSlug", string.IsNullOrEmpty(svc.StatusSlug) ? SlugForStatus(status) : svc.StatusSlug);
            w.Str("statusDetail", svc.StatusDetail ?? string.Empty);
            w.Bool("truncated", svc.Truncated);
            w.Num("maxItems", BrowseKit.MAX_ALL_PAGES * CatalogKit.PAGE_SIZE);
            w.Str("tileM2", BrowseKit.M2Value((long)Math.Round(CatalogKit.TILE_AREA_M2)));

            // ---- 左栏：类型（词条 slug，不发明句子）----
            w.BeginArr("categories");
            IReadOnlyList<ListItem> all = svc.Items;
            for (int i = 0; i < CatalogKit.CategoryIds.Length; i++)
            {
                string id = CatalogKit.CategoryIds[i];
                int count = 0;
                for (int j = 0; j < all.Count; j++)
                    if (BrowseKit.CategoryMatches(all[j], id)) count++;
                w.BeginObj();
                w.Str("id", id);
                w.Str("slug", "CAT_" + id);
                w.Str("descSlug", "DESC_" + id);
                w.Str("label", id);                 // 未知 id 才用得上；已知 id 前端按 slug 取词
                w.Num("count", count);
                w.Bool("selected", svc.Query.Category == id);
                w.End();
            }
            w.End();

            // ---- 菜单条 ----
            w.BeginObj("menu");
            w.Str("search", svc.Query.Search ?? string.Empty);
            w.Str("area", svc.Query.Area);
            w.Str("areaSlug", "AREA_" + svc.Query.Area);
            w.BeginArr("areas");
            WriteOption(w, "all");
            WriteOption(w, "small");
            WriteOption(w, "medium");
            WriteOption(w, "large");
            w.End();
            w.Str("sort", svc.Query.Sort);
            w.Str("sortSlug", "SORT_" + svc.Query.Sort);
            w.BeginArr("sorts");
            for (int i = 0; i < CatalogKit.SortIds.Length; i++)
                WriteOption(w, CatalogKit.SortIds[i]);
            w.End();
            w.Num("page", res.Page);
            w.Num("pages", res.Pages);
            w.Num("total", res.Total);
            w.Num("pageSize", CatalogKit.PAGE_SIZE);
            w.End();

            // ---- 卡片：一屏 4 排 × 5 ----
            w.BeginArr("items");
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < res.Slice.Count; i++) WriteItem(w, res.Slice[i], svc, now);
            w.End();

            w.End();
            return w.Finish();
        }

        /// <summary>选项：只发 id + 词条 slug，句子留给词典。</summary>
        private static void WriteOption(JsonWriter w, string id)
        {
            w.BeginObj();
            w.Str("id", id);
            w.Str("slug", OptionSlug(id));
            w.Str("label", id);            // 未知 id 时的兜底显示
            w.End();
        }

        private static string OptionSlug(string id)
        {
            switch (id)
            {
                case "all": case "small": case "medium": case "large": return "AREA_" + id;
                case "weekly": case "total": case "uploadTime": case "area": case "name": return "SORT_" + id;
                default: return string.Empty;
            }
        }

        private static string SlugForStatus(string status)
        {
            switch (status)
            {
                case "empty": return "EMPTY_TITLE";
                case "error": return "ERR_TITLE";
                case "noresult": return "NORESULT_TITLE";
                case "loading": return "STATUS_LOADING";
                default: return string.Empty;
            }
        }

        private static void WriteItem(JsonWriter w, ListItem it, CatalogService svc, DateTime now)
        {
            bool liked = svc.VoteBook.Has(it.Id, "likes", svc.PlayerKey);
            bool used = svc.VoteBook.Has(it.Id, "downloads", svc.PlayerKey);
            long likes = it.Likes + (liked ? 1 : 0);
            long downloads = it.Downloads + (used ? 1 : 0);

            string unit; int n;
            bool hasAgo = BrowseKit.AgoParts(it.UpdatedAt, now, out unit, out n);

            w.BeginObj();
            w.Str("id", it.Id);
            w.Str("name", it.Name);
            w.Str("author", it.AuthorName ?? string.Empty);      // 空 = 前端显示 ANONYMOUS
            w.Str("authorId", it.AuthorId);
            w.Str("cover", it.CoverUrl ?? string.Empty);
            w.Num("likes", likes);
            w.Num("downloads", downloads);
            w.Bool("liked", liked);
            w.Bool("used", used);
            w.Num("areaM2", it.AreaM2);
            w.Str("tiles", BrowseKit.TilesValue(it.AreaM2, CatalogKit.TILE_AREA_M2));
            w.Str("m2", BrowseKit.M2Value(it.AreaM2));
            w.Str("wan", BrowseKit.WanValue(it.AreaM2));
            w.Str("km2", BrowseKit.Km2Value(it.AreaM2));
            w.Str("areaClass", it.AreaClass);
            w.Str("areaClassSlug", "AREA_" + (string.IsNullOrEmpty(it.AreaClass) ? "all" : it.AreaClass));
            w.Str("agoUnit", hasAgo ? unit : string.Empty);
            w.Num("agoN", n);
            w.Num("assets", it.AssetCount);
            w.Str("desc", it.Description ?? string.Empty);
            w.Str("cats", string.Join(",", it.Categories ?? new string[0]));   // 前端按 id 取色与取词
            w.End();
        }

        /// <summary>引用相等比较器：getter 每帧都跑，值没换时连字符串内容都不必比。</summary>
        private sealed class ReferenceStringComparer : EqualityComparer<string>
        {
            public static readonly ReferenceStringComparer Instance = new ReferenceStringComparer();
            public override bool Equals(string a, string b) { return ReferenceEquals(a, b); }
            public override int GetHashCode(string s) { return s == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s); }
        }
    }
}
