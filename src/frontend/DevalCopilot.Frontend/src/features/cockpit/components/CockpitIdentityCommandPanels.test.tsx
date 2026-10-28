// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { abandonManualRunClient, localCommitStatusClient, manualRunAbandonmentClient, requestLocalCommitClient } from '../../../api/clients'
import {
  AgentClaimPathTimeFitResponse,
  AgentInvocationTimeBudgetResponse,
  GetLocalCommitStatusResponse,
  GetManualRunAbandonmentResponse,
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
} from '../../../api/generated/api-client'
import * as useRunCockpitModule from '../hooks/useRunCockpit'
import * as useCollaborationTimelineModule from '../hooks/useCollaborationTimeline'
import * as useAgentAttemptStatusModule from '../hooks/useAgentAttemptStatus'
import * as useRequestCodexPlanningAttemptModule from '../hooks/useRequestCodexPlanningAttempt'
import * as useRequestCodexPlanningRepairAttemptModule from '../hooks/useRequestCodexPlanningRepairAttempt'
import * as useClaudeCriticalReviewAttemptStatusModule from '../hooks/useClaudeCriticalReviewAttemptStatus'
import * as useRequestClaudeCriticalReviewModule from '../hooks/useRequestClaudeCriticalReview'
import * as useRequestClaudeCriticalReviewRepairAttemptModule from '../hooks/useRequestClaudeCriticalReviewRepairAttempt'
import * as useChallengeResolutionAttemptStatusModule from '../hooks/useChallengeResolutionAttemptStatus'
import * as useRequestChallengeResolutionModule from '../hooks/useRequestChallengeResolution'
import * as useRequestChallengeResolutionRepairAttemptModule from '../hooks/useRequestChallengeResolutionRepairAttempt'
import * as useImplementationAttemptStatusModule from '../hooks/useImplementationAttemptStatus'
import * as useRequestImplementationModule from '../hooks/useRequestImplementation'
import * as useCodeReviewAttemptStatusModule from '../hooks/useCodeReviewAttemptStatus'
import * as useRequestCodeReviewModule from '../hooks/useRequestCodeReview'
import * as useRequestCodeReviewRepairAttemptModule from '../hooks/useRequestCodeReviewRepairAttempt'
import * as useReviewCorrectionAttemptStatusModule from '../hooks/useReviewCorrectionAttemptStatus'
import * as useRequestReviewCorrectionModule from '../hooks/useRequestReviewCorrection'
import * as useAuthorizeReviewCorrectionModule from '../hooks/useAuthorizeReviewCorrection'
import * as useVerificationDiagnosisStatusModule from '../hooks/useVerificationDiagnosisStatus'
import * as useRequestVerificationDiagnosisModule from '../hooks/useRequestVerificationDiagnosis'
import * as useRequestDiagnosisCorrectionModule from '../hooks/useRequestDiagnosisCorrection'
import * as usePlanningImplementationAuthorizationModule from '../hooks/usePlanningImplementationAuthorization'
import * as useAuthorizePlanningImplementationModule from '../hooks/useAuthorizePlanningImplementation'
import * as useAgentSupervisionModule from '../hooks/useAgentSupervision'
import * as usePlanningSequenceModule from '../hooks/usePlanningSequence'
import { resultFor } from '../agentSupervisionFixture'
import { armableStatusFor, hookResultFor } from '../planningSequenceFixture'
import { idleStateFor } from '../workspaceExecutionFixture'
import { availableCapacityState } from '../executionCapacityFixture'
import { noneLocalReader } from '../localExecutionSequenceFixture'
import { RunCockpitView } from './RunCockpitView'

