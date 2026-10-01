/**
 * 静态预览台（只给开发用，不进模组产物）：
 *   node tools/dev-preview.mjs        → 起服时打印实际地址（默认 8765，被占/被系统划走会顺延）
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
 * 0.4.0 起这个台子还要替「市辖区面板」站位：
 *   registry.extend(...) 在预览台里是真的实现的（见下面 sectionMap / renderDistrict），
 *   所以 ?dpanel=1 能把 DistrictUploadSectionView 按游戏给它的那 10 个字符串属性画出来 ——
 *   上传按钮、表单、busy→done 的流转、失败态都能在开机之前拍到。采集本身不在这里测（没有 ECS）。
 *
 * 可选参数：BPH_PREVIEW_CATALOG=<dev-catalog 路径>  PORT=8765
 *   ?lang=zh-HANS|zh-HANT|en-US   ?cat=park  ?area=small  ?sort=createdDesc  ?page=2  ?q=xx  ?like=<id>
 *   ?status=loading|error|empty   ?native=0  ?neighbors=0 ?alpha=0.9 ?demo=15
 *   ?dpanel=1 拍市辖区面板    ?logged=1 已登录    ?nohook=1 钩子没挂上（拍主面板兜底入口）
 *   ?uphase=busy|done|failed 直落某一态   ?upfail=1 点上传后走失败   ?upmissing=1 完成态带缺采提示
 *   ?dist=<区名>  ?areaU=<u 数>
 *   ?act=click:<css>;clickN:<css>=<n>;text:<css>=<值>;wait:<ms> —— 页面自己把动作跑一遍，配 tools/shot.mjs 拍「点下去之后」
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
// path.resolve 过一遍：从 Git Bash 传进来的路径常是 C:\...Local/Temp/... 这种混合分隔符，
// 不归一化的话 path.join 出来的绝对路与 CAT 前缀对不上，/files/ 会整片 404（封面全裂）。
const CAT = path.resolve(process.env.BPH_PREVIEW_CATALOG
  || path.join(os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
    "ModsData", "BlueprintHub", "dev-catalog"));
const PORT = Number(process.env.PORT || 8765);
const LIVE = "https://yuexian7.github.io/blueprinthub-workshop/";

// 与 Bpc/CatalogKit.cs 同一组常量：1 地图区块 = 14336/23 见方；1u = 一个 8m×8m 功能区单元格
const TILE_EDGE_M = 14336 / 23;
const TILE_M2 = TILE_EDGE_M * TILE_EDGE_M;          // 388508.31
const U_EDGE_M = 8;
const U_M2 = U_EDGE_M * U_EDGE_M;                    // 64
// 0.4.0：「全部」+ 7 类（作者点名：产业区不叫工业区、文教区不只等于教育、公共区一分为二、去掉混合区）
const CAT_IDS = ["residential", "commercial", "industrial", "park", "education", "transit", "public_service"];
const SORT_IDS = ["weekly", "uploadTime", "createdDesc", "createdAsc", "areaDesc", "areaAsc", "nameAsc", "nameDesc"];
const AREA_IDS = ["all", "small", "medium", "large"];
const areaClassOf = (m2) => { const u = Math.round(m2 / U_M2); return u < 1000 ? "small" : u <= 4000 ? "medium" : "large"; };

const readIf = (p) => { try { return fs.readFileSync(p, "utf8"); } catch { return null; } };

/** 顶栏那个版本号必须是真的：直接从 BlueprintHubMod.cs 读 kVersion，免得预览台拍出一张撒谎的图。 */
function modVersion() {
  const src = readIf(path.join(ROOT, "BlueprintHubMod.cs")) || "";
  return (src.match(/kVersion\s*=\s*"([^"]+)"/) || [null, "0.0.0-dev"])[1];
}
const MODVER = modVersion();

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
  const NAMES_ZH = ["河滨住宅街区", "站前商业圈", "老城产业片区", "临港仓储区", "大学路文教片", "中心公园生活圈", "枢纽立交镇", "山景别墅湾"];
  const out = [];
  for (let i = 0; i < n; i++) {
    const cat = CAT_IDS[i % CAT_IDS.length];
    // 面积铺满三档（<1000u / 1000~4000u / >4000u），否则「面积筛选 + 面积排序」在预览台里拍不出差异
    const u = [40, 320, 900, 1800, 3200, 6100, 14000, 42000][i % 8];
    out.push({
      id: `demo-${i}-${cat}`, author: `7656119800000${1000 + i}`, authorName: i % 4 === 0 ? "" : "测试作者" + i,
      name: NAMES_ZH[i % NAMES_ZH.length] + " " + (i + 1), categories: [cat],
      areaClass: areaClassOf(u * U_M2), areaM2: u * U_M2,
      likes: i * 7 % 53, downloads: i * 11 % 97, hotTotal: i * 31, hotWeekly: (i * 17) % 41,
      createdAt: new Date(Date.now() - (i * 47 + 3) * 864e5).toISOString(),
      updatedAt: new Date(Date.now() - (i * 11 + 1) * 36e5).toISOString(),
      cover: "", assetCount: 20 + i * 3, desc: "预览台合成数据，只为拍布局。",
    });
  }
  return out;
}

