import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  configureVerificationCommandClient,
  deleteVerificationCommandClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  prepareWorkspaceClient,
  projectCheckpointReviewsClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  projectWorkspaceClient,
  recheckPhysicalIdentityClient,
  recordCheckpointReviewClient,
  updateVerificationCommandClient,
  verificationExecutionOutputClient,
} from '../../../api/clients'
import {
  CheckpointReviewResponse,
  GetGitCheckpointDiffOmissionResponse,
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GetProjectWorkspaceResponse,
  VerificationExecutionOutputQueryResult,
  GitCheckpointChangedFileResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { useProjectCheckpointReviews } from './useProjectCheckpointReviews'
import { useProjectGitEvidence } from './useProjectGitEvidence'
import { useProjectVerificationCommands } from './useProjectVerificationCommands'
import { useProjectVerificationExecutions } from './useProjectVerificationExecutions'
import { useProjectWorkspace } from './useProjectWorkspace'
import { useVerificationExecutionOutput } from './useVerificationExecutionOutput'

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

// Records every committed render, so a frame showing the previous selection's evidence cannot
// hide behind a later, correct state.
function recorded<P, R, F>(useHook: (props: P) => R, initial: P, pick: (result: R, props: P) => F) {
  const frames: { props: P; view: F }[] = []
  const rendered = renderHook(
    (props: P) => {
      const result = useHook(props)
      frames.push({ props, view: pick(result, props) })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

function framesWith<P, F>(frames: { props: P; view: F }[], match: (props: P) => boolean, from = 0) {
  return frames.slice(from).filter((frame) => match(frame.props))
}

// The project hooks start their first read in a microtask, so a selection that is replaced before it runs never requests anything.
const flush = () => act(async () => {})

function client<T>(factory: unknown, methods: Record<string, unknown>) {
  vi.mocked(factory as () => unknown).mockReturnValue(methods as unknown as T)
}

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useProjectWorkspace ownership', () => {
  const workspace = (name: string) =>
    new GetProjectWorkspaceResponse({
      physicalIdentityStatus: 'Resolved',
      state: 'Ready',
      candidatePath: `C:\\candidates\\${name}`,
      branchName: `candidate/${name}`,
    })
  const pick = (r: ReturnType<typeof useProjectWorkspace>) => ({
    path: r.workspace?.candidatePath,
    loading: r.loading,
    preparing: r.preparing,
    rechecking: r.rechecking,
    error: r.error,
  })
  const useUnderTest = ({ id }: { id: string | null }) => useProjectWorkspace(id)

  it('shows only the selected project in every committed frame: pending B, null, and the returning A', async () => {
    const getProjectWorkspace = vi.fn()
    client(projectWorkspaceClient, { getProjectWorkspace })
    const b = deferred<GetProjectWorkspaceResponse>()
    getProjectWorkspace.mockResolvedValueOnce(workspace('a')).mockReturnValueOnce(b.promise)

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)
    await waitFor(() => expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\a'))

    let mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    const bFrames = framesWith(frames, (p) => p.id === 'project-b', mark)
    expect(bFrames.length).toBeGreaterThan(0)
    for (const frame of bFrames) {
      expect(frame.view.path).toBeUndefined()
      expect(frame.view.loading).toBe(true)
      expect(frame.view.error).toBeNull()
    }
    expect(getProjectWorkspace).toHaveBeenLastCalledWith('project-b')

    mark = frames.length
    rerender({ id: null })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === null, mark)) {
      expect(frame.view.path).toBeUndefined()
      expect(frame.view.loading).toBe(false)
    }
    expect(getProjectWorkspace).toHaveBeenCalledTimes(2)

    getProjectWorkspace.mockReturnValueOnce(new Promise(() => {}))
    mark = frames.length
    rerender({ id: 'project-a' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-a', mark)) {
      expect(frame.view.path).toBeUndefined()
    }

    // B resolving after it was replaced changes nothing.
    await act(async () => {
      b.resolve(workspace('b'))
    })
    expect(result.current.workspace).toBeNull()
  })

  it('ignores a late failure and a late success of the replaced project', async () => {
    const getProjectWorkspace = vi.fn()
    client(projectWorkspaceClient, { getProjectWorkspace })
    const a = deferred<GetProjectWorkspaceResponse>()
    getProjectWorkspace.mockReturnValueOnce(a.promise).mockResolvedValue(workspace('b'))
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\b'))

    await act(async () => {
      a.reject(new Error('A failed'))
    })
    expect(result.current.error).toBeNull()
    expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\b')
    expect(result.current.loading).toBe(false)
  })

  it('applies only the newest of overlapping reads of one project', async () => {
    const getProjectWorkspace = vi.fn()
    client(projectWorkspaceClient, { getProjectWorkspace })
    client(prepareWorkspaceClient, { prepareRepositoryWorkspace: vi.fn().mockResolvedValue({}) })
    const first = deferred<GetProjectWorkspaceResponse>()
    const second = deferred<GetProjectWorkspaceResponse>()
    getProjectWorkspace.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)

    act(() => {
      void result.current.prepare()
    })
    await waitFor(() => expect(getProjectWorkspace).toHaveBeenCalledTimes(2))
    await act(async () => {
      second.resolve(workspace('newer'))
    })
    await waitFor(() => expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\newer'))
    await act(async () => {
      first.resolve(workspace('older'))
    })
    expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\newer')
    expect(result.current.loading).toBe(false)
  })

  it('does not start work from a handler of the replaced project, and does not refresh or flag the replacement after an accepted preparation', async () => {
    const getProjectWorkspace = vi.fn().mockImplementation((id: string) => Promise.resolve(workspace(id)))
    const accepted = deferred<unknown>()
    const prepareRepositoryWorkspace = vi.fn().mockReturnValue(accepted.promise)
    const recheckProjectPhysicalIdentity = vi.fn().mockResolvedValue({})
    client(projectWorkspaceClient, { getProjectWorkspace })
    client(prepareWorkspaceClient, { prepareRepositoryWorkspace })
    client(recheckPhysicalIdentityClient, { recheckProjectPhysicalIdentity })

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)
    await waitFor(() => expect(result.current.workspace).not.toBeNull())
    const stalePrepare = result.current.prepare
    const staleRecheck = result.current.recheckIdentity
    act(() => {
      void stalePrepare()
    })
    expect(prepareRepositoryWorkspace).toHaveBeenCalledWith('project-a')

    const mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\project-b'))
    const reads = getProjectWorkspace.mock.calls.length

    await act(async () => {
      accepted.resolve({})
    })
    for (const frame of frames.slice(mark)) {
      expect(frame.view.preparing).toBe(false)
    }
    expect(getProjectWorkspace).toHaveBeenCalledTimes(reads)
    expect(result.current.error).toBeNull()

    await act(async () => {
      await stalePrepare()
      await staleRecheck()
    })
    expect(prepareRepositoryWorkspace).toHaveBeenCalledTimes(1)
    expect(recheckProjectPhysicalIdentity).not.toHaveBeenCalled()
    expect(getProjectWorkspace).toHaveBeenCalledTimes(reads)
  })

  it('does not write the obsolete failure of a preparation onto the replacement', async () => {
    const getProjectWorkspace = vi.fn().mockImplementation((id: string) => Promise.resolve(workspace(id)))
    const refused = deferred<unknown>()
    client(projectWorkspaceClient, { getProjectWorkspace })
    client(prepareWorkspaceClient, { prepareRepositoryWorkspace: vi.fn().mockReturnValue(refused.promise) })
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)
    await waitFor(() => expect(result.current.workspace).not.toBeNull())
    act(() => {
      void result.current.prepare()
    })
    rerender({ id: 'project-b' })
    await flush()
    await act(async () => {
      refused.reject(new Error('preparation of A failed'))
    })
    expect(result.current.error).toBeNull()
    expect(result.current.preparing).toBe(false)
  })

  it('keeps same-project preparation, refresh and recoverable failure working', async () => {
    const getProjectWorkspace = vi
      .fn()
      .mockResolvedValueOnce(workspace('before'))
      .mockResolvedValueOnce(workspace('after'))
    const prepareRepositoryWorkspace = vi.fn().mockRejectedValueOnce(new Error('no')).mockResolvedValueOnce({})
    client(projectWorkspaceClient, { getProjectWorkspace })
    client(prepareWorkspaceClient, { prepareRepositoryWorkspace })
    const { result } = recorded(useUnderTest, { id: 'project-a' } as { id: string | null }, pick)
    await waitFor(() => expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\before'))

    await act(async () => {
      await result.current.prepare()
    })
    expect(result.current.error).toBe('This workspace request could not be completed.')
    expect(result.current.preparing).toBe(false)

    await act(async () => {
      await result.current.prepare()
    })
    expect(result.current.error).toBeNull()
    expect(result.current.workspace?.candidatePath).toBe('C:\\candidates\\after')
    expect(prepareRepositoryWorkspace).toHaveBeenCalledWith('project-a')
  })
})

