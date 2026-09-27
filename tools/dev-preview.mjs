/**
 * 静态预览台（只给开发用，不进模组产物）：
 *   node tools/dev-preview.mjs        → http://127.0.0.1:8765/
 * 作用：在没有游戏的情况下把 UI/dist/BlueprintHub.mjs 真的跑起来，检查布局/配色/四态/交互流转。
 * Cohtml 与 Chrome 仍有差别（字体、rem 基准、backdrop-filter、图片缓存），
 * 所以「预览台里对」不等于「游戏里对」；它的价值是让明显画坏的东西在 5 秒内暴露，而不是等一次游戏启动。
 *
 * 数据源：本机 dev-catalog（node tools/seed-dev-catalog.mjs 生成）。没有就退化成一条内置示例。
 * 状态包字段与 Systems/UI/BlueprintHubUISystem.cs 的 Build() 同形；改了那边记得改这里。
 *
 * 可选参数：BPH_PREVIEW_CATALOG=<dev-catalog 路径>  PORT=8765
 */
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, "..");
const UI = path.join(ROOT, "UI");
const NM = path.join(UI, "node_modules");
// persistentDataPath 口径与 Platform/LocalLibrary.cs 一致（LocalLow，不是 LocalAppData）
const CAT = process.env.BPH_PREVIEW_CATALOG
  || path.join(os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
    "ModsData", "BlueprintHub", "dev-catalog");
const PORT = Number(process.env.PORT || 8765);

const TILE_M2 = 388129;
const CAT_IDS = ["residential", "commercial", "industrial", "park", "education", "public", "mixed"];
const CAT_ZH = ["住宅区", "商业区", "产业区", "公园区", "文教区", "公共区", "混合区"];
const CAT_DEF = [
  "以居民住宅为主体，低密度商业/办公混合的社区",
  "以商业街区、中大型商场或商业/办公高楼等为主体的社区",
  "以产业设施、货运设施、资源设施或仓储等为主体的社区",
  "以公园绿化、开放空间或景点建筑等为主的社区",
  "以教育建筑、科研机构等为主体的社区",
  "以客运设施、政府机构、公共服务设施等为主体的社区",
  "多种功能高度融合、无法区分主体的社区",
];
const SORTS = [["weekly", "周热度"], ["total", "总热度"], ["uploadTime", "上传时间"], ["area", "面积"], ["name", "名称"]];
const AREAS = [["all", "全部"], ["small", "小 · ≤2 区块"], ["medium", "中 · 3~9 区块"], ["large", "大 · >9 区块"]];

const readIf = (p) => { try { return fs.readFileSync(p, "utf8"); } catch { return null; } };
const hasCatalog = fs.existsSync(path.join(CAT, "catalog", "index.json"));
const INDEX = hasCatalog ? JSON.parse(readIf(path.join(CAT, "catalog", "index.json"))) : null;
const ALL = (() => {
  if (!INDEX) return null;
  const out = [];
  for (let p = 1; p <= Math.max(1, INDEX.allPages ?? 1); p++) {
    const raw = readIf(path.join(CAT, "catalog", "all", `p${p}.json`));
    if (!raw) break;
    out.push(...(JSON.parse(raw).items || []));
  }
  return out;
})();

const fmt = (n) => (n < 10000 ? String(n) : n < 1e6 ? (n / 1000).toFixed(1).replace(/\.0$/, "") + "k" : (n / 1e6).toFixed(1) + "M");
// 与 Bpc/BrowseKit.cs 的 AreaTextZh / AreaTextFull / TilesLabel 同口径（改了那边要改这里）
const tilesLabel = (m2) => {
  const t = m2 / TILE_M2;
  if (t >= 100) return t.toFixed(0) + " 区块";
  return (Math.abs(t - Math.round(t)) < 0.05 ? String(Math.round(t)) : t.toFixed(1)) + " 区块";
};
const areaText = (m2) => `${tilesLabel(m2)} · ${m2 >= 10000 ? (m2 / 10000).toFixed(m2 % 10000 === 0 ? 0 : 1) + "万㎡" : m2.toLocaleString("en-US") + "㎡"}`;
const areaFull = (m2) => `${tilesLabel(m2)} · ${m2.toLocaleString("en-US")} ㎡`;
const relTime = (iso) => {
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 6e4) return "刚刚";
  const min = ms / 6e4, h = min / 60, d = h / 24;
  if (h < 1) return `${Math.floor(min)} 分钟前`;
  if (d < 1) return `${Math.floor(h)} 小时前`;
  if (d < 30) return `${Math.floor(d)} 天前`;
  if (d < 365) return `${Math.floor(d / 30)} 个月前`;
  return `${Math.floor(d / 365)} 年前`;
};

