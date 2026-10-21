import { useCallback, useEffect } from 'react'
import { useArmLocalExecutionSequence } from '../hooks/useArmLocalExecutionSequence'
import { useLocalExecutionSequenceAvailability } from '../hooks/useLocalExecutionSequenceAvailability'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { useStopLocalExecutionSequence } from '../hooks/useStopLocalExecutionSequence'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import type { LocalExecutionArmReceipt, LocalExecutionArmTuple, LocalExecutionStopAnswer } from '../localExecutionSequence'
import {
  LOCAL_EXECUTION_ARM_LABEL,
  LOCAL_EXECUTION_AVAILABILITY_FAILED,
  LOCAL_EXECUTION_AVAILABILITY_READING,
  LOCAL_EXECUTION_CONFIRMATION_DRIFT,
  LOCAL_EXECUTION_NEVER_RESUMES,
  LOCAL_EXECUTION_NO_PLAN,
  LOCAL_EXECUTION_READING,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_STOP_LABEL,
  LOCAL_EXECUTION_STOP_NOTE,
  LOCAL_EXECUTION_UNAVAILABLE,
  describeLocalExecutionRefusalCode,
} from '../localExecutionSequenceCopy'
import { LocalExecutionConfirmation } from './LocalExecutionConfirmation'
import { LocalExecutionSequenceStatusView } from './LocalExecutionSequenceStatusView'

interface LocalExecutionSequencePanelProps {
  runId: string
  /** The run's one shared reader (ADR-0060); everything shown, and every control, derives from this one validated status. */
  localExecutionSequence: UseLocalExecutionSequenceResult | null
  /** The one plan message selected for implementation, or null when none is. Arming is bound to exactly this plan. */
  planProposalMessageId: string | null
  /** A durable run hint: the arming availability is read again when it changes. */
  latestEventSequence?: number
  /** The fixed reason another veto (workspace ownership or capacity) withholds arming; it only ever vetoes. */
  armWithheldReason?: string | null
}

// What the run-and-plan visit owns beyond the readers: the draft confirmation (bound to the availability it came from), whether it was
// discarded because that availability changed, and the answers of requests this window sent.
interface Interaction {
  draftIdentity: string | null
  drifted: boolean
  receipt: LocalExecutionArmReceipt | null
  stopAnswer: LocalExecutionStopAnswer | null
}

const createInteraction = (): Interaction => ({ draftIdentity: null, drifted: false, receipt: null, stopAnswer: null })

const NO_COMMANDS = { beginCommand: () => null }

function tupleOf(planProposalMessageId: string, availability: ReturnType<typeof useLocalExecutionSequenceAvailability>['availability']): LocalExecutionArmTuple | null {
  const candidate = availability?.candidate ?? null
  return candidate === null
    ? null
    : {
        planProposalMessageId,
        workspaceId: candidate.workspaceId,
        checkpointId: candidate.checkpointId,
        fingerprintSha256: candidate.fingerprintSha256,
        confirmationSha256: candidate.confirmationSha256,
      }
}

/**
 * The explicit local execution cycle of a run (ADR-0060). Everything it says comes from the run's one shared status and the settled
 * availability of the one selected plan. Arming is offered only for the settled exact candidate while no sequence is recorded and the
 * fence is established; it opens a confirmation bound to the availability it came from, which is discarded (and arming withheld with
 * fixed copy) when run, plan or availability changes. After arming it shows the closed status truthfully and a Stop after current
 * operation control only while the sequence is live. Unknown, loading, masked or failed status offers neither. There is no resume,
 * rearm, retry or cancel control, and every host text is literal text.
 */
