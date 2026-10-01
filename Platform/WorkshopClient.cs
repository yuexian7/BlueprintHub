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
        // GitHub 对带 UA 的请求更宽容，也是排障时的身份标识。版本号跟着模组走，不许写死（写死过一次：0.2/0.3 的包还在报 0.1.0）
        private static readonly string USER_AGENT = "BlueprintHub/" + BlueprintHubMod.kVersion + " (Cities Skylines II mod)";

        /// <summary>整个进程共用一个 client；超时收紧到秒级，别用默认 100 秒。</summary>
        private static readonly HttpClient s_http = CreateClient();

        /// <summary>
        /// 仓库根址（index.json 的 mirrors 会覆盖它；这里是「第一次拉 index」时的兜底顺序）。
        /// 顺序按 2026-09-27 本机实测排：Pages 0.59s → jsDelivr 2.0s → raw 直接超时（000）。
        /// raw 从大陆网络经常整段不通，把它放第一位 = 每次冷启动白等一个 12s 超时，
        /// 而「网络上不能慢」是硬约束，所以它退到第二。
        /// </summary>
        public static string[] Mirrors =
        {
            "https://yuexian7.github.io/blueprinthub-workshop/",
            "https://raw.githubusercontent.com/yuexian7/blueprinthub-workshop/main/",
            "https://cdn.jsdelivr.net/gh/yuexian7/blueprinthub-workshop@main/",
        };

        private static int s_mirror;              // 当前胜出的镜像下标，探测后固定
        private static int s_DevLogged;           // 开发覆盖只播报一次，别刷屏
        private static volatile bool s_probed;    // 首帧并发探路只做一次
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

        /// <summary>给错误提示用的一行镜像名单（只取主机名，语言无关）。</summary>
        public static string MirrorList()
        {
            string[] arr;
            lock (s_gate) arr = Mirrors;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(HostOf(arr[i]));
            }
            return sb.ToString();
        }

        /// <summary>https://host/path → host（拿不到就原样返回，别在错误提示里再抛一次）。</summary>
        public static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            int p = url.IndexOf("://", StringComparison.Ordinal);
            string rest = p < 0 ? url : url.Substring(p + 3);
            int slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }

        /// <summary>开发覆盖是否生效（玩家机器上没有 dev-catalog 目录 → 恒 false）。</summary>
        public static bool UsingDevCatalog { get { return s_DevActive; } }
        private static volatile bool s_DevActive;

        /// <summary>请求次数上限（含降级换镜像）：3 个镜像各试一次。</summary>
        public const int MAX_ATTEMPTS = 3;

        /// <summary>
        /// 冷启动探路：三条镜像并发各拉一次 index.json，谁先回来（且 200）就把 s_mirror 钉在谁身上。
        /// 这是 CatalogKit.NextMirror 注释里承诺过的「探一次取最快」那一半的实现（另一半是失败轮转）。
        /// 为什么要：2026-09-27 本机实测 Pages 0.59s / jsDelivr 2.0s / raw 超时不通 ——
        /// 固定顺序必然有人每次多等好几秒，而「网络上不能慢」是硬约束。
        /// 探路失败不报错：照常走轮转，只是慢一次。
        /// </summary>
        public static async Task<byte[]> ProbeMirrorsAsync(string relativeRepoPath, CancellationToken token)
        {
            if (s_probed) return null;
            string[] mirrors;
            lock (s_gate) mirrors = Mirrors;
            if (mirrors.Length == 0) return null;

            var tasks = new Task<byte[]>[mirrors.Length];
            for (int i = 0; i < mirrors.Length; i++) tasks[i] = ProbeOneAsync(mirrors[i], relativeRepoPath, token);
            byte[] winner = null;
            int winIdx = -1;
            var pending = new List<Task<byte[]>>(tasks);
            while (pending.Count > 0)
            {
                Task<byte[]> done = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(done);
                byte[] r = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
                if (r != null && r.Length > 0) { winner = r; winIdx = Array.IndexOf(tasks, done); break; }
            }
            if (winner == null) return null;               // 三条都不通：留给正式请求去降级
            lock (s_gate) s_mirror = winIdx;
            s_probed = true;
            BlueprintHubMod.log.Info("镜像探路：" + mirrors[winIdx] + " 最快");
            return winner;
        }

        /// <summary>
        /// index.json 专用：没探路过就顺手把探路结果当返回值用掉（省一次请求），探过就走正常轮转。
        /// </summary>
        public static async Task<string> GetIndexTextAsync(string relativeRepoPath, CancellationToken token)
        {
            if (!s_probed)
            {
                byte[] probed = await ProbeMirrorsAsync(relativeRepoPath, token).ConfigureAwait(false);
                if (probed != null && probed.Length > 0)
                {
                    try { return Encoding.UTF8.GetString(probed); } catch { }
                }
            }
            return await GetTextAsync(relativeRepoPath, token).ConfigureAwait(false);
        }

        private static async Task<byte[]> ProbeOneAsync(string mirror, string rel, CancellationToken token)
        {
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));         // 探路预算比正式请求短
                    using (HttpResponseMessage resp = await s_http.GetAsync(CatalogKit.BuildUrl(mirror, rel), cts.Token)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }
            catch { return null; }
        }

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
            s_DevActive = true;
            if (Interlocked.CompareExchange(ref s_DevLogged, 1, 0) == 0)
                BlueprintHubMod.log.Warn("开发覆盖生效：读本地 dev-catalog，不访问网络（" + DevCatalogDir + "）");
            try { return File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("dev-catalog " + rel + ": " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>分节哈希：算法只有一份，在 Bpc/BlueprintId.Hash16（纯逻辑层，t3 直接测它）。这里只是历史调用名。</summary>
        public static string ShortHashOf(byte[] data)
        {
            return BlueprintId.Hash16(data);
        }
    }


}
