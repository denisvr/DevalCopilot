import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CollaborationMessageEvidenceResponse } from '../../../api/generated/api-client'
import { collaborationMessageEvidenceClient } from '../../../api/clients'
import { CollaborationEvidenceDrilldown } from './CollaborationEvidenceDrilldown'

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
    attemptNumber: 3,
    attemptKind: 'Agent',
    attemptStatus: 'Completed',
    agentProvider: 'ClaudeCode',
    agentRole: 'Planner',
    agentResponseContract: 'Proposal',
    agentOutcome: 'Proposed',
    agentDispatchedAtUtc: new Date('2026-09-24T10:00:00Z') as never,
    startingGitCheckpointId: 'checkpoint-1',
    artifacts: [],
    artifactsOmitted: false,
    artifactTotalCount: 0,
    ...overrides,
  })
}

function mockClient(getCollaborationMessageEvidence: ReturnType<typeof vi.fn>) {
  vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
    getCollaborationMessageEvidence,
  } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)
}

describe('CollaborationEvidenceDrilldown', () => {
  it('shows only the collapsed control before it is expanded', () => {
    mockClient(vi.fn())
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    expect(screen.getByText('Attempt evidence')).toBeInTheDocument()
    expect(screen.queryByText(/Historical attempt evidence/)).not.toBeInTheDocument()
  })

  it('shows a loading state while the fetch is in flight', async () => {
    const pending = deferred<CollaborationMessageEvidenceResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))

    await waitFor(() => expect(screen.getByText('Loading attempt evidence…')).toBeInTheDocument())
  })

  it('renders the bounded evidence with an explicit historical-evidence caveat once loaded', async () => {
    mockClient(vi.fn().mockResolvedValue(evidence()))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))

    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())
    expect(screen.getByText(/not a current approval/)).toBeInTheDocument()
    expect(screen.getByText(/not verification against the run's current source state/)).toBeInTheDocument()
    expect(screen.getByText('ClaudeCode')).toBeInTheDocument()
    expect(screen.getByText('Planner')).toBeInTheDocument()
  })

  it('shows a distinct unavailable state, not an error, when the message legitimately has no agent evidence', async () => {
    mockClient(vi.fn().mockResolvedValue(evidence({ evidenceStatus: 'NoAgentEvidence', attemptId: undefined })))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))

    await waitFor(() => expect(screen.getByText('This message has no linked Agent attempt.')).toBeInTheDocument())
  })

  it('shows a distinct broken-link state and never the historical-evidence success panel for it', async () => {
    mockClient(vi.fn().mockResolvedValue(evidence({ evidenceStatus: 'AttemptLinkBroken', attemptId: undefined })))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))

    await waitFor(() =>
      expect(
        screen.getByText("This message's Agent attempt evidence could not be verified and is not shown."),
      ).toBeInTheDocument(),
    )
    expect(screen.queryByText(/Historical attempt evidence/)).not.toBeInTheDocument()
    expect(screen.queryByText('This message has no linked Agent attempt.')).not.toBeInTheDocument()
  })

  it('displays starting/result checkpoint fingerprints and the configured timeout, all labeled historical', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        evidence({
          startingGitCheckpointId: 'checkpoint-start',
          startingCheckpointFingerprintSha256: 'a'.repeat(64),
          resultGitCheckpointId: 'checkpoint-result',
          resultCheckpointFingerprintSha256: 'b'.repeat(64),
          processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 600000 } as never,
        }),
      ),
    )
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(screen.getByText('Starting checkpoint fingerprint (historical)')).toBeInTheDocument()
    expect(screen.getByText('a'.repeat(64))).toBeInTheDocument()
    expect(screen.getByText('Result checkpoint fingerprint (historical)')).toBeInTheDocument()
    expect(screen.getByText('b'.repeat(64))).toBeInTheDocument()
    expect(screen.getByText('Configured timeout (historical)')).toBeInTheDocument()
    expect(screen.getByText('600000 ms')).toBeInTheDocument()
    expect(screen.getByText('Process duration (historical)')).toBeInTheDocument()
    expect(screen.getByText('1234 ms')).toBeInTheDocument()
  })

  it('omits the result checkpoint fingerprint when the backend reports it as unresolved, without fabricating one', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        evidence({
          resultGitCheckpointId: 'checkpoint-result',
          resultCheckpointFingerprintSha256: undefined,
        }),
      ),
    )
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(screen.getByText('checkpoint-result')).toBeInTheDocument()
    expect(screen.queryByText('Result checkpoint fingerprint (historical)')).not.toBeInTheDocument()
  })

  // Regression: this component renders processExecution.outcome/durationMilliseconds directly
  // rather than through ProcessEvidenceLine, so it must reuse the same shared trust check
  // (hasTrustedProcessEvidence) rather than trusting the raw object. A well-formed-looking
  // processExecution paired with a still-Running attemptStatus must never be shown as if the
  // process had concluded — the configured timeout stays visible regardless, since it is a
  // separate, always-known value.
  it('never shows process outcome/duration for a still-running attempt, even with a well-formed-looking object', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        evidence({
          attemptStatus: 'Running',
          processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 600000 } as never,
        }),
      ),
    )
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(screen.getByText('Configured timeout (historical)')).toBeInTheDocument()
    expect(screen.getByText('600000 ms')).toBeInTheDocument()
    expect(screen.queryByText('Process outcome (historical)')).not.toBeInTheDocument()
    expect(screen.queryByText('Process duration (historical)')).not.toBeInTheDocument()
  })

  it('never shows process outcome/duration for an undispatched attempt, even with a well-formed-looking object', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        evidence({
          agentDispatchedAtUtc: undefined,
          processExecution: { outcome: 'TimedOut', durationMilliseconds: 1234, timeoutMilliseconds: 600000 } as never,
        }),
      ),
    )
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(screen.queryByText('Process outcome (historical)')).not.toBeInTheDocument()
    expect(screen.queryByText('Process duration (historical)')).not.toBeInTheDocument()
  })

  it('shows an error state with a retry action that fetches again', async () => {
    const getCollaborationMessageEvidence = vi.fn().mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(evidence())
    mockClient(getCollaborationMessageEvidence)
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText('Attempt evidence is unavailable.')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())
    expect(getCollaborationMessageEvidence).toHaveBeenCalledTimes(2)
  })

  it('never renders a raw output viewer — only bounded metadata fields', async () => {
    mockClient(vi.fn().mockResolvedValue(evidence()))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(screen.queryByText(/stdout/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/stderr/i)).not.toBeInTheDocument()
  })

  it('never writes fetched evidence to browser-persisted state or the URL', async () => {
    localStorage.clear()
    sessionStorage.clear()
    document.cookie = ''
    const sentinel = 'SENTINEL-drilldown-do-not-persist-me'
    mockClient(vi.fn().mockResolvedValue(evidence({ agentOutcome: sentinel })))
    render(<CollaborationEvidenceDrilldown runId="run-1" messageId="message-1" />)

    fireEvent.click(screen.getByText('Attempt evidence'))
    await waitFor(() => expect(screen.getByText(/Historical attempt evidence/)).toBeInTheDocument())

    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
    expect(document.cookie).not.toContain(sentinel)
    expect(window.location.href).not.toContain(sentinel)
  })
})
