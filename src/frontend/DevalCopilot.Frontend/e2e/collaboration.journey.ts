import { execFileSync } from 'node:child_process'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { JourneyData, sameId } from './journey/journeyDb'
import type { CollaborationHistoryRow, EventHeaderRow } from './journey/journeyDb'
import { normalizeGuidance, sha256Hex } from './journey/guidance'
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
import { planIdentityProblems } from './journey/planIdentity'
import { canonicalSnapshot, EXPECTED_MODEL_LIMITS, renderedRows, responseMember } from './journey/modelContextLimits'
import type { ExpectedModelLimit } from './journey/modelContextLimits'
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
const RECEIPT_PATH = /\/api\/runs\/[^/]+\/local-delivery-receipt$/
const HISTORY_PATH = /\/api\/projects\/[^/]+\/run-history$/
const EVENT_HISTORY_PATH = /\/api\/runs\/[^/]+\/event-history$/
const COLLABORATION_HISTORY_PATH = /\/api\/runs\/[^/]+\/collaboration-history$/
const REPLY_PATH = /\/api\/runs\/[^/]+\/collaboration-history\/[^/]+\/reply$/
const ATTEMPT_PATH = /\/api\/runs\/[^/]+\/collaboration-history\/[^/]+\/attempt$/
const FOLLOW_UP_OBJECTIVE = 'Plan the follow-up report for the ledger'
// The persisted Human review names both recipes' latest Passed executions of the corrected checkpoint (listed in the host's own order).
const HUMAN_BOTH_RUNS = /Checkpoint #3 · verification (#3, #4|#4, #3) · Human/

// This journey reads only the rows of its own project and run, and only the doubles' log entries written after it began, so it shares
// the host, database and log with any other journey without depending on which of them ran first.
const data = new JourneyData(PROJECT)
let mark = 0

// The doubles' stage invocations (the capability probes of the host's own supervisors are not workflow stages).
function stageInvocations() {
  return readInvocations(mark).filter((entry) => entry.kind !== 'probe')
}

function agentContracts(): string[] {
  return data.attempts().map((attempt) => attempt.AgentResponseContract)
}

// One rendered recipe row of the Verification commands panel: each recipe has its own Run control and last-run status.
const recipeRow = (verification: Locator, number: number) =>
  verification.locator('.dc-verification-command').filter({ hasText: `#${number} Candidate` })

// Explicit local verification of one recipe through its own rendered Run control; the host accepts the claim with HTTP 202.
async function runRecipe(page: Page, verification: Locator, number: number) {
  const claim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM_PATH.test(new URL(response.url()).pathname))
  await recipeRow(verification, number).getByRole('button', { name: 'Run', exact: true }).click()
  expect((await claim).status()).toBe(202)
}

const lower = (value: string) => value.toLowerCase()

async function reloadAndReselect(page: Page) {
  await page.reload()
  await selectProject(page, PROJECT)
}

// Opens the run's Agent attempt history if it is closed, inspects one attempt through its rendered control, and returns the evidence the
// generated client received from the host for exactly that attempt (observed on the wire) beside the rendered detail. The attempt must not
// be the one already selected: selecting it again requests nothing.
async function inspectAttempt(page: Page, attemptNumber: number) {
  const history = page.getByRole('region', { name: 'Agent attempt history' })
  const show = history.getByRole('button', { name: 'Show Agent attempt history' })
  if ((await show.count()) > 0) {
    await show.click()
  }

  const attempt = data.attempts().find((row) => row.AttemptNumber === attemptNumber)!
  const evidencePath = new RegExp(`/api/runs/[^/]+/agent-attempts/${attempt.Id}/evidence$`, 'i')
  const answered = page.waitForResponse(
    (response) => response.request().method() === 'GET' && evidencePath.test(new URL(response.url()).pathname),
    { timeout: 30_000 },
  )
  await history.getByRole('button', { name: `Inspect attempt ${attemptNumber}` }).click()
  const response = await answered
  expect(response.status()).toBe(200)
  const detail = page.getByRole('region', { name: 'Selected Agent attempt evidence' })
  await expect(detail).toContainText(`Attempt #${attemptNumber}`)
  return { attempt, evidence: await response.json(), detail }
}

async function expectRenderedModelLimits(detail: Locator, models: readonly ExpectedModelLimit[]) {
  const region = detail.getByRole('region', { name: 'Claude-reported model limits' })
  await expect(region).toBeVisible()
  await expect(region).toContainText('Models listed by Claude in its result for this attempt (not independently proven to have been used):')
  await expect(region).toContainText('Remaining context and the capacity of the next invocation were not measured.')
  const rows = await region
    .locator('tbody tr')
    .evaluateAll((elements) => elements.map((row) => Array.from(row.children).map((cell) => cell.textContent)))
  expect(rows).toEqual(renderedRows(models))
  await expect(region).not.toContainText('Not recorded')
}

