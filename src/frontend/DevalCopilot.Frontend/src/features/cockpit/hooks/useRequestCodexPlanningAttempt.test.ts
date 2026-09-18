import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestCodexPlanningAttemptResponse } from '../../../api/generated/api-client'
import { requestCodexPlanningAttemptClient } from '../../../api/clients'
import { useRequestCodexPlanningAttempt } from './useRequestCodexPlanningAttempt'

vi.mock('../../../api/clients', () => ({
  requestCodexPlanningAttemptClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('useRequestCodexPlanningAttempt', () => {
  it('requests the attempt for the given run and notifies onRequested on success', async () => {
    const requestCodexPlanningAttempt = vi
      .fn()
      .mockResolvedValue(new RequestCodexPlanningAttemptResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
    vi.mocked(requestCodexPlanningAttemptClient).mockReturnValue({
      requestCodexPlanningAttempt,
    } as unknown as ReturnType<typeof requestCodexPlanningAttemptClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestCodexPlanningAttempt(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1')
    })

    expect(requestCodexPlanningAttempt).toHaveBeenCalledWith('run-1')
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestCodexPlanningAttemptResponse>()
    vi.mocked(requestCodexPlanningAttemptClient).mockReturnValue({
      requestCodexPlanningAttempt: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestCodexPlanningAttemptClient>)

    const { result } = renderHook(() => useRequestCodexPlanningAttempt(vi.fn()))

    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1')
    })

    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(new RequestCodexPlanningAttemptResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('extracts the safe backend-supplied conflict detail from a failed request', async () => {
    const apiException = new ApiException(
      'Conflict',
      409,
      JSON.stringify({ errors: [{ detail: 'This run already has a Codex planning attempt in progress.' }] }),
      {},
      null,
    )
    vi.mocked(requestCodexPlanningAttemptClient).mockReturnValue({
      requestCodexPlanningAttempt: vi.fn().mockRejectedValue(apiException),
    } as unknown as ReturnType<typeof requestCodexPlanningAttemptClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestCodexPlanningAttempt(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('This run already has a Codex planning attempt in progress.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception', async () => {
    vi.mocked(requestCodexPlanningAttemptClient).mockReturnValue({
      requestCodexPlanningAttempt: vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')),
    } as unknown as ReturnType<typeof requestCodexPlanningAttemptClient>)

    const { result } = renderHook(() => useRequestCodexPlanningAttempt(vi.fn()))

    await act(async () => {
      await result.current.request('run-1')
    })

    expect(result.current.error).toBe('A Codex plan could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })
})
