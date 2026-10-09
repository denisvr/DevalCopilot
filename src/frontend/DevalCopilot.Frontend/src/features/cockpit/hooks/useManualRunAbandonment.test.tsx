import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { manualRunAbandonmentClient } from '../../../api/clients'
import { GetManualRunAbandonmentResponse } from '../../../api/generated/api-client'
import { useManualRunAbandonment } from './useManualRunAbandonment'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  manualRunAbandonmentClient: vi.fn(),
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

const statusFor = (marker: string) => new GetManualRunAbandonmentResponse({ eligible: true, refusalCode: marker, latestEventSequence: 1 })

interface Props {
  runId: string | null
  sequence?: number
  generation?: number
}

function install(read: (runId: string) => Promise<GetManualRunAbandonmentResponse> = (runId) => Promise.resolve(statusFor(runId))) {
  const getManualRunAbandonment = vi.fn(read)
  vi.mocked(manualRunAbandonmentClient).mockReturnValue({ getManualRunAbandonment } as unknown as ReturnType<typeof manualRunAbandonmentClient>)
  return getManualRunAbandonment
}

function mounted(initial: Props) {
  return renderHook((props: Props) => useManualRunAbandonment(props.runId, props.sequence ?? 3, props.generation ?? 0), { initialProps: initial })
}

describe('useManualRunAbandonment', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is loading and not current until the first read settles, then current', async () => {
    const read = deferred<GetManualRunAbandonmentResponse>()
    install(() => read.promise)
    const { result } = mounted({ runId: 'run-a' })

    expect(result.current.loading).toBe(true)
    expect(result.current.current).toBe(false)

    await act(async () => read.resolve(statusFor('run-a')))

    expect(result.current.status?.refusalCode).toBe('run-a')
    expect(result.current.loading).toBe(false)
    expect(result.current.current).toBe(true)
  })

  it('reads again when the event sequence or the evidence generation changes, and is not current meanwhile', async () => {
    const get = install()
    const { result, rerender } = mounted({ runId: 'run-a', sequence: 1 })
    await waitFor(() => expect(result.current.current).toBe(true))

    const pending = deferred<GetManualRunAbandonmentResponse>()
    get.mockReturnValueOnce(pending.promise)
    rerender({ runId: 'run-a', sequence: 2 })
    expect(result.current.current).toBe(false)
    await act(async () => pending.resolve(statusFor('run-a')))
    expect(result.current.current).toBe(true)

    rerender({ runId: 'run-a', sequence: 2, generation: 1 })
    await waitFor(() => expect(get).toHaveBeenCalledTimes(3))
    await waitFor(() => expect(result.current.current).toBe(true))
  })

  it('masks the status and is not current after a failed refresh, then recovers on the next successful read', async () => {
    const get = install()
    const { result, rerender } = mounted({ runId: 'run-a', sequence: 1 })
    await waitFor(() => expect(result.current.current).toBe(true))

    get.mockRejectedValueOnce(new Error('boom'))
    rerender({ runId: 'run-a', sequence: 2 })
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.status).toBeNull()
    expect(result.current.current).toBe(false)
    expect(result.current.error).not.toContain('boom')

    rerender({ runId: 'run-a', sequence: 3 })
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.error).toBeNull()
  })

  it('ignores an older overlapping read that completes after a newer one', async () => {
    const first = deferred<GetManualRunAbandonmentResponse>()
    const second = deferred<GetManualRunAbandonmentResponse>()
    const reads = [first, second]
    const get = install(() => reads.shift()!.promise)
    const { result, rerender } = mounted({ runId: 'run-a', sequence: 1 })
    rerender({ runId: 'run-a', sequence: 2 })
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2))

    await act(async () => second.resolve(statusFor('newer')))
    await act(async () => first.resolve(statusFor('older')))

    expect(result.current.status?.refusalCode).toBe('newer')
    expect(result.current.current).toBe(true)
  })

  it('ignores a completion that belongs to a replaced run and never exposes the previous run status', async () => {
    const slow = deferred<GetManualRunAbandonmentResponse>()
    const get = install((runId) => (runId === 'run-a' ? slow.promise : Promise.resolve(statusFor(runId))))
    const { result, rerender } = mounted({ runId: 'run-a' })

    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.status?.refusalCode).toBe('run-b'))
    await act(async () => slow.resolve(statusFor('stale-a')))

    expect(result.current.status?.refusalCode).toBe('run-b')
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b'])
  })

  it('starts fresh when A returns after B: no status carried over and the older A read stays ignored', async () => {
    const oldA = deferred<GetManualRunAbandonmentResponse>()
    const newA = deferred<GetManualRunAbandonmentResponse>()
    let aReads = 0
    install((runId) => (runId === 'run-a' ? (++aReads === 1 ? oldA.promise : newA.promise) : Promise.resolve(statusFor(runId))))
    const { result, rerender } = mounted({ runId: 'run-a' })
    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.current).toBe(true))
    rerender({ runId: 'run-a' })

    expect(result.current.status).toBeNull()
    expect(result.current.current).toBe(false)
    await act(async () => oldA.resolve(statusFor('old-a')))
    expect(result.current.status).toBeNull()
    await act(async () => newA.resolve(statusFor('new-a')))
    expect(result.current.status?.refusalCode).toBe('new-a')
  })

  it('ignores a completion after unmount and makes a retained refresh inert', async () => {
    const slow = deferred<GetManualRunAbandonmentResponse>()
    const get = install(() => slow.promise)
    const { result, unmount } = mounted({ runId: 'run-a' })
    const retained = result.current.refresh
    unmount()

    await act(async () => slow.resolve(statusFor('late')))
    act(() => retained())

    expect(get).toHaveBeenCalledTimes(1)
  })

  it('reads again on refresh, and a refresh retained from a replaced run starts no read', async () => {
    const get = install()
    const { result, rerender } = mounted({ runId: 'run-a' })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.refresh

    act(() => result.current.refresh())
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2))

    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.status?.refusalCode).toBe('run-b'))
    act(() => retained())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-a', 'run-b'])
  })

  it('owns nothing and reads nothing while no run is selected', () => {
    const get = install()
    const { result } = mounted({ runId: null })

    expect(get).not.toHaveBeenCalled()
    expect(result.current.current).toBe(false)
    expect(result.current.loading).toBe(false)
  })
})
