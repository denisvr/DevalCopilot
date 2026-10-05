import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  claimVerificationExecutionClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  projectWorkspaceClient,
  recordCheckpointReviewClient,
} from '../../../api/clients'
import {
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  GitCheckpointChangedFileResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'

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

function evidenceOf(projectId: string, number: number) {
  return new GetProjectGitEvidenceResponse({
    checkpointId: `${projectId}-checkpoint-${number}`,
    checkpointNumber: number,
    changedFileCount: number - 1,
    headCommitSha: 'a'.repeat(40),
    fingerprintSha256: `${projectId}-fingerprint-${number}`.padEnd(64, '0'),
  })
}

function commandOf(projectId: string) {
  return new VerificationCommandResponse({
    verificationCommandId: `${projectId}-command`,
    commandNumber: 1,
    name: `${projectId} check`,
    executablePath: String.raw`C:\tools\verify.exe`,
    arguments: ['check'],
    timeoutSeconds: 300,
    isEnabled: true,
  })
}

function executionOn(projectId: string, number: number, status: string) {
  return new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-execution-${number}`,
    verificationCommandId: `${projectId}-command`,
    executionNumber: number,
    gitCheckpointId: `${projectId}-checkpoint-${number}`,
    checkpointFingerprintSha256: `${projectId}-fingerprint-${number}`.padEnd(64, '0'),
    status,
    isDispatched: true,
    hasStandardOutput: false,
    hasStandardError: false,
  })
}

type EvidenceRead = (projectId: string) => Promise<GetProjectGitEvidenceResponse>

// Every client the three evidence consumers use, with one controllable evidence read; the verification claim, the capture
// and the review record are observable so a stale submission or an unrequested mutation is visible.
function installClients(options: { read: EvidenceRead; executions?: (projectId: string) => VerificationExecutionResponse[] }) {
  const getProjectGitEvidence = vi.fn(options.read)
  const captureGitWorkspaceCheckpoint = vi.fn()
  const claimVerificationExecution = vi.fn().mockResolvedValue({ verificationExecutionId: 'claimed', executionNumber: 9 })
  const recordCheckpointReview = vi.fn().mockResolvedValue({ reviewId: 'review-1', decision: 'Pending' })
  const getGitCheckpointChangedFiles = vi.fn()
  const getGitCheckpointDiff = vi.fn()
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({ getGitCheckpointChangedFiles } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
  vi.mocked(gitCheckpointDiffClient).mockReturnValue({ getGitCheckpointDiff } as unknown as ReturnType<typeof gitCheckpointDiffClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn((projectId: string) => Promise.resolve([commandOf(projectId)])),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({
    getProjectVerificationExecutions: vi.fn((projectId: string) => Promise.resolve(options.executions?.(projectId) ?? [])),
  } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({
    getProjectCheckpointReviews: vi.fn().mockResolvedValue([]),
  } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  return { getProjectGitEvidence, captureGitWorkspaceCheckpoint, claimVerificationExecution, recordCheckpointReview, getGitCheckpointChangedFiles, getGitCheckpointDiff }
}

const source = () => screen.getByRole('region', { name: 'Source evidence' })
const verification = () => screen.getByRole('region', { name: 'Verification commands' })
const review = () => screen.getByRole('region', { name: 'Checkpoint review evidence' })
const runButton = () => within(verification()).getByRole('button', { name: 'Run' })
const refreshButton = () => screen.getByRole('button', { name: 'Refresh evidence' })

async function renderReady(projectId = 'project-a') {
  const view = render(<CandidateWorkspacePanel projectId={projectId} />)
  await waitFor(() => expect(source()).toHaveTextContent(/Checkpoint #\d+/))
  await waitFor(() => expect(within(verification()).getByText(`#1 ${projectId} check`)).toBeInTheDocument())
  return view
}

describe('CandidateWorkspacePanel evidence refresh', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows the initial capture to every evidence consumer through the existing read', async () => {
    let latest: GetProjectGitEvidenceResponse = new GetProjectGitEvidenceResponse({ changedFileCount: 0 })
    const clients = installClients({ read: () => Promise.resolve(latest) })

    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(within(source()).getByText('No source checkpoint has been captured yet.')).toBeInTheDocument())
    expect(runButton()).toBeDisabled()

    latest = evidenceOf('project-a', 1)
    clients.captureGitWorkspaceCheckpoint.mockResolvedValue({})
    fireEvent.click(within(source()).getByRole('button', { name: 'Capture checkpoint' }))

    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #1'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(review()).queryByText(/Capture a current source checkpoint/)).not.toBeInTheDocument()
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-1')
    expect(clients.captureGitWorkspaceCheckpoint).toHaveBeenCalledTimes(1)
  })

  it('tells its owner once per explicit refresh so the run can read again, and not for a capture or a plain mount', async () => {
    const latest = evidenceOf('project-a', 1)
    const clients = installClients({ read: () => Promise.resolve(latest) })
    const onEvidenceRefreshRequested = vi.fn()
    render(<CandidateWorkspacePanel projectId="project-a" onEvidenceRefreshRequested={onEvidenceRefreshRequested} />)
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #1'))
    expect(onEvidenceRefreshRequested).not.toHaveBeenCalled()

    clients.captureGitWorkspaceCheckpoint.mockResolvedValue({})
    fireEvent.click(within(source()).getByRole('button', { name: 'Capture checkpoint' }))
    await waitFor(() => expect(clients.captureGitWorkspaceCheckpoint).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(within(source()).getByRole('button', { name: 'Capture checkpoint' })).toBeEnabled())
    expect(onEvidenceRefreshRequested).not.toHaveBeenCalled()

    fireEvent.click(refreshButton())
    fireEvent.click(refreshButton())

    expect(onEvidenceRefreshRequested).toHaveBeenCalledTimes(2)
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })

  it('refreshes Source evidence, Verification commands and Checkpoint review after a mutation without capturing or starting anything', async () => {
    let latest = evidenceOf('project-a', 1)
    const clients = installClients({
      read: () => Promise.resolve(latest),
      executions: (projectId) => [executionOn(projectId, 2, 'Passed')],
    })
    await renderReady()
    expect(within(review()).getByText(/No terminal verification evidence is available/)).toBeInTheDocument()
    expect(within(review()).getByRole('button', { name: 'Approve' })).toBeDisabled()

    latest = evidenceOf('project-a', 2) // an implementation or correction changed the candidate and recorded a checkpoint
    fireEvent.click(refreshButton())

    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #2 · 1 changed files'))
    await waitFor(() => expect(within(review()).getByRole('button', { name: 'Approve' })).toBeEnabled())
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-2')
    expect(clients.captureGitWorkspaceCheckpoint).not.toHaveBeenCalled()
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()
  })

  it('disables every checkpoint-dependent action while the refresh is pending and then submits the new checkpoint', async () => {
    let latest = evidenceOf('project-a', 1)
    const gate = deferred<GetProjectGitEvidenceResponse>()
    let gated = false
    const clients = installClients({ read: () => (gated ? gate.promise : Promise.resolve(latest)) })
    await renderReady()
    expect(runButton()).toBeEnabled()

    gated = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(runButton()).toBeDisabled())
    expect(within(review()).getByRole('button', { name: 'Pending' })).toBeDisabled()
    expect(within(review()).getByRole('button', { name: 'Approve' })).toBeDisabled()
    fireEvent.click(runButton())
    fireEvent.click(within(review()).getByRole('button', { name: 'Pending' }))
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()

    latest = evidenceOf('project-a', 2)
    await act(async () => gate.resolve(latest))
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-2')
  })

  it('keeps the dependent actions disabled after a failed refresh and recovers on the next successful one', async () => {
    let latest = evidenceOf('project-a', 1)
    let failing = false
    const clients = installClients({ read: () => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve(latest)) })
    await renderReady()

    failing = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(runButton()).toBeDisabled())
    expect(within(review()).getByRole('button', { name: 'Pending' })).toBeDisabled()
    expect(source()).toHaveTextContent('Checkpoint #1') // the previous metadata stays visible but is not used for a submission
    expect(within(verification()).getByText(/could not be refreshed/)).toBeInTheDocument()
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()

    failing = false
    latest = evidenceOf('project-a', 2)
    fireEvent.click(refreshButton())

    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #2'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(/could not be refreshed/)).not.toBeInTheDocument()
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-2')
  })

  it('refreshes the other consumers after a successful capture and none after a failed one', async () => {
    let latest = evidenceOf('project-a', 1)
    const clients = installClients({ read: () => Promise.resolve(latest) })
    await renderReady()

    clients.captureGitWorkspaceCheckpoint.mockRejectedValueOnce(new Error('refused'))
    const readsBefore = clients.getProjectGitEvidence.mock.calls.length
    fireEvent.click(within(source()).getByRole('button', { name: 'Capture checkpoint' }))
    await waitFor(() => expect(clients.captureGitWorkspaceCheckpoint).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(within(source()).getByRole('button', { name: 'Capture checkpoint' })).toBeEnabled())
    expect(clients.getProjectGitEvidence.mock.calls.length).toBe(readsBefore)

    latest = evidenceOf('project-a', 2)
    clients.captureGitWorkspaceCheckpoint.mockResolvedValueOnce({})
    fireEvent.click(within(source()).getByRole('button', { name: 'Capture checkpoint' }))

    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #2'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-2')
  })

  it('preserves the drafts, the reviewer choice and an inspection of an unchanged checkpoint', async () => {
    const latest = evidenceOf('project-a', 1)
    const clients = installClients({ read: () => Promise.resolve(latest) })
    clients.getGitCheckpointChangedFiles.mockResolvedValue([new GitCheckpointChangedFileResponse({ indexStatus: ' ', workTreeStatus: 'M', path: 'src/Feature.cs' })])
    clients.getGitCheckpointDiff.mockResolvedValue(new GetGitCheckpointDiffResponse({ comparisonText: 'diff --git a/src/Feature.cs b/src/Feature.cs', isComplete: true, trackedPathCount: 1, comparedPathCount: 1, omissions: [] }))
    await renderReady()
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Draft command' } })
    fireEvent.change(screen.getByLabelText('Verification arguments'), { target: { value: 'one\ntwo' } })
    fireEvent.change(within(review()).getByLabelText('Reviewer'), { target: { value: 'FutureAgent' } })
    fireEvent.click(within(source()).getByRole('button', { name: /Inspect files/ }))
    await within(source()).findByText('src/Feature.cs')

    const readsBefore = clients.getProjectGitEvidence.mock.calls.length
    fireEvent.click(refreshButton())
    await waitFor(() => expect(clients.getProjectGitEvidence.mock.calls.length).toBe(readsBefore + 3))
    await waitFor(() => expect(runButton()).toBeEnabled())

    expect(screen.getByLabelText('Verification command name')).toHaveValue('Draft command')
    expect(screen.getByLabelText('Verification arguments')).toHaveValue('one\ntwo')
    expect(within(review()).getByLabelText('Reviewer')).toHaveValue('FutureAgent')
    expect(within(source()).getByText('src/Feature.cs')).toBeInTheDocument()
  })

  it('applies only the newest of overlapping refreshes', async () => {
    const first = deferred<GetProjectGitEvidenceResponse>()
    const second = deferred<GetProjectGitEvidenceResponse>()
    let gate: ReturnType<typeof deferred<GetProjectGitEvidenceResponse>> | null = null
    const clients = installClients({ read: () => gate?.promise ?? Promise.resolve(evidenceOf('project-a', 1)) })
    await renderReady()

    gate = first
    fireEvent.click(refreshButton())
    await waitFor(() => expect(clients.getProjectGitEvidence.mock.calls.length).toBeGreaterThanOrEqual(6))
    const afterFirst = clients.getProjectGitEvidence.mock.calls.length
    gate = second
    fireEvent.click(refreshButton())
    await waitFor(() => expect(clients.getProjectGitEvidence.mock.calls.length).toBeGreaterThanOrEqual(afterFirst + 3))

    await act(async () => second.resolve(evidenceOf('project-a', 3)))
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #3'))
    await act(async () => first.resolve(evidenceOf('project-a', 2)))

    expect(source()).toHaveTextContent('Checkpoint #3')
    expect(source()).not.toHaveTextContent('Checkpoint #2')
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint-3')
  })

  it('drops an inspection that was requested for a checkpoint the refresh has replaced', async () => {
    let latest = evidenceOf('project-a', 1)
    const clients = installClients({ read: () => Promise.resolve(latest) })
    const files = deferred<GitCheckpointChangedFileResponse[]>()
    clients.getGitCheckpointChangedFiles.mockReturnValue(files.promise)
    clients.getGitCheckpointDiff.mockResolvedValue(new GetGitCheckpointDiffResponse({ comparisonText: 'old checkpoint diff', isComplete: true, trackedPathCount: 1, comparedPathCount: 1, omissions: [] }))
    await renderReady()
    fireEvent.click(within(source()).getByRole('button', { name: /Inspect files/ }))
    await waitFor(() => expect(clients.getGitCheckpointChangedFiles).toHaveBeenCalledTimes(1))

    latest = evidenceOf('project-a', 2)
    fireEvent.click(refreshButton())
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #2'))
    await act(async () => files.resolve([new GitCheckpointChangedFileResponse({ indexStatus: ' ', workTreeStatus: 'M', path: 'old.cs' })]))

    expect(within(source()).queryByText('old.cs')).not.toBeInTheDocument()
    expect(within(source()).queryByText('old checkpoint diff')).not.toBeInTheDocument()
  })

  it('keeps a refresh with its project through A to B to A, never carrying evidence or a pending read across', async () => {
    const latestByProject: Record<string, GetProjectGitEvidenceResponse> = {
      'project-a': evidenceOf('project-a', 1),
      'project-b': evidenceOf('project-b', 5),
    }
    const lateA = deferred<GetProjectGitEvidenceResponse>()
    let holdA = false
    const clients = installClients({ read: (projectId) => (holdA && projectId === 'project-a' ? lateA.promise : Promise.resolve(latestByProject[projectId])) })
    const view = await renderReady('project-a')

    holdA = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(runButton()).toBeDisabled())

    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #5'))
    await act(async () => lateA.resolve(evidenceOf('project-a', 2)))
    expect(source()).toHaveTextContent('Checkpoint #5')
    expect(source()).not.toHaveTextContent('Checkpoint #2')

    holdA = false
    latestByProject['project-a'] = evidenceOf('project-a', 3)
    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #3'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution).toHaveBeenCalledWith('project-a', 'project-a-command', expect.objectContaining({ gitCheckpointId: 'project-a-checkpoint-3' }))
  })
})
