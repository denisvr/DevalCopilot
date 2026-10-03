import { createHash, randomUUID } from 'node:crypto'
import { DatabaseSync } from 'node:sqlite'
import {
  CaptureGitWorkspaceCheckpointEndpointClient,
  CreateManualRunEndpointClient,
  CreateManualRunRequest,
  PrepareRepositoryWorkspaceEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL, SMOKE_DB_PATH } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { createFixtureRepository } from './support'
import { authorizedHttp } from './planningAuthorizationFixture'

// RAW-SQL EVIDENCE FIXTURE, NOT PRODUCTION-WRITTEN: owned metadata for the display of a completed local verification-failure
// diagnosis (ADR-0018). The project, manual run, workspace, lease, and checkpoint are created through the PUBLIC operations of
// the real host. Only the rows no public operation can create without a provider or a real verification process are inserted
// into the run-owned database: the planning/implementation attempts and their provider-observed messages (up to the
// ExecutionReport), the verification command/execution/sealed-output rows, and one COMPLETED diagnosis attempt with its pinned
// report input, pinned verification rows, and ReviewFinding messages. The implementation's start and result checkpoint are
// both the single checkpoint captured through the public operation, so no invented checkpoint exists. No provider is ever
// started and nothing here claims an attempt.

export interface VerificationDiagnosisFixture {
  projectName: string
  projectId: string
  runId: string
  executionReportId: string
  diagnosisAttemptId: string
  workspaceId: string
  checkpointId: string
  checkpointFingerprint: string
  findingIds: string[]
  findingSummaries: string[]
  pinnedVerification: string[]
}

const NOW = '2026-10-01 12:00:00+00:00'

// UTC ticks (100 ns since 0001-01-01) of NOW, exactly as the host's snapshot digest writes an instant.
const NOW_TICKS = BigInt(Date.parse('2026-10-01T12:00:00Z')) * 10_000n + 621_355_968_000_000_000n

interface SnapshotOutput {
  id: string
  path: string
  hash: string
}

/** The host's versioned verification-diagnosis snapshot digest (Application: VerificationDiagnosisSnapshot, version 1),
 * reproduced for this raw-SQL fixture so its diagnosis memberships carry the digest the host would have pinned. */
function snapshotDigest(facts: {
  commandId: string
  projectId: string
  commandNumber: number
  name: string
  executionId: string
  executionNumber: number
  workspaceId: string
  checkpointId: string
  workspacePath: string
  fingerprint: string
  status: string
  exitCode: number
  outputs: SnapshotOutput[] | null
}): string {
  const text = (value: string) => `${Buffer.byteLength(value, 'utf8')}:${value}`
  const lines: string[] = ['verification-diagnosis-snapshot-v1']
  const add = (name: string, value: string | number | bigint) => lines.push(`${name}=${value}`)
  add('command.id', facts.commandId)
  add('command.projectId', facts.projectId)
  add('command.number', facts.commandNumber)
  add('command.name', text(facts.name))
  add('command.executablePath', text('fixture-tool'))
  add('command.arguments.count', 0)
  add('command.timeoutSeconds', 60)
  add('command.enabled', 1)
  add('command.configuredAt', NOW_TICKS)
  add('command.updatedAt', NOW_TICKS)
  add('execution.id', facts.executionId)
  add('execution.projectId', facts.projectId)
  add('execution.number', facts.executionNumber)
  add('execution.workspaceId', facts.workspaceId)
  add('execution.checkpointId', facts.checkpointId)
  add('execution.commandId', facts.commandId)
  add('execution.workspacePath', text(facts.workspacePath))
  add('execution.checkpointFingerprint', text(facts.fingerprint))
  add('execution.commandName', text(facts.name))
  add('execution.executablePath', text('fixture-tool'))
  add('execution.arguments.count', 0)
  add('execution.timeoutSeconds', 60)
  add('execution.status', text(facts.status))
  add('execution.claimedAt', NOW_TICKS)
  add('execution.dispatchedAt', NOW_TICKS)
  add('execution.completedAt', NOW_TICKS)
  add('execution.outcome', text('Exited'))
  add('execution.exitCode', facts.exitCode)
  add('execution.completionFingerprint', text(facts.fingerprint))
  add('failed', facts.outputs ? 1 : 0)
  facts.outputs?.forEach((output, index) => {
    const prefix = index === 0 ? 'stdout' : 'stderr'
    add(prefix + '.id', output.id)
    add(prefix + '.path', text(output.path))
    add(prefix + '.length', 0)
    add(prefix + '.hash', text(output.hash))
    add(prefix + '.truncated', 0)
    add(prefix + '.capture', text('CapturedWithKnownTruncation'))
  })
  return createHash('sha256').update(lines.join('\n') + '\n', 'utf8').digest('hex')
}

