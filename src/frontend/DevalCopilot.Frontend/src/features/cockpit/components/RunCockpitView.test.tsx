import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { planningImplementationAuthorizationClient, processAttemptOutputClient, requestCodeReviewClient, requestImplementationClient, requestCodeReviewRepairAttemptClient, setClaudeModelPreferenceClient, setClaudeMutationTurnLimitClient } from '../../../api/clients'
import {
  AgentAttemptStatusResponse,
  AgentClaimPathTimeFitResponse,
  AgentInvocationTimeBudgetResponse,
  AgentProcessExecutionResponse,
  AgentTokenUsageResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  ChallengeResolutionAttemptStatusResponse,
  CodeReviewAttemptStatusResponse,
  ClaudeMutationTurnLimitResponse,
  GetRunCockpitResponse,
  PlanningImplementationAuthorizationResponse,
  ParticipantIdentityResponse,
  ReviewCorrectionAttemptStatusResponse,
  RunCockpitAgentAttemptResponse,
  RunCockpitTokenStopResponse,
  RunCockpitTokenWarningResponse,
  RunCockpitProviderTokenUsageEntryResponse,
  RunTokenUsageSummaryResponse,
} from '../../../api/generated/api-client'
import * as useRunCockpitModule from '../hooks/useRunCockpit'
import * as useCollaborationTimelineModule from '../hooks/useCollaborationTimeline'
import * as useAgentAttemptStatusModule from '../hooks/useAgentAttemptStatus'
import * as useRequestCodexPlanningAttemptModule from '../hooks/useRequestCodexPlanningAttempt'
import * as useRequestCodexPlanningRepairAttemptModule from '../hooks/useRequestCodexPlanningRepairAttempt'
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
import * as useAuthorizeReviewCorrectionModule from '../hooks/useAuthorizeReviewCorrection'
import * as usePlanningImplementationAuthorizationModule from '../hooks/usePlanningImplementationAuthorization'
import * as useAuthorizePlanningImplementationModule from '../hooks/useAuthorizePlanningImplementation'
import * as useRequestClaudeCriticalReviewRepairAttemptModule from '../hooks/useRequestClaudeCriticalReviewRepairAttempt'
import * as useRequestChallengeResolutionRepairAttemptModule from '../hooks/useRequestChallengeResolutionRepairAttempt'
import * as useRequestCodeReviewRepairAttemptModule from '../hooks/useRequestCodeReviewRepairAttempt'
import type { CollaborationCard, CollaborationTimelineCard } from '../types'
import { RunCockpitView } from './RunCockpitView'

vi.mock('../hooks/useRunCockpit')
vi.mock('../hooks/useCollaborationTimeline')
vi.mock('../hooks/useAgentAttemptStatus')
vi.mock('../hooks/useRequestCodexPlanningAttempt')
vi.mock('../hooks/useRequestCodexPlanningRepairAttempt')
vi.mock('../hooks/useRequestCodeReviewRepairAttempt')
vi.mock('../hooks/useRequestChallengeResolutionRepairAttempt')
vi.mock('../hooks/useRequestClaudeCriticalReviewRepairAttempt')
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
vi.mock('../hooks/useAuthorizeReviewCorrection')
vi.mock('../hooks/usePlanningImplementationAuthorization')
vi.mock('../hooks/useAuthorizePlanningImplementation')
vi.mock('../../../api/clients', () => ({
  processAttemptOutputClient: vi.fn(),
  planningImplementationAuthorizationClient: vi.fn(),
  setClaudeModelPreferenceClient: vi.fn(),
  setClaudeMutationTurnLimitClient: vi.fn(),
  requestCodeReviewClient: vi.fn(),
  requestImplementationClient: vi.fn(),
  requestCodeReviewRepairAttemptClient: vi.fn(),
  reviewCorrectionAttemptStatusClient: vi.fn(),
  codexAccountAllowanceClient: vi.fn(() => ({
    getCodexAccountAllowance: vi.fn().mockResolvedValue({ status: 'Unknown' }),
  })),
  codexModelCatalogClient: vi.fn(() => ({
    getCodexModelCatalog: vi.fn().mockResolvedValue({ status: 'Unknown' }),
  })),
}))

const useRunCockpitMock = vi.mocked(useRunCockpitModule.useRunCockpit)
const useCollaborationTimelineMock = vi.mocked(useCollaborationTimelineModule.useCollaborationTimeline)
const useAgentAttemptStatusMock = vi.mocked(useAgentAttemptStatusModule.useAgentAttemptStatus)
const useRequestCodexPlanningAttemptMock = vi.mocked(useRequestCodexPlanningAttemptModule.useRequestCodexPlanningAttempt)
const useRequestCodexPlanningRepairAttemptMock = vi.mocked(
  useRequestCodexPlanningRepairAttemptModule.useRequestCodexPlanningRepairAttempt,
)
const useRequestClaudeCriticalReviewRepairAttemptMock = vi.mocked(
  useRequestClaudeCriticalReviewRepairAttemptModule.useRequestClaudeCriticalReviewRepairAttempt,
)
const useRequestChallengeResolutionRepairAttemptMock = vi.mocked(
  useRequestChallengeResolutionRepairAttemptModule.useRequestChallengeResolutionRepairAttempt,
)
const useRequestCodeReviewRepairAttemptMock = vi.mocked(
  useRequestCodeReviewRepairAttemptModule.useRequestCodeReviewRepairAttempt,
)
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
const useAuthorizeReviewCorrectionMock = vi.mocked(useAuthorizeReviewCorrectionModule.useAuthorizeReviewCorrection)
const usePlanningImplementationAuthorizationMock = vi.mocked(
  usePlanningImplementationAuthorizationModule.usePlanningImplementationAuthorization,
)
const useAuthorizePlanningImplementationMock = vi.mocked(
  useAuthorizePlanningImplementationModule.useAuthorizePlanningImplementation,
)

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
  useRequestCodexPlanningRepairAttemptMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useRequestClaudeCriticalReviewRepairAttemptMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useRequestChallengeResolutionRepairAttemptMock.mockReturnValue({
    requesting: false,
    error: null,
    request: vi.fn(),
  })
  useRequestCodeReviewRepairAttemptMock.mockReturnValue({
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
  useAuthorizeReviewCorrectionMock.mockReturnValue({
    authorizing: false,
    error: null,
    authorize: vi.fn().mockResolvedValue(true),
  })
  usePlanningImplementationAuthorizationMock.mockReturnValue({ authorization: null, loading: false, error: null, refresh: vi.fn() })
  useAuthorizePlanningImplementationMock.mockReturnValue({
    authorizing: false,
    error: null,
    authorize: vi.fn().mockResolvedValue(true),
  })
})

function mockProcessOutputClient(getProcessAttemptOutput: ReturnType<typeof vi.fn>) {
  vi.mocked(processAttemptOutputClient).mockReturnValue({
    getProcessAttemptOutput,
  } as unknown as ReturnType<typeof processAttemptOutputClient>)
}

// A healthy, non-exhausted budget state shared by every fixture below, so tests exercising
// role-specific wiring are never incidentally blocked by `deriveGlobalAgentClaimBlock`
// (see its own dedicated test file for the budget-blocking scenarios themselves).
const healthyAgentClaimBudget = {
  maximumAgentAttempts: 16,
  agentAttemptsUsed: 1,
  agentBudgetExhausted: false,
  agentInvocationTimeBudget: new AgentInvocationTimeBudgetResponse({
    maximumMilliseconds: 7_200_000,
    reservedMilliseconds: 600_000,
    remainingMilliseconds: 6_600_000,
    isLegacyUnknown: false,
    evidenceInvalid: false,
  }),
  // Every claim path fits comfortably within this healthy budget, so tests exercising
  // role-specific wiring are never incidentally blocked by `deriveAgentClaimPathTimeFit`
  // either (see its own dedicated test file for the fit/no-fit scenarios themselves).
  agentClaimPathTimeFits: [
    new AgentClaimPathTimeFitResponse({ claimPath: 'CodexPlanning', fit: 'Fits' }),
    new AgentClaimPathTimeFitResponse({ claimPath: 'ClaudeCriticalReview', fit: 'Fits' }),
    new AgentClaimPathTimeFitResponse({ claimPath: 'ChallengeResolution', fit: 'Fits' }),
    new AgentClaimPathTimeFitResponse({ claimPath: 'Implementation', fit: 'Fits' }),
    new AgentClaimPathTimeFitResponse({ claimPath: 'CodeReview', fit: 'Fits' }),
    new AgentClaimPathTimeFitResponse({ claimPath: 'ReviewCorrection', fit: 'Fits' }),
  ],
}

