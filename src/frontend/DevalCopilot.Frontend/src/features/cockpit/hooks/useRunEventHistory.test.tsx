import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { runEventHistoryClient } from '../../../api/clients'
import type { GetRunEventHistoryResponse } from '../../../api/generated/api-client'
import { descendingSequences, eventPageOf, headerFor } from '../runEventHistoryFixture'
import { useRunEventHistory } from './useRunEventHistory'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  runEventHistoryClient: vi.fn(),
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

type Read = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetRunEventHistoryResponse>

function install(read: Read) {
  const getRunEventHistory = vi.fn(read)
  vi.mocked(runEventHistoryClient).mockReturnValue({ getRunEventHistory } as unknown as ReturnType<typeof runEventHistoryClient>)
  return getRunEventHistory
}

const forRun = (runId: string) => (sequences: readonly (number | string)[], options: { hasMore?: boolean } = {}) =>
  eventPageOf(sequences, { runId, hasMore: options.hasMore ?? false })

const sequences = (rows: ReadonlyArray<{ sequence: string }>) => rows.map((row) => row.sequence)
const texts = (list: readonly number[]) => list.map(String)

function mounted(runId: string | null) {
  return renderHook((props: { runId: string | null }) => useRunEventHistory(props.runId), { initialProps: { runId } })
}