describe('useProjectGitEvidence ownership', () => {
  const evidence = (checkpointId: string, number = 1) =>
    new GetProjectGitEvidenceResponse({
      checkpointId,
      checkpointNumber: number,
      headCommitSha: `head-${checkpointId}`,
      fingerprintSha256: `fingerprint-${checkpointId}`,
      changedFileCount: 1,
    })
  const files = (path: string) => [new GitCheckpointChangedFileResponse({ path, indexStatus: ' ', workTreeStatus: 'M' })]
  const diff = (text: string) => new GetGitCheckpointDiffResponse({ comparisonText: text, isComplete: true, trackedPathCount: text === '' ? 0 : 1, comparedPathCount: text === '' ? 0 : 1, omissions: [] })
  type Props = { id: string | null; enabled: boolean }
  const useUnderTest = ({ id, enabled }: Props) => useProjectGitEvidence(id, enabled)
  const pick = (r: ReturnType<typeof useProjectGitEvidence>) => ({
    checkpoint: r.evidence?.checkpointId,
    files: r.changedFiles?.map((file) => file.path),
    diff: r.comparison?.text,
    loading: r.loading,
    capturing: r.capturing,
    inspecting: r.inspecting,
    error: r.error,
  })

  it('shows no evidence of the previous project for pending B, null, disabled and the returning A', async () => {
    const getProjectGitEvidence = vi.fn()
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    const b = deferred<GetProjectGitEvidenceResponse>()
    getProjectGitEvidence.mockResolvedValueOnce(evidence('checkpoint-a')).mockReturnValueOnce(b.promise)

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-a'))

    let mark = frames.length
    rerender({ id: 'project-b', enabled: true })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-b', mark)) {
      expect(frame.view.checkpoint).toBeUndefined()
      expect(frame.view.loading).toBe(true)
    }

    mark = frames.length
    rerender({ id: null, enabled: true })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === null, mark)) {
      expect(frame.view.checkpoint).toBeUndefined()
      expect(frame.view.loading).toBe(false)
    }

    getProjectGitEvidence.mockReturnValueOnce(new Promise(() => {}))
    rerender({ id: 'project-a', enabled: true })
    await flush()
    expect(result.current.evidence).toBeNull()

    mark = frames.length
    rerender({ id: 'project-a', enabled: false })
    await flush()
    for (const frame of framesWith(frames, (p) => !p.enabled, mark)) {
      expect(frame.view.checkpoint).toBeUndefined()
      expect(frame.view.loading).toBe(false)
    }
    await act(async () => {
      b.resolve(evidence('checkpoint-b'))
    })
    expect(result.current.evidence).toBeNull()
  })

  it('ends at the later selection when B resolves first and the older A answers last', async () => {
    const getProjectGitEvidence = vi.fn()
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    const a = deferred<GetProjectGitEvidenceResponse>()
    const b = deferred<GetProjectGitEvidenceResponse>()
    getProjectGitEvidence.mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise)

    const { result, rerender } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await flush()
    rerender({ id: 'project-b', enabled: true })
    await flush()
    await act(async () => {
      b.resolve(evidence('checkpoint-b', 2))
    })
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-b'))
    await act(async () => {
      a.resolve(evidence('checkpoint-a', 9))
    })
    expect(result.current.evidence?.checkpointId).toBe('checkpoint-b')
    expect(result.current.loading).toBe(false)
  })

  it('ignores a late failure of the replaced project', async () => {
    const getProjectGitEvidence = vi.fn()
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    const a = deferred<GetProjectGitEvidenceResponse>()
    getProjectGitEvidence.mockReturnValueOnce(a.promise).mockResolvedValue(evidence('checkpoint-b'))
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await flush()
    rerender({ id: 'project-b', enabled: true })
    await flush()
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-b'))
    await act(async () => {
      a.reject(new Error('A failed'))
    })
    expect(result.current.error).toBeNull()
  })

  it('keeps files and diff on the exact checkpoint that was inspected (C1 to C2 during inspection)', async () => {
    const getProjectGitEvidence = vi
      .fn()
      .mockResolvedValueOnce(evidence('checkpoint-1', 1))
      .mockResolvedValueOnce(evidence('checkpoint-2', 2))
    const getGitCheckpointChangedFiles = vi.fn()
    const getGitCheckpointDiff = vi.fn()
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    client(captureGitWorkspaceCheckpointClient, { captureGitWorkspaceCheckpoint: vi.fn().mockResolvedValue({}) })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff })
    const firstFiles = deferred<GitCheckpointChangedFileResponse[]>()
    const firstDiff = deferred<GetGitCheckpointDiffResponse>()
    getGitCheckpointChangedFiles.mockReturnValueOnce(firstFiles.promise).mockResolvedValueOnce(files('second.txt'))
    getGitCheckpointDiff.mockReturnValueOnce(firstDiff.promise).mockResolvedValueOnce(diff('diff of checkpoint two'))

    const { result, frames } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))
    act(() => {
      void result.current.inspect()
    })
    expect(getGitCheckpointChangedFiles).toHaveBeenCalledWith('project-a', 'checkpoint-1')
    expect(getGitCheckpointDiff).toHaveBeenCalledWith('project-a', 'checkpoint-1')
    expect(result.current.inspecting).toBe(true)

    await act(async () => {
      await result.current.capture()
    })
    expect(result.current.evidence?.checkpointId).toBe('checkpoint-2')
    // The inspection of C1 is not C2's: C2 can be inspected and nothing of C1 is shown.
    expect(result.current.inspecting).toBe(false)
    expect(result.current.changedFiles).toBeNull()
    expect(result.current.comparison).toBeNull()

    const mark = frames.length
    await act(async () => {
      firstFiles.resolve(files('first.txt'))
      firstDiff.resolve(diff('diff of checkpoint one'))
    })
    for (const frame of frames.slice(mark)) {
      expect(frame.view.files ?? null).toBeNull()
      expect(frame.view.diff ?? null).toBeNull()
    }
    expect(result.current.changedFiles).toBeNull()

    await act(async () => {
      await result.current.inspect()
    })
    expect(getGitCheckpointChangedFiles).toHaveBeenLastCalledWith('project-a', 'checkpoint-2')
    expect(getGitCheckpointDiff).toHaveBeenLastCalledWith('project-a', 'checkpoint-2')
    expect(result.current.changedFiles?.map((file) => file.path)).toEqual(['second.txt'])
    expect(result.current.comparison?.text).toBe('diff of checkpoint two')
  })

  it('drops the inspected files and diff when a refresh names a newer checkpoint, and an inspection failure stays on its checkpoint', async () => {
    const getProjectGitEvidence = vi
      .fn()
      .mockResolvedValueOnce(evidence('checkpoint-1', 1))
      .mockResolvedValueOnce(evidence('checkpoint-2', 2))
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    client(captureGitWorkspaceCheckpointClient, { captureGitWorkspaceCheckpoint: vi.fn().mockResolvedValue({}) })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles: vi.fn().mockResolvedValue(files('one.txt')) })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff: vi.fn().mockResolvedValue(diff('one')) })
    const { result } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))
    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.comparison?.text).toBe('one')

    await act(async () => {
      await result.current.capture()
    })
    expect(result.current.evidence?.checkpointId).toBe('checkpoint-2')
    expect(result.current.changedFiles).toBeNull()
    expect(result.current.comparison).toBeNull()
  })

  it('does not refresh, flag or write for the replacement after an accepted capture, and a stale handler starts nothing', async () => {
    const getProjectGitEvidence = vi.fn().mockImplementation((id: string) => Promise.resolve(evidence(`checkpoint-of-${id}`)))
    const accepted = deferred<unknown>()
    const captureGitWorkspaceCheckpoint = vi.fn().mockReturnValue(accepted.promise)
    const getGitCheckpointChangedFiles = vi.fn()
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    client(captureGitWorkspaceCheckpointClient, { captureGitWorkspaceCheckpoint })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff: vi.fn() })

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-of-project-a'))
    const staleCapture = result.current.capture
    const staleInspect = result.current.inspect
    act(() => {
      void staleCapture()
    })
    expect(captureGitWorkspaceCheckpoint).toHaveBeenCalledWith('project-a')

    const mark = frames.length
    rerender({ id: 'project-b', enabled: true })
    await flush()
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-of-project-b'))
    const reads = getProjectGitEvidence.mock.calls.length
    await act(async () => {
      accepted.resolve({})
    })
    for (const frame of frames.slice(mark)) {
      expect(frame.view.capturing).toBe(false)
    }
    expect(getProjectGitEvidence).toHaveBeenCalledTimes(reads)

    await act(async () => {
      await staleCapture()
      await staleInspect()
    })
    expect(captureGitWorkspaceCheckpoint).toHaveBeenCalledTimes(1)
    expect(getGitCheckpointChangedFiles).not.toHaveBeenCalled()
    expect(getProjectGitEvidence).toHaveBeenCalledTimes(reads)
  })

  it('exposes the host comparison with its coverage, omissions and limitation and never reads a missing field as clean', async () => {
    const answers = [
      new GetGitCheckpointDiffResponse({
        comparisonText: 'diff --git a/safe b/safe',
        isComplete: false,
        trackedPathCount: 2,
        comparedPathCount: 1,
        limitation: 'fixed limitation',
        omissions: [new GetGitCheckpointDiffOmissionResponse({ path: 'linked', reason: 'containment_unproven' })],
      }),
      new GetGitCheckpointDiffResponse({ comparisonText: '', isComplete: true, trackedPathCount: 0, comparedPathCount: 0, omissions: [] }),
      new GetGitCheckpointDiffResponse({}),
    ]
    const getGitCheckpointDiff = vi.fn()
    answers.forEach((answer) => getGitCheckpointDiff.mockResolvedValueOnce(answer))
    client(projectGitEvidenceClient, { getProjectGitEvidence: vi.fn().mockResolvedValue(evidence('checkpoint-1')) })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles: vi.fn().mockResolvedValue(files('x.txt')) })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff })
    const { result } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))

    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.comparison).toEqual({
      text: 'diff --git a/safe b/safe',
      complete: false,
      trackedPathCount: 2,
      comparedPathCount: 1,
      limitation: 'fixed limitation',
      omissions: [{ path: 'linked', reason: 'containment_unproven' }],
    })

    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.comparison).toMatchObject({ text: '', complete: true, trackedPathCount: 0, omissions: [] })

    await act(async () => {
      await result.current.inspect()
    })
    // A body without explicit coverage is refused: the previous comparison is cleared, the fixed message is shown, nothing is "clean".
    expect(result.current.comparison).toBeNull()
    expect(result.current.changedFiles).toBeNull()
    expect(result.current.error).toBe('The host response could not be confirmed as a valid checkpoint comparison.')
  })

  it('refuses contradictory accounting, keeps the refusal on its checkpoint and recovers on a valid answer', async () => {
    const contradictory = new GetGitCheckpointDiffResponse({
      comparisonText: '', isComplete: true, trackedPathCount: 3, comparedPathCount: 0, omissions: [],
    })
    const getGitCheckpointDiff = vi.fn().mockResolvedValueOnce(contradictory).mockResolvedValueOnce(diff('ok'))
    client(projectGitEvidenceClient, { getProjectGitEvidence: vi.fn().mockResolvedValue(evidence('checkpoint-1')) })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles: vi.fn().mockResolvedValue(files('x.txt')) })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff })
    const { result } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))

    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.comparison).toBeNull()
    expect(result.current.error).toBe('The host response could not be confirmed as a valid checkpoint comparison.')

    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.error).toBeNull()
    expect(result.current.comparison?.text).toBe('ok')
  })

  it('keeps the newest inspection when an older overlapping answer for the same checkpoint arrives late', async () => {
    const olderDiff = deferred<GetGitCheckpointDiffResponse>()
    const olderFiles = deferred<GitCheckpointChangedFileResponse[]>()
    const getGitCheckpointDiff = vi.fn().mockReturnValueOnce(olderDiff.promise).mockResolvedValueOnce(diff('newer comparison'))
    const getGitCheckpointChangedFiles = vi.fn().mockReturnValueOnce(olderFiles.promise).mockResolvedValueOnce(files('newer.txt'))
    client(projectGitEvidenceClient, { getProjectGitEvidence: vi.fn().mockResolvedValue(evidence('checkpoint-1')) })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff })
    const { result } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))

    act(() => {
      void result.current.inspect()
    })
    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.comparison?.text).toBe('newer comparison')

    await act(async () => {
      olderFiles.resolve(files('older.txt'))
      olderDiff.resolve(diff('older comparison'))
    })

    expect(result.current.comparison?.text).toBe('newer comparison')
    expect(result.current.changedFiles?.map((file) => file.path)).toEqual(['newer.txt'])
    expect(result.current.inspecting).toBe(false)
  })

  it('keeps same-project capture, inspection and recoverable failure working', async () => {
    const getProjectGitEvidence = vi.fn().mockResolvedValue(evidence('checkpoint-1'))
    const getGitCheckpointChangedFiles = vi
      .fn()
      .mockRejectedValueOnce(new Error('failed'))
      .mockResolvedValueOnce(files('ok.txt'))
    client(projectGitEvidenceClient, { getProjectGitEvidence })
    client(gitCheckpointChangedFilesClient, { getGitCheckpointChangedFiles })
    client(gitCheckpointDiffClient, { getGitCheckpointDiff: vi.fn().mockResolvedValue(diff('')) })
    const { result } = recorded(useUnderTest, { id: 'project-a', enabled: true } as Props, pick)
    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-1'))
    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.error).toBe('This source evidence request could not be completed.')
    await act(async () => {
      await result.current.inspect()
    })
    expect(result.current.error).toBeNull()
    expect(result.current.changedFiles?.map((file) => file.path)).toEqual(['ok.txt'])
    expect(result.current.comparison?.text).toBe('')
  })
})

