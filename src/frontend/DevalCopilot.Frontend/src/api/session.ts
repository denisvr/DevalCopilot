/**
 * The launch-session bootstrap context: the loopback API base URL and the per-launch
 * bearer secret. Held in module memory only — never localStorage, sessionStorage,
 * IndexedDB, the URL, or a log.
 *
 * The production path is a narrowly scoped Tauri command that returns this same shape;
 * it is added with the Tauri shell in a later increment. Until then, the only supported
 * way to obtain a session outside Tauri is the test-harness override below, which a
 * Playwright global setup injects with `page.addInitScript` before any application code
 * runs. There is deliberately no other channel: an interactive browser session with
 * neither Tauri nor the test harness present has no way to authenticate, by design.
 */
export interface LaunchSession {
  readonly baseUrl: string
  readonly secret: string
}

declare global {
  interface Window {
    __DEVALCOPILOT_SESSION__?: LaunchSession
    __TAURI__?: unknown
  }
}

let cachedSession: LaunchSession | null | undefined

function readTestHarnessSession(): LaunchSession | null {
  return window.__DEVALCOPILOT_SESSION__ ?? null
}

/**
 * Resolves the current launch session once and caches it in memory for the life of
 * the page. Returns null when no supported bootstrap channel is present.
 */
export function getLaunchSession(): LaunchSession | null {
  if (cachedSession !== undefined) {
    return cachedSession
  }

  if (window.__TAURI__) {
    // The Tauri bootstrap command is added with the Tauri shell increment. Until then,
    // a Tauri-hosted webview has no working session either.
    cachedSession = null
    return cachedSession
  }

  cachedSession = readTestHarnessSession()
  return cachedSession
}
