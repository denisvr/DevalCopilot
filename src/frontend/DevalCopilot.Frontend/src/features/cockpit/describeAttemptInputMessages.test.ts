import { describe, expect, it } from 'vitest'
import { describeAttemptInputMessage, describeAttemptInputMessagesStatus } from './describeAttemptInputMessages'
import type { CollaborationTimelineCard } from './types'

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

describe('describeAttemptInputMessagesStatus', () => {
  it('describes Empty honestly, never as an unverifiable set', () => {
    expect(describeAttemptInputMessagesStatus('Empty')).toBe('No recorded collaboration inputs — this attempt started from none.')
  })

  it('describes Invalid as a failed verification, never a partial list', () => {
    expect(describeAttemptInputMessagesStatus('Invalid')).toBe(
      "This attempt's recorded collaboration inputs could not be verified and are not shown.",
    )
  })

  it('returns null for Recorded so the caller renders the ordered list instead', () => {
    expect(describeAttemptInputMessagesStatus('Recorded')).toBeNull()
  })

  it('fails closed to an honest unavailable label for any unrecognized or missing status', () => {
    expect(describeAttemptInputMessagesStatus('SomeFutureStatus')).toBe('Recorded collaboration inputs unavailable.')
    expect(describeAttemptInputMessagesStatus(undefined)).toBe('Recorded collaboration inputs unavailable.')
    expect(describeAttemptInputMessagesStatus(null)).toBe('Recorded collaboration inputs unavailable.')
  })
})

describe('describeAttemptInputMessage', () => {
  it('shows the referenced messages own already-loaded summary when it is present in the timeline window', () => {
    const card = buildCard({ id: 'message-1', type: 'Proposal', summary: 'Add the ledger table' })
    const cardsById = new Map([['message-1', card]])

    const text = describeAttemptInputMessage({ sequence: 0, collaborationMessageId: 'message-1', type: 'Proposal' }, cardsById)

    expect(text).toBe('1. Proposal: Add the ledger table')
  })

  it('numbers entries from their own recorded sequence, one-based for display', () => {
    const card = buildCard({ id: 'message-2', type: 'Challenge', summary: 'A challenge' })
    const cardsById = new Map([['message-2', card]])

    const text = describeAttemptInputMessage({ sequence: 1, collaborationMessageId: 'message-2', type: 'Challenge' }, cardsById)

    expect(text).toBe('2. Challenge: A challenge')
  })

  // The core honesty rule this slice adds: a recorded input whose message is not in the caller's
  // own currently loaded timeline window must never be described as if it were visible there —
  // mirrors the existing reply-parent-verification wording in AgentCollaboration.tsx.
  it('never claims a referenced message is visible when it is not in the currently loaded timeline', () => {
    const cardsById = new Map<string, CollaborationTimelineCard>()

    const text = describeAttemptInputMessage({ sequence: 0, collaborationMessageId: 'message-not-loaded', type: 'Proposal' }, cardsById)

    expect(text).toBe('1. Proposal: not present in the currently loaded timeline; the relationship is not verified here.')
  })

  it('describes a missing reference identity without crashing', () => {
    const cardsById = new Map<string, CollaborationTimelineCard>()

    const text = describeAttemptInputMessage({ sequence: 0, type: 'Proposal' }, cardsById)

    expect(text).toBe('1. Proposal: no referenced message identity.')
  })

  it('labels an unrecognized type using its raw value rather than throwing', () => {
    const cardsById = new Map<string, CollaborationTimelineCard>()

    const text = describeAttemptInputMessage({ sequence: 0, collaborationMessageId: 'message-x', type: 'SomeFutureType' }, cardsById)

    expect(text).toBe('1. SomeFutureType: not present in the currently loaded timeline; the relationship is not verified here.')
  })
})
