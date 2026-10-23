import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionQueueInspectionClient } from '../../../api/clients'
import type { LocalExecutionQueueInspectionEntry } from '../localExecutionQueueInspection'
import { toLocalExecutionQueueInspection } from '../localExecutionQueueInspection'
import {
  emptyQueueFor,
  recordedQueueFor,
  reservedEntryFor,
  unavailableQueueFor,
  unresolvedEntryFor,
  waitingEntryFor,
} from '../localExecutionQueueInspectionFixture'
import { useLocalExecutionQueueInspection } from './useLocalExecutionQueueInspection'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionQueueInspectionClient: vi.fn(),
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

const DEFAULT = recordedQueueFor([waitingEntryFor(1), reservedEntryFor(2), unresolvedEntryFor(3)])

function install(read?: () => Promise<unknown>) {
  const readOutstanding = vi.fn(read ?? (() => Promise.resolve(DEFAULT)))
  const signals: (AbortSignal | undefined)[] = []
  vi.mocked(localExecutionQueueInspectionClient).mockImplementation((signal?: AbortSignal) => {
    signals.push(signal)
    return { readOutstanding } as unknown as ReturnType<typeof localExecutionQueueInspectionClient>
  })
  return { readOutstanding, signals }
}

const flush = () => act(async () => undefined)
const entriesOf = (raw: unknown): readonly LocalExecutionQueueInspectionEntry[] => {
  const decoded = toLocalExecutionQueueInspection(raw)
  if (decoded?.status !== 'Recorded') {
    throw new Error('expected a recorded answer')
  }

  return decoded.entries
}
const [WAITING, RESERVED, UNRESOLVED] = entriesOf(DEFAULT)

async function opened() {
  const { result, unmount } = renderHook(() => useLocalExecutionQueueInspection())
  act(() => result.current.show())
  await flush()
  return { result, unmount }
}

