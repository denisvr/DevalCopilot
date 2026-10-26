import { toVerifiedIdentity } from './agentAttemptHistory'
import type { AgentAttemptIdentityExpectation, AgentAttemptResponseContract } from './agentAttemptHistory'
import { hasExactKeys, isInt, isUtcRoundTrip } from './agentAttemptVerification'
import { isLocalRevisionText } from './localExecutionSequence'
import {
  PLANNING_SEQUENCE_MAXIMUM_STEPS,
  PLANNING_SEQUENCE_REASON_CODES,
  PLANNING_SEQUENCE_STATES,
  PLANNING_SEQUENCE_STEP_STATES,
  isPlanningSequenceLive,
} from './planningSequence'
import type {
  PlanningSequenceProvider,
  PlanningSequenceReasonCode,
  PlanningSequenceRole,
  PlanningSequenceState,
  PlanningSequenceStepState,
} from './planningSequence'

// The closed, whole-answer decoders of the two recorded planning sequence inspection operations (ADR-0068). Each accepts a parsed host
// answer only when all of it is coherent for exactly the run, sequence and step that was asked about: exactly the documented members at
// every level, exact types, closed vocabularies, canonical identifiers, times and Int64 texts, and the structural rules the host itself
// proves. Anything else is null (or `incoherent`) and is never repaired, trimmed, coerced into a known state or partially accepted. A
// recorded link, claim or state is bookkeeping: never proof of an outcome, acceptance, a dispatch or a running process.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const isOptionalGuid = (value: unknown): value is string | null => value === null || isGuid(value)
const isOptionalInstant = (value: unknown): value is string | null => value === null || isUtcRoundTrip(value)
const isRevision = (value: unknown): value is string => isLocalRevisionText(value) && BigInt(value) <= BigInt(Number.MAX_SAFE_INTEGER)

// The fixed ADR-0055 progression: the role, provider and response contract each ordinal is owned by.
const ROLES: readonly PlanningSequenceRole[] = ['Planner', 'CriticalReviewer', 'Resolver', 'CriticalReviewer', 'Resolver']
const PROVIDERS: readonly PlanningSequenceProvider[] = ['Codex', 'ClaudeCode', 'Codex', 'ClaudeCode', 'Codex']
const CONTRACTS: readonly AgentAttemptResponseContract[] = [
  'Proposal',
  'CriticalReview',
  'ChallengeResolution',
  'CriticalReview',
  'ChallengeResolution',
]

/** The response contract the step of this ordinal is claimed under, or null for an ordinal outside the five. */
export const planningInspectionContractOf = (ordinal: number): AgentAttemptResponseContract | null => CONTRACTS[ordinal] ?? null

/** The stored bookkeeping of the recorded sequence: never effective live control, an acceptance or a commit proof. */
export interface PlanningInspectionHeader {
  sequenceId: string
  workspaceId: string
  checkpointId: string
  fingerprintSha256: string
  recordedState: PlanningSequenceState
  reasonCode: PlanningSequenceReasonCode | null
  /** A canonical positive Int64 text of at most 2^53 - 1. */
  revision: string
  /** The validated invariant UTC round-trip texts, exactly as the host wrote them. */
  armedAtUtc: string
  updatedAtUtc: string
  stopRequestedAtUtc: string | null
  endedAtUtc: string | null
}

/** One recorded step. `attemptId` is a recorded link, not proof of the attempt, a dispatch or any outcome. */
export interface PlanningInspectionStep {
  stepId: string
  ordinal: number
  role: PlanningSequenceRole
  provider: PlanningSequenceProvider
  responseContract: AgentAttemptResponseContract
  recordedState: PlanningSequenceStepState
  reasonCode: PlanningSequenceReasonCode | null
  revision: string
  predecessorAttemptId: string | null
  inputProposalMessageId: string | null
  attemptId: string | null
  preparationReservedAtUtc: string
  preparationDeadlineUtc: string
  updatedAtUtc: string
}

export type PlanningInspectionIndex =
  // A known run with no recorded sequence. This is not "no planning": nothing else is claimed.
  | { status: 'NotRecorded' }
  // A sequence is present but its structure could not be proved or changed during the read; none of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; sequence: PlanningInspectionHeader; steps: readonly PlanningInspectionStep[] }

