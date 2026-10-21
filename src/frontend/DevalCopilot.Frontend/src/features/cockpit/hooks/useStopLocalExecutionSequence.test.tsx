import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { stopLocalExecutionSequenceClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { LOCAL_EXECUTION_UNKNOWN_OUTCOME } from '../localExecutionSequenceCopy'
import { LOCAL_SEQUENCE_ID, localGuid, localStopAnswerFor } from '../localExecutionSequenceFixture'
import { BOUNDED_COMMAND_TIMEOUT_MS } from './boundedCommand'
import { LOCAL_EXECUTION_COMMAND_BUSY } from './useArmLocalExecutionSequence'
import { useStopLocalExecutionSequence } from './useStopLocalExecutionSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  stopLocalExecutionSequenceClient: vi.fn(),
}))

const signals: AbortSignal[] = []

function install(stop: (runId: string, sequenceId: string, signal: AbortSignal) => Promise<unknown>) {
  const stopLocalExecutionSequenceRaw = vi.fn()
  vi.mocked(stopLocalExecutionSequenceClient).mockImplementation((signal?: AbortSignal) => {
    signals.push(signal!)
    return {
      stopLocalExecutionSequenceRaw: (runId: string, sequenceId: string) => {
        stopLocalExecutionSequenceRaw(runId, sequenceId)
        return stop(runId, sequenceId, signal!)
      },
    } as unknown as ReturnType<typeof stopLocalExecutionSequenceClient>
  })
  return stopLocalExecutionSequenceRaw
}

interface Props {
  runId: string
  sequenceId: string | null
}

function mounted(beginCommand: () => { settled: () => void } | null, initial: Props = { runId: 'run-1', sequenceId: LOCAL_SEQUENCE_ID }) {
  const onAnswer = vi.fn()
  const reader = { beginCommand }
  const view = renderHook((props: Props) => useStopLocalExecutionSequence(props.runId, props.sequenceId, reader, onAnswer), {
    initialProps: initial,
  })
  return { ...view, onAnswer }
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('useStopLocalExecutionSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    signals.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sends one stop for exactly the run and sequence, settles the barrier and reports the answer', async () => {
    const send = install(() => Promise.resolve(localStopAnswerFor('run-1')))
    const settled = vi.fn()
    const { result, onAnswer } = mounted(() => ({ settled }))

    let accepted = false
    await act(async () => {
      accepted = await result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })

    expect(accepted).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledWith('run-1', LOCAL_SEQUENCE_ID)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(onAnswer).toHaveBeenCalledWith(expect.objectContaining({ state: 'Stopping', changed: true }))
  })

  it('never resends a failed stop and reconciles only through the barrier', async () => {
    const send = install(() => Promise.reject(new Error('socket closed')))
    const settled = vi.fn()
    const { result } = mounted(() => ({ settled }))

    await act(async () => {
      await result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })
    await act(async () => vi.advanceTimersByTimeAsync(60_000))

    expect(send).toHaveBeenCalledTimes(1)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it('words a refusal from its stable code only', async () => {
    install(() => Promise.reject(refused(404, 'local_execution_sequence.not_found')))
    const { result } = mounted(() => ({ settled: vi.fn() }))

    await act(async () => {
      await result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })

    expect(result.current.error).toBe('This local execution sequence was not found for this run.')
  })

  it('aborts after ten seconds and treats a foreign answer as an unknown outcome', async () => {
    install((_runId, _sequenceId, signal) => new Promise<unknown>((_, reject) => signal.addEventListener('abort', () => reject(new DOMException('a', 'AbortError')))))
    const { result } = mounted(() => ({ settled: vi.fn() }))
    act(() => {
      void result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })
    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS))
    expect(signals[0].aborted).toBe(true)
    expect(result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)

    install(() => Promise.resolve(localStopAnswerFor('run-1', { sequenceId: localGuid(9) })))
    const other = mounted(() => ({ settled: vi.fn() }))
    await act(async () => {
      await other.result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })
    expect(other.onAnswer).not.toHaveBeenCalled()
    expect(other.result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it('sends nothing for another run or sequence, without a sequence or without a barrier', async () => {
    const send = install(() => Promise.resolve(localStopAnswerFor('run-1')))
    const { result, rerender } = mounted(() => ({ settled: vi.fn() }))
    await act(async () => {
      await result.current.stop('run-2', LOCAL_SEQUENCE_ID)
      await result.current.stop('run-1', localGuid(9))
    })
    rerender({ runId: 'run-1', sequenceId: null })
    await act(async () => {
      await result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })
    expect(send).not.toHaveBeenCalled()

    const blocked = mounted(() => null)
    await act(async () => {
      await blocked.result.current.stop('run-1', LOCAL_SEQUENCE_ID)
    })
    expect(send).not.toHaveBeenCalled()
    expect(blocked.result.current.error).toBe(LOCAL_EXECUTION_COMMAND_BUSY)
  })
})
