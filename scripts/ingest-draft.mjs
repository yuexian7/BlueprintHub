#!/usr/bin/env node
/**
 * 把模组导出的草稿搬进工坊仓库（M3 的最后一米）：
 *   node scripts/ingest-draft.mjs --latest                 # 草稿箱里最新那一张
 *   node scripts/ingest-draft.mjs "<草稿目录>"              # 指定一张（面板上「选中路径」复制出来的就是它）
 *   node scripts/ingest-draft.mjs --latest --dry-run       # 只说会做什么，不落盘
 *
 * 为什么要这个脚本：游戏里点「生成蓝图」只到**本机草稿箱**为止 —— 模组拿不到任何写仓库的凭据
 * （这是有意的：客户端里不许藏能写公共仓库的 token）。投递这一步由作者本人做完，
 * 这个脚本负责把「复制 meta / 复制 blob / 建 stats / 重建 catalog」四步钉成一条命令，
 * 并在搬运前后各校验一次内容哈希，避免手复制把 sha16 与文件对不上这种脏数据带进库。
 *
 * 它**只往工坊仓库里写**，且只写四类路径；不 git add / commit / push（提交与发布是人的决定）。
 * 环境变量 BLUEPRINTHUB_WORKSHOP 指工坊仓库位置（默认 ../blueprinthub-workshop）。
 */
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const MOD_ROOT = path.join(HERE, "..");
// path.resolve 过一遍：环境变量里常混着 / 与 \（Git Bash 传出来的就是混合分隔符），
// 不归一化的话下面那句「目标必须在仓库内」的前缀比对会假报警，把合法路径当成越界。
const WS = path.resolve(process.env.BLUEPRINTHUB_WORKSHOP || path.resolve(MOD_ROOT, "..", "blueprinthub-workshop"));
const DRAFTS = path.resolve(process.env.BPH_DRAFTS || path.join(
  os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
  "ModsData", "BlueprintHub", "drafts"));

const args = process.argv.slice(2);
const DRY = args.includes("--dry-run");
const NO_BUILD = args.includes("--no-build");
const byName = (n) => { const i = args.indexOf(n); return i > 0 ? args[i + 1] : ""; };

const BP_ID = /^b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}-[0-9a-f]{16}$/;
const HEX16 = /^[0-9a-f]{16}$/;
const SECTIONS = ["terrain", "nets", "objects", "zones", "areas"];
const MAX_SECTION_BYTES = 90 * 1024 * 1024;      // 与 schema 的 sections.bytes 上限一致

const fail = (msg) => { console.error("✗ " + msg); process.exit(1); };
const sha16 = (buf) => crypto.createHash("sha256").update(buf).digest("hex").slice(0, 16);
const put = (file, buf) => {
  const abs = path.join(WS, file);
  if (!abs.startsWith(WS + path.sep)) fail("目标路径跑到了仓库外面：" + file);
  if (DRY) { console.log("  · 将写入 " + file + "（" + buf.length + " B）"); return; }
  fs.mkdirSync(path.dirname(abs), { recursive: true });
  fs.writeFileSync(abs, buf);
  console.log("  ✓ " + file + "（" + buf.length + " B）");
};

// ---------- 1. 定位草稿 ----------
let dir = args.find((a) => !a.startsWith("--")) || "";
if (args.includes("--latest") || !dir) {
  if (!fs.existsSync(DRAFTS)) fail("草稿箱不存在：" + DRAFTS + "\n  先在进游戏里对着市辖区点一次「生成蓝图」。");
  const subs = fs.readdirSync(DRAFTS, { withFileTypes: true }).filter((e) => e.isDirectory())
    .map((e) => ({ p: path.join(DRAFTS, e.name), t: fs.statSync(path.join(DRAFTS, e.name)).mtimeMs }))
    .sort((a, b) => b.t - a.t);
  if (!subs.length) fail("草稿箱是空的：" + DRAFTS);
  dir = subs[0].p;
  console.log("取最新草稿：" + dir + "（共 " + subs.length + " 张）");
}
dir = path.resolve(dir);
if (!fs.existsSync(path.join(dir, "meta.json"))) fail("这不是一张草稿（缺 meta.json）：" + dir);

// ---------- 2. 读 meta 并逐条自检（与工坊 build-catalog 同一套判据，先在这里挡住）----------
let meta;
try { meta = JSON.parse(fs.readFileSync(path.join(dir, "meta.json"), "utf8")); }
catch (e) { fail("meta.json 读不动：" + e.message); }

if (!BP_ID.test(String(meta.bpId || ""))) fail("bpId 形状不对：" + meta.bpId);
if (!/^[0-9a-f]{16}$/.test(String(meta.authorId || ""))) fail("authorId 必须是 16 位小写十六进制：" + meta.authorId);
if (!String(meta.bpId).endsWith("-" + meta.authorId)) fail("bpId 尾段不等于 authorId（这张草稿是别机的）");
if (!Array.isArray(meta.categories) || !meta.categories.length) fail("categories 为空");
if (meta.cover !== "preview/cover.svg") fail("cover 只认 preview/cover.svg，实际：" + meta.cover);