const INDEX_KEYS = ['runId', 'status', 'sequence', 'steps'] as const
const HEADER_KEYS = [
  'sequenceId',
  'workspaceId',
  'checkpointId',
  'fingerprintSha256',
  'recordedState',
  'reasonCode',
  'revision',
  'armedAtUtc',
  'updatedAtUtc',
  'stopRequestedAtUtc',
  'endedAtUtc',
] as const
const STEP_KEYS = [
  'stepId',
  'ordinal',
  'role',
  'provider',
  'responseContract',
  'recordedState',
  'reasonCode',
  'revision',
  'predecessorAttemptId',
  'inputProposalMessageId',
  'attemptId',
  'preparationReservedAtUtc',
  'preparationDeadlineUtc',
  'updatedAtUtc',
] as const
const STEP_ANSWER_KEYS = ['runId', 'sequenceId', 'stepId', 'status', 'step', 'agent'] as const
const AGENT_KEYS = ['id', 'attemptNumber', 'role', 'provider', 'responseContract'] as const

// The canonical times are fixed-width, so their text orders exactly like their instants and (unlike the millisecond instant parser)
// at tick precision.
function toHeader(raw: unknown): PlanningInspectionHeader | null {
  if (!hasExactKeys(raw, HEADER_KEYS)) {
    return null
  }

  const { recordedState, reasonCode, armedAtUtc, updatedAtUtc, stopRequestedAtUtc, endedAtUtc } = raw
  if (
    !isGuid(raw.sequenceId)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isSha256(raw.fingerprintSha256)
    || !isOneOf(PLANNING_SEQUENCE_STATES, recordedState)
    || !isRevision(raw.revision)
    || !isUtcRoundTrip(armedAtUtc)
    || !isUtcRoundTrip(updatedAtUtc)
    || updatedAtUtc < armedAtUtc
    || !isOptionalInstant(stopRequestedAtUtc)
    || !isOptionalInstant(endedAtUtc)
  ) {
    return null
  }

  // An ended sequence has a fixed reason and an end time within its lifetime; a live one, Stopping included, has neither. A stop request
  // exists only for Stopping and an ended sequence, and always for Stopping and Stopped.
  const live = isPlanningSequenceLive(recordedState)
  if (live) {
    if (reasonCode !== null || endedAtUtc !== null) {
      return null
    }
  } else if (!isOneOf(PLANNING_SEQUENCE_REASON_CODES, reasonCode) || endedAtUtc === null || endedAtUtc < armedAtUtc || endedAtUtc > updatedAtUtc) {
    return null
  }

  const stopAllowed = !live || recordedState === 'Stopping'
  const stopRequired = recordedState === 'Stopping' || recordedState === 'Stopped'
  if (
    (stopRequestedAtUtc !== null && (!stopAllowed || stopRequestedAtUtc < armedAtUtc || stopRequestedAtUtc > updatedAtUtc))
    || (stopRequired && stopRequestedAtUtc === null)
  ) {
    return null
  }

  return {
    sequenceId: raw.sequenceId,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    fingerprintSha256: raw.fingerprintSha256,
    recordedState,
    reasonCode: live ? null : (reasonCode as PlanningSequenceReasonCode),
    revision: raw.revision,
    armedAtUtc,
    updatedAtUtc,
    stopRequestedAtUtc,
    endedAtUtc,
  }
}

/**
 * Decodes one step member as the index and the selected-step answer both carry it. The shape rules are the host's: a role, provider and
 * contract the ordinal owns, a positive Int64 revision, preparation times in order, only the Planner without a predecessor and input
 * Proposal, a recorded attempt link exactly for a Claimed or Concluded step and a fixed reason exactly for a Revoked or Unresolved one.
 */
