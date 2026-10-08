import { join } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { journeyRoot } from './journeyEnv.ts'

// Read-only views of the owned database used to SUPPLEMENT what the page shows. Nothing here ever writes, and nothing in the journey
// seeds or changes workflow state outside the rendered controls. The database is the host's own SQLite file under the owned root.
//
// Every journey of a run shares this one database and host. Each journey therefore reads ONLY the rows of its own project, its own run
// and its own workspace (see JourneyData): no assertion depends on a table being otherwise empty, on row order across journeys, or on
// which journey ran first.

export interface AttemptRow {
  AttemptNumber: number
  Id: string
  AgentRole: string
  AgentResponseContract: string
  AgentOutcome: string | null
  Status: string
  AgentBudgetSlot: number
}

export interface CheckpointRow {
  CheckpointNumber: number
  Id: string
  HeadCommitSha: string
  FingerprintSha256: string
}

export interface ExecutionRow {
  Id: string
  VerificationCommandId: string
  ExecutionNumber: number
  Status: string
  ExitCode: number | null
  GitCheckpointId: string
  CompletionFingerprintSha256: string | null
}

export interface CheckpointReviewRow {
  Id: string
  GitCheckpointId: string
  CheckpointNumber: number
  CheckpointFingerprintSha256: string
  ActorKind: string
  Decision: string
}

export interface CheckpointReviewEvidenceRow {
  CheckpointReviewId: string
  VerificationCommandId: string
  VerificationExecutionId: string
  VerificationExecutionNumber: number
  VerificationExecutionCheckpointFingerprintSha256: string
  VerificationExecutionStatus: string
  VerificationExecutionExitCode: number | null
}

export interface MessageRow {
  Id: string
  AttemptId: string | null
  Type: string
  InReplyToMessageId: string | null
  Summary: string
  Sequence: number
  StructuredContentJson: string
  Provenance: string
}

export interface InputRow {
  AttemptId: string
  CollaborationMessageId: string
  Sequence: number
}

export interface PlanningAuthorizationRow {
  Id: string
  EscalationMessageId: string
  FinalProposalMessageId: string
  HumanInstructionMessageId: string
  ConsumedByAttemptId: string | null
}

export interface RunLimitsRow {
  MaximumAgentAttempts: number
  MaximumReviewCorrectionAttempts: number
  Lifecycle: string
}

export function withJourneyDb<T>(read: (db: DatabaseSync) => T): T {
  const db = new DatabaseSync(join(journeyRoot().root, 'db', 'journey.db'), { readOnly: true })
  try {
    return read(db)
  } finally {
    db.close()
  }
}

export const sameId = (left: string | null | undefined, right: string | null | undefined) =>
  left != null && right != null && left.toLowerCase() === right.toLowerCase()

/**
 * Read-only access to the rows of ONE journey: the project it registered by name, that project's single run, and the single workspace
 * prepared for it. The identities are looked up on every call (they only exist once the rendered controls created them), so a journey
 * can call these at any point after the run was recorded and never sees another journey's attempts, messages, checkpoints, executions
 * or reviews, however many other journeys share the database.
 */
export class JourneyData {
  readonly projectName: string

  constructor(projectName: string) {
    this.projectName = projectName
  }

  private one<T>(sql: string, ...parameters: (string | number)[]): T {
    const row = withJourneyDb((db) => db.prepare(sql).all(...parameters)) as unknown as T[]
    if (row.length !== 1) {
      throw new Error(`Expected exactly one row for the journey scope, found ${row.length}.`)
    }
    return row[0]
  }

  private all<T>(sql: string, ...parameters: (string | number)[]): T[] {
    return withJourneyDb((db) => db.prepare(sql).all(...parameters)) as unknown as T[]
  }

  projectId(): string {
    return this.one<{ Id: string }>('select Id from projects where Name = ?', this.projectName).Id
  }

  runId(): string {
    return this.one<{ Id: string }>('select Id from runs where ProjectId = ?', this.projectId()).Id
  }

  workspace(): { Id: string; WorkspacePath: string } {
    return this.one<{ Id: string; WorkspacePath: string }>('select Id, WorkspacePath from git_workspaces where ProjectId = ?', this.projectId())
  }

  attempts(): AttemptRow[] {
    return this.all<AttemptRow>(
      'select AttemptNumber, Id, AgentRole, AgentResponseContract, AgentOutcome, Status, AgentBudgetSlot from attempts where RunId = ? order by AttemptNumber',
      this.runId(),
    )
  }

  /** The raw direct-guidance snapshot recorded on every attempt, by attempt number (null is the unguided snapshot). */
  directGuidanceByAttempt(): { AttemptNumber: number; AgentDirectHumanGuidance: string | null }[] {
    return this.all('select AttemptNumber, AgentDirectHumanGuidance from attempts where RunId = ? order by AttemptNumber', this.runId())
  }

  /** The raw model-limits snapshot recorded on every attempt, by attempt number (null is the attempt that recorded none). */
  modelContextLimitsByAttempt(): { AttemptNumber: number; AgentModelContextLimitsSnapshot: string | null }[] {
    return this.all(
      'select AttemptNumber, AgentModelContextLimitsSnapshot from attempts where RunId = ? order by AttemptNumber',
      this.runId(),
    )
  }

  /** The run-scoped Codex account-usage stop exactly as stored (an INTEGER; null when disabled). */
  runAccountUsageStop(): number | null {
    return this.one<{ CodexAccountUsageStopPercent: number | null }>(
      'select CodexAccountUsageStopPercent from runs where Id = ?',
      this.runId(),
    ).CodexAccountUsageStopPercent
  }

