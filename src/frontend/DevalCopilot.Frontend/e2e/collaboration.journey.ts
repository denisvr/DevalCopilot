import { execFileSync } from 'node:child_process'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import {
  attempts,
  checkpointReviewEvidence,
  checkpointReviews,
  checkpoints,
  directGuidanceByAttempt,
  executions,
  inputsOf,
  messages,
  runLimits,
  sameId,
} from './journey/journeyDb'
import { normalizeGuidance, sha256Hex } from './journey/guidance'
import {
  createJourneyRepository,
  journeyRoot,
  journeySecret,
  readInvocationLogText,
  readInvocations,
  readLaunchTargetVerdict,
  repositorySnapshot,
} from './journey/journeyEnv'
import { planIdentityProblems } from './journey/planIdentity'
import { injectJourneySession, registerProject, selectProject, workflowLines } from './journey/journeySupport'

// The browser-driven local collaboration proof (Increment 4): ONE explicit manual journey through the rendered controls of the
// production frontend, over the real generated client, MVC authentication, mediator, SQLite, supervisors, provider adapters, process
// executor, Git/worktree evidence and artifact stores. Only the external provider executables are deterministic doubles
// (tests/DevalCopilot.Api.IntegrationTests/BrowserJourney); no real provider is ever invoked, so this proves the assembled local
// workflow, not provider reliability and not completion of the increment. Every workflow mutation below originates from a rendered
// control; the database and the doubles' invocation log are only READ to supplement what the page shows.

const PROJECT = 'Collaboration journey fixture'
const OBJECTIVE = 'Implement the ledger total in the Feature file'
const CODE_REVIEW_PATH = '/agent-attempts/code-review'
// Harmless advisory guidance typed into the rendered form of the diagnosis correction, with whitespace the host normalizes away.
const CORRECTION_GUIDANCE_DRAFT = '  Keep the change inside the one file the findings name.\nLeave every other file alone.  '
const CORRECTION_GUIDANCE = normalizeGuidance(CORRECTION_GUIDANCE_DRAFT)
const VERIFICATION_CLAIM_PATH = /\/api\/projects\/[^/]+\/verification-commands\/[^/]+\/executions$/
const CHECKPOINT_REVIEW_PATH = /\/api\/projects\/[^/]+\/reviews$/

// The doubles' stage invocations (the capability probes of the host's own supervisors are not workflow stages).
function stageInvocations() {
  return readInvocations().filter((entry) => entry.kind !== 'probe')
}

function agentContracts(): string[] {
  return attempts().map((attempt) => attempt.AgentResponseContract)
}

async function reloadAndReselect(page: Page) {
  await page.reload()
  await selectProject(page, PROJECT)
}

