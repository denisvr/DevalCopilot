import type { GetGitHubCheckRunOutputObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery, the immutable target and the one check run whose output is inspected (ADR-0040, ADR-0047). */
export interface CheckRunOutputScope {
  source: ObservationSource
  target: ObservationScope
  checkRunId: string
}

/** The immutable Human confirmation of the repository identity (ADR-0043) this inspection is compared with. */
export interface CheckRunOutputBindingFacts {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/** The repository identity GitHub returned (equal to the bound ID, names equal to the recorded ones ignoring only ASCII case) and the host's observation time. */
export interface CheckRunOutputObservedFacts {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/** The branch GitHub returned: the exact short name of the recorded branch and the 40-character lowercase hexadecimal commit it pointed at. */
export interface CheckRunOutputObservedRef {
  branchName: string
  commitSha: string
}

/** The provider-supplied title, summary and text; each is null (GitHub returned no value) or a string, which may be empty or only whitespace. */
export interface CheckRunOutputText {
  title: string | null
  summary: string | null
  text: string | null
}

/** The admitted check run: its ID (the requested one), its suite, the suite's commit (the recorded one), its safe name, literal status and conclusion. */
export interface CheckRunOutputCheckRun {
  id: string
  checkSuiteId: string
  commitSha: string
  name: string
  status: string
  conclusion: string | null
}

export type CheckRunOutputReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'BindingNotRecorded' }
  | { status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged'; binding: CheckRunOutputBindingFacts }
  | { status: 'RefNotReturned'; binding: CheckRunOutputBindingFacts; facts: CheckRunOutputObservedFacts }
  | {
      status: 'CommitDifferent' | 'CheckRunUnavailable'
      binding: CheckRunOutputBindingFacts
      facts: CheckRunOutputObservedFacts
      ref: CheckRunOutputObservedRef
    }
  | {
      status: 'OutputNotReturned'
      binding: CheckRunOutputBindingFacts
      facts: CheckRunOutputObservedFacts
      ref: CheckRunOutputObservedRef
      checkRun: CheckRunOutputCheckRun
    }
  | {
      status: 'OutputReported'
      binding: CheckRunOutputBindingFacts
      facts: CheckRunOutputObservedFacts
      ref: CheckRunOutputObservedRef
      checkRun: CheckRunOutputCheckRun
      output: CheckRunOutputText
    }

export const MAXIMUM_TITLE_BYTES = 256
export const MAXIMUM_SUMMARY_BYTES = 8 * 1024
export const MAXIMUM_TEXT_BYTES = 16 * 1024
export const MAXIMUM_OUTPUT_BYTES = 24 * 1024

/** The lifetime key of a scope: every identifying fact of the source, the target and the selected check run. */
export const checkRunOutputScopeKey = (scope: CheckRunOutputScope): string =>
  JSON.stringify([
    observationSourceKey(scope.source),
    scope.target.id,
    scope.target.eventId,
    scope.target.owner,
    scope.target.repository,
    scope.target.recordedAtUtc.getTime(),
    scope.checkRunId,
  ])

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const CHECK_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const COMMIT_SHA = /^[0-9a-f]{40}$/
// Control, Format, Surrogate (a lone one), LineSeparator and ParagraphSeparator; the replacement character is checked beside it.
const FORBIDDEN_NAME_CHARACTER = /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u
// The same, except that tab, line feed and carriage return are allowed in the output text.
const FORBIDDEN_OUTPUT_CHARACTER = /[\p{Cf}\p{Cs}\p{Zl}\p{Zp}]|(?![\t\n\r])\p{Cc}/u
const REPLACEMENT_CHARACTER = String.fromCodePoint(0xfffd)
const MAXIMUM_NAME_BYTES = 256
const STATUSES: readonly string[] = [
  'NotRecorded',
  'Unavailable',
  'BindingNotRecorded',
  'CliUnavailable',
  'Unknown',
  'IdentityChanged',
  'RefNotReturned',
  'CommitDifferent',
  'CheckRunUnavailable',
  'OutputNotReturned',
  'OutputReported',
]
const CHECK_RUN_STATUSES: readonly string[] = ['COMPLETED', 'IN_PROGRESS', 'PENDING', 'QUEUED', 'REQUESTED', 'WAITING']
const CHECK_RUN_CONCLUSIONS: readonly string[] = [
  'ACTION_REQUIRED',
  'CANCELLED',
  'FAILURE',
  'NEUTRAL',
  'SKIPPED',
  'STALE',
  'STARTUP_FAILURE',
  'SUCCESS',
  'TIMED_OUT',
]

const encoder = new TextEncoder()
const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isNodeId = (value: unknown): value is string => typeof value === 'string' && NODE_ID.test(value)
const isCheckId = (value: unknown): value is string => typeof value === 'string' && CHECK_ID.test(value)
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

/** Nonblank, well-formed text of 1 to 256 UTF-8 bytes without control (tab and line breaks included), format, surrogate, separator or replacement characters. */
const isName = (value: unknown): value is string =>
  typeof value === 'string'
  && value.length <= MAXIMUM_NAME_BYTES
  && value.trim().length > 0
  && !FORBIDDEN_NAME_CHARACTER.test(value)
  && !value.includes(REPLACEMENT_CHARACTER)
  && encoder.encode(value).length <= MAXIMUM_NAME_BYTES

/** A string (empty and whitespace included) of at most `maximum` UTF-8 bytes, well formed, with no control character other than tab, line feed and carriage return. */
const isOutputString = (value: unknown, maximum: number): value is string =>
  typeof value === 'string'
  && value.length <= maximum
  && !FORBIDDEN_OUTPUT_CHARACTER.test(value)
  && !value.includes(REPLACEMENT_CHARACTER)
  && encoder.encode(value).length <= maximum

const byteLength = (value: string | null) => (value === null ? 0 : encoder.encode(value).length)

function matchesTarget(value: unknown, expected: ObservationScope): boolean {
  const scope = toScope(value)
  return (
    scope !== null
    && sameId(scope.id, expected.id)
    && sameId(scope.eventId, expected.eventId)
    && scope.host === expected.host
    && scope.owner === expected.owner
    && scope.repository === expected.repository
    && scope.recordedAtUtc.getTime() === expected.recordedAtUtc.getTime()
  )
}

function toBinding(value: unknown, expected: ObservationScope): CheckRunOutputBindingFacts | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc } = value as Record<string, unknown>
  if (
    !isText(id)
    || !isText(eventId)
    || !isNodeId(repositoryNodeId)
    || !isGitHubOwner(owner)
    || !isGitHubRepository(repository)
    || !sameNameIgnoringAsciiCase(owner, expected.owner)
    || !sameNameIgnoringAsciiCase(repository, expected.repository)
    || !isInstant(observedAtUtc)
    || !isInstant(recordedAtUtc)
    || observedAtUtc.getTime() < expected.recordedAtUtc.getTime()
    || recordedAtUtc.getTime() < observedAtUtc.getTime()
  ) {
    return null
  }

  return { id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc }
}

function toFacts(
  value: unknown,
  expected: ObservationScope,
  binding: CheckRunOutputBindingFacts,
): (CheckRunOutputObservedFacts & { ref: unknown; checkRun: unknown }) | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc, ref, checkRun } = value as Record<string, unknown>
  if (
    !isNodeId(repositoryNodeId)
    || repositoryNodeId !== binding.repositoryNodeId
    || !isGitHubOwner(owner)
    || !isGitHubRepository(repository)
    || !sameNameIgnoringAsciiCase(owner, expected.owner)
    || !sameNameIgnoringAsciiCase(repository, expected.repository)
    || !isInstant(observedAtUtc)
    || observedAtUtc.getTime() < binding.recordedAtUtc.getTime()
  ) {
    return null
  }

  return { repositoryNodeId, owner, repository, observedAtUtc, ref, checkRun }
}

