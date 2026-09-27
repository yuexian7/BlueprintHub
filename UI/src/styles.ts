/**
 * 面板样式：整份 CSS 内联成一个字符串，由 ensureStyles() 注进 <head>。
 * 为什么不用 .css 文件：游戏 loader 会按模块旁的同名 .css 去拉（exports.hasCSS 决定），
 * 少一个文件 = 少一个 404 面（BusLineAutoStops 的结论）。
 *
 * 单位：全部 rem。Cohtml 里 1rem ≈ 1080p 下的 1px（与已上架的 BusLineAutoStops 面板同一口径）。
 * 故意不设 font-family：让游戏自己的字体注册表决定，中文走原版 CJK 字体，避免出现方块。
 */
import { C } from "./icons";

export const PANEL_CSS = `
.bph-root{position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;z-index:900;}
.bph-panel{position:absolute;left:50%;top:50%;width:1240rem;height:768rem;margin-left:-620rem;margin-top:-384rem;
  border-radius:14rem;border:1rem solid ${C.line};box-shadow:0 18rem 60rem rgba(0,0,0,0.62);
  display:flex;flex-direction:column;pointer-events:auto;overflow:hidden;}
.bph-body{flex:1 1 auto;display:flex;flex-direction:row;min-height:0;}

/* ---------- 顶栏（需求 1）---------- */
.bph-head{height:86rem;flex:0 0 86rem;display:flex;flex-direction:column;padding:10rem 18rem 0 18rem;
  border-bottom:1rem solid ${C.line};}
.bph-head-row{display:flex;flex-direction:row;align-items:center;}
.bph-brand{display:flex;flex-direction:row;align-items:center;}
.bph-logo{width:34rem;height:34rem;margin-right:9rem;flex:0 0 34rem;}
.bph-brand-txt{display:flex;flex-direction:column;justify-content:center;margin-right:18rem;}
.bph-brand-name{font-size:13rem;color:${C.dim};letter-spacing:0.3rem;}
.bph-brand-ver{font-size:11rem;color:${C.dim};opacity:0.75;}
.bph-title{font-size:26rem;font-weight:bold;color:${C.text};letter-spacing:1.2rem;}
.bph-spacer{flex:1 1 auto;}
.bph-hint{font-size:12rem;color:${C.dim};margin-right:14rem;max-width:330rem;text-align:right;line-height:1.35;}
.bph-avatar{width:36rem;height:36rem;border-radius:18rem;border:1rem solid ${C.line};
  display:flex;align-items:center;justify-content:center;cursor:pointer;margin-right:10rem;flex:0 0 36rem;}
.bph-avatar-txt{font-size:15rem;font-weight:bold;color:${C.bg};}
.bph-close{width:34rem;height:34rem;border-radius:8rem;display:flex;align-items:center;justify-content:center;
  cursor:pointer;border:1rem solid ${C.line};}
.bph-close:hover{background:rgba(229,72,77,0.24);border-color:${C.danger};}
.bph-sub{font-size:13rem;color:${C.accent};padding:3rem 0 6rem 44rem;letter-spacing:0.4rem;}

/* ---------- 左栏（需求 2）---------- */
.bph-side{width:198rem;flex:0 0 198rem;border-right:1rem solid ${C.line};padding:12rem 0 10rem 0;
  display:flex;flex-direction:column;min-height:0;}
.bph-side-h{font-size:12rem;color:${C.dim};letter-spacing:2rem;padding:0 16rem 10rem 16rem;}
.bph-cat{display:flex;flex-direction:row;align-items:center;padding:9rem 14rem;margin:1rem 8rem;border-radius:8rem;
  cursor:pointer;border:1rem solid transparent;}
.bph-cat:hover{background:${C.panelHi};}
.bph-cat-on{background:${C.panelHi};border-color:${C.line};}
.bph-cat-dot{width:9rem;height:9rem;border-radius:5rem;margin-right:9rem;flex:0 0 9rem;}
.bph-cat-name{font-size:15rem;color:${C.text};flex:0 0 auto;}
.bph-cat-n{font-size:11rem;color:${C.dim};margin-left:auto;padding-left:6rem;}

/* ---------- 右区 ---------- */
.bph-main{flex:1 1 auto;display:flex;flex-direction:column;min-width:0;padding:0 16rem;overflow:hidden;}
.bph-menu{height:52rem;flex:0 0 52rem;display:flex;flex-direction:row;align-items:center;}
.bph-tile{font-size:12rem;color:${C.dim};white-space:nowrap;}
.bph-goto{font-size:12rem;color:${C.dim};margin-left:8rem;white-space:nowrap;}
.bph-search{display:flex;flex-direction:row;align-items:center;margin-left:14rem;width:286rem;height:32rem;
  border-radius:8rem;border:1rem solid ${C.line};background:rgba(255,255,255,0.04);padding:0 8rem;}
.bph-search:focus-within{border-color:${C.accent};}
.bph-search input{flex:1 1 auto;background:transparent;border:0;outline:0;color:${C.text};font-size:14rem;
  margin-left:6rem;min-width:0;}
.bph-ph{color:${C.dim};}
.bph-drop{position:relative;margin-left:10rem;}
.bph-drop-btn{display:flex;flex-direction:row;align-items:center;height:32rem;padding:0 10rem;border-radius:8rem;
  border:1rem solid ${C.line};background:rgba(255,255,255,0.04);cursor:pointer;font-size:13rem;color:${C.text};white-space:nowrap;}
.bph-drop-btn:hover{border-color:${C.accent};}
.bph-drop-cur{color:${C.dim};margin-right:5rem;font-size:12rem;}
.bph-drop-menu{position:absolute;left:0;top:38rem;min-width:158rem;background:${C.panel};border:1rem solid ${C.line};
  border-radius:8rem;box-shadow:0 10rem 26rem rgba(0,0,0,0.55);z-index:600;padding:4rem 0;pointer-events:auto;}
.bph-drop-item{padding:7rem 12rem;font-size:13rem;color:${C.text};cursor:pointer;white-space:nowrap;}
.bph-drop-item:hover{background:${C.panelHi};}
.bph-drop-item-on{color:${C.accent};}

/* ---------- 卡片网格：5 列 × 3 行（需求 3）---------- */
.bph-grid{flex:1 1 auto;display:flex;flex-direction:row;flex-wrap:wrap;align-content:flex-start;
  margin:4rem -6rem 0 -6rem;min-height:0;overflow:hidden;}
.bph-card{width:180rem;margin:0 6rem 10rem 6rem;border-radius:10rem;overflow:hidden;cursor:pointer;
  background:${C.panelHi};border:1rem solid ${C.line};}
.bph-card:hover{border-color:${C.accent};}
.bph-cover{position:relative;width:100%;height:104rem;overflow:hidden;background:#0d151f;}
.bph-cover img{width:100%;height:100%;object-fit:cover;display:block;}
.bph-catbar{position:absolute;left:0;top:0;width:4rem;height:100%;opacity:0.9;}
.bph-strip{position:absolute;left:0;right:0;bottom:0;height:26rem;display:flex;flex-direction:row;align-items:center;
  padding:0 6rem;background:linear-gradient(to top, rgba(8,12,18,0.92), rgba(8,12,18,0.32));
  backdrop-filter:blur(5px);}
.bph-area{font-size:10.5rem;color:#dbe6f0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:104rem;}
.bph-stat{display:flex;flex-direction:row;align-items:center;cursor:pointer;padding:2rem 3rem;border-radius:5rem;}
.bph-stat:first-of-type{margin-left:auto;}
.bph-stat+.bph-stat{margin-left:2rem;}
.bph-stat-n{font-size:11rem;color:#dbe6f0;margin-left:3rem;}
.bph-stat-on .bph-stat-n{color:${C.voted};}
.bph-name{font-size:13.5rem;color:${C.text};padding:6rem 8rem 0 8rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.bph-meta{display:flex;flex-direction:row;align-items:center;padding:4rem 8rem 8rem 8rem;}
.bph-mini-av{width:16rem;height:16rem;border-radius:8rem;display:flex;align-items:center;justify-content:center;flex:0 0 16rem;}
.bph-mini-txt{font-size:10rem;color:${C.bg};font-weight:bold;}
.bph-by{font-size:11.5rem;color:${C.dim};margin-left:5rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:96rem;}
.bph-when{font-size:11rem;color:${C.dim};margin-left:auto;white-space:nowrap;opacity:0.85;}

/* ---------- 状态位（加载中 / 空 / 失败）---------- */
.bph-state{flex:1 1 auto;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:0;padding:0 40rem;}
.bph-state-t{font-size:16rem;color:${C.text};margin-top:14rem;text-align:center;line-height:1.5;}
.bph-state-s{font-size:12.5rem;color:${C.dim};margin-top:8rem;text-align:center;line-height:1.5;max-width:620rem;}
.bph-spin{width:44rem;height:44rem;animation:bph-rot 1.1s linear infinite;}
@keyframes bph-rot{from{transform:rotate(0deg);}to{transform:rotate(360deg);}}
.bph-btn{display:flex;flex-direction:row;align-items:center;height:34rem;padding:0 16rem;border-radius:8rem;
  border:1rem solid ${C.line};cursor:pointer;font-size:14rem;color:${C.text};background:rgba(255,255,255,0.05);white-space:nowrap;}
.bph-btn:hover{border-color:${C.accent};}
.bph-btn-accent{background:${C.accent};border-color:${C.accent};color:#08222a;font-weight:bold;}
.bph-btn-accent:hover{filter:brightness(1.1);}
.bph-btn-warn{border-color:${C.action};color:${C.action};}
.bph-btn-danger{border-color:${C.danger};color:${C.danger};}
.bph-btn-row{display:flex;flex-direction:row;align-items:center;margin-top:18rem;}

/* ---------- 分页 ---------- */
.bph-pager{height:46rem;flex:0 0 46rem;display:flex;flex-direction:row;align-items:center;justify-content:center;
  border-top:1rem solid ${C.line};}
.bph-pg{min-width:28rem;height:28rem;padding:0 6rem;margin:0 3rem;border-radius:6rem;display:flex;align-items:center;
  justify-content:center;font-size:13rem;color:${C.dim};cursor:pointer;border:1rem solid transparent;}
.bph-pg:hover{color:${C.text};border-color:${C.line};}
.bph-pg-on{color:#08222a;background:${C.accent};border-color:${C.accent};font-weight:bold;}
.bph-pg-off{opacity:0.3;cursor:default;}
.bph-count{font-size:12rem;color:${C.dim};margin-left:auto;}

/* ---------- 浮层提示 ---------- */
.bph-toast{position:absolute;left:50%;bottom:26rem;margin-left:-260rem;width:520rem;padding:11rem 14rem;border-radius:9rem;
  font-size:13.5rem;text-align:center;pointer-events:none;border:1rem solid ${C.line};color:${C.text};
  background:rgba(12,18,26,0.95);}
.bph-toast-err{border-color:${C.danger};color:#ffd9da;}
.bph-toast-warn{border-color:${C.action};color:#ffeccb;}
.bph-toast-ok{border-color:${C.voted};color:#dff5e8;}

/* ---------- 入口按钮（GameTopLeft）---------- */
.bph-launch{display:flex;flex-direction:row;align-items:center;height:42rem;padding:0 12rem 0 8rem;border-radius:9rem;
  border:1rem solid ${C.line};cursor:pointer;pointer-events:auto;margin:2rem;
  background:rgba(23,34,47,0.9);}
.bph-launch:hover{border-color:${C.accent};}
.bph-launch-txt{font-size:13rem;color:${C.text};margin-left:7rem;letter-spacing:0.5rem;}
.bph-launch-key{font-size:11rem;color:${C.dim};margin-left:8rem;}
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
