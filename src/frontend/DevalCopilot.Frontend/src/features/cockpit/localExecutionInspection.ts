import { toWireInstant } from './agentAttemptEvidenceWire'
import { toVerifiedIdentity } from './agentAttemptHistory'
import type { AgentAttemptIdentityExpectation, AgentAttemptResponseContract } from './agentAttemptHistory'
import {
  hasExactKeys,
  isByteCount,
  isInt,
  isUtcRoundTrip,
  toVerificationStreamDescriptor,
} from './agentAttemptVerification'
import type { RecordedVerificationStreamDescriptor, VerificationStream, VerificationStreamDescriptor, VerificationWindowAnswer } from './agentAttemptVerification'
import {
  LOCAL_EXECUTION_ADMISSION_MODES,
  LOCAL_EXECUTION_PLAN_AUTHORITIES,
  LOCAL_EXECUTION_REASON_CODES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES,
  LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES,
  LOCAL_EXECUTION_SEQUENCE_STATES,
  LOCAL_EXECUTION_STEP_KINDS,
  LOCAL_EXECUTION_STEP_STATES,
  isLocalExecutionSequenceLive,
  isLocalRevisionText,
  maximumLocalExecutionSteps,
} from './localExecutionSequence'
import type {
  LocalExecutionAdmissionMode,
  LocalExecutionPlanAuthority,
  LocalExecutionReasonCode,
  LocalExecutionSequenceStateName,
  LocalExecutionStepKind,
  LocalExecutionStepState,
} from './localExecutionSequence'

// The closed, whole-answer decoders of the three recorded local execution inspection operations (ADR-0061). Each accepts a parsed host
// answer only when all of it is coherent for exactly the run, sequence, step and stream that was asked about: exactly the documented
// members at every level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the structural rules the
// host itself proves. Anything else is null (or `incoherent`) and is never repaired, trimmed, coerced into a known state or partially
// accepted. A recorded link, claim or dispatch marker is bookkeeping: it is never proof of an outcome or of a running process.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/
const MAX_NAME_UNITS = 200
const MAX_STEP_ORDINAL = 3 * LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES + 6 - 1
const INT32_MIN = -(2 ** 31)
const INT32_MAX = 2 ** 31 - 1

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const isName = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_NAME_UNITS
const instantOf = (value: string): number => toWireInstant(value)?.getTime() ?? Number.NaN

export const LOCAL_EXECUTION_INSPECTION_STREAMS = ['stdout', 'stderr'] as const

export const LOCAL_EXECUTION_VERIFICATION_STATUSES = [
  'Running',
  'Passed',
  'Failed',
  'TimedOut',
  'Cancelled',
  'Interrupted',
  'SourceChanged',
  'LocalExecutionSequenceDispatchRefused',
] as const
export type LocalExecutionVerificationStatus = (typeof LOCAL_EXECUTION_VERIFICATION_STATUSES)[number]

export const LOCAL_EXECUTION_VERIFICATION_OUTCOMES = ['Exited', 'TimedOut', 'Cancelled', 'Failed'] as const
export type LocalExecutionVerificationOutcome = (typeof LOCAL_EXECUTION_VERIFICATION_OUTCOMES)[number]

/** The stored bookkeeping of the recorded sequence: never effective live control, a final approval or a commit proof. */
export interface LocalExecutionInspectionHeader {
  sequenceId: string
  planProposalMessageId: string
  planAuthority: LocalExecutionPlanAuthority
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  recordedState: LocalExecutionSequenceStateName
  reasonCode: LocalExecutionReasonCode | null
  /** A canonical positive Int64 text. */
  revision: string
  recipeCount: number
  correctionAllowance: number
  /** The validated invariant UTC round-trip texts, exactly as the host wrote them. */
  armedAtUtc: string
  updatedAtUtc: string
  endedAtUtc: string | null
  /** ADR-0062: the recorded, immutable admission mode. A recorded waiting state is bookkeeping, never current eligibility. */
  admissionMode: LocalExecutionAdmissionMode
}

