import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localDeliveryReceiptClient } from '../../../api/clients'
import type { GetLocalDeliveryReceiptResponse } from '../../../api/generated/api-client'
import type { ReceiptSource } from '../localDeliveryReceipt'
import { availableFor, sourceFor, stateOnly } from '../localDeliveryReceiptFixture'
import { useLocalDeliveryReceipt } from './useLocalDeliveryReceipt'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localDeliveryReceiptClient: vi.fn(),
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

const sources: Record<string, ReceiptSource> = { a: sourceFor('a'), b: sourceFor('b') }

function install(read: (runId: string) => Promise<GetLocalDeliveryReceiptResponse>) {
  const getLocalDeliveryReceipt = vi.fn(read)
  vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
  return getLocalDeliveryReceipt
}

const answerFor = (runId: string) => Promise.resolve(availableFor(sources[runId.replace('run-', '')]))

const operationOf = (reading: ReturnType<typeof useLocalDeliveryReceipt>['reading']) =>
  reading?.kind === 'Available' ? reading.receipt.operationId : null

function mounted(initial: { source: ReceiptSource | null }) {
  return renderHook((props: { source: ReceiptSource | null }) => useLocalDeliveryReceipt(props.source), { initialProps: initial })
}

describe('useLocalDeliveryReceipt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is loading until its one read settles and then exposes the normalized receipt', async () => {
    const read = deferred<GetLocalDeliveryReceiptResponse>()
    const get = install(() => read.promise)
    const { result } = mounted({ source: sources.a })

    expect(result.current.loading).toBe(true)
    expect(result.current.reading).toBeNull()
    expect(result.current.failure).toBeNull()

    await act(async () => read.resolve(availableFor(sources.a)))

    expect(result.current.loading).toBe(false)
    expect(operationOf(result.current.reading)).toBe('operation-a')
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
  })

  it('reads nothing without a source and never again for an equal source or a re-render', async () => {
    const get = install(answerFor)
    const none = mounted({ source: null })
    expect(none.result.current.loading).toBe(false)
    expect(none.result.current.reading).toBeNull()
    expect(get).not.toHaveBeenCalled()

    const { result, rerender } = mounted({ source: sources.a })
    await waitFor(() => expect(result.current.loading).toBe(false))
    rerender({ source: { ...sources.a } })
    rerender({ source: { ...sources.a } })

    expect(get).toHaveBeenCalledTimes(1)
    expect(operationOf(result.current.reading)).toBe('operation-a')
  })

  it('never exposes the previous source in the first frame of a replacement and ignores its late completion', async () => {
    const slowA = deferred<GetLocalDeliveryReceiptResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : answerFor(runId)))
    const { result, rerender } = mounted({ source: sources.a })

    rerender({ source: sources.b })

    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-b'))
    await act(async () => slowA.resolve(availableFor(sources.a)))

    expect(operationOf(result.current.reading)).toBe('operation-b')
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b'])
  })

  it('starts fresh when A returns after B: nothing is carried over and the older A read stays ignored', async () => {
    const oldA = deferred<GetLocalDeliveryReceiptResponse>()
    const newA = deferred<GetLocalDeliveryReceiptResponse>()
    let aReads = 0
    install((runId) => (runId === 'run-a' ? (++aReads === 1 ? oldA.promise : newA.promise) : answerFor(runId)))
    const { result, rerender } = mounted({ source: sources.a })
    rerender({ source: sources.b })
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-b'))

    rerender({ source: sources.a })

    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(true)
    await act(async () => oldA.resolve(availableFor({ ...sources.a, commitSha: 'e'.repeat(40) })))
    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(true)
    await act(async () => newA.resolve(availableFor(sources.a)))
    expect(operationOf(result.current.reading)).toBe('operation-a')
  })

  it('drops everything when the source becomes null and starts a new lifetime when it returns', async () => {
    const get = install(answerFor)
    const { result, rerender } = mounted({ source: sources.a })
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-a'))

    rerender({ source: null })
    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(false)

    rerender({ source: sources.a })
    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-a'))
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('lets only the newest of overlapping reads decide', async () => {
    const first = deferred<GetLocalDeliveryReceiptResponse>()
    const second = deferred<GetLocalDeliveryReceiptResponse>()
    const reads = [first, second]
    const get = install(() => reads.shift()!.promise)
    const { result } = mounted({ source: sources.a })
    await waitFor(() => expect(get).toHaveBeenCalledTimes(1))

    act(() => result.current.reload())
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2))
    await act(async () => second.resolve(availableFor(sources.a)))
    await act(async () => first.reject(new Error('older failure')))

    expect(operationOf(result.current.reading)).toBe('operation-a')
    expect(result.current.failure).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('reports a failed read without echoing its text, never retries by itself and recovers only on an explicit reload', async () => {
    const get = install(() => Promise.reject(new Error('secret server detail')))
    const { result } = mounted({ source: sources.a })

    await waitFor(() => expect(result.current.failure).toBe('read'))
    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(false)
    expect(JSON.stringify(result.current)).not.toContain('secret server detail')
    await new Promise((resolve) => setTimeout(resolve, 30))
    expect(get).toHaveBeenCalledTimes(1)

    get.mockImplementationOnce(() => answerFor('run-a'))
    act(() => result.current.reload())
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-a'))
    expect(result.current.failure).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('treats a response for another run, operation or commit as inconsistent and shows nothing from it', async () => {
    install(() => Promise.resolve(availableFor({ ...sources.a, operationId: 'operation-other' })))
    const { result } = mounted({ source: sources.a })

    await waitFor(() => expect(result.current.failure).toBe('inconsistent'))
    expect(result.current.reading).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('treats a contradictory state as inconsistent and keeps a recorded unavailable reading', async () => {
    const get = install(() => Promise.resolve(stateOnly('NotRecorded')))
    const first = mounted({ source: sources.a })
    await waitFor(() => expect(first.result.current.failure).toBe('inconsistent'))

    get.mockImplementation(() => Promise.resolve(stateOnly('Unavailable')))
    const second = mounted({ source: sources.b })
    await waitFor(() => expect(second.result.current.reading).toEqual({ kind: 'Unavailable' }))
    expect(second.result.current.failure).toBeNull()
  })

  it('starts nothing for a reload retained from a replaced lifetime or after unmount, and ignores a late completion', async () => {
    const pending = deferred<GetLocalDeliveryReceiptResponse>()
    const get = install((runId) => (runId === 'run-a' ? pending.promise : answerFor(runId)))
    const { result, rerender, unmount } = mounted({ source: sources.a })
    const retained = result.current.reload

    rerender({ source: sources.b })
    await waitFor(() => expect(operationOf(result.current.reading)).toBe('operation-b'))
    const callsBefore = get.mock.calls.length
    act(() => retained())
    await act(async () => pending.resolve(availableFor(sources.a)))
    expect(get.mock.calls.length).toBe(callsBefore)
    expect(operationOf(result.current.reading)).toBe('operation-b')

    const live = result.current.reload
    unmount()
    act(() => live())
    expect(get.mock.calls.length).toBe(callsBefore)
  })
})