describe('useProjectVerificationCommands ownership', () => {
  const command = (id: string, args: string[] = ['--literal', 'a b']) =>
    new VerificationCommandResponse({
      verificationCommandId: id,
      commandNumber: 1,
      name: `name of ${id}`,
      executablePath: `C:\\tools\\${id}.exe`,
      arguments: args,
      timeoutSeconds: 45,
      isEnabled: true,
    })
  type Props = { id: string | null }
  const useUnderTest = ({ id }: Props) => useProjectVerificationCommands(id)
  const pick = (r: ReturnType<typeof useProjectVerificationCommands>) => ({
    ids: r.commands.map((item) => item.verificationCommandId),
    loading: r.loading,
    saving: r.saving,
    error: r.error,
  })
  const draft = { name: 'Typecheck', executablePath: 'C:\\tools\\tsc.exe', arguments: ['-b', '--pretty false'], timeoutSeconds: 90, isEnabled: true }

  it('shows no command of the previous project for pending B, null and the returning A, and ignores late A results', async () => {
    const getProjectVerificationCommands = vi.fn()
    client(projectVerificationCommandsClient, { getProjectVerificationCommands })
    const firstA = deferred<VerificationCommandResponse[]>()
    const b = deferred<VerificationCommandResponse[]>()
    getProjectVerificationCommands
      .mockResolvedValueOnce([command('command-a')])
      .mockReturnValueOnce(b.promise)
      .mockReturnValueOnce(firstA.promise)

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.commands.map((item) => item.verificationCommandId)).toEqual(['command-a']))

    let mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-b', mark)) {
      expect(frame.view.ids).toEqual([])
      expect(frame.view.loading).toBe(true)
    }
    mark = frames.length
    rerender({ id: null })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === null, mark)) {
      expect(frame.view.ids).toEqual([])
      expect(frame.view.loading).toBe(false)
    }
    mark = frames.length
    rerender({ id: 'project-a' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-a', mark)) {
      expect(frame.view.ids).toEqual([])
    }
    await act(async () => {
      b.resolve([command('command-b')])
    })
    expect(result.current.commands).toEqual([])
    await act(async () => {
      firstA.resolve([command('command-a2')])
    })
    expect(result.current.commands.map((item) => item.verificationCommandId)).toEqual(['command-a2'])
  })

  it('ignores a late failure of the replaced project', async () => {
    const getProjectVerificationCommands = vi.fn()
    client(projectVerificationCommandsClient, { getProjectVerificationCommands })
    const a = deferred<VerificationCommandResponse[]>()
    getProjectVerificationCommands.mockReturnValueOnce(a.promise).mockResolvedValue([command('command-b')])
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await flush()
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.commands).toHaveLength(1))
    await act(async () => {
      a.reject(new Error('A failed'))
    })
    expect(result.current.error).toBeNull()
  })

  it('sends the literal arguments and exact identifiers, and an obsolete configure resolves false without refreshing or touching the replacement', async () => {
    const getProjectVerificationCommands = vi.fn().mockImplementation((id: string) => Promise.resolve([command(`command-of-${id}`)]))
    const accepted = deferred<unknown>()
    const configureVerificationCommand = vi.fn().mockReturnValue(accepted.promise)
    client(projectVerificationCommandsClient, { getProjectVerificationCommands })
    client(configureVerificationCommandClient, { configureVerificationCommand })

    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.commands).toHaveLength(1))
    const staleConfigure = result.current.configure
    let outcome: boolean | undefined
    act(() => {
      void staleConfigure(draft).then((value) => {
        outcome = value
      })
    })
    expect(configureVerificationCommand).toHaveBeenCalledTimes(1)
    const [projectId, request] = configureVerificationCommand.mock.calls[0]
    expect(projectId).toBe('project-a')
    expect(request).toMatchObject(draft)
    expect(request.arguments).toEqual(['-b', '--pretty false'])

    const mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.commands.map((item) => item.verificationCommandId)).toEqual(['command-of-project-b']))
    const reads = getProjectVerificationCommands.mock.calls.length
    await act(async () => {
      accepted.resolve({})
    })
    expect(outcome).toBe(false)
    for (const frame of frames.slice(mark)) {
      expect(frame.view.saving).toBe(false)
      expect(frame.view.error).toBeNull()
    }
    expect(getProjectVerificationCommands).toHaveBeenCalledTimes(reads)

    await act(async () => {
      expect(await staleConfigure(draft)).toBe(false)
    })
    expect(configureVerificationCommand).toHaveBeenCalledTimes(1)
  })

  it('does not let an obsolete update or remove start work, refresh or fail for the replacement', async () => {
    const getProjectVerificationCommands = vi.fn().mockImplementation((id: string) => Promise.resolve([command(`command-of-${id}`)]))
    const updateRefused = deferred<unknown>()
    const updateVerificationCommand = vi.fn().mockReturnValue(updateRefused.promise)
    const deleteVerificationCommand = vi.fn().mockResolvedValue({})
    client(projectVerificationCommandsClient, { getProjectVerificationCommands })
    client(updateVerificationCommandClient, { updateVerificationCommand })
    client(deleteVerificationCommandClient, { deleteVerificationCommand })
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await flush()
    await waitFor(() => expect(result.current.commands).toHaveLength(1))
    const staleUpdate = result.current.update
    const staleRemove = result.current.remove
    const target = result.current.commands[0]
    act(() => {
      void staleUpdate(target, false)
    })
    expect(updateVerificationCommand.mock.calls[0].slice(0, 2)).toEqual(['project-a', 'command-of-project-a'])
    expect(updateVerificationCommand.mock.calls[0][2]).toMatchObject({
      name: 'name of command-of-project-a',
      executablePath: 'C:\\tools\\command-of-project-a.exe',
      arguments: ['--literal', 'a b'],
      timeoutSeconds: 45,
      isEnabled: false,
    })
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.commands.map((item) => item.verificationCommandId)).toEqual(['command-of-project-b']))
    const reads = getProjectVerificationCommands.mock.calls.length
    await act(async () => {
      updateRefused.reject(new Error('refused'))
    })
    expect(result.current.error).toBeNull()
    expect(result.current.saving).toBe(false)
    await act(async () => {
      await staleRemove('command-of-project-a')
    })
    expect(deleteVerificationCommand).not.toHaveBeenCalled()
    expect(getProjectVerificationCommands).toHaveBeenCalledTimes(reads)
  })

  it('keeps same-project configure, update, remove and recoverable failure working', async () => {
    const getProjectVerificationCommands = vi.fn().mockResolvedValue([command('command-a')])
    const configureVerificationCommand = vi.fn().mockRejectedValueOnce(new Error('no')).mockResolvedValueOnce({})
    const deleteVerificationCommand = vi.fn().mockResolvedValue({})
    client(projectVerificationCommandsClient, { getProjectVerificationCommands })
    client(configureVerificationCommandClient, { configureVerificationCommand })
    client(deleteVerificationCommandClient, { deleteVerificationCommand })
    const { result } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.commands).toHaveLength(1))
    await act(async () => {
      expect(await result.current.configure(draft)).toBe(false)
    })
    expect(result.current.error).toBe('This verification configuration request could not be completed.')
    await act(async () => {
      expect(await result.current.configure(draft)).toBe(true)
    })
    expect(result.current.error).toBeNull()
    await act(async () => {
      await result.current.remove('command-a')
    })
    expect(deleteVerificationCommand).toHaveBeenCalledWith('project-a', 'command-a')
    expect(result.current.saving).toBe(false)
  })
})

