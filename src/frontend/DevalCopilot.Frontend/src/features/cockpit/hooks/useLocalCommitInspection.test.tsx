import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localCommitInspectionClient } from '../../../api/clients'
import {
  LOCAL_COMMIT_OTHER_RUN,
  LOCAL_COMMIT_RUN,
  notRecordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionOf,
  unavailableLocalCommitInspectionFor,
} from '../localCommitInspectionFixture'
import type { LocalCommitInspectionTarget } from '../localCommitInspection'
import { useLocalCommitInspection } from './useLocalCommitInspection'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localCommitInspectionClient: vi.fn(),
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

const TARGET: LocalCommitInspectionTarget = { runId: LOCAL_COMMIT_RUN }
const KEY_A = JSON.stringify(['project-1', LOCAL_COMMIT_RUN])
const KEY_B = JSON.stringify(['project-1', LOCAL_COMMIT_OTHER_RUN])

function install(read?: (runId: string) => Promise<unknown>) {
  const readOperation = vi.fn(read ?? (() => Promise.resolve(recordedLocalCommitInspectionFor())))
  const signals: (AbortSignal | undefined)[] = []
  vi.mocked(localCommitInspectionClient).mockImplementation((signal?: AbortSignal) => {
    signals.push(signal)
    return { readOperation } as unknown as ReturnType<typeof localCommitInspectionClient>
  })
  return { readOperation, signals }
}

interface Props {
  ownerKey: string | null
  target: LocalCommitInspectionTarget | null
}

const flush = () => act(async () => undefined)

function mount(initial: Props = { ownerKey: KEY_A, target: TARGET }) {
  return renderHook(({ ownerKey, target }: Props) => useLocalCommitInspection(ownerKey, target), { initialProps: initial })
}

async function opened(initial?: Props) {
  const view = mount(initial)
  act(() => view.result.current.show())
  await flush()
  return view
}

