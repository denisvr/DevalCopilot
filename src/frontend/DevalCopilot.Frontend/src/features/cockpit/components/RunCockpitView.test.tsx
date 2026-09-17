import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { processAttemptOutputClient } from '../../../api/clients'
import { GetRunCockpitResponse } from '../../../api/generated/api-client'
import * as useRunCockpitModule from '../hooks/useRunCockpit'
import * as useCollaborationTimelineModule from '../hooks/useCollaborationTimeline'
import type { CollaborationCard } from '../types'
import { RunCockpitView } from './RunCockpitView'

vi.mock('../hooks/useRunCockpit')
vi.mock('../hooks/useCollaborationTimeline')
vi.mock('../../../api/clients', () => ({
  processAttemptOutputClient: vi.fn(),
}))

const useRunCockpitMock = vi.mocked(useRunCockpitModule.useRunCockpit)
const useCollaborationTimelineMock = vi.mocked(useCollaborationTimelineModule.useCollaborationTimeline)

beforeEach(() => {
  useCollaborationTimelineMock.mockReturnValue({
    cards: [],
    loading: false,
    error: null,
    hasSuccessfulResponse: true,
  })
})

function mockProcessOutputClient(getProcessAttemptOutput: ReturnType<typeof vi.fn>) {
  vi.mocked(processAttemptOutputClient).mockReturnValue({
    getProcessAttemptOutput,
  } as unknown as ReturnType<typeof processAttemptOutputClient>)
}

const runningCockpit = new GetRunCockpitResponse({
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
})

function processOutputCard(overrides: Partial<CollaborationCard>): CollaborationCard {
  return {
    sequence: 0,
    id: 'event-0',
    attemptId: null,
    eventType: 'process.output_captured',
    actor: 'Orchestrator',
    summary: 'Captured process output.',
    occurredAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

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

  describe('live output wiring', () => {
    it('retains the truthful simulated-run placeholder when no process attempt exists', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [{ ...processOutputCard({}), eventType: 'codex.proposal', attemptId: null }],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText(/not yet collected for the simulated adapter/i)).toBeInTheDocument()
      expect(processAttemptOutputClient).not.toHaveBeenCalled()
    })

    it('makes both stdout and stderr viewers reachable for a process-output card, with the correct run and attempt ids', async () => {
      const getProcessAttemptOutput = vi
        .fn()
        .mockResolvedValue({ status: 'Ok', text: 'build succeeded', nextOffset: 16, totalLengthSoFar: 16, isFinal: true, truncated: false })
      mockProcessOutputClient(getProcessAttemptOutput)

      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [processOutputCard({ sequence: 1, attemptId: 'attempt-1' })],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      render(<RunCockpitView runId="run-1" />)

      await waitFor(() => expect(getProcessAttemptOutput).toHaveBeenCalledTimes(2))

      const calledStreams = getProcessAttemptOutput.mock.calls.map((call) => call[2])
      expect(calledStreams.sort()).toEqual(['stderr', 'stdout'])
      for (const call of getProcessAttemptOutput.mock.calls) {
        expect(call[0]).toBe('run-1')
        expect(call[1]).toBe('attempt-1')
      }

      expect(await screen.findAllByText('build succeeded')).toHaveLength(2)
    })

    it('deterministically selects the latest eligible event when a run has more than one process attempt', async () => {
      const getProcessAttemptOutput = vi
        .fn()
        .mockResolvedValue({ status: 'Ok', text: '', nextOffset: 0, totalLengthSoFar: 0, isFinal: true, truncated: false })
      mockProcessOutputClient(getProcessAttemptOutput)

      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [
          processOutputCard({ sequence: 3, attemptId: 'attempt-2' }),
          processOutputCard({ sequence: 1, attemptId: 'attempt-1' }),
        ],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      render(<RunCockpitView runId="run-1" />)

      await waitFor(() => expect(getProcessAttemptOutput).toHaveBeenCalledTimes(2))

      for (const call of getProcessAttemptOutput.mock.calls) {
        expect(call[1]).toBe('attempt-2')
      }
    })

    it('never copies captured output text into the URL, browser storage, or the event cards themselves', async () => {
      localStorage.clear()
      sessionStorage.clear()
      document.cookie = ''

      const sentinel = 'SENTINEL-run-cockpit-do-not-persist-me'
      const getProcessAttemptOutput = vi.fn().mockResolvedValue({
        status: 'Ok',
        text: sentinel,
        nextOffset: sentinel.length,
        totalLengthSoFar: sentinel.length,
        isFinal: true,
        truncated: false,
      })
      mockProcessOutputClient(getProcessAttemptOutput)

      const card = processOutputCard({ sequence: 1, attemptId: 'attempt-1' })
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [card],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      render(<RunCockpitView runId="run-1" />)

      expect(await screen.findAllByText(sentinel)).toHaveLength(2)

      // The card object handed to AgentCollaboration is exactly the fixture above — reading
      // captured output never mutates it or any other event-card state.
      expect(card.summary).toBe('Captured process output.')
      expect(JSON.stringify(card)).not.toContain(sentinel)

      expect(Object.keys(localStorage)).toHaveLength(0)
      expect(Object.keys(sessionStorage)).toHaveLength(0)
      expect(document.cookie).not.toContain(sentinel)
      expect(window.location.href).not.toContain(sentinel)
    })
  })
})