const runningCockpit = new GetRunCockpitResponse({
  runId: 'run-1',
  executionMode: 'ManualAgent',
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
  ...healthyAgentClaimBudget,
} as ConstructorParameters<typeof GetRunCockpitResponse>[0])

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
      refresh: async () => true,
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
      refresh: async () => true,
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
      refresh: async () => true,
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
        ...healthyAgentClaimBudget,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
      cards: [],
      connection: 'disconnected',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
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
        ...healthyAgentClaimBudget,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
      cards: [],
      connection: 'disconnected',
      loading: false,
      error: null,
      syncError: 'cockpit query unavailable',
      refresh: async () => true,
    })

    render(<RunCockpitView runId="run-1" />)

    // The specific sync-error reason is surfaced, not just the generic disconnected text —
    // and the cockpit already on screen is untouched by it.
    expect(screen.getByRole('status')).toHaveTextContent('cockpit query unavailable')
    expect(screen.getByText('Prove the walking skeleton')).toBeInTheDocument()
  })

  it('never carries a previously selected run\'s global claim block into the newly selected run, even transiently', () => {
    const exhaustedRunOne = new GetRunCockpitResponse({
      runId: 'run-1',
      executionMode: 'ManualAgent',
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
      maximumAgentAttempts: 16,
      agentAttemptsUsed: 16,
      agentBudgetExhausted: true,
      agentInvocationTimeBudget: healthyAgentClaimBudget.agentInvocationTimeBudget,
    } as ConstructorParameters<typeof GetRunCockpitResponse>[0])

    useRunCockpitMock.mockReturnValue({
      cockpit: exhaustedRunOne,
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
    })

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
    expect(screen.getByText(/run-wide agent claim budget/i)).toBeInTheDocument()

    // The user selects a different run, but the cockpit hook has not yet caught up with a
    // projection for it (still returning the previous run's own projection, by identity). The
    // newly selected run must never inherit run-1's exhausted state, even for this one frame:
    // it must show the honest "unavailable" state instead, since no coherent data for run-2 has
    // arrived yet.
    rerender(<RunCockpitView runId="run-2" />)
    expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
    expect(screen.queryByText(/run-wide agent claim budget/i)).not.toBeInTheDocument()
    expect(screen.getByText(/budget status is confirmed/i)).toBeInTheDocument()

    // Once the hook catches up with run-2's own healthy projection, the action reflects it
    // immediately.
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        ...exhaustedRunOne,
        runId: 'run-2',
        ...healthyAgentClaimBudget,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
    })
    rerender(<RunCockpitView runId="run-2" />)
    expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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

  describe('Review correction guidance wiring', () => {
    function withExhaustedEscalation() {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
      useCodeReviewAttemptStatusMock.mockReturnValue({
        status: { hasAttempt: true, attemptId: 'review-1', outcome: 'ReviewChangesRequested' } as never,
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      useReviewCorrectionAttemptStatusMock.mockReturnValue({
        status: new ReviewCorrectionAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'correction-1',
          attemptNumber: 2,
          implementationReviewAttemptId: 'review-1',
          status: 'Failed',
          budgetExhausted: true,
          escalationId: 'escalation-1',
          hasAvailableHumanAuthorization: false,
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
    }

    it('sends the guidance with the current run and escalation, and the plain button stays bodyless', () => {
      withExhaustedEscalation()
      const authorize = vi.fn().mockResolvedValue(true)
      useAuthorizeReviewCorrectionMock.mockReturnValue({ authorizing: false, error: null, authorize })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Authorize one additional correction' }))
      expect(authorize).toHaveBeenLastCalledWith('run-1', 'escalation-1')

      fireEvent.change(screen.getByLabelText('Optional guidance for the correction'), { target: { value: 'Keep it small.' } })
      fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
      expect(authorize).toHaveBeenLastCalledWith('run-1', 'escalation-1', 'Keep it small.')
    })

    it('binds the authorization hook to the selected run', () => {
      withExhaustedEscalation()

      render(<RunCockpitView runId="run-1" />)

      expect(useAuthorizeReviewCorrectionMock).toHaveBeenCalledWith('run-1', expect.any(Function))
    })
  })

  describe('Codex planning repair wiring', () => {
    function withInvalidLatestAttempt(overrides: Partial<ConstructorParameters<typeof AgentAttemptStatusResponse>[0]> = {}) {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
      useAgentAttemptStatusMock.mockReturnValue({
        status: new AgentAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'attempt-9',
          attemptNumber: 3,
          status: 'Failed',
          outcome: 'InvalidStructuredOutput',
          ...overrides,
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
    }

    it('requests the repair for the current run and the displayed attempt, beside the ordinary action', () => {
      withInvalidLatestAttempt()
      const request = vi.fn()
      useRequestCodexPlanningRepairAttemptMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request one format-repair plan' }))

      expect(request).toHaveBeenCalledWith('attempt-9')
      expect(useRequestCodexPlanningRepairAttemptMock).toHaveBeenCalledWith('run-1', expect.any(Function))
      expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeEnabled()
    })

    it('binds the repair hook to the newly selected run and offers no repair for a stale status', () => {
      withInvalidLatestAttempt()
      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(screen.getByRole('button', { name: 'Request one format-repair plan' })).toBeInTheDocument()

      // The status hook masks another run's status to null during a switch (its own contract).
      useAgentAttemptStatusMock.mockReturnValue({ status: null, loading: true, error: null, refresh: vi.fn() })
      rerender(<RunCockpitView runId="run-2" />)

      expect(useRequestCodexPlanningRepairAttemptMock).toHaveBeenLastCalledWith('run-2', expect.any(Function))
      expect(screen.queryByRole('button', { name: 'Request one format-repair plan' })).not.toBeInTheDocument()
    })

    it('shows a safe repair error while the ordinary action stays available', () => {
      withInvalidLatestAttempt()
      useRequestCodexPlanningRepairAttemptMock.mockReturnValue({
        requesting: false,
        error: 'A repair was already requested for this attempt.',
        request: vi.fn(),
      })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByText('A repair was already requested for this attempt.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useAgentAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useAgentAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })

  it('exposes typed findings and revision responses through the real cockpit collaboration surface', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: runningCockpit,
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
    })
    useCollaborationTimelineMock.mockReturnValue({
      cards: [
        providerObservedCodexProposal({
          id: 'finding-1',
          type: 'ReviewFinding',
          summary: 'The input guard is missing.',
          structuredContentJson: '{"severity":"high","category":"correctness","evidence":"The input is unchecked.","requiredChange":"Add the guard."}',
        }),
        providerObservedCodexProposal({
          sequence: 2,
          id: 'response-1',
          type: 'RevisionResponse',
          summary: 'The guard was added.',
          inReplyToMessageId: 'finding-1',
          structuredContentJson: '{"disposition":"Fixed","evidence":"The guard rejects invalid input.","resultingSourceChanges":"Added the guard."}',
        }),
      ],
      loading: false,
      error: null,
      hasSuccessfulResponse: true,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByText('The input guard is missing.')).toBeInTheDocument()
    expect(screen.getByText('The guard was added.')).toBeInTheDocument()
    expect(screen.getByText('Required change')).toBeInTheDocument()
    expect(screen.getByText('In reply to Review finding message finding-1: The input guard is missing.')).toBeInTheDocument()
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useClaudeCriticalReviewAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useClaudeCriticalReviewAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })

  describe('Second challenge round wiring', () => {
    const resolver = { kind: 'Agent', role: 'Resolver', provider: 'Codex' }
    const rootCard = providerObservedCodexProposal({ sequence: 1, id: 'root' })
    const firstRevision = providerObservedCodexProposal({
      sequence: 10,
      id: 'first',
      actor: resolver,
      inReplyToMessageId: 'root',
    })
    const secondRevision = providerObservedCodexProposal({
      sequence: 20,
      id: 'second',
      actor: resolver,
      inReplyToMessageId: 'first',
    })
    const escalationCard = providerObservedCodexProposal({
      sequence: 21,
      id: 'escalation',
      type: 'Escalation',
      actor: { kind: 'Orchestrator', role: null, provider: null },
      provenance: 'HostConstructed',
      inReplyToMessageId: 'second',
      summary: 'The second challenge-resolution round is complete and needs a human decision.',
    })

    function arrange(
      cards: CollaborationTimelineCard[],
      review: { outcome?: string; status?: string; reviewed?: string; loading?: boolean; error?: string } | null = null,
      resolution: { outcome: string; original: string } | null = null,
    ) {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
      useCollaborationTimelineMock.mockReturnValue({ cards, loading: false, error: null, hasSuccessfulResponse: true })
      useClaudeCriticalReviewAttemptStatusMock.mockReturnValue({
        status: review?.reviewed
          ? new ClaudeCriticalReviewAttemptStatusResponse({
              attemptId: 'review-x',
              attemptNumber: 2,
              status: review.status ?? 'Completed',
              outcome: review.outcome,
              reviewedProposalMessageId: review.reviewed,
            })
          : null,
        loading: review?.loading ?? false,
        error: review?.error ?? null,
        refresh: vi.fn(),
      })
      useChallengeResolutionAttemptStatusMock.mockReturnValue({
        status: resolution
          ? new ChallengeResolutionAttemptStatusResponse({
              attemptId: 'resolution-x',
              attemptNumber: 3,
              status: 'Completed',
              outcome: resolution.outcome,
              originalProposalMessageId: resolution.original,
            })
          : null,
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
    }

    it('selects the first valid revised proposal for the optional second review', () => {
      arrange([rootCard, firstRevision], { reviewed: 'root', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'root' })
      const request = vi.fn()
      useRequestClaudeCriticalReviewMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request Claude review of the revised proposal' }))

      expect(request).toHaveBeenCalledWith('run-1', 'first')
      expect(screen.queryByRole('button', { name: 'Request Claude review' })).not.toBeInTheDocument()
    })

    it('offers the direct implementation of a first revision that has no second review', () => {
      arrange([rootCard, firstRevision], { reviewed: 'root', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'root' })
      const request = vi.fn()
      useRequestImplementationMock.mockReturnValue({ requesting: false, error: null, request })

      render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' }))

      expect(request).toHaveBeenCalledWith('run-1', 'first', undefined)
    })

    it('keeps the Implementer request isolated per run while an older run completes late (real hook)', async () => {
      const realImplementation = await vi.importActual<typeof useRequestImplementationModule>(
        '../hooks/useRequestImplementation',
      )
      useRequestImplementationMock.mockImplementation(realImplementation.useRequestImplementation)
      arrange([rootCard, firstRevision], { reviewed: 'root', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'root' })
      useRunCockpitMock.mockImplementation((id: string | null) => ({
        cockpit: new GetRunCockpitResponse({ ...runningCockpit, runId: id ?? undefined }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      }))
      let finishA!: () => void
      let finishB!: () => void
      const requestImplementation = vi
        .fn()
        .mockReturnValueOnce(new Promise<void>((resolve) => (finishA = resolve)))
        .mockReturnValueOnce(new Promise<void>((resolve) => (finishB = resolve)))
      vi.mocked(requestImplementationClient).mockReturnValue({ requestImplementation } as unknown as ReturnType<
        typeof requestImplementationClient
      >)
      const implement = () => screen.getByRole('button', { name: /Implement the resolved plan with Claude|^Requesting…$/ })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      fireEvent.click(implement())
      expect(implement()).toBeDisabled()

      rerender(<RunCockpitView runId="run-2" />)
      expect(implement()).toBeEnabled()
      fireEvent.click(implement())
      expect(implement()).toBeDisabled()
      expect(requestImplementation).toHaveBeenCalledTimes(2)
      expect(requestImplementation.mock.calls.map((call) => call[0])).toEqual(['run-1', 'run-2'])

      await act(async () => {
        finishA()
      })
      expect(implement()).toBeDisabled()

      await act(async () => {
        finishB()
      })
      await waitFor(() => expect(implement()).toBeEnabled())
    })

    it('offers the implementation of a first revision after its own second review was Accepted', () => {
      arrange([rootCard, firstRevision], { reviewed: 'first', outcome: 'Accepted' }, { outcome: 'Resolved', original: 'root' })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' })).toBeInTheDocument()
      expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/accepted the revised proposal/)
    })

    it('withholds the implementation of a first revision whose second review Challenged and offers the last resolution', () => {
      arrange([rootCard, firstRevision], { reviewed: 'first', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'root' })

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Request Claude review of the revised proposal' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Resolve challenges with Codex' })).toBeInTheDocument()
      expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/implementing it is blocked/)
    })

    it('withholds review, resolution, and implementation once the lineage is exhausted and shows the escalation truthfully', () => {
      arrange(
        [rootCard, firstRevision, secondRevision, escalationCard],
        { reviewed: 'first', outcome: 'Challenged' },
        { outcome: 'Resolved', original: 'first' },
      )

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: /Request Claude review/ })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
      expect(screen.getByLabelText('Proposal lineage')).toHaveTextContent(/cannot be implemented through this lineage/)
      expect(screen.getByText(/Human decision required: The second challenge-resolution round is complete/)).toBeInTheDocument()
    })

    it('withholds a first revision implementation while its review status is loading or failed, showing the error', () => {
      arrange([rootCard, firstRevision], { loading: true })
      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()

      arrange([rootCard, firstRevision], { error: 'Claude critical review attempt status is unavailable.' })
      rerender(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
      expect(screen.getByText('Claude critical review attempt status is unavailable.')).toBeInTheDocument()
    })

    it("never carries the previous run's lineage into a newly selected run without a revision", () => {
      arrange([rootCard, firstRevision, secondRevision, escalationCard], { reviewed: 'first', outcome: 'Challenged' })
      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(screen.getByLabelText('Proposal lineage')).toBeInTheDocument()

      // The newly selected run's hooks report only its own data: one unreviewed root proposal.
      arrange([providerObservedCodexProposal({ sequence: 1, id: 'other-root' })])
      useRunCockpitMock.mockReturnValue({
        cockpit: new GetRunCockpitResponse({ ...runningCockpit, runId: 'run-2' } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
      const request = vi.fn()
      useRequestClaudeCriticalReviewMock.mockReturnValue({ requesting: false, error: null, request })
      rerender(<RunCockpitView runId="run-2" />)

      expect(screen.queryByLabelText('Proposal lineage')).not.toBeInTheDocument()
      fireEvent.click(screen.getByRole('button', { name: 'Request Claude review' }))
      expect(request).toHaveBeenCalledWith('run-2', 'other-root')
    })

    describe('Human authorization of the final plan', () => {
      const exhausted = [rootCard, firstRevision, secondRevision, escalationCard]
      const authorizedLabel = 'Implement the human-authorized final plan with Claude'

      function arrangeFacts(state: string | null, overrides: Partial<ConstructorParameters<typeof PlanningImplementationAuthorizationResponse>[0]> = {}) {
        arrange(exhausted, { reviewed: 'first', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'first' })
        const authorize = vi.fn().mockResolvedValue(true)
        const refresh = vi.fn()
        usePlanningImplementationAuthorizationMock.mockReturnValue({
          authorization:
            state === null
              ? null
              : new PlanningImplementationAuthorizationResponse({
                  runId: 'run-1',
                  escalationMessageId: 'escalation',
                  state,
                  finalProposalMessageId: 'second',
                  orderedDecisionMessageIds: ['decision-1', 'decision-2'],
                  authorizationId: state === 'Absent' ? undefined : 'authorization-1',
                  rationale: state === 'Absent' ? undefined : 'Reviewed both rounds.',
                  ...overrides,
                }),
          loading: false,
          error: null,
          refresh,
        })
        useAuthorizePlanningImplementationMock.mockReturnValue({ authorizing: false, error: null, authorize })
        return { authorize, refresh }
      }

      it('binds the hooks to the loaded escalation and offers nothing before the facts are read', () => {
        arrangeFacts(null)

        render(<RunCockpitView runId="run-1" />)

        expect(usePlanningImplementationAuthorizationMock).toHaveBeenLastCalledWith('run-1', 'escalation', runningCockpit.latestSequence)
        expect(useAuthorizePlanningImplementationMock).toHaveBeenLastCalledWith('run-1', 'escalation', expect.any(Function))
        expect(screen.getByLabelText('Human decision on the final plan')).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
      })

      it('shows the decision form for an absent authorization and sends the reason for exactly that escalation', () => {
        const { authorize } = arrangeFacts('Absent')

        render(<RunCockpitView runId="run-1" />)

        const region = screen.getByLabelText('Human decision on the final plan')
        expect(region).toHaveTextContent(/exactly one implementation claim/)
        expect(region).toHaveTextContent(/does not start an implementation/)
        expect(region).toHaveTextContent(/2 second-round decisions/)
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        fireEvent.change(screen.getByLabelText(/Your reason for authorizing this final plan/), { target: { value: 'I accept it.' } })
        fireEvent.click(screen.getByRole('button', { name: 'Authorize one implementation claim' }))

        expect(authorize).toHaveBeenCalledWith('run-1', 'escalation', 'I accept it.')
      })

      it('offers the implementation separately and only for the exact authorized final plan', () => {
        arrangeFacts('Available')
        const request = vi.fn().mockResolvedValue(true)
        useRequestImplementationMock.mockReturnValue({ requesting: false, error: null, request })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/Authorized by a human/)
        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/Recorded reason: Reviewed both rounds\./)
        expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
        expect(useRequestImplementationMock).toHaveBeenLastCalledWith('run-1', 'second', expect.any(Function))
        fireEvent.click(screen.getByRole('button', { name: authorizedLabel }))
        expect(request).toHaveBeenCalledWith('run-1', 'second', undefined)
        expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
      })

      it('keeps provider review and resolution of the final revision unavailable whatever the authorization says', () => {
        for (const state of ['Absent', 'Available', 'Consumed', 'Stale', 'Invalid']) {
          arrangeFacts(state)
          const { unmount } = render(<RunCockpitView runId="run-1" />)

          expect(screen.queryByRole('button', { name: /Request Claude review/ }), state).not.toBeInTheDocument()
          expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' }), state).not.toBeInTheDocument()
          unmount()
        }
      })

      it('shows a consumed authorization truthfully and withholds any new request', () => {
        arrangeFacts('Consumed', { consumedByAttemptId: 'attempt-9' })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/used by an implementation claim and cannot be reused/)
        expect(screen.getByText(/was already used by an implementation claim\. It cannot be reused/)).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
      })

      it.each([
        ['Stale', /no longer matches the run.s current plan, checkpoint, or fingerprint/],
        ['Invalid', /could not be validated, so it is not usable/],
      ])('shows a %s authorization as unusable with no action', (state, expected) => {
        arrangeFacts(state)

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(expected)
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
      })

      it('offers nothing when the server names a different final plan than the timeline', () => {
        arrangeFacts('Available', { finalProposalMessageId: 'other-plan' })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/names a different final plan/)
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
      })

      it('shows a failed read honestly and offers no decision or request', () => {
        arrangeFacts(null)
        usePlanningImplementationAuthorizationMock.mockReturnValue({
          authorization: null,
          loading: false,
          error: 'The authorization status could not be read, so no decision is shown. Nothing is assumed from earlier actions.',
          refresh: vi.fn(),
        })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/could not be read, so no decision is shown/)
        expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
      })

      it('renders no authorization panel without the escalation, for an ambiguous chain, or before the second revision', () => {
        arrange([rootCard, firstRevision, secondRevision], { reviewed: 'first', outcome: 'Challenged' }, { outcome: 'Resolved', original: 'first' })
        const { rerender } = render(<RunCockpitView runId="run-1" />)
        expect(screen.queryByLabelText('Human decision on the final plan')).not.toBeInTheDocument()
        expect(usePlanningImplementationAuthorizationMock).toHaveBeenLastCalledWith('run-1', null, runningCockpit.latestSequence)

        arrange([...exhausted, { ...secondRevision, id: 'second-b', sequence: 22 }], { reviewed: 'first', outcome: 'Challenged' })
        rerender(<RunCockpitView runId="run-1" />)
        expect(screen.queryByLabelText('Human decision on the final plan')).not.toBeInTheDocument()

        arrange([rootCard, firstRevision], { reviewed: 'root', outcome: 'Challenged' })
        rerender(<RunCockpitView runId="run-1" />)
        expect(screen.queryByLabelText('Human decision on the final plan')).not.toBeInTheDocument()
      })

      describe('with the real authorization read (committed lifetimes)', () => {
        type Reads = Array<{
          run: string
          escalation: string
          resolve: (value: PlanningImplementationAuthorizationResponse) => void
          reject: (reason: unknown) => void
        }>

        const available = (run: string) =>
          new PlanningImplementationAuthorizationResponse({
            runId: run,
            escalationMessageId: 'escalation',
            state: 'Available',
            finalProposalMessageId: 'second',
            orderedDecisionMessageIds: ['decision-1'],
            authorizationId: 'authorization-1',
            rationale: 'Reviewed both rounds.',
          })

        async function arrangeRealRead() {
          arrangeFacts(null)
          const actual = await vi.importActual<typeof import('../hooks/usePlanningImplementationAuthorization')>(
            '../hooks/usePlanningImplementationAuthorization',
          )
          usePlanningImplementationAuthorizationMock.mockImplementation(actual.usePlanningImplementationAuthorization)
          const reads: Reads = []
          vi.mocked(planningImplementationAuthorizationClient).mockReturnValue({
            getPlanningImplementationAuthorization: (run: string, escalation: string) =>
              new Promise<PlanningImplementationAuthorizationResponse>((resolve, reject) => {
                reads.push({ run, escalation, resolve, reject })
              }),
          } as never)
          const request = vi.fn().mockResolvedValue(true)
          useRequestImplementationMock.mockReturnValue({ requesting: false, error: null, request })
          return { reads, request }
        }

        const settle = async (action: () => void) => {
          await act(async () => {
            action()
            await Promise.resolve()
          })
        }

        it('withholds the implementation on returning to an earlier run until the replacement read supplies usable facts', async () => {
          const { reads } = await arrangeRealRead()
          const { rerender } = render(<RunCockpitView runId="run-1" />)
          await settle(() => reads[0].resolve(available('run-1')))
          expect(screen.getByRole('button', { name: authorizedLabel })).toBeInTheDocument()

          rerender(<RunCockpitView runId="run-2" />)
          expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
          rerender(<RunCockpitView runId="run-1" />)

          expect(reads).toHaveLength(3)
          expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
          expect(screen.getByLabelText('Human decision on the final plan')).not.toHaveTextContent(/Authorized by a human/)
          expect(useRequestImplementationMock).toHaveBeenLastCalledWith('run-1', null, expect.any(Function))

          // Obsolete completions of the earlier lifetimes change nothing.
          await settle(() => reads[0].resolve(available('run-1')))
          await settle(() => reads[1].resolve(available('run-2')))
          expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()

          await settle(() => reads[2].resolve(available('run-1')))
          expect(screen.getByRole('button', { name: authorizedLabel })).toBeInTheDocument()
          expect(useRequestImplementationMock).toHaveBeenLastCalledWith('run-1', 'second', expect.any(Function))
        })

        it('shows an unavailable read, not the earlier Available facts, when the replacement read fails', async () => {
          const { reads } = await arrangeRealRead()
          const { rerender } = render(<RunCockpitView runId="run-1" />)
          await settle(() => reads[0].resolve(available('run-1')))
          rerender(<RunCockpitView runId="run-2" />)
          rerender(<RunCockpitView runId="run-1" />)

          await settle(() => reads[2].reject(new Error('offline')))

          expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/could not be read, so no decision is shown/)
          expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
          expect(screen.queryByRole('button', { name: 'Authorize one implementation claim' })).not.toBeInTheDocument()
        })

        it('does not resurrect an earlier Consumed state after an A to B to A round trip', async () => {
          const { reads } = await arrangeRealRead()
          const { rerender } = render(<RunCockpitView runId="run-1" />)
          await settle(() =>
            reads[0].resolve(
              new PlanningImplementationAuthorizationResponse({
                runId: 'run-1',
                escalationMessageId: 'escalation',
                state: 'Consumed',
                finalProposalMessageId: 'second',
                orderedDecisionMessageIds: ['decision-1'],
                authorizationId: 'authorization-1',
                consumedByAttemptId: 'attempt-9',
              }),
            ),
          )
          expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/used by an implementation claim/)

          rerender(<RunCockpitView runId="run-2" />)
          rerender(<RunCockpitView runId="run-1" />)

          expect(screen.getByLabelText('Human decision on the final plan')).not.toHaveTextContent(/used by an implementation claim/)
          expect(screen.queryByRole('button', { name: authorizedLabel })).not.toBeInTheDocument()
        })
      })

      it('refreshes both the implementation status and the authorization after an implementation request is accepted', () => {
        const { refresh } = arrangeFacts('Available')
        const implementationRefresh = vi.fn()
        useImplementationAttemptStatusMock.mockReturnValue({ status: null, loading: false, error: null, refresh: implementationRefresh })
        let onRequested: (() => void) | undefined
        useRequestImplementationMock.mockImplementation((_run, _plan, callback) => {
          onRequested = callback
          return { requesting: false, error: null, request: vi.fn().mockResolvedValue(true) }
        })

        render(<RunCockpitView runId="run-1" />)
        onRequested?.()

        expect(implementationRefresh).toHaveBeenCalledOnce()
        expect(refresh).toHaveBeenCalledOnce()
      })
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
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
        refresh: async () => true,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(useChallengeResolutionAttemptStatusMock).toHaveBeenLastCalledWith('run-1', runningCockpit.latestSequence)

      rerender(<RunCockpitView runId="run-2" />)
      expect(useChallengeResolutionAttemptStatusMock).toHaveBeenLastCalledWith('run-2', runningCockpit.latestSequence)
    })
  })

  describe('Agent role repair wiring', () => {
    const roles = [
      {
        name: 'Claude critical review',
        repairHook: useRequestClaudeCriticalReviewRepairAttemptMock,
        ordinaryHook: useRequestClaudeCriticalReviewMock,
        statusHook: useClaudeCriticalReviewAttemptStatusMock,
        makeStatus: (overrides: object) =>
          new ClaudeCriticalReviewAttemptStatusResponse({
            hasAttempt: true,
            attemptId: 'review-9',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'InvalidStructuredOutput',
            ...overrides,
          }),
        repairButton: 'Request one format-repair critical review',
        ordinaryButton: 'Request Claude review',
        prepareOrdinary: () =>
          useCollaborationTimelineMock.mockReturnValue({
            cards: [providerObservedCodexProposal({ sequence: 1, id: 'message-1' })],
            loading: false,
            error: null,
            hasSuccessfulResponse: true,
          }),
        sourceId: 'review-9',
        errorText: 'A critical review repair was already requested for this attempt.',
      },
      {
        name: 'challenge resolution',
        repairHook: useRequestChallengeResolutionRepairAttemptMock,
        ordinaryHook: useRequestChallengeResolutionMock,
        statusHook: useChallengeResolutionAttemptStatusMock,
        makeStatus: (overrides: object) =>
          new ChallengeResolutionAttemptStatusResponse({
            hasAttempt: true,
            attemptId: 'resolution-9',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'InvalidStructuredOutput',
            ...overrides,
          }),
        repairButton: 'Request one format-repair resolution',
        ordinaryButton: 'Resolve challenges with Codex',
        prepareOrdinary: () =>
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
          }),
        sourceId: 'resolution-9',
        errorText: 'A resolution repair was already requested for this attempt.',
      },
      {
        name: 'code review',
        repairHook: useRequestCodeReviewRepairAttemptMock,
        ordinaryHook: useRequestCodeReviewMock,
        statusHook: useCodeReviewAttemptStatusMock,
        makeStatus: (overrides: object) =>
          new CodeReviewAttemptStatusResponse({
            hasAttempt: true,
            attemptId: 'code-review-9',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'InvalidStructuredOutput',
            ...overrides,
          }),
        repairButton: 'Request one format-repair code review',
        ordinaryButton: 'Request code review',
        prepareOrdinary: () =>
          useReviewCorrectionAttemptStatusMock.mockReturnValue({
            status: new ReviewCorrectionAttemptStatusResponse({ reviewableExecutionReportMessageId: 'report-1' }),
            loading: false,
            error: null,
            refresh: vi.fn(),
          }),
        sourceId: 'code-review-9',
        errorText: 'A code review repair was already requested for this attempt.',
      },
    ]

    function showRunningCockpit() {
      useRunCockpitMock.mockReturnValue({
        cockpit: runningCockpit,
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
    }

    describe.each(roles)('$name repair', (role) => {
      function withStatus(overrides: object = {}) {
        showRunningCockpit()
        role.prepareOrdinary()
        const refresh = vi.fn()
        role.statusHook.mockReturnValue({ status: role.makeStatus(overrides), loading: false, error: null, refresh })
        return refresh
      }

      it('posts the repair once for the current run and displayed attempt and refreshes that role on success', () => {
        const refresh = withStatus()
        const request = vi.fn()
        role.repairHook.mockReturnValue({ requesting: false, error: null, request })

        render(<RunCockpitView runId="run-1" />)
        fireEvent.click(screen.getByRole('button', { name: role.repairButton }))

        expect(request).toHaveBeenCalledExactlyOnceWith(role.sourceId)
        expect(role.repairHook).toHaveBeenCalledWith('run-1', refresh)
        expect(screen.getByRole('button', { name: role.ordinaryButton })).toBeEnabled()
      })

      it('disables the ordinary request while the repair is pending', () => {
        withStatus()
        role.repairHook.mockReturnValue({ requesting: true, error: null, request: vi.fn() })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByRole('button', { name: 'Requesting repair…' })).toBeDisabled()
        expect(screen.getByRole('button', { name: /^Requesting…$/ })).toBeDisabled()
      })

      it('disables the repair while the ordinary request is pending', () => {
        withStatus()
        role.ordinaryHook.mockReturnValue({ requesting: true, error: null, request: vi.fn() })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByRole('button', { name: role.repairButton })).toBeDisabled()
      })

      it('binds to the newly selected run and offers no repair for a masked status', () => {
        withStatus()
        const { rerender } = render(<RunCockpitView runId="run-1" />)
        expect(screen.getByRole('button', { name: role.repairButton })).toBeInTheDocument()

        role.statusHook.mockReturnValue({ status: null, loading: true, error: null, refresh: vi.fn() })
        rerender(<RunCockpitView runId="run-2" />)

        expect(role.repairHook).toHaveBeenLastCalledWith('run-2', expect.any(Function))
        expect(screen.queryByRole('button', { name: role.repairButton })).not.toBeInTheDocument()
      })

      it('shows a safe repair error while the ordinary action stays available', () => {
        withStatus()
        role.repairHook.mockReturnValue({ requesting: false, error: role.errorText, request: vi.fn() })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByText(role.errorText)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: role.ordinaryButton })).toBeEnabled()
      })

      it('keeps the lineage visible for a repair attempt and offers no second repair', () => {
        withStatus({ attemptNumber: 4, repairSourceAttemptId: 'earlier-1', repairSourceAttemptNumber: 3 })

        render(<RunCockpitView runId="run-1" />)

        expect(screen.getByText(/Attempt #4 is the one repair request for attempt #3\./)).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: role.repairButton })).not.toBeInTheDocument()
      })
    })
  })

  describe('repair request state across run switches (real hook)', () => {
    it('keeps run B pending and disabled when run A’s older repair completes after the switch', async () => {
      const real = await vi.importActual<typeof useRequestCodeReviewRepairAttemptModule>(
        '../hooks/useRequestCodeReviewRepairAttempt',
      )
      useRequestCodeReviewRepairAttemptMock.mockImplementation(real.useRequestCodeReviewRepairAttempt)
      let finishA!: () => void
      vi.mocked(requestCodeReviewRepairAttemptClient).mockReturnValue({
        requestCodeReviewRepairAttempt: vi
          .fn()
          .mockReturnValueOnce(new Promise<void>((resolve) => (finishA = resolve)))
          .mockReturnValueOnce(new Promise<void>(() => {})),
      } as unknown as ReturnType<typeof requestCodeReviewRepairAttemptClient>)
      useRunCockpitMock.mockImplementation((id: string | null) => ({
        cockpit: new GetRunCockpitResponse({ ...runningCockpit, runId: id ?? undefined }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      }))
      useReviewCorrectionAttemptStatusMock.mockReturnValue({
        status: new ReviewCorrectionAttemptStatusResponse({ reviewableExecutionReportMessageId: 'report-1' }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      useCodeReviewAttemptStatusMock.mockReturnValue({
        status: new CodeReviewAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'code-review-9',
          attemptNumber: 3,
          status: 'Failed',
          outcome: 'InvalidStructuredOutput',
        }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request one format-repair code review' }))
      rerender(<RunCockpitView runId="run-2" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request one format-repair code review' }))
      expect(screen.getByRole('button', { name: 'Requesting repair…' })).toBeDisabled()

      await act(async () => {
        finishA()
      })

      expect(screen.getByRole('button', { name: 'Requesting repair…' })).toBeDisabled()
      expect(screen.getByRole('button', { name: /^Requesting…$/ })).toBeDisabled()
    })

    it('keeps ordinary and repair controls isolated per run while an older run’s requests complete late (real hooks)', async () => {
      const realRepair = await vi.importActual<typeof useRequestCodeReviewRepairAttemptModule>(
        '../hooks/useRequestCodeReviewRepairAttempt',
      )
      const realOrdinary = await vi.importActual<typeof useRequestCodeReviewModule>('../hooks/useRequestCodeReview')
      useRequestCodeReviewRepairAttemptMock.mockImplementation(realRepair.useRequestCodeReviewRepairAttempt)
      useRequestCodeReviewMock.mockImplementation(realOrdinary.useRequestCodeReview)
      let finishOrdinaryA!: () => void
      let finishOrdinaryB!: () => void
      const requestCodeReview = vi
        .fn()
        .mockReturnValueOnce(new Promise<void>((resolve) => (finishOrdinaryA = resolve)))
        .mockReturnValueOnce(new Promise<void>((resolve) => (finishOrdinaryB = resolve)))
      vi.mocked(requestCodeReviewClient).mockReturnValue({ requestCodeReview } as unknown as ReturnType<
        typeof requestCodeReviewClient
      >)
      useRunCockpitMock.mockImplementation((id: string | null) => ({
        cockpit: new GetRunCockpitResponse({ ...runningCockpit, runId: id ?? undefined }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      }))
      useReviewCorrectionAttemptStatusMock.mockReturnValue({
        status: new ReviewCorrectionAttemptStatusResponse({ reviewableExecutionReportMessageId: 'report-1' }),
        loading: false,
        error: null,
        refresh: vi.fn(),
      })
      const refreshA = vi.fn()
      useCodeReviewAttemptStatusMock.mockReturnValue({
        status: new CodeReviewAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'code-review-9',
          attemptNumber: 3,
          status: 'Failed',
          outcome: 'InvalidStructuredOutput',
        }),
        loading: false,
        error: null,
        refresh: refreshA,
      })
      const repairButton = () => screen.getByRole('button', { name: 'Request one format-repair code review' })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      fireEvent.click(screen.getByRole('button', { name: 'Request code review' }))
      expect(repairButton()).toBeDisabled()

      rerender(<RunCockpitView runId="run-2" />)
      expect(repairButton()).toBeEnabled()
      expect(screen.getByRole('button', { name: 'Request code review' })).toBeEnabled()

      fireEvent.click(screen.getByRole('button', { name: 'Request code review' }))
      expect(repairButton()).toBeDisabled()
      expect(requestCodeReview).toHaveBeenCalledTimes(2)

      await act(async () => {
        finishOrdinaryA()
      })
      expect(repairButton()).toBeDisabled()
      expect(screen.getByRole('button', { name: /^Requesting…$/ })).toBeDisabled()
      expect(refreshA).not.toHaveBeenCalled()

      await act(async () => {
        finishOrdinaryB()
      })
      await waitFor(() => expect(repairButton()).toBeEnabled())
      expect(refreshA).toHaveBeenCalledTimes(1)
    })
  })

  describe('candidate-specific time-fit wiring', () => {
    function cockpitWithFits(overrides: Partial<Record<string, string>>) {
      const paths = ['CodexPlanning', 'ClaudeCriticalReview', 'ChallengeResolution', 'Implementation', 'CodeReview', 'ReviewCorrection']
      return new GetRunCockpitResponse({
        ...runningCockpit,
        agentClaimPathTimeFits: paths.map(
          (path) =>
            new AgentClaimPathTimeFitResponse({
              claimPath: path,
              fit: overrides[path] ?? 'Fits',
            }),
        ),
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0])
    }

    it('the ten/twenty-minute split: a 10-minute claim path still offers its action while a 20-minute one is withheld on its own time fit', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: cockpitWithFits({ Implementation: 'DoesNotFit' }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })

      render(<RunCockpitView runId="run-1" />)

      // CodexPlanning (a 10-minute-configured path) is unaffected by Implementation's own
      // (20-minute-configured) time-fit block: each claim path's fit is independent.
      expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
    })

    it('withholds the challenge-resolution action on its own time fit, with copy distinct from the global-block copy', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: cockpitWithFits({ ChallengeResolution: 'DoesNotFit' }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
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

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
      expect(screen.getByText(/insufficient reserved invocation time/i)).toBeInTheDocument()
      expect(screen.queryByText(/run-wide agent claim budget/i)).not.toBeInTheDocument()
    })

    it('represents both the global block and the candidate-fit block together when both apply, never hiding one for the other', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: new GetRunCockpitResponse({
          ...cockpitWithFits({ ChallengeResolution: 'DoesNotFit' }),
          agentBudgetExhausted: true,
          agentAttemptsUsed: 16,
        } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
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

      render(<RunCockpitView runId="run-1" />)

      expect(screen.queryByRole('button', { name: 'Resolve challenges with Codex' })).not.toBeInTheDocument()
      // Both the global count-budget block AND the ChallengeResolution-specific time-fit block
      // are present at once — the global message appears once per withheld action (CodexPlanning
      // and ChallengeResolution both show it), so this asserts "at least one", not "exactly one".
      expect(screen.getAllByText(/run-wide agent claim budget/i).length).toBeGreaterThan(0)
      expect(screen.getByText(/insufficient reserved invocation time/i)).toBeInTheDocument()
    })

    it('never carries a previously selected run\'s candidate-fit state into the newly selected run, even transiently', () => {
      useRunCockpitMock.mockReturnValue({
        cockpit: cockpitWithFits({ CodexPlanning: 'DoesNotFit' }),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })

      const { rerender } = render(<RunCockpitView runId="run-1" />)
      expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
      expect(screen.getByText(/insufficient reserved invocation time/i)).toBeInTheDocument()

      // Selecting a different run while the hook still returns run-1's own stale projection
      // must never carry run-1's DoesNotFit state — or its Fits state — into run-2: the honest
      // "unavailable" state is shown instead until run-2's own projection arrives.
      rerender(<RunCockpitView runId="run-2" />)
      expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
      expect(screen.queryByText(/insufficient reserved invocation time/i)).not.toBeInTheDocument()
      expect(screen.getByText(/time-fit status is confirmed/i)).toBeInTheDocument()

      useRunCockpitMock.mockReturnValue({
        cockpit: new GetRunCockpitResponse({ ...cockpitWithFits({}), runId: 'run-2' } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
        cards: [],
        connection: 'live',
        loading: false,
        error: null,
        syncError: null,
        refresh: async () => true,
      })
      rerender(<RunCockpitView runId="run-2" />)
      expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
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
      refresh: async () => true,
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
      refresh: async () => true,
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
      refresh: async () => true,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.queryByRole('region', { name: 'Latest agent attempt' })).not.toBeInTheDocument()
  })
})

describe('RunCockpitView token usage', () => {
  const partialCockpit = new GetRunCockpitResponse({
    ...runningCockpit,
    latestAgentAttempt: new RunCockpitAgentAttemptResponse({
      attemptId: 'attempt-2',
      attemptNumber: 2,
      role: 'CriticalReviewer',
      provider: 'ClaudeCode',
      status: 'Failed',
      outcome: 'ProviderInvocationFailed',
      dispatchedAtUtc: new Date('2026-09-24T10:00:00Z'),
      processExecution: new AgentProcessExecutionResponse({ outcome: 'Exited', exitCode: 1, durationMilliseconds: 900 }),
      tokenUsage: new AgentTokenUsageResponse({
        inputTokens: 1200,
        outputTokens: 345,
        cacheCreationInputTokens: 67,
        cacheReadInputTokens: 890,
      }),
    }),
    tokenUsageSummary: new RunTokenUsageSummaryResponse({
      completeness: 'Partial',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 1,
      inputTokens: 1200,
      outputTokens: 345,
      cacheCreationInputTokens: 67,
      cacheReadInputTokens: 890,
    }),
  })

  function renderWith(cockpit: GetRunCockpitResponse, loading = false) {
    useRunCockpitMock.mockReturnValue({ cockpit, cards: [], connection: 'live', loading, error: null, syncError: null, refresh: async () => true })
  }

  it('renders the latest attempt usage beside its process evidence and the run summary as partial', () => {
    renderWith(partialCockpit)

    render(<RunCockpitView runId="run-1" />)

    const section = screen.getByRole('region', { name: 'Latest agent attempt' })
    expect(section).toHaveTextContent('Process exited with code 1 after 900 ms')
    expect(section).toHaveTextContent('Tokens: 1,200 input · 345 output · 67 cache write · 890 cache read')

    const summary = screen.getByLabelText('Run token usage')
    expect(summary).toHaveAttribute('data-completeness', 'Partial')
    expect(summary).toHaveTextContent('Partial token count: 1,200 input')
    expect(summary).toHaveTextContent('so this is not the run total')
    expect(screen.queryByText(/Run token total/)).not.toBeInTheDocument()
  })

  it('labels a complete summary as the run total', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...partialCockpit,
        tokenUsageSummary: new RunTokenUsageSummaryResponse({
          completeness: 'Complete',
          attemptsWithKnownUsage: 1,
          attemptsWithUnknownUsage: 0,
          inputTokens: 1200,
          outputTokens: 345,
        }),
      }),
    )

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByLabelText('Run token usage')).toHaveTextContent(
      'Run token total: 1,200 input · 345 output (all 1 dispatched attempt reported usage)',
    )
  })

  it('shows a neutral empty state before any agent attempt is dispatched', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...runningCockpit,
        tokenUsageSummary: new RunTokenUsageSummaryResponse({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 }),
      }),
    )

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByLabelText('Run token usage')).toHaveTextContent('No token usage data yet')
    expect(screen.queryByRole('region', { name: 'Latest agent attempt' })).not.toBeInTheDocument()
  })

  it('never renders token usage from the previously selected run for the newly selected one', () => {
    renderWith(partialCockpit, true)

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.getByLabelText('Run token usage')).toBeInTheDocument()

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByLabelText('Run token usage')).not.toBeInTheDocument()
    expect(screen.queryByText(/Partial token count/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Tokens: 1,200 input/)).not.toBeInTheDocument()
  })

  it('renders the provider-separated projection beside, and consistent with, the run-wide summary', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...partialCockpit,
        providerTokenUsageSummaries: [
          new RunCockpitProviderTokenUsageEntryResponse({
            attribution: 'Codex',
            summary: new RunTokenUsageSummaryResponse({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 }),
          }),
          new RunCockpitProviderTokenUsageEntryResponse({
            attribution: 'ClaudeCode',
            summary: new RunTokenUsageSummaryResponse({
              completeness: 'Partial',
              attemptsWithKnownUsage: 1,
              attemptsWithUnknownUsage: 1,
              inputTokens: 1200,
              outputTokens: 345,
            }),
          }),
          new RunCockpitProviderTokenUsageEntryResponse({
            attribution: 'Unattributed',
            summary: new RunTokenUsageSummaryResponse({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 }),
          }),
        ],
      }),
    )

    render(<RunCockpitView runId="run-1" />)

    const list = screen.getByLabelText('Provider token usage')
    expect(list.querySelectorAll('li')).toHaveLength(3)
    expect(list).toHaveTextContent('Claude Code: Partial token count: 1,200 input · 345 output')
    expect(list).toHaveTextContent('Codex: No token usage data yet')
    expect(list).toHaveTextContent('Unattributed: No token usage data yet')
    // Both the run-wide and the provider-separated projections are shown together, never one in
    // place of the other.
    expect(screen.getByLabelText('Run token usage')).toBeInTheDocument()
  })

  it('never renders the previously selected run\'s provider buckets for the newly selected run', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...partialCockpit,
        providerTokenUsageSummaries: [
          new RunCockpitProviderTokenUsageEntryResponse({
            attribution: 'Codex',
            summary: new RunTokenUsageSummaryResponse({
              completeness: 'Complete',
              attemptsWithKnownUsage: 1,
              attemptsWithUnknownUsage: 0,
              inputTokens: 1000,
              outputTokens: 200,
            }),
          }),
        ],
      }),
      true,
    )

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.getByLabelText('Provider token usage')).toBeInTheDocument()

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByLabelText('Provider token usage')).not.toBeInTheDocument()
  })
})

