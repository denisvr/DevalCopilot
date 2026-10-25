import { toWireInstant } from './agentAttemptEvidenceWire'
import { hasExactKeys, isInt, isUtcRoundTrip } from './agentAttemptVerification'
import {
  LOCAL_EXECUTION_ADMISSION_MODES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES,
  LOCAL_EXECUTION_STEP_KINDS,
  isLocalNonNegativeInt64Text,
  isLocalRevisionText,
} from './localExecutionSequence'
import type {
  LocalExecutionAdmissionMode,
  LocalExecutionSequenceDetails,
  LocalExecutionSequenceStateName,
  LocalExecutionStep,
  LocalExecutionStepKind,
} from './localExecutionSequence'

// The closed, whole-answer decoder of the ownership release reading and receipt (ADR-0065), shared by the GET and the POST. It accepts a
// parsed host answer only when all of it is coherent for exactly the run, step and recorded operation that was asked about: exactly the
// documented members at every level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the structural
// rules the host itself proves. Anything else is null and is never repaired, coerced or partially accepted. A physical termination fact
// is evidence about the invoked process only; it is never proof about descendants and never changes the recorded result.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/
const INT32_MIN = -(2 ** 31)
const INT32_MAX = 2 ** 31 - 1
const MAX_STEP_ORDINAL = 3 * LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES + 6 - 1

export const LOCAL_EXECUTION_RELEASE_STATUSES = ['NotEligible', 'Unavailable', 'Available', 'Released'] as const
export type LocalExecutionReleaseStatus = (typeof LOCAL_EXECUTION_RELEASE_STATUSES)[number]

export const LOCAL_EXECUTION_RELEASE_NOT_ELIGIBLE_REASONS = [
  'step_not_linked',
  'operation_not_terminal',
  'not_dispatched',
  'already_free',
  'sequence_not_ended',
] as const
export const LOCAL_EXECUTION_RELEASE_UNAVAILABLE_REASONS = ['evidence_missing', 'evidence_incoherent'] as const
export type LocalExecutionReleaseReasonCode =
  | (typeof LOCAL_EXECUTION_RELEASE_NOT_ELIGIBLE_REASONS)[number]
  | (typeof LOCAL_EXECUTION_RELEASE_UNAVAILABLE_REASONS)[number]

export const LOCAL_EXECUTION_RELEASE_OPERATION_FAMILIES = ['Agent', 'Verification'] as const
export type LocalExecutionReleaseOperationFamily = (typeof LOCAL_EXECUTION_RELEASE_OPERATION_FAMILIES)[number]

export const LOCAL_EXECUTION_RELEASE_CANDIDATE_PROCESS_OUTCOMES = ['TimedOut', 'Cancelled'] as const
export type LocalExecutionReleaseProcessOutcome = (typeof LOCAL_EXECUTION_RELEASE_CANDIDATE_PROCESS_OUTCOMES)[number]

/** The only sequence states that retain a releasable quarantine: the cycle ended without finishing normally. */
export const LOCAL_EXECUTION_RELEASE_SEQUENCE_STATES = ['Stopped', 'NeedsAttention', 'Interrupted'] as const
export type LocalExecutionReleaseSequenceState = (typeof LOCAL_EXECUTION_RELEASE_SEQUENCE_STATES)[number]

/**
 * The recorded terminal result a release candidate can carry (ADR-0065), by family and process outcome. A clean exit is exactly the proof
 * that already frees an operation, so a candidate's process outcome is a timeout or a cancellation of the owned process and its semantic exit
 * code is absent. An Agent attempt is then Failed. A verification execution is TimedOut for a timeout and Cancelled for a cancellation, or
 * SourceChanged when its completion fingerprint differs from the pinned source (the existing VerificationExecution rule). Anything else is
 * a status the host never records for such a candidate (Running, Passed, Completed, Interrupted, an unknown name) and is refused.
 */
