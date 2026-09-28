#!/usr/bin/env node
/**
 * 生成「开发覆盖目录」：让面板在工坊还是空库的时候就能测出真东西。
 *
 * 产出位置 = UnityEngine.Application.persistentDataPath/ModsData/BlueprintHub/dev-catalog/
 *   （WorkshopClient.DevCatalogDir 会优先读它，玩家机器上没这个目录 → 对玩家零影响）
 *
 * 关键做法：**不自己拼 catalog 文件格式**，而是把假蓝图写进 workshop 仓库、跑一遍真的
 * tools/build-catalog.mjs（schema + ajv + 自洽校验全过），再把产物复制到 dev-catalog，最后把仓库恢复原状。
 * 这样数据面格式一改，这个脚本要么一起对、要么立刻报错，不存在「开发机能跑、线上不能跑」的漂移。
 *
 * 用法：
 *   node tools/seed-dev-catalog.mjs --clean                 # 删掉 dev-catalog，回到真实数据
 *   node tools/seed-dev-catalog.mjs --yes-dev-data          # 明确要造 18 张假蓝图才造
 *   node tools/seed-dev-catalog.mjs --yes-dev-data --items 40
 * 0.3.0 起必须带 --yes-dev-data：工坊里的真蓝图由玩家自己上传，
 * 任何「面板里凭空多出一堆模板」的状态都只允许是开发者显式按开关造出来的。
 * 面板在这种情况下会在顶栏挂 DEV_TAG（测试数据）标签，绝不会与真实条目混淆。
 * 环境变量 BLUEPRINTHUB_WORKSHOP 可指 workshop 仓库位置（默认 ../blueprinthub-workshop）。
 */
import { spawnSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const MOD_ROOT = path.join(HERE, "..");
const WS = process.env.BLUEPRINTHUB_WORKSHOP
  || path.resolve(MOD_ROOT, "..", "blueprinthub-workshop");
const GAME_DATA = path.join(
  os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
  "ModsData", "BlueprintHub");
const DEV = path.join(GAME_DATA, "dev-catalog");
const TILE_M2 = 388129;                       // 1 区块 = 623m × 623m

const args = process.argv.slice(2);
const N = (() => {
  const i = args.indexOf("--items");
  return i > 0 && args[i + 1] ? Math.max(1, Math.min(200, Number(args[i + 1]) || 18)) : 18;
})();

if (args.includes("--clean")) {
  fs.rmSync(DEV, { recursive: true, force: true });
  console.log("已删除开发覆盖：", DEV);
  console.log("下次开面板会回到真实三镜像。");
  process.exit(0);
}

if (!fs.existsSync(path.join(WS, "tools", "build-catalog.mjs"))) {
  console.error("✗ 找不到工坊仓库：", WS);
  console.error("  设一下环境变量 BLUEPRINTHUB_WORKSHOP 指向 blueprinthub-workshop 目录。");
  process.exit(2);
}
if (!fs.existsSync(path.join(WS, "node_modules", "ajv"))) {
  console.error("✗ workshop 侧依赖没装：先在", WS, "里跑 npm install");
  process.exit(2);
}

// ---------- 假数据（名字/面积/计数都是编的，只用来喂渲染）----------
const CATS = ["residential", "commercial", "industrial", "park", "education", "public", "mixed"];
const CAT_ZH = {
  residential: "住宅", commercial: "商圈", industrial: "厂区", park: "公园",
  education: "文教", public: "枢纽", mixed: "混合",
};
const AUTHORS = [
  ["76561198000001234", "阿明"], ["76561198000005678", "牛姐"],
  ["76561198000009999", "老王"], ["76561198000012222", "海岸线"],
];
const SUFFIX = ["北岸", "老城", "新区", "天际", "港湾", "山麓", "中环", "夜市", "学府", "厂区东"];

function guid(seed) {
  const h = crypto.createHash("md5").update("bph-dev-" + seed).digest("hex");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20, 32)}`;
}

function coverSvg(seed, color) {
  let s = 0;
  const rnd = () => { s = (s * 1103515245 + seed * 12345 + 12345) & 0x7fffffff; return s / 0x7fffffff; };
  let blocks = "";
  for (let i = 0; i < 46; i++) {
    const x = 8 + Math.floor(rnd() * 210), y = 10 + Math.floor(rnd() * 110);
    const w = 8 + Math.floor(rnd() * 26), h = 6 + Math.floor(rnd() * 20);
    blocks += `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="1.5" fill="${color}" opacity="${(0.14 + rnd() * 0.4).toFixed(2)}"/>`;
  }
  const roads = `<g stroke="#dfe8f0" stroke-opacity="0.5" stroke-width="2" fill="none">
    <path d="M0 46 H240"/><path d="M0 96 H240"/><path d="M78 0 V140"/><path d="M164 0 V140"/></g>
    <g stroke="#dfe8f0" stroke-opacity="0.22" stroke-width="1" fill="none"><path d="M0 22 H240M0 70 H240M0 120 H240M34 0 V140M120 0 V140M206 0 V140"/></g>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 140">
