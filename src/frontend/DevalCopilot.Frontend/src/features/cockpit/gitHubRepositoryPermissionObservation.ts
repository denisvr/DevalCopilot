import type { GetGitHubRepositoryPermissionObservationResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The exact recorded local delivery and the immutable target the repository permission is observed for (ADR-0040, ADR-0049). */
export interface PermissionObservationScope {
  source: ObservationSource
  target: ObservationScope
}

/** The immutable Human confirmation of the repository identity (ADR-0043) this observation is compared with. */
export interface PermissionBindingFacts {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/** The repository identity GitHub returned (equal to the bound ID, names equal to the recorded ones ignoring only ASCII case) and the host's observation time. */
export interface PermissionObservedFacts {
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
}

/** The six literal values of GitHub's `RepositoryPermission`, exactly as GitHub names them. Nothing ranks, translates or derives from them. */
export const PERMISSION_LITERALS = ['READ', 'TRIAGE', 'TRIAGE_PLUS', 'WRITE', 'MAINTAIN', 'ADMIN'] as const

export type PermissionLiteral = (typeof PERMISSION_LITERALS)[number]

/**
 * A reading proven coherent for exactly one scope (ADR-0049). The first three statuses carry no binding; CliUnavailable, Unknown and
 * IdentityChanged carry the bound identity and no observation; PermissionNotReturned carries an observation with no permission (an explicit
 * null from GitHub, which is neither a denial nor proof of an account type); PermissionReported carries the observation and GitHub's literal
 * role. The repository access GitHub reported at one moment: never permission to push this branch, readiness, an approval or a publication
 * authority.
 */
export type PermissionObservationReading =
  | { status: 'NotRecorded' | 'Unavailable' | 'BindingNotRecorded' }
  | { status: 'CliUnavailable' | 'Unknown' | 'IdentityChanged'; binding: PermissionBindingFacts }
  | { status: 'PermissionNotReturned'; binding: PermissionBindingFacts; facts: PermissionObservedFacts }
  | { status: 'PermissionReported'; binding: PermissionBindingFacts; facts: PermissionObservedFacts; viewerPermission: PermissionLiteral }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/
const STATUSES: readonly string[] = [
  'NotRecorded',
  'Unavailable',
  'BindingNotRecorded',
  'CliUnavailable',
  'Unknown',
  'IdentityChanged',
  'PermissionNotReturned',
  'PermissionReported',
]

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isNodeId = (value: unknown): value is string => typeof value === 'string' && NODE_ID.test(value)
const isPermissionLiteral = (value: unknown): value is PermissionLiteral => (PERMISSION_LITERALS as readonly unknown[]).includes(value)
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

/** The lifetime key of a scope: every identifying fact of the source and the target, so a change of any one is a different owner. */
export const permissionObservationScopeKey = (scope: PermissionObservationScope): string =>
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

function toBinding(value: unknown, expected: ObservationScope): PermissionBindingFacts | null {
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
  binding: PermissionBindingFacts,
): (PermissionObservedFacts & { viewerPermission: unknown }) | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { repositoryNodeId, owner, repository, observedAtUtc, viewerPermission } = value as Record<string, unknown>
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

  return { repositoryNodeId, owner, repository, observedAtUtc, viewerPermission }
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0049): the same run and one of the eight closed statuses with exactly
 * its documented facts. NotRecorded and Unavailable carry nothing; BindingNotRecorded carries exactly this source and target; every other
 * status also carries a coherent binding (the conservative opaque ID, supported names equal to the recorded ones ignoring only ASCII
 * case, instants ordered target, observation, binding); CliUnavailable, Unknown and IdentityChanged no observation. An observation must
 * carry the bound repository ID exactly and a time not before the binding. PermissionNotReturned carries a permission that is literally
 * null (an undefined or omitted member is refused); PermissionReported carries exactly one of the six case-sensitive literals. A contradictory or unknown status, an unknown literal, or a mismatched, missing
 * or malformed fact is null: it is never repaired, trimmed, normalized, ranked, adopted or partially shown.
 */
export function toPermissionObservation(
  response: GetGitHubRepositoryPermissionObservationResponse | null | undefined,
  expected: PermissionObservationScope,
): PermissionObservationReading | null {
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

  const { viewerPermission, ...rest } = facts
  if (status === 'PermissionNotReturned') {
    // GitHub's null is a reported fact. An undefined or omitted member (JSON has no undefined) is a malformed answer, never that fact.
    return viewerPermission === null ? { status, binding: bound, facts: rest } : null
  }

  return status === 'PermissionReported' && isPermissionLiteral(viewerPermission)
    ? { status, binding: bound, facts: rest, viewerPermission }
    : null
}

/** Fixed plain-text statements; never host or provider text. */
export const PERMISSION_OBSERVATION_STATEMENTS = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown:
    'Unknown: no usable answer was returned for this repository. The repository may be missing, inaccessible, renamed or transferred, or the GitHub CLI, its stored authentication or the answer may be unusable, or the host clock was unusable. Nothing is inferred and nothing was changed.',
  IdentityChanged:
    'The repository ID GitHub returned now differs from the repository ID bound for this recorded target, so no repository access is shown. Nothing is inferred about why, and nothing was changed.',
  NotRecorded: 'The host no longer reports a recorded GitHub observation target for this local delivery, so nothing was checked.',
  BindingNotRecorded:
    'No repository identity is bound for this recorded target, so nothing was checked. A check needs the immutable identity binding to compare with.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, of its recorded target or of its identity binding do not agree, or changed while GitHub was being queried, so nothing is shown. The cause is not reported and nothing was changed.',
  PermissionNotReturned:
    'GitHub returned no repository permission for the stored default authentication at the time shown. That is not a denial and it does not show what kind of account or token was used. The repository identity GitHub returned at the time shown:',
  PermissionReported:
    'GitHub reported this repository permission for the stored default authentication at the time shown, for the bound repository:',
  Disclosure: 'Repository access does not establish permission to push this branch.',
} as const
