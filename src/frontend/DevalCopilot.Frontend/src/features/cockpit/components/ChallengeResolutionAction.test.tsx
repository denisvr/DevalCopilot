import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ChallengeResolutionAttemptStatusResponse } from '../../../api/generated/api-client'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'

describe('ChallengeResolutionAction', () => {
  it('renders nothing when there is no latest challenged review to resolve', () => {
    const { container } = render(
      <ChallengeResolutionAction
        challengedReviewAttemptId={null}
        reviewedProposalMessageId={null}
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

  it('offers the request action once a challenged review exists and no resolution has been made yet', () => {
    const onRequest = vi.fn()
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={onRequest}
        globalClaimBlock={null}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Resolve challenges with Codex' }))
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('shows a visibly-working pending state before dispatch, with no action button', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Running',
            originalProposalMessageId: 'proposal-1',
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
    expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
  })

  it('shows a visibly-working running state once the attempt has been dispatched', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Running',
            originalProposalMessageId: 'proposal-1',
            dispatchedAtUtc: new Date('2026-09-18T00:00:00Z'),
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
    expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
  })

  it('withholds the request action once the current review already reached a durable Resolved outcome', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Completed',
            outcome: 'Resolved',
            originalProposalMessageId: 'proposal-1',
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

    expect(screen.getByText('Last attempt #3: Challenges resolved.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
  })

  it('withholds the request action once the current review already reached a durable InputAlreadyResolved outcome', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'InputAlreadyResolved',
            originalProposalMessageId: 'proposal-1',
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

    expect(screen.getByText('Last attempt #3: Challenges already resolved by another attempt.')).toBeInTheDocument()
    // Never falls through to the raw enum identifier as user-facing copy.
    expect(screen.queryByText(/InputAlreadyResolved/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
  })

  it('allows requesting a fresh resolution once a newer challenged review supersedes an already-resolved one', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-2"
        reviewedProposalMessageId="proposal-2"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Completed',
            outcome: 'Resolved',
            originalProposalMessageId: 'proposal-1',
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

    expect(screen.getByRole('button', { name: 'Resolve challenges with Codex' })).toBeInTheDocument()
  })

  it.each([
    ['SourceChanged', 'Source changed before the resolution completed'],
    ['InvalidStructuredOutput', 'Codex returned an invalid structured response'],
    ['ProviderInvocationFailed', 'Codex could not be invoked'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for dispatch'],
  ])('labels the %s terminal outcome safely and still allows a retry', (outcome, label) => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 4,
            status: 'Failed',
            outcome,
            originalProposalMessageId: 'proposal-1',
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

    expect(screen.getByText(`Last attempt #4: ${label}.`)).toBeInTheDocument()
    expect(screen.queryByText(new RegExp(outcome))).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Resolve challenges with Codex' })).toBeInTheDocument()
  })

  it('disables the request button while a request is in flight', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
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
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={
          new ChallengeResolutionAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'ProviderInvocationFailed',
            originalProposalMessageId: 'proposal-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError="This challenged review already has a successful resolution."
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('This challenged review already has a successful resolution.')
    expect(screen.getByText(/Last attempt #3/)).toBeInTheDocument()
  })

  it('surfaces a safe status-read failure', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError="Challenge resolution attempt status is unavailable."
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Challenge resolution attempt status is unavailable.')
  })

  it('withholds the request and attributes the block to the global run-wide budget, never this role', () => {
    render(
      <ChallengeResolutionAction
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={{ reason: 'BudgetProjectionUnavailable' }}
      />,
    )

    expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/budget status is confirmed/i)
  })
})
