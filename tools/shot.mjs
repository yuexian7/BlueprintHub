#!/usr/bin/env node
/**
 * 预览台实拍（只给开发用，不进模组产物）：
 *   node tools/dev-preview.mjs &            # 先把预览台起起来
 *   node tools/shot.mjs                     # 一轮 12 张，落到 %TEMP%/BlueprintHub/shots/<时间戳>/
 *   node tools/shot.mjs --only=panel,drop-area --size=1600x900
 *
 * 为什么用 Chrome headless 而不是 Qoder 的浏览器：面板要在没有游戏的情况下反复拍同一个态，
 * 命令行一次一张、可脚本化，且不吃「窗口有没有显示在前台」这种环境条件。
 *
 * 交互怎么拍：headless 一次只交一张图，没有点击能力 —— 所以点击动作写进 ?act=（见 dev-preview 的 actScript），
 * 页面自己把 click / 填字 / 等待跑一遍，再配 --virtual-time-budget 让 setTimeout 在预算内走完。
 * 于是「点了面积下拉」「点了排序下拉」「提交上传后 1.2 秒」都是可以被固定拍下来的状态。
 *
 * 临时目录：每次跑都建一个专属 --user-data-dir，跑完（含出错）删掉；截图目录留在 TEMP 里给人看，不写进仓库。
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { findChrome, findBase, encodeQuery } from "./preview-common.mjs";

const args = process.argv.slice(2);
const opt = (name, dft) => {
  const hit = args.find((a) => a.startsWith("--" + name + "="));
  return hit ? hit.slice(name.length + 3) : dft;
};

const PORT = Number(opt("port", process.env.BPH_PREVIEW_PORT || 0)) || 0;
const SIZE = opt("size", "1600x900");
const OUT_ROOT = opt("out", path.join(os.tmpdir(), "BlueprintHub", "shots"));
const BUDGET = Number(opt("budget", "2600"));

const chrome = findChrome();
if (!chrome) {
  console.error("✗ 找不到 Chrome/Edge。设一下环境变量 BPH_CHROME=<chrome.exe 路径>（或 --port= 指给你的预览台）");
  process.exit(2);
}

// ---- 要拍的状态清单：每一条都对应作者点名的一个需求，名字会进文件名 ----
const SHOTS = [
  { name: "01-closed", q: "?closed=1", note: "左上角入口方块（需求：位置跟官方同一排）" },
  { name: "02-panel", q: "", note: "面板默认视图：8 类左栏 + 换算提示 + 账号按钮" },
  { name: "03-cat-desc", q: "?cat=transit", note: "需求 8/9：选中「交通枢纽区」，定义显示在左栏清单下方" },
  { name: "04-area-8", q: "?area=small", note: "需求 6：面积档按 u（<1000u）" },
  { name: "05-sort-created", q: "?sort=createdAsc", note: "需求 7：最晚/最早创建" },
  { name: "06-drop-area", q: `?act=click:.bph-drop-btn`, note: "需求 5：点开面积下拉" },
  { name: "07-drop-sort", q: `?act=click:.bph-drop-btn;wait:200;clickN:.bph-drop-btn=1`, note: "需求 5：点第二个下拉 → 第一个自动收起（openId 只有一个）" },
  { name: "08-drop-scrim", q: `?act=click:.bph-drop-btn;wait:200;click:.bph-drop-scrim`, note: "需求 5：点遮罩 → 收起" },
  { name: "09-empty", q: "?status=empty", note: "空态" },
  { name: "10-error", q: "?status=error", note: "失败态" },
  { name: "11-en", q: "?lang=en-US", note: "英文面板（同一批词条）" },
  { name: "12-district", q: "?dpanel=1&logged=1", note: "需求 2：市辖区面板里的上传按钮（已登录）" },
  { name: "13-district-out", q: "?dpanel=1", note: "需求 2/4：未登录时按钮不可点并提示登录" },
  { name: "14-district-form", q: `?dpanel=1&logged=1&act=click:.bph-btn-main;wait:200;text:.bph-input=滨江新区试点;wait:200`, note: "需求 2：表单展开 + 改名（类型芯片、简介框）" },
  { name: "15-district-busy", q: `?dpanel=1&logged=1&uphase=busy`, note: "需求 2：正在采集（钉住这一态：headless 的虚拟时钟下靠轮询追不上）" },
  { name: "16-district-done", q: `?dpanel=1&logged=1&act=click:.bph-btn-main;wait:200;click:.bph-btn-main`, note: "需求 2：采集完成给出草稿路径", budget: 3200 },
  { name: "17-district-fail", q: "?dpanel=1&logged=1&uphase=failed", note: "需求 2：失败态（太大）" },
  { name: "18-nohook", q: "?nohook=1", note: "钩子没挂上时主面板的兜底入口（不是死路）" },
  { name: "19-account", q: "?logged=1", note: "需求 4：登录后顶栏显示头像" },
  { name: "20-fallback-btn", q: "?native=0", note: "cs2/ui 拿不到时的降级入口方块" },
];

const only = opt("only", "");
const wanted = only ? SHOTS.filter((s) => only.split(",").some((k) => s.name.includes(k.trim()))) : SHOTS;

const base = await findBase(PORT);
if (!base) {
  console.error("✗ 预览台没在跑。先起一个：node tools/dev-preview.mjs（默认 8765，被占会自动顺延，日志里有实际地址）");
  process.exit(2);
}
console.log("预览台：" + base + " · 浏览器：" + chrome);

const stamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
const OUT = path.join(OUT_ROOT, stamp);
fs.mkdirSync(OUT, { recursive: true });
const profile = path.join(os.tmpdir(), "BlueprintHub", "chrome-profile-" + stamp);
const [w, h] = SIZE.split("x");

let ok = 0, bad = 0;
try {
  for (const s of wanted) {
    const url = base + "/" + encodeQuery(s.q);
    const file = path.join(OUT, s.name + ".png");
    const run = spawnSync(chrome, [
      "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
      "--window-size=" + w + "," + h,
      "--user-data-dir=" + profile,
      "--virtual-time-budget=" + (s.budget || BUDGET),
      "--screenshot=" + file,
      url,
    ], { encoding: "utf8", timeout: 60000 });
    const size = fs.existsSync(file) ? fs.statSync(file).size : 0;
    if (size > 3000) { ok++; console.log(`  ✓ ${s.name.padEnd(20)} ${String(Math.round(size / 1024)).padStart(4)}KB  ${s.note}`); }
    else {
      bad++;
      console.log(`  ✗ ${s.name.padEnd(20)} 没拍出图（${size}B）${(run.stderr || "").split("\n").filter(Boolean).slice(-1)[0] || ""}`);
      console.log(`     url: ${url}`);
    }
  }
} finally {
  // Chrome 偶尔会在退出时留下锁文件 —— 等一下再试几次，别让一次实拍在临时目录里留个垃圾堆
  for (let i = 0; i < 5; i++) {
    try { fs.rmSync(profile, { recursive: true, force: true }); break; }
    catch { const t = Date.now(); while (Date.now() - t < 300) { /* Windows 下没有 sleep 可 spawn */ } }
  }
  if (fs.existsSync(profile)) console.log("  ⚠ 临时浏览器目录没删掉（Chrome 还占着）：" + profile);
  else console.log("✓ 临时浏览器目录已清理");
}

console.log(`\n实拍 ${ok} 张成功 / ${bad} 张失败 → ${OUT}`);
console.log("拍完直接看：" + OUT);
if (bad) process.exitCode = 1;
