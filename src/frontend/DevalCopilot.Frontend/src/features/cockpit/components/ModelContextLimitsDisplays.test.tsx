// @vitest-environment jsdom
import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentModelContextLimitResponse, AgentModelContextLimitsResponse } from '../../../api/generated/api-client'
import { agentAttemptEvidenceClient, agentAttemptHistoryClient } from '../../../api/clients'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'
import { ModelContextLimitsFact } from './ModelContextLimitsFact'

vi.mock('../../../api/clients', () => ({
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
}))

const limits = (...models: [string, number, number][]) =>
  new AgentModelContextLimitsResponse({
    models: models.map(([modelId, contextWindowTokens, maxOutputTokens]) =>
      new AgentModelContextLimitResponse({ modelId, contextWindowTokens, maxOutputTokens }),
    ),
  })

const EXPLANATION = /Remaining context and the capacity of the next invocation were not measured\./
const LISTED = /Models listed by Claude in its result for this attempt \(not independently proven to have been used\)/

function rowsOf(region: HTMLElement) {
  return within(region)
    .getAllByRole('row')
    .slice(1)
    .map((row) => Array.from(row.children).map((cell) => cell.textContent))
}

describe('ModelContextLimitsFact', () => {
  it('shows one listed model with its context-window and maximum-output tokens and the fixed explanation', () => {
    render(<ModelContextLimitsFact provider="ClaudeCode" limits={limits(['claude-journey-a1', 200000, 32000])} />)

    const region = screen.getByRole('region', { name: 'Claude-reported model limits' })
    expect(within(region).getByRole('heading', { name: 'Claude-reported model limits' })).toBeTruthy()
    expect(within(region).getByText(LISTED)).toBeTruthy()
    expect(within(region).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Model identifier',
      'Context-window tokens',
      'Maximum output tokens',
    ])
    expect(rowsOf(region)).toEqual([['claude-journey-a1', '200,000', '32,000']])
    expect(within(region).getByText(EXPLANATION)).toBeTruthy()
  })

  it('shows every listed model in the order received, each with its own limits', () => {
    render(
      <ModelContextLimitsFact
        provider="ClaudeCode"
        limits={limits(['Claude-Z', 200000, 200000], ['claude-a', 200000, 32000], ['claude-b', 1000000, 64000])}
      />,
    )

    expect(rowsOf(screen.getByRole('region', { name: 'Claude-reported model limits' }))).toEqual([
      ['Claude-Z', '200,000', '200,000'],
      ['claude-a', '200,000', '32,000'],
      ['claude-b', '1,000,000', '64,000'],
    ])
  })

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['an empty member', new AgentModelContextLimitsResponse({ models: [] })],
    ['no models', new AgentModelContextLimitsResponse()],
  ])('says "Not recorded" for %s and never shows a zero or an invented limit', (_name, value) => {
    const { container } = render(<ModelContextLimitsFact provider="ClaudeCode" limits={value} />)

    const region = screen.getByRole('region', { name: 'Claude-reported model limits' })
    expect(within(region).getByText('Not recorded')).toBeTruthy()
    expect(within(region).getByText(EXPLANATION)).toBeTruthy()
    expect(within(region).queryByRole('table')).toBeNull()
    expect(container.textContent).not.toMatch(/\b0\b/)
    expect(container.textContent).not.toMatch(/200,000|32,000/)
  })

  it('shows "Not recorded" for a field the response does not carry rather than zero', () => {
    render(
      <ModelContextLimitsFact
        provider="ClaudeCode"
        limits={new AgentModelContextLimitsResponse({ models: [new AgentModelContextLimitResponse({ modelId: 'claude-partial' })] })}
      />,
    )

    expect(rowsOf(screen.getByRole('region', { name: 'Claude-reported model limits' }))).toEqual([
      ['claude-partial', 'Not recorded', 'Not recorded'],
    ])
  })

  it('is a Claude-only fact: another provider and an unknown provider show nothing', () => {
    const { container, rerender } = render(<ModelContextLimitsFact provider="Codex" limits={limits(['claude-a', 1, 1])} />)
    expect(container.textContent).toBe('')
    rerender(<ModelContextLimitsFact limits={limits(['claude-a', 1, 1])} />)
    expect(container.textContent).toBe('')
  })

  it('renders an identifier as text, never as markup', () => {
    const hostile = '<img src=x onerror=alert(1)>'
    const { container } = render(<ModelContextLimitsFact provider="ClaudeCode" limits={limits([hostile, 10, 5])} />)

    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByText(hostile)).toBeTruthy()
  })

  it('derives nothing: no percentage, meter, progress, remaining-capacity claim or action', () => {
    const { container } = render(<ModelContextLimitsFact provider="ClaudeCode" limits={limits(['claude-a', 200000, 32000])} />)

    expect(container.textContent).not.toMatch(/%|percent|full|remaining capacity|ready|eligible/i)
    expect(container.querySelector('progress, meter, [role="progressbar"], [role="meter"], button, a, input')).toBeNull()
  })
})

