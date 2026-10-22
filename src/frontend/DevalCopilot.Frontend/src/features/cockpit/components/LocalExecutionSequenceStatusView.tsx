import type { LocalExecutionSequenceDetails } from '../localExecutionSequence'
import {
  LOCAL_EXECUTION_BOOKKEEPING_NOTE,
  LOCAL_EXECUTION_DEADLINE_ELAPSED_NOTE,
  LOCAL_EXECUTION_NO_RECOVERY,
  LOCAL_EXECUTION_PINNED_NOTE,
  LOCAL_EXECUTION_QUEUE_RECORDED_NOTE,
  LOCAL_EXECUTION_QUEUE_RESERVED_NOTE,
  LOCAL_EXECUTION_QUEUE_UNRESOLVED_NOTE,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_WAITING_REASON_LABEL,
  describeLocalExecutionQueuePending,
  describeLocalExecutionState,
} from '../localExecutionSequenceCopy'
import { LocalExecutionSequenceStepTable } from './LocalExecutionSequenceStepTable'

const NONE = '—'

/**
 * The closed recorded status of one local execution sequence. Everything comes from the one validated status: the fixed meaning of the
 * state, the host's fixed reason and next-action texts as literal text, the immutable pinned source, the allowance and deadlines, every
 * step receipt and the exact final references by identity only. An awaiting or quarantined operation says there is no supported
 * recovery action and keeps its exclusion; nothing here infers a running process from lifecycle, claim, dispatch, timers or heartbeats.
 */
export function LocalExecutionSequenceStatusView({ sequence }: { sequence: LocalExecutionSequenceDetails }) {
  const unresolved = sequence.awaitingReconciliation || sequence.steps.some((step) => step.quarantined)
  return (
    <>
      <div role="status" aria-label="Local execution sequence state" className="dc-local-execution-state" data-state={sequence.state}>
        <p>
          <strong>{LOCAL_EXECUTION_STATE_LABEL[sequence.state]}</strong>
        </p>
        <p>{describeLocalExecutionState(sequence)}</p>
        {sequence.observedBeforePersisted && <p>{LOCAL_EXECUTION_DEADLINE_ELAPSED_NOTE}</p>}
        {sequence.reasonText !== null && <p className="dc-local-execution-reason">{sequence.reasonText}</p>}
        {sequence.detailCode !== null && <p className="dc-local-execution-detail">Detail code: {sequence.detailCode}</p>}
        {unresolved && (
          <p className="dc-local-execution-reconciliation">
            {sequence.outstandingOperationId !== null ? `Operation ${sequence.outstandingOperationId} is unresolved: ` : 'An operation is unresolved: '}
            it stays owned and excluded and no process was cancelled. {LOCAL_EXECUTION_NO_RECOVERY}.
          </p>
        )}
        <p className="dc-local-execution-next">{sequence.nextActionText}</p>
      </div>
      {sequence.queue !== null && (
        <dl className="dc-local-execution-queue" aria-label="Local execution queue disclosure" data-admission-state={sequence.queue.admissionState}>
          <dt>Admission mode</dt>
          <dd>Queued (waits for admission)</dd>
          <dt>Pending step</dt>
          <dd>{describeLocalExecutionQueuePending(sequence.queue)}</dd>
          <dt>Admission</dt>
          <dd>
            {sequence.queue.admissionId} (order {sequence.queue.admissionNumber}, {sequence.queue.admissionState})
          </dd>
          <dt>Queued at (UTC)</dt>
          <dd>{sequence.queue.enqueuedAtUtc}</dd>
          <dt>Original deadline (UTC)</dt>
          <dd>{sequence.queue.advancementDeadlineUtc}</dd>
          <dt>Last recorded admission check</dt>
          <dd>
            {LOCAL_EXECUTION_WAITING_REASON_LABEL[sequence.queue.waitingReason]} (recorded {sequence.queue.waitingReasonRecordedAtUtc})
          </dd>
        </dl>
      )}
      {sequence.queue !== null && (
        <p className="dc-local-execution-note">
          {sequence.queue.admissionState === 'Reserved'
            ? LOCAL_EXECUTION_QUEUE_RESERVED_NOTE
            : sequence.queue.admissionState === 'Unresolved'
              ? LOCAL_EXECUTION_QUEUE_UNRESOLVED_NOTE
              : LOCAL_EXECUTION_QUEUE_RECORDED_NOTE}
        </p>
      )}
      <dl className="dc-local-execution-pinned" aria-label="Local execution sequence pinned source">
        <dt>Sequence</dt>
        <dd>{sequence.sequenceId}</dd>
        <dt>Plan message</dt>
        <dd>
          {sequence.planProposalMessageId} ({sequence.planAuthority})
        </dd>
        <dt>Workspace</dt>
        <dd>{sequence.workspaceId}</dd>
        <dt>Pinned checkpoint</dt>
        <dd>{sequence.checkpointId}</dd>
        <dt>Pinned fingerprint (SHA-256)</dt>
        <dd>{sequence.fingerprintSha256}</dd>
        <dt>Newest step source checkpoint</dt>
        <dd>{sequence.currentCheckpointId}</dd>
        <dt>Corrections claimed</dt>
        <dd>
          {sequence.correctionsClaimed} of {sequence.correctionAllowance}
        </dd>
        <dt>Verification passes started</dt>
        <dd>{sequence.passesStarted}</dd>
        <dt>Agent claims</dt>
        <dd>{sequence.agentClaims}</dd>
        <dt>Recipes pinned</dt>
        <dd>{sequence.recipeCount}</dd>
        <dt>{sequence.admissionMode === 'WaitForAdmission' ? 'Queued at (UTC)' : 'Armed at (UTC)'}</dt>
        <dd>{sequence.armedAtUtc}</dd>
        <dt>Advancement deadline (UTC)</dt>
        <dd>{sequence.advancementDeadlineUtc}</dd>
        <dt>Last change (UTC)</dt>
        <dd>{sequence.updatedAtUtc}</dd>
        <dt>Stop requested (UTC)</dt>
        <dd>{sequence.stopRequestedAtUtc ?? NONE}</dd>
        <dt>Ended (UTC)</dt>
        <dd>{sequence.endedAtUtc ?? NONE}</dd>
        <dt>Revision</dt>
        <dd>{sequence.revision}</dd>
      </dl>
      <p className="dc-local-execution-note">{LOCAL_EXECUTION_PINNED_NOTE}</p>
      <p className="dc-local-execution-note">{LOCAL_EXECUTION_BOOKKEEPING_NOTE}</p>
      <LocalExecutionSequenceStepTable steps={sequence.steps} />
      {sequence.final !== null && (
        <dl className="dc-local-execution-final" aria-label="Local execution sequence final references">
          <dt>Final report message</dt>
          <dd>{sequence.final.reportMessageId}</dd>
          {sequence.final.reviewAttemptId !== null && (
            <>
              <dt>Approving review attempt</dt>
              <dd>{sequence.final.reviewAttemptId}</dd>
            </>
          )}
          {sequence.final.escalationMessageId !== null && (
            <>
              <dt>Escalation message</dt>
              <dd>{sequence.final.escalationMessageId}</dd>
            </>
          )}
        </dl>
      )}
      {(sequence.state === 'ReadyForHumanApproval' || sequence.state === 'HumanEscalation') && sequence.final === null && (
        <p className="dc-local-execution-final-missing">The host could not prove the final references of this sequence, so none is shown.</p>
      )}
    </>
  )
}
