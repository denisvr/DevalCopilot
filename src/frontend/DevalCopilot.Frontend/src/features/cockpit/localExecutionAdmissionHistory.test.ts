import { describe, expect, it } from 'vitest'
import { toLocalExecutionAdmissionHistory } from './localExecutionAdmissionHistory'
import {
  RUN,
  admissionFor,
  admissionIdFor,
  guidOf,
  notQueuedHistoryFor,
  notRecordedHistoryFor,
  queuedHeaderFor,
  recordedHistoryFor,
  reservedHistoryFor,
  retiredWithStepHistoryFor,
  stepIdFor,
  stoppedWhileWaitingHistoryFor,
  unavailableHistoryFor,
  unresolvedHistoryFor,
} from './localExecutionAdmissionHistoryFixture'

const accepted = (raw: unknown) => toLocalExecutionAdmissionHistory(raw, RUN)

const withAdmission = (ordinal: number, patch: Record<string, unknown>) =>
  recordedHistoryFor(RUN, [admissionFor(0), admissionFor(1), admissionFor(2)].map((item) => (item.ordinal === ordinal ? { ...item, ...patch } : item)))

const withHeader = (patch: Record<string, unknown>, admissions?: Record<string, unknown>[]) =>
  recordedHistoryFor(RUN, admissions, { sequence: queuedHeaderFor(patch) })

