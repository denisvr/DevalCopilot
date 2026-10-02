// @vitest-environment jsdom
import { createElement } from 'react'
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { VerificationDiagnosisStatusResponse } from '../../../api/generated/api-client'
import { verificationDiagnosisStatusClient } from '../../../api/clients'
import { useVerificationDiagnosisStatus } from './useVerificationDiagnosisStatus'

vi.mock('../../../api/clients', () => ({
  verificationDiagnosisStatusClient: vi.fn(),
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

function mockClient(getVerificationDiagnosisStatus: ReturnType<typeof vi.fn>) {
  vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({
    getVerificationDiagnosisStatus,
  } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
}

interface RenderLogEntry {
  runId: string | null
  attemptId: string | null
  loading: boolean
}

/** Records the value observed on EVERY render from inside the component body, so a stale-but-committed
 * render flushed within the same `act()` batch as a rerender can never be missed. */
function RecordingHarness({ runId, log }: { runId: string | null; log: RenderLogEntry[] }) {
  const result = useVerificationDiagnosisStatus(runId, 1)
  log.push({ runId, attemptId: result.status?.attemptId ?? null, loading: result.loading })
  return null
}

describe('useVerificationDiagnosisStatus', () => {
  it('reports loading before the initial request settles', () => {
    const pending = deferred<VerificationDiagnosisStatusResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))

    expect(result.current.loading).toBe(true)
    expect(result.current.status).toBeNull()
  })

  // Unlike the attempt-only status hooks, a response without an attempt still carries the display hints
  // for what could be diagnosed now, so it is kept.
  it('keeps a response without an attempt because it carries the diagnosable-report hint', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        new VerificationDiagnosisStatusResponse({ hasAttempt: false, diagnosableExecutionReportMessageId: 'report-1' }),
      ),
    )

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.status?.diagnosableExecutionReportMessageId).toBe('report-1')
    expect(result.current.error).toBeNull()
  })

  it('reports a failure with a fixed sentence, never the raw transport error', async () => {
    mockClient(vi.fn().mockRejectedValue(new Error('ECONNRESET at Socket._destroy (node:net:123:45)')))

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))

    await waitFor(() => expect(result.current.error).toBe('Verification diagnosis status is unavailable.'))
    expect(result.current.status).toBeNull()
    expect(result.current.error).not.toContain('ECONNRESET')
  })

  it('refetches when the latest event sequence advances', async () => {
    const getStatus = vi
      .fn()
      .mockResolvedValueOnce(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'a', status: 'Running' }))
      .mockResolvedValueOnce(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'a', status: 'Completed' }))
    mockClient(getStatus)

    const { result, rerender } = renderHook(({ sequence }) => useVerificationDiagnosisStatus('run-1', sequence), {
      initialProps: { sequence: 1 },
    })
    await waitFor(() => expect(result.current.status?.status).toBe('Running'))
    rerender({ sequence: 2 })
    await waitFor(() => expect(result.current.status?.status).toBe('Completed'))
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('refetches on demand via refresh() without waiting for a sequence change', async () => {
    const getStatus = vi
      .fn()
      .mockResolvedValueOnce(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'attempt-1' }))
      .mockResolvedValueOnce(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'attempt-2' }))
    mockClient(getStatus)

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    act(() => {
      result.current.refresh()
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-2'))
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('ignores an older overlapping response that settles after a newer one', async () => {
    const first = deferred<VerificationDiagnosisStatusResponse>()
    const second = deferred<VerificationDiagnosisStatusResponse>()
    mockClient(vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise))

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))
    act(() => {
      result.current.refresh()
    })

    await act(async () => {
      second.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'newer' }))
    })
    await act(async () => {
      first.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'older' }))
    })

    await waitFor(() => expect(result.current.status?.attemptId).toBe('newer'))
  })

  it('isolates state across a run switch and ignores the previous run response', async () => {
    const runOne = deferred<VerificationDiagnosisStatusResponse>()
    const runTwo = deferred<VerificationDiagnosisStatusResponse>()
    mockClient(vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise))

    const { result, rerender } = renderHook(({ runId }) => useVerificationDiagnosisStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      runOne.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt' }))
    })
    expect(result.current.status).toBeNull()

    await act(async () => {
      runTwo.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt' }))
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  it('never exposes a different run status or loading flag in any recorded render, including A to B to A', async () => {
    const runOne = new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt' })
    const runTwoPending = deferred<VerificationDiagnosisStatusResponse>()
    const runOneAgain = deferred<VerificationDiagnosisStatusResponse>()
    mockClient(
      vi.fn().mockResolvedValueOnce(runOne).mockReturnValueOnce(runTwoPending.promise).mockReturnValueOnce(runOneAgain.promise),
    )
    const log: RenderLogEntry[] = []

    const { rerender } = render(createElement(RecordingHarness, { runId: 'run-1', log }))
    await waitFor(() => expect(log.at(-1)?.attemptId).toBe('run-one-attempt'))

    rerender(createElement(RecordingHarness, { runId: 'run-2', log }))
    const underRunTwo = log.filter((entry) => entry.runId === 'run-2')
    expect(underRunTwo.length).toBeGreaterThan(0)
    for (const entry of underRunTwo) {
      expect(entry.attemptId).toBeNull()
      expect(entry.loading).toBe(true)
    }

    // Back to run 1 while run 2's read is still pending: run 1 must not show run 2's late result.
    rerender(createElement(RecordingHarness, { runId: 'run-1', log }))
    await act(async () => {
      runTwoPending.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt' }))
    })
    expect(log.some((entry) => entry.runId === 'run-1' && entry.attemptId === 'run-two-attempt')).toBe(false)

    await act(async () => {
      runOneAgain.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'run-one-again' }))
    })
    await waitFor(() => expect(log.at(-1)?.attemptId).toBe('run-one-again'))
  })

  it('clears status and stops loading when runId becomes null', async () => {
    mockClient(vi.fn().mockResolvedValue(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'attempt-1' })))

    const { result, rerender } = renderHook(({ runId }) => useVerificationDiagnosisStatus(runId, 1), {
      initialProps: { runId: 'run-1' as string | null },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    rerender({ runId: null })

    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('does not set state after unmount', async () => {
    const pending = deferred<VerificationDiagnosisStatusResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    const { unmount } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))
    unmount()
    await act(async () => {
      pending.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'late' }))
    })

    expect(errorSpy).not.toHaveBeenCalled()
    errorSpy.mockRestore()
  })

  it('never writes status or identifiers into localStorage, sessionStorage, or the URL', async () => {
    mockClient(vi.fn().mockResolvedValue(new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId: 'attempt-1' })))
    localStorage.clear()
    sessionStorage.clear()
    const urlBefore = window.location.href

    const { result } = renderHook(() => useVerificationDiagnosisStatus('run-1', 1))
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.href).toBe(urlBefore)
  })

  describe('lifetime ownership', () => {
    const ok = (attemptId: string) => new VerificationDiagnosisStatusResponse({ hasAttempt: true, attemptId })
    const UNAVAILABLE = 'Verification diagnosis status is unavailable.'

    interface Frame {
      runId: string | null
      attemptId: string | null
      loading: boolean
      error: string | null
    }

    interface Sink {
      refresh?: () => void
    }

    const publishRefresh = (sink: Sink, refresh: () => void) => {
      sink.refresh = refresh
    }

    function Recorder({ runId, log, sink }: { runId: string | null; log: Frame[]; sink: Sink }) {
      const result = useVerificationDiagnosisStatus(runId, 1)
      publishRefresh(sink, result.refresh)
      log.push({ runId, attemptId: result.status?.attemptId ?? null, loading: result.loading, error: result.error })
      return null
    }

    it('A resolved, B pending, A again starts a new lifetime without the old A status', async () => {
      const bPending = deferred<VerificationDiagnosisStatusResponse>()
      const aAgain = deferred<VerificationDiagnosisStatusResponse>()
      mockClient(vi.fn().mockResolvedValueOnce(ok('old-a')).mockReturnValueOnce(bPending.promise).mockReturnValueOnce(aAgain.promise))
      const log: Frame[] = []
      const sink: Sink = {}

      const { rerender } = render(createElement(Recorder, { runId: 'A', log, sink }))
      await waitFor(() => expect(log.at(-1)?.attemptId).toBe('old-a'))
      const mark = log.length
      rerender(createElement(Recorder, { runId: 'B', log, sink }))
      rerender(createElement(Recorder, { runId: 'A', log, sink }))
      await act(async () => {})

      const after = log.slice(mark)
      expect(after.length).toBeGreaterThan(0)
      for (const frame of after) {
        expect(frame).toMatchObject({ attemptId: null, loading: true, error: null })
      }

      await act(async () => {
        bPending.resolve(ok('late-b'))
      })
      expect(log.some((f) => f.attemptId === 'late-b')).toBe(false)
      await act(async () => {
        aAgain.resolve(ok('new-a'))
      })
      expect(log.at(-1)).toMatchObject({ attemptId: 'new-a', loading: false })
    })

    it('does not show the previous run error in any frame of B or of A returning', async () => {
      mockClient(vi.fn().mockRejectedValueOnce(new Error('x')).mockReturnValue(new Promise(() => {})))
      const log: Frame[] = []
      const sink: Sink = {}

      const { rerender } = render(createElement(Recorder, { runId: 'A', log, sink }))
      await waitFor(() => expect(log.at(-1)?.error).toBe(UNAVAILABLE))
      const mark = log.length
      rerender(createElement(Recorder, { runId: 'B', log, sink }))
      rerender(createElement(Recorder, { runId: 'A', log, sink }))
      await act(async () => {})

      expect(log.length).toBeGreaterThan(mark)
      for (const frame of log.slice(mark)) {
        expect(frame).toMatchObject({ error: null, attemptId: null, loading: true })
      }
    })

    it('a refresh retained from A issues no request after the selection moved to B', async () => {
      const getStatus = vi.fn().mockResolvedValueOnce(ok('a')).mockReturnValue(new Promise(() => {}))
      mockClient(getStatus)
      const log: Frame[] = []
      const sink: Sink = {}

      const { rerender } = render(createElement(Recorder, { runId: 'A', log, sink }))
      await waitFor(() => expect(log.at(-1)?.attemptId).toBe('a'))
      const retained = sink.refresh!
      rerender(createElement(Recorder, { runId: 'B', log, sink }))
      await act(async () => {})
      expect(getStatus).toHaveBeenCalledTimes(2)

      await act(async () => {
        retained()
      })
      expect(getStatus).toHaveBeenCalledTimes(2)
      expect(log.at(-1)).toMatchObject({ runId: 'B', attemptId: null, loading: true })
    })

    it('a refresh retained after a null selection or unmount issues no request', async () => {
      const getStatus = vi.fn().mockResolvedValue(ok('a'))
      mockClient(getStatus)
      const log: Frame[] = []
      const sink: Sink = {}

      const { rerender, unmount } = render(createElement(Recorder, { runId: 'A', log, sink }))
      await waitFor(() => expect(log.at(-1)?.attemptId).toBe('a'))
      const retained = sink.refresh!
      rerender(createElement(Recorder, { runId: null, log, sink }))
      expect(log.at(-1)).toMatchObject({ attemptId: null, loading: false, error: null })
      const nullRefresh = sink.refresh!
      await act(async () => {
        retained()
        nullRefresh()
      })
      unmount()
      await act(async () => {
        retained()
      })
      expect(getStatus).toHaveBeenCalledTimes(1)
    })

    it('an older late answer never overwrites a newer one nor clears its loading or error', async () => {
      const first = deferred<VerificationDiagnosisStatusResponse>()
      const second = deferred<VerificationDiagnosisStatusResponse>()
      const third = deferred<VerificationDiagnosisStatusResponse>()
      mockClient(vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise).mockReturnValueOnce(third.promise))
      const log: Frame[] = []
      const sink: Sink = {}

      render(createElement(Recorder, { runId: 'A', log, sink }))
      await act(async () => {
        sink.refresh!()
      })
      await act(async () => {
        sink.refresh!()
      })
      await act(async () => {
        second.reject(new Error('boom'))
      })
      expect(log.at(-1)).toMatchObject({ loading: true })
      await act(async () => {
        first.resolve(ok('older'))
      })
      expect(log.some((f) => f.attemptId === 'older')).toBe(false)
      await act(async () => {
        third.reject(new Error('boom'))
      })
      expect(log.at(-1)).toMatchObject({ loading: false, error: UNAVAILABLE, attemptId: null })
    })

    it('a late older error does not clear the newer success or surface as an error', async () => {
      const first = deferred<VerificationDiagnosisStatusResponse>()
      mockClient(vi.fn().mockReturnValueOnce(first.promise).mockResolvedValueOnce(ok('newer')))
      const log: Frame[] = []
      const sink: Sink = {}

      render(createElement(Recorder, { runId: 'A', log, sink }))
      await act(async () => {
        sink.refresh!()
      })
      await waitFor(() => expect(log.at(-1)?.attemptId).toBe('newer'))
      await act(async () => {
        first.reject(new Error('late'))
      })
      expect(log.at(-1)).toMatchObject({ attemptId: 'newer', loading: false, error: null })
    })

    it('keeps the previous status while a same-owner refresh is pending, truthfully loading', async () => {
      const second = deferred<VerificationDiagnosisStatusResponse>()
      mockClient(vi.fn().mockResolvedValueOnce(ok('one')).mockReturnValueOnce(second.promise))
      const log: Frame[] = []
      const sink: Sink = {}

      render(createElement(Recorder, { runId: 'A', log, sink }))
      await waitFor(() => expect(log.at(-1)?.attemptId).toBe('one'))
      await act(async () => {
        sink.refresh!()
      })
      expect(log.at(-1)).toMatchObject({ attemptId: 'one', loading: true })
      await act(async () => {
        second.resolve(ok('two'))
      })
      expect(log.at(-1)).toMatchObject({ attemptId: 'two', loading: false })
    })

    it('ignores late completions after the owner became null', async () => {
      const pending = deferred<VerificationDiagnosisStatusResponse>()
      mockClient(vi.fn().mockReturnValue(pending.promise))
      const log: Frame[] = []
      const sink: Sink = {}

      const { rerender } = render(createElement(Recorder, { runId: 'A', log, sink }))
      rerender(createElement(Recorder, { runId: null, log, sink }))
      await act(async () => {
        pending.resolve(ok('late'))
      })
      expect(log.some((f) => f.attemptId === 'late')).toBe(false)
      expect(log.at(-1)).toMatchObject({ runId: null, attemptId: null, loading: false, error: null })
    })
  })
})
