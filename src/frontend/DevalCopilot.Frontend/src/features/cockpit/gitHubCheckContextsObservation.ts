import type { GetGitHubCheckContextsObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery and the immutable target a commit's check contexts are observed for (ADR-0040, ADR-0045). */
export interface CheckContextsScope {
  source: ObservationSource
  target: ObservationScope
}

/** The immutable Human confirmation of the repository identity (ADR-0043) this observation is compared with. */
export interface CheckContextsBindingFacts {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/** The repository identity GitHub returned (equal to the bound ID, names equal to the recorded ones ignoring only ASCII case) and the host's observation time. */
export interface CheckContextsObservedFacts {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/** The branch GitHub returned: the exact short name of the recorded branch and the 40-character lowercase hexadecimal commit it pointed at. */
export interface CheckContextsObservedRef {
  branchName: string
  commitSha: string
}

/** One check run or commit status exactly as GitHub returned it. A status context has no conclusion and no check suite. */
export interface CheckContextEntry {
  kind: 'CheckRun' | 'StatusContext'
  id: string
  name: string
  status: string
  conclusion: string | null
  checkSuiteId: string | null
}

/** The host never returns more entries than this; a larger total is reported by its count alone. */
export const MAXIMUM_CHECK_CONTEXTS = 50

/**
 * A reading proven coherent for exactly one scope (ADR-0045). The first three statuses carry no binding; CliUnavailable, Unknown and
 * IdentityChanged carry the bound identity and no observation; RefNotReturned carries an observation without a ref; CommitDifferent
 * carries the returned ref and nothing else (the commit is not the recorded one); NoContextsReturned carries the ref and nothing else
 * (GitHub returned no rollup, which never means that no checks exist); LimitExceeded carries the ref and the total, never an entry;
 * ContextsReported carries the ref, the total (0 to 50) and exactly that many entries in provider order. The bounded collection GitHub
 * returned for one commit: never complete check or workflow evidence, a required-check approval, a permission or publication authority.
 */
export type CheckContextsReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'BindingNotRecorded' }
  | { status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged'; binding: CheckContextsBindingFacts }
  | { status: 'RefNotReturned'; binding: CheckContextsBindingFacts; facts: CheckContextsObservedFacts }
  | {
      status: 'CommitDifferent' | 'NoContextsReturned'
      binding: CheckContextsBindingFacts
      facts: CheckContextsObservedFacts
      ref: CheckContextsObservedRef
    }
  | {
      status: 'LimitExceeded'
      binding: CheckContextsBindingFacts
      facts: CheckContextsObservedFacts
      ref: CheckContextsObservedRef
      totalCount: number
    }
  | {
      status: 'ContextsReported'
      binding: CheckContextsBindingFacts
      facts: CheckContextsObservedFacts
      ref: CheckContextsObservedRef
      totalCount: number
      contexts: CheckContextEntry[]
    }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const CONTEXT_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const COMMIT_SHA = /^[0-9a-f]{40}$/
// Control, Format, Surrogate (a lone one), LineSeparator and ParagraphSeparator; the replacement character is checked beside it.
const FORBIDDEN_LABEL_CHARACTER = /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u
const REPLACEMENT_CHARACTER = String.fromCodePoint(0xfffd)
const MAXIMUM_LABEL_BYTES = 256
const MAXIMUM_INT32 = 2147483647
const STATUSES: readonly string[] = [
  'NotRecorded',
  'Unavailable',
  'BindingNotRecorded',
  'CliUnavailable',
  'Unknown',
  'IdentityChanged',
  'RefNotReturned',
  'CommitDifferent',
  'NoContextsReturned',
  'LimitExceeded',
  'ContextsReported',
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
const STATUS_CONTEXT_STATES: readonly string[] = ['ERROR', 'EXPECTED', 'FAILURE', 'PENDING', 'SUCCESS']

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isNodeId = (value: unknown): value is string => typeof value === 'string' && NODE_ID.test(value)
const isContextId = (value: unknown): value is string => typeof value === 'string' && CONTEXT_ID.test(value)
const isCount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= MAXIMUM_INT32
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

/** Nonblank, well-formed text of 1 to 256 UTF-8 bytes without control, format, surrogate, separator or replacement characters. */
const isLabel = (value: unknown): value is string =>
  typeof value === 'string'
  && value.length <= MAXIMUM_LABEL_BYTES
  && value.trim().length > 0
  && !FORBIDDEN_LABEL_CHARACTER.test(value)
  && !value.includes(REPLACEMENT_CHARACTER)
  && new TextEncoder().encode(value).length <= MAXIMUM_LABEL_BYTES

/** The lifetime key of a scope: every identifying fact of the source and the target, so a change of any one is a different owner. */
export const checkContextsScopeKey = (scope: CheckContextsScope): string =>
  JSON.stringify([
    observationSourceKey(scope.source),
    scope.target.id,
    scope.target.eventId,
    scope.target.owner,
    scope.target.repository,
    scope.target.recordedAtUtc.getTime(),
  ])

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

function toBinding(value: unknown, expected: ObservationScope): CheckContextsBindingFacts | null {
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
  binding: CheckContextsBindingFacts,
): (CheckContextsObservedFacts & { ref: unknown; totalCount: unknown; contexts: unknown }) | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc, ref, totalCount, contexts } = value as Record<string, unknown>
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

  return { repositoryNodeId, owner, repository, observedAtUtc, ref, totalCount, contexts }
}

function toRef(value: unknown, source: ObservationSource): CheckContextsObservedRef | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { branchName, commitSha } = value as Record<string, unknown>
  return branchName === source.branchName && typeof commitSha === 'string' && COMMIT_SHA.test(commitSha) ? { branchName, commitSha } : null
}

/** Only the six named members are read; nothing else a wire object carries is copied. A check run carries a suite and a conclusion or null. */
function toEntry(value: unknown): CheckContextEntry | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }

  const { kind, id, name, status, conclusion, checkSuiteId } = value as Record<string, unknown>
  if (!isContextId(id) || !isLabel(name) || typeof status !== 'string') {
    return null
  }

  if (kind === 'CheckRun') {
    return CHECK_RUN_STATUSES.includes(status)
      && (conclusion === null || (typeof conclusion === 'string' && CHECK_RUN_CONCLUSIONS.includes(conclusion)))
      && isContextId(checkSuiteId)
      ? { kind, id, name, status, conclusion: conclusion as string | null, checkSuiteId }
      : null
  }

  return kind === 'StatusContext' && STATUS_CONTEXT_STATES.includes(status) && conclusion === null && checkSuiteId === null
    ? { kind, id, name, status, conclusion: null, checkSuiteId: null }
    : null
}

