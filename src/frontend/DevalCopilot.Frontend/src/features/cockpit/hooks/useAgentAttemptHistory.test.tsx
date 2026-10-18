import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptHistoryClient } from '../../../api/clients'
import type { AgentAttemptHistoryResponse } from '../../../api/generated/api-client'
import { attemptEntryFor, attemptIdFor, attemptPageOf, descendingAttempts } from '../agentAttemptHistoryFixture'
import { useAgentAttemptHistory } from './useAgentAttemptHistory'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptHistoryClient: vi.fn(),
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

type Read = (runId: string, before: number | null | undefined, limit: number | null | undefined) => Promise<AgentAttemptHistoryResponse>

function install(read: Read) {
  const getAgentAttemptHistory = vi.fn(read)
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory } as unknown as ReturnType<typeof agentAttemptHistoryClient>)
  return getAgentAttemptHistory
}

const numbers = (rows: ReadonlyArray<{ attemptNumber?: number }>) => rows.map((row) => row.attemptNumber)

function mounted(runId: string | null) {
  return renderHook((props: { runId: string | null }) => useAgentAttemptHistory(props.runId), { initialProps: { runId } })
}

// Two pages per run: 12..3 (a full first page) and then 2..1.
const twoPages: Read = (runId, before) =>
  Promise.resolve(before === null ? attemptPageOf(runId, descendingAttempts(12, 3), true) : attemptPageOf(runId, [2, 1]))

