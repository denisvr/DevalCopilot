// @vitest-environment jsdom
import { createElement } from 'react'
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ImplementationAttemptStatusResponse } from '../../../api/generated/api-client'
import { implementationAttemptStatusClient } from '../../../api/clients'
import { useImplementationAttemptStatus } from './useImplementationAttemptStatus'

vi.mock('../../../api/clients', () => ({
  implementationAttemptStatusClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

interface RenderLogEntry {
  runId: string | null
  status: string | null
  loading: boolean
  error: string | null
}

function RecordingHarness({ runId, sequence, log }: { runId: string | null; sequence: number; log: RenderLogEntry[] }) {
  const result = useImplementationAttemptStatus(runId, sequence)
  log.push({ runId, status: result.status?.attemptId ?? null, loading: result.loading, error: result.error })
  return null
}

describe('useImplementationAttemptStatus', () => {
  it('reports loading before the initial request settles', () => {
    const pending = deferred<ImplementationAttemptStatusResponse>()
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result } = renderHook(() => useImplementationAttemptStatus('run-1', 1))

    expect(result.current.loading).toBe(true)
    expect(result.current.status).toBeNull()
  })

  it('treats an explicit hasAttempt: false response as no attempt rather than as a loaded attempt', async () => {
    const response = new ImplementationAttemptStatusResponse({ hasAttempt: false, changedRelativePaths: [], artifacts: [] })
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result } = renderHook(() => useImplementationAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.status).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('surfaces a real attempt once hasAttempt is true', async () => {
    const response = new ImplementationAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      attemptNumber: 2,
      planProposalMessageId: 'proposal-1',
      status: 'Running',
      changedRelativePaths: [],
      artifacts: [],
    })
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result } = renderHook(() => useImplementationAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    expect(result.current.status?.planProposalMessageId).toBe('proposal-1')
  })

  it('reports a failure without pretending no attempt exists', async () => {
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus: vi.fn().mockRejectedValue(new Error('network unavailable')),
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result } = renderHook(() => useImplementationAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.error).toBe('Implementation attempt status is unavailable.'))
    expect(result.current.status).toBeNull()
  })

  it('refetches when the latest event sequence advances', async () => {
    const first = new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-1', status: 'Running', changedRelativePaths: [] })
    const second = new ImplementationAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'attempt-1',
      status: 'Completed',
      outcome: 'Implemented',
      changedRelativePaths: [],
    })
    const getImplementationAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus,
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result, rerender } = renderHook(({ sequence }) => useImplementationAttemptStatus('run-1', sequence), {
      initialProps: { sequence: 1 },
    })

    await waitFor(() => expect(result.current.status?.status).toBe('Running'))
    rerender({ sequence: 2 })
    await waitFor(() => expect(result.current.status?.status).toBe('Completed'))
    expect(getImplementationAttemptStatus).toHaveBeenCalledTimes(2)
  })

  it('refetches on demand via refresh() without waiting for a sequence change', async () => {
    const first = new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-1', status: 'Running', changedRelativePaths: [] })
    const second = new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-2', status: 'Running', changedRelativePaths: [] })
    const getImplementationAttemptStatus = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus,
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result } = renderHook(() => useImplementationAttemptStatus('run-1', 1))

    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))
    act(() => {
      result.current.refresh()
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-2'))
    expect(getImplementationAttemptStatus).toHaveBeenCalledTimes(2)
  })

  it('isolates state across a run switch and ignores the previous run response', async () => {
    const runOne = deferred<ImplementationAttemptStatusResponse>()
    const runTwo = deferred<ImplementationAttemptStatusResponse>()
    const getImplementationAttemptStatus = vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise)
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus,
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useImplementationAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      runOne.resolve(
        new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-one-attempt', status: 'Running', changedRelativePaths: [] }),
      )
    })
    expect(result.current.status).toBeNull()

    await act(async () => {
      runTwo.resolve(
        new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', status: 'Running', changedRelativePaths: [] }),
      )
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-two-attempt'))
  })

  it('never exposes a different run\'s status/loading in any recorded render across a run switch', async () => {
    const runOneResponse = new ImplementationAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'run-one-attempt',
      status: 'Running',
      changedRelativePaths: [],
    })
    const runTwoPending = deferred<ImplementationAttemptStatusResponse>()
    const getImplementationAttemptStatus = vi
      .fn()
      .mockResolvedValueOnce(runOneResponse)
      .mockReturnValueOnce(runTwoPending.promise)
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus,
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

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
      runTwoPending.resolve(
        new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'run-two-attempt', status: 'Running', changedRelativePaths: [] }),
      )
    })
    await waitFor(() => expect(log.at(-1)?.status).toBe('run-two-attempt'))

    expect(log.some((entry) => entry.runId === 'run-2' && entry.status === 'run-one-attempt')).toBe(false)
  })

  it('clears status and stops loading when runId becomes null', async () => {
    const response = new ImplementationAttemptStatusResponse({ hasAttempt: true, attemptId: 'attempt-1', status: 'Running', changedRelativePaths: [] })
    vi.mocked(implementationAttemptStatusClient).mockReturnValue({
      getImplementationAttemptStatus: vi.fn().mockResolvedValue(response),
    } as unknown as ReturnType<typeof implementationAttemptStatusClient>)

    const { result, rerender } = renderHook(({ runId }) => useImplementationAttemptStatus(runId, 1), {
      initialProps: { runId: 'run-1' as string | null },
    })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('attempt-1'))

    rerender({ runId: null })

    expect(result.current.status).toBeNull()
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })
})