// ---------------- 假 C# 侧：一条 GetState + 一条 Cmd ----------------
const session = { category: "", search: "", area: "all", sort: "weekly", page: 1, forceStatus: "", votes: new Set() };

function buildState() {
  let pool = ALL ? [...ALL] : [];
  if (!ALL) {
    pool = [{
      id: "b0a7f3c2-1111-2222-3333-444455556666-76561198000001234", author: "76561198000001234",
      authorName: "阿明", name: "内置示例（没有 dev-catalog）", categories: ["park"], areaClass: "medium",
      areaM2: 4 * TILE_M2, likes: 12, downloads: 34, hotTotal: 114, hotWeekly: 40,
      updatedAt: new Date(Date.now() - 3 * 864e5).toISOString(), cover: "", assetCount: 41, desc: "示例描述",
    }];
  }
  if (session.category) pool = pool.filter((x) => (x.categories || []).includes(session.category));
  if (session.area !== "all") pool = pool.filter((x) => x.areaClass === session.area);
  const q = session.search.trim().toLowerCase();
  if (q) {
    const score = (x) => {
      const n = (x.name || "").toLowerCase();
      if (n === q) return 1000; if (n.startsWith(q)) return 600; if (n.includes(q)) return 400;
      if ((x.authorName || "").toLowerCase().includes(q)) return 250;
      if ((x.desc || "").toLowerCase().includes(q)) return 100;
      return null;
    };
    pool = pool.map((x) => [score(x), x]).filter(([s]) => s !== null)
      .sort((a, b) => b[0] - a[0] || b[1].hotWeekly - a[1].hotWeekly).map(([, x]) => x);
  }
  pool.sort({
    weekly: (a, b) => b.hotWeekly - a.hotWeekly || b.hotTotal - a.hotTotal,
    total: (a, b) => b.hotTotal - a.hotTotal,
    uploadTime: (a, b) => String(b.updatedAt).localeCompare(String(a.updatedAt)),
    area: (a, b) => b.areaM2 - a.areaM2,
    name: (a, b) => String(a.name).localeCompare(String(b.name), "en"),
  }[session.sort] || ((a, b) => b.hotWeekly - a.hotWeekly));

  const total = pool.length;
  const pages = Math.ceil(total / 15);
  const page = Math.max(1, Math.min(session.page, pages || 1));
  const slice = pool.slice((page - 1) * 15, page * 15).map((it) => {
    const liked = session.votes.has("L" + it.id), used = session.votes.has("D" + it.id);
    const likes = it.likes + (liked ? 1 : 0), downloads = it.downloads + (used ? 1 : 0);
    return {
      id: it.id, name: it.name, author: it.authorName || "匿名玩家", authorId: it.author,
      cover: it.cover ? "files/" + it.cover : "",
      likes, downloads, likesText: fmt(likes), downloadsText: fmt(downloads), liked, used,
      areaM2: it.areaM2, areaText: areaText(it.areaM2), areaFull: areaFull(it.areaM2), areaClass: it.areaClass,
      areaClassLabel: { small: "小", medium: "中", large: "大" }[it.areaClass] || "全部",
      updated: relTime(it.updatedAt), assets: it.assetCount, desc: it.desc || "",
      categories: (it.categories || []).map((c) => CAT_ZH[CAT_IDS.indexOf(c)] || c).join(" / "),
      cats: (it.categories || []).join(","),
    };
  });

  let status = session.forceStatus || (total === 0 ? (ALL && INDEX.total === 0 ? "empty" : "noresult") : "ready");
  if (status === "error") session.forceStatus = "";   // 一次性的：点重试就恢复
  const idx = ALL ? ALL.length : 1;
  return {
    seq: Date.now(), visible: true, opacity: 0.5, modVersion: "0.1.0", modAuthor: "yuexian",
    title: "蓝图工坊", hint: "分享蓝图请点击市辖区面板的上传按钮", hotkey: "B",
    status,
    statusText: status === "empty" ? "工坊还没有蓝图。"
      : status === "error" ? "连不上工坊（三个镜像都没响应）。检查网络后点重试。" : "",
    subtitle: session.category
      ? CAT_ZH[CAT_IDS.indexOf(session.category)] + " · " + CAT_DEF[CAT_IDS.indexOf(session.category)] : "",
    truncated: false, playerKey: "set",
    categories: CAT_IDS.map((id, i) => ({
      id, label: CAT_ZH[i], definition: CAT_DEF[i],
      count: ALL ? ALL.filter((x) => (x.categories || []).includes(id)).length : (INDEX?.categories?.[i]?.pages ?? 0) * 15,
      selected: session.category === id,
    })),
    menu: {
      tileHint: "1 区块 ≈ 388,129 ㎡", search: session.search, area: session.area,
      areaLabel: (AREAS.find((a) => a[0] === session.area) || AREAS[0])[1],
      areas: AREAS.map(([x, y]) => ({ id: x, label: y })),
      sort: session.sort, sortLabel: (SORTS.find((s) => s[0] === session.sort) || SORTS[0])[1],
      sorts: SORTS.map(([x, y]) => ({ id: x, label: y })),
      page, pages, total,   // 与 C# 一致：这里报的是「当前条件下命中数」，不是库内总数
      libraryTotal: idx, pageSize: 15,
    },
    items: status === "ready" ? slice : [],
  };
}

