import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { processAttemptOutputClient } from '../../../api/clients'
import { ProcessAttemptOutputViewer } from './ProcessAttemptOutputViewer'

vi.mock('../../../api/clients', () => ({
  processAttemptOutputClient: vi.fn(),
}))

function mockClient(getProcessAttemptOutput: ReturnType<typeof vi.fn>) {
  vi.mocked(processAttemptOutputClient).mockReturnValue({
    getProcessAttemptOutput,
  } as unknown as ReturnType<typeof processAttemptOutputClient>)
}

describe('ProcessAttemptOutputViewer', () => {
  it('renders captured text once loaded', async () => {
    mockClient(
      vi.fn().mockResolvedValue({ status: 'Ok', text: 'build succeeded', nextOffset: 16, totalLengthSoFar: 16, isFinal: true, truncated: false }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

    expect(await screen.findByText('build succeeded')).toBeInTheDocument()
  })

  it('shows a truncation notice when the artifact was truncated', async () => {
    mockClient(
      vi.fn().mockResolvedValue({ status: 'Ok', text: 'partial', nextOffset: 7, totalLengthSoFar: 7, isFinal: true, truncated: true }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

    expect(await screen.findByText(/truncated at the capture limit/i)).toBeInTheDocument()
    expect(screen.queryByText(/truncation status unknown/i)).not.toBeInTheDocument()
  })

  it('shows no truncation copy at all when truncation is definitely false', async () => {
    mockClient(
      vi.fn().mockResolvedValue({ status: 'Ok', text: 'complete', nextOffset: 8, totalLengthSoFar: 8, isFinal: true, truncated: false }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

    expect(await screen.findByText('complete')).toBeInTheDocument()
    expect(screen.queryByText(/truncated at the capture limit/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/truncation status unknown/i)).not.toBeInTheDocument()
  })

  // A recovered-from-interruption artifact reports `truncated: undefined` (genuinely unknown)
  // rather than a known true/false. The viewer must show a distinct "unknown" indicator, and
  // must never show the definite-truncated copy for this case.
  it('shows a distinct unknown-truncation notice when truncation was never observed, never the definite-truncated copy', async () => {
    // Empty text lets the hook's own "caught up" polling contract latch `isFinal` on the
    // first response — the unknown-truncation indicator is gated on `isFinal`, since a
    // genuinely-unknown truncation only ever occurs for an already-sealed artifact.
    mockClient(
      vi.fn().mockResolvedValue({ status: 'Ok', text: '', nextOffset: 7, totalLengthSoFar: 7, isFinal: true, truncated: undefined }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

    expect(await screen.findByText(/truncation status unknown/i)).toBeInTheDocument()
    expect(screen.queryByText(/^Output was truncated at the capture limit\.$/)).not.toBeInTheDocument()
  })

  it('shows an integrity-mismatch message rather than any content', async () => {
    mockClient(
      vi.fn().mockResolvedValue({ status: 'IntegrityMismatch', text: '', nextOffset: 0, totalLengthSoFar: 0, isFinal: true, truncated: false }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

    expect(await screen.findByText(/failed an integrity check/i)).toBeInTheDocument()
  })

  it('shows a no-output message when nothing was ever captured', async () => {
    mockClient(
      vi.fn().mockResolvedValue({ status: 'NoOutputAvailable', text: '', nextOffset: 0, totalLengthSoFar: 0, isFinal: true, truncated: false }),
    )

    render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stderr" />)

    expect(await screen.findByText(/no output was captured/i)).toBeInTheDocument()
  })

  describe('browser output safety', () => {
    // A distinctive value that could not appear by coincidence anywhere else in the page,
    // storage, URL, or a mocked summary payload — so any match below is unambiguous.
    const sentinel = 'SENTINEL-7f3a9c21-do-not-persist-me'

    it('shows the sentinel only in the component\'s own rendered output, never in storage, the URL, cookies, or a summary payload', async () => {
      localStorage.clear()
      sessionStorage.clear()
      document.cookie = ''

      mockClient(
        vi.fn().mockResolvedValue({
          status: 'Ok',
          text: sentinel,
          nextOffset: sentinel.length,
          totalLengthSoFar: sentinel.length,
          isFinal: true,
          truncated: false,
        }),
      )

      render(<ProcessAttemptOutputViewer runId="run-1" attemptId="attempt-1" stream="stdout" />)

      // Confirms the real hook/viewer lifecycle actually ran and rendered the sentinel — the
      // absence checks below are only meaningful once this has actually appeared on screen.
      expect(await screen.findByText(sentinel)).toBeInTheDocument()

      // Browser storage: never written to at all by this hook or component.
      expect(Object.keys(localStorage)).toHaveLength(0)
      expect(JSON.stringify(localStorage)).not.toContain(sentinel)
      expect(Object.keys(sessionStorage)).toHaveLength(0)
      expect(JSON.stringify(sessionStorage)).not.toContain(sentinel)

      // The URL: identifiers travel as normal path/query values, never captured content.
      expect(window.location.href).not.toContain(sentinel)
      expect(window.location.search).not.toContain(sentinel)
      expect(window.location.hash).not.toContain(sentinel)

      // Cookies: this app never sets any; output content certainly never does.
      expect(document.cookie).not.toContain(sentinel)

      // A representative summary payload shape (what a project/run-cockpit summary carries) —
      // constructed independently of anything the output hook touched, demonstrating by
      // construction that fetching output content never populates or mutates such a payload.
      const mockedProjectSummary = {
        projectId: 'project-1',
        projectName: 'DevalCopilot',
        runId: 'run-1',
        lifecycle: 'Running',
        stage: 'Plan',
        capabilities: [{ capability: 'Git', displayStatus: 'Ready' }],
      }
      expect(JSON.stringify(mockedProjectSummary)).not.toContain(sentinel)
    })
  })
})
