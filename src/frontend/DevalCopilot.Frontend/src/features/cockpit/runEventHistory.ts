import type { GetRunEventHistoryResponse } from '../../api/clients'

/** The page size this region asks for; the host accepts 1 to 50 and never clamps. */
export const RUN_EVENT_HISTORY_PAGE_SIZE = 10

/** The fixed value the host reports for a stored value this version does not recognize. */
export const UNRECOGNIZED = 'Unrecognized'

/** The event types the host admits (the exact `RunEventType` constants); anything else arrives only as `Unrecognized`. */
export const KNOWN_EVENT_TYPES = [
  'run.started',
  'codex.proposal',
  'claude.challenge',
  'codex.resolution',
  'claude.execution',
  'run.completed',
  'collaboration.message_recorded',
  'process.output_captured',
  'process.ended_without_output',
  'agent.attempt_dispatched',
  'agent.attempt_completed',
  'agent.output_recovered',
  'run.codex_assignment_preference_changed',
  'run.claude_model_preference_changed',
  'run.claude_mutation_turn_limit_changed',
  'run.token_warning_threshold_changed',
  'run.token_stop_threshold_changed',
  'run.codex_account_usage_stop_changed',
  'run.codex_account_usage_warning_changed',
  'local_commit.admitted',
  'local_commit.executing',
  'local_commit.completed',
  'local_commit.failed',
  'local_commit.interrupted',
  'local_commit.needs_attention',
  'run.abandoned',
  'run.github_observation_target_recorded',
  'run.github_repository_identity_bound',
  'run.github_branch_publication_approved',
  'planning_sequence.armed',
  'planning_sequence.step_reserved',
  'planning_sequence.step_claimed',
  'planning_sequence.step_concluded',
  'planning_sequence.stop_requested',
  'planning_sequence.ended',
  'local_execution_sequence.armed',
  'local_execution_sequence.step_reserved',
  'local_execution_sequence.step_claimed',
  'local_execution_sequence.step_concluded',
  'local_execution_sequence.stop_requested',
  'local_execution_sequence.ended',
  'local_execution_sequence.admission_queued',
  'local_execution_sequence.admission_waiting',
  'local_execution_sequence.ownership_released',
  'agent_supervision.registered',
  'agent_supervision.phase_entered',
  'agent_supervision.attention',
  'agent_supervision.closed',
] as const

export type KnownEventType = (typeof KNOWN_EVENT_TYPES)[number]
export type RunEventType = KnownEventType | typeof UNRECOGNIZED
export type RunEventActorKind = 'None' | 'Orchestrator' | 'Agent' | 'Human' | typeof UNRECOGNIZED
export type RunEventActorRole = 'Planner' | 'CriticalReviewer' | 'Resolver' | 'Implementer' | 'CodeReviewer'
export type RunEventActorProvider = 'Codex' | 'ClaudeCode'

/** What the host recorded about who produced an event: never inferred, so an Agent may have no recorded role. */
export interface RunEventActor {
  kind: RunEventActorKind
  role: RunEventActorRole | null
  provider: RunEventActorProvider | null
}

/** One recorded event header, already proven coherent for the page that carried it. Nothing of the event's details is carried. */
export interface RunEventHeader {
  runId: string
  id: string
  /** The exact canonical Int64 decimal text; the only ordering authority. Gaps between a run's events are valid. */
  sequence: string
  occurredAtUtc: Date
  eventType: RunEventType
  actor: RunEventActor
}

export interface RunEventHistoryPage {
  entries: readonly RunEventHeader[]
  hasMore: boolean
  /** The exact exclusive cursor of the next older page; present exactly when `hasMore`. */
  nextBeforeSequence: string | null
}

/** What a page was requested with: the run, the exclusive cursor (null: the newest page) and the page bound. */
export interface RunEventHistoryRequest {
  runId: string
  beforeSequence: string | null
  limit: number
}

