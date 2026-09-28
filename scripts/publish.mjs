/**
 * 发布 0.3.0（用户口径：改好后上传线上，**保持 Private**；上传前先跑 pull-live.mjs 核对线上）。
 *
 *   node scripts/publish.mjs                 # Publish（首发，PublishConfiguration.xml 里 ModId 为空）
 *   node scripts/publish.mjs --new-version   # NewVersion（ModId 已有值时用这个，别再造新条目）
 *   node scripts/publish.mjs --update-only   # Update（只推元数据，不传内容）
 *
 * 内容包 = releases/<ModVersion>/ 里的 **BlueprintHub.dll + BlueprintHub.mjs + mod.json** 三件
 * （与已上架的 BusLineAutoStops 同一形态；.pdb 与三平台桩不上线）。
 *
 * 本机踩过的坑（都写进这里，免得下次再查）：
 *  · 工具链是 net6.0，本机只有 .NET 8 运行时 → 必须 DOTNET_ROLL_FORWARD=Major
 *  · 线上元数据会被**无条件覆盖**：DisplayName / 描述 / ChangeLog / ForumLink / ExternalLink /
 *    Thumbnail / Tag 全部以 XML 为准 → 网页端改过的东西必须先抄回 XML 再推（先跑 pull-live.mjs）
 *  · <Thumbnail> 指向的文件不存在时不报错，会静默换成 Colossal 官方占位图
 *  · 平台侧限制：**每个账号最多 3 个 Private 模组**，满了会直接 "You have too many private mods, max 3."
 *    → 这时不能改成 Public 绕过（那是玩家可见的公开动作），要先由作者决定腾哪个位子
 */
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const CFG = path.join("Properties", "PublishConfiguration.xml");
const GAME = "F:\\SteamLibrary\\steamapps\\common\\Cities Skylines II";
const PUBLISHER = path.join(GAME, "Cities2_Data", "Content", "Game", ".ModdingToolchain", "ModPublisher", "ModPublisher.dll");
const DEPLOY = path.join(os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", "Mods", "BlueprintHub");

const xml = fs.readFileSync(path.join(ROOT, CFG), "utf8");
const ver = (xml.match(/<ModVersion Value="([^"]*)"/) || [])[1];
const modId = (xml.match(/<ModId Value="([^"]*)"/) || [])[1] || "";
const access = (xml.match(/<AccessLevel Value="([^"]*)"/) || [])[1];
const args = process.argv.slice(2);
const cmd = args.includes("--new-version") ? "NewVersion" : args.includes("--update-only") ? "Update" : "Publish";

if (!fs.existsSync(PUBLISHER)) { console.error("✗ 找不到 ModPublisher：" + PUBLISHER); process.exit(1); }
if (cmd === "Publish" && modId) { console.error("✗ PublishConfiguration.xml 已有 ModId=" + modId + "，该走 --new-version，别造重复条目"); process.exit(1); }
if (cmd === "NewVersion" && !modId) { console.error("✗ NewVersion 需要 ModId（先跑 node scripts/pull-live.mjs 查线上）"); process.exit(1); }

const out = path.join(ROOT, "releases", ver);
fs.mkdirSync(out, { recursive: true });
for (const f of ["BlueprintHub.dll", "BlueprintHub.mjs", "mod.json"]) {
  const src = path.join(DEPLOY, f);
  if (!fs.existsSync(src)) { console.error("✗ 本机部署目录缺 " + f + "（先 dotnet build -c Release）"); process.exit(1); }
  fs.copyFileSync(src, path.join(out, f));
}
const banner = fs.readFileSync(path.join(out, "BlueprintHub.mjs"), "utf8").slice(0, 200);
if (!/Version: " \+ ver/.test(banner) && !banner.includes("Version: " + ver))
  console.warn("⚠ .mjs 横幅里的 Version 与 " + ver + " 不一致：游戏解析 moduleInfo 用的就是这行，回 UI/mod.json 改齐再重跑 webpack");

console.log(`→ ${cmd} v${ver} · AccessLevel=${access} · 内容 ${out}`);
for (const f of fs.readdirSync(out)) console.log("   ", f, fs.statSync(path.join(out, f)).size, "bytes");

const r = spawnSync("dotnet", [PUBLISHER, cmd, CFG, "--contentFolder", path.join("releases", ver)], {
  cwd: ROOT, encoding: "utf8", env: { ...process.env, DOTNET_ROLL_FORWARD: "Major" },
});
const text = (r.stdout || "") + (r.stderr || "");
console.log(text.trim());
if (r.status !== 0 || /failed|Could not publish/i.test(text)) {
  console.error("✗ 发布未成功（退出码 " + r.status + "）");
  process.exit(1);
}
const got = text.match(/(\d{4,8})/g);
console.log("✓ 已上传。若这是首发，把返回的 ModId 写进 Properties/PublishConfiguration.xml 的 <ModId>，之后一律走 --new-version。");
if (got) console.log("  输出里出现的数字（可能就是 ModId）：" + got.slice(0, 6).join(", "));