function toEntries(value: unknown, totalCount: number): CheckContextEntry[] | null {
  if (!Array.isArray(value) || value.length !== totalCount) {
    return null
  }

  const entries: CheckContextEntry[] = []
  const identifiers = new Set<string>()
  for (const item of value) {
    const entry = toEntry(item)
    if (entry === null || identifiers.has(entry.id)) {
      return null
    }

    identifiers.add(entry.id)
    entries.push(entry)
  }

  return entries
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0045): the same run and one of the eleven closed statuses with
 * exactly its documented facts. NotRecorded and Unavailable carry nothing; BindingNotRecorded carries exactly this source and target;
 * every other status also carries a coherent binding (the conservative opaque ID, supported names equal to the recorded ones ignoring
 * only ASCII case, instants ordered target, observation, binding); CliUnavailable, Unknown and IdentityChanged no observation. An
 * observation must carry the bound repository ID exactly and a time not before the binding. RefNotReturned carries no ref, count or
 * collection; the others carry a ref with exactly the recorded branch name and a 40-character lowercase hexadecimal commit that
 * differs from the recorded one (CommitDifferent, never with a count or entries) or equals it. For the recorded commit,
 * NoContextsReturned carries neither a count nor entries, LimitExceeded a total above 50 and no entry, and ContextsReported a total
 * from 0 to 50 and an array of exactly that many valid, uniquely identified entries. A contradictory or unknown status, or a
 * mismatched, missing or malformed fact or entry, is null: it is never repaired, trimmed, adopted or partially shown.
 */