// The local-commit and abandonment panels are siblings in the cockpit's collaboration column. Their forms and drafts belong to the
// current run, so an ordinary parent update must keep them; only a run replacement, an ineligible or absent mode and an unmount end them.
// The run-scoped hooks and the children that are not under test are replaced; both panels, their hooks and their transports are real
// up to the four mocked clients, whose command methods must never be called here.
vi.mock('../hooks/useRunCockpit')
vi.mock('../hooks/useCollaborationTimeline')
vi.mock('../hooks/useAgentAttemptStatus')
vi.mock('../hooks/useRequestCodexPlanningAttempt')
vi.mock('../hooks/useRequestCodexPlanningRepairAttempt')
vi.mock('../hooks/useClaudeCriticalReviewAttemptStatus')
vi.mock('../hooks/useRequestClaudeCriticalReview')
vi.mock('../hooks/useRequestClaudeCriticalReviewRepairAttempt')
vi.mock('../hooks/useChallengeResolutionAttemptStatus')
vi.mock('../hooks/useRequestChallengeResolution')
vi.mock('../hooks/useRequestChallengeResolutionRepairAttempt')
vi.mock('../hooks/useImplementationAttemptStatus')
vi.mock('../hooks/useRequestImplementation')
vi.mock('../hooks/useCodeReviewAttemptStatus')
vi.mock('../hooks/useRequestCodeReview')
vi.mock('../hooks/useRequestCodeReviewRepairAttempt')
vi.mock('../hooks/useReviewCorrectionAttemptStatus')
vi.mock('../hooks/useRequestReviewCorrection')
vi.mock('../hooks/useAuthorizeReviewCorrection')
vi.mock('../hooks/useVerificationDiagnosisStatus')
vi.mock('../hooks/useRequestVerificationDiagnosis')
vi.mock('../hooks/useRequestDiagnosisCorrection')
vi.mock('../hooks/usePlanningImplementationAuthorization')
vi.mock('../hooks/useAuthorizePlanningImplementation')
vi.mock('../hooks/usePlanningSequence')
vi.mock('../hooks/useAgentSupervision')
vi.mock('./AgentCollaboration', () => ({ AgentCollaboration: () => null }))
vi.mock('./UsageEvidenceRail', () => ({ UsageEvidenceRail: () => null }))
vi.mock('./LiveOutputDrawer', () => ({ LiveOutputDrawer: () => null }))
vi.mock('./CodexAssignmentPreferenceControl', () => ({ CodexAssignmentPreferenceControl: () => null }))
vi.mock('./ClaudeModelPreferenceControl', () => ({ ClaudeModelPreferenceControl: () => null }))
vi.mock('./ClaudeMutationTurnLimitControl', () => ({ ClaudeMutationTurnLimitControl: () => null }))
vi.mock('./TokenWarningPanel', () => ({ TokenWarningPanel: () => null }))
vi.mock('./TokenStopPanel', () => ({ TokenStopPanel: () => null }))
vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localCommitStatusClient: vi.fn(),
  requestLocalCommitClient: vi.fn(),
  manualRunAbandonmentClient: vi.fn(),
  abandonManualRunClient: vi.fn(),
}))

const budget = {
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
  agentClaimPathTimeFits: ['CodexPlanning', 'ClaudeCriticalReview', 'ChallengeResolution', 'Implementation', 'CodeReview', 'ReviewCorrection'].map(
    (claimPath) => new AgentClaimPathTimeFitResponse({ claimPath, fit: 'Fits' }),
  ),
}

let projection: Record<string, unknown> = {}

function cockpitOf(runId: string) {
  return new GetRunCockpitResponse({
    runId,
    executionMode: 'ManualAgent',
    projectId: 'project',
    projectName: 'Project',
    executionNumber: 1,
    objective: `Objective of ${runId}`,
    lifecycle: 'Running',
    stage: 'Implement',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 5,
    latestSequence: 7,
    stageMap: [],
    canPause: false,
    canStop: false,
    ...budget,
    ...projection,
  } as ConstructorParameters<typeof GetRunCockpitResponse>[0])
}

const idle = () => ({ status: null, loading: false, error: null, refresh: vi.fn() })
const idleRequest = () => ({ requesting: false, error: null, request: vi.fn() })

