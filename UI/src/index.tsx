import React from "react";
import { ModRegistrar } from "cs2/modding";
import { C, IconClose, IconLogo, IconUpload, glyphStyle } from "./icons";
import { cmd, useUiState } from "./api";
import { BrowseBody } from "./browse";
import { ensureStyles } from "./styles";
import { useT } from "./l10n";
import * as GameUI from "cs2/ui";

/**
 * 蓝图工坊面板外壳。两个挂载点：
 *   GameTopLeft → 入口按钮（就用原版组件 Button variant="floating"：40rem 方块、6rem 圆角、
 *                --accentColorNormal 蓝底 —— 与旁边其它模组的入口是同一套规范，不是我在模仿它）
 *   Game        → 面板本体（FACT：可选槽位 Menu / Editor / Game / GameTopLeft / GameTopRight /
 *                GameBottomLeft / GameBottomRight / UniversalModMenu，实测自 index.js 的 name: 字面量）
 * 两处各自渲染同一份 C# 状态，不做本地镜像 —— 谁都不许自己攒数据。
 */

// 原版组件库是 window["cs2/ui"]（webpack externals）。拿不到时退成自画的同款方块，
// 而不是让入口凭空消失 —— 游戏的模块 loader 会静默吞掉顶层抛错，这一层降级是本模组唯一的报警面。
let GameButton: React.ComponentType<Record<string, unknown>> | null = null;
let GameTooltip: React.ComponentType<Record<string, unknown>> | null = null;
try {
  const ui: any = GameUI;
  if (ui) {
    if (ui.Button) GameButton = ui.Button as React.ComponentType<Record<string, unknown>>;
    if (ui.Tooltip) GameTooltip = ui.Tooltip as React.ComponentType<Record<string, unknown>>;
  }
} catch (e) {
  console.warn("[BlueprintHub] cs2/ui unavailable", e);
}

const Header = ({ st }: { st: ReturnType<typeof useUiState> }) => {
  const t = useT();
  return (
    <div className="bph-head">
      <span className="bph-logo"><IconLogo size={30} /></span>
      <span className="bph-title">{t(st.titleSlug)}</span>
      <span className="bph-ver">v{st.modVersion}</span>
      {st.dev ? <span className="bph-dev">{t("DEV_TAG")}</span> : null}
      <div className="bph-spacer" />
      {st.hotkey ? <span className="bph-hint">{t("HOTKEY_TIP", { key: st.hotkey })}</span> : null}
      {/* 需求 5：右上角是图标按钮，不是一句话 */}
      <div className="bph-hbtn bph-hbtn-acc" title={t("BTN_UPLOAD")} onClick={() => cmd("upload")}>
        <IconUpload size={18} />
      </div>
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
        <BrowseBody st={st} t={t} />
        <Toast st={st} />
      </div>
    </div>
  );
};

const Launcher = () => {
  const st = useUiState();
  const t = useT();
  if (st.visible) return null;               // 面板开着就不占左上角的位置
  const tip = st.hotkey ? t("HOTKEY_TIP", { key: st.hotkey }) : t("PANEL_TITLE");
  const open = () => cmd("open");
  const glyph = <span className="bph-launch-glyph" style={glyphStyle} />;
  const btn = GameButton
    ? <GameButton variant="floating" onSelect={open}>{glyph}</GameButton>
    : <div className="bph-launch" onClick={open}>{glyph}</div>;
  return GameTooltip ? <GameTooltip tooltip={tip}>{btn}</GameTooltip> : btn;
};

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
  // 这两行日志是排障的第一现场：没有它 = 模块根本没被 loader 拉到
  console.log("[BlueprintHub] UI module registered, slots=" + mounted + ", nativeUi=" + (GameButton ? "yes" : "fallback"));
};

/** 本模组没有独立 css 文件；loader 靠这个导出决定是否再拉同名 .css。 */
export const hasCSS = false;

export default register;
