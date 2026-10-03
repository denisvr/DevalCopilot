import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { codeReviewAttemptStatusClient, verificationDiagnosisStatusClient } from '../../../api/clients'
import { CodeReviewAttemptStatusResponse, VerificationDiagnosisStatusResponse } from '../../../api/generated/api-client'
import { useCodeReviewAttemptStatus } from './useCodeReviewAttemptStatus'
import { useVerificationDiagnosisStatus } from './useVerificationDiagnosisStatus'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  verificationDiagnosisStatusClient: vi.fn(),
  codeReviewAttemptStatusClient: vi.fn(),
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

const diagnosis = (marker: string) => new VerificationDiagnosisStatusResponse({ hasAttempt: false, diagnosableExecutionReportMessageId: marker })
const review = (marker: string) => new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: marker, executionReportMessageId: marker })

interface Props {
  runId: string | null
  generation: number
}

// Records every committed render, so a frame that still offers the previous read's answer as current cannot hide behind a later, correct one.
function recordedDiagnosis(initial: Props) {
  const frames: { props: Props; loading: boolean; marker: string | undefined }[] = []
  const rendered = renderHook(
    (props: Props) => {
      const result = useVerificationDiagnosisStatus(props.runId, 5, props.generation)
      frames.push({ props, loading: result.loading, marker: result.status?.diagnosableExecutionReportMessageId })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

function recordedReview(initial: Props) {
  const frames: { props: Props; loading: boolean; marker: string | undefined }[] = []
  const rendered = renderHook(
    (props: Props) => {
      const result = useCodeReviewAttemptStatus(props.runId, 5, props.generation)
      frames.push({ props, loading: result.loading, marker: result.status?.attemptId })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

describe('run status reads follow the evidence refresh generation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads the diagnosis status again for each new generation, and only then, with no loading gap in the first frame', async () => {
    let latest = diagnosis('before')
    const getVerificationDiagnosisStatus = vi.fn(() => Promise.resolve(latest))
    vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({ getVerificationDiagnosisStatus } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
    const { result, rerender, frames } = recordedDiagnosis({ runId: 'run-1', generation: 0 })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(getVerificationDiagnosisStatus).toHaveBeenCalledTimes(1)
    rerender({ runId: 'run-1', generation: 0 })
    expect(getVerificationDiagnosisStatus).toHaveBeenCalledTimes(1)

    latest = diagnosis('after')
    const settled = frames.length
    rerender({ runId: 'run-1', generation: 1 })

    expect(frames[settled]).toMatchObject({ loading: true, marker: 'before' })
    await waitFor(() => expect(result.current.status?.diagnosableExecutionReportMessageId).toBe('after'))
    expect(getVerificationDiagnosisStatus).toHaveBeenCalledTimes(2)
    expect(result.current.loading).toBe(false)
  })

  it('reads the code-review status again for each new generation, with no loading gap in the first frame', async () => {
    let latest = review('before')
    const getCodeReviewAttemptStatus = vi.fn(() => Promise.resolve(latest))
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({ getCodeReviewAttemptStatus } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
    const { result, rerender, frames } = recordedReview({ runId: 'run-1', generation: 0 })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(getCodeReviewAttemptStatus).toHaveBeenCalledTimes(1)

    latest = review('after')
    const settled = frames.length
    rerender({ runId: 'run-1', generation: 1 })

    expect(frames[settled]).toMatchObject({ loading: true, marker: 'before' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('after'))
    expect(getCodeReviewAttemptStatus).toHaveBeenCalledTimes(2)
    expect(result.current.loading).toBe(false)
  })

  it('accepts only the newest of overlapping reads for both statuses', async () => {
    const olderDiagnosis = deferred<VerificationDiagnosisStatusResponse>()
    const newerDiagnosis = deferred<VerificationDiagnosisStatusResponse>()
    const diagnosisReads = [Promise.resolve(diagnosis('initial')), olderDiagnosis.promise, newerDiagnosis.promise]
    vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({
      getVerificationDiagnosisStatus: vi.fn(() => diagnosisReads.shift()!),
    } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
    const olderReview = deferred<CodeReviewAttemptStatusResponse>()
    const newerReview = deferred<CodeReviewAttemptStatusResponse>()
    const reviewReads = [Promise.resolve(review('initial')), olderReview.promise, newerReview.promise]
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn(() => reviewReads.shift()!),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
    const d = recordedDiagnosis({ runId: 'run-1', generation: 0 })
    const r = recordedReview({ runId: 'run-1', generation: 0 })
    await waitFor(() => expect(d.result.current.loading || r.result.current.loading).toBe(false))

    d.rerender({ runId: 'run-1', generation: 1 })
    r.rerender({ runId: 'run-1', generation: 1 })
    await waitFor(() => expect(diagnosisReads).toHaveLength(1))
    d.rerender({ runId: 'run-1', generation: 2 })
    r.rerender({ runId: 'run-1', generation: 2 })
    await waitFor(() => expect(diagnosisReads).toHaveLength(0))
    await waitFor(() => expect(reviewReads).toHaveLength(0))

    await act(async () => {
      newerDiagnosis.resolve(diagnosis('newer'))
      newerReview.resolve(review('newer'))
    })
    await act(async () => {
      olderDiagnosis.resolve(diagnosis('older'))
      olderReview.resolve(review('older'))
    })

    expect(d.result.current.status?.diagnosableExecutionReportMessageId).toBe('newer')
    expect(r.result.current.status?.attemptId).toBe('newer')
    expect(d.result.current.loading).toBe(false)
    expect(r.result.current.loading).toBe(false)
  })

  it('drops the previous status and shows a safe error when a refresh fails, then recovers on the next one', async () => {
    let failing = false
    vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({
      getVerificationDiagnosisStatus: vi.fn(() => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve(diagnosis('ok')))),
    } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn(() => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve(review('ok')))),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
    const d = recordedDiagnosis({ runId: 'run-1', generation: 0 })
    const r = recordedReview({ runId: 'run-1', generation: 0 })
    await waitFor(() => expect(d.result.current.status).not.toBeNull())
    await waitFor(() => expect(r.result.current.status).not.toBeNull())

    failing = true
    d.rerender({ runId: 'run-1', generation: 1 })
    r.rerender({ runId: 'run-1', generation: 1 })
    await waitFor(() => expect(d.result.current.error).not.toBeNull())
    await waitFor(() => expect(r.result.current.error).not.toBeNull())
    expect(d.result.current.status).toBeNull()
    expect(r.result.current.status).toBeNull()

    failing = false
    d.rerender({ runId: 'run-1', generation: 2 })
    r.rerender({ runId: 'run-1', generation: 2 })
    await waitFor(() => expect(d.result.current.status?.diagnosableExecutionReportMessageId).toBe('ok'))
    await waitFor(() => expect(r.result.current.status?.attemptId).toBe('ok'))
    expect(d.result.current.error).toBeNull()
    expect(r.result.current.error).toBeNull()
  })

  it('never writes the late answer of a replaced run into its replacement, and starts a fresh generation for it', async () => {
    const lateDiagnosis = deferred<VerificationDiagnosisStatusResponse>()
    const lateReview = deferred<CodeReviewAttemptStatusResponse>()
    let hold = false
    vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({
      getVerificationDiagnosisStatus: vi.fn((runId: string) => (hold && runId === 'run-1' ? lateDiagnosis.promise : Promise.resolve(diagnosis(runId)))),
    } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn((runId: string) => (hold && runId === 'run-1' ? lateReview.promise : Promise.resolve(review(runId)))),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
    const d = recordedDiagnosis({ runId: 'run-1', generation: 0 })
    const r = recordedReview({ runId: 'run-1', generation: 0 })
    await waitFor(() => expect(d.result.current.status).not.toBeNull())
    await waitFor(() => expect(r.result.current.status).not.toBeNull())

    hold = true
    d.rerender({ runId: 'run-1', generation: 1 })
    r.rerender({ runId: 'run-1', generation: 1 })
    d.rerender({ runId: 'run-2', generation: 0 })
    r.rerender({ runId: 'run-2', generation: 0 })
    await waitFor(() => expect(d.result.current.status?.diagnosableExecutionReportMessageId).toBe('run-2'))
    await waitFor(() => expect(r.result.current.status?.attemptId).toBe('run-2'))
    await act(async () => {
      lateDiagnosis.resolve(diagnosis('late-run-1'))
      lateReview.resolve(review('late-run-1'))
    })

    expect(d.result.current.status?.diagnosableExecutionReportMessageId).toBe('run-2')
    expect(r.result.current.status?.attemptId).toBe('run-2')
    expect(d.frames.filter((frame) => frame.props.runId === 'run-2').some((frame) => frame.marker === 'run-1' || frame.marker === 'late-run-1')).toBe(false)
  })
})
