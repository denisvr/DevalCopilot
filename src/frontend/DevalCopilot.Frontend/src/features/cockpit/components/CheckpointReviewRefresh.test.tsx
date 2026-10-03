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
  CheckpointReviewResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { CandidateWorkspacePanel } from './CandidateWorkspacePanel'

// The manual checkpoint review's evidence follows the project's explicit Refresh evidence generation: a decided action needs
// settled, successful source AND verification-execution reads of the displayed generation, a cached Passed execution is never
// eligible while its refresh is pending or failed, Pending stays execution-free, and review applicability is never shown as
// freshly confirmed while its read is pending or failed. Real hooks and components over controllable generated clients.

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

const fingerprintOf = (projectId: string) => `${projectId}-fingerprint`.padEnd(64, '0')

function evidenceOf(projectId: string) {
  return new GetProjectGitEvidenceResponse({
    checkpointId: `${projectId}-checkpoint`,
    checkpointNumber: 1,
    changedFileCount: 0,
    headCommitSha: 'a'.repeat(40),
    fingerprintSha256: fingerprintOf(projectId),
  })
}

function executionOf(projectId: string, number: number, status: string) {
  return new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-execution-${number}`,
    verificationCommandId: `${projectId}-command`,
    executionNumber: number,
    gitCheckpointId: `${projectId}-checkpoint`,
    checkpointFingerprintSha256: fingerprintOf(projectId),
    status,
    isDispatched: true,
    hasStandardOutput: false,
    hasStandardError: false,
  })
}

function reviewOf(projectId: string, isApplicable: boolean) {
  return new CheckpointReviewResponse({
    reviewId: `${projectId}-review`,
    gitCheckpointId: `${projectId}-checkpoint`,
    checkpointNumber: 1,
    decision: 'Approved',
    actorKind: 'Human',
    isApplicable,
    staleReasonCode: isApplicable ? undefined : 'review.source_changed',
    evidence: [],
  })
}

interface Reads {
  executions: (projectId: string) => Promise<VerificationExecutionResponse[]>
  reviews: (projectId: string) => Promise<CheckpointReviewResponse[]>
}

function installClients(reads: Partial<Reads> = {}) {
  const getProjectVerificationExecutions = vi.fn(reads.executions ?? ((projectId: string) => Promise.resolve([executionOf(projectId, 1, 'Passed')])))
  const getProjectCheckpointReviews = vi.fn(reads.reviews ?? (() => Promise.resolve([])))
  const getProjectGitEvidence = vi.fn((projectId: string) => Promise.resolve(evidenceOf(projectId)))
  const captureGitWorkspaceCheckpoint = vi.fn()
  const claimVerificationExecution = vi.fn()
  const recordCheckpointReview = vi.fn().mockResolvedValue({ reviewId: 'new-review', decision: 'Pending' })
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
    getProjectVerificationCommands: vi.fn((projectId: string) => Promise.resolve([
      new VerificationCommandResponse({ verificationCommandId: `${projectId}-command`, commandNumber: 1, name: `${projectId} check`, executablePath: String.raw`C:\t.exe`, arguments: [], timeoutSeconds: 60, isEnabled: true }),
    ])),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  vi.mocked(projectCheckpointReviewsClient).mockReturnValue({ getProjectCheckpointReviews } as unknown as ReturnType<typeof projectCheckpointReviewsClient>)
  return { getProjectVerificationExecutions, getProjectCheckpointReviews, getProjectGitEvidence, captureGitWorkspaceCheckpoint, claimVerificationExecution, recordCheckpointReview }
}

const review = () => screen.getByRole('region', { name: 'Checkpoint review evidence' })
const button = (name: string) => within(review()).getByRole('button', { name })
const refreshButton = () => screen.getByRole('button', { name: 'Refresh evidence' })

async function renderSettled(projectId = 'project-a') {
  const view = render(<CandidateWorkspacePanel projectId={projectId} />)
  await waitFor(() => expect(button('Approve')).toBeEnabled())
  return view
}

function expectNoMutationRequested(clients: ReturnType<typeof installClients>) {
  expect(clients.recordCheckpointReview).not.toHaveBeenCalled()
  expect(clients.claimVerificationExecution).not.toHaveBeenCalled()
  expect(clients.captureGitWorkspaceCheckpoint).not.toHaveBeenCalled()
}

describe('CheckpointReviewPanel evidence refresh generation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('offers no decision in the first frames and enables each only as its own read settles', async () => {
    const executions = deferred<VerificationExecutionResponse[]>()
    const reviews = deferred<CheckpointReviewResponse[]>()
    const clients = installClients({ executions: () => executions.promise, reviews: () => reviews.promise })
    const source = deferred<GetProjectGitEvidenceResponse>()
    clients.getProjectGitEvidence.mockImplementation(() => source.promise)

    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(within(review()).getByText('Reading the current source checkpoint…')).toBeInTheDocument())
    for (const name of ['Pending', 'Changes requested', 'Escalate', 'Approve']) {
      expect(within(review()).queryByRole('button', { name })).not.toBeInTheDocument()
    }

    // The source read settles first: Pending is execution-free and may be recorded; decided actions still wait for evidence.
    await act(async () => source.resolve(evidenceOf('project-a')))
    await waitFor(() => expect(button('Pending')).toBeEnabled())
    expect(within(review()).getByText('Reading verification evidence…')).toBeInTheDocument()
    expect(button('Changes requested')).toBeDisabled()
    expect(button('Escalate')).toBeDisabled()
    expect(button('Approve')).toBeDisabled()
    fireEvent.click(button('Approve'))
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()

    await act(async () => executions.resolve([executionOf('project-a', 1, 'Passed')]))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expect(button('Changes requested')).toBeEnabled()
    expect(within(review()).queryByText('Reading verification evidence…')).not.toBeInTheDocument()
    await act(async () => reviews.resolve([]))
  })

  it('withholds decided actions on stale Passed evidence while the refresh read is pending, then offers them for the fresh answer', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const clients = installClients({ executions: (projectId) => (gated ? gate.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')])) })
    await renderSettled()

    gated = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(review()).getByText('Reading verification evidence…')).toBeInTheDocument())
    expect(button('Approve')).toBeDisabled()
    expect(button('Changes requested')).toBeDisabled()
    expect(button('Escalate')).toBeDisabled()
    expect(button('Pending')).toBeEnabled() // source evidence is current and Pending names no execution
    fireEvent.click(button('Approve'))
    fireEvent.click(button('Changes requested'))
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()

    await act(async () => gate.resolve([executionOf('project-a', 1, 'Failed')]))
    await waitFor(() => expect(button('Changes requested')).toBeEnabled())
    expect(button('Approve')).toBeDisabled() // the fresh answer is Failed: never approvable
    expectNoMutationRequested(clients)
  })

  it('never submits a decided review from a click made while the execution read is pending', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const clients = installClients({ executions: (projectId) => (gated ? gate.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')])) })
    await renderSettled()
    gated = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button('Approve')).toBeDisabled())

    await act(async () => gate.resolve([executionOf('project-a', 1, 'Passed')]))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    fireEvent.click(button('Approve'))

    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
    expect(clients.recordCheckpointReview.mock.calls[0][1]).toMatchObject({
      gitCheckpointId: 'project-a-checkpoint',
      verificationExecutionId: 'project-a-execution-1',
      decision: 'Approved',
      actorKind: 'Human',
    })
  })

  it('keeps decided actions withheld after a failed execution read, keeps Pending available, and recovers on the next successful refresh', async () => {
    let failing = false
    const clients = installClients({
      executions: (projectId) => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve([executionOf(projectId, 1, 'Passed')])),
    })
    await renderSettled()

    failing = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(review()).getByText(/Verification evidence could not be refreshed/)).toBeInTheDocument())
    expect(button('Approve')).toBeDisabled()
    expect(button('Changes requested')).toBeDisabled()
    expect(button('Escalate')).toBeDisabled()
    expect(button('Pending')).toBeEnabled()
    expect(within(review()).queryByText(/No terminal verification evidence is available/)).not.toBeInTheDocument()

    failing = false
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expect(within(review()).queryByText(/could not be refreshed/)).not.toBeInTheDocument()
    expectNoMutationRequested(clients)
  })

  it('withholds every action while the source read is pending even when the execution read is fresh', async () => {
    const source = deferred<GetProjectGitEvidenceResponse>()
    let gated = false
    const clients = installClients()
    clients.getProjectGitEvidence.mockImplementation((projectId: string) => (gated ? source.promise : Promise.resolve(evidenceOf(projectId))))
    await renderSettled()

    gated = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(button('Pending')).toBeDisabled())
    await waitFor(() => expect(clients.getProjectVerificationExecutions.mock.calls.length).toBeGreaterThanOrEqual(2))
    expect(button('Approve')).toBeDisabled()
    expect(button('Changes requested')).toBeDisabled()

    await act(async () => source.resolve(evidenceOf('project-a')))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expectNoMutationRequested(clients)
  })

  it('does not present cached review applicability as confirmed while its read is pending, and confirms it afterwards', async () => {
    const gate = deferred<CheckpointReviewResponse[]>()
    let gated = false
    installClients({ reviews: (projectId) => (gated ? gate.promise : Promise.resolve([reviewOf(projectId, true)])) })
    await renderSettled()
    await waitFor(() => expect(within(review()).getByText('Current checkpoint')).toBeInTheDocument())

    gated = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(review()).getByText('Confirming applicability to the current checkpoint…')).toBeInTheDocument())
    expect(within(review()).queryByText('Current checkpoint')).not.toBeInTheDocument()
    expect(within(review()).getByText(/Checkpoint #1/)).toBeInTheDocument() // the historical fact itself stays visible

    await act(async () => gate.resolve([reviewOf('project-a', false)]))
    await waitFor(() => expect(within(review()).getByText('Historical review · Source changed since this review')).toBeInTheDocument())
    expect(within(review()).getByText('A previous approval is no longer applicable because the source checkpoint changed.')).toBeInTheDocument()
  })

  it('never shows a stale approval warning or a current marker while the review read has failed, and recovers on the next success', async () => {
    let failing = false
    installClients({ reviews: (projectId) => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve([reviewOf(projectId, false)])) })
    await renderSettled()
    await waitFor(() => expect(within(review()).getByText('A previous approval is no longer applicable because the source checkpoint changed.')).toBeInTheDocument())

    failing = true
    fireEvent.click(refreshButton())

    await waitFor(() => expect(within(review()).getByText('Applicability to the current checkpoint could not be confirmed.')).toBeInTheDocument())
    expect(within(review()).getByText('Review evidence could not be loaded.')).toBeInTheDocument()
    expect(within(review()).queryByText('Current checkpoint')).not.toBeInTheDocument()
    expect(within(review()).queryByText(/Historical review/)).not.toBeInTheDocument()
    expect(within(review()).queryByText('A previous approval is no longer applicable because the source checkpoint changed.')).not.toBeInTheDocument()
    expect(button('Approve')).toBeEnabled() // the review list is not decision authority

    failing = false
    fireEvent.click(refreshButton())
    await waitFor(() => expect(within(review()).getByText('Historical review · Source changed since this review')).toBeInTheDocument())
    expect(within(review()).queryByText('Review evidence could not be loaded.')).not.toBeInTheDocument()
  })

  it('reports an accepted decision as recorded-then-unreadable, never as a failed submission, and sends it once', async () => {
    let reviewReads = 0
    const clients = installClients({
      reviews: () => (++reviewReads === 1 ? Promise.resolve([]) : Promise.reject(new Error('read unavailable'))),
    })
    await renderSettled()

    fireEvent.click(button('Pending'))

    await waitFor(() => expect(within(review()).getByText('Review evidence could not be loaded.')).toBeInTheDocument())
    expect(within(review()).queryByText('This review decision could not be recorded.')).not.toBeInTheDocument()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(button('Pending')).toBeEnabled())
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('still reports a refused decision as a failed submission', async () => {
    const clients = installClients()
    clients.recordCheckpointReview.mockRejectedValue(new Error('refused'))
    await renderSettled()

    fireEvent.click(button('Approve'))

    await waitFor(() => expect(within(review()).getByText('This review decision could not be recorded.')).toBeInTheDocument())
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('turns an explicit refresh into one execution read and one review read for the panel, and no review, verification, capture or Agent request', async () => {
    const clients = installClients()
    await renderSettled()
    const executionReads = clients.getProjectVerificationExecutions.mock.calls.length
    const reviewReads = clients.getProjectCheckpointReviews.mock.calls.length

    fireEvent.click(refreshButton())

    await waitFor(() => expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(executionReads + 1))
    await waitFor(() => expect(clients.getProjectCheckpointReviews.mock.calls.length).toBe(reviewReads + 1))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expectNoMutationRequested(clients)
  })

  it('applies only the newest of overlapping refreshes to the decision evidence', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const newer = deferred<VerificationExecutionResponse[]>()
    let mode: 'settled' | 'older' | 'newer' = 'settled'
    installClients({
      executions: (projectId) =>
        mode === 'older' ? older.promise : mode === 'newer' ? newer.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')]),
    })
    await renderSettled()

    mode = 'older'
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button('Approve')).toBeDisabled())
    mode = 'newer'
    fireEvent.click(refreshButton())

    await act(async () => newer.resolve([executionOf('project-a', 1, 'Failed')]))
    await waitFor(() => expect(button('Changes requested')).toBeEnabled())
    await act(async () => older.resolve([executionOf('project-a', 1, 'Passed')]))

    expect(button('Approve')).toBeDisabled()
    expect(button('Changes requested')).toBeEnabled()
  })

  it('keeps a pending refresh with its project through A to B to A and never lets the replaced project answer for another', async () => {
    const lateA = deferred<VerificationExecutionResponse[]>()
    let holdA = false
    installClients({
      executions: (projectId) =>
        holdA && projectId === 'project-a' ? lateA.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')]),
    })
    const view = await renderSettled('project-a')

    holdA = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button('Approve')).toBeDisabled())

    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    await act(async () => lateA.resolve([executionOf('project-a', 1, 'Failed')]))
    expect(button('Approve')).toBeEnabled()

    holdA = false
    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expect(within(review()).queryByText('Reading verification evidence…')).not.toBeInTheDocument()
  })

  it('does not offer the returning project the evidence it showed before it was replaced until its own read settles', async () => {
    const returning = deferred<VerificationExecutionResponse[]>()
    let holdA = false
    installClients({
      executions: (projectId) =>
        holdA && projectId === 'project-a' ? returning.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')]),
    })
    const view = await renderSettled('project-a')

    view.rerender(<CandidateWorkspacePanel projectId="project-b" />)
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    holdA = true
    view.rerender(<CandidateWorkspacePanel projectId="project-a" />)

    await waitFor(() => expect(within(review()).getByText('Reading verification evidence…')).toBeInTheDocument())
    expect(button('Approve')).toBeDisabled()
    await act(async () => returning.resolve([executionOf('project-a', 1, 'Passed')]))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
  })

  it('requests nothing and warns about nothing when the panel unmounts with refresh reads pending', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const clients = installClients({ executions: (projectId) => (gated ? gate.promise : Promise.resolve([executionOf(projectId, 1, 'Passed')])) })
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {})
    const view = await renderSettled()
    gated = true
    fireEvent.click(refreshButton())
    await waitFor(() => expect(button('Approve')).toBeDisabled())
    const reads = clients.getProjectVerificationExecutions.mock.calls.length

    view.unmount()
    await act(async () => gate.resolve([executionOf('project-a', 1, 'Running')]))

    expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(reads)
    expect(errors).not.toHaveBeenCalled()
    expectNoMutationRequested(clients)
    errors.mockRestore()
  })

  it('R1: withholds cached applicability and the stale-approval warning during the read after an accepted decision, and keeps the acceptance real', async () => {
    const gate = deferred<CheckpointReviewResponse[]>()
    let gated = false
    const clients = installClients({
      reviews: (projectId) => (gated ? gate.promise : Promise.resolve([reviewOf(projectId, true), { ...reviewOf(projectId, false), reviewId: 'stale-review' } as CheckpointReviewResponse])),
    })
    await renderSettled()
    await waitFor(() => expect(within(review()).getByText('Current checkpoint')).toBeInTheDocument())
    expect(within(review()).getByText('A previous approval is no longer applicable because the source checkpoint changed.')).toBeInTheDocument()

    gated = true
    fireEvent.click(button('Pending'))
    await waitFor(() => expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(within(review()).getAllByText('Confirming applicability to the current checkpoint…').length).toBe(2))
    expect(within(review()).queryByText('Current checkpoint')).not.toBeInTheDocument()
    expect(within(review()).queryByText(/Historical review/)).not.toBeInTheDocument()
    expect(within(review()).queryByText('A previous approval is no longer applicable because the source checkpoint changed.')).not.toBeInTheDocument()
    expect(within(review()).queryByText('This review decision could not be recorded.')).not.toBeInTheDocument()

    gated = false
    await act(async () => gate.resolve([reviewOf('project-a', true), { ...reviewOf('project-a', false), reviewId: 'stale-review' } as CheckpointReviewResponse]))
    await waitFor(() => expect(within(review()).getByText('Current checkpoint')).toBeInTheDocument())
    expect(within(review()).getByText('A previous approval is no longer applicable because the source checkpoint changed.')).toBeInTheDocument()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('R1: the failed read after an accepted decision leaves applicability unconfirmed with a read-failure message, never a failed submission', async () => {
    let failing = false
    const clients = installClients({
      reviews: (projectId) => (failing ? Promise.reject(new Error('read unavailable')) : Promise.resolve([reviewOf(projectId, true)])),
    })
    await renderSettled()
    await waitFor(() => expect(within(review()).getByText('Current checkpoint')).toBeInTheDocument())

    failing = true
    fireEvent.click(button('Pending'))

    await waitFor(() => expect(within(review()).getByText('Applicability to the current checkpoint could not be confirmed.')).toBeInTheDocument())
    expect(within(review()).getByText('Review evidence could not be loaded.')).toBeInTheDocument()
    expect(within(review()).queryByText('This review decision could not be recorded.')).not.toBeInTheDocument()
    expect(within(review()).queryByText('Current checkpoint')).not.toBeInTheDocument()
    expect(clients.recordCheckpointReview).toHaveBeenCalledTimes(1)

    failing = false
    fireEvent.click(refreshButton())
    await waitFor(() => expect(within(review()).getByText('Current checkpoint')).toBeInTheDocument())
    expect(within(review()).queryByText('Review evidence could not be loaded.')).not.toBeInTheDocument()
  })

  it('R1: an older overlapping review read cannot restore applicability while the newer read is pending', async () => {
    const older = deferred<CheckpointReviewResponse[]>()
    const newer = deferred<CheckpointReviewResponse[]>()
    let mode: 'settled' | 'older' | 'newer' = 'settled'
    installClients({
      reviews: (projectId) => (mode === 'older' ? older.promise : mode === 'newer' ? newer.promise : Promise.resolve([reviewOf(projectId, true)])),
    })
    await renderSettled()
    mode = 'older'
    fireEvent.click(refreshButton())
    await waitFor(() => expect(within(review()).getByText('Confirming applicability to the current checkpoint…')).toBeInTheDocument())
    mode = 'newer'
    fireEvent.click(refreshButton())

    await act(async () => older.resolve([reviewOf('project-a', true)]))
    expect(within(review()).getByText('Confirming applicability to the current checkpoint…')).toBeInTheDocument()
    expect(within(review()).queryByText('Current checkpoint')).not.toBeInTheDocument()

    await act(async () => newer.resolve([reviewOf('project-a', false)]))
    await waitFor(() => expect(within(review()).getByText('Historical review · Source changed since this review')).toBeInTheDocument())
  })

  it('R1: withholds decided actions while a poll of a running verification is pending, keeps Pending, and offers them again when it settles', async () => {
    const poll = deferred<VerificationExecutionResponse[]>()
    let mode: 'running' | 'held' | 'done' = 'running'
    const running = (projectId: string) => ({ ...executionOf(projectId, 2, 'Running'), verificationExecutionId: 'running-execution' }) as VerificationExecutionResponse
    const clients = installClients({
      executions: (projectId) =>
        mode === 'held' ? poll.promise : Promise.resolve(mode === 'running' ? [executionOf(projectId, 1, 'Passed'), running(projectId)] : [executionOf(projectId, 1, 'Passed')]),
    })
    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(button('Approve')).toBeEnabled())

    // The next poll (one second later) is held pending: the older Passed execution is cached history, not authority.
    mode = 'held'
    await waitFor(() => expect(button('Approve')).toBeDisabled(), { timeout: 3_000 })
    expect(within(review()).getByText('Reading verification evidence…')).toBeInTheDocument()
    expect(button('Changes requested')).toBeDisabled()
    expect(button('Escalate')).toBeDisabled()
    expect(button('Pending')).toBeEnabled()
    fireEvent.click(button('Approve'))
    expect(clients.recordCheckpointReview).not.toHaveBeenCalled()

    mode = 'done'
    await act(async () => poll.resolve([executionOf('project-a', 1, 'Passed')]))
    await waitFor(() => expect(button('Approve')).toBeEnabled())
    expect(within(review()).queryByText('Reading verification evidence…')).not.toBeInTheDocument()
    expectNoMutationRequested(clients)
  })

  it('keeps the reviewer choice and the verification evidence selection across a refresh that returns the same checkpoint', async () => {
    const clients = installClients({
      executions: (projectId) => Promise.resolve([executionOf(projectId, 2, 'Failed'), executionOf(projectId, 1, 'Passed')]),
    })
    render(<CandidateWorkspacePanel projectId="project-a" />)
    await waitFor(() => expect(button('Changes requested')).toBeEnabled())
    fireEvent.change(within(review()).getByLabelText('Reviewer'), { target: { value: 'FutureAgent' } })

    const reads = clients.getProjectVerificationExecutions.mock.calls.length
    fireEvent.click(refreshButton())
    await waitFor(() => expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(reads + 1))
    await waitFor(() => expect(button('Changes requested')).toBeEnabled())

    expect(within(review()).getByLabelText('Reviewer')).toHaveValue('FutureAgent')
  })
})
