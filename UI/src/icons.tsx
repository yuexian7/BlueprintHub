import React from "react";

/**
 * 图标全部内联成 SVG 组件，不走 coui:// 外部文件：
 * 省掉一个 host location，也省掉「图片没到 → 面板上出现空洞」这类只有实机才看得见的故障。
 *
 * 配色（0.3.0 换成原版 UI 自己的色板，取游戏 CSS 变量 + 同值兜底）：
 *   主强调  --accentColorNormal #4bc3f1（原版蓝）  文本 --normalTextColor #F0FBFF
 *   正向    --positiveColor #8bdb46   警告 --warningColor #ffa42d   危险 --negativeColor #e95f4a
 *   面板底  #121a26 / 卡片 #17222f / 分隔 rgba(255,255,255,.08)
 * 这里必须是**字面量**：SVG 的 presentation 属性不吃 var()，只有 styles.ts 里的 CSS 才用 var() 走原版主题。
 */
export const C = {
  bg: "#121a26",
  panel: "#17222f",
  panelHi: "#1d2b3a",
  line: "rgba(255,255,255,0.08)",
  accent: "#4bc3f1",
  action: "#ffa42d",
  like: "#ff6b81",
  voted: "#8bdb46",
  download: "#4bc3f1",
  danger: "#e95f4a",
  text: "#F0FBFF",
  dim: "#9fb0c0",
};

/** 7 类社区各自的色标（左侧栏选中态与封面角标都用它，玩家靠颜色认类别）。 */
export const CATEGORY_COLOR: Record<string, string> = {
  residential: "#6ea8fe",
  commercial: "#ffa42d",
  industrial: "#9b8cff",
  park: "#8bdb46",
  education: "#4bc3f1",
  public: "#ff8f5e",
  mixed: "#c0cb78",
};

type P = { size?: number; color?: string; style?: React.CSSProperties };

const wrap = (size: number, color: string, style: React.CSSProperties | undefined, children: React.ReactNode) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth={1.7}
       strokeLinecap="round" strokeLinejoin="round" style={style} xmlns="http://www.w3.org/2000/svg">
    {children}
  </svg>
);

/** 面板标志：一张蓝图上有路网与地块，右下角一个图钉 —— 蓝图工坊的本体隐喻。 */
export const IconLogo = ({ size = 22, color = C.accent, style }: P) =>
  wrap(size, color, style, (
    <>
      <path d="M3 6.5 12 3l9 3.5-9 3.5-9-3.5Z" />
      <path d="M3 6.5v10L12 20v-7" />
      <path d="M21 6.5v10L12 20" />
      <path d="M7.5 12.2 12 14l4.5-1.8" opacity=".75" />
      <path d="M12 14v6" opacity=".5" />
    </>
  ));

export const IconClose = ({ size = 16, color = C.text, style }: P) =>
  wrap(size, color, style, <><path d="M6 6l12 12" /><path d="M18 6 6 18" /></>);

export const IconSearch = ({ size = 15, color = C.dim, style }: P) =>
  wrap(size, color, style, <><circle cx="10.5" cy="10.5" r="5.5" /><path d="M15 15l4 4" /></>);

export const IconHeart = ({ size = 14, color = C.like, style, filled }: P & { filled?: boolean }) =>
  <svg width={size} height={size} viewBox="0 0 24 24" style={style} xmlns="http://www.w3.org/2000/svg">
    <path d="M12 20s-7.2-4.6-7.2-9.4A4.1 4.1 0 0 1 12 7.6a4.1 4.1 0 0 1 7.2 3c0 4.8-7.2 9.4-7.2 9.4Z"
          fill={filled ? color : "none"} stroke={color} strokeWidth={1.7} strokeLinejoin="round" />
  </svg>;

export const IconDownload = ({ size = 14, color = C.download, style }: P) =>
  wrap(size, color, style, <><path d="M12 4v10" /><path d="M7.5 10 12 14.5 16.5 10" /><path d="M5 19h14" /></>);

/** 顶栏那颗上传按钮：方框 + 向上箭头（与原版「上传资产」同族语义）。 */
export const IconUpload = ({ size = 18, color = C.text, style }: P) =>
  wrap(size, color, style, <><path d="M12 19.5V9" /><path d="M7.5 13.5 12 9l4.5 4.5" /><path d="M4.5 5.5h15" /></>);

export const IconArea = ({ size = 14, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M4 7.5 12 4l8 3.5v9L12 20l-8-3.5Z" /><path d="M12 4v16" opacity=".55" /></>);

export const IconSpinner = ({ size = 18, color = C.accent, style }: P) =>
  <svg width={size} height={size} viewBox="0 0 24 24" style={style} xmlns="http://www.w3.org/2000/svg">
    <circle cx="12" cy="12" r="8" fill="none" stroke={color} strokeOpacity=".25" strokeWidth="2.4" />
    <path d="M20 12a8 8 0 0 0-8-8" fill="none" stroke={color} strokeWidth="2.4" strokeLinecap="round" />
  </svg>;

export const IconWarning = ({ size = 18, color = C.action, style }: P) =>
  wrap(size, color, style, <><path d="M12 4.5 20 19H4Z" /><path d="M12 10v4.2" /><path d="M12 16.6v.6" /></>);

// ---------------- 入口方块的图案（左上角 GameTopLeft）----------------

/**
 * FACT（0.3.1 定位到的两处实测结论，别再倒回去）：
 *  1. Cohtml 2.2.1.3 认 mask，但**只认能当成图片资源加载的 URL**。整份游戏 index.css / index.js 里
 *     data URI 出现 0 次，官方图标全是 `url(Media/Glyphs/Checkmark.svg)` 这种文件路径。
 *     0.3.0 用 `mask-image:url("data:image/svg+xml,...")` + `background-color:#fff` 的空 span，
 *     mask 没生效 → 剩下的就是一整块实心白方 —— 玩家看到的「只有白底」就是这么来的。
 *  2. 游戏的 Button 有 `src` 属性：`src` 存在时它自己渲染 `<img class=icon_be5 onError=缺图占位>`，
 *     `tinted` 时才走官方 TintedIcon（div + mask）。所以图案交给官方组件画，我只给路径。
 *     这样即使 svg 没到，官方占位图会顶上，最坏是蓝底没图案，不会再变成白块。
 * 图案本身随包发布在 UI/images/，宿主根 = UIModuleAsset 所在目录（FACT：ModManager.InitializeUIModules
 * 对每个模组 AddHostLocation("ui-mods", 资产目录)），所以引用写成 coui://ui-mods/images/xxx.svg。
 */
export const LAUNCHER_GLYPH_SRC = "coui://ui-mods/images/BlueprintHub_Glyph.svg";
