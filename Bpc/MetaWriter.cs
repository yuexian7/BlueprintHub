using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>一张待上传蓝图的完整草稿：表单字段 + 采集结果 + 编好的分节。</summary>
    public sealed class BlueprintDraft
    {
        public string BpId = string.Empty;
        public string AuthorId = string.Empty;
        public string AuthorName = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Category = string.Empty;
        public string DistrictName = string.Empty;
        public string GameBuild = string.Empty;
        public string GeneratorVersion = string.Empty;
        public DateTime CreatedUtc = DateTime.UtcNow;
        public DateTime UpdatedUtc;
        public CaptureModel Model = new CaptureModel();
        public SectionResult[] Sections = new SectionResult[0];

        /// <summary>预览图在仓库里的相对路径（schema 要求以 preview/ 开头）。</summary>
        public string Cover = "preview/cover.svg";

        /// <summary>没采到的东西，逐条如实记（界面上是「有 N 项没采到」，日志里是明细）。绝不静默吞。</summary>
        public readonly List<string> Missing = new List<string>();

        public int TotalBlobBytes
        {
            get
            {
                long t = 0;
                if (Sections != null)
                    for (int i = 0; i < Sections.Length; i++)
                        if (Sections[i] != null) t += Sections[i].Length;
                return t > int.MaxValue ? int.MaxValue : (int)t;
            }
        }
    }

    /// <summary>
    /// meta.json 生成器。**字段与 workshop 仓库的 schema/meta.schema.json 一一对应**（tests/t3 拿真实 schema
    /// 的 required 列表来核对这里确实写了那些键），改一边必须改另一边，否则玩家提交的蓝图会在 CI 被打回。
    /// 这里只产字符串，不碰文件系统（写盘在 Workshop/UploadService.cs）。
    /// </summary>
    public static class MetaWriter
    {
        public const int SCHEMA_VERSION = 1;

        public static string Iso(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        public static string ToJson(BlueprintDraft d)
        {
            if (d == null) throw new ArgumentNullException("d");
            CaptureModel c = d.Model ?? new CaptureModel();
            long u = CatalogKit.U((long)Math.Round(c.AreaM2));

            JsonWriter w = new JsonWriter();
            w.BeginObj();
            w.Num("schemaVersion", SCHEMA_VERSION);
            w.Str("bpId", d.BpId);
            w.Str("authorId", d.AuthorId);
            w.Str("authorName", Clamp(d.AuthorName, 48));
            w.Str("name", Clamp(d.Name, 48));
            w.Str("description", Clamp(d.Description, 2000));
            w.BeginArr("categories");
            w.Str(string.IsNullOrEmpty(d.Category) ? CatalogKit.CategoryIds[0] : d.Category);
            w.End();
            w.Str("areaClass", CatalogKit.AreaClass(u));
            w.Str("createdAt", Iso(d.CreatedUtc));
            w.Str("updatedAt", Iso(d.UpdatedUtc == default(DateTime) ? d.CreatedUtc : d.UpdatedUtc));
            w.Str("gameBuild", Clamp(d.GameBuild, 16));
            w.Str("generatorVersion", Clamp(d.GeneratorVersion, 16));
            w.Str("districtName", Clamp(d.DistrictName, 64));

            w.BeginObj("bounds");
            w.Num("widthM", Round2(c.WidthM));
            w.Num("depthM", Round2(c.DepthM));
            w.Num("areaM2", Math.Round(c.AreaM2));
            w.Num("tilesX", c.TilesX);
            w.Num("tilesY", c.TilesY);
            w.End();

            w.BeginObj("anchors");
            w.Num("centerOffsetXM", Round2(c.MinX + (c.MaxX - c.MinX) * 0.5d - c.CenterX));
            w.Num("centerOffsetZM", Round2(c.MinZ + (c.MaxZ - c.MinZ) * 0.5d - c.CenterZ));
            w.Bool("centroidIsApproximate", c.CenterApproximate);
            w.End();

            w.BeginObj("sections");
            for (int i = 0; i < BpcCodec.kSectionNames.Length; i++)
            {
                string name = BpcCodec.kSectionNames[i];
                SectionResult s = d.Sections != null && i < d.Sections.Length ? d.Sections[i] : null;
                w.BeginObj(name);
                w.Str("sha16", s == null ? string.Empty : s.Sha16);
                w.Num("bytes", s == null ? 0 : s.Length);
                w.Str("codec", s == null || s.Empty ? string.Empty : s.Codec);
                w.End();
            }
            w.End();

            w.BeginObj("counts");
            w.Num("nodes", c.Nodes.Count);
            w.Num("segments", c.Edges.Count);
            Num(c, w, "buildings", "props", "trees");
            w.Num("zones", c.Blocks.Count);
            w.End();

            w.Str("cover", d.Cover);

            w.BeginArr("assets");
            for (int i = 0; i < c.Assets.Count; i++)
            {
                CaptureAsset a = c.Assets[i];
                w.BeginObj();
                w.Str("name", Clamp(a.Name, 128));
                w.Str("prefabName", Clamp(a.PrefabName, 128));
                w.Str("kind", string.IsNullOrEmpty(a.Kind) ? "other" : a.Kind);
                if (a.ModId > 0)
                {
                    w.Num("modId", a.ModId);
                    w.Str("modName", Clamp(a.ModName, 96));
                }
                w.Bool("required", a.ModId > 0);
                w.End();
            }
            w.End();

            if (d.Missing.Count > 0)
            {
                w.BeginArr("missing");
                for (int i = 0; i < d.Missing.Count; i++) w.Str(d.Missing[i]);
                w.End();
            }
            w.End();
            return w.Finish();
        }

        /// <summary>buildings / props / trees 三个计数按资产 kind 分：一次遍历，不复制列表。</summary>
        private static void Num(CaptureModel c, JsonWriter w, string a, string b, string t)
        {
            long na = 0, nb = 0, nt = 0;
            for (int i = 0; i < c.Objects.Count; i++)
            {
                string k = c.Objects[i].Kind;
                if (k == "building") na++;
                else if (k == "tree") nt++;
                else nb++;
            }
            w.Num(a, na);
            w.Num(b, nb);
            w.Num(t, nt);
        }

        private static double Round2(double v)
        {
            return Math.Round(v, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>schema 对每个字符串都有 maxLength，超了宁可截断也别让 CI 打回整张蓝图。</summary>
        private static string Clamp(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
