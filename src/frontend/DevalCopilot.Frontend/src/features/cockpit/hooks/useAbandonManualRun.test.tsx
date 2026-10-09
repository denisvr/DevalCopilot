import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { abandonManualRunClient } from '../../../api/clients'
import { AbandonManualRunResponse, ApiException } from '../../../api/generated/api-client'
import { ABANDON_UNKNOWN_OUTCOME_MESSAGE, abandonIdentityKey } from '../describeRunAbandonment'
import { useAbandonManualRun } from './useAbandonManualRun'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  abandonManualRunClient: vi.fn(),
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

const problem = (status: number, code?: string) =>
  new ApiException('raw server text that must never be shown', status, JSON.stringify({ errors: [{ code, detail: 'server detail' }] }), {}, null)

function install(post: () => Promise<unknown>) {
  const abandonManualRun = vi.fn(post)
  vi.mocked(abandonManualRunClient).mockReturnValue({ abandonManualRun } as unknown as ReturnType<typeof abandonManualRunClient>)
  return abandonManualRun
}

interface Props {
  runId: string
  identityKey?: string
  draftVersion?: number
}

function mounted(
  refreshStatus = vi.fn(),
  refreshProject: (() => void) | undefined = vi.fn(),
  onSaved: (() => unknown) | undefined = vi.fn(async () => true),
) {
  const rendered = renderHook(
    (props: Props) =>
      useAbandonManualRun(
        props.runId,
        props.identityKey ?? abandonIdentityKey(props.runId),
        props.draftVersion ?? 0,
        refreshStatus,
        refreshProject,
        onSaved,
      ),
    { initialProps: { runId: 'run-1' } as Props },
  )
  return { ...rendered, refreshStatus, refreshProject, onSaved }
}

const submit = (result: { current: ReturnType<typeof useAbandonManualRun> }, runId = 'run-1', reason = 'Superseded') =>
  result.current.submit(runId, reason)

