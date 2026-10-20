import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { workspaceExecutionStatusClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { deriveWorkspaceExecutionStartGate } from '../workspaceExecution'
import { OTHER_PROJECT_ID, PROJECT_ID, idleWire, wireFor } from '../workspaceExecutionFixture'
import {
  WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES,
  WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS,
  WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS,
  useWorkspaceExecutionStatus,
} from './useWorkspaceExecutionStatus'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  workspaceExecutionStatusClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

type Read = (projectId: string) => Promise<unknown>

function install(read: Read) {
  const readWorkspaceExecutionStatus = vi.fn(read)
  vi.mocked(workspaceExecutionStatusClient).mockReturnValue({ readWorkspaceExecutionStatus } as unknown as ReturnType<typeof workspaceExecutionStatusClient>)
  return readWorkspaceExecutionStatus
}

const mounted = (projectId: string | null, hint?: number) =>
  renderHook((props: { projectId: string | null; hint?: number }) => useWorkspaceExecutionStatus(props.projectId, props.hint), {
    initialProps: { projectId, hint },
  })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))
const allowsStart = (result: { current: ReturnType<typeof useWorkspaceExecutionStatus> }, projectId = PROJECT_ID) =>
  deriveWorkspaceExecutionStartGate(result.current, projectId).allowsExecutionStart

