import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { armPlanningSequenceClient, stopPlanningSequenceClient } from '../../../api/clients'
import { useArmPlanningSequence } from '../hooks/useArmPlanningSequence'
import { useStopPlanningSequence } from '../hooks/useStopPlanningSequence'
import type { PlanningSequenceTuple } from '../planningSequence'
import { SEQUENCE_ID, TUPLE, armReceiptFor, planningGuid, stopAnswerFor } from '../planningSequenceFixture'

// The stale UI action control of ADR-0055: a Stop or an Arm that is issued after the visit, the run, the sequence or the candidate it
// was rendered for has changed must be dropped before any request is sent. The callbacks below are the ones a retained handler or a
// queued click holds: captured from an earlier render and called after the change.
vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  armPlanningSequenceClient: vi.fn(),
  stopPlanningSequenceClient: vi.fn(),
}))

const OTHER_SEQUENCE = planningGuid(2)
const OTHER_TUPLE: PlanningSequenceTuple = { ...TUPLE, checkpointId: planningGuid(9) }

function installStop() {
  const stopPlanningSequenceRaw = vi.fn(() => Promise.resolve(stopAnswerFor('run-1')))
  vi.mocked(stopPlanningSequenceClient).mockReturnValue({ stopPlanningSequenceRaw } as unknown as ReturnType<typeof stopPlanningSequenceClient>)
  return stopPlanningSequenceRaw
}

function installArm() {
  const armPlanningSequenceRaw = vi.fn(() => Promise.resolve(armReceiptFor('run-1')))
  vi.mocked(armPlanningSequenceClient).mockReturnValue({ armPlanningSequenceRaw } as unknown as ReturnType<typeof armPlanningSequenceClient>)
  return armPlanningSequenceRaw
}

interface StopProps {
  runId: string
  sequenceId: string | null
}

function stopHarness() {
  const refreshStatus = vi.fn()
  const onAnswer = vi.fn()
  const view = renderHook((props: StopProps) => useStopPlanningSequence(props.runId, props.sequenceId, refreshStatus, onAnswer), {
    initialProps: { runId: 'run-1', sequenceId: SEQUENCE_ID } as StopProps,
  })
  return { ...view, refreshStatus, onAnswer }
}

interface ArmProps {
  runId: string
  candidate: PlanningSequenceTuple | null
}

function armHarness() {
  const refreshStatus = vi.fn()
  const onReceipt = vi.fn()
  const view = renderHook((props: ArmProps) => useArmPlanningSequence(props.runId, props.candidate, refreshStatus, onReceipt), {
    initialProps: { runId: 'run-1', candidate: TUPLE } as ArmProps,
  })
  return { ...view, refreshStatus, onReceipt }
}

