import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationHistoryClient } from '../../../api/clients'
import type { GetCollaborationHistoryResponse } from '../../../api/generated/api-client'
import { descendingSequences, collaborationPageOf, entryFor } from '../collaborationHistoryFixture'
import { useCollaborationHistory } from './useCollaborationHistory'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationHistoryClient: vi.fn(),
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

type Read = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetCollaborationHistoryResponse>

function install(read: Read) {
  const getCollaborationHistory = vi.fn(read)
  vi.mocked(collaborationHistoryClient).mockReturnValue({ getCollaborationHistory } as unknown as ReturnType<typeof collaborationHistoryClient>)
  return getCollaborationHistory
}

const forRun = (runId: string) => (sequences: readonly (number | string)[], options: { hasMore?: boolean } = {}) =>
  collaborationPageOf(sequences, { runId, hasMore: options.hasMore ?? false })

const sequences = (rows: ReadonlyArray<{ sequence: string }>) => rows.map((row) => row.sequence)
const texts = (list: readonly number[]) => list.map(String)

function mounted(runId: string | null) {
  return renderHook((props: { runId: string | null }) => useCollaborationHistory(props.runId), { initialProps: { runId } })
}

describe('useCollaborationHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is closed by default and reads nothing until it is opened', () => {
    const get = install(() => Promise.resolve(collaborationPageOf([1])))
    const { result } = mounted('run-1')

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with exactly one newest-page request and exposes its validated rows', async () => {
    const read = deferred<GetCollaborationHistoryResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))

    expect(result.current.open).toBe(true)
    expect(result.current.pending).toBe('first')
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', null, 5)

    await act(async () => read.resolve(collaborationPageOf([40, 12, 9])))

    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(sequences(result.current.rows)).toEqual(['40', '12', '9'])
    expect(result.current.hasMore).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('reports an empty run as a loaded, empty, ended history', async () => {
    install(() => Promise.resolve(collaborationPageOf([])))
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.rows).toEqual([])
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
  })

  it('shows a failed first page as an explicit read failure with no rows, and only reload reads again', async () => {
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(collaborationPageOf([4, 3]))))
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('read')
    expect(result.current.failedPage).toBe('first')
    expect(result.current.rows).toEqual([])
    expect(result.current.hasMore).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)
    act(() => result.current.loadOlder())
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.reload())
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe('first')
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(sequences(result.current.rows)).toEqual(['4', '3'])
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', null, 5)
  })

  it.each([
    ['another run', () => collaborationPageOf([3, 2], { runId: 'run-2' })],
    ['an out-of-order page', () => collaborationPageOf([2, 3])],
    ['an oversized page', () => collaborationPageOf(descendingSequences(6, 1))],
    ['an incoherent continuation', () => collaborationPageOf(descendingSequences(5, 1), { hasMore: true, next: "4" })],
    ['a non-canonical sequence', () => collaborationPageOf([], { entries: [entryFor(3, { sequence: '03' })] })],
    ['an unlisted message type', () => collaborationPageOf([], { entries: [entryFor(3, { type: 'Overlord' })] })],
  ])('refuses %s as an explicit invalid-page failure and never shows a partial page', async (_name, build) => {
    install(() => Promise.resolve(build()))
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('invalid')
    expect(result.current.rows).toEqual([])
    expect(result.current.hasMore).toBe(false)
  })

  it('loads the next older page with the exact server cursor only, appends it and ends at the last page', async () => {
    const get = install((_run, before) =>
      Promise.resolve(
        before === null
          ? collaborationPageOf(descendingSequences(30, 26), { hasMore: true })
          : before === '26'
            ? collaborationPageOf(descendingSequences(25, 21), { hasMore: true })
            : collaborationPageOf(descendingSequences(20, 16)),
      ),
    )
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.pending).toBe('older')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 16)))
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', '21'])
    act(() => result.current.loadOlder())
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('sends a sequence above 2^53 as the exact cursor text and accepts the neighbors Number cannot tell apart', async () => {
    const big = Array.from({ length: 5 }, (_, index) => (9223372036854775807n - BigInt(index)).toString())
    const get = install((_run, before) =>
      Promise.resolve(before === null ? collaborationPageOf(big, { hasMore: true }) : collaborationPageOf(['9007199254740993', '9007199254740992', '9007199254740991'])),
    )
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '9223372036854775803'])
    expect(sequences(result.current.rows).slice(-3)).toEqual(['9007199254740993', '9007199254740992', '9007199254740991'])
    expect(result.current.failure).toBeNull()
  })

  it('sends one older request for any number of calls while it is in flight', async () => {
    const older = deferred<GetCollaborationHistoryResponse>()
    const get = install((_run, before) => (before === null ? Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true })) : older.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    const retained = result.current.loadOlder

    act(() => {
      result.current.loadOlder()
      retained()
      result.current.loadOlder()
    })

    expect(get).toHaveBeenCalledTimes(2)
    await act(async () => older.resolve(collaborationPageOf(descendingSequences(25, 21))))
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
  })

  it('keeps only its own valid rows after a failed older page and retries the same cursor', async () => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(collaborationPageOf(descendingSequences(25, 21)))
    })
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('read')
    expect(result.current.failedPage).toBe('older')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.failure).toBeNull()
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', '26'])
  })

  it.each([
    ['a repeated message', () => collaborationPageOf([], { entries: [entryFor(24, { id: 'message-30' })] })],
    ['a sequence not below the cursor', () => collaborationPageOf([26, 20])],
    ['another run', () => collaborationPageOf(descendingSequences(25, 21), { runId: 'run-2' })],
    ['an oversized page', () => collaborationPageOf(descendingSequences(10, -10))],
  ])('refuses an older page with %s, retains the earlier rows only and retries that cursor', async (_name, bad) => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true }))
      }

      return Promise.resolve(++olderCalls === 1 ? bad() : collaborationPageOf(descendingSequences(25, 21)))
    })
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('invalid')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', '26'])
  })

  it('ignores an older page that was still in flight when a reload replaced its lifetime, whatever it carries', async () => {
    const olderOfOldLifetime = deferred<GetCollaborationHistoryResponse>()
    let firstReads = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(++firstReads === 1 ? collaborationPageOf(descendingSequences(30, 26), { hasMore: true }) : collaborationPageOf([90, 89]))
      }

      return olderOfOldLifetime.promise
    })
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.loadOlder())
    expect(result.current.pending).toBe('older')

    act(() => result.current.reload())
    await waitFor(() => expect(sequences(result.current.rows)).toEqual(['90', '89']))
    await act(async () => olderOfOldLifetime.resolve(collaborationPageOf(descendingSequences(25, 21))))

    expect(sequences(result.current.rows)).toEqual(['90', '89'])
    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', null])
  })

  it('lets a reload start a fresh lifetime that a late read of the old one cannot settle, and a reload reads the newest page again', async () => {
    const stale = deferred<GetCollaborationHistoryResponse>()
    const fresh = deferred<GetCollaborationHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? stale.promise : fresh.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))

    act(() => result.current.reload())
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', null, 5)
    await act(async () => stale.resolve(collaborationPageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => fresh.resolve(collaborationPageOf([13, 12])))

    expect(sequences(result.current.rows)).toEqual(['13', '12'])
  })

  it('discards rows, errors and the pending read on close, and a reopen is a new lifetime with its own first read', async () => {
    const reopened = deferred<GetCollaborationHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? Promise.resolve(collaborationPageOf([5, 4])) : reopened.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.setOpen(false))
    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])

    act(() => result.current.setOpen(true))
    expect(result.current.rows).toEqual([])
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe('first')
    expect(get).toHaveBeenCalledTimes(2)

    await act(async () => reopened.resolve(collaborationPageOf([6, 5])))
    expect(sequences(result.current.rows)).toEqual(['6', '5'])
  })

  it('ignores the late answer of a read that was pending when the history was closed', async () => {
    const pending = deferred<GetCollaborationHistoryResponse>()
    const reopened = deferred<GetCollaborationHistoryResponse>()
    let reads = 0
    install(() => (++reads === 1 ? pending.promise : reopened.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    act(() => result.current.setOpen(false))
    act(() => result.current.setOpen(true))

    await act(async () => pending.resolve(collaborationPageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => reopened.resolve(collaborationPageOf([2, 1])))
    expect(sequences(result.current.rows)).toEqual(['2', '1'])
  })

  it('shows a replaced run closed and empty in its first frame, with no request until it is opened', async () => {
    const slowA = deferred<GetCollaborationHistoryResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(forRun(runId)([7, 6]))))
    const { result, rerender } = mounted('run-a')
    act(() => result.current.setOpen(true))
    expect(result.current.open).toBe(true)

    rerender({ runId: 'run-b' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    await act(async () => slowA.resolve(forRun('run-a')([99, 98])))
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(sequences(result.current.rows)).toEqual(['7', '6']))
    expect(get).toHaveBeenLastCalledWith('run-b', null, 5)
  })

  it('starts A fresh and closed when A returns after B, and the older A read stays ignored', async () => {
    const oldA = deferred<GetCollaborationHistoryResponse>()
    let aReads = 0
    const get = install((runId) => {
      if (runId === 'run-a') {
        return ++aReads === 1 ? oldA.promise : Promise.resolve(forRun('run-a')([30, 29]))
      }

      return Promise.resolve(forRun(runId)([7, 6]))
    })
    const { result, rerender } = mounted('run-a')
    act(() => result.current.setOpen(true))
    rerender({ runId: 'run-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(sequences(result.current.rows)).toEqual(['7', '6']))

    rerender({ runId: 'run-a' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    await act(async () => oldA.resolve(forRun('run-a')([99, 98])))
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(sequences(result.current.rows)).toEqual(['30', '29']))
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b', 'run-a'])
  })

  it('treats no selected run as nothing to read or open', () => {
    const get = install(() => Promise.resolve(collaborationPageOf([1])))
    const { result } = mounted(null)

    act(() => result.current.setOpen(true))
    act(() => result.current.reload())
    act(() => result.current.loadOlder())

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(get).not.toHaveBeenCalled()
  })

  it('starts nothing from the callbacks of an ended lifetime, whatever replaced or unmounted it', async () => {
    const get = install((runId, before) =>
      Promise.resolve(forRun(runId)(before === null ? descendingSequences(30, 26) : descendingSequences(25, 21), { hasMore: before === null })),
    )
    const { result, rerender, unmount } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))
    const ofA = { ...result.current }

    rerender({ runId: 'run-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))
    const calls = get.mock.calls.length
    act(() => {
      ofA.loadOlder()
      ofA.reload()
      ofA.setOpen(false)
    })

    expect(get).toHaveBeenCalledTimes(calls)
    expect(result.current.open).toBe(true)

    const ofB = { ...result.current }
    unmount()
    act(() => {
      ofB.loadOlder()
      ofB.reload()
      ofB.setOpen(true)
    })
    expect(get).toHaveBeenCalledTimes(calls)
  })

  it('ignores a retained loadOlder of an earlier open lifetime after a reload replaced it', async () => {
    const get = install((_run, before) =>
      Promise.resolve(before === null ? collaborationPageOf(descendingSequences(30, 26), { hasMore: true }) : collaborationPageOf(descendingSequences(25, 21))),
    )
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    const retained = result.current.loadOlder

    act(() => result.current.reload())
    await waitFor(() => expect(result.current.pending).toBeNull())
    const calls = get.mock.calls.length
    act(() => retained())

    expect(get).toHaveBeenCalledTimes(calls)
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 26)))
  })

  it('ignores setOpen, reload and loadOlder retained from an earlier run (A to B to A), so they never open or read another run', async () => {
    const get = install((runId) => Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { runId, hasMore: true })))
    const { result, rerender } = mounted('run-a')
    const stale = result.current
    act(() => stale.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    rerender({ runId: 'run-b' })
    expect(result.current.open).toBe(false)
    act(() => stale.setOpen(true))
    act(() => stale.reload())
    act(() => stale.loadOlder())

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)

    rerender({ runId: 'run-a' })
    act(() => stale.setOpen(true))

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('never reads again by itself: no timer, interval or repeated read for an unchanged open history', async () => {
    vi.useFakeTimers()
    try {
      const get = install(() => Promise.resolve(collaborationPageOf([3, 2])))
      const { result, rerender } = mounted('run-1')
      act(() => result.current.setOpen(true))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      rerender({ runId: 'run-1' })
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10 * 60 * 1000)
      })

      expect(get).toHaveBeenCalledTimes(1)
      expect(vi.getTimerCount()).toBe(0)
    } finally {
      vi.useRealTimers()
    }
  })
})
