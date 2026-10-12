import type { GetCollaborationHistoryAttemptResponse } from '../../api/clients'
import { KNOWN_PROTOCOL_VERSION } from './collaborationHistory'
import type { CollaborationHistoryEntry, CollaborationParticipantProvider, CollaborationParticipantRole } from './collaborationHistory'

/** The closed attribution states the host reports for one message's referenced Agent attempt. */
export type CollaborationAttemptStatus = 'NoAgentAttempt' | 'Unavailable' | 'Available'

export type AttemptResponseContract =
  | 'Proposal'
  | 'CriticalReview'
  | 'ChallengeResolution'
  | 'ImplementationReport'
  | 'ImplementationReview'
  | 'ReviewCorrection'
  | 'VerificationDiagnosis'

export type RecordedAttemptStatus = 'Running' | 'Completed' | 'Failed' | 'Interrupted'

/** The minimal recorded header of an attempt: recorded attribution only, never current execution, outcome or authority. */
export interface CollaborationAttemptHeader {
  id: string
  attemptNumber: number
  role: CollaborationParticipantRole
  provider: CollaborationParticipantProvider
  responseContract: AttemptResponseContract
  status: RecordedAttemptStatus
  claimedAtUtc: Date
  dispatchedAtUtc: Date | null
  completedAtUtc: Date | null
}

/** The observed facts of the one history row whose attempt attribution is requested; every decoded answer is checked against them. */
export interface CollaborationAttemptSource {
  runId: string
  messageId: string
  /** The exact canonical Int64 decimal text of the source as the validated history page showed it. */
  sequence: string
  type: CollaborationHistoryEntry['type']
  protocolVersion: CollaborationHistoryEntry['protocolVersion']
  provenance: CollaborationHistoryEntry['provenance']
  actor: CollaborationHistoryEntry['actor']
}

export type CollaborationAttempt =
  | { status: 'NoAgentAttempt' }
  | { status: 'Unavailable' }
  | { status: 'Available'; attempt: CollaborationAttemptHeader }

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/
const MAX_ATTEMPT_NUMBER = 2_147_483_647

const ROLES: readonly string[] = ['Planner', 'CriticalReviewer', 'Resolver', 'Implementer', 'CodeReviewer']
const PROVIDERS: readonly string[] = ['Codex', 'ClaudeCode']
const STATUSES: readonly string[] = ['Running', 'Completed', 'Failed', 'Interrupted']

/** The role each response contract belongs to, restated for this view (the Domain contract table is authoritative). */
const CONTRACT_ROLES: Record<AttemptResponseContract, CollaborationParticipantRole> = {
  Proposal: 'Planner',
  CriticalReview: 'CriticalReviewer',
  ChallengeResolution: 'Resolver',
  ImplementationReport: 'Implementer',
  ImplementationReview: 'CodeReviewer',
  ReviewCorrection: 'Implementer',
  VerificationDiagnosis: 'CodeReviewer',
}

/** The role/provider pairs the product launches. */
const SUPPORTED_PAIRS: readonly (readonly [string, string])[] = [
  ['Planner', 'Codex'],
  ['Resolver', 'Codex'],
  ['CodeReviewer', 'Codex'],
  ['CriticalReviewer', 'ClaudeCode'],
  ['Implementer', 'ClaudeCode'],
]

const KNOWN_TYPES: readonly string[] = [
  'Proposal',
  'Acceptance',
  'Challenge',
  'Question',
  'Decision',
  'ExecutionReport',
  'ReviewFinding',
  'RevisionResponse',
  'Escalation',
  'ReviewApproval',
  'HumanInstruction',
]
const NON_PROVIDER_PROVENANCES: readonly string[] = ['Simulated', 'HostConstructed', 'HumanSubmitted']

const absent = (value: unknown) => value === null || value === undefined
const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isContract = (value: unknown): value is AttemptResponseContract =>
  typeof value === 'string' && Object.prototype.hasOwnProperty.call(CONTRACT_ROLES, value)

function exactSequence(value: unknown): boolean {
  return typeof value === 'string' && CANONICAL_SEQUENCE.test(value) && BigInt(value) <= MAX_SEQUENCE
}

const isRecognizedSource = (source: CollaborationAttemptSource) =>
  source.protocolVersion === KNOWN_PROTOCOL_VERSION && KNOWN_TYPES.includes(source.type)

