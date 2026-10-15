import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubRepositoryPermissionObservationClient } from '../../../api/clients'
import type { GetGitHubRepositoryPermissionObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import type { PermissionObservationScope } from '../gitHubRepositoryPermissionObservation'
import { useGitHubRepositoryPermissionObservation } from './useGitHubRepositoryPermissionObservation'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubRepositoryPermissionObservationClient: vi.fn(),
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
const AT = new Date('2026-10-12T20:00:00.000Z')
const BOUND_OBSERVED = new Date('2026-10-12T20:10:00.000Z')
const BOUND_RECORDED = new Date('2026-10-12T20:11:00.000Z')
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')
const NODE_ID = 'R_kgDOExample01'

const scopeOf = (runId: string, owner = 'octo-cat'): PermissionObservationScope => {
  const source: ObservationSource = { runId, operationId: 'op-' + runId, commitSha: SHA, branchName: 'devalcopilot/workspace/x/1' }
  const target: ObservationScope = {
    id: 'target-' + runId,
    eventId: 'event-' + runId,
    host: 'github.com',
    owner,
    repository: 'hello.world',
    recordedAtUtc: AT,
  }
  return { source, target }
}
const A = scopeOf('run-a')
const B = scopeOf('run-b')

const wireSource = (scope: PermissionObservationScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: PermissionObservationScope) => ({ ...scope.target })
const wireBinding = (scope: PermissionObservationScope) => ({
  id: 'binding-' + scope.source.runId,
  eventId: 'binding-event-' + scope.source.runId,
  repositoryNodeId: NODE_ID,
  owner: scope.target.owner,
  repository: scope.target.repository,
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
})
const reportedFor = (scope: PermissionObservationScope, permission: string | null = 'WRITE', repositoryNodeId = NODE_ID) =>
  ({
    runId: scope.source.runId,
    status: permission === null ? 'PermissionNotReturned' : 'PermissionReported',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
    observation: {
      repositoryNodeId,
      owner: scope.target.owner,
      repository: scope.target.repository,
      observedAtUtc: OBSERVED,
      viewerPermission: permission,
    },
  }) as unknown as GetGitHubRepositoryPermissionObservationResponse
const statusFor = (scope: PermissionObservationScope, status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged') =>
  ({
    runId: scope.source.runId,
    status,
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
  }) as unknown as GetGitHubRepositoryPermissionObservationResponse

function install(read: (runId: string) => Promise<GetGitHubRepositoryPermissionObservationResponse>) {
  const getGitHubRepositoryPermissionObservation = vi.fn(read)
  vi.mocked(gitHubRepositoryPermissionObservationClient).mockReturnValue(
    { getGitHubRepositoryPermissionObservation } as unknown as ReturnType<typeof gitHubRepositoryPermissionObservationClient>,
  )
  return getGitHubRepositoryPermissionObservation
}

const mountFor = (initial: PermissionObservationScope | null) =>
  renderHook(({ scope }: { scope: PermissionObservationScope | null }) => useGitHubRepositoryPermissionObservation(scope), {
    initialProps: { scope: initial },
  })

describe('useGitHubRepositoryPermissionObservation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing on mount, opening, rerender, a scope change, a timer or a reconnect-like remount', () => {
    vi.useFakeTimers()
    const get = install((id) => Promise.resolve(reportedFor(scopeOf(id))))
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

    expect(get).not.toHaveBeenCalled()
  })

  it('is collapsed and idle for a newly committed scope and does nothing without a scope', () => {
    const get = install((id) => Promise.resolve(reportedFor(scopeOf(id))))
    const mounted = mountFor(null)

    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    expect(get).not.toHaveBeenCalled()
  })

  it('does nothing when asked to check while collapsed', () => {
    const get = install((id) => Promise.resolve(reportedFor(scopeOf(id))))
    const mounted = mountFor(A)

    act(() => mounted.result.current.check())

    expect(get).not.toHaveBeenCalled()
  })

  it('reads the exact run once per explicit check, hides any answer while pending and ignores a second activation in flight', async () => {
    const first = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => first.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.check()
      mounted.result.current.check()
    })
    act(() => mounted.result.current.check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })

    await act(async () => first.resolve(reportedFor(A)))

    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'PermissionReported', viewerPermission: 'WRITE' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows the CLI-unavailable, unknown and identity-changed statuses with the binding and a recheck hides the earlier answer', async () => {
    const answers = [statusFor(A, 'CliUnavailable'), statusFor(A, 'Unknown'), statusFor(A, 'IdentityChanged'), reportedFor(A, 'READ')]
    const last = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => (answers.length > 1 ? Promise.resolve(answers.shift()!) : last.promise))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged']) {
      await act(async () => mounted.result.current.check())
      expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status, binding: { repositoryNodeId: NODE_ID } } })
    }

    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => last.resolve(reportedFor(A, 'READ')))

    expect(get).toHaveBeenCalledTimes(4)
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'PermissionReported', viewerPermission: 'READ' } })
  })

  it('removes the previous observation while pending and after a failure, and does not retry', async () => {
    const second = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.resolve(reportedFor(A, 'WRITE')) : calls === 2 ? second.promise : Promise.reject(new Error('x'))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result' })

    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.reject(new Error('network')))
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('reports a failed read and an incoherent answer, never a partial one', async () => {
    const get = install(() => Promise.reject(new Error('network')))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })

    get.mockImplementation(() => Promise.resolve({ ...reportedFor(A), runId: 'someone-else' } as GetGitHubRepositoryPermissionObservationResponse))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockImplementation(() => Promise.resolve(reportedFor(A, 'WRITE', 'R_other')))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockImplementation(() => Promise.resolve(reportedFor(A, 'OWNER')))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
    expect(get).toHaveBeenCalledTimes(4)
  })

  it('ignores the late answer of a closed opening and starts unchecked when reopened', async () => {
    const late = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())
    act(() => mounted.result.current.close())

    act(() => mounted.result.current.show())
    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    await act(async () => late.resolve(reportedFor(A)))

    expect(mounted.result.current.phase).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('ignores a late answer after the scope changed or returned (A to B to A) and starts collapsed each time', async () => {
    const late = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: B })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    mounted.rerender({ scope: A })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    act(() => mounted.result.current.show())
    await act(async () => late.resolve(reportedFor(A)))

    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('treats a change of the recorded target as another scope', () => {
    install((id) => Promise.resolve(reportedFor(scopeOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    mounted.rerender({ scope: scopeOf('run-a', 'someone-else') })

    expect(mounted.result.current.open).toBe(false)
  })

  it('refuses a check after a close in the same batch and a check from a callback retained from an ended opening', async () => {
    const get = install((id) => Promise.resolve(reportedFor(scopeOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    const retained = mounted.result.current

    act(() => {
      mounted.result.current.close()
      mounted.result.current.check()
    })
    expect(get).not.toHaveBeenCalled()

    act(() => mounted.result.current.show())
    await act(async () => retained.check())
    act(() => retained.close())

    expect(get).not.toHaveBeenCalled()
    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
  })

  it('does not let a stale close of an earlier opening close its successor', () => {
    install((id) => Promise.resolve(reportedFor(scopeOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    const staleClose = mounted.result.current.close
    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())

    act(() => staleClose())

    expect(mounted.result.current.open).toBe(true)
  })

  it('sends a check that precedes a close in the same batch but ignores its answer', async () => {
    const late = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.check()
      mounted.result.current.close()
    })
    await act(async () => late.resolve(reportedFor(A)))

    expect(get).toHaveBeenCalledTimes(1)
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
  })

  it('ignores the answer after the component unmounts', async () => {
    const late = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    const get = install(() => late.promise)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.unmount()
    late.resolve(reportedFor(A))
    await late.promise
    await Promise.resolve()

    expect(get).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('shows an explicit null permission as PermissionNotReturned and a recheck hides it while pending', async () => {
    const second = deferred<GetGitHubRepositoryPermissionObservationResponse>()
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.resolve(reportedFor(A, null)) : second.promise))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'PermissionNotReturned' } })

    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(reportedFor(A, 'TRIAGE_PLUS')))

    expect(get).toHaveBeenCalledTimes(2)
    expect(mounted.result.current.phase).toMatchObject({
      kind: 'result',
      reading: { status: 'PermissionReported', viewerPermission: 'TRIAGE_PLUS' },
    })
  })

  it('accepts each of the six literals as returned and refuses an unknown literal as an invalid answer', async () => {
    const answers: (string | null)[] = ['READ', 'TRIAGE', 'TRIAGE_PLUS', 'WRITE', 'MAINTAIN', 'ADMIN', 'owner']
    const get = install(() => Promise.resolve(reportedFor(A, answers.shift()!)))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    for (const literal of ['READ', 'TRIAGE', 'TRIAGE_PLUS', 'WRITE', 'MAINTAIN', 'ADMIN']) {
      await act(async () => mounted.result.current.check())
      expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { viewerPermission: literal } })
    }

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
    expect(get).toHaveBeenCalledTimes(7)
  })

  it('uses no browser storage and sends exactly the one request of an explicit check', async () => {
    const reads = vi.spyOn(Storage.prototype, 'getItem')
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    const get = install(() => Promise.resolve(reportedFor(A)))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.check())
    mounted.unmount()

    expect(get).toHaveBeenCalledTimes(1)
    expect(reads).not.toHaveBeenCalled()
    expect(writes).not.toHaveBeenCalled()
    reads.mockRestore()
    writes.mockRestore()
  })

  // The settling of an ended read must not release the in-flight guard of its successor, and the successor must still accept its own answer.
  it.each(['A-B', 'close-reopen', 'A-B-A'])(
    '%s: an old read settling keeps the successor check in flight and the successor answer is accepted',
    async (flow) => {
      const old = deferred<GetGitHubRepositoryPermissionObservationResponse>()
      const successor = deferred<GetGitHubRepositoryPermissionObservationResponse>()
      let calls = 0
      const get = install(() => (++calls === 1 ? old.promise : successor.promise))
      const mounted = mountFor(A)
      act(() => mounted.result.current.show())
      act(() => mounted.result.current.check())
      if (flow === 'close-reopen') {
        act(() => mounted.result.current.close())
      } else {
        mounted.rerender({ scope: B })
        if (flow === 'A-B-A') {
          mounted.rerender({ scope: A })
        }
      }

      act(() => mounted.result.current.show())
      act(() => mounted.result.current.check())
      expect(get).toHaveBeenCalledTimes(2)

      await act(async () => old.resolve(reportedFor(A, 'READ')))
      expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
      act(() => mounted.result.current.check())
      expect(get).toHaveBeenCalledTimes(2)

      await act(async () => successor.resolve(reportedFor(flow === 'A-B' ? B : A, 'MAINTAIN')))
      expect(get).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.phase).toMatchObject({
        kind: 'result',
        reading: { status: 'PermissionReported', viewerPermission: 'MAINTAIN' },
      })
    },
  )
})
