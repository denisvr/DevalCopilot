import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { processAttemptOutputClient } from '../../../api/clients'
import {
  AgentAttemptStatusResponse,
  AgentProcessExecutionResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  ChallengeResolutionAttemptStatusResponse,
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  RunCockpitAgentAttemptResponse,
} from '../../../api/generated/api-client'
import * as useRunCockpitModule from '../hooks/useRunCockpit'
import * as useCollaborationTimelineModule from '../hooks/useCollaborationTimeline'
import * as useAgentAttemptStatusModule from '../hooks/useAgentAttemptStatus'
import * as useRequestCodexPlanningAttemptModule from '../hooks/useRequestCodexPlanningAttempt'
import * as useClaudeCriticalReviewAttemptStatusModule from '../hooks/useClaudeCriticalReviewAttemptStatus'
import * as useRequestClaudeCriticalReviewModule from '../hooks/useRequestClaudeCriticalReview'
import * as useChallengeResolutionAttemptStatusModule from '../hooks/useChallengeResolutionAttemptStatus'
import * as useRequestChallengeResolutionModule from '../hooks/useRequestChallengeResolution'
import * as useImplementationAttemptStatusModule from '../hooks/useImplementationAttemptStatus'
import * as useRequestImplementationModule from '../hooks/useRequestImplementation'
import * as useCodeReviewAttemptStatusModule from '../hooks/useCodeReviewAttemptStatus'
import * as useRequestCodeReviewModule from '../hooks/useRequestCodeReview'
import * as useReviewCorrectionAttemptStatusModule from '../hooks/useReviewCorrectionAttemptStatus'
import * as useRequestReviewCorrectionModule from '../hooks/useRequestReviewCorrection'
import type { CollaborationCard, CollaborationTimelineCard } from '../types'
import { RunCockpitView } from './RunCockpitView'

vi.mock('../hooks/useRunCockpit')
vi.mock('../hooks/useCollaborationTimeline')
vi.mock('../hooks/useAgentAttemptStatus')
vi.mock('../hooks/useRequestCodexPlanningAttempt')
vi.mock('../hooks/useClaudeCriticalReviewAttemptStatus')
vi.mock('../hooks/useRequestClaudeCriticalReview')
vi.mock('../hooks/useChallengeResolutionAttemptStatus')
vi.mock('../hooks/useRequestChallengeResolution')
vi.mock('../hooks/useImplementationAttemptStatus')
vi.mock('../hooks/useRequestImplementation')
vi.mock('../hooks/useCodeReviewAttemptStatus')
vi.mock('../hooks/useRequestCodeReview')
vi.mock('../hooks/useReviewCorrectionAttemptStatus')
vi.mock('../hooks/useRequestReviewCorrection')
vi.mock('../../../api/clients', () => ({
  processAttemptOutputClient: vi.fn(),
  reviewCorrectionAttemptStatusClient: vi.fn(),
}))

const useRunCockpitMock = vi.mocked(useRunCockpitModule.useRunCockpit)
const useCollaborationTimelineMock = vi.mocked(useCollaborationTimelineModule.useCollaborationTimeline)
const useAgentAttemptStatusMock = vi.mocked(useAgentAttemptStatusModule.useAgentAttemptStatus)
const useRequestCodexPlanningAttemptMock = vi.mocked(useRequestCodexPlanningAttemptModule.useRequestCodexPlanningAttempt)
const useClaudeCriticalReviewAttemptStatusMock = vi.mocked(
  useClaudeCriticalReviewAttemptStatusModule.useClaudeCriticalReviewAttemptStatus,
)
const useRequestClaudeCriticalReviewMock = vi.mocked(useRequestClaudeCriticalReviewModule.useRequestClaudeCriticalReview)
const useChallengeResolutionAttemptStatusMock = vi.mocked(
  useChallengeResolutionAttemptStatusModule.useChallengeResolutionAttemptStatus,
)
const useRequestChallengeResolutionMock = vi.mocked(useRequestChallengeResolutionModule.useRequestChallengeResolution)
const useImplementationAttemptStatusMock = vi.mocked(useImplementationAttemptStatusModule.useImplementationAttemptStatus)
const useRequestImplementationMock = vi.mocked(useRequestImplementationModule.useRequestImplementation)
const useCodeReviewAttemptStatusMock = vi.mocked(useCodeReviewAttemptStatusModule.useCodeReviewAttemptStatus)
const useRequestCodeReviewMock = vi.mocked(useRequestCodeReviewModule.useRequestCodeReview)
const useReviewCorrectionAttemptStatusMock = vi.mocked(
  useReviewCorrectionAttemptStatusModule.useReviewCorrectionAttemptStatus,
)
const useRequestReviewCorrectionMock = vi.mocked(useRequestReviewCorrectionModule.useRequestReviewCorrection)

