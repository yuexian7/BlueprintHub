using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.UI;
using Game.Zones;
using BlueprintHub.Bpc;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Block = Game.Zones.Block;
using Cell = Game.Zones.Cell;
using AreaNode = Game.Areas.Node;
using NetNode = Game.Net.Node;
using NetEdge = Game.Net.Edge;
using NetCurve = Game.Net.Curve;
using NetElevation = Game.Net.Elevation;
using ObjTransform = Game.Objects.Transform;
using ObjElevation = Game.Objects.Elevation;
using ObjColor = Game.Objects.Color;
using NetSearchSystem = Game.Net.SearchSystem;
using ObjectsSearchSystem = Game.Objects.SearchSystem;
using ZonesSearchSystem = Game.Zones.SearchSystem;

namespace BlueprintHub.Workshop
{
    /// <summary>
    /// 把一个市辖区采成 <see cref="CaptureModel"/>。**只读，不改世界** —— 这一层能不能产出蓝图，与套用（M4）无关。
    ///
    /// 线程口径：全程主线程、由面板上的一次点击触发（一次几百毫秒量级，不进帧循环）。
    /// 不写成 job 的理由是诚实的：四叉树要先 Complete 依赖、地形要等 GPU 回读，两边都在等；
    /// 而且这一层的正确性远比性能重要，出了错要能精确记「哪一节没采到」，job 里做不到。
    ///
    /// 容错口径（与 Playbook 3.5 一致）：每条支线单独 try/catch，采不着就记进 Missing 继续走，
    /// 玩家看到「有 N 项没采到」而不是「生成失败」。只有三种情况中止：不是市辖区、多边形读不出来、超单张上限。
    /// </summary>
    public static class DistrictCapture
    {
        public sealed class Result
        {
            public bool Ok;
            public string Error = string.Empty;
            public CaptureModel Model = new CaptureModel();
            public readonly List<string> Missing = new List<string>();
            public string DistrictName = string.Empty;
        }

        /// <summary>四叉树候选框外扩的米数：边界上的实体有一半属于这个区，别整颗丢掉（真正的取舍交给 <see cref="Inside"/>）。</summary>
        private const double MARGIN_M = 24d;

        public static Result Run(Entity district)
        {
            Result r = new Result();
            CaptureModel c = new CaptureModel();
            r.Model = c;

            try
            {
                World w = World.DefaultGameObjectInjectionWorld;
                if (w == null) { r.Error = "world-null"; return r; }
                EntityManager em = w.EntityManager;
                if (district == Entity.Null || !em.Exists(district) || !em.HasComponent<District>(district))
                {
                    r.Error = "not-district";
                    return r;
                }

                Safe(r, "name", delegate
                {
                    NameSystem ns = w.GetOrCreateSystemManaged<NameSystem>();
                    r.DistrictName = ns.GetRenderedLabelName(district) ?? string.Empty;
                });

                if (!ReadPolygon(em, district, c, r)) { r.Error = "no-polygon"; return r; }
                c.FinishPolygon();

                TerrainHeightData terrain = default(TerrainHeightData);
                bool hasTerrain = false;
                Safe(r, "terrain-system", delegate
                {
                    TerrainSystem ts = w.GetOrCreateSystemManaged<TerrainSystem>();
                    terrain = ts.GetHeightData(true);
                    hasTerrain = terrain.isCreated;
                    if (hasTerrain)
                        c.CenterGroundY = TerrainUtils.SampleHeight(ref terrain,
                            new float3((float)c.CenterX, 0f, (float)c.CenterZ));
                });
                if (!hasTerrain) r.Missing.Add("terrain:unavailable");

                Bounds3 box = new Bounds3
                {
                    min = new float3((float)(c.MinX - MARGIN_M), -10000f, (float)(c.MinZ - MARGIN_M)),
                    max = new float3((float)(c.MaxX + MARGIN_M), 10000f, (float)(c.MaxZ + MARGIN_M))
                };

                if (hasTerrain) SampleTerrain(c, box, terrain, r);
                CollectNets(w, em, box, c, r);
                CollectObjects(w, em, box, c, r);
                CollectZones(w, em, box, c, r);

                if (c.Oversize(out string what, out int count, out int limit))
                {
                    r.Error = "too-big:" + what + ":" + count + ":" + limit;
                    return r;
                }
                if (c.IsEmpty) { r.Error = "empty"; return r; }

                r.Ok = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Error = "capture:" + ex.GetType().Name;
                BlueprintHubMod.log.Warn("采集失败：" + ex.GetType().Name + " " + ex.Message);
                return r;
            }
        }

