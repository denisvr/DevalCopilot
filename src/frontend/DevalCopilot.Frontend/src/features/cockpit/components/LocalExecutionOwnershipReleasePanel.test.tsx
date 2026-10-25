import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionOwnershipReleaseCommandClient, localExecutionOwnershipReleaseReadClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { BOUNDED_COMMAND_TIMEOUT_MS } from '../hooks/boundedCommand'
import { RELEASE_CONFIRMATION_LIFETIME_MS } from '../hooks/useLocalExecutionOwnershipRelease'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import {
  RELEASED_LINE,
  RELEASE_CONFIRM_LABEL,
  RELEASE_EXPIRED,
  RELEASE_HIDE_LABEL,
  RELEASE_READ_AGAIN_LABEL,
  RELEASE_READING,
  RELEASE_REGION_LABEL,
  RELEASE_UNKNOWN_OUTCOME,
} from '../localExecutionOwnershipReleaseCopy'
import {
  RELEASE_CONFIRMATION,
  RELEASE_OBSERVATION_ID,
  RELEASE_RUN,
  availableReleaseFor,
  notEligibleReleaseFor,
  releasedReleaseFor,
  unavailableReleaseFor,
} from '../localExecutionOwnershipReleaseFixture'
import { LOCAL_EXECUTION_RELEASED_NOTE } from '../localExecutionSequenceCopy'
import { localReaderFor, localStatusFor, localStepFor } from '../localExecutionSequenceFixture'
import { LocalExecutionSequencePanel } from './LocalExecutionSequencePanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionOwnershipReleaseReadClient: vi.fn(),
  localExecutionOwnershipReleaseCommandClient: vi.fn(),
}))

function install(read: () => Promise<unknown>, command: () => Promise<unknown> = () => Promise.resolve(releasedReleaseFor())) {
  const readRelease = vi.fn(read)
  const releaseRaw = vi.fn(command)
  vi.mocked(localExecutionOwnershipReleaseReadClient).mockReturnValue({ readRelease } as unknown as ReturnType<typeof localExecutionOwnershipReleaseReadClient>)
  vi.mocked(localExecutionOwnershipReleaseCommandClient).mockReturnValue({ releaseRaw } as unknown as ReturnType<typeof localExecutionOwnershipReleaseCommandClient>)
  return { readRelease, releaseRaw }
}

const sequencePatch = {
  steps: [localStepFor(0, 'quarantined')],
  reasonCode: 'exit_evidence_missing',
  reasonText: 'A dispatched operation ended without recorded process exit evidence.',
}

const quarantinedReader = (overrides: Partial<UseLocalExecutionSequenceResult> = {}) =>
  localReaderFor(localStatusFor(RELEASE_RUN, 'NeedsAttention', {}, sequencePatch), RELEASE_RUN, overrides)

const releasedReader = () =>
  localReaderFor(
    localStatusFor(RELEASE_RUN, 'NeedsAttention', { manualFence: 'Released' }, { ...sequencePatch, steps: [localStepFor(0, 'released')] }),
    RELEASE_RUN,
  )

const renderPanel = (reader: UseLocalExecutionSequenceResult | null) =>
  render(<LocalExecutionSequencePanel runId={RELEASE_RUN} localExecutionSequence={reader} planProposalMessageId={null} />)

const region = () => screen.getByRole('region', { name: RELEASE_REGION_LABEL })

