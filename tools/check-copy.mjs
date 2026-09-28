#!/usr/bin/env node
/**
 * 文案门禁（需求 4 的自动化那一半）：
 *   node tools/check-copy.mjs
 *
 * 钉五件事，任何一条不满足就 exit 1：
 *   ① 三份面板词条表（PanelZh / PanelZhTw / PanelEn）键集必须完全一致 —— 少一个键，
 *      那个语言的面板就会露出 slug。
 *   ② 前端与 C# 里出现的每一个 slug 都必须在表里；表里也不许躺着没人用的键。
 *   ③ 模板里的占位符只允许下面 ALLOWED 那一组（C# 按这套名字发数字，名字漂了占位符就填不上）。
 *   ④ 前端源码里不许出现成品玩家可见文案（引号里带中日韩字符 = 硬编码，BridgeTheLanguageGap 翻不动）。
 *      注释与 console 不算（那是给开发者看的，本来就不该被翻译）；C# 侧的中文全是日志，同样不算。
 *   ⑤ 选项页词条引用的属性名必须在 Setting.cs 里真的存在。
 *
 * 只做静态扫描，不启游戏、不联网；跑一次 < 1 秒，所以每次改文案都该跑。
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const LOCALE = fs.readFileSync(path.join(ROOT, "Locale.cs"), "utf8");
const UI_SRC = path.join(ROOT, "UI", "src");
const UISYS = path.join(ROOT, "Systems", "UI", "BlueprintHubUISystem.cs");

const fail = [];
const ok = (m) => console.log("  ✓ " + m);
const bad = (m) => { fail.push(m); console.log("  ✗ " + m); };

/** 抓 `Dictionary<string, string> <fn>() => new Dictionary<...> { { "k", "v" }, ... }` */
function tableOf(fnName) {
  const at = LOCALE.indexOf(`Dictionary<string, string> ${fnName}()`);
  if (at < 0) throw new Error("Locale.cs 里找不到 " + fnName);
  const end = LOCALE.indexOf("\n        };", at);
  const body = LOCALE.slice(at, end < 0 ? at + 40000 : end);
  const re = /\{\s*"([^"]+)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\}/g;
  const out = {};
  let m;
  while ((m = re.exec(body))) out[m[1]] = m[2];
  return out;
}

const ZH = tableOf("PanelZh");
const TW = tableOf("PanelZhTw");
const EN = tableOf("PanelEn");

console.log("\n[1] 三份面板词条表键集一致");
const keysOf = (t) => Object.keys(t).sort().join(",");
if (keysOf(ZH) === keysOf(TW) && keysOf(ZH) === keysOf(EN)) ok(`三份表都是 ${Object.keys(ZH).length} 条`);
else {
  const a = Object.keys(ZH);
  const b = new Set(Object.keys(TW)), c = new Set(Object.keys(EN));
  bad("键集不一致 → zh-HANT 缺: " + a.filter((x) => !b.has(x)).join("|")
    + " / en-US 缺: " + a.filter((x) => !c.has(x)).join("|"));
}

console.log("\n[2] 用到的 slug 都在表里，表里也没有躺着不用的");
const used = new Set();
const files = [];
const walk = (d) => {
  for (const f of fs.readdirSync(d)) {
    const p = path.join(d, f);
    if (fs.statSync(p).isDirectory()) walk(p);
    else if (/\.(ts|tsx)$/.test(f)) files.push(p);
  }
};
walk(UI_SRC);
files.push(UISYS);