function toRef(value: unknown, source: ObservationSource): CheckRunOutputObservedRef | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { branchName, commitSha } = value as Record<string, unknown>
  return branchName === source.branchName && typeof commitSha === 'string' && COMMIT_SHA.test(commitSha) ? { branchName, commitSha } : null
}

/** Only the named members are read; nothing else a wire object carries is copied. The check run echoes the requested ID and the recorded commit. */
function toCheckRun(value: unknown, expected: CheckRunOutputScope): (CheckRunOutputCheckRun & { output: unknown }) | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }

  const { id, checkSuiteId, commitSha, name, status, conclusion, output } = value as Record<string, unknown>
  if (
    id !== expected.checkRunId
    || !isCheckId(checkSuiteId)
    || commitSha !== expected.source.commitSha
    || !isName(name)
    || typeof status !== 'string'
    || !CHECK_RUN_STATUSES.includes(status)
    || !(conclusion === null || (typeof conclusion === 'string' && CHECK_RUN_CONCLUSIONS.includes(conclusion)))
  ) {
    return null
  }

  return { id, checkSuiteId, commitSha, name, status, conclusion, output }
}

/** Each value is null or a bounded string, the three are bounded together, and nothing is trimmed, normalized or repaired. */
function toOutputText(value: unknown): CheckRunOutputText | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }

  const { title, summary, text } = value as Record<string, unknown>
  if (
    !(title === null || isOutputString(title, MAXIMUM_TITLE_BYTES))
    || !(summary === null || isOutputString(summary, MAXIMUM_SUMMARY_BYTES))
    || !(text === null || isOutputString(text, MAXIMUM_TEXT_BYTES))
  ) {
    return null
  }

  const shown = { title: title as string | null, summary: summary as string | null, text: text as string | null }
  return byteLength(shown.title) + byteLength(shown.summary) + byteLength(shown.text) <= MAXIMUM_OUTPUT_BYTES ? shown : null
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0047): the same run and check run identifier and one of the
 * eleven closed statuses with exactly its documented facts. NotRecorded and Unavailable carry nothing; BindingNotRecorded carries
 * exactly this source and target; every other status also carries a coherent binding; CliUnavailable, Unknown and IdentityChanged no
 * observation. An observation must carry the bound repository ID exactly, names equal to the recorded ones ignoring only ASCII case and
 * a time not before the binding. RefNotReturned carries no ref and no check run; the others carry a ref with exactly the recorded
 * branch name and a 40-character lowercase hexadecimal commit that differs from the recorded one (CommitDifferent, never a check run)
 * or equals it. For the recorded commit, CheckRunUnavailable carries no check run, OutputNotReturned an admitted check run whose output
 * is null, and OutputReported an admitted check run whose output has at least one non-null field. The check run echoes the requested ID
 * and the recorded commit, and its text values satisfy the host's bounds and character contract. A contradictory or unknown status, or a
 * mismatched, missing or malformed fact, is null: it is never repaired, trimmed, adopted or partially shown.
 */
