import { hasExactKeys, isUtcRoundTrip } from './agentAttemptOutputs'
import { toWireInstant } from './agentAttemptEvidenceWire'
import { isRevisionText } from './planningSequence'

// The closed, whole-answer decoder of the current Agent supervision read (ADR-0056). It accepts a parsed host answer only when all of it
// is coherent for exactly the run that was asked about: exactly the documented members at every level, exact types, closed vocabularies,
// canonical identifiers, UTC round-trip texts and an Int64 revision text, and every nullability relationship of the status. Anything
// else is null and is never repaired, trimmed, coerced or partially accepted. What it describes is what the host's own observer last
// recorded: never provider progress, a live child process, or permission to act. Nothing here renews a heartbeat.

export const AGENT_SUPERVISION_STATUSES = ['NoAgentAttempt', 'NotObserved', 'Observed', 'NeedsAttention', 'Ended', 'Unavailable'] as const
export type AgentSupervisionStatusName = (typeof AGENT_SUPERVISION_STATUSES)[number]

export const AGENT_SUPERVISION_PHASES = ['Preparing', 'AwaitingAdapterResult', 'RecordingResult'] as const
export type AgentSupervisionPhaseName = (typeof AGENT_SUPERVISION_PHASES)[number]

export const AGENT_SUPERVISION_ATTENTION_REASONS = [
  'stale_heartbeat',
  'phase_deadline_elapsed',
  'observer_ended_without_terminal_result',
  'host_interrupted',
] as const
export type AgentSupervisionAttentionReasonCode = (typeof AGENT_SUPERVISION_ATTENTION_REASONS)[number]

export const AGENT_SUPERVISION_CLOSURE_REASONS = ['attempt_terminal', 'observer_ended', 'host_interrupted'] as const
export type AgentSupervisionClosureReasonCode = (typeof AGENT_SUPERVISION_CLOSURE_REASONS)[number]

export const AGENT_SUPERVISION_UNAVAILABLE_REASONS = [
  'multiple_active_agent_attempts',
  'attempt_facts_incoherent',
  'supervision_facts_incoherent',
  'chronology_invalid',
] as const
export type AgentSupervisionUnavailableReasonCode = (typeof AGENT_SUPERVISION_UNAVAILABLE_REASONS)[number]

export const AGENT_SUPERVISION_NEXT_ACTIONS = [
  'no_agent_attempt_recorded',
  'inspect_the_recorded_attempt',
  'wait_and_refresh',
  'inspect_and_reconcile_before_further_work',
  'review_the_recorded_result',
] as const
export type AgentSupervisionNextActionCode = (typeof AGENT_SUPERVISION_NEXT_ACTIONS)[number]

const ATTEMPT_STATUSES = ['Running', 'Completed', 'Failed', 'Interrupted'] as const
export type AgentSupervisionAttemptStatus = (typeof ATTEMPT_STATUSES)[number]

const PROVIDERS = ['Codex', 'ClaudeCode'] as const
export type AgentSupervisionProvider = (typeof PROVIDERS)[number]

// The seven existing response contracts and the one role each belongs to (ADR-0009).
const ROLE_BY_CONTRACT = {
  Proposal: 'Planner',
  CriticalReview: 'CriticalReviewer',
  ChallengeResolution: 'Resolver',
  ImplementationReport: 'Implementer',
  ImplementationReview: 'CodeReviewer',
  ReviewCorrection: 'Implementer',
  VerificationDiagnosis: 'CodeReviewer',
} as const
export type AgentSupervisionContract = keyof typeof ROLE_BY_CONTRACT
export type AgentSupervisionRole = (typeof ROLE_BY_CONTRACT)[AgentSupervisionContract]

/** The heartbeat is stale after exactly thirty seconds, and the boundary itself is expired (ADR-0056). */
export const AGENT_SUPERVISION_STALE_AFTER_TICKS = 30n * 10_000_000n

export interface AgentSupervisionAttempt {
  attemptId: string
  attemptNumber: number
  role: AgentSupervisionRole
  provider: AgentSupervisionProvider
  responseContract: AgentSupervisionContract
  /** The real recorded status; it, never the observation, says whether the attempt is terminal. */
  status: AgentSupervisionAttemptStatus
  outcome: string | null
  /** Whether the single-use dispatch marker exists: bookkeeping, never proof that a provider process is running. */
  dispatched: boolean
  claimedAtUtc: string
  dispatchedAtUtc: string | null
  completedAtUtc: string | null
}

