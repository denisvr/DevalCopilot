import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { normalizeGuidance, sha256Hex } from './journey/guidance'
import { JourneyData, sameId } from './journey/journeyDb'
import {
  CANDIDATE_BASELINE,
  CANDIDATE_RELATIVE_PATH,
  createJourneyRepository,
  instructionFiles,
  journeyRoot,
  journeySecret,
  markInvocations,
  readInvocationLogText,
  readInvocations,
  readLaunchTargetVerdict,
  readSealedManifestText,
  repositorySnapshot,
} from './journey/journeyEnv'
import { instructionDeliveryProblems } from './journey/instructionDelivery'
import { trackedDeliveryProblems } from './journey/trackedDelivery'
import { escalatedLineageProblems, ESCALATED_STAGE_SEQUENCE } from './journey/planIdentity'
import { injectJourneySession, registerProject, selectProject, workflowLines } from './journey/journeySupport'

// The second browser-driven local collaboration proof (Increment 4): an ESCALATED final plan reaches a reviewed candidate. After two
// challenge rounds a person reads the escalation, records a required reason that authorizes exactly one implementation of the exact
// final Proposal, separately requests that implementation, and the same plan then goes through failed local verification, a Codex
// diagnosis, one explicitly guided Claude correction, passed verification, an ordinary Codex approval and a separate Human checkpoint
// approval, with one final persistence reload. Every workflow mutation originates from a rendered control over the generated client;
// the database and the doubles' invocation log are only READ. Only the external provider executables are deterministic doubles, so this
// proves the assembled local workflow, not provider reliability and not completion of the increment. It shares the host, database and
// log with the ordinary journey and reads only its own project, run, workspace and invocation interval.

const PROJECT = 'Escalated plan journey fixture'
const OBJECTIVE = 'Implement the ledger total as a single statement'
const CODE_REVIEW_PATH = '/agent-attempts/code-review'
// The required human reason, typed with whitespace the host normalizes away. The journey derives the value it must have recorded.
const REASON_DRAFT = '  I read the final plan and both rounds of decisions and accept it for one implementation.  '
const REASON = normalizeGuidance(REASON_DRAFT)
const CORRECTION_GUIDANCE_DRAFT = '  Change only the one statement the findings name.\nLeave every other statement alone.  '
const CORRECTION_GUIDANCE = normalizeGuidance(CORRECTION_GUIDANCE_DRAFT)
const VERIFICATION_CLAIM_PATH = /\/api\/projects\/[^/]+\/verification-commands\/[^/]+\/executions$/
const CHECKPOINT_REVIEW_PATH = /\/api\/projects\/[^/]+\/reviews$/
const AUTHORIZATION_PATH = /\/api\/runs\/[^/]+\/planning-escalations\/[^/]+\/implementation-authorization$/
const IMPLEMENT_AUTHORIZED = 'Implement the human-authorized final plan with Claude'

const data = new JourneyData(PROJECT)
let mark = 0

function agentContracts(): string[] {
  return data.attempts().map((attempt) => attempt.AgentResponseContract)
}

function stageInvocations() {
  return readInvocations(mark).filter((entry) => entry.kind !== 'probe')
}

const shortId = (id: string) => id.toLowerCase().slice(0, 8)

async function reloadAndReselect(page: Page) {
  await page.reload()
  await selectProject(page, PROJECT)
}

