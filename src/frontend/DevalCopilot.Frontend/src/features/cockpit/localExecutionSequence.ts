import { hasExactKeys, isUtcRoundTrip } from './agentAttemptOutputs'
import { toWireInstant } from './agentAttemptEvidenceWire'

// The closed, whole-answer decoders of the four local-execution-sequence operations (ADR-0060). Each accepts a parsed host answer only
// when all of it is coherent for exactly the run (plan, sequence and tuple) that was asked about: exactly the documented members at every
// level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the consistency rules of the contract.
// Anything else is null and is never repaired, trimmed, coerced into permission or partially accepted. A claim, a dispatch marker or a
// recorded exit in a step is bookkeeping: it is never proof that a process is running or has stopped.

export const LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES = 32
export const LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS = 2
export const LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES = 3
export const LOCAL_EXECUTION_SEQUENCE_MAXIMUM_AGENT_CLAIMS = 6
const ADVANCEMENT_WINDOW_MILLISECONDS = 2 * 60 * 60 * 1000
const PREPARATION_WINDOW_MILLISECONDS = 120_000

export const maximumLocalExecutionSteps = (recipeCount: number): number => 3 * recipeCount + 6

export const LOCAL_EXECUTION_SEQUENCE_STATES = [
  'Armed',
  'PreparingStep',
  'AwaitingStepResult',
  'Stopping',
  'ReadyForHumanApproval',
  'HumanEscalation',
  'Stopped',
  'NeedsAttention',
  'Interrupted',
  'WaitingForAdmission',
] as const
export type LocalExecutionSequenceStateName = (typeof LOCAL_EXECUTION_SEQUENCE_STATES)[number]

const LIVE_STATES: readonly LocalExecutionSequenceStateName[] = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'WaitingForAdmission']

/** Whether a (recorded or effective) state still holds automatic authority. */
export const isLocalExecutionSequenceLive = (state: LocalExecutionSequenceStateName): boolean => LIVE_STATES.includes(state)

export const LOCAL_EXECUTION_STEP_KINDS = [
  'Implementation',
  'Verification',
  'CodeReview',
  'VerificationDiagnosis',
  'ReviewCorrection',
  'DiagnosisCorrection',
] as const
export type LocalExecutionStepKind = (typeof LOCAL_EXECUTION_STEP_KINDS)[number]

export const LOCAL_EXECUTION_STEP_STATES = ['Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved'] as const
export type LocalExecutionStepState = (typeof LOCAL_EXECUTION_STEP_STATES)[number]

export const LOCAL_EXECUTION_PLAN_AUTHORITIES = ['AcceptedRoot', 'AcceptedRevision', 'HumanAuthorizedRevision'] as const
export type LocalExecutionPlanAuthority = (typeof LOCAL_EXECUTION_PLAN_AUTHORITIES)[number]

export type LocalExecutionManualFence = 'Blocked' | 'Released' | 'Unavailable'

/** ADR-0062: how a sequence reaches its operations. A sequence recorded before the queue existed is Immediate and is never adopted. */
export const LOCAL_EXECUTION_ADMISSION_MODES = ['Immediate', 'WaitForAdmission'] as const
export type LocalExecutionAdmissionMode = (typeof LOCAL_EXECUTION_ADMISSION_MODES)[number]

export const LOCAL_EXECUTION_ADMISSION_STATES = ['Waiting', 'Reserved', 'Unresolved'] as const
export type LocalExecutionAdmissionState = (typeof LOCAL_EXECUTION_ADMISSION_STATES)[number]

/** The closed waiting reasons: the last durably recorded admission observation, never present worker, process or slot availability. */
export const LOCAL_EXECUTION_WAITING_REASONS = [
  'AwaitingAdmissionCheck',
  'WaitingForWorkspace',
  'WaitingForCapacity',
  'WaitingForEarlierEligibleWork',
  'WaitingForPreparationCapacity',
  'Unavailable',
] as const
export type LocalExecutionWaitingReason = (typeof LOCAL_EXECUTION_WAITING_REASONS)[number]

export const LOCAL_EXECUTION_REASON_CODES = [
  'review_approved',
  'allowance_reached',
  'diagnosis_escalated',
  'stopped_before_claim',
  'stopped_after_operation',
  'preparation_refused',
  'preparation_expired',
  'preparation_failed',
  'preparation_unresolved',
  'result_deadline_elapsed',
  'advancement_deadline_elapsed',
  'operation_failed',
  'operation_outcome_invalid',
  'exit_evidence_missing',
  'verification_not_diagnosable',
  'authority_changed',
  'capacity_refused',
  'host_restarted',
] as const
export type LocalExecutionReasonCode = (typeof LOCAL_EXECUTION_REASON_CODES)[number]

export const LOCAL_EXECUTION_NEXT_ACTION_CODES = [
  'wait_or_stop_after_current_operation',
  'wait_for_current_operation_to_finish',
  'review_and_decide_on_human_approval_separately',
  'decide_on_the_recorded_escalation',
  'continue_with_explicit_manual_work',
  'inspect_and_reconcile',
  'no_supported_recovery_action_in_this_version',
] as const
export type LocalExecutionNextActionCode = (typeof LOCAL_EXECUTION_NEXT_ACTION_CODES)[number]

export const LOCAL_EXECUTION_ARMING_REFUSAL_CODES = [
  'local_execution_sequence.already_armed',
  'local_execution_sequence.run_not_manual',
  'local_execution_sequence.run_not_running',
  'local_execution_sequence.prior_mutation',
  'local_execution_sequence.plan_not_found',
  'local_execution_sequence.plan_not_eligible',
  'local_execution_sequence.planning_active',
  'local_execution_sequence.project_busy',
  'local_execution_sequence.workspace_not_ready',
  'local_execution_sequence.lease_not_active',
  'local_execution_sequence.checkpoint_missing',
  'local_execution_sequence.workspace_owned',
  'local_execution_sequence.capacity_unavailable',
  'local_execution_sequence.recipes_missing',
  'local_execution_sequence.recipes_too_many',
  'local_execution_sequence.recipes_incoherent',
  'local_execution_sequence.settings_unavailable',
  'local_execution_sequence.queue_full',
] as const
export type LocalExecutionArmingRefusalCode = (typeof LOCAL_EXECUTION_ARMING_REFUSAL_CODES)[number]