export interface AgentSupervisionObservation {
  hostInstanceId: string
  observerId: string
  /** False for the observation of another host launch, whose freshness is never accepted. */
  ownedByCurrentHost: boolean
  /** The canonical Int64 text exactly as written; never converted to a number. */
  revision: string
  phase: AgentSupervisionPhaseName
  startedAtUtc: string
  /** The host observer's last committed report. Only the host's own observation loop reported it. */
  lastHeartbeatAtUtc: string
  phaseEnteredAtUtc: string
  /** The absolute deadline of the phase, fixed at phase entry; a heartbeat never moves it. */
  phaseDeadlineUtc: string
  open: boolean
  closureReason: AgentSupervisionClosureReasonCode | null
  closedAtUtc: string | null
}

export interface AgentSupervisionAttention {
  effectiveReasonCode: AgentSupervisionAttentionReasonCode
  effectiveReasonText: string
  effectiveAtUtc: string
  /** Present only when the same attention is already durable; absent means derived by this read and not yet recorded. */
  recordedReasonCode: AgentSupervisionAttentionReasonCode | null
  recordedAtUtc: string | null
}

export interface AgentSupervisionReading {
  runId: string
  status: AgentSupervisionStatusName
  reasonCode: AgentSupervisionAttentionReasonCode | AgentSupervisionUnavailableReasonCode | null
  reasonText: string | null
  nextActionCode: AgentSupervisionNextActionCode
  nextActionText: string
  attempt: AgentSupervisionAttempt | null
  observation: AgentSupervisionObservation | null
  attention: AgentSupervisionAttention | null
  /** The host clock of this read, a UTC round-trip text. */
  observedAtUtc: string
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const OUTCOME_NAME = /^[A-Za-z][A-Za-z0-9]{0,63}$/
const UNSAFE_TEXT = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u
const MAX_HOST_TEXT = 400
const TOP_KEYS = ['attempt', 'attention', 'nextActionCode', 'nextActionText', 'observation', 'observedAtUtc', 'reasonCode', 'reasonText', 'runId', 'status']
const ATTEMPT_KEYS = [
  'attemptId',
  'attemptNumber',
  'claimedAtUtc',
  'completedAtUtc',
  'dispatched',
  'dispatchedAtUtc',
  'outcome',
  'provider',
  'responseContract',
  'role',
  'status',
]
const OBSERVATION_KEYS = [
  'closedAtUtc',
  'closureReason',
  'hostInstanceId',
  'lastHeartbeatAtUtc',
  'observerId',
  'open',
  'ownedByCurrentHost',
  'phase',
  'phaseDeadlineUtc',
  'phaseEnteredAtUtc',
  'revision',
  'startedAtUtc',
]
const ATTENTION_KEYS = ['effectiveAtUtc', 'effectiveReasonCode', 'effectiveReasonText', 'recordedAtUtc', 'recordedReasonCode']

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T => typeof value === 'string' && (values as readonly string[]).includes(value)
const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'
const isHostText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_HOST_TEXT && !UNSAFE_TEXT.test(value)

/**
 * The UTC round-trip text as 100-nanosecond ticks since the epoch of its whole seconds plus its seven fractional digits, so that an exact
 * boundary (thirty seconds, an exact deadline) is judged without a millisecond truncation. Null for anything that is not a valid text.
 */
export function utcTicks(text: string): bigint | null {
  if (!isUtcRoundTrip(text)) {
    return null
  }

  const wholeSeconds = toWireInstant(`${text.slice(0, 19)}.0000000Z`)
  if (wholeSeconds === null) {
    return null
  }

  return BigInt(wholeSeconds.getTime()) * 10_000n + BigInt(text.slice(20, 27))
}

const isTicks = (value: unknown): value is string => typeof value === 'string' && utcTicks(value) !== null
const ticksOf = (text: string): bigint => utcTicks(text) as bigint

const isAbsentOr = <T>(value: unknown, accepts: (candidate: unknown) => candidate is T): value is T | null => value === null || accepts(value)

function toAttempt(raw: unknown): AgentSupervisionAttempt | null {
  if (!hasExactKeys(raw, ATTEMPT_KEYS)) {
    return null
  }

  const { attemptNumber, responseContract, role, provider, status, outcome } = raw
  if (
    !isGuid(raw.attemptId)
    || typeof attemptNumber !== 'number'
    || !Number.isInteger(attemptNumber)
    || attemptNumber < 1
    || !isOneOf(Object.keys(ROLE_BY_CONTRACT) as AgentSupervisionContract[], responseContract)
    || role !== ROLE_BY_CONTRACT[responseContract]
    || !isOneOf(PROVIDERS, provider)
    || !isOneOf(ATTEMPT_STATUSES, status)
    || !isAbsentOr(outcome, (value): value is string => typeof value === 'string' && OUTCOME_NAME.test(value))
    || !isBoolean(raw.dispatched)
    || !isTicks(raw.claimedAtUtc)
    || !isAbsentOr(raw.dispatchedAtUtc, isTicks)
    || !isAbsentOr(raw.completedAtUtc, isTicks)
  ) {
    return null
  }

  // The marker and its time exist together; a running attempt has neither outcome nor completion, a terminal one has a completion.
  if ((raw.dispatched ? raw.dispatchedAtUtc === null : raw.dispatchedAtUtc !== null)) {
    return null
  }

  if (status === 'Running' ? outcome !== null || raw.completedAtUtc !== null : raw.completedAtUtc === null) {
    return null
  }

  // Claim, dispatch and completion are ordered.
  const claimed = ticksOf(raw.claimedAtUtc)
  if (
    (raw.dispatchedAtUtc !== null && ticksOf(raw.dispatchedAtUtc) < claimed)
    || (raw.completedAtUtc !== null && ticksOf(raw.completedAtUtc) < claimed)
    || (raw.dispatchedAtUtc !== null && raw.completedAtUtc !== null && ticksOf(raw.completedAtUtc) < ticksOf(raw.dispatchedAtUtc))
  ) {
    return null
  }

  return {
    attemptId: raw.attemptId,
    attemptNumber,
    role: role as AgentSupervisionRole,
    provider,
    responseContract,
    status,
    outcome,
    dispatched: raw.dispatched,
    claimedAtUtc: raw.claimedAtUtc,
    dispatchedAtUtc: raw.dispatchedAtUtc,
    completedAtUtc: raw.completedAtUtc,
  }
}

function toObservation(raw: unknown): AgentSupervisionObservation | null {
  if (!hasExactKeys(raw, OBSERVATION_KEYS)) {
    return null
  }

  if (
    !isGuid(raw.hostInstanceId)
    || !isGuid(raw.observerId)
    || !isBoolean(raw.ownedByCurrentHost)
    || !isRevisionText(raw.revision)
    || !isOneOf(AGENT_SUPERVISION_PHASES, raw.phase)
    || !isTicks(raw.startedAtUtc)
    || !isTicks(raw.lastHeartbeatAtUtc)
    || !isTicks(raw.phaseEnteredAtUtc)
    || !isTicks(raw.phaseDeadlineUtc)
    || !isBoolean(raw.open)
    || !isAbsentOr(raw.closureReason, (value): value is AgentSupervisionClosureReasonCode => isOneOf(AGENT_SUPERVISION_CLOSURE_REASONS, value))
    || !isAbsentOr(raw.closedAtUtc, isTicks)
  ) {
    return null
  }

  // Open means neither closure fact; closed means both. Times are ordered: start, phase entry, last report, and the deadline after entry.
  const closed = raw.closureReason !== null || raw.closedAtUtc !== null
  if (raw.open === closed || (closed && (raw.closureReason === null || raw.closedAtUtc === null))) {
    return null
  }

  const started = ticksOf(raw.startedAtUtc)
  const entered = ticksOf(raw.phaseEnteredAtUtc)
  const heartbeat = ticksOf(raw.lastHeartbeatAtUtc)
  if (
    entered < started
    || heartbeat < entered
    || ticksOf(raw.phaseDeadlineUtc) <= entered
    || (raw.closedAtUtc !== null && ticksOf(raw.closedAtUtc) < heartbeat)
  ) {
    return null
  }

  return {
    hostInstanceId: raw.hostInstanceId,
    observerId: raw.observerId,
    ownedByCurrentHost: raw.ownedByCurrentHost,
    revision: raw.revision,
    phase: raw.phase,
    startedAtUtc: raw.startedAtUtc,
    lastHeartbeatAtUtc: raw.lastHeartbeatAtUtc,
    phaseEnteredAtUtc: raw.phaseEnteredAtUtc,
    phaseDeadlineUtc: raw.phaseDeadlineUtc,
    open: raw.open,
    closureReason: raw.closureReason,
    closedAtUtc: raw.closedAtUtc,
  }
}

function toAttention(raw: unknown): AgentSupervisionAttention | null {
  if (!hasExactKeys(raw, ATTENTION_KEYS)) {
    return null
  }

  if (
    !isOneOf(AGENT_SUPERVISION_ATTENTION_REASONS, raw.effectiveReasonCode)
    || !isHostText(raw.effectiveReasonText)
    || !isTicks(raw.effectiveAtUtc)
    || !isAbsentOr(raw.recordedReasonCode, (value): value is AgentSupervisionAttentionReasonCode => isOneOf(AGENT_SUPERVISION_ATTENTION_REASONS, value))
    || !isAbsentOr(raw.recordedAtUtc, isTicks)
  ) {
    return null
  }

  // A recorded attention is exactly the effective one (the first attention is retained and never replaced); both facts or neither.
  const recorded = raw.recordedReasonCode !== null || raw.recordedAtUtc !== null
  if (recorded && (raw.recordedReasonCode !== raw.effectiveReasonCode || raw.recordedAtUtc !== raw.effectiveAtUtc)) {
    return null
  }

  return {
    effectiveReasonCode: raw.effectiveReasonCode,
    effectiveReasonText: raw.effectiveReasonText,
    effectiveAtUtc: raw.effectiveAtUtc,
    recordedReasonCode: raw.recordedReasonCode,
    recordedAtUtc: raw.recordedAtUtc,
  }
}

/** The whole host answer for exactly `runId`, or null unless every member and every relationship of the status is coherent. */
export function toAgentSupervisionReading(raw: unknown, runId: string): AgentSupervisionReading | null {
  if (!hasExactKeys(raw, TOP_KEYS)) {
    return null
  }

  if (
    raw.runId !== runId
    || !isOneOf(AGENT_SUPERVISION_STATUSES, raw.status)
    || !isOneOf(AGENT_SUPERVISION_NEXT_ACTIONS, raw.nextActionCode)
    || !isHostText(raw.nextActionText)
    || !isTicks(raw.observedAtUtc)
    || !isAbsentOr(raw.reasonCode, (value): value is string => typeof value === 'string')
    || !isAbsentOr(raw.reasonText, isHostText)
    || (raw.reasonCode === null) !== (raw.reasonText === null)
  ) {
    return null
  }

  const attempt = raw.attempt === null ? null : toAttempt(raw.attempt)
  const observation = raw.observation === null ? null : toObservation(raw.observation)
  const attention = raw.attention === null ? null : toAttention(raw.attention)
  if ((raw.attempt !== null && attempt === null) || (raw.observation !== null && observation === null) || (raw.attention !== null && attention === null)) {
    return null
  }

  const reading: AgentSupervisionReading = {
    runId,
    status: raw.status,
    reasonCode: raw.reasonCode as AgentSupervisionReading['reasonCode'],
    reasonText: raw.reasonText,
    nextActionCode: raw.nextActionCode,
    nextActionText: raw.nextActionText,
    attempt,
    observation,
    attention,
    observedAtUtc: raw.observedAtUtc,
  }

  return hasCoherentRelationships(reading) && hasNoFactAfterTheRead(reading) ? reading : null
}

/**
 * A successful answer (Observed, NeedsAttention, Ended) is judged at its own host clock, so none of its recorded facts can lie after it:
 * not the observation's start, phase entry, last report or closure, not a recorded or effective attention, and not the attempt's
 * completion. The host refuses such an impossible chronology as Unavailable (for example after a clock rollback); an answer that claims
 * otherwise is not coherent. The exact host clock itself is valid. An Unavailable answer claims nothing current and is shown as read.
 */
function hasNoFactAfterTheRead(reading: AgentSupervisionReading): boolean {
  if (reading.status !== 'Observed' && reading.status !== 'NeedsAttention' && reading.status !== 'Ended') {
    return true
  }

  const observed = ticksOf(reading.observedAtUtc)
  const { attempt, observation, attention } = reading
  const facts = [
    observation?.startedAtUtc,
    observation?.phaseEnteredAtUtc,
    observation?.lastHeartbeatAtUtc,
    observation?.closedAtUtc,
    attention?.effectiveAtUtc,
    attention?.recordedAtUtc,
    attempt?.completedAtUtc,
  ]
  return facts.every((fact) => fact === null || fact === undefined || ticksOf(fact) <= observed)
}

function hasCoherentRelationships(reading: AgentSupervisionReading): boolean {
  const { status, attempt, observation, attention, reasonCode, nextActionCode } = reading
  const terminal = attempt !== null && attempt.status !== 'Running'
  const reasonIsAttention = isOneOf(AGENT_SUPERVISION_ATTENTION_REASONS, reasonCode)
  const reasonIsUnavailable = isOneOf(AGENT_SUPERVISION_UNAVAILABLE_REASONS, reasonCode)

  switch (status) {
    case 'NoAgentAttempt':
      return attempt === null && observation === null && attention === null && reasonCode === null && nextActionCode === 'no_agent_attempt_recorded'

    case 'NotObserved':
      return attempt !== null
        && observation === null
        && attention === null
        && reasonCode === null
        && nextActionCode === (terminal ? 'review_the_recorded_result' : 'inspect_the_recorded_attempt')

    case 'Observed':
      return attempt !== null
        && !terminal
        && observation !== null
        && observation.open
        && observation.ownedByCurrentHost
        && attention === null
        && reasonCode === null
        && nextActionCode === 'wait_and_refresh'
        && isFreshAtReading(reading, observation)

    case 'NeedsAttention':
      return attempt !== null
        && !terminal
        && observation !== null
        && attention !== null
        && reasonIsAttention
        && reasonCode === attention.effectiveReasonCode
        && nextActionCode === 'inspect_and_reconcile_before_further_work'
        // A closed observation of a running attempt always carries an attention that was recorded, never merely derived.
        && (observation.open || attention.recordedReasonCode !== null)

    case 'Ended':
      return attempt !== null
        && terminal
        && observation !== null
        && reasonCode === null
        && nextActionCode === 'review_the_recorded_result'

    case 'Unavailable':
      return reasonIsUnavailable
        && attention === null
        && nextActionCode === 'inspect_and_reconcile_before_further_work'
        // An unavailable read never claims a fresh heartbeat: whatever coherent facts it carries are shown as read, nothing more.
        && (observation === null || attempt !== null)

    default:
      return false
  }
}

/** The host judged this observation fresh and within its deadline at its own clock; a disagreement is not a coherent answer. */
function isFreshAtReading(reading: AgentSupervisionReading, observation: AgentSupervisionObservation): boolean {
  const observed = ticksOf(reading.observedAtUtc)
  const heartbeat = ticksOf(observation.lastHeartbeatAtUtc)
  return heartbeat <= observed && observed - heartbeat < AGENT_SUPERVISION_STALE_AFTER_TICKS && observed < ticksOf(observation.phaseDeadlineUtc)
}

/** Whole seconds between the host observer's last report and the host clock of the read (the host's own measurement, never the browser's). */
export function reportAgeSeconds(observation: AgentSupervisionObservation, observedAtUtc: string): number {
  const age = ticksOf(observedAtUtc) - ticksOf(observation.lastHeartbeatAtUtc)
  return age <= 0n ? 0 : Number(age / 10_000_000n)
}

/** Whole seconds from the host clock of the read until the phase deadline; negative once it elapsed. */
export function secondsToDeadline(observation: AgentSupervisionObservation, observedAtUtc: string): number {
  const remaining = ticksOf(observation.phaseDeadlineUtc) - ticksOf(observedAtUtc)
  return Number(remaining / 10_000_000n)
}

/** Whether the attempt of this reading can still produce a different answer: nothing terminal and no absence of an attempt. */
export function isAgentSupervisionWatching(reading: AgentSupervisionReading): boolean {
  return reading.status !== 'NoAgentAttempt' && !(reading.attempt !== null && reading.attempt.status !== 'Running')
}

/** UTC text shortened for display: `2026-10-19 09:00:00 UTC`. */
export const formatSupervisionTime = (text: string): string => `${text.slice(0, 10)} ${text.slice(11, 19)} UTC`

export const SUPERVISION_PHASE_LABELS: Readonly<Record<AgentSupervisionPhaseName, string>> = {
  Preparing: 'Preparing: the host is checking Git and account evidence before any provider call',
  AwaitingAdapterResult: 'Awaiting the adapter result: the host called the provider adapter; this does not show a process started',
  RecordingResult: 'Recording the result: the adapter call returned or none was made; the host is recording it',
}

export const SUPERVISION_CLOSURE_LABELS: Readonly<Record<AgentSupervisionClosureReasonCode, string>> = {
  attempt_terminal: 'closed after the attempt reached its recorded result',
  observer_ended: 'closed because the observer ended without a recorded terminal result',
  host_interrupted: 'closed because the host that observed it is no longer running',
}