describe('useProjectVerificationExecutions ownership', () => {
  const execution = (id: string, status = 'Passed') =>
    new VerificationExecutionResponse({
      verificationExecutionId: id,
      verificationCommandId: `command-of-${id}`,
      gitCheckpointId: `checkpoint-of-${id}`,
      status,
      executionNumber: 1,
    })
  type Props = { id: string | null }
  const useUnderTest = ({ id }: Props) => useProjectVerificationExecutions(id)
  const pick = (r: ReturnType<typeof useProjectVerificationExecutions>) => ({
    ids: r.executions.map((item) => item.verificationExecutionId),
    error: r.error,
  })

  it('shows no execution of the previous project for pending B, null and the returning A, and ignores late A results', async () => {
    const getProjectVerificationExecutions = vi.fn()
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions })
    const firstA = deferred<VerificationExecutionResponse[]>()
    const b = deferred<VerificationExecutionResponse[]>()
    getProjectVerificationExecutions
      .mockResolvedValueOnce([execution('execution-a')])
      .mockReturnValueOnce(b.promise)
      .mockReturnValueOnce(firstA.promise)
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.executions).toHaveLength(1))

    let mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-b', mark)) {
      expect(frame.view.ids).toEqual([])
    }
    mark = frames.length
    rerender({ id: null })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === null, mark)) {
      expect(frame.view.ids).toEqual([])
    }
    expect(getProjectVerificationExecutions).toHaveBeenCalledTimes(2)
    mark = frames.length
    rerender({ id: 'project-a' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-a', mark)) {
      expect(frame.view.ids).toEqual([])
    }
    await act(async () => {
      b.resolve([execution('execution-b')])
    })
    expect(result.current.executions).toEqual([])
    await act(async () => {
      firstA.resolve([execution('execution-a2')])
    })
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['execution-a2'])
  })

  it('does not show the failure of the replaced project, and a stale refresh starts no request', async () => {
    const getProjectVerificationExecutions = vi.fn()
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions })
    const a = deferred<VerificationExecutionResponse[]>()
    getProjectVerificationExecutions.mockReturnValueOnce(a.promise).mockResolvedValue([execution('execution-b')])
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    const staleRefresh = result.current.refresh
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.executions).toHaveLength(1))
    const reads = getProjectVerificationExecutions.mock.calls.length
    await act(async () => {
      a.reject(new Error('A failed'))
      expect(await staleRefresh()).toEqual([])
    })
    expect(result.current.error).toBeNull()
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['execution-b'])
    expect(getProjectVerificationExecutions).toHaveBeenCalledTimes(reads)
  })

  it('polls only while an execution is running, stops with its project, and an older response never overwrites a newer one', async () => {
    vi.useFakeTimers()
    const getProjectVerificationExecutions = vi.fn()
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions })
    getProjectVerificationExecutions
      .mockResolvedValueOnce([execution('execution-a', 'Running')])
      .mockResolvedValueOnce([execution('execution-a', 'Running')])
      .mockResolvedValue([execution('execution-b', 'Passed')])
    const { result, rerender } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await flush()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(getProjectVerificationExecutions).toHaveBeenCalledTimes(1)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000)
    })
    expect(getProjectVerificationExecutions).toHaveBeenCalledTimes(2)

    rerender({ id: 'project-b' })
    await flush()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000)
    })
    // A's poll chain is gone: only B's single read happened, and B is terminal.
    expect(getProjectVerificationExecutions.mock.calls.map((call) => call[0])).toEqual([
      'project-a',
      'project-a',
      'project-b',
    ])
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['execution-b'])

    const older = deferred<VerificationExecutionResponse[]>()
    const newer = deferred<VerificationExecutionResponse[]>()
    getProjectVerificationExecutions.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
    act(() => {
      void result.current.refresh()
      void result.current.refresh()
    })
    await act(async () => {
      newer.resolve([execution('execution-newer')])
    })
    await act(async () => {
      older.resolve([execution('execution-older')])
    })
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['execution-newer'])
  })
})