export interface LocalExecutionStep {
  stepId: string
  ordinal: number
  kind: LocalExecutionStepKind
  passNumber: number
  recipeOrdinal: number | null
  /** Untrusted text: rendered inert, never interpreted. */
  recipeName: string | null
  state: LocalExecutionStepState
  reasonCode: LocalExecutionReasonCode | null
  operationId: string | null
  operationStatus: string | null
  operationOutcome: string | null
  /** Whether a dispatch marker exists: bookkeeping, never proof that a process is running. */
  dispatched: boolean | null
  /** Whether coherent host Exited evidence exists: never proof about descendants. */
  exitRecorded: boolean | null
  /** A terminal dispatched operation without exit evidence: it stays owned and has no supported recovery action. */
  quarantined: boolean
  preparationReservedAtUtc: string
  preparationDeadlineUtc: string
  claimedAtUtc: string | null
  resultDeadlineUtc: string | null
}

export interface LocalExecutionFinal {
  reportMessageId: string
  reviewAttemptId: string | null
  escalationMessageId: string | null
}

export interface LocalExecutionSequenceDetails {
  sequenceId: string
  planProposalMessageId: string
  planAuthority: LocalExecutionPlanAuthority
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  currentCheckpointId: string
  currentFingerprintSha256: string
  /** The effective state: an elapsed deadline reads as NeedsAttention before the host persisted it. */
  state: LocalExecutionSequenceStateName
  recordedState: LocalExecutionSequenceStateName
  observedBeforePersisted: boolean
  reasonCode: LocalExecutionReasonCode | null
  reasonText: string | null
  detailCode: string | null
  nextActionCode: LocalExecutionNextActionCode
  nextActionText: string
  /** Canonical Int64 text, never converted to a number. */
  revision: string
  recipeCount: number
  maximumSteps: number
  correctionAllowance: number
  correctionsClaimed: number
  passesStarted: number
  agentClaims: number
  armedAtUtc: string
  advancementDeadlineUtc: string
  updatedAtUtc: string
  stopRequestedAtUtc: string | null
  endedAtUtc: string | null
  awaitingReconciliation: boolean
  outstandingOperationId: string | null
  steps: readonly LocalExecutionStep[]
  final: LocalExecutionFinal | null
  admissionMode: LocalExecutionAdmissionMode
  /** The pending or reserved admission of a queued sequence; null for an Immediate one or when no admission is outstanding. */
  queue: LocalExecutionQueueDisclosure | null
}

/** ADR-0062: the closed disclosure of one queued cycle's pending admission. It is never an operation step and promises no start time. */
export interface LocalExecutionQueueDisclosure {
  admissionId: string
  /** Canonical Int64 text, never converted to a number. */
  admissionNumber: string
  admissionState: LocalExecutionAdmissionState
  kind: LocalExecutionStepKind
  passNumber: number
  recipeOrdinal: number | null
  enqueuedAtUtc: string
  advancementDeadlineUtc: string
  waitingReason: LocalExecutionWaitingReason
  waitingReasonText: string
  waitingReasonRecordedAtUtc: string
}

export interface LocalExecutionSequenceStatus {
  runId: string
  /** `None`: no sequence is recorded. Otherwise exactly the effective state of `sequence`. */
  status: 'None' | LocalExecutionSequenceStateName
  sequence: LocalExecutionSequenceDetails | null
  manualFence: LocalExecutionManualFence
  latestEventSequence: string
}

export interface LocalExecutionRecipeDisclosure {
  ordinal: number
  commandId: string
  commandNumber: number
  /** Untrusted text: rendered inert. */
  name: string
  timeoutSeconds: number
}

export interface LocalExecutionAssignmentDisclosure {
  codexModel: string | null
  codexEffort: string | null
  claudeModel: string | null
  claudeEffort: string | null
  claudeMaxTurns: number | null
  codexAccountUsageStopPercent: number | null
  codexTokenStopThreshold: string | null
  claudeTokenStopThreshold: string | null
}

export interface LocalExecutionAvailabilityCandidate {
  planAuthority: LocalExecutionPlanAuthority
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  confirmationSha256: string
  correctionAllowance: number
  maximumSteps: number
  recipes: readonly LocalExecutionRecipeDisclosure[]
  disclosure: LocalExecutionAssignmentDisclosure
}

export interface LocalExecutionSequenceAvailability {
  runId: string
  planProposalMessageId: string
  admissionMode: LocalExecutionAdmissionMode
  available: boolean
  refusalCode: LocalExecutionArmingRefusalCode | null
  refusalText: string | null
  candidate: LocalExecutionAvailabilityCandidate | null
}

/** The exact tuple the Human saw and arms: the only caller-supplied facts of arming. */
export interface LocalExecutionArmTuple {
  planProposalMessageId: string
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  confirmationSha256: string
}

export interface LocalExecutionArmReceipt {
  sequenceId: string
  runId: string
  admissionMode: LocalExecutionAdmissionMode
  queue: LocalExecutionQueueDisclosure | null
  state: LocalExecutionSequenceStateName
  recipeCount: number
  correctionAllowance: number
  armedAtUtc: string
  advancementDeadlineUtc: string
  revision: string
  replayed: boolean
}

export interface LocalExecutionStopAnswer {
  sequenceId: string
  runId: string
  state: LocalExecutionSequenceStateName
  reasonCode: LocalExecutionReasonCode | null
  revision: string
  changed: boolean
}

/**
 * Who holds automatic authority over a run, derived from one validated status only: `unknown` (nothing validated, or the host could not
 * establish the fence), `blocked`, `released` (a sequence exists and its fence is lifted) and `none`. `released` and `none` lift only
 * this fence: they never assert that an ordinary manual operation is eligible.
 */