// 只接受这四类文件：草稿目录里混进别的东西就不搬（防止把任意文件塞进公共仓库）
const allowed = new Set(["meta.json", "preview/cover.svg"]);
for (const s of SECTIONS) {
  const sec = meta.sections?.[s];
  if (!sec) fail("meta.sections 缺 " + s);
  if (sec.sha16 && !HEX16.test(sec.sha16)) fail(s + ".sha16 不是 16 位十六进制：" + sec.sha16);
  if (sec.sha16) allowed.add("blob/" + sec.sha16 + ".bin");
}
const walk = (d, base = d) => fs.readdirSync(d, { withFileTypes: true }).flatMap((e) =>
  e.isDirectory() ? walk(path.join(d, e.name), base) : [path.relative(base, path.join(d, e.name)).split(path.sep).join("/")]);
const present = walk(dir);
const extra = present.filter((f) => !allowed.has(f));
if (extra.length) fail("草稿里有不认识的文件，先手工确认再搬：" + extra.join(", "));
for (const f of allowed) if (f !== "meta.json" && !present.includes(f)) fail("草稿缺文件：" + f);

// blob 逐节验哈希与声明字节数：内容寻址的地基，进来一条脏的就永久污染这个 sha16
const blobs = [];
for (const s of SECTIONS) {
  const sec = meta.sections[s];
  if (!sec.sha16) continue;
  const file = "blob/" + sec.sha16 + ".bin";
  const buf = fs.readFileSync(path.join(dir, file));
  if (buf.length !== sec.bytes) fail(`${s}：声明 ${sec.bytes} B，实际 ${buf.length} B`);
  if (buf.length > MAX_SECTION_BYTES) fail(`${s}：超过单节上限 ${MAX_SECTION_BYTES} B`);
  if (sha16(buf) !== sec.sha16) fail(`${s}：内容与文件名哈希 ${sec.sha16} 不自洽（草稿被改过或写坏了）`);
  blobs.push({ file, buf, section: s });
}
const cover = fs.readFileSync(path.join(dir, "preview", "cover.svg"));
if (!/^<svg[\s>]/i.test(String(cover.slice(0, 64)))) fail("preview/cover.svg 不是一张 SVG");

if (!fs.existsSync(path.join(WS, "tools", "build-catalog.mjs")))
  fail("找不到工坊仓库：" + WS + "\n  设一下环境变量 BLUEPRINTHUB_WORKSHOP 指向 blueprinthub-workshop 目录。");

// ---------- 3. 搬运（ID 永不复用：同 bpId 不同内容 = 硬失败）----------
const target = `blueprints/${meta.authorId}/${meta.bpId}`;
const metaBuf = Buffer.from(JSON.stringify(meta, null, 2) + "\n");
const wantMeta = path.join(WS, target, "meta.json");
if (fs.existsSync(wantMeta)) {
  const have = fs.readFileSync(wantMeta);
  if (!have.equals(metaBuf)) fail(`${meta.bpId} 已在库里且内容不同 —— 蓝图 ID 永不复用，这张草稿不能覆盖它`);
  console.log("· meta 与库里一致（之前搬过），继续核对分节");
}
console.log((DRY ? "预演：将搬运 " : "开始搬运 ") + meta.bpId + "　《" + meta.name + "》 / " + blobs.length + " 个分节");
put(target + "/meta.json", metaBuf);
put(target + "/preview/cover.svg", cover);
for (const b of blobs) {
  const abs = path.join(WS, b.file);
  if (fs.existsSync(abs)) {
    if (sha16(fs.readFileSync(abs)) !== path.basename(b.file, ".bin")) fail(b.file + " 已存在但内容与哈希不符（库里这条是脏的，先查）");
    if (!DRY) console.log("  = " + b.file + "（已存在，内容寻址不用重复写）");
    continue;
  }
  put(b.file, b.buf);
}
const statsFile = `stats/${meta.authorId}/${meta.bpId}.json`;
if (!fs.existsSync(path.join(WS, statsFile))) {
  put(statsFile, Buffer.from(JSON.stringify({ downloads: 0, likes: 0, downloads7d: 0, likes7d: 0 }, null, 1) + "\n"));
}

if (DRY) { console.log("\n（--dry-run：什么都没写）"); process.exit(0); }

// ---------- 4. 重建 catalog（跑的是工坊里那份真 builder：schema + ajv + 自洽校验）----------
if (!NO_BUILD) {
  const run = spawnSync(process.execPath, ["tools/build-catalog.mjs"], { cwd: WS, encoding: "utf8" });
  console.log(((run.stdout || "") + (run.stderr || "")).trim());
  if (run.status !== 0) fail("catalog 重建失败：草稿已复制进仓库，但索引没更新 —— 修好后再跑一次 node tools/build-catalog.mjs");
}

console.log("\n下一步（提交与发布由你自己做，脚本不碰 git）：");
console.log("  cd " + WS);
console.log(`  git add ${target} blob stats catalog && git commit -m "add《${meta.name}》 ${meta.bpId}"`);
console.log("  git push        # 推上去几分钟后三个镜像就能读到，面板里就会出现这张");
console.log("本地先看效果：node tools/seed-dev-catalog.mjs --yes-dev-data 之后开面板（DEV 标签会提示是测试数据）");
