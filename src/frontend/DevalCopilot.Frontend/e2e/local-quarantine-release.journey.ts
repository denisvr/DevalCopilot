import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, journeyRoot, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { PostInventory } from './journey/requestInventory'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the Human release of a terminal local operation's retained quarantine (ADR-0065), through the rendered
// controls of the production frontend over the real generated client, MVC authentication, mediator, SQLite, the planning and local
// execution coordinators and ALL supervisors. Only the provider and verification executables are deterministic owned doubles; the
// database and the doubles' invocation log are only READ.
//
// The recorded-execution limit is set to ONE through its rendered control. The first project's authorized cycle implements for real and
// its first verification is held by the owned gate (a plain file in the owned fixture directory) past its three-second recipe timeout:
// the host times it out and kills the invoked process, observes it terminated while it still owns it, records that physical fact in the
// same commit as the ordinary TimedOut result, and the cycle ends NeedsAttention. The terminal operation STILL holds its quarantine
// AFTER that evidence: the manual start is withheld and the one global unit stays consumed. A second project (a different repository,
// recognized by the doubles through its own conventions marker) then plans for real and is QUEUED through the rendered queue
// confirmation; it WAITS for global capacity while the doubles see zero invocations of it. Only the first project's rendered release
// control, after reading the original host's recorded termination evidence and one separate explicit confirmation, releases the
// quarantine: the failed result and the ended cycle are not rewritten, exactly one release and one Human event are appended, and the
// already queued cycle then reserves, claims and advances through its OWN gates to "Awaiting Human approval" with no browser request
// beyond the release POST. Nothing is approved, committed or published, no ownership is seeded by SQL and no production hook exists.

const PROJECT = 'Quarantine release journey fixture'
const QUEUED_PROJECT = 'Quarantine release queued fixture'
const OBJECTIVE = 'Implement the ledger total through one authorized local cycle that times out in verification'
const QUEUED_OBJECTIVE = 'Implement the ledger total through one queued local cycle behind a released quarantine'
const RECIPE = 'Candidate total check'
const QUEUED_RECIPES = ['Candidate total check', 'Candidate lint check']
const ARM_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence$/
const QUEUE_LOCAL = /\/api\/runs\/[^/]+\/local-execution-sequence\/queue$/
const RELEASE_PATH = /\/api\/runs\/[^/]+\/local-execution-sequence\/steps\/[^/]+\/ownership-release$/
const OWNER_NOTE = /is recorded as this workspace's execution owner/
const RELEASE_REGION = 'Release terminal operation quarantine'

