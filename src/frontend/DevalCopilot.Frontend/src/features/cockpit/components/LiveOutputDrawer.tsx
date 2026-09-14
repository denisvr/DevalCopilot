import { ProcessAttemptOutputViewer } from './ProcessAttemptOutputViewer'

interface LiveOutputDrawerProps {
  runId: string
  /** The current Process attempt to show output for, or `null` for a Simulated-only run. */
  attemptId: string | null
}

/**
 * The bounded live-output drawer. Renders the real captured stdout/stderr for the current
 * Process attempt once one exists; a Simulated-only run has no real output to stream, so
 * the drawer states that actual (empty) condition rather than fabricating log lines.
 */
export function LiveOutputDrawer({ runId, attemptId }: LiveOutputDrawerProps) {
  if (!attemptId) {
    return (
      <div className="dc-live-output" aria-label="Live output">
        Live output: not yet collected for the simulated adapter.
      </div>
    )
  }

  return (
    <div className="dc-live-output" data-has-attempt="true" aria-label="Live output">
      <div className="dc-live-output-stream">
        <h3 className="dc-live-output-label">stdout</h3>
        <ProcessAttemptOutputViewer runId={runId} attemptId={attemptId} stream="stdout" />
      </div>
      <div className="dc-live-output-stream">
        <h3 className="dc-live-output-label">stderr</h3>
        <ProcessAttemptOutputViewer runId={runId} attemptId={attemptId} stream="stderr" />
      </div>
    </div>
  )
}
