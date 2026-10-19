import { KNOWN_PROTOCOL_VERSION, toCollaborationHistoryEntry } from './collaborationHistory'
import type { CollaborationHistoryEntry, CollaborationParticipant, CollaborationProvenance, KnownMessageType } from './collaborationHistory'
import { toWireInstant } from './agentAttemptEvidenceWire'
import {
  OUTPUT_REFERENCE_KEYS,
  UTC_ROUND_TRIP,
  hasExactKeys,
  isSelectedOutputHeader,
} from './agentAttemptOutputs'
import type { AgentAttemptOutputReference, AgentAttemptOutputsExpectation } from './agentAttemptOutputs'

/** One recorded collaboration message of a selected output, already proven coherent for exactly that output. */
export interface RecordedOutputMessage {
  id: string
  /** The exact canonical Int64 decimal text, equal to the selected output's collaboration sequence; never a Number. */
  sequence: string
  /** The validated invariant UTC round-trip text exactly as the host wrote it, equal to the selected output's time; never a Date. */
  occurredAtUtc: string
  protocolVersion: typeof KNOWN_PROTOCOL_VERSION
  type: KnownMessageType
  provenance: CollaborationProvenance
  actor: CollaborationParticipant
  recipient: CollaborationParticipant
  /** Available content carries the summary and the closed schema's fields in schema order with their fixed labels. */
  content: CollaborationHistoryEntry['content']
}

export type AgentAttemptOutputMessage =
  // The output set, its identity or the reread association could not be proved; which one is not disclosed, and no part of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; output: AgentAttemptOutputReference; message: RecordedOutputMessage }

/** The selected attempt and the one accepted reference of its current opening that the answer must name exactly. */
export interface AgentAttemptOutputMessageExpectation extends AgentAttemptOutputsExpectation {
  reference: AgentAttemptOutputReference
}

const ROOT_KEYS = ['attempt', 'attemptId', 'collaborationMessageId', 'message', 'output', 'runId', 'status']
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

/** The output must be exactly the selected reference: its position, identity, type, sequence text and time text. */
function isSelectedOutput(value: unknown, reference: AgentAttemptOutputReference): boolean {
  return hasExactKeys(value, OUTPUT_REFERENCE_KEYS)
    && value.position === reference.position
    && value.collaborationMessageId === reference.collaborationMessageId
    && value.type === reference.type
    && value.collaborationSequence === reference.collaborationSequence
    && value.occurredAtUtc === reference.occurredAtUtc
}

function toMessage(value: unknown, expected: AgentAttemptOutputMessageExpectation): RecordedOutputMessage | null {
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

  // The host proves provider-observed provenance and an Agent actor that is exactly the attempt; a recorded answer that says otherwise
  // is not coherent for this selection, whatever its content.
  const { entry } = decoded
  const { actor } = entry
  if (
    entry.provenance !== 'ProviderObserved'
    || actor.kind !== 'Agent'
    || actor.role !== expected.role
    || actor.provider !== expected.provider
  ) {
    return null
  }

  return {
    id: reference.collaborationMessageId,
    sequence: reference.collaborationSequence,
    occurredAtUtc: reference.occurredAtUtc,
    protocolVersion: KNOWN_PROTOCOL_VERSION,
    type: reference.type,
    provenance: entry.provenance,
    actor,
    recipient: entry.recipient,
    content: entry.content,
  }
}

/**
 * Accepts a parsed host answer only when the whole of it is coherent for exactly `expected` (ADR-0054): exactly the documented members
 * at every level, the requested run, attempt and message, and a closed status with exactly its shape. `Unavailable` carries no attempt,
 * output or message. `Recorded` carries the selected attempt's immutable header, the selected reference itself (position, identity,
 * type, Int64 sequence text and UTC round-trip time text, all compared as the host wrote them) and that message, whose identity,
 * sequence, time, version and type equal the reference, whose provenance is provider-observed and whose actor is exactly the selected
 * attempt, and whose participants and content pass the unchanged history validation: unavailable content is bare, available content is
 * complete against the closed schema within its bounds. Anything else is null and is never repaired, trimmed or partially accepted.
 */
export function toAgentAttemptOutputMessage(raw: unknown, expected: AgentAttemptOutputMessageExpectation): AgentAttemptOutputMessage | null {
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
      return raw.attempt === null && raw.output === null && raw.message === null ? { status: 'Unavailable' } : null
    case 'Recorded': {
      if (!isSelectedOutputHeader(raw.attempt, expected) || !isSelectedOutput(raw.output, expected.reference)) {
        return null
      }

      const message = toMessage(raw.message, expected)
      return message === null ? null : { status: 'Recorded', output: expected.reference, message }
    }
    default:
      return null
  }
}
