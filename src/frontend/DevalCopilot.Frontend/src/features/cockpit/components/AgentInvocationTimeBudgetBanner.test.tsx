import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentInvocationTimeBudgetBanner } from './AgentInvocationTimeBudgetBanner'

describe('AgentInvocationTimeBudgetBanner', () => {
  it('shows a distinct legacy state instead of a fabricated remaining time', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={null}
        reservedMilliseconds={null}
        remainingMilliseconds={null}
        isLegacyUnknown={true}
        evidenceInvalid={false}
      />,
    )
    expect(screen.getByText(/legacy run/i)).toBeInTheDocument()
    expect(screen.getByText(/not tracked/i)).toBeInTheDocument()
    expect(screen.queryByText(/0 min/i)).not.toBeInTheDocument()
  })

  it('reports invalid prior evidence as unsafe to show rather than as zero reserved', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={120 * 60000}
        reservedMilliseconds={null}
        remainingMilliseconds={null}
        isLegacyUnknown={false}
        evidenceInvalid={true}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/missing or invalid/i)
  })

  it('shows max/reserved/remaining for a budgeted run with room left, without an alert role', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={120 * 60000}
        reservedMilliseconds={30 * 60000}
        remainingMilliseconds={90 * 60000}
        isLegacyUnknown={false}
        evidenceInvalid={false}
      />,
    )
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(/30 min/)).toBeInTheDocument()
    expect(screen.getByText(/120 min/)).toBeInTheDocument()
    expect(screen.getByText(/90 min/)).toBeInTheDocument()
  })

  it('reports exhaustion truthfully once no reserved time remains', () => {
    render(
      <AgentInvocationTimeBudgetBanner
        maximumMilliseconds={120 * 60000}
        reservedMilliseconds={120 * 60000}
        remainingMilliseconds={0}
        isLegacyUnknown={false}
        evidenceInvalid={false}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/reached its maximum reserved agent invocation time/i)
  })
})
