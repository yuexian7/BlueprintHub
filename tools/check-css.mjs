/**
 * tools/check-css.mjs —— 面板 CSS 的 Cohtml 兼容门禁（0.3.1 起，和 check-copy.mjs 同级）。
 *
 * 为什么要钉成脚本：0.3.0 那轮「面板丑 / 选中态看不见 / 入口白块」三条，根因全部是
 * Cohtml 吃不下某种 CSS 写法，而**它是静默丢弃整条声明**（只在 UI.log 里留一行 WARN），
 * 本地 tsc / webpack / 浏览器都不会报错 —— 也就是说只有开游戏才看得出来。这类问题只能靠静态门禁。
 *
 * 每条规则的出处（都是本会话在真机 + 游戏自带 index.css/index.js 上量出来的，不是查文档抄的）：
 *  R1 shorthand 里不能用 var()。官方统计：`background:<var>` 1 处、`border:<var>` 0 处，
 *     而那唯一 1 处每次开机都在 UI.log 打「Custom CSS expressions are not supported in shorthand
 *     declaration, please use the long versions of the CSS property」→ 整条声明作废。
 *     字面量进 shorthand 是合法的：官方 `border:2.4rem solid #4ac0f0` 15 处、`border-color:rgba(..)` 176 处。
 *  R2 var() 的逗号兜底不支持。实机日志：「Unable to resolve custom variable: normalTextColor,#F0FBFF」
 *     —— 逗号后面那截被当成变量名的一部分了。官方 CSS 里带逗号兜底的 var() 是 0 处。
 *  R3 禁 data URI。官方 index.css / index.js 全文 data URI 0 次；mask 只吃能当资源加载的 URL。
 *     0.3.0 的 `mask-image:url("data:image/svg+xml,...")` + `background-color:#fff` 的 span
 *     mask 没生效 → 剩下一整块实心白方（玩家报「左上角只有白底」）。
 *  R4 实机点名的不认属性：object-fit / outline / word-wrap / text-rendering（官方 CSS 里
 *     object-fit 也是 0 使用）。
 *  R5 实机点名的不认伪类：:focus-within / :first-of-type（官方用显式类名，不用这俩）。
 *  R6 var() 的 token 名必须在白名单里 —— 白名单是逐条在官方 index.css 里 grep 到定义才进来的。
 *     反面教材：0.3.0 用了 --normalBorderColor，官方根本没这个 token（正确的那类叫 --stroke1/--stroke2 或写死）。
 *  R7 mask 系列属性必须用无前缀写法（官方 -webkit-mask* 0 处、mask-image 26 处）。
 *
 * 用法：node tools/check-css.mjs       （exit 0 = 通过）
 */
import fs from "fs";
import path from "path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const cssFile = path.join(ROOT, "UI", "src", "styles.ts");
const srcDir = path.join(ROOT, "UI", "src");


/** 官方 index.css 里核对过定义的 token（值为「至少有一处定义」）。新增 token 必须先确认存在再进来。 */
const TOKENS = new Set([
  "accentColorNormal", "accentColorNormal-hover", "accentColorNormal-pressed",
  "accentColorLight", "accentColorDark",
  "selectedColor", "selectedColor-hover",
  "normalTextColor", "positiveColor", "warningColor", "negativeColor",
  "floatingToggleSize", "floatingToggleBorderRadius",
  "panelColorDark", "screenPadding", "panelRadius", "menuBlur",
  "stroke1", "stroke2", "gap2", "fontSizeS", "fontSizeM", "iconColor",
]);

/** 出现这些属性名 + 值里有 var() 就判错（R1）。 */
const SHORTHAND = new Set([
  "background", "border", "border-top", "border-right", "border-bottom", "border-left",
  "border-color", "border-width", "border-style", "border-radius", "margin", "padding",
  "font", "flex", "flow", "transition", "box-shadow", "filter", "text-shadow", "list-style",
  "outline", "grid", "grid-area", "animation",
]);

const BANNED_PROPS = ["object-fit", "outline", "word-wrap", "text-rendering"];
const BANNED_PSEUDO = [":focus-within", ":first-of-type", ":last-of-type", ":nth-of-type", ":has("];

const fails = [];
const warn = (rule, where, msg) => fails.push(rule + "  " + where + "  → " + msg);

