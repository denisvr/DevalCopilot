import { hasExactKeys, isByteCount, isInt, isUtcRoundTrip, toVerificationStreamDescriptor } from './agentAttemptVerification'
import type {
  RecordedVerificationStreamDescriptor,
  VerificationStream,
  VerificationStreamDescriptor,
  VerificationWindowAnswer,
} from './agentAttemptVerification'

// The closed, whole-answer decoders of the three recorded project verification operations (ADR-0069). Each accepts a parsed host answer
// only when all of it is coherent for exactly the project, execution and stream that was asked about: exactly the documented members at
// every level, exact types, closed vocabularies, canonical identifiers, times and fingerprints, and the structural rules the host itself
// proves. Anything else is null (or `incoherent`) and is never repaired, trimmed, coerced into a known state or partially accepted. A
// recorded status, outcome or exit code is bookkeeping: never proof of a physical exit, a successful verification, a run, or that a
// source is current or approved.

/** The fixed page size of the history, and so the most items one answer may carry. */
export const PROJECT_VERIFICATION_PAGE_SIZE = 20

const INT32_MAX = 2 ** 31 - 1
const MAX_COMMAND_NAME_UNITS = 200

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const SHA256 = /^[0-9a-f]{64}$/

const isGuid = (value: unknown): value is string =>
  typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isSha256 = (value: unknown): value is string => typeof value === 'string' && SHA256.test(value)
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)
const isCommandName = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_COMMAND_NAME_UNITS

export const PROJECT_VERIFICATION_STATUSES = [
  'Running',
  'Passed',
  'Failed',
  'TimedOut',
  'Cancelled',
  'Interrupted',
  'SourceChanged',
  'LocalExecutionSequenceDispatchRefused',
] as const
export type ProjectVerificationStatus = (typeof PROJECT_VERIFICATION_STATUSES)[number]

export const PROJECT_VERIFICATION_OUTCOMES = ['Exited', 'TimedOut', 'Cancelled', 'Failed'] as const
export type ProjectVerificationOutcome = (typeof PROJECT_VERIFICATION_OUTCOMES)[number]

/** One listed execution: its exact identity, number, the snapshot name and the claim time; nothing here says what it did. */
export interface ProjectVerificationHistoryItem {
  executionId: string
  executionNumber: number
  /** The command name of the execution's immutable snapshot, never the current recipe. */
  commandName: string
  /** The validated invariant UTC round-trip text, exactly as the host wrote it. */
  claimedAtUtc: string
}

export type ProjectVerificationHistory =
  // A selected row or the lookahead key could not be admitted: no item and no cursor, which is not an assertion that history is exhausted.
  | { status: 'Unavailable' }
  | {
      status: 'Recorded'
      /** At most 20, in strictly descending execution-number order. */
      items: readonly ProjectVerificationHistoryItem[]
      hasMore: boolean
      /** The last displayed number exactly when `hasMore`. */
      nextBeforeExecutionNumber: number | null
    }

/** What the page read asked for: the project and the cursor (null: the newest page). */
export interface ProjectVerificationHistoryExpectation {
  projectId: string
  beforeExecutionNumber: number | null
}

const HISTORY_KEYS = ['projectId', 'status', 'items', 'hasMore', 'nextBeforeExecutionNumber'] as const
const ITEM_KEYS = ['executionId', 'executionNumber', 'commandName', 'claimedAtUtc'] as const

function toHistoryItem(raw: unknown): ProjectVerificationHistoryItem | null {
  if (!hasExactKeys(raw, ITEM_KEYS)) {
    return null
  }

  const { executionId, executionNumber, commandName, claimedAtUtc } = raw
  return isGuid(executionId) && isInt(executionNumber, 1, INT32_MAX) && isCommandName(commandName) && isUtcRoundTrip(claimedAtUtc)
    ? { executionId, executionNumber, commandName, claimedAtUtc }
    : null
}

/**
 * Accepts one parsed history page only when the whole of it is coherent for exactly `expected`: the requested project, a closed status
 * (`Unavailable`: no item, no further page and no cursor; `Recorded`: at most 20 items with unique identities and canonical facts, in
 * strictly descending positive Int32 number order and below the requested cursor, and a cursor that is the last displayed number
 * exactly when older executions exist, which a page that is not full or ends at number 1 can never claim). Anything else is null.
 */
