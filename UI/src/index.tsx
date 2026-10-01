import React from "react";
import { ModRegistrar } from "cs2/modding";
import { C, IconClose, IconLogo, LAUNCHER_GLYPH_SRC } from "./icons";
import { cmd, useUiState } from "./api";
import { BrowseBody } from "./browse";
import { DistrictUploadSectionView, DISTRICT_SECTION_TYPE, DistrictFallbackHint } from "./district";
import { ensureStyles } from "./styles";
import { useT } from "./l10n";
import * as GameUI from "cs2/ui";

/**
 * 蓝图工坊面板外壳。三个挂载点：
 *   GameTopLeft → 入口方块（官方组件 FloatingButton，theme 就是原版的 .button_ke4）
 *   Game        → 面板本体
 *   selectedInfoSectionComponents → 市辖区面板底部那颗「上传蓝图」（需求 2）
 * 三处各自渲染同一份 C# 状态，不做本地镜像 —— 谁都不许自己攒数据。
 *
 * 槽位（FACT，运行时从游戏 bundle 里数出来的 ModdingHook 名字，共 8 个）：
 * Menu / Editor / Game / GameTopLeft / GameTopRight / GameBottomLeft / GameBottomRight / UniversalModMenu。
 * 官方模板的 .d.ts 少写了 GameBottomLeft —— 以 bundle 为准（0.4.0 复核）。
 */

// 原版组件库是 window["cs2/ui"]（webpack externals）。拿不到时退成自画的同款方块，
// 而不是让入口凭空消失 —— 游戏的模块 loader 会静默吞掉顶层抛错，这一层降级是本模组唯一的报警面。
let GameButton: React.ComponentType<Record<string, unknown>> | null = null;
let GameFloatingButton: React.ComponentType<Record<string, unknown>> | null = null;
let GameTooltip: React.ComponentType<Record<string, unknown>> | null = null;
try {
  const ui: any = GameUI;
  if (ui) {
    if (ui.Button) GameButton = ui.Button as React.ComponentType<Record<string, unknown>>;
    if (ui.FloatingButton) GameFloatingButton = ui.FloatingButton as React.ComponentType<Record<string, unknown>>;
    if (ui.Tooltip) GameTooltip = ui.Tooltip as React.ComponentType<Record<string, unknown>>;
  }
} catch (e) {
  console.warn("[BlueprintHub] cs2/ui unavailable", e);
}

const Header = ({ st }: { st: ReturnType<typeof useUiState> }) => {
  const t = useT();
  const acc = st.account || { loggedIn: false, avatar: "", author: "" };
  return (
    <div className="bph-head">
      <span className="bph-logo"><IconLogo size={30} /></span>
      <span className="bph-title">{t(st.titleSlug)}</span>
      <span className="bph-ver">v{st.modVersion}</span>
      {st.dev ? <span className="bph-dev">{t("DEV_TAG")}</span> : null}
      <div className="bph-spacer" />
      {st.hotkey ? <span className="bph-hint">{t("HOTKEY_TIP", { key: st.hotkey })}</span> : null}
      {/* 需求 3：右上角不再是上传按钮 —— 那里现在是账号按钮，左边一句淡蓝提示。
          上传的入口在**市辖区面板**（需求 2），钩子没挂上时才由 DistrictFallbackHint 兜底。 */}
      <span className="bph-share">{t("UPLOAD_HINT")}</span>
      {acc.loggedIn && acc.avatar
        ? <img className="bph-avatar" src={acc.avatar} alt="" title={t("ACCOUNT_PROFILE_TIP")} onClick={() => cmd("profile")} />
        : (acc.loggedIn
          ? <div className="bph-avatar-box" title={t("ACCOUNT_PROFILE_TIP")} onClick={() => cmd("profile")}>
              <span className="bph-avatar-dot" />
            </div>
          : <div className="bph-login-box" title={t("ACCOUNT_LOGIN_TIP")} onClick={() => cmd("account")}>{t("ACCOUNT_LOGIN")}</div>)}
      <div className="bph-hbtn" title={t("BTN_CLOSE")} onClick={() => cmd("close")}>
        <IconClose size={16} />
      </div>
    </div>
  );
};

const Toast = ({ st }: { st: ReturnType<typeof useUiState> }) => {
  const t = useT();
  if (!st.toast || !st.toast.slug) return null;
  const kind = st.toast.kind === "error" ? "-err" : st.toast.kind === "warn" ? "-warn" : st.toast.kind === "ok" ? "-ok" : "";
  return <div key={st.toast.id} className={"bph-toast bph-toast" + kind}>{t(st.toast.slug)}</div>;
};

const Panel = () => {
  const st = useUiState();
  const t = useT();
  if (!st.visible) return null;
  // 透明度就是玩家在那根滑条上看到的数：0% = 背景完全透明，不做「其实最少还有 20%」这种小动作
  const alpha = Math.max(0, Math.min(1, st.opacity));
  const a = alpha.toFixed(3);
  return (
    <div className="bph-root">
      <div
        className="bph-panel"
        style={{ background: "linear-gradient(180deg, rgba(23,34,47," + a + ") 0%, rgba(18,26,38," + a + ") 100%)" }}
      >
        <Header st={st} />
        <DistrictFallbackHint />
        <BrowseBody st={st} t={t} />
        <Toast st={st} />
      </div>
    </div>
  );
};