/** One recorded step. `operationId` is a recorded link, not proof of the operation or of any outcome. */
export interface LocalExecutionInspectionStep {
  stepId: string
  ordinal: number
  kind: LocalExecutionStepKind
  passNumber: number
  recipeOrdinal: number | null
  /** Untrusted text: rendered inert, never interpreted. */
  recipeName: string | null
  recordedState: LocalExecutionStepState
  reasonCode: LocalExecutionReasonCode | null
  sourceCheckpointId: string
  sourceFingerprintSha256: string
  reportMessageId: string | null
  subjectAttemptId: string | null
  operationId: string | null
  preparationReservedAtUtc: string
  preparationDeadlineUtc: string
  claimedAtUtc: string | null
  resultDeadlineUtc: string | null
}

export type LocalExecutionInspectionIndex =
  // A known run with no recorded sequence. This is not "no execution": nothing else is claimed.
  | { status: 'NotRecorded' }
  // A sequence is present but its structure could not be proved or changed during the read; none of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; sequence: LocalExecutionInspectionHeader; steps: readonly LocalExecutionInspectionStep[] }

const INDEX_KEYS = ['runId', 'status', 'sequence', 'steps'] as const
const HEADER_KEYS = [
  'sequenceId',
  'planProposalMessageId',
  'planAuthority',
  'workspaceId',
  'checkpointId',
  'fingerprintSha256',
  'recordedState',
  'reasonCode',
  'revision',
  'recipeCount',
  'correctionAllowance',
  'armedAtUtc',
  'updatedAtUtc',
  'endedAtUtc',
  'admissionMode',
] as const
const STEP_KEYS = [
  'stepId',
  'ordinal',
  'kind',
  'passNumber',
  'recipeOrdinal',
  'recipeName',
  'recordedState',
  'reasonCode',
  'sourceCheckpointId',
  'sourceFingerprintSha256',
  'reportMessageId',
  'subjectAttemptId',
  'operationId',
  'preparationReservedAtUtc',
  'preparationDeadlineUtc',
  'claimedAtUtc',
  'resultDeadlineUtc',
] as const
const STEP_ANSWER_KEYS = ['runId', 'sequenceId', 'stepId', 'status', 'step', 'agent', 'verification'] as const
const AGENT_KEYS = ['id', 'attemptNumber', 'role', 'provider', 'responseContract'] as const
const VERIFICATION_KEYS = [
  'executionId',
  'executionNumber',
  'commandName',
  'status',
  'outcome',
  'exitCode',
  'claimedAtUtc',
  'dispatchedAtUtc',
  'completedAtUtc',
  'standardOutput',
  'standardError',
] as const
const WINDOW_KEYS = ['runId', 'sequenceId', 'stepId', 'executionId', 'stream', 'status', 'artifact', 'text', 'nextOffset', 'totalLength'] as const

const isOptionalInstant = (value: unknown): value is string | null => value === null || isUtcRoundTrip(value)

/** The response contract an Agent step of each kind is claimed under (both correction origins share one). */
export function localExecutionAgentContractOf(kind: LocalExecutionStepKind): AgentAttemptResponseContract | null {
  switch (kind) {
    case 'Implementation':
      return 'ImplementationReport'
    case 'CodeReview':
      return 'ImplementationReview'
    case 'VerificationDiagnosis':
      return 'VerificationDiagnosis'
    case 'ReviewCorrection':
    case 'DiagnosisCorrection':
      return 'ReviewCorrection'
    default:
      return null
  }
}

const isCorrection = (kind: LocalExecutionStepKind): boolean => kind === 'ReviewCorrection' || kind === 'DiagnosisCorrection'

