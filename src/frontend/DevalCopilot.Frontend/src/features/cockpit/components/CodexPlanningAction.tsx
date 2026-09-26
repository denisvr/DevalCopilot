import type { AgentAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { describeAgentClaimPathTimeFitBlock, isAgentClaimPathTimeFitBlocking } from '../deriveAgentClaimPathTimeFit'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface CodexPlanningActionProps {
  status: AgentAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Never a positive eligibility signal — see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
  /** The advisory, candidate-specific time-fit result for THIS claim path (ADR-0013's
   * companion candidate-fit projection) — separate from, and combined with, `globalClaimBlock`.
   * Never a positive eligibility signal on its own — see `deriveAgentClaimPathTimeFit`. */
  timeFit: AgentClaimPathTimeFit
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
  globalClaimBlock,
  timeFit,
}: CodexPlanningActionProps) {
  const isActive = status?.status === 'Running'
  const timeFitBlocked = isAgentClaimPathTimeFitBlocking(timeFit)

  return (
    <section className="dc-codex-planning-action" aria-label="Codex planning">
      {isActive ? (
        <p className="dc-codex-planning-status" aria-busy="true">
          Codex plan {phaseLabel(status).toLowerCase()}…
        </p>
      ) : (
        <>
          {globalClaimBlock && (
            <p className="dc-codex-planning-global-block" role="status">
              {describeGlobalAgentClaimBlock(globalClaimBlock)}
            </p>
          )}
          {timeFitBlocked && (
            <p className="dc-codex-planning-time-fit-block" role="status">
              {describeAgentClaimPathTimeFitBlock(timeFit)}
            </p>
          )}
          {!globalClaimBlock && !timeFitBlocked && (
            <button
              type="button"
              className="dc-codex-planning-request"
              disabled={requesting || statusLoading}
              onClick={onRequest}
            >
              {requesting ? 'Requesting…' : 'Request Codex plan'}
            </button>
          )}
        </>
      )}
      {status && !isActive && (
        <p className="dc-codex-planning-status">
          Last attempt #{status.attemptNumber}: {phaseLabel(status)}.
        </p>
      )}
      {status && (
        <>
          <ProcessEvidenceLine
            processExecution={status.processExecution}
            dispatchedAtUtc={status.dispatchedAtUtc}
            status={status.status}
          />
          <TokenUsageLine tokenUsage={status.tokenUsage} dispatchedAtUtc={status.dispatchedAtUtc} status={status.status} />
        </>
      )}
      {(requestError ?? statusError) && (
        <p className="dc-codex-planning-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
