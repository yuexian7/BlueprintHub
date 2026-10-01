/**
 * 面板样式：整份 CSS 内联成一个字符串，由 ensureStyles() 注进 <head>。
 * 为什么不用 .css 文件：游戏 loader 会按模块旁的同名 .css 去拉（exports.hasCSS 决定），
 * 少一个文件 = 少一个 404 面（BusLineAutoStops 的结论）。
 *
 * 单位：全部 rem。Cohtml 里 1rem ≈ 1080p 下的 1px（与已上架的 BusLineAutoStops 面板同一口径）。
 * 故意不设 font-family：让游戏自己的字体注册表决定，中文走原版 CJK 字体，避免出现方块。
 *
 * ============ Cohtml 能吃什么（0.3.1 用游戏自己 50 万行 index.css 的统计 + 实机 UI.log 校准）============
 * 这三条是 0.3.0「面板很丑 / 选中态看不见 / 入口变成白块」的真正原因，写在前面，改样式前先读：
 *
 * 1. **var() 不能出现在 shorthand 里。** 官方 CSS 统计：`background:<var>` 只有 1 处、`border:<var>` 0 处，
 *    而那 1 处每次开机都会在 UI.log 里打一条
 *    「Custom CSS expressions are not supported in shorthand declaration, please use the long versions…」，
 *    并且整条声明被丢弃。0.3.0 我写的 `border:1rem solid var(--x,#兜底)`、`background:var(--x,#兜底)`、
 *    `border-color:var(--x,#兜底)` 全中 → 边框、选中底色、激活页码**整条没生效**，所以看起来又平又丑。
 *    字面量放 shorthand 是合法的（官方 `border:2.4rem solid #4ac0f0` 有 15 处、`border-color:rgba(...)` 176 处）。
 * 2. **var() 的逗号兜底写法 Cohtml 解析不了。** 会把「normalTextColor,#F0FBFF」整串当成变量名去查，
 *    于是每次刷新刷一片「Unable to resolve custom variable: normalTextColor,#F0FBFF」。
 *    官方 CSS 里带逗号兜底的 var() 是 **0 处** —— 不是风格问题，是不支持。
 * 3. **不认的属性 / 伪类**（实机日志逐条抓到的）：object-fit、outline、word-wrap、text-rendering、
 *    :focus-within、:first-of-type。官方 CSS 里 mask 全部是无前缀 `mask-image`（26 处，-webkit- 0 处），
 *    而且 **data URI 出现 0 次** —— mask 只吃能当资源加载的 URL，所以入口图案改成随包的 .svg 文件。
 *
 * 所以现在的写法约定：
 *  · 需要跟着玩家 UI 主题走的颜色 → 只用 var(--token)，且只放在**长写法**属性上
 *    （color / background-color / border-*-color / width / height / border-*-radius / font-size）。
 *    用到的 token 全部在官方 index.css 里核对过名字（见 tools/check-css.mjs 里的白名单）；
 *    --accentColorNormal 在官方主题表里有蓝 / 深蓝 / 橙三套值，所以走 var() 才真的等于「和原版一个规范」。
 *  · 面板自己的深海军蓝底、发丝分隔线、dim 文本 → 官方没有对应 token，直接用字面量（这就是最稳的）。
 *  · 禁止：var() 进 shorthand、var() 带逗号兜底、data URI、上面那六个不认的属性/伪类。
 *    tools/check-css.mjs 会把这些钉成门禁，跑不过就不许提交。
 */

