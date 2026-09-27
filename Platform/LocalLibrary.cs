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
    }
}
