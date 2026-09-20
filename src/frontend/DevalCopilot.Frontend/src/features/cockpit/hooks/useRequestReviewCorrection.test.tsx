// @vitest-environment jsdom
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { requestReviewCorrectionClient } from '../../../api/clients'
import { useRequestReviewCorrection } from './useRequestReviewCorrection'

vi.mock('../../../api/clients', () => ({
  requestReviewCorrectionClient: vi.fn(),
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

describe('useRequestReviewCorrection', () => {
  it('uses the rendered run identity before effects flush and rejects an old request', async () => {
    const refresh = vi.fn()
    const renders: ReturnType<typeof useRequestReviewCorrection>[] = []
    function Recorder({ runId }: { runId: string }) {
      renders.push(useRequestReviewCorrection(runId, refresh))
      return null
    }

    const { rerender } = render(<Recorder runId="run-1" />)
    const oldRequest = renders[0].request
    rerender(<Recorder runId="run-2" />)

    await act(async () => {
      expect(await oldRequest('run-1', 'review-1')).toBe(false)
    })

    expect(refresh).not.toHaveBeenCalled()
    expect(renders.at(-1)?.requesting).toBe(false)
    expect(renders.at(-1)?.error).toBeNull()
  })

  it('does not expose a previous run error or completion after switching runs', async () => {
    const pending = deferred<void>()
    const refresh = vi.fn()
    vi.mocked(requestReviewCorrectionClient).mockReturnValue({
      requestReviewCorrection: vi.fn().mockReturnValue(pending.promise),
    } as never)

    const { result, rerender } = renderHook(({ runId }) => useRequestReviewCorrection(runId, refresh), {
      initialProps: { runId: 'run-1' },
    })
    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'review-1')
    })
    expect(result.current.requesting).toBe(true)

    rerender({ runId: 'run-2' })
    expect(result.current.requesting).toBe(false)
    expect(result.current.error).toBeNull()

    await act(async () => {
      pending.resolve()
      await requestPromise
    })
    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
  })

  it('keeps a current-run request failure bounded and safe', async () => {
    vi.mocked(requestReviewCorrectionClient).mockReturnValue({
      requestReviewCorrection: vi.fn().mockRejectedValue(new Error('raw transport details')),
    } as never)
    const { result } = renderHook(() => useRequestReviewCorrection('run-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'review-1')
    })
    await waitFor(() => expect(result.current.error).toBe('A review correction could not be requested for this run.'))
    expect(result.current.error).not.toContain('raw transport details')
  })
})
