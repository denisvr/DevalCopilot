import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, journeyRoot, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the explicitly QUEUED local execution cycle waiting behind a held owner (ADR-0062), through the rendered
// controls of the production frontend over the real generated client, MVC authentication, mediator, SQLite, the planning and local
// execution coordinators and ALL supervisors. Only the provider and verification executables are deterministic owned doubles; the database
// and the doubles' invocation log are only READ.
//
// The recorded-execution limit is set to ONE through its rendered control. A second project's manual verification, started through its
// rendered Run control, becomes the held owner: it consumes the one global unit, and the verification double waits on its OWNED gate (a plain
// file in the owned fixture directory) while the gate exists. The queued project then plans for real, is queued through the rendered
// queue confirmation, and WAITS: the host records it as WaitingForAdmission with its recorded reason while the double sees zero invocations
// of the queued cycle. Only removing the owned gate releases the owner; the host then reserves, claims and advances the cycle to
// "Awaiting Human approval" with no per-step browser request, and a reload reads the recorded state. Nothing is approved, committed or
// published, no ownership is seeded by SQL and no production hook exists.
//
// The provider double accepts the planner root only for the repository named like the existing local execution journey's, and the held
// owner uses a different repository name, so each journey runs in its own Playwright invocation and its own owned root.

const OWNER_PROJECT = 'Queue contention owner fixture'
const PROJECT = 'Local execution queue journey fixture'
const OBJECTIVE = 'Implement the ledger total through one queued local cycle behind a held owner'
const RECIPES = ['Candidate total check', 'Candidate lint check']
const QUEUE_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence\/queue$/
const ARM_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence$/
const ARM_PLANNING = /\/api\/runs\/[^/]+\/planning-sequence$/
const VERIFICATION_CLAIM = /\/api\/projects\/[^/]+\/verification-commands\/[^/]+\/executions$/
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

const data = new JourneyData(PROJECT)
const ownerData = new JourneyData(OWNER_PROJECT)

interface SequenceRow {
  Id: string
  State: string
  ReasonCode: string | null
  AdmissionMode: string
  CoordinationNumber: number
  EndedAtUtc: string | null
}
interface StepRow {
  Id: string
  Ordinal: number
  Kind: string
  State: string
  ReservedOperationId: string
  OperationId: string | null
}
interface AdmissionRow {
  Id: string
  AdmissionNumber: number
  Ordinal: number
  Kind: string
  State: string
  StepId: string | null
  WaitingReason: string
}

function sequences(): SequenceRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Id, State, ReasonCode, AdmissionMode, CoordinationNumber, EndedAtUtc from local_execution_sequences where RunId = ?')
        .all(data.runId()) as unknown as SequenceRow[],
  )
}
function steps(sequenceId: string): StepRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Id, Ordinal, Kind, State, ReservedOperationId, OperationId from local_execution_sequence_steps where SequenceId = ? order by Ordinal')
        .all(sequenceId) as unknown as StepRow[],
  )
}
function admissions(sequenceId: string): AdmissionRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Id, AdmissionNumber, Ordinal, Kind, State, StepId, WaitingReason from local_execution_admissions where SequenceId = ? order by Ordinal')
        .all(sequenceId) as unknown as AdmissionRow[],
  )
}
function localEventTypes(): string[] {
  return data.eventTypesOf(data.runId()).filter((type) => type.startsWith('local_execution_sequence.'))
}

