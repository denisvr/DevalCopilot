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

  describe('an Agent run and the host observation (ADR-0056)', () => {
    it('shows the lifecycle exactly as recorded and the duration as lifecycle elapsed time that never ticks, even with no planning sequence', () => {
      vi.useFakeTimers()
      render(<RunHeader cockpit={buildCockpit()} planningSequenceControl="none" agentRun />)

      const badge = screen.getByText('Running · Plan')
      expect(badge).toHaveAttribute('data-recorded-lifecycle', 'true')
      expect(badge).toHaveAttribute('data-tone', 'default')
      expect(badge).toHaveAttribute('title', expect.stringContaining('not a live-execution indicator'))
      const duration = screen.getByTitle('Lifecycle elapsed time as recorded: not measured agent activity')
      expect(screen.queryByTitle('Autonomous session time: excludes paused and terminal time')).not.toBeInTheDocument()
      const before = duration.textContent
      act(() => {
        vi.advanceTimersByTime(120_000)
      })
      expect(duration.textContent).toBe(before)
    })

    it('keeps the live treatment only for a run that is not an Agent run', () => {
      render(<RunHeader cockpit={buildCockpit()} planningSequenceControl="none" agentRun={false} />)

      expect(screen.getByText('Running · Plan')).toHaveAttribute('data-tone', 'running')
      expect(screen.getByTitle('Autonomous session time: excludes paused and terminal time')).toBeInTheDocument()
    })

    it('words the host observation in its own badge, separately from the lifecycle, and shows none when there is nothing to say', () => {
      const { rerender } = render(<RunHeader cockpit={buildCockpit()} agentRun hostObservation="Host observation: reporting" />)

      expect(screen.getByTestId('header-host-observation')).toHaveTextContent('Host observation: reporting')
      expect(screen.getByTestId('header-host-observation')).toHaveAttribute('title', expect.stringContaining('not provider progress'))
      expect(screen.getByText('Running · Plan')).not.toHaveTextContent('Host observation')

      rerender(<RunHeader cockpit={buildCockpit()} agentRun hostObservation={null} />)
      expect(screen.queryByTestId('header-host-observation')).not.toBeInTheDocument()
    })
  })
})