test('the collaboration journey: failed verification, diagnosis, correction, fresh verification, and ordinary approval', async ({ page }) => {
  test.setTimeout(240_000)
  const repository = createJourneyRepository('journey-source')
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

  // 1-2. Register and explicitly select the project, recheck its physical identity, prepare its real candidate worktree, capture
  // the checkpoint, configure the verification command, and record a ManualAgent objective.
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

  // 3. Codex planning, explicitly requested.
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Plan proposed.')).toBeVisible({ timeout: 60_000 })
  expect(agentContracts()).toEqual(['Proposal'])

  // 4. Claude critical review produces a material Challenge.
  await page.getByRole('button', { name: 'Request Claude review' }).click()
  await expect(page.getByText('Last attempt #2: Proposal challenged.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('The lookup table duplicates the arithmetic.').first()).toBeVisible()
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview'])

  // 5. Codex resolution: a first revision that differs substantively from the Planner root.
  await page.getByRole('button', { name: 'Resolve challenges with Codex' }).click()
  await expect(page.getByText('Last attempt #3: Challenges resolved.')).toBeVisible({ timeout: 60_000 })
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution'])
  const rootProposal = messages().find((message) => message.Type === 'Proposal' && attempts().find((a) => sameId(a.Id, message.AttemptId))?.AgentResponseContract === 'Proposal')
  const revisedProposal = messages().find(
    (message) => message.Type === 'Proposal' && attempts().find((a) => sameId(a.Id, message.AttemptId))?.AgentResponseContract === 'ChallengeResolution',
  )
  expect(rootProposal && revisedProposal).toBeTruthy()
  expect(sameId(rootProposal!.Id, revisedProposal!.Id)).toBe(false)
  expect(rootProposal!.Summary).not.toEqual(revisedProposal!.Summary)

  // 6. Claude implements the revised Proposal: the double edits the one plan-scoped file of the real candidate worktree.
  await page.getByRole('button', { name: 'Implement the resolved plan with Claude', exact: true }).click()
  await expect(page.getByText('Last attempt #4: Implemented.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('Checkpoint ').filter({ hasText: '→' }).first()).toBeVisible()
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport'])
  expect(executions()).toEqual([]) // nothing verified, diagnosed, or reviewed on its own
  const implementation = attempts().find((a) => a.AgentResponseContract === 'ImplementationReport')!
  expect(sameId(inputsOf(implementation.Id)[0].CollaborationMessageId, revisedProposal!.Id)).toBe(true)

  // 7. The explicit Refresh evidence control reads the checkpoint the implementation recorded into Source evidence, Verification
  // commands and Checkpoint review; it records no checkpoint and starts no stage. Then explicit local verification: the host accepts
  // the claim with HTTP 202 over the authenticated generated client, and the failed result appears without a reload.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #2 · 1 changed files', { timeout: 30_000 })
  expect(checkpoints()).toHaveLength(2)
  expect(agentContracts()).toHaveLength(4)
  const firstClaim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM_PATH.test(new URL(response.url()).pathname))
  await verification.getByRole('button', { name: 'Run', exact: true }).click()
  expect((await firstClaim).status()).toBe(202)
  await expect(verification).toContainText('Last run: Failed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #1 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(executions().map((e) => e.Status)).toEqual(['Failed'])
  await verification.getByRole('button', { name: /Inspect stderr/ }).click()
  await expect(page.getByText('TOTAL-NOT-SUM').first()).toBeVisible({ timeout: 30_000 })
  expect(agentContracts()).toHaveLength(4)

  // The completed verification is project-scoped and emits no run event, so the run's diagnosis display is still the one read before
  // it. Refresh evidence reads the host's current diagnosis and review status again (reads only), and the run then shows the facts the
  // host holds: the failed verification is diagnosable, before any diagnosis is requested.
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toBeEnabled({ timeout: 30_000 })
  await expect(diagnosis).not.toContainText('Verification has not been run for the current source')
  // The same explicit refresh shows the failed verification as current evidence in the verification panel and in the manual review
  // panel: a decision that needs evidence is offered, but never an approval of a failed execution. Nothing is clicked.
  await expect(verification).toContainText('Last run: Failed')
  await expect(verification).not.toContainText('Reading verification status…')
  await expect(manual.getByRole('button', { name: 'Changes requested' })).toBeEnabled({ timeout: 30_000 })
  await expect(manual.getByRole('button', { name: 'Approve' })).toBeDisabled()
  await expect(manual).not.toContainText('Reading verification evidence…')
  expect(agentContracts()).toHaveLength(4)
  expect(checkpoints()).toHaveLength(2)
  expect(executions()).toHaveLength(1)

  // 8. Ordinary review is unavailable: the rendered control submits, and the server's non-Passed gate refuses it without creating
  // an attempt or an invocation.
  const refusal = page.waitForResponse((response) => response.url().endsWith(CODE_REVIEW_PATH) && response.request().method() === 'POST')
  await review.getByRole('button', { name: 'Request code review' }).click()
  expect((await refusal).status()).toBe(409)
  await expect(review).toContainText("has not Passed for the current checkpoint")
  expect(agentContracts()).toHaveLength(4)
  expect(readInvocations().filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 9. Codex diagnosis of the failed verification (made available by the refresh above); its findings are inspected on the page.
  await diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' }).click()
  await expect(diagnosis).toContainText('Last diagnosis #5: Diagnosis findings recorded.', { timeout: 60_000 })
  await expect(diagnosis).toContainText('#1 Candidate total check · execution 1 · Failed · exit code 1')
  await expect(page.getByText('Total does not add its operands.').first()).toBeVisible()
  expect(agentContracts()).toHaveLength(5)

  // 10. Claude diagnosis-origin correction, explicitly requested through the rendered guidance form: the double edits the actual
  // candidate file back, and receives the exact normalized guidance the host sealed.
  await diagnosis.getByLabel('Direct guidance for this diagnosis correction').fill(CORRECTION_GUIDANCE_DRAFT)
  await diagnosis.getByRole('button', { name: 'Correct the diagnosed findings with guidance' }).click()
  await expect(diagnosis).toContainText('Last correction #6: Correction applied.', { timeout: 60_000 })
  await expect(diagnosis).toContainText('Direct human guidance supplied to this attempt')
  await expect(diagnosis).toContainText('whether the provider followed it is not observed')
  await expect(diagnosis).toContainText('Leave every other file alone.')
  await expect(diagnosis).toContainText('1 of 2 used')
  expect(agentContracts()).toEqual([
    'Proposal', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport', 'VerificationDiagnosis', 'ReviewCorrection',
  ])
  expect(executions()).toHaveLength(1) // the correction did not verify itself

  // 11. Refresh the evidence, then fresh verification of the corrected checkpoint passes, observed without a reload.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #3', { timeout: 30_000 })
  expect(checkpoints()).toHaveLength(3)
  const secondClaim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM_PATH.test(new URL(response.url()).pathname))
  await verification.getByRole('button', { name: 'Run', exact: true }).click()
  expect((await secondClaim).status()).toBe(202)
  await expect(verification).toContainText('Last run: Passed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #2 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(executions().map((e) => e.Status)).toEqual(['Failed', 'Passed'])
  expect(agentContracts()).toHaveLength(6)

  // Refresh evidence once more after the terminal verification: the run reads the host's current diagnosis and review status again,
  // shows no refusal reported for an earlier verification, offers the ordinary review, and no longer offers a diagnosis. Nothing is
  // requested, claimed or captured.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(review).not.toContainText('has not Passed for the current checkpoint', { timeout: 30_000 })
  await expect(review.getByRole('button', { name: 'Request code review' })).toBeEnabled()
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  // The Passed verification of the corrected checkpoint is current in both evidence views after this refresh, before any decision.
  await expect(verification).toContainText('Last run: Passed')
  await expect(verification).not.toContainText('Reading verification status…')
  await expect(manual.getByRole('button', { name: 'Approve' })).toBeEnabled({ timeout: 30_000 })
  await expect(manual).not.toContainText('Reading verification evidence…')
  expect(checkpointReviews()).toEqual([]) // no manual review exists yet, and the refresh recorded none
  expect(agentContracts()).toHaveLength(6)
  expect(checkpoints()).toHaveLength(3)
  expect(executions()).toHaveLength(2)
  expect(readInvocations().filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 12. Ordinary Codex review of the corrected report: approval.
  await review.getByRole('button', { name: 'Request code review' }).click()
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible({ timeout: 60_000 })

  // 12b. The explicit manual checkpoint review: after the ordinary Agent approval and an explicit Refresh evidence (reads only, no
  // reload), the rendered manual panel offers Approve for the exact current checkpoint and its Passed execution, and the host records
  // one Human Approved fact over the authenticated generated client's HTTP 201. It is a separate, human-authored fact: it creates no
  // attempt, message, invocation, claim, grant or lifecycle change. The ordinary approval above already recorded its own FutureAgent
  // review fact for the same checkpoint; the manual one is a second, distinct fact and never replaces or rewrites it.
  const agentReviews = checkpointReviews()
  expect(agentReviews.map((row) => [row.ActorKind, row.Decision, row.CheckpointNumber])).toEqual([['FutureAgent', 'Approved', 3]])
  const beforeManual = { attempts: attempts().length, messages: messages().length, invocations: stageInvocations().length }
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(manual.getByRole('button', { name: 'Approve' })).toBeEnabled({ timeout: 30_000 })
  await expect(manual).not.toContainText('Reading verification evidence…')
  await expect(manual).not.toContainText('could not be refreshed')
  await expect(manual.getByLabel('Reviewer')).toHaveValue('Human')
  expect(checkpointReviews()).toEqual(agentReviews) // the refresh recorded nothing
  expect(attempts()).toHaveLength(beforeManual.attempts)
  const manualRecorded = page.waitForResponse(
    (response) => response.request().method() === 'POST' && CHECKPOINT_REVIEW_PATH.test(new URL(response.url()).pathname),
  )
  await manual.getByRole('button', { name: 'Approve' }).click()
  expect((await manualRecorded).status()).toBe(201)
  await expect(manual).toContainText('Approved')
  await expect(manual).toContainText('Checkpoint #3 · verification #2 · Human')
  await expect(manual).toContainText('Current checkpoint')
  await expect(manual).not.toContainText('This review decision could not be recorded.')
  await expect(manual).not.toContainText('Review evidence could not be loaded.')
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible()
  expect(attempts()).toHaveLength(beforeManual.attempts)
  expect(messages()).toHaveLength(beforeManual.messages)
  expect(stageInvocations()).toHaveLength(beforeManual.invocations)

  // 13. Persisted visible results stay attached to this run after the journey's only reload; approval is not lifecycle completion.
  await reloadAndReselect(page)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible()
  await expect(manual).toContainText('Checkpoint #3 · verification #2 · Human', { timeout: 30_000 })
  await expect(manual).toContainText('Current checkpoint')
  await expect(diagnosis).toContainText('Last correction #6: Correction applied.')
  await expect(page.getByText('Completed · Completed')).toHaveCount(0)
  expect((await workflowLines(page)).join('\n')).not.toContain('Run completed')

  // ---- Evidence read back from the host's own records and the doubles' allowlisted log ---------------------------------------
  const all = attempts()
  expect(all.map((a) => a.AgentOutcome)).toEqual([
    'Proposed', 'Challenged', 'Resolved', 'Implemented', 'DiagnosisFindingsRecorded', 'CorrectionApplied', 'ReviewApproved',
  ])
  const limits = runLimits()
  expect(all).toHaveLength(7) // seven Agent claims, within the unchanged default of 16
  expect(limits.MaximumAgentAttempts).toBe(16)
  expect(limits.MaximumReviewCorrectionAttempts).toBe(2)
  expect(all.filter((a) => a.AgentResponseContract === 'ReviewCorrection')).toHaveLength(1) // one spent shared correction slot
  expect(limits.Lifecycle).not.toBe('Completed')
  await expect(page.getByText('Reserved Agent invocation time: 90 min of 120 min')).toBeVisible()

  const diagnosisAttempt = all.find((a) => a.AgentResponseContract === 'VerificationDiagnosis')!
  const correctionAttempt = all.find((a) => a.AgentResponseContract === 'ReviewCorrection')!
  const reviewAttempt = all.find((a) => a.AgentResponseContract === 'ImplementationReview')!
  const executionReport = messages().find((m) => m.Type === 'ExecutionReport' && sameId(m.AttemptId, implementation.Id))!
  const findings = messages().filter((m) => m.Type === 'ReviewFinding' && sameId(m.AttemptId, diagnosisAttempt.Id))
  const correctedReport = messages().find((m) => m.Type === 'ExecutionReport' && sameId(m.AttemptId, correctionAttempt.Id))!
  expect(findings).toHaveLength(1)
  expect(inputsOf(diagnosisAttempt.Id).map((i) => i.CollaborationMessageId.toLowerCase())).toEqual([executionReport.Id.toLowerCase()])
  expect(inputsOf(correctionAttempt.Id).map((i) => i.CollaborationMessageId.toLowerCase())).toEqual([
    executionReport.Id.toLowerCase(),
    ...findings.map((f) => f.Id.toLowerCase()),
  ])
  const revisionResponses = messages().filter((m) => m.Type === 'RevisionResponse' && sameId(m.AttemptId, correctionAttempt.Id))
  expect(revisionResponses.map((m) => m.InReplyToMessageId?.toLowerCase())).toEqual(findings.map((f) => f.Id.toLowerCase()))
  expect(sameId(inputsOf(reviewAttempt.Id)[0].CollaborationMessageId, correctedReport.Id)).toBe(true)

  // Diagnosis and review judged the revised Proposal, never the Planner root (see also planIdentity.test.ts).
  const invocations = readInvocations()
  expect(planIdentityProblems(invocations, { rootId: rootProposal!.Id, revisedId: revisedProposal!.Id })).toEqual([])
  expect(invocations.find((entry) => entry.contract === 'ImplementationReview')?.reportMessageId?.toLowerCase()).toBe(correctedReport.Id.toLowerCase())
  expect(invocations.find((entry) => entry.contract === 'VerificationDiagnosis')?.reportMessageId?.toLowerCase()).toBe(executionReport.Id.toLowerCase())

  // A real fingerprint/checkpoint change at each mutation, with verification bound to the checkpoint it judged.
  const cps = checkpoints()
  expect(cps.map((c) => c.CheckpointNumber)).toEqual([1, 2, 3])
  expect(new Set(cps.map((c) => c.FingerprintSha256)).size).toBe(3)
  expect(cps.every((c) => c.HeadCommitSha === repository.baselineCommit)).toBe(true)
  const runs = executions()
  expect(runs.map((e) => [e.Status, e.ExitCode])).toEqual([['Failed', 1], ['Passed', 0]])
  expect(sameId(runs[0].GitCheckpointId, cps[1].Id)).toBe(true)
  expect(sameId(runs[1].GitCheckpointId, cps[2].Id)).toBe(true)
  expect(runs[0].CompletionFingerprintSha256).toBe(cps[1].FingerprintSha256)
  expect(runs[1].CompletionFingerprintSha256).toBe(cps[2].FingerprintSha256)

  // The manual review is one persisted Human Approved fact on the exact current checkpoint, binding exactly the Passed execution of that
  // checkpoint (not the earlier Failed one), and it is separate from the ordinary Agent approval: that approval is the seventh attempt's
  // outcome and message, this is a checkpoint review row that no attempt, message, claim or lifecycle owns.
  const reviewFacts = checkpointReviews()
  expect(reviewFacts.map((row) => [row.ActorKind, row.Decision])).toEqual([['FutureAgent', 'Approved'], ['Human', 'Approved']])
  expect(sameId(reviewFacts[0].Id, agentReviews[0].Id)).toBe(true) // the ordinary approval's fact is unchanged
  const manualReview = reviewFacts[1]
  expect(sameId(manualReview.Id, agentReviews[0].Id)).toBe(false)
  expect(manualReview).toMatchObject({ CheckpointNumber: 3, CheckpointFingerprintSha256: cps[2].FingerprintSha256 })
  expect(sameId(manualReview.GitCheckpointId, cps[2].Id)).toBe(true)
  const manualEvidence = checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, manualReview.Id))
  expect(manualEvidence).toHaveLength(1)
  expect(sameId(manualEvidence[0].VerificationExecutionId, runs[1].Id)).toBe(true)
  expect(manualEvidence[0]).toMatchObject({
    VerificationExecutionNumber: 2,
    VerificationExecutionCheckpointFingerprintSha256: cps[2].FingerprintSha256,
    VerificationExecutionStatus: 'Passed',
    VerificationExecutionExitCode: 0,
  })
  expect(all.filter((a) => a.AgentOutcome === 'ReviewApproved')).toHaveLength(1) // seven attempts: the manual review claimed none
  expect(all.filter((a) => a.AgentResponseContract === 'ImplementationReview')).toHaveLength(1) // the one Agent review, still Codex's

  // Direct guidance: exactly the one explicitly guided correction recorded the normalized text; every other attempt recorded none. The native
  // double received that exact sealed value inside the host's fixed boundary (it logs only the text's hash), and the text itself never
  // reached its log. This proves local agreement of the sealed context, not that a provider would follow the guidance.
  expect(directGuidanceByAttempt().map((row) => [row.AttemptNumber, row.AgentDirectHumanGuidance])).toEqual(
    all.map((a) => [a.AttemptNumber, a.AttemptNumber === correctionAttempt.AttemptNumber ? CORRECTION_GUIDANCE : null]),
  )
  const guidedEntries = invocations.filter((entry) => entry.contract === 'ReviewCorrection')
  expect(guidedEntries).toHaveLength(1)
  expect(guidedEntries[0].guidanceSha256).toBe(sha256Hex(CORRECTION_GUIDANCE))
  expect(guidedEntries[0].guidanceBoundary).toBe('fixed')
  expect(invocations.filter((entry) => entry.contract !== 'ReviewCorrection' && entry.guidanceSha256 !== undefined)).toEqual([])
  expect(readInvocationLogText()).not.toContain('Keep the change inside')
  expect(readInvocationLogText()).not.toContain('Leave every other file alone')

  // The doubles' own log: exactly the contracts requested, in order, each served once; the verification executable saw real content.
  expect(invocations.filter((e) => e.kind !== 'probe').map((e) => e.contract ?? `verify:${e.outcome}`)).toEqual([
    'Proposal', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport', 'verify:failed', 'VerificationDiagnosis', 'ReviewCorrection',
    'verify:passed', 'ImplementationReview',
  ])

  // No agent ever committed or pushed: the worktree still has the single baseline commit and the original repository is untouched.
  const workspaces = join(journeyRoot().root, 'workspaces')
  const worktree = join(workspaces, readdirSync(workspaces)[0], '1')
  const git = (...args: string[]) => execFileSync('git', ['-C', worktree, ...args], { stdio: 'pipe' }).toString().trim()
  expect(git('rev-list', '--count', 'HEAD')).toBe('1')
  expect(git('rev-parse', 'HEAD')).toBe(repository.baselineCommit)
  expect(git('remote')).toBe('')
  expect(readFileSync(join(worktree, 'src', 'Feature.cs'), 'utf8')).toContain('return left + right;')
  const sourceAfter = repositorySnapshot(repository.path)
  expect(sourceAfter.head).toBe(sourceBefore.head)
  expect(sourceAfter.status).toBe(sourceBefore.status)
  expect(sourceAfter.candidate).toBe(sourceBefore.candidate)
  // The only new ref is the tool-owned worktree branch, still at the baseline commit; the source branches are untouched.
  const refs = sourceAfter.branches.split('\n').filter((line) => line.length > 0)
  expect(refs.filter((line) => !line.includes('refs/heads/devalcopilot/workspace/'))).toEqual(
    sourceBefore.branches.split('\n').filter((line) => line.length > 0),
  )
  expect(refs.filter((line) => line.includes('refs/heads/devalcopilot/workspace/'))).toHaveLength(1)
  expect(refs.every((line) => line.endsWith(` ${repository.baselineCommit}`))).toBe(true)

  // The launch secret never reached the page URL, browser storage, or any file under the owned root.
  expect(page.url()).not.toContain(journeySecret())
  expect(await page.evaluate(() => JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage))).not.toContain(journeySecret())
  const secret = Buffer.from(journeySecret())
  const walk = (directory: string): string[] =>
    readdirSync(directory, { withFileTypes: true }).flatMap((entry) => (entry.isDirectory() ? walk(join(directory, entry.name)) : [join(directory, entry.name)]))
  const leaking = walk(journeyRoot().root).filter((file) => !file.includes(`${join('workspaces')}`) && readFileSync(file).includes(secret))
  expect(leaking).toEqual([])
})