export type LocalExecutionSequenceControl = 'unknown' | 'blocked' | 'released' | 'none'

export function deriveLocalExecutionSequenceControl(status: LocalExecutionSequenceStatus | null): LocalExecutionSequenceControl {
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
const REFUSAL_CODE = /^local_execution_sequence\.[a-z_]{1,64}$/
const DETAIL_CODE = /^[a-z0-9._]{1,96}$/
const NAME_TOKEN = /^[A-Za-z][A-Za-z0-9]{0,63}$/
const UNSAFE_TEXT = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u
const MAX_INT64 = (1n << 63n) - 1n
const MAX_HOST_TEXT = 400

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T => typeof value === 'string' && (values as readonly string[]).includes(value)
const isHostText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_HOST_TEXT && !UNSAFE_TEXT.test(value)
const isUntrustedName = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 1024
const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'
const isIntegerIn = (value: unknown, min: number, max: number): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max
const isAbsentOr = <T>(value: unknown, accepts: (candidate: unknown) => candidate is T): value is T | null => value === null || accepts(value)
const isNameToken = (value: unknown): value is string => typeof value === 'string' && NAME_TOKEN.test(value)

const isInt64Text = (value: unknown, pattern: RegExp): value is string => typeof value === 'string' && pattern.test(value) && BigInt(value) <= MAX_INT64

/** A canonical positive Int64 decimal text (a revision). */
export const isLocalRevisionText = (value: unknown): value is string => isInt64Text(value, CANONICAL_POSITIVE)

/** A canonical non-negative Int64 decimal text (an event sequence or a stop threshold). */
export const isLocalNonNegativeInt64Text = (value: unknown): value is string => isInt64Text(value, CANONICAL_NON_NEGATIVE)

const instantOf = (value: string): number | null => toWireInstant(value)?.getTime() ?? null

const REASONS_BY_STATE: Readonly<Record<LocalExecutionSequenceStateName, readonly LocalExecutionReasonCode[]>> = {
  Armed: [],
  PreparingStep: [],
  AwaitingStepResult: [],
  Stopping: [],
  ReadyForHumanApproval: ['review_approved'],
  HumanEscalation: ['allowance_reached', 'diagnosis_escalated'],
  Stopped: ['stopped_before_claim', 'stopped_after_operation'],
  NeedsAttention: [
    'preparation_refused',
    'preparation_expired',
    'preparation_failed',
    'preparation_unresolved',
    'result_deadline_elapsed',
    'advancement_deadline_elapsed',
    'operation_failed',
    'operation_outcome_invalid',
    'exit_evidence_missing',
    'verification_not_diagnosable',
    'authority_changed',
    'capacity_refused',
  ],
  Interrupted: ['host_restarted'],
  WaitingForAdmission: [],
}

function expectedNextAction(state: LocalExecutionSequenceStateName, awaitingReconciliation: boolean): LocalExecutionNextActionCode {
  switch (state) {
    case 'Armed':
    case 'PreparingStep':
    case 'AwaitingStepResult':
    case 'WaitingForAdmission':
      return 'wait_or_stop_after_current_operation'
    case 'Stopping':
      return 'wait_for_current_operation_to_finish'
    default:
      break
  }

  if (awaitingReconciliation) {
    return 'no_supported_recovery_action_in_this_version'
  }

  switch (state) {
    case 'ReadyForHumanApproval':
      return 'review_and_decide_on_human_approval_separately'
    case 'HumanEscalation':
      return 'decide_on_the_recorded_escalation'
    case 'Stopped':
      return 'continue_with_explicit_manual_work'
    default:
      return 'inspect_and_reconcile'
  }
}

const STEP_KEYS = [
  'claimedAtUtc',
  'dispatched',
  'exitRecorded',
  'kind',
  'operationId',
  'operationOutcome',
  'operationStatus',
  'ordinal',
  'passNumber',
  'preparationDeadlineUtc',
  'preparationReservedAtUtc',
  'quarantined',
  'reasonCode',
  'recipeName',
  'recipeOrdinal',
  'resultDeadlineUtc',
  'state',
  'stepId',
]
const FINAL_KEYS = ['escalationMessageId', 'reportMessageId', 'reviewAttemptId']
const SEQUENCE_KEYS = [
  'admissionMode',
  'advancementDeadlineUtc',
  'agentClaims',
  'armedAtUtc',
  'awaitingReconciliation',
  'checkpointId',
  'correctionAllowance',
  'correctionsClaimed',
  'currentCheckpointId',
  'currentFingerprintSha256',
  'detailCode',
  'endedAtUtc',
  'final',
  'fingerprintSha256',
  'maximumSteps',
  'nextActionCode',
  'nextActionText',
  'observedBeforePersisted',
  'outstandingOperationId',
  'passesStarted',
  'planAuthority',
  'planProposalMessageId',
  'queue',
  'reasonCode',
  'reasonText',
  'recipeCount',
  'recordedState',
  'revision',
  'sequenceId',
  'state',
  'steps',
  'stopRequestedAtUtc',
  'updatedAtUtc',
  'workspaceId',
]
const STATUS_KEYS = ['latestEventSequence', 'manualFence', 'runId', 'sequence', 'status']
const AVAILABILITY_KEYS = [
  'admissionMode',
  'available',
  'checkpointId',
  'confirmationSha256',
  'correctionAllowance',
  'disclosure',
  'fingerprintSha256',
  'maximumSteps',
  'planAuthority',
  'planProposalMessageId',
  'recipes',
  'refusalCode',
  'refusalText',
  'runId',
  'workspaceId',
]
const RECIPE_KEYS = ['commandId', 'commandNumber', 'name', 'ordinal', 'timeoutSeconds']
const DISCLOSURE_KEYS = [
  'claudeEffort',
  'claudeMaxTurns',
  'claudeModel',
  'claudeTokenStopThreshold',
  'codexAccountUsageStopPercent',
  'codexEffort',
  'codexModel',
  'codexTokenStopThreshold',
]
const RECEIPT_KEYS = [
  'admissionMode',
  'advancementDeadlineUtc',
  'armedAtUtc',
  'checkpointId',
  'confirmationSha256',
  'correctionAllowance',
  'fingerprintSha256',
  'planProposalMessageId',
  'queue',
  'recipeCount',
  'replayed',
  'revision',
  'runId',
  'sequenceId',
  'state',
  'workspaceId',
]
const QUEUE_KEYS = [
  'admissionId',
  'admissionNumber',
  'admissionState',
  'advancementDeadlineUtc',
  'enqueuedAtUtc',
  'kind',
  'passNumber',
  'recipeOrdinal',
  'waitingReason',
  'waitingReasonRecordedAtUtc',
  'waitingReasonText',
]
const STOP_KEYS = ['changed', 'reasonCode', 'revision', 'runId', 'sequenceId', 'state']

function toStep(raw: unknown, ordinal: number, recipeCount: number): LocalExecutionStep | null {
  if (!hasExactKeys(raw, STEP_KEYS) || raw.ordinal !== ordinal) {
    return null
  }

  const { kind, state, recipeOrdinal, recipeName, reasonCode } = raw
  if (
    !isGuid(raw.stepId)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, kind)
    || !isOneOf(LOCAL_EXECUTION_STEP_STATES, state)
    || !isIntegerIn(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || (kind === 'Implementation') !== (raw.passNumber === 0)
    || !isUtcRoundTrip(raw.preparationReservedAtUtc)
    || !isUtcRoundTrip(raw.preparationDeadlineUtc)
    || !isBoolean(raw.quarantined)
  ) {
    return null
  }

  const reservedAt = instantOf(raw.preparationReservedAtUtc)
  const deadlineAt = instantOf(raw.preparationDeadlineUtc)
  if (reservedAt === null || deadlineAt === null || deadlineAt - reservedAt !== PREPARATION_WINDOW_MILLISECONDS) {
    return null
  }

  // A recipe belongs exactly to a verification step and names a position of the pinned set.
  if (kind === 'Verification') {
    if (!isIntegerIn(recipeOrdinal, 0, recipeCount - 1) || !isAbsentOr(recipeName, isUntrustedName)) {
      return null
    }
  } else if (recipeOrdinal !== null || recipeName !== null) {
    return null
  }

  const endedWithReason = state === 'Revoked' || state === 'Unresolved'
  if (endedWithReason ? !isOneOf(LOCAL_EXECUTION_REASON_CODES, reasonCode) : reasonCode !== null) {
    return null
  }

  const linked = state === 'Claimed' || state === 'Concluded'
  if (!linked) {
    const noLink = [
      raw.operationId,
      raw.operationStatus,
      raw.operationOutcome,
      raw.dispatched,
      raw.exitRecorded,
      raw.claimedAtUtc,
      raw.resultDeadlineUtc,
    ]
    if (!noLink.every((value) => value === null) || raw.quarantined) {
      return null
    }
  } else if (
    !isGuid(raw.operationId)
    || !isUtcRoundTrip(raw.claimedAtUtc)
    || !isUtcRoundTrip(raw.resultDeadlineUtc)
    || !isAbsentOr(raw.operationStatus, isNameToken)
    || !isAbsentOr(raw.operationOutcome, isNameToken)
    || !isAbsentOr(raw.dispatched, isBoolean)
    || !isAbsentOr(raw.exitRecorded, isBoolean)
  ) {
    return null
  } else {
    // The linked row's facts exist together or not at all (a missing row), and a running operation has no outcome yet.
    const facts = [raw.operationStatus, raw.dispatched, raw.exitRecorded]
    if (!(facts.every((value) => value === null) || facts.every((value) => value !== null))) {
      return null
    }

    if (raw.operationStatus === null && (raw.operationOutcome !== null || raw.quarantined)) {
      return null
    }

    if (raw.operationStatus === 'Running' && (raw.operationOutcome !== null || raw.quarantined)) {
      return null
    }

    // Quarantined is exactly: dispatched, no longer running, without exit evidence.
    const expectedQuarantine = raw.dispatched === true && raw.operationStatus !== 'Running' && raw.exitRecorded === false
    if (raw.quarantined !== expectedQuarantine) {
      return null
    }

    if (state === 'Concluded' && raw.operationStatus === 'Running') {
      return null
    }
  }

  return {
    stepId: raw.stepId,
    ordinal,
    kind,
    passNumber: raw.passNumber,
    recipeOrdinal: recipeOrdinal as number | null,
    recipeName: recipeName as string | null,
    state,
    reasonCode: reasonCode as LocalExecutionReasonCode | null,
    operationId: raw.operationId as string | null,
    operationStatus: raw.operationStatus as string | null,
    operationOutcome: raw.operationOutcome as string | null,
    dispatched: raw.dispatched as boolean | null,
    exitRecorded: raw.exitRecorded as boolean | null,
    quarantined: raw.quarantined,
    preparationReservedAtUtc: raw.preparationReservedAtUtc,
    preparationDeadlineUtc: raw.preparationDeadlineUtc,
    claimedAtUtc: raw.claimedAtUtc as string | null,
    resultDeadlineUtc: raw.resultDeadlineUtc as string | null,
  }
}

function toSteps(raw: unknown, recipeCount: number): LocalExecutionStep[] | null {
  if (!Array.isArray(raw) || raw.length > maximumLocalExecutionSteps(recipeCount)) {
    return null
  }

  const steps: LocalExecutionStep[] = []
  const identities = new Set<string>()
  for (const [ordinal, item] of raw.entries()) {
    const step = toStep(item, ordinal, recipeCount)
    if (step === null || identities.has(step.stepId)) {
      return null
    }

    identities.add(step.stepId)
    steps.push(step)
  }

  // Strictly sequential: every step before the last concluded, and only the last may be outstanding.
  const lastIndex = steps.length - 1
  return steps.every((step, index) => index === lastIndex || step.state === 'Concluded') ? steps : null
}

function toFinal(raw: unknown, recorded: LocalExecutionSequenceStateName, steps: readonly LocalExecutionStep[]): LocalExecutionFinal | null {
  if (
    !hasExactKeys(raw, FINAL_KEYS)
    || !isGuid(raw.reportMessageId)
    || !isAbsentOr(raw.reviewAttemptId, isGuid)
    || !isAbsentOr(raw.escalationMessageId, isGuid)
  ) {
    return null
  }

  const approved = recorded === 'ReadyForHumanApproval' && raw.reviewAttemptId !== null && raw.escalationMessageId === null
  const escalated = recorded === 'HumanEscalation' && raw.escalationMessageId !== null && raw.reviewAttemptId === null
  if (!(approved || escalated)) {
    return null
  }

  if (approved && !steps.some((step) => step.kind === 'CodeReview' && step.operationId === raw.reviewAttemptId)) {
    return null
  }

  return { reportMessageId: raw.reportMessageId, reviewAttemptId: raw.reviewAttemptId, escalationMessageId: raw.escalationMessageId }
}

function stepsAgreeWithState(recorded: LocalExecutionSequenceStateName, steps: readonly LocalExecutionStep[]): boolean {
  const last = steps[steps.length - 1]
  switch (recorded) {
    case 'Armed':
      return steps.length === 0
    case 'WaitingForAdmission':
      return steps.length === 0 || last?.state === 'Concluded'
    case 'PreparingStep':
      return last?.state === 'Reserved'
    case 'AwaitingStepResult':
    case 'Stopping':
      return last?.state === 'Claimed' || last?.state === 'Concluded'
    case 'ReadyForHumanApproval':
      return last?.state === 'Concluded' && last.kind === 'CodeReview'
    case 'HumanEscalation':
      return last?.state === 'Concluded'
    default:
      return true
  }
}

/**
 * The closed queue disclosure of one pending or reserved admission (ADR-0062), judged against the sequence it belongs to. It is never an
 * operation step: it carries no operation, no process fact and no start time, only the recorded admission observation.
 */
function toQueue(raw: unknown, armedAtMilliseconds: number, advancementDeadlineUtc: string): LocalExecutionQueueDisclosure | null {
  if (!hasExactKeys(raw, QUEUE_KEYS)) {
    return null
  }

  const { kind, recipeOrdinal } = raw
  if (
    !isGuid(raw.admissionId)
    || !isInt64Text(raw.admissionNumber, CANONICAL_POSITIVE)
    || !isOneOf(LOCAL_EXECUTION_ADMISSION_STATES, raw.admissionState)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, kind)
    || !isIntegerIn(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || (kind === 'Implementation') !== (raw.passNumber === 0)
    || (kind === 'Verification') !== (recipeOrdinal !== null)
    || !(recipeOrdinal === null || isIntegerIn(recipeOrdinal, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES - 1))
    || !isUtcRoundTrip(raw.enqueuedAtUtc)
    || raw.advancementDeadlineUtc !== advancementDeadlineUtc
    || !isOneOf(LOCAL_EXECUTION_WAITING_REASONS, raw.waitingReason)
    || !isHostText(raw.waitingReasonText)
    || !isUtcRoundTrip(raw.waitingReasonRecordedAtUtc)
  ) {
    return null
  }

  const enqueuedAt = instantOf(raw.enqueuedAtUtc)
  const recordedAt = instantOf(raw.waitingReasonRecordedAtUtc)
  if (enqueuedAt === null || recordedAt === null || enqueuedAt < armedAtMilliseconds || recordedAt < enqueuedAt) {
    return null
  }

  return {
    admissionId: raw.admissionId,
    admissionNumber: raw.admissionNumber,
    admissionState: raw.admissionState,
    kind,
    passNumber: raw.passNumber,
    recipeOrdinal: recipeOrdinal as number | null,
    enqueuedAtUtc: raw.enqueuedAtUtc,
    advancementDeadlineUtc,
    waitingReason: raw.waitingReason,
    waitingReasonText: raw.waitingReasonText,
    waitingReasonRecordedAtUtc: raw.waitingReasonRecordedAtUtc,
  }
}

const isOutstanding = (step: LocalExecutionStep): boolean => step.operationId !== null && (step.operationStatus === 'Running' || step.quarantined)

function toDetails(raw: unknown): LocalExecutionSequenceDetails | null {
  if (!hasExactKeys(raw, SEQUENCE_KEYS)) {
    return null
  }

  const { state, recordedState, observedBeforePersisted, reasonCode, reasonText, detailCode, recipeCount } = raw
  if (
    !isGuid(raw.sequenceId)
    || !isGuid(raw.planProposalMessageId)
    || !isOneOf(LOCAL_EXECUTION_PLAN_AUTHORITIES, raw.planAuthority)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isSha256(raw.fingerprintSha256)
    || !isGuid(raw.currentCheckpointId)
    || !isSha256(raw.currentFingerprintSha256)
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, state)
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, recordedState)
    || !isBoolean(observedBeforePersisted)
    || !isLocalRevisionText(raw.revision)
    || !isIntegerIn(recipeCount, 1, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES)
    || raw.maximumSteps !== maximumLocalExecutionSteps(recipeCount)
    || !isIntegerIn(raw.correctionAllowance, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS)
    || !isIntegerIn(raw.correctionsClaimed, 0, raw.correctionAllowance)
    || !isIntegerIn(raw.passesStarted, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || !isIntegerIn(raw.agentClaims, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_AGENT_CLAIMS)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isUtcRoundTrip(raw.advancementDeadlineUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || !isAbsentOr(raw.stopRequestedAtUtc, isUtcRoundTrip)
    || !isAbsentOr(raw.endedAtUtc, isUtcRoundTrip)
    || !isBoolean(raw.awaitingReconciliation)
    || !isAbsentOr(raw.outstandingOperationId, isGuid)
    || !isOneOf(LOCAL_EXECUTION_NEXT_ACTION_CODES, raw.nextActionCode)
    || !isHostText(raw.nextActionText)
  ) {
    return null
  }

  const armedAt = instantOf(raw.armedAtUtc)
  const deadlineAt = instantOf(raw.advancementDeadlineUtc)
  if (armedAt === null || deadlineAt === null || deadlineAt - armedAt !== ADVANCEMENT_WINDOW_MILLISECONDS) {
    return null
  }

  // The effective state is the recorded one, except that an elapsed deadline reads as NeedsAttention before the host persisted it.
  if (observedBeforePersisted) {
    const allowed: readonly LocalExecutionReasonCode[] =
      recordedState === 'Armed' || recordedState === 'WaitingForAdmission'
        ? ['advancement_deadline_elapsed']
        : recordedState === 'PreparingStep'
          ? ['advancement_deadline_elapsed', 'preparation_expired']
          : recordedState === 'AwaitingStepResult' || recordedState === 'Stopping'
            ? ['result_deadline_elapsed']
            : []
    if (state !== 'NeedsAttention' || !isOneOf(allowed, reasonCode) || detailCode !== null) {
      return null
    }
  } else if (state !== recordedState || !(reasonCode === null ? REASONS_BY_STATE[state].length === 0 : isOneOf(REASONS_BY_STATE[state], reasonCode))) {
    return null
  }

  if (reasonCode === null ? reasonText !== null : !isHostText(reasonText)) {
    return null
  }

  if (!(detailCode === null || (typeof detailCode === 'string' && DETAIL_CODE.test(detailCode) && state === 'NeedsAttention'))) {
    return null
  }

  const recordedLive = isLocalExecutionSequenceLive(recordedState)
  if ((raw.endedAtUtc === null) !== recordedLive) {
    return null
  }

  if ((recordedState === 'Stopping' || recordedState === 'Stopped') && raw.stopRequestedAtUtc === null) {
    return null
  }

  if (
    (recordedState === 'Armed' || recordedState === 'PreparingStep' || recordedState === 'AwaitingStepResult' || recordedState === 'WaitingForAdmission')
    && raw.stopRequestedAtUtc !== null
  ) {
    return null
  }

  const steps = toSteps(raw.steps, recipeCount)
  if (steps === null || !stepsAgreeWithState(recordedState, steps)) {
    return null
  }

  // ADR-0062: the mode is immutable, a queued sequence is never Armed, and its disclosure is exactly the one pending or reserved admission
  // its recorded state implies. An elapsed waiting admission read before persistence discloses none; a reserved or unresolved one stays.
  const admissionMode = raw.admissionMode
  if (!isOneOf(LOCAL_EXECUTION_ADMISSION_MODES, admissionMode) || (admissionMode === 'WaitForAdmission' && recordedState === 'Armed')) {
    return null
  }

  const queue = raw.queue === null ? null : toQueue(raw.queue, armedAt, raw.advancementDeadlineUtc)
  if (raw.queue !== null && queue === null) {
    return null
  }

  if (admissionMode === 'Immediate' ? recordedState === 'WaitingForAdmission' || queue !== null : false) {
    return null
  }

  if (admissionMode === 'WaitForAdmission') {
    const expected =
      recordedState === 'WaitingForAdmission'
        ? observedBeforePersisted
          ? null
          : 'Waiting'
        : recordedState === 'PreparingStep'
          ? 'Reserved'
          : undefined
    if (expected !== undefined ? queue?.admissionState !== (expected ?? undefined) : queue !== null && queue.admissionState !== 'Unresolved') {
      return null
    }

    if (queue?.admissionState === 'Unresolved' && isLocalExecutionSequenceLive(recordedState)) {
      return null
    }
  }

  // The counters are exactly what the recorded steps imply.
  const counted = steps.filter((step) => step.state !== 'Revoked')
  const corrections = counted.filter((step) => step.kind === 'ReviewCorrection' || step.kind === 'DiagnosisCorrection').length
  const passes = new Set(counted.filter((step) => step.kind === 'Verification').map((step) => step.passNumber)).size
  const agentClaims = counted.filter((step) => step.kind !== 'Verification').length
  if (raw.correctionsClaimed !== corrections || raw.passesStarted !== passes || raw.agentClaims !== agentClaims) {
    return null
  }

  const outstanding = steps.find(isOutstanding)?.operationId ?? null
  if (raw.outstandingOperationId !== outstanding || raw.awaitingReconciliation !== (outstanding !== null && !isLocalExecutionSequenceLive(state))) {
    return null
  }

  if (raw.nextActionCode !== expectedNextAction(state, raw.awaitingReconciliation)) {
    return null
  }

  let final: LocalExecutionFinal | null = null
  if (raw.final !== null) {
    final = toFinal(raw.final, recordedState, steps)
    if (final === null) {
      return null
    }
  }

  return {
    sequenceId: raw.sequenceId,
    planProposalMessageId: raw.planProposalMessageId,
    planAuthority: raw.planAuthority,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    fingerprintSha256: raw.fingerprintSha256,
    currentCheckpointId: raw.currentCheckpointId,
    currentFingerprintSha256: raw.currentFingerprintSha256,
    state,
    recordedState,
    observedBeforePersisted,
    reasonCode: reasonCode as LocalExecutionReasonCode | null,
    reasonText: reasonText as string | null,
    detailCode: detailCode as string | null,
    nextActionCode: raw.nextActionCode,
    nextActionText: raw.nextActionText,
    revision: raw.revision,
    recipeCount,
    maximumSteps: raw.maximumSteps,
    correctionAllowance: raw.correctionAllowance,
    correctionsClaimed: raw.correctionsClaimed,
    passesStarted: raw.passesStarted,
    agentClaims: raw.agentClaims,
    armedAtUtc: raw.armedAtUtc,
    advancementDeadlineUtc: raw.advancementDeadlineUtc,
    updatedAtUtc: raw.updatedAtUtc,
    stopRequestedAtUtc: raw.stopRequestedAtUtc,
    endedAtUtc: raw.endedAtUtc,
    awaitingReconciliation: raw.awaitingReconciliation,
    outstandingOperationId: raw.outstandingOperationId,
    steps,
    final,
    admissionMode,
    queue,
  }
}

/**
 * Accepts a parsed recorded status only when the whole of it is coherent for exactly `expectedRunId`. `None` carries no sequence and a
 * Released or Unavailable fence; any other status carries exactly the sequence of that effective state. A Blocked fence is required
 * whenever the recorded state holds authority, a step preparation is reserved or unresolved, or a linked operation is running or
 * quarantined; the fence is never accepted as lifted while such a fact is recorded.
 */
export function toLocalExecutionSequenceStatus(raw: unknown, expectedRunId: string): LocalExecutionSequenceStatus | null {
  if (
    !hasExactKeys(raw, STATUS_KEYS)
    || raw.runId !== expectedRunId
    || !isLocalNonNegativeInt64Text(raw.latestEventSequence)
    || !isOneOf(['Blocked', 'Released', 'Unavailable'] as const, raw.manualFence)
  ) {
    return null
  }

  const manualFence = raw.manualFence
  const latestEventSequence = raw.latestEventSequence
  if (raw.status === 'None') {
    return raw.sequence === null && manualFence !== 'Blocked'
      ? { runId: expectedRunId, status: 'None', sequence: null, manualFence, latestEventSequence }
      : null
  }

  if (!isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, raw.status)) {
    return null
  }

  const sequence = toDetails(raw.sequence)
  if (sequence === null || sequence.state !== raw.status) {
    return null
  }

  const mustBlock =
    isLocalExecutionSequenceLive(sequence.recordedState)
    || sequence.steps.some((step) => step.state === 'Reserved' || step.state === 'Unresolved' || isOutstanding(step))
  if (manualFence === 'Released' && mustBlock) {
    return null
  }

  return { runId: expectedRunId, status: raw.status, sequence, manualFence, latestEventSequence }
}

