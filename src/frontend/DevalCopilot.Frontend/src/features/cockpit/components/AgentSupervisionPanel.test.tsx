import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  attemptWire,
  attentionWire,
  endedWire,
  needsAttentionWire,
  noAgentAttemptWire,
  notObservedWire,
  observationWire,
  observedWire,
  readingOf,
  resultFor,
  unavailableWire,
} from '../agentSupervisionFixture'
import { AgentSupervisionPanel } from './AgentSupervisionPanel'

const RUN = 'run-1'

function renderPanel(body: Record<string, unknown> | null, options: { stale?: boolean; age?: number | null; refresh?: () => void; failure?: 'read' | 'timeout' | 'not-found' | 'invalid'; loading?: boolean } = {}) {
  const supervision = resultFor(body === null ? null : readingOf(body, RUN), {
    failure: options.failure ?? null,
    loading: options.loading ?? false,
    refresh: options.refresh ?? (() => undefined),
  })
  return render(<AgentSupervisionPanel supervision={supervision} readingAgeSeconds={options.age ?? 0} stale={options.stale ?? false} />)
}

const panel = () => screen.getByRole('region', { name: 'Host observation of the current Agent attempt' })

describe('AgentSupervisionPanel', () => {
  it('shows only a loading line while nothing is read yet, and no reading, alert or claim', () => {
    renderPanel(null, { loading: true })

    expect(screen.getByText(/Reading the host.s observation/)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(panel()).toHaveAttribute('data-status', 'loading')
  })

  it.each([
    ['read', 'could not be read'],
    ['timeout', 'did not answer within ten seconds'],
    ['not-found', 'no longer knows this run'],
    ['invalid', 'was not coherent'],
  ] as const)('shows %s as unavailable and says that nothing is current', (failure, text) => {
    renderPanel(null, { failure })

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(text)
    expect(alert).toHaveTextContent('Nothing is shown as current.')
    expect(panel()).toHaveAttribute('data-status', 'unavailable')
    expect(screen.queryByTestId('agent-supervision-status')).not.toBeInTheDocument()
  })

  it('says only that no Agent attempt is recorded for a run without one', () => {
    renderPanel(noAgentAttemptWire(RUN))

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('No Agent attempt recorded')
    expect(screen.getByText('No Agent attempt is recorded for this run.')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Host observation' })).not.toBeInTheDocument()
    expect(screen.queryByText(/retry, resume, cancel or takeover/)).not.toBeInTheDocument()
  })

  it('states for a missing receipt that nothing is observed and that this is not a heartbeat', () => {
    renderPanel(notObservedWire(RUN))

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Not observed')
    expect(screen.getByText(/never observed or it predates observations\. This is not a heartbeat\./)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Host observation' })).not.toBeInTheDocument()
    const attempt = screen.getByRole('list', { name: 'Selected Agent attempt' })
    expect(attempt).toHaveTextContent('Attempt #1 · Planner · Codex · Proposal')
    expect(attempt).toHaveTextContent('Recorded status Running · dispatch marker not recorded')
  })

  it.each([
    ['Preparing', 'checking Git and account evidence before any provider call'],
    ['AwaitingAdapterResult', 'this does not show a process started'],
    ['RecordingResult', 'the host is recording it'],
  ] as const)('words the %s phase as what the host observer reported, never as a process or progress', (phase, text) => {
    renderPanel(observedWire(RUN, { observation: observationWire({ phase }) }))

    const observation = screen.getByRole('list', { name: 'Host observation' })
    expect(observation).toHaveTextContent(text)
    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Observed by the host')
  })

  it('shows the last host report with its age on the host clock and the absolute deadline with the time left', () => {
    renderPanel(observedWire(RUN))

    const observation = screen.getByRole('list', { name: 'Host observation' })
    expect(observation).toHaveTextContent('Last host report 2026-10-19 09:00:50 UTC, 10 s before this reading (host clock 2026-10-19 09:01:00 UTC).')
    expect(observation).toHaveTextContent('Absolute deadline of this phase 2026-10-19 09:02:00 UTC (in 60 s at the host clock).')
    expect(observation).toHaveTextContent('Observation revision 3 · written by this host launch.')
    expect(screen.getByText(/It is not provider progress, a live process or permission to act\./)).toBeInTheDocument()
    expect(screen.getByText('Wait; the host observer is reporting.')).toBeInTheDocument()
  })

  it('shows an elapsed deadline as elapsed and an observation of another host launch as such', () => {
    renderPanel(
      needsAttentionWire(RUN, {
        observation: observationWire({
          ownedByCurrentHost: false,
          lastHeartbeatAtUtc: '2026-10-19T08:50:00.0000000Z',
          startedAtUtc: '2026-10-19T08:49:00.0000000Z',
          phaseEnteredAtUtc: '2026-10-19T08:49:00.0000000Z',
          phaseDeadlineUtc: '2026-10-19T08:51:00.0000000Z',
        }),
      }),
    )

    const observation = screen.getByRole('list', { name: 'Host observation' })
    expect(observation).toHaveTextContent('has elapsed.')
    expect(observation).toHaveTextContent('written by a previous host launch')
  })

  it('shows attention with its fixed text, as derived and not yet recorded when that is the case', () => {
    renderPanel(needsAttentionWire(RUN))

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Needs attention')
    const attention = screen.getByRole('status', { name: 'Attention' })
    expect(attention).toHaveTextContent('has not reported for at least thirty seconds. This does not show whether earlier work stopped.')
    expect(attention).toHaveTextContent('derived by this reading and not yet recorded')
    expect(screen.getByText('Inspect the recorded attempt and reconcile it before any further work.')).toBeInTheDocument()
    expect(screen.getByText(/Earlier authorized work may still finish later\./)).toBeInTheDocument()
  })

  it('shows a recorded first attention as recorded', () => {
    renderPanel(
      needsAttentionWire(RUN, {
        attention: attentionWire({ recordedReasonCode: 'stale_heartbeat', recordedAtUtc: '2026-10-19T09:00:30.0000000Z' }),
      }),
    )

    expect(screen.getByRole('status', { name: 'Attention' })).toHaveTextContent('First attention recorded at 2026-10-19 09:00:30 UTC.')
  })

  it('shows a closed observation with its closure and never as live', () => {
    renderPanel(
      needsAttentionWire(RUN, {
        reasonCode: 'observer_ended_without_terminal_result',
        reasonText: 'The host observer ended without a recorded terminal result. The attempt stays as recorded until it is reconciled.',
        observation: observationWire({ open: false, closureReason: 'observer_ended', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }),
        attention: attentionWire({
          effectiveReasonCode: 'observer_ended_without_terminal_result',
          effectiveReasonText: 'The host observer ended without a recorded terminal result. The attempt stays as recorded until it is reconciled.',
          effectiveAtUtc: '2026-10-19T09:00:55.0000000Z',
          recordedReasonCode: 'observer_ended_without_terminal_result',
          recordedAtUtc: '2026-10-19T09:00:55.0000000Z',
        }),
      }),
    )

    const observation = screen.getByRole('list', { name: 'Host observation' })
    expect(observation).toHaveTextContent('closed because the observer ended without a recorded terminal result at 2026-10-19 09:00:55 UTC.')
    expect(observation).not.toHaveTextContent('Absolute deadline')
  })

  it('lets the real terminal attempt govern: it leads, and a recorded attention is history, never the headline', () => {
    renderPanel(
      endedWire(RUN, {
        // Read after the recorded attention: a successful answer never carries a fact later than its own host clock.
        observedAtUtc: '2026-10-19T09:03:00.0000000Z',
        attention: attentionWire({
          effectiveReasonCode: 'phase_deadline_elapsed',
          effectiveReasonText: 'The observed phase passed its deadline. No process was cancelled and earlier authorized work may still finish later.',
          effectiveAtUtc: '2026-10-19T09:02:30.0000000Z',
          recordedReasonCode: 'phase_deadline_elapsed',
          recordedAtUtc: '2026-10-19T09:02:30.0000000Z',
        }),
      }),
    )

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Attempt ended')
    const attempt = screen.getByRole('list', { name: 'Selected Agent attempt' })
    expect(attempt.querySelector('li')).toHaveTextContent('The attempt ended as recorded: Completed · Proposed.')
    const history = screen.getByRole('status', { name: 'Attention history' })
    expect(history).toHaveTextContent('Attention history, kept as recorded:')
    expect(history).toHaveTextContent('passed its deadline')
    expect(screen.queryByText(/retry, resume, cancel or takeover/)).not.toBeInTheDocument()
    expect(screen.getByText('Review the recorded result of the attempt.')).toBeInTheDocument()
  })

  it('shows a timely ended attempt without any attention', () => {
    renderPanel(endedWire(RUN))

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Attempt ended')
  })

  it.each([
    ['multiple_active_agent_attempts', 'More than one Agent attempt is recorded as running; no single one can be shown.'],
    ['chronology_invalid', 'The recorded observation times are not coherent with the host clock, so nothing is shown as fresh.'],
  ])('shows an unavailable read with its fixed reason %s and claims no heartbeat', (reason, text) => {
    renderPanel({ ...unavailableWire(RUN, reason), reasonText: text })

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Observation unavailable')
    expect(screen.getByText(text)).toBeInTheDocument()
    expect(panel()).toHaveAttribute('data-status', 'Unavailable')
  })

  it('shows a reading that is no longer refreshed as stale, with its age, instead of as current', () => {
    renderPanel(observedWire(RUN), { stale: true, age: 23 })

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Reading is stale')
    expect(screen.getByRole('status')).toHaveTextContent('Last answer 23 s ago.')
    expect(screen.getByRole('status')).toHaveTextContent('the browser never renews a heartbeat')
    expect(panel()).toHaveAttribute('data-stale', 'true')
  })

  it('keeps a terminal answer as it is even when it is old: nothing about it is refreshed', () => {
    renderPanel(endedWire(RUN), { stale: true, age: 300 })

    expect(screen.getByTestId('agent-supervision-status')).toHaveTextContent('Attempt ended')
    expect(screen.queryByText(/Last answer/)).not.toBeInTheDocument()
  })

  it('never calls the observation work, progress or a running Agent, and offers only a read, never a retry, resume, cancel or takeover', () => {
    renderPanel(observedWire(RUN, { attempt: attemptWire({ dispatched: true, dispatchedAtUtc: '2026-10-19T09:00:05.0000000Z' }) }))

    const text = panel().textContent ?? ''
    expect(text).not.toMatch(/\bworking\b/i)
    expect(text).not.toMatch(/making progress|in progress|is running|autonomous/i)
    const buttons = within(panel()).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Read the host observation again'])
    expect(screen.getByRole('list', { name: 'Selected Agent attempt' })).toHaveTextContent('dispatch marker recorded')
  })

  it('reads again only when asked, through the supervision refresh', () => {
    const refresh = vi.fn()
    renderPanel(observedWire(RUN), { refresh })

    fireEvent.click(screen.getByRole('button', { name: 'Read the host observation again' }))

    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('renders every host text as literal text, never as markup', () => {
    renderPanel(
      needsAttentionWire(RUN, {
        reasonText: '<img src=x onerror=alert(1)>',
        attention: attentionWire({ effectiveReasonText: '<b>bold</b> <img src=x onerror=alert(1)>' }),
      }),
    )

    expect(screen.getByRole('status', { name: 'Attention' })).toHaveTextContent('<b>bold</b> <img src=x onerror=alert(1)>')
    expect(panel().querySelector('img, b')).toBeNull()
  })
})
