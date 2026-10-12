import {
  CollaborationAttemptHeaderResponse,
  CollaborationHistoryEntryResponse,
  CollaborationHistoryFieldResponse,
  CollaborationHistoryParticipantResponse,
  GetCollaborationHistoryAttemptResponse,
  GetCollaborationHistoryReplyResponse,
  GetCollaborationHistoryResponse,
} from '../../api/generated/api-client'
import type {
  ICollaborationAttemptHeaderResponse,
  ICollaborationHistoryEntryResponse,
  ICollaborationHistoryParticipantResponse,
} from '../../api/generated/api-client'
import { MESSAGE_SCHEMAS } from './collaborationHistory'
import type { KnownMessageType } from './collaborationHistory'

/** Test-only builders of a coherent collaboration-history response; every field can be replaced to build a contradiction. */
export const MESSAGE_RUN = 'run-1'

type Sequence = string | number | bigint

export const participantFor = (patch: Partial<ICollaborationHistoryParticipantResponse> = {}) =>
  new CollaborationHistoryParticipantResponse({ kind: 'Orchestrator', role: undefined, provider: undefined, ...patch })

export const agentFor = () => participantFor({ kind: 'Agent', role: 'Planner', provider: 'Codex' })

/** The fields of a type's closed schema in the given order (the schema's own by default), each with a recognizable value. */
export const fieldsFor = (type: KnownMessageType, names: readonly string[] = MESSAGE_SCHEMAS[type].map(([name]) => name)) =>
  names.map((name) => new CollaborationHistoryFieldResponse({ name, value: `${name} text` }))

export const entryFor = (sequence: Sequence, patch: Partial<ICollaborationHistoryEntryResponse> = {}) =>
  new CollaborationHistoryEntryResponse({
    runId: MESSAGE_RUN,
    id: `message-${sequence}`,
    sequence: String(sequence),
    occurredAtUtc: new Date(Date.UTC(2026, 9, 9, 10, 0, Number(BigInt(sequence) % 60n))),
    protocolVersion: '1.0',
    type: 'Proposal',
    provenance: 'ProviderObserved',
    actor: agentFor(),
    recipient: participantFor(),
    contentStatus: 'Available',
    summary: `Summary ${sequence}`,
    fields: fieldsFor('Proposal'),
    ...patch,
  })

/** A header whose content the host reported unavailable: no summary and no field. */
export const unavailableEntryFor = (sequence: Sequence, patch: Partial<ICollaborationHistoryEntryResponse> = {}) =>
  entryFor(sequence, { contentStatus: 'Unavailable', summary: undefined, fields: [], ...patch })

/** Descending messages for the given sequences, with `hasMore` and the next cursor derived exactly as the host derives them. */
export const collaborationPageOf = (
  sequences: readonly Sequence[],
  options: { runId?: string; hasMore?: boolean; next?: string | undefined; entries?: CollaborationHistoryEntryResponse[] } = {},
) => {
  const hasMore = options.hasMore ?? false
  return new GetCollaborationHistoryResponse({
    runId: options.runId ?? MESSAGE_RUN,
    entries: options.entries ?? sequences.map((sequence) => entryFor(sequence, { runId: options.runId ?? MESSAGE_RUN })),
    hasMore,
    nextBeforeSequence: 'next' in options ? options.next : hasMore ? String(sequences[sequences.length - 1]) : undefined,
  })
}

/** Sequence numbers from `from` down to `to`, inclusive. */
export const descendingSequences = (from: number, to: number) => Array.from({ length: from - to + 1 }, (_, index) => from - index)

/** A coherent reply-inspection response for the observed source; `parent` is the recorded parent entry (omitted unless Available). */
export const replyFor = (
  source: { id?: string; sequence: Sequence; runId?: string },
  status: 'None' | 'Unavailable' | 'Available' | string,
  parent?: CollaborationHistoryEntryResponse,
) =>
  new GetCollaborationHistoryReplyResponse({
    runId: source.runId ?? MESSAGE_RUN,
    messageId: source.id ?? `message-${source.sequence}`,
    sourceSequence: String(source.sequence),
    status,
    parent,
  })

/** A coherent recorded attempt header (a Planner on Codex under the Proposal contract); every field can be replaced to build a contradiction. */
export const attemptHeaderFor = (patch: Partial<ICollaborationAttemptHeaderResponse> = {}) =>
  new CollaborationAttemptHeaderResponse({
    id: 'attempt-7',
    attemptNumber: 3,
    role: 'Planner',
    provider: 'Codex',
    responseContract: 'Proposal',
    status: 'Completed',
    claimedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 0, 0)),
    dispatchedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 1, 2)),
    completedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 7, 8)),
    ...patch,
  })

/** A coherent attempt-attribution response for the observed source. */
export const attemptAnswerFor = (
  source: { id?: string; sequence: Sequence; runId?: string },
  status: 'NoAgentAttempt' | 'Unavailable' | 'Available' | string,
  attempt?: CollaborationAttemptHeaderResponse,
) =>
  new GetCollaborationHistoryAttemptResponse({
    runId: source.runId ?? MESSAGE_RUN,
    messageId: source.id ?? `message-${source.sequence}`,
    sourceSequence: String(source.sequence),
    status,
    attempt,
  })
