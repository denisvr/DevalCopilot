import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { executionCapacityClient } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { deriveExecutionCapacityStartGate } from '../executionCapacity'
import { availableCapacityWire, capacityWireFor } from '../executionCapacityFixture'
import {
  EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES,
  EXECUTION_CAPACITY_REFRESH_INTERVAL_MS,
  EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS,
  useExecutionCapacity,
} from './useExecutionCapacity'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  executionCapacityClient: vi.fn(),
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

type Read = () => Promise<unknown>

function install(read: Read) {
  const readExecutionCapacity = vi.fn(read)
  vi.mocked(executionCapacityClient).mockReturnValue({ readExecutionCapacity } as unknown as ReturnType<typeof executionCapacityClient>)
  return readExecutionCapacity
}

const mounted = (enabled: boolean, hint?: number) =>
  renderHook((props: { enabled: boolean; hint?: number }) => useExecutionCapacity(props.enabled, props.hint), {
    initialProps: { enabled, hint },
  })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))
const allowsStart = (result: { current: ReturnType<typeof useExecutionCapacity> }) =>
  deriveExecutionCapacityStartGate(result.current).allowsMutatingStart

describe('useExecutionCapacity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads once on mount, withholds while the first read is pending, then exposes the validated Available', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)

    const { result } = mounted(true)

    expect(read).toHaveBeenCalledTimes(1)
    expect(result.current).toMatchObject({ loading: true, reading: null, failure: null })
    expect(allowsStart(result)).toBe(false)

    await act(async () => {
      pending.resolve(availableCapacityWire())
      await pending.promise
    })

    expect(result.current).toMatchObject({ loading: false, failure: null, polling: true })
    expect(result.current.reading).toEqual(availableCapacityWire())
    expect(allowsStart(result)).toBe(true)
  })

  it('reads nothing while the session is not ready', () => {
    const read = install(() => Promise.resolve(availableCapacityWire()))

    const { result } = mounted(false)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ reading: null, loading: false, polling: false, failure: null })
    expect(allowsStart(result)).toBe(false)
  })

  it.each([
    ['AtCapacity', 2],
    ['AtCapacity', 5],
    ['Unavailable', undefined],
  ] as const)('exposes a validated %s reading, which withholds', async (status, owners) => {
    install(() => Promise.resolve(capacityWireFor(status, {}, owners)))

    const { result } = mounted(true)
    await flush()

    expect(result.current.reading?.status).toBe(status)
    expect(allowsStart(result)).toBe(false)
  })

  it.each([
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a refused session', () => Promise.reject(new ApiException('unauthorized', 401, '{}', {}, null)), 'read'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Available' }), 'invalid'],
    ['an answer with an unknown member', () => Promise.resolve({ ...availableCapacityWire(), extra: 1 }), 'invalid'],
    ['an invalid tuple', () => Promise.resolve(capacityWireFor('Available', { reasonCode: 'execution_capacity_unavailable' })), 'invalid'],
    ['a contradictory count', () => Promise.resolve(capacityWireFor('Available', { recordedOwners: 2 })), 'invalid'],
  ] as const)('exposes %s as unavailable with no reading, never a partial answer, and withholds', async (_, respond, failure) => {
    install(respond)

    const { result } = mounted(true)
    await flush()

    expect(result.current).toMatchObject({ reading: null, failure, loading: false })
    expect(allowsStart(result)).toBe(false)
  })

  it('throws no error when the client cannot even be built: it is a failed read', async () => {
    vi.mocked(executionCapacityClient).mockImplementation(() => {
      throw new Error('no launch session')
    })

    const { result } = mounted(true)
    await flush()

    expect(result.current).toMatchObject({ reading: null, failure: 'read', loading: false })
  })

  it('abandons a request that does not answer within ten seconds and masks an earlier Available with it', async () => {
    const hung = deferred<unknown>()
    const bodies = [Promise.resolve(availableCapacityWire()), hung.promise]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(true)
    await flush()
    expect(allowsStart(result)).toBe(true)

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(2)
    // The pending re-read keeps the last validated reading as a hint for as long as the bound allows.
    expect(allowsStart(result)).toBe(true)

    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS - 1)
    expect(allowsStart(result)).toBe(true)
    await tick(1)

    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(allowsStart(result)).toBe(false)

    // A late answer of the abandoned read neither revives the Available nor changes the failure.
    await act(async () => {
      hung.resolve(availableCapacityWire())
      await hung.promise
    })
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(allowsStart(result)).toBe(false)
  })

  it('masks an earlier Available when the re-read fails or is incoherent', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(availableCapacityWire()), second.promise, Promise.resolve({ ...availableCapacityWire(), extra: 1 })]
    install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(true)
    await flush()
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(allowsStart(result)).toBe(true)

    await act(async () => {
      second.reject(new Error('lost'))
      await second.promise.catch(() => undefined)
    })
    expect(result.current).toMatchObject({ reading: null, failure: 'read' })
    expect(allowsStart(result)).toBe(false)

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(result.current).toMatchObject({ reading: null, failure: 'invalid' })
    expect(allowsStart(result)).toBe(false)
  })

  it('reads again every five seconds while mounted, one read at a time, and never before the previous one settled', async () => {
    const second = deferred<unknown>()
    const bodies = [Promise.resolve(availableCapacityWire()), second.promise, Promise.resolve(capacityWireFor('AtCapacity'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    mounted(true)
    await flush()
    expect(read).toHaveBeenCalledTimes(1)

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS - 1)
    expect(read).toHaveBeenCalledTimes(1)
    await tick(1)
    expect(read).toHaveBeenCalledTimes(2)

    // No timer runs while a read is pending: the next one is scheduled from the settled read.
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 3)
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      second.resolve(availableCapacityWire())
      await second.promise
    })
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('keeps reading every five seconds for as long as it stays mounted, whatever the status: nothing is terminal', async () => {
    const read = install(() => Promise.resolve(capacityWireFor('AtCapacity')))
    const { result } = mounted(true)
    await flush()

    for (let step = 0; step < 10; step++) {
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    }

    expect(read).toHaveBeenCalledTimes(11)
    expect(result.current.polling).toBe(true)
  })

  it('exposes the change a later read found, and recovers from AtCapacity without any release command', async () => {
    const bodies = [
      Promise.resolve(availableCapacityWire(1)),
      Promise.resolve(capacityWireFor('AtCapacity')),
      Promise.resolve(availableCapacityWire(1)),
    ]
    install(() => bodies.shift() as Promise<unknown>)
    const { result } = mounted(true)
    await flush()
    expect(result.current.reading?.status).toBe('Available')

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(result.current.reading?.status).toBe('AtCapacity')
    expect(allowsStart(result)).toBe(false)

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(result.current.reading?.status).toBe('Available')
    expect(allowsStart(result)).toBe(true)
  })

  it('coalesces every trigger that arrives while a read is pending into exactly one further read', async () => {
    const first = deferred<unknown>()
    const bodies = [first.promise, Promise.resolve(capacityWireFor('AtCapacity'))]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result, rerender } = mounted(true, 1)
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ enabled: true, hint: 2 })
    rerender({ enabled: true, hint: 3 })
    act(() => result.current.refresh())
    act(() => result.current.refresh())
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      first.resolve(availableCapacityWire())
      await first.promise
    })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.reading?.status).toBe('AtCapacity')
  })

  it('reads again on a changed hint (a durable run or evidence refresh generation)', async () => {
    const read = install(() => Promise.resolve(availableCapacityWire()))
    const { rerender } = mounted(true, 0)
    await flush()
    expect(read).toHaveBeenCalledTimes(1)

    rerender({ enabled: true, hint: 1 })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
  })

  describe('after repeated failures', () => {
    async function failedThreeTimes() {
      const read = install(() => Promise.reject(new Error('down')))
      const mount = mounted(true, 0)
      await flush()
      for (let failure = 1; failure < EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES; failure++) {
        await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      }

      expect(read).toHaveBeenCalledTimes(EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES)
      expect(mount.result.current.polling).toBe(false)
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 4)
      expect(read).toHaveBeenCalledTimes(EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES)
      return { read, ...mount }
    }

    it('stops polling after three consecutive failures and reads again on an explicit refresh', async () => {
      const { read, result } = await failedThreeTimes()

      act(() => result.current.refresh())
      await flush()

      expect(read).toHaveBeenCalledTimes(EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES + 1)
    })

    it('reads again on a durable hint', async () => {
      const { read, rerender } = await failedThreeTimes()

      rerender({ enabled: true, hint: 1 })
      await flush()

      expect(read).toHaveBeenCalledTimes(EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES + 1)
    })

    it('reads again on a remount, with polling restored once a read succeeds', async () => {
      const { read, unmount } = await failedThreeTimes()
      unmount()
      read.mockImplementation(() => Promise.resolve(availableCapacityWire()))

      const again = mounted(true)
      await flush()

      expect(read).toHaveBeenCalledTimes(EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES + 1)
      expect(again.result.current).toMatchObject({ polling: true })
      expect(again.result.current.reading?.status).toBe('Available')
    })

    it('counts a refused or timed-out answer like a failed one, and a validated reading resets the count', async () => {
      const bodies: Read[] = [
        () => Promise.resolve({ nonsense: true }),
        () => Promise.reject(new Error('down')),
        () => Promise.resolve(availableCapacityWire()),
        () => Promise.resolve({ nonsense: true }),
        () => Promise.reject(new Error('down')),
        () => Promise.resolve({ nonsense: true }),
        () => Promise.resolve(availableCapacityWire()),
      ]
      const read = install(() => (bodies.shift() as Read)())
      const { result } = mounted(true)
      await flush()
      for (let step = 0; step < 5; step++) {
        await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      }

      // The success after two failures reset the count, so three more failures were needed to stop.
      expect(read).toHaveBeenCalledTimes(6)
      expect(result.current.polling).toBe(false)
    })
  })

  describe('the host session lifetime, remount and stale callbacks', () => {
    it('exposes a remounted host as a fresh loading lifetime, not the earlier Available', async () => {
      const bodies = [Promise.resolve(availableCapacityWire()), new Promise<unknown>(() => undefined)]
      install(() => bodies.shift() as Promise<unknown>)
      const first = mounted(true)
      await flush()
      expect(first.result.current.reading?.status).toBe('Available')
      first.unmount()

      const second = mounted(true)

      expect(second.result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(second.result)).toBe(false)
    })

    it('does not carry an Available across a session that stopped being ready and became ready again', async () => {
      const again = deferred<unknown>()
      const bodies = [Promise.resolve(availableCapacityWire()), again.promise]
      install(() => bodies.shift() as Promise<unknown>)
      const { result, rerender } = mounted(true)
      await flush()
      expect(allowsStart(result)).toBe(true)

      rerender({ enabled: false, hint: undefined })
      expect(result.current).toMatchObject({ reading: null, loading: false, polling: false })
      expect(allowsStart(result)).toBe(false)

      rerender({ enabled: true, hint: undefined })
      expect(result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(result)).toBe(false)

      await act(async () => {
        again.resolve(capacityWireFor('AtCapacity'))
        await again.promise
      })
      expect(result.current.reading?.status).toBe('AtCapacity')
    })

    it('does not let the response of an ended lifetime settle the next one', async () => {
      const stale = deferred<unknown>()
      const fresh = deferred<unknown>()
      const bodies = [stale.promise, fresh.promise]
      install(() => bodies.shift() as Promise<unknown>)
      const { result, rerender } = mounted(true)

      rerender({ enabled: false, hint: undefined })
      rerender({ enabled: true, hint: undefined })
      await act(async () => {
        stale.resolve(availableCapacityWire())
        await stale.promise
      })

      expect(result.current).toMatchObject({ loading: true, reading: null })
      expect(allowsStart(result)).toBe(false)

      await act(async () => {
        fresh.resolve(capacityWireFor('Unavailable'))
        await fresh.promise
      })
      expect(result.current.reading?.status).toBe('Unavailable')
    })

    it('starts no request from a refresh retained by an ended or unmounted lifetime', async () => {
      const read = install(() => Promise.resolve(availableCapacityWire()))
      const { result, rerender, unmount } = mounted(true)
      await flush()
      const retained = result.current.refresh
      rerender({ enabled: false, hint: undefined })
      await flush()
      const callsAfterStop = read.mock.calls.length

      act(() => retained())
      await flush()
      expect(read).toHaveBeenCalledTimes(callsAfterStop)

      const lastRefresh = result.current.refresh
      unmount()
      act(() => lastRefresh())
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 3)
      expect(read).toHaveBeenCalledTimes(callsAfterStop)
    })

    it('stops every timer on unmount and ignores an answer that arrives after it', async () => {
      const pending = deferred<unknown>()
      const read = install(() => pending.promise)
      const { unmount } = mounted(true)
      expect(vi.getTimerCount()).toBeGreaterThan(0)

      unmount()
      expect(vi.getTimerCount()).toBe(0)
      await act(async () => {
        pending.resolve(availableCapacityWire())
        await pending.promise
      })
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 3)

      expect(read).toHaveBeenCalledTimes(1)
    })
  })

  it('stores nothing in the browser', async () => {
    const localSet = vi.spyOn(Storage.prototype, 'setItem')
    install(() => Promise.resolve(availableCapacityWire()))
    mounted(true)
    await flush()
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 2)

    expect(localSet).not.toHaveBeenCalled()
    expect(window.localStorage.length).toBe(0)
    expect(window.sessionStorage.length).toBe(0)
    localSet.mockRestore()
  })
})
