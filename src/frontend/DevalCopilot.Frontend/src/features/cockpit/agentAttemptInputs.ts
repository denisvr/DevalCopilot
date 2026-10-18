import type { AgentAttemptIdentityExpectation } from './agentAttemptHistory'
import { toWireInstant } from './agentAttemptEvidenceWire'

/** The most references the host shows and the most inputs it inspects (ADR-0051). */
export const AGENT_ATTEMPT_INPUTS_SHOWN = 10
export const AGENT_ATTEMPT_INPUTS_INSPECTED = 64

export type RecordedInputMessageType =
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
  | 'HumanInstruction'

/** One recorded input reference; never the referenced message's content. */
export interface AgentAttemptInputReference {
  /** The zero-based recorded input position. */
  position: number
  collaborationMessageId: string
  type: RecordedInputMessageType
  /** The referenced message's exact canonical Int64 timeline sequence, kept as text and never converted to a number. */
  collaborationSequence: string
  /** The validated invariant UTC round-trip text exactly as the host wrote it. */
  occurredAtUtc: string
}

export type AgentAttemptInputs =
  // The identity, the size or the coherence of the set could not be proved; which one is not disclosed, and it is not an empty set.
  | { status: 'Unavailable' }
  // A Planner attempt that recorded no input.
  | { status: 'Empty' }
  | { status: 'Recorded'; totalCount: number; omitted: boolean; inputs: readonly AgentAttemptInputReference[] }

/** The selected attempt the answer must name: its run and the accepted history row's identity, number, role, provider and contract. */
export interface AgentAttemptInputsExpectation extends AgentAttemptIdentityExpectation {
  runId: string
}

const KNOWN_TYPES: readonly string[] = [
  'Proposal',
  'Acceptance',
  'Challenge',
  'Question',
  'Decision',
  'ExecutionReport',
  'ReviewFinding',
  'RevisionResponse',
  'Escalation',
  'ReviewApproval',
  'HumanInstruction',
]

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/
// The host writes `DateTime.ToString("O")` of a UTC instant: seven fractional digits and `Z`, nothing else.
const UTC_ROUND_TRIP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0

const isExactSequence = (value: unknown): value is string =>
  typeof value === 'string' && CANONICAL_SEQUENCE.test(value) && BigInt(value) <= MAX_SEQUENCE

const isUtcRoundTrip = (value: unknown): value is string =>
  typeof value === 'string' && UTC_ROUND_TRIP.test(value) && toWireInstant(value) !== null

/** The header must be exactly the selected attempt: the same identity and number and the same immutable role, provider and contract. */
function isSelectedHeader(value: unknown, expected: AgentAttemptInputsExpectation): boolean {
  return isRecord(value)
    && value.id === expected.attemptId
    && value.attemptNumber === expected.attemptNumber
    && value.role === expected.role
    && value.provider === expected.provider
    && value.responseContract === expected.responseContract
}

function toReferences(raw: unknown[], totalCount: number): AgentAttemptInputReference[] | null {
  if (raw.length !== Math.min(totalCount, AGENT_ATTEMPT_INPUTS_SHOWN)) {
    return null
  }

  const identities = new Set<string>()
  const sequences = new Set<string>()
  const references: AgentAttemptInputReference[] = []
  for (const [position, item] of raw.entries()) {
    if (
      !isRecord(item)
      || item.sequence !== position
      || !isText(item.collaborationMessageId)
      || identities.has(item.collaborationMessageId)
      || typeof item.type !== 'string'
      || !KNOWN_TYPES.includes(item.type)
      || !isExactSequence(item.collaborationSequence)
      || sequences.has(item.collaborationSequence)
      || !isUtcRoundTrip(item.occurredAtUtc)
    ) {
      return null
    }

    identities.add(item.collaborationMessageId)
    sequences.add(item.collaborationSequence)
    references.push({
      position,
      collaborationMessageId: item.collaborationMessageId,
      type: item.type as RecordedInputMessageType,
      collaborationSequence: item.collaborationSequence,
      occurredAtUtc: item.occurredAtUtc,
    })
  }

  return references
}

/**
 * Accepts a parsed host answer only when the whole of it is coherent for exactly `expected`: the requested run and attempt, a closed
 * status with exactly its shape (`Unavailable`: no attempt, count or reference and not omitted; `Empty`: the selected Planner attempt,
 * count 0, no reference and not omitted; `Recorded`: the selected attempt, a whole count from 1 to 64, its first at most 10 references
 * and omitted exactly when the count exceeds them), and references at their own ordered positions from zero with unique identities and
 * sequences, a recognized type, an exact canonical positive Int64 sequence text and a valid UTC round-trip time text. Anything else is
 * null and is never repaired, trimmed or partially accepted.
 */
export function toAgentAttemptInputs(raw: unknown, expected: AgentAttemptInputsExpectation): AgentAttemptInputs | null {
  if (
    !isRecord(raw)
    || raw.runId !== expected.runId
    || raw.attemptId !== expected.attemptId
    || !Array.isArray(raw.inputMessages)
    || typeof raw.inputMessagesOmitted !== 'boolean'
  ) {
    return null
  }

  const { attempt, inputMessages, inputMessageTotalCount: totalCount, inputMessagesOmitted: omitted } = raw
  switch (raw.status) {
    case 'Unavailable':
      return attempt === null && totalCount === null && inputMessages.length === 0 && !omitted ? { status: 'Unavailable' } : null
    case 'Empty':
      return expected.role === 'Planner'
        && isSelectedHeader(attempt, expected)
        && totalCount === 0
        && inputMessages.length === 0
        && !omitted
        ? { status: 'Empty' }
        : null
    case 'Recorded': {
      if (
        !isSelectedHeader(attempt, expected)
        || typeof totalCount !== 'number'
        || !Number.isInteger(totalCount)
        || totalCount < 1
        || totalCount > AGENT_ATTEMPT_INPUTS_INSPECTED
        || omitted !== totalCount > AGENT_ATTEMPT_INPUTS_SHOWN
      ) {
        return null
      }

      const inputs = toReferences(inputMessages, totalCount)
      return inputs === null ? null : { status: 'Recorded', totalCount, omitted, inputs }
    }
    default:
      return null
  }
}
