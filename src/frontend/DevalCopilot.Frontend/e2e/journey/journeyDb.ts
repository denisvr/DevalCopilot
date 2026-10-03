import { join } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { journeyRoot } from './journeyEnv'

// Read-only views of the owned database used to SUPPLEMENT what the page shows. Nothing here ever writes, and nothing in the journey
// seeds or changes workflow state outside the rendered controls. The database is the host's own SQLite file under the owned root.

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
  ExecutionNumber: number
  Status: string
  ExitCode: number | null
  GitCheckpointId: string
  CompletionFingerprintSha256: string | null
}

export interface MessageRow {
  Id: string
  AttemptId: string | null
  Type: string
  InReplyToMessageId: string | null
  Summary: string
  Sequence: number
}

export interface InputRow {
  AttemptId: string
  CollaborationMessageId: string
  Sequence: number
}

export function withJourneyDb<T>(read: (db: DatabaseSync) => T): T {
  const db = new DatabaseSync(join(journeyRoot().root, 'db', 'journey.db'), { readOnly: true })
  try {
    return read(db)
  } finally {
    db.close()
  }
}

export function attempts(): AttemptRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select AttemptNumber, Id, AgentRole, AgentResponseContract, AgentOutcome, Status, AgentBudgetSlot from attempts order by AttemptNumber')
        .all() as unknown as AttemptRow[],
  )
}

/** The raw direct-guidance snapshot recorded on every attempt, by attempt number (null is the unguided snapshot). */
export function directGuidanceByAttempt(): { AttemptNumber: number; AgentDirectHumanGuidance: string | null }[] {
  return withJourneyDb(
    (db) =>
      db.prepare('select AttemptNumber, AgentDirectHumanGuidance from attempts order by AttemptNumber').all() as unknown as {
        AttemptNumber: number
        AgentDirectHumanGuidance: string | null
      }[],
  )
}

export function checkpoints(): CheckpointRow[] {
  return withJourneyDb(
    (db) =>
      db.prepare('select CheckpointNumber, Id, HeadCommitSha, FingerprintSha256 from git_checkpoints order by CheckpointNumber').all() as unknown as CheckpointRow[],
  )
}

export function executions(): ExecutionRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select ExecutionNumber, Status, ExitCode, GitCheckpointId, CompletionFingerprintSha256 from verification_executions order by ExecutionNumber')
        .all() as unknown as ExecutionRow[],
  )
}

export function messages(): MessageRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Id, AttemptId, Type, InReplyToMessageId, Summary, Sequence from collaboration_messages order by Sequence')
        .all() as unknown as MessageRow[],
  )
}

export function inputsOf(attemptId: string): InputRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select AttemptId, CollaborationMessageId, Sequence from attempt_input_messages where AttemptId = ? order by Sequence')
        .all(attemptId) as unknown as InputRow[],
  )
}

export function runLimits(): { MaximumAgentAttempts: number; MaximumReviewCorrectionAttempts: number; Lifecycle: string } {
  return withJourneyDb(
    (db) =>
      db.prepare('select MaximumAgentAttempts, MaximumReviewCorrectionAttempts, Lifecycle from runs').get() as unknown as {
        MaximumAgentAttempts: number
        MaximumReviewCorrectionAttempts: number
        Lifecycle: string
      },
  )
}

export const sameId = (left: string | null | undefined, right: string | null | undefined) =>
  left != null && right != null && left.toLowerCase() === right.toLowerCase()
