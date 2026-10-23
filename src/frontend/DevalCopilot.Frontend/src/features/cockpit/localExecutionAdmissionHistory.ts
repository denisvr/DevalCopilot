import { toWireInstant } from './agentAttemptEvidenceWire'
import { hasExactKeys, isInt, isUtcRoundTrip } from './agentAttemptVerification'
import {
  LOCAL_EXECUTION_REASON_CODES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES,
  LOCAL_EXECUTION_SEQUENCE_STATES,
  LOCAL_EXECUTION_STEP_KINDS,
  LOCAL_EXECUTION_WAITING_REASONS,
  isLocalExecutionSequenceLive,
  isLocalRevisionText,
  maximumLocalExecutionSteps,
} from './localExecutionSequence'
import type {
  LocalExecutionReasonCode,
  LocalExecutionSequenceStateName,
  LocalExecutionStepKind,
  LocalExecutionWaitingReason,
} from './localExecutionSequence'

// The closed, whole-answer decoder of the recorded local execution admission ledger (ADR-0063). It accepts a parsed host answer only when
// all of it is coherent for exactly the run that was asked about: exactly the documented members at every level, exact types, closed
// vocabularies, canonical identifiers, times and Int64 texts, and the structural rules the host itself proved (strictly increasing order
// numbers, gapless ordinals, the closed kind shape, the link and reason shape of each admission state, only Consumed admissions before the
// last and a last admission that fits the recorded sequence state). Anything else is null and is never repaired, trimmed, coerced into a
// known state or partially accepted. A recorded admission, step link or waiting reason is bookkeeping: never an operation, a current
// eligibility, a rank, a promised start or proof of any outcome.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const MAX_NAME_UNITS = 200
const ADVANCEMENT_WINDOW_MILLISECONDS = 2 * 60 * 60 * 1000
const MAX_LEDGER = maximumLocalExecutionSteps(LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES)

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const isName = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_NAME_UNITS
const instantOf = (value: string): number => toWireInstant(value)?.getTime() ?? Number.NaN

export const LOCAL_EXECUTION_ADMISSION_HISTORY_STATUSES = ['NotRecorded', 'NotQueued', 'Recorded', 'Unavailable'] as const
export type LocalExecutionAdmissionHistoryStatus = (typeof LOCAL_EXECUTION_ADMISSION_HISTORY_STATUSES)[number]

/** The recorded state of one admission: the full ledger vocabulary, wider than the live disclosure's outstanding states. */
export const LOCAL_EXECUTION_ADMISSION_RECORDED_STATES = ['Waiting', 'Reserved', 'Consumed', 'Retired', 'Unresolved'] as const
export type LocalExecutionAdmissionRecordedState = (typeof LOCAL_EXECUTION_ADMISSION_RECORDED_STATES)[number]

/** The stored bookkeeping of the queued sequence this ledger belongs to: never effective live control or authority. */
export interface LocalExecutionAdmissionHistoryHeader {
  sequenceId: string
  admissionMode: 'WaitForAdmission'
  recordedState: LocalExecutionSequenceStateName
  reasonCode: LocalExecutionReasonCode | null
  /** A canonical positive Int64 text. */
  sequenceRevision: string
  /** The validated invariant UTC round-trip texts, exactly as the host wrote them. */
  armedAtUtc: string
  advancementDeadlineUtc: string
  updatedAtUtc: string
  endedAtUtc: string | null
}

/**
 * One recorded admission. `admissionNumber` is the recorded admission order, never an exact global queue position. `waitingReason` is the
 * last durably recorded observation made at `waitingReasonRecordedAtUtc`, never present capacity, rank or eligibility. `stepId` is
 * recorded structure, not proof of an operation or its outcome; the existing selected-step inspection proves those.
 */
export interface LocalExecutionAdmissionRecord {
  admissionId: string
  /** A canonical positive Int64 text. */
  admissionNumber: string
  ordinal: number
  kind: LocalExecutionStepKind
  passNumber: number
  recipeOrdinal: number | null
  /** Untrusted text: rendered inert, never interpreted. */
  recipeName: string | null
  recordedState: LocalExecutionAdmissionRecordedState
  waitingReason: LocalExecutionWaitingReason
  waitingReasonRecordedAtUtc: string
  enqueuedAtUtc: string
  updatedAtUtc: string
  /** A canonical positive Int64 text. */
  revision: string
  reasonCode: LocalExecutionReasonCode | null
  stepId: string | null
}

export type LocalExecutionAdmissionHistory =
  // A known run with no recorded sequence. This is not "no execution": nothing else is claimed.
  | { status: 'NotRecorded' }
  // A coherent Immediate sequence has no admissions: it never queued, which is not an empty queue.
  | { status: 'NotQueued' }
  // A sequence is present but its root, structure or linkage could not be proved; none of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; sequence: LocalExecutionAdmissionHistoryHeader; admissions: readonly LocalExecutionAdmissionRecord[] }