describe('useLocalExecutionQueueInspection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the owner opens the region, then reads the overview exactly once with a real signal', async () => {
    const { readOutstanding, signals } = install()
    const { result } = renderHook(() => useLocalExecutionQueueInspection())

    expect(result.current.open).toBe(false)
    expect(readOutstanding).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(readOutstanding).toHaveBeenCalledTimes(1)
    expect(readOutstanding).toHaveBeenCalledWith()
    expect(signals).toHaveLength(1)
    expect(signals[0]).toBeInstanceOf(AbortSignal)
    expect(signals[0]?.aborted).toBe(false)
    expect(result.current.overview?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(result.current.selection).toBeNull()
  })

  it('shows each closed non-Recorded status as itself', async () => {
    for (const answer of [emptyQueueFor, unavailableQueueFor]) {
      install(() => Promise.resolve(answer()))
      const { result } = await opened()

      expect(result.current.overview?.status).toBe(answer().status)
      expect(result.current.failure).toBeNull()
    }
  })

  it('refuses an incoherent answer and a request failure instead of showing anything', async () => {
    install(() => Promise.resolve({ ...DEFAULT, extra: 1 }))
    const invalid = await opened()
    expect(invalid.result.current.failure).toBe('invalid')
    expect(invalid.result.current.overview).toBeNull()

    install(() => Promise.reject(new Error('network')))
    const failed = await opened()
    expect(failed.result.current.failure).toBe('read')
    expect(failed.result.current.overview).toBeNull()
    expect(failed.result.current.pending).toBe(false)
  })

  it('closes without a read, aborts the request in flight and cannot be repopulated by a late answer', async () => {
    const late = deferred<unknown>()
    const { readOutstanding, signals } = install(() => late.promise)
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    act(() => result.current.show())
    await flush()
    expect(result.current.pending).toBe(true)

    act(() => result.current.close())
    await flush()

    expect(result.current.open).toBe(false)
    expect(signals[0]?.aborted).toBe(true)
    await act(async () => late.resolve(DEFAULT))
    expect(result.current.overview).toBeNull()
    expect(result.current.pending).toBe(false)
    expect(readOutstanding).toHaveBeenCalledTimes(1)
  })

  it('opens a new visit after closing: it starts pending with no row of the earlier one and reads once more', async () => {
    const reopened = deferred<unknown>()
    let call = 0
    const { readOutstanding } = install(() => (++call === 1 ? Promise.resolve(DEFAULT) : reopened.promise))
    const { result } = await opened()
    expect(result.current.overview?.status).toBe('Recorded')

    act(() => result.current.close())
    act(() => result.current.show())
    await flush()

    expect(readOutstanding).toHaveBeenCalledTimes(2)
    expect(result.current.pending).toBe(true)
    expect(result.current.overview).toBeNull()
    await act(async () => reopened.resolve(emptyQueueFor()))
    expect(result.current.overview?.status).toBe('Empty')
  })

  it('reloads as a new visit: the earlier rows and selection are gone at once, its request is aborted and its late answer is ignored', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    let call = 0
    const { readOutstanding, signals } = install(() => (++call === 1 ? first.promise : second.promise))
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    act(() => result.current.show())
    await act(async () => first.resolve(DEFAULT))
    act(() => result.current.select(WAITING))
    expect(result.current.selection?.entry.runId).toBe(WAITING.runId)

    act(() => result.current.reload())

    expect(result.current.pending).toBe(true)
    expect(result.current.overview).toBeNull()
    expect(result.current.selection).toBeNull()
    await flush()
    expect(readOutstanding).toHaveBeenCalledTimes(2)
    expect(signals[0]?.aborted).toBe(true)
    expect(signals[1]?.aborted).toBe(false)
    await act(async () => second.resolve(emptyQueueFor()))
    expect(result.current.overview?.status).toBe('Empty')
  })

  it('never shows the old rows as a fresh answer when a reload fails or is incoherent', async () => {
    let call = 0
    install(() => {
      call += 1
      return call === 1 ? Promise.resolve(DEFAULT) : call === 2 ? Promise.reject(new Error('down')) : Promise.resolve({ status: 'Recorded' })
    })
    const { result } = await opened()
    expect(result.current.overview?.status).toBe('Recorded')

    act(() => result.current.reload())
    await flush()
    expect(result.current).toMatchObject({ overview: null, failure: 'read', selection: null, pending: false })

    act(() => result.current.reload())
    await flush()
    expect(result.current).toMatchObject({ overview: null, failure: 'invalid', selection: null, pending: false })
  })

  it('ignores the late answer of a reload that was replaced by a newer one', async () => {
    const older = deferred<unknown>()
    const newer = deferred<unknown>()
    let call = 0
    install(() => (++call === 1 ? Promise.resolve(DEFAULT) : call === 2 ? older.promise : newer.promise))
    const { result } = await opened()

    act(() => result.current.reload())
    act(() => result.current.reload())
    await flush()
    await act(async () => newer.resolve(emptyQueueFor()))
    await act(async () => older.resolve(recordedQueueFor([waitingEntryFor(9)])))

    expect(result.current.overview?.status).toBe('Empty')
  })

  describe('selection', () => {
    it('selects exactly one entry of the visit and reads nothing', async () => {
      const { readOutstanding } = install()
      const { result } = await opened()
      expect(readOutstanding).toHaveBeenCalledTimes(1)

      act(() => result.current.select(RESERVED))

      expect(result.current.selection?.entry).toEqual(RESERVED)
      expect(result.current.selection?.key).toEqual(expect.stringContaining(RESERVED.admission.admissionId))
      expect(result.current.selection?.key).toEqual(expect.stringContaining(RESERVED.runId))
      expect(result.current.selection?.key).toEqual(expect.stringContaining(RESERVED.projectId))
      expect(result.current.selection?.key).toEqual(expect.stringContaining(RESERVED.sequence.sequenceId))
      expect(readOutstanding).toHaveBeenCalledTimes(1)
    })

    it('does nothing for an entry the visit does not hold exactly (admission, sequence, run or project)', async () => {
      install()
      const { result } = await opened()
      const [other] = entriesOf(recordedQueueFor([waitingEntryFor(9)]))

      for (const candidate of [
        other,
        { ...WAITING, runId: RESERVED.runId },
        { ...WAITING, projectId: RESERVED.projectId },
        { ...WAITING, sequence: RESERVED.sequence },
        { ...WAITING, admission: { ...WAITING.admission, admissionNumber: '999' } },
        { ...WAITING, admission: { ...WAITING.admission, admissionId: RESERVED.admission.admissionId } },
      ]) {
        act(() => result.current.select(candidate))
        expect(result.current.selection).toBeNull()
      }

      act(() => result.current.select(WAITING))
      expect(result.current.selection?.entry.runId).toBe(WAITING.runId)
    })

    it('selects the entry the visit holds, never the object the caller supplied', async () => {
      install()
      const { result } = await opened()
      const copy = { ...WAITING, projectName: WAITING.projectName }

      act(() => result.current.select(copy))

      const held = result.current.overview?.status === 'Recorded' ? result.current.overview.entries[0] : undefined
      expect(held).toBeDefined()
      expect(result.current.selection?.entry).toBe(held)
      expect(result.current.selection?.entry).not.toBe(copy)
    })

    it('gives every selection its own lifetime, even when the same entry is selected again (A-B-A)', async () => {
      install()
      const { result } = await opened()

      act(() => result.current.select(WAITING))
      const first = result.current.selection?.key
      act(() => result.current.select(RESERVED))
      const second = result.current.selection?.key
      act(() => result.current.select(WAITING))
      const third = result.current.selection?.key

      expect(new Set([first, second, third]).size).toBe(3)
      expect(result.current.selection?.entry).toEqual(WAITING)
    })

    it('lets a retained clear of an earlier selection do nothing to a newer one, and the current clear end exactly its own', async () => {
      install()
      const { result } = await opened()
      act(() => result.current.select(WAITING))
      const retained = result.current.clearSelection
      act(() => result.current.select(RESERVED))

      act(() => retained())
      expect(result.current.selection?.entry).toEqual(RESERVED)

      act(() => result.current.clearSelection())
      expect(result.current.selection).toBeNull()
      act(() => result.current.select(UNRESOLVED))
      expect(result.current.selection?.entry).toEqual(UNRESOLVED)
    })

    it('ends the selection with the visit: closing, reloading and unmounting leave no selection to revive', async () => {
      install()
      const { result, unmount } = await opened()
      const retainedSelect = result.current.select

      act(() => result.current.select(WAITING))
      act(() => result.current.close())
      expect(result.current.selection).toBeNull()
      act(() => result.current.show())
      await flush()
      expect(result.current.selection).toBeNull()
      act(() => retainedSelect(WAITING))
      expect(result.current.selection).toBeNull()

      const current = result.current.select
      act(() => result.current.reload())
      await flush()
      act(() => current(WAITING))
      expect(result.current.selection).toBeNull()

      unmount()
      act(() => current(WAITING))
    })

    it('lets a retained callback of an ended visit select nothing, even an entry the new visit holds identically (A-B-A)', async () => {
      install()
      const { result } = await opened()
      const retained = result.current.select

      act(() => result.current.reload())
      await flush()
      expect(result.current.overview?.status).toBe('Recorded')
      act(() => retained(WAITING))

      expect(result.current.selection).toBeNull()
    })

    it('cannot select while the visit has no validated overview', async () => {
      const pending = deferred<unknown>()
      install(() => pending.promise)
      const { result } = renderHook(() => useLocalExecutionQueueInspection())
      act(() => result.current.show())
      await flush()

      act(() => result.current.select(WAITING))

      expect(result.current.selection).toBeNull()
    })
  })

  describe('retained lifecycle callbacks', () => {
    it('lets a close from an earlier opening do nothing to a later one', async () => {
      install()
      const { result } = await opened()
      const retainedClose = result.current.close
      act(() => result.current.close())
      act(() => result.current.show())
      await flush()

      act(() => retainedClose())

      expect(result.current.open).toBe(true)
    })

    it('lets a reload from an earlier visit start no further read for a later one', async () => {
      const { readOutstanding } = install()
      const { result } = await opened()
      const retainedReload = result.current.reload
      act(() => result.current.reload())
      await flush()
      expect(readOutstanding).toHaveBeenCalledTimes(2)

      act(() => retainedReload())
      await flush()

      expect(readOutstanding).toHaveBeenCalledTimes(2)
    })

    it('starts no read after unmounting and aborts the one in flight', async () => {
      const pending = deferred<unknown>()
      const { readOutstanding, signals } = install(() => pending.promise)
      const { result, unmount } = renderHook(() => useLocalExecutionQueueInspection())
      act(() => result.current.show())
      await flush()
      const { show, reload } = result.current

      unmount()
      act(() => {
        show()
        reload()
      })
      await flush()

      expect(signals[0]?.aborted).toBe(true)
      expect(readOutstanding).toHaveBeenCalledTimes(1)
    })
  })
})