describe('toLocalExecutionAdmissionHistory', () => {
  it('accepts every closed status with its exact shape', () => {
    expect(accepted(notRecordedHistoryFor())).toEqual({ status: 'NotRecorded' })
    expect(accepted(notQueuedHistoryFor())).toEqual({ status: 'NotQueued' })
    expect(accepted(unavailableHistoryFor())).toEqual({ status: 'Unavailable' })
    const recorded = accepted(recordedHistoryFor())
    expect(recorded?.status).toBe('Recorded')
    if (recorded?.status === 'Recorded') {
      expect(recorded.admissions.map((item) => item.recordedState)).toEqual(['Consumed', 'Consumed', 'Waiting'])
      expect(recorded.admissions[2].stepId).toBeNull()
      expect(recorded.admissions[1].recipeName).toBe('Unit <b>tests</b>')
      expect(recorded.sequence.sequenceRevision).toBe('7')
    }
  })

  it('accepts every admission state with its fixed link and reason shape', () => {
    for (const history of [stoppedWhileWaitingHistoryFor(), retiredWithStepHistoryFor(), unresolvedHistoryFor(), reservedHistoryFor()]) {
      expect(accepted(history)?.status).toBe('Recorded')
    }
    const retired = accepted(stoppedWhileWaitingHistoryFor())
    expect(retired?.status === 'Recorded' ? retired.admissions[2].stepId : 'x').toBeNull()
    const revoked = accepted(retiredWithStepHistoryFor())
    expect(revoked?.status === 'Recorded' ? revoked.admissions[1].stepId : null).toBe(stepIdFor(1))
  })

  it('accepts an order number above the JavaScript safe integer range as the canonical Int64 text it is', () => {
    const big = accepted(withAdmission(2, { admissionNumber: '9223372036854775807' }))
    expect(big?.status).toBe('Recorded')
    expect(accepted(withAdmission(2, { admissionNumber: '9223372036854775808' }))).toBeNull()
  })

  it('refuses a wrong run, an extra, a missing or a renamed member at every level', () => {
    expect(toLocalExecutionAdmissionHistory(recordedHistoryFor(), guidOf('r', 9))).toBeNull()
    expect(accepted({ ...recordedHistoryFor(), extra: 1 })).toBeNull()
    const { admissions: _admissions, ...withoutAdmissions } = recordedHistoryFor()
    expect(accepted(withoutAdmissions)).toBeNull()
    expect(accepted(withHeader({ extra: 1 }))).toBeNull()
    expect(accepted(withAdmission(1, { extra: 1 }))).toBeNull()
    const { reasonCode: _reason, ...withoutReason } = admissionFor(0)
    expect(accepted(recordedHistoryFor(RUN, [withoutReason, admissionFor(1), admissionFor(2)]))).toBeNull()
    for (const raw of [null, undefined, 'text', 7, [], {}]) {
      expect(accepted(raw)).toBeNull()
    }
  })

  it('refuses an unknown status and a non-Recorded answer carrying a header or a list', () => {
    expect(accepted({ ...recordedHistoryFor(), status: 'Queued' })).toBeNull()
    expect(accepted({ ...recordedHistoryFor(), status: 'recorded' })).toBeNull()
    expect(accepted({ ...notRecordedHistoryFor(), sequence: queuedHeaderFor() })).toBeNull()
    expect(accepted({ ...notQueuedHistoryFor(), admissions: [admissionFor(0)] })).toBeNull()
    expect(accepted({ ...unavailableHistoryFor(), admissions: [admissionFor(0)] })).toBeNull()
    expect(accepted({ ...notQueuedHistoryFor(), admissions: null })).toBeNull()
  })

  it('refuses a Recorded answer with no header and an empty ledger', () => {
    expect(accepted({ ...recordedHistoryFor(), sequence: null })).toBeNull()
    expect(accepted(recordedHistoryFor(RUN, []))).toBeNull()
  })

  it('refuses an incoherent header: mode, state, revision, deadline, ordering, reason and end', () => {
    expect(accepted(withHeader({ admissionMode: 'Immediate' }))).toBeNull()
    expect(accepted(withHeader({ admissionMode: 'Bogus' }))).toBeNull()
    expect(accepted(withHeader({ recordedState: 'Armed' }))).toBeNull()
    expect(accepted(withHeader({ recordedState: 'Bogus' }))).toBeNull()
    for (const revision of ['0', '01', '-1', '1.5', 'x', '', '9007199254740992']) {
      expect(accepted(withHeader({ sequenceRevision: revision }))).toBeNull()
    }
    expect(accepted(withHeader({ sequenceRevision: 7 }))).toBeNull()
    expect(accepted(withHeader({ advancementDeadlineUtc: '2026-10-20T11:00:01.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ advancementDeadlineUtc: '2026-10-20T10:59:59.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ updatedAtUtc: '2026-10-19T09:00:00.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ armedAtUtc: '2026-10-20T09:00:00Z' }))).toBeNull()
    expect(accepted(withHeader({ armedAtUtc: '2026-10-20 09:00:00.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ reasonCode: 'review_approved' }))).toBeNull()
    expect(accepted(withHeader({ endedAtUtc: '2026-10-20T09:00:40.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ recordedState: 'Stopped', reasonCode: null, endedAtUtc: '2026-10-20T09:00:40.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ recordedState: 'Stopped', reasonCode: 'made_up', endedAtUtc: '2026-10-20T09:00:40.0000000Z' }))).toBeNull()
    expect(accepted(withHeader({ recordedState: 'Stopped', reasonCode: 'stopped_before_claim', endedAtUtc: null }))).toBeNull()
    expect(accepted(withHeader({ sequenceId: '00000000-0000-0000-0000-000000000000' }))).toBeNull()
    expect(accepted(withHeader({ sequenceId: 'ABCDEFAB-0000-4000-8000-000000000001' }))).toBeNull()
  })

  it('refuses a bad identity, order number or ordinal', () => {
    expect(accepted(withAdmission(1, { admissionId: 'not-a-guid' }))).toBeNull()
    expect(accepted(withAdmission(1, { admissionId: admissionIdFor(0) }))).toBeNull()
    for (const number of ['0', '01', '-5', '1.5', 'x', '', 12, null]) {
      expect(accepted(withAdmission(1, { admissionNumber: number }))).toBeNull()
    }
    expect(accepted(withAdmission(1, { admissionNumber: '11' }))).toBeNull()
    expect(accepted(withAdmission(1, { admissionNumber: '10' }))).toBeNull()
    expect(accepted(withAdmission(2, { admissionNumber: '13' }))).toBeNull()
    expect(accepted(withAdmission(1, { ordinal: 2 }))).toBeNull()
    expect(accepted(withAdmission(1, { ordinal: 1.5 }))).toBeNull()
    expect(accepted(withAdmission(1, { ordinal: -1 }))).toBeNull()
    expect(accepted(recordedHistoryFor(RUN, [admissionFor(0), admissionFor(2)]))).toBeNull()
    expect(accepted(recordedHistoryFor(RUN, [admissionFor(1), admissionFor(0), admissionFor(2)]))).toBeNull()
  })

  it('refuses a broken kind, pass or recipe shape', () => {
    expect(accepted(withAdmission(1, { kind: 'Bogus' }))).toBeNull()
    expect(accepted(withAdmission(1, { kind: 'CodeReview' }))).toBeNull()
    expect(accepted(withAdmission(0, { kind: 'CodeReview' }))).toBeNull()
    expect(accepted(withAdmission(1, { kind: 'Implementation' }))).toBeNull()
    expect(accepted(withAdmission(1, { passNumber: 0 }))).toBeNull()
    expect(accepted(withAdmission(1, { passNumber: 4 }))).toBeNull()
    expect(accepted(withAdmission(1, { passNumber: 1.5 }))).toBeNull()
    expect(accepted(withAdmission(0, { passNumber: 1 }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeOrdinal: null }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeName: null }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeOrdinal: 32 }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeOrdinal: -1 }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeName: '   ' }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeName: 'x'.repeat(201) }))).toBeNull()
    expect(accepted(withAdmission(1, { recipeName: 'x'.repeat(200) }))?.status).toBe('Recorded')
    expect(accepted(withAdmission(2, { recipeOrdinal: 0, recipeName: 'x' }))).toBeNull()
    expect(accepted(withAdmission(0, { recipeOrdinal: 0, recipeName: 'x' }))).toBeNull()
  })

  it('refuses a repeated pass and recipe membership and a correction with no earlier origin', () => {
    const second = admissionFor(2, { kind: 'Verification', recipeOrdinal: 0, recipeName: 'again', passNumber: 1 })
    expect(accepted(recordedHistoryFor(RUN, [admissionFor(0), admissionFor(1), second]))).toBeNull()
    const correction = admissionFor(2, { kind: 'ReviewCorrection', passNumber: 1 })
    expect(accepted(recordedHistoryFor(RUN, [admissionFor(0), admissionFor(1), correction]))).toBeNull()
    const review = admissionFor(2, { kind: 'CodeReview', recordedState: 'Consumed', stepId: stepIdFor(2), revision: '3', waitingReason: 'AwaitingAdmissionCheck' })
    const fixed = admissionFor(3, { kind: 'ReviewCorrection', passNumber: 1 })
    expect(
      accepted(recordedHistoryFor(RUN, [admissionFor(0), admissionFor(1), review, fixed], { sequence: queuedHeaderFor({ recordedState: 'WaitingForAdmission' }) }))?.status,
    ).toBe('Recorded')
    const wrongOrigin = admissionFor(3, { kind: 'DiagnosisCorrection', passNumber: 1 })
    expect(
      accepted(recordedHistoryFor(RUN, [admissionFor(0), admissionFor(1), review, wrongOrigin], { sequence: queuedHeaderFor({ recordedState: 'WaitingForAdmission' }) })),
    ).toBeNull()
  })

  it('refuses an unknown state, waiting reason or a malformed time', () => {
    expect(accepted(withAdmission(1, { recordedState: 'Bogus' }))).toBeNull()
    expect(accepted(withAdmission(2, { recordedState: 'waiting' }))).toBeNull()
    expect(accepted(withAdmission(2, { waitingReason: 'Bogus' }))).toBeNull()
    expect(accepted(withAdmission(2, { waitingReason: null }))).toBeNull()
    for (const key of ['waitingReasonRecordedAtUtc', 'enqueuedAtUtc', 'updatedAtUtc']) {
      expect(accepted(withAdmission(2, { [key]: '2026-10-20T09:00:21Z' }))).toBeNull()
      expect(accepted(withAdmission(2, { [key]: null }))).toBeNull()
      expect(accepted(withAdmission(2, { [key]: 20 }))).toBeNull()
    }
    expect(accepted(withAdmission(2, { enqueuedAtUtc: '2026-10-20T09:00:59.0000000Z' }))).toBeNull()
    expect(accepted(withAdmission(2, { updatedAtUtc: '2026-10-20T09:00:00.0000000Z' }))).toBeNull()
    expect(accepted(withAdmission(2, { revision: '0' }))).toBeNull()
    expect(accepted(withAdmission(2, { revision: 2 }))).toBeNull()
  })

  it('refuses a reason on a state that carries none and a missing or unknown reason on one that must', () => {
    expect(accepted(withAdmission(1, { reasonCode: 'stopped_before_claim' }))).toBeNull()
    expect(accepted(withAdmission(2, { reasonCode: 'stopped_before_claim' }))).toBeNull()
    const stopped = stoppedWhileWaitingHistoryFor() as unknown as { admissions: Record<string, unknown>[] }
    const retired = (reasonCode: unknown) =>
      recordedHistoryFor(RUN, [stopped.admissions[0], stopped.admissions[1], { ...stopped.admissions[2], reasonCode }], {
        sequence: (stopped as unknown as { sequence: unknown }).sequence,
      })
    expect(accepted(retired('stopped_before_claim'))?.status).toBe('Recorded')
    expect(accepted(retired(null))).toBeNull()
    expect(accepted(retired('made_up'))).toBeNull()
    expect(accepted(retired(3))).toBeNull()
  })

  it('refuses a link that its state forbids or requires', () => {
    expect(accepted(withAdmission(2, { stepId: stepIdFor(2) }))).toBeNull()
    expect(accepted(withAdmission(1, { stepId: null }))).toBeNull()
    expect(accepted(withAdmission(1, { stepId: 'not-a-guid' }))).toBeNull()
    expect(accepted(withAdmission(1, { stepId: stepIdFor(0) }))).toBeNull()
    const reserved = reservedHistoryFor() as unknown as { admissions: Record<string, unknown>[]; sequence: unknown }
    expect(
      accepted({ ...reserved, admissions: [reserved.admissions[0], { ...reserved.admissions[1], stepId: null }] }),
    ).toBeNull()
    const unresolved = unresolvedHistoryFor() as unknown as { admissions: Record<string, unknown>[]; sequence: unknown }
    expect(
      accepted({ ...unresolved, admissions: [unresolved.admissions[0], { ...unresolved.admissions[1], stepId: null }] }),
    ).toBeNull()
  })

  it('refuses a ledger in which anything but a Consumed admission precedes the last', () => {
    const retired = admissionFor(1, { recordedState: 'Retired', reasonCode: 'stopped_before_claim', stepId: null })
    expect(accepted(recordedHistoryFor(RUN, [admissionFor(0), retired, admissionFor(2)]))).toBeNull()
    const waiting = admissionFor(0, { recordedState: 'Waiting', stepId: null })
    expect(accepted(recordedHistoryFor(RUN, [waiting, admissionFor(1), admissionFor(2)]))).toBeNull()
  })

  it('refuses a last admission that does not fit the recorded sequence state', () => {
    const consumedTail = [admissionFor(0), admissionFor(1)]
    expect(accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: 'WaitingForAdmission' }) }))).toBeNull()
    expect(accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: 'PreparingStep' }) }))).toBeNull()
    expect(accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: 'AwaitingStepResult' }) }))?.status).toBe('Recorded')
    expect(accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: 'Stopping' }) }))?.status).toBe('Recorded')
    for (const state of ['NeedsAttention', 'Stopped', 'Interrupted']) {
      expect(
        accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: state, reasonCode: 'host_restarted', endedAtUtc: '2026-10-20T09:00:40.0000000Z' }) }))?.status,
      ).toBe('Recorded')
    }
    for (const state of ['ReadyForHumanApproval', 'HumanEscalation']) {
      expect(
        accepted(recordedHistoryFor(RUN, consumedTail, { sequence: queuedHeaderFor({ recordedState: state, reasonCode: 'review_approved', endedAtUtc: '2026-10-20T09:00:40.0000000Z' }) }))?.status,
      ).toBe('Recorded')
    }
    // A waiting tail only fits a waiting sequence, and a reserved tail only a preparing one; an ended cycle never ends waiting.
    expect(accepted(withHeader({ recordedState: 'Stopped', reasonCode: 'stopped_before_claim', endedAtUtc: '2026-10-20T09:00:40.0000000Z' }))).toBeNull()
    const reserved = reservedHistoryFor() as unknown as { admissions: Record<string, unknown>[] }
    expect(accepted(recordedHistoryFor(RUN, reserved.admissions, { sequence: queuedHeaderFor({ recordedState: 'WaitingForAdmission' }) }))).toBeNull()
    const stopped = stoppedWhileWaitingHistoryFor() as unknown as { admissions: Record<string, unknown>[] }
    expect(accepted(recordedHistoryFor(RUN, stopped.admissions, { sequence: queuedHeaderFor({ recordedState: 'ReadyForHumanApproval', reasonCode: 'review_approved', endedAtUtc: '2026-10-20T09:00:40.0000000Z' }) }))).toBeNull()
  })

  it('accepts the largest legitimate ledger and refuses one member more', () => {
    const consumed = (ordinal: number) => ({
      ...admissionFor(2, { kind: 'Verification', recipeOrdinal: ordinal % 32, recipeName: `r${ordinal}`, passNumber: 1 + Math.floor(ordinal / 32) }),
      admissionId: admissionIdFor(ordinal),
      admissionNumber: String(100 + ordinal),
      ordinal,
      recordedState: 'Consumed',
      waitingReason: 'AwaitingAdmissionCheck',
      revision: '3',
      stepId: stepIdFor(ordinal),
    })
    const build = (count: number) => [admissionFor(0), ...Array.from({ length: count - 1 }, (_, position) => consumed(position + 1))]
    const header = queuedHeaderFor({ recordedState: 'AwaitingStepResult' })
    expect(accepted(recordedHistoryFor(RUN, build(96), { sequence: header }))?.status).toBe('Recorded')
    expect(accepted(recordedHistoryFor(RUN, build(103), { sequence: header }))).toBeNull()
  })
})
