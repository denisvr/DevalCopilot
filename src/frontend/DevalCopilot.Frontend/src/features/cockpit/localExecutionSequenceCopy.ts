import type { GlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import type {
  LocalExecutionAvailabilityCandidate,
  LocalExecutionReasonCode,
  LocalExecutionSequenceControl,
  LocalExecutionSequenceDetails,
  LocalExecutionSequenceStateName,
  LocalExecutionStep,
  LocalExecutionStepKind,
  LocalExecutionStepState,
} from './localExecutionSequence'
import { problemOf } from './planningSequenceCopy'

// Fixed English copy of the local-execution-sequence surface (ADR-0060). Nothing here is taken from a host answer, a payload or an
// exception: each text is chosen from a closed code, so an unrecognized value can never be worded as a known one. Recipe names are the
// only untrusted text the panel shows, and they are rendered as inert text by the component.

export const LOCAL_EXECUTION_ARM_LABEL = 'Authorize local execution cycle'
export const LOCAL_EXECUTION_STOP_LABEL = 'Stop after current operation'
export const LOCAL_EXECUTION_CONFIRM_LABEL = 'Confirm and authorize'
export const LOCAL_EXECUTION_CANCEL_LABEL = 'Cancel'
export const LOCAL_EXECUTION_NO_RECOVERY = 'No supported recovery action in this version'

export const LOCAL_EXECUTION_READING = 'Reading the local execution sequence status…'
export const LOCAL_EXECUTION_UNAVAILABLE =
  'The local execution sequence status could not be read or validated. Authorizing a cycle and every manual start for this run are withheld until it can.'
export const LOCAL_EXECUTION_AVAILABILITY_READING = 'Reading the recorded arming availability of the selected plan…'
export const LOCAL_EXECUTION_AVAILABILITY_FAILED =
  'The arming availability of the selected plan could not be read or validated, so nothing can be authorized. Read it again.'
export const LOCAL_EXECUTION_NO_PLAN = 'No plan is selected for implementation, so no local execution cycle can be authorized yet.'
export const LOCAL_EXECUTION_CONFIRMATION_DRIFT =
  'The recorded availability changed after you opened this confirmation. It was discarded and nothing was sent; review the new disclosure and confirm again.'
export const LOCAL_EXECUTION_UNKNOWN_OUTCOME =
  'The outcome of the request is unknown: the host may have recorded it. The recorded status is being read again and nothing was resent.'
export const LOCAL_EXECUTION_NEVER_RESUMES =
  'A sequence is never resumed, rearmed or retried by this window; later work is an explicit manual request.'
export const LOCAL_EXECUTION_STOP_NOTE =
  'Stop after current operation is not process cancellation, run Stop or Pause: an operation that is already claimed may still dispatch and finish, and only successor operations are prevented.'
export const LOCAL_EXECUTION_DEADLINE_ELAPSED_NOTE = 'Deadline elapsed, not yet persisted by the host.'
export const LOCAL_EXECUTION_APPROVAL_NOTE =
  'Awaiting Human approval. This is not committed and not published, and it is not proof that the source is still current. Approval and commit each need their own separate, explicit request.'
export const LOCAL_EXECUTION_PINNED_NOTE = 'The pinned source is immutable: it is never replaced by a newer workspace or checkpoint.'
export const LOCAL_EXECUTION_BOOKKEEPING_NOTE =
  'A recorded claim, dispatch marker or exit record is bookkeeping: it is not proof that a process is running or that it has stopped.'

/** The confirmation the Human reads before arming; every line is fixed copy and the recipe list is rendered by the component. */
export function describeLocalExecutionConfirmation(candidate: LocalExecutionAvailabilityCandidate): readonly string[] {
  const corrections =
    candidate.correctionAllowance === 0
      ? 'No automatic correction remains in this authorization.'
      : `At most ${candidate.correctionAllowance} shared ${candidate.correctionAllowance === 1 ? 'correction' : 'corrections'} (the smaller remaining amount) can run automatically.`
  return [
    'Authorizing lets the host run one fixed cycle without further requests from you: an implementation of exactly this plan, whole verification passes, an ordinary code review or failure diagnosis, and corrections.',
    'The verification commands listed below CAN MUTATE the workspace. They run inside the isolated workspace with the timeouts shown.',
    corrections,
    'The cycle can advance for at most two hours from authorization. A deadline closes further steps; it does not cancel a running process.',
    'Approval and commit stay separate and separately authorized: a successful cycle only ends at Awaiting Human approval.',
    'Stop after current operation prevents successor operations only. It is not process cancellation, run Stop or Pause.',
    'The host checks the source again when you authorize and authorizes nothing if it changed. This window never advances steps and stores no authority.',
  ]
}

const BLOCKED_NOTE =
  'Withheld: a local execution sequence holds automatic authority over this run, its workspace or its verification recipes, or still owns an unresolved operation. Use Stop after current operation and wait for it to drain.'
const UNKNOWN_NOTE =
  'Withheld until the local execution sequence status is confirmed: whether a local execution sequence holds automatic authority over this run could not be established.'

/** The fixed reason a manual mutation is withheld by the local-execution fence, or null when this fence withholds nothing. */
export function describeLocalExecutionSequenceWithholding(control: LocalExecutionSequenceControl): string | null {
  return control === 'unknown' ? UNKNOWN_NOTE : control === 'blocked' ? BLOCKED_NOTE : null
}

/** The manual-action veto of this fence in the shape every Agent claim control already honours; only ever a veto. */
export function deriveLocalExecutionSequenceClaimBlock(control: LocalExecutionSequenceControl): GlobalAgentClaimBlock | null {
  return control === 'unknown'
    ? { reason: 'LocalExecutionSequenceStatusUnknown' }
    : control === 'blocked'
      ? { reason: 'LocalExecutionSequenceAutomaticAuthority' }
      : null
}

export const LOCAL_EXECUTION_CLAIM_BLOCK_MESSAGES = {
  LocalExecutionSequenceAutomaticAuthority: BLOCKED_NOTE,
  LocalExecutionSequenceStatusUnknown: UNKNOWN_NOTE,
} as const

export const LOCAL_EXECUTION_STATE_LABEL: Readonly<Record<LocalExecutionSequenceStateName, string>> = {
  Armed: 'Armed',
  PreparingStep: 'Preparing the next operation',
  AwaitingStepResult: 'Awaiting an operation result',
  Stopping: 'Stopping after the current operation',
  ReadyForHumanApproval: 'Awaiting Human approval',
  HumanEscalation: 'Awaiting a Human decision',
  Stopped: 'Stopped',
  NeedsAttention: 'Needs attention',
  Interrupted: 'Interrupted',
}

export const LOCAL_EXECUTION_STEP_KIND_LABEL: Readonly<Record<LocalExecutionStepKind, string>> = {
  Implementation: 'Implementation (Claude Code)',
  Verification: 'Verification recipe',
  CodeReview: 'Code review (Codex)',
  VerificationDiagnosis: 'Verification diagnosis (Codex)',
  ReviewCorrection: 'Correction after review',
  DiagnosisCorrection: 'Correction after diagnosis',
}

export const LOCAL_EXECUTION_STEP_STATE_LABEL: Readonly<Record<LocalExecutionStepState, string>> = {
  Reserved: 'Reserved, not yet claimed',
  Claimed: 'Claimed',
  Concluded: 'Concluded',
  Revoked: 'Revoked before any claim',
  Unresolved: 'Unresolved: not proved claimed or absent',
}

/** The exact disclosure of the outcome recorded when a sequence no longer authorized dispatching a claimed Agent operation. */
export const LOCAL_EXECUTION_DISPATCH_REFUSED_LABEL =
  'Not started: the local execution sequence no longer authorized dispatching it (no provider was invoked, no output recorded, no budget refunded)'

/** The same disclosure for a verification execution: no process was started. */
export const LOCAL_EXECUTION_VERIFICATION_DISPATCH_REFUSED_LABEL =
  'Not started: the local execution sequence no longer authorized dispatching it (no process was started and no exit was recorded)'

export const LOCAL_EXECUTION_DISPATCH_REFUSED = 'LocalExecutionSequenceDispatchRefused'

/** The dispatch marker of a step, worded as bookkeeping only. */
export function describeLocalExecutionStepOperation(step: LocalExecutionStep): string {
  if (step.operationId === null) {
    return 'No operation is linked.'
  }

  if (step.operationStatus === null) {
    return 'The linked operation could not be read.'
  }

  const outcome =
    step.operationOutcome === null
      ? ''
      : step.operationOutcome === LOCAL_EXECUTION_DISPATCH_REFUSED
        ? ` · ${LOCAL_EXECUTION_DISPATCH_REFUSED_LABEL}`
        : ` · outcome ${step.operationOutcome}`
  const status = step.operationStatus === LOCAL_EXECUTION_DISPATCH_REFUSED ? LOCAL_EXECUTION_DISPATCH_REFUSED_LABEL : `recorded status ${step.operationStatus}`
  const dispatch = step.dispatched ? 'dispatch recorded' : 'claimed, not yet dispatched'
  const exit = step.dispatched ? (step.exitRecorded ? 'exit evidence recorded' : 'no exit evidence recorded') : 'no process evidence'
  return `${status}${outcome} · ${dispatch} · ${exit}`
}

export const LOCAL_EXECUTION_QUARANTINE_NOTE =
  'This operation ended after it was dispatched without recorded process exit evidence. It stays owned and excluded; it is not described as a running process. '
  + `${LOCAL_EXECUTION_NO_RECOVERY}.`

/** The fixed statement of what a state means; the host's own reason and next-action texts are shown beside it, as literal text. */
export function describeLocalExecutionState(sequence: LocalExecutionSequenceDetails): string {
  const claimed = sequence.steps.some((step) => step.state === 'Claimed')
  switch (sequence.state) {
    case 'Armed':
      return 'Armed: nothing is reserved yet. No operation has been claimed.'
    case 'PreparingStep':
      return 'Preparing the next operation: it is reserved but not yet claimed. Nothing has been dispatched for it.'
    case 'AwaitingStepResult':
      return claimed
        ? 'An operation is claimed and its result is awaited. See the receipts below for whether a dispatch is recorded. A claim, a dispatch marker or a timer is not proof that a process is running.'
        : 'Between operations: the previous one is concluded and the next one is not yet reserved.'
    case 'Stopping':
      return 'Stopping: Stop after current operation was requested. The already claimed operation may still dispatch and finish; no successor will be started.'
    case 'ReadyForHumanApproval':
      return LOCAL_EXECUTION_APPROVAL_NOTE
    case 'HumanEscalation':
      return 'The cycle ended at a recorded escalation and awaits a Human decision. Nothing is approved or committed.'
    case 'Stopped':
      return 'Stopped: no successor operation will be started.'
    case 'NeedsAttention':
      return 'Needs attention: automatic advancement is closed and nothing is retried.'
    case 'Interrupted':
      return 'Interrupted by a host restart. It never resumes automatically.'
  }
}

const FAILURE_COPY: Readonly<Record<string, string>> = {
  'local_execution_sequence.not_found': 'This local execution sequence was not found for this run.',
  'local_execution_sequence.run_not_manual': 'Only a manual Agent run can be armed for a local execution sequence.',
  'local_execution_sequence.run_not_running': 'Only a running run can be armed for a local execution sequence.',
  'local_execution_sequence.prior_mutation':
    'A run that already claimed a workspace-mutating Agent attempt, or has a running attempt, cannot arm a local execution sequence.',
  'local_execution_sequence.already_armed': 'This run already has a local execution sequence; it is armed at most once per run.',
  'local_execution_sequence.plan_not_found': 'The selected plan was not found for this run.',
  'local_execution_sequence.plan_not_eligible':
    'The selected plan is not an explicitly accepted or separately Human-authorized plan that can be implemented in the current source.',
  'local_execution_sequence.planning_active': 'A planning sequence of this run still holds its automatic authority.',
  'local_execution_sequence.project_busy':
    'An attempt, verification execution, local commit or workspace of this project is active or in an unknown state, so nothing was armed.',
  'local_execution_sequence.workspace_not_ready': 'The latest isolated workspace of this project must be ready and owned to arm a local execution sequence.',
  'local_execution_sequence.lease_not_active': 'An active workspace lease is required to arm a local execution sequence.',
  'local_execution_sequence.checkpoint_missing': 'A current Git checkpoint is required to arm a local execution sequence.',
  'local_execution_sequence.workspace_owned': 'The workspace has a recorded or unresolved execution owner, or its ownership cannot be trusted.',
  'local_execution_sequence.capacity_unavailable': 'The recorded global mutating capacity is at its limit or cannot be trusted.',
  'local_execution_sequence.recipes_missing': 'At least one enabled verification recipe is required to arm a local execution sequence.',
  'local_execution_sequence.recipes_too_many': 'At most 32 enabled verification recipes can be pinned.',
  'local_execution_sequence.recipes_incoherent': 'The enabled verification recipes are not a coherent, uniquely numbered set.',
  'local_execution_sequence.settings_unavailable': 'The requested settings, stop policies or budget policies of this run could not be read safely.',
  'local_execution_sequence.tuple_stale':
    'The workspace, checkpoint and fingerprint shown are no longer the current source of this project. Nothing was armed; read the availability again.',
  'local_execution_sequence.confirmation_stale':
    'The confirmed plan, recipes, settings or allowance changed after the availability was read. Nothing was armed; read the availability again.',
  'local_execution_sequence.tuple_conflict': 'This run was already armed for a different plan, source or confirmation. Nothing was changed.',
  'local_execution_sequence.manual_control_blocked':
    'A local execution sequence still holds automatic authority; use Stop after current operation and wait for it to drain.',
  'local_execution_sequence.step_authority_lost': 'The local execution sequence no longer authorizes this step; nothing was claimed.',
  'local_execution_sequence.step_expired': 'The step preparation deadline elapsed; nothing was claimed.',
  'local_execution_sequence.step_source_changed': 'The pinned source, plan, recipes or settings of the local execution sequence no longer hold.',
  'local_execution_sequence.step_input_mismatch': 'The claim does not consume the exact predecessor input that the local execution sequence step expects.',
  'local_execution_sequence.allowance_reached': 'The authorized automatic correction allowance was reached; nothing was claimed.',
  'local_execution_sequence.persistence_failed': 'The local execution sequence change could not be recorded. The status is being read again.',
  'local_execution_sequence.persistence_unresolved': 'Whether the local execution sequence change was recorded could not be confirmed. The status is being read again.',
  'local_execution_sequence.invalid_request': 'The request was not accepted by the host.',
  'local_execution_sequence.body_too_large': 'The request was not accepted by the host.',
  'local_execution_sequence.body_not_allowed': 'The request was not accepted by the host.',
}

const GENERIC_REFUSAL = 'The local execution sequence request could not be completed. The status is being read again.'

/** The fixed copy of an arming refusal code (the availability's own refusal), or the generic line for any other code. */
export const describeLocalExecutionRefusalCode = (code: string | null): string =>
  code !== null && Object.hasOwn(FAILURE_COPY, code) ? FAILURE_COPY[code] : 'Arming is not available.'

export type LocalExecutionRequestFailure = { kind: 'unknown' } | { kind: 'refused'; code: string | null; message: string }

/** Raised by an action when the host's success answer is not exactly the answer for the request: the host may still have recorded it. */
export class LocalExecutionAnswerInvalid extends Error {
  constructor() {
    super('The local execution sequence answer was not valid for the request.')
    this.name = 'LocalExecutionAnswerInvalid'
  }
}

/**
 * A failure with no HTTP response, an aborted or timed-out request, an answer that failed validation or a 5xx has an unknown outcome:
 * the host may have recorded the request, so only the recorded status is read again. Any other HTTP answer is a definite refusal worded
 * from its stable code (or its status) with fixed copy; server text is never echoed.
 */
export function classifyLocalExecutionFailure(caught: unknown): LocalExecutionRequestFailure {
  const problem = problemOf(caught)
  if (problem === null || problem.status === undefined || problem.status >= 500 || problem.status < 100) {
    return { kind: 'unknown' }
  }

  if (problem.code !== null && Object.hasOwn(FAILURE_COPY, problem.code)) {
    return { kind: 'refused', code: problem.code, message: FAILURE_COPY[problem.code] }
  }

  switch (problem.status) {
    case 400:
    case 413:
      return { kind: 'refused', code: problem.code, message: 'The request was not accepted by the host.' }
    case 404:
      return { kind: 'refused', code: problem.code, message: 'This run or local execution sequence could not be found.' }
    case 409:
      return { kind: 'refused', code: problem.code, message: 'The local execution sequence changed or the request can no longer be accepted. The status is being read again.' }
    default:
      return { kind: 'refused', code: problem.code, message: GENERIC_REFUSAL }
  }
}

export const localExecutionFailureMessage = (caught: unknown): string => {
  const failure = classifyLocalExecutionFailure(caught)
  return failure.kind === 'unknown' ? LOCAL_EXECUTION_UNKNOWN_OUTCOME : failure.message
}

const EVENT_STATE_PHRASE: Readonly<Record<LocalExecutionSequenceStateName, string>> = {
  Armed: 'armed',
  PreparingStep: 'preparing an operation',
  AwaitingStepResult: 'awaiting an operation result',
  Stopping: 'stopping after the current operation',
  ReadyForHumanApproval: 'awaiting Human approval (not committed, not published)',
  HumanEscalation: 'awaiting a Human decision',
  Stopped: 'stopped',
  NeedsAttention: 'needs attention',
  Interrupted: 'interrupted',
}

export const describeLocalExecutionEventState = (state: LocalExecutionSequenceStateName): string => EVENT_STATE_PHRASE[state]

/** The short fixed clause the run event summary uses for a closed reason code. */
export const LOCAL_EXECUTION_REASON_PHRASE: Readonly<Record<LocalExecutionReasonCode, string>> = {
  review_approved: 'an ordinary review approved the linked report',
  allowance_reached: 'the correction allowance was reached with unresolved findings',
  diagnosis_escalated: 'the verification diagnosis escalated',
  stopped_before_claim: 'stopped before the next operation was claimed',
  stopped_after_operation: 'stopped after the claimed operation finished',
  preparation_refused: 'the next operation claim was refused',
  preparation_expired: 'the next operation was not claimed in time',
  preparation_failed: 'preparing the next operation failed',
  preparation_unresolved: 'whether the next operation was claimed could not be confirmed',
  result_deadline_elapsed: 'the result deadline elapsed (no process was cancelled)',
  advancement_deadline_elapsed: 'the two-hour advancement deadline elapsed',
  operation_failed: 'a claimed operation did not complete as expected',
  operation_outcome_invalid: 'a recorded operation result could not be proved',
  exit_evidence_missing: 'a dispatched operation ended without recorded exit evidence (no supported recovery action in this version)',
  verification_not_diagnosable: 'the verification pass could not be diagnosed',
  authority_changed: 'the pinned source, plan, recipes or settings changed',
  capacity_refused: 'workspace or global capacity did not allow dispatch',
  host_restarted: 'the host restarted',
}

export const LOCAL_EXECUTION_EVENT_SUMMARIES = {
  armed: 'A Human authorized a local execution cycle. Arming started no provider, ran no verification and consumed no budget.',
  step_reserved: 'The host reserved the next local execution operation. A reservation is not a claim and consumes no budget.',
  step_claimed: 'The host claimed a local execution operation. A claim is not proof that a process is running.',
  step_concluded: 'The host recorded the end of a claimed local execution operation.',
  stop_requested: 'A Human chose Stop after current operation. This is not process cancellation, run Stop or Pause.',
  ended: 'The local execution sequence ended.',
} as const
