import { GetAgentAttemptEvidenceEndpointClient } from '../../api/generated/api-client'
import type { AgentAttemptEvidenceResponse } from '../../api/generated/api-client'

/** The evidence answer's three wire timestamps, judged on their raw JSON value before the generated client revives them. */
export const AGENT_ATTEMPT_EVIDENCE_TIMESTAMPS = ['claimedAtUtc', 'dispatchedAtUtc', 'completedAtUtc'] as const
type EvidenceTimestamp = (typeof AGENT_ATTEMPT_EVIDENCE_TIMESTAMPS)[number]

/** Absent (missing or null), an instant computed from a valid wire text, or present but not a valid wire timestamp. */
export type RawTimestampVerdict = { status: 'absent' } | { status: 'valid'; at: Date } | { status: 'invalid' }

// The text `System.Text.Json` writes for a `DateTimeOffset`: a date, `T`, a time, up to seven fractional digits and `Z` or a
// `±HH:mm` offset. Nothing else (no lowercase separators, no omitted seconds, no bare date, no other offset form) is accepted.
const WIRE_TIMESTAMP = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:(Z)|([+-])(\d{2}):(\d{2}))$/
const MAX_OFFSET_MINUTES = 14 * 60

function utcOf(year: number, month: number, day: number, hour: number, minute: number, second: number, milliseconds: number): number {
  // `Date.UTC` maps years 0-99 to 1900-1999, so the year is set explicitly.
  const at = new Date(0)
  at.setUTCFullYear(year, month - 1, day)
  at.setUTCHours(hour, minute, second, milliseconds)
  return at.getTime()
}

// The `DateTimeOffset` range, as UTC instants.
const MIN_UTC = utcOf(1, 1, 1, 0, 0, 0, 0)
const MAX_UTC = utcOf(9999, 12, 31, 23, 59, 59, 999)

const isLeapYear = (year: number) => (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0
const daysInMonth = (year: number, month: number) => [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1]

/**
 * The instant a wire timestamp names, or null unless the text has exactly the serialized shape and every field is in range: year
 * 0001-9999, a real calendar day (leap years included), 00:00:00-23:59:59, an offset within ±14:00 and a UTC instant inside the
 * `DateTimeOffset` range. The instant is computed from the validated fields (milliseconds kept, finer digits truncated); JavaScript's
 * own date parsing, which normalizes or guesses, is never consulted.
 */
export function toWireInstant(text: string): Date | null {
  const parts = WIRE_TIMESTAMP.exec(text)
  if (parts === null) {
    return null
  }

  const [year, month, day, hour, minute, second] = parts.slice(1, 7).map(Number)
  const milliseconds = Number((parts[7] ?? '').padEnd(3, '0').slice(0, 3))
  const offsetHours = parts[8] === 'Z' ? 0 : Number(parts[10])
  const offsetMinutes = parts[8] === 'Z' ? 0 : Number(parts[11])
  const offset = (parts[9] === '-' ? -1 : 1) * (offsetHours * 60 + offsetMinutes)
  if (
    year < 1
    || month < 1
    || month > 12
    || day < 1
    || day > daysInMonth(year, month)
    || hour > 23
    || minute > 59
    || second > 59
    || offsetMinutes > 59
    || Math.abs(offset) > MAX_OFFSET_MINUTES
  ) {
    return null
  }

  const utc = utcOf(year, month, day, hour, minute, second, milliseconds) - offset * 60_000
  return utc >= MIN_UTC && utc <= MAX_UTC ? new Date(utc) : null
}

function judge(root: Record<string, unknown>, key: EvidenceTimestamp): RawTimestampVerdict {
  const value = Object.hasOwn(root, key) ? root[key] : undefined
  if (value === null || value === undefined) {
    return { status: 'absent' }
  }

  const at = typeof value === 'string' ? toWireInstant(value) : null
  return at === null ? { status: 'invalid' } : { status: 'valid', at }
}

/**
 * The verdict for each evidence timestamp in one raw response body, or null when the body is empty, not JSON or not an object (the
 * generated client then answers exactly as it always has).
 */
export function judgeRawEvidenceTimestamps(body: string): Record<EvidenceTimestamp, RawTimestampVerdict> | null {
  let root: unknown
  try {
    root = body === '' ? null : JSON.parse(body)
  } catch {
    return null
  }

  if (typeof root !== 'object' || root === null || Array.isArray(root)) {
    return null
  }

  const record = root as Record<string, unknown>
  return {
    claimedAtUtc: judge(record, 'claimedAtUtc'),
    dispatchedAtUtc: judge(record, 'dispatchedAtUtc'),
    completedAtUtc: judge(record, 'completedAtUtc'),
  }
}

/**
 * A stateless `JSON.parse` reviver that removes the three timestamp members from the parsed root before the generated model is built,
 * so no raw timestamp value reaches the generated conversion. `JSON.parse` calls a reviver with the key `''` for the root last; it uses
 * that key otherwise only for a member literally named `''`, which the generated model never reads, so nothing it does read changes.
 */
export function withoutRootTimestamps(key: string, value: unknown): unknown {
  if (key === '' && typeof value === 'object' && value !== null && !Array.isArray(value)) {
    for (const name of AGENT_ATTEMPT_EVIDENCE_TIMESTAMPS) {
      delete (value as Record<string, unknown>)[name]
    }
  }
  return value
}

/**
 * The generated evidence operation with its wire timestamps judged before, and kept away from, generated revival. The generated client
 * turns any truthy value into `new Date(value.toString())` and drops falsy ones: a number such as `1` would become a plausible date, `0`
 * or `false` would look absent, and an object whose own `toString` is not callable would throw and lose the whole answer. This facade
 * reads the successful body once and judges each raw timestamp from its own parse; the unchanged generated processing (parsing of the
 * same text, model construction, status and error handling) then runs with a reviver that removes the three timestamp members, so no raw
 * timestamp is ever converted there. Each timestamp is then set from its verdict: a valid one to the instant computed from its validated
 * fields, an absent one to nothing and a present but invalid one to an invalid `Date`, which every reader already treats as unavailable
 * (never as absent). URL construction, authentication, the single request and every non-200 answer stay the generated operation's own;
 * nothing is logged or stored. Every other member, nested ones included, is parsed and converted by the generated client exactly as
 * before.
 */
export class AgentAttemptEvidenceWireClient extends GetAgentAttemptEvidenceEndpointClient {
  protected override jsonParseReviver: ((key: string, value: unknown) => unknown) | undefined = withoutRootTimestamps

  protected override processGetAgentAttemptEvidence(response: Response): Promise<AgentAttemptEvidenceResponse> {
    if (response.status !== 200) {
      return super.processGetAgentAttemptEvidence(response)
    }

    return response.text().then((body) => {
      const verdicts = judgeRawEvidenceTimestamps(body)
      const replay = { status: response.status, headers: response.headers, text: () => Promise.resolve(body) } as Response
      return super.processGetAgentAttemptEvidence(replay).then((answer) => {
        if (verdicts !== null && answer !== null && typeof answer === 'object') {
          for (const key of AGENT_ATTEMPT_EVIDENCE_TIMESTAMPS) {
            const verdict = verdicts[key]
            answer[key] = verdict.status === 'valid' ? verdict.at : verdict.status === 'invalid' ? new Date(Number.NaN) : undefined
          }
        }
        return answer
      })
    })
  }
}
