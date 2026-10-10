import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { projectRunHistoryClient } from '../../../api/clients'
import type { GetProjectRunHistoryResponse } from '../../../api/generated/api-client'
import { deliveredEntryFor, descending, entryFor, pageOf, sourceResponseFor } from '../projectRunHistoryFixture'
import { useProjectRunHistory } from './useProjectRunHistory'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
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

type Read = (projectId: string, before: number | null | undefined, limit: number | null | undefined) => Promise<GetProjectRunHistoryResponse>

function install(read: Read) {
  const getProjectRunHistory = vi.fn(read)
  vi.mocked(projectRunHistoryClient).mockReturnValue({ getProjectRunHistory } as unknown as ReturnType<typeof projectRunHistoryClient>)
  return getProjectRunHistory
}

const forProject = (projectId: string) => (executionNumbers: readonly number[], options: { hasMore?: boolean } = {}) =>
  pageOf([], { projectId, entries: executionNumbers.map((number) => entryFor(number, { projectId })), hasMore: options.hasMore ?? false, next: options.hasMore ? executionNumbers[executionNumbers.length - 1] : undefined })

const numbers = (rows: ReadonlyArray<{ executionNumber: number }>) => rows.map((row) => row.executionNumber)

function mounted(projectId: string | null) {
  return renderHook((props: { projectId: string | null }) => useProjectRunHistory(props.projectId), { initialProps: { projectId } })
}

