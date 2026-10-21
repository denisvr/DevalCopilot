import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  armLocalExecutionSequenceClient,
  localExecutionSequenceAvailabilityClient,
  stopLocalExecutionSequenceClient,
} from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import {
  LOCAL_EXECUTION_ARM_LABEL,
  LOCAL_EXECUTION_CONFIRMATION_DRIFT,
  LOCAL_EXECUTION_NO_RECOVERY,
  LOCAL_EXECUTION_STOP_LABEL,
} from '../localExecutionSequenceCopy'
import {
  LOCAL_PLAN_ID,
  LOCAL_SEQUENCE_ID,
  LOCAL_TUPLE,
  localArmReceiptFor,
  localAvailabilityFor,
  localRecipe,
  localRefusedAvailabilityFor,
  localReaderFor,
  localStatusFor,
  localStepFor,
  localStopAnswerFor,
  noneLocalReader,
  unknownLocalReader,
} from '../localExecutionSequenceFixture'
import { LocalExecutionSequencePanel } from './LocalExecutionSequencePanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionSequenceAvailabilityClient: vi.fn(),
  armLocalExecutionSequenceClient: vi.fn(),
  stopLocalExecutionSequenceClient: vi.fn(),
}))

const RUN = 'run-1'

function installAvailability(read: (runId: string, planId: string) => Promise<unknown>) {
  const readAvailability = vi.fn(read)
  vi.mocked(localExecutionSequenceAvailabilityClient).mockReturnValue({ readAvailability } as unknown as ReturnType<typeof localExecutionSequenceAvailabilityClient>)
  return readAvailability
}

function installArm(arm: () => Promise<unknown>) {
  const armLocalExecutionSequenceRaw = vi.fn(arm)
  vi.mocked(armLocalExecutionSequenceClient).mockReturnValue({ armLocalExecutionSequenceRaw } as unknown as ReturnType<typeof armLocalExecutionSequenceClient>)
  return armLocalExecutionSequenceRaw
}

function installStop(stop: () => Promise<unknown>) {
  const stopLocalExecutionSequenceRaw = vi.fn(stop)
  vi.mocked(stopLocalExecutionSequenceClient).mockReturnValue({ stopLocalExecutionSequenceRaw } as unknown as ReturnType<typeof stopLocalExecutionSequenceClient>)
  return stopLocalExecutionSequenceRaw
}

const renderPanel = (reader: UseLocalExecutionSequenceResult | null, extra: Partial<Parameters<typeof LocalExecutionSequencePanel>[0]> = {}) =>
  render(<LocalExecutionSequencePanel runId={RUN} localExecutionSequence={reader} planProposalMessageId={LOCAL_PLAN_ID} {...extra} />)

