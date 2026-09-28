/**
 * 静态预览台（只给开发用，不进模组产物）：
 *   node tools/dev-preview.mjs        → http://127.0.0.1:8765/
 * 作用：在没有游戏的情况下把 UI/dist/BlueprintHub.mjs 真的跑起来，检查布局/配色/四态/交互流转。
 * Cohtml 与 Chrome 仍有差别（字体、rem 基准、backdrop-filter、图片缓存），
 * 所以「预览台里对」不等于「游戏里对」；它的价值是让明显画坏的东西在 5 秒内暴露，而不是等一次游戏启动。
 *
 * 0.3.0 起这个台子还多担一件事：**冒充游戏的本地化面**。
 *   词典直接从 Locale.cs 的 PanelZh/PanelZhTw/PanelEn 解析出来，键由本文件的 keyOf 生成 ——
 *   如果 UI/src/l10n.ts 的 keyOf 与 Bpc/PanelKeyKit.cs 的 Key 写得不一样，截图里会直接露出 slug，
 *   于是「前后端键名不同源」这种只有实机才看得见的错，在这里就能拍到。
 *
 * 数据源优先级：本机 dev-catalog → 线上工坊（真目录，通常是空的）→ 空态。
 *   ?demo=12 才生成 12 条合成条目，纯粹为了拍布局（这条路径不会进任何产物，也不会写进仓库）。
 *
 * 可选参数：BPH_PREVIEW_CATALOG=<dev-catalog 路径>  PORT=8765
 *   ?lang=zh-HANS|zh-HANT|en-US   ?cat=park  ?area=small  ?sort=name  ?page=2  ?q=xx  ?like=<id>
 *   ?status=loading|error|empty   ?native=0  （native=0 关掉假 cs2/ui，走降级方块）
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
const LIVE = "https://yuexian7.github.io/blueprinthub-workshop/";

const TILE_M2 = 388129;
const CAT_IDS = ["residential", "commercial", "industrial", "park", "education", "public", "mixed"];
const SORT_IDS = ["weekly", "total", "uploadTime", "area", "name"];
const AREA_IDS = ["all", "small", "medium", "large"];

const readIf = (p) => { try { return fs.readFileSync(p, "utf8"); } catch { return null; } };

// ---------------- 词典：从 Locale.cs 现读，杜绝两份文案漂移 ----------------
function panelDict(locale) {
  const src = readIf(path.join(ROOT, "Locale.cs")) || "";
  const fn = /HANT/i.test(locale) ? "PanelZhTw" : (/^zh/i.test(locale) ? "PanelZh" : "PanelEn");
  const start = src.indexOf("Panel(string locale)") >= 0 ? src.indexOf(`Dictionary<string, string> ${fn}()`) : -1;
  if (start < 0) return {};
  const end = src.indexOf("\n        }", start);
  const body = src.slice(start, end < 0 ? start + 20000 : end);
  const re = /\{\s*"([^"]+)"\s*,\s*"((?:\\.|[^"\\])*)"\s*,?\s*\}/g;
  const out = {};
  let m;
  while ((m = re.exec(body))) out[m[1]] = m[2].replace(/\\"/g, '"').replace(/\\u([0-9a-fA-F]{4})/g, (_, h) => String.fromCharCode(parseInt(h, 16)));
  return out;
}
/** 与 Bpc/PanelKeyKit.cs / UI/src/l10n.ts 三处同形（这里再写一遍就是为了当对照面） */
const SETTING_ID = "BlueprintHub.BlueprintHub.BlueprintHubMod";
const keyOf = (slug) => `Options.BLUEPRINT_HUB.${slug}[${SETTING_ID}.Panel.${slug}]`;
const dictFor = (locale) => {
  const d = panelDict(locale);
  const bag = {};
  for (const slug of Object.keys(d)) bag[keyOf(slug)] = d[slug];
  return bag;
};

// ---------------- 数据源：dev-catalog → 线上工坊 → 空 ----------------
const hasCatalog = fs.existsSync(path.join(CAT, "catalog", "index.json"));

function loadDev() {
  if (!hasCatalog) return null;
  try {
    const index = JSON.parse(readIf(path.join(CAT, "catalog", "index.json")));
    const items = [];
    for (let p = 1; p <= Math.max(1, index.allPages ?? 1); p++) {
      const raw = readIf(path.join(CAT, "catalog", "all", `p${p}.json`));
      if (!raw) break;
      items.push(...(JSON.parse(raw).items || []));
    }
    return { index, items, from: "dev-catalog" };
  } catch (e) { console.warn("dev-catalog 读失败:", e.message); return null; }
}

