using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// bpc1 分节编码器：一节 = 一个内容寻址 blob（`blob/&lt;sha16&gt;.bin`），内部是一张张定长记录表。
    ///
    /// 布局（小端）：
    ///   "BPC1" (4B) | sectionCode(u8)
    ///   然后一张接一张写到末尾： nameLen(u8) name(ascii) | recordSize(u16) | count(u32) | payload(count × recordSize)
    ///
    /// 为什么定长 + 表名而不是逐条变长：
    ///  · 内容寻址要求同样输入产出**逐字节相同**的 blob（sha16 命中才有缓存可言），所以这里不许出现字典序、时间、随机数；
    ///  · 定长记录让套用端按 stride 跳读，不必把 40 万个功能区格全部解析进托管堆。
    /// 表名与字段顺序就是版本内容；改字段必须同步改下面的 k* 常量与 tests/t3 的往返断言。
    /// 输入的 CaptureModel 见 Bpc/CaptureModel.cs —— 这个文件里一个游戏类型都不许出现（硬规则 14）。
    /// </summary>
    public static class BpcCodec
    {
        public const string kMagic = "BPC1";
        public const string kCodec = "bpc1";

        public static readonly string[] kSectionNames = { "terrain", "nets", "objects", "zones", "areas" };

        // ---------------- 记录尺寸（字节，实测由下面的写入顺序数出来，改字段必须同步改这里）----------------
        public const int kRing = 12;          // x:i32 z:i32 y:i16 pad:u16
        public const int kBBox = 24;          // minX:minZ:maxX:maxZ:i32 groundY:i16 approx:u8 pad:u8 pad:u32
        public const int kTerrainMeta = 16;   // nx:u16 ny:u16 stepCm:u16 pad:u16 originX:i32 originZ:i32
        public const int kTerrainSample = 2;  // i16 高差（cm，相对中心点地面）
        public const int kNode = 28;          // x:i32 z:i32 y:i16 elev:i16 q:4*i16 flags:u32 pad:u32
        public const int kEdge = 20;          // start:u32 end:u32 asset:u16 pad:u16 lenCm:i32 flags:u32
        public const int kCurve = 84;         // edge:u32 + 8 × (x:i32 y:i16 z:i32) —— 控制点按 x,y,z 顺序
        public const int kObject = 32;        // x:i32 z:i32 y:i16 elev:i16 q:4*i16 asset:u16 kind:u8 ci:u8 cv:u8 sub:u8 pad:u16 option:u32
        public const int kUpgrade = 12;       // obj:u32 asset:u16 pad:u16 option:u32
        public const int kBlock = 20;         // x:i32 z:i32 y:i16 pad:u16 dirX:i16 dirY:i16 sx:u16 sz:u16
        public const int kCell = 16;          // blk:u32 cx:u8 cz:u8 state:u16 h:i16 asset:u16 zoneIdx:u16 pad:u16

        /// <summary>把一张蓝图编成 5 个 blob（可能为空节 → sha16 空串，与 schema 的 "^$|^[0-9a-f]{16}$" 对齐）。</summary>
        public static SectionResult[] Encode(CaptureModel c)
        {
            if (c == null) throw new ArgumentNullException("c");
            SectionResult[] r = new SectionResult[kSectionNames.Length];
            r[0] = EncodeTerrain(c);
            r[1] = EncodeNets(c);
            r[2] = EncodeObjects(c);
            r[3] = EncodeZones(c);
            r[4] = EncodeAreas(c);
            return r;
        }

        // ---------------- areas ----------------
        public static SectionResult EncodeAreas(CaptureModel c)
        {
            int n = c.RingX.Length;
            if (n > 1 && c.RingClosed
                && Math.Abs(c.RingX[0] - c.RingX[n - 1]) < 1e-4 && Math.Abs(c.RingZ[0] - c.RingZ[n - 1]) < 1e-4) n--;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII))
            {
                if (n >= 3)
                {
                    BeginTable(w, "ring", kRing, n);
                    for (int i = 0; i < n; i++)
                    {
                        WriteM(w, c.RingX[i] - c.CenterX);
                        WriteM(w, c.RingZ[i] - c.CenterZ);
                        w.Write(CatalogKit.QuantizeCm16(CatalogKit.ToRelativeHeight(
                            c.RingY != null && c.RingY.Length > i ? c.RingY[i] : c.CenterGroundY, c.CenterGroundY)));
                        w.Write((ushort)0);
                    }
                    EndTable(w);
                }
                BeginTable(w, "bbox", kBBox, 1);
                WriteM(w, c.MinX - c.CenterX);
                WriteM(w, c.MinZ - c.CenterZ);
                WriteM(w, c.MaxX - c.CenterX);
                WriteM(w, c.MaxZ - c.CenterZ);
                w.Write(CatalogKit.QuantizeCm16(0d));
                w.Write((byte)(c.CenterApproximate ? 1 : 0));
                w.Write((byte)0);
                w.Write((uint)0);
                EndTable(w);
                w.Flush();
                return Finish("areas", ms.ToArray());
            }
        }

        // ---------------- terrain ----------------
        public static SectionResult EncodeTerrain(CaptureModel c)
        {
            if (c.TerrainNx <= 0 || c.TerrainNy <= 0) return Finish("terrain", new byte[0]);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII))
            {
                BeginTable(w, "meta", kTerrainMeta, 1);
                w.Write((ushort)c.TerrainNx);
                w.Write((ushort)c.TerrainNy);
                w.Write((ushort)Math.Round(c.TerrainStepM * 100d));
                w.Write((ushort)0);
                WriteM(w, c.TerrainOriginX - c.CenterX);
                WriteM(w, c.TerrainOriginZ - c.CenterZ);
                EndTable(w);

                int n = Math.Min(c.TerrainDeltasCm.Count, c.TerrainNx * c.TerrainNy);
                BeginTable(w, "h", kTerrainSample, n);
                for (int i = 0; i < n; i++) w.Write(c.TerrainDeltasCm[i]);
                EndTable(w);
                w.Flush();
                return Finish("terrain", ms.ToArray());
            }
        }

        // ---------------- nets ----------------
        public static SectionResult EncodeNets(CaptureModel c)
        {
            if (c.Nodes.Count == 0 && c.Edges.Count == 0) return Finish("nets", new byte[0]);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII))
            {
                BeginTable(w, "node", kNode, c.Nodes.Count);
                for (int i = 0; i < c.Nodes.Count; i++)
                {
                    CaptureNode p = c.Nodes[i];
                    WriteM(w, p.X);
                    WriteM(w, p.Z);
                    w.Write(CatalogKit.QuantizeCm16(p.GroundY - c.CenterGroundY));
                    w.Write(CatalogKit.QuantizeCm16(p.Elevation));
                    WriteQuat(w, p);
                    w.Write(p.Flags);
                    w.Write((uint)0);
                }
                EndTable(w);

                BeginTable(w, "edge", kEdge, c.Edges.Count);
                for (int i = 0; i < c.Edges.Count; i++)
                {
                    CaptureEdge e = c.Edges[i];
                    w.Write((uint)Math.Max(0, e.StartNode));
                    w.Write((uint)Math.Max(0, e.EndNode));
                    w.Write((ushort)Math.Max(0, e.Asset));
                    w.Write((ushort)0);
                    w.Write((int)Math.Round(e.LengthM * 100d));
                    w.Write(e.Flags);
                }
                EndTable(w);

                int curved = 0;
                for (int i = 0; i < c.Edges.Count; i++)
                    if (c.Edges[i].Bezier != null && c.Edges[i].Bezier.Length >= 24) curved++;
                if (curved > 0)
                {
                    BeginTable(w, "curve", kCurve, curved);
                    for (int i = 0; i < c.Edges.Count; i++)
                    {
                        double[] b = c.Edges[i].Bezier;
                        if (b == null || b.Length < 24) continue;
                        w.Write((uint)i);
                        for (int k = 0; k < 8; k++)
                        {
                            WriteM(w, b[k * 3 + 0]);
                            w.Write(CatalogKit.QuantizeCm16(b[k * 3 + 1]));
                            WriteM(w, b[k * 3 + 2]);
                        }
                    }
                    EndTable(w);
                }
                w.Flush();
                return Finish("nets", ms.ToArray());
            }
        }

        // ---------------- objects ----------------
        public static SectionResult EncodeObjects(CaptureModel c)
        {
            if (c.Objects.Count == 0) return Finish("objects", new byte[0]);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII))
            {
                BeginTable(w, "obj", kObject, c.Objects.Count);
                for (int i = 0; i < c.Objects.Count; i++)
                {
                    CaptureObject o = c.Objects[i];
                    WriteM(w, o.X);
                    WriteM(w, o.Z);
                    w.Write(CatalogKit.QuantizeCm16(o.GroundY - c.CenterGroundY));
                    w.Write(CatalogKit.QuantizeCm16(o.Elevation));
                    WriteQuat(w, o);
                    w.Write((ushort)Math.Max(0, o.Asset));
                    w.Write(KindCode(o.Kind));
                    w.Write(o.ColorIndex);
                    w.Write(o.ColorValue);
                    w.Write((byte)(o.SubColor ? 1 : 0));
                    w.Write((ushort)0);
                    w.Write(o.OptionMask);
                }
                EndTable(w);

                if (c.Upgrades.Count > 0)
                {
                    BeginTable(w, "upg", kUpgrade, c.Upgrades.Count);
                    for (int i = 0; i < c.Upgrades.Count; i++)
                    {
                        CaptureUpgrade u = c.Upgrades[i];
                        w.Write((uint)Math.Max(0, u.Object));
                        w.Write((ushort)Math.Max(0, u.Asset));
                        w.Write((ushort)0);
                        w.Write(u.OptionMask);
                    }
                    EndTable(w);
                }
                w.Flush();
                return Finish("objects", ms.ToArray());
            }
        }

        // ---------------- zones ----------------
        public static SectionResult EncodeZones(CaptureModel c)
        {
            if (c.Blocks.Count == 0 && c.Cells.Count == 0) return Finish("zones", new byte[0]);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII))
            {
                BeginTable(w, "blk", kBlock, c.Blocks.Count);
                for (int i = 0; i < c.Blocks.Count; i++)
                {
                    CaptureZoneBlock b = c.Blocks[i];
                    WriteM(w, b.X);
                    WriteM(w, b.Z);
                    w.Write(CatalogKit.QuantizeCm16(b.GroundY - c.CenterGroundY));
                    w.Write((ushort)0);
                    w.Write((short)Math.Round(b.DirX * 10000d));
                    w.Write((short)Math.Round(b.DirY * 10000d));
                    w.Write((ushort)Math.Max(0, Math.Min(byte.MaxValue, b.SizeX)));
                    w.Write((ushort)Math.Max(0, Math.Min(byte.MaxValue, b.SizeZ)));
                }
                EndTable(w);

                BeginTable(w, "cell", kCell, c.Cells.Count);
                for (int i = 0; i < c.Cells.Count; i++)
                {
                    CaptureZoneCell k = c.Cells[i];
                    w.Write((uint)Math.Max(0, k.Block));
                    w.Write(k.CX);
                    w.Write(k.CZ);
                    w.Write(k.State);
                    w.Write(k.HeightCm);
                    w.Write((ushort)Math.Max(0, k.Asset));
                    w.Write(k.ZoneIndex);
                    w.Write((ushort)0);
                }
                EndTable(w);
                w.Flush();
                return Finish("zones", ms.ToArray());
            }
        }

        // ---------------- 解码侧（读回来只为两件事：t3 的往返断言、M4 的套用）----------------

        /// <summary>
        /// 把 blob 拆成「表名 → 定长记录字节」。不解释字段，字段解释留给 M4（那时才知道要跳读哪几张表）。
        /// 读到流末尾为止：表的数量不由头部声明（见 Finish 的注释），所以坏数据的上限就是「少读几张表」，
        /// 而不是「按一个被改过的 count 读出天长的字节」。
        /// </summary>
        public static List<CapturedTable> Decode(byte[] blob, out int sectionCode)
        {
            sectionCode = -1;
            List<CapturedTable> tables = new List<CapturedTable>();
            if (blob == null || blob.Length < 5) return tables;
            using (MemoryStream ms = new MemoryStream(blob))
            using (BinaryReader r = new BinaryReader(ms, Encoding.ASCII))
            {
                if (new string(r.ReadChars(4)) != kMagic) return tables;
                sectionCode = r.ReadByte();
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    int nl = r.ReadByte();
                    if (nl <= 0 || nl > 32 || r.BaseStream.Position + nl > r.BaseStream.Length) break;
                    string name = new string(r.ReadChars(nl));
                    int rs = r.ReadUInt16();
                    long count = r.ReadUInt32();
                    long size = (long)count * rs;
                    if (rs <= 0 || size > r.BaseStream.Length - r.BaseStream.Position) break;   // 声明得比剩下的字节还长 → 停
                    byte[] body = size > 0 ? r.ReadBytes((int)size) : new byte[0];
                    tables.Add(new CapturedTable { Name = name, RecordSize = rs, Count = (int)count, Bytes = body });
                }
            }
            return tables;
        }

        public sealed class CapturedTable
        {
            public string Name;
            public int RecordSize;
            public int Count;
            public byte[] Bytes;

            public short I16(int record, int fieldByte) { return BitConverter.ToInt16(Bytes, record * RecordSize + fieldByte); }
            public ushort U16(int record, int fieldByte) { return BitConverter.ToUInt16(Bytes, record * RecordSize + fieldByte); }
            public double M(int record, int fieldByte) { return BitConverter.ToInt32(Bytes, record * RecordSize + fieldByte) / 1000d; }
        }

        // ---------------- 小工具 ----------------

        /// <summary>
        /// BeginTable 写表头（调用方此刻必须已知条数）；EndTable 只把「 Begin/End 成对 」这件事显式写在代码里，
        /// 好让「记了 N 条却写了 M 条 payload」这类错误在编码阶段就抛，而不是产出一个歪 blob 进仓库。
        /// </summary>
        private sealed class Pending
        {
            public string Name;
            public int RecordSize;
            public int Count;
            public long Start;
        }

        private static readonly List<Pending> m_Pending = new List<Pending>();

        private static void BeginTable(BinaryWriter w, string name, int recordSize, int count)
        {
            Pending p = new Pending { Name = name, RecordSize = recordSize, Count = count };
            byte[] nb = Encoding.ASCII.GetBytes(name);
            if (nb.Length > 255) throw new InvalidOperationException("表名太长: " + name);
            w.Write((byte)nb.Length);
            w.Write(nb);
            w.Write((ushort)recordSize);
            w.Write((uint)count);
            p.Start = w.BaseStream.Position;
            m_Pending.Add(p);
        }

        private static void EndTable(BinaryWriter w)
        {
            if (m_Pending.Count == 0) throw new InvalidOperationException("EndTable 没有配对的 BeginTable");
            Pending p = m_Pending[m_Pending.Count - 1];
            m_Pending.RemoveAt(m_Pending.Count - 1);
            long wrote = w.BaseStream.Position - p.Start;
            if (wrote != (long)p.Count * p.RecordSize)
                throw new InvalidOperationException("表 " + p.Name + " 声明 " + p.Count + " 条 × " + p.RecordSize +
                    " 字节，实际写了 " + wrote + " 字节");
        }

        /// <summary>米 → i32（mm 定点；区域内部最大 ±8000m，mm 精度用掉 ±8×10^6，远小于 int32）。</summary>
        private static void WriteM(BinaryWriter w, double meters)
        {
            double mm = Math.Round(meters * 1000d, MidpointRounding.AwayFromZero);
            if (mm > int.MaxValue) mm = int.MaxValue;
            if (mm < int.MinValue) mm = int.MinValue;
            w.Write((int)mm);
        }

        private static void WriteQuat(BinaryWriter w, CaptureNode p)
        {
            WriteQ(w, p.Qx); WriteQ(w, p.Qy); WriteQ(w, p.Qz); WriteQ(w, p.Qw);
        }

        private static void WriteQuat(BinaryWriter w, CaptureObject p)
        {
            WriteQ(w, p.Qx); WriteQ(w, p.Qy); WriteQ(w, p.Qz); WriteQ(w, p.Qw);
        }

        /// <summary>四元数分量 ∈ [-1,1] → i16（1/32767 精度，旋转角误差 &lt; 0.004°，足够道路与建筑朝向）。</summary>
        private static void WriteQ(BinaryWriter w, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            if (v > 1f) v = 1f;
            if (v < -1f) v = -1f;
            w.Write((short)Math.Round(v * 32767f));
        }

        private static byte KindCode(string kind)
        {
            switch (kind)
            {
                case "building": return 1;
                case "prop": return 2;
                case "tree": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// 空节 → 空 sha16 + 0 字节（schema 允许 "^$"，面板与套用端都按「这节没有」处理）。
        /// 非空节在这里补上 5 字节头：magic(4) + sectionCode(1)。
        /// 表**不写总数**，由读取方按流末尾自行收敛 —— 写总数就得在编码中途回填，
        /// 而这张 blob 是单向往前写的（定长记录一路 append），回填会把 Begin/End 那套配对检查绕过去。
        /// </summary>
        private static SectionResult Finish(string section, byte[] body)
        {
            SectionResult r = new SectionResult();
            r.Section = section;
            r.Codec = kCodec;
            byte[] payload = body ?? new byte[0];
            if (payload.Length == 0)
            {
                r.Bytes = new byte[0];
                r.Sha16 = string.Empty;
                return r;
            }
            int code = SectionCode(section);
            byte[] all = new byte[payload.Length + 5];
            Encoding.ASCII.GetBytes(kMagic, 0, 4, all, 0);
            all[4] = (byte)code;
            Buffer.BlockCopy(payload, 0, all, 5, payload.Length);
            r.Bytes = all;
            r.Sha16 = BlueprintId.Hash16(all);
            return r;
        }

        /// <summary>节名 → 编号（顺序就是 kSectionNames 的顺序，schema 的 sections 五个键同序）。</summary>
        public static int SectionCode(string section)
        {
            for (int i = 0; i < kSectionNames.Length; i++) if (kSectionNames[i] == section) return i;
            return 255;
        }
    }

    /// <summary>一节编出来的结果：blob 字节 + 内容寻址名 + 编码器版本。</summary>
    public sealed class SectionResult
    {
        public string Section;
        public string Sha16;
        public string Codec;
        public byte[] Bytes;
        public int Length { get { return Bytes == null ? 0 : Bytes.Length; } }
        public bool Empty { get { return Length == 0; } }
    }
}
