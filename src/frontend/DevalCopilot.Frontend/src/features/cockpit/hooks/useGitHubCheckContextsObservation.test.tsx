import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubCheckContextsObservationClient } from '../../../api/clients'
import type { GetGitHubCheckContextsObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import type { CheckContextsScope } from '../gitHubCheckContextsObservation'
import { useGitHubCheckContextsObservation } from './useGitHubCheckContextsObservation'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubCheckContextsObservationClient: vi.fn(),
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

const scopeOf = (runId: string, owner = 'octo-cat'): CheckContextsScope => {
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

const wireSource = (scope: CheckContextsScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: CheckContextsScope) => ({ ...scope.target })
const wireBinding = (scope: CheckContextsScope) => ({
  id: 'binding-' + scope.source.runId,
  eventId: 'binding-event-' + scope.source.runId,
  repositoryNodeId: NODE_ID,
  owner: scope.target.owner,
  repository: scope.target.repository,
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
})
const reportedFor = (scope: CheckContextsScope, label = 'build', repositoryNodeId = NODE_ID) =>
  ({
    runId: scope.source.runId,
    status: 'ContextsReported',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
    observation: {
      repositoryNodeId,
      owner: scope.target.owner,
      repository: scope.target.repository,
      observedAtUtc: OBSERVED,
      ref: { branchName: scope.source.branchName, commitSha: scope.source.commitSha },
      totalCount: 1,
      contexts: [{ kind: 'CheckRun', id: 'CR_1', name: label, status: 'COMPLETED', conclusion: 'SUCCESS', checkSuiteId: 'CS_1' }],
    },
  }) as unknown as GetGitHubCheckContextsObservationResponse
const statusFor = (scope: CheckContextsScope, status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged') =>
  ({
    runId: scope.source.runId,
    status,
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
  }) as unknown as GetGitHubCheckContextsObservationResponse

function install(read: (runId: string) => Promise<GetGitHubCheckContextsObservationResponse>) {
  const getGitHubCheckContextsObservation = vi.fn(read)
  vi.mocked(gitHubCheckContextsObservationClient).mockReturnValue(
    { getGitHubCheckContextsObservation } as unknown as ReturnType<typeof gitHubCheckContextsObservationClient>,
  )
  return getGitHubCheckContextsObservation
}

const mountFor = (initial: CheckContextsScope | null) =>
  renderHook(({ scope }: { scope: CheckContextsScope | null }) => useGitHubCheckContextsObservation(scope), {
    initialProps: { scope: initial },
  })

describe('useGitHubCheckContextsObservation', () => {
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
    const first = deferred<GetGitHubCheckContextsObservationResponse>()
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

    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'ContextsReported', contexts: [{ name: 'build' }] } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows the CLI-unavailable, unknown and identity-changed statuses with the binding and a recheck hides the earlier answer', async () => {
    const answers = [statusFor(A, 'CliUnavailable'), statusFor(A, 'Unknown'), statusFor(A, 'IdentityChanged'), reportedFor(A, 'failing-job')]
    const last = deferred<GetGitHubCheckContextsObservationResponse>()
    const get = install(() => (answers.length > 1 ? Promise.resolve(answers.shift()!) : last.promise))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged']) {
      await act(async () => mounted.result.current.check())
      expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status, binding: { repositoryNodeId: NODE_ID } } })
    }

    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => last.resolve(reportedFor(A, 'failing-job')))

    expect(get).toHaveBeenCalledTimes(4)
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'ContextsReported', contexts: [{ name: 'failing-job' }] } })
  })

  it('removes the previous observation while pending and after a failure, and does not retry', async () => {
    const second = deferred<GetGitHubCheckContextsObservationResponse>()
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.resolve(reportedFor(A, 'build')) : calls === 2 ? second.promise : Promise.reject(new Error('x'))))
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

    get.mockImplementation(() => Promise.resolve({ ...reportedFor(A), runId: 'someone-else' } as GetGitHubCheckContextsObservationResponse))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockImplementation(() => Promise.resolve(reportedFor(A, 'build', 'R_other')))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockImplementation(() => Promise.resolve(reportedFor(A, 'bad' + String.fromCharCode(10) + 'label')))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
    expect(get).toHaveBeenCalledTimes(4)
  })

  it('ignores the late answer of a closed opening and starts unchecked when reopened', async () => {
    const late = deferred<GetGitHubCheckContextsObservationResponse>()
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
    const late = deferred<GetGitHubCheckContextsObservationResponse>()
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
    const late = deferred<GetGitHubCheckContextsObservationResponse>()
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
    const late = deferred<GetGitHubCheckContextsObservationResponse>()
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

  // The settling of an ended read must not release the in-flight guard of its successor, and the successor must still accept its own answer.
  it.each(['A-B', 'close-reopen', 'A-B-A'])(
    '%s: an old read settling keeps the successor check in flight and the successor answer is accepted',
    async (flow) => {
      const old = deferred<GetGitHubCheckContextsObservationResponse>()
      const successor = deferred<GetGitHubCheckContextsObservationResponse>()
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

      await act(async () => old.resolve(reportedFor(A, 'failing-job')))
      expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
      act(() => mounted.result.current.check())
      expect(get).toHaveBeenCalledTimes(2)

      await act(async () => successor.resolve(reportedFor(flow === 'A-B' ? B : A, 'pending-job')))
      expect(get).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.phase).toMatchObject({
        kind: 'result',
        reading: { status: 'ContextsReported', contexts: [{ name: 'pending-job' }] },
      })
    },
  )
})
