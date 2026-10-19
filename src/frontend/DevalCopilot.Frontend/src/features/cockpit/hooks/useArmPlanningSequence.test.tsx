import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { armPlanningSequenceClient } from '../../../api/clients'
import { ApiException, ApiProblemDetails } from '../../../api/generated/api-client'
import type { ArmPlanningSequenceRequest } from '../../../api/generated/api-client'
import type { PlanningSequenceTuple } from '../planningSequence'
import { PLANNING_SEQUENCE_UNKNOWN_OUTCOME } from '../planningSequenceCopy'
import { FINGERPRINT, TUPLE, armReceiptFor, planningGuid } from '../planningSequenceFixture'
import { useArmPlanningSequence } from './useArmPlanningSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  armPlanningSequenceClient: vi.fn(),
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

function install(arm: (runId: string, request: ArmPlanningSequenceRequest) => Promise<unknown>) {
  const armPlanningSequenceRaw = vi.fn(arm)
  vi.mocked(armPlanningSequenceClient).mockReturnValue({ armPlanningSequenceRaw } as unknown as ReturnType<typeof armPlanningSequenceClient>)
  return armPlanningSequenceRaw
}

interface Props {
  runId: string
  candidate: PlanningSequenceTuple | null
}

function mounted(refreshStatus = vi.fn(), onReceipt = vi.fn(), initial: Props = { runId: 'run-1', candidate: TUPLE }) {
  const view = renderHook((props: Props) => useArmPlanningSequence(props.runId, props.candidate, refreshStatus, onReceipt), {
    initialProps: initial,
  })
  return { ...view, refreshStatus, onReceipt }
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('useArmPlanningSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('sends one request whose body is exactly the candidate tuple, then reads the recorded status once and reports the receipt', async () => {
    const send = install(() => Promise.resolve(armReceiptFor('run-1')))
    const { result, refreshStatus, onReceipt } = mounted()

    let accepted = false
    await act(async () => {
      accepted = await result.current.arm('run-1', TUPLE)
    })

    expect(accepted).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0][0]).toBe('run-1')
    expect(JSON.parse(JSON.stringify(send.mock.calls[0][1]))).toEqual(TUPLE)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(onReceipt).toHaveBeenCalledWith(expect.objectContaining({ replayed: false, state: 'Armed', revision: '1' }))
    expect(result.current).toMatchObject({ requesting: false, error: null })
  })

  it('reports a replayed receipt as the recorded receipt, never as a second arming', async () => {
    install(() => Promise.resolve(armReceiptFor('run-1', { replayed: true, state: 'AwaitingStepResult', revision: '5' })))
    const { result, onReceipt } = mounted()

    await act(async () => {
      await result.current.arm('run-1', TUPLE)
    })

    expect(onReceipt).toHaveBeenCalledTimes(1)
    expect(onReceipt).toHaveBeenCalledWith(expect.objectContaining({ replayed: true, state: 'AwaitingStepResult' }))
  })

  it('ignores a second request while one is in flight and shows the pending state meanwhile', async () => {
    const pending = deferred<unknown>()
    const send = install(() => pending.promise)
    const { result } = mounted()

    let first!: Promise<boolean>
    let second!: Promise<boolean>
    act(() => {
      first = result.current.arm('run-1', TUPLE)
      second = result.current.arm('run-1', TUPLE)
    })

    expect(result.current.requesting).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    await act(async () => {
      pending.resolve(armReceiptFor('run-1'))
      await pending.promise
    })
    expect(await first).toBe(true)
    expect(await second).toBe(false)
    expect(result.current.requesting).toBe(false)
  })

  it('sends nothing for another run, another tuple or no candidate', async () => {
    const send = install(() => Promise.resolve(armReceiptFor('run-1')))
    const { result, rerender } = mounted()

    await act(async () => {
      expect(await result.current.arm('run-2', TUPLE)).toBe(false)
      expect(await result.current.arm('run-1', { ...TUPLE, checkpointId: planningGuid(9) })).toBe(false)
      expect(await result.current.arm('run-1', { ...TUPLE, fingerprintSha256: 'cd'.repeat(32) })).toBe(false)
    })
    rerender({ runId: 'run-1', candidate: null })
    await act(async () => {
      expect(await result.current.arm('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it.each([
    ['planning_sequence.tuple_stale', /no longer the current source/],
    ['planning_sequence.tuple_conflict', /different workspace, checkpoint or fingerprint/],
    ['planning_sequence.project_busy', /active or in an unknown state/],
    ['planning_sequence.run_not_pristine', /created run with no attempt/],
    ['planning_sequence.manual_control_blocked', /Stop after current step/],
    ['planning_sequence.run_not_manual', /manual Agent run/],
    ['planning_sequence.workspace_not_ready', /ready and owned/],
    ['planning_sequence.lease_not_active', /active workspace lease/],
    ['planning_sequence.checkpoint_missing', /current Git checkpoint/],
  ])('words the refusal %s with fixed copy, never the server text, and is never resent', async (code, copy) => {
    const send = install(() => Promise.reject(refused(409, code)))
    const { result, refreshStatus } = mounted()

    let accepted = true
    await act(async () => {
      accepted = await result.current.arm('run-1', TUPLE)
    })

    expect(accepted).toBe(false)
    expect(result.current.error).toMatch(copy)
    expect(result.current.error).not.toContain('server text')
    expect(send).toHaveBeenCalledTimes(1)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['a failure with no HTTP response', () => Promise.reject(new TypeError('network down'))],
    ['a 500 answer', () => Promise.reject(new ApiException('x', 500, '{"errors":[{"code":"planning_sequence.persistence_unresolved"}]}', {}, null))],
    ['a success answer for another run', () => Promise.resolve(armReceiptFor('run-2'))],
    ['a success answer with no body', () => Promise.resolve(undefined)],
    ['a success answer for another tuple', () => Promise.resolve(armReceiptFor('run-1', { fingerprintSha256: 'cd'.repeat(32) }))],
    ['a first arming that is not Armed', () => Promise.resolve(armReceiptFor('run-1', { state: 'Stopped' }))],
  ])('treats %s as an unknown outcome: fixed copy, one status read, no resend and no receipt', async (_, respond) => {
    const send = install(respond)
    const { result, refreshStatus, onReceipt } = mounted()

    let accepted = true
    await act(async () => {
      accepted = await result.current.arm('run-1', TUPLE)
    })
    await act(async () => undefined)

    expect(accepted).toBe(false)
    expect(result.current.error).toBe(PLANNING_SEQUENCE_UNKNOWN_OUTCOME)
    expect(send).toHaveBeenCalledTimes(1)
    expect(refreshStatus).toHaveBeenCalledTimes(1)
    expect(onReceipt).not.toHaveBeenCalled()
  })

  it('never retries by itself: no further request after a failure, with time passing', async () => {
    vi.useFakeTimers()
    try {
      const send = install(() => Promise.reject(new TypeError('network down')))
      const { result } = mounted()
      await act(async () => {
        await result.current.arm('run-1', TUPLE)
      })

      await act(async () => vi.advanceTimersByTimeAsync(60_000))

      expect(send).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('does not let the completion of a replaced owner touch its replacement, yet still reads the recorded status', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const { result, rerender, refreshStatus, onReceipt } = mounted()
    let inFlight!: Promise<boolean>
    act(() => {
      inFlight = result.current.arm('run-1', TUPLE)
    })
    expect(result.current.requesting).toBe(true)

    rerender({ runId: 'run-2', candidate: { ...TUPLE, checkpointId: planningGuid(7) } })
    expect(result.current).toMatchObject({ requesting: false, error: null })
    await act(async () => {
      pending.resolve(armReceiptFor('run-1'))
      await pending.promise
    })

    expect(await inFlight).toBe(false)
    expect(onReceipt).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ requesting: false, error: null })
    // The accepted request is real, so the recorded status is read regardless of who owns the interaction now.
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })

  it('holds no browser storage state', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    install(() => Promise.resolve(armReceiptFor('run-1')))
    const { result } = mounted()

    await act(async () => {
      await result.current.arm('run-1', TUPLE)
    })

    expect(setItem).not.toHaveBeenCalled()
    setItem.mockRestore()
    expect(FINGERPRINT).toHaveLength(64)
  })
})
