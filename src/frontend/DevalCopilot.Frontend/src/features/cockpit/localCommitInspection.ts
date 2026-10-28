import { hasExactKeys, isInt, isUtcRoundTrip } from './agentAttemptVerification'

// The closed, whole-answer decoder of the recorded local-commit operation of one run (ADR-0070). It accepts a parsed host answer only
// when all of it is coherent for exactly the run that was asked about: exactly the documented members at every level, exact types,
// closed vocabularies, canonical identifiers and times, exact integer bounds, the complete status/time/reason/commit-identity tuple the
// host proves, and a reason that is either absent, one of the finite allowlisted codes, or unrecognized without a code. Anything else is
// null and is never repaired, coerced, clamped or partially accepted. The operation is unsigned relational consistency: it is not a
// receipt, a live Git observation, current eligibility or proof of promotion or publication.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const OBJECT_ID = /^[0-9a-f]{40}$/
const SHA256 = /^[0-9a-f]{64}$/
// `char.IsControl`: the C0 and C1 control characters.
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/
const INT32_MAX = 2 ** 31 - 1
const MAXIMUM_CHANGED_PATHS = 128
const MAXIMUM_TOTAL_BYTES = 4 * 1024 * 1024
const MAXIMUM_BRANCH_LENGTH = 512

export const LOCAL_COMMIT_INSPECTION_STATUSES = ['NotRecorded', 'Unavailable', 'Recorded'] as const
export type LocalCommitInspectionStatus = (typeof LOCAL_COMMIT_INSPECTION_STATUSES)[number]

export const LOCAL_COMMIT_INSPECTION_OPERATION_STATUSES = [
  'Prepared',
  'Executing',
  'Completed',
  'Failed',
  'Interrupted',
  'NeedsAttention',
] as const
export type LocalCommitInspectionOperationStatus = (typeof LOCAL_COMMIT_INSPECTION_OPERATION_STATUSES)[number]

/** The operation-owned finite allowlist of recorded reason codes the host describes; every other recorded reason is Unrecognized. */
export const LOCAL_COMMIT_INSPECTION_KNOWN_REASON_CODES = [
  'local_commit.authority_changed',
  'local_commit.git_unprovable',
  'local_commit.ownership_unprovable',
  'local_commit.head_not_bound',
  'local_commit.prepared_state_unexpected',
  'local_commit.unpromoted_state_unknown',
  'local_commit.branch_tip_unrecognized',
  'local_commit.unknown_index_lock',
  'local_commit.index_state_unrecognized',
  'local_commit.unknown_reference_lock',
  'local_commit.reference_lock_unproven',
  'local_commit.interrupted_before_execution',
  'local_commit.interrupted_unpromoted',
] as const
export type LocalCommitInspectionKnownReasonCode = (typeof LOCAL_COMMIT_INSPECTION_KNOWN_REASON_CODES)[number]

export type LocalCommitInspectionReason =
  | { kind: 'None'; code: null }
  | { kind: 'Known'; code: LocalCommitInspectionKnownReasonCode }
  | { kind: 'Unrecognized'; code: null }

export interface LocalCommitInspectionOperation {
  operationId: string
  projectId: string
  workspaceId: string
  checkpointId: string
  checkpointNumber: number
  checkpointFingerprintSha256: string
  status: LocalCommitInspectionOperationStatus
  branchName: string
  /** A recorded preparation fact, never proof of promotion. */
  parentCommitSha: string
  /** A recorded preparation fact, never proof of promotion. */
  treeSha: string
  /** The recorded result commit: present only for a Completed operation. */
  commitSha: string | null
  changedPathCount: number
  totalBytes: number
  createdAtUtc: string
  /** Null when the single-use execution marker was never recorded (Failed, Interrupted and NeedsAttention may lack it). */
  executionStartedAtUtc: string | null
  completedAtUtc: string | null
  reason: LocalCommitInspectionReason
}

export type LocalCommitInspectionAnswer =
  | { status: 'NotRecorded' | 'Unavailable'; runId: string }
  | { status: 'Recorded'; runId: string; operation: LocalCommitInspectionOperation }

/** What an answer must belong to: the one run the read was made for. Frozen when the read begins. */
export interface LocalCommitInspectionTarget {
  runId: string
}

// The stored default instant is never a recorded time; the backend refuses it, and a legitimate instant one tick later stays valid.
const DEFAULT_UTC = '0001-01-01T00:00:00.0000000Z'
const isRecordedInstant = (value: unknown): value is string => isUtcRoundTrip(value) && value !== DEFAULT_UTC

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const isObjectId = (value: unknown): value is string => typeof value === 'string' && OBJECT_ID.test(value)
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isBranchName = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '' && value.length <= MAXIMUM_BRANCH_LENGTH && !CONTROL.test(value)

