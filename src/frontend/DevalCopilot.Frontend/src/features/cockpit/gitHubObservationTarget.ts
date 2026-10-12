import type {
  GetGitHubObservationTargetResponse,
  LocalCommitOperationResponse,
  RecordGitHubObservationTargetResponse,
} from '../../api/clients'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

export const OBSERVATION_HOST = 'github.com'

/** The host never records a longer branch name for a local delivery. */
export const MAXIMUM_OBSERVATION_BRANCH_LENGTH = 512

const COMMIT = /^[0-9a-f]{40}$/

/** The exact completed local delivery a scope is selected for. Every fact must be repeated exactly by any answer about it. */
export interface ObservationSource {
  runId: string
  operationId: string
  commitSha: string
  branchName: string
}

/** The immutable Human selection, already proven coherent: the fixed host and the recorded names, nothing derived or defaulted. */
export interface ObservationScope {
  id: string
  eventId: string
  host: typeof OBSERVATION_HOST
  owner: string
  repository: string
  recordedAtUtc: Date
}

/**
 * A reading proven coherent for exactly one source. `NotRecorded` is recordable only when the host itself named this exact source;
 * a host that names no source cannot admit a selection, so nothing may be offered.
 */
export type ObservationReading =
  | { kind: 'NotRecorded'; recordable: boolean }
  | { kind: 'Unavailable' }
  | { kind: 'Available'; target: ObservationScope }

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()

/** The source of an observation: only a Completed operation that carries every identifying fact. Anything else has none. */
export function toObservationSource(runId: string, operation: LocalCommitOperationResponse | null | undefined): ObservationSource | null {
  if (
    !isText(runId)
    || !operation
    || operation.status !== 'Completed'
    || !isText(operation.operationId)
    || typeof operation.commitSha !== 'string'
    || !COMMIT.test(operation.commitSha)
    || !isText(operation.branchName)
    || operation.branchName.length > MAXIMUM_OBSERVATION_BRANCH_LENGTH
  ) {
    return null
  }

  return { runId, operationId: operation.operationId, commitSha: operation.commitSha, branchName: operation.branchName }
}

/** The lifetime key of a source: every identifying fact, so a change of any one is a different owner. */
export const observationSourceKey = (source: ObservationSource): string =>
  JSON.stringify([source.runId, source.operationId, source.commitSha, source.branchName])

function matchesSource(value: unknown, expected: ObservationSource): boolean {
  if (!value || typeof value !== 'object') {
    return false
  }

  const { operationId, commitSha, branchName } = value as Record<string, unknown>
  return sameId(operationId, expected.operationId) && commitSha === expected.commitSha && branchName === expected.branchName
}

function toScope(value: unknown): ObservationScope | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { id, eventId, host, owner, repository, recordedAtUtc } = value as Record<string, unknown>
  if (
    !isText(id)
    || !isText(eventId)
    || host !== OBSERVATION_HOST
    || !isGitHubOwner(owner)
    || !isGitHubRepository(repository)
    || !isInstant(recordedAtUtc)
  ) {
    return null
  }

  return { id, eventId, host: OBSERVATION_HOST, owner, repository, recordedAtUtc }
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0040): the same run, one of the three closed states,
 * and for Available the exact source (operation, commit, branch) with a coherent target of the fixed host and supported names. A
 * NotRecorded answer has no target and a source that is either absent or exactly this one; Unavailable has neither. A contradictory
 * state, an unknown state, a mismatched, missing or malformed fact is null and is never repaired, trimmed or substituted.
 */
export function toObservationReading(
  response: GetGitHubObservationTargetResponse | null | undefined,
  expected: ObservationSource,
): ObservationReading | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.runId)) {
    return null
  }

  const { state, source, target } = response
  switch (state) {
    case 'Unavailable':
      return isAbsent(source) && isAbsent(target) ? { kind: 'Unavailable' } : null
    case 'NotRecorded':
      if (!isAbsent(target) || (!isAbsent(source) && !matchesSource(source, expected))) {
        return null
      }
      return { kind: 'NotRecorded', recordable: !isAbsent(source) }
    case 'Available': {
      const scope = toScope(target)
      return matchesSource(source, expected) && scope !== null ? { kind: 'Available', target: scope } : null
    }
    default:
      return null
  }
}

/**
 * Accepts the recorded answer of a selection only when it is coherent and exactly what was asked: the same run and source, and a
 * target of the fixed host whose names equal the submitted ones. Anything else leaves the outcome unknown (the host may have
 * recorded something), so the caller must read again before recording once more.
 */
export function toRecordedObservation(
  response: RecordGitHubObservationTargetResponse | null | undefined,
  expected: ObservationSource,
  submitted: { owner: string; repository: string },
): ObservationScope | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.runId) || !matchesSource(response.source, expected)) {
    return null
  }

  const scope = toScope(response.target)
  return scope !== null && scope.owner === submitted.owner && scope.repository === submitted.repository ? scope : null
}

/** Fixed plain-text statements; never host or provider text. */
export const OBSERVATION_STATEMENTS = {
  NotRecordedRecordable: 'No GitHub observation target is recorded for this local delivery yet.',
  NotRecordedNotRecordable:
    'No GitHub observation target is recorded, and the host cannot currently admit this delivery as a source, so none can be recorded now.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery and of any recorded target do not agree, so nothing is shown. The cause is not reported and nothing was changed.',
} as const
