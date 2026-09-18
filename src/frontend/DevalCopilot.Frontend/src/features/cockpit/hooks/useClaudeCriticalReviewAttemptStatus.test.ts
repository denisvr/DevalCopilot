// @vitest-environment jsdom
import { createElement } from 'react'
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ClaudeCriticalReviewAttemptStatusResponse } from '../../../api/generated/api-client'
import { claudeCriticalReviewAttemptStatusClient } from '../../../api/clients'
import { useClaudeCriticalReviewAttemptStatus } from './useClaudeCriticalReviewAttemptStatus'

vi.mock('../../../api/clients', () => ({
  claudeCriticalReviewAttemptStatusClient: vi.fn(),
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
 * render that React/Testing Library flushed within the same `act()` batch as the rerender. */
function RecordingHarness({ runId, sequence, log }: { runId: string | null; sequence: number; log: RenderLogEntry[] }) {
  const result = useClaudeCriticalReviewAttemptStatus(runId, sequence)
  log.push({ runId, status: result.status?.attemptId ?? null, loading: result.loading, error: result.error })
  return null
}

describe('useClaudeCriticalReviewAttemptStatus', () => {
  it('reports loading before the initial request settles', () => {
    const pending = deferred<ClaudeCriticalReviewAttemptStatusResponse>()
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result } = renderHook(() => useClaudeCriticalReviewAttemptStatus('run-1', 1))

    expect(result.current.loading).toBe(true)
    expect(result.current.status).toBeNull()
  })

  // The endpoint always returns a real, well-formed body now: `hasAttempt` is the explicit
  // discriminator for "this run has never requested a Claude critical review", never an
  // ambiguous all-undefined instance to infer presence from.
  it('treats an explicit hasAttempt: false response as no attempt rather than as a loaded attempt', async () => {
    const response = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: false,
      attemptId: undefined,
      attemptNumber: undefined,
      reviewedProposalMessageId: undefined,
      status: undefined,
      outcome: undefined,
      claimedAtUtc: undefined,
      dispatchedAtUtc: undefined,
      completedAtUtc: undefined,
      artifacts: [],
    })
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result } = renderHook(() => useClaudeCriticalReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.status).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('surfaces a real attempt once hasAttempt is true', async () => {
    const response = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      reviewedProposalMessageId: 'message-1',
      status: 'Running',
      claimedAtUtc: new Date('2026-09-17T00:00:00Z'),
      artifacts: [],
    })
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result } = renderHook(() => useClaudeCriticalReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
  })

  it('reports a failure without pretending no attempt exists', async () => {
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus: vi.fn().mockRejectedValue(new Error('network unavailable')),
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result } = renderHook(() => useClaudeCriticalReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.error).toBe('Claude critical review attempt status is unavailable.'))
    expect(result.current.status).toBeNull()
  })

  it('refetches when the latest event sequence advances', async () => {
    const first = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      reviewedProposalMessageId: 'message-1',
      status: 'Running',
    })
    const second = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      reviewedProposalMessageId: 'message-1',
      status: 'Completed',
      outcome: 'Accepted',
    })
    const getClaudeCriticalReviewAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus,
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ sequence }) => useClaudeCriticalReviewAttemptStatus('run-1', sequence), {
      initialProps: { sequence: 1 },
    })

    await waitFor(() => expect(result.current.status?.status).toBe('Running'))
    rerender({ sequence: 2 })
    await waitFor(() => expect(result.current.status?.status).toBe('Completed'))
    expect(getClaudeCriticalReviewAttemptStatus).toHaveBeenCalledTimes(2)
  })

  it('refetches on demand via refresh() without waiting for a sequence change', async () => {
    const first = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      reviewedProposalMessageId: 'message-1',
      status: 'Running',
    })
    const second = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-2',
      attemptNumber: 2,
      reviewedProposalMessageId: 'message-1',
      status: 'Running',
    })
    const getClaudeCriticalReviewAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus,
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result } = renderHook(() => useClaudeCriticalReviewAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    act(() => {
      result.current.refresh()
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-2'))
    expect(getClaudeCriticalReviewAttemptStatus).toHaveBeenCalledTimes(2)
  })

  it('isolates state across a run switch and ignores the previous run response', async () => {
    const runOne = deferred<ClaudeCriticalReviewAttemptStatusResponse>()
    const runTwo = deferred<ClaudeCriticalReviewAttemptStatusResponse>()
    const getClaudeCriticalReviewAttemptStatus = vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise)
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus,
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useClaudeCriticalReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      runOne.resolve(
        new ClaudeCriticalReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt', attemptNumber: 1, status: 'Running' }),
      )
    })
    expect(result.current.status).toBeNull()

    await act(async () => {
      runTwo.resolve(
        new ClaudeCriticalReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }),
      )
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  // Regression test: switching from a run that already has a loaded status must never leave
  // that stale status visible "on" the newly-selected run, even for the brief window before
  // the new run's own fetch resolves. A prior version of this hook only guarded against a
  // late-arriving response overwriting the new run's state (the test above) but never cleared
  // the previous run's status/loading synchronously on the runId change itself, so the cockpit
  // could briefly (or indefinitely, if the new run's fetch is slow) show run A's status while
  // "on" run B.
  it('clears the previous run status synchronously on a run switch, before the new run fetch resolves', async () => {
    const runOneResponse = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'run-one-attempt',
      attemptNumber: 1,
      status: 'Running',
    })
    const runTwoPending = deferred<ClaudeCriticalReviewAttemptStatusResponse>()
    const getClaudeCriticalReviewAttemptStatus = vi
      .fn()
      .mockResolvedValueOnce(runOneResponse)
      .mockReturnValueOnce(runTwoPending.promise)
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus,
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useClaudeCriticalReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-one-attempt'))

    rerender({ runId: 'run-2' })

    // Must never still be showing run-1's status while "on" run-2, even before run-2's own
    // fetch has resolved.
    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(true)

    await act(async () => {
      runTwoPending.resolve(
        new ClaudeCriticalReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }),
      )
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  // Stronger than the "clears ... synchronously" test above: that test only inspects
  // `result.current` after `rerender()` returns, which cannot prove no stale render ever
  // occurred — React/Testing Library can flush an effect's own synchronous state updates
  // within that same `act()` batch, so a hook that clears state from inside `useEffect`
  // (rather than masking it at render time from props+state alone) can still pass that test
  // while genuinely producing a stale committed render the test never gets to see. This test
  // records every render's value from inside the component body itself, so it cannot miss one.
  it('never exposes a different run\'s status/loading in any recorded render across a run switch', async () => {
    const runOneResponse = new ClaudeCriticalReviewAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'run-one-attempt',
      attemptNumber: 1,
      status: 'Running',
    })
    const runTwoPending = deferred<ClaudeCriticalReviewAttemptStatusResponse>()
    const getClaudeCriticalReviewAttemptStatus = vi
      .fn()
      .mockResolvedValueOnce(runOneResponse)
      .mockReturnValueOnce(runTwoPending.promise)
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus,
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const log: RenderLogEntry[] = []

    const { rerender } = render(createElement(RecordingHarness, { runId: 'run-1', sequence: 1, log }))
    await waitFor(() => expect(log.at(-1)?.status).toBe('run-one-attempt'))

    rerender(createElement(RecordingHarness, { runId: 'run-2', sequence: 1, log }))

    // Every render recorded once the harness is rendering run-2 — including any flushed
    // synchronously within the same act() batch as the rerender call — must never expose
    // run-1's attempt id, and must truthfully report loading for run-2's own still-pending
    // fetch.
    const rendersUnderRunTwo = log.filter((entry) => entry.runId === 'run-2')
    expect(rendersUnderRunTwo.length).toBeGreaterThan(0)
    for (const entry of rendersUnderRunTwo) {
      expect(entry.status).not.toBe('run-one-attempt')
      expect(entry.loading).toBe(true)
    }

    await act(async () => {
      runTwoPending.resolve(
        new ClaudeCriticalReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', attemptNumber: 1, status: 'Running' }),
      )
    })
    await waitFor(() => expect(log.at(-1)?.status).toBe('run-two-attempt'))

    // Exhaustive confirmation across every render this test ever produced, not just the final
    // settled one.
    expect(log.some((entry) => entry.runId === 'run-2' && entry.status === 'run-one-attempt')).toBe(false)
  })

  it('clears status and stops loading when runId becomes null', async () => {
    const response = new ClaudeCriticalReviewAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-1', attemptNumber: 1, status: 'Running' })
    vi.mocked(claudeCriticalReviewAttemptStatusClient).mockReturnValue({
      getClaudeCriticalReviewAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof claudeCriticalReviewAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useClaudeCriticalReviewAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' as string | null },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    rerender({ runId: null })

    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })
})