function toDisclosure(raw: unknown): LocalExecutionAssignmentDisclosure | null {
  if (!hasExactKeys(raw, DISCLOSURE_KEYS)) {
    return null
  }

  const text = (value: unknown): value is string => typeof value === 'string' && value.length <= 200 && !UNSAFE_TEXT.test(value)
  if (
    !isAbsentOr(raw.codexModel, text)
    || !isAbsentOr(raw.codexEffort, text)
    || !isAbsentOr(raw.claudeModel, text)
    || !isAbsentOr(raw.claudeEffort, text)
    || !isAbsentOr(raw.claudeMaxTurns, (value): value is number => isIntegerIn(value, 0, 1_000_000))
    || !isAbsentOr(raw.codexAccountUsageStopPercent, (value): value is number => isIntegerIn(value, 0, 100))
    || !isAbsentOr(raw.codexTokenStopThreshold, isLocalNonNegativeInt64Text)
    || !isAbsentOr(raw.claudeTokenStopThreshold, isLocalNonNegativeInt64Text)
  ) {
    return null
  }

  return {
    codexModel: raw.codexModel,
    codexEffort: raw.codexEffort,
    claudeModel: raw.claudeModel,
    claudeEffort: raw.claudeEffort,
    claudeMaxTurns: raw.claudeMaxTurns,
    codexAccountUsageStopPercent: raw.codexAccountUsageStopPercent,
    codexTokenStopThreshold: raw.codexTokenStopThreshold,
    claudeTokenStopThreshold: raw.claudeTokenStopThreshold,
  } as LocalExecutionAssignmentDisclosure
}

