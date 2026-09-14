import type { CollaborationCard } from './types'

/** Mirrors the backend's `RunEventType.ProcessOutputCaptured` durable event-type string. */
const PROCESS_OUTPUT_CAPTURED_EVENT_TYPE = 'process.output_captured'

/**
 * The Process attempt to show live output for, derived entirely from the collaboration
 * cards already loaded for this run — never a new API call or projection. Picks the
 * attempt of the highest-sequence `process.output_captured` card, so a run with more than
 * one Process attempt always shows the most recent one. Returns `null` for a
 * Simulated-only run, or one that has not yet captured any process output.
 */
export function selectCurrentProcessAttemptId(cards: readonly CollaborationCard[]): string | null {
  let latest: CollaborationCard | null = null

  for (const card of cards) {
    if (card.eventType !== PROCESS_OUTPUT_CAPTURED_EVENT_TYPE || card.attemptId === null) {
      continue
    }
    if (latest === null || card.sequence > latest.sequence) {
      latest = card
    }
  }

  return latest?.attemptId ?? null
}