describe('useAbandonManualRun', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('posts only the reason once and refreshes the status, the project list and the cockpit on acceptance', async () => {
    const post = install(() => Promise.resolve(new AbandonManualRunResponse({ runId: 'run-1' })))
    const { result, refreshStatus, refreshProject, onSaved } = mounted()

    let accepted = false
    await act(async () => {
      accepted = await submit(result)
    })

    expect(accepted).toBe(true)
    expect(post).toHaveBeenCalledTimes(1)
    const [runId, body] = post.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(runId).toBe('run-1')
    expect(Object.keys(body)).toEqual(['reason'])
    expect(body.reason).toBe('Superseded')
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(refreshProject).toHaveBeenCalledTimes(1)
    expect(onSaved).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBeNull()
  })

  it('does not report a recorded abandonment as failed when a follow-up refresh rejects or throws', async () => {
    install(() => Promise.resolve(new AbandonManualRunResponse({})))
    const rejecting = mounted(vi.fn(), vi.fn(), () => Promise.reject(new Error('refresh failed')))
    let accepted = false
    await act(async () => {
      accepted = await submit(rejecting.result)
    })
    expect(accepted).toBe(true)
    expect(rejecting.result.current.error).toBeNull()

    const throwingCockpit = mounted(vi.fn(), vi.fn(), () => {
      throw new Error('sync throw')
    })
    await act(async () => {
      accepted = await submit(throwingCockpit.result)
    })
    expect(accepted).toBe(true)
    expect(throwingCockpit.result.current.error).toBeNull()

    const throwingProject = mounted(vi.fn(), () => {
      throw new Error('project refresh throw')
    })
    await act(async () => {
      accepted = await submit(throwingProject.result)
    })
    expect(accepted).toBe(true)
    expect(throwingProject.result.current.error).toBeNull()
  })

  it('shows fixed copy for a definite refusal, never the server text, and reads nothing further', async () => {
    install(() => Promise.reject(problem(409, 'run_abandonment.active_attempt')))
    const { result, refreshStatus, refreshProject, onSaved } = mounted()

    await act(async () => {
      await submit(result)
    })

    expect(result.current.error).toContain('still active')
    expect(result.current.error).not.toContain('server')
    expect(refreshStatus).not.toHaveBeenCalled()
    expect(refreshProject).not.toHaveBeenCalled()
    expect(onSaved).not.toHaveBeenCalled()
  })

  it.each([
    ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['a 5xx response', () => Promise.reject(problem(503))],
  ])('treats %s as an unknown outcome: one POST, a read-only status reconciliation and no resubmission', async (_name, failure) => {
    const post = install(failure)
    const { result, refreshStatus, refreshProject, onSaved } = mounted()

    let accepted = true
    await act(async () => {
      accepted = await submit(result)
    })

    expect(accepted).toBe(false)
    expect(post).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBe(ABANDON_UNKNOWN_OUTCOME_MESSAGE)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(refreshProject).not.toHaveBeenCalled()
    expect(onSaved).not.toHaveBeenCalled()
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(post).toHaveBeenCalledTimes(1)
  })

  it('ignores a duplicate submission while one is in flight', async () => {
    const pending = deferred<unknown>()
    const post = install(() => pending.promise)
    const { result } = mounted()

    let first!: Promise<boolean>
    let second = true
    act(() => {
      first = submit(result)
    })
    await act(async () => {
      second = await submit(result)
    })
    expect(second).toBe(false)
    expect(result.current.requesting).toBe(true)

    await act(async () => {
      pending.resolve(new AbandonManualRunResponse({}))
      await first
    })
    expect(post).toHaveBeenCalledTimes(1)
    expect(result.current.requesting).toBe(false)
  })

  it('rejects a submission for another run, or while no eligible form owns the interaction, without posting', async () => {
    const post = install(() => Promise.resolve(new AbandonManualRunResponse({})))
    const { result } = mounted()
    const idle = renderHook(() => useAbandonManualRun('run-1', '', 0, vi.fn()))

    let otherRun = true
    let noOwner = true
    await act(async () => {
      otherRun = await submit(result, 'run-2')
      noOwner = await idle.result.current.submit('run-1', 'Reason')
    })

    expect(otherRun).toBe(false)
    expect(noOwner).toBe(false)
    expect(post).not.toHaveBeenCalled()
  })

  it('does not let an obsolete completion refresh the cockpit or report after the form identity is replaced, yet the recorded abandonment still refreshes the read-only status and project list', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender, refreshStatus, refreshProject, onSaved } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submit(result)
    })
    rerender({ runId: 'run-1', identityKey: 'another-form-lifetime' })
    expect(result.current.requesting).toBe(false)
    await act(async () => {
      pending.resolve(new AbandonManualRunResponse({}))
      expect(await outcome).toBe(false)
    })

    expect(onSaved).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(refreshProject).toHaveBeenCalledTimes(1)
  })

  it('does not surface an obsolete failure on A to B to A, and the returning A starts clean', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submit(result)
    })
    rerender({ runId: 'run-1', identityKey: 'b' })
    rerender({ runId: 'run-1' })
    await act(async () => {
      pending.reject(problem(409, 'run_abandonment.reason_conflict'))
      expect(await outcome).toBe(false)
    })

    expect(result.current.error).toBeNull()
    expect(result.current.requesting).toBe(false)
  })

  it('drops a pending state and error when the draft version advances (even for the same text), and ignores the older completion', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submit(result)
    })
    rerender({ runId: 'run-1', draftVersion: 1 })
    expect(result.current.requesting).toBe(false)
    await act(async () => {
      pending.reject(problem(404))
      expect(await outcome).toBe(false)
    })
    expect(result.current.error).toBeNull()
  })

  it('does nothing visible after unmount', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, unmount, onSaved } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submit(result)
    })
    unmount()
    await act(async () => {
      pending.resolve(new AbandonManualRunResponse({}))
      expect(await outcome).toBe(false)
    })

    expect(onSaved).not.toHaveBeenCalled()
  })

  it('works without the optional project refresh and cockpit refresh callbacks', async () => {
    install(() => Promise.resolve(new AbandonManualRunResponse({})))
    const { result } = renderHook(() => useAbandonManualRun('run-1', abandonIdentityKey('run-1'), 0, vi.fn()))

    let accepted = false
    await act(async () => {
      accepted = await result.current.submit('run-1', 'Reason')
    })

    expect(accepted).toBe(true)
  })
})