function toRecipes(raw: unknown): LocalExecutionRecipeDisclosure[] | null {
  if (!Array.isArray(raw) || raw.length < 1 || raw.length > LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES) {
    return null
  }

  const recipes: LocalExecutionRecipeDisclosure[] = []
  const identities = new Set<string>()
  const numbers = new Set<number>()
  for (const [ordinal, item] of raw.entries()) {
    if (
      !hasExactKeys(item, RECIPE_KEYS)
      || item.ordinal !== ordinal
      || !isGuid(item.commandId)
      || !isIntegerIn(item.commandNumber, 1, 2_147_483_647)
      || !isUntrustedName(item.name)
      || !isIntegerIn(item.timeoutSeconds, 1, 86_400)
      || identities.has(item.commandId)
      || numbers.has(item.commandNumber)
    ) {
      return null
    }

    identities.add(item.commandId)
    numbers.add(item.commandNumber)
    recipes.push({ ordinal, commandId: item.commandId, commandNumber: item.commandNumber, name: item.name, timeoutSeconds: item.timeoutSeconds })
  }

  // The pinned set is ordered by command number.
  return recipes.every((recipe, index) => index === 0 || recipes[index - 1].commandNumber < recipe.commandNumber) ? recipes : null
}

/**
 * Accepts a parsed availability only when it is exactly the answer for `expectedRunId` and `expectedPlanId`: an available answer carries
 * the whole exact candidate (tuple, confirmation, allowance, bounded recipes and assignment disclosure) and no refusal; a refusal
 * carries one closed code with its text and no confirmation or recipes.
 */
