import type { GetCollaborationHistoryResponse } from '../../api/clients'

/** The page size this region asks for (the host default is 10; it accepts 1 to 20 and never clamps). */
export const COLLABORATION_HISTORY_PAGE_SIZE = 5

/** The fixed value the host reports for a stored value this version does not recognize. */
export const UNRECOGNIZED = 'Unrecognized'

/** The only protocol version whose content this view can show. */
export const KNOWN_PROTOCOL_VERSION = '1.0'

/** The host bounds the summary and each field value; a longer one is never accepted, truncated or repaired. */
export const MAX_SUMMARY_LENGTH = 600
export const MAX_FIELD_VALUE_LENGTH = 900

/** The closed structured-content schema of every message type: field names with their fixed labels, in the order they are shown. */
export const MESSAGE_SCHEMAS = {
  Proposal: [
    ['scope', 'Scope'],
    ['implementationSteps', 'Implementation steps'],
    ['risks', 'Risks'],
    ['verificationPlan', 'Verification plan'],
    ['escalationPoints', 'Escalation points'],
  ],
  Acceptance: [['rationale', 'Rationale']],
  Challenge: [
    ['disputedItem', 'Disputed item'],
    ['materialImpact', 'Material impact'],
    ['reasoning', 'Reasoning'],
    ['alternativeOrQuestion', 'Alternative or question'],
  ],
  Question: [
    ['question', 'Question'],
    ['why', 'Why it matters'],
  ],
  Decision: [
    ['resolution', 'Resolution'],
    ['rationale', 'Rationale'],
    ['resultingPlanChanges', 'Resulting plan changes'],
    ['nextAction', 'Next action'],
  ],
  ExecutionReport: [
    ['completedWork', 'Completed work'],
    ['verification', 'Verification'],
  ],
  ReviewFinding: [
    ['severity', 'Severity'],
    ['category', 'Category'],
    ['evidence', 'Evidence'],
    ['requiredChange', 'Required change'],
  ],
  RevisionResponse: [
    ['disposition', 'Disposition'],
    ['evidence', 'Evidence'],
    ['resultingSourceChanges', 'Resulting source changes'],
  ],
  Escalation: [
    ['unresolvedDecision', 'Unresolved decision'],
    ['options', 'Options'],
    ['consequences', 'Consequences'],
    ['evidence', 'Evidence'],
    ['recommendedChoice', 'Recommended choice'],
  ],
  ReviewApproval: [
    ['rationale', 'Rationale'],
    ['residualRisks', 'Residual risks'],
  ],
  HumanInstruction: [
    ['instruction', 'Instruction'],
    ['rationale', 'Rationale'],
  ],
} as const satisfies Record<string, readonly (readonly [string, string])[]>

export type KnownMessageType = keyof typeof MESSAGE_SCHEMAS
export type CollaborationMessageType = KnownMessageType | typeof UNRECOGNIZED
export type CollaborationProvenance = 'Simulated' | 'ProviderObserved' | 'HostConstructed' | 'HumanSubmitted' | typeof UNRECOGNIZED
export type CollaborationParticipantKind = 'Orchestrator' | 'Agent' | 'Human' | typeof UNRECOGNIZED
export type CollaborationParticipantRole = 'Planner' | 'CriticalReviewer' | 'Resolver' | 'Implementer' | 'CodeReviewer'
export type CollaborationParticipantProvider = 'Codex' | 'ClaudeCode'

/** What the host recorded about one side of a message: never inferred, so an Agent may have no recorded role. */
export interface CollaborationParticipant {
  kind: CollaborationParticipantKind
  role: CollaborationParticipantRole | null
  provider: CollaborationParticipantProvider | null
}

export interface CollaborationField {
  name: string
  label: string
  value: string
}