describe('useProjectRunHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is closed by default and reads nothing until it is opened', async () => {
    const get = install(() => Promise.resolve(pageOf([1])))
    const { result } = mounted('project-1')

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with exactly one first-page request and exposes its validated rows', async () => {
    const read = deferred<GetProjectRunHistoryResponse>()
    const get = install(() => read.promise)
    const { result } = mounted('project-1')

    act(() => result.current.setOpen(true))

    expect(result.current.open).toBe(true)
    expect(result.current.pending).toBe('first')
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('project-1', null, 10)

    await act(async () => read.resolve(pageOf(descending(10, 8), { hasMore: false })))

    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(numbers(result.current.rows)).toEqual([10, 9, 8])
    expect(result.current.hasMore).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('reports an empty project as a loaded, empty, ended history', async () => {
    install(() => Promise.resolve(pageOf([])))
    const { result } = mounted('project-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.rows).toEqual([])
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
  })

  it('shows a failed first page as an explicit read failure with no rows, and only reload reads again', async () => {
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(pageOf([4, 3]))))
    const { result } = mounted('project-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('read')
    expect(result.current.rows).toEqual([])
    expect(result.current.hasMore).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.reload())
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe('first')
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(numbers(result.current.rows)).toEqual([4, 3])
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('project-1', null, 10)
  })

  it.each([
    ['another project', () => pageOf([3, 2], { projectId: 'project-2' })],
    ['an out-of-order page', () => pageOf([2, 3])],
    ['an oversized page', () => pageOf(descending(11, 1))],
    ['an incoherent continuation', () => pageOf(descending(10, 1), { hasMore: true, next: 4 })],
    ['an entry with a source of another run', () => pageOf([], { entries: [deliveredEntryFor(3, { receiptSource: sourceResponseFor(3, { runId: 'run-9' }) })] })],
  ])('refuses %s as an explicit invalid-page failure and never shows a partial page', async (_name, build) => {
    install(() => Promise.resolve(build()))
    const { result } = mounted('project-1')

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('invalid')
    expect(result.current.rows).toEqual([])
    expect(result.current.hasMore).toBe(false)
  })

  it('loads the next older page with the server cursor only, appends it and ends at the last page', async () => {
    const get = install((_project, before) =>
      Promise.resolve(before === null ? pageOf(descending(25, 16), { hasMore: true }) : before === 16 ? pageOf(descending(15, 6), { hasMore: true }) : pageOf(descending(5, 1))),
    )
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.pending).toBe('older')
    expect(numbers(result.current.rows)).toEqual(descending(25, 16))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual(descending(25, 6))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(numbers(result.current.rows)).toEqual(descending(25, 1))
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, 16, 6])
    act(() => result.current.loadOlder())
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('sends one older request for any number of calls while it is in flight', async () => {
    const older = deferred<GetProjectRunHistoryResponse>()
    const get = install((_project, before) => (before === null ? Promise.resolve(pageOf(descending(12, 3), { hasMore: true })) : older.promise))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    const retained = result.current.loadOlder

    act(() => {
      result.current.loadOlder()
      retained()
      result.current.loadOlder()
    })

    expect(get).toHaveBeenCalledTimes(2)
    await act(async () => older.resolve(pageOf(descending(2, 1))))
    expect(numbers(result.current.rows)).toEqual(descending(12, 1))
  })

  it('keeps only its own valid rows after a failed older page and retries the same cursor', async () => {
    let olderCalls = 0
    const get = install((_project, before) => {
      if (before === null) {
        return Promise.resolve(pageOf(descending(12, 3), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(pageOf(descending(2, 1)))
    })
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('read')
    expect(numbers(result.current.rows)).toEqual(descending(12, 3))
    expect(result.current.hasMore).toBe(true)

    act(() => result.current.loadOlder())
    expect(result.current.failure).toBeNull()
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(numbers(result.current.rows)).toEqual(descending(12, 1))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, 3, 3])
  })

  it.each([
    ['a repeated run', () => pageOf([], { entries: [entryFor(2, { runId: 'run-12' })] })],
    ['a run not older than the cursor', () => pageOf([], { entries: [entryFor(4), entryFor(2)] })],
    ['another project', () => pageOf(descending(2, 1), { projectId: 'project-2' })],
    ['an oversized page', () => pageOf(descending(2, -9))],
  ])('refuses an older page with %s, retains the earlier rows only and retries that cursor', async (_name, bad) => {
    let olderCalls = 0
    const get = install((_project, before) => {
      if (before === null) {
        return Promise.resolve(pageOf(descending(12, 3), { hasMore: true }))
      }

      return Promise.resolve(++olderCalls === 1 ? bad() : pageOf(descending(2, 1)))
    })
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())

    expect(result.current.failure).toBe('invalid')
    expect(numbers(result.current.rows)).toEqual(descending(12, 3))

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual(descending(12, 1))
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, 3, 3])
  })

  it('ignores an older page that was still in flight when a reload replaced its lifetime, whatever it carries', async () => {
    const olderOfOldLifetime = deferred<GetProjectRunHistoryResponse>()
    let firstReads = 0
    const get = install((_project, before) => {
      if (before === null) {
        return Promise.resolve(++firstReads === 1 ? pageOf(descending(12, 3), { hasMore: true }) : pageOf([30, 29]))
      }

      return olderOfOldLifetime.promise
    })
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.loadOlder())
    expect(result.current.pending).toBe('older')

    act(() => result.current.reload())
    await waitFor(() => expect(numbers(result.current.rows)).toEqual([30, 29]))
    await act(async () => olderOfOldLifetime.resolve(pageOf(descending(2, 1))))

    expect(numbers(result.current.rows)).toEqual([30, 29])
    expect(result.current.pending).toBeNull()
    expect(result.current.failure).toBeNull()
    expect(result.current.hasMore).toBe(false)
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, 3, null])
  })

  it('lets a reload start a fresh first-page and selection lifetime that a late read of the old one cannot settle', async () => {
    const stale = deferred<GetProjectRunHistoryResponse>()
    const fresh = deferred<GetProjectRunHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? stale.promise : fresh.promise))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))

    act(() => result.current.reload())
    expect(get).toHaveBeenCalledTimes(2)
    await act(async () => stale.resolve(pageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => fresh.resolve(pageOf([3, 2])))

    expect(numbers(result.current.rows)).toEqual([3, 2])
  })

  it('clears the selection with a reload, even for a row that is still there afterwards', async () => {
    install(() => Promise.resolve(pageOf([3, 2])))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select('run-3'))
    expect(result.current.selectedRunId).toBe('run-3')

    act(() => result.current.reload())

    expect(result.current.selectedRunId).toBeNull()
    expect(result.current.rows).toEqual([])
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.selectedRunId).toBeNull()
  })

  it('selects only a loaded row, clears on null, and keeps the selection while older rows are appended', async () => {
    install((_project, before) => Promise.resolve(before === null ? pageOf(descending(12, 3), { hasMore: true }) : pageOf(descending(2, 1))))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.select('run-404'))
    expect(result.current.selectedRunId).toBeNull()

    act(() => result.current.select('run-7'))
    expect(result.current.selectedRunId).toBe('run-7')
    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.selectedRunId).toBe('run-7')

    act(() => result.current.select(null))
    expect(result.current.selectedRunId).toBeNull()
    act(() => result.current.select('run-1'))
    expect(result.current.selectedRunId).toBe('run-1')
  })

  it('discards rows, selection and the pending read on close, and a reopen is a new lifetime with its own first read', async () => {
    const reopened = deferred<GetProjectRunHistoryResponse>()
    let reads = 0
    const get = install(() => (++reads === 1 ? Promise.resolve(pageOf([5, 4])) : reopened.promise))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select('run-5'))

    act(() => result.current.setOpen(false))
    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.selectedRunId).toBeNull()

    act(() => result.current.setOpen(true))
    expect(result.current.rows).toEqual([])
    expect(result.current.selectedRunId).toBeNull()
    expect(result.current.pending).toBe('first')
    expect(get).toHaveBeenCalledTimes(2)

    // The reopened lifetime owns the second request and shows nothing of the closed lifetime's rows or selection.
    await act(async () => reopened.resolve(pageOf([6, 5])))
    expect(numbers(result.current.rows)).toEqual([6, 5])
  })

  it('ignores the late answer of a read that was pending when the history was closed', async () => {
    const pending = deferred<GetProjectRunHistoryResponse>()
    const reopened = deferred<GetProjectRunHistoryResponse>()
    let reads = 0
    install(() => (++reads === 1 ? pending.promise : reopened.promise))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    act(() => result.current.setOpen(false))
    act(() => result.current.setOpen(true))

    await act(async () => pending.resolve(pageOf([9, 8])))

    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBe('first')
    await act(async () => reopened.resolve(pageOf([2, 1])))
    expect(numbers(result.current.rows)).toEqual([2, 1])
  })

  it('shows a replaced project closed and empty in its first frame, with no request until it is opened', async () => {
    const slowA = deferred<GetProjectRunHistoryResponse>()
    const get = install((projectId) => (projectId === 'project-a' ? slowA.promise : Promise.resolve(forProject(projectId)([7, 6]))))
    const { result, rerender } = mounted('project-a')
    act(() => result.current.setOpen(true))
    expect(result.current.open).toBe(true)

    rerender({ projectId: 'project-b' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    expect(result.current.selectedRunId).toBeNull()
    await act(async () => slowA.resolve(forProject('project-a')([99, 98])))
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(numbers(result.current.rows)).toEqual([7, 6]))
    expect(get).toHaveBeenLastCalledWith('project-b', null, 10)
  })

  it('starts A fresh and closed when A returns after B, and the older A read stays ignored', async () => {
    const oldA = deferred<GetProjectRunHistoryResponse>()
    let aReads = 0
    const get = install((projectId) => {
      if (projectId === 'project-a') {
        return ++aReads === 1 ? oldA.promise : Promise.resolve(forProject('project-a')([30, 29]))
      }

      return Promise.resolve(forProject(projectId)([7, 6]))
    })
    const { result, rerender } = mounted('project-a')
    act(() => result.current.setOpen(true))
    rerender({ projectId: 'project-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(numbers(result.current.rows)).toEqual([7, 6]))

    rerender({ projectId: 'project-a' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    await act(async () => oldA.resolve(forProject('project-a')([99, 98])))
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(numbers(result.current.rows)).toEqual([30, 29]))
    expect(get.mock.calls.map(([projectId]) => projectId)).toEqual(['project-a', 'project-b', 'project-a'])
  })

  it('treats no selected project as nothing to read or open', () => {
    const get = install(() => Promise.resolve(pageOf([1])))
    const { result } = mounted(null)

    act(() => result.current.setOpen(true))
    act(() => result.current.reload())
    act(() => result.current.loadOlder())
    act(() => result.current.select('run-1'))

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.selectedRunId).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('starts nothing from the callbacks of an ended lifetime, whatever replaced or unmounted it', async () => {
    const get = install((projectId, before) => Promise.resolve(forProject(projectId)(before === null ? descending(12, 3) : descending(2, 1), before === null ? { hasMore: true } : {})))
    const { result, rerender, unmount } = mounted('project-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual(descending(12, 3))
    const ofA = { ...result.current }

    rerender({ projectId: 'project-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual(descending(12, 3))
    const calls = get.mock.calls.length
    act(() => {
      ofA.loadOlder()
      ofA.reload()
      ofA.select('run-7')
      ofA.setOpen(false)
    })

    expect(get).toHaveBeenCalledTimes(calls)
    expect(result.current.open).toBe(true)
    expect(result.current.selectedRunId).toBeNull()

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
    const get = install((_project, before) => Promise.resolve(before === null ? pageOf(descending(12, 3), { hasMore: true }) : pageOf(descending(2, 1))))
    const { result } = mounted('project-1')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    const retained = result.current.loadOlder

    act(() => result.current.reload())
    await waitFor(() => expect(result.current.pending).toBeNull())
    const calls = get.mock.calls.length
    act(() => retained())

    expect(get).toHaveBeenCalledTimes(calls)
    expect(numbers(result.current.rows)).toEqual(descending(12, 3))
  })

  it('never reads again by itself: no timer, interval or repeated read for an unchanged open history', async () => {
    vi.useFakeTimers()
    try {
      const get = install(() => Promise.resolve(pageOf([3, 2])))
      const { result, rerender } = mounted('project-1')
      act(() => result.current.setOpen(true))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      rerender({ projectId: 'project-1' })
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

describe('useProjectRunHistory selected-row lifetime', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  async function openedWithRows() {
    install(() => Promise.resolve(pageOf([3, 2, 1])))
    const hook = mounted('project-1')
    act(() => hook.result.current.setOpen(true))
    await waitFor(() => expect(hook.result.current.pending).toBeNull())
    return hook
  }

  it('closes the current selection through the callback of that selection', async () => {
    const { result } = await openedWithRows()
    act(() => result.current.select('run-3'))

    act(() => result.current.closeSelected())

    expect(result.current.selectedRunId).toBeNull()
  })

  it('ignores the retained close of an earlier selection (A to B) and of an earlier A (A to B to A)', async () => {
    const { result } = await openedWithRows()
    act(() => result.current.select('run-3'))
    const closeFirstA = result.current.closeSelected
    act(() => result.current.select('run-2'))
    const closeB = result.current.closeSelected

    act(() => closeFirstA())
    expect(result.current.selectedRunId).toBe('run-2')

    act(() => result.current.select('run-3'))
    act(() => closeFirstA())
    act(() => closeB())
    expect(result.current.selectedRunId).toBe('run-3')

    act(() => result.current.closeSelected())
    expect(result.current.selectedRunId).toBeNull()
  })

  it('does not restart the selection when the selected run is chosen again', async () => {
    const { result } = await openedWithRows()
    act(() => result.current.select('run-3'))
    const close = result.current.closeSelected

    act(() => result.current.select('run-3'))

    expect(result.current.closeSelected).toBe(close)
    act(() => close())
    expect(result.current.selectedRunId).toBeNull()
  })

  it('ignores a retained close after a reload, a close of the history or another project replaced the selection lifetime', async () => {
    const { result, rerender } = await openedWithRows()
    act(() => result.current.select('run-3'))
    const retained = result.current.closeSelected

    act(() => result.current.reload())
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select('run-3'))
    act(() => retained())
    expect(result.current.selectedRunId).toBe('run-3')

    const second = result.current.closeSelected
    act(() => result.current.setOpen(false))
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select('run-3'))
    act(() => second())
    expect(result.current.selectedRunId).toBe('run-3')

    const third = result.current.closeSelected
    rerender({ projectId: 'project-2' })
    act(() => third())
    expect(result.current.selectedRunId).toBeNull()
  })

  it('has no close to run when nothing is selected', async () => {
    const { result } = await openedWithRows()
    const noop = result.current.closeSelected

    act(() => noop())

    expect(result.current.selectedRunId).toBeNull()
  })
})
