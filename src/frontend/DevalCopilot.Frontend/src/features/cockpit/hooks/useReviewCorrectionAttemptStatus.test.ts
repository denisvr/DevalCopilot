// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ReviewCorrectionAttemptStatusResponse } from '../../../api/generated/api-client'
import { reviewCorrectionAttemptStatusClient } from '../../../api/clients'
import { useReviewCorrectionAttemptStatus } from './useReviewCorrectionAttemptStatus'

vi.mock('../../../api/clients', () => ({
  reviewCorrectionAttemptStatusClient: vi.fn(),
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

function response(attemptId: string, hasAttempt = true) {
  return new ReviewCorrectionAttemptStatusResponse({
    hasAttempt,
    attemptId,
    attemptNumber: 1,
    implementationReviewAttemptId: 'review-1',
    status: 'Completed',
    outcome: 'CorrectionApplied',
    artifacts: [],
  })
}

describe('useReviewCorrectionAttemptStatus', () => {
  it('starts loading and treats an explicit no-attempt response as null', async () => {
    const getStatus = vi.fn().mockResolvedValue(response('', false))
    vi.mocked(reviewCorrectionAttemptStatusClient).mockReturnValue({ getReviewCorrectionAttemptStatus: getStatus } as never)

    const { result } = renderHook(() => useReviewCorrectionAttemptStatus('run-1', 1))
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.status).toBeNull()
  })

  it('preserves an authoritative initial report when no correction attempt exists', async () => {
    const initial = response('', false)
    initial.reviewableExecutionReportMessageId = 'initial-report'
    const getStatus = vi.fn().mockResolvedValue(initial)
    vi.mocked(reviewCorrectionAttemptStatusClient).mockReturnValue({ getReviewCorrectionAttemptStatus: getStatus } as never)

    const { result } = renderHook(() => useReviewCorrectionAttemptStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.reviewableExecutionReportMessageId).toBe('initial-report'))
  })

  it('clears the last-known status when a same-run refresh fails', async () => {
    const getStatus = vi.fn().mockResolvedValueOnce(response('attempt-1')).mockRejectedValueOnce(new Error('network'))
    vi.mocked(reviewCorrectionAttemptStatusClient).mockReturnValue({ getReviewCorrectionAttemptStatus: getStatus } as never)

    const { result } = renderHook(() => useReviewCorrectionAttemptStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    act(() => result.current.refresh())
    await waitFor(() => expect(result.current.error).toBe('Review correction status is unavailable.'))
    expect(result.current.status).toBeNull()
  })

  it('masks old status and ignores stale completion after a run switch', async () => {
    const runOne = deferred<ReviewCorrectionAttemptStatusResponse>()
    const runTwo = deferred<ReviewCorrectionAttemptStatusResponse>()
    const getStatus = vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise)
    vi.mocked(reviewCorrectionAttemptStatusClient).mockReturnValue({ getReviewCorrectionAttemptStatus: getStatus } as never)

    const { result, rerender } = renderHook(({ runId }) => useReviewCorrectionAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })
    expect(result.current.status).toBeNull()
    expect(result.current.error).toBeNull()

    await act(async () => runOne.resolve(response('old-attempt')))
    expect(result.current.status).toBeNull()
    await act(async () => runTwo.resolve(response('new-attempt')))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('new-attempt'))
  })
})
