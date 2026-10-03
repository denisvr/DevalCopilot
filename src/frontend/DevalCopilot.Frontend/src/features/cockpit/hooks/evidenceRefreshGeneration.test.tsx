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

// The project's refresh generation reaches the manual review panel's two other reads. These cases pin when a read counts as
// authority for the displayed generation (first frame, pending, failed, recovered), and that the newest-read ordering, the
// per-project lifetimes, the retained callbacks and the executions hook's single polling chain are unchanged by it.

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

type Props = { id: string | null; generation: number }

// Records every committed render so a frame that claims currency before its read settled cannot hide behind a later one.
function recorded<R, F>(useHook: (props: Props) => R, initial: Props, pick: (result: R) => F) {
  const frames: { props: Props; view: F }[] = []
  const rendered = renderHook(
    (props: Props) => {
      const result = useHook(props)
      frames.push({ props, view: pick(result) })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

const execution = (id: string, status = 'Passed') =>
  new VerificationExecutionResponse({ verificationExecutionId: id, verificationCommandId: `command-of-${id}`, status, executionNumber: 1 })

const review = (id: string, isApplicable = true) =>
  new CheckpointReviewResponse({ reviewId: id, decision: 'Approved', isApplicable, checkpointNumber: 1 })

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useProjectVerificationExecutions refresh generation', () => {
  const useUnderTest = ({ id, generation }: Props) => useProjectVerificationExecutions(id, generation)
  const pick = (r: ReturnType<typeof useProjectVerificationExecutions>) => ({
    ids: r.executions.map((item) => item.verificationExecutionId),
    current: r.current,
  })

  it('is not current in the first frame and becomes current only when the read of that generation succeeds', async () => {
    const first = deferred<VerificationExecutionResponse[]>()
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: vi.fn().mockReturnValue(first.promise) })
    const { result, frames } = recorded(useUnderTest, { id: 'project-a', generation: 0 }, pick)

    expect(frames[0].view.current).toBe(false)
    await flush()
    expect(result.current.current).toBe(false)
    expect(result.current.loading).toBe(true)

    await act(async () => first.resolve([execution('e1')]))
    expect(result.current.current).toBe(true)
    expect(result.current.loading).toBe(false)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['e1'])
  })

  it('reads again when the generation advances and is not current in any committed frame until that read succeeds', async () => {
    const next = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('passed')]).mockReturnValueOnce(next.promise)
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', generation: 0 }, pick)
    await flush()
    expect(result.current.current).toBe(true)

    rerender({ id: 'project-a', generation: 1 })
    await flush()

    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['passed']) // cached, but not authority
    expect(result.current.current).toBe(false)
    expect(frames.filter((frame) => frame.props.generation === 1 && frame.view.current)).toEqual([])

    await act(async () => next.resolve([execution('failed', 'Failed')]))
    expect(result.current.current).toBe(true)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['failed'])
  })

  it('stays not current after a failed read of the present generation and recovers on the next successful one', async () => {
    const get = vi.fn().mockResolvedValueOnce([execution('passed')]).mockRejectedValueOnce(new Error('unavailable')).mockResolvedValue([execution('later')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()

    rerender({ id: 'project-a', generation: 1 })
    await flush()
    expect(result.current.current).toBe(false)
    expect(result.current.readFailed).toBe(true)
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBe('Verification status could not be loaded.')

    rerender({ id: 'project-a', generation: 2 })
    await flush()
    expect(result.current.current).toBe(true)
    expect(result.current.readFailed).toBe(false)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['later'])
  })

  it('applies only the newest of overlapping generation reads', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const newer = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('initial')]).mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    rerender({ id: 'project-a', generation: 1 })
    await flush()
    rerender({ id: 'project-a', generation: 2 })
    await flush()

    await act(async () => newer.resolve([execution('newest', 'Failed')]))
    await act(async () => older.resolve([execution('older-passed')]))

    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['newest'])
    expect(result.current.current).toBe(true)
  })

  it('does not let an older generation read that fails late spoil a newer successful one', async () => {
    const older = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('initial')]).mockReturnValueOnce(older.promise).mockResolvedValueOnce([execution('newest')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    rerender({ id: 'project-a', generation: 1 })
    await flush()
    rerender({ id: 'project-a', generation: 2 })
    await flush()
    expect(result.current.current).toBe(true)

    await act(async () => older.reject(new Error('late')))

    expect(result.current.current).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('counts a read a retained refresh starts after the generation advanced as a read of the present generation', async () => {
    const get = vi.fn().mockResolvedValueOnce([execution('initial')]).mockReturnValueOnce(new Promise(() => {})).mockResolvedValueOnce([execution('retained')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    const retained = result.current.refresh

    rerender({ id: 'project-a', generation: 1 })
    await flush()
    expect(result.current.current).toBe(false)
    await act(async () => {
      await retained()
    })

    expect(result.current.current).toBe(true)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['retained'])
  })

  it('never carries a generation, evidence or failure across A to B to A, and ignores the late read of the replaced project', async () => {
    const lateA = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn((projectId: string) =>
      projectId === 'project-a' && get.mock.calls.length === 2 ? lateA.promise : Promise.resolve([execution(`${projectId}-${get.mock.calls.length}`)]),
    )
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', generation: 3 }, pick)
    await flush()
    expect(result.current.current).toBe(true)

    rerender({ id: 'project-a', generation: 4 }) // call 2: A's read of generation 4 stays pending
    await flush()
    rerender({ id: 'project-b', generation: 0 })
    await flush()
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['project-b-3'])
    await act(async () => lateA.resolve([execution('late-a-passed')]))
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['project-b-3'])

    const from = frames.length
    rerender({ id: 'project-a', generation: 4 })
    await flush()
    expect(frames.slice(from).filter((frame) => frame.view.ids.includes('late-a-passed'))).toEqual([])
    expect(frames[from].view.current).toBe(false)
    expect(result.current.current).toBe(true)
    expect(result.current.executions.map((item) => item.verificationExecutionId)).toEqual(['project-a-4'])
  })

  it('changes nothing and requests nothing after unmount, whatever the pending reads do', async () => {
    const pending = deferred<VerificationExecutionResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([execution('initial')]).mockReturnValueOnce(pending.promise)
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {})
    const { rerender, unmount } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    rerender({ id: 'project-a', generation: 1 })
    await flush()
    unmount()

    await act(async () => pending.resolve([execution('late', 'Running')]))
    expect(get).toHaveBeenCalledTimes(2)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})

