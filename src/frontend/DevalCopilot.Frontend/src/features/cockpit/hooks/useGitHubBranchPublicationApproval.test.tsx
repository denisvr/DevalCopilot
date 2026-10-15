import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubBranchPublicationApprovalClient, recordGitHubBranchPublicationApprovalClient } from '../../../api/clients'
import type {
  GetGitHubBranchPublicationApprovalResponse,
  RecordGitHubBranchPublicationApprovalRequest,
  RecordGitHubBranchPublicationApprovalResponse,
} from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import type { PublicationApprovalScope } from '../gitHubBranchPublicationApproval'
import { useGitHubBranchPublicationApproval } from './useGitHubBranchPublicationApproval'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubBranchPublicationApprovalClient: vi.fn(),
  recordGitHubBranchPublicationApprovalClient: vi.fn(),
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

const SHA = 'a'.repeat(40)
const BRANCH = 'devalcopilot/workspace/x/1'
const TARGET_AT = new Date('2026-10-14T12:00:00.000Z')
const OBSERVED = new Date('2026-10-14T12:01:00.000Z')
const BOUND = new Date('2026-10-14T12:02:00.000Z')
const APPROVED = new Date('2026-10-14T12:03:00.000Z')

const scopeOf = (runId: string, owner = 'octo-cat', projectId = 'project-1'): PublicationApprovalScope => ({
  projectId,
  source: { runId, operationId: 'op-' + runId, commitSha: SHA, branchName: BRANCH },
  target: {
    id: 'target-' + runId,
    eventId: 'event-' + runId,
    host: 'github.com',
    owner,
    repository: 'hello.world',
    recordedAtUtc: TARGET_AT,
  },
})
const A = scopeOf('run-a')
const B = scopeOf('run-b')

const wireSource = (scope: PublicationApprovalScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: PublicationApprovalScope) => ({ ...scope.target })
const wireBinding = (scope: PublicationApprovalScope, nodeId = 'R_one') => ({
  id: 'binding-' + scope.source.runId,
  eventId: 'binding-event-' + scope.source.runId,
  repositoryNodeId: nodeId,
  owner: scope.target.owner,
  repository: scope.target.repository,
  observedAtUtc: OBSERVED,
  recordedAtUtc: BOUND,
})
const wireApproval = (scope: PublicationApprovalScope, id: string, nodeId = 'R_one') => ({
  id,
  eventId: 'approval-event-' + scope.source.runId,
  host: 'github.com',
  action: 'PushExactCommit',
  policyVersion: 1,
  localCommitOperationId: scope.source.operationId,
  workspaceId: 'workspace-' + scope.source.runId,
  targetId: scope.target.id,
  bindingId: 'binding-' + scope.source.runId,
  repositoryNodeId: nodeId,
  owner: scope.target.owner,
  repository: scope.target.repository,
  branchName: scope.source.branchName,
  commitSha: scope.source.commitSha,
  approvedAtUtc: APPROVED,
})

const notApproved = (scope: PublicationApprovalScope, nodeId = 'R_one') =>
  ({
    runId: scope.source.runId,
    status: 'ApprovalNotRecorded',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope, nodeId),
    approval: null,
  }) as unknown as GetGitHubBranchPublicationApprovalResponse
const approved = (scope: PublicationApprovalScope, id: string) =>
  ({
    runId: scope.source.runId,
    status: 'Available',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
    approval: wireApproval(scope, id),
  }) as unknown as GetGitHubBranchPublicationApprovalResponse
const empty = (runId: string, status: 'NotRecorded' | 'Unavailable') =>
  ({ runId, status, source: null, target: null, binding: null, approval: null }) as unknown as GetGitHubBranchPublicationApprovalResponse
const posted = (scope: PublicationApprovalScope, id: string, nodeId = 'R_one') =>
  ({
    runId: scope.source.runId,
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope, nodeId),
    approval: wireApproval(scope, id, nodeId),
  }) as unknown as RecordGitHubBranchPublicationApprovalResponse

interface Mocks {
  read: ReturnType<typeof vi.fn>
  record: ReturnType<typeof vi.fn>
}

