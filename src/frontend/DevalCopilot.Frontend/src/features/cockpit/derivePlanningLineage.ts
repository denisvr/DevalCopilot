import { selectLatestCodexProposalMessageId } from './selectLatestCodexProposal'
import type { CollaborationTimelineCard } from './types'

const PROPOSAL_TYPE = 'Proposal'
const ESCALATION_TYPE = 'Escalation'
/** Mirrors the backend's `AgentRole.Resolver`: the only role that authors a revised Proposal. */
const RESOLVER_ROLE = 'Resolver'
const REAL_PROVENANCE = 'ProviderObserved'
const HOST_PROVENANCE = 'HostConstructed'
const ORCHESTRATOR_KIND = 'Orchestrator'

/**
 * The Proposal → revised Proposal chain of the run's newest Planner root, read only from the
 * collaboration timeline cards already loaded — never a new API call. It is a display hint: the
 * backend independently decides review, resolution, and implementation eligibility from durable
 * identity, so an unverifiable or ambiguous chain here only ever withholds an action.
 *
 * Depth 0 is the root, depth 1 its first Resolver revision (optionally reviewable once), depth 2
 * the revision made from a challenged second review — the end of the lineage, followed by one
 * orchestrator escalation for a human decision.
 */
export interface PlanningLineage {
  root: CollaborationTimelineCard | null
  firstRevision: CollaborationTimelineCard | null
  secondRevision: CollaborationTimelineCard | null
  /** The host-constructed human escalation replying to the depth-two revision, when loaded. */
  escalation: CollaborationTimelineCard | null
  /** More than one revision claims the same parent, so the chain is not a single trusted line. */
  ambiguous: boolean
  depth: 0 | 1 | 2 | null
}

const EMPTY_LINEAGE: PlanningLineage = {
  root: null,
  firstRevision: null,
  secondRevision: null,
  escalation: null,
  ambiguous: false,
  depth: null,
}

function revisionsOf(
  cards: readonly CollaborationTimelineCard[],
  parent: CollaborationTimelineCard,
): CollaborationTimelineCard[] {
  return cards.filter(
    (card) =>
      card.type === PROPOSAL_TYPE &&
      card.actor.role === RESOLVER_ROLE &&
      card.provenance === REAL_PROVENANCE &&
      card.inReplyToMessageId === parent.id &&
      card.sequence > parent.sequence,
  )
}

export function derivePlanningLineage(cards: readonly CollaborationTimelineCard[]): PlanningLineage {
  const rootId = selectLatestCodexProposalMessageId(cards)
  const root = rootId ? (cards.find((card) => card.id === rootId) ?? null) : null
  if (!root) {
    return EMPTY_LINEAGE
  }

  const firstCandidates = revisionsOf(cards, root)
  if (firstCandidates.length === 0) {
    return { ...EMPTY_LINEAGE, root, depth: 0 }
  }
  if (firstCandidates.length > 1) {
    return { ...EMPTY_LINEAGE, root, ambiguous: true, depth: 0 }
  }

  const firstRevision = firstCandidates[0]
  const secondCandidates = revisionsOf(cards, firstRevision)
  if (secondCandidates.length === 0) {
    return { ...EMPTY_LINEAGE, root, firstRevision, depth: 1 }
  }
  if (secondCandidates.length > 1) {
    return { ...EMPTY_LINEAGE, root, firstRevision, ambiguous: true, depth: 1 }
  }

  const secondRevision = secondCandidates[0]
  const escalation =
    cards.find(
      (card) =>
        card.type === ESCALATION_TYPE &&
        card.provenance === HOST_PROVENANCE &&
        card.actor.kind === ORCHESTRATOR_KIND &&
        card.inReplyToMessageId === secondRevision.id,
    ) ?? null

  return { root, firstRevision, secondRevision, escalation, ambiguous: false, depth: 2 }
}

/** The latest Claude critical-review attempt as the cockpit read it, or how far that read got. */
export interface ReviewStatusHint {
  status: {
    reviewedProposalMessageId?: string | null
    outcome?: string | null
    status?: string | null
  } | null
  loading: boolean
  error: string | null
}

