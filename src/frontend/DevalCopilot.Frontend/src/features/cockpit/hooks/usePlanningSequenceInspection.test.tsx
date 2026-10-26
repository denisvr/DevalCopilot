import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { planningSequenceInspectionIndexClient, planningSequenceInspectionStepClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import {
  FIVE_STEPS,
  RUN,
  SEQUENCE,
  guidOf,
  notRecordedIndexFor,
  recordedIndexFor,
  refusedStepAnswerFor,
  reservedStepFor,
  stepAnswerFor,
  stepFor,
  stepIdFor,
  unavailableIndexFor,
} from '../planningSequenceInspectionFixture'
import { usePlanningSequenceInspection } from './usePlanningSequenceInspection'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  planningSequenceInspectionIndexClient: vi.fn(),
  planningSequenceInspectionStepClient: vi.fn(),
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

const RUN_B = guidOf('r', 2)

function install(handlers: {
  index?: (runId: string) => Promise<unknown>
  step?: (runId: string, stepId: string) => Promise<unknown>
} = {}) {
  const readIndex = vi.fn(handlers.index ?? ((runId: string) => Promise.resolve(recordedIndexFor(runId))))
  const readStep = vi.fn(
    handlers.step ?? ((_runId: string, stepId: string) => Promise.resolve(stepAnswerFor(FIVE_STEPS.findIndex((step) => step.stepId === stepId)))),
  )
  vi.mocked(planningSequenceInspectionIndexClient).mockReturnValue({ readIndex } as unknown as ReturnType<typeof planningSequenceInspectionIndexClient>)
  vi.mocked(planningSequenceInspectionStepClient).mockReturnValue({ readStep } as unknown as ReturnType<typeof planningSequenceInspectionStepClient>)
  return { readIndex, readStep }
}

const flush = () => act(async () => undefined)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'trace' })