test('the collaboration journey: failed verification, diagnosis, correction, fresh verification, and ordinary approval', async ({ page }) => {
  test.setTimeout(240_000)
  mark = markInvocations()
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
  // A second, distinct enabled recipe through the same rendered form: delivery needs the complete verification set.
  await page.getByLabel('Verification command name').fill('Candidate lint check')
  await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
  await page.getByLabel('Verification arguments').fill('check')
  await page.getByRole('button', { name: 'Add command' }).click()
  await expect(verification).toContainText('#2 Candidate lint check')
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
  const rootProposal = data.messages().find((message) => message.Type === 'Proposal' && data.attempts().find((a) => sameId(a.Id, message.AttemptId))?.AgentResponseContract === 'Proposal')
  const revisedProposal = data.messages().find(
    (message) => message.Type === 'Proposal' && data.attempts().find((a) => sameId(a.Id, message.AttemptId))?.AgentResponseContract === 'ChallengeResolution',
  )
  expect(rootProposal && revisedProposal).toBeTruthy()
  expect(sameId(rootProposal!.Id, revisedProposal!.Id)).toBe(false)
  expect(rootProposal!.Summary).not.toEqual(revisedProposal!.Summary)

  // 6. Claude implements the revised Proposal: the double edits the one plan-scoped file of the real candidate worktree.
  await page.getByRole('button', { name: 'Implement the resolved plan with Claude', exact: true }).click()
  await expect(page.getByText('Last attempt #4: Implemented.')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('Checkpoint ').filter({ hasText: '→' }).first()).toBeVisible()
  expect(agentContracts()).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport'])
  expect(data.executions()).toEqual([]) // nothing verified, diagnosed, or reviewed on its own
  const implementation = data.attempts().find((a) => a.AgentResponseContract === 'ImplementationReport')!
  expect(sameId(data.inputsOf(implementation.Id)[0].CollaborationMessageId, revisedProposal!.Id)).toBe(true)

  // 7. The explicit Refresh evidence control reads the checkpoint the implementation recorded into Source evidence, Verification
  // commands and Checkpoint review; it records no checkpoint and starts no stage. Then explicit local verification: the host accepts
  // the claim with HTTP 202 over the authenticated generated client, and the failed result appears without a reload.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #2 · 1 changed files', { timeout: 30_000 })
  expect(data.checkpoints()).toHaveLength(2)
  expect(agentContracts()).toHaveLength(4)
  await runRecipe(page, verification, 1)
  await expect(recipeRow(verification, 1)).toContainText('Last run: Failed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #1 was requested.')
  await runRecipe(page, verification, 2)
  await expect(recipeRow(verification, 2)).toContainText('Last run: Failed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #2 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(data.executions().map((e) => e.Status)).toEqual(['Failed', 'Failed'])
  await recipeRow(verification, 1).getByRole('button', { name: /Inspect stderr/ }).click()
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
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeDisabled()
  await expect(manual).not.toContainText('Reading verification evidence…')
  expect(agentContracts()).toHaveLength(4)
  expect(data.checkpoints()).toHaveLength(2)
  expect(data.executions()).toHaveLength(2)

  // 8. Ordinary review is unavailable: the rendered control submits, and the server's non-Passed gate refuses it without creating
  // an attempt or an invocation.
  const refusal = page.waitForResponse((response) => response.url().endsWith(CODE_REVIEW_PATH) && response.request().method() === 'POST')
  await review.getByRole('button', { name: 'Request code review' }).click()
  expect((await refusal).status()).toBe(409)
  await expect(review).toContainText("has not Passed for the current checkpoint")
  expect(agentContracts()).toHaveLength(4)
  expect(readInvocations(mark).filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 9. Codex diagnosis of the failed verification (made available by the refresh above); its findings are inspected on the page.
  await diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' }).click()
  await expect(diagnosis).toContainText('Last diagnosis #5: Diagnosis findings recorded.', { timeout: 60_000 })
  await expect(diagnosis).toContainText('#1 Candidate total check · execution 1 · Failed · exit code 1')
  await expect(diagnosis).toContainText('#2 Candidate lint check · execution 2 · Failed · exit code 1')
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
  expect(data.executions()).toHaveLength(2) // the correction did not verify itself

  // 11. Refresh the evidence, then fresh verification of the corrected checkpoint passes, observed without a reload.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(source).toContainText('Checkpoint #3', { timeout: 30_000 })
  expect(data.checkpoints()).toHaveLength(3)
  await runRecipe(page, verification, 1)
  await expect(recipeRow(verification, 1)).toContainText('Last run: Passed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #3 was requested.')
  await runRecipe(page, verification, 2)
  await expect(recipeRow(verification, 2)).toContainText('Last run: Passed', { timeout: 60_000 })
  await expect(verification).toContainText('Verification #4 was requested.')
  await expect(verification).not.toContainText('This verification could not be started.')
  expect(data.executions().map((e) => e.Status)).toEqual(['Failed', 'Failed', 'Passed', 'Passed'])
  expect(agentContracts()).toHaveLength(6)

  // Refresh evidence once more after the terminal verification: the run reads the host's current diagnosis and review status again,
  // shows no refusal reported for an earlier verification, offers the ordinary review, and no longer offers a diagnosis. Nothing is
  // requested, claimed or captured.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(review).not.toContainText('has not Passed for the current checkpoint', { timeout: 30_000 })
  await expect(review.getByRole('button', { name: 'Request code review' })).toBeEnabled()
  await expect(diagnosis.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  // The Passed verification of the corrected checkpoint is current in both evidence views after this refresh, before any decision.
  await expect(recipeRow(verification, 1)).toContainText('Last run: Passed')
  await expect(recipeRow(verification, 2)).toContainText('Last run: Passed')
  await expect(verification).not.toContainText('Reading verification status…')
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeEnabled({ timeout: 30_000 })
  await expect(manual).not.toContainText('Reading verification evidence…')
  expect(data.checkpointReviews()).toEqual([]) // no manual review exists yet, and the refresh recorded none
  expect(agentContracts()).toHaveLength(6)
  expect(data.checkpoints()).toHaveLength(3)
  expect(data.executions()).toHaveLength(4)
  expect(readInvocations(mark).filter((entry) => entry.contract === 'ImplementationReview')).toEqual([])

  // 12. Ordinary Codex review of the corrected report: approval.
  await review.getByRole('button', { name: 'Request code review' }).click()
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible({ timeout: 60_000 })

  // 12b. The explicit manual checkpoint review: after the ordinary Agent approval and an explicit Refresh evidence (reads only, no
  // reload), the rendered manual panel offers Approve for the exact current checkpoint and its Passed execution, and the host records
  // one Human Approved fact over the authenticated generated client's HTTP 201. It is a separate, human-authored fact: it creates no
  // attempt, message, invocation, claim, grant or lifecycle change. The ordinary approval above already recorded its own FutureAgent
  // review fact for the same checkpoint; the manual one is a second, distinct fact and never replaces or rewrites it.
  const agentReviews = data.checkpointReviews()
  expect(agentReviews.map((row) => [row.ActorKind, row.Decision, row.CheckpointNumber])).toEqual([['FutureAgent', 'Approved', 3]])
  const beforeManual = { attempts: data.attempts().length, messages: data.messages().length, invocations: stageInvocations().length }
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  await expect(manual.getByRole('button', { name: 'Approve', exact: true })).toBeEnabled({ timeout: 30_000 })
  await expect(manual).not.toContainText('Reading verification evidence…')
  await expect(manual).not.toContainText('could not be refreshed')
  await expect(manual.getByLabel('Reviewer')).toHaveValue('Human')
  expect(data.checkpointReviews()).toEqual(agentReviews) // the refresh recorded nothing
  expect(data.attempts()).toHaveLength(beforeManual.attempts)
  // ADR-0030: the complete Human approval. The rendered action shows the exact bundle it will submit (both enabled recipes, each with
  // its latest Passed execution of this checkpoint, read from the protected approval-evidence operation), and one click records one
  // Human review whose members are exactly that bundle. The legacy single-run Approve is left alone.
  const approveAll = manual.getByRole('button', { name: 'Approve all enabled checks' })
  await expect(approveAll).toBeEnabled({ timeout: 30_000 })
  await expect(manual.getByRole('list', { name: 'Verification set to approve' }).getByRole('listitem')).toHaveText([
    '#1 Candidate total check · execution #3',
    '#2 Candidate lint check · execution #4',
  ])
  await expect(manual).toContainText('The explicit local commit requires this complete approval; approving commits nothing.')
  const manualRecorded = page.waitForResponse(
    (response) => response.request().method() === 'POST' && CHECKPOINT_REVIEW_PATH.test(new URL(response.url()).pathname),
  )
  await approveAll.click()
  expect((await manualRecorded).status()).toBe(201)
  await expect(manual).toContainText('Recorded. The Human approval covers these runs.')
  await expect(manual).toContainText('Approved')
  await expect(manual).toContainText(HUMAN_BOTH_RUNS)
  await expect(manual).toContainText('Current checkpoint')
  await expect(manual).not.toContainText('This review decision could not be recorded.')
  await expect(manual).not.toContainText('Review evidence could not be loaded.')
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible()
  expect(data.attempts()).toHaveLength(beforeManual.attempts)
  expect(data.messages()).toHaveLength(beforeManual.messages)
  expect(stageInvocations()).toHaveLength(beforeManual.invocations)

  // 12c. Claude-reported model limits (ADR-0023): each of the three Claude stages' real adapters read the models its native double listed
  // (with the limits it reported for each) from the provider envelope, and the host recorded them in the completion transaction. The
  // rendered history detail shows them, and the generated client received exactly them from the host; a Codex attempt has none. This is
  // historical observation the double reported, not remaining context, a live capability, or proof of what a real provider would say.
  const limitsBeforeReload: Record<string, unknown> = {}
  for (const [attemptNumber, contract] of [[2, 'CriticalReview'], [4, 'ImplementationReport'], [6, 'ReviewCorrection']] as const) {
    const inspected = await inspectAttempt(page, attemptNumber)
    expect(inspected.attempt.AgentResponseContract).toBe(contract)
    expect(inspected.evidence.provider).toBe('ClaudeCode')
    expect(inspected.evidence.modelContextLimits).toEqual(responseMember(EXPECTED_MODEL_LIMITS[contract]))
    await expectRenderedModelLimits(inspected.detail, EXPECTED_MODEL_LIMITS[contract])
    // Selecting the next attempt replaces the section: only the selected attempt's limits are ever on the page.
    await expect(page.getByRole('region', { name: 'Claude-reported model limits' })).toHaveCount(1)
    limitsBeforeReload[contract] = inspected.evidence.modelContextLimits
  }

  const codexPlanning = await inspectAttempt(page, 1)
  expect(codexPlanning.evidence.provider).toBe('Codex')
  expect(codexPlanning.evidence.modelContextLimits ?? null).toBeNull()
  await expect(codexPlanning.detail.getByRole('region', { name: 'Claude-reported model limits' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Claude-reported model limits' })).toHaveCount(0)

  // The host's own record of the same fact (read-only): the canonical project snapshot for exactly the three Claude attempts, nothing for the others.
  expect(data.modelContextLimitsByAttempt().map((row) => [row.AttemptNumber, row.AgentModelContextLimitsSnapshot])).toEqual([
    [1, null],
    [2, canonicalSnapshot(EXPECTED_MODEL_LIMITS.CriticalReview)],
    [3, null],
    [4, canonicalSnapshot(EXPECTED_MODEL_LIMITS.ImplementationReport)],
    [5, null],
    [6, canonicalSnapshot(EXPECTED_MODEL_LIMITS.ReviewCorrection)],
    [7, null],
  ])

  // 13. Persisted visible results stay attached to this run after the journey's only reload; approval is not lifecycle completion.
  await reloadAndReselect(page)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Last attempt #7: Implementation approved.')).toBeVisible()
  await expect(manual).toContainText(HUMAN_BOTH_RUNS, { timeout: 30_000 })
  await expect(manual).toContainText('Current checkpoint')
  await expect(diagnosis).toContainText('Last correction #6: Correction applied.')
  await expect(page.getByText('Completed · Completed')).toHaveCount(0)
  expect((await workflowLines(page)).join('\n')).not.toContain('Run completed')

  // The recorded limits survive the reload: the history is closed again, and the same attempts, inspected again through the rendered controls,
  // show the same limits, and the host answers with the same evidence it did before.
  for (const [attemptNumber, contract] of [[4, 'ImplementationReport'], [6, 'ReviewCorrection']] as const) {
    const inspected = await inspectAttempt(page, attemptNumber)
    expect(inspected.evidence.modelContextLimits).toEqual(limitsBeforeReload[contract])
    expect(inspected.evidence.modelContextLimits).toEqual(responseMember(EXPECTED_MODEL_LIMITS[contract]))
    await expectRenderedModelLimits(inspected.detail, EXPECTED_MODEL_LIMITS[contract])
  }

  // ---- Evidence read back from the host's own records and the doubles' allowlisted log ---------------------------------------
  const all = data.attempts()
  expect(all.map((a) => a.AgentOutcome)).toEqual([
    'Proposed', 'Challenged', 'Resolved', 'Implemented', 'DiagnosisFindingsRecorded', 'CorrectionApplied', 'ReviewApproved',
  ])
  const limits = data.runLimits()
  expect(all).toHaveLength(7) // seven Agent claims, within the unchanged default of 16
  expect(limits.MaximumAgentAttempts).toBe(16)
  expect(limits.MaximumReviewCorrectionAttempts).toBe(2)
  expect(all.filter((a) => a.AgentResponseContract === 'ReviewCorrection')).toHaveLength(1) // one spent shared correction slot
  expect(limits.Lifecycle).not.toBe('Completed')
  await expect(page.getByText('Reserved Agent invocation time: 90 min of 120 min')).toBeVisible()

  const diagnosisAttempt = all.find((a) => a.AgentResponseContract === 'VerificationDiagnosis')!
  const correctionAttempt = all.find((a) => a.AgentResponseContract === 'ReviewCorrection')!
  const reviewAttempt = all.find((a) => a.AgentResponseContract === 'ImplementationReview')!
  const executionReport = data.messages().find((m) => m.Type === 'ExecutionReport' && sameId(m.AttemptId, implementation.Id))!
  const findings = data.messages().filter((m) => m.Type === 'ReviewFinding' && sameId(m.AttemptId, diagnosisAttempt.Id))
  const correctedReport = data.messages().find((m) => m.Type === 'ExecutionReport' && sameId(m.AttemptId, correctionAttempt.Id))!
  expect(findings).toHaveLength(1)
  expect(data.inputsOf(diagnosisAttempt.Id).map((i) => i.CollaborationMessageId.toLowerCase())).toEqual([executionReport.Id.toLowerCase()])
  expect(data.inputsOf(correctionAttempt.Id).map((i) => i.CollaborationMessageId.toLowerCase())).toEqual([
    executionReport.Id.toLowerCase(),
    ...findings.map((f) => f.Id.toLowerCase()),
  ])
  const revisionResponses = data.messages().filter((m) => m.Type === 'RevisionResponse' && sameId(m.AttemptId, correctionAttempt.Id))
  expect(revisionResponses.map((m) => m.InReplyToMessageId?.toLowerCase())).toEqual(findings.map((f) => f.Id.toLowerCase()))
  expect(sameId(data.inputsOf(reviewAttempt.Id)[0].CollaborationMessageId, correctedReport.Id)).toBe(true)

  // Diagnosis and review judged the revised Proposal, never the Planner root (see also planIdentity.test.ts).
  const invocations = readInvocations(mark)
  expect(planIdentityProblems(invocations, { rootId: rootProposal!.Id, revisedId: revisedProposal!.Id })).toEqual([])
  expect(invocations.find((entry) => entry.contract === 'ImplementationReview')?.reportMessageId?.toLowerCase()).toBe(correctedReport.Id.toLowerCase())
  expect(invocations.find((entry) => entry.contract === 'VerificationDiagnosis')?.reportMessageId?.toLowerCase()).toBe(executionReport.Id.toLowerCase())

  // A real fingerprint/checkpoint change at each mutation, with verification bound to the checkpoint it judged.
  const cps = data.checkpoints()
  expect(cps.map((c) => c.CheckpointNumber)).toEqual([1, 2, 3])
  expect(new Set(cps.map((c) => c.FingerprintSha256)).size).toBe(3)
  expect(cps.every((c) => c.HeadCommitSha === repository.baselineCommit)).toBe(true)
  const runs = data.executions()
  expect(runs.map((e) => [e.Status, e.ExitCode])).toEqual([['Failed', 1], ['Failed', 1], ['Passed', 0], ['Passed', 0]])
  const recipes = data.recipes()
  expect(recipes.map((recipe) => [recipe.CommandNumber, recipe.Name, recipe.IsEnabled])).toEqual([
    [1, 'Candidate total check', 1],
    [2, 'Candidate lint check', 1],
  ])
  expect(runs.map((e) => lower(e.VerificationCommandId))).toEqual([recipes[0].Id, recipes[1].Id, recipes[0].Id, recipes[1].Id].map(lower))
  expect([0, 1].every((index) => sameId(runs[index].GitCheckpointId, cps[1].Id))).toBe(true)
  expect([2, 3].every((index) => sameId(runs[index].GitCheckpointId, cps[2].Id))).toBe(true)
  expect([0, 1].every((index) => runs[index].CompletionFingerprintSha256 === cps[1].FingerprintSha256)).toBe(true)
  expect([2, 3].every((index) => runs[index].CompletionFingerprintSha256 === cps[2].FingerprintSha256)).toBe(true)

  // The manual review is one persisted Human Approved fact on the exact current checkpoint, binding exactly the Passed execution of that
  // checkpoint (not the earlier Failed one), and it is separate from the ordinary Agent approval: that approval is the seventh attempt's
  // outcome and message, this is a checkpoint review row that no attempt, message, claim or lifecycle owns.
  const reviewFacts = data.checkpointReviews()
  expect(reviewFacts.map((row) => [row.ActorKind, row.Decision])).toEqual([['FutureAgent', 'Approved'], ['Human', 'Approved']])
  expect(sameId(reviewFacts[0].Id, agentReviews[0].Id)).toBe(true) // the ordinary approval's fact is unchanged
  const manualReview = reviewFacts[1]
  expect(sameId(manualReview.Id, agentReviews[0].Id)).toBe(false)
  expect(manualReview).toMatchObject({ CheckpointNumber: 3, CheckpointFingerprintSha256: cps[2].FingerprintSha256 })
  expect(sameId(manualReview.GitCheckpointId, cps[2].Id)).toBe(true)
  // One Human review whose relational members are exactly both recipes' latest Passed executions of that checkpoint (never the
  // earlier Failed ones), the same membership the Agent's own review claimed.
  const manualEvidence = data.checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, manualReview.Id))
  expect(manualEvidence).toHaveLength(2)
  const expectedMembers = [
    [lower(recipes[0].Id), lower(runs[2].Id), 3],
    [lower(recipes[1].Id), lower(runs[3].Id), 4],
  ]
  expect(manualEvidence.map((row) => [lower(row.VerificationCommandId), lower(row.VerificationExecutionId), row.VerificationExecutionNumber])).toEqual(
    expectedMembers,
  )
  for (const member of manualEvidence) {
    expect(member).toMatchObject({
      VerificationExecutionCheckpointFingerprintSha256: cps[2].FingerprintSha256,
      VerificationExecutionStatus: 'Passed',
      VerificationExecutionExitCode: 0,
    })
  }

  const agentEvidence = data.checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, reviewFacts[0].Id))
  expect(agentEvidence.map((row) => [lower(row.VerificationCommandId), lower(row.VerificationExecutionId), row.VerificationExecutionNumber])).toEqual(
    expectedMembers,
  )
  expect(all.filter((a) => a.AgentOutcome === 'ReviewApproved')).toHaveLength(1) // seven attempts: the manual review claimed none
  expect(all.filter((a) => a.AgentResponseContract === 'ImplementationReview')).toHaveLength(1) // the one Agent review, still Codex's

  // Direct guidance: exactly the one explicitly guided correction recorded the normalized text; every other attempt recorded none. The native
  // double received that exact sealed value inside the host's fixed boundary (it logs only the text's hash), and the text itself never
  // reached its log. This proves local agreement of the sealed context, not that a provider would follow the guidance.
  expect(data.directGuidanceByAttempt().map((row) => [row.AttemptNumber, row.AgentDirectHumanGuidance])).toEqual(
    all.map((a) => [a.AttemptNumber, a.AttemptNumber === correctionAttempt.AttemptNumber ? CORRECTION_GUIDANCE : null]),
  )
  const guidedEntries = invocations.filter((entry) => entry.contract === 'ReviewCorrection')
  expect(guidedEntries).toHaveLength(1)
  expect(guidedEntries[0].guidanceSha256).toBe(sha256Hex(CORRECTION_GUIDANCE))
  expect(guidedEntries[0].guidanceBoundary).toBe('fixed')
  expect(invocations.filter((entry) => entry.contract !== 'ReviewCorrection' && entry.guidanceSha256 !== undefined)).toEqual([])
  expect(readInvocationLogText(mark)).not.toContain('Keep the change inside')
  expect(readInvocationLogText(mark)).not.toContain('Leave every other file alone')

  // The doubles' own log: exactly the contracts requested, in order, each served once; the verification executable saw real content.
  expect(invocations.filter((e) => e.kind !== 'probe').map((e) => e.contract ?? `verify:${e.outcome}`)).toEqual([
    'Proposal', 'CriticalReview', 'ChallengeResolution', 'ImplementationReport', 'verify:failed', 'verify:failed', 'VerificationDiagnosis',
    'ReviewCorrection', 'verify:passed', 'verify:passed', 'ImplementationReview',
  ])

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
      { files: instructionFiles('journey-source'), workspaceId: data.workspace().Id, foreignFiles: instructionFiles('escalated-source') },
      all.map((attempt) => attempt.AgentResponseContract),
      readInvocationLogText(mark),
    ),
  ).toEqual([])

  // Attested tracked-change text (ADR-0024): the one tracked file the doubles edit is a regular single-name file of the owned worktree,
  // so every stage claimed after an edit received, in its sealed manifest, the host comparison of the committed HEAD text and the proven
  // current bytes, and no stage received any before the first edit. The sealed patches are applied to the text this journey committed with
  // an independent applier; the last stage's rebuilds the worktree file exactly as it is now. No Git patch header or function text appears.
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
  // The only new ref is the tool-owned worktree branch, still at the baseline commit; the source branches are untouched.
  const refs = sourceAfter.branches.split('\n').filter((line) => line.length > 0)
  expect(refs.filter((line) => !line.includes('refs/heads/devalcopilot/workspace/'))).toEqual(
    sourceBefore.branches.split('\n').filter((line) => line.length > 0),
  )
  expect(refs.filter((line) => line.includes('refs/heads/devalcopilot/workspace/'))).toHaveLength(1)
  expect(refs.every((line) => line.endsWith(` ${repository.baselineCommit}`))).toBe(true)

  // 14. The explicitly approved checkpoint is delivered only through the rendered local-commit control. The host creates the
  // unsigned, hook-free commit on its owned branch; the source repository stays untouched and no remote is added or pushed.
  const localCommit = page.getByRole('region', { name: 'Local commit' })
  await expect(localCommit.getByRole('button', { name: 'Commit locally' })).toBeVisible({ timeout: 30_000 })
  await localCommit.getByLabel('Commit message').fill('Deliver the verified local change')
  await expect(localCommit.getByRole('button', { name: 'Commit locally' })).toBeEnabled()
  const localCommitResponse = page.waitForResponse(
    (response) => response.request().method() === 'POST' && /\/api\/runs\/[^/]+\/local-commit$/.test(new URL(response.url()).pathname),
  )
  // The receipt is read only once the operation is recorded as Completed; that one wire read is observed from here.
  const receiptWire = page.waitForResponse(
    (response) => response.request().method() === 'GET' && RECEIPT_PATH.test(new URL(response.url()).pathname),
    { timeout: 90_000 },
  )
  await localCommit.getByRole('button', { name: 'Commit locally' }).click()
  expect((await localCommitResponse).status()).toBe(200)
  await expect(localCommit).toContainText('Completed. Local commit only — not pushed.', { timeout: 60_000 })
  await expect(page.getByText('Completed · Completed')).toBeVisible()
  // The operation recorded exactly the complete verification set the Agent and the Human approved, in the host's command order.
  expect(data.localCommitVerificationMembers().map((member) => [member.Sequence, lower(member.CommandId), lower(member.SubjectId)])).toEqual([
    [0, lower(recipes[0].Id), lower(runs[2].Id)],
    [1, lower(recipes[1].Id), lower(runs[3].Id)],
  ])

  const deliveredCommit = git('rev-parse', 'HEAD')
  expect(deliveredCommit).not.toBe(repository.baselineCommit)
  expect(git('rev-list', '--count', 'HEAD')).toBe('2')
  expect(git('log', '-1', '--format=%B')).toContain('DevalCopilot-Operation:')
  expect(git('remote')).toBe('')
  expect(readFileSync(join(worktree, 'src', 'Feature.cs'), 'utf8')).toContain('return left + right;')

  const sourceAfterCommit = repositorySnapshot(repository.path)
  expect(sourceAfterCommit.head).toBe(sourceBefore.head)
  expect(sourceAfterCommit.status).toBe(sourceBefore.status)
  expect(sourceAfterCommit.candidate).toBe(sourceBefore.candidate)
  const deliveredRefs = sourceAfterCommit.branches.split('\n').filter((line) => line.length > 0)
  expect(deliveredRefs.filter((line) => !line.includes('refs/heads/devalcopilot/workspace/'))).toEqual(
    sourceBefore.branches.split('\n').filter((line) => line.length > 0),
  )
  expect(deliveredRefs.filter((line) => line.includes('refs/heads/devalcopilot/workspace/'))).toEqual([
    expect.stringMatching(new RegExp(` ${deliveredCommit}$`)),
  ])

  // 14b. The recorded local-delivery receipt (ADR-0032) shows, beside the completed operation, exactly what the host recorded for this
  // delivery: the SHA, the checkpoint, the CodeReviewer approval, the selected Human approval and both verification members in recorded
  // order. Every identity is compared with the durable rows the delivery itself pinned (never with the latest or current records), and
  // the earlier failed executions of the same recipes are not part of it. It is read-only: it creates no attempt, message, verification,
  // review, commit operation or invocation, and it says nothing about remote publication.
  const operation = data.localCommitOperation()
  expect(operation).not.toBeNull()
  const deliveryReview = data.attempts().find((attempt) => attempt.AttemptNumber === 7)!
  const approvals = data.messages().filter((message) => sameId(message.AttemptId, deliveryReview.Id) && message.Type === 'ReviewApproval')
  expect(approvals).toHaveLength(1)
  const reviewedReport = data.inputsOf(deliveryReview.Id)
  expect(reviewedReport).toHaveLength(1)
  expect(operation).toMatchObject({ Status: 'Completed', CheckpointNumber: 3, CheckpointFingerprintSha256: cps[2].FingerprintSha256 })
  expect(lower(operation!.CommitSha)).toBe(deliveredCommit)
  expect(sameId(operation!.GitCheckpointId, cps[2].Id)).toBe(true)
  expect(sameId(operation!.CodeReviewAttemptId, deliveryReview.Id)).toBe(true)
  expect(sameId(operation!.CodeReviewApprovalMessageId, approvals[0].Id)).toBe(true)
  expect(sameId(operation!.ExecutionReportMessageId, reviewedReport[0].CollaborationMessageId)).toBe(true)
  expect(sameId(operation!.HumanCheckpointReviewId, manualReview.Id)).toBe(true)
  const receiptResponse = await receiptWire
  expect(receiptResponse.status()).toBe(200)
  const wire = await receiptResponse.json()
  expect(wire.state).toBe('Available')
  expect(Object.keys(wire.receipt).sort()).toEqual([
    'branchName', 'checkpoint', 'codeReview', 'commitSha', 'completedAtUtc', 'executionReportMessageId', 'humanReview', 'objective',
    'operationId', 'parentCommitSha', 'runId', 'treeSha', 'verification', 'version',
  ])
  expect(wire.receipt).toMatchObject({
    version: 1,
    objective: OBJECTIVE,
    commitSha: deliveredCommit,
    parentCommitSha: repository.baselineCommit,
    treeSha: git('rev-parse', 'HEAD^{tree}'),
    branchName: git('rev-parse', '--abbrev-ref', 'HEAD'),
    checkpoint: { number: 3, fingerprintSha256: cps[2].FingerprintSha256, changedPathCount: 1 },
    humanReview: { decision: 'Approved' },
    codeReview: { attemptNumber: 7 },
  })
  expect(lower(wire.receipt.operationId)).toBe(lower(operation!.Id))
  expect(lower(wire.receipt.checkpoint.id)).toBe(lower(cps[2].Id))
  expect(lower(wire.receipt.executionReportMessageId)).toBe(lower(reviewedReport[0].CollaborationMessageId))
  expect(lower(wire.receipt.codeReview.attemptId)).toBe(lower(deliveryReview.Id))
  expect(lower(wire.receipt.codeReview.approvalMessageId)).toBe(lower(approvals[0].Id))
  expect(lower(wire.receipt.humanReview.reviewId)).toBe(lower(manualReview.Id))
  expect(
    wire.receipt.verification.map((member: Record<string, unknown>) => [
      member.order, lower(String(member.commandId)), lower(String(member.executionId)), member.executionNumber, member.commandName, member.status, member.exitCode,
    ]),
  ).toEqual([
    [0, lower(recipes[0].Id), lower(runs[2].Id), runs[2].ExecutionNumber, 'Candidate total check', 'Passed', 0],
    [1, lower(recipes[1].Id), lower(runs[3].Id), runs[3].ExecutionNumber, 'Candidate lint check', 'Passed', 0],
  ])
  const wireText = JSON.stringify(wire)
  for (const hidden of [worktree, repository.path, 'dotnet', 'AGENTS.md']) {
    expect(wireText).not.toContain(hidden)
  }

  const deliveryReceipt = page.getByRole('region', { name: 'Local delivery receipt' })
  const assertReceiptRegion = async (receiptRegion: Locator) => {
    await expect(receiptRegion).toContainText(deliveredCommit, { timeout: 30_000 })
    await expect(receiptRegion).toContainText('Nothing was pushed')
    await expect(receiptRegion).toContainText(OBJECTIVE)
    await expect(receiptRegion).toContainText('#3')
    await expect(receiptRegion).toContainText(cps[2].FingerprintSha256)
    await expect(receiptRegion).toContainText(lower(reviewedReport[0].CollaborationMessageId))
    await expect(receiptRegion).toContainText(`attempt #7 (${lower(deliveryReview.Id)})`)
    await expect(receiptRegion).toContainText(lower(approvals[0].Id))
    await expect(receiptRegion).toContainText(`${lower(manualReview.Id)} — Approved`)
    const members = receiptRegion.getByRole('listitem')
    await expect(members).toHaveCount(2)
    await expect(members.nth(0)).toContainText('Candidate total check — Passed, exit code 0')
    await expect(members.nth(0)).toContainText(`execution #${runs[2].ExecutionNumber} (${lower(runs[2].Id)})`)
    await expect(members.nth(1)).toContainText('Candidate lint check — Passed, exit code 0')
    await expect(members.nth(1)).toContainText(`execution #${runs[3].ExecutionNumber} (${lower(runs[3].Id)})`)
    await expect(receiptRegion).not.toContainText(lower(runs[0].Id))
    await expect(receiptRegion).not.toContainText(lower(runs[1].Id))
    await expect(receiptRegion).not.toContainText('could not be read')
    await expect(receiptRegion.getByRole('button')).toHaveCount(0)
  }
  const assertRenderedReceipt = () => assertReceiptRegion(deliveryReceipt)
  await assertRenderedReceipt()
  const afterDelivery = {
    attempts: data.attempts().length,
    messages: data.messages().length,
    executions: data.executions().length,
    operations: data.localCommitOperationCount(),
    reviews: data.checkpointReviews().length,
    invocations: stageInvocations().length,
  }
  expect(afterDelivery).toMatchObject({ attempts: 7, executions: 4, operations: 1, reviews: 2 })

  // The operation and terminal run remain visible after a real reload; the browser never resubmits it.
  const receiptAfterReload = page.waitForResponse(
    (response) => response.request().method() === 'GET' && RECEIPT_PATH.test(new URL(response.url()).pathname),
  )
  await reloadAndReselect(page)
  await expect(localCommit).toContainText('Completed. Local commit only — not pushed.', { timeout: 30_000 })
  await expect(page.getByText('Completed · Completed')).toBeVisible()
  expect(git('rev-parse', 'HEAD')).toBe(deliveredCommit)
  // The same exact receipt is read again from the persisted rows after the reload, byte for byte, and reading it changed nothing.
  expect(await (await receiptAfterReload).json()).toEqual(wire)
  await assertRenderedReceipt()
  expect({
    attempts: data.attempts().length,
    messages: data.messages().length,
    executions: data.executions().length,
    operations: data.localCommitOperationCount(),
    reviews: data.checkpointReviews().length,
    invocations: stageInvocations().length,
  }).toEqual(afterDelivery)
  expect(git('rev-list', '--count', 'HEAD')).toBe('2')
  // The persisted memberships are the same after the reload: nothing was re-recorded and nothing was resubmitted.
  expect(data.localCommitVerificationMembers()).toHaveLength(2)
  expect(data.checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, manualReview.Id))).toHaveLength(2)
  expect(data.checkpointReviews()).toHaveLength(2)

  // 15. Run history (ADR-0033). The delivered run is terminal, so the normal intake records a SUBSEQUENT manual objective and the live
  // cockpit follows that newer run. The older delivery is then reached only through the collapsed, read-only Run history region: its
  // recorded metadata, its located source and the existing receipt, read again from the persisted rows. The intake adds exactly one
  // run and its own intake event; every read after it adds no Agent attempt, message, verification, review, checkpoint or local-commit
  // operation, invokes no provider double and changes neither Git nor the source repository.
  const firstRunId = data.runs()[0].Id
  expect(data.runs()).toHaveLength(1)
  const beforeFollowUp = data.projectTotals()
  const firstRunEvents = data.eventTypesOf(firstRunId)
  await page.getByRole('textbox', { name: 'Objective' }).fill(FOLLOW_UP_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: FOLLOW_UP_OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  const followUpRun = data.runs()[1]
  expect(data.runs()).toHaveLength(2)
  expect(followUpRun).toMatchObject({ ExecutionNumber: 2, Objective: FOLLOW_UP_OBJECTIVE, Lifecycle: 'Created', Stage: 'Intake' })
  expect(String(followUpRun.ExecutionMode)).toBe('2')
  const intakeEvents = data.eventTypesOf(followUpRun.Id)
  expect(intakeEvents.length).toBeGreaterThan(0)
  // The intentional change is one run and its own intake event(s); nothing of any other workflow kind and nothing of the first run.
  const afterFollowUp = data.projectTotals()
  expect(afterFollowUp).toEqual({ ...beforeFollowUp, runs: beforeFollowUp.runs + 1, events: beforeFollowUp.events + intakeEvents.length })
  expect(data.eventTypesOf(firstRunId)).toEqual(firstRunEvents)
  expect(stageInvocations()).toHaveLength(afterDelivery.invocations)
  // The live cockpit follows the newer run: the delivered run's receipt is no longer shown there.
  await expect(page.getByRole('region', { name: 'Local delivery receipt' })).toHaveCount(0)

  const historyRegion = page.getByRole('region', { name: 'Run history' })
  const historyRows = historyRegion.locator('.dc-run-history-items > li')
  const historyRequests: string[] = []
  const receiptRequests: string[] = []
  const eventRequests: string[] = []
  const messageRequests: string[] = []
  const replyRequests: { method: string; path: string; search: string; body: string | null }[] = []
  const attemptRequests: { method: string; path: string; search: string; body: string | null }[] = []
  page.on('request', (request) => {
    const { pathname: path, search } = new URL(request.url())
    if (request.method() === 'GET' && HISTORY_PATH.test(path)) {
      historyRequests.push(path)
    }

    if (ATTEMPT_PATH.test(path)) {
      attemptRequests.push({ method: request.method(), path: path.toLowerCase(), search, body: request.postData() })
    }

    if (REPLY_PATH.test(path)) {
      replyRequests.push({ method: request.method(), path: path.toLowerCase(), search, body: request.postData() })
    }

    if (request.method() === 'GET' && COLLABORATION_HISTORY_PATH.test(path)) {
      messageRequests.push(path.toLowerCase() + search)
    }

    if (request.method() === 'GET' && EVENT_HISTORY_PATH.test(path)) {
      eventRequests.push(path.toLowerCase() + search)
    }

    if (request.method() === 'GET' && RECEIPT_PATH.test(path)) {
      receiptRequests.push(path.toLowerCase())
    }
  })
  const firstRunReceiptPath = `/api/runs/${lower(firstRunId)}/local-delivery-receipt`
  await expect(historyRegion.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
  expect(historyRequests).toEqual([])
  expect(eventRequests).toEqual([])
  expect(messageRequests).toEqual([])

  // Recorded events (ADR-0034). Every page is read through a rendered control, and what the host returned is compared exactly with
  // the durable rows (sequence text, identity, type, actor columns and instant) and with what the page renders. Only headers exist:
  // no payload and no attempt link is returned.
  const PAGE = 10
  const instant = (text: string) => Date.parse(text.replace(' ', 'T').replace(/(\.\d{3})\d*/, '$1'))
  const eventsRegion = () => historyRegion.getByRole('region', { name: 'Selected run' }).getByRole('region', { name: 'Recorded events' })
  const eventRows = () => eventsRegion().locator('.dc-run-event-history-items > li')
  const expectCollapsedEvents = async () => {
    await expect(eventsRegion().getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    await expect(eventRows()).toHaveCount(0)
  }

  const readEventPage = async (runId: string, expectedBefore: string | null, action: () => Promise<void>) => {
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && EVENT_HISTORY_PATH.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    )
    const before = eventRequests.length
    await action()
    const response = await answered
    expect(response.status()).toBe(200)
    const url = new URL(response.url())
    expect(url.pathname.toLowerCase()).toBe(`/api/runs/${lower(runId)}/event-history`)
    expect([...url.searchParams.keys()].sort()).toEqual(expectedBefore === null ? ['limit'] : ['beforeSequence', 'limit'])
    expect(url.searchParams.get('limit')).toBe(String(PAGE))
    expect(url.searchParams.get('beforeSequence')).toBe(expectedBefore)
    expect(eventRequests).toHaveLength(before + 1)
    const text = await response.text()
    for (const hidden of ['payload', 'attemptId', worktree, repository.path]) {
      expect(text).not.toContain(hidden)
    }

    return JSON.parse(text)
  }

  const expectHeaders = (runId: string, entries: Record<string, any>[], durable: EventHeaderRow[]) => {
    expect(entries).toHaveLength(durable.length)
    entries.forEach((entry, index) => {
      const row = durable[index]
      expect(Object.keys(entry).sort()).toEqual(['actor', 'eventType', 'id', 'occurredAtUtc', 'runId', 'sequence'])
      expect(Object.keys(entry.actor).sort()).toEqual(['kind', 'provider', 'role'])
      expect(entry.sequence).toBe(row.Sequence)
      expect(sameId(entry.id, row.Id)).toBe(true)
      expect(sameId(entry.runId, runId)).toBe(true)
      expect(entry.eventType).toBe(row.EventType)
      expect(entry.actor).toEqual({ kind: row.ActorKind, role: row.ActorAgentRole, provider: row.ActorAgentProvider })
      expect(instant(entry.occurredAtUtc)).toBe(instant(row.OccurredAtUtc))
    })
  }

  const expectRendered = async (entries: Record<string, any>[], rendered: number) => {
    await expect(eventRows()).toHaveCount(rendered)
    for (const [index, entry] of entries.entries()) {
      await expect(eventRows().nth(index)).toHaveText(new RegExp(`^Sequence ${entry.sequence} .* Event ${entry.id}$`))
    }
  }

  // Opens the recorded events of the selected run, pages explicitly until the last page, and proves that the pages, in order and
  // without repetition, are exactly the durable rows of that run. Returns the first (newest) page exactly as the host answered it.
  const browseRecordedEvents = async (runId: string, durable: EventHeaderRow[], expectPages: number) => {
    await expectCollapsedEvents()
    const first = await readEventPage(runId, null, () => eventsRegion().getByRole('button', { name: 'Show recorded events' }).click())
    expect(Object.keys(first).sort()).toEqual(['entries', 'hasMore', 'nextBeforeSequence', 'runId'])
    expect(sameId(first.runId, runId)).toBe(true)
    let collected: Record<string, any>[] = first.entries
    let body = first
    let pages = 1
    expectHeaders(runId, first.entries, durable.slice(0, PAGE))
    await expectRendered(collected, collected.length)
    await expect(eventsRegion()).toContainText('details of an event are omitted')
    while (body.hasMore) {
      expect(body.entries).toHaveLength(PAGE)
      expect(body.nextBeforeSequence).toBe(body.entries[PAGE - 1].sequence)
      const cursor: string = body.nextBeforeSequence
      body = await readEventPage(runId, cursor, () => eventsRegion().getByRole('button', { name: 'Load older events' }).click())
      expectHeaders(runId, body.entries, durable.slice(collected.length, collected.length + PAGE))
      collected = [...collected, ...body.entries]
      pages += 1
      await expectRendered(collected, collected.length)
    }

    expect(body.nextBeforeSequence).toBeNull()
    expect(pages).toBe(expectPages)
    expectHeaders(runId, collected, durable)
    expect(new Set(collected.map((entry) => lower(entry.id))).size).toBe(durable.length)
    await expect(eventsRegion().getByRole('button', { name: 'Load older events' })).toHaveCount(0)
    await expect(eventsRegion().getByRole('alert')).toHaveCount(0)
    return first
  }

  // The collaboration region asks for 5 messages per page (the host default is 10) so the delivered run's 9 durable messages need a
  // second page and explicit older paging runs through rendered controls without seeding rows.
  const MESSAGE_PAGE = 5
  // Recorded collaboration (ADR-0035), through rendered controls only. What the host returned is compared exactly with the durable
  // messages (sequence text, identity, closed metadata, summary and every structured field) and with what the page renders. Content
  // is never an attempt link, a reply target or raw JSON, and every durable message of this journey passes the existing content policy.
  const messagesRegion = () => historyRegion.getByRole('region', { name: 'Selected run' }).getByRole('region', { name: 'Recorded collaboration' })
  const messageRows = () => messagesRegion().locator('.dc-collaboration-history-items > li')
  const expectCollapsedMessages = async () => {
    await expect(messagesRegion().getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    await expect(messageRows()).toHaveCount(0)
  }

  const readMessagePage = async (runId: string, expectedBefore: string | null, action: () => Promise<void>) => {
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && COLLABORATION_HISTORY_PATH.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    )
    const before = messageRequests.length
    await action()
    const response = await answered
    expect(response.status()).toBe(200)
    const url = new URL(response.url())
    expect(url.pathname.toLowerCase()).toBe(`/api/runs/${lower(runId)}/collaboration-history`)
    expect([...url.searchParams.keys()].sort()).toEqual(expectedBefore === null ? ['limit'] : ['beforeSequence', 'limit'])
    expect(url.searchParams.get('limit')).toBe(String(MESSAGE_PAGE))
    expect(url.searchParams.get('beforeSequence')).toBe(expectedBefore)
    expect(messageRequests).toHaveLength(before + 1)
    const text = await response.text()
    for (const hidden of ['attemptId', 'inReplyTo', 'structuredContentJson', worktree, repository.path]) {
      expect(text).not.toContain(hidden)
    }

    return JSON.parse(text)
  }

  const expectMessages = (runId: string, entries: Record<string, any>[], durable: CollaborationHistoryRow[]) => {
    expect(entries).toHaveLength(durable.length)
    entries.forEach((entry, index) => {
      const row = durable[index]
      expect(Object.keys(entry).sort()).toEqual([
        'actor', 'contentStatus', 'fields', 'id', 'occurredAtUtc', 'protocolVersion', 'provenance', 'recipient', 'runId', 'sequence', 'summary', 'type',
      ])
      expect(entry.sequence).toBe(row.Sequence)
      expect(sameId(entry.id, row.Id)).toBe(true)
      expect(sameId(entry.runId, runId)).toBe(true)
      expect(entry.type).toBe(row.Type)
      expect(entry.protocolVersion).toBe(row.ProtocolVersion)
      expect(entry.provenance).toBe(row.Provenance)
      expect(entry.actor).toEqual({ kind: row.ActorKind, role: row.ActorAgentRole, provider: row.ActorAgentProvider })
      expect(entry.recipient).toEqual({ kind: row.RecipientKind, role: row.RecipientAgentRole, provider: row.RecipientAgentProvider })
      expect(instant(entry.occurredAtUtc)).toBe(instant(row.OccurredAtUtc))
      expect(entry.contentStatus, `message ${row.Sequence} (${row.Type}) must be available`).toBe('Available')
      expect(entry.summary).toBe(row.Summary)
      expect(entry.fields).toEqual(Object.entries(JSON.parse(row.StructuredContentJson)).map(([name, value]) => ({ name, value })))
    })
  }

  // The fixed labels of the closed protocol, restated here independently of the cockpit source: the rendered text must agree with them.
  const TYPE_LABELS: Record<string, string> = {
    Proposal: 'Proposal', Acceptance: 'Acceptance', Challenge: 'Challenge', Question: 'Question', Decision: 'Decision',
    ExecutionReport: 'Execution report', ReviewFinding: 'Review finding', RevisionResponse: 'Revision response',
    Escalation: 'Escalation', ReviewApproval: 'Review approval', HumanInstruction: 'Human instruction',
  }
  const FIELD_LABELS: Record<string, [string, string][]> = {
    Proposal: [['scope', 'Scope'], ['implementationSteps', 'Implementation steps'], ['risks', 'Risks'], ['verificationPlan', 'Verification plan'], ['escalationPoints', 'Escalation points']],
    Acceptance: [['rationale', 'Rationale']],
    Challenge: [['disputedItem', 'Disputed item'], ['materialImpact', 'Material impact'], ['reasoning', 'Reasoning'], ['alternativeOrQuestion', 'Alternative or question']],
    Question: [['question', 'Question'], ['why', 'Why it matters']],
    Decision: [['resolution', 'Resolution'], ['rationale', 'Rationale'], ['resultingPlanChanges', 'Resulting plan changes'], ['nextAction', 'Next action']],
    ExecutionReport: [['completedWork', 'Completed work'], ['verification', 'Verification']],
    ReviewFinding: [['severity', 'Severity'], ['category', 'Category'], ['evidence', 'Evidence'], ['requiredChange', 'Required change']],
    RevisionResponse: [['disposition', 'Disposition'], ['evidence', 'Evidence'], ['resultingSourceChanges', 'Resulting source changes']],
    Escalation: [['unresolvedDecision', 'Unresolved decision'], ['options', 'Options'], ['consequences', 'Consequences'], ['evidence', 'Evidence'], ['recommendedChoice', 'Recommended choice']],
    ReviewApproval: [['rationale', 'Rationale'], ['residualRisks', 'Residual risks']],
    HumanInstruction: [['instruction', 'Instruction'], ['rationale', 'Rationale']],
  }
  const PROVENANCE_LABELS: Record<string, string> = {
    Simulated: 'Simulated', ProviderObserved: 'Provider-observed', HostConstructed: 'Host-constructed', HumanSubmitted: 'Human-submitted',
  }
  const ROLE_LABELS: Record<string, string> = {
    Planner: 'Planner', CriticalReviewer: 'Critical reviewer', Resolver: 'Resolver', Implementer: 'Implementer', CodeReviewer: 'Code reviewer',
  }
  const participantText = (participant: { kind: string; role: string | null; provider: string | null }) =>
    participant.kind === 'Agent'
      ? `Agent · ${participant.role === null ? 'role not recorded' : `${ROLE_LABELS[participant.role]} role`} · ${participant.provider === 'ClaudeCode' ? 'Claude Code' : participant.provider}`
      : participant.kind

  // Every rendered message is compared with the host's own page: sequence, type, version, provenance, sender and recipient wording, the
  // summary and EVERY fixed field label with its exact value, in the closed schema's order and with no field or text beyond it.
  const expectMessagesRendered = async (entries: Record<string, any>[], rendered: number) => {
    await expect(messageRows()).toHaveCount(rendered)
    for (const [index, entry] of entries.entries()) {
      const row = messageRows().nth(index)
      await expect(row.locator('.dc-collaboration-history-sequence')).toHaveText(`Sequence ${entry.sequence}`)
      await expect(row.locator('.dc-collaboration-history-type')).toHaveText(TYPE_LABELS[entry.type])
      await expect(row.locator('p').first()).toContainText(`Protocol ${entry.protocolVersion}`)
      await expect(row.locator('p').first()).toContainText(PROVENANCE_LABELS[entry.provenance])
      await expect(row.locator('p').nth(1)).toHaveText(`From ${participantText(entry.actor)} to ${participantText(entry.recipient)}`)
      await expect(row.locator('.dc-collaboration-history-summary')).toHaveText(`Summary: ${entry.summary}`)
      const schema = FIELD_LABELS[entry.type]
      const byName = new Map<string, string>(entry.fields.map((field: { name: string; value: string }) => [field.name, field.value]))
      expect(byName.size, `message ${entry.sequence} carries exactly its schema`).toBe(schema.length)
      const pairs = row.locator('dl > div')
      await expect(pairs).toHaveCount(schema.length)
      for (const [position, [name, label]] of schema.entries()) {
        await expect(pairs.nth(position).locator('dt')).toHaveText(label)
        await expect(pairs.nth(position).locator('dd')).toHaveText(byName.get(name) as string)
      }

      await expect(row.locator('.dc-collaboration-history-id')).toHaveText(`Message ${entry.id}`)
      await expect(row).not.toContainText('Content unavailable')
    }
  }

  // Opens the recorded collaboration of the selected run, pages explicitly until the last page, and proves that the pages, in order and
  // without repetition, are exactly the durable messages of that run. Returns the first (newest) page exactly as the host answered it.
  const browseRecordedMessages = async (runId: string, durable: CollaborationHistoryRow[], expectPages: number) => {
    await expectCollapsedMessages()
    const first = await readMessagePage(runId, null, () => messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click())
    expect(Object.keys(first).sort()).toEqual(['entries', 'hasMore', 'nextBeforeSequence', 'runId'])
    expect(sameId(first.runId, runId)).toBe(true)
    let collected: Record<string, any>[] = first.entries
    let body = first
    let pages = 1
    expectMessages(runId, first.entries, durable.slice(0, MESSAGE_PAGE))
    await expectMessagesRendered(collected, collected.length)
    await expect(messagesRegion()).toContainText('not proof that any provider succeeded')
    while (body.hasMore) {
      expect(body.entries).toHaveLength(MESSAGE_PAGE)
      expect(body.nextBeforeSequence).toBe(body.entries[MESSAGE_PAGE - 1].sequence)
      const cursor: string = body.nextBeforeSequence
      body = await readMessagePage(runId, cursor, () => messagesRegion().getByRole('button', { name: 'Load older messages' }).click())
      expectMessages(runId, body.entries, durable.slice(collected.length, collected.length + MESSAGE_PAGE))
      collected = [...collected, ...body.entries]
      pages += 1
      await expectMessagesRendered(collected, collected.length)
    }

    expect(body.nextBeforeSequence).toBeNull()
    expect(pages).toBe(expectPages)
    expectMessages(runId, collected, durable)
    expect(new Set(collected.map((entry) => lower(entry.id))).size).toBe(durable.length)
    await expect(messagesRegion().getByRole('button', { name: 'Load older messages' })).toHaveCount(0)
    await expect(messagesRegion().getByRole('alert')).toHaveCount(0)
    return first
  }

  // Recorded reply (ADR-0036). One explicit action per loaded message reads exactly that message's own stored reply parent, even when the
  // parent lies outside the pages loaded so far. The answer is compared with the durable rows (never seeded or edited) and with what the
  // single inspector renders; loaded pages, the cursor and every other history request stay untouched.
  const expectParentRendered = async (inspector: Locator, entry: Record<string, any>) => {
    const parent = inspector.locator('.dc-collaboration-reply-parent')
    await expect(parent).toHaveCount(1)
    await expect(parent.locator('p').first()).toHaveText('Recorded reply parent')
    await expect(parent.locator('.dc-collaboration-history-sequence')).toHaveText(`Sequence ${entry.sequence}`)
    await expect(parent.locator('.dc-collaboration-history-type')).toHaveText(TYPE_LABELS[entry.type])
    await expect(parent.locator('p').nth(1)).toContainText(`Protocol ${entry.protocolVersion}`)
    await expect(parent.locator('p').nth(1)).toContainText(PROVENANCE_LABELS[entry.provenance])
    await expect(parent.locator('p').nth(2)).toHaveText(`From ${participantText(entry.actor)} to ${participantText(entry.recipient)}`)
    await expect(parent.locator('.dc-collaboration-history-summary')).toHaveText(`Summary: ${entry.summary}`)
    const schema = FIELD_LABELS[entry.type]
    const byName = new Map<string, string>(entry.fields.map((field: { name: string; value: string }) => [field.name, field.value]))
    expect(byName.size, `parent ${entry.sequence} carries exactly its schema`).toBe(schema.length)
    const pairs = parent.locator('dl > div')
    await expect(pairs).toHaveCount(schema.length)
    for (const [position, [name, label]] of schema.entries()) {
      await expect(pairs.nth(position).locator('dt')).toHaveText(label)
      await expect(pairs.nth(position).locator('dd')).toHaveText(byName.get(name) as string)
    }

    await expect(parent.locator('.dc-collaboration-history-id')).toHaveText(`Message ${entry.id}`)
    await expect(parent).not.toContainText('Content unavailable')
    await expect(parent.getByRole('button')).toHaveCount(0)
  }

  let repliesInspected = 0
  const inspectRecordedReply = async (runId: string, index: number, loaded: number, durable: CollaborationHistoryRow[]) => {
    const links = data.collaborationReplyLinksOf(runId)
    const source = durable[index]
    const link = links.find((candidate) => sameId(candidate.Id, source.Id))!
    const parentRow = link.InReplyToMessageId === null ? null : durable.find((row) => sameId(row.Id, link.InReplyToMessageId))
    expect(link.InReplyToMessageId === null || parentRow !== undefined, 'the stored reply target belongs to this run').toBe(true)
    const expectedStatus = link.InReplyToMessageId !== null ? 'Available' : source.Type === 'Proposal' ? 'None' : 'Unavailable'
    const row = messageRows().nth(index)
    await expect(messageRows()).toHaveCount(loaded)
    await expect(row.locator('p').first().locator('.dc-collaboration-history-sequence')).toHaveText(`Sequence ${source.Sequence}`)
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && REPLY_PATH.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    )
    const pagesBefore = messageRequests.length
    const repliesBefore = replyRequests.length
    await row.getByRole('button', { name: 'Inspect recorded reply' }).click()
    const response = await answered
    expect(response.status()).toBe(200)
    const url = new URL(response.url())
    expect(url.pathname.toLowerCase()).toBe(`/api/runs/${lower(runId)}/collaboration-history/${lower(source.Id)}/reply`)
    expect(url.search).toBe('')
    expect(response.request().postData()).toBeNull()
    expect(replyRequests).toHaveLength(repliesBefore + 1)
    repliesInspected += 1
    const text = await response.text()
    for (const hidden of ['attemptId', 'inReplyTo', 'structuredContentJson', worktree, repository.path]) {
      expect(text).not.toContain(hidden)
    }

    const body = JSON.parse(text)
    expect(Object.keys(body).sort()).toEqual(['messageId', 'parent', 'runId', 'sourceSequence', 'status'])
    expect(sameId(body.runId, runId)).toBe(true)
    expect(sameId(body.messageId, source.Id)).toBe(true)
    expect(body.sourceSequence).toBe(source.Sequence)
    expect(body.status).toBe(expectedStatus)
    const inspector = row.getByRole('region', { name: 'Recorded reply' })
    await expect(messagesRegion().getByRole('region', { name: 'Recorded reply' })).toHaveCount(1)
    await expect(inspector.locator('p').nth(1)).toHaveText(`Recorded reply of sequence ${source.Sequence}`)
    if (parentRow) {
      expectMessages(runId, [body.parent], [parentRow])
      await expectParentRendered(inspector, body.parent)
    } else {
      expect(body.parent).toBeNull()
      await expect(inspector).toContainText(expectedStatus === 'None' ? 'No reply parent is recorded' : 'Reply parent unavailable')
      await expect(inspector.locator('.dc-collaboration-reply-parent')).toHaveCount(0)
    }

    // Reading a parent never changes the loaded pages, their cursor or any other history read.
    expect(messageRequests).toHaveLength(pagesBefore)
    await expect(messageRows()).toHaveCount(loaded)
    return { body, parentRow, inspector }
  }

  // Recorded attempt (ADR-0037). One explicit action per loaded message reads exactly the Agent attempt that message itself references.
  // The answer is compared with the durable message and attempt rows (never seeded or edited) and with what the single attempt
  // inspector renders; the reply inspector, the loaded pages, their cursor and every other history request stay untouched.
  const CONTRACT_LABELS: Record<string, string> = {
    Proposal: 'Proposal', CriticalReview: 'Critical review', ChallengeResolution: 'Challenge resolution',
    ImplementationReport: 'Implementation report', ImplementationReview: 'Implementation review', ReviewCorrection: 'Review correction',
    VerificationDiagnosis: 'Verification diagnosis',
  }
  const expectAttemptHeaderRendered = async (inspector: Locator, header: Record<string, any>) => {
    const box = inspector.locator('.dc-collaboration-attempt-header')
    await expect(box).toHaveCount(1)
    await expect(box.locator('p').first()).toHaveText('Recorded attempt header')
    const iso = (value: string | null) => (value === null ? 'Not recorded' : new Date(value).toISOString())
    const rows: [string, string][] = [
      ['Attempt', header.id],
      ['Attempt number', String(header.attemptNumber)],
      ['Role', ROLE_LABELS[header.role]],
      ['Provider', header.provider === 'ClaudeCode' ? 'Claude Code' : header.provider],
      ['Response contract', CONTRACT_LABELS[header.responseContract]],
      ['Recorded status', header.status],
      ['Claimed', iso(header.claimedAtUtc)],
      ['Dispatched', iso(header.dispatchedAtUtc)],
      ['Completed', iso(header.completedAtUtc)],
    ]
    await expect(box.locator('dl > div')).toHaveCount(rows.length)
    for (const [position, [label, value]] of rows.entries()) {
      await expect(box.locator('dl > div').nth(position).locator('dt')).toHaveText(label)
      await expect(box.locator('dl > div').nth(position).locator('dd')).toHaveText(value)
    }

    await expect(box.getByRole('button')).toHaveCount(0)
  }

  let attemptsInspected = 0
  const inspectRecordedAttempt = async (runId: string, index: number, loaded: number, durable: CollaborationHistoryRow[]) => {
    const source = durable[index]
    const stored = data.collaborationAttemptLinksOf(runId).find((candidate) => sameId(candidate.MessageId, source.Id))!
    const providerObserved = stored.Provenance === 'ProviderObserved'
    if (providerObserved) {
      expect(stored.AttemptRowId, 'a provider-observed journey message references an attempt row').not.toBeNull()
      expect(sameId(stored.AttemptRunId, runId)).toBe(true)
      expect(stored.Kind).toBe('Agent')
    }

    const expectedStatus = providerObserved ? 'Available' : 'NoAgentAttempt'
    const row = messageRows().nth(index)
    await expect(messageRows()).toHaveCount(loaded)
    await expect(row.locator('p').first().locator('.dc-collaboration-history-sequence')).toHaveText(`Sequence ${source.Sequence}`)
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && ATTEMPT_PATH.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    )
    const pagesBefore = messageRequests.length
    const repliesBefore = replyRequests.length
    const attemptsBefore = attemptRequests.length
    await row.getByRole('button', { name: 'Inspect recorded attempt' }).click()
    const response = await answered
    expect(response.status()).toBe(200)
    const url = new URL(response.url())
    expect(url.pathname.toLowerCase()).toBe(`/api/runs/${lower(runId)}/collaboration-history/${lower(source.Id)}/attempt`)
    expect(url.search).toBe('')
    expect(response.request().postData()).toBeNull()
    expect(attemptRequests).toHaveLength(attemptsBefore + 1)
    attemptsInspected += 1
    const text = await response.text()
    for (const hidden of ['summary', 'structuredContent', 'inReplyTo', 'outcome', 'sessionId', 'checkpoint', 'fingerprint', worktree, repository.path]) {
      expect(text).not.toContain(hidden)
    }

    const body = JSON.parse(text)
    expect(Object.keys(body).sort()).toEqual(['attempt', 'messageId', 'runId', 'sourceSequence', 'status'])
    expect(sameId(body.runId, runId)).toBe(true)
    expect(sameId(body.messageId, source.Id)).toBe(true)
    expect(body.sourceSequence).toBe(source.Sequence)
    expect(body.status).toBe(expectedStatus)
    const inspector = row.getByRole('region', { name: 'Recorded attempt' })
    await expect(messagesRegion().getByRole('region', { name: 'Recorded attempt' })).toHaveCount(1)
    await expect(inspector.locator('p').nth(1)).toHaveText(`Recorded attempt of sequence ${source.Sequence}`)
    if (providerObserved) {
      expect(Object.keys(body.attempt).sort()).toEqual([
        'attemptNumber', 'claimedAtUtc', 'completedAtUtc', 'dispatchedAtUtc', 'id', 'provider', 'responseContract', 'role', 'status',
      ])
      expect(sameId(body.attempt.id, stored.AttemptRowId)).toBe(true)
      expect(body.attempt.attemptNumber).toBe(stored.AttemptNumber)
      expect(body.attempt.role).toBe(stored.AgentRole)
      expect(body.attempt.provider).toBe(stored.AgentProvider)
      expect(body.attempt.responseContract).toBe(stored.AgentResponseContract)
      expect(body.attempt.status).toBe(stored.Status)
      expect(instant(body.attempt.claimedAtUtc)).toBe(instant(stored.ClaimedAtUtc!))
      expect(body.attempt.dispatchedAtUtc === null ? null : instant(body.attempt.dispatchedAtUtc)).toBe(
        stored.AgentDispatchedAtUtc === null ? null : instant(stored.AgentDispatchedAtUtc),
      )
      expect(body.attempt.completedAtUtc === null ? null : instant(body.attempt.completedAtUtc)).toBe(
        stored.CompletedAtUtc === null ? null : instant(stored.CompletedAtUtc),
      )
      await expectAttemptHeaderRendered(inspector, body.attempt)
    } else {
      expect(body.attempt).toBeNull()
      await expect(inspector).toContainText('No Agent attempt is attributed')
      await expect(inspector.locator('.dc-collaboration-attempt-header')).toHaveCount(0)
    }

    // Reading an attribution never changes the loaded pages, their cursor, the reply reads or any other history read.
    expect(messageRequests).toHaveLength(pagesBefore)
    expect(replyRequests).toHaveLength(repliesBefore)
    await expect(messageRows()).toHaveCount(loaded)
    return { body, stored, inspector }
  }

  // Opens the history (one page request), checks that page on the wire and in the rendered rows, selects the older delivered run and
  // returns its page and the receipt the host answered for exactly that run.
  const openHistoryAndInspectDelivery = async () => {
    const historyResponse = page.waitForResponse(
      (response) => response.request().method() === 'GET' && HISTORY_PATH.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    )
    const historyBefore = historyRequests.length
    const eventsBefore = eventRequests.length
    const messagesBefore = messageRequests.length
    await historyRegion.getByRole('button', { name: 'Show run history' }).click()
    const answered = await historyResponse
    expect(answered.status()).toBe(200)
    expect(historyRequests).toHaveLength(historyBefore + 1)
    const body = await answered.json()
    expect(Object.keys(body).sort()).toEqual(['entries', 'hasMore', 'nextBeforeExecutionNumber', 'projectId'])
    expect(lower(body.projectId)).toBe(lower(data.projectId()))
    expect(body.hasMore).toBe(false)
    expect(body.nextBeforeExecutionNumber).toBeNull()
    expect(body.entries.map((entry: Record<string, unknown>) => entry.executionNumber)).toEqual([2, 1])
    const [newer, older] = body.entries
    const entryKeys = [
      'createdAtUtc', 'executionMode', 'executionNumber', 'lastAdvancedAtUtc', 'lifecycle', 'objective', 'projectId', 'receiptSource', 'runId', 'stage',
    ]
    expect(Object.keys(newer).sort()).toEqual(entryKeys)
    expect(Object.keys(older).sort()).toEqual(entryKeys)
    expect(newer).toMatchObject({ objective: FOLLOW_UP_OBJECTIVE, lifecycle: 'Created', stage: 'Intake', executionMode: 'ManualAgent', receiptSource: null })
    expect(lower(newer.runId)).toBe(lower(followUpRun.Id))
    expect(older).toMatchObject({ objective: OBJECTIVE, lifecycle: 'Completed', stage: 'Completed', executionMode: 'ManualAgent' })
    expect(lower(older.runId)).toBe(lower(firstRunId))
    expect(Object.keys(older.receiptSource).sort()).toEqual(['checkpointId', 'checkpointNumber', 'commitSha', 'operationId', 'runId'])
    expect(older.receiptSource).toMatchObject({ commitSha: deliveredCommit, checkpointNumber: 3 })
    expect(lower(older.receiptSource.runId)).toBe(lower(firstRunId))
    expect(lower(older.receiptSource.operationId)).toBe(lower(operation!.Id))
    expect(lower(older.receiptSource.checkpointId)).toBe(lower(cps[2].Id))
    const historyText = JSON.stringify(body)
    for (const hidden of [worktree, repository.path, 'dotnet', 'AGENTS.md', operation!.CheckpointFingerprintSha256]) {
      expect(historyText).not.toContain(hidden)
    }

    await expect(historyRows).toHaveCount(2)
    await expect(historyRows.nth(0)).toContainText('Run #2')
    await expect(historyRows.nth(0)).toContainText(FOLLOW_UP_OBJECTIVE)
    await expect(historyRows.nth(0)).toContainText('Created · Intake')
    await expect(historyRows.nth(1)).toContainText('Run #1')
    await expect(historyRows.nth(1)).toContainText(OBJECTIVE)
    await expect(historyRows.nth(1)).toContainText('Completed · Completed')
    await expect(historyRegion).toContainText('not live progress')
    await expect(historyRegion.getByRole('button', { name: 'Load older runs' })).toHaveCount(0)

    const receiptRead = page.waitForResponse(
      (response) => response.request().method() === 'GET' && new URL(response.url()).pathname.toLowerCase() === firstRunReceiptPath,
      { timeout: 30_000 },
    )
    const receiptsBefore = receiptRequests.length
    await historyRegion.getByRole('button', { name: 'Inspect run #1' }).click()
    const receiptAnswer = await receiptRead
    expect(receiptAnswer.status()).toBe(200)
    // The dev server runs React's StrictMode, which may repeat a mount-time read; every read is for exactly the selected run.
    expect(receiptRequests.length).toBeGreaterThan(receiptsBefore)
    expect(new Set(receiptRequests.slice(receiptsBefore))).toEqual(new Set([firstRunReceiptPath]))
    const detail = historyRegion.getByRole('region', { name: 'Selected run' })
    await expect(detail).toContainText(OBJECTIVE)
    await expect(detail).toContainText(lower(firstRunId))
    await expect(detail).toContainText(`commit ${deliveredCommit}, operation ${lower(operation!.Id)}, checkpoint #3 (${lower(cps[2].Id)})`)
    await assertReceiptRegion(detail.getByRole('region', { name: 'Local delivery receipt' }))
    // The selected historical run offers only its own close control and the two collapsed toggles (recorded events and recorded collaboration), which have read nothing:
    // no mutation, configuration or live-progress control.
    expect(await detail.getByRole('button').allTextContents()).toEqual(['Show recorded events', 'Show recorded collaboration', 'Close selected run'])
    await expectCollapsedEvents()
    expect(eventRequests).toHaveLength(eventsBefore)
    await expectCollapsedMessages()
    expect(messageRequests).toHaveLength(messagesBefore)
    await expect(historyRegion.getByRole('textbox')).toHaveCount(0)
    await expect(
      historyRegion.getByRole('button', { name: /Commit locally|Request|Approve|Abandon|Record manual|Prepare workspace|Capture checkpoint|Refresh evidence/ }),
    ).toHaveCount(0)
    return { body, receipt: await receiptAnswer.json() }
  }

  const firstReading = await openHistoryAndInspectDelivery()
  // The same exact receipt the delivery showed, read again from the pinned rows after another objective replaced it in the cockpit.
  expect(firstReading.receipt).toEqual(wire)

  // Recorded events of the older delivered run, through the rendered controls only: the newest page on open, every older page on an
  // explicit request until the last, a reload and a close and reopen. The pages, in order and without repetition, are exactly the
  // durable rows of that run (more than one page of them), and each read is for exactly this run with the exclusive exact cursor.
  const durableOlder = data.eventHeadersOf(firstRunId)
  expect(durableOlder.map((row) => row.EventType)).toEqual([...firstRunEvents].reverse())
  expect(durableOlder.length, 'the delivered run must record more than one page of events').toBeGreaterThan(PAGE)
  const olderFirstPage = await browseRecordedEvents(firstRunId, durableOlder, Math.ceil(durableOlder.length / PAGE))
  expect(olderFirstPage.hasMore).toBe(true)
  expect(olderFirstPage.nextBeforeSequence).toBe(durableOlder[PAGE - 1].Sequence)

  const reloadedEvents = await readEventPage(firstRunId, null, () =>
    eventsRegion().getByRole('button', { name: 'Reload newest events' }).click(),
  )
  expect(reloadedEvents).toEqual(olderFirstPage)
  await expectRendered(reloadedEvents.entries, PAGE)
  await expect(eventsRegion().getByRole('button', { name: 'Load older events' })).toHaveCount(1)

  await eventsRegion().getByRole('button', { name: 'Hide recorded events' }).click()
  await expectCollapsedEvents()
  const reopenedEvents = await readEventPage(firstRunId, null, () =>
    eventsRegion().getByRole('button', { name: 'Show recorded events' }).click(),
  )
  expect(reopenedEvents).toEqual(olderFirstPage)
  await expectRendered(reopenedEvents.entries, PAGE)

  // Recorded collaboration of the same older run: the actual durable messages of the whole journey (more than one page), read page by
  // page through rendered controls, then reloaded and closed and reopened. Opening it never changes the recorded events region.
  const durableMessages = data.collaborationRowsOf(firstRunId)
  expect(durableMessages.length, 'the delivered run must record more than one page of messages').toBeGreaterThan(MESSAGE_PAGE)
  expect(new Set(durableMessages.map((row) => row.Type)).size, 'the journey records several message types').toBeGreaterThan(3)
  const eventReadsBeforeMessages = eventRequests.length
  const olderMessagesFirstPage = await browseRecordedMessages(firstRunId, durableMessages, Math.ceil(durableMessages.length / MESSAGE_PAGE))
  expect(olderMessagesFirstPage.hasMore).toBe(true)
  expect(olderMessagesFirstPage.nextBeforeSequence).toBe(durableMessages[MESSAGE_PAGE - 1].Sequence)
  expect(eventRequests).toHaveLength(eventReadsBeforeMessages)
  await expect(eventRows()).toHaveCount(PAGE)

  const reloadedMessages = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Reload newest messages' }).click(),
  )
  expect(reloadedMessages).toEqual(olderMessagesFirstPage)
  await expectMessagesRendered(reloadedMessages.entries, MESSAGE_PAGE)
  await expect(messagesRegion().getByRole('button', { name: 'Load older messages' })).toHaveCount(1)

  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  const reopenedMessages = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click(),
  )
  expect(reopenedMessages).toEqual(olderMessagesFirstPage)
  await expectMessagesRendered(reopenedMessages.entries, MESSAGE_PAGE)

  // Recorded reply of a loaded message whose own stored parent lies OUTSIDE the loaded page (only the newest page is loaded here, and
  // the parent is never seeded: it is the actual durable reply identity of the journey's own message).
  const newestPage = durableMessages.slice(0, MESSAGE_PAGE)
  const newestIds = new Set(newestPage.map((row) => lower(row.Id)))
  const outsideIndex = newestPage.findIndex((row) => {
    const target = data.collaborationReplyLinksOf(firstRunId).find((link) => sameId(link.Id, row.Id))!.InReplyToMessageId
    return target !== null && !newestIds.has(lower(target))
  })
  expect(outsideIndex, 'a message of the newest page must reply to a parent outside that page').toBeGreaterThanOrEqual(0)
  expect(replyRequests).toEqual([])
  const outside = await inspectRecordedReply(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(outside.parentRow).toBeTruthy()
  expect(newestIds.has(lower(outside.parentRow!.Id))).toBe(false)
  await expect(messagesRegion().getByRole('button', { name: 'Load older messages' })).toHaveCount(1)

  // Closing ends the inspector without a request; inspecting again reads the same answer.
  const repliesBeforeClose = replyRequests.length
  await outside.inspector.getByRole('button', { name: 'Close recorded reply' }).click()
  await expect(messagesRegion().getByRole('region', { name: 'Recorded reply' })).toHaveCount(0)
  expect(replyRequests).toHaveLength(repliesBeforeClose)
  await expect(messageRows()).toHaveCount(MESSAGE_PAGE)
  const outsideAgain = await inspectRecordedReply(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(outsideAgain.body).toEqual(outside.body)

  // Hiding the recorded collaboration ends the inspector; reopening reads the newest page again with no inspector and no reply read.
  const repliesBeforeHide = replyRequests.length
  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  await expect(messagesRegion().getByRole('region', { name: 'Recorded reply' })).toHaveCount(0)
  const reopenedAfterReply = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click(),
  )
  expect(reopenedAfterReply).toEqual(olderMessagesFirstPage)
  await expect(messagesRegion().getByRole('region', { name: 'Recorded reply' })).toHaveCount(0)
  expect(replyRequests).toHaveLength(repliesBeforeHide)
  const outsideReopened = await inspectRecordedReply(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(outsideReopened.body).toEqual(outside.body)

  // Recorded attempt of a loaded message (pre-reload). The reply inspector of the same row is open: the two inspectors are independent.
  expect(attemptRequests).toEqual([])
  const outsideReplyBody = outsideReopened.body
  const attemptFirst = await inspectRecordedAttempt(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(attemptFirst.body.status).toBe('Available')
  await expect(messageRows().nth(outsideIndex).getByRole('region', { name: 'Recorded reply' })).toHaveCount(1)
  await expect(messageRows().nth(outsideIndex).getByRole('region', { name: 'Recorded reply' }).locator('.dc-collaboration-reply-parent')).toHaveCount(1)

  // Closing ends the attempt inspector without a request or touching the reply inspector; inspecting again reads the same answer.
  const attemptsBeforeClose = attemptRequests.length
  await attemptFirst.inspector.getByRole('button', { name: 'Close recorded attempt' }).click()
  await expect(messagesRegion().getByRole('region', { name: 'Recorded attempt' })).toHaveCount(0)
  await expect(messageRows().nth(outsideIndex).getByRole('region', { name: 'Recorded reply' })).toHaveCount(1)
  expect(attemptRequests).toHaveLength(attemptsBeforeClose)
  const attemptAgain = await inspectRecordedAttempt(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(attemptAgain.body).toEqual(attemptFirst.body)

  // Hiding the recorded collaboration ends both inspectors; reopening starts with neither and no attempt read.
  const attemptsBeforeHide = attemptRequests.length
  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  const reopenedAfterAttempt = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click(),
  )
  expect(reopenedAfterAttempt).toEqual(olderMessagesFirstPage)
  await expect(messagesRegion().getByRole('region', { name: 'Recorded attempt' })).toHaveCount(0)
  expect(attemptRequests).toHaveLength(attemptsBeforeHide)
  const attemptReopened = await inspectRecordedAttempt(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(attemptReopened.body).toEqual(attemptFirst.body)
  expect(outsideReplyBody).toEqual(outside.body)

  // The newer run has no completed delivery source: that is disclosed, no receipt is requested for it, and nothing is inferred.
  const receiptsBeforeNewer = receiptRequests.length
  await historyRegion.getByRole('button', { name: 'Inspect run #2' }).click()
  const newerDetail = historyRegion.getByRole('region', { name: 'Selected run' })
  await expect(newerDetail).toContainText(FOLLOW_UP_OBJECTIVE)
  await expect(newerDetail).toContainText('No completed delivery source is available in this view')
  await expect(newerDetail.getByRole('region', { name: 'Local delivery receipt' })).toHaveCount(0)
  expect(receiptRequests).toHaveLength(receiptsBeforeNewer)

  // The newer run's events replace the older run's inside the same detail: collapsed first, then only its own durable rows (one page),
  // and returning to the older run (A to B to A) starts collapsed again without reading anything.
  const durableNewer = data.eventHeadersOf(followUpRun.Id)
  expect(durableNewer.map((row) => row.EventType)).toEqual([...intakeEvents].reverse())
  expect(durableNewer.length).toBeGreaterThan(0)
  expect(durableNewer.length).toBeLessThanOrEqual(PAGE)
  const newerFirstPage = await browseRecordedEvents(followUpRun.Id, durableNewer, 1)
  expect(newerFirstPage.hasMore).toBe(false)
  expect(newerFirstPage.nextBeforeSequence).toBeNull()
  // The newer run recorded no collaboration message: its page is explicitly empty (one request), never the older run's messages.
  expect(data.collaborationRowsOf(followUpRun.Id)).toEqual([])
  const newerMessagesFirstPage = await browseRecordedMessages(followUpRun.Id, [], 1)
  expect(newerMessagesFirstPage.entries).toEqual([])
  expect(newerMessagesFirstPage.hasMore).toBe(false)
  await expect(messagesRegion()).toContainText('No collaboration messages have been recorded for this run.')
  const eventReadsBeforeReturn = eventRequests.length
  const messageReadsBeforeReturn = messageRequests.length
  await historyRegion.getByRole('button', { name: 'Inspect run #1' }).click()
  await expect(historyRegion.getByRole('region', { name: 'Selected run' })).toContainText(OBJECTIVE)
  await expectCollapsedEvents()
  await expectCollapsedMessages()
  expect(eventRequests).toHaveLength(eventReadsBeforeReturn)
  expect(messageRequests).toHaveLength(messageReadsBeforeReturn)

  // Closing discards every row and the selection; reopening reads a fresh first page and the same delivery again.
  await historyRegion.getByRole('button', { name: 'Hide run history' }).click()
  await expect(historyRows).toHaveCount(0)
  await expect(historyRegion.getByRole('region', { name: 'Selected run' })).toHaveCount(0)
  const reopened = await openHistoryAndInspectDelivery()
  expect(reopened.body).toEqual(firstReading.body)
  expect(reopened.receipt).toEqual(wire)

  // After a real reload the history is collapsed again and requests nothing until it is opened, then shows the same delivery again.
  const requestsBeforeReload = historyRequests.length
  const eventRequestsBeforeReload = eventRequests.length
  const messageRequestsBeforeReload = messageRequests.length
  await reloadAndReselect(page)
  await expect(page.getByRole('heading', { name: FOLLOW_UP_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(historyRegion.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
  expect(historyRequests).toHaveLength(requestsBeforeReload)
  expect(eventRequests).toHaveLength(eventRequestsBeforeReload)
  expect(messageRequests).toHaveLength(messageRequestsBeforeReload)
  const afterReload = await openHistoryAndInspectDelivery()
  expect(afterReload.body).toEqual(firstReading.body)
  expect(afterReload.receipt).toEqual(wire)

  // After the real browser reload the recorded collaboration is collapsed and read nothing; opening it again reads the same durable
  // messages page by page, rendered with the same labels and exact values as before the reload.
  await expectCollapsedMessages()
  expect(messageRequests).toHaveLength(messageRequestsBeforeReload)
  const afterReloadMessages = await browseRecordedMessages(firstRunId, durableMessages, Math.ceil(durableMessages.length / MESSAGE_PAGE))
  expect(afterReloadMessages).toEqual(olderMessagesFirstPage)

  // With every page loaded, a root Proposal's recorded reply is None (no parent is implied). Then, after a real browser reload, the
  // same parent outside the newest page is inspected again from a fresh newest-page open and the host answers exactly as before.
  const rootIndex = durableMessages.findIndex(
    (row) => row.Type === 'Proposal' && data.collaborationReplyLinksOf(firstRunId).find((link) => sameId(link.Id, row.Id))!.InReplyToMessageId === null,
  )
  expect(rootIndex, 'the journey records a root proposal').toBeGreaterThanOrEqual(0)
  const root = await inspectRecordedReply(firstRunId, rootIndex, durableMessages.length, durableMessages)
  expect(root.body.status).toBe('None')
  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  const reopenedAfterReload = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click(),
  )
  expect(reopenedAfterReload).toEqual(olderMessagesFirstPage)
  const outsideAfterReload = await inspectRecordedReply(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(outsideAfterReload.body).toEqual(outside.body)
  expect(replyRequests).toHaveLength(repliesInspected)
  expect(
    replyRequests.every(
      (request) =>
        request.method === 'GET'
        && request.search === ''
        && request.body === null
        && request.path.startsWith(`/api/runs/${lower(firstRunId)}/collaboration-history/`),
    ),
  ).toBe(true)

  // Recorded attempt after the real browser reload: a message on an older page that references a DIFFERENT durable attempt is read once
  // its page is loaded (this run records only provider-observed messages, and nothing is seeded, so the non-provider status is covered by
  // the host and UI tests); then the same newest-page attribution is read again from a fresh open and the host answers as before.
  const attemptLinks = data.collaborationAttemptLinksOf(firstRunId)
  const olderAttemptIndex = durableMessages.findIndex(
    (row, index) =>
      index >= MESSAGE_PAGE
      && attemptLinks.find((link) => sameId(link.MessageId, row.Id))?.Provenance === 'ProviderObserved'
      && !sameId(attemptLinks.find((link) => sameId(link.MessageId, row.Id))?.AttemptId, attemptFirst.stored.AttemptId),
  )
  expect(olderAttemptIndex, 'an older message references a different durable attempt').toBeGreaterThanOrEqual(0)
  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  await readMessagePage(firstRunId, null, () => messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click())
  let loadedForNonProvider = MESSAGE_PAGE
  let cursor: string | null = olderMessagesFirstPage.nextBeforeSequence
  while (olderAttemptIndex >= loadedForNonProvider) {
    const older = await readMessagePage(firstRunId, cursor, () => messagesRegion().getByRole('button', { name: 'Load older messages' }).click())
    loadedForNonProvider += older.entries.length
    cursor = older.nextBeforeSequence
  }

  const olderAttempt = await inspectRecordedAttempt(firstRunId, olderAttemptIndex, loadedForNonProvider, durableMessages)
  expect(olderAttempt.body.status).toBe('Available')
  expect(sameId(olderAttempt.body.attempt.id, attemptFirst.body.attempt.id)).toBe(false)
  await messagesRegion().getByRole('button', { name: 'Hide recorded collaboration' }).click()
  await expectCollapsedMessages()
  const reopenedAttemptAfterReload = await readMessagePage(firstRunId, null, () =>
    messagesRegion().getByRole('button', { name: 'Show recorded collaboration' }).click(),
  )
  expect(reopenedAttemptAfterReload).toEqual(olderMessagesFirstPage)
  const attemptAfterReload = await inspectRecordedAttempt(firstRunId, outsideIndex, MESSAGE_PAGE, durableMessages)
  expect(attemptAfterReload.body).toEqual(attemptFirst.body)
  expect(attemptRequests).toHaveLength(attemptsInspected)
  expect(
    attemptRequests.every(
      (request) =>
        request.method === 'GET'
        && request.search === ''
        && request.body === null
        && request.path.startsWith(`/api/runs/${lower(firstRunId)}/collaboration-history/`),
    ),
  ).toBe(true)

  // The history reads created nothing: no row of any workflow kind, no provider invocation, no new commit, an untouched source.
  expect(data.projectTotals()).toEqual(afterFollowUp)
  expect(data.eventTypesOf(firstRunId)).toEqual(firstRunEvents)
  expect(data.eventTypesOf(followUpRun.Id)).toEqual(intakeEvents)
  expect(data.runs().map((run) => [run.ExecutionNumber, run.Lifecycle, run.Stage])).toEqual([[1, 'Completed', 'Completed'], [2, 'Created', 'Intake']])
  expect(stageInvocations()).toHaveLength(afterDelivery.invocations)
  expect(git('rev-parse', 'HEAD')).toBe(deliveredCommit)
  expect(git('rev-list', '--count', 'HEAD')).toBe('2')
  expect(git('remote')).toBe('')
  const sourceAfterHistory = repositorySnapshot(repository.path)
  expect(sourceAfterHistory.head).toBe(sourceBefore.head)
  expect(sourceAfterHistory.status).toBe(sourceBefore.status)
  expect(sourceAfterHistory.candidate).toBe(sourceBefore.candidate)
  expect(sourceAfterHistory.branches).toBe(sourceAfterCommit.branches)

  // The launch secret never reached the page URL, browser storage, or any file under the owned root.
  expect(page.url()).not.toContain(journeySecret())
  expect(await page.evaluate(() => JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage))).not.toContain(journeySecret())
  const secret = Buffer.from(journeySecret())
  const walk = (directory: string): string[] =>
    readdirSync(directory, { withFileTypes: true }).flatMap((entry) => (entry.isDirectory() ? walk(join(directory, entry.name)) : [join(directory, entry.name)]))
  const leaking = walk(journeyRoot().root).filter((file) => {
    if (file.includes(`${join('workspaces')}`)) {
      return false
    }

    try {
      return readFileSync(file).includes(secret)
    } catch (error: unknown) {
      // Git's exclusively created index lock can disappear between directory enumeration and this read. It is a transient artifact,
      // never a stable place to retain the session secret; any other read failure remains a test failure.
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') {
        return false
      }

      throw error
    }
  })
  expect(leaking).toEqual([])
})