function installHooks() {
  vi.mocked(useRunCockpitModule.useRunCockpit).mockImplementation(
    (runId: string | null) =>
      ({ cockpit: runId ? cockpitOf(runId) : null, cards: [], connection: 'live', loading: false, error: null, syncError: null, refresh: async () => true }) as ReturnType<
        typeof useRunCockpitModule.useRunCockpit
      >,
  )
  vi.mocked(useCollaborationTimelineModule.useCollaborationTimeline).mockReturnValue({ cards: [], loading: false, error: null, hasSuccessfulResponse: true })
  vi.mocked(useAgentSupervisionModule.useAgentSupervision).mockReturnValue(resultFor(null, { loading: true, polling: true }))
  vi.mocked(usePlanningSequenceModule.usePlanningSequence).mockImplementation((runId) => hookResultFor(armableStatusFor(runId ?? 'run-1'), runId ?? 'run-1'))
  vi.mocked(useAgentAttemptStatusModule.useAgentAttemptStatus).mockReturnValue(idle())
  vi.mocked(useClaudeCriticalReviewAttemptStatusModule.useClaudeCriticalReviewAttemptStatus).mockReturnValue(idle())
  vi.mocked(useChallengeResolutionAttemptStatusModule.useChallengeResolutionAttemptStatus).mockReturnValue(idle())
  vi.mocked(useImplementationAttemptStatusModule.useImplementationAttemptStatus).mockReturnValue(idle())
  vi.mocked(useCodeReviewAttemptStatusModule.useCodeReviewAttemptStatus).mockReturnValue(idle())
  vi.mocked(useReviewCorrectionAttemptStatusModule.useReviewCorrectionAttemptStatus).mockReturnValue(idle())
  vi.mocked(useVerificationDiagnosisStatusModule.useVerificationDiagnosisStatus).mockReturnValue(idle())
  vi.mocked(usePlanningImplementationAuthorizationModule.usePlanningImplementationAuthorization).mockReturnValue({
    authorization: null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  })
  vi.mocked(useRequestCodexPlanningAttemptModule.useRequestCodexPlanningAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestCodexPlanningRepairAttemptModule.useRequestCodexPlanningRepairAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestClaudeCriticalReviewModule.useRequestClaudeCriticalReview).mockReturnValue(idleRequest())
  vi.mocked(useRequestClaudeCriticalReviewRepairAttemptModule.useRequestClaudeCriticalReviewRepairAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestChallengeResolutionModule.useRequestChallengeResolution).mockReturnValue(idleRequest())
  vi.mocked(useRequestChallengeResolutionRepairAttemptModule.useRequestChallengeResolutionRepairAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestImplementationModule.useRequestImplementation).mockReturnValue(idleRequest())
  vi.mocked(useRequestCodeReviewModule.useRequestCodeReview).mockReturnValue(idleRequest())
  vi.mocked(useRequestCodeReviewRepairAttemptModule.useRequestCodeReviewRepairAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestReviewCorrectionModule.useRequestReviewCorrection).mockReturnValue(idleRequest())
  vi.mocked(useRequestVerificationDiagnosisModule.useRequestVerificationDiagnosis).mockReturnValue(idleRequest())
  vi.mocked(useRequestDiagnosisCorrectionModule.useRequestDiagnosisCorrection).mockReturnValue(idleRequest())
  vi.mocked(useAuthorizeReviewCorrectionModule.useAuthorizeReviewCorrection).mockReturnValue({ authorizing: false, error: null, authorize: vi.fn().mockResolvedValue(true) })
  vi.mocked(useAuthorizePlanningImplementationModule.useAuthorizePlanningImplementation).mockReturnValue({
    authorizing: false,
    error: null,
    authorize: vi.fn().mockResolvedValue(true),
  })
}

const eligibleCommit = () =>
  new GetLocalCommitStatusResponse({
    eligible: true,
    checkpointId: 'checkpoint-a',
    checkpointNumber: 3,
    codeReviewAttemptId: 'review-a',
    humanCheckpointReviewId: 'human-a',
    latestEventSequence: 7,
  })
const eligibleAbandonment = () => new GetManualRunAbandonmentResponse({ eligible: true, latestEventSequence: 7 })

const calls = {
  commitStatus: vi.fn(),
  abandonStatus: vi.fn(),
  commit: vi.fn(),
  abandon: vi.fn(),
}

