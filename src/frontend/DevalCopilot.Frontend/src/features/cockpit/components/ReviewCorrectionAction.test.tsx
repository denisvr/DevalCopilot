import { fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ReviewCorrectionAttemptStatusResponse } from '../../../api/generated/api-client'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'

const currentReviewId = 'review-current'

function status(overrides: Partial<ReviewCorrectionAttemptStatusResponse> = {}) {
  return new ReviewCorrectionAttemptStatusResponse({
    hasAttempt: true,
    attemptId: 'correction-1',
    attemptNumber: 1,
    implementationReviewAttemptId: currentReviewId,
    status: 'Completed',
    outcome: 'CorrectionApplied',
    revisionResponseCount: 1,
    ...overrides,
  })
}

function renderAction(overrides: Partial<ComponentProps<typeof ReviewCorrectionAction>> = {}) {
  return render(
    <ReviewCorrectionAction
      reviewAttemptId={currentReviewId}
      reviewOutcome="ReviewChangesRequested"
      status={null}
      statusLoading={false}
      statusError={null}
      requesting={false}
      requestError={null}
      onRequest={vi.fn()}
      {...overrides}
    />,
  )
}

describe('ReviewCorrectionAction', () => {
  it('is hidden unless the applicable review requested changes', () => {
    const { container } = renderAction({ reviewOutcome: 'ReviewApproved' })
    expect(container).toBeEmptyDOMElement()
  })

  it.each([
    ['CorrectionApplied', 'Correction applied'],
    ['CorrectionNoChangesProduced', 'No correction changes produced'],
    ['InputAlreadyCorrected', 'This review was already corrected'],
    ['CorrectionHeadChanged', 'Unexpected HEAD change requires attention'],
    ['InvalidStructuredOutput', 'The Implementer returned an invalid correction response'],
    ['ProviderInvocationFailed', 'The Implementer could not be invoked for correction'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for correction'],
  ])('renders terminal outcome %s', (outcome, label) => {
    renderAction({ status: status({ outcome }) })
    expect(screen.getByText(new RegExp(label))).toBeInTheDocument()
  })

  it('renders Pending and Running without exposing a request button', () => {
    const { rerender } = renderAction({ status: status({ status: 'Running', dispatchedAtUtc: undefined, outcome: undefined }) })
    expect(screen.getByText(/pending/)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()

    rerender(
      <ReviewCorrectionAction
        reviewAttemptId={currentReviewId}
        reviewOutcome="ReviewChangesRequested"
        status={status({ status: 'Running', dispatchedAtUtc: new Date(), outcome: undefined })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )
    expect(screen.getByText(/running/)).toBeInTheDocument()
  })

  it('disables the request while status is loading or a request is in flight', () => {
    const { rerender } = renderAction({ statusLoading: true })
    expect(screen.getByRole('button')).toBeDisabled()
    rerender(
      <ReviewCorrectionAction
        reviewAttemptId={currentReviewId}
        reviewOutcome="ReviewChangesRequested"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting
        requestError={null}
        onRequest={vi.fn()}
      />,
    )
    expect(screen.getByRole('button')).toBeDisabled()
  })

  it('shows safe request and status errors', () => {
    const { rerender } = renderAction({ statusError: 'Review correction status is unavailable.' })
    expect(screen.getByRole('status')).toHaveTextContent('Review correction status is unavailable.')
    rerender(
      <ReviewCorrectionAction
        reviewAttemptId={currentReviewId}
        reviewOutcome="ReviewChangesRequested"
        status={null}
        statusLoading={false}
        statusError="Review correction status is unavailable."
        requesting={false}
        requestError="A review correction could not be requested for this run."
        onRequest={vi.fn()}
      />,
    )
    expect(screen.getByRole('status')).toHaveTextContent('A review correction could not be requested for this run.')
  })

  it('does not let a correction belonging to another review suppress the current action', () => {
    const onRequest = vi.fn()
    renderAction({ status: status({ implementationReviewAttemptId: 'review-old' }), onRequest })
    fireEvent.click(screen.getByRole('button'))
    expect(onRequest).toHaveBeenCalledOnce()
  })

  it('uses role-first wording and does not persist identifiers or output in browser storage or URL', () => {
    const { container } = renderAction({ status: status({ outcome: 'InvalidStructuredOutput' }) })
    expect(container.textContent).not.toContain('Claude')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(document.cookie).toBe('')
    expect(window.location.search).toBe('')
  })
})
