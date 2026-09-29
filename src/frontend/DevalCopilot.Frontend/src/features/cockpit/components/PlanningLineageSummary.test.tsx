import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { derivePlanningLineage } from '../derivePlanningLineage'
import type { ReviewStatusHint } from '../derivePlanningLineage'
import type { CollaborationTimelineCard } from '../types'
import { PlanningLineageSummary } from './PlanningLineageSummary'

function card(overrides: Partial<CollaborationTimelineCard>): CollaborationTimelineCard {
  return {
    sequence: 1,
    id: 'root',
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

const resolver = { kind: 'Agent', role: 'Resolver', provider: 'Codex' }
const root = card({})
const first = card({ sequence: 10, id: 'first', actor: resolver, inReplyToMessageId: 'root' })
const second = card({ sequence: 20, id: 'second', actor: resolver, inReplyToMessageId: 'first' })
const escalation = card({
  sequence: 21,
  id: 'escalation',
  type: 'Escalation',
  actor: { kind: 'Orchestrator', role: null, provider: null },
  provenance: 'HostConstructed',
  inReplyToMessageId: 'second',
  summary: 'The second challenge-resolution round is complete and needs a human decision.',
})
const idle: ReviewStatusHint = { status: null, loading: false, error: null }

function renderSummary(cards: CollaborationTimelineCard[], review: ReviewStatusHint = idle) {
  return render(<PlanningLineageSummary lineage={derivePlanningLineage(cards)} review={review} />)
}

describe('PlanningLineageSummary', () => {
  it('renders nothing without a revision', () => {
    const { container } = renderSummary([root])

    expect(container).toBeEmptyDOMElement()
  })

  it('offers the optional second review and the direct implementation as choices for a first revision', () => {
    renderSummary([root, first])

    expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/optional second Claude review/)
    expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/implemented directly/)
  })

  it('states truthfully that a challenged second review blocks implementation and leaves one last resolution', () => {
    renderSummary([root, first], { status: { reviewedProposalMessageId: 'first', outcome: 'Challenged', status: 'Completed' }, loading: false, error: null })

    expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/implementing it is blocked/)
    expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/last round/)
  })

  it('does not claim a next step while the second-review status is unavailable', () => {
    renderSummary([root, first], { status: null, loading: false, error: 'Claude critical review attempt status is unavailable.' })

    expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/status is not available/)
    expect(screen.getByLabelText('Proposal lineage')).not.toHaveTextContent(/may be implemented/)
  })

  it('shows the end of the lineage and the escalation as a human decision that is not an approval', () => {
    renderSummary([root, first, second, escalation])

    const region = screen.getByLabelText('Proposal lineage')
    expect(region).toHaveTextContent(/no further review or automatic resolution/)
    expect(region).toHaveTextContent(/cannot be implemented through this lineage/)
    expect(screen.getByRole('status')).toHaveTextContent('Human decision required: The second challenge-resolution round is complete')
    expect(screen.getByRole('status')).toHaveTextContent(/not an approval/)
  })

  it('still states a human decision is required when the escalation record is not in the loaded timeline', () => {
    renderSummary([root, first, second])

    expect(screen.getByRole('status')).toHaveTextContent(/not present in the currently loaded timeline/)
    expect(screen.getByRole('status')).toHaveTextContent(/not an approval/)
  })
})
