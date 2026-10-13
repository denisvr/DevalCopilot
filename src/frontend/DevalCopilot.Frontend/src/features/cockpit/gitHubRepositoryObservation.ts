import type { GetGitHubRepositoryObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery and the immutable target a repository identity is observed for (ADR-0040, ADR-0041). */
export interface RepositoryObservationScope {
  source: ObservationSource
  target: ObservationScope
}

/** The identity GitHub returned: an opaque node ID and names equal to the recorded ones ignoring only ASCII case. */
export interface ObservedRepositoryIdentity {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/**
 * A reading proven coherent for exactly one scope. The first four statuses carry no identity; `Observed` carries one. A point-in-time
 * observation of the repository ID and names, never a remote branch, commit, permission, CI result, publication approval or binding.
 */
export type RepositoryObservationReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'CliUnavailable' | 'Unknown' }
  | { status: 'Observed'; identity: ObservedRepositoryIdentity }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const STATUSES: readonly string[] = ['NotRecorded', 'Unavailable', 'CliUnavailable', 'Unknown', 'Observed']

const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const sameNameIgnoringAsciiCase = (left: string, right: string) =>
  left.length === right.length && left.replace(/[A-Z]/g, (letter) => letter.toLowerCase()) === right.replace(/[A-Z]/g, (letter) => letter.toLowerCase())

/** The lifetime key of a scope: every identifying fact of the source and the target, so a change of any one is a different owner. */
export const repositoryObservationScopeKey = (scope: RepositoryObservationScope): string =>
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

function toIdentity(value: unknown, expected: ObservationScope): ObservedRepositoryIdentity | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc } = value as Record<string, unknown>
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

  return { repositoryNodeId, owner, repository, observedAtUtc }
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0041): the same run and one of the five closed statuses.
 * NotRecorded and Unavailable carry no source, target or observation; CliUnavailable and Unknown carry exactly this source and target
 * and no observation; Observed additionally carries a coherent identity (an opaque ID of the conservative character set, supported names
 * equal to the recorded ones ignoring only ASCII case, and a valid instant). A contradictory status, an unknown status, or a mismatched,
 * missing or malformed fact is null: it is never repaired, trimmed, adopted or partially shown.
 */
export function toRepositoryObservation(
  response: GetGitHubRepositoryObservationResponse | null | undefined,
  expected: RepositoryObservationScope,
): RepositoryObservationReading | null {
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

  const identity = toIdentity(observation, expected.target)
  return identity === null ? null : { status: 'Observed', identity }
}

/** Fixed plain-text statements; never host or provider text. */
export const REPOSITORY_OBSERVATION_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown:
    'Unknown: no usable identity was returned for this repository. It may be missing, inaccessible, renamed or transferred, or the GitHub CLI or its stored authentication may be unusable. Nothing is inferred and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was checked.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery and of its recorded target do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  Observed: 'GitHub reported this repository identity at the time shown. It is a point-in-time observation, not a binding:',
} as const
