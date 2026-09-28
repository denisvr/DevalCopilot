import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AgentAttemptArtifactMetadataResponse, SealedAgentArtifactWindowResponse } from '../../../api/generated/api-client'
import { sealedAgentArtifactWindowClient } from '../../../api/clients'
import { AttemptArtifactWindowViewer } from './AttemptArtifactWindowViewer'

vi.mock('../../../api/clients', () => ({
  sealedAgentArtifactWindowClient: vi.fn(),
}))

function artifact(purpose: string): AgentAttemptArtifactMetadataResponse {
  return new AgentAttemptArtifactMetadataResponse({
    purpose,
    byteLength: 10,
    truncated: false,
    captureOutcome: 'Captured',
  })
}

function windowResponse(overrides: Partial<SealedAgentArtifactWindowResponse> = {}): SealedAgentArtifactWindowResponse {
  return new SealedAgentArtifactWindowResponse({
    status: 'Ok',
    text: 'hello',
    nextOffset: 5,
    totalLengthSoFar: 5,
    truncated: false,
    ...overrides,
  })
}

function mockClient(getSealedAgentArtifactWindow: ReturnType<typeof vi.fn>) {
  vi.mocked(sealedAgentArtifactWindowClient).mockReturnValue({
    getSealedAgentArtifactWindow,
  } as unknown as ReturnType<typeof sealedAgentArtifactWindowClient>)
}

