import { hasExactKeys } from './agentAttemptOutputs'

// The closed, whole-answer decoder of the execution capacity read (ADR-0058, limit per ADR-0059) and the pure start gate derived from it.
// The decoder accepts a parsed host answer only when all of it is coherent: exactly the four documented members, a limit of one or two
// (null only when the host could not validate its configuration), a closed status vocabulary and the one tuple (recorded owners, reason) of
// that status judged against that limit. Anything else is null and is never repaired, trimmed, coerced or partially accepted. A missing
// member is not an explicit null. What a reading says is only what the host's durable ownership read found across the whole database:
// never permission, liveness, remaining provider allowance or a frozen cross-family snapshot.

export const EXECUTION_CAPACITY_LIMITS = [1, 2] as const
export type ExecutionCapacityLimit = (typeof EXECUTION_CAPACITY_LIMITS)[number]
const MAXIMUM_RECORDED_OWNERS = 2_147_483_647

export const isExecutionCapacityLimit = (value: unknown): value is ExecutionCapacityLimit => value === 1 || value === 2

export const EXECUTION_CAPACITY_STATUSES = ['Available', 'AtCapacity', 'Unavailable'] as const
export type ExecutionCapacityStatus = (typeof EXECUTION_CAPACITY_STATUSES)[number]

/** The one reason code each status carries (null: none); the closed table of the contract. */
export const EXECUTION_CAPACITY_REASON_BY_STATUS = {
  Available: null,
  AtCapacity: 'execution_capacity_limit_reached',
  Unavailable: 'execution_capacity_unavailable',
} as const satisfies Readonly<Record<ExecutionCapacityStatus, string | null>>
export type ExecutionCapacityReasonCode = Exclude<(typeof EXECUTION_CAPACITY_REASON_BY_STATUS)[ExecutionCapacityStatus], null>

export interface ExecutionCapacityReading {
  /** The validated persisted limit; null only for an Unavailable reading whose configuration the host could not validate. */
  limit: ExecutionCapacityLimit | null
  /** The coherent recorded owners the host counted; null exactly when the status is Unavailable. */
  recordedOwners: number | null
  status: ExecutionCapacityStatus
  reasonCode: ExecutionCapacityReasonCode | null
}

/** `read`: the request failed (including a refusal of the session). `timeout`: it did not answer within ten seconds. `invalid`: the answer is not coherent. */
export type ExecutionCapacityReadFailure = 'read' | 'timeout' | 'invalid'

const KEYS = ['limit', 'reasonCode', 'recordedOwners', 'status']

const isStatus = (value: unknown): value is ExecutionCapacityStatus =>
  typeof value === 'string' && (EXECUTION_CAPACITY_STATUSES as readonly string[]).includes(value)

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value)

/** The whole host answer, or null unless every member and the tuple of its status are coherent. */
export function toExecutionCapacityReading(raw: unknown): ExecutionCapacityReading | null {
  if (!hasExactKeys(raw, KEYS)) {
    return null
  }

  const { limit, recordedOwners, status, reasonCode } = raw
  if (!isStatus(status) || reasonCode !== EXECUTION_CAPACITY_REASON_BY_STATUS[status]) {
    return null
  }

  if (status === 'Unavailable') {
    // The limit is the validated one when only the ownership reading failed, and an explicit null exactly when the configuration did.
    return recordedOwners === null && (limit === null || isExecutionCapacityLimit(limit))
      ? { limit, recordedOwners: null, status, reasonCode: EXECUTION_CAPACITY_REASON_BY_STATUS[status] }
      : null
  }

  if (!isExecutionCapacityLimit(limit) || !isCount(recordedOwners)) {
    return null
  }

  // Available records fewer owners than the limit; AtCapacity the limit through Int32.MaxValue. Both are checked, so a count that
  // contradicts its status or its limit is refused.
  const inRange =
    status === 'Available' ? recordedOwners >= 0 && recordedOwners < limit
      : recordedOwners >= limit && recordedOwners <= MAXIMUM_RECORDED_OWNERS
  return inRange ? { limit, recordedOwners, status, reasonCode: EXECUTION_CAPACITY_REASON_BY_STATUS[status] } : null
}

