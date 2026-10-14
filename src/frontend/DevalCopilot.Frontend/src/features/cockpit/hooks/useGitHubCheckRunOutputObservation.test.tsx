import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubCheckRunOutputObservationClient } from '../../../api/clients'
import type { GetGitHubCheckRunOutputObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import type { CheckRunOutputScope } from '../gitHubCheckRunOutputObservation'
import { useGitHubCheckRunOutputObservation } from './useGitHubCheckRunOutputObservation'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubCheckRunOutputObservationClient: vi.fn(),
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

const scopeOf = (runId: string, checkRunId = 'CR_1', owner = 'octo-cat'): CheckRunOutputScope => {
  const source: ObservationSource = { runId, operationId: 'op-' + runId, commitSha: SHA, branchName: 'devalcopilot/workspace/x/1' }
  const target: ObservationScope = {
    id: 'target-' + runId,
    eventId: 'event-' + runId,
    host: 'github.com',
    owner,
    repository: 'hello.world',
    recordedAtUtc: AT,
  }
  return { source, target, checkRunId }
}
const A = scopeOf('run-a')
const B = scopeOf('run-b')
const A1 = scopeOf('run-a', 'CR_1')
const A2 = scopeOf('run-a', 'CR_2')

const wireSource = (scope: CheckRunOutputScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: CheckRunOutputScope) => ({ ...scope.target })
const wireBinding = (scope: CheckRunOutputScope) => ({
  id: 'binding-' + scope.source.runId,
  eventId: 'binding-event-' + scope.source.runId,
  repositoryNodeId: NODE_ID,
  owner: scope.target.owner,
  repository: scope.target.repository,
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
})
const reportedFor = (scope: CheckRunOutputScope, text = 'output text', repositoryNodeId = NODE_ID) =>
  ({
    runId: scope.source.runId,
    checkRunId: scope.checkRunId,
    status: 'OutputReported',
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
    observation: {
      repositoryNodeId,
      owner: scope.target.owner,
      repository: scope.target.repository,
      observedAtUtc: OBSERVED,
      ref: { branchName: scope.source.branchName, commitSha: scope.source.commitSha },
      checkRun: {
        id: scope.checkRunId,
        checkSuiteId: 'CS_1',
        commitSha: scope.source.commitSha,
        name: 'build',
        status: 'COMPLETED',
        conclusion: 'FAILURE',
        output: { title: null, summary: null, text },
      },
    },
  }) as unknown as GetGitHubCheckRunOutputObservationResponse
const statusFor = (scope: CheckRunOutputScope, status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged') =>
  ({
    runId: scope.source.runId,
    checkRunId: scope.checkRunId,
    status,
    source: wireSource(scope),
    target: wireTarget(scope),
    binding: wireBinding(scope),
  }) as unknown as GetGitHubCheckRunOutputObservationResponse

function install(read: (runId: string, checkRunId: string) => Promise<GetGitHubCheckRunOutputObservationResponse>) {
  const getGitHubCheckRunOutputObservation = vi.fn(read)
  vi.mocked(gitHubCheckRunOutputObservationClient).mockReturnValue(
    { getGitHubCheckRunOutputObservation } as unknown as ReturnType<typeof gitHubCheckRunOutputObservationClient>,
  )
  return getGitHubCheckRunOutputObservation
}

/** The answer a host would give for exactly the request the hook sent, keyed by the run and the check run identifier. */
const answering = (runId: string, checkRunId: string) => reportedFor(scopeOf(runId, checkRunId), `text of ${runId}/${checkRunId}`)

const PARENT_1 = { reading: 1 }
const PARENT_2 = { reading: 2 }

interface Props {
  scope: CheckRunOutputScope | null
  parent: object | null
}

const mountFor = (scope: CheckRunOutputScope | null, parent: object | null = PARENT_1) =>
  renderHook(({ scope: current, parent: owner }: Props) => useGitHubCheckRunOutputObservation(current, owner), {
    initialProps: { scope, parent },
  })

const textOf = (phase: ReturnType<typeof useGitHubCheckRunOutputObservation>['phase']) =>
  phase.kind === 'result' && phase.reading.status === 'OutputReported' ? phase.reading.output.text : undefined

describe('useGitHubCheckRunOutputObservation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing on mount, opening, rerender, a scope or check change, a parent change, a timer or a reconnect-like remount', () => {
    vi.useFakeTimers()
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)

    act(() => mounted.result.current.show())
    mounted.rerender({ scope: A, parent: PARENT_1 })
    mounted.rerender({ scope: B, parent: PARENT_1 })
    mounted.rerender({ scope: A2, parent: PARENT_1 })
    mounted.rerender({ scope: A, parent: PARENT_2 })
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    mounted.unmount()
    const remounted = mountFor(A)
    act(() => remounted.result.current.show())

    expect(get).not.toHaveBeenCalled()
  })

  it('is collapsed and idle for a newly committed scope and does nothing without a scope or a parent', () => {
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const withoutScope = mountFor(null)
    const withoutParent = mountFor(A, null)

    for (const mounted of [withoutScope, withoutParent]) {
      act(() => mounted.result.current.show())
      act(() => mounted.result.current.check())
      expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    }

    expect(get).not.toHaveBeenCalled()
  })

  it('does nothing when asked to check while collapsed', () => {
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)

    act(() => mounted.result.current.check())

    expect(mounted.result.current.open).toBe(false)
    expect(get).not.toHaveBeenCalled()
  })

  it('opens without a request and sends exactly one request for exactly the run and the selected check on the explicit check', async () => {
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)

    act(() => mounted.result.current.show())
    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).not.toHaveBeenCalled()

    await act(async () => {
      mounted.result.current.check()
    })

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a', 'CR_1')
    expect(textOf(mounted.result.current.phase)).toBe('text of run-a/CR_1')
  })

  it('is single flight: a second activation while one is pending and two in the same batch send one request', async () => {
    const pending = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const get = install(() => pending.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.check()
      mounted.result.current.check()
    })
    act(() => mounted.result.current.check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })

    await act(async () => pending.resolve(reportedFor(A)))
    expect(mounted.result.current.phase.kind).toBe('result')
  })

  it('sends nothing for an activation after close in the same batch', () => {
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.close()
      mounted.result.current.check()
    })

    expect(get).not.toHaveBeenCalled()
    expect(mounted.result.current.open).toBe(false)
  })

  it('can recheck after an answer and then asks the host again, never reusing the earlier answer', async () => {
    let calls = 0
    const get = install(async (runId, checkRunId) => reportedFor(scopeOf(runId, checkRunId), `answer ${++calls}`))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(textOf(mounted.result.current.phase)).toBe('answer 1')
    await act(async () => mounted.result.current.check())

    expect(get).toHaveBeenCalledTimes(2)
    expect(textOf(mounted.result.current.phase)).toBe('answer 2')
  })

  it('removes an earlier answer while a recheck is pending and after it failed', async () => {
    const second = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const answers = [Promise.resolve(reportedFor(A)), second.promise]
    install(() => answers.shift()!)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase.kind).toBe('result')

    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.reject(new Error('network')))

    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })
  })

  it('reports a response that is not coherent for exactly this source, target and check as invalid and shows none of it', async () => {
    const incoherent = [
      reportedFor(scopeOf('run-other', 'CR_1')),
      reportedFor(A, 'text', 'R_otherBinding'),
      reportedFor(scopeOf('run-a', 'CR_other')),
      { ...reportedFor(A), status: 'Passed' } as unknown as GetGitHubCheckRunOutputObservationResponse,
    ]
    for (const response of incoherent) {
      install(async () => response)
      const mounted = mountFor(A)
      act(() => mounted.result.current.show())

      await act(async () => mounted.result.current.check())

      expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
    }
  })

  it('accepts the bound-identity statuses that carry no output', async () => {
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged'] as const) {
      install(async () => statusFor(A, status))
      const mounted = mountFor(A)
      act(() => mounted.result.current.show())

      await act(async () => mounted.result.current.check())

      expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status } })
    }
  })

  it('ignores a late answer after close, discards it for a later opening and starts the next opening unchecked', async () => {
    const pending = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const get = install(() => pending.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())
    await act(async () => pending.resolve(reportedFor(A, 'late')))

    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('a callback retained from an ended opening starts and closes nothing of its successor', async () => {
    const get = install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    const retained = mounted.result.current
    act(() => retained.close())
    act(() => mounted.result.current.show())

    act(() => retained.check())
    act(() => retained.close())

    expect(get).not.toHaveBeenCalled()
    expect(mounted.result.current.open).toBe(true)
  })

  it('ends ownership when the scope changes and ignores the old answer even after a return to it (A-B-A)', async () => {
    const first = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const answers = [first.promise]
    const get = install((runId, checkRunId) => answers.shift() ?? Promise.resolve(answering(runId, checkRunId)))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: B, parent: PARENT_1 })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    mounted.rerender({ scope: A, parent: PARENT_1 })
    act(() => mounted.result.current.show())
    await act(async () => first.resolve(reportedFor(A, 'stale first A')))

    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('ignores a late answer of another selected check, including after A-B-A selection of the check', async () => {
    const first = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const answers = [first.promise]
    const get = install((runId, checkRunId) => answers.shift() ?? Promise.resolve(answering(runId, checkRunId)))
    const mounted = mountFor(A1)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: A2, parent: PARENT_1 })
    mounted.rerender({ scope: A1, parent: PARENT_1 })
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())
    await act(async () => first.resolve(reportedFor(A1, 'stale obsolete callback')))

    expect(get).toHaveBeenCalledTimes(2)
    // The obsolete answer never settles the current lifetime: only the current request's own answer does.
    expect(textOf(mounted.result.current.phase)).toBe('text of run-a/CR_1')
    expect(textOf(mounted.result.current.phase)).not.toBe('stale obsolete callback')
  })

  it('a late completion of an ended selection never clears the pending marker of the current one, so no duplicate request follows', async () => {
    const first = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const second = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const answers = [first.promise, second.promise]
    const get = install(() => answers.shift() ?? Promise.reject(new Error('a third request must never be sent')))
    const mounted = mountFor(A1)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: A2, parent: PARENT_1 })
    mounted.rerender({ scope: A1, parent: PARENT_1 })
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())
    expect(get).toHaveBeenCalledTimes(2)

    // The obsolete callback of the first selection settles while the current one is still pending.
    await act(async () => first.resolve(reportedFor(A1, 'obsolete')))
    act(() => mounted.result.current.check())
    act(() => mounted.result.current.check())

    expect(get).toHaveBeenCalledTimes(2)
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(reportedFor(A1, 'current')))
    expect(textOf(mounted.result.current.phase)).toBe('current')
  })

  it('ends ownership when the parent reading changes, collapses and discards a late answer', async () => {
    const pending = deferred<GetGitHubCheckRunOutputObservationResponse>()
    const get = install(() => pending.promise)
    const mounted = mountFor(A, PARENT_1)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: A, parent: PARENT_2 })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    mounted.rerender({ scope: A, parent: PARENT_1 })
    act(() => mounted.result.current.show())
    await act(async () => pending.resolve(reportedFor(A, 'late')))

    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('keeps its state across a rerender with the same scope and the same parent', async () => {
    install(async (runId, checkRunId) => answering(runId, checkRunId))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.check())

    mounted.rerender({ scope: scopeOf('run-a', 'CR_1'), parent: PARENT_1 })

    expect(mounted.result.current.open).toBe(true)
    expect(textOf(mounted.result.current.phase)).toBe('text of run-a/CR_1')
  })

  it('discards a late answer after unmount without a fault', async () => {
    const pending = deferred<GetGitHubCheckRunOutputObservationResponse>()
    install(() => pending.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.unmount()

    await expect(Promise.resolve().then(() => pending.resolve(reportedFor(A)))).resolves.toBeUndefined()
  })

  it('does not poll: no timer, retry or background read follows a failure or an answer', async () => {
    vi.useFakeTimers()
    const get = install(() => Promise.reject(new Error('network')))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.check())

    await act(async () => {
      vi.advanceTimersByTime(60 * 60 * 1000)
    })

    expect(get).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })
  })
})
