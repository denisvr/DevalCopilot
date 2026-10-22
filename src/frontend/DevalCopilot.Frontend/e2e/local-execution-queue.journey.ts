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
