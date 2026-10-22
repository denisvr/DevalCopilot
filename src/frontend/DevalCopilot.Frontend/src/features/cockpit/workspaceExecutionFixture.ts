import { toWorkspaceExecutionReading } from './workspaceExecution'
import type { WorkspaceExecutionReading, WorkspaceExecutionState, WorkspaceExecutionStatus } from './workspaceExecution'

// Test fixtures for the workspace execution status read (ADR-0057): complete host answers exactly as the wire carries them, each
// coherent for the status it builds. A test overrides one member to prove that the decoder refuses the incoherent form.

export const PROJECT_ID = '0a1b2c3d-1111-4111-8111-aaaaaaaaaaaa'
export const OTHER_PROJECT_ID = '0a1b2c3d-2222-4222-8222-bbbbbbbbbbbb'
export const WORKSPACE_ID = '0a1b2c3d-3333-4333-8333-cccccccccccc'

export type WireBody = Record<string, unknown>

const REASONS: Readonly<Record<WorkspaceExecutionStatus, string | null>> = {
  NoWorkspace: 'workspace_not_prepared',
  Idle: null,
  AgentAttemptActive: 'agent_attempt_active',
  VerificationActive: 'verification_active',
  CommitReserved: 'local_commit_reserved',
  Conflicted: 'conflicting_execution_owners',
  Unavailable: 'workspace_execution_unavailable',
  PreparationReserved: 'queued_preparation_reserved',
}

/** The one coherent answer of `status` for `projectId`; `NoWorkspace` carries no workspace, every other status the fixture workspace. */
export function wireFor(status: WorkspaceExecutionStatus, projectId: string = PROJECT_ID, overrides: WireBody = {}): WireBody {
  return {
    projectId,
    workspaceId: status === 'NoWorkspace' ? null : WORKSPACE_ID,
    status,
    reasonCode: REASONS[status],
    ...overrides,
  }
}

export const idleWire = (projectId: string = PROJECT_ID): WireBody => wireFor('Idle', projectId)

export function readingOf(status: WorkspaceExecutionStatus, projectId: string = PROJECT_ID): WorkspaceExecutionReading {
  const reading = toWorkspaceExecutionReading(wireFor(status, projectId), projectId)
  if (reading === null) {
    throw new Error('The workspace execution fixture is not a coherent answer.')
  }

  return reading
}

/** What `useWorkspaceExecutionStatus` exposes for a validated reading of `status` (or for no reading at all). */
export function stateFor(
  status: WorkspaceExecutionStatus | null,
  overrides: Partial<WorkspaceExecutionState> = {},
  projectId: string = PROJECT_ID,
): WorkspaceExecutionState {
  return {
    reading: status === null ? null : readingOf(status, projectId),
    loading: false,
    failure: null,
    refresh: () => undefined,
    ...overrides,
  }
}

/**
 * A validated Idle state for `projectId` in the form existing region tests need (their project ids are not canonical identifiers, so
 * this builds the reading directly rather than through the decoder). It is explicit test data for a region composition, never a default
 * of the components: they require the state and withhold starting without it.
 */
export function idleStateFor(projectId: string): WorkspaceExecutionState {
  return {
    reading: { projectId, workspaceId: WORKSPACE_ID, status: 'Idle', reasonCode: null },
    loading: false,
    failure: null,
    refresh: () => undefined,
  }
}
