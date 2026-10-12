import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationReplyClient } from '../../../api/clients'
import type { GetCollaborationHistoryReplyResponse } from '../../../api/generated/api-client'
import { entryFor, replyFor } from '../collaborationHistoryFixture'
import type { CollaborationReplySource } from '../collaborationReply'
import { useCollaborationReply } from './useCollaborationReply'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationReplyClient: vi.fn(),
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

type Read = (runId: string, messageId: string) => Promise<GetCollaborationHistoryReplyResponse>

function install(read: Read) {
  const getCollaborationHistoryReply = vi.fn(read)
  vi.mocked(collaborationReplyClient).mockReturnValue({ getCollaborationHistoryReply } as unknown as ReturnType<typeof collaborationReplyClient>)
  return getCollaborationHistoryReply
}

const sourceAt = (sequence: number, runId = 'run-1'): CollaborationReplySource => ({
  runId,
  messageId: `message-${sequence}`,
  sequence: String(sequence),
  type: 'Acceptance',
  protocolVersion: '1.0',
})

const answer = (source: CollaborationReplySource, parentSequence = source.sequence === '20' ? 10 : Number(source.sequence) - 10) =>
  replyFor({ id: source.messageId, sequence: source.sequence, runId: source.runId }, 'Available', entryFor(parentSequence, { runId: source.runId }))

const A = sourceAt(20)
const B = sourceAt(30)

function mounted(lifetime: string | null) {
  return renderHook((props: { lifetime: string | null }) => useCollaborationReply(props.lifetime), { initialProps: { lifetime } })
}