/**
 * 左上角入口方块。三处都是照游戏自己的做法来的（证据见 README「入口方块为什么长这样」）：
 *  · 位置**不用模组自己算**：GameTopLeft 的容器 .info-menu-layout 是 position:absolute;top/left:10rem
 *    的 display:flex 行，且 `>*{margin:0 6rem 6rem 0}` —— 官方方块和所有模组的方块都在这一行里自动排。
 *    0.3.0 之所以和 Road Builder / Write Everywhere 叠在一起，是因为我方块里塞了一个
 *    `width:100%;height:100%` 的涂色 span（mask 没生效 → 变成实心白块），方块本身又没有
 *    flex:0 0 auto 保护 → 被当成可伸缩、可撑破的内容。现在：外层只管占位（不伸不缩），
 *    尺寸由官方 .button_ke4 决定，图案交给 Button 的 src（缺图时官方占位图顶上，最差是蓝底无图案）。
 *  · 组件优先用官方 FloatingButton（= Button + tinted + floating theme），拿不到再退 Button
 *    variant="floating"，连 cs2/ui 都没有才自画同款。
 */
const Launcher = () => {
  const st = useUiState();
  const t = useT();
  if (st.visible) return null;               // 面板开着就不占左上角的位置
  const tip = st.hotkey ? t("HOTKEY_TIP", { key: st.hotkey }) : t("PANEL_TITLE");
  const open = () => cmd("open");
  let tile: React.ReactElement;
  if (GameFloatingButton) {
    tile = <GameFloatingButton src={LAUNCHER_GLYPH_SRC} onSelect={open} />;
  } else if (GameButton) {
    tile = <GameButton variant="floating" src={LAUNCHER_GLYPH_SRC} onSelect={open} />;
  } else {
    tile = <div className="bph-launch" onClick={open}><img className="bph-launch-img" src={LAUNCHER_GLYPH_SRC} alt="" /></div>;
  }
  const inner = GameTooltip ? <GameTooltip tooltip={tip}>{tile}</GameTooltip> : tile;
  return <div className="bph-toggle">{inner}</div>;
};

/**
 * 市辖区面板的条目映射表（游戏自己的模块路径）。
 * FACT：bundle 里 `Q.add("game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
 * {get selectedInfoSectionComponents(){return xMe}…})`（index.js @2095653），而 xMe 是一个 `var`
 * 的对象「section 类型全名 → 组件」，渲染时才读（`components: xMe`）→ 可以被 extend 换掉。
 * 已上架的 AdvancedBuildingControl 就是这么把条目塞进同一个面板的（它的 .mjs 里是
 * map["AdvancedBuildingControl.Systems.SIP_ABC"]），键的形状与本模组一致。
 */
const SECTIONS_PATH = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";

/** 挂上去成不成，一定要回告 C#：不然 hooks.districtSection 会报假阳性，玩家点了按钮找不到入口。 */
function attachDistrictSection(registry: any): boolean {
  try {
    if (!registry || typeof registry.extend !== "function") return false;
    registry.extend(SECTIONS_PATH, "selectedInfoSectionComponents", (orig: any) => {
      const map: { [k: string]: unknown } = orig && typeof orig === "object" ? Object.assign({}, orig) : {};
      map[DISTRICT_SECTION_TYPE] = (props: any) => <DistrictUploadSectionView {...(props || {})} />;
      return map;
    });
    return true;
  } catch (e) {
    console.warn("[BlueprintHub] extend selectedInfoSectionComponents failed", e);
    return false;
  }
}

const register: ModRegistrar = (moduleRegistry) => {
  try { ensureStyles(); } catch (e) { console.warn("[BlueprintHub] styles", e); }
  let mounted = 0;
  try {
    moduleRegistry.append("GameTopLeft", () => <Launcher />);
    mounted++;
  } catch (e) { console.warn("[BlueprintHub] append GameTopLeft failed", e); }
  try {
    moduleRegistry.append("Game", () => <Panel />);
    mounted++;
  } catch (e) { console.warn("[BlueprintHub] append Game failed", e); }
  const districtHook = attachDistrictSection(moduleRegistry);
  try { cmd("hookok", districtHook ? "1" : "0"); } catch (e) { /* 报不出去也只是少了一条兜底提示 */ }
  // 这几行日志是排障的第一现场：没有它 = 模块根本没被 loader 拉到
  console.log("[BlueprintHub] UI module registered, slots=" + mounted +
    ", nativeUi=" + (GameButton ? "yes" : "fallback") + ", districtHook=" + (districtHook ? "yes" : "no"));
};

/** 本模组没有独立 css 文件；loader 靠这个导出决定是否再拉同名 .css。 */
export const hasCSS = false;

export default register;
