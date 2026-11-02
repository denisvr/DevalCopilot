import type { AgentAttemptEvidenceResponse } from '../../../api/clients'
import { INVOCATION_DIAGNOSTIC_CAVEAT, describeInvocationDiagnostic, hasIncompleteCapture } from '../describeInvocationDiagnostic'

/**
 * ADR-0072: the recorded closed invocation diagnostic of one attempt, with fixed copy, and whether its recorded output capture was
 * incomplete. Nothing is shown for an attempt that recorded neither; an unreadable value is reported as unavailable, never guessed.
 */
export function InvocationDiagnosticFact({ evidence }: { evidence: AgentAttemptEvidenceResponse }) {
  const view = describeInvocationDiagnostic(evidence.invocationDiagnostic)
  const incomplete = hasIncompleteCapture(evidence.processExecution)

  if (view.status === 'not-recorded' && !incomplete) {
    return null
  }

  return (
    <div className="dc-attempt-history-diagnostic" aria-label="Invocation diagnostic">
      {view.status === 'recorded' && (
        <p>
          Invocation diagnostic ({view.category}): {view.copy} {INVOCATION_DIAGNOSTIC_CAVEAT}
        </p>
      )}
      {view.status === 'unavailable' && <p>The recorded invocation diagnostic could not be read.</p>}
      {incomplete && <p>The recorded output capture did not complete, so this attempt’s output is partial and is not evidence.</p>}
    </div>
  )
}
