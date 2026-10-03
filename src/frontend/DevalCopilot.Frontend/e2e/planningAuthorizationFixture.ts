import { randomUUID } from 'node:crypto'
import { DatabaseSync } from 'node:sqlite'
import {
  CaptureGitWorkspaceCheckpointEndpointClient,
  CreateManualRunEndpointClient,
  CreateManualRunRequest,
  PrepareRepositoryWorkspaceEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL, SMOKE_DB_PATH, TEST_LAUNCH_SECRET } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { createFixtureRepository } from './support'

// Owned metadata fixtures for the human implementation authorization of an escalated final plan (ADR-0016). The project,
// manual run, workspace, lease, and checkpoint are created through the PUBLIC operations of the real host; only the
// completed planning-lineage rows (attempts, their ordered inputs, and the provider-observed messages plus the host's
// canonical escalation), which no public operation can create without a provider, are inserted into the run-owned database.
// No provider is ever started: nothing here claims an attempt, and the specifications never request an implementation.

export const authorizedHttp = {
  fetch(url: RequestInfo, init?: RequestInit): Promise<Response> {
    const headers = new Headers(init?.headers)
    headers.set('Authorization', `Bearer ${TEST_LAUNCH_SECRET}`)
    return fetch(url, { ...init, headers })
  },
}

export interface PlanningAuthorizationFixture {
  projectName: string
  projectId: string
  runId: string
  rootId: string
  firstRevisionId: string
  finalProposalId: string
  escalationId: string
  decisionIds: string[]
}

const NOW = '2026-10-01 12:00:00+00:00'

/**
 * The ORIGINAL canonical escalation content (written before ADR-0020) for these durable identifiers, kept on purpose: this seeded record
 * plays a historical escalation, which the host must keep recognizing as an authorization source. The current wording is exercised
 * end to end by the escalated native-double journey, which records its escalation through the production writer.
 */
function canonicalEscalationContent(rootId: string, firstId: string, secondId: string, challengeIds: string[]): string {
  return JSON.stringify({
    unresolvedDecision:
      'The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.',
    options:
      'Decide manually whether the revised proposal is acceptable, or start a new explicit planning request. Neither is chosen by this record.',
    consequences:
      'The revised proposal is not implementable through this lineage and is not approved; a third review is not available.',
    evidence:
      `Root proposal ${rootId}; first revision ${firstId}; second revision ${secondId}; ${challengeIds.length} second-round challenge(s) ` +
      `each decided once: ${challengeIds.join(', ')}.`,
    recommendedChoice: 'Read the second-round decisions before starting any new planning request.',
  })
}

const PROPOSAL = (scope: string) =>
  JSON.stringify({
    scope,
    implementationSteps: 'Add the table then the query',
    risks: 'Unbounded content',
    verificationPlan: 'Tests',
    escalationPoints: 'None expected',
  })

const CHALLENGE = JSON.stringify({
  disputedItem: 'Step 1',
  materialImpact: 'Could cause data loss',
  reasoning: 'The step does not account for concurrent writers',
  alternativeOrQuestion: 'Consider a serialized write path instead',
})

const DECISION = JSON.stringify({
  resolution: 'accepted',
  rationale: 'The challenge is correct.',
  resultingPlanChanges: 'Serialize the write path.',
  nextAction: 'None',
})

/**
 * Registers a project, records a manual run, prepares its workspace and checkpoint through the public operations, marks the
 * run Running, and inserts a complete, coherent two-round planning lineage that ends in the host's canonical escalation.
 */
