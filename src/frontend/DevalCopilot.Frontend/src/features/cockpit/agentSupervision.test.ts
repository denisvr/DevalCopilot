import { describe, expect, it } from 'vitest'
import {
  AGENT_SUPERVISION_STALE_AFTER_TICKS,
  isAgentSupervisionWatching,
  reportAgeSeconds,
  secondsToDeadline,
  toAgentSupervisionReading,
  utcTicks,
} from './agentSupervision'
import {
  OBSERVED_AT,
  attemptWire,
  attentionWire,
  endedWire,
  needsAttentionWire,
  noAgentAttemptWire,
  notObservedWire,
  observationWire,
  observedWire,
  readingOf,
  unavailableWire,
} from './agentSupervisionFixture'
import type { WireBody } from './agentSupervisionFixture'

const RUN = 'run-1'
const decode = (body: unknown) => toAgentSupervisionReading(body, RUN)

describe('toAgentSupervisionReading: the closed, whole-answer decoder', () => {
  it.each([
    ['NoAgentAttempt', noAgentAttemptWire(RUN)],
    ['NotObserved (running)', notObservedWire(RUN)],
    ['NotObserved (terminal)', notObservedWire(RUN, true)],
    ['Observed', observedWire(RUN)],
    ['NeedsAttention', needsAttentionWire(RUN)],
    ['Ended', endedWire(RUN)],
    ['Unavailable', unavailableWire(RUN)],
  ])('accepts a coherent %s answer exactly as the host wrote it', (_, body) => {
    const reading = decode(body)

    expect(reading).not.toBeNull()
    expect(reading?.runId).toBe(RUN)
    expect(reading?.status).toBe((body as WireBody).status)
  })

  it('keeps every member of an observed answer exactly, including the Int64 revision as text', () => {
    const reading = readingOf(observedWire(RUN, { observation: observationWire({ revision: '9223372036854775807' }) }), RUN)

    expect(reading.attempt).toMatchObject({ attemptNumber: 1, role: 'Planner', provider: 'Codex', responseContract: 'Proposal', status: 'Running' })
    expect(reading.observation).toMatchObject({ phase: 'Preparing', open: true, ownedByCurrentHost: true })
    expect(reading.observation?.revision).toBe('9223372036854775807')
    expect(typeof reading.observation?.revision).toBe('string')
    expect(reading.observedAtUtc).toBe(OBSERVED_AT)
  })

  it.each([
    ['nothing', undefined],
    ['null', null],
    ['an array', []],
    ['a string', 'Observed'],
    ['an empty object', {}],
  ])('refuses %s', (_, body) => {
    expect(decode(body)).toBeNull()
  })

  it('refuses the answer of another run', () => {
    expect(toAgentSupervisionReading(observedWire('run-2'), RUN)).toBeNull()
  })

  it.each([
    ['top level', (body: WireBody) => ({ ...body, extra: 1 })],
    ['attempt', (body: WireBody) => ({ ...body, attempt: { ...(body.attempt as WireBody), extra: 1 } })],
    ['observation', (body: WireBody) => ({ ...body, observation: { ...(body.observation as WireBody), extra: 1 } })],
  ])('refuses an unknown member at the %s', (_, mutate) => {
    expect(decode(mutate(observedWire(RUN)))).toBeNull()
  })

  it('refuses an unknown member of the attention, and a missing member anywhere', () => {
    const body = needsAttentionWire(RUN)
    expect(decode({ ...body, attention: { ...(body.attention as WireBody), extra: 1 } })).toBeNull()
    const { reasonText: _removed, ...withoutReasonText } = body
    expect(decode(withoutReasonText)).toBeNull()
    const { revision: _revision, ...withoutRevision } = observationWire()
    expect(decode(observedWire(RUN, { observation: withoutRevision }))).toBeNull()
  })

  it.each([
    ['an unknown status', { status: 'Working' }],
    ['a lowercase status', { status: 'observed' }],
    ['an unknown next action', { nextActionCode: 'retry' }],
    ['a blank next action text', { nextActionText: '  ' }],
    ['a control character in the next action text', { nextActionText: 'Wait\u0000now' }],
    ['a reason code without text', { reasonCode: 'stale_heartbeat' }],
    ['a reason text without a code', { reasonText: 'Something' }],
    ['a time that is not canonical UTC', { observedAtUtc: '2026-10-19T09:01:00+00:00' }],
    ['a time with six fractional digits', { observedAtUtc: '2026-10-19T09:01:00.000000Z' }],
    ['a lowercase time separator', { observedAtUtc: '2026-10-19t09:01:00.0000000Z' }],
    ['a non-existent calendar day', { observedAtUtc: '2026-02-30T09:01:00.0000000Z' }],
  ])('refuses %s', (_, overrides) => {
    expect(decode(observedWire(RUN, overrides))).toBeNull()
  })

  describe('the attempt', () => {
    const attemptWith = (overrides: WireBody) => observedWire(RUN, { attempt: attemptWire(overrides) })

    it.each([
      ['a role that does not belong to the contract', { role: 'CriticalReviewer' }],
      ['an unknown contract', { responseContract: 'Everything' }],
      ['an unknown provider', { provider: 'Gemini' }],
      ['an unknown status', { status: 'Working' }],
      ['an attempt number of zero', { attemptNumber: 0 }],
      ['a fractional attempt number', { attemptNumber: 1.5 }],
      ['an attempt number sent as text', { attemptNumber: '1' }],
      ['an identity in uppercase', { attemptId: '11111111-1111-4111-8111-11111111111A' }],
      ['the empty identity', { attemptId: '00000000-0000-0000-0000-000000000000' }],
      ['a running attempt with an outcome', { outcome: 'Proposed' }],
      ['a running attempt with a completion', { completedAtUtc: '2026-10-19T09:00:30.0000000Z' }],
      ['a dispatch marker without its time', { dispatched: true }],
      ['a dispatch time without its marker', { dispatchedAtUtc: '2026-10-19T09:00:05.0000000Z' }],
      ['a dispatch before the claim', { dispatched: true, dispatchedAtUtc: '2026-10-19T08:59:59.0000000Z' }],
      ['an outcome name with a space', { status: 'Failed', outcome: 'Not a name', completedAtUtc: '2026-10-19T09:00:30.0000000Z' }],
      ['a terminal attempt without its completion', { status: 'Failed' }],
    ])('refuses %s', (_, overrides) => {
      expect(decode(attemptWith(overrides))).toBeNull()
    })

    it.each([
      ['Proposal', 'Planner'],
      ['CriticalReview', 'CriticalReviewer'],
      ['ChallengeResolution', 'Resolver'],
      ['ImplementationReport', 'Implementer'],
      ['ImplementationReview', 'CodeReviewer'],
      ['ReviewCorrection', 'Implementer'],
      ['VerificationDiagnosis', 'CodeReviewer'],
    ])('accepts the %s contract only with the %s role', (contract, role) => {
      expect(decode(attemptWith({ responseContract: contract, role }))).not.toBeNull()
    })
  })

  describe('the observation', () => {
    const observationWith = (overrides: WireBody) => observedWire(RUN, { observation: observationWire(overrides) })

    it.each([
      ['a revision of zero', { revision: '0' }],
      ['a revision with a leading zero', { revision: '01' }],
      ['a negative revision', { revision: '-1' }],
      ['a revision sent as a number', { revision: 3 }],
      ['a revision beyond the signed 64-bit maximum', { revision: '9223372036854775808' }],
      ['an unknown phase', { phase: 'Working' }],
      ['an observation both open and closed', { closureReason: 'attempt_terminal', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }],
      ['a closed observation still marked open', { open: true, closureReason: 'attempt_terminal', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }],
      ['a closed observation without its reason', { open: false, closedAtUtc: '2026-10-19T09:00:55.0000000Z' }],
      ['a closure reason without its time', { open: false, closureReason: 'attempt_terminal' }],
      ['an unknown closure reason', { open: false, closureReason: 'finished', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }],
      ['a phase entered before the start', { phaseEnteredAtUtc: '2026-10-19T08:59:59.0000000Z' }],
      ['a report before the phase entry', { phaseEnteredAtUtc: '2026-10-19T09:00:55.0000000Z' }],
      ['a deadline at the phase entry', { phaseDeadlineUtc: '2026-10-19T09:00:00.0000000Z' }],
      ['a deadline before the phase entry', { phaseDeadlineUtc: '2026-10-19T08:59:00.0000000Z' }],
      ['a closure before the last report', { open: false, closureReason: 'observer_ended', closedAtUtc: '2026-10-19T09:00:49.0000000Z' }],
      ['an ownership flag that is not a boolean', { ownedByCurrentHost: 'yes' }],
      ['the empty host identity', { hostInstanceId: '00000000-0000-0000-0000-000000000000' }],
    ])('refuses %s', (_, overrides) => {
      expect(decode(observationWith(overrides))).toBeNull()
    })
  })

  describe('the attention', () => {
    const attentionWith = (overrides: WireBody) => needsAttentionWire(RUN, { attention: attentionWire(overrides) })

    it('accepts a derived attention (not yet recorded) and a recorded one that is exactly the effective one', () => {
      expect(decode(attentionWith({}))).not.toBeNull()
      expect(decode(attentionWith({ recordedReasonCode: 'stale_heartbeat', recordedAtUtc: '2026-10-19T09:00:30.0000000Z' }))).not.toBeNull()
    })

    it.each([
      ['an unknown reason', { effectiveReasonCode: 'working' }],
      ['a blank reason text', { effectiveReasonText: '' }],
      ['a recorded reason that is not the effective one', { recordedReasonCode: 'host_interrupted', recordedAtUtc: '2026-10-19T09:00:30.0000000Z' }],
      ['a recorded time that is not the effective one', { recordedReasonCode: 'stale_heartbeat', recordedAtUtc: '2026-10-19T09:00:31.0000000Z' }],
      ['a recorded reason without a time', { recordedReasonCode: 'stale_heartbeat' }],
      ['a recorded time without a reason', { recordedAtUtc: '2026-10-19T09:00:30.0000000Z' }],
    ])('refuses %s', (_, overrides) => {
      expect(decode(attentionWith(overrides))).toBeNull()
    })
  })

  describe('the closed nullability relationships of each status', () => {
    it.each([
      ['NoAgentAttempt with an attempt', { ...noAgentAttemptWire(RUN), attempt: attemptWire() }],
      ['NoAgentAttempt with an observation', { ...noAgentAttemptWire(RUN), observation: observationWire() }],
      ['NoAgentAttempt with a reason', { ...noAgentAttemptWire(RUN), reasonCode: 'chronology_invalid', reasonText: 'x' }],
      ['NoAgentAttempt with another next action', { ...noAgentAttemptWire(RUN), nextActionCode: 'wait_and_refresh' }],
      ['NotObserved without an attempt', { ...notObservedWire(RUN), attempt: null }],
      ['NotObserved with an observation', { ...notObservedWire(RUN), observation: observationWire() }],
      ['NotObserved with an attention', { ...notObservedWire(RUN), attention: attentionWire() }],
      ['NotObserved of a running attempt with the terminal next action', { ...notObservedWire(RUN), nextActionCode: 'review_the_recorded_result' }],
      ['NotObserved of a terminal attempt with the running next action', { ...notObservedWire(RUN, true), nextActionCode: 'inspect_the_recorded_attempt' }],
      ['Observed without an observation', observedWire(RUN, { observation: null })],
      ['Observed with a closed observation', observedWire(RUN, { observation: observationWire({ open: false, closureReason: 'observer_ended', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }) })],
      ['Observed of another host launch', observedWire(RUN, { observation: observationWire({ ownedByCurrentHost: false }) })],
      ['Observed with an attention', observedWire(RUN, { attention: attentionWire() })],
      ['Observed of a terminal attempt', observedWire(RUN, { attempt: attemptWire({ status: 'Failed', completedAtUtc: '2026-10-19T09:00:30.0000000Z' }) })],
      ['Observed with a reason', observedWire(RUN, { reasonCode: 'stale_heartbeat', reasonText: 'x' })],
      ['NeedsAttention without an attention', needsAttentionWire(RUN, { attention: null })],
      ['NeedsAttention without a reason', needsAttentionWire(RUN, { reasonCode: null, reasonText: null })],
      ['NeedsAttention whose reason is not the attention', needsAttentionWire(RUN, { reasonCode: 'host_interrupted' })],
      ['NeedsAttention with an unavailable reason', needsAttentionWire(RUN, { reasonCode: 'chronology_invalid' })],
      ['NeedsAttention of a terminal attempt', needsAttentionWire(RUN, { attempt: attemptWire({ status: 'Failed', completedAtUtc: '2026-10-19T09:00:30.0000000Z' }) })],
      ['NeedsAttention with a closed observation whose attention is only derived', needsAttentionWire(RUN, { observation: observationWire({ open: false, closureReason: 'observer_ended', closedAtUtc: '2026-10-19T09:00:55.0000000Z' }) })],
      ['NeedsAttention with the wait next action', needsAttentionWire(RUN, { nextActionCode: 'wait_and_refresh' })],
      ['Ended with a running attempt', endedWire(RUN, { attempt: attemptWire() })],
      ['Ended without an observation', endedWire(RUN, { observation: null })],
      ['Ended with a reason', endedWire(RUN, { reasonCode: 'stale_heartbeat', reasonText: 'x' })],
      ['Ended with the wait next action', endedWire(RUN, { nextActionCode: 'wait_and_refresh' })],
      ['Unavailable with an attention', { ...unavailableWire(RUN), attention: attentionWire() }],
      ['Unavailable without a reason', { ...unavailableWire(RUN), reasonCode: null, reasonText: null }],
      ['Unavailable with an attention reason', unavailableWire(RUN, 'stale_heartbeat')],
      ['Unavailable with an observation but no attempt', { ...unavailableWire(RUN), attempt: null, observation: observationWire() }],
      ['Unavailable with the wait next action', { ...unavailableWire(RUN), nextActionCode: 'wait_and_refresh' }],
    ])('refuses %s', (_, body) => {
      expect(decode(body)).toBeNull()
    })

    it.each([
      'multiple_active_agent_attempts',
      'attempt_facts_incoherent',
      'supervision_facts_incoherent',
      'chronology_invalid',
    ])('accepts Unavailable with the fixed reason %s', (reason) => {
      expect(decode(unavailableWire(RUN, reason))).not.toBeNull()
    })

    it('accepts Unavailable carrying no attempt or observation at all', () => {
      expect(decode({ ...unavailableWire(RUN, 'multiple_active_agent_attempts'), attempt: null, observation: null })).not.toBeNull()
    })

    it('accepts a NeedsAttention of a closed observation only when its attention is recorded', () => {
      const closed = observationWire({ open: false, closureReason: 'observer_ended', closedAtUtc: '2026-10-19T09:00:55.0000000Z' })
      const recorded = needsAttentionWire(RUN, {
        reasonCode: 'observer_ended_without_terminal_result',
        observation: closed,
        attention: attentionWire({
          effectiveReasonCode: 'observer_ended_without_terminal_result',
          effectiveAtUtc: '2026-10-19T09:00:55.0000000Z',
          recordedReasonCode: 'observer_ended_without_terminal_result',
          recordedAtUtc: '2026-10-19T09:00:55.0000000Z',
        }),
      })
      expect(decode(recorded)).not.toBeNull()
    })

    it('accepts an Ended answer with its attention kept as history', () => {
      // Read after the recorded attention: a successful answer never carries a fact later than its own host clock.
      const withHistory = endedWire(RUN, {
        observedAtUtc: '2026-10-19T09:03:00.0000000Z',
        attention: attentionWire({
          effectiveReasonCode: 'phase_deadline_elapsed',
          effectiveAtUtc: '2026-10-19T09:02:30.0000000Z',
          recordedReasonCode: 'phase_deadline_elapsed',
          recordedAtUtc: '2026-10-19T09:02:30.0000000Z',
        }),
      })
      expect(decode(withHistory)?.attention?.recordedReasonCode).toBe('phase_deadline_elapsed')
    })
  })

  describe('an Observed answer must be fresh at its own host clock: the host’s judgment is re-checked, never trusted blindly', () => {
    const at = (heartbeat: string, deadline = '2026-10-19T09:30:00.0000000Z') =>
      observedWire(RUN, {
        observedAtUtc: '2026-10-19T09:01:00.0000000Z',
        observation: observationWire({ lastHeartbeatAtUtc: heartbeat, phaseDeadlineUtc: deadline }),
      })

    it('accepts a heartbeat one tick short of thirty seconds and refuses exactly thirty seconds (the boundary is expired)', () => {
      expect(decode(at('2026-10-19T09:00:30.0000001Z'))).not.toBeNull()
      expect(decode(at('2026-10-19T09:00:30.0000000Z'))).toBeNull()
    })

    it('refuses a heartbeat after the host clock of the read', () => {
      expect(decode(at('2026-10-19T09:01:00.0000001Z'))).toBeNull()
    })

    it('accepts the heartbeat at the host clock and refuses a phase whose deadline is the host clock (the boundary is expired)', () => {
      expect(decode(at('2026-10-19T09:01:00.0000000Z', '2026-10-19T09:01:00.0000001Z'))).not.toBeNull()
      expect(decode(at('2026-10-19T09:01:00.0000000Z', '2026-10-19T09:01:00.0000000Z'))).toBeNull()
    })
  })

  describe('a successful answer never records a fact later than its own host clock (ASUP-R3, mirroring the host’s chronology rule)', () => {
    const LATER = '2026-10-19T09:01:10.0000000Z'
    const closedObservation = (closedAtUtc: string) =>
      observationWire({ phase: 'RecordingResult', open: false, closureReason: 'attempt_terminal', closedAtUtc, phaseEnteredAtUtc: '2026-10-19T09:00:30.0000000Z', phaseDeadlineUtc: '2026-10-19T09:02:30.0000000Z' })
    const recordedAttention = (at: string) =>
      attentionWire({ effectiveReasonCode: 'stale_heartbeat', effectiveAtUtc: at, recordedReasonCode: 'stale_heartbeat', recordedAtUtc: at })

    it.each([
      ['Ended whose closure lies after the read', endedWire(RUN, { observation: closedObservation(LATER) })],
      [
        'Ended whose completion lies after the read',
        endedWire(RUN, {
          attempt: attemptWire({ status: 'Completed', outcome: 'Proposed', dispatched: true, dispatchedAtUtc: '2026-10-19T09:00:05.0000000Z', completedAtUtc: LATER }),
        }),
      ],
      ['Ended whose recorded attention lies after the read', endedWire(RUN, { attention: recordedAttention(LATER) })],
      ['NeedsAttention whose recorded attention lies after the read', needsAttentionWire(RUN, { attention: recordedAttention(LATER) })],
      [
        'NeedsAttention whose closure lies after the read',
        needsAttentionWire(RUN, {
          observation: observationWire({ lastHeartbeatAtUtc: '2026-10-19T09:00:00.0000000Z', open: false, closureReason: 'observer_ended', closedAtUtc: LATER }),
          attention: recordedAttention('2026-10-19T09:00:30.0000000Z'),
        }),
      ],
    ])('refuses %s', (_, body) => {
      expect(decode(body)).toBeNull()
    })

    it('accepts a closure, a completion and a recorded attention at exactly the host clock of the read', () => {
      const body = endedWire(RUN, {
        attempt: attemptWire({ status: 'Completed', outcome: 'Proposed', dispatched: true, dispatchedAtUtc: '2026-10-19T09:00:05.0000000Z', completedAtUtc: OBSERVED_AT }),
        observation: closedObservation(OBSERVED_AT),
        attention: recordedAttention(OBSERVED_AT),
      })

      expect(decode(body)?.status).toBe('Ended')
    })

    it('still shows an Unavailable answer that carries the impossible facts as read, since it claims nothing current', () => {
      expect(decode({ ...unavailableWire(RUN), attempt: attemptWire(), observation: closedObservation(LATER) })?.status).toBe('Unavailable')
    })
  })
})

