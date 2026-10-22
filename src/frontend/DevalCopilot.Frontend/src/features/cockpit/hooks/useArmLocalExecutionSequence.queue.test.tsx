import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { armLocalExecutionSequenceClient, queueLocalExecutionSequenceClient } from '../../../api/clients'
import type { LocalExecutionAdmissionMode, LocalExecutionArmTuple } from '../localExecutionSequence'
import { LOCAL_QUEUE_TUPLE, LOCAL_TUPLE, localArmReceiptFor, localQueueReceiptFor } from '../localExecutionSequenceFixture'
import { useArmLocalExecutionSequence } from './useArmLocalExecutionSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  armLocalExecutionSequenceClient: vi.fn(),
  queueLocalExecutionSequenceClient: vi.fn(),
}))

function install(armAnswer: unknown, queueAnswer: unknown) {
  const arm = vi.fn(() => Promise.resolve(armAnswer))
  const queue = vi.fn(() => Promise.resolve(queueAnswer))
  vi.mocked(armLocalExecutionSequenceClient).mockImplementation(
    () => ({ armLocalExecutionSequenceRaw: arm }) as unknown as ReturnType<typeof armLocalExecutionSequenceClient>,
  )
  vi.mocked(queueLocalExecutionSequenceClient).mockImplementation(
    () => ({ queueLocalExecutionSequenceRaw: queue }) as unknown as ReturnType<typeof queueLocalExecutionSequenceClient>,
  )
  return { arm, queue }
}

interface Props {
  tuple: LocalExecutionArmTuple
  mode: LocalExecutionAdmissionMode
}

function mounted(initial: Props) {
  const settled = vi.fn()
  const onSettled = vi.fn()
  const onReceipt = vi.fn()
  const reader = { beginCommand: () => ({ settled }) }
  const view = renderHook(
    (props: Props) => useArmLocalExecutionSequence('run-1', props.tuple, reader, onSettled, onReceipt, props.mode),
    { initialProps: initial },
  )
  return { ...view, onReceipt, settled }
}

describe('useArmLocalExecutionSequence in the queue mode (ADR-0062)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('queues through the queue operation only, with the queue tuple, and reports the queued receipt', async () => {
    const { arm, queue } = install(localArmReceiptFor('run-1'), localQueueReceiptFor('run-1'))
    const { result, onReceipt, settled } = mounted({ tuple: LOCAL_QUEUE_TUPLE, mode: 'WaitForAdmission' })

    let accepted = false
    await act(async () => {
      accepted = await result.current.arm('run-1', LOCAL_QUEUE_TUPLE)
    })

    expect(accepted).toBe(true)
    expect(queue).toHaveBeenCalledTimes(1)
    expect(queue).toHaveBeenCalledWith('run-1', LOCAL_QUEUE_TUPLE)
    expect(arm).not.toHaveBeenCalled()
    expect(settled).toHaveBeenCalledTimes(1)
    expect(onReceipt).toHaveBeenCalledWith(expect.objectContaining({ admissionMode: 'WaitForAdmission', state: 'WaitingForAdmission' }))
  })

  it('arms through the Immediate operation only when the mode is Immediate', async () => {
    const { arm, queue } = install(localArmReceiptFor('run-1'), localQueueReceiptFor('run-1'))
    const { result } = mounted({ tuple: LOCAL_TUPLE, mode: 'Immediate' })

    await act(async () => {
      await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(arm).toHaveBeenCalledTimes(1)
    expect(queue).not.toHaveBeenCalled()
  })

  it('rejects an Immediate receipt answered to a queue request as an invalid answer and reports no receipt', async () => {
    const { queue } = install(localArmReceiptFor('run-1'), localArmReceiptFor('run-1'))
    const { result, onReceipt, settled } = mounted({ tuple: LOCAL_QUEUE_TUPLE, mode: 'WaitForAdmission' })

    let accepted = true
    await act(async () => {
      accepted = await result.current.arm('run-1', LOCAL_QUEUE_TUPLE)
    })

    expect(queue).toHaveBeenCalledTimes(1)
    expect(accepted).toBe(false)
    expect(onReceipt).not.toHaveBeenCalled()
    expect(settled).toHaveBeenCalledTimes(1)
    expect(result.current.error).not.toBeNull()
  })

  it('drops a handler retained from the other mode before any request is sent', async () => {
    const { arm, queue } = install(localArmReceiptFor('run-1'), localQueueReceiptFor('run-1'))
    const { result, rerender } = mounted({ tuple: LOCAL_TUPLE, mode: 'Immediate' })
    const retained = result.current.arm

    rerender({ tuple: LOCAL_QUEUE_TUPLE, mode: 'WaitForAdmission' })
    let accepted = true
    await act(async () => {
      accepted = await retained('run-1', LOCAL_TUPLE)
    })

    expect(accepted).toBe(false)
    expect(arm).not.toHaveBeenCalled()
    expect(queue).not.toHaveBeenCalled()
  })

  it('refuses a tuple that is not the current queue candidate', async () => {
    const { queue } = install(localArmReceiptFor('run-1'), localQueueReceiptFor('run-1'))
    const { result } = mounted({ tuple: LOCAL_QUEUE_TUPLE, mode: 'WaitForAdmission' })

    let accepted = true
    await act(async () => {
      accepted = await result.current.arm('run-1', LOCAL_TUPLE)
    })

    expect(accepted).toBe(false)
    expect(queue).not.toHaveBeenCalled()
  })
})
