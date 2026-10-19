import type { AgentAttemptIdentityExpectation, AgentAttemptRole } from './agentAttemptHistory'
import { toWireInstant } from './agentAttemptEvidenceWire'

/** The most output references the host proves and returns (ADR-0054); a larger set is Unavailable, never a prefix. */
export const AGENT_ATTEMPT_OUTPUTS_INSPECTED = 64

/** How many loaded references the inspector reveals first; further local groups of this size are revealed on request. */
export const AGENT_ATTEMPT_OUTPUTS_SHOWN = 10

/** The caveat every recorded-output view states: stored messages attributed to the attempt, never its complete response. */
export const RECORDED_OUTPUTS_CAVEAT =
  'Recorded collaboration outputs — the stored collaboration messages attributed to this attempt, as read now. They are not its '
  + 'complete provider response, not proof of what it generated or saw, and neither a current approval nor proof that it succeeded.'

export type RecordedOutputMessageType =
  | 'Proposal'
  | 'Acceptance'
  | 'Challenge'
  | 'Question'
  | 'Decision'
  | 'ExecutionReport'
  | 'ReviewFinding'
  | 'RevisionResponse'
  | 'Escalation'
  | 'ReviewApproval'

/** One recorded output reference; never the message's content. */
export interface AgentAttemptOutputReference {
  /** The zero-based ordinal in the read's sequence order; a position of this read, not a persisted sequence. */
  position: number
  collaborationMessageId: string
  type: RecordedOutputMessageType
  /** The message's exact canonical Int64 timeline sequence, kept as text and never converted to a number. */
  collaborationSequence: string
  /** The validated invariant UTC round-trip text exactly as the host wrote it. */
  occurredAtUtc: string
}

export type AgentAttemptOutputs =
  // The identity, the size or the coherence of the association set could not be proved; which one is not disclosed, and it is not an
  // empty set.
  | { status: 'Unavailable' }
  // No associated message was recorded in this read: not proof that the provider produced nothing.
  | { status: 'Empty' }
  | { status: 'Recorded'; totalCount: number; outputs: readonly AgentAttemptOutputReference[] }

/** The selected attempt the answer must name: its run and the accepted history row's identity, number, role, provider and contract. */
export interface AgentAttemptOutputsExpectation extends AgentAttemptIdentityExpectation {
  runId: string
}

// The Domain `CollaborationMessageAuthorPolicy` restated for this view; admitting a new role or type requires both to learn it together.
const AUTHORED_TYPES: Record<AgentAttemptRole, readonly string[]> = {
  Planner: ['Proposal', 'Question', 'Escalation'],
  CriticalReviewer: ['Acceptance', 'Challenge', 'Question', 'Escalation'],
  Resolver: ['Decision', 'Proposal', 'Question', 'Escalation'],
  Implementer: ['ExecutionReport', 'RevisionResponse', 'Question', 'Escalation'],
  CodeReviewer: ['ReviewApproval', 'ReviewFinding', 'Question', 'Escalation'],
}

const ROOT_KEYS = ['attempt', 'attemptId', 'outputMessageTotalCount', 'outputMessages', 'runId', 'status']
export const OUTPUT_HEADER_KEYS = ['attemptNumber', 'id', 'provider', 'responseContract', 'role']
export const OUTPUT_REFERENCE_KEYS = ['collaborationMessageId', 'collaborationSequence', 'occurredAtUtc', 'position', 'type']

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/
// The host writes `DateTime.ToString("O")` of a UTC instant: seven fractional digits and `Z`, nothing else.
export const UTC_ROUND_TRIP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0

/** Exactly these own keys, no more and no fewer: an unknown member is never ignored. */
export function hasExactKeys(value: unknown, keys: readonly string[]): value is Record<string, unknown> {
  if (!isRecord(value)) {
    return false
  }

  const own = Object.keys(value).sort()
  return own.length === keys.length && own.every((key, index) => key === keys[index])
}

const isExactSequence = (value: unknown): value is string =>
  typeof value === 'string' && CANONICAL_SEQUENCE.test(value) && BigInt(value) <= MAX_SEQUENCE

