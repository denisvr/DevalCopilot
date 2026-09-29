import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ReviewCorrectionAttemptStatusResponse } from '../../../api/generated/api-client'
import { MAXIMUM_GUIDANCE_LENGTH, ReviewCorrectionGuidanceEntry } from './ReviewCorrectionGuidanceEntry'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'

const GUIDANCE_LABEL = 'Optional guidance for the correction'

function exhaustedStatus(escalationId = 'escalation-1', overrides: Partial<ReviewCorrectionAttemptStatusResponse> = {}) {
  return new ReviewCorrectionAttemptStatusResponse({
    hasAttempt: true,
    attemptId: 'correction-1',
    attemptNumber: 2,
    implementationReviewAttemptId: 'review-1',
    status: 'Failed',
    budgetExhausted: true,
    escalationId,
    hasAvailableHumanAuthorization: false,
    ...overrides,
  })
}

type ActionProps = ComponentProps<typeof ReviewCorrectionAction>

function actionElement(props: Partial<ActionProps> = {}, status: ReviewCorrectionAttemptStatusResponse | null = exhaustedStatus()) {
  return (
    <ReviewCorrectionAction
      reviewAttemptId="review-1"
      reviewOutcome="ReviewChangesRequested"
      status={status}
      statusLoading={false}
      statusError={null}
      requesting={false}
      requestError={null}
      onRequest={vi.fn()}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
      {...props}
    />
  )
}