function toStep(raw: unknown): PlanningInspectionStep | null {
  if (!hasExactKeys(raw, STEP_KEYS)) {
    return null
  }

  const { ordinal, recordedState, reasonCode, predecessorAttemptId, inputProposalMessageId, attemptId } = raw
  if (
    !isGuid(raw.stepId)
    || !isInt(ordinal, 0, PLANNING_SEQUENCE_MAXIMUM_STEPS - 1)
    || raw.role !== ROLES[ordinal]
    || raw.provider !== PROVIDERS[ordinal]
    || raw.responseContract !== CONTRACTS[ordinal]
    || !isOneOf(PLANNING_SEQUENCE_STEP_STATES, recordedState)
    || !isRevision(raw.revision)
    || !isOptionalGuid(predecessorAttemptId)
    || !isOptionalGuid(inputProposalMessageId)
    || !isOptionalGuid(attemptId)
    || !isUtcRoundTrip(raw.preparationReservedAtUtc)
    || !isUtcRoundTrip(raw.preparationDeadlineUtc)
    || !isUtcRoundTrip(raw.updatedAtUtc)
    || raw.preparationDeadlineUtc <= raw.preparationReservedAtUtc
    || raw.updatedAtUtc < raw.preparationReservedAtUtc
    // Only the Planner has no predecessor and no input Proposal.
    || (ordinal === 0) !== (predecessorAttemptId === null)
    || (ordinal === 0) !== (inputProposalMessageId === null)
  ) {
    return null
  }

  const linked = recordedState === 'Claimed' || recordedState === 'Concluded'
  const reasoned = recordedState === 'Revoked' || recordedState === 'Unresolved'
  if (linked !== (attemptId !== null) || (reasoned ? !isOneOf(PLANNING_SEQUENCE_REASON_CODES, reasonCode) : reasonCode !== null)) {
    return null
  }

  return {
    stepId: raw.stepId,
    ordinal,
    role: raw.role as PlanningSequenceRole,
    provider: raw.provider as PlanningSequenceProvider,
    responseContract: raw.responseContract as AgentAttemptResponseContract,
    recordedState,
    reasonCode: reasoned ? (reasonCode as PlanningSequenceReasonCode) : null,
    revision: raw.revision,
    predecessorAttemptId,
    inputProposalMessageId,
    attemptId,
    preparationReservedAtUtc: raw.preparationReservedAtUtc,
    preparationDeadlineUtc: raw.preparationDeadlineUtc,
    updatedAtUtc: raw.updatedAtUtc,
  }
}

/** The complete step list as one coherent recorded sequence: consecutive ordinals, unique identities and the host's structural rules. */
function toSteps(raw: unknown[]): PlanningInspectionStep[] | null {
  if (raw.length > PLANNING_SEQUENCE_MAXIMUM_STEPS) {
    return null
  }

  const stepIds = new Set<string>()
  const attemptIds = new Set<string>()
  const steps: PlanningInspectionStep[] = []
  for (const [position, item] of raw.entries()) {
    const step = toStep(item)
    const previous = position === 0 ? null : steps[position - 1]
    if (
      step === null
      || step.ordinal !== position
      || stepIds.has(step.stepId)
      || (step.attemptId !== null && attemptIds.has(step.attemptId))
      // Only the last step can still be outstanding, revoked or unresolved: every earlier one concluded before its successor existed.
      || (position < raw.length - 1 && step.recordedState !== 'Concluded')
      // Every later step names exactly the immediately preceding step's linked attempt, and a Resolver names its review's input Proposal.
      || (previous !== null && step.predecessorAttemptId !== previous.attemptId)
      || (previous !== null && step.role === 'Resolver' && step.inputProposalMessageId !== previous.inputProposalMessageId)
    ) {
      return null
    }

    stepIds.add(step.stepId)
    if (step.attemptId !== null) {
      attemptIds.add(step.attemptId)
    }

    steps.push(step)
  }

  return steps
}

/**
 * Accepts a parsed index answer only when the whole of it is coherent for exactly `expectedRunId`: closed shapes at every level, the
 * requested run, a closed status (`NotRecorded` and `Unavailable` carry a null header and an empty list; `Recorded` carries the stored
 * bookkeeping header and the complete list of at most five steps at their own consecutive ordinals). Anything else is null and is never
 * repaired, trimmed or partially accepted.
 */
export function toPlanningInspectionIndex(raw: unknown, expectedRunId: string): PlanningInspectionIndex | null {
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
  const accepted = header === null ? null : toSteps(steps)
  return header === null || accepted === null ? null : { status, sequence: header, steps: accepted }
}

/** What the selected-step read asked about: the run and sequence, and the step as the accepted index showed it. */
export interface PlanningStepRequestFacts {
  runId: string
  sequenceId: string
  step: PlanningInspectionStep
}