describe('useProjectCheckpointReviews ownership', () => {
  const review = (id: string) => new CheckpointReviewResponse({ reviewId: id, decision: 'Approved', isApplicable: true })
  type Props = { id: string | null }
  const useUnderTest = ({ id }: Props) => useProjectCheckpointReviews(id)
  const pick = (r: ReturnType<typeof useProjectCheckpointReviews>) => ({
    ids: r.reviews.map((item) => item.reviewId),
    saving: r.saving,
    error: r.error,
  })

  it('shows no review of the previous project for pending B, null and the returning A, and ignores late A results', async () => {
    const getProjectCheckpointReviews = vi.fn()
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews })
    const firstA = deferred<CheckpointReviewResponse[]>()
    const b = deferred<CheckpointReviewResponse[]>()
    getProjectCheckpointReviews
      .mockResolvedValueOnce([review('review-a')])
      .mockReturnValueOnce(b.promise)
      .mockReturnValueOnce(firstA.promise)
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.reviews).toHaveLength(1))

    let mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-b', mark)) {
      expect(frame.view.ids).toEqual([])
    }
    mark = frames.length
    rerender({ id: null })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === null, mark)) {
      expect(frame.view.ids).toEqual([])
    }
    mark = frames.length
    rerender({ id: 'project-a' })
    await flush()
    for (const frame of framesWith(frames, (p) => p.id === 'project-a', mark)) {
      expect(frame.view.ids).toEqual([])
    }
    await act(async () => {
      b.resolve([review('review-b')])
    })
    expect(result.current.reviews).toEqual([])
    await act(async () => {
      firstA.resolve([review('review-a2')])
    })
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['review-a2'])
  })

  it('submits the exact identifiers, and an obsolete record resolves false without refreshing or touching the replacement', async () => {
    const getProjectCheckpointReviews = vi.fn().mockImplementation((id: string) => Promise.resolve([review(`review-of-${id}`)]))
    const accepted = deferred<unknown>()
    const recordCheckpointReview = vi.fn().mockReturnValue(accepted.promise)
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews })
    client(recordCheckpointReviewClient, { recordCheckpointReview })
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(result.current.reviews).toHaveLength(1))
    const staleRecord = result.current.record
    let outcome: boolean | undefined
    act(() => {
      void staleRecord('checkpoint-1', 'execution-1', 'Approved', 'Human').then((value) => {
        outcome = value
      })
    })
    expect(recordCheckpointReview.mock.calls[0][0]).toBe('project-a')
    expect(recordCheckpointReview.mock.calls[0][1]).toMatchObject({
      gitCheckpointId: 'checkpoint-1',
      verificationExecutionId: 'execution-1',
      actorKind: 'Human',
      decision: 'Approved',
    })
    const mark = frames.length
    rerender({ id: 'project-b' })
    await flush()
    await waitFor(() => expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['review-of-project-b']))
    const reads = getProjectCheckpointReviews.mock.calls.length
    await act(async () => {
      accepted.resolve({})
    })
    expect(outcome).toBe(false)
    for (const frame of frames.slice(mark)) {
      expect(frame.view.saving).toBe(false)
      expect(frame.view.error).toBeNull()
    }
    expect(getProjectCheckpointReviews).toHaveBeenCalledTimes(reads)
    await act(async () => {
      expect(await staleRecord('checkpoint-1', 'execution-1', 'Approved')).toBe(false)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('does not write the obsolete failure of a record onto the replacement, and keeps same-project failure recoverable', async () => {
    const getProjectCheckpointReviews = vi.fn().mockResolvedValue([review('review-a')])
    const refused = deferred<unknown>()
    const recordCheckpointReview = vi
      .fn()
      .mockReturnValueOnce(refused.promise)
      .mockRejectedValueOnce(new Error('no'))
      .mockResolvedValueOnce({})
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews })
    client(recordCheckpointReviewClient, { recordCheckpointReview })
    const first = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(first.result.current.reviews).toHaveLength(1))
    act(() => {
      void first.result.current.record('checkpoint-1', undefined, 'Pending')
    })
    first.rerender({ id: 'project-b' })
    await flush()
    await act(async () => {
      refused.reject(new Error('refused'))
    })
    expect(first.result.current.error).toBeNull()
    first.unmount()

    const second = recorded(useUnderTest, { id: 'project-a' } as Props, pick)
    await waitFor(() => expect(second.result.current.reviews).toHaveLength(1))
    await act(async () => {
      expect(await second.result.current.record('checkpoint-1', undefined, 'Pending')).toBe(false)
    })
    expect(second.result.current.error).toBe('This review decision could not be recorded.')
    await act(async () => {
      expect(await second.result.current.record('checkpoint-1', undefined, 'Pending')).toBe(true)
    })
    expect(second.result.current.error).toBeNull()
  })
})

