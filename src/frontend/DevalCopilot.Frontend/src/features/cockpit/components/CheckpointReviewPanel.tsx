import { toApprovalSource } from '../hooks/checkpointApprovalBundle'
import { useCheckpointApprovalEvidence } from '../hooks/useCheckpointApprovalEvidence'
import { useProjectCheckpointReviews } from '../hooks/useProjectCheckpointReviews'
import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'
import { useProjectVerificationExecutions } from '../hooks/useProjectVerificationExecutions'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'

interface CheckpointReviewPanelProps {
  projectId: string
  // Advanced by the project's owner when the current checkpoint metadata must be read again.
  refreshGeneration?: number
}

// The reviewer's local choices belong to the current project's lifetime, and the chosen verification
// evidence additionally to the checkpoint it was chosen for: another project or a newer checkpoint
// starts from the default choice instead of inheriting a stale target.
interface ReviewChoices {
  selection: { checkpointId: string; executionId: string } | null
  actorKind: string
}

function createChoices(): ReviewChoices {
  return { selection: null, actorKind: 'Human' }
}

function decisionLabel(decision: string | undefined): string {
  return decision === 'ChangesRequested' ? 'Changes requested' : decision ?? 'Unknown'
}

function staleReasonDescription(reason: string | undefined): string {
  switch (reason) {
    case 'review.newer_checkpoint':
      return 'A newer checkpoint exists'
    case 'review.source_changed':
      return 'Source changed since this review'
    case 'review.current_evidence_unavailable':
      return 'Current source evidence is unavailable'
    case 'review.no_current_checkpoint':
      return 'No current checkpoint exists'
    default:
      return 'This review is not applicable to the current checkpoint'
  }
}

// Why the complete set is not offered right now: still reading, a fixed host refusal, or an unavailable read.
function describeBundleState(approval: { loading: boolean; readFailed: boolean; refusal: string | null; current: boolean }): string {
  if (!approval.readFailed) {
    return approval.current ? 'The complete verification set does not match the source shown here. Use Refresh evidence.' : 'Reading the complete verification set…'
  }

  switch (approval.refusal) {
    case 'none':
      return 'Enable at least one verification check before approving the complete set.'
    case 'tooMany':
      return 'More than 32 verification checks are enabled, so the complete set cannot be approved at once.'
    case 'incomplete':
      return 'Every enabled check needs a passing latest run for this checkpoint before the complete set can be approved.'
    default:
      return 'The complete verification set could not be read. Use Refresh evidence.'
  }
}