async function loadLive() {
  try {
    const index = JSON.parse(await (await fetch(LIVE + "catalog/index.json", { cache: "no-store" })).text());
    const items = [];
    for (let p = 1; p <= Math.max(1, index.allPages ?? 0); p++) {
      const raw = await (await fetch(`${LIVE}catalog/all/p${p}.json`, { cache: "no-store" })).text();
      items.push(...(JSON.parse(raw).items || []));
    }
    return { index, items, from: LIVE };
  } catch (e) { return { index: null, items: [], from: "unreachable:" + e.message }; }
}

/** 合成条目：只给 ?demo=N 用（拍布局），字段与 catalog 的 item 同形。 */
function demoItems(n) {
  const NAMES_ZH = ["河滨住宅街区", "站前商业圈", "老城混合社区", "临港仓储区", "大学路文教片", "中心公园生活圈", "山景别墅湾", "夜市美食街"];
  const out = [];
  for (let i = 0; i < n; i++) {
    const cat = CAT_IDS[i % CAT_IDS.length];
    out.push({
      id: `demo-${i}-${cat}`, author: `7656119800000${1000 + i}`, authorName: i % 4 === 0 ? "" : "测试作者" + i,
      name: NAMES_ZH[i % NAMES_ZH.length] + " " + (i + 1), categories: [cat],
      areaClass: ["small", "medium", "large"][i % 3], areaM2: Math.round((0.4 + (i % 17) * 1.35) * TILE_M2),
      likes: i * 7 % 53, downloads: i * 11 % 97, hotTotal: i * 31, hotWeekly: (i * 17) % 41,
      updatedAt: new Date(Date.now() - (i * 11 + 1) * 36e5).toISOString(),
      cover: "", assetCount: 20 + i * 3, desc: "预览台合成数据，只为拍布局。",
    });
  }
  return out;
}

// 与 Bpc/BrowseKit.cs 的 TilesValue / WanValue / Km2Value / M2Value / AgoParts 同口径
const tilesValue = (m2) => {
  const t = m2 / TILE_M2;
  if (t >= 100) return t.toFixed(0);
  return Math.abs(t - Math.round(t)) < 0.05 ? String(Math.round(t)) : String(Math.round(t * 10) / 10);
};
const wanValue = (m2) => (m2 < 10000 ? m2.toLocaleString("en-US") : String(Math.round(m2 / 1000) / 10).replace(/\.0$/, ""));
const km2Value = (m2) => { const k = m2 / 1e6; return k < 0.1 ? k.toFixed(2) : String(Math.round(k * 10) / 10); };
const m2Value = (m2) => m2.toLocaleString("en-US");
function agoParts(iso) {
  const ms = Date.now() - new Date(iso).getTime();
  if (!Number.isFinite(ms) || ms < 0) return { unit: "now", n: 0 };
  const min = ms / 6e4, h = min / 60, d = h / 24;
  if (min < 1) return { unit: "now", n: 0 };
  if (h < 1) return { unit: "min", n: Math.floor(min) };
  if (d < 1) return { unit: "hour", n: Math.floor(h) };
  if (d < 30) return { unit: "day", n: Math.floor(d) };
  if (d < 365) return { unit: "month", n: Math.floor(d / 30) };
  return { unit: "year", n: Math.floor(d / 365) };
}

// ---------------- 假 C# 侧：一条 GetState + 一条 Cmd ----------------
const session = {
  category: "", search: "", area: "all", sort: "weekly", page: 1,
  forceStatus: "", votes: new Set(), toast: null, demo: 0, lang: "zh-HANS", opacity: 0.8, closed: false,
};
let source = null;   // { index, items, from }
let sourceAt = 0;

async function getSource(demo) {
  if (demo > 0) return { index: { pageSize: 15, total: demo }, items: demoItems(demo), from: "demo" };
  const dev = loadDev();
  if (dev) return dev;
  if (!source || Date.now() - sourceAt > 60000) { source = await loadLive(); sourceAt = Date.now(); }
  return source;
}

