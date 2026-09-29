import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AgentAttemptStatusResponse } from '../../../api/generated/api-client'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { CodexPlanningRepairAction } from './CodexPlanningRepairAction'

const REPAIR_BUTTON = 'Request one format-repair plan'

function invalidStatus(overrides: Partial<ConstructorParameters<typeof AgentAttemptStatusResponse>[0]> = {}) {
  return new AgentAttemptStatusResponse({
    hasAttempt: true,
    attemptId: 'attempt-3',
    attemptNumber: 3,
    status: 'Failed',
    outcome: 'InvalidStructuredOutput',
    ...overrides,
  })
}

function renderAction(
  overrides: {
    status?: AgentAttemptStatusResponse | null
    statusLoading?: boolean
    ordinaryRequesting?: boolean
    repairRequesting?: boolean
    repairError?: string | null
    onRequestRepair?: (sourceAttemptId: string) => void
    globalClaimBlock?: GlobalAgentClaimBlock | null
    timeFit?: AgentClaimPathTimeFit
  } = {},
) {
  return render(
    <CodexPlanningRepairAction
      status={invalidStatus()}
      statusLoading={false}
      ordinaryRequesting={false}
      repairRequesting={false}
      repairError={null}
      onRequestRepair={vi.fn()}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
      {...overrides}
    />,
  )
}

describe('CodexPlanningRepairAction', () => {
  it('suggests one repair for the latest invalid attempt and sends that attempt id', () => {
    const onRequestRepair = vi.fn()
    renderAction({ onRequestRepair })

    expect(screen.getByText(/does not correct or reuse the earlier response/i)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: REPAIR_BUTTON }))

    expect(onRequestRepair).toHaveBeenCalledExactlyOnceWith('attempt-3')
  })

  it.each([
    ['no attempt', null],
    ['a different terminal outcome', invalidStatus({ outcome: 'ProviderInvocationFailed' })],
    ['a proposed plan', invalidStatus({ status: 'Completed', outcome: 'Proposed' })],
    ['a running attempt', invalidStatus({ status: 'Running', outcome: undefined })],
    ['a missing attempt id', invalidStatus({ attemptId: undefined })],
  ])('offers no repair for %s', (_name, status) => {
    const { container } = renderAction({ status })

    expect(screen.queryByRole('button', { name: REPAIR_BUTTON })).not.toBeInTheDocument()
    expect(container).toBeEmptyDOMElement()
  })

  it('reports lineage and never offers a repair of a repair', () => {
    const { container } = renderAction({
      status: invalidStatus({ attemptNumber: 4, repairSourceAttemptId: 'attempt-3', repairSourceAttemptNumber: 3 }),
    })

    expect(screen.queryByRole('button', { name: REPAIR_BUTTON })).not.toBeInTheDocument()
    expect(container).toHaveTextContent('Attempt #4 is the one repair request for attempt #3.')
    expect(container).toHaveTextContent('not repaired again')
    expect(container).toHaveTextContent('ordinary plan')
    expect(container).not.toHaveTextContent(/fixed|corrected the|preserved/i)
  })

  it('reports lineage without inventing a source number when it is unknown', () => {
    const { container } = renderAction({
      status: invalidStatus({ attemptNumber: 4, repairSourceAttemptId: 'attempt-3' }),
    })

    expect(container).toHaveTextContent('Attempt #4 is a repair request for an earlier attempt.')
    expect(container).not.toHaveTextContent('#undefined')
  })

  it('offers no repair while a global budget block or a time-fit block is known', () => {
    const blocked = renderAction({ globalClaimBlock: { reason: 'CountBudgetExhausted' } })
    expect(screen.queryByRole('button', { name: REPAIR_BUTTON })).not.toBeInTheDocument()
    blocked.unmount()

    renderAction({ timeFit: { reason: 'DoesNotFit' } })
    expect(screen.queryByRole('button', { name: REPAIR_BUTTON })).not.toBeInTheDocument()
  })

  it('disables the repair while it or an ordinary request is in flight, or the status is loading', () => {
    const inFlight = renderAction({ repairRequesting: true })
    expect(screen.getByRole('button', { name: 'Requesting repair…' })).toBeDisabled()
    inFlight.unmount()

    const ordinary = renderAction({ ordinaryRequesting: true })
    expect(screen.getByRole('button', { name: REPAIR_BUTTON })).toBeDisabled()
    ordinary.unmount()

    renderAction({ statusLoading: true })
    expect(screen.getByRole('button', { name: REPAIR_BUTTON })).toBeDisabled()
  })

  it('surfaces a safe repair error without discarding the suggestion', () => {
    renderAction({ repairError: 'A repair was already requested for this attempt.' })

    expect(screen.getByRole('status')).toHaveTextContent('A repair was already requested for this attempt.')
    expect(screen.getByRole('button', { name: REPAIR_BUTTON })).toBeEnabled()
  })
})
