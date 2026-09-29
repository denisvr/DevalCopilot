import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestCodexPlanningRepairAttemptResponse } from '../../../api/generated/api-client'
import { requestCodexPlanningRepairAttemptClient } from '../../../api/clients'
import { useRequestCodexPlanningRepairAttempt } from './useRequestCodexPlanningRepairAttempt'

vi.mock('../../../api/clients', () => ({
  requestCodexPlanningRepairAttemptClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

function mockClient(requestCodexPlanningRepairAttempt: ReturnType<typeof vi.fn>) {
  vi.mocked(requestCodexPlanningRepairAttemptClient).mockReturnValue({
    requestCodexPlanningRepairAttempt,
  } as unknown as ReturnType<typeof requestCodexPlanningRepairAttemptClient>)
}

const REPAIR_RESPONSE = new RequestCodexPlanningRepairAttemptResponse({
  attemptId: 'attempt-4',
  attemptNumber: 4,
  repairSourceAttemptId: 'attempt-3',
})

describe('useRequestCodexPlanningRepairAttempt', () => {
  it('requests the repair for the given run and source, then notifies onRequested', async () => {
    const requestRepair = vi.fn().mockResolvedValue(REPAIR_RESPONSE)
    mockClient(requestRepair)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestCodexPlanningRepairAttempt('run-1', onRequested))

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
    const pending = deferred<RequestCodexPlanningRepairAttemptResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))

    const { result } = renderHook(() => useRequestCodexPlanningRepairAttempt('run-1', vi.fn()))
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

    const { result } = renderHook(() => useRequestCodexPlanningRepairAttempt('run-1', onRequested))

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

    const { result } = renderHook(() => useRequestCodexPlanningRepairAttempt('run-1', vi.fn()))

    await act(async () => {
      await result.current.request('attempt-3')
    })

    expect(result.current.error).toBe('A repair attempt could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })

  it('never shows one run’s in-flight state or error on another run', async () => {
    mockClient(vi.fn().mockRejectedValue(new Error('boom')))

    const { result, rerender } = renderHook(({ runId }) => useRequestCodexPlanningRepairAttempt(runId, vi.fn()), {
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
})