describe('useVerificationExecutionOutput ownership', () => {
  const chunk = (text: string, isFinal = false, nextOffset = text.length) =>
    new VerificationExecutionOutputQueryResult({ text, isFinal, nextOffset })
  type Props = { project: string; execution: string | null; stream: 'stdout' | 'stderr'; enabled: boolean }
  const useUnderTest = ({ project, execution, stream, enabled }: Props) =>
    useVerificationExecutionOutput(project, execution, stream, enabled)
  const pick = (r: ReturnType<typeof useVerificationExecutionOutput>) => ({ text: r.text, isFinal: r.isFinal, error: r.error })

  it('never shows the output, final flag or error of another execution, stream or project', async () => {
    const getVerificationExecutionOutput = vi.fn()
    client(verificationExecutionOutputClient, { getVerificationExecutionOutput })
    getVerificationExecutionOutput
      .mockResolvedValueOnce(chunk('output of A'))
      .mockResolvedValueOnce(chunk('', true))
      .mockReturnValue(new Promise(() => {}))
    const initial: Props = { project: 'project-a', execution: 'execution-a', stream: 'stdout', enabled: true }
    const { result, rerender, frames } = recorded(useUnderTest, initial, pick)
    await waitFor(() => expect(result.current.text).toBe('output of A'))
    await waitFor(() => expect(result.current.isFinal).toBe(true))

    let mark = frames.length
    rerender({ ...initial, execution: 'execution-b' })
    for (const frame of framesWith(frames, (p) => p.execution === 'execution-b', mark)) {
      expect(frame.view).toEqual({ text: '', isFinal: false, error: null })
    }
    expect(getVerificationExecutionOutput).toHaveBeenLastCalledWith('project-a', 'execution-b', 'stdout', 0, 16 * 1024)

    mark = frames.length
    rerender({ ...initial, stream: 'stderr' })
    for (const frame of framesWith(frames, (p) => p.stream === 'stderr', mark)) {
      expect(frame.view).toEqual({ text: '', isFinal: false, error: null })
    }
    mark = frames.length
    rerender({ ...initial, project: 'project-c' })
    for (const frame of framesWith(frames, (p) => p.project === 'project-c', mark)) {
      expect(frame.view).toEqual({ text: '', isFinal: false, error: null })
    }
    mark = frames.length
    rerender({ ...initial, enabled: false })
    for (const frame of framesWith(frames, (p) => !p.enabled, mark)) {
      expect(frame.view).toEqual({ text: '', isFinal: false, error: null })
    }
  })

  it('ignores a late chunk of a replaced execution', async () => {
    const getVerificationExecutionOutput = vi.fn()
    client(verificationExecutionOutputClient, { getVerificationExecutionOutput })
    const lateA = deferred<VerificationExecutionOutputQueryResult>()
    getVerificationExecutionOutput.mockReturnValueOnce(lateA.promise).mockReturnValue(new Promise(() => {}))
    const initial: Props = { project: 'project-a', execution: 'execution-a', stream: 'stdout', enabled: true }
    const { result, rerender } = recorded(useUnderTest, initial, pick)
    rerender({ ...initial, execution: 'execution-b' })
    await act(async () => {
      lateA.resolve(chunk('late A', true))
    })
    expect(result.current).toEqual({ text: '', isFinal: false, error: null })
  })

  it('does not apply the late failure of a replaced execution', async () => {
    const getVerificationExecutionOutput = vi.fn()
    client(verificationExecutionOutputClient, { getVerificationExecutionOutput })
    const lateA = deferred<VerificationExecutionOutputQueryResult>()
    getVerificationExecutionOutput.mockReturnValueOnce(lateA.promise).mockResolvedValue(chunk('B', true))
    const initial: Props = { project: 'project-a', execution: 'execution-a', stream: 'stdout', enabled: true }
    const { result, rerender } = recorded(useUnderTest, initial, pick)
    rerender({ ...initial, execution: 'execution-b' })
    await flush()
    await waitFor(() => expect(result.current.text).toBe('B'))
    await act(async () => {
      lateA.reject(new Error('A failed'))
    })
    expect(result.current.error).toBeNull()
    expect(result.current.text).toBe('B')
  })
})

