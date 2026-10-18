import type { AgentAttemptIdentityExpectation } from './agentAttemptHistory'
import { toWireInstant } from './agentAttemptEvidenceWire'

/** The most members the host inspects and shows (ADR-0052). */
export const VERIFICATION_MEMBERS_INSPECTED = 32

/** The retained source bytes per member/stream selection, the default and smallest window and the smallest request worth sending. */
export const VERIFICATION_VIEW_BUDGET_BYTES = 256 * 1024
export const VERIFICATION_WINDOW_BYTES = 16 * 1024
export const VERIFICATION_MIN_WINDOW_BYTES = 64

const MAX_COMMAND_NAME_UNITS = 200
const INT32_MIN = -(2 ** 31)
const INT32_MAX = 2 ** 31 - 1

export type VerificationStream = 'stdout' | 'stderr'
export type VerificationCaptureOutcome = 'CapturedWithKnownTruncation' | 'RecoveredAfterHostInterruption'

/** One stream of a member: the metadata as the host admitted it. Only `Recorded` can be selected and read. */
export type VerificationStreamDescriptor =
  | { status: 'NotRecorded' }
  | { status: 'Unavailable' }
  | {
      status: 'Recorded'
      artifactId: string
      byteLength: number
      captureOutcome: VerificationCaptureOutcome
      /** False and true are known capture results; null is unknown (recovered after a host interruption). */
      truncated: boolean | null
    }

export type RecordedVerificationStreamDescriptor = Extract<VerificationStreamDescriptor, { status: 'Recorded' }>

export interface VerificationMember {
  sequence: number
  commandId: string
  executionId: string
  executionNumber: number
  /** The command name of the execution's immutable snapshot, never the current recipe. */
  commandName: string
  status: 'Passed' | 'Failed'
  exitCode: number
  /** The validated invariant UTC round-trip texts, exactly as the host wrote them. */
  claimedAtUtc: string
  dispatchedAtUtc: string
  completedAtUtc: string
  standardOutput: VerificationStreamDescriptor
  standardError: VerificationStreamDescriptor
}

export type AgentAttemptVerificationEvidence =
  // The identity, the contract, the size or the coherence of the membership could not be proved; which one is not disclosed.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; checkpoint: { id: string; checkpointNumber: number }; members: readonly VerificationMember[] }

