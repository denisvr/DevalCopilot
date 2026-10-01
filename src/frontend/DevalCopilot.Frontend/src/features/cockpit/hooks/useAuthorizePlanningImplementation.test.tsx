import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, AuthorizePlanningImplementationResponse } from '../../../api/generated/api-client'
import { authorizePlanningImplementationClient } from '../../../api/clients'
import { useAuthorizePlanningImplementation } from './useAuthorizePlanningImplementation'

vi.mock('../../../api/clients', () => ({
  authorizePlanningImplementationClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

const accepted = () => new AuthorizePlanningImplementationResponse({ status: 'Authorized' })

function install(authorizePlanningImplementation: ReturnType<typeof vi.fn>) {
  vi.mocked(authorizePlanningImplementationClient).mockReturnValue({
    authorizePlanningImplementation,
  } as unknown as ReturnType<typeof authorizePlanningImplementationClient>)
}

describe('useAuthorizePlanningImplementation', () => {
  it('sends only the rationale for the given run and escalation and notifies once on success', async () => {
    const operation = vi.fn().mockResolvedValue(accepted())
    install(operation)
    const onAuthorized = vi.fn()
    const { result } = renderHook(() => useAuthorizePlanningImplementation('run-1', 'esc-1', onAuthorized))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.authorize('run-1', 'esc-1', 'My reason.')
    })

    expect(outcome).toBe(true)
    expect(operation).toHaveBeenCalledOnce()
    expect(operation.mock.calls[0][0]).toBe('run-1')
    expect(operation.mock.calls[0][1]).toBe('esc-1')
    expect(operation.mock.calls[0][2].toJSON()).toEqual({ rationale: 'My reason.' })
    expect(onAuthorized).toHaveBeenCalledOnce()
    expect(result.current.error).toBeNull()
  })

  it('tracks authorizing while in flight and refuses a duplicate submission', async () => {
    const pending = deferred<AuthorizePlanningImplementationResponse>()
    const operation = vi.fn().mockReturnValue(pending.promise)
    install(operation)
    const { result } = renderHook(() => useAuthorizePlanningImplementation('run-1', 'esc-1', vi.fn()))

    let first!: Promise<boolean>
    let second!: Promise<boolean>
    act(() => {
      first = result.current.authorize('run-1', 'esc-1', 'one')
      second = result.current.authorize('run-1', 'esc-1', 'two')
    })

    await waitFor(() => expect(result.current.authorizing).toBe(true))
    await expect(second).resolves.toBe(false)
    expect(operation).toHaveBeenCalledOnce()
    await act(async () => pending.resolve(accepted()))
    await expect(first).resolves.toBe(true)
    expect(result.current.authorizing).toBe(false)
  })

  it('refuses a foreign run or escalation without calling the server', async () => {
    const operation = vi.fn().mockResolvedValue(accepted())
    install(operation)
    const { result } = renderHook(() => useAuthorizePlanningImplementation('run-1', 'esc-1', vi.fn()))

    let outcomes: boolean[] = []
    await act(async () => {
      outcomes = [
        await result.current.authorize('run-2', 'esc-1', 'x'),
        await result.current.authorize('run-1', 'esc-2', 'x'),
      ]
    })

    expect(outcomes).toEqual([false, false])
    expect(operation).not.toHaveBeenCalled()
  })

  it('maps a refusal to fixed copy, resolves false, and does not notify', async () => {
    const operation = vi
      .fn()
      .mockRejectedValue(new ApiException('x', 409, JSON.stringify({ errors: [{ code: 'planning_authorizations.rationale_conflict', detail: 'SERVER-WORDING' }] }), {}, null))
    install(operation)
    const onAuthorized = vi.fn()
    const { result } = renderHook(() => useAuthorizePlanningImplementation('run-1', 'esc-1', onAuthorized))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.authorize('run-1', 'esc-1', 'x')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toMatch(/different reason already exists/)
    expect(result.current.error).not.toMatch(/SERVER-WORDING/)
    expect(onAuthorized).not.toHaveBeenCalled()
  })

  it('drops its state and reports an obsolete completion as not current when the escalation changes', async () => {
    const pending = deferred<AuthorizePlanningImplementationResponse>()
    const operation = vi.fn().mockReturnValue(pending.promise)
    install(operation)
    const onAuthorized = vi.fn()
    const { result, rerender } = renderHook(({ escalation }) => useAuthorizePlanningImplementation('run-1', escalation, onAuthorized), {
      initialProps: { escalation: 'esc-1' },
    })

    let request!: Promise<boolean>
    act(() => {
      request = result.current.authorize('run-1', 'esc-1', 'x')
    })
    rerender({ escalation: 'esc-2' })
    expect(result.current.authorizing).toBe(false)
    await act(async () => pending.resolve(accepted()))

    await expect(request).resolves.toBe(false)
    expect(onAuthorized).not.toHaveBeenCalled()
    expect(operation).toHaveBeenCalledOnce()
  })
})
