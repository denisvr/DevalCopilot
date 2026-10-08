import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { requestLocalCommitClient } from '../../../api/clients'
import { ApiException, LocalCommitOperationResponse } from '../../../api/generated/api-client'
import { LOCAL_COMMIT_UNKNOWN_OUTCOME_MESSAGE, localCommitIdentityKey } from '../describeLocalCommit'
import type { LocalCommitIdentity } from '../describeLocalCommit'
import { useRequestLocalCommit } from './useRequestLocalCommit'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  requestLocalCommitClient: vi.fn(),
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

const identity = (marker: string): LocalCommitIdentity => ({
  checkpointId: `checkpoint-${marker}`,
  codeReviewAttemptId: `review-${marker}`,
  humanCheckpointReviewId: `human-${marker}`,
})

const problem = (status: number, code?: string) =>
  new ApiException('raw server text that must never be shown', status, JSON.stringify({ errors: [{ code, detail: 'server detail' }] }), {}, null)

function install(post: () => Promise<unknown>) {
  const requestLocalCommit = vi.fn(post)
  vi.mocked(requestLocalCommitClient).mockReturnValue({ requestLocalCommit } as unknown as ReturnType<typeof requestLocalCommitClient>)
  return requestLocalCommit
}

interface Props {
  runId: string
  marker: string
  draftVersion?: number
}

function mounted(refreshStatus = vi.fn(), onSaved: (() => unknown) | undefined = vi.fn(async () => true)) {
  const rendered = renderHook(
    (props: Props) =>
      useRequestLocalCommit(props.runId, localCommitIdentityKey(props.runId, identity(props.marker)), props.draftVersion ?? 0, refreshStatus, onSaved),
    { initialProps: { runId: 'run-1', marker: 'a' } as Props },
  )
  return { ...rendered, refreshStatus, onSaved }
}

const submitA = (result: { current: ReturnType<typeof useRequestLocalCommit> }, runId = 'run-1', marker = 'a') =>
  result.current.submit(runId, identity(marker), 'Subject', 'operation-1')

describe('useRequestLocalCommit', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('posts the bound identity, message and operation id once and refreshes the status and the cockpit on acceptance', async () => {
    const post = install(() => Promise.resolve(new LocalCommitOperationResponse({ status: 'Prepared' })))
    const { result, refreshStatus, onSaved } = mounted()

    let accepted = false
    await act(async () => {
      accepted = await submitA(result)
    })

    expect(accepted).toBe(true)
    expect(post).toHaveBeenCalledTimes(1)
    const [runId, body] = post.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(runId).toBe('run-1')
    expect(body).toMatchObject({
      operationId: 'operation-1',
      checkpointId: 'checkpoint-a',
      codeReviewAttemptId: 'review-a',
      humanCheckpointReviewId: 'human-a',
      message: 'Subject',
    })
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(onSaved).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBeNull()
  })

  it('does not report an accepted request as failed when the follow-up cockpit refresh rejects or throws', async () => {
    install(() => Promise.resolve(new LocalCommitOperationResponse({ status: 'Prepared' })))
    const rejecting = mounted(vi.fn(), () => Promise.reject(new Error('refresh failed')))
    let accepted = false
    await act(async () => {
      accepted = await submitA(rejecting.result)
    })
    expect(accepted).toBe(true)
    expect(rejecting.result.current.error).toBeNull()

    const throwing = mounted(vi.fn(), () => {
      throw new Error('sync throw')
    })
    await act(async () => {
      accepted = await submitA(throwing.result)
    })
    expect(accepted).toBe(true)
    expect(throwing.result.current.error).toBeNull()
  })

  it('shows fixed copy for an operation conflict and never the server text, and does not read the status', async () => {
    install(() => Promise.reject(problem(409, 'local_commit.operation_conflict')))
    const { result, refreshStatus, onSaved } = mounted()

    await act(async () => {
      await submitA(result)
    })

    expect(result.current.error).toContain('already has a different local-commit operation')
    expect(result.current.error).not.toContain('server')
    expect(refreshStatus).not.toHaveBeenCalled()
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('describes a conversion refusal with fixed copy', async () => {
    install(() => Promise.reject(problem(409, 'local_commit.refused.conversion_refused')))
    const { result } = mounted()

    await act(async () => {
      await submitA(result)
    })

    expect(result.current.error).toMatch(/line-ending normalization/)
    expect(result.current.error).toMatch(/new checkpoint/)
  })

  it.each([
    ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['a 5xx response', () => Promise.reject(problem(503))],
  ])('treats %s as an unknown outcome: one POST, a read-only status reconciliation and no resubmission', async (_name, failure) => {
    const post = install(failure)
    const { result, refreshStatus, onSaved } = mounted()

    let accepted = true
    await act(async () => {
      accepted = await submitA(result)
    })

    expect(accepted).toBe(false)
    expect(post).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBe(LOCAL_COMMIT_UNKNOWN_OUTCOME_MESSAGE)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
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
      first = submitA(result)
    })
    await act(async () => {
      second = await submitA(result)
    })
    expect(second).toBe(false)
    expect(result.current.requesting).toBe(true)

    await act(async () => {
      pending.resolve(new LocalCommitOperationResponse({}))
      await first
    })
    expect(post).toHaveBeenCalledTimes(1)
    expect(result.current.requesting).toBe(false)
  })

  it('rejects a submission whose run or identity is not the committed owner without posting', async () => {
    const post = install(() => Promise.resolve(new LocalCommitOperationResponse({})))
    const { result } = mounted()

    let otherIdentity = true
    let otherRun = true
    await act(async () => {
      otherIdentity = await submitA(result, 'run-1', 'b')
      otherRun = await submitA(result, 'run-2', 'a')
    })

    expect(otherIdentity).toBe(false)
    expect(otherRun).toBe(false)
    expect(post).not.toHaveBeenCalled()
  })

  it('does not let an obsolete completion refresh the cockpit or report after the identity is replaced', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender, onSaved } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submitA(result)
    })
    rerender({ runId: 'run-1', marker: 'b' })
    expect(result.current.requesting).toBe(false)
    await act(async () => {
      pending.resolve(new LocalCommitOperationResponse({}))
      expect(await outcome).toBe(false)
    })

    expect(onSaved).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
  })

  it('does not surface an obsolete failure on A to B to A, and the returning A starts clean', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submitA(result)
    })
    rerender({ runId: 'run-1', marker: 'b' })
    rerender({ runId: 'run-1', marker: 'a' })
    await act(async () => {
      pending.reject(problem(409, 'local_commit.operation_conflict'))
      expect(await outcome).toBe(false)
    })

    expect(result.current.error).toBeNull()
    expect(result.current.requesting).toBe(false)
  })

  it('drops a pending state and error when the draft version advances, and ignores the older completion', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender } = mounted()

    let outcome: Promise<boolean> | undefined
    act(() => {
      outcome = submitA(result)
    })
    rerender({ runId: 'run-1', marker: 'a', draftVersion: 1 })
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
      outcome = submitA(result)
    })
    unmount()
    await act(async () => {
      pending.resolve(new LocalCommitOperationResponse({}))
      expect(await outcome).toBe(false)
    })

    expect(onSaved).not.toHaveBeenCalled()
  })
})
