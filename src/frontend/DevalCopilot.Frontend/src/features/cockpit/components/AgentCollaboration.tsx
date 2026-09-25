import type { CollaborationTimelineCard } from '../types'
import type { ParticipantIdentityView } from '../types'
import { formatParticipantIdentity } from '../participantIdentity'
import { isTypedCollaborationCard, parseCollaborationCardContent } from '../collaborationCardContent'
import { CollaborationEvidenceDrilldown } from './CollaborationEvidenceDrilldown'

interface AgentCollaborationProps {
  runId: string
  cards: CollaborationTimelineCard[]
  loading?: boolean
  error?: string | null
  hasSuccessfulResponse?: boolean
}

function alignmentFor(actor: ParticipantIdentityView): 'left' | 'right' | 'center' {
  if (actor.role === 'Planner' || actor.role === 'Resolver' || actor.role === 'CodeReviewer') {
    return 'left'
  }
  if (actor.role === 'CriticalReviewer' || actor.role === 'Implementer') {
    return 'right'
  }
  return 'center'
}

/** Mirrors the backend's `CollaborationMessageType` enum with a readable label, so a card
 * never falls back to showing the raw enum identifier as its type. */
const TYPE_LABEL: Record<string, string> = {
  Proposal: 'Proposal',
  Acceptance: 'Acceptance',
  Challenge: 'Challenge',
  Question: 'Question',
  Decision: 'Decision',
  ExecutionReport: 'Execution report',
  ReviewFinding: 'Review finding',
  RevisionResponse: 'Revision response',
  Escalation: 'Escalation',
  ReviewApproval: 'Review approval',
  HumanInstruction: 'Human instruction',
}

function typeLabelFor(type: string): string {
  return TYPE_LABEL[type] ?? type
}

/** Mirrors the backend's closed Domain policy exactly (`CollaborationMessageReplyPolicy`,
 * `IsAllowedParent`, in src/backend/DevalCopilot.Domain/Features/Runs/) — the durable ledger
 * already rejects any reply whose parent type is not in this set, but the UI never assumes that:
 * it re-checks locally before ever describing a relationship as verified. A root Proposal has no
 * parent at all (never reaches this table, since `replyDescription` returns early when
 * `inReplyToMessageId` is absent); a revised Proposal's only valid parent is the prior Proposal it
 * supersedes. `ExecutionReport`'s real Increment 4 Implementer path always replies to Proposal —
 * Decision is also allowed by the Domain policy as the aspirational later full-loop shape, but is
 * not the path any current attempt actually takes. */
const REPLY_PARENT_TYPES: Record<string, readonly string[]> = {
  Proposal: ['Proposal'],
  Acceptance: ['Proposal'],
  Challenge: ['Proposal'],
  Decision: ['Proposal', 'Challenge'],
  ExecutionReport: ['Decision', 'Proposal'],
  ReviewFinding: ['ExecutionReport'],
  RevisionResponse: ['ReviewFinding'],
  Question: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding'],
  Escalation: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding', 'RevisionResponse', 'Question'],
  // A Codex implementation review's approval fact always replies directly to the Execution
  // report it approved — never the Proposal, and never overloading Acceptance, which is scoped
  // to a not-yet-implemented plan.
  ReviewApproval: ['ExecutionReport'],
  HumanInstruction: ['Escalation'],
}

/** A parent is only ever described as verified when it is both an earlier message (by sequence)
 * and a protocol-allowed parent type for the child's own type — never inferred from its mere
 * presence in the loaded window. */
function isVerifiedParent(card: CollaborationTimelineCard, parent: CollaborationTimelineCard): boolean {
  const allowedParentTypes = REPLY_PARENT_TYPES[card.type]
  return Boolean(allowedParentTypes) && allowedParentTypes.includes(parent.type) && parent.sequence < card.sequence
}

