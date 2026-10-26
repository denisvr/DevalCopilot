import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
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
import { PROJECT, pageFor, unavailablePageFor } from '../projectVerificationInspectionFixture'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'
import { idleStateFor } from '../workspaceExecutionFixture'
import { availableCapacityState } from '../executionCapacityFixture'
import { noneLocalReader } from '../localExecutionSequenceFixture'

// The recorded verification region lives in the project workspace's verification area, beside (never inside) the current-run controls:
// real components and hooks over the real generated history client on a fake network, with the live project reads controllable.

vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))
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

const HISTORY = `http://127.0.0.1:5099/api/projects/${PROJECT}/verification-history`
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function installClients() {
  const claimVerificationExecution = vi.fn().mockResolvedValue({ verificationExecutionId: 'claimed', executionNumber: 9 })
  const getProjectVerificationExecutions = vi.fn().mockResolvedValue([])
  vi.mocked(projectWorkspaceClient).mockReturnValue({
    getProjectWorkspace: vi.fn().mockResolvedValue(
      new GetProjectWorkspaceResponse({ physicalIdentityStatus: 'Resolved', state: 'Ready', candidatePath: String.raw`C:\w`, branchName: 'b', sourceCommitSha: 'c'.repeat(40) }),
    ),
  } as unknown as ReturnType<typeof projectWorkspaceClient>)
  vi.mocked(projectGitEvidenceClient).mockReturnValue({
    getProjectGitEvidence: vi.fn().mockResolvedValue(
      new GetProjectGitEvidenceResponse({ checkpointId: 'checkpoint', checkpointNumber: 1, changedFileCount: 0, headCommitSha: 'a'.repeat(40), fingerprintSha256: 'f'.repeat(64), gitWorkspaceId: 'workspace' }),
    ),
  } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint: vi.fn() } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview: vi.fn() } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({ getGitCheckpointChangedFiles: vi.fn() } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
  vi.mocked(gitCheckpointDiffClient).mockReturnValue({ getGitCheckpointDiff: vi.fn() } as unknown as ReturnType<typeof gitCheckpointDiffClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn().mockResolvedValue([
      new VerificationCommandResponse({
        verificationCommandId: 'command-1',
        commandNumber: 1,
        name: 'unit check',
        executablePath: String.raw`C:\tools\verify.exe`,
        arguments: ['unit'],
        timeoutSeconds: 300,
        isEnabled: true,
      }),
    ]),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  return { claimVerificationExecution, getProjectVerificationExecutions }
}

function installNetwork(handler: () => Promise<Response> = () => Promise.resolve(json(pageFor(45, 20)))) {
  const requests: string[] = []
  // The workspace's own live reads (for example the checkpoint approval evidence) are not this region's: only its routes are recorded.
  vi.spyOn(window, 'fetch').mockImplementation((input) => {
    const url = String(input)
    if (!url.includes('/verification-history')) {
      return Promise.resolve(json({ errors: [] }, 404))
    }

    requests.push(url)
    return handler()
  })
  return requests
}

const REGION = 'Inspect recorded verifications'
const commands = () => screen.getByRole('region', { name: 'Verification commands' })
const runButton = () => within(commands()).getByRole('button', { name: /^(Run|Pending…|Running…|Starting…)/ })

function mount(projectId = PROJECT) {
  return (
    <CandidateWorkspacePanel
      projectId={projectId}
      localExecutionSequence={noneLocalReader()}
      executionCapacity={availableCapacityState()}
      workspaceExecution={idleStateFor(projectId)}
    />
  )
}

async function settled(projectId = PROJECT) {
  const view = render(mount(projectId))
  await waitFor(() => expect(runButton()).toBeEnabled())
  return view
}

describe('the recorded verification region in the project workspace', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is mounted exactly once, beside and after the verification commands, never inside the current-run controls', async () => {
    installClients()
    installNetwork()
    await settled()

    const regions = screen.getAllByRole('region', { name: REGION })
    expect(regions).toHaveLength(1)
    const region = regions[0]
    expect(commands().contains(region)).toBe(false)
    expect(region.contains(commands())).toBe(false)
    expect(commands().compareDocumentPosition(region) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByLabelText('Candidate workspace').contains(region)).toBe(true)
    expect(within(region).getByRole('button', { name: REGION })).toHaveAttribute('aria-expanded', 'false')
  })

  it('reads nothing while collapsed, then only the newest page when opened, and leaves Run and the configuration actions exactly as they were', async () => {
    const clients = installClients()
    const requests = installNetwork()
    await settled()
    const before = {
      run: runButton().hasAttribute('disabled'),
      actions: within(commands()).getAllByRole('button').map((button) => [button.textContent, button.hasAttribute('disabled')]),
      executionReads: clients.getProjectVerificationExecutions.mock.calls.length,
    }

    expect(requests).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: REGION }))
    await screen.findByText('Recorded verifications #45 to #26.')
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #45' }))
    await waitFor(() => expect(requests).toHaveLength(2))

    expect(requests[0]).toBe(HISTORY)
    expect(requests.every((url) => url.includes('/verification-history'))).toBe(true)
    expect(runButton().hasAttribute('disabled')).toBe(before.run)
    expect(within(commands()).getAllByRole('button').map((button) => [button.textContent, button.hasAttribute('disabled')])).toEqual(before.actions)
    expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(before.executionReads)
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  })

  it('does not change Run or the live list when its own read fails or answers an unavailable page', async () => {
    const clients = installClients()
    installNetwork(() => Promise.resolve(json({ errors: [] }, 500)))
    await settled()
    fireEvent.click(screen.getByRole('button', { name: REGION }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The recorded verifications could not be loaded.')
    expect(runButton()).toBeEnabled()
    expect(clients.claimVerificationExecution).not.toHaveBeenCalled()

    vi.restoreAllMocks()
    installNetwork(() => Promise.resolve(json(unavailablePageFor())))
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded verifications' }))
    expect(await screen.findByText(/Recorded verifications unavailable/)).toBeInTheDocument()
    expect(runButton()).toBeEnabled()
  })

  it('starts closed and cold for another project and for a return to an earlier one', async () => {
    installClients()
    const requests = installNetwork()
    const view = await settled()
    fireEvent.click(screen.getByRole('button', { name: REGION }))
    await screen.findByText('Recorded verifications #45 to #26.')

    view.rerender(mount('another-project'))
    await waitFor(() => expect(screen.getByRole('button', { name: REGION })).toHaveAttribute('aria-expanded', 'false'))
    expect(screen.queryByText('Recorded verifications #45 to #26.')).not.toBeInTheDocument()

    view.rerender(mount(PROJECT))
    await waitFor(() => expect(screen.getByRole('button', { name: REGION })).toHaveAttribute('aria-expanded', 'false'))
    expect(screen.queryByText('Recorded verifications #45 to #26.')).not.toBeInTheDocument()
    expect(requests).toEqual([HISTORY])
  })
})