export function toProjectVerificationHistory(
  raw: unknown,
  expected: ProjectVerificationHistoryExpectation,
): ProjectVerificationHistory | null {
  if (!hasExactKeys(raw, HISTORY_KEYS) || raw.projectId !== expected.projectId || !Array.isArray(raw.items)) {
    return null
  }

  const { status, items, hasMore, nextBeforeExecutionNumber } = raw
  if (status === 'Unavailable') {
    return items.length === 0 && hasMore === false && nextBeforeExecutionNumber === null ? { status } : null
  }

  if (status !== 'Recorded' || typeof hasMore !== 'boolean' || items.length > PROJECT_VERIFICATION_PAGE_SIZE) {
    return null
  }

  const accepted: ProjectVerificationHistoryItem[] = []
  const identities = new Set<string>()
  let previous = expected.beforeExecutionNumber ?? INT32_MAX + 1
  for (const candidate of items) {
    const item = toHistoryItem(candidate)
    if (item === null || item.executionNumber >= previous || identities.has(item.executionId)) {
      return null
    }

    previous = item.executionNumber
    identities.add(item.executionId)
    accepted.push(item)
  }

  const last = accepted.at(-1)
  if (hasMore) {
    // An older execution can only exist behind a full page whose last number is above 1.
    return accepted.length === PROJECT_VERIFICATION_PAGE_SIZE
      && last !== undefined
      && last.executionNumber > 1
      && nextBeforeExecutionNumber === last.executionNumber
      ? { status, items: accepted, hasMore: true, nextBeforeExecutionNumber: last.executionNumber }
      : null
  }

  return nextBeforeExecutionNumber === null ? { status, items: accepted, hasMore: false, nextBeforeExecutionNumber: null } : null
}

/** The recorded facts of one execution as the host read them now. Bookkeeping only: never proof of a physical exit or a verdict. */
export interface ProjectVerificationExecution {
  executionNumber: number
  commandId: string
  commandName: string
  workspaceId: string
  checkpointId: string
  checkpointNumber: number
  claimFingerprintSha256: string
  /** Null until completion; may legitimately differ from the claim fingerprint for a SourceChanged execution. */
  completionFingerprintSha256: string | null
  status: ProjectVerificationStatus
  outcome: ProjectVerificationOutcome | null
  exitCode: number | null
  claimedAtUtc: string
  dispatchedAtUtc: string | null
  completedAtUtc: string | null
  standardOutput: VerificationStreamDescriptor
  standardError: VerificationStreamDescriptor
}

export type ProjectVerificationInspection =
  // The execution belongs to the project but its recorded facts are incoherent; which fact is not disclosed.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; execution: ProjectVerificationExecution }

/** The listed execution a selected answer must name: the project and the indexed identity, number, snapshot name and claim time. */
export interface ProjectVerificationInspectionExpectation {
  projectId: string
  item: ProjectVerificationHistoryItem
}

const INSPECTION_KEYS = ['projectId', 'executionId', 'status', 'execution'] as const
const EXECUTION_KEYS = [
  'executionNumber',
  'commandId',
  'commandName',
  'workspaceId',
  'checkpointId',
  'checkpointNumber',
  'claimFingerprintSha256',
  'completionFingerprintSha256',
  'status',
  'outcome',
  'exitCode',
  'claimedAtUtc',
  'dispatchedAtUtc',
  'completedAtUtc',
  'standardOutput',
  'standardError',
] as const

interface ResultTuple {
  status: ProjectVerificationStatus
  outcome: ProjectVerificationOutcome | null
  exitCode: number | null
  claimFingerprint: string
  completionFingerprint: string | null
  claimedAt: string
  dispatchedAt: string | null
  completedAt: string | null
}

