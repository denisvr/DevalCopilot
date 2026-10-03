import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  claimVerificationExecutionClient,
  codeReviewAttemptStatusClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  projectWorkspaceClient,
  requestCodeReviewClient,
  requestDiagnosisCorrectionClient,
  requestVerificationDiagnosisClient,
  verificationDiagnosisStatusClient,
} from '../../../api/clients'
import {
  AgentClaimPathTimeFitResponse,
  AgentInvocationTimeBudgetResponse,
  ApiException,
  CodeReviewAttemptStatusResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  ReviewCorrectionAttemptStatusResponse,
  VerificationCommandResponse,
  VerificationDiagnosisStatusResponse,
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
import * as useRequestCodeReviewRepairAttemptModule from '../hooks/useRequestCodeReviewRepairAttempt'
import * as useReviewCorrectionAttemptStatusModule from '../hooks/useReviewCorrectionAttemptStatus'
import * as useRequestReviewCorrectionModule from '../hooks/useRequestReviewCorrection'
import * as useAuthorizeReviewCorrectionModule from '../hooks/useAuthorizeReviewCorrection'
import * as usePlanningImplementationAuthorizationModule from '../hooks/usePlanningImplementationAuthorization'
import * as useAuthorizePlanningImplementationModule from '../hooks/useAuthorizePlanningImplementation'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'
import { RunCockpitView } from './RunCockpitView'

// The run-scoped hooks whose reads are NOT under test, and the children that fetch on their own, are replaced; the two statuses that
// depend on verification (diagnosis, code review), their requests, the project evidence consumers and the connection are real.
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
vi.mock('../hooks/useRequestCodeReviewRepairAttempt')
vi.mock('../hooks/useReviewCorrectionAttemptStatus')
vi.mock('../hooks/useRequestReviewCorrection')
vi.mock('../hooks/useAuthorizeReviewCorrection')
vi.mock('../hooks/usePlanningImplementationAuthorization')
vi.mock('../hooks/useAuthorizePlanningImplementation')
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
  captureGitWorkspaceCheckpointClient: vi.fn(),
  gitCheckpointChangedFilesClient: vi.fn(),
  gitCheckpointDiffClient: vi.fn(),
  projectVerificationCommandsClient: vi.fn(),
  projectVerificationExecutionsClient: vi.fn(),
  claimVerificationExecutionClient: vi.fn(),
  projectCheckpointReviewsClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
  verificationDiagnosisStatusClient: vi.fn(),
  codeReviewAttemptStatusClient: vi.fn(),
  requestCodeReviewClient: vi.fn(),
  requestVerificationDiagnosisClient: vi.fn(),
  requestDiagnosisCorrectionClient: vi.fn(),
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

// The composition App performs: one project-and-run-owned connection, the candidate-workspace action on one side, the run on the other.
function Composition({ projectId, runId }: { projectId: string; runId: string }) {
  const refresh = useEvidenceRefreshConnection(projectId, runId)
  return (
    <>
      <CandidateWorkspacePanel key={projectId} projectId={projectId} onEvidenceRefreshRequested={refresh.request} />
      <RunCockpitView key={runId} runId={runId} evidenceRefreshGeneration={refresh.generation} />
    </>
  )
}

const UNAVAILABLE = 'verification_diagnosis.evidence_missing'
const NOT_PASSED = 'Enabled verification command \'Candidate total check\' has not Passed for the current checkpoint.'

interface RunServer {
  diagnosis: () => Promise<VerificationDiagnosisStatusResponse>
  review: () => Promise<CodeReviewAttemptStatusResponse>
  executions: VerificationExecutionResponse[]
}

const noDiagnosis = () => new VerificationDiagnosisStatusResponse({ hasAttempt: false, reviewableExecutionReportMessageId: 'report-1', diagnosisUnavailableCode: UNAVAILABLE })
const diagnosable = (marker = 'report-1') =>
  new VerificationDiagnosisStatusResponse({ hasAttempt: false, reviewableExecutionReportMessageId: marker, diagnosableExecutionReportMessageId: marker })
const noReview = () => new CodeReviewAttemptStatusResponse({ hasAttempt: false })

function execution(runId: string, status: string) {
  return new VerificationExecutionResponse({
    verificationExecutionId: `${runId}-execution`,
    verificationCommandId: `${runId}-command`,
    executionNumber: 1,
    gitCheckpointId: `${runId}-checkpoint`,
    checkpointFingerprintSha256: 'f'.repeat(64),
    status,
    isDispatched: true,
    hasStandardOutput: false,
    hasStandardError: false,
  })
}

const servers = new Map<string, RunServer>()
// The report the review-correction status names as reviewable: the historical fallback of the ordinary-review target.
let fallbackReportId: string | null = null
const calls = {
  diagnosisReads: vi.fn(),
  reviewReads: vi.fn(),
  claim: vi.fn(),
  capture: vi.fn(),
  requestDiagnosis: vi.fn(),
  requestCorrection: vi.fn(),
  requestReview: vi.fn(),
}

function serve(runId: string, server: Partial<RunServer> = {}) {
  servers.set(runId, { diagnosis: () => Promise.resolve(noDiagnosis()), review: () => Promise.resolve(noReview()), executions: [], ...server })
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
  // Every claim path fits, so no request is incidentally withheld by the advisory time-fit derivation.
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

function installMocks() {
  vi.mocked(useRunCockpitModule.useRunCockpit).mockImplementation(
    (runId: string | null) =>
      ({ cockpit: runId ? cockpitOf(runId) : null, cards: [], connection: 'live', loading: false, error: null, syncError: null, refresh: async () => true }) as ReturnType<
        typeof useRunCockpitModule.useRunCockpit
      >,
  )
  vi.mocked(useCollaborationTimelineModule.useCollaborationTimeline).mockReturnValue({ cards: [], loading: false, error: null, hasSuccessfulResponse: true })
  vi.mocked(useAgentAttemptStatusModule.useAgentAttemptStatus).mockReturnValue(idle())
  vi.mocked(useClaudeCriticalReviewAttemptStatusModule.useClaudeCriticalReviewAttemptStatus).mockReturnValue(idle())
  vi.mocked(useChallengeResolutionAttemptStatusModule.useChallengeResolutionAttemptStatus).mockReturnValue(idle())
  vi.mocked(useImplementationAttemptStatusModule.useImplementationAttemptStatus).mockReturnValue(idle())
  vi.mocked(useReviewCorrectionAttemptStatusModule.useReviewCorrectionAttemptStatus).mockImplementation(() => ({
    status: fallbackReportId ? new ReviewCorrectionAttemptStatusResponse({ reviewableExecutionReportMessageId: fallbackReportId }) : null,
    loading: false,
    error: null,
    refresh: vi.fn(),
  }))
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
  vi.mocked(useRequestCodeReviewRepairAttemptModule.useRequestCodeReviewRepairAttempt).mockReturnValue(idleRequest())
  vi.mocked(useRequestReviewCorrectionModule.useRequestReviewCorrection).mockReturnValue(idleRequest())
  vi.mocked(useAuthorizeReviewCorrectionModule.useAuthorizeReviewCorrection).mockReturnValue({ authorizing: false, error: null, authorize: vi.fn().mockResolvedValue(true) })
  vi.mocked(useAuthorizePlanningImplementationModule.useAuthorizePlanningImplementation).mockReturnValue({
    authorizing: false,
    error: null,
    authorize: vi.fn().mockResolvedValue(true),
  })

  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({
    getProjectGitEvidence: vi.fn((projectId: string) =>
      Promise.resolve(new GetProjectGitEvidenceResponse({ checkpointId: `${projectId}-checkpoint`, checkpointNumber: 2, changedFileCount: 1, fingerprintSha256: 'f'.repeat(64) })),
    ),
  } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint: calls.capture } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution: calls.claim } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn((projectId: string) =>
      Promise.resolve([
        new VerificationCommandResponse({
          verificationCommandId: `${projectId}-command`,
          commandNumber: 1,
          name: 'Candidate total check',
          executablePath: String.raw`C:\tools\verify.exe`,
          arguments: ['check'],
          timeoutSeconds: 300,
          isEnabled: true,
        }),
      ]),
    ),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({
    getProjectVerificationExecutions: vi.fn((projectId: string) => Promise.resolve(servers.get(`run-${projectId.replace('project-', '')}`)?.executions ?? [])),
  } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  vi.mocked(verificationDiagnosisStatusClient).mockReturnValue({
    getVerificationDiagnosisStatus: vi.fn((runId: string) => {
      calls.diagnosisReads(runId)
      return servers.get(runId)!.diagnosis()
    }),
  } as unknown as ReturnType<typeof verificationDiagnosisStatusClient>)
  vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({
    getCodeReviewAttemptStatus: vi.fn((runId: string) => {
      calls.reviewReads(runId)
      return servers.get(runId)!.review()
    }),
  } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
  vi.mocked(requestCodeReviewClient).mockReturnValue({ requestCodeReview: calls.requestReview } as unknown as ReturnType<typeof requestCodeReviewClient>)
  vi.mocked(requestVerificationDiagnosisClient).mockReturnValue({ requestVerificationDiagnosis: calls.requestDiagnosis } as unknown as ReturnType<typeof requestVerificationDiagnosisClient>)
  vi.mocked(requestDiagnosisCorrectionClient).mockReturnValue({ requestDiagnosisCorrection: calls.requestCorrection } as unknown as ReturnType<typeof requestDiagnosisCorrectionClient>)
}

const refreshButton = () => screen.getByRole('button', { name: 'Refresh evidence' })
const diagnosisRegion = () => screen.getByRole('region', { name: 'Verification diagnosis' })
const reviewRegion = () => screen.getByRole('region', { name: 'Code review' })
const diagnoseButton = () => within(diagnosisRegion()).queryByRole('button', { name: 'Diagnose failed verification with Codex' })

async function renderReady(projectId = 'project-1', runId = 'run-1') {
  const view = render(<Composition projectId={projectId} runId={runId} />)
  await waitFor(() => expect(screen.getByRole('region', { name: 'Source evidence' })).toHaveTextContent('Checkpoint #2'))
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Verification commands' })).getByText('#1 Candidate total check')).toBeInTheDocument())
  await waitFor(() => expect(diagnosisRegion()).toBeInTheDocument())
  return view
}

describe('Refresh evidence reaches the run\'s verification-dependent display', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    servers.clear()
    fallbackReportId = null
    installMocks()
  })

  it('makes the diagnosis available from the fresh server status after a failed verification, with reads only', async () => {
    let server: VerificationDiagnosisStatusResponse = noDiagnosis()
    serve('run-1', { diagnosis: () => Promise.resolve(server) })
    await renderReady()
    expect(within(diagnosisRegion()).getByText(/Verification has not been run for the current source/)).toBeInTheDocument()
    expect(diagnoseButton()).toBeNull()
    const diagnosisReads = calls.diagnosisReads.mock.calls.length
    const reviewReads = calls.reviewReads.mock.calls.length

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()), executions: [execution('run-1', 'Failed')] })
    server = diagnosable()
    fireEvent.click(refreshButton())

    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    expect(within(diagnosisRegion()).queryByText(/Verification has not been run/)).not.toBeInTheDocument()
    expect(calls.diagnosisReads.mock.calls.length).toBe(diagnosisReads + 1)
    expect(calls.reviewReads.mock.calls.length).toBe(reviewReads + 1)
    for (const mutation of [calls.requestDiagnosis, calls.requestCorrection, calls.requestReview, calls.claim, calls.capture]) {
      expect(mutation).not.toHaveBeenCalled()
    }
  })

  it('updates the ordinary-review display after a passed verification: the obsolete refusal goes, the request is not made for the operator', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()), executions: [execution('run-1', 'Failed')] })
    calls.requestReview.mockRejectedValueOnce(new ApiException('refused', 409, JSON.stringify({ errors: [{ detail: NOT_PASSED }] }), {}, null))
    await renderReady()
    fireEvent.click(within(reviewRegion()).getByRole('button', { name: 'Request code review' }))
    expect(await within(reviewRegion()).findByText(NOT_PASSED)).toBeInTheDocument()

    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()), executions: [execution('run-1', 'Passed')] })
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(reviewRegion()).queryByText(NOT_PASSED)).not.toBeInTheDocument())
    expect(within(reviewRegion()).getByRole('button', { name: 'Request code review' })).toBeEnabled()
    expect(calls.requestReview).toHaveBeenCalledTimes(1)
  })

  it('keeps diagnosis and review requests unavailable while the refresh is pending, then offers them from the fresh answer', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    await renderReady()
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    const gate = deferred<VerificationDiagnosisStatusResponse>()
    serve('run-1', { diagnosis: () => gate.promise })

    fireEvent.click(refreshButton())

    await waitFor(() => expect(diagnoseButton()).toBeDisabled())
    expect(within(reviewRegion()).getByRole('button', { name: 'Request code review' })).toBeDisabled()
    await act(async () => gate.resolve(diagnosable('report-2')))
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    expect(within(reviewRegion()).getByRole('button', { name: 'Request code review' })).toBeEnabled()
    fireEvent.click(diagnoseButton()!)
    await waitFor(() => expect(calls.requestDiagnosis).toHaveBeenCalledTimes(1))
    expect(calls.requestDiagnosis.mock.calls[0][0]).toBe('run-1')
    expect(calls.requestDiagnosis.mock.calls[0][1].executionReportMessageId).toBe('report-2')
  })

  const unavailable = () => Promise.reject(new Error('unavailable'))
  // The review region itself is absent when no report is known to review; either way no request control may exist.
  const reviewButton = () => {
    const region = screen.queryByRole('region', { name: 'Code review' })
    return region ? within(region).queryByRole('button', { name: 'Request code review' }) : null
  }
  const noMutation = () => {
    for (const mutation of [calls.requestDiagnosis, calls.requestCorrection, calls.requestReview, calls.claim, calls.capture]) {
      expect(mutation).not.toHaveBeenCalled()
    }
  }

  it('fails closed on a failed code-review read alone: the diagnosis stays usable, no review is offered, and a successful refresh recovers', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    await renderReady()
    await waitFor(() => expect(reviewButton()).toBeEnabled())

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()), review: unavailable })
    fireEvent.click(refreshButton())

    expect(await within(reviewRegion()).findByText('Code review attempt status is unavailable.')).toBeInTheDocument()
    expect(reviewButton()).toBeNull()
    expect(diagnoseButton()).toBeEnabled()
    noMutation()

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    fireEvent.click(refreshButton())

    await waitFor(() => expect(reviewButton()).toBeEnabled())
    expect(within(reviewRegion()).queryByText('Code review attempt status is unavailable.')).not.toBeInTheDocument()
    noMutation()
  })

  it('fails closed on a failed diagnosis read alone, and never revives the older fallback report as freshly validated', async () => {
    fallbackReportId = 'fallback-report'
    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    await renderReady()
    await waitFor(() => expect(reviewButton()).toBeEnabled())

    serve('run-1', { diagnosis: unavailable })
    fireEvent.click(refreshButton())

    expect(await within(diagnosisRegion()).findByText('Verification diagnosis status is unavailable.')).toBeInTheDocument()
    expect(diagnoseButton()).toBeNull()
    expect(reviewButton()).toBeNull()
    noMutation()

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    fireEvent.click(refreshButton())

    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    await waitFor(() => expect(reviewButton()).toBeEnabled())
    expect(within(diagnosisRegion()).queryByText('Verification diagnosis status is unavailable.')).not.toBeInTheDocument()
    noMutation()
  })

  it('fails closed when both reads fail, with a populated fallback, and recovers on the next successful refresh', async () => {
    fallbackReportId = 'fallback-report'
    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    await renderReady()
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())

    serve('run-1', { diagnosis: unavailable, review: unavailable })
    fireEvent.click(refreshButton())

    expect(await within(diagnosisRegion()).findByText('Verification diagnosis status is unavailable.')).toBeInTheDocument()
    expect(diagnoseButton()).toBeNull()
    expect(reviewButton()).toBeNull()
    noMutation()

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    fireEvent.click(refreshButton())

    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    await waitFor(() => expect(reviewButton()).toBeEnabled())
    noMutation()
  })

  it('keeps the legitimate fallback: a successful diagnosis read that names no report leaves the review-correction report as the target', async () => {
    fallbackReportId = 'fallback-report'
    serve('run-1', { diagnosis: () => Promise.resolve(new VerificationDiagnosisStatusResponse({ hasAttempt: false, diagnosisUnavailableCode: UNAVAILABLE })) })
    calls.requestReview.mockResolvedValue({})
    await renderReady()
    await waitFor(() => expect(reviewButton()).toBeEnabled())

    fireEvent.click(refreshButton())
    await waitFor(() => expect(reviewButton()).toBeEnabled())
    fireEvent.click(reviewButton()!)

    await waitFor(() => expect(calls.requestReview).toHaveBeenCalledTimes(1))
    expect(calls.requestReview.mock.calls[0][0]).toBe('run-1')
    expect(calls.requestReview.mock.calls[0][1].executionReportMessageId).toBe('fallback-report')
  })

  it('preserves drafts, the mounted subtree and an accepted pending operation, and starts none itself', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable()) })
    const accepted = deferred<unknown>()
    calls.requestDiagnosis.mockReturnValue(accepted.promise)
    await renderReady()
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Draft name' } })
    fireEvent.click(diagnoseButton()!)
    await waitFor(() => expect(calls.requestDiagnosis).toHaveBeenCalledTimes(1))
    const sectionBefore = diagnosisRegion()

    fireEvent.click(refreshButton())
    await waitFor(() => expect(calls.diagnosisReads.mock.calls.length).toBeGreaterThanOrEqual(2))

    expect(screen.getByLabelText('Verification command name')).toHaveValue('Draft name')
    expect(diagnosisRegion()).toBe(sectionBefore)
    expect(calls.requestDiagnosis).toHaveBeenCalledTimes(1)
    await act(async () => accepted.resolve({}))
    expect(calls.requestDiagnosis).toHaveBeenCalledTimes(1)
  })

  it('applies only the newest of overlapping refreshes', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    await renderReady()
    const older = deferred<VerificationDiagnosisStatusResponse>()
    const newer = deferred<VerificationDiagnosisStatusResponse>()
    serve('run-1', { diagnosis: () => older.promise })
    const before = calls.diagnosisReads.mock.calls.length
    fireEvent.click(refreshButton())
    await waitFor(() => expect(calls.diagnosisReads.mock.calls.length).toBe(before + 1))
    serve('run-1', { diagnosis: () => newer.promise })
    fireEvent.click(refreshButton())
    await waitFor(() => expect(calls.diagnosisReads.mock.calls.length).toBe(before + 2))

    await act(async () => newer.resolve(diagnosable('newest')))
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
    await act(async () => older.resolve(noDiagnosis()))

    expect(diagnoseButton()).toBeEnabled()
    expect(within(diagnosisRegion()).queryByText(/Verification has not been run/)).not.toBeInTheDocument()
  })

  it('keeps a refresh with its project and run through replacement and A to B to A', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    serve('run-2', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    const lateA = deferred<VerificationDiagnosisStatusResponse>()
    const view = await renderReady('project-1', 'run-1')
    serve('run-1', { diagnosis: () => lateA.promise })
    fireEvent.click(refreshButton())
    await waitFor(() => expect(diagnoseButton()).toBeNull())

    view.rerender(<Composition projectId="project-2" runId="run-2" />)
    await waitFor(() => expect(screen.getByText('Objective of run-2')).toBeInTheDocument())
    await waitFor(() => expect(within(diagnosisRegion()).getByText(/Verification has not been run/)).toBeInTheDocument())
    await act(async () => lateA.resolve(diagnosable('late-a')))
    expect(diagnoseButton()).toBeNull()
    expect(screen.queryByText('Objective of run-1')).not.toBeInTheDocument()

    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    const readsBeforeReturn = calls.diagnosisReads.mock.calls.length
    view.rerender(<Composition projectId="project-1" runId="run-1" />)
    await waitFor(() => expect(screen.getByText('Objective of run-1')).toBeInTheDocument())
    await waitFor(() => expect(calls.diagnosisReads.mock.calls.length).toBe(readsBeforeReturn + 1)) // a fresh lifetime reads once; no inherited generation
    expect(diagnoseButton()).toBeNull()

    serve('run-1', { diagnosis: () => Promise.resolve(diagnosable('run-1-report')) })
    fireEvent.click(refreshButton())
    await waitFor(() => expect(diagnoseButton()).toBeEnabled())
  })

  it('starts the replacement run of the same project without the old run\'s generation, and ignores the old run\'s late answer', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    serve('run-3', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    const lateOld = deferred<VerificationDiagnosisStatusResponse>()
    const view = await renderReady('project-1', 'run-1')
    serve('run-1', { diagnosis: () => lateOld.promise })
    fireEvent.click(refreshButton())
    await waitFor(() => expect(diagnoseButton()).toBeNull())

    view.rerender(<Composition projectId="project-1" runId="run-3" />)
    await waitFor(() => expect(screen.getByText('Objective of run-3')).toBeInTheDocument())
    await waitFor(() => expect(within(diagnosisRegion()).getByText(/Verification has not been run/)).toBeInTheDocument())
    await act(async () => lateOld.resolve(diagnosable('late-old-run')))

    expect(diagnoseButton()).toBeNull()
    expect(calls.diagnosisReads.mock.calls.filter(([runId]) => runId === 'run-3')).toHaveLength(1)
  })

  it('starts no read after unmount, and an unmounted pending read changes nothing', async () => {
    serve('run-1', { diagnosis: () => Promise.resolve(noDiagnosis()) })
    const view = await renderReady()
    const late = deferred<VerificationDiagnosisStatusResponse>()
    serve('run-1', { diagnosis: () => late.promise })
    fireEvent.click(refreshButton())
    await waitFor(() => expect(diagnoseButton()).toBeNull())
    const reads = calls.diagnosisReads.mock.calls.length

    view.unmount()
    await act(async () => late.resolve(diagnosable('late')))

    expect(calls.diagnosisReads.mock.calls.length).toBe(reads)
  })
})