describe('the selected historical attempt detail', () => {
  const history = vi.fn()
  const evidence = vi.fn()

  const entry = (attemptNumber: number) => ({
    attemptId: `attempt-${attemptNumber}`,
    attemptNumber,
    status: 'Completed',
    claimedAtUtc: new Date('2026-10-04T10:00:00Z'),
    identityValid: true,
    role: 'Implementer',
    provider: 'ClaudeCode',
  })

  const evidenceFor = (attemptId: string, provider: string, modelContextLimits?: AgentModelContextLimitsResponse) => ({
    identityValid: true,
    attemptId,
    attemptNumber: Number(attemptId.split('-')[1]),
    attemptStatus: 'Completed',
    claimedAtUtc: new Date('2026-10-04T10:00:00Z'),
    role: 'Implementer',
    provider,
    artifacts: [],
    modelContextLimits,
  })

  beforeEach(() => {
    history.mockReset()
    evidence.mockReset()
    vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: history } as never)
    vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidence } as never)
  })

  async function inspect(attemptNumber: number) {
    fireEvent.click(await screen.findByRole('button', { name: `Inspect attempt ${attemptNumber}` }))
    return screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
  }

  it('shows the recorded models of a selected Claude attempt inside its own detail', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor('attempt-1', 'ClaudeCode', limits(['claude-a', 200000, 32000], ['claude-b', 1000000, 64000])))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))

    const detail = await inspect(1)
    const region = await within(detail).findByRole('region', { name: 'Claude-reported model limits' })

    expect(rowsOf(region)).toEqual([
      ['claude-a', '200,000', '32,000'],
      ['claude-b', '1,000,000', '64,000'],
    ])
    expect(within(region).getByText(EXPLANATION)).toBeTruthy()
    expect(evidence).toHaveBeenCalledWith('run-1', 'attempt-1')
  })

  it('says "Not recorded" for a Claude attempt whose evidence carries none', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor('attempt-1', 'ClaudeCode', undefined))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))

    const detail = await inspect(1)
    const region = await within(detail).findByRole('region', { name: 'Claude-reported model limits' })

    expect(within(region).getByText('Not recorded')).toBeTruthy()
    expect(within(region).queryByRole('table')).toBeNull()
  })

  it('shows no model-limits section for a Codex attempt', async () => {
    history.mockResolvedValue({ items: [{ ...entry(1), provider: 'Codex' }], hasMore: false })
    evidence.mockResolvedValue(evidenceFor('attempt-1', 'Codex', undefined))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))

    const detail = await inspect(1)
    await within(detail).findByText(/No sealed artifacts were recorded/)

    expect(screen.queryByText('Claude-reported model limits')).toBeNull()
  })

  it('replaces the section with the newly selected attempt and never leaves the previous one visible', async () => {
    history.mockResolvedValue({ items: [entry(2), entry(1)], hasMore: false })
    evidence.mockImplementation((_runId: string, attemptId: string) =>
      Promise.resolve(
        attemptId === 'attempt-1'
          ? evidenceFor('attempt-1', 'ClaudeCode', limits(['claude-first', 111, 11]))
          : evidenceFor('attempt-2', 'ClaudeCode', limits(['claude-second', 222, 22])),
      ),
    )
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))

    const first = await inspect(1)
    await within(first).findByText('claude-first')
    const second = await inspect(2)
    await within(second).findByText('claude-second')

    expect(screen.queryByText('claude-first')).toBeNull()
    expect(screen.getAllByRole('region', { name: 'Claude-reported model limits' })).toHaveLength(1)
  })

  it('discards the section with the selection when the run changes', async () => {
    history
      .mockResolvedValueOnce({ items: [entry(1)], hasMore: false })
      .mockResolvedValueOnce({ items: [entry(7)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor('attempt-1', 'ClaudeCode', limits(['claude-run-one', 123, 12])))
    const { rerender } = render(<AgentAttemptHistoryPanel key="run-1" runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    const detail = await inspect(1)
    await within(detail).findByText('claude-run-one')

    rerender(<AgentAttemptHistoryPanel key="run-2" runId="run-2" />)

    expect(screen.queryByText('claude-run-one')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Claude-reported model limits' })).toBeNull()
  })

  it('shows nothing from an attempt whose identity could not be verified', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue({
      identityValid: false,
      attemptId: 'attempt-1',
      attemptNumber: 1,
      artifacts: [],
      modelContextLimits: limits(['claude-hidden', 1, 1]),
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))

    await inspect(1)
    await screen.findByText(/recorded identity could not be verified, so its evidence is not shown/)

    expect(screen.queryByText('claude-hidden')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Claude-reported model limits' })).toBeNull()
  })
})
