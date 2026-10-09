import { ApiException } from '../../api/generated/api-client'

/** Fixed client-side copy for the explicit abandonment of a manual run (ADR-0031). Server text and the reason are never echoed as copy. */

export const ABANDON_MAX_REASON_BYTES = 2048

export const ABANDON_UNKNOWN_OUTCOME_MESSAGE = 'The request outcome was unknown; the recorded status is shown.'
export const ABANDON_GENERIC_FAILURE = 'The run could not be abandoned.'

export const ABANDON_EXPLANATION: readonly string[] = [
  'Abandoning ends this run without completing the objective. It is not a successful completion.',
  'Everything recorded stays: the run history, attempts, messages, checkpoints and any changes already in the workspace are kept exactly as they are.',
  'Nothing is cancelled, repaired, released or deleted, and no local commit is created or undone.',
  'Another objective can then be recorded for this project. New work still needs its own normal checks and approvals.',
  'It is refused while an attempt, verification or local commit of this project is active or unclear.',
]

export const ABANDONED_NOTE =
  'This run ended without completing its objective. Its history and any existing changes remain as they were, nothing was cancelled, repaired or deleted, and new work still needs normal checks.'

const REFUSAL_COPY: Readonly<Record<string, string>> = {
  run_not_manual: 'Only a manual Agent run can be abandoned.',
  run_not_abandonable: 'Only a created or running manual run can be abandoned; this run has already ended.',
  active_attempt: 'An Agent or process attempt of this project is still active or in an unknown state, so this run cannot be abandoned now.',
  active_verification:
    'A verification execution of this project is still active or in an unknown state, so this run cannot be abandoned now.',
  local_commit_open:
    'A local commit of this project is not finished or is in an unknown state. Abandoning a run is never used to override an ambiguous local delivery.',
  workspace_busy:
    'A workspace of this project is being prepared or committed, or is in an unknown state, so this run cannot be abandoned now.',
  already_abandoned: 'This run was already abandoned.',
  reason_conflict: 'This run was already abandoned with a different reason. Reload to see the recorded reason.',
  abandonment_incoherent:
    'The recorded abandonment of this run is not coherent, so it is not treated as abandoned and no new objective is offered through it.',
  concurrent_change: 'The run changed while the abandonment was being recorded. Read its status again and retry.',
}

const UNKNOWN_REFUSAL = 'This run cannot be abandoned right now.'
const PREFIX = 'run_abandonment.'

const normalizeCode = (code: string) => (code.startsWith(PREFIX) ? code.slice(PREFIX.length) : code)

/** Fixed explanation for a refusal code (status refusal or request refusal); unknown codes never echo server text. */
export function describeAbandonmentRefusal(code: string | null | undefined): string {
  if (!code) {
    return UNKNOWN_REFUSAL
  }
  return REFUSAL_COPY[normalizeCode(code)] ?? UNKNOWN_REFUSAL
}

export type AbandonFailure = { kind: 'unknown' } | { kind: 'refused'; message: string }

function problemCode(caught: ApiException): string | null {
  try {
    const parsed = JSON.parse(caught.response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    return typeof code === 'string' ? code : null
  } catch {
    return null
  }
}

/**
 * A failure with no HTTP response (network, timeout) or a 5xx is an unknown outcome: the server may have recorded the abandonment, so
 * the recorded status is read again and nothing is resubmitted. Any other HTTP response is a definite refusal described by fixed copy.
 */
export function classifyAbandonFailure(caught: unknown): AbandonFailure {
  if (!ApiException.isApiException(caught)) {
    return { kind: 'unknown' }
  }
  const exception = caught as ApiException
  if (exception.status >= 500 || exception.status < 100) {
    return { kind: 'unknown' }
  }
  const code = problemCode(exception)
  if (code && code.startsWith(PREFIX)) {
    return { kind: 'refused', message: describeAbandonmentRefusal(code) }
  }
  switch (exception.status) {
    case 400:
      return { kind: 'refused', message: 'The reason was not accepted. It needs text, no control characters and at most 2048 bytes.' }
    case 404:
      return { kind: 'refused', message: 'This run could not be found.' }
    case 409:
      return { kind: 'refused', message: 'The run changed or can no longer be abandoned. Reload the run and retry.' }
    default:
      return { kind: 'refused', message: ABANDON_GENERIC_FAILURE }
  }
}

export type AbandonReasonProblem = 'empty' | 'control_character' | 'too_long'

const SEPARATOR_OR_FORMAT = new RegExp('[\\p{Cf}' + String.fromCharCode(0x2028) + String.fromCharCode(0x2029) + ']', 'u')

function hasUnpairedSurrogate(text: string): boolean {
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index)
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = text.charCodeAt(index + 1)
      if (next >= 0xdc00 && next <= 0xdfff) {
        index += 1
        continue
      }
      return true
    }
    if (code >= 0xdc00 && code <= 0xdfff) {
      return true
    }
  }
  return false
}

/**
 * Client-side mirror of the host's reason rules: CRLF to LF, trimmed, non-blank, well-formed text, no control, format or separator
 * character other than line feed, and at most 2048 bytes of UTF-8. The sent text is exactly the normalized text that is valid.
 */
export function validateAbandonReason(draft: string): { valid: true; reason: string } | { valid: false; problem: AbandonReasonProblem } {
  const reason = draft.replaceAll('\r\n', '\n').trim()
  if (reason.length === 0) {
    return { valid: false, problem: 'empty' }
  }
  if (hasUnpairedSurrogate(reason)) {
    return { valid: false, problem: 'control_character' }
  }
  for (let index = 0; index < reason.length; index += 1) {
    const code = reason.charCodeAt(index)
    if ((code < 0x20 && code !== 0x0a) || code === 0x7f || (code >= 0x80 && code < 0xa0)) {
      return { valid: false, problem: 'control_character' }
    }
  }
  if (SEPARATOR_OR_FORMAT.test(reason)) {
    return { valid: false, problem: 'control_character' }
  }
  if (new TextEncoder().encode(reason).length > ABANDON_MAX_REASON_BYTES) {
    return { valid: false, problem: 'too_long' }
  }
  return { valid: true, reason }
}

export const ABANDON_REASON_PROBLEM_COPY: Readonly<Record<AbandonReasonProblem, string>> = {
  empty: 'Enter the reason this run is being abandoned.',
  control_character: 'The reason must not contain control or invisible formatting characters; line breaks are fine.',
  too_long: 'The reason must be at most 2048 bytes.',
}

/** The identity of the eligible form lifetime of one run: replacing it discards the draft and ends every pending interaction. */
export const abandonIdentityKey = (runId: string) => `abandon|${runId}`