const AGENT_CANDIDATE_STATUSES = ['Failed'] as const
const VERIFICATION_CANDIDATE_STATUSES: Readonly<Record<'TimedOut' | 'Cancelled', readonly string[]>> = {
  TimedOut: ['TimedOut', 'SourceChanged'],
  Cancelled: ['Cancelled', 'SourceChanged'],
}

/** The only confirmation version this window can word truthfully; any other version is refused, never confirmed. */
export const LOCAL_EXECUTION_RELEASE_CONFIRMATION_VERSION = '1'

export interface LocalExecutionReleaseCandidate {
  sequenceId: string
  sequenceState: LocalExecutionReleaseSequenceState
  admissionMode: LocalExecutionAdmissionMode
  sourceCheckpointId: string
  sourceFingerprintSha256: string
  stepKind: LocalExecutionStepKind
  ordinal: number
  passNumber: number
  operationFamily: LocalExecutionReleaseOperationFamily
  operationId: string
  operationStatus: string
  processOutcome: LocalExecutionReleaseProcessOutcome
  /** The semantic exit code the ordinary result recorded: always absent for a timeout or cancellation. Never the physical exit code. */
  semanticExitCode: null
  observationId: string
  /** The exit code the operating system reported when the host's owned process was observed terminated. */
  physicalExitCode: number
  observedElapsedTicks: string
  processDurationTicks: string
  dispatchedAtUtc: string
  observedAtUtc: string
  confirmationVersion: string
  confirmationSha256: string
  confirmationExpiresAtUtc: string
}

export interface LocalExecutionReleaseReceipt {
  releaseId: string
  operationFamily: LocalExecutionReleaseOperationFamily
  operationId: string
  stepKind: LocalExecutionStepKind
  releasedAtUtc: string
  /** The canonical positive Int64 sequence number of the one Human run event. */
  eventSequence: string
}

export type LocalExecutionOwnershipReleaseAnswer =
  | { status: 'NotEligible' | 'Unavailable'; runId: string; stepId: string; reasonCode: LocalExecutionReleaseReasonCode }
  | { status: 'Available'; runId: string; stepId: string; candidate: LocalExecutionReleaseCandidate }
  | { status: 'Released'; runId: string; stepId: string; release: LocalExecutionReleaseReceipt }

/** What the accepted recorded status says about the one step an answer must belong to. Frozen when the step is chosen. */
export interface LocalExecutionReleaseTarget {
  runId: string
  stepId: string
  sequenceId: string
  sequenceState: LocalExecutionSequenceStateName
  admissionMode: LocalExecutionAdmissionMode
  stepKind: LocalExecutionStepKind
  ordinal: number
  passNumber: number
  operationId: string
  /** Whether the accepted status already shows the release (rather than the retained quarantine). */
  releasedInStatus: boolean
}

/** The steps of an accepted status a Human can read or release: a retained quarantine, or one the host shows as released. */
export function releaseTargetsOf(runId: string, sequence: LocalExecutionSequenceDetails): readonly LocalExecutionReleaseTarget[] {
  return sequence.steps
    .filter((step) => step.operationId !== null && (step.quarantined || step.ownershipReleased))
    .map((step) => releaseTargetOf(runId, sequence, step))
}

function releaseTargetOf(runId: string, sequence: LocalExecutionSequenceDetails, step: LocalExecutionStep): LocalExecutionReleaseTarget {
  return {
    runId,
    stepId: step.stepId,
    sequenceId: sequence.sequenceId,
    sequenceState: sequence.state,
    admissionMode: sequence.admissionMode,
    stepKind: step.kind,
    ordinal: step.ordinal,
    passNumber: step.passNumber,
    operationId: step.operationId as string,
    releasedInStatus: step.ownershipReleased,
  }
}

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const instantOf = (value: string): number => toWireInstant(value)?.getTime() ?? Number.NaN

const FAMILY_OF_KIND = (kind: LocalExecutionStepKind): LocalExecutionReleaseOperationFamily => (kind === 'Verification' ? 'Verification' : 'Agent')