<rect width="240" height="140" fill="#16212e"/>${blocks}${roads}
<circle cx="120" cy="70" r="3" fill="#4fd1c5"/><text x="8" y="132" font-size="8" fill="#9fb0c0">DEV ONLY</text></svg>`;
}

const COLOR = {
  residential: "#6ea8fe", commercial: "#f0b429", industrial: "#9b8cff", park: "#4caf7d",
  education: "#4fd1c5", public: "#ff8f5e", mixed: "#c0cb78",
};

const DAY = 86400000;
const now = Date.now();
const items = [];
for (let i = 0; i < N; i++) {
  const [authorId, authorName] = AUTHORS[i % AUTHORS.length];
  const cat = CATS[i % CATS.length];
  const tilesX = 1 + (i % 4), tilesY = 1 + ((i * 3) % 4);
  const tiles = tilesX * tilesY;
  const bpId = `b${guid(i)}-${authorId}`;
  items.push({
    bpId, authorId, authorName,
    name: CAT_ZH[cat] + SUFFIX[i % SUFFIX.length] + (i >= SUFFIX.length ? " " + Math.floor(i / SUFFIX.length + 1) : ""),
    cat, tiles, tilesX, tilesY,
    likes: (i * 7) % 60, downloads: (i * 11) % 90,
    likes7d: (i * 3) % 12, downloads7d: (i * 5) % 20,
    days: 1 + (i * 5) % 400,
    desc: "这是开发用的假蓝图，只用来验面板渲染：卡片、封面、分页、搜索、排序、点赞去重。真数据来自 " +
      "blueprinthub-workshop。",
  });
}

// ---------- 写进 workshop → 跑真 builder → 复制产物 → 恢复仓库 ----------
const walkFiles = (dir, base = dir) => {
  if (!fs.existsSync(dir)) return [];
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const abs = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...walkFiles(abs, base));
    else out.push(path.relative(base, abs).split(path.sep).join("/"));
  }
  return out;
};
const snapshot = (dir) => new Map(walkFiles(dir).map((r) => [r, fs.readFileSync(path.join(dir, r))]));

// 先整片快照：builder 会「删空目录 + 删尾部残留页」，只复原写过的文件会丢东西（真丢过一次就再也找不回来）
const SNAP = { catalog: snapshot(path.join(WS, "catalog")), stats: snapshot(path.join(WS, "stats")), blueprints: snapshot(path.join(WS, "blueprints")) };
const CREATED = new Set();

function put(file, content) {
  const abs = path.join(WS, file);
  if (!fs.existsSync(abs)) CREATED.add(file);
  fs.mkdirSync(path.dirname(abs), { recursive: true });
  fs.writeFileSync(abs, content);
}

function restore() {
  for (const rel of Object.keys(SNAP)) {
    const dir = path.join(WS, rel);
    const want = SNAP[rel];
    for (const f of walkFiles(dir)) if (!want.has(f)) { try { fs.rmSync(path.join(dir, f), { force: true }); } catch { } }
    for (const [f, buf] of want) {
      const abs = path.join(dir, f);
      fs.mkdirSync(path.dirname(abs), { recursive: true });
      if (!fs.existsSync(abs) || !fs.readFileSync(abs).equals(buf)) fs.writeFileSync(abs, buf);
    }
    // 快照里没有、现在却空着的目录 = 本次跑出来的，删干净（含只剩空壳的父目录）
    const prune = (d) => {
      if (!fs.existsSync(d)) return;
      for (const e of fs.readdirSync(d, { withFileTypes: true })) {
        if (e.isDirectory()) prune(path.join(d, e.name));
      }
      try { if (walkFiles(d).length === 0) fs.rmSync(d, { recursive: true, force: true }); } catch { }
    };
    prune(dir);
  }
}
let failed = null;
try {
  for (const it of items) {
    const dir = `blueprints/${it.authorId}/${it.bpId}`;
    const stamp = new Date(now - it.days * DAY).toISOString().replace(/\.\d+Z$/, "Z");
    const meta = {
      schemaVersion: 1,
      bpId: it.bpId,
      authorId: it.authorId,
      authorName: it.authorName,
      name: it.name,
      description: it.desc,
      categories: [it.cat],
      createdAt: stamp,
      updatedAt: stamp,
      gameBuild: "1.6.2f1",
      generatorVersion: "dev-seed",
      districtName: it.name + "区",
      bounds: {
        widthM: it.tilesX * 623, depthM: it.tilesY * 623,
        areaM2: it.tiles * TILE_M2, tilesX: it.tilesX, tilesY: it.tilesY,
      },
      anchors: { centerOffsetXM: 0, centerOffsetZM: 0, centroidIsApproximate: false },
      // 分节全空：浏览侧不碰 blob（套用是 M4），这样假数据不会在 blob/ 里留孤儿
      sections: {
        terrain: { sha16: "", bytes: 0 }, nets: { sha16: "", bytes: 0 },
        objects: { sha16: "", bytes: 0 }, zones: { sha16: "", bytes: 0 },
        areas: { sha16: "", bytes: 0 },
      },
      counts: { segments: 0, nodes: 0, buildings: 0, props: 0, trees: 0, zones: 0 },
      cover: "preview/cover.svg",
      assets: [
        { name: "Medium Residential (6x4)", kind: "zone" },
        { name: "Asphalt Road 2L", kind: "net" },
        { name: "Street Lamp 01", kind: "prop" },
      ],
    };
    put(`${dir}/meta.json`, JSON.stringify(meta, null, 2) + "\n");
    put(`${dir}/preview/cover.svg`, coverSvg(it.bpId.length + it.tiles, COLOR[it.cat]));
    put(`stats/${it.authorId}/${it.bpId}.json`, JSON.stringify({
      downloads: it.downloads, likes: it.likes,
      downloads7d: it.downloads7d, likes7d: it.likes7d,
    }, null, 1) + "\n");
  }

  const run = spawnSync(process.execPath, ["tools/build-catalog.mjs"], { cwd: WS, encoding: "utf8" });
  const out = (run.stdout || "") + (run.stderr || "");
  if (run.status !== 0) {
    failed = "builder 退出码 " + run.status;
    console.error(out);
    process.exitCode = 1;
  } else {
    console.log(out.trim());
    fs.rmSync(DEV, { recursive: true, force: true });
    let copied = 0;
    for (const rel of ["catalog", "blueprints", "stats"]) {
      const from = path.join(WS, rel);
      if (!fs.existsSync(from)) continue;
      fs.cpSync(from, path.join(DEV, rel), { recursive: true });
      copied += fs.readdirSync(from, { recursive: true }).filter((f) => !fs.statSync(path.join(from, f)).isDirectory()).length;
    }
    console.log(`✓ 开发覆盖已写入：${DEV}`);
    console.log(`  条目 ${items.length} 张 / 复制文件 ${copied} 个`);
    console.log("  进游戏开面板即可看到卡片与分页；想回到真实数据：node tools/seed-dev-catalog.mjs --clean");
  }
} catch (e) {
  failed = e && e.message ? e.message : String(e);
  console.error("✗ ", failed);
  process.exitCode = 1;
} finally {
  try {
    restore();
    const st = spawnSync("git", ["status", "--porcelain"], { cwd: WS, encoding: "utf8" });
    const dirty = (st.stdout || "").split("\n").filter(Boolean)
      .filter((l) => !/\.(md|json|mjs)$/.test(l) || !/catalog|blueprints|stats/.test(l));
    console.log(failed ? "（恢复仓库时出错：" + failed + "）" : "✓ workshop 仓库已恢复原状");
    if ((st.stdout || "").trim()) {
      console.log("  git status 剩余改动（应只有本次之前的未提交内容）：");
      for (const l of st.stdout.trim().split("\n")) console.log("   ", l);
    }
  } catch (e) { console.error("✗ 恢复仓库失败，请手工检查", WS, e); }
}
