import type { GetGitHubBranchPublicationApprovalResponse, RecordGitHubBranchPublicationApprovalResponse } from '../../api/clients'
import { matchesSource, observationSourceKey, toScope } from './gitHubObservationTarget'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'
import { isRepositoryNodeId } from './gitHubRepositoryIdentityBinding'

/** The committed project and the recorded delivery and target an approval is read and recorded for (ADR-0048). */
export interface PublicationApprovalScope {
  projectId: string
  source: ObservationSource
  target: ObservationScope
}

/** The recorded binding the host named for this scope: the opaque repository ID the approval consents to. Historical identity evidence only. */
export interface PublicationBinding {
  id: string
  eventId: string
  repositoryNodeId: string
  owner: string
  repository: string
  observedAtUtc: Date
  recordedAtUtc: Date
}

/** The immutable approval, already proven coherent with the scope and binding: recorded consent only, never an executed push. */
export interface RecordedPublicationApproval {
  id: string
  eventId: string
  workspaceId: string
  approvedAtUtc: Date
}

/**
 * A reading proven coherent for exactly one scope. `NotRecorded` and `Unavailable` carry no fact. `ApprovalNotRecorded` carries the
 * recorded binding that an approval would name, and is the only reading that arms an approval. `Available` carries the recorded consent.
 */
export type PublicationApprovalReading =
  | { kind: 'NotRecorded' }
  | { kind: 'Unavailable' }
  | { kind: 'ApprovalNotRecorded'; binding: PublicationBinding }
  | { kind: 'Available'; binding: PublicationBinding; approval: RecordedPublicationApproval }

/** What one approval request carried; a recorded answer is accepted only when it is exactly this. */
export interface SubmittedPublicationApproval {
  approvalId: string
  bindingId: string
  repositoryNodeId: string
}

export const PUBLICATION_HOST = 'github.com'
export const PUBLICATION_ACTION = 'PushExactCommit'
export const PUBLICATION_POLICY_VERSION = 1

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isAbsent = (value: unknown) => value === null || value === undefined
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const sameId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
// The supported names are ASCII, so this is exactly an ASCII-case-insensitive comparison (never locale or Unicode folding).
const lowerAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const sameNameIgnoringAsciiCase = (left: string, right: string) => left.length === right.length && lowerAscii(left) === lowerAscii(right)

