import type { AgentAttemptStatusResponse } from '../../../api/clients'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'

interface CodexPlanningActionProps {
  status: AgentAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
}

const OUTCOME_LABEL: Record<string, string> = {
  Proposed: 'Plan proposed',
  SourceChanged: 'Source changed before the plan completed',
  InvalidStructuredOutput: 'Codex returned an invalid structured response',
  ProviderInvocationFailed: 'Codex could not be invoked',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
}

function phaseLabel(status: AgentAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

/**
 * The one durable action this slice exposes: request a read-only Codex planning attempt.
 * A validated Proposal appears through the existing collaboration timeline once recorded —
 * this component never claims Claude has reviewed it.
 */
export function CodexPlanningAction({
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
}: CodexPlanningActionProps) {
  const isActive = status?.status === 'Running'

  return (
    <section className="dc-codex-planning-action" aria-label="Codex planning">
      {isActive ? (
        <p className="dc-codex-planning-status" aria-busy="true">
          Codex plan {phaseLabel(status).toLowerCase()}…
        </p>
      ) : (
        <button
          type="button"
          className="dc-codex-planning-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Request Codex plan'}
        </button>
      )}
      {status && !isActive && (
        <p className="dc-codex-planning-status">
          Last attempt #{status.attemptNumber}: {phaseLabel(status)}.
        </p>
      )}
      {status && (
        <ProcessEvidenceLine
          processExecution={status.processExecution}
          dispatchedAtUtc={status.dispatchedAtUtc}
          status={status.status}
        />
      )}
      {(requestError ?? statusError) && (
        <p className="dc-codex-planning-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