describe('AttemptArtifactWindowViewer', () => {
  it('renders nothing when no artifact metadata is linked to this attempt', () => {
    mockClient(vi.fn())
    const { container } = render(<AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[]} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('only offers purposes actually present in the attempts own artifact metadata', () => {
    mockClient(vi.fn())
    render(
      <AttemptArtifactWindowViewer
        runId="run-1"
        messageId="message-1"
        artifacts={[artifact('AgentFinalResponse'), artifact('AgentStandardOutput')]}
      />,
    )

    expect(screen.getByRole('option', { name: 'Final response' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Standard output' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Standard error' })).not.toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Context manifest' })).not.toBeInTheDocument()
  })

  it('fetches and renders the verified window as plain text once a purpose is selected and Load is clicked', async () => {
    const getSealedAgentArtifactWindow = vi.fn().mockResolvedValue(windowResponse({ text: '<script>evil()</script>' }))
    mockClient(getSealedAgentArtifactWindow)
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))

    await waitFor(() => expect(screen.getByText('<script>evil()</script>')).toBeInTheDocument())
    expect(getSealedAgentArtifactWindow).toHaveBeenCalledWith('run-1', 'message-1', 'AgentFinalResponse', 0, expect.any(Number))
    // Rendered as literal text content, never parsed/executed markup.
    expect(document.querySelector('script')).not.toBeInTheDocument()
  })

  it('loads the next window and appends to the accumulated text when the window is incomplete', async () => {
    const getSealedAgentArtifactWindow = vi
      .fn()
      .mockResolvedValueOnce(windowResponse({ text: 'part one ', nextOffset: 9, totalLengthSoFar: 18 }))
      .mockResolvedValueOnce(windowResponse({ text: 'part two', nextOffset: 18, totalLengthSoFar: 18 }))
    mockClient(getSealedAgentArtifactWindow)
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText('part one')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Load next window' }))
    await waitFor(() => expect(screen.getByText('part one part two')).toBeInTheDocument())
    expect(getSealedAgentArtifactWindow).toHaveBeenNthCalledWith(2, 'run-1', 'message-1', 'AgentFinalResponse', 9, expect.any(Number))
    expect(screen.queryByRole('button', { name: 'Load next window' })).not.toBeInTheDocument()
  })

  // Regression: a naive retry that always restarts at offset 0 would re-fetch and re-append the
  // already-accumulated first window's text on top of itself once the retried second window
  // succeeds, duplicating "part one". Retrying must re-request the exact offset that failed and
  // must never touch the text already accumulated from the first, successful window.
  it('retries a failed later window at its own failed offset without duplicating or omitting earlier text', async () => {
    const getSealedAgentArtifactWindow = vi
      .fn()
      .mockResolvedValueOnce(windowResponse({ text: 'part one ', nextOffset: 9, totalLengthSoFar: 18 }))
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(windowResponse({ text: 'part two', nextOffset: 18, totalLengthSoFar: 18 }))
    mockClient(getSealedAgentArtifactWindow)
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText('part one')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Load next window' }))
    await waitFor(() => expect(screen.getByText('This artifact window is unavailable.')).toBeInTheDocument())
    // The failed window's own text is never shown as if it had loaded, and the earlier
    // successfully-loaded text is not discarded just because a later request failed.
    expect(screen.queryByText('part one part two')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.getByText('part one part two')).toBeInTheDocument())

    expect(getSealedAgentArtifactWindow).toHaveBeenCalledTimes(3)
    expect(getSealedAgentArtifactWindow).toHaveBeenNthCalledWith(1, 'run-1', 'message-1', 'AgentFinalResponse', 0, expect.any(Number))
    expect(getSealedAgentArtifactWindow).toHaveBeenNthCalledWith(2, 'run-1', 'message-1', 'AgentFinalResponse', 9, expect.any(Number))
    // The retry re-requests the SAME offset (9) the failed attempt requested — never 0 (which
    // would re-fetch and re-append the first window) and never a different, guessed offset.
    expect(getSealedAgentArtifactWindow).toHaveBeenNthCalledWith(3, 'run-1', 'message-1', 'AgentFinalResponse', 9, expect.any(Number))
  })

  it('resets accumulated text and offset when the selected purpose changes', async () => {
    const getSealedAgentArtifactWindow = vi
      .fn()
      .mockResolvedValueOnce(windowResponse({ text: 'stdout text' }))
      .mockResolvedValueOnce(windowResponse({ text: 'final text' }))
    mockClient(getSealedAgentArtifactWindow)
    render(
      <AttemptArtifactWindowViewer
        runId="run-1"
        messageId="message-1"
        artifacts={[artifact('AgentFinalResponse'), artifact('AgentStandardOutput')]}
      />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentStandardOutput' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText('stdout text')).toBeInTheDocument())

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    expect(screen.queryByText('stdout text')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText('final text')).toBeInTheDocument())
    expect(screen.queryByText('stdout text')).not.toBeInTheDocument()
  })

  it('shows a distinct unavailable state for NoAgentEvidence/AttemptLinkBroken/PurposeNotAllowlisted/ArtifactNotFound', async () => {
    mockClient(vi.fn().mockResolvedValue(windowResponse({ status: 'ArtifactNotFound', text: '' })))
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))

    await waitFor(() =>
      expect(screen.getByText('No sealed artifact is available for this purpose.')).toBeInTheDocument(),
    )
  })

  it('shows a distinct missing state without disclosing any path or hash', async () => {
    mockClient(vi.fn().mockResolvedValue(windowResponse({ status: 'Missing', text: '' })))
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))

    await waitFor(() =>
      expect(screen.getByText("This artifact's sealed file could not be verified and is not shown.")).toBeInTheDocument(),
    )
  })

  it('shows a distinct integrity-mismatch state', async () => {
    mockClient(vi.fn().mockResolvedValue(windowResponse({ status: 'IntegrityMismatch', text: '' })))
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))

    await waitFor(() =>
      expect(screen.getByText('This artifact failed integrity verification and is not shown.')).toBeInTheDocument(),
    )
  })

  it('shows an error state with a retry action that fetches again', async () => {
    const getSealedAgentArtifactWindow = vi
      .fn()
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(windowResponse({ text: 'recovered' }))
    mockClient(getSealedAgentArtifactWindow)
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText('This artifact window is unavailable.')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.getByText('recovered')).toBeInTheDocument())
    expect(getSealedAgentArtifactWindow).toHaveBeenCalledTimes(2)
  })

  it('never writes fetched text to browser-persisted state or the URL', async () => {
    localStorage.clear()
    sessionStorage.clear()
    const sentinel = 'SENTINEL-artifact-window-do-not-persist-me'
    mockClient(vi.fn().mockResolvedValue(windowResponse({ text: sentinel })))
    render(
      <AttemptArtifactWindowViewer runId="run-1" messageId="message-1" artifacts={[artifact('AgentFinalResponse')]} />,
    )

    fireEvent.change(screen.getByLabelText('Artifact:'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load' }))
    await waitFor(() => expect(screen.getByText(sentinel)).toBeInTheDocument())

    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
    expect(window.location.href).not.toContain(sentinel)
  })
})
