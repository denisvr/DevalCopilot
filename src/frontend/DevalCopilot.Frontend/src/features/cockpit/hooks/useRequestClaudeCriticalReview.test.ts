import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestClaudeCriticalReviewResponse } from '../../../api/generated/api-client'
import { requestClaudeCriticalReviewClient } from '../../../api/clients'
import { useRequestClaudeCriticalReview } from './useRequestClaudeCriticalReview'

vi.mock('../../../api/clients', () => ({
  requestClaudeCriticalReviewClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('useRequestClaudeCriticalReview', () => {
  it('requests the attempt for the given run and Proposal message, and notifies onRequested on success', async () => {
    const requestClaudeCriticalReview = vi
      .fn()
      .mockResolvedValue(new RequestClaudeCriticalReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
    vi.mocked(requestClaudeCriticalReviewClient).mockReturnValue({
      requestClaudeCriticalReview,
    } as unknown as ReturnType<typeof requestClaudeCriticalReviewClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestClaudeCriticalReview(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'message-1')
    })

    expect(requestClaudeCriticalReview).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestClaudeCriticalReview.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg).toMatchObject({ proposalMessageId: 'message-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestClaudeCriticalReviewResponse>()
    vi.mocked(requestClaudeCriticalReviewClient).mockReturnValue({
      requestClaudeCriticalReview: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestClaudeCriticalReviewClient>)

    const { result } = renderHook(() => useRequestClaudeCriticalReview(vi.fn()))

    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'message-1')
    })

    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(new RequestClaudeCriticalReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('extracts the safe backend-supplied conflict detail from a failed request', async () => {
    const apiException = new ApiException(
      'Conflict',
      409,
      JSON.stringify({ errors: [{ detail: 'This run already has a Claude critical-review attempt in progress.' }] }),
      {},
      null,
    )
    vi.mocked(requestClaudeCriticalReviewClient).mockReturnValue({
      requestClaudeCriticalReview: vi.fn().mockRejectedValue(apiException),
    } as unknown as ReturnType<typeof requestClaudeCriticalReviewClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestClaudeCriticalReview(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'message-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('This run already has a Claude critical-review attempt in progress.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception', async () => {
    vi.mocked(requestClaudeCriticalReviewClient).mockReturnValue({
      requestClaudeCriticalReview: vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')),
    } as unknown as ReturnType<typeof requestClaudeCriticalReviewClient>)

    const { result } = renderHook(() => useRequestClaudeCriticalReview(vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'message-1')
    })

    expect(result.current.error).toBe('A Claude critical review could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })
})
