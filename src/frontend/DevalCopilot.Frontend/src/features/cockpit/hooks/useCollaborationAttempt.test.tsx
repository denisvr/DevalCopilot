import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationAttemptClient } from '../../../api/clients'
import type { GetCollaborationHistoryAttemptResponse } from '../../../api/generated/api-client'
import { attemptAnswerFor, attemptHeaderFor } from '../collaborationHistoryFixture'
import type { CollaborationAttemptSource } from '../collaborationAttempt'
import { useCollaborationAttempt } from './useCollaborationAttempt'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationAttemptClient: vi.fn(),
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

type Read = (runId: string, messageId: string) => Promise<GetCollaborationHistoryAttemptResponse>

function install(read: Read) {
  const getCollaborationHistoryAttempt = vi.fn(read)
  vi.mocked(collaborationAttemptClient).mockReturnValue({ getCollaborationHistoryAttempt } as unknown as ReturnType<typeof collaborationAttemptClient>)
  return getCollaborationHistoryAttempt
}

const sourceAt = (sequence: number, runId = 'run-1'): CollaborationAttemptSource => ({
  runId,
  messageId: `message-${sequence}`,
  sequence: String(sequence),
  type: 'Proposal',
  protocolVersion: '1.0',
  provenance: 'ProviderObserved',
  actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
})

const answer = (source: CollaborationAttemptSource, attemptId = `attempt-for-${source.sequence}`) =>
  attemptAnswerFor({ id: source.messageId, sequence: source.sequence, runId: source.runId }, 'Available', attemptHeaderFor({ id: attemptId }))

const A = sourceAt(20)
const B = sourceAt(30)

type Hook = { current: ReturnType<typeof useCollaborationAttempt> }

function mounted(lifetime: string | null) {
  return renderHook((props: { lifetime: string | null }) => useCollaborationAttempt(props.lifetime), { initialProps: { lifetime } })
}

