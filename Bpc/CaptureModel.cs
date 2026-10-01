using System;
using System.Collections.Generic;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 一张蓝图采集下来的东西（纯数据，一个游戏类型都不碰 —— 硬规则 14，tests/t3 能离线直接构造它并跑编解码往返）。
    ///
    /// 坐标口径（需求 9：全程不出现「海拔」）：
    ///  · x/z 一律是**世界米**；写 blob 时才减区域中心点，所以这里的 Ring* 与 Center* 并存是有意的
    ///  · y 分两种：GroundY 是采集现场的地面绝对高（只用来做减法），而进 blob 的一切高度都是**相对中心点地面**的差值
    ///    （中心点处记 0，其余点是它的高差），量化 1cm —— CatalogKit.QuantizeCm16 / ToRelativeHeight
    ///  · 单元格 u：1u = 8m × 8m（FACT：Game.Zones.ZoneUtils.CELL_SIZE = 8f / CELL_AREA = 64f），功能区格的边长就是这个
    /// </summary>
    public sealed class CaptureModel
    {
        // ---- 区域本身（市辖区多边形）----
        public double[] RingX = new double[0];       // 顶点世界坐标
        public double[] RingZ = new double[0];
        public double[] RingY = new double[0];       // 顶点处地面绝对高
        public bool RingClosed;                       // 首尾是否重复了一点（写环时要去掉）

        public double CenterX;
        public double CenterZ;
        public double CenterGroundY;                  // 中心点地面绝对高 = 一切 y 的零点
        public bool CenterApproximate;                // 质心退化（共线 / 顶点不足 3）时为真

        public double MinX, MinZ, MaxX, MaxZ;         // 内容物+多边形的并集包围盒（世界米）
        public double AreaM2;                         // 多边形面积（鞋带公式）

        // ---- 地形：按 u（8m）栅格采的高差 ----
        public int TerrainNx;
        public int TerrainNy;
        public double TerrainOriginX;                 // 栅格第 0 列的世界 x
        public double TerrainOriginZ;
        public double TerrainStepM = CatalogKit.CELL_EDGE_M;
        public readonly List<short> TerrainDeltasCm = new List<short>();   // nx*ny，行主序（沿 z 逐行）

        // ---- 资产表：blob 记录里的 asset 字段就是它的下标 ----
        public readonly List<CaptureAsset> Assets = new List<CaptureAsset>();
        private readonly Dictionary<string, int> m_AssetIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        public const int MAX_ASSETS = 2000;           // 与 schema 的 assets.maxItems 同步
        public const int NOT_FOUND = -1;

        /// <summary>取（或登记）一个资产下标。键用「类型\名字」—— 与 PrefabID 的两个字符串字段一致，跨玩家稳定。</summary>
        /// <returns>下标，或 NOT_FOUND（资产表已满 —— 调用方必须把这一项计入「没采到」，不能静默塞 0）</returns>
        public int Asset(string prefabType, string prefabName, string kind, int modId, string modName)
        {
            string key = (prefabType ?? string.Empty) + "\\" + (prefabName ?? string.Empty);
            int idx;
            if (m_AssetIndex.TryGetValue(key, out idx)) return idx;
            if (Assets.Count >= MAX_ASSETS) return NOT_FOUND;
            idx = Assets.Count;
            Assets.Add(new CaptureAsset
            {
                Name = string.IsNullOrEmpty(prefabName) ? (prefabType ?? string.Empty) : prefabName,
                PrefabName = key,
                Kind = string.IsNullOrEmpty(kind) ? "other" : kind,
                ModId = modId,
                ModName = modName,
            });
            m_AssetIndex[key] = idx;
            return idx;
        }

        // ---- 路网 ----
        public readonly List<CaptureNode> Nodes = new List<CaptureNode>();
        public readonly List<CaptureEdge> Edges = new List<CaptureEdge>();

        // ---- 建筑 / 道具 / 树 ----
        public readonly List<CaptureObject> Objects = new List<CaptureObject>();
        public readonly List<CaptureUpgrade> Upgrades = new List<CaptureUpgrade>();

        // ---- 功能区 ----
        public readonly List<CaptureZoneBlock> Blocks = new List<CaptureZoneBlock>();
        public readonly List<CaptureZoneCell> Cells = new List<CaptureZoneCell>();

        // ---- 数量硬闸：超了就不上传，给玩家一句人话，别把公开仓库写爆 ----
        public const int MAX_OBJECTS = 60000;
        public const int MAX_NODES = 30000;
        public const int MAX_EDGES = 30000;
        public const int MAX_BLOCKS = 20000;
        public const int MAX_CELLS = 400000;
        public const int MAX_TERRAIN_SAMPLES = 1024 * 1024;
        public const double MAX_SIDE_M = 8000d;       // 与 schema bounds.widthM/depthM 的上限一致

        /// <summary>哪个维度爆了。what 是给日志用的英文键，界面按它取词条。</summary>
        public bool Oversize(out string what, out int count, out int limit)
        {
            what = null; count = 0; limit = 0;
            if (WidthM > MAX_SIDE_M) { what = "width"; count = (int)Math.Ceiling(WidthM); limit = (int)MAX_SIDE_M; return true; }
            if (DepthM > MAX_SIDE_M) { what = "depth"; count = (int)Math.Ceiling(DepthM); limit = (int)MAX_SIDE_M; return true; }
            if (Objects.Count > MAX_OBJECTS) { what = "objects"; count = Objects.Count; limit = MAX_OBJECTS; return true; }
            if (Nodes.Count > MAX_NODES) { what = "nodes"; count = Nodes.Count; limit = MAX_NODES; return true; }
            if (Edges.Count > MAX_EDGES) { what = "edges"; count = Edges.Count; limit = MAX_EDGES; return true; }
            if (Blocks.Count > MAX_BLOCKS) { what = "blocks"; count = Blocks.Count; limit = MAX_BLOCKS; return true; }
            if (Cells.Count > MAX_CELLS) { what = "cells"; count = Cells.Count; limit = MAX_CELLS; return true; }
            if (TerrainDeltasCm.Count > MAX_TERRAIN_SAMPLES) { what = "terrain"; count = TerrainDeltasCm.Count; limit = MAX_TERRAIN_SAMPLES; return true; }
            return false;
        }

        /// <summary>有没有采到任何东西（全空的蓝图不许上传，也别让玩家以为成功了）。</summary>
        public bool IsEmpty
        {
            get
            {
                return Objects.Count == 0 && Nodes.Count == 0 && Blocks.Count == 0 && Cells.Count == 0
                    && TerrainDeltasCm.Count == 0 && (RingX == null || RingX.Length < 3);
            }
        }

        public double WidthM { get { return Math.Max(1d, MaxX - MinX); } }
        public double DepthM { get { return Math.Max(1d, MaxZ - MinZ); } }

        /// <summary>跨多少个地图区块（向上取整，schema 的 tilesX/tilesY 最小为 1）。</summary>
        public int TilesX { get { return Math.Max(1, (int)Math.Ceiling(WidthM / CatalogKit.TILE_EDGE_M)); } }
        public int TilesY { get { return Math.Max(1, (int)Math.Ceiling(DepthM / CatalogKit.TILE_EDGE_M)); } }

        /// <summary>累计包围盒。第一点负责把框铺开（不铺开的话初值 0 会把框一路拉到原点）。</summary>
        public void Grow(double x, double z)
        {
            if (!HasBounds) { GrowFirst(x, z); return; }
            if (x < MinX) MinX = x;
            if (z < MinZ) MinZ = z;
            if (x > MaxX) MaxX = x;
            if (z > MaxZ) MaxZ = z;
        }

        /// <summary>包围盒有没有铺开过（采集层不用管，Grow 自己会初始化）。</summary>
        public bool HasBounds { get; private set; }

        public void GrowFirst(double x, double z)
        {
            MinX = MaxX = x;
            MinZ = MaxZ = z;
            HasBounds = true;
        }

        /// <summary>鞋带公式算多边形面积 + 质心（复用 CatalogKit.PolygonCentroid，退化时中心点标 approximate）。</summary>
        public void FinishPolygon()
        {
            if (RingX == null || RingX.Length < 1) return;
            double cx, cz; bool approx;
            if (!CatalogKit.PolygonCentroid(RingX, RingZ, out cx, out cz, out approx)) return;
            CenterX = cx;
            CenterZ = cz;
            CenterApproximate = approx;
            AreaM2 = Math.Abs(PolygonArea(RingX, RingZ));
            for (int i = 0; i < RingX.Length; i++) Grow(RingX[i], RingZ[i]);
        }

        private static double PolygonArea(double[] xs, double[] zs)
        {
            int n = xs.Length;
            if (n < 3) return 0d;
            double a = 0d;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += xs[i] * zs[j] - xs[j] * zs[i];
            }
            return a * 0.5d;
        }
    }

    public sealed class CaptureAsset
    {
        public string Name;
        public string PrefabName;
        public string Kind;          // net / building / prop / tree / zone / areaStyle / other
        public int ModId;            // 0 = 原版资产（人人可套）
        public string ModName;
    }

    public sealed class CaptureNode
    {
        public double X, Z;          // 世界米
        public double GroundY;       // 地面绝对高（世界米）
        public double Elevation;     // 相对地面的抬升（Game.Net.Elevation.m_Elevation.x）
        public float Qx, Qy, Qz, Qw;
        public uint Flags;
    }

    public sealed class CaptureEdge
    {
        public int StartNode;        // CaptureModel.Nodes 下标
        public int EndNode;
        public int Asset;            // Assets 下标（-1 = 没认出来，照实记进 missing）
        public uint Flags;
        public double LengthM;
        public double[] Bezier;      // 8 个控制点 ×(x,y,z) = 24 个 double，世界米；没有就 null
    }

    public sealed class CaptureObject
    {
        public double X, Z;
        public double GroundY;
        public double Elevation;
        public float Qx, Qy, Qz, Qw;
        public int Asset;
        public string Kind;          // building / prop / tree
        public byte ColorIndex;
        public byte ColorValue;
        public bool SubColor;
        public uint OptionMask;
    }

    public sealed class CaptureUpgrade
    {
        public int Object;           // Objects 下标
        public int Asset;
        public uint OptionMask;
    }

    public sealed class CaptureZoneBlock
    {
        public double X, Z;          // 块中心（世界米）
        public double GroundY;
        public double DirX, DirY;    // Game.Zones.Block.m_Direction
        public int SizeX;            // 单位 = u（单元格数，FACT：Block.m_Size 就是这个）
        public int SizeZ;
    }

    public sealed class CaptureZoneCell
    {
        public int Block;            // Blocks 下标
        public byte CX;
        public byte CZ;
        public ushort State;         // Game.Zones.CellFlags 原样存，套用端按需解释
        public short HeightCm;       // Cell.m_Height 换算成相对中心点地面的 cm
        public int Asset;            // 该格的功能区 prefab（-1 = 空格）
        public ushort ZoneIndex;     // 现场的 Game.Zones.ZoneType.m_Index：只作诊断，绝不当跨玩家键
    }
}
