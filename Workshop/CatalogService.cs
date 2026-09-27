using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlueprintHub.Bpc;
using BlueprintHub.Platform;

namespace BlueprintHub.Workshop
{
    /// <summary>
    /// 浏览态的持有者：index.json → catalog/all 分页 → 本地筛选/排序/分页 → 给 UI 系统一份可推送的快照。
    /// 线程模型（Playbook §3.5 与硬规则 16）：
    ///  · 网络全在 ThreadPool；结果写进字段后只 bump <see cref="Seq"/>（volatile），
    ///    **绝不从后台线程碰绑定**（GetterValueBinding 由 UISystemBase.OnUpdate 在主线程拉，见 FACT）。
    ///  · 同一个刷新请求只允许在飞一次（Interlocked），换分类/翻页重复点不会堆请求。
    /// </summary>
    public sealed class CatalogService
    {
        public static CatalogService Instance { get; private set; }

        /// <summary>任何一次可见状态变化都 +1：UI 的 getter 靠它判断要不要重出 JSON。</summary>
        public int Seq { get; private set; }

        public string Status { get; private set; }        // loading | ready | empty | error
        public string StatusText { get; private set; }
        public bool Busy { get; private set; }

        public BrowseKit.Meta Meta { get; private set; }
        public List<ListItem> Items { get { lock (s_gate) return _allItems; } }
        /// <summary>当前查询条件（struct：直接改字段，别走属性 —— C# 不允许改属性返回值的成员）。</summary>
        public BrowseKit.Query Query;
        public BrowseKit.Result Last { get; private set; }
        public bool Truncated { get; private set; }        // 条目超过 MaxItems：如实告诉玩家看到的是前 N 张

        public Votes VoteBook { get; private set; }

        private static readonly object s_gate = new object();
        private List<ListItem> _allItems = new List<ListItem>();
        private int _inFlight;
        private int _reloadToken;                         // 取消上一批请求（换分类时旧页不许覆盖新页）
        private readonly HashSet<string> _coverFailed = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _lastAttemptUtc = DateTime.MinValue;

        private const string STATUS_LOADING = "loading";
        private const string STATUS_READY = "ready";
        private const string STATUS_EMPTY = "empty";
        private const string STATUS_ERROR = "error";

        private CatalogService()
        {
            Query = new BrowseKit.Query
            {
                Category = "",
                Search = "",
                Area = "all",
                Sort = CatalogKit.DefaultSort,
                Page = 1
            };
            Last = new BrowseKit.Result();
            VoteBook = Votes.Load();
        }

        public static CatalogService Ensure()
        {
            CatalogService s = Instance;
            if (s == null) s = Instance = new CatalogService();
            return s;
        }

        private void Bump() { Seq = Seq + 1; }

        // ---------------- 刷新 ----------------