export function toCheckRunOutputObservation(
  response: GetGitHubCheckRunOutputObservationResponse | null | undefined,
  expected: CheckRunOutputScope,
): CheckRunOutputReading | null {
  if (
    !response
    || typeof response !== 'object'
    || !sameId(response.runId, expected.source.runId)
    || response.checkRunId !== expected.checkRunId
  ) {
    return null
  }

  const { status, source, target, binding, observation } = response
  if (typeof status !== 'string' || !STATUSES.includes(status)) {
    return null
  }

  if (status === 'NotRecorded' || status === 'Unavailable') {
    return isAbsent(source) && isAbsent(target) && isAbsent(binding) && isAbsent(observation) ? { status } : null
  }

  if (!matchesSource(source, expected.source) || !matchesTarget(target, expected.target)) {
    return null
  }

  if (status === 'BindingNotRecorded') {
    return isAbsent(binding) && isAbsent(observation) ? { status } : null
  }

  const bound = toBinding(binding, expected.target)
  if (bound === null) {
    return null
  }

  if (status === 'CliUnavailable' || status === 'Unknown' || status === 'IdentityChanged') {
    return isAbsent(observation) ? { status, binding: bound } : null
  }

  const facts = toFacts(observation, expected.target, bound)
  if (facts === null) {
    return null
  }

  const { ref: returned, checkRun: returnedCheckRun, ...rest } = facts
  if (status === 'RefNotReturned') {
    return isAbsent(returned) && isAbsent(returnedCheckRun) ? { status, binding: bound, facts: rest } : null
  }

  const ref = toRef(returned, expected.source)
  if (ref === null) {
    return null
  }

  const isRecordedCommit = ref.commitSha === expected.source.commitSha
  if (status === 'CommitDifferent') {
    return !isRecordedCommit && isAbsent(returnedCheckRun) ? { status, binding: bound, facts: rest, ref } : null
  }

  if (!isRecordedCommit) {
    return null
  }

  if (status === 'CheckRunUnavailable') {
    return isAbsent(returnedCheckRun) ? { status, binding: bound, facts: rest, ref } : null
  }

  const found = toCheckRun(returnedCheckRun, expected)
  if (found === null) {
    return null
  }

  const { output: returnedOutput, ...checkRun } = found
  if (status === 'OutputNotReturned') {
    return isAbsent(returnedOutput) ? { status, binding: bound, facts: rest, ref, checkRun } : null
  }

  const output = toOutputText(returnedOutput)
  return status === 'OutputReported' && output !== null && (output.title !== null || output.summary !== null || output.text !== null)
    ? { status, binding: bound, facts: rest, ref, checkRun, output }
    : null
}

