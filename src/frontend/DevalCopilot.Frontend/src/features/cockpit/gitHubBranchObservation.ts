import type { GetGitHubBranchObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery and the immutable target a remote branch is observed for (ADR-0040, ADR-0042). */
export interface BranchObservationScope {
  source: ObservationSource
  target: ObservationScope
}

/** The branch GitHub returned: the exact short name of the recorded branch and the 40-character lowercase hexadecimal commit it pointed at. */
export interface ObservedBranchRef {
  branchName: string
  commitSha: string
}

/** The repository identity GitHub returned (names equal to the recorded ones ignoring only ASCII case) and the host's observation time. */
export interface ObservedBranchFacts {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/**
 * A reading proven coherent for exactly one scope. The first four statuses carry no observation. `RefNotReturned` carries one without a
 * ref, and means only that GitHub returned no ref for this observation. `CommitMatched` and `CommitDifferent` carry the returned ref,
 * whose commit equals (respectively differs from) the recorded local commit. A point-in-time observation, never a CI result, permission,
 * durable binding, publication approval or publication performed by DevalCopilot.
 */
export type BranchObservationReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'CliUnavailable' | 'Unknown' }
  | { status: 'RefNotReturned'; facts: ObservedBranchFacts }
  | { status: 'CommitMatched' | 'CommitDifferent'; facts: ObservedBranchFacts; ref: ObservedBranchRef }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const COMMIT_SHA = /^[0-9a-f]{40}$/
const STATUSES: readonly string[] = [
  'NotRecorded',
  'Unavailable',
  'CliUnavailable',
  'Unknown',
  'RefNotReturned',
  'CommitMatched',
  'CommitDifferent',
]

const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const sameNameIgnoringAsciiCase = (left: string, right: string) =>
  left.length === right.length && left.replace(/[A-Z]/g, (letter) => letter.toLowerCase()) === right.replace(/[A-Z]/g, (letter) => letter.toLowerCase())

/** The lifetime key of a scope: every identifying fact of the source and the target, so a change of any one is a different owner. */
export const branchObservationScopeKey = (scope: BranchObservationScope): string =>
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

function toFacts(value: unknown, expected: ObservationScope): (ObservedBranchFacts & { ref: unknown }) | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc, ref } = value as Record<string, unknown>
  if (
    typeof repositoryNodeId !== 'string'
    || !NODE_ID.test(repositoryNodeId)
    || !isGitHubOwner(owner)
    || !isGitHubRepository(repository)
    || !sameNameIgnoringAsciiCase(owner, expected.owner)
    || !sameNameIgnoringAsciiCase(repository, expected.repository)
    || !isInstant(observedAtUtc)
  ) {
    return null
  }

  return { repositoryNodeId, owner, repository, observedAtUtc, ref }
}

function toRef(value: unknown, source: ObservationSource): ObservedBranchRef | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { branchName, commitSha } = value as Record<string, unknown>
  return branchName === source.branchName && typeof commitSha === 'string' && COMMIT_SHA.test(commitSha) ? { branchName, commitSha } : null
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0042): the same run and one of the seven closed statuses.
 * NotRecorded and Unavailable carry no source, target or observation; CliUnavailable and Unknown carry exactly this source and target and
 * no observation; RefNotReturned additionally carries a coherent observation with a null ref; CommitMatched and CommitDifferent carry a
 * coherent observation whose ref has exactly the recorded branch name and a 40-character lowercase hexadecimal commit that equals,
 * respectively differs from, the recorded local commit. A contradictory or unknown status, or a mismatched, missing or malformed fact, is
 * null: it is never repaired, trimmed, adopted or partially shown.
 */
export function toBranchObservation(
  response: GetGitHubBranchObservationResponse | null | undefined,
  expected: BranchObservationScope,
): BranchObservationReading | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.source.runId)) {
    return null
  }

  const { status, source, target, observation } = response
  if (typeof status !== 'string' || !STATUSES.includes(status)) {
    return null
  }

  if (status === 'NotRecorded' || status === 'Unavailable') {
    return isAbsent(source) && isAbsent(target) && isAbsent(observation) ? { status } : null
  }

  if (!matchesSource(source, expected.source) || !matchesTarget(target, expected.target)) {
    return null
  }

  if (status === 'CliUnavailable' || status === 'Unknown') {
    return isAbsent(observation) ? { status } : null
  }

  const facts = toFacts(observation, expected.target)
  if (facts === null) {
    return null
  }

  const { ref: returned, ...rest } = facts
  if (status === 'RefNotReturned') {
    return isAbsent(returned) ? { status, facts: rest } : null
  }

  const ref = toRef(returned, expected.source)
  if (ref === null || (ref.commitSha === expected.source.commitSha) !== (status === 'CommitMatched')) {
    return null
  }

  return { status: status as 'CommitMatched' | 'CommitDifferent', facts: rest, ref }
}

/** Fixed plain-text statements; never host or provider text. */
export const BRANCH_OBSERVATION_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown:
    'Unknown: no usable answer was returned for this repository and branch. The repository may be missing, inaccessible, renamed or transferred, or the GitHub CLI, its stored authentication or the answer may be unusable. Nothing is inferred and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was checked.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery and of its recorded target do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  RefNotReturned:
    'GitHub returned no ref for this branch in this observation. That alone does not show that the branch does not exist, and it does not show a permission problem. The repository identity GitHub returned at the time shown:',
  CommitMatched:
    'The branch GitHub returned points at the same commit as the recorded local commit, as reported at the time shown. This is only what GitHub reported then, not a CI result, a permission, a durable binding, a publication approval or a publication performed by DevalCopilot, and it may change at any moment.',
  CommitDifferent:
    'The branch GitHub returned points at a different commit than the recorded local commit, as reported at the time shown. Nothing is inferred about why, and nothing was changed.',
} as const
