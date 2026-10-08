import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  checkpointApprovalEvidenceClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationExecutionsClient,
  recordCheckpointReviewClient,
} from '../../../api/clients'
import {
  ApiException,
  CheckpointApprovalEvidenceMemberResponse,
  CheckpointApprovalEvidenceResponse,
  CheckpointReviewResponse,
  GetProjectGitEvidenceResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { CheckpointReviewPanel } from './CheckpointReviewPanel'

// ADR-0030's complete-set action in the real checkpoint-review surface: real hooks and components over controllable generated
// clients. The action exists only for a settled, successful bundle read that names exactly the displayed source, submits exactly the
// displayed bundle, and its pending guard, continuation and accepted result belong to the committed source's lifetime.

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectGitEvidenceClient: vi.fn(),
  projectVerificationExecutionsClient: vi.fn(),
  projectCheckpointReviewsClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
  checkpointApprovalEvidenceClient: vi.fn(),
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

const fingerprintOf = (projectId: string) => `${projectId}-fingerprint`.padEnd(64, '0')
const checkpointOf = (projectId: string) => `${projectId}-checkpoint`

const evidenceOf = (projectId: string, overrides: Partial<GetProjectGitEvidenceResponse> = {}) =>
  new GetProjectGitEvidenceResponse({
    gitWorkspaceId: `${projectId}-workspace`,
    checkpointId: checkpointOf(projectId),
    checkpointNumber: 2,
    changedFileCount: 0,
    headCommitSha: 'a'.repeat(40),
    fingerprintSha256: fingerprintOf(projectId),
    ...overrides,
  })

const executionOf = (projectId: string, number: number) =>
  new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-execution-${number}`,
    verificationCommandId: `${projectId}-command-${number}`,
    executionNumber: number,
    gitCheckpointId: checkpointOf(projectId),
    checkpointFingerprintSha256: fingerprintOf(projectId),
    status: 'Passed',
    isDispatched: true,
    hasStandardOutput: false,
    hasStandardError: false,
  })

function bundleOf(projectId: string, overrides: Partial<CheckpointApprovalEvidenceResponse> = {}, suffix = '') {
  return new CheckpointApprovalEvidenceResponse({
    projectId,
    workspaceId: `${projectId}-workspace`,
    checkpointId: checkpointOf(projectId),
    checkpointNumber: 2,
    fingerprintSha256: fingerprintOf(projectId),
    members: [1, 2].map(number => new CheckpointApprovalEvidenceMemberResponse({
      verificationCommandId: `${projectId}-command-${number}`,
      commandNumber: number,
      recipeLabel: number === 1 ? 'Backend tests' : 'Lint',
      verificationExecutionId: `${projectId}-execution-${number}${suffix}`,
      executionNumber: number + 10,
    })),
    ...overrides,
  })
}

interface Reads {
  bundle: (projectId: string) => Promise<CheckpointApprovalEvidenceResponse>
  evidence: (projectId: string) => Promise<GetProjectGitEvidenceResponse>
  reviews: (projectId: string) => Promise<CheckpointReviewResponse[]>
  record: (projectId: string, request: unknown) => Promise<unknown>
}

function install(reads: Partial<Reads> = {}) {
  const getCheckpointApprovalEvidence = vi.fn(reads.bundle ?? ((projectId: string) => Promise.resolve(bundleOf(projectId))))
  const getProjectCheckpointReviews = vi.fn(reads.reviews ?? (() => Promise.resolve([])))
  const getProjectGitEvidence = vi.fn(reads.evidence ?? ((projectId: string) => Promise.resolve(evidenceOf(projectId))))
  const getProjectVerificationExecutions = vi.fn((projectId: string) => Promise.resolve([executionOf(projectId, 1), executionOf(projectId, 2)]))
  const recordCheckpointReview = vi.fn(reads.record ?? (() => Promise.resolve({ reviewId: 'new-review', decision: 'Approved' })))
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  vi.mocked(checkpointApprovalEvidenceClient).mockReturnValue({ getCheckpointApprovalEvidence } as unknown as ReturnType<typeof checkpointApprovalEvidenceClient>)
  return { getCheckpointApprovalEvidence, getProjectCheckpointReviews, getProjectGitEvidence, getProjectVerificationExecutions, recordCheckpointReview }
}

const panel = () => screen.getByRole('region', { name: 'Checkpoint review evidence' })
const approveAll = () => within(panel()).getByRole('button', { name: 'Approve all enabled checks' })
const legacyApprove = () => within(panel()).getByRole('button', { name: 'Approve' })
const problem = (code: string) => new ApiException('raw', 409, JSON.stringify({ errors: [{ code, detail: 'host text' }] }), {}, null)

async function settled(projectId = 'project-a', refreshGeneration = 0) {
  const view = render(<CheckpointReviewPanel projectId={projectId} refreshGeneration={refreshGeneration} />)
  await waitFor(() => expect(approveAll()).toBeEnabled())
  return view
}

describe('CheckpointReviewPanel complete-set approval', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows the exact bundle and, on click, submits exactly its executions as one Human approval and refreshes the review list', async () => {
    const clients = install()
    await settled()

    const list = within(panel()).getByRole('list', { name: 'Verification set to approve' })
    expect(within(list).getAllByRole('listitem').map(item => item.textContent)).toEqual([
      '#1 Backend tests · execution #11',
      '#2 Lint · execution #12',
    ])
    expect(within(panel()).getByText(/The explicit local commit requires this complete approval; approving commits nothing\./)).toBeInTheDocument()
    const readsBefore = clients.getProjectCheckpointReviews.mock.calls.length

    fireEvent.click(approveAll())

    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
    const [projectId, request] = clients.recordCheckpointReview.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(projectId).toBe('project-a')
    expect(request).toMatchObject({
      gitCheckpointId: 'project-a-checkpoint',
      actorKind: 'Human',
      decision: 'Approved',
      verificationExecutionIds: ['project-a-execution-1', 'project-a-execution-2'],
    })
    await waitFor(() => expect(clients.getProjectCheckpointReviews.mock.calls.length).toBeGreaterThan(readsBefore))
    await waitFor(() => expect(screen.getByText('Recorded. The Human approval covers these runs.')).toBeInTheDocument())
    expect(approveAll()).toBeDisabled()
  })

  it('offers nothing while the bundle is being read and starts no request from a disabled action', async () => {
    const gate = deferred<CheckpointApprovalEvidenceResponse>()
    const clients = install({ bundle: () => gate.promise })
    render(<CheckpointReviewPanel projectId="project-a" />)
    await waitFor(() => expect(clients.getCheckpointApprovalEvidence).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(legacyApprove()).toBeEnabled())

    expect(approveAll()).toBeDisabled()
    expect(within(panel()).getByText('Reading the complete verification set…')).toBeInTheDocument()
    expect(within(panel()).queryByRole('list', { name: 'Verification set to approve' })).toBeNull()
    fireEvent.click(approveAll())
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()

    await act(async () => {
      gate.resolve(bundleOf('project-a'))
    })
    await waitFor(() => expect(approveAll()).toBeEnabled())
  })

  it.each([
    ['an incomplete set', () => Promise.reject(problem('approval_evidence.verification_incomplete')), 'Every enabled check needs a passing latest run'],
    ['no enabled check', () => Promise.reject(problem('approval_evidence.no_enabled_recipes')), 'Enable at least one verification check'],
    ['too many checks', () => Promise.reject(problem('approval_evidence.too_many_recipes')), 'More than 32 verification checks are enabled'],
    ['a failed read', () => Promise.reject(new Error('down')), 'could not be read'],
    ['a malformed body', () => Promise.resolve({ unexpected: true } as unknown as CheckpointApprovalEvidenceResponse), 'could not be read'],
    ['another source', () => Promise.resolve(bundleOf('project-b')), 'could not be read'],
  ])('withholds the action after %s with a fixed message and no host text', async (_name, bundle, message) => {
    const clients = install({ bundle })
    render(<CheckpointReviewPanel projectId="project-a" />)

    await waitFor(() => expect(within(panel()).getByText(new RegExp(message))).toBeInTheDocument())

    expect(approveAll()).toBeDisabled()
    expect(within(panel()).queryByText('host text')).toBeNull()
    await waitFor(() => expect(legacyApprove()).toBeEnabled())
    fireEvent.click(approveAll())
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()
  })

  it('hides the complete-set action for a non-Human reviewer', async () => {
    install()
    await settled()

    fireEvent.change(screen.getByRole('combobox', { name: 'Reviewer' }), { target: { value: 'FutureAgent' } })

    expect(within(panel()).queryByRole('button', { name: 'Approve all enabled checks' })).toBeNull()
    fireEvent.change(screen.getByRole('combobox', { name: 'Reviewer' }), { target: { value: 'Human' } })
    expect(approveAll()).toBeEnabled()
  })

  it('states the difference between the selected-run approval and the complete set when several checks are enabled', async () => {
    install()
    await settled()

    expect(within(panel()).getByText(/Approve records only the selected run\./)).toBeInTheDocument()
  })

  it('withholds the action while only the refreshed bundle is still being read, though the source and runs have settled', async () => {
    const reads = [() => Promise.resolve(bundleOf('project-a')), () => new Promise<CheckpointApprovalEvidenceResponse>(() => undefined)]
    install({ bundle: () => reads.shift()!() })
    const view = await settled()

    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)

    // Source, execution and review reads of the new generation settle; only the bundle read stays pending, so the single-run
    // decisions come back while the complete-set action stays withheld with the cached bundle shown as nothing but a pending read.
    await waitFor(() => expect(legacyApprove()).toBeEnabled())
    expect(approveAll()).toBeDisabled()
    expect(within(panel()).getByText('Reading the complete verification set…')).toBeInTheDocument()
    expect(within(panel()).queryByRole('list', { name: 'Verification set to approve' })).toBeNull()
    fireEvent.click(approveAll())
    expect(vi.mocked(recordCheckpointReviewClient)().recordCheckpointReview).not.toHaveBeenCalled()
  })

  it('withholds the action after the refreshed bundle read failed while the rest stays current', async () => {
    const reads = [() => Promise.resolve(bundleOf('project-a')), () => Promise.reject(new Error('down'))]
    install({ bundle: () => reads.shift()!() })
    const view = await settled()

    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)

    await waitFor(() => expect(within(panel()).getByText(/could not be read/)).toBeInTheDocument())
    await waitFor(() => expect(legacyApprove()).toBeEnabled())
    expect(approveAll()).toBeDisabled()
    expect(within(panel()).queryByRole('list', { name: 'Verification set to approve' })).toBeNull()
  })

  it('blocks the other decisions while the set approval is pending, and the set approval while another decision is pending', async () => {
    const gate = deferred<unknown>()
    const clients = install({ record: () => gate.promise })
    await settled()

    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    await waitFor(() => expect(approveAll()).toBeDisabled())
    expect(legacyApprove()).toBeDisabled()
    expect(within(panel()).getByRole('button', { name: 'Pending' })).toBeDisabled()
    fireEvent.click(approveAll())
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)

    await act(async () => {
      gate.resolve({ reviewId: 'r', decision: 'Approved' })
    })
    await waitFor(() => expect(within(panel()).getByRole('button', { name: 'Pending' })).toBeEnabled())
  })

  it('disables the set approval while a legacy decision is pending', async () => {
    const gate = deferred<unknown>()
    const clients = install({ record: () => gate.promise })
    await settled()

    fireEvent.click(within(panel()).getByRole('button', { name: 'Pending' }))
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    await waitFor(() => expect(approveAll()).toBeDisabled())
    fireEvent.click(approveAll())
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
    await act(async () => {
      gate.resolve({ reviewId: 'r', decision: 'Pending' })
    })
  })

  it('reports a refused approval with fixed text, keeps the action available and never retries', async () => {
    const clients = install({ record: () => Promise.reject(problem('reviews.approval_requires_complete_verification_set')) })
    await settled()

    fireEvent.click(approveAll())

    await waitFor(() => expect(within(panel()).getByText(/The enabled checks changed since they were read/)).toBeInTheDocument())
    expect(within(panel()).queryByText('host text')).toBeNull()
    expect(approveAll()).toBeEnabled()
    await new Promise(resolve => setTimeout(resolve, 30))
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('keeps an accepted approval accepted when the follow-up review refresh fails, without retry or a failure label', async () => {
    let reviewReads = 0
    const clients = install({
      reviews: () => (++reviewReads === 1 ? Promise.resolve([]) : Promise.reject(new Error('refresh failed'))),
    })
    await settled()

    fireEvent.click(approveAll())

    await waitFor(() => expect(screen.getByText('Recorded. The Human approval covers these runs.')).toBeInTheDocument())
    await waitFor(() => expect(reviewReads).toBeGreaterThan(1))
    expect(within(panel()).queryByText(/could not be recorded/)).toBeNull()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
    await new Promise(resolve => setTimeout(resolve, 30))
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('does not let an approval accepted for one project refresh, report on or block the replacement project', async () => {
    const gate = deferred<unknown>()
    const clients = install({ record: () => gate.promise })
    const view = await settled('project-a')
    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    view.rerender(<CheckpointReviewPanel projectId="project-b" />)
    await waitFor(() => expect(approveAll()).toBeEnabled())
    const reviewReadsForB = clients.getProjectCheckpointReviews.mock.calls.filter(([project]) => project === 'project-b').length

    await act(async () => {
      gate.resolve({ reviewId: 'accepted-for-a', decision: 'Approved' })
      await Promise.resolve()
    })

    expect(screen.queryByText('Recorded. The Human approval covers these runs.')).toBeNull()
    expect(clients.getProjectCheckpointReviews.mock.calls.filter(([project]) => project === 'project-b')).toHaveLength(reviewReadsForB)
    expect(approveAll()).toBeEnabled()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('treats A to B to A as new lifetimes: the first visit never marks the later one approved or busy', async () => {
    const posts: ReturnType<typeof deferred<unknown>>[] = []
    const clients = install({
      record: () => {
        const gate = deferred<unknown>()
        posts.push(gate)
        return gate.promise
      },
    })
    const view = await settled('project-a')
    fireEvent.click(approveAll())
    await waitFor(() => expect(posts).toHaveLength(1))
    view.rerender(<CheckpointReviewPanel projectId="project-b" />)
    await waitFor(() => expect(approveAll()).toBeEnabled())
    view.rerender(<CheckpointReviewPanel projectId="project-a" />)
    await waitFor(() => expect(approveAll()).toBeEnabled())

    await act(async () => {
      posts[0].resolve({ reviewId: 'first-visit', decision: 'Approved' })
      await Promise.resolve()
    })

    expect(screen.queryByText('Recorded. The Human approval covers these runs.')).toBeNull()
    expect(approveAll()).toBeEnabled()
    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(2))
  })

  it('submits the displayed bundle even when the member set changed in a later read, never the earlier one', async () => {
    const bundles = [bundleOf('project-a'), bundleOf('project-a', {}, '-rerun')]
    const clients = install({ bundle: () => Promise.resolve(bundles.shift()!) })
    const view = await settled()
    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)
    await waitFor(() => expect(within(panel()).getAllByRole('listitem')[0].textContent).toContain('execution #11'))
    await waitFor(() => expect(approveAll()).toBeEnabled())

    fireEvent.click(approveAll())

    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
    const [, request] = clients.recordCheckpointReview.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(request.verificationExecutionIds).toEqual(['project-a-execution-1-rerun', 'project-a-execution-2-rerun'])
  })
})

// The complete-set action is bound to every admitted fact of the observed source: the workspace and the checkpoint number take part
// in the decoding and in the lifetime, so a mismatching, missing or replaced identity withholds it and is never repaired.
describe('CheckpointReviewPanel complete-set approval source identity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  async function withheld(clients: ReturnType<typeof install>, message: RegExp) {
    render(<CheckpointReviewPanel projectId="project-a" />)
    await waitFor(() => expect(within(panel()).getByText(message)).toBeInTheDocument())
    await waitFor(() => expect(legacyApprove()).toBeEnabled())
    expect(approveAll()).toBeDisabled()
    expect(within(panel()).queryByRole('list', { name: 'Verification set to approve' })).toBeNull()
    fireEvent.click(approveAll())
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()
  }

  it.each([
    ['a foreign workspace', { workspaceId: 'workspace-foreign' }],
    ['an absent workspace', { workspaceId: undefined }],
    ['a different checkpoint number', { checkpointNumber: 999 }],
    ['an absent checkpoint number', { checkpointNumber: undefined }],
  ])('withholds the action for a bundle with %s', async (_name, overrides) => {
    const clients = install({ bundle: projectId => Promise.resolve(bundleOf(projectId, overrides)) })

    await withheld(clients, /could not be read/)
  })

  it.each([
    ['no workspace', { gitWorkspaceId: undefined }],
    ['a blank workspace', { gitWorkspaceId: '  ' }],
    ['no checkpoint number', { checkpointNumber: undefined }],
    ['a zero checkpoint number', { checkpointNumber: 0 }],
  ])('reads no bundle and withholds the action when the observed source has %s', async (_name, overrides) => {
    const clients = install({ evidence: projectId => Promise.resolve(evidenceOf(projectId, overrides)) })

    await withheld(clients, /source identity is incomplete/)

    expect(clients.getCheckpointApprovalEvidence).not.toHaveBeenCalled()
  })

  it('offers the action and submits for a bundle that repeats every admitted fact of the observed source', async () => {
    const clients = install({ evidence: projectId => Promise.resolve(evidenceOf(projectId, { checkpointNumber: 7 })), bundle: projectId => Promise.resolve(bundleOf(projectId, { checkpointNumber: 7 })) })
    await settled()

    expect(within(panel()).getByText(/for Checkpoint #7\./)).toBeInTheDocument()
    fireEvent.click(approveAll())

    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
  })

  it('treats a replaced workspace of the same checkpoint as a new lifetime: the earlier approval neither reports on nor blocks it', async () => {
    const gate = deferred<unknown>()
    let workspace = 'workspace-first'
    const clients = install({
      evidence: projectId => Promise.resolve(evidenceOf(projectId, { gitWorkspaceId: workspace })),
      bundle: projectId => Promise.resolve(bundleOf(projectId, { workspaceId: workspace })),
      record: () => gate.promise,
    })
    const view = await settled()
    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    workspace = 'workspace-second'
    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)
    await waitFor(() => expect(approveAll()).toBeEnabled())
    const reviewReads = clients.getProjectCheckpointReviews.mock.calls.length

    await act(async () => {
      gate.reject(problem('reviews.checkpoint_not_current'))
      await Promise.resolve()
    })

    expect(within(panel()).queryByText(/no longer current or is busy/)).toBeNull()
    expect(clients.getProjectCheckpointReviews.mock.calls).toHaveLength(reviewReads)
    expect(approveAll()).toBeEnabled()
  })
})

// The pending approval, its failure and its follow-up belong to the committed bundle: a replaced member set ends them.
describe('CheckpointReviewPanel complete-set approval bundle lifetime', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('writes neither an old failure nor a busy state onto replacement members', async () => {
    const gate = deferred<unknown>()
    const bundles = [() => bundleOf('project-a'), () => bundleOf('project-a', {}, '-rerun')]
    const clients = install({ bundle: () => Promise.resolve(bundles.shift()!()), record: () => gate.promise })
    const view = await settled()
    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)
    await waitFor(() => expect(within(panel()).getAllByRole('listitem')[0].textContent).toContain('execution #11'))
    await waitFor(() => expect(approveAll()).toBeEnabled())
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)

    await act(async () => {
      gate.reject(problem('reviews.approval_requires_complete_verification_set'))
      await Promise.resolve()
    })

    expect(within(panel()).queryByText(/The enabled checks changed/)).toBeNull()
    expect(approveAll()).toBeEnabled()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('does not mark replacement members approved or refresh them when an old approval is accepted', async () => {
    const gate = deferred<unknown>()
    const bundles = [() => bundleOf('project-a'), () => bundleOf('project-a', {}, '-rerun')]
    const clients = install({ bundle: () => Promise.resolve(bundles.shift()!()), record: () => gate.promise })
    const view = await settled()
    fireEvent.click(approveAll())
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)
    await waitFor(() => expect(within(panel()).getAllByRole('listitem')[0].textContent).toContain('execution #11'))
    await waitFor(() => expect(approveAll()).toBeEnabled())
    const reviewReads = clients.getProjectCheckpointReviews.mock.calls.length

    await act(async () => {
      gate.resolve({ reviewId: 'accepted-for-the-old-members', decision: 'Approved' })
      await Promise.resolve()
    })

    expect(screen.queryByText('Recorded. The Human approval covers these runs.')).toBeNull()
    expect(approveAll()).toBeEnabled()
    expect(clients.getProjectCheckpointReviews.mock.calls).toHaveLength(reviewReads)
  })

  it('starts nothing from a retained earlier bundle after the same source goes A to B to A', async () => {
    const members = [() => bundleOf('project-a'), () => bundleOf('project-a', {}, '-b'), () => bundleOf('project-a')]
    const clients = install({ bundle: () => Promise.resolve(members.shift()!()) })
    const view = await settled()
    const firstClick = approveAll()
    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={1} />)
    await waitFor(() => expect(within(panel()).getAllByRole('listitem')[0].textContent).toContain('execution #11'))
    view.rerender(<CheckpointReviewPanel projectId="project-a" refreshGeneration={2} />)
    await waitFor(() => expect(clients.getCheckpointApprovalEvidence).toHaveBeenCalledTimes(3))
    await waitFor(() => expect(approveAll()).toBeEnabled())

    fireEvent.click(firstClick)
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))

    // One click of the single rendered button: the bundle it submits is the third read, with the original member identities.
    const [, request] = clients.recordCheckpointReview.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(request.verificationExecutionIds).toEqual(['project-a-execution-1', 'project-a-execution-2'])
    await act(async () => {
      await Promise.resolve()
    })
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })
})
