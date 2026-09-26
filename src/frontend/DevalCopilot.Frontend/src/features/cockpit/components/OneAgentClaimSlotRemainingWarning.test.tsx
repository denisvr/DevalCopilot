import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { GetRunCockpitResponse } from '../../../api/generated/api-client'
import { OneAgentClaimSlotRemainingWarning } from './OneAgentClaimSlotRemainingWarning'

const RUN_ID = 'run-1'

function cockpitFor(overrides: Partial<GetRunCockpitResponse>): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId: RUN_ID,
    maximumAgentAttempts: 16,
    agentAttemptsUsed: 15,
    agentBudgetExhausted: false,
    ...overrides,
  } as GetRunCockpitResponse)
}

describe('OneAgentClaimSlotRemainingWarning', () => {
  it('renders the warning with the exact used/maximum counts and never implies the next claim is eligible', () => {
    render(<OneAgentClaimSlotRemainingWarning cockpit={cockpitFor({})} selectedRunId={RUN_ID} />)

    const warning = screen.getByRole('status')
    expect(warning).toHaveTextContent('Only one Agent claim slot remains for this run (15/16 used).')
    expect(warning).toHaveTextContent('other controls may still block that attempt')
  })

  it('renders nothing once the budget is fully exhausted', () => {
    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 16 })}
        selectedRunId={RUN_ID}
      />,
    )

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('renders nothing when the explicit exhausted flag is true', () => {
    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ agentBudgetExhausted: true })}
        selectedRunId={RUN_ID}
      />,
    )

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('renders nothing for a historically raised maximum unless exactly one slot remains', () => {
    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ maximumAgentAttempts: 19, agentAttemptsUsed: 10 })}
        selectedRunId={RUN_ID}
      />,
    )

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('renders the warning for a historically raised maximum with one slot remaining', () => {
    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ maximumAgentAttempts: 19, agentAttemptsUsed: 18 })}
        selectedRunId={RUN_ID}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('(18/19 used)')
  })

  it('renders nothing for malformed or inconsistent budget data', () => {
    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ maximumAgentAttempts: 16.5 })}
        selectedRunId={RUN_ID}
      />,
    )
    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    render(
      <OneAgentClaimSlotRemainingWarning
        cockpit={cockpitFor({ agentAttemptsUsed: undefined })}
        selectedRunId={RUN_ID}
      />,
    )
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('renders nothing for a cockpit still describing a previously selected run', () => {
    render(<OneAgentClaimSlotRemainingWarning cockpit={cockpitFor({})} selectedRunId="run-2" />)

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('renders nothing without a cockpit', () => {
    render(<OneAgentClaimSlotRemainingWarning cockpit={null} selectedRunId={RUN_ID} />)

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
