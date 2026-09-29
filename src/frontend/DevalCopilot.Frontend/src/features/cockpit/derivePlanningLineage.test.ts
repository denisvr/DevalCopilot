import { describe, expect, it } from 'vitest'
import {
  derivePlanningLineage,
  deriveLineageStage,
  selectImplementablePlanMessageId,
  selectReviewableProposalMessageId,
} from './derivePlanningLineage'
import type { ReviewStatusHint } from './derivePlanningLineage'
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
    occurredAtUtc: '2026-09-29T12:00:00Z',
    ...overrides,
  }
}

const root = card({ sequence: 1, id: 'root' })
const first = card({
  sequence: 10,
  id: 'first',
  actor: { kind: 'Agent', role: 'Resolver', provider: 'Codex' },
  inReplyToMessageId: 'root',
})
const second = card({
  sequence: 20,
  id: 'second',
  actor: { kind: 'Agent', role: 'Resolver', provider: 'Codex' },
  inReplyToMessageId: 'first',
})
const escalation = card({
  sequence: 21,
  id: 'escalation',
  type: 'Escalation',
  actor: { kind: 'Orchestrator', role: null, provider: null },
  recipient: { kind: 'Human', role: null, provider: null },
  provenance: 'HostConstructed',
  inReplyToMessageId: 'second',
  summary: 'The second challenge-resolution round is complete and needs a human decision.',
})

const idle: ReviewStatusHint = { status: null, loading: false, error: null }

function review(
  reviewedProposalMessageId: string,
  outcome: string | null,
  status = 'Completed',
): ReviewStatusHint {
  return { status: { reviewedProposalMessageId, outcome, status }, loading: false, error: null }
}

