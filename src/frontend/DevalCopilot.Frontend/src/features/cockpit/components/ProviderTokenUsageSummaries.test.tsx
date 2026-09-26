import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProviderTokenUsageSummaries } from './ProviderTokenUsageSummaries'

describe('ProviderTokenUsageSummaries', () => {
  it('renders nothing without any entries', () => {
    render(<ProviderTokenUsageSummaries entries={null} />)
    expect(screen.queryByLabelText('Provider token usage')).not.toBeInTheDocument()

    render(<ProviderTokenUsageSummaries entries={[]} />)
    expect(screen.queryByLabelText('Provider token usage')).not.toBeInTheDocument()
  })

  it('renders one line per provider bucket, each with its own attribution and completeness', () => {
    render(
      <ProviderTokenUsageSummaries
        entries={[
          {
            attribution: 'Codex',
            summary: { completeness: 'Complete', attemptsWithKnownUsage: 1, attemptsWithUnknownUsage: 0, inputTokens: 1000, outputTokens: 200 },
          },
          {
            attribution: 'ClaudeCode',
            summary: { completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 },
          },
          {
            attribution: 'Unattributed',
            summary: { completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 },
          },
        ]}
      />,
    )

    const list = screen.getByLabelText('Provider token usage')
    const items = list.querySelectorAll('li')
    expect(items).toHaveLength(3)

    expect(items[0]).toHaveAttribute('data-attribution', 'Codex')
    expect(items[0]).toHaveAttribute('data-completeness', 'Complete')
    expect(items[0]).toHaveTextContent('Codex: Run token total: 1,000 input · 200 output')

    expect(items[1]).toHaveAttribute('data-attribution', 'ClaudeCode')
    expect(items[1]).toHaveTextContent('Claude Code: No token usage data yet')

    expect(items[2]).toHaveAttribute('data-attribution', 'Unattributed')
    expect(items[2]).toHaveTextContent('Unattributed: No token usage data yet')
  })

  it('never labels a partial bucket as a total, mirroring the run-wide summary rules', () => {
    render(
      <ProviderTokenUsageSummaries
        entries={[
          {
            attribution: 'Codex',
            summary: {
              completeness: 'Partial',
              attemptsWithKnownUsage: 1,
              attemptsWithUnknownUsage: 1,
              pendingAttemptCount: 0,
              terminalAttemptsWithUnknownUsage: 1,
              inputTokens: 1000,
              outputTokens: 200,
            },
          },
        ]}
      />,
    )

    const item = screen.getByLabelText('Provider token usage').querySelector('li')!
    expect(item).toHaveTextContent('so this is not the run total')
    expect(item.textContent).not.toMatch(/total:/i)
  })
})