describe('useWorkspaceExecutionStatus', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads once when the project is selected, withholds while the first read is pending, then exposes the validated Idle', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)

    const { result } = mounted(PROJECT_ID)

    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith(PROJECT_ID)
    expect(result.current).toMatchObject({ loading: true, reading: null, failure: null })
    expect(allowsStart(result)).toBe(false)

    await act(async () => {
      pending.resolve(idleWire())
      await pending.promise
    })

    expect(result.current).toMatchObject({ loading: false, failure: null, polling: true })
    expect(result.current.reading?.status).toBe('Idle')
    expect(allowsStart(result)).toBe(true)
  })

  it('reads nothing without a project', () => {
    const read = install(() => Promise.resolve(idleWire()))

    const { result } = mounted(null)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ reading: null, loading: false, polling: false, failure: null })
    expect(allowsStart(result)).toBe(false)
  })

  it.each([
    ['AgentAttemptActive'],
    ['VerificationActive'],
    ['CommitReserved'],
    ['Conflicted'],
    ['Unavailable'],
    ['NoWorkspace'],
  ] as const)('exposes a validated %s reading, which withholds', async (status) => {
    install(() => Promise.resolve(wireFor(status)))

    const { result } = mounted(PROJECT_ID)
    await flush()

    expect(result.current.reading?.status).toBe(status)
    expect(allowsStart(result)).toBe(false)
  })

  it.each([
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(new ApiProblemDetails({ status: 404, errors: [], traceId: 't' })), 'not-found'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Idle' }), 'invalid'],
    ["another project's answer", () => Promise.resolve(idleWire(OTHER_PROJECT_ID)), 'invalid'],
    ['an answer with an unknown member', () => Promise.resolve({ ...idleWire(), extra: 1 }), 'invalid'],
    ['an invalid tuple', () => Promise.resolve(wireFor('Idle', PROJECT_ID, { reasonCode: 'agent_attempt_active' })), 'invalid'],
  ] as const)('exposes %s as unavailable with no reading, never a partial answer, and withholds', async (_, respond, failure) => {
    install(respond)

    const { result } = mounted(PROJECT_ID)
    await flush()

    expect(result.current).toMatchObject({ reading: null, failure, loading: false })
    expect(allowsStart(result)).toBe(false)
  })

  it('throws no error when the client cannot even be built: it is a failed read', async () => {
    vi.mocked(workspaceExecutionStatusClient).mockImplementation(() => {
      throw new Error('no launch session')
    })

    const { result } = mounted(PROJECT_ID)
    await flush()

    expect(result.current).toMatchObject({ reading: null, failure: 'read', loading: false })
  })

  it('abandons a request that does not answer within ten seconds and masks an earlier Idle with it', async () => {
    const hung = deferred<unknown>()
    const bodies = [Promise.resolve(idleWire()), hung.promise]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(PROJECT_ID)
    await flush()
    expect(allowsStart(result)).toBe(true)

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(2)
    // The pending re-read keeps the last validated reading for as long as the bound allows.
    expect(allowsStart(result)).toBe(true)

    await tick(WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS - 1)
    expect(allowsStart(result)).toBe(true)
    await tick(1)

    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(allowsStart(result)).toBe(false)

    // A late answer of the abandoned read neither revives the Idle nor changes the failure.
    await act(async () => {
      hung.resolve(idleWire())
      await hung.promise
    })
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(allowsStart(result)).toBe(false)
  })

  it('masks an earlier Idle when the re-read fails or is incoherent', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(idleWire()), second.promise, Promise.resolve({ ...idleWire(), extra: 1 })]
    install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(PROJECT_ID)
    await flush()
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(allowsStart(result)).toBe(true)

    await act(async () => {
      second.reject(new Error('lost'))
      await second.promise.catch(() => undefined)
    })
    expect(result.current).toMatchObject({ reading: null, failure: 'read' })
    expect(allowsStart(result)).toBe(false)

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(result.current).toMatchObject({ reading: null, failure: 'invalid' })
    expect(allowsStart(result)).toBe(false)
  })

  it('reads again every five seconds while the project is mounted, one read at a time, and never before the previous one settled', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(idleWire()), second.promise, Promise.resolve(wireFor('VerificationActive'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    mounted(PROJECT_ID)
    await flush()
    expect(read).toHaveBeenCalledTimes(1)

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS - 1)
    expect(read).toHaveBeenCalledTimes(1)
    await tick(1)
    expect(read).toHaveBeenCalledTimes(2)

    // No timer runs while a read is pending: the next one is scheduled from the settled read.
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 3)
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      second.resolve(idleWire())
      await second.promise
    })
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('keeps reading every five seconds for as long as it stays mounted, whatever the status: nothing is terminal', async () => {
    const read = install(() => Promise.resolve(idleWire()))
    const { result } = mounted(PROJECT_ID)
    await flush()

    for (let step = 0; step < 10; step++) {
      await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    }

    expect(read).toHaveBeenCalledTimes(11)
    expect(result.current.polling).toBe(true)
  })

  it('exposes the change of owner a later read found', async () => {
    const bodies = [Promise.resolve(idleWire()), Promise.resolve(wireFor('VerificationActive')), Promise.resolve(idleWire())]
    install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(PROJECT_ID)
    await flush()
    expect(result.current.reading?.status).toBe('Idle')

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(result.current.reading?.status).toBe('VerificationActive')
    expect(allowsStart(result)).toBe(false)

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(result.current.reading?.status).toBe('Idle')
    expect(allowsStart(result)).toBe(true)
  })

  it('coalesces every trigger that arrives while a read is pending into exactly one further read', async () => {
    const first = deferred<unknown>()
    const bodies = [first.promise, Promise.resolve(wireFor('AgentAttemptActive'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result, rerender } = mounted(PROJECT_ID, 1)
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ projectId: PROJECT_ID, hint: 2 })
    rerender({ projectId: PROJECT_ID, hint: 3 })
    act(() => result.current.refresh())
    act(() => result.current.refresh())
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      first.resolve(idleWire())
      await first.promise
    })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.reading?.status).toBe('AgentAttemptActive')
  })

  it('reads again on a changed hint (the evidence refresh generation)', async () => {
    const read = install(() => Promise.resolve(idleWire()))
    const { rerender } = mounted(PROJECT_ID, 0)
    await flush()
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ projectId: PROJECT_ID, hint: 1 })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
  })

  describe('after repeated failures', () => {
    async function failedThreeTimes() {
      const read = install(() => Promise.reject(new Error('down')))
      const mount = mounted(PROJECT_ID, 0)
      await flush()
      for (let failure = 1; failure < WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES; failure++) {
        await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
      }

      expect(read).toHaveBeenCalledTimes(WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES)
      expect(mount.result.current.polling).toBe(false)
      await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 4)
      expect(read).toHaveBeenCalledTimes(WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES)
      return { read, ...mount }
    }

    it('stops polling after three consecutive failures and reads again on an explicit refresh', async () => {
      const { read, result } = await failedThreeTimes()

      act(() => result.current.refresh())
      await flush()

      expect(read).toHaveBeenCalledTimes(WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES + 1)
    })

    it('reads again on a durable hint', async () => {
      const { read, rerender } = await failedThreeTimes()

      rerender({ projectId: PROJECT_ID, hint: 1 })
      await flush()

      expect(read).toHaveBeenCalledTimes(WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES + 1)
    })

    it('reads again on a remount, with polling restored once a read succeeds', async () => {
      const { read, unmount } = await failedThreeTimes()
      unmount()
      read.mockImplementation(() => Promise.resolve(idleWire()))

      const again = mounted(PROJECT_ID)
      await flush()

      expect(read).toHaveBeenCalledTimes(WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES + 1)
      expect(again.result.current).toMatchObject({ polling: true })
      expect(again.result.current.reading?.status).toBe('Idle')
    })

    it('counts a refused or timed-out answer like a failed one, and a validated reading resets the count', async () => {
      const bodies: (() => Promise<unknown>)[] = [
        () => Promise.resolve({ nonsense: true }),
        () => Promise.reject(new Error('down')),
        () => Promise.resolve(idleWire()),
        () => Promise.resolve({ nonsense: true }),
        () => Promise.reject(new Error('down')),
        () => Promise.resolve({ nonsense: true }),
        () => Promise.resolve(idleWire()),
      ]
      const read = install(() => (bodies.shift() as () => Promise<unknown>)())
      const { result } = mounted(PROJECT_ID)
      await flush()
      for (let step = 0; step < 5; step++) {
        await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
      }

      // The success after two failures reset the count, so three more failures were needed to stop.
      expect(read).toHaveBeenCalledTimes(6)
      expect(result.current.polling).toBe(false)
    })
  })

  describe('selection, remount and stale callbacks', () => {
    it('never exposes the previous project, and a response of an obsolete selection cannot settle the new one (A-B-A)', async () => {
      const firstA = deferred<unknown>()
      const b = deferred<unknown>()
      const secondA = deferred<unknown>()
      const bodies = { [PROJECT_ID]: [firstA.promise, secondA.promise], [OTHER_PROJECT_ID]: [b.promise] }
      install((projectId) => bodies[projectId as keyof typeof bodies].shift() as Promise<unknown>)
      const { result, rerender } = mounted(PROJECT_ID)

      rerender({ projectId: OTHER_PROJECT_ID, hint: undefined })
      expect(result.current).toMatchObject({ loading: true, reading: null })
      rerender({ projectId: PROJECT_ID, hint: undefined })
      expect(result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(result)).toBe(false)

      // The first visit to project A and the visit to project B answer late: neither may settle the current lifetime.
      await act(async () => {
        firstA.resolve(idleWire())
        b.resolve(idleWire(OTHER_PROJECT_ID))
        await Promise.all([firstA.promise, b.promise])
      })
      expect(result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(result)).toBe(false)

      await act(async () => {
        secondA.resolve(wireFor('AgentAttemptActive'))
        await secondA.promise
      })
      expect(result.current.reading).toMatchObject({ projectId: PROJECT_ID, status: 'AgentAttemptActive' })
    })

    it("does not carry an Idle across projects: the other project's first read is pending and withholds", async () => {
      const b = deferred<unknown>()
      const bodies = { [PROJECT_ID]: [Promise.resolve(idleWire())], [OTHER_PROJECT_ID]: [b.promise] }
      install((projectId) => bodies[projectId as keyof typeof bodies].shift() as Promise<unknown>)
      const { result, rerender } = mounted(PROJECT_ID)
      await flush()
      expect(allowsStart(result)).toBe(true)

      rerender({ projectId: OTHER_PROJECT_ID, hint: undefined })

      expect(result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(result, OTHER_PROJECT_ID)).toBe(false)
    })

    it('starts no request from a refresh retained by a replaced or unmounted lifetime', async () => {
      const read = install(() => Promise.resolve(idleWire()))
      const { result, rerender, unmount } = mounted(PROJECT_ID)
      await flush()
      const retained = result.current.refresh
      rerender({ projectId: OTHER_PROJECT_ID, hint: undefined })
      await flush()
      const callsAfterSwitch = read.mock.calls.length

      act(() => retained())
      await flush()
      expect(read).toHaveBeenCalledTimes(callsAfterSwitch)

      const lastRefresh = result.current.refresh
      unmount()
      act(() => lastRefresh())
      await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 3)
      expect(read).toHaveBeenCalledTimes(callsAfterSwitch)
    })

    it('stops every timer on unmount and ignores an answer that arrives after it', async () => {
      const pending = deferred<unknown>()
      const read = install(() => pending.promise)
      const { unmount } = mounted(PROJECT_ID)
      expect(vi.getTimerCount()).toBeGreaterThan(0)

      unmount()
      expect(vi.getTimerCount()).toBe(0)
      await act(async () => {
        pending.resolve(idleWire())
        await pending.promise
      })
      await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 3)

      expect(read).toHaveBeenCalledTimes(1)
    })

    it('exposes a remounted project as a fresh loading lifetime, not the earlier Idle', async () => {
      const bodies = [Promise.resolve(idleWire()), new Promise<unknown>(() => undefined)]
      install(() => bodies.shift() as Promise<unknown>)
      const first = mounted(PROJECT_ID)
      await flush()
      expect(first.result.current.reading?.status).toBe('Idle')
      first.unmount()

      const second = mounted(PROJECT_ID)

      expect(second.result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(second.result)).toBe(false)
    })
  })

  it('stores nothing in the browser', async () => {
    const localSet = vi.spyOn(Storage.prototype, 'setItem')
    install(() => Promise.resolve(idleWire()))
    mounted(PROJECT_ID)
    await flush()
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 2)

    expect(localSet).not.toHaveBeenCalled()
    expect(window.localStorage.length).toBe(0)
    expect(window.sessionStorage.length).toBe(0)
    localSet.mockRestore()
  })
})
