import type { ClaudeCriticalReviewAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface ClaudeCriticalReviewActionProps {
  proposalMessageId: string | null
  status: ClaudeCriticalReviewAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Never a positive eligibility signal — see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
}

const OUTCOME_LABEL: Record<string, string> = {
  Accepted: 'Proposal accepted',
  Challenged: 'Proposal challenged',
  SourceChanged: 'Source changed before the review completed',
  InvalidStructuredOutput: 'Claude returned an invalid structured response',
  ProviderInvocationFailed: 'Claude could not be invoked',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
  InputAlreadyReviewed: 'Proposal already reviewed by another attempt',
}

function phaseLabel(status: ClaudeCriticalReviewAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

/**
 * The one durable action this slice exposes: request a read-only Claude critical review of
 * the latest real Codex Proposal. There is nothing to review before a real (provider-observed)
 * Proposal exists, so the whole action is withheld until `proposalMessageId` names one. The
 * request action itself is further withheld once a review of that exact Proposal is already
 * Running or has already reached a durable Accepted/Challenged outcome, so the user can never
 * queue a duplicate review of the same Proposal — a Proposal reviewed earlier and since
 * superseded by a newer one remains requestable again.
 */
export function ClaudeCriticalReviewAction({
  proposalMessageId,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
  globalClaimBlock,
}: ClaudeCriticalReviewActionProps) {
  if (!proposalMessageId) {
    return null
  }

  const reviewsCurrentProposal = status?.reviewedProposalMessageId === proposalMessageId
  const isActive = reviewsCurrentProposal && status?.status === 'Running'
  const isSettledForCurrentProposal =
    reviewsCurrentProposal &&
    (status?.outcome === 'Accepted' || status?.outcome === 'Challenged' || status?.outcome === 'InputAlreadyReviewed')
  const canRequest = !isActive && !isSettledForCurrentProposal

  return (
    <section className="dc-claude-critical-review-action" aria-label="Claude critical review">
      {isActive && (
        <p className="dc-claude-critical-review-status" aria-busy="true">
          Claude critical review {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!isActive && canRequest && globalClaimBlock && (
        <p className="dc-claude-critical-review-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {!isActive && canRequest && !globalClaimBlock && (
        <button
          type="button"
          className="dc-claude-critical-review-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Request Claude review'}
        </button>
      )}
      {status && !isActive && (
        <p className="dc-claude-critical-review-status">
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
        <p className="dc-claude-critical-review-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
