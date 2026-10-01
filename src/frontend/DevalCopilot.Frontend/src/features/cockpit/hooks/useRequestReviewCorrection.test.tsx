// @vitest-environment jsdom
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException } from '../../../api/generated/api-client'
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
      renders.push(useRequestReviewCorrection(runId, 'review-1', refresh))
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

    const { result, rerender } = renderHook(({ runId }) => useRequestReviewCorrection(runId, 'review-1', refresh), {
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
    const { result } = renderHook(() => useRequestReviewCorrection('run-1', 'review-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'review-1')
    })
    await waitFor(() => expect(result.current.error).toBe('A review correction could not be requested for this run.'))
    expect(result.current.error).not.toContain('raw transport details')
  })

  it('sends the guidance with the source, and omits it for the plain request', async () => {
    const operation = vi.fn().mockResolvedValue({})
    vi.mocked(requestReviewCorrectionClient).mockReturnValue({ requestReviewCorrection: operation } as never)
    const { result } = renderHook(() => useRequestReviewCorrection('run-1', 'review-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'review-1', 'Keep it small.')
      await result.current.request('run-1', 'review-1')
    })

    expect(operation.mock.calls[0][1]).toMatchObject({ implementationReviewAttemptId: 'review-1', guidance: 'Keep it small.' })
    expect(operation.mock.calls[1][1].toJSON()).toEqual({ implementationReviewAttemptId: 'review-1' })
  })

  it.each([
    ['agent_attempts.direct_guidance_invalid', 400, /non-blank text of at most 600 characters/],
    ['agent_attempts.direct_guidance_unavailable', 409, /only within the ordinary correction budget/],
  ])('maps %s to fixed copy without echoing the server text', async (code, status, expected) => {
    vi.mocked(requestReviewCorrectionClient).mockReturnValue({
      requestReviewCorrection: vi
        .fn()
        .mockRejectedValue(new ApiException('x', status, JSON.stringify({ errors: [{ code, detail: 'SERVER-WORDING echo: SECRET-TEXT' }] }), {}, null)),
    } as never)
    const { result } = renderHook(() => useRequestReviewCorrection('run-1', 'review-1', vi.fn()))

    await act(async () => {
      expect(await result.current.request('run-1', 'review-1', 'SECRET-TEXT')).toBe(false)
    })

    expect(result.current.error).toMatch(expected)
    expect(result.current.error).not.toContain('SECRET-TEXT')
    expect(result.current.error).not.toContain('SERVER-WORDING')
  })

  it('treats a same-run source replacement as a new lifetime: the old request is rejected and its completion ignored', async () => {
    const pending = deferred<void>()
    const operation = vi.fn().mockReturnValue(pending.promise)
    const refresh = vi.fn()
    vi.mocked(requestReviewCorrectionClient).mockReturnValue({ requestReviewCorrection: operation } as never)
    const { result, rerender } = renderHook(({ source }) => useRequestReviewCorrection('run-1', source, refresh), {
      initialProps: { source: 'review-A' },
    })
    const oldRequest = result.current.request
    let accepted!: Promise<boolean>
    act(() => {
      accepted = oldRequest('run-1', 'review-A', 'guidance')
    })
    expect(operation).toHaveBeenCalledTimes(1)

    rerender({ source: 'review-B' })
    expect(result.current.requesting).toBe(false)
    await act(async () => {
      expect(await oldRequest('run-1', 'review-A', 'again')).toBe(false)
    })
    expect(operation).toHaveBeenCalledTimes(1)

    await act(async () => {
      pending.resolve()
      expect(await accepted).toBe(false)
    })
    expect(refresh).not.toHaveBeenCalled()

    rerender({ source: 'review-A' })
    expect(result.current.requesting).toBe(false)
  })
})
