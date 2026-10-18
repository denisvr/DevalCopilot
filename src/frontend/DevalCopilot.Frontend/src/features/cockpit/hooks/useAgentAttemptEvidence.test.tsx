import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptEvidenceClient } from '../../../api/clients'
import type { AgentAttemptEvidenceResponse } from '../../../api/generated/api-client'
import { attemptEvidenceFor, attemptIdFor } from '../agentAttemptHistoryFixture'
import { useAgentAttemptEvidence } from './useAgentAttemptEvidence'
import type { AgentAttemptEvidenceTarget } from './useAgentAttemptEvidence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptEvidenceClient: vi.fn(),
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

function install(read: (runId: string, attemptId: string) => Promise<AgentAttemptEvidenceResponse>) {
  const getAgentAttemptEvidence = vi.fn(read)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence } as unknown as ReturnType<typeof agentAttemptEvidenceClient>)
  return getAgentAttemptEvidence
}

// The fixture's attempts are Planner · Codex · Proposal rows; the target repeats that immutable identity as the selected row's.
const target = (selectionKey: string, runId: string, attemptNumber: number): AgentAttemptEvidenceTarget => ({
  selectionKey,
  runId,
  expected: { attemptId: attemptIdFor(runId, attemptNumber), attemptNumber, role: 'Planner', provider: 'Codex', responseContract: 'Proposal' },
})

function mounted(initial: AgentAttemptEvidenceTarget) {
  return renderHook((props: AgentAttemptEvidenceTarget) => useAgentAttemptEvidence(props), { initialProps: initial })
}

describe('useAgentAttemptEvidence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads exactly the selected run and attempt once and accepts only an answer for that identity and number', async () => {
    const get = install((runId, attemptId) => Promise.resolve(attemptEvidenceFor(runId, Number(attemptId.split('-').pop()))))
    const { result } = mounted(target('selection-1', 'run-a', 4))

    expect(result.current.state).toEqual({ status: 'loading' })
    await waitFor(() => expect(result.current.state.status).toBe('success'))
    expect(get.mock.calls).toEqual([['run-a', attemptIdFor('run-a', 4)]])
  })

  it('fails closed with an explicit error when the answer names another attempt, and reads the same attempt again only on retry', async () => {
    let reads = 0
    const get = install((runId) => Promise.resolve(++reads === 1 ? attemptEvidenceFor(runId, 5) : attemptEvidenceFor(runId, 4)))
    const { result } = mounted(target('selection-1', 'run-a', 4))

    await waitFor(() => expect(result.current.state.status).toBe('error'))
    expect(result.current.state).toMatchObject({ failure: 'invalid' })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.state.status).toBe('success'))
    expect(get.mock.calls).toEqual([
      ['run-a', attemptIdFor('run-a', 4)],
      ['run-a', attemptIdFor('run-a', 4)],
    ])
  })

  it('fails closed on verified evidence whose recognized role, provider and contract contradict the selected row', async () => {
    const get = install((runId) =>
      Promise.resolve(attemptEvidenceFor(runId, 4, ['AgentContextManifest'], { role: 'Resolver', responseContract: 'ChallengeResolution' })),
    )
    const { result } = mounted(target('selection-1', 'run-a', 4))

    await waitFor(() => expect(result.current.state.status).toBe('error'))
    expect(result.current.state).toEqual({
      status: 'error',
      failure: 'invalid',
      message: 'The host answered with attempt evidence that is not coherent for this attempt, so none of it is shown.',
    })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('accepts evidence whose status advanced since the page was read', async () => {
    install((runId) => Promise.resolve(attemptEvidenceFor(runId, 4, ['AgentFinalResponse'], { attemptStatus: 'Completed', outcome: 'Proposed' })))
    const { result } = mounted(target('selection-1', 'run-a', 4))

    await waitFor(() => expect(result.current.state.status).toBe('success'))
    const state = result.current.state
    expect(state.status === 'success' ? state.evidence.attemptStatus : null).toBe('Completed')
  })

  it('maps a 404 to not-found and any other failure to a read error that discloses nothing', async () => {
    install(() => Promise.reject({ status: 404 }))
    const { result, rerender } = mounted(target('selection-1', 'run-a', 4))
    await waitFor(() => expect(result.current.state).toEqual({ status: 'not-found' }))

    install(() => Promise.reject(new Error('C:\\secret\\path')))
    rerender(target('selection-2', 'run-a', 4))
    await waitFor(() => expect(result.current.state.status).toBe('error'))
    expect(JSON.stringify(result.current.state)).not.toContain('secret')
  })

  it('ignores retry while loaded or loading, so it never duplicates a read', async () => {
    const slow = deferred<AgentAttemptEvidenceResponse>()
    const get = install(() => slow.promise)
    const { result } = mounted(target('selection-1', 'run-a', 4))

    act(() => result.current.retry())
    expect(get).toHaveBeenCalledTimes(1)
    await act(async () => {
      slow.resolve(attemptEvidenceFor('run-a', 4))
      await slow.promise
    })
    act(() => result.current.retry())
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows a replaced selection loading in its first frame and never lets the earlier answer settle it', async () => {
    const first = deferred<AgentAttemptEvidenceResponse>()
    const second = deferred<AgentAttemptEvidenceResponse>()
    install((_runId, attemptId) => (attemptId.endsWith('-4') ? first.promise : second.promise))
    const { result, rerender } = mounted(target('selection-1', 'run-a', 4))

    rerender(target('selection-2', 'run-a', 3))
    expect(result.current.state).toEqual({ status: 'loading' })
    await act(async () => {
      first.resolve(attemptEvidenceFor('run-a', 4))
      await first.promise
    })
    expect(result.current.state).toEqual({ status: 'loading' })
    await act(async () => {
      second.resolve(attemptEvidenceFor('run-a', 3))
      await second.promise
    })
    expect(result.current.state.status).toBe('success')
  })

  it('treats a return to the same attempt as a new selection lifetime that a late answer of the first visit cannot settle', async () => {
    const stale = deferred<AgentAttemptEvidenceResponse>()
    let reads = 0
    install((runId) => (++reads === 1 ? stale.promise : Promise.resolve(attemptEvidenceFor(runId, 4, ['AgentFinalResponse']))))
    const { result, rerender } = mounted(target('selection-1', 'run-a', 4))
    rerender(target('selection-2', 'run-a', 4))
    await waitFor(() => expect(result.current.state.status).toBe('success'))

    await act(async () => {
      stale.resolve(attemptEvidenceFor('run-a', 4, ['AgentStandardError']))
      await stale.promise
    })
    const state = result.current.state
    expect(state.status === 'success' ? state.evidence.artifacts?.map((artifact) => artifact.purpose) : null).toEqual(['AgentFinalResponse'])
  })

  it('starts nothing from the retry of an ended selection or an unmounted detail', async () => {
    const get = install(() => Promise.reject(new Error('offline')))
    const { result, rerender, unmount } = mounted(target('selection-1', 'run-a', 4))
    await waitFor(() => expect(result.current.state.status).toBe('error'))
    const retryFirst = result.current.retry

    rerender(target('selection-2', 'run-a', 3))
    await waitFor(() => expect(result.current.state.status).toBe('error'))
    act(() => retryFirst())
    expect(get).toHaveBeenCalledTimes(2)

    const retrySecond = result.current.retry
    unmount()
    act(() => retrySecond())
    expect(get).toHaveBeenCalledTimes(2)
  })
})
