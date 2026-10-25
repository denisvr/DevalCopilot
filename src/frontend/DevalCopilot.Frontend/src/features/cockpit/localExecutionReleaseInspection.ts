import { hasExactKeys, isInt, isUtcRoundTrip } from './agentAttemptVerification'
import type { LocalExecutionInspectionHeader, LocalExecutionInspectionStep } from './localExecutionInspection'
import {
  LOCAL_EXECUTION_ADMISSION_MODES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES,
  LOCAL_EXECUTION_STEP_KINDS,
  isLocalNonNegativeInt64Text,
  isLocalRevisionText,
} from './localExecutionSequence'
import type { LocalExecutionAdmissionMode, LocalExecutionStepKind } from './localExecutionSequence'

// The closed, whole-answer decoder of the recorded quarantine-release receipt of one step (ADR-0067). It accepts a parsed host answer
// only when all of it is coherent for exactly the run, step and recorded operation that was asked about: exactly the documented members
// at every level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the structural rules the host
// itself proves (family, kind, status, process outcome and nullability together). Anything else is null and is never repaired, coerced
// or partially accepted. A physical termination fact is evidence about the invoked process only; it never changes the recorded result.
// The receipt is unsigned relational consistency, not authenticated history.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/
const INT32_MIN = -(2 ** 31)
const INT32_MAX = 2 ** 31 - 1
const MAX_STEP_ORDINAL = 3 * LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES + 6 - 1

export const LOCAL_EXECUTION_RELEASE_INSPECTION_STATUSES = ['NotRecorded', 'Unavailable', 'Recorded'] as const
export type LocalExecutionReleaseInspectionStatus = (typeof LOCAL_EXECUTION_RELEASE_INSPECTION_STATUSES)[number]

export const LOCAL_EXECUTION_RELEASE_INSPECTION_FAMILIES = ['Agent', 'Verification'] as const
export type LocalExecutionReleaseInspectionFamily = (typeof LOCAL_EXECUTION_RELEASE_INSPECTION_FAMILIES)[number]

/** A release exists only for a terminal process that was stopped by the host: a clean exit already frees an operation. */
export const LOCAL_EXECUTION_RELEASE_INSPECTION_PROCESS_OUTCOMES = ['TimedOut', 'Cancelled'] as const
export type LocalExecutionReleaseInspectionProcessOutcome = (typeof LOCAL_EXECUTION_RELEASE_INSPECTION_PROCESS_OUTCOMES)[number]

/** The only sequence states that retain a releasable quarantine: the cycle ended without finishing normally. */
export const LOCAL_EXECUTION_RELEASE_INSPECTION_SEQUENCE_STATES = ['Stopped', 'NeedsAttention', 'Interrupted'] as const
export type LocalExecutionReleaseInspectionSequenceState = (typeof LOCAL_EXECUTION_RELEASE_INSPECTION_SEQUENCE_STATES)[number]

const AGENT_STATUSES = ['Failed'] as const
const VERIFICATION_STATUSES: Readonly<Record<LocalExecutionReleaseInspectionProcessOutcome, readonly string[]>> = {
  TimedOut: ['TimedOut', 'SourceChanged'],
  Cancelled: ['Cancelled', 'SourceChanged'],
}

export interface LocalExecutionReleaseInspectionReceipt {
  releaseId: string
  releasedAtUtc: string
  /** The canonical positive Int64 sequence number of the one Human run event. */
  eventSequence: string
  sequenceId: string
  sequenceState: LocalExecutionReleaseInspectionSequenceState
  admissionMode: LocalExecutionAdmissionMode
  stepKind: LocalExecutionStepKind
  ordinal: number
  passNumber: number
  operationFamily: LocalExecutionReleaseInspectionFamily
  operationId: string
  sourceCheckpointId: string
  sourceFingerprintSha256: string
  /** The operation's unchanged recorded result status. */
  operationStatus: string
  processOutcome: LocalExecutionReleaseInspectionProcessOutcome
  /** The semantic exit code the ordinary result recorded: always absent for a timeout or cancellation. Never the physical exit code. */
  semanticExitCode: null
  processDurationTicks: string
  witnessId: string
  observationId: string
  dispatchedAtUtc: string
  recordedAtUtc: string
  /** The exit code the operating system reported when the host's owned process was observed terminated. */
  physicalExitCode: number
  observedElapsedTicks: string
}

