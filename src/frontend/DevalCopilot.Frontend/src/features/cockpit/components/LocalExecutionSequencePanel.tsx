import { useCallback, useEffect } from 'react'
import { useArmLocalExecutionSequence } from '../hooks/useArmLocalExecutionSequence'
import { useLocalExecutionSequenceAvailability } from '../hooks/useLocalExecutionSequenceAvailability'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { useStopLocalExecutionSequence } from '../hooks/useStopLocalExecutionSequence'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import type {
  LocalExecutionAdmissionMode,
  LocalExecutionArmReceipt,
  LocalExecutionArmTuple,
  LocalExecutionStopAnswer,
} from '../localExecutionSequence'
import {
  LOCAL_EXECUTION_ARM_LABEL,
  LOCAL_EXECUTION_AVAILABILITY_FAILED,
  LOCAL_EXECUTION_AVAILABILITY_READING,
  LOCAL_EXECUTION_CONFIRMATION_DRIFT,
  LOCAL_EXECUTION_NEVER_RESUMES,
  LOCAL_EXECUTION_NO_PLAN,
  LOCAL_EXECUTION_QUEUE_LABEL,
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
  /** ADR-0062: why a queue request is withheld. Workspace busy and global capacity at its limit are exactly what queueing waits for, so they
   *  are never a reason here; only a conflicting live authority of the run is. Defaults to none. */
  queueWithheldReason?: string | null
}

// What the run-and-plan visit owns beyond the readers: the draft confirmation (bound to its admission mode and to the availability it
// came from), whether it was discarded because that availability changed, and the answers of requests this window sent.
interface Interaction {
  draftMode: LocalExecutionAdmissionMode | null
  draftIdentity: string | null
  drifted: boolean
  receipt: LocalExecutionArmReceipt | null
  stopAnswer: LocalExecutionStopAnswer | null
}

const createInteraction = (): Interaction => ({ draftMode: null, draftIdentity: null, drifted: false, receipt: null, stopAnswer: null })

const NO_COMMANDS = { beginCommand: () => null }

const MODES: readonly LocalExecutionAdmissionMode[] = ['Immediate', 'WaitForAdmission']

const LIVE_STATES = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'WaitingForAdmission']

type Availability = ReturnType<typeof useLocalExecutionSequenceAvailability>['availability']