describe('RunCockpitView one Agent claim slot remaining warning', () => {
  function renderWith(cockpit: GetRunCockpitResponse, loading = false) {
    useRunCockpitMock.mockReturnValue({ cockpit, cards: [], connection: 'live', loading, error: null, syncError: null, refresh: async () => true })
  }

  it('renders alongside the exhausted banner\'s own healthy (non-exhausted) state without it, and shows nothing when the budget is not down to its last slot', () => {
    renderWith(runningCockpit)

    render(<RunCockpitView runId="run-1" />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText(/Only one Agent claim slot remains/)).not.toBeInTheDocument()
  })

  it('renders the one-slot-remaining warning without ever showing the exhausted banner, and never implies the next claim is eligible', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...runningCockpit,
        maximumAgentAttempts: 16,
        agentAttemptsUsed: 15,
        agentBudgetExhausted: false,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
    )

    render(<RunCockpitView runId="run-1" />)

    const warning = screen.getByRole('status')
    expect(warning).toHaveTextContent('Only one Agent claim slot remains for this run (15/16 used).')
    expect(warning).toHaveTextContent('other controls may still block that attempt')
    // The exhausted banner's own copy/behavior is untouched by this warning.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText(/reached its maximum/)).not.toBeInTheDocument()
  })

  it('shows only the existing exhausted banner, never this warning, once the budget is fully exhausted', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...runningCockpit,
        maximumAgentAttempts: 16,
        agentAttemptsUsed: 16,
        agentBudgetExhausted: true,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
    )

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('alert')).toHaveTextContent('reached its maximum of 16 claimed Agent attempts')
    expect(screen.queryByText(/Only one Agent claim slot remains/)).not.toBeInTheDocument()
  })

  it('never carries a previously selected run\'s one-slot-remaining warning into the newly selected run, even transiently', () => {
    renderWith(
      new GetRunCockpitResponse({
        ...runningCockpit,
        maximumAgentAttempts: 16,
        agentAttemptsUsed: 15,
        agentBudgetExhausted: false,
      } as ConstructorParameters<typeof GetRunCockpitResponse>[0]),
      true,
    )

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.getByRole('status')).toBeInTheDocument()

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByText(/Only one Agent claim slot remains/)).not.toBeInTheDocument()
  })
})

