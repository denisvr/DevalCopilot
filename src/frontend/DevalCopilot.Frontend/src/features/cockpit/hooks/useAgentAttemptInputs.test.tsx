import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptInputsClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { emptyInputsFor, recordedInputsFor, unavailableInputsFor } from '../agentAttemptInputsFixture'
import { attemptIdFor } from '../agentAttemptHistoryFixture'
import { useAgentAttemptInputs } from './useAgentAttemptInputs'
import type { AgentAttemptInputsTarget } from './useAgentAttemptInputs'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptInputsClient: vi.fn(),
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

type Read = (runId: string, attemptId: string) => Promise<unknown>

function install(read: Read) {
  const readAgentAttemptInputs = vi.fn(read)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs } as unknown as ReturnType<typeof agentAttemptInputsClient>)
  return readAgentAttemptInputs
}

const expectationFor = (runId: string, attemptNumber: number): AgentAttemptIdentityExpectation => ({
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner',
  provider: 'Codex',
  responseContract: 'Proposal',
})

const A: AgentAttemptInputsTarget = { selectionKey: 'selection-a', runId: 'run-1', expected: expectationFor('run-1', 2) }
const B: AgentAttemptInputsTarget = { selectionKey: 'selection-b', runId: 'run-1', expected: expectationFor('run-1', 1) }
const answerFor = (target: AgentAttemptInputsTarget, count = 2) => recordedInputsFor(target.runId, target.expected.attemptNumber, count)

function mounted(target: AgentAttemptInputsTarget) {
  return renderHook((props: AgentAttemptInputsTarget) => useAgentAttemptInputs(props), { initialProps: target })
}

const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'inputs-test-trace' })