describe('useProjectVerificationExecutions refresh generation and polling', () => {
  const useUnderTest = ({ id, generation }: Props) => useProjectVerificationExecutions(id, generation)

  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('keeps one one-second chain when the generation advances while an execution is running', async () => {
    const get = vi.fn().mockResolvedValue([execution('running', 'Running')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { rerender, unmount } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await tick(0)
    await tick(1000)
    expect(get).toHaveBeenCalledTimes(2)

    rerender({ id: 'project-a', generation: 1 })
    await tick(0)
    expect(get).toHaveBeenCalledTimes(3) // the one explicit read
    get.mockClear()
    await tick(3000)
    expect(get).toHaveBeenCalledTimes(3) // exactly one chain: one request per second

    rerender({ id: 'project-a', generation: 2 })
    rerender({ id: 'project-a', generation: 3 })
    await tick(0)
    get.mockClear()
    await tick(2000)
    expect(get).toHaveBeenCalledTimes(2)

    unmount()
    get.mockClear()
    await tick(5000)
    expect(get).not.toHaveBeenCalled()
  })

  it('ends the chain on a terminal answer and a refresh requests the same single read without restarting a chain', async () => {
    const get = vi.fn().mockResolvedValue([execution('done', 'Passed')])
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await tick(0)
    await tick(5000)
    expect(get).toHaveBeenCalledTimes(1)

    rerender({ id: 'project-a', generation: 1 })
    await tick(0)
    await tick(5000)
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('stops a chain whose generation read fails', async () => {
    const get = vi.fn().mockResolvedValueOnce([execution('running', 'Running')]).mockRejectedValue(new Error('unavailable'))
    client(projectVerificationExecutionsClient, { getProjectVerificationExecutions: get })
    const { result } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await tick(0)
    await tick(1000)

    expect(result.current.current).toBe(false)
    get.mockClear()
    await tick(5000)
    expect(get).not.toHaveBeenCalled()
  })
})

describe('useProjectCheckpointReviews refresh generation', () => {
  const useUnderTest = ({ id, generation }: Props) => useProjectCheckpointReviews(id, generation)
  const pick = (r: ReturnType<typeof useProjectCheckpointReviews>) => ({
    ids: r.reviews.map((item) => item.reviewId),
    current: r.current,
  })

  it('is not current in the first frame, reads again for each generation and is current only after that read succeeds', async () => {
    const next = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([review('r1')]).mockReturnValueOnce(next.promise)
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', generation: 0 }, pick)

    expect(frames[0].view.current).toBe(false)
    await flush()
    expect(result.current.current).toBe(true)

    rerender({ id: 'project-a', generation: 1 })
    await flush()
    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['r1'])
    expect(result.current.current).toBe(false)
    expect(frames.filter((frame) => frame.props.generation === 1 && frame.view.current)).toEqual([])

    await act(async () => next.resolve([review('r2', false)]))
    expect(result.current.current).toBe(true)
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['r2'])
  })

  it('stays not current after a failed read and recovers on the next successful generation', async () => {
    const get = vi.fn().mockResolvedValueOnce([review('r1')]).mockRejectedValueOnce(new Error('unavailable')).mockResolvedValue([review('r3')])
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()

    rerender({ id: 'project-a', generation: 1 })
    await flush()
    expect(result.current.current).toBe(false)
    expect(result.current.readFailed).toBe(true)
    expect(result.current.error).toBe('Review evidence could not be loaded.')

    rerender({ id: 'project-a', generation: 2 })
    await flush()
    expect(result.current.current).toBe(true)
    expect(result.current.error).toBeNull()
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['r3'])
  })

  it('applies only the newest of overlapping generation reads', async () => {
    const older = deferred<CheckpointReviewResponse[]>()
    const newer = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([]).mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    rerender({ id: 'project-a', generation: 1 })
    await flush()
    rerender({ id: 'project-a', generation: 2 })
    await flush()

    await act(async () => newer.resolve([review('newest', false)]))
    await act(async () => older.resolve([review('older')]))

    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['newest'])
    expect(result.current.current).toBe(true)
  })

  it('does not carry a generation or reviews across A to B to A, and a read of the replaced project changes nothing', async () => {
    const lateA = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn((projectId: string) =>
      projectId === 'project-a' && get.mock.calls.length === 2 ? lateA.promise : Promise.resolve([review(`${projectId}-${get.mock.calls.length}`)]),
    )
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const { result, rerender, frames } = recorded(useUnderTest, { id: 'project-a', generation: 5 }, pick)
    await flush()
    rerender({ id: 'project-a', generation: 6 })
    await flush()
    rerender({ id: 'project-b', generation: 0 })
    await flush()
    await act(async () => lateA.resolve([review('late-a')]))
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['project-b-3'])

    const from = frames.length
    rerender({ id: 'project-a', generation: 6 })
    await flush()
    expect(frames[from].view).toEqual({ ids: [], current: false })
    expect(frames.slice(from).filter((frame) => frame.view.ids.includes('late-a'))).toEqual([])
    expect(result.current.reviews.map((item) => item.reviewId)).toEqual(['project-a-4'])
    expect(result.current.current).toBe(true)
  })

  it('never reports an accepted decision as failed when the read after it fails, and does not send it twice', async () => {
    const get = vi.fn().mockResolvedValueOnce([]).mockRejectedValueOnce(new Error('read unavailable')).mockResolvedValue([review('recorded')])
    const recordCheckpointReview = vi.fn().mockResolvedValue({ reviewId: 'recorded', decision: 'Pending' })
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    client(recordCheckpointReviewClient, { recordCheckpointReview })
    const { result } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()

    let accepted: boolean | undefined
    await act(async () => {
      accepted = await result.current.record('checkpoint-1', undefined, 'Pending', 'Human')
    })

    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    expect(accepted).toBe(true)
    expect(result.current.error).toBe('Review evidence could not be loaded.')
    expect(result.current.error).not.toContain('could not be recorded')
    expect(result.current.saving).toBe(false)
    expect(result.current.current).toBe(false)
    await tickRealTimers()
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('keeps a refused decision failed and a retained record of the replaced project requesting nothing', async () => {
    const get = vi.fn().mockResolvedValue([])
    const recordCheckpointReview = vi.fn().mockRejectedValueOnce(new Error('refused')).mockResolvedValue({ reviewId: 'x', decision: 'Pending' })
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    client(recordCheckpointReviewClient, { recordCheckpointReview })
    const { result, rerender } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    const retained = result.current.record

    await act(async () => {
      await retained('checkpoint-1', undefined, 'Pending', 'Human')
    })
    expect(result.current.error).toBe('This review decision could not be recorded.')

    rerender({ id: 'project-b', generation: 0 })
    await flush()
    recordCheckpointReview.mockClear()
    let resolved: boolean | undefined
    await act(async () => {
      resolved = await retained('checkpoint-1', undefined, 'Pending', 'Human')
    })
    expect(resolved).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
  })

  it('changes nothing after unmount, whatever the pending reads do', async () => {
    const pending = deferred<CheckpointReviewResponse[]>()
    const get = vi.fn().mockResolvedValueOnce([]).mockReturnValueOnce(pending.promise)
    client(projectCheckpointReviewsClient, { getProjectCheckpointReviews: get })
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {})
    const { rerender, unmount } = renderHook(useUnderTest, { initialProps: { id: 'project-a', generation: 0 } })
    await flush()
    rerender({ id: 'project-a', generation: 1 })
    await flush()
    unmount()

    await act(async () => pending.resolve([review('late')]))
    expect(get).toHaveBeenCalledTimes(2)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})

const tickRealTimers = () => act(async () => {
  await new Promise((resolve) => setTimeout(resolve, 20))
})
