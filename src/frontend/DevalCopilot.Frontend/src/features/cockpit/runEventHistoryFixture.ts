import {
  GetRunEventHistoryResponse,
  RunEventHistoryActorResponse,
  RunEventHistoryEntryResponse,
} from '../../api/generated/api-client'
import type { IRunEventHistoryActorResponse, IRunEventHistoryEntryResponse } from '../../api/generated/api-client'

/** Test-only builders of a coherent event-history response; every field can be replaced to build a contradiction. */
export const EVENT_RUN = 'run-1'

type Sequence = string | number | bigint

export const actorFor = (patch: Partial<IRunEventHistoryActorResponse> = {}) =>
  new RunEventHistoryActorResponse({ kind: 'Orchestrator', role: undefined, provider: undefined, ...patch })

export const headerFor = (sequence: Sequence, patch: Partial<IRunEventHistoryEntryResponse> = {}) =>
  new RunEventHistoryEntryResponse({
    runId: EVENT_RUN,
    id: `event-${sequence}`,
    sequence: String(sequence),
    occurredAtUtc: new Date(Date.UTC(2026, 9, 9, 10, 0, Number(BigInt(sequence) % 60n))),
    eventType: 'collaboration.message_recorded',
    actor: actorFor(),
    ...patch,
  })

/** Descending headers for the given sequences, with `hasMore` and the next cursor derived exactly as the host derives them. */
export const eventPageOf = (
  sequences: readonly Sequence[],
  options: { runId?: string; hasMore?: boolean; next?: string | undefined; entries?: RunEventHistoryEntryResponse[] } = {},
) => {
  const hasMore = options.hasMore ?? false
  return new GetRunEventHistoryResponse({
    runId: options.runId ?? EVENT_RUN,
    entries: options.entries ?? sequences.map((sequence) => headerFor(sequence, { runId: options.runId ?? EVENT_RUN })),
    hasMore,
    nextBeforeSequence: 'next' in options ? options.next : hasMore ? String(sequences[sequences.length - 1]) : undefined,
  })
}

/** Sequence numbers from `from` down to `to`, inclusive. */
export const descendingSequences = (from: number, to: number) => Array.from({ length: from - to + 1 }, (_, index) => from - index)