describe('project hooks unmount and remount', () => {
  it('changes nothing after unmount and remounts as a fresh lifetime', async () => {
    const answer = deferred<GetProjectWorkspaceResponse>()
    const getProjectWorkspace = vi.fn().mockReturnValueOnce(answer.promise).mockReturnValue(new Promise(() => {}))
    client(projectWorkspaceClient, { getProjectWorkspace })
    const first = recorded(
      ({ id }: { id: string }) => useProjectWorkspace(id),
      { id: 'project-a' },
      (r) => r.workspace?.candidatePath,
    )
    const rendered = first.frames.length
    first.unmount()
    await act(async () => {
      answer.resolve(new GetProjectWorkspaceResponse({ state: 'Ready', candidatePath: 'C:\\late' }))
    })
    expect(first.frames).toHaveLength(rendered)

    const second = recorded(
      ({ id }: { id: string }) => useProjectWorkspace(id),
      { id: 'project-a' },
      (r) => ({ path: r.workspace?.candidatePath, loading: r.loading }),
    )
    expect(second.frames[0].view).toEqual({ path: undefined, loading: true })
  })

  it('stops polling and starts no refresh for an accepted operation once unmounted', async () => {
    vi.useFakeTimers()
    const getProjectVerificationExecutions = vi
      .fn()
      .mockResolvedValue([new VerificationExecutionResponse({ verificationExecutionId: 'execution-a', status: 'Running' })])
    const getProjectCheckpointReviews = vi.fn().mockResolvedValue([])
    const accepted = deferred<unknown>()
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions })
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews })
    client(recordCheckpointReviewClient, { recordCheckpointReview: vi.fn().mockReturnValue(accepted.promise) })

    const polling = renderHook(() => useProjectVerificationExecutions('project-a'))
    const reviews = renderHook(() => useProjectCheckpointReviews('project-a'))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2500)
    })
    expect(getProjectVerificationExecutions.mock.calls.length).toBeGreaterThanOrEqual(3)
    act(() => {
      void reviews.result.current.record('checkpoint-1', undefined, 'Pending')
    })

    polling.unmount()
    reviews.unmount()
    const polls = getProjectVerificationExecutions.mock.calls.length
    const reads = getProjectCheckpointReviews.mock.calls.length
    await act(async () => {
      accepted.resolve({})
      await vi.advanceTimersByTimeAsync(5000)
    })
    expect(getProjectVerificationExecutions).toHaveBeenCalledTimes(polls)
    expect(getProjectCheckpointReviews).toHaveBeenCalledTimes(reads)
  })
})

