import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  prepareWorkspaceClient,
  projectGitEvidenceClient,
  projectWorkspaceClient,
  recheckPhysicalIdentityClient,
} from '../../../api/clients'
import {
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  PrepareRepositoryWorkspaceResponse,
  RecheckProjectPhysicalIdentityResponse,
} from '../../../api/generated/api-client'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'

vi.mock('../../../api/clients', () => ({
  projectWorkspaceClient: vi.fn(),
  prepareWorkspaceClient: vi.fn(),
  recheckPhysicalIdentityClient: vi.fn(),
  projectGitEvidenceClient: vi.fn(),
  captureGitWorkspaceCheckpointClient: vi.fn(),
  gitCheckpointChangedFilesClient: vi.fn(),
  gitCheckpointDiffClient: vi.fn(),
}))

vi.mocked(projectGitEvidenceClient).mockReturnValue({
  getProjectGitEvidence: vi.fn().mockResolvedValue(new GetProjectGitEvidenceResponse({ changedFileCount: 0 })),
} as unknown as ReturnType<typeof projectGitEvidenceClient>)
vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({
  captureGitWorkspaceCheckpoint: vi.fn(),
} as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({
  getGitCheckpointChangedFiles: vi.fn(),
} as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
vi.mocked(gitCheckpointDiffClient).mockReturnValue({
  getGitCheckpointDiff: vi.fn(),
} as unknown as ReturnType<typeof gitCheckpointDiffClient>)

function mockGetWorkspace(response: GetProjectWorkspaceResponse) {
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(response),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
}

describe('CandidateWorkspacePanel', () => {
  it('offers a bounded recheck action when physical identity blocks preparation, never a spinner-only wait', async () => {
    mockGetWorkspace(
      new GetProjectWorkspaceResponse({
        physicalIdentityStatus: 'Unavailable',
        physicalIdentityBlockedMessage: "This project's path could not be verified.",
        state: 'NotRequested',
      }),
    )
    const recheckProjectPhysicalIdentity = vi.fn().mockResolvedValue(new RecheckProjectPhysicalIdentityResponse({ status: 'Resolved' }))
    vi.mocked(recheckPhysicalIdentityClient).mockReturnValue({
      recheckProjectPhysicalIdentity,
    } as unknown as ReturnType<typeof recheckPhysicalIdentityClient>)

    render(<CandidateWorkspacePanel projectId="project-1" />)

    expect(await screen.findByText("This project's path could not be verified.")).toBeInTheDocument()
    expect(screen.queryByText('Prepare workspace')).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Recheck identity'))

    await waitFor(() => expect(recheckProjectPhysicalIdentity).toHaveBeenCalledWith('project-1'))
  })

  it('offers preparation once physical identity is resolved and nothing has been requested yet', async () => {
    mockGetWorkspace(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'NotRequested' }),
    )
    const prepareRepositoryWorkspace = vi.fn().mockResolvedValue(
      new PrepareRepositoryWorkspaceResponse({
        workspaceId: 'workspace-1',
        workspacePath: String.raw`C:\Users\dev\AppData\Local\DevalCopilot\workspaces\p\1`,
        branchName: 'devalcopilot/workspace/p/1',
        sourceCommitSha: 'a'.repeat(40),
      }),
    )
    vi.mocked(prepareWorkspaceClient).mockReturnValue({
      prepareRepositoryWorkspace,
    } as unknown as ReturnType<typeof prepareWorkspaceClient>)

    render(<CandidateWorkspacePanel projectId="project-1" />)

    expect(await screen.findByText('Prepare workspace')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Prepare workspace'))

    await waitFor(() => expect(prepareRepositoryWorkspace).toHaveBeenCalledWith('project-1'))
  })

  it('renders the candidate path and branch distinctly once ready, never confused with the stable checkout', async () => {
    mockGetWorkspace(
      new GetProjectWorkspaceResponse({
        physicalIdentityStatus: 'Resolved',
        state: 'Ready',
        candidatePath: String.raw`C:\Users\dev\AppData\Local\DevalCopilot\workspaces\p\1`,
        branchName: 'devalcopilot/workspace/p/1',
        sourceCommitSha: 'c'.repeat(40),
        sourceBranchName: 'main',
      }),
    )

    render(<CandidateWorkspacePanel projectId="project-1" />)

    expect(await screen.findByText(String.raw`C:\Users\dev\AppData\Local\DevalCopilot\workspaces\p\1`)).toBeInTheDocument()
    expect(screen.getByText(/devalcopilot\/workspace\/p\/1 @ ccccccc/)).toBeInTheDocument()
    expect(screen.getByText('Candidate workspace (isolated)')).toBeInTheDocument()
  })

  it('flags needs-attention content divergence with its own safe message', async () => {
    mockGetWorkspace(
      new GetProjectWorkspaceResponse({
        physicalIdentityStatus: 'Resolved',
        state: 'NeedsAttention',
        candidatePath: String.raw`C:\Users\dev\AppData\Local\DevalCopilot\workspaces\p\1`,
        branchName: 'devalcopilot/workspace/p/1',
        sourceCommitSha: 'd'.repeat(40),
        blockedReasonMessage: 'This workspace\'s content changed outside DevalCopilot and needs review.',
      }),
    )

    render(<CandidateWorkspacePanel projectId="project-1" />)

    expect(await screen.findByText("This workspace's content changed outside DevalCopilot and needs review.")).toBeInTheDocument()
  })
})
