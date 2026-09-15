import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  projectGitEvidenceClient,
} from '../../../api/clients'
import {
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GitCheckpointChangedFileResponse,
} from '../../../api/generated/api-client'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'

vi.mock('../../../api/clients', () => ({
  projectGitEvidenceClient: vi.fn(),
  captureGitWorkspaceCheckpointClient: vi.fn(),
  gitCheckpointChangedFilesClient: vi.fn(),
  gitCheckpointDiffClient: vi.fn(),
}))

describe('WorkspaceEvidencePanel', () => {
  it('shows only checkpoint-bound changed files and diff after a fresh inspection request', async () => {
    vi.mocked(projectGitEvidenceClient).mockReturnValue({
      getProjectGitEvidence: vi.fn().mockResolvedValue(new GetProjectGitEvidenceResponse({
        checkpointId: 'checkpoint-1',
        checkpointNumber: 1,
        headCommitSha: 'a'.repeat(40),
        fingerprintSha256: 'b'.repeat(64),
        changedFileCount: 1,
      })),
    } as unknown as ReturnType<typeof projectGitEvidenceClient>)
    vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({
      captureGitWorkspaceCheckpoint: vi.fn(),
    } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
    vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({
      getGitCheckpointChangedFiles: vi.fn().mockResolvedValue([
        new GitCheckpointChangedFileResponse({ path: 'src/App.tsx', indexStatus: ' ', workTreeStatus: 'M' }),
      ]),
    } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
    vi.mocked(gitCheckpointDiffClient).mockReturnValue({
      getGitCheckpointDiff: vi.fn().mockResolvedValue(new GetGitCheckpointDiffResponse({
        fingerprintSha256: 'b'.repeat(64),
        completeDiff: 'diff --git a/src/App.tsx b/src/App.tsx',
      })),
    } as unknown as ReturnType<typeof gitCheckpointDiffClient>)

    render(<WorkspaceEvidencePanel projectId="project-1" workspaceReady />)

    expect(await screen.findByText('Checkpoint #1 · 1 changed files')).toBeInTheDocument()
    expect(screen.queryByText('src/App.tsx')).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Inspect files & diff'))

    await waitFor(() => expect(screen.getByText('src/App.tsx')).toBeInTheDocument())
    expect(screen.getByText('diff --git a/src/App.tsx b/src/App.tsx')).toBeInTheDocument()
  })
})
