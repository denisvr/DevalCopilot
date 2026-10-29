// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  checkpointApprovalEvidenceClient,
  localCommitStatusClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  projectWorkspaceClient,
  recordCheckpointReviewClient,
  requestLocalCommitClient,
} from '../../../api/clients'
import {
  AgentClaimPathTimeFitResponse,
  AgentInvocationTimeBudgetResponse,
  ApiException,
  CheckpointApprovalEvidenceMemberResponse,
  CheckpointApprovalEvidenceResponse,
  GetLocalCommitStatusResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { useEvidenceRefreshConnection } from '../hooks/useEvidenceRefreshConnection'
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
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'
import { RunCockpitView } from './RunCockpitView'

// The composition App performs, over the real components and hooks: one project-and-run-owned evidence-refresh connection, the
// candidate workspace's complete-set approval on one side and the run cockpit's local-commit status and form on the other. Only the
// generated clients, the run-scoped hooks that are not under test and the children that fetch on their own are replaced. The status
// the host answers decides everything; the approval is a notification and never an eligibility.
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
  projectWorkspaceClient: vi.fn(),
  projectGitEvidenceClient: vi.fn(),
  projectVerificationCommandsClient: vi.fn(),
  projectVerificationExecutionsClient: vi.fn(),
  projectCheckpointReviewsClient: vi.fn(),
  checkpointApprovalEvidenceClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
  localCommitStatusClient: vi.fn(),
  requestLocalCommitClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

interface CompositionProps {
  projectId: string
  runId: string
  // Withholds the local-commit request the way an unproven host capacity does; the status read itself is unaffected.
  withheld?: boolean
}

function Composition({ projectId, runId, withheld = false }: CompositionProps) {
  const refresh = useEvidenceRefreshConnection(projectId, runId)
  return (
    <>
      <CandidateWorkspacePanel
        key={projectId}
        projectId={projectId}
        onEvidenceRefreshRequested={refresh.request}
        localExecutionSequence={noneLocalReader()}
        executionCapacity={availableCapacityState()}
        workspaceExecution={idleStateFor(projectId)}
      />
      <RunCockpitView
        key={runId}
        runId={runId}
        evidenceRefreshGeneration={refresh.generation}
        localExecutionSequence={noneLocalReader(runId)}
        executionCapacity={withheld ? null : availableCapacityState()}
        workspaceExecution={idleStateFor('project')}
      />
    </>
  )
}

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

const fingerprintOf = (projectId: string) => `${projectId}-fingerprint`.padEnd(64, '0')
const checkpointOf = (projectId: string) => `${projectId}-checkpoint`

const bundleOf = (projectId: string) =>
  new CheckpointApprovalEvidenceResponse({
    projectId,
    workspaceId: `${projectId}-workspace`,
    checkpointId: checkpointOf(projectId),
    checkpointNumber: 2,
    fingerprintSha256: fingerprintOf(projectId),
    members: [1, 2].map(
      (number) =>
        new CheckpointApprovalEvidenceMemberResponse({
          verificationCommandId: `${projectId}-command-${number}`,
          commandNumber: number,
          recipeLabel: number === 1 ? 'Backend tests' : 'Lint',
          verificationExecutionId: `${projectId}-execution-${number}`,
          executionNumber: number + 10,
        }),
    ),
  })

const executionOf = (projectId: string, number: number) =>
  new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-execution-${number}`,
    verificationCommandId: `${projectId}-command-${number}`,
    executionNumber: number + 10,
    gitCheckpointId: checkpointOf(projectId),
    checkpointFingerprintSha256: fingerprintOf(projectId),
    status: 'Passed',
    isDispatched: true,
    hasStandardOutput: false,
    hasStandardError: false,
  })

const notEligible = () => new GetLocalCommitStatusResponse({ eligible: false, refusalCode: 'local_commit.human_approval_missing', latestEventSequence: 7 })
const eligible = (marker = 'a') =>
  new GetLocalCommitStatusResponse({
    eligible: true,
    checkpointId: `checkpoint-${marker}`,
    checkpointNumber: 2,
    codeReviewAttemptId: `review-${marker}`,
    humanCheckpointReviewId: `human-${marker}`,
    latestEventSequence: 7,
  })

type StatusRead = () => Promise<GetLocalCommitStatusResponse>

// Every host call, in the order the composition made it, so a status read can be shown to follow the approval it reacts to.
const order: string[] = []
const statusReads = vi.fn<(runId: string) => void>()
const record = vi.fn<(projectId: string, request: unknown) => Promise<unknown>>()
const commit = vi.fn()
const reviewReads = vi.fn<(projectId: string) => void>()
// The answer each read of a run gets: the next queued one, else the standing one.
const queued = new Map<string, StatusRead[]>()
const standing = new Map<string, StatusRead>()

const answerWith = (runId: string, read: StatusRead) => standing.set(runId, read)
const queueFor = (runId: string, ...reads: StatusRead[]) => queued.set(runId, reads)
const readsOf = (runId: string) => statusReads.mock.calls.filter(([id]) => id === runId).length

function installClients() {
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({
    getProjectGitEvidence: vi.fn((projectId: string) =>
      Promise.resolve(
        new GetProjectGitEvidenceResponse({
          gitWorkspaceId: `${projectId}-workspace`,
          checkpointId: checkpointOf(projectId),
          checkpointNumber: 2,
          changedFileCount: 0,
          headCommitSha: 'a'.repeat(40),
          fingerprintSha256: fingerprintOf(projectId),
        }),
      ),
    ),
  } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn((projectId: string) =>
      Promise.resolve([
        new VerificationCommandResponse({
          verificationCommandId: `${projectId}-command-1`,
          commandNumber: 1,
          name: 'Backend tests',
          executablePath: String.raw`C:\tools\verify.exe`,
          arguments: ['check'],
          timeoutSeconds: 300,
          isEnabled: true,
        }),
      ]),
    ),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({
    getProjectVerificationExecutions: vi.fn((projectId: string) => Promise.resolve([executionOf(projectId, 1), executionOf(projectId, 2)])),
  } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({
    getProjectCheckpointReviews: vi.fn((projectId: string) => {
      reviewReads(projectId)
      return Promise.resolve([])
    }),
  } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  vi.mocked(checkpointApprovalEvidenceClient).mockReturnValue({
    getCheckpointApprovalEvidence: vi.fn((projectId: string) => Promise.resolve(bundleOf(projectId))),
  } as unknown as ReturnType<typeof checkpointApprovalEvidenceClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview: record } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(localCommitStatusClient).mockReturnValue({
    getLocalCommitStatus: vi.fn((runId: string) => {
      statusReads(runId)
      order.push(`status:${runId}`)
      const next = queued.get(runId)?.shift() ?? standing.get(runId)
      return next ? next() : Promise.resolve(notEligible())
    }),
  } as unknown as ReturnType<typeof localCommitStatusClient>)
  vi.mocked(requestLocalCommitClient).mockReturnValue({ requestLocalCommit: commit } as unknown as ReturnType<typeof requestLocalCommitClient>)
}

const reviewRegion = () => screen.getByRole('region', { name: 'Checkpoint review evidence' })
const commitRegion = () => screen.getByRole('region', { name: 'Local commit' })
const approveAll = () => within(reviewRegion()).getByRole('button', { name: 'Approve all enabled checks' })
const refreshButton = () => screen.getByRole('button', { name: 'Refresh evidence' })
const commitForm = () => within(commitRegion()).queryByLabelText('Commit message')
const commitButton = () => within(commitRegion()).queryByRole('button', { name: 'Commit locally' })

async function renderSettled(props: CompositionProps = { projectId: 'project-a', runId: 'run-1' }) {
  const view = render(<Composition {...props} />)
  await waitFor(() => expect(approveAll()).toBeEnabled())
  await waitFor(() => expect(within(commitRegion()).queryByText('Reading the local-commit status…')).toBeNull())
  await waitFor(() => expect(readsOf(props.runId)).toBeGreaterThanOrEqual(1))
  return view
}

const flush = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)) })

function expectNoCommitStarted() {
  expect(commit).not.toHaveBeenCalled()
  expect(commitForm()).toBeNull()
  expect(commitButton()).toBeNull()
}

describe('the complete-set approval requests the current run\'s local-commit status without a manual Refresh evidence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    order.length = 0
    queued.clear()
    standing.clear()
    record.mockImplementation((projectId) => {
      order.push(`approve:${projectId}`)
      return Promise.resolve({ reviewId: 'new-review', decision: 'Approved' })
    })
    installHooks()
    installClients()
  })

  it('reads the status again after the accepted approval and offers the commit form only from the eligible answer', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    expectNoCommitStarted()
    expect(within(commitRegion()).getByText('No human approval of this checkpoint was found.')).toBeInTheDocument()
    const readsBefore = readsOf('run-1')
    const reviewReadsBefore = reviewReads.mock.calls.length
    answerWith('run-1', () => Promise.resolve(eligible()))

    fireEvent.click(approveAll())

    await waitFor(() => expect(commitForm()).not.toBeNull())
    expect(record).toHaveBeenCalledTimes(1)
    expect(readsOf('run-1')).toBe(readsBefore + 1)
    expect(order.slice(-2)).toEqual(['approve:project-a', 'status:run-1'])
    await waitFor(() => expect(reviewReads.mock.calls.length).toBe(reviewReadsBefore + 1))
    expect(commitButton()).toBeDisabled()
    expect(within(reviewRegion()).getByText('Recorded. The Human approval covers these runs.')).toBeInTheDocument()
    expect(commit).not.toHaveBeenCalled()
    await flush()
    expect(readsOf('run-1')).toBe(readsBefore + 1)
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('offers no commit form while the follow-up read is pending, then only from its eligible answer', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const followUp = deferred<GetLocalCommitStatusResponse>()
    const readsBefore = readsOf('run-1')
    answerWith('run-1', () => followUp.promise)

    fireEvent.click(approveAll())

    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 1))
    await flush()
    expectNoCommitStarted()
    await act(async () => followUp.resolve(eligible()))
    await waitFor(() => expect(commitForm()).not.toBeNull())
    expect(commit).not.toHaveBeenCalled()
  })

  it('keeps the accepted approval accepted and offers nothing when the follow-up read fails, with no retry and no second write', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const readsBefore = readsOf('run-1')
    answerWith('run-1', () => Promise.reject(new Error('host unavailable')))

    fireEvent.click(approveAll())

    expect(await within(commitRegion()).findByText(/could not be read/)).toBeInTheDocument()
    expectNoCommitStarted()
    expect(within(reviewRegion()).getByText('Recorded. The Human approval covers these runs.')).toBeInTheDocument()
    expect(within(reviewRegion()).queryByText(/approval failed|could not be recorded/i)).toBeNull()
    await flush()
    expect(readsOf('run-1')).toBe(readsBefore + 1)
    expect(record).toHaveBeenCalledTimes(1)
    expect(approveAll()).toBeDisabled()
  })

  it('offers nothing when the fresh answer is still ineligible, incoherent or withheld', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const readsBefore = readsOf('run-1')
    answerWith('run-1', () => Promise.resolve(notEligible()))

    fireEvent.click(approveAll())

    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 1))
    await flush()
    expectNoCommitStarted()
    expect(within(commitRegion()).getByText('No human approval of this checkpoint was found.')).toBeInTheDocument()
  })

  it('offers nothing for an eligible answer that does not name its whole approval identity', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const readsBefore = readsOf('run-1')
    answerWith('run-1', () => Promise.resolve(new GetLocalCommitStatusResponse({ eligible: true, checkpointId: 'checkpoint-a', codeReviewAttemptId: 'review-a', latestEventSequence: 7 })))

    fireEvent.click(approveAll())

    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 1))
    await flush()
    expectNoCommitStarted()
  })

  it('offers no form from an eligible answer while the cockpit withholds the request', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled({ projectId: 'project-a', runId: 'run-1', withheld: true })
    const readsBefore = readsOf('run-1')
    answerWith('run-1', () => Promise.resolve(eligible()))

    fireEvent.click(approveAll())

    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 1))
    await flush()
    expectNoCommitStarted()
    expect(within(commitRegion()).getByRole('status')).toBeInTheDocument()
  })

  it('never lets an older answer that settles after a newer one decide the form', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const older = deferred<GetLocalCommitStatusResponse>()
    const newer = deferred<GetLocalCommitStatusResponse>()
    const readsBefore = readsOf('run-1')
    queueFor('run-1', () => older.promise, () => newer.promise)

    fireEvent.click(approveAll())
    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 1))
    fireEvent.click(refreshButton())
    await waitFor(() => expect(readsOf('run-1')).toBe(readsBefore + 2))

    await act(async () => newer.resolve(eligible()))
    await waitFor(() => expect(commitForm()).not.toBeNull())
    await act(async () => older.resolve(notEligible()))

    expect(commitForm()).not.toBeNull()
    expect(commit).not.toHaveBeenCalled()
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('starts no status read and no second write after a refused approval', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    await renderSettled()
    const readsBefore = readsOf('run-1')
    const reviewReadsBefore = reviewReads.mock.calls.length
    record.mockImplementation(() => Promise.reject(new ApiException('raw', 409, JSON.stringify({ errors: [{ code: 'reviews.approval_requires_complete_verification_set', detail: 'host text' }] }), {}, null)))

    fireEvent.click(approveAll())

    await waitFor(() => expect(within(reviewRegion()).getByText(/Refresh evidence/, { selector: '.dc-candidate-workspace-error' })).toBeInTheDocument())
    await flush()
    expect(record).toHaveBeenCalledTimes(1)
    expect(readsOf('run-1')).toBe(readsBefore)
    expect(reviewReads.mock.calls.length).toBe(reviewReadsBefore)
    expectNoCommitStarted()
  })

  it('does not let an approval accepted under an earlier run of the same project refresh the replacement run', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    answerWith('run-2', () => Promise.resolve(notEligible()))
    const post = deferred<unknown>()
    const view = await renderSettled()
    record.mockImplementation(() => post.promise)
    fireEvent.click(approveAll())
    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))

    view.rerender(<Composition projectId="project-a" runId="run-2" />)
    await waitFor(() => expect(screen.getByText('Objective of run-2')).toBeInTheDocument())
    await waitFor(() => expect(readsOf('run-2')).toBe(1))
    await flush()
    const reads = { first: readsOf('run-1'), second: readsOf('run-2') }
    answerWith('run-2', () => Promise.resolve(eligible()))
    await act(async () => post.resolve({ reviewId: 'accepted', decision: 'Approved' }))
    await flush()

    expect({ first: readsOf('run-1'), second: readsOf('run-2') }).toEqual(reads)
    expectNoCommitStarted()
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('ends a pending approval at unmount: no status read, nothing started', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    const post = deferred<unknown>()
    const view = await renderSettled()
    record.mockImplementation(() => post.promise)
    fireEvent.click(approveAll())
    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    const reads = readsOf('run-1')

    view.unmount()
    await act(async () => post.resolve({ reviewId: 'accepted', decision: 'Approved' }))
    await flush()

    expect(readsOf('run-1')).toBe(reads)
    expect(commit).not.toHaveBeenCalled()
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('keeps A to B to A as separate lifetimes: an earlier approval and its late status answer never reach the later visit', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    answerWith('run-2', () => Promise.resolve(notEligible()))
    const post = deferred<unknown>()
    const view = await renderSettled()
    record.mockImplementation(() => post.promise)
    fireEvent.click(approveAll())
    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))

    view.rerender(<Composition projectId="project-b" runId="run-2" />)
    await waitFor(() => expect(screen.getByText('Objective of run-2')).toBeInTheDocument())
    await waitFor(() => expect(readsOf('run-2')).toBe(1))
    view.rerender(<Composition projectId="project-a" runId="run-1" />)
    await waitFor(() => expect(screen.getByText('Objective of run-1')).toBeInTheDocument())
    await waitFor(() => expect(within(commitRegion()).queryByText('Reading the local-commit status…')).toBeNull())
    await waitFor(() => expect(approveAll()).toBeEnabled())
    const reads = { first: readsOf('run-1'), second: readsOf('run-2') }
    expect(reads.first).toBe(2)

    answerWith('run-1', () => Promise.resolve(eligible()))
    await act(async () => post.resolve({ reviewId: 'accepted-by-the-first-visit', decision: 'Approved' }))
    await flush()

    expect({ first: readsOf('run-1'), second: readsOf('run-2') }).toEqual(reads)
    expectNoCommitStarted()
    expect(within(reviewRegion()).queryByText('Recorded. The Human approval covers these runs.')).toBeNull()
    expect(approveAll()).toBeEnabled()
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('ignores the late answer of a follow-up read that belongs to an earlier visit of the run', async () => {
    answerWith('run-1', () => Promise.resolve(notEligible()))
    answerWith('run-2', () => Promise.resolve(notEligible()))
    const view = await renderSettled()
    const lateFirstVisit = deferred<GetLocalCommitStatusResponse>()
    queueFor('run-1', () => lateFirstVisit.promise)
    fireEvent.click(approveAll())
    await waitFor(() => expect(readsOf('run-1')).toBe(2))

    view.rerender(<Composition projectId="project-b" runId="run-2" />)
    await waitFor(() => expect(screen.getByText('Objective of run-2')).toBeInTheDocument())
    view.rerender(<Composition projectId="project-a" runId="run-1" />)
    await waitFor(() => expect(screen.getByText('Objective of run-1')).toBeInTheDocument())
    await waitFor(() => expect(within(commitRegion()).queryByText('Reading the local-commit status…')).toBeNull())

    await act(async () => lateFirstVisit.resolve(eligible()))
    await flush()

    expectNoCommitStarted()
    expect(commit).not.toHaveBeenCalled()
  })
})
