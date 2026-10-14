import type { BindGitHubRepositoryIdentityResponse, GetGitHubRepositoryIdentityBindingResponse } from '../../api/clients'
import { matchesSource, toScope } from './gitHubObservationTarget'
import type { ObservationScope } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'
import type { RepositoryObservationScope } from './gitHubRepositoryObservation'

/** The immutable Human confirmation (ADR-0043): historical identity evidence, never a fresh remote check or any authority. */
export interface RepositoryIdentityBinding {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/**
 * A reading proven coherent for exactly one scope. `NotRecorded` is bindable only when the host itself named this exact source and
 * target; a host that names none cannot admit a binding, so nothing may be offered. `Available` carries the recorded binding.
 */
export type RepositoryIdentityBindingReading =
  | { kind: 'NotRecorded'; bindable: boolean }
  | { kind: 'Unavailable' }
  | { kind: 'Available'; binding: RepositoryIdentityBinding }

const NODE_ID = /^[A-Za-z0-9_\-/+=]{1,256}$/

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

export const isRepositoryNodeId = (value: unknown): value is string => typeof value === 'string' && NODE_ID.test(value)

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

function toBinding(value: unknown, expected: ObservationScope): RepositoryIdentityBinding | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc } = value as Record<string, unknown>
  if (
    !isText(id)
    || !isText(eventId)
    || !isRepositoryNodeId(repositoryNodeId)
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

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0043): the same run and one of the three closed states.
 * Unavailable carries no source, target or binding; NotRecorded carries no binding and either neither or exactly this source and target;
 * Available carries exactly this source and target and a coherent binding (an opaque ID of the conservative character set, supported names
 * equal to the recorded ones ignoring only ASCII case, valid instants in the order target, observation, binding). A contradictory state,
 * an unknown state, or a mismatched, missing or malformed fact is null: it is never repaired, trimmed, adopted or partially shown.
 */
export function toBindingReading(
  response: GetGitHubRepositoryIdentityBindingResponse | null | undefined,
  expected: RepositoryObservationScope,
): RepositoryIdentityBindingReading | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.source.runId)) {
    return null
  }

  const { state, source, target, binding } = response
  switch (state) {
    case 'Unavailable':
      return isAbsent(source) && isAbsent(target) && isAbsent(binding) ? { kind: 'Unavailable' } : null
    case 'NotRecorded':
      if (!isAbsent(binding)) {
        return null
      }
      if (isAbsent(source) && isAbsent(target)) {
        return { kind: 'NotRecorded', bindable: false }
      }
      return matchesSource(source, expected.source) && matchesTarget(target, expected.target) ? { kind: 'NotRecorded', bindable: true } : null
    case 'Available': {
      if (!matchesSource(source, expected.source) || !matchesTarget(target, expected.target)) {
        return null
      }

      const recorded = toBinding(binding, expected.target)
      return recorded === null ? null : { kind: 'Available', binding: recorded }
    }
    default:
      return null
  }
}

/**
 * Accepts the answer of a binding only when it is coherent and exactly what was asked: the same run, source and target, and a binding
 * whose node ID equals the submitted one and whose names equal the recorded target's ignoring only ASCII case. Anything else leaves the
 * outcome unknown (the host may have recorded something), so the caller must read again before binding once more.
 */
export function toBoundIdentity(
  response: BindGitHubRepositoryIdentityResponse | null | undefined,
  expected: RepositoryObservationScope,
  submittedNodeId: string,
): RepositoryIdentityBinding | null {
  if (
    !response
    || typeof response !== 'object'
    || !sameId(response.runId, expected.source.runId)
    || !matchesSource(response.source, expected.source)
    || !matchesTarget(response.target, expected.target)
  ) {
    return null
  }

  const recorded = toBinding(response.binding, expected.target)
  return recorded !== null && recorded.repositoryNodeId === submittedNodeId ? recorded : null
}

/** Fixed plain-text statements; never host or provider text. */
export const BINDING_STATEMENTS = {
  NotRecordedBindable: 'No repository identity is bound for this recorded target yet.',
  NotRecordedNotBindable:
    'No repository identity is bound, and the host cannot currently admit this recorded target as a binding scope, so none can be bound now.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, of its recorded target or of any binding do not agree, so nothing is shown. The cause is not reported and nothing was changed.',
  Available:
    'This repository identity was bound for this recorded target and is immutable. It is historical evidence of what was confirmed then, not a current check of GitHub:',
  Disclosure:
    'Binding records, once and immutably, the repository ID that GitHub reported just now for exactly the recorded owner and repository. It is historical identity evidence only: not a permission, a branch or commit proof, a CI result, publication authority or an alias for any other repository. The host observes the identity again when you bind, and refuses if it differs from the one shown.',
} as const