const HISTORY_KEYS = ['runId', 'status', 'sequence', 'admissions'] as const
const HEADER_KEYS = [
  'sequenceId',
  'admissionMode',
  'recordedState',
  'reasonCode',
  'sequenceRevision',
  'armedAtUtc',
  'advancementDeadlineUtc',
  'updatedAtUtc',
  'endedAtUtc',
] as const
const ADMISSION_KEYS = [
  'admissionId',
  'admissionNumber',
  'ordinal',
  'kind',
  'passNumber',
  'recipeOrdinal',
  'recipeName',
  'recordedState',
  'waitingReason',
  'waitingReasonRecordedAtUtc',
  'enqueuedAtUtc',
  'updatedAtUtc',
  'revision',
  'reasonCode',
  'stepId',
] as const

const isCorrection = (kind: LocalExecutionStepKind): boolean => kind === 'ReviewCorrection' || kind === 'DiagnosisCorrection'

function toHeader(raw: unknown): LocalExecutionAdmissionHistoryHeader | null {
  if (!hasExactKeys(raw, HEADER_KEYS)) {
    return null
  }

  const { recordedState, reasonCode, endedAtUtc } = raw
  if (
    !isGuid(raw.sequenceId)
    || raw.admissionMode !== 'WaitForAdmission'
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, recordedState)
    // A queued sequence is first waiting for admission: it is never recorded as Armed.
    || recordedState === 'Armed'
    || !isLocalRevisionText(raw.sequenceRevision)
    || BigInt(raw.sequenceRevision) > BigInt(Number.MAX_SAFE_INTEGER)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || !isUtcRoundTrip(raw.advancementDeadlineUtc)
    || instantOf(raw.updatedAtUtc) < instantOf(raw.armedAtUtc)
    || instantOf(raw.advancementDeadlineUtc) !== instantOf(raw.armedAtUtc) + ADVANCEMENT_WINDOW_MILLISECONDS
  ) {
    return null
  }

  // A live state has neither a reason nor an end; an ended one has both (the reason a closed vocabulary member).
  const live = isLocalExecutionSequenceLive(recordedState)
  if (live) {
    if (reasonCode !== null || endedAtUtc !== null) {
      return null
    }
  } else if (!isOneOf(LOCAL_EXECUTION_REASON_CODES, reasonCode) || !isUtcRoundTrip(endedAtUtc) || instantOf(endedAtUtc) < instantOf(raw.armedAtUtc)) {
    return null
  }

  return {
    sequenceId: raw.sequenceId,
    admissionMode: 'WaitForAdmission',
    recordedState,
    reasonCode: live ? null : (reasonCode as LocalExecutionReasonCode),
    sequenceRevision: raw.sequenceRevision,
    armedAtUtc: raw.armedAtUtc,
    advancementDeadlineUtc: raw.advancementDeadlineUtc,
    updatedAtUtc: raw.updatedAtUtc,
    endedAtUtc: live ? null : (endedAtUtc as string),
  }
}