const MAX_SEQUENCE = (1n << 63n) - 1n
const CANONICAL_SEQUENCE = /^[1-9][0-9]{0,18}$/
const KINDS: readonly string[] = ['None', 'Orchestrator', 'Agent', 'Human', UNRECOGNIZED]
const ROLES: readonly string[] = ['Planner', 'CriticalReviewer', 'Resolver', 'Implementer', 'CodeReviewer']
const PROVIDERS: readonly string[] = ['Codex', 'ClaudeCode']

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())
const absent = (value: unknown) => value === null || value === undefined

/** The exact value of a canonical positive Int64 decimal text, or null for anything else; never a Number. */
function toSequence(value: unknown): bigint | null {
  if (typeof value !== 'string' || !CANONICAL_SEQUENCE.test(value)) {
    return null
  }

  const parsed = BigInt(value)
  return parsed <= MAX_SEQUENCE ? parsed : null
}

function toActor(actor: unknown): RunEventActor | null {
  if (!actor || typeof actor !== 'object') {
    return null
  }

  const { kind, role, provider } = actor as Record<string, unknown>
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
    kind: kind as RunEventActorKind,
    role: (role ?? null) as RunEventActorRole | null,
    provider: (provider ?? null) as RunEventActorProvider | null,
  }
}

function toHeader(entry: unknown, runId: string): { header: RunEventHeader; sequence: bigint } | null {
  if (!entry || typeof entry !== 'object') {
    return null
  }

  const candidate = entry as Record<string, unknown>
  const sequence = toSequence(candidate.sequence)
  const actor = toActor(candidate.actor)
  if (
    candidate.runId !== runId
    || !isText(candidate.id)
    || sequence === null
    || actor === null
    || !isInstant(candidate.occurredAtUtc)
    || typeof candidate.eventType !== 'string'
    || !(candidate.eventType === UNRECOGNIZED || (KNOWN_EVENT_TYPES as readonly string[]).includes(candidate.eventType))
  ) {
    return null
  }

  return {
    header: {
      runId,
      id: candidate.id,
      sequence: candidate.sequence as string,
      occurredAtUtc: candidate.occurredAtUtc,
      eventType: candidate.eventType as RunEventType,
      actor,
    },
    sequence,
  }
}

/**
 * Accepts a host page only when the whole of it is coherent for exactly `request` and for the rows already `retained`: the answered
 * run, a page no larger than requested, nonempty unique event identities, canonical positive Int64 sequence texts in strictly
 * descending order (gaps are valid), every one below the exclusive cursor and the last retained row, valid instants, only known (or
 * fixed Unrecognized) event type and actor disclosures with coherent role and provider, and a continuation that is a full page whose
 * next cursor is exactly its last sequence (or none). Sequences are compared as exact integers, never as Numbers. Anything else is
 * null and is never repaired, trimmed or partially accepted.
 */
export function toRunEventHistoryPage(
  response: GetRunEventHistoryResponse | null | undefined,
  request: RunEventHistoryRequest,
  retained: readonly RunEventHeader[] = [],
): RunEventHistoryPage | null {
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

  const entries: RunEventHeader[] = []
  for (const item of raw) {
    const decoded = toHeader(item, request.runId)
    if (!decoded || ids.has(decoded.header.id) || decoded.sequence >= ceiling) {
      return null
    }

    ids.add(decoded.header.id)
    ceiling = decoded.sequence
    entries.push(decoded.header)
  }

  const next = response.nextBeforeSequence
  if (!response.hasMore) {
    return absent(next) ? { entries, hasMore: false, nextBeforeSequence: null } : null
  }

  return entries.length === request.limit && toSequence(next) !== null && next === entries[entries.length - 1].sequence
    ? { entries, hasMore: true, nextBeforeSequence: next as string }
    : null
}