describe('useCollaborationReply', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing on mount, with an open history lifetime and without one', () => {
    const get = install(() => Promise.resolve(answer(A)))

    mounted('history-1')
    mounted(null)

    expect(get).not.toHaveBeenCalled()
    expect(collaborationReplyClient).not.toHaveBeenCalled()
  })

  it('inspects one source with exactly one request and exposes its validated relationship', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('history-1')

    act(() => result.current.inspect(A))

    expect(result.current.source).toEqual(A)
    expect(result.current.pending).toBe(true)
    expect(result.current.reply).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', 'message-20')

    await act(async () => read.resolve(answer(A)))

    expect(result.current.pending).toBe(false)
    expect(result.current.failure).toBeNull()
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-10' } })
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
    expect(result.current.reply?.status).toBe('Available')
  })

  it('replacing A with B ends A: its late answer or failure can neither show nor settle B', async () => {
    const reads: Record<string, ReturnType<typeof deferred<GetCollaborationHistoryReplyResponse>>> = {
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
    expect(result.current.reply).toBeNull()

    await act(async () => reads['message-30'].resolve(answer(B)))
    expect(result.current.pending).toBe(false)
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-20' } })
    expect(result.current.source).toEqual(B)
  })

  it('A, then B, then A again is a new lifetime: the first A can never settle the second', async () => {
    const reads = [deferred<GetCollaborationHistoryReplyResponse>(), deferred<GetCollaborationHistoryReplyResponse>(), deferred<GetCollaborationHistoryReplyResponse>()]
    const get = install(() => reads[get.mock.calls.length - 1].promise)
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    act(() => result.current.inspect(B))
    act(() => result.current.inspect(A))

    expect(get).toHaveBeenCalledTimes(3)
    expect(result.current.pending).toBe(true)

    await act(async () => reads[0].resolve(answer(A, 11)))
    expect(result.current.pending).toBe(true)
    expect(result.current.reply).toBeNull()

    await act(async () => reads[1].resolve(answer(B)))
    expect(result.current.pending).toBe(true)

    await act(async () => reads[2].resolve(answer(A, 12)))
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-12' } })
  })

  it('closing discards the answer and any read in flight; reopening the same source reads it again as a new lifetime', async () => {
    const reads = [deferred<GetCollaborationHistoryReplyResponse>(), deferred<GetCollaborationHistoryReplyResponse>()]
    const get = install(() => reads[get.mock.calls.length - 1].promise)
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))

    act(() => result.current.close())
    expect(result.current.source).toBeNull()
    expect(result.current.pending).toBe(false)
    await act(async () => reads[0].resolve(answer(A, 11)))
    expect(result.current.reply).toBeNull()
    expect(result.current.source).toBeNull()

    act(() => result.current.inspect(A))
    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.reply).toBeNull()
    await act(async () => reads[1].resolve(answer(A, 12)))
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-12' } })
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
  ])('%s ends the inspector at once, with no source, answer or error, and its late answer is ignored', async (_name, next) => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const get = install(() => read.promise)
    const { result, rerender } = mounted('history-1')
    act(() => result.current.inspect(A))

    rerender({ lifetime: next })

    expect(result.current.source).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(result.current.reply).toBeNull()
    expect(result.current.failure).toBeNull()
    await act(async () => read.resolve(answer(A)))
    expect(result.current.source).toBeNull()
    expect(result.current.reply).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('returning to an earlier history lifetime key never resurrects its inspector', async () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result, rerender } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)
    expect(result.current.reply?.status).toBe('Available')

    rerender({ lifetime: 'history-2' })
    rerender({ lifetime: 'history-1' })

    expect(result.current.source).toBeNull()
    expect(result.current.reply).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('refuses to inspect without an open history lifetime', () => {
    const get = install(() => Promise.resolve(answer(A)))
    const { result } = mounted(null)

    act(() => result.current.inspect(A))

    expect(result.current.source).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('callbacks retained from an ended lifetime start nothing and change nothing', async () => {
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
    expect(result.current.reply).toBeNull()
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
    expect(result.current.reply?.status).toBe('Available')
  })

  it('unmounting leaves a pending read inert: nothing settles and later calls start nothing', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const get = install(() => read.promise)
    const { result, unmount } = mounted('history-1')
    act(() => result.current.inspect(A))
    const retained = result.current
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    unmount()
    await act(async () => read.resolve(answer(A)))
    retained.retry()
    retained.inspect(B)

    expect(get).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('reports a failed request, keeps the source and retries the same source only on an explicit retry', async () => {
    const answers: Array<() => Promise<GetCollaborationHistoryReplyResponse>> = [
      () => Promise.reject(new Error('down')),
      () => Promise.resolve(answer(A, 7)),
    ]
    const get = install(() => answers[get.mock.calls.length - 1]())
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    expect(result.current.failure).toBe('read')
    expect(result.current.source).toEqual(A)
    expect(result.current.reply).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.retry())
    act(() => result.current.retry())
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', 'message-20')
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(true)

    await act(async () => undefined)
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-7' } })
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('retry does nothing without a failure, while pending or with nothing inspected', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
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
    ['another run', replyFor({ id: 'message-20', sequence: '20', runId: 'run-2' }, 'Unavailable')],
    ['another message', replyFor({ id: 'message-21', sequence: '20' }, 'Unavailable')],
    ['another source sequence', replyFor({ id: 'message-20', sequence: '21' }, 'Unavailable')],
    ['an unknown status', replyFor({ id: 'message-20', sequence: '20' }, 'Pending')],
    ['a forward parent', replyFor({ id: 'message-20', sequence: '20' }, 'Available', entryFor(25))],
    ['an incompatible parent', replyFor({ id: 'message-20', sequence: '20' }, 'Available', entryFor(10, { type: 'Decision', fields: [] }))],
    ['a parent beside Unavailable', replyFor({ id: 'message-20', sequence: '20' }, 'Unavailable', entryFor(10))],
  ])('shows nothing of a contradictory answer (%s) and reports an invalid one', async (_name, response) => {
    install(() => Promise.resolve(response))
    const { result } = mounted('history-1')

    act(() => result.current.inspect(A))
    await act(async () => undefined)

    expect(result.current.failure).toBe('invalid')
    expect(result.current.reply).toBeNull()
    expect(result.current.source).toEqual(A)
  })

  it('accepts None for a root Proposal and Unavailable for any source', async () => {
    const root = { ...sourceAt(5), type: 'Proposal' as const }
    install((_run, id) =>
      Promise.resolve(id === 'message-5' ? replyFor({ id, sequence: '5' }, 'None') : replyFor({ id, sequence: '20' }, 'Unavailable')))
    const { result } = mounted('history-1')

    act(() => result.current.inspect(root))
    await act(async () => undefined)
    expect(result.current.reply).toEqual({ status: 'None' })

    act(() => result.current.inspect(A))
    await act(async () => undefined)
    expect(result.current.reply).toEqual({ status: 'Unavailable' })
  })
})

describe('a retained close callback of an ended inspector', () => {
  type Flow = 'replace' | 'a-b-a' | 'close-reopen'
  const flows: Flow[] = ['replace', 'a-b-a', 'close-reopen']

  // Ends A's inspector by the given flow and returns the source that must still be inspected afterwards.
  const endA = (result: { current: ReturnType<typeof useCollaborationReply> }, flow: Flow) => {
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
    expect(result.current.reply?.status).toBe('Available')
    expect(get).toHaveBeenCalledTimes(reads)
  })

  it.each(flows)('cannot close its successor while that is still pending, nor settle or restart it (%s)', async (flow) => {
    const reads: Array<ReturnType<typeof deferred<GetCollaborationHistoryReplyResponse>>> = []
    const get = install(() => {
      const next = deferred<GetCollaborationHistoryReplyResponse>()
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
    await act(async () => reads[reads.length - 1].resolve(answer(expected, 8)))
    expect(result.current.reply).toMatchObject({ status: 'Available', parent: { id: 'message-8' } })
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
    expect(result.current.reply?.status).toBe('Available')
  })

  it('still lets the current close end its own inspector, once, and a later one is not closed by it', async () => {
    const get = install((_run, id) => Promise.resolve(answer(id === A.messageId ? A : B)))
    const { result } = mounted('history-1')
    act(() => result.current.inspect(A))
    await act(async () => undefined)

    const currentClose = result.current.close
    act(() => currentClose())
    expect(result.current.source).toBeNull()
    expect(result.current.reply).toBeNull()

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
    expect(result.current.reply?.status).toBe('Available')
  })
})
