import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'
import {
  ApiException,
  CaptureGitWorkspaceCheckpointEndpointClient,
  ClaimVerificationExecutionEndpointClient,
  ClaimVerificationExecutionRequest,
  ConfigureVerificationCommandEndpointClient,
  ConfigureVerificationCommandRequest,
  GetProjectGitEvidenceEndpointClient,
  GetProjectVerificationExecutionsEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
  UpdateVerificationCommandEndpointClient,
  UpdateVerificationCommandRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { authorizedHttp } from './planningAuthorizationFixture'
import { createFixtureRepository } from './support'

// The REAL generated TypeScript client over real HTTP against the actual host and a database owned by this run: the explicit
// verification claim is answered with HTTP 202 and its typed execution identity and number, both execution and evidence reads carry
// the additive workspace identity, and refusals keep safe Problem Details. The project, workspace, lease, checkpoint and recipe are
// created through the public operations; the host's own supervisor then runs the recipe (the Node executable that runs this
// specification, with a no-op script) inside the isolated workspace, so nothing here is written to the database directly.

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

test('the generated client resolves the 202 verification claim, exposes the workspace identity and keeps refusals safe', async () => {
  // Registration retries on the host's Git readiness refusal only; budget 45 x 1 s fits this timeout.
  test.setTimeout(90_000)
  const repositoryPath = createFixtureRepository('verification-claim-wire')
  const projectId = (
    await retryOnGitUnavailable(
      () => new RegisterProjectEndpointClient(API_BASE_URL, authorizedHttp).registerProject(new RegisterProjectRequest({ name: 'verification-claim-wire fixture', path: repositoryPath })),
      { attempts: 45, delayMs: 1_000 },
    )
  ).projectId!
  const workspaceId = (await new PrepareRepositoryWorkspaceEndpointClient(API_BASE_URL, authorizedHttp).prepareRepositoryWorkspace(projectId)).workspaceId!
  const checkpoint = await new CaptureGitWorkspaceCheckpointEndpointClient(API_BASE_URL, authorizedHttp).captureGitWorkspaceCheckpoint(projectId)
  const configure = new ConfigureVerificationCommandEndpointClient(API_BASE_URL, authorizedHttp)
  const recipe = await configure.configureVerificationCommand(
    projectId,
    new ConfigureVerificationCommandRequest({ name: 'noop', executablePath: process.execPath, arguments: ['-e', '0'], timeoutSeconds: 60, isEnabled: true }),
  )
  const claim = new ClaimVerificationExecutionEndpointClient(API_BASE_URL, authorizedHttp)
  const evidence = new GetProjectGitEvidenceEndpointClient(API_BASE_URL, authorizedHttp)
  const executions = new GetProjectVerificationExecutionsEndpointClient(API_BASE_URL, authorizedHttp)

  // The evidence read names the workspace of the checkpoint it shows.
  const current = await evidence.getProjectGitEvidence(projectId)
  expect(current.checkpointId).toBe(checkpoint.checkpointId)
  expect(current.gitWorkspaceId).toBe(workspaceId)

  // Unknown checkpoint: a safe refusal that records nothing.
  const unknown = await rejectionOf(claim.claimVerificationExecution(projectId, recipe.verificationCommandId!, new ClaimVerificationExecutionRequest({ gitCheckpointId: randomUUID() })))
  expect(unknown.status).toBe(404)
  expect(unknown.response).toContain('verification.not_found')
  expect(unknown.response).not.toContain(repositoryPath)
  expect(await executions.getProjectVerificationExecutions(projectId)).toEqual([])

  // The accepted claim resolves with its typed identity and number.
  const accepted = await claim.claimVerificationExecution(
    projectId,
    recipe.verificationCommandId!,
    new ClaimVerificationExecutionRequest({ gitCheckpointId: checkpoint.checkpointId }),
  )
  expect(accepted.verificationExecutionId).toBeTruthy()
  expect(accepted.executionNumber).toBe(1)

  // The execution list carries the workspace identity of the claim, for the running and for the terminal execution alike.
  const listed = await executions.getProjectVerificationExecutions(projectId)
  const execution = listed.find((candidate) => candidate.verificationExecutionId === accepted.verificationExecutionId)
  expect(execution?.gitWorkspaceId).toBe(workspaceId)
  expect(execution?.gitCheckpointId).toBe(checkpoint.checkpointId)

  // The host's supervisor runs the claimed snapshot to a terminal result.
  await expect
    .poll(async () => (await executions.getProjectVerificationExecutions(projectId)).find((candidate) => candidate.verificationExecutionId === accepted.verificationExecutionId)?.status, {
      timeout: 30_000,
    })
    .not.toBe('Running')
  const finished = (await executions.getProjectVerificationExecutions(projectId)).find((candidate) => candidate.verificationExecutionId === accepted.verificationExecutionId)!
  expect(finished.gitWorkspaceId).toBe(workspaceId)

  // A disabled recipe is a safe conflict and records nothing more.
  await new UpdateVerificationCommandEndpointClient(API_BASE_URL, authorizedHttp).updateVerificationCommand(
    projectId,
    recipe.verificationCommandId!,
    new UpdateVerificationCommandRequest({ name: 'noop', executablePath: process.execPath, arguments: ['-e', '0'], timeoutSeconds: 60, isEnabled: false }),
  )
  const disabled = await rejectionOf(claim.claimVerificationExecution(projectId, recipe.verificationCommandId!, new ClaimVerificationExecutionRequest({ gitCheckpointId: checkpoint.checkpointId })))
  expect(disabled.status).toBe(409)
  expect(disabled.response).toContain('verification.disabled')
  expect((await executions.getProjectVerificationExecutions(projectId)).length).toBe(1)
})