async function buildState() {
  const src = await getSource(session.demo);
  const closed = !!session.closed;
  let pool = [...src.items];
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
    const ago = agoParts(it.updatedAt);
    return {
      id: it.id, name: it.name, author: it.authorName || "", authorId: it.author,
      cover: src.from === "demo" ? "" : (it.cover ? "files/" + it.cover : ""),
      likes: it.likes + (liked ? 1 : 0), downloads: it.downloads + (used ? 1 : 0), liked, used,
      areaM2: it.areaM2, tiles: tilesValue(it.areaM2), m2: m2Value(it.areaM2),
      wan: wanValue(it.areaM2), km2: km2Value(it.areaM2),
      areaClass: it.areaClass, areaClassSlug: "AREA_" + it.areaClass,
      agoUnit: ago.unit, agoN: ago.n, assets: it.assetCount, desc: it.desc || "",
      cats: (it.categories || []).join(","),
    };
  });

  let status = session.forceStatus || (total === 0 ? (src.index && src.index.total === 0 ? "empty" : "noresult") : "ready");
  if (status === "error") session.forceStatus = "";   // 一次性的：点重试就恢复
  return {
    seq: Date.now(), visible: !session.closed, opacity: session.opacity, lang: session.lang,
    dev: src.from === "dev-catalog" || src.from === "demo",
    modVersion: "0.3.0", titleSlug: "PANEL_TITLE", hotkey: "B",
    status,
    statusSlug: status === "error" ? "ERR_TITLE" : status === "empty" ? "EMPTY_TITLE"
      : status === "loading" ? "STATUS_LOADING" : status === "noresult" ? "NORESULT_TITLE" : "",
    statusDetail: status === "error" ? "pages raw jsdelivr → no response" : "",
    truncated: false, maxItems: 300, tileM2: m2Value(TILE_M2),
    toast: session.toast || undefined,
    categories: CAT_IDS.map((id) => ({
      id, slug: "CAT_" + id, descSlug: "DESC_" + id, label: id,
      count: src.items.filter((x) => (x.categories || []).includes(id)).length,
      selected: session.category === id,
    })),
    menu: {
      search: session.search, area: session.area, areaSlug: "AREA_" + session.area,
      areas: AREA_IDS.map((x) => ({ id: x, slug: "AREA_" + x, label: x })),
      sort: session.sort, sortSlug: "SORT_" + session.sort,
      sorts: SORT_IDS.map((x) => ({ id: x, slug: "SORT_" + x, label: x })),
      page, pages, total, pageSize: 15,
    },
    items: status === "ready" ? slice : [],
  };
}

async function onCmd(raw) {
  const i = String(raw).indexOf("|");
  const kind = i < 0 ? raw : raw.slice(0, i);
  const arg = i < 0 ? "" : raw.slice(i + 1);
  if (kind === "cat") { session.category = session.category === arg ? "" : arg; session.page = 1; }
  else if (kind === "search") { session.search = arg; session.page = 1; }
  else if (kind === "area") { session.area = arg; session.page = 1; }
  else if (kind === "sort") { session.sort = arg; session.page = 1; }
  else if (kind === "page") session.page = Number(arg) || 1;
  else if (kind === "retry") session.forceStatus = "";
  else if (kind === "like" || kind === "dl") {
    const k = kind === "like" ? "L" : "D";
    if (session.votes.has(k + arg)) session.toast = { id: ++toastId, slug: "VOTE_DUP", kind: "warn" };
    else {
      session.votes.add(k + arg);
      session.toast = { id: ++toastId, slug: kind === "like" ? "VOTE_LIKE_DONE" : "VOTE_USE_DONE", kind: "ok" };
    }
  } else if (kind === "detail") session.toast = { id: ++toastId, slug: "SOON_DETAIL", kind: "info" };
  else if (kind === "upload") session.toast = { id: ++toastId, slug: "SOON_UPLOAD", kind: "info" };
  else if (kind === "close") console.log("  （预览台不关面板，方便一直看）");
  return buildState();
}
let toastId = 0;