export function toLocalExecutionSequenceAvailability(
  raw: unknown,
  expectedRunId: string,
  expectedPlanId: string,
  expectedMode: LocalExecutionAdmissionMode = 'Immediate',
): LocalExecutionSequenceAvailability | null {
  if (
    !hasExactKeys(raw, AVAILABILITY_KEYS)
    || raw.admissionMode !== expectedMode
    || raw.runId !== expectedRunId
    || raw.planProposalMessageId !== expectedPlanId
    || !isBoolean(raw.available)
    || !isAbsentOr(raw.planAuthority, (value): value is LocalExecutionPlanAuthority => isOneOf(LOCAL_EXECUTION_PLAN_AUTHORITIES, value))
  ) {
    return null
  }

  if (raw.available) {
    const recipes = toRecipes(raw.recipes)
    const disclosure = toDisclosure(raw.disclosure)
    if (
      raw.refusalCode !== null
      || raw.refusalText !== null
      || raw.planAuthority === null
      || !isGuid(raw.workspaceId)
      || !isGuid(raw.checkpointId)
      || !isSha256(raw.fingerprintSha256)
      || !isSha256(raw.confirmationSha256)
      || !isIntegerIn(raw.correctionAllowance, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS)
      || recipes === null
      || disclosure === null
      || raw.maximumSteps !== maximumLocalExecutionSteps(recipes.length)
    ) {
      return null
    }

    return {
      runId: expectedRunId,
      planProposalMessageId: expectedPlanId,
      admissionMode: expectedMode,
      available: true,
      refusalCode: null,
      refusalText: null,
      candidate: {
        planAuthority: raw.planAuthority,
        workspaceId: raw.workspaceId,
        checkpointId: raw.checkpointId,
        fingerprintSha256: raw.fingerprintSha256,
        confirmationSha256: raw.confirmationSha256,
        correctionAllowance: raw.correctionAllowance,
        maximumSteps: raw.maximumSteps,
        recipes,
        disclosure,
      },
    }
  }

  if (
    !(typeof raw.refusalCode === 'string' && REFUSAL_CODE.test(raw.refusalCode))
    || !isHostText(raw.refusalText)
    || raw.confirmationSha256 !== null
    || raw.recipes !== null
    || raw.disclosure !== null
    || raw.workspaceId !== null
    || raw.checkpointId !== null
    || raw.fingerprintSha256 !== null
    || raw.correctionAllowance !== null
    || raw.maximumSteps !== null
  ) {
    return null
  }

  return {
    runId: expectedRunId,
    planProposalMessageId: expectedPlanId,
    admissionMode: expectedMode,
    available: false,
    refusalCode: raw.refusalCode as LocalExecutionArmingRefusalCode,
    refusalText: raw.refusalText,
    candidate: null,
  }
}

