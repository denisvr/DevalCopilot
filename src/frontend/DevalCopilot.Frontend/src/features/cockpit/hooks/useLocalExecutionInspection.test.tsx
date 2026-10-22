import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  localExecutionInspectionIndexClient,
  localExecutionInspectionStepClient,
  localExecutionInspectionWindowClient,
} from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import {
  RUN,
  SEQUENCE,
  guidOf,
  operationIdFor,
  recordedIndexFor,
  refusedWindowFor,
  stepFor,
  stepIdFor,
  streamFor,
  verificationStepAnswerFor,
  windowAnswerFor,
  agentStepAnswerFor,
  notRecordedIndexFor,
  verificationFor,
} from '../localExecutionInspectionFixture'
import { useLocalExecutionInspection } from './useLocalExecutionInspection'
import { useLocalExecutionInspectionWindow } from './useLocalExecutionInspectionWindow'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionInspectionIndexClient: vi.fn(),
  localExecutionInspectionStepClient: vi.fn(),
  localExecutionInspectionWindowClient: vi.fn(),
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
const indexFor = (run: string) => recordedIndexFor(run)

function install(handlers: {
  index?: (runId: string) => Promise<unknown>
  step?: (runId: string, stepId: string) => Promise<unknown>
  window?: (runId: string, stepId: string, stream: string, from: number, max: number) => Promise<unknown>
} = {}) {
  const readIndex = vi.fn(handlers.index ?? ((runId: string) => Promise.resolve(indexFor(runId))))
  const readStep = vi.fn(
    handlers.step ??
      ((_runId: string, stepId: string) =>
        Promise.resolve(stepId === stepIdFor(1) ? verificationStepAnswerFor(1) : stepId === stepIdFor(0) ? agentStepAnswerFor(0) : agentStepAnswerFor(2, { agent: null }))),
  )
  const readWindow = vi.fn(handlers.window ?? (() => Promise.reject(new Error('unexpected window read'))))
  vi.mocked(localExecutionInspectionIndexClient).mockReturnValue({ readIndex } as unknown as ReturnType<typeof localExecutionInspectionIndexClient>)
  vi.mocked(localExecutionInspectionStepClient).mockReturnValue({ readStep } as unknown as ReturnType<typeof localExecutionInspectionStepClient>)
  vi.mocked(localExecutionInspectionWindowClient).mockReturnValue({ readWindow } as unknown as ReturnType<typeof localExecutionInspectionWindowClient>)
  return { readIndex, readStep, readWindow }
}

const flush = () => act(async () => undefined)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'trace' })

