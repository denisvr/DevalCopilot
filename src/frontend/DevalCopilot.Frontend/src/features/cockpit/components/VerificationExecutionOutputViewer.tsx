import type { VerificationOutputStream } from '../hooks/useVerificationExecutionOutput'
import { useVerificationExecutionOutput } from '../hooks/useVerificationExecutionOutput'

interface VerificationExecutionOutputViewerProps {
  projectId: string
  executionId: string
  stream: VerificationOutputStream
}

export function VerificationExecutionOutputViewer({ projectId, executionId, stream }: VerificationExecutionOutputViewerProps) {
  const { text, isFinal, error } = useVerificationExecutionOutput(projectId, executionId, stream, true)

  if (error) {
    return <p className="dc-empty-state">{error}</p>
  }

  return (
    <div className="dc-process-output" data-stream={stream} data-final={isFinal}>
      <pre className="dc-process-output-text">{text || (isFinal ? '(empty)' : 'Loading…')}</pre>
    </div>
  )
}
