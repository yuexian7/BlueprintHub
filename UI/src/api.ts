/**
 * C# 桥（组名必须与 BlueprintHubUISystem.kGroup 一致）。
 *
 * 两条通道（其余一律不加，绑定越少越好排障）：
 *   GetState  GetterValueBinding<string> —— C# 是唯一真值源，整块面板状态一个 JSON
 *   Cmd       TriggerBinding<string>     —— 前端回报动作，格式 "kind|arg"
 *
 * 0.3.0 起这条 JSON 里**没有任何成品句子**：只有 slug（词条键）与数字，句子交给 cs2/l10n（见 l10n.ts）。
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
  slug: string;         // CAT_<id>
  descSlug: string;     // DESC_<id>
  label: string;        // 未知 id 的兜底显示
  count: number;
  selected: boolean;
}

export interface Option { id: string; slug: string; label: string }

export interface ItemCard {
  id: string;
  name: string;
  author: string;       // 空串 = 匿名（前端用 ANONYMOUS 词条）
  authorId: string;
  cover: string;
  likes: number;
  downloads: number;
  liked: boolean;
  used: boolean;
  areaM2: number;
  tiles: string;        // 区块数（已按语言无关形态算好）
  m2: string;           // 千分位完整 ㎡
  wan: string;          // 万㎡ 的数值部分
  km2: string;          // km² 的数值部分
  areaClass: string;
  areaClassSlug: string;
  agoUnit: string;      // now|min|hour|day|month|year（""= 没有可信时间）
  agoN: number;
  assets: number;
  desc: string;
  cats: string;         // 原始 id，逗号分隔：颜色与占位图按它取
}

export interface UiState {
  seq: number;
  visible: boolean;
  opacity: number;              // 0~1，直接当背景 alpha 用
  lang: string;                 // zh-HANS / en-US / …（C# 取 activeLocaleId）
  dev: boolean;                 // 读的是本地 dev-catalog（只有开发者机器可能为 true）
  modVersion: string;
  titleSlug: string;
  hotkey: string;
  status: "loading" | "ready" | "empty" | "error" | "noresult" | string;
  statusSlug: string;
  statusDetail: string;         // 一行技术信息（镜像名 / 文件 / 异常类型），不是句子
  truncated?: boolean;
  maxItems?: number;
  tileM2?: string;
  toast?: { id: number; slug: string; kind: string };
  categories: CategoryCell[];
  menu?: {
    search: string;
    area: string;
    areaSlug: string;
    areas: Option[];
    sort: string;
    sortSlug: string;
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
  opacity: 0.8,
  lang: "en-US",
  dev: false,
  modVersion: "0.3.0",
  titleSlug: "PANEL_TITLE",
  hotkey: "",
  status: "loading",
  statusSlug: "STATUS_LOADING",
  statusDetail: "",
  categories: [
    "residential", "commercial", "industrial", "park", "education", "public", "mixed",
  ].map((id) => ({ id, slug: "CAT_" + id, descSlug: "DESC_" + id, label: id, count: 0, selected: false })),
  menu: {
    search: "",
    area: "all",
    areaSlug: "AREA_all",
    areas: [
      { id: "all", slug: "AREA_all", label: "all" },
      { id: "small", slug: "AREA_small", label: "small" },
      { id: "medium", slug: "AREA_medium", label: "medium" },
      { id: "large", slug: "AREA_large", label: "large" },
    ],
    sort: "weekly",
    sortSlug: "SORT_weekly",
    sorts: [
      { id: "weekly", slug: "SORT_weekly", label: "weekly" },
      { id: "total", slug: "SORT_total", label: "total" },
      { id: "uploadTime", slug: "SORT_uploadTime", label: "uploadTime" },
      { id: "area", slug: "SORT_area", label: "area" },
      { id: "name", slug: "SORT_name", label: "name" },
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

/** 7 类色标：唯一出处是 icons.tsx 的 CATEGORY_COLOR（前端两处共用一份，改色不会漏）。 */
export const CAT_COLOR: { [id: string]: string } = CATEGORY_COLOR;

/** 一条卡片的面积文案：小于一格的东西讲「㎡」，其余讲「区块 + 万㎡/km²」。 */
export function areaSlugFor(it: ItemCard): string {
  return it.areaM2 > 0 && it.areaM2 < 10000 ? "CARD_AREA_TINY" : "CARD_AREA";
}

export function areaArgs(it: ItemCard): { [k: string]: string } {
  return { tiles: it.tiles, m2: it.m2, wan: it.wan, km2: it.km2 };
}

/** 卡片没有封面时的占位图：直接内联 SVG，不引任何外部图片（省掉一个 host location 的失败面）。 */
export function placeholderCover(categoryIds: string, seed: string): string {
  const first = (categoryIds || "").split(",")[0].trim();
  const color = CAT_COLOR[first] || "#4bc3f1";
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

/** 首字母头像的小色块（作者名哈希出来的颜色，不是身份）。 */
export function avatarColor(seed: string): string {
  let h = 0;
  for (let i = 0; i < (seed || "").length; i++) h = (h * 31 + seed.charCodeAt(i)) & 0xffffff;
  const palette = ["#4bc3f1", "#6ea8fe", "#f0b429", "#ff8f5e", "#9b8cff", "#4caf7d", "#ff6b81", "#c0cb78"];
  return palette[h % palette.length];
}
