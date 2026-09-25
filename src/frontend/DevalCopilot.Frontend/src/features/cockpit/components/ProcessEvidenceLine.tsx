import { describeProcessEvidence, hasTrustedProcessEvidence, type ProcessEvidenceView } from '../describeProcessEvidence'

interface ProcessEvidenceLineProps {
  processExecution: ProcessEvidenceView | null | undefined
  dispatchedAtUtc: Date | string | null | undefined
  status: string | null | undefined
}

/**
 * Renders an attempt's host-measured process evidence on its own line, separate from the
 * semantic outcome line each action already shows. Only bounded facts are rendered — never a
 * path, argument, environment value, output, manifest, session identifier, or credential.
 *
 * The `data-process-outcome` test hook always agrees with the rendered text: it reflects the raw
 * outcome only when that evidence is actually trusted for this attempt's dispatch/running state
 * (see `hasTrustedProcessEvidence`), and reports `Unknown` otherwise — never the tampered or
 * premature raw value from a still-running or undispatched attempt's object.
 */
export function ProcessEvidenceLine({ processExecution, dispatchedAtUtc, status }: ProcessEvidenceLineProps) {
  const context = {
    dispatched: Boolean(dispatchedAtUtc),
    running: status === 'Running',
  }
  const text = describeProcessEvidence(processExecution, context)
  const trusted = hasTrustedProcessEvidence(processExecution, context)

  return (
    <p className="dc-process-evidence" data-process-outcome={trusted ? processExecution.outcome ?? 'Unknown' : 'Unknown'}>
      {text}
    </p>
  )
}
