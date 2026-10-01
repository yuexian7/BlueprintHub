using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 蓝图身份。格式 <c>b{GUID小写}-{作者键}</c>，照 Road Builder 的 <c>r{guid}-{steamid}</c> 同形：
    /// 全局唯一、自带作者归属、**一经分配永不复用**（改名换描述都不换 ID，删了也不回收）。
    /// 纯逻辑，离线可测。
    ///
    /// 0.4.0 把尾段从「SteamID64」换成「作者键」= 本机稳定身份的 sha256 前 16 位（IdentityKit.PlayerKey 同算法）。
    /// 换的理由有两条，都是硬的：
    ///  · 官方侧拿不到可靠的 SteamID64 —— FACT：Colossal.PSI.PdxSdk 的 Profile.Get() 只给 Social.DisplayName
    ///    （research/PdxSdkPlatform.cs:1690、1920-1930 那两处就是全部可用字段），而 PlatformManager 只有
    ///    userSpecificPath（research/api-PlatformManager.txt:200）；
    ///  · 需求 5 本来就要「玩家账号不对外展示」，把 SteamID64 编进公开仓库的每个 meta.json 正好违背它。
    /// 键是不可逆哈希、且不含任何账号信息，跨机器同一玩家算出来同一个值 ⇒ 归属仍然稳定，改名也不换 ID。
    /// </summary>
    public static class BlueprintId
    {
        public const char Prefix = 'b';
        public const int GUID_CHARS = 36;     // 8-4-4-4-12 + 4 个连字符
        public const int AUTHOR_KEY_CHARS = 16;

        public static string New(Guid g, string authorKey)
        {
            return Prefix + g.ToString("D").ToLowerInvariant() + "-" + (authorKey ?? string.Empty);
        }

        /// <summary>校验：前缀、GUID 段形状、尾段必须是 16 位小写十六进制的作者键。</summary>
        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != Prefix) return false;
            string body = id.Substring(1);
            int dash = body.LastIndexOf('-');
            if (dash != GUID_CHARS) return false;
            if (!IsHexGuid(body.Substring(0, dash))) return false;
            string tail = body.Substring(dash + 1);
            if (tail.Length != AUTHOR_KEY_CHARS) return false;
            for (int i = 0; i < tail.Length; i++)
            {
                char c = tail[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>作者键本身也要能单独校验（schema 的 authorId 与 ID 尾段是同一个东西）。</summary>
        public static bool IsAuthorKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length != AUTHOR_KEY_CHARS) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
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

        /// <summary>
        /// 字节 → sha16（sha256 前 8 字节的小写 hex）。**全模组只有这一处算分节哈希**：
        /// 上传侧算它、下载侧校验它、作者键也算它，任何第二份实现都是未来的对不上号。
        /// </summary>
        public static string Hash16(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
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
