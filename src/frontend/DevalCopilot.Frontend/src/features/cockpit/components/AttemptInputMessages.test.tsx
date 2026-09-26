import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { CollaborationTimelineCard } from '../types'
import { AttemptInputMessages } from './AttemptInputMessages'

function buildCard(overrides: Partial<CollaborationTimelineCard> = {}): CollaborationTimelineCard {
  return {
    sequence: 0,
    id: 'message-1',
    attemptId: 'attempt-1',
    actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
    recipient: { kind: 'Agent', role: 'CriticalReviewer', provider: 'ClaudeCode' },
    type: 'Proposal',
    inReplyToMessageId: null,
    summary: 'A proposal summary',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-09-26T12:00:00Z',
    ...overrides,
  }
}

describe('AttemptInputMessages', () => {
  it('labels a legitimately empty set honestly, never as a failed verification', () => {
    render(<AttemptInputMessages evidence={{ inputMessagesStatus: 'Empty' }} cardsById={new Map()} />)

    const empty = screen.getByText(/No recorded collaboration inputs/)
    expect(empty).toHaveAttribute('data-input-messages-status', 'Empty')
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('states an Invalid set could not be verified and never lists a partial subset', () => {
    render(
      <AttemptInputMessages
        evidence={{ inputMessagesStatus: 'Invalid', inputMessages: [], inputMessageTotalCount: 2 }}
        cardsById={new Map()}
      />,
    )

    const message = screen.getByText(/could not be verified and are not shown/)
    expect(message).toHaveAttribute('data-input-messages-status', 'Invalid')
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('falls back to an honest unavailable label for a missing or unrecognized status', () => {
    render(<AttemptInputMessages evidence={undefined} cardsById={new Map()} />)

    expect(screen.getByText('Recorded collaboration inputs unavailable.')).toBeInTheDocument()
  })

  it('renders the ordered recorded inputs with the honest recorded-inputs caveat, cross-referencing the loaded timeline', () => {
    const proposalCard = buildCard({ id: 'proposal-1', type: 'Proposal', summary: 'Add the ledger table' })
    const cardsById = new Map([['proposal-1', proposalCard]])

    render(
      <AttemptInputMessages
        evidence={{
          inputMessagesStatus: 'Recorded',
          inputMessages: [
            { sequence: 0, collaborationMessageId: 'proposal-1', type: 'Proposal' },
            { sequence: 1, collaborationMessageId: 'challenge-not-loaded', type: 'Challenge' },
          ],
          inputMessagesOmitted: false,
          inputMessageTotalCount: 2,
        }}
        cardsById={cardsById}
      />,
    )

    expect(screen.getByText(/never the complete prompt/)).toBeInTheDocument()
    const items = screen.getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('1. Proposal: Add the ledger table')
    expect(items[1]).toHaveTextContent('2. Challenge: not present in the currently loaded timeline')
    expect(screen.queryByText(/recorded inputs in total/)).not.toBeInTheDocument()
  })

  it('states the bounded-cap omission honestly when more inputs exist than are shown', () => {
    render(
      <AttemptInputMessages
        evidence={{
          inputMessagesStatus: 'Recorded',
          inputMessages: [{ sequence: 0, collaborationMessageId: 'proposal-1', type: 'Proposal' }],
          inputMessagesOmitted: true,
          inputMessageTotalCount: 11,
        }}
        cardsById={new Map()}
      />,
    )

    expect(screen.getByText(/11 recorded inputs in total; only the first 1 are shown/)).toBeInTheDocument()
  })
})