/** One recorded message, already proven coherent for the page that carried it. Unavailable content carries no text at all. */
export interface CollaborationHistoryEntry {
  runId: string
  id: string
  /** The exact canonical Int64 decimal text; the only ordering authority. Gaps between a run's messages are valid. */
  sequence: string
  occurredAtUtc: Date
  protocolVersion: typeof KNOWN_PROTOCOL_VERSION | typeof UNRECOGNIZED
  type: CollaborationMessageType
  provenance: CollaborationProvenance
  actor: CollaborationParticipant
  recipient: CollaborationParticipant
  content: { status: 'Available'; summary: string; fields: readonly CollaborationField[] } | { status: 'Unavailable' }
}

export interface CollaborationHistoryPage {
  entries: readonly CollaborationHistoryEntry[]
  hasMore: boolean
  /** The exact exclusive cursor of the next older page; present exactly when `hasMore`. */
  nextBeforeSequence: string | null
}

/** What a page was requested with: the run, the exclusive cursor (null: the newest page) and the page bound. */
export interface CollaborationHistoryRequest {
  runId: string
  beforeSequence: string | null
  limit: number
}

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/
const KINDS: readonly string[] = ['Orchestrator', 'Agent', 'Human', UNRECOGNIZED]
const ROLES: readonly string[] = ['Planner', 'CriticalReviewer', 'Resolver', 'Implementer', 'CodeReviewer']
const PROVIDERS: readonly string[] = ['Codex', 'ClaudeCode']
const PROVENANCES: readonly string[] = ['Simulated', 'ProviderObserved', 'HostConstructed', 'HumanSubmitted', UNRECOGNIZED]

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const absent = (value: unknown) => value === null || value === undefined
const isKnownType = (value: unknown): value is KnownMessageType =>
  typeof value === 'string' && Object.prototype.hasOwnProperty.call(MESSAGE_SCHEMAS, value)

/** The exact value of a canonical positive Int64 decimal text, or null for anything else; never a Number. */
function toSequence(value: unknown): bigint | null {
  if (typeof value !== 'string' || !CANONICAL_SEQUENCE.test(value)) {
    return null
  }

  const parsed = BigInt(value)
  return parsed <= MAX_SEQUENCE ? parsed : null
}

function toParticipant(participant: unknown): CollaborationParticipant | null {
  if (!participant || typeof participant !== 'object') {
    return null
  }

  const { kind, role, provider } = participant as Record<string, unknown>
  if (typeof kind !== 'string' || !KINDS.includes(kind) || !(absent(role) || (typeof role === 'string' && ROLES.includes(role)))) {
    return null
  }

  if (!(absent(provider) || (typeof provider === 'string' && PROVIDERS.includes(provider)))) {
    return null
  }

  // Only an Agent may carry a provider (it requires one) or a role; every other kind, including the unrecognized one, carries neither.
  if (kind === 'Agent' ? absent(provider) : !absent(role) || !absent(provider)) {
    return null
  }

  return {
    kind: kind as CollaborationParticipantKind,
    role: (role ?? null) as CollaborationParticipantRole | null,
    provider: (provider ?? null) as CollaborationParticipantProvider | null,
  }
}

/**
 * The content of one entry. Unavailable content must be bare (no summary, no field). Available content requires every disclosure
 * to be recognized, a bounded nonempty summary, and exactly the fields of the type's closed schema, each once with a bounded nonempty
 * string value; the fields are returned in the schema's own order with their fixed labels. Anything else is null: a whole-page error.
 */
function toContent(
  candidate: Record<string, unknown>,
  type: CollaborationMessageType,
  recognized: boolean,
): CollaborationHistoryEntry['content'] | null {
  const fields = candidate.fields
  if (candidate.contentStatus === 'Unavailable') {
    return absent(candidate.summary) && Array.isArray(fields) && fields.length === 0 ? { status: 'Unavailable' } : null
  }

  if (candidate.contentStatus !== 'Available' || !recognized || !isKnownType(type) || !Array.isArray(fields)) {
    return null
  }

  const summary = candidate.summary
  if (!isText(summary) || summary.length > MAX_SUMMARY_LENGTH) {
    return null
  }

  const schema = MESSAGE_SCHEMAS[type]
  if (fields.length !== schema.length) {
    return null
  }

  const byName = new Map<string, string>()
  for (const field of fields) {
    if (!field || typeof field !== 'object') {
      return null
    }

    const { name, value } = field as Record<string, unknown>
    if (
      typeof name !== 'string'
      || byName.has(name)
      || !schema.some(([expected]) => expected === name)
      || !isText(value)
      || value.length > MAX_FIELD_VALUE_LENGTH
    ) {
      return null
    }

    byName.set(name, value)
  }

  return {
    status: 'Available',
    summary,
    fields: schema.map(([name, label]) => ({ name, label, value: byName.get(name) as string })),
  }
}