describe('useAgentAttemptInputs', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the inspector is opened, with a selection and without one', () => {
    const read = install(() => Promise.resolve(answerFor(A)))

    const { result } = mounted(A)
    mounted({ ...A, selectionKey: null })

    expect(result.current.open).toBe(false)
    expect(result.current.inputs).toBeNull()
    expect(read).not.toHaveBeenCalled()
    expect(agentAttemptInputsClient).not.toHaveBeenCalled()
  })

  it('opens with exactly one request for the selected run and attempt and exposes the validated answer', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)
    const { result } = mounted(A)

    act(() => result.current.show())

    expect(result.current.open).toBe(true)
    expect(result.current.pending).toBe(true)
    expect(result.current.inputs).toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1', A.expected.attemptId)

    await act(async () => {
      pending.resolve(answerFor(A, 11))
      await pending.promise
    })

    expect(result.current.pending).toBe(false)
    expect(result.current.failure).toBeNull()
    expect(result.current.inputs).toMatchObject({ status: 'Recorded', totalCount: 11, omitted: true })
    act(() => result.current.show())
    expect(read).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['Empty', emptyInputsFor('run-1', 2)],
    ['Unavailable', unavailableInputsFor('run-1', 2)],
  ])('exposes %s as its own state', async (status, body) => {
    install(() => Promise.resolve(body))
    const { result } = mounted(A)

    act(() => result.current.show())
    await act(async () => undefined)

    expect(result.current.inputs).toEqual({ status })
    expect(result.current.failure).toBeNull()
  })

  it.each([
    ['an answer for another attempt', () => Promise.resolve(answerFor(B)), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Recorded' }), 'invalid'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(notFound()), 'not-found'],
  ] as const)('reports %s as one failure with no inputs', async (_, respond, failure) => {
    install(respond)
    const { result } = mounted(A)

    act(() => result.current.show())
    await act(async () => undefined)

    expect(result.current.failure).toBe(failure)
    expect(result.current.inputs).toBeNull()
    expect(result.current.pending).toBe(false)
  })

  it('retries the same attempt only after a failure, once per click, and never while a read is pending or succeeded', async () => {
    const second = deferred<unknown>()
    const read = install(vi.fn<Read>().mockRejectedValueOnce(new Error('boom')).mockReturnValueOnce(second.promise))
    const { result } = mounted(A)
    act(() => result.current.retry())
    expect(read).not.toHaveBeenCalled()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(result.current.failure).toBe('read')

    act(() => {
      result.current.retry()
      result.current.retry()
    })
    expect(read).toHaveBeenCalledTimes(2)
    expect(read).toHaveBeenLastCalledWith('run-1', A.expected.attemptId)
    expect(result.current.pending).toBe(true)
    expect(result.current.failure).toBeNull()
    act(() => result.current.retry())
    expect(read).toHaveBeenCalledTimes(2)

    await act(async () => {
      second.resolve(answerFor(A))
      await second.promise
    })
    act(() => result.current.retry())
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.inputs?.status).toBe('Recorded')
  })

  it('closing discards the answer and the read in flight; opening again is a new lifetime that the first read cannot settle', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const read = install(vi.fn<Read>().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise))
    const { result } = mounted(A)
    act(() => result.current.show())

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    expect(result.current.pending).toBe(false)
    await act(async () => {
      first.resolve(answerFor(A, 5))
      await first.promise
    })
    expect(result.current.inputs).toBeNull()

    act(() => result.current.show())
    expect(result.current.pending).toBe(true)
    expect(result.current.inputs).toBeNull()
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      second.resolve(answerFor(A, 3))
      await second.promise
    })
    expect(result.current.inputs).toMatchObject({ status: 'Recorded', totalCount: 3 })
  })

  it('an older overlapping read of the same opening can never overwrite or settle a newer one', async () => {
    const older = deferred<unknown>()
    const newer = deferred<unknown>()
    const read = install(vi.fn<Read>().mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    // The same opening reads again when its target changes without a new selection (no product path does this today).
    rerender({ ...A, runId: 'run-2', expected: expectationFor('run-2', 2) })
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      newer.resolve(recordedInputsFor('run-2', 2, 3))
      await newer.promise
    })
    await act(async () => {
      older.resolve(recordedInputsFor('run-1', 2, 8))
      await older.promise
    })

    expect(result.current.inputs).toMatchObject({ status: 'Recorded', totalCount: 3 })
  })

  it('a retained close or retry of an ended opening neither closes nor reads for its successor, even in the same batch', async () => {
    const read = install(() => Promise.reject(new Error('boom')))
    const { result } = mounted(A)
    act(() => result.current.show())
    await act(async () => undefined)
    const staleClose = result.current.close
    const staleRetry = result.current.retry

    act(() => {
      result.current.close()
      result.current.show()
    })
    await act(async () => undefined)
    expect(result.current.open).toBe(true)
    expect(read).toHaveBeenCalledTimes(2)

    act(() => {
      staleClose()
      staleRetry()
    })
    expect(result.current.open).toBe(true)
    expect(read).toHaveBeenCalledTimes(2)

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
  })

  it('another selection (A to B) shows nothing of A in its first frame and no late answer of A settles B', async () => {
    const lateA = deferred<unknown>()
    const read = install((_run, attemptId) => (attemptId === A.expected.attemptId ? lateA.promise : Promise.resolve(answerFor(B))))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    rerender(B)

    expect(result.current.open).toBe(false)
    expect(result.current.pending).toBe(false)
    expect(result.current.inputs).toBeNull()
    await act(async () => {
      lateA.resolve(answerFor(A, 7))
      await lateA.promise
    })
    expect(result.current.inputs).toBeNull()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(read).toHaveBeenLastCalledWith('run-1', B.expected.attemptId)
    expect(result.current.inputs).toMatchObject({ status: 'Recorded', totalCount: 2 })
  })

  it('returning to A (A to B to A) starts closed and fresh, and the first visit read stays ignored', async () => {
    const firstA = deferred<unknown>()
    let visits = 0
    const read = install((_run, attemptId) =>
      attemptId === A.expected.attemptId && visits++ === 0 ? firstA.promise : Promise.resolve(answerFor(attemptId === A.expected.attemptId ? A : B, 4)))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())
    rerender(B)
    rerender({ ...A, selectionKey: 'selection-a-again' })

    expect(result.current.open).toBe(false)
    await act(async () => {
      firstA.resolve(answerFor(A, 9))
      await firstA.promise
    })
    expect(result.current.inputs).toBeNull()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(result.current.inputs).toMatchObject({ status: 'Recorded', totalCount: 4 })
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('a retained show of an ended selection opens nothing for its successor', () => {
    const read = install(() => Promise.resolve(answerFor(A)))
    const { result, rerender } = mounted(A)
    const staleShow = result.current.show

    rerender(B)
    act(() => staleShow())

    expect(result.current.open).toBe(false)
    expect(read).not.toHaveBeenCalled()
  })

  it('ending the selection closes the inspector and makes every callback inert', () => {
    const read = install(() => new Promise<unknown>(() => undefined))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    rerender({ ...A, selectionKey: null })

    expect(result.current.open).toBe(false)
    act(() => {
      result.current.show()
      result.current.retry()
    })
    expect(result.current.open).toBe(false)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('unmounting leaves a pending read inert and reports no React error', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const consoleError = vi.spyOn(console, 'error')
    const { result, unmount } = mounted(A)
    act(() => result.current.show())
    const retainedRetry = result.current.retry

    unmount()
    await act(async () => {
      pending.resolve(answerFor(A))
      await pending.promise
    })
    retainedRetry()

    expect(consoleError).not.toHaveBeenCalled()
    expect(agentAttemptInputsClient).toHaveBeenCalledTimes(1)
    consoleError.mockRestore()
  })
})
