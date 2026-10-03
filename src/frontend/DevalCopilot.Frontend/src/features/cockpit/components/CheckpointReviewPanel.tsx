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

export function CheckpointReviewPanel({ projectId, refreshGeneration }: CheckpointReviewPanelProps) {
  const { evidence, current: evidenceCurrent, loading: evidenceLoading } = useProjectGitEvidence(projectId, true, refreshGeneration)
  const { executions } = useProjectVerificationExecutions(projectId)
  const { reviews, error, saving, record } = useProjectCheckpointReviews(projectId)
  const lifetime = useOwnedLifetime(projectId)
  const [{ selection, actorKind }, commit] = useOwnedState(lifetime, createChoices)
  const selectedExecutionId = selection && selection.checkpointId === evidence?.checkpointId ? selection.executionId : null
  const eligibleExecutions = executions.filter(execution =>
    execution.gitCheckpointId === evidence?.checkpointId
    && execution.checkpointFingerprintSha256 === evidence?.fingerprintSha256
    && execution.status !== 'Running',
  )
  const currentExecution = eligibleExecutions.find(execution => execution.verificationExecutionId === selectedExecutionId) ?? eligibleExecutions[0]
  // A decision names the checkpoint it judged, so none is offered while its current metadata is being read or could not be read.
  const canDecide = evidenceCurrent && !saving
  const canApprove = currentExecution?.status === 'Passed'
  async function submit(decision: string) {
    if (evidenceCurrent && evidence?.checkpointId && (decision === 'Pending' || currentExecution?.verificationExecutionId)) {
      await record(evidence.checkpointId, decision === 'Pending' ? undefined : currentExecution.verificationExecutionId, decision, actorKind)
    }
  }

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
      {evidence?.checkpointId && !currentExecution ? (
        <span className="dc-workspace-evidence-empty">No terminal verification evidence is available. Pending may still be recorded for this checkpoint.</span>
      ) : null}
      {evidence?.checkpointId ? (
        <>
          {eligibleExecutions.length > 1 ? (
            <label>
              Verification evidence
              <select aria-label="Verification evidence" value={currentExecution.verificationExecutionId ?? ''} onChange={event => evidence?.checkpointId && commit(previous => ({ ...previous, selection: { checkpointId: evidence.checkpointId!, executionId: event.target.value } }))}>
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
          <div className="dc-verification-command-actions">
          <button type="button" className="dc-button" disabled={!canDecide} onClick={() => void submit('Pending')}>Pending</button>
          <button type="button" className="dc-button" disabled={!currentExecution || !canDecide} onClick={() => void submit('ChangesRequested')}>Changes requested</button>
          <button type="button" className="dc-button" disabled={!currentExecution || !canDecide} onClick={() => void submit('Escalated')}>Escalate</button>
          <button type="button" className="dc-button" data-variant="primary" disabled={!canApprove || !canDecide} onClick={() => void submit('Approved')}>Approve</button>
          </div>
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
          <span>{review.isApplicable ? 'Current checkpoint' : `Historical review · ${staleReasonDescription(review.staleReasonCode)}`}</span>
        </div>
      ))}
      {reviews.some(review => review.decision === 'Approved' && !review.isApplicable) ? (
        <span className="dc-candidate-workspace-attention">A previous approval is no longer applicable because the source checkpoint changed.</span>
      ) : null}
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </section>
  )
}
