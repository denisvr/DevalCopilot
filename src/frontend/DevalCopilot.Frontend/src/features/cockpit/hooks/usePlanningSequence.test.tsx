import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { planningSequenceStatusClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { armableStatusFor, statusFor, unavailableStatusFor } from '../planningSequenceFixture'
import {
  PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES,
  PLANNING_SEQUENCE_POLL_INTERVAL_MS,
  usePlanningSequence,
} from './usePlanningSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  planningSequenceStatusClient: vi.fn(),
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
  const readPlanningSequence = vi.fn(read)
  vi.mocked(planningSequenceStatusClient).mockReturnValue({ readPlanningSequence } as unknown as ReturnType<typeof planningSequenceStatusClient>)
  return readPlanningSequence
}

const mounted = (runId: string | null, latestEventSequence?: number) =>
  renderHook((props: { runId: string | null; sequence?: number }) => usePlanningSequence(props.runId, props.sequence), {
    initialProps: { runId, sequence: latestEventSequence },
  })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('usePlanningSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads once when the run is selected, is unknown until the answer settles, and then exposes the validated status', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)

    const { result } = mounted('run-1')

    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1')
    expect(result.current).toMatchObject({ loading: true, status: null, control: 'unknown', ownership: 'unknown', failure: null })

    await act(async () => {
      pending.resolve(armableStatusFor('run-1'))
      await pending.promise
    })

    expect(result.current).toMatchObject({ loading: false, failure: null, control: 'none', ownership: 'known' })
    expect(result.current.status?.arming).toMatchObject({ available: true })
  })

  it('reads nothing without a run', () => {
    const read = install(() => Promise.resolve(armableStatusFor('run-1')))

    const { result } = mounted(null)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ status: null, loading: false, control: 'unknown', polling: false })
  })

  it.each([
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(new ApiProblemDetails({ status: 404, errors: [], traceId: 't' })), 'not-found'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Armed' }), 'invalid'],
    ['another run\'s answer', () => Promise.resolve(armableStatusFor('run-2')), 'invalid'],
    ['an answer with an unknown member', () => Promise.resolve({ ...armableStatusFor('run-1'), extra: 1 }), 'invalid'],
    ['an incoherent fence', () => Promise.resolve(statusFor('run-1', 'Armed', { manualFence: 'Released' })), 'invalid'],
    ['a fence the host could not establish', () => Promise.resolve(unavailableStatusFor('run-1')), 'unavailable'],
  ] as const)('exposes %s as unknown ownership with no status, never a partial answer', async (_, respond, failure) => {
    install(respond)

    const { result } = mounted('run-1')
    await flush()

    expect(result.current).toMatchObject({ status: null, failure, control: 'unknown', ownership: 'unknown', loading: false })
  })

  it('derives every control from the validated status only', async () => {
    const cases = [
      [statusFor('run-1', 'AwaitingStepResult'), 'blocked'],
      [statusFor('run-1', 'Stopping'), 'blocked'],
      [statusFor('run-1', 'Stopped'), 'released'],
      [statusFor('run-1', 'ReadyForImplementation'), 'released'],
      [armableStatusFor('run-1'), 'none'],
    ] as const
    for (const [body, control] of cases) {
      install(() => Promise.resolve(body))
      const { result, unmount } = mounted('run-1')
      await flush()
      expect(result.current.control).toBe(control)
      unmount()
    }
  })

  it('reads again when the latest event sequence changes, keeps the last validated answer while it is pending, and masks it when it fails', async () => {
    const second = deferred<unknown>()
    const third = deferred<unknown>()
    const bodies = [Promise.resolve(statusFor('run-1', 'Stopped')), second.promise, third.promise]
    const read = install(() => bodies.shift() as Promise<unknown>)
    const { result, rerender } = mounted('run-1', 1)
    await flush()
    expect(result.current.control).toBe('released')

    rerender({ runId: 'run-1', sequence: 2 })

    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current).toMatchObject({ control: 'released', loading: false })
    await act(async () => {
      second.reject(new Error('boom'))
      await second.promise.catch(() => undefined)
    })
    expect(result.current).toMatchObject({ status: null, control: 'unknown', failure: 'read' })

    rerender({ runId: 'run-1', sequence: 3 })
    await act(async () => {
      third.resolve(statusFor('run-1', 'Stopped'))
      await third.promise
    })
    expect(result.current).toMatchObject({ control: 'released', failure: null })
  })

  it('reads again on refresh, and ignores a refresh retained from a replaced or unmounted visit', async () => {
    const read = install(() => Promise.resolve(armableStatusFor('run-1')))
    const { result, rerender, unmount } = mounted('run-1')
    await flush()
    const retained = result.current.refresh

    act(() => result.current.refresh())
    expect(read).toHaveBeenCalledTimes(2)

    install(() => Promise.resolve(armableStatusFor('run-2')))
    rerender({ runId: 'run-2', sequence: undefined })
    await flush()
    const readsAfterSwitch = vi.mocked(planningSequenceStatusClient).mock.calls.length
    act(() => retained())
    expect(vi.mocked(planningSequenceStatusClient).mock.calls.length).toBe(readsAfterSwitch)

    const latest = result.current.refresh
    unmount()
    act(() => latest())
    expect(vi.mocked(planningSequenceStatusClient).mock.calls.length).toBe(readsAfterSwitch)
  })

  describe('bounded polling', () => {
    it.each(['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping'] as const)('reads every two seconds while the status is %s', async (state) => {
      const read = install(() => Promise.resolve(statusFor('run-1', state)))
      const { result } = mounted('run-1')
      await flush()
      expect(read).toHaveBeenCalledTimes(1)
      expect(result.current.polling).toBe(true)

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS - 1)
      expect(read).toHaveBeenCalledTimes(1)
      await tick(1)
      expect(read).toHaveBeenCalledTimes(2)
      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)
      expect(read).toHaveBeenCalledTimes(3)
    })

    it.each(['Stopped', 'ReadyForImplementation', 'AwaitingHumanDecision', 'NeedsAttention', 'Interrupted'] as const)(
      'never polls for the terminal status %s',
      async (state) => {
        const read = install(() => Promise.resolve(statusFor('run-1', state)))
        const { result } = mounted('run-1')
        await flush()

        await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 5)

        expect(read).toHaveBeenCalledTimes(1)
        expect(result.current.polling).toBe(false)
      },
    )

    it('never polls a run with no sequence', async () => {
      const read = install(() => Promise.resolve(armableStatusFor('run-1')))
      mounted('run-1')
      await flush()

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 5)

      expect(read).toHaveBeenCalledTimes(1)
    })

    it('keeps polling an elapsed deadline the host has not persisted yet, because its recorded state can still advance', async () => {
      const body = statusFor(
        'run-1',
        'NeedsAttention',
        {},
        {
          recordedState: 'PreparingStep',
          observedBeforePersisted: true,
          reasonCode: 'preparation_expired',
          reasonText: 'Expired.',
          steps: [(statusFor('run-1', 'PreparingStep').sequence as { steps: unknown[] }).steps[0]],
          endedAtUtc: null,
        },
      )
      const read = install(() => Promise.resolve(body))
      mounted('run-1')
      await flush()

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)

      expect(read).toHaveBeenCalledTimes(2)
    })

    it('stops as soon as the polled status reaches a terminal state', async () => {
      const bodies = [statusFor('run-1', 'AwaitingStepResult'), statusFor('run-1', 'Stopped')]
      const read = install(() => Promise.resolve(bodies.shift() ?? statusFor('run-1', 'Stopped')))
      const { result } = mounted('run-1')
      await flush()

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)
      expect(result.current.control).toBe('released')
      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 5)

      expect(read).toHaveBeenCalledTimes(2)
      expect(result.current.polling).toBe(false)
    })

    it('never overlaps reads: the next read is scheduled only after the previous one settled', async () => {
      const pending = deferred<unknown>()
      const read = install(() => pending.promise)
      mounted('run-1')
      await flush()

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 3)

      expect(read).toHaveBeenCalledTimes(1)
    })

    it('stops after the cap of consecutive failures and keeps the status unknown', async () => {
      const read = install(() => Promise.reject(new Error('boom')))
      const { result } = mounted('run-1')
      await flush()
      for (let attempt = 1; attempt < PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES; attempt += 1) {
        await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)
      }
      expect(read).toHaveBeenCalledTimes(PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES)

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 10)

      expect(read).toHaveBeenCalledTimes(PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES)
      expect(result.current).toMatchObject({ control: 'unknown', polling: false, failure: 'read' })
    })

    it('counts only consecutive failures: a successful read resets the cap, and a notification or refresh reads again after it', async () => {
      const outcomes = ['fail', 'fail', 'ok', 'fail', 'fail', 'fail', 'ok-after-refresh']
      const read = install(() => {
        const next = outcomes.shift()
        return next === 'fail' ? Promise.reject(new Error('boom')) : Promise.resolve(statusFor('run-1', 'AwaitingStepResult'))
      })
      const { result } = mounted('run-1')
      await flush()
      for (let step = 0; step < 5; step += 1) {
        await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)
      }
      expect(read).toHaveBeenCalledTimes(6)
      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 10)
      expect(read).toHaveBeenCalledTimes(6)
      expect(result.current.polling).toBe(false)

      act(() => result.current.refresh())
      await flush()

      expect(read).toHaveBeenCalledTimes(7)
      expect(result.current).toMatchObject({ control: 'blocked', polling: true })
    })

    it('stops reading when the visit ends, even with a read in flight and a poll scheduled', async () => {
      const read = install(() => Promise.resolve(statusFor('run-1', 'Armed')))
      const { unmount } = mounted('run-1')
      await flush()

      unmount()
      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 5)

      expect(read).toHaveBeenCalledTimes(1)
    })

    it('keeps one poll chain per visit: a replaced run never keeps polling, and its successor starts its own', async () => {
      const read = install((runId) => Promise.resolve(statusFor(runId, 'Armed')))
      const { rerender } = mounted('run-1')
      await flush()
      rerender({ runId: 'run-2', sequence: undefined })
      await flush()
      read.mockClear()

      await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS)

      expect(read.mock.calls.map(([runId]) => runId)).toEqual(['run-2'])
    })
  })

  describe('selection ownership', () => {
    it('never exposes the previous run, and a late answer of a replaced selection changes nothing (A, B, A)', async () => {
      const firstA = deferred<unknown>()
      const b = deferred<unknown>()
      const secondA = deferred<unknown>()
      const queue = [firstA, b, secondA]
      const read = install(() => (queue.shift() as ReturnType<typeof deferred<unknown>>).promise)
      const { result, rerender } = mounted('run-a')

      rerender({ runId: 'run-b', sequence: undefined })
      expect(result.current).toMatchObject({ loading: true, status: null, control: 'unknown' })
      rerender({ runId: 'run-a', sequence: undefined })
      expect(read).toHaveBeenCalledTimes(3)

      await act(async () => {
        firstA.resolve(statusFor('run-a', 'Stopped'))
        b.resolve(statusFor('run-b', 'Stopped'))
        await firstA.promise
      })
      // The first run A lifetime and run B are over: neither answer reaches the returned run A.
      expect(result.current).toMatchObject({ loading: true, status: null, control: 'unknown' })

      await act(async () => {
        secondA.resolve(armableStatusFor('run-a'))
        await secondA.promise
      })
      expect(result.current).toMatchObject({ loading: false, control: 'none' })
      expect(result.current.status?.runId).toBe('run-a')
    })

    it('never shows an answer for another run even if the host mixed it into a read of this one', async () => {
      install(() => Promise.resolve(statusFor('run-b', 'Stopped')))

      const { result } = mounted('run-a')
      await flush()

      expect(result.current).toMatchObject({ status: null, failure: 'invalid', control: 'unknown' })
    })

    it('starts loading again after a remount and never reuses what the previous mount read', async () => {
      const second = deferred<unknown>()
      const queue = [Promise.resolve(statusFor('run-1', 'Stopped')), second.promise]
      install(() => queue.shift() as Promise<unknown>)
      const first = mounted('run-1')
      await flush()
      expect(first.result.current.control).toBe('released')
      first.unmount()

      const remounted = mounted('run-1')

      expect(remounted.result.current).toMatchObject({ loading: true, status: null, control: 'unknown' })
      await act(async () => {
        second.resolve(armableStatusFor('run-1'))
        await second.promise
      })
      expect(remounted.result.current.control).toBe('none')
    })

    it('ignores an older overlapping read that settles after a newer one', async () => {
      const older = deferred<unknown>()
      const newer = deferred<unknown>()
      const queue = [older, newer]
      install(() => (queue.shift() as ReturnType<typeof deferred<unknown>>).promise)
      const { result, rerender } = mounted('run-1', 1)
      rerender({ runId: 'run-1', sequence: 2 })

      await act(async () => {
        newer.resolve(statusFor('run-1', 'Stopped'))
        await newer.promise
      })
      await act(async () => {
        older.resolve(statusFor('run-1', 'AwaitingStepResult'))
        await older.promise
      })

      expect(result.current).toMatchObject({ control: 'released' })
      expect(result.current.status?.status).toBe('Stopped')
    })
  })

  it('stores nothing in the browser', async () => {
    const local = vi.spyOn(Storage.prototype, 'setItem')
    install(() => Promise.resolve(statusFor('run-1', 'Armed')))

    mounted('run-1')
    await flush()
    await tick(PLANNING_SEQUENCE_POLL_INTERVAL_MS * 2)

    expect(local).not.toHaveBeenCalled()
    local.mockRestore()
  })
})