const stripComments = (s) => s
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .replace(/^\s*\/\/.*$/gm, "")
  .replace(/(^|[^:"'`\\])\/\/[^\n]*/g, "$1");

for (const f of files) {
  const src = stripComments(fs.readFileSync(f, "utf8"));
  for (const m of src.matchAll(/\bt\(\s*"([A-Z][A-Z0-9_]*)"/g)) used.add(m[1]);
  for (const m of src.matchAll(/Toast\(\s*"([A-Z][A-Z0-9_]*)"/g)) used.add(m[1]);
  // C# 那边是拼出来的（"CAT_" + id / "SORT_" + id / "AREA_" + id / "AGO_" + unit.toUpperCase()）
  for (const m of src.matchAll(/"(CAT|DESC|AREA|SORT|AGO)_" *\+/g)) {
    const pre = m[1];
    const ids = pre === "CAT" || pre === "DESC"
      ? ["residential", "commercial", "industrial", "park", "education", "public", "mixed"]
      : pre === "AREA" ? ["all", "small", "medium", "large"]
      : pre === "SORT" ? ["weekly", "total", "uploadTime", "area", "name"]
      : ["NOW", "MIN", "HOUR", "DAY", "MONTH", "YEAR"];      // AGO_* 表里用大写档名
    for (const id of ids) used.add(pre + "_" + id);
  }
  // 兜底：任何「全大写常量字符串」只要表里有对应词条，就算被引用到
  //（C# 里 return "ERR_TITLE"; 这种不是 t("...") 形状，但同样是发给前端的 slug）
  for (const m of src.matchAll(/"([A-Z][A-Z0-9_]{2,})"/g)) if (ZH[m[1]]) used.add(m[1]);
}
for (const s of [...used]) if (/_$/.test(s)) used.delete(s);     // "AGO_" 这类拼接前缀本身不是 slug

const missing = [...used].filter((s) => !ZH[s]);
if (!missing.length) ok(`前端与 C# 用到的 ${used.size} 个 slug 全部有词条`);
else bad("这些 slug 被引用但表里没有: " + missing.join(", "));
const unused = Object.keys(ZH).filter((s) => !used.has(s));
if (!unused.length) ok("表里没有躺着没人用的键");
else bad("这些词条没人引用（要么删掉，要么确实是漏接）: " + unused.join(", "));

console.log("\n[3] 模板占位符只允许已知的几个");
const ALLOWED = new Set(["tiles", "m2", "wan", "km2", "n", "total", "max", "key", "tilem2"]);
const stray = [];
for (const [name, tbl] of [["zh", ZH], ["tw", TW], ["en", EN]]) {
  for (const [k, v] of Object.entries(tbl)) {
    for (const m of String(v).matchAll(/\{(\w+)\}/g)) if (!ALLOWED.has(m[1])) stray.push(name + ":" + k + "{" + m[1] + "}");
  }
}
if (!stray.length) ok("三份表里所有 {占位符} 都在允许集合（" + [...ALLOWED].join(", ") + "）");
else bad("未知占位符（C# 不会发这个数）: " + stray.join(", "));

console.log("\n[4] 前端不许硬编码玩家可见文案");
// 假名 + 中日韩统一表文 + 扩展 A + 兼容表文（\u 转义写死，避免源码里出现裸字符被编辑器改坏）
const CJK = /[぀-ヿ㐀-䶿一-鿿豈-﫿]/
const hard = [];
for (const f of files) {
  const rel = path.relative(ROOT, f).replace(/\\/g, "/");
  if (!rel.startsWith("UI/src/")) continue;        // C# 侧的中文全是日志，不进词典
  if (/styles\.ts$/.test(rel)) continue;           // 样式里没有文案
  const code = stripComments(fs.readFileSync(f, "utf8"));
  const ls = code.split(/\r?\n/);
  for (let i = 0; i < ls.length; i++) {
    const l = ls[i];
    if (/console\.(log|warn|error|info)/.test(l)) continue;
    const m = l.match(/(["'`])([^"'`\n]*)\1/);
    if (m && CJK.test(m[2])) hard.push(rel + ":" + (i + 1) + ": " + m[2].slice(0, 24));
  }
}
if (!hard.length) ok("UI/src 里没有硬编码的中文句子（全部走 t(slug)）");
else bad("这些地方出现了成品文案，请改成词条:\n      " + hard.join("\n      "));

console.log("\n[5] 选项页词条引用的属性真实存在");
const setting = fs.readFileSync(path.join(ROOT, "Setting.cs"), "utf8");
for (const prop of ["PanelOpacitySlider", "TogglePanelBinding", "AboutVersion", "AboutAuthor", "LinkKofi", "LinkForum", "LinkRainbow"]) {
  if (!new RegExp("public\\s+\\S+\\s+" + prop + "\\b").test(setting))
    bad("Locale.cs 给 " + prop + " 配了词，Setting.cs 却没有这个属性");
}
ok("选项页 7 个属性的词条与属性名对得上");

console.log("\n" + (fail.length ? "✗ 文案门禁未过：" + fail.length + " 条" : "✓ 文案门禁全绿"));
process.exit(fail.length ? 1 : 0);
