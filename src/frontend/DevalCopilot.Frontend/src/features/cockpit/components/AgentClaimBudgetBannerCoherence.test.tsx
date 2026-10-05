// @vitest-environment jsdom
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'

const UNSAFE = 9007199254740992

describe('AgentClaimBudgetBanner projection coherence', () => {
  it.each([
    ['exhausted with usage below the ceiling', 4, 0, true],
    ['exhausted with a missing ceiling', undefined, 0, true],
    ['exhausted with a null ceiling', null, 0, true],
    ['exhausted with a missing usage', 4, undefined, true],
    ['exhausted with a null usage', 4, null, true],
    ['exhausted with a zero ceiling', 0, 0, true],
    ['exhausted with a negative usage', 4, -1, true],
    ['exhausted with a fractional ceiling', 4.5, 5, true],
    ['exhausted with a fractional usage', 4, 4.5, true],
    ['exhausted with a NaN ceiling', Number.NaN, 4, true],
    ['exhausted with an infinite usage', 4, Number.POSITIVE_INFINITY, true],
    ['exhausted with an unsafe ceiling', UNSAFE, UNSAFE, true],
    ['exhausted with an unsafe usage', 4, UNSAFE, true],
    ['not exhausted with an unsafe ceiling', UNSAFE, 0, false],
    ['not exhausted with an unsafe usage', 4, UNSAFE, false],
    ['not exhausted with an infinite ceiling', Number.POSITIVE_INFINITY, 0, false],
    ['not exhausted with usage at the ceiling', 4, 4, false],
    ['not exhausted with usage above the ceiling', 4, 5, false],
    ['a missing exhausted flag', 4, 4, undefined],
    ['a null exhausted flag', 4, 0, null],
  ])('shows only the unknown or invalid note, with no count or reached-limit claim, for %s', (_name, maximum, used, exhausted) => {
    const { container } = render(
      <AgentClaimBudgetBanner maximumAgentAttempts={maximum} agentAttemptsUsed={used} agentBudgetExhausted={exhausted} />,
    )
    expect(screen.queryByRole('alert')).toBeNull()
    expect(container).toHaveTextContent(/unknown or invalid/i)
    expect(container).toHaveTextContent(/does not prove any claim is available/i)
    expect(container).not.toHaveTextContent(/reached its maximum/i)
    expect(container).not.toHaveTextContent(/\d+\s*(of|\/)\s*\d+/)
  })

  it.each([
    ['the ordinary exhaustion', 4, 4, true, '4/4 used'],
    ['coherent usage above the ceiling', 4, 5, true, '5/4 used'],
    ['a historical ceiling above 16', 24, 24, true, '24/24 used'],
    ['a historical ceiling above 16 with usage above it', 24, 25, true, '25/24 used'],
  ])('keeps the exhausted alert for %s', (_name, maximum, used, exhausted, count) => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={maximum} agentAttemptsUsed={used} agentBudgetExhausted={exhausted} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(`maximum of ${maximum} claimed Agent attempts`)
    expect(alert).toHaveTextContent(count)
    expect(alert).toHaveTextContent(/does not finish this run or authorize replacing it/i)
  })

  it.each([
    ['a chosen small ceiling', 4, 0, 'Agent claims: 0 of 4 used.'],
    ['one slot remaining', 4, 3, 'Agent claims: 3 of 4 used.'],
    ['a historical ceiling above 16', 24, 20, 'Agent claims: 20 of 24 used.'],
    ['the largest safe ceiling', Number.MAX_SAFE_INTEGER, 0, `Agent claims: 0 of ${Number.MAX_SAFE_INTEGER} used.`],
  ])('keeps the quiet pre-exhaustion fact for %s', (_name, maximum, used, text) => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={maximum} agentAttemptsUsed={used} agentBudgetExhausted={false} />)
    expect(screen.getByText(/Agent claims:/)).toHaveTextContent(text)
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