  /** How many times the human changed the account-usage stop (events of this run), by their recorded payloads in order. */
  accountUsageStopEvents(): string[] {
    return this.all<{ PayloadJson: string }>(
      "select PayloadJson from events where RunId = ? and EventType = 'run.codex_account_usage_stop_changed' order by Sequence",
      this.runId(),
    ).map((row) => row.PayloadJson)
  }

  /** The raw account-usage facts on every attempt, by attempt number: dispatch marker, threshold snapshot and decision text. */
  accountUsageByAttempt(): {
    AttemptNumber: number
    AgentDispatchedAtUtc: string | null
    AgentCodexAccountUsageStopPercent: number | null
    AgentAccountUsageDecisionSnapshot: string | null
  }[] {
    return this.all(
      'select AttemptNumber, AgentDispatchedAtUtc, AgentCodexAccountUsageStopPercent, AgentAccountUsageDecisionSnapshot from attempts where RunId = ? order by AttemptNumber',
      this.runId(),
    )
  }

  checkpoints(): CheckpointRow[] {
    return this.all<CheckpointRow>(
      'select CheckpointNumber, Id, HeadCommitSha, FingerprintSha256 from git_checkpoints where WorkspaceId = ? order by CheckpointNumber',
      this.workspace().Id,
    )
  }

  executions(): ExecutionRow[] {
    return this.all<ExecutionRow>(
      'select Id, VerificationCommandId, ExecutionNumber, Status, ExitCode, GitCheckpointId, CompletionFingerprintSha256 from verification_executions where ProjectId = ? order by ExecutionNumber',
      this.projectId(),
    )
  }

  checkpointReviews(): CheckpointReviewRow[] {
    return this.all<CheckpointReviewRow>(
      'select Id, GitCheckpointId, CheckpointNumber, CheckpointFingerprintSha256, ActorKind, Decision from checkpoint_reviews where ProjectId = ? order by RecordedAtUtcTicks',
      this.projectId(),
    )
  }

  checkpointReviewEvidence(): CheckpointReviewEvidenceRow[] {
    return this.all<CheckpointReviewEvidenceRow>(
      'select e.CheckpointReviewId, e.VerificationCommandId, e.VerificationExecutionId, e.VerificationExecutionNumber, e.VerificationExecutionCheckpointFingerprintSha256, e.VerificationExecutionStatus, e.VerificationExecutionExitCode from checkpoint_review_evidence e join checkpoint_reviews r on r.Id = e.CheckpointReviewId where r.ProjectId = ? order by e.VerificationExecutionNumber',
      this.projectId(),
    )
  }

  /** The enabled recipes of the project in the host's own CommandNumber order. */
  recipes(): { Id: string; CommandNumber: number; Name: string; IsEnabled: number }[] {
    return this.all(
      'select Id, CommandNumber, Name, IsEnabled from verification_commands where ProjectId = ? order by CommandNumber',
      this.projectId(),
    )
  }

  /** The verification members the explicit local commit recorded for this run's operation, in their recorded sequence. */
  localCommitVerificationMembers(): { Sequence: number; SubjectId: string; CommandId: string }[] {
    return this.all(
      "select m.Sequence, m.SubjectId, m.CommandId from local_commit_authority_members m join local_commit_operations o on o.Id = m.OperationId where o.RunId = ? and m.Kind = 'Verification' order by m.Sequence",
      this.runId(),
    )
  }

  messages(): MessageRow[] {
    return this.all<MessageRow>(
      'select Id, AttemptId, Type, InReplyToMessageId, Summary, Sequence, StructuredContentJson, Provenance from collaboration_messages where RunId = ? order by Sequence',
      this.runId(),
    )
  }

  inputsOf(attemptId: string): InputRow[] {
    return this.all<InputRow>(
      'select i.AttemptId, i.CollaborationMessageId, i.Sequence from attempt_input_messages i join attempts a on a.Id = i.AttemptId where a.RunId = ? and i.AttemptId = ? order by i.Sequence',
      this.runId(),
      attemptId,
    )
  }

  planningAuthorizations(): PlanningAuthorizationRow[] {
    return this.all<PlanningAuthorizationRow>(
      'select Id, EscalationMessageId, FinalProposalMessageId, HumanInstructionMessageId, ConsumedByAttemptId from planning_implementation_authorizations where RunId = ?',
      this.runId(),
    )
  }

  /** The sealed context manifest artifacts of this run's attempts, by attempt (identity, hash, length and the host's own relative path; never the content). */
  manifestArtifacts(): { AttemptId: string; ContentHash: string; ByteLength: number; RelativeStoragePath: string }[] {
    return this.all(
      "select AttemptId, ContentHash, ByteLength, RelativeStoragePath from artifacts where RunId = ? and Purpose = 'AgentContextManifest' order by AttemptId",
      this.runId(),
    )
  }

  /** The run-wide Agent claim ceiling and reserved-time ceiling exactly as stored (the time as its INTEGER tick count; null when none). */
  runBudgets(): { MaximumAgentAttempts: number; MaximumAgentInvocationTime: number | null } {
    return this.one('select MaximumAgentAttempts, MaximumAgentInvocationTime from runs where Id = ?', this.runId())
  }

  runLimits(): RunLimitsRow {
    return this.one<RunLimitsRow>('select MaximumAgentAttempts, MaximumReviewCorrectionAttempts, Lifecycle from runs where Id = ?', this.runId())
  }
}
