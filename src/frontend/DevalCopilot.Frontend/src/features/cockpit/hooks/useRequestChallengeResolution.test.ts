import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestChallengeResolutionResponse } from '../../../api/generated/api-client'
import { requestChallengeResolutionClient } from '../../../api/clients'
import { useRequestChallengeResolution } from './useRequestChallengeResolution'

vi.mock('../../../api/clients', () => ({
  requestChallengeResolutionClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('useRequestChallengeResolution', () => {
  it('requests the attempt for the given run and challenged review attempt, and notifies onRequested on success', async () => {
    const requestChallengeResolution = vi
      .fn()
      .mockResolvedValue(new RequestChallengeResolutionResponse({ attemptId: 'attempt-1', attemptNumber: 3 }))
    vi.mocked(requestChallengeResolutionClient).mockReturnValue({
      requestChallengeResolution,
    } as unknown as ReturnType<typeof requestChallengeResolutionClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestChallengeResolution(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'review-attempt-1')
    })

    expect(requestChallengeResolution).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestChallengeResolution.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg).toMatchObject({ challengedReviewAttemptId: 'review-attempt-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestChallengeResolutionResponse>()
    vi.mocked(requestChallengeResolutionClient).mockReturnValue({
      requestChallengeResolution: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestChallengeResolutionClient>)

    const { result } = renderHook(() => useRequestChallengeResolution(vi.fn()))

    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'review-attempt-1')
    })

    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(new RequestChallengeResolutionResponse({ attemptId: 'attempt-1', attemptNumber: 3 }))
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('extracts the safe backend-supplied conflict detail from a failed request', async () => {
    const apiException = new ApiException(
      'Conflict',
      409,
      JSON.stringify({ errors: [{ detail: 'This challenged review already has a successful resolution.' }] }),
      {},
      null,
    )
    vi.mocked(requestChallengeResolutionClient).mockReturnValue({
      requestChallengeResolution: vi.fn().mockRejectedValue(apiException),
    } as unknown as ReturnType<typeof requestChallengeResolutionClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestChallengeResolution(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'review-attempt-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('This challenged review already has a successful resolution.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception', async () => {
    vi.mocked(requestChallengeResolutionClient).mockReturnValue({
      requestChallengeResolution: vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')),
    } as unknown as ReturnType<typeof requestChallengeResolutionClient>)

    const { result } = renderHook(() => useRequestChallengeResolution(vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'review-attempt-1')
    })

    expect(result.current.error).toBe('A challenge resolution could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })
})
