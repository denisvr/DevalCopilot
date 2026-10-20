import { useCallback } from 'react'
import { useArmPlanningSequence } from '../hooks/useArmPlanningSequence'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { useStopPlanningSequence } from '../hooks/useStopPlanningSequence'
import type { UsePlanningSequenceResult } from '../hooks/usePlanningSequence'
import type {
  PlanningSequenceArmReceipt,
  PlanningSequenceDetails,
  PlanningSequenceStatus,
  PlanningSequenceStep,
  PlanningSequenceStopAnswer,
  PlanningSequenceTuple,
} from '../planningSequence'
import {
  PLANNING_SEQUENCE_ARM_EXPLANATION,
  PLANNING_SEQUENCE_ARM_LABEL,
  PLANNING_SEQUENCE_DEADLINE_ELAPSED_NOTE,
  PLANNING_SEQUENCE_DISPATCH_REFUSED_LABEL,
  PLANNING_SEQUENCE_NEVER_RESUMES,
  PLANNING_SEQUENCE_PROVIDER_LABEL,
  PLANNING_SEQUENCE_READING,
  PLANNING_SEQUENCE_ROLE_LABEL,
  PLANNING_SEQUENCE_STATE_LABEL,
  PLANNING_SEQUENCE_STEP_STATE_LABEL,
  PLANNING_SEQUENCE_STOP_LABEL,
  PLANNING_SEQUENCE_STOP_NOTE,
  PLANNING_SEQUENCE_UNAVAILABLE,
  describePlanningSequenceState,
} from '../planningSequenceCopy'

interface PlanningSequencePanelProps {
  runId: string
  /** The run's planning-sequence read (ADR-0055); everything shown, and every control, derives from this one validated status. */
  planningSequence: UsePlanningSequenceResult
  /**
   * The fixed reason the workspace execution status (ADR-0057) withholds arming, or null/absent when it withholds nothing. It is only a
   * veto of Arm: reading the status, Stop after current step and every recorded fact stay as they are.
   */
  armWithheldReason?: string | null
}

// What the run's visit owns beyond the status: the receipt of an arming and the answer of a stop that this window sent.
interface Acknowledgements {
  receipt: PlanningSequenceArmReceipt | null
  stopAnswer: PlanningSequenceStopAnswer | null
}

const createAcknowledgements = (): Acknowledgements => ({ receipt: null, stopAnswer: null })

const NONE = '—'

const tupleOf = (status: PlanningSequenceStatus | null): PlanningSequenceTuple | null => {
  const arming = status?.arming
  return arming?.available && arming.workspaceId && arming.checkpointId && arming.fingerprintSha256
    ? { workspaceId: arming.workspaceId, checkpointId: arming.checkpointId, fingerprintSha256: arming.fingerprintSha256 }
    : null
}

function attemptCell(step: PlanningSequenceStep): string {
  if (step.attemptId === null) {
    return NONE
  }

  const outcome =
    step.attemptOutcome === null
      ? ''
      : step.attemptOutcome === 'PlanningSequenceDispatchRefused'
        ? ` · ${PLANNING_SEQUENCE_DISPATCH_REFUSED_LABEL}`
        : ` · ${step.attemptOutcome}`
  return `#${step.attemptNumber} · recorded status ${step.attemptStatus}${outcome} · dispatch marker ${step.dispatched ? 'recorded' : 'not recorded'}`
}

