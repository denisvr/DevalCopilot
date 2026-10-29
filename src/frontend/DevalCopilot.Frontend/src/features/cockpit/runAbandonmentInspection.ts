import { hasExactKeys, isUtcRoundTrip } from './agentAttemptVerification'

// The closed, whole-answer decoder of the recorded abandonment of one run (ADR-0071). It accepts a parsed host answer only when all of
// it is coherent for exactly the run that was asked about and the project of the selected history entry: exactly the documented members
// at every level, a closed status vocabulary, null shapes per status, canonical lowercase identifiers, an ADR-0031 canonical reason, a
// canonical seven-digit UTC instant (the default instant included, like every supported instant) and a canonical positive decimal
// Int64 text. Anything else is null and is never repaired, coerced, clamped or partially accepted. The recorded abandonment is
// unsigned relational consistency: it proves no cancellation, workspace repair, ownership release, current intake permission or
// publication.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const POSITIVE_DECIMAL = /^[1-9][0-9]*$/
const INT64_MAX = 9223372036854775807n
const MAXIMUM_REASON_UTF8_BYTES = 2 * 1024
// A scalar the reason may not hold (ADR-0031): a control character, a format character or a line or paragraph separator.
const FORBIDDEN_SCALAR = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u
// What .NET trims at the ends of a text: JavaScript's whitespace plus U+0085 (a control character, refused anyway).
const UNTRIMMED_EDGE = /^[\s\p{Cc}]|[\s\p{Cc}]$/u
const LINE_FEED = 0x0a

export const RUN_ABANDONMENT_INSPECTION_STATUSES = ['NotRecorded', 'Unavailable', 'Recorded'] as const
export type RunAbandonmentInspectionStatus = (typeof RUN_ABANDONMENT_INSPECTION_STATUSES)[number]

export interface RunAbandonmentInspectionRecorded {
  projectId: string
  /** The exact canonical reason, literal text; LF may separate lines. */
  reason: string
  /** The validated canonical UTC round-trip text of the recorded instant. */
  abandonedAtUtc: string
  /** The one supporting run.abandoned event. */
  eventId: string
  /** The canonical positive decimal Int64 text of the event's sequence; never a JavaScript number. */
  eventSequence: string
}

export type RunAbandonmentInspectionAnswer =
  | { status: 'NotRecorded' | 'Unavailable'; runId: string }
  | { status: 'Recorded'; runId: string; abandonment: RunAbandonmentInspectionRecorded }

/** What an answer must belong to: the one run the read was made for and the project of the selected entry. Frozen when the read begins. */
export interface RunAbandonmentInspectionTarget {
  runId: string
  projectId: string
}

const ANSWER_KEYS = ['abandonment', 'runId', 'status']
const RECORDED_KEYS = ['abandonedAtUtc', 'eventId', 'eventSequence', 'projectId', 'reason']

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== NIL_GUID
const isOneOf = <T extends string>(values: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (values as readonly string[]).includes(value)

/** Whether `value` is exactly its own ADR-0031 normalization: nonblank, untrimmed, well-formed, only LF as a line break, at most 2 KiB of UTF-8. */
export function isCanonicalAbandonmentReason(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || UNTRIMMED_EDGE.test(value)) {
    return false
  }

  for (const character of value) {
    const scalar = character.codePointAt(0) as number
    // An unpaired surrogate is not a Unicode scalar value at all.
    if (scalar >= 0xd800 && scalar <= 0xdfff) {
      return false
    }

    if (scalar !== LINE_FEED && FORBIDDEN_SCALAR.test(character)) {
      return false
    }
  }

  return new TextEncoder().encode(value).length <= MAXIMUM_REASON_UTF8_BYTES
}

/** A canonical positive decimal Int64 text: digits only, no sign, no leading zero, 1 to 9223372036854775807. Compared as a BigInt, never a number. */
export const isCanonicalEventSequence = (value: unknown): value is string =>
  typeof value === 'string' && POSITIVE_DECIMAL.test(value) && BigInt(value) <= INT64_MAX

function toRecorded(raw: unknown, target: RunAbandonmentInspectionTarget): RunAbandonmentInspectionRecorded | null {
  if (!hasExactKeys(raw, RECORDED_KEYS)) {
    return null
  }

  if (
    !isGuid(raw.projectId)
    || raw.projectId !== target.projectId
    || !isCanonicalAbandonmentReason(raw.reason)
    || !isUtcRoundTrip(raw.abandonedAtUtc)
    || !isGuid(raw.eventId)
    || !isCanonicalEventSequence(raw.eventSequence)
  ) {
    return null
  }

  return {
    projectId: raw.projectId,
    reason: raw.reason,
    abandonedAtUtc: raw.abandonedAtUtc,
    eventId: raw.eventId,
    eventSequence: raw.eventSequence,
  }
}

/**
 * Accepts a parsed recorded-abandonment answer only when the whole of it is coherent for exactly `target`: the closed graph has
 * exactly its three members, an abandonment only for Recorded, and a Recorded payload passes every bound. Otherwise null.
 */
export function toRunAbandonmentInspection(raw: unknown, target: RunAbandonmentInspectionTarget): RunAbandonmentInspectionAnswer | null {
  if (!hasExactKeys(raw, ANSWER_KEYS) || !isGuid(raw.runId) || raw.runId !== target.runId || !isOneOf(RUN_ABANDONMENT_INSPECTION_STATUSES, raw.status)) {
    return null
  }

  const { runId } = target
  if (raw.status !== 'Recorded') {
    return raw.abandonment === null ? { status: raw.status, runId } : null
  }

  const abandonment = toRecorded(raw.abandonment, target)
  return abandonment === null ? null : { status: 'Recorded', runId, abandonment }
}
