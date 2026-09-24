import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'

describe('AgentClaimBudgetBanner', () => {
  it('renders nothing while the budget is not exhausted', () => {
    const { container } = render(
      <AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={5} agentBudgetExhausted={false} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('shows a clear human next action once the budget is exhausted', () => {
    render(<AgentClaimBudgetBanner maximumAgentAttempts={16} agentAttemptsUsed={16} agentBudgetExhausted={true} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('16')
    expect(alert).toHaveTextContent(/no further agent attempt can be claimed/i)
  })
})
