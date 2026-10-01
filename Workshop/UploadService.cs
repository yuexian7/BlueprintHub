using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BlueprintHub.Bpc;
using BlueprintHub.Platform;
using BlueprintHub.Systems.UI;
using Unity.Entities;

namespace BlueprintHub.Workshop
{
    /// <summary>
    /// 「市辖区 → 可提交的蓝图包」这条流水线的总控。写侧走 PR 模式（设计文档 6.2 已定）：
    /// 模组只产出一个**仓库形状**的草稿目录，玩家/作者自己把它放进 workshop 仓库再提交。
    /// 模组的二进制里**没有任何能写仓库的凭据**，也不发任何写请求 —— 这是硬约束，别加。
    ///
    ///   ModsData/BlueprintHub/drafts/&lt;bpId&gt;/meta.json
    ///   ModsData/BlueprintHub/drafts/&lt;bpId&gt;/preview/cover.svg
    ///   ModsData/BlueprintHub/drafts/&lt;bpId&gt;/blob/&lt;sha16&gt;.bin
    ///
    /// 口径：
    ///  · 一次只跑一个采集（<see cref="Phase.Busy"/> 期间再点不排队，直接忽略）；
    ///  · 全程主线程、全程 try/catch，任何异常只变成 Failed + 一个错误键（句子在前端词条里）；
    ///  · 落盘只写自己算出来的路径：bpId 过 <see cref="BlueprintId.IsValid"/>、blob 名是 sha16 hex，
    ///    玩家输入的名称/简介**永远不进路径**（需求：安全审查里这条是第一条）。
    /// </summary>
    public static class UploadService
    {
        public enum Phase { Idle, Busy, Done, Failed }

        public static Phase State = Phase.Idle;
        public static string DraftPath = string.Empty;
        public static string BpId = string.Empty;
        public static string Detail = string.Empty;      // 失败时是错误键（not-district / no-polygon / too-big:… / empty / capture:…）
        public static int MissingCount;
        public static long TotalBytes;
        public static string CoverUrl = string.Empty;    // coui:// 地址，面板与市辖区面板都能立刻显示预览
        public static string DraftName = string.Empty;

        /// <summary>
        /// 当前选中的那个市辖区，由 <see cref="Systems.UI.DistrictUploadSection"/> 在每次面板刷新时写。
        /// Entity 不跨 JS 边界传（它的 index/version 只有本帧的 archetype 能解释），所以采集目标只在 C# 侧流动。
        /// </summary>
        public static Entity ReadyDistrict = Entity.Null;

        /// <summary>给前端的相位名（小写英文，前端按它取词条，不在这儿造句）。</summary>
        public static string PhaseName
        {
            get
            {
                switch (State)
                {
                    case Phase.Busy: return "busy";
                    case Phase.Done: return "done";
                    case Phase.Failed: return "failed";
                    default: return "idle";
                }
            }
        }

        /// <summary>草稿箱里最多留几张（超了删最旧的 —— 作者要求「临时文件记得做自动清理」）。</summary>
        public const int KEEP_DRAFTS = 12;

        /// <summary>单张蓝图的 blob 总量上限：公开的 CDN 与免费档仓库都吃不下更大的东西。</summary>
        public const long MAX_PACKAGE_BYTES = 24L * 1024L * 1024L;

        public static void Reset()
        {
            if (State == Phase.Busy) return;
            State = Phase.Idle;
            Detail = string.Empty;
            Bump();
        }

        /// <summary>面板上那颗按钮的入口：现场采集 → 编码 → 落盘。一次点击跑完，玩家不需要等第二个线程。</summary>
        public static void Start(Entity district, string name, string category, string description)
        {
            if (State == Phase.Busy) return;
            State = Phase.Busy;
            Detail = string.Empty;
            MissingCount = 0;
            TotalBytes = 0;
            Bump();

            try { Run(district, name, category, description); }
            catch (Exception ex)
            {
                State = Phase.Failed;
                Detail = "capture:" + ex.GetType().Name;
                BlueprintHubMod.log.Error("上传流程异常：" + ex.GetType().Name + " " + ex.Message);
            }
            if (State != Phase.Busy) Bump();
        }

