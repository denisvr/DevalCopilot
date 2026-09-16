import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { CapabilityReadinessResponse } from '../../../api/clients'
import { CapabilityReadinessStrip } from './CapabilityReadinessStrip'

function capability(overrides: Partial<CapabilityReadinessResponse>): CapabilityReadinessResponse {
  return {
    capability: 'Git',
    isRequired: true,
    displayStatus: 'Ready',
    reasonCode: 'None',
    version: '2.43.0',
    lastCheckedUtc: new Date('2026-09-13T12:00:00Z'),
    isStale: false,
    ...overrides,
  } as CapabilityReadinessResponse
}

describe('CapabilityReadinessStrip', () => {
  it('renders nothing when there are no capabilities', () => {
    const { container } = render(<CapabilityReadinessStrip capabilities={[]} refreshingCapability={null} onRefresh={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows Ready for a successful parsed probe', () => {
    render(<CapabilityReadinessStrip capabilities={[capability({})]} refreshingCapability={null} onRefresh={vi.fn()} />)
    expect(screen.getByText('Ready')).toBeInTheDocument()
    expect(screen.getByText('2.43.0')).toBeInTheDocument()
  })

  it('maps a required capability that is not found to Unavailable', () => {
    render(
      <CapabilityReadinessStrip
        capabilities={[capability({ capability: 'Git', isRequired: true, displayStatus: 'Unavailable', version: undefined })]}
        refreshingCapability={null}
        onRefresh={vi.fn()}
      />,
    )
    expect(screen.getByText('Unavailable')).toBeInTheDocument()
  })

  it('maps optional Docker that is not found to Degraded, not Unavailable', () => {
    render(
      <CapabilityReadinessStrip
        capabilities={[capability({ capability: 'Docker', isRequired: false, displayStatus: 'Degraded', version: undefined })]}
        refreshingCapability={null}
        onRefresh={vi.fn()}
      />,
    )
    expect(screen.getByText('Degraded')).toBeInTheDocument()
    expect(screen.queryByText('Unavailable')).not.toBeInTheDocument()
  })

  it('shows Checking for a capability with no display status yet', () => {
    render(
      <CapabilityReadinessStrip
        capabilities={[capability({ displayStatus: undefined, reasonCode: 'NeverProbed', version: undefined })]}
        refreshingCapability={null}
        onRefresh={vi.fn()}
      />,
    )
    expect(screen.getByText('Checking…')).toBeInTheDocument()
  })

  it('appends a stale qualifier without changing the underlying status', () => {
    render(<CapabilityReadinessStrip capabilities={[capability({ isStale: true })]} refreshingCapability={null} onRefresh={vi.fn()} />)
    expect(screen.getByText(/Ready \(stale\)/)).toBeInTheDocument()
  })

  it('invokes onRefresh with the capability name when its refresh button is clicked', () => {
    const onRefresh = vi.fn()
    render(<CapabilityReadinessStrip capabilities={[capability({ capability: 'Node' })]} refreshingCapability={null} onRefresh={onRefresh} />)

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(onRefresh).toHaveBeenCalledWith('Node')
  })

  it('disables and relabels the refresh button for the capability currently refreshing', () => {
    render(<CapabilityReadinessStrip capabilities={[capability({ capability: 'Node' })]} refreshingCapability="Node" onRefresh={vi.fn()} />)

    const button = screen.getByRole('button', { name: 'Refreshing…' })
    expect(button).toBeDisabled()
  })
})
