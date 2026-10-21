import { execFileSync } from 'node:child_process'
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
// "Awaiting Human approval". Nothing is approved, committed or published. This is not provider reliability or proof of real recovery.

const PROJECT = 'Local execution journey fixture'
const OBJECTIVE = 'Implement the ledger total through one authorized local cycle'
const RECIPES = ['Candidate total check', 'Candidate lint check']
const ARM_PLANNING = /\/api\/runs\/[^/]+\/planning-sequence$/
const ARM_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence$/
const FENCE_NOTE = /local execution sequence holds automatic authority/

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

test('the local execution cycle journey: one authorization, both correction origins, three passes, and Awaiting Human approval', async ({ page }) => {
  test.setTimeout(345_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('local-execution-source')
  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST') {
      posts.push(new URL(request.url()).pathname)
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
})
