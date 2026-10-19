import { describe, expect, it } from 'vitest'
import {
  PLANNING_SEQUENCE_STATES,
  derivePlanningSequenceControl,
  toPlanningSequenceArmReceipt,
  toPlanningSequenceStatus,
  toPlanningSequenceStopAnswer,
} from './planningSequence'
import type { PlanningSequenceState } from './planningSequence'
import {
  FINGERPRINT,
  TUPLE,
  armReceiptFor,
  armableStatusFor,
  planningGuid,
  refusedStatusFor,
  sequenceFor,
  statusFor,
  stepFor,
  stopAnswerFor,
  unavailableStatusFor,
} from './planningSequenceFixture'

const RUN = 'run-1'
const decode = (raw: unknown) => toPlanningSequenceStatus(raw, RUN)

type Raw = Record<string, unknown>
const withSequence = (state: PlanningSequenceState, sequencePatch: Raw, patch: Raw = {}) => statusFor(RUN, state, patch, sequencePatch)
const withSteps = (state: PlanningSequenceState, steps: Raw[], sequencePatch: Raw = {}, patch: Raw = {}) =>
  withSequence(state, { steps, ...sequencePatch }, patch)

describe('toPlanningSequenceStatus: accepted answers', () => {
  it.each(PLANNING_SEQUENCE_STATES)('accepts a coherent %s status exactly as the host wrote it', (state) => {
    const raw = statusFor(RUN, state)

    const status = decode(raw)

    expect(status).not.toBeNull()
    expect(status?.status).toBe(state)
    expect(status?.sequence?.state).toBe(state)
    expect(status?.sequence?.revision).toBe((raw.sequence as Raw).revision)
  })

  it('keeps the raw revision, event-sequence and time texts as written, never converting them', () => {
    const status = decode(statusFor(RUN, 'Armed', { latestEventSequence: '9223372036854775807' }, { revision: '9007199254740993' }))

    expect(status?.latestEventSequence).toBe('9223372036854775807')
    expect(status?.sequence?.revision).toBe('9007199254740993')
    expect(status?.sequence?.armedAtUtc).toBe('2026-10-09T10:00:00.1234567Z')
  })

  it('accepts a lower-case GUID of any hexadecimal digits, the control of the upper-case refusal below', () => {
    expect(decode(statusFor(RUN, 'Armed', {}, { checkpointId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' }))).not.toBeNull()
  })

  it('accepts no sequence with the exact arming candidate, a fixed refusal, and an unavailable fence as its own answers', () => {
    expect(decode(armableStatusFor(RUN))?.arming).toMatchObject({ available: true, ...TUPLE })
    expect(decode(refusedStatusFor(RUN))?.arming).toMatchObject({ available: false, refusalCode: 'planning_sequence.run_not_pristine' })
    expect(decode(unavailableStatusFor(RUN))).toMatchObject({ status: 'None', manualFence: 'Unavailable', sequence: null, arming: null })
  })

  it('accepts an elapsed deadline read as attention before the host persisted it, and keeps both states', () => {
    const status = decode(
      withSteps('NeedsAttention', [stepFor(0, 'reserved')], {
        recordedState: 'PreparingStep',
        observedBeforePersisted: true,
        reasonCode: 'preparation_expired',
        reasonText: 'The next step was not claimed within two minutes of its reservation; nothing was claimed.',
        endedAtUtc: null,
      }),
    )

    expect(status?.sequence).toMatchObject({ state: 'NeedsAttention', recordedState: 'PreparingStep', observedBeforePersisted: true })
  })

  it('accepts a genuinely late completed attempt read as attention before the host persisted it, with the effective top-level status', () => {
    const status = decode(
      withSteps(
        'NeedsAttention',
        [stepFor(0, 'claimed', { attemptStatus: 'Completed', attemptOutcome: 'Proposed', dispatched: true })],
        {
          recordedState: 'AwaitingStepResult',
          observedBeforePersisted: true,
          reasonCode: 'result_deadline_elapsed',
          reasonText: 'The result deadline elapsed. No process was cancelled; the same attempt stays visible.',
          endedAtUtc: null,
        },
      ),
    )

    expect(status).not.toBeNull()
    expect(status?.status).toBe('NeedsAttention')
    expect(status?.sequence).toMatchObject({ state: 'NeedsAttention', recordedState: 'AwaitingStepResult', observedBeforePersisted: true })
  })

  it('accepts a running linked attempt of an ended sequence as awaiting reconciliation with the Blocked fence', () => {
    const status = decode(
      withSteps('NeedsAttention', [stepFor(0, 'claimed')], {
        reasonCode: 'result_deadline_elapsed',
        reasonText: 'The result deadline elapsed. No process was cancelled; the same attempt stays visible.',
      }),
    )

    expect(status?.sequence).toMatchObject({ awaitingReconciliation: true, outstandingAttemptId: planningGuid(200) })
    expect(derivePlanningSequenceControl(status)).toBe('blocked')
  })

  it('accepts a proved final plan for an accepted sequence and a proved escalation for the depth-two end', () => {
    expect(decode(statusFor(RUN, 'ReadyForImplementation'))?.sequence?.final).toMatchObject({ acceptanceMessageId: planningGuid(301), escalationMessageId: null })
    expect(decode(statusFor(RUN, 'AwaitingHumanDecision'))?.sequence?.final).toMatchObject({ escalationMessageId: planningGuid(311), acceptanceMessageId: null })
  })

  it('accepts an unproved final reference as null, never as a reference', () => {
    expect(decode(withSequence('ReadyForImplementation', { final: null }))?.sequence?.final).toBeNull()
  })
})

describe('toPlanningSequenceStatus: whole-answer refusals', () => {
  it.each([
    ['nothing', undefined],
    ['null', null],
    ['an array', []],
    ['a string', 'Armed'],
    ['an empty object', {}],
  ])('refuses %s', (_, raw) => {
    expect(decode(raw)).toBeNull()
  })

  it('refuses another run, even when everything else is coherent', () => {
    expect(toPlanningSequenceStatus(statusFor('run-2', 'Armed'), RUN)).toBeNull()
  })

  it.each([
    ['an unknown root member', (raw: Raw) => ({ ...raw, extra: true })],
    ['a missing root member', (raw: Raw) => Object.fromEntries(Object.entries(raw).filter(([key]) => key !== 'arming'))],
    ['an unknown sequence member', (raw: Raw) => ({ ...raw, sequence: { ...(raw.sequence as Raw), extra: 1 } })],
    ['a missing sequence member', (raw: Raw) => ({ ...raw, sequence: Object.fromEntries(Object.entries(raw.sequence as Raw).filter(([key]) => key !== 'final')) })],
    ['an unknown step member', (raw: Raw) => ({ ...raw, sequence: { ...(raw.sequence as Raw), steps: [{ ...stepFor(0, 'reserved'), extra: 1 }] } })],
  ])('refuses %s', (_, mutate) => {
    expect(decode(mutate(statusFor(RUN, 'PreparingStep')))).toBeNull()
  })

  it.each([
    ['a status outside the closed set', { status: 'Running' }],
    ['a lower-case status', { status: 'armed' }],
    ['a fence outside the closed set', { manualFence: 'released' }],
    ['a numeric event sequence', { latestEventSequence: 12 }],
    ['a non-canonical event sequence', { latestEventSequence: '012' }],
    ['a negative event sequence', { latestEventSequence: '-1' }],
    ['an event sequence beyond Int64', { latestEventSequence: '9223372036854775808' }],
    ['a status that differs from the sequence state', { status: 'Stopped' }],
    ['a Released fence while the sequence holds authority', { manualFence: 'Released' }],
    ['an Unavailable fence beside a sequence', { manualFence: 'Unavailable' }],
    ['an arming projection beside a sequence', { arming: { available: false } }],
  ])('refuses %s', (_, patch) => {
    expect(decode(statusFor(RUN, 'Armed', patch))).toBeNull()
  })

  it('refuses a Blocked fence for a sequence that holds no authority and runs no attempt', () => {
    expect(decode(statusFor(RUN, 'Stopped', { manualFence: 'Blocked' }))).toBeNull()
  })

  it('refuses a Released fence while a linked attempt of an ended sequence is still running', () => {
    const blocked = withSteps('NeedsAttention', [stepFor(0, 'claimed')], { reasonCode: 'result_deadline_elapsed', reasonText: 'Elapsed.' })
    expect(decode(blocked)).not.toBeNull()
    expect(decode({ ...blocked, manualFence: 'Released' })).toBeNull()
  })

  it.each([
    ['a sequence with no arming and an Unavailable fence that still names arming', { ...unavailableStatusFor(RUN), arming: armableStatusFor(RUN).arming }],
    ['no sequence with a Blocked fence', armableStatusFor(RUN, { manualFence: 'Blocked' })],
    ['no sequence and no arming while the fence is Released', armableStatusFor(RUN, { arming: null })],
    ['no sequence beside a sequence-less status that names one', armableStatusFor(RUN, { sequence: sequenceFor('Armed') })],
    ['an available candidate with a refusal', armableStatusFor(RUN, {}, { refusalCode: 'planning_sequence.project_busy' })],
    ['an available candidate missing its tuple', armableStatusFor(RUN, {}, { checkpointId: null })],
    ['an available candidate with a malformed fingerprint', armableStatusFor(RUN, {}, { fingerprintSha256: FINGERPRINT.toUpperCase() })],
    ['an unavailable candidate without a refusal', refusedStatusFor(RUN, 'planning_sequence.project_busy', ' ')],
    ['an unavailable candidate with an unknown refusal code', refusedStatusFor(RUN, 'planning_sequence.something_else')],
    ['an unavailable candidate that still names a tuple', armableStatusFor(RUN, {}, { available: false, refusalCode: 'planning_sequence.project_busy', refusalText: 'Busy.' })],
  ])('refuses %s', (_, raw) => {
    expect(decode(raw)).toBeNull()
  })

  it.each([
    ['a sequence id that is not a GUID', { sequenceId: 'sequence-1' }],
    ['an empty GUID', { workspaceId: '00000000-0000-0000-0000-000000000000' }],
    ['an upper-case GUID', { checkpointId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'.toUpperCase() }],
    ['an upper-case fingerprint', { fingerprintSha256: FINGERPRINT.toUpperCase() }],
    ['a short fingerprint', { fingerprintSha256: FINGERPRINT.slice(1) }],
    ['a numeric revision', { revision: 3 }],
    ['a zero revision', { revision: '0' }],
    ['a padded revision', { revision: '01' }],
    ['a revision beyond Int64', { revision: '9223372036854775808' }],
    ['a time that is not a UTC round-trip text', { armedAtUtc: '2026-10-09T10:00:00Z' }],
    ['a time with an offset', { updatedAtUtc: '2026-10-09T10:00:00.1234567+00:00' }],
    ['a time that names no calendar day', { armedAtUtc: '2026-02-30T10:00:00.1234567Z' }],
    ['a stop time on an Armed sequence', { stopRequestedAtUtc: '2026-10-09T10:20:00.1234567Z' }],
    ['an end time on a live sequence', { endedAtUtc: '2026-10-09T10:20:00.1234567Z' }],
    ['another step maximum', { maximumSteps: 6 }],
    ['a next action outside the closed set', { nextActionCode: 'resume' }],
    ['a next action that does not belong to the state', { nextActionCode: 'inspect_and_reconcile' }],
    ['an empty next-action text', { nextActionText: '   ' }],
    ['a next-action text with a control character', { nextActionText: 'Wait' + String.fromCharCode(7) }],
    ['a reason text without a reason', { reasonText: 'Stopped.' }],
    ['a reason on a live sequence', { reasonCode: 'step_failed', reasonText: 'Failed.' }],
    ['a recorded state that differs without an observed deadline', { recordedState: 'PreparingStep' }],
    ['a boolean written as text', { observedBeforePersisted: 'false' }],
    ['a reconciliation flag written as text', { awaitingReconciliation: 'false' }],
    ['an outstanding attempt with nothing running', { outstandingAttemptId: planningGuid(9) }],
    ['a detail code on a live sequence', { detailCode: 'planning_sequence.step_expired' }],
  ])('refuses %s', (_, patch) => {
    expect(decode(statusFor(RUN, 'Armed', {}, patch))).toBeNull()
  })

  it.each([
    ['a reason that no ended state of its kind records', 'Stopped' as const, { reasonCode: 'plan_accepted', reasonText: 'Accepted.' }],
    ['an ended sequence without a reason', 'Stopped' as const, { reasonCode: null, reasonText: null }],
    ['a detail code with characters outside the closed alphabet', 'NeedsAttention' as const, { detailCode: 'Planning Sequence.Step' }],
    ['a detail code that is too long', 'NeedsAttention' as const, { detailCode: 'a'.repeat(97) }],
    ['a detail code on a state that is not attention', 'Stopped' as const, { detailCode: 'planning_sequence.step_expired' }],
    ['an ended sequence without its end time', 'Stopped' as const, { endedAtUtc: null }],
    ['a stopped sequence without its stop time', 'Stopped' as const, { stopRequestedAtUtc: null }],
    ['a Stopping sequence without its stop time', 'Stopping' as const, { stopRequestedAtUtc: null }],
  ])('refuses %s', (_, state, patch) => {
    const extra = state === 'NeedsAttention' ? { ...patch } : patch
    expect(decode(statusFor(RUN, state, {}, extra))).toBeNull()
  })

  it.each([
    ['an observed deadline whose effective state is not attention', { observedBeforePersisted: true, recordedState: 'PreparingStep' }],
    ['an observed deadline of a recorded state that cannot expire', {
      state: 'NeedsAttention', recordedState: 'Armed', observedBeforePersisted: true, reasonCode: 'preparation_expired', reasonText: 'Expired.',
    }],
    ['an observed deadline with the wrong reason for its recorded state', {
      state: 'NeedsAttention', recordedState: 'PreparingStep', observedBeforePersisted: true, reasonCode: 'result_deadline_elapsed', reasonText: 'Elapsed.',
      nextActionCode: 'inspect_and_reconcile', nextActionText: 'Inspect.',
    }],
    ['an observed deadline that also carries a detail', {
      state: 'NeedsAttention', recordedState: 'PreparingStep', observedBeforePersisted: true, reasonCode: 'preparation_expired', reasonText: 'Expired.',
      detailCode: 'planning_sequence.step_expired', nextActionCode: 'inspect_and_reconcile', nextActionText: 'Inspect.',
    }],
  ])('refuses %s', (_, patch) => {
    expect(decode(withSteps('PreparingStep', [stepFor(0, 'reserved')], patch, { manualFence: 'Blocked' }))).toBeNull()
  })

  const claimed = (patch: Raw = {}) => stepFor(0, 'claimed', patch)
  // Every case below is otherwise coherent for its state, so the one violation named is the only reason for the refusal.
  it.each([
    ['more than five steps', 'AwaitingStepResult', [0, 1, 2, 3, 4].map((ordinal) => stepFor(ordinal, 'concluded')).concat(stepFor(5, 'concluded', { role: 'Resolver', provider: 'Codex' }))],
    ['steps that skip an ordinal', 'PreparingStep', [stepFor(0, 'concluded'), stepFor(2, 'reserved')]],
    ['steps that do not start at zero', 'PreparingStep', [stepFor(1, 'reserved')]],
    ['two steps with the same identity', 'PreparingStep', [stepFor(0, 'concluded'), stepFor(1, 'reserved', { stepId: planningGuid(100) })]],
    ['an unconcluded step before the last step', 'PreparingStep', [stepFor(0, 'reserved'), stepFor(1, 'reserved')]],
    ['a role that does not match the ordinal', 'PreparingStep', [stepFor(0, 'reserved', { role: 'Resolver' })]],
    ['a provider that does not match the role', 'PreparingStep', [stepFor(0, 'reserved', { provider: 'ClaudeCode' })]],
    ['a step state outside the closed set', 'PreparingStep', [stepFor(0, 'reserved', { state: 'Running' })]],
    ['an ordinal written as text', 'PreparingStep', [stepFor(0, 'reserved', { ordinal: '0' })]],
    ['a reserved step that names an attempt', 'PreparingStep', [stepFor(0, 'reserved', { attemptId: planningGuid(200) })]],
    ['a reserved step with a claim time', 'PreparingStep', [stepFor(0, 'reserved', { claimedAtUtc: '2026-10-09T10:00:30.1234567Z' })]],
    ['a reserved step with a reason', 'PreparingStep', [stepFor(0, 'reserved', { reasonCode: 'step_failed' })]],
    ['a revoked step without a reason', 'Stopped', [stepFor(0, 'revoked', { reasonCode: null })]],
    ['a revoked step with an unknown reason', 'Stopped', [stepFor(0, 'revoked', { reasonCode: 'cancelled' })]],
    ['a claimed step without an attempt', 'AwaitingStepResult', [claimed({ attemptId: null })]],
    ['a claimed step without an attempt number', 'AwaitingStepResult', [claimed({ attemptNumber: null })]],
    ['a claimed step with a zero attempt number', 'AwaitingStepResult', [claimed({ attemptNumber: 0 })]],
    ['a claimed step with an unknown attempt status', 'AwaitingStepResult', [claimed({ attemptStatus: 'Executing' })]],
    ['a claimed step without a dispatch fact', 'AwaitingStepResult', [claimed({ dispatched: null })]],
    ['a claimed step with a dispatch fact written as text', 'AwaitingStepResult', [claimed({ dispatched: 'false' })]],
    ['a claimed step without its claim time', 'AwaitingStepResult', [claimed({ claimedAtUtc: null })]],
    ['a running attempt that already has an outcome', 'AwaitingStepResult', [claimed({ attemptOutcome: 'Proposed' })]],
    ['a concluded step whose attempt is still running', 'AwaitingStepResult', [stepFor(0, 'concluded', { attemptStatus: 'Running', attemptOutcome: null })]],
    ['an outcome that is not an identifier', 'AwaitingStepResult', [stepFor(0, 'concluded', { attemptOutcome: 'Proposed; drop table' })]],
    ['a preparation deadline that is not two minutes after the reservation', 'PreparingStep', [stepFor(0, 'reserved', { preparationDeadlineUtc: '2026-10-09T10:03:00.1234567Z' })]],
    ['a malformed result deadline', 'AwaitingStepResult', [claimed({ resultDeadlineUtc: 'soon' })]],
  ] as [string, PlanningSequenceState, Raw[]][])('refuses %s', (_, state, steps) => {
    expect(decode(withSteps(state, steps))).toBeNull()
  })

  it('accepts the otherwise coherent step tables the refusals above vary', () => {
    expect(decode(withSteps('PreparingStep', [stepFor(0, 'concluded'), stepFor(1, 'reserved')]))).not.toBeNull()
    expect(decode(withSteps('AwaitingStepResult', [stepFor(0, 'concluded')]))).not.toBeNull()
    expect(decode(withSteps('AwaitingStepResult', [claimed()]))).not.toBeNull()
    expect(decode(withSteps('AwaitingStepResult', [stepFor(0, 'concluded', { attemptOutcome: null, attemptStatus: 'Failed' })]))).not.toBeNull()
    expect(decode(withSteps('Stopped', [stepFor(0, 'revoked')]))).not.toBeNull()
    expect(decode(withSteps('AwaitingStepResult', [0, 1, 2, 3, 4].map((ordinal) => stepFor(ordinal, 'concluded'))))).not.toBeNull()
  })

  it.each([
    ['an Armed sequence with a step', 'Armed' as const, [stepFor(0, 'reserved')]],
    ['a PreparingStep sequence with no reserved step', 'PreparingStep' as const, [stepFor(0, 'claimed')]],
    ['an AwaitingStepResult sequence whose last step is only reserved', 'AwaitingStepResult' as const, [stepFor(0, 'reserved')]],
    ['an accepted plan that ended at the Planner', 'ReadyForImplementation' as const, [stepFor(0, 'concluded')]],
    ['an accepted plan that ended at a Resolver', 'ReadyForImplementation' as const, [stepFor(0, 'concluded'), stepFor(1, 'concluded'), stepFor(2, 'concluded')]],
    ['an escalation that ended before the second Resolver', 'AwaitingHumanDecision' as const, [stepFor(0, 'concluded'), stepFor(1, 'concluded')]],
  ])('refuses %s', (_, state, steps) => {
    const raw = statusFor(RUN, state, {}, { steps })
    expect(decode(raw)).toBeNull()
  })

  it.each([
    ['a final reference on a live sequence', 'Armed' as const, { final: { finalProposalMessageId: planningGuid(300), acceptanceMessageId: planningGuid(301), escalationMessageId: null, provingAttemptId: planningGuid(201) } }],
    ['an accepted plan without its acceptance', 'ReadyForImplementation' as const, { final: { finalProposalMessageId: planningGuid(300), acceptanceMessageId: null, escalationMessageId: null, provingAttemptId: planningGuid(201) } }],
    ['an accepted plan that also names an escalation', 'ReadyForImplementation' as const, { final: { finalProposalMessageId: planningGuid(300), acceptanceMessageId: planningGuid(301), escalationMessageId: planningGuid(311), provingAttemptId: planningGuid(201) } }],
    ['an escalation that names an acceptance instead', 'AwaitingHumanDecision' as const, { final: { finalProposalMessageId: planningGuid(310), acceptanceMessageId: planningGuid(301), escalationMessageId: null, provingAttemptId: planningGuid(204) } }],
    ['a proving attempt that is not the last concluded step', 'ReadyForImplementation' as const, { final: { finalProposalMessageId: planningGuid(300), acceptanceMessageId: planningGuid(301), escalationMessageId: null, provingAttemptId: planningGuid(200) } }],
    ['a final reference with an unknown member', 'ReadyForImplementation' as const, { final: { finalProposalMessageId: planningGuid(300), acceptanceMessageId: planningGuid(301), escalationMessageId: null, provingAttemptId: planningGuid(201), path: 'x' } }],
    ['a final reference that is not a GUID', 'ReadyForImplementation' as const, { final: { finalProposalMessageId: 'message-1', acceptanceMessageId: planningGuid(301), escalationMessageId: null, provingAttemptId: planningGuid(201) } }],
  ])('refuses %s', (_, state, patch) => {
    expect(decode(statusFor(RUN, state, {}, patch))).toBeNull()
  })

  it.each([
    ['an outstanding attempt id that names no running attempt', { outstandingAttemptId: planningGuid(299) }],
    ['no outstanding attempt id while a linked attempt is running', { outstandingAttemptId: null }],
    ['a reconciliation flag while the sequence still holds authority', { awaitingReconciliation: true }],
  ])('refuses %s', (_, patch) => {
    expect(decode(withSteps('AwaitingStepResult', [stepFor(0, 'claimed')], patch))).toBeNull()
  })

  it('refuses a reconciliation flag that is missing for an ended sequence with a running attempt', () => {
    const ended = withSteps('NeedsAttention', [stepFor(0, 'claimed')], { reasonCode: 'step_failed', reasonText: 'Failed.' })
    expect(decode(ended)).not.toBeNull()
    expect(decode(withSteps('NeedsAttention', [stepFor(0, 'claimed')], { reasonCode: 'step_failed', reasonText: 'Failed.', awaitingReconciliation: false }))).toBeNull()
  })
})

describe('derivePlanningSequenceControl', () => {
  it.each([
    ['nothing validated', null, 'unknown'],
    ['an unavailable fence', decode(unavailableStatusFor(RUN)), 'unknown'],
    ['a sequence holding authority', decode(statusFor(RUN, 'AwaitingStepResult')), 'blocked'],
    ['a stopping sequence', decode(statusFor(RUN, 'Stopping')), 'blocked'],
    ['an ended sequence', decode(statusFor(RUN, 'Stopped')), 'released'],
    ['an accepted plan', decode(statusFor(RUN, 'ReadyForImplementation')), 'released'],
    ['no sequence', decode(armableStatusFor(RUN)), 'none'],
    ['no sequence and a refused arming', decode(refusedStatusFor(RUN)), 'none'],
  ] as const)('is %s: %s', (_, status, control) => {
    expect(derivePlanningSequenceControl(status)).toBe(control)
  })
})

describe('toPlanningSequenceArmReceipt', () => {
  it('accepts exactly the receipt of the run and tuple that was armed, and a replay in whatever state it has reached', () => {
    expect(toPlanningSequenceArmReceipt(armReceiptFor(RUN), RUN, TUPLE)).toMatchObject({ replayed: false, state: 'Armed', revision: '1' })
    expect(toPlanningSequenceArmReceipt(armReceiptFor(RUN, { replayed: true, state: 'Stopped', revision: '7' }), RUN, TUPLE)).toMatchObject({
      replayed: true,
      state: 'Stopped',
    })
  })

  it.each([
    ['nothing', undefined],
    ['another run', armReceiptFor('run-2')],
    ['another workspace', armReceiptFor(RUN, { workspaceId: planningGuid(8) })],
    ['another checkpoint', armReceiptFor(RUN, { checkpointId: planningGuid(8) })],
    ['another fingerprint', armReceiptFor(RUN, { fingerprintSha256: 'cd'.repeat(32) })],
    ['an unknown member', { ...armReceiptFor(RUN), extra: 1 }],
    ['a missing member', Object.fromEntries(Object.entries(armReceiptFor(RUN)).filter(([key]) => key !== 'replayed'))],
    ['a state outside the closed set', armReceiptFor(RUN, { state: 'Running' })],
    ['a first arming that is not Armed', armReceiptFor(RUN, { state: 'Stopping' })],
    ['a first arming that is not revision 1', armReceiptFor(RUN, { revision: '2' })],
    ['a non-canonical revision', armReceiptFor(RUN, { replayed: true, revision: '007' })],
    ['a malformed time', armReceiptFor(RUN, { armedAtUtc: 'now' })],
    ['a replayed flag written as text', armReceiptFor(RUN, { replayed: 'true' })],
    ['a sequence id that is not a GUID', armReceiptFor(RUN, { sequenceId: 's' })],
  ])('refuses %s', (_, raw) => {
    expect(toPlanningSequenceArmReceipt(raw, RUN, TUPLE)).toBeNull()
  })
})

describe('toPlanningSequenceStopAnswer', () => {
  const SEQUENCE = planningGuid(1)
  const accept = (raw: unknown) => toPlanningSequenceStopAnswer(raw, RUN, SEQUENCE)

  it.each([
    ['a stop that won after a claim', { state: 'Stopping', reasonCode: null, changed: true }],
    ['a stop that won before a claim', { state: 'Stopped', reasonCode: 'stopped_before_claim', changed: true }],
    ['a repeated stop', { state: 'Stopping', reasonCode: null, changed: false }],
    ['a late stop of a completed sequence', { state: 'ReadyForImplementation', reasonCode: 'plan_accepted', changed: false }],
    ['a late stop of a drained sequence', { state: 'Stopped', reasonCode: 'stopped_after_step', changed: false }],
    ['a late stop of an attention sequence', { state: 'NeedsAttention', reasonCode: 'step_failed', changed: false }],
  ])('accepts %s', (_, patch) => {
    expect(accept(stopAnswerFor(RUN, patch))).toMatchObject(patch)
  })

  it.each([
    ['nothing', undefined],
    ['another run', stopAnswerFor('run-2')],
    ['another sequence', stopAnswerFor(RUN, { sequenceId: planningGuid(2) })],
    ['an unknown member', { ...stopAnswerFor(RUN), extra: 1 }],
    ['a missing member', Object.fromEntries(Object.entries(stopAnswerFor(RUN)).filter(([key]) => key !== 'changed'))],
    ['a state that can still advance', stopAnswerFor(RUN, { state: 'AwaitingStepResult', changed: false })],
    ['an Armed sequence', stopAnswerFor(RUN, { state: 'Armed', changed: false })],
    ['a state outside the closed set', stopAnswerFor(RUN, { state: 'Cancelled' })],
    ['a reason that does not belong to the state', stopAnswerFor(RUN, { state: 'Stopped', reasonCode: 'plan_accepted', changed: false })],
    ['a Stopping answer with a reason', stopAnswerFor(RUN, { state: 'Stopping', reasonCode: 'stopped_before_claim' })],
    ['a change that no stop makes', stopAnswerFor(RUN, { state: 'ReadyForImplementation', reasonCode: 'plan_accepted', changed: true })],
    ['a change to Stopped after a step', stopAnswerFor(RUN, { state: 'Stopped', reasonCode: 'stopped_after_step', changed: true })],
    ['a non-canonical revision', stopAnswerFor(RUN, { revision: '04' })],
    ['a changed flag written as text', stopAnswerFor(RUN, { changed: 'true' })],
  ])('refuses %s', (_, raw) => {
    expect(accept(raw)).toBeNull()
  })
})
