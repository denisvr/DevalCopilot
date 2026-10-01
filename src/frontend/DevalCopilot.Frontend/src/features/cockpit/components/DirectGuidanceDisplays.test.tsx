// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  DirectHumanGuidanceResponse,
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

const fact = (state: string, text?: string) => new DirectHumanGuidanceResponse({ state, text })

const PROVIDED_LABEL = /Direct human guidance supplied to this attempt \(host-supplied input; whether the provider followed it is not observed\)/

const NOT_RECORDED =
  'Direct human guidance for this attempt: none recorded.'
const UNKNOWN = 'Direct human guidance for this attempt: unknown (the recorded facts disagree); no text is shown.'
const MARKUP = '<img src=x onerror=alert(1)> **bold** <b>tag</b>\nsecond line'

/** Asserts the rendered statement for one fact, in whichever surface rendered it. */
function expectFact(container: HTMLElement, input: DirectHumanGuidanceResponse | undefined) {
  if (!input) {
    expect(screen.queryByText(/Direct human guidance/)).toBeNull()
    return
  }
  switch (input.state) {
    case 'Provided': {
      expect(screen.getByText(PROVIDED_LABEL)).toBeTruthy()
      const text = container.querySelector('.dc-direct-guidance-text')
      expect(text?.textContent).toBe(input.text)
      return
    }
    case 'NotRecorded':
      expect(screen.getByText(NOT_RECORDED)).toBeTruthy()
      return
    default:
      expect(screen.getByText(UNKNOWN)).toBeTruthy()
  }
}

const CASES: [string, DirectHumanGuidanceResponse][] = [
  ['Provided', fact('Provided', 'Keep the change small.\nPrefer tests first.')],
  ['NotRecorded', fact('NotRecorded')],
  ['Unknown', fact('Unknown', 'TEXT-THAT-MUST-NOT-APPEAR')],
]

describe('latest attempt direct-guidance fact', () => {
  const attempt = (directGuidance?: DirectHumanGuidanceResponse) =>
    new RunCockpitAgentAttemptResponse({
      attemptId: 'a-1',
      attemptNumber: 1,
      role: 'Implementer',
      provider: 'ClaudeCode',
      status: 'Completed',
      directGuidance,
    })

  it.each(CASES)('renders %s', (_name, input) => {
    const { container } = render(<LatestAgentAttemptEvidence attempt={attempt(input)} />)
    expectFact(container, input)
    expect(screen.queryByText(/TEXT-THAT-MUST-NOT-APPEAR/)).toBeNull()
  })

  it('shows nothing for an attempt outside the mutation paths', () => {
    const { container } = render(<LatestAgentAttemptEvidence attempt={attempt(undefined)} />)
    expectFact(container, undefined)
  })
})

describe('implementation role status', () => {
  const renderImplementation = (status: ImplementationAttemptStatusResponse) =>
    render(
      <ImplementationAction
        runId="run-1"
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

  it.each(CASES)('renders %s', (_name, input) => {
    const { container } = renderImplementation(status({ directGuidance: input }))
    expectFact(container, input)
    expect(screen.queryByText(/TEXT-THAT-MUST-NOT-APPEAR/)).toBeNull()
  })

  it('shows nothing when absent, or when there is no attempt', () => {
    const absent = renderImplementation(status())
    expectFact(absent.container, undefined)
    absent.unmount()

    const noAttempt = renderImplementation(status({ hasAttempt: false, directGuidance: fact('Provided', 'stale') }))
    expectFact(noAttempt.container, undefined)
  })

  it('shows the text literally, never as markup, with line breaks preserved by styling', () => {
    const { container } = renderImplementation(status({ directGuidance: fact('Provided', MARKUP) }))

    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('b')).toBeNull()
    const text = container.querySelector('.dc-direct-guidance-text')
    expect(text?.textContent).toBe(MARKUP)
    expect(text?.innerHTML).not.toContain('<img')
  })
})

describe('review correction role status', () => {
  const renderCorrection = (status: ReviewCorrectionAttemptStatusResponse) =>
    render(
      <ReviewCorrectionAction
        runId="run-1"
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

  it.each(CASES)('renders %s', (_name, input) => {
    const { container } = renderCorrection(status({ directGuidance: input }))
    expectFact(container, input)
    expect(screen.queryByText(/TEXT-THAT-MUST-NOT-APPEAR/)).toBeNull()
  })

  it('shows nothing when absent', () => {
    const { container } = renderCorrection(status())
    expectFact(container, undefined)
  })

  it('shows the text literally, never as markup', () => {
    const { container } = renderCorrection(status({ directGuidance: fact('Provided', MARKUP) }))

    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('.dc-direct-guidance-text')?.textContent).toBe(MARKUP)
  })

  it('labels the fact separately from the additional-correction authorization guidance', () => {
    renderCorrection(status({ directGuidance: fact('NotRecorded') }))

    expect(screen.queryByText(/authoriz/i)).toBeNull()
  })
})

describe('historical attempt evidence direct-guidance fact', () => {
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

  async function openEvidence(directGuidance: unknown) {
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
        directGuidance,
      }),
    } as never)
    const view = render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    await screen.findByLabelText('Selected Agent attempt evidence')
    return view
  }

  it.each(CASES)('renders %s', async (_name, input) => {
    const { container } = await openEvidence(input)
    await waitFor(() => expectFact(container, input))
    expect(screen.queryByText(/TEXT-THAT-MUST-NOT-APPEAR/)).toBeNull()
  })

  it('shows nothing when the evidence carries no fact', async () => {
    await openEvidence(undefined)
    await screen.findByText(/No sealed artifacts were recorded/)
    expect(screen.queryByText(/Direct human guidance/)).toBeNull()
  })

  it('shows markup literally', async () => {
    const { container } = await openEvidence(fact('Provided', MARKUP))
    await waitFor(() => expect(container.querySelector('.dc-direct-guidance-text')?.textContent).toBe(MARKUP))
    expect(container.querySelector('img')).toBeNull()
  })
})
