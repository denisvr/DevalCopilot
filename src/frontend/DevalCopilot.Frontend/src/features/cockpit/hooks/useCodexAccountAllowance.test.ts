// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CodexAccountAllowanceResponse, CodexAllowanceBucketResponse, CodexAllowanceWindowResponse } from '../../../api/generated/api-client'
import { codexAccountAllowanceClient } from '../../../api/clients'
import { useCodexAccountAllowance } from './useCodexAccountAllowance'

vi.mock('../../../api/clients', () => ({
  codexAccountAllowanceClient: vi.fn(),
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

describe('useCodexAccountAllowance', () => {
  it('reports loading before the initial request settles, then the observed snapshot', async () => {
    const pending = deferred<CodexAccountAllowanceResponse>()
    const getCodexAccountAllowance = vi.fn().mockReturnValue(pending.promise)
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    const { result } = renderHook(() => useCodexAccountAllowance())

    expect(result.current.loading).toBe(true)
    expect(result.current.allowance).toBeNull()

    const response = new CodexAccountAllowanceResponse({
      status: 'Observed',
      retrievedAtUtc: new Date('2026-09-27T12:00:00Z') as never,
      buckets: [
        new CodexAllowanceBucketResponse({
          limitId: 'codex',
          primary: new CodexAllowanceWindowResponse({ usedPercent: 42, windowDurationMins: 300, resetsAtUtc: new Date() as never }),
          secondary: undefined,
        }),
      ],
    })

    await act(async () => {
      pending.resolve(response)
      await pending.promise
    })

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.allowance?.status).toBe('Observed')
    expect(result.current.error).toBeNull()
  })

  it('reports a safe error message without leaking the underlying failure, and clears any prior observed snapshot', async () => {
    const observedResponse = new CodexAccountAllowanceResponse({
      status: 'Observed',
      retrievedAtUtc: new Date('2026-09-27T12:00:00Z') as never,
      buckets: [new CodexAllowanceBucketResponse({ primary: new CodexAllowanceWindowResponse({ usedPercent: 42 }) })],
    })
    const getCodexAccountAllowance = vi.fn().mockResolvedValueOnce(observedResponse).mockRejectedValueOnce(new Error('sensitive detail'))
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    const { result } = renderHook(() => useCodexAccountAllowance())
    await waitFor(() => expect(result.current.allowance?.status).toBe('Observed'))

    act(() => {
      result.current.refresh()
    })

    await waitFor(() => expect(result.current.error).toBe('Codex account allowance is unavailable.'))
    expect(result.current.error).not.toContain('sensitive detail')
    // A failed refresh must never leave the previous Observed status/retrieval time on screen.
    expect(result.current.allowance).toBeNull()
  })

  it('never fetches while not ready', () => {
    const getCodexAccountAllowance = vi.fn()
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    renderHook(() => useCodexAccountAllowance(false))

    expect(getCodexAccountAllowance).not.toHaveBeenCalled()
  })

  it('refresh requests a fresh snapshot', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    const { result } = renderHook(() => useCodexAccountAllowance())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(getCodexAccountAllowance).toHaveBeenCalledTimes(1)

    act(() => {
      result.current.refresh()
    })

    await waitFor(() => expect(getCodexAccountAllowance).toHaveBeenCalledTimes(2))
  })
})
