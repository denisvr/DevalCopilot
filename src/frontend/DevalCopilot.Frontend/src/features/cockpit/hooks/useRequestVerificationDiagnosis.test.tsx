// @vitest-environment jsdom
import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, RequestVerificationDiagnosisResponse } from '../../../api/generated/api-client'
import { requestVerificationDiagnosisClient } from '../../../api/clients'
import { useRequestVerificationDiagnosis } from './useRequestVerificationDiagnosis'

vi.mock('../../../api/clients', () => ({
  requestVerificationDiagnosisClient: vi.fn(),
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

function mockClient(requestVerificationDiagnosis: ReturnType<typeof vi.fn>) {
  vi.mocked(requestVerificationDiagnosisClient).mockReturnValue({
    requestVerificationDiagnosis,
  } as unknown as ReturnType<typeof requestVerificationDiagnosisClient>)
}

describe('useRequestVerificationDiagnosis', () => {
  it('sends the exact run and report identifiers and refreshes on success', async () => {
    const requestVerificationDiagnosis = vi
      .fn()
      .mockResolvedValue(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
    mockClient(requestVerificationDiagnosis)
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'report-1')
    })

    expect(requestVerificationDiagnosis).toHaveBeenCalledTimes(1)
    const [runIdArg, requestArg] = requestVerificationDiagnosis.mock.calls[0]
    expect(runIdArg).toBe('run-1')
    expect(requestArg).toMatchObject({ executionReportMessageId: 'report-1' })
    expect(onRequested).toHaveBeenCalledTimes(1)
    expect(outcome).toBe(true)
    expect(result.current.error).toBeNull()
  })

  it('tracks requesting while in flight, refuses a duplicate submission, and clears afterwards', async () => {
    const pending = deferred<RequestVerificationDiagnosisResponse>()
    const requestVerificationDiagnosis = vi.fn().mockReturnValue(pending.promise)
    mockClient(requestVerificationDiagnosis)
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', vi.fn()))

    let first!: Promise<boolean>
    act(() => {
      first = result.current.request('run-1', 'report-1')
    })
    await waitFor(() => expect(result.current.requesting).toBe(true))

    let duplicate!: boolean
    await act(async () => {
      duplicate = await result.current.request('run-1', 'report-1')
    })
    expect(duplicate).toBe(false)
    expect(requestVerificationDiagnosis).toHaveBeenCalledTimes(1)

    await act(async () => {
      pending.resolve(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await first
    })
    expect(result.current.requesting).toBe(false)
  })

  it.each([
    ['verification_diagnosis.no_failed_verification', /nothing to diagnose/],
    ['verification_diagnosis.evidence_running', /still running/],
    ['agent_attempts.already_diagnosed', /already diagnosed/],
    ['attempts.run_has_active_attempt', /Another Agent attempt is still active/],
    ['agent_attempts.budget_exhausted', /budget is exhausted/],
    ['agent_attempts.checkpoint_not_current', /no longer current/],
    ['attempts.persistence_failed', /could not record the request/],
  ])('maps %s to fixed local copy rather than the server wording', async (code, copy) => {
    mockClient(vi.fn().mockRejectedValue(problem(code, 'SERVER WORDING /secret/path')))
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', onRequested))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-1', 'report-1')
    })

    expect(outcome).toBe(false)
    expect(result.current.error).toMatch(copy)
    expect(result.current.error).not.toContain('SERVER WORDING')
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('reports persistence ambiguity truthfully: neither success nor failure, and a refresh before retrying', async () => {
    mockClient(vi.fn().mockRejectedValue(problem('attempts.persistence_unresolved')))
    const onRequested = vi.fn()
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', onRequested))

    await act(async () => {
      await result.current.request('run-1', 'report-1')
    })

    expect(result.current.error).toBe(
      'The host could not confirm whether the request was recorded. Refresh the run before trying again.',
    )
    expect(result.current.error).not.toMatch(/was not recorded|succeeded|has been requested/i)
    expect(onRequested).not.toHaveBeenCalled()
  })

  it('falls back to the backend-authored detail for an unlisted shared code, and to a generic sentence otherwise', async () => {
    const requestVerificationDiagnosis = vi
      .fn()
      .mockRejectedValueOnce(problem('tokens.stop_reached', 'A token stop is active for this run.'))
      .mockRejectedValueOnce(new Error('ECONNRESET at 10.0.0.7:5432'))
    mockClient(requestVerificationDiagnosis)
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'report-1')
    })
    expect(result.current.error).toBe('A token stop is active for this run.')

    await act(async () => {
      await result.current.request('run-1', 'report-1')
    })
    expect(result.current.error).toBe('A diagnosis of the failed verification could not be requested for this run.')
    expect(result.current.error).not.toContain('10.0.0.7')
  })

  it('rejects a request bound to another run than the rendered one', async () => {
    const requestVerificationDiagnosis = vi.fn()
    mockClient(requestVerificationDiagnosis)
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', vi.fn()))

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.request('run-2', 'report-1')
    })

    expect(outcome).toBe(false)
    expect(requestVerificationDiagnosis).not.toHaveBeenCalled()
  })

  it('rejects a handler retained from an earlier render after the run changed', async () => {
    const onRequested = vi.fn()
    const renders: ReturnType<typeof useRequestVerificationDiagnosis>[] = []
    function Recorder({ runId }: { runId: string }) {
      renders.push(useRequestVerificationDiagnosis(runId, 'report-1', onRequested))
      return null
    }
    mockClient(vi.fn())

    const { rerender } = render(<Recorder runId="run-1" />)
    const oldRequest = renders[0].request
    rerender(<Recorder runId="run-2" />)

    await act(async () => {
      expect(await oldRequest('run-1', 'report-1')).toBe(false)
    })
    expect(onRequested).not.toHaveBeenCalled()
    expect(renders.at(-1)?.requesting).toBe(false)
    expect(renders.at(-1)?.error).toBeNull()
  })

  it('does not leak pending, completion, or error across A to B to A', async () => {
    const pending = deferred<RequestVerificationDiagnosisResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, rerender } = renderHook(({ runId }) => useRequestVerificationDiagnosis(runId, 'report-1', refresh), {
      initialProps: { runId: 'run-1' },
    })

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'report-1')
    })
    expect(result.current.requesting).toBe(true)

    rerender({ runId: 'run-2' })
    expect(result.current.requesting).toBe(false)
    rerender({ runId: 'run-1' })
    expect(result.current.requesting).toBe(false)
    expect(result.current.error).toBeNull()

    let outcome!: boolean
    await act(async () => {
      pending.resolve(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      outcome = await started
    })

    expect(outcome).toBe(false)
    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.requesting).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('does not surface an old run error after switching runs', async () => {
    const pending = deferred<RequestVerificationDiagnosisResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const { result, rerender } = renderHook(({ runId }) => useRequestVerificationDiagnosis(runId, 'report-1', vi.fn()), {
      initialProps: { runId: 'run-1' },
    })

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'report-1')
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      pending.reject(problem('attempts.persistence_unresolved'))
      await started
    })

    expect(result.current.error).toBeNull()
  })

  it('ends the lifetime when the target report is replaced', async () => {
    const pending = deferred<RequestVerificationDiagnosisResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, rerender } = renderHook(({ reportId }) => useRequestVerificationDiagnosis('run-1', reportId, refresh), {
      initialProps: { reportId: 'report-1' },
    })

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'report-1')
    })
    rerender({ reportId: 'report-2' })

    await act(async () => {
      pending.resolve(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await started
    })

    expect(refresh).not.toHaveBeenCalled()
    expect(result.current.requesting).toBe(false)
  })

  it('does not refresh after unmount', async () => {
    const pending = deferred<RequestVerificationDiagnosisResponse>()
    mockClient(vi.fn().mockReturnValue(pending.promise))
    const refresh = vi.fn()
    const { result, unmount } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', refresh))

    let started!: Promise<boolean>
    act(() => {
      started = result.current.request('run-1', 'report-1')
    })
    unmount()
    await act(async () => {
      pending.resolve(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 }))
      await started
    })

    expect(refresh).not.toHaveBeenCalled()
  })

  it('never writes identifiers or error text into localStorage, sessionStorage, or the URL', async () => {
    mockClient(vi.fn().mockResolvedValue(new RequestVerificationDiagnosisResponse({ attemptId: 'attempt-1', attemptNumber: 1 })))
    localStorage.clear()
    sessionStorage.clear()
    const urlBefore = window.location.href
    const { result } = renderHook(() => useRequestVerificationDiagnosis('run-1', 'report-1', vi.fn()))

    await act(async () => {
      await result.current.request('run-1', 'report-1')
    })

    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(window.location.href).toBe(urlBefore)
  })
})