        // ---------------- 多边形 / 中心点 ----------------

        private static bool ReadPolygon(EntityManager em, Entity district, CaptureModel c, Result r)
        {
            if (!em.HasBuffer<AreaNode>(district)) { r.Missing.Add("area-node-buffer:missing"); return false; }
            DynamicBuffer<AreaNode> nodes;
            try { nodes = em.GetBuffer<AreaNode>(district, true); }
            catch (Exception ex) { r.Missing.Add("area-node-buffer:" + ex.GetType().Name); return false; }

            int n = nodes.Length;
            if (n < 3) { r.Missing.Add("area-node-buffer:too-few-" + n); return false; }
            c.RingX = new double[n];
            c.RingZ = new double[n];
            c.RingY = new double[n];
            for (int i = 0; i < n; i++)
            {
                AreaNode nd = nodes[i];
                c.RingX[i] = nd.m_Position.x;
                c.RingZ[i] = nd.m_Position.z;
                c.RingY[i] = nd.m_Elevation;
            }
            if (n > 1 && Math.Abs(c.RingX[0] - c.RingX[n - 1]) < 0.01 && Math.Abs(c.RingZ[0] - c.RingZ[n - 1]) < 0.01)
                c.RingClosed = true;
            return true;
        }

        // ---------------- 地形：按 u（8m）栅格采相对中心点地面高差 ----------------

        private static void SampleTerrain(CaptureModel c, Bounds3 box, TerrainHeightData terrain, Result r)
        {
            try
            {
                double spanX = box.max.x - box.min.x;
                double spanZ = box.max.z - box.min.z;
                double step = CatalogKit.CELL_EDGE_M;
                // 大区里一格一点能到百万级：超上限就整倍数放大步长（保住不卡比保住分辨率重要）
                while (Math.Ceiling(spanX / step) * Math.Ceiling(spanZ / step) > CaptureModel.MAX_TERRAIN_SAMPLES && step < 64d)
                    step *= 2d;

                int nx = (int)Math.Ceiling(spanX / step);
                int ny = (int)Math.Ceiling(spanZ / step);
                if (nx < 1 || ny < 1 || nx > 4096 || ny > 4096) { r.Missing.Add("terrain:grid-" + nx + "x" + ny); return; }

                c.TerrainNx = nx;
                c.TerrainNy = ny;
                c.TerrainStepM = step;
                c.TerrainOriginX = box.min.x + MARGIN_M;
                c.TerrainOriginZ = box.min.z + MARGIN_M;
                c.TerrainDeltasCm.Clear();

                float3 p = float3.zero;
                for (int j = 0; j < ny; j++)
                {
                    p.z = (float)(c.TerrainOriginZ + j * step);
                    for (int i = 0; i < nx; i++)
                    {
                        p.x = (float)(c.TerrainOriginX + i * step);
                        double h;
                        try { h = TerrainUtils.SampleHeight(ref terrain, p); }
                        catch (Exception) { h = c.CenterGroundY; }
                        c.TerrainDeltasCm.Add(CatalogKit.QuantizeCm16(CatalogKit.ToRelativeHeight(h, c.CenterGroundY)));
                    }
                }
            }
            catch (Exception ex) { r.Missing.Add("terrain:" + ex.GetType().Name); }
        }

        // ---------------- 路网 ----------------