function tupleOf(planProposalMessageId: string, availability: Availability): LocalExecutionArmTuple | null {
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
 * The explicit local execution cycle of a run (ADR-0060, ADR-0062). Everything it says comes from the run's one shared status and the
 * settled availability of the one selected plan, read separately for each admission mode. Authorizing now or queueing is offered only for
 * the settled exact candidate of that mode while no sequence is recorded and the fence is established; each opens its own confirmation
 * bound to the mode and the availability it came from, which is discarded (and the request withheld with fixed copy) when run, plan, mode
 * or availability changes. A queued cycle shows its recorded waiting reason truthfully (the last recorded admission check, never a
 * promise that anything is free now) and a Stop after current operation control while it is live. Unknown, loading, masked or failed
 * status offers neither. There is no resume, rearm, retry or cancel control, no UI-side advancement, and every host text is literal text.
 */
export function LocalExecutionSequencePanel({
  runId,
  localExecutionSequence,
  planProposalMessageId,
  latestEventSequence,
  armWithheldReason = null,
  queueWithheldReason = null,
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
  const immediateRead = useLocalExecutionSequenceAvailability(runId, availabilityPlan, latestEventSequence, 'Immediate')
  const queueRead = useLocalExecutionSequenceAvailability(runId, availabilityPlan, latestEventSequence, 'WaitForAdmission')
  const reads = { Immediate: immediateRead, WaitForAdmission: queueRead } as const
  const identities = {
    Immediate: immediateRead.availability?.available ? JSON.stringify(immediateRead.availability) : null,
    WaitForAdmission: queueRead.availability?.available ? JSON.stringify(queueRead.availability) : null,
  } as const

  const owner = useOwnedLifetime(JSON.stringify([runId, planProposalMessageId]))
  const [{ draftMode, draftIdentity, drifted, receipt, stopAnswer }, commit] = useOwnedState(owner, createInteraction)
  const draftIdentityNow = draftMode === null ? null : identities[draftMode]
  const draftReading = draftMode === null ? false : reads[draftMode].reading
  const draftValid = draftMode !== null && draftIdentity !== null && draftIdentity === draftIdentityNow

  useEffect(() => {
    if (draftMode !== null && draftIdentity !== null && draftIdentity !== draftIdentityNow && !draftReading) {
      commit((previous) => ({ ...previous, draftMode: null, draftIdentity: null, drifted: true }))
    }
  }, [draftMode, draftIdentity, draftIdentityNow, draftReading, commit])

  const onReceipt = useCallback(
    (next: LocalExecutionArmReceipt) => commit((previous) => ({ ...previous, receipt: next, draftMode: null, draftIdentity: null, drifted: false })),
    [commit],
  )
  const onStopAnswer = useCallback((next: LocalExecutionStopAnswer) => commit((previous) => ({ ...previous, stopAnswer: next })), [commit])
  const refreshImmediate = immediateRead.refresh
  const refreshQueue = queueRead.refresh
  const onArmSettled = useCallback(() => {
    refreshImmediate()
    refreshQueue()
  }, [refreshImmediate, refreshQueue])

  const commands = reader ?? NO_COMMANDS
  const tuples = {
    Immediate:
      planProposalMessageId === null || !draftValid || draftMode !== 'Immediate' ? null : tupleOf(planProposalMessageId, immediateRead.availability),
    WaitForAdmission:
      planProposalMessageId === null || !draftValid || draftMode !== 'WaitForAdmission'
        ? null
        : tupleOf(planProposalMessageId, queueRead.availability),
  } as const
  const armAction = useArmLocalExecutionSequence(runId, tuples.Immediate, commands, onArmSettled, onReceipt, 'Immediate')
  const queueAction = useArmLocalExecutionSequence(runId, tuples.WaitForAdmission, commands, onArmSettled, onReceipt, 'WaitForAdmission')
  const actions = { Immediate: armAction, WaitForAdmission: queueAction } as const
  const requesting = armAction.requesting || queueAction.requesting
  const stopAction = useStopLocalExecutionSequence(runId, sequence?.sequenceId ?? null, commands, onStopAnswer)

  const canOpen = (mode: LocalExecutionAdmissionMode) =>
    control === 'none'
    && reads[mode].availability?.available === true
    && !reads[mode].reading
    && (mode === 'WaitForAdmission' ? queueWithheldReason : armWithheldReason) === null
    && !requesting
  const stoppable =
    sequence !== null && control !== 'unknown' && ['Armed', 'PreparingStep', 'AwaitingStepResult', 'WaitingForAdmission'].includes(sequence.state)
  const alreadyStopping = sequence !== null && control !== 'unknown' && sequence.state === 'Stopping'

  const renderOffer = (mode: LocalExecutionAdmissionMode) => {
    const read = reads[mode]
    const { availability, failure } = read
    const label = mode === 'WaitForAdmission' ? LOCAL_EXECUTION_QUEUE_LABEL : LOCAL_EXECUTION_ARM_LABEL
    if (failure !== null) {
      // One notice and one retry for every failed read, shown at the first failed mode so the same sentence never repeats.
      if (MODES.find((candidate) => reads[candidate].failure !== null) !== mode) {
        return null
      }

      return (
        <div key={mode} data-admission-mode={mode}>
          <p className="dc-local-execution-error" role="status">
            {LOCAL_EXECUTION_AVAILABILITY_FAILED}
          </p>
          <button
            type="button"
            onClick={() => {
              for (const candidate of MODES) {
                if (reads[candidate].failure !== null) {
                  reads[candidate].refresh()
                }
              }
            }}
          >
            Read availability again
          </button>
        </div>
      )
    }

    if (availability === null) {
      return (
        <p key={mode} role="status" data-admission-mode={mode}>
          {LOCAL_EXECUTION_AVAILABILITY_READING}
        </p>
      )
    }

    if (!availability.available || availability.candidate === null) {
      return (
        <p key={mode} className="dc-local-execution-refusal" data-admission-mode={mode}>
          {mode === 'WaitForAdmission' ? 'Queueing' : 'Arming'} is not available: {describeLocalExecutionRefusalCode(availability.refusalCode)}
        </p>
      )
    }

    if (draftValid && draftMode === mode) {
      const tuple = tuples[mode]
      return (
        <LocalExecutionConfirmation
          key={mode}
          planProposalMessageId={availability.planProposalMessageId}
          candidate={availability.candidate}
          mode={mode}
          canConfirm={canOpen(mode) && tuple !== null && !actions[mode].requesting}
          submitting={actions[mode].requesting}
          onConfirm={() => {
            if (tuple !== null) {
              void actions[mode].arm(runId, tuple)
            }
          }}
          onCancel={() => commit((previous) => ({ ...previous, draftMode: null, draftIdentity: null, drifted: false }))}
        />
      )
    }

    return (
      <div key={mode} data-admission-mode={mode}>
        <button
          type="button"
          disabled={!canOpen(mode) || draftMode !== null}
          onClick={() => commit((previous) => ({ ...previous, draftMode: mode, draftIdentity: identities[mode], drifted: false }))}
        >
          {label}
        </button>
        {read.reading && <p className="dc-local-execution-note">{LOCAL_EXECUTION_AVAILABILITY_READING}</p>}
      </div>
    )
  }

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
    } else {
      arming = (
        <>
          {MODES.map((mode) => renderOffer(mode))}
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
          {queueWithheldReason !== null && queueWithheldReason !== armWithheldReason && (
            <p className="dc-local-execution-note" role="status">
              {queueWithheldReason}
            </p>
          )}
        </>
      )
    }

    body = (
      <>
        <p className="dc-local-execution-state" data-state="None">
          No local execution cycle is authorized for this run. Nothing runs until one is authorized or queued, or a stage is requested
          explicitly.
        </p>
        {arming}
        {armAction.error && (
          <p className="dc-local-execution-error" role="alert">
            {armAction.error}
          </p>
        )}
        {queueAction.error && (
          <p className="dc-local-execution-error" role="alert">
            {queueAction.error}
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
            {acknowledged.admissionMode === 'WaitForAdmission'
              ? acknowledged.replayed
                ? 'Already queued: the recorded receipt for this tuple was returned. Nothing was queued again.'
                : 'Queued by this window: the cycle was recorded and waits for admission. Nothing was started, reserved or claimed.'
              : acknowledged.replayed
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
        {!LIVE_STATES.includes(sequence.state) && <p className="dc-local-execution-note">{LOCAL_EXECUTION_NEVER_RESUMES}</p>}
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