const authorize = () => screen.findByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })
const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('LocalExecutionSequencePanel: arming', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId)))
    installArm(() => Promise.resolve(localArmReceiptFor(RUN)))
    installStop(() => Promise.resolve(localStopAnswerFor(RUN)))
  })

  it('offers nothing but a reading note while the status is not established', () => {
    const { unmount } = renderPanel(null)
    expect(screen.getByRole('status')).toHaveTextContent('Reading the local execution sequence status')
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
    unmount()

    const refresh = vi.fn()
    renderPanel(unknownLocalReader(RUN, { refresh }))
    expect(screen.getByRole('status')).toHaveTextContent('could not be read or validated')
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Read status again' }))
    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('offers the exact button for the settled candidate and discloses everything before any submission', async () => {
    const arm = installArm(() => Promise.resolve(localArmReceiptFor(RUN)))
    installAvailability((runId, planId) =>
      Promise.resolve(
        localAvailabilityFor(runId, planId, {
          recipes: [localRecipe(0), localRecipe(1, { name: 'Lint', timeoutSeconds: 90 })],
          maximumSteps: 12,
          disclosure: {
            codexModel: 'gpt-x', codexEffort: null, claudeModel: 'opus', claudeEffort: 'high', claudeMaxTurns: 25,
            codexAccountUsageStopPercent: 80, codexTokenStopThreshold: '1000', claudeTokenStopThreshold: null,
          },
        }),
      ),
    )
    renderPanel(noneLocalReader(RUN))

    fireEvent.click(await authorize())

    const confirmation = screen.getByRole('group', { name: 'Confirm local execution cycle' })
    expect(arm).not.toHaveBeenCalled()
    expect(confirmation).toHaveTextContent('CAN MUTATE the workspace')
    expect(confirmation).toHaveTextContent('At most 2 shared corrections')
    expect(confirmation).toHaveTextContent('at most two hours')
    expect(confirmation).toHaveTextContent('Approval and commit stay separate and separately authorized')
    expect(confirmation).toHaveTextContent('Stop after current operation prevents successor operations only. It is not process cancellation')
    const recipes = within(screen.getByRole('list', { name: 'Verification commands that can mutate the workspace' })).getAllByRole('listitem')
    expect(recipes.map((item) => item.textContent)).toEqual(['#1 Unit tests · 300s timeout', '#2 Lint · 90s timeout'])
    expect(confirmation).toHaveTextContent(LOCAL_PLAN_ID)
    expect(confirmation).toHaveTextContent('opus / high')
    expect(confirmation).toHaveTextContent('1000')
  })

  it('discloses the smaller remaining correction amount', async () => {
    installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId, { correctionAllowance: 1 })))
    renderPanel(noneLocalReader(RUN))
    fireEvent.click(await authorize())
    expect(screen.getByRole('group', { name: 'Confirm local execution cycle' })).toHaveTextContent('At most 1 shared correction (the smaller remaining amount)')
  })

  it('renders recipe names as inert text, never as markup', async () => {
    const hostile = '<img src=x onerror=alert(1)><b>bold</b>'
    installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId, { recipes: [localRecipe(0, { name: hostile })] })))
    const { container } = renderPanel(noneLocalReader(RUN))
    fireEvent.click(await authorize())

    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('b')).toBeNull()
    expect(within(screen.getByRole('list', { name: 'Verification commands that can mutate the workspace' })).getByRole('listitem').textContent).toContain(hostile)
  })

  it('submits once with exactly the confirmed tuple, then reconciles through the status barrier', async () => {
    const arm = installArm(() => Promise.resolve(localArmReceiptFor(RUN)))
    const settled = vi.fn()
    const beginCommand = vi.fn(() => ({ settled }))
    renderPanel(noneLocalReader(RUN, { beginCommand }))

    fireEvent.click(await authorize())
    fireEvent.click(screen.getByRole('button', { name: 'Confirm and authorize' }))

    await waitFor(() => expect(arm).toHaveBeenCalledTimes(1))
    expect(arm).toHaveBeenCalledWith(RUN, LOCAL_TUPLE)
    await waitFor(() => expect(settled).toHaveBeenCalledTimes(1))
    expect(beginCommand).toHaveBeenCalledTimes(1)
  })

  it('words an arming refusal from its stable code and never echoes the body', async () => {
    installArm(() => Promise.reject(refused(409, 'local_execution_sequence.tuple_stale')))
    renderPanel(noneLocalReader(RUN))
    fireEvent.click(await authorize())
    fireEvent.click(screen.getByRole('button', { name: 'Confirm and authorize' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('no longer the current source of this project')
    expect(alert).not.toHaveTextContent('server text')
  })

  it('discards a confirmation whose availability changed, withholds arming and sends nothing', async () => {
    const arm = installArm(() => Promise.resolve(localArmReceiptFor(RUN)))
    let confirmation = 'cd'.repeat(32)
    const read = installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId, { confirmationSha256: confirmation })))
    const reader = noneLocalReader(RUN)
    const { rerender } = renderPanel(reader, { latestEventSequence: 1 })
    fireEvent.click(await authorize())
    expect(screen.getByRole('group', { name: 'Confirm local execution cycle' })).toBeInTheDocument()

    confirmation = 'ef'.repeat(32)
    rerender(<LocalExecutionSequencePanel runId={RUN} localExecutionSequence={reader} planProposalMessageId={LOCAL_PLAN_ID} latestEventSequence={2} />)

    await waitFor(() => expect(read).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(LOCAL_EXECUTION_CONFIRMATION_DRIFT)).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Confirm local execution cycle' })).toBeNull()
    expect(arm).not.toHaveBeenCalled()
  })

  it('keeps the confirmation when a re-read returns the identical availability', async () => {
    const read = installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId)))
    const reader = noneLocalReader(RUN)
    const { rerender } = renderPanel(reader, { latestEventSequence: 1 })
    fireEvent.click(await authorize())

    rerender(<LocalExecutionSequencePanel runId={RUN} localExecutionSequence={reader} planProposalMessageId={LOCAL_PLAN_ID} latestEventSequence={2} />)
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2))
    await act(async () => undefined)

    expect(screen.getByRole('group', { name: 'Confirm local execution cycle' })).toBeInTheDocument()
    expect(screen.queryByText(LOCAL_EXECUTION_CONFIRMATION_DRIFT)).toBeNull()
  })

  it('shows a fixed refusal instead of an arming button and offers no button without a plan', async () => {
    installAvailability((runId, planId) => Promise.resolve(localRefusedAvailabilityFor(runId, 'local_execution_sequence.recipes_missing', planId)))
    const { unmount } = renderPanel(noneLocalReader(RUN))
    expect(await screen.findByText(/At least one enabled verification recipe is required/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
    unmount()

    renderPanel(noneLocalReader(RUN), { planProposalMessageId: null })
    expect(screen.getByText(/No plan is selected for implementation/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })

  it('withholds arming while another veto holds and says why', async () => {
    renderPanel(noneLocalReader(RUN), { armWithheldReason: 'Withheld: see the execution capacity guidance in this region.' })
    const button = await authorize()
    expect(button).toBeDisabled()
    expect(screen.getByText('Withheld: see the execution capacity guidance in this region.')).toBeInTheDocument()
  })

  it('withholds arming when the availability read failed', async () => {
    installAvailability(() => Promise.reject(new Error('network')))
    renderPanel(noneLocalReader(RUN))
    expect(await screen.findByText(/arming availability of the selected plan could not be read/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })

  it('offers no arming while the status is masked by a command barrier', async () => {
    renderPanel(unknownLocalReader(RUN, { confirming: true, failure: null }))
    expect(screen.getByRole('status')).toHaveTextContent('Reading the local execution sequence status')
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })

  it('treats a reader that belongs to another run as unknown', () => {
    renderPanel(noneLocalReader('run-2'))
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })
})

describe('LocalExecutionSequencePanel: recorded status', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installAvailability((runId, planId) => Promise.resolve(localAvailabilityFor(runId, planId)))
    installStop(() => Promise.resolve(localStopAnswerFor(RUN)))
  })

  const readerFor = (state: Parameters<typeof localStatusFor>[1], sequencePatch: Record<string, unknown> = {}, patch: Record<string, unknown> = {}) =>
    localReaderFor(localStatusFor(RUN, state, patch, sequencePatch), RUN)

  it('words a claim without a dispatch marker as claimed, not dispatched, and infers no running indicator', () => {
    renderPanel(readerFor('AwaitingStepResult'))

    const state = screen.getByRole('status', { name: 'Local execution sequence state' })
    expect(state).toHaveTextContent('Awaiting an operation result')
    expect(screen.getByRole('table')).toHaveTextContent('claimed, not yet dispatched')
    expect(document.querySelector('[data-state="Running"]')).toBeNull()
    expect(state).not.toHaveTextContent(/actively running|currently running|executing now/i)
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })

  it('words a recorded dispatch marker as bookkeeping', () => {
    renderPanel(readerFor('AwaitingStepResult', { steps: [localStepFor(0, 'claimed', { dispatched: true })] }))

    expect(screen.getByRole('table')).toHaveTextContent('dispatch recorded')
    expect(screen.getByText(/not proof that a process is running or that it has stopped/)).toBeInTheDocument()
  })

  it('lists every step receipt with kind, pass, inert recipe, state and operation outcome', () => {
    renderPanel(readerFor('ReadyForHumanApproval', { steps: [
      localStepFor(0, 'concluded'),
      localStepFor(1, 'concluded', { recipeName: '<script>x</script>' }),
      localStepFor(2, 'concluded'),
    ] }))

    const rows = within(screen.getByRole('table')).getAllByRole('row')
    expect(rows).toHaveLength(4)
    expect(rows[1]).toHaveTextContent('Implementation (Claude Code)')
    expect(rows[2]).toHaveTextContent('Verification recipe')
    expect(rows[2]).toHaveTextContent('#1 <script>x</script>')
    expect(rows[2]).toHaveTextContent('outcome Exited')
    expect(rows[3]).toHaveTextContent('outcome ReviewApproved')
    expect(document.querySelector('script')).toBeNull()
  })

  it('reads a successful sequence as awaiting Human approval, not committed and not published', () => {
    renderPanel(readerFor('ReadyForHumanApproval'))

    expect(screen.getByRole('status', { name: 'Local execution sequence state' })).toHaveTextContent('Awaiting Human approval')
    expect(screen.getByText(/not committed and not published, and it is not proof that the source is still current/)).toBeInTheDocument()
    expect(screen.getByLabelText('Local execution sequence final references')).toHaveTextContent('00000000-0000-4000-8000-000000000300')
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })).toBeNull()
    expect(screen.getByText(/never resumed, rearmed or retried/)).toBeInTheDocument()
  })

  it('shows the fixed server reason and next action as literal text', () => {
    renderPanel(readerFor('HumanEscalation'))
    const state = screen.getByRole('status', { name: 'Local execution sequence state' })
    expect(state).toHaveTextContent('The verification diagnosis escalated; a Human decision is required.')
    expect(state).toHaveTextContent('Review the recorded escalation and decide separately.')
  })

  it('says there is no supported recovery action for a quarantined operation and never calls it a running process', () => {
    renderPanel(readerFor('NeedsAttention', {
      steps: [localStepFor(0, 'quarantined')],
      reasonCode: 'exit_evidence_missing',
      reasonText: 'A dispatched operation ended without recorded process exit evidence.',
    }))

    const state = screen.getByRole('status', { name: 'Local execution sequence state' })
    expect(state).toHaveTextContent(LOCAL_EXECUTION_NO_RECOVERY)
    expect(state).toHaveTextContent('stays owned and excluded')
    expect(screen.getByRole('table')).toHaveTextContent('no exit evidence recorded')
    expect(screen.getByRole('table')).toHaveTextContent(LOCAL_EXECUTION_NO_RECOVERY)
    expect(screen.getByRole('table')).not.toHaveTextContent(/actively running|is running/i)
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })).toBeNull()
  })

  it('discloses an elapsed deadline that the host has not persisted yet', () => {
    renderPanel(readerFor('AwaitingStepResult', {
      state: 'NeedsAttention',
      observedBeforePersisted: true,
      reasonCode: 'result_deadline_elapsed',
      reasonText: 'The result deadline elapsed. No process was cancelled.',
      awaitingReconciliation: true,
      nextActionCode: 'no_supported_recovery_action_in_this_version',
      nextActionText: 'No supported recovery action in this version.',
    }, { status: 'NeedsAttention' }))

    expect(screen.getByRole('status', { name: 'Local execution sequence state' })).toHaveTextContent('Deadline elapsed, not yet persisted by the host.')
  })

  it('offers Stop after current operation only while live and sends one stop for the exact sequence', async () => {
    const stop = installStop(() => Promise.resolve(localStopAnswerFor(RUN)))
    const settled = vi.fn()
    const reader = localReaderFor(localStatusFor(RUN, 'AwaitingStepResult'), RUN, { beginCommand: () => ({ settled }) })
    renderPanel(reader)

    const button = screen.getByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })
    expect(screen.getByText(/not process cancellation, run Stop or Pause/)).toBeInTheDocument()
    fireEvent.click(button)

    await waitFor(() => expect(stop).toHaveBeenCalledTimes(1))
    expect(stop).toHaveBeenCalledWith(RUN, LOCAL_SEQUENCE_ID)
    await waitFor(() => expect(settled).toHaveBeenCalledTimes(1))
    expect(await screen.findByText('Stop after current operation was recorded.')).toBeInTheDocument()
  })

  it('shows a stopping sequence with Stop disabled and the drain semantics', () => {
    renderPanel(readerFor('Stopping'))

    expect(screen.getByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })).toBeDisabled()
    expect(screen.getByText(/Stop was already requested; the claimed operation may still finish/)).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Local execution sequence state' })).toHaveTextContent('Stopping after the current operation')
  })

  it('words a stop failure from its stable code only', async () => {
    installStop(() => Promise.reject(refused(404, 'local_execution_sequence.not_found')))
    renderPanel(readerFor('Armed'))

    fireEvent.click(screen.getByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('This local execution sequence was not found for this run.')
    expect(alert).not.toHaveTextContent('server text')
  })

  it('shows a restart as interrupted, never resumed', () => {
    renderPanel(readerFor('Interrupted'))
    expect(screen.getByRole('status', { name: 'Local execution sequence state' })).toHaveTextContent('Interrupted by a host restart. It never resumes automatically.')
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_STOP_LABEL })).toBeNull()
    expect(screen.queryByRole('button', { name: LOCAL_EXECUTION_ARM_LABEL })).toBeNull()
  })
})