const isCandidateStatus = (family: LocalExecutionReleaseOperationFamily, outcome: 'TimedOut' | 'Cancelled', status: unknown): status is string =>
  typeof status === 'string'
  && (family === 'Agent'
    ? (AGENT_CANDIDATE_STATUSES as readonly string[]).includes(status)
    : VERIFICATION_CANDIDATE_STATUSES[outcome].includes(status))

function toCandidate(raw: unknown, target: LocalExecutionReleaseTarget): LocalExecutionReleaseCandidate | null {
  if (!hasExactKeys(raw, CANDIDATE_KEYS)) {
    return null
  }

  if (
    !isGuid(raw.sequenceId)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_SEQUENCE_STATES, raw.sequenceState)
    || !isOneOf(LOCAL_EXECUTION_ADMISSION_MODES, raw.admissionMode)
    || !isGuid(raw.sourceCheckpointId)
    || !isSha256(raw.sourceFingerprintSha256)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, raw.stepKind)
    || !isInt(raw.ordinal, 0, MAX_STEP_ORDINAL)
    || !isInt(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || (raw.stepKind === 'Implementation') !== (raw.passNumber === 0)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_OPERATION_FAMILIES, raw.operationFamily)
    || raw.operationFamily !== FAMILY_OF_KIND(raw.stepKind)
    || !isGuid(raw.operationId)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_CANDIDATE_PROCESS_OUTCOMES, raw.processOutcome)
    || !isCandidateStatus(raw.operationFamily, raw.processOutcome, raw.operationStatus)
    || !isGuid(raw.observationId)
    || !isInt(raw.physicalExitCode, INT32_MIN, INT32_MAX)
    || !isLocalNonNegativeInt64Text(raw.observedElapsedTicks)
    || !isLocalNonNegativeInt64Text(raw.processDurationTicks)
    || !isUtcRoundTrip(raw.dispatchedAtUtc)
    || !isUtcRoundTrip(raw.observedAtUtc)
    || raw.confirmationVersion !== LOCAL_EXECUTION_RELEASE_CONFIRMATION_VERSION
    || !isSha256(raw.confirmationSha256)
    || !isUtcRoundTrip(raw.confirmationExpiresAtUtc)
  ) {
    return null
  }

  // A timed-out or cancelled process has no semantic exit code. The observation never outlasts the process it observed.
  const semantic = raw.semanticExitCode
  if (semantic !== null) {
    return null
  }

  if (BigInt(raw.observedElapsedTicks) > BigInt(raw.processDurationTicks)) {
    return null
  }

  const dispatchedAt = instantOf(raw.dispatchedAtUtc)
  const observedAt = instantOf(raw.observedAtUtc)
  const expiresAt = instantOf(raw.confirmationExpiresAtUtc)
  if (!(dispatchedAt <= observedAt) || !(observedAt <= expiresAt)) {
    return null
  }

  // The candidate is for exactly the chosen step of the accepted status, never merely a well-formed one.
  if (
    raw.sequenceId !== target.sequenceId
    || raw.sequenceState !== target.sequenceState
    || raw.admissionMode !== target.admissionMode
    || raw.stepKind !== target.stepKind
    || raw.ordinal !== target.ordinal
    || raw.passNumber !== target.passNumber
    || raw.operationId !== target.operationId
  ) {
    return null
  }

  return {
    sequenceId: raw.sequenceId,
    sequenceState: raw.sequenceState,
    admissionMode: raw.admissionMode,
    sourceCheckpointId: raw.sourceCheckpointId,
    sourceFingerprintSha256: raw.sourceFingerprintSha256,
    stepKind: raw.stepKind,
    ordinal: raw.ordinal,
    passNumber: raw.passNumber,
    operationFamily: raw.operationFamily,
    operationId: raw.operationId,
    operationStatus: raw.operationStatus,
    processOutcome: raw.processOutcome,
    semanticExitCode: null,
    observationId: raw.observationId,
    physicalExitCode: raw.physicalExitCode,
    observedElapsedTicks: raw.observedElapsedTicks,
    processDurationTicks: raw.processDurationTicks,
    dispatchedAtUtc: raw.dispatchedAtUtc,
    observedAtUtc: raw.observedAtUtc,
    confirmationVersion: raw.confirmationVersion,
    confirmationSha256: raw.confirmationSha256,
    confirmationExpiresAtUtc: raw.confirmationExpiresAtUtc,
  }
}