function installClients() {
  vi.mocked(localCommitStatusClient).mockReturnValue({
    getLocalCommitStatus: vi.fn((runId: string) => {
      calls.commitStatus(runId)
      return Promise.resolve(eligibleCommit())
    }),
  } as unknown as ReturnType<typeof localCommitStatusClient>)
  vi.mocked(manualRunAbandonmentClient).mockReturnValue({
    getManualRunAbandonment: vi.fn((runId: string) => {
      calls.abandonStatus(runId)
      return Promise.resolve(eligibleAbandonment())
    }),
  } as unknown as ReturnType<typeof manualRunAbandonmentClient>)
  vi.mocked(requestLocalCommitClient).mockReturnValue({ requestLocalCommit: calls.commit } as unknown as ReturnType<typeof requestLocalCommitClient>)
  vi.mocked(abandonManualRunClient).mockReturnValue({ abandonManualRun: calls.abandon } as unknown as ReturnType<typeof abandonManualRunClient>)
}

// Observes the console without suppressing it: every diagnostic still reaches the original, and only duplicate-key ones are inspected here.
function observeConsole() {
  const spy = vi.spyOn(console, 'error')
  return { duplicateKeys: () => spy.mock.calls.map((call) => call.map(String).join(' ')).filter((message) => /two children with the same key/i.test(message)) }
}

const cockpitView = (runId: string, props: { onProjectChanged?: () => void; evidenceRefreshGeneration?: number } = {}) => (
  <RunCockpitView
    runId={runId}
    localExecutionSequence={noneLocalReader(runId)}
    executionCapacity={availableCapacityState()}
    workspaceExecution={idleStateFor('project')}
    {...props}
  />
)

const commitRegion = () => screen.getByRole('region', { name: 'Local commit' })
const abandonRegion = () => screen.getByRole('region', { name: 'Abandon run' })
const messageBox = () => within(commitRegion()).getByLabelText('Commit message') as HTMLTextAreaElement
const reasonBox = () => within(abandonRegion()).getByLabelText('Reason') as HTMLTextAreaElement

async function renderWithDrafts(runId = 'run-1') {
  const view = render(cockpitView(runId))
  await waitFor(() => expect(within(commitRegion()).queryByText('Reading the local-commit status…')).toBeNull())
  await waitFor(() => expect(within(abandonRegion()).queryByText('Reading the abandonment status…')).toBeNull())
  fireEvent.change(messageBox(), { target: { value: 'A draft commit message' } })
  fireEvent.change(reasonBox(), { target: { value: 'A draft abandonment reason' } })
  return { view, message: messageBox(), reason: reasonBox() }
}

// The same form instances, still holding their drafts, exactly one of each panel, no added status read and no command.
function expectFormsPreserved(forms: { message: HTMLTextAreaElement; reason: HTMLTextAreaElement }, reads: { commit: number; abandon: number }) {
  expect(screen.getAllByRole('region', { name: 'Local commit' })).toHaveLength(1)
  expect(screen.getAllByRole('region', { name: 'Abandon run' })).toHaveLength(1)
  expect(messageBox()).toBe(forms.message)
  expect(reasonBox()).toBe(forms.reason)
  expect(messageBox().value).toBe('A draft commit message')
  expect(reasonBox().value).toBe('A draft abandonment reason')
  expect(calls.commitStatus).toHaveBeenCalledTimes(reads.commit)
  expect(calls.abandonStatus).toHaveBeenCalledTimes(reads.abandon)
  expect(calls.commit).not.toHaveBeenCalled()
  expect(calls.abandon).not.toHaveBeenCalled()
}