test('the queued local execution cycle journey: waiting behind a held owner at limit one, released by the owned gate, then Awaiting Human approval', async ({ page }) => {
  test.setTimeout(345_000)
  const repository = createJourneyRepository('local-execution-source')
  const ownerRepository = createJourneyRepository('queue-owner-source')
  const gate = join(journeyRoot().root, 'fixture', 'verify.hold')
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

  // 1. The global recorded-execution limit is ONE, through its rendered control.
  const limit = page.getByRole('region', { name: 'Recorded-execution limit' })
  await limit.getByLabel('One', { exact: true }).check()
  await limit.getByRole('button', { name: 'Apply limit' }).click()
  await expect(limit).toContainText('Current limit: one', { timeout: 30_000 })

  // 2. The held owner: another project's manual verification, started through its rendered Run control, held by the owned gate.
  await registerProject(page, OWNER_PROJECT, ownerRepository.path)
  await selectProject(page, OWNER_PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByLabel('Verification command name').fill('Owner hold check')
  await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
  await page.getByLabel('Verification arguments').fill('check')
  await page.getByRole('button', { name: 'Add command' }).click()
  await expect(verification).toContainText('#1 Owner hold check')
  mkdirSync(join(journeyRoot().root, 'fixture'), { recursive: true })
  writeFileSync(gate, 'hold')
  try {
    const claim = page.waitForResponse((response) => response.request().method() === 'POST' && VERIFICATION_CLAIM.test(new URL(response.url()).pathname))
    await verification.getByRole('button', { name: 'Run', exact: true }).click()
    expect((await claim).status()).toBe(202)
    await expect.poll(() => ownerData.executions().map((execution) => execution.Status), { timeout: 60_000 }).toEqual(['Running'])
    const markHeld = markInvocations()

    // 3. The queued project: identity, workspace, checkpoint, two recipes, a manual run, REAL planning (read-only, not a mutating owner).
    await registerProject(page, PROJECT, repository.path)
    await selectProject(page, PROJECT)
    await page.getByRole('button', { name: 'Recheck identity' }).click()
    await page.getByRole('button', { name: 'Prepare workspace' }).click()
    await page.getByRole('button', { name: 'Capture checkpoint' }).click()
    await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
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
    expect(sequences()).toEqual([])
    await expect(planning).toContainText('No planning sequence is armed', { timeout: 30_000 })
    await planning.getByRole('button', { name: 'Arm planning sequence' }).click()
    await expect(planning.getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute('data-state', 'ReadyForImplementation', {
      timeout: 120_000,
    })
    expect(sequences()).toEqual([])
    const markQueued = markInvocations()

    // 4. The queue offer is shown with its own confirmation. Opening and reading the queue confirmation invokes and records nothing.
    await expect(local).toContainText('No local execution cycle is authorized for this run', { timeout: 30_000 })
    // At the limit the Immediate offer is refused by the host, while the queue offer stays available: waiting is exactly what it is for.
    await expect(local).toContainText('Arming is not available: The recorded global mutating capacity is at its limit', { timeout: 30_000 })
    await expect(local.getByRole('button', { name: 'Authorize local execution cycle' })).toHaveCount(0)
    await expect(local.getByRole('button', { name: 'Queue local execution cycle' })).toBeEnabled({ timeout: 30_000 })
    const attemptsBeforeQueue = data.attempts().length
    await local.getByRole('button', { name: 'Queue local execution cycle' }).click()
    const confirmation = local.getByRole('group', { name: 'Confirm queued local execution cycle' })
    await expect(confirmation).toBeVisible({ timeout: 30_000 })
    await expect(confirmation).toContainText('CAN MUTATE the workspace')
    await expect(local.getByRole('group', { name: 'Confirm local execution cycle' })).toHaveCount(0)
    expect(data.attempts()).toHaveLength(attemptsBeforeQueue)
    expect(sequences()).toEqual([])

    const postsBeforeQueue = posts.length
    const queued = page.waitForResponse((response) => response.request().method() === 'POST' && QUEUE_LOCAL.test(new URL(response.url()).pathname), {
      timeout: 30_000,
    })
    await confirmation.getByRole('button', { name: 'Confirm and queue' }).click()
    expect((await queued).status()).toBeLessThan(300)

    // 5. WAITING behind the held owner: the recorded state, its recorded reason, no operation, and zero invocations of the queued cycle.
    const state = local.getByRole('status', { name: 'Local execution sequence state' })
    await expect(state).toHaveAttribute('data-state', 'WaitingForAdmission', { timeout: 30_000 })
    await expect(local).toContainText('Queued: waiting for admission')
    await expect(local).toContainText('Waiting for global mutating capacity', { timeout: 30_000 })
    await expect(page.getByText(FENCE_NOTE).first()).toBeVisible({ timeout: 30_000 })
    await expect(local.getByRole('button', { name: 'Queue local execution cycle' })).toHaveCount(0)
    await expect(local.getByRole('button', { name: 'Stop after current operation' })).toBeVisible()
    // The owner stays held for a real interval, during which the cycle never leaves the queue and nothing is invoked for it.
    await page.waitForTimeout(6_000)
    const [waiting] = sequences()
    expect(sequences()).toHaveLength(1)
    expect(waiting.State).toBe('WaitingForAdmission')
    expect(waiting.AdmissionMode).toBe('WaitForAdmission')
    expect(waiting.EndedAtUtc).toBeNull()
    expect(steps(waiting.Id)).toEqual([])
    expect(admissions(waiting.Id)).toHaveLength(1)
    expect(admissions(waiting.Id)[0].State).toBe('Waiting')
    expect(admissions(waiting.Id)[0].WaitingReason).toBe('WaitingForCapacity')
    expect(data.attempts()).toHaveLength(attemptsBeforeQueue)
    expect(data.executions()).toEqual([])
    expect(readInvocations(markQueued).filter((entry) => entry.kind !== 'probe')).toEqual([])
    expect(readInvocations(markHeld).filter((entry) => entry.kind !== 'probe' && entry.role === 'verify')).toEqual([])
    expect(ownerData.executions().map((execution) => execution.Status)).toEqual(['Running'])

    // 5b. LOCAL-QUEUE-INSPECT-01: the recorded admission ledger of the WAITING cycle, from the current run. The inspector and its ledger
    // are collapsed and read nothing until the owner opens each; the one recorded admission is Waiting with its recorded reason and has
    // no step, so there is no operation to open. Every request here is an explicit GET and nothing is written.
    const admissionsPath = `/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection/admissions`
    const ledgerReads = (start: number) => gets.slice(start).filter((path) => path === admissionsPath)
    const getsBeforeWaitingLedger = gets.length
    const postsBeforeWaitingLedger = posts.length
    const waitingInspector = page.getByRole('region', { name: 'Inspect local execution' })
    await expect(waitingInspector.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
    await waitingInspector.getByRole('button', { name: 'Inspect local execution' }).click()
    await expect(waitingInspector).toContainText('Recorded state', { timeout: 30_000 })
    const waitingLedger = waitingInspector.getByRole('region', { name: 'Inspect queue admissions' })
    await expect(waitingLedger.getByRole('button', { name: 'Inspect queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(ledgerReads(getsBeforeWaitingLedger)).toEqual([])
    await waitingLedger.getByRole('button', { name: 'Inspect queue admissions' }).click()
    await expect(waitingLedger).toContainText('Queued sequence', { timeout: 30_000 })
    expect(ledgerReads(getsBeforeWaitingLedger)).toEqual([admissionsPath])
    await expect(waitingLedger.getByRole('listitem')).toHaveCount(1)
    await expect(waitingLedger).toContainText('Admission 1 · recorded order')
    await expect(waitingLedger).toContainText('Waiting (recorded; owns no workspace, slot or step)')
    await expect(waitingLedger).toContainText('Last recorded admission check: Waiting for global mutating capacity')
    await expect(waitingLedger).toContainText('No step was created for this admission')
    await expect(waitingLedger.getByRole('button', { name: /Open linked step/ })).toHaveCount(0)
    expect(posts.slice(postsBeforeWaitingLedger)).toEqual([])
    expect(steps(waiting.Id)).toEqual([])
    expect(admissions(waiting.Id)).toHaveLength(1)
    expect(admissions(waiting.Id)[0].State).toBe('Waiting')
    expect(data.executions()).toEqual([])
    expect(readInvocations(markQueued).filter((entry) => entry.kind !== 'probe')).toEqual([])
    await waitingInspector.getByRole('button', { name: 'Hide local execution inspection' }).click()
    await expect(waitingInspector.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')

    // 5c. LOCAL-QUEUE-OVERVIEW-01: the global outstanding-admission region, discovered from ANOTHER selected project. The region is
    // collapsed and reads nothing until opened; the held owner's running verification is a recorded owner but not an admission, so the
    // one outstanding admission is this cycle's Waiting one, labelled with its registered project. Selecting it renders the existing
    // inspector for exactly its run, reads nothing by itself and leaves the selected project (and its "no run yet") untouched; the
    // inspector and its ledger then read only on their own explicit actions. Switching projects back neither re-reads nor resets it.
    // Every request here is an explicit GET and nothing is written.
    const overviewPath = '/api/local-execution-queue/inspection'
    const indexPath = `/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection`
    const overviewReads = (start: number) => gets.slice(start).filter((path) => path === overviewPath)
    const getsBeforeOverview = gets.length
    const postsBeforeOverview = posts.length
    const attemptsBeforeOverview = data.attempts().length
    const ownerExecutionsBeforeOverview = JSON.stringify(ownerData.executions())
    const admissionRowsBeforeOverview = JSON.stringify(admissions(waiting.Id))
    const waitingNumber = admissions(waiting.Id)[0].AdmissionNumber

    await selectProject(page, OWNER_PROJECT)
    await expect(page.getByText(`${OWNER_PROJECT} has no run yet.`)).toBeVisible({ timeout: 30_000 })
    const overview = page.getByRole('region', { name: 'Inspect outstanding queue admissions' })
    const openOverview = overview.getByRole('button', { name: 'Inspect outstanding queue admissions' })
    await expect(openOverview).toHaveAttribute('aria-expanded', 'false')
    expect(overviewReads(getsBeforeOverview)).toEqual([])
    await openOverview.click()
    await expect(overview).toContainText('Last recorded admission check: Waiting for global mutating capacity', { timeout: 30_000 })
    expect(overviewReads(getsBeforeOverview)).toEqual([overviewPath])
    await expect(overview.getByRole('listitem')).toHaveCount(1)
    await expect(overview).toContainText(
      `Project ${PROJECT} · run ${runId.toLowerCase()} · recorded order ${waitingNumber} · Waiting (recorded; owns no workspace, slot or step)`,
    )
    await expect(overview).toContainText('Recorded sequence state: Queued: waiting for admission')

    await overview.getByRole('button', { name: new RegExp(`^Select recorded cycle: project ${PROJECT}, recorded order ${waitingNumber}$`) }).click()
    const selectedInspector = overview.getByRole('region', { name: 'Inspect local execution' })
    await expect(overview).toContainText(`Selected recorded cycle: project ${PROJECT} · run ${runId.toLowerCase()}`)
    await expect(selectedInspector.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
    expect(gets.slice(getsBeforeOverview).filter((path) => path.includes('/local-execution-sequence/inspection'))).toEqual([])
    await expect(page.getByText(`${OWNER_PROJECT} has no run yet.`)).toBeVisible()
    await expect(page.getByRole('button', { name: new RegExp(`^${OWNER_PROJECT}`) })).toHaveAttribute('data-selected', 'true')

    await selectedInspector.getByRole('button', { name: 'Inspect local execution' }).click()
    await expect(selectedInspector).toContainText('Recorded state', { timeout: 30_000 })
    expect(gets.slice(getsBeforeOverview).filter((path) => path.includes('/local-execution-sequence/inspection'))).toEqual([indexPath])
    const selectedLedger = selectedInspector.getByRole('region', { name: 'Inspect queue admissions' })
    await selectedLedger.getByRole('button', { name: 'Inspect queue admissions' }).click()
    await expect(selectedLedger).toContainText('Queued sequence', { timeout: 30_000 })
    expect(gets.slice(getsBeforeOverview).filter((path) => path.includes('/local-execution-sequence/inspection'))).toEqual([indexPath, admissionsPath])
    await expect(selectedLedger).toContainText('Waiting (recorded; owns no workspace, slot or step)')
    await expect(selectedLedger.getByRole('button', { name: /Open linked step/ })).toHaveCount(0)

    await selectProject(page, PROJECT)
    await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
    await expect(overview).toContainText(`Selected recorded cycle: project ${PROJECT}`)
    await expect(overview.getByRole('button', { name: 'Hide outstanding queue admissions' })).toHaveAttribute('aria-expanded', 'true')
    expect(overviewReads(getsBeforeOverview)).toEqual([overviewPath])
    expect(posts.slice(postsBeforeOverview).filter((path) => path.startsWith('/api/'))).toEqual([])
    expect(JSON.stringify(admissions(waiting.Id))).toBe(admissionRowsBeforeOverview)
    expect(steps(waiting.Id)).toEqual([])
    expect(data.attempts()).toHaveLength(attemptsBeforeOverview)
    expect(data.executions()).toEqual([])
    expect(JSON.stringify(ownerData.executions())).toBe(ownerExecutionsBeforeOverview)
    expect(readInvocations(markQueued).filter((entry) => entry.kind !== 'probe')).toEqual([])

    // 6. Release ONLY the owned gate: the owner finishes by itself, and the host alone reserves, claims and advances the cycle.
    rmSync(gate, { force: true })
    await expect.poll(() => ownerData.executions().map((execution) => execution.Status), { timeout: 60_000 }).toEqual(['Failed'])
    await expect(state).toHaveAttribute('data-state', /^(PreparingStep|AwaitingStepResult|ReadyForHumanApproval)$/, { timeout: 120_000 })
    await expect(state).toHaveAttribute('data-state', 'ReadyForHumanApproval', { timeout: 280_000 })
    await expect(local).toContainText('Awaiting Human approval')
    await expect(local.getByRole('button', { name: 'Stop after current operation' })).toHaveCount(0)

    // Exactly one request left the browser since queueing began: the queue POST itself (no arm, step, verification or review request).
    expect(posts.slice(postsBeforeQueue).filter((path) => path.startsWith('/api/'))).toHaveLength(1)
    expect(QUEUE_LOCAL.test(posts[postsBeforeQueue])).toBe(true)
    expect(posts.filter((path) => ARM_LOCAL.test(path))).toEqual([])
    expect(posts.filter((path) => ARM_PLANNING.test(path))).toHaveLength(1)
    expect(posts.filter((path) => /\/agent-attempts\//.test(path))).toEqual([])
    expect(posts.filter((path) => VERIFICATION_CLAIM.test(path))).toHaveLength(1) // the owner's rendered Run, before queueing
    expect(gets.filter((path) => path.endsWith('/local-execution-sequence/queue/availability')).length).toBeGreaterThan(0)

    // 7. Evidence from the host's own records: a queued sequence, its twelve ordinary steps and its twelve consumed admissions in order.
    const [sequence] = sequences()
    expect(sequences()).toHaveLength(1)
    expect(sequence.State).toBe('ReadyForHumanApproval')
    expect(sequence.ReasonCode).toBe('review_approved')
    expect(sequence.AdmissionMode).toBe('WaitForAdmission')
    expect(sequence.EndedAtUtc).not.toBeNull()
    const allSteps = steps(sequence.Id)
    expect(allSteps.map((step) => step.Kind)).toEqual(STEP_KINDS)
    expect(allSteps.every((step) => step.State === 'Concluded')).toBe(true)
    expect(allSteps.every((step) => step.OperationId !== null && sameId(step.OperationId, step.ReservedOperationId))).toBe(true)

    const recorded = admissions(sequence.Id)
    expect(recorded.map((admission) => admission.Kind)).toEqual(STEP_KINDS)
    expect(recorded.map((admission) => admission.Ordinal)).toEqual(STEP_KINDS.map((_, index) => index))
    expect(recorded.every((admission) => admission.State === 'Consumed')).toBe(true)
    const numbers = recorded.map((admission) => admission.AdmissionNumber)
    expect(numbers.every((value, index) => index === 0 || value > numbers[index - 1])).toBe(true)
    expect(new Set(numbers).size).toBe(12)
    expect(recorded.every((admission, index) => admission.StepId !== null && sameId(admission.StepId, allSteps[index].Id))).toBe(true)

    // The events: queued once, twelve claims, twelve conclusions, one end.
    const events = localEventTypes()
    expect(events.filter((type) => type === 'local_execution_sequence.armed')).toHaveLength(1)
    expect(events.filter((type) => type === 'local_execution_sequence.step_claimed')).toHaveLength(12)
    expect(events.filter((type) => type === 'local_execution_sequence.step_concluded')).toHaveLength(12)
    expect(events.filter((type) => type === 'local_execution_sequence.ended')).toHaveLength(1)

    // After the release the doubles saw exactly the owner's one verification and the cycle's twelve stage invocations; nothing else.
    const afterRelease = readInvocations(markQueued).filter((entry) => entry.kind !== 'probe')
    expect(afterRelease.filter((entry) => entry.role === 'verify')).toHaveLength(7) // the owner's one plus the cycle's six
    expect(afterRelease).toHaveLength(13) // the owner's one plus the cycle's twelve; planning ran before the mark
    expect(data.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toEqual([])
    expect(data.localCommitOperationCount()).toBe(0)
    expect(data.gitHubObservationTargets()).toEqual([])

    // 7b. LOCAL-QUEUE-OVERVIEW-01: the open overview is an explicit snapshot, so after the whole cycle progressed it STILL shows its earlier
    // Waiting row and its selected inspector, and it read nothing by itself (no polling, no refresh by the host's events or the project
    // selection). Only the explicit reload reads it again: the prior rows and the selection are discarded at once, and the answer no
    // longer lists the cycle, whose last admission is Consumed. Empty is not idle: the sentence says so, and the Consumed ledger is
    // unchanged and still available through the per-run ledger.
    await expect(overview).toContainText(`Selected recorded cycle: project ${PROJECT}`)
    await expect(overview).toContainText('Waiting (recorded; owns no workspace, slot or step)')
    expect(overviewReads(getsBeforeOverview)).toEqual([overviewPath])
    const getsBeforeOverviewReload = gets.length
    const postsBeforeOverviewReload = posts.length
    await overview.getByRole('button', { name: 'Reload outstanding queue admissions' }).click()
    await expect(overview).toContainText('No Waiting, Reserved or Unresolved admission is recorded', { timeout: 30_000 })
    expect(overviewReads(getsBeforeOverviewReload)).toEqual([overviewPath])
    await expect(overview).toContainText('This does not mean no cycle is active')
    await expect(overview.getByRole('listitem')).toHaveCount(0)
    await expect(overview.getByRole('region', { name: 'Inspect local execution' })).toHaveCount(0)
    await expect(overview).not.toContainText('Selected recorded cycle')
    await expect(overview).not.toContainText('Waiting (recorded; owns no workspace, slot or step)')
    expect(posts.slice(postsBeforeOverviewReload).filter((path) => path.startsWith('/api/'))).toEqual([])
    expect(admissions(sequence.Id).every((admission) => admission.State === 'Consumed')).toBe(true)
    await overview.getByRole('button', { name: 'Hide outstanding queue admissions' }).click()
    await expect(overview.getByRole('button', { name: 'Inspect outstanding queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(overviewReads(getsBeforeOverview)).toEqual([overviewPath, overviewPath])

    // 8. The fence is released at the end; a reload reads the durable result back and requests nothing.
    await expect(page.getByText(FENCE_NOTE)).toHaveCount(0, { timeout: 30_000 })
    const requestsBeforeReload = posts.length
    await page.reload()
    await selectProject(page, PROJECT)
    const reloaded = page.getByRole('region', { name: 'Local execution sequence', exact: true })
    await expect(reloaded.getByRole('status', { name: 'Local execution sequence state' })).toHaveAttribute('data-state', 'ReadyForHumanApproval', {
      timeout: 30_000,
    })
    await expect(reloaded.getByRole('button', { name: 'Queue local execution cycle' })).toHaveCount(0)
    await expect(reloaded.getByRole('button', { name: 'Authorize local execution cycle' })).toHaveCount(0)
    expect(posts.slice(requestsBeforeReload).filter((path) => path.startsWith('/api/'))).toEqual([])

    // 9. The recorded inspection discloses the recorded mode and reads only on explicit actions.
    const current = page.getByRole('region', { name: 'Inspect local execution' })
    await current.getByRole('button', { name: 'Inspect local execution' }).click()
    await expect(current).toContainText('Recorded state', { timeout: 30_000 })
    await expect(current).toContainText(/queued/i)
    await expect(current.getByRole('button', { name: /^Inspect step \d+$/ })).toHaveCount(12)

    // 10. LOCAL-QUEUE-INSPECT-01: the consumed ledger of the finished cycle, from the current run and again from Run history. Opening a
    // ledger reads only the ledger; a linked step opens only through its own explicit action, which reuses the existing selected-step
    // read; no request that is not a GET leaves the browser and nothing executes, claims or mutates.
    const inspectionPaths = (start: number) => gets.slice(start).filter((path) => path.includes('/local-execution-sequence/inspection'))
    const stepPath = (stepId: string) => `/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection/steps/${stepId.toLowerCase()}`
    const consumed = (inspector: typeof current) => inspector.getByRole('region', { name: 'Inspect queue admissions' })
    const postsBeforeLedgers = posts.length
    const attemptsBeforeLedgers = data.attempts().length
    const executionsBeforeLedgers = data.executions().length
    const admissionRowsBeforeLedgers = JSON.stringify(admissions(sequence.Id))

    const getsBeforeCurrentLedger = gets.length
    const currentLedger = consumed(current)
    await expect(currentLedger.getByRole('button', { name: 'Inspect queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(inspectionPaths(getsBeforeCurrentLedger)).toEqual([])
    await currentLedger.getByRole('button', { name: 'Inspect queue admissions' }).click()
    await expect(currentLedger).toContainText('Queued sequence', { timeout: 30_000 })
    expect(inspectionPaths(getsBeforeCurrentLedger)).toEqual([admissionsPath])
    await expect(currentLedger.getByRole('listitem')).toHaveCount(12)
    for (const [position, kind] of STEP_KINDS.entries()) {
      const row = currentLedger.getByRole('listitem').nth(position)
      await expect(row).toContainText(`Admission ${position + 1} · recorded order ${recorded[position].AdmissionNumber}`)
      await expect(row).toContainText('Consumed (its operation was claimed; this is not proof the operation succeeded)')
      await expect(row).toContainText(kind === 'Verification' ? 'Verification recipe' : /\(Claude Code\)|\(Codex\)|Correction after/)
    }

    await expect(currentLedger.getByRole('button', { name: /^Open linked step \d+ evidence from admission \d+$/ })).toHaveCount(12)
    await currentLedger.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' }).click()
    const currentDetail = current.getByRole('region', { name: 'Selected local execution step' })
    await expect(currentDetail).toContainText('Failed · process outcome Exited · exit code 1', { timeout: 30_000 })
    expect(inspectionPaths(getsBeforeCurrentLedger)).toEqual([admissionsPath, stepPath(allSteps[1].Id)])

    const getsBeforeHistoryLedger = gets.length
    const history = page.getByRole('region', { name: 'Run history' })
    await history.getByRole('button', { name: 'Show run history' }).click()
    await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
    const historical = history.getByRole('region', { name: 'Selected run' }).getByRole('region', { name: 'Inspect local execution' })
    await historical.getByRole('button', { name: 'Inspect local execution' }).click()
    await expect(historical).toContainText('Recorded state', { timeout: 30_000 })
    await expect(historical.getByRole('button', { name: /^Inspect step \d+$/ })).toHaveCount(12)
    const historicalLedger = consumed(historical)
    expect(inspectionPaths(getsBeforeHistoryLedger)).toEqual([`/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection`])
    await historicalLedger.getByRole('button', { name: 'Inspect queue admissions' }).click()
    await expect(historicalLedger).toContainText('Queued sequence', { timeout: 30_000 })
    await expect(historicalLedger.getByRole('listitem')).toHaveCount(12)
    expect(inspectionPaths(getsBeforeHistoryLedger)).toHaveLength(2)
    await historicalLedger.getByRole('button', { name: 'Open linked step 8 evidence from admission 8' }).click()
    await expect(
      historical.getByRole('region', { name: 'Selected local execution step' }).getByRole('region', { name: 'Selected Agent attempt evidence' }),
    ).toBeVisible({ timeout: 30_000 })
    expect(inspectionPaths(getsBeforeHistoryLedger)).toEqual([
      `/api/runs/${runId.toLowerCase()}/local-execution-sequence/inspection`,
      admissionsPath,
      stepPath(allSteps[7].Id),
    ])

    expect(posts.slice(postsBeforeLedgers)).toEqual([])
    expect(JSON.stringify(admissions(sequence.Id))).toBe(admissionRowsBeforeLedgers)
    expect(data.attempts()).toHaveLength(attemptsBeforeLedgers)
    expect(data.executions()).toHaveLength(executionsBeforeLedgers)

    expect(sequences()).toHaveLength(1)
    expect(admissions(sequence.Id)).toHaveLength(12)
    expect(data.attempts()).toHaveLength(8)
    expect(data.executions()).toHaveLength(6)
    expect(runId).toBe(data.runId())
  } finally {
    // Never leave the owned gate behind, even when an assertion above failed.
    rmSync(gate, { force: true })
  }
})
