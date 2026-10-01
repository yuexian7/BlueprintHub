/**
 * 预览台工具共用的一小块：找浏览器 + 找端口。
 * shot.mjs（实拍）与 verify-ui.mjs（DOM 断言）都要干这两件事，抄两份迟早一份过期。
 */
import fs from "node:fs";
import path from "node:path";

const CANDIDATES = [
  process.env.BPH_CHROME,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
  process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, "Google/Chrome/Application/chrome.exe") : "",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
].filter(Boolean);

export function findChrome() {
  return CANDIDATES.find((p) => fs.existsSync(p)) || null;
}

/**
 * dev-preview 在 8765 被系统划走时会自己顺延，所以这里跟着探测实际端口，而不是把端口写死。
 * 判据用 /state 返回的是不是 JSON —— 端口被别的程序占着时它也会连上，但不会给 JSON。
 */
export async function findBase(preferred = 0) {
  const ports = preferred ? [preferred] : [8765, 8766, 8767, 8768, 8769, 8770, 8771, 8772, 8773];
  for (const p of ports) {
    try {
      const r = await fetch(`http://127.0.0.1:${p}/state`, { signal: AbortSignal.timeout(1200) });
      if (r.ok && (await r.text()).trimStart().startsWith("{")) return `http://127.0.0.1:${p}`;
    } catch { /* 换下一个 */ }
  }
  return null;
}

/** query 里的 ?act= / &act= 段含 : ; = 三种字符，交给浏览器前先编码（Node 侧 searchParams 会自动解回来）。 */
export function encodeQuery(q) {
  return String(q || "").replace(/([?&]act=)([^&]*)/g, (m, pre, a) => pre + encodeURIComponent(a));
}
