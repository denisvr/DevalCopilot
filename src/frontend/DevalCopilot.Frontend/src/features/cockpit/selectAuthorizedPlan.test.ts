import { describe, expect, it } from 'vitest'
import { derivePlanningLineage, selectAuthorizedPlanMessageId, selectImplementablePlanMessageId } from './derivePlanningLineage'
import type { CollaborationTimelineCard } from './types'

function card(overrides: Partial<CollaborationTimelineCard>): CollaborationTimelineCard {
  return {
    sequence: 1,
    id: 'message-1',
    attemptId: null,
    actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
    recipient: { kind: 'Agent', role: null, provider: 'ClaudeCode' },
    type: 'Proposal',
    inReplyToMessageId: null,
    summary: 'A bounded proposal',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-10-01T12:00:00Z',
    ...overrides,
  }
}

const resolver = { kind: 'Agent', role: 'Resolver', provider: 'Codex' }
const root = card({ sequence: 1, id: 'root' })
const first = card({ sequence: 10, id: 'first', actor: resolver, inReplyToMessageId: 'root' })
const second = card({ sequence: 20, id: 'second', actor: resolver, inReplyToMessageId: 'first' })
const escalation = card({
  sequence: 21,
  id: 'escalation',
  type: 'Escalation',
  actor: { kind: 'Orchestrator', role: null, provider: null },
  recipient: { kind: 'Human', role: null, provider: null },
  provenance: 'HostConstructed',
  inReplyToMessageId: 'second',
})

describe('selectAuthorizedPlanMessageId', () => {
  const lineage = derivePlanningLineage([root, first, second, escalation])

  it.each(['Available', 'Consumed'])('selects exactly the final revision the server names with a %s authorization', (state) => {
    expect(selectAuthorizedPlanMessageId(lineage, { state, finalProposalMessageId: 'second' })).toBe('second')
  })

  it.each(['Absent', 'Stale', 'Invalid', 'Unknown', null, undefined])('selects nothing for state %s', (state) => {
    expect(selectAuthorizedPlanMessageId(lineage, { state, finalProposalMessageId: 'second' })).toBeNull()
  })

  it('selects nothing without server facts or when they name another Proposal', () => {
    expect(selectAuthorizedPlanMessageId(lineage, null)).toBeNull()
    expect(selectAuthorizedPlanMessageId(lineage, { state: 'Available', finalProposalMessageId: 'first' })).toBeNull()
    expect(selectAuthorizedPlanMessageId(lineage, { state: 'Available', finalProposalMessageId: null })).toBeNull()
    expect(selectAuthorizedPlanMessageId(lineage, { state: 'Available' })).toBeNull()
  })

  it('selects nothing without the escalation, before a second revision, or for an ambiguous chain', () => {
    const facts = { state: 'Available', finalProposalMessageId: 'second' }
    expect(selectAuthorizedPlanMessageId(derivePlanningLineage([root, first, second]), facts)).toBeNull()
    expect(selectAuthorizedPlanMessageId(derivePlanningLineage([root, first]), facts)).toBeNull()
    const ambiguous = derivePlanningLineage([root, first, second, escalation, card({ sequence: 22, id: 'second-b', actor: resolver, inReplyToMessageId: 'first' })])
    expect(selectAuthorizedPlanMessageId(ambiguous, facts)).toBeNull()
  })

  it('never makes the automatic implementation selector offer the final revision', () => {
    expect(selectImplementablePlanMessageId(lineage, { status: null, loading: false, error: null })).toBeNull()
  })
})