/**
 * Where the one explicit limit change of the host-session lifetime is (ADR-0059). `applying`: the write is in flight. `confirming`: its
 * transport settled and a read that began afterwards has not. `idle`: neither; `outcome` then says how the last change ended, and
 * `unconfirmed` never means success or rollback.
 */
export type ExecutionCapacityChangePhase = 'idle' | 'applying' | 'confirming'
export type ExecutionCapacityChangeOutcome = 'applied' | 'belowOwners' | 'refused' | 'unconfirmed'

export interface ExecutionCapacityChange {
  phase: ExecutionCapacityChangePhase
  outcome: ExecutionCapacityChangeOutcome | null
  /** The limit the operator asked for; null before any change of this lifetime. */
  requested: ExecutionCapacityLimit | null
}

/** What the host session's owned lifetime exposes of the read; the subset a region needs to derive its start gate. */
export interface ExecutionCapacityState {
  /** The validated reading of the current lifetime; null while nothing was validated, after a failed, timed-out or refused read and while a limit change is unconfirmed. */
  reading: ExecutionCapacityReading | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: ExecutionCapacityReadFailure | null
  /** Reads the capacity again now; a callback retained from a replaced or unmounted lifetime does nothing. */
  refresh: () => void
  /** The limit change barrier of the lifetime; absent means idle (every state a region was handed before ADR-0059). */
  change?: ExecutionCapacityChange
}

export interface ExecutionCapacityStartGate {
  /**
   * True only for a validated, current Available reading with nothing pending, failed or being changed. It never grants an action: every
   * existing eligibility rule still applies, and this value is only ever an additional veto of a new potentially mutating start.
   */
  allowsMutatingStart: boolean
  /** The fixed English sentence shown once per region while a mutating start is withheld; null when it is allowed. Never an id or a path. */
  guidance: string | null
}

const AFTER =
  'Planning, review, diagnosis, authorizations, approvals, evidence, history, Refresh, Stop after current step, abandonment and recorded results stay available; refresh the capacity to read it again.'

const WITHHELD = 'starting an implementation, correction, verification or local commit is withheld'

/** Fixed copy per state; nothing is taken from the host answer, so an unrecognized value can never be worded as a known one. */
export const EXECUTION_CAPACITY_GUIDANCE = {
  pending: `Reading the execution capacity. Starting an implementation, correction, verification or local commit is withheld until it is confirmed. ${AFTER}`,
  read: `The execution capacity could not be read, so ${WITHHELD}. ${AFTER}`,
  timeout: `The execution capacity did not answer within ten seconds, so ${WITHHELD}. ${AFTER}`,
  invalid: `The execution capacity was not coherent, so ${WITHHELD}. ${AFTER}`,
  changing: `The recorded-execution limit is being changed, so ${WITHHELD} until the new limit has been read back. Changing it starts no work. ${AFTER}`,
  AtCapacity: `Two potentially mutating executions are already recorded across this host (a dispatched execution that ended without recorded process exit evidence still counts until it is reconciled or a separate Human release frees it), so ${WITHHELD} until one finishes or is reconciled. Nothing is cancelled, queued or released here. ${AFTER}`,
  AtCapacityLimitOne: `One potentially mutating execution is already recorded across this host (the recorded-execution limit is one; a dispatched execution that ended without recorded process exit evidence still counts until it is reconciled or a separate Human release frees it), so ${WITHHELD} until it finishes or is reconciled. Nothing is cancelled, queued or released here. ${AFTER}`,
  Unavailable: `The recorded executions could not be counted safely, so ${WITHHELD}. Inspect the recorded executions before any further work. ${AFTER}`,
} as const

/**
 * Whether the host's execution capacity lets a new potentially mutating start apply (an additional veto only). Only a validated,
 * coherent Available reading with nothing pending, failed or being changed allows; every other state, including an absent one, withholds.
 * The reading is judged again whole here, so a reading that is not exactly a closed tuple can never allow.
 */