function onCmd(raw) {
  const i = String(raw).indexOf("|");
  const kind = i < 0 ? raw : raw.slice(0, i);
  const arg = i < 0 ? "" : raw.slice(i + 1);
  if (kind === "cat") { session.category = session.category === arg ? "" : arg; session.page = 1; }
  else if (kind === "search") { session.search = arg; session.page = 1; }
  else if (kind === "area") { session.area = arg; session.page = 1; }
  else if (kind === "sort") { session.sort = arg; session.page = 1; }
  else if (kind === "page") session.page = Number(arg) || 1;
  else if (kind === "retry") session.forceStatus = "";
  else if (kind === "like" || kind === "dl") session.votes.add((kind === "like" ? "L" : "D") + arg);
  else if (kind === "detail") console.log("  detail ->", arg);
  else if (kind === "close") console.log("  （预览台不关面板，方便一直看）");
  // 预览台专用调试开关：?status=error / ?status=empty
  return buildState();
}

const PAGE = (state) => `<!doctype html><html><head><meta charset="utf-8"><title>BlueprintHub 预览台</title>
<style>
html{font-size:1px}body{margin:0;height:100vh;overflow:hidden;
  background:url('/files/preview-bg.svg') center/cover no-repeat,#0b1118;
  font:14px "Microsoft YaHei","Noto Sans CJK SC",sans-serif}
#game{position:fixed;inset:0}#topleft{position:fixed;left:12px;top:70px;display:flex;z-index:5}
#bar{position:fixed;right:10px;bottom:8px;z-index:9;font:12px monospace;color:#8fa0b0}
#bar a{color:#4fd1c5;cursor:pointer;margin-left:8px}
</style>
<script src="/react.js"></script><script src="/react-dom.js"></script>
<script>
window.__state = ${JSON.stringify(JSON.stringify(state))};   // 字符串！与 GetterValueBinding<string> 同形
// 游戏把 API 挂在 window 的「cs2/api」这种带斜杠的扁平键上（webpack externalsType:window 就是这个形状）
window["cs2/api"] = {
  bindValue: function () { return {}; },
  useValue: function () { return window.__state; },
  trigger: function (g, n, a) { if (g === "BlueprintHub" && n === "Cmd") window.__cmd(a); },
  call: function () { return Promise.resolve("ok"); },
};
window["cs2/modding"] = {};
window["cs2/l10n"] = { useLocalization: function () { return {}; }, Localized: function () { return null; } };
window["cohtml/cohtml"] = { call: function () { return Promise.resolve(null); }, on: function () {}, trigger: function() {} };
window.__cmd = async function (a) {
  const r = await fetch("/cmd?a=" + encodeURIComponent(a));
  window.__state = await r.text();
  window.__rerender();
};
</script></head><body>
<div id="topleft"></div><div id="game"></div>
<div id="bar">预览台 · 只在本机 <a href="/?status=error">看失败态</a><a href="/?status=empty">看空态</a><a href="/">恢复</a></div>
<script type="module">
import register from "/BlueprintHub.mjs";
const roots = { Game: ReactDOM.createRoot(document.getElementById("game")),
                GameTopLeft: ReactDOM.createRoot(document.getElementById("topleft")) };
const comps = {};
register({ append(slot, comp) { comps[slot] = comp; if (roots[slot]) roots[slot].render(React.createElement(comp)); } });
window.__rerender = () => { for (const [slot, comp] of Object.entries(comps)) if (roots[slot]) roots[slot].render(React.createElement(comp)); };
console.log("[preview] module registered, slots =", Object.keys(comps).join(","));
</script></body></html>`;

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8", ".svg": "image/svg+xml", ".css": "text/css", ".json": "application/json", ".png": "image/png" };
const BG = `<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#16212e"/>${Array.from({ length: 14 }, (_, i) => `<g stroke="${i % 3 === 0 ? "#2b3a4a" : "#22303f"}" stroke-width="${i % 3 === 0 ? 7 : 3}"><line x1="0" y1="${i * 28}" x2="640" y2="${i * 28 + 30}"/></g>`).join("")}${Array.from({ length: 9 }, (_, i) => `<line x1="${i * 74}" y1="0" x2="${i * 74 + 20}" y2="360" stroke="#22303f" stroke-width="4"/>`).join("")}</svg>`;