describe('RunCockpitView Claude model request', () => {
  it('shows the run request and the latest attempt\'s own snapshot as separate request-only facts', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        ...runningCockpit,
        requestedClaudeModel: 'haiku',
        latestAgentAttempt: new RunCockpitAgentAttemptResponse({
          attemptId: 'attempt-2',
          attemptNumber: 2,
          role: 'CriticalReviewer',
          provider: 'ClaudeCode',
          status: 'Running',
          requestedModel: 'sonnet',
        }),
      }),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByLabelText('Claude model request')).toHaveTextContent(
      'Requested Claude model for future attempts: haiku.',
    )
    expect(screen.getByRole('region', { name: 'Latest agent attempt' })).toHaveTextContent(
      'Model requested at claim: sonnet',
    )
  })

  it('does not render the previously selected run\'s request for the newly selected run', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({ ...runningCockpit, requestedClaudeModel: 'opus' }),
      cards: [],
      connection: 'live',
      loading: true,
      error: null,
      syncError: null,
      refresh: async () => true,
    })

    const { rerender } = render(<RunCockpitView runId="run-1" />)
    expect(screen.getByLabelText('Claude model request')).toBeInTheDocument()

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByLabelText('Claude model request')).not.toBeInTheDocument()
  })
})

describe('RunCockpitView token-activity warnings', () => {
  it('renders each provider warning from the cockpit projection and hides it for a newly selected run', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        ...runningCockpit,
        tokenWarnings: [
          new RunCockpitTokenWarningResponse({
            provider: 'Codex', state: 'ThresholdReached', thresholdTokens: 1200, knownTokenCount: 1200, countedAttempts: 1,
            pendingAttempts: 0, insufficientEvidenceAttempts: 0, unattributedAttempts: 0,
          }),
          new RunCockpitTokenWarningResponse({
            provider: 'ClaudeCode', state: 'Indeterminate', thresholdTokens: 900, knownTokenCount: 4, countedAttempts: 1,
            pendingAttempts: 1, insufficientEvidenceAttempts: 0, unattributedAttempts: 0,
          }),
        ],
      }),
      cards: [],
      connection: 'live',
      loading: true,
      error: null,
      syncError: null,
      refresh: async () => true,
    })

    const { rerender } = render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('alert')).toHaveTextContent('Codex token-activity warning')
    expect(screen.getByLabelText('Claude Code token-activity warning')).toHaveTextContent('not an all-clear')

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByLabelText('Codex token-activity warning')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('RunCockpitView token-activity stops', () => {
  it('renders each provider stop from the cockpit projection, separate from the advisory warning, and hides it for a newly selected run', () => {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        ...runningCockpit,
        tokenStops: [
          new RunCockpitTokenStopResponse({
            provider: 'Codex', state: 'ThresholdReached', claimBlocked: true, thresholdTokens: 1200, knownTokenCount: 1200,
            countedAttempts: 1, pendingAttempts: 0, insufficientEvidenceAttempts: 0, unattributedAttempts: 0, countOverflowed: false,
          }),
          new RunCockpitTokenStopResponse({
            provider: 'ClaudeCode', state: 'BelowThresholdComplete', claimBlocked: false, thresholdTokens: 900, knownTokenCount: 4,
            countedAttempts: 1, pendingAttempts: 0, insufficientEvidenceAttempts: 0, unattributedAttempts: 0, countOverflowed: false,
          }),
        ],
      }),
      cards: [],
      connection: 'live',
      loading: true,
      error: null,
      syncError: null,
      refresh: async () => true,
    })

    const { rerender } = render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('alert')).toHaveTextContent('Codex token stop reached')
    expect(screen.getByLabelText('Claude Code token stop')).toHaveTextContent('does not show that the provider is available')
    expect(screen.queryByLabelText('Codex token-activity warning')).toHaveTextContent('no token-activity warning threshold set')

    rerender(<RunCockpitView runId="run-2" />)

    expect(screen.queryByLabelText('Codex token stop')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('RunCockpitView Claude effort request', () => {
  function mockCockpit(refresh: () => Promise<boolean>, overrides: Partial<GetRunCockpitResponse> = {}) {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({ ...runningCockpit, requestedClaudeModel: 'opus', requestedClaudeEffort: 'low', ...overrides }),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh,
    })
  }

  it('shows the run effort request separately from the latest attempt\'s claim-time effort, both as requests only', () => {
    mockCockpit(async () => true, {
      latestAgentAttempt: new RunCockpitAgentAttemptResponse({
        attemptId: 'attempt-2',
        attemptNumber: 2,
        role: 'Implementer',
        provider: 'ClaudeCode',
        status: 'Running',
        requestedModel: 'sonnet',
        requestedEffort: 'high',
      }),
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByLabelText('Claude model request')).toHaveTextContent(
      'Requested Claude effort for future attempts: low. This is a request only; the effort actually applied is not observed',
    )
    expect(screen.getByRole('region', { name: 'Latest agent attempt' })).toHaveTextContent(
      'Effort requested at claim: high (a request only',
    )
  })

  it('refreshes the authoritative cockpit after a successful Save without any run notification', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue({ requestedModel: 'sonnet', requestedEffort: 'medium' })
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    const refresh = vi.fn().mockResolvedValue(true)
    mockCockpit(refresh)

    render(<RunCockpitView runId="run-1" />)
    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'sonnet' } })
    fireEvent.change(screen.getByLabelText('Requested Claude effort'), { target: { value: 'medium' } })
    fireEvent.click(within(screen.getByLabelText('Claude model request')).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1))
    expect(setClaudeModelPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: 'sonnet', requestedEffort: 'medium' }),
    )
    expect(screen.queryByText(/could not be refreshed/)).not.toBeInTheDocument()
  })

  it('refreshes after a successful Clear and shows a fixed safe message when the refresh fails or the run went stale', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue({})
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    const refresh = vi.fn().mockResolvedValue(false)
    mockCockpit(refresh)

    render(<RunCockpitView runId="run-1" />)
    fireEvent.click(within(screen.getByLabelText('Claude model request')).getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1))
    expect(setClaudeModelPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: undefined, requestedEffort: undefined }),
    )
    expect(
      await screen.findByText('Saved, but the cockpit could not be refreshed; the displayed request may be out of date.'),
    ).toBeInTheDocument()
  })

  it('does not refresh when the save itself fails', async () => {
    const setClaudeModelPreference = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    const refresh = vi.fn().mockResolvedValue(true)
    mockCockpit(refresh)

    render(<RunCockpitView runId="run-1" />)
    fireEvent.click(within(screen.getByLabelText('Claude model request')).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The Claude model request could not be saved for this run.')).toBeInTheDocument()
    expect(refresh).not.toHaveBeenCalled()
    expect(screen.queryByText(/sensitive detail/)).not.toBeInTheDocument()
  })
})