export function deriveExecutionCapacityStartGate(state: ExecutionCapacityState | null | undefined): ExecutionCapacityStartGate {
  const withheld = (guidance: string): ExecutionCapacityStartGate => ({ allowsMutatingStart: false, guidance })
  // An absent state (null, or undefined from a caller that omitted it) is never a permission: it withholds like a pending read.
  if (state === null || state === undefined) {
    return withheld(EXECUTION_CAPACITY_GUIDANCE.pending)
  }

  if (state.change !== undefined && state.change.phase !== 'idle') {
    return withheld(EXECUTION_CAPACITY_GUIDANCE.changing)
  }

  if (state.failure !== null) {
    return withheld(Object.hasOwn(EXECUTION_CAPACITY_GUIDANCE, state.failure) ? EXECUTION_CAPACITY_GUIDANCE[state.failure] : EXECUTION_CAPACITY_GUIDANCE.invalid)
  }

  if (state.reading === null) {
    return withheld(EXECUTION_CAPACITY_GUIDANCE.pending)
  }

  const reading = toExecutionCapacityReading(state.reading)
  if (reading === null) {
    return withheld(EXECUTION_CAPACITY_GUIDANCE.invalid)
  }

  if (reading.status === 'AtCapacity') {
    return withheld(reading.limit === 1 ? EXECUTION_CAPACITY_GUIDANCE.AtCapacityLimitOne : EXECUTION_CAPACITY_GUIDANCE.AtCapacity)
  }

  return reading.status === 'Available' ? { allowsMutatingStart: true, guidance: null } : withheld(EXECUTION_CAPACITY_GUIDANCE.Unavailable)
}

/**
 * The short pointer every withheld mutating start control shows while the region's one guidance is displayed, where a control needs its
 * own fixed reason string; the guidance itself is shown once per region, never once per button.
 */
export const EXECUTION_CAPACITY_WITHHELD_POINTER = 'Withheld: see the execution capacity guidance in this region.'

/** What the global control always says: changing the limit decides only how many recorded executions may exist and starts nothing. */
export const EXECUTION_CAPACITY_CHANGE_DISCLOSURE =
  'Changing the recorded-execution limit starts no work: nothing is cancelled, queued or released, and a lower limit is refused while more executions are recorded than it allows.'

/** Fixed feedback of the limit change; the confirmed wording states the limit that was read back, never the one that was requested. */
export const EXECUTION_CAPACITY_CHANGE_FEEDBACK = {
  applying: 'Applying the recorded-execution limit.',
  confirming: 'Reading the execution capacity back to confirm the limit.',
  appliedOne: 'The recorded-execution limit is now one.',
  appliedTwo: 'The recorded-execution limit is now two.',
  differs: 'The limit read back is not the one requested; the recorded-execution limit shown is the current one.',
  belowOwners:
    'The limit was not lowered: more executions are recorded as active than that limit allows, so nothing was changed and no pending reduction is saved. Try again after recorded work changes.',
  refused: 'The limit change was refused and nothing was changed.',
  unconfirmed:
    'The limit change could not be confirmed: it may or may not have been applied. The recorded-execution limit shown is only what was read back.',
} as const

/** The feedback sentence for the control, or null when there is nothing to say; never an optimistic result. */
export function describeExecutionCapacityChange(
  change: ExecutionCapacityChange,
  reading: ExecutionCapacityReading | null,
): string | null {
  if (change.phase === 'applying') {
    return EXECUTION_CAPACITY_CHANGE_FEEDBACK.applying
  }

  if (change.phase === 'confirming') {
    return EXECUTION_CAPACITY_CHANGE_FEEDBACK.confirming
  }

  if (change.outcome === null) {
    return null
  }

  if (change.outcome !== 'applied') {
    return EXECUTION_CAPACITY_CHANGE_FEEDBACK[change.outcome]
  }

  // The write answered success, but only a validated read-back states what is current.
  if (reading === null || reading.limit === null) {
    return EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed
  }

  if (reading.limit !== change.requested) {
    return EXECUTION_CAPACITY_CHANGE_FEEDBACK.differs
  }

  return reading.limit === 1 ? EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedOne : EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedTwo
}
