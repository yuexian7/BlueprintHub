using System;
using System.Collections.Generic;
using System.Globalization;
using BlueprintHub.Bpc;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// catalog 三份只读文件的解析 + 一次浏览查询的纯计算。
    /// 数据来源（workshop 仓库，FACT：tools/build-catalog.mjs 的产出格式）：
    ///  · catalog/index.json  —— pageSize / categories[] / mirrors[] / sorts[] / tileAreaM2
    ///  · catalog/&lt;cat&gt;/p&lt;n&gt;.json 与 catalog/all/p&lt;n&gt;.json —— {schemaVersion,page,pages,total,sort,items[]}
    ///  · catalog/search.json —— 轻量搜索表 [[id,name,authorName,categories,areaClass,desc], ...]
    /// v0.1.0 读侧只走 all 分页（一次拿全量后本地排序/筛选/搜索），原因：静态托管没有服务端排序能力，
    /// 而「换个排序不重新请求」是需求 3 的手感底线。条目数超过 MAX_ALL_PAGES 时降级为「只显示前 N 张」并如实提示。
    /// </summary>
    public static class BrowseKit
    {
        public const int MAX_ALL_PAGES = 20;          // 20 × 15 = 300 张封顶
        public static int MaxItems => MAX_ALL_PAGES * CatalogKit.PAGE_SIZE;

        // ---------- index.json ----------
        public sealed class Meta
        {
            public int SchemaVersion;
            public int PageSize = CatalogKit.PAGE_SIZE;
            public int CatalogVersion;
            public string UpdatedAt = "";
            public readonly List<CategoryMeta> Categories = new List<CategoryMeta>();
            public readonly List<string> Mirrors = new List<string>();
            public readonly List<string> Sorts = new List<string>();
            public int HotWindowDays = 7;
            public int WDownloads = 3;
            public int WLikes = 1;
            public double TileAreaM2 = CatalogKit.TILE_AREA_M2;

            public CategoryMeta Find(string id)
            {
                for (int i = 0; i < Categories.Count; i++)
                    if (Categories[i].Id == id) return Categories[i];
                return null;
            }

            public int TotalPages(string catId)
            {
                if (string.IsNullOrEmpty(catId) || catId == "all") return AllPages;
                CategoryMeta c = Find(catId);
                return c == null ? 0 : c.Pages;
            }

            public int AllPages;
            public int Total = -1;              // index.json 的 total；-1 = 老索引没写
        }

        public sealed class CategoryMeta
        {
            public string Id = "";
            public string LabelZh = "";
            public string DefinitionZh = "";
            public int Pages;
        }

        public static Meta ParseMeta(string jsonText, out string error)
        {
            error = null;
            JsonValue root;
            if (!Json.TryParse(jsonText, out root, out error)) return null;
            if (!root.IsObject) { error = "index.json 不是对象"; return null; }

            Meta m = new Meta();
            m.SchemaVersion = root.Int("schemaVersion");
            m.CatalogVersion = root.Int("catalogVersion");
            m.PageSize = Math.Max(1, root.Int("pageSize", CatalogKit.PAGE_SIZE));
            m.UpdatedAt = root.Str("updatedAt");
            m.TileAreaM2 = root.Num("tileAreaM2", CatalogKit.TILE_AREA_M2);
            m.HotWindowDays = Math.Max(1, root.Int("hotWindowDays", 7));
            JsonValue weights = root.Obj("hotWeights");
            m.WDownloads = weights.Int("downloads", 3);
            m.WLikes = weights.Int("likes", 1);

            foreach (JsonValue c in root.Arr("categories").A())
            {
                CategoryMeta cm = new CategoryMeta();
                cm.Id = c.Str("id");
                cm.LabelZh = c.Str("labelZh");
                cm.DefinitionZh = c.Str("definitionZh");
                cm.Pages = c.Int("pages");
                if (cm.Id.Length > 0) m.Categories.Add(cm);
            }
            foreach (JsonValue s in root.Arr("mirrors").A())
            {
                string url = s.S("");
                if (url.Length > 0) m.Mirrors.Add(url);
            }
            foreach (JsonValue s in root.Arr("sorts").A())
            {
                string id = s.S("");
                if (id.Length > 0) m.Sorts.Add(id);
            }
            if (m.Sorts.Count == 0) m.Sorts.AddRange(CatalogKit.SortIds);
            // FACT：build-catalog.mjs 会往 index.json 写 allPages / total / categories[].pages。
            // 缺字段（老索引或镜像缓存）时给 -1 = 未知，由读侧退回「至少探第 1 页」。
            m.AllPages = root.Int("allPages", -1);
            m.Total = root.Int("total", -1);
            return m;
        }

        // ---------- 分页 ----------
        public static List<ListItem> ParseItems(string jsonText, out string error)
        {
            error = null;
            List<ListItem> list = new List<ListItem>();
            JsonValue root;
            if (!Json.TryParse(jsonText, out root, out error)) return null;
            foreach (JsonValue it in root.Arr("items").A()) list.Add(ParseItem(it));
            error = null;
            return list;
        }

        public static JsonValue ParsePageRoot(string jsonText)
        {
            JsonValue root;
            string err;
            if (!Json.TryParse(jsonText, out root, out err)) return null;
            return root;
        }

        /// <summary>page/pagination 字段容错：pages 缺失时按 total/pageSize 推。</summary>
        public static int PageCountOf(JsonValue pageRoot, int pageSize)
        {
            int pages = pageRoot.Int("pages");
            if (pages > 0) return pages;
            int total = pageRoot.Int("total");
            return CatalogKit.PageCount(total);
        }

        public static ListItem ParseItem(JsonValue it)
        {
            ListItem x = new ListItem();
            x.Id = it.Str("id");
            x.Name = it.Str("name");
            x.AuthorId = it.Str("author");
            x.AuthorName = it.Str("authorName");
            x.Categories = it.StrArray("categories");
            x.AreaClass = it.Str("areaClass", "small");
            x.AreaM2 = it.Long("areaM2");
            x.Tiles = it.Long("tiles");
            x.Likes = it.Long("likes");
            x.Downloads = it.Long("downloads");
            x.HotWeekly = it.Long("hotWeekly");
            x.HotTotal = it.Long("hotTotal");
            x.UpdatedAt = it.Str("updatedAt");
            x.CreatedAt = it.Str("createdAt");
            x.CoverRepoPath = it.Str("cover");
            x.AssetCount = it.Int("assetCount");
            x.Description = it.Str("desc");
            // u 优先读目录里算好的值；老目录/镜像没这个字段时按 ㎡ 现算，两边永不分叉
            long u = it.Long("u");
            x.U = u > 0L ? u : CatalogKit.U(x.AreaM2);
            return x;
        }

        /// <summary>㎡ → 区块数（四舍五入）。与 workshop 侧 build-catalog 的 tilesOf 同一口径。</summary>
        public static long Tiles(long areaM2, double tileAreaM2)
        {
            if (areaM2 <= 0L || tileAreaM2 <= 0d) return 0L;
            return (long)Math.Round(areaM2 / tileAreaM2, MidpointRounding.AwayFromZero);
        }

        // ---------- 一次查询 ----------
        public enum FilterKind { None, Area, Search }

        /// <summary>
        /// 纯函数：全量条目 + 查询条件 → 该显示的第 page 页。C# 是唯一真值源（照 Road Builder 的桥接原则），
        /// 所以这里做完全部排序、筛选、分页，前端只渲染。
        /// </summary>
        public static Result Run(IList<ListItem> all, Query q)
        {
            Result r = new Result();
            if (all == null) { r.Items = new List<ListItem>(); return r; }

            List<ListItem> pool = new List<ListItem>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ListItem it = all[i];
                if (it == null || string.IsNullOrEmpty(it.Id)) continue;
                if (!CategoryMatches(it, q.Category)) continue;
                if (!CatalogKit.AreaClassMatches(it.AreaClass, q.Area)) continue;
                pool.Add(it);
            }

            bool searching = !string.IsNullOrWhiteSpace(q.Search);
            if (searching)
            {
                // 搜索：先按相关度，命中集重新装池（需求 3「搜索结果默认按相关度排」）
                List<Scored> scored = new List<Scored>(pool.Count);
                for (int i = 0; i < pool.Count; i++)
                {
                    long? s = CatalogKit.Relevance(pool[i], q.Search);
                    if (s.HasValue) scored.Add(new Scored { Item = pool[i], Score = s.Value });
                }
                scored.Sort((a, b) => CatalogKit.CompareRanked(a.Score, a.Item.HotWeekly, b.Score, b.Item.HotWeekly));
                r.Items = new List<ListItem>(scored.Count);
                foreach (Scored s in scored) r.Items.Add(s.Item);
                r.SearchHits = scored.Count;
            }
            else
            {
                pool.Sort(CatalogKit.MakeComparator(q.Sort));
                r.Items = pool;
            }

            r.Total = r.Items.Count;
            r.Pages = CatalogKit.PageCount(r.Total);
            int page = q.Page < 1 ? 1 : q.Page;
            if (r.Pages > 0 && page > r.Pages) page = r.Pages;      // 越界夹到末页：给空白页比给个假页码更糟
            if (r.Pages == 0) page = 1;                             // 空结果集仍然报「第 1 页」，前端据此不画分页条
            int from = (page - 1) * CatalogKit.PAGE_SIZE;
            int take = Math.Min(CatalogKit.PAGE_SIZE, r.Items.Count - from);
            if (take < 0) take = 0;
            r.Page = page;
            r.Slice = new List<ListItem>(take);
            for (int i = 0; i < take; i++) r.Slice.Add(r.Items[from + i]);
            return r;
        }

        private sealed class Scored { public ListItem Item; public long Score; }

        /// <summary>分类：空 = 全部；一条蓝图可以进多个分类（上传勾多选时归混合区，但 meta 里可留多个）。</summary>
        public static bool CategoryMatches(ListItem it, string category)
        {
            if (string.IsNullOrEmpty(category) || category == "all") return true;
            string[] cats = it.Categories;
            if (cats == null) return false;
            for (int i = 0; i < cats.Length; i++) if (cats[i] == category) return true;
            return false;
        }

        public struct Query
        {
            public string Category;     // "" / all / 7 类之一
            public string Search;
            public string Area;         // all | small | medium | large
            public string Sort;         // CatalogKit.SortIds 之一
            public int Page;
        }

        public sealed class Result
        {
            public List<ListItem> Items = new List<ListItem>();     // 排好序的全量（前端不用）
            public List<ListItem> Slice = new List<ListItem>();     // 这一页要画的
            public int Total;
            public int Pages;
            public int Page = 1;
            public int SearchHits;
        }

        // ---------- 展示文案 ----------
        // 桥层往面板发的是**数据 + 词条键**，句子归游戏本地化词典（见 Locale.BuildPanelMap）。
        // 下面这几个 `*Value` 只负责「把数算成一条与语言无关的数字串」（千分位用不变文化、小数点用句点），
        // 前端把它塞进模板的 {u} / {tiles} / {tileu} 占位符。
        // 0.4.0：面积一律按 u 讲（作者要求，1u = 8m×8m），㎡ / 万㎡ / km² 那套从界面上全部撤掉，
        // 只保留 TilesValue 用来讲「一块地图区块有多大」；*Label 系列是参照实现，T3 用
        // 「同一条输入必须推出同一个数」把它们和 *Value 钉在一起。

        /// <summary>u 数（不带单位）：整数，不加千分位 —— 卡片脚注只有百来 rem 宽。</summary>
        public static string UValue(long areaM2)
        {
            return CatalogKit.U(areaM2).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>u 数带单位（参照实现，T3 拿它当判据）。</summary>
        public static string ULabel(long areaM2)
        {
            return UValue(areaM2) + "u";
        }

        /// <summary>区块数（不带单位）：整块不写小数，非整块写一位，≥100 不带小数。</summary>
        public static string TilesValue(long areaM2, double tileAreaM2)
        {
            double t = tileAreaM2 > 0 ? areaM2 / tileAreaM2 : 0d;
            if (t >= 100d) return t.ToString("F0", CultureInfo.InvariantCulture);
            if (Math.Abs(t - Math.Round(t)) < 0.05d) return ((long)Math.Round(t)).ToString(CultureInfo.InvariantCulture);
            return t.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>「1 地图区块 ≈ 6065u」里那个数：由 TILE_AREA_M2 / U_AREA_M2 现算，不写死。</summary>
        public static string TileUValue()
        {
            return CatalogKit.TileInU.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>完整面积（详情页 / 悬停用）：「1234u · 2 区块」。</summary>
        public static string AreaTextFull(long areaM2, double tileAreaM2)
        {
            return ULabel(areaM2) + " · " + TilesLabel(areaM2, tileAreaM2);
        }

        /// <summary>区块数标签：整块不写小数，非整块写一位（「0.5 区块」）。</summary>
        public static string TilesLabel(long areaM2, double tileAreaM2)
        {
            double t = tileAreaM2 > 0 ? areaM2 / tileAreaM2 : 0d;
            string v = t >= 100d ? t.ToString("F0", CultureInfo.InvariantCulture)
                : Math.Abs(t - Math.Round(t)) < 0.05d ? ((long)Math.Round(t)).ToString(CultureInfo.InvariantCulture)
                : t.ToString("0.#", CultureInfo.InvariantCulture);
            return v + " 区块";
        }

        /// <summary>
        /// 相对时间的**档位**（数据版）：unit ∈ now / min / hour / day / month / year。
        /// 判据与 <see cref="RelativeTimeZh"/> 逐条一致，T3 里两边互推；未来时间一律 now（不许出现「-3 天前」）。
        /// 解析失败返回 false —— 前端就没有时间可显示。
        /// </summary>
        public static bool AgoParts(string iso, DateTime nowUtc, out string unit, out int n)
        {
            unit = "now"; n = 0;
            DateTime t;
            if (!TryParseIso(iso, out t)) return false;
            TimeSpan d = nowUtc.ToUniversalTime() - t.ToUniversalTime();
            if (d.TotalMinutes < 0d) d = TimeSpan.Zero;
            if (d.TotalMinutes < 1d) { unit = "now"; n = 0; return true; }
            if (d.TotalHours < 1d) { unit = "min"; n = (int)d.TotalMinutes; return true; }
            if (d.TotalDays < 1d) { unit = "hour"; n = (int)d.TotalHours; return true; }
            if (d.TotalDays < 30d) { unit = "day"; n = (int)d.TotalDays; return true; }
            if (d.TotalDays < 365d) { unit = "month"; n = (int)(d.TotalDays / 30d); return true; }
            unit = "year"; n = (int)(d.TotalDays / 365d);
            return true;
        }

        /// <summary>相对时间：列表脚注放不下长日期。</summary>
        public static string RelativeTimeZh(string iso, DateTime nowUtc)
        {
            DateTime t;
            if (!TryParseIso(iso, out t)) return iso ?? string.Empty;
            TimeSpan d = nowUtc.ToUniversalTime() - t.ToUniversalTime();
            if (d.TotalMinutes < 0d) d = TimeSpan.Zero;                     // 时钟不齐：未来时间当「刚刚」
            if (d.TotalMinutes < 1d) return "刚刚";
            if (d.TotalHours < 1d) return (int)d.TotalMinutes + " 分钟前";
            if (d.TotalDays < 1d) return (int)d.TotalHours + " 小时前";
            if (d.TotalDays < 30d) return (int)d.TotalDays + " 天前";
            if (d.TotalDays < 365d) return (int)(d.TotalDays / 30d) + " 个月前";
            return (int)(d.TotalDays / 365d) + " 年前";
        }

        public static bool TryParseIso(string iso, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(iso)) return false;
            DateTime parsed;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                utc = parsed;
                return true;
            }
            return false;
        }

        /// <summary>数字节流显示：3 位数直接写，4 位起用 k（脚注宽度有限）。</summary>
        public static string Count(long n)
        {
            if (n < 10000L) return n.ToString(CultureInfo.InvariantCulture);
            if (n < 1000000L) return (n / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return (n / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }
    }
}
