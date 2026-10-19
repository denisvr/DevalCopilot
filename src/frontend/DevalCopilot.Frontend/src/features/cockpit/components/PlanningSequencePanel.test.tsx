import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { armPlanningSequenceClient, stopPlanningSequenceClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { UsePlanningSequenceResult } from '../hooks/usePlanningSequence'
import { PLANNING_SEQUENCE_STATES, derivePlanningSequenceControl, toPlanningSequenceStatus } from '../planningSequence'
import type { PlanningSequenceState } from '../planningSequence'
import {
  CHECKPOINT_ID,
  FINGERPRINT,
  SEQUENCE_ID,
  TUPLE,
  WORKSPACE_ID,
  armReceiptFor,
  armableStatusFor,
  planningGuid,
  refusedStatusFor,
  statusFor,
  stepFor,
  stopAnswerFor,
} from '../planningSequenceFixture'
import { PlanningSequencePanel } from './PlanningSequencePanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  armPlanningSequenceClient: vi.fn(),
  stopPlanningSequenceClient: vi.fn(),
}))

const RUN = 'run-1'
type Raw = Record<string, unknown>

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

function known(raw: unknown, refresh = vi.fn()): UsePlanningSequenceResult {
  const status = toPlanningSequenceStatus(raw, RUN)
  if (status === null) {
    throw new Error('The fixture is not a valid status')
  }
  return { status, loading: false, failure: null, control: derivePlanningSequenceControl(status), ownership: 'known', polling: false, refresh }
}

const unknownStatus = (loading: boolean, refresh = vi.fn()): UsePlanningSequenceResult => ({
  status: null,
  loading,
  failure: loading ? null : 'read',
  control: 'unknown',
  ownership: 'unknown',
  polling: false,
  refresh,
})

const panel = (planningSequence: UsePlanningSequenceResult) => <PlanningSequencePanel runId={RUN} planningSequence={planningSequence} />

function installArm(arm: () => Promise<unknown>) {
  const armPlanningSequenceRaw = vi.fn(arm)
  vi.mocked(armPlanningSequenceClient).mockReturnValue({ armPlanningSequenceRaw } as unknown as ReturnType<typeof armPlanningSequenceClient>)
  return armPlanningSequenceRaw
}

function installStop(stop: () => Promise<unknown>) {
  const stopPlanningSequenceRaw = vi.fn(stop)
  vi.mocked(stopPlanningSequenceClient).mockReturnValue({ stopPlanningSequenceRaw } as unknown as ReturnType<typeof stopPlanningSequenceClient>)
  return stopPlanningSequenceRaw
}

const armButton = () => screen.queryByRole('button', { name: 'Arm planning sequence' })
const stopButton = () => screen.queryByRole('button', { name: 'Stop after current step' })
const stateRegion = () => screen.getByRole('status', { name: 'Planning sequence state' })

