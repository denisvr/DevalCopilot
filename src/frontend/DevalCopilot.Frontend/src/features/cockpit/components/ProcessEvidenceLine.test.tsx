import { render, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import {
  AgentAttemptStatusResponse,
  AgentProcessExecutionResponse,
  ChallengeResolutionAttemptStatusResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  CodeReviewAttemptStatusResponse,
  ImplementationAttemptStatusResponse,
  ReviewCorrectionAttemptStatusResponse,
} from '../../../api/generated/api-client'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { CodeReviewAction } from './CodeReviewAction'
import { CodexPlanningAction } from './CodexPlanningAction'
import { ImplementationAction } from './ImplementationAction'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'

const common = {
  statusLoading: false,
  statusError: null,
  requesting: false,
  requestError: null,
  onRequest: vi.fn(),
}

const dispatchedAtUtc = new Date('2026-09-24T10:00:00Z')

function evidence(outcome: string | undefined, exitCode?: number) {
  return new AgentProcessExecutionResponse({
    outcome,
    exitCode,
    durationMilliseconds: outcome ? 2500 : undefined,
    timeoutMilliseconds: 600_000,
  })
}

type ProcessExecution = AgentProcessExecutionResponse | undefined

/** Each role's action rendered with a terminal, semantically failed attempt carrying the given
 * process evidence — the role's own semantic label and the evidence line must both appear. */
const roleActions: Array<[string, (processExecution: ProcessExecution) => ReactElement]> = [
  [
    'Planner',
    (processExecution) => (
      <CodexPlanningAction
        {...common}
        status={new AgentAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc, processExecution,
        })}
      />
    ),
  ],
  [
    'CriticalReviewer',
    (processExecution) => (
      <ClaudeCriticalReviewAction
        {...common}
        proposalMessageId="proposal-1"
        status={new ClaudeCriticalReviewAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc,
          reviewedProposalMessageId: 'proposal-1', processExecution,
        })}
      />
    ),
  ],
  [
    'Resolver',
    (processExecution) => (
      <ChallengeResolutionAction
        {...common}
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={new ChallengeResolutionAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc,
          originalProposalMessageId: 'proposal-1', processExecution,
        })}
      />
    ),
  ],
  [
    'Implementer',
    (processExecution) => (
      <ImplementationAction
        {...common}
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc,
          planProposalMessageId: 'proposal-1', processExecution,
        })}
      />
    ),
  ],
  [
    'CodeReviewer',
    (processExecution) => (
      <CodeReviewAction
        {...common}
        executionReportMessageId="report-1"
        status={new CodeReviewAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc,
          executionReportMessageId: 'report-1', processExecution,
        })}
      />
    ),
  ],
  [
    'ReviewCorrection',
    (processExecution) => (
      <ReviewCorrectionAction
        {...common}
        reviewAttemptId="review-1"
        reviewOutcome="ReviewChangesRequested"
        status={new ReviewCorrectionAttemptStatusResponse({
          hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc,
          implementationReviewAttemptId: 'review-1', processExecution,
        })}
      />
    ),
  ],
]

function evidenceLine(): HTMLElement {
  const lines = document.querySelectorAll<HTMLElement>('.dc-process-evidence')
  expect(lines).toHaveLength(1)
  return lines[0]
}

