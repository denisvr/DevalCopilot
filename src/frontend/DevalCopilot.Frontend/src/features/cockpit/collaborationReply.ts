import type { GetCollaborationHistoryReplyResponse } from '../../api/clients'
import { KNOWN_PROTOCOL_VERSION, UNRECOGNIZED, toCollaborationHistoryEntry } from './collaborationHistory'
import type { CollaborationHistoryEntry, KnownMessageType } from './collaborationHistory'

/** The closed relationship states the host reports for one message's recorded reply parent. */
export type CollaborationReplyStatus = 'None' | 'Unavailable' | 'Available'

/** The observed facts of the one history row whose recorded reply is requested; every decoded answer is checked against them. */
export interface CollaborationReplySource {
  runId: string
  messageId: string
  /** The exact canonical Int64 decimal text of the source as the validated history page showed it. */
  sequence: string
  /** The recorded type and protocol version of the source (or the fixed `Unrecognized`). */
  type: CollaborationHistoryEntry['type']
  protocolVersion: CollaborationHistoryEntry['protocolVersion']
}

/** The recorded relationship only. An Available parent keeps its own, separate content status. */
export type CollaborationReply =
  | { status: 'None' }
  | { status: 'Unavailable' }
  | { status: 'Available'; parent: CollaborationHistoryEntry }

/** The version-1.0 reply table, restated for this view: the parent types a message of each type may reply to. */
const ALLOWED_PARENTS: Record<KnownMessageType, readonly KnownMessageType[]> = {
  Proposal: ['Proposal'],
  Acceptance: ['Proposal'],
  Challenge: ['Proposal'],
  Decision: ['Proposal', 'Challenge'],
  ExecutionReport: ['Decision', 'Proposal'],
  ReviewFinding: ['ExecutionReport'],
  ReviewApproval: ['ExecutionReport'],
  RevisionResponse: ['ReviewFinding'],
  Question: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding'],
  Escalation: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding', 'RevisionResponse', 'Question'],
  HumanInstruction: ['Escalation'],
}

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/

const absent = (value: unknown) => value === null || value === undefined

function exactSequence(value: unknown): bigint | null {
  if (typeof value !== 'string' || !CANONICAL_SEQUENCE.test(value)) {
    return null
  }

  const parsed = BigInt(value)
  return parsed <= MAX_SEQUENCE ? parsed : null
}

const isRecognizedSource = (source: CollaborationReplySource): source is CollaborationReplySource & { type: KnownMessageType } =>
  source.protocolVersion === KNOWN_PROTOCOL_VERSION && source.type !== UNRECOGNIZED

/**
 * Accepts a host answer only when the whole of it is coherent for exactly `source`: the answered run and message, the observed exact
 * sequence text, a closed status and a target shape that agrees with it (a parent only for Available), and for a recognized source
 * a relationship that its own type permits: None only for a Proposal, and an Available parent that is a distinct, recognized
 * version-1.0 message of a type the reply table allows, recorded strictly earlier by exact integer order. An unrecognized source
 * can only be Unavailable. The parent itself is decoded by the unchanged recorded-message rules, so its content may be Unavailable
 * independently of the relationship. Anything else is null and is never repaired or partially accepted.
 */
export function toCollaborationReply(
  response: GetCollaborationHistoryReplyResponse | null | undefined,
  source: CollaborationReplySource,
): CollaborationReply | null {
  const sourceSequence = exactSequence(source.sequence)
  if (
    !response
    || typeof response !== 'object'
    || sourceSequence === null
    || response.runId !== source.runId
    || response.messageId !== source.messageId
    || response.sourceSequence !== source.sequence
  ) {
    return null
  }

  switch (response.status) {
    case 'None':
      return absent(response.parent) && isRecognizedSource(source) && source.type === 'Proposal' ? { status: 'None' } : null
    case 'Unavailable':
      return absent(response.parent) ? { status: 'Unavailable' } : null
    case 'Available': {
      if (absent(response.parent) || !isRecognizedSource(source)) {
        return null
      }

      const decoded = toCollaborationHistoryEntry(response.parent, source.runId)
      if (decoded === null) {
        return null
      }

      const { entry, sequence } = decoded
      const parentType = entry.type
      return entry.id !== source.messageId
        && entry.protocolVersion === KNOWN_PROTOCOL_VERSION
        && parentType !== UNRECOGNIZED
        && ALLOWED_PARENTS[source.type].includes(parentType)
        && sequence < sourceSequence
        ? { status: 'Available', parent: entry }
        : null
    }
    default:
      return null
  }
}