/**
 * What the host requires before it calls content Available: distinct participants, and an actor that agrees with how the message
 * entered the ledger (provider-observed needs an Agent with a recorded role, host-constructed an Orchestrator, human-submitted a
 * Human; simulated facts constrain nothing further). A page that says otherwise contradicts itself and is never shown.
 */
function isCoherentEnvelope(actor: CollaborationParticipant, recipient: CollaborationParticipant, provenance: string): boolean {
  if (actor.kind === recipient.kind && actor.role === recipient.role && actor.provider === recipient.provider) {
    return false
  }

  switch (provenance) {
    case 'ProviderObserved':
      return actor.kind === 'Agent' && actor.role !== null
    case 'HostConstructed':
      return actor.kind === 'Orchestrator'
    case 'HumanSubmitted':
      return actor.kind === 'Human'
    default:
      return true
  }
}

export function toCollaborationHistoryEntry(entry: unknown, runId: string): { entry: CollaborationHistoryEntry; sequence: bigint } | null {
  if (!entry || typeof entry !== 'object') {
    return null
  }

  const candidate = entry as Record<string, unknown>
  const sequence = toSequence(candidate.sequence)
  const actor = toParticipant(candidate.actor)
  const recipient = toParticipant(candidate.recipient)
  const { type, provenance, protocolVersion } = candidate
  if (
    candidate.runId !== runId
    || !isText(candidate.id)
    || sequence === null
    || actor === null
    || recipient === null
    || !isInstant(candidate.occurredAtUtc)
    || !(type === UNRECOGNIZED || isKnownType(type))
    || typeof provenance !== 'string'
    || !PROVENANCES.includes(provenance)
    || !(protocolVersion === KNOWN_PROTOCOL_VERSION || protocolVersion === UNRECOGNIZED)
  ) {
    return null
  }

  const recognized =
    protocolVersion === KNOWN_PROTOCOL_VERSION
    && type !== UNRECOGNIZED
    && provenance !== UNRECOGNIZED
    && actor.kind !== UNRECOGNIZED
    && recipient.kind !== UNRECOGNIZED
    && isCoherentEnvelope(actor, recipient, provenance)
  const content = toContent(candidate, type, recognized)
  if (content === null) {
    return null
  }

  return {
    entry: {
      runId,
      id: candidate.id,
      sequence: candidate.sequence as string,
      occurredAtUtc: candidate.occurredAtUtc,
      protocolVersion,
      type,
      provenance: provenance as CollaborationProvenance,
      actor,
      recipient,
      content,
    },
    sequence,
  }
}

/**
 * Accepts a host page only when the whole of it is coherent for exactly `request` and for the rows already `retained`: the answered
 * run, a page no larger than requested, nonempty unique message identities, canonical positive Int64 sequence texts in strictly
 * descending order (gaps are valid), every one below the exclusive cursor and the last retained row, valid instants, only closed (or
 * fixed Unrecognized) disclosures with coherent role and provider, content that is either bare-unavailable or complete against the
 * closed schema of its type within the host's bounds, and a continuation that is a full page whose next cursor is exactly its last
 * sequence (or none). Sequences are compared as exact integers, never as Numbers. A well-formed Unavailable entry is kept; anything
 * else is null and is never repaired, trimmed or partially accepted.
 */
