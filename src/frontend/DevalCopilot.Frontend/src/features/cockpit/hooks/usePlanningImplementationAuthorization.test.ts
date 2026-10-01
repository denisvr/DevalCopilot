import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PlanningImplementationAuthorizationResponse } from '../../../api/generated/api-client'
import { planningImplementationAuthorizationClient } from '../../../api/clients'
import { usePlanningImplementationAuthorization } from './usePlanningImplementationAuthorization'

vi.mock('../../../api/clients', () => ({
  planningImplementationAuthorizationClient: vi.fn(),
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

const facts = (state: string, escalation = 'esc-1') =>
  new PlanningImplementationAuthorizationResponse({ runId: 'run-1', escalationMessageId: escalation, state })

let read: ReturnType<typeof vi.fn>

beforeEach(() => {
  read = vi.fn()
  vi.mocked(planningImplementationAuthorizationClient).mockReturnValue({
    getPlanningImplementationAuthorization: read,
  } as unknown as ReturnType<typeof planningImplementationAuthorizationClient>)
})

describe('usePlanningImplementationAuthorization', () => {
  it('reads the facts for the exact run and escalation', async () => {
    read.mockResolvedValue(facts('Absent'))

    const { result } = renderHook(() => usePlanningImplementationAuthorization('run-1', 'esc-1', 5))

    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.authorization?.state).toBe('Absent'))
    expect(read).toHaveBeenCalledWith('run-1', 'esc-1')
    expect(result.current.error).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('reads nothing and reports not loading without an escalation', () => {
    const { result } = renderHook(() => usePlanningImplementationAuthorization('run-1', null, 5))

    expect(read).not.toHaveBeenCalled()
    expect(result.current.authorization).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('re-reads when the run advances or a refresh is requested', async () => {
    read.mockResolvedValue(facts('Absent'))
    const { result, rerender } = renderHook(({ sequence }) => usePlanningImplementationAuthorization('run-1', 'esc-1', sequence), {
      initialProps: { sequence: 1 },
    })
    await waitFor(() => expect(result.current.authorization).not.toBeNull())

    rerender({ sequence: 2 })
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2))
    act(() => result.current.refresh())
    await waitFor(() => expect(read).toHaveBeenCalledTimes(3))
  })

  it('never shows facts read for another escalation, even for one frame, and ignores its late response', async () => {
    const late = deferred<PlanningImplementationAuthorizationResponse>()
    read.mockImplementation((_run: string, escalation: string) =>
      escalation === 'esc-1' ? late.promise : Promise.resolve(facts('Stale', 'esc-2')))
    const { result, rerender } = renderHook(({ escalation }) => usePlanningImplementationAuthorization('run-1', escalation, 1), {
      initialProps: { escalation: 'esc-1' },
    })

    rerender({ escalation: 'esc-2' })
    expect(result.current.authorization).toBeNull()
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.authorization?.state).toBe('Stale'))

    await act(async () => late.resolve(facts('Available', 'esc-1')))

    expect(result.current.authorization?.state).toBe('Stale')
  })

  it('replaces earlier facts with an honest unavailable message when a refresh fails, and recovers on the next read', async () => {
    read.mockResolvedValueOnce(facts('Available'))
    read.mockRejectedValueOnce(new Error('offline with SECRET-TEXT'))
    read.mockResolvedValueOnce(facts('Consumed'))
    const { result } = renderHook(() => usePlanningImplementationAuthorization('run-1', 'esc-1', 1))
    await waitFor(() => expect(result.current.authorization?.state).toBe('Available'))

    act(() => result.current.refresh())
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.authorization).toBeNull()
    expect(result.current.error).not.toMatch(/SECRET-TEXT/)

    act(() => result.current.refresh())
    await waitFor(() => expect(result.current.authorization?.state).toBe('Consumed'))
    expect(result.current.error).toBeNull()
  })

  describe('returning to an earlier identity (A to B to A)', () => {
    const pendingByKey = () => {
      const reads: Array<{ run: string; escalation: string; control: ReturnType<typeof deferred<PlanningImplementationAuthorizationResponse>> }> = []
      read.mockImplementation((run: string, escalation: string) => {
        const control = deferred<PlanningImplementationAuthorizationResponse>()
        reads.push({ run, escalation, control })
        return control.promise
      })
      return reads
    }

    it.each([
      ['escalation', { run: 'run-1', escalation: 'esc-2' }],
      ['run', { run: 'run-2', escalation: 'esc-1' }],
    ])('awaits a fresh read after a %s round trip and never resurrects the earlier Available facts', async (_name, other) => {
      const reads = pendingByKey()
      const { result, rerender } = renderHook(
        ({ run, escalation }) => usePlanningImplementationAuthorization(run, escalation, 7),
        { initialProps: { run: 'run-1', escalation: 'esc-1' } },
      )
      await act(async () => reads[0].control.resolve(facts('Available')))
      expect(result.current.authorization?.state).toBe('Available')

      rerender(other)
      expect(result.current.authorization).toBeNull()
      expect(result.current.loading).toBe(true)

      rerender({ run: 'run-1', escalation: 'esc-1' })
      expect(reads).toHaveLength(3)
      expect(result.current.authorization).toBeNull()
      expect(result.current.error).toBeNull()
      expect(result.current.loading).toBe(true)

      await act(async () => reads[2].control.resolve(facts('Consumed')))
      expect(result.current.authorization?.state).toBe('Consumed')
      expect(result.current.loading).toBe(false)
    })

    it('does not resurrect an earlier Consumed or error state while the replacement read is pending', async () => {
      const reads = pendingByKey()
      const { result, rerender } = renderHook(
        ({ escalation }) => usePlanningImplementationAuthorization('run-1', escalation, 3),
        { initialProps: { escalation: 'esc-1' } },
      )
      await act(async () => reads[0].control.reject(new Error('offline')))
      expect(result.current.error).not.toBeNull()

      rerender({ escalation: 'esc-2' })
      await act(async () => reads[1].control.resolve(facts('Consumed', 'esc-2')))
      expect(result.current.authorization?.state).toBe('Consumed')

      rerender({ escalation: 'esc-1' })
      expect(result.current.error).toBeNull()
      expect(result.current.authorization).toBeNull()
      expect(result.current.loading).toBe(true)

      rerender({ escalation: 'esc-2' })
      expect(result.current.authorization).toBeNull()
      expect(result.current.loading).toBe(true)
    })

    it('shows an honest unavailable state, not the earlier facts, when the replacement read fails', async () => {
      const reads = pendingByKey()
      const { result, rerender } = renderHook(
        ({ escalation }) => usePlanningImplementationAuthorization('run-1', escalation, 3),
        { initialProps: { escalation: 'esc-1' } },
      )
      await act(async () => reads[0].control.resolve(facts('Available')))
      rerender({ escalation: 'esc-2' })
      rerender({ escalation: 'esc-1' })

      await act(async () => reads[2].control.reject(new Error('offline')))

      expect(result.current.authorization).toBeNull()
      expect(result.current.error).not.toBeNull()
      expect(result.current.loading).toBe(false)
    })

    it('ignores obsolete completions of the earlier lifetime of the same identity', async () => {
      const reads = pendingByKey()
      const { result, rerender } = renderHook(
        ({ escalation }) => usePlanningImplementationAuthorization('run-1', escalation, 3),
        { initialProps: { escalation: 'esc-1' } },
      )
      rerender({ escalation: 'esc-2' })
      rerender({ escalation: 'esc-1' })
      expect(reads).toHaveLength(3)

      // The first lifetime's read (and the intermediate identity's) complete late, after the identity came back.
      await act(async () => reads[0].control.resolve(facts('Available')))
      await act(async () => reads[1].control.resolve(facts('Stale', 'esc-2')))
      expect(result.current.authorization).toBeNull()
      expect(result.current.loading).toBe(true)

      await act(async () => reads[2].control.resolve(facts('Absent')))
      expect(result.current.authorization?.state).toBe('Absent')
    })

    it('treats an unchanged event sequence after a null round trip as a fresh read', async () => {
      const reads = pendingByKey()
      const { result, rerender } = renderHook(
        ({ escalation }: { escalation: string | null }) => usePlanningImplementationAuthorization('run-1', escalation, 3),
        { initialProps: { escalation: 'esc-1' as string | null } },
      )
      await act(async () => reads[0].control.resolve(facts('Available')))

      rerender({ escalation: null })
      expect(result.current.loading).toBe(false)
      expect(result.current.authorization).toBeNull()
      rerender({ escalation: 'esc-1' })

      expect(result.current.authorization).toBeNull()
      expect(result.current.loading).toBe(true)
      expect(reads).toHaveLength(2)
    })
  })

  it('does not apply a response that arrives after unmount', async () => {
    const late = deferred<PlanningImplementationAuthorizationResponse>()
    read.mockReturnValue(late.promise)
    const { unmount } = renderHook(() => usePlanningImplementationAuthorization('run-1', 'esc-1', 1))

    unmount()
    await expect(
      act(async () => {
        late.resolve(facts('Available'))
      }),
    ).resolves.not.toThrow()
  })
})
