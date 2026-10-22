import { hasExactKeys } from './agentAttemptOutputs'

// The closed, whole-answer decoder of the workspace execution status read (ADR-0057) and the pure start gate derived from it. The decoder
// accepts a parsed host answer only when all of it is coherent for exactly the project that was asked about: exactly the four documented
// members, canonical identifiers, a closed status vocabulary and the one tuple (workspace, reason) of that status. Anything else is null
// and is never repaired, trimmed, coerced or partially accepted. A missing member is not an explicit null. What a reading says is only
// what the host's durable ownership read found: never permission, provider readiness or a current source attestation.

export const WORKSPACE_EXECUTION_STATUSES = [
  'NoWorkspace',
  'Idle',
  'AgentAttemptActive',
  'VerificationActive',
  'CommitReserved',
  'Conflicted',
  'Unavailable',
  'PreparationReserved',
] as const
export type WorkspaceExecutionStatus = (typeof WORKSPACE_EXECUTION_STATUSES)[number]

/** The one reason code each status carries (null: none); the closed table of the contract. */
export const WORKSPACE_EXECUTION_REASON_BY_STATUS = {
  NoWorkspace: 'workspace_not_prepared',
  Idle: null,
  AgentAttemptActive: 'agent_attempt_active',
  VerificationActive: 'verification_active',
  CommitReserved: 'local_commit_reserved',
  Conflicted: 'conflicting_execution_owners',
  Unavailable: 'workspace_execution_unavailable',
  PreparationReserved: 'queued_preparation_reserved',
} as const satisfies Readonly<Record<WorkspaceExecutionStatus, string | null>>
export type WorkspaceExecutionReasonCode = Exclude<(typeof WORKSPACE_EXECUTION_REASON_BY_STATUS)[WorkspaceExecutionStatus], null>

export interface WorkspaceExecutionReading {
  projectId: string
  /** The exact latest workspace the host judged; null only for NoWorkspace and for an Unavailable judgment with no known workspace. */
  workspaceId: string | null
  status: WorkspaceExecutionStatus
  reasonCode: WorkspaceExecutionReasonCode | null
}

/**
 * `read`: the request failed. `timeout`: it did not answer within ten seconds. `not-found`: the host no longer knows this project.
 * `invalid`: the answer is not coherent for it.
 */
export type WorkspaceExecutionReadFailure = 'read' | 'timeout' | 'not-found' | 'invalid'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const KEYS = ['projectId', 'reasonCode', 'status', 'workspaceId']

const isGuid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const isStatus = (value: unknown): value is WorkspaceExecutionStatus =>
  typeof value === 'string' && (WORKSPACE_EXECUTION_STATUSES as readonly string[]).includes(value)

/** The whole host answer for exactly `projectId`, or null unless every member and the tuple of its status are coherent. */
export function toWorkspaceExecutionReading(raw: unknown, projectId: string): WorkspaceExecutionReading | null {
  if (!hasExactKeys(raw, KEYS)) {
    return null
  }

  const { status, workspaceId, reasonCode } = raw
  if (!isGuid(raw.projectId) || raw.projectId !== projectId || !isStatus(status) || reasonCode !== WORKSPACE_EXECUTION_REASON_BY_STATUS[status]) {
    return null
  }

  if (workspaceId !== null && !isGuid(workspaceId)) {
    return null
  }

  // NoWorkspace never names a workspace; Idle and every owner status always do; Unavailable names the latest one only when it is known.
  if ((status === 'NoWorkspace' && workspaceId !== null) || (status !== 'NoWorkspace' && status !== 'Unavailable' && workspaceId === null)) {
    return null
  }

  return { projectId, workspaceId, status, reasonCode: WORKSPACE_EXECUTION_REASON_BY_STATUS[status] }
}

/** What the selected project's owned lifetime exposes of the read; the subset a region needs to derive its start gate. */
export interface WorkspaceExecutionState {
  /** The validated reading of the current lifetime; null while nothing was validated and after a failed, timed-out or refused read. */
  reading: WorkspaceExecutionReading | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: WorkspaceExecutionReadFailure | null
  /** Reads the status again now; a callback retained from a replaced or unmounted lifetime does nothing. */
  refresh: () => void
}

export interface WorkspaceExecutionStartGate {
  /**
   * True only for a validated, current Idle reading of exactly the selected project. It never grants an action: every existing
   * eligibility rule still applies, and this value is only ever an additional veto.
   */
  allowsExecutionStart: boolean
  /** The fixed English sentence shown once per region while starting is withheld; null when it is allowed. Never an id or a path. */
  guidance: string | null
}

const AFTER = 'Evidence, history, Refresh and recorded results stay available; refresh the status to read it again.'