/** 把 CSS 文本切成 {selector, decls[]} 太啰嗦，这里只按「声明行」粒度检查就够用。 */
function checkDecls(name, css) {
  const lines = css.split("\n");
  lines.forEach((raw, idx) => {
    const line = raw.trim();
    if (!line || line.startsWith("/*") || line.startsWith("*") || line.startsWith("@")) return;
    const at = name + ":" + (idx + 1);
    for (const b of BANNED_PROPS) {
      if (new RegExp("(^|[;{\\s])" + b + "\\s*:").test(line)) warn("R4", at, "不认的属性 " + b + "：" + line);
    }
    for (const p of BANNED_PSEUDO) if (line.includes(p)) warn("R5", at, "不认的伪类 " + p + "：" + line);
    if (/data:image|data:application|data:text/.test(line)) warn("R3", at, "data URI 不吃：" + line.slice(0, 90));
    if (/-webkit-mask/i.test(line)) warn("R7", at, "mask 用无前缀写法：" + line);

    // 逐个声明拆开看：prop:value（一行里可能有多个）
    for (const decl of line.replace(/[{}]/g, "").split(";").filter(Boolean)) {
      const i = decl.indexOf(":");
      if (i < 0) continue;
      const prop = decl.slice(0, i).trim().toLowerCase();
      const value = decl.slice(i + 1);
      if (!prop || prop.startsWith("/")) continue;
      if (SHORTHAND.has(prop) && value.includes("var(")) {
        warn("R1", at, "shorthand " + prop + " 里不能放 var()，改长写法或字面量：" + decl.trim());
      }
      const vars = value.match(/var\(\s*--[a-zA-Z0-9-]+[^)]*\)/g) || [];
      for (const v of vars) {
        if (v.includes(",")) warn("R2", at, "var() 的逗号兜底 Cohtml 解析不了：" + v);
        const nm = (v.match(/--([a-zA-Z0-9-]+)/) || [])[1];
        if (nm && !TOKENS.has(nm)) warn("R6", at, "token --" + nm + " 不在官方已核对白名单里");
      }
    }
  });
}

if (!fs.existsSync(cssFile)) {
  console.error("找不到 " + cssFile);
  process.exit(2);
}
const tsText = fs.readFileSync(cssFile, "utf8");
const m = tsText.match(/PANEL_CSS = `([\s\S]*?)`;!/g) || tsText.match(/PANEL_CSS = `([\s\S]*?)`;/g);
if (!m) {
  console.error("styles.ts 里没抓到 PANEL_CSS 模板串");
  process.exit(2);
}
checkDecls("UI/src/styles.ts", m.map((x) => x.replace(/PANEL_CSS = `|`;!?/g, "")).join("\n"));

// 组件里的内联 style 对象同样跑一遍（把 camelCase 还原成 kebab 再按声明检查）
/** 注释里引用「错误写法」是常态（本项目的规则说明就是这么写的），所以先剥掉注释再扫。
 *  只剥块注释和整行 // 注释：coui:// 与 https:// 出现在字符串里，naive 的行注释正则会把它砍断。 */
const stripComments = (s) => s
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .split("\n")
  .filter((l) => !l.trim().startsWith("//"))
  .join("\n");

for (const f of fs.readdirSync(srcDir)) {
  if (!/\.tsx?$/.test(f) || f === "styles.ts") continue;
  const txt = stripComments(fs.readFileSync(path.join(srcDir, f), "utf8"));
  const hits = [];
  const re = /(\w+)\s*:\s*(["'`][^"'`]*["'`]|[A-Za-z0-9_.]+\([^)]*\)|[A-Za-z0-9_.]+)/g;
  let mm;
  while ((mm = re.exec(txt)) !== null) {
    const prop = mm[1].replace(/[A-Z]/g, (c) => "-" + c.toLowerCase());
    const value = mm[2];
    const line = txt.slice(0, mm.index).split("\n").length;
    if (/-webkit-mask|maskImage/i.test(mm[1])) hits.push("R7  " + f + ":" + line + "  → mask 用无前缀写法（官方 -webkit-mask* 0 处）");
    if (/data:image/.test(value)) hits.push("R3  " + f + ":" + line + "  → 内联样式里的 data URI：" + value.slice(0, 60));
    if (/^objectFit$|^outline$|^wordWrap$|^textRendering$/.test(mm[1])) hits.push("R4  " + f + ":" + line + "  → 不认的属性 " + mm[1]);
    if (prop === "background" && /var\(/.test(value)) hits.push("R1  " + f + ":" + line + "  → background 里放 var()");
  }
  fails.push(...hits);
}

if (fails.length) {
  console.log("CSS 门禁：不通过（" + fails.length + " 条）");
  for (const f of fails) console.log("  · " + f);
  console.log("\n规则出处见本文件头部注释（R1~R7，全部来自真机 UI.log + 官方 index.css 统计）。");
  console.log("注：组件文件是先剥注释再扫的，所以 .tsx 的行号是「去注释后」的行号，定位时按上下文找。");
  process.exit(1);
}
console.log("CSS 门禁：通过（styles.ts + " + (fs.readdirSync(srcDir).filter((f) => /\.tsx?$/.test(f) && f !== "styles.ts").length) + " 个组件文件；token 白名单 " + TOKENS.size + " 条）");