export type LocalExecutionReleaseInspectionAnswer =
  | { status: 'NotRecorded' | 'Unavailable'; runId: string; stepId: string }
  | { status: 'Recorded'; runId: string; stepId: string; receipt: LocalExecutionReleaseInspectionReceipt }

/** What the accepted inspection says about the one step an answer must belong to. Frozen when the read begins. */
export interface LocalExecutionReleaseInspectionTarget {
  runId: string
  stepId: string
  sequenceId: string
  admissionMode: LocalExecutionAdmissionMode
  stepKind: LocalExecutionStepKind
  ordinal: number
  passNumber: number
  operationFamily: LocalExecutionReleaseInspectionFamily
  operationId: string
  sourceCheckpointId: string
  sourceFingerprintSha256: string
}

export const releaseInspectionFamilyOf = (kind: LocalExecutionStepKind): LocalExecutionReleaseInspectionFamily =>
  kind === 'Verification' ? 'Verification' : 'Agent'

/** The target of a proved linked step of an accepted index, or null for a step that names no operation. */
export function releaseInspectionTargetOf(
  runId: string,
  header: Pick<LocalExecutionInspectionHeader, 'sequenceId' | 'admissionMode'>,
  step: LocalExecutionInspectionStep,
): LocalExecutionReleaseInspectionTarget | null {
  return step.operationId === null
    ? null
    : {
        runId,
        stepId: step.stepId,
        sequenceId: header.sequenceId,
        admissionMode: header.admissionMode,
        stepKind: step.kind,
        ordinal: step.ordinal,
        passNumber: step.passNumber,
        operationFamily: releaseInspectionFamilyOf(step.kind),
        operationId: step.operationId,
        sourceCheckpointId: step.sourceCheckpointId,
        sourceFingerprintSha256: step.sourceFingerprintSha256,
      }
}

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)

const isReceiptStatus = (family: LocalExecutionReleaseInspectionFamily, outcome: LocalExecutionReleaseInspectionProcessOutcome, status: unknown): status is string =>
  typeof status === 'string'
  && (family === 'Agent' ? (AGENT_STATUSES as readonly string[]).includes(status) : VERIFICATION_STATUSES[outcome].includes(status))

const ANSWER_KEYS = ['receipt', 'runId', 'status', 'stepId']
const RECEIPT_KEYS = [
  'admissionMode',
  'dispatchedAtUtc',
  'eventSequence',
  'observationId',
  'observedElapsedTicks',
  'operationFamily',
  'operationId',
  'operationStatus',
  'ordinal',
  'passNumber',
  'physicalExitCode',
  'processDurationTicks',
  'processOutcome',
  'recordedAtUtc',
  'releaseId',
  'releasedAtUtc',
  'semanticExitCode',
  'sequenceId',
  'sequenceState',
  'sourceCheckpointId',
  'sourceFingerprintSha256',
  'stepKind',
  'witnessId',
]