describe('useCollaborationAttempt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing on mount, with an open history lifetime and without one', () => {
    const get = install(() => Promise.resolve(answer(A)))

    mounted('history-1')
    mounted(null)

    expect(get).not.toHaveBeenCalled()
    expect(collaborationAttemptClient).not.toHaveBeenCalled()
  })

  it('inspects one source with exactly one request and exposes its validated attribution', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('history-1')

    act(() => result.current.inspect(A))

    expect(result.current.source).toEqual(A)
    expect(result.current.pending).toBe(true)
    expect(result.current.attempt).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', 'message-20')

    await act(async () => read.resolve(answer(A)))

    expect(result.current.pending).toBe(false)
    expect(result.current.failure).toBeNull()
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'attempt-for-20' } })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('inspecting the source already inspected reads nothing again', async () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    act(() => result.current.inspect(A))
    act(() => result.current.inspect({ ...A }))

    expect(get).toHaveBeenCalledTimes(1)
    expect(result.current.attempt?.status).toBe('Available')
  })

  it('replacing A with B ends A: its late answer or failure can neither show nor settle B', async () => {
    const reads: Record<string, ReturnType<typeof deferred<GetCollaborationHistoryAttemptResponse>>> = {
      'message-20': deferred(),
      'message-30': deferred(),
    }
    install((_run, id) => reads[id].promise)
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    act(() => result.current.inspect(B))

    expect(result.current.source).toEqual(B)
    expect(result.current.pending).toBe(true)

    await act(async () => reads['message-20'].reject(new Error('late')))
    expect(result.current.pending).toBe(true)
    expect(result.current.failure).toBeNull()
    expect(result.current.attempt).toBeNull()

    await act(async () => reads['message-30'].resolve(answer(B)))
    expect(result.current.pending).toBe(false)
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'attempt-for-30' } })
    expect(result.current.source).toEqual(B)
  })

  it('A, then B, then A again is a new lifetime: the first A can never settle the second', async () => {
    const reads = [
      deferred<GetCollaborationHistoryAttemptResponse>(),
      deferred<GetCollaborationHistoryAttemptResponse>(),
      deferred<GetCollaborationHistoryAttemptResponse>(),
    ]
    const get = install(() => reads[get.mock.calls.length - 1].promise)
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    act(() => result.current.inspect(B))
    act(() => result.current.inspect(A))

    expect(get).toHaveBeenCalledTimes(3)
    await act(async () => reads[0].resolve(answer(A, 'first-a')))
    expect(result.current.pending).toBe(true)
    expect(result.current.attempt).toBeNull()

    await act(async () => reads[1].resolve(answer(B)))
    expect(result.current.pending).toBe(true)

    await act(async () => reads[2].resolve(answer(A, 'second-a')))
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'second-a' } })
  })

  it('closing discards the answer and any read in flight; inspecting the same source again is a new lifetime', async () => {
    const reads = [deferred<GetCollaborationHistoryAttemptResponse>(), deferred<GetCollaborationHistoryAttemptResponse>()]
    const get = install(() => reads[get.mock.calls.length - 1].promise)
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))

    act(() => result.current.close())
    expect(result.current.source).toBeNull()
    expect(result.current.pending).toBe(false)
    await act(async () => reads[0].resolve(answer(A, 'closed')))
    expect(result.current.attempt).toBeNull()
    expect(result.current.source).toBeNull()

    act(() => result.current.inspect(A))
    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.attempt).toBeNull()
    await act(async () => reads[1].resolve(answer(A, 'reopened')))
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'reopened' } })
  })

  it('closing with nothing inspected is inert', () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result } = mounted('history-1')

    act(() => result.current.close())

    expect(result.current.source).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it.each([
    ['a history reload or reopen (another lifetime)', 'history-2'],
    ['the history closing or another run (no lifetime)', null],
  ])('%s ends the inspector at once and its late answer is ignored', async (_name, next) => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const get = install(() => read.promise)
    const { result, rerender } = mounted('history-1')
    act(() => result.current.inspect(A))

    rerender({ lifetime: next })

    expect(result.current.source).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(result.current.attempt).toBeNull()
    expect(result.current.failure).toBeNull()
    await act(async () => read.resolve(answer(A)))
    expect(result.current.source).toBeNull()
    expect(result.current.attempt).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('returning to an earlier history lifetime key never resurrects its inspector', async () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result, rerender } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)
    expect(result.current.attempt?.status).toBe('Available')

    rerender({ lifetime: 'history-2' })
    rerender({ lifetime: 'history-1' })

    expect(result.current.source).toBeNull()
    expect(result.current.attempt).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('refuses to inspect without an open history lifetime', () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result } = mounted(null)

    act(() => result.current.inspect(A))

    expect(result.current.source).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('callbacks retained from an ended history lifetime start nothing and change nothing', async () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result, rerender } = mounted('history-1')
    const stale = result.current
    act(() => stale.inspect(A))
    await act(async () => undefined)
    rerender({ lifetime: 'history-2' })
    get.mockClear()

    act(() => stale.inspect(B))
    act(() => stale.retry())
    act(() => stale.close())

    expect(get).not.toHaveBeenCalled()
    expect(result.current.source).toBeNull()
    expect(result.current.attempt).toBeNull()
  })

  it('a stale retry cannot start a read for a replaced source', async () => {
    const get = install((_run, id) => (id === 'message-20' ? Promise.reject(new Error('down')) : Promise.resolve(answer(B))))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)
    const staleRetry = result.current.retry
    expect(result.current.failure).toBe('read')
    act(() => result.current.inspect(B))
    await act(async () => undefined)
    get.mockClear()

    act(() => staleRetry())

    expect(get).not.toHaveBeenCalled()
    expect(result.current.attempt?.status).toBe('Available')
  })

  it('unmounting leaves a pending read inert: nothing settles and later calls start nothing', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const get = install(() => read.promise)
    const { result, unmount } = mounted('history-1')
    act(() => result.current.inspect(A))
    const retained = result.current
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    unmount()
    await act(async () => read.resolve(answer(A)))
    retained.retry()
    retained.inspect(B)
    retained.close()

    expect(get).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('reports a failed request, keeps the source and retries the same source only on an explicit retry', async () => {
    const answers: Array<() => Promise<GetCollaborationHistoryAttemptResponse>> = [
      () => Promise.reject(new Error('down')),
      () => Promise.resolve(answer(A, 'recovered')),
    ]
    const get = install(() => answers[get.mock.calls.length - 1]())
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    expect(result.current.failure).toBe('read')
    expect(result.current.source).toEqual(A)
    expect(result.current.attempt).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.retry())
    act(() => result.current.retry())
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', 'message-20')
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(true)

    await act(async () => undefined)
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'recovered' } })
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('retry does nothing without a failure, while pending or with nothing inspected', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('history-1')

    act(() => result.current.retry())
    act(() => result.current.inspect(A))
    act(() => result.current.retry())
    await act(async () => read.resolve(answer(A)))
    act(() => result.current.retry())

    expect(get).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['another run', attemptAnswerFor({ id: 'message-20', sequence: '20', runId: 'run-2' }, 'Unavailable')],
    ['another message', attemptAnswerFor({ id: 'message-21', sequence: '20' }, 'Unavailable')],
    ['another source sequence', attemptAnswerFor({ id: 'message-20', sequence: '21' }, 'Unavailable')],
    ['an unknown status', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'Pending')],
    ['an attempt beside Unavailable', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'Unavailable', attemptHeaderFor())],
    ['NoAgentAttempt for a provider-observed source', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'NoAgentAttempt')],
    ['a header for another actor', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'Available', attemptHeaderFor({ role: 'Resolver', responseContract: 'ChallengeResolution' }))],
    ['a header with number zero', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'Available', attemptHeaderFor({ attemptNumber: 0 }))],
    ['a header with an unsupported contract', attemptAnswerFor({ id: 'message-20', sequence: '20' }, 'Available', attemptHeaderFor({ responseContract: 'ReviewCorrection' }))],
  ])('shows nothing of a contradictory answer (%s) and reports an invalid one', async (_name, response) => {
    install(() => Promise.resolve(response))
    const { result } = mounted('history-1')

    act(() => result.current.inspect(A))
    await act(async () => undefined)

    expect(result.current.failure).toBe('invalid')
    expect(result.current.attempt).toBeNull()
    expect(result.current.source).toEqual(A)
  })

  it('accepts NoAgentAttempt for a non-provider-observed source and Unavailable for any source', async () => {
    const host: CollaborationAttemptSource = { ...sourceAt(5), provenance: 'HostConstructed', actor: { kind: 'Orchestrator', role: null, provider: null } }
    install((_run, id) =>
      Promise.resolve(
        id === 'message-5'
          ? attemptAnswerFor({ id, sequence: '5' }, 'NoAgentAttempt')
          : attemptAnswerFor({ id, sequence: '20' }, 'Unavailable'),
      ))
    const { result } = mounted('history-1')

    act(() => result.current.inspect(host))
    await act(async () => undefined)
    expect(result.current.attempt).toEqual({ status: 'NoAgentAttempt' })

    act(() => result.current.inspect(A))
    await act(async () => undefined)
    expect(result.current.attempt).toEqual({ status: 'Unavailable' })
  })
})