describe('useProjectVerificationExecutions polling follows the newest accepted read', () => {
  const run = (id: string, status: string) =>
    new VerificationExecutionResponse({ verificationExecutionId: id, status, executionNumber: 1 })
  const useUnderTest = ({ id }: { id: string | null }) => useProjectVerificationExecutions(id)
  const tick = (ms: number) => act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })

  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('starts one bounded chain when a current refresh returns Running after an empty initial read', async () => {
    const get = vi.fn().mockResolvedValueOnce([]).mockResolvedValueOnce([run('e1', 'Running')]).mockResolvedValue([run('e1', 'Passed')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest, { initialProps: { id: 'project-a' } })
    await tick(0)
    expect(get).toHaveBeenCalledTimes(1)
    await act(async () => {
      await result.current.refresh()
    })
    expect(get).toHaveBeenCalledTimes(2)
    await tick(1000)
    expect(get).toHaveBeenCalledTimes(3)
    // The terminal answer ends the chain.
    await tick(5000)
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('lets a newer Running refresh control scheduling when the older pending poll later returns terminal', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockReturnValueOnce(older.promise).mockResolvedValueOnce([run('e1', 'Running')]).mockResolvedValue([run('e1', 'Passed')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest, { initialProps: { id: 'project-a' } })
    await tick(0)
    await act(async () => {
      await result.current.refresh()
    })
    await act(async () => {
      older.resolve([run('e0', 'Passed')])
    })
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['e1'])
    await tick(1000)
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('does not let an obsolete Running response restart polling after a newer terminal result', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockReturnValueOnce(older.promise).mockResolvedValueOnce([run('e1', 'Passed')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest, { initialProps: { id: 'project-a' } })
    await tick(0)
    await act(async () => {
      await result.current.refresh()
    })
    await act(async () => {
      older.resolve([run('e0', 'Running')])
    })
    await tick(5000)
    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['e1'])
  })

  it('keeps a single chain across repeated Running refreshes, and stops it on replacement and unmount', async () => {
    const get = vi.fn().mockResolvedValue([run('e1', 'Running')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender, unmount } = renderHook(useUnderTest, { initialProps: { id: 'project-a' } })
    await tick(0)
    await act(async () => {
      await result.current.refresh()
      await result.current.refresh()
    })
    const before = get.mock.calls.length
    await tick(1000)
    expect(get.mock.calls.length).toBe(before + 1)

    get.mockClear()
    get.mockResolvedValue([])
    rerender({ id: 'project-b' })
    await tick(5000)
    expect(get.mock.calls.map((call) => call[0])).toEqual(['project-b'])

    get.mockResolvedValue([run('e2', 'Running')])
    await act(async () => {
      await result.current.refresh()
    })
    unmount()
    get.mockClear()
    await tick(5000)
    expect(get).not.toHaveBeenCalled()
  })
})
