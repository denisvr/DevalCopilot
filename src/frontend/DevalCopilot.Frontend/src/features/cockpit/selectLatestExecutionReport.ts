import type { CollaborationTimelineCard } from './types'

/** Mirrors the backend's `CollaborationMessageType.ExecutionReport` and `ParticipantKind.Claude`. */
const EXECUTION_REPORT_TYPE = 'ExecutionReport'
const CLAUDE_ACTOR = 'Claude'
/** Mirrors the backend's `CollaborationMessageProvenance.ProviderObserved` — the only provenance
 * a code review can meaningfully be requested against. Mirrors
 * `selectLatestCodexProposalMessageId`'s identical reasoning. */
const REAL_PROVENANCE = 'ProviderObserved'

/**
 * The Claude implementation ExecutionReport message id a "Request code review" action should
 * target, derived entirely from the collaboration timeline cards already loaded for this run —
 * never a new API call or projection. Picks the highest-sequence, provider-observed Claude
 * ExecutionReport, so a run with more than one implementation always targets the most recent one.
 * Returns `null` when no real ExecutionReport has been recorded yet. Mirrors
 * `selectLatestCodexProposalMessageId` exactly.
 */
export function selectLatestExecutionReportMessageId(cards: readonly CollaborationTimelineCard[]): string | null {
  let latest: CollaborationTimelineCard | null = null

  for (const card of cards) {
    if (card.type !== EXECUTION_REPORT_TYPE || card.actor !== CLAUDE_ACTOR || card.provenance !== REAL_PROVENANCE) {
      continue
    }
    if (latest === null || card.sequence > latest.sequence) {
      latest = card
    }
  }

  return latest?.id ?? null
}