export type PlanningStepAnswer =
  | { kind: 'agent'; step: PlanningInspectionStep; agent: AgentAttemptIdentityExpectation }
  // The coherent Reserved, Revoked or Unresolved no-link shape: no link is recorded, which proves nothing about preparation or execution.
  | { kind: 'no-linked-attempt' }
  | { kind: 'unavailable' }
  | { kind: 'incoherent' }

const INCOHERENT_STEP: PlanningStepAnswer = { kind: 'incoherent' }

const FORWARD: Readonly<Record<PlanningSequenceStepState, readonly PlanningSequenceStepState[]>> = {
  Reserved: ['Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved'],
  Claimed: ['Claimed', 'Concluded'],
  Concluded: ['Concluded'],
  Revoked: ['Revoked'],
  Unresolved: ['Unresolved'],
}

/**
 * Whether a step read now is the very step the index showed: every immutable fact equal, its state at or after the indexed one, its
 * revision and update time not earlier (a step legitimately advances between two reads) and an established link or reason never changing
 * or disappearing.
 */
function isSameStep(read: PlanningInspectionStep, indexed: PlanningInspectionStep): boolean {
  return read.stepId === indexed.stepId
    && read.ordinal === indexed.ordinal
    && read.role === indexed.role
    && read.provider === indexed.provider
    && read.responseContract === indexed.responseContract
    && read.predecessorAttemptId === indexed.predecessorAttemptId
    && read.inputProposalMessageId === indexed.inputProposalMessageId
    && read.preparationReservedAtUtc === indexed.preparationReservedAtUtc
    && read.preparationDeadlineUtc === indexed.preparationDeadlineUtc
    && FORWARD[indexed.recordedState].includes(read.recordedState)
    && BigInt(read.revision) >= BigInt(indexed.revision)
    && read.updatedAtUtc >= indexed.updatedAtUtc
    && (indexed.attemptId === null || read.attemptId === indexed.attemptId)
    && (indexed.reasonCode === null || read.reasonCode === indexed.reasonCode)
}

function toAgentHeader(raw: unknown, step: PlanningInspectionStep): AgentAttemptIdentityExpectation | null {
  if (!hasExactKeys(raw, AGENT_KEYS) || !isGuid(raw.id) || raw.id !== step.attemptId || !isInt(raw.attemptNumber, 1, Number.MAX_SAFE_INTEGER)) {
    return null
  }

  const identity = toVerifiedIdentity(raw.role, raw.provider, raw.responseContract)
  return identity !== null
    && identity.role === step.role
    && identity.provider === step.provider
    && identity.responseContract === step.responseContract
    ? { attemptId: raw.id, attemptNumber: raw.attemptNumber, ...identity }
    : null
}

/**
 * Decides one selected-step answer for exactly `request`: the requested run, sequence and step, a closed status and, by status, a whole
 * shape. `NoLinkedAttempt` and `Unavailable` carry no step and no agent (and `NoLinkedAttempt` is accepted only for a step the index
 * showed without a link, since an established link never disappears). `Recorded` carries the very indexed step at or after its indexed
 * state, with a recorded link, and the immutable identity header of exactly that linked attempt in the step's fixed role, provider and
 * response contract.
 */
export function toPlanningStepAnswer(raw: unknown, request: PlanningStepRequestFacts): PlanningStepAnswer {
  if (
    !hasExactKeys(raw, STEP_ANSWER_KEYS)
    || raw.runId !== request.runId
    || raw.sequenceId !== request.sequenceId
    || raw.stepId !== request.step.stepId
  ) {
    return INCOHERENT_STEP
  }

  const { status, step: rawStep, agent } = raw
  if (status === 'NoLinkedAttempt' || status === 'Unavailable') {
    if (rawStep !== null || agent !== null) {
      return INCOHERENT_STEP
    }

    return status === 'Unavailable'
      ? { kind: 'unavailable' }
      : request.step.attemptId === null
        ? { kind: 'no-linked-attempt' }
        : INCOHERENT_STEP
  }

  if (status !== 'Recorded') {
    return INCOHERENT_STEP
  }

  const step = toStep(rawStep)
  if (step === null || !isSameStep(step, request.step) || step.attemptId === null) {
    return INCOHERENT_STEP
  }

  const header = toAgentHeader(agent, step)
  return header === null ? INCOHERENT_STEP : { kind: 'agent', step, agent: header }
}