const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://127.0.0.1");
  const send = (body, type) => { res.writeHead(200, { "content-type": type, "cache-control": "no-store" }); res.end(body); };
  if (url.pathname === "/cmd") {
    const raw = decodeURIComponent(url.searchParams.get("a") || "");
    return send(JSON.stringify(onCmd(raw)), MIME[".json"]);
  }
  if (url.pathname === "/" || url.pathname === "/index.html") {
    // 查询参数预置状态：让无头截图能拍到「选中分类 / 第 2 页 / 已点赞 / 失败态」这些分支
    const g = (k, dft) => (url.searchParams.has(k) ? url.searchParams.get(k) : dft);
    session.category = ["", ...CAT_IDS].includes(g("cat", "")) ? g("cat", "") : "";
    session.area = ["all", "small", "medium", "large"].includes(g("area", "all")) ? g("area", "all") : "all";
    session.sort = SORTS.some((s) => s[0] === g("sort", "weekly")) ? g("sort", "weekly") : "weekly";
    session.page = Number(g("page", "1")) || 1;
    session.search = g("q", "");
    session.forceStatus = ["error", "empty", "loading"].includes(url.searchParams.get("status")) ? url.searchParams.get("status") : "";
    const liked = g("like", "");
    if (liked) { session.votes.add("L" + liked); session.votes.add("D" + liked); }
    return send(PAGE(buildState()), MIME[".html"]);
  }
  if (url.pathname === "/react.js") return send(fs.readFileSync(path.join(NM, "react/umd/react.production.min.js")), MIME[".js"]);
  if (url.pathname === "/react-dom.js") return send(fs.readFileSync(path.join(NM, "react-dom/umd/react-dom.production.min.js")), MIME[".js"]);
  if (url.pathname === "/BlueprintHub.mjs") {
    const dist = path.join(UI, "dist/BlueprintHub.mjs");
    if (!fs.existsSync(dist)) return send("console.warn('先跑 npx --prefix UI webpack')", MIME[".js"]);
    return send(fs.readFileSync(dist), MIME[".mjs"]);
  }
  if (url.pathname === "/files/preview-bg.svg") return send(BG, MIME[".svg"]);
  if (url.pathname.startsWith("/files/")) {
    const f = path.join(CAT, url.pathname.slice(7).replace(/\//g, path.sep));
    if (f.startsWith(CAT) && fs.existsSync(f)) return send(fs.readFileSync(f), MIME[path.extname(f)] || "application/octet-stream");
  }
  res.writeHead(404); res.end("nope");
});

server.listen(PORT, "127.0.0.1", () => {
  console.log("BlueprintHub 预览台（只监听本机）： http://127.0.0.1:" + PORT + "/");
  console.log(hasCatalog ? "数据源：dev-catalog  " + CAT : "数据源：内置示例（先跑 node tools/seed-dev-catalog.mjs 会有 18 张）");
  console.log("改了 UI/src 记得：npx --prefix UI webpack   然后刷新页面");
});