test('the escalated journey: an authorized final plan through failed verification, diagnosis, guided correction, passed verification, and both approvals', async ({ page }) => {
  test.setTimeout(300_000)
  mark = markInvocations()
  const repository = createJourneyRepository('escalated-source')
  const sourceBefore = repositorySnapshot(repository.path)

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)

  // The provider launch targets the workflow will use are the owned doubles, before any agent stage is requested.
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  const source = page.getByRole('region', { name: 'Source evidence' })
  const verification = page.getByRole('region', { name: 'Verification commands' })
  const diagnosis = page.getByRole('region', { name: 'Verification diagnosis' })
  const review = page.getByRole('region', { name: 'Code review' })
  const manual = page.getByRole('region', { name: 'Checkpoint review evidence' })
  const lineage = page.getByRole('region', { name: 'Proposal lineage' })
  const decision = page.getByRole('region', { name: 'Human decision on the final plan' })

  // 1-2. Register and explicitly select the project, recheck its physical identity, prepare its real candidate worktree, capture the
  // checkpoint, configure the verification command, and record a ManualAgent objective.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByLabel('Verification command name').fill('Candidate total check')
  await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
  await page.getByLabel('Verification arguments').fill('check')
  await page.getByRole('button', { name: 'Add command' }).click()
  await expect(verification).toContainText('#1 Candidate total check')
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  expect(agentContracts()).toEqual([])

  // 3. Codex planning produces the Planner root.
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Plan proposed.')).toBeVisible({ timeout: 60_000 })

  // 4-5. First round: Claude's critical review challenges the root, and Codex resolves exactly that challenge into the first revision.
  await page.getByRole('button', { name: 'Request Claude review', exact: true }).click()
  await expect(page.getByText('Last attempt #2: Proposal challenged.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('The lookup table duplicates the arithmetic.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Resolve challenges with Codex' }).click()
  await expect(page.getByText('Last attempt #3: Challenges resolved.')).toBeVisible({ timeout: 60_000 })
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution'])

  // 6-7. Second round, explicitly requested: Claude reviews the FIRST REVISION and raises a materially different challenge, and Codex
  // resolves exactly that challenge into the final revision. Nothing here is automatic: each stage is one click.
  await page.getByRole('button', { name: 'Request Claude review of the revised proposal' }).click()
  await expect(page.getByText('Last attempt #4: Proposal challenged.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('The revised plan never says whether operand order matters.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Resolve challenges with Codex' }).click()
  await expect(page.getByText('Last attempt #5: Challenges resolved.')).toBeVisible({ timeout: 60_000 })
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution', 'CriticalReview', 'ChallengeResolution'])

  // The lineage: three distinct Proposals with distinguishable substantive content, each resolution deciding exactly its own challenges.
  const attemptsAfterSecondRound = data.attempts()
  const messagesAfterSecondRound = data.messages()
  const ownedBy = (attemptNumber: number, type: string) =>
    messagesAfterSecondRound.filter(
      (message) => message.Type === type && sameId(message.AttemptId, attemptsAfterSecondRound[attemptNumber - 1].Id),
    )
  const rootProposal = ownedBy(1, 'Proposal')[0]
  const firstRevision = ownedBy(3, 'Proposal')[0]
  const finalProposal = ownedBy(5, 'Proposal')[0]
  const firstChallenges = ownedBy(2, 'Challenge')
  const secondChallenges = ownedBy(4, 'Challenge')
  const firstDecisions = ownedBy(3, 'Decision')
  const secondDecisions = ownedBy(5, 'Decision')
  expect([rootProposal, firstRevision, finalProposal].every(Boolean)).toBe(true)
  expect(new Set([rootProposal.Id, firstRevision.Id, finalProposal.Id].map((id) => id.toLowerCase())).size).toBe(3)
  expect(rootProposal.StructuredContentJson).toContain('ROOT-PLAN')
  expect(firstRevision.StructuredContentJson).toContain('REVISED-PLAN')
  expect(finalProposal.StructuredContentJson).toContain('FINAL-PLAN')
  expect(new Set([rootProposal, firstRevision, finalProposal].map((message) => message.StructuredContentJson)).size).toBe(3)
  expect(new Set([rootProposal, firstRevision, finalProposal].map((message) => message.Summary)).size).toBe(3)
  expect(firstChallenges).toHaveLength(1)
  expect(secondChallenges).toHaveLength(1)
  expect(sameId(firstChallenges[0].Id, secondChallenges[0].Id)).toBe(false)
  expect(firstChallenges[0].StructuredContentJson).not.toEqual(secondChallenges[0].StructuredContentJson)
  expect(firstDecisions.map((message) => message.InReplyToMessageId?.toLowerCase())).toEqual(firstChallenges.map((message) => message.Id.toLowerCase()))
  expect(secondDecisions.map((message) => message.InReplyToMessageId?.toLowerCase())).toEqual(secondChallenges.map((message) => message.Id.toLowerCase()))
  expect(sameId(firstRevision.InReplyToMessageId, rootProposal.Id)).toBe(true)
  expect(sameId(finalProposal.InReplyToMessageId, firstRevision.Id)).toBe(true)

  // Exactly one host escalation answers the final Proposal, written in the current explanation (it chooses, grants and approves nothing).
  const escalations = messagesAfterSecondRound.filter((message) => message.Type === 'Escalation')
  expect(escalations).toHaveLength(1)
  const escalation = escalations[0]
  expect(sameId(escalation.InReplyToMessageId, finalProposal.Id)).toBe(true)
  expect(escalation.Provenance).toBe('HostConstructed')
  expect(escalation.AttemptId).toBeNull()
  expect(escalation.Summary).toBe('The second challenge-resolution round is complete and needs a human decision.')
  const escalationContent = JSON.parse(escalation.StructuredContentJson) as Record<string, string>
  expect(Object.keys(escalationContent)).toEqual(['unresolvedDecision', 'options', 'consequences', 'evidence', 'recommendedChoice'])
  expect(escalationContent.options).toContain('separately authorize one implementation of that exact final plan and explicitly request it')
  expect(escalationContent.options).toContain('request a new plan')
  expect(escalationContent.consequences).toContain('selects nothing, grants nothing and approves nothing')
  expect(escalationContent.consequences).toContain('third critical review or resolution is not available')
  expect(escalationContent.consequences).toContain('Only a durably committed implementation claim consumes the authorization')
  expect(escalation.StructuredContentJson).not.toContain('spends the authorization')
  expect(escalation.StructuredContentJson).not.toContain('not implementable through this lineage')
  expect(escalationContent.evidence).toContain(rootProposal.Id.toLowerCase())
  expect(escalationContent.evidence).toContain(firstRevision.Id.toLowerCase())
  expect(escalationContent.evidence).toContain(finalProposal.Id.toLowerCase())
  expect(escalationContent.evidence).toContain(secondChallenges[0].Id.toLowerCase())

  // 8a. The cockpit explains the escalation and offers the human decision, but NOTHING that implements, reviews or resolves the final
  // plan: no implementation of it is offered before the authorization, and no third critical review or resolution at all.
  await expect(lineage).toContainText('The second challenge round is resolved')
  await expect(lineage).toContainText('Human decision required')
  await expect(lineage).toContainText('This record is not an approval')
  await expect(decision).toBeVisible({ timeout: 30_000 })
  await expect(decision).toContainText(`Final revised plan ${shortId(finalProposal.Id)} of escalation ${shortId(escalation.Id)}`, { timeout: 30_000 })
  await expect(decision).toContainText('1 second-round decision')
  await expect(decision).toContainText(shortId(secondDecisions[0].Id))
  await expect(decision).toContainText(/exactly one implementation claim/)
  await expect(decision).toContainText(/does not start an implementation/)
  await expect(page.getByRole('button', { name: /Implement/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)
  expect(data.planningAuthorizations()).toEqual([])
  await expect(page.getByText('Reserved Agent invocation time: 50 min of 120 min')).toBeVisible()

  // 8b. The required reason is enforced where it is typed: a blank reason is never sent, and nothing is recorded.
  const beforeAuthorization = {
    attempts: data.attempts().length,
    messages: data.messages().length,
    manifests: data.manifestArtifacts().length,
    checkpoints: data.checkpoints().length,
    executions: data.executions().length,
    reviews: data.checkpointReviews().length,
    invocations: stageInvocations().length,
  }
  await decision.getByRole('button', { name: 'Authorize one implementation claim' }).click()
  await expect(decision).toContainText('A reason is required.')
  expect(data.planningAuthorizations()).toEqual([])
  expect(data.messages()).toHaveLength(beforeAuthorization.messages)

  // 8c. The explicit authorization: one rendered form, one generated-client POST, exactly one HumanInstruction and one grant, and
  // no claim, reservation, manifest, provider invocation, checkpoint or approval.
  await decision.getByLabel(/Your reason for authorizing this final plan/).fill(REASON_DRAFT)
  const authorized = page.waitForResponse((response) => response.request().method() === 'POST' && AUTHORIZATION_PATH.test(new URL(response.url()).pathname))
  await decision.getByRole('button', { name: 'Authorize one implementation claim' }).click()
  expect((await authorized).status()).toBe(200)
  await expect(decision).toContainText('Authorized by a human', { timeout: 15_000 })
  await expect(decision).toContainText(`Recorded reason: ${REASON}`)
  await expect(decision).toContainText('No implementation has been claimed with it yet')
  await expect(decision.getByRole('button', { name: 'Authorize one implementation claim' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: IMPLEMENT_AUTHORIZED })).toBeVisible()
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)

  const grants = data.planningAuthorizations()
  expect(grants).toHaveLength(1)
  const grant = grants[0]
  expect(sameId(grant.EscalationMessageId, escalation.Id)).toBe(true)
  expect(sameId(grant.FinalProposalMessageId, finalProposal.Id)).toBe(true)
  expect(grant.ConsumedByAttemptId).toBeNull()
  const instructions = data.messages().filter((message) => message.Type === 'HumanInstruction')
  expect(instructions).toHaveLength(1)
  expect(sameId(instructions[0].Id, grant.HumanInstructionMessageId)).toBe(true)
  expect(sameId(instructions[0].InReplyToMessageId, escalation.Id)).toBe(true)
  expect(instructions[0].Provenance).toBe('HumanSubmitted')
  expect(instructions[0].AttemptId).toBeNull()
  expect((JSON.parse(instructions[0].StructuredContentJson) as { rationale: string }).rationale).toBe(REASON)
  expect(data.attempts()).toHaveLength(beforeAuthorization.attempts)
  expect(data.manifestArtifacts()).toHaveLength(beforeAuthorization.manifests)
  expect(data.checkpoints()).toHaveLength(beforeAuthorization.checkpoints)
  expect(data.executions()).toHaveLength(beforeAuthorization.executions)
  expect(data.checkpointReviews()).toHaveLength(beforeAuthorization.reviews)
  expect(stageInvocations()).toHaveLength(beforeAuthorization.invocations)
  expect(data.messages()).toHaveLength(beforeAuthorization.messages + 1)
  await expect(page.getByText('Reserved Agent invocation time: 50 min of 120 min')).toBeVisible()

  // 9. The separate, explicit implementation request of the exact final plan: the double edits the one plan-scoped file, and the grant
  // is consumed by exactly this claim.
  await page.getByRole('button', { name: IMPLEMENT_AUTHORIZED }).click()
  await expect(page.getByText('Last attempt #6: Implemented.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('Checkpoint ').filter({ hasText: '→' }).first()).toBeVisible()
  expect(agentContracts()).toEqual([
    'Proposal', 'CriticalReview', 'ChallengeResolution', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport',
  ])
  expect(data.executions()).toEqual([]) // nothing verified, diagnosed, or reviewed on its own
  const implementation = data.attempts()[5]
  expect(implementation.AgentResponseContract).toBe('ImplementationReport')
  expect(data.inputsOf(implementation.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([
    finalProposal.Id.toLowerCase(),
    ...secondDecisions.map((message) => message.Id.toLowerCase()),
    grant.HumanInstructionMessageId.toLowerCase(),
  ])
  expect(sameId(data.planningAuthorizations()[0].ConsumedByAttemptId, implementation.Id)).toBe(true)
  await expect(decision).toContainText('The authorization was used by an implementation claim', { timeout: 30_000 })
  await expect(decision).toContainText(`Recorded reason: ${REASON}`)
  await expect(page.getByRole('button', { name: IMPLEMENT_AUTHORIZED })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)

  // 10. Refresh evidence reads the checkpoint the implementation recorded; explicit local verification fails on the real edited file.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #2 · 1 changed files', { timeout: 30_000 })
  expect(data.checkpoints()).toHaveLength(2)
  expect(agentContracts()).toHaveLength(6)
  const firstClaim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM_PATH.test(new URL(response.url()).pathname))
  await verification.getByRole('button', { name: 'Run', exact: true }).click()
  expect((await firstClaim).status()).toBe(202)
  await expect(verification).toContainText('Last run: Failed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #1 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(data.executions().map((execution) => execution.Status)).toEqual(['Failed'])
  await verification.getByRole('button', { name: /Inspect stderr/ }).click()
  await expect(page.getByText('TOTAL-NOT-SUM').first()).toBeVisible({ timeout: 30_000 })
  expect(agentContracts()).toHaveLength(6)

  // The explicit refresh makes the failed verification diagnosable, and offers a decision but never an approval of a failed execution.
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toBeEnabled({ timeout: 30_000 })
  await expect(verification).toContainText('Last run: Failed')
  await expect(manual.getByRole('button', { name: 'Changes requested' })).toBeEnabled({ timeout: 30_000 })
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeDisabled()
  expect(agentContracts()).toHaveLength(6)
  expect(data.checkpoints()).toHaveLength(2)

  // Ordinary review of the authorized implementation is unavailable until verification Passed: the server refuses it, creating nothing.
  const refusal = page.waitForResponse((response) => response.url().endsWith(CODE_REVIEW_PATH) && response.request().method() === 'POST')
  await review.getByRole('button', { name: 'Request code review' }).click()
  expect((await refusal).status()).toBe(409)
  await expect(review).toContainText('has not Passed for the current checkpoint')
  expect(agentContracts()).toHaveLength(6)
  expect(readInvocations(mark).filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 11. Codex diagnosis of the failed verification, judging the final plan.
  await diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' }).click()
  await expect(diagnosis).toContainText('Last diagnosis #7: Diagnosis findings recorded.', { timeout: 60_000 })
  await expect(diagnosis).toContainText('#1 Candidate total check · execution 1 · Failed · exit code 1')
  await expect(page.getByText('Total does not add its operands.').first()).toBeVisible()
  expect(agentContracts()).toHaveLength(7)

  // 12. The one explicitly guided Claude correction of the diagnosed findings, through the rendered guidance form.
  await diagnosis.getByLabel('Direct guidance for this diagnosis correction').fill(CORRECTION_GUIDANCE_DRAFT)
  await diagnosis.getByRole('button', { name: 'Correct the diagnosed findings with guidance' }).click()
  await expect(diagnosis).toContainText('Last correction #8: Correction applied.', { timeout: 60_000 })
  await expect(diagnosis).toContainText('Direct human guidance supplied to this attempt')
  await expect(diagnosis).toContainText('whether the provider followed it is not observed')
  await expect(diagnosis).toContainText('Leave every other statement alone.')
  await expect(diagnosis).toContainText('1 of 2 used')
  expect(agentContracts()).toEqual([
    'Proposal', 'CriticalReview', 'ChallengeResolution', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport',
    'VerificationDiagnosis', 'ReviewCorrection',
  ])
  expect(data.executions()).toHaveLength(1) // the correction did not verify itself

  // 13. Refresh the evidence; fresh verification of the corrected checkpoint passes, observed without a reload.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #3', { timeout: 30_000 })
  expect(data.checkpoints()).toHaveLength(3)
  const secondClaim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM_PATH.test(new URL(response.url()).pathname))
  await verification.getByRole('button', { name: 'Run', exact: true }).click()
  expect((await secondClaim).status()).toBe(202)
  await expect(verification).toContainText('Last run: Passed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #2 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(data.executions().map((execution) => execution.Status)).toEqual(['Failed', 'Passed'])
  expect(agentContracts()).toHaveLength(8)

  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(review).not.toContainText('has not Passed for the current checkpoint', { timeout: 30_000 })
  await expect(review.getByRole('button', { name: 'Request code review' })).toBeEnabled()
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeEnabled({ timeout: 30_000 })
  expect(data.checkpointReviews()).toEqual([]) // no review of any kind exists yet, and the refreshes recorded none
  expect(agentContracts()).toHaveLength(8)
  expect(data.checkpoints()).toHaveLength(3)
  expect(data.executions()).toHaveLength(2)
  expect(readInvocations(mark).filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 14. Ordinary Codex review of the corrected report: approval.
  await review.getByRole('button', { name: 'Request code review' }).click()
  await expect(page.getByText('Last attempt #9: Implementation approved.')).toBeVisible({ timeout: 60_000 })

  // 14b. The separate, explicit Human checkpoint approval of the exact current checkpoint and its Passed execution: one more fact that
  // creates no attempt, message, invocation, claim, grant or lifecycle change, and never replaces the ordinary Agent approval.
  const agentReviews = data.checkpointReviews()
  expect(agentReviews.map((row) => [row.ActorKind, row.Decision, row.CheckpointNumber])).toEqual([['FutureAgent', 'Approved', 3]])
  const beforeManual = { attempts: data.attempts().length, messages: data.messages().length, invocations: stageInvocations().length }
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeEnabled({ timeout: 30_000 })
  await expect(manual).not.toContainText('could not be refreshed')
  await expect(manual.getByLabel('Reviewer')).toHaveValue('Human')
  expect(data.checkpointReviews()).toEqual(agentReviews) // the refresh recorded nothing
  const manualRecorded = page.waitForResponse((response) => response.request().method() === 'POST' && CHECKPOINT_REVIEW_PATH.test(new URL(response.url()).pathname))
  await manual.getByRole('button', { name: 'Approve', exact: true }).click()
  expect((await manualRecorded).status()).toBe(201)
  await expect(manual).toContainText('Approved')
  await expect(manual).toContainText('Checkpoint #3 · verification #2 · Human')
  await expect(manual).toContainText('Current checkpoint')
  await expect(manual).not.toContainText('This review decision could not be recorded.')
  await expect(page.getByText('Last attempt #9: Implementation approved.')).toBeVisible()
  expect(data.attempts()).toHaveLength(beforeManual.attempts)
  expect(data.messages()).toHaveLength(beforeManual.messages)
  expect(stageInvocations()).toHaveLength(beforeManual.invocations)

  // 15. The journey's only reload: persisted visible results stay attached to this run, the grant stays Consumed, and approval is not
  // lifecycle completion.
  await reloadAndReselect(page)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Last attempt #9: Implementation approved.')).toBeVisible()
  await expect(manual).toContainText('Checkpoint #3 · verification #2 · Human', { timeout: 30_000 })
  await expect(manual).toContainText('Current checkpoint')
  await expect(diagnosis).toContainText('Last correction #8: Correction applied.')
  await expect(decision).toContainText('The authorization was used by an implementation claim', { timeout: 30_000 })
  await expect(decision).toContainText(`Recorded reason: ${REASON}`)
  await expect(page.getByRole('button', { name: IMPLEMENT_AUTHORIZED })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)
  await expect(page.getByText('Completed · Completed')).toHaveCount(0)
  expect((await workflowLines(page)).join('\n')).not.toContain('Run completed')

  // ---- Evidence read back from the host's own records and the doubles' allowlisted log, scoped to this journey -------------------
  const all = data.attempts()
  expect(all.map((attempt) => attempt.AgentOutcome)).toEqual([
    'Proposed', 'Challenged', 'Resolved', 'Challenged', 'Resolved', 'Implemented', 'DiagnosisFindingsRecorded', 'CorrectionApplied',
    'ReviewApproved',
  ])
  expect(all).toHaveLength(9) // nine Agent claims in slots one through nine, within the unchanged default of 16
  expect(all.map((attempt) => attempt.AgentBudgetSlot)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9])
  const limits = data.runLimits()
  expect(limits.MaximumAgentAttempts).toBe(16)
  expect(limits.MaximumReviewCorrectionAttempts).toBe(2)
  expect(all.filter((attempt) => attempt.AgentResponseContract === 'ReviewCorrection')).toHaveLength(1) // one spent shared correction slot
  expect(limits.Lifecycle).not.toBe('Completed')
  await expect(page.getByText('Reserved Agent invocation time: 110 min of 120 min')).toBeVisible()

  const diagnosisAttempt = all[6]
  const correctionAttempt = all[7]
  const reviewAttempt = all[8]
  expect([diagnosisAttempt.AgentResponseContract, correctionAttempt.AgentResponseContract, reviewAttempt.AgentResponseContract]).toEqual([
    'VerificationDiagnosis', 'ReviewCorrection', 'ImplementationReview',
  ])
  const messages = data.messages()
  const executionReport = messages.find((message) => message.Type === 'ExecutionReport' && sameId(message.AttemptId, implementation.Id))!
  const findings = messages.filter((message) => message.Type === 'ReviewFinding' && sameId(message.AttemptId, diagnosisAttempt.Id))
  const correctedReport = messages.find((message) => message.Type === 'ExecutionReport' && sameId(message.AttemptId, correctionAttempt.Id))!
  expect(findings).toHaveLength(1)
  // The implementation report answers the final Proposal; the correction report keeps the accepted Planner-root reply identity.
  expect(sameId(executionReport.InReplyToMessageId, finalProposal.Id)).toBe(true)
  expect(sameId(correctedReport.InReplyToMessageId, rootProposal.Id)).toBe(true)
  expect(data.inputsOf(diagnosisAttempt.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([executionReport.Id.toLowerCase()])
  expect(data.inputsOf(correctionAttempt.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([
    executionReport.Id.toLowerCase(),
    ...findings.map((finding) => finding.Id.toLowerCase()),
  ])
  const revisionResponses = messages.filter((message) => message.Type === 'RevisionResponse' && sameId(message.AttemptId, correctionAttempt.Id))
  expect(revisionResponses.map((message) => message.InReplyToMessageId?.toLowerCase())).toEqual(findings.map((finding) => finding.Id.toLowerCase()))
  expect(sameId(data.inputsOf(reviewAttempt.Id)[0].CollaborationMessageId, correctedReport.Id)).toBe(true)
  expect(messages.filter((message) => message.Type === 'Escalation')).toHaveLength(1) // no further escalation, no third round
  expect(messages.filter((message) => message.Type === 'HumanInstruction')).toHaveLength(1)

  // The authorization stayed one relation, consumed exactly once by the implementation, through correction, review and the reload.
  const finalGrants = data.planningAuthorizations()
  expect(finalGrants).toHaveLength(1)
  expect(sameId(finalGrants[0].Id, grant.Id)).toBe(true)
  expect(sameId(finalGrants[0].ConsumedByAttemptId, implementation.Id)).toBe(true)
  expect(all.filter((attempt) => attempt.AgentResponseContract === 'ImplementationReport')).toHaveLength(1)

  // The doubles saw the FINAL plan in every plan-bearing stage and each round its own proposal and challenges; the human authorization
  // reached the implementation exactly as recorded. The identities are the host's own, so this holds whatever the doubles accepted.
  const invocations = readInvocations(mark)
  expect(
    escalatedLineageProblems(invocations, {
      rootId: rootProposal.Id,
      firstRevisionId: firstRevision.Id,
      finalId: finalProposal.Id,
      firstChallengeIds: firstChallenges.map((message) => message.Id),
      secondChallengeIds: secondChallenges.map((message) => message.Id),
      authorization: {
        authorizationId: grant.Id,
        escalationMessageId: escalation.Id,
        instructionMessageId: grant.HumanInstructionMessageId,
        rationaleSha256: sha256Hex(REASON),
      },
    }),
  ).toEqual([])
  expect(invocations.find((entry) => entry.contract === 'ImplementationReview')?.reportMessageId?.toLowerCase()).toBe(correctedReport.Id.toLowerCase())
  expect(invocations.find((entry) => entry.contract === 'VerificationDiagnosis')?.reportMessageId?.toLowerCase()).toBe(executionReport.Id.toLowerCase())
  expect(invocations.find((entry) => entry.contract === 'ReviewCorrection')?.reportMessageId?.toLowerCase()).toBe(executionReport.Id.toLowerCase())
  expect(stageInvocations().map((entry) => entry.contract ?? `verify:${entry.outcome}`)).toEqual([...ESCALATED_STAGE_SEQUENCE])

  // Project instruction context (ADR-0021): every one of the claimed stages received ITS OWN repository's tracked root AGENTS.md and
  // CLAUDE.md, complete and identity-verified, and nothing of the other journey's repository. Two independent views agree with the files
  // this journey itself committed: what each double observed about the section it was handed (identity facts only; the log never holds
  // the text) and the sealed manifests read back, read-only, from the owned artifact root. The files are tracked and clean, so they
  // added no changed path to any checkpoint (asserted above), and the doubles still edited only the one plan-scoped candidate file.
  const manifestRows = data.manifestArtifacts()
  const sealedManifests = all.map((attempt) => {
    const row = manifestRows.find((candidate) => sameId(candidate.AttemptId, attempt.Id))!
    return { attemptId: attempt.Id, text: readSealedManifestText(row.RelativeStoragePath), byteLength: row.ByteLength, contentHash: row.ContentHash }
  })
  expect(
    instructionDeliveryProblems(
      invocations.filter((entry) => entry.role !== 'verify' && entry.kind !== 'probe'),
      sealedManifests,
      { files: instructionFiles('escalated-source'), workspaceId: data.workspace().Id, foreignFiles: instructionFiles('journey-source') },
      all.map((attempt) => attempt.AgentResponseContract),
      readInvocationLogText(mark),
    ),
  ).toEqual([])

  // Attested tracked-change text (ADR-0024): the one tracked file the doubles edit is a regular single-name file of this journey's own
  // worktree, so every stage claimed after an edit received the host comparison of the committed HEAD text and the proven current bytes
  // in its sealed manifest, and no stage received any before the first edit. The patches are applied to the text this journey committed
  // with an independent applier, and the last stage's rebuilds the worktree file exactly as it is now.
  const candidateNow = readFileSync(join(data.workspace().WorkspacePath, 'src', 'Feature.cs'), 'utf8')
  expect(
    trackedDeliveryProblems(sealedManifests, all.map((attempt) => attempt.AgentResponseContract), {
      path: CANDIDATE_RELATIVE_PATH,
      baseline: CANDIDATE_BASELINE,
      currentByContract: {
        Proposal: null,
        CriticalReview: null,
        ChallengeResolution: null,
        ImplementationReport: null,
        VerificationDiagnosis: CANDIDATE_BASELINE.replace('return 0;', 'return left - right;'),
        ReviewCorrection: CANDIDATE_BASELINE.replace('return 0;', 'return left - right;'),
        ImplementationReview: CANDIDATE_BASELINE.replace('return 0;', 'return left + right;'),
      },
      finalContract: 'ImplementationReview',
      finalWorktreeText: candidateNow,
    }),
  ).toEqual([])

  // A real fingerprint/checkpoint change at each mutation, with verification bound to the checkpoint it judged.
  const cps = data.checkpoints()
  expect(cps.map((checkpoint) => checkpoint.CheckpointNumber)).toEqual([1, 2, 3])
  expect(new Set(cps.map((checkpoint) => checkpoint.FingerprintSha256)).size).toBe(3)
  expect(cps.every((checkpoint) => checkpoint.HeadCommitSha === repository.baselineCommit)).toBe(true)
  const runs = data.executions()
  expect(runs.map((execution) => [execution.Status, execution.ExitCode])).toEqual([['Failed', 1], ['Passed', 0]])
  expect(sameId(runs[0].GitCheckpointId, cps[1].Id)).toBe(true)
  expect(sameId(runs[1].GitCheckpointId, cps[2].Id)).toBe(true)
  expect(runs[0].CompletionFingerprintSha256).toBe(cps[1].FingerprintSha256)
  expect(runs[1].CompletionFingerprintSha256).toBe(cps[2].FingerprintSha256)

  // Two distinct approvals of checkpoint three: the ordinary Agent approval (the ninth attempt's outcome) and the separate Human fact.
  const reviewFacts = data.checkpointReviews()
  expect(reviewFacts.map((row) => [row.ActorKind, row.Decision, row.CheckpointNumber])).toEqual([['FutureAgent', 'Approved', 3], ['Human', 'Approved', 3]])
  expect(sameId(reviewFacts[0].Id, agentReviews[0].Id)).toBe(true) // the ordinary approval's fact is unchanged
  const manualReview = reviewFacts[1]
  expect(sameId(manualReview.Id, agentReviews[0].Id)).toBe(false)
  expect(manualReview.CheckpointFingerprintSha256).toBe(cps[2].FingerprintSha256)
  expect(sameId(manualReview.GitCheckpointId, cps[2].Id)).toBe(true)
  const manualEvidence = data.checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, manualReview.Id))
  expect(manualEvidence).toHaveLength(1)
  expect(sameId(manualEvidence[0].VerificationExecutionId, runs[1].Id)).toBe(true)
  expect(manualEvidence[0]).toMatchObject({
    VerificationExecutionNumber: 2,
    VerificationExecutionCheckpointFingerprintSha256: cps[2].FingerprintSha256,
    VerificationExecutionStatus: 'Passed',
    VerificationExecutionExitCode: 0,
  })
  expect(all.filter((attempt) => attempt.AgentOutcome === 'ReviewApproved')).toHaveLength(1)
  expect(all.filter((attempt) => attempt.AgentResponseContract === 'ImplementationReview')).toHaveLength(1)

  // Direct guidance: exactly the one explicitly guided correction recorded the normalized text, and the authorization rationale is a
  // separate fact that no attempt recorded as direct guidance. Neither text ever reached the doubles' log; only hashes did.
  expect(data.directGuidanceByAttempt().map((row) => [row.AttemptNumber, row.AgentDirectHumanGuidance])).toEqual(
    all.map((attempt) => [attempt.AttemptNumber, attempt.AttemptNumber === correctionAttempt.AttemptNumber ? CORRECTION_GUIDANCE : null]),
  )
  const guidedEntries = invocations.filter((entry) => entry.contract === 'ReviewCorrection')
  expect(guidedEntries).toHaveLength(1)
  expect(guidedEntries[0].guidanceSha256).toBe(sha256Hex(CORRECTION_GUIDANCE))
  expect(guidedEntries[0].guidanceBoundary).toBe('fixed')
  expect(invocations.filter((entry) => entry.contract !== 'ReviewCorrection' && entry.guidanceSha256 !== undefined)).toEqual([])
  const logText = readInvocationLogText(mark)
  for (const text of [REASON, 'Change only the one statement', 'Leave every other statement alone', 'accept it for one implementation']) {
    expect(logText).not.toContain(text)
  }

  // No agent ever committed or pushed: the worktree still has the single baseline commit and the original repository is untouched.
  const worktree = data.workspace().WorkspacePath
  const git = (...args: string[]) => execFileSync('git', ['-C', worktree, ...args], { stdio: 'pipe' }).toString().trim()
  expect(git('rev-list', '--count', 'HEAD')).toBe('1')
  expect(git('rev-parse', 'HEAD')).toBe(repository.baselineCommit)
  expect(git('remote')).toBe('')
  expect(readFileSync(join(worktree, 'src', 'Feature.cs'), 'utf8')).toContain('return left + right;')
  const sourceAfter = repositorySnapshot(repository.path)
  expect(sourceAfter.head).toBe(sourceBefore.head)
  expect(sourceAfter.status).toBe(sourceBefore.status)
  expect(sourceAfter.candidate).toBe(sourceBefore.candidate)
  const refs = sourceAfter.branches.split('\n').filter((line) => line.length > 0)
  expect(refs.filter((line) => !line.includes('refs/heads/devalcopilot/workspace/'))).toEqual(
    sourceBefore.branches.split('\n').filter((line) => line.length > 0),
  )
  expect(refs.filter((line) => line.includes('refs/heads/devalcopilot/workspace/'))).toHaveLength(1)
  expect(refs.every((line) => line.endsWith(` ${repository.baselineCommit}`))).toBe(true)

  // The launch secret never reached the page URL or browser storage.
  expect(page.url()).not.toContain(journeySecret())
  expect(await page.evaluate(() => JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage))).not.toContain(journeySecret())
})
