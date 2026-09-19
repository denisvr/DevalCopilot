import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CodeReviewAttemptStatusResponse } from '../../../api/generated/api-client'
import { CodeReviewAction } from './CodeReviewAction'

describe('CodeReviewAction', () => {
  it('renders nothing when no real ExecutionReport exists yet for this run', () => {
    const { container } = render(
      <CodeReviewAction
        executionReportMessageId={null}
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('offers the request action once a real ExecutionReport exists and no attempt has been made yet', () => {
    const onRequest = vi.fn()
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={onRequest}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Request code review' }))
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('shows a visibly-working pending state before dispatch, with no action button', () => {
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={
          new CodeReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Running',
            executionReportMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/pending/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Request code review' })).not.toBeInTheDocument()
  })

  it.each([
    ['ReviewApproved', 'Implementation approved'],
    ['ReviewChangesRequested', 'Changes requested'],
    ['InputAlreadyCodeReviewed', 'This implementation already has a code review'],
  ])('withholds the request action once the current ExecutionReport has already reached a durable %s outcome', (outcome, label) => {
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={
          new CodeReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome,
            executionReportMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(`Last attempt #1: ${label}.`)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request code review' })).not.toBeInTheDocument()
  })

  it('allows requesting a fresh review once a newer implementation supersedes an already-reviewed one', () => {
    render(
      <CodeReviewAction
        executionReportMessageId="message-2"
        status={
          new CodeReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome: 'ReviewApproved',
            executionReportMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Request code review' })).toBeInTheDocument()
  })

  it('disables the request button while a request is in flight', () => {
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={true}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()
  })

  it('surfaces a safe request-failure message without discarding the last known status, and never leaks a raw reason code', () => {
    // `requestError` is whatever useRequestCodeReview's own extractSafeErrorDetail already
    // resolved it to — the backend's bounded, human-readable ApiError.detail sentence, never the
    // raw dot-separated reason code (e.g. "agent_attempts.verification_evidence_not_passed")
    // that code lives behind. This component trusts that extraction and only renders the string
    // it is given, so this test proves the safe copy renders and the raw code is never present.
    const SafeHumanDetail = 'One of the enabled verification commands has not passed for the current checkpoint.'
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={
          new CodeReviewAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Failed',
            outcome: 'ProviderInvocationFailed',
            executionReportMessageId: 'message-1',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={SafeHumanDetail}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent(SafeHumanDetail)
    expect(screen.getByText(/Last attempt #1/)).toBeInTheDocument()
    expect(screen.queryByText(/agent_attempts\./)).not.toBeInTheDocument()
  })

  it('surfaces a safe status-read failure', () => {
    render(
      <CodeReviewAction
        executionReportMessageId="message-1"
        status={null}
        statusLoading={false}
        statusError="Code review attempt status is unavailable."
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Code review attempt status is unavailable.')
  })
})