/**
 * The one Proposal an optional critical review may target next: the root before any revision, the
 * first revision once one exists, and nothing once the lineage reached its second revision or its
 * chain is ambiguous. A later independent Planner root replaces the whole lineage.
 */
export function selectReviewableProposalMessageId(lineage: PlanningLineage): string | null {
  if (!lineage.root || lineage.ambiguous || lineage.secondRevision) {
    return null
  }
  return (lineage.firstRevision ?? lineage.root).id
}

/**
 * The one Proposal an implementation may target next, withholding every certainly blocked case:
 * the original root only after an Accepted review of exactly that root; the first revision unless
 * its own review Challenged or is still running (and not while that review status is loading or
 * failed, so a hidden Challenged review is never overlooked); never a second revision, whose
 * lineage ended in a human escalation, and never an ambiguous chain.
 */
export function selectImplementablePlanMessageId(
  lineage: PlanningLineage,
  review: ReviewStatusHint,
): string | null {
  if (!lineage.root || lineage.ambiguous || lineage.secondRevision) {
    return null
  }

  if (!lineage.firstRevision) {
    return review.status?.outcome === 'Accepted' && review.status.reviewedProposalMessageId === lineage.root.id
      ? lineage.root.id
      : null
  }

  if (review.loading || review.error) {
    return null
  }

  const reviewsFirstRevision = review.status?.reviewedProposalMessageId === lineage.firstRevision.id
  if (reviewsFirstRevision && (review.status?.outcome === 'Challenged' || review.status?.status === 'Running')) {
    return null
  }

  return lineage.firstRevision.id
}

export type LineageStage =
  | 'none'
  | 'root'
  | 'revised'
  | 'reviewRunning'
  | 'reviewAccepted'
  | 'reviewChallenged'
  | 'reviewNotConcluded'
  | 'statusUnavailable'
  | 'exhausted'
  | 'ambiguous'

/** A truthful one-line stage for the revision lineage, or `none` when no revision exists yet. */
export function deriveLineageStage(lineage: PlanningLineage, review: ReviewStatusHint): LineageStage {
  if (!lineage.root) {
    return 'none'
  }
  if (lineage.ambiguous) {
    return 'ambiguous'
  }
  if (lineage.secondRevision) {
    return 'exhausted'
  }
  if (!lineage.firstRevision) {
    return 'root'
  }
  if (review.loading || review.error) {
    return 'statusUnavailable'
  }

  const status = review.status
  if (status?.reviewedProposalMessageId !== lineage.firstRevision.id) {
    return 'revised'
  }
  if (status.status === 'Running') {
    return 'reviewRunning'
  }
  if (status.outcome === 'Accepted') {
    return 'reviewAccepted'
  }
  if (status.outcome === 'Challenged') {
    return 'reviewChallenged'
  }
  return 'reviewNotConcluded'
}

/** The server's authorization facts as far as plan selection needs them. */
export interface PlanningAuthorizationHint {
  state?: string | null
  finalProposalMessageId?: string | null
}

/**
 * The one final plan an explicit human authorization concerns, offered to the implementation action only from
 * current server facts: the lineage must end in a second revision with its escalation, and the server must name that exact
 * revision with an `Available` authorization (to request) or a `Consumed` one (to keep showing that claim's outcome).
 * Stale, invalid, absent, unread, ambiguous, or mismatched facts select nothing, and nothing is inferred from a click.
 */
export function selectAuthorizedPlanMessageId(
  lineage: PlanningLineage,
  authorization: PlanningAuthorizationHint | null,
): string | null {
  if (!lineage.secondRevision || !lineage.escalation || lineage.ambiguous || !authorization) {
    return null
  }
  if (authorization.finalProposalMessageId !== lineage.secondRevision.id) {
    return null
  }
  return authorization.state === 'Available' || authorization.state === 'Consumed' ? lineage.secondRevision.id : null
}
