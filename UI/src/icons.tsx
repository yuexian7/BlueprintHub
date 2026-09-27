import React from "react";

/**
 * 图标全部内联成 SVG 组件，不走 coui:// 外部文件：
 * 省掉一个 host location，也省掉"图片没到 → 面板上出现空洞"这类只有实机才看得见的故障。
 *
 * 配色（我定的，蓝图工坊的一套）：
 *   面板底  #121a26 / 卡片  #17222f / 分隔  rgba(255,255,255,.08)
 *   主强调  蓝图青 #4fd1c5    行动  琥珀 #f0b429
 *   点赞  玫红 #ff6b81 → 已投 绿 #4caf7d    下载  蓝 #63a9ff    危险  #e5484d
 *   文本  #e6edf3 / 次要  #9fb0c0
 */
export const C = {
  bg: "#121a26",
  panel: "#17222f",
  panelHi: "#1d2b3a",
  line: "rgba(255,255,255,0.08)",
  accent: "#4fd1c5",
  action: "#f0b429",
  like: "#ff6b81",
  voted: "#4caf7d",
  download: "#63a9ff",
  danger: "#e5484d",
  text: "#e6edf3",
  dim: "#9fb0c0",
};

/** 7 类社区各自的色标（左侧栏选中态与封面角标都用它，玩家靠颜色认类别）。 */
export const CATEGORY_COLOR: Record<string, string> = {
  residential: "#6ea8fe",
  commercial: "#f0b429",
  industrial: "#9b8cff",
  park: "#4caf7d",
  education: "#4fd1c5",
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

/** 模组图标：一张"蓝图"上有路网与地块，右下角一个图钉 —— 蓝图工坊的本体隐喻。 */
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

export const IconClose = ({ size = 16, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M6 6l12 12" /><path d="M18 6 6 18" /></>);

export const IconBack = ({ size = 16, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M15 5l-7 7 7 7" /></>);

export const IconUser = ({ size = 18, color = C.dim, style }: P) =>
  wrap(size, color, style, <><circle cx="12" cy="8.5" r="3.5" /><path d="M5.5 20c1.2-3.6 3.6-5.4 6.5-5.4s5.3 1.8 6.5 5.4" /></>);

export const IconSearch = ({ size = 15, color = C.dim, style }: P) =>
  wrap(size, color, style, <><circle cx="10.5" cy="10.5" r="5.5" /><path d="M15 15l4 4" /></>);

export const IconHeart = ({ size = 14, color = C.like, style, filled }: P & { filled?: boolean }) =>
  <svg width={size} height={size} viewBox="0 0 24 24" style={style} xmlns="http://www.w3.org/2000/svg">
    <path d="M12 20s-7.2-4.6-7.2-9.4A4.1 4.1 0 0 1 12 7.6a4.1 4.1 0 0 1 7.2 3c0 4.8-7.2 9.4-7.2 9.4Z"
          fill={filled ? color : "none"} stroke={color} strokeWidth={1.7} strokeLinejoin="round" />
  </svg>;

export const IconDownload = ({ size = 14, color = C.download, style }: P) =>
  wrap(size, color, style, <><path d="M12 4v10" /><path d="M7.5 10 12 14.5 16.5 10" /><path d="M5 19h14" /></>);

export const IconArea = ({ size = 14, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M4 7.5 12 4l8 3.5v9L12 20l-8-3.5Z" /><path d="M12 4v16" opacity=".55" /></>);

export const IconSave = ({ size = 15, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M6 4h9l3.5 3.5V20H6Z" /><path d="M9.5 4v5h5.5V4" /><path d="M9 14.5h6.5" /></>);

export const IconUpload = ({ size = 15, color = C.accent, style }: P) =>
  wrap(size, color, style, <><path d="M12 19V8" /><path d="M7.5 12.5 12 8l4.5 4.5" /><path d="M5 5h14" /></>);

export const IconTrash = ({ size = 15, color = C.danger, style }: P) =>
  wrap(size, color, style, <><path d="M5.5 7h13" /><path d="M9 7V4.8h6V7" /><path d="M7 7l.9 13h8.2L17 7" /></>);

export const IconCheck = ({ size = 14, color = C.voted, style }: P) =>
  wrap(size, color, style, <path d="M5 12.8 9.6 17 19 7" />);

export const IconSpinner = ({ size = 18, color = C.accent, style }: P) =>
  <svg width={size} height={size} viewBox="0 0 24 24" style={style} xmlns="http://www.w3.org/2000/svg">
    <circle cx="12" cy="12" r="8" fill="none" stroke={color} strokeOpacity=".25" strokeWidth="2.4" />
    <path d="M20 12a8 8 0 0 0-8-8" fill="none" stroke={color} strokeWidth="2.4" strokeLinecap="round" />
  </svg>;

export const IconWarning = ({ size = 18, color = C.action, style }: P) =>
  wrap(size, color, style, <><path d="M12 4.5 20 19H4Z" /><path d="M12 10v4.2" /><path d="M12 16.6v.6" /></>);

export const IconCamera = ({ size = 15, color = C.dim, style }: P) =>
  wrap(size, color, style, <><path d="M4 8.5h3l1.4-2h7.2L17 8.5h3V18H4Z" /><circle cx="12" cy="13" r="3.2" /></>);
