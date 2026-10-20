import { hasExactKeys } from './agentAttemptOutputs'

// The closed, whole-answer decoder of the execution capacity read (ADR-0058) and the pure start gate derived from it. The decoder accepts a
// parsed host answer only when all of it is coherent: exactly the four documented members, the fixed limit, a closed status vocabulary and
// the one tuple (recorded owners, reason) of that status. Anything else is null and is never repaired, trimmed, coerced or partially
// accepted. A missing member is not an explicit null. What a reading says is only what the host's durable ownership read found across the
// whole database: never permission, liveness, remaining provider allowance or a frozen cross-family snapshot.

export const EXECUTION_CAPACITY_LIMIT = 2
const MAXIMUM_RECORDED_OWNERS = 2_147_483_647

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
  limit: typeof EXECUTION_CAPACITY_LIMIT
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
  if (limit !== EXECUTION_CAPACITY_LIMIT || !isStatus(status) || reasonCode !== EXECUTION_CAPACITY_REASON_BY_STATUS[status]) {
    return null
  }

  if (status === 'Unavailable') {
    return recordedOwners === null ? { limit, recordedOwners: null, status, reasonCode: EXECUTION_CAPACITY_REASON_BY_STATUS[status] } : null
  }

  if (!isCount(recordedOwners)) {
    return null
  }

  // Available records zero or one owner; AtCapacity two through Int32.MaxValue. Both are checked, so a count that contradicts its status is refused.
  const inRange =
    status === 'Available' ? recordedOwners >= 0 && recordedOwners < EXECUTION_CAPACITY_LIMIT
      : recordedOwners >= EXECUTION_CAPACITY_LIMIT && recordedOwners <= MAXIMUM_RECORDED_OWNERS
  return inRange ? { limit, recordedOwners, status, reasonCode: EXECUTION_CAPACITY_REASON_BY_STATUS[status] } : null
}

/** What the host session's owned lifetime exposes of the read; the subset a region needs to derive its start gate. */
export interface ExecutionCapacityState {
  /** The validated reading of the current lifetime; null while nothing was validated and after a failed, timed-out or refused read. */
  reading: ExecutionCapacityReading | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: ExecutionCapacityReadFailure | null
  /** Reads the capacity again now; a callback retained from a replaced or unmounted lifetime does nothing. */
  refresh: () => void
}

export interface ExecutionCapacityStartGate {
  /**
   * True only for a validated, current Available reading with nothing pending or failed. It never grants an action: every existing
   * eligibility rule still applies, and this value is only ever an additional veto of a new potentially mutating start.
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
  AtCapacity: `Two potentially mutating executions are already recorded across this host, so ${WITHHELD} until one finishes. Nothing is cancelled, queued or released here. ${AFTER}`,
  Unavailable: `The recorded executions could not be counted safely, so ${WITHHELD}. Inspect the recorded executions before any further work. ${AFTER}`,
} as const

/**
 * Whether the host's execution capacity lets a new potentially mutating start apply (an additional veto only). Only a validated,
 * coherent Available reading with nothing pending or failed allows; every other state, including an absent one, withholds. The reading
 * is judged again whole here, so a reading that is not exactly a closed tuple can never allow.
 */
export function deriveExecutionCapacityStartGate(state: ExecutionCapacityState | null | undefined): ExecutionCapacityStartGate {
  const withheld = (guidance: string): ExecutionCapacityStartGate => ({ allowsMutatingStart: false, guidance })
  // An absent state (null, or undefined from a caller that omitted it) is never a permission: it withholds like a pending read.
  if (state === null || state === undefined) {
    return withheld(EXECUTION_CAPACITY_GUIDANCE.pending)
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

  return reading.status === 'Available' ? { allowsMutatingStart: true, guidance: null } : withheld(EXECUTION_CAPACITY_GUIDANCE[reading.status])
}

/**
 * The short pointer every withheld mutating start control shows while the region's one guidance is displayed, where a control needs its
 * own fixed reason string; the guidance itself is shown once per region, never once per button.
 */
export const EXECUTION_CAPACITY_WITHHELD_POINTER = 'Withheld: see the execution capacity guidance in this region.'
