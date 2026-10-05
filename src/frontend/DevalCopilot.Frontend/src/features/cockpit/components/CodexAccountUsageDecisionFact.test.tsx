// @vitest-environment jsdom
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CodexAccountUsageDecisionResponse, CodexAccountUsageStopResponse, CodexAccountUsageWindowResponse } from '../../../api/generated/api-client'
import { CodexAccountUsageDecisionFact } from './CodexAccountUsageDecisionFact'

const NOTE = 'A local guard over a provider-reported percentage; it says nothing about account access, remaining quota or live capacity.'
const stop = new CodexAccountUsageStopResponse({ state: 'Configured', percent: 80 })
const retrievedAtUtc = new Date('2026-10-01T12:30:00Z')
const decision = new CodexAccountUsageDecisionResponse({
  state: 'Recorded',
  decision: 'Reached',
  reason: 'ThresholdReached',
  thresholdPercent: 80,
  retrievedAtUtc,
  windows: [new CodexAccountUsageWindowResponse({ bucketId: 'codex', window: 'Primary', usedPercent: 85 })],
})

describe('CodexAccountUsageDecisionFact', () => {
  it('renders nothing for a non-Codex attempt', () => {
    const { container } = render(<CodexAccountUsageDecisionFact provider="ClaudeCode" stop={stop} decision={decision} />)
    expect(container.textContent).toBe('')
  })

  it('shows the claim-time stop, the decision headline, retrieval time, windows and the fixed note', () => {
    render(<CodexAccountUsageDecisionFact provider="Codex" stop={stop} decision={decision} />)

    const region = screen.getByRole('region', { name: 'Codex account-usage stop' })
    expect(within(region).getByText('Claimed with account-usage stop: 80% used')).toBeTruthy()
    expect(within(region).getByText('Not started: a reported usage window reached the configured stop (80%).')).toBeTruthy()
    expect(within(region).getByText(`Host retrieval time: ${retrievedAtUtc.toLocaleString()}`)).toBeTruthy()
    expect(within(region).getByText('codex primary window: 85% used')).toBeTruthy()
    expect(within(region).getByText(NOTE)).toBeTruthy()
  })

  it('shows no decision line when there is none, and never guesses a below-threshold result', () => {
    const { container } = render(<CodexAccountUsageDecisionFact provider="Codex" stop={stop} decision={null} />)

    expect(screen.getByText('Claimed with account-usage stop: 80% used')).toBeTruthy()
    expect(container.textContent).not.toMatch(/Not started|below|under|within/i)
  })

  it('shows an unverifiable message for an Unavailable decision', () => {
    render(
      <CodexAccountUsageDecisionFact
        provider="Codex"
        stop={undefined}
        decision={new CodexAccountUsageDecisionResponse({ state: 'Unavailable', decision: 'Unavailable', windows: [] })}
      />,
    )

    expect(screen.getByText('The stored account-usage decision could not be verified.')).toBeTruthy()
    expect(screen.queryByText(/Claimed with/)).toBeNull()
  })
})