async function openAndSelect() {
  await act(async () => {
    fireEvent.click(within(region()).getByRole('button', { name: RELEASE_REGION_LABEL }))
  })
  await act(async () => {
    fireEvent.click(within(region()).getByRole('button', { name: /^Select step 1/ }))
  })
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('LocalExecutionOwnershipReleasePanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('is mounted collapsed beside the sequence controls only while the accepted status shows a quarantine, and reads nothing', async () => {
    const { readRelease } = install(() => Promise.resolve(availableReleaseFor()))
    const { unmount } = renderPanel(localReaderFor(localStatusFor(RELEASE_RUN, 'ReadyForHumanApproval'), RELEASE_RUN))
    expect(screen.queryByRole('region', { name: RELEASE_REGION_LABEL })).toBeNull()
    unmount()

    renderPanel(quarantinedReader())
    expect(within(region()).getByRole('button', { name: RELEASE_REGION_LABEL })).toHaveAttribute('aria-expanded', 'false')
    expect(within(region()).queryByRole('list')).toBeNull()
    await act(async () => vi.advanceTimersByTimeAsync(RELEASE_CONFIRMATION_LIFETIME_MS))
    expect(readRelease).not.toHaveBeenCalled()
  })

  it('explains physical termination, the invoked-process limit, unchanged gates and possible queued progress before anything is read', async () => {
    const { readRelease } = install(() => Promise.resolve(availableReleaseFor()))
    renderPanel(quarantinedReader())
    await act(async () => {
      fireEvent.click(within(region()).getByRole('button', { name: RELEASE_REGION_LABEL }))
    })

    const text = region().textContent ?? ''
    expect(text).toContain('only when the original host recorded')
    expect(text).toContain('evidence about the invoked process only')
    expect(text).toContain('not proof about any descendant')
    expect(text).toContain('the failed result, the ended cycle, the workspace and its source gates')
    expect(text).toContain('already authorized work that was waiting, including a queued cycle')
    expect(text).toContain('through its own fresh checks')
    expect(readRelease).not.toHaveBeenCalled()
  })

  it('reads only the chosen step, discloses the physical evidence and releases once after the explicit confirmation', async () => {
    const { readRelease, releaseRaw } = install(() => Promise.resolve(availableReleaseFor()))
    const settled = vi.fn()
    renderPanel(quarantinedReader({ beginCommand: () => ({ settled }) }))
    await openAndSelect()

    expect(readRelease).toHaveBeenCalledTimes(1)
    expect(releaseRaw).not.toHaveBeenCalled()
    const evidence = screen.getByRole('group', { name: 'Confirm ownership release' })
    expect(evidence).toHaveTextContent('Exit code the operating system reported at termination137')
    expect(evidence).toHaveTextContent('Recorded exit code of the resultnone recorded')
    expect(evidence).toHaveTextContent(RELEASE_OBSERVATION_ID)
    expect(evidence).toHaveTextContent(RELEASE_CONFIRMATION)
    expect(evidence).toHaveTextContent('it is not rewritten, and the ended cycle stays ended')

    await act(async () => {
      fireEvent.click(within(evidence).getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    expect(releaseRaw).toHaveBeenCalledTimes(1)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(region()).toHaveTextContent(RELEASED_LINE)
    expect(region()).toHaveTextContent('Human event sequence42')
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
  })

  it('marks nothing released itself: the step stays quarantined until the host\'s own status shows the release', async () => {
    install(() => Promise.resolve(availableReleaseFor()))
    const { rerender } = renderPanel(quarantinedReader())
    await openAndSelect()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    expect(screen.getByRole('table')).toHaveTextContent('stays owned and excluded')
    expect(screen.getByRole('table')).not.toHaveTextContent(LOCAL_EXECUTION_RELEASED_NOTE)

    rerender(<LocalExecutionSequencePanel runId={RELEASE_RUN} localExecutionSequence={releasedReader()} planProposalMessageId={null} />)

    expect(screen.getByRole('table')).toHaveTextContent(LOCAL_EXECUTION_RELEASED_NOTE)
    expect(screen.getByRole('table')).not.toHaveTextContent('stays owned and excluded')
    expect(region()).toHaveTextContent(RELEASED_LINE)
  })

  it('keeps an in-flight release and its outcome across the status mask', async () => {
    const { releaseRaw } = install(() => Promise.resolve(availableReleaseFor()))
    const { rerender } = renderPanel(quarantinedReader())
    await openAndSelect()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    rerender(<LocalExecutionSequencePanel runId={RELEASE_RUN} localExecutionSequence={null} planProposalMessageId={null} />)
    expect(screen.queryByRole('region', { name: RELEASE_REGION_LABEL })).toBeNull()
    rerender(<LocalExecutionSequencePanel runId={RELEASE_RUN} localExecutionSequence={releasedReader()} planProposalMessageId={null} />)

    expect(region()).toHaveTextContent(RELEASED_LINE)
    expect(releaseRaw).toHaveBeenCalledTimes(1)
  })

  it.each([
    [notEligibleReleaseFor('already_free'), 'already has recorded process exit evidence'],
    [notEligibleReleaseFor('sequence_not_ended'), 'has not ended'],
    [unavailableReleaseFor('evidence_missing'), 'did not record that it observed'],
    [unavailableReleaseFor('evidence_incoherent'), 'cannot be trusted'],
  ])('offers no confirmation for a read that is not available (%#)', async (answer, words) => {
    install(() => Promise.resolve(answer))
    renderPanel(quarantinedReader())
    await openAndSelect()

    expect(region()).toHaveTextContent(words)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
  })

  it('shows the recorded receipt of an already released operation without a confirmation', async () => {
    install(() => Promise.resolve(releasedReleaseFor()))
    renderPanel(releasedReader())
    await openAndSelect()

    expect(region()).toHaveTextContent('already released by a recorded Human confirmation')
    expect(region()).toHaveTextContent('Human event sequence42')
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
  })

  it('discards an expired confirmation, sends nothing and renews it only by an explicit new read', async () => {
    const { readRelease, releaseRaw } = install(() => Promise.resolve(availableReleaseFor()))
    renderPanel(quarantinedReader())
    await openAndSelect()

    await act(async () => vi.advanceTimersByTimeAsync(RELEASE_CONFIRMATION_LIFETIME_MS))
    expect(region()).toHaveTextContent(RELEASE_EXPIRED)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    expect(releaseRaw).not.toHaveBeenCalled()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_READ_AGAIN_LABEL }))
    })
    expect(readRelease).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeEnabled()
  })

  it('words a refusal from its stable code only and an unknown outcome as unknown, never resending', async () => {
    const { releaseRaw } = install(
      () => Promise.resolve(availableReleaseFor()),
      () => Promise.reject(refused(409, 'local_execution_ownership_release.not_eligible')),
    )
    renderPanel(quarantinedReader())
    await openAndSelect()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    expect(screen.getByRole('alert')).toHaveTextContent('no longer retains a quarantine this control may release')
    expect(region()).not.toHaveTextContent('server text')
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    await act(async () => vi.advanceTimersByTimeAsync(60_000))
    expect(releaseRaw).toHaveBeenCalledTimes(1)
  })

  it('words an unreadable answer neutrally: it claims neither that a hold remains nor that one was released (ADR-0066)', async () => {
    const { releaseRaw } = install(
      () => Promise.resolve(availableReleaseFor()),
      () => Promise.reject(refused(409, 'local_execution_ownership_release.unavailable')),
    )
    renderPanel(quarantinedReader())
    await openAndSelect()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    const refusal = screen.getByRole('alert')
    expect(refusal).toHaveTextContent('cannot be trusted')
    expect(refusal).toHaveTextContent('live workspace and capacity readings show what is held')
    expect(refusal.textContent).not.toMatch(/hold stays|still held|remains held|was released|has been released/i)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    await act(async () => vi.advanceTimersByTimeAsync(60_000))
    expect(releaseRaw).toHaveBeenCalledTimes(1)
  })

  it('shows an unavailable read of an operation the live fence already shows released without a control or a claim either way', async () => {
    // The checksum-only case: the receipt's digest no longer verifies, but the live fence (the sequence reader) still shows the release.
    install(() => Promise.resolve(unavailableReleaseFor('evidence_incoherent')))
    renderPanel(releasedReader())
    await openAndSelect()

    expect(region()).toHaveTextContent('cannot be trusted')
    expect(region()).toHaveTextContent('says nothing about whether ownership is held or was released')
    expect(region().textContent).not.toMatch(/hold stays/i)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
  })

  it('says the outcome is unknown after a transport failure and offers only an explicit new read', async () => {
    const { readRelease, releaseRaw } = install(
      () => Promise.resolve(availableReleaseFor()),
      () => Promise.reject(new Error('socket closed')),
    )
    renderPanel(quarantinedReader())
    await openAndSelect()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })

    expect(screen.getByRole('alert')).toHaveTextContent(RELEASE_UNKNOWN_OUTCOME)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_READ_AGAIN_LABEL }))
    })
    expect(readRelease).toHaveBeenCalledTimes(2)
    expect(releaseRaw).toHaveBeenCalledTimes(1)
  })

  it('cannot be hidden, and shows its confirm control disabled, while the release is in flight', async () => {
    install(() => Promise.resolve(availableReleaseFor()), () => new Promise(() => undefined))
    renderPanel(quarantinedReader())
    await openAndSelect()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: RELEASE_CONFIRM_LABEL }))
    })
    expect(within(region()).getByRole('button', { name: RELEASE_HIDE_LABEL })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Releasing…' })).toBeDisabled()
  })

  it.each(['Running', 'Passed', 'NotAStatus'])('offers no confirmation for an Agent candidate whose operation status is %s', async (operationStatus) => {
    const { releaseRaw } = install(() => Promise.resolve(availableReleaseFor({}, { operationStatus })))
    renderPanel(quarantinedReader())
    await openAndSelect()

    expect(region()).toHaveTextContent('not coherent for this operation')
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    expect(screen.queryByRole('group', { name: 'Confirm ownership release' })).toBeNull()
    expect(releaseRaw).not.toHaveBeenCalled()
  })

  it('ends a read that never answers at ten seconds with the fixed read failure and offers no confirmation', async () => {
    const { releaseRaw } = install(() => new Promise<never>(() => undefined))
    renderPanel(quarantinedReader())
    await openAndSelect()
    expect(region()).toHaveTextContent(RELEASE_READING)

    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS))

    expect(region()).toHaveTextContent('The release evidence could not be read. Nothing was changed.')
    expect(region()).not.toHaveTextContent(RELEASE_READING)
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
    expect(releaseRaw).not.toHaveBeenCalled()
  })

  it('says so when the host answered with evidence that is not coherent for the chosen operation', async () => {
    install(() => Promise.resolve(availableReleaseFor({}, { operationId: '00000000-0000-4000-8000-0000000009ff' })))
    renderPanel(quarantinedReader())
    await openAndSelect()

    expect(region()).toHaveTextContent('not coherent for this operation')
    expect(screen.queryByRole('button', { name: RELEASE_CONFIRM_LABEL })).toBeNull()
  })
})
