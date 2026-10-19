import type { AgentAttemptInputReference, AgentAttemptInputsExpectation } from './agentAttemptInputs'
import { toWireInstant } from './agentAttemptEvidenceWire'
import { KNOWN_PROTOCOL_VERSION, toCollaborationHistoryEntry } from './collaborationHistory'
import type { CollaborationHistoryEntry, CollaborationParticipant, CollaborationProvenance, KnownMessageType } from './collaborationHistory'

/** One recorded collaboration message of a selected input, already proven coherent for exactly that input. */
export interface RecordedInputMessage {
  id: string
  /** The exact canonical Int64 decimal text, equal to the selected input's collaboration sequence; never a Number. */
  sequence: string
  /** The validated invariant UTC round-trip text exactly as the host wrote it, equal to the selected input's time; never a Date. */
  occurredAtUtc: string
  protocolVersion: typeof KNOWN_PROTOCOL_VERSION
  type: KnownMessageType
  provenance: CollaborationProvenance
  actor: CollaborationParticipant
  recipient: CollaborationParticipant
  /** Available content carries the summary and the closed schema's fields in schema order with their fixed labels. */
  content: CollaborationHistoryEntry['content']
}

export type AgentAttemptInputMessage =
  // The input set, its identity or the reread header could not be proved; which one is not disclosed, and no part of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; input: AgentAttemptInputReference; message: RecordedInputMessage }

/** The selected attempt and the one accepted reference of its current opening that the answer must name exactly. */
export interface AgentAttemptInputMessageExpectation extends AgentAttemptInputsExpectation {
  reference: AgentAttemptInputReference
}

const ROOT_KEYS = ['attempt', 'attemptId', 'collaborationMessageId', 'input', 'message', 'runId', 'status']
const HEADER_KEYS = ['attemptNumber', 'id', 'provider', 'responseContract', 'role']
const INPUT_KEYS = ['collaborationMessageId', 'collaborationSequence', 'occurredAtUtc', 'sequence', 'type']
const MESSAGE_KEYS = [
  'actor',
  'contentStatus',
  'fields',
  'id',
  'occurredAtUtc',
  'protocolVersion',
  'provenance',
  'recipient',
  'runId',
  'sequence',
  'summary',
  'type',
]
const PARTICIPANT_KEYS = ['kind', 'provider', 'role']
const FIELD_KEYS = ['name', 'value']

// The host writes `DateTime.ToString("O")` of a UTC instant: seven fractional digits and `Z`, nothing else.
const UTC_ROUND_TRIP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** Exactly these own keys, no more and no fewer: an unknown member is never ignored. */
function hasExactKeys(value: unknown, keys: readonly string[]): value is Record<string, unknown> {
  if (!isRecord(value)) {
    return false
  }

  const own = Object.keys(value).sort()
  return own.length === keys.length && own.every((key, index) => key === keys[index])
}

function isSelectedHeader(value: unknown, expected: AgentAttemptInputsExpectation): boolean {
  return hasExactKeys(value, HEADER_KEYS)
    && value.id === expected.attemptId
    && value.attemptNumber === expected.attemptNumber
    && value.role === expected.role
    && value.provider === expected.provider
    && value.responseContract === expected.responseContract
}

/** The input must be exactly the selected reference: its position, identity, type, sequence text and time text. */
function isSelectedInput(value: unknown, reference: AgentAttemptInputReference): boolean {
  return hasExactKeys(value, INPUT_KEYS)
    && value.sequence === reference.position
    && value.collaborationMessageId === reference.collaborationMessageId
    && value.type === reference.type
    && value.collaborationSequence === reference.collaborationSequence
    && value.occurredAtUtc === reference.occurredAtUtc
}

function toMessage(value: unknown, expected: AgentAttemptInputMessageExpectation): RecordedInputMessage | null {
  const { reference } = expected
  if (
    !hasExactKeys(value, MESSAGE_KEYS)
    || !hasExactKeys(value.actor, PARTICIPANT_KEYS)
    || !hasExactKeys(value.recipient, PARTICIPANT_KEYS)
    || !Array.isArray(value.fields)
    || !value.fields.every((field) => hasExactKeys(field, FIELD_KEYS))
    || value.runId !== expected.runId
    || value.id !== reference.collaborationMessageId
    || value.sequence !== reference.collaborationSequence
    || value.protocolVersion !== KNOWN_PROTOCOL_VERSION
    || value.type !== reference.type
    || typeof value.occurredAtUtc !== 'string'
    || !UTC_ROUND_TRIP.test(value.occurredAtUtc)
    || value.occurredAtUtc !== reference.occurredAtUtc
  ) {
    return null
  }

  // The raw time text is judged above; only then is it converted for the unchanged history content validation, which also judges the
  // participants, provenance, their coherence and the closed content schema with its bounds.
  const instant = toWireInstant(value.occurredAtUtc)
  const decoded = instant === null ? null : toCollaborationHistoryEntry({ ...value, occurredAtUtc: instant }, expected.runId)
  if (decoded === null) {
    return null
  }

  const { entry } = decoded
  return {
    id: reference.collaborationMessageId,
    sequence: reference.collaborationSequence,
    occurredAtUtc: reference.occurredAtUtc,
    protocolVersion: KNOWN_PROTOCOL_VERSION,
    type: reference.type,
    provenance: entry.provenance,
    actor: entry.actor,
    recipient: entry.recipient,
    content: entry.content,
  }
}

/**
 * Accepts a parsed host answer only when the whole of it is coherent for exactly `expected` (ADR-0053): exactly the documented members
 * at every level, the requested run, attempt and message, and a closed status with exactly its shape. `Unavailable` carries no attempt,
 * input or message. `Recorded` carries the selected attempt's immutable header, the selected reference itself (position, identity, type,
 * Int64 sequence text and UTC round-trip time text, all compared as the host wrote them) and that message, whose identity, sequence,
 * time, version and type equal the reference and whose participants, provenance and content pass the unchanged history validation:
 * unavailable content is bare, available content is complete against the closed schema within its bounds. Anything else is null and is
 * never repaired, trimmed or partially accepted.
 */
export function toAgentAttemptInputMessage(raw: unknown, expected: AgentAttemptInputMessageExpectation): AgentAttemptInputMessage | null {
  if (
    !hasExactKeys(raw, ROOT_KEYS)
    || raw.runId !== expected.runId
    || raw.attemptId !== expected.attemptId
    || raw.collaborationMessageId !== expected.reference.collaborationMessageId
  ) {
    return null
  }

  switch (raw.status) {
    case 'Unavailable':
      return raw.attempt === null && raw.input === null && raw.message === null ? { status: 'Unavailable' } : null
    case 'Recorded': {
      if (!isSelectedHeader(raw.attempt, expected) || !isSelectedInput(raw.input, expected.reference)) {
        return null
      }

      const message = toMessage(raw.message, expected)
      return message === null ? null : { status: 'Recorded', input: expected.reference, message }
    }
    default:
      return null
  }
}