function toHeader(raw: unknown): LocalExecutionInspectionHeader | null {
  if (!hasExactKeys(raw, HEADER_KEYS)) {
    return null
  }

  const { recordedState, reasonCode, endedAtUtc } = raw
  if (
    !isGuid(raw.sequenceId)
    || !isGuid(raw.planProposalMessageId)
    || !isOneOf(LOCAL_EXECUTION_PLAN_AUTHORITIES, raw.planAuthority)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isSha256(raw.fingerprintSha256)
    || !isOneOf(LOCAL_EXECUTION_SEQUENCE_STATES, recordedState)
    || !isOneOf(LOCAL_EXECUTION_ADMISSION_MODES, raw.admissionMode)
    || (recordedState === 'WaitingForAdmission' && raw.admissionMode !== 'WaitForAdmission')
    || !isLocalRevisionText(raw.revision)
    || BigInt(raw.revision) > BigInt(Number.MAX_SAFE_INTEGER)
    || !isInt(raw.recipeCount, 1, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES)
    || !isInt(raw.correctionAllowance, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_CORRECTIONS)
    || !isUtcRoundTrip(raw.armedAtUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || instantOf(raw.updatedAtUtc) < instantOf(raw.armedAtUtc)
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
    planProposalMessageId: raw.planProposalMessageId,
    planAuthority: raw.planAuthority,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    fingerprintSha256: raw.fingerprintSha256,
    recordedState,
    reasonCode: live ? null : (reasonCode as LocalExecutionReasonCode),
    revision: raw.revision,
    recipeCount: raw.recipeCount,
    correctionAllowance: raw.correctionAllowance,
    armedAtUtc: raw.armedAtUtc,
    updatedAtUtc: raw.updatedAtUtc,
    endedAtUtc: live ? null : (endedAtUtc as string),
    admissionMode: raw.admissionMode,
  }
}

/**
 * Decodes one step member as the index and the selected-step answer both carry it. The shape rules are the host's: the implementation is
 * ordinal and pass zero with no recipe, report or subject; every other step has a pass of one to three and a report; a verification
 * names a recipe position below the recipe count with a nonblank name of at most 200 units and no other kind does; a correction names its
 * subject attempt; a Reserved, Revoked or Unresolved step has no link, claim time or result deadline and a Claimed or Concluded one has all
 * three; only a Revoked or Unresolved step carries a fixed reason.
 */
function toStep(raw: unknown, recipeCount: number): LocalExecutionInspectionStep | null {
  if (!hasExactKeys(raw, STEP_KEYS)) {
    return null
  }

  const { kind, recordedState } = raw
  if (
    !isGuid(raw.stepId)
    || !isInt(raw.ordinal, 0, MAX_STEP_ORDINAL)
    || !isOneOf(LOCAL_EXECUTION_STEP_KINDS, kind)
    || !isInt(raw.passNumber, 0, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_PASSES)
    || !isOneOf(LOCAL_EXECUTION_STEP_STATES, recordedState)
    || !isGuid(raw.sourceCheckpointId)
    || !isSha256(raw.sourceFingerprintSha256)
    || !isUtcRoundTrip(raw.preparationReservedAtUtc)
    || !isUtcRoundTrip(raw.preparationDeadlineUtc)
    || instantOf(raw.preparationDeadlineUtc) <= instantOf(raw.preparationReservedAtUtc)
    || !isOptionalInstant(raw.claimedAtUtc)
    || !isOptionalInstant(raw.resultDeadlineUtc)
  ) {
    return null
  }

  const implementation = kind === 'Implementation'
  const verification = kind === 'Verification'
  const { recipeOrdinal, recipeName, reportMessageId, subjectAttemptId, operationId, reasonCode, claimedAtUtc, resultDeadlineUtc } = raw
  if (
    implementation !== (raw.passNumber === 0)
    || implementation !== (raw.ordinal === 0)
    || implementation !== (reportMessageId === null)
    || (reportMessageId !== null && !isGuid(reportMessageId))
    || isCorrection(kind) !== (subjectAttemptId !== null)
    || (subjectAttemptId !== null && !isGuid(subjectAttemptId))
    || verification !== (recipeOrdinal !== null)
    || verification !== (recipeName !== null)
    || (verification && (!isInt(recipeOrdinal, 0, recipeCount - 1) || !isName(recipeName)))
  ) {
    return null
  }

  const linked = recordedState === 'Claimed' || recordedState === 'Concluded'
  const reasoned = recordedState === 'Revoked' || recordedState === 'Unresolved'
  if (
    linked !== (operationId !== null)
    || (operationId !== null && !isGuid(operationId))
    || linked !== (claimedAtUtc !== null)
    || linked !== (resultDeadlineUtc !== null)
    || (linked
      && (instantOf(claimedAtUtc as string) < instantOf(raw.preparationReservedAtUtc)
        || instantOf(resultDeadlineUtc as string) <= instantOf(claimedAtUtc as string)))
    || (reasoned ? !isOneOf(LOCAL_EXECUTION_REASON_CODES, reasonCode) : reasonCode !== null)
  ) {
    return null
  }

  return {
    stepId: raw.stepId,
    ordinal: raw.ordinal,
    kind,
    passNumber: raw.passNumber,
    recipeOrdinal: verification ? (recipeOrdinal as number) : null,
    recipeName: verification ? (recipeName as string) : null,
    recordedState,
    reasonCode: reasoned ? (reasonCode as LocalExecutionReasonCode) : null,
    sourceCheckpointId: raw.sourceCheckpointId,
    sourceFingerprintSha256: raw.sourceFingerprintSha256,
    reportMessageId: reportMessageId as string | null,
    subjectAttemptId: subjectAttemptId as string | null,
    operationId: operationId as string | null,
    preparationReservedAtUtc: raw.preparationReservedAtUtc,
    preparationDeadlineUtc: raw.preparationDeadlineUtc,
    claimedAtUtc: claimedAtUtc as string | null,
    resultDeadlineUtc: resultDeadlineUtc as string | null,
  }
}

/** The complete step list as one coherent recorded sequence: consecutive ordinals, unique identities and the host's structural rules. */
function toSteps(raw: unknown[], header: LocalExecutionInspectionHeader): LocalExecutionInspectionStep[] | null {
  if (raw.length > maximumLocalExecutionSteps(header.recipeCount)) {
    return null
  }

  const stepIds = new Set<string>()
  const operationIds = new Set<string>()
  const memberships = new Set<string>()
  const steps: LocalExecutionInspectionStep[] = []
  for (const [position, item] of raw.entries()) {
    const step = toStep(item, header.recipeCount)
    if (
      step === null
      || step.ordinal !== position
      || stepIds.has(step.stepId)
      || (step.operationId !== null && operationIds.has(step.operationId))
      // The implementation operates on the starting checkpoint pinned at arming.
      || (step.kind === 'Implementation'
        && (step.sourceCheckpointId !== header.checkpointId || step.sourceFingerprintSha256 !== header.fingerprintSha256))
      // Only the last step can still be outstanding, revoked or unresolved: every earlier one concluded before its successor existed.
      || (position < raw.length - 1 && step.recordedState !== 'Concluded')
    ) {
      return null
    }

    if (step.kind === 'Verification') {
      const membership = `${step.passNumber}:${step.recipeOrdinal}`
      if (memberships.has(membership)) {
        return null
      }

      memberships.add(membership)
    }

    // A correction names an earlier linked review (or diagnosis) of the matching origin, never an arbitrary operation.
    if (isCorrection(step.kind)) {
      const origin: LocalExecutionStepKind = step.kind === 'ReviewCorrection' ? 'CodeReview' : 'VerificationDiagnosis'
      if (!steps.some((earlier) => earlier.kind === origin && earlier.operationId === step.subjectAttemptId)) {
        return null
      }
    }

    stepIds.add(step.stepId)
    if (step.operationId !== null) {
      operationIds.add(step.operationId)
    }

    steps.push(step)
  }

  return steps
}

/**
 * Accepts a parsed index answer only when the whole of it is coherent for exactly `expectedRunId`: closed shapes at every level, the
 * requested run, a closed status (`NotRecorded` and `Unavailable` carry a null header and an empty list; `Recorded` carries the stored
 * bookkeeping header and the complete step list of at most 3 * recipes + 6 members at their own consecutive ordinals). Anything else is
 * null and is never repaired, trimmed or partially accepted.
 */
export function toLocalExecutionInspectionIndex(raw: unknown, expectedRunId: string): LocalExecutionInspectionIndex | null {
  if (!hasExactKeys(raw, INDEX_KEYS) || raw.runId !== expectedRunId || !Array.isArray(raw.steps)) {
    return null
  }

  const { status, sequence, steps } = raw
  if (status === 'NotRecorded' || status === 'Unavailable') {
    return sequence === null && steps.length === 0 ? { status } : null
  }

  if (status !== 'Recorded') {
    return null
  }

  const header = toHeader(sequence)
  const accepted = header === null ? null : toSteps(steps, header)
  return header === null || accepted === null ? null : { status, sequence: header, steps: accepted }
}

/** What the selected-step read asked about: the run and sequence, and the step as the accepted index showed it. */
export interface LocalExecutionStepRequestFacts {
  runId: string
  sequenceId: string
  step: LocalExecutionInspectionStep
}

/** The proved verification of a step: facts as recorded, never promoted to a clean exit, and two admitted stream descriptors. */
export interface LocalExecutionInspectionVerification {
  executionId: string
  executionNumber: number
  /** The immutable snapshot command name: untrusted text, rendered inert. */
  commandName: string
  status: LocalExecutionVerificationStatus
  outcome: LocalExecutionVerificationOutcome | null
  exitCode: number | null
  claimedAtUtc: string
  dispatchedAtUtc: string | null
  completedAtUtc: string | null
  standardOutput: VerificationStreamDescriptor
  standardError: VerificationStreamDescriptor
}

export type LocalExecutionStepAnswer =
  | { kind: 'agent'; step: LocalExecutionInspectionStep; agent: AgentAttemptIdentityExpectation }
  | { kind: 'verification'; step: LocalExecutionInspectionStep; verification: LocalExecutionInspectionVerification }
  // The coherent Reserved, Revoked or Unresolved no-link shape: nothing is guessed about a reserved operation.
  | { kind: 'no-operation' }
  | { kind: 'unavailable' }
  | { kind: 'incoherent' }

const INCOHERENT_STEP: LocalExecutionStepAnswer = { kind: 'incoherent' }

const FORWARD: Readonly<Record<LocalExecutionStepState, readonly LocalExecutionStepState[]>> = {
  Reserved: ['Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved'],
  Claimed: ['Claimed', 'Concluded'],
  Concluded: ['Concluded'],
  Revoked: ['Revoked'],
  Unresolved: ['Unresolved'],
}

/**
 * Whether a step read now is the very step the index showed: every immutable fact equal and its state at or after the indexed one
 * (a step legitimately advances between two reads, and an established link never changes or disappears).
 */
function isSameStep(read: LocalExecutionInspectionStep, indexed: LocalExecutionInspectionStep): boolean {
  return read.stepId === indexed.stepId
    && read.ordinal === indexed.ordinal
    && read.kind === indexed.kind
    && read.passNumber === indexed.passNumber
    && read.recipeOrdinal === indexed.recipeOrdinal
    && read.recipeName === indexed.recipeName
    && read.sourceCheckpointId === indexed.sourceCheckpointId
    && read.sourceFingerprintSha256 === indexed.sourceFingerprintSha256
    && read.reportMessageId === indexed.reportMessageId
    && read.subjectAttemptId === indexed.subjectAttemptId
    && read.preparationReservedAtUtc === indexed.preparationReservedAtUtc
    && read.preparationDeadlineUtc === indexed.preparationDeadlineUtc
    && FORWARD[indexed.recordedState].includes(read.recordedState)
    && (indexed.operationId === null || read.operationId === indexed.operationId)
}

function toAgentHeader(raw: unknown, step: LocalExecutionInspectionStep): AgentAttemptIdentityExpectation | null {
  if (!hasExactKeys(raw, AGENT_KEYS) || !isGuid(raw.id) || raw.id !== step.operationId || !isInt(raw.attemptNumber, 1, Number.MAX_SAFE_INTEGER)) {
    return null
  }

  const identity = toVerifiedIdentity(raw.role, raw.provider, raw.responseContract)
  return identity !== null && identity.responseContract === localExecutionAgentContractOf(step.kind)
    ? { attemptId: raw.id, attemptNumber: raw.attemptNumber, ...identity }
    : null
}

/**
 * The statuses, process outcomes, exit codes and times a recorded verification can have, and none else: Running has no completion;
 * Passed is an Exited process with exit code 0; Failed is an Exited process with a nonzero code or a failed one without a code; TimedOut
 * and Cancelled carry their own outcome and no code; Interrupted and a dispatch refusal have no outcome; a refusal was never dispatched;
 * SourceChanged is complete, with an outcome exactly when it was dispatched. An exit code exists exactly for an Exited outcome, and the
 * claim, dispatch and completion times never run backwards. A status is bookkeeping and is never promoted to a clean exit.
 */
function isCoherentVerificationShape(verification: Omit<LocalExecutionInspectionVerification, 'standardOutput' | 'standardError'>): boolean {
  const { status, outcome, exitCode, claimedAtUtc, dispatchedAtUtc, completedAtUtc } = verification
  if (
    (exitCode !== null) !== (outcome === 'Exited')
    || (dispatchedAtUtc !== null && instantOf(dispatchedAtUtc) < instantOf(claimedAtUtc))
    || (completedAtUtc !== null && instantOf(completedAtUtc) < instantOf(dispatchedAtUtc ?? claimedAtUtc))
  ) {
    return false
  }

  const dispatched = dispatchedAtUtc !== null
  const completed = completedAtUtc !== null
  switch (status) {
    case 'Running':
      return !completed && outcome === null
    case 'Passed':
      return dispatched && completed && outcome === 'Exited' && exitCode === 0
    case 'Failed':
      return dispatched && completed && ((outcome === 'Exited' && exitCode !== 0) || outcome === 'Failed')
    case 'TimedOut':
      return dispatched && completed && outcome === 'TimedOut'
    case 'Cancelled':
      return dispatched && completed && outcome === 'Cancelled'
    case 'Interrupted':
      return completed && outcome === null
    case 'SourceChanged':
      return completed && (outcome !== null) === dispatched
    default:
      return !dispatched && completed && outcome === null
  }
}

function toVerification(raw: unknown, step: LocalExecutionInspectionStep): LocalExecutionInspectionVerification | null {
  if (!hasExactKeys(raw, VERIFICATION_KEYS)) {
    return null
  }

  const { status, outcome, exitCode, dispatchedAtUtc, completedAtUtc } = raw
  const standardOutput = toVerificationStreamDescriptor(raw.standardOutput)
  const standardError = toVerificationStreamDescriptor(raw.standardError)
  if (
    !isGuid(raw.executionId)
    || raw.executionId !== step.operationId
    || !isInt(raw.executionNumber, 1, INT32_MAX)
    || !isName(raw.commandName)
    || raw.commandName !== step.recipeName
    || !isOneOf(LOCAL_EXECUTION_VERIFICATION_STATUSES, status)
    || !(outcome === null || isOneOf(LOCAL_EXECUTION_VERIFICATION_OUTCOMES, outcome))
    || !(exitCode === null || isInt(exitCode, INT32_MIN, INT32_MAX))
    || !isUtcRoundTrip(raw.claimedAtUtc)
    || !isOptionalInstant(dispatchedAtUtc)
    || !isOptionalInstant(completedAtUtc)
    || standardOutput === null
    || standardError === null
  ) {
    return null
  }

  const accepted = {
    executionId: raw.executionId,
    executionNumber: raw.executionNumber,
    commandName: raw.commandName,
    status,
    outcome: outcome as LocalExecutionVerificationOutcome | null,
    exitCode: exitCode as number | null,
    claimedAtUtc: raw.claimedAtUtc,
    dispatchedAtUtc: dispatchedAtUtc as string | null,
    completedAtUtc: completedAtUtc as string | null,
  }
  return isCoherentVerificationShape(accepted) ? { ...accepted, standardOutput, standardError } : null
}

/**
 * Decides one selected-step answer for exactly `request`: the requested run, sequence and step, a closed status and, by status, a whole
 * shape. `NoOperation` and `Unavailable` carry no step, agent or verification (and `NoOperation` is accepted only for a step the index
 * showed without a link). `Recorded` carries exactly one of the agent header and the verification, by the step's kind: the step must be
 * the indexed one at or after its indexed state, the agent header the immutable identity of the linked attempt (its contract the one of
 * the step's kind), and the verification the exact linked execution (its name the step's recipe name) in a shape the host can record.
 */
export function toLocalExecutionStepAnswer(raw: unknown, request: LocalExecutionStepRequestFacts): LocalExecutionStepAnswer {
  if (
    !hasExactKeys(raw, STEP_ANSWER_KEYS)
    || raw.runId !== request.runId
    || raw.sequenceId !== request.sequenceId
    || raw.stepId !== request.step.stepId
  ) {
    return INCOHERENT_STEP
  }

  const { status, step: rawStep, agent, verification } = raw
  if (status === 'NoOperation' || status === 'Unavailable') {
    if (rawStep !== null || agent !== null || verification !== null) {
      return INCOHERENT_STEP
    }

    return status === 'Unavailable' ? { kind: 'unavailable' } : request.step.operationId === null ? { kind: 'no-operation' } : INCOHERENT_STEP
  }

  if (status !== 'Recorded') {
    return INCOHERENT_STEP
  }

  // The step payload carries no recipe count, so it is judged against the largest the sequence allows and then against the indexed step.
  const step = toStep(rawStep, LOCAL_EXECUTION_SEQUENCE_MAXIMUM_RECIPES)
  if (step === null || !isSameStep(step, request.step) || step.operationId === null) {
    return INCOHERENT_STEP
  }

  if (step.kind === 'Verification') {
    const accepted = agent === null ? toVerification(verification, step) : null
    return accepted === null ? INCOHERENT_STEP : { kind: 'verification', step, verification: accepted }
  }

  const header = verification === null ? toAgentHeader(agent, step) : null
  return header === null ? INCOHERENT_STEP : { kind: 'agent', step, agent: header }
}

/** What one window read asked for: the step's proved execution, the chosen stream and its Recorded descriptor, and the byte window. */
export interface LocalExecutionWindowRequestFacts {
  runId: string
  sequenceId: string
  stepId: string
  executionId: string
  stream: VerificationStream
  descriptor: RecordedVerificationStreamDescriptor
  fromOffset: number
  maxBytes: number
}

const INCOHERENT_WINDOW: VerificationWindowAnswer = { kind: 'incoherent' }

/**
 * Decides one window answer for exactly `request`: the requested run, sequence, step and canonical stream, an execution identity that is
 * either the proved execution or (for a refusal) null, a closed status and, by status, a whole shape. A non-`Ok` status carries no
 * artifact, empty text, the requested offset unchanged and a total length of 0, and nothing else of it is used. An `Ok` window names the
 * proved execution, carries the very descriptor the selection was made from, a total length equal to that descriptor's byte length, a
 * cursor with `fromOffset <= nextOffset <= totalLength` that advances by at most the requested cap and by at least one byte while bytes
 * remain, and text that is empty exactly when the cursor did not advance. The cursor counts UTF-8 bytes of the sealed file and the host
 * decodes malformed UTF-8 with replacement and defers a split code point to the next window, so the text is never re-encoded: only two
 * facts true of every UTF-8 decoding are checked (a positive advance decodes to some text, and `text.length <= advance`).
 */
export function decideLocalExecutionWindowAnswer(raw: unknown, request: LocalExecutionWindowRequestFacts): VerificationWindowAnswer {
  if (
    !hasExactKeys(raw, WINDOW_KEYS)
    || raw.runId !== request.runId
    || raw.sequenceId !== request.sequenceId
    || raw.stepId !== request.stepId
    || raw.stream !== request.stream
    || !(raw.executionId === null || raw.executionId === request.executionId)
    || typeof raw.text !== 'string'
    || !isByteCount(raw.nextOffset)
    || !isByteCount(raw.totalLength)
  ) {
    return INCOHERENT_WINDOW
  }

  const { status, artifact, text, nextOffset, totalLength, executionId } = raw
  const { fromOffset, maxBytes, descriptor } = request
  if (status === 'Ok') {
    const advance = nextOffset - fromOffset
    const served = toVerificationStreamDescriptor(artifact)
    return executionId === request.executionId
      && served !== null
      && served.status === 'Recorded'
      && served.artifactId === descriptor.artifactId
      && served.byteLength === descriptor.byteLength
      && served.captureOutcome === descriptor.captureOutcome
      && served.truncated === descriptor.truncated
      && totalLength === descriptor.byteLength
      && nextOffset >= fromOffset
      && nextOffset <= totalLength
      && advance <= maxBytes
      && !(fromOffset < totalLength && advance === 0)
      && (advance === 0) === (text === '')
      && (text as string).length <= advance
      ? { kind: 'ok', text: text as string, nextOffset, totalLength }
      : INCOHERENT_WINDOW
  }

  if (artifact !== null || text !== '' || nextOffset !== fromOffset || totalLength !== 0) {
    return INCOHERENT_WINDOW
  }

  switch (status) {
    case 'Unavailable':
      return { kind: 'unavailable' }
    case 'NotRecorded':
      // An absent artifact is described only for a proved execution; an unproved association is Unavailable.
      return executionId === request.executionId ? { kind: 'not-recorded' } : INCOHERENT_WINDOW
    case 'Missing':
      return executionId === request.executionId ? { kind: 'missing' } : INCOHERENT_WINDOW
    case 'IntegrityMismatch':
      return executionId === request.executionId ? { kind: 'integrity-mismatch' } : INCOHERENT_WINDOW
    default:
      return INCOHERENT_WINDOW
  }
}