function StepTable({ steps }: { steps: readonly PlanningSequenceStep[] }) {
  if (steps.length === 0) {
    return <p className="dc-planning-sequence-steps-empty">No step has been reserved.</p>
  }

  return (
    <table className="dc-planning-sequence-steps" aria-label="Planning sequence steps">
      <thead>
        <tr>
          <th scope="col">Step</th>
          <th scope="col">Role</th>
          <th scope="col">Provider</th>
          <th scope="col">State</th>
          <th scope="col">Attempt</th>
          <th scope="col">Preparation deadline (UTC)</th>
          <th scope="col">Result deadline (UTC)</th>
        </tr>
      </thead>
      <tbody>
        {steps.map((step) => (
          <tr key={step.stepId} data-step-state={step.state}>
            <td>{step.ordinal}</td>
            <td>{PLANNING_SEQUENCE_ROLE_LABEL[step.role]}</td>
            <td>{PLANNING_SEQUENCE_PROVIDER_LABEL[step.provider]}</td>
            <td>{PLANNING_SEQUENCE_STEP_STATE_LABEL[step.state]}</td>
            <td>{attemptCell(step)}</td>
            <td>{step.preparationDeadlineUtc}</td>
            <td>{step.resultDeadlineUtc ?? NONE}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function FinalReferences({ sequence }: { sequence: PlanningSequenceDetails }) {
  if (sequence.state !== 'ReadyForImplementation' && sequence.state !== 'AwaitingHumanDecision') {
    return null
  }

  const { final } = sequence
  if (final === null) {
    return <p className="dc-planning-sequence-final-missing">The host could not prove the final references of this sequence, so none is shown.</p>
  }

  return (
    <dl className="dc-planning-sequence-final" aria-label="Planning sequence final references">
      <dt>Final proposal message</dt>
      <dd>{final.finalProposalMessageId}</dd>
      {final.acceptanceMessageId !== null && (
        <>
          <dt>Acceptance message</dt>
          <dd>{final.acceptanceMessageId}</dd>
        </>
      )}
      {final.escalationMessageId !== null && (
        <>
          <dt>Escalation message</dt>
          <dd>{final.escalationMessageId}</dd>
        </>
      )}
      <dt>Proving attempt</dt>
      <dd>{final.provingAttemptId}</dd>
    </dl>
  )
}

/**
 * The explicit, read-only planning sequence of a run (ADR-0055). Everything it says comes from the one validated recorded status: the
 * exact arming candidate or its fixed refusal, the pinned tuple of an existing sequence (never a replacement target), the step table,
 * the fixed meaning of each closed state, and the exact final references by identity only. A recorded claim or a dispatch marker is
 * bookkeeping and is never worded as a live provider process. Unknown, loading or failed status offers neither arming nor Stop. The
 * panel has no resume, rearm, retry or cancel control, and it shows no transcript or artifact body; every host text is literal text.
 */
export function PlanningSequencePanel({ runId, planningSequence, armWithheldReason = null }: PlanningSequencePanelProps) {
  const { status, loading, failure, control, refresh } = planningSequence
  const sequence = status?.sequence ?? null
  const candidate = tupleOf(status)

  const owner = useOwnedLifetime(runId)
  const [{ receipt, stopAnswer }, commit] = useOwnedState(owner, createAcknowledgements)
  const onReceipt = useCallback((next: PlanningSequenceArmReceipt) => commit((previous) => ({ ...previous, receipt: next })), [commit])
  const onStopAnswer = useCallback((next: PlanningSequenceStopAnswer) => commit((previous) => ({ ...previous, stopAnswer: next })), [commit])

  const armAction = useArmPlanningSequence(runId, candidate, refresh, onReceipt)
  const stopAction = useStopPlanningSequence(runId, sequence?.sequenceId ?? null, refresh, onStopAnswer)

  const canArm = control === 'none' && candidate !== null && !armAction.requesting && armWithheldReason === null
  const stoppable = sequence !== null && control !== 'unknown' && ['Armed', 'PreparingStep', 'AwaitingStepResult'].includes(sequence.state)
  const alreadyStopping = sequence !== null && control !== 'unknown' && sequence.state === 'Stopping'

  let body
  if (control === 'unknown' || status === null) {
    body = (
      <>
        <p role="status" className="dc-planning-sequence-unknown" data-state="Unknown">
          {loading && failure === null ? PLANNING_SEQUENCE_READING : PLANNING_SEQUENCE_UNAVAILABLE}
        </p>
        {!loading && (
          <button type="button" onClick={refresh}>
            Read status again
          </button>
        )}
      </>
    )
  } else if (sequence === null) {
    const arming = status.arming
    body = (
      <>
        <p className="dc-planning-sequence-state" data-state="None">
          No planning sequence is armed for this run. Nothing runs until one is armed or a stage is requested explicitly.
        </p>
        {arming?.available && candidate !== null ? (
          <>
            <ul className="dc-planning-sequence-explanation">
              {PLANNING_SEQUENCE_ARM_EXPLANATION.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
            <dl className="dc-planning-sequence-candidate" aria-label="Planning sequence arming candidate">
              <dt>Workspace</dt>
              <dd>{candidate.workspaceId}</dd>
              <dt>Checkpoint</dt>
              <dd>{candidate.checkpointId}</dd>
              <dt>Fingerprint (SHA-256)</dt>
              <dd>{candidate.fingerprintSha256}</dd>
            </dl>
            <p className="dc-planning-sequence-note">
              This is the recorded candidate, not a readiness proof: the host checks the source again when you arm, and arms nothing if it changed.
            </p>
            <button
              type="button"
              disabled={!canArm}
              onClick={() => {
                if (candidate !== null) {
                  void armAction.arm(runId, candidate)
                }
              }}
            >
              {armAction.requesting ? 'Arming…' : PLANNING_SEQUENCE_ARM_LABEL}
            </button>
            {armWithheldReason !== null && (
              <p className="dc-planning-sequence-note" role="status">
                {armWithheldReason}
              </p>
            )}
          </>
        ) : (
          <p className="dc-planning-sequence-refusal">
            Arming is not available: {arming?.refusalText}
          </p>
        )}
        {armAction.error && (
          <p className="dc-planning-sequence-error" role="alert">
            {armAction.error}
          </p>
        )}
      </>
    )
  } else {
    const acknowledged = receipt !== null && receipt.sequenceId === sequence.sequenceId ? receipt : null
    const stopped = stopAnswer !== null && stopAnswer.sequenceId === sequence.sequenceId ? stopAnswer : null
    body = (
      <>
        <div role="status" aria-label="Planning sequence state" className="dc-planning-sequence-state" data-state={sequence.state}>
          <p>
            <strong>{PLANNING_SEQUENCE_STATE_LABEL[sequence.state]}</strong>
          </p>
          <p>{describePlanningSequenceState(sequence)}</p>
          {sequence.observedBeforePersisted && <p>{PLANNING_SEQUENCE_DEADLINE_ELAPSED_NOTE}</p>}
          {sequence.reasonText !== null && <p className="dc-planning-sequence-reason">{sequence.reasonText}</p>}
          {sequence.detailCode !== null && <p className="dc-planning-sequence-detail">Detail code: {sequence.detailCode}</p>}
          {sequence.awaitingReconciliation && sequence.outstandingAttemptId !== null && (
            <p className="dc-planning-sequence-reconciliation">
              Attempt {sequence.outstandingAttemptId} is awaiting reconciliation: it was claimed and has no recorded final result. No process was cancelled.
            </p>
          )}
          <p className="dc-planning-sequence-next">{sequence.nextActionText}</p>
        </div>
        {acknowledged && (
          <p className="dc-planning-sequence-receipt" role="status">
            {acknowledged.replayed
              ? 'Already armed: the recorded receipt for this tuple was returned. Nothing was armed again.'
              : 'Armed by this window: the sequence was recorded, and the host now claims its steps.'}
          </p>
        )}
        {stopped && (
          <p className="dc-planning-sequence-stop-answer" role="status">
            {stopped.changed
              ? 'Stop after current step was recorded.'
              : `No change: the sequence was already ${PLANNING_SEQUENCE_STATE_LABEL[stopped.state].toLowerCase()}.`}
          </p>
        )}
        <dl className="dc-planning-sequence-pinned" aria-label="Planning sequence pinned source">
          <dt>Sequence</dt>
          <dd>{sequence.sequenceId}</dd>
          <dt>Workspace</dt>
          <dd>{sequence.workspaceId}</dd>
          <dt>Checkpoint</dt>
          <dd>{sequence.checkpointId}</dd>
          <dt>Fingerprint (SHA-256)</dt>
          <dd>{sequence.fingerprintSha256}</dd>
          <dt>Armed at (UTC)</dt>
          <dd>{sequence.armedAtUtc}</dd>
          <dt>Last change (UTC)</dt>
          <dd>{sequence.updatedAtUtc}</dd>
          <dt>Revision</dt>
          <dd>{sequence.revision}</dd>
        </dl>
        <p className="dc-planning-sequence-note">The pinned source is immutable: it is never replaced by a newer workspace or checkpoint.</p>
        <StepTable steps={sequence.steps} />
        <FinalReferences sequence={sequence} />
        {(stoppable || alreadyStopping) && (
          <>
            <button
              type="button"
              disabled={!stoppable || stopAction.requesting}
              onClick={() => {
                void stopAction.stop(runId, sequence.sequenceId)
              }}
            >
              {PLANNING_SEQUENCE_STOP_LABEL}
            </button>
            <p className="dc-planning-sequence-note">
              {alreadyStopping ? 'Stop was already requested; the claimed step may still finish. ' : ''}
              {PLANNING_SEQUENCE_STOP_NOTE}
            </p>
          </>
        )}
        {stopAction.error && (
          <p className="dc-planning-sequence-error" role="alert">
            {stopAction.error}
          </p>
        )}
        {sequence.state !== 'Armed' && sequence.state !== 'PreparingStep' && sequence.state !== 'AwaitingStepResult' && sequence.state !== 'Stopping' && (
          <p className="dc-planning-sequence-note">{PLANNING_SEQUENCE_NEVER_RESUMES}</p>
        )}
      </>
    )
  }

  return (
    <section className="dc-planning-sequence" aria-label="Planning sequence">
      <h3>Planning sequence</h3>
      {body}
    </section>
  )
}
