import { describe, expect, it } from 'vitest'
import {
  LOCAL_EXECUTION_WAITING_REASONS,
  toLocalExecutionArmReceipt,
  toLocalExecutionSequenceAvailability,
  toLocalExecutionSequenceStatus,
} from './localExecutionSequence'
import {
  LOCAL_PLAN_ID,
  LOCAL_QUEUE_CONFIRMATION,
  LOCAL_QUEUE_TUPLE,
  LOCAL_TUPLE,
  localArmReceiptFor,
  localAvailabilityFor,
  localQueueAvailabilityFor,
  localQueueFor,
  localQueueReceiptFor,
  localQueuedStatusFor,
  localStatusFor,
} from './localExecutionSequenceFixture'

const RUN = 'run-1'
type Raw = Record<string, unknown>
const queuedStatus = (state: Parameters<typeof localQueuedStatusFor>[1], patch: Raw = {}, sequencePatch: Raw = {}) =>
  toLocalExecutionSequenceStatus(localQueuedStatusFor(RUN, state, patch, sequencePatch), RUN)

describe('queued sequence status (ADR-0062)', () => {
  it('accepts a WaitingForAdmission cycle with exactly its one Waiting admission and no steps', () => {
    const status = queuedStatus('WaitingForAdmission')
    expect(status?.sequence?.admissionMode).toBe('WaitForAdmission')
    expect(status?.sequence?.queue?.admissionState).toBe('Waiting')
    expect(status?.sequence?.steps).toHaveLength(0)
  })

  it('accepts a PreparingStep queued cycle only with its Reserved admission', () => {
    expect(queuedStatus('PreparingStep')?.sequence?.queue?.admissionState).toBe('Reserved')
    expect(queuedStatus('PreparingStep', {}, { queue: null })).toBeNull()
    expect(queuedStatus('PreparingStep', {}, { queue: localQueueFor() })).toBeNull()
  })

  it('refuses a waiting cycle without a disclosure or with a non-Waiting one', () => {
    expect(queuedStatus('WaitingForAdmission', {}, { queue: null })).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ admissionState: 'Reserved' }) })).toBeNull()
  })

  it('refuses an Immediate sequence that is waiting or carries a queue disclosure', () => {
    expect(toLocalExecutionSequenceStatus(localStatusFor(RUN, 'WaitingForAdmission', {}, { admissionMode: 'Immediate' }), RUN)).toBeNull()
    expect(toLocalExecutionSequenceStatus(localStatusFor(RUN, 'Armed', {}, { queue: localQueueFor() }), RUN)).toBeNull()
  })

  it('refuses a queued sequence recorded as Armed and an unknown admission mode', () => {
    expect(queuedStatus('Armed')).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { admissionMode: 'Later' })).toBeNull()
  })

  it.each(LOCAL_EXECUTION_WAITING_REASONS)('accepts the fixed waiting reason %s', (waitingReason) => {
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ waitingReason }) })).not.toBeNull()
  })

  it('refuses an unknown waiting reason, a non-canonical admission number and extra or missing disclosure members', () => {
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ waitingReason: 'WaitingForLuck' }) })).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ admissionNumber: '01' }) })).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ admissionNumber: 1 }) })).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: localQueueFor({ position: '2' }) })).toBeNull()
    const { waitingReasonText: _omitted, ...missing } = localQueueFor()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: missing })).toBeNull()
  })

  it('refuses a disclosure whose observation precedes the enqueue or whose enqueue precedes arming', () => {
    const base = localQueueFor()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: { ...base, waitingReasonRecordedAtUtc: '2000-01-01T00:00:00.0000000+00:00' } })).toBeNull()
    expect(queuedStatus('WaitingForAdmission', {}, { queue: { ...base, enqueuedAtUtc: '2000-01-01T00:00:00.0000000+00:00' } })).toBeNull()
  })
})

describe('queue availability and receipt (ADR-0062)', () => {
  it('accepts the queue availability only as the queue mode with the separate queue confirmation', () => {
    const raw = localQueueAvailabilityFor(RUN)
    expect(toLocalExecutionSequenceAvailability(raw, RUN, LOCAL_PLAN_ID, 'WaitForAdmission')?.candidate?.confirmationSha256).toBe(LOCAL_QUEUE_CONFIRMATION)
    expect(toLocalExecutionSequenceAvailability(raw, RUN, LOCAL_PLAN_ID, 'Immediate')).toBeNull()
    expect(toLocalExecutionSequenceAvailability(localAvailabilityFor(RUN), RUN, LOCAL_PLAN_ID, 'WaitForAdmission')).toBeNull()
  })

  it('accepts a first queue receipt as WaitingForAdmission at revision 1 with its Waiting admission, never as Armed', () => {
    const receipt = toLocalExecutionArmReceipt(localQueueReceiptFor(RUN), RUN, LOCAL_QUEUE_TUPLE, 'WaitForAdmission')
    expect(receipt?.state).toBe('WaitingForAdmission')
    expect(receipt?.queue?.admissionState).toBe('Waiting')
    expect(toLocalExecutionArmReceipt(localQueueReceiptFor(RUN, { state: 'Armed' }), RUN, LOCAL_QUEUE_TUPLE, 'WaitForAdmission')).toBeNull()
    expect(toLocalExecutionArmReceipt(localQueueReceiptFor(RUN, { queue: null }), RUN, LOCAL_QUEUE_TUPLE, 'WaitForAdmission')).toBeNull()
  })

  it('refuses a receipt of the other mode in both directions and an Immediate receipt that carries a queue', () => {
    expect(toLocalExecutionArmReceipt(localQueueReceiptFor(RUN), RUN, LOCAL_QUEUE_TUPLE, 'Immediate')).toBeNull()
    expect(toLocalExecutionArmReceipt(localArmReceiptFor(RUN), RUN, LOCAL_TUPLE, 'WaitForAdmission')).toBeNull()
    expect(toLocalExecutionArmReceipt(localArmReceiptFor(RUN, { queue: localQueueFor() }), RUN, LOCAL_TUPLE, 'Immediate')).toBeNull()
  })

  it('refuses a queue receipt whose confirmation is not the tuple the Human saw', () => {
    expect(toLocalExecutionArmReceipt(localQueueReceiptFor(RUN), RUN, LOCAL_TUPLE, 'WaitForAdmission')).toBeNull()
  })
})