        private static void Run(Entity district, string name, string category, string description)
        {
            name = (name ?? string.Empty).Trim();
            description = (description ?? string.Empty).Trim();
            if (name.Length == 0) name = "Blueprint";
            if (name.Length > 48) name = name.Substring(0, 48);           // schema maxLength=48，超了截断而不是让 CI 打回
            if (description.Length > 2000) description = description.Substring(0, 2000);
            if (!CatalogKit.IsKnownCategory(category)) category = CatalogKit.CategoryIds[0];

            string author = IdentityKit.PlayerKey();
            if (string.IsNullOrEmpty(author) || !BlueprintId.IsAuthorKey(author))
            {
                State = Phase.Failed;
                Detail = "no-author-key";
                return;
            }

            DistrictCapture.Result cap = DistrictCapture.Run(district);
            MissingCount = cap.Missing.Count;
            if (!cap.Ok)
            {
                State = Phase.Failed;
                Detail = cap.Error ?? "capture:unknown";
                return;
            }

            CaptureModel m = cap.Model;
            SectionResult[] sections;
            try { sections = BpcCodec.Encode(m); }
            catch (Exception ex)
            {
                State = Phase.Failed;
                Detail = "encode:" + ex.GetType().Name;
                BlueprintHubMod.log.Error("编码失败：" + ex.GetType().Name + " " + ex.Message);
                return;
            }

            long bytes = 0;
            for (int i = 0; i < sections.Length; i++) bytes += sections[i].Length;
            if (bytes > MAX_PACKAGE_BYTES)
            {
                State = Phase.Failed;
                Detail = "too-big:bytes:" + bytes.ToString(CultureInfo.InvariantCulture) + ":" + MAX_PACKAGE_BYTES;
                return;
            }

            DateTime now = DateTime.UtcNow;
            string bpId = BlueprintId.New(Guid.NewGuid(), author);
            if (!BlueprintId.IsValid(bpId)) { State = Phase.Failed; Detail = "bad-bpid"; return; }

            string cover;
            try { cover = CoverSvg.Build(m, category, name); }
            catch (Exception ex) { cover = string.Empty; MissingCount++; BlueprintHubMod.log.Warn("预览生成失败：" + ex.GetType().Name); }

            BlueprintDraft d = new BlueprintDraft();
            d.BpId = bpId;
            d.AuthorId = author;
            d.AuthorName = AuthorName();
            d.Name = name;
            d.Description = description;
            d.Category = category;
            d.DistrictName = cap.DistrictName;
            d.GameBuild = Shorten(UnityEngine.Application.version, 16);
            d.GeneratorVersion = Shorten(BlueprintHubMod.kVersion, 16);
            d.CreatedUtc = now;
            d.UpdatedUtc = now;
            d.Model = m;
            d.Sections = sections;
            d.Cover = "preview/cover.svg";
            for (int i = 0; i < cap.Missing.Count; i++) d.Missing.Add(Shorten(cap.Missing[i], 96));

            string meta;
            try { meta = MetaWriter.ToJson(d); }
            catch (Exception ex)
            {
                State = Phase.Failed;
                Detail = "meta:" + ex.GetType().Name;
                BlueprintHubMod.log.Error("meta 生成失败：" + ex.GetType().Name + " " + ex.Message);
                return;
            }

            LocalLibrary.EnsureDirs();
            string dir = Path.Combine(LocalLibrary.DraftsDir, bpId);
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "blob"));
                Directory.CreateDirectory(Path.Combine(dir, "preview"));

                if (!LocalLibrary.WriteText(Path.Combine(dir, "meta.json"), meta + "\n"))
                {
                    State = Phase.Failed; Detail = "write:meta"; return;
                }

                for (int i = 0; i < sections.Length; i++)
                {
                    SectionResult s = sections[i];
                    if (s.Empty) continue;
                    if (!IsHex(s.Sha16)) { MissingCount++; continue; }        // 名字不合法就整节丢掉，绝不拼出越界路径
                    if (!LocalLibrary.WriteBytes(Path.Combine(dir, "blob", s.Sha16 + ".bin"), s.Bytes))
                    {
                        State = Phase.Failed; Detail = "write:blob"; DeleteDraft(dir); return;
                    }
                }