describe('the supervision time helpers', () => {
  it('reads a UTC text as exact 100-nanosecond ticks and nothing else', () => {
    expect(utcTicks('2026-10-19T09:00:00.0000001Z')! - utcTicks('2026-10-19T09:00:00.0000000Z')!).toBe(1n)
    expect(utcTicks('2026-10-19T09:00:30.0000000Z')! - utcTicks('2026-10-19T09:00:00.0000000Z')!).toBe(AGENT_SUPERVISION_STALE_AFTER_TICKS)
    expect(utcTicks('2026-10-19T09:00:00Z')).toBeNull()
    expect(utcTicks('yesterday')).toBeNull()
  })

  it('measures the report age and the time to the deadline on the host clock, never negative for the age', () => {
    const observation = readingOf(observedWire(RUN), RUN).observation!

    expect(reportAgeSeconds(observation, OBSERVED_AT)).toBe(10)
    expect(reportAgeSeconds(observation, '2026-10-19T09:00:40.0000000Z')).toBe(0)
    expect(secondsToDeadline(observation, OBSERVED_AT)).toBe(60)
    expect(secondsToDeadline(observation, '2026-10-19T09:02:05.0000000Z')).toBe(-5)
  })

  it('watches only an attempt that is not terminal', () => {
    expect(isAgentSupervisionWatching(readingOf(observedWire(RUN), RUN))).toBe(true)
    expect(isAgentSupervisionWatching(readingOf(needsAttentionWire(RUN), RUN))).toBe(true)
    expect(isAgentSupervisionWatching(readingOf(notObservedWire(RUN), RUN))).toBe(true)
    expect(isAgentSupervisionWatching(readingOf(unavailableWire(RUN), RUN))).toBe(true)
    expect(isAgentSupervisionWatching(readingOf(endedWire(RUN), RUN))).toBe(false)
    expect(isAgentSupervisionWatching(readingOf(notObservedWire(RUN, true), RUN))).toBe(false)
    expect(isAgentSupervisionWatching(readingOf(noAgentAttemptWire(RUN), RUN))).toBe(false)
  })
})
