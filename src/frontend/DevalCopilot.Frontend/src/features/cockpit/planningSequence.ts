import { hasExactKeys, isUtcRoundTrip } from './agentAttemptOutputs'
import { toWireInstant } from './agentAttemptEvidenceWire'

// The closed, whole-answer decoders of the three planning-sequence operations (ADR-0055). Each accepts a parsed host answer only when
// all of it is coherent for exactly the run (and sequence, and tuple) that was asked about: exactly the documented members at every
// level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the consistency rules of the contract.
// Anything else is null and is never repaired, trimmed, coerced into permission or partially accepted. The recorded status is not a
// runtime readiness proof: a claim or a dispatch marker in it is bookkeeping and never proof of a live provider process.

/** The most steps a sequence has: Planner, CriticalReviewer, Resolver, CriticalReviewer, Resolver. */
export const PLANNING_SEQUENCE_MAXIMUM_STEPS = 5

export const PLANNING_SEQUENCE_STATES = [
  'Armed',
  'PreparingStep',
  'AwaitingStepResult',
  'Stopping',
  'ReadyForImplementation',
  'AwaitingHumanDecision',
  'Stopped',
  'NeedsAttention',
  'Interrupted',
] as const
export type PlanningSequenceState = (typeof PLANNING_SEQUENCE_STATES)[number]

/** The recorded states that still hold automatic authority: the sequence can advance or drains an already claimed step. */
export const PLANNING_SEQUENCE_LIVE_STATES: readonly PlanningSequenceState[] = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping']

export const PLANNING_SEQUENCE_STEP_STATES = ['Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved'] as const
export type PlanningSequenceStepState = (typeof PLANNING_SEQUENCE_STEP_STATES)[number]

export type PlanningSequenceRole = 'Planner' | 'CriticalReviewer' | 'Resolver'
export type PlanningSequenceProvider = 'Codex' | 'ClaudeCode'
export type PlanningSequenceManualFence = 'Blocked' | 'Released' | 'Unavailable'

export const PLANNING_SEQUENCE_REASON_CODES = [
  'plan_accepted',
  'second_round_escalated',
  'stopped_before_claim',
  'stopped_after_step',
  'preparation_refused',
  'preparation_expired',
  'preparation_failed',
  'preparation_unresolved',
  'result_deadline_elapsed',
  'step_failed',
  'step_outcome_invalid',
  'authority_changed',
  'host_restarted',
] as const
export type PlanningSequenceReasonCode = (typeof PLANNING_SEQUENCE_REASON_CODES)[number]

export const PLANNING_SEQUENCE_NEXT_ACTION_CODES = [
  'wait_or_stop_after_current_step',
  'wait_for_current_step_to_finish',
  'review_accepted_plan_then_request_implementation_separately',
  'decide_on_the_recorded_escalation',
  'continue_with_explicit_manual_work',
  'inspect_and_reconcile',
] as const
export type PlanningSequenceNextActionCode = (typeof PLANNING_SEQUENCE_NEXT_ACTION_CODES)[number]

export const PLANNING_SEQUENCE_ARMING_REFUSAL_CODES = [
  'planning_sequence.run_not_manual',
  'planning_sequence.run_not_pristine',
  'planning_sequence.project_busy',
  'planning_sequence.workspace_not_ready',
  'planning_sequence.lease_not_active',
  'planning_sequence.checkpoint_missing',
] as const
export type PlanningSequenceArmingRefusalCode = (typeof PLANNING_SEQUENCE_ARMING_REFUSAL_CODES)[number]

const ATTEMPT_STATUSES = ['Running', 'Completed', 'Failed', 'Interrupted'] as const
export type PlanningSequenceAttemptStatus = (typeof ATTEMPT_STATUSES)[number]

/** Whether a (recorded or effective) state still holds automatic authority. */
export const isPlanningSequenceLive = (state: PlanningSequenceState): boolean => PLANNING_SEQUENCE_LIVE_STATES.includes(state)

