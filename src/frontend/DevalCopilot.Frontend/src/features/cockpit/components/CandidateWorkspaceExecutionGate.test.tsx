import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
import { GetProjectGitEvidenceResponse, GetProjectWorkspaceResponse, VerificationCommandResponse } from '../../../api/generated/api-client'
import { WORKSPACE_EXECUTION_GUIDANCE, WORKSPACE_EXECUTION_WITHHELD_POINTER } from '../workspaceExecution'
import type { WorkspaceExecutionReadFailure, WorkspaceExecutionState, WorkspaceExecutionStatus } from '../workspaceExecution'
import { OTHER_PROJECT_ID, PROJECT_ID, readingOf, stateFor } from '../workspaceExecutionFixture'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'

// ADR-0057 in the candidate workspace region: the workspace execution status is an additional veto of verification Run. Anything but a
// validated, current Idle withholds Run and shows one fixed guidance; Idle enables nothing by itself; and Refresh evidence, checkpoint
// capture, evidence inspection and the recipe controls stay operable in every state.

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

function installClients(options: { checkpoint: boolean } = { checkpoint: true }) {
  const claimVerificationExecution = vi.fn().mockResolvedValue({ verificationExecutionId: 'claimed', executionNumber: 9 })
  const captureGitWorkspaceCheckpoint = vi.fn().mockResolvedValue({})
  const evidence = options.checkpoint
    ? new GetProjectGitEvidenceResponse({
        checkpointId: `${PROJECT_ID}-checkpoint-1`,
        checkpointNumber: 1,
        changedFileCount: 0,
        headCommitSha: 'a'.repeat(40),
        fingerprintSha256: 'f'.repeat(64),
      })
    : new GetProjectGitEvidenceResponse({ changedFileCount: 0 })
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence: vi.fn().mockResolvedValue(evidence) } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview: vi.fn() } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({ getGitCheckpointChangedFiles: vi.fn() } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
  vi.mocked(gitCheckpointDiffClient).mockReturnValue({ getGitCheckpointDiff: vi.fn() } as unknown as ReturnType<typeof gitCheckpointDiffClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn().mockResolvedValue([
      new VerificationCommandResponse({
        verificationCommandId: 'command-1',
        commandNumber: 1,
        name: 'unit',
        executablePath: String.raw`C:\tools\verify.exe`,
        arguments: ['check'],
        timeoutSeconds: 300,
        isEnabled: true,
      }),
    ]),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  return { claimVerificationExecution, captureGitWorkspaceCheckpoint }
}

const source = () => screen.getByRole('region', { name: 'Source evidence' })
const verification = () => screen.getByRole('region', { name: 'Verification commands' })
const runButton = () => within(verification()).getByRole('button', { name: 'Run' })
const guidanceRegion = () => screen.queryByRole('status', { name: 'Workspace execution status: candidate workspace' })

// A JavaScript caller may omit the required prop, so the helper can render undefined (cast) to prove it withholds like an explicit null.
async function renderPanel(workspaceExecution: WorkspaceExecutionState | null | undefined, props: { runWithheldReason?: string | null; onEvidenceRefreshRequested?: () => void } = {}) {
  const view = render(<CandidateWorkspacePanel projectId={PROJECT_ID} workspaceExecution={workspaceExecution as WorkspaceExecutionState | null} {...props} />)
  await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #1'))
  await waitFor(() => expect(within(verification()).getByText('#1 unit')).toBeInTheDocument())
  await waitFor(() => expect(within(verification()).queryByText('Reading verification status…')).not.toBeInTheDocument())
  return view
}

const WITHHELD_STATUSES: Exclude<WorkspaceExecutionStatus, 'Idle'>[] = ['NoWorkspace', 'AgentAttemptActive', 'VerificationActive', 'CommitReserved', 'Conflicted', 'Unavailable']
const FAILURES: WorkspaceExecutionReadFailure[] = ['read', 'timeout', 'not-found', 'invalid']

describe('CandidateWorkspacePanel workspace execution gate', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('leaves Run to the ordinary gates when the status is a validated Idle, with no guidance', async () => {
    const clients = installClients()
    await renderPanel(stateFor('Idle'))

    expect(runButton()).toBeEnabled()
    expect(guidanceRegion()).not.toBeInTheDocument()
    expect(screen.queryByText(WORKSPACE_EXECUTION_WITHHELD_POINTER)).not.toBeInTheDocument()
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
  })

  it('does not enable Run for an Idle status when the ordinary gates disable it: Idle enables nothing by itself', async () => {
    installClients({ checkpoint: false })
    render(<CandidateWorkspacePanel projectId={PROJECT_ID} workspaceExecution={stateFor('Idle')} />)
    await waitFor(() => expect(within(source()).getByText('No source checkpoint has been captured yet.')).toBeInTheDocument())

    expect(runButton()).toBeDisabled()
  })

  it.each([
    ['an explicit null state', null],
    ['an omitted state', undefined],
  ])('withholds Run, with the fixed guidance and no refresh control, for %s: absence is never a permission', async (_name, state) => {
    const clients = installClients()
    await renderPanel(state)

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE.pending)).toHaveLength(1)
    expect(within(verification()).getByText(WORKSPACE_EXECUTION_WITHHELD_POINTER)).toBeInTheDocument()
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
    // Inspection and Refresh evidence stay operable.
    expect(screen.getByRole('button', { name: 'Refresh evidence' })).toBeEnabled()
  })

  it('withholds Run as incoherent for a state whose status or failure this build does not know', async () => {
    const clients = installClients()
    const unknownStatus = { ...stateFor('Idle'), reading: { ...readingOf('Idle'), status: 'Zz-unknown' as never } }
    await renderPanel(unknownStatus)

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE.invalid)).toHaveLength(1)
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })

  it.each(WITHHELD_STATUSES)('withholds Run while the status is %s, with its fixed guidance once and a pointer at the control', async (status) => {
    const clients = installClients()
    await renderPanel(stateFor(status))

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE[status])).toHaveLength(1)
    expect(guidanceRegion()).toHaveTextContent(WORKSPACE_EXECUTION_GUIDANCE[status])
    expect(within(verification()).getByText(WORKSPACE_EXECUTION_WITHHELD_POINTER)).toBeInTheDocument()
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })

  it.each(FAILURES)('withholds Run while the read %s even beside a validated Idle reading, so a failed read masks it', async (failure) => {
    const clients = installClients()
    await renderPanel(stateFor('Idle', { failure }))

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE[failure])).toHaveLength(1)
    fireEvent.click(runButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })

  it('withholds Run while the first read is pending', async () => {
    installClients()
    await renderPanel(stateFor(null, { loading: true }))

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE.pending)).toHaveLength(1)
  })

  it("withholds Run for another project's reading as replaced", async () => {
    installClients()
    await renderPanel(stateFor('Idle', {}, OTHER_PROJECT_ID))

    expect(runButton()).toBeDisabled()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE.replaced)).toHaveLength(1)
  })

  it('keeps the planning-sequence reason first, still shows the status guidance once, and lets the gate lift only its own veto', async () => {
    installClients()
    const fence = 'Withheld: a planning sequence holds automatic authority over this run or its workspace.'
    const view = await renderPanel(stateFor('Idle'), { runWithheldReason: fence })
    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(fence)).toBeInTheDocument()
    expect(guidanceRegion()).not.toBeInTheDocument()
    view.unmount()

    await renderPanel(stateFor('Conflicted'), { runWithheldReason: fence })
    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(fence)).toBeInTheDocument()
    expect(screen.getAllByText(WORKSPACE_EXECUTION_GUIDANCE.Conflicted)).toHaveLength(1)
  })

  describe.each([
    ['AgentAttemptActive', stateFor('AgentAttemptActive')],
    ['Conflicted', stateFor('Conflicted')],
    ['a failed read', stateFor(null, { failure: 'read' })],
    ['a pending first read', stateFor(null, { loading: true })],
  ] as const)('while %s withholds Run', (_, state) => {
    it('keeps Refresh evidence, checkpoint capture and the recipe controls operable, and tells its owner of the refresh', async () => {
      const clients = installClients()
      const onEvidenceRefreshRequested = vi.fn()
      await renderPanel(state, { onEvidenceRefreshRequested })

      const refresh = screen.getByRole('button', { name: 'Refresh evidence' })
      expect(refresh).toBeEnabled()
      fireEvent.click(refresh)
      expect(onEvidenceRefreshRequested).toHaveBeenCalledTimes(1)

      const capture = within(source()).getByRole('button', { name: 'Capture checkpoint' })
      expect(capture).toBeEnabled()
      fireEvent.click(capture)
      await waitFor(() => expect(clients.captureGitWorkspaceCheckpoint).toHaveBeenCalledTimes(1))

      expect(within(verification()).getByRole('button', { name: 'Disable' })).toBeEnabled()
      expect(within(verification()).getByRole('button', { name: 'Remove' })).toBeEnabled()
      expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
    })
  })

  it('offers its own refresh of the status, which reads the status again and starts nothing', async () => {
    const clients = installClients()
    const refresh = vi.fn()
    await renderPanel(stateFor('VerificationActive', { refresh }))

    fireEvent.click(within(guidanceRegion() as HTMLElement).getByRole('button', { name: 'Refresh workspace execution status' }))

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })
})
