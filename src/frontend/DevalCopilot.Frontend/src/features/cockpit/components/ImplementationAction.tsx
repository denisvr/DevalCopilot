import type { ImplementationAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface ImplementationActionProps {
  planProposalMessageId: string | null
  status: ImplementationAttemptStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  onRequest: () => void
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Never a positive eligibility signal — see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
}

function phaseLabel(status: ImplementationAttemptStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    const name = 'Claude'
    return {
      Implemented: 'Implemented',
      NoChangesProduced: `${name} reported no changes`,
      InvalidStructuredOutput: `${name} returned an invalid or untrustworthy response`,
      ProviderInvocationFailed: `${name} could not be invoked`,
      CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
      WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
      InputAlreadyImplemented: 'This plan was already implemented by another attempt',
    }[status.outcome] ?? status.outcome
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
  globalClaimBlock,
}: ImplementationActionProps) {
  if (!planProposalMessageId) {
    return null
  }

  const implementsCurrentPlan = status?.planProposalMessageId === planProposalMessageId
  const hasAttempt = status?.hasAttempt !== false
  const isActive = implementsCurrentPlan && status?.status === 'Running'
  const isSettledForCurrentPlan =
    implementsCurrentPlan && (status?.outcome === 'Implemented' || status?.outcome === 'InputAlreadyImplemented')
  const canRequest = !isActive && !isSettledForCurrentPlan
  const assignmentProvider = status?.provider === 'ClaudeCode'
    ? 'Claude Code'
    : status?.provider === 'Codex'
      ? 'Codex'
      : 'Unknown'
  const assignmentRole = status?.role === 'Implementer' ? 'Implementer' : 'Unknown'
  const permissionProfile = status?.permissionProfile === 'WorkspaceEditOnly' ? 'Workspace edit only' : 'Unknown'
  const adapterContract = status?.adapterContractVersion === 'claude-implementation-v1'
    ? 'claude-implementation-v1'
    : 'Unknown'
  const formatFact = (value: string | undefined) => value || 'Unknown'
  return (
    <section className="dc-implementation-action" aria-label="Claude implementation">
      {isActive && (
        <p className="dc-implementation-status" aria-busy="true">
          Implementation {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!isActive && canRequest && globalClaimBlock && (
        <p className="dc-implementation-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {!isActive && canRequest && !globalClaimBlock && (
        <button
          type="button"
          className="dc-implementation-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Implement the resolved plan with Claude'}
        </button>
      )}
      {status && hasAttempt && (
        <p className="dc-implementation-assignment">
          {assignmentProvider} · {assignmentRole} · Model requested: {formatFact(status.requestedModel)} · Model observed:{' '}
          {formatFact(status.observedModel)} · Effort requested: {formatFact(status.requestedEffort)} · Effort observed:{' '}
          {formatFact(status.observedEffort)} · {permissionProfile} · Adapter contract: {adapterContract}
        </p>
      )}
      {status && hasAttempt && (
        <>
          <ProcessEvidenceLine
            processExecution={status.processExecution}
            dispatchedAtUtc={status.dispatchedAtUtc}
            status={status.status}
          />
          <TokenUsageLine tokenUsage={status.tokenUsage} dispatchedAtUtc={status.dispatchedAtUtc} status={status.status} />
        </>
      )}
      {status && !isActive && (
        <div className="dc-implementation-result">
          {hasAttempt && (
            <p className="dc-implementation-status">
              Last attempt #{status.attemptNumber}: {phaseLabel(status)}.
            </p>
          )}
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
