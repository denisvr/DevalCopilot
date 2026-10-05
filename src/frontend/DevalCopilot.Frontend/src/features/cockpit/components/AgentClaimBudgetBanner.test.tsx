import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'

describe('AgentClaimBudgetBanner', () => {
  it('shows a quiet fact, not an alert, while the budget is not exhausted', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={5} agentBudgetExhausted={false} />)
    expect(screen.getByText(/Agent claims:/)).toHaveTextContent('Agent claims: 5 of 16 used.')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows a clear human next action once the budget is exhausted', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={16} agentBudgetExhausted={true} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('16')
    expect(alert).toHaveTextContent(/no further agent attempt can be claimed/i)
  })

  it('never promises an immediate replacement run for a run that is still active', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={16} agentBudgetExhausted={true} />)
    const alert = screen.getByRole('alert')
    expect(alert).not.toHaveTextContent(/start a new run/i)
    expect(alert).toHaveTextContent(/does not finish this run or authorize replacing it/i)
    expect(alert).toHaveTextContent(/only after every run of this project has finished/i)
  })
})