// 与 Bpc/BrowseKit.cs 的 UValue / TilesValue / TileUValue / AgoParts 同口径
const uValue = (m2) => String(Math.round(m2 / U_M2));          // CatalogKit.U：四舍五入到整数 u，不加千分位
const tilesValue = (m2) => {
  const t = m2 / TILE_M2;
  if (t >= 100) return t.toFixed(0);
  return Math.abs(t - Math.round(t)) < 0.05 ? String(Math.round(t)) : String(Math.round(t * 10) / 10);
};
// 「0.#」：整数就不带小数点（C# 的 0.# 与 JS 的 toString 在这件事上一致）
const tileUValue = () => String(Math.round(TILE_M2 / U_M2 * 10) / 10);
const cmpText = (a, b) => String(a || "").localeCompare(String(b || ""), "en");   // ≈ C# StringComparer.Ordinal 的够用近似
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
  forceStatus: "", votes: new Set(), toast: null, demo: 0, lang: "zh-HANS", opacity: 0.9, closed: false,
  // 账号 / 挂钩 / 上传：这三块 0.4.0 才有，预览台把它们做成可 URL 驱动的，方便逐态截图
  loggedIn: false, avatar: "", hook: true,
  upload: { phase: "idle", detail: "", path: "", bpId: "", name: "", cover: "", missing: 0, bytes: 0, seq: 0 },
  district: { name: "滨江新区", areaU: "12480" },
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
  advanceUpload();                     // 上传模拟跟着「出一次状态」走一格（见 advanceUpload 的注释）
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
  // 与 CatalogKit.MakeComparator 同序（8 项，需求 7）：同分一律落到可预期的次键，避免每次刷新乱跳
  const cmpDt = (a, b) => cmpText(b || "", a || "");          // 新→旧
  const ascDt = (a, b) => cmpText(a || "", b || "");          // 旧→新
  pool.sort({
    weekly: (a, b) => (b.hotWeekly - a.hotWeekly) || (b.hotTotal - a.hotTotal) || cmpText(b.updatedAt, a.updatedAt),
    uploadTime: (a, b) => cmpDt(a.updatedAt, b.updatedAt) || cmpText(a.name, b.name),
    createdDesc: (a, b) => cmpDt(a.createdAt, b.createdAt) || cmpDt(a.updatedAt, b.updatedAt),
    createdAsc: (a, b) => ascDt(a.createdAt, b.createdAt) || ascDt(a.updatedAt, b.updatedAt),
    areaDesc: (a, b) => (b.areaM2 - a.areaM2) || cmpDt(a.updatedAt, b.updatedAt),
    areaAsc: (a, b) => (a.areaM2 - b.areaM2) || cmpDt(a.updatedAt, b.updatedAt),
    nameAsc: (a, b) => cmpText(a.name, b.name),
    nameDesc: (a, b) => cmpText(b.name, a.name),
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
      areaM2: it.areaM2, tiles: tilesValue(it.areaM2), u: uValue(it.areaM2),
      areaClass: it.areaClass || areaClassOf(it.areaM2),
      areaClassSlug: "AREA_" + (it.areaClass || areaClassOf(it.areaM2)),
      agoUnit: ago.unit, agoN: ago.n, assets: it.assetCount, desc: it.desc || "",
      cats: (it.categories || []).join(","),
    };
  });

  let status = session.forceStatus || (total === 0 ? (src.index && src.index.total === 0 ? "empty" : "noresult") : "ready");
  if (status === "error") session.forceStatus = "";   // 一次性的：点重试就恢复
  return {
    seq: Date.now(), visible: !session.closed, opacity: session.opacity, lang: session.lang,
    dev: src.from === "dev-catalog" || src.from === "demo",
    modVersion: MODVER, titleSlug: "PANEL_TITLE", hotkey: "B",
    status,
    statusSlug: status === "error" ? "ERR_TITLE" : status === "empty" ? "EMPTY_TITLE"
      : status === "loading" ? "STATUS_LOADING" : status === "noresult" ? "NORESULT_TITLE" : "",
    statusDetail: status === "error" ? "pages raw jsdelivr → no response" : "",
    truncated: false, maxItems: 300,
    // 需求 6 那句换算提示的两个数：C# 由常量现算（cellM=8、tileU≈6070.4），这里同一口径
    cellM: String(U_EDGE_M), tileU: tileUValue(),
    // 需求 4：顶栏那颗按钮 = 个人资料；未登录显示「登录」，登录后显示头像
    account: { loggedIn: session.loggedIn, avatar: session.avatar, author: session.loggedIn ? "本机作者" : "" },
    // 需求 2：市辖区面板那条钩子挂没挂上（没挂上时主面板要出兜底入口）
    hooks: { districtSection: session.hook },
    // seq/ticks/auto/pinned 只属于预览台自己的推进逻辑，C# 的 upload 块没有这几项 ——
    // 不发出去，免得桥层契约被台子带歪（前端读到多余字段是察觉不到问题的，直到实机对不上）
    upload: (({ seq, ticks, auto, pinned, ...rest }) => rest)(session.upload),
    toast: session.toast || undefined,
    // 需求 8：左栏最上面是「全部」（不显示定义），其下 7 类各带 DESC_* 词条键
    categories: [
      {
        id: "all", slug: "CAT_all", descSlug: "", label: "all",
        count: src.items.length,
        selected: !session.category || session.category === "all",
      },
      ...CAT_IDS.map((id) => ({
        id, slug: "CAT_" + id, descSlug: "DESC_" + id, label: id,
        count: src.items.filter((x) => (x.categories || []).includes(id)).length,
        selected: session.category === id,
      })),
    ],
    catSlug: "CAT_" + (session.category || "all"),
    catDescSlug: !session.category || session.category === "all" ? "" : "DESC_" + session.category,
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
  // 需求 4：顶栏那颗按钮 = 个人资料。预览台不能真登录（也不该真登录），点一下就把登录态翻面 ——
  // 目的是「未登录显示登录 / 已登录显示头像」两态都能拍到，不是冒充平台流程。
  else if (kind === "account") { session.loggedIn = true; session.avatar = session.avatar || "/files/avatar.svg"; }
  else if (kind === "logout") { session.loggedIn = false; session.avatar = ""; }
  else if (kind === "profile") console.log("  （预览台打不开平台个人页：真实现是 Platform.AccountKit.OpenProfile）");
  // 需求 2：市辖区面板的上传按钮。这里没有 ECS，采集用定时模拟：busy → done（?upfail=1 则 failed），
  // 为的是把「表单收起 → 进度 → 草稿路径」这条 UI 流转拍全（真采集只能在游戏里验）。
  else if (kind === "upload") runUpload(arg);
  else if (kind === "uploadreset") session.upload = { ...IDLE_UPLOAD, seq: session.upload.seq + 1 };
  else if (kind === "hookok") session.hook = arg !== "0";
  else if (kind === "close") console.log("  （预览台不关面板，方便一直看）");
  return stateJSON();
}
let toastId = 0;

