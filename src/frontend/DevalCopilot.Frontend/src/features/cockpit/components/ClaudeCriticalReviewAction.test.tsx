import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ClaudeCriticalReviewAttemptStatusResponse } from '../../../api/generated/api-client'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'

describe('ClaudeCriticalReviewAction', () => {
  it('renders nothing when no real Codex Proposal exists yet for this run', () => {
    const { container } = render(
      <ClaudeCriticalReviewAction
        proposalMessageId={null}
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('offers the request action once a real Proposal exists and no attempt has been made yet', () => {
    const onRequest = vi.fn()
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={onRequest}
        globalClaimBlock={null}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Request Claude review' }))
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('shows a visibly-working pending state before dispatch, with no action button', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Running',
            reviewedProposalMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByText(/pending/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
  })

  it('shows a visibly-working running state once the attempt has been dispatched', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Running',
            reviewedProposalMessageId: 'message-1',
            dispatchedAtUtc: new Date('2026-09-17T00:00:00Z'),
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByText(/running/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
  })

  it.each([
    ['Accepted', 'Proposal accepted'],
    ['Challenged', 'Proposal challenged'],
    ['InputAlreadyReviewed', 'Proposal already reviewed by another attempt'],
  ])('withholds the request action once the current Proposal has already reached a durable %s outcome', (outcome, label) => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome,
            reviewedProposalMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByText(`Last attempt #1: ${label}.`)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
  })

  it('allows requesting a fresh review once a newer Proposal supersedes an already-reviewed one', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-2"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome: 'Accepted',
            reviewedProposalMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('button', { name: 'Request Claude review' })).toBeInTheDocument()
  })

  it.each([
    ['SourceChanged', 'Source changed before the review completed'],
    ['InvalidStructuredOutput', 'Claude returned an invalid structured response'],
    ['ProviderInvocationFailed', 'Claude could not be invoked'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for dispatch'],
  ])('labels the %s terminal outcome safely and still allows a retry', (outcome, label) => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 2,
            status: 'Failed',
            outcome,
            reviewedProposalMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByText(`Last attempt #2: ${label}.`)).toBeInTheDocument()
    // Never fall through to the raw enum identifier as user-facing copy.
    expect(screen.queryByText(new RegExp(outcome))).not.toBeInTheDocument()
    // A terminal failure allows requesting a new review.
    expect(screen.getByRole('button', { name: 'Request Claude review' })).toBeInTheDocument()
  })

  it('disables the request button while a request is in flight', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={true}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()
  })

  it('surfaces a safe request-failure message without discarding the last known status', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={
          new ClaudeCriticalReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Failed',
            outcome: 'ProviderInvocationFailed',
            reviewedProposalMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError="This run already has a Claude critical-review attempt in progress."
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('This run already has a Claude critical-review attempt in progress.')
    expect(screen.getByText(/Last attempt #1/)).toBeInTheDocument()
  })

  it('surfaces a safe status-read failure', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError="Claude critical review attempt status is unavailable."
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Claude critical review attempt status is unavailable.')
  })

  it('withholds the request and attributes the block to the global run-wide budget, never this role', () => {
    render(
      <ClaudeCriticalReviewAction
        proposalMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={{ reason: 'TimeBudgetExhausted' }}
      />,
    )

    expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/invocation-time budget/i)
  })
})