export const PANEL_CSS = `
.bph-root{position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;z-index:900;}
.bph-panel{position:absolute;left:50%;top:50%;width:1280rem;height:768rem;margin-left:-640rem;margin-top:-384rem;
  border-radius:14rem;border:1rem solid rgba(150,178,200,0.20);box-shadow:0 18rem 60rem rgba(0,0,0,0.62);
  display:flex;flex-direction:column;pointer-events:auto;overflow:hidden;color:#F0FBFF;}
.bph-body{flex:1 1 auto;display:flex;flex-direction:row;min-height:0;}

/* ---------- 顶栏：48rem 高、底部一条发丝线，标题跟面板同名 ---------- */
.bph-head{height:60rem;flex:0 0 60rem;display:flex;flex-direction:row;align-items:center;padding:0 12rem 0 16rem;
  border-bottom:1rem solid rgba(150,178,200,0.20);background-color:rgba(255,255,255,0.03);}
.bph-logo{width:28rem;height:28rem;flex:0 0 28rem;margin-right:10rem;}
.bph-title{font-size:22rem;font-weight:bold;letter-spacing:1rem;white-space:nowrap;color:#F0FBFF;}
.bph-ver{font-size:11rem;color:#9fb0c0;margin:6rem 0 0 8rem;
  border:1rem solid rgba(150,178,200,0.28);border-radius:5rem;padding:1rem 6rem;white-space:nowrap;}
.bph-dev{font-size:11rem;color:#ffeccb;background-color:rgba(255,164,45,0.18);
  border:1rem solid rgba(255,164,45,0.65);border-radius:5rem;padding:1rem 6rem;margin-left:8rem;white-space:nowrap;}
.bph-spacer{flex:1 1 auto;}
.bph-hint{font-size:11.5rem;color:#9fb0c0;margin-right:12rem;white-space:nowrap;}
/* 右上角两颗图标按钮（需求 5）：34rem 方块 + 图标，不是一句话 */
.bph-hbtn{width:34rem;height:34rem;flex:0 0 34rem;border-radius:7rem;display:flex;align-items:center;justify-content:center;
  cursor:pointer;border:1rem solid rgba(150,178,200,0.28);background-color:rgba(255,255,255,0.05);margin-left:8rem;}
.bph-hbtn:hover{border-color:rgba(122,211,245,0.75);background-color:rgba(255,255,255,0.10);}
.bph-hbtn-acc{background-color:rgba(75,195,241,0.16);border-color:rgba(75,195,241,0.55);}
.bph-hbtn-acc:hover{background-color:rgba(75,195,241,0.30);border-color:rgba(122,211,245,0.90);}

/* ---------- 左栏 ---------- */
.bph-side{width:196rem;flex:0 0 196rem;border-right:1rem solid rgba(150,178,200,0.20);padding:14rem 0 10rem 0;
  display:flex;flex-direction:column;min-height:0;}
.bph-side-h{font-size:11rem;color:#7f93a6;letter-spacing:2rem;padding:0 16rem 8rem 16rem;}
.bph-cat{display:flex;flex-direction:row;align-items:center;padding:9rem 12rem;margin:1rem 8rem;border-radius:8rem;
  cursor:pointer;border:1rem solid rgba(255,255,255,0.00);}
.bph-cat:hover{background-color:rgba(255,255,255,0.06);}
/* 选中底与描边跟着原版主题：这两条 0.3.0 写在 shorthand 里，所以当时整条被丢掉 */
.bph-cat-on{background-color:var(--selectedColor);border-top-color:rgba(255,255,255,0.16);
  border-left-color:rgba(255,255,255,0.16);border-right-color:rgba(255,255,255,0.16);border-bottom-color:rgba(255,255,255,0.16);}
.bph-cat-dot{width:10rem;height:10rem;border-radius:6rem;margin-right:9rem;flex:0 0 10rem;}
.bph-cat-name{font-size:15rem;white-space:nowrap;}
.bph-cat-on .bph-cat-name{font-weight:bold;}
.bph-cat-n{font-size:11rem;color:#7f93a6;margin-left:auto;padding-left:6rem;}
.bph-cat-on .bph-cat-n{color:rgba(255,255,255,0.78);}

/* ---------- 右区 ---------- */
.bph-main{flex:1 1 auto;display:flex;flex-direction:column;min-width:0;padding:0 14rem;overflow:hidden;}
.bph-menu{height:46rem;flex:0 0 46rem;display:flex;flex-direction:row;align-items:center;}
.bph-tile{font-size:11.5rem;color:#9fb0c0;white-space:nowrap;}
.bph-search{display:flex;flex-direction:row;align-items:center;margin-left:14rem;width:300rem;height:32rem;
  border-radius:8rem;border:1rem solid rgba(150,178,200,0.28);background-color:rgba(255,255,255,0.04);padding:0 8rem;}
/* :focus-within Cohtml 不认（实机日志点名）→ 改成 React 的 focus/blur 状态类 */
.bph-search-on{border-color:rgba(122,211,245,0.85);background-color:rgba(255,255,255,0.07);}
.bph-search input{flex:1 1 auto;background-color:rgba(0,0,0,0);border-width:0;color:#F0FBFF;
  font-size:14rem;margin-left:6rem;min-width:0;}
.bph-drop{position:relative;margin-left:10rem;z-index:520;}
.bph-drop-btn{display:flex;flex-direction:row;align-items:center;height:32rem;padding:0 10rem;border-radius:8rem;
  border:1rem solid rgba(150,178,200,0.28);background-color:rgba(255,255,255,0.04);cursor:pointer;font-size:13rem;white-space:nowrap;}
.bph-drop-btn:hover{border-color:rgba(122,211,245,0.85);}
.bph-drop-cur{color:#9fb0c0;margin-right:5rem;font-size:12rem;}
.bph-drop-menu{position:absolute;left:0;top:38rem;min-width:180rem;background-color:#17222f;
  border:1rem solid rgba(150,178,200,0.28);border-radius:8rem;box-shadow:0 10rem 26rem rgba(0,0,0,0.55);
  z-index:600;padding:4rem 0;pointer-events:auto;}
.bph-drop-item{padding:8rem 12rem;font-size:13rem;cursor:pointer;white-space:nowrap;}
.bph-drop-item:hover{background-color:rgba(255,255,255,0.06);}
.bph-drop-item-on{color:var(--accentColorNormal);}

/* ---------- 卡片网格：5 列 × 3 行，封面比 0.2.0 大一档、间隙收紧 ---------- */
.bph-grid{flex:1 1 auto;display:flex;flex-direction:row;flex-wrap:wrap;align-content:flex-start;
  margin:4rem -5rem 0 -5rem;min-height:0;overflow:hidden;}
.bph-card{width:200rem;margin:0 5rem 8rem 5rem;border-radius:10rem;overflow:hidden;cursor:pointer;
  background-color:#1d2b3a;border:1rem solid rgba(150,178,200,0.16);transition:border-color 120ms,background-color 120ms;}
.bph-card:hover{border-color:var(--accentColorNormal);background-color:#22303f;}
.bph-cover{position:relative;width:100%;height:140rem;overflow:hidden;background-color:#0d151f;}
/* object-fit 不认（官方 CSS 也 0 使用）→ 封面按 100%×100% 铺，出图比例由 M3 导出侧固定 3:2 */
.bph-cover img{width:100%;height:100%;display:block;}
.bph-cover-svg{width:100%;height:100%;display:block;}
.bph-catbar{position:absolute;left:0;top:0;width:4rem;height:100%;}
.bph-strip{position:absolute;left:0;right:0;bottom:0;height:28rem;display:flex;flex-direction:row;align-items:center;
  padding:0 6rem;background-color:rgba(10,16,24,0.72);}
.bph-area{font-size:10.5rem;color:#dbe6f0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:112rem;}
.bph-stat{display:flex;flex-direction:row;align-items:center;cursor:pointer;padding:2rem 3rem;border-radius:5rem;}
/* :first-of-type 不认 → 第一颗统计按钮由 JS 带这个类，作用还是「把统计推到右侧」 */
.bph-stat-push{margin-left:auto;}
.bph-stat+.bph-stat{margin-left:2rem;}
.bph-stat-n{font-size:11rem;color:#dbe6f0;margin-left:3rem;}
.bph-stat-on .bph-stat-n{color:var(--positiveColor);}
.bph-name{font-size:13.5rem;padding:7rem 8rem 0 8rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:#F0FBFF;}
.bph-meta{display:flex;flex-direction:row;align-items:center;padding:3rem 8rem 8rem 8rem;}
.bph-mini-av{width:16rem;height:16rem;border-radius:8rem;display:flex;align-items:center;justify-content:center;flex:0 0 16rem;}
.bph-mini-txt{font-size:10rem;color:#121a26;font-weight:bold;}
.bph-by{font-size:11.5rem;color:#9fb0c0;margin-left:5rem;white-space:nowrap;
  overflow:hidden;text-overflow:ellipsis;max-width:104rem;}
.bph-when{font-size:11rem;color:#7f93a6;margin-left:auto;white-space:nowrap;}

/* ---------- 状态位（加载中 / 空 / 无结果 / 失败）---------- */
.bph-state{flex:1 1 auto;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:0;padding:0 40rem;}
.bph-state-t{font-size:17rem;margin-top:14rem;text-align:center;line-height:1.5;color:#F0FBFF;}
.bph-state-s{font-size:12.5rem;color:#9fb0c0;margin-top:8rem;text-align:center;line-height:1.5;max-width:620rem;}
.bph-state-tech{font-size:11rem;color:#7f93a6;margin-top:10rem;text-align:center;max-width:700rem;
  border:1rem solid rgba(150,178,200,0.22);border-radius:6rem;padding:4rem 8rem;}
.bph-spin{width:44rem;height:44rem;animation:bph-rot 1.1s linear infinite;}
@keyframes bph-rot{from{transform:rotate(0deg);}to{transform:rotate(360deg);}}
.bph-btn{display:flex;flex-direction:row;align-items:center;height:32rem;padding:0 16rem;border-radius:8rem;
  border:1rem solid rgba(150,178,200,0.28);cursor:pointer;font-size:14rem;background-color:rgba(255,255,255,0.05);white-space:nowrap;}
.bph-btn:hover{border-color:var(--accentColorNormal);background-color:rgba(255,255,255,0.09);}
.bph-btn-warn{color:var(--warningColor);}
.bph-btn-row{display:flex;flex-direction:row;align-items:center;margin-top:18rem;}

/* ---------- 分页 ---------- */
.bph-pager{height:42rem;flex:0 0 42rem;display:flex;flex-direction:row;align-items:center;justify-content:center;
  border-top:1rem solid rgba(150,178,200,0.20);}
.bph-pg{min-width:26rem;height:26rem;padding:0 5rem;margin:0 3rem;border-radius:6rem;display:flex;align-items:center;
  justify-content:center;font-size:13rem;color:#9fb0c0;cursor:pointer;border:1rem solid rgba(255,255,255,0.00);}
.bph-pg:hover{color:#F0FBFF;border-color:rgba(150,178,200,0.34);}
/* 0.3.0 这两条写在 background/border-color 里 → 整条丢弃，激活页码当时是「看不见的高亮」 */
.bph-pg-on{color:#08222a;background-color:var(--accentColorNormal);font-weight:bold;}
.bph-pg-off{opacity:0.3;cursor:default;}
.bph-count{font-size:12rem;color:#9fb0c0;margin-left:auto;white-space:nowrap;}

/* ---------- 浮层提示：三种状态色只走长写法 --- */
.bph-toast{position:absolute;left:50%;bottom:24rem;margin-left:-260rem;width:520rem;padding:10rem 14rem;border-radius:9rem;
  font-size:13.5rem;text-align:center;pointer-events:none;border:1rem solid rgba(150,178,200,0.28);
  background-color:rgba(12,18,26,0.95);color:#F0FBFF;}
.bph-toast-err{border-left-color:var(--negativeColor);border-left-width:4rem;}
.bph-toast-warn{border-left-color:var(--warningColor);border-left-width:4rem;}
.bph-toast-ok{border-left-color:var(--positiveColor);border-left-width:4rem;}

/* ---------- 顶栏：账号按钮 + 那一句淡蓝提示（需求 3/4）---------- */
/* 提示语用字面量淡蓝而不是 var：它必须明显是「一句提醒」，跟主题色走会在橙色主题下变成橙字 */
.bph-share{font-size:12rem;color:#7ec8f2;margin-right:14rem;white-space:nowrap;}
.bph-avatar{width:34rem;height:34rem;border-radius:7rem;cursor:pointer;display:block;
  border:1rem solid rgba(150,178,200,0.38);}
.bph-avatar-box{width:34rem;height:34rem;flex:0 0 34rem;border-radius:7rem;cursor:pointer;display:flex;
  align-items:center;justify-content:center;border:1rem solid rgba(150,178,200,0.38);background-color:rgba(255,255,255,0.06);}
.bph-avatar-dot{width:14rem;height:14rem;border-radius:7rem;background-color:var(--accentColorNormal);}
.bph-login-box{height:34rem;min-width:60rem;padding:0 12rem;border-radius:8rem;display:flex;align-items:center;
  justify-content:center;cursor:pointer;font-size:13rem;font-weight:bold;color:#08222a;background-color:var(--accentColorNormal);}
.bph-login-box:hover{background-color:var(--accentColorNormal-hover);}

/* ---------- 左栏底部：选中那一类的定义（需求 9）---------- */
.bph-side-desc{margin:12rem 8rem 0 8rem;padding:9rem 10rem;border-radius:8rem;font-size:11.5rem;line-height:1.45;
  color:#9fb0c0;background-color:rgba(255,255,255,0.04);border:1rem solid rgba(150,178,200,0.16);}

/* ---------- 下拉遮罩（需求 5）：整屏一层透明 div，点哪儿都收；菜单自己的 .bph-drop 比它高一级 ---------- */
.bph-drop-scrim{position:fixed;left:0;top:0;width:100%;height:100%;z-index:500;pointer-events:auto;}

/* ---------- 市辖区面板底部那颗上传（需求 2，条目挂进游戏自己的 footer，所以尺寸按它那一排来）---------- */
.bph-dpanel{display:flex;flex-direction:column;padding:4rem 10rem 8rem 10rem;}
.bph-drow{display:flex;flex-direction:row;align-items:center;}
.bph-btn-main{color:#08222a;font-weight:bold;background-color:var(--accentColorNormal);border-color:rgba(255,255,255,0.22);}
.bph-btn-main:hover{background-color:var(--accentColorNormal-hover);border-color:rgba(255,255,255,0.34);}
.bph-btn-main[disabled]{opacity:0.45;cursor:default;}
.bph-btn-off{color:#F0FBFF;background-color:rgba(255,255,255,0.06);}
.bph-btn+.bph-btn{margin-left:8rem;}
.bph-btn span{margin-left:5rem;}
.bph-darea{font-size:11.5rem;color:#7ec8f2;margin-left:10rem;white-space:nowrap;}
.bph-dneed{font-size:11rem;color:var(--warningColor);margin-left:10rem;white-space:nowrap;}
.bph-form{display:flex;flex-direction:column;}
.bph-form-row{display:flex;flex-direction:row;align-items:flex-start;margin-top:6rem;}
/* 标签列宽：预览台实拍抓出来的 —— 74rem 装不下「简介（可留空）」七个字，中文标签会折行。
   88rem 让 zh/zh-TW 单行放平；英文 "Description (optional)" 仍会折两行，align-items 是 flex-start，
   折行只往下长，不会把输入框挤歪。 */
.bph-form-label{font-size:11.5rem;color:#9fb0c0;width:88rem;flex:0 0 88rem;margin-right:8rem;}
.bph-input{flex:1 1 auto;height:28rem;padding:0 8rem;border-radius:6rem;font-size:12.5rem;color:#F0FBFF;min-width:0;
  border:1rem solid rgba(150,178,200,0.28);background-color:rgba(255,255,255,0.05);}
.bph-textarea{height:46rem;padding:5rem 8rem;}
.bph-chips{display:flex;flex-direction:row;flex-wrap:wrap;flex:1 1 auto;}
.bph-chip{font-size:11.5rem;padding:3rem 8rem;margin:2rem;border-radius:6rem;cursor:pointer;color:#c9d7e4;
  border:1rem solid rgba(150,178,200,0.24);background-color:rgba(255,255,255,0.04);}
.bph-chip-on{color:#F0FBFF;font-weight:bold;background-color:var(--selectedColor);border-color:rgba(255,255,255,0.20);}
.bph-form-foot{display:flex;flex-direction:row;align-items:center;margin-top:9rem;}
.bph-done{display:flex;flex-direction:column;}
.bph-done-title{font-size:12rem;color:#cfe6f5;line-height:1.4;}
.bph-warn-line{display:flex;flex-direction:row;align-items:center;font-size:11.5rem;color:var(--warningColor);margin-top:5rem;}
.bph-warn-line svg{margin-right:5rem;}
.bph-hint-line{font-size:11rem;color:#9fb0c0;margin-top:5rem;line-height:1.4;}
.bph-path-row{display:flex;flex-direction:row;align-items:center;margin-top:7rem;}
.bph-path{font-size:10.5rem;color:#cfe6f5;}
.bph-fail{display:flex;flex-direction:row;align-items:center;font-size:11.5rem;color:var(--negativeColor);line-height:1.4;}
.bph-fail svg{margin-right:5rem;}
.bph-busy{display:flex;flex-direction:row;align-items:center;font-size:11.5rem;color:#9fb0c0;}
.bph-busy svg{margin-right:6rem;}

/* ---------- 主面板里的兜底：游戏条目没挂上时才出现（不把玩家堵死）---------- */
.bph-fallback{display:flex;flex-direction:column;margin:10rem 14rem 0 14rem;padding:10rem 12rem;border-radius:9rem;
  border:1rem solid rgba(150,178,200,0.20);background-color:rgba(126,200,242,0.08);}
.bph-fallback-title{font-size:12.5rem;color:#7ec8f2;line-height:1.4;}
.bph-fallback-body{display:flex;flex-direction:row;align-items:center;margin-top:8rem;}
.bph-fail-inline{font-size:11.5rem;color:var(--negativeColor);margin-left:12rem;}
.bph-ok-inline{font-size:11rem;color:#9fb0c0;margin-left:12rem;}

/* ---------- 入口方块（GameTopLeft）---------- */
/* 外层只负责「在自动排列的那一行里占住自己、不伸不缩」：
   容器 .info-menu-layout 是 absolute top/left:10rem 的 display:flex 行，官方与所有模组的方块都在这一行，
   间距由容器的 >*{margin:0 6rem 6rem 0} 给 —— 位置本来就是自动的，模组不需要算 left。 */
.bph-toggle{position:relative;display:flex;flex-direction:row;align-items:stretch;flex:0 0 auto;pointer-events:auto;}
/* 只有拿不到 cs2/ui 的 Button 时才用：按官方 .button_ke4 的四个数画同一个方块（尺寸走 var 长写法）。 */
.bph-launch{width:40rem;height:40rem;padding:6rem;display:flex;align-items:center;justify-content:center;cursor:pointer;
  border-top-left-radius:var(--floatingToggleBorderRadius);border-top-right-radius:var(--floatingToggleBorderRadius);
  border-bottom-left-radius:var(--floatingToggleBorderRadius);border-bottom-right-radius:var(--floatingToggleBorderRadius);
  background-color:var(--accentColorNormal);}
.bph-launch:hover{background-color:var(--accentColorNormal-hover);}
.bph-launch:active{background-color:var(--accentColorNormal-pressed);}
/* 图案 = 随包的 .svg 文件（官方 .icon_be5 同口径：100%×100%，白色描边画在文件里，不用 mask 也不用 data URI） */
.bph-launch-img{width:100%;height:100%;display:block;}
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