describe('stale UI action: Stop after current step', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('stale UI action: a Stop issued after the sequence changed is dropped', async () => {
    const send = installStop()
    const { result, rerender, refreshStatus, onAnswer } = stopHarness()
    const retained = result.current.stop

    rerender({ runId: 'run-1', sequenceId: OTHER_SEQUENCE })
    let accepted = true
    await act(async () => {
      accepted = await retained('run-1', SEQUENCE_ID)
    })

    expect(accepted).toBe(false)
    expect(send).not.toHaveBeenCalled()
    expect(refreshStatus).not.toHaveBeenCalled()
    expect(onAnswer).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ requesting: false, error: null })
  })

  it('stale UI action: a Stop issued after the run changed is dropped', async () => {
    const send = installStop()
    const { result, rerender } = stopHarness()
    const retained = result.current.stop

    rerender({ runId: 'run-2', sequenceId: SEQUENCE_ID })
    await act(async () => {
      expect(await retained('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('stale UI action: a Stop issued after the visit ended is dropped', async () => {
    const send = installStop()
    const { result, unmount, refreshStatus } = stopHarness()
    const retained = result.current.stop

    unmount()
    await act(async () => {
      expect(await retained('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
    expect(refreshStatus).not.toHaveBeenCalled()
  })

  it('stale UI action: a Stop from the first visit of a sequence is dropped even after the same sequence returns (A, B, A)', async () => {
    const send = installStop()
    const { result, rerender } = stopHarness()
    const retained = result.current.stop

    rerender({ runId: 'run-1', sequenceId: OTHER_SEQUENCE })
    rerender({ runId: 'run-1', sequenceId: SEQUENCE_ID })
    await act(async () => {
      expect(await retained('run-1', SEQUENCE_ID)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
    // The returned visit has its own callback, and that one acts.
    await act(async () => {
      expect(await result.current.stop('run-1', SEQUENCE_ID)).toBe(true)
    })
    expect(send).toHaveBeenCalledTimes(1)
  })

  it('stale UI action: a Stop whose sequence is not the one the control is bound to is dropped', async () => {
    const send = installStop()
    const { result } = stopHarness()

    await act(async () => {
      expect(await result.current.stop('run-1', OTHER_SEQUENCE)).toBe(false)
      expect(await result.current.stop('run-2', SEQUENCE_ID)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('stale UI action: the answer of a Stop that was sent before the sequence changed never reaches the replacement', async () => {
    let finish!: (value: unknown) => void
    const pending = new Promise<unknown>((resolve) => {
      finish = resolve
    })
    const send = vi.fn(() => pending)
    vi.mocked(stopPlanningSequenceClient).mockReturnValue({ stopPlanningSequenceRaw: send } as unknown as ReturnType<typeof stopPlanningSequenceClient>)
    const { result, rerender, onAnswer, refreshStatus } = stopHarness()
    let inFlight!: Promise<boolean>
    act(() => {
      inFlight = result.current.stop('run-1', SEQUENCE_ID)
    })

    rerender({ runId: 'run-1', sequenceId: OTHER_SEQUENCE })
    await act(async () => {
      finish(stopAnswerFor('run-1'))
      await pending
    })

    expect(await inFlight).toBe(false)
    expect(onAnswer).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ requesting: false, error: null })
    // The request already reached the host, so the recorded status is read to show what it did.
    expect(refreshStatus).toHaveBeenCalledTimes(1)
  })
})

describe('stale UI action: Arm planning sequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('stale UI action: an Arm issued after the run changed is dropped', async () => {
    const send = installArm()
    const { result, rerender, refreshStatus, onReceipt } = armHarness()
    const retained = result.current.arm

    rerender({ runId: 'run-2', candidate: TUPLE })
    await act(async () => {
      expect(await retained('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
    expect(refreshStatus).not.toHaveBeenCalled()
    expect(onReceipt).not.toHaveBeenCalled()
  })

  it('stale UI action: an Arm issued after the candidate tuple was replaced is dropped', async () => {
    const send = installArm()
    const { result, rerender } = armHarness()
    const retained = result.current.arm

    rerender({ runId: 'run-1', candidate: OTHER_TUPLE })
    await act(async () => {
      expect(await retained('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('stale UI action: an Arm issued after the candidate disappeared is dropped', async () => {
    const send = installArm()
    const { result, rerender } = armHarness()
    const retained = result.current.arm

    rerender({ runId: 'run-1', candidate: null })
    await act(async () => {
      expect(await retained('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('stale UI action: an Arm issued after the visit ended is dropped', async () => {
    const send = installArm()
    const { result, unmount } = armHarness()
    const retained = result.current.arm

    unmount()
    await act(async () => {
      expect(await retained('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
  })

  it('stale UI action: an Arm from the first visit of a candidate is dropped even after the same candidate returns (A, B, A)', async () => {
    const send = installArm()
    const { result, rerender } = armHarness()
    const retained = result.current.arm

    rerender({ runId: 'run-1', candidate: OTHER_TUPLE })
    rerender({ runId: 'run-1', candidate: TUPLE })
    await act(async () => {
      expect(await retained('run-1', TUPLE)).toBe(false)
    })

    expect(send).not.toHaveBeenCalled()
    await act(async () => {
      expect(await result.current.arm('run-1', TUPLE)).toBe(true)
    })
    expect(send).toHaveBeenCalledTimes(1)
  })
})