export async function createPlanningAuthorizationFixture(name: string): Promise<PlanningAuthorizationFixture> {
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

  const rootId = randomUUID()
  const challenge1 = randomUUID()
  const decision1 = randomUUID()
  const firstRevisionId = randomUUID()
  const challenge2 = randomUUID()
  const decision2 = randomUUID()
  const finalProposalId = randomUUID()
  const escalationId = randomUUID()
  const attempts = Array.from({ length: 5 }, () => randomUUID())

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
          AgentMaxTotalCapturedBytes, AgentBudgetSlot)
       VALUES (?, ?, ?, 'Agent', 'Completed', ?, ?, '', ?, ?, '1.0', ?, ?, ?, ?, ?, ?, ?, 1200000, 65536, 131072, ?)`,
    )
    const lineage: [string, string, string, string, string, string][] = [
      ['Codex', 'Planner', 'Proposal', 'Proposal', 'Proposed', ''],
      ['ClaudeCode', 'CriticalReviewer', 'Challenge', 'CriticalReview', 'Challenged', ''],
      ['Codex', 'Resolver', 'Decision', 'ChallengeResolution', 'Resolved', ''],
      ['ClaudeCode', 'CriticalReviewer', 'Challenge', 'CriticalReview', 'Challenged', ''],
      ['Codex', 'Resolver', 'Decision', 'ChallengeResolution', 'Resolved', ''],
    ]
    lineage.forEach(([provider, role, expectedMessageType, contract, outcome], index) => {
      insertAttempt.run(
        upper(attempts[index]), upper(runId), index + 1, NOW, NOW, provider, role, expectedMessageType, contract, outcome,
        upper(workspaceId), upper(checkpointId), fingerprint, upper(randomUUID()), index + 1,
      )
    })

    const insertInput = db.prepare('INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES (?, ?, ?, ?)')
    const inputs: [number, string[]][] = [
      [1, [rootId]],
      [2, [rootId, challenge1]],
      [3, [firstRevisionId]],
      [4, [firstRevisionId, challenge2]],
    ]
    for (const [attemptIndex, messageIds] of inputs) {
      messageIds.forEach((messageId, sequence) => {
        insertInput.run(upper(randomUUID()), upper(attempts[attemptIndex]), upper(messageId), sequence)
      })
    }

    const insertMessage = db.prepare(
      `INSERT INTO collaboration_messages
         (Id, RunId, AttemptId, ProtocolVersion, ActorKind, ActorAgentRole, ActorAgentProvider, RecipientKind, RecipientAgentRole,
          RecipientAgentProvider, Type, InReplyToMessageId, Summary, StructuredContentJson, Provenance, OccurredAtUtc)
       VALUES (?, ?, ?, '1.0', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    )
    const agent = (id: string, attemptIndex: number | null, actor: [string, string | null, string | null], recipient: [string, string | null, string | null], type: string, replyTo: string | null, summary: string, content: string, provenance = 'ProviderObserved') =>
      insertMessage.run(
        upper(id), upper(runId), attemptIndex === null ? null : upper(attempts[attemptIndex]), actor[0], actor[1], actor[2],
        recipient[0], recipient[1], recipient[2], type, replyTo === null ? null : upper(replyTo), summary, content, provenance, NOW,
      )
    const planner: [string, string, string] = ['Agent', 'Planner', 'Codex']
    const reviewer: [string, string, string] = ['Agent', 'CriticalReviewer', 'ClaudeCode']
    const resolver: [string, string, string] = ['Agent', 'Resolver', 'Codex']
    const toClaude: [string, null, string] = ['Agent', null, 'ClaudeCode']
    const toCodex: [string, null, string] = ['Agent', null, 'Codex']
    agent(rootId, 0, planner, toClaude, 'Proposal', null, 'Add the ledger table and its query.', PROPOSAL('Ledger'))
    agent(challenge1, 1, reviewer, toCodex, 'Challenge', rootId, 'The first plan has a concurrency gap.', CHALLENGE)
    agent(decision1, 2, resolver, toClaude, 'Decision', challenge1, 'The first challenge is accepted.', DECISION)
    agent(firstRevisionId, 2, resolver, toClaude, 'Proposal', rootId, 'Revised ledger proposal.', PROPOSAL('First revised scope'))
    agent(challenge2, 3, reviewer, toCodex, 'Challenge', firstRevisionId, 'The revision still has a concurrency gap.', CHALLENGE)
    agent(decision2, 4, resolver, toClaude, 'Decision', challenge2, 'The second challenge is accepted.', DECISION)
    agent(finalProposalId, 4, resolver, toClaude, 'Proposal', firstRevisionId, 'Final revised ledger proposal.', PROPOSAL('Final revised scope'))
    agent(
      escalationId, null, ['Orchestrator', null, null], ['Human', null, null], 'Escalation', finalProposalId,
      'The second challenge-resolution round is complete and needs a human decision.',
      canonicalEscalationContent(rootId, firstRevisionId, finalProposalId, [challenge2]), 'HostConstructed',
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

  return { projectName, projectId, runId, rootId, firstRevisionId, finalProposalId, escalationId, decisionIds: [decision2] }
}