        /// <summary>拉 index.json + catalog/all 全量。force=false 时，已有数据且未到最小间隔就直接返回。</summary>
        public void Refresh(bool force)
        {
            if (!force && Meta != null && Items.Count >= 0 &&
                (DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < 20d) return;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;      // 已在飞
            int token = Interlocked.Increment(ref _reloadToken);

            if (Meta == null)
            {
                Status = STATUS_LOADING;
                StatusText = "正在读取工坊索引…";
            }
            Busy = true;
            Bump();
            Run(token);
        }

        private async void Run(int token)
        {
            try
            {
                await LoadAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("CatalogService.Run: " + ex.GetType().Name + " " + ex.Message);
                if (token == Volatile.Read(ref _reloadToken)) Fail("工坊读取失败：" + ex.GetType().Name);
            }
            finally
            {
                if (token == Volatile.Read(ref _reloadToken)) { Busy = false; Bump(); }
                Volatile.Write(ref _inFlight, 0);
            }
        }

        private async Task LoadAsync(int token)
        {
            _lastAttemptUtc = DateTime.UtcNow;
            string indexJson = await WorkshopClient.GetTextAsync("catalog/index.json", CancellationToken.None)
                .ConfigureAwait(false);
            if (token != Volatile.Read(ref _reloadToken)) return;
            if (string.IsNullOrEmpty(indexJson))
            {
                Fail("连不上工坊（三个镜像都没响应）。检查网络后点重试。");
                return;
            }
            string err;
            BrowseKit.Meta meta = BrowseKit.ParseMeta(indexJson, out err);
            if (meta == null)
            {
                Fail("工坊索引格式不正确：" + err);
                return;
            }
            Meta = meta;
            if (meta.Mirrors.Count > 0) WorkshopClient.ApplyMirrors(meta.Mirrors);

            // 逐页拉全量（第 1 页到手就先让面板出东西，剩下的页在后台补）
            List<ListItem> acc = new List<ListItem>();
            int pages = Math.Max(1, meta.AllPages);
            for (int p = 1; p <= pages && p <= BrowseKit.MAX_ALL_PAGES; p++)
            {
                if (token != Volatile.Read(ref _reloadToken)) return;
                string text = await WorkshopClient
                    .GetTextAsync("catalog/all/p" + p.ToString(CultureInfo.InvariantCulture) + ".json",
                        CancellationToken.None).ConfigureAwait(false);
                if (token != Volatile.Read(ref _reloadToken)) return;
                if (string.IsNullOrEmpty(text))
                {
                    if (p == 1)
                    {
                        // 一页都没有：两种可能 —— 库里真没蓝图（CI 会删空目录），或镜像/网络坏了。
                        // 用 index.json 的 total 判定（categories 里有 pages 汇总），不猜。
                        if (TotalPagesInIndex(meta) == 0) Set(STATUS_EMPTY, "工坊还没有蓝图。", acc);
                        else Fail("读取蓝图列表失败（索引说有内容，但列表页拿不到）。");
                        return;
                    }
                    break;                                              // 后面的页缺就当截断
                }
                JsonValue root = BrowseKit.ParsePageRoot(text);
                if (root == null) { Fail("蓝图列表页解析失败。"); return; }
                foreach (JsonValue it in root.Arr("items").A()) acc.Add(BrowseKit.ParseItem(it));
                pages = Math.Max(pages, BrowseKit.PageCountOf(root, meta.PageSize));
                if (p == 1) Set(STATUS_READY, "", acc);                  // 首屏先渲染
            }
            if (token != Volatile.Read(ref _reloadToken)) return;
            Truncated = pages > BrowseKit.MAX_ALL_PAGES;
            Set(acc.Count == 0 ? STATUS_EMPTY : STATUS_READY,
                acc.Count == 0 ? "工坊还没有蓝图。" : "", acc);
        }

        private static int TotalPagesInIndex(BrowseKit.Meta meta)
        {
            int sum = 0;
            for (int i = 0; i < meta.Categories.Count; i++) sum += meta.Categories[i].Pages;
            return sum;
        }

        private void Set(string status, string text, List<ListItem> items)
        {
            lock (s_gate) _allItems = items;
            Status = status;
            StatusText = text ?? string.Empty;
            Recompute();
        }

        private void Fail(string text)
        {
            Status = STATUS_ERROR;
            StatusText = text;
            Bump();
        }

        // ---------------- 查询条件（全部由前端 trigger 驱动）----------------

        public void SetCategory(string id)
        {
            if (id == "all") id = "";
            if (!string.IsNullOrEmpty(id) && !CatalogKit.IsKnownCategory(id)) return;
            if (Query.Category == id) id = "";                          // 再点同一个小类 = 取消选择（需求 1 的小标题只在选中时显示）
            Query.Category = id;
            Query.Page = 1;
            Bump();
            Recompute();
        }

        public void SetSearch(string text)
        {
            text = text ?? string.Empty;
            if (Query.Search == text) return;
            Query.Search = text;
            Query.Page = 1;
            Bump();
            Recompute();
        }

        public void SetArea(string id)
        {
            if (string.IsNullOrEmpty(id)) id = "all";
            Query.Area = id;
            Query.Page = 1;
            Bump();
            Recompute();
        }

        public void SetSort(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            bool known = false;
            for (int i = 0; i < CatalogKit.SortIds.Length; i++) if (CatalogKit.SortIds[i] == id) known = true;
            if (!known) return;
            Query.Sort = id;
            Query.Page = 1;
            Bump();
            Recompute();
        }

        public void SetPage(int page)
        {
            if (page < 1) page = 1;
            if (Query.Page == page) return;
            Query.Page = page;
            Bump();
            Recompute();
        }

        private void Recompute()
        {
            BrowseKit.Result r = BrowseKit.Run(Items, Query);
            Last = r;
            EnsureCovers(r.Slice);
            Bump();
        }

        // ---------------- 点赞 / 下载计数（本地去重，全局聚合等写通道）----------------

        /// <summary>返回 false = 这个玩家已经点过（前端按钮保持绿色，不重复加）。</summary>
        public bool TryVote(string bpId, string kind)
        {
            if (string.IsNullOrEmpty(bpId)) return false;
            bool ok = VoteBook.Mark(bpId, kind, PlayerKey);
            if (ok)
            {
                VoteBook.SaveAsync();
                Bump();
            }
            return ok;
        }

        public string PlayerKey { get; set; }       // 由 Mod 注入（userSpecificPath 哈希；拿不到时用持久化随机 id）

        // ---------------- 封面 ----------------

        /// <summary>
        /// 封面是仓库里的 SVG，Cohtml 不能直接吃 http 地址 —— 下载到临时目录再走 coui:// 虚拟主机
        /// （Road Builder 的 thumbnails 就是这个办法）。文件名带内容哈希：同一张蓝图更新封面后 URL 变，
        /// 不会吃到视图的图片缓存。
        /// </summary>
        private void EnsureCovers(List<ListItem> slice)
        {
            if (slice == null || slice.Count == 0) return;
            for (int i = 0; i < slice.Count; i++)
            {
                ListItem it = slice[i];
                if (it == null || string.IsNullOrEmpty(it.CoverRepoPath)) continue;
                if (!string.IsNullOrEmpty(it.CoverUrl)) continue;
                if (_coverFailed.Count > 200) _coverFailed.Clear();   // 只当短期黑名单，别无限长
                if (_coverFailed.Contains(it.CoverRepoPath)) continue;
                EnsureCoverAsync(it, it.CoverRepoPath);
            }
        }

        private async void EnsureCoverAsync(ListItem it, string repoPath)
        {
            try
            {
                byte[] raw = await WorkshopClient.GetBytesAsync(repoPath, CancellationToken.None)
                    .ConfigureAwait(false);
                if (raw == null || raw.Length == 0 || raw.Length > 512 * 1024)
                {
                    if (raw != null && raw.Length > 512 * 1024) BlueprintHubMod.log.Warn("封面过大，跳过 " + repoPath);
                    else _coverFailed.Add(repoPath);
                    return;
                }
                string hash = WorkshopClient.ShortHashOf(raw);
                string ext = ExtensionOf(repoPath);
                string file = SafeName(it.Id) + "-" + hash + ext;
                string target = Path.Combine(LocalLibrary.TempDir, file);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(LocalLibrary.TempDir);
                    File.WriteAllBytes(target, raw);
                }
                it.CoverUrl = "coui://" + LocalLibrary.CoverHost + "/" + file;
                Bump();
            }
            catch (Exception ex)
            {
                _coverFailed.Add(repoPath);
                BlueprintHubMod.log.Warn("cover " + repoPath + ": " + ex.GetType().Name);
            }
        }

