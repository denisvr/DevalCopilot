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
import { availableCapacityState } from '../executionCapacityFixture'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import { describeLocalExecutionSequenceWithholding } from '../localExecutionSequenceCopy'
import { PROJECT_ID, stateFor } from '../workspaceExecutionFixture'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'
import { blockedLocalReader, localReaderFor, localStatusFor, noneLocalReader, unknownLocalReader } from '../localExecutionSequenceFixture'

// ADR-0060 in the candidate workspace region: the run's one local-execution-sequence reader is an additional veto of verification Run, of
// creating, enabling, disabling and removing a verification command, and of capturing a checkpoint. Reading evidence, Refresh evidence and
// inspection stay operable in every state.

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


async function renderPanel(reader: UseLocalExecutionSequenceResult | null, props: { runWithheldReason?: string | null } = {}) {
  const view = render(
    <CandidateWorkspacePanel
      projectId={PROJECT_ID}
      localExecutionSequence={reader}
      executionCapacity={availableCapacityState()}
      workspaceExecution={stateFor('Idle')}
      {...props}
    />,
  )
  await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #1'))
  await waitFor(() => expect(within(verification()).getByText('#1 unit')).toBeInTheDocument())
  await waitFor(() => expect(within(verification()).queryByText('Reading verification status…')).not.toBeInTheDocument())
  return view
}

const captureButton = () => within(source()).getByRole('button', { name: 'Capture checkpoint' })
const commandButtons = () => ['Disable', 'Remove', 'Add command'].map((name) => within(verification()).getByRole('button', { name }))

describe('CandidateWorkspacePanel local execution sequence fence (ADR-0060)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each([
    ['none', () => noneLocalReader('run-1')],
    ['released', () => localReaderFor(localStatusFor('run-1', 'Stopped'), 'run-1')],
  ])('leaves every control to the ordinary gates when the fence is %s', async (_name, reader) => {
    const clients = installClients()
    await renderPanel(reader())

    expect(runButton()).toBeEnabled()
    expect(captureButton()).toBeEnabled()
    for (const button of commandButtons()) {
      expect(button).toBeEnabled()
    }
    expect(screen.queryByText(describeLocalExecutionSequenceWithholding('blocked')!)).not.toBeInTheDocument()
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
  })

  it.each([
    ['blocked', () => blockedLocalReader('run-1'), 'blocked' as const],
    ['unknown', () => unknownLocalReader('run-1'), 'unknown' as const],
    ['absent', () => null, 'unknown' as const],
  ])('withholds verification Run, command configuration and capture, with the fixed reason, while the fence is %s', async (_name, reader, control) => {
    const clients = installClients()
    await renderPanel(reader())

    expect(runButton()).toBeDisabled()
    expect(captureButton()).toBeDisabled()
    for (const button of commandButtons()) {
      expect(button).toBeDisabled()
    }
    expect(screen.getAllByText(describeLocalExecutionSequenceWithholding(control)!).length).toBeGreaterThan(0)
    fireEvent.click(runButton())
    fireEvent.click(captureButton())
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
    expect(clients.captureGitWorkspaceCheckpoint).not.toHaveBeenCalled()
  })

  it('keeps Refresh evidence and the evidence inspection operable while the fence withholds starts', async () => {
    installClients()
    const onEvidenceRefreshRequested = vi.fn()
    render(
      <CandidateWorkspacePanel
        projectId={PROJECT_ID}
        localExecutionSequence={blockedLocalReader('run-1')}
        executionCapacity={availableCapacityState()}
        workspaceExecution={stateFor('Idle')}
        onEvidenceRefreshRequested={onEvidenceRefreshRequested}
      />,
    )
    await waitFor(() => expect(source()).toHaveTextContent('Checkpoint #1'))

    fireEvent.click(screen.getByRole('button', { name: 'Refresh evidence' }))
    expect(onEvidenceRefreshRequested).toHaveBeenCalledTimes(1)
    expect(within(source()).getByRole('button', { name: /Inspect/ })).toBeEnabled()
  })

  it('does not enable Run for a lifted fence when the ordinary gates disable it', async () => {
    installClients({ checkpoint: false })
    render(<CandidateWorkspacePanel projectId={PROJECT_ID} localExecutionSequence={noneLocalReader()} executionCapacity={availableCapacityState()} workspaceExecution={stateFor('Idle')} />)
    await waitFor(() => expect(within(source()).getByText('No source checkpoint has been captured yet.')).toBeInTheDocument())

    expect(runButton()).toBeDisabled()
  })
})
