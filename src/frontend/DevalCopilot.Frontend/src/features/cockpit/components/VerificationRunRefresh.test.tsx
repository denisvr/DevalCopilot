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
  ApiException,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'

// Run needs settled, successful source AND verification-execution reads of the displayed refresh generation and no Running
// execution in the current workspace (whatever its recipe), in the rendered button and in its handler. A start request whose
// outcome is unknown blocks another submission until a later explicit refresh read the status. Real hooks and components over
// controllable generated clients; refresh and stale handlers must create no work.

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

const workspaceOf = (projectId: string) => `${projectId}-workspace`

function evidenceOf(projectId: string) {
  return new GetProjectGitEvidenceResponse({
    checkpointId: `${projectId}-checkpoint`,
    checkpointNumber: 1,
    changedFileCount: 0,
    headCommitSha: 'a'.repeat(40),
    fingerprintSha256: `${projectId}-fingerprint`.padEnd(64, '0'),
    gitWorkspaceId: workspaceOf(projectId),
  })
}

function executionOf(projectId: string, command: string, number: number, status: string, workspaceId = workspaceOf(projectId)) {
  return new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-${command}-execution-${number}`,
    verificationCommandId: `${projectId}-${command}`,
    executionNumber: number,
    gitWorkspaceId: workspaceId,
    gitCheckpointId: `${projectId}-checkpoint`,
    checkpointFingerprintSha256: `${projectId}-fingerprint`.padEnd(64, '0'),
    status,
    isDispatched: status !== 'Running',
    hasStandardOutput: false,
    hasStandardError: false,
  })
}

function commandOf(projectId: string, command: string, number: number) {
  return new VerificationCommandResponse({
    verificationCommandId: `${projectId}-${command}`,
    commandNumber: number,
    name: `${command} check`,
    executablePath: String.raw`C:\tools\verify.exe`,
    arguments: [command],
    timeoutSeconds: 300,
    isEnabled: true,
  })
}

interface Reads {
  executions: (projectId: string) => Promise<VerificationExecutionResponse[]>
  evidence: (projectId: string) => Promise<GetProjectGitEvidenceResponse>
}

function installClients(reads: Partial<Reads> = {}) {
  const getProjectVerificationExecutions = vi.fn(reads.executions ?? (() => Promise.resolve([])))
  const getProjectGitEvidence = vi.fn(reads.evidence ?? ((projectId: string) => Promise.resolve(evidenceOf(projectId))))
  const claimVerificationExecution = vi.fn().mockResolvedValue({ verificationExecutionId: 'claimed', executionNumber: 9 })
  const captureGitWorkspaceCheckpoint = vi.fn()
  const recordCheckpointReview = vi.fn()
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({ getGitCheckpointChangedFiles: vi.fn() } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
  vi.mocked(gitCheckpointDiffClient).mockReturnValue({ getGitCheckpointDiff: vi.fn() } as unknown as ReturnType<typeof gitCheckpointDiffClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn((projectId: string) => Promise.resolve([commandOf(projectId, 'unit', 1), commandOf(projectId, 'lint', 2)])),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  return { getProjectVerificationExecutions, getProjectGitEvidence, claimVerificationExecution, captureGitWorkspaceCheckpoint, recordCheckpointReview }
}

const verification = () => screen.getByRole('region', { name: 'Verification commands' })
const runButton = (command = 'unit') => {
  const row = within(verification()).getByText(`${command} check`, { exact: false }).closest('.dc-verification-command') as HTMLElement
  return within(row).getByRole('button', { name: /^(Run|Pending…|Running…|Starting…)/ })
}
const refreshButton = () => screen.getByRole('button', { name: 'Refresh evidence' })

// React ignores a click on a control whose props say disabled, so a handler retained from an earlier render is invoked directly: it
// is the handler itself, not the rendered control, that must decide from the newest authority.
function retainedHandlerOf(button: HTMLElement): () => Promise<void> {
  const propsKey = Object.keys(button).find((key) => key.startsWith('__reactProps'))!
  return (button as unknown as Record<string, { onClick: () => Promise<void> }>)[propsKey].onClick
}

async function renderSettled(projectId = 'project-a') {
  const view = render(<CandidateWorkspacePanel projectId={projectId} />)
  await waitFor(() => expect(runButton()).toBeEnabled())
  return view
}

function expectNoWork(clients: ReturnType<typeof installClients>) {
  expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  expect(clients.captureGitWorkspaceCheckpoint).not.toHaveBeenCalled()
  expect(clients.recordCheckpointReview).not.toHaveBeenCalled()
}

describe('VerificationCommandsPanel Run authority', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('enables Run only when both the source and the execution reads of the first generation have settled', async () => {
    const executions = deferred<VerificationExecutionResponse[]>()
    const clients = installClients({ executions: () => executions.promise })

    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(within(verification()).getByText('Reading verification status…')).toBeInTheDocument())
    await waitFor(() => expect(within(verification()).queryByText('Reading the current source checkpoint…')).not.toBeInTheDocument())
    expect(runButton()).toBeDisabled()
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()

    await act(async () => executions.resolve([]))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText('Reading verification status…')).not.toBeInTheDocument()
  })

  it('disables Run while the explicit refresh reads the executions, creates no work, and enables it for the fresh answer', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const clients = installClients({ executions: () => (gated ? gate.promise : Promise.resolve([])) })
    await renderSettled()

    gated = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(runButton()).toBeDisabled())
    expect(within(verification()).getByText('Reading verification status…')).toBeInTheDocument()
    fireEvent.click(runButton())
    expectNoWork(clients)

    await act(async () => gate.resolve([executionOf('project-a', 'unit', 1, 'Passed')]))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expectNoWork(clients)
  })

  it('keeps Run disabled after a failed execution read, keeps the cached history visible, and recovers on the next successful refresh', async () => {
    let failing = false
    const clients = installClients({
      executions: (projectId) => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve([executionOf(projectId, 'unit', 1, 'Passed')])),
    })
    await renderSettled()
    await waitFor(() => expect(within(verification()).getByText('Last run: Passed')).toBeInTheDocument())

    failing = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(verification()).getByText('Verification status could not be loaded.')).toBeInTheDocument())
    expect(runButton()).toBeDisabled()
    expect(runButton('lint')).toBeDisabled()
    expect(within(verification()).getByText('Last run: Passed')).toBeInTheDocument() // history, not authority
    expect(within(verification()).queryByText('Reading verification status…')).not.toBeInTheDocument()

    failing = false
    fireEvent.click(refreshButton())
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText('Verification status could not be loaded.')).not.toBeInTheDocument()
    expectNoWork(clients)
  })

  it('blocks every recipe while another recipe is running in the current workspace and releases them when it finishes', async () => {
    let mode: 'running' | 'done' = 'running'
    const clients = installClients({
      executions: (projectId) => Promise.resolve(mode === 'running' ? [executionOf(projectId, 'lint', 1, 'Running')] : [executionOf(projectId, 'lint', 1, 'Passed')]),
    })
    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())

    expect(runButton('unit')).toBeDisabled()
    expect(runButton('lint')).toBeDisabled()
    fireEvent.click(runButton('unit'))
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()

    mode = 'done'
    await waitFor(() => expect(runButton('unit')).toBeEnabled(), { timeout: 4_000 })
    expect(within(verification()).queryByText(/A verification is running in this workspace/)).not.toBeInTheDocument()
  })

  it('is not blocked by a historical running execution of another workspace, but is by one of the current workspace', async () => {
    let workspaceId = 'old-workspace'
    installClients({
      executions: (projectId) => Promise.resolve([executionOf(projectId, 'lint', 1, 'Running', workspaceId)]),
    })
    const view = render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(/A verification is running in this workspace/)).not.toBeInTheDocument()

    workspaceId = workspaceOf('project-a')
    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())
    expect(runButton()).toBeDisabled()
  })

  it('refuses a submission in the handler as well as in the button when the authority is not current', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const clients = installClients({ executions: () => (gated ? gate.promise : Promise.resolve([])) })
    await renderSettled()
    const button = runButton()
    const retained = retainedHandlerOf(button) // bound while the authority was current

    gated = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button).toBeDisabled())
    await act(async () => {
      await retained()
    })

    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
    expect(within(verification()).getByText(/wait for no running verification/)).toBeInTheDocument()
    await act(async () => gate.resolve([]))
  })

  it('submits the claim for the current checkpoint exactly once even when the control is activated twice synchronously', async () => {
    const clients = installClients()
    await renderSettled()
    const claim = deferred<{ executionNumber: number }>()
    clients.claimVerificationExecution.mockReturnValue(claim.promise)

    const retained = retainedHandlerOf(runButton())
    await act(async () => {
      void retained()
      void retained() // a second synchronous activation of the same, not yet re-rendered handler
    })

    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('project-a-checkpoint')
    await act(async () => claim.resolve({ executionNumber: 1 }))
  })

  it('reports an accepted claim as requested even when the status read that follows fails, and keeps Run blocked until a read succeeds', async () => {
    let executionReads = 0
    const clients = installClients({
      executions: () => (++executionReads <= 2 ? Promise.resolve([]) : Promise.reject(new Error('read unavailable'))),
    })
    await renderSettled()

    fireEvent.click(runButton())

    await waitFor(() => expect(within(verification()).getByText('Verification #9 was requested.')).toBeInTheDocument())
    await waitFor(() => expect(within(verification()).getByText('Verification status could not be loaded.')).toBeInTheDocument())
    expect(within(verification()).queryByText('This verification could not be started.')).not.toBeInTheDocument()
    expect(runButton()).toBeDisabled()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('reports a refused claim as not started and leaves Run available', async () => {
    const clients = installClients()
    clients.claimVerificationExecution.mockRejectedValue(new ApiException('conflict', 409, '{"errors":[]}', {}, null))
    await renderSettled()

    fireEvent.click(runButton())

    await waitFor(() => expect(within(verification()).getByText('This verification could not be started.')).toBeInTheDocument())
    expect(within(verification()).queryByText(/may or may not have been recorded/)).not.toBeInTheDocument()
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['a transport failure', () => new TypeError('network down')],
    ['a server fault', () => new ApiException('boom', 500, 'SERVER-DETAIL-SENTINEL internal stack', {}, null)],
  ])('treats %s as an uncertain outcome: fixed copy, no retry, and Run blocked until a later explicit refresh read the status', async (_name, failure) => {
    const clients = installClients()
    clients.claimVerificationExecution.mockRejectedValue(failure())
    await renderSettled()

    fireEvent.click(runButton())

    const uncertain = 'The verification request may or may not have been recorded. Use Refresh evidence to check the verification status before running again.'
    await waitFor(() => expect(within(verification()).getByText(uncertain)).toBeInTheDocument())
    expect(within(verification()).queryByText('This verification could not be started.')).not.toBeInTheDocument()
    expect(verification()).not.toHaveTextContent('SERVER-DETAIL-SENTINEL')
    await waitFor(() => expect(runButton()).toBeDisabled())
    // The status reads of the same generation do not clear it, and nothing retries by itself.
    await new Promise((resolve) => setTimeout(resolve, 1_300))
    expect(runButton()).toBeDisabled()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)

    fireEvent.click(refreshButton())
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(uncertain)).not.toBeInTheDocument()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('keeps an uncertain outcome blocked while the refresh that follows it has not read the status successfully', async () => {
    let failing = false
    const clients = installClients({ executions: () => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve([])) })
    clients.claimVerificationExecution.mockRejectedValue(new TypeError('network down'))
    await renderSettled()
    fireEvent.click(runButton())
    await waitFor(() => expect(within(verification()).getByText(/may or may not have been recorded/)).toBeInTheDocument())

    failing = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(within(verification()).getByText('Verification status could not be loaded.')).toBeInTheDocument())
    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(/may or may not have been recorded/)).toBeInTheDocument()

    failing = false
    fireEvent.click(refreshButton())
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('keeps an accepted claim real after the project is replaced, without reporting on or blocking the replacement', async () => {
    const claim = deferred<{ executionNumber: number }>()
    const clients = installClients()
    const view = await renderSettled('project-a')
    clients.claimVerificationExecution.mockReturnValue(claim.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))

    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await waitFor(() => expect(within(verification()).getByText('unit check', { exact: false })).toBeInTheDocument())
    await waitFor(() => expect(runButton()).toBeEnabled())
    await act(async () => claim.resolve({ executionNumber: 4 }))

    expect(within(verification()).queryByText(/Verification #4 was requested/)).not.toBeInTheDocument()
    expect(runButton()).toBeEnabled()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
    expect(clients.claimVerificationExecution.mock.calls[0][0]).toBe('project-a')

    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(/Verification #4 was requested/)).not.toBeInTheDocument()
  })

  it('never offers the returning project the status it showed before it was replaced until its own read settles', async () => {
    const returning = deferred<VerificationExecutionResponse[]>()
    let holdA = false
    installClients({ executions: (projectId) => (holdA && projectId === 'project-a' ? returning.promise : Promise.resolve([])) })
    const view = await renderSettled('project-a')

    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await waitFor(() => expect(runButton()).toBeEnabled())
    holdA = true
    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)

    await waitFor(() => expect(within(verification()).getByText('Reading verification status…')).toBeInTheDocument())
    await waitFor(() => expect(runButton()).toBeDisabled())
    await act(async () => returning.resolve([]))
    await waitFor(() => expect(runButton()).toBeEnabled())
  })

  it('requests nothing and warns about nothing when it unmounts with a refresh and a claim pending', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    const claim = deferred<{ executionNumber: number }>()
    let gated = false
    const clients = installClients({ executions: () => (gated ? gate.promise : Promise.resolve([])) })
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {})
    const view = await renderSettled()
    clients.claimVerificationExecution.mockReturnValue(claim.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    gated = true
    fireEvent.click(refreshButton())
    const reads = clients.getProjectVerificationExecutions.mock.calls.length

    view.unmount()
    await act(async () => {
      gate.resolve([])
      claim.resolve({ executionNumber: 2 })
    })

    expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(reads)
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('applies only the newest of overlapping refreshes to Run', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const newer = deferred<VerificationExecutionResponse[]>()
    let mode: 'settled' | 'older' | 'newer' = 'settled'
    const clients = installClients({
      executions: () => (mode === 'older' ? older.promise : mode === 'newer' ? newer.promise : Promise.resolve([])),
    })
    await renderSettled()
    mode = 'older'
    fireEvent.click(refreshButton())
    await waitFor(() => expect(runButton()).toBeDisabled())
    mode = 'newer'
    fireEvent.click(refreshButton())

    await act(async () => newer.resolve([executionOf('project-a', 'lint', 1, 'Running')]))
    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())
    await act(async () => older.resolve([]))

    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument()
    expectNoWork(clients)
  })
})