/**
 * 与 C# 的 getter 同一件事：值没变就还回**同一个字符串**（那边靠 ReferenceStringComparer 省掉比对）。
 * 预览台拿它做同样的缓存，否则 seq 每 400ms 变一次 → 一直在重绘 → 截图永远拍糊。
 */
let lastState = { fp: "", json: "" };
async function stateJSON() {
  const s = await buildState();
  const { seq, ...rest } = s;
  const fp = JSON.stringify(rest);
  if (fp === lastState.fp) return lastState.json;
  lastState = { fp, json: JSON.stringify(s) };
  return lastState.json;
}

const IDLE_UPLOAD = { phase: "idle", detail: "", path: "", bpId: "", name: "", cover: "", missing: 0, bytes: 0, seq: 0, ticks: 0, auto: false, pinned: false };

// bpId 形状与 Bpc/BlueprintId.cs 一致：b + 36 位 GUID + '-' + 16 位作者键（预览台造假也照这个形状造）
const hex = (n) => Array.from({ length: n }, () => "0123456789abcdef"[Math.floor(Math.random() * 16)]).join("");
const fakeBpId = () => "b" + [8, 4, 4, 4, 12].map((n) => hex(n)).join("-") + "-" + hex(16);

/** ?uphase=busy|done|failed —— 直接**钉**在某一态（pinned：不会被下面的推进逻辑改动），专供截图与断言。 */
function seedUpload(phase) {
  if (!phase || phase === "idle") return { ...IDLE_UPLOAD, seq: 0 };
  const bpId = fakeBpId();
  const base = { ...IDLE_UPLOAD, phase, seq: 0, pinned: true, name: session.district.name, bpId };
  if (phase === "busy") return base;
  if (phase === "failed") return { ...base, bpId: "", phase: "failed", detail: "too-big:objects:4213:4000" };
  return {
    ...base, missing: session.upMissing ? 3 : 0, bytes: 6 * 1048576 + 154321,
    path: "%USERPROFILE%/AppData/LocalLow/Colossal Order/Cities Skylines II/ModsData/BlueprintHub/drafts/" + bpId,
    cover: "coui://blueprinthubcovers/draft-" + bpId + ".svg",   // 宿主名与 LocalLibrary.CoverHost 一致
  };
}

