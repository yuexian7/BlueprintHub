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

/**
 * 卡片没有封面时的占位图案。
 * 0.3.1 改动的原因：这里原本返回 `data:image/svg+xml,...`，而 Cohtml 对 data URI 的支持是
 * 「mask 完全不吃、图片路径按资源加载」—— 官方整份 index.css/index.js 里 data URI 出现 0 次，
 * 左上角那颗方块就是被这个坑成实心白块的。占位图不再走 URL，改成前端**内联 `<svg>` 元素**：
 * 面板里的图标（心形/箭头/警告）一直是内联 SVG，实机确认画得出来，所以这条路比 data URI 稳。
 * 这个函数只负责「画什么」（按 seed 稳定哈希出几块楼），不负责「怎么画」。
 */
export interface CoverBlock { x: number; y: number; w: number; h: number; o: string }
export interface CoverPlan { color: string; blocks: CoverBlock[] }

export function coverPlan(categoryIds: string, seed: string): CoverPlan {
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
  const blocks: CoverBlock[] = [];
  for (let i = 0; i < 9; i++) {
    blocks.push({
      x: 6 + rand(84), y: 10 + rand(50), w: 8 + rand(26), h: 6 + rand(18),
      o: (0.1 + rand(28) / 100).toFixed(2),
    });
  }
  return { color, blocks };
}

/** 首字母头像的小色块（作者名哈希出来的颜色，不是身份）。 */
export function avatarColor(seed: string): string {
  let h = 0;
  for (let i = 0; i < (seed || "").length; i++) h = (h * 31 + seed.charCodeAt(i)) & 0xffffff;
  const palette = ["#4bc3f1", "#6ea8fe", "#f0b429", "#ff8f5e", "#9b8cff", "#4caf7d", "#ff6b81", "#c0cb78"];
  return palette[h % palette.length];
}
