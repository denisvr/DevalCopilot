import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { environmentClient } from '../../../api/clients'
import { useHostCapabilityRefresh } from './useHostCapabilityRefresh'

vi.mock('../../../api/clients', () => ({
  environmentClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('useHostCapabilityRefresh', () => {
  it('calls the refresh endpoint with the capability and notifies onRefreshed on success', async () => {
    const requestHostCapabilityRefresh = vi.fn().mockResolvedValue({ nextProbeDueAtUtc: new Date() })
    vi.mocked(environmentClient).mockReturnValue({ requestHostCapabilityRefresh } as unknown as ReturnType<typeof environmentClient>)
    const onRefreshed = vi.fn()

    const { result } = renderHook(() => useHostCapabilityRefresh(onRefreshed))

    await act(async () => {
      await result.current.requestRefresh('Git')
    })

    expect(requestHostCapabilityRefresh).toHaveBeenCalledWith('Git')
    expect(onRefreshed).toHaveBeenCalledTimes(1)
  })

  it('tracks refreshingCapability while the request is in flight and clears it afterward', async () => {
    const pending = deferred<{ nextProbeDueAtUtc: Date }>()
    const requestHostCapabilityRefresh = vi.fn().mockReturnValue(pending.promise)
    vi.mocked(environmentClient).mockReturnValue({ requestHostCapabilityRefresh } as unknown as ReturnType<typeof environmentClient>)

    const { result } = renderHook(() => useHostCapabilityRefresh(vi.fn()))

    expect(result.current.refreshingCapability).toBeNull()

    let refreshPromise!: Promise<void>
    act(() => {
      refreshPromise = result.current.requestRefresh('Node')
    })

    await waitFor(() => expect(result.current.refreshingCapability).toBe('Node'))

    await act(async () => {
      pending.resolve({ nextProbeDueAtUtc: new Date() })
      await refreshPromise
    })

    expect(result.current.refreshingCapability).toBeNull()
  })

  it('clears refreshingCapability even when the request rejects', async () => {
    const requestHostCapabilityRefresh = vi.fn().mockRejectedValue(new Error('network error'))
    vi.mocked(environmentClient).mockReturnValue({ requestHostCapabilityRefresh } as unknown as ReturnType<typeof environmentClient>)

    const { result } = renderHook(() => useHostCapabilityRefresh(vi.fn()))

    await act(async () => {
      await expect(result.current.requestRefresh('Git')).rejects.toThrow('network error')
    })

    expect(result.current.refreshingCapability).toBeNull()
  })
})