/**
 * 需求 2 的采集是假的（这里没有 ECS），但**推进方式**要跟真机一致：
 * 不用 setTimeout —— headless 的 --virtual-time-budget 会把客户端时钟压掉，服务端定时器和客户端轮询
 * 不在同一个时钟上，busy→done 那一跳永远拍不到。改成「每次出状态走一格」：
 * 客户端轮询到第 2 次 = 采集完成，真时钟与虚拟时钟下都成立。?uphase= 钉住的态不参与推进。
 */
function advanceUpload() {
  const u = session.upload;
  if (!u.auto || u.pinned || u.phase !== "busy") return;
  u.ticks = (u.ticks || 0) + 1;
  if (u.ticks < 2) return;
  const { name, seq } = u;
  if (session.upFail) {
    session.upload = { ...IDLE_UPLOAD, auto: true, seq, phase: "failed", detail: "too-big:objects:4213:4000", name };
    return;
  }
  const bpId = fakeBpId();
  session.upload = {
    ...IDLE_UPLOAD, auto: true, seq, phase: "done", name, bpId, missing: session.upMissing ? 3 : 0,
    bytes: 6 * 1048576 + 154321,
    path: "%USERPROFILE%/AppData/LocalLow/Colossal Order/Cities Skylines II/ModsData/BlueprintHub/drafts/" + bpId,
    cover: "coui://blueprinthubcovers/draft-" + bpId + ".svg",
  };
}

function runUpload(arg) {
  const seg = String(arg || "").split("|");
  const name = (seg[0] || "").trim() || session.district.name;
  session.upload = { ...IDLE_UPLOAD, auto: true, phase: "busy", name, seq: session.upload.seq + 1 };
}

/** ?act= 动作串：click:<css> / clickN:<css>=<序号> / text:<css>=<值> / wait:<毫秒>，分号隔开 */
function parseActs(raw) {
  const out = [];
  for (const seg of String(raw || "").split(";")) {
    const i = seg.indexOf(":");
    if (i < 0) continue;
    const op = seg.slice(0, i).trim().toLowerCase();
    const rest = seg.slice(i + 1);
    if (op === "wait") { const ms = Number(rest); if (Number.isFinite(ms)) out.push({ op, ms }); }
    else if (op === "click") { if (rest) out.push({ op, sel: rest }); }
    // clickN:<选择器>=<序号>：同一种元素有多个时必须能指到第 n 个（两个下拉就是这种情况）
    else if (op === "clickn") {
      const eq = rest.lastIndexOf("=");
      if (eq > 0) {
        const n = Number(rest.slice(eq + 1));
        if (Number.isFinite(n)) out.push({ op: "clickN", sel: rest.slice(0, eq), n });
      }
    }
    else if (op === "text") {
      const eq = rest.indexOf("=");
      if (eq > 0) out.push({ op, sel: rest.slice(0, eq), val: rest.slice(eq + 1) });
    }
  }
  return out;
}

/** 生成「页面自己把这套动作跑一遍」的脚本；没给 act 就是空串（页面行为和以前完全一致）。 */
function actScript(raw) {
  const list = parseActs(raw);
  if (!list.length) return "";
  // React 18 的 createRoot().render() 是异步提交的，脚本跑到底时 DOM 还是空的 —— 先等一下再点
  if (list[0].op !== "wait" || list[0].ms < 150) list.unshift({ op: "wait", ms: 150 });
  return `
(async function runActs() {
  const acts = ${JSON.stringify(list)};
  for (const a of acts) {
    if (a.op === "wait") { await new Promise((r) => setTimeout(r, a.ms)); continue; }
    const el = document.querySelector(a.sel);
    if (!el) { console.warn("[preview] act 找不到元素: " + a.sel); continue; }
    if (a.op === "click") { el.click(); }
    else if (a.op === "clickN") {
      const all = document.querySelectorAll(a.sel);
      const target = all[a.n];
      if (!target) { console.warn("[preview] act 第 " + a.n + " 个不存在: " + a.sel + "（只有 " + all.length + " 个）"); continue; }
      target.click();
    }
    else if (a.op === "text") {
      // React 受控输入必须走原生 setter，直接赋 .value 它收不到（这一条在真浏览器里也一样）
      const proto = el.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      const setter = Object.getOwnPropertyDescriptor(proto, "value").set;
      setter.call(el, a.val);
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
    }
    await new Promise((r) => setTimeout(r, 60));
  }
  console.log("[preview] acts done: " + acts.length);
})();`;
}

