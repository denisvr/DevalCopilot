import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  armLocalExecutionSequenceClient,
  localExecutionQueueAvailabilityClient,
  localExecutionSequenceAvailabilityClient,
  queueLocalExecutionSequenceClient,
  stopLocalExecutionSequenceClient,
} from '../../../api/clients'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import { LOCAL_EXECUTION_ARM_LABEL, LOCAL_EXECUTION_QUEUE_CONFIRM_LABEL, LOCAL_EXECUTION_QUEUE_LABEL, LOCAL_EXECUTION_STOP_LABEL } from '../localExecutionSequenceCopy'
import {
  LOCAL_PLAN_ID,
  LOCAL_QUEUE_TUPLE,
  localArmReceiptFor,
  localAvailabilityFor,
  localQueueAvailabilityFor,
  localQueueFor,
  localQueueReceiptFor,
  localQueuedStatusFor,
  localReaderFor,
  localStopAnswerFor,
  noneLocalReader,
} from '../localExecutionSequenceFixture'
import { LocalExecutionSequencePanel } from './LocalExecutionSequencePanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionSequenceAvailabilityClient: vi.fn(),
  localExecutionQueueAvailabilityClient: vi.fn(),
  armLocalExecutionSequenceClient: vi.fn(),
  queueLocalExecutionSequenceClient: vi.fn(),
  stopLocalExecutionSequenceClient: vi.fn(),
}))

const RUN = 'run-1'

function install() {
  const readAvailability = vi.fn((runId: string, planId: string) => Promise.resolve(localAvailabilityFor(runId, planId)))
  const readQueueAvailability = vi.fn((runId: string, planId: string) => Promise.resolve(localQueueAvailabilityFor(runId, planId)))
  const armLocalExecutionSequenceRaw = vi.fn(() => Promise.resolve(localArmReceiptFor(RUN)))
  const queueLocalExecutionSequenceRaw = vi.fn(() => Promise.resolve(localQueueReceiptFor(RUN)))
  const stopLocalExecutionSequenceRaw = vi.fn(() => Promise.resolve(localStopAnswerFor(RUN)))
  vi.mocked(localExecutionSequenceAvailabilityClient).mockReturnValue({ readAvailability } as unknown as ReturnType<typeof localExecutionSequenceAvailabilityClient>)
  vi.mocked(localExecutionQueueAvailabilityClient).mockReturnValue({ readQueueAvailability } as unknown as ReturnType<typeof localExecutionQueueAvailabilityClient>)
  vi.mocked(armLocalExecutionSequenceClient).mockReturnValue({ armLocalExecutionSequenceRaw } as unknown as ReturnType<typeof armLocalExecutionSequenceClient>)
  vi.mocked(queueLocalExecutionSequenceClient).mockReturnValue({ queueLocalExecutionSequenceRaw } as unknown as ReturnType<typeof queueLocalExecutionSequenceClient>)
  vi.mocked(stopLocalExecutionSequenceClient).mockReturnValue({ stopLocalExecutionSequenceRaw } as unknown as ReturnType<typeof stopLocalExecutionSequenceClient>)
  return { readAvailability, readQueueAvailability, armLocalExecutionSequenceRaw, queueLocalExecutionSequenceRaw, stopLocalExecutionSequenceRaw }
}

const renderPanel = (reader: UseLocalExecutionSequenceResult | null) =>
  render(<LocalExecutionSequencePanel runId={RUN} localExecutionSequence={reader} planProposalMessageId={LOCAL_PLAN_ID} />)