/**
 * Accepts the parsed arming receipt only when it is exactly the receipt of arming `expectedRunId` with exactly `expectedTuple`. A first
 * arming is Armed at revision 1 with the two-hour window; a replay is the recorded receipt in whatever state the sequence has reached.
 */
export function toLocalExecutionArmReceipt(
  raw: unknown,
  expectedRunId: string,
  expectedTuple: LocalExecutionArmTuple,
  expectedMode: LocalExecutionAdmissionMode = 'Immediate',
): LocalExecutionArmReceipt | null {
  if (
    !hasExactKeys(raw, RECEIPT_KEYS)
    || raw.admissionMode !== expectedMode
    || raw.runId !== expectedRunId
    || raw.planProposalMessageId !== expectedTuple.planProposalMessageId
    || raw.workspaceId !== expectedTuple.workspaceId
    || raw.checkpointId !== expectedTuple.checkpointId
    || raw.fingerprintSha256 !== expectedTuple.fingerprintSha256
    || raw.confirmationSha256 !== expectedTuple.confirmationSha256
    || !isGuid(raw.sequenceId)
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, raw.state)
    || !isIntegerIn(raw.recipeCount, 1, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES)
    || !isIntegerIn(raw.correctionAllowance, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isUtcRoundTrip(raw.advancementDeadlineUtc)
    || !isLocalRevisionText(raw.revision)
    || !isBoolean(raw.replayed)
    || (!raw.replayed && (raw.state !== (expectedMode === 'Immediate' ? 'Armed' : 'WaitingForAdmission') || raw.revision !== '1'))
  ) {
    return null
  }

  const armedAt = instantOf(raw.armedAtUtc)
  const deadlineAt = instantOf(raw.advancementDeadlineUtc)
  if (armedAt === null || deadlineAt === null || deadlineAt - armedAt !== ADVANCEMENT_WINDOW_MILLISECONDS) {
    return null
  }

  // ADR-0062: only a queued receipt carries a disclosure; a first queue receipt always carries the initial Waiting admission.
  const queue = raw.queue === null ? null : toQueue(raw.queue, armedAt, raw.advancementDeadlineUtc)
  if (
    (raw.queue !== null && queue === null)
    || (expectedMode === 'Immediate' && queue !== null)
    || (expectedMode === 'WaitForAdmission' && !raw.replayed && queue?.admissionState !== 'Waiting')
    || (expectedMode === 'WaitForAdmission' && raw.state === 'Armed')
  ) {
    return null
  }

  return {
    sequenceId: raw.sequenceId,
    runId: expectedRunId,
    admissionMode: expectedMode,
    queue,
    state: raw.state,
    recipeCount: raw.recipeCount,
    correctionAllowance: raw.correctionAllowance,
    armedAtUtc: raw.armedAtUtc,
    advancementDeadlineUtc: raw.advancementDeadlineUtc,
    revision: raw.revision,
    replayed: raw.replayed,
  }
}

