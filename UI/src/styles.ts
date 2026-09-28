/**
 * 面板样式：整份 CSS 内联成一个字符串，由 ensureStyles() 注进 <head>。
 * 为什么不用 .css 文件：游戏 loader 会按模块旁的同名 .css 去拉（exports.hasCSS 决定），
 * 少一个文件 = 少一个 404 面（BusLineAutoStops 的结论）。
 *
 * 单位：全部 rem。Cohtml 里 1rem ≈ 1080p 下的 1px（与已上架的 BusLineAutoStops 面板同一口径）。
 * 故意不设 font-family：让游戏自己的字体注册表决定，中文走原版 CJK 字体，避免出现方块。
 *
 * 0.3.0：颜色一律 var(--原版变量, 兜底字面量)。这样玩家换 UI 主题、游戏改强调色时面板跟着走，
 * 而不是我自创一套「看起来像」的配色。（兜底值 = 从游戏 index.css 抄的同名变量值，dev-preview 台没有变量时用。）
 */
import { C } from "./icons";

export const PANEL_CSS = `
.bph-root{position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;z-index:900;}
.bph-panel{position:absolute;left:50%;top:50%;width:1280rem;height:768rem;margin-left:-640rem;margin-top:-384rem;
  border-radius:14rem;border:1rem solid var(--normalBorderColor,${C.line});box-shadow:0 18rem 60rem rgba(0,0,0,0.62);
  display:flex;flex-direction:column;pointer-events:auto;overflow:hidden;color:var(--normalTextColor,#F0FBFF);}
.bph-body{flex:1 1 auto;display:flex;flex-direction:row;min-height:0;}

/* ---------- 顶栏 ---------- */
.bph-head{height:64rem;flex:0 0 64rem;display:flex;flex-direction:row;align-items:center;padding:0 14rem 0 16rem;
  border-bottom:1rem solid ${C.line};}
.bph-logo{width:30rem;height:30rem;flex:0 0 30rem;margin-right:10rem;}
.bph-title{font-size:24rem;font-weight:bold;letter-spacing:1rem;white-space:nowrap;}
.bph-ver{font-size:11rem;color:var(--normalTextColorDimmer,${C.dim});margin:6rem 0 0 8rem;
  border:1rem solid ${C.line};border-radius:5rem;padding:1rem 6rem;white-space:nowrap;}
.bph-dev{font-size:11rem;color:#ffeccb;background:rgba(255,164,45,0.18);border:1rem solid ${C.action};
  border-radius:5rem;padding:1rem 6rem;margin-left:8rem;white-space:nowrap;}
.bph-spacer{flex:1 1 auto;}
.bph-hint{font-size:11.5rem;color:var(--normalTextColorDimmer,${C.dim});margin-right:12rem;white-space:nowrap;}
.bph-hbtn{width:34rem;height:34rem;flex:0 0 34rem;border-radius:8rem;display:flex;align-items:center;justify-content:center;
  cursor:pointer;border:1rem solid ${C.line};background:rgba(255,255,255,0.05);margin-left:8rem;}
.bph-hbtn:hover{border-color:var(--accentColorNormal,${C.accent});background:rgba(255,255,255,0.1);}
.bph-hbtn-acc{background:rgba(75,195,241,0.16);border-color:rgba(75,195,241,0.55);}
.bph-hbtn-acc:hover{background:rgba(75,195,241,0.3);border-color:var(--accentColorNormal,${C.accent});}

/* ---------- 左栏 ---------- */
.bph-side{width:190rem;flex:0 0 190rem;border-right:1rem solid ${C.line};padding:12rem 0 8rem 0;
  display:flex;flex-direction:column;min-height:0;}
.bph-side-h{font-size:11.5rem;color:var(--normalTextColorDimmer,${C.dim});letter-spacing:2rem;padding:0 16rem 8rem 16rem;}
.bph-cat{display:flex;flex-direction:row;align-items:center;padding:8rem 12rem;margin:1rem 8rem;border-radius:8rem;
  cursor:pointer;border:1rem solid transparent;}
.bph-cat:hover{background:${C.panelHi};}
.bph-cat-on{background:var(--selectedColor,#1e83aa);border-color:rgba(255,255,255,0.14);}
.bph-cat-dot{width:9rem;height:9rem;border-radius:5rem;margin-right:9rem;flex:0 0 9rem;}
.bph-cat-name{font-size:15rem;white-space:nowrap;}
.bph-cat-on .bph-cat-name{font-weight:bold;}
.bph-cat-n{font-size:11rem;color:var(--normalTextColorDimmer,${C.dim});margin-left:auto;padding-left:6rem;}
.bph-cat-on .bph-cat-n{color:rgba(255,255,255,0.75);}

/* ---------- 右区 ---------- */
.bph-main{flex:1 1 auto;display:flex;flex-direction:column;min-width:0;padding:0 14rem;overflow:hidden;}
.bph-menu{height:44rem;flex:0 0 44rem;display:flex;flex-direction:row;align-items:center;}
.bph-tile{font-size:11.5rem;color:var(--normalTextColorDimmer,${C.dim});white-space:nowrap;}
.bph-search{display:flex;flex-direction:row;align-items:center;margin-left:14rem;width:300rem;height:30rem;
  border-radius:8rem;border:1rem solid ${C.line};background:rgba(255,255,255,0.04);padding:0 8rem;}
.bph-search:focus-within{border-color:var(--accentColorNormal,${C.accent});}
.bph-search input{flex:1 1 auto;background:transparent;border:0;outline:0;color:var(--normalTextColor,#F0FBFF);
  font-size:14rem;margin-left:6rem;min-width:0;}
.bph-ph{color:var(--normalTextColorDimmer,${C.dim});}
.bph-drop{position:relative;margin-left:10rem;}
.bph-drop-btn{display:flex;flex-direction:row;align-items:center;height:30rem;padding:0 10rem;border-radius:8rem;
  border:1rem solid ${C.line};background:rgba(255,255,255,0.04);cursor:pointer;font-size:13rem;white-space:nowrap;}
.bph-drop-btn:hover{border-color:var(--accentColorNormal,${C.accent});}
.bph-drop-cur{color:var(--normalTextColorDimmer,${C.dim});margin-right:5rem;font-size:12rem;}
.bph-drop-menu{position:absolute;left:0;top:36rem;min-width:170rem;background:${C.panel};border:1rem solid ${C.line};
  border-radius:8rem;box-shadow:0 10rem 26rem rgba(0,0,0,0.55);z-index:600;padding:4rem 0;pointer-events:auto;}
.bph-drop-item{padding:7rem 12rem;font-size:13rem;cursor:pointer;white-space:nowrap;}
.bph-drop-item:hover{background:${C.panelHi};}
.bph-drop-item-on{color:var(--accentColorNormal,${C.accent});}

/* ---------- 卡片网格：5 列 × 3 行，封面比 0.2.0 大一档、间隙收紧 ---------- */
.bph-grid{flex:1 1 auto;display:flex;flex-direction:row;flex-wrap:wrap;align-content:flex-start;
  margin:2rem -5rem 0 -5rem;min-height:0;overflow:hidden;}
.bph-card{width:200rem;margin:0 5rem 8rem 5rem;border-radius:10rem;overflow:hidden;cursor:pointer;
  background:${C.panelHi};border:1rem solid ${C.line};}
.bph-card:hover{border-color:var(--accentColorNormal,${C.accent});background:#22303f;}
.bph-cover{position:relative;width:100%;height:132rem;overflow:hidden;background:#0d151f;}
.bph-cover img{width:100%;height:100%;object-fit:cover;display:block;}
.bph-catbar{position:absolute;left:0;top:0;width:4rem;height:100%;opacity:0.9;}
.bph-strip{position:absolute;left:0;right:0;bottom:0;height:26rem;display:flex;flex-direction:row;align-items:center;
  padding:0 6rem;background:linear-gradient(to top, rgba(8,12,18,0.92), rgba(8,12,18,0.32));backdrop-filter:blur(5px);}
.bph-area{font-size:10.5rem;color:#dbe6f0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:118rem;}
.bph-stat{display:flex;flex-direction:row;align-items:center;cursor:pointer;padding:2rem 3rem;border-radius:5rem;}
.bph-stat:first-of-type{margin-left:auto;}
.bph-stat+.bph-stat{margin-left:2rem;}
.bph-stat-n{font-size:11rem;color:#dbe6f0;margin-left:3rem;}
.bph-stat-on .bph-stat-n{color:var(--positiveColor,${C.voted});}
.bph-name{font-size:13.5rem;padding:6rem 8rem 0 8rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.bph-meta{display:flex;flex-direction:row;align-items:center;padding:3rem 8rem 7rem 8rem;}
.bph-mini-av{width:16rem;height:16rem;border-radius:8rem;display:flex;align-items:center;justify-content:center;flex:0 0 16rem;}
.bph-mini-txt{font-size:10rem;color:${C.bg};font-weight:bold;}
.bph-by{font-size:11.5rem;color:var(--normalTextColorDimmer,${C.dim});margin-left:5rem;white-space:nowrap;
  overflow:hidden;text-overflow:ellipsis;max-width:104rem;}
.bph-when{font-size:11rem;color:var(--normalTextColorDimmer,${C.dim});margin-left:auto;white-space:nowrap;opacity:0.85;}

/* ---------- 状态位（加载中 / 空 / 无结果 / 失败）---------- */
.bph-state{flex:1 1 auto;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:0;padding:0 40rem;}
.bph-state-t{font-size:17rem;margin-top:14rem;text-align:center;line-height:1.5;}
.bph-state-s{font-size:12.5rem;color:var(--normalTextColorDimmer,${C.dim});margin-top:8rem;text-align:center;
  line-height:1.5;max-width:620rem;}
.bph-state-tech{font-size:11rem;color:${C.dim};margin-top:10rem;text-align:center;max-width:700rem;
  border:1rem solid ${C.line};border-radius:6rem;padding:4rem 8rem;}
.bph-spin{width:44rem;height:44rem;animation:bph-rot 1.1s linear infinite;}
@keyframes bph-rot{from{transform:rotate(0deg);}to{transform:rotate(360deg);}}
.bph-btn{display:flex;flex-direction:row;align-items:center;height:32rem;padding:0 16rem;border-radius:8rem;
  border:1rem solid ${C.line};cursor:pointer;font-size:14rem;background:rgba(255,255,255,0.05);white-space:nowrap;}
.bph-btn:hover{border-color:var(--accentColorNormal,${C.accent});}
.bph-btn-warn{border-color:var(--warningColor,${C.action});color:var(--warningColor,${C.action});}
.bph-btn-row{display:flex;flex-direction:row;align-items:center;margin-top:18rem;}

/* ---------- 分页 ---------- */
.bph-pager{height:40rem;flex:0 0 40rem;display:flex;flex-direction:row;align-items:center;justify-content:center;
  border-top:1rem solid ${C.line};}
.bph-pg{min-width:26rem;height:26rem;padding:0 5rem;margin:0 3rem;border-radius:6rem;display:flex;align-items:center;
  justify-content:center;font-size:13rem;color:var(--normalTextColorDimmer,${C.dim});cursor:pointer;border:1rem solid transparent;}
.bph-pg:hover{color:var(--normalTextColor,#F0FBFF);border-color:${C.line};}
.bph-pg-on{color:#08222a;background:var(--accentColorNormal,${C.accent});border-color:var(--accentColorNormal,${C.accent});font-weight:bold;}
.bph-pg-off{opacity:0.3;cursor:default;}
.bph-count{font-size:12rem;color:var(--normalTextColorDimmer,${C.dim});margin-left:auto;white-space:nowrap;}

/* ---------- 浮层提示 ---------- */
.bph-toast{position:absolute;left:50%;bottom:24rem;margin-left:-260rem;width:520rem;padding:10rem 14rem;border-radius:9rem;
  font-size:13.5rem;text-align:center;pointer-events:none;border:1rem solid ${C.line};
  background:rgba(12,18,26,0.95);}
.bph-toast-err{border-color:var(--negativeColor,${C.danger});}
.bph-toast-warn{border-color:var(--warningColor,${C.action});}
.bph-toast-ok{border-color:var(--positiveColor,${C.voted});}

/* ---------- 入口按钮（GameTopLeft）：底与尺寸都交给原版 Button variant="floating" ---------- */
.bph-launch-glyph{width:100%;height:100%;display:block;}
/* 只有拿不到 cs2/ui 的 Button 时才会用到（降级路径）：按原版 theme 的三个数画同一个方块。 */
.bph-launch{width:40rem;height:40rem;border-radius:6rem;display:flex;align-items:center;justify-content:center;
  cursor:pointer;pointer-events:auto;margin:2rem;padding:6rem;background:var(--accentColorNormal,${C.accent});
  box-shadow:0 2rem 6rem rgba(0,0,0,0.45);}
.bph-launch:hover{background:var(--accentColorNormal-hover,#7ad3f5);}
.bph-hidden{display:none;}
`;

let injected = false;

/** 幂等注入：面板可能同时挂在 Game / GameTopLeft 两个槽位，样式只允许出现一份。 */
export function ensureStyles(): void {
  if (injected) return;
  try {
    if (typeof document === "undefined" || !document.head) return;
    if (document.getElementById("bph-style")) { injected = true; return; }
    const el = document.createElement("style");
    el.id = "bph-style";
    el.type = "text/css";
    el.appendChild(document.createTextNode(PANEL_CSS));
    document.head.appendChild(el);
    injected = true;
  } catch (e) {
    console.warn("[BlueprintHub] styles inject failed", e);
  }
}
