import type { CollaborationCard } from '../types'

interface AgentCollaborationProps {
  cards: CollaborationCard[]
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

/**
 * Codex cards align left, Claude cards align right, orchestrator/human/system events
 * are centered. Reconstructed from durable events only — there is no dependency on
 * either provider's native conversation history.
 */
export function AgentCollaboration({ cards }: AgentCollaborationProps) {
  if (cards.length === 0) {
    return (
      <section className="dc-collaboration" aria-label="Agent collaboration">
        <p className="dc-empty-state">No collaboration events yet.</p>
      </section>
    )
  }

  return (
    <section className="dc-collaboration" aria-label="Agent collaboration">
      {cards.map((card) => (
        <article key={card.sequence} className="dc-card" data-align={alignmentFor(card.actor)}>
          <div className="dc-card-actor">
            {card.actor} · {card.eventType}
          </div>
          <p className="dc-card-summary">{card.summary}</p>
        </article>
      ))}
    </section>
  )
}