                if (cover.Length > 0)
                {
                    if (!LocalLibrary.WriteText(Path.Combine(dir, "preview", "cover.svg"), cover))
                        BlueprintHubMod.log.Warn("预览写盘失败（蓝图仍然可用，只是没封面）");
                    else
                        StageCover(bpId, cover);
                }
            }
            catch (Exception ex)
            {
                State = Phase.Failed;
                Detail = "write:" + ex.GetType().Name;
                DeleteDraft(dir);
                BlueprintHubMod.log.Error("草稿落盘失败：" + ex.GetType().Name + " " + ex.Message);
                return;
            }

            BpId = bpId;
            DraftPath = dir;
            DraftName = name;
            TotalBytes = bytes;
            State = Phase.Done;
            PruneDrafts();
            BlueprintHubMod.log.Info("蓝图已生成 " + bpId + "（" + bytes + " 字节，缺采 " + MissingCount + " 项）→ " + dir);
        }

        /// <summary>把封面同时放进已挂成 coui 主机的临时目录，面板立刻能看到这张图（文件名固定 → 清理目标也固定）。</summary>
        private static void StageCover(string bpId, string cover)
        {
            try
            {
                LocalLibrary.EnsureDirs();
                string file = Path.Combine(LocalLibrary.TempDir, "draft-" + SafeName(bpId) + ".svg");
                if (LocalLibrary.WriteText(file, cover))
                    CoverUrl = "coui://" + LocalLibrary.CoverHost + "/" + Path.GetFileName(file);
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("封面预览: " + ex.GetType().Name); }
        }

        /// <summary>草稿箱只留最近 KEEP_DRAFTS 张，其余整目录删掉（临时文件不留垃圾）。</summary>
        public static void PruneDrafts()
        {
            try
            {
                LocalLibrary.EnsureDirs();
                string root = LocalLibrary.DraftsDir;
                string[] dirs = Directory.GetDirectories(root);
                if (dirs.Length <= KEEP_DRAFTS) return;
                Array.Sort(dirs, (a, b) => Directory.GetLastWriteTimeUtc(a).CompareTo(Directory.GetLastWriteTimeUtc(b)));
                int drop = dirs.Length - KEEP_DRAFTS;
                for (int i = 0; i < drop; i++) DeleteDraft(dirs[i]);
                if (drop > 0) BlueprintHubMod.log.Info("草稿箱清理：" + drop + " 张最旧的已删除");
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("PruneDrafts: " + ex.GetType().Name); }
        }

        private static void DeleteDraft(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (!string.IsNullOrEmpty(CoverUrl) && !string.IsNullOrEmpty(DraftPath) && dir == DraftPath)
                {
                    string f = Path.Combine(LocalLibrary.TempDir, Path.GetFileName(new Uri(CoverUrl).LocalPath));
                    if (File.Exists(f)) File.Delete(f);
                    CoverUrl = string.Empty;
                }
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("删除草稿 " + dir + ": " + ex.GetType().Name); }
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        /// <summary>只留 [A-Za-z0-9-]：任何要进文件名的字符串都先过这一关（防路径穿越）。</summary>
        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length && sb.Length < 64; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-') sb.Append(c);
            }
            return sb.Length == 0 ? "x" : sb.ToString();
        }

        private static bool IsHex(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 16) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        /// <summary>
        /// 作者显示名 = 本机 state/author.json 里那一个，第一次生成蓝图时随机分配（需求 5 的「首次进入分配一串数字」）。
        /// 有意**不**去读 Paradox 显示名：官方只有 async 的 Profile.Get()（research/api-PlatformManager.txt 里没有同步取名的入口），
        /// 而把账号显示名写进公开仓库里每张 meta.json 与「玩家账号不对外展示」相冲。该做的是给玩家一个可改的本机署名，不是抓账号。
        /// </summary>
        public static string AuthorName()
        {
            try
            {
                LocalLibrary.EnsureDirs();
                string file = Path.Combine(LocalLibrary.StateDir, "author.json");
                string text = LocalLibrary.ReadText(file);
                if (!string.IsNullOrEmpty(text))
                {
                    JsonValue v;
                    string err;
                    if (Json.TryParse(text, out v, out err))
                    {
                        string n = v.Str("name");
                        if (n.Length >= 4 && n.Length <= 48) return n;
                    }
                }
                string fresh = BlueprintId.RandomAuthorName(null);
                LocalLibrary.WriteText(file, "{\"schemaVersion\":1,\"name\":\"" + fresh + "\"}\n");
                return fresh;
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("author name: " + ex.GetType().Name);
                return BlueprintId.RandomAuthorName(null);
            }
        }

        private static void Bump()
        {
            BlueprintHubMod.StateEpoch++;
            BlueprintHubUISystem.BumpIfAny();
        }
    }
}
