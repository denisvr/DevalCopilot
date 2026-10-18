import type { AgentAttemptEvidenceResponse, AgentAttemptHistoryEntryResponse, AgentAttemptHistoryResponse } from '../../api/clients'
import { ARTIFACT_PURPOSE_LABELS } from './artifactPurposes'

/** The page size this inspector asks for; the host defaults to 10, caps a page at 20 and refuses (never clamps) other values. */
export const AGENT_ATTEMPT_HISTORY_PAGE_SIZE = 10

/** What a page was requested with: the run, the exclusive cursor (null: the first page) and the page bound. */
export interface AgentAttemptHistoryRequest {
  runId: string
  beforeAttemptNumber: number | null
  limit: number
}

export interface AgentAttemptHistoryPage {
  entries: readonly AgentAttemptHistoryEntryResponse[]
  nextBeforeAttemptNumber: number | null
}

export type AgentAttemptRole = 'Planner' | 'CriticalReviewer' | 'Resolver' | 'Implementer' | 'CodeReviewer'
export type AgentAttemptProvider = 'Codex' | 'ClaudeCode'
export type AgentAttemptResponseContract =
  | 'Proposal'
  | 'CriticalReview'
  | 'ChallengeResolution'
  | 'ImplementationReport'
  | 'ImplementationReview'
  | 'ReviewCorrection'
  | 'VerificationDiagnosis'

/**
 * The attempt a selected evidence read must answer for: the exact identity and number of the accepted history row and its immutable
 * role, provider and response contract. The recorded status and times are deliberately absent: the attempt may progress between reads.
 */
export interface AgentAttemptIdentityExpectation {
  attemptId: string
  attemptNumber: number
  role: AgentAttemptRole
  provider: AgentAttemptProvider
  responseContract: AgentAttemptResponseContract
}

// The read-side identity rule restated for this view; the Domain (`AgentAttemptIdentity.IsCoherent`, `AttemptStatus` and
// `AgentAttemptContract`) is authoritative, and admitting a new pair or contract requires both to learn it together.
const STATUSES: readonly string[] = ['Running', 'Completed', 'Failed', 'Interrupted']
const SUPPORTED_PAIRS: Readonly<Record<AgentAttemptRole, AgentAttemptProvider>> = {
  Planner: 'Codex',
  Resolver: 'Codex',
  CodeReviewer: 'Codex',
  CriticalReviewer: 'ClaudeCode',
  Implementer: 'ClaudeCode',
}
const CONTRACT_ROLES: Readonly<Record<AgentAttemptResponseContract, AgentAttemptRole>> = {
  Proposal: 'Planner',
  CriticalReview: 'CriticalReviewer',
  ChallengeResolution: 'Resolver',
  ImplementationReport: 'Implementer',
  ImplementationReview: 'CodeReviewer',
  ReviewCorrection: 'Implementer',
  VerificationDiagnosis: 'CodeReviewer',
}

const isRecognizedStatus = (value: unknown): value is string => typeof value === 'string' && STATUSES.includes(value)

/**
 * The verified identity a row or an evidence answer names, or null unless it is coherent: a recognized role whose supported provider is
 * exactly the named one, and a recognized response contract that belongs to that role.
 */
function toVerifiedIdentity(role: unknown, provider: unknown, responseContract: unknown): Omit<AgentAttemptIdentityExpectation, 'attemptId' | 'attemptNumber'> | null {
  if (
    typeof role !== 'string'
    || !Object.hasOwn(SUPPORTED_PAIRS, role)
    || SUPPORTED_PAIRS[role as AgentAttemptRole] !== provider
    || typeof responseContract !== 'string'
    || !Object.hasOwn(CONTRACT_ROLES, responseContract)
    || CONTRACT_ROLES[responseContract as AgentAttemptResponseContract] !== role
  ) {
    return null
  }

  return { role: role as AgentAttemptRole, provider: provider as AgentAttemptProvider, responseContract: responseContract as AgentAttemptResponseContract }
}

/** What evidence for an accepted, verified history row must match; null for a row that offers no evidence (an unverified one). */
export function toAgentAttemptIdentityExpectation(entry: AgentAttemptHistoryEntryResponse): AgentAttemptIdentityExpectation | null {
  const identity = entry.identityValid === true ? toVerifiedIdentity(entry.role, entry.provider, entry.responseContract) : null
  return identity !== null && isText(entry.attemptId) && isPositiveInteger(entry.attemptNumber)
    ? { attemptId: entry.attemptId, attemptNumber: entry.attemptNumber, ...identity }
    : null
}

export type AgentAttemptEvidenceAnswer =
  | { status: 'success'; evidence: AgentAttemptEvidenceResponse }
  // The host could not prove the attempt's persisted identity coherent: nothing else about it is disclosed.
  | { status: 'identity-invalid' }

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isPositiveInteger = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
const isByteLength = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const isAbsent = (value: unknown): boolean => value === null || value === undefined
const isOptionalText = (value: unknown): boolean => isAbsent(value) || typeof value === 'string'
const isOptionalInstant = (value: unknown): boolean => isAbsent(value) || isInstant(value)

/** A repair source is provenance only: absent, or a proved earlier attempt of the same run (an identity, and a lower number when known). */
function isCoherentRepairSource(candidate: Record<string, unknown>, attemptNumber: number): boolean {
  const sourceId = candidate.repairSourceAttemptId
  const sourceNumber = candidate.repairSourceAttemptNumber
  if (isAbsent(sourceId)) {
    return isAbsent(sourceNumber)
  }

  return isText(sourceId) && (isAbsent(sourceNumber) || (isPositiveInteger(sourceNumber) && sourceNumber < attemptNumber))
}

