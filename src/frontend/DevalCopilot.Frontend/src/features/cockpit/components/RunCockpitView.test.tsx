import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { GetRunCockpitResponse } from '../../../api/generated/api-client'
import * as useRunCockpitModule from '../hooks/useRunCockpit'
import { RunCockpitView } from './RunCockpitView'

vi.mock('../hooks/useRunCockpit')

const useRunCockpitMock = vi.mocked(useRunCockpitModule.useRunCockpit)

describe('RunCockpitView', () => {
  it('shows a loading state before the first cockpit projection arrives', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: null,
      cards: [],
      connection: 'connecting',
      loading: true,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByText('Loading run…')).toBeInTheDocument()
  })

  it('renders the run header and workspace once the cockpit projection is running', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        runId: 'run-1',
        projectId: 'project-1',
        projectName: 'DevalCopilot',
        executionNumber: 1,
        objective: 'Prove the walking skeleton',
        lifecycle: 'Running',
        stage: 'Plan',
        activeParticipant: 'Codex',
        autonomousDurationSeconds: 5,
        latestSequence: 2,
        stageMap: [],
        canPause: false,
        canStop: false,
      }),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
    expect(screen.getByText('Running · Plan')).toBeInTheDocument()
  })

  it('renders the terminal completed state', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        runId: 'run-1',
        projectId: 'project-1',
        projectName: 'DevalCopilot',
        executionNumber: 1,
        objective: 'Prove the walking skeleton',
        lifecycle: 'Completed',
        stage: 'Completed',
        activeParticipant: 'None',
        autonomousDurationSeconds: 5,
        latestSequence: 6,
        stageMap: [],
        canPause: false,
        canStop: false,
      }),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByText('Completed · Completed')).toBeInTheDocument()
  })

  it('shows the stale-state banner when the live connection is disconnected', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        runId: 'run-1',
        projectId: 'project-1',
        projectName: 'DevalCopilot',
        executionNumber: 1,
        objective: 'Prove the walking skeleton',
        lifecycle: 'Running',
        stage: 'Plan',
        activeParticipant: 'Codex',
        autonomousDurationSeconds: 5,
        latestSequence: 2,
        stageMap: [],
        canPause: false,
        canStop: false,
      }),
      cards: [],
      connection: 'disconnected',
      loading: false,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('status')).toHaveTextContent(/disconnected/i)
  })

  it('shows the sync-error reason without discarding the already-loaded cockpit', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        runId: 'run-1',
        projectId: 'project-1',
        projectName: 'DevalCopilot',
        executionNumber: 1,
        objective: 'Prove the walking skeleton',
        lifecycle: 'Running',
        stage: 'Plan',
        activeParticipant: 'Codex',
        autonomousDurationSeconds: 5,
        latestSequence: 2,
        stageMap: [],
        canPause: false,
        canStop: false,
      }),
      cards: [],
      connection: 'disconnected',
      loading: false,
      error: null,
      syncError: 'cockpit query unavailable',
    })

    render(<RunCockpitView runId="run-1" />)

    // The specific sync-error reason is surfaced, not just the generic disconnected text —
    // and the cockpit already on screen is untouched by it.
    expect(screen.getByRole('status')).toHaveTextContent('cockpit query unavailable')
    expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
  })
})