describe('the cockpit keeps its local-commit and abandonment forms across ordinary parent updates', () => {
  let console_: ReturnType<typeof observeConsole>

  beforeEach(() => {
    vi.restoreAllMocks()
    vi.clearAllMocks()
    projection = {}
    console_ = observeConsole()
    installHooks()
    installClients()
  })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  it('keeps both eligible drafts and form instances through stable-run parent updates, and asks and sends nothing', async () => {
    const { view, message, reason } = await renderWithDrafts()
    const reads = { commit: calls.commitStatus.mock.calls.length, abandon: calls.abandonStatus.mock.calls.length }

    view.rerender(cockpitView('run-1', { onProjectChanged: () => undefined }))
    expectFormsPreserved({ message, reason }, reads)

    projection = { objective: 'The same run after an ordinary projection refresh' }
    view.rerender(cockpitView('run-1', { onProjectChanged: vi.fn() }))
    expect(await screen.findByText('The same run after an ordinary projection refresh')).toBeInTheDocument()
    expectFormsPreserved({ message, reason }, reads)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('keeps both drafts while the run lifecycle changes the Agent action siblings before them, and reads nothing for it', async () => {
    const { view, message, reason } = await renderWithDrafts()
    const reads = { commit: calls.commitStatus.mock.calls.length, abandon: calls.abandonStatus.mock.calls.length }

    projection = { lifecycle: 'Abandoned' }
    view.rerender(cockpitView('run-1'))
    expect(await screen.findByText(/This run was abandoned, so no further requests are available for it/)).toBeInTheDocument()
    expectFormsPreserved({ message, reason }, reads)

    projection = { lifecycle: 'Running' }
    view.rerender(cockpitView('run-1'))
    await waitFor(() => expect(screen.queryByText(/This run was abandoned, so no further requests are available for it/)).toBeNull())
    expectFormsPreserved({ message, reason }, reads)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('ends both forms when the manual mode ends and starts them cold when it returns, each read once more', async () => {
    const { view } = await renderWithDrafts()
    const reads = { commit: calls.commitStatus.mock.calls.length, abandon: calls.abandonStatus.mock.calls.length }

    projection = { executionMode: 'Simulated' }
    view.rerender(cockpitView('run-1'))
    expect(screen.queryByRole('region', { name: 'Local commit' })).toBeNull()
    expect(screen.queryByRole('region', { name: 'Abandon run' })).toBeNull()

    projection = {}
    view.rerender(cockpitView('run-1'))
    await waitFor(() => expect(messageBox()).toBeInTheDocument())
    await waitFor(() => expect(reasonBox()).toBeInTheDocument())
    expect(messageBox().value).toBe('')
    expect(reasonBox().value).toBe('')
    expect(calls.commitStatus).toHaveBeenCalledTimes(reads.commit + 1)
    expect(calls.abandonStatus).toHaveBeenCalledTimes(reads.abandon + 1)
    expect(calls.commit).not.toHaveBeenCalled()
    expect(calls.abandon).not.toHaveBeenCalled()
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('ends both drafts on a run replacement and an A to B to A return, and each successor reads and opens cold', async () => {
    const { view } = await renderWithDrafts('run-1')

    view.rerender(cockpitView('run-2'))
    await waitFor(() => expect(screen.getByText('Objective of run-2')).toBeInTheDocument())
    await waitFor(() => expect(messageBox()).toBeInTheDocument())
    await waitFor(() => expect(reasonBox()).toBeInTheDocument())
    expect(messageBox().value).toBe('')
    expect(reasonBox().value).toBe('')
    expect(calls.commitStatus.mock.calls.map(([runId]) => runId)).toEqual(['run-1', 'run-2'])
    expect(calls.abandonStatus.mock.calls.map(([runId]) => runId)).toEqual(['run-1', 'run-2'])

    fireEvent.change(messageBox(), { target: { value: 'Run two draft' } })
    view.rerender(cockpitView('run-1'))
    await waitFor(() => expect(screen.getByText('Objective of run-1')).toBeInTheDocument())
    await waitFor(() => expect(messageBox()).toBeInTheDocument())
    expect(messageBox().value).toBe('')
    expect(calls.commitStatus.mock.calls.map(([runId]) => runId)).toEqual(['run-1', 'run-2', 'run-1'])
    expect(calls.commit).not.toHaveBeenCalled()
    expect(calls.abandon).not.toHaveBeenCalled()
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('starts no read after unmount and lets a pending status read change nothing', async () => {
    let release!: (status: GetLocalCommitStatusResponse) => void
    const pending = new Promise<GetLocalCommitStatusResponse>((resolve) => {
      release = resolve
    })
    vi.mocked(localCommitStatusClient).mockReturnValue({
      getLocalCommitStatus: vi.fn((runId: string) => {
        calls.commitStatus(runId)
        return pending
      }),
    } as unknown as ReturnType<typeof localCommitStatusClient>)
    const view = render(cockpitView('run-1'))
    await waitFor(() => expect(calls.commitStatus).toHaveBeenCalledTimes(1))

    view.unmount()
    await act(async () => release(eligibleCommit()))

    expect(calls.commitStatus).toHaveBeenCalledTimes(1)
    expect(calls.commit).not.toHaveBeenCalled()
    expect(console_.duplicateKeys()).toEqual([])
  })
})