describe('LocalExecutionSequencePanel: the queue mode (ADR-0062)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads both modes and offers authorizing now and queueing as separate buttons', async () => {
    const mocks = install()
    renderPanel(noneLocalReader(RUN))

    expect(await screen.findByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL })).toBeEnabled()
    expect(await screen.findByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeEnabled()
    expect(mocks.readAvailability).toHaveBeenCalledTimes(1)
    expect(mocks.readQueueAvailability).toHaveBeenCalledTimes(1)
  })

  it('opens the queue confirmation only, sends nothing before confirming and queues exactly the queue tuple once', async () => {
    const mocks = install()
    renderPanel(noneLocalReader(RUN))

    fireEvent.click(await screen.findByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL }))

    expect(screen.getByRole('group', { name: 'Confirm queued local execution cycle' })).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Confirm local execution cycle' })).toBeNull()
    expect(mocks.queueLocalExecutionSequenceRaw).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: LOCAL_EXECUTION_QUEUE_CONFIRM_LABEL }))

    await waitFor(() => expect(mocks.queueLocalExecutionSequenceRaw).toHaveBeenCalledTimes(1))
    expect(mocks.queueLocalExecutionSequenceRaw).toHaveBeenCalledWith(RUN, LOCAL_QUEUE_TUPLE)
    expect(mocks.armLocalExecutionSequenceRaw).not.toHaveBeenCalled()
  })

  it('discards a queue confirmation when the queue availability changes and withholds the request with fixed copy', async () => {
    const mocks = install()
    const reader = noneLocalReader(RUN)
    const view = renderPanel(reader)
    fireEvent.click(await screen.findByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL }))
    expect(screen.getByRole('group', { name: 'Confirm queued local execution cycle' })).toBeInTheDocument()

    mocks.readQueueAvailability.mockImplementation((runId: string, planId: string) =>
      Promise.resolve(localQueueAvailabilityFor(runId, planId, { confirmationSha256: '12'.repeat(32) })),
    )
    view.rerender(
      <LocalExecutionSequencePanel runId={RUN} localExecutionSequence={reader} planProposalMessageId={LOCAL_PLAN_ID} latestEventSequence={2} />,
    )

    await waitFor(() => expect(screen.queryByRole('group', { name: 'Confirm queued local execution cycle' })).toBeNull())
    expect(mocks.queueLocalExecutionSequenceRaw).not.toHaveBeenCalled()
  })

  it('shows a waiting queued cycle truthfully with its recorded reason, a Stop control and no start promise', async () => {
    install()
    const reader = localReaderFor(
      localQueuedStatusFor(RUN, 'WaitingForAdmission', {}, { queue: localQueueFor({ waitingReason: 'WaitingForWorkspace' }) }),
      RUN,
    )
    renderPanel(reader)

    expect(await screen.findByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })).toBeEnabled()
    expect(screen.getAllByText(/waiting for admission/i).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL })).toBeNull()
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
    expect(document.body.textContent ?? '').not.toMatch(/will start|starts in|position \d/i)
  })

  it('offers no queue or arm control while a queued cycle is recorded', async () => {
    const mocks = install()
    renderPanel(localReaderFor(localQueuedStatusFor(RUN, 'PreparingStep'), RUN))

    await screen.findByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL })).toBeNull()
    expect(mocks.readQueueAvailability).not.toHaveBeenCalled()
  })

  it('keeps the queue offer open while the Immediate offer is withheld by workspace or capacity, and withholds it only for its own reason', async () => {
    install()
    const withheld = 'Withheld: see the execution capacity guidance in this region.'
    const view = render(
      <LocalExecutionSequencePanel runId={RUN} localExecutionSequence={noneLocalReader(RUN)} planProposalMessageId={LOCAL_PLAN_ID} armWithheldReason={withheld} />,
    )

    expect(await screen.findByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL })).toBeEnabled()
    expect(screen.getByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeDisabled()

    view.rerender(
      <LocalExecutionSequencePanel
        runId={RUN}
        localExecutionSequence={noneLocalReader(RUN)}
        planProposalMessageId={LOCAL_PLAN_ID}
        armWithheldReason={withheld}
        queueWithheldReason="Withheld: planning is live."
      />,
    )
    await waitFor(() => expect(screen.getByRole('button', { name: LOCAL_EXECUTION_QUEUE_LABEL })).toBeDisabled())
    expect(screen.getByText('Withheld: planning is live.')).toBeInTheDocument()
  })
})
