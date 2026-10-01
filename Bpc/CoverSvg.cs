using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 预览图生成器：把采集结果画成一张**俯视矢量 SVG**。
    ///
    /// 为什么自己画而不是截图（与 Road Builder 同一套理由）：
    ///  · Cohtml 不吃 http 地址，图片必须先落到本地再挂 host —— 位图截图意味着整套图床/编码管线；
    ///  · 布局天生是矢量，SVG 一张几十 KB，公开仓库放得起，CDN 也传得快（三条硬约束里的「不慢」）；
    ///  · 不截图就不碰渲染管线，采集过程零帧率影响。
    /// 纯逻辑：吃 CaptureModel，吐字符串，不碰文件系统也不碰游戏类型（t3 可以直接断言它产出的结构）。
    /// </summary>
    public static class CoverSvg
    {
        public const int kSize = 512;
        public const int kMaxEdges = 4000;
        public const int kMaxBlocks = 8000;
        public const int kMaxBuildings = 4000;
        public const int kMaxObjects = 1500;      // 道具只画前若干个点，避免一张 SVG 几 MB

        /// <summary>类型 → 配色（面板的封面占位与真封面共用这一组色，别出现两套色）。</summary>
        public static string ColorFor(string category)
        {
            switch (category)
            {
                case "residential": return "#5ac8fa";
                case "commercial": return "#ff9f0a";
                case "industrial": return "#c7a2ff";
                case "park": return "#34c759";
                case "education": return "#ffd60a";
                case "transit": return "#64d2ff";
                case "public_service": return "#ff6482";
                default: return "#8e9aab";
            }
        }

        public static string Build(CaptureModel c, string category, string label)
        {
            if (c == null) throw new ArgumentNullException("c");

            double minX = c.MinX, minZ = c.MinZ;
            double w = Math.Max(1d, c.MaxX - minX);
            double h = Math.Max(1d, c.MaxZ - minZ);
            double span = Math.Max(w, h);
            double pad = kSize * 0.045;
            double scale = (kSize - pad * 2d) / span;

            StringBuilder sb = new StringBuilder(8192);
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(kSize)
              .Append("\" height=\"").Append(kSize).Append("\" viewBox=\"0 0 ")
              .Append(kSize).Append(' ').Append(kSize).Append("\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#101c28\"/>");

            Func<double, double, double> X = (x, z) => pad + (x - minX) * scale;
            Func<double, double, double> Z = (x, z) => pad + (z - minZ) * scale;

            // ---- 区域边界：填充 + 描边，一眼看出这张蓝图覆盖哪一片 ----
            int n = c.RingX == null ? 0 : c.RingX.Length;
            if (n >= 3)
            {
                sb.Append("<path d=\"").Append(Path(c, X, Z)).Append("\" fill=\"")
                  .Append(ColorFor(category)).Append("\" fill-opacity=\"0.10\" stroke=\"")
                  .Append(ColorFor(category)).Append("\" stroke-opacity=\"0.85\" stroke-width=\"2\"/>");
            }

            // ---- 功能区：每个块一个平行四边形（用 dir 把 size 展开），颜色跟类型走 ----
            sb.Append("<g fill=\"").Append(ColorFor(category)).Append("\" fill-opacity=\"0.34\">");
            int nb = Math.Min(c.Blocks.Count, kMaxBlocks);
            for (int i = 0; i < nb; i++)
            {
                CaptureZoneBlock b = c.Blocks[i];
                double halfX = b.SizeX * CatalogKit.CELL_EDGE_M * 0.5;
                double halfZ = b.SizeZ * CatalogKit.CELL_EDGE_M * 0.5;
                double dx = b.DirX, dy = b.DirY;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4) { dx = 1d; dy = 0d; }
                else { dx /= len; dy /= len; }
                // 块的局部 x 轴 = dir，局部 y 轴 = 垂直于 dir
                double ax = dx * halfX, ay = dy * halfX;
                double bx = -dy * halfZ, by = dx * halfZ;
                sb.Append("<polygon points=\"")
                  .Append(N(X(b.X + ax + bx, b.Z + ay + by))).Append(',').Append(N(Z(b.X + ax + bx, b.Z + ay + by)))
                  .Append(' ').Append(N(X(b.X + ax - bx, b.Z + ay - by))).Append(',').Append(N(Z(b.X + ax - bx, b.Z + ay - by)))
                  .Append(' ').Append(N(X(b.X - ax - bx, b.Z - ay - by))).Append(',').Append(N(Z(b.X - ax - bx, b.Z - ay - by)))
                  .Append(' ').Append(N(X(b.X - ax + bx, b.Z - ay + by))).Append(',').Append(N(Z(b.X - ax + bx, b.Z - ay + by)))
                  .Append("\"/>");
            }
            sb.Append("</g>");

            // ---- 道路：优先用贝塞尔控制点画曲线，没有就直线 ----
            sb.Append("<g stroke=\"#e9eef5\" stroke-opacity=\"0.82\" fill=\"none\" stroke-width=\"1.6\" stroke-linecap=\"round\">");
            int ne = Math.Min(c.Edges.Count, kMaxEdges);
            for (int i = 0; i < ne; i++)
            {
                CaptureEdge e = c.Edges[i];
                if (e.StartNode < 0 || e.EndNode < 0 || e.StartNode >= c.Nodes.Count || e.EndNode >= c.Nodes.Count) continue;
                CaptureNode a = c.Nodes[e.StartNode], b2 = c.Nodes[e.EndNode];
                if (e.Bezier != null && e.Bezier.Length >= 24)
                {
                    sb.Append("<path d=\"M").Append(N(X(e.Bezier[0], e.Bezier[2]))).Append(' ').Append(N(Z(e.Bezier[0], e.Bezier[2])))
                      .Append("C").Append(N(X(e.Bezier[3], e.Bezier[5]))).Append(' ').Append(N(Z(e.Bezier[3], e.Bezier[5])))
                      .Append(' ').Append(N(X(e.Bezier[6], e.Bezier[8]))).Append(' ').Append(N(Z(e.Bezier[6], e.Bezier[8])))
                      .Append(' ').Append(N(X(e.Bezier[9], e.Bezier[11]))).Append(' ').Append(N(Z(e.Bezier[9], e.Bezier[11])))
                      .Append("\"/>");
                }
                else
                {
                    sb.Append("<path d=\"M").Append(N(X(a.X, a.Z))).Append(' ').Append(N(Z(a.X, a.Z)))
                      .Append("L").Append(N(X(b2.X, b2.Z))).Append(' ').Append(N(Z(b2.X, b2.Z))).Append("\"/>");
                }
            }
            sb.Append("</g>");

            // ---- 建筑：小方块（朝向来自四元数的 yaw），道具：点 ----
            sb.Append("<g fill=\"#f2c66b\" fill-opacity=\"0.9\">");
            int no = Math.Min(c.Objects.Count, kMaxBuildings);
            int drawn = 0;
            for (int i = 0; i < no && drawn < kMaxBuildings; i++)
            {
                CaptureObject o = c.Objects[i];
                if (o.Kind != "building") continue;
                drawn++;
                double yaw = YawDeg(o.Qx, o.Qy, o.Qz, o.Qw);
                double cx = X(o.X, o.Z), cz = Z(o.X, o.Z);
                double s = Math.Max(2.2, 24d * scale / 2d);     // 建筑占地按 1 区块的 1/16 画，纯示意
                sb.Append("<rect x=\"").Append(N(cx - s)).Append("\" y=\"").Append(N(cz - s))
                  .Append("\" width=\"").Append(N(s * 2)).Append("\" height=\"").Append(N(s * 2))
                  .Append("\" transform=\"rotate(").Append(N(yaw)).Append(',').Append(N(cx)).Append(',').Append(N(cz)).Append(")\"/>");
            }
            sb.Append("</g>");

            sb.Append("<g fill=\"#9fd0ff\" fill-opacity=\"0.55\">");
            int np = Math.Min(c.Objects.Count, kMaxObjects);
            int dots = 0;
            for (int i = 0; i < np && dots < kMaxObjects; i++)
            {
                CaptureObject o = c.Objects[i];
                if (o.Kind != "prop" && o.Kind != "tree") continue;
                dots++;
                sb.Append("<circle cx=\"").Append(N(X(o.X, o.Z))).Append("\" cy=\"").Append(N(Z(o.X, o.Z)))
                  .Append("\" r=\"").Append(o.Kind == "tree" ? "1.8" : "1.2").Append("\"/>");
            }
            sb.Append("</g>");

            // ---- 角标：面积（按 u，需求 6）+ 类型色条 ----
            long u = CatalogKit.U((long)Math.Round(c.AreaM2));
            sb.Append("<rect x=\"0\" y=\"").Append(kSize - 6).Append("\" width=\"").Append(kSize)
              .Append("\" height=\"6\" fill=\"").Append(ColorFor(category)).Append("\" fill-opacity=\"0.9\"/>");
            if (!string.IsNullOrEmpty(label))
            {
                sb.Append("<text x=\"").Append(pad).Append("\" y=\"").Append(kSize - 16)
                  .Append("\" fill=\"#eaf2fb\" fill-opacity=\"0.85\" font-size=\"22\" font-family=\"sans-serif\">")
                  .Append(EscapeSvgText(label)).Append("</text>");
            }
            sb.Append("<text x=\"").Append(pad).Append("\" y=\"26").Append("\" fill=\"#eaf2fb\" fill-opacity=\"0.85\"")
              .Append(" font-size=\"20\" font-family=\"sans-serif\">")
              .Append(u.ToString("N0", CultureInfo.InvariantCulture)).Append("u</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Path(CaptureModel c, Func<double, double, double> X, Func<double, double, double> Z)
        {
            StringBuilder p = new StringBuilder(256);
            int n = c.RingX.Length;
            if (n > 1 && c.RingClosed
                && Math.Abs(c.RingX[0] - c.RingX[n - 1]) < 1e-4 && Math.Abs(c.RingZ[0] - c.RingZ[n - 1]) < 1e-4) n--;
            for (int i = 0; i < n; i++)
            {
                p.Append(i == 0 ? "M" : "L").Append(N(X(c.RingX[i], c.RingZ[i]))).Append(' ').Append(N(Z(c.RingX[i], c.RingZ[i])));
            }
            p.Append("Z");
            return p.ToString();
        }

        /// <summary>四元数 → 绕 Y 的偏航角（度）。只用 yaw：俯视图里俯仰/翻滚看不出来，别为它多存字节。</summary>
        private static double YawDeg(float x, float y, float z, float w)
        {
            double sin = 2d * (w * y - x * z);
            double cos = 1d - 2d * (y * y + x * z);
            double yaw = Math.Atan2(sin, cos) * 180d / Math.PI;
            if (double.IsNaN(yaw)) return 0d;
            return yaw;
        }

        /// <summary>坐标一律保留 1 位小数：512 见方的图，0.1px 就是精度上限，多写只会把 SVG 撑肥。</summary>
        private static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>SVG 文本转义：作者给蓝图起的名字里什么都有。</summary>
        public static string EscapeSvgText(string s)
        {
            return Esc(s ?? string.Empty);
        }

        private static string Esc(string s)
        {
            if (s.Length == 0) return s;
            StringBuilder b = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': b.Append("&amp;"); break;
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '"': b.Append("&quot;"); break;
                    case '\'': b.Append("&#39;"); break;
                    default:
                        if (ch < 32) b.Append(' ');
                        else b.Append(ch);
                        break;
                }
            }
            return b.ToString();
        }
    }
}
