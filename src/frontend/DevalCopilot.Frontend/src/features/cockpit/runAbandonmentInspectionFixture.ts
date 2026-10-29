/**
 * Test-only builders of the parsed recorded-abandonment answers the host writes (ADR-0071). They write exactly the members the contract
 * names with explicit nulls, and every member can be replaced through a patch to build a contradiction. The defaults describe a Recorded
 * abandonment of the standard run.
 */
type Raw = Record<string, unknown>

const guid = (letter: string, index: number): string => `${letter.repeat(8)}-0000-4000-8000-${index.toString(16).padStart(12, '0')}`

export const ABANDONMENT_RUN = guid('a', 1)
export const ABANDONMENT_OTHER_RUN = guid('a', 2)
export const ABANDONMENT_PROJECT = guid('b', 1)
export const ABANDONMENT_OTHER_PROJECT = guid('b', 2)
export const ABANDONMENT_EVENT = guid('c', 1)
export const ABANDONMENT_REASON = 'The objective was replaced by a different approach.'
export const ABANDONMENT_AT = '2026-10-08T12:00:00.1234567Z'
export const ABANDONMENT_SEQUENCE = '42'
export const ABANDONMENT_TARGET = { runId: ABANDONMENT_RUN, projectId: ABANDONMENT_PROJECT }

export const runAbandonmentRecordedFor = (patch: Raw = {}): Raw => ({
  projectId: ABANDONMENT_PROJECT,
  reason: ABANDONMENT_REASON,
  abandonedAtUtc: ABANDONMENT_AT,
  eventId: ABANDONMENT_EVENT,
  eventSequence: ABANDONMENT_SEQUENCE,
  ...patch,
})

export const recordedRunAbandonmentInspectionFor = (patch: Raw = {}, abandonment: Raw = {}): Raw => ({
  runId: ABANDONMENT_RUN,
  status: 'Recorded',
  abandonment: runAbandonmentRecordedFor(abandonment),
  ...patch,
})

export const notRecordedRunAbandonmentInspectionFor = (patch: Raw = {}): Raw => ({
  runId: ABANDONMENT_RUN,
  status: 'NotRecorded',
  abandonment: null,
  ...patch,
})

export const unavailableRunAbandonmentInspectionFor = (patch: Raw = {}): Raw => ({
  runId: ABANDONMENT_RUN,
  status: 'Unavailable',
  abandonment: null,
  ...patch,
})