const PAGE = (state, locale, native, neighbors, dpanel, acts) => {
  // 下面 :root 那批变量、以及 .info-menu-layout / .ke4 / .be5 三条规则，都是 0.3.1 从游戏
  // index.css 里逐字抄来的。抄来的意义：预览台排得对，游戏里才是真的排得对 ——
  // 尤其 >*{margin:0 6rem 6rem 0} 这条，它就是「左上角这一排本来自动排、模组不该自己算 left」的证据本体。
  return `<!doctype html><html><head><meta charset="utf-8"><title>BlueprintHub 预览台</title>
<style>
html{font-size:1px}body{margin:0;height:100vh;overflow:hidden;
  background:url('/files/preview-bg.svg') center/cover no-repeat,#0b1118;
  font:14px "Microsoft YaHei","Noto Sans CJK SC",sans-serif;color:#dfe8f0}
:root{--accentColorNormal:#4bc3f1;--accentColorNormal-hover:#7ad3f5;--accentColorNormal-pressed:#c1eafa;
  --accentColorLight:#9ee2fc;--accentColorDark:#1e83aa;--selectedColor:#1e83aa;
  --normalTextColor:#F0FBFF;--positiveColor:#8bdb46;--warningColor:#ffa42d;--negativeColor:#e95f4a;
  --floatingToggleSize:40rem;--floatingToggleBorderRadius:6rem;--gap2:2px;--stroke1:1px;--stroke2:2px;
  --screenPadding:10rem;--panelRadius:4rem}
.info-menu-layout{pointer-events:auto;position:absolute;top:10rem;left:10rem;display:flex}
.info-menu-layout>*{margin:0 6rem 6rem 0}
.ke4{display:flex;justify-content:center;align-items:center;width:var(--floatingToggleSize);height:var(--floatingToggleSize);
  padding-top:var(--gap2);padding-right:var(--gap2);padding-bottom:var(--gap2);padding-left:var(--gap2);
  background-color:var(--accentColorNormal);border-top-left-radius:var(--floatingToggleBorderRadius);
  border-top-right-radius:var(--floatingToggleBorderRadius);border-bottom-left-radius:var(--floatingToggleBorderRadius);
  border-bottom-right-radius:var(--floatingToggleBorderRadius)}
.ke4:hover{background-color:var(--accentColorNormal-hover)}
.ke4:active{background-color:var(--accentColorNormal-pressed)}
.be5{width:100%;height:100%;--iconColor:rgb(250,250,250)}
#game{position:fixed;inset:0}
/* 左上角容器：与游戏同一个形状，按顺序排「邻居模组 → 我们」，间距由 >* 的 margin 给 */
#topleft{position:absolute;top:10rem;left:10rem;display:flex;pointer-events:auto;z-index:5}
#topleft>*{margin:0 6rem 6rem 0}
.nb{display:flex;justify-content:center;align-items:center;width:40rem;height:40rem;padding:6rem;font-size:13rem;
  font-weight:bold;color:#0d2230;background-color:#4bc3f1;border-top-left-radius:6rem;border-top-right-radius:6rem;
  border-bottom-left-radius:6rem;border-bottom-right-radius:6rem}
/* 假「市辖区」选中信息面板（需求 2 的拍摄现场）：右下角那张卡，底部那一排就是 selectedInfoSectionComponents。
   这一段样式只属于预览台，不进模组产物 —— 真游戏里这张卡的样式是游戏自己的。 */
#dpanel{position:absolute;right:24px;bottom:24px;width:360px;max-height:70vh;overflow:hidden;
  background:rgba(16,24,33,.94);border:1px solid #2b3a4a;border-radius:6rem;padding:12px 14px;font-size:13px;color:#dfe8f0;z-index:6}
.dp-head{font-size:15px;font-weight:bold;color:#9ee2fc;margin-bottom:6px}
.dp-orig{font-size:12px;color:#7c8b9a;line-height:1.5;border-bottom:1px solid #22303f;padding-bottom:8px;margin-bottom:8px}
.dp-foot{display:flex;flex-direction:column}
#bar{position:fixed;right:10px;bottom:8px;z-index:9;font:12px monospace;color:#8fa0b0}
#bar a{color:#4bc3f1;cursor:pointer;margin-left:8px}
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
// 假 cs2/ui：按 icon-button.tsx 的 $b 真实现画 —— 有 src 就 <img class=be5>，tinted 才上 mask；
// variant="floating" 与 FloatingButton 都落到官方那对 theme 类（.ke4 / .be5）。
// Tooltip 原版把浮层丢进 Portal，不占 flex 行，所以这里也用 Fragment 原样交出 children。
window["cs2/ui"] = ${native ? `{
  _btn: function (p, cls) {
    var kids = [];
    if (p.src) kids.push(React.createElement("img", { src: p.src, className: "be5", alt: "" }));
    if (p.children) { var c = React.Children.toArray(p.children); for (var i = 0; i < c.length; i++) kids.push(c[i]); }
    return React.createElement("button", { className: cls, onClick: function () { p.onSelect && p.onSelect({}); } }, kids);
  },
  Button: function (p) { return window["cs2/ui"]._btn(p, p.variant === "floating" ? "ke4" : "fbx"); },
  FloatingButton: function (p) { return window["cs2/ui"]._btn(p, "ke4"); },
  Icon: function (p) { return React.createElement("img", { src: p.src, className: "be5", alt: "" }); },
  Tooltip: function (p) { return React.createElement(React.Fragment, null, p.children); },
  useTooltip: function () { return {}; },
}` : "undefined"};
window["cohtml/cohtml"] = { call: function () { return Promise.resolve(null); }, on: function () {}, trigger: function () {} };
window.__cmd = async function (a) {
  const r = await fetch("/cmd?a=" + encodeURIComponent(a));
  window.__state = await r.text();
  window.__rerender();
};
</script></head><body>
<div id="topleft">${neighbors ? `<div class="nb">RB</div><div class="nb">WE</div>` : ""}</div>
<div id="game"></div>
${dpanel ? `<div id="dpanel">
  <div class="dp-head">市辖区：<span id="dp-name"></span></div>
  <div class="dp-orig">原版条目：人口 / 面积 / 日常服务需求 …（预览台占位，用来证明 extend 是「合并」不是「替换」）</div>
  <div class="dp-foot" id="dp-sections"></div>
</div>` : ""}
<div id="bar">预览台 · 只在本机
<a href="/?status=error">失败态</a><a href="/?status=empty">空态</a><a href="/?status=loading">加载态</a>
<a href="/?cat=park">选中类</a><a href="/?page=2">翻页</a><a href="/?lang=en-US">EN</a><a href="/?lang=zh-HANT">繁</a>
<a href="/?sort=createdDesc">按创建</a><a href="/?demo=15">合成布局</a>
<a href="/?dpanel=1">区面板</a><a href="/?dpanel=1&logged=1">区面板+已登录</a><a href="/?dpanel=1&logged=1&uphase=done">草稿完成</a>
<a href="/?nohook=1">钩子没挂上</a><a href="/?native=0">降级按钮</a><a href="/?neighbors=0">去掉邻居</a><a href="/">恢复</a></div>
<script type="module">
import register from "/BlueprintHub.mjs";
const roots = { Game: ReactDOM.createRoot(document.getElementById("game")) };
// createRoot 会把容器原有子节点清空 —— 邻居方块就是被它抹掉的。
// 所以另建一个 display:contents 的挂载点：它对 flex 布局透明，我们的 .bph-toggle 仍然是那一行的直接子项，
// 与游戏里 append 进 .info-menu-layout 的形态一致。
const tl = document.getElementById("topleft");
const host = document.createElement("div");
host.style.display = "contents";
tl.appendChild(host);
roots.GameTopLeft = ReactDOM.createRoot(host);
${dpanel ? `roots.District = ReactDOM.createRoot(document.getElementById("dp-sections"));` : ""}
const comps = {};
// 真组件里图片 src 写的是 coui://ui-mods/images/xxx.svg —— Chrome 不吃这个协议。
// 这里统一改写成 /coui/ui-mods/...，服务端再映射回 UI/ 目录（等价于游戏把模组包根挂成 ui-mods 宿主）。
const fixCoui = () => {
  const imgs = document.querySelectorAll("img");
  for (const im of imgs) {
    const s = im.getAttribute("src");
    if (s && s.indexOf("coui://") === 0) im.setAttribute("src", "/coui/" + s.slice(7));
  }
};
new MutationObserver(fixCoui).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ["src"] });

// ---- 假 moduleRegistry：append 之外还要吃 extend，否则 index.tsx 的 attachDistrictSection 永远返回 false，
//      预览台就只能拍到兜底提示，拍不到市辖区面板里那颗真按钮。
//      ?nohook=1 时反过来：故意不给 extend，让前端自己报 hookok|0 —— 与「游戏改了内部模块路径、我们挂不上」同形。
const SECTIONS_PATH = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
let sectionMap = {};
const registry = {
  append(slot, comp) { comps[slot] = comp; if (roots[slot]) roots[slot].render(React.createElement(comp)); },
  ${session.hook ? `extend(p, name, fn) {
    if (p !== SECTIONS_PATH || name !== "selectedInfoSectionComponents") throw new Error("预览台只 extend selectedInfoSectionComponents");
    sectionMap = fn(sectionMap) || {};
    renderDistrict();
  },` : ""}
};
// section 属性 = DistrictUploadSection.OnWriteProperties 的那 10 个字符串字段（键 = C# 类全名）
function districtProps() {
  let st = {};
  try { st = JSON.parse(window.__state) || {}; } catch (e) { /* 状态还没到 */ }
  const u = st.upload || {};
  const dn = ${JSON.stringify(session.district.name)};
  const au = ${JSON.stringify(session.district.areaU)};
  const name = u.name || dn;
  return {
    type: "district-upload",
    districtName: name,
    areaU: au,
    phase: u.phase || "idle",
    detail: u.detail || "",
    draftPath: u.path || "",
    bpId: u.bpId || "",
    missing: String(u.missing || 0),
    bytes: String(u.bytes || 0),
    loggedIn: st.account && st.account.loggedIn ? "1" : "0",
  };
}
function renderDistrict() {
  if (!roots.District) return;
  const p = districtProps();
  const nm = document.getElementById("dp-name");
  if (nm) nm.textContent = p.districtName;
  const kids = Object.keys(sectionMap).map((k) => React.createElement(sectionMap[k], { key: k, ...p }));
  roots.District.render(React.createElement(React.Fragment, null, kids));
}

register(registry);
window.__rerender = () => {
  for (const [slot, comp] of Object.entries(comps)) if (roots[slot]) roots[slot].render(React.createElement(comp));
  renderDistrict();
};
// 状态轮询：游戏里 C# 的 getter 每帧都在推新值，预览台没有帧循环 —— 不轮询就拍不到 busy→done 那一跳。
setInterval(async () => {
  try {
    const t = await (await fetch("/state")).text();
    if (t !== window.__state) { window.__state = t; window.__rerender(); }
  } catch (e) { /* 服务端刚重启 */ }
}, 400);
fixCoui();
renderDistrict();
// ---- ?act= 自动操作：拍「点一下之后」的状态用的（chrome --headless 一次只能交一张图，
//      没有交互能力，所以把交互写进 URL）。支持 click:<选择器> / text:<选择器>=<值> / wait:<毫秒>。
${actScript(acts)}
console.log("[preview] module registered, slots =", Object.keys(comps).join(","), ", sections =", Object.keys(sectionMap).join(","));
</script></body></html>`;
};

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8", ".svg": "image/svg+xml", ".css": "text/css", ".json": "application/json", ".png": "image/png" };
const BG = `<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#16212e"/>${Array.from({ length: 14 }, (_, i) => `<g stroke="${i % 3 === 0 ? "#2b3a4a" : "#22303f"}" stroke-width="${i % 3 === 0 ? 7 : 3}"><line x1="0" y1="${i * 28}" x2="640" y2="${i * 28 + 30}"/></g>`).join("")}${Array.from({ length: 9 }, (_, i) => `<line x1="${i * 74}" y1="0" x2="${i * 74 + 20}" y2="360" stroke="#22303f" stroke-width="4"/>`).join("")}</svg>`;
const AVATAR = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" rx="12" fill="#1e83aa"/><circle cx="32" cy="24" r="11" fill="#cfe6f5"/><path d="M10 58c4-14 12-20 22-20s18 6 22 20z" fill="#cfe6f5"/></svg>`;

