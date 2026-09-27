using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 蓝图工坊的纯逻辑层：分类 / 面积档 / 排序与相关度 / 镜像选择 / 失败分类 / 质心与锚点。
    /// 硬约束：这个文件里**一个游戏类型都不许出现**（Playbook 硬规则 14：想离线测某方法，它所在的类必须一个游戏类型都不碰），
    /// 所以 tests/t3 的离线回归壳可以直接把它 <Compile Include> 进去，与模组共用同一份源码，不写桩不复制。
    /// </summary>
    public static class CatalogKit
    {
        // ---- 社区类型（需求 2 的 7 类；与 workshop 的 catalog/index.json 同源，改一处要改两处并跑校验）----
        public static readonly string[] CategoryIds =
        {
            "residential", "commercial", "industrial", "park", "education", "public", "mixed"
        };

        public static readonly string[] CategoryLabelsZh =
        {
            "住宅区", "商业区", "产业区", "公园区", "文教区", "公共区", "混合区"
        };

        public static readonly string[] CategoryDefinitionsZh =
        {
            "以居民住宅为主体，低密度商业/办公混合的社区",
            "以商业街区、中大型商场或商业/办公高楼等为主体的社区",
            "以产业设施、货运设施、资源设施或仓储等为主体的社区",
            "以公园绿化、开放空间或景点建筑等为主的社区",
            "以教育建筑、科研机构等为主体的社区",
            "以客运设施、政府机构、公共服务设施等为主体的社区",
            "多种功能高度融合、无法区分主体的社区"
        };

        public const string Mixed = "mixed";

        /// <summary>需求 5：上传时勾了多个主体类型 = 没有单一主体，自动归混合区。原始勾选仍留在 meta 里。</summary>
        public static string[] IndexCategories(string[] chosen)
        {
            if (chosen == null || chosen.Length == 0) return new[] { Mixed };
            if (chosen.Length > 1) return new[] { Mixed };
            return new[] { chosen[0] };
        }

        public static bool IsKnownCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < CategoryIds.Length; i++) if (CategoryIds[i] == id) return true;
            return false;
        }

        public static string LabelZh(string id)
        {
            for (int i = 0; i < CategoryIds.Length; i++) if (CategoryIds[i] == id) return CategoryLabelsZh[i];
            return id ?? string.Empty;
        }

        /// <summary>需求 1：小标题「什么区」的定义句 —— 没选就不显示，选哪个显示哪个。</summary>
        public static string SubtitleLabel(string category, bool multiSelected)
        {
            if (multiSelected) return CategoryLabelsZh[6] + " · " + CategoryDefinitionsZh[6];
            for (int i = 0; i < CategoryIds.Length; i++)
                if (CategoryIds[i] == category) return CategoryLabelsZh[i] + " · " + CategoryDefinitionsZh[i];
            return string.Empty;
        }

        // ---- 面积档（需求 3 的「全部 / 大 / 中 / 小」）----
        /// <summary>1 区块 = 623m × 623m（ZoneSnapper 反编译 FACT 35）→ 388,129 ㎡。</summary>
        public const double TILE_EDGE_M = 623d;
        public const double TILE_AREA_M2 = TILE_EDGE_M * TILE_EDGE_M;   // 388129

        public const int SMALL_MAX_TILES = 2;
        public const int MEDIUM_MAX_TILES = 9;

        public static string AreaClass(long tiles)
        {
            if (tiles <= SMALL_MAX_TILES) return "small";
            if (tiles <= MEDIUM_MAX_TILES) return "medium";
            return "large";
        }

        public static string AreaClassZh(string areaClass)
        {
            switch (areaClass)
            {
                case "small": return "小";
                case "medium": return "中";
                case "large": return "大";
                default: return "全部";
            }
        }

        /// <summary>菜单里那句「游戏内 1 个区块面积约多少㎡」。</summary>
        public static string TileHintZh()
        {
            return "1 区块 ≈ " + TILE_AREA_M2.ToString("N0", CultureInfo.GetCultureInfo("zh-Hans")) + " ㎡";
        }

        /// <summary>面积档筛选：all 放行一切；**未知档位也当 all**（宁可多显示，也不要把列表筛成空白 —— 面板只会传四个已知值，真出现别的值说明前后端不同源，那更不该让玩家看到空列表）。</summary>
        public static bool AreaClassMatches(string itemAreaClass, string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == "all") return true;
            if (filter != "small" && filter != "medium" && filter != "large") return true;
            return itemAreaClass == filter;
        }

        // ---- 热度（需求 3：按下载量与点赞加权）----
        public const int W_DOWNLOADS = 3;
        public const int W_LIKES = 1;

        public static long HotScore(long downloads, long likes)
        {
            return downloads * W_DOWNLOADS + likes * W_LIKES;
        }

        // ---- 排序 ----
        public static readonly string[] SortIds = { "weekly", "total", "uploadTime", "area", "name" };
        public static readonly string[] SortLabelsZh = { "周热度", "总热度", "上传时间", "面积", "名称" };

        public static string DefaultSort => "weekly";

        /// <summary>
        /// 需求 3：默认按周热度（7 天内热度最高排前）。周热度相同再落总热度，再落上传时间。
        /// area: 大→小；name: 用 InvariantCulture 序数（中文排序交给面板用 localeCompare 处理，C# 这边只保证稳定）。
        /// </summary>
        public static Comparison<ListItem> MakeComparator(string sort)
        {
            switch (sort)
            {
                case "total":
                    return (a, b) =>
                    {
                        int r = Cmp(b.HotTotal, a.HotTotal);
                        if (r != 0) return r;
                        r = CmpDt(b.UpdatedAt, a.UpdatedAt);
                        return r != 0 ? r : CmpName(a.Name, b.Name);
                    };
                case "uploadTime":
                    return (a, b) =>
                    {
                        int r = CmpDt(b.UpdatedAt, a.UpdatedAt);
                        return r != 0 ? r : CmpName(a.Name, b.Name);
                    };
                case "area":
                    return (a, b) =>
                    {
                        int r = Cmp(b.AreaM2, a.AreaM2);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                case "name":
                    return (a, b) =>
                    {
                        int r = CmpName(a.Name, b.Name);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                default:   // weekly，也是「搜索结果默认按相关度」时的同分兜底
                    return (a, b) =>
                    {
                        int r = Cmp(b.HotWeekly, a.HotWeekly);
                        if (r != 0) return r;
                        r = Cmp(b.HotTotal, a.HotTotal);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
            }
        }

        /// <summary>搜索命中后按「(相关度, 周热度)」排：调用方自己带分数进来，本层不留任何静态状态。</summary>
        public static int CompareRanked(long scoreA, long weeklyA, long scoreB, long weeklyB)
        {
            int r = scoreB.CompareTo(scoreA);
            return r != 0 ? r : weeklyB.CompareTo(weeklyA);
        }

        private static int Cmp(long a, long b) => a.CompareTo(b);
        private static int CmpName(string a, string b) => string.CompareOrdinal(a ?? "", b ?? "");
        private static int CmpDt(string a, string b) => string.CompareOrdinal(a ?? "", b ?? "");   // ISO8601 可直接序数比

        // ---- 相关度（静态托管下没有服务端搜索，只能客户端匹配 + 打分）----
        /// <summary>
        /// 打分：名称完全相等 1000 &gt; 名称前缀 600 &gt; 名称含 400 &gt; 作者含 250 &gt; 描述含 100；再叠周热度做同分裁决。
        /// 返回 null = 不匹配（query 为空则人人匹配，交回周热度序）。
        /// </summary>
        public static long? Relevance(ListItem it, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = Normalize(query);
            string name = Normalize(it.Name);
            string author = Normalize(it.AuthorName);
            string desc = Normalize(it.Description);
            long score;
            if (name.Length > 0 && name == q) score = 1000;
            else if (name.Length > 0 && name.StartsWith(q, StringComparison.Ordinal)) score = 600;
            else if (name.Length > 0 && name.IndexOf(q, StringComparison.Ordinal) >= 0) score = 400;
            else if (author.Length > 0 && author.IndexOf(q, StringComparison.Ordinal) >= 0) score = 250;
            else if (desc.Length > 0 && desc.IndexOf(q, StringComparison.Ordinal) >= 0) score = 100;
            else return null;
            // 同分时周热度参与裁决，但权重远小于命中位置（÷1000 保证热度最多影响个位）
            return score + it.HotWeekly / 1000;
        }

        /// <summary>匹配用归一化：Trim + 转小写 + 折叠内部连续空白（中文不切词，直接子串）。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s.Trim())
            {
                if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; continue; }
                space = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ---- 分页与 URL ----
        public const int PAGE_SIZE = 15;      // 需求 3：默认 3 排 × 5

        public static int PageCount(int total) => total <= 0 ? 0 : (total + PAGE_SIZE - 1) / PAGE_SIZE;

        /// <summary>mirror 只能是 catalog/index.json 里 mirrors 数组的元素；空串当根路径。</summary>
        public static string BuildUrl(string mirrorBase, string relativeRepoPath)
        {
            string b = mirrorBase ?? string.Empty;
            string r = (relativeRepoPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (b.Length == 0) return r;
            return b[b.Length - 1] == '/' ? b + r : b + "/" + r;
        }

        /// <summary>
        /// 三镜像并发探一次取最快，之后固定用胜出者；失败按数组顺序降级。
        /// 顺序（workshop 的 index.json 已定）：raw → GitHub Pages → jsDelivr。
        /// 中国大陆 jsDelivr 有 DNS 污染史，所以它只当兜底而不是首选。
        /// </summary>
        public static int NextMirror(int current, int total, bool failed)
        {
            if (total <= 0) return 0;
            if (!failed) return current < 0 ? 0 : current % total;
            return (current + 1) % total;
        }

        // ---- 失败分类（Playbook 3.5：瞬时与永久必须分开，判据只允许一处）----
        /// <summary>429 / 超时 / 5xx / 网络层失败 = 瞬时，要留队退避；401/402/403/404 = 永久拒绝，一次都不许多试。</summary>
        public static bool IsPermanentStatus(int statusCode)
        {
            switch (statusCode)
            {
                case 400: case 401: case 402: case 403: case 404: case 405: case 410: return true;
                default: return false;
            }
        }

        public static int BackoffMs(int attempt)
        {
            if (attempt < 1) attempt = 1;
            long ms = 500L * attempt;
            return ms > 30000L ? 30000 : (int)ms;
        }

        // ---- 点赞 / 下载去重（需求 3：数字只可加一次）----
        /// <summary>去重键 = 玩家稳定身份 + 蓝图 ID。身份取 PlatformManager.instance.userSpecificPath 的哈希，不用显示名（可改）。</summary>
        public static string VoteKey(string playerKey, string bpId, string kind)
        {
            return (playerKey ?? "?") + "|" + (bpId ?? "?") + "|" + (kind == "likes" ? "L" : "D");
        }

        // ---- 质心与锚点（需求 9：不规则区域也要有中心点）----
        /// <summary>
        /// 多边形质心（鞋带公式）。退化情形按顺序兜底：
        /// 面积为 0（共线/两点）→ 顶点包围盒中心；无顶点 → 边界框中心。
        /// 返回 centroidIsApproximate：共线或顶点数 &lt; 3 时为 true，面板与上传日志要如实标「近似」。
        /// </summary>
        public static bool PolygonCentroid(double[] xs, double[] zs, out double cx, out double cz, out bool approximate)
        {
            cx = cz = 0d; approximate = true;
            if (xs == null || zs == null || xs.Length != zs.Length || xs.Length == 0) return false;

            int n = xs.Length;
            double area = 0d, sx = 0d, sz = 0d;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double cross = xs[i] * zs[j] - xs[j] * zs[i];
                area += cross;
                sx += (xs[i] + xs[j]) * cross;
                sz += (zs[i] + zs[j]) * cross;
            }
            area *= 0.5d;

            if (Math.Abs(area) > 1e-6d && n >= 3)
            {
                cx = sx / (6d * area);
                cz = sz / (6d * area);
                approximate = false;
                return true;
            }

            double minX = xs[0], maxX = xs[0], minZ = zs[0], maxZ = zs[0];
            for (int i = 1; i < n; i++)
            {
                if (xs[i] < minX) minX = xs[i];
                if (xs[i] > maxX) maxX = xs[i];
                if (zs[i] < minZ) minZ = zs[i];
                if (zs[i] > maxZ) maxZ = zs[i];
            }
            cx = (minX + maxX) * 0.5d;
            cz = (minZ + maxZ) * 0.5d;
            approximate = true;
            return true;
        }

        /// <summary>
        /// 需求 9：一切高度都以「中心点地面」为零点。上传时把绝对高程减去中心点高程，
        /// 套用时的目标高程 = 玩家光标处地面高 + 记录值。全程不出现海拔概念。
        /// </summary>
        public static double ToRelativeHeight(double absoluteM, double anchorGroundM) => absoluteM - anchorGroundM;
        public static double ToWorldHeight(double relativeM, double targetGroundM) => relativeM + targetGroundM;

        /// <summary>量化到 1cm 定点（int16 可表达 ±327.67m，超出走 int32）。</summary>
        public static short QuantizeCm16(double meters)
        {
            double cm = Math.Round(meters * 100d, MidpointRounding.AwayFromZero);
            if (cm > short.MaxValue) return short.MaxValue;
            if (cm < short.MinValue) return short.MinValue;
            return (short)cm;
        }

        public static double DequantizeCm16(int cm) => cm / 100d;
    }

    /// <summary>列表项（catalog 分页里的那一行；纯数据，无游戏类型）。</summary>
    public sealed class ListItem
    {
        public string Id;
        public string Name;
        public string AuthorId;
        public string AuthorName;
        public string[] Categories;
        public string AreaClass;
        public long AreaM2;
        public long Likes;
        public long Downloads;
        public long HotWeekly;
        public long HotTotal;
        public string UpdatedAt;      // ISO8601
        public string CoverUrl;       // 面板用的 coui:// 地址（下载完才有值）
        public string CoverRepoPath;  // 仓库相对路径 blueprints/&lt;author&gt;/&lt;bpId&gt;/&lt;file&gt;
        public long Tiles;            // 面积折算成区块数（1 区块 = 388,129 ㎡）
        public int AssetCount;
        public string Description;
    }
}