const QUEUED_STEP_KINDS = [
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
const queuedData = new JourneyData(QUEUED_PROJECT)

interface SequenceRow {
  Id: string
  State: string
  ReasonCode: string | null
  AdmissionMode: string
  EndedAtUtc: string | null
}
interface StepRow {
  Id: string
  Ordinal: number
  Kind: string
  State: string
  OperationId: string | null
  PreparationReservedAtUtc: string
}
interface WitnessRow {
  Id: string
  StepId: string
  OperationFamily: string
  Kind: string
  HostLaunchId: string
}
interface ObservationRow {
  Id: string
  WitnessId: string
  ProcessOutcome: string
  SemanticExitCode: number | null
  PhysicalExitCode: number
}
interface ReleaseRow {
  Id: string
  WitnessId: string
  ObservationId: string
  StepId: string
  EventId: string
  ReleasedAtUtc: string
}
interface AdmissionRow {
  State: string
  WaitingReason: string
}

const sequencesOf = (runId: string): SequenceRow[] =>
  withJourneyDb(
    (db) =>
      db.prepare('select Id, State, ReasonCode, AdmissionMode, EndedAtUtc from local_execution_sequences where RunId = ?').all(runId) as unknown as SequenceRow[],
  )
const stepsOf = (sequenceId: string): StepRow[] =>
  withJourneyDb(
    (db) =>
      db
        .prepare('select Id, Ordinal, Kind, State, OperationId, PreparationReservedAtUtc from local_execution_sequence_steps where SequenceId = ? order by Ordinal')
        .all(sequenceId) as unknown as StepRow[],
  )
const witnesses = (runId: string): WitnessRow[] =>
  withJourneyDb(
    (db) =>
      db
        .prepare('select Id, StepId, OperationFamily, Kind, HostLaunchId from local_execution_dispatch_witnesses where RunId = ? order by Ordinal')
        .all(runId) as unknown as WitnessRow[],
  )
const observations = (runId: string): ObservationRow[] =>
  withJourneyDb(
    (db) =>
      db
        .prepare(
          'select o.Id, o.WitnessId, o.ProcessOutcome, o.SemanticExitCode, o.PhysicalExitCode from local_execution_termination_observations o join local_execution_dispatch_witnesses w on w.Id = o.WitnessId where w.RunId = ? order by w.Ordinal',
        )
        .all(runId) as unknown as ObservationRow[],
  )
const releases = (): ReleaseRow[] =>
  withJourneyDb(
    (db) =>
      db
        .prepare('select Id, WitnessId, ObservationId, StepId, EventId, ReleasedAtUtc from local_execution_ownership_releases')
        .all() as unknown as ReleaseRow[],
  )
const admissionsOf = (sequenceId: string): AdmissionRow[] =>
  withJourneyDb(
    (db) => db.prepare('select State, WaitingReason from local_execution_admissions where SequenceId = ? order by Ordinal').all(sequenceId) as unknown as AdmissionRow[],
  )
const localEventTypes = (source: JourneyData): string[] => source.eventTypesOf(source.runId()).filter((type) => type.startsWith('local_execution_sequence.'))

test('the quarantine release journey: a timed-out verification keeps its quarantine after physical evidence, one Human confirmation releases it, and a queued cycle then runs through its own gates', async ({ page }) => {
  test.setTimeout(590_000)
  const repository = createJourneyRepository('local-execution-source')
  const queuedRepository = createJourneyRepository('local-release-queued-source')
  const gate = join(journeyRoot().root, 'fixture', 'verify.hold')
  // Every POST the page issues. The run notification hub (LongPolling) posts at moments no business action controls; the inventory
  // keeps that traffic in its raw record but never counts it as, or lets it shift the index of, a business command.
  const posts = new PostInventory()
  const gets: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST') {
      posts.record(new URL(request.url()).pathname)
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
  const release = page.getByRole('region', { name: RELEASE_REGION })

  mkdirSync(join(journeyRoot().root, 'fixture'), { recursive: true })
  try {
    // 1. The global recorded-execution limit is ONE, through its rendered control.
    const limit = page.getByRole('region', { name: 'Recorded-execution limit' })
    await limit.getByLabel('One', { exact: true }).check()
    await limit.getByRole('button', { name: 'Apply limit' }).click()
    await expect(limit).toContainText('Current limit: one', { timeout: 30_000 })

    // 2. The first project: identity, workspace, checkpoint, ONE recipe with a three-second timeout, a manual run and REAL planning.
    await registerProject(page, PROJECT, repository.path)
    await selectProject(page, PROJECT)
    await page.getByRole('button', { name: 'Recheck identity' }).click()
    await page.getByRole('button', { name: 'Prepare workspace' }).click()
    await page.getByRole('button', { name: 'Capture checkpoint' }).click()
    await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
    await page.getByLabel('Verification command name').fill(RECIPE)
    await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
    await page.getByLabel('Verification arguments').fill('check')
    await page.getByLabel('Verification timeout seconds').fill('3')
    await page.getByRole('button', { name: 'Add command' }).click()
    await expect(verification).toContainText(`#1 ${RECIPE}`)
    await expect(verification).toContainText('3s timeout')
    await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
    await page.getByRole('button', { name: 'Record manual run' }).click()
    await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
    const runId = data.runId()
    await expect(planning).toContainText('No planning sequence is armed', { timeout: 30_000 })
    await planning.getByRole('button', { name: 'Arm planning sequence' }).click()
    await expect(planning.getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute('data-state', 'ReadyForImplementation', {
      timeout: 120_000,
    })

    // 3. The one authorization of the cycle, with the owned gate holding the verification past its timeout.
    writeFileSync(gate, 'hold')
    await expect(local).toContainText('No local execution cycle is authorized for this run', { timeout: 30_000 })
    await local.getByRole('button', { name: 'Authorize local execution cycle' }).click()
    const confirmation = local.getByRole('group', { name: 'Confirm local execution cycle' })
    await expect(confirmation).toBeVisible({ timeout: 30_000 })
    const armed = page.waitForResponse((response) => response.request().method() === 'POST' && ARM_LOCAL.test(new URL(response.url()).pathname), {
      timeout: 30_000,
    })
    await confirmation.getByRole('button', { name: 'Confirm and authorize' }).click()
    expect((await armed).status()).toBeLessThan(300)

    // 4. The host times the held verification out, kills the invoked process, observes it terminated and ends the cycle NeedsAttention.
    const state = local.getByRole('status', { name: 'Local execution sequence state' })
    await expect(state).toHaveAttribute('data-state', 'NeedsAttention', { timeout: 200_000 })
    rmSync(gate, { force: true })
    const [sequence] = sequencesOf(runId)
    expect(sequencesOf(runId)).toHaveLength(1)
    expect(sequence.ReasonCode).toBe('exit_evidence_missing')
    expect(sequence.EndedAtUtc).not.toBeNull()
    const cycleSteps = stepsOf(sequence.Id)
    expect(cycleSteps.map((step) => step.Kind)).toEqual(['Implementation', 'Verification'])
    expect(data.executions().map((execution) => [execution.Status, execution.ExitCode])).toEqual([['TimedOut', null]])
    const verificationStep = cycleSteps[1]

    // 5. The physical evidence exists, and it is NOT a release: the quarantine still holds, the manual start is withheld and no release
    // exists. Two dispatch witnesses (both original-host dispatches) and the observation of each terminated process are recorded.
    const recordedWitnesses = witnesses(runId)
    expect(recordedWitnesses.map((witness) => [witness.OperationFamily, witness.Kind])).toEqual([['Agent', 'Implementation'], ['Verification', 'Verification']])
    expect(new Set(recordedWitnesses.map((witness) => witness.HostLaunchId)).size).toBe(1)
    const recordedObservations = observations(runId)
    expect(recordedObservations.map((observation) => observation.ProcessOutcome)).toEqual(['Exited', 'TimedOut'])
    expect(recordedObservations[1].SemanticExitCode).toBeNull()
    expect(recordedObservations.every((observation, index) => sameId(observation.WitnessId, recordedWitnesses[index].Id))).toBe(true)
    expect(releases()).toEqual([])
    expect(localEventTypes(data)).not.toContain('local_execution_sequence.ownership_released')
    await expect(local.locator('tr[data-quarantined="true"]')).toHaveCount(1)
    await expect(local.locator('tr[data-quarantined="true"]')).toContainText('no exit evidence recorded')
    await expect(local.locator('tr[data-ownership-released="true"]')).toHaveCount(0)
    await expect(local).toContainText('stays owned and excluded')
    await expect(page.getByText(OWNER_NOTE).first()).toBeVisible({ timeout: 30_000 })
    await expect(verification.getByRole('button', { name: 'Run', exact: true }).first()).toBeDisabled({ timeout: 30_000 })

    // 6. The second project: its own repository, one queued cycle behind the quarantine. Planning is read-only and runs for real.
    await registerProject(page, QUEUED_PROJECT, queuedRepository.path)
    await selectProject(page, QUEUED_PROJECT)
    await page.getByRole('button', { name: 'Recheck identity' }).click()
    await page.getByRole('button', { name: 'Prepare workspace' }).click()
    await page.getByRole('button', { name: 'Capture checkpoint' }).click()
    await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
    for (const [index, name] of QUEUED_RECIPES.entries()) {
      await page.getByLabel('Verification command name').fill(name)
      await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
      await page.getByLabel('Verification arguments').fill('check')
      await page.getByRole('button', { name: 'Add command' }).click()
      await expect(verification).toContainText(`#${index + 1} ${name}`)
    }
    await page.getByRole('textbox', { name: 'Objective' }).fill(QUEUED_OBJECTIVE)
    await page.getByRole('button', { name: 'Record manual run' }).click()
    await expect(page.getByRole('heading', { name: QUEUED_OBJECTIVE })).toBeVisible({ timeout: 15_000 })
    const queuedRunId = queuedData.runId()
    await expect(planning).toContainText('No planning sequence is armed', { timeout: 30_000 })
    await planning.getByRole('button', { name: 'Arm planning sequence' }).click()
    await expect(planning.getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute('data-state', 'ReadyForImplementation', {
      timeout: 120_000,
    })
    const markQueued = markInvocations()

    // 7. At the limit the Immediate offer is refused and the queue offer stays available; queueing waits for capacity.
    await expect(local).toContainText('Arming is not available: The recorded global mutating capacity is at its limit', { timeout: 30_000 })
    await expect(local.getByRole('button', { name: 'Queue local execution cycle' })).toBeEnabled({ timeout: 30_000 })
    await local.getByRole('button', { name: 'Queue local execution cycle' }).click()
    const queueConfirmation = local.getByRole('group', { name: 'Confirm queued local execution cycle' })
    await expect(queueConfirmation).toBeVisible({ timeout: 30_000 })
    const queued = page.waitForResponse((response) => response.request().method() === 'POST' && QUEUE_LOCAL.test(new URL(response.url()).pathname), {
      timeout: 30_000,
    })
    await queueConfirmation.getByRole('button', { name: 'Confirm and queue' }).click()
    expect((await queued).status()).toBeLessThan(300)
    const queuedState = local.getByRole('status', { name: 'Local execution sequence state' })
    await expect(queuedState).toHaveAttribute('data-state', 'WaitingForAdmission', { timeout: 30_000 })
    await expect(local).toContainText('Waiting for global mutating capacity', { timeout: 30_000 })
    // The quarantine keeps the one global unit for a real interval: the queued cycle never leaves the queue and nothing is invoked for it.
    await page.waitForTimeout(6_000)
    const [waiting] = sequencesOf(queuedRunId)
    expect(waiting.State).toBe('WaitingForAdmission')
    expect(stepsOf(waiting.Id)).toEqual([])
    expect(admissionsOf(waiting.Id)).toEqual([{ State: 'Waiting', WaitingReason: 'WaitingForCapacity' }])
    expect(readInvocations(markQueued).filter((entry) => entry.kind !== 'probe')).toEqual([])
    expect(releases()).toEqual([])

    // 8. Back at the first project: the release control is collapsed and reads nothing until opened; opening it explains the limits.
    await selectProject(page, PROJECT)
    await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
    await expect(state).toHaveAttribute('data-state', 'NeedsAttention', { timeout: 30_000 })
    const releaseReads = (start: number) => gets.slice(start).filter((path) => path.endsWith('/ownership-release'))
    const getsBeforeOpen = gets.length
    const postsBeforeRelease = posts.mark()
    await expect(release.getByRole('button', { name: RELEASE_REGION })).toHaveAttribute('aria-expanded', 'false')
    expect(releaseReads(getsBeforeOpen)).toEqual([])
    await release.getByRole('button', { name: RELEASE_REGION }).click()
    await expect(release).toContainText('only when the original host recorded')
    await expect(release).toContainText('evidence about the invoked process only')
    await expect(release).toContainText('the failed result, the ended cycle, the workspace and its source gates')
    await expect(release).toContainText('already authorized work that was waiting, including a queued cycle')
    expect(releaseReads(getsBeforeOpen)).toEqual([])

    // 9. Choosing the terminal verification reads only that step; the evidence is the original host's recorded observation.
    const stepPath = `/api/runs/${runId.toLowerCase()}/local-execution-sequence/steps/${verificationStep.Id.toLowerCase()}/ownership-release`
    await release.getByRole('button', { name: /^Select step 2/ }).click()
    const evidence = page.getByRole('group', { name: 'Confirm ownership release' })
    await expect(evidence).toBeVisible({ timeout: 30_000 })
    expect(releaseReads(getsBeforeOpen)).toEqual([stepPath])
    await expect(evidence).toContainText('process outcome Timed out (the host stopped it)')
    await expect(evidence.getByRole('term').filter({ hasText: 'Recorded exit code of the result' })).toBeVisible()
    await expect(evidence).toContainText('none recorded')
    await expect(evidence.getByRole('term').filter({ hasText: 'Exit code the operating system reported at termination' })).toBeVisible()
    await expect(evidence).toContainText(String(recordedObservations[1].PhysicalExitCode))
    await expect(evidence).toContainText(recordedObservations[1].Id.toLowerCase())
    await expect(evidence).toContainText('it is not rewritten, and the ended cycle stays ended')
    expect(posts.businessSince(postsBeforeRelease)).toEqual([])
    expect(releases()).toEqual([])
    await expect(local.locator('tr[data-quarantined="true"]')).toHaveCount(1)

    // 10. The separate confirmation releases it: one POST, one release, one Human event, and nothing rewritten.
    const releasing = page.waitForResponse((response) => response.request().method() === 'POST' && RELEASE_PATH.test(new URL(response.url()).pathname), {
      timeout: 30_000,
    })
    await evidence.getByRole('button', { name: 'Confirm and release quarantine' }).click()
    expect((await releasing).status()).toBe(200)
    await expect(release).toContainText('Released. The retained quarantine of this operation was released by a Human confirmation.', { timeout: 30_000 })
    const [receipt] = releases()
    expect(releases()).toHaveLength(1)
    expect(sameId(receipt.StepId, verificationStep.Id)).toBe(true)
    expect(sameId(receipt.ObservationId, recordedObservations[1].Id)).toBe(true)
    expect(sameId(receipt.WitnessId, recordedWitnesses[1].Id)).toBe(true)
    expect(localEventTypes(data).filter((type) => type === 'local_execution_sequence.ownership_released')).toHaveLength(1)
    const releaseEvent = data.eventHeadersOf(runId).find((event) => event.EventType === 'local_execution_sequence.ownership_released')!
    expect(releaseEvent.ActorKind).toBe('Human')
    expect(sameId(releaseEvent.Id, receipt.EventId)).toBe(true)
    expect(posts.businessSince(postsBeforeRelease)).toEqual([stepPath])

    // The failed result and the ended cycle are exactly what they were; the status now shows the host's own release.
    const [after] = sequencesOf(runId)
    expect(after.State).toBe('NeedsAttention')
    expect(after.ReasonCode).toBe('exit_evidence_missing')
    expect(after.EndedAtUtc).toBe(sequence.EndedAtUtc)
    expect(data.executions().map((execution) => [execution.Status, execution.ExitCode])).toEqual([['TimedOut', null]])
    expect(stepsOf(sequence.Id).map((step) => step.State)).toEqual(['Concluded', 'Concluded'])
    await expect(local.locator('tr[data-ownership-released="true"]')).toHaveCount(1, { timeout: 30_000 })
    await expect(local.locator('tr[data-quarantined="true"]')).toHaveCount(0)
    await expect(local.locator('tr[data-ownership-released="true"]')).toContainText('a Human released its retained quarantine')
    await expect(state).toHaveAttribute('data-state', 'NeedsAttention')
    await expect(page.getByText(OWNER_NOTE)).toHaveCount(0, { timeout: 30_000 })
    await expect(verification.getByRole('button', { name: 'Run', exact: true }).first()).toBeEnabled({ timeout: 30_000 })

    // 11. The already queued, independent cycle uses its OWN gates afterwards: it was empty before the release, and its first step was
    // reserved only after it.
    await selectProject(page, QUEUED_PROJECT)
    await expect(page.getByRole('heading', { name: QUEUED_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
    await expect(queuedState).toHaveAttribute('data-state', 'ReadyForHumanApproval', { timeout: 380_000 })
    await expect(local).toContainText('Awaiting Human approval')
    const [finished] = sequencesOf(queuedRunId)
    expect(finished.State).toBe('ReadyForHumanApproval')
    expect(finished.AdmissionMode).toBe('WaitForAdmission')
    const queuedSteps = stepsOf(finished.Id)
    expect(queuedSteps.map((step) => step.Kind)).toEqual(QUEUED_STEP_KINDS)
    expect(queuedSteps.every((step) => step.State === 'Concluded')).toBe(true)
    expect(queuedSteps[0].PreparationReservedAtUtc > receipt.ReleasedAtUtc).toBe(true)
    expect(localEventTypes(queuedData).filter((type) => type === 'local_execution_sequence.ended')).toHaveLength(1)

    // 12. The release request was the only mutation of the first project since the release, and the queued cycle needed no browser request.
    expect(posts.businessSince(postsBeforeRelease)).toEqual([stepPath])
    expect(posts.business().filter((path) => ARM_LOCAL.test(path))).toHaveLength(1)
    expect(posts.business().filter((path) => QUEUE_LOCAL.test(path))).toHaveLength(1)
    expect(posts.business().filter((path) => RELEASE_PATH.test(path))).toHaveLength(1)
    expect(releases()).toHaveLength(1)
    expect(data.localCommitOperationCount()).toBe(0)
    expect(queuedData.localCommitOperationCount()).toBe(0)
    expect(data.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toEqual([])
    expect(queuedData.checkpointReviews().filter((review) => review.ActorKind === 'Human')).toEqual([])

    // 13. A reload reads the durable result back and requests nothing.
    const requestsBeforeReload = posts.mark()
    await page.reload()
    await selectProject(page, PROJECT)
    const reloaded = page.getByRole('region', { name: 'Local execution sequence', exact: true })
    await expect(reloaded.getByRole('status', { name: 'Local execution sequence state' })).toHaveAttribute('data-state', 'NeedsAttention', {
      timeout: 30_000,
    })
    await expect(reloaded.locator('tr[data-ownership-released="true"]')).toHaveCount(1)
    await expect(reloaded.locator('tr[data-quarantined="true"]')).toHaveCount(0)
    expect(posts.businessSince(requestsBeforeReload)).toEqual([])
  } finally {
    // Never leave the owned gate behind, even when an assertion above failed.
    rmSync(gate, { force: true })
  }
})