export function CheckpointReviewPanel({ projectId, refreshGeneration }: CheckpointReviewPanelProps) {
  const { evidence, current: evidenceCurrent, loading: evidenceLoading } = useProjectGitEvidence(projectId, true, refreshGeneration)
  const { executions, current: executionsCurrent, loading: executionsLoading, readFailed: executionsFailed } = useProjectVerificationExecutions(projectId, refreshGeneration)
  const { reviews, error, saving, record, refresh: refreshReviews, current: reviewsCurrent, loading: reviewsLoading, readFailed: reviewsFailed } = useProjectCheckpointReviews(projectId, refreshGeneration)
  // The complete-set action is bound to every identity fact the evidence admitted; any missing or malformed one withholds it.
  const source = toApprovalSource(projectId, evidence)
  const approval = useCheckpointApprovalEvidence(source, refreshGeneration, refreshReviews)
  const lifetime = useOwnedLifetime(projectId)
  const [{ selection, actorKind }, commit] = useOwnedState(lifetime, createChoices)
  const selectedExecutionId = selection && selection.checkpointId === evidence?.checkpointId ? selection.executionId : null
  // The cached executions stay visible (and selectable) as history, but they are authority for a decision only while no
  // execution read is in flight and the newest one of the displayed generation succeeded; cached evidence that may still say
  // Passed is never offered while a read is pending or after it failed.
  const eligibleExecutions = executions.filter(execution =>
    execution.gitCheckpointId === evidence?.checkpointId
    && execution.checkpointFingerprintSha256 === evidence?.fingerprintSha256
    && execution.status !== 'Running',
  )
  const selectedExecution = eligibleExecutions.find(execution => execution.verificationExecutionId === selectedExecutionId) ?? eligibleExecutions[0]
  const currentExecution = executionsCurrent ? selectedExecution : undefined
  // A decision names the checkpoint it judged, so none is offered while its current metadata is being read or could not be read.
  const busy = saving || approval.approving
  const canDecide = evidenceCurrent && !busy
  const canApprove = currentExecution?.status === 'Passed'
  async function submit(decision: string) {
    if (evidenceCurrent && evidence?.checkpointId && (decision === 'Pending' || (executionsCurrent && currentExecution?.verificationExecutionId))) {
      await record(evidence.checkpointId, decision === 'Pending' ? undefined : currentExecution?.verificationExecutionId, decision, actorKind)
    }
  }
  // The complete-set action is offered only for a settled, successful bundle read that names exactly the source displayed here.
  const bundle = approval.bundle
  const bundleMatchesSource = bundle !== null
    && source !== null
    && bundle.projectId === source.projectId
    && bundle.workspaceId === source.workspaceId
    && bundle.checkpointId === source.checkpointId
    && bundle.checkpointNumber === source.checkpointNumber
    && bundle.fingerprintSha256 === source.fingerprintSha256
  const canApproveAll = canDecide && actorKind === 'Human' && approval.current && bundleMatchesSource && !approval.accepted
  async function approveAll() {
    if (canApproveAll) {
      await approval.approve()
    }
  }
  // A read that is not current is either still being read (including the first frame and a retry) or has settled as failed.
  const executionsReading = !executionsCurrent && (executionsLoading || !executionsFailed)
  const reviewsReading = !reviewsCurrent && (reviewsLoading || !reviewsFailed)

  return (
    <section className="dc-verification-commands" aria-label="Checkpoint review evidence">
      <div className="dc-verification-commands-heading">
        <div>
          <span className="dc-candidate-workspace-title">Checkpoint review</span>
          <span className="dc-workspace-evidence-subtitle">Decisions apply only to the current source fingerprint and verification evidence</span>
        </div>
      </div>

      {!evidenceCurrent ? (
        <span className="dc-workspace-evidence-empty">
          {evidenceLoading ? 'Reading the current source checkpoint…' : 'The current source checkpoint could not be refreshed. Use Refresh evidence before recording a review.'}
        </span>
      ) : null}
      {evidenceCurrent && !evidence?.checkpointId ? <span className="dc-workspace-evidence-empty">Capture a current source checkpoint before recording a review.</span> : null}
      {evidence?.checkpointId && !executionsCurrent ? (
        <span className="dc-workspace-evidence-empty">
          {executionsReading
            ? 'Reading verification evidence…'
            : 'Verification evidence could not be refreshed. Use Refresh evidence before recording a decided review; Pending may still be recorded.'}
        </span>
      ) : null}
      {evidence?.checkpointId && executionsCurrent && !currentExecution ? (
        <span className="dc-workspace-evidence-empty">No terminal verification evidence is available. Pending may still be recorded for this checkpoint.</span>
      ) : null}
      {evidence?.checkpointId ? (
        <>
          {eligibleExecutions.length > 1 ? (
            <label>
              Verification evidence
              <select aria-label="Verification evidence" value={selectedExecution?.verificationExecutionId ?? ''} onChange={event => evidence?.checkpointId && commit(previous => ({ ...previous, selection: { checkpointId: evidence.checkpointId!, executionId: event.target.value } }))}>
                {eligibleExecutions.map(execution => <option key={execution.verificationExecutionId} value={execution.verificationExecutionId}>{`#${execution.executionNumber} · ${execution.status}`}</option>)}
              </select>
            </label>
          ) : null}
          <label>
            Reviewer
            <select aria-label="Reviewer" value={actorKind} onChange={event => commit(previous => ({ ...previous, actorKind: event.target.value }))}>
              <option value="Human">Human</option>
              <option value="FutureAgent">Future agent</option>
            </select>
          </label>
          {actorKind === 'Human' ? (
            <div className="dc-verification-command" role="group" aria-label="Complete verification set">
              <strong>Complete verification set</strong>
              {approval.current && bundleMatchesSource && bundle ? (
                <>
                  <ul aria-label="Verification set to approve">
                    {bundle.members.map(member => (
                      <li key={member.executionId}>{`#${member.commandNumber} ${member.recipeLabel} · execution #${member.executionNumber}`}</li>
                    ))}
                  </ul>
                  <span>
                    Records one Human approval of exactly these runs for Checkpoint #{bundle.checkpointNumber}. The explicit local commit requires
                    this complete approval; approving commits nothing.
                  </span>
                  {approval.accepted ? <span>Recorded. The Human approval covers these runs.</span> : null}
                </>
              ) : (
                <span className="dc-workspace-evidence-empty">
                  {source === null
                    ? 'The current source identity is incomplete, so the complete verification set cannot be approved. Use Refresh evidence.'
                    : describeBundleState(approval)}
                </span>
              )}
              <div className="dc-verification-command-actions">
                <button type="button" className="dc-button" data-variant="primary" disabled={!canApproveAll} onClick={() => void approveAll()}>
                  Approve all enabled checks
                </button>
              </div>
              {approval.error ? <span className="dc-candidate-workspace-error">{approval.error}</span> : null}
            </div>
          ) : null}
          <div className="dc-verification-command-actions">
          <button type="button" className="dc-button" disabled={!canDecide} onClick={() => void submit('Pending')}>Pending</button>
          <button type="button" className="dc-button" disabled={!currentExecution || !canDecide} onClick={() => void submit('ChangesRequested')}>Changes requested</button>
          <button type="button" className="dc-button" disabled={!currentExecution || !canDecide} onClick={() => void submit('Escalated')}>Escalate</button>
          <button type="button" className="dc-button" data-variant="primary" disabled={!canApprove || !canDecide} onClick={() => void submit('Approved')}>Approve</button>
          </div>
          {approval.current && bundleMatchesSource && bundle && bundle.members.length > 1 ? (
            <span className="dc-workspace-evidence-empty">
              Approve records only the selected run. With several enabled checks, the local commit needs Approve all enabled checks.
            </span>
          ) : null}
        </>
      ) : null}

      {reviews.map(review => (
        <div className="dc-verification-command" key={review.reviewId}>
          <strong>{decisionLabel(review.decision)}</strong>
          <span>
            Checkpoint #{review.checkpointNumber}
            {review.evidence && review.evidence.length > 0
              ? ` · verification ${review.evidence.map(item => `#${item.verificationExecutionNumber}`).join(', ')}`
              : ''}
            {' · '}{review.actorKind}
          </span>
          <span>
            {!reviewsCurrent
              ? reviewsReading ? 'Confirming applicability to the current checkpoint…' : 'Applicability to the current checkpoint could not be confirmed.'
              : review.isApplicable ? 'Current checkpoint' : `Historical review · ${staleReasonDescription(review.staleReasonCode)}`}
          </span>
        </div>
      ))}
      {reviewsCurrent && reviews.some(review => review.decision === 'Approved' && !review.isApplicable) ? (
        <span className="dc-candidate-workspace-attention">A previous approval is no longer applicable because the source checkpoint changed.</span>
      ) : null}
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </section>
  )
}
