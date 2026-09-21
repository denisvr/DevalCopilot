// @vitest-environment jsdom
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { authorizeReviewCorrectionClient } from '../../../api/clients'
import { useAuthorizeReviewCorrection } from './useAuthorizeReviewCorrection'

vi.mock('../../../api/clients', () => ({
  authorizeReviewCorrectionClient: vi.fn(),
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

describe('useAuthorizeReviewCorrection', () => {
  beforeEach(() => vi.clearAllMocks())

  it('authorizes successfully and refreshes the current run', async () => {
    const refresh = vi.fn()
    const authorizeReviewCorrection = vi.fn().mockResolvedValue({ status: 'Authorized' })
    vi.mocked(authorizeReviewCorrectionClient).mockReturnValue({ authorizeReviewCorrection } as never)
    const { result } = renderHook(() => useAuthorizeReviewCorrection('run-1', refresh))

    await act(async () => expect(await result.current.authorize('run-1', 'escalation-1')).toBe(true))

    expect(authorizeReviewCorrection).toHaveBeenCalledWith('run-1', 'escalation-1')
    expect(refresh).toHaveBeenCalledOnce()
    expect(result.current.authorizing).toBe(false)
  })

  it('maps failures to a safe message', async () => {
    vi.mocked(authorizeReviewCorrectionClient).mockReturnValue({
      authorizeReviewCorrection: vi.fn().mockRejectedValue(new Error('raw provider path')),
    } as never)
    const { result } = renderHook(() => useAuthorizeReviewCorrection('run-1', vi.fn()))

    await act(async () => expect(await result.current.authorize('run-1', 'escalation-1')).toBe(false))
    await waitFor(() => expect(result.current.error).toBe('An additional correction could not be authorized for this run.'))
    expect(result.current.error).not.toContain('raw provider path')
  })

  it('rejects an old run before the effect or callback boundary', async () => {
    const refresh = vi.fn()
    const renders: ReturnType<typeof useAuthorizeReviewCorrection>[] = []
    function Recorder({ runId }: { runId: string }) {
      renders.push(useAuthorizeReviewCorrection(runId, refresh))
      return null
    }

    const { rerender } = render(<Recorder runId="run-1" />)
    const oldAuthorize = renders[0].authorize
    rerender(<Recorder runId="run-2" />)

    await act(async () => expect(await oldAuthorize('run-1', 'escalation-1')).toBe(false))
    expect(refresh).not.toHaveBeenCalled()
    expect(authorizeReviewCorrectionClient).not.toHaveBeenCalled()
  })

  it('ignores late completion from the previous run', async () => {
    const pending = deferred<void>()
    const refresh = vi.fn()
    vi.mocked(authorizeReviewCorrectionClient).mockReturnValue({
      authorizeReviewCorrection: vi.fn().mockReturnValue(pending.promise),
    } as never)
    const { result, rerender } = renderHook(({ runId }) => useAuthorizeReviewCorrection(runId, refresh), {
      initialProps: { runId: 'run-1' },
    })
    let requestPromise!: Promise<boolean>
    act(() => { requestPromise = result.current.authorize('run-1', 'escalation-1') })
    rerender({ runId: 'run-2' })

    await act(async () => {
      pending.resolve()
      await requestPromise
    })
    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
    expect(result.current.authorizing).toBe(false)
  })

  it('isolates repeated request generations and never persists correction data', async () => {
    const first = deferred<void>()
    const second = deferred<void>()
    const refresh = vi.fn()
    const authorizeReviewCorrection = vi.fn()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
    vi.mocked(authorizeReviewCorrectionClient).mockReturnValue({ authorizeReviewCorrection } as never)
    const { result } = renderHook(() => useAuthorizeReviewCorrection('run-1', refresh))
    let firstRequest!: Promise<boolean>
    let secondRequest!: Promise<boolean>
    act(() => { firstRequest = result.current.authorize('run-1', 'escalation-1') })
    act(() => { secondRequest = result.current.authorize('run-1', 'escalation-2') })

    await act(async () => {
      first.resolve()
      await firstRequest
      expect(refresh).not.toHaveBeenCalled()
      second.resolve()
      await secondRequest
    })
    expect(refresh).toHaveBeenCalledOnce()
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(document.cookie).toBe('')
    expect(window.location.search).toBe('')
  })
})