function toHeader(value: unknown, source: CollaborationAttemptSource): CollaborationAttemptHeader | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const header = value as Record<string, unknown>
  const { id, attemptNumber, role, provider, responseContract, status, claimedAtUtc, dispatchedAtUtc, completedAtUtc } = header
  if (
    !isText(id)
    || typeof attemptNumber !== 'number'
    || !Number.isInteger(attemptNumber)
    || attemptNumber < 1
    || attemptNumber > MAX_ATTEMPT_NUMBER
    || typeof role !== 'string'
    || !ROLES.includes(role)
    || typeof provider !== 'string'
    || !PROVIDERS.includes(provider)
    || !SUPPORTED_PAIRS.some(([supportedRole, supportedProvider]) => supportedRole === role && supportedProvider === provider)
    || !isContract(responseContract)
    || CONTRACT_ROLES[responseContract] !== role
    || typeof status !== 'string'
    || !STATUSES.includes(status)
    || !isInstant(claimedAtUtc)
    || !(absent(dispatchedAtUtc) || isInstant(dispatchedAtUtc))
    || !(absent(completedAtUtc) || isInstant(completedAtUtc))
  ) {
    return null
  }

  // The attempt must be the one the observed provider-observed Agent actor claims to be.
  if (source.actor.kind !== 'Agent' || source.actor.role !== role || source.actor.provider !== provider) {
    return null
  }

  return {
    id,
    attemptNumber,
    role: role as CollaborationParticipantRole,
    provider: provider as CollaborationParticipantProvider,
    responseContract,
    status: status as RecordedAttemptStatus,
    claimedAtUtc,
    dispatchedAtUtc: isInstant(dispatchedAtUtc) ? dispatchedAtUtc : null,
    completedAtUtc: isInstant(completedAtUtc) ? completedAtUtc : null,
  }
}

/**
 * Accepts a host answer only when the whole of it is coherent for exactly `source`: the answered run and message, the observed exact
 * sequence text, a closed status and a header shape that agrees with it (an attempt only for Available). `NoAgentAttempt` needs a
 * recognized version-1.0 source of a known type whose provenance is recognized and not provider-observed; `Available` needs a
 * recognized provider-observed source with a recognized Agent actor and a header whose identity, positive bounded number, supported
 * role/provider pair, contract-to-role agreement, status and timestamps are valid and whose role and provider are exactly the
 * source's actor. `Unavailable` is always admissible and names no target. Anything else is null and is never repaired or partially
 * accepted.
 */
export function toCollaborationAttempt(
  response: GetCollaborationHistoryAttemptResponse | null | undefined,
  source: CollaborationAttemptSource,
): CollaborationAttempt | null {
  if (
    !response
    || typeof response !== 'object'
    || !exactSequence(source.sequence)
    || response.runId !== source.runId
    || response.messageId !== source.messageId
    || response.sourceSequence !== source.sequence
  ) {
    return null
  }

  switch (response.status) {
    case 'NoAgentAttempt':
      return absent(response.attempt) && isRecognizedSource(source) && NON_PROVIDER_PROVENANCES.includes(source.provenance)
        ? { status: 'NoAgentAttempt' }
        : null
    case 'Unavailable':
      return absent(response.attempt) ? { status: 'Unavailable' } : null
    case 'Available': {
      if (absent(response.attempt) || !isRecognizedSource(source) || source.provenance !== 'ProviderObserved') {
        return null
      }

      const header = toHeader(response.attempt, source)
      return header === null ? null : { status: 'Available', attempt: header }
    }
    default:
      return null
  }
}

const ROLE_LABELS: Record<CollaborationParticipantRole, string> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical reviewer',
  Resolver: 'Resolver',
  Implementer: 'Implementer',
  CodeReviewer: 'Code reviewer',
}

const PROVIDER_LABELS: Record<CollaborationParticipantProvider, string> = { Codex: 'Codex', ClaudeCode: 'Claude Code' }

const CONTRACT_LABELS: Record<AttemptResponseContract, string> = {
  Proposal: 'Proposal',
  CriticalReview: 'Critical review',
  ChallengeResolution: 'Challenge resolution',
  ImplementationReport: 'Implementation report',
  ImplementationReview: 'Implementation review',
  ReviewCorrection: 'Review correction',
  VerificationDiagnosis: 'Verification diagnosis',
}

export const attemptRoleLabel = (role: CollaborationParticipantRole): string => ROLE_LABELS[role]
export const attemptProviderLabel = (provider: CollaborationParticipantProvider): string => PROVIDER_LABELS[provider]
export const attemptContractLabel = (contract: AttemptResponseContract): string => CONTRACT_LABELS[contract]