function replyDescription(card: CollaborationTimelineCard, cardsById: ReadonlyMap<string, CollaborationTimelineCard>) {
  if (!card.inReplyToMessageId) {
    return null
  }

  const parent = cardsById.get(card.inReplyToMessageId)
  if (!parent) {
    // Absence only means this run's currently loaded timeline does not contain that message id —
    // it is never evidence that the parent is outside the API's bounded window, or that it does
    // not exist at all.
    return 'The referenced parent message is not present in the currently loaded timeline; the relationship is not verified here.'
  }

  if (!isVerifiedParent(card, parent)) {
    // The id matched a loaded message, but it is not an earlier, protocol-compatible parent for
    // this card's own type — never described as "in reply to", which would assert a semantic
    // relationship this card has not actually verified.
    return `References ${typeLabelFor(parent.type)} message ${parent.id}; not verified as an earlier, protocol-compatible parent.`
  }

  return `In reply to ${typeLabelFor(parent.type)} message ${parent.id}: ${parent.summary}`
}

/**
 * Role families align consistently even when their provider changes; actors without a known
 * role and orchestrator/human/system events are centered. Reconstructed from durable events only — there is no dependency on
 * either provider's native conversation history.
 */
export function AgentCollaboration({
  runId,
  cards,
  loading = false,
  error = null,
  hasSuccessfulResponse = true,
}: AgentCollaborationProps) {
  if (loading && cards.length === 0) {
    return (
      <section className="dc-collaboration" aria-label="Agent collaboration" aria-busy="true">
        <p className="dc-empty-state">Loading collaboration timeline…</p>
      </section>
    )
  }

  if (error && cards.length === 0) {
    return (
      <section className="dc-collaboration" aria-label="Agent collaboration">
        <p className="dc-empty-state" role="status">{error}</p>
      </section>
    )
  }

  if (hasSuccessfulResponse && cards.length === 0) {
    return (
      <section className="dc-collaboration" aria-label="Agent collaboration">
        <p className="dc-empty-state">No collaboration messages yet.</p>
      </section>
    )
  }

  const cardsById = new Map(cards.map((card) => [card.id, card]))

  return (
    <section className="dc-collaboration" aria-label="Agent collaboration">
      {error && <p className="dc-collaboration-status" role="status">{error}</p>}
      {cards.map((card) => {
        const reply = replyDescription(card, cardsById)
        const structuredContent = parseCollaborationCardContent(card)

        return (
          <article key={card.sequence} className="dc-card" data-align={alignmentFor(card.actor)} data-type={card.type}>
            <div className="dc-card-actor">
              {formatParticipantIdentity(card.actor)} → {formatParticipantIdentity(card.recipient)} · {typeLabelFor(card.type)} · {card.provenance}
            </div>
            <p className="dc-card-summary">{card.summary}</p>
            {reply && <p className="dc-card-reply">{reply}</p>}
            <time className="dc-card-timestamp" dateTime={card.occurredAtUtc}>
              {card.occurredAtUtc}
            </time>
            {structuredContent ? (
              <dl className="dc-card-structured-content" aria-label={`${typeLabelFor(card.type)} details`}>
                {structuredContent.map((section) => (
                  <div key={section.label}>
                    <dt>{section.label}</dt>
                    <dd>{section.value}</dd>
                  </div>
                ))}
              </dl>
            ) : isTypedCollaborationCard(card) && card.structuredContentJson ? (
              <p className="dc-card-details-unavailable">Structured details unavailable.</p>
            ) : card.details.length > 0 ? (
              <details className="dc-card-details">
                <summary>Bounded details</summary>
                <ul>
                  {card.details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              </details>
            ) : null}
            {/* Only ever offered for a card the backend can truthfully resolve to one exact
                Attempt — a Human/Orchestrator/Simulated card (ProviderObserved false, or no
                attemptId) never shows this control at all, not even a disabled one. */}
            {card.provenance === 'ProviderObserved' && card.attemptId && (
              <CollaborationEvidenceDrilldown runId={runId} messageId={card.id} />
            )}
          </article>
        )
      })}
    </section>
  )
}