describe('useAgentAttemptHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is closed by default, reads nothing until it is opened and then reads exactly one first page of that run', async () => {
    const get = install(twoPages)
    const { result } = mounted('run-a')

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(get).not.toHaveBeenCalled()

    act(() => result.current.setOpen(true))
    expect(result.current.pending).toBe('first')
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(get.mock.calls).toEqual([['run-a', null, 10]])
    expect(numbers(result.current.rows)).toEqual(descendingAttempts(12, 3))
    expect(result.current.hasMore).toBe(true)
  })

  it('treats no run as nothing to open or read', () => {
    const get = install(twoPages)
    const { result } = mounted(null)

    act(() => result.current.setOpen(true))

    expect(result.current.open).toBe(false)
    expect(get).not.toHaveBeenCalled()
  })

  it('sends one older request for any number of calls in the same tick, with the exact host cursor', async () => {
    const older = deferred<AgentAttemptHistoryResponse>()
    const get = install((runId, before) => (before === null ? twoPages(runId, before, 10) : older.promise))
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => {
      result.current.loadOlder()
      result.current.loadOlder()
      result.current.retry()
    })

    expect(get.mock.calls).toEqual([['run-a', null, 10], ['run-a', 3, 10]])
    older.resolve(attemptPageOf('run-a', [2, 1]))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual(descendingAttempts(12, 1))
    expect(result.current.hasMore).toBe(false)
  })

  it('keeps only its own valid rows after a failed or invalid older page and retries the same cursor on request only', async () => {
    let olderReads = 0
    const get = install((runId, before) => {
      if (before === null) {
        return twoPages(runId, before, 10)
      }

      olderReads += 1
      return olderReads === 1
        ? Promise.reject(new Error('offline'))
        : olderReads === 2
          ? Promise.resolve(attemptPageOf(runId, [5, 4]))
          : Promise.resolve(attemptPageOf(runId, [2, 1]))
    })
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    act(() => result.current.loadOlder())
    await waitFor(() => expect(result.current.failure).toBe('read'))
    expect(result.current.failedPage).toBe('older')
    expect(result.current.hasMore).toBe(false)
    act(() => result.current.loadOlder())
    expect(get).toHaveBeenCalledTimes(2)

    act(() => result.current.retry())
    await waitFor(() => expect(result.current.failure).toBe('invalid'))
    expect(numbers(result.current.rows)).toEqual(descendingAttempts(12, 3))

    act(() => result.current.retry())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(result.current.failure).toBeNull()
    expect(get.mock.calls.map((call) => call[1])).toEqual([null, 3, 3, 3])
    expect(numbers(result.current.rows)).toEqual(descendingAttempts(12, 1))
  })

  it('reads a failed first page again on retry with no rows retained', async () => {
    let reads = 0
    const get = install((runId) => (++reads === 1 ? Promise.resolve(attemptPageOf(runId, [1, 2])) : Promise.resolve(attemptPageOf(runId, [2, 1]))))
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.failure).toBe('invalid'))
    expect(result.current.failedPage).toBe('first')
    expect(result.current.rows).toEqual([])

    act(() => result.current.retry())
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(get.mock.calls.map((call) => call[1])).toEqual([null, null])
    expect(numbers(result.current.rows)).toEqual([2, 1])
  })

  it('shows a replaced run closed and empty in its first frame and ignores the late page of the earlier run', async () => {
    const slowA = deferred<AgentAttemptHistoryResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(attemptPageOf(runId, [4, 3]))))
    const { result, rerender } = mounted('run-a')
    act(() => result.current.setOpen(true))

    rerender({ runId: 'run-b' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    expect(result.current.pending).toBeNull()
    expect(result.current.selected).toBeNull()
    await act(async () => {
      slowA.resolve(attemptPageOf('run-a', [9, 8]))
      await slowA.promise
    })
    expect(result.current.rows).toEqual([])
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('starts A closed and fresh when it returns after B (A to B to A), and the first A read stays ignored', async () => {
    const staleA = deferred<AgentAttemptHistoryResponse>()
    let aReads = 0
    const get = install((runId) =>
      runId === 'run-a' && ++aReads === 1 ? staleA.promise : Promise.resolve(attemptPageOf(runId, runId === 'run-a' ? [30, 29] : [4, 3])),
    )
    const { result, rerender } = mounted('run-a')
    act(() => result.current.setOpen(true))
    rerender({ runId: 'run-b' })
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())

    rerender({ runId: 'run-a' })

    expect(result.current.open).toBe(false)
    expect(result.current.rows).toEqual([])
    await act(async () => {
      staleA.resolve(attemptPageOf('run-a', [99, 98]))
      await staleA.promise
    })
    expect(result.current.rows).toEqual([])
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    expect(numbers(result.current.rows)).toEqual([30, 29])
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b', 'run-a'])
  })

  it('discards rows and the selection on close, and a reopen is a new lifetime with its own first read', async () => {
    const get = install((runId) => Promise.resolve(attemptPageOf(runId, [2, 1])))
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.rows).toHaveLength(2))
    act(() => result.current.select(attemptIdFor('run-a', 2)))
    const firstSelection = result.current.selectionKey
    expect(firstSelection).not.toBeNull()

    act(() => result.current.setOpen(false))
    expect(result.current.rows).toEqual([])
    expect(result.current.selected).toBeNull()
    expect(result.current.selectionKey).toBeNull()

    act(() => result.current.setOpen(true))
    expect(result.current.rows).toEqual([])
    expect(result.current.selected).toBeNull()
    await waitFor(() => expect(result.current.rows).toHaveLength(2))
    expect(get).toHaveBeenCalledTimes(2)
    act(() => result.current.select(attemptIdFor('run-a', 2)))
    expect(result.current.selectionKey).not.toBe(firstSelection)
  })

  it('selects only loaded verified rows, keeps a repeated choice and makes every other choice (A to B to A) a new selection', async () => {
    install((runId) =>
      Promise.resolve({
        ...attemptPageOf(runId, []),
        items: [
          attemptEntryFor(runId, 3),
          attemptEntryFor(runId, 2, { identityValid: false, role: undefined, provider: undefined, responseContract: undefined, outcome: undefined }),
          attemptEntryFor(runId, 1),
        ],
      } as AgentAttemptHistoryResponse),
    )
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.rows).toHaveLength(3))

    act(() => result.current.select(attemptIdFor('run-a', 2)))
    act(() => result.current.select('not-loaded'))
    expect(result.current.selected).toBeNull()

    act(() => result.current.select(attemptIdFor('run-a', 3)))
    const a = result.current.selectionKey
    act(() => result.current.select(attemptIdFor('run-a', 3)))
    expect(result.current.selectionKey).toBe(a)
    act(() => result.current.select(attemptIdFor('run-a', 1)))
    act(() => result.current.select(attemptIdFor('run-a', 3)))

    expect(result.current.selected?.attemptNumber).toBe(3)
    expect(result.current.selectionKey).not.toBe(a)
  })

  it('never lets the close of an earlier selection close the one that replaced it, even a return to the same attempt', async () => {
    install((runId) => Promise.resolve(attemptPageOf(runId, [2, 1])))
    const { result } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.rows).toHaveLength(2))
    act(() => result.current.select(attemptIdFor('run-a', 2)))
    const closeFirst = result.current.closeSelected
    act(() => result.current.select(attemptIdFor('run-a', 1)))
    const closeSecond = result.current.closeSelected
    act(() => result.current.select(attemptIdFor('run-a', 2)))

    act(() => {
      closeFirst()
      closeSecond()
    })
    expect(result.current.selected?.attemptNumber).toBe(2)

    act(() => result.current.closeSelected())
    expect(result.current.selected).toBeNull()
  })

  it('starts, selects, closes and opens nothing from the callbacks of an ended run or an unmounted history', async () => {
    const get = install(twoPages)
    const { result, rerender, unmount } = mounted('run-a')
    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select(attemptIdFor('run-a', 12)))
    const ofA = { ...result.current }

    rerender({ runId: 'run-b' })
    act(() => {
      ofA.setOpen(false)
      ofA.loadOlder()
      ofA.retry()
      ofA.select(attemptIdFor('run-a', 11))
      ofA.closeSelected()
    })
    expect(result.current.open).toBe(false)
    expect(get).toHaveBeenCalledTimes(1)

    act(() => result.current.setOpen(true))
    await waitFor(() => expect(result.current.pending).toBeNull())
    act(() => result.current.select(attemptIdFor('run-b', 12)))
    const ofB = { ...result.current }
    act(() => {
      ofA.setOpen(false)
      ofA.closeSelected()
    })
    expect(result.current.open).toBe(true)
    expect(result.current.selected?.attemptId).toBe(attemptIdFor('run-b', 12))

    unmount()
    act(() => {
      ofB.loadOlder()
      ofB.retry()
      ofB.setOpen(false)
      ofB.select(attemptIdFor('run-b', 11))
      ofB.closeSelected()
    })
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('never reads again by itself: no timer or repeated read for an unchanged open history', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const get = install(twoPages)
      const { result } = mounted('run-a')
      act(() => result.current.setOpen(true))
      await waitFor(() => expect(result.current.pending).toBeNull())

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10 * 60 * 1000)
      })

      expect(get).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })
})
