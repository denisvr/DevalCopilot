import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  bindGitHubRepositoryIdentityClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
} from '../../../api/clients'
import type {
  BindGitHubRepositoryIdentityResponse,
  GetGitHubRepositoryIdentityBindingResponse,
  GetGitHubRepositoryObservationResponse,
} from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import type { RepositoryObservationScope } from '../gitHubRepositoryObservation'
import { useGitHubRepositoryIdentityBinding } from './useGitHubRepositoryIdentityBinding'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  bindGitHubRepositoryIdentityClient: vi.fn(),
  gitHubRepositoryIdentityBindingClient: vi.fn(),
  gitHubRepositoryObservationClient: vi.fn(),
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
const TARGET_AT = new Date('2026-10-13T12:00:00.000Z')
const OBSERVED = new Date('2026-10-13T12:01:00.000Z')
const BOUND = new Date('2026-10-13T12:02:00.000Z')

const scopeOf = (runId: string, owner = 'octo-cat'): RepositoryObservationScope => {
  const source: ObservationSource = { runId, operationId: 'op-' + runId, commitSha: SHA, branchName: 'devalcopilot/workspace/x/1' }
  const target: ObservationScope = {
    id: 'target-' + runId,
    eventId: 'event-' + runId,
    host: 'github.com',
    owner,
    repository: 'hello.world',
    recordedAtUtc: TARGET_AT,
  }
  return { source, target }
}
const A = scopeOf('run-a')
const B = scopeOf('run-b')

const wireSource = (scope: RepositoryObservationScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: RepositoryObservationScope) => ({ ...scope.target })
const wireBinding = (scope: RepositoryObservationScope, nodeId = 'R_one') => ({
  id: 'binding-' + scope.source.runId,
  eventId: 'binding-event-' + scope.source.runId,
  repositoryNodeId: nodeId,
  owner: scope.target.owner,
  repository: scope.target.repository,
  observedAtUtc: OBSERVED,
  recordedAtUtc: BOUND,
})

const notBound = (scope: RepositoryObservationScope) =>
  ({
    runId: scope.source.runId,
    state: 'NotRecorded',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: null,
  }) as unknown as GetGitHubRepositoryIdentityBindingResponse
const bound = (scope: RepositoryObservationScope, nodeId = 'R_one') =>
  ({
    runId: scope.source.runId,
    state: 'Available',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope, nodeId),
  }) as unknown as GetGitHubRepositoryIdentityBindingResponse
const observed = (scope: RepositoryObservationScope, nodeId = 'R_one') =>
  ({
    runId: scope.source.runId,
    status: 'Observed',
    source: wireSource(scope),
    target: wireTarget(scope),
    observation: { repositoryNodeId: nodeId, owner: scope.target.owner, repository: scope.target.repository, observedAtUtc: OBSERVED },
  }) as unknown as GetGitHubRepositoryObservationResponse
const posted = (scope: RepositoryObservationScope, nodeId = 'R_one') =>
  ({
    runId: scope.source.runId,
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope, nodeId),
  }) as unknown as BindGitHubRepositoryIdentityResponse

interface Mocks {
  read: ReturnType<typeof vi.fn>
  observe: ReturnType<typeof vi.fn>
  bind: ReturnType<typeof vi.fn>
}

function install(overrides: Partial<{
  read: (runId: string) => Promise<GetGitHubRepositoryIdentityBindingResponse>
  observe: (runId: string) => Promise<GetGitHubRepositoryObservationResponse>
  bind: (runId: string, request: { repositoryNodeId: string }) => Promise<BindGitHubRepositoryIdentityResponse>
}> = {}): Mocks {
  const read = vi.fn(overrides.read ?? ((runId: string) => Promise.resolve(notBound(scopeOf(runId)))))
  const observe = vi.fn(overrides.observe ?? ((runId: string) => Promise.resolve(observed(scopeOf(runId)))))
  const bind = vi.fn(overrides.bind ?? ((runId: string) => Promise.resolve(posted(scopeOf(runId)))))
  vi.mocked(gitHubRepositoryIdentityBindingClient).mockReturnValue(
    { getGitHubRepositoryIdentityBinding: read } as unknown as ReturnType<typeof gitHubRepositoryIdentityBindingClient>,
  )
  vi.mocked(gitHubRepositoryObservationClient).mockReturnValue(
    { getGitHubRepositoryObservation: observe } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>,
  )
  vi.mocked(bindGitHubRepositoryIdentityClient).mockReturnValue(
    { bindGitHubRepositoryIdentity: bind } as unknown as ReturnType<typeof bindGitHubRepositoryIdentityClient>,
  )
  return { read, observe, bind }
}

