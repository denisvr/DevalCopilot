import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestChallengeResolutionRepairAttemptResponse } from '../../../api/generated/api-client'
import { requestChallengeResolutionRepairAttemptClient } from '../../../api/clients'
import { useRequestChallengeResolutionRepairAttempt } from './useRequestChallengeResolutionRepairAttempt'

vi.mock('../../../api/clients', () => ({
  requestChallengeResolutionRepairAttemptClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

function mockClient(requestChallengeResolutionRepairAttempt: ReturnType<typeof vi.fn>) {
  vi.mocked(requestChallengeResolutionRepairAttemptClient).mockReturnValue({
    requestChallengeResolutionRepairAttempt,
  } as unknown as ReturnType<typeof requestChallengeResolutionRepairAttemptClient>)
}

const REPAIR_RESPONSE = new RequestChallengeResolutionRepairAttemptResponse({
  attemptId: 'attempt-4',
  attemptNumber: 4,
  repairSourceAttemptId: 'attempt-3',
})

describe('useRequestChallengeResolutionRepairAttempt', () => {
  it('requests the repair for the given run and source, then notifies onRequested', async () => {
    const requestRepair = vi.fn().mockResolvedValue(REPAIR_RESPONSE)
    mockClient(requestRepair)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('attempt-3')
    })

    expect(requestRepair).toHaveBeenCalledExactlyOnceWith('run-1', 'attempt-3')
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
    expect(result.current.requesting).toBe(false)
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestChallengeResolutionRepairAttemptResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))

    const { result } = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', vi.fn()))
    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('attempt-3')
    })
    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(REPAIR_RESPONSE)
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('surfaces only the backend fixed conflict detail and does not refresh on failure', async () => {
    const conflict = new ApiException(
      'Conflict',
      409,
      JSON.stringify({ errors: [{ detail: 'A repair was already requested for this attempt.' }] }),
      {},
      null,
    )
    mockClient(vi.fn().mockRejectedValue(conflict))
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('attempt-3')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('A repair was already requested for this attempt.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception', async () => {
    mockClient(vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')))

    const { result } = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', vi.fn()))

    await act(async () => {
      await result.current.request('attempt-3')
    })

    expect(result.current.error).toBe('A challenge resolution repair attempt could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })

  it('never shows one run’s in-flight state or error on another run', async () => {
    mockClient(vi.fn().mockRejectedValue(new Error('boom')))

    const { result, rerender } = renderHook(({ runId }) => useRequestChallengeResolutionRepairAttempt(runId, vi.fn()), {
      initialProps: { runId: 'run-1' },
    })

    await act(async () => {
      await result.current.request('attempt-3')
    })
    expect(result.current.error).not.toBeNull()

    rerender({ runId: 'run-2' })

    expect(result.current.error).toBeNull()
    expect(result.current.requesting).toBe(false)
  })

  describe('request generations', () => {
    function controllable<T>() {
      let resolve!: (value: T) => void
      let reject!: (reason: unknown) => void
      const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
      })
      return { promise, resolve, reject }
    }

    it('keeps run B pending when run A’s older request completes after the switch', async () => {
      const a = controllable<typeof REPAIR_RESPONSE>()
      const b = controllable<typeof REPAIR_RESPONSE>()
      mockClient(vi.fn().mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise))
      const onRequested = vi.fn()

      const { result, rerender } = renderHook(({ runId }) => useRequestChallengeResolutionRepairAttempt(runId, onRequested), {
        initialProps: { runId: 'run-A' },
      })

      let aPromise!: Promise<boolean>
      act(() => {
        aPromise = result.current.request('attempt-3')
      })
      rerender({ runId: 'run-B' })
      let bPromise!: Promise<boolean>
      act(() => {
        bPromise = result.current.request('attempt-7')
      })
      await waitFor(() => expect(result.current.requesting).toBe(true))

      let aOutcome!: boolean
      await act(async () => {
        a.resolve(REPAIR_RESPONSE)
        aOutcome = await aPromise
      })

      expect(aOutcome).toBe(false)
      expect(result.current.requesting).toBe(true)
      expect(onRequested).not.toHaveBeenCalled()

      await act(async () => {
        b.resolve(REPAIR_RESPONSE)
        await bPromise
      })
      expect(result.current.requesting).toBe(false)
      expect(onRequested).toHaveBeenCalledTimes(1)
    })

    it('does not resurrect run A’s error after A to B to A, nor surface a late failure', async () => {
      const late = controllable<typeof REPAIR_RESPONSE>()
      mockClient(vi.fn().mockRejectedValueOnce(new Error('boom')).mockReturnValueOnce(late.promise))

      const { result, rerender } = renderHook(({ runId }) => useRequestChallengeResolutionRepairAttempt(runId, vi.fn()), {
        initialProps: { runId: 'run-A' },
      })
      await act(async () => {
        await result.current.request('attempt-3')
      })
      expect(result.current.error).not.toBeNull()

      rerender({ runId: 'run-B' })
      rerender({ runId: 'run-A' })
      expect(result.current.error).toBeNull()
      expect(result.current.requesting).toBe(false)

      // A request started on A, then the run switches away and back before it fails.
      let pending!: Promise<boolean>
      act(() => {
        pending = result.current.request('attempt-3')
      })
      rerender({ runId: 'run-B' })
      rerender({ runId: 'run-A' })
      await act(async () => {
        late.reject(new Error('late'))
        await pending
      })
      expect(result.current.error).toBeNull()
      expect(result.current.requesting).toBe(false)
    })

    it('ignores a second submission while one is in flight and ignores a completion after a remount', async () => {
      const first = controllable<typeof REPAIR_RESPONSE>()
      const op = vi.fn().mockReturnValueOnce(first.promise)
      mockClient(op)
      const onRequested = vi.fn()

      const { result, unmount } = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', onRequested))
      let firstPromise!: Promise<boolean>
      act(() => {
        firstPromise = result.current.request('attempt-3')
      })
      await act(async () => expect(await result.current.request('attempt-3')).toBe(false))
      expect(op).toHaveBeenCalledTimes(1)
      expect(result.current.requesting).toBe(true)

      unmount()
      const remounted = renderHook(() => useRequestChallengeResolutionRepairAttempt('run-1', onRequested))
      await act(async () => {
        first.resolve(REPAIR_RESPONSE)
        expect(await firstPromise).toBe(false)
      })
      expect(remounted.result.current.requesting).toBe(false)
      expect(onRequested).not.toHaveBeenCalled()
    })
  })
})