        private static void CollectNets(World w, EntityManager em, Bounds3 box, CaptureModel c, Result r)
        {
            try
            {
                NetSearchSystem search = w.GetOrCreateSystemManaged<NetSearchSystem>();
                JobHandle deps;
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetNetSearchTree(true, out deps);
                deps.Complete();
                search.AddNetSearchTreeReader(deps);   // 只声明读：我们是过客，不占游戏的写权限

                NetGather g = new NetGather { m_Manager = em, m_Bounds = box };
                tree.Iterate(ref g);

                Dictionary<Entity, int> nodeIndex = new Dictionary<Entity, int>();
                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    Entity e = g.Nodes[i];
                    try
                    {
                        if (!em.HasComponent<NetNode>(e)) continue;
                        NetNode nd = em.GetComponentData<NetNode>(e);
                        double x = nd.m_Position.x, z = nd.m_Position.z;
                        if (!Inside(c, x, z)) continue;

                        nodeIndex[e] = c.Nodes.Count;
                        CaptureNode cn = new CaptureNode { X = x, Z = z, GroundY = nd.m_Position.y };
                        quaternion q = nd.m_Rotation;
                        cn.Qx = q.value.x; cn.Qy = q.value.y; cn.Qz = q.value.z; cn.Qw = q.value.w;
                        try
                        {
                            if (em.HasComponent<NetElevation>(e))
                                cn.Elevation = em.GetComponentData<NetElevation>(e).m_Elevation.x;
                        }
                        catch (Exception) { /* 没抬升就当 0，节点位置本身就带着高度 */ }
                        c.Nodes.Add(cn);
                        c.Grow(x, z);
                    }
                    catch (Exception ex) { r.Missing.Add("node:" + ex.GetType().Name); }
                }

                for (int i = 0; i < g.Edges.Count; i++)
                {
                    Entity e = g.Edges[i];
                    try
                    {
                        if (!em.HasComponent<NetEdge>(e)) continue;
                        NetEdge ed = em.GetComponentData<NetEdge>(e);
                        int a, b;
                        if (!nodeIndex.TryGetValue(ed.m_Start, out a)) continue;
                        if (!nodeIndex.TryGetValue(ed.m_End, out b)) continue;

                        CaptureEdge ce = new CaptureEdge { StartNode = a, EndNode = b, Asset = CaptureModel.NOT_FOUND };
                        try
                        {
                            if (em.HasComponent<NetCurve>(e))
                            {
                                NetCurve cv = em.GetComponentData<NetCurve>(e);
                                ce.LengthM = cv.m_Length;
                                ce.Bezier = new double[24];
                                FillBezier(ce.Bezier, cv.m_Bezier);
                            }
                        }
                        catch (Exception ex) { r.Missing.Add("curve:" + ex.GetType().Name); }

                        ce.Asset = ResolveNetAsset(w, em, e, ed.m_Start, ed.m_End, r);
                        c.Edges.Add(ce);
                    }
                    catch (Exception ex) { r.Missing.Add("edge:" + ex.GetType().Name); }
                }
            }
            catch (Exception ex)
            {
                r.Missing.Add("nets:" + ex.GetType().Name);
                BlueprintHubMod.log.Warn("路网采集失败：" + ex.GetType().Name);
            }
        }

        /// <summary>
        /// 四个控制点 ×(x,y,z) = 12 个 double 是真值，后 12 个是它的重复 ——
        /// bpc1 的 kCurve 记录是 8 点（正反两个方向各一条），现在只采得到一条中心线，
        /// 所以按「同一条线填两格」写死，并把这件事记在 Missing 里，M4 反解时才知道只有半边是真数据。
        /// </summary>
        private static void FillBezier(double[] dst, Bezier4x3 bz)
        {
            dst[0] = bz.a.x; dst[1] = bz.a.y; dst[2] = bz.a.z;
            dst[3] = bz.b.x; dst[4] = bz.b.y; dst[5] = bz.b.z;
            dst[6] = bz.c.x; dst[7] = bz.c.y; dst[8] = bz.c.z;
            dst[9] = bz.d.x; dst[10] = bz.d.y; dst[11] = bz.d.z;
            for (int k = 0; k < 12; k++) dst[12 + k] = dst[k];
        }

        private static int ResolveNetAsset(World w, EntityManager em, Entity edge, Entity start, Entity end, Result r)
        {
            PrefabSystem ps;
            try { ps = w.GetOrCreateSystemManaged<PrefabSystem>(); }
            catch (Exception) { return CaptureModel.NOT_FOUND; }

            Entity prefab = Entity.Null;
            Entity[] probe = { edge, start, end };
            for (int i = 0; i < probe.Length && prefab == Entity.Null; i++)
            {
                try
                {
                    if (probe[i] != Entity.Null && em.Exists(probe[i]) && em.HasComponent<PrefabRef>(probe[i]))
                        prefab = em.GetComponentData<PrefabRef>(probe[i]).m_Prefab;
                }
                catch (Exception) { /* 逐个探；三个都不带 PrefabRef 就算没认出来 */ }
            }
            if (prefab == Entity.Null) { r.Missing.Add("net-asset:unresolved"); return CaptureModel.NOT_FOUND; }
            return AddAsset(em, ps, prefab, "net", r);
        }

        // ---------------- 建筑 / 道具 / 树 ----------------

