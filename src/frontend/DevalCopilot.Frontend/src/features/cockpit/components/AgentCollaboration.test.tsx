import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { CollaborationCard } from '../types'
import { AgentCollaboration } from './AgentCollaboration'

describe('AgentCollaboration', () => {
  it('shows an explicit empty state before any event has arrived', () => {
    render(<AgentCollaboration cards={[]} />)
    expect(screen.getByText('No collaboration events yet.')).toBeInTheDocument()
  })

  it('aligns Codex left, Claude right, and the orchestrator centered', () => {
    const cards: CollaborationCard[] = [
      { sequence: 1, id: 'a', attemptId: null, eventType: 'run.started', actor: 'Orchestrator', summary: 'Started', occurredAtUtc: '' },
      { sequence: 2, id: 'b', attemptId: null, eventType: 'codex.proposal', actor: 'Codex', summary: 'Proposal', occurredAtUtc: '' },
      { sequence: 3, id: 'c', attemptId: null, eventType: 'claude.challenge', actor: 'Claude', summary: 'Challenge', occurredAtUtc: '' },
    ]

    render(<AgentCollaboration cards={cards} />)

    expect(screen.getByText('Started').closest('article')).toHaveAttribute('data-align', 'center')
    expect(screen.getByText('Proposal').closest('article')).toHaveAttribute('data-align', 'left')
    expect(screen.getByText('Challenge').closest('article')).toHaveAttribute('data-align', 'right')
  })
})
