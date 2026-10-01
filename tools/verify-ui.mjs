#!/usr/bin/env node
/**
 * 预览台 DOM 断言（只给开发用，不进模组产物）：
 *   node tools/dev-preview.mjs &   然后   node tools/verify-ui.mjs
 *
 * 与 shot.mjs 的分工：截图给人看，这份给机器判 —— 每条都对应作者点名的一个需求，
 * 用 chrome --headless --dump-dom 把「页面自己把 ?act= 跑完之后的 DOM」抓下来做断言。
 * 于是「点了第二个下拉，第一个必须收起」这种**交互后**的状态也能被反复判，而不是靠人眼盯图。
 *
 * 仍然测不到的是游戏本体：Cohtml 的字体/rem 基准/图片缓存、以及真 ECS 采集。
 * 这份全绿只说明「前端与桥层自洽」，不说明「实机一定对」。
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { findChrome, findBase, encodeQuery } from "./preview-common.mjs";

const args = process.argv.slice(2);
const only = (args.find((a) => a.startsWith("--only=")) || "").slice(7);
const keep = args.includes("--keep-dump");
const BUDGET = Number((args.find((a) => a.startsWith("--budget=")) || "").split("=")[1] || 2600);

const chrome = findChrome();
if (!chrome) { console.error("✗ 找不到 Chrome/Edge（设 BPH_CHROME=<chrome.exe>）"); process.exit(2); }

// ---- 断言表：q=URL 参数；has=必须出现的片段；not=必须不出现；counts=[[css 片段, 期望次数], ...] ----
const CASES = [
  {
    name: "类别 8 项与措辞", q: "",
    has: ["全部", "住宅区", "商业区", "产业区", "公园区", "文教区", "交通枢纽区", "公共服务区"],
    not: ["工业区", "教育区", "混合区", ">公共区<"],
    counts: [["bph-cat-dot", 8]],          // 8 行类型（含「全部」），每行一个色点
  },
  { name: "选中类型才出定义（需求 9）", q: "?cat=transit", has: ["bph-side-desc", "以客运设施、货运设施、大型立交或复杂路网组成的区域"], counts: [["bph-side-desc", 1]] },
  { name: "「全部」不配定义（需求 8）", q: "", not: ["bph-side-desc"] },
  { name: "换算提示（需求 6）", q: "", has: ["1u = 8m × 8m", "1 地图区块 ≈ 6070.4u"] },
  { name: "面积三档按 u（需求 6）", q: "?act=click:.bph-drop-btn", has: ["小 · &lt;1000u", "中 · 1000~4000u", "大 · &gt;4000u"] },
  {
    name: "排序 8 项（需求 7）", q: "?act=clickN:.bph-drop-btn=1",
    has: ["最热门", "最近更新", "最晚创建", "最早创建", "面积最大", "面积最小", "名称 A→Z", "名称 Z→A"],
  },
  { name: "两个下拉互斥（需求 5）", q: "?act=click:.bph-drop-btn;wait:200;clickN:.bph-drop-btn=1", counts: [["bph-drop-menu", 1]], has: ["最晚创建"] },
  { name: "点外面收起（需求 5）", q: "?act=click:.bph-drop-btn;wait:200;click:.bph-drop-scrim", counts: [["bph-drop-menu", 0]] },
  { name: "顶栏：提示语 + 登录框（需求 3/4）", q: "", has: ["分享蓝图请进入市辖区面板上传！", "bph-login-box", "登录"] },
  { name: "登录后换成头像（需求 4）", q: "?logged=1", has: ["bph-avatar"], not: ["bph-login-box"] },
  { name: "卡片面积一律讲 u（需求 6）", q: "", has: ["12480u · 2.1 区块", "3900u · 0.6 区块"], not: ["㎡", "km²"] },
  { name: "市辖区面板的上传按钮（需求 2）", q: "?dpanel=1&logged=1", has: ["bph-dpanel", "上传蓝图"] },
  { name: "未登录时不给按（需求 2/4）", q: "?dpanel=1", has: ["bph-dneed", "登录 Paradox 账号后才能上传蓝图"], not: ["bph-form"] },
  { name: "表单：名称 + 7 个类型芯片（需求 2/8）", q: "?dpanel=1&logged=1&act=click:.bph-btn-main", has: ["bph-form", "value=\"滨江新区\"", "社区类型", "简介（可留空）"], counts: [["bph-chip ", 1], ["bph-chip\"", 6]] },
  { name: "改名后提交：名字走完整条命令链（需求 2）", q: "?dpanel=1&logged=1&act=click:.bph-btn-main;wait:200;text:.bph-input=试点区甲;wait:200;click:.bph-btn-main", has: ["试点区甲", "ModsData/BlueprintHub/drafts/"], budget: 3400 },
  { name: "采集进行中（需求 2）", q: "?dpanel=1&logged=1&uphase=busy", has: ["正在采集", "bph-busy"], not: ["bph-form", "bph-done"] },
  { name: "点提交后按钮先收起表单（需求 2）", q: "?dpanel=1&logged=1&act=click:.bph-btn-main;wait:200;click:.bph-btn-main", not: ["bph-form"], budget: 500 },
  { name: "完成态给出草稿路径（需求 2）", q: "?dpanel=1&logged=1&act=click:.bph-btn-main;wait:200;click:.bph-btn-main", has: ["ModsData/BlueprintHub/drafts/", "bph-path", "选中路径"], budget: 3400 },
  { name: "缺采计数进了句子（需求 2）", q: "?dpanel=1&logged=1&uphase=done&upmissing=1", has: ["有 3 项没采到"] },
  { name: "失败态说人话（需求 2）", q: "?dpanel=1&logged=1&uphase=failed", has: ["bph-fail", "这个市辖区内容太多", "单张上限 4000"] },
  { name: "钩子没挂上时留兜底入口（需求 2）", q: "?nohook=1", has: ["bph-fallback"] },
  { name: "钩子挂上了就不该看到兜底（需求 2）", q: "", not: ["bph-fallback"] },
  { name: "词条没漏：DOM 里不许出现裸 slug", q: "?cat=public_service&sort=createdAsc&area=large", not: ["CAT_", "SORT_", "AREA_", "DESC_", "AGO_", "DUPLOAD_", "ERR_", "EMPTY_"] },
  { name: "英文面板同一批词条", q: "?lang=en-US&cat=public_service", has: ["Public Services", "government offices"] },
  { name: "cs2/ui 缺席时的降级入口", q: "?closed=1&native=0", has: ["bph-launch"] },
];

const profile = path.join(os.tmpdir(), "BlueprintHub", "chrome-verify-" + Date.now().toString(16));
const dumps = path.join(os.tmpdir(), "BlueprintHub", "verify-dumps");
if (keep) fs.mkdirSync(dumps, { recursive: true });

const base = await findBase(Number(process.env.BPH_PREVIEW_PORT || 0));
if (!base) { console.error("✗ 预览台没在跑：先 node tools/dev-preview.mjs"); process.exit(2); }
console.log("预览台：" + base + " · 断言 " + CASES.length + " 条\n");

let pass = 0, fail = 0;
/**
 * 判据只看「页面画出来的东西」：
 *   body = 去掉 <script> 与 <style> 的 DOM —— 类名断言用它（模组的 ensureStyles 会把整份 CSS
 *          注进 <style>，不剥掉的话每个 .bph-xxx 都算「出现」，not 断言全成假警报）。
 *   text = body 再剥标签 —— 句子断言用它。
 * 内联 <script> 里躺着 window.__dict（全部 slug 键名）与 window.__state，同样必须排除。
 */