// The shapes the Domain transitions can produce, exactly as the host proves them; any other combination is contradictory. The times are
// the canonical fixed-width round-trip texts, so comparing the texts compares the instants at tick precision.
function isCoherentTuple(tuple: ResultTuple): boolean {
  const { status, outcome, exitCode, claimFingerprint, completionFingerprint, claimedAt, dispatchedAt, completedAt } = tuple
  if ((dispatchedAt !== null && dispatchedAt < claimedAt) || (completedAt !== null && completedAt < (dispatchedAt ?? claimedAt))) {
    return false
  }

  const dispatched = dispatchedAt !== null
  const completed = completedAt !== null
  const hasCompletionFingerprint = completionFingerprint !== null
  const same = hasCompletionFingerprint && completionFingerprint === claimFingerprint
  switch (status) {
    case 'Running':
      return !completed && outcome === null && exitCode === null && !hasCompletionFingerprint
    case 'Passed':
      return dispatched && completed && same && outcome === 'Exited' && exitCode === 0
    case 'Failed':
      return dispatched && completed && same && ((outcome === 'Exited' && exitCode !== null && exitCode !== 0) || (outcome === 'Failed' && exitCode === null))
    case 'TimedOut':
      return dispatched && completed && same && outcome === 'TimedOut' && exitCode === null
    case 'Cancelled':
      return dispatched && completed && same && outcome === 'Cancelled' && exitCode === null
    case 'Interrupted':
      return completed && outcome === null && exitCode === null && !hasCompletionFingerprint
    case 'SourceChanged':
      // Invalidated before dispatch (no process outcome) or a dispatched process that completed against a different source.
      return completed
        && hasCompletionFingerprint
        && !same
        && (dispatched ? outcome !== null && (outcome === 'Exited') === (exitCode !== null) : outcome === null && exitCode === null)
    default:
      // The dispatch refusal: terminal before any dispatch, with no process outcome, exit code or completion fingerprint.
      return !dispatched && completed && outcome === null && exitCode === null && !hasCompletionFingerprint
  }
}

function toStreams(raw: Record<string, unknown>): [VerificationStreamDescriptor, VerificationStreamDescriptor] | null {
  const standardOutput = toVerificationStreamDescriptor(raw.standardOutput)
  const standardError = toVerificationStreamDescriptor(raw.standardError)
  if (standardOutput === null || standardError === null) {
    return null
  }

  const artifacts = [standardOutput, standardError].flatMap((descriptor) => (descriptor.status === 'Recorded' ? [descriptor.artifactId] : []))
  return artifacts.every(isGuid) && new Set(artifacts).size === artifacts.length ? [standardOutput, standardError] : null
}

function toExecution(raw: unknown, expected: ProjectVerificationInspectionExpectation): ProjectVerificationExecution | null {
  if (!hasExactKeys(raw, EXECUTION_KEYS)) {
    return null
  }

  const streams = toStreams(raw)
  const { item } = expected
  if (
    streams === null
    // The immutable facts the page indexed: a changed one is an unavailable read, never a silent replacement.
    || raw.executionNumber !== item.executionNumber
    || raw.commandName !== item.commandName
    || raw.claimedAtUtc !== item.claimedAtUtc
    || !isGuid(raw.commandId)
    || !isGuid(raw.workspaceId)
    || !isGuid(raw.checkpointId)
    || !isInt(raw.checkpointNumber, 1, INT32_MAX)
    || !isSha256(raw.claimFingerprintSha256)
    || !(raw.completionFingerprintSha256 === null || isSha256(raw.completionFingerprintSha256))
    || !isOneOf(PROJECT_VERIFICATION_STATUSES, raw.status)
    || !(raw.outcome === null || isOneOf(PROJECT_VERIFICATION_OUTCOMES, raw.outcome))
    || !(raw.exitCode === null || isInt(raw.exitCode, -(2 ** 31), INT32_MAX))
    || !(raw.dispatchedAtUtc === null || isUtcRoundTrip(raw.dispatchedAtUtc))
    || !(raw.completedAtUtc === null || isUtcRoundTrip(raw.completedAtUtc))
  ) {
    return null
  }

  const tuple: ResultTuple = {
    status: raw.status,
    outcome: raw.outcome,
    exitCode: raw.exitCode,
    claimFingerprint: raw.claimFingerprintSha256,
    completionFingerprint: raw.completionFingerprintSha256,
    claimedAt: item.claimedAtUtc,
    dispatchedAt: raw.dispatchedAtUtc,
    completedAt: raw.completedAtUtc,
  }
  if (!isCoherentTuple(tuple)) {
    return null
  }

  return {
    executionNumber: item.executionNumber,
    commandId: raw.commandId,
    commandName: item.commandName,
    workspaceId: raw.workspaceId,
    checkpointId: raw.checkpointId,
    checkpointNumber: raw.checkpointNumber,
    claimFingerprintSha256: raw.claimFingerprintSha256,
    completionFingerprintSha256: raw.completionFingerprintSha256,
    status: raw.status,
    outcome: raw.outcome,
    exitCode: raw.exitCode,
    claimedAtUtc: item.claimedAtUtc,
    dispatchedAtUtc: raw.dispatchedAtUtc,
    completedAtUtc: raw.completedAtUtc,
    standardOutput: streams[0],
    standardError: streams[1],
  }
}

