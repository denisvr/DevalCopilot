import { admissionFor, guidOf, queuedHeaderFor, stepIdFor } from './localExecutionAdmissionHistoryFixture'

/** Test-only builders of the parsed outstanding queue-admission answers the host writes (ADR-0064); every field can be replaced to build a contradiction. */
export { guidOf }

const instant = (second: number) => `2026-10-20T09:00:${String(second).padStart(2, '0')}.0000000Z`

export const projectIdFor = (index: number) => guidOf('j', index)
export const runIdFor = (index: number) => guidOf('r', index)
export const sequenceIdFor = (index: number) => guidOf('s', index)

/** One Waiting entry: the implementation admission of a queued cycle that has not been reserved, with its own project, run and sequence. */
export const waitingEntryFor = (index: number, patch: Record<string, unknown> = {}) => ({
  projectId: projectIdFor(index),
  projectName: `Project ${index}`,
  runId: runIdFor(index),
  sequence: queuedHeaderFor({ sequenceId: sequenceIdFor(index) }),
  admission: admissionFor(0, {
    admissionId: guidOf('a', index),
    admissionNumber: String(100 + index),
    recordedState: 'Waiting',
    waitingReason: 'WaitingForWorkspace',
    stepId: null,
    revision: '2',
  }),
  ...patch,
})

/** One Reserved entry of a sequence that is preparing its step. */
export const reservedEntryFor = (index: number, patch: Record<string, unknown> = {}) => ({
  ...waitingEntryFor(index),
  sequence: queuedHeaderFor({ sequenceId: sequenceIdFor(index), recordedState: 'PreparingStep' }),
  admission: admissionFor(0, {
    admissionId: guidOf('a', index),
    admissionNumber: String(100 + index),
    recordedState: 'Reserved',
    stepId: stepIdFor(index),
    revision: '2',
  }),
  ...patch,
})

/** One Unresolved reservation retained on an ended sequence. */
export const unresolvedEntryFor = (index: number, patch: Record<string, unknown> = {}) => ({
  ...waitingEntryFor(index),
  sequence: queuedHeaderFor({
    sequenceId: sequenceIdFor(index),
    recordedState: 'NeedsAttention',
    reasonCode: 'preparation_unresolved',
    endedAtUtc: instant(40),
    updatedAtUtc: instant(40),
  }),
  admission: admissionFor(0, {
    admissionId: guidOf('a', index),
    admissionNumber: String(100 + index),
    recordedState: 'Unresolved',
    reasonCode: 'preparation_unresolved',
    stepId: stepIdFor(index),
    revision: '2',
  }),
  ...patch,
})

export const recordedQueueFor = (entries: readonly Record<string, unknown>[] = [waitingEntryFor(1)], patch: Record<string, unknown> = {}) => ({
  status: 'Recorded',
  entries,
  ...patch,
})

export const emptyQueueFor = () => ({ status: 'Empty', entries: [] })
export const unavailableQueueFor = () => ({ status: 'Unavailable', entries: [] })
