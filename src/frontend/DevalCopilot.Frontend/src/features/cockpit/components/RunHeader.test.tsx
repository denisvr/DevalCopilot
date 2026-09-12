import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { GetRunCockpitResponse } from '../../../api/generated/api-client'
import { RunHeader } from './RunHeader'

function buildCockpit(overrides: Partial<GetRunCockpitResponse> = {}) {
  return new GetRunCockpitResponse({
    runId: 'run-1',
    projectId: 'project-1',
    projectName: 'DevalCopilot',
    executionNumber: 1,
    objective: 'Prove the walking skeleton',
    lifecycle: 'Running',
    stage: 'Plan',
    activeParticipant: 'Codex',
    autonomousDurationSeconds: 12,
    latestSequence: 2,
    stageMap: [],
    canPause: false,
    canStop: false,
    ...overrides,
  })
}

describe('RunHeader', () => {
  it('shows the running state with the objective and lifecycle badge', () => {
    render(<RunHeader cockpit={buildCockpit()} />)

    expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
    expect(screen.getByText('Running · Plan')).toBeInTheDocument()
  })

  it('shows the terminal completed state', () => {
    render(<RunHeader cockpit={buildCockpit({ lifecycle: 'Completed', stage: 'Completed', activeParticipant: 'None' })} />)

    expect(screen.getByText('Completed · Completed')).toBeInTheDocument()
  })

  it('renders Pause and Stop as visible but disabled controls', () => {
    render(<RunHeader cockpit={buildCockpit()} />)

    expect(screen.getByRole('button', { name: 'Pause' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
  })
})