/**
 * Accepts one parsed selected-execution answer only when the whole of it is coherent for exactly `expected`: the requested project and
 * the listed execution's identity, a closed status (`Unavailable`: no execution; `Recorded`: closed execution facts whose number, snapshot
 * name and claim time equal the indexed ones, canonical identities, a positive checkpoint number, lowercase 64-hex fingerprints, a
 * status/outcome/exit-code/fingerprint/time tuple the Domain can produce, and exactly two descriptors of unique artifacts). Anything
 * else is null and is never repaired or partially accepted.
 */
export function toProjectVerificationInspection(
  raw: unknown,
  expected: ProjectVerificationInspectionExpectation,
): ProjectVerificationInspection | null {
  if (!hasExactKeys(raw, INSPECTION_KEYS) || raw.projectId !== expected.projectId || raw.executionId !== expected.item.executionId) {
    return null
  }

  if (raw.status === 'Unavailable') {
    return raw.execution === null ? { status: 'Unavailable' } : null
  }

  if (raw.status !== 'Recorded') {
    return null
  }

  const execution = toExecution(raw.execution, expected)
  return execution === null ? null : { status: 'Recorded', execution }
}

/** The Recorded descriptor of one stream of an accepted answer, or null for anything else. */
export function recordedDescriptorOf(
  inspection: ProjectVerificationInspection | null,
  stream: VerificationStream,
): RecordedVerificationStreamDescriptor | null {
  if (inspection?.status !== 'Recorded') {
    return null
  }

  const descriptor = stream === 'stdout' ? inspection.execution.standardOutput : inspection.execution.standardError
  return descriptor.status === 'Recorded' ? descriptor : null
}

/** What one window read asked for: the execution's stream and its Recorded descriptor from the accepted metadata, the byte window. */
export interface ProjectVerificationWindowRequestFacts {
  projectId: string
  executionId: string
  stream: VerificationStream
  descriptor: RecordedVerificationStreamDescriptor
  fromOffset: number
  maxBytes: number
}

const WINDOW_KEYS = ['projectId', 'executionId', 'stream', 'status', 'artifact', 'text', 'nextOffset', 'totalLength'] as const
const INCOHERENT: VerificationWindowAnswer = { kind: 'incoherent' }

/**
 * Decides one window answer for exactly `request`: the requested project, execution and canonical stream, a closed status, and, by
 * status, a whole shape. A non-`Ok` status carries no artifact, empty text, the requested offset unchanged and a total length of 0. An
 * `Ok` window carries the very descriptor the selection was made from, a total length equal to that descriptor's byte length, a cursor
 * with `fromOffset <= nextOffset <= totalLength` that advances by at most the requested cap and by at least one byte while bytes
 * remain, and text that is empty exactly when the cursor did not advance. The cursor counts UTF-8 bytes of the sealed file and the host
 * decodes malformed UTF-8 with replacement and defers a split code point to the next window, so the text is never re-encoded: only two
 * facts that hold for every UTF-8 decoding are checked, that a positive advance decodes to some text and that no UTF-16 code unit is
 * produced without consuming at least one byte (`text.length <= advance`).
 */
export function decideProjectVerificationWindowAnswer(
  raw: unknown,
  request: ProjectVerificationWindowRequestFacts,
): VerificationWindowAnswer {
  if (
    !hasExactKeys(raw, WINDOW_KEYS)
    || raw.projectId !== request.projectId
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
    const served = toVerificationStreamDescriptor(artifact)
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