function install(overrides: Partial<{
  read: (runId: string) => Promise<GetGitHubBranchPublicationApprovalResponse>
  record: (runId: string, request: RecordGitHubBranchPublicationApprovalRequest) => Promise<RecordGitHubBranchPublicationApprovalResponse>
}> = {}): Mocks {
  const read = vi.fn(overrides.read ?? ((runId: string) => Promise.resolve(notApproved(scopeOf(runId)))))
  const record = vi.fn(
    overrides.record ?? ((runId: string, request: RecordGitHubBranchPublicationApprovalRequest) => Promise.resolve(posted(scopeOf(runId), request.approvalId))),
  )
  vi.mocked(gitHubBranchPublicationApprovalClient).mockReturnValue(
    { getGitHubBranchPublicationApproval: read } as unknown as ReturnType<typeof gitHubBranchPublicationApprovalClient>,
  )
  vi.mocked(recordGitHubBranchPublicationApprovalClient).mockReturnValue(
    { recordGitHubBranchPublicationApproval: record } as unknown as ReturnType<typeof recordGitHubBranchPublicationApprovalClient>,
  )
  return { read, record }
}

const mountFor = (initial: PublicationApprovalScope | null) =>
  renderHook(({ scope }: { scope: PublicationApprovalScope | null }) => useGitHubBranchPublicationApproval(scope), {
    initialProps: { scope: initial },
  })

type Mounted = ReturnType<typeof mountFor>

/** Opens the region and loads a not-approved read, leaving the hook armed. */
async function armed(mounted: Mounted) {
  act(() => mounted.result.current.show())
  await act(async () => mounted.result.current.reload())
}

