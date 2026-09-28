/**
 * 面板文案的唯一出口。
 *
 * 口径（有机器证据）：
 *  · 词条由 C# 的 Locale.BuildPanelMap 注册进游戏本地化词典（12 个官方语言都有，
 *    非中英由 en 兜底，再由玩家的 BridgeTheLanguageGap 机翻 —— 它 Hook 的就是 Translate 这一层）。
 *  · 前端只能用 cs2/l10n 的 translate(key, fallback) 取词：**禁止硬编码句子**（TrafficToolEssentials
 *    的面板翻不动就是因为前端直接写了成品文案）。
 *  · useLocalization() 返回的是 { translate, unitSettings }，不是扁平词典（游戏 UI bundle dc() 的实现）。
 *  · 键的形状必须与 Bpc/PanelKeyKit.cs 逐字符一致，否则词典里查不到 → 面板露出 slug（T3 与本文件的
 *    dev-preview 台各钉一次）。
 */
import { useLocalization } from "cs2/l10n";

export const SETTING_ID = "BlueprintHub.BlueprintHub.BlueprintHubMod";

/** 与 PanelKeyKit.Key 同形：Options.BLUEPRINT_HUB.<SLUG>[setting.id.Panel.<SLUG>] */
export function keyOf(slug: string): string {
  return "Options.BLUEPRINT_HUB." + slug + "[" + SETTING_ID + ".Panel." + slug + "]";
}

export type LArgs = { [name: string]: string | number | undefined | null };

/** {name} 占位符填充：值由 C# 算成与语言无关的数字串，模板只负责词。 */
export function fill(template: string, args?: LArgs): string {
  if (!template || !args) return template || "";
  return template.replace(/\{(\w+)\}/g, (m: string, k: string) => {
    const v = args[k];
    return v === undefined || v === null ? m : String(v);
  });
}

const warned: { [slug: string]: boolean } = {};

export type Translate = (slug: string, args?: LArgs) => string;

/**
 * 取一个 hook 用的翻译器。词典没命中时**露出 slug 并打一次 warn**：
 * 那说明 AddSource 或键名对不上，是必须当场发现的接线错误，不该被一份前端硬编码文案悄悄盖过去。
 */
export function useT(): Translate {
  let ctx: ReturnType<typeof useLocalization> | null = null;
  try {
    ctx = useLocalization();
  } catch (e) {
    console.warn("[BlueprintHub] useLocalization failed", e);
    ctx = null;
  }
  return (slug: string, args?: LArgs): string => {
    if (!slug) return "";
    let s: string | null = null;
    try {
      if (ctx && typeof ctx.translate === "function") s = ctx.translate(keyOf(slug), "");
    } catch (e) {
      console.warn("[BlueprintHub] translate failed", slug, e);
    }
    if (!s) {
      if (!warned[slug]) {
        warned[slug] = true;
        console.warn("[BlueprintHub] 词典没有这条词条: " + slug + " → " + keyOf(slug));
      }
      return slug;
    }
    return fill(s, args);
  };
}

/**
 * 计数缩写：与原版 UI 同口径（游戏自己的 makePretty 用 {VALUE}k / m / b）。
 * 中文玩家熟「万」，所以 zh-* 走 万；其余走 k/m/b。lang 由 C# 发过来。
 */
export function prettyCount(n: number, lang: string): string {
  const v = Math.round(n || 0);
  if (v < 1000) return String(v);
  const zh = !!lang && lang.indexOf("zh") === 0;
  if (zh) {
    if (v < 100000000) {
      const w = v / 10000;
      return (w >= 100 ? String(Math.round(w)) : w.toFixed(1).replace(/\.0$/, "")) + "万";
    }
    return (v / 100000000).toFixed(1).replace(/\.0$/, "") + "亿";
  }
  const units = ["k", "m", "b"];
  for (let i = 0; i < units.length; i++) {
    const div = 1000 * Math.pow(1000, i);
    if (Math.round(Math.abs(v) / div) < 1000 && Math.abs(v) >= div) {
      const q = v / div;
      return (q >= 100 ? String(Math.round(q)) : q.toFixed(1).replace(/\.0$/, "")) + units[i];
    }
  }
  return String(v);
}