const ANSWER_KEYS = ['operation', 'runId', 'status']
const OPERATION_KEYS = [
  'branchName',
  'changedPathCount',
  'checkpointFingerprintSha256',
  'checkpointId',
  'checkpointNumber',
  'commitSha',
  'completedAtUtc',
  'createdAtUtc',
  'executionStartedAtUtc',
  'operationId',
  'parentCommitSha',
  'projectId',
  'reason',
  'status',
  'totalBytes',
  'treeSha',
  'workspaceId',
]
const REASON_KEYS = ['code', 'kind']

function toReason(raw: unknown): LocalCommitInspectionReason | null {
  if (!hasExactKeys(raw, REASON_KEYS)) {
    return null
  }

  if (raw.kind === 'None' || raw.kind === 'Unrecognized') {
    return raw.code === null ? { kind: raw.kind, code: null } : null
  }

  return raw.kind === 'Known' && isOneOf(LOCAL_COMMIT_INSPECTION_KNOWN_REASON_CODES, raw.code) ? { kind: 'Known', code: raw.code } : null
}

/**
 * The status/time/reason/commit-identity tuple ADR-0070 enforces. Canonical UTC round-trip texts have one fixed width, so their text
 * order is their chronological order at full tick precision. No dispatch or completion time is ever invented for a state that lacks one.
 */
function isCoherentTuple(
  status: LocalCommitInspectionOperationStatus,
  created: string,
  started: string | null,
  completed: string | null,
  reason: LocalCommitInspectionReason,
  commitSha: string | null,
): boolean {
  if ((status === 'Completed') !== (commitSha !== null)) {
    return false
  }

  if ((status === 'Failed' || status === 'Interrupted' || status === 'NeedsAttention') === (reason.kind === 'None')) {
    return false
  }

  switch (status) {
    case 'Prepared':
      return started === null && completed === null
    case 'Executing':
      return started !== null && started >= created && completed === null
    case 'Completed':
      return started !== null && started >= created && completed !== null && completed >= started
    case 'Failed':
    case 'Interrupted':
      return completed !== null && completed >= created && (started === null || (started >= created && started <= completed))
    default:
      return completed === null && (started === null || started >= created)
  }
}

function toOperation(raw: unknown): LocalCommitInspectionOperation | null {
  if (!hasExactKeys(raw, OPERATION_KEYS)) {
    return null
  }

  const reason = toReason(raw.reason)
  if (
    reason === null
    || !isGuid(raw.operationId)
    || !isGuid(raw.projectId)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isInt(raw.checkpointNumber, 1, INT32_MAX)
    || !isSha256(raw.checkpointFingerprintSha256)
    || !isOneOf(LOCAL_COMMIT_INSPECTION_OPERATION_STATUSES, raw.status)
    || !isBranchName(raw.branchName)
    || !isObjectId(raw.parentCommitSha)
    || !isObjectId(raw.treeSha)
    || (raw.commitSha !== null && !isObjectId(raw.commitSha))
    || !isInt(raw.changedPathCount, 1, MAXIMUM_CHANGED_PATHS)
    || !isInt(raw.totalBytes, 0, MAXIMUM_TOTAL_BYTES)
    || !isRecordedInstant(raw.createdAtUtc)
    || (raw.executionStartedAtUtc !== null && !isRecordedInstant(raw.executionStartedAtUtc))
    || (raw.completedAtUtc !== null && !isRecordedInstant(raw.completedAtUtc))
  ) {
    return null
  }

  if (!isCoherentTuple(raw.status, raw.createdAtUtc, raw.executionStartedAtUtc, raw.completedAtUtc, reason, raw.commitSha)) {
    return null
  }

  return {
    operationId: raw.operationId,
    projectId: raw.projectId,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    checkpointNumber: raw.checkpointNumber,
    checkpointFingerprintSha256: raw.checkpointFingerprintSha256,
    status: raw.status,
    branchName: raw.branchName,
    parentCommitSha: raw.parentCommitSha,
    treeSha: raw.treeSha,
    commitSha: raw.commitSha,
    changedPathCount: raw.changedPathCount,
    totalBytes: raw.totalBytes,
    createdAtUtc: raw.createdAtUtc,
    executionStartedAtUtc: raw.executionStartedAtUtc,
    completedAtUtc: raw.completedAtUtc,
    reason,
  }
}

/**
 * Accepts a parsed recorded-local-commit answer only when the whole of it is coherent for exactly `target`'s run: the closed graph has
 * exactly its three members, an operation only for Recorded, and a Recorded operation passes every bound and the whole tuple.
 * Otherwise null.
 */
export function toLocalCommitInspection(raw: unknown, target: LocalCommitInspectionTarget): LocalCommitInspectionAnswer | null {
  if (!hasExactKeys(raw, ANSWER_KEYS) || raw.runId !== target.runId || !isOneOf(LOCAL_COMMIT_INSPECTION_STATUSES, raw.status)) {
    return null
  }

  const { runId } = target
  if (raw.status !== 'Recorded') {
    return raw.operation === null ? { status: raw.status, runId } : null
  }

  const operation = toOperation(raw.operation)
  return operation === null ? null : { status: 'Recorded', runId, operation }
}
