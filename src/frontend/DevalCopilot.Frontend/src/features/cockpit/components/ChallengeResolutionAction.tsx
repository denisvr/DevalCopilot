import type { ChallengeResolutionAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { describeAgentClaimPathTimeFitBlock, isAgentClaimPathTimeFitBlocking } from '../deriveAgentClaimPathTimeFit'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface ChallengeResolutionActionProps {
  challengedReviewAttemptId: string | null
  reviewedProposalMessageId: string | null
  status: ChallengeResolutionAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Never a positive eligibility signal — see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
  /** The advisory, candidate-specific time-fit result for THIS claim path — separate from, and
   * combined with, `globalClaimBlock`. See `deriveAgentClaimPathTimeFit`. */
  timeFit: AgentClaimPathTimeFit
}

const OUTCOME_LABEL: Record<string, string> = {
  Resolved: 'Challenges resolved',
  SourceChanged: 'Source changed before the resolution completed',
  InvalidStructuredOutput: 'Codex returned an invalid structured response',
  ProviderInvocationFailed: 'Codex could not be invoked',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
  InputAlreadyResolved: 'Challenges already resolved by another attempt',
}

function phaseLabel(status: ChallengeResolutionAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

/**
 * The one durable action this slice exposes: request a read-only Codex resolution of the
 * latest real Challenged Claude critical review. There is nothing to resolve before a real
 * challenged review exists, so the whole action is withheld until `challengedReviewAttemptId`
 * names one. The request action itself is further withheld once a resolution of that exact
 * review is already Running or has already reached a durable Resolved outcome, so the user can
 * never queue a duplicate resolution of the same review — a review resolved earlier and since
 * superseded by a newer Proposal/review cycle remains requestable again once it is itself
 * Challenged. Mirrors `ClaudeCriticalReviewAction` exactly.
 */
export function ChallengeResolutionAction({
  challengedReviewAttemptId,
  reviewedProposalMessageId,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
  globalClaimBlock,
  timeFit,
}: ChallengeResolutionActionProps) {
  if (!challengedReviewAttemptId) {
    return null
  }

  const resolvesCurrentReview = status?.originalProposalMessageId === reviewedProposalMessageId
  const isActive = resolvesCurrentReview && status?.status === 'Running'
  const isSettledForCurrentReview =
    resolvesCurrentReview && (status?.outcome === 'Resolved' || status?.outcome === 'InputAlreadyResolved')
  const canRequest = !isActive && !isSettledForCurrentReview
  const timeFitBlocked = isAgentClaimPathTimeFitBlocking(timeFit)

  return (
    <section className="dc-challenge-resolution-action" aria-label="Codex challenge resolution">
      {isActive && (
        <p className="dc-challenge-resolution-status" aria-busy="true">
          Challenge resolution {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!isActive && canRequest && globalClaimBlock && (
        <p className="dc-challenge-resolution-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {!isActive && canRequest && timeFitBlocked && (
        <p className="dc-challenge-resolution-time-fit-block" role="status">
          {describeAgentClaimPathTimeFitBlock(timeFit)}
        </p>
      )}
      {!isActive && canRequest && !globalClaimBlock && !timeFitBlocked && (
        <button
          type="button"
          className="dc-challenge-resolution-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Resolve challenges with Codex'}
        </button>
      )}
      {status && !isActive && (
        <p className="dc-challenge-resolution-status">
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
        <p className="dc-challenge-resolution-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
