import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  projectCheckpointReviewsClient,
  projectVerificationExecutionsClient,
  recordCheckpointReviewClient,
} from '../../../api/clients'
import { CheckpointReviewResponse, VerificationExecutionResponse } from '../../../api/generated/api-client'
import { useProjectCheckpointReviews } from './useProjectCheckpointReviews'
import { useProjectVerificationExecutions } from './useProjectVerificationExecutions'

// R1: a read that is in flight is never a settled successful read. Even within a generation that already succeeded (a manual or
// retained refresh, one executions poll, the read that follows an accepted decision), the hook is not `current` until the read
// settles successfully; the cached list stays visible only as non-authoritative history.

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectVerificationExecutionsClient: vi.fn(),
  projectCheckpointReviewsClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
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

function client<T>(factory: unknown, methods: Record<string, unknown>) {
  vi.mocked(factory as () => unknown).mockReturnValue(methods as unknown as T)
}

const flush = () => act(async () => {})
const tick = (ms: number) => act(async () => {
  await vi.advanceTimersByTimeAsync(ms)
})

const execution = (id: string, status = 'Passed') =>
  new VerificationExecutionResponse({ verificationExecutionId: id, verificationCommandId: `command-of-${id}`, status, executionNumber: 1 })

const review = (id: string, isApplicable = true) =>
  new CheckpointReviewResponse({ reviewId: id, decision: 'Approved', isApplicable, checkpointNumber: 1 })

const executionIds = (list: VerificationExecutionResponse[]) => list.map((item) => item.verificationExecutionId)
const reviewIds = (list: CheckpointReviewResponse[]) => list.map((item) => item.reviewId)

// Records every committed render, so a frame claiming currency while a read was in flight cannot hide behind a later one.
function recorded<R>(useHook: () => R, pick: (result: R) => { current: boolean; loading: boolean }) {
  const frames: { current: boolean; loading: boolean }[] = []
  const rendered = renderHook(() => {
    const result = useHook()
    frames.push(pick(result))
    return result
  })
  return { ...rendered, frames }
}

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useProjectVerificationExecutions settled reads', () => {
  const useUnderTest = () => useProjectVerificationExecutions('project-a')

  it('keeps a previously Passed list as history but is not current while a same-generation refresh is pending', async () => {
    const next = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('passed')]).mockReturnValueOnce(next.promise)
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, frames } = recorded(useUnderTest, (r) => ({ current: r.current, loading: r.loading }))
    await flush()
    expect(result.current.current).toBe(true)

    let refreshing!: Promise<unknown>
    act(() => {
      refreshing = result.current.refresh()
    })
    await flush()

    expect(result.current.loading).toBe(true)
    expect(result.current.current).toBe(false)
    expect(executionIds(result.current.executions)).toEqual(['passed'])
    expect(frames.filter((frame) => frame.loading && frame.current)).toEqual([])

    await act(async () => next.resolve([execution('failed', 'Failed')]))
    await refreshing
    expect(result.current.current).toBe(true)
    expect(result.current.loading).toBe(false)
    expect(executionIds(result.current.executions)).toEqual(['failed'])
  })

  it('is not current while each poll of a running execution is pending and current once it settles', async () => {
    vi.useFakeTimers()
    const poll = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('older-passed'), execution('running', 'Running')]).mockReturnValueOnce(poll.promise)
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest)
    await tick(0)
    expect(result.current.current).toBe(true)

    await tick(1000)
    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.current).toBe(false)
    expect(executionIds(result.current.executions)).toEqual(['older-passed', 'running'])

    await act(async () => poll.resolve([execution('older-passed'), execution('running', 'Passed')]))
    expect(result.current.current).toBe(true)
    await tick(5000)
    expect(get).toHaveBeenCalledTimes(2) // the terminal answer ended the single chain
  })

  it('lets an older completion neither restore currency nor clear the newer pending read, in either order', async () => {
    for (const order of ['older first', 'newer first'] as const) {
      const older = deferred<VerificationExecutionResponse[]>()
      const newer = deferred<VerificationExecutionResponse[]>()
      const get = vi.fn().mockResolvedValueOnce([execution('initial')]).mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
      client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
      const { result, unmount } = renderHook(useUnderTest)
      await flush()
      act(() => void result.current.refresh())
      act(() => void result.current.refresh())
      await flush()
      expect(get).toHaveBeenCalledTimes(3)

      if (order === 'older first') {
        await act(async () => older.resolve([execution('older-passed')]))
        expect(result.current.current).toBe(false)
        expect(result.current.loading).toBe(true)
        expect(executionIds(result.current.executions)).toEqual(['initial'])
        await act(async () => newer.resolve([execution('newest', 'Failed')]))
      } else {
        await act(async () => newer.resolve([execution('newest', 'Failed')]))
        expect(result.current.current).toBe(true)
        await act(async () => older.resolve([execution('older-passed')]))
      }
      expect(result.current.current).toBe(true)
      expect(result.current.loading).toBe(false)
      expect(executionIds(result.current.executions)).toEqual(['newest'])
      unmount()
    }
  })

  it('is not current after a failed same-generation read and recovers on the next successful one', async () => {
    const get = vi.fn().mockResolvedValueOnce([execution('passed')]).mockRejectedValueOnce(new Error('unavailable')).mockResolvedValueOnce([execution('later')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest)
    await flush()
    await act(async () => void (await result.current.refresh()))
    expect(result.current).toMatchObject({ current: false, loading: false, readFailed: true })
    expect(executionIds(result.current.executions)).toEqual(['passed'])

    await act(async () => void (await result.current.refresh()))
    expect(result.current).toMatchObject({ current: true, loading: false, readFailed: false })
    expect(executionIds(result.current.executions)).toEqual(['later'])
  })
})

