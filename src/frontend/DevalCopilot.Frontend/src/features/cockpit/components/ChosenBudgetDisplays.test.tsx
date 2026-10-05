// @vitest-environment jsdom
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'
import { AgentInvocationTimeBudgetBanner } from './AgentInvocationTimeBudgetBanner'

describe('AgentClaimBudgetBanner before exhaustion', () => {
  it('shows the actual immutable non-default ceiling and usage as a quiet status', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={4} agentAttemptsUsed={1} agentBudgetExhausted={false} />)
    const fact = screen.getByText(/Agent claim/)
    expect(fact).toHaveTextContent('Agent claims: 1 of 4 used.')
    expect(fact).toHaveTextContent(/fixed when the run was recorded and cannot be changed/i)
    expect(fact).toHaveTextContent(/stays consumed after a failure or interruption/i)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows an unused default ceiling too, so the choice is confirmable after creation', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={0} agentBudgetExhausted={false} />)
    expect(screen.getByText(/Agent claim/)).toHaveTextContent('Agent claims: 0 of 16 used.')
  })

  it('never claims a claim is available', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={4} agentAttemptsUsed={1} agentBudgetExhausted={false} />)
    expect(screen.getByText(/Agent claim/)).not.toHaveTextContent(/available|eligible|can be claimed/i)
  })

  it.each([
    ['a missing ceiling', undefined, 1, false],
    ['a missing usage', 4, undefined, false],
    ['a missing exhausted flag', 4, 1, undefined],
    ['a null ceiling', null, 1, false],
    ['a zero ceiling', 0, 0, false],
    ['a negative usage', 4, -1, false],
    ['a fractional ceiling', 4.5, 1, false],
    ['a fractional usage', 4, 1.5, false],
    ['usage above the ceiling', 4, 5, false],
    ['usage at the ceiling without the exhausted flag', 4, 4, false],
  ])('shows a distinct unknown or invalid state for %s and no count', (_name, maximum, used, exhausted) => {
    render(
      <AgentClaimBudgetBanner maximumAgentAttempts={maximum} agentAttemptsUsed={used} agentBudgetExhausted={exhausted} />,
    )
    const fact = screen.getByText(/Agent claim/)
    expect(fact).toHaveTextContent(/unknown or invalid/i)
    expect(fact).toHaveTextContent(/does not prove any claim is available/i)
    expect(fact).not.toHaveTextContent(/\d+ of \d+ used/)
  })

  it('keeps the exhausted alert for a non-default ceiling', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={4} agentAttemptsUsed={4} agentBudgetExhausted={true} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('maximum of 4 claimed Agent attempts')
    expect(alert).toHaveTextContent('4/4 used')
    expect(screen.queryByRole('status')).toBeNull()
  })
})

describe('AgentInvocationTimeBudgetBanner for a chosen ceiling', () => {
  it('shows the non-default ceiling, the reservation and the remainder without proving eligibility', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={40 * 60000}
        reservedMilliseconds={10 * 60000}
        remainingMilliseconds={30 * 60000}
        isLegacyUnknown={false}
        evidenceInvalid={false}
      />,
    )
    const fact = screen.getByText(/Reserved Agent invocation time/)
    expect(fact).toHaveTextContent('10 min of 40 min (30 min remaining)')
    expect(fact).toHaveTextContent(/reserved from configured timeouts, not measured elapsed time/i)
    expect(fact).toHaveTextContent(/positive remainder does not prove that any role can be claimed/i)
  })

  it('shows an unused small ceiling before any claim', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={9 * 60000}
        reservedMilliseconds={0}
        remainingMilliseconds={9 * 60000}
        isLegacyUnknown={false}
        evidenceInvalid={false}
      />,
    )
    expect(screen.getByText(/Reserved Agent invocation time/)).toHaveTextContent('0 min of 9 min (9 min remaining)')
  })
})
