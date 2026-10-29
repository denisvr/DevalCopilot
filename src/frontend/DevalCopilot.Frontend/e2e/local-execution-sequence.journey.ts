import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import {
  createJourneyRepository,
  journeyRoot,
  markInvocations,
  readInvocations,
  readLaunchTargetVerdict,
  repositorySnapshot,
} from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the explicitly authorized local execution cycle (ADR-0060), through the rendered controls of the production
// frontend over the real generated client, MVC authentication, mediator, SQLite, the planning and local execution coordinators and ALL
// supervisors. Only the provider and verification executables are deterministic owned doubles. The database and the doubles' invocation
// log are only READ. Real planning through the existing planning sequence yields an explicitly Accepted Planner root; then ONE rendered
// "Authorize local execution cycle" request (after its confirmation disclosure) is the only request that starts anything: the host itself
// claims implementation, verification pass one (both recipes fail), the diagnosis, the diagnosis-origin correction, pass two (both pass),
// a review that requests a change, the review-origin correction, pass three and the approving review, and ends at the exact boundary
// "Awaiting Human approval". Nothing is approved, committed or published up to that boundary. LOCAL-DELIVERY-CHAIN-01 then continues the
// SAME run with two separate explicit Human actions through rendered controls: approval of the complete final verification pass, and a
// local commit of that same final verified source, checked against the cycle's own final records. Nothing is pushed. This is
// deterministic-double evidence only: not provider reliability, automatic approval, remote publication, restart recovery or MVP completion.

const PROJECT = 'Local execution journey fixture'
const OBJECTIVE = 'Implement the ledger total through one authorized local cycle'
const RECIPES = ['Candidate total check', 'Candidate lint check']
const ARM_PLANNING = /\/api\/runs\/[^/]+\/planning-sequence$/
const ARM_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence$/
const FENCE_NOTE = /local execution sequence holds automatic authority/
const CHECKPOINT_REVIEW_PATH = /\/api\/projects\/[^/]+\/reviews$/
const LOCAL_COMMIT_PATH = /\/api\/runs\/[^/]+\/local-commit$/
const RECEIPT_PATH = /\/api\/runs\/[^/]+\/local-delivery-receipt$/

const STEP_KINDS = [
  'Implementation',
  'Verification', 'Verification',
  'VerificationDiagnosis',
  'DiagnosisCorrection',
  'Verification', 'Verification',
  'CodeReview',
  'ReviewCorrection',
  'Verification', 'Verification',
  'CodeReview',
]
const STEP_PASSES = [0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3]
const CONTRACTS = [
  'Proposal', 'CriticalReview', 'ImplementationReport', 'VerificationDiagnosis', 'ReviewCorrection', 'ImplementationReview',
  'ReviewCorrection', 'ImplementationReview',
]
const OUTCOMES = [
  'Proposed', 'Accepted', 'Implemented', 'DiagnosisFindingsRecorded', 'CorrectionApplied', 'ReviewChangesRequested', 'CorrectionApplied',
  'ReviewApproved',
]
const STAGE_SEQUENCE = [
  'Proposal', 'CriticalReview', 'ImplementationReport', 'verify:failed', 'verify:failed', 'VerificationDiagnosis', 'ReviewCorrection',
  'verify:passed', 'verify:passed', 'ImplementationReview', 'ReviewCorrection', 'verify:passed', 'verify:passed', 'ImplementationReview',
]

const data = new JourneyData(PROJECT)

interface SequenceRow {
  Id: string
  State: string
  ReasonCode: string | null
  PlanAuthority: string
  CorrectionAllowance: number
  RecipeCount: number
  PlanProposalMessageId: string
  CheckpointId: string
  FinalReportMessageId: string | null
  FinalReviewAttemptId: string | null
  FinalEscalationMessageId: string | null
  EndedAtUtc: string | null
}
interface StepRow {
  Id: string
  Ordinal: number
  Kind: string
  PassNumber: number
  RecipeOrdinal: number | null
  State: string
  ReservedOperationId: string
  OperationId: string | null
  PredecessorStepId: string | null
  SubjectAttemptId: string | null
  ReportMessageId: string | null
  SourceCheckpointId: string
}
interface RecipeRow {
  Ordinal: number
  VerificationCommandId: string
  CommandName: string
}

function sequences(): SequenceRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, State, ReasonCode, PlanAuthority, CorrectionAllowance, RecipeCount, PlanProposalMessageId, CheckpointId, FinalReportMessageId, FinalReviewAttemptId, FinalEscalationMessageId, EndedAtUtc from local_execution_sequences where RunId = ?',
        )
        .all(data.runId()) as unknown as SequenceRow[],
  )
}
function steps(sequenceId: string): StepRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, Ordinal, Kind, PassNumber, RecipeOrdinal, State, ReservedOperationId, OperationId, PredecessorStepId, SubjectAttemptId, ReportMessageId, SourceCheckpointId from local_execution_sequence_steps where SequenceId = ? order by Ordinal',
        )
        .all(sequenceId) as unknown as StepRow[],
  )
}
function recipes(sequenceId: string): RecipeRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Ordinal, VerificationCommandId, CommandName from local_execution_sequence_recipes where SequenceId = ? order by Ordinal')
        .all(sequenceId) as unknown as RecipeRow[],
  )
}
function localEventTypes(): string[] {
  return data.eventTypesOf(data.runId()).filter((type) => type.startsWith('local_execution_sequence.'))
}
function planningSequenceCount(): number {
  return withJourneyDb(
    (db) => (db.prepare('select count(*) as Count from planning_sequences where RunId = ?').get(data.runId()) as unknown as { Count: number }).Count,
  )
}