const PAGE = (state, locale, native) => `<!doctype html><html><head><meta charset="utf-8"><title>BlueprintHub 预览台</title>
<style>
html{font-size:1px}body{margin:0;height:100vh;overflow:hidden;
  background:url('/files/preview-bg.svg') center/cover no-repeat,#0b1118;
  font:14px "Microsoft YaHei","Noto Sans CJK SC",sans-serif;color:#dfe8f0}
#game{position:fixed;inset:0}#topleft{position:fixed;left:12px;top:70px;display:flex;z-index:5}
#bar{position:fixed;right:10px;bottom:8px;z-index:9;font:12px monospace;color:#8fa0b0}
#bar a{color:#4bc3f1;cursor:pointer;margin-left:8px}
/* 假的原版 Button variant="floating"：40rem 方块 / 6rem 圆角 / --accentColorNormal 蓝底（值取自游戏 index.css） */
.fb{width:40rem;height:40rem;border-radius:6rem;border:0;padding:6rem;cursor:pointer;display:block;
  background:#4bc3f1;box-shadow:0 2rem 6rem rgba(0,0,0,0.45)}
.fb:hover{background:#7ad3f5}
</style>
<script src="/react.js"></script><script src="/react-dom.js"></script>
<script>
window.__state = ${JSON.stringify(JSON.stringify(state))};   // 字符串！与 GetterValueBinding<string> 同形
window.__dict = ${JSON.stringify(dictFor(locale))};            // 假的游戏本地化词典（真键名，从 Locale.cs 现读）
// 游戏把 API 挂在 window 的「cs2/api」这种带斜杠的扁平键上（webpack externalsType:window 就是这个形状）
window["cs2/api"] = {
  bindValue: function () { return {}; },
  useValue: function () { return window.__state; },
  trigger: function (g, n, a) { if (g === "BlueprintHub" && n === "Cmd") window.__cmd(a); },
  call: function () { return Promise.resolve("ok"); },
};
window["cs2/modding"] = {};
// cs2/l10n 的真形状：useLocalization() → { translate(key, fallback) }（从游戏 UI bundle 的 dc() 抄的）
window["cs2/l10n"] = {
  useLocalization: function () {
    return { translate: function (k, fb) { var v = window.__dict[k]; return v === undefined ? (fb || null) : v; } };
  },
  Localized: function () { return null; },
};
window["cs2/ui"] = ${native ? `{
  Button: function (p) {
    var kids = React.Children.toArray(p.children);
    return React.createElement("button", { className: "fb", onClick: function () { p.onSelect && p.onSelect({}); },
      title: p.title || "" }, kids);
  },
  Tooltip: function (p) {
    return React.createElement("div", { title: String(p.tooltip || "") }, p.children);
  },
  useTooltip: function () { return {}; },
}` : "undefined"};
window["cohtml/cohtml"] = { call: function () { return Promise.resolve(null); }, on: function () {}, trigger: function () {} };
window.__cmd = async function (a) {
  const r = await fetch("/cmd?a=" + encodeURIComponent(a));
  window.__state = await r.text();
  window.__rerender();
};
</script></head><body>
<div id="topleft"></div><div id="game"></div>
<div id="bar">预览台 · 只在本机
<a href="/?status=error">失败态</a><a href="/?status=empty">空态</a><a href="/?status=loading">加载态</a>
<a href="/?cat=park">选中类</a><a href="/?page=2">翻页</a><a href="/?lang=en-US">EN</a><a href="/?lang=zh-HANT">繁</a>
<a href="/?native=0">降级按钮</a><a href="/?demo=15">合成布局</a><a href="/">恢复</a></div>
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
  const g = (k, dft) => (url.searchParams.has(k) ? url.searchParams.get(k) : dft);

  if (url.pathname === "/cmd") {
    const raw = decodeURIComponent(url.searchParams.get("a") || "");
    onCmd(raw).then((s) => send(JSON.stringify(s), MIME[".json"]));
    return;
  }
  if (url.pathname === "/" || url.pathname === "/index.html") {
    session.category = ["", ...CAT_IDS].includes(g("cat", "")) ? g("cat", "") : "";
    session.area = AREA_IDS.includes(g("area", "all")) ? g("area", "all") : "all";
    session.sort = SORT_IDS.includes(g("sort", "weekly")) ? g("sort", "weekly") : "weekly";
    session.page = Number(g("page", "1")) || 1;
    session.search = g("q", "");
    session.forceStatus = ["error", "empty", "loading"].includes(url.searchParams.get("status")) ? url.searchParams.get("status") : "";
    session.demo = Math.max(0, Math.min(30, Number(g("demo", "0")) || 0));
    session.lang = ["zh-HANS", "zh-HANT", "en-US"].includes(g("lang", "zh-HANS")) ? g("lang", "zh-HANS") : "en-US";
    session.opacity = Math.max(0, Math.min(1, Number(g("alpha", "0.8"))));
    session.toast = null;
    session.closed = url.searchParams.get("closed") === "1";
    const liked = g("like", "");
    if (liked) { session.votes.add("L" + liked); session.votes.add("D" + liked); }
    buildState().then((s) => send(PAGE(s, session.lang, g("native", "1") !== "0"), MIME[".html"]));
    return;
  }
  if (url.pathname === "/react.js") return send(fs.readFileSync(path.join(NM, "react/umd/react.production.min.js")), MIME[".js"]);
  if (url.pathname === "/react-dom.js") return send(fs.readFileSync(path.join(NM, "react-dom/umd/react-dom.production.min.js")), MIME[".js"]);
  if (url.pathname === "/BlueprintHub.mjs") {
    const dist = path.join(UI, "dist/BlueprintHub.mjs");
    if (!fs.existsSync(dist)) return send("console.warn('先跑 cd UI && npx webpack')", MIME[".js"]);
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
  console.log(hasCatalog ? "dev-catalog 在： " + CAT + "（读的是本地假目录，注意顶栏会挂测试数据标签）"
    : "dev-catalog 不在 → 直连线上工坊（真目录；工坊为空就是正常现象，不是坏了）");
});