describe('useLocalCommitInspection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the owner opens the region, then reads the operation exactly once for the frozen run with a real signal', async () => {
    const { readOperation, signals } = install()
    const { result } = mount()

    expect(result.current).toMatchObject({ open: false, pending: false, answer: null, failure: null, visitKey: null })
    expect(readOperation).not.toHaveBeenCalled()
    expect(localCommitInspectionClient).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(readOperation).toHaveBeenCalledTimes(1)
    expect(readOperation).toHaveBeenCalledWith(LOCAL_COMMIT_RUN)
    expect(signals).toHaveLength(1)
    expect(signals[0]).toBeInstanceOf(AbortSignal)
    expect(signals[0]?.aborted).toBe(false)
    expect(result.current.open).toBe(true)
    expect(result.current.answer?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(false)
  })

  it('shows each closed status as itself and every recorded operation status', async () => {
    for (const answer of [
      notRecordedLocalCommitInspectionFor(),
      unavailableLocalCommitInspectionFor(),
      ...['Prepared', 'Executing', 'Completed', 'Failed', 'Interrupted', 'NeedsAttention'].map((status) => recordedLocalCommitInspectionOf(status)),
    ]) {
      install(() => Promise.resolve(answer))
      const { result } = await opened()

      expect(result.current.answer?.status).toBe(answer.status)
      expect(result.current.failure).toBeNull()
    }
  })

  it('is pending with nothing shown until the one read settles', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result } = await opened()

    expect(result.current).toMatchObject({ open: true, pending: true, answer: null, failure: null })
    await act(async () => pending.resolve(notRecordedLocalCommitInspectionFor()))
    expect(result.current).toMatchObject({ pending: false, failure: null })
    expect(result.current.answer?.status).toBe('NotRecorded')
  })

  it('refuses an incoherent answer, a request failure, a 404 and a synchronous failure instead of showing anything', async () => {
    install(() => Promise.resolve({ ...recordedLocalCommitInspectionFor(), extra: 1 }))
    expect((await opened()).result.current).toMatchObject({ failure: 'invalid', answer: null, pending: false })

    install(() => Promise.resolve(recordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_OTHER_RUN })))
    expect((await opened()).result.current).toMatchObject({ failure: 'invalid', answer: null })

    install(() => Promise.resolve(recordedLocalCommitInspectionFor({}, { commitSha: null })))
    expect((await opened()).result.current).toMatchObject({ failure: 'invalid', answer: null })

    install(() => Promise.reject(new Error('network')))
    expect((await opened()).result.current).toMatchObject({ failure: 'read', answer: null, pending: false })

    install(() => Promise.reject(Object.assign(new Error('missing'), { status: 404 })))
    expect((await opened()).result.current).toMatchObject({ failure: 'not-found', answer: null, pending: false })

    install(() => {
      throw new Error('synchronous failure')
    })
    expect((await opened()).result.current).toMatchObject({ failure: 'read', answer: null, pending: false })
  })

  it('offers nothing to open without a target: show reads nothing and the region stays closed', async () => {
    const { readOperation } = install()
    const { result } = mount({ ownerKey: KEY_A, target: null })

    act(() => result.current.show())
    await flush()

    expect(result.current.open).toBe(false)
    expect(readOperation).not.toHaveBeenCalled()
  })

  describe('opening, closing and reloading', () => {
    it('closes without a read, aborts the request in flight and cannot be repopulated by a late answer', async () => {
      const late = deferred<unknown>()
      const { readOperation, signals } = install(() => late.promise)
      const { result } = await opened()
      expect(result.current.pending).toBe(true)

      act(() => result.current.close())
      await flush()

      expect(result.current.open).toBe(false)
      expect(signals[0]?.aborted).toBe(true)
      await act(async () => late.resolve(recordedLocalCommitInspectionFor()))
      expect(result.current).toMatchObject({ answer: null, pending: false, failure: null })
      expect(readOperation).toHaveBeenCalledTimes(1)
    })

    it('opens a new visit after closing: it starts pending with nothing of the earlier one and reads once more (no cache across visits)', async () => {
      const reopened = deferred<unknown>()
      let call = 0
      const { readOperation } = install(() => (++call === 1 ? Promise.resolve(recordedLocalCommitInspectionFor()) : reopened.promise))
      const { result } = await opened()
      const first = result.current.visitKey
      expect(result.current.answer?.status).toBe('Recorded')

      act(() => result.current.close())
      act(() => result.current.show())
      await flush()

      expect(readOperation).toHaveBeenCalledTimes(2)
      expect(result.current.visitKey).not.toBe(first)
      expect(result.current).toMatchObject({ pending: true, answer: null })
      await act(async () => reopened.resolve(notRecordedLocalCommitInspectionFor()))
      expect(result.current.answer?.status).toBe('NotRecorded')
    })

    it('reloads as a new visit: the earlier answer is gone at once, its request is aborted and its late answer is ignored', async () => {
      const first = deferred<unknown>()
      const second = deferred<unknown>()
      let call = 0
      const { readOperation, signals } = install(() => (++call === 1 ? first.promise : second.promise))
      const { result } = mount()
      act(() => result.current.show())
      await act(async () => first.resolve(recordedLocalCommitInspectionFor()))
      expect(result.current.answer?.status).toBe('Recorded')

      act(() => result.current.reload())

      expect(result.current).toMatchObject({ pending: true, answer: null, failure: null })
      await flush()
      expect(readOperation).toHaveBeenCalledTimes(2)
      expect(signals[0]?.aborted).toBe(true)
      expect(signals[1]?.aborted).toBe(false)
      await act(async () => second.resolve(unavailableLocalCommitInspectionFor()))
      expect(result.current.answer?.status).toBe('Unavailable')
    })

    it('never shows the old answer as a fresh one when a reload fails or is incoherent', async () => {
      let call = 0
      install(() => {
        call += 1
        return call === 1
          ? Promise.resolve(recordedLocalCommitInspectionFor())
          : call === 2
            ? Promise.reject(new Error('down'))
            : Promise.resolve({ status: 'Recorded' })
      })
      const { result } = await opened()
      expect(result.current.answer?.status).toBe('Recorded')

      act(() => result.current.reload())
      await flush()
      expect(result.current).toMatchObject({ answer: null, failure: 'read', pending: false })

      act(() => result.current.reload())
      await flush()
      expect(result.current).toMatchObject({ answer: null, failure: 'invalid', pending: false })
    })

    it('keeps a single flight: rapid reloads abort every earlier request, only the newest answer counts and a late older answer is ignored', async () => {
      const older = deferred<unknown>()
      const newer = deferred<unknown>()
      let call = 0
      const { readOperation, signals } = install(() =>
        ++call === 1 ? Promise.resolve(recordedLocalCommitInspectionFor()) : call === 2 ? older.promise : newer.promise,
      )
      const { result } = await opened()

      act(() => result.current.reload())
      act(() => result.current.reload())
      await flush()

      expect(readOperation).toHaveBeenCalledTimes(3)
      expect(signals.map((signal) => signal?.aborted)).toEqual([true, true, false])
      await act(async () => older.resolve(recordedLocalCommitInspectionFor()))
      expect(result.current).toMatchObject({ pending: true, answer: null })
      await act(async () => newer.resolve(notRecordedLocalCommitInspectionFor()))
      expect(result.current).toMatchObject({ pending: false })
      expect(result.current.answer?.status).toBe('NotRecorded')
    })

    it('lets a late failure of a replaced read neither clear the pending state nor show a failure for its successor', async () => {
      const older = deferred<unknown>()
      const newer = deferred<unknown>()
      let call = 0
      install(() => (++call === 1 ? older.promise : newer.promise))
      const { result } = await opened()

      act(() => result.current.reload())
      await flush()
      await act(async () => older.reject(new Error('late failure')))

      expect(result.current).toMatchObject({ pending: true, answer: null, failure: null })
      await act(async () => newer.resolve(notRecordedLocalCommitInspectionFor()))
      expect(result.current.answer?.status).toBe('NotRecorded')
    })
  })

  describe('lifetimes', () => {
    it('ends with the run it belongs to: another run shows nothing, reads nothing and a return to the first (A-B-A) is a new closed lifetime', async () => {
      const { readOperation, signals } = install()
      const view = await opened({ ownerKey: KEY_A, target: TARGET })
      expect(view.result.current.answer?.status).toBe('Recorded')

      view.rerender({ ownerKey: KEY_B, target: { runId: LOCAL_COMMIT_OTHER_RUN } })
      expect(view.result.current).toMatchObject({ open: false, answer: null, pending: false, failure: null, visitKey: null })
      await flush()
      expect(readOperation).toHaveBeenCalledTimes(1)

      view.rerender({ ownerKey: KEY_A, target: TARGET })
      expect(view.result.current).toMatchObject({ open: false, answer: null, pending: false, failure: null, visitKey: null })
      await flush()
      expect(readOperation).toHaveBeenCalledTimes(1)
      expect(signals.map((signal) => signal?.aborted)).toEqual([true])
    })

    it('ignores the late answer of a run visit that ended, even when the same run is opened again (A-B-A)', async () => {
      const late = deferred<unknown>()
      let call = 0
      const { readOperation, signals } = install(() => (++call === 1 ? late.promise : Promise.resolve(notRecordedLocalCommitInspectionFor())))
      const view = await opened({ ownerKey: KEY_A, target: TARGET })
      expect(view.result.current.pending).toBe(true)

      view.rerender({ ownerKey: KEY_B, target: { runId: LOCAL_COMMIT_OTHER_RUN } })
      view.rerender({ ownerKey: KEY_A, target: TARGET })
      act(() => view.result.current.show())
      await flush()

      expect(signals[0]?.aborted).toBe(true)
      expect(readOperation).toHaveBeenCalledTimes(2)
      await act(async () => late.resolve(recordedLocalCommitInspectionFor()))
      expect(view.result.current.answer?.status).toBe('NotRecorded')
    })

    it('ends when the project of the same run key changes, reading nothing for the replacement', async () => {
      const { readOperation } = install()
      const view = await opened({ ownerKey: KEY_A, target: TARGET })

      view.rerender({ ownerKey: JSON.stringify(['project-2', LOCAL_COMMIT_RUN]), target: TARGET })

      expect(view.result.current).toMatchObject({ open: false, answer: null, visitKey: null })
      await flush()
      expect(readOperation).toHaveBeenCalledTimes(1)
    })

    it('judges every answer of an opening against the run frozen when it was opened, even when the caller later passes another run', async () => {
      let call = 0
      const { readOperation } = install(() => Promise.resolve(++call === 1 ? recordedLocalCommitInspectionFor() : notRecordedLocalCommitInspectionFor()))
      const view = await opened({ ownerKey: KEY_A, target: TARGET })
      expect(view.result.current.answer?.status).toBe('Recorded')

      view.rerender({ ownerKey: KEY_A, target: { runId: LOCAL_COMMIT_OTHER_RUN } })
      act(() => view.result.current.reload())
      await flush()

      expect(readOperation).toHaveBeenLastCalledWith(LOCAL_COMMIT_RUN)
      expect(view.result.current.answer?.status).toBe('NotRecorded')
      expect(view.result.current.failure).toBeNull()
    })
  })

  describe('retained callbacks', () => {
    it('lets a close from an earlier opening do nothing to a later one', async () => {
      install()
      const { result } = await opened()
      const retainedClose = result.current.close
      act(() => result.current.close())
      act(() => result.current.show())
      await flush()
      const second = result.current.visitKey

      act(() => retainedClose())

      expect(result.current.open).toBe(true)
      expect(result.current.visitKey).toBe(second)
    })

    it('lets a reload from an earlier visit start no further read for a later one', async () => {
      const { readOperation } = install()
      const { result } = await opened()
      const retainedReload = result.current.reload
      act(() => result.current.reload())
      await flush()
      expect(readOperation).toHaveBeenCalledTimes(2)

      act(() => retainedReload())
      await flush()

      expect(readOperation).toHaveBeenCalledTimes(2)
    })

    it('starts no read after unmounting and aborts the one in flight', async () => {
      const pending = deferred<unknown>()
      const { readOperation, signals } = install(() => pending.promise)
      const { result, unmount } = await opened()
      const { show, reload, close } = result.current

      unmount()
      act(() => {
        show()
        reload()
        close()
      })
      await flush()

      expect(signals[0]?.aborted).toBe(true)
      expect(readOperation).toHaveBeenCalledTimes(1)
    })

    it('lets callbacks retained from an ended run visit open, reload and close nothing for the next one', async () => {
      const { readOperation } = install()
      const view = await opened({ ownerKey: KEY_A, target: TARGET })
      const { show, reload, close } = view.result.current

      view.rerender({ ownerKey: KEY_B, target: { runId: LOCAL_COMMIT_OTHER_RUN } })
      act(() => {
        show()
        reload()
        close()
      })
      await flush()

      expect(view.result.current).toMatchObject({ open: false, answer: null, visitKey: null })
      expect(readOperation).toHaveBeenCalledTimes(1)
    })
  })
})
