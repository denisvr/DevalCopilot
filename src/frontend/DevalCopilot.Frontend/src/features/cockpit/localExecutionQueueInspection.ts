import { hasExactKeys } from './agentAttemptVerification'
import { toAdmission, toHeader } from './localExecutionAdmissionHistory'
import type { LocalExecutionAdmissionHistoryHeader, LocalExecutionAdmissionRecord } from './localExecutionAdmissionHistory'
import { isLocalExecutionSequenceLive } from './localExecutionSequence'

// The closed, whole-answer decoder of the recorded outstanding queue admissions (ADR-0064). It accepts a parsed host answer only when all
// of it is coherent: exactly status and entries, a closed status, and for Recorded one through 68 entries in strictly increasing recorded
// admission order, each with exactly the registered project label, the run, the stored sequence header and the one admission that is the
// proved last admission of that sequence (the ADR-0063 shapes, decoded by the same functions). The admission is outstanding (Waiting,
// Reserved or Unresolved) in a state its sequence allows; no sequence or run repeats; at most 64 sequences are still live and at most
// four admissions are held. Anything else is null and is never repaired, trimmed, coerced into a known state or partially accepted.
// An admission, step link, reservation or waiting reason is bookkeeping: never an operation, a process, a current eligibility, a rank,
// free capacity, a promised start or proof of any outcome. Empty is not idle.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const MAX_PROJECT_NAME_UNITS = 200

/** The most coherent entries: 64 live queued cycles plus four held reservations of ended ones. */
export const LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_ENTRIES = 68
export const LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_LIVE = 64
export const LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_HELD = 4

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isProjectName = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_PROJECT_NAME_UNITS

export const LOCAL_EXECUTION_QUEUE_INSPECTION_STATUSES = ['Empty', 'Recorded', 'Unavailable'] as const
export type LocalExecutionQueueInspectionStatus = (typeof LOCAL_EXECUTION_QUEUE_INSPECTION_STATUSES)[number]

/** One proved outstanding admission. `projectName` is untrusted descriptive text, rendered inert, never pinned authority. */
export interface LocalExecutionQueueInspectionEntry {
  projectId: string
  projectName: string
  runId: string
  sequence: LocalExecutionAdmissionHistoryHeader
  admission: LocalExecutionAdmissionRecord
}

export type LocalExecutionQueueInspection =
  // No Waiting, Reserved or Unresolved admission is recorded. This is not "no active cycle", "no owner" or "free capacity".
  | { status: 'Empty' }
  // The reading could not be proved as a whole; none of it is shown.
  | { status: 'Unavailable' }
  | { status: 'Recorded'; entries: readonly LocalExecutionQueueInspectionEntry[] }

const RESPONSE_KEYS = ['status', 'entries'] as const
const ENTRY_KEYS = ['projectId', 'projectName', 'runId', 'sequence', 'admission'] as const

/** Whether the outstanding admission fits the recorded state of its sequence, exactly as the host proved it. */
function fitsSequence(sequence: LocalExecutionAdmissionHistoryHeader, admission: LocalExecutionAdmissionRecord): boolean {
  switch (admission.recordedState) {
    case 'Waiting':
      return sequence.recordedState === 'WaitingForAdmission'
    case 'Reserved':
      return sequence.recordedState === 'PreparingStep'
    case 'Unresolved':
      return sequence.recordedState === 'Stopped' || sequence.recordedState === 'NeedsAttention' || sequence.recordedState === 'Interrupted'
    default:
      return false
  }
}

function toEntry(raw: unknown): LocalExecutionQueueInspectionEntry | null {
  if (!hasExactKeys(raw, ENTRY_KEYS) || !isGuid(raw.projectId) || !isProjectName(raw.projectName) || !isGuid(raw.runId)) {
    return null
  }

  const sequence = toHeader(raw.sequence)
  const admission = toAdmission(raw.admission)
  if (sequence === null || admission === null || !fitsSequence(sequence, admission)) {
    return null
  }

  return { projectId: raw.projectId, projectName: raw.projectName, runId: raw.runId, sequence, admission }
}

/**
 * The whole answer: nonempty, at most 68 entries with strictly increasing recorded admission numbers, unique admissions, sequences and
 * runs, at most 64 sequences that still hold authority and at most four held (Reserved or Unresolved) admissions.
 */
function toEntries(raw: unknown[]): LocalExecutionQueueInspectionEntry[] | null {
  if (raw.length === 0 || raw.length > LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_ENTRIES) {
    return null
  }

  const admissionIds = new Set<string>()
  const sequenceIds = new Set<string>()
  const runIds = new Set<string>()
  const entries: LocalExecutionQueueInspectionEntry[] = []
  let live = 0
  let held = 0
  for (const item of raw) {
    const entry = toEntry(item)
    const previous = entries[entries.length - 1]
    if (
      entry === null
      || admissionIds.has(entry.admission.admissionId)
      || sequenceIds.has(entry.sequence.sequenceId)
      || runIds.has(entry.runId)
      || (previous !== undefined && BigInt(entry.admission.admissionNumber) <= BigInt(previous.admission.admissionNumber))
    ) {
      return null
    }

    admissionIds.add(entry.admission.admissionId)
    sequenceIds.add(entry.sequence.sequenceId)
    runIds.add(entry.runId)
    live += isLocalExecutionSequenceLive(entry.sequence.recordedState) ? 1 : 0
    held += entry.admission.recordedState === 'Waiting' ? 0 : 1
    if (live > LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_LIVE || held > LOCAL_EXECUTION_QUEUE_INSPECTION_MAXIMUM_HELD) {
      return null
    }

    entries.push(entry)
  }

  return entries
}

/**
 * Accepts a parsed outstanding-admission answer only when the whole of it is coherent: closed shapes at every level and a closed status
 * (`Empty` and `Unavailable` carry an empty list; `Recorded` carries the complete nonempty list). Anything else is null and is never
 * repaired, trimmed or partially accepted.
 */
export function toLocalExecutionQueueInspection(raw: unknown): LocalExecutionQueueInspection | null {
  if (!hasExactKeys(raw, RESPONSE_KEYS) || !Array.isArray(raw.entries)) {
    return null
  }

  const { status, entries } = raw
  if (status === 'Empty' || status === 'Unavailable') {
    return entries.length === 0 ? { status } : null
  }

  if (status !== 'Recorded') {
    return null
  }

  const accepted = toEntries(entries)
  return accepted === null ? null : { status, entries: accepted }
}

/** Whether two entries are the same recorded outstanding admission: its identity, sequence, run and project all agree. */
export const isSameQueueInspectionEntry = (a: LocalExecutionQueueInspectionEntry, b: LocalExecutionQueueInspectionEntry): boolean =>
  a.admission.admissionId === b.admission.admissionId
  && a.admission.admissionNumber === b.admission.admissionNumber
  && a.sequence.sequenceId === b.sequence.sequenceId
  && a.runId === b.runId
  && a.projectId === b.projectId