describe('a retained close callback of an ended attempt inspector', () => {
  type Flow = 'replace' | 'a-b-a' | 'close-reopen'
  const flows: Flow[] = ['replace', 'a-b-a', 'close-reopen']

  // Ends A's inspector by the given flow and returns the source that must still be inspected afterwards.
  const endA = (result: Hook, flow: Flow) => {
    if (flow === 'close-reopen') {
      act(() => result.current.close())
      act(() => result.current.inspect(A))
      return A
    }

    act(() => result.current.inspect(B))
    if (flow === 'a-b-a') {
      act(() => result.current.inspect(A))
      return A
    }

    return B
  }

  it.each(flows)('cannot close its successor once that is settled (%s)', async (flow) => {
    const get = install((_run, id) => Promise.resolve(answer(id === A.messageId ? A : B)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)
    const staleClose = result.current.close
    const expected = endA(result, flow)
    await act(async () => undefined)
    const reads = get.mock.calls.length

    act(() => staleClose())

    expect(result.current.source).toEqual(expected)
    expect(result.current.attempt?.status).toBe('Available')
    expect(get).toHaveBeenCalledTimes(reads)
  })

  it.each(flows)('cannot close its successor while that is still pending, nor settle or restart it (%s)', async (flow) => {
    const reads: Array<ReturnType<typeof deferred<GetCollaborationHistoryAttemptResponse>>> = []
    const get = install(() => {
      const next = deferred<GetCollaborationHistoryAttemptResponse>()
      reads.push(next)
      return next.promise
    })
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    const staleClose = result.current.close
    const expected = endA(result, flow)
    const started = get.mock.calls.length

    act(() => staleClose())

    expect(result.current.source).toEqual(expected)
    expect(result.current.pending).toBe(true)
    expect(get).toHaveBeenCalledTimes(started)
    await act(async () => reads[reads.length - 1].resolve(answer(expected, 'successor')))
    expect(result.current.attempt).toMatchObject({ status: 'Available', attempt: { id: 'successor' } })
  })

  it.each(flows)('cannot close a successor queued in the same React batch (%s)', async (flow) => {
    install((_run, id) => Promise.resolve(answer(id === A.messageId ? A : B)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)
    const staleClose = result.current.close
    const calls: Array<() => void> = []
    if (flow === 'close-reopen') {
      calls.push(() => result.current.close(), () => result.current.inspect(A))
    } else {
      calls.push(() => result.current.inspect(B))
      if (flow === 'a-b-a') {
        calls.push(() => result.current.inspect(A))
      }
    }

    act(() => {
      for (const call of calls) {
        call()
      }

      staleClose()
    })
    await act(async () => undefined)

    expect(result.current.source).toEqual(flow === 'replace' ? B : A)
    expect(result.current.attempt?.status).toBe('Available')
  })

  it('still lets the current close end its own inspector, once, and a later one is not closed by it', async () => {
    const get = install((_run, id) => Promise.resolve(answer(id === A.messageId ? A : B)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    const currentClose = result.current.close
    act(() => currentClose())
    expect(result.current.source).toBeNull()
    expect(result.current.attempt).toBeNull()

    act(() => result.current.inspect(B))
    await act(async () => undefined)
    act(() => currentClose())
    expect(result.current.source).toEqual(B)
    expect(get).toHaveBeenCalledTimes(2)

    act(() => result.current.close())
    expect(result.current.source).toBeNull()
  })

  it('a current close in the same batch as a later inspect leaves that inspect in place', async () => {
    install((_run, id) => Promise.resolve(answer(id === A.messageId ? A : B)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    act(() => {
      result.current.close()
      result.current.inspect(B)
    })
    await act(async () => undefined)

    expect(result.current.source).toEqual(B)
    expect(result.current.attempt?.status).toBe('Available')
  })
})