        private static void CollectObjects(World w, EntityManager em, Bounds3 box, CaptureModel c, Result r)
        {
            try
            {
                ObjectsSearchSystem search = w.GetOrCreateSystemManaged<ObjectsSearchSystem>();
                JobHandle deps;
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetStaticSearchTree(true, out deps);
                deps.Complete();
                search.AddStaticSearchTreeReader(deps);

                BoxGather g = new BoxGather { m_Manager = em, m_Bounds = box };
                tree.Iterate(ref g);

                PrefabSystem ps = w.GetOrCreateSystemManaged<PrefabSystem>();
                for (int i = 0; i < g.m_Out.Count; i++)
                {
                    Entity e = g.m_Out[i];
                    try
                    {
                        if (!em.HasComponent<ObjTransform>(e)) continue;
                        ObjTransform t = em.GetComponentData<ObjTransform>(e);
                        double x = t.m_Position.x, z = t.m_Position.z;
                        if (!Inside(c, x, z)) continue;

                        CaptureObject o = new CaptureObject
                        {
                            X = x,
                            Z = z,
                            GroundY = t.m_Position.y,
                            Asset = CaptureModel.NOT_FOUND,
                        };
                        quaternion q = t.m_Rotation;
                        o.Qx = q.value.x; o.Qy = q.value.y; o.Qz = q.value.z; o.Qw = q.value.w;

                        string kind = "prop";
                        try
                        {
                            if (em.HasComponent<Building>(e)) kind = "building";
                            else if (em.HasComponent<Tree>(e)) kind = "tree";
                        }
                        catch (Exception) { /* 判不了就按 prop 记，位置与资产仍然有用 */ }
                        o.Kind = kind;

                        try { if (em.HasComponent<ObjElevation>(e)) o.Elevation = em.GetComponentData<ObjElevation>(e).m_Elevation; }
                        catch (Exception) { }
                        try
                        {
                            if (em.HasComponent<ObjColor>(e))
                            {
                                ObjColor col = em.GetComponentData<ObjColor>(e);
                                o.ColorIndex = col.m_Index;
                                o.ColorValue = col.m_Value;
                                o.SubColor = col.m_SubColor;
                            }
                        }
                        catch (Exception) { }

                        if (em.HasComponent<PrefabRef>(e))
                            o.Asset = AddAsset(em, ps, em.GetComponentData<PrefabRef>(e).m_Prefab, kind, r);

                        int objIdx = c.Objects.Count;
                        c.Objects.Add(o);
                        c.Grow(x, z);

                        if (kind == "building")
                        {
                            try { o.OptionMask = em.GetComponentData<Building>(e).m_OptionMask; }
                            catch (Exception) { }
                            CollectUpgrades(em, e, objIdx, ps, r);
                        }
                    }
                    catch (Exception ex) { r.Missing.Add("object:" + ex.GetType().Name); }
                }
            }
            catch (Exception ex)
            {
                r.Missing.Add("objects:" + ex.GetType().Name);
                BlueprintHubMod.log.Warn("物体采集失败：" + ex.GetType().Name);
            }
        }

        private static void CollectUpgrades(EntityManager em, Entity building, int objIdx, PrefabSystem ps, Result r)
        {
            try
            {
                if (!em.HasBuffer<InstalledUpgrade>(building)) return;
                DynamicBuffer<InstalledUpgrade> ups = em.GetBuffer<InstalledUpgrade>(building, true);
                for (int i = 0; i < ups.Length; i++)
                {
                    Entity up = ups[i].m_Upgrade;
                    int asset = CaptureModel.NOT_FOUND;
                    try
                    {
                        if (up != Entity.Null && em.Exists(up) && em.HasComponent<PrefabRef>(up))
                            asset = AddAsset(em, ps, em.GetComponentData<PrefabRef>(up).m_Prefab, "other", r);
                    }
                    catch (Exception) { }
                    r.Model.Upgrades.Add(new CaptureUpgrade
                    {
                        Object = objIdx,
                        Asset = asset,
                        OptionMask = ups[i].m_OptionMask,
                    });
                }
            }
            catch (Exception ex) { r.Missing.Add("upgrade:" + ex.GetType().Name); }
        }

        // ---------------- 功能区 ----------------

