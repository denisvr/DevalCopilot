import type { ReviewCorrectionAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface ReviewCorrectionActionProps {
  reviewAttemptId: string | null
  reviewOutcome: string | null
  status: ReviewCorrectionAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
  authorizing?: boolean
  authorizationError?: string | null
  onAuthorize?: () => void
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Review correction's own human-authorization budget (ADR-0010) is a SEPARATE mechanism that
   * cannot override this one: an available or granted authorization never makes a new Agent
   * claim possible while this global block is present. Never a positive eligibility signal —
   * see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
}

const OUTCOME_LABEL: Record<string, string> = {
  CorrectionApplied: 'Correction applied',
  CorrectionNoChangesProduced: 'No correction changes produced',
  InputAlreadyCorrected: 'This review was already corrected',
  CorrectionHeadChanged: 'Unexpected HEAD change requires attention',
  SourceChanged: 'Source changed before correction completed',
  InvalidStructuredOutput: 'The Implementer returned an invalid correction response',
  ProviderInvocationFailed: 'The Implementer could not be invoked for correction',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for correction',
}

function phaseLabel(status: ReviewCorrectionAttemptStatusResponse): string {
  if (status.status === 'Running') return status.dispatchedAtUtc ? 'Running' : 'Pending'
  return status.outcome ? OUTCOME_LABEL[status.outcome] ?? status.outcome : status.status ?? ''
}

export function ReviewCorrectionAction({
  reviewAttemptId,
  reviewOutcome,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
  authorizing,
  authorizationError,
  onAuthorize = () => undefined,
  globalClaimBlock,
}: ReviewCorrectionActionProps) {
  if (reviewOutcome !== 'ReviewChangesRequested' || !reviewAttemptId) return null

  const isForCurrentReview = status?.implementationReviewAttemptId === reviewAttemptId
  const isActive = isForCurrentReview && status?.status === 'Running'
  const isSettled = isForCurrentReview && Boolean(status?.outcome)
  const hasAvailableAuthorization = isForCurrentReview && status?.hasAvailableHumanAuthorization === true
  const budgetExhausted = isForCurrentReview && status?.budgetExhausted === true
  const hasCurrentEscalation = isForCurrentReview && Boolean(status?.escalationId)
  const canRequest = !isActive && (
    hasAvailableAuthorization
    || (!isSettled && !budgetExhausted)
  )
  const canCreateEscalation = !isActive && !isSettled && budgetExhausted && !hasCurrentEscalation
  const canAuthorize = !isActive && budgetExhausted && hasCurrentEscalation && !hasAvailableAuthorization

  return (
    <section className="dc-review-correction-action" aria-label="Review correction">
      {isActive && <p aria-busy="true">Review correction {phaseLabel(status).toLowerCase()}…</p>}
      {!isActive && canRequest && !hasAvailableAuthorization && globalClaimBlock && (
        <p className="dc-review-correction-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {!isActive && canRequest && !globalClaimBlock && (
        <button type="button" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Requesting…' : 'Request review correction'}
        </button>
      )}
      {canCreateEscalation && globalClaimBlock && (
        // Creating an escalation still calls CreateReviewCorrectionAttempt, whose handler checks
        // the ADR-0012/ADR-0013 global budgets BEFORE its escalation branch — so this control
        // withholds the button here too, for the same known-certain-rejection reason as every
        // other Agent-claim control, rather than offering an action that cannot actually succeed.
        <p className="dc-review-correction-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {canCreateEscalation && !globalClaimBlock && (
        <button type="button" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Creating…' : 'Create human escalation'}
        </button>
      )}
      {canAuthorize && (
        <div role="alert">
          <p>Review correction attempts are exhausted. No provider invocation will occur without explicit authorization.</p>
          {globalClaimBlock ? (
            // A human authorization for this review-correction-specific budget (ADR-0010) is a
            // separate mechanism from the run-wide ADR-0012/ADR-0013 budgets below, and cannot
            // override them: authorizing here would still not let a new Agent claim proceed.
            <p className="dc-review-correction-global-block" role="status">
              This authorization cannot proceed: {describeGlobalAgentClaimBlock(globalClaimBlock).charAt(0).toLowerCase()}
              {describeGlobalAgentClaimBlock(globalClaimBlock).slice(1)}
            </p>
          ) : (
            status?.escalationId && (
              <button type="button" disabled={authorizing || statusLoading} onClick={onAuthorize}>
                {authorizing ? 'Authorizing…' : 'Authorize one additional correction'}
              </button>
            )
          )}
        </div>
      )}
      {hasAvailableAuthorization && !isActive && !globalClaimBlock && (
        <p role="status">One additional correction attempt is authorized.</p>
      )}
      {hasAvailableAuthorization && !isActive && globalClaimBlock && (
        <p className="dc-review-correction-global-block" role="status">
          An additional correction attempt is authorized, but {describeGlobalAgentClaimBlock(globalClaimBlock).charAt(0).toLowerCase()}
          {describeGlobalAgentClaimBlock(globalClaimBlock).slice(1)} The authorization does not override this.
        </p>
      )}
      {status?.hasAttempt && !isActive && <p>Last correction #{status.attemptNumber}: {phaseLabel(status)}.</p>}
      {status?.hasAttempt && (
        <>
          <ProcessEvidenceLine
            processExecution={status.processExecution}
            dispatchedAtUtc={status.dispatchedAtUtc}
            status={status.status}
          />
          <TokenUsageLine tokenUsage={status.tokenUsage} dispatchedAtUtc={status.dispatchedAtUtc} status={status.status} />
        </>
      )}
      {(requestError ?? authorizationError ?? statusError) && <p role="status">{requestError ?? authorizationError ?? statusError}</p>}
    </section>
  )
}