describe('RunCockpitView Claude turn limit request', () => {
  function mockCockpit(refresh: () => Promise<boolean>, overrides: Partial<GetRunCockpitResponse> = {}) {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({
        ...runningCockpit,
        claudeMutationTurnLimit: new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns: 12 }),
        ...overrides,
      }),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh,
    })
  }

  it('shows the control with the current saved request for an editable run', () => {
    mockCockpit(async () => true)

    render(<RunCockpitView runId="run-1" />)

    const control = screen.getByRole('group', { name: 'Claude turn limit' })
    expect(control).toHaveTextContent('Current run request: 12 turns')
    expect((within(control).getByLabelText('Requested Claude turn limit') as HTMLInputElement).value).toBe('12')
  })

  it.each(['Created', 'Running'])('is editable while the run is %s', (lifecycle) => {
    mockCockpit(async () => true, { lifecycle })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByLabelText('Requested Claude turn limit')).toBeTruthy()
  })

  it.each(['Completed', 'Failed'])('is read-only once the run is %s', (lifecycle) => {
    mockCockpit(async () => true, { lifecycle })

    render(<RunCockpitView runId="run-1" />)

    const control = screen.getByRole('group', { name: 'Claude turn limit' })
    expect(control).toHaveTextContent('Current run request: 12 turns')
    expect(within(control).queryByLabelText('Requested Claude turn limit')).toBeNull()
    expect(within(control).queryByRole('button', { name: 'Save' })).toBeNull()
  })

  it('refreshes the authoritative cockpit after a successful save', async () => {
    const setClaudeMutationTurnLimit = vi.fn().mockResolvedValue({ maxTurns: 20 })
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({ setClaudeMutationTurnLimit } as never)
    const refresh = vi.fn().mockResolvedValue(true)
    mockCockpit(refresh)

    render(<RunCockpitView runId="run-1" />)
    const control = screen.getByRole('group', { name: 'Claude turn limit' })
    fireEvent.change(within(control).getByLabelText('Requested Claude turn limit'), { target: { value: '20' } })
    fireEvent.click(within(control).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1))
    expect(setClaudeMutationTurnLimit).toHaveBeenCalledWith('run-1', expect.objectContaining({ maxTurns: 20 }))
  })

  it('refreshes after a clear and does not refresh when the save fails', async () => {
    const setClaudeMutationTurnLimit = vi.fn().mockResolvedValueOnce({}).mockRejectedValueOnce(new Error('sensitive detail'))
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({ setClaudeMutationTurnLimit } as never)
    const refresh = vi.fn().mockResolvedValue(true)
    mockCockpit(refresh)

    render(<RunCockpitView runId="run-1" />)
    const control = screen.getByRole('group', { name: 'Claude turn limit' })
    fireEvent.click(within(control).getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1))

    fireEvent.click(within(screen.getByRole('group', { name: 'Claude turn limit' })).getByRole('button', { name: 'Clear' }))
    expect(await screen.findByText('The Claude turn limit request could not be saved for this run.')).toBeTruthy()
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(screen.queryByText(/sensitive detail/)).toBeNull()
  })

  it('shows Unknown for a stored value the server could not validate', () => {
    mockCockpit(async () => true, { claudeMutationTurnLimit: new ClaudeMutationTurnLimitResponse({ state: 'Unknown' }) })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('group', { name: 'Claude turn limit' })).toHaveTextContent('Current run request: Unknown')
  })

  it('shows the latest attempt immutable fact separately from the current run request', () => {
    mockCockpit(async () => true, {
      claudeMutationTurnLimit: new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' }),
      latestAgentAttempt: new RunCockpitAgentAttemptResponse({
        attemptId: 'attempt-2',
        attemptNumber: 2,
        role: 'Implementer',
        provider: 'ClaudeCode',
        status: 'Running',
        maxTurns: new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns: 7 }),
      }),
    })

    render(<RunCockpitView runId="run-1" />)

    expect(screen.getByRole('region', { name: 'Latest agent attempt' })).toHaveTextContent(
      'Claude turn limit for this attempt: Requested: 7 turns',
    )
    expect(screen.getByRole('group', { name: 'Claude turn limit' })).toHaveTextContent('Current run request: Not requested')
  })

  it('does not carry a typed draft or error across a run switch', () => {
    mockCockpit(async () => true, { claudeMutationTurnLimit: new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' }) })
    const { rerender } = render(<RunCockpitView runId="run-1" />)
    fireEvent.change(screen.getByLabelText('Requested Claude turn limit'), { target: { value: '55' } })
    expect((screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement).value).toBe('55')

    mockCockpit(async () => true, {
      runId: 'run-2',
      claudeMutationTurnLimit: new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' }),
    })
    rerender(<RunCockpitView runId="run-2" />)

    expect((screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement).value).toBe('')
  })
})

