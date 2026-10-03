import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { codeReviewAttemptStatusClient } from '../../../api/clients'
import { CodeReviewAttemptStatusResponse } from '../../../api/generated/api-client'
import { useCodeReviewAttemptStatus } from './useCodeReviewAttemptStatus'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  codeReviewAttemptStatusClient: vi.fn(),
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

const review = (marker: string) => new CodeReviewAttemptStatusResponse({ hasAttempt: true, attemptId: marker, executionReportMessageId: marker })

interface Props {
  runId: string | null
  generation?: number
}

function install(read: (runId: string) => Promise<CodeReviewAttemptStatusResponse> = (runId) => Promise.resolve(review(runId))) {
  const getCodeReviewAttemptStatus = vi.fn(read)
  vi.mocked(codeReviewAttemptStatusClient).mockReturnValue({ getCodeReviewAttemptStatus } as unknown as ReturnType<typeof codeReviewAttemptStatusClient>)
  return getCodeReviewAttemptStatus
}

function mounted(initial: Props) {
  const frames: { runId: string | null; marker: string | undefined; loading: boolean }[] = []
  const rendered = renderHook(
    (props: Props) => {
      const result = useCodeReviewAttemptStatus(props.runId, 3, props.generation ?? 0)
      frames.push({ runId: props.runId, marker: result.status?.attemptId, loading: result.loading })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

const readsOf = (get: ReturnType<typeof install>) => get.mock.calls.map(([runId]) => runId)

describe('useCodeReviewAttemptStatus run lifetime ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('lets the current refresh read again, once', async () => {
    const get = install()
    const { result } = mounted({ runId: 'run-a' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-a'))

    act(() => result.current.refresh())

    await waitFor(() => expect(get).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(readsOf(get)).toEqual(['run-a', 'run-a'])
  })

  it('makes a refresh retained from a replaced run inert: it starts no read for the replacement', async () => {
    const get = install()
    const { result, rerender } = mounted({ runId: 'run-a' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-a'))
    const retained = result.current.refresh

    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-b'))
    act(() => retained())

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readsOf(get)).toEqual(['run-a', 'run-b'])
    expect(result.current.loading).toBe(false)
  })

  it('keeps an old callback inert across A to B to A, while the returning A\'s own callback works', async () => {
    const get = install()
    const { result, rerender } = mounted({ runId: 'run-a' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-a'))
    const firstA = result.current.refresh
    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-b'))
    rerender({ runId: 'run-a' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-a'))
    expect(readsOf(get)).toEqual(['run-a', 'run-b', 'run-a'])

    act(() => firstA())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readsOf(get)).toEqual(['run-a', 'run-b', 'run-a'])

    act(() => result.current.refresh())
    await waitFor(() => expect(get).toHaveBeenCalledTimes(4))
    expect(readsOf(get)).toEqual(['run-a', 'run-b', 'run-a', 'run-a'])
  })

  it('starts nothing for a refresh retained past unmount', async () => {
    const get = install()
    const { result, unmount } = mounted({ runId: 'run-a' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-a'))
    const retained = result.current.refresh

    unmount()
    act(() => retained())

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(readsOf(get)).toEqual(['run-a'])
  })

  it('never lets a late answer of a replaced run, or an older overlapping answer, replace the current result', async () => {
    const lateA = deferred<CodeReviewAttemptStatusResponse>()
    const olderB = deferred<CodeReviewAttemptStatusResponse>()
    const newerB = deferred<CodeReviewAttemptStatusResponse>()
    const bReads = [Promise.resolve(review('run-b')), olderB.promise, newerB.promise]
    const get = install((runId) => (runId === 'run-a' ? lateA.promise : bReads.shift()!))
    const { result, rerender, frames } = mounted({ runId: 'run-a' })

    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.status?.attemptId).toBe('run-b'))
    await act(async () => lateA.resolve(review('late-a')))
    expect(result.current.status?.attemptId).toBe('run-b')

    act(() => result.current.refresh())
    await waitFor(() => expect(bReads).toHaveLength(1))
    act(() => result.current.refresh())
    await waitFor(() => expect(bReads).toHaveLength(0))
    await act(async () => newerB.resolve(review('newer-b')))
    await act(async () => olderB.resolve(review('older-b')))

    expect(result.current.status?.attemptId).toBe('newer-b')
    expect(result.current.loading).toBe(false)
    expect(frames.filter((frame) => frame.runId === 'run-b').some((frame) => frame.marker === 'run-a' || frame.marker === 'late-a')).toBe(false)
    expect(readsOf(get).filter((runId) => runId === 'run-a')).toHaveLength(1)
  })

  it('reports loading in the very first frame of each evidence refresh generation, and not before it', async () => {
    install()
    const { result, rerender, frames } = mounted({ runId: 'run-a', generation: 0 })
    await waitFor(() => expect(result.current.loading).toBe(false))
    const settled = frames.length

    rerender({ runId: 'run-a', generation: 1 })

    expect(frames[settled]).toMatchObject({ loading: true, marker: 'run-a' })
    await waitFor(() => expect(result.current.loading).toBe(false))
    rerender({ runId: 'run-a', generation: 1 })
    expect(result.current.loading).toBe(false)
  })

  it('reports loading, never a status, for no run', () => {
    const get = install()
    const { result } = mounted({ runId: null })

    expect(result.current.loading).toBe(false)
    expect(result.current.status).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })
})