const PROPOSAL = JSON.stringify({
  scope: 'Ledger',
  implementationSteps: 'Add the table then the query',
  risks: 'Unbounded content',
  verificationPlan: 'Tests',
  escalationPoints: 'None expected',
})

const EXECUTION_REPORT = JSON.stringify({
  completedWork: 'Added the ledger table and its query.',
  verification: 'Local verification was not run by the Implementer.',
})

/** One RUNNING Claude correction attempt of the fixture diagnosis (the existing ReviewCorrection contract at its version 2 tuple),
 * carrying the given raw direct-guidance snapshot and the diagnosis's exact ordered inputs: the previous report, then every finding.
 * Raw evidence for the display and wire specifications: it is never dispatched and no provider is started. */
export function insertDiagnosisCorrectionAttempt(fixture: VerificationDiagnosisFixture, guidance: string | null): string {
  const attemptId = randomUUID()
  const upper = (id: string) => id.toUpperCase()
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.exec('BEGIN IMMEDIATE')
    db.prepare(
      `INSERT INTO attempts
         (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole, AgentProtocolVersion,
          AgentExpectedMessageType, AgentResponseContract, AgentGitWorkspaceId, AgentGitCheckpointId, AgentCheckpointFingerprintSha256,
          AgentContextManifestArtifactId, AgentTimeout, AgentMaxBytesPerStream, AgentMaxTotalCapturedBytes, AgentBudgetSlot,
          AgentPermissionProfile, AgentAdapterContractVersion, AgentDirectHumanGuidance)
       VALUES (?, ?, 5, 'Agent', 'Running', ?, '', 'ClaudeCode', 'Implementer', '1.0', 'ExecutionReport', 'ReviewCorrection', ?, ?, ?, ?,
               1200000, 65536, 131072, 5, 'WorkspaceEditOnly', 'claude-review-correction-v2', ?)`,
    ).run(
      upper(attemptId), upper(fixture.runId), NOW, upper(fixture.workspaceId), upper(fixture.checkpointId), fixture.checkpointFingerprint,
      upper(randomUUID()), guidance,
    )
    const insertInput = db.prepare('INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES (?, ?, ?, ?)')
    ;[fixture.executionReportId, ...fixture.findingIds].forEach((messageId, sequence) =>
      insertInput.run(upper(randomUUID()), upper(attemptId), upper(messageId), sequence),
    )
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
  return attemptId
}

/** Rewrites the raw direct-guidance snapshot of one attempt (a null value is the unguided snapshot). */
export function setAttemptGuidance(attemptId: string, guidance: string | null) {
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.prepare('UPDATE attempts SET AgentDirectHumanGuidance = ? WHERE Id = ?').run(guidance, attemptId.toUpperCase())
  } finally {
    db.close()
  }
}

/** Registers a project and a manual run, prepares its workspace and checkpoint through the public operations, marks the run
 * Running, and inserts the coherent evidence chain described above. */