describe('ReviewCorrectionGuidanceEntry', () => {
  it('submits the raw draft once and clears it only after acceptance', async () => {
    const onSubmit = vi.fn().mockResolvedValue(true)
    render(<ReviewCorrectionGuidanceEntry authorizing={false} statusLoading={false} onSubmit={onSubmit} />)
    const box = screen.getByLabelText(GUIDANCE_LABEL)

    fireEvent.change(box, { target: { value: '  Keep the fix small.  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))

    expect(onSubmit).toHaveBeenCalledExactlyOnceWith('  Keep the fix small.  ')
    await waitFor(() => expect(box).toHaveValue(''))
  })

  it('keeps the draft when the server refuses it', async () => {
    const onSubmit = vi.fn().mockResolvedValue(false)
    render(<ReviewCorrectionGuidanceEntry authorizing={false} statusLoading={false} onSubmit={onSubmit} />)
    const box = screen.getByLabelText(GUIDANCE_LABEL)

    fireEvent.change(box, { target: { value: 'Try again later.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledOnce())
    expect(box).toHaveValue('Try again later.')
  })

  it('validates blank and over-long drafts locally without calling the server', () => {
    const onSubmit = vi.fn().mockResolvedValue(true)
    render(<ReviewCorrectionGuidanceEntry authorizing={false} statusLoading={false} onSubmit={onSubmit} />)
    const box = screen.getByLabelText(GUIDANCE_LABEL)

    fireEvent.change(box, { target: { value: '   \n ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
    expect(screen.getByRole('status')).toHaveTextContent('Enter guidance')

    fireEvent.change(box, { target: { value: 'x'.repeat(MAXIMUM_GUIDANCE_LENGTH + 1) } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
    expect(screen.getByRole('status')).toHaveTextContent(`at most ${MAXIMUM_GUIDANCE_LENGTH} characters`)
    expect(onSubmit).not.toHaveBeenCalled()

    fireEvent.change(box, { target: { value: 'x'.repeat(MAXIMUM_GUIDANCE_LENGTH) } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
    expect(onSubmit).toHaveBeenCalledOnce()
  })

  it('shows a pending state, locks the field, and warns that the text is not screened for secrets', () => {
    render(<ReviewCorrectionGuidanceEntry authorizing={true} statusLoading={false} onSubmit={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Authorizing…' })).toBeDisabled()
    expect(screen.getByLabelText(GUIDANCE_LABEL)).toBeDisabled()
    expect(screen.getByText(/not screened for secrets/i)).toBeInTheDocument()
  })

  it('never persists the draft in browser storage or the URL', () => {
    render(<ReviewCorrectionGuidanceEntry authorizing={false} statusLoading={false} onSubmit={vi.fn()} />)

    fireEvent.change(screen.getByLabelText(GUIDANCE_LABEL), { target: { value: 'DRAFT-SENTINEL' } })

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.href).not.toContain('DRAFT-SENTINEL')
    expect(document.cookie).not.toContain('DRAFT-SENTINEL')
  })
})

describe('ReviewCorrectionAction guidance entry', () => {
  it('offers the entry only where the escalation can be authorized, beside the unchanged plain button', () => {
    const onAuthorize = vi.fn()
    render(actionElement({ onAuthorize, onAuthorizeWithGuidance: vi.fn() }))

    fireEvent.click(screen.getByRole('button', { name: 'Authorize one additional correction' }))
    expect(onAuthorize).toHaveBeenCalledOnce()
    expect(screen.getByRole('form', { name: 'Authorize with guidance' })).toBeInTheDocument()
  })

  it.each([
    ['no escalation', exhaustedStatus('escalation-1', { escalationId: undefined })],
    ['an already available authorization', exhaustedStatus('escalation-1', { hasAvailableHumanAuthorization: true })],
    ['budget not exhausted', exhaustedStatus('escalation-1', { budgetExhausted: false })],
    ['another review', exhaustedStatus('escalation-1', { implementationReviewAttemptId: 'review-other' })],
    ['an active correction', exhaustedStatus('escalation-1', { status: 'Running' })],
  ])('does not offer the entry for %s', (_name, status) => {
    render(actionElement({ onAuthorizeWithGuidance: vi.fn() }, status))

    expect(screen.queryByRole('form', { name: 'Authorize with guidance' })).not.toBeInTheDocument()
  })

  it('withholds the entry while a global budget block or a time-fit block applies', () => {
    const blocked = render(
      actionElement({ onAuthorizeWithGuidance: vi.fn(), globalClaimBlock: { reason: 'CountBudgetExhausted' } }),
    )
    expect(screen.queryByRole('form', { name: 'Authorize with guidance' })).not.toBeInTheDocument()
    blocked.unmount()

    render(actionElement({ onAuthorizeWithGuidance: vi.fn(), timeFit: { reason: 'DoesNotFit' } }))
    expect(screen.queryByRole('form', { name: 'Authorize with guidance' })).not.toBeInTheDocument()
  })

  it('is hidden when no guided handler is wired', () => {
    render(actionElement())

    expect(screen.queryByRole('form', { name: 'Authorize with guidance' })).not.toBeInTheDocument()
  })

  it('resets the draft when the escalation changes and when the entry unmounts', () => {
    const props = { onAuthorizeWithGuidance: vi.fn() }
    const view = render(actionElement(props, exhaustedStatus('escalation-1')))
    fireEvent.change(screen.getByLabelText(GUIDANCE_LABEL), { target: { value: 'first draft' } })
    expect(screen.getByLabelText(GUIDANCE_LABEL)).toHaveValue('first draft')

    view.rerender(actionElement(props, exhaustedStatus('escalation-2')))
    expect(screen.getByLabelText(GUIDANCE_LABEL)).toHaveValue('')

    fireEvent.change(screen.getByLabelText(GUIDANCE_LABEL), { target: { value: 'second draft' } })
    view.rerender(actionElement({ ...props, statusLoading: true }, null))
    expect(screen.queryByRole('form', { name: 'Authorize with guidance' })).not.toBeInTheDocument()

    view.rerender(actionElement(props, exhaustedStatus('escalation-2')))
    expect(screen.getByLabelText(GUIDANCE_LABEL)).toHaveValue('')
  })

  it('shows a safe server refusal in the shared status line while both authorizations stay available', () => {
    render(
      actionElement({
        onAuthorizeWithGuidance: vi.fn(),
        authorizationError: 'An authorization with different guidance already exists for this escalation.',
      }),
    )

    expect(screen.getByRole('status')).toHaveTextContent('different guidance already exists')
    expect(screen.getByRole('button', { name: 'Authorize one additional correction' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Authorize with guidance' })).toBeEnabled()
  })
})
