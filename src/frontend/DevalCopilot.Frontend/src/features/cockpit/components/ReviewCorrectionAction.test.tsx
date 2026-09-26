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
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
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
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
      />,
    )
    expect(screen.getByText(/running/)).toBeInTheDocument()
  })

  it('disables the request while status is loading or a request is in flight', () => {
    const { rerender } = renderAction({ status: null, statusLoading: true })
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
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
      />,
    )
    expect(screen.getByRole('button')).toBeDisabled()
  })

  it('offers the initial correction request without projecting a last attempt', () => {
    renderAction({
      status: status({
        hasAttempt: false,
        attemptId: undefined,
        attemptNumber: undefined,
        status: undefined,
        outcome: undefined,
        revisionResponseCount: 0,
        reviewableExecutionReportMessageId: 'initial-report',
        budgetExhausted: false,
        hasAvailableHumanAuthorization: false,
      }),
    })

    expect(screen.getByRole('button', { name: 'Request review correction' })).toBeInTheDocument()
    expect(screen.queryByText(/Last correction/)).not.toBeInTheDocument()
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
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
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

  it('shows human attention and hides provider request when the budget is exhausted', () => {
    const onRequest = vi.fn()
    const onAuthorize = vi.fn()
    renderAction({
      status: status({ budgetExhausted: true, escalationId: 'escalation-1', escalationMessageId: 'message-1' }),
      onRequest,
      onAuthorize,
    })

    expect(screen.getByText(/No provider invocation will occur/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Authorize one additional correction/i }))
    expect(onAuthorize).toHaveBeenCalledOnce()
    expect(onRequest).not.toHaveBeenCalled()
  })

  it('shows exactly one authorized continuation without exposing raw reason codes', () => {
    renderAction({ status: status({ budgetExhausted: true, hasAvailableHumanAuthorization: true }) })

    expect(screen.getByRole('status')).toHaveTextContent('One additional correction attempt is authorized.')
    expect(screen.getByRole('button', { name: 'Request review correction' })).toBeInTheDocument()
    expect(screen.queryByText(/BudgetExhausted/)).not.toBeInTheDocument()
  })

  it('creates a human escalation when the current review budget is exhausted without one', () => {
    const onRequest = vi.fn()
    renderAction({ status: status({ budgetExhausted: true, escalationId: undefined, outcome: undefined, status: undefined }), onRequest })

    fireEvent.click(screen.getByRole('button', { name: 'Create human escalation' }))
    expect(onRequest).toHaveBeenCalledOnce()
    expect(screen.queryByText(/No provider invocation will occur/)).not.toBeInTheDocument()
  })

  it('shows only the terminal result for a settled current review when budget remains', () => {
    renderAction({ status: status({ budgetExhausted: false }) })

    expect(screen.getByText(/Correction applied/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request review correction' })).not.toBeInTheDocument()
  })

  it('does not project Review A escalation into the current Review B action', () => {
    const onRequest = vi.fn()
    const onAuthorize = vi.fn()
    renderAction({
      status: status({
        implementationReviewAttemptId: 'review-b',
        budgetExhausted: true,
        escalationId: 'escalation-a',
        hasAvailableHumanAuthorization: false,
      }),
      onRequest,
      onAuthorize,
    })

    expect(screen.getByRole('button', { name: 'Request review correction' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Authorize one additional correction/i })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Request review correction' }))
    expect(onRequest).toHaveBeenCalledOnce()
    expect(onAuthorize).not.toHaveBeenCalled()
  })

  it('withholds the plain request and attributes the block to the global run-wide budget, never this role', () => {
    const onRequest = vi.fn()
    renderAction({
      status: status({ hasAttempt: false, status: undefined, outcome: undefined, budgetExhausted: false }),
      onRequest,
      globalClaimBlock: { reason: 'CountBudgetExhausted' },
    })

    expect(screen.queryByRole('button', { name: 'Request review correction' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/run-wide agent claim budget/i)
  })

  it('never presents an available review-correction authorization as an effective path around a known global block', () => {
    renderAction({
      status: status({ budgetExhausted: true, hasAvailableHumanAuthorization: true }),
      globalClaimBlock: { reason: 'TimeBudgetExhausted' },
    })

    // The review-correction-specific authorization (ADR-0010) exists, but the run-wide
    // ADR-0013 invocation-time budget is separately exhausted — the request button must not
    // reappear as though the authorization overrides that global hard stop.
    expect(screen.queryByRole('button', { name: 'Request review correction' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/invocation-time budget/i)
    expect(screen.getByRole('status')).toHaveTextContent(/does not override/i)
  })

  it('never lets a granted escalation authorization proceed while a known global block is present', () => {
    const onAuthorize = vi.fn()
    renderAction({
      status: status({ budgetExhausted: true, escalationId: 'escalation-1', hasAvailableHumanAuthorization: false }),
      onAuthorize,
      globalClaimBlock: { reason: 'CountBudgetExhausted' },
    })

    expect(screen.queryByRole('button', { name: /Authorize one additional correction/i })).not.toBeInTheDocument()
    expect(screen.getByText(/This authorization cannot proceed/i)).toBeInTheDocument()
  })

  it('withholds creating a human escalation while a global count-budget block is present, since the handler checks the global budget before its escalation branch', () => {
    renderAction({
      status: status({ budgetExhausted: true, escalationId: undefined, outcome: undefined, status: undefined }),
      globalClaimBlock: { reason: 'CountBudgetExhausted' },
    })

    expect(screen.queryByRole('button', { name: 'Create human escalation' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/run-wide agent claim budget/i)
  })

  it('withholds creating a human escalation while a global time-budget block is present', () => {
    renderAction({
      status: status({ budgetExhausted: true, escalationId: undefined, outcome: undefined, status: undefined }),
      globalClaimBlock: { reason: 'TimeBudgetExhausted' },
    })

    expect(screen.queryByRole('button', { name: 'Create human escalation' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/invocation-time budget/i)
  })

  it('still permits creating a human escalation for review-correction-specific budget exhaustion when no global block is present', () => {
    const onRequest = vi.fn()
    renderAction({
      status: status({ budgetExhausted: true, escalationId: undefined, outcome: undefined, status: undefined }),
      onRequest,
      globalClaimBlock: null,
    })

    fireEvent.click(screen.getByRole('button', { name: 'Create human escalation' }))
    expect(onRequest).toHaveBeenCalledOnce()
  })

  it('withholds the plain request and attributes the block to this specific request\'s own time fit, never the global budget', () => {
    const onRequest = vi.fn()
    renderAction({
      status: status({ hasAttempt: false, status: undefined, outcome: undefined, budgetExhausted: false }),
      onRequest,
      timeFit: { reason: 'DoesNotFit' },
    })

    expect(screen.queryByRole('button', { name: 'Request review correction' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/insufficient reserved invocation time/i)
  })

  it('withholds creating a human escalation while this specific request\'s own time fit is blocked, mirroring the global-block withholding', () => {
    renderAction({
      status: status({ budgetExhausted: true, escalationId: undefined, outcome: undefined, status: undefined }),
      timeFit: { reason: 'DoesNotFit' },
    })

    expect(screen.queryByRole('button', { name: 'Create human escalation' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/insufficient reserved invocation time/i)
  })

  it('shows both the global block and the candidate-fit block together when both apply, never hiding one for the other', () => {
    renderAction({
      status: status({ hasAttempt: false, status: undefined, outcome: undefined, budgetExhausted: false }),
      globalClaimBlock: { reason: 'CountBudgetExhausted' },
      timeFit: { reason: 'DoesNotFit' },
    })

    expect(screen.queryByRole('button', { name: 'Request review correction' })).not.toBeInTheDocument()
    expect(screen.getByText(/run-wide agent claim budget/i)).toBeInTheDocument()
    expect(screen.getByText(/insufficient reserved invocation time/i)).toBeInTheDocument()
  })

  it('still permits the plain request when the candidate time fits and no global block is present', () => {
    renderAction({
      status: status({ hasAttempt: false, status: undefined, outcome: undefined, budgetExhausted: false }),
      timeFit: { reason: 'Fits' },
    })

    expect(screen.getByRole('button', { name: 'Request review correction' })).toBeInTheDocument()
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
