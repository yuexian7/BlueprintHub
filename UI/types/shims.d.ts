declare module "cs2/modding" {
  export type ModRegistrar = (registry: {
    append: (slot: string, component: React.ComponentType) => void;
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

// 游戏自带的 React 组件库，运行时由 window["cs2/ui"] 提供（webpack externals）。
// Button variant="floating" 就是原版左上角那排小方按钮：theme 给 40rem 尺寸、6rem 圆角、
// --accentColorNormal 蓝底，以及 hover / active / .selected 状态 —— 入口按钮要「和旁边的图标一个规范」，
// 用它就不是我在模仿规范，而是我在用规范本身。
// 注意：Button 会把未识别的 prop 透传到根 <button>，但**不透传 alt**；
//       Tooltip 的 tooltip 必须是 string，否则报 "[tooltip] must be of type string"。
declare module "cs2/ui" {
  export const Button: React.ComponentType<{
    variant?: "default" | "floating" | "editor" | "primary" | "destructive" | "hover" | "square" | "round";
    selectable?: boolean;
    selected?: boolean;
    disabled?: boolean;
    onSelect?: (e: unknown) => void;
    className?: string;
    style?: React.CSSProperties;
    children?: React.ReactNode;
    [key: string]: unknown;
  }>;
  export const Tooltip: React.ComponentType<{
    tooltip: string;
    direction?: string;
    children?: React.ReactNode;
  }>;
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