/**
 * Accepts the parsed Stop answer only when it is exactly the answer for `expectedRunId` and `expectedSequenceId`: a state a stop can
 * leave (Stopping or an ended state), the reason that state records, a canonical revision, and `changed` only for a transition a stop makes.
 */
export function toLocalExecutionStopAnswer(raw: unknown, expectedRunId: string, expectedSequenceId: string): LocalExecutionStopAnswer | null {
  if (
    !hasExactKeys(raw, STOP_KEYS)
    || raw.runId !== expectedRunId
    || raw.sequenceId !== expectedSequenceId
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, raw.state)
    || !isLocalRevisionText(raw.revision)
    || !isBoolean(raw.changed)
  ) {
    return null
  }

  const { state, reasonCode, changed } = raw
  if (state === 'Armed' || state === 'PreparingStep' || state === 'AwaitingStepResult' || state === 'WaitingForAdmission') {
    return null
  }

  if (reasonCode === null ? REASONS_BY_STATE[state].length > 0 : !isOneOf(REASONS_BY_STATE[state], reasonCode)) {
    return null
  }

  if (
    changed
    && !(
      state === 'Stopping'
      || (state === 'Stopped' && reasonCode === 'stopped_before_claim')
      || (state === 'NeedsAttention' && reasonCode === 'preparation_unresolved')
    )
  ) {
    return null
  }

  return {
    sequenceId: expectedSequenceId,
    runId: expectedRunId,
    state,
    reasonCode: reasonCode as LocalExecutionReasonCode | null,
    revision: raw.revision,
    changed,
  }
}