export function toCheckContextsObservation(
  response: GetGitHubCheckContextsObservationResponse | null | undefined,
  expected: CheckContextsScope,
): CheckContextsReading | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.source.runId)) {
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

  const { ref: returned, totalCount, contexts, ...rest } = facts
  if (status === 'RefNotReturned') {
    return isAbsent(returned) && isAbsent(totalCount) && isAbsent(contexts) ? { status, binding: bound, facts: rest } : null
  }

  const ref = toRef(returned, expected.source)
  if (ref === null) {
    return null
  }

  const isRecordedCommit = ref.commitSha === expected.source.commitSha
  if (status === 'CommitDifferent') {
    return !isRecordedCommit && isAbsent(totalCount) && isAbsent(contexts) ? { status, binding: bound, facts: rest, ref } : null
  }

  if (!isRecordedCommit) {
    return null
  }

  if (status === 'NoContextsReturned') {
    return isAbsent(totalCount) && isAbsent(contexts) ? { status, binding: bound, facts: rest, ref } : null
  }

  if (!isCount(totalCount)) {
    return null
  }

  if (status === 'LimitExceeded') {
    return totalCount > MAXIMUM_CHECK_CONTEXTS && isAbsent(contexts) ? { status, binding: bound, facts: rest, ref, totalCount } : null
  }

  if (status !== 'ContextsReported' || totalCount > MAXIMUM_CHECK_CONTEXTS) {
    return null
  }

  const entries = toEntries(contexts, totalCount)
  return entries === null ? null : { status, binding: bound, facts: rest, ref, totalCount, contexts: entries }
}

/** Fixed plain-text statements; never host or provider text. */
export const CHECK_CONTEXTS_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown:
    'Unknown: no usable answer was returned for this repository, branch and commit. The repository may be missing, inaccessible, renamed or transferred, or the GitHub CLI, its stored authentication or the answer may be unusable, or the host clock was unusable. Nothing is inferred and nothing was changed.',
  IdentityChanged:
    'The repository ID GitHub returned now differs from the repository ID bound for this recorded target, so no check details are shown. Nothing is inferred about why, and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was checked.',
  BindingNotRecorded:
    'No repository identity is bound for this recorded target, so nothing was checked. Check details need the immutable identity binding to compare with.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, of its recorded target or of its identity binding do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  RefNotReturned:
    'GitHub returned no ref for this branch in this observation, so no check details are shown. That alone does not show that the branch does not exist, and it does not show a permission problem. The repository identity GitHub returned at the time shown:',
  CommitDifferent:
    'The branch GitHub returned points at a different commit than the recorded local commit, as reported at the time shown, so no check details for the recorded commit are shown. Nothing is inferred about why, and nothing was changed.',
  NoContextsReturned:
    'GitHub returned no check summary for the recorded commit at the time shown, so it returned no check details. That means only that nothing was returned in this observation; it does not mean that no checks exist or that none are running.',
  LimitExceeded:
    'GitHub reports more than 50 checks and statuses for the recorded commit. Only the 50-entry bound is ever requested, so no entry is shown, and nothing is truncated silently. The total GitHub reported at the time shown:',
  ContextsReported:
    'The branch GitHub returned points at the recorded local commit, and GitHub returned these checks and statuses for that commit at the time shown, in the order GitHub listed them:',
  ZeroReturned:
    'GitHub returned zero checks and statuses for the recorded commit at the time shown. That is not a success, a pass or an approval; it only means none were returned in this observation.',
  LabelRedaction:
    "Names are shown as safe text: the host may have redacted any part of a check name that looked like a secret, so a name is not necessarily the provider's original text.",
  Disclosure:
    'This is the bounded collection GitHub returned for this commit. It is not required-check approval, complete workflow history, permission or publication authority.',
} as const
