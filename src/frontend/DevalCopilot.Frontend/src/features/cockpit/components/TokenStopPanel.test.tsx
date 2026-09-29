// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RunCockpitTokenStopResponse, SetTokenStopThresholdResponse } from '../../../api/generated/api-client'
import { setTokenStopThresholdClient } from '../../../api/clients'
import { TokenStopPanel } from './TokenStopPanel'

vi.mock('../../../api/clients', () => ({
  setTokenStopThresholdClient: vi.fn(),
}))

function stop(overrides: Partial<ConstructorParameters<typeof RunCockpitTokenStopResponse>[0]> & { provider: string }) {
  return new RunCockpitTokenStopResponse({
    state: 'NotConfigured',
    claimBlocked: false,
    knownTokenCount: 0,
    countedAttempts: 0,
    pendingAttempts: 0,
    insufficientEvidenceAttempts: 0,
    unattributedAttempts: 0,
    countOverflowed: false,
    ...overrides,
  })
}

const NEUTRAL = [stop({ provider: 'Codex' }), stop({ provider: 'ClaudeCode' })]

function codexSection() {
  return screen.getByLabelText('Codex token stop')
}

function claudeSection() {
  return screen.getByLabelText('Claude Code token stop')
}

describe('TokenStopPanel', () => {
  it('is neutral with no stops: no alert and no eligibility claim, and states what a stop is not', () => {
    render(<TokenStopPanel runId="run-1" tokenStops={NEUTRAL} />)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(codexSection()).getByText('Codex: no token stop threshold set; a new Codex attempt is not limited by a token stop.')).toBeTruthy()
    expect(within(claudeSection()).getByText(/Claude Code: no token stop threshold set/)).toBeTruthy()
    expect(screen.getByText(/not an account allowance, does not cap or cancel a claimed attempt, and does not show that a provider is available/)).toBeTruthy()
  })

  it('shows a blocking alert only for the provider whose stop is reached and says a claimed attempt is unaffected', () => {
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[
          stop({ provider: 'Codex', state: 'ThresholdReached', claimBlocked: true, thresholdTokens: 1200, knownTokenCount: 1200, countedAttempts: 1 }),
          stop({ provider: 'ClaudeCode', state: 'BelowThresholdComplete', thresholdTokens: 10_000, knownTokenCount: 615, countedAttempts: 1 }),
        ]}
      />,
    )

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Codex token stop reached: 1,200 reported tokens recorded across 1 concluded attempt(s), at or above the 1,200 threshold.')
    expect(alert).toHaveTextContent('New Codex attempts are refused until the threshold is raised or cleared; an attempt already claimed is unaffected.')
    expect(within(claudeSection()).queryByRole('alert')).toBeNull()
  })

  it('marks a reached stop with evidence gaps as a lower bound', () => {
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[
          stop({ provider: 'Codex', state: 'ThresholdReached', claimBlocked: true, thresholdTokens: 15, knownTokenCount: 15, countedAttempts: 1, insufficientEvidenceAttempts: 1, pendingAttempts: 1 }),
          stop({ provider: 'ClaudeCode' }),
        ]}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('This is a lower bound; evidence gaps: 1 still running, 1 without usable usage evidence.')
  })

  it('states the exact reason an unprovable stop refuses attempts, listing every evidence gap', () => {
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[
          stop({ provider: 'Codex' }),
          stop({
            provider: 'ClaudeCode', state: 'EvidenceIndeterminate', claimBlocked: true, thresholdTokens: 1000, knownTokenCount: 4,
            countedAttempts: 1, insufficientEvidenceAttempts: 1, pendingAttempts: 1, unattributedAttempts: 2,
          }),
        ]}
      />,
    )

    const text = screen.getByRole('alert').textContent ?? ''
    expect(text).toContain('Claude Code token stop cannot be cleared: staying below the 1,000 threshold cannot be proved')
    expect(text).toContain('1 still running')
    expect(text).toContain('1 without complete usage evidence (all four counts are required)')
    expect(text).toContain('2 dispatched attempt(s) not attributable to a provider')
    expect(text).toContain('New Claude Code attempts are refused.')
    expect(text).toContain('4 reported tokens are known so far; the real count may be higher.')
  })

  it('reports an unrepresentable total without inventing a number', () => {
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[
          stop({ provider: 'Codex', state: 'EvidenceIndeterminate', claimBlocked: true, thresholdTokens: 5, knownTokenCount: Number.MAX_SAFE_INTEGER, countOverflowed: true }),
          stop({ provider: 'ClaudeCode' }),
        ]}
      />,
    )

    const text = screen.getByRole('alert').textContent ?? ''
    expect(text).toContain('the total is not representable')
    expect(text).toContain('an unrepresentable number of reported tokens are known so far')
    expect(text).not.toContain("9,007")
  })

  it('never presents a permitting state as provider eligibility or as a known zero', () => {
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[
          stop({ provider: 'Codex', state: 'NoDispatchedHistory', thresholdTokens: 10 }),
          stop({ provider: 'ClaudeCode', state: 'BelowThresholdComplete', thresholdTokens: 10, knownTokenCount: 0, countedAttempts: 2 }),
        ]}
      />,
    )

    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(codexSection()).getByText(/stop set at 10\. No dispatched attempts are recorded, so this stop does not refuse the first attempt \(this is not a measured zero, and it does not show that the provider is available\)/)).toBeTruthy()
    expect(within(claudeSection()).getByText(/0 reported tokens across 2 concluded attempt\(s\), below the 10 threshold, with complete evidence\. This stop does not refuse a new attempt; it does not show that the provider is available/)).toBeTruthy()
    expect(screen.queryByText(/eligible|can be claimed|safe to/i)).toBeNull()
  })

  it('states each providers own count formula', () => {
    render(<TokenStopPanel runId="run-1" tokenStops={NEUTRAL} />)

    expect(within(codexSection()).getByText(/input \+ output tokens \(cached input is already part of input\)/)).toBeTruthy()
    expect(within(claudeSection()).getByText(/input \+ cache-creation \+ cache-read \+ output tokens/)).toBeTruthy()
  })

  it('saves a positive whole-number stop for exactly one provider', async () => {
    const setTokenStopThreshold = vi.fn().mockResolvedValue(new SetTokenStopThresholdResponse({ provider: 'ClaudeCode', thresholdTokens: 5000 }))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    render(<TokenStopPanel runId="run-1" tokenStops={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Claude Code stop threshold'), { target: { value: '5000' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Claude Code stop' }))

    await waitFor(() => expect(setTokenStopThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenStopThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'ClaudeCode', thresholdTokens: 5000 }))
  })

  it.each(['', '0', '-3', '1.5', '1e3', 'abc', '1000000000001', '99999999999999999999', '9'.repeat(400)])('rejects %j locally without calling the API', async (value) => {
    const setTokenStopThreshold = vi.fn()
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    render(<TokenStopPanel runId="run-1" tokenStops={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))

    expect(await screen.findByText('Enter a whole number of tokens from 1 to 1,000,000,000,000.')).toBeTruthy()
    expect(setTokenStopThreshold).not.toHaveBeenCalled()
  })

  it('clears a configured stop by sending no value, and offers Clear only when one exists', async () => {
    const setTokenStopThreshold = vi.fn().mockResolvedValue(new SetTokenStopThresholdResponse({ provider: 'Codex' }))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    render(
      <TokenStopPanel
        runId="run-1"
        tokenStops={[stop({ provider: 'Codex', state: 'NoDispatchedHistory', thresholdTokens: 900 }), stop({ provider: 'ClaudeCode' })]}
      />,
    )

    expect((screen.getByRole('button', { name: 'Clear Claude Code stop' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Clear Codex stop' }))

    await waitFor(() => expect(setTokenStopThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenStopThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: undefined }))
  })

  it('shows a safe error without leaking the failure detail', async () => {
    const setTokenStopThreshold = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    render(<TokenStopPanel runId="run-1" tokenStops={NEUTRAL} />)

    fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value: '10' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))

    expect(await screen.findByText('The token stop threshold could not be saved for this run.')).toBeTruthy()
    expect(screen.queryByText(/sensitive detail/)).toBeNull()
  })

  it('renders neutral states when the cockpit carries no stops at all', () => {
    render(<TokenStopPanel runId="run-1" tokenStops={undefined} />)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(codexSection()).getByText(/no token stop threshold set/)).toBeTruthy()
  })
})