export function toCollaborationHistoryPage(
  response: GetCollaborationHistoryResponse | null | undefined,
  request: CollaborationHistoryRequest,
  retained: readonly CollaborationHistoryEntry[] = [],
): CollaborationHistoryPage | null {
  if (!response || typeof response !== 'object' || response.runId !== request.runId || typeof response.hasMore !== 'boolean') {
    return null
  }

  const raw = response.entries
  if (!Array.isArray(raw) || raw.length > request.limit) {
    return null
  }

  const ids = new Set(retained.map((entry) => entry.id))
  let ceiling = request.beforeSequence === null ? MAX_SEQUENCE + 1n : (toSequence(request.beforeSequence) ?? 0n)
  if (retained.length > 0) {
    const last = BigInt(retained[retained.length - 1].sequence)
    ceiling = last < ceiling ? last : ceiling
  }

  const entries: CollaborationHistoryEntry[] = []
  for (const item of raw) {
    const decoded = toCollaborationHistoryEntry(item, request.runId)
    if (!decoded || ids.has(decoded.entry.id) || decoded.sequence >= ceiling) {
      return null
    }

    ids.add(decoded.entry.id)
    ceiling = decoded.sequence
    entries.push(decoded.entry)
  }

  const next = response.nextBeforeSequence
  if (!response.hasMore) {
    return absent(next) ? { entries, hasMore: false, nextBeforeSequence: null } : null
  }

  return entries.length === request.limit && toSequence(next) !== null && next === entries[entries.length - 1].sequence
    ? { entries, hasMore: true, nextBeforeSequence: next as string }
    : null
}

const TYPE_LABELS: Record<KnownMessageType, string> = {
  Proposal: 'Proposal',
  Acceptance: 'Acceptance',
  Challenge: 'Challenge',
  Question: 'Question',
  Decision: 'Decision',
  ExecutionReport: 'Execution report',
  ReviewFinding: 'Review finding',
  RevisionResponse: 'Revision response',
  Escalation: 'Escalation',
  ReviewApproval: 'Review approval',
  HumanInstruction: 'Human instruction',
}

const PROVENANCE_LABELS: Record<Exclude<CollaborationProvenance, typeof UNRECOGNIZED>, string> = {
  Simulated: 'Simulated',
  ProviderObserved: 'Provider-observed',
  HostConstructed: 'Host-constructed',
  HumanSubmitted: 'Human-submitted',
}

const ROLE_LABELS: Record<CollaborationParticipantRole, string> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical reviewer',
  Resolver: 'Resolver',
  Implementer: 'Implementer',
  CodeReviewer: 'Code reviewer',
}

const PROVIDER_LABELS: Record<CollaborationParticipantProvider, string> = { Codex: 'Codex', ClaudeCode: 'Claude Code' }

/** The fixed label of a recorded message type; a value this version does not recognize is never worded as a known one. */
export const messageTypeLabel = (type: string): string => (isKnownType(type) ? TYPE_LABELS[type] : 'Unrecognized message type')

export const provenanceLabel = (provenance: string): string =>
  Object.prototype.hasOwnProperty.call(PROVENANCE_LABELS, provenance) ? PROVENANCE_LABELS[provenance as keyof typeof PROVENANCE_LABELS] : 'Unrecognized provenance'

export const protocolVersionLabel = (version: string): string =>
  version === KNOWN_PROTOCOL_VERSION ? `Protocol ${KNOWN_PROTOCOL_VERSION}` : 'Unrecognized protocol version'

/** Words a participant only from what was recorded: no role, provider or authority is ever supplied. */
export function participantLabel(participant: CollaborationParticipant): string {
  switch (participant.kind) {
    case 'Unrecognized':
      return 'Unrecognized participant'
    case 'Agent':
      return `Agent · ${participant.role === null ? 'role not recorded' : `${ROLE_LABELS[participant.role]} role`} · ${PROVIDER_LABELS[participant.provider as CollaborationParticipantProvider]}`
    default:
      return participant.kind
  }
}
