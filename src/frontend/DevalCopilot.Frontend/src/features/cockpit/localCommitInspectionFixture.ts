/**
 * Test-only builders of the parsed recorded local-commit answers the host writes (ADR-0070). They write exactly the members the contract
 * names with explicit nulls, and every member can be replaced through a patch to build a contradiction. The defaults describe a
 * Completed operation of the standard run.
 */
type Raw = Record<string, unknown>

const guid = (letter: string, index: number): string => `${letter.repeat(8)}-0000-4000-8000-${index.toString(16).padStart(12, '0')}`

export const LOCAL_COMMIT_RUN = guid('a', 1)
export const LOCAL_COMMIT_OTHER_RUN = guid('a', 2)
export const LOCAL_COMMIT_PROJECT = guid('b', 1)
export const LOCAL_COMMIT_OPERATION = guid('c', 1)
export const LOCAL_COMMIT_WORKSPACE = guid('d', 1)
export const LOCAL_COMMIT_CHECKPOINT = guid('e', 1)
export const LOCAL_COMMIT_FINGERPRINT = 'f'.repeat(64)
export const LOCAL_COMMIT_PARENT = '1'.repeat(40)
export const LOCAL_COMMIT_TREE = '2'.repeat(40)
export const LOCAL_COMMIT_RESULT = '3'.repeat(40)

export const CREATED = '2026-10-20T09:00:00.1234567Z'
export const STARTED = '2026-10-20T09:00:10.1234567Z'
export const COMPLETED = '2026-10-20T09:00:20.1234567Z'

export const NO_REASON = { kind: 'None', code: null }

export const localCommitInspectionOperationFor = (patch: Raw = {}): Raw => ({
  operationId: LOCAL_COMMIT_OPERATION,
  projectId: LOCAL_COMMIT_PROJECT,
  workspaceId: LOCAL_COMMIT_WORKSPACE,
  checkpointId: LOCAL_COMMIT_CHECKPOINT,
  checkpointNumber: 2,
  checkpointFingerprintSha256: LOCAL_COMMIT_FINGERPRINT,
  status: 'Completed',
  branchName: 'devalcopilot/workspace/x/1',
  parentCommitSha: LOCAL_COMMIT_PARENT,
  treeSha: LOCAL_COMMIT_TREE,
  commitSha: LOCAL_COMMIT_RESULT,
  changedPathCount: 3,
  totalBytes: 1234,
  createdAtUtc: CREATED,
  executionStartedAtUtc: STARTED,
  completedAtUtc: COMPLETED,
  reason: NO_REASON,
  ...patch,
})

/** One supported operation per recorded status, each with its exact time, reason and commit-identity tuple. */
export const localCommitInspectionOperationOf = (status: string, patch: Raw = {}): Raw => {
  switch (status) {
    case 'Prepared':
      return localCommitInspectionOperationFor({ status, commitSha: null, executionStartedAtUtc: null, completedAtUtc: null, ...patch })
    case 'Executing':
      return localCommitInspectionOperationFor({ status, commitSha: null, completedAtUtc: null, ...patch })
    case 'Failed':
    case 'Interrupted':
      return localCommitInspectionOperationFor({
        status,
        commitSha: null,
        reason: { kind: 'Known', code: status === 'Failed' ? 'local_commit.git_unprovable' : 'local_commit.interrupted_unpromoted' },
        ...patch,
      })
    case 'NeedsAttention':
      return localCommitInspectionOperationFor({
        status,
        commitSha: null,
        completedAtUtc: null,
        reason: { kind: 'Known', code: 'local_commit.unpromoted_state_unknown' },
        ...patch,
      })
    default:
      return localCommitInspectionOperationFor({ status, ...patch })
  }
}

export const recordedLocalCommitInspectionFor = (patch: Raw = {}, operation: Raw = {}): Raw => ({
  runId: LOCAL_COMMIT_RUN,
  status: 'Recorded',
  operation: localCommitInspectionOperationFor(operation),
  ...patch,
})

export const recordedLocalCommitInspectionOf = (status: string, patch: Raw = {}, operation: Raw = {}): Raw => ({
  runId: LOCAL_COMMIT_RUN,
  status: 'Recorded',
  operation: localCommitInspectionOperationOf(status, operation),
  ...patch,
})

export const notRecordedLocalCommitInspectionFor = (patch: Raw = {}): Raw => ({
  runId: LOCAL_COMMIT_RUN,
  status: 'NotRecorded',
  operation: null,
  ...patch,
})

export const unavailableLocalCommitInspectionFor = (patch: Raw = {}): Raw => ({
  runId: LOCAL_COMMIT_RUN,
  status: 'Unavailable',
  operation: null,
  ...patch,
})
