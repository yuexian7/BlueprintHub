/**
 * 上传前先看线上（用户的固定要求：「先检查线上模组数据是否和本地一致，不一致先同步到本地再上传，
 * 不要动我线上改过的地方」）。
 *
 * 做三件事：
 *   1. 从本机 Paradox SDK 库里读 SessionToken（只在本机用，绝不打印、绝不写进任何文件）
 *   2. 查这个账号名下所有模组 + 按名字搜，找 BlueprintHub 是否已经有线上条目
 *   3. 找到了就把线上元数据落到 research/live-<modId>.json，并逐字段与 Properties/PublishConfiguration.xml 对比
 *
 *   node scripts/pull-live.mjs            # 只查 + 打印差异
 *   node scripts/pull-live.mjs --sync     # 把线上字段写回 PublishConfiguration.xml（线上为准）
 *
 * 认证方式与已上架的 BusLineAutoStops 同一套：Authorization: {"session":<token>,"type":"Session"}。
 */
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const CFG = path.join(ROOT, "Properties", "PublishConfiguration.xml");

function token() {
  const base = path.join(os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", ".pdxsdk");
  if (!fs.existsSync(base)) return null;
  for (const steam of fs.readdirSync(base)) {
    const db = path.join(base, steam, "database.json");
    if (!fs.existsSync(db)) continue;
    const m = fs.readFileSync(db, "utf8").match(/"SessionToken"\s*:\s*"([^"]+)"/);
    if (m) return m[1];
  }
  return null;
}

const HEADERS = (t) => ({ Authorization: JSON.stringify({ session: t, type: "Session" }), Accept: "application/json" });

async function get(url, t) {
  try {
    const r = await fetch(url, { headers: HEADERS(t) });
    const text = await r.text();
    let json = null;
    try { json = JSON.parse(text); } catch { /* 非 JSON 就原样报 */ }
    return { status: r.status, json, text };
  } catch (e) {
    return { status: 0, text: "fetch failed: " + e.message };
  }
}

/** PublishConfiguration.xml → 扁平字段（只取比对需要的那几个） */
function readCfg() {
  if (!fs.existsSync(CFG)) return null;
  const xml = fs.readFileSync(CFG, "utf8");
  const one = (tag) => (xml.match(new RegExp(`<${tag} Value="([^"]*)"\\s*/>`)) || [])[1];
  const out = {
    modId: one("ModId"), gameVersion: one("GameVersion"), modVersion: one("ModVersion"),
    displayName: one("DisplayName"), shortDescription: one("ShortDescription"),
    longDescription: one("LongDescription"), changeLog: one("ChangeLog"), forumLink: one("ForumLink"),
    accessLevel: one("AccessLevel"), tags: [...xml.matchAll(/<Tag Value="([^"]*)"\/>/g)].map((m) => m[1]),
    externals: [...xml.matchAll(/<ExternalLink Type="([^"]+)" Url="([^"]+)"\/>/g)].map((m) => m[1] + "=" + m[2]),
  };
  return out;
}

const unesc = (s) => (s || "").replace(/&#xA;/g, "\n").replace(/&amp;/g, "&").replace(/&quot;/g, '"').replace(/&lt;/g, "<").replace(/&gt;/g, ">");

(async () => {
  const t = token();
  if (!t) { console.error("✗ 没找到 SessionToken（游戏没登录过 Paradox 账号？）"); process.exit(1); }
  console.log("SessionToken: 读到（长度 " + t.length + "，不打印）");

  const cfg = readCfg();
  const wantId = cfg && cfg.modId ? cfg.modId : null;
  const urls = [];
  if (wantId) urls.push(["byModId", `https://api.paradox-interactive.com/mods?modId=${wantId}&os=windows`]);
  urls.push(["search-blueprinthub", "https://api.paradox-interactive.com/mods/search?query=BlueprintHub&os=windows"]);
  urls.push(["search-blueprint", "https://api.paradox-interactive.com/mods/search?query=Blueprint&os=windows"]);

  let found = null;
  for (const [name, url] of urls) {
    const r = await get(url, t);
    console.log(`\n[${name}] HTTP ${r.status}  ${url.replace(/^https:\/\//, "")}`);
    if (!r.json) { console.log("  非 JSON：" + r.text.slice(0, 160)); continue; }
    const list = r.json.data || r.json.mods || (Array.isArray(r.json) ? r.json : []);
    console.log("  命中 " + (list.length || 0) + " 条");
    for (const it of list.slice(0, 12)) {
      const a = it.attributes || it;
      console.log(`   - id=${it.id} name="${a.displayName || a.name}" v=${a.modVersion || a.userVersion} access=${a.accessLevel || "?"} os=${a.os || "?"}`);
      const nm = String(a.displayName || a.name || "");
      if (!found && /blueprint\s*hub/i.test(nm)) found = it;
    }
    if (wantId && !found && list.length === 1) found = list[0];
  }

  if (!found) {
    console.log("\n结论：线上没有 BlueprintHub 条目 → 本次是首发（NewMod），没有「线上被我改过」的东西要保护。");
    process.exit(0);
  }

  const a = found.attributes || found;
  fs.mkdirSync(path.join(ROOT, "research"), { recursive: true });
  const out = path.join(ROOT, "research", `live-${found.id}.json`);
  fs.writeFileSync(out, JSON.stringify(found, null, 2));
  console.log("\n线上条目已存：", out);

  const live = {
    modId: String(found.id),
    displayName: a.displayName ?? a.name,
    shortDescription: a.shortDescription,
    longDescription: a.longDescription,
    changeLog: a.changeLog,
    forumLink: a.forumLink,
    accessLevel: a.accessLevel,
    gameVersion: a.gameVersion,
    tags: a.tags,
    externals: (a.externalLinks || []).map((e) => `${e.type}=${e.url}`),
  };
  console.log("\n逐字段对比（线上 vs PublishConfiguration.xml）：");
  if (!cfg) console.log("  ✗ 本地还没有 Properties/PublishConfiguration.xml");
  for (const k of Object.keys(live)) {
    if (!cfg) break;
    const lv = Array.isArray(live[k]) ? live[k].slice().sort().join(",") : unesc(String(live[k] ?? ""));
    const mv = Array.isArray(cfg[k]) ? cfg[k].slice().sort().join(",") : unesc(String(cfg[k] ?? ""));
    const same = lv === mv;
    console.log(`  ${same ? "=" : "≠"} ${k}${same ? "" : `\n      线上: ${lv.slice(0, 180)}\n      本地: ${mv.slice(0, 180)}`}`);
  }
  console.log(`\nModId=${live.modId}（写进 PublishConfiguration.xml 才能 NewVersion；当前本地=${wantId || "无"}）`);
  console.log("下一步：把线上为准的字段同步进 XML（或 --sync 自动写），再跑 publish-030.mjs。");
})();
