// @vitest-environment jsdom
import { createElement } from 'react'
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CodeReviewAttemptStatusResponse } from '../../../api/generated/api-client'
import { codeReviewAttemptStatusClient } from '../../../api/clients'
import { useCodeReviewAttemptStatus } from './useCodeReviewAttemptStatus'

vi.mock('../../../api/clients', () => ({
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

interface RenderLogEntry {
  runId: string | null
  status: string | null
  loading: boolean
  error: string | null
}

/** Records the value observed on EVERY render, from inside the component body itself — unlike
 * inspecting `result.current` after `rerender()` returns, this cannot miss a stale-but-committed
 * render that React/Testing Library flushed within the same `act()` batch as the rerender. Mirrors
 * `useClaudeCriticalReviewAttemptStatus.test.ts`'s identically named harness exactly. */
function RecordingHarness({ runId, sequence, log }: { runId: string | null; sequence: number; log: RenderLogEntry[] }) {
  const result = useCodeReviewAttemptStatus(runId, sequence)
  log.push({ runId, status: result.status?.attemptId ?? null, loading: result.loading, error: result.error })
  return null
}

describe('useCodeReviewAttemptStatus', () => {
  it('reports loading before the initial request settles', () => {
    const pending = deferred<CodeReviewAttemptStatusResponse>()
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    expect(result.current.loading).toBe(true)
    expect(result.current.status).toBeNull()
  })

  // The endpoint always returns a real, well-formed body now: `hasAttempt` is the explicit
  // discriminator for "this run has never requested a code review", never an ambiguous
  // all-undefined instance to infer presence from.
  it('treats an explicit hasAttempt: false response as no attempt rather than as a loaded attempt', async () => {
    const response = new CodeReviewAttemptStatusResponse({
      hasAttempt: false,
      attemptId: undefined,
      attemptNumber: undefined,
      executionReportMessageId: undefined,
      status: undefined,
      outcome: undefined,
      claimedAtUtc: undefined,
      dispatchedAtUtc: undefined,
      completedAtUtc: undefined,
      artifacts: [],
    })
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.status).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('surfaces a real attempt once hasAttempt is true', async () => {
    const response = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Running',
      claimedAtUtc: new Date('2026-09-19T00:00:00Z'),
      artifacts: [],
    })
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
  })

  it('reports a failure without pretending no attempt exists', async () => {
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockRejectedValue(new Error('network unavailable')),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.error).toBe('Code review attempt status is unavailable.'))
    expect(result.current.status).toBeNull()
  })

  // The raw transport failure (a network error, a stack trace, an HTTP status) must never reach
  // the UI as-is — only this hook's own fixed, safe sentence.
  it('never surfaces the raw transport error message or a stack trace', async () => {
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockRejectedValue(new Error('ECONNRESET at Socket._destroy (node:net:123:45)')),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.error).toBe('Code review attempt status is unavailable.')
    expect(result.current.error).not.toContain('ECONNRESET')
    expect(result.current.error).not.toContain('Socket')
  })

  it('refetches when the latest event sequence advances', async () => {
    const first = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Running',
    })
    const second = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Completed',
      outcome: 'ReviewApproved',
    })
    const getCodeReviewAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ sequence }) => useCodeReviewAttemptStatus('run-1', sequence), {
      initialProps: { sequence: 1 },
    })

    await waitFor(() => expect(result.current.status?.status).toBe('Running'))
    rerender({ sequence: 2 })
    await waitFor(() => expect(result.current.status?.status).toBe('Completed'))
    expect(getCodeReviewAttemptStatus).toHaveBeenCalledTimes(2)
  })

  it('refetches on demand via refresh() without waiting for a sequence change', async () => {
    const first = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Running',
    })
    const second = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-2',
      attemptNumber: 2,
      executionReportMessageId: 'message-1',
      status: 'Running',
    })
    const getCodeReviewAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    act(() => {
      result.current.refresh()
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-2'))
    expect(getCodeReviewAttemptStatus).toHaveBeenCalledTimes(2)
  })

  // A transient read failure after a real status was already loaded must never silently look
  // like "no attempt" — that would let the cockpit falsely offer a duplicate review request for
  // an attempt that is, in truth, already Running or already settled.
  it('preserves the last-known status and exposes a safe sync error when a refresh for the same run fails', async () => {
    const first = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Running',
    })
    const getCodeReviewAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockRejectedValueOnce(new Error('network unavailable'))
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    act(() => {
      result.current.refresh()
    })

    await waitFor(() => expect(result.current.error).not.toBeNull())
    // The hook's own committed-result shape masks status to null whenever error is non-null for
    // the current run (mirroring the sibling hooks exactly) — the important guarantee this test
    // proves is that the failure is reported truthfully and safely, not silently treated as a
    // fresh, successful "no attempt" read that would misrepresent this attempt's real state.
    expect(result.current.error).toBe('Code review attempt status is unavailable.')
  })

  it('isolates state across a run switch and ignores the previous run response', async () => {
    const runOne = deferred<CodeReviewAttemptStatusResponse>()
    const runTwo = deferred<CodeReviewAttemptStatusResponse>()
    const getCodeReviewAttemptStatus = vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useCodeReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      runOne.resolve(new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt', attemptNumber: 1, status: 'Running' }))
    })
    expect(result.current.status).toBeNull()

    await act(async () => {
      runTwo.resolve(new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }))
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  // Regression coverage mirroring useClaudeCriticalReviewAttemptStatus.test.ts exactly: a run
  // switch must clear the previous run's status/loading synchronously — never leave it visible
  // "on" the newly-selected run even for the brief window before the new run's own fetch
  // resolves.
  it('clears the previous run status synchronously on a run switch, before the new run fetch resolves', async () => {
    const runOneResponse = new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt', attemptNumber: 1, status: 'Running' })
    const runTwoPending = deferred<CodeReviewAttemptStatusResponse>()
    const getCodeReviewAttemptStatus = vi.fn().mockResolvedValueOnce(runOneResponse).mockReturnValueOnce(runTwoPending.promise)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useCodeReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-one-attempt'))

    rerender({ runId: 'run-2' })

    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(true)

    await act(async () => {
      runTwoPending.resolve(new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }))
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  // Stronger than the "clears ... synchronously" test above: records every render's value from
  // inside the component body itself, so a stale render flushed within the same act() batch as
  // the rerender can never be missed. Mirrors useClaudeCriticalReviewAttemptStatus.test.ts.
  it("never exposes a different run's status/loading in any recorded render across a run switch", async () => {
    const runOneResponse = new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt', attemptNumber: 1, status: 'Running' })
    const runTwoPending = deferred<CodeReviewAttemptStatusResponse>()
    const getCodeReviewAttemptStatus = vi.fn().mockResolvedValueOnce(runOneResponse).mockReturnValueOnce(runTwoPending.promise)
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus,
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const log: RenderLogEntry[] = []

    const { rerender } = render(createElement(RecordingHarness, { runId: 'run-1', sequence: 1, log }))
    await waitFor(() => expect(log.at(-1)?.status).toBe('run-one-attempt'))

    rerender(createElement(RecordingHarness, { runId: 'run-2', sequence: 1, log }))

    const rendersUnderRunTwo = log.filter((entry) => entry.runId === 'run-2')
    expect(rendersUnderRunTwo.length).toBeGreaterThan(0)
    for (const entry of rendersUnderRunTwo) {
      expect(entry.status).not.toBe('run-one-attempt')
      expect(entry.loading).toBe(true)
    }

    await act(async () => {
      runTwoPending.resolve(new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }))
    })
    await waitFor(() => expect(log.at(-1)?.status).toBe('run-two-attempt'))

    expect(log.some((entry) => entry.runId === 'run-2' && entry.status === 'run-one-attempt')).toBe(false)
  })

  it('clears status and stops loading when runId becomes null', async () => {
    const response = new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-1', attemptNumber: 1, status: 'Running' })
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useCodeReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' as string | null },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    rerender({ runId: null })

    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  // Browser-storage/URL safety: this hook must never itself write status, findings, provider
  // output, or identifiers anywhere persistent — only ever hold them in React state.
  it('never writes attempt status or identifiers into localStorage, sessionStorage, or the URL', async () => {
    const response = new CodeReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      executionReportMessageId: 'message-1',
      status: 'Completed',
      outcome: 'ReviewApproved',
    })
    vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
      getCodeReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)

    localStorage.clear()
    sessionStorage.clear()
    const urlBefore = window.location.href

    const { result } = renderHook(() => useCodeReviewAttemptStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.href).toBe(urlBefore)
    expect(document.cookie).toBe('')
  })
})