/** One admission member on its own: the fixed vocabularies, the closed kind shape, the reason and link shape of its state and its times. */
function toAdmission(raw: unknown): LocalExecutionAdmissionRecord | null {
  if (!hasExactKeys(raw, ADMISSION_KEYS)) {
    return null
  }

  const { kind, recordedState, recipeOrdinal, recipeName, reasonCode, stepId } = raw
  if (
    !isGuid(raw.admissionId)
    || !isLocalRevisionText(raw.admissionNumber)
    || !isInt(raw.ordinal, 0, maximumLocalExecutionSteps(LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES) - 1)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, kind)
    || !isInt(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || !isOneOf(LOCAL_EXECUTION_ADMISSION_RECORDED_STATES, recordedState)
    || !isOneOf(LOCAL_EXECUTION_WAITING_REASONS, raw.waitingReason)
    || !isUtcRoundTrip(raw.waitingReasonRecordedAtUtc)
    || !isUtcRoundTrip(raw.enqueuedAtUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || instantOf(raw.waitingReasonRecordedAtUtc) < instantOf(raw.enqueuedAtUtc)
    || instantOf(raw.updatedAtUtc) < instantOf(raw.waitingReasonRecordedAtUtc)
    || !isLocalRevisionText(raw.revision)
  ) {
    return null
  }

  const implementation = kind === 'Implementation'
  const verification = kind === 'Verification'
  if (
    implementation !== (raw.passNumber === 0)
    || implementation !== (raw.ordinal === 0)
    || verification !== (recipeOrdinal !== null)
    || verification !== (recipeName !== null)
    || (verification && (!isInt(recipeOrdinal, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES - 1) || !isName(recipeName)))
  ) {
    return null
  }

  // Only a Retired or Unresolved admission carries a fixed reason. A waiting admission has no step; a Reserved, Consumed or Unresolved
  // one links exactly one; a Retired one links the revoked step of a reservation or none (it closed while still waiting).
  const reasoned = recordedState === 'Retired' || recordedState === 'Unresolved'
  const mustLink = recordedState === 'Reserved' || recordedState === 'Consumed' || recordedState === 'Unresolved'
  if (
    (reasoned ? !isOneOf(LOCAL_EXECUTION_REASON_CODES, reasonCode) : reasonCode !== null)
    || (stepId !== null && !isGuid(stepId))
    || (mustLink && stepId === null)
    || (recordedState === 'Waiting' && stepId !== null)
  ) {
    return null
  }

  return {
    admissionId: raw.admissionId,
    admissionNumber: raw.admissionNumber,
    ordinal: raw.ordinal,
    kind,
    passNumber: raw.passNumber,
    recipeOrdinal: verification ? (recipeOrdinal as number) : null,
    recipeName: verification ? (recipeName as string) : null,
    recordedState,
    waitingReason: raw.waitingReason,
    waitingReasonRecordedAtUtc: raw.waitingReasonRecordedAtUtc,
    enqueuedAtUtc: raw.enqueuedAtUtc,
    updatedAtUtc: raw.updatedAtUtc,
    revision: raw.revision,
    reasonCode: reasoned ? (reasonCode as LocalExecutionReasonCode) : null,
    stepId: stepId as string | null,
  }
}

/** Whether the last recorded admission fits the recorded sequence state, exactly as the host proved it. */
function fitsSequenceState(state: LocalExecutionSequenceStateName, tail: LocalExecutionAdmissionRecordedState): boolean {
  switch (state) {
    case 'WaitingForAdmission':
      return tail === 'Waiting'
    case 'PreparingStep':
      return tail === 'Reserved'
    case 'AwaitingStepResult':
    case 'Stopping':
    case 'ReadyForHumanApproval':
    case 'HumanEscalation':
      return tail === 'Consumed'
    case 'Stopped':
    case 'NeedsAttention':
    case 'Interrupted':
      return tail === 'Consumed' || tail === 'Retired' || tail === 'Unresolved'
    default:
      return false
  }
}

/**
 * The complete ledger as one coherent recorded sequence of admissions: nonempty, bounded by the largest legitimate cycle, at its own
 * consecutive ordinals with strictly increasing order numbers, unique identities and steps, only the last admission outstanding or closed
 * and a correction never before the work it follows. Only Consumed admissions precede the last.
 */
function toAdmissions(raw: unknown[], header: LocalExecutionAdmissionHistoryHeader): LocalExecutionAdmissionRecord[] | null {
  if (raw.length === 0 || raw.length > MAX_LEDGER) {
    return null
  }

  const admissionIds = new Set<string>()
  const stepIds = new Set<string>()
  const memberships = new Set<string>()
  const admissions: LocalExecutionAdmissionRecord[] = []
  for (const [position, item] of raw.entries()) {
    const admission = toAdmission(item)
    const previous = admissions[position - 1]
    if (
      admission === null
      || admission.ordinal !== position
      || admissionIds.has(admission.admissionId)
      || (admission.stepId !== null && stepIds.has(admission.stepId))
      || (previous !== undefined && BigInt(admission.admissionNumber) <= BigInt(previous.admissionNumber))
      || (position < raw.length - 1 && admission.recordedState !== 'Consumed')
    ) {
      return null
    }

    if (admission.kind === 'Verification') {
      const membership = `${admission.passNumber}:${admission.recipeOrdinal}`
      if (memberships.has(membership)) {
        return null
      }

      memberships.add(membership)
    }

    // A correction follows an earlier review or diagnosis of the matching origin, never the implementation or another correction.
    if (isCorrection(admission.kind)) {
      const origin: LocalExecutionStepKind = admission.kind === 'ReviewCorrection' ? 'CodeReview' : 'VerificationDiagnosis'
      if (!admissions.some((earlier) => earlier.kind === origin)) {
        return null
      }
    }

    admissionIds.add(admission.admissionId)
    if (admission.stepId !== null) {
      stepIds.add(admission.stepId)
    }

    admissions.push(admission)
  }

  return fitsSequenceState(header.recordedState, admissions[admissions.length - 1].recordedState) ? admissions : null
}

/**
 * Accepts a parsed admission-ledger answer only when the whole of it is coherent for exactly `expectedRunId`: closed shapes at every
 * level, the requested run, a closed status (`NotRecorded`, `NotQueued` and `Unavailable` carry a null header and an empty list;
 * `Recorded` carries the stored bookkeeping header and the complete nonempty ledger). Anything else is null and is never repaired,
 * trimmed or partially accepted.
 */
export function toLocalExecutionAdmissionHistory(raw: unknown, expectedRunId: string): LocalExecutionAdmissionHistory | null {
  if (!hasExactKeys(raw, HISTORY_KEYS) || raw.runId !== expectedRunId || !Array.isArray(raw.admissions)) {
    return null
  }

  const { status, sequence, admissions } = raw
  if (status === 'NotRecorded' || status === 'NotQueued' || status === 'Unavailable') {
    return sequence === null && admissions.length === 0 ? { status } : null
  }

  if (status !== 'Recorded') {
    return null
  }

  const header = toHeader(sequence)
  const accepted = header === null ? null : toAdmissions(admissions, header)
  return header === null || accepted === null ? null : { status, sequence: header, admissions: accepted }
}