export function LocalExecutionSequencePanel({
  runId,
  localExecutionSequence,
  planProposalMessageId,
  latestEventSequence,
  armWithheldReason = null,
}: LocalExecutionSequencePanelProps) {
  const reader = localExecutionSequence !== null && localExecutionSequence.runId === runId ? localExecutionSequence : null
  const status = reader?.status ?? null
  const sequence = status?.sequence ?? null
  const control = reader?.control ?? 'unknown'
  const refreshStatus = reader?.refresh

  // The arm command masks the shared status (control becomes unknown) from its submission until a later status read settles. That mask
  // must not end the availability visit the confirmed tuple came from: dropping it would discard the draft, change the arm owner and
  // cancel the very request being sent. While the command is confirming the plan's availability visit therefore stays open.
  const availabilityPlan = control === 'none' || reader?.confirming === true ? planProposalMessageId : null
  const availabilityRead = useLocalExecutionSequenceAvailability(runId, availabilityPlan, latestEventSequence)
  const { availability, reading: availabilityReading, failure: availabilityFailure } = availabilityRead
  const refreshAvailability = availabilityRead.refresh
  const identity = availability?.available ? JSON.stringify(availability) : null

  const owner = useOwnedLifetime(JSON.stringify([runId, planProposalMessageId]))
  const [{ draftIdentity, drifted, receipt, stopAnswer }, commit] = useOwnedState(owner, createInteraction)
  const draftValid = draftIdentity !== null && draftIdentity === identity

  useEffect(() => {
    if (draftIdentity !== null && draftIdentity !== identity && !availabilityReading) {
      commit((previous) => ({ ...previous, draftIdentity: null, drifted: true }))
    }
  }, [draftIdentity, identity, availabilityReading, commit])

  const onReceipt = useCallback(
    (next: LocalExecutionArmReceipt) => commit((previous) => ({ ...previous, receipt: next, draftIdentity: null, drifted: false })),
    [commit],
  )
  const onStopAnswer = useCallback((next: LocalExecutionStopAnswer) => commit((previous) => ({ ...previous, stopAnswer: next })), [commit])
  const onArmSettled = useCallback(() => refreshAvailability(), [refreshAvailability])

  const commands = reader ?? NO_COMMANDS
  const tuple = planProposalMessageId === null || !draftValid ? null : tupleOf(planProposalMessageId, availability)
  const armAction = useArmLocalExecutionSequence(runId, tuple, commands, onArmSettled, onReceipt)
  const stopAction = useStopLocalExecutionSequence(runId, sequence?.sequenceId ?? null, commands, onStopAnswer)

  const canOpen =
    control === 'none' && availability?.available === true && !availabilityReading && armWithheldReason === null && !armAction.requesting
  const canConfirm = canOpen && draftValid && tuple !== null
  const stoppable = sequence !== null && control !== 'unknown' && ['Armed', 'PreparingStep', 'AwaitingStepResult'].includes(sequence.state)
  const alreadyStopping = sequence !== null && control !== 'unknown' && sequence.state === 'Stopping'

  let body
  if (reader === null || control === 'unknown' || status === null) {
    const reading = reader === null || (reader.loading && reader.failure === null) || (reader.confirming && reader.failure === null)
    body = (
      <>
        <p role="status" className="dc-local-execution-unknown" data-state="Unknown">
          {reading ? LOCAL_EXECUTION_READING : LOCAL_EXECUTION_UNAVAILABLE}
        </p>
        {reader !== null && !reader.loading && !reader.confirming && (
          <button type="button" onClick={refreshStatus}>
            Read status again
          </button>
        )}
      </>
    )
  } else if (sequence === null) {
    let arming
    if (planProposalMessageId === null) {
      arming = <p className="dc-local-execution-note">{LOCAL_EXECUTION_NO_PLAN}</p>
    } else if (availabilityFailure !== null) {
      arming = (
        <>
          <p className="dc-local-execution-error" role="status">
            {LOCAL_EXECUTION_AVAILABILITY_FAILED}
          </p>
          <button type="button" onClick={refreshAvailability}>
            Read availability again
          </button>
        </>
      )
    } else if (availability === null) {
      arming = <p role="status">{LOCAL_EXECUTION_AVAILABILITY_READING}</p>
    } else if (!availability.available || availability.candidate === null) {
      arming = <p className="dc-local-execution-refusal">Arming is not available: {describeLocalExecutionRefusalCode(availability.refusalCode)}</p>
    } else {
      arming = (
        <>
          {draftValid ? (
            <LocalExecutionConfirmation
              planProposalMessageId={availability.planProposalMessageId}
              candidate={availability.candidate}
              canConfirm={canConfirm && !armAction.requesting}
              submitting={armAction.requesting}
              onConfirm={() => {
                if (tuple !== null) {
                  void armAction.arm(runId, tuple)
                }
              }}
              onCancel={() => commit((previous) => ({ ...previous, draftIdentity: null, drifted: false }))}
            />
          ) : (
            <button
              type="button"
              disabled={!canOpen}
              onClick={() => commit((previous) => ({ ...previous, draftIdentity: identity, drifted: false }))}
            >
              {LOCAL_EXECUTION_ARM_LABEL}
            </button>
          )}
          {drifted && (
            <p className="dc-local-execution-note" role="status">
              {LOCAL_EXECUTION_CONFIRMATION_DRIFT}
            </p>
          )}
          {armWithheldReason !== null && (
            <p className="dc-local-execution-note" role="status">
              {armWithheldReason}
            </p>
          )}
          {availabilityReading && <p className="dc-local-execution-note">{LOCAL_EXECUTION_AVAILABILITY_READING}</p>}
        </>
      )
    }

    body = (
      <>
        <p className="dc-local-execution-state" data-state="None">
          No local execution cycle is authorized for this run. Nothing runs until one is authorized or a stage is requested explicitly.
        </p>
        {arming}
        {armAction.error && (
          <p className="dc-local-execution-error" role="alert">
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
        {acknowledged && (
          <p className="dc-local-execution-receipt" role="status">
            {acknowledged.replayed
              ? 'Already authorized: the recorded receipt for this tuple was returned. Nothing was armed again.'
              : 'Authorized by this window: the cycle was recorded, and the host now claims its operations.'}
          </p>
        )}
        {stopped && (
          <p className="dc-local-execution-stop-answer" role="status">
            {stopped.changed
              ? 'Stop after current operation was recorded.'
              : `No change: the sequence was already ${LOCAL_EXECUTION_STATE_LABEL[stopped.state].toLowerCase()}.`}
          </p>
        )}
        <LocalExecutionSequenceStatusView sequence={sequence} />
        {(stoppable || alreadyStopping) && (
          <>
            <button
              type="button"
              disabled={!stoppable || stopAction.requesting}
              onClick={() => {
                void stopAction.stop(runId, sequence.sequenceId)
              }}
            >
              {LOCAL_EXECUTION_STOP_LABEL}
            </button>
            <p className="dc-local-execution-note">
              {alreadyStopping ? 'Stop was already requested; the claimed operation may still finish. ' : ''}
              {LOCAL_EXECUTION_STOP_NOTE}
            </p>
          </>
        )}
        {stopAction.error && (
          <p className="dc-local-execution-error" role="alert">
            {stopAction.error}
          </p>
        )}
        {!['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping'].includes(sequence.state) && (
          <p className="dc-local-execution-note">{LOCAL_EXECUTION_NEVER_RESUMES}</p>
        )}
      </>
    )
  }

  return (
    <section className="dc-local-execution" aria-label="Local execution sequence">
      <h3>Local execution cycle</h3>
      {body}
    </section>
  )
}
