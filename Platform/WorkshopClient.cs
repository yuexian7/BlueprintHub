using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlueprintHub.Bpc;

namespace BlueprintHub.Platform
{
    /// <summary>
    /// 工坊只读数据面客户端：三镜像 + 显式超时 + 内容哈希自校验。
    /// 与 Road Builder 的 ApiUtilBase 相比刻意改掉三处（Playbook §3.5 与硬规则 16）：
    ///  ① HttpClient 全局复用（它每次请求 new 一个 → 端口/连接耗尽）
    ///  ② 显式 Timeout + CancellationToken（它一个都没设 → 退出游戏时点了没反应）
    ///  ③ 瞬时失败与永久拒绝分开（它只 Warn ReasonPhrase）
    /// </summary>
    public static class WorkshopClient
    {
        // GitHub 对带 UA 的请求更宽容，也是排障时的身份标识
        private const string USER_AGENT = "BlueprintHub/0.1.0 (Cities Skylines II mod)";

        /// <summary>整个进程共用一个 client；超时收紧到秒级，别用默认 100 秒。</summary>
        private static readonly HttpClient s_http = CreateClient();

        /// <summary>仓库根址（index.json 的 mirrors 覆盖它；这里是兜底默认）。</summary>
        public static string[] Mirrors =
        {
            "https://raw.githubusercontent.com/yuexian7/blueprinthub-workshop/main/",
            "https://yuexian7.github.io/blueprinthub-workshop/",
            "https://cdn.jsdelivr.net/gh/yuexian7/blueprinthub-workshop@main/",
        };

        private static int s_mirror;              // 当前胜出的镜像下标，探测后固定
        private static int s_DevLogged;           // 开发覆盖只播报一次，别刷屏
        private static readonly object s_gate = new object();

        private static HttpClientHandler CreateHandler()
        {
            var h = new HttpClientHandler();
            try
            {
                h.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }
            catch { }
            return h;
        }

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(CreateHandler());
            c.Timeout = TimeSpan.FromSeconds(12);            // 收紧，退出时最坏等 12 秒
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            return c;
        }

        /// <summary>
        /// index.json 的 mirrors 到手后覆盖默认址（FACT：仓库里那份的三条顺序 = raw → Pages → jsDelivr，
        /// 中国大陆 jsDelivr 有 DNS 污染史所以只当兜底）。空列表忽略，别让面板从此请求不到东西。
        /// </summary>
        public static void ApplyMirrors(System.Collections.Generic.IEnumerable<string> urls)
        {
            if (urls == null) return;
            var list = new System.Collections.Generic.List<string>();
            foreach (string u in urls) if (!string.IsNullOrEmpty(u)) list.Add(u);
            if (list.Count == 0) return;
            lock (s_gate)
            {
                Mirrors = list.ToArray();
                s_mirror = 0;
            }
        }

        public static int MirrorCount { get { lock (s_gate) return Mirrors.Length; } }
        public static int ActiveMirror { get { lock (s_gate) return s_mirror; } }

        /// <summary>请求次数上限（含降级换镜像）：3 个镜像各试一次。</summary>
        public const int MAX_ATTEMPTS = 3;

