import { describe, expect, it } from 'vitest'
import type { CollaborationTimelineCard } from './types'
import { selectLatestCodexProposalMessageId } from './selectLatestCodexProposal'

function card(overrides: Partial<CollaborationTimelineCard>): CollaborationTimelineCard {
  return {
    sequence: 0,
    id: 'message-0',
    attemptId: null,
    actor: 'Codex',
    recipient: 'Claude',
    type: 'Proposal',
    inReplyToMessageId: null,
    summary: '',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('selectLatestCodexProposalMessageId', () => {
  it('returns null for an empty card list', () => {
    expect(selectLatestCodexProposalMessageId([])).toBeNull()
  })

  it('returns null when no card is a Codex Proposal', () => {
    const cards = [
      card({ sequence: 1, type: 'Acceptance', actor: 'Claude', id: 'message-1' }),
      card({ sequence: 2, type: 'Question', actor: 'Codex', id: 'message-2' }),
    ]

    expect(selectLatestCodexProposalMessageId(cards)).toBeNull()
  })

  it('ignores a Simulated Proposal, since it is a fixture rather than a real Codex plan', () => {
    const cards = [card({ sequence: 1, id: 'message-1', provenance: 'Simulated' })]

    expect(selectLatestCodexProposalMessageId(cards)).toBeNull()
  })

  it('ignores a Proposal from an actor other than Codex', () => {
    const cards = [card({ sequence: 1, id: 'message-1', actor: 'Human' })]

    expect(selectLatestCodexProposalMessageId(cards)).toBeNull()
  })

  it('returns the id of the single matching card', () => {
    const cards = [card({ sequence: 1, id: 'message-1' })]

    expect(selectLatestCodexProposalMessageId(cards)).toBe('message-1')
  })

  it('deterministically picks the highest-sequence match, regardless of array order', () => {
    const cards = [
      card({ sequence: 5, id: 'message-2' }),
      card({ sequence: 1, id: 'message-1' }),
      card({ sequence: 3, id: 'message-3' }),
    ]

    expect(selectLatestCodexProposalMessageId(cards)).toBe('message-2')
  })

  it('ignores non-matching cards interleaved with the matching one', () => {
    const cards = [
      card({ sequence: 1, type: 'Question', id: 'message-1' }),
      card({ sequence: 2, id: 'message-2' }),
      card({ sequence: 3, type: 'Acceptance', actor: 'Claude', id: 'message-3' }),
    ]

    expect(selectLatestCodexProposalMessageId(cards)).toBe('message-2')
  })
})
