using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BlueprintHub.Platform
{
    /// <summary>
    /// 本模组的落盘位置。两条硬约束（都踩过坑）：
    ///  · 运行时数据**绝不写进 Mods\\&lt;模组名&gt;\\** —— DeployWIP 每次构建 RemoveDir 整个部署目录（Playbook 步骤 2）
    ///  · 目录口径照 Road Builder 的 FoldersUtil：kUserDataPath/ModsData/&lt;mod&gt;，Temp 用 kTempDataPath
    ///    这里用 UnityEngine.Application.persistentDataPath（与 ToolModeMemory 同口径，少引一个程序集）
    /// </summary>
    public static class LocalLibrary
    {
        public const string MOD_DIR = "BlueprintHub";

        /// <summary>封面的 coui:// 虚拟主机名（全小写，与 Cohtml 的 host 注册口径一致）。</summary>
        public const string CoverHost = "blueprinthubcovers";

        public static string Root { get { return Path.Combine(Application.persistentDataPath, "ModsData", MOD_DIR); } }

        /// <summary>下载下来的蓝图 meta.json 与已保存蓝图（需求 5 的「保存的蓝图」）。</summary>
        public static string ContentDir { get { return Path.Combine(Root, "content"); } }

        /// <summary>内容寻址的分节缓存 blob/&lt;sha16&gt;.bin（不可变 → 永久命中）。</summary>
        public static string BlobCacheDir { get { return Path.Combine(Root, "cache"); } }

        /// <summary>预览图临时目录：注册成 Cohtml host location 给面板用（照 Road Builder 的 thumbnails）。</summary>
        public static string TempDir { get { return Path.Combine(Path.GetTempPath(), "BlueprintHub"); } }

        /// <summary>玩家本地计数（点赞/下载只加一次）与草稿箱。</summary>
        public static string StateDir { get { return Path.Combine(Root, "state"); } }

        public static string DraftsDir { get { return Path.Combine(Root, "drafts"); } }

        public static void EnsureDirs()
        {
            TryMk(ContentDir); TryMk(BlobCacheDir); TryMk(StateDir); TryMk(DraftsDir); TryMk(TempDir);
        }

        private static void TryMk(string dir)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { BlueprintHubMod.log.Warn("mkdir " + dir + " failed: " + ex.GetType().Name); }
        }

        /// <summary>退出 / 落盘入口：把内存态计数写盘（votes.json，原子替换见 WriteText）。</summary>
        public static void Flush(string reason)
        {
            EnsureDirs();
            try
            {
                Workshop.CatalogService cs = Workshop.CatalogService.Instance;
                if (cs != null && cs.VoteBook != null) cs.VoteBook.SaveIfDirty();
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("Flush(" + reason + ") votes: " + ex.GetType().Name); }
        }

        public static string ReadText(string file)
        {
            try
            {
                if (!File.Exists(file)) return string.Empty;
                return File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("read " + file + ": " + ex.GetType().Name);
                return string.Empty;   // 缺键安全：空串而不是 null（Playbook §3.2 那条 null 炸 SceneFlow 的教训）
            }
        }

        public static bool WriteText(string file, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, text ?? string.Empty, new UTF8Encoding(false));   // 无 BOM
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
                return true;
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("write " + file + ": " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>二进制同口径的原子写（头像 PNG、封面、导出的 blob 都用它）：先 .tmp 再替换，不留半截文件。</summary>
        public static bool WriteBytes(string file, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllBytes(tmp, data ?? new byte[0]);
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
                return true;
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("write " + file + ": " + ex.GetType().Name + " " + ex.Message);
                TryDeleteTmp(file);
                return false;
            }
        }

        static void TryDeleteTmp(string file)
        {
            try { string tmp = file + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        // ---------------- 临时与缓存的生命周期（作者 0.4.0 点名：临时文件要自动清理）----------------

        /// <summary>封面/头像/预览这些可再生物：最多留这么多个、这么大字节。</summary>
        public const int TEMP_KEEP_FILES = 96;
        public const long TEMP_KEEP_BYTES = 24L * 1024L * 1024L;

        /// <summary>内容寻址的分节缓存：一张蓝图最多五节，240 个 ≈ 几十张蓝图的分节量。</summary>
        public const int BLOB_KEEP_FILES = 240;
        public const long BLOB_KEEP_BYTES = 160L * 1024L * 1024L;

        /// <summary>TempDir 是本模组私有目录（%TEMP%\BlueprintHub），里面的东西全是可再生缓存 → 开机直接清空。</summary>
        public static int PurgeTemp(string reason)
        {
            return Prune(TempDir, 0, 0L, reason);
        }

        /// <summary>关面板 / 定期：封面缓存按「最新 N 个 + 总字节上限」收敛，超出部分删掉。</summary>
        public static int PruneTemp(int keepFiles, long keepBytes)
        {
            return Prune(TempDir, keepFiles, keepBytes, "temp");
        }

        /// <summary>同上，但 pined 里点名的文件一定留下（头像、当前草稿预览：删了不会自己长回来）。</summary>
        public static int PruneTemp(System.Collections.Generic.HashSet<string> keepNames, int keepFiles, long keepBytes)
        {
            return Prune(TempDir, keepFiles, keepBytes, "temp", keepNames);
        }

        /// <summary>内容寻址的 blob 缓存同理，不能无限长（13 MB 一张的话 200 个就是 2.6 GB）。</summary>
        public static int PruneBlobs(int keepFiles, long keepBytes)
        {
            return Prune(BlobCacheDir, keepFiles, keepBytes, "blob cache");
        }

        /// <summary>
        /// 按最后写入时间从新到旧排序，留下 keepFiles 个且累计不超过 keepBytes，其余删除。
        /// keepFiles=0 表示全删。只删本模组自己目录里的普通文件，不递归进别的用途目录，
        /// 并且逐个 try —— 被游戏占着读的文件删不掉就跳过，绝不抛出去。
        /// </summary>
        static int Prune(string dir, int keepFiles, long keepBytes, string reason, System.Collections.Generic.HashSet<string> keepNames = null)
        {
            int deleted = 0;
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
                DirectoryInfo di = new DirectoryInfo(dir);
                FileInfo[] files = di.GetFiles("*.*", SearchOption.AllDirectories);
                System.Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                int keptCount = 0;
                long keptBytes = 0L;
                foreach (FileInfo f in files)
                {
                    // .tmp 是写坏了的残留，一律先清；其余按「最新 keepFiles 个且总字节不超 keepBytes」保留
                    bool stale = f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase);
                    bool pinned = !stale && keepNames != null && keepNames.Contains(f.Name);
                    if (!stale && (pinned || (keptCount < keepFiles && keptBytes + f.Length <= keepBytes)))
                    {
                        keptCount++;
                        keptBytes += f.Length;
                        continue;
                    }
                    try { f.Delete(); deleted++; }
                    catch { keptCount++; }   // 被游戏占着删不掉：算进保留名额，免得每轮都去啃同一个文件
                }
            }
            catch (Exception ex) { BlueprintHubMod.log.Warn("prune " + reason + ": " + ex.GetType().Name); }
            if (deleted > 0) BlueprintHubMod.log.Info("清理 " + reason + "：删掉 " + deleted + " 个缓存文件");
            return deleted;
        }
    }
}
