import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubBranchObservationClient } from '../../../api/clients'
import type { GetGitHubBranchObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import type { BranchObservationScope } from '../gitHubBranchObservation'
import { useGitHubBranchObservation } from './useGitHubBranchObservation'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubBranchObservationClient: vi.fn(),
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
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')

const scopeOf = (runId: string, owner = 'octo-cat'): BranchObservationScope => {
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

const wireSource = (scope: BranchObservationScope) => ({
  operationId: scope.source.operationId,
  commitSha: scope.source.commitSha,
  branchName: scope.source.branchName,
})
const wireTarget = (scope: BranchObservationScope) => ({ ...scope.target })
const matchedFor = (scope: BranchObservationScope, repositoryNodeId = 'R_kgDOExample01') =>
  ({
    runId: scope.source.runId,
    status: 'CommitMatched',
    source: wireSource(scope),
    target: wireTarget(scope),
    observation: {
      repositoryNodeId,
      owner: scope.target.owner,
      repository: scope.target.repository,
      observedAtUtc: OBSERVED,
      ref: { branchName: scope.source.branchName, commitSha: scope.source.commitSha },
    },
  }) as unknown as GetGitHubBranchObservationResponse
const statusFor = (scope: BranchObservationScope, status: 'CliUnavailable' | 'Unknown') =>
  ({ runId: scope.source.runId, status, source: wireSource(scope), target: wireTarget(scope) }) as unknown as GetGitHubBranchObservationResponse

function install(read: (runId: string) => Promise<GetGitHubBranchObservationResponse>) {
  const getGitHubBranchObservation = vi.fn(read)
  vi.mocked(gitHubBranchObservationClient).mockReturnValue(
    { getGitHubBranchObservation } as unknown as ReturnType<typeof gitHubBranchObservationClient>,
  )
  return getGitHubBranchObservation
}

const mountFor = (initial: BranchObservationScope | null) =>
  renderHook(({ scope }: { scope: BranchObservationScope | null }) => useGitHubBranchObservation(scope), {
    initialProps: { scope: initial },
  })

describe('useGitHubBranchObservation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing on mount, opening, rerender, a scope change, a timer or a reconnect-like remount', () => {
    vi.useFakeTimers()
    const get = install((id) => Promise.resolve(matchedFor(scopeOf(id))))
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
    const get = install((id) => Promise.resolve(matchedFor(scopeOf(id))))
    const mounted = mountFor(null)

    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    expect(get).not.toHaveBeenCalled()
  })

  it('does nothing when asked to check while collapsed', () => {
    const get = install((id) => Promise.resolve(matchedFor(scopeOf(id))))
    const mounted = mountFor(A)

    act(() => mounted.result.current.check())

    expect(get).not.toHaveBeenCalled()
  })

  it('reads the exact run once per explicit check, hides any answer while pending and ignores a second activation in flight', async () => {
    const first = deferred<GetGitHubBranchObservationResponse>()
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

    await act(async () => first.resolve(matchedFor(A)))

    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'CommitMatched' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows the CLI-unavailable and unknown statuses and an explicit recheck is one more read that hides the earlier answer', async () => {
    const answers = [statusFor(A, 'CliUnavailable'), statusFor(A, 'Unknown'), matchedFor(A)]
    const second = deferred<GetGitHubBranchObservationResponse>()
    const get = install(() => (answers.length > 1 ? Promise.resolve(answers.shift()!) : second.promise))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'result', reading: { status: 'CliUnavailable' } })
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'result', reading: { status: 'Unknown' } })
    act(() => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(matchedFor(A)))

    expect(get).toHaveBeenCalledTimes(3)
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'CommitMatched' } })
  })

  it('reports a failed read and an incoherent answer, never a partial one, and does not retry', async () => {
    const get = install(() => Promise.reject(new Error('network')))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'read' })

    get.mockImplementation(() => Promise.resolve({ ...matchedFor(A), runId: 'someone-else' } as GetGitHubBranchObservationResponse))
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockImplementation(() =>
      Promise.resolve(matchedFor({ ...A, target: { ...A.target, owner: 'someone-else' } }) as GetGitHubBranchObservationResponse),
    )
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('shows the three ref-bearing statuses exactly and refuses a status that contradicts the commit comparison', async () => {
    const withRef = (status: string, ref: unknown) => {
      const base = matchedFor(A) as unknown as { observation: Record<string, unknown> }
      return { ...base, status, observation: { ...base.observation, ref } } as unknown as GetGitHubBranchObservationResponse
    }
    const own = { branchName: A.source.branchName, commitSha: A.source.commitSha }
    const answers = [
      withRef('CommitDifferent', { ...own, commitSha: 'b'.repeat(40) }),
      withRef('RefNotReturned', null),
      withRef('CommitMatched', { ...own, commitSha: 'b'.repeat(40) }),
    ]
    install(() => Promise.resolve(answers.shift()!))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toMatchObject({
      kind: 'result',
      reading: { status: 'CommitDifferent', ref: { commitSha: 'b'.repeat(40) } },
    })
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toMatchObject({ kind: 'result', reading: { status: 'RefNotReturned' } })
    await act(async () => mounted.result.current.check())
    expect(mounted.result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
  })

  it('ignores the late answer of a closed opening and starts unchecked when reopened', async () => {
    const late = deferred<GetGitHubBranchObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())
    act(() => mounted.result.current.close())

    act(() => mounted.result.current.show())
    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    await act(async () => late.resolve(matchedFor(A)))

    expect(mounted.result.current.phase).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('ignores a late answer after the scope changed or returned (A to B to A) and starts collapsed each time', async () => {
    const late = deferred<GetGitHubBranchObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.rerender({ scope: B })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    mounted.rerender({ scope: A })
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
    act(() => mounted.result.current.show())
    await act(async () => late.resolve(matchedFor(A)))

    expect(mounted.result.current).toMatchObject({ open: true, phase: { kind: 'idle' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('treats a change of the recorded target as another scope', () => {
    install((id) => Promise.resolve(matchedFor(scopeOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    mounted.rerender({ scope: scopeOf('run-a', 'someone-else') })

    expect(mounted.result.current.open).toBe(false)
  })

  it('refuses a check after a close in the same batch and a check from a callback retained from an ended opening', async () => {
    const get = install((id) => Promise.resolve(matchedFor(scopeOf(id))))
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
    install((id) => Promise.resolve(matchedFor(scopeOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    const staleClose = mounted.result.current.close
    act(() => mounted.result.current.close())
    act(() => mounted.result.current.show())

    act(() => staleClose())

    expect(mounted.result.current.open).toBe(true)
  })

  it('sends a check that precedes a close in the same batch but ignores its answer', async () => {
    const late = deferred<GetGitHubBranchObservationResponse>()
    const get = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => {
      mounted.result.current.check()
      mounted.result.current.close()
    })
    await act(async () => late.resolve(matchedFor(A)))

    expect(get).toHaveBeenCalledTimes(1)
    expect(mounted.result.current).toMatchObject({ open: false, phase: { kind: 'idle' } })
  })

  it('ignores the answer after the component unmounts', async () => {
    const late = deferred<GetGitHubBranchObservationResponse>()
    const get = install(() => late.promise)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.check())

    mounted.unmount()
    late.resolve(matchedFor(A))
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
      const old = deferred<GetGitHubBranchObservationResponse>()
      const successor = deferred<GetGitHubBranchObservationResponse>()
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

      await act(async () => old.resolve(matchedFor(A, 'R_old')))
      expect(mounted.result.current.phase).toEqual({ kind: 'pending' })
      act(() => mounted.result.current.check())
      expect(get).toHaveBeenCalledTimes(2)

      await act(async () => successor.resolve(matchedFor(flow === 'A-B' ? B : A, 'R_successor')))
      expect(get).toHaveBeenCalledTimes(2)
      expect(mounted.result.current.phase).toMatchObject({
        kind: 'result',
        reading: { status: 'CommitMatched', facts: { repositoryNodeId: 'R_successor' } },
      })
    },
  )
})
