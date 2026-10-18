import type { AgentAttemptEvidenceResponse } from '../../api/clients'
import type { ProcessEvidenceView } from './describeProcessEvidence'
import type { TokenUsageView } from './describeTokenUsage'

/** A recorded instant: present and valid, truthfully absent, or present but not a valid instant (or contradicting the status). */
export type AttemptInstantFact = { status: 'recorded'; at: Date } | { status: 'absent' } | { status: 'unavailable' }

/**
 * A measured or provider-reported fact of one attempt. `not-dispatched` and `running` come first and are decisive: such an attempt has no
 * measured result or known usage, whatever its answer carries. `unknown` is a concluded attempt with nothing recorded (never zero);
 * `unavailable` is a value that is malformed or contradictory, or a dispatch time that cannot be read, so nothing of it is shown.
 */
export type AttemptMeasuredFact<T> =
  | { status: 'not-dispatched' }
  | { status: 'running' }
  | { status: 'unknown' }
  | { status: 'known'; value: T }
  | { status: 'unavailable' }

/** The configured timeout is set at claim, independent of dispatch and of measured evidence. */
export type AttemptTimeoutFact = { status: 'known'; milliseconds: number } | { status: 'unknown' } | { status: 'unavailable' }

export interface AgentAttemptExecutionFacts {
  claimed: AttemptInstantFact
  dispatched: AttemptInstantFact
  completed: AttemptInstantFact
  process: AttemptMeasuredFact<ProcessEvidenceView>
  timeout: AttemptTimeoutFact
  tokenUsage: AttemptMeasuredFact<TokenUsageView>
}

const INT32_MIN = -2_147_483_648
const INT32_MAX = 2_147_483_647
const PROCESS_OUTCOMES: readonly string[] = ['Exited', 'TimedOut', 'Cancelled']

const isAbsent = (value: unknown): value is null | undefined => value === null || value === undefined
const isInt32 = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= INT32_MIN && value <= INT32_MAX
const isCount = (value: unknown): value is number => isInt32(value) && value >= 0
const isWholeMilliseconds = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)

/**
 * Only a valid `Date` is an instant; a string, number or any other value is never parsed here. A `Date` check alone cannot prove the
 * wire value was a timestamp, because the generated client coerces any truthy value with `new Date(String(value))`: the evidence
 * operation's facade (`AgentAttemptEvidenceWireClient`) judges the raw text first and hands over an invalid `Date` for a present
 * malformed value, so it stays unavailable here and never becomes absent or plausible.
 */
function toInstantFact(value: unknown): AttemptInstantFact {
  if (isAbsent(value)) {
    return { status: 'absent' }
  }

  return value instanceof Date && !Number.isNaN(value.getTime()) ? { status: 'recorded', at: value } : { status: 'unavailable' }
}

/**
 * Host-measured process evidence of a concluded, dispatched attempt: all three members absent is unknown; otherwise a closed outcome, an
 * int32 exit code exactly for `Exited` and a nonnegative whole-millisecond duration, or nothing of it is used.
 */
function toProcessFact(processExecution: Record<string, unknown>): AttemptMeasuredFact<ProcessEvidenceView> {
  const { outcome, exitCode, durationMilliseconds } = processExecution
  if (isAbsent(outcome) && isAbsent(exitCode) && isAbsent(durationMilliseconds)) {
    return { status: 'unknown' }
  }

  if (typeof outcome !== 'string' || !PROCESS_OUTCOMES.includes(outcome) || !isWholeMilliseconds(durationMilliseconds)) {
    return { status: 'unavailable' }
  }

  if (outcome === 'Exited') {
    return isInt32(exitCode) ? { status: 'known', value: { outcome, exitCode, durationMilliseconds } } : { status: 'unavailable' }
  }

  return isAbsent(exitCode) ? { status: 'known', value: { outcome, durationMilliseconds } } : { status: 'unavailable' }
}