describe('useProjectCheckpointReviews settled reads', () => {
  const useUnderTest = () => useProjectCheckpointReviews('project-a')

  it('keeps the cached reviews as history but is not current while a same-generation refresh is pending', async () => {
    const next = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([review('r1')]).mockReturnValueOnce(next.promise)
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result, frames } = recorded(useUnderTest, (r) => ({ current: r.current, loading: r.loading }))
    await flush()
    expect(result.current.current).toBe(true)

    let refreshing!: Promise<unknown>
    act(() => {
      refreshing = result.current.refresh()
    })
    await flush()
    expect(result.current).toMatchObject({ current: false, loading: true })
    expect(reviewIds(result.current.reviews)).toEqual(['r1'])
    expect(frames.filter((frame) => frame.loading && frame.current)).toEqual([])

    await act(async () => next.resolve([review('r2', false)]))
    await refreshing
    expect(result.current).toMatchObject({ current: true, loading: false })
    expect(reviewIds(result.current.reviews)).toEqual(['r2'])
  })

  it('is not current during the read that follows an accepted decision, and that read failing stays a read failure of an accepted decision', async () => {
    const after = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([review('r1')]).mockReturnValueOnce(after.promise)
    const recordCheckpointReview = vi.fn().mockResolvedValue({ reviewId: 'new', decision: 'Pending' })
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    client(recordCheckpointReviewClient, { recordCheckpointReview })
    const { result } = renderHook(useUnderTest)
    await flush()

    let accepted!: Promise<boolean>
    act(() => {
      accepted = result.current.record('checkpoint-1', undefined, 'Pending', 'Human')
    })
    await flush()
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    expect(result.current).toMatchObject({ current: false, loading: true })
    expect(reviewIds(result.current.reviews)).toEqual(['r1'])

    await act(async () => after.reject(new Error('read unavailable')))
    expect(await accepted).toBe(true)
    expect(result.current).toMatchObject({ current: false, loading: false, readFailed: true, error: 'Review evidence could not be loaded.' })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('lets an older completion neither restore currency nor clear the newer pending read, in either order', async () => {
    for (const order of ['older first', 'newer first'] as const) {
      const older = deferred<CheckpointReviewResponse[]>()
      const newer = deferred<CheckpointReviewResponse[]>()
      const get = vi.fn().mockResolvedValueOnce([review('initial')]).mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
      client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
      const { result, unmount } = renderHook(useUnderTest)
      await flush()
      act(() => void result.current.refresh())
      act(() => void result.current.refresh())
      await flush()

      if (order === 'older first') {
        await act(async () => older.resolve([review('older')]))
        expect(result.current).toMatchObject({ current: false, loading: true })
        expect(reviewIds(result.current.reviews)).toEqual(['initial'])
        await act(async () => newer.resolve([review('newest')]))
      } else {
        await act(async () => newer.resolve([review('newest')]))
        expect(result.current.current).toBe(true)
        await act(async () => older.resolve([review('older')]))
      }
      expect(result.current).toMatchObject({ current: true, loading: false })
      expect(reviewIds(result.current.reviews)).toEqual(['newest'])
      unmount()
    }
  })

  it('is not current after a failed same-generation read and recovers on the next successful one', async () => {
    const get = vi.fn().mockResolvedValueOnce([review('r1')]).mockRejectedValueOnce(new Error('unavailable')).mockResolvedValueOnce([review('r3')])
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result } = renderHook(useUnderTest)
    await flush()
    await act(async () => void (await result.current.refresh()))
    expect(result.current).toMatchObject({ current: false, loading: false, readFailed: true })

    await act(async () => void (await result.current.refresh()))
    expect(result.current).toMatchObject({ current: true, loading: false, readFailed: false })
    expect(reviewIds(result.current.reviews)).toEqual(['r3'])
  })
})