const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://127.0.0.1");
  const send = (body, type) => { res.writeHead(200, { "content-type": type, "cache-control": "no-store" }); res.end(body); };
  const g = (k, dft) => (url.searchParams.has(k) ? url.searchParams.get(k) : dft);

  if (url.pathname === "/cmd") {
    const raw = decodeURIComponent(url.searchParams.get("a") || "");
    onCmd(raw).then((s) => send(s, MIME[".json"]));       // onCmd 交出来的已经是 JSON 字符串
    return;
  }
  if (url.pathname === "/state") { stateJSON().then((s) => send(s, MIME[".json"])); return; }
  if (url.pathname === "/" || url.pathname === "/index.html") {
    session.category = ["", ...CAT_IDS].includes(g("cat", "")) ? g("cat", "") : "";   // "all" 归零 = 全部（与 C# 同义）
    session.area = AREA_IDS.includes(g("area", "all")) ? g("area", "all") : "all";
    session.sort = SORT_IDS.includes(g("sort", "weekly")) ? g("sort", "weekly") : "weekly";
    session.page = Number(g("page", "1")) || 1;
    session.search = g("q", "");
    session.forceStatus = ["error", "empty", "loading"].includes(url.searchParams.get("status")) ? url.searchParams.get("status") : "";
    session.demo = Math.max(0, Math.min(30, Number(g("demo", "0")) || 0));
    session.lang = ["zh-HANS", "zh-HANT", "en-US"].includes(g("lang", "zh-HANS")) ? g("lang", "zh-HANS") : "en-US";
    session.opacity = Math.max(0, Math.min(1, Number(g("alpha", "0.9"))));            // 需求 1：默认 90%
    session.toast = null;
    session.closed = url.searchParams.get("closed") === "1";
    const liked = g("like", "");
    if (liked) { session.votes.add("L" + liked); session.votes.add("D" + liked); }
    // 需求 4：登录态（预览台不碰平台，只翻这个布尔）
    session.loggedIn = url.searchParams.get("logged") === "1";
    session.avatar = session.loggedIn ? (g("avatar", "") || "/files/avatar.svg") : "";
    // 需求 2：市辖区那条钩子（nohook=1 用来拍主面板的兜底入口）
    session.hook = url.searchParams.get("nohook") !== "1";
    session.upFail = url.searchParams.get("upfail") === "1";
    session.upMissing = url.searchParams.get("upmissing") === "1";
    session.district = {
      name: String(g("dist", "滨江新区")).slice(0, 48),
      areaU: /^\d{1,9}$/.test(g("areaU", "12480")) ? g("areaU", "12480") : "12480",
    };
    const uph = ["busy", "done", "failed"].includes(g("uphase", "")) ? g("uphase", "") : "";
    session.upload = seedUpload(uph);
    const dp = url.searchParams.get("dpanel") === "1";
    buildState().then((s) => send(PAGE(s, session.lang, g("native", "1") !== "0", g("neighbors", "1") !== "0", dp, g("act", "")), MIME[".html"]));
    return;
  }
  if (url.pathname === "/react.js") return send(fs.readFileSync(path.join(NM, "react/umd/react.production.min.js")), MIME[".js"]);
  if (url.pathname === "/react-dom.js") return send(fs.readFileSync(path.join(NM, "react-dom/umd/react-dom.production.min.js")), MIME[".js"]);
  if (url.pathname === "/BlueprintHub.mjs") {
    const dist = path.join(UI, "dist/BlueprintHub.mjs");
    if (!fs.existsSync(dist)) return send("console.warn('先跑 cd UI && npx webpack')", MIME[".js"]);
    return send(fs.readFileSync(dist), MIME[".mjs"]);
  }
  // coui://<host>/<path> 的本地等价：/coui/ui-mods/images/x.svg → UI/images/x.svg。
  // 游戏里 ui-mods 宿主的根 = 模组包目录（ModManager.InitializeUIModules 对每个模组
  // AddHostLocation("ui-mods", 资产所在目录)），所以包里的 images/ 就是 coui://ui-mods/images/。
  if (url.pathname.startsWith("/coui/")) {
    const seg = url.pathname.slice(6).split("/");   // "/coui/ui-mods/images/x.svg" → ["ui-mods","images","x.svg"]
    const host = seg.shift();
    const rel = seg.join(path.sep);
    const base = host === "ui-mods" ? UI
      : host === "blueprinthubcovers" ? path.join(os.tmpdir(), "BlueprintHub") : null;
    if (base && rel) {
      const f2 = path.join(base, rel);
      if (f2.startsWith(base) && fs.existsSync(f2)) return send(fs.readFileSync(f2), MIME[path.extname(f2)] || "application/octet-stream");
    }
    res.writeHead(404); res.end("coui 宿主不认识: " + url.pathname);
    return;
  }
  if (url.pathname === "/files/preview-bg.svg") return send(BG, MIME[".svg"]);
  if (url.pathname === "/files/avatar.svg") return send(AVATAR, MIME[".svg"]);   // 假头像：真实现是平台给的 coui:// 本地缓存图
  if (url.pathname.startsWith("/files/")) {
    const f = path.join(CAT, url.pathname.slice(7).replace(/\//g, path.sep));
    if (f.startsWith(CAT) && fs.existsSync(f)) return send(fs.readFileSync(f), MIME[path.extname(f)] || "application/octet-stream");
  }
  res.writeHead(404); res.end("nope");
});

