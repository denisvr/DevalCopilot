// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ClaudeMutationTurnLimitResponse,
  ImplementationAttemptStatusResponse,
  ReviewCorrectionAttemptStatusResponse,
  RunCockpitAgentAttemptResponse,
} from '../../../api/generated/api-client'
import { agentAttemptEvidenceClient, agentAttemptHistoryClient } from '../../../api/clients'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'
import { ImplementationAction } from './ImplementationAction'
import { LatestAgentAttemptEvidence } from './LatestAgentAttemptEvidence'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'

vi.mock('../../../api/clients', () => ({
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
}))

const fact = (state: string, maxTurns?: number) => new ClaudeMutationTurnLimitResponse({ state, maxTurns })

const ATTEMPT_CASES: [ClaudeMutationTurnLimitResponse, string][] = [
  [fact('NotRecorded'), 'Claude turn limit for this attempt: Not recorded'],
  [fact('NotRequested'), 'Claude turn limit for this attempt: Not requested'],
  [fact('Requested', 12), 'Claude turn limit for this attempt: Requested: 12 turns (a request only; the turns used are not measured)'],
  [fact('Unknown'), 'Claude turn limit for this attempt: Unknown'],
]

describe('latest attempt turn-limit fact', () => {
  const attempt = (maxTurns?: ClaudeMutationTurnLimitResponse) =>
    new RunCockpitAgentAttemptResponse({
      attemptId: 'a-1',
      attemptNumber: 1,
      role: 'Implementer',
      provider: 'ClaudeCode',
      status: 'Completed',
      maxTurns,
    })

  it.each(ATTEMPT_CASES)('renders %j', (input, text) => {
    render(<LatestAgentAttemptEvidence attempt={attempt(input)} />)
    expect(screen.getByText(text)).toBeTruthy()
  })

  it('shows nothing for an attempt that is not a Claude mutation attempt', () => {
    render(<LatestAgentAttemptEvidence attempt={attempt(undefined)} />)
    expect(screen.queryByText(/Claude turn limit/)).toBeNull()
  })
})

describe('implementation role status', () => {
  const renderImplementation = (status: ImplementationAttemptStatusResponse) =>
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={status}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
      />,
    )

  const status = (overrides: Partial<ImplementationAttemptStatusResponse> = {}) =>
    new ImplementationAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'a-1',
      attemptNumber: 1,
      status: 'Completed',
      outcome: 'NoChangesProduced',
      planProposalMessageId: 'proposal-1',
      provider: 'ClaudeCode',
      role: 'Implementer',
      ...overrides,
    })

  it.each(ATTEMPT_CASES)('renders the attempt fact %j', (input, text) => {
    renderImplementation(status({ attemptTurnLimit: input }))
    expect(screen.getByText(text)).toBeTruthy()
  })

  it('labels the current run request separately as applying to future attempts', () => {
    renderImplementation(status({ attemptTurnLimit: fact('NotRequested'), runTurnLimitRequest: fact('Requested', 30) }))

    expect(screen.getByText('Claude turn limit for this attempt: Not requested')).toBeTruthy()
    expect(screen.getByText('Current run request (applies to future attempts): 30 turns')).toBeTruthy()
  })

  it('shows no attempt fact when there is no attempt but still shows the run request', () => {
    renderImplementation(status({ hasAttempt: false, attemptTurnLimit: undefined, runTurnLimitRequest: fact('NotRequested') }))

    expect(screen.queryByText(/for this attempt/)).toBeNull()
    expect(screen.getByText('Current run request (applies to future attempts): Not requested')).toBeTruthy()
  })

  it.each(['claude-implementation-v1', 'claude-implementation-v2'])('recognizes the exact adapter contract %s', (contract) => {
    renderImplementation(status({ adapterContractVersion: contract }))
    expect(screen.getByText(new RegExp(`Adapter contract: ${contract} ·`))).toBeTruthy()
  })

  it.each(['claude-implementation-v3', 'claude-implementation-v2-extra', 'claude-implementation-', 'xclaude-implementation-v1', undefined])(
    'treats %s as Unknown, never by prefix',
    (contract) => {
      renderImplementation(status({ adapterContractVersion: contract }))
      expect(screen.getByText(/Adapter contract: Unknown ·/)).toBeTruthy()
    },
  )
})

describe('review correction role status', () => {
  const renderCorrection = (status: ReviewCorrectionAttemptStatusResponse) =>
    render(
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
      />,
    )

  const status = (overrides: Partial<ReviewCorrectionAttemptStatusResponse> = {}) =>
    new ReviewCorrectionAttemptStatusResponse({
      hasAttempt: true,
      attemptId: 'c-1',
      attemptNumber: 1,
      implementationReviewAttemptId: 'review-1',
      status: 'Completed',
      outcome: 'CorrectionApplied',
      ...overrides,
    })

  it.each(ATTEMPT_CASES)('renders the attempt fact %j', (input, text) => {
    renderCorrection(status({ attemptTurnLimit: input }))
    expect(screen.getByText(text)).toBeTruthy()
  })

  it('shows the current run request separately', () => {
    renderCorrection(status({ attemptTurnLimit: fact('Requested', 5), runTurnLimitRequest: fact('Unknown') }))

    expect(screen.getByText(/Requested: 5 turns/)).toBeTruthy()
    expect(screen.getByText('Current run request (applies to future attempts): Unknown')).toBeTruthy()
  })

  it('shows nothing about turn limits when the status carries none', () => {
    renderCorrection(status())
    expect(screen.queryByText(/turn limit|run request/i)).toBeNull()
  })
})

describe('historical attempt evidence turn-limit fact', () => {
  beforeEach(() => {
    vi.mocked(agentAttemptHistoryClient).mockReturnValue({
      getAgentAttemptHistory: vi.fn().mockResolvedValue({
        items: [
          {
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            claimedAtUtc: new Date('2026-09-29T10:00:00Z'),
            identityValid: true,
            role: 'Implementer',
            provider: 'ClaudeCode',
          },
        ],
        hasMore: false,
      }),
    } as never)
  })

  async function openEvidence(maxTurns: unknown) {
    vi.mocked(agentAttemptEvidenceClient).mockReturnValue({
      getAgentAttemptEvidence: vi.fn().mockResolvedValue({
        identityValid: true,
        attemptId: 'attempt-1',
        attemptNumber: 1,
        attemptStatus: 'Completed',
        claimedAtUtc: new Date('2026-09-29T10:00:00Z'),
        role: 'Implementer',
        provider: 'ClaudeCode',
        artifacts: [],
        maxTurns,
      }),
    } as never)
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    await screen.findByLabelText('Selected Agent attempt evidence')
  }

  it.each(ATTEMPT_CASES)('renders %j', async (input, text) => {
    await openEvidence(input)
    await waitFor(() => expect(screen.getByText(text)).toBeTruthy())
  })

  it('shows nothing when the evidence carries no fact', async () => {
    await openEvidence(undefined)
    await screen.findByText(/No sealed artifacts were recorded/)
    expect(screen.queryByText(/Claude turn limit/)).toBeNull()
  })
})
