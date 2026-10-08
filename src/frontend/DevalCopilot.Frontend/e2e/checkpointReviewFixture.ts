import { randomUUID } from 'node:crypto'
import { DatabaseSync } from 'node:sqlite'
import {
  CaptureGitWorkspaceCheckpointEndpointClient,
  GetProjectWorkspaceEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL, SMOKE_DB_PATH } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { createFixtureRepository } from './support'
import { authorizedHttp } from './planningAuthorizationFixture'

// RAW-SQL EVIDENCE FIXTURE, NOT PRODUCTION-WRITTEN: owned metadata for the wire specification of the manual checkpoint review.
// The project, workspace, lease and checkpoint are created through the PUBLIC operations of the real host. Only the rows no
// public operation can create without a real verification process are inserted into the run-owned database: two enabled
// verification recipes and one terminal Passed and one terminal Failed (or, on request, Passed) execution bound to that one
// checkpoint and to the prepared workspace's own path. No provider and no verification process is ever started.

const NOW = '2026-10-01 12:00:00+00:00'

export interface CheckpointReviewFixture {
  projectId: string
  checkpointId: string
  checkpointFingerprint: string
  passedExecutionId: string
  failedExecutionId: string
  unitCommandId: string
  lintCommandId: string
}

export interface CheckpointReviewFixtureOptions {
  /** The second recipe's terminal execution: Failed (the default) or Passed, which makes the enabled set complete. */
  lintStatus?: 'Failed' | 'Passed'
}

export async function createCheckpointReviewFixture(
  name: string,
  options: CheckpointReviewFixtureOptions = {},
): Promise<CheckpointReviewFixture> {
  const projects = new RegisterProjectEndpointClient(API_BASE_URL, authorizedHttp)
  // The fixture repository is built exactly once, before the retry loop: a second `git commit` of unchanged content fails.
  const repositoryPath = createFixtureRepository(name)
  const projectId = (
    await retryOnGitUnavailable(
      () => projects.registerProject(new RegisterProjectRequest({ name: `${name} fixture`, path: repositoryPath })),
      { attempts: 45, delayMs: 1_000 },
    )
  ).projectId!
  const workspaceId = (
    await new PrepareRepositoryWorkspaceEndpointClient(API_BASE_URL, authorizedHttp).prepareRepositoryWorkspace(projectId)
  ).workspaceId!
  const checkpoint = await new CaptureGitWorkspaceCheckpointEndpointClient(API_BASE_URL, authorizedHttp).captureGitWorkspaceCheckpoint(projectId)
  const checkpointId = checkpoint.checkpointId!
  const fingerprint = checkpoint.fingerprintSha256!
  const workspacePath = (await new GetProjectWorkspaceEndpointClient(API_BASE_URL, authorizedHttp).getProjectWorkspace(projectId)).candidatePath!

  const upper = (id: string) => id.toUpperCase()
  const commands = [randomUUID(), randomUUID()]
  const executions = [randomUUID(), randomUUID()]
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.exec('BEGIN IMMEDIATE')
    const insertCommand = db.prepare(
      `INSERT INTO verification_commands
         (Id, ProjectId, CommandNumber, Name, ExecutablePath, Arguments, TimeoutSeconds, IsEnabled, ConfiguredAtUtc, UpdatedAtUtc)
       VALUES (?, ?, ?, ?, 'fixture-tool', '', 60, 1, ?, ?)`,
    )
    const insertExecution = db.prepare(
      `INSERT INTO verification_executions
         (Id, ProjectId, GitWorkspaceId, GitCheckpointId, VerificationCommandId, ExecutionNumber, WorkspacePath,
          CheckpointFingerprintSha256, CommandName, ExecutablePath, Arguments, TimeoutSeconds, Status, Outcome, ExitCode,
          CompletionFingerprintSha256, ClaimedAtUtc, DispatchedAtUtc, CompletedAtUtc)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'fixture-tool', '', 60, ?, 'Exited', ?, ?, ?, ?, ?)`,
    )
    const lintPassed = options.lintStatus === 'Passed'
    const specs: [string, string, number][] = [
      ['unit', 'Passed', 0],
      ['lint', lintPassed ? 'Passed' : 'Failed', lintPassed ? 0 : 1],
    ]
    specs.forEach(([commandName, status, exitCode], index) => {
      insertCommand.run(upper(commands[index]), upper(projectId), index + 1, commandName, NOW, NOW)
      insertExecution.run(
        upper(executions[index]), upper(projectId), upper(workspaceId), upper(checkpointId), upper(commands[index]), index + 1,
        workspacePath, fingerprint, commandName, status, exitCode, fingerprint, NOW, NOW, NOW,
      )
    })
    db.exec('COMMIT')
  } catch (error) {
    try {
      db.exec('ROLLBACK')
    } catch {
      // The transaction may already be closed.
    }
    throw error
  } finally {
    db.close()
  }

  return {
    projectId,
    checkpointId,
    checkpointFingerprint: fingerprint,
    passedExecutionId: executions[0],
    failedExecutionId: executions[1],
    unitCommandId: commands[0],
    lintCommandId: commands[1],
  }
}
