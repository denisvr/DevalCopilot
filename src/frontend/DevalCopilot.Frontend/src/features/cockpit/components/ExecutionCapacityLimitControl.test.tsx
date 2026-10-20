import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { EXECUTION_CAPACITY_CHANGE_FEEDBACK, EXECUTION_CAPACITY_CHANGE_DISCLOSURE } from '../executionCapacity'
import type { ExecutionCapacityChange } from '../executionCapacity'
import { capacityStateFor } from '../executionCapacityFixture'
import type { UseExecutionCapacityResult } from '../hooks/useExecutionCapacity'
import { ExecutionCapacityLimitControl } from './ExecutionCapacityLimitControl'

const IDLE: ExecutionCapacityChange = { phase: 'idle', outcome: null, requested: null }

function capacity(overrides: Partial<UseExecutionCapacityResult> = {}, limit: 1 | 2 = 2): UseExecutionCapacityResult {
  return {
    ...capacityStateFor('Available', {}, limit),
    polling: true,
    change: IDLE,
    applyLimit: vi.fn(),
    ...overrides,
  }
}

const region = () => screen.getByRole('region', { name: 'Recorded-execution limit' })

describe('ExecutionCapacityLimitControl', () => {
  it('is a global control that needs no project: it shows the validated current limit, the one-or-two choice, Apply and Refresh', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity()} />)

    expect(within(region()).getByText('Current limit: two')).toBeInTheDocument()
    expect(within(region()).getByRole('radio', { name: 'One' })).not.toBeChecked()
    expect(within(region()).getByRole('radio', { name: 'Two' })).toBeChecked()
    expect(within(region()).getByRole('button', { name: 'Apply limit' })).toBeEnabled()
    expect(within(region()).getByRole('button', { name: 'Refresh limit' })).toBeEnabled()
  })

  it('states the validated limit one and follows it', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity({}, 1)} />)

    expect(within(region()).getByText('Current limit: one')).toBeInTheDocument()
    expect(within(region()).getByRole('radio', { name: 'One' })).toBeChecked()
  })

  it('explains that changing the limit starts no work', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity()} />)

    expect(within(region()).getByText(EXECUTION_CAPACITY_CHANGE_DISCLOSURE)).toBeInTheDocument()
    expect(EXECUTION_CAPACITY_CHANGE_DISCLOSURE).toMatch(/starts no work/i)
    expect(EXECUTION_CAPACITY_CHANGE_DISCLOSURE).not.toMatch(/pending|later|saved/i)
  })

  it('applies exactly the chosen one or two on an explicit Apply, once, and never on choosing', () => {
    const applyLimit = vi.fn()
    render(<ExecutionCapacityLimitControl capacity={capacity({ applyLimit })} />)

    fireEvent.click(within(region()).getByRole('radio', { name: 'One' }))
    expect(applyLimit).not.toHaveBeenCalled()
    fireEvent.click(within(region()).getByRole('button', { name: 'Apply limit' }))

    expect(applyLimit).toHaveBeenCalledTimes(1)
    expect(applyLimit).toHaveBeenCalledWith(1)
  })

  it('reads the capacity again on an explicit Refresh and nothing else', () => {
    const refresh = vi.fn()
    const applyLimit = vi.fn()
    render(<ExecutionCapacityLimitControl capacity={capacity({ refresh, applyLimit })} />)

    fireEvent.click(within(region()).getByRole('button', { name: 'Refresh limit' }))

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(applyLimit).not.toHaveBeenCalled()
  })

  it.each(['applying', 'confirming'] as const)('disables the choice and Apply while a change is %s, and keeps Refresh available', (phase) => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ change: { phase, outcome: null, requested: 1 } })} />)

    expect(within(region()).getByRole('button', { name: 'Apply limit' })).toBeDisabled()
    expect(within(region()).getByRole('radio', { name: 'One' })).toBeDisabled()
    expect(within(region()).getByRole('radio', { name: 'Two' })).toBeDisabled()
    expect(within(region()).getByRole('button', { name: 'Refresh limit' })).toBeEnabled()
  })

  it.each([
    ['applying', EXECUTION_CAPACITY_CHANGE_FEEDBACK.applying],
    ['confirming', EXECUTION_CAPACITY_CHANGE_FEEDBACK.confirming],
  ] as const)('shows the fixed %s feedback in a status', (phase, text) => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ reading: null, change: { phase, outcome: null, requested: 1 } })} />)

    expect(within(region()).getByRole('status')).toHaveTextContent(text)
  })

  it.each([
    ['belowOwners', EXECUTION_CAPACITY_CHANGE_FEEDBACK.belowOwners],
    ['refused', EXECUTION_CAPACITY_CHANGE_FEEDBACK.refused],
    ['unconfirmed', EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed],
  ] as const)('shows the fixed %s feedback and never a success', (outcome, text) => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ change: { phase: 'idle', outcome, requested: 1 } })} />)

    expect(within(region()).getByRole('status')).toHaveTextContent(text)
    expect(within(region()).queryByText(/is now/i)).toBeNull()
  })

  it('shows a confirmed change with the limit read back', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ change: { phase: 'idle', outcome: 'applied', requested: 1 } }, 1)} />)

    expect(within(region()).getByRole('status')).toHaveTextContent(EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedOne)
  })

  it('shows no feedback before any change', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity()} />)

    expect(within(region()).queryByRole('status')).toBeNull()
  })

  it('says the current limit is unknown without a validated reading, and still lets the operator refresh', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ reading: null, loading: true })} />)

    expect(within(region()).getByText('Current limit: unknown')).toBeInTheDocument()
    expect(within(region()).getByRole('button', { name: 'Refresh limit' })).toBeEnabled()
  })

  it('says the current limit is unknown for a validated Unavailable reading without a limit', () => {
    const unavailable = capacityStateFor('Unavailable')
    render(
      <ExecutionCapacityLimitControl
        capacity={capacity({ reading: { ...unavailable.reading!, limit: null } })}
      />,
    )

    expect(within(region()).getByText('Current limit: unknown')).toBeInTheDocument()
  })

  it('keeps a validated limit visible for an Unavailable reading that retained it', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity({ reading: capacityStateFor('Unavailable', {}, 1).reading })} />)

    expect(within(region()).getByText('Current limit: one')).toBeInTheDocument()
  })

  it('renders no project, path, run or provider text', () => {
    render(<ExecutionCapacityLimitControl capacity={capacity()} />)

    expect(region().textContent).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}|[A-Z]:\\|claude|codex|project/i)
  })
})