describe('process evidence rendering for every agent role', () => {
  it.each(roleActions)('%s shows a timeout as process evidence beside its semantic outcome', (_, renderAction) => {
    render(renderAction(evidence('TimedOut')))

    expect(evidenceLine()).toHaveTextContent('Process timed out after 2.5 s · timeout 10m 00s')
    expect(evidenceLine()).toHaveAttribute('data-process-outcome', 'TimedOut')
    expect(screen.getByText(/Last (attempt|correction) #1:/)).not.toHaveTextContent('timed out')
  })

  it.each(roleActions)('%s shows a non-zero exit code exactly', (_, renderAction) => {
    render(renderAction(evidence('Exited', 2)))

    expect(evidenceLine()).toHaveTextContent('Process exited with code 2 after 2.5 s · timeout 10m 00s')
  })

  it.each(roleActions)('%s shows a cancellation without an exit code', (_, renderAction) => {
    render(renderAction(evidence('Cancelled')))

    expect(evidenceLine()).toHaveTextContent('Process cancelled after 2.5 s · timeout 10m 00s')
  })

  it.each(roleActions)('%s states unknown evidence truthfully when none was recorded', (_, renderAction) => {
    render(renderAction(evidence(undefined)))

    expect(evidenceLine()).toHaveTextContent('Process evidence unknown · timeout 10m 00s')
    expect(evidenceLine()).toHaveAttribute('data-process-outcome', 'Unknown')
  })
})

describe('ProcessEvidenceLine', () => {
  it('distinguishes a clean exit from the semantic success it accompanies', () => {
    render(
      <ImplementationAction
        {...common}
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({
          hasAttempt: true,
          attemptNumber: 1,
          status: 'Completed',
          outcome: 'Implemented',
          dispatchedAtUtc,
          planProposalMessageId: 'proposal-1',
          processExecution: evidence('Exited', 0),
        })}
      />,
    )

    expect(screen.getByText('Last attempt #1: Implemented.')).toBeInTheDocument()
    expect(evidenceLine()).toHaveTextContent('Process exited with code 0 after 2.5 s · timeout 10m 00s')
  })

  it('reports an undispatched attempt as not started and a dispatched running attempt as not yet recorded', () => {
    const { rerender } = render(
      <ProcessEvidenceLine processExecution={evidence(undefined)} dispatchedAtUtc={undefined} status="Running" />,
    )
    expect(evidenceLine()).toHaveTextContent('Process not started · timeout 10m 00s')

    rerender(<ProcessEvidenceLine processExecution={evidence(undefined)} dispatchedAtUtc={dispatchedAtUtc} status="Running" />)
    expect(evidenceLine()).toHaveTextContent('Process result not yet recorded · timeout 10m 00s')
  })

  it('renders no evidence line when there is no attempt at all', () => {
    render(<CodexPlanningAction {...common} status={null} />)

    expect(document.querySelectorAll('.dc-process-evidence')).toHaveLength(0)
  })

  // Regression: a well-formed-looking process object (exactly what a tampered or prematurely
  // populated persisted row would look like) must never be shown as concluded evidence, in either
  // the rendered text or the `data-process-outcome` test hook, while the attempt is still running or
  // was never dispatched — the two must always agree with each other.
  it('never trusts a well-formed-looking process object for a still-running attempt, in either the text or the outcome attribute', () => {
    render(<ProcessEvidenceLine processExecution={evidence('Exited', 0)} dispatchedAtUtc={dispatchedAtUtc} status="Running" />)

    expect(evidenceLine()).toHaveTextContent('Process result not yet recorded · timeout 10m 00s')
    expect(evidenceLine()).toHaveAttribute('data-process-outcome', 'Unknown')
  })

  it('never trusts a well-formed-looking process object for an undispatched attempt, in either the text or the outcome attribute', () => {
    render(<ProcessEvidenceLine processExecution={evidence('TimedOut')} dispatchedAtUtc={undefined} status="Failed" />)

    expect(evidenceLine()).toHaveTextContent('Process not started · timeout 10m 00s')
    expect(evidenceLine()).toHaveAttribute('data-process-outcome', 'Unknown')
  })

  it('still trusts genuine evidence, including a nonzero exit code, for a dispatched terminal attempt', () => {
    render(<ProcessEvidenceLine processExecution={evidence('Exited', 137)} dispatchedAtUtc={dispatchedAtUtc} status="Failed" />)

    expect(evidenceLine()).toHaveTextContent('Process exited with code 137 after 2.5 s · timeout 10m 00s')
    expect(evidenceLine()).toHaveAttribute('data-process-outcome', 'Exited')
  })
})