function toReceipt(raw: unknown, target: LocalExecutionReleaseInspectionTarget): LocalExecutionReleaseInspectionReceipt | null {
  if (!hasExactKeys(raw, RECEIPT_KEYS)) {
    return null
  }

  if (
    !isGuid(raw.releaseId)
    || !isUtcRoundTrip(raw.releasedAtUtc)
    || !isLocalRevisionText(raw.eventSequence)
    || !isGuid(raw.sequenceId)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_INSPECTION_SEQUENCE_STATES, raw.sequenceState)
    || !isOneOf(LOCAL_EXECUTION_ADMISSION_MODES, raw.admissionMode)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, raw.stepKind)
    || !isInt(raw.ordinal, 0, MAX_STEP_ORDINAL)
    || !isInt(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || (raw.stepKind === 'Implementation') !== (raw.passNumber === 0)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_INSPECTION_FAMILIES, raw.operationFamily)
    || raw.operationFamily !== releaseInspectionFamilyOf(raw.stepKind)
    || !isGuid(raw.operationId)
    || !isGuid(raw.sourceCheckpointId)
    || !isSha256(raw.sourceFingerprintSha256)
    || !isOneOf(LOCAL_EXECUTION_RELEASE_INSPECTION_PROCESS_OUTCOMES, raw.processOutcome)
    || !isReceiptStatus(raw.operationFamily, raw.processOutcome, raw.operationStatus)
    || raw.semanticExitCode !== null
    || !isLocalNonNegativeInt64Text(raw.processDurationTicks)
    || !isGuid(raw.witnessId)
    || !isGuid(raw.observationId)
    || !isUtcRoundTrip(raw.dispatchedAtUtc)
    || !isUtcRoundTrip(raw.recordedAtUtc)
    || !isInt(raw.physicalExitCode, INT32_MIN, INT32_MAX)
    || !isLocalNonNegativeInt64Text(raw.observedElapsedTicks)
  ) {
    return null
  }

  // The observation never outlasts the process it observed, and the recorded chronology is dispatch, observation, Human release.
  if (BigInt(raw.observedElapsedTicks) > BigInt(raw.processDurationTicks)) {
    return null
  }

  // Canonical UTC round-trip texts have one fixed width, so their text order is their chronological order at full tick precision.
  if (raw.dispatchedAtUtc > raw.recordedAtUtc || raw.recordedAtUtc > raw.releasedAtUtc) {
    return null
  }

  // The receipt is for exactly the selected step of the accepted index, never merely a well-formed one.
  if (
    raw.sequenceId !== target.sequenceId
    || raw.admissionMode !== target.admissionMode
    || raw.stepKind !== target.stepKind
    || raw.ordinal !== target.ordinal
    || raw.passNumber !== target.passNumber
    || raw.operationFamily !== target.operationFamily
    || raw.operationId !== target.operationId
    || raw.sourceCheckpointId !== target.sourceCheckpointId
    || raw.sourceFingerprintSha256 !== target.sourceFingerprintSha256
  ) {
    return null
  }

  return {
    releaseId: raw.releaseId,
    releasedAtUtc: raw.releasedAtUtc,
    eventSequence: raw.eventSequence,
    sequenceId: raw.sequenceId,
    sequenceState: raw.sequenceState,
    admissionMode: raw.admissionMode,
    stepKind: raw.stepKind,
    ordinal: raw.ordinal,
    passNumber: raw.passNumber,
    operationFamily: raw.operationFamily,
    operationId: raw.operationId,
    sourceCheckpointId: raw.sourceCheckpointId,
    sourceFingerprintSha256: raw.sourceFingerprintSha256,
    operationStatus: raw.operationStatus,
    processOutcome: raw.processOutcome,
    semanticExitCode: null,
    processDurationTicks: raw.processDurationTicks,
    witnessId: raw.witnessId,
    observationId: raw.observationId,
    dispatchedAtUtc: raw.dispatchedAtUtc,
    recordedAtUtc: raw.recordedAtUtc,
    physicalExitCode: raw.physicalExitCode,
    observedElapsedTicks: raw.observedElapsedTicks,
  }
}

/**
 * Accepts a parsed recorded-release answer only when the whole of it is coherent for exactly `target`'s run and step: the closed graph
 * has exactly its four members, a receipt only for Recorded, and a Recorded receipt names exactly the selected sequence, mode, step,
 * operation and pinned source. Otherwise null.
 */
export function toLocalExecutionReleaseInspection(
  raw: unknown,
  target: LocalExecutionReleaseInspectionTarget,
): LocalExecutionReleaseInspectionAnswer | null {
  if (
    !hasExactKeys(raw, ANSWER_KEYS)
    || raw.runId !== target.runId
    || raw.stepId !== target.stepId
    || !isOneOf(LOCAL_EXECUTION_RELEASE_INSPECTION_STATUSES, raw.status)
  ) {
    return null
  }

  const { runId, stepId } = target
  if (raw.status !== 'Recorded') {
    return raw.receipt === null ? { status: raw.status, runId, stepId } : null
  }

  const receipt = toReceipt(raw.receipt, target)
  return receipt === null ? null : { status: 'Recorded', runId, stepId, receipt }
}
