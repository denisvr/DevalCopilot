import { Profiler } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  claimVerificationExecutionClient,
  configureVerificationCommandClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  prepareWorkspaceClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  projectWorkspaceClient,
  recordCheckpointReviewClient,
  verificationExecutionOutputClient,
} from '../../../api/clients'
import {
  CheckpointReviewResponse,
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  GitCheckpointChangedFileResponse,
  VerificationCommandResponse,
  VerificationExecutionOutputQueryResult,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectWorkspaceClient: vi.fn(),
  prepareWorkspaceClient: vi.fn(),
  recheckPhysicalIdentityClient: vi.fn(),
  projectGitEvidenceClient: vi.fn(),
  captureGitWorkspaceCheckpointClient: vi.fn(),
  gitCheckpointChangedFilesClient: vi.fn(),
  gitCheckpointDiffClient: vi.fn(),
  projectVerificationCommandsClient: vi.fn(),
  configureVerificationCommandClient: vi.fn(),
  updateVerificationCommandClient: vi.fn(),
  deleteVerificationCommandClient: vi.fn(),
  projectVerificationExecutionsClient: vi.fn(),
  claimVerificationExecutionClient: vi.fn(),
  projectCheckpointReviewsClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
  verificationExecutionOutputClient: vi.fn(),
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

const flush = () => act(async () => {})

function asClient<T>(factory: unknown, methods: Record<string, unknown>) {
  vi.mocked(factory as () => unknown).mockReturnValue(methods as unknown as T)
}

// Two projects whose every identifier and visible value differs.
const PROJECTS = {
  'project-a': { suffix: 'a', number: 11, path: 'C:\\candidates\\alpha', branch: 'candidate/alpha' },
  'project-b': { suffix: 'b', number: 22, path: 'C:\\candidates\\bravo', branch: 'candidate/bravo' },
} as const
type ProjectId = keyof typeof PROJECTS

const workspace = (id: ProjectId) =>
  new GetProjectWorkspaceResponse({
    physicalIdentityStatus: 'Resolved',
    state: 'Ready',
    candidatePath: PROJECTS[id].path,
    branchName: PROJECTS[id].branch,
    sourceCommitSha: `${PROJECTS[id].suffix}`.repeat(40),
  })
const evidence = (id: ProjectId) =>
  new GetProjectGitEvidenceResponse({
    checkpointId: `checkpoint-${PROJECTS[id].suffix}`,
    checkpointNumber: PROJECTS[id].number,
    changedFileCount: PROJECTS[id].number,
    headCommitSha: `head-${PROJECTS[id].suffix}`,
    fingerprintSha256: `fingerprint-${PROJECTS[id].suffix}`,
  })
const command = (id: ProjectId) =>
  new VerificationCommandResponse({
    verificationCommandId: `command-${PROJECTS[id].suffix}`,
    commandNumber: PROJECTS[id].number,
    name: `Verify ${PROJECTS[id].branch}`,
    executablePath: `C:\\tools\\${PROJECTS[id].suffix}.exe`,
    arguments: ['--only', `${PROJECTS[id].suffix} literal`],
    timeoutSeconds: 30 + PROJECTS[id].number,
    isEnabled: true,
  })
const execution = (id: ProjectId, status = 'Passed') =>
  new VerificationExecutionResponse({
    verificationExecutionId: `execution-${PROJECTS[id].suffix}`,
    verificationCommandId: `command-${PROJECTS[id].suffix}`,
    gitCheckpointId: `checkpoint-${PROJECTS[id].suffix}`,
    checkpointFingerprintSha256: `fingerprint-${PROJECTS[id].suffix}`,
    executionNumber: PROJECTS[id].number,
    status,
    isDispatched: true,
    hasStandardOutput: true,
    hasStandardError: false,
  })
const review = (id: ProjectId) =>
  new CheckpointReviewResponse({
    reviewId: `review-${PROJECTS[id].suffix}`,
    gitCheckpointId: `checkpoint-${PROJECTS[id].suffix}`,
    checkpointNumber: PROJECTS[id].number,
    actorKind: 'Human',
    decision: 'Approved',
    isApplicable: true,
  })

interface World {
  getProjectWorkspace: ReturnType<typeof vi.fn>
  getProjectGitEvidence: ReturnType<typeof vi.fn>
  getProjectVerificationCommands: ReturnType<typeof vi.fn>
  getProjectVerificationExecutions: ReturnType<typeof vi.fn>
  getProjectCheckpointReviews: ReturnType<typeof vi.fn>
  claimVerificationExecution: ReturnType<typeof vi.fn>
  configureVerificationCommand: ReturnType<typeof vi.fn>
  recordCheckpointReview: ReturnType<typeof vi.fn>
  prepareRepositoryWorkspace: ReturnType<typeof vi.fn>
  getVerificationExecutionOutput: ReturnType<typeof vi.fn>
}

// Every read answers per project from `answers`; a project listed in `gates` waits for its gate.
function createWorld(gates: Partial<Record<ProjectId, Promise<unknown>>> = {}): World {
  const answer = <T,>(make: (id: ProjectId) => T) =>
    vi.fn().mockImplementation(async (id: ProjectId) => {
      await gates[id]
      return make(id)
    })
  const world: World = {
    getProjectWorkspace: answer(workspace),
    getProjectGitEvidence: answer(evidence),
    getProjectVerificationCommands: answer((id) => [command(id)]),
    getProjectVerificationExecutions: answer((id) => [execution(id)]),
    getProjectCheckpointReviews: answer((id) => [review(id)]),
    claimVerificationExecution: vi.fn().mockResolvedValue(new VerificationExecutionResponse({ executionNumber: 77 })),
    configureVerificationCommand: vi.fn().mockResolvedValue({}),
    recordCheckpointReview: vi.fn().mockResolvedValue({}),
    prepareRepositoryWorkspace: vi.fn().mockResolvedValue({}),
    getVerificationExecutionOutput: vi
      .fn()
      .mockResolvedValue(new VerificationExecutionOutputQueryResult({ text: 'stdout of the execution', isFinal: true, nextOffset: 5 })),
  }
  asClient(projectWorkspaceClient, { getProjectWorkspace: world.getProjectWorkspace })
  asClient(projectGitEvidenceClient, { getProjectGitEvidence: world.getProjectGitEvidence })
  asClient(projectVerificationCommandsClient, { getProjectVerificationCommands: world.getProjectVerificationCommands })
  asClient(projectVerificationExecutionsClient, { getProjectVerificationExecutions: world.getProjectVerificationExecutions })
  asClient(projectCheckpointReviewsClient, { getProjectCheckpointReviews: world.getProjectCheckpointReviews })
  asClient(claimVerificationExecutionClient, { claimVerificationExecution: world.claimVerificationExecution })
  asClient(configureVerificationCommandClient, { configureVerificationCommand: world.configureVerificationCommand })
  asClient(recordCheckpointReviewClient, { recordCheckpointReview: world.recordCheckpointReview })
  asClient(prepareWorkspaceClient, { prepareRepositoryWorkspace: world.prepareRepositoryWorkspace })
  asClient(verificationExecutionOutputClient, { getVerificationExecutionOutput: world.getVerificationExecutionOutput })
  return world
}

// Records what the document shows in every commit of the subtree (Profiler fires after the DOM is mutated
// and before paint), so a frame that shows the previous project cannot hide behind a later, correct one.
const committedFrames: string[] = []
const recordFrame = () => {
  committedFrames.push(document.body.textContent ?? '')
}

const probed = (id: ProjectId) => (
  <Profiler id="panel" onRender={recordFrame}>
    <CandidateWorkspacePanel projectId={id} />
  </Profiler>
)

function expectNoFrameOf(id: ProjectId, from: number) {
  const { path, branch, number } = PROJECTS[id]
  for (const frame of committedFrames.slice(from)) {
    expect(frame).not.toContain(path)
    expect(frame).not.toContain(branch)
    expect(frame).not.toContain(`Checkpoint #${number}`)
    expect(frame).not.toContain(`#${number} Verify`)
  }
}

function expectNothingOf(id: ProjectId) {
  const { path, branch, number } = PROJECTS[id]
  const text = document.body.textContent ?? ''
  expect(text).not.toContain(path)
  expect(text).not.toContain(branch)
  expect(text).not.toContain(`Checkpoint #${number}`)
  expect(text).not.toContain(`#${number} Verify`)
  expect(text).not.toContain(`HEAD head-${PROJECTS[id].suffix}`)
}

beforeEach(() => {
  vi.clearAllMocks()
  committedFrames.length = 0
})

describe('CandidateWorkspacePanel project selection ownership', () => {
  it('renders no workspace, checkpoint, command, execution or review of the previous project in the frame that selects another', async () => {
    const gate = deferred<void>()
    createWorld({ 'project-b': gate.promise })
    const { rerender } = render(probed('project-a'))
    await flush()
    expect(screen.getByText(PROJECTS['project-a'].path)).toBeInTheDocument()
    expect(screen.getByText(/Checkpoint #11 · 11 changed files/)).toBeInTheDocument()
    expect(screen.getByText(/#11 Verify candidate\/alpha/)).toBeInTheDocument()
    expect(screen.getByText(/Last run: Passed/)).toBeInTheDocument()
    expect(screen.getByText("Current checkpoint")).toBeInTheDocument()

    // The committed frames of the new selection, before any of its requests could answer.
    const mark = committedFrames.length
    rerender(probed('project-b'))
    expect(committedFrames.length).toBeGreaterThan(mark)
    expectNoFrameOf('project-a', mark)
    expectNothingOf('project-a')
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Run' })).toBeNull()
    expect(screen.queryByText(/Last run/)).toBeNull()

    await act(async () => {
      gate.resolve()
    })
    await flush()
    expectNoFrameOf('project-a', mark)
    expectNothingOf('project-a')
    expect(screen.getByText(PROJECTS['project-b'].path)).toBeInTheDocument()
    expect(screen.getByText(/Checkpoint #22 · 22 changed files/)).toBeInTheDocument()
    expect(screen.getByText(/#22 Verify candidate\/bravo/)).toBeInTheDocument()
  })

  it('drops everything of an unselected project, does not resurrect it on return, and ignores its late answers', async () => {
    const gateA = deferred<void>()
    const world = createWorld()
    const { rerender } = render(probed('project-a'))
    await flush()

    rerender(probed('project-b'))
    await flush()
    // Returning to A is a new lifetime whose reads are all pending.
    world.getProjectWorkspace.mockImplementationOnce(async () => {
      await gateA.promise
      return workspace('project-a')
    })
    const mark = committedFrames.length
    rerender(probed('project-a'))
    expectNoFrameOf('project-b', mark)
    expectNoFrameOf('project-a', mark)
    expectNothingOf('project-b')
    expect(screen.queryByText(PROJECTS['project-a'].path)).toBeNull()
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    await act(async () => {
      gateA.resolve()
    })
    await flush()
    expect(screen.getByText(PROJECTS['project-a'].path)).toBeInTheDocument()
    expectNothingOf('project-b')
  })

  it('targets the selected project and checkpoint with the literal arguments of the selected command', async () => {
    const world = createWorld()
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await flush()

    expect(screen.getByText('C:\\tools\\b.exe --only b literal')).toBeInTheDocument()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Run' }))
    })
    expect(world.claimVerificationExecution).toHaveBeenCalledTimes(1)
    const [projectId, commandId, request] = world.claimVerificationExecution.mock.calls[0]
    expect([projectId, commandId, request.gitCheckpointId]).toEqual(['project-b', 'command-b', 'checkpoint-b'])
    expect(screen.getByText('Verification #77 was requested.')).toBeInTheDocument()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Approve' }))
    })
    expect(world.recordCheckpointReview).toHaveBeenCalledTimes(1)
    const [reviewProject, reviewRequest] = world.recordCheckpointReview.mock.calls[0]
    expect(reviewProject).toBe('project-b')
    expect(reviewRequest).toMatchObject({
      gitCheckpointId: 'checkpoint-b',
      verificationExecutionId: 'execution-b',
      decision: 'Approved',
      actorKind: 'Human',
    })
  })

  it('does not report, refresh or release the replacement when an accepted verification start completes after the selection changed', async () => {
    const world = createWorld()
    const accepted = deferred<VerificationExecutionResponse>()
    world.claimVerificationExecution.mockReturnValueOnce(accepted.promise)
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Run' }))
    })
    expect(screen.getByRole('button', { name: 'Starting…' })).toBeInTheDocument()

    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await flush()
    expect(screen.queryByRole('button', { name: 'Starting…' })).toBeNull()
    const reads = world.getProjectVerificationExecutions.mock.calls.length

    await act(async () => {
      accepted.resolve(new VerificationExecutionResponse({ executionNumber: 5 }))
    })
    await flush()
    expect(screen.queryByText(/Verification #5/)).toBeNull()
    expect(world.getProjectVerificationExecutions).toHaveBeenCalledTimes(reads)
    expect(world.claimVerificationExecution).toHaveBeenCalledTimes(1)
    // B can start its own verification: nothing of A's pending start blocks or misdirects it.
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Run' }))
    })
    expect(world.claimVerificationExecution.mock.calls[1].slice(0, 2)).toEqual(['project-b', 'command-b'])
  })

  it('keeps the newer draft and does not clear it when an accepted configuration of the previous project completes late', async () => {
    const world = createWorld()
    const accepted = deferred<unknown>()
    world.configureVerificationCommand.mockReturnValueOnce(accepted.promise)
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Draft for A' } })
    fireEvent.change(screen.getByLabelText('Verification executable path'), { target: { value: 'C:\\tools\\a-new.exe' } })
    fireEvent.change(screen.getByLabelText('Verification arguments'), { target: { value: '--one\n--two words\n' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Add command' }))
    })
    const [projectId, request] = world.configureVerificationCommand.mock.calls[0]
    expect(projectId).toBe('project-a')
    expect(request).toMatchObject({ name: 'Draft for A', executablePath: 'C:\\tools\\a-new.exe', arguments: ['--one', '--two words'] })

    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await flush()
    // The form of project B starts empty and receives its own draft.
    expect(screen.getByLabelText('Verification command name')).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Add command' })).toBeEnabled()
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Draft for B' } })
    const reads = world.getProjectVerificationCommands.mock.calls.length

    await act(async () => {
      accepted.resolve({})
    })
    await flush()
    expect(screen.getByLabelText('Verification command name')).toHaveValue('Draft for B')
    expect(world.getProjectVerificationCommands).toHaveBeenCalledTimes(reads)
    expect(screen.queryByText(/could not be completed/)).toBeNull()
  })

  it('clears a saved draft on success, but not text the reviewer has since typed', async () => {
    const world = createWorld()
    const accepted = deferred<unknown>()
    world.configureVerificationCommand.mockReturnValueOnce(accepted.promise)
    render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Saved name' } })
    fireEvent.change(screen.getByLabelText('Verification executable path'), { target: { value: 'C:\\tools\\saved.exe' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Add command' }))
    })
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Typed while saving' } })
    await act(async () => {
      accepted.resolve({})
    })
    await flush()
    expect(screen.getByLabelText('Verification command name')).toHaveValue('Typed while saving')

    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Second' } })
    fireEvent.change(screen.getByLabelText('Verification executable path'), { target: { value: 'C:\\tools\\second.exe' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Add command' }))
    })
    await flush()
    expect(screen.getByLabelText('Verification command name')).toHaveValue('')
    expect(world.configureVerificationCommand).toHaveBeenCalledTimes(2)
  })

  it('shows no verification output, selected stream or selected evidence of the previous project', async () => {
    const world = createWorld()
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Inspect stdout' }))
    })
    await flush()
    expect(screen.getByText('stdout of the execution')).toBeInTheDocument()
    expect(world.getVerificationExecutionOutput.mock.calls[0].slice(0, 3)).toEqual(['project-a', 'execution-a', 'stdout'])

    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    expect(screen.queryByText('stdout of the execution')).toBeNull()
    await flush()
    expect(screen.queryByText('stdout of the execution')).toBeNull()
    expect(screen.getByRole('button', { name: 'Inspect stdout' })).toBeInTheDocument()
    expect(world.getVerificationExecutionOutput.mock.calls.every((call) => call[1] === 'execution-a')).toBe(true)
  })

  it('does not show a preparation of the previous project as pending for the next, nor refresh it', async () => {
    const world = createWorld()
    world.getProjectWorkspace.mockImplementation(async (id: ProjectId) =>
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'NotRequested', branchName: id }),
    )
    const accepted = deferred<unknown>()
    world.prepareRepositoryWorkspace.mockReturnValueOnce(accepted.promise)
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Prepare workspace' }))
    })
    expect(screen.getByRole('button', { name: 'Preparing…' })).toBeDisabled()

    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await flush()
    expect(screen.getByRole('button', { name: 'Prepare workspace' })).toBeEnabled()
    const reads = world.getProjectWorkspace.mock.calls.length
    await act(async () => {
      accepted.resolve({})
    })
    await flush()
    expect(world.getProjectWorkspace).toHaveBeenCalledTimes(reads)
    expect(screen.getByRole('button', { name: 'Prepare workspace' })).toBeEnabled()
    expect(world.prepareRepositoryWorkspace).toHaveBeenCalledWith('project-a')
  })

  it('does not apply the failed evidence read of the replaced project to the replacement', async () => {
    const world = createWorld()
    const failure = deferred<void>()
    world.getProjectGitEvidence.mockImplementationOnce(async () => {
      await failure.promise
      throw new Error('A evidence failed')
    })
    const { rerender } = render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await flush()
    expect(screen.getByText(/Checkpoint #22 · 22 changed files/)).toBeInTheDocument()
    await act(async () => {
      failure.resolve()
    })
    await flush()
    expect(screen.queryByText('This source evidence request could not be completed.')).toBeNull()
    expect(screen.getByText(/Checkpoint #22 · 22 changed files/)).toBeInTheDocument()
  })
})

describe('WorkspaceEvidencePanel checkpoint ownership', () => {
  it('never shows the files and diff of an earlier checkpoint for a newer one, and inspects the newer by its own identifiers', async () => {
    const firstEvidence = new GetProjectGitEvidenceResponse({ checkpointId: 'checkpoint-1', checkpointNumber: 1, changedFileCount: 1 })
    const secondEvidence = new GetProjectGitEvidenceResponse({ checkpointId: 'checkpoint-2', checkpointNumber: 2, changedFileCount: 3 })
    const getProjectGitEvidence = vi.fn().mockResolvedValueOnce(firstEvidence).mockResolvedValueOnce(secondEvidence)
    const firstFiles = deferred<GitCheckpointChangedFileResponse[]>()
    const firstDiff = deferred<GetGitCheckpointDiffResponse>()
    const getGitCheckpointChangedFiles = vi
      .fn()
      .mockReturnValueOnce(firstFiles.promise)
      .mockResolvedValueOnce([new GitCheckpointChangedFileResponse({ path: 'second-only.txt', indexStatus: ' ', workTreeStatus: 'M' })])
    const getGitCheckpointDiff = vi
      .fn()
      .mockReturnValueOnce(firstDiff.promise)
      .mockResolvedValueOnce(new GetGitCheckpointDiffResponse({ completeDiff: 'diff --git second' }))
    asClient(projectGitEvidenceClient, { getProjectGitEvidence })
    asClient(captureGitWorkspaceCheckpointClient, { captureGitWorkspaceCheckpoint: vi.fn().mockResolvedValue({}) })
    asClient(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles })
    asClient(gitCheckpointDiffClient, { getGitCheckpointDiff })

    render(<WorkspaceEvidencePanel projectId="project-a" workspaceReady />)
    await flush()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Inspect files & diff' }))
    })
    expect(screen.getByRole('button', { name: 'Checking…' })).toBeDisabled()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Capture checkpoint' }))
    })
    await flush()
    expect(screen.getByText(/Checkpoint #2 ·/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inspect files & diff' })).toBeEnabled()

    await act(async () => {
      firstFiles.resolve([new GitCheckpointChangedFileResponse({ path: 'first-only.txt', indexStatus: ' ', workTreeStatus: 'M' })])
      firstDiff.resolve(new GetGitCheckpointDiffResponse({ completeDiff: 'diff --git first' }))
    })
    await flush()
    expect(screen.queryByText(/first-only\.txt/)).toBeNull()
    expect(screen.queryByText(/diff --git first/)).toBeNull()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Inspect files & diff' }))
    })
    await flush()
    expect(getGitCheckpointChangedFiles).toHaveBeenLastCalledWith('project-a', 'checkpoint-2')
    expect(getGitCheckpointDiff).toHaveBeenLastCalledWith('project-a', 'checkpoint-2')
    expect(screen.getByText(/second-only\.txt/)).toBeInTheDocument()
    expect(screen.getByText('diff --git second')).toBeInTheDocument()
    expect(screen.queryByText(/first-only\.txt/)).toBeNull()
  })

  it('exposes no evidence once the workspace is no longer ready', async () => {
    asClient(projectGitEvidenceClient, { getProjectGitEvidence: vi.fn().mockResolvedValue(evidence('project-a')) })
    const { rerender } = render(<WorkspaceEvidencePanel projectId="project-a" workspaceReady />)
    await flush()
    expect(screen.getByText(/Checkpoint #11 · 11 changed files/)).toBeInTheDocument()
    rerender(<WorkspaceEvidencePanel projectId="project-a" workspaceReady={false} />)
    expect(document.body.textContent).toBe('')
    rerender(<WorkspaceEvidencePanel projectId="project-a" workspaceReady />)
    expect(screen.queryByText(/Checkpoint #11/)).toBeNull()
    await waitFor(() => expect(screen.getByText(/Checkpoint #11 · 11 changed files/)).toBeInTheDocument())
  })
})

describe('verification draft identity', () => {
  it('keeps a draft edited and then restored to the saved values while the earlier save completes', async () => {
    const world = createWorld()
    const accepted = deferred<unknown>()
    world.configureVerificationCommand.mockReturnValueOnce(accepted.promise)
    render(<CandidateWorkspacePanel projectId="project-a" />)
    await flush()
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Saved recipe' } })
    fireEvent.change(screen.getByLabelText('Verification executable path'), { target: { value: 'C:\\tools\\saved.exe' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Add command' }))
    })
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'New draft' } })
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Saved recipe' } })
    await act(async () => {
      accepted.resolve({})
    })
    await flush()
    expect(screen.getByLabelText('Verification command name')).toHaveValue('Saved recipe')
    expect(screen.getByLabelText('Verification executable path')).toHaveValue('C:\\tools\\saved.exe')
    expect(world.configureVerificationCommand).toHaveBeenCalledTimes(1)
  })
})
