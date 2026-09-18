import type { CollaborationTimelineCard } from '../types'

interface AgentCollaborationProps {
  cards: CollaborationTimelineCard[]
  loading?: boolean
  error?: string | null
  hasSuccessfulResponse?: boolean
}

function alignmentFor(actor: string): 'left' | 'right' | 'center' {
  if (actor === 'Codex') {
    return 'left'
  }
  if (actor === 'Claude') {
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
}

function typeLabelFor(type: string): string {
  return TYPE_LABEL[type] ?? type
}

/**
 * Codex cards align left, Claude cards align right, orchestrator/human/system events
 * are centered. Reconstructed from durable events only — there is no dependency on
 * either provider's native conversation history.
 */
export function AgentCollaboration({
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

  return (
    <section className="dc-collaboration" aria-label="Agent collaboration">
      {error && <p className="dc-collaboration-status" role="status">{error}</p>}
      {cards.map((card) => (
        <article key={card.sequence} className="dc-card" data-align={alignmentFor(card.actor)} data-type={card.type}>
          <div className="dc-card-actor">
            {card.actor} → {card.recipient} · {typeLabelFor(card.type)} · {card.provenance}
          </div>
          <p className="dc-card-summary">{card.summary}</p>
          {card.inReplyToMessageId && <p className="dc-card-reply">In reply to an earlier message.</p>}
          <time className="dc-card-timestamp" dateTime={card.occurredAtUtc}>
            {card.occurredAtUtc}
          </time>
          {card.details.length > 0 && (
            <details className="dc-card-details">
              <summary>Bounded details</summary>
              <ul>
                {card.details.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            </details>
          )}
        </article>
      ))}
    </section>
  )
}
