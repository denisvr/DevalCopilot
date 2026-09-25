import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { CollaborationTimelineCard } from '../types'
import { AgentCollaboration } from './AgentCollaboration'
import { collaborationMessageEvidenceClient } from '../../../api/clients'

vi.mock('../../../api/clients', () => ({
  collaborationMessageEvidenceClient: vi.fn(),
}))

describe('AgentCollaboration', () => {
  it('shows an explicit empty state before any event has arrived', () => {
    render(<AgentCollaboration runId="run-1" cards={[]} loading={false} error={null} hasSuccessfulResponse />)
    expect(screen.getByText('No collaboration messages yet.')).toBeInTheDocument()
  })

  it('shows loading and failure states without presenting them as an empty timeline', () => {
    const { rerender } = render(
      <AgentCollaboration runId="run-1" cards={[]} loading error={null} hasSuccessfulResponse={false} />,
    )
    expect(screen.getByText('Loading collaboration timeline…')).toBeInTheDocument()
    expect(screen.queryByText('No collaboration messages yet.')).not.toBeInTheDocument()

    rerender(
      <AgentCollaboration
        runId="run-1"
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
        runId="run-1"
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
      fixture({ sequence: 1, actor: { kind: 'Orchestrator', role: null, provider: null }, summary: 'Started' }),
      fixture({ sequence: 2, actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' }, summary: 'Proposal' }),
      fixture({ sequence: 3, actor: { kind: 'Agent', role: 'CriticalReviewer', provider: 'ClaudeCode' }, summary: 'Challenge' }),
    ]

    render(<AgentCollaboration runId="run-1" cards={cards} />)

    expect(screen.getByText('Started').closest('article')).toHaveAttribute('data-align', 'center')
    expect(screen.getByText('Proposal').closest('article')).toHaveAttribute('data-align', 'left')
    expect(screen.getByText('Challenge').closest('article')).toHaveAttribute('data-align', 'right')
  })

  it('renders a readable label for every collaboration message type, distinguishing Acceptance/Challenge from a bare Proposal', () => {
    const cards: CollaborationTimelineCard[] = [
      fixture({ sequence: 1, type: 'Proposal', summary: 'Proposal card' }),
      fixture({ sequence: 2, type: 'Acceptance', summary: 'Acceptance card' }),
      fixture({ sequence: 3, type: 'Challenge', summary: 'Challenge card' }),
      fixture({ sequence: 4, type: 'ExecutionReport', summary: 'Execution report card' }),
      fixture({ sequence: 5, type: 'ReviewFinding', summary: 'Review finding card' }),
      fixture({ sequence: 6, type: 'RevisionResponse', summary: 'Revision response card' }),
    ]

    render(<AgentCollaboration runId="run-1" cards={cards} />)

    expect(screen.getByText('Proposal card').closest('article')).toHaveAttribute('data-type', 'Proposal')
    expect(screen.getByText('Acceptance card').closest('article')).toHaveAttribute('data-type', 'Acceptance')
    expect(screen.getByText('Challenge card').closest('article')).toHaveAttribute('data-type', 'Challenge')
    expect(screen.getByText(/Execution report card/).closest('article')).toHaveTextContent('Execution report')
    expect(screen.getByText(/Review finding card/).closest('article')).toHaveTextContent('Review finding')
    expect(screen.getByText(/Revision response card/).closest('article')).toHaveTextContent('Revision response')
    // Never falls through to the raw enum identifier as user-facing copy.
    expect(screen.queryByText(/ExecutionReport/)).not.toBeInTheDocument()
    expect(screen.queryByText(/ReviewFinding/)).not.toBeInTheDocument()
    expect(screen.queryByText(/RevisionResponse/)).not.toBeInTheDocument()
  })

  it('labels simulation and presents bounded structured details without claiming a provider transcript', () => {
    render(<AgentCollaboration runId="run-1" cards={[fixture({ details: ['rationale: Durable facts'], inReplyToMessageId: 'prior-message' })]} />)

    expect(screen.getByText(/simulated/i)).toBeInTheDocument()
    expect(screen.getByText('The referenced parent message is not present in the currently loaded timeline; the relationship is not verified here.')).toBeInTheDocument()
    expect(screen.getByText('Bounded details')).toBeInTheDocument()
    expect(screen.queryByText(/transcript/i)).not.toBeInTheDocument()
  })

  it('renders the four typed card shapes with their meaningful bounded fields', () => {
    const cards = [
      fixture({
        sequence: 1,
        id: 'challenge-1',
        type: 'Challenge',
        summary: 'The plan omits rollback evidence.',
        structuredContentJson: JSON.stringify({
          disputedItem: 'Rollback step',
          materialImpact: 'A failed deployment could be unrecoverable.',
          reasoning: 'The verification plan has no rollback check.',
          alternativeOrQuestion: 'Can the plan add a rollback rehearsal?',
        }),
      }),
      fixture({
        sequence: 2,
        id: 'decision-1',
        type: 'Decision',
        summary: 'Add the rollback rehearsal.',
        structuredContentJson: JSON.stringify({
          resolution: 'partiallyAccepted',
          rationale: 'The challenge identifies a material gap.',
          resultingPlanChanges: 'Add a rollback rehearsal to verification.',
          nextAction: 'Update the proposal.',
        }),
      }),
      fixture({
        sequence: 3,
        id: 'finding-1',
        type: 'ReviewFinding',
        summary: 'The input guard is missing.',
        structuredContentJson: JSON.stringify({
          severity: 'high',
          category: 'testCoverage',
          evidence: 'The boundary test accepts an empty input.',
          requiredChange: 'Add the missing boundary test.',
        }),
      }),
      fixture({
        sequence: 4,
        id: 'response-1',
        type: 'RevisionResponse',
        summary: 'The boundary test was added.',
        inReplyToMessageId: 'finding-1',
        structuredContentJson: JSON.stringify({
          disposition: 'Fixed',
          evidence: 'The new test rejects empty input.',
          resultingSourceChanges: 'Added the boundary validation test.',
        }),
      }),
    ]

    render(<AgentCollaboration runId="run-1" cards={cards} />)

    expect(screen.getByText('Disputed item')).toBeInTheDocument()
    expect(screen.getByText('Resolution')).toBeInTheDocument()
    expect(screen.getByText('Partially accepted')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('Test coverage')).toBeInTheDocument()
    expect(screen.getByText('Disposition')).toBeInTheDocument()
    expect(screen.getByText('In reply to Review finding message finding-1: The input guard is missing.')).toBeInTheDocument()
    expect(screen.queryByText(/affected/i)).not.toBeInTheDocument()
  })

  it('does not invent typed fields from malformed or unknown structured content', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({
            type: 'ReviewFinding',
            structuredContentJson: '{"severity":"urgent","category":"correctness","evidence":"Evidence","requiredChange":"Change"}',
          }),
          fixture({
            sequence: 2,
            id: 'decision-unknown',
            type: 'Decision',
            structuredContentJson: '{"resolution":"accepted","rationale":"Rationale","unexpected":"not a protocol field"}',
          }),
        ]}
      />,
    )

    expect(screen.getAllByText('Structured details unavailable.')).toHaveLength(2)
    expect(screen.queryByText('Urgent')).not.toBeInTheDocument()
    expect(screen.queryByText('Resolution')).not.toBeInTheDocument()
  })

  // ChallengeResolutionOutputSchema's own `resolution` enum is closed to exactly "accepted",
  // "partiallyAccepted", and "rejected" — a well-formed Decision payload (correct field count and
  // names) whose resolution value is anything else must never render as a valid typed Decision,
  // even though every other field is a plain, otherwise-acceptable string.
  it('does not render a Decision whose resolution is not one of the protocol closed values', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({
            type: 'Decision',
            structuredContentJson: JSON.stringify({
              resolution: 'approved',
              rationale: 'Rationale',
              resultingPlanChanges: 'None',
              nextAction: 'None',
            }),
          }),
        ]}
      />,
    )

    expect(screen.getByText('Structured details unavailable.')).toBeInTheDocument()
    expect(screen.queryByText('Resolution')).not.toBeInTheDocument()
    expect(screen.queryByText('approved')).not.toBeInTheDocument()
  })

  it('renders untrusted summaries and fields as text, never as markup', () => {
    const text = '<script>alert("unsafe")</script>'
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({
            type: 'Challenge',
            summary: text,
            structuredContentJson: JSON.stringify({
              disputedItem: text,
              materialImpact: 'Impact',
              reasoning: 'Reasoning',
              alternativeOrQuestion: 'Question',
            }),
          }),
        ]}
      />,
    )

    expect(screen.getAllByText(text)).toHaveLength(2)
    expect(document.querySelector('script')).toBeNull()
  })

  it('resolves a reply only by the exact parent id in the loaded run timeline', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ id: 'finding-a', type: 'ReviewFinding', summary: 'Finding A' }),
          fixture({
            sequence: 2,
            id: 'response-a',
            type: 'RevisionResponse',
            inReplyToMessageId: 'finding-a',
            summary: 'Response A',
          }),
          fixture({
            sequence: 3,
            id: 'response-b',
            type: 'RevisionResponse',
            inReplyToMessageId: 'finding-outside-window',
            summary: 'Response B',
          }),
        ]}
      />,
    )

    expect(screen.getByText('In reply to Review finding message finding-a: Finding A')).toBeInTheDocument()
    expect(screen.getByText('The referenced parent message is not present in the currently loaded timeline; the relationship is not verified here.')).toBeInTheDocument()
  })

  // A matching id proves nothing by itself: the backend's own closed reply-semantics table (see
  // docs/architecture/agent-collaboration-protocol.md) requires the parent to both precede the
  // reply and be an allowed parent type for it. A loaded id that fails either check is never
  // described as "in reply to" — only as an observed, unverified id match.
  it('never describes a later (forward) message as a verified reply parent, even when the id matches', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ sequence: 1, id: 'response-early', type: 'RevisionResponse', inReplyToMessageId: 'finding-later', summary: 'Response early' }),
          fixture({ sequence: 2, id: 'finding-later', type: 'ReviewFinding', summary: 'Finding later' }),
        ]}
      />,
    )

    expect(screen.getByText('References Review finding message finding-later; not verified as an earlier, protocol-compatible parent.')).toBeInTheDocument()
    expect(screen.queryByText(/^In reply to/)).not.toBeInTheDocument()
  })

  it('never describes an earlier message of a protocol-incompatible type as a verified reply parent', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ sequence: 1, id: 'proposal-1', type: 'Proposal', summary: 'A bounded proposal' }),
          // RevisionResponse may only reply to a ReviewFinding per the protocol's closed table —
          // never directly to a Proposal, even though it is earlier and its id matches exactly.
          fixture({ sequence: 2, id: 'response-1', type: 'RevisionResponse', inReplyToMessageId: 'proposal-1', summary: 'Response' }),
        ]}
      />,
    )

    expect(screen.getByText('References Proposal message proposal-1; not verified as an earlier, protocol-compatible parent.')).toBeInTheDocument()
    expect(screen.queryByText(/^In reply to/)).not.toBeInTheDocument()
  })

  // Mirrors CollaborationMessageReplyPolicy exactly: a revised Proposal's only valid parent is
  // the prior Proposal it supersedes (a root Proposal instead has no parent at all).
  it('verifies a revised Proposal replying to an earlier Proposal', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ sequence: 1, id: 'proposal-original', type: 'Proposal', summary: 'The original proposal' }),
          fixture({ sequence: 2, id: 'proposal-revised', type: 'Proposal', inReplyToMessageId: 'proposal-original', summary: 'The revised proposal' }),
        ]}
      />,
    )

    expect(screen.getByText('In reply to Proposal message proposal-original: The original proposal')).toBeInTheDocument()
  })

  // Mirrors CollaborationMessageReplyPolicy exactly: ExecutionReport's Domain policy allows a
  // Decision or a Proposal parent, and the real Increment 4 Implementer path always replies
  // directly to the implemented Proposal — never a Decision, since neither eligible resolved-plan
  // form (an accepted original proposal, or a resolved revised proposal) has a single Decision to
  // reply to.
  it('verifies an ExecutionReport replying to the implemented Proposal, the real Increment 4 path', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ sequence: 1, id: 'proposal-1', type: 'Proposal', summary: 'The implemented proposal' }),
          fixture({ sequence: 2, id: 'report-1', type: 'ExecutionReport', inReplyToMessageId: 'proposal-1', summary: 'Implemented the proposal' }),
        ]}
      />,
    )

    expect(screen.getByText('In reply to Proposal message proposal-1: The implemented proposal')).toBeInTheDocument()
  })

  // ReviewApproval (CollaborationMessageType = 9) is a real, currently recorded message type
  // whose Domain policy (CollaborationMessageReplyPolicy.IsAllowedParent) allows only an
  // ExecutionReport parent — a Codex implementation review's approval fact always replies
  // directly to the Execution report it approved.
  it('displays a readable label and a verified relationship for a Review approval replying to its Execution report', () => {
    render(
      <AgentCollaboration
        runId="run-1"
        cards={[
          fixture({ sequence: 1, id: 'report-1', type: 'ExecutionReport', summary: 'Implemented the proposal' }),
          fixture({ sequence: 2, id: 'approval-1', type: 'ReviewApproval', inReplyToMessageId: 'report-1', summary: 'The implementation is approved.' }),
        ]}
      />,
    )

    expect(screen.getByText(/Review approval/)).toBeInTheDocument()
    expect(screen.queryByText(/ReviewApproval/)).not.toBeInTheDocument()
    expect(screen.getByText('In reply to Execution report message report-1: Implemented the proposal')).toBeInTheDocument()
  })

  // The Increment 4 drill-down control (see CollaborationEvidenceDrilldown) is only ever offered
  // for a card the backend can truthfully resolve to one exact Attempt: ProviderObserved
  // provenance with a non-null attemptId. Every other legitimate shape (Human, Orchestrator,
  // Simulated) never shows the control at all, not even a disabled one.
  describe('the attempt evidence drill-down control', () => {
    it('is offered for a ProviderObserved card with a linked attempt', () => {
      render(
        <AgentCollaboration
          runId="run-1"
          cards={[fixture({ provenance: 'ProviderObserved', attemptId: 'attempt-1' })]}
        />,
      )

      expect(screen.getByText('Attempt evidence')).toBeInTheDocument()
    })

    it('is never offered for a Simulated card even if it somehow carried an attemptId', () => {
      render(
        <AgentCollaboration
          runId="run-1"
          cards={[fixture({ provenance: 'Simulated', attemptId: 'attempt-1' })]}
        />,
      )

      expect(screen.queryByText('Attempt evidence')).not.toBeInTheDocument()
    })

    it('is never offered for a ProviderObserved card with no linked attempt', () => {
      render(
        <AgentCollaboration
          runId="run-1"
          cards={[fixture({ provenance: 'ProviderObserved', attemptId: null })]}
        />,
      )

      expect(screen.queryByText('Attempt evidence')).not.toBeInTheDocument()
    })

    it('is never offered for a HostConstructed or HumanSubmitted card', () => {
      render(
        <AgentCollaboration
          runId="run-1"
          cards={[
            fixture({ sequence: 1, id: 'a', provenance: 'HostConstructed', attemptId: null, type: 'Escalation' }),
            fixture({ sequence: 2, id: 'b', provenance: 'HumanSubmitted', attemptId: null, type: 'HumanInstruction' }),
          ]}
        />,
      )

      expect(screen.queryByText('Attempt evidence')).not.toBeInTheDocument()
    })

    it('fetches using the card own messageId and the run id when expanded, never a substituted id', async () => {
      const getCollaborationMessageEvidence = vi.fn().mockReturnValue(new Promise(() => {}))
      vi.mocked(collaborationMessageEvidenceClient).mockReturnValue({
        getCollaborationMessageEvidence,
      } as unknown as ReturnType<typeof collaborationMessageEvidenceClient>)

      render(
        <AgentCollaboration
          runId="the-run"
          cards={[fixture({ id: 'the-message', provenance: 'ProviderObserved', attemptId: 'attempt-1' })]}
        />,
      )

      const summary = screen.getByText('Attempt evidence')
      fireEvent.click(summary)

      await waitFor(() => expect(getCollaborationMessageEvidence).toHaveBeenCalledWith('the-run', 'the-message'))
    })
  })

  function fixture(overrides: Partial<CollaborationTimelineCard> = {}): CollaborationTimelineCard {
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
      provenance: 'Simulated',
      occurredAtUtc: '2026-09-16T12:00:00Z',
      ...overrides,
    }
  }
})
