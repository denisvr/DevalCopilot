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

    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', onRequested))

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

    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', vi.fn()))

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

    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', onRequested))

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

    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'proposal-1')
    })

    expect(result.current.error).toBe('An implementation could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })

  it('sends the guidance with the plan, and omits it for the plain request', async () => {
    const operation = vi.fn().mockResolvedValue(new RequestImplementationResponse({ attemptId: 'a', attemptNumber: 1 }))
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: operation,
    } as unknown as ReturnType<typeof requestImplementationClient>)
    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'proposal-1', 'Prefer small steps.')
      await result.current.request('run-1', 'proposal-1')
    })

    expect(operation.mock.calls[0][1]).toMatchObject({ planProposalMessageId: 'proposal-1', guidance: 'Prefer small steps.' })
    expect(operation.mock.calls[1][1].toJSON()).toEqual({ planProposalMessageId: 'proposal-1' })
  })

  it('maps the invalid-guidance refusal to fixed copy without echoing the text or the server wording', async () => {
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: vi.fn().mockRejectedValue(
        new ApiException(
          'Bad Request',
          400,
          JSON.stringify({ errors: [{ code: 'agent_attempts.direct_guidance_invalid', detail: 'SERVER-WORDING SECRET-TEXT' }] }),
          {},
          null,
        ),
      ),
    } as unknown as ReturnType<typeof requestImplementationClient>)
    const { result } = renderHook(() => useRequestImplementation('run-1', 'proposal-1', vi.fn()))

    await act(async () => {
      expect(await result.current.request('run-1', 'proposal-1', 'SECRET-TEXT')).toBe(false)
    })

    expect(result.current.error).toMatch(/non-blank text of at most 600 characters/)
    expect(result.current.error).not.toContain('SECRET-TEXT')
    expect(result.current.error).not.toContain('SERVER-WORDING')
  })

  it('rejects a handler for a replaced plan and ignores its late completion, even within the same run', async () => {
    const pending = deferred<RequestImplementationResponse>()
    const operation = vi.fn().mockReturnValue(pending.promise)
    const onRequested = vi.fn()
    vi.mocked(requestImplementationClient).mockReturnValue({
      requestImplementation: operation,
    } as unknown as ReturnType<typeof requestImplementationClient>)
    const { result, rerender } = renderHook(({ plan }) => useRequestImplementation('run-1', plan, onRequested), {
      initialProps: { plan: 'plan-A' },
    })
    const oldRequest = result.current.request
    let accepted!: Promise<boolean>
    act(() => {
      accepted = oldRequest('run-1', 'plan-A', 'guidance')
    })

    rerender({ plan: 'plan-B' })
    expect(result.current.requesting).toBe(false)
    await act(async () => {
      expect(await oldRequest('run-1', 'plan-A', 'guidance')).toBe(false)
    })
    expect(operation).toHaveBeenCalledTimes(1)

    await act(async () => {
      pending.resolve(new RequestImplementationResponse({ attemptId: 'a', attemptNumber: 1 }))
      expect(await accepted).toBe(false)
    })
    expect(onRequested).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
  })
})
