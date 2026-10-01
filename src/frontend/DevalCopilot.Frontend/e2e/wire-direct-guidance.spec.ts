import { randomUUID } from 'node:crypto'
import { DatabaseSync } from 'node:sqlite'
import { expect, test } from '@playwright/test'
import {
  ApiException,
  CreateManualRunEndpointClient,
  CreateManualRunRequest,
  GetAgentAttemptEvidenceEndpointClient,
  GetImplementationAttemptStatusEndpointClient,
  GetRunCockpitEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
  RequestImplementationEndpointClient,
  RequestImplementationRequest,
  RequestReviewCorrectionEndpointClient,
  RequestReviewCorrectionRequest,
} from '../src/api/generated/api-client'
import { describeDirectGuidance } from '../src/features/cockpit/describeDirectGuidance'
import { describeDirectGuidanceFailure } from '../src/features/cockpit/hooks/directGuidanceFailure'
import { API_BASE_URL, SMOKE_DB_PATH, TEST_LAUNCH_SECRET } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { createFixtureRepository } from './support'

// The REAL generated TypeScript client over real HTTP: the actual host (PlaywrightSmoke environment, a database owned by this
// run), the launch secret as an Authorization header, and fixtures created through the public operations. Only the
// attempt rows (which no public operation can create without a provider) are inserted straight into the run-owned
// database, so the generated client's request serialization, response revival, and error mapping are exercised for real.
// No provider is ever started and the bodies are small and bounded. It registers its own project (with a nonterminal run) in the
// shared run-owned database; the UI specifications select and assert their own fixture project, so no order is assumed.

const http = {
  fetch(url: RequestInfo, init?: RequestInit): Promise<Response> {
    const headers = new Headers(init?.headers)
    headers.set('Authorization', `Bearer ${TEST_LAUNCH_SECRET}`)
    return fetch(url, { ...init, headers })
  },
}

const SENTINEL = 'SENTINEL-WIRE-31'
const ACCEPTED = `Reuse the existing helper.\nKeep it small. ${SENTINEL}`

async function rejectionOf(promise: Promise<unknown>): Promise<ApiException> {
  try {
    await promise
  } catch (caught) {
    if (ApiException.isApiException(caught)) {
      return caught
    }
    throw new Error('Expected an ApiException from the generated client.')
  }
  throw new Error('Expected the request to be refused.')
}

function insertImplementationAttempt(runId: string, attemptId: string, guidance: string | null) {
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.prepare(
      `INSERT INTO attempts
         (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole, AgentProtocolVersion,
          AgentExpectedMessageType, AgentResponseContract, AgentPermissionProfile, AgentAdapterContractVersion, AgentDirectHumanGuidance,
          AgentGitWorkspaceId, AgentGitCheckpointId, AgentCheckpointFingerprintSha256, AgentContextManifestArtifactId, AgentTimeout,
          AgentMaxBytesPerStream, AgentMaxTotalCapturedBytes, AgentBudgetSlot)
       VALUES (?, ?, 1, 'Agent', 'Running', '2026-10-01 12:00:00+00:00', '', 'ClaudeCode', 'Implementer', '1.0',
               'Proposal', 'ImplementationReport', 'WorkspaceEditOnly', 'claude-implementation-v2', ?,
               ?, ?, ?, ?, 1200000, 65536, 131072, 1)`,
    ).run(
      attemptId.toUpperCase(),
      runId.toUpperCase(),
      guidance,
      randomUUID().toUpperCase(),
      randomUUID().toUpperCase(),
      'a'.repeat(64),
      randomUUID().toUpperCase(),
    )
    db.prepare('INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES (?, ?, ?, 0)').run(
      randomUUID().toUpperCase(),
      attemptId.toUpperCase(),
      randomUUID().toUpperCase(),
    )
  } finally {
    db.close()
  }
}

function setGuidance(attemptId: string, guidance: string | null) {
  const db = new DatabaseSync(SMOKE_DB_PATH)
  try {
    db.exec('PRAGMA busy_timeout = 5000')
    db.prepare('UPDATE attempts SET AgentDirectHumanGuidance = ? WHERE Id = ?').run(guidance, attemptId.toUpperCase())
  } finally {
    db.close()
  }
}

