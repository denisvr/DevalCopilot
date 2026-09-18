import type { CollaborationTimelineCard } from './types'

/** Mirrors the backend's `CollaborationMessageType.Proposal` and `ParticipantKind.Codex`. */
const PROPOSAL_TYPE = 'Proposal'
const CODEX_ACTOR = 'Codex'
/** Mirrors the backend's `CollaborationMessageProvenance.ProviderObserved` — the only
 * provenance a Claude critical review can meaningfully be requested against. A `Simulated`
 * Proposal is a walking-skeleton fixture, never a real Codex plan to review. */
const REAL_PROVENANCE = 'ProviderObserved'

/**
 * The Codex Proposal message id a "Request Claude review" action should target, derived
 * entirely from the collaboration timeline cards already loaded for this run — never a new
 * API call or projection. Picks the highest-sequence, provider-observed Codex Proposal, so a
 * run with more than one Proposal always targets the most recent one. Returns `null` when no
 * real Proposal has been recorded yet.
 */
export function selectLatestCodexProposalMessageId(cards: readonly CollaborationTimelineCard[]): string | null {
  let latest: CollaborationTimelineCard | null = null

  for (const card of cards) {
    if (card.type !== PROPOSAL_TYPE || card.actor !== CODEX_ACTOR || card.provenance !== REAL_PROVENANCE) {
      continue
    }
    if (latest === null || card.sequence > latest.sequence) {
      latest = card
    }
  }

  return latest?.id ?? null
}
