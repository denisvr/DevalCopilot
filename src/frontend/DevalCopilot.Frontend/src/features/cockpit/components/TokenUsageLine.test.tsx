import { render, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import {
  AgentAttemptStatusResponse,
  AgentProcessExecutionResponse,
  AgentTokenUsageResponse,
  ChallengeResolutionAttemptStatusResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  CodeReviewAttemptStatusResponse,
  ImplementationAttemptStatusResponse,
  ReviewCorrectionAttemptStatusResponse,
  RunTokenUsageSummaryResponse,
} from '../../../api/generated/api-client'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { CodeReviewAction } from './CodeReviewAction'
import { CodexPlanningAction } from './CodexPlanningAction'
import { ImplementationAction } from './ImplementationAction'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'
import { RunTokenUsageSummary } from './RunTokenUsageSummary'
import { TokenUsageLine } from './TokenUsageLine'

const common = {
  statusLoading: false,
  statusError: null,
  requesting: false,
  requestError: null,
  onRequest: vi.fn(),
}

const dispatchedAtUtc = new Date('2026-09-24T10:00:00Z')
const processExecution = new AgentProcessExecutionResponse({
  outcome: 'Exited',
  exitCode: 1,
  durationMilliseconds: 2500,
  timeoutMilliseconds: 600_000,
})

const knownUsage = new AgentTokenUsageResponse({
  inputTokens: 1200,
  outputTokens: 345,
  cacheCreationInputTokens: 67,
  cacheReadInputTokens: 890,
})
const unknownUsage = new AgentTokenUsageResponse({})

type TokenUsage = AgentTokenUsageResponse | undefined

const base = { hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'ProviderInvocationFailed', dispatchedAtUtc, processExecution }

const roleActions: Array<[string, (tokenUsage: TokenUsage) => ReactElement]> = [
  ['Planner', (tokenUsage) => <CodexPlanningAction {...common} status={new AgentAttemptStatusResponse({ ...base, tokenUsage })} />],
  [
    'CriticalReviewer',
    (tokenUsage) => (
      <ClaudeCriticalReviewAction
        {...common}
        proposalMessageId="proposal-1"
        status={new ClaudeCriticalReviewAttemptStatusResponse({ ...base, reviewedProposalMessageId: 'proposal-1', tokenUsage })}
      />
    ),
  ],
  [
    'Resolver',
    (tokenUsage) => (
      <ChallengeResolutionAction
        {...common}
        challengedReviewAttemptId="review-1"
        reviewedProposalMessageId="proposal-1"
        status={new ChallengeResolutionAttemptStatusResponse({ ...base, originalProposalMessageId: 'proposal-1', tokenUsage })}
      />
    ),
  ],
  [
    'Implementer',
    (tokenUsage) => (
      <ImplementationAction
        {...common}
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({ ...base, planProposalMessageId: 'proposal-1', tokenUsage })}
      />
    ),
  ],
  [
    'CodeReviewer',
    (tokenUsage) => (
      <CodeReviewAction
        {...common}
        executionReportMessageId="report-1"
        status={new CodeReviewAttemptStatusResponse({ ...base, executionReportMessageId: 'report-1', tokenUsage })}
      />
    ),
  ],
  [
    'ReviewCorrection',
    (tokenUsage) => (
      <ReviewCorrectionAction
        {...common}
        reviewAttemptId="review-1"
        reviewOutcome="ReviewChangesRequested"
        status={new ReviewCorrectionAttemptStatusResponse({ ...base, implementationReviewAttemptId: 'review-1', tokenUsage })}
      />
    ),
  ],
]

function usageLine(): HTMLElement {
  const lines = document.querySelectorAll<HTMLElement>('.dc-token-usage')
  expect(lines).toHaveLength(1)
  return lines[0]
}

describe('token usage rendering for every agent role', () => {
  it.each(roleActions)('%s shows known usage next to, not instead of, its process evidence', (_, renderAction) => {
    render(renderAction(knownUsage))

    expect(usageLine()).toHaveTextContent('Tokens: 1,200 input · 345 output · 67 cache write · 890 cache read')
    expect(usageLine()).toHaveAttribute('data-token-usage', 'Known')
    expect(document.querySelectorAll('.dc-process-evidence')).toHaveLength(1)
    expect(screen.getByText(/Process exited with code 1/)).toBeInTheDocument()
  })

  it.each(roleActions)('%s states unknown usage truthfully when none was recorded', (_, renderAction) => {
    render(renderAction(unknownUsage))

    expect(usageLine()).toHaveTextContent('Token usage unknown')
    expect(usageLine()).toHaveAttribute('data-token-usage', 'Unknown')
  })

  it.each(roleActions)('%s treats a missing usage object as unknown', (_, renderAction) => {
    render(renderAction(undefined))

    expect(usageLine()).toHaveTextContent('Token usage unknown')
  })
})

describe('TokenUsageLine', () => {
  it('reports an undispatched attempt as not invoked and a running attempt as not yet recorded', () => {
    const { rerender } = render(<TokenUsageLine tokenUsage={unknownUsage} dispatchedAtUtc={undefined} status="Running" />)
    expect(usageLine()).toHaveTextContent('Token usage: none (provider not invoked)')

    rerender(<TokenUsageLine tokenUsage={unknownUsage} dispatchedAtUtc={dispatchedAtUtc} status="Running" />)
    expect(usageLine()).toHaveTextContent('Token usage not yet recorded')
  })

  it('renders no usage line when there is no attempt at all', () => {
    render(<CodexPlanningAction {...common} status={null} />)

    expect(document.querySelectorAll('.dc-token-usage')).toHaveLength(0)
  })
})

describe('RunTokenUsageSummary', () => {
  it('labels a complete summary as the run total', () => {
    render(
      <RunTokenUsageSummary
        summary={new RunTokenUsageSummaryResponse({
          completeness: 'Complete',
          attemptsWithKnownUsage: 2,
          attemptsWithUnknownUsage: 0,
          inputTokens: 2000,
          outputTokens: 400,
          cacheCreationInputTokens: 70,
          cacheReadInputTokens: 1000,
        })}
      />,
    )

    const summary = screen.getByLabelText('Run token usage')
    expect(summary).toHaveAttribute('data-completeness', 'Complete')
    expect(summary).toHaveTextContent(/^Run token total: 2,000 input/)
  })

  it('never renders a partial sum under a total label', () => {
    render(
      <RunTokenUsageSummary
        summary={new RunTokenUsageSummaryResponse({
          completeness: 'Partial',
          attemptsWithKnownUsage: 1,
          attemptsWithUnknownUsage: 1,
          inputTokens: 1200,
          outputTokens: 345,
        })}
      />,
    )

    const summary = screen.getByLabelText('Run token usage')
    expect(summary).toHaveAttribute('data-completeness', 'Partial')
    expect(summary).toHaveTextContent(/^Partial token count: 1,200 input · 345 output from 1 of 2 dispatched attempts/)
    expect(summary).toHaveTextContent('so this is not the run total')
    expect(summary.textContent).not.toMatch(/Run token total/)
  })

  it('shows a neutral empty state when no attempt has been dispatched', () => {
    render(
      <RunTokenUsageSummary
        summary={new RunTokenUsageSummaryResponse({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 })}
      />,
    )

    expect(screen.getByLabelText('Run token usage')).toHaveTextContent('No token usage data yet')
  })

  it('renders nothing without a summary', () => {
    render(<RunTokenUsageSummary summary={null} />)

    expect(screen.queryByLabelText('Run token usage')).not.toBeInTheDocument()
  })
})
