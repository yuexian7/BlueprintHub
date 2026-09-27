import React from "react";
import { ModRegistrar } from "cs2/modding";
import { C, IconClose, IconLogo, IconUser } from "./icons";
import { avatarColor, cmd, UiState, useUiState } from "./api";
import { BrowseBody } from "./browse";
import { ensureStyles } from "./styles";

/**
 * 蓝图工坊面板外壳（需求 1、6）。
 * 两个挂载点：
 *   GameTopLeft → 入口小按钮（需求 7 要求游戏里也要有个按钮）
 *   Game        → 面板本体（FACT：游戏 UI 里可选槽位为 Menu / Editor / Game / GameTopLeft / GameTopRight /
 *                 GameBottomLeft / GameBottomRight / UniversalModMenu，实测自 index.js 的 name: 字面量）
 * 两处各自渲染同一份 C# 状态，不做本地镜像 —— 谁都不许自己攒数据。
 */

const Header = ({ st }: { st: ReturnType<typeof useUiState> }) => {
  const nick: string = "";                   // M3 接平台身份；现在恒为空 → 显示通用头像
  return (
    <div className="bph-head">
      <div className="bph-head-row">
        <div className="bph-brand">
          <span className="bph-logo"><IconLogo size={34} /></span>
          <span className="bph-brand-txt">
            <span className="bph-brand-name">{st.modAuthor}</span>
            <span className="bph-brand-ver">v{st.modVersion}</span>
          </span>
        </div>
        <div className="bph-title">{st.title}</div>
        <div className="bph-spacer" />
        <div className="bph-hint">{st.hint}</div>
        <div
          className="bph-avatar"
          style={nick ? { background: avatarColor(nick), borderColor: C.line } : undefined}
          title={nick ? "个人主页" : "个人主页（账号面板在 M3 开放）"}
          onClick={() => cmd("avatar")}
        >
          {nick ? <span className="bph-avatar-txt">{nick.substr(0, 1).toUpperCase()}</span> : <IconUser size={18} />}
        </div>
        <div className="bph-close" title="关闭" onClick={() => cmd("close")}>
          <IconClose size={16} color={C.text} />
        </div>
      </div>
      {st.subtitle ? <div className="bph-sub">{st.subtitle}</div> : null}
    </div>
  );
};

const Toast = ({ st }: { st: ReturnType<typeof useUiState> }) => {
  if (!st.toast || !st.toast.text) return null;
  const kind = st.toast.kind === "error" ? "-err" : st.toast.kind === "warn" ? "-warn" : st.toast.kind === "ok" ? "-ok" : "";
  return <div key={st.toast.id} className={"bph-toast bph-toast" + kind}>{st.toast.text}</div>;
};

const Panel = () => {
  const st = useUiState();
  if (!st.visible) return null;
  const alpha = Math.max(0.2, Math.min(1, st.opacity || 0.5));
  return (
    <div className="bph-root">
      <div
        className="bph-panel"
        style={{
          background:
            "linear-gradient(180deg, rgba(23,34,47," + alpha.toFixed(3) + ") 0%, rgba(18,26,38," +
            alpha.toFixed(3) + ") 100%)",
        }}
      >
        <Header st={st} />
        <BrowseBody st={st} />
        <Toast st={st} />
      </div>
    </div>
  );
};

const Launcher = () => {
  const st = useUiState();
  if (st.visible) return null;               // 面板开着就不占左上角的位置
  return (
    <div className="bph-launch" title="蓝图工坊" onClick={() => cmd("open")}>
      <IconLogo size={24} />
      <span className="bph-launch-txt">蓝图工坊</span>
      {st.hotkey ? <span className="bph-launch-key">{st.hotkey}</span> : null}
    </div>
  );
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
  // 这行日志是排障的第一现场：没有它 = 模块根本没被 loader 拉到
  console.log("[BlueprintHub] UI module registered, slots=" + mounted);
};

/** 本模组没有独立 css 文件；loader 靠这个导出决定是否再拉同名 .css。 */
export const hasCSS = false;

export default register;
