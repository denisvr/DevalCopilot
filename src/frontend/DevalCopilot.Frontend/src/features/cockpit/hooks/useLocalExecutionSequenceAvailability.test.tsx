import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionSequenceAvailabilityClient } from '../../../api/clients'
import { localAvailabilityFor, localGuid, localRefusedAvailabilityFor } from '../localExecutionSequenceFixture'
import { LOCAL_EXECUTION_AVAILABILITY_TIMEOUT_MS, useLocalExecutionSequenceAvailability } from './useLocalExecutionSequenceAvailability'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionSequenceAvailabilityClient: vi.fn(),
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

const signals: AbortSignal[] = []
const PLAN_A = localGuid(41)
const PLAN_B = localGuid(42)

function install(read: (runId: string, planId: string) => Promise<unknown>) {
  const readAvailability = vi.fn(read)
  vi.mocked(localExecutionSequenceAvailabilityClient).mockImplementation((signal?: AbortSignal) => {
    if (signal !== undefined) {
      signals.push(signal)
    }

    return { readAvailability } as unknown as ReturnType<typeof localExecutionSequenceAvailabilityClient>
  })
  return readAvailability
}

const mounted = (runId: string | null, planId: string | null, hint?: number) =>
  renderHook<ReturnType<typeof useLocalExecutionSequenceAvailability>, { runId: string | null; planId: string | null; hint?: number }>(
    (props) => useLocalExecutionSequenceAvailability(props.runId, props.planId, props.hint),
    { initialProps: { runId, planId, hint } },
  )

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useLocalExecutionSequenceAvailability', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    signals.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing without a run and a selected plan', () => {
    const read = install(() => Promise.resolve(localAvailabilityFor('run-1')))
    const { result } = mounted('run-1', null)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ availability: null, loading: false, reading: false })
  })

  it('reads the exact run and plan once and exposes the validated candidate', async () => {
    const read = install((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId)))
    const { result } = mounted('run-1', PLAN_A)
    expect(result.current).toMatchObject({ availability: null, loading: true, reading: true })
    await flush()

    expect(read).toHaveBeenCalledWith('run-1', PLAN_A)
    expect(result.current).toMatchObject({ loading: false, reading: false, failure: null })
    expect(result.current.availability?.available).toBe(true)
  })

  it('exposes a fixed refusal as an answer and an incoherent body as a failure', async () => {
    install((runId, planId) => Promise.resolve(localRefusedAvailabilityFor(runId, 'local_execution_sequence.recipes_missing', planId)))
    const refused = mounted('run-1', PLAN_A)
    await flush()
    expect(refused.result.current.availability).toMatchObject({ available: false, refusalCode: 'local_execution_sequence.recipes_missing' })
    refused.unmount()

    install(() => Promise.resolve({ available: true }))
    const invalid = mounted('run-1', PLAN_A)
    await flush()
    expect(invalid.result.current).toMatchObject({ availability: null, failure: 'invalid' })
  })

  it('refuses an answer for another plan as invalid', async () => {
    install((runId) => Promise.resolve(localAvailabilityFor(runId, PLAN_B)))
    const { result } = mounted('run-1', PLAN_A)
    await flush()

    expect(result.current).toMatchObject({ availability: null, failure: 'invalid' })
  })

  it('never shows the answer of plan A for plan B, nor an earlier visit of A when A is selected again', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<unknown>>[]>()
    install((_runId, planId) => {
      const next = deferred<unknown>()
      answers.set(planId, [...(answers.get(planId) ?? []), next])
      return next.promise
    })
    const { result, rerender } = mounted('run-1', PLAN_A)
    await act(async () => answers.get(PLAN_A)![0].resolve(localAvailabilityFor('run-1', PLAN_A)))
    expect(result.current.availability?.planProposalMessageId).toBe(PLAN_A)

    rerender({ runId: 'run-1', planId: PLAN_B })
    expect(result.current).toMatchObject({ availability: null, loading: true })

    rerender({ runId: 'run-1', planId: PLAN_A })
    expect(result.current).toMatchObject({ availability: null, loading: true })

    await act(async () => answers.get(PLAN_B)![0].resolve(localAvailabilityFor('run-1', PLAN_B)))
    expect(result.current.availability).toBeNull()

    await act(async () => answers.get(PLAN_A)![1].resolve(localAvailabilityFor('run-1', PLAN_A, { correctionAllowance: 1 })))
    expect(result.current.availability?.candidate?.correctionAllowance).toBe(1)
  })

  it('aborts a hung read after ten seconds and masks the earlier answer', async () => {
    let call = 0
    install((runId, planId) => {
      call += 1
      return call === 1 ? Promise.resolve(localAvailabilityFor(runId, planId)) : new Promise<unknown>(() => undefined)
    })
    const { result, rerender } = mounted('run-1', PLAN_A, 0)
    await flush()
    expect(result.current.availability).not.toBeNull()

    rerender({ runId: 'run-1', planId: PLAN_A, hint: 1 })
    await flush()
    expect(result.current.reading).toBe(true)
    expect(result.current.availability).not.toBeNull()

    await tick(LOCAL_EXECUTION_AVAILABILITY_TIMEOUT_MS)
    expect(signals[1].aborted).toBe(true)
    expect(result.current).toMatchObject({ availability: null, failure: 'timeout', reading: false })
  })

  it('coalesces triggers that arrive while a read is pending into one further read', async () => {
    const first = deferred<unknown>()
    let call = 0
    const read = install((runId, planId) => {
      call += 1
      return call === 1 ? first.promise : Promise.resolve(localAvailabilityFor(runId, planId))
    })
    const { result, rerender } = mounted('run-1', PLAN_A, 0)
    rerender({ runId: 'run-1', planId: PLAN_A, hint: 1 })
    rerender({ runId: 'run-1', planId: PLAN_A, hint: 2 })
    await act(async () => {
      result.current.refresh()
      result.current.refresh()
    })
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      first.resolve(localAvailabilityFor('run-1', PLAN_A))
      await first.promise
    })
    await flush()
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('aborts the pending request when the visit ends and starts no read from a retained refresh', async () => {
    const read = install(() => new Promise<unknown>(() => undefined))
    const { result, unmount } = mounted('run-1', PLAN_A)
    const retained = result.current.refresh

    unmount()
    expect(signals[0].aborted).toBe(true)
    retained()
    await flush()
    expect(read).toHaveBeenCalledTimes(1)
  })
})
