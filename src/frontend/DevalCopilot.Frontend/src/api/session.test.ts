import { afterEach, describe, expect, it, vi } from 'vitest'

const invokeMock = vi.fn()
const listenMock = vi.fn()

vi.mock('@tauri-apps/api/core', () => ({ invoke: (...args: unknown[]) => invokeMock(...args) }))
vi.mock('@tauri-apps/api/event', () => ({ listen: (...args: unknown[]) => listenMock(...args) }))

// Module-level state (cachedSession, status, the memoized init promise) must not leak
// between tests, so each test re-imports a fresh module instance.
async function freshSessionModule() {
  vi.resetModules()
  return import('./session')
}

describe('session', () => {
  afterEach(() => {
    delete (window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__
    delete window.__DEVALCOPILOT_SESSION__
    invokeMock.mockReset()
    listenMock.mockReset()
    localStorage.clear()
    sessionStorage.clear()
  })

  it('resolves a session via the narrowly scoped Tauri bootstrap command when running inside Tauri', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc' })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(invokeMock).toHaveBeenCalledWith('get_launch_session')
    expect(session.getLaunchSession()).toEqual({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc', startupWarnings: [] })
    expect(session.getSessionStatus()).toBe('ready')
    expect(session.getStartupFailure()).toBeNull()
  })

  it('does not fall back to the test-harness session when the Tauri bootstrap fails', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    window.__DEVALCOPILOT_SESSION__ = { baseUrl: 'http://test', secret: 'test-harness-secret' }
    invokeMock.mockRejectedValue(new Error('sidecar never became ready'))
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getLaunchSession()).toBeNull()
    expect(session.getSessionStatus()).toBe('failed')
  })

  it('reports failed, not a fallback session, when the bootstrap command resolves with nothing', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    window.__DEVALCOPILOT_SESSION__ = { baseUrl: 'http://test', secret: 'test-harness-secret' }
    invokeMock.mockResolvedValue(null)
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getLaunchSession()).toBeNull()
    expect(session.getSessionStatus()).toBe('failed')
  })

  it('uses the injected test-harness session only when Tauri is not present', async () => {
    window.__DEVALCOPILOT_SESSION__ = { baseUrl: 'http://127.0.0.1:5099', secret: 'harness-secret' }
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(invokeMock).not.toHaveBeenCalled()
    expect(session.getLaunchSession()).toEqual({ baseUrl: 'http://127.0.0.1:5099', secret: 'harness-secret' })
    expect(session.getSessionStatus()).toBe('ready')
  })

  it('reports failed when neither Tauri nor the test harness provides a session', async () => {
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getLaunchSession()).toBeNull()
    expect(session.getSessionStatus()).toBe('failed')
  })

  it('keeps the session in memory only — never in browser storage', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'super-secret-value' })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(document.cookie).not.toContain('super-secret-value')
  })

  it('discards the session and reports disconnected when the shell reports the sidecar exited', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc' })
    let disconnectHandler: (() => void) | undefined
    listenMock.mockImplementation((_event: string, handler: () => void) => {
      disconnectHandler = handler
      return Promise.resolve(() => undefined)
    })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()
    expect(session.getSessionStatus()).toBe('ready')

    disconnectHandler?.()

    expect(session.getLaunchSession()).toBeNull()
    expect(session.getSessionStatus()).toBe('disconnected')
  })

  it('reports disconnected, never ready, when the sidecar exits between the command resolving and the caller\'s next step', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    let disconnectHandler: (() => void) | undefined
    listenMock.mockImplementation((_event: string, handler: () => void) => {
      disconnectHandler = handler
      return Promise.resolve(() => undefined)
    })
    // Simulates the exact race this fix closes: a disconnect landing the instant
    // `get_launch_session` resolves — i.e. before the code that used to register the
    // listener only afterward would ever have run. Because the listener is registered
    // before this call is even made, it is already active to catch this.
    invokeMock.mockImplementation(async () => {
      disconnectHandler?.()
      return { baseUrl: 'http://127.0.0.1:5080', secret: 'abc' }
    })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getSessionStatus()).toBe('disconnected')
    expect(session.getLaunchSession()).toBeNull()
  })

  it('registers the disconnect listener only once, even if initialization is requested repeatedly', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc' })
    listenMock.mockResolvedValue(() => undefined)
    const session = await freshSessionModule()

    await Promise.all([
      session.initializeLaunchSession(),
      session.initializeLaunchSession(),
      session.initializeLaunchSession(),
    ])
    await session.initializeLaunchSession()

    expect(listenMock).toHaveBeenCalledTimes(1)
    expect(invokeMock).toHaveBeenCalledTimes(1)
  })

  it('fails closed without ever requesting a session when disconnect-listener registration itself fails', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    listenMock.mockRejectedValue(new Error('IPC channel unavailable'))
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc' })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getLaunchSession()).toBeNull()
    expect(session.getSessionStatus()).toBe('failed')
    expect(invokeMock).not.toHaveBeenCalled()
  })

  // ADR-0072: the shell's closed startup failure and the closed warnings of a ready session.
  it('keeps the closed startup failure the shell reported and never becomes ready afterwards', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockRejectedValue({
      kind: 'startup-failed',
      stage: 'Workspaces',
      reason: 'StateIncoherent',
      action: 'RequestReviewedRepair',
    })
    const session = await freshSessionModule()

    await session.initializeLaunchSession()
    await session.initializeLaunchSession()

    expect(invokeMock).toHaveBeenCalledTimes(1)
    expect(session.getSessionStatus()).toBe('failed')
    expect(session.getLaunchSession()).toBeNull()
    expect(session.getStartupFailure()).toEqual({
      stage: 'Workspaces',
      reason: 'StateIncoherent',
      action: 'RequestReviewedRepair',
      code: 'DCSF1-Workspaces-StateIncoherent-RequestReviewedRepair',
    })
  })

  it.each([
    [{ kind: 'startup-failed', stage: 'Elsewhere', reason: 'StateIncoherent', action: 'RequestReviewedRepair' }],
    [{ kind: 'startup-failed', stage: 'Workspaces', reason: 'StorageUnavailable', action: 'RequestReviewedRepair' }],
    [{ kind: 'startup-failed', stage: 'Workspaces', reason: 'StateIncoherent', action: 'RequestReviewedRepair', path: 'C:/private' }],
    [{ kind: 'other', stage: 'Workspaces', reason: 'StateIncoherent', action: 'RequestReviewedRepair' }],
    ['raw stderr text'],
    [null],
  ])('treats a malformed or unknown rejection as an ordinary failure with no startup diagnosis (%j)', async (rejection) => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockRejectedValue(rejection)
    const session = await freshSessionModule()

    await session.initializeLaunchSession()

    expect(session.getSessionStatus()).toBe('failed')
    expect(session.getStartupFailure()).toBeNull()
  })

  it('carries closed startup warnings with a ready session and fails closed on malformed ones', async () => {
    ;(window as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {}
    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc', startupWarnings: ['OptionalOutputDeferred'] })
    const session = await freshSessionModule()
    await session.initializeLaunchSession()
    expect(session.getLaunchSession()?.startupWarnings).toEqual(['OptionalOutputDeferred'])

    invokeMock.mockResolvedValue({ baseUrl: 'http://127.0.0.1:5080', secret: 'abc', startupWarnings: ['EverythingIsFine'] })
    const malformed = await freshSessionModule()
    await malformed.initializeLaunchSession()
    expect(malformed.getSessionStatus()).toBe('failed')
    expect(malformed.getLaunchSession()).toBeNull()
  })
})
