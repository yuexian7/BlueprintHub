/**
 * C# 桥（组名必须与 BlueprintHubUISystem.kGroup 一致）。
 *
 * 两条通道（其余一律不加，绑定越少越好排障）：
 *   GetState  GetterValueBinding<string> —— C# 是唯一真值源，整块面板状态一个 JSON
 *   Cmd       TriggerBinding<string>     —— 前端回报动作，格式 "kind|arg"
 *
 * 为什么全裹 try/catch：游戏的模块 loader 是 `import(url).then(...).catch(()=>{})`，
 * 模块顶层任何抛错都会被静默吞掉 → 面板不出现且日志里什么都没有（已踩过的坑）。
 */
import { bindValue, trigger, useValue } from "cs2/api";
import { CATEGORY_COLOR } from "./icons";

export const GROUP = "BlueprintHub";
export const STATE = "GetState";
export const CMD = "Cmd";

/** 命令参数里允许出现 | （搜索词），所以 C# 只按第一个 | 切分。 */
export function cmd(kind: string, arg?: string | number): void {
  try {
    const payload = arg === undefined || arg === null ? kind : kind + "|" + String(arg);
    trigger(GROUP, CMD, payload);
  } catch (e) {
    console.warn("[BlueprintHub] cmd failed", kind, e);
  }
}

function safeBind(): unknown | null {
  try {
    return bindValue<string>(GROUP, STATE, "");
  } catch (e) {
    console.warn("[BlueprintHub] bindValue failed", e);
    return null;
  }
}

const state$ = safeBind();

// ---------------- 与 C# Build() 一一对应的类型 ----------------

export interface CategoryCell {
  id: string;
  label: string;
  definition: string;
  count: number;
  selected: boolean;
}

export interface Option {
  id: string;
  label: string;
}

export interface ItemCard {
  id: string;
  name: string;
  author: string;
  authorId: string;
  cover: string;
  likes: number;
  downloads: number;
  likesText: string;
  downloadsText: string;
  liked: boolean;
  used: boolean;
  areaM2: number;
  areaText: string;
  areaFull: string;      // tooltip / 详情用完整值
  areaClass: string;
  areaClassLabel: string;
  updated: string;
  assets: number;
  desc: string;
  categories: string;   // 展示用：住宅区 / 混合区
  cats: string;         // 原始 id，逗号分隔：颜色与占位图按它取
}

export interface UiState {
  seq: number;
  visible: boolean;
  opacity: number;
  modVersion: string;
  modAuthor: string;
  title: string;
  hint: string;
  hotkey?: string;
  status: "loading" | "ready" | "empty" | "error" | "noresult" | string;
  statusText: string;
  subtitle: string;
  truncated?: boolean;
  playerKey?: string;
  toast?: { id: number; text: string; kind: string };
  categories: CategoryCell[];
  menu?: {
    tileHint: string;
    search: string;
    area: string;
    areaLabel: string;
    areas: Option[];
    sort: string;
    sortLabel: string;
    sorts: Option[];
    page: number;
    pages: number;
    total: number;
    pageSize: number;
  };
  items: ItemCard[];
}

export const EMPTY_STATE: UiState = {
  seq: 0,
  visible: false,
  opacity: 0.5,
  modVersion: "0.1.0",
  modAuthor: "yuexian",
  title: "蓝图工坊",
  hint: "分享蓝图请点击市辖区面板的上传按钮",
  status: "loading",
  statusText: "正在读取工坊索引…",
  subtitle: "",
  categories: [
    { id: "residential", label: "住宅区", definition: "", count: 0, selected: false },
    { id: "commercial", label: "商业区", definition: "", count: 0, selected: false },
    { id: "industrial", label: "产业区", definition: "", count: 0, selected: false },
    { id: "park", label: "公园区", definition: "", count: 0, selected: false },
    { id: "education", label: "文教区", definition: "", count: 0, selected: false },
    { id: "public", label: "公共区", definition: "", count: 0, selected: false },
    { id: "mixed", label: "混合区", definition: "", count: 0, selected: false },
  ],
  menu: {
    tileHint: "1 区块 ≈ 388,129 ㎡",
    search: "",
    area: "all",
    areaLabel: "全部",
    areas: [
      { id: "all", label: "全部" },
      { id: "small", label: "小 · ≤2 区块" },
      { id: "medium", label: "中 · 3~9 区块" },
      { id: "large", label: "大 · >9 区块" },
    ],
    sort: "weekly",
    sortLabel: "周热度",
    sorts: [
      { id: "weekly", label: "周热度" },
      { id: "total", label: "总热度" },
      { id: "uploadTime", label: "上传时间" },
      { id: "area", label: "面积" },
      { id: "name", label: "名称" },
    ],
    page: 1,
    pages: 0,
    total: 0,
    pageSize: 15,
  },
  items: [],
};