function providerObservedCodexProposal(overrides: Partial<CollaborationTimelineCard> = {}): CollaborationTimelineCard {
  return {
    sequence: 1,
    id: 'message-1',
    attemptId: null,
    actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
    recipient: { kind: 'Agent', role: null, provider: 'ClaudeCode' },
    type: 'Proposal',
    inReplyToMessageId: null,
    summary: 'A bounded proposal',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-09-16T12:00:00Z',
    ...overrides,
  }
}

beforeEach(() => {
  useCollaborationTimelineMock.mockReturnValue({
    cards: [],
    loading: false,
    error: null,
    hasSuccessfulResponse: true,
  })
  useAgentAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestCodexPlanningAttemptMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestClaudeCriticalReviewMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useChallengeResolutionAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestChallengeResolutionMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useImplementationAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestImplementationMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useCodeReviewAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestCodeReviewMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useReviewCorrectionAttemptStatusMock.mockReturnValue({
    status: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  useRequestReviewCorrectionMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
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
  activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
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
    actor: { kind: 'Orchestrator', role: null, provider: null },
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
        activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
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
        activeParticipant: new ParticipantIdentityResponse({ kind: 'None' }),
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
        activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
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
        activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
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

  describe('Codex planning wiring', () => {
    it('requests a Codex plan for the current run when the action is used', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      const request = vi.fn()
      useRequestCodexPlanningAttemptMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request Codex plan' }))

      expect(request).toHaveBeenCalledWith('run-1')
    })

    it('shows a visibly-working state while the attempt is running, without implying a review happened', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useAgentAttemptStatusMock.mockReturnValue({
        status: new AgentAttemptStatusResponse({ attemptId: 'attempt-1', attemptNumber: 1, status: 'Running' }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText(/pending/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
    })

    it('surfaces a safe conflict message from a failed request without losing the rest of the cockpit', async () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      const request = vi.fn().mockResolvedValue(false)
      useRequestCodexPlanningAttemptMock.mockReturnValue({
        requesting: false,
        error: 'This run already has a Codex planning attempt in progress.',
        request,
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText('This run already has a Codex planning attempt in progress.')).toBeInTheDocument()
      expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
    })

    it('re-reads status for the newly selected run when the cockpit switches runs', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useAgentAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useAgentAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })

  describe('Claude critical review wiring', () => {
    it('withholds the request action until a real Codex Proposal exists for this run', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useCollaborationTimelineMock.mockReturnValue({
        cards: [],
        loading: false,
        error: null,
        hasSuccessfulResponse: true,
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
    })

    it('requests a Claude critical review of the latest real Codex Proposal when the action is used', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useCollaborationTimelineMock.mockReturnValue({
        cards: [providerObservedCodexProposal({ sequence: 1, id: 'message-1' })],
        loading: false,
        error: null,
        hasSuccessfulResponse: true,
      })
      const request = vi.fn()
      useRequestClaudeCriticalReviewMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request Claude review' }))

      expect(request).toHaveBeenCalledWith('run-1', 'message-1')
    })

    it('shows a visibly-working state while the review is running, without implying it already reached a verdict', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useCollaborationTimelineMock.mockReturnValue({
        cards: [providerObservedCodexProposal({ sequence: 1, id: 'message-1' })],
        loading: false,
        error: null,
        hasSuccessfulResponse: true,
      })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: new ClaudeCriticalReviewAttemptStatusResponse({
          attemptId: 'attempt-1',
          attemptNumber: 1,
          status: 'Running',
          reviewedProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText(/pending/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
    })

    it('surfaces a safe conflict message from a failed request without losing the rest of the cockpit', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useCollaborationTimelineMock.mockReturnValue({
        cards: [providerObservedCodexProposal({ sequence: 1, id: 'message-1' })],
        loading: false,
        error: null,
        hasSuccessfulResponse: true,
      })
      useRequestClaudeCriticalReviewMock.mockReturnValue({
        requesting: false,
        error: 'This run already has a Claude critical-review attempt in progress.',
        request: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText('This run already has a Claude critical-review attempt in progress.')).toBeInTheDocument()
      expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
    })

    it('re-reads status for the newly selected run when the cockpit switches runs', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useClaudeCriticalReviewAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useClaudeCriticalReviewAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })

  describe('Challenge resolution wiring', () => {
    it('withholds the request action until the latest Claude critical review is Challenged', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: new ClaudeCriticalReviewAttemptStatusResponse({
          attemptId: 'review-1',
          attemptNumber: 1,
          status: 'Completed',
          outcome: 'Accepted',
          reviewedProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
    })

    it('requests a resolution of the latest Challenged review when the action is used', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: new ClaudeCriticalReviewAttemptStatusResponse({
          attemptId: 'review-1',
          attemptNumber: 1,
          status: 'Completed',
          outcome: 'Challenged',
          reviewedProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      const request = vi.fn()
      useRequestChallengeResolutionMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Resolve challenges with Codex' }))

      expect(request).toHaveBeenCalledWith('run-1', 'review-1')
    })

    it('shows a visibly-working state while the resolution is running', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: new ClaudeCriticalReviewAttemptStatusResponse({
          attemptId: 'review-1',
          attemptNumber: 1,
          status: 'Completed',
          outcome: 'Challenged',
          reviewedProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      useChallengeResolutionAttemptStatusMock.mockReturnValue({
        status: new ChallengeResolutionAttemptStatusResponse({
          attemptId: 'attempt-1',
          attemptNumber: 3,
          status: 'Running',
          originalProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText(/pending/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
    })

    it('hides the action once the exact challenged review has already been resolved', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: new ClaudeCriticalReviewAttemptStatusResponse({
          attemptId: 'review-1',
          attemptNumber: 1,
          status: 'Completed',
          outcome: 'Challenged',
          reviewedProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      useChallengeResolutionAttemptStatusMock.mockReturnValue({
        status: new ChallengeResolutionAttemptStatusResponse({
          attemptId: 'attempt-1',
          attemptNumber: 3,
          status: 'Completed',
          outcome: 'Resolved',
          originalProposalMessageId: 'message-1',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
      expect(screen.getByText('Last attempt #3: Challenges resolved.')).toBeInTheDocument()
    })

    it('re-reads status for the newly selected run when the cockpit switches runs', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useChallengeResolutionAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useChallengeResolutionAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })
})

describe('RunCockpitView process evidence', () => {
  const cockpitWithLatestAttempt = new GetRunCockpitResponse({
    ...runningCockpit,
    latestAgentAttempt: new RunCockpitAgentAttemptResponse({
      attemptId: 'attempt-2',
      attemptNumber: 2,
      role: 'CriticalReviewer',
      provider: 'ClaudeCode',
      status: 'Failed',
      outcome: 'ProviderInvocationFailed',
      dispatchedAtUtc: new Date('2026-09-24T10:00:00Z'),
      processExecution: new AgentProcessExecutionResponse({
        outcome: 'TimedOut',
        durationMilliseconds: 600_123,
        timeoutMilliseconds: 600_000,
      }),
    }),
  })

  it('renders the latest agent attempt semantic result and its process evidence as separate facts', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: cockpitWithLatestAttempt,
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    const section = screen.getByRole('region', { name: 'Latest agent attempt' })
    expect(section).toHaveTextContent(
      'Latest agent attempt #2 · Critical reviewer · Claude Code · Result: ProviderInvocationFailed',
    )
    expect(section).toHaveTextContent('Process timed out after 10m 00s · timeout 10m 00s')
  })

  it('never renders a stale cockpit projection from the previously selected run as evidence for the new run', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: cockpitWithLatestAttempt,
      cards: [],
      connection: 'live',
      loading: true,
      error: null,
      syncError: null,
    })

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.getByRole('region', { name: 'Latest agent attempt' })).toBeInTheDocument()

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByRole('region', { name: 'Latest agent attempt' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Process timed out/)).not.toBeInTheDocument()
  })

  it('renders nothing for a run whose cockpit has no agent attempt yet', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: runningCockpit,
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.queryByRole('region', { name: 'Latest agent attempt' })).not.toBeInTheDocument()
  })
})
