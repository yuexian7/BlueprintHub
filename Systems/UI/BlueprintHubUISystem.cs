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
    /// 面板与 C# 的唯一桥。设计口径（Road Builder 机制分析 §桥接）：**C# 是数据的唯一真值源**，
    /// 前端只渲染 + 回报动作，所以整块状态压成一条绑定 GetState，前端只有一条命令通道 Cmd。
    ///
    /// 线程口径（机器证据，不是猜的）：
    ///  · FACT：Game.UI.UISystemBase 有 AddUpdateBinding(IUpdateBinding)，OnUpdate 在主线程逐个调用 Update()；
    ///  · FACT：GetterValueBinding&lt;T&gt;.Update() 只在「视图 active」时调 getter，值与上次不同才推给 JS。
    ///  → 所以 getter 每帧都会跑，必须便宜：seq 没变就返回**同一个字符串引用**，比较器用引用相等，零拷贝。
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

        private string m_Toast = string.Empty;
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

        private void Toast(string text, string kind)
        {
            m_Toast = text ?? string.Empty;
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
                    case "detail": Toast("蓝图详情与套用（需求 3/8）在 M2 里程碑开放。", "info"); break;
                    case "avatar": Toast("账号面板（需求 5）在 M3 里程碑开放。", "info"); break;
                    case "upload": Toast("上传面板（需求 4）在 M3 里程碑开放。", "info"); break;
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
                Toast(kind == "likes" ? "已点赞（本机计数）。" : "已记录一次套用（本机计数）。", "ok");
                Bump();
            }
            else Toast("这个号已经点过一次了：每张蓝图只能加一次。", "warn");
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
                m_Toast = string.Empty;
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
                    + ",\"status\":\"error\",\"statusText\":\"面板内部错误：" + Json.Quote(ex.GetType().Name).Trim('"') + "\"}";
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
            w.Str("modVersion", BlueprintHubMod.kVersion);
            w.Str("modAuthor", "yuexian");
            w.Str("title", "蓝图工坊");
            w.Str("hint", "分享蓝图请点击市辖区面板的上传按钮");
            w.Str("hotkey", BlueprintHubSetting.BoundKeyText);

            bool toastLive = m_ToastUntil > 0f && UnityEngine.Time.unscaledTime <= m_ToastUntil;
            if (toastLive)
            {
                w.BeginObj("toast");
                w.Num("id", m_ToastId);
                w.Str("text", m_Toast);
                w.Str("kind", m_ToastKind);
                w.End();
            }

            if (svc == null)
            {
                w.Str("status", "loading");
                w.Str("statusText", "正在初始化…");
                w.End();
                return w.Finish();
            }

            BrowseKit.Meta meta = svc.Meta;
            BrowseKit.Result res = svc.Last ?? new BrowseKit.Result();
            string status = svc.Status ?? "loading";
            // 查询条件把「已就绪但这一屏没结果」和「库里真没东西」分开：前者要显示清空搜索的引导
            if (status == "ready" && res.Slice.Count == 0)
            {
                status = "noresult";
            }
            w.Str("status", status);
            w.Str("statusText", svc.StatusText ?? string.Empty);
            w.Str("subtitle", Subtitle(svc, meta));
            w.Bool("truncated", svc.Truncated);
            w.Str("playerKey", PlayerKeyOf(svc));

            // ---- 左栏：社区类型（需求 2）----
            w.BeginArr("categories");
            IReadOnlyList<ListItem> all = svc.Items;
            for (int i = 0; i < CatalogKit.CategoryIds.Length; i++)
            {
                string id = CatalogKit.CategoryIds[i];
                int count = 0;
                for (int j = 0; j < all.Count; j++)
                    if (BrowseKit.CategoryMatches(all[j], id)) count++;
                BrowseKit.CategoryMeta cm = meta == null ? null : meta.Find(id);
                w.BeginObj();
                w.Str("id", id);
                w.Str("label", cm != null && cm.LabelZh.Length > 0 ? cm.LabelZh : CatalogKit.CategoryLabelsZh[i]);
                w.Str("definition", cm != null && cm.DefinitionZh.Length > 0
                    ? cm.DefinitionZh : CatalogKit.CategoryDefinitionsZh[i]);
                w.Num("count", count);
                w.Bool("selected", svc.Query.Category == id);
                w.End();
            }
            w.End();

            // ---- 菜单条（需求 3）----
            w.BeginObj("menu");
            w.Str("tileHint", CatalogKit.TileHintZh());
            w.Str("search", svc.Query.Search ?? string.Empty);
            w.Str("area", svc.Query.Area);
            w.Str("areaLabel", CatalogKit.AreaClassZh(svc.Query.Area));
            w.BeginArr("areas");
            WriteOption(w, "all", "全部");
            WriteOption(w, "small", "小 · ≤2 区块");
            WriteOption(w, "medium", "中 · 3~9 区块");
            WriteOption(w, "large", "大 · >9 区块");
            w.End();
            w.Str("sort", svc.Query.Sort);
            w.Str("sortLabel", SortLabel(svc.Query.Sort));
            w.BeginArr("sorts");
            for (int i = 0; i < CatalogKit.SortIds.Length; i++)
                WriteOption(w, CatalogKit.SortIds[i], CatalogKit.SortLabelsZh[i]);
            w.End();
            w.Num("page", res.Page);
            w.Num("pages", res.Pages);
            w.Num("total", res.Total);
            w.Num("pageSize", CatalogKit.PAGE_SIZE);
            w.End();

            // ---- 卡片（3 排 × 5）----
            w.BeginArr("items");
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < res.Slice.Count; i++) WriteItem(w, res.Slice[i], svc, now);
            w.End();

            w.End();
            return w.Finish();
        }

        private static void WriteOption(JsonWriter w, string id, string label)
        {
            w.BeginObj();
            w.Str("id", id);
            w.Str("label", label);
            w.End();
        }

        private static void WriteItem(JsonWriter w, ListItem it, CatalogService svc, DateTime now)
        {
            bool liked = svc.VoteBook.Has(it.Id, "likes", svc.PlayerKey);
            bool used = svc.VoteBook.Has(it.Id, "downloads", svc.PlayerKey);
            long likes = it.Likes + (liked ? 1 : 0);
            long downloads = it.Downloads + (used ? 1 : 0);

            w.BeginObj();
            w.Str("id", it.Id);
            w.Str("name", it.Name);
            w.Str("author", string.IsNullOrEmpty(it.AuthorName) ? "匿名玩家" : it.AuthorName);
            w.Str("authorId", it.AuthorId);
            w.Str("cover", it.CoverUrl ?? string.Empty);
            w.Num("likes", likes);
            w.Num("downloads", downloads);
            w.Str("likesText", BrowseKit.Count(likes));
            w.Str("downloadsText", BrowseKit.Count(downloads));
            w.Bool("liked", liked);
            w.Bool("used", used);
            w.Num("areaM2", it.AreaM2);
            w.Str("areaText", BrowseKit.AreaTextZh(it.AreaM2, CatalogKit.TILE_AREA_M2));
            w.Str("areaClass", it.AreaClass);
            w.Str("areaClassLabel", CatalogKit.AreaClassZh(it.AreaClass));
            w.Str("updated", BrowseKit.RelativeTimeZh(it.UpdatedAt, now));
            w.Num("assets", it.AssetCount);
            w.Str("desc", it.Description ?? string.Empty);
            w.Str("categories", JoinLabels(it.Categories));
            w.Str("cats", string.Join(",", it.Categories ?? new string[0]));   // 前端按 id 取色，中文标签不能当键
            w.End();
        }

        private static string JoinLabels(string[] cats)
        {
            if (cats == null || cats.Length == 0) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < cats.Length; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(CatalogKit.LabelZh(cats[i]));
            }
            return sb.ToString();
        }

        /// <summary>需求 1：小标题只在选中社区类型时出现，内容就是那一类的定义句。</summary>
        private static string Subtitle(CatalogService svc, BrowseKit.Meta meta)
        {
            string cat = svc.Query.Category;
            if (string.IsNullOrEmpty(cat)) return string.Empty;
            BrowseKit.CategoryMeta cm = meta == null ? null : meta.Find(cat);
            if (cm != null && cm.LabelZh.Length > 0) return cm.LabelZh + " · " + cm.DefinitionZh;
            return CatalogKit.SubtitleLabel(cat, false);
        }

        private static string SortLabel(string sort)
        {
            for (int i = 0; i < CatalogKit.SortIds.Length; i++)
                if (CatalogKit.SortIds[i] == sort) return CatalogKit.SortLabelsZh[i];
            return CatalogKit.SortLabelsZh[0];
        }

        private static string PlayerKeyOf(CatalogService svc)
        {
            // 身份只用来做本机去重键，不显示、不外传（需求 5：玩家账号不对外展示）
            return string.IsNullOrEmpty(svc.PlayerKey) ? "local" : "set";
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
