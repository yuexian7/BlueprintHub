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
        // ---- 社区类型（0.4.0 按作者给的 8 项重划：全部 + 下面 7 类）----
        // 这里只留 id 序列：**分类的中文名与定义不在 C# 里**，玩家可见文字唯一出处是 Locale.cs 的
        // CAT_<id> / DESC_<id> 词条（tests/t3 直接拿 workshop 目录的 labelZh/definitionZh 与那张表对照钉住）。
        // 作者的三条硬要求，别改回去：① 产业区不叫工业区；② 文教区不只等于教育，别改成教育区；
        // ③ 原「公共区」一分为二（公共服务区 / 交通枢纽区），「混合区」取消。
        public static readonly string[] CategoryIds =
        {
            "residential", "commercial", "industrial", "park", "education", "transit", "public_service"
        };

        /// <summary>取消混合区之后：一张蓝图只有一个主类型。多选时取第一个（采集表单是单选，这里只兜底）。</summary>
        public static string PrimaryCategory(string[] chosen)
        {
            if (chosen == null || chosen.Length == 0) return null;
            return IsKnownCategory(chosen[0]) ? chosen[0] : null;
        }

        public static bool IsKnownCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < CategoryIds.Length; i++) if (CategoryIds[i] == id) return true;
            return false;
        }

        // ---- 面积单位：u（游戏单元格面积）----
        /// <summary>
        /// 0.4.0 起面板与目录一律按 **u** 讲面积：1u = 一个可划分单元格 = 8m × 8m = 64 ㎡。
        /// 单元格边长与地图区块边长都放成常量，界面里那句换算提示由它们算出来，不写死数字。
        /// </summary>
        public const double CELL_EDGE_M = 8d;
        public const double U_AREA_M2 = CELL_EDGE_M * CELL_EDGE_M;             // 64
        /// <summary>
        /// 1 地图区块的边长：**623.3043478m**（FACT：Game.Areas.MapTileSystem.LEGACY_CELL_SIZE = 623.3043f，
        /// 全图 14336m ÷ 23 格 = 623.3043478m；MapTilePurchaseSystem.kMapTileSizeModifier = 1/623.3043478²）。
        /// 0.3.x 记的 623m / 388129㎡ 是把它取整了，少算 379.3㎡（0.098%），这里换成真值。
        /// </summary>
        public const double TILE_EDGE_M = 14336d / 23d;                        // 623.304347826087
        public const double TILE_AREA_M2 = TILE_EDGE_M * TILE_EDGE_M;          // 388508.31

        /// <summary>1 个地图区块 = 6070.44u —— 不是整数（区块边长不是 8 的倍数），所以界面里一律写「≈」。</summary>
        public static double TileInU => TILE_AREA_M2 / U_AREA_M2;

        // 作者定的三档（0.4.0）：小 <1000u、中 1000~4000u、大 >4000u
        public const long SMALL_MAX_U = 1000L;
        public const long MEDIUM_MAX_U = 4000L;

        /// <summary>㎡ → u（向下取整到整数 u；不足 1u 的显示 1u 由界面层负责，这里只算数）。</summary>
        public static long U(long areaM2)
        {
            if (areaM2 <= 0L) return 0L;
            return (long)Math.Round(areaM2 / U_AREA_M2, MidpointRounding.AwayFromZero);
        }

        public static string AreaClass(long u)
        {
            if (u < SMALL_MAX_U) return "small";
            if (u <= MEDIUM_MAX_U) return "medium";
            return "large";
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

        // ---- 排序（0.4.0 按作者的 8 项）----
        public static readonly string[] SortIds =
        {
            "weekly", "uploadTime", "createdDesc", "createdAsc", "areaDesc", "areaAsc", "nameAsc", "nameDesc"
        };

        public static string DefaultSort => "weekly";

        /// <summary>
        /// 最热门 = 7 天窗口内的加权热度（下载×3 + 点赞×1），同分落总热度再落更新时间。
        /// 最近更新 = updatedAt 新→旧；最晚/最早创建 = createdAt 两个方向；
        /// 面积两档按 ㎡ 比（u 是它的线性换算，比谁都一样）；名称 A→Z / Z→A。
        /// 名称比较用 InvariantCulture 序数（中文分序交给面板按玩家语言处理，C# 这边只保证稳定可预期）。
        /// </summary>
        public static Comparison<ListItem> MakeComparator(string sort)
        {
            switch (sort)
            {
                case "uploadTime":
                    return (a, b) =>
                    {
                        int r = CmpDt(b.UpdatedAt, a.UpdatedAt);
                        return r != 0 ? r : CmpName(a.Name, b.Name);
                    };
                case "createdDesc":
                    return (a, b) =>
                    {
                        int r = CmpDt(b.CreatedAt, a.CreatedAt);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                case "createdAsc":
                    return (a, b) =>
                    {
                        int r = CmpDt(a.CreatedAt, b.CreatedAt);
                        return r != 0 ? r : CmpDt(a.UpdatedAt, b.UpdatedAt);
                    };
                case "areaDesc":
                    return (a, b) =>
                    {
                        int r = Cmp(b.AreaM2, a.AreaM2);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                case "areaAsc":
                    return (a, b) =>
                    {
                        int r = Cmp(a.AreaM2, b.AreaM2);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                case "nameAsc":
                    return (a, b) =>
                    {
                        int r = CmpName(a.Name, b.Name);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                case "nameDesc":
                    return (a, b) =>
                    {
                        int r = CmpName(b.Name, a.Name);
                        return r != 0 ? r : CmpDt(b.UpdatedAt, a.UpdatedAt);
                    };
                default:   // weekly = 最热门，也是「搜索结果默认按相关度」时的同分兜底
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
        public string UpdatedAt;      // ISO8601：最后一次内容变更（重新上传）的时间
        public string CreatedAt;      // ISO8601：首次上传的时间（0.4.0 新增，「最晚/最早创建」两个排序用它）
        public string CoverUrl;       // 面板用的 coui:// 地址（下载完才有值）
        public string CoverRepoPath;  // 仓库相对路径 blueprints/&lt;author&gt;/&lt;bpId&gt;/&lt;file&gt;
        public long Tiles;            // 面积折算成区块数（1 区块 ≈ 6065u）
        public long U;                // 面积折算成 u（1u = 8m×8m = 64 ㎡）：面板上显示的就是这个数
        public int AssetCount;
        public string Description;
    }
}
