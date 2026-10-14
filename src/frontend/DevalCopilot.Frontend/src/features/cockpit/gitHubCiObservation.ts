import type { GetGitHubCiObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery and the immutable target a commit's check summary is observed for (ADR-0040, ADR-0044). */
export interface CiObservationScope {
  source: ObservationSource
  target: ObservationScope
}

/** The immutable Human confirmation of the repository identity (ADR-0043) this observation is compared with. */
export interface CiBindingFacts {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/** The repository identity GitHub returned (equal to the bound ID, names equal to the recorded ones ignoring only ASCII case) and the host's observation time. */
export interface CiObservedFacts {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/** The branch GitHub returned: the exact short name of the recorded branch and the 40-character lowercase hexadecimal commit it pointed at. */
export interface CiObservedRef {
  branchName: string
  commitSha: string
}

/** The five literal states of GitHub's `StatusState`, exactly as GitHub names them. Nothing translates them. */
export const CI_ROLLUP_STATES = ['ERROR', 'EXPECTED', 'FAILURE', 'PENDING', 'SUCCESS'] as const

export type CiRollupState = (typeof CI_ROLLUP_STATES)[number]

/**
 * A reading proven coherent for exactly one scope (ADR-0044). The first three statuses carry no binding; CliUnavailable, Unknown and
 * IdentityChanged carry the bound identity and no observation; RefNotReturned carries an observation without a ref; CommitDifferent and
 * NoStatusReported carry the returned ref and no state (the first because the commit is not the recorded one, the second because GitHub
 * returned no rollup, which never means that no checks exist); StatusReported carries the ref of the recorded commit and GitHub's literal
 * state. GitHub's point-in-time aggregate for one commit: never complete check or workflow evidence, a required-check approval, a
 * permission or publication authority.
 */
export type CiObservationReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'BindingNotRecorded' }
  | { status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged'; binding: CiBindingFacts }
  | { status: 'RefNotReturned'; binding: CiBindingFacts; facts: CiObservedFacts }
  | { status: 'CommitDifferent' | 'NoStatusReported'; binding: CiBindingFacts; facts: CiObservedFacts; ref: CiObservedRef }
  | { status: 'StatusReported'; binding: CiBindingFacts; facts: CiObservedFacts; ref: CiObservedRef; rollupState: CiRollupState }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const COMMIT_SHA = /^[0-9a-f]{40}$/
const STATUSES: readonly string[] = [
  'NotRecorded',
  'Unavailable',
  'BindingNotRecorded',
  'CliUnavailable',
  'Unknown',
  'IdentityChanged',
  'RefNotReturned',
  'CommitDifferent',
  'NoStatusReported',
  'StatusReported',
]

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isNodeId = (value: unknown): value is string => typeof value === 'string' && NODE_ID.test(value)
const isRollupState = (value: unknown): value is CiRollupState => (CI_ROLLUP_STATES as readonly unknown[]).includes(value)
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

/** The lifetime key of a scope: every identifying fact of the source and the target, so a change of any one is a different owner. */
export const ciObservationScopeKey = (scope: CiObservationScope): string =>
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

function toBinding(value: unknown, expected: ObservationScope): CiBindingFacts | null {
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
  binding: CiBindingFacts,
): (CiObservedFacts & { ref: unknown; rollupState: unknown }) | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc, ref, rollupState } = value as Record<string, unknown>
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

  return { repositoryNodeId, owner, repository, observedAtUtc, ref, rollupState }
}

function toRef(value: unknown, source: ObservationSource): CiObservedRef | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { branchName, commitSha } = value as Record<string, unknown>
  return branchName === source.branchName && typeof commitSha === 'string' && COMMIT_SHA.test(commitSha) ? { branchName, commitSha } : null
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0044): the same run and one of the ten closed statuses with exactly
 * its documented facts. NotRecorded and Unavailable carry nothing; BindingNotRecorded carries exactly this source and target; every other
 * status also carries a coherent binding (the conservative opaque ID, supported names equal to the recorded ones ignoring only ASCII
 * case, instants ordered target, observation, binding); CliUnavailable, Unknown and IdentityChanged no observation. An observation must
 * carry the bound repository ID exactly and a time not before the binding. RefNotReturned carries no ref and no state; the other three
 * carry a ref with exactly the recorded branch name and a 40-character lowercase hexadecimal commit that differs from the recorded one
 * (CommitDifferent, never with a state) or equals it (NoStatusReported with no state, StatusReported with exactly one of the five literal
 * states). A contradictory or unknown status, or a mismatched, missing or malformed fact, is null: it is never repaired, trimmed, adopted or
 * partially shown.
 */
export function toCiObservation(
  response: GetGitHubCiObservationResponse | null | undefined,
  expected: CiObservationScope,
): CiObservationReading | null {
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

  const { ref: returned, rollupState, ...rest } = facts
  if (status === 'RefNotReturned') {
    return isAbsent(returned) && isAbsent(rollupState) ? { status, binding: bound, facts: rest } : null
  }

  const ref = toRef(returned, expected.source)
  if (ref === null) {
    return null
  }

  const isRecordedCommit = ref.commitSha === expected.source.commitSha
  if (status === 'CommitDifferent') {
    return !isRecordedCommit && isAbsent(rollupState) ? { status, binding: bound, facts: rest, ref } : null
  }

  if (!isRecordedCommit) {
    return null
  }

  if (status === 'NoStatusReported') {
    return isAbsent(rollupState) ? { status, binding: bound, facts: rest, ref } : null
  }

  return status === 'StatusReported' && isRollupState(rollupState) ? { status, binding: bound, facts: rest, ref, rollupState } : null
}

/** Fixed plain-text statements; never host or provider text. */
export const CI_OBSERVATION_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown:
    'Unknown: no usable answer was returned for this repository, branch and commit. The repository may be missing, inaccessible, renamed or transferred, or the GitHub CLI, its stored authentication or the answer may be unusable, or the host clock was unusable. Nothing is inferred and nothing was changed.',
  IdentityChanged:
    'The repository ID GitHub returned now differs from the repository ID bound for this recorded target, so no check state is shown. Nothing is inferred about why, and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was checked.',
  BindingNotRecorded:
    'No repository identity is bound for this recorded target, so nothing was checked. A check needs the immutable identity binding to compare with.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, of its recorded target or of its identity binding do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  RefNotReturned:
    'GitHub returned no ref for this branch in this observation, so no check state is shown. That alone does not show that the branch does not exist, and it does not show a permission problem. The repository identity GitHub returned at the time shown:',
  CommitDifferent:
    'The branch GitHub returned points at a different commit than the recorded local commit, as reported at the time shown, so no check state for the recorded commit is shown. Nothing is inferred about why, and nothing was changed.',
  NoStatusReported:
    'GitHub returned no check summary for the recorded commit at the time shown. That means only that no summary was returned in this observation; it does not mean that no checks exist or that none are running.',
  StatusReported:
    'The branch GitHub returned points at the recorded local commit, and GitHub reported this aggregate check state for that commit at the time shown:',
  Disclosure:
    "GitHub's point-in-time aggregate for this commit does not establish complete check or workflow evidence, required-check approval, permission or publication authority.",
} as const