/** 解析失败 / 后端还没起来时一律回退到 EMPTY_STATE：面板永远能画出来。 */
export function useUiState(): UiState {
  if (!state$) return EMPTY_STATE;
  let raw = "";
  try {
    raw = useValue<string>(state$) ?? "";
  } catch (e) {
    console.warn("[BlueprintHub] useValue failed", e);
    return EMPTY_STATE;
  }
  if (!raw) return EMPTY_STATE;
  try {
    const parsed = JSON.parse(raw) as UiState;
    if (!parsed || typeof parsed !== "object") return EMPTY_STATE;
    return {
      ...EMPTY_STATE,
      ...parsed,
      categories: Array.isArray(parsed.categories) && parsed.categories.length
        ? parsed.categories
        : EMPTY_STATE.categories,
      menu: parsed.menu ? { ...EMPTY_STATE.menu!, ...parsed.menu } : EMPTY_STATE.menu!,
      items: Array.isArray(parsed.items) ? parsed.items : [],
    };
  } catch (e) {
    console.warn("[BlueprintHub] state json parse failed", e);
    return EMPTY_STATE;
  }
}

/** 7 类社区色标：唯一出处是 icons.tsx 的 CATEGORY_COLOR，这里只做别名（前端两处共用一份，改色不会漏）。 */
export const CAT_COLOR: { [id: string]: string } = CATEGORY_COLOR;

/** 卡片没有封面时的占位图：直接内联 SVG，不引任何外部图片（省掉一个 host location 的失败面）。 */
export function placeholderCover(categoryIds: string, seed: string): string {
  const first = (categoryIds || "").split(",")[0].trim();
  const color = CAT_COLOR[first] || "#4fd1c5";
  let h = 2166136261;
  for (let i = 0; i < seed.length; i++) {
    h ^= seed.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  const rand = (n: number): number => {
    h ^= h << 13; h ^= h >>> 17; h ^= h << 5;
    return Math.abs(h % n);
  };
  let paths = "";
  for (let i = 0; i < 9; i++) {
    const x = 6 + rand(84);
    const y = 10 + rand(50);
    const w = 8 + rand(26);
    const hh = 6 + rand(18);
    const o = 0.1 + rand(28) / 100;
    paths += `<rect x="${x}" y="${y}" width="${w}" height="${hh}" rx="1.5" fill="${color}" opacity="${o.toFixed(2)}"/>`;
  }
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 70" preserveAspectRatio="none">` +
    `<rect width="100" height="70" fill="#101a26"/>` +
    `<g opacity="0.85">${paths}</g>` +
    `<g stroke="${color}" stroke-width="0.7" opacity="0.55" fill="none">` +
    `<path d="M0 46 H100"/><path d="M34 0 V70"/></g>` +
    `<g fill="none" stroke="#ffffff" stroke-opacity="0.07" stroke-width="0.4">` +
    `<path d="M0 23 H100M0 60 H100M67 0 V70"/></g>` +
    `</svg>`;
  return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
}

/** 首字母头像（需求 5 的头像在没有真实头像时的样子）。 */
export function avatarColor(seed: string): string {
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) & 0xffffff;
  const palette = ["#4fd1c5", "#6ea8fe", "#f0b429", "#ff8f5e", "#9b8cff", "#4caf7d", "#ff6b81", "#c0cb78"];
  return palette[h % palette.length];
}
