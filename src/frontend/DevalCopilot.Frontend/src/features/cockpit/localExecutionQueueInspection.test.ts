import { describe, expect, it } from 'vitest'
import { queuedHeaderFor } from './localExecutionAdmissionHistoryFixture'
import { isSameQueueInspectionEntry, toLocalExecutionQueueInspection } from './localExecutionQueueInspection'
import type { LocalExecutionQueueInspectionEntry } from './localExecutionQueueInspection'
import {
  emptyQueueFor,
  guidOf,
  recordedQueueFor,
  reservedEntryFor,
  unavailableQueueFor,
  unresolvedEntryFor,
  waitingEntryFor,
} from './localExecutionQueueInspectionFixture'

const accepted = (raw: unknown) => toLocalExecutionQueueInspection(raw)
const entriesOf = (raw: unknown): readonly LocalExecutionQueueInspectionEntry[] => {
  const decoded = accepted(raw)
  if (decoded?.status !== 'Recorded') {
    throw new Error('expected a recorded answer')
  }

  return decoded.entries
}
const recorded = (...entries: Record<string, unknown>[]) => recordedQueueFor(entries)

describe('toLocalExecutionQueueInspection', () => {
  it('accepts the closed Empty and Unavailable answers only with an empty list', () => {
    expect(accepted(emptyQueueFor())).toEqual({ status: 'Empty' })
    expect(accepted(unavailableQueueFor())).toEqual({ status: 'Unavailable' })
    expect(accepted({ status: 'Empty', entries: [waitingEntryFor(1)] })).toBeNull()
    expect(accepted({ status: 'Unavailable', entries: [waitingEntryFor(1)] })).toBeNull()
  })

  it('accepts a recorded answer with Waiting, Reserved and an ended Unresolved admission in recorded order', () => {
    const entries = entriesOf(recorded(waitingEntryFor(1), reservedEntryFor(2), unresolvedEntryFor(3)))

    expect(entries.map((entry) => entry.admission.recordedState)).toEqual(['Waiting', 'Reserved', 'Unresolved'])
    expect(entries.map((entry) => entry.sequence.recordedState)).toEqual(['WaitingForAdmission', 'PreparingStep', 'NeedsAttention'])
    expect(entries[0]).toMatchObject({ projectName: 'Project 1', admission: { admissionNumber: '101', stepId: null } })
  })

  it('refuses a missing, unknown or extra member at every level', () => {
    const entry = waitingEntryFor(1)
    expect(accepted({ status: 'Recorded' })).toBeNull()
    expect(accepted({ entries: [] })).toBeNull()
    expect(accepted({ ...emptyQueueFor(), extra: 1 })).toBeNull()
    expect(accepted(recordedQueueFor([entry], { extra: 1 }))).toBeNull()
    expect(accepted(recordedQueueFor([{ ...entry, extra: 1 }]))).toBeNull()
    const { projectName: _removed, ...withoutName } = entry
    expect(accepted(recordedQueueFor([withoutName]))).toBeNull()
    expect(accepted(recordedQueueFor([{ ...entry, sequence: { ...entry.sequence, extra: 1 } }]))).toBeNull()
    expect(accepted(recordedQueueFor([{ ...entry, admission: { ...entry.admission, extra: 1 } }]))).toBeNull()
    const { stepId: _step, ...withoutStep } = entry.admission
    expect(accepted(recordedQueueFor([{ ...entry, admission: withoutStep }]))).toBeNull()
  })

  it.each([undefined, null, 'Recorded', 7, [], { status: 'Recorded', entries: {} }, { status: 'Running', entries: [] }, { status: 'recorded', entries: [] }])(
    'refuses %j as a whole answer',
    (raw) => {
      expect(accepted(raw)).toBeNull()
    },
  )

  it('refuses a recorded answer with no entry: Recorded is never empty', () => {
    expect(accepted(recordedQueueFor([]))).toBeNull()
  })

  it('refuses an outstanding admission that is not Waiting, Reserved or Unresolved', () => {
    for (const recordedState of ['Consumed', 'Retired', 'waiting', 'Bogus', 1, null]) {
      const entry = waitingEntryFor(1)
      expect(accepted(recorded({ ...entry, admission: { ...entry.admission, recordedState } }))).toBeNull()
    }
  })

  it('refuses an admission whose state its sequence cannot hold', () => {
    const waiting = waitingEntryFor(1)
    const reserved = reservedEntryFor(1)
    const unresolved = unresolvedEntryFor(1)
    const cases = [
      { ...waiting, sequence: reserved.sequence },
      { ...waiting, sequence: unresolved.sequence },
      { ...reserved, sequence: waiting.sequence },
      { ...reserved, sequence: unresolved.sequence },
      { ...unresolved, sequence: waiting.sequence },
      { ...unresolved, sequence: reserved.sequence },
      { ...unresolved, sequence: queuedHeaderFor({ recordedState: 'AwaitingStepResult' }) },
      { ...waiting, sequence: queuedHeaderFor({ recordedState: 'Armed' }) },
    ]

    for (const entry of cases) {
      expect(accepted(recorded(entry))).toBeNull()
    }

    for (const ended of ['Stopped', 'Interrupted'] as const) {
      const sequence = { ...unresolved.sequence, recordedState: ended }
      expect(accepted(recorded({ ...unresolved, sequence }))).not.toBeNull()
    }
  })

  it('refuses the link shape of each state: a Waiting step, a Reserved or Unresolved admission without one', () => {
    const waiting = waitingEntryFor(1)
    const reserved = reservedEntryFor(1)
    const unresolved = unresolvedEntryFor(1)

    expect(accepted(recorded({ ...waiting, admission: { ...waiting.admission, stepId: guidOf('t', 1) } }))).toBeNull()
    expect(accepted(recorded({ ...reserved, admission: { ...reserved.admission, stepId: null } }))).toBeNull()
    expect(accepted(recorded({ ...unresolved, admission: { ...unresolved.admission, stepId: null } }))).toBeNull()
    expect(accepted(recorded({ ...unresolved, admission: { ...unresolved.admission, reasonCode: null } }))).toBeNull()
    expect(accepted(recorded({ ...reserved, admission: { ...reserved.admission, reasonCode: 'preparation_unresolved' } }))).toBeNull()
  })

  it('refuses a project or run that is not a canonical lowercase identity, or a project name that is not text of one to 200 units', () => {
    const entry = waitingEntryFor(1)
    for (const patch of [
      { projectId: 'not-a-guid' },
      { projectId: '00000000-0000-0000-0000-000000000000' },
      { projectId: entry.projectId.toUpperCase() },
      { runId: 7 },
      { runId: null },
      { runId: '00000000-0000-0000-0000-000000000000' },
      { projectName: '' },
      { projectName: '   ' },
      { projectName: 'x'.repeat(201) },
      { projectName: 7 },
      { projectName: null },
    ]) {
      expect(accepted(recorded({ ...entry, ...patch }))).toBeNull()
    }

    expect(accepted(recorded({ ...entry, projectName: 'x'.repeat(200) }))).not.toBeNull()
    expect(accepted(recorded({ ...entry, projectName: '<b>not markup</b> & "quoted"' }))).not.toBeNull()
  })

  it('refuses a number that is not a canonical positive Int64 text, an order that is not strictly increasing and any repeat', () => {
    const first = waitingEntryFor(1)
    const second = waitingEntryFor(2)
    const withNumber = (entry: ReturnType<typeof waitingEntryFor>, admissionNumber: unknown) => ({
      ...entry,
      admission: { ...entry.admission, admissionNumber },
    })

    for (const bad of [101, '0', '-1', '01', '1.5', '9223372036854775808', '', ' 1']) {
      expect(accepted(recorded(withNumber(first, bad)))).toBeNull()
    }

    expect(accepted(recorded(second, first))).toBeNull()
    expect(accepted(recorded(first, withNumber(second, '101')))).toBeNull()
    expect(accepted(recorded(first, { ...second, admission: { ...second.admission, admissionId: first.admission.admissionId } }))).toBeNull()
    expect(accepted(recorded(first, { ...second, sequence: first.sequence }))).toBeNull()
    expect(accepted(recorded(first, { ...second, runId: first.runId }))).toBeNull()
    expect(accepted(recorded(first, withNumber(second, '9223372036854775807')))).not.toBeNull()
  })

  it('accepts exactly the largest coherent answer and refuses any more: 64 live cycles and four held reservations of ended ones', () => {
    const live = Array.from({ length: 64 }, (_, index) => waitingEntryFor(index + 1))
    const held = Array.from({ length: 4 }, (_, index) => unresolvedEntryFor(index + 65))

    expect(entriesOf(recorded(...live, ...held))).toHaveLength(68)
    expect(accepted(recorded(...live, ...held, unresolvedEntryFor(69)))).toBeNull()
    expect(accepted(recorded(...live, waitingEntryFor(65)))).toBeNull()
    expect(accepted(recorded(...live.slice(0, 63), ...held.slice(0, 3)))).not.toBeNull()
  })

  it('counts a reserved admission on a live sequence as both live and held', () => {
    const waiting = Array.from({ length: 60 }, (_, index) => waitingEntryFor(index + 1))
    const reserved = Array.from({ length: 4 }, (_, index) => reservedEntryFor(index + 61))

    expect(entriesOf(recorded(...waiting, ...reserved))).toHaveLength(64)
    expect(accepted(recorded(...waiting, ...reserved, reservedEntryFor(65)))).toBeNull()
    expect(accepted(recorded(...waiting.slice(0, 59), ...reserved, reservedEntryFor(65)))).toBeNull()
    expect(accepted(recorded(...waiting.slice(0, 59), ...reserved, unresolvedEntryFor(65)))).toBeNull()
    expect(accepted(recorded(...waiting, ...reserved.slice(0, 2), unresolvedEntryFor(65), unresolvedEntryFor(66)))).not.toBeNull()
  })

  it('does not coerce: a numeric revision, a numeric ordinal as text or a date that is not a UTC round-trip is refused', () => {
    const entry = waitingEntryFor(1)
    expect(accepted(recorded({ ...entry, sequence: { ...entry.sequence, sequenceRevision: 7 } }))).toBeNull()
    expect(accepted(recorded({ ...entry, admission: { ...entry.admission, revision: 2 } }))).toBeNull()
    expect(accepted(recorded({ ...entry, admission: { ...entry.admission, ordinal: '0' } }))).toBeNull()
    expect(accepted(recorded({ ...entry, admission: { ...entry.admission, enqueuedAtUtc: '2026-10-20T09:00:00+02:00' } }))).toBeNull()
    expect(accepted(recorded({ ...entry, sequence: { ...entry.sequence, armedAtUtc: '2026-10-20' } }))).toBeNull()
  })

  it('treats the same recorded outstanding admission as one entry only when identity, sequence, run and project all agree', () => {
    const [entry] = entriesOf(recorded(waitingEntryFor(1)))
    const [other] = entriesOf(recorded(waitingEntryFor(2)))

    expect(isSameQueueInspectionEntry(entry, { ...entry })).toBe(true)
    expect(isSameQueueInspectionEntry(entry, other)).toBe(false)
    expect(isSameQueueInspectionEntry(entry, { ...entry, runId: other.runId })).toBe(false)
    expect(isSameQueueInspectionEntry(entry, { ...entry, projectId: other.projectId })).toBe(false)
    expect(isSameQueueInspectionEntry(entry, { ...entry, sequence: other.sequence })).toBe(false)
    expect(isSameQueueInspectionEntry(entry, { ...entry, admission: { ...entry.admission, admissionNumber: '999' } })).toBe(false)
    expect(isSameQueueInspectionEntry(entry, { ...entry, admission: { ...entry.admission, admissionId: other.admission.admissionId } })).toBe(false)
  })
})