/** Valid UTC round-trip text: the exact shape and an instant the unchanged wire conversion accepts. */
export const isUtcRoundTrip = (value: unknown): value is string =>
  typeof value === 'string' && UTC_ROUND_TRIP.test(value) && toWireInstant(value) !== null

/** The header must be exactly the selected attempt: the same identity and number and the same immutable role, provider and contract. */
export function isSelectedOutputHeader(value: unknown, expected: AgentAttemptOutputsExpectation): boolean {
  return hasExactKeys(value, OUTPUT_HEADER_KEYS)
    && value.id === expected.attemptId
    && value.attemptNumber === expected.attemptNumber
    && value.role === expected.role
    && value.provider === expected.provider
    && value.responseContract === expected.responseContract
}

/** Whether the role of the selected attempt may author the message type (the table above); an unknown role authors nothing. */
export const isAuthoredBy = (role: AgentAttemptRole, type: unknown): boolean =>
  Object.hasOwn(AUTHORED_TYPES, role) && typeof type === 'string' && AUTHORED_TYPES[role].includes(type)

function toReferences(raw: unknown[], expected: AgentAttemptOutputsExpectation): AgentAttemptOutputReference[] | null {
  const identities = new Set<string>()
  const references: AgentAttemptOutputReference[] = []
  let previous = 0n
  for (const [position, item] of raw.entries()) {
    if (
      !hasExactKeys(item, OUTPUT_REFERENCE_KEYS)
      || item.position !== position
      || !isText(item.collaborationMessageId)
      || identities.has(item.collaborationMessageId)
      || !isAuthoredBy(expected.role, item.type)
      || !isExactSequence(item.collaborationSequence)
      || BigInt(item.collaborationSequence) <= previous
      || !isUtcRoundTrip(item.occurredAtUtc)
    ) {
      return null
    }

    identities.add(item.collaborationMessageId)
    previous = BigInt(item.collaborationSequence)
    references.push({
      position,
      collaborationMessageId: item.collaborationMessageId,
      type: item.type as RecordedOutputMessageType,
      collaborationSequence: item.collaborationSequence,
      occurredAtUtc: item.occurredAtUtc,
    })
  }

  return references
}

/**
 * Accepts a parsed host answer only when the whole of it is coherent for exactly `expected` (ADR-0054): exactly the documented members
 * at every level, the requested run and attempt, and a closed status with exactly its shape (`Unavailable`: no attempt or count and no
 * reference; `Empty`: the selected attempt, count 0 and no reference; `Recorded`: the selected attempt, a whole count from 1 to 64 and
 * exactly that many references, with no omission). References sit at their own ordered positions from zero with unique identities, a
 * type the selected role may author, strictly increasing exact canonical positive Int64 sequence texts and valid UTC round-trip time
 * texts. Anything else is null and is never repaired, trimmed or partially accepted.
 */
export function toAgentAttemptOutputs(raw: unknown, expected: AgentAttemptOutputsExpectation): AgentAttemptOutputs | null {
  if (
    !hasExactKeys(raw, ROOT_KEYS)
    || raw.runId !== expected.runId
    || raw.attemptId !== expected.attemptId
    || !Array.isArray(raw.outputMessages)
  ) {
    return null
  }

  const { attempt, outputMessages, outputMessageTotalCount: totalCount } = raw
  switch (raw.status) {
    case 'Unavailable':
      return attempt === null && totalCount === null && outputMessages.length === 0 ? { status: 'Unavailable' } : null
    case 'Empty':
      return isSelectedOutputHeader(attempt, expected) && totalCount === 0 && outputMessages.length === 0 ? { status: 'Empty' } : null
    case 'Recorded': {
      if (
        !isSelectedOutputHeader(attempt, expected)
        || typeof totalCount !== 'number'
        || !Number.isInteger(totalCount)
        || totalCount < 1
        || totalCount > AGENT_ATTEMPT_OUTPUTS_INSPECTED
        || outputMessages.length !== totalCount
      ) {
        return null
      }

      const outputs = toReferences(outputMessages, expected)
      return outputs === null ? null : { status: 'Recorded', totalCount, outputs }
    }
    default:
      return null
  }
}
