// @vitest-environment jsdom
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CollaborationMessageEvidenceResponse } from '../../../api/generated/api-client'
import { collaborationMessageEvidenceClient } from '../../../api/clients'
import { useCollaborationMessageEvidence } from './useCollaborationMessageEvidence'

vi.mock('../../../api/clients', () => ({
  collaborationMessageEvidenceClient: vi.fn(),
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

function evidence(overrides: Partial<CollaborationMessageEvidenceResponse> = {}): CollaborationMessageEvidenceResponse {
  return new CollaborationMessageEvidenceResponse({
    evidenceStatus: 'HasEvidence',
    attemptId: 'attempt-1',
    attemptNumber: 1,
    attemptKind: 'Agent',
    attemptStatus: 'Completed',
    agentProvider: 'ClaudeCode',
    agentRole: 'Planner',
    agentResponseContract: 'Proposal',
    agentOutcome: 'Proposed',
    artifacts: [],
    artifactsOmitted: false,
    artifactTotalCount: 0,
    ...overrides,
  })
}

describe('useCollaborationMessageEvidence', () => {
  it('starts idle and never fetches until fetchEvidence is called', () => {
    const getCollaborationMessageEvidence = vi.fn()
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))

    expect(result.current.state).toEqual({ status: 'idle' })
    expect(getCollaborationMessageEvidence).not.toHaveBeenCalled()
  })

  it('fetches using exactly the hook own runId and messageId', async () => {
    const getCollaborationMessageEvidence = vi.fn().mockResolvedValue(evidence())
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-42', 'message-99'))
    act(() => result.current.fetchEvidence())

    expect(result.current.state.status).toBe('loading')
    await waitFor(() => expect(result.current.state.status).toBe('success'))
    expect(getCollaborationMessageEvidence).toHaveBeenCalledWith('run-42', 'message-99')
    expect(getCollaborationMessageEvidence).toHaveBeenCalledTimes(1)
  })

  it('maps an explicit NoAgentEvidence response to the unavailable state, not an error', async () => {
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence: vi.fn().mockResolvedValue(evidence({ evidenceStatus: 'NoAgentEvidence', attemptId: undefined })),
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))
    act(() => result.current.fetchEvidence())

    await waitFor(() => expect(result.current.state.status).toBe('unavailable'))
  })

  it('reports a failed fetch as an error state with a retry available', async () => {
    const getCollaborationMessageEvidence = vi.fn().mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(evidence())
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))
    act(() => result.current.fetchEvidence())

    await waitFor(() => expect(result.current.state.status).toBe('error'))

    act(() => result.current.fetchEvidence())
    await waitFor(() => expect(result.current.state.status).toBe('success'))
    expect(getCollaborationMessageEvidence).toHaveBeenCalledTimes(2)
  })

  it('resets to idle and ignores a stale in-flight response when runId changes', async () => {
    const first = deferred<CollaborationMessageEvidenceResponse>()
    const getCollaborationMessageEvidence = vi.fn().mockReturnValueOnce(first.promise)
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result, rerender } = renderHook(
      ({ runId, messageId }) => useCollaborationMessageEvidence(runId, messageId),
      { initialProps: { runId: 'run-1', messageId: 'message-1' } },
    )
    act(() => result.current.fetchEvidence())
    expect(result.current.state.status).toBe('loading')

    rerender({ runId: 'run-2', messageId: 'message-1' })
    expect(result.current.state).toEqual({ status: 'idle' })

    await act(async () => {
      first.resolve(evidence())
    })
    // The stale run-1 response must never be applied once run-2 is current.
    expect(result.current.state).toEqual({ status: 'idle' })
  })

  it('resets to idle when messageId changes even if runId stays the same', () => {
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence: vi.fn().mockResolvedValue(evidence()),
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result, rerender } = renderHook(
      ({ runId, messageId }) => useCollaborationMessageEvidence(runId, messageId),
      { initialProps: { runId: 'run-1', messageId: 'message-1' } },
    )
    act(() => result.current.fetchEvidence())

    rerender({ runId: 'run-1', messageId: 'message-2' })
    expect(result.current.state).toEqual({ status: 'idle' })
  })

  it('maps an explicit AttemptLinkBroken response to a distinct broken state, never success or unavailable', async () => {
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence: vi.fn().mockResolvedValue(evidence({ evidenceStatus: 'AttemptLinkBroken', attemptId: undefined })),
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))
    act(() => result.current.fetchEvidence())

    await waitFor(() => expect(result.current.state.status).toBe('broken'))
    expect(result.current.state.status).not.toBe('success')
    expect(result.current.state.status).not.toBe('unavailable')
  })

  it('rejects an evidenceStatus it does not recognize as an error, never rendering it as the success panel', async () => {
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      // A hypothetical future/unknown status this client version has never seen — must fail
      // closed, not be treated as a known-safe state.
      getCollaborationMessageEvidence: vi.fn().mockResolvedValue(evidence({ evidenceStatus: 'SomeFutureStatus' })),
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))
    act(() => result.current.fetchEvidence())

    await waitFor(() => expect(result.current.state.status).toBe('error'))
  })

  it('resets to idle and ignores a stale in-flight response when messageId changes, even with a deferred response', async () => {
    const first = deferred<CollaborationMessageEvidenceResponse>()
    const getCollaborationMessageEvidence = vi.fn().mockReturnValueOnce(first.promise)
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result, rerender } = renderHook(
      ({ runId, messageId }) => useCollaborationMessageEvidence(runId, messageId),
      { initialProps: { runId: 'run-1', messageId: 'message-1' } },
    )
    act(() => result.current.fetchEvidence())
    expect(result.current.state.status).toBe('loading')

    rerender({ runId: 'run-1', messageId: 'message-2' })
    expect(result.current.state).toEqual({ status: 'idle' })

    await act(async () => {
      first.resolve(evidence())
    })
    expect(result.current.state).toEqual({ status: 'idle' })
  })

  it('never paints a previous card/run pair evidence, even transiently, when switching before a deferred fetch resolves', async () => {
    const first = deferred<CollaborationMessageEvidenceResponse>()
    const getCollaborationMessageEvidence = vi.fn().mockReturnValueOnce(first.promise)
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence,
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    // Renders only what the DOM actually committed — a render-time state adjustment can invoke a
    // component's body more than once for a single visible update, so asserting on the COMMITTED
    // output (not a side-effect counter incremented from inside render) is what proves nothing
    // stale was ever painted, independent of how many times the function body itself ran.
    const sentinelOutcome = 'SENTINEL-stale-run-1-outcome'
    function Harness({ runId, messageId }: { runId: string; messageId: string }) {
      const { state, fetchEvidence } = useCollaborationMessageEvidence(runId, messageId)
      return (
        <div>
          <button type="button" onClick={() => fetchEvidence()}>
            fetch
          </button>
          <p data-testid="status">{state.status}</p>
          {state.status === 'success' && <p data-testid="outcome">{state.evidence.agentOutcome}</p>}
        </div>
      )
    }

    const { getByRole, getByTestId, queryByTestId, rerender } = render(
      <Harness runId="run-1" messageId="message-1" />,
    )
    act(() => getByRole('button').click())
    expect(getByTestId('status').textContent).toBe('loading')

    rerender(<Harness runId="run-2" messageId="message-2" />)
    // Immediately after the switch, and before the stale run-1/message-1 promise ever resolves,
    // the committed DOM must show idle for the new pair — never the prior loading/evidence state.
    expect(getByTestId('status').textContent).toBe('idle')
    expect(queryByTestId('outcome')).toBeNull()

    await act(async () => {
      first.resolve(evidence({ agentOutcome: sentinelOutcome }))
    })

    // The stale run-1/message-1 response must never reach the DOM for the new (run-2, message-2)
    // pair — status stays idle (no fetch was ever issued for the new pair) and the sentinel
    // outcome from the dropped response never appears anywhere in the committed document.
    expect(getByTestId('status').textContent).toBe('idle')
    expect(queryByTestId('outcome')).toBeNull()
    expect(document.body.textContent).not.toContain(sentinelOutcome)
  })

  it('never copies fetched evidence into browser-persisted state or the URL', async () => {
    localStorage.clear()
    sessionStorage.clear()
    document.cookie = ''
    const sentinel = 'SENTINEL-evidence-do-not-persist-me'
    vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
      getCollaborationMessageEvidence: vi.fn().mockResolvedValue(evidence({ agentOutcome: sentinel })),
    } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

    const { result } = renderHook(() => useCollaborationMessageEvidence('run-1', 'message-1'))
    act(() => result.current.fetchEvidence())

    await waitFor(() => expect(result.current.state.status).toBe('success'))
    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
    expect(document.cookie).not.toContain(sentinel)
    expect(window.location.href).not.toContain(sentinel)
  })
})