const mountFor = (initial: RepositoryObservationScope | null) =>
  renderHook(({ scope }: { scope: RepositoryObservationScope | null }) => useGitHubRepositoryIdentityBinding(scope), {
    initialProps: { scope: initial },
  })

type Mounted = ReturnType<typeof mountFor>

/** Opens the region, loads a not-bound read and checks a candidate, leaving the hook armed. */
async function armed(mounted: Mounted) {
  act(() => mounted.result.current.show())
  await act(async () => mounted.result.current.reload())
  await act(async () => mounted.result.current.check())
}

describe('useGitHubRepositoryIdentityBinding', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads, observes and binds nothing on mount, opening, rerender, a scope change, a timer or a remount', () => {
    vi.useFakeTimers()
    const mocks = install()
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
    expect(mocks.observe).not.toHaveBeenCalled()
    expect(mocks.bind).not.toHaveBeenCalled()
  })

  it('is collapsed for a new scope, does nothing without one and does nothing while collapsed', () => {
    const mocks = install()
    const none = mountFor(null)
    act(() => none.result.current.show())
    act(() => none.result.current.reload())
    const collapsed = mountFor(A)
    act(() => collapsed.result.current.reload())
    act(() => collapsed.result.current.check())
    act(() => collapsed.result.current.bind())

    expect(none.result.current.open).toBe(false)
    expect(mocks.read).not.toHaveBeenCalled()
    expect(mocks.observe).not.toHaveBeenCalled()
    expect(mocks.bind).not.toHaveBeenCalled()
  })

  it('arms a bind only after a settled bindable read and an Observed candidate, and binds exactly the candidate shown', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    expect(mounted.result.current.canBind).toBe(false)

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.canBind).toBe(false)
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.canBind).toBe(true)
    expect(mounted.result.current.candidateIdentity).toMatchObject({ repositoryNodeId: 'R_one' })

    await act(async () => mounted.result.current.bind())

    expect(mocks.bind).toHaveBeenCalledTimes(1)
    expect(mocks.bind).toHaveBeenCalledWith('run-a', expect.objectContaining({
      operationId: 'op-run-a',
      commitSha: SHA,
      targetId: 'target-run-a',
      repositoryNodeId: 'R_one',
    }))
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available', binding: { repositoryNodeId: 'R_one' } } })
    expect(mounted.result.current.canBind).toBe(false)
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
  })

  it('is never armed by a candidate alone, by a read alone, or by a non-Observed candidate', async () => {
    install({ observe: (runId) => Promise.resolve({ runId, status: 'CliUnavailable', source: wireSource(scopeOf(runId)), target: wireTarget(scopeOf(runId)) } as unknown as GetGitHubRepositoryObservationResponse) })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.canBind).toBe(false)
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.canBind).toBe(false)
    act(() => mounted.result.current.bind())

    expect(vi.mocked(bindGitHubRepositoryIdentityClient)).not.toHaveBeenCalled()
  })

  it('is not armed when the host reads Available, Unavailable or a not bindable NotRecorded', async () => {
    for (const reading of [bound(A), { runId: 'run-a', state: 'Unavailable', source: null, target: null, binding: null }, { runId: 'run-a', state: 'NotRecorded', source: null, target: null, binding: null }]) {
      vi.clearAllMocks()
      const mocks = install({ read: () => Promise.resolve(reading as unknown as GetGitHubRepositoryIdentityBindingResponse) })
      const mounted = mountFor(A)
      await armed(mounted)

      expect(mounted.result.current.canBind).toBe(false)
      act(() => mounted.result.current.bind())
      expect(mocks.bind).not.toHaveBeenCalled()
      mounted.unmount()
    }
  })

  it('sends one request for two activations in the same tick, for any mix of actions, and for a second while pending', async () => {
    const first = deferred<GetGitHubRepositoryIdentityBindingResponse>()
    const mocks = install({ read: () => first.promise })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.reload()
      mounted.result.current.reload()
      mounted.result.current.check()
      mounted.result.current.bind()
    })
    act(() => mounted.result.current.check())

    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mocks.observe).not.toHaveBeenCalled()
    expect(mounted.result.current.load).toEqual({ kind: 'pending' })

    await act(async () => first.resolve(notBound(A)))
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it('sends one bind for two activations in the same batch and refuses a bind while one is pending', async () => {
    const pending = deferred<BindGitHubRepositoryIdentityResponse>()
    const mocks = install({ bind: () => pending.promise })
    const mounted = mountFor(A)
    await armed(mounted)

    act(() => {
      mounted.result.current.bind()
      mounted.result.current.bind()
    })
    act(() => mounted.result.current.bind())

    expect(mocks.bind).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.write).toEqual({ kind: 'pending' })
    await act(async () => pending.resolve(posted(A)))
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available' } })
    expect(mocks.bind).toHaveBeenCalledTimes(1)
  })

  it('disarms synchronously on a reload or a recheck, so a callback retained from an armed render cannot bind', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current.bind

    act(() => {
      mounted.result.current.reload()
      retained()
    })
    expect(mocks.bind).not.toHaveBeenCalled()
    await act(async () => undefined)

    // The settled read arms a NEW render's callback again, but the one retained from before the reload stays dead.
    expect(mounted.result.current.canBind).toBe(true)
    act(() => retained())
    expect(mocks.bind).not.toHaveBeenCalled()

    await act(async () => mounted.result.current.check())
    const second = mounted.result.current.bind
    act(() => {
      mounted.result.current.check()
      second()
    })
    expect(mocks.bind).not.toHaveBeenCalled()
  })

  it('never submits a candidate captured before a recheck changed it, even for a callback retained from the earlier render', async () => {
    let node = 'R_first'
    const mocks = install({ observe: (runId) => Promise.resolve(observed(scopeOf(runId), node)) })
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current.bind
    node = 'R_second'

    await act(async () => mounted.result.current.check())
    act(() => retained())
    expect(mocks.bind).not.toHaveBeenCalled()

    await act(async () => mounted.result.current.bind())
    expect(mocks.bind).toHaveBeenCalledWith('run-a', expect.objectContaining({ repositoryNodeId: 'R_second' }))
  })

  it('ignores the late answer of a superseded check and a superseded read', async () => {
    const olds = [deferred<GetGitHubRepositoryObservationResponse>(), deferred<GetGitHubRepositoryObservationResponse>()]
    let calls = 0
    const mocks = install({ observe: () => olds[calls++].promise })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())
    act(() => mounted.result.current.check())
    // A second check while one is in flight is refused, so the first answer is the only one that can settle.
    act(() => mounted.result.current.check())
    expect(mocks.observe).toHaveBeenCalledTimes(1)

    await act(async () => olds[0].resolve(observed(A, 'R_late')))
    expect(mounted.result.current.candidateIdentity).toMatchObject({ repositoryNodeId: 'R_late' })
  })

  it('ignores the late answers of a closed opening and starts blank when reopened', async () => {
    const late = deferred<GetGitHubRepositoryObservationResponse>()
    const mocks = install({ observe: () => late.promise })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())
    act(() => mounted.result.current.check())
    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())

    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, candidate: { kind: 'idle' } })
    await act(async () => late.resolve(observed(A)))

    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canBind).toBe(false)
    expect(mocks.observe).toHaveBeenCalledTimes(1)
  })

  it.each(['A-B', 'A-B-A', 'close-reopen'])('%s: nothing of an earlier lifetime can bind or settle into the successor', async (flow) => {
    const old = deferred<GetGitHubRepositoryObservationResponse>()
    let calls = 0
    const mocks = install({
      observe: (runId) => (++calls === 1 ? old.promise : Promise.resolve(observed(scopeOf(runId), 'R_successor'))),
    })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())
    act(() => mounted.result.current.check())
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
    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, candidate: { kind: 'idle' } })
    await act(async () => old.resolve(observed(A, 'R_old')))
    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })

    act(() => retained.bind())
    act(() => retained.reload())
    expect(mocks.bind).not.toHaveBeenCalled()
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it('refuses a bind after a close in the same batch and a bind from a callback retained from an ended opening', async () => {
    const mocks = install()
    const mounted = mountFor(A)
    await armed(mounted)
    const retained = mounted.result.current

    act(() => {
      mounted.result.current.close()
      mounted.result.current.bind()
    })
    expect(mocks.bind).not.toHaveBeenCalled()

    act(() => mounted.result.current.show())
    act(() => retained.bind())
    expect(mocks.bind).not.toHaveBeenCalled()
  })

  it('treats a change of the recorded target as another scope', () => {
    install()
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    mounted.rerender({ scope: scopeOf('run-a', 'someone-else') })

    expect(mounted.result.current.open).toBe(false)
  })

  // Every way a write can end without a coherent recorded binding.
  const FAILED_WRITES: [string, () => Partial<Parameters<typeof install>[0]>][] = [
    ['a definite refusal', () => ({ bind: () => Promise.reject(new ApiException('conflict', 409, '', {}, null)) })],
    ['a server fault', () => ({ bind: () => Promise.reject(new ApiException('boom', 500, '', {}, null)) })],
    ['a network failure', () => ({ bind: () => Promise.reject(new Error('network')) })],
    ['an incoherent success', () => ({ bind: (runId: string) => Promise.resolve(posted(scopeOf(runId), 'R_other')) })],
  ]

  it.each(FAILED_WRITES)('%s: recovery is reload, then a new settled check, then bind, and each step stays disarmed until its predecessor', async (_name, overrides) => {
    const mocks = install(overrides())
    const mounted = mountFor(A)
    await armed(mounted)

    await act(async () => mounted.result.current.bind())
    expect(mounted.result.current.write.kind).toBe('failed')
    expect(mounted.result.current.canBind).toBe(false)
    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })
    act(() => mounted.result.current.bind())
    expect(mocks.bind).toHaveBeenCalledTimes(1)

    // Reload alone is not enough: the old candidate is gone and a new settled check is required.
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canBind).toBe(false)
    act(() => mounted.result.current.bind())
    expect(mocks.bind).toHaveBeenCalledTimes(1)

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.canBind).toBe(true)
    await act(async () => mounted.result.current.bind())
    expect(mocks.bind).toHaveBeenCalledTimes(2)
  })

  it.each(FAILED_WRITES)('%s: the earlier not-bound read is cleared, the outcome stays, and nothing is requested by itself', async (_name, overrides) => {
    vi.useFakeTimers()
    const mocks = install(overrides())
    const mounted = mountFor(A)
    await armed(mounted)
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'NotRecorded' } })

    await act(async () => mounted.result.current.bind())

    expect(mounted.result.current.write.kind).toBe('failed')
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })
    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canBind).toBe(false)
    await act(async () => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mocks.observe).toHaveBeenCalledTimes(1)
    expect(mocks.bind).toHaveBeenCalledTimes(1)

    // Only an explicit reload supplies the next authoritative reading.
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'NotRecorded' } })
    expect(mocks.read).toHaveBeenCalledTimes(2)
  })

  it('a coherent successful write still shows its Available binding', async () => {
    install()
    const mounted = mountFor(A)
    await armed(mounted)

    await act(async () => mounted.result.current.bind())

    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available', binding: { repositoryNodeId: 'R_one' } } })
  })

  it.each(FAILED_WRITES)('%s: a check made before the recovery reload cannot satisfy the new-check requirement', async (_name, overrides) => {
    const mocks = install(overrides())
    const mounted = mountFor(A)
    await armed(mounted)
    await act(async () => mounted.result.current.bind())
    const retainedBeforeRecovery = mounted.result.current.bind

    // Reversed order: check, then reload.
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.canBind).toBe(false)
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.candidate).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canBind).toBe(false)
    act(() => mounted.result.current.bind())
    act(() => retainedBeforeRecovery())
    expect(mocks.bind).toHaveBeenCalledTimes(1)

    // The ordered recovery then works.
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.canBind).toBe(true)
  })

  it('refuses a bind from a callback retained from before the recovery, and from the same batch as the recovery actions', async () => {
    const mocks = install({ bind: () => Promise.reject(new Error('lost response')) })
    const mounted = mountFor(A)
    await armed(mounted)
    await act(async () => mounted.result.current.bind())
    await act(async () => mounted.result.current.reload())
    await act(async () => mounted.result.current.check())
    const armedCallback = mounted.result.current.bind
    expect(mounted.result.current.canBind).toBe(true)

    // A recovery reload in the same batch disarms the callback synchronously.
    act(() => {
      mounted.result.current.reload()
      armedCallback()
    })
    expect(mocks.bind).toHaveBeenCalledTimes(1)
  })

  it('does not recover while the recovery reload itself fails: the write stays failed and nothing is armed', async () => {
    const mocks = install({ bind: () => Promise.reject(new Error('lost response')) })
    const mounted = mountFor(A)
    await armed(mounted)
    await act(async () => mounted.result.current.bind())

    mocks.read.mockImplementation(() => Promise.reject(new Error('network')))
    await act(async () => mounted.result.current.reload())
    await act(async () => mounted.result.current.check())

    expect(mounted.result.current.write.kind).toBe('failed')
    expect(mounted.result.current.canBind).toBe(false)
    act(() => mounted.result.current.bind())
    expect(mocks.bind).toHaveBeenCalledTimes(1)
  })

  it('treats an answer that is not exactly what was submitted as an unknown outcome', async () => {
    const mocks = install({ bind: (runId) => Promise.resolve(posted(scopeOf(runId), 'R_other')) })
    const mounted = mountFor(A)
    await armed(mounted)

    await act(async () => mounted.result.current.bind())

    expect(mounted.result.current.write).toEqual({ kind: 'failed', reason: 'ambiguous' })
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })
    expect(mocks.bind).toHaveBeenCalledTimes(1)
  })

  it('reports a failed read and an incoherent answer, never a partial one, and does not retry', async () => {
    const mocks = install({ read: () => Promise.reject(new Error('network')) })
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'read' })

    mocks.read.mockImplementation(() => Promise.resolve({ ...notBound(A), runId: 'someone-else' }))
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'invalid' })

    mocks.observe.mockImplementation(() => Promise.resolve({ ...observed(A), runId: 'someone-else' }))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.candidate).toEqual({ kind: 'failed', reason: 'invalid' })
    expect(mocks.read).toHaveBeenCalledTimes(2)
    expect(mocks.observe).toHaveBeenCalledTimes(1)
  })

  it('ignores every answer after the component unmounts', async () => {
    const late = deferred<GetGitHubRepositoryIdentityBindingResponse>()
    const mocks = install({ read: () => late.promise })
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())

    mounted.unmount()
    late.resolve(notBound(A))
    await late.promise
    await Promise.resolve()

    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('never stores or reads anything in the browser', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    const getItem = vi.spyOn(Storage.prototype, 'getItem')
    install()
    const mounted = mountFor(A)
    await armed(mounted)
    await act(async () => mounted.result.current.bind())

    expect(setItem).not.toHaveBeenCalled()
    expect(getItem).not.toHaveBeenCalled()
    setItem.mockRestore()
    getItem.mockRestore()
  })
})