export interface PlanningSequenceStep {
  stepId: string
  ordinal: number
  role: PlanningSequenceRole
  provider: PlanningSequenceProvider
  state: PlanningSequenceStepState
  reasonCode: PlanningSequenceReasonCode | null
  attemptId: string | null
  attemptNumber: number | null
  attemptStatus: PlanningSequenceAttemptStatus | null
  /** The recorded outcome name of the linked attempt; displayed as text, never interpreted as authority. */
  attemptOutcome: string | null
  /** Whether a dispatch marker exists: bookkeeping, never proof that a provider process is running. */
  dispatched: boolean | null
  /** The UTC round-trip texts exactly as the host wrote them. */
  preparationReservedAtUtc: string
  preparationDeadlineUtc: string
  claimedAtUtc: string | null
  resultDeadlineUtc: string | null
}

export interface PlanningSequenceFinal {
  finalProposalMessageId: string
  acceptanceMessageId: string | null
  escalationMessageId: string | null
  provingAttemptId: string
}

export interface PlanningSequenceDetails {
  sequenceId: string
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  /** The effective state: an elapsed deadline reads as NeedsAttention before the host persisted it. */
  state: PlanningSequenceState
  recordedState: PlanningSequenceState
  observedBeforePersisted: boolean
  reasonCode: PlanningSequenceReasonCode | null
  reasonText: string | null
  detailCode: string | null
  nextActionCode: PlanningSequenceNextActionCode
  nextActionText: string
  /** The canonical Int64 text exactly as written; never converted to a number. */
  revision: string
  armedAtUtc: string
  updatedAtUtc: string
  stopRequestedAtUtc: string | null
  endedAtUtc: string | null
  awaitingReconciliation: boolean
  outstandingAttemptId: string | null
  maximumSteps: number
  steps: readonly PlanningSequenceStep[]
  final: PlanningSequenceFinal | null
}

export interface PlanningSequenceArming {
  available: boolean
  refusalCode: PlanningSequenceArmingRefusalCode | null
  refusalText: string | null
  workspaceId: string | null
  checkpointId: string | null
  fingerprintSha256: string | null
}

/** The exact tuple the Human saw and arms; the only caller-supplied facts of arming. */
export interface PlanningSequenceTuple {
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
}

export interface PlanningSequenceStatus {
  runId: string
  /** `None`: no sequence is recorded. Otherwise exactly the effective state of `sequence`. */
  status: 'None' | PlanningSequenceState
  sequence: PlanningSequenceDetails | null
  arming: PlanningSequenceArming | null
  manualFence: PlanningSequenceManualFence
  /** The canonical Int64 text (zero when the run has no events). */
  latestEventSequence: string
}

export interface PlanningSequenceArmReceipt {
  sequenceId: string
  runId: string
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  state: PlanningSequenceState
  armedAtUtc: string
  revision: string
  /** True when the identical tuple was already armed: the recorded receipt, nothing was armed again. */
  replayed: boolean
}

export interface PlanningSequenceStopAnswer {
  sequenceId: string
  runId: string
  state: PlanningSequenceState
  reasonCode: PlanningSequenceReasonCode | null
  revision: string
  /** True only when this call recorded a transition. */
  changed: boolean
}

/**
 * Who holds automatic authority over a run, derived from one validated status only: `unknown` (nothing validated, or the host could not
 * establish the fence), `blocked` (a sequence holds authority or has an unresolved linked attempt), `released` (a sequence exists and its
 * fence is lifted) and `none` (no sequence is recorded). `released` and `none` lift only this fence: they never assert that an
 * ordinary manual operation is eligible.
 */
export type PlanningSequenceControl = 'unknown' | 'blocked' | 'released' | 'none'