describe('RunCockpitView execution mode', () => {
  function renderMode(executionMode: string | undefined, lifecycle = 'Running') {
    useRunCockpitMock.mockReturnValue({
      cockpit: new GetRunCockpitResponse({ ...runningCockpit, executionMode, lifecycle } as ConstructorParameters<
        typeof GetRunCockpitResponse
      >[0]),
      cards: [],
      connection: 'live',
      loading: false,
      error: null,
      syncError: null,
      refresh: async () => true,
    })
    return render(<RunCockpitView runId="run-1" />)
  }

  it('discloses a manual Agent run and, while Created, that it waits for an explicit planning request', () => {
    renderMode('ManualAgent', 'Created')
    expect(screen.getByText('Manual Agent run')).toBeInTheDocument()
    expect(screen.getByText(/waiting for an explicit planning request/i)).toBeInTheDocument()
    expect(screen.getByText(/waiting for an explicit planning request/i).textContent).not.toMatch(/autonomous|ready/i)
    expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
  })

  it('does not claim a waiting state for a manual Agent run that is already running', () => {
    renderMode('ManualAgent', 'Running')
    expect(screen.queryByText(/waiting for an explicit planning request/i)).not.toBeInTheDocument()
  })

  it('keeps a legacy run visibly unclassified while still offering Agent requests', () => {
    renderMode('Legacy')
    expect(screen.getByText('Legacy run — execution mode was not recorded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Request Codex plan' })).toBeInTheDocument()
  })

  it('labels a simulated run as a demo and offers no Agent request action', () => {
    renderMode('Simulated')
    expect(screen.getByText('Simulated demo run')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Codex planning' })).not.toBeInTheDocument()
    expect(screen.getByText(/agent requests are not available for it/i)).toBeInTheDocument()
  })

  it.each([['Unrecognized'], ['SomethingNew'], [undefined]])(
    'discloses an unrecognized mode (%s) and offers no Agent request action',
    (mode) => {
      renderMode(mode)
      expect(screen.getByText('Unrecognized execution mode')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Request Codex plan' })).not.toBeInTheDocument()
      expect(screen.queryByRole('region', { name: 'Codex planning' })).not.toBeInTheDocument()
      expect(screen.getByText(/does not recognize/i)).toBeInTheDocument()
    },
  )
})