describe('useLocalExecutionInspection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the owner opens the region, then reads the index exactly once', async () => {
    const { readIndex } = install()
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))

    expect(result.current.open).toBe(false)
    expect(readIndex).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(readIndex).toHaveBeenCalledTimes(1)
    expect(readIndex).toHaveBeenCalledWith(RUN)
    expect(result.current.index?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
  })

  it('does nothing for a null run', async () => {
    const { readIndex } = install()
    const { result } = renderHook(() => useLocalExecutionInspection(null))

    act(() => result.current.show())
    await flush()

    expect(result.current.open).toBe(false)
    expect(readIndex).not.toHaveBeenCalled()
  })

  it('reads only the chosen step, never the others, and no window until a stream is chosen and loaded', async () => {
    const { readStep, readWindow } = install()
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()

    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()

    expect(readStep).toHaveBeenCalledTimes(1)
    expect(readStep).toHaveBeenCalledWith(RUN, stepIdFor(1))
    expect(result.current.stepAnswer?.kind).toBe('verification')
    act(() => result.current.selectStream('stdout'))
    await flush()
    expect(result.current.stream?.stream).toBe('stdout')
    expect(readWindow).not.toHaveBeenCalled()
  })

  it('ignores an unknown step and an unrecorded stream', async () => {
    const { readStep } = install({
      step: () => Promise.resolve(verificationStepAnswerFor(1, { verification: verificationFor(1, { standardError: { status: 'NotRecorded', artifactId: null, byteLength: null, captureOutcome: null, truncated: null } }) })),
    })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()

    act(() => result.current.selectStep(guidOf('t', 99)))
    expect(readStep).not.toHaveBeenCalled()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    act(() => result.current.selectStream('stderr'))

    expect(result.current.stream).toBeNull()
  })

  it('refuses an incoherent index and an incoherent step answer instead of showing anything', async () => {
    install({ index: () => Promise.resolve({ ...indexFor(RUN), extra: 1 }) })
    const bad = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => bad.result.current.show())
    await flush()
    expect(bad.result.current.failure).toBe('invalid')
    expect(bad.result.current.index).toBeNull()

    install({ step: () => Promise.resolve(verificationStepAnswerFor(1, { sequenceId: guidOf('s', 9) })) })
    const stepBad = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => stepBad.result.current.show())
    await flush()
    act(() => stepBad.result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(stepBad.result.current.stepFailure).toBe('invalid')
    expect(stepBad.result.current.stepAnswer).toBeNull()
  })

  it('reports a not-found run and a failed read, and retries only the same step on request', async () => {
    install({ index: () => Promise.reject(notFound()) })
    const missing = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => missing.result.current.show())
    await flush()
    expect(missing.result.current.failure).toBe('not-found')

    let attempts = 0
    const { readStep } = install({
      step: () => (attempts++ === 0 ? Promise.reject(new Error('network')) : Promise.resolve(verificationStepAnswerFor(1))),
    })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    expect(result.current.stepFailure).toBe('read')
    act(() => result.current.retryStep())
    await flush()

    expect(readStep).toHaveBeenCalledTimes(2)
    expect(result.current.stepAnswer?.kind).toBe('verification')
    act(() => result.current.retryStep())
    expect(readStep).toHaveBeenCalledTimes(2)
  })

  it('reload ends the selection, the operation and the stream before reading, and a late answer of the earlier visit settles nothing', async () => {
    const first = deferred<unknown>()
    let calls = 0
    const { readIndex } = install({ index: (runId) => (calls++ === 0 ? Promise.resolve(indexFor(runId)) : first.promise) })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    await flush()
    act(() => result.current.selectStream('stdout'))

    act(() => result.current.reload())

    expect(readIndex).toHaveBeenCalledTimes(2)
    expect(result.current.index).toBeNull()
    expect(result.current.selectedStep).toBeNull()
    expect(result.current.stepAnswer).toBeNull()
    expect(result.current.stream).toBeNull()
    expect(result.current.pending).toBe(true)

    await act(async () => first.resolve(recordedIndexFor(RUN, [stepFor(0)])))
    expect(result.current.index?.status === 'Recorded' && result.current.index.steps).toHaveLength(1)
  })

  it('does not let a retained reload of an ended visit start another read', async () => {
    const { readIndex } = install()
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
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
    const { readIndex } = install({ index: (runId) => (calls++ === 0 ? late.promise : Promise.resolve(indexFor(runId))) })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    const staleClose = result.current.close
    act(() => result.current.close())

    await act(async () => late.resolve(indexFor(RUN)))
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
    install({ index: (runId) => (runId === RUN && aCalls++ === 0 ? lateA.promise : Promise.resolve(indexFor(runId))) })
    const { result, rerender } = renderHook(({ run }: { run: string }) => useLocalExecutionInspection(run), { initialProps: { run: RUN } })
    act(() => result.current.show())

    rerender({ run: RUN_B })
    expect(result.current.open).toBe(false)
    rerender({ run: RUN })
    expect(result.current.open).toBe(false)
    expect(result.current.index).toBeNull()

    await act(async () => lateA.resolve(indexFor(RUN)))
    expect(result.current.index).toBeNull()
    act(() => result.current.show())
    await flush()
    expect(result.current.index?.status).toBe('Recorded')
  })

  it('does not settle after unmount', async () => {
    const late = deferred<unknown>()
    install({ index: () => late.promise })
    const { result, unmount } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    unmount()

    await expect(act(async () => late.resolve(indexFor(RUN)))).resolves.toBeUndefined()
  })

  it('lets only the latest overlapping step choice settle', async () => {
    const slow = deferred<unknown>()
    install({ step: (_run, stepId) => (stepId === stepIdFor(1) ? slow.promise : Promise.resolve(agentStepAnswerFor(0))) })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(1)))
    act(() => result.current.selectStep(stepIdFor(0)))
    await flush()

    await act(async () => slow.resolve(verificationStepAnswerFor(1)))

    expect(result.current.selectedStep?.stepId).toBe(stepIdFor(0))
    expect(result.current.stepAnswer?.kind).toBe('agent')
  })

  it('a retained Hide of an earlier step visit never clears a later choice, also when queued in the same batch', async () => {
    install()
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
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

  it('never reads an unrelated page of history and sends no mutation: only the three inspection reads exist', async () => {
    const { readIndex, readStep, readWindow } = install()
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()
    act(() => result.current.selectStep(stepIdFor(2)))
    await flush()

    expect(readIndex).toHaveBeenCalledTimes(1)
    expect(readStep).toHaveBeenCalledTimes(1)
    expect(readWindow).not.toHaveBeenCalled()
    expect(vi.mocked(localExecutionInspectionIndexClient)).toHaveBeenCalledTimes(1)
  })

  it('NotRecorded is shown as itself', async () => {
    install({ index: () => Promise.resolve(notRecordedIndexFor(RUN)) })
    const { result } = renderHook(() => useLocalExecutionInspection(RUN))
    act(() => result.current.show())
    await flush()

    expect(result.current.index).toEqual({ status: 'NotRecorded' })
  })
})

describe('useLocalExecutionInspectionWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  const descriptor = streamFor(guidOf('a', 1), 100) as never
  const selection = (visit = 1) => ({ stream: 'stdout' as const, visit, descriptor })
  const target = (stepKey: string | null = 'step-visit-1', stream: ReturnType<typeof selection> | null = selection()) => ({
    stepKey,
    runId: RUN,
    sequenceId: SEQUENCE,
    stepId: stepIdFor(1),
    executionId: operationIdFor(1),
    stream,
  })
  const serving = () => (_run: string, _step: string, _stream: string, from: number, max: number) =>
    Promise.resolve(windowAnswerFor(1, 'stdout', streamFor(guidOf('a', 1), 100), 'x'.repeat(Math.min(max, 100 - from)), from))

  it('reads nothing until load, then the first window with the step, stream, zero offset and the 16 KiB default cap', async () => {
    const { readWindow } = install({ window: serving() })
    const { result } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: target() })
    expect(readWindow).not.toHaveBeenCalled()

    act(() => result.current.load())
    await flush()

    expect(readWindow).toHaveBeenCalledWith(RUN, stepIdFor(1), 'stdout', 0, 16384)
    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 100, totalLength: 100, isComplete: true })
  })

  it('stays idle without a chosen stream or step visit', () => {
    const { readWindow } = install({ window: serving() })
    const { result } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: target(null) })
    act(() => result.current.load())
    expect(result.current.state).toEqual({ status: 'idle' })
    expect(readWindow).not.toHaveBeenCalled()
  })

  it('discards the text and a late answer when the stream visit changes', async () => {
    const late = deferred<unknown>()
    install({ window: () => late.promise })
    const { result, rerender } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: target() })
    act(() => result.current.load())

    rerender(target('step-visit-1', selection(2)))
    expect(result.current.state).toEqual({ status: 'idle' })
    await act(async () => late.resolve(windowAnswerFor(1, 'stdout', streamFor(guidOf('a', 1), 100), 'x'.repeat(100), 0)))

    expect(result.current.state).toEqual({ status: 'idle' })
  })

  it('does not let a load retained from an ended stream visit read anything, and the current visit still reads once', async () => {
    const { readWindow } = install({ window: serving() })
    const { result, rerender } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: target() })
    const staleLoad = result.current.load

    rerender(target('step-visit-1', selection(2)))
    act(() => staleLoad())
    await flush()
    expect(readWindow).not.toHaveBeenCalled()
    expect(result.current.state).toEqual({ status: 'idle' })

    act(() => result.current.load())
    await flush()
    expect(readWindow).toHaveBeenCalledTimes(1)
  })

  it('maps a refusal for the proved execution and an incoherent answer, keeping a retry at the same offset', async () => {
    let calls = 0
    const { readWindow } = install({
      window: () => Promise.resolve(calls++ === 0 ? { ...refusedWindowFor(1, 'stdout', 'Missing', 0), extra: 1 } : refusedWindowFor(1, 'stdout', 'Missing', 0)),
    })
    const { result } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: target() })

    act(() => result.current.load())
    await flush()
    expect(result.current.state).toMatchObject({ status: 'error', failure: 'invalid' })
    act(() => result.current.load())
    await flush()

    expect(result.current.state).toEqual({ status: 'missing' })
    expect(readWindow).toHaveBeenNthCalledWith(2, RUN, stepIdFor(1), 'stdout', 0, 16384)
  })

  it('stops reading once fewer than 64 bytes of the 256 KiB source view budget remain and discloses a local limit', async () => {
    const big = streamFor(guidOf('a', 1), 1_000_000) as never
    const readWindow = vi.fn((_run: string, _step: string, _stream: string, from: number, max: number) =>
      Promise.resolve(windowAnswerFor(1, 'stdout', big, 'x'.repeat(max), from)),
    )
    vi.mocked(localExecutionInspectionWindowClient).mockReturnValue({ readWindow } as never)
    const { result } = renderHook((props) => useLocalExecutionInspectionWindow(props), { initialProps: { ...target(), stream: { stream: 'stdout' as const, visit: 1, descriptor: big } } })

    for (let index = 0; index < 20; index += 1) {
      act(() => result.current.load())
      await flush()
    }

    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 256 * 1024, viewLimitReached: true, isComplete: false })
    expect(readWindow).toHaveBeenCalledTimes(16)
    expect(readWindow).toHaveBeenLastCalledWith(RUN, stepIdFor(1), 'stdout', 240 * 1024, 16384)
  })
})