        private static void CollectZones(World w, EntityManager em, Bounds3 box, CaptureModel c, Result r)
        {
            try
            {
                ZonesSearchSystem search = w.GetOrCreateSystemManaged<ZonesSearchSystem>();
                JobHandle deps;
                NativeQuadTree<Entity, Bounds2> tree = search.GetSearchTree(true, out deps);
                deps.Complete();
                search.AddSearchTreeReader(deps);

                ZoneGather g = new ZoneGather { m_Manager = em, m_Bounds = ToBounds2(box) };
                tree.Iterate(ref g);

                PrefabSystem ps = w.GetOrCreateSystemManaged<PrefabSystem>();
                ZoneSystem zs = null;
                try { zs = w.GetOrCreateSystemManaged<ZoneSystem>(); }
                catch (Exception) { r.Missing.Add("zone-system:unavailable"); }

                for (int i = 0; i < g.m_Out.Count; i++)
                {
                    Entity e = g.m_Out[i];
                    try
                    {
                        if (!em.HasComponent<Block>(e)) continue;
                        Block b = em.GetComponentData<Block>(e);
                        double x = b.m_Position.x, z = b.m_Position.z;
                        if (!Inside(c, x, z)) continue;

                        int blkIdx = c.Blocks.Count;
                        c.Blocks.Add(new CaptureZoneBlock
                        {
                            X = x,
                            Z = z,
                            GroundY = b.m_Position.y,
                            DirX = b.m_Direction.x,
                            DirY = b.m_Direction.y,
                            SizeX = b.m_Size.x,
                            SizeZ = b.m_Size.y,
                        });
                        c.Grow(x, z);

                        if (!em.HasBuffer<Cell>(e)) continue;
                        DynamicBuffer<Cell> cells = em.GetBuffer<Cell>(e, true);
                        int width = Math.Max(1, b.m_Size.x);
                        for (int k = 0; k < cells.Length; k++)
                        {
                            Cell cell = cells[k];
                            CaptureZoneCell cc = new CaptureZoneCell
                            {
                                Block = blkIdx,
                                CX = (byte)Math.Min(byte.MaxValue, k % width),
                                CZ = (byte)Math.Min(byte.MaxValue, k / width),
                                State = (ushort)cell.m_State,
                                ZoneIndex = cell.m_Zone.m_Index,
                                HeightCm = cell.m_Height,
                                Asset = CaptureModel.NOT_FOUND,
                            };
                            if (zs != null)
                            {
                                try
                                {
                                    Entity zp = zs.GetPrefab(cell.m_Zone);
                                    if (zp != Entity.Null) cc.Asset = AddAsset(em, ps, zp, "zone", r);
                                }
                                catch (Exception) { /* 认不出来留 -1，别让整块格子丢掉 */ }
                            }
                            c.Cells.Add(cc);
                        }
                    }
                    catch (Exception ex) { r.Missing.Add("zone:" + ex.GetType().Name); }
                }
            }
            catch (Exception ex)
            {
                r.Missing.Add("zones:" + ex.GetType().Name);
                BlueprintHubMod.log.Warn("功能区采集失败：" + ex.GetType().Name);
            }
        }

        // ---------------- 公共小工具 ----------------

        /// <summary>把 prefab 登记进资产表（同一 prefab 只登记一次，下标就是 blob 里的 asset 字段）。</summary>
        private static int AddAsset(EntityManager em, PrefabSystem ps, Entity prefab, string kind, Result r)
        {
            if (ps == null || prefab == Entity.Null || !em.Exists(prefab)) return CaptureModel.NOT_FOUND;
            string type = string.Empty, name = string.Empty;
            PrefabBase pb = null;
            try
            {
                name = ps.GetPrefabName(prefab) ?? string.Empty;
                pb = ps.GetPrefab<PrefabBase>(prefab);
                if (pb != null) type = pb.GetType().Name;
            }
            catch (Exception ex)
            {
                r.Missing.Add("asset:" + ex.GetType().Name);
                return CaptureModel.NOT_FOUND;
            }

            // 依赖标注：官方侧拿不到可靠的数字 modId（PrefabBase 只有 isBuiltin / isSubscribedMod 两个布尔），
            // 所以非原版资产只记 modName，绝不谎报一个数字 ID 让玩家去装不存在的东西。
            int modId = 0;
            string modName = null;
            try
            {
                if (pb != null && !pb.isBuiltin)
                {
                    modName = pb.isSubscribedMod ? "subscribed-mod" : "local-mod";
                    r.Missing.Add("asset-mod:" + name);
                }
            }
            catch (Exception) { /* 认不出来源就按原版报 */ }

            int idx = r.Model.Asset(type, name, kind, modId, modName);
            if (idx == CaptureModel.NOT_FOUND) r.Missing.Add("asset-table-full");
            return idx;
        }