/** Fixed plain-text statements; never host or provider text. */
export const CHECK_RUN_OUTPUT_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was inspected.',
  Unknown:
    'Unknown: no usable answer was returned for this repository, branch, commit and check run. The repository may be missing, inaccessible, renamed or transferred, or the GitHub CLI, its stored authentication or an answer may be unusable, or the host clock was unusable. Nothing is inferred and nothing was changed.',
  IdentityChanged:
    'The repository ID GitHub returned now differs from the repository ID bound for this recorded target, so no check output is shown. Nothing is inferred about why, and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was inspected.',
  BindingNotRecorded:
    'No repository identity is bound for this recorded target, so nothing was inspected. Check output needs the immutable identity binding to compare with.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, of its recorded target or of its identity binding do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  RefNotReturned:
    'GitHub returned no ref for this branch in this inspection, so no check output is shown. That alone does not show that the branch does not exist, and it does not show a permission problem. The repository identity GitHub returned at the time shown:',
  CommitDifferent:
    'The branch GitHub returned points at a different commit than the recorded local commit, as reported at the time shown, so no output for a check of the recorded commit is shown. Nothing is inferred about why, and nothing was changed.',
  CheckRunUnavailable:
    'GitHub did not show this check run as belonging to the bound repository and the recorded commit in this inspection, or its association changed while it was being read, so no check details or output are shown. Nothing is inferred about why, and nothing was changed.',
  OutputNotReturned:
    'GitHub returned this check run for the recorded commit at the time shown, with no title, summary or text. That means only that nothing was returned in this inspection; it does not mean that the check has no output.',
  OutputReported:
    'GitHub returned this check run for the recorded commit, with the following output, at the time shown. Each value is exactly what the host admitted; a missing value is stated as such:',
  NullField: 'GitHub returned no value for this field.',
  EmptyField: 'GitHub returned an empty string for this field.',
  WhitespaceField: 'GitHub returned only whitespace for this field; it is shown as returned.',
  Redaction:
    "This text may have been redacted by the host and is not necessarily the provider's original text. Only five supported secret shapes are detected, so any other secret would not be redacted.",
  Disclosure:
    'This is untrusted text supplied by GitHub for one check run at one moment. It is shown as plain text only: it is not an instruction, a diagnosis, required-check approval, permission or publication authority, and the host does not send it to any Agent.',
} as const
