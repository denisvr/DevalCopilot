// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RunCockpitTokenWarningResponse, SetTokenWarningThresholdResponse } from '../../../api/generated/api-client'
import { setTokenWarningThresholdClient } from '../../../api/clients'
import { TokenWarningPanel } from './TokenWarningPanel'

vi.mock('../../../api/clients', () => ({
  setTokenWarningThresholdClient: vi.fn(),
}))

function warning(overrides: Partial<ConstructorParameters<typeof RunCockpitTokenWarningResponse>[0]> & { provider: string }) {
  return new RunCockpitTokenWarningResponse({
    state: 'NotConfigured',
    knownTokenCount: 0,
    countedAttempts: 0,
    pendingAttempts: 0,
    insufficientEvidenceAttempts: 0,
    unattributedAttempts: 0,
    ...overrides,
  })
}

const NEUTRAL = [warning({ provider: 'Codex' }), warning({ provider: 'ClaudeCode' })]

function codexSection() {
  return screen.getByLabelText('Codex token-activity warning')
}

function claudeSection() {
  return screen.getByLabelText('Claude Code token-activity warning')
}

describe('TokenWarningPanel', () => {
  it('is neutral with no thresholds: no alert and no all-clear, and states it is advisory only', () => {
    render(<TokenWarningPanel runId="run-1" tokenWarnings={NEUTRAL} />)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(codexSection()).getByText('Codex: no token-activity warning threshold set.')).toBeTruthy()
    expect(within(claudeSection()).getByText('Claude Code: no token-activity warning threshold set.')).toBeTruthy()
    expect(screen.getByText(/do not limit or block any attempt/)).toBeTruthy()
    expect(screen.queryByText(/all-clear\./)).toBeNull()
  })

  it('shows a prominent alert only for the provider whose threshold is reached', () => {
    render(
      <TokenWarningPanel
        runId="run-1"
        tokenWarnings={[
          warning({ provider: 'Codex', state: 'ThresholdReached', thresholdTokens: 1200, knownTokenCount: 1200, countedAttempts: 1 }),
          warning({ provider: 'ClaudeCode', state: 'BelowThresholdComplete', thresholdTokens: 10_000, knownTokenCount: 615, countedAttempts: 1 }),
        ]}
      />,
    )

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Codex token-activity warning: 1,200 reported tokens recorded across 1 concluded attempt(s), at or above the 1,200 threshold.')
    expect(within(claudeSection()).queryByRole('alert')).toBeNull()
    expect(within(claudeSection()).getByText(/615 reported tokens across 1 concluded attempt\(s\); below the 10,000 threshold, with complete evidence/)).toBeTruthy()
  })

  it('marks a reached threshold with evidence gaps as a lower bound', () => {
    render(
      <TokenWarningPanel
        runId="run-1"
        tokenWarnings={[
          warning({ provider: 'Codex', state: 'ThresholdReached', thresholdTokens: 15, knownTokenCount: 15, countedAttempts: 1, insufficientEvidenceAttempts: 1, pendingAttempts: 1 }),
          warning({ provider: 'ClaudeCode' }),
        ]}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('This is a lower bound; evidence gaps: 1 still running, 1 without usable usage evidence.')
  })

  it('distinguishes an incomplete below-threshold result from a complete one and never shows it as an all-clear', () => {
    render(
      <TokenWarningPanel
        runId="run-1"
        tokenWarnings={[
          warning({ provider: 'Codex' }),
          warning({
            provider: 'ClaudeCode', state: 'Indeterminate', thresholdTokens: 1000, knownTokenCount: 4, countedAttempts: 1,
            insufficientEvidenceAttempts: 1, unattributedAttempts: 2,
          }),
        ]}
      />,
    )

    const text = claudeSection().textContent ?? ''
    expect(text).toContain('Claude Code: not an all-clear.')
    expect(text).toContain('4 reported tokens are known, below the 1,000 threshold')
    expect(text).toContain('1 without complete usage evidence (all four counts are required)')
    expect(text).toContain('2 dispatched attempt(s) not attributable to a provider')
    expect(text).toContain('the real count may be higher')
    expect(text).not.toContain('with complete evidence')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows a genuine known zero differently from no evidence', () => {
    render(
      <TokenWarningPanel
        runId="run-1"
        tokenWarnings={[
          warning({ provider: 'Codex', state: 'BelowThresholdComplete', thresholdTokens: 10, knownTokenCount: 0, countedAttempts: 2 }),
          warning({ provider: 'ClaudeCode', state: 'NoEvidence', thresholdTokens: 10 }),
        ]}
      />,
    )

    expect(within(codexSection()).getByText('Codex: known zero — 0 reported tokens across 2 concluded attempt(s); below the 10 threshold.')).toBeTruthy()
    expect(within(claudeSection()).getByText(/no dispatched attempts recorded yet, so there is no evidence either way \(this is not a known zero\)/)).toBeTruthy()
  })

  it('states each providers own count formula', () => {
    render(<TokenWarningPanel runId="run-1" tokenWarnings={NEUTRAL} />)

    expect(within(codexSection()).getByText(/input \+ output tokens \(cached input is already part of input\)/)).toBeTruthy()
    expect(within(claudeSection()).getByText(/input \+ cache-creation \+ cache-read \+ output tokens/)).toBeTruthy()
  })

  it('saves a positive whole-number threshold for exactly one provider', async () => {
    const setTokenWarningThreshold = vi.fn().mockResolvedValue(new SetTokenWarningThresholdResponse({ provider: 'ClaudeCode', thresholdTokens: 5000 }))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    render(<TokenWarningPanel runId="run-1" tokenWarnings={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Claude Code warning threshold'), { target: { value: '5000' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Claude Code threshold' }))

    await waitFor(() => expect(setTokenWarningThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenWarningThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'ClaudeCode', thresholdTokens: 5000 }))
  })

  it.each(['', '0', '-3', '1.5', '1e3', 'abc', '1000000000001', '99999999999999999999', '9'.repeat(400)])('rejects %j locally without calling the API', async (value) => {
    const setTokenWarningThreshold = vi.fn()
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    render(<TokenWarningPanel runId="run-1" tokenWarnings={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Codex warning threshold'), { target: { value } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex threshold' }))

    expect(await screen.findByText('Enter a whole number of tokens from 1 to 1,000,000,000,000.')).toBeTruthy()
    expect(setTokenWarningThreshold).not.toHaveBeenCalled()
  })

  it('clears a configured threshold by sending no value, and offers Clear only when one exists', async () => {
    const setTokenWarningThreshold = vi.fn().mockResolvedValue(new SetTokenWarningThresholdResponse({ provider: 'Codex' }))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    render(
      <TokenWarningPanel
        runId="run-1"
        tokenWarnings={[warning({ provider: 'Codex', state: 'NoEvidence', thresholdTokens: 900 }), warning({ provider: 'ClaudeCode' })]}
      />,
    )

    expect((screen.getByRole('button', { name: 'Clear Claude Code threshold' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Clear Codex threshold' }))

    await waitFor(() => expect(setTokenWarningThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenWarningThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: undefined }))
  })

  it('shows a safe error without leaking the failure detail', async () => {
    const setTokenWarningThreshold = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    render(<TokenWarningPanel runId="run-1" tokenWarnings={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Codex warning threshold'), { target: { value: '10' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex threshold' }))

    expect(await screen.findByText('The token warning threshold could not be saved for this run.')).toBeTruthy()
    expect(screen.queryByText(/sensitive detail/)).toBeNull()
  })

  it('renders neutral states when the cockpit carries no warnings at all', () => {
    render(<TokenWarningPanel runId="run-1" tokenWarnings={undefined} />)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(codexSection()).getByText('Codex: no token-activity warning threshold set.')).toBeTruthy()
  })
})
