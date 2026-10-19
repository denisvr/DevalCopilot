import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { agentSupervisionClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { endedWire, needsAttentionWire, noAgentAttemptWire, observedWire } from '../agentSupervisionFixture'
import {
  AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES,
  AGENT_SUPERVISION_REFRESH_INTERVAL_MS,
  AGENT_SUPERVISION_REQUEST_TIMEOUT_MS,
  useAgentSupervision,
} from './useAgentSupervision'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentSupervisionClient: vi.fn(),
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

type Read = (runId: string) => Promise<unknown>

function install(read: Read) {
  const readAgentSupervision = vi.fn(read)
  vi.mocked(agentSupervisionClient).mockReturnValue({ readAgentSupervision } as unknown as ReturnType<typeof agentSupervisionClient>)
  return readAgentSupervision
}

const mounted = (runId: string | null, latestEventSequence?: number) =>
  renderHook((props: { runId: string | null; sequence?: number }) => useAgentSupervision(props.runId, props.sequence), {
    initialProps: { runId, sequence: latestEventSequence },
  })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useAgentSupervision', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads once when the run is selected, shows loading until the answer settles, then exposes the validated reading and its age clock', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)

    const { result } = mounted('run-1')

    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1')
    expect(result.current).toMatchObject({ loading: true, reading: null, failure: null, receivedAtMilliseconds: null })

    await act(async () => {
      pending.resolve(observedWire('run-1'))
      await pending.promise
    })

    expect(result.current).toMatchObject({ loading: false, failure: null, polling: true })
    expect(result.current.reading?.status).toBe('Observed')
    expect(typeof result.current.receivedAtMilliseconds).toBe('number')
  })

  it('reads nothing without a run', () => {
    const read = install(() => Promise.resolve(observedWire('run-1')))

    const { result } = mounted(null)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ reading: null, loading: false, polling: false, failure: null })
  })

  it.each([
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(new ApiProblemDetails({ status: 404, errors: [], traceId: 't' })), 'not-found'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Observed' }), 'invalid'],
    ["another run's answer", () => Promise.resolve(observedWire('run-2')), 'invalid'],
    ['an answer with an unknown member', () => Promise.resolve({ ...observedWire('run-1'), extra: 1 }), 'invalid'],
    ['an incoherent relationship', () => Promise.resolve({ ...observedWire('run-1'), attention: { anything: true } }), 'invalid'],
  ] as const)('exposes %s as unavailable with no reading, never a partial answer', async (_, respond, failure) => {
    install(respond)

    const { result } = mounted('run-1')
    await flush()

    expect(result.current).toMatchObject({ reading: null, failure, loading: false, receivedAtMilliseconds: null })
  })

  it('abandons a request that does not answer within ten seconds and masks an earlier success with it', async () => {
    const first = Promise.resolve(observedWire('run-1'))
    const hung = deferred<unknown>()
    const bodies = [first, hung.promise]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted('run-1')
    await flush()
    expect(result.current.reading?.status).toBe('Observed')

    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(2)
    // The pending re-read keeps the last validated reading for as long as the bound allows.
    expect(result.current.reading?.status).toBe('Observed')

    await tick(AGENT_SUPERVISION_REQUEST_TIMEOUT_MS - 1)
    expect(result.current.reading?.status).toBe('Observed')
    await tick(1)

    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })

    // A late answer of the abandoned read neither revives the reading nor changes the failure.
    await act(async () => {
      hung.resolve(observedWire('run-1'))
      await hung.promise
    })
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
  })

  it('reads again every five seconds while the attempt is not terminal, one read at a time, and never before the previous one settled', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(observedWire('run-1')), second.promise, Promise.resolve(needsAttentionWire('run-1'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    mounted('run-1')
    await flush()
    expect(read).toHaveBeenCalledTimes(1)

    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS - 1)
    expect(read).toHaveBeenCalledTimes(1)
    await tick(1)
    expect(read).toHaveBeenCalledTimes(2)

    // No timer runs while a read is pending: the next one is scheduled from the settled read.
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 3)
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      second.resolve(observedWire('run-1'))
      await second.promise
    })
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(3)
  })

  it.each([
    ['an ended attempt', () => endedWire('run-1')],
    ['a run without an Agent attempt', () => noAgentAttemptWire('run-1')],
  ])('does not poll once the answer is %s, but reads again on a durable hint', async (_, body) => {
    const read = install(() => Promise.resolve(body()))
    const { result, rerender } = mounted('run-1', 1)
    await flush()
    expect(result.current.polling).toBe(false)

    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 4)
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ runId: 'run-1', sequence: 2 })
    await flush()
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('coalesces every trigger that arrives while a read is pending into exactly one further read', async () => {
    const first = deferred<unknown>()
    const bodies = [first.promise, Promise.resolve(endedWire('run-1'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result, rerender } = mounted('run-1', 1)
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ runId: 'run-1', sequence: 2 })
    rerender({ runId: 'run-1', sequence: 3 })
    act(() => result.current.refresh())
    act(() => result.current.refresh())
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      first.resolve(observedWire('run-1'))
      await first.promise
    })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.reading?.status).toBe('Ended')
  })

  it('stops polling after three consecutive failures and reads again on an explicit refresh', async () => {
    const read = install(() => Promise.reject(new Error('down')))
    const { result } = mounted('run-1')
    await flush()
    for (let failure = 1; failure < AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES; failure++) {
      await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    }

    expect(read).toHaveBeenCalledTimes(AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES)
    expect(result.current.polling).toBe(false)
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 4)
    expect(read).toHaveBeenCalledTimes(AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES)

    act(() => result.current.refresh())
    await flush()
    expect(read).toHaveBeenCalledTimes(AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES + 1)
  })

  it('keeps the last validated reading while a re-read is pending and masks it when the re-read fails', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(observedWire('run-1')), second.promise]
    install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted('run-1')
    await flush()
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    expect(result.current.reading?.status).toBe('Observed')

    await act(async () => {
      second.reject(new Error('lost'))
      await second.promise.catch(() => undefined)
    })

    expect(result.current).toMatchObject({ reading: null, failure: 'read', receivedAtMilliseconds: null })
  })

  describe('selection, remount and stale callbacks', () => {
    it('never exposes the previous run, and a response of an obsolete selection cannot settle the new one (A-B-A)', async () => {
      const firstA = deferred<unknown>()
      const b = deferred<unknown>()
      const secondA = deferred<unknown>()
      const bodies = { 'run-1': [firstA.promise, secondA.promise], 'run-2': [b.promise] }
      install((runId) => (bodies[runId as 'run-1' | 'run-2'].shift() as Promise<unknown>))
      const { result, rerender } = mounted('run-1')

      rerender({ runId: 'run-2', sequence: undefined })
      expect(result.current).toMatchObject({ loading: true, reading: null })
      rerender({ runId: 'run-1', sequence: undefined })
      expect(result.current).toMatchObject({ loading: true, reading: null })

      // The first visit to run-1 and the visit to run-2 answer late: neither may settle the current lifetime.
      await act(async () => {
        firstA.resolve(endedWire('run-1'))
        b.resolve(endedWire('run-2'))
        await Promise.all([firstA.promise, b.promise])
      })
      expect(result.current).toMatchObject({ loading: true, reading: null })

      await act(async () => {
        secondA.resolve(observedWire('run-1'))
        await secondA.promise
      })
      expect(result.current.reading).toMatchObject({ runId: 'run-1', status: 'Observed' })
    })

    it('starts no request from a refresh retained by a replaced or unmounted lifetime', async () => {
      const read = install(() => Promise.resolve(observedWire('run-1')))
      const { result, rerender, unmount } = mounted('run-1')
      await flush()
      const retained = result.current.refresh
      rerender({ runId: 'run-2', sequence: undefined })
      await flush()
      const callsAfterSwitch = read.mock.calls.length

      act(() => retained())
      await flush()
      expect(read).toHaveBeenCalledTimes(callsAfterSwitch)

      const lastRefresh = result.current.refresh
      unmount()
      act(() => lastRefresh())
      await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 3)
      expect(read).toHaveBeenCalledTimes(callsAfterSwitch)
    })

    it('stops every timer on unmount and ignores an answer that arrives after it', async () => {
      const pending = deferred<unknown>()
      const read = install(() => pending.promise)
      const { unmount } = mounted('run-1')
      expect(vi.getTimerCount()).toBeGreaterThan(0)

      unmount()
      expect(vi.getTimerCount()).toBe(0)
      await act(async () => {
        pending.resolve(observedWire('run-1'))
        await pending.promise
      })
      await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 3)

      expect(read).toHaveBeenCalledTimes(1)
    })

    it('exposes a remounted run as a fresh loading lifetime, not the earlier success', async () => {
      const bodies = [Promise.resolve(observedWire('run-1')), new Promise<unknown>(() => undefined)]
      install(() => bodies.shift() as Promise<unknown>)
      const first = mounted('run-1')
      await flush()
      expect(first.result.current.reading?.status).toBe('Observed')
      first.unmount()

      const second = mounted('run-1')

      expect(second.result.current).toMatchObject({ loading: true, reading: null })
    })
  })

  it('stores nothing in the browser and never reads a heartbeat from its own clock', async () => {
    const localSet = vi.spyOn(Storage.prototype, 'setItem')
    install(() => Promise.resolve(observedWire('run-1')))
    mounted('run-1')
    await flush()
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 2)

    expect(localSet).not.toHaveBeenCalled()
    localSet.mockRestore()
  })
})