/**
 * Provider-reported usage of a concluded, dispatched attempt: all four members absent is unknown; otherwise both input and output are
 * nonnegative int32 counts and each cache count is independently absent or one too. Nothing is defaulted, clamped or derived across
 * members, and one invalid member makes the whole usage unavailable.
 */
function toTokenUsageFact(tokenUsage: Record<string, unknown>): AttemptMeasuredFact<TokenUsageView> {
  const { inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens } = tokenUsage
  if (isAbsent(inputTokens) && isAbsent(outputTokens) && isAbsent(cacheCreationInputTokens) && isAbsent(cacheReadInputTokens)) {
    return { status: 'unknown' }
  }

  if (
    !isCount(inputTokens)
    || !isCount(outputTokens)
    || !(isAbsent(cacheCreationInputTokens) || isCount(cacheCreationInputTokens))
    || !(isAbsent(cacheReadInputTokens) || isCount(cacheReadInputTokens))
  ) {
    return { status: 'unavailable' }
  }

  const value: TokenUsageView = { inputTokens, outputTokens }
  if (!isAbsent(cacheCreationInputTokens)) {
    value.cacheCreationInputTokens = cacheCreationInputTokens
  }
  if (!isAbsent(cacheReadInputTokens)) {
    value.cacheReadInputTokens = cacheReadInputTokens
  }
  return { status: 'known', value }
}

/** Dispatch and status decide first; only a concluded, dispatched attempt's own section is admitted. */
function toMeasuredFact<T>(
  container: unknown,
  dispatched: AttemptInstantFact,
  running: boolean,
  admit: (record: Record<string, unknown>) => AttemptMeasuredFact<T>,
): AttemptMeasuredFact<T> {
  if (dispatched.status === 'unavailable') {
    return { status: 'unavailable' }
  }
  if (dispatched.status === 'absent') {
    return { status: 'not-dispatched' }
  }
  if (running) {
    return { status: 'running' }
  }
  if (isAbsent(container)) {
    return { status: 'unknown' }
  }

  return isRecord(container) ? admit(container) : { status: 'unavailable' }
}

function toTimeoutFact(processExecution: unknown): AttemptTimeoutFact {
  if (isAbsent(processExecution)) {
    return { status: 'unknown' }
  }
  if (!isRecord(processExecution)) {
    return { status: 'unavailable' }
  }

  const milliseconds = processExecution.timeoutMilliseconds
  if (isAbsent(milliseconds)) {
    return { status: 'unknown' }
  }

  return typeof milliseconds === 'number' && Number.isSafeInteger(milliseconds) && milliseconds > 0
    ? { status: 'known', milliseconds }
    : { status: 'unavailable' }
}

/**
 * The recorded execution facts of one selected attempt whose evidence answer was already accepted for exactly that attempt (see
 * `toAgentAttemptEvidence`), admitted section by section before anything reaches shared presentation. Each section stands alone: a
 * malformed one is unavailable without discrediting a valid sibling, the identity, the metadata or the artifact access. The browser
 * checks only shape, range and the status/dispatch rule the Domain also applies; provider schema and provenance stay the host's proof.
 */
export function toAgentAttemptExecutionFacts(evidence: AgentAttemptEvidenceResponse): AgentAttemptExecutionFacts {
  const running = evidence.attemptStatus === 'Running'
  const dispatched = toInstantFact(evidence.dispatchedAtUtc)
  const ended = toInstantFact(evidence.completedAtUtc)
  // Only a concluded attempt is ever given a completion time; a running one that names one contradicts itself.
  const completed: AttemptInstantFact = running && ended.status === 'recorded' ? { status: 'unavailable' } : ended
  const processExecution: unknown = evidence.processExecution
  const tokenUsage: unknown = evidence.tokenUsage

  return {
    claimed: toInstantFact(evidence.claimedAtUtc),
    dispatched,
    completed,
    process: toMeasuredFact(processExecution, dispatched, running, toProcessFact),
    timeout: toTimeoutFact(processExecution),
    tokenUsage: toMeasuredFact(tokenUsage, dispatched, running, toTokenUsageFact),
  }
}
