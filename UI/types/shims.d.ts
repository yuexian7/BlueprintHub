/**
 * cs2/modding —— 逐条抄自游戏自带的官方模板
 * `<game>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/template/types/modding.d.ts`
 * 要点（0.3.1 校准）：
 *   append(target, component, index?) 的 target 合法值只有 7 个：
 *     Menu / Editor / Game / GameTopLeft / GameTopRight / GameBottomRight / UniversalModMenu
 *   （**没有** GameBottomLeft —— 0.3.0 的注释里写错了一处，别再照它加槽位。）
 *   index 可以是负数，用来插到已有列表前面。
 */
declare module "cs2/modding" {
  export type AppendHookTargets =
    "Menu" | "Editor" | "Game" | "GameTopLeft" | "GameTopRight" | "GameBottomRight" | "UniversalModMenu";
  export type ModRegistrar = (registry: {
    append: (target: AppendHookTargets, component: React.ComponentType, index?: number) => void;
    extend: (modulePath: string, exportNameOrValue: unknown, extendCb?: unknown) => void;
    get: (modulePath: string, exportName: string) => any;
    hasAppend: (target: AppendHookTargets) => boolean;
  }) => void;
}

declare module "cs2/api" {
  export function bindValue<T>(group: string, name: string, initial: T): unknown;
  export function useValue<T>(binding: unknown): T;
  export function trigger(group: string, name: string, arg?: unknown): void;
  export function call<T>(group: string, name: string, arg?: unknown): Promise<T>;
}

/**
 * 游戏自带的本地化面（FACT：从游戏 UI bundle 里读到的实现，不是猜的）
 *   cs2/l10n 导出 { Localized, LocalizedNumber, ..., useLocalization }
 *   useLocalization() = useCachedLocalization() → **{ translate(key, fallback), unitSettings }**
 *   —— 它返回的是一个带 translate() 的对象，不是扁平词典。
 *      （按 `bag[key]` 取值的那种写法取不到东西：minified 源码 dc() 里返回的就是 {translate,unitSettings}。）
 *   translate 走 LocalizationContext → 游戏 C# 侧 UILocalizationManager.Translate
 *      → BridgeTheLanguageGap 正是 Hook 这一层，所以只有走 translate() 的面板文案才翻得动。
 */
declare module "cs2/l10n" {
  export function useLocalization(): {
    translate: (key: string, fallback?: string) => string | null;
    unitSettings?: unknown;
  };
  export const Localized: React.ComponentType<{ value?: unknown; transformer?: unknown }>;
}

/**
 * cs2/ui —— 逐条抄自官方模板同目录的 ui.d.ts，并从游戏 UI bundle 里核对过实现（不是猜的）：
 *   Button 的 variant 合法值 = "flat" | "primary" | "round" | "menu" | "icon" | "floating" | "text" | "default"
 *     （index.js @713141 的映射表 bj={flat,primary,round,menu,default,icon,floating,text}；
 *      0.3.0 这份声明里那几个 "editor"/"destructive"/"square" 是我自己编的，删掉了。）
 *   variant="floating" → theme {button:"button_ke4", icon:"icon_be5"}
 *     .button_ke4 = 40rem 方块 / padding var(--gap2) / background-color var(--accentColorNormal)
 *                   / 四角 var(--floatingToggleBorderRadius)，另带 :hover(:active/.selected/[disabled])
 *     .icon_be5   = width:100%;height:100%;--iconColor:rgb(250,250,250)
 *   Button 实现（icon-button.tsx 的 $b）：`src` 存在 → tinted ? <TintedIcon> : <img onError=缺图占位>；
 *     `src` 不存在 → 只画按钮本体再塞 children。所以图案要给 src，别自己塞一个涂色的 span。
 *   FloatingButton = Button + {tinted:true, theme:floating}，是官方给「左上角方块」的现成组件。
 *   Tooltip 的 children 要求是可挂 ref 的元素；tooltip 必须给 string。
 */
declare module "cs2/ui" {
  export type ButtonVariant = "flat" | "primary" | "round" | "menu" | "icon" | "floating" | "text" | "default";
  export const Button: React.ComponentType<{
    variant?: ButtonVariant;
    src?: string;
    tinted?: boolean;
    theme?: Record<string, unknown>;
    focusKey?: unknown;
    selected?: boolean;
    disabled?: boolean;
    onSelect?: (e?: unknown) => void;
    className?: string;
    style?: React.CSSProperties;
    children?: React.ReactNode;
    [key: string]: unknown;
  }>;
  export const FloatingButton: React.ComponentType<{
    src?: string;
    tinted?: boolean;
    focusKey?: unknown;
    selected?: boolean;
    disabled?: boolean;
    onSelect?: (e?: unknown) => void;
    className?: string;
    style?: React.CSSProperties;
    children?: React.ReactNode;
    [key: string]: unknown;
  }>;
  export const Icon: React.ComponentType<{ src: string; tinted?: boolean | string; className?: string; children?: React.ReactNode }>;
  export const Tooltip: React.ComponentType<{
    tooltip: React.ReactNode;
    direction?: "up" | "down" | "left" | "right";
    alignment?: "start" | "center" | "end";
    disabled?: boolean;
    children?: React.ReactNode;
  }>;
  export const Panel: React.ComponentType<Record<string, unknown>>;
  export const PanelSection: React.ComponentType<Record<string, unknown>>;
  export const Portal: React.ComponentType<{ children?: React.ReactNode }>;
  export const Scrollable: React.ComponentType<Record<string, unknown>>;
  export function useTooltip(): unknown;
}

declare module "cohtml/cohtml" {
  const engine: {
    call: (name: string, ...args: unknown[]) => Promise<unknown>;
    on: (name: string, cb: (...args: unknown[]) => void) => void;
    trigger: (name: string, ...args: unknown[]) => void;
  };
  export default engine;
}

declare module "*.svg" {
  const content: string;
  export default content;
}
