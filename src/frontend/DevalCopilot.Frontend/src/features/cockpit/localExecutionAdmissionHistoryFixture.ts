import { CHECKPOINT, FINGERPRINT, RUN, SEQUENCE, guidOf, operationIdFor, stepIdFor } from './localExecutionInspectionFixture'

/** Test-only builders of the parsed recorded admission-ledger answers the host writes (ADR-0063); every field can be replaced to build a contradiction. */
export { RUN, SEQUENCE, CHECKPOINT, FINGERPRINT, guidOf, operationIdFor, stepIdFor }

const instant = (second: number) => `2026-10-20T09:00:${String(second).padStart(2, '0')}.0000000Z`

export const admissionIdFor = (ordinal: number) => guidOf('a', ordinal)

/**
 * One recorded admission member. The default ledger of a queued cycle in progress: ordinal 0 the implementation and ordinal 1 a
 * verification, both Consumed with their linked steps, and ordinal 2 a code review that is still Waiting with no step.
 */
export const admissionFor = (ordinal: number, patch: Record<string, unknown> = {}) => {
  const kinds = ['Implementation', 'Verification', 'CodeReview']
  const kind = kinds[ordinal] ?? 'CodeReview'
  const verification = kind === 'Verification'
  const waiting = ordinal >= 2
  return {
    admissionId: admissionIdFor(ordinal),
    admissionNumber: String(11 + ordinal * 2),
    ordinal,
    kind,
    passNumber: ordinal === 0 ? 0 : 1,
    recipeOrdinal: verification ? 0 : null,
    recipeName: verification ? 'Unit <b>tests</b>' : null,
    recordedState: waiting ? 'Waiting' : 'Consumed',
    waitingReason: waiting ? 'WaitingForWorkspace' : 'AwaitingAdmissionCheck',
    waitingReasonRecordedAtUtc: instant(ordinal * 10 + 1),
    enqueuedAtUtc: instant(ordinal * 10),
    updatedAtUtc: instant(ordinal * 10 + 5),
    revision: waiting ? '2' : '3',
    reasonCode: null,
    stepId: waiting ? null : stepIdFor(ordinal),
    ...patch,
  }
}

export const queuedHeaderFor = (patch: Record<string, unknown> = {}) => ({
  sequenceId: SEQUENCE,
  admissionMode: 'WaitForAdmission',
  recordedState: 'WaitingForAdmission',
  reasonCode: null,
  sequenceRevision: '7',
  armedAtUtc: instant(0),
  advancementDeadlineUtc: '2026-10-20T11:00:00.0000000Z',
  updatedAtUtc: instant(30),
  endedAtUtc: null,
  ...patch,
})

export const recordedHistoryFor = (
  runId = RUN,
  admissions: readonly Record<string, unknown>[] = [admissionFor(0), admissionFor(1), admissionFor(2)],
  patch: Record<string, unknown> = {},
) => ({
  runId,
  status: 'Recorded',
  sequence: queuedHeaderFor(),
  admissions,
  ...patch,
})

export const notRecordedHistoryFor = (runId = RUN) => ({ runId, status: 'NotRecorded', sequence: null, admissions: [] })
export const notQueuedHistoryFor = (runId = RUN) => ({ runId, status: 'NotQueued', sequence: null, admissions: [] })
export const unavailableHistoryFor = (runId = RUN) => ({ runId, status: 'Unavailable', sequence: null, admissions: [] })

/** A finished cycle whose last wait retired without a step (a Stop while waiting): two consumed admissions and one retired with no step. */
export const stoppedWhileWaitingHistoryFor = (runId = RUN) =>
  recordedHistoryFor(
    runId,
    [
      admissionFor(0),
      admissionFor(1),
      admissionFor(2, { recordedState: 'Retired', reasonCode: 'stopped_before_claim', revision: '3', stepId: null }),
    ],
    {
      sequence: queuedHeaderFor({
        recordedState: 'Stopped',
        reasonCode: 'stopped_before_claim',
        endedAtUtc: instant(40),
        updatedAtUtc: instant(40),
      }),
    },
  )

/** A reservation retired unused: the Retired admission links the revoked step of its reservation. */
export const retiredWithStepHistoryFor = (runId = RUN) =>
  recordedHistoryFor(
    runId,
    [
      admissionFor(0),
      admissionFor(1, { recordedState: 'Retired', reasonCode: 'preparation_expired', stepId: stepIdFor(1) }),
    ],
    {
      sequence: queuedHeaderFor({
        recordedState: 'NeedsAttention',
        reasonCode: 'preparation_expired',
        endedAtUtc: instant(40),
        updatedAtUtc: instant(40),
      }),
    },
  )

/** An unresolved reservation retained on an ended sequence. */
export const unresolvedHistoryFor = (runId = RUN) =>
  recordedHistoryFor(
    runId,
    [admissionFor(0), admissionFor(1, { recordedState: 'Unresolved', reasonCode: 'preparation_unresolved', stepId: stepIdFor(1) })],
    {
      sequence: queuedHeaderFor({
        recordedState: 'NeedsAttention',
        reasonCode: 'preparation_unresolved',
        endedAtUtc: instant(40),
        updatedAtUtc: instant(40),
      }),
    },
  )

/** A reserved last admission of a sequence that is preparing its step. */
export const reservedHistoryFor = (runId = RUN) =>
  recordedHistoryFor(runId, [admissionFor(0), admissionFor(1, { recordedState: 'Reserved', stepId: stepIdFor(1) })], {
    sequence: queuedHeaderFor({ recordedState: 'PreparingStep' }),
  })