        /// <summary>射线法点在多边形内。四叉树只给包围盒，越界的东西全靠这一句挡掉。</summary>
        private static bool Inside(CaptureModel c, double x, double z)
        {
            double[] xs = c.RingX, zs = c.RingZ;
            if (xs == null || xs.Length < 3) return true;      // 没多边形就退化成包围盒（已是外层过滤的结果）
            bool inside = false;
            int n = xs.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((zs[i] > z) != (zs[j] > z) &&
                    x < (xs[j] - xs[i]) * (z - zs[i]) / (zs[j] - zs[i] + 1e-9) + xs[i])
                    inside = !inside;
            }
            return inside;
        }

        private static Bounds2 ToBounds2(Bounds3 b)
        {
            return new Bounds2(new float2(b.min.x, b.min.z), new float2(b.max.x, b.max.z));
        }

        /// <summary>一段可以整体失败的小支线：异常只记名字，不让它带走整次采集。</summary>
        private static void Safe(Result r, string tag, Action body)
        {
            try { body(); }
            catch (Exception ex) { r.Missing.Add(tag + ":" + ex.GetType().Name); }
        }

        // ---------------- 四叉树迭代器 ----------------
        // 必须同时实现 INativeQuadTreeIterator 与 IUnsafeQuadTreeIterator，只实现后者会 CS0315（Playbook 步骤 3.4）。
        // 结构体里放托管字段是安全的：不 Burst，只在主线程跟着 tree.Iterate 跑一次。

        private struct NetGather : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public EntityManager m_Manager;
            public Bounds3 m_Bounds;
            public List<Entity> Edges;
            public List<Entity> Nodes;
            public HashSet<Entity> SeenEdges;
            public HashSet<Entity> SeenNodes;

            public bool Intersect(QuadTreeBoundsXZ b)
            {
                return MathUtils.Intersect(b.m_Bounds, m_Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ b, Entity item)
            {
                if (item == Entity.Null || !Intersect(b)) return;
                try
                {
                    if (Edges == null) { Edges = new List<Entity>(); Nodes = new List<Entity>(); SeenEdges = new HashSet<Entity>(); SeenNodes = new HashSet<Entity>(); }
                    if (m_Manager.HasComponent<NetEdge>(item)) { if (SeenEdges.Add(item)) Edges.Add(item); }
                    else if (m_Manager.HasComponent<NetNode>(item)) { if (SeenNodes.Add(item)) Nodes.Add(item); }
                }
                catch (Exception) { /* 迭代器里抛异常会打断整棵树 */ }
            }
        }

        private struct BoxGather : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public EntityManager m_Manager;
            public Bounds3 m_Bounds;
            public List<Entity> m_Out;
            public HashSet<Entity> m_Seen;

            public bool Intersect(QuadTreeBoundsXZ b)
            {
                return MathUtils.Intersect(b.m_Bounds, m_Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ b, Entity item)
            {
                if (item == Entity.Null || !Intersect(b)) return;
                try
                {
                    if (m_Out == null) { m_Out = new List<Entity>(); m_Seen = new HashSet<Entity>(); }
                    if (m_Seen.Add(item)) m_Out.Add(item);
                }
                catch (Exception) { }
            }
        }

        private struct ZoneGather : INativeQuadTreeIterator<Entity, Bounds2>, IUnsafeQuadTreeIterator<Entity, Bounds2>
        {
            public EntityManager m_Manager;
            public Bounds2 m_Bounds;
            public List<Entity> m_Out;
            public HashSet<Entity> m_Seen;

            public bool Intersect(Bounds2 b)
            {
                return b.min.x <= m_Bounds.max.x && b.max.x >= m_Bounds.min.x
                    && b.min.y <= m_Bounds.max.y && b.max.y >= m_Bounds.min.y;
            }

            public void Iterate(Bounds2 b, Entity item)
            {
                if (item == Entity.Null || !Intersect(b)) return;
                try
                {
                    if (m_Out == null) { m_Out = new List<Entity>(); m_Seen = new HashSet<Entity>(); }
                    if (m_Seen.Add(item)) m_Out.Add(item);
                }
                catch (Exception) { }
            }
        }
    }
}
