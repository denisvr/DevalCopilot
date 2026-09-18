import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AgentAttemptStatusResponse } from '../../../api/generated/api-client'
import { CodexPlanningAction } from './CodexPlanningAction'

describe('CodexPlanningAction', () => {
  it('offers the request action when no attempt exists yet', () => {
    const onRequest = vi.fn()
    render(
      <CodexPlanningAction
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={onRequest}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Request Codex plan' }))
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('shows a visibly-working pending state before dispatch, with no action button', () => {
    render(
      <CodexPlanningAction
        status={new AgentAttemptStatusResponse({ attemptId: 'attempt-1', attemptNumber: 1, status: 'Running' })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/pending/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
  })

  it('shows a visibly-working running state once the attempt has been dispatched', () => {
    render(
      <CodexPlanningAction
        status={
          new AgentAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Running',
            dispatchedAtUtc: new Date('2026-09-17T00:00:00Z'),
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/running/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
  })

  it('never implies Claude has reviewed a completed Proposal', () => {
    render(
      <CodexPlanningAction
        status={
          new AgentAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome: 'Proposed',
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/Last attempt #1: Plan proposed\./)).toBeInTheDocument()
    expect(screen.queryByText(/claude/i)).not.toBeInTheDocument()
    // A terminal attempt allows requesting a new one.
    expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
  })

  it.each([
    ['SourceChanged', 'Source changed before the plan completed'],
    ['InvalidStructuredOutput', 'Codex returned an invalid structured response'],
    ['ProviderInvocationFailed', 'Codex could not be invoked'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for dispatch'],
  ])('labels the %s terminal outcome safely', (outcome, label) => {
    render(
      <CodexPlanningAction
        status={new AgentAttemptStatusResponse({ attemptId: 'attempt-1', attemptNumber: 2, status: 'Failed', outcome })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(`Last attempt #2: ${label}.`)).toBeInTheDocument()
    // Never fall through to the raw enum identifier as user-facing copy.
    expect(screen.queryByText(new RegExp(outcome))).not.toBeInTheDocument()
  })

  it('disables the request button while a request is in flight', () => {
    render(
      <CodexPlanningAction
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

  it('surfaces a safe request-failure message without discarding the last known status', () => {
    render(
      <CodexPlanningAction
        status={new AgentAttemptStatusResponse({ attemptId: 'attempt-1', attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed' })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError="This run already has a Codex planning attempt in progress."
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('This run already has a Codex planning attempt in progress.')
    expect(screen.getByText(/Last attempt #1/)).toBeInTheDocument()
  })

  it('surfaces a safe status-read failure', () => {
    render(
      <CodexPlanningAction
        status={null}
        statusLoading={false}
        statusError="Codex planning attempt status is unavailable."
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Codex planning attempt status is unavailable.')
  })
})
