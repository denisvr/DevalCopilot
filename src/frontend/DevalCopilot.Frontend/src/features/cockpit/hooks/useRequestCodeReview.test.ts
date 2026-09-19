import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestCodeReviewResponse } from '../../../api/generated/api-client'
import { requestCodeReviewClient } from '../../../api/clients'
import { useRequestCodeReview } from './useRequestCodeReview'

vi.mock('../../../api/clients', () => ({
  requestCodeReviewClient: vi.fn(),
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

describe('useRequestCodeReview', () => {
  it('requests the attempt for the given run and ExecutionReport message, and notifies onRequested on success', async () => {
    const requestCodeReview = vi.fn().mockResolvedValue(new RequestCodeReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview,
    } as unknown as ReturnType<typeof requestCodeReviewClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestCodeReview(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'message-1')
    })

    expect(requestCodeReview).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestCodeReview.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg).toMatchObject({ executionReportMessageId: 'message-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestCodeReviewResponse>()
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestCodeReviewClient>)

    const { result } = renderHook(() => useRequestCodeReview(vi.fn()))

    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'message-1')
    })

    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(new RequestCodeReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('extracts the safe backend-supplied conflict detail from a failed request, never the raw reason code', async () => {
    const apiException = new ApiException(
      'Conflict',
      409,
      JSON.stringify({
        errors: [{ code: 'agent_attempts.verification_evidence_not_passed', detail: 'One of the enabled verification commands has not passed for the current checkpoint.' }],
      }),
      {},
      null,
    )
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview: vi.fn().mockRejectedValue(apiException),
    } as unknown as ReturnType<typeof requestCodeReviewClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestCodeReview(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'message-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('One of the enabled verification commands has not passed for the current checkpoint.')
    expect(result.current.error).not.toContain('agent_attempts.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception or stack trace', async () => {
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview: vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')),
    } as unknown as ReturnType<typeof requestCodeReviewClient>)

    const { result } = renderHook(() => useRequestCodeReview(vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'message-1')
    })

    expect(result.current.error).toBe('A code review could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })

  // A request kicked off for a run the cockpit has since navigated away from must never surface
  // its eventual completion or error against the run now being viewed. This hook has no runId of
  // its own — the caller supplies runId per call and closes over whichever `onRequested` was
  // live when `request()` was invoked. That closure is exactly what fires when the promise
  // settles, even after a later render supplies a different `onRequested` — proving this hook
  // never reads a *new* render's callback for an *already in-flight* call, which is what would
  // let an old run's request reach back into a newer run's state. (In `RunCockpitView`, the
  // `onRequested` passed in is `useCodeReviewAttemptStatus(runId, ...).refresh`, which is itself
  // safe to call unconditionally — it only ever nudges that same status hook to re-fetch for
  // whatever run it is currently bound to, never "run-1" specifically, so this closure behavior
  // is exactly the property that keeps an old run's settled request from producing a misleading
  // refresh against the run now being viewed.)
  it('settles against the exact onRequested closure captured when request() was called, never a later render\'s callback', async () => {
    const pending = deferred<RequestCodeReviewResponse>()
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestCodeReviewClient>)

    const onRequestedAtCallTime = vi.fn()
    const { result, rerender } = renderHook(({ onRequested }) => useRequestCodeReview(onRequested), {
      initialProps: { onRequested: onRequestedAtCallTime },
    })

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'message-1')
    })

    // The cockpit switches runs while the request for run-1 is still in flight — a fresh
    // onRequested callback (bound to the new run's own status hook instance) is supplied on the
    // next render.
    const onRequestedAfterRunSwitch = vi.fn()
    rerender({ onRequested: onRequestedAfterRunSwitch })

    await act(async () => {
      pending.resolve(new RequestCodeReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await requestPromise
    })

    expect(onRequestedAtCallTime).toHaveBeenCalledTimes(1)
    expect(onRequestedAfterRunSwitch).not.toHaveBeenCalled()
  })

  // Browser-storage/URL safety: requesting a review must never itself persist the run id,
  // message id, attempt id, or any error text anywhere outside React state.
  it('never writes request identifiers or error text into localStorage, sessionStorage, or the URL', async () => {
    vi.mocked(requestCodeReviewClient).mockReturnValue({
      requestCodeReview: vi.fn().mockResolvedValue(new RequestCodeReviewResponse({ attemptId: 'attempt-1', attemptNumber: 1 })),
    } as unknown as ReturnType<typeof requestCodeReviewClient>)

    localStorage.clear()
    sessionStorage.clear()
    const urlBefore = window.location.href

    const { result } = renderHook(() => useRequestCodeReview(vi.fn()))
    await act(async () => {
      await result.current.request('run-1', 'message-1')
    })

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.href).toBe(urlBefore)
    expect(document.cookie).toBe('')
  })
})
