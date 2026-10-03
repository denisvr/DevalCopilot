// @vitest-environment jsdom
import { act, render, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestDiagnosisCorrectionResponse } from '../../../api/generated/api-client'
import { requestDiagnosisCorrectionClient } from '../../../api/clients'
import { useRequestDiagnosisCorrection } from './useRequestDiagnosisCorrection'

vi.mock('../../../api/clients', () => ({
  requestDiagnosisCorrectionClient: vi.fn(),
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

function problem(code: string, detail = 'server wording') {
  return new ApiException('Conflict', 409, JSON.stringify({ errors: [{ code, detail }] }), {}, null)
}

function mockClient(requestDiagnosisCorrection: ReturnType<typeof vi.fn>) {
  vi.mocked(requestDiagnosisCorrectionClient).mockReturnValue({
    requestDiagnosisCorrection,
  } as unknown as ReturnType<typeof requestDiagnosisCorrectionClient>)
}

describe('useRequestDiagnosisCorrection', () => {
  it('sends only the exact run and diagnosis attempt identifiers and refreshes on success', async () => {
    const requestDiagnosisCorrection = vi
      .fn()
      .mockResolvedValue(new RequestDiagnosisCorrectionResponse({ status: 'AttemptCreated', attemptId: 'correction-1', attemptNumber: 1 }))
    mockClient(requestDiagnosisCorrection)
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'diagnosis-1')
    })

    expect(requestDiagnosisCorrection).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestDiagnosisCorrection.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg.toJSON()).toEqual({ verificationDiagnosisAttemptId: 'diagnosis-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
  })

  it('sends the raw guidance beside the diagnosis attempt, and nothing when there is none', async () => {
    const requestDiagnosisCorrection = vi.fn().mockResolvedValue(new RequestDiagnosisCorrectionResponse({ status: 'AttemptCreated' }))
    mockClient(requestDiagnosisCorrection)
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1', '  Keep it small.  ')
    })
    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1')
    })

    expect(requestDiagnosisCorrection.mock.calls[0][1].toJSON()).toEqual({
      verificationDiagnosisAttemptId: 'diagnosis-1',
      guidance: '  Keep it small.  ',
    })
    expect(requestDiagnosisCorrection.mock.calls[1][1].toJSON()).toEqual({ verificationDiagnosisAttemptId: 'diagnosis-1' })
  })

  it('maps the two guidance refusals to fixed copy that echoes neither the text nor the server wording', async () => {
    const requestDiagnosisCorrection = vi
      .fn()
      .mockRejectedValueOnce(problem('agent_attempts.direct_guidance_invalid', 'SERVER-WORDING SECRET'))
      .mockRejectedValueOnce(problem('agent_attempts.direct_guidance_unavailable', 'SERVER-WORDING SECRET'))
    mockClient(requestDiagnosisCorrection)
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1', 'SECRET')
    })
    const invalid = result.current.error
    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1', 'SECRET')
    })
    const unavailable = result.current.error

    expect(invalid).toMatch(/The guidance was not accepted/)
    expect(unavailable).toMatch(/only within the ordinary correction budget/)
    expect(`${invalid}${unavailable}`).not.toMatch(/SECRET|SERVER-WORDING/)
  })

  it('treats a recorded escalation as an accepted request and refreshes', async () => {
    mockClient(
      vi.fn().mockResolvedValue(
        new RequestDiagnosisCorrectionResponse({ status: 'Escalated', escalationId: 'escalation-1', escalationMessageId: 'message-1' }),
      ),
    )
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', onRequested))

    await act(async () => {
      expect(await result.current.request('run-1', 'diagnosis-1')).toBe(true)
    })
    expect(onRequested).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['agent_attempts.already_corrected', /already corrected/],
    ['agent_attempts.diagnosis_not_applicable', /no longer applies/],
    ['attempts.run_has_active_attempt', /Another Agent attempt is still active/],
    ['agent_attempts.context_manifest_too_large', /too large/],
  ])('maps %s to fixed local copy rather than the server wording', async (code, copy) => {
    mockClient(vi.fn().mockRejectedValue(problem(code, 'SERVER WORDING')))
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', onRequested))

    await act(async () => {
      expect(await result.current.request('run-1', 'diagnosis-1')).toBe(false)
    })

    expect(result.current.error).toMatch(copy)
    expect(result.current.error).not.toContain('SERVER WORDING')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('reports persistence ambiguity truthfully and asks for a refresh before retrying', async () => {
    mockClient(vi.fn().mockRejectedValue(problem('attempts.persistence_unresolved')))
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1')
    })

    expect(result.current.error).toBe(
      'The host could not confirm whether the request was recorded. Refresh the run before trying again.',
    )
  })

  it('falls back to a generic sentence for a non-API failure', async () => {
    mockClient(vi.fn().mockRejectedValue(new Error('ECONNRESET at 10.0.0.7:5432')))
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'diagnosis-1')
    })

    expect(result.current.error).toBe('A correction of the diagnosed findings could not be requested for this run.')
  })

  it('refuses a duplicate submission while one is in flight', async () => {
    const pending = deferred<RequestDiagnosisCorrectionResponse>()
    const requestDiagnosisCorrection = vi.fn().mockReturnValue(pending.promise)
    mockClient(requestDiagnosisCorrection)
    const { result } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', vi.fn()))

    let first!: Promise<boolean>
    act(() => {
      first = result.current.request('run-1', 'diagnosis-1')
    })
    await act(async () => {
      expect(await result.current.request('run-1', 'diagnosis-1')).toBe(false)
    })
    expect(requestDiagnosisCorrection).toHaveBeenCalledTimes(1)

    await act(async () => {
      pending.resolve(new RequestDiagnosisCorrectionResponse({ status: 'AttemptCreated' }))
      await first
    })
    expect(result.current.requesting).toBe(false)
  })

  it('rejects a handler retained from an earlier render after the run changed', async () => {
    const onRequested = vi.fn()
    const renders: ReturnType<typeof useRequestDiagnosisCorrection>[] = []
    function Recorder({ runId }: { runId: string }) {
      renders.push(useRequestDiagnosisCorrection(runId, 'diagnosis-1', onRequested))
      return null
    }
    mockClient(vi.fn())

    const { rerender } = render(<Recorder runId="run-1" />)
    const oldRequest = renders[0].request
    rerender(<Recorder runId="run-2" />)

    await act(async () => {
      expect(await oldRequest('run-1', 'diagnosis-1')).toBe(false)
    })
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('does not leak pending, completion, or error across A to B to A', async () => {
    const pending = deferred<RequestDiagnosisCorrectionResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, rerender } = renderHook(({ runId }) => useRequestDiagnosisCorrection(runId, 'diagnosis-1', refresh), {
      initialProps: { runId: 'run-1' },
    })

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'diagnosis-1')
    })
    expect(result.current.requesting).toBe(true)
    rerender({ runId: 'run-2' })
    rerender({ runId: 'run-1' })
    expect(result.current.requesting).toBe(false)

    await act(async () => {
      pending.reject(problem('attempts.persistence_unresolved'))
      await started
    })

    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.error).toBeNull()
    expect(result.current.requesting).toBe(false)
  })

  it('ends the lifetime when the target diagnosis is replaced', async () => {
    const pending = deferred<RequestDiagnosisCorrectionResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, rerender } = renderHook(({ attemptId }) => useRequestDiagnosisCorrection('run-1', attemptId, refresh), {
      initialProps: { attemptId: 'diagnosis-1' },
    })

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'diagnosis-1')
    })
    rerender({ attemptId: 'diagnosis-2' })

    await act(async () => {
      pending.resolve(new RequestDiagnosisCorrectionResponse({ status: 'AttemptCreated' }))
      await started
    })

    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.requesting).toBe(false)
  })

  it('does not refresh after unmount', async () => {
    const pending = deferred<RequestDiagnosisCorrectionResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, unmount } = renderHook(() => useRequestDiagnosisCorrection('run-1', 'diagnosis-1', refresh))

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'diagnosis-1')
    })
    unmount()
    await act(async () => {
      pending.resolve(new RequestDiagnosisCorrectionResponse({ status: 'AttemptCreated' }))
      await started
    })

    expect(refresh).not.toHaveBeenCalled()
  })
})
