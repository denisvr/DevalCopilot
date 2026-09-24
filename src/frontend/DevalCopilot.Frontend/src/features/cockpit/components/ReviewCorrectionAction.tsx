import type { ReviewCorrectionAttemptStatusResponse } from '../../../api/clients'
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
      {!isActive && canRequest && (
        <button type="button" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Requesting…' : 'Request review correction'}
        </button>
      )}
      {canCreateEscalation && (
        <button type="button" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Creating…' : 'Create human escalation'}
        </button>
      )}
      {canAuthorize && (
        <div role="alert">
          <p>Review correction attempts are exhausted. No provider invocation will occur without explicit authorization.</p>
          {status?.escalationId && (
            <button type="button" disabled={authorizing || statusLoading} onClick={onAuthorize}>
              {authorizing ? 'Authorizing…' : 'Authorize one additional correction'}
            </button>
          )}
        </div>
      )}
      {hasAvailableAuthorization && !isActive && (
        <p role="status">One additional correction attempt is authorized.</p>
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