test('the generated client serializes guidance and revives the recorded fact over real HTTP', async () => {
  // Registration retries on the host's Git readiness refusal only; budget 45 x 1 s fits this timeout.
  test.setTimeout(60_000)
  const projects = new RegisterProjectEndpointClient(API_BASE_URL, http)
  // The fixture repository is built exactly once, before the retry loop: a second `git commit` of unchanged content fails.
  const repositoryPath = createFixtureRepository('direct-guidance-wire')
  const projectId = (
    await retryOnGitUnavailable(
      () => projects.registerProject(new RegisterProjectRequest({ name: 'Direct guidance wire fixture', path: repositoryPath })),
      { attempts: 45, delayMs: 1_000 },
    )
  ).projectId

  const runId = (
    await new CreateManualRunEndpointClient(API_BASE_URL, http).createManualRun(
      new CreateManualRunRequest({ projectId, objective: 'Exercise the direct guidance wire contract' }),
    )
  ).runId!

  const implementation = new RequestImplementationEndpointClient(API_BASE_URL, http)
  const correction = new RequestReviewCorrectionEndpointClient(API_BASE_URL, http)
  const statusClient = new GetImplementationAttemptStatusEndpointClient(API_BASE_URL, http)
  const evidenceClient = new GetAgentAttemptEvidenceEndpointClient(API_BASE_URL, http)
  const cockpitClient = new GetRunCockpitEndpointClient(API_BASE_URL, http)

  // Request serialization: the guidance member reaches the server and is validated before anything else, for both operations.
  for (const send of [
    () => implementation.requestImplementation(runId, new RequestImplementationRequest({ planProposalMessageId: randomUUID(), guidance: `${SENTINEL} the password is x` })),
    () => implementation.requestImplementation(runId, new RequestImplementationRequest({ planProposalMessageId: randomUUID(), guidance: '   ' })),
    () => implementation.requestImplementation(runId, new RequestImplementationRequest({ planProposalMessageId: randomUUID(), guidance: 'x'.repeat(601) })),
    () => correction.requestReviewCorrection(runId, new RequestReviewCorrectionRequest({ implementationReviewAttemptId: randomUUID(), guidance: `${SENTINEL} the password is x` })),
    () => correction.requestReviewCorrection(runId, new RequestReviewCorrectionRequest({ implementationReviewAttemptId: randomUUID(), guidance: '' })),
  ]) {
    const refused = await rejectionOf(send())
    expect(refused.status).toBe(400)
    const message = describeDirectGuidanceFailure(refused)
    expect(message).toContain('The guidance was not accepted.')
    expect(JSON.stringify([refused.message, refused.response, message])).not.toContain(SENTINEL)
  }

  // Valid guidance and omitted guidance pass validation identically: the run has not started, so both reach the same
  // domain refusal (not a validation error), which the failure mapper leaves to the generic path.
  const withGuidance = await rejectionOf(
    implementation.requestImplementation(runId, new RequestImplementationRequest({ planProposalMessageId: randomUUID(), guidance: ACCEPTED })),
  )
  const withoutGuidance = await rejectionOf(
    implementation.requestImplementation(runId, new RequestImplementationRequest({ planProposalMessageId: randomUUID() })),
  )
  expect(withGuidance.status).toBe(409)
  expect(withoutGuidance.status).toBe(409)
  expect(describeDirectGuidanceFailure(withGuidance)).toBeNull()
  const correctionWithGuidance = await rejectionOf(
    correction.requestReviewCorrection(runId, new RequestReviewCorrectionRequest({ implementationReviewAttemptId: randomUUID(), guidance: ACCEPTED })),
  )
  expect(correctionWithGuidance.status).toBe(409)
  expect(describeDirectGuidanceFailure(correctionWithGuidance)).toBeNull()

  // No attempt: no fact.
  const empty = await statusClient.getImplementationAttemptStatus(runId)
  expect(empty.hasAttempt).toBe(false)
  expect(empty.directGuidance ?? null).toBeNull()

  // Revival of the fact in all three surfaces: Provided -> Unknown (malformed, no text) -> NotRecorded (null snapshot).
  const attemptId = randomUUID()
  insertImplementationAttempt(runId, attemptId, ACCEPTED)
  const read = async () => ({
    status: (await statusClient.getImplementationAttemptStatus(runId)).directGuidance,
    evidence: (await evidenceClient.getAgentAttemptEvidence(runId, attemptId)).directGuidance,
    cockpit: (await cockpitClient.getRunCockpit(runId)).latestAgentAttempt?.directGuidance,
  })

  const provided = await read()
  for (const fact of [provided.status, provided.evidence, provided.cockpit]) {
    expect(fact?.state).toBe('Provided')
    expect(fact?.text).toBe(ACCEPTED)
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'provided', text: ACCEPTED })
  }

  setGuidance(attemptId, ` ${SENTINEL} padded and unnormalized `)
  const unknown = await read()
  for (const fact of [unknown.status, unknown.evidence, unknown.cockpit]) {
    expect(fact?.state).toBe('Unknown')
    expect(fact?.text ?? null).toBeNull()
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'unknown' })
  }

  setGuidance(attemptId, null)
  const recorded = await read()
  for (const fact of [recorded.status, recorded.evidence, recorded.cockpit]) {
    expect(fact?.state).toBe('NotRecorded')
    expect(fact?.text ?? null).toBeNull()
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'notRecorded' })
  }
})