describe('useRunEventHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is closed by default and reads nothing until it is opened', () => {
    const get = install(() => Promise.resolve(eventPageOf([1])))
    const { result } = mounted('run-1')

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with exactly one newest-page request and exposes its validated rows', async () => {
    const read = deferred<GetRunEventHistoryResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))

    expect(result.current.open).toBe(true)
    expect(result.current.pending).toBe('first')
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', null, 10)

    await act(async () => read.resolve(eventPageOf([40, 12, 9])))

    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(sequences(result.current.rows)).toEqual(['40', '12', '9'])
    expect(result.current.hasMore).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('reports an empty run as a loaded, empty, ended history', async () => {
    install(() => Promise.resolve(eventPageOf([])))
    const { result } = mounted('run-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.rows).toEqual([])
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
  })

  it('shows a failed first page as an explicit read failure with no rows, and only reload reads again', async () => {
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(eventPageOf([4, 3]))))
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
    expect(get).toHaveBeenLastCalledWith('run-1', null, 10)
  })

  it.each([
    ['another run', () => eventPageOf([3, 2], { runId: 'run-2' })],
    ['an out-of-order page', () => eventPageOf([2, 3])],
    ['an oversized page', () => eventPageOf(descendingSequences(11, 1))],
    ['an incoherent continuation', () => eventPageOf(descendingSequences(10, 1), { hasMore: true, next: "4" })],
    ['a non-canonical sequence', () => eventPageOf([], { entries: [headerFor(3, { sequence: '03' })] })],
    ['an unknown event type', () => eventPageOf([], { entries: [headerFor(3, { eventType: 'run.exploded' })] })],
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
          ? eventPageOf(descendingSequences(30, 21), { hasMore: true })
          : before === '21'
            ? eventPageOf(descendingSequences(20, 11), { hasMore: true })
            : eventPageOf(descendingSequences(10, 1)),
      ),
    )
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.pending).toBe('older')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 11)))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 1)))
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', '11'])
    act(() => result.current.loadOlder())
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('sends a sequence above 2^53 as the exact cursor text and accepts the neighbors Number cannot tell apart', async () => {
    const big = Array.from({ length: 10 }, (_, index) => (9223372036854775807n - BigInt(index)).toString())
    const get = install((_run, before) =>
      Promise.resolve(before === null ? eventPageOf(big, { hasMore: true }) : eventPageOf(['9007199254740993', '9007199254740992', '9007199254740991'])),
    )
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '9223372036854775798'])
    expect(sequences(result.current.rows).slice(-3)).toEqual(['9007199254740993', '9007199254740992', '9007199254740991'])
    expect(result.current.failure).toBeNull()
  })

  it('sends one older request for any number of calls while it is in flight', async () => {
    const older = deferred<GetRunEventHistoryResponse>()
    const get = install((_run, before) => (before === null ? Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true })) : older.promise))
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
    await act(async () => older.resolve(eventPageOf(descendingSequences(20, 11))))
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 11)))
  })

  it('keeps only its own valid rows after a failed older page and retries the same cursor', async () => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(eventPageOf(descendingSequences(20, 11)))
    })
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('read')
    expect(result.current.failedPage).toBe('older')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.failure).toBeNull()
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 11)))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', '21'])
  })

  it.each([
    ['a repeated event', () => eventPageOf([], { entries: [headerFor(20, { id: 'event-30' })] })],
    ['a sequence not below the cursor', () => eventPageOf([21, 20])],
    ['another run', () => eventPageOf(descendingSequences(20, 11), { runId: 'run-2' })],
    ['an oversized page', () => eventPageOf(descendingSequences(10, -10))],
  ])('refuses an older page with %s, retains the earlier rows only and retries that cursor', async (_name, bad) => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true }))
      }

      return Promise.resolve(++olderCalls === 1 ? bad() : eventPageOf(descendingSequences(20, 11)))
    })
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('invalid')
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 11)))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', '21'])
  })

  it('ignores an older page that was still in flight when a reload replaced its lifetime, whatever it carries', async () => {
    const olderOfOldLifetime = deferred<GetRunEventHistoryResponse>()
    let firstReads = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(++firstReads === 1 ? eventPageOf(descendingSequences(30, 21), { hasMore: true }) : eventPageOf([90, 89]))
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
    await act(async () => olderOfOldLifetime.resolve(eventPageOf(descendingSequences(20, 11))))

    expect(sequences(result.current.rows)).toEqual(['90', '89'])
    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', null])
  })

  it('lets a reload start a fresh lifetime that a late read of the old one cannot settle, and a reload reads the newest page again', async () => {
    const stale = deferred<GetRunEventHistoryResponse>()
    const fresh = deferred<GetRunEventHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? stale.promise : fresh.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))

    act(() => result.current.reload())
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', null, 10)
    await act(async () => stale.resolve(eventPageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => fresh.resolve(eventPageOf([13, 12])))

    expect(sequences(result.current.rows)).toEqual(['13', '12'])
  })

  it('discards rows, errors and the pending read on close, and a reopen is a new lifetime with its own first read', async () => {
    const reopened = deferred<GetRunEventHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? Promise.resolve(eventPageOf([5, 4])) : reopened.promise))
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

    await act(async () => reopened.resolve(eventPageOf([6, 5])))
    expect(sequences(result.current.rows)).toEqual(['6', '5'])
  })

  it('ignores the late answer of a read that was pending when the history was closed', async () => {
    const pending = deferred<GetRunEventHistoryResponse>()
    const reopened = deferred<GetRunEventHistoryResponse>()
    let reads = 0
    install(() => (++reads === 1 ? pending.promise : reopened.promise))
    const { result } = mounted('run-1')
    act(() => result.current.setOpen(true))
    act(() => result.current.setOpen(false))
    act(() => result.current.setOpen(true))

    await act(async () => pending.resolve(eventPageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => reopened.resolve(eventPageOf([2, 1])))
    expect(sequences(result.current.rows)).toEqual(['2', '1'])
  })

  it('shows a replaced run closed and empty in its first frame, with no request until it is opened', async () => {
    const slowA = deferred<GetRunEventHistoryResponse>()
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
    expect(get).toHaveBeenLastCalledWith('run-b', null, 10)
  })

  it('starts A fresh and closed when A returns after B, and the older A read stays ignored', async () => {
    const oldA = deferred<GetRunEventHistoryResponse>()
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
    const get = install(() => Promise.resolve(eventPageOf([1])))
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
      Promise.resolve(forRun(runId)(before === null ? descendingSequences(30, 21) : descendingSequences(20, 11), { hasMore: before === null })),
    )
    const { result, rerender, unmount } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
    const ofA = { ...result.current }

    rerender({ runId: 'run-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
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
      Promise.resolve(before === null ? eventPageOf(descendingSequences(30, 21), { hasMore: true }) : eventPageOf(descendingSequences(20, 11))),
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
    expect(sequences(result.current.rows)).toEqual(texts(descendingSequences(30, 21)))
  })

  it('never reads again by itself: no timer, interval or repeated read for an unchanged open history', async () => {
    vi.useFakeTimers()
    try {
      const get = install(() => Promise.resolve(eventPageOf([3, 2])))
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
