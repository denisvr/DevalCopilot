import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GetRunCockpitResponse, ParticipantIdentityResponse } from '../../../api/generated/api-client'
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
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 12,
    latestSequence: 2,
    stageMap: [],
    canPause: false,
    canStop: false,
    ...overrides,
  })
}

afterEach(() => {
  vi.useRealTimers()
})

describe('RunHeader', () => {
  it('shows the running state with the objective and lifecycle badge', () => {
    render(<RunHeader cockpit={buildCockpit()} />)

    expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
    expect(screen.getByText('Running · Plan')).toBeInTheDocument()
  })

  it('shows the terminal completed state', () => {
    render(<RunHeader cockpit={buildCockpit({
      lifecycle: 'Completed',
      stage: 'Completed',
      activeParticipant: new ParticipantIdentityResponse({ kind: 'None' }),
    })} />)

    expect(screen.getByText('Completed · Completed')).toBeInTheDocument()
  })

  it('shows an abandoned run as its own terminal state, never as a completion, and keeps its time frozen', () => {
    vi.useFakeTimers()
    render(<RunHeader cockpit={buildCockpit({
      lifecycle: 'Abandoned',
      stage: 'Plan',
      autonomousDurationSeconds: 90,
      activeParticipant: new ParticipantIdentityResponse({ kind: 'None' }),
    })} />)

    const badge = screen.getByText('Abandoned · Plan')
    expect(badge).toHaveAttribute('data-tone', 'abandoned')
    expect(screen.queryByText(/Completed/)).not.toBeInTheDocument()
    const before = screen.getByTitle('Autonomous session time: excludes paused and terminal time').textContent
    act(() => {
      vi.advanceTimersByTime(120_000)
    })
    expect(screen.getByTitle('Autonomous session time: excludes paused and terminal time').textContent).toBe(before)
  })

  it('renders Pause and Stop as visible but disabled controls', () => {
    render(<RunHeader cockpit={buildCockpit()} />)

    expect(screen.getByRole('button', { name: 'Pause' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
  })
})
