import { invoke } from '@tauri-apps/api/core'
import { listen } from '@tauri-apps/api/event'

/**
 * The launch-session bootstrap context: the loopback API base URL and the per-launch
 * bearer secret. Held in module memory only — never localStorage, sessionStorage,
 * IndexedDB, the URL, a file, or a log.
 *
 * The production path is a narrowly scoped Tauri command (`get_launch_session`) that
 * returns this same shape from the Rust shell's in-memory state. The only other supported
 * way to obtain a session is the test-harness override below, which a Playwright global
 * setup injects with `page.addInitScript` before any application code runs. There is
 * deliberately no other channel: an interactive browser session with neither Tauri nor the
 * test harness present has no way to authenticate, by design.
 */
export interface LaunchSession {
  readonly baseUrl: string
  readonly secret: string
}

/**
 * - idle: not yet initialized.
 * - initializing: the Tauri bootstrap command is in flight (or the test-harness lookup is
 *   about to run — that one resolves synchronously, so this is only ever observed for
 *   the Tauri path).
 * - ready: a session was obtained and is currently valid.
 * - failed: no session could be obtained (no Tauri and no test harness present, or the
 *   Tauri bootstrap command itself failed/returned nothing — the sidecar never became
 *   ready). This never falls back to the test-harness channel.
 * - disconnected: a session was ready, but the shell reported the sidecar is gone. The
 *   retained session is discarded; a previous session is never reused.
 */
export type SessionStatus = 'idle' | 'initializing' | 'ready' | 'failed' | 'disconnected'

declare global {
  interface Window {
    __DEVALCOPILOT_SESSION__?: LaunchSession
    __TAURI_INTERNALS__?: unknown
  }
}

interface TauriLaunchSessionResponse {
  baseUrl: string
  secret: string
}

let cachedSession: LaunchSession | null | undefined
let status: SessionStatus = 'idle'
let initialized: Promise<void> | undefined
const statusListeners = new Set<() => void>()

function setStatus(next: SessionStatus): void {
  status = next
  statusListeners.forEach((listener) => listener())
}

function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window
}

function readTestHarnessSession(): LaunchSession | null {
  return window.__DEVALCOPILOT_SESSION__ ?? null
}

/** The current session, if one has been resolved. Safe to call synchronously anywhere. */
export function getLaunchSession(): LaunchSession | null {
  return cachedSession ?? null
}

export function getSessionStatus(): SessionStatus {
  return status
}

export function subscribeToSessionStatus(listener: () => void): () => void {
  statusListeners.add(listener)
  return () => statusListeners.delete(listener)
}

async function resolveTauriSession(): Promise<void> {
  setStatus('initializing')
  // Set by the disconnect listener if the sidecar exits at any point from here on,
  // including while `get_launch_session` below is still resolving. Registering the
  // listener — and awaiting its registration — before invoking that command is what makes
  // this reliable: Tauri events are fire-and-forget, so a listener added only after the
  // command resolved could miss a disconnect that happened in that exact gap, leaving the
  // session looking `ready` when the sidecar was already gone.
  let disconnectedDuringBootstrap = false

  try {
    await listen('devalcopilot://sidecar-disconnected', () => {
      disconnectedDuringBootstrap = true
      cachedSession = null
      setStatus('disconnected')
    })

    const session = await invoke<TauriLaunchSessionResponse | null>('get_launch_session')

    if (disconnectedDuringBootstrap) {
      // Already reported via the listener above; do not overwrite 'disconnected' with
      // 'failed' just because the command also settled (with or without a session).
      return
    }

    if (!session) {
      cachedSession = null
      setStatus('failed')
      return
    }

    cachedSession = { baseUrl: session.baseUrl, secret: session.secret }
    setStatus('ready')
  } catch {
    // Covers both a failed `listen` registration and a failed `invoke`: either way this
    // fails closed rather than proceeding without disconnect observation. A failed Tauri
    // bootstrap never falls back to the test-harness channel: outside a real Tauri runtime
    // there is no bundled WebView to expose that command to, and inside one, falling back
    // would mean accepting a session this specific launch never issued.
    cachedSession = null
    setStatus('failed')
  }
}

/**
 * Resolves the launch session once, at application startup. Safe to call more than once —
 * later calls await the same in-flight or completed resolution.
 */
export function initializeLaunchSession(): Promise<void> {
  if (initialized) {
    return initialized
  }

  initialized = isTauriRuntime()
    ? resolveTauriSession()
    : Promise.resolve().then(() => {
        cachedSession = readTestHarnessSession()
        setStatus(cachedSession ? 'ready' : 'failed')
      })

  return initialized
}
