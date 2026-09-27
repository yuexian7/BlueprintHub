using System;
using System.Globalization;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 蓝图身份。格式 <c>b{GUID小写}-{作者SteamID64}</c>，照 Road Builder 的 <c>r{guid}-{steamid}</c> 同形：
    /// 全局唯一、自带作者归属、**一经分配永不复用**（改名换描述都不换 ID，删了也不回收）。
    /// 纯逻辑，离线可测。
    /// </summary>
    public static class BlueprintId
    {
        public const char Prefix = 'b';
        public const int GUID_CHARS = 36;     // 8-4-4-4-12 + 4 个连字符

        public static string New(Guid g, string steamId64)
        {
            return Prefix + g.ToString("D").ToLowerInvariant() + "-" + (steamId64 ?? string.Empty);
        }

        /// <summary>校验：前缀、GUID 段形状、尾段必须是 16~20 位数字（SteamID64）。</summary>
        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != Prefix) return false;
            string body = id.Substring(1);
            int dash = body.LastIndexOf('-');
            if (dash != GUID_CHARS) return false;
            if (!IsHexGuid(body.Substring(0, dash))) return false;
            string tail = body.Substring(dash + 1);
            if (tail.Length < 16 || tail.Length > 20) return false;
            for (int i = 0; i < tail.Length; i++)
            {
                char c = tail[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        public static string AuthorOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            int dash = id.LastIndexOf('-');
            return dash < 0 ? string.Empty : id.Substring(dash + 1);
        }

        /// <summary>仓库内相对路径。authorId 直接取自 ID 尾段，两者不许各说各话。</summary>
        public static string MetaPath(string id)
        {
            return "blueprints/" + AuthorOf(id) + "/" + id + "/meta.json";
        }

        public static string PreviewPath(string id, string previewRelative)
        {
            string r = (previewRelative ?? string.Empty).Replace('\\', '/');
            if (r.StartsWith("preview/", StringComparison.Ordinal)) r = r.Substring("preview/".Length);
            return "blueprints/" + AuthorOf(id) + "/" + id + "/preview/" + r;
        }

        public static string BlobPath(string sha16)
        {
            return "blob/" + (sha16 ?? string.Empty) + ".bin";
        }

        private static bool IsHexGuid(string s)
        {
            if (s.Length != GUID_CHARS) return false;
            int[] groups = { 8, 4, 4, 4, 12 };
            int pos = 0;
            for (int g = 0; g < groups.Length; g++)
            {
                for (int i = 0; i < groups[g]; i++)
                {
                    if (!Uri.IsHexDigit(s[pos])) return false;
                    pos++;
                }
                if (g < groups.Length - 1)
                {
                    if (s[pos] != '-') return false;
                    pos++;
                }
            }
            return true;
        }

        /// <summary>
        /// 分节哈希的前 16 位（sha256 hex 前 16 字符 = 64 bit）。
        /// 64 bit 的随机碰撞概率在 10^6 个分节级别仍低于 10^-8，够用且文件名短。
        /// </summary>
        public static string ShortenHash(string fullHexSha256)
        {
            if (string.IsNullOrEmpty(fullHexSha256)) return string.Empty;
            string s = fullHexSha256.Trim().ToLowerInvariant();
            return s.Length <= 16 ? s : s.Substring(0, 16);
        }

        /// <summary>随机作者名（需求 5：首次进入模组默认分配一串随机数字，可随时改）。</summary>
        public static string RandomAuthorName(Random rnd)
        {
            Random r = rnd ?? new Random();
            long n;
            do { n = (long)(r.NextDouble() * 9000000000L) + 1000000000L; } while (false);
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
