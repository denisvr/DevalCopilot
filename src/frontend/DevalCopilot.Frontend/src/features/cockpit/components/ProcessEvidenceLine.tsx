import { describeProcessEvidence, type ProcessEvidenceView } from '../describeProcessEvidence'

interface ProcessEvidenceLineProps {
  processExecution: ProcessEvidenceView | null | undefined
  dispatchedAtUtc: Date | string | null | undefined
  status: string | null | undefined
}

/**
 * Renders an attempt's host-measured process evidence on its own line, separate from the
 * semantic outcome line each action already shows. Only bounded facts are rendered — never a
 * path, argument, environment value, output, manifest, session identifier, or credential.
 */
export function ProcessEvidenceLine({ processExecution, dispatchedAtUtc, status }: ProcessEvidenceLineProps) {
  const text = describeProcessEvidence(processExecution, {
    dispatched: Boolean(dispatchedAtUtc),
    running: status === 'Running',
  })

  return (
    <p className="dc-process-evidence" data-process-outcome={processExecution?.outcome ?? 'Unknown'}>
      {text}
    </p>
  )
}
