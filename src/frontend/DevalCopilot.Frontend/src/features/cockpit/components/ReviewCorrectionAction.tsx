import type { ReviewCorrectionAttemptStatusResponse } from '../../../api/clients'

interface ReviewCorrectionActionProps {
  reviewAttemptId: string | null
  reviewOutcome: string | null
  status: ReviewCorrectionAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
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
}: ReviewCorrectionActionProps) {
  if (reviewOutcome !== 'ReviewChangesRequested' || !reviewAttemptId) return null

  const isForCurrentReview = status?.implementationReviewAttemptId === reviewAttemptId
  const isActive = isForCurrentReview && status?.status === 'Running'
  const isSettled = isForCurrentReview && status?.outcome
  const canRequest = !isActive && !isSettled

  return (
    <section className="dc-review-correction-action" aria-label="Review correction">
      {isActive && <p aria-busy="true">Review correction {phaseLabel(status).toLowerCase()}…</p>}
      {!isActive && canRequest && (
        <button type="button" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Requesting…' : 'Request review correction'}
        </button>
      )}
      {status && !isActive && <p>Last correction #{status.attemptNumber}: {phaseLabel(status)}.</p>}
      {(requestError ?? statusError) && <p role="status">{requestError ?? statusError}</p>}
    </section>
  )
}
