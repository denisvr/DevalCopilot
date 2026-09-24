import type { CodeReviewAttemptStatusResponse } from '../../../api/clients'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'

interface CodeReviewActionProps {
  executionReportMessageId: string | null
  status: CodeReviewAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
}

const OUTCOME_LABEL: Record<string, string> = {
  ReviewApproved: 'Implementation approved',
  ReviewChangesRequested: 'Changes requested',
  SourceChanged: 'Source changed before the review completed',
  InvalidStructuredOutput: 'Codex returned an invalid structured response',
  ProviderInvocationFailed: 'Codex could not be invoked',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
  InputAlreadyCodeReviewed: 'This implementation already has a code review',
}

function phaseLabel(status: CodeReviewAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

/**
 * The one durable action this slice exposes: request a read-only Codex code review of the latest
 * real, complete Claude implementation ExecutionReport, evaluated against its bounded
 * local-verification evidence. There is nothing to review before a real (provider-observed)
 * ExecutionReport exists, so the whole action is withheld until `executionReportMessageId` names
 * one — the cockpit never surfaces the verification-gate closed reason codes directly; a request
 * that is not yet eligible simply fails with the backend's own safe, path-free reason, shown via
 * `requestError`. The request action itself is further withheld once a review of that exact
 * ExecutionReport is already Running or has already reached a durable Approved/ChangesRequested
 * outcome, so the user can never queue a duplicate review of the same implementation — an
 * ExecutionReport reviewed earlier and since superseded by a newer implementation remains
 * requestable again. Mirrors <c>ClaudeCriticalReviewAction</c> exactly. Never displays an
 * executable path, argument, prompt, raw manifest, credential, or full provider output — only the
 * closed outcome label and the attempt number.
 */
export function CodeReviewAction({
  executionReportMessageId,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
}: CodeReviewActionProps) {
  if (!executionReportMessageId) {
    return null
  }

  const reviewsCurrentExecutionReport = status?.executionReportMessageId === executionReportMessageId
  const isActive = reviewsCurrentExecutionReport && status?.status === 'Running'
  const isSettledForCurrentExecutionReport =
    reviewsCurrentExecutionReport &&
    (status?.outcome === 'ReviewApproved' ||
      status?.outcome === 'ReviewChangesRequested' ||
      status?.outcome === 'InputAlreadyCodeReviewed')
  const canRequest = !isActive && !isSettledForCurrentExecutionReport

  return (
    <section className="dc-code-review-action" aria-label="Code review">
      {isActive && (
        <p className="dc-code-review-status" aria-busy="true">
          Code review {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!isActive && canRequest && (
        <button type="button" className="dc-code-review-request" disabled={requesting || statusLoading} onClick={onRequest}>
          {requesting ? 'Requesting…' : 'Request code review'}
        </button>
      )}
      {status && !isActive && (
        <p className="dc-code-review-status">
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
        <p className="dc-code-review-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