/** Fixed copy per status; nothing is taken from the host answer, so an unrecognized value can never be worded as a known one. */
export const WORKSPACE_EXECUTION_GUIDANCE = {
  pending: `Reading this project's workspace execution status. Starting an Agent, repair, planning sequence, verification or local commit is withheld until it is confirmed. ${AFTER}`,
  read: `The workspace execution status could not be read, so starting an Agent, repair, planning sequence, verification or local commit is withheld. ${AFTER}`,
  timeout: `The workspace execution status did not answer within ten seconds, so starting an Agent, repair, planning sequence, verification or local commit is withheld. ${AFTER}`,
  'not-found': `The host no longer knows this project, so starting an Agent, repair, planning sequence, verification or local commit is withheld. ${AFTER}`,
  invalid: `The workspace execution status was not coherent, so starting an Agent, repair, planning sequence, verification or local commit is withheld. ${AFTER}`,
  replaced: `The workspace execution status shown does not belong to the selected project, so starting is withheld until it is read again. ${AFTER}`,
  NoWorkspace: `No isolated workspace is prepared for this project, so nothing can be started in it. Prepare the workspace first. ${AFTER}`,
  AgentAttemptActive: `An Agent attempt is recorded as this workspace's execution owner: it is still running, or it was dispatched and ended without recorded process exit evidence (a local execution sequence keeps such an owner excluded, with no supported recovery action in this version). A recorded owner is not proof of a running process. Inspect it and wait; nothing is cancelled or released here. ${AFTER}`,
  VerificationActive: `A verification is recorded as this workspace's execution owner: it is still running, or it was dispatched and ended without recorded process exit evidence (a local execution sequence keeps such an owner excluded, with no supported recovery action in this version). A recorded owner is not proof of a running process. Inspect it and wait; nothing is cancelled or released here. ${AFTER}`,
  CommitReserved: `A local commit holds this workspace. Inspect it and wait; nothing is cancelled or released here. ${AFTER}`,
  PreparationReserved: `A queued local execution cycle has reserved this workspace for its next step preparation (ADR-0062). The reservation is not an Agent or verification process and nothing has been claimed or dispatched for it. Wait for it to start or end; nothing is cancelled or released here. ${AFTER}`,
  Conflicted: `More than one execution owner is recorded for this workspace, and none is chosen as the winner. Inspect the recorded attempts, verifications and local commit before any further work. ${AFTER}`,
  Unavailable: `Who owns execution in this workspace could not be judged safely, so starting is withheld. Inspect the recorded evidence before any further work. ${AFTER}`,
} as const

/**
 * Whether this project's workspace execution status lets the existing start actions apply (an additional veto only). Only a validated
 * Idle reading of exactly `projectId`, with nothing pending, failed or replaced, allows; every other state, including an absent one, withholds.
 */
export function deriveWorkspaceExecutionStartGate(
  state: WorkspaceExecutionState | null | undefined,
  projectId: string | null,
): WorkspaceExecutionStartGate {
  const withheld = (guidance: string): WorkspaceExecutionStartGate => ({ allowsExecutionStart: false, guidance })
  // An absent state (null, or undefined from a caller that omitted it) is never a permission: it withholds like a pending read.
  if (state === null || state === undefined || projectId === null) {
    return withheld(WORKSPACE_EXECUTION_GUIDANCE.pending)
  }

  if (state.failure !== null) {
    return withheld(failureGuidance(state.failure))
  }

  const { reading } = state
  if (reading === null) {
    return withheld(WORKSPACE_EXECUTION_GUIDANCE.pending)
  }

  if (reading.projectId !== projectId) {
    return withheld(WORKSPACE_EXECUTION_GUIDANCE.replaced)
  }

  if (reading.status === 'Idle') {
    return { allowsExecutionStart: true, guidance: null }
  }

  // An unrecognized status is never worded as a known one: it is the incoherent answer it is.
  return withheld(isStatus(reading.status) ? WORKSPACE_EXECUTION_GUIDANCE[reading.status] : WORKSPACE_EXECUTION_GUIDANCE.invalid)
}

function failureGuidance(failure: WorkspaceExecutionReadFailure): string {
  return Object.hasOwn(WORKSPACE_EXECUTION_GUIDANCE, failure) ? WORKSPACE_EXECUTION_GUIDANCE[failure] : WORKSPACE_EXECUTION_GUIDANCE.invalid
}

/**
 * The short pointer every start control shows while the region's one guidance is displayed, where a control needs its own fixed reason
 * string; the guidance itself is shown once per region, never once per button.
 */
export const WORKSPACE_EXECUTION_WITHHELD_POINTER = 'Withheld: see the workspace execution guidance in this region.'