/** The lifetime key of a scope: the project and every identifying fact of the source and the target, so a change of any one is a different owner. */
export const publicationApprovalScopeKey = (scope: PublicationApprovalScope): string =>
  JSON.stringify([
    scope.projectId,
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

function toBinding(value: unknown, expected: ObservationScope): PublicationBinding | null {
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

function toApproval(
  value: unknown,
  expected: PublicationApprovalScope,
  binding: PublicationBinding,
): RecordedPublicationApproval | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const record = value as Record<string, unknown>
  const { id, eventId, workspaceId, approvedAtUtc } = record
  if (
    !isText(id)
    || !isText(eventId)
    || !isText(workspaceId)
    || record.host !== PUBLICATION_HOST
    || record.action !== PUBLICATION_ACTION
    || record.policyVersion !== PUBLICATION_POLICY_VERSION
    || !sameId(record.localCommitOperationId, expected.source.operationId)
    || !sameId(record.targetId, expected.target.id)
    || !sameId(record.bindingId, binding.id)
    || record.repositoryNodeId !== binding.repositoryNodeId
    || record.owner !== expected.target.owner
    || record.repository !== expected.target.repository
    || record.branchName !== expected.source.branchName
    || record.commitSha !== expected.source.commitSha
    || !isInstant(approvedAtUtc)
    || approvedAtUtc.getTime() < binding.recordedAtUtc.getTime()
  ) {
    return null
  }

  return { id, eventId, workspaceId, approvedAtUtc }
}

/**
 * Accepts the whole answer or none of it, for exactly `expected` (ADR-0048): the same run and one of the four closed statuses.
 * NotRecorded and Unavailable carry no source, target, binding or approval; ApprovalNotRecorded carries exactly this source and target
 * and a coherent binding and no approval; Available also carries an approval that repeats the fixed host, action and policy version and
 * every recorded fact exactly (operation, target, binding, node ID, target names, branch, commit) and was approved at or after the binding
 * was recorded. A contradictory status, an unknown status, or a mismatched, missing or malformed fact is null: it is never repaired,
 * trimmed, adopted or partially shown.
 */
export function toPublicationApprovalReading(
  response: GetGitHubBranchPublicationApprovalResponse | null | undefined,
  expected: PublicationApprovalScope,
): PublicationApprovalReading | null {
  if (!response || typeof response !== 'object' || !sameId(response.runId, expected.source.runId)) {
    return null
  }

  const { status, source, target, binding, approval } = response
  switch (status) {
    case 'NotRecorded':
      return isAbsent(source) && isAbsent(target) && isAbsent(binding) && isAbsent(approval) ? { kind: 'NotRecorded' } : null
    case 'Unavailable':
      return isAbsent(source) && isAbsent(target) && isAbsent(binding) && isAbsent(approval) ? { kind: 'Unavailable' } : null
    case 'ApprovalNotRecorded': {
      if (!isAbsent(approval) || !matchesSource(source, expected.source) || !matchesTarget(target, expected.target)) {
        return null
      }

      const recorded = toBinding(binding, expected.target)
      return recorded === null ? null : { kind: 'ApprovalNotRecorded', binding: recorded }
    }
    case 'Available': {
      if (!matchesSource(source, expected.source) || !matchesTarget(target, expected.target)) {
        return null
      }

      const recorded = toBinding(binding, expected.target)
      const consent = recorded === null ? null : toApproval(approval, expected, recorded)
      return recorded === null || consent === null ? null : { kind: 'Available', binding: recorded, approval: consent }
    }
    default:
      return null
  }
}

/**
 * Accepts the answer of an approval only when it is coherent and exactly what was asked: the same run, source and target, the binding and
 * node ID that were submitted, and an approval with the submitted identifier. Anything else leaves the outcome unknown (the host may have
 * recorded something), so the caller must read again before approving once more.
 */
export function toRecordedPublicationApproval(
  response: RecordGitHubBranchPublicationApprovalResponse | null | undefined,
  expected: PublicationApprovalScope,
  submitted: SubmittedPublicationApproval,
): { binding: PublicationBinding; approval: RecordedPublicationApproval } | null {
  if (
    !response
    || typeof response !== 'object'
    || !sameId(response.runId, expected.source.runId)
    || !matchesSource(response.source, expected.source)
    || !matchesTarget(response.target, expected.target)
  ) {
    return null
  }

  const binding = toBinding(response.binding, expected.target)
  if (binding === null || !sameId(binding.id, submitted.bindingId) || binding.repositoryNodeId !== submitted.repositoryNodeId) {
    return null
  }

  const approval = toApproval(response.approval, expected, binding)
  return approval !== null && sameId(approval.id, submitted.approvalId) ? { binding, approval } : null
}

/** Fixed plain-text statements; never host or provider text, and never a promise that anything was or will be executed. */
export const PUBLICATION_STATEMENTS = {
  Consent:
    'Approving records your consent to ONE future normal push of the exact local commit below to the exact branch below, in the repository below. No push occurs now: nothing contacts GitHub and nothing is executed or checked. A pull request, force-push, merge, deletion, rerun and automatic execution are not authorized. A later push needs its own explicit request and fresh checks of ownership, the commit, the remote repository identity, the reference, authentication and permission.',
  NotRecorded:
    'No GitHub branch publication approval is recorded: this delivery has no recorded target and repository binding that can be approved yet.',
  ApprovalNotRecorded: 'No GitHub branch publication approval is recorded for this local delivery yet.',
  Unavailable:
    'Unavailable: the recorded facts of this delivery, its target, its binding or any approval do not agree, so nothing is shown. The cause is not reported and nothing was changed.',
  Available:
    'Recorded consent: this approval was recorded for one future normal push of this exact commit to this exact branch. It is immutable and describes consent only. It is not a push, not readiness or eligibility to push, not a permission check, not a pull request and not a current check of GitHub:',
} as const
