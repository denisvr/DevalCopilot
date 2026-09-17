import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { CollaborationTimelineCard } from '../types'
import { AgentCollaboration } from './AgentCollaboration'

describe('AgentCollaboration', () => {
  it('shows an explicit empty state before any event has arrived', () => {
    render(<AgentCollaboration cards={[]} loading={false} error={null} hasSuccessfulResponse />)
    expect(screen.getByText('No collaboration messages yet.')).toBeInTheDocument()
  })

  it('shows loading and failure states without presenting them as an empty timeline', () => {
    const { rerender } = render(
      <AgentCollaboration cards={[]} loading error={null} hasSuccessfulResponse={false} />,
    )
    expect(screen.getByText('Loading collaboration timeline…')).toBeInTheDocument()
    expect(screen.queryByText('No collaboration messages yet.')).not.toBeInTheDocument()

    rerender(
      <AgentCollaboration
        cards={[]}
        loading={false}
        error="Collaboration timeline is unavailable."
        hasSuccessfulResponse={false}
      />,
    )
    expect(screen.getByRole('status')).toHaveTextContent('Collaboration timeline is unavailable.')
    expect(screen.queryByText('No collaboration messages yet.')).not.toBeInTheDocument()
  })

  it('retains loaded cards while showing a refresh failure', () => {
    render(
      <AgentCollaboration
        cards={[fixture()]}
        loading={false}
        error="Collaboration timeline is unavailable."
        hasSuccessfulResponse={false}
      />,
    )

    expect(screen.getByText('A bounded proposal')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Collaboration timeline is unavailable.')
  })

  it('aligns Codex left, Claude right, and the orchestrator centered', () => {
    const cards: CollaborationTimelineCard[] = [
      fixture({ sequence: 1, actor: 'Orchestrator', summary: 'Started' }),
      fixture({ sequence: 2, actor: 'Codex', summary: 'Proposal' }),
      fixture({ sequence: 3, actor: 'Claude', summary: 'Challenge' }),
    ]

    render(<AgentCollaboration cards={cards} />)

    expect(screen.getByText('Started').closest('article')).toHaveAttribute('data-align', 'center')
    expect(screen.getByText('Proposal').closest('article')).toHaveAttribute('data-align', 'left')
    expect(screen.getByText('Challenge').closest('article')).toHaveAttribute('data-align', 'right')
  })

  it('labels simulation and presents bounded structured details without claiming a provider transcript', () => {
    render(<AgentCollaboration cards={[fixture({ details: ['rationale: Durable facts'], inReplyToMessageId: 'prior-message' })]} />)

    expect(screen.getByText(/simulated/i)).toBeInTheDocument()
    expect(screen.getByText('In reply to an earlier message.')).toBeInTheDocument()
    expect(screen.getByText('Bounded details')).toBeInTheDocument()
    expect(screen.queryByText(/transcript/i)).not.toBeInTheDocument()
  })

  function fixture(overrides: Partial<CollaborationTimelineCard> = {}): CollaborationTimelineCard {
    return {
      sequence: 1,
      id: 'message-1',
      attemptId: null,
      actor: 'Codex',
      recipient: 'Claude',
      type: 'Proposal',
      inReplyToMessageId: null,
      summary: 'A bounded proposal',
      details: [],
      provenance: 'Simulated',
      occurredAtUtc: '2026-09-16T12:00:00Z',
      ...overrides,
    }
  }
})
