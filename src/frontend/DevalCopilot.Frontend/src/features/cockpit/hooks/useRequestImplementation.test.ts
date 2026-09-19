import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestImplementationResponse } from '../../../api/generated/api-client'
import { requestImplementationClient } from '../../../api/clients'
import { useRequestImplementation } from './useRequestImplementation'

vi.mock('../../../api/clients', () => ({
  requestImplementationClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('useRequestImplementation', () => {
  it('requests the attempt for the given run and plan proposal, and notifies onRequested on success', async () => {
    const requestImplementation = vi
      .fn()
      .mockResolvedValue(new RequestImplementationResponse({ attemptId: 'attempt-1', attemptNumber: 2 }))
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation,
    } as unknown as ReturnType<typeof requestImplementationClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestImplementation(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'proposal-1')
    })

    expect(requestImplementation).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestImplementation.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg).toMatchObject({ planProposalMessageId: 'proposal-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight and clears it afterward', async () => {
    const pending = deferred<RequestImplementationResponse>()
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof requestImplementationClient>)

    const { result } = renderHook(() => useRequestImplementation(vi.fn()))

    expect(result.current.requesting).toBe(false)

    let requestPromise!: Promise<boolean>
    act(() => {
      requestPromise = result.current.request('run-1', 'proposal-1')
    })

    await waitFor(() => expect(result.current.requesting).toBe(true))

    await act(async () => {
      pending.resolve(new RequestImplementationResponse({ attemptId: 'attempt-1', attemptNumber: 2 }))
      await requestPromise
    })

    expect(result.current.requesting).toBe(false)
  })

  it('extracts the safe backend-supplied conflict detail from a failed request', async () => {
    const apiException = new ApiException(
      'Conflict',
      409,
      JSON.stringify({ errors: [{ detail: 'This resolved plan already has a successful implementation.' }] }),
      {},
      null,
    )
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: vi.fn().mockRejectedValue(apiException),
    } as unknown as ReturnType<typeof requestImplementationClient>)
    const onRequested = vi.fn()

    const { result } = renderHook(() => useRequestImplementation(onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'proposal-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toBe('This resolved plan already has a successful implementation.')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to a generic message for a non-API failure, never surfacing a raw exception', async () => {
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')),
    } as unknown as ReturnType<typeof requestImplementationClient>)

    const { result } = renderHook(() => useRequestImplementation(vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'proposal-1')
    })

    expect(result.current.error).toBe('An implementation could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })
})
