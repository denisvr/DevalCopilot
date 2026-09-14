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

  return (
    <div className="dc-process-output" data-stream={stream} data-final={isFinal}>
      <pre className="dc-process-output-text">{text || (isFinal ? '(empty)' : 'Loading…')}</pre>
      {truncated ? <p className="dc-process-output-truncated">Output was truncated at the capture limit.</p> : null}
    </div>
  )
}