        private static string ExtensionOf(string path)
        {
            string e = Path.GetExtension(path ?? "");
            if (string.IsNullOrEmpty(e)) return ".svg";
            e = e.ToLowerInvariant();
            return e == ".svg" || e == ".png" || e == ".jpg" || e == ".jpeg" ? e : ".svg";
        }

        private static string SafeName(string s)
        {
            StringBuilder sb = new StringBuilder(s == null ? 4 : s.Length);
            for (int i = 0; i < (s == null ? 0 : s.Length); i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? "cover" : sb.ToString();
        }

        // ---------------- 给 UI 系统的读取口 ----------------

        public int PageCount { get { BrowseKit.Result r = Last; return r == null ? 0 : r.Pages; } }
        public int Page { get { BrowseKit.Result r = Last; return r == null ? 1 : r.Page; } }
        public int Total { get { BrowseKit.Result r = Last; return r == null ? 0 : r.Total; } }
    }

    /// <summary>
    /// 本地投票账本：state/votes.json。
    /// 为什么只有本地：写侧目前是 PR 模式（没有能让客户端直接写仓库的通道），
    /// 所以「点赞 +1 / 下载 +1」先只保证**同一玩家不重复加**，全局聚合等写通道接通（设计文档 §8）。
    /// </summary>
    public sealed class Votes
    {
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private bool _dirty;

        public static Votes Load()
        {
            Votes v = new Votes();
            try
            {
                string text = LocalLibrary.ReadText(Path.Combine(LocalLibrary.StateDir, "votes.json"));
                if (string.IsNullOrEmpty(text)) return v;
                JsonValue root;
                string err;
                if (!Json.TryParse(text, out root, out err)) return v;
                foreach (JsonValue e in root.Arr("v").A())
                {
                    string k = e.S("");
                    if (k.Length > 0) v._keys.Add(k);
                }
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("votes load: " + ex.GetType().Name); }
            return v;
        }

        public bool Has(string bpId, string kind, string playerKey)
        {
            string key = CatalogKit.VoteKey(playerKey, bpId, kind);
            lock (_keys) return _keys.Contains(key);
        }

        public bool Mark(string bpId, string kind, string playerKey)
        {
            string key = CatalogKit.VoteKey(playerKey, bpId, kind);
            lock (_keys)
            {
                if (_keys.Contains(key)) return false;
                _keys.Add(key);
                _dirty = true;
                return true;
            }
        }

        public int Count { get { lock (_keys) return _keys.Count; } }

        public void SaveIfDirty()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                LocalLibrary.EnsureDirs();
                JsonWriter w = new JsonWriter();
                w.BeginObj();
                w.Num("schemaVersion", 1);
                w.BeginArr("v");
                lock (_keys) foreach (string k in _keys) w.Str(k);
                w.End();
                w.End();
                LocalLibrary.WriteText(Path.Combine(LocalLibrary.StateDir, "votes.json"), w.Finish());
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("votes save: " + ex.GetType().Name); }
        }

        public void SaveAsync()
        {
            try { ThreadPool.QueueUserWorkItem(_ => SaveIfDirty()); }
            catch (Exception ex) { BlueprintHubMod.log.Warn("votes queue: " + ex.GetType().Name); }
        }
    }
}
