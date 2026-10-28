import type { LocalCommitInspectionFailure } from './hooks/useLocalCommitInspection'
import type { LocalCommitInspectionKnownReasonCode, LocalCommitInspectionOperationStatus } from './localCommitInspection'

// Fixed English copy of the recorded local-commit inspection (ADR-0070). Nothing here is taken from a host answer or an exception: each
// text is chosen from a closed code, so an unrecognized value can never be worded as a known one. Every description is historical (what
// the host recorded), never a live claim about a process, the repository, the workspace or publication.

export const LOCAL_COMMIT_INSPECTION_REGION_LABEL = 'Inspect recorded local commit'
export const LOCAL_COMMIT_INSPECTION_HIDE_LABEL = 'Hide recorded local commit'
export const LOCAL_COMMIT_INSPECTION_RELOAD_LABEL = 'Reload recorded local commit'
export const LOCAL_COMMIT_INSPECTION_READING = 'Reading the recorded local commit of this run…'

export const LOCAL_COMMIT_INSPECTION_NOTE = [
  'This reads the local commit operation the host recorded for this run, if one exists, when you asked. It offers no request, retry, recovery, release, approval, commit, publication or export action.',
  'Every value below is what was recorded. It says nothing about whether a process is running now, what the repository, branch or workspace look like now, whether this run could request a commit now, or whether anything was published to a remote.',
  'The record is internally consistent within one database read, not signed or authenticated history. This read, the live local commit panel and any delivery receipt are read separately and are not one atomic replay.',
] as const

export const LOCAL_COMMIT_INSPECTION_FAILURES: Readonly<Record<LocalCommitInspectionFailure, string>> = {
  read: 'The recorded local commit could not be read. Nothing was changed.',
  'not-found': 'The host no longer knows this run. Nothing was changed.',
  timeout: 'The host did not answer in time, so no recorded local commit is shown. Reload to read it again.',
  invalid: 'The host answered with a recorded local commit that is not coherent for this run, so none of it is shown.',
}

export const LOCAL_COMMIT_INSPECTION_NOT_RECORDED =
  'This read found no local commit operation associated with this run. That says nothing about whether this run is eligible for a local commit, whether a request was ever made, or whether anything was delivered.'

export const LOCAL_COMMIT_INSPECTION_UNAVAILABLE =
  'A local commit operation is associated with this run, but its recorded facts could not be proved as a whole, so none of it is shown. That is neither a success nor a failure, and it is not diagnosed, repaired or recovered here.'

export const LOCAL_COMMIT_INSPECTION_STATUS_LABEL: Readonly<Record<LocalCommitInspectionOperationStatus, string>> = {
  Prepared: 'Prepared',
  Executing: 'Executing',
  Completed: 'Completed',
  Failed: 'Failed',
  Interrupted: 'Interrupted',
  NeedsAttention: 'Needs attention',
}

export const LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION: Readonly<Record<LocalCommitInspectionOperationStatus, string>> = {
  Prepared:
    'The host recorded the prepared facts of this commit before any branch change. Prepared is a recorded marker, not an observation that anything is running or about to run.',
  Executing:
    'The host recorded the single-use execution marker before it first changed the branch or index. Executing is a recorded marker, not an observation of a running process; no later outcome was recorded.',
  Completed:
    'The host recorded this local commit as completed. That is a recorded operation result, not a new delivery receipt, a live Git observation, proof that the source still matches, or remote publication.',
  Failed:
    'The host recorded that this commit definitely did not promote: the branch and index were proven unchanged. A failed record does not permit or perform a retry.',
  Interrupted:
    'The host recorded a startup interruption proven not to have promoted this commit. An interrupted record does not permit or perform a retry.',
  NeedsAttention:
    'The host recorded an ambiguous result: it could neither prove the commit promoted nor prove it did not. Needs attention is not terminal, not a success, not a failure and not permission to retry, and nothing here diagnoses or recovers it.',
}

export const LOCAL_COMMIT_INSPECTION_NO_START =
  'No execution marker was recorded: this outcome was recorded without one, so no start time is shown or implied.'

export const LOCAL_COMMIT_INSPECTION_PREPARATION_NOTE =
  'The parent commit and tree are recorded preparation facts of the commit the host intended; they are not proof that anything was promoted.'

export const LOCAL_COMMIT_INSPECTION_COMMIT_NOTE =
  'The commit identity is shown only for a completed operation, as the recorded result. It is not a live Git observation, and it says nothing about remote publication.'

export const LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE =
  'No commit identity is shown: it is disclosed only for a completed operation, so a commit that was only proposed at preparation is never presented as a result.'

export const LOCAL_COMMIT_INSPECTION_REASON_NONE = 'No reason was recorded.'

export const LOCAL_COMMIT_INSPECTION_REASON_UNRECOGNIZED =
  'A reason was recorded that this version does not describe. Its text is not shown.'

export const LOCAL_COMMIT_INSPECTION_REASON_EXPLANATION: Readonly<Record<LocalCommitInspectionKnownReasonCode, string>> = {
  'local_commit.authority_changed': 'The host recorded that the approval authority changed.',
  'local_commit.git_unprovable': 'The host recorded that it could not prove the repository state.',
  'local_commit.ownership_unprovable': 'The host recorded that it could not prove its ownership of the workspace.',
  'local_commit.head_not_bound': 'The host recorded that HEAD did not name the owned branch, or that the branch tip could not be read.',
  'local_commit.prepared_state_unexpected': 'The host recorded that the prepared state was unexpected.',
  'local_commit.unpromoted_state_unknown': 'The host recorded that it could not prove whether the commit was promoted.',
  'local_commit.branch_tip_unrecognized': 'The host recorded that the branch tip was not recognized.',
  'local_commit.unknown_index_lock': 'The host recorded an index lock it did not recognize.',
  'local_commit.index_state_unrecognized': 'The host recorded an index state it did not recognize.',
  'local_commit.unknown_reference_lock': 'The host recorded a reference lock it did not recognize.',
  'local_commit.reference_lock_unproven': 'The host recorded that it could not prove the state of the reference lock.',
  'local_commit.interrupted_before_execution': 'The host recorded an interruption before execution.',
  'local_commit.interrupted_unpromoted': 'The host recorded an interruption with the commit proven not promoted.',
}