function toReceipt(raw: unknown, target: LocalExecutionReleaseTarget): LocalExecutionReleaseReceipt | null {
  if (!hasExactKeys(raw, RECEIPT_KEYS)) {
    return null
  }

  if (
    !isGuid(raw.releaseId)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_OPERATION_FAMILIES, raw.operationFamily)
    || !isGuid(raw.operationId)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, raw.stepKind)
    || raw.operationFamily !== FAMILY_OF_KIND(raw.stepKind)
    || !isUtcRoundTrip(raw.releasedAtUtc)
    || !isLocalRevisionText(raw.eventSequence)
    || raw.operationId !== target.operationId
    || raw.stepKind !== target.stepKind
  ) {
    return null
  }

  return {
    releaseId: raw.releaseId,
    operationFamily: raw.operationFamily,
    operationId: raw.operationId,
    stepKind: raw.stepKind,
    releasedAtUtc: raw.releasedAtUtc,
    eventSequence: raw.eventSequence,
  }
}

const ANSWER_KEYS = ['candidate', 'reasonCode', 'release', 'runId', 'status', 'stepId']
const CANDIDATE_KEYS = [
  'admissionMode',
  'confirmationExpiresAtUtc',
  'confirmationSha256',
  'confirmationVersion',
  'dispatchedAtUtc',
  'observationId',
  'observedAtUtc',
  'observedElapsedTicks',
  'operationFamily',
  'operationId',
  'operationStatus',
  'ordinal',
  'passNumber',
  'physicalExitCode',
  'processDurationTicks',
  'processOutcome',
  'semanticExitCode',
  'sequenceId',
  'sequenceState',
  'sourceCheckpointId',
  'sourceFingerprintSha256',
  'stepKind',
]
const RECEIPT_KEYS = ['eventSequence', 'operationFamily', 'operationId', 'releaseId', 'releasedAtUtc', 'stepKind']

/**
 * Accepts a parsed release answer only when the whole of it is coherent for exactly `target`'s run, step and recorded operation: the
 * closed graph has exactly the members of its status (a reason for the two refusals, a candidate only for Available and a receipt only
 * for Released), and a candidate or receipt names exactly the chosen operation. Otherwise null.
 */
export function toLocalExecutionOwnershipRelease(raw: unknown, target: LocalExecutionReleaseTarget): LocalExecutionOwnershipReleaseAnswer | null {
  if (!hasExactKeys(raw, ANSWER_KEYS) || raw.runId !== target.runId || raw.stepId !== target.stepId || !isOneOf(LOCAL_EXECUTION_RELEASE_STATUSES, raw.status)) {
    return null
  }

  const { runId, stepId } = target
  switch (raw.status) {
    case 'NotEligible':
      return raw.candidate === null && raw.release === null && isOneOf(LOCAL_EXECUTION_RELEASE_NOT_ELIGIBLE_REASONS, raw.reasonCode)
        ? { status: 'NotEligible', runId, stepId, reasonCode: raw.reasonCode }
        : null
    case 'Unavailable':
      return raw.candidate === null && raw.release === null && isOneOf(LOCAL_EXECUTION_RELEASE_UNAVAILABLE_REASONS, raw.reasonCode)
        ? { status: 'Unavailable', runId, stepId, reasonCode: raw.reasonCode }
        : null
    case 'Available': {
      if (raw.reasonCode !== null || raw.release !== null) {
        return null
      }

      const candidate = toCandidate(raw.candidate, target)
      return candidate === null ? null : { status: 'Available', runId, stepId, candidate }
    }

    default: {
      if (raw.reasonCode !== null || raw.candidate !== null) {
        return null
      }

      const release = toReceipt(raw.release, target)
      return release === null ? null : { status: 'Released', runId, stepId, release }
    }
  }
}