test('the local execution cycle journey: one authorization, both correction origins, three passes, Awaiting Human approval, then separate Human approval and local commit', async ({ page }) => {
  test.setTimeout(345_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('local-execution-source')
  const posts: string[] = []
  const gets: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST') {
      posts.push(new URL(request.url()).pathname)
    }
    if (request.method() === 'GET') {
      gets.push(new URL(request.url()).pathname)
    }
  })

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)

  const source = page.getByRole('region', { name: 'Source evidence' })
  const verification = page.getByRole('region', { name: 'Verification commands' })
  const planning = page.getByRole('region', { name: 'Planning sequence' })
  const local = page.getByRole('region', { name: 'Local execution sequence', exact: true })

  // 1. Project, identity, workspace, checkpoint, two verification recipes through the rendered form, and a manual run.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  const sourceBefore = repositorySnapshot(repository.path)
  for (const [index, name] of RECIPES.entries()) {
    await page.getByLabel('Verification command name').fill(name)
    await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
    await page.getByLabel('Verification arguments').fill('check')
    await page.getByRole('button', { name: 'Add command' }).click()
    await expect(verification).toContainText(`#${index + 1} ${name}`)
  }
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  const runId = data.runId()
  expect(data.attempts()).toEqual([])
  expect(sequences()).toEqual([])

  // 2. REAL planning through the existing planning sequence: the Planner root, accepted by the critical review.
  await expect(planning).toContainText('No planning sequence is armed', { timeout: 30_000 })
  await planning.getByRole('button', { name: 'Arm planning sequence' }).click()
  await expect(planning.getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute('data-state', 'ReadyForImplementation', {
    timeout: 120_000,
  })
  expect(data.attempts().map((attempt) => [attempt.AgentResponseContract, attempt.AgentOutcome])).toEqual([
    ['Proposal', 'Proposed'],
    ['CriticalReview', 'Accepted'],
  ])
  expect(planningSequenceCount()).toBe(1)
  expect(sequences()).toEqual([])
  const rootProposal = data.messages().find((message) => message.Type === 'Proposal')!
  const acceptance = data.messages().find((message) => message.Type === 'Acceptance')
  expect(acceptance).toBeDefined()

  // 3. The one authorization: open the confirmation, read its disclosure, confirm. Nothing else is requested from here on.
  await expect(local).toContainText('No local execution cycle is authorized for this run', { timeout: 30_000 })
  const attemptsBeforeArming = data.attempts().length
  await local.getByRole('button', { name: 'Authorize local execution cycle' }).click()
  const confirmation = local.getByRole('group', { name: 'Confirm local execution cycle' })
  await expect(confirmation).toBeVisible({ timeout: 30_000 })
  await expect(confirmation).toContainText('CAN MUTATE the workspace')
  await expect(confirmation).toContainText('At most 2 shared corrections')
  await expect(confirmation).toContainText('at most two hours')
  await expect(confirmation).toContainText('Approval and commit stay separate')
  await expect(confirmation).toContainText('Stop after current operation prevents successor operations only')
  await expect(confirmation).toContainText('AcceptedRoot')
  await expect(confirmation).toContainText(rootProposal.Id.toLowerCase())
  for (const [index, name] of RECIPES.entries()) {
    await expect(confirmation.getByRole('list', { name: 'Verification commands that can mutate the workspace' })).toContainText(`#${index + 1} ${name}`)
  }
  expect(data.attempts()).toHaveLength(attemptsBeforeArming) // availability and confirmation invoke and claim nothing
  expect(sequences()).toEqual([])

  const postsBeforeArming = posts.length
  const armed = page.waitForResponse((response) => response.request().method() === 'POST' && ARM_LOCAL.test(new URL(response.url()).pathname), {
    timeout: 30_000,
  })
  await confirmation.getByRole('button', { name: 'Confirm and authorize' }).click()
  expect((await armed).status()).toBeLessThan(300)

  // 4. While authority is live both regions withhold every manual start (the shared fence), and the fence copy is shown.
  const state = local.getByRole('status', { name: 'Local execution sequence state' })
  await expect(state).toHaveAttribute('data-state', /^(Armed|PreparingStep|AwaitingStepResult)$/, { timeout: 30_000 })
  await expect(page.getByText(FENCE_NOTE).first()).toBeVisible({ timeout: 30_000 })
  await expect(verification.getByRole('button', { name: 'Run', exact: true }).first()).toBeDisabled({ timeout: 30_000 })
  await expect(local.getByRole('button', { name: 'Stop after current operation' })).toBeVisible()

  // 5. The host alone advances to the exact approved-review boundary; the page only observes.
  await expect(state).toHaveAttribute('data-state', 'ReadyForHumanApproval', { timeout: 280_000 })
  await expect(local).toContainText('Awaiting Human approval')
  await expect(local).toContainText('not committed and not published')
  await expect(local).toContainText('Corrections claimed')
  await expect(local.getByRole('button', { name: 'Stop after current operation' })).toHaveCount(0)

  // Exactly one request left the browser since arming began: the arm POST itself. No per-stage, verification, review, approval or commit.
  expect(posts.slice(postsBeforeArming).filter((path) => path.startsWith('/api/'))).toHaveLength(1)
  expect(ARM_LOCAL.test(posts[postsBeforeArming])).toBe(true)
  expect(posts.filter((path) => ARM_PLANNING.test(path))).toHaveLength(1)
  expect(posts.filter((path) => /\/agent-attempts\//.test(path))).toEqual([])
  expect(posts.filter((path) => /\/verification-commands\/[^/]+\/executions$/.test(path))).toEqual([])
  expect(posts.filter((path) => /\/reviews$|local-commit|\/commit/.test(path))).toEqual([])

  // 6. Evidence from the host's own records: the sequence, its pinned recipes, its twelve steps and their exact operation links.
  const [sequence] = sequences()
  expect(sequences()).toHaveLength(1)
  expect(sequence.State).toBe('ReadyForHumanApproval')
  expect(sequence.ReasonCode).toBe('review_approved')
  expect(sequence.PlanAuthority).toBe('AcceptedRoot')
  expect(sameId(sequence.PlanProposalMessageId, rootProposal.Id)).toBe(true)
  expect(sequence.CorrectionAllowance).toBe(2)
  expect(sequence.RecipeCount).toBe(2)
  expect(sequence.EndedAtUtc).not.toBeNull()
  const pinned = recipes(sequence.Id)
  expect(pinned.map((recipe) => recipe.CommandName)).toEqual(RECIPES)
  expect(pinned.map((recipe) => sameId(recipe.VerificationCommandId, data.recipes()[recipe.Ordinal].Id))).toEqual([true, true])

  const allSteps = steps(sequence.Id)
  expect(allSteps.map((step) => step.Kind)).toEqual(STEP_KINDS)
  expect(allSteps.map((step) => step.PassNumber)).toEqual(STEP_PASSES)
  expect(allSteps.map((step) => step.Ordinal)).toEqual(STEP_KINDS.map((_, index) => index))
  expect(allSteps.every((step) => step.State === 'Concluded')).toBe(true)
  expect(allSteps.every((step) => step.OperationId !== null && sameId(step.OperationId, step.ReservedOperationId))).toBe(true)
  expect(allSteps.map((step) => step.RecipeOrdinal)).toEqual([null, 0, 1, null, null, 0, 1, null, null, 0, 1, null])
  expect(new Set(allSteps.map((step) => step.ReservedOperationId.toLowerCase())).size).toBe(12)

  // Agent steps link exactly the run's attempts after planning, in order; verification steps link the executions in order.
  const attempts = data.attempts()
  expect(attempts.map((attempt) => attempt.AgentResponseContract)).toEqual(CONTRACTS)
  expect(attempts.map((attempt) => attempt.AgentOutcome)).toEqual(OUTCOMES)
  expect(attempts.every((attempt) => attempt.Status === 'Completed')).toBe(true)
  expect(attempts.map((attempt) => attempt.AgentBudgetSlot)).toEqual([1, 2, 3, 4, 5, 6, 7, 8])
  const agentSteps = allSteps.filter((step) => step.Kind !== 'Verification')
  expect(agentSteps.map((step) => step.OperationId!.toLowerCase())).toEqual(attempts.slice(2).map((attempt) => attempt.Id.toLowerCase()))
  const executions = data.executions()
  expect(executions).toHaveLength(6)
  const verificationSteps = allSteps.filter((step) => step.Kind === 'Verification')
  expect(verificationSteps.map((step) => step.OperationId!.toLowerCase())).toEqual(executions.map((execution) => execution.Id.toLowerCase()))
  expect(executions.map((execution) => [execution.Status, execution.ExitCode])).toEqual([
    ['Failed', 1], ['Failed', 1], ['Passed', 0], ['Passed', 0], ['Passed', 0], ['Passed', 0],
  ])
  const commandIds = data.recipes().map((recipe) => recipe.Id.toLowerCase())
  expect(executions.map((execution) => execution.VerificationCommandId.toLowerCase())).toEqual([
    commandIds[0], commandIds[1], commandIds[0], commandIds[1], commandIds[0], commandIds[1],
  ])
  // Each pass verified exactly the checkpoint its source step named, and the three passes saw three distinct sources.
  const checkpoints = data.checkpoints()
  expect(executions.map((execution) => checkpoints.find((checkpoint) => sameId(checkpoint.Id, execution.GitCheckpointId))?.CheckpointNumber)).toEqual([
    2, 2, 3, 3, 4, 4,
  ])
  expect(new Set(checkpoints.map((checkpoint) => checkpoint.FingerprintSha256)).size).toBe(checkpoints.length)
  verificationSteps.forEach((step, index) => {
    expect(sameId(step.SourceCheckpointId, executions[index].GitCheckpointId)).toBe(true)
  })

  // Messages: the exact lineage of the two shared corrections and both reviews, with exactly the whole finding set of each origin.
  const messages = data.messages()
  const [implementation, diagnosis, correctionOne, reviewOne, correctionTwo, reviewTwo] = attempts.slice(2)
  const reportOf = (attemptId: string) => messages.find((message) => message.Type === 'ExecutionReport' && sameId(message.AttemptId, attemptId))!
  const findingsOf = (attemptId: string) => messages.filter((message) => message.Type === 'ReviewFinding' && sameId(message.AttemptId, attemptId))
  const reportImplementation = reportOf(implementation.Id)
  const reportOne = reportOf(correctionOne.Id)
  const reportTwo = reportOf(correctionTwo.Id)
  const diagnosisFindings = findingsOf(diagnosis.Id)
  const reviewFindings = findingsOf(reviewOne.Id)
  expect(diagnosisFindings).toHaveLength(1)
  expect(reviewFindings).toHaveLength(1)
  expect(data.inputsOf(diagnosis.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([reportImplementation.Id.toLowerCase()])
  expect(data.inputsOf(correctionOne.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([
    reportImplementation.Id.toLowerCase(),
    diagnosisFindings[0].Id.toLowerCase(),
  ])
  expect(sameId(data.inputsOf(reviewOne.Id)[0].CollaborationMessageId, reportOne.Id)).toBe(true)
  expect(data.inputsOf(correctionTwo.Id).map((input) => input.CollaborationMessageId.toLowerCase())).toEqual([
    reportOne.Id.toLowerCase(),
    reviewFindings[0].Id.toLowerCase(),
  ])
  expect(sameId(data.inputsOf(reviewTwo.Id)[0].CollaborationMessageId, reportTwo.Id)).toBe(true)
  expect(messages.filter((message) => message.Type === 'Escalation')).toEqual([])
  expect(messages.filter((message) => message.Type === 'HumanInstruction')).toEqual([])
  expect(findingsOf(reviewTwo.Id)).toEqual([])
  const approvals = messages.filter((message) => message.Type === 'ReviewApproval')
  expect(approvals).toHaveLength(1)
  expect(sameId(approvals[0].AttemptId, reviewTwo.Id)).toBe(true)

  // The step receipts carry the same links: report and subject identities per origin, the sequence's final references.
  expect(sameId(allSteps[3].ReportMessageId, reportImplementation.Id)).toBe(true) // diagnosis of the implementation report
  expect(sameId(allSteps[4].ReportMessageId, reportImplementation.Id)).toBe(true) // diagnosis-origin correction
  expect(sameId(allSteps[4].SubjectAttemptId, diagnosis.Id)).toBe(true)
  expect(sameId(allSteps[7].ReportMessageId, reportOne.Id)).toBe(true) // review of the first correction
  expect(sameId(allSteps[8].ReportMessageId, reportOne.Id)).toBe(true) // review-origin correction
  expect(sameId(allSteps[8].SubjectAttemptId, reviewOne.Id)).toBe(true)
  expect(sameId(allSteps[11].ReportMessageId, reportTwo.Id)).toBe(true)
  expect(sameId(sequence.FinalReportMessageId, reportTwo.Id)).toBe(true)
  expect(sameId(sequence.FinalReviewAttemptId, reviewTwo.Id)).toBe(true)
  expect(sequence.FinalEscalationMessageId).toBeNull()

  // Run limits: two shared corrections spent of two, and the reserved time (110 of 120 min) is within the unchanged ceilings.
  const limits = data.runLimits()
  expect(limits.MaximumReviewCorrectionAttempts).toBe(2)
  expect(attempts.filter((attempt) => attempt.AgentResponseContract === 'ReviewCorrection')).toHaveLength(2)
  expect(limits.Lifecycle).not.toBe('Completed')

  // The events of the cycle: armed once, twelve claims, twelve conclusions, one end.
  const events = localEventTypes()
  expect(events.filter((type) => type === 'local_execution_sequence.armed')).toHaveLength(1)
  expect(events.filter((type) => type === 'local_execution_sequence.step_claimed')).toHaveLength(12)
  expect(events.filter((type) => type === 'local_execution_sequence.step_concluded')).toHaveLength(12)
  expect(events.filter((type) => type === 'local_execution_sequence.ended')).toHaveLength(1)

  // Provider invocation counts: exactly the fourteen stage invocations, in order; both review outcomes were decided by the double
  // statelessly from the sealed report; no GitHub role was ever invoked.
  const invocations = readInvocations(mark)
  const stages = invocations.filter((entry) => entry.kind !== 'probe')
  expect(stages.map((entry) => entry.contract ?? `verify:${entry.outcome}`)).toEqual(STAGE_SEQUENCE)
  expect(stages.filter((entry) => entry.role === 'verify')).toHaveLength(6)
  expect(stages.filter((entry) => entry.contract === 'ImplementationReview').map((entry) => entry.outcome)).toEqual(['changesRequested', 'approved'])
  expect(stages.filter((entry) => entry.contract === 'ReviewCorrection').map((entry) => entry.findingCount)).toEqual([1, 1])
  expect(invocations.filter((entry) => entry.role === 'gh')).toEqual([])

  // Human approval, local commit and remote mutation: none. Agent reviews are the ordinary FutureAgent facts, never a Human review.
  const reviews = data.checkpointReviews()
  expect(reviews.filter((review) => review.ActorKind === 'Human')).toEqual([])
  expect(data.checkpointReviewEvidence().filter((evidence) => reviews.some((review) => sameId(review.Id, evidence.CheckpointReviewId) && review.ActorKind === 'Human'))).toEqual([])
  expect(data.localCommitOperationCount()).toBe(0)
  expect(data.localCommitOperation()).toBeNull()
  expect(data.gitHubObservationTargets()).toEqual([])
  const worktree = data.workspace().WorkspacePath
  const git = (...args: string[]) => execFileSync('git', ['-C', worktree, ...args], { stdio: 'pipe' }).toString().trim()
  expect(git('rev-list', '--count', 'HEAD')).toBe('1')
  expect(git('rev-parse', 'HEAD')).toBe(repository.baselineCommit)
  expect(git('remote')).toBe('')
  const sourceAfter = repositorySnapshot(repository.path)
  expect({ ...sourceAfter, branches: '' }).toEqual({ ...sourceBefore, branches: '' })
  expect(sourceAfter.branches).toEqual(sourceBefore.branches)

  // 7. The fence is released at the end (the same shared status feeds both regions) and shows no stale manual-start withholding.
  await expect(page.getByText(FENCE_NOTE)).toHaveCount(0, { timeout: 30_000 })

  // 8. A reload reads the durable result back: the same state, no step advanced, rearmed or re-requested, the fence released.
  const requestsBeforeReload = posts.length
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('region', { name: 'Local execution sequence', exact: true }).getByRole('status', { name: 'Local execution sequence state' })).toHaveAttribute(
    'data-state',
    'ReadyForHumanApproval',
    { timeout: 30_000 },
  )
  await expect(page.getByRole('region', { name: 'Local execution sequence', exact: true })).toContainText('Awaiting Human approval')
  await expect(page.getByRole('region', { name: 'Local execution sequence', exact: true }).getByRole('button', { name: 'Authorize local execution cycle' })).toHaveCount(0)
  await expect(page.getByText(FENCE_NOTE)).toHaveCount(0)
  // 9. LOCAL-INSPECT-01: the recorded inspection of the finished cycle, from the current run and again from Run history. Every read is an
  // explicit GET of the three inspection routes or of the existing selected-attempt evidence; nothing executes, claims or mutates.
  const inspectionPaths = (start: number) => gets.slice(start).filter((path) => path.includes('/local-execution-sequence/inspection'))
  const getsBeforeInspection = gets.length
  const current = page.getByRole('region', { name: 'Inspect local execution' })
  await expect(current.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
  expect(inspectionPaths(getsBeforeInspection)).toEqual([])
  await current.getByRole('button', { name: 'Inspect local execution' }).click()
  await expect(current).toContainText('Recorded state', { timeout: 30_000 })
  await expect(current).toContainText('Awaiting Human approval')
  await expect(current).toContainText('not a coherent replay')
  expect(inspectionPaths(getsBeforeInspection)).toEqual([`/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection`])
  await expect(current.getByRole('button', { name: /^Inspect step \d+$/ })).toHaveCount(12)

  // The implementation step is an Agent step: its immutable attempt header and the existing selected-attempt detail appear.
  await current.getByRole('button', { name: 'Inspect step 1', exact: true }).click()
  const stepDetail = current.getByRole('region', { name: 'Selected local execution step' })
  await expect(stepDetail.getByRole('region', { name: 'Selected Agent attempt evidence' })).toBeVisible({ timeout: 30_000 })
  expect(inspectionPaths(getsBeforeInspection)).toHaveLength(2)

  // The first verification failed with sealed output: the explicit window is the only read of bytes, once per request.
  await current.getByRole('button', { name: 'Inspect step 2', exact: true }).click()
  await expect(stepDetail).toContainText('Failed · process outcome Exited · exit code 1', { timeout: 30_000 })
  const windowsBefore = inspectionPaths(getsBeforeInspection).filter((path) => path.includes('/artifact-window/')).length
  expect(windowsBefore).toBe(0)
  const stepTwo = steps(sequence.Id)[1]
  await stepDetail.getByRole('button', { name: 'View standard output of this step' }).click()
  expect(inspectionPaths(getsBeforeInspection).filter((path) => path.includes('/artifact-window/'))).toEqual([])
  await stepDetail.getByRole('button', { name: 'Load standard output of this step' }).click()
  await expect(stepDetail).toContainText('bytes read', { timeout: 30_000 })
  expect(inspectionPaths(getsBeforeInspection).filter((path) => path.includes('/artifact-window/'))).toEqual([
    `/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection/steps/${stepTwo.Id.toLowerCase()}/artifact-window/stdout`,
  ])

  // Run history reopens the same recorded cycle for the selected (here: the same) run, again only on explicit actions.
  const getsBeforeHistory = gets.length
  const history = page.getByRole('region', { name: 'Run history' })
  await history.getByRole('button', { name: 'Show run history' }).click()
  await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
  const historical = history.getByRole('region', { name: 'Selected run' }).getByRole('region', { name: 'Inspect local execution' })
  expect(inspectionPaths(getsBeforeHistory)).toEqual([])
  await historical.getByRole('button', { name: 'Inspect local execution' }).click()
  await expect(historical).toContainText('Recorded state', { timeout: 30_000 })
  await expect(historical.getByRole('button', { name: /^Inspect step \d+$/ })).toHaveCount(12)
  expect(inspectionPaths(getsBeforeHistory)).toEqual([`/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection`])
  expect(posts.slice(requestsBeforeReload).filter((path) => path.startsWith('/api/'))).toEqual([])

  expect(posts.slice(requestsBeforeReload).filter((path) => path.startsWith('/api/'))).toEqual([])
  expect(sequences()).toHaveLength(1)
  expect(sequences()[0].State).toBe('ReadyForHumanApproval')
  expect(steps(sequence.Id)).toHaveLength(12)
  expect(data.attempts()).toHaveLength(8)
  expect(data.executions()).toHaveLength(6)
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(14)
  expect(data.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toEqual([])
  expect(data.localCommitOperationCount()).toBe(0)
  expect(runId).toBe(data.runId())

  // 10. LOCAL-DELIVERY-CHAIN-01: only now, as two separate explicit Human actions through the rendered controls, the complete final
  // verification pass is approved and then the SAME final verified source is committed locally. Everything above (including the
  // no-approval and no-commit assertions) ran first, at its original point, and still holds at this boundary.
  const finalCheckpoint = checkpoints[3]
  const finalPass = executions.slice(4)
  const finalSteps = verificationSteps.slice(4)
  expect(checkpoints.map((checkpoint) => checkpoint.CheckpointNumber)).toEqual([1, 2, 3, 4])
  expect(finalPass.map((execution) => sameId(execution.GitCheckpointId, finalCheckpoint.Id))).toEqual([true, true])
  expect(finalPass.map((execution) => execution.CompletionFingerprintSha256)).toEqual([finalCheckpoint.FingerprintSha256, finalCheckpoint.FingerprintSha256])
  expect(finalSteps.map((step) => sameId(step.SourceCheckpointId, finalCheckpoint.Id))).toEqual([true, true])
  expect(finalSteps.map((step) => step.OperationId!.toLowerCase())).toEqual(finalPass.map((execution) => execution.Id.toLowerCase()))
  const expectedMembers = finalPass.map((execution, index) => [commandIds[index], execution.Id.toLowerCase(), execution.ExecutionNumber])
  const membersOfReview = (reviewId: string) =>
    data
      .checkpointReviewEvidence()
      .filter((row) => sameId(row.CheckpointReviewId, reviewId))
      .map((row) => [row.VerificationCommandId.toLowerCase(), row.VerificationExecutionId.toLowerCase(), row.VerificationExecutionNumber])

  // The Agent reviews are the ordinary FutureAgent facts of the two cycle reviews: changes requested on checkpoint 3, approved on 4.
  const agentReviews = data.checkpointReviews()
  expect(agentReviews.map((review) => [review.ActorKind, review.Decision, review.CheckpointNumber])).toEqual([
    ['FutureAgent', 'ChangesRequested', 3],
    ['FutureAgent', 'Approved', 4],
  ])
  const agentFinalReview = agentReviews[1]
  expect(sameId(agentFinalReview.GitCheckpointId, finalCheckpoint.Id)).toBe(true)
  expect(membersOfReview(agentFinalReview.Id)).toEqual(expectedMembers)

  const cycleBefore = {
    totals: data.projectTotals(),
    sequences: sequences(),
    steps: steps(sequence.Id),
    attempts: data.attempts(),
    executions: data.executions(),
    messages: data.messages().length,
    invocations: readInvocations(mark).length,
    events: localEventTypes(),
  }

  // A reload gives a clean current cockpit (no historical regions) whose panels read the durable facts afresh.
  await page.reload()
  await selectProject(page, PROJECT)
  const manual = page.getByRole('region', { name: 'Checkpoint review evidence' })
  // Exact: the recorded local commit region ("Inspect recorded local commit") also contains this name as a substring (ADR-0070).
  const localCommit = page.getByRole('region', { name: 'Local commit', exact: true })
  const postsBeforeChain = posts.length
  const getsBeforeChain = gets.length

  // Before the Human acts, the host's own status names the one missing fact and the commit request is not offered at all.
  await expect(localCommit).toContainText('No human approval of this checkpoint was found.', { timeout: 30_000 })
  await expect(localCommit.getByRole('button', { name: 'Commit locally' })).toHaveCount(0)

  // 10a. The complete Human approval: the rendered action shows the exact bundle it will submit (both enabled recipes, each with its
  // latest Passed execution of the final checkpoint), and one click records one Human review whose members are exactly that bundle.
  await expect(manual.getByLabel('Reviewer')).toHaveValue('Human', { timeout: 30_000 })
  const approveAll = manual.getByRole('button', { name: 'Approve all enabled checks' })
  await expect(approveAll).toBeEnabled({ timeout: 30_000 })
  await expect(manual.getByRole('list', { name: 'Verification set to approve' }).getByRole('listitem')).toHaveText(
    finalPass.map((execution, index) => `#${index + 1} ${RECIPES[index]} · execution #${execution.ExecutionNumber}`),
  )
  await expect(manual).toContainText('The explicit local commit requires this complete approval; approving commits nothing.')
  expect(data.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toEqual([])
  expect(data.localCommitOperationCount()).toBe(0)
  const approved = page.waitForResponse(
    (response) => response.request().method() === 'POST' && CHECKPOINT_REVIEW_PATH.test(new URL(response.url()).pathname),
    { timeout: 30_000 },
  )
  await approveAll.click()
  expect((await approved).status()).toBe(201)
  await expect(manual).toContainText('Recorded. The Human approval covers these runs.', { timeout: 30_000 })
  await expect(manual).toContainText(new RegExp(`Checkpoint #4 · verification (#${finalPass[0].ExecutionNumber}, #${finalPass[1].ExecutionNumber}|#${finalPass[1].ExecutionNumber}, #${finalPass[0].ExecutionNumber}) · Human`))
  await expect(approveAll).toBeDisabled()

  // The approval is one distinct Human fact over the final checkpoint and exactly the final pass; it commits nothing and creates no
  // Agent attempt, message, verification, provider invocation or operation, and it leaves the Agent review fact untouched.
  const humanReviews = data.checkpointReviews().filter((review) => review.ActorKind === 'Human')
  expect(humanReviews).toHaveLength(1)
  const humanReview = humanReviews[0]
  expect(humanReview).toMatchObject({ Decision: 'Approved', CheckpointNumber: 4, CheckpointFingerprintSha256: finalCheckpoint.FingerprintSha256 })
  expect(sameId(humanReview.GitCheckpointId, finalCheckpoint.Id)).toBe(true)
  expect(sameId(humanReview.Id, agentFinalReview.Id)).toBe(false)
  expect(data.checkpointReviews().map((review) => review.Id.toLowerCase())).toEqual([...agentReviews.map((review) => review.Id.toLowerCase()), humanReview.Id.toLowerCase()])
  expect(membersOfReview(humanReview.Id)).toEqual(expectedMembers)
  expect(membersOfReview(agentFinalReview.Id)).toEqual(expectedMembers)
  for (const member of data.checkpointReviewEvidence().filter((row) => sameId(row.CheckpointReviewId, humanReview.Id))) {
    expect(member).toMatchObject({
      VerificationExecutionCheckpointFingerprintSha256: finalCheckpoint.FingerprintSha256,
      VerificationExecutionStatus: 'Passed',
      VerificationExecutionExitCode: 0,
    })
  }
  expect({ ...data.projectTotals(), events: 0 }).toEqual({ ...cycleBefore.totals, reviews: cycleBefore.totals.reviews + 1, events: 0 })
  expect(data.messages()).toHaveLength(cycleBefore.messages)
  expect(readInvocations(mark)).toHaveLength(cycleBefore.invocations)
  expect(data.localCommitOperationCount()).toBe(0)
  expect(git('rev-parse', 'HEAD')).toBe(repository.baselineCommit)
  expect(posts.slice(postsBeforeChain).filter((path) => path.startsWith('/api/'))).toHaveLength(1)

  // 10b. The separate explicit local commit. After the approval the journey clicks the page's read-only Refresh evidence control and then
  // waits for the commit form; whether the form would appear without that click is not tested here.
  await page.getByRole('button', { name: 'Refresh evidence' }).click()
  const commitButton = localCommit.getByRole('button', { name: 'Commit locally' })
  await expect(commitButton).toBeVisible({ timeout: 30_000 })
  expect(data.localCommitOperationCount()).toBe(0)
  await expect(commitButton).toBeDisabled() // no message yet
  await localCommit.getByLabel('Commit message').fill('Deliver the verified local change')
  await expect(commitButton).toBeEnabled()
  // The one tool-owned workspace branch, observed just before the commit request: its complete ref name, the baseline commit it points
  // at, and the branch the worktree actually has checked out. `sourceBefore` (taken after workspace preparation) must agree.
  const isOwnedBranch = (line: string) => line.includes('refs/heads/devalcopilot/workspace/')
  const refsOf = (text: string) => text.split('\n').filter((line) => line.length > 0)
  const ownedRefsBefore = refsOf(repositorySnapshot(repository.path).branches).filter(isOwnedBranch)
  expect(ownedRefsBefore).toHaveLength(1)
  expect(refsOf(sourceBefore.branches).filter(isOwnedBranch)).toEqual(ownedRefsBefore)
  const [ownedRefName, ownedRefCommitBefore] = ownedRefsBefore[0].split(' ')
  expect(ownedRefName).toMatch(/^refs\/heads\/devalcopilot\/workspace\/[^ ]+$/)
  expect(ownedRefCommitBefore).toBe(repository.baselineCommit)
  expect(git('symbolic-ref', 'HEAD')).toBe(ownedRefName)
  const committed = page.waitForResponse(
    (response) => response.request().method() === 'POST' && LOCAL_COMMIT_PATH.test(new URL(response.url()).pathname),
    { timeout: 30_000 },
  )
  const receiptWire = page.waitForResponse(
    (response) => response.request().method() === 'GET' && RECEIPT_PATH.test(new URL(response.url()).pathname),
    { timeout: 90_000 },
  )
  await commitButton.click()
  expect((await committed).status()).toBe(200)
  await expect(localCommit).toContainText('Completed. Local commit only — not pushed.', { timeout: 60_000 })

  // Exactly the two explicit requests left the browser for the chain, in order: the one approval, then the one commit.
  const chainPosts = posts.slice(postsBeforeChain).filter((path) => path.startsWith('/api/'))
  expect(chainPosts).toHaveLength(2)
  expect(CHECKPOINT_REVIEW_PATH.test(chainPosts[0])).toBe(true)
  expect(LOCAL_COMMIT_PATH.test(chainPosts[1])).toBe(true)

  // The recorded operation binds the cycle's own final facts: the final checkpoint, the cycle's last Agent review attempt and its
  // approval, the report that review judged, the one Human approval, and the ordered final verification executions.
  const operation = data.localCommitOperation()
  expect(operation).not.toBeNull()
  expect(data.localCommitOperationCount()).toBe(1)
  expect(operation).toMatchObject({ Status: 'Completed', CheckpointNumber: 4, CheckpointFingerprintSha256: finalCheckpoint.FingerprintSha256 })
  expect(sameId(operation!.GitCheckpointId, finalCheckpoint.Id)).toBe(true)
  expect(sameId(operation!.CodeReviewAttemptId, reviewTwo.Id)).toBe(true)
  expect(sameId(operation!.CodeReviewAttemptId, sequence.FinalReviewAttemptId)).toBe(true)
  expect(sameId(operation!.CodeReviewApprovalMessageId, approvals[0].Id)).toBe(true)
  expect(sameId(operation!.ExecutionReportMessageId, reportTwo.Id)).toBe(true)
  expect(sameId(operation!.ExecutionReportMessageId, sequence.FinalReportMessageId)).toBe(true)
  expect(sameId(operation!.HumanCheckpointReviewId, humanReview.Id)).toBe(true)
  expect(sameId(operation!.HumanCheckpointReviewId, agentFinalReview.Id)).toBe(false)
  expect(data.localCommitVerificationMembers().map((member) => [member.Sequence, member.CommandId.toLowerCase(), member.SubjectId.toLowerCase()])).toEqual([
    [0, commandIds[0], finalSteps[0].OperationId!.toLowerCase()],
    [1, commandIds[1], finalSteps[1].OperationId!.toLowerCase()],
  ])

  // The commit: one new commit on the worktree's branch, parented on the baseline, carrying the operation marker, changing only the
  // candidate file, in a worktree with no remote. Signing and hooks are not examined by this journey.
  const deliveredCommit = git('rev-parse', 'HEAD')
  expect(operation!.CommitSha.toLowerCase()).toBe(deliveredCommit)
  expect(deliveredCommit).not.toBe(repository.baselineCommit)
  expect(git('rev-list', '--count', 'HEAD')).toBe('2')
  expect(git('rev-parse', 'HEAD^')).toBe(repository.baselineCommit)
  expect(git('log', '-1', '--format=%B')).toContain('DevalCopilot-Operation:')
  expect(git('remote')).toBe('')
  expect(readFileSync(join(worktree, 'src', 'Feature.cs'), 'utf8')).toContain('return left + right;')
  expect(git('diff', '--name-only', 'HEAD^', 'HEAD')).toBe('src/Feature.cs')

  // The source repository's snapshot (HEAD, status, candidate bytes and refs) equals the earlier one, except the intended movement of the
  // one tool-owned branch. `sourceBefore` was taken after workspace preparation, so all non-owned refs are compared with it, and the
  // owned ref is bound to its exact name: the same ref, at the baseline before and at the delivered commit after, still the worktree's
  // checked-out branch.
  const sourceAfterCommit = repositorySnapshot(repository.path)
  expect(sourceAfterCommit.head).toBe(sourceBefore.head)
  expect(sourceAfterCommit.status).toBe(sourceBefore.status)
  expect(sourceAfterCommit.candidate).toBe(sourceBefore.candidate)
  const deliveredRefs = refsOf(sourceAfterCommit.branches)
  expect(deliveredRefs.filter((line) => !isOwnedBranch(line))).toEqual(refsOf(sourceBefore.branches).filter((line) => !isOwnedBranch(line)))
  expect(deliveredRefs.filter(isOwnedBranch)).toEqual([`${ownedRefName} ${deliveredCommit}`])
  expect(deliveredRefs).toHaveLength(refsOf(sourceBefore.branches).length)
  expect(git('symbolic-ref', 'HEAD')).toBe(ownedRefName)

  // No additional Agent or verification invocation, attempt, message, execution or checkpoint, and no cycle fact was rewritten.
  expect(data.attempts()).toEqual(cycleBefore.attempts)
  expect(data.executions()).toEqual(cycleBefore.executions)
  expect(data.messages()).toHaveLength(cycleBefore.messages)
  expect(readInvocations(mark)).toHaveLength(cycleBefore.invocations)
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(14)
  expect(readInvocations(mark).filter((entry) => entry.role === 'gh')).toEqual([])
  // `data.checkpoints()` returns every checkpoint row of the workspace, so this also pins that the commit added none.
  expect(data.checkpoints()).toEqual(checkpoints)
  const totalsAfter = data.projectTotals()
  expect({ ...totalsAfter, events: 0 }).toEqual({
    ...cycleBefore.totals,
    reviews: cycleBefore.totals.reviews + 1,
    operations: cycleBefore.totals.operations + 1,
    events: 0,
  })
  expect(sequences()).toEqual(cycleBefore.sequences)
  expect(steps(sequence.Id)).toEqual(cycleBefore.steps)
  expect(localEventTypes()).toEqual(cycleBefore.events)
  expect(data.gitHubObservationTargets()).toEqual([])
  expect(data.gitHubBranchPublicationApprovals()).toEqual([])
  expect(posts.slice(postsBeforeChain).filter((path) => /github|publication/i.test(path))).toEqual([])
  expect(gets.slice(getsBeforeChain).filter((path) => /github/i.test(path))).toEqual([])

  // The rendered receipt and the wire receipt: the commit, the final checkpoint, the cycle's last Agent review (distinct from the Human
  // approval) and the final pass's two members in order; earlier failed or superseded executions are not part of it.
  const receiptResponse = await receiptWire
  expect(receiptResponse.status()).toBe(200)
  const wire = await receiptResponse.json()
  expect(wire.state).toBe('Available')
  expect(wire.receipt).toMatchObject({
    version: 1,
    objective: OBJECTIVE,
    commitSha: deliveredCommit,
    parentCommitSha: repository.baselineCommit,
    treeSha: git('rev-parse', 'HEAD^{tree}'),
    branchName: ownedRefName.replace(/^refs\/heads\//, ''),
    checkpoint: { number: 4, fingerprintSha256: finalCheckpoint.FingerprintSha256 },
    humanReview: { decision: 'Approved' },
    codeReview: { attemptNumber: reviewTwo.AttemptNumber },
  })
  expect(wire.receipt.operationId.toLowerCase()).toBe(operation!.Id.toLowerCase())
  expect(wire.receipt.codeReview.attemptId.toLowerCase()).toBe(reviewTwo.Id.toLowerCase())
  expect(wire.receipt.codeReview.approvalMessageId.toLowerCase()).toBe(approvals[0].Id.toLowerCase())
  expect(wire.receipt.executionReportMessageId.toLowerCase()).toBe(reportTwo.Id.toLowerCase())
  expect(wire.receipt.humanReview.reviewId.toLowerCase()).toBe(humanReview.Id.toLowerCase())
  expect(
    wire.receipt.verification.map((member: Record<string, unknown>) => [
      member.order, String(member.commandId).toLowerCase(), String(member.executionId).toLowerCase(), member.executionNumber, member.commandName, member.status, member.exitCode,
    ]),
  ).toEqual(finalPass.map((execution, index) => [index, commandIds[index], execution.Id.toLowerCase(), execution.ExecutionNumber, RECIPES[index], 'Passed', 0]))
  const wireText = JSON.stringify(wire)
  for (const hidden of [worktree, repository.path, 'dotnet', 'AGENTS.md']) {
    expect(wireText).not.toContain(hidden)
  }
  const receiptRegion = page.getByRole('region', { name: 'Local delivery receipt' })
  await expect(receiptRegion).toContainText(deliveredCommit, { timeout: 30_000 })
  await expect(receiptRegion).toContainText('Nothing was pushed')
  await expect(receiptRegion.getByRole('listitem')).toHaveCount(2)
  await expect(receiptRegion).toContainText(`${humanReview.Id.toLowerCase()} — Approved`)
  await expect(receiptRegion).toContainText(`attempt #${reviewTwo.AttemptNumber} (${reviewTwo.Id.toLowerCase()})`)
  for (const execution of executions.slice(0, 4)) {
    await expect(receiptRegion).not.toContainText(execution.Id.toLowerCase())
  }

  // 10c. The durable result read back after a page reload (not a host restart or recovery proof): the same commit and receipt, nothing
  // re-requested, and the earlier cycle facts still the ones the chain bound.
  const postsBeforeFinalReload = posts.length
  await page.reload()
  await selectProject(page, PROJECT)
  const reloadedCommit = page.getByRole('region', { name: 'Local commit', exact: true })
  await expect(reloadedCommit).toContainText('Completed. Local commit only — not pushed.', { timeout: 30_000 })
  await expect(reloadedCommit).toContainText(deliveredCommit)
  await expect(reloadedCommit.getByRole('button', { name: 'Commit locally' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Local delivery receipt' })).toContainText(deliveredCommit, { timeout: 30_000 })
  expect(posts.slice(postsBeforeFinalReload).filter((path) => path.startsWith('/api/'))).toEqual([])
  expect(data.localCommitOperationCount()).toBe(1)
  expect(data.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toHaveLength(1)
  expect(git('rev-parse', 'HEAD')).toBe(deliveredCommit)
  expect(git('remote')).toBe('')
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(14)
  expect(data.attempts()).toHaveLength(8)
  expect(data.executions()).toHaveLength(6)
  expect(data.gitHubObservationTargets()).toEqual([])
})
