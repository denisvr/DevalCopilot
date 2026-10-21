import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { armLocalExecutionSequenceClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { LocalExecutionArmTuple } from '../localExecutionSequence'
import { LOCAL_EXECUTION_UNKNOWN_OUTCOME } from '../localExecutionSequenceCopy'
import { LOCAL_TUPLE, localArmReceiptFor, localGuid } from '../localExecutionSequenceFixture'
import { BOUNDED_COMMAND_TIMEOUT_MS } from './boundedCommand'
import { LOCAL_EXECUTION_COMMAND_BUSY, useArmLocalExecutionSequence } from './useArmLocalExecutionSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  armLocalExecutionSequenceClient: vi.fn(),
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

function install(arm: (runId: string, tuple: LocalExecutionArmTuple, signal: AbortSignal) => Promise<unknown>) {
  const armLocalExecutionSequenceRaw = vi.fn()
  vi.mocked(armLocalExecutionSequenceClient).mockImplementation((signal?: AbortSignal) => {
    signals.push(signal!)
    return {
      armLocalExecutionSequenceRaw: (runId: string, tuple: LocalExecutionArmTuple) => {
        armLocalExecutionSequenceRaw(runId, tuple)
        return arm(runId, tuple, signal!)
      },
    } as unknown as ReturnType<typeof armLocalExecutionSequenceClient>
  })
  return armLocalExecutionSequenceRaw
}

const hangUntilAborted = (_runId: string, _tuple: LocalExecutionArmTuple, signal: AbortSignal) =>
  new Promise<unknown>((_, reject) => signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError'))))

interface Props {
  runId: string
  candidate: LocalExecutionArmTuple | null
}

function mounted(beginCommand: () => { settled: () => void } | null, initial: Props = { runId: 'run-1', candidate: LOCAL_TUPLE }) {
  const onSettled = vi.fn()
  const onReceipt = vi.fn()
  const reader = { beginCommand }
  const view = renderHook((props: Props) => useArmLocalExecutionSequence(props.runId, props.candidate, reader, onSettled, onReceipt), {
    initialProps: initial,
  })
  return { ...view, onSettled, onReceipt }
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('useArmLocalExecutionSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    signals.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sends one request with exactly the confirmed tuple, settles the change barrier and reports the receipt', async () => {
    const send = install(() => Promise.resolve(localArmReceiptFor('run-1')))
    const settled = vi.fn()
    const { result, onSettled, onReceipt } = mounted(() => ({ settled }))

    let accepted = false
    await act(async () => {
      accepted = await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(accepted).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledWith('run-1', LOCAL_TUPLE)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(onSettled).toHaveBeenCalledTimes(1)
    expect(onReceipt).toHaveBeenCalledWith(expect.objectContaining({ replayed: false, state: 'Armed', revision: '1' }))
    expect(result.current).toMatchObject({ requesting: false, error: null })
  })

  it('settles the barrier only after the transport has settled, never before', async () => {
    const answer = deferred<unknown>()
    install(() => answer.promise)
    const settled = vi.fn()
    const { result } = mounted(() => ({ settled }))

    let pending: Promise<boolean> | undefined
    act(() => {
      pending = result.current.arm('run-1', LOCAL_TUPLE)
    })
    expect(result.current.requesting).toBe(true)
    expect(settled).not.toHaveBeenCalled()

    await act(async () => {
      answer.resolve(localArmReceiptFor('run-1'))
      await pending
    })
    expect(settled).toHaveBeenCalledTimes(1)
  })

  it('never resends: an unknown outcome is reconciled only through the status barrier', async () => {
    const send = install(() => Promise.reject(new Error('socket closed')))
    const settled = vi.fn()
    const { result, onSettled, onReceipt } = mounted(() => ({ settled }))

    let accepted = true
    await act(async () => {
      accepted = await result.current.arm('run-1', LOCAL_TUPLE)
    })
    await act(async () => vi.advanceTimersByTimeAsync(60_000))

    expect(accepted).toBe(false)
    expect(send).toHaveBeenCalledTimes(1)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(onSettled).toHaveBeenCalledTimes(1)
    expect(onReceipt).not.toHaveBeenCalled()
    expect(result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it('words a refusal from its stable code only and never echoes the body', async () => {
    install(() => Promise.reject(refused(409, 'local_execution_sequence.confirmation_stale')))
    const { result } = mounted(() => ({ settled: vi.fn() }))

    await act(async () => {
      await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(result.current.error).toMatch(/changed after the availability was read/)
    expect(result.current.error).not.toMatch(/server text/)
  })

  it('treats an answer that is not exactly the receipt of the request as an unknown outcome', async () => {
    install(() => Promise.resolve(localArmReceiptFor('run-1', { workspaceId: localGuid(77) })))
    const settled = vi.fn()
    const { result, onReceipt } = mounted(() => ({ settled }))

    await act(async () => {
      await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(onReceipt).not.toHaveBeenCalled()
    expect(settled).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it('aborts the real request after ten seconds, never retries it and reconciles through the barrier', async () => {
    const send = install(hangUntilAborted)
    const settled = vi.fn()
    const { result } = mounted(() => ({ settled }))

    act(() => {
      void result.current.arm('run-1', LOCAL_TUPLE)
    })
    expect(signals[0].aborted).toBe(false)
    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS - 1))
    expect(signals[0].aborted).toBe(false)
    await act(async () => vi.advanceTimersByTimeAsync(1))

    expect(signals[0].aborted).toBe(true)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it('ignores a second submission while one is in flight', async () => {
    const answer = deferred<unknown>()
    const send = install(() => answer.promise)
    const { result } = mounted(() => ({ settled: vi.fn() }))

    act(() => {
      void result.current.arm('run-1', LOCAL_TUPLE)
    })
    let second = true
    await act(async () => {
      second = await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(second).toBe(false)
    expect(send).toHaveBeenCalledTimes(1)
    await act(async () => answer.resolve(localArmReceiptFor('run-1')))
  })

  it('sends nothing for another run, another tuple or no candidate', async () => {
    const send = install(() => Promise.resolve(localArmReceiptFor('run-1')))
    const { result, rerender } = mounted(() => ({ settled: vi.fn() }))

    await act(async () => {
      await result.current.arm('run-2', LOCAL_TUPLE)
      await result.current.arm('run-1', { ...LOCAL_TUPLE, confirmationSha256: 'ef'.repeat(32) })
    })
    rerender({ runId: 'run-1', candidate: null })
    await act(async () => {
      await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('sends nothing when the reader cannot start a change barrier', async () => {
    const send = install(() => Promise.resolve(localArmReceiptFor('run-1')))
    const { result } = mounted(() => null)

    await act(async () => {
      await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(send).not.toHaveBeenCalled()
    expect(result.current.error).toBe(LOCAL_EXECUTION_COMMAND_BUSY)
  })

  it('drops a handler retained from an ended owner and aborts a request in flight when the owner is replaced', async () => {
    const send = install(hangUntilAborted)
    const settled = vi.fn()
    const { result, rerender } = mounted(() => ({ settled }))
    const retained = result.current.arm

    act(() => {
      void result.current.arm('run-1', LOCAL_TUPLE)
    })
    rerender({ runId: 'run-2', candidate: LOCAL_TUPLE })
    await act(async () => undefined)

    expect(signals[0].aborted).toBe(true)
    expect(settled).toHaveBeenCalledTimes(1)
    let late = true
    await act(async () => {
      late = await retained('run-1', LOCAL_TUPLE)
    })
    expect(late).toBe(false)
    expect(send).toHaveBeenCalledTimes(1)
  })
})