function isCoherentEntry(entry: unknown): entry is AgentAttemptHistoryEntryResponse & { attemptId: string; attemptNumber: number } {
  if (!entry || typeof entry !== 'object') {
    return false
  }

  const candidate = entry as Record<string, unknown>
  if (
    !isText(candidate.attemptId)
    || !isPositiveInteger(candidate.attemptNumber)
    || typeof candidate.identityValid !== 'boolean'
    || !isInstant(candidate.claimedAtUtc)
    || !isOptionalInstant(candidate.dispatchedAtUtc)
    || !isOptionalInstant(candidate.completedAtUtc)
    || !isCoherentRepairSource(candidate, candidate.attemptNumber)
  ) {
    return false
  }

  // A row whose identity the host could not verify carries only its number, times and (when readable) status: the withheld role,
  // provider, contract and outcome must really be absent. A verified row names a recognized status and a coherent identity.
  return candidate.identityValid
    ? isRecognizedStatus(candidate.status)
      && toVerifiedIdentity(candidate.role, candidate.provider, candidate.responseContract) !== null
      && isOptionalText(candidate.outcome)
    : isOptionalText(candidate.status) && isAbsent(candidate.role) && isAbsent(candidate.provider)
      && isAbsent(candidate.responseContract) && isAbsent(candidate.outcome)
}

/**
 * Accepts a host page only when the whole of it is coherent for exactly `request` and for the rows already `retained`: no more entries
 * than requested, unique attempt identities and numbers that are safe positive integers in strictly descending order, every one older
 * than the exclusive cursor and than the last retained row, coherent rows (see above), and a continuation that is a full page whose next
 * cursor is its last number (or none). The response names no run, so the run is proved by the request that the owning lifetime sent.
 * Anything else is null and is never repaired, trimmed or partially accepted.
 */
export function toAgentAttemptHistoryPage(
  response: AgentAttemptHistoryResponse | null | undefined,
  request: AgentAttemptHistoryRequest,
  retained: readonly AgentAttemptHistoryEntryResponse[] = [],
): AgentAttemptHistoryPage | null {
  if (!response || typeof response !== 'object' || typeof response.hasMore !== 'boolean') {
    return null
  }

  const raw: unknown = response.items
  if (!Array.isArray(raw) || raw.length > request.limit) {
    return null
  }

  const attemptIds = new Set(retained.map((entry) => entry.attemptId))
  const numbers = new Set(retained.map((entry) => entry.attemptNumber))
  const last = retained.length > 0 ? retained[retained.length - 1].attemptNumber : undefined
  let ceiling = Math.min(request.beforeAttemptNumber ?? Number.POSITIVE_INFINITY, last ?? Number.POSITIVE_INFINITY)
  const entries: AgentAttemptHistoryEntryResponse[] = []
  for (const item of raw) {
    if (!isCoherentEntry(item) || attemptIds.has(item.attemptId) || numbers.has(item.attemptNumber) || item.attemptNumber >= ceiling) {
      return null
    }

    attemptIds.add(item.attemptId)
    numbers.add(item.attemptNumber)
    ceiling = item.attemptNumber
    entries.push(item)
  }

  const next = response.nextBeforeAttemptNumber
  if (!response.hasMore) {
    return isAbsent(next) ? { entries, nextBeforeAttemptNumber: null } : null
  }

  return entries.length === request.limit && isPositiveInteger(next) && next === entries[entries.length - 1].attemptNumber
    ? { entries, nextBeforeAttemptNumber: next }
    : null
}

function hasCoherentArtifacts(artifacts: unknown): boolean {
  if (!Array.isArray(artifacts)) {
    return false
  }

  const purposes = new Set<string>()
  for (const artifact of artifacts) {
    if (!artifact || typeof artifact !== 'object') {
      return false
    }

    const candidate = artifact as Record<string, unknown>
    const purpose = candidate.purpose
    if (
      typeof purpose !== 'string'
      || !Object.hasOwn(ARTIFACT_PURPOSE_LABELS, purpose)
      || purposes.has(purpose)
      || !isByteLength(candidate.byteLength)
      || !(isAbsent(candidate.truncated) || typeof candidate.truncated === 'boolean')
    ) {
      return false
    }

    purposes.add(purpose)
  }

  return true
}

/**
 * Accepts an evidence answer only for exactly the selected attempt: the same identity and number as the accepted history row, an explicit
 * identity verdict and, for a verified attempt, a recognized status, the same immutable role, provider and response contract as that row
 * (a coherent identity by the same rule), a claim time and a metadata list holding only distinct allowlisted purposes. The status and
 * times are fresh observations and may differ from the page, because the attempt may have progressed. An unverified attempt is reported as such and nothing else of it is used. Anything else is null: none of it is
 * shown and no artifact purpose is offered from it.
 */
export function toAgentAttemptEvidence(
  response: AgentAttemptEvidenceResponse | null | undefined,
  expected: AgentAttemptIdentityExpectation,
): AgentAttemptEvidenceAnswer | null {
  if (
    !response
    || typeof response !== 'object'
    || response.attemptId !== expected.attemptId
    || response.attemptNumber !== expected.attemptNumber
    || typeof response.identityValid !== 'boolean'
  ) {
    return null
  }

  if (!response.identityValid) {
    return { status: 'identity-invalid' }
  }

  const identity = toVerifiedIdentity(response.role, response.provider, response.responseContract)
  return isRecognizedStatus(response.attemptStatus)
    && identity !== null
    && identity.role === expected.role
    && identity.provider === expected.provider
    && identity.responseContract === expected.responseContract
    && isInstant(response.claimedAtUtc)
    && hasCoherentArtifacts(response.artifacts)
    ? { status: 'success', evidence: response }
    : null
}