const clean = (s) => s.replace(/<script[\s\S]*?<\/script>/g, "").replace(/<style[\s\S]*?<\/style>/g, "");
const strip = (s) => s.replace(/<[^>]+>/g, "\n");
try {
  for (const c of CASES) {
    if (only && !c.name.includes(only)) continue;
    const url = base + "/" + encodeQuery(c.q);
    const run = spawnSync(chrome, [
      "--headless=new", "--disable-gpu", "--no-first-run", "--window-size=1600,900",
      "--user-data-dir=" + profile, "--virtual-time-budget=" + (c.budget || BUDGET), "--dump-dom", url,
    ], { encoding: "utf8", timeout: 60000 });
    const html = run.stdout || "";
    if (keep) fs.writeFileSync(path.join(dumps, c.name.replace(/[\\/:*?"<>|]/g, "_") + ".html"), html);
    const body = clean(html);
    const text = strip(body);
    const seen = (needle) => body.includes(needle) || text.includes(needle);
    const bad = [];
    for (const h of c.has || []) if (!seen(h)) bad.push("缺「" + h + "」");
    for (const n of c.not || []) if (seen(n)) bad.push("出现了不该出现的「" + n + "」");
    for (const [pat, want] of c.counts || []) {
      const got = body.split(pat).length - 1;
      if (got !== want) bad.push(`「${pat}」出现 ${got} 次，应为 ${want}`);
    }
    if (bad.length) { fail++; console.log("  ✗ " + c.name + "\n     " + bad.join("\n     ") + "\n     url: " + url); }
    else { pass++; console.log("  ✓ " + c.name); }
  }
} finally {
  for (let i = 0; i < 5; i++) {
    try { fs.rmSync(profile, { recursive: true, force: true }); break; }
    catch { const t = Date.now(); while (Date.now() - t < 300) { /* Windows 没有 sleep 可 spawn */ } }
  }
  if (fs.existsSync(profile)) console.log("  ⚠ 临时浏览器目录没删掉：" + profile);
}
console.log(`\n断言 ${pass} 通过 / ${fail} 失败${keep ? " · DOM 留在 " + dumps : ""}`);
process.exitCode = fail ? 1 : 0;