        public static async Task<string> GetTextAsync(string relativeRepoPath, CancellationToken token)
        {
            byte[] raw = await GetBytesAsync(relativeRepoPath, token).ConfigureAwait(false);
            if (raw == null) return null;
            try { return Encoding.UTF8.GetString(raw); }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("decode " + relativeRepoPath + ": " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// 拉一个仓库相对路径的字节。失败返回 null（调用方按「瞬时/永久」决定留队还是放弃）。
        /// 逐镜像降级，顺序按 s_mirror 轮转 —— 探出哪个快就用哪个，坏了自动挪到下一个。
        /// </summary>
        public static async Task<byte[]> GetBytesAsync(string relativeRepoPath, CancellationToken token)
        {
            string rel = (relativeRepoPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0) return null;

            // 开发覆盖优先（玩家机器上没这个目录，见 DevCatalogDir 注释）
            string dev = DevFile(rel);
            if (dev != null) return DevRead(dev, rel);

            string[] mirrors;
            int start;
            lock (s_gate) { start = s_mirror; mirrors = Mirrors; }
            int attempts = mirrors.Length < MAX_ATTEMPTS ? mirrors.Length : MAX_ATTEMPTS;
            if (attempts <= 0) attempts = 1;
            int lastMiss = 0;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int idx = (start + attempt) % mirrors.Length;
                string url = CatalogKit.BuildUrl(mirrors[idx], rel);
                try
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(12));
                        using (HttpResponseMessage resp = await s_http.GetAsync(url, cts.Token).ConfigureAwait(false))
                        {
                            int code = (int)resp.StatusCode;
                            if (resp.IsSuccessStatusCode)
                            {
                                byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                lock (s_gate) { s_mirror = idx; }              // 记住这次赢的镜像
                                return bytes;
                            }
                            if (code == 404)
                            {
                                // 404 要逐镜像问一遍：Pages 可能还没部署完、jsDelivr 可能命中旧缓存，
                                // 三条都 404 才算真没有（返回 null，调用方按「永久」处理，不会自动重试）。
                                lastMiss = 404;
                                BlueprintHubMod.log.Warn("404 镜像#" + idx + " " + rel);
                            }
                            else if (CatalogKit.IsPermanentStatus(code))
                            {
                                BlueprintHubMod.log.Warn("永久拒绝 " + code + " " + rel);
                                return null;                            // 403/401/400 换镜像没意义，一次都不许多试
                            }
                            else
                            {
                                BlueprintHubMod.log.Warn("瞬时失败 " + code + " 镜像#" + idx + " " + rel);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return null;             // 玩家关了面板/退出游戏
                    BlueprintHubMod.log.Warn("超时 镜像#" + idx + " " + rel);
                }
                catch (Exception ex)
                {
                    BlueprintHubMod.log.Warn("请求异常 镜像#" + idx + " " + rel + " " + ex.GetType().Name);
                }
                // 404 是立刻回来的，不必退避；瞬时失败才需要给镜像喘息
                if (attempt + 1 < attempts && lastMiss != 404)
                {
                    await Task.Delay(CatalogKit.BackoffMs(attempt + 1), token).ConfigureAwait(false);
                }
            }
            if (lastMiss == 404) BlueprintHubMod.log.Info("三条镜像都 404：" + rel);
            return null;
        }

        /// <summary>
         /// 下载一个分节并**按文件名哈希复核**：镜像脏了宁可失败也不落缓存。
         /// 已存在且哈希一致 → 直接返回本地路径（0 网络请求，第二次套用的关键）。
         /// </summary>
        public static async Task<string> GetSectionAsync(string sha16, long expectedBytes, CancellationToken token)
        {
            if (string.IsNullOrEmpty(sha16)) return string.Empty;            // 空节
            string cache = Path.Combine(LocalLibrary.BlobCacheDir, sha16 + ".bin");
            if (File.Exists(cache))
            {
                var fi = new FileInfo(cache);
                if (fi.Length == expectedBytes && ShortHashOf(cache) == sha16) return cache;
                try { File.Delete(cache); } catch { }                          // 脏缓存：删掉重下
            }

            byte[] raw = await GetBytesAsync(BlueprintId.BlobPath(sha16), token).ConfigureAwait(false);
            if (raw == null) return null;
            string actual = ShortHashOf(raw);
            if (actual != sha16)
            {
                BlueprintHubMod.log.Warn("分节哈希不符，丢弃：" + sha16 + " != " + actual);
                return null;
            }
            if (expectedBytes > 0 && raw.LongLength != expectedBytes)
            {
                BlueprintHubMod.log.Warn("分节字节数不符，丢弃：" + sha16);
                return null;
            }
            try
            {
                Directory.CreateDirectory(LocalLibrary.BlobCacheDir);
                File.WriteAllBytes(cache, raw);
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("写缓存失败 " + sha16 + ": " + ex.GetType().Name);
                return null;
            }
            return cache;
        }

        public static string ShortHashOf(string file)
        {
            try { return ShortHashOf(File.ReadAllBytes(file)); }
            catch { return string.Empty; }
        }

        /// <summary>
        /// 开发覆盖：本地存在 ModsData\BlueprintHub\dev-catalog\&lt;仓库相对路径&gt; 时优先读它，完全不碰网络。
        /// 只影响开发者自己（玩家机器上没这个目录）。用途：库里还没有蓝图时也能测卡片渲染、分页、封面、搜索与排序。
        /// 生成工具：tools/seed-dev-catalog.mjs（同一步也用来复现「镜像返回坏数据」这类场景）。
        /// </summary>
        public static string DevCatalogDir { get { return Path.Combine(LocalLibrary.Root, "dev-catalog"); } }

        private static string DevFile(string rel)
        {
            try
            {
                string root = Path.GetFullPath(DevCatalogDir);
                if (!Directory.Exists(root)) return null;
                string cand = Path.GetFullPath(Path.Combine(root, rel));
                // 只允许落在 dev-catalog 里面：拼出来的绝对路径必须在根下（挡 .. 跳出）
                if (!cand.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(cand) ? cand : null;
            }
            catch { return null; }
        }

        private static byte[] DevRead(string file, string rel)
        {
            if (Interlocked.CompareExchange(ref s_DevLogged, 1, 0) == 0)
                BlueprintHubMod.log.Warn("开发覆盖生效：读本地 dev-catalog，不访问网络（" + DevCatalogDir + "）");
            try { return File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("dev-catalog " + rel + ": " + ex.GetType().Name);
                return null;
            }
        }

        public static string ShortHashOf(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }


}