describe('PlanningSequencePanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('while the status is not established', () => {
    it('says it is reading, and offers neither arming nor Stop', () => {
      render(panel(unknownStatus(true)))

      expect(screen.getByRole('region', { name: 'Planning sequence' })).toBeInTheDocument()
      expect(screen.getByText('Reading the planning sequence status…')).toBeInTheDocument()
      expect(armButton()).not.toBeInTheDocument()
      expect(stopButton()).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Read status again' })).not.toBeInTheDocument()
    })

    it('says a failed or invalid status withholds arming and manual requests, and offers only a read of the status again', () => {
      const refresh = vi.fn()
      render(panel(unknownStatus(false, refresh)))

      expect(screen.getByText(/withheld until it can/)).toBeInTheDocument()
      expect(armButton()).not.toBeInTheDocument()
      expect(stopButton()).not.toBeInTheDocument()
      fireEvent.click(screen.getByRole('button', { name: 'Read status again' }))
      expect(refresh).toHaveBeenCalledTimes(1)
    })

    it('never offers arming or the candidate when the status is not validated, even beside a status that looks armable', () => {
      render(panel({ ...known(armableStatusFor(RUN)), status: null, control: 'unknown', ownership: 'unknown' }))

      expect(armButton()).not.toBeInTheDocument()
      expect(screen.queryByText(WORKSPACE_ID)).not.toBeInTheDocument()
    })

    it('never offers Stop when ownership is unknown, even beside a status that names a live sequence', () => {
      render(panel({ ...known(statusFor(RUN, 'AwaitingStepResult')), control: 'unknown', ownership: 'unknown' }))

      expect(stopButton()).not.toBeInTheDocument()
    })
  })

  describe('with no sequence', () => {
    it('shows the exact candidate tuple, the five-step maximum, the included second review and the separate implementation boundary', () => {
      render(panel(known(armableStatusFor(RUN))))

      const candidate = screen.getByLabelText('Planning sequence arming candidate')
      expect(candidate).toHaveTextContent(WORKSPACE_ID)
      expect(candidate).toHaveTextContent(CHECKPOINT_ID)
      expect(candidate).toHaveTextContent(FINGERPRINT)
      expect(screen.getByText(/up to five read-only steps/)).toBeInTheDocument()
      expect(screen.getByText(/Planner \(Codex\), Critical review \(Claude Code\), Resolver \(Codex\)/)).toBeInTheDocument()
      expect(screen.getByText(/second Critical review \(Claude Code\) and a second Resolver \(Codex\)/)).toBeInTheDocument()
      expect(screen.getByText(/The second review is included in this authorization\. Acceptance by a review ends planning/)).toBeInTheDocument()
      expect(screen.getByText(/Implementation is a separate explicit decision\. This control never starts it/)).toBeInTheDocument()
      expect(screen.getByText(/keeps advancing the sequence if this window is closed/)).toBeInTheDocument()
      expect(armButton()).toBeEnabled()
      expect(stopButton()).not.toBeInTheDocument()
    })

    it('sends exactly the shown tuple for the run when Arm is used, and reads the status afterwards', async () => {
      const send = installArm(() => Promise.resolve(armReceiptFor(RUN)))
      const refresh = vi.fn()
      render(panel(known(armableStatusFor(RUN), refresh)))

      fireEvent.click(armButton() as HTMLElement)
      await act(async () => undefined)

      expect(send).toHaveBeenCalledTimes(1)
      expect(send.mock.calls[0]).toEqual([RUN, expect.anything()])
      expect(JSON.parse(JSON.stringify((send.mock.calls[0] as unknown[])[1]))).toEqual(TUPLE)
      expect(refresh).toHaveBeenCalledTimes(1)
    })

    it('shows a pending arming that cannot be repeated', async () => {
      const pending = deferred<unknown>()
      const send = installArm(() => pending.promise)
      render(panel(known(armableStatusFor(RUN))))

      fireEvent.click(armButton() as HTMLElement)

      const arming = screen.getByRole('button', { name: 'Arming…' })
      expect(arming).toBeDisabled()
      fireEvent.click(arming)
      expect(send).toHaveBeenCalledTimes(1)
      await act(async () => {
        pending.resolve(armReceiptFor(RUN))
        await pending.promise
      })
      expect(screen.getByRole('button', { name: 'Arm planning sequence' })).toBeEnabled()
    })

    it('shows a fixed refusal copy for a refused arming and keeps the control for another explicit attempt', async () => {
      installArm(() =>
        Promise.reject(ApiProblemDetails.fromJS({ status: 409, errors: [{ code: 'planning_sequence.tuple_stale', detail: 'server text' }], traceId: 't' })),
      )
      render(panel(known(armableStatusFor(RUN))))

      fireEvent.click(armButton() as HTMLElement)
      await act(async () => undefined)

      expect(screen.getByRole('alert')).toHaveTextContent(/no longer the current source/)
      expect(screen.getByRole('alert')).not.toHaveTextContent('server text')
      expect(armButton()).toBeEnabled()
    })

    it('shows the fixed refusal and no arming control or tuple when the host refuses arming', () => {
      render(panel(known(refusedStatusFor(RUN, 'planning_sequence.project_busy', 'A project attempt is active.'))))

      expect(screen.getByText('Arming is not available: A project attempt is active.')).toBeInTheDocument()
      expect(armButton()).not.toBeInTheDocument()
      expect(screen.queryByLabelText('Planning sequence arming candidate')).not.toBeInTheDocument()
    })
  })

  describe('states', () => {
    const wording: [PlanningSequenceState, RegExp, 'enabled' | 'disabled' | 'absent'][] = [
      ['Armed', /Armed: nothing is reserved yet\. No step has been claimed and no provider was started\./, 'enabled'],
      ['PreparingStep', /Preparing the next step: it is reserved but not yet claimed\. Nothing has been dispatched for it\./, 'enabled'],
      ['AwaitingStepResult', /A step is claimed, awaiting its result\. A recorded claim or dispatch marker is not proof that a provider process is running\./, 'enabled'],
      ['Stopping', /Stop after current step was requested\. The already claimed step may still finish; no successor will be started\./, 'disabled'],
      ['ReadyForImplementation', /an accepted plan is recorded\. No implementation or current-source approval is granted/, 'absent'],
      ['AwaitingHumanDecision', /awaits a Human decision\. Nothing is approved for implementation\./, 'absent'],
      ['Stopped', /Stopped: no successor step will be started\./, 'absent'],
      ['NeedsAttention', /Needs attention: automatic advancement is closed and nothing is retried\./, 'absent'],
      ['Interrupted', /Interrupted by a host restart\. It never resumes automatically\./, 'absent'],
    ]

    it('covers every closed state', () => {
      expect(wording.map(([state]) => state).sort()).toEqual([...PLANNING_SEQUENCE_STATES].sort())
    })

    it.each(wording)('words %s truthfully and offers Stop only where it can still apply', (state, statement, stop) => {
      const raw = statusFor(RUN, state)
      render(panel(known(raw)))

      const region = stateRegion()
      expect(region).toHaveAttribute('data-state', state)
      expect(region).toHaveTextContent(statement)
      expect(region).not.toHaveTextContent(/executing/i)
      const reason = (raw.sequence as Raw).reasonText as string | null
      if (reason !== null) {
        expect(region).toHaveTextContent(reason)
      }
      expect(region).toHaveTextContent((raw.sequence as Raw).nextActionText as string)
      if (stop === 'absent') {
        expect(stopButton()).not.toBeInTheDocument()
      } else {
        expect(stopButton()).toBeInTheDocument()
        if (stop === 'enabled') {
          expect(stopButton()).toBeEnabled()
        } else {
          expect(stopButton()).toBeDisabled()
          expect(screen.getByText(/Stop was already requested; the claimed step may still finish\./)).toBeInTheDocument()
        }
      }
      expect(armButton()).not.toBeInTheDocument()
    })

    it.each(PLANNING_SEQUENCE_STATES)('offers no resume, rearm, retry, restart, pause or cancel control in %s', (state) => {
      render(panel(known(statusFor(RUN, state))))

      const names = screen.queryAllByRole('button').map((button) => button.textContent ?? '')
      expect(names.filter((name) => /resume|rearm|re-arm|retry|restart|pause|cancel|kill/i.test(name))).toEqual([])
      expect(names.every((name) => name === 'Stop after current step')).toBe(true)
    })

    it('says a claimed step with a recorded claim awaits its result, and a concluded last step is between steps', () => {
      const claimed = render(panel(known(statusFor(RUN, 'AwaitingStepResult'))))
      expect(stateRegion()).toHaveTextContent(/A step is claimed, awaiting its result/)
      claimed.unmount()

      render(panel(known(statusFor(RUN, 'AwaitingStepResult', {}, { steps: [stepFor(0, 'concluded')] }))))
      expect(stateRegion()).toHaveTextContent('Between steps: the previous step is concluded and the next one is not yet reserved.')
      expect(stateRegion()).not.toHaveTextContent(/awaiting its result/)
    })

    it('shows an attention state with its fixed reason text, its detail code and the attempt awaiting reconciliation', () => {
      const raw = statusFor(
        RUN,
        'NeedsAttention',
        {},
        {
          steps: [stepFor(0, 'claimed')],
          reasonCode: 'result_deadline_elapsed',
          reasonText: 'The result deadline elapsed. No process was cancelled; the step\'s attempt stays visible until it is reconciled.',
          detailCode: 'planning_sequence.step_expired',
        },
      )
      render(panel(known(raw)))

      const region = stateRegion()
      expect(region).toHaveTextContent('The result deadline elapsed. No process was cancelled')
      expect(region).toHaveTextContent('Detail code: planning_sequence.step_expired')
      expect(region).toHaveTextContent(`Attempt ${planningGuid(200)} is awaiting reconciliation`)
      expect(region).toHaveTextContent('No process was cancelled.')
      expect(stopButton()).not.toBeInTheDocument()
    })

    it('shows no reconciliation statement when no linked attempt is outstanding', () => {
      render(panel(known(statusFor(RUN, 'NeedsAttention'))))

      expect(stateRegion()).not.toHaveTextContent(/awaiting reconciliation/)
    })

    it('shows an elapsed deadline that the host has not persisted as exactly that, without Stop', () => {
      const raw = statusFor(
        RUN,
        'NeedsAttention',
        {},
        {
          recordedState: 'PreparingStep',
          observedBeforePersisted: true,
          reasonCode: 'preparation_expired',
          reasonText: 'The next step was not claimed within two minutes of its reservation; nothing was claimed.',
          steps: [stepFor(0, 'reserved')],
          endedAtUtc: null,
        },
      )
      render(panel(known(raw)))

      expect(stateRegion()).toHaveTextContent('Deadline elapsed, not yet persisted by the host.')
      expect(stopButton()).not.toBeInTheDocument()
    })

    it('shows the exact final plan by identity only for an accepted sequence, and the exact escalation for the depth-two end', () => {
      const accepted = render(panel(known(statusFor(RUN, 'ReadyForImplementation'))))
      const references = screen.getByLabelText('Planning sequence final references')
      expect(references).toHaveTextContent(planningGuid(300))
      expect(references).toHaveTextContent(planningGuid(301))
      expect(references).toHaveTextContent(planningGuid(201))
      expect(references).not.toHaveTextContent('Escalation message')
      expect(within(references).queryAllByRole('link')).toEqual([])
      accepted.unmount()

      render(panel(known(statusFor(RUN, 'AwaitingHumanDecision'))))
      const escalation = screen.getByLabelText('Planning sequence final references')
      expect(escalation).toHaveTextContent(planningGuid(310))
      expect(escalation).toHaveTextContent(planningGuid(311))
      expect(escalation).not.toHaveTextContent('Acceptance message')
    })

    it('says the final reference could not be proved when the host disclosed none, and shows none', () => {
      render(panel(known(statusFor(RUN, 'ReadyForImplementation', {}, { final: null }))))

      expect(screen.getByText(/could not prove the final references/)).toBeInTheDocument()
      expect(screen.queryByLabelText('Planning sequence final references')).not.toBeInTheDocument()
    })

    it('shows no final references for a state that has none', () => {
      render(panel(known(statusFor(RUN, 'Stopped'))))

      expect(screen.queryByLabelText('Planning sequence final references')).not.toBeInTheDocument()
      expect(screen.queryByText(/could not prove the final references/)).not.toBeInTheDocument()
    })

    it('shows the pinned tuple of an existing sequence as immutable, with no arming candidate and no Arm control', () => {
      render(panel(known(statusFor(RUN, 'Stopped'))))

      const pinned = screen.getByLabelText('Planning sequence pinned source')
      expect(pinned).toHaveTextContent(SEQUENCE_ID)
      expect(pinned).toHaveTextContent(WORKSPACE_ID)
      expect(pinned).toHaveTextContent(CHECKPOINT_ID)
      expect(pinned).toHaveTextContent(FINGERPRINT)
      expect(screen.getByText(/never replaced by a newer workspace or checkpoint/)).toBeInTheDocument()
      expect(screen.queryByLabelText('Planning sequence arming candidate')).not.toBeInTheDocument()
      expect(armButton()).not.toBeInTheDocument()
    })

    it('renders every host text as literal text, never as markup', () => {
      const hostile = '<img src="x" onerror="alert(1)"> <b>bold</b>'
      render(panel(known(statusFor(RUN, 'Stopped', {}, { reasonText: hostile, nextActionText: hostile }))))

      expect(screen.getAllByText(hostile, { exact: false }).length).toBeGreaterThan(0)
      expect(document.querySelector('img')).toBeNull()
      expect(document.querySelector('b')).toBeNull()
    })

    it('says an ended sequence is never resumed, rearmed or retried from this window', () => {
      render(panel(known(statusFor(RUN, 'Interrupted'))))

      expect(screen.getByText(/never resumed, rearmed or retried by this window/)).toBeInTheDocument()
    })
  })

  describe('step table', () => {
    it('lists every step by ordinal with its role, provider, state, linked attempt and deadlines as the host wrote them', () => {
      const raw = statusFor(RUN, 'AwaitingHumanDecision')
      render(panel(known(raw)))

      const table = screen.getByRole('table', { name: 'Planning sequence steps' })
      const rows = within(table).getAllByRole('row')
      expect(rows).toHaveLength(6)
      const first = within(rows[1]).getAllByRole('cell').map((cell) => cell.textContent)
      expect(first).toEqual([
        '0',
        'Planner',
        'Codex',
        'Concluded',
        '#1 · recorded status Completed · Proposed · dispatch marker recorded',
        '2026-10-09T10:02:00.1234567Z',
        '2026-10-09T10:40:00.1234567Z',
      ])
      const second = within(rows[2]).getAllByRole('cell').map((cell) => cell.textContent)
      expect(second.slice(0, 4)).toEqual(['1', 'Critical review', 'Claude Code', 'Concluded'])
      expect(within(rows[5]).getAllByRole('cell')[1]).toHaveTextContent('Resolver')
    })

    it('words a claimed step and its dispatch marker as bookkeeping, never as a running process', () => {
      render(panel(known(statusFor(RUN, 'AwaitingStepResult'))))

      const row = within(screen.getByRole('table', { name: 'Planning sequence steps' })).getAllByRole('row')[1]
      expect(row).toHaveTextContent('Claimed')
      expect(row).toHaveTextContent('#1 · recorded status Running · dispatch marker not recorded')
      expect(row.textContent).not.toMatch(/executing/i)
    })

    it('shows a reserved, a revoked and an unresolved step as not claimed, without an attempt', () => {
      const reserved = render(panel(known(statusFor(RUN, 'PreparingStep'))))
      expect(screen.getByRole('table', { name: 'Planning sequence steps' })).toHaveTextContent('Reserved, not yet claimed')
      reserved.unmount()
      const revoked = render(panel(known(statusFor(RUN, 'Stopped'))))
      expect(screen.getByRole('table', { name: 'Planning sequence steps' })).toHaveTextContent('Revoked before any claim')
      revoked.unmount()
      render(panel(known(statusFor(RUN, 'NeedsAttention'))))
      const row = within(screen.getByRole('table', { name: 'Planning sequence steps' })).getAllByRole('row')[1]
      expect(row).toHaveTextContent('Unresolved: not proved claimed or absent')
      expect(within(row).getAllByRole('cell')[4]).toHaveTextContent('—')
    })

    it('discloses the dispatch-refused outcome exactly, and shows any other outcome as the plain recorded name', () => {
      const refused = statusFor(
        RUN,
        'NeedsAttention',
        {},
        {
          steps: [stepFor(0, 'claimed', { attemptStatus: 'Failed', attemptOutcome: 'PlanningSequenceDispatchRefused', dispatched: false })],
          reasonCode: 'authority_changed',
          reasonText: 'The pinned source, workspace or objective changed; the sequence did not continue.',
        },
      )
      render(panel(known(refused)))

      expect(screen.getByRole('table', { name: 'Planning sequence steps' })).toHaveTextContent(
        'Not started: the planning sequence no longer authorized dispatching it (no provider was invoked, no output recorded, no budget refunded)',
      )
    })

    it('says no step has been reserved for an Armed sequence', () => {
      render(panel(known(statusFor(RUN, 'Armed'))))

      expect(screen.queryByRole('table', { name: 'Planning sequence steps' })).not.toBeInTheDocument()
      expect(screen.getByText('No step has been reserved.')).toBeInTheDocument()
    })
  })

  describe('Stop after current step', () => {
    it('stops exactly the run and sequence shown with one request, then reads the status, and says what the host recorded', async () => {
      const send = installStop(() => Promise.resolve(stopAnswerFor(RUN)))
      const refresh = vi.fn()
      render(panel(known(statusFor(RUN, 'AwaitingStepResult'), refresh)))

      fireEvent.click(stopButton() as HTMLElement)
      await act(async () => undefined)

      expect(send).toHaveBeenCalledExactlyOnceWith(RUN, SEQUENCE_ID)
      expect(refresh).toHaveBeenCalledTimes(1)
      expect(screen.getByText('Stop after current step was recorded.')).toBeInTheDocument()
    })

    it('says no change when the host answers unchanged, naming the state the sequence already had', async () => {
      installStop(() => Promise.resolve(stopAnswerFor(RUN, { state: 'ReadyForImplementation', reasonCode: 'plan_accepted', changed: false })))
      render(panel(known(statusFor(RUN, 'AwaitingStepResult'))))

      fireEvent.click(stopButton() as HTMLElement)
      await act(async () => undefined)

      expect(screen.getByText('No change: the sequence was already planning complete.')).toBeInTheDocument()
    })

    it('disables the control while a stop is pending and sends one request', async () => {
      const pending = deferred<unknown>()
      const send = installStop(() => pending.promise)
      render(panel(known(statusFor(RUN, 'Armed'))))

      fireEvent.click(stopButton() as HTMLElement)

      expect(stopButton()).toBeDisabled()
      fireEvent.click(stopButton() as HTMLElement)
      expect(send).toHaveBeenCalledTimes(1)
      await act(async () => {
        pending.resolve(stopAnswerFor(RUN))
        await pending.promise
      })
      expect(stopButton()).toBeEnabled()
    })

    it('shows fixed copy for a refused stop and states that Stop is not process cancellation', async () => {
      installStop(() => Promise.reject(ApiProblemDetails.fromJS({ status: 404, errors: [{ code: 'planning_sequence.not_found', detail: 'server text' }], traceId: 't' })))
      render(panel(known(statusFor(RUN, 'Armed'))))

      expect(screen.getByText(/not process cancellation, run Stop or Pause/)).toBeInTheDocument()
      fireEvent.click(stopButton() as HTMLElement)
      await act(async () => undefined)

      expect(screen.getByRole('alert')).toHaveTextContent('This planning sequence was not found for this run.')
      expect(screen.getByRole('alert')).not.toHaveTextContent('server text')
    })
  })

  describe('arming receipt', () => {
    it('shows a replayed receipt as already armed, never as a second arming, once the sequence is read', async () => {
      installArm(() => Promise.resolve(armReceiptFor(RUN, { replayed: true, state: 'AwaitingStepResult', revision: '5' })))
      const view = render(panel(known(armableStatusFor(RUN))))

      fireEvent.click(armButton() as HTMLElement)
      await act(async () => undefined)
      view.rerender(panel(known(statusFor(RUN, 'AwaitingStepResult'))))

      expect(screen.getByText('Already armed: the recorded receipt for this tuple was returned. Nothing was armed again.')).toBeInTheDocument()
      expect(screen.queryByText(/Armed by this window/)).not.toBeInTheDocument()
      expect(armButton()).not.toBeInTheDocument()
    })

    it('shows a first receipt as armed by this window', async () => {
      installArm(() => Promise.resolve(armReceiptFor(RUN)))
      const view = render(panel(known(armableStatusFor(RUN))))

      fireEvent.click(armButton() as HTMLElement)
      await act(async () => undefined)
      view.rerender(panel(known(statusFor(RUN, 'Armed'))))

      expect(screen.getByText(/Armed by this window: the sequence was recorded/)).toBeInTheDocument()
    })

    it('shows no receipt note for a sequence the receipt does not name', async () => {
      installArm(() => Promise.resolve(armReceiptFor(RUN, { sequenceId: planningGuid(77) })))
      const view = render(panel(known(armableStatusFor(RUN))))

      fireEvent.click(armButton() as HTMLElement)
      await act(async () => undefined)
      view.rerender(panel(known(statusFor(RUN, 'Armed'))))

      expect(screen.queryByText(/Already armed|Armed by this window/)).not.toBeInTheDocument()
    })
  })
})