export function derivePlanningSequenceControl(status: PlanningSequenceStatus | null): PlanningSequenceControl {
  if (status === null || status.manualFence === 'Unavailable') {
    return 'unknown'
  }

  if (status.manualFence === 'Blocked') {
    return 'blocked'
  }

  return status.sequence === null ? 'none' : 'released'
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/
const CANONICAL_POSITIVE = /^[1-9][0-9]{0,18}$/
const CANONICAL_NON_NEGATIVE = /^(0|[1-9][0-9]{0,18})$/
const DETAIL_CODE = /^[a-z0-9._]{1,96}$/
const OUTCOME_NAME = /^[A-Za-z][A-Za-z0-9]{0,63}$/
const UNSAFE_TEXT = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u
const MAX_INT64 = (1n << 63n) - 1n
const MAX_HOST_TEXT = 400
const PREPARATION_WINDOW_MILLISECONDS = 120_000

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T => typeof value === 'string' && (values as readonly string[]).includes(value)
const isHostText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_HOST_TEXT && !UNSAFE_TEXT.test(value)
const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'

const isInt64Text = (value: unknown, pattern: RegExp): value is string =>
  typeof value === 'string' && pattern.test(value) && BigInt(value) <= MAX_INT64

/** A canonical positive Int64 decimal text (a revision). */
export const isRevisionText = (value: unknown): value is string => isInt64Text(value, CANONICAL_POSITIVE)

/** A canonical non-negative Int64 decimal text (an event sequence: zero when the run has no events). */
export const isEventSequenceText = (value: unknown): value is string => isInt64Text(value, CANONICAL_NON_NEGATIVE)

const roleOf = (ordinal: number): PlanningSequenceRole => (ordinal === 0 ? 'Planner' : ordinal === 1 || ordinal === 3 ? 'CriticalReviewer' : 'Resolver')
const providerOf = (role: PlanningSequenceRole): PlanningSequenceProvider => (role === 'CriticalReviewer' ? 'ClaudeCode' : 'Codex')

// The reason the Domain records with each ended state; a live state records none. The effective NeedsAttention read before persistence
// is the only live recorded state that carries a reason (see `toDetails`).
const REASONS_BY_STATE: Readonly<Record<PlanningSequenceState, readonly PlanningSequenceReasonCode[]>> = {
  Armed: [],
  PreparingStep: [],
  AwaitingStepResult: [],
  Stopping: [],
  ReadyForImplementation: ['plan_accepted'],
  AwaitingHumanDecision: ['second_round_escalated'],
  Stopped: ['stopped_before_claim', 'stopped_after_step'],
  NeedsAttention: [
    'preparation_refused',
    'preparation_expired',
    'preparation_failed',
    'preparation_unresolved',
    'result_deadline_elapsed',
    'step_failed',
    'step_outcome_invalid',
    'authority_changed',
  ],
  Interrupted: ['host_restarted'],
}

const NEXT_ACTION_BY_STATE: Readonly<Record<PlanningSequenceState, PlanningSequenceNextActionCode>> = {
  Armed: 'wait_or_stop_after_current_step',
  PreparingStep: 'wait_or_stop_after_current_step',
  AwaitingStepResult: 'wait_or_stop_after_current_step',
  Stopping: 'wait_for_current_step_to_finish',
  ReadyForImplementation: 'review_accepted_plan_then_request_implementation_separately',
  AwaitingHumanDecision: 'decide_on_the_recorded_escalation',
  Stopped: 'continue_with_explicit_manual_work',
  NeedsAttention: 'inspect_and_reconcile',
  Interrupted: 'inspect_and_reconcile',
}

const STEP_KEYS = [
  'attemptId',
  'attemptNumber',
  'attemptOutcome',
  'attemptStatus',
  'claimedAtUtc',
  'dispatched',
  'ordinal',
  'preparationDeadlineUtc',
  'preparationReservedAtUtc',
  'provider',
  'reasonCode',
  'resultDeadlineUtc',
  'role',
  'state',
  'stepId',
]
const FINAL_KEYS = ['acceptanceMessageId', 'escalationMessageId', 'finalProposalMessageId', 'provingAttemptId']
const SEQUENCE_KEYS = [
  'armedAtUtc',
  'awaitingReconciliation',
  'checkpointId',
  'detailCode',
  'endedAtUtc',
  'final',
  'fingerprintSha256',
  'maximumSteps',
  'nextActionCode',
  'nextActionText',
  'observedBeforePersisted',
  'outstandingAttemptId',
  'reasonCode',
  'reasonText',
  'recordedState',
  'revision',
  'sequenceId',
  'state',
  'steps',
  'stopRequestedAtUtc',
  'updatedAtUtc',
  'workspaceId',
]
const ARMING_KEYS = ['available', 'checkpointId', 'fingerprintSha256', 'refusalCode', 'refusalText', 'workspaceId']
const STATUS_KEYS = ['arming', 'latestEventSequence', 'manualFence', 'runId', 'sequence', 'status']
const RECEIPT_KEYS = ['armedAtUtc', 'checkpointId', 'fingerprintSha256', 'replayed', 'revision', 'runId', 'sequenceId', 'state', 'workspaceId']
const STOP_KEYS = ['changed', 'reasonCode', 'revision', 'runId', 'sequenceId', 'state']

const isAbsentOr = <T>(value: unknown, accepts: (candidate: unknown) => candidate is T): value is T | null => value === null || accepts(value)

function toStep(raw: unknown, ordinal: number): PlanningSequenceStep | null {
  if (!hasExactKeys(raw, STEP_KEYS) || raw.ordinal !== ordinal) {
    return null
  }

  const role = roleOf(ordinal)
  if (
    !isGuid(raw.stepId)
    || raw.role !== role
    || raw.provider !== providerOf(role)
    || !isOneOf(PLANNING_SEQUENCE_STEP_STATES, raw.state)
    || !isUtcRoundTrip(raw.preparationReservedAtUtc)
    || !isUtcRoundTrip(raw.preparationDeadlineUtc)
  ) {
    return null
  }

  // The host reserves the preparation intent for exactly two minutes.
  const reservedAt = toWireInstant(raw.preparationReservedAtUtc)
  const deadlineAt = toWireInstant(raw.preparationDeadlineUtc)
  if (reservedAt === null || deadlineAt === null || deadlineAt.getTime() - reservedAt.getTime() !== PREPARATION_WINDOW_MILLISECONDS) {
    return null
  }

  // A reason belongs only to a step whose preparation authority was revoked or left unresolved.
  const stepEndedWithReason = raw.state === 'Revoked' || raw.state === 'Unresolved'
  if (stepEndedWithReason ? !isOneOf(PLANNING_SEQUENCE_REASON_CODES, raw.reasonCode) : raw.reasonCode !== null) {
    return null
  }

  // The attempt link exists exactly for a claimed or concluded step, and then carries its whole recorded receipt.
  const linked = raw.state === 'Claimed' || raw.state === 'Concluded'
  if (!linked) {
    const noLink = [raw.attemptId, raw.attemptNumber, raw.attemptStatus, raw.attemptOutcome, raw.dispatched, raw.claimedAtUtc, raw.resultDeadlineUtc]
    return noLink.every((value) => value === null) ? toUnlinkedStep(raw, role) : null
  }

  const { attemptNumber, attemptStatus, attemptOutcome } = raw
  if (
    !isGuid(raw.attemptId)
    || typeof attemptNumber !== 'number'
    || !Number.isInteger(attemptNumber)
    || attemptNumber < 1
    || !isOneOf(ATTEMPT_STATUSES, attemptStatus)
    || !isBoolean(raw.dispatched)
    || !isUtcRoundTrip(raw.claimedAtUtc)
    || !isAbsentOr(raw.resultDeadlineUtc, isUtcRoundTrip)
    || !isAbsentOr(attemptOutcome, (value): value is string => typeof value === 'string' && OUTCOME_NAME.test(value))
  ) {
    return null
  }

  // A running attempt has no outcome yet, and a concluded step has a terminal attempt.
  if ((attemptStatus === 'Running' && attemptOutcome !== null) || (raw.state === 'Concluded' && attemptStatus === 'Running')) {
    return null
  }

  return {
    stepId: raw.stepId,
    ordinal,
    role,
    provider: providerOf(role),
    state: raw.state,
    reasonCode: null,
    attemptId: raw.attemptId,
    attemptNumber,
    attemptStatus,
    attemptOutcome,
    dispatched: raw.dispatched,
    preparationReservedAtUtc: raw.preparationReservedAtUtc,
    preparationDeadlineUtc: raw.preparationDeadlineUtc,
    claimedAtUtc: raw.claimedAtUtc,
    resultDeadlineUtc: raw.resultDeadlineUtc,
  }
}

function toUnlinkedStep(raw: Record<string, unknown>, role: PlanningSequenceRole): PlanningSequenceStep {
  return {
    stepId: raw.stepId as string,
    ordinal: raw.ordinal as number,
    role,
    provider: providerOf(role),
    state: raw.state as PlanningSequenceStepState,
    reasonCode: raw.reasonCode as PlanningSequenceReasonCode | null,
    attemptId: null,
    attemptNumber: null,
    attemptStatus: null,
    attemptOutcome: null,
    dispatched: null,
    preparationReservedAtUtc: raw.preparationReservedAtUtc as string,
    preparationDeadlineUtc: raw.preparationDeadlineUtc as string,
    claimedAtUtc: null,
    resultDeadlineUtc: null,
  }
}

function toSteps(raw: unknown): PlanningSequenceStep[] | null {
  if (!Array.isArray(raw) || raw.length > PLANNING_SEQUENCE_MAXIMUM_STEPS) {
    return null
  }

  const steps: PlanningSequenceStep[] = []
  const identities = new Set<string>()
  for (const [ordinal, item] of raw.entries()) {
    const step = toStep(item, ordinal)
    if (step === null || identities.has(step.stepId)) {
      return null
    }

    identities.add(step.stepId)
    steps.push(step)
  }

  // Steps are strictly sequential: every step before the last concluded, and at most one step (the last) is outstanding.
  const lastIndex = steps.length - 1
  return steps.every((step, index) => index === lastIndex || step.state === 'Concluded') ? steps : null
}

function toFinal(raw: unknown, state: PlanningSequenceState, steps: readonly PlanningSequenceStep[]): PlanningSequenceFinal | null {
  if (
    !hasExactKeys(raw, FINAL_KEYS)
    || !isGuid(raw.finalProposalMessageId)
    || !isGuid(raw.provingAttemptId)
    || !isAbsentOr(raw.acceptanceMessageId, isGuid)
    || !isAbsentOr(raw.escalationMessageId, isGuid)
  ) {
    return null
  }

  // An accepted plan names exactly its Acceptance and an escalated one exactly its Escalation, each proved by the last concluded step.
  const accepted = state === 'ReadyForImplementation'
  const escalated = state === 'AwaitingHumanDecision'
  const last = steps[steps.length - 1]
  if (
    !(accepted ? raw.acceptanceMessageId !== null && raw.escalationMessageId === null : escalated && raw.escalationMessageId !== null && raw.acceptanceMessageId === null)
    || last === undefined
    || last.attemptId !== raw.provingAttemptId
  ) {
    return null
  }

  return {
    finalProposalMessageId: raw.finalProposalMessageId,
    acceptanceMessageId: raw.acceptanceMessageId,
    escalationMessageId: raw.escalationMessageId,
    provingAttemptId: raw.provingAttemptId,
  }
}

// The step table each ended or live state implies (ADR-0055), judged on the recorded state.
function stepsAgreeWithState(recorded: PlanningSequenceState, steps: readonly PlanningSequenceStep[]): boolean {
  const last = steps[steps.length - 1]
  switch (recorded) {
    case 'Armed':
      return steps.length === 0
    case 'PreparingStep':
      return last?.state === 'Reserved'
    case 'AwaitingStepResult':
    case 'Stopping':
      // Between two steps the previous one is concluded and the next is not yet reserved.
      return last?.state === 'Claimed' || last?.state === 'Concluded'
    case 'ReadyForImplementation':
      return last?.state === 'Concluded' && (last.ordinal === 1 || last.ordinal === 3)
    case 'AwaitingHumanDecision':
      return last?.state === 'Concluded' && last.ordinal === 4
    default:
      return true
  }
}

function toDetails(raw: unknown): PlanningSequenceDetails | null {
  if (!hasExactKeys(raw, SEQUENCE_KEYS)) {
    return null
  }

  const { state, recordedState, observedBeforePersisted, reasonCode, reasonText, detailCode } = raw
  if (
    !isGuid(raw.sequenceId)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isSha256(raw.fingerprintSha256)
    || !isOneOf(PLANNING_SEQUENCE_STATES, state)
    || !isOneOf(PLANNING_SEQUENCE_STATES, recordedState)
    || !isBoolean(observedBeforePersisted)
    || !isRevisionText(raw.revision)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || !isAbsentOr(raw.stopRequestedAtUtc, isUtcRoundTrip)
    || !isAbsentOr(raw.endedAtUtc, isUtcRoundTrip)
    || !isBoolean(raw.awaitingReconciliation)
    || !isAbsentOr(raw.outstandingAttemptId, isGuid)
    || raw.maximumSteps !== PLANNING_SEQUENCE_MAXIMUM_STEPS
    || !isOneOf(PLANNING_SEQUENCE_NEXT_ACTION_CODES, raw.nextActionCode)
    || !isHostText(raw.nextActionText)
  ) {
    return null
  }

  // The effective state is the recorded one, except that an elapsed deadline reads as NeedsAttention before the host persisted it.
  if (observedBeforePersisted) {
    const expectedReason = recordedState === 'PreparingStep' ? 'preparation_expired' : 'result_deadline_elapsed'
    if (
      state !== 'NeedsAttention'
      || (recordedState !== 'PreparingStep' && recordedState !== 'AwaitingStepResult' && recordedState !== 'Stopping')
      || reasonCode !== expectedReason
      || detailCode !== null
    ) {
      return null
    }
  } else if (state !== recordedState || !(reasonCode === null ? REASONS_BY_STATE[state].length === 0 : isOneOf(REASONS_BY_STATE[state], reasonCode))) {
    return null
  }

  // Reason text exists exactly with a reason code; a detail code sharpens only an attention reason.
  if (reasonCode === null ? reasonText !== null : !isHostText(reasonText)) {
    return null
  }

  if (!(detailCode === null || (typeof detailCode === 'string' && DETAIL_CODE.test(detailCode) && state === 'NeedsAttention'))) {
    return null
  }

  if (raw.nextActionCode !== NEXT_ACTION_BY_STATE[state]) {
    return null
  }

  // Times that belong to the recorded transitions: an ended sequence has its end time, a live one never, and a stop was requested
  // before it was Stopping or Stopped.
  const recordedLive = isPlanningSequenceLive(recordedState)
  if ((raw.endedAtUtc === null) !== recordedLive) {
    return null
  }

  if ((recordedState === 'Stopping' || recordedState === 'Stopped') && raw.stopRequestedAtUtc === null) {
    return null
  }

  if ((recordedState === 'Armed' || recordedState === 'PreparingStep' || recordedState === 'AwaitingStepResult') && raw.stopRequestedAtUtc !== null) {
    return null
  }

  const steps = toSteps(raw.steps)
  if (steps === null || !stepsAgreeWithState(recordedState, steps)) {
    return null
  }

  // The outstanding attempt is the linked attempt that is still Running; it awaits reconciliation once the state no longer holds authority.
  const outstanding = steps.find((step) => step.attemptStatus === 'Running')?.attemptId ?? null
  if (raw.outstandingAttemptId !== outstanding || raw.awaitingReconciliation !== (outstanding !== null && !isPlanningSequenceLive(state))) {
    return null
  }

  let final: PlanningSequenceFinal | null = null
  if (raw.final !== null) {
    final = toFinal(raw.final, state, steps)
    if (final === null) {
      return null
    }
  }

  return {
    sequenceId: raw.sequenceId,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    fingerprintSha256: raw.fingerprintSha256,
    state,
    recordedState,
    observedBeforePersisted,
    reasonCode: reasonCode as PlanningSequenceReasonCode | null,
    reasonText: reasonText as string | null,
    detailCode: detailCode as string | null,
    nextActionCode: raw.nextActionCode,
    nextActionText: raw.nextActionText,
    revision: raw.revision,
    armedAtUtc: raw.armedAtUtc,
    updatedAtUtc: raw.updatedAtUtc,
    stopRequestedAtUtc: raw.stopRequestedAtUtc,
    endedAtUtc: raw.endedAtUtc,
    awaitingReconciliation: raw.awaitingReconciliation,
    outstandingAttemptId: raw.outstandingAttemptId,
    maximumSteps: PLANNING_SEQUENCE_MAXIMUM_STEPS,
    steps,
    final,
  }
}

function toArming(raw: unknown): PlanningSequenceArming | null {
  if (!hasExactKeys(raw, ARMING_KEYS) || !isBoolean(raw.available)) {
    return null
  }

  if (raw.available) {
    return raw.refusalCode === null
      && raw.refusalText === null
      && isGuid(raw.workspaceId)
      && isGuid(raw.checkpointId)
      && isSha256(raw.fingerprintSha256)
      ? {
          available: true,
          refusalCode: null,
          refusalText: null,
          workspaceId: raw.workspaceId,
          checkpointId: raw.checkpointId,
          fingerprintSha256: raw.fingerprintSha256,
        }
      : null
  }

  return isOneOf(PLANNING_SEQUENCE_ARMING_REFUSAL_CODES, raw.refusalCode)
    && isHostText(raw.refusalText)
    && raw.workspaceId === null
    && raw.checkpointId === null
    && raw.fingerprintSha256 === null
    ? {
        available: false,
        refusalCode: raw.refusalCode,
        refusalText: raw.refusalText,
        workspaceId: null,
        checkpointId: null,
        fingerprintSha256: null,
      }
    : null
}

/**
 * Accepts a parsed recorded status only when the whole of it is coherent for exactly `expectedRunId`: the exact members, a closed
 * status, fence and vocabularies, canonical identifiers, times and Int64 texts, and the consistency of the shape. `None` carries no
 * sequence and either the arming availability (fence Released) or neither (the host could not establish the fence: Unavailable). Any
 * other status carries exactly the sequence of that effective state, no arming and a Blocked or Released fence that agrees with the
 * recorded authority and the linked attempts. Anything else is null and is never repaired or partially accepted.
 */
export function toPlanningSequenceStatus(raw: unknown, expectedRunId: string): PlanningSequenceStatus | null {
  if (
    !hasExactKeys(raw, STATUS_KEYS)
    || raw.runId !== expectedRunId
    || !isEventSequenceText(raw.latestEventSequence)
    || !isOneOf(['Blocked', 'Released', 'Unavailable'] as const, raw.manualFence)
  ) {
    return null
  }

  const manualFence = raw.manualFence
  const latestEventSequence = raw.latestEventSequence
  if (raw.status === 'None') {
    if (raw.sequence !== null) {
      return null
    }

    if (manualFence === 'Unavailable') {
      return raw.arming === null ? { runId: expectedRunId, status: 'None', sequence: null, arming: null, manualFence, latestEventSequence } : null
    }

    const arming = manualFence === 'Released' ? toArming(raw.arming) : null
    return arming === null ? null : { runId: expectedRunId, status: 'None', sequence: null, arming, manualFence, latestEventSequence }
  }

  if (!isOneOf(PLANNING_SEQUENCE_STATES, raw.status) || raw.arming !== null || manualFence === 'Unavailable') {
    return null
  }

  const sequence = toDetails(raw.sequence)
  if (sequence === null || sequence.state !== raw.status) {
    return null
  }

  // The fence is Blocked exactly while the recorded state holds authority or a linked attempt is still Running.
  const blocked = isPlanningSequenceLive(sequence.recordedState) || sequence.steps.some((step) => step.attemptStatus === 'Running')
  if ((manualFence === 'Blocked') !== blocked) {
    return null
  }

  return { runId: expectedRunId, status: raw.status, sequence, arming: null, manualFence, latestEventSequence }
}

/**
 * Accepts the parsed arming receipt only when it is exactly the receipt of arming `expectedRunId` with exactly `expectedTuple`: the
 * exact members, the requested run and tuple, a closed state and canonical revision and time. A first arming is Armed at revision 1; a
 * replay is the recorded receipt in whatever state the sequence has reached.
 */
export function toPlanningSequenceArmReceipt(
  raw: unknown,
  expectedRunId: string,
  expectedTuple: PlanningSequenceTuple,
): PlanningSequenceArmReceipt | null {
  if (
    !hasExactKeys(raw, RECEIPT_KEYS)
    || raw.runId !== expectedRunId
    || raw.workspaceId !== expectedTuple.workspaceId
    || raw.checkpointId !== expectedTuple.checkpointId
    || raw.fingerprintSha256 !== expectedTuple.fingerprintSha256
    || !isGuid(raw.sequenceId)
    || !isOneOf(PLANNING_SEQUENCE_STATES, raw.state)
    || !isRevisionText(raw.revision)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isBoolean(raw.replayed)
    || (!raw.replayed && (raw.state !== 'Armed' || raw.revision !== '1'))
  ) {
    return null
  }

  return {
    sequenceId: raw.sequenceId,
    runId: expectedRunId,
    workspaceId: expectedTuple.workspaceId,
    checkpointId: expectedTuple.checkpointId,
    fingerprintSha256: expectedTuple.fingerprintSha256,
    state: raw.state,
    armedAtUtc: raw.armedAtUtc,
    revision: raw.revision,
    replayed: raw.replayed,
  }
}

/**
 * Accepts the parsed Stop answer only when it is exactly the answer for `expectedRunId` and `expectedSequenceId`: the exact members, a
 * closed state a stop can leave (Stopping or an ended state: a stop never leaves a sequence able to advance), the reason that state
 * records, a canonical revision, and `changed` only for a transition a stop makes (Stopping, or Stopped before a claim).
 */
export function toPlanningSequenceStopAnswer(
  raw: unknown,
  expectedRunId: string,
  expectedSequenceId: string,
): PlanningSequenceStopAnswer | null {
  if (
    !hasExactKeys(raw, STOP_KEYS)
    || raw.runId !== expectedRunId
    || raw.sequenceId !== expectedSequenceId
    || !isOneOf(PLANNING_SEQUENCE_STATES, raw.state)
    || !isRevisionText(raw.revision)
    || !isBoolean(raw.changed)
  ) {
    return null
  }

  const state = raw.state
  const reasonCode = raw.reasonCode
  const changed = raw.changed
  if (state === 'Armed' || state === 'PreparingStep' || state === 'AwaitingStepResult') {
    return null
  }

  if (reasonCode === null ? REASONS_BY_STATE[state].length > 0 : !isOneOf(REASONS_BY_STATE[state], reasonCode)) {
    return null
  }

  if (changed && !(state === 'Stopping' || (state === 'Stopped' && reasonCode === 'stopped_before_claim'))) {
    return null
  }

  return {
    sequenceId: expectedSequenceId,
    runId: expectedRunId,
    state,
    reasonCode: reasonCode as PlanningSequenceReasonCode | null,
    revision: raw.revision,
    changed,
  }
}