/**
 * 端口被占 / 被系统划走就往后顺延几个再试（这台机器真遇过 8765 报 EACCES：Windows 把它收进了保留范围）。
 * 只认「地址确实是我要的那个端口」这一次成功：迟到或错位的回调一律不作数，
 * 否则日志会指着一个根本没在服务我们的端口，浪费的是自己排查的时间。
 */
let attempt = 0;
let announced = false;
server.on("error", (err) => {
  if (server.listening || announced) return;
  if ((err.code === "EADDRINUSE" || err.code === "EACCES") && attempt < 8) { attempt++; tryListen(); return; }
  console.error("✗ 预览台起不来：" + err.code + " port " + (PORT + attempt));
  process.exit(1);
});
function tryListen() {
  const port = PORT + attempt;
  server.listen(port, "127.0.0.1", () => {
    const a = server.address();
    if (announced || !a || a.port !== port) return;
    announced = true;
    console.log("BlueprintHub 预览台（只监听本机）： http://127.0.0.1:" + port + "/");
    if (port !== PORT) console.log("  （默认端口 " + PORT + " 被占/被系统划走，已顺延到 " + port + "）");
    console.log(hasCatalog ? "dev-catalog 在： " + CAT + "（读的是本地假目录，注意顶栏会挂测试数据标签）"
      : "dev-catalog 不在 → 直连线上工坊（真目录；工坊为空就是正常现象，不是坏了）");
  });
}
tryListen();
