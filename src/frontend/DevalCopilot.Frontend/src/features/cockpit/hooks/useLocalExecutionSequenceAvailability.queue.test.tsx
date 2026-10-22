import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionQueueAvailabilityClient, localExecutionSequenceAvailabilityClient } from '../../../api/clients'
import type { LocalExecutionAdmissionMode } from '../localExecutionSequence'
import { LOCAL_PLAN_ID, LOCAL_QUEUE_CONFIRMATION, localAvailabilityFor, localQueueAvailabilityFor } from '../localExecutionSequenceFixture'
import { useLocalExecutionSequenceAvailability } from './useLocalExecutionSequenceAvailability'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionSequenceAvailabilityClient: vi.fn(),
  localExecutionQueueAvailabilityClient: vi.fn(),
}))

function install(immediateAnswer: unknown, queueAnswer: unknown) {
  const readAvailability = vi.fn(() => Promise.resolve(immediateAnswer))
  const readQueueAvailability = vi.fn(() => Promise.resolve(queueAnswer))
  vi.mocked(localExecutionSequenceAvailabilityClient).mockImplementation(
    () => ({ readAvailability }) as unknown as ReturnType<typeof localExecutionSequenceAvailabilityClient>,
  )
  vi.mocked(localExecutionQueueAvailabilityClient).mockImplementation(
    () => ({ readQueueAvailability }) as unknown as ReturnType<typeof localExecutionQueueAvailabilityClient>,
  )
  return { readAvailability, readQueueAvailability }
}

const mounted = (mode: LocalExecutionAdmissionMode) =>
  renderHook<ReturnType<typeof useLocalExecutionSequenceAvailability>, { mode: LocalExecutionAdmissionMode }>(
    (props) => useLocalExecutionSequenceAvailability('run-1', LOCAL_PLAN_ID, undefined, props.mode),
    { initialProps: { mode } },
  )

const flush = () => act(async () => undefined)

describe('useLocalExecutionSequenceAvailability in both modes (ADR-0062)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads the queue availability only through the queue operation and exposes the queue confirmation', async () => {
    const { readAvailability, readQueueAvailability } = install(localAvailabilityFor('run-1'), localQueueAvailabilityFor('run-1'))
    const { result } = mounted('WaitForAdmission')
    await flush()

    expect(readQueueAvailability).toHaveBeenCalledTimes(1)
    expect(readAvailability).not.toHaveBeenCalled()
    expect(result.current.availability?.admissionMode).toBe('WaitForAdmission')
    expect(result.current.availability?.candidate?.confirmationSha256).toBe(LOCAL_QUEUE_CONFIRMATION)
  })

  it('reads the Immediate availability only through the Immediate operation', async () => {
    const { readAvailability, readQueueAvailability } = install(localAvailabilityFor('run-1'), localQueueAvailabilityFor('run-1'))
    const { result } = mounted('Immediate')
    await flush()

    expect(readAvailability).toHaveBeenCalledTimes(1)
    expect(readQueueAvailability).not.toHaveBeenCalled()
    expect(result.current.availability?.admissionMode).toBe('Immediate')
  })

  it('starts a fresh visit when the mode changes and never exposes the other mode answer', async () => {
    install(localAvailabilityFor('run-1'), localQueueAvailabilityFor('run-1'))
    const { result, rerender } = mounted('Immediate')
    await flush()
    expect(result.current.availability?.admissionMode).toBe('Immediate')

    rerender({ mode: 'WaitForAdmission' })
    expect(result.current.availability).toBeNull()
    expect(result.current.loading).toBe(true)
    await flush()
    expect(result.current.availability?.admissionMode).toBe('WaitForAdmission')

    rerender({ mode: 'Immediate' })
    expect(result.current.availability).toBeNull()
    await flush()
    expect(result.current.availability?.admissionMode).toBe('Immediate')
  })

  it('treats an answer of the other mode as invalid', async () => {
    install(localAvailabilityFor('run-1'), localAvailabilityFor('run-1'))
    const { result } = mounted('WaitForAdmission')
    await flush()

    expect(result.current.availability).toBeNull()
    expect(result.current.failure).toBe('invalid')
  })
})
