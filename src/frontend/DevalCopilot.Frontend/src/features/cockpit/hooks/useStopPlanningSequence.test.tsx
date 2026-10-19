import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { stopPlanningSequenceClient } from '../../../api/clients'
import { ApiException, ApiProblemDetails } from '../../../api/generated/api-client'
import { PLANNING_SEQUENCE_UNKNOWN_OUTCOME } from '../planningSequenceCopy'
import { SEQUENCE_ID, planningGuid, stopAnswerFor } from '../planningSequenceFixture'
import { useStopPlanningSequence } from './useStopPlanningSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  stopPlanningSequenceClient: vi.fn(),
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

function install(stop: (runId: string, sequenceId: string) => Promise<unknown>) {
  const stopPlanningSequenceRaw = vi.fn(stop)
  vi.mocked(stopPlanningSequenceClient).mockReturnValue({ stopPlanningSequenceRaw } as unknown as ReturnType<typeof stopPlanningSequenceClient>)
  return stopPlanningSequenceRaw
}

interface Props {
  runId: string
  sequenceId: string | null
}

function mounted(refreshStatus = vi.fn(), onAnswer = vi.fn(), initial: Props = { runId: 'run-1', sequenceId: SEQUENCE_ID }) {
  const view = renderHook((props: Props) => useStopPlanningSequence(props.runId, props.sequenceId, refreshStatus, onAnswer), {
    initialProps: initial,
  })
  return { ...view, refreshStatus, onAnswer }
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('useStopPlanningSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('stops exactly the run and sequence it is bound to with one bodyless request, then reads the recorded status once', async () => {
    const send = install(() => Promise.resolve(stopAnswerFor('run-1')))
    const { result, refreshStatus, onAnswer } = mounted()

    let accepted = false
    await act(async () => {
      accepted = await result.current.stop('run-1', SEQUENCE_ID)
    })

    expect(accepted).toBe(true)
    expect(send).toHaveBeenCalledExactlyOnceWith('run-1', SEQUENCE_ID)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(onAnswer).toHaveBeenCalledWith(expect.objectContaining({ state: 'Stopping', changed: true }))
  })

  it.each([
    ['a stop that won before the claim', { state: 'Stopped', reasonCode: 'stopped_before_claim', changed: true }],
    ['a stop that lost to the claim and is draining', { state: 'Stopping', reasonCode: null, changed: true }],
    ['a repeated stop', { state: 'Stopping', reasonCode: null, changed: false }],
    ['a late stop after the result concluded the plan', { state: 'ReadyForImplementation', reasonCode: 'plan_accepted', changed: false }],
    ['a late stop after the step drained', { state: 'Stopped', reasonCode: 'stopped_after_step', changed: false }],
  ])('reports %s exactly as answered, with `changed` as the host wrote it, and reads the status', async (_, patch) => {
    install(() => Promise.resolve(stopAnswerFor('run-1', patch)))
    const { result, refreshStatus, onAnswer } = mounted()

    await act(async () => {
      await result.current.stop('run-1', SEQUENCE_ID)
    })

    expect(onAnswer).toHaveBeenCalledWith(expect.objectContaining(patch))
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })

  it('ignores a second stop while one is in flight and shows the pending state meanwhile', async () => {
    const pending = deferred<unknown>()
    const send = install(() => pending.promise)
    const { result } = mounted()

    let first!: Promise<boolean>
    let second!: Promise<boolean>
    act(() => {
      first = result.current.stop('run-1', SEQUENCE_ID)
      second = result.current.stop('run-1', SEQUENCE_ID)
    })

    expect(result.current.requesting).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    await act(async () => {
      pending.resolve(stopAnswerFor('run-1'))
      await pending.promise
    })
    expect(await first).toBe(true)
    expect(await second).toBe(false)
  })

  it('sends nothing for another run, another sequence or no sequence', async () => {
    const send = install(() => Promise.resolve(stopAnswerFor('run-1')))
    const { result, rerender } = mounted()

    await act(async () => {
      expect(await result.current.stop('run-2', SEQUENCE_ID)).toBe(false)
      expect(await result.current.stop('run-1', planningGuid(9))).toBe(false)
    })
    rerender({ runId: 'run-1', sequenceId: null })
    await act(async () => {
      expect(await result.current.stop('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it.each([
    [404, 'planning_sequence.not_found', /could not be found|was not found/],
    [400, 'planning_sequence.invalid_request', /not accepted/],
    [409, 'planning_sequence.manual_control_blocked', /Stop after current step/],
  ])('words the definite refusal %s %s with fixed copy and never resends', async (status, code, copy) => {
    const send = install(() => Promise.reject(refused(status, code)))
    const { result, refreshStatus } = mounted()

    await act(async () => {
      expect(await result.current.stop('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(result.current.error).toMatch(copy)
    expect(result.current.error).not.toContain('server text')
    expect(send).toHaveBeenCalledTimes(1)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['a failure with no HTTP response', () => Promise.reject(new TypeError('network down'))],
    ['a 500 answer', () => Promise.reject(new ApiException('x', 500, '', {}, null))],
    ['an answer for another sequence', () => Promise.resolve(stopAnswerFor('run-1', { sequenceId: planningGuid(9) }))],
    ['an answer that leaves the sequence able to advance', () => Promise.resolve(stopAnswerFor('run-1', { state: 'AwaitingStepResult', changed: false }))],
    ['an answer with no body', () => Promise.resolve(undefined)],
  ])('treats %s as an unknown outcome: fixed copy, one status read and no answer', async (_, respond) => {
    const send = install(respond)
    const { result, refreshStatus, onAnswer } = mounted()

    await act(async () => {
      expect(await result.current.stop('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(result.current.error).toBe(PLANNING_SEQUENCE_UNKNOWN_OUTCOME)
    expect(send).toHaveBeenCalledTimes(1)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(onAnswer).not.toHaveBeenCalled()
  })

  it('never retries by itself: no further request after a failure, with time passing', async () => {
    vi.useFakeTimers()
    try {
      const send = install(() => Promise.reject(new TypeError('network down')))
      const { result } = mounted()
      await act(async () => {
        await result.current.stop('run-1', SEQUENCE_ID)
      })

      await act(async () => vi.advanceTimersByTimeAsync(60_000))

      expect(send).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('lets a late completion of a replaced sequence change nothing for its replacement, yet still reads the recorded status', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender, refreshStatus, onAnswer } = mounted()
    let inFlight!: Promise<boolean>
    act(() => {
      inFlight = result.current.stop('run-1', SEQUENCE_ID)
    })

    rerender({ runId: 'run-1', sequenceId: planningGuid(2) })
    expect(result.current).toMatchObject({ requesting: false, error: null })
    await act(async () => {
      pending.resolve(stopAnswerFor('run-1'))
      await pending.promise
    })

    expect(await inFlight).toBe(false)
    expect(onAnswer).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ requesting: false, error: null })
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })
})