export async function createVerificationDiagnosisFixture(name: string): Promise<VerificationDiagnosisFixture> {
  const projectName = `${name} fixture`
  const projects = new RegisterProjectEndpointClient(API_BASE_URL, authorizedHttp)
  // The fixture repository is built exactly once, before the retry loop: a second `git commit` of unchanged content fails.
  const repositoryPath = createFixtureRepository(name)
  const projectId = (
    await retryOnGitUnavailable(
      () => projects.registerProject(new RegisterProjectRequest({ name: projectName, path: repositoryPath })),
      { attempts: 45, delayMs: 1_000 },
    )
  ).projectId!

  const runId = (
    await new CreateManualRunEndpointClient(API_BASE_URL, authorizedHttp).createManualRun(
      new CreateManualRunRequest({ projectId, objective: 'Implement the ledger table and its query' }),
    )
  ).runId!

  const workspaceId = (
    await new PrepareRepositoryWorkspaceEndpointClient(API_BASE_URL, authorizedHttp).prepareRepositoryWorkspace(projectId)
  ).workspaceId!
  const checkpoint = await new CaptureGitWorkspaceCheckpointEndpointClient(API_BASE_URL, authorizedHttp).captureGitWorkspaceCheckpoint(projectId)
  const checkpointId = checkpoint.checkpointId!
  const fingerprint = checkpoint.fingerprintSha256!

  const proposalId = randomUUID()
  const acceptanceId = randomUUID()
  const executionReportId = randomUUID()
  const findingIds = [randomUUID(), randomUUID()]
  const findingSummaries = ['The ledger query ignores the unit test failure.', 'The ledger table lacks the migration the unit test needs.']
  const attempts = { planner: randomUUID(), reviewer: randomUUID(), implementer: randomUUID(), diagnosis: randomUUID() }
  const commands = [randomUUID(), randomUUID()]
  const executions = [randomUUID(), randomUUID()]

  const upper = (id: string) => id.toUpperCase()
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.exec('BEGIN IMMEDIATE')
    db.prepare("UPDATE runs SET Lifecycle = 'Running' WHERE Id = ?").run(upper(runId))

    const insertAttempt = db.prepare(
      `INSERT INTO attempts
         (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments, AgentProvider, AgentRole, AgentProtocolVersion,
          AgentExpectedMessageType, AgentResponseContract, AgentOutcome, AgentGitWorkspaceId, AgentGitCheckpointId,
          AgentCheckpointFingerprintSha256, AgentContextManifestArtifactId, AgentTimeout, AgentMaxBytesPerStream,
          AgentMaxTotalCapturedBytes, AgentBudgetSlot, AgentDispatchedAtUtc, AgentPermissionProfile, AgentAdapterContractVersion,
          AgentResultGitCheckpointId)
       VALUES (?, ?, ?, 'Agent', 'Completed', ?, ?, '', ?, ?, '1.0', ?, ?, ?, ?, ?, ?, ?, 1200000, 65536, 131072, ?, ?, ?, ?, ?)`,
    )
    const attempt = (
      id: string, number: number, provider: string, role: string, expectedMessageType: string, contract: string, outcome: string,
      profile: string | null, adapterContract: string | null, resultCheckpointId: string | null,
    ) =>
      insertAttempt.run(
        upper(id), upper(runId), number, NOW, NOW, provider, role, expectedMessageType, contract, outcome, upper(workspaceId),
        upper(checkpointId), fingerprint, upper(randomUUID()), number, NOW, profile, adapterContract, resultCheckpointId,
      )
    attempt(attempts.planner, 1, 'Codex', 'Planner', 'Proposal', 'Proposal', 'Proposed', null, null, null)
    attempt(attempts.reviewer, 2, 'ClaudeCode', 'CriticalReviewer', 'Acceptance', 'CriticalReview', 'Accepted', null, null, null)
    attempt(
      attempts.implementer, 3, 'ClaudeCode', 'Implementer', 'ExecutionReport', 'ImplementationReport', 'Implemented',
      'WorkspaceEditOnly', null, upper(checkpointId),
    )
    // The diagnosis carries the exact persisted tuple the host's diagnosis path requires (VerificationDiagnosisPolicy.HasExactTuple).
    attempt(
      attempts.diagnosis, 4, 'Codex', 'CodeReviewer', 'ReviewFinding', 'VerificationDiagnosis', 'DiagnosisFindingsRecorded',
      'ReadOnly', 'codex-verification-diagnosis-v1', null,
    )

    const insertInput = db.prepare('INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES (?, ?, ?, ?)')
    const input = (attemptId: string, messageId: string, sequence: number) =>
      insertInput.run(upper(randomUUID()), upper(attemptId), upper(messageId), sequence)
    input(attempts.reviewer, proposalId, 0)
    input(attempts.implementer, proposalId, 0)
    input(attempts.implementer, acceptanceId, 1)
    input(attempts.diagnosis, executionReportId, 0)

    const insertMessage = db.prepare(
      `INSERT INTO collaboration_messages
         (Id, RunId, AttemptId, ProtocolVersion, ActorKind, ActorAgentRole, ActorAgentProvider, RecipientKind, RecipientAgentRole,
          RecipientAgentProvider, Type, InReplyToMessageId, Summary, StructuredContentJson, Provenance, OccurredAtUtc)
       VALUES (?, ?, ?, '1.0', 'Agent', ?, ?, 'Agent', ?, ?, ?, ?, ?, ?, 'ProviderObserved', ?)`,
    )
    const message = (
      id: string, attemptId: string, actor: [string, string], recipient: [string | null, string], type: string, replyTo: string | null,
      summary: string, content: string,
    ) =>
      insertMessage.run(
        upper(id), upper(runId), upper(attemptId), actor[0], actor[1], recipient[0], recipient[1], type,
        replyTo === null ? null : upper(replyTo), summary, content, NOW,
      )
    // Inserted in causal order so the ledger sequence (the table's autoincrement key) increases along the chain.
    message(proposalId, attempts.planner, ['Planner', 'Codex'], [null, 'ClaudeCode'], 'Proposal', null, 'Add the ledger table and its query.', PROPOSAL)
    message(
      acceptanceId, attempts.reviewer, ['CriticalReviewer', 'ClaudeCode'], [null, 'Codex'], 'Acceptance', proposalId,
      'The plan is accepted.', JSON.stringify({ rationale: 'The plan is sound.' }),
    )
    message(
      executionReportId, attempts.implementer, ['Implementer', 'ClaudeCode'], [null, 'Codex'], 'ExecutionReport', proposalId,
      'The ledger table and its query were added.', EXECUTION_REPORT,
    )
    findingIds.forEach((id, index) =>
      message(
        id, attempts.diagnosis, ['CodeReviewer', 'Codex'], ['Implementer', 'ClaudeCode'], 'ReviewFinding', executionReportId,
        findingSummaries[index],
        JSON.stringify({
          severity: 'high',
          category: 'correctness',
          evidence: 'The failed unit verification output names this defect.',
          requiredChange: 'Make the failing unit test pass.',
        }),
      ),
    )

    // Two enabled commands bound to the one captured checkpoint: `unit` Failed (exit 1, with both sealed output rows) and `lint` Passed.
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
    const insertOutput = db.prepare(
      `INSERT INTO verification_output_artifacts
         (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ByteLength, ContentHash, Truncated, CaptureOutcome, CreatedAtUtc)
       VALUES (?, ?, ?, ?, 0, ?, 0, 'CapturedWithKnownTruncation', ?)`,
    )
    const insertEvidence = db.prepare(
      'INSERT INTO attempt_verification_evidence (Id, AttemptId, Sequence, VerificationCommandId, VerificationExecutionId, SnapshotSha256) VALUES (?, ?, ?, ?, ?, ?)',
    )
    const specs: [string, string, number][] = [
      ['unit', 'Failed', 1],
      ['lint', 'Passed', 0],
    ]
    specs.forEach(([commandName, status, exitCode], index) => {
      insertCommand.run(upper(commands[index]), upper(projectId), index + 1, commandName, NOW, NOW)
      insertExecution.run(
        upper(executions[index]), upper(projectId), upper(workspaceId), upper(checkpointId), upper(commands[index]), index + 1,
        repositoryPath, fingerprint, commandName, status, exitCode, fingerprint, NOW, NOW, NOW,
      )
      let outputs: SnapshotOutput[] | null = null
      if (status === 'Failed') {
        outputs = []
        for (const purpose of ['StandardOutput', 'StandardError']) {
          const output = { id: randomUUID(), path: `fixture/${executions[index]}/${purpose}.txt`, hash: 'a'.repeat(64) }
          outputs.push(output)
          insertOutput.run(upper(output.id), upper(executions[index]), purpose, output.path, output.hash, NOW)
        }
      }
      // The diagnosis membership pins the host's versioned snapshot digest of the facts above (a diagnosis row without it fails closed).
      const digest = snapshotDigest({
        commandId: commands[index], projectId, commandNumber: index + 1, name: commandName, executionId: executions[index],
        executionNumber: index + 1, workspaceId, checkpointId, workspacePath: repositoryPath, fingerprint, status, exitCode, outputs,
      })
      insertEvidence.run(upper(randomUUID()), upper(attempts.diagnosis), index, upper(commands[index]), upper(executions[index]), digest)
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
    projectName,
    projectId,
    runId,
    executionReportId,
    diagnosisAttemptId: attempts.diagnosis,
    workspaceId,
    checkpointId,
    checkpointFingerprint: fingerprint,
    findingIds,
    findingSummaries,
    pinnedVerification: ['#1 unit · execution 1 · Failed · exit code 1', '#2 lint · execution 2 · Passed · exit code 0'],
  }
}
