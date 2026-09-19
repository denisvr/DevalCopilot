import type { ImplementationAttemptStatusResponse } from '../../../api/clients'

interface ImplementationActionProps {
  planProposalMessageId: string | null
  status: ImplementationAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
}

const OUTCOME_LABEL: Record<string, string> = {
  Implemented: 'Implemented',
  NoChangesProduced: 'Claude reported no changes',
  InvalidStructuredOutput: 'Claude returned an invalid or untrustworthy response',
  ProviderInvocationFailed: 'Claude could not be invoked',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
  InputAlreadyImplemented: 'This plan was already implemented by another attempt',
}

function phaseLabel(status: ImplementationAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

/**
 * The one durable action this slice exposes: implement the current authoritative resolved plan
 * with Claude, entirely inside the owned worktree. There is nothing to implement before a real
 * resolved plan exists (`planProposalMessageId` is only ever set by the caller once one is), so
 * the whole action is withheld until then. The request action itself is further withheld once
 * an implementation of that exact plan is already Running or has already reached a durable
 * Implemented outcome, so the user can never queue a duplicate implementation of the same plan.
 * Never shows an absolute path, prompt, manifest, credential, environment value, or raw
 * transcript — only the bounded summary, the changed relative paths, and the starting/resulting
 * checkpoint identities the status endpoint itself already bounds. Mirrors
 * `ChallengeResolutionAction` exactly, plus the checkpoint/changed-file evidence unique to this
 * role.
 */
export function ImplementationAction({
  planProposalMessageId,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
}: ImplementationActionProps) {
  if (!planProposalMessageId) {
    return null
  }

  const implementsCurrentPlan = status?.planProposalMessageId === planProposalMessageId
  const isActive = implementsCurrentPlan && status?.status === 'Running'
  const isSettledForCurrentPlan =
    implementsCurrentPlan && (status?.outcome === 'Implemented' || status?.outcome === 'InputAlreadyImplemented')
  const canRequest = !isActive && !isSettledForCurrentPlan

  return (
    <section className="dc-implementation-action" aria-label="Claude implementation">
      {isActive && (
        <p className="dc-implementation-status" aria-busy="true">
          Implementation {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!isActive && canRequest && (
        <button
          type="button"
          className="dc-implementation-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Implement the resolved plan with Claude'}
        </button>
      )}
      {status && !isActive && (
        <div className="dc-implementation-result">
          <p className="dc-implementation-status">
            Last attempt #{status.attemptNumber}: {phaseLabel(status)}.
          </p>
          {status.outcome === 'Implemented' && (
            <>
              {status.executionReportSummary && <p className="dc-implementation-summary">{status.executionReportSummary}</p>}
              <p className="dc-implementation-checkpoints">
                Checkpoint {status.startingCheckpointFingerprintSha256?.slice(0, 12)} →{' '}
                {status.resultCheckpointFingerprintSha256?.slice(0, 12)}
              </p>
              {(status.changedRelativePaths?.length ?? 0) > 0 && (
                <ul className="dc-implementation-changed-files">
                  {status.changedRelativePaths?.map((path) => (
                    <li key={path}>{path}</li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      )}
      {(requestError ?? statusError) && (
        <p className="dc-implementation-error" role="status">
          {requestError ?? statusError}
        </p>
      )}
    </section>
  )
}