const EVENT_TYPE_LABELS: Record<KnownEventType, string> = {
  'run.started': 'Run started',
  'codex.proposal': 'Codex proposal recorded',
  'claude.challenge': 'Claude challenge recorded',
  'codex.resolution': 'Codex resolution recorded',
  'claude.execution': 'Claude execution recorded',
  'run.completed': 'Run completed',
  'collaboration.message_recorded': 'Collaboration message recorded',
  'process.output_captured': 'Process output captured',
  'process.ended_without_output': 'Process ended without output',
  'agent.attempt_dispatched': 'Agent attempt dispatched',
  'agent.attempt_completed': 'Agent attempt completed',
  'agent.output_recovered': 'Agent output recovered',
  'run.codex_assignment_preference_changed': 'Codex assignment preference changed',
  'run.claude_model_preference_changed': 'Claude model preference changed',
  'run.claude_mutation_turn_limit_changed': 'Claude turn limit changed',
  'run.token_warning_threshold_changed': 'Token warning threshold changed',
  'run.token_stop_threshold_changed': 'Token stop threshold changed',
  'run.codex_account_usage_stop_changed': 'Codex account-usage stop changed',
  'run.codex_account_usage_warning_changed': 'Codex account-usage warning changed',
  'local_commit.admitted': 'Local commit admitted',
  'local_commit.executing': 'Local commit executing',
  'local_commit.completed': 'Local commit completed',
  'local_commit.failed': 'Local commit failed',
  'local_commit.interrupted': 'Local commit interrupted',
  'local_commit.needs_attention': 'Local commit needs attention',
  'run.abandoned': 'Run abandoned',
  'run.github_observation_target_recorded': 'GitHub observation target recorded',
  'run.github_repository_identity_bound': 'GitHub repository identity bound',
  'run.github_branch_publication_approved': 'GitHub branch publication approved (consent recorded, not pushed)',
  'planning_sequence.armed': 'Planning sequence armed',
  'planning_sequence.step_reserved': 'Planning step reserved',
  'planning_sequence.step_claimed': 'Planning step claimed',
  'planning_sequence.step_concluded': 'Planning step concluded',
  'planning_sequence.stop_requested': 'Planning sequence stop requested',
  'planning_sequence.ended': 'Planning sequence ended',
  'local_execution_sequence.armed': 'Local execution cycle authorized',
  'local_execution_sequence.step_reserved': 'Local execution operation reserved',
  'local_execution_sequence.step_claimed': 'Local execution operation claimed',
  'local_execution_sequence.step_concluded': 'Local execution operation concluded',
  'local_execution_sequence.stop_requested': 'Local execution stop requested',
  'local_execution_sequence.ended': 'Local execution sequence ended',
  'local_execution_sequence.admission_queued': 'Queued local execution step recorded',
  'local_execution_sequence.admission_waiting': 'Queued local execution step waiting reason recorded',
  'local_execution_sequence.ownership_released': 'Local execution quarantine released by a Human',
  'agent_supervision.registered': 'Agent supervision registered (host observation)',
  'agent_supervision.phase_entered': 'Agent supervision phase entered (host observation)',
  'agent_supervision.attention': 'Agent supervision attention (host observation)',
  'agent_supervision.closed': 'Agent supervision closed (host observation)',
}

const ROLE_LABELS: Record<RunEventActorRole, string> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical reviewer',
  Resolver: 'Resolver',
  Implementer: 'Implementer',
  CodeReviewer: 'Code reviewer',
}

const PROVIDER_LABELS: Record<RunEventActorProvider, string> = { Codex: 'Codex', ClaudeCode: 'Claude Code' }

/** The fixed label of a recorded event type; a value this version does not recognize is never worded as a known one. */
export const eventTypeLabel = (eventType: string): string =>
  eventType === UNRECOGNIZED ? 'Unrecognized event type' : (EVENT_TYPE_LABELS[eventType as KnownEventType] ?? 'Unrecognized event type')

/** Words an actor only from what was recorded: no role, provider or authority is ever supplied. */
export function actorLabel(actor: RunEventActor): string {
  switch (actor.kind) {
    case 'None':
      return 'No actor recorded'
    case 'Unrecognized':
      return 'Unrecognized actor'
    case 'Agent':
      return `Agent · ${actor.role === null ? 'role not recorded' : `${ROLE_LABELS[actor.role]} role`} · ${PROVIDER_LABELS[actor.provider as RunEventActorProvider]}`
    default:
      return actor.kind
  }
}