describe('useGitHubBranchPublicationApproval', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('reads and approves nothing, and creates no request identifier, on mount, opening, rerender, a scope change, a timer or a remount', () => {
    vi.useFakeTimers()
    const mocks = install()
    const uuid = vi.spyOn(crypto, 'randomUUID')
    const mounted = mountFor(A)

    act(() => mounted.result.current.show())
    mounted.rerender({ scope: A })
    mounted.rerender({ scope: B })
    mounted.rerender({ scope: A })
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    mounted.unmount()
    const remounted = mountFor(A)
    act(() => remounted.result.current.show())

    expect(mocks.read).not.toHaveBeenCalled()
    expect(mocks.record).not.toHaveBeenCalled()
    expect(uuid).not.toHaveBeenCalled()
  })

  it('is collapsed for a new scope, does nothing without one and does nothing while collapsed', () => {
    const mocks = install()
    const none = mountFor(null)
    act(() => none.result.current.show())
    act(() => none.result.current.reload())
    act(() => none.result.current.approve())
    const collapsed = mountFor(A)
    act(() => collapsed.result.current.reload())
    act(() => collapsed.result.current.approve())

    expect(none.result.current.open).toBe(false)
    expect(collapsed.result.current.open).toBe(false)
    expect(mocks.read).not.toHaveBeenCalled()
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('arms an approval only after a settled ApprovalNotRecorded read and sends exactly the seven facts shown', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000001')
    const mocks = install()
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    expect(mounted.result.current.canApprove).toBe(false)
    act(() => mounted.result.current.approve())
    expect(mocks.record).not.toHaveBeenCalled()

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.canApprove).toBe(true)
    await act(async () => mounted.result.current.approve())

    expect(mocks.record).toHaveBeenCalledTimes(1)
    const [runId, request] = mocks.record.mock.calls[0] as [string, RecordGitHubBranchPublicationApprovalRequest]
    expect(runId).toBe('run-a')
    expect({ ...request }).toEqual({
      approvalId: '00000000-0000-4000-8000-000000000001',
      operationId: 'op-run-a',
      commitSha: SHA,
      branchName: BRANCH,
      targetId: 'target-run-a',
      bindingId: 'binding-run-a',
      repositoryNodeId: 'R_one',
    })
    expect(mounted.result.current.load).toMatchObject({
      kind: 'result',
      reading: { kind: 'Available', approval: { id: '00000000-0000-4000-8000-000000000001' } },
    })
    expect(mounted.result.current.canApprove).toBe(false)
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
  })

  it.each([
    ['Available', (scope: PublicationApprovalScope) => approved(scope, 'approval-1')],
    ['Unavailable', (scope: PublicationApprovalScope) => empty(scope.source.runId, 'Unavailable')],
    ['NotRecorded', (scope: PublicationApprovalScope) => empty(scope.source.runId, 'NotRecorded')],
    ['an incoherent answer', (scope: PublicationApprovalScope) => ({ ...notApproved(scope), runId: 'run-other' })],
  ])('is not armed when the host reads %s', async (_name, reading) => {
    const mocks = install({ read: (runId) => Promise.resolve(reading(scopeOf(runId)) as GetGitHubBranchPublicationApprovalResponse) })
    const mounted = mountFor(A)
    await armed(mounted)

    expect(mounted.result.current.canApprove).toBe(false)
    act(() => mounted.result.current.approve())
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('is not armed by a failed read and shows no stale reading', async () => {
    const mocks = install({ read: () => Promise.reject(new Error('offline')) })
    const mounted = mountFor(A)
    await armed(mounted)

    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'read' })
    expect(mounted.result.current.canApprove).toBe(false)
    act(() => mounted.result.current.approve())
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('sends one read for two activations in the same tick, for any mix of actions, and for a second while pending', async () => {
    const first = deferred<GetGitHubBranchPublicationApprovalResponse>()
    const mocks = install({ read: () => first.promise })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.reload()
      mounted.result.current.reload()
      mounted.result.current.approve()
    })
    act(() => mounted.result.current.reload())

    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mocks.record).not.toHaveBeenCalled()
    expect(mounted.result.current.load).toEqual({ kind: 'pending' })
    await act(async () => first.resolve(notApproved(A)))
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it('sends one approval for two activations in the same batch and refuses an approval and a reload while one is pending', async () => {
    const pending = deferred<RecordGitHubBranchPublicationApprovalResponse>()
    const mocks = install({ record: () => pending.promise })
    const mounted = mountFor(A)
    await armed(mounted)

    act(() => {
      mounted.result.current.approve()
      mounted.result.current.approve()
    })
    act(() => {
      mounted.result.current.approve()
      mounted.result.current.reload()
    })

    expect(mocks.record).toHaveBeenCalledTimes(1)
    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.write).toEqual({ kind: 'pending' })
    expect(mounted.result.current.canApprove).toBe(false)
    const request = mocks.record.mock.calls[0][1] as RecordGitHubBranchPublicationApprovalRequest
    await act(async () => pending.resolve(posted(A, request.approvalId)))
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available' } })
    expect(mocks.record).toHaveBeenCalledTimes(1)
  })

  it('disarms synchronously on a reload, so a callback retained from an armed render cannot approve, even after the read settles', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current.approve

    act(() => {
      mounted.result.current.reload()
      retained()
    })
    expect(mocks.record).not.toHaveBeenCalled()
    await act(async () => undefined)

    expect(mounted.result.current.canApprove).toBe(true)
    act(() => retained())
    expect(mocks.record).not.toHaveBeenCalled()
    await act(async () => mounted.result.current.approve())
    expect(mocks.record).toHaveBeenCalledTimes(1)
  })

  it('uses the facts of the settled read it was armed by, never an earlier one', async () => {
    let node = 'R_first'
    const mocks = install({ read: (runId) => Promise.resolve(notApproved(scopeOf(runId), node)) })
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current.approve
    node = 'R_second'

    await act(async () => mounted.result.current.reload())
    act(() => retained())
    expect(mocks.record).not.toHaveBeenCalled()

    await act(async () => mounted.result.current.approve())
    expect(mocks.record).toHaveBeenCalledTimes(1)
    expect((mocks.record.mock.calls[0][1] as RecordGitHubBranchPublicationApprovalRequest).repositoryNodeId).toBe('R_second')
  })

  it('ignores the late read of a closed opening and starts blank when reopened', async () => {
    const late = deferred<GetGitHubBranchPublicationApprovalResponse>()
    const mocks = install({ read: () => late.promise })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())
    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())

    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, write: { kind: 'idle' } })
    await act(async () => late.resolve(notApproved(A)))

    expect(mounted.result.current.load).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canApprove).toBe(false)
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it.each(['A-B', 'A-B-A', 'close-reopen'])('%s: nothing of an earlier lifetime can approve or settle into the successor', async (flow) => {
    const old = deferred<GetGitHubBranchPublicationApprovalResponse>()
    let calls = 0
    const mocks = install({ read: (runId) => (++calls === 1 ? old.promise : Promise.resolve(notApproved(scopeOf(runId), 'R_successor'))) })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())
    const retained = mounted.result.current
    if (flow === 'close-reopen') {
      act(() => mounted.result.current.close())
    } else {
      mounted.rerender({ scope: B })
      if (flow === 'A-B-A') {
        mounted.rerender({ scope: A })
      }
    }

    act(() => mounted.result.current.show())
    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, write: { kind: 'idle' } })
    await act(async () => old.resolve(notApproved(A, 'R_old')))
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })

    act(() => retained.approve())
    act(() => retained.reload())
    expect(mocks.record).not.toHaveBeenCalled()
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it.each(['A-B', 'A-B-A', 'close-reopen'])('%s: the late completion of an approval cannot settle into or clear the successor', async (flow) => {
    const late = deferred<RecordGitHubBranchPublicationApprovalResponse>()
    const mocks = install({ record: () => late.promise })
    const mounted = mountFor(A)
    await armed(mounted)
    act(() => mounted.result.current.approve())
    expect(mounted.result.current.write).toEqual({ kind: 'pending' })
    const request = mocks.record.mock.calls[0][1] as RecordGitHubBranchPublicationApprovalRequest
    if (flow === 'close-reopen') {
      act(() => mounted.result.current.close())
    } else {
      mounted.rerender({ scope: B })
      if (flow === 'A-B-A') {
        mounted.rerender({ scope: A })
      }
    }

    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'ApprovalNotRecorded' } })
    await act(async () => late.resolve(posted(A, request.approvalId)))

    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'ApprovalNotRecorded' } })
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canApprove).toBe(true)
  })

  it.each(['A-B', 'A-B-A', 'close-reopen'])(
    '%s: the late completion of an approval cannot release the single-flight guard of the successor that is reading',
    async (flow) => {
      const lateWrite = deferred<RecordGitHubBranchPublicationApprovalResponse>()
      const successorRead = deferred<GetGitHubBranchPublicationApprovalResponse>()
      let reads = 0
      const mocks = install({
        read: (runId) => (++reads === 1 ? Promise.resolve(notApproved(scopeOf(runId))) : successorRead.promise),
        record: () => lateWrite.promise,
      })
      const mounted = mountFor(A)
      await armed(mounted)
      act(() => mounted.result.current.approve())
      const request = mocks.record.mock.calls[0][1] as RecordGitHubBranchPublicationApprovalRequest
      if (flow === 'close-reopen') {
        act(() => mounted.result.current.close())
      } else {
        mounted.rerender({ scope: B })
        if (flow === 'A-B-A') {
          mounted.rerender({ scope: A })
        }
      }

      act(() => mounted.result.current.show())
      act(() => mounted.result.current.reload())
      expect(mocks.read).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.load).toEqual({ kind: 'pending' })

      await act(async () => lateWrite.resolve(posted(A, request.approvalId)))
      act(() => {
        mounted.result.current.reload()
        mounted.result.current.reload()
      })

      expect(mocks.read).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.load).toEqual({ kind: 'pending' })
      await act(async () => successorRead.resolve(notApproved(flow === 'A-B' ? B : A)))
      expect(mounted.result.current.canApprove).toBe(true)
    },
  )

  it.each(['A-B', 'A-B-A', 'close-reopen'])(
    '%s: the late read of an ended lifetime cannot release the single-flight guard of the successor that is reading',
    async (flow) => {
      const oldRead = deferred<GetGitHubBranchPublicationApprovalResponse>()
      const successorRead = deferred<GetGitHubBranchPublicationApprovalResponse>()
      let reads = 0
      const mocks = install({ read: () => (++reads === 1 ? oldRead.promise : successorRead.promise) })
      const mounted = mountFor(A)
      act(() => mounted.result.current.show())
      act(() => mounted.result.current.reload())
      if (flow === 'close-reopen') {
        act(() => mounted.result.current.close())
      } else {
        mounted.rerender({ scope: B })
        if (flow === 'A-B-A') {
          mounted.rerender({ scope: A })
        }
      }

      act(() => mounted.result.current.show())
      act(() => mounted.result.current.reload())
      expect(mocks.read).toHaveBeenCalledTimes(2)

      await act(async () => oldRead.resolve(notApproved(A)))
      act(() => {
        mounted.result.current.reload()
        mounted.result.current.reload()
      })

      expect(mocks.read).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.load).toEqual({ kind: 'pending' })
      await act(async () => successorRead.resolve(notApproved(flow === 'A-B' ? B : A)))
      expect(mounted.result.current.canApprove).toBe(true)
    },
  )

  it('the late failure of an approval of an ended opening cannot raise an alert or clear the successor', async () => {
    const late = deferred<RecordGitHubBranchPublicationApprovalResponse>()
    install({ record: () => late.promise })
    const mounted = mountFor(A)
    await armed(mounted)
    act(() => mounted.result.current.approve())
    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())

    await act(async () => late.reject(new Error('offline')))

    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'ApprovalNotRecorded' } })
  })

  it('refuses an approval after a close in the same batch and from a callback retained from an ended opening', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current

    act(() => {
      mounted.result.current.close()
      mounted.result.current.approve()
    })
    expect(mocks.record).not.toHaveBeenCalled()

    act(() => mounted.result.current.show())
    act(() => retained.approve())
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('treats a change of the recorded target, of the source or of the project as another scope', () => {
    install()
    for (const changed of [scopeOf('run-a', 'someone-else'), scopeOf('run-a', 'octo-cat', 'project-2'), B]) {
      const mounted = mountFor(A)
      act(() => mounted.result.current.show())

      mounted.rerender({ scope: changed })

      expect(mounted.result.current.open).toBe(false)
      mounted.unmount()
    }
  })

  describe('the request identifier', () => {
    it('is created on the first activation that sends, retained for a recovery retry in the same opening, and new for a new opening or scope', async () => {
      const ids = ['00000000-0000-4000-8000-00000000000a', '00000000-0000-4000-8000-00000000000b', '00000000-0000-4000-8000-00000000000c']
      const uuid = vi.spyOn(crypto, 'randomUUID').mockImplementation(() => ids.shift() as `${string}-${string}-${string}-${string}-${string}`)
      let attempt = 0
      const mocks = install({ record: (runId, request) => (++attempt === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(posted(scopeOf(runId), request.approvalId))) })
      const mounted = mountFor(A)
      await armed(mounted)
      expect(uuid).not.toHaveBeenCalled()

      await act(async () => mounted.result.current.approve())
      expect(uuid).toHaveBeenCalledTimes(1)
      await act(async () => mounted.result.current.reload())
      await act(async () => mounted.result.current.approve())

      expect(uuid).toHaveBeenCalledTimes(1)
      expect(mocks.record.mock.calls.map((call) => (call[1] as RecordGitHubBranchPublicationApprovalRequest).approvalId)).toEqual([
        '00000000-0000-4000-8000-00000000000a',
        '00000000-0000-4000-8000-00000000000a',
      ])

      act(() => mounted.result.current.close())
      act(() => mounted.result.current.show())
      await act(async () => mounted.result.current.reload())
      await act(async () => mounted.result.current.approve())
      expect((mocks.record.mock.calls[2][1] as RecordGitHubBranchPublicationApprovalRequest).approvalId).toBe('00000000-0000-4000-8000-00000000000b')

      mounted.rerender({ scope: B })
      act(() => mounted.result.current.show())
      await act(async () => mounted.result.current.reload())
      await act(async () => mounted.result.current.approve())
      expect((mocks.record.mock.calls[3][1] as RecordGitHubBranchPublicationApprovalRequest).approvalId).toBe('00000000-0000-4000-8000-00000000000c')
    })

    it('is not consumed by activations that send nothing', async () => {
      const uuid = vi.spyOn(crypto, 'randomUUID')
      install({ read: (runId) => Promise.resolve(empty(runId, 'Unavailable')) })
      const mounted = mountFor(A)
      await armed(mounted)

      act(() => mounted.result.current.approve())
      act(() => mounted.result.current.approve())

      expect(uuid).not.toHaveBeenCalled()
    })
  })

  describe('after a write that did not record a coherent approval', () => {
    const FAILED_WRITES: [string, 'refused' | 'ambiguous', () => Partial<Parameters<typeof install>[0]>][] = [
      ['a 400', 'refused', () => ({ record: () => Promise.reject(new ApiException('bad', 400, '', {}, null)) })],
      ['a 404', 'refused', () => ({ record: () => Promise.reject(new ApiException('gone', 404, '', {}, null)) })],
      ['a 409', 'refused', () => ({ record: () => Promise.reject(new ApiException('conflict', 409, '', {}, null)) })],
      ['a 413', 'refused', () => ({ record: () => Promise.reject(new ApiException('large', 413, '', {}, null)) })],
      ['a 500', 'ambiguous', () => ({ record: () => Promise.reject(new ApiException('fault', 500, '', {}, null)) })],
      ['a network failure', 'ambiguous', () => ({ record: () => Promise.reject(new Error('offline')) })],
      ['another approval identifier', 'ambiguous', () => ({ record: (runId) => Promise.resolve(posted(scopeOf(runId), 'someone-elses')) })],
      ['another node identifier', 'ambiguous', () => ({ record: (runId, request) => Promise.resolve(posted(scopeOf(runId), request.approvalId, 'R_other')) })],
      ['an answer for another run', 'ambiguous', () => ({ record: (runId, request) => Promise.resolve({ ...posted(scopeOf(runId), request.approvalId), runId: 'run-x' } as RecordGitHubBranchPublicationApprovalResponse) })],
      ['an answer without an approval', 'ambiguous', () => ({ record: (runId, request) => Promise.resolve({ ...posted(scopeOf(runId), request.approvalId), approval: undefined } as RecordGitHubBranchPublicationApprovalResponse) })],
    ]

    it.each(FAILED_WRITES)('%s clears the reading, keeps a fixed alert and needs an explicit reload before another approval', async (_name, reason, overrides) => {
      const mocks = install(overrides())
      const mounted = mountFor(A)
      await armed(mounted)

      await act(async () => mounted.result.current.approve())

      expect(mounted.result.current.write).toEqual({ kind: 'failed', reason })
      expect(mounted.result.current.load).toEqual({ kind: 'idle' })
      expect(mounted.result.current.canApprove).toBe(false)
      act(() => mounted.result.current.approve())
      expect(mocks.record).toHaveBeenCalledTimes(1)

      await act(async () => mounted.result.current.reload())

      expect(mounted.result.current.write).toEqual({ kind: 'idle' })
      expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'ApprovalNotRecorded' } })
      expect(mounted.result.current.canApprove).toBe(true)
    })

    it('keeps the alert when the recovery read itself fails, and never retries on its own', async () => {
      vi.useFakeTimers()
      let reads = 0
      const mocks = install({
        read: (runId) => (++reads === 1 ? Promise.resolve(notApproved(scopeOf(runId))) : Promise.reject(new Error('offline'))),
        record: () => Promise.reject(new Error('offline')),
      })
      const mounted = mountFor(A)
      await armed(mounted)
      await act(async () => mounted.result.current.approve())

      await act(async () => mounted.result.current.reload())
      act(() => {
        vi.advanceTimersByTime(10 * 60 * 1000)
      })

      expect(mounted.result.current.write).toEqual({ kind: 'failed', reason: 'ambiguous' })
      expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'read' })
      expect(mounted.result.current.canApprove).toBe(false)
      expect(mocks.record).toHaveBeenCalledTimes(1)
      expect(mocks.read).toHaveBeenCalledTimes(2)
    })

    it('shows the recorded consent when the recovery read finds the approval that the unknown write did record', async () => {
      let recorded = false
      install({
        read: (runId) => Promise.resolve(recorded ? approved(scopeOf(runId), 'approval-1') : notApproved(scopeOf(runId))),
        record: () => {
          recorded = true
          return Promise.reject(new Error('offline'))
        },
      })
      const mounted = mountFor(A)
      await armed(mounted)
      await act(async () => mounted.result.current.approve())

      await act(async () => mounted.result.current.reload())

      expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available' } })
      expect(mounted.result.current.write).toEqual({ kind: 'idle' })
      expect(mounted.result.current.canApprove).toBe(false)
    })
  })

  it('has no execution action and never sends anything further after the consent is recorded', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    await armed(mounted)
    await act(async () => mounted.result.current.approve())

    act(() => mounted.result.current.approve())
    act(() => mounted.result.current.approve())

    expect(mocks.record).toHaveBeenCalledTimes(1)
    expect(Object.keys(mounted.result.current).sort()).toEqual(['approve', 'canApprove', 'close', 'load', 'open', 'reload', 'show', 'write'])
  })
})
