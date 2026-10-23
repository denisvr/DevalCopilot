import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionAdmissionHistoryClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import {
  RUN,
  guidOf,
  notQueuedHistoryFor,
  notRecordedHistoryFor,
  recordedHistoryFor,
  stepIdFor,
  unavailableHistoryFor,
} from '../localExecutionAdmissionHistoryFixture'
import { useLocalExecutionAdmissionHistory } from './useLocalExecutionAdmissionHistory'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionAdmissionHistoryClient: vi.fn(),
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

const RUN_B = guidOf('r', 2)

function install(read?: (runId: string) => Promise<unknown>) {
  const readAdmissions = vi.fn(read ?? ((runId: string) => Promise.resolve(recordedHistoryFor(runId))))
  vi.mocked(localExecutionAdmissionHistoryClient).mockReturnValue({ readAdmissions } as unknown as ReturnType<typeof localExecutionAdmissionHistoryClient>)
  return { readAdmissions }
}

const flush = () => act(async () => undefined)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'trace' })

describe('useLocalExecutionAdmissionHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the owner opens the region, then reads the ledger exactly once', async () => {
    const { readAdmissions } = install()
    const select = vi.fn()
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))

    expect(result.current.open).toBe(false)
    expect(readAdmissions).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(readAdmissions).toHaveBeenCalledTimes(1)
    expect(readAdmissions).toHaveBeenCalledWith(RUN)
    expect(result.current.history?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(select).not.toHaveBeenCalled()
  })

  it('does nothing for a null run', async () => {
    const { readAdmissions } = install()
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(null, vi.fn()))

    act(() => result.current.show())
    await flush()

    expect(result.current.open).toBe(false)
    expect(readAdmissions).not.toHaveBeenCalled()
  })

  it('shows each closed non-Recorded status as itself', async () => {
    for (const answer of [notRecordedHistoryFor, notQueuedHistoryFor, unavailableHistoryFor]) {
      install(() => Promise.resolve(answer()))
      const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
      act(() => result.current.show())
      await flush()

      expect(result.current.history?.status).toBe(answer().status)
      expect(result.current.failure).toBeNull()
    }
  })

  it('refuses an incoherent answer, a request failure and an unknown run instead of showing anything', async () => {
    install(() => Promise.resolve({ ...recordedHistoryFor(RUN), extra: 1 }))
    const invalid = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => invalid.result.current.show())
    await flush()
    expect(invalid.result.current.failure).toBe('invalid')
    expect(invalid.result.current.history).toBeNull()

    install(() => Promise.reject(new Error('network')))
    const failed = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => failed.result.current.show())
    await flush()
    expect(failed.result.current.failure).toBe('read')

    install(() => Promise.reject(notFound()))
    const missing = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => missing.result.current.show())
    await flush()
    expect(missing.result.current.failure).toBe('not-found')

    install(() => Promise.resolve(recordedHistoryFor(RUN_B)))
    const foreign = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => foreign.result.current.show())
    await flush()
    expect(foreign.result.current.failure).toBe('invalid')
  })

  it('reloads as a new visit that ends the earlier one, once, and an unchanged render repeats nothing', async () => {
    const { readAdmissions } = install()
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => result.current.show())
    await flush()
    const firstVisit = result.current.visitKey

    act(() => result.current.reload())
    await flush()

    expect(readAdmissions).toHaveBeenCalledTimes(2)
    expect(result.current.visitKey).not.toBe(firstVisit)
    const reloader = result.current.reload
    act(() => {
      reloader()
      reloader()
    })
    await flush()
    expect(readAdmissions).toHaveBeenCalledTimes(3)
  })

  it('discards the ledger and ignores a late result when the region is closed, and a reopening starts clean', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const { readAdmissions } = install()
    readAdmissions.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))

    act(() => result.current.show())
    expect(result.current.pending).toBe(true)
    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    expect(result.current.history).toBeNull()
    act(() => result.current.show())
    expect(result.current.pending).toBe(true)

    await act(async () => first.resolve(recordedHistoryFor(RUN)))
    expect(result.current.history).toBeNull()
    expect(result.current.pending).toBe(true)

    await act(async () => second.resolve(notQueuedHistoryFor(RUN)))
    expect(result.current.history?.status).toBe('NotQueued')
    expect(readAdmissions).toHaveBeenCalledTimes(2)
  })

  it('ignores a late result of an earlier visit after a reload, even one that settles last', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const { readAdmissions } = install()
    readAdmissions.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => result.current.show())
    act(() => result.current.reload())

    await act(async () => second.resolve(notQueuedHistoryFor(RUN)))
    await act(async () => first.resolve(recordedHistoryFor(RUN)))

    expect(result.current.history?.status).toBe('NotQueued')
    expect(result.current.pending).toBe(false)
  })

  it('never shows the ledger of another run: a run change and a return (A-B-A) are new, closed lifetimes', async () => {
    const late = deferred<unknown>()
    const { readAdmissions } = install()
    readAdmissions.mockReturnValueOnce(late.promise)
    const { result, rerender } = renderHook(({ run }: { run: string }) => useLocalExecutionAdmissionHistory(run, vi.fn()), {
      initialProps: { run: RUN },
    })
    act(() => result.current.show())

    rerender({ run: RUN_B })
    expect(result.current.open).toBe(false)
    expect(result.current.history).toBeNull()
    rerender({ run: RUN })
    expect(result.current.open).toBe(false)
    await act(async () => late.resolve(recordedHistoryFor(RUN)))

    expect(result.current.history).toBeNull()
    expect(result.current.open).toBe(false)
    expect(readAdmissions).toHaveBeenCalledTimes(1)
  })

  it('settles nothing after unmounting', async () => {
    const late = deferred<unknown>()
    const { readAdmissions } = install()
    readAdmissions.mockReturnValueOnce(late.promise)
    const { result, unmount } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => result.current.show())

    unmount()
    await act(async () => late.resolve(recordedHistoryFor(RUN)))

    expect(readAdmissions).toHaveBeenCalledTimes(1)
  })

  it('lets a retained callback of an ended opening neither close, reload nor reopen a later one', async () => {
    const { readAdmissions } = install()
    const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, vi.fn()))
    act(() => result.current.show())
    await flush()
    const oldClose = result.current.close
    const oldReload = result.current.reload
    act(() => result.current.close())
    act(() => result.current.show())
    await flush()
    const reads = readAdmissions.mock.calls.length

    act(() => {
      oldClose()
      oldReload()
    })
    await flush()

    expect(result.current.open).toBe(true)
    expect(readAdmissions).toHaveBeenCalledTimes(reads)
  })

  describe('opening the evidence of a linked step', () => {
    it('delegates exactly once for a step the validated ledger of this visit links, and for nothing else', async () => {
      install()
      const select = vi.fn()
      const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))
      act(() => result.current.show())
      await flush()

      act(() => result.current.inspectLinkedStep(stepIdFor(1)))
      expect(select).toHaveBeenCalledTimes(1)
      expect(select).toHaveBeenCalledWith(stepIdFor(1))

      act(() => result.current.inspectLinkedStep(guidOf('t', 99)))
      act(() => result.current.inspectLinkedStep(stepIdFor(2)))
      expect(select).toHaveBeenCalledTimes(1)
    })

    it('does nothing before the ledger is loaded, while it is closed and after a failure', async () => {
      const pending = deferred<unknown>()
      const { readAdmissions } = install()
      readAdmissions.mockReturnValueOnce(pending.promise)
      const select = vi.fn()
      const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))

      act(() => result.current.inspectLinkedStep(stepIdFor(1)))
      act(() => result.current.show())
      act(() => result.current.inspectLinkedStep(stepIdFor(1)))
      await act(async () => pending.reject(new Error('network')))
      act(() => result.current.inspectLinkedStep(stepIdFor(1)))

      expect(select).not.toHaveBeenCalled()
    })

    it('does nothing for the steps of a ledger with no linked step or of a non-Recorded status', async () => {
      install(() => Promise.resolve(notQueuedHistoryFor(RUN)))
      const select = vi.fn()
      const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))
      act(() => result.current.show())
      await flush()

      act(() => result.current.inspectLinkedStep(stepIdFor(1)))

      expect(select).not.toHaveBeenCalled()
    })

    it('never opens evidence for another visit: a retained callback of a closed or reloaded visit does nothing even when the new visit shows the same ledger', async () => {
      install()
      const select = vi.fn()
      const { result } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))
      act(() => result.current.show())
      await flush()
      const retainedFromFirstVisit = result.current.inspectLinkedStep

      act(() => result.current.close())
      act(() => result.current.show())
      await flush()
      act(() => retainedFromFirstVisit(stepIdFor(1)))
      expect(select).not.toHaveBeenCalled()

      const retainedFromSecondVisit = result.current.inspectLinkedStep
      act(() => result.current.reload())
      await flush()
      act(() => retainedFromSecondVisit(stepIdFor(1)))
      expect(select).not.toHaveBeenCalled()

      act(() => result.current.inspectLinkedStep(stepIdFor(1)))
      expect(select).toHaveBeenCalledTimes(1)
    })

    it('never opens evidence for another run: a retained callback after a run change or a return does nothing', async () => {
      install()
      const select = vi.fn()
      const { result, rerender } = renderHook(({ run }: { run: string }) => useLocalExecutionAdmissionHistory(run, select), {
        initialProps: { run: RUN },
      })
      act(() => result.current.show())
      await flush()
      const retained = result.current.inspectLinkedStep

      rerender({ run: RUN_B })
      act(() => retained(stepIdFor(1)))
      rerender({ run: RUN })
      act(() => result.current.show())
      await flush()
      act(() => retained(stepIdFor(1)))

      expect(select).not.toHaveBeenCalled()
    })

    it('never opens evidence after unmounting', async () => {
      install()
      const select = vi.fn()
      const { result, unmount } = renderHook(() => useLocalExecutionAdmissionHistory(RUN, select))
      act(() => result.current.show())
      await flush()
      const retained = result.current.inspectLinkedStep

      unmount()
      retained(stepIdFor(1))

      expect(select).not.toHaveBeenCalled()
    })
  })
})
