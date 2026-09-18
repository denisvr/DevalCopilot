import type { ProcessOutputStream } from '../hooks/useProcessAttemptOutput'
import { useProcessAttemptOutput } from '../hooks/useProcessAttemptOutput'

interface ProcessAttemptOutputViewerProps {
  runId: string
  attemptId: string
  stream: ProcessOutputStream
}

/**
 * Bounded, on-demand evidence view for one Process attempt's captured stream — never inlined
 * into a run/project summary. Only ever shows redacted, capped text the API already verified;
 * never raw process output.
 */
export function ProcessAttemptOutputViewer({ runId, attemptId, stream }: ProcessAttemptOutputViewerProps) {
  const { text, status, isFinal, truncated, error } = useProcessAttemptOutput(runId, attemptId, stream)

  if (error) {
    return <p className="dc-empty-state">{error}</p>
  }

  if (status === 'IntegrityMismatch') {
    return <p className="dc-empty-state">This output failed an integrity check and cannot be shown.</p>
  }

  if (status === 'NoOutputAvailable') {
    return <p className="dc-empty-state">No output was captured for this stream.</p>
  }

  // A `null`/`undefined` truncation only ever occurs once the underlying artifact is final
  // (a sealed artifact recovered from a host interruption, whose truncation at the point of
  // interruption was never observed) — never during the initial pre-response loading state,
  // and never for a live in-progress capture (always a definite `false`). Gating on `isFinal`
  // keeps this indicator from ever flashing before the first poll response arrives.
  const truncationUnknown = isFinal && (truncated === undefined || truncated === null)

  return (
    <div className="dc-process-output" data-stream={stream} data-final={isFinal}>
      <pre className="dc-process-output-text">{text || (isFinal ? '(empty)' : 'Loading…')}</pre>
      {truncated === true ? (
        <p className="dc-process-output-truncated">Output was truncated at the capture limit.</p>
      ) : null}
      {truncationUnknown ? (
        <p className="dc-process-output-truncation-unknown">
          Truncation status unknown (recovered after an interruption).
        </p>
      ) : null}
    </div>
  )
}