/** The selected attempt the answer must name: its run and the accepted history row's identity, number, role, provider and contract. */
export interface AgentAttemptVerificationExpectation extends AgentAttemptIdentityExpectation {
  runId: string
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isInt = (value: unknown, min: number, max: number): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max
const isByteCount = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0

// The host writes `DateTime.ToString("O")` of a UTC instant: seven fractional digits and `Z`, nothing else. The instant parser is the
// proven one the evidence timestamps use.
const UTC_ROUND_TRIP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/
const isUtcRoundTrip = (value: unknown): value is string =>
  typeof value === 'string' && UTC_ROUND_TRIP.test(value) && toWireInstant(value) !== null

/** Whether `value` is an object with exactly `keys` as own properties: a closed wire shape, so an unexpected field refuses the answer. */
function hasExactKeys(value: unknown, keys: readonly string[]): value is Record<string, unknown> {
  if (!isRecord(value)) {
    return false
  }

  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

const ROOT_KEYS = ['runId', 'attemptId', 'status', 'attempt', 'checkpoint', 'memberTotalCount', 'members'] as const
const HEADER_KEYS = ['id', 'attemptNumber', 'role', 'provider', 'responseContract'] as const
const CHECKPOINT_KEYS = ['id', 'checkpointNumber'] as const
const STREAM_KEYS = ['status', 'artifactId', 'byteLength', 'captureOutcome', 'truncated'] as const
const MEMBER_KEYS = [
  'sequence',
  'commandId',
  'executionId',
  'executionNumber',
  'commandName',
  'status',
  'exitCode',
  'claimedAtUtc',
  'dispatchedAtUtc',
  'completedAtUtc',
  'standardOutput',
  'standardError',
] as const
const WINDOW_KEYS = ['runId', 'attemptId', 'executionId', 'stream', 'status', 'artifact', 'text', 'nextOffset', 'totalLength'] as const

/** Only these two contracts of a CodeReviewer attempt record verification membership; the control is offered for nothing else. */
export const supportsVerificationEvidence = (expected: AgentAttemptIdentityExpectation): boolean =>
  expected.role === 'CodeReviewer'
  && expected.provider === 'Codex'
  && (expected.responseContract === 'ImplementationReview' || expected.responseContract === 'VerificationDiagnosis')

/** The header must be exactly the selected attempt: the same identity and number and the same immutable role, provider and contract. */
function isSelectedHeader(value: unknown, expected: AgentAttemptVerificationExpectation): boolean {
  return hasExactKeys(value, HEADER_KEYS)
    && value.id === expected.attemptId
    && value.attemptNumber === expected.attemptNumber
    && value.role === expected.role
    && value.provider === expected.provider
    && value.responseContract === expected.responseContract
}

function toDescriptor(raw: unknown): VerificationStreamDescriptor | null {
  if (!hasExactKeys(raw, STREAM_KEYS)) {
    return null
  }

  const { status, artifactId, byteLength, captureOutcome, truncated } = raw
  if (status === 'NotRecorded' || status === 'Unavailable') {
    return artifactId === null && byteLength === null && captureOutcome === null && truncated === null ? { status } : null
  }

  if (status !== 'Recorded' || !isText(artifactId) || !isByteCount(byteLength)) {
    return null
  }

  // Captured with known truncation carries a Boolean, recovered after a host interruption an unknown null: false, true and null stay distinct.
  if (captureOutcome === 'CapturedWithKnownTruncation' && typeof truncated === 'boolean') {
    return { status, artifactId, byteLength, captureOutcome, truncated }
  }

  return captureOutcome === 'RecoveredAfterHostInterruption' && truncated === null
    ? { status, artifactId, byteLength, captureOutcome, truncated: null }
    : null
}

function toMember(raw: unknown, position: number): VerificationMember | null {
  if (!hasExactKeys(raw, MEMBER_KEYS)) {
    return null
  }

  const standardOutput = toDescriptor(raw.standardOutput)
  const standardError = toDescriptor(raw.standardError)
  if (
    raw.sequence !== position
    || !isText(raw.commandId)
    || !isText(raw.executionId)
    || !isInt(raw.executionNumber, 1, INT32_MAX)
    || !isText(raw.commandName)
    || raw.commandName.length > MAX_COMMAND_NAME_UNITS
    || (raw.status !== 'Passed' && raw.status !== 'Failed')
    || !isInt(raw.exitCode, INT32_MIN, INT32_MAX)
    // Passed is exit code 0 and Failed is a nonzero one; the process always exited.
    || (raw.status === 'Passed') !== (raw.exitCode === 0)
    || !isUtcRoundTrip(raw.claimedAtUtc)
    || !isUtcRoundTrip(raw.dispatchedAtUtc)
    || !isUtcRoundTrip(raw.completedAtUtc)
    || standardOutput === null
    || standardError === null
  ) {
    return null
  }

  return {
    sequence: position,
    commandId: raw.commandId,
    executionId: raw.executionId,
    executionNumber: raw.executionNumber,
    commandName: raw.commandName,
    status: raw.status,
    exitCode: raw.exitCode,
    claimedAtUtc: raw.claimedAtUtc,
    dispatchedAtUtc: raw.dispatchedAtUtc,
    completedAtUtc: raw.completedAtUtc,
    standardOutput,
    standardError,
  }
}

function toMembers(raw: unknown[], diagnosis: boolean): VerificationMember[] | null {
  const commands = new Set<string>()
  const executions = new Set<string>()
  const artifacts = new Set<string>()
  const members: VerificationMember[] = []
  for (const [position, item] of raw.entries()) {
    const member = toMember(item, position)
    if (member === null || commands.has(member.commandId) || executions.has(member.executionId)) {
      return null
    }

    commands.add(member.commandId)
    executions.add(member.executionId)
    for (const descriptor of [member.standardOutput, member.standardError]) {
      if (descriptor.status === 'Recorded') {
        if (artifacts.has(descriptor.artifactId)) {
          return null
        }

        artifacts.add(descriptor.artifactId)
      }
    }

    members.push(member)
  }

  // An ordinary review admits only passed members; a diagnosis was claimed against at least one failed one.
  const anyFailed = members.some((member) => member.status === 'Failed')
  return diagnosis ? (anyFailed ? members : null) : anyFailed ? null : members
}

/**
 * Accepts a parsed host answer only when the whole of it is coherent for exactly `expected`: closed shapes at every level, the requested
 * run and attempt, a closed status (`Unavailable`: no attempt, checkpoint or count and no member; `Recorded`: the selected immutable
 * header of a supported CodeReviewer contract, a checkpoint with a nonempty identity and positive Int32 number, a count from 1 to 32
 * equal to the members, and members at their own ordered positions from zero with unique command, execution and artifact identities,
 * a nonblank snapshot name of at most 200 UTF-16 units, Passed with exit code 0 or Failed with a nonzero one, valid UTC round-trip
 * texts and closed stream descriptors whose capture tuple is coherent). Anything else is null and is never repaired, trimmed or
 * partially accepted.
 */
export function toAgentAttemptVerificationEvidence(
  raw: unknown,
  expected: AgentAttemptVerificationExpectation,
): AgentAttemptVerificationEvidence | null {
  if (
    !hasExactKeys(raw, ROOT_KEYS)
    || raw.runId !== expected.runId
    || raw.attemptId !== expected.attemptId
    || !Array.isArray(raw.members)
  ) {
    return null
  }

  const { status, attempt, checkpoint, memberTotalCount, members } = raw
  if (status === 'Unavailable') {
    return attempt === null && checkpoint === null && memberTotalCount === null && members.length === 0 ? { status } : null
  }

  if (
    status !== 'Recorded'
    || !supportsVerificationEvidence(expected)
    || !isSelectedHeader(attempt, expected)
    || !hasExactKeys(checkpoint, CHECKPOINT_KEYS)
    || !isText(checkpoint.id)
    || !isInt(checkpoint.checkpointNumber, 1, INT32_MAX)
    || !isInt(memberTotalCount, 1, VERIFICATION_MEMBERS_INSPECTED)
    || memberTotalCount !== members.length
  ) {
    return null
  }

  const accepted = toMembers(members, expected.responseContract === 'VerificationDiagnosis')
  return accepted === null
    ? null
    : { status, checkpoint: { id: checkpoint.id, checkpointNumber: checkpoint.checkpointNumber }, members: accepted }
}

/** What one window read asked for: the selected member's stream and its Recorded descriptor from the accepted metadata, the byte window. */
export interface VerificationWindowRequestFacts {
  runId: string
  attemptId: string
  executionId: string
  stream: VerificationStream
  descriptor: RecordedVerificationStreamDescriptor
  fromOffset: number
  maxBytes: number
}

/** One host answer, decided whole: an accepted verified window, a closed non-text status, or an answer that is not coherent. */
export type VerificationWindowAnswer =
  | { kind: 'ok'; text: string; nextOffset: number; totalLength: number }
  | { kind: 'unavailable' }
  | { kind: 'not-recorded' }
  | { kind: 'missing' }
  | { kind: 'integrity-mismatch' }
  | { kind: 'incoherent' }

const INCOHERENT: VerificationWindowAnswer = { kind: 'incoherent' }

/**
 * Decides one window answer for exactly `request`: the requested run, attempt, member and canonical stream, a closed status, and, by
 * status, a whole shape. A non-`Ok` status carries no artifact, empty text, the requested offset unchanged and a total length of 0, and
 * nothing else of it is used. An `Ok` window carries the very descriptor the selection was made from, a total length equal to that
 * descriptor's byte length, a cursor with `fromOffset <= nextOffset <= totalLength` that advances by at most the requested cap and by
 * at least one byte while bytes remain, and text that is empty exactly when the cursor did not advance. The cursor counts UTF-8 bytes
 * of the sealed file and the host decodes malformed UTF-8 with replacement and defers a split code point to the next window, so the
 * text is never re-encoded to recompute or require the advance; only two facts that hold for every UTF-8 decoding are checked: a
 * positive advance decodes to some text, and no UTF-16 code unit is produced without consuming at least one byte
 * (`text.length <= advance`).
 */
export function decideVerificationWindowAnswer(raw: unknown, request: VerificationWindowRequestFacts): VerificationWindowAnswer {
  if (
    !hasExactKeys(raw, WINDOW_KEYS)
    || raw.runId !== request.runId
    || raw.attemptId !== request.attemptId
    || raw.executionId !== request.executionId
    || raw.stream !== request.stream
    || typeof raw.text !== 'string'
    || !isByteCount(raw.nextOffset)
    || !isByteCount(raw.totalLength)
  ) {
    return INCOHERENT
  }

  const { status, artifact, text, nextOffset, totalLength } = raw
  const { fromOffset, maxBytes, descriptor } = request
  if (status === 'Ok') {
    const advance = nextOffset - fromOffset
    const served = toDescriptor(artifact)
    return served !== null
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
      : INCOHERENT
  }

  if (artifact !== null || text !== '' || nextOffset !== fromOffset || totalLength !== 0) {
    return INCOHERENT
  }

  switch (status) {
    case 'Unavailable':
      return { kind: 'unavailable' }
    case 'NotRecorded':
      return { kind: 'not-recorded' }
    case 'Missing':
      return { kind: 'missing' }
    case 'IntegrityMismatch':
      return { kind: 'integrity-mismatch' }
    default:
      return INCOHERENT
  }
}