describe('usePlanningSequenceInspection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the owner opens the region, then reads the index exactly once', async () => {
    const { readIndex, readStep } = install()
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))

    expect(result.current.open).toBe(false)
    expect(readIndex).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(readIndex).toHaveBeenCalledTimes(1)
    expect(readIndex).toHaveBeenCalledWith(RUN)
    expect(readStep).not.toHaveBeenCalled()
    expect(result.current.index?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
  })

  it('does nothing for a null run', async () => {
    const { readIndex } = install()
    const { result } = renderHook(() => usePlanningSequenceInspection(null))

    act(() => result.current.show())
    await flush()

    expect(result.current.open).toBe(false)
    expect(readIndex).not.toHaveBeenCalled()
  })

  it('reads only the chosen step, never the others', async () => {
    const { readStep } = install()
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()

    act(() => result.current.selectStep(stepIdFor(3)))
    await flush()

    expect(readStep).toHaveBeenCalledTimes(1)
    expect(readStep).toHaveBeenCalledWith(RUN, stepIdFor(3))
    expect(result.current.stepAnswer?.kind).toBe('agent')
    expect(result.current.selectedStep?.stepId).toBe(stepIdFor(3))
  })

  it('ignores an unknown step', async () => {
    const { readStep } = install()
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()

    act(() => result.current.selectStep(guidOf('t', 99)))

    expect(readStep).not.toHaveBeenCalled()
    expect(result.current.selectedStep).toBeNull()
  })

  it('shows a step without a recorded link as itself and an unavailable association as itself', async () => {
    install({
      index: () => Promise.resolve(recordedIndexFor(RUN, [FIVE_STEPS[0], reservedStepFor(1)])),
      step: (_run, stepId) => Promise.resolve(refusedStepAnswerFor(stepId === stepIdFor(1) ? 1 : 0, stepId === stepIdFor(1) ? 'NoLinkedAttempt' : 'Unavailable')),
    })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()

    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepAnswer).toEqual({ kind: 'no-linked-attempt' })

    act(() => result.current.selectStep(stepIdFor(0)))
    await flush()
    expect(result.current.stepAnswer).toEqual({ kind: 'unavailable' })
  })

  it('refuses an incoherent index and an incoherent step answer instead of showing anything', async () => {
    install({ index: () => Promise.resolve({ ...recordedIndexFor(), extra: 1 }) })
    const bad = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => bad.result.current.show())
    await flush()
    expect(bad.result.current.failure).toBe('invalid')
    expect(bad.result.current.index).toBeNull()

    install({ step: () => Promise.resolve(stepAnswerFor(1, { sequenceId: guidOf('s', 9) })) })
    const stepBad = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => stepBad.result.current.show())
    await flush()
    act(() => stepBad.result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(stepBad.result.current.stepFailure).toBe('invalid')
    expect(stepBad.result.current.stepAnswer).toBeNull()
  })

  it('reports a not-found run and a failed read, and retries only the same step on request', async () => {
    install({ index: () => Promise.reject(notFound()) })
    const missing = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => missing.result.current.show())
    await flush()
    expect(missing.result.current.failure).toBe('not-found')

    let attempts = 0
    const { readStep } = install({
      step: () => (attempts++ === 0 ? Promise.reject(new Error('network')) : Promise.resolve(stepAnswerFor(1))),
    })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepFailure).toBe('read')
    act(() => result.current.retryStep())
    await flush()

    expect(readStep).toHaveBeenCalledTimes(2)
    expect(result.current.stepAnswer?.kind).toBe('agent')
    act(() => result.current.retryStep())
    expect(readStep).toHaveBeenCalledTimes(2)
  })

  it('reload ends the selection and the association before reading, and a late answer of the earlier visit settles nothing', async () => {
    const first = deferred<unknown>()
    let calls = 0
    const { readIndex } = install({ index: (runId) => (calls++ === 0 ? Promise.resolve(recordedIndexFor(runId)) : first.promise) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepAnswer?.kind).toBe('agent')

    act(() => result.current.reload())

    expect(readIndex).toHaveBeenCalledTimes(2)
    expect(result.current.index).toBeNull()
    expect(result.current.selectedStep).toBeNull()
    expect(result.current.stepAnswer).toBeNull()
    expect(result.current.stepKey).toBeNull()
    expect(result.current.pending).toBe(true)

    await act(async () => first.resolve(recordedIndexFor(RUN, [stepFor(0)])))
    expect(result.current.index?.status === 'Recorded' && result.current.index.steps).toHaveLength(1)
    expect(result.current.selectedStep).toBeNull()
  })

  it('clears an earlier success when a reload answers Unavailable, NotRecorded, invalid or a failure', async () => {
    const answers: unknown[] = [recordedIndexFor(), unavailableIndexFor(), recordedIndexFor(), notRecordedIndexFor(), recordedIndexFor(), { nope: 1 }]
    install({ index: () => Promise.resolve(answers.shift()) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    expect(result.current.index?.status).toBe('Recorded')

    act(() => result.current.reload())
    await flush()
    expect(result.current.index).toEqual({ status: 'Unavailable' })
    act(() => result.current.reload())
    await flush()
    act(() => result.current.reload())
    await flush()
    expect(result.current.index).toEqual({ status: 'NotRecorded' })
    act(() => result.current.reload())
    await flush()
    act(() => result.current.reload())
    await flush()
    expect(result.current.index).toBeNull()
    expect(result.current.failure).toBe('invalid')
  })

  it('does not let a retained reload of an ended visit start another read', async () => {
    const { readIndex } = install()
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    const staleReload = result.current.reload

    act(() => result.current.reload())
    await flush()
    act(() => staleReload())
    await flush()

    expect(readIndex).toHaveBeenCalledTimes(2)
  })

  it('close discards everything and a late index answer settles nothing; reopening is a new lifetime that reads again', async () => {
    const late = deferred<unknown>()
    let calls = 0
    const { readIndex } = install({ index: (runId) => (calls++ === 0 ? late.promise : Promise.resolve(recordedIndexFor(runId))) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    const staleClose = result.current.close
    act(() => result.current.close())

    await act(async () => late.resolve(recordedIndexFor(RUN)))
    expect(result.current.open).toBe(false)
    expect(result.current.index).toBeNull()

    act(() => result.current.show())
    await flush()
    expect(readIndex).toHaveBeenCalledTimes(2)
    expect(result.current.index?.status).toBe('Recorded')
    act(() => staleClose())
    expect(result.current.open).toBe(true)
  })

  it('treats a return to an earlier run as a new lifetime (A-B-A): nothing of the first visit survives and its late answer is dropped', async () => {
    const lateA = deferred<unknown>()
    let aCalls = 0
    install({ index: (runId) => (runId === RUN && aCalls++ === 0 ? lateA.promise : Promise.resolve(recordedIndexFor(runId))) })
    const { result, rerender } = renderHook(({ run }: { run: string }) => usePlanningSequenceInspection(run), { initialProps: { run: RUN } })
    act(() => result.current.show())

    rerender({ run: RUN_B })
    expect(result.current.open).toBe(false)
    rerender({ run: RUN })
    expect(result.current.open).toBe(false)
    expect(result.current.index).toBeNull()

    await act(async () => lateA.resolve(recordedIndexFor(RUN)))
    expect(result.current.index).toBeNull()
    act(() => result.current.show())
    await flush()
    expect(result.current.index?.status).toBe('Recorded')
  })

  it('a replacement run never shows the earlier run\'s index, selection or association', async () => {
    install()
    const { result, rerender } = renderHook(({ run }: { run: string }) => usePlanningSequenceInspection(run), { initialProps: { run: RUN } })
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepAnswer?.kind).toBe('agent')

    rerender({ run: RUN_B })

    expect(result.current.open).toBe(false)
    expect(result.current.index).toBeNull()
    expect(result.current.selectedStep).toBeNull()
    expect(result.current.stepAnswer).toBeNull()
  })

  it('a remount starts closed and reads nothing', async () => {
    const { readIndex } = install()
    const first = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => first.result.current.show())
    await flush()
    first.unmount()

    const second = renderHook(() => usePlanningSequenceInspection(RUN))

    expect(second.result.current.open).toBe(false)
    expect(second.result.current.index).toBeNull()
    expect(readIndex).toHaveBeenCalledTimes(1)
  })

  it('does not settle after unmount', async () => {
    const late = deferred<unknown>()
    install({ index: () => late.promise })
    const { result, unmount } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    unmount()

    await expect(act(async () => late.resolve(recordedIndexFor(RUN)))).resolves.toBeUndefined()
  })

  it('lets only the latest overlapping step choice settle', async () => {
    const slow = deferred<unknown>()
    install({ step: (_run, stepId) => (stepId === stepIdFor(1) ? slow.promise : Promise.resolve(stepAnswerFor(0))) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    act(() => result.current.selectStep(stepIdFor(0)))
    await flush()

    await act(async () => slow.resolve(stepAnswerFor(1)))

    expect(result.current.selectedStep?.stepId).toBe(stepIdFor(0))
    expect(result.current.stepAnswer?.kind === 'agent' && result.current.stepAnswer.agent.attemptNumber).toBe(1)
  })

  it('a late answer of a step visit that was closed settles nothing, also after the same step is chosen again', async () => {
    const late = deferred<unknown>()
    let calls = 0
    install({ step: () => (calls++ === 0 ? late.promise : Promise.resolve(stepAnswerFor(1, { agent: { ...stepAnswerFor(1).agent, attemptNumber: 9 } }))) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    act(() => result.current.clearStep())
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepAnswer?.kind === 'agent' && result.current.stepAnswer.agent.attemptNumber).toBe(9)

    await act(async () => late.resolve(stepAnswerFor(1)))

    expect(result.current.stepAnswer?.kind === 'agent' && result.current.stepAnswer.agent.attemptNumber).toBe(9)
  })

  it('a retained Hide of an earlier step visit never clears a later choice, also when queued in the same batch', async () => {
    install()
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(0)))
    await flush()
    const staleClear = result.current.clearStep

    act(() => {
      result.current.selectStep(stepIdFor(1))
    })
    await flush()
    act(() => staleClear())
    expect(result.current.selectedStep?.stepId).toBe(stepIdFor(1))

    const clear = result.current.clearStep
    act(() => {
      clear()
      result.current.selectStep(stepIdFor(1))
    })
    await flush()
    expect(result.current.selectedStep?.stepId).toBe(stepIdFor(1))
  })

  it('reads only the index and the chosen step: no polling, prefetch or automatic retry', async () => {
    vi.useFakeTimers()
    try {
      const { readIndex, readStep } = install({ step: () => Promise.reject(new Error('network')) })
      const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
      act(() => result.current.show())
      await flush()
      act(() => result.current.selectStep(stepIdFor(2)))
      await flush()
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10 * 60 * 1000)
      })

      expect(readIndex).toHaveBeenCalledTimes(1)
      expect(readStep).toHaveBeenCalledTimes(1)
      expect(result.current.stepFailure).toBe('read')
    } finally {
      vi.useRealTimers()
    }
  })

  it('binds the step read to the sequence of the accepted index', async () => {
    const { readStep } = install({ step: () => Promise.resolve(stepAnswerFor(1, { sequenceId: SEQUENCE })) })
    const { result } = renderHook(() => usePlanningSequenceInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()

    expect(readStep).toHaveBeenCalledWith(RUN, stepIdFor(1))
    expect(result.current.stepAnswer?.kind).toBe('agent')
  })
})