describe('derivePlanningLineage', () => {
  it('is empty without a Planner root', () => {
    expect(derivePlanningLineage([]).depth).toBeNull()
    expect(derivePlanningLineage([first]).depth).toBeNull()
  })

  it('reports depth 0 for a root with no revision', () => {
    const lineage = derivePlanningLineage([root])

    expect(lineage.depth).toBe(0)
    expect(lineage.root?.id).toBe('root')
    expect(lineage.firstRevision).toBeNull()
  })

  it('follows reply links to the first and second revision and the escalation regardless of array order', () => {
    const lineage = derivePlanningLineage([escalation, second, first, root])

    expect(lineage.depth).toBe(2)
    expect(lineage.firstRevision?.id).toBe('first')
    expect(lineage.secondRevision?.id).toBe('second')
    expect(lineage.escalation?.id).toBe('escalation')
  })

  it('never treats a revision of another proposal, a simulated card, or a non-Resolver author as part of the chain', () => {
    const foreignParent = card({ ...first, id: 'foreign-revision', inReplyToMessageId: 'some-other-proposal' })
    const simulated = card({ ...first, id: 'simulated', provenance: 'Simulated' })
    const plannerAuthored = card({ ...first, id: 'planner-authored', actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' } })

    expect(derivePlanningLineage([root, foreignParent, simulated, plannerAuthored]).depth).toBe(0)
  })

  it('does not follow a revision that claims to precede its parent or replies to itself', () => {
    const earlier = card({ ...first, id: 'early', sequence: 0 })
    const selfReply = card({ ...first, id: 'self', inReplyToMessageId: 'self' })

    expect(derivePlanningLineage([root, earlier, selfReply]).depth).toBe(0)
  })

  it('marks two revisions of the same parent as ambiguous and follows neither', () => {
    const duplicate = card({ ...first, id: 'first-again', sequence: 11 })

    const lineage = derivePlanningLineage([root, first, duplicate])

    expect(lineage.ambiguous).toBe(true)
    expect(lineage.firstRevision).toBeNull()
    expect(derivePlanningLineage([root, first, second, card({ ...second, id: 'second-again', sequence: 22 })]).ambiguous).toBe(true)
  })

  it('does not follow a third revision beyond the second', () => {
    const third = card({ ...second, id: 'third', sequence: 30, inReplyToMessageId: 'second' })

    expect(derivePlanningLineage([root, first, second, third]).secondRevision?.id).toBe('second')
  })

  it('ignores an escalation that is not host-constructed, not orchestrator-authored, or not linked to the second revision', () => {
    const humanClaim = card({ ...escalation, id: 'human', provenance: 'HumanSubmitted' })
    const agentClaim = card({ ...escalation, id: 'agent', actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' } })
    const wrongParent = card({ ...escalation, id: 'wrong', inReplyToMessageId: 'first' })

    expect(derivePlanningLineage([root, first, second, humanClaim, agentClaim, wrongParent]).escalation).toBeNull()
  })

  it('tracks a newer independent Planner root instead of an older lineage', () => {
    const newerRoot = card({ sequence: 40, id: 'newer-root' })

    const lineage = derivePlanningLineage([root, first, second, newerRoot])

    expect(lineage.root?.id).toBe('newer-root')
    expect(lineage.depth).toBe(0)
  })
})

describe('selectReviewableProposalMessageId', () => {
  it('targets the root, then the first revision, and nothing once a second revision exists', () => {
    expect(selectReviewableProposalMessageId(derivePlanningLineage([root]))).toBe('root')
    expect(selectReviewableProposalMessageId(derivePlanningLineage([root, first]))).toBe('first')
    expect(selectReviewableProposalMessageId(derivePlanningLineage([root, first, second]))).toBeNull()
    expect(selectReviewableProposalMessageId(derivePlanningLineage([]))).toBeNull()
  })

  it('withholds an ambiguous chain', () => {
    const duplicate = card({ ...first, id: 'first-again', sequence: 11 })

    expect(selectReviewableProposalMessageId(derivePlanningLineage([root, first, duplicate]))).toBeNull()
  })
})

describe('selectImplementablePlanMessageId', () => {
  it('offers the root only after an Accepted review of exactly that root', () => {
    const lineage = derivePlanningLineage([root])

    expect(selectImplementablePlanMessageId(lineage, idle)).toBeNull()
    expect(selectImplementablePlanMessageId(lineage, review('root', 'Accepted'))).toBe('root')
    expect(selectImplementablePlanMessageId(lineage, review('root', 'Challenged'))).toBeNull()
    expect(selectImplementablePlanMessageId(lineage, review('another-proposal', 'Accepted'))).toBeNull()
  })

  it('offers a first revision directly, or after its own Accepted review', () => {
    const lineage = derivePlanningLineage([root, first])

    expect(selectImplementablePlanMessageId(lineage, idle)).toBe('first')
    expect(selectImplementablePlanMessageId(lineage, review('root', 'Challenged'))).toBe('first')
    expect(selectImplementablePlanMessageId(lineage, review('first', 'Accepted'))).toBe('first')
  })

  it('withholds a first revision whose own review Challenged or is still running', () => {
    const lineage = derivePlanningLineage([root, first])

    expect(selectImplementablePlanMessageId(lineage, review('first', 'Challenged'))).toBeNull()
    expect(selectImplementablePlanMessageId(lineage, review('first', null, 'Running'))).toBeNull()
  })

  it('withholds a first revision while its review status is loading or failed', () => {
    const lineage = derivePlanningLineage([root, first])

    expect(selectImplementablePlanMessageId(lineage, { status: null, loading: true, error: null })).toBeNull()
    expect(selectImplementablePlanMessageId(lineage, { status: null, loading: false, error: 'unavailable' })).toBeNull()
  })

  it('never offers a second revision, even beside an escalation or an accepted-looking status', () => {
    const lineage = derivePlanningLineage([root, first, second, escalation])

    expect(selectImplementablePlanMessageId(lineage, idle)).toBeNull()
    expect(selectImplementablePlanMessageId(lineage, review('second', 'Accepted'))).toBeNull()
  })
})

describe('deriveLineageStage', () => {
  it('describes each stage of the first revision from its own review status only', () => {
    const lineage = derivePlanningLineage([root, first])

    expect(deriveLineageStage(derivePlanningLineage([]), idle)).toBe('none')
    expect(deriveLineageStage(derivePlanningLineage([root]), idle)).toBe('root')
    expect(deriveLineageStage(lineage, idle)).toBe('revised')
    expect(deriveLineageStage(lineage, review('root', 'Challenged'))).toBe('revised')
    expect(deriveLineageStage(lineage, review('first', null, 'Running'))).toBe('reviewRunning')
    expect(deriveLineageStage(lineage, review('first', 'Accepted'))).toBe('reviewAccepted')
    expect(deriveLineageStage(lineage, review('first', 'Challenged'))).toBe('reviewChallenged')
    expect(deriveLineageStage(lineage, review('first', 'InvalidStructuredOutput'))).toBe('reviewNotConcluded')
    expect(deriveLineageStage(lineage, { status: null, loading: true, error: null })).toBe('statusUnavailable')
    expect(deriveLineageStage(lineage, { status: null, loading: false, error: 'x' })).toBe('statusUnavailable')
  })

  it('is exhausted once a second revision exists and ambiguous when the chain is not a single line', () => {
    expect(deriveLineageStage(derivePlanningLineage([root, first, second]), idle)).toBe('exhausted')
    const duplicate = card({ ...first, id: 'first-again', sequence: 11 })
    expect(deriveLineageStage(derivePlanningLineage([root, first, duplicate]), idle)).toBe('ambiguous')
  })
})
