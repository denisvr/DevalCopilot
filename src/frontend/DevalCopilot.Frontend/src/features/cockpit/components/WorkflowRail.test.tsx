import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StageMapEntryResponse } from '../../../api/generated/api-client'
import { WorkflowRail } from './WorkflowRail'

const stageMap = [
  new StageMapEntryResponse({ stage: 'Intake', isCompleted: true, isActive: false }),
  new StageMapEntryResponse({ stage: 'Plan', isCompleted: false, isActive: true }),
  new StageMapEntryResponse({ stage: 'Critique', isCompleted: false, isActive: false }),
]

describe('WorkflowRail', () => {
  it('renders every stage with its completed and active state', () => {
    render(<WorkflowRail stageMap={stageMap} />)

    const intake = screen.getByText('Intake').closest('li')
    const plan = screen.getByText('Plan').closest('li')

    expect(intake).toHaveAttribute('data-completed', 'true')
    expect(plan).toHaveAttribute('data-active', 'true')
  })

  it('collapses to a compact progress indicator', () => {
    render(<WorkflowRail stageMap={stageMap} />)

    fireEvent.click(screen.getByRole('button', { name: 'Collapse workflow rail' }))

    expect(screen.getByText('1/3')).toBeInTheDocument()
    expect(screen.queryByText('Critique')).not.toBeInTheDocument()
  })
})
