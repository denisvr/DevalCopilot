import { createHash } from 'node:crypto'
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import {
  createJourneyRepository,
  markInvocations,
  readInvocations,
  readLaunchTargetVerdict,
  readSealedManifestText,
  repositorySnapshot,
} from './journey/journeyEnv'
import { PostInventory } from './journey/requestInventory'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the explicit abandonment of an inactive manual run (ADR-0031), through the rendered controls of the
// production frontend over the real generated client, MVC authentication, mediator, SQLite and ALL supervisors. Only the provider
// executables are deterministic owned doubles; nothing here is a raw-SQL substitute: the objective, the planning attempt, the
// abandonment and the second objective each originate from a rendered control. The database, the owned workspace, the sealed
// manifest bytes and the doubles' invocation log are only READ.
//
// It proves the assembled path: record an objective, finish one process-double planning attempt, abandon through the rendered form
// (the generated client sends only the reason), see the persisted reason and time after a reload, and then record a second objective
// in the same project through the normal intake. The abandonment and the reload cause no provider invocation, and the workspace,
// the source repository and the earlier sealed bytes are exactly as they were. It is not provider reliability, process cancellation
// or proof that any abandoned task is recoverable.

const PROJECT = 'Abandonment journey fixture'
const FIRST_OBJECTIVE = 'Plan the ledger total, then decide against it'
const SECOND_OBJECTIVE = 'Record a different objective after the abandonment'
const REASON = 'The ledger total will be delivered by a different change.'
const ABANDON_PATH = /\/api\/runs\/[^/]+\/abandon$/
const MANUAL_PATH = '/api/runs/manual'
const INSPECTION_PATH = /\/api\/runs\/[^/]+\/abandonment-inspection$/

const data = new JourneyData(PROJECT)

interface RunRow {
  Id: string
  ExecutionNumber: number
  Lifecycle: string
  Stage: string
  ActiveParticipantKind: string
  AbandonmentReason: string | null
  AbandonedAtUtc: string | null
  AccumulatedAutonomousSeconds: number
  MaximumAgentAttempts: number
  MaximumAgentInvocationTime: number | null
}

const sha256 = (value: string | Buffer) => createHash('sha256').update(value).digest('hex')

function runs(): RunRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, ExecutionNumber, Lifecycle, Stage, ActiveParticipantKind, AbandonmentReason, AbandonedAtUtc, AccumulatedAutonomousSeconds, MaximumAgentAttempts, MaximumAgentInvocationTime from runs where ProjectId = ? order by ExecutionNumber',
        )
        .all(data.projectId()) as unknown as RunRow[],
  )
}

function eventTypes(runId: string): { EventType: string; ActorKind: string; PayloadJson: string }[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select EventType, ActorKind, PayloadJson from events where RunId = ? order by Sequence')
        .all(runId) as unknown as { EventType: string; ActorKind: string; PayloadJson: string }[],
  )
}

interface EventRow {
  Sequence: number
  Id: string
  RunId: string
  EventType: string
  ActorKind: string
  PayloadJson: string
  OccurredAtUtc: string
}

/** Every stored event row of the run, with the identity, sequence and time the recorded abandonment inspection discloses. */
function allEventRows(runId: string): EventRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Sequence, Id, RunId, EventType, ActorKind, PayloadJson, OccurredAtUtc from events where RunId = ? order by Sequence')
        .all(runId) as unknown as EventRow[],
  )
}

function attemptsOf(runId: string): { AttemptNumber: number; Status: string; AgentOutcome: string | null }[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select AttemptNumber, Status, AgentOutcome from attempts where RunId = ? order by AttemptNumber')
        .all(runId) as unknown as { AttemptNumber: number; Status: string; AgentOutcome: string | null }[],
  )
}

function workspaceFacts(): { Status: string; LeaseStatus: string; WorkspacePath: string } {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select w.Status as Status, l.Status as LeaseStatus, w.WorkspacePath as WorkspacePath from git_workspaces w join repository_mutation_leases l on l.WorkspaceId = w.Id where w.ProjectId = ?',
        )
        .all(data.projectId()) as unknown as { Status: string; LeaseStatus: string; WorkspacePath: string }[],
  )[0]
}

/** A digest of every byte of the owned candidate workspace except Git's own administrative link file. */
function workspaceDigest(path: string): string {
  const hash = createHash('sha256')
  const files = readdirSync(path, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name !== '.git')
    .map((entry) => join(entry.parentPath, entry.name))
    .sort()
  for (const file of files) {
    hash.update(`${file.slice(path.length)}\n`)
    hash.update(readFileSync(file))
  }
  return hash.digest('hex')
}

/** The sealed context manifests of the run, by their stored hash and by the SHA-256 of the bytes read back from the owned artifact root. */
function sealedManifests(runId: string): { Id: string; ContentHash: string; ActualSha256: string }[] {
  const artifacts = withJourneyDb(
    (db) =>
      db
        .prepare("select AttemptId, ContentHash, RelativeStoragePath from artifacts where RunId = ? and Purpose = 'AgentContextManifest' order by AttemptId")
        .all(runId) as unknown as { AttemptId: string; ContentHash: string; RelativeStoragePath: string }[],
  )
  return artifacts.map((artifact) => ({
    Id: artifact.AttemptId,
    ContentHash: artifact.ContentHash,
    ActualSha256: sha256(readSealedManifestText(artifact.RelativeStoragePath)),
  }))
}

test('the abandonment journey: plan, abandon through the form, reload the recorded reason, and record another objective', async ({ page }) => {
  test.setTimeout(240_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('abandon-source')
  const stageInvocations = () => readInvocations(mark).filter((entry) => entry.kind !== 'probe')
  const abandonRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && ABANDON_PATH.test(new URL(request.url()).pathname)) {
      abandonRequests.push(request.url())
    }
  })

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project with a prepared candidate workspace and a captured checkpoint, so that the abandonment has real source,
  // lease and workspace facts to leave exactly as they are.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  const sourceBefore = repositorySnapshot(repository.path)

  // 2. Record the objective through the rendered intake form, then finish one process-double planning attempt.
  await page.getByRole('textbox', { name: 'Objective' }).fill(FIRST_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: FIRST_OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Plan proposed.')).toBeVisible({ timeout: 60_000 })

  const [first] = runs()
  expect(first.Lifecycle).toBe('Running')
  expect(attemptsOf(first.Id)).toEqual([{ AttemptNumber: 1, Status: 'Completed', AgentOutcome: 'Proposed' }])
  const stagesBefore = stageInvocations().length
  expect(stagesBefore).toBeGreaterThan(0)

  // What the abandonment must leave untouched: the owned workspace bytes, its row and lease, the source repository and the earlier sealed
  // manifest bytes.
  const workspace = workspaceFacts()
  expect(workspace).toMatchObject({ Status: 'Ready', LeaseStatus: 'Active' })
  const workspaceBefore = workspaceDigest(workspace.WorkspacePath)
  const manifestsBefore = sealedManifests(first.Id)
  expect(manifestsBefore).toHaveLength(1)
  expect(manifestsBefore[0].ActualSha256).toBe(manifestsBefore[0].ContentHash.toLowerCase().replace(/^sha256:/, ''))

  // 3. The form is offered for this settled, inactive run. Abandon through it: the generated client sends only the reason.
  const panel = page.getByRole('region', { name: 'Abandon run' })
  await expect(panel).toContainText('not a successful completion')
  await expect(page.getByRole('button', { name: 'Abandon run', exact: true })).toBeDisabled()
  await panel.getByLabel('Reason').fill(`  ${REASON}  `)
  const answered = page.waitForResponse((response) => response.request().method() === 'POST' && ABANDON_PATH.test(new URL(response.url()).pathname), {
    timeout: 30_000,
  })
  await page.getByRole('button', { name: 'Abandon run', exact: true }).click()
  const response = await answered
  expect(response.status()).toBe(200)
  expect(response.request().postDataJSON()).toEqual({ reason: REASON })
  const recorded = (await response.json()) as { runId: string; reason: string; abandonedAtUtc: string }
  expect(sameId(recorded.runId, first.Id)).toBe(true)
  expect(recorded.reason).toBe(REASON)

  // 4. The page shows the recorded abandonment as a terminal state that is not a completion, with the persisted reason and time, and
  // offers normal intake for another objective, without a reload.
  await expect(panel).toContainText('not completed')
  await expect(panel).toContainText(REASON)
  await expect(panel).toContainText(new Date(recorded.abandonedAtUtc).toISOString())
  await expect(panel.getByLabel('Reason')).toHaveCount(0)
  await expect(page.getByText(/^Abandoned · /)).toBeVisible()
  await expect(page.getByText(/This run was abandoned, so no further requests are available for it/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Request Codex plan' })).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Objective' })).toBeVisible({ timeout: 30_000 })

  // The host's durable facts: one closed run with its reason, the frozen clock, the cleared participant, exactly one Human event, the
  // attempt history intact, and no provider invoked by the abandonment.
  const [abandoned] = runs()
  expect(abandoned).toMatchObject({
    Id: first.Id,
    Lifecycle: 'Abandoned',
    Stage: first.Stage,
    ActiveParticipantKind: 'None',
    AbandonmentReason: REASON,
    MaximumAgentAttempts: first.MaximumAgentAttempts,
  })
  expect(Date.parse(abandoned.AbandonedAtUtc!)).toBe(Date.parse(recorded.abandonedAtUtc))
  const closure = eventTypes(first.Id).filter((event) => event.EventType === 'run.abandoned')
  expect(closure).toHaveLength(1)
  expect(closure[0].ActorKind).toBe('Human')
  expect(JSON.parse(closure[0].PayloadJson)).toEqual({ reason: REASON })
  expect(attemptsOf(first.Id)).toHaveLength(1)
  expect(abandonRequests).toHaveLength(1)
  expect(stageInvocations()).toHaveLength(stagesBefore)

  // 5. A reload reads the durable reason and history back and makes no abandonment request and no provider invocation.
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: FIRST_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(panel).toContainText(REASON, { timeout: 30_000 })
  await expect(panel).toContainText('not completed')
  await expect(panel).toContainText(new Date(recorded.abandonedAtUtc).toISOString())
  await expect(page.getByText(/^Abandoned · /)).toBeVisible()
  await expect(page.getByRole('region', { name: 'Agent attempt history' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Objective' })).toBeVisible()
  expect(abandonRequests).toHaveLength(1)
  expect(stageInvocations()).toHaveLength(stagesBefore)
  expect(runs()[0]).toEqual(abandoned)

  // 6. Record a second objective in the same project through the normal intake: a distinct run with its own number, normal fresh
  // budgets and no attempt, inheriting nothing from the abandoned one.
  await page.getByRole('textbox', { name: 'Objective' }).fill(SECOND_OBJECTIVE)
  const created = page.waitForResponse((answer) => answer.request().method() === 'POST' && new URL(answer.url()).pathname === MANUAL_PATH, {
    timeout: 30_000,
  })
  await page.getByRole('button', { name: 'Record manual run' }).click()
  expect((await created).status()).toBe(200)
  await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 15_000 })

  const all = runs()
  expect(all).toHaveLength(2)
  const [earlier, second] = all
  expect(earlier).toEqual(abandoned)
  expect(second.ExecutionNumber).toBe(2)
  expect(sameId(second.Id, earlier.Id)).toBe(false)
  expect(second).toMatchObject({ Lifecycle: 'Created', Stage: 'Intake', AbandonmentReason: null, AbandonedAtUtc: null, AccumulatedAutonomousSeconds: 0 })
  expect(second.MaximumAgentAttempts).toBe(16)
  expect(attemptsOf(second.Id)).toEqual([])
  expect(eventTypes(second.Id).map((event) => event.EventType)).toEqual(['run.started'])
  await expect(page.getByText('Agent claims: 0 of 16 used.')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('button', { name: 'Request Codex plan' })).toBeVisible()

  // Nothing about the first run, the workspace, the source or any provider changed; a reload shows the same two runs.
  expect(stageInvocations()).toHaveLength(stagesBefore)
  expect(workspaceFacts()).toEqual(workspace)
  expect(workspaceDigest(workspace.WorkspacePath)).toBe(workspaceBefore)
  expect(sealedManifests(first.Id)).toEqual(manifestsBefore)
  expect(repositorySnapshot(repository.path)).toEqual(sourceBefore)
  expect(abandonRequests).toHaveLength(1)
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  expect(runs()).toEqual(all)
  expect(stageInvocations()).toHaveLength(stagesBefore)

  // 7. The recorded abandonment inspection (ADR-0071). The second objective has settled and the live cockpit follows it, so the earlier
  // closure is reachable only through the collapsed Run history. Everything the inspection must leave alone is baselined first: the
  // business POSTs, the invocation count of the doubles and the named durable, repository, workspace and sealed observations (the run
  // rows, every event row, the attempts, the owned workspace and its bytes, the sealed manifests and the source repository). Every exact
  // response is awaited and compared with the stored rows, and the rendered facts with that same row. A page reload is not a host restart.
  const posts = new PostInventory()
  const inspectionRequests: { method: string; path: string; search: string; body: string | null }[] = []
  page.on('request', (request) => {
    const { pathname, search } = new URL(request.url())
    if (request.method() === 'POST') {
      posts.record(pathname)
    }

    if (INSPECTION_PATH.test(pathname)) {
      inspectionRequests.push({ method: request.method(), path: pathname.toLowerCase(), search, body: request.postData() })
    }
  })
  const lower = (value: string) => value.toLowerCase()
  // The provider writes a stored instant as `yyyy-MM-dd HH:mm:ss.FFFFFFF+00:00`; the wire carries it as a seven-digit UTC round trip.
  const storedInstant = (text: string): string => {
    const match = /^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?\+00:00$/.exec(text)
    expect(match, `stored instant ${text}`).not.toBeNull()
    return `${match![1]}T${match![2]}.${(match![3] ?? '').padEnd(7, '0')}Z`
  }
  const durableBaseline = () => ({
    runs: runs(),
    events: [first.Id, second.Id].map((runId) => allEventRows(runId)),
    attempts: [first.Id, second.Id].map((runId) => attemptsOf(runId)),
    workspace: workspaceFacts(),
    workspaceBytes: workspaceDigest(workspace.WorkspacePath),
    manifests: sealedManifests(first.Id),
    source: repositorySnapshot(repository.path),
    invocations: stageInvocations().length,
  })
  const baseline = durableBaseline()
  const postMark = posts.mark()

  const closureEvent = allEventRows(first.Id).filter((event) => event.EventType === 'run.abandoned')
  expect(closureEvent).toHaveLength(1)
  const expectedRecorded = {
    runId: lower(first.Id),
    status: 'Recorded',
    abandonment: {
      projectId: lower(data.projectId()),
      reason: REASON,
      abandonedAtUtc: storedInstant(abandoned.AbandonedAtUtc!),
      eventId: lower(closureEvent[0].Id),
      eventSequence: String(closureEvent[0].Sequence),
    },
  }
  const expectedNotRecorded = { runId: lower(second.Id), status: 'NotRecorded', abandonment: null }
  expect(allEventRows(second.Id).filter((event) => event.EventType === 'run.abandoned')).toEqual([])

  const historyRegion = page.getByRole('region', { name: 'Run history' })
  const historyRows = historyRegion.locator('.dc-run-history-items > li')
  const selected = historyRegion.getByRole('region', { name: 'Selected run' })
  const inspection = selected.getByRole('region', { name: 'Inspect recorded abandonment', exact: true })
  const toggle = inspection.getByRole('button', { name: 'Inspect recorded abandonment', exact: true })
  const inspectionAnswered = (runId: string) =>
    page.waitForResponse(
      (answer) => answer.request().method() === 'GET' && new URL(answer.url()).pathname.toLowerCase() === `/api/runs/${lower(runId)}/abandonment-inspection`,
      { timeout: 30_000 },
    )
  const expectCold = async () => {
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await expect(inspection.getByRole('button')).toHaveText(['Inspect recorded abandonment'])
    await expect(inspection).not.toContainText(REASON)
    await expect(inspection).not.toContainText('found no recorded abandonment')
  }
  const expectRecordedRendered = async () => {
    await expect(inspection).toContainText(REASON)
    await expect(inspection).toContainText(expectedRecorded.abandonment.abandonedAtUtc)
    await expect(inspection).toContainText(`${expectedRecorded.abandonment.eventId} · sequence ${expectedRecorded.abandonment.eventSequence}`)
    await expect(inspection).toContainText(expectedRecorded.abandonment.projectId)
    await expect(inspection).toContainText('without successful completion')
    await expect(inspection.getByRole('button')).toHaveText(['Hide recorded abandonment', 'Reload recorded abandonment'])
  }

  await historyRegion.getByRole('button', { name: 'Show run history' }).click()
  await expect(historyRows).toHaveCount(2, { timeout: 30_000 })
  await expect(historyRows.nth(0)).toContainText('Run #2')
  await expect(historyRows.nth(0)).toContainText('Created · Intake')
  await expect(historyRows.nth(1)).toContainText('Run #1')
  await expect(historyRows.nth(1)).toContainText('Abandoned · ')
  expect(inspectionRequests).toEqual([])

  // The earlier run: collapsed and cold, then one exact recorded answer, an explicit reload, and a cold close.
  await historyRegion.getByRole('button', { name: 'Inspect run #1' }).click()
  await expect(selected).toContainText(FIRST_OBJECTIVE)
  await expectCold()
  expect(inspectionRequests).toEqual([])
  const firstPath = `/api/runs/${lower(first.Id)}/abandonment-inspection`
  const opened = inspectionAnswered(first.Id)
  await toggle.click()
  const openedAnswer = await opened
  expect(openedAnswer.status()).toBe(200)
  const body = await openedAnswer.json()
  expect(body).toEqual(expectedRecorded)
  expect(Object.keys(body).sort()).toEqual(['abandonment', 'runId', 'status'])
  expect(Object.keys(body.abandonment).sort()).toEqual(['abandonedAtUtc', 'eventId', 'eventSequence', 'projectId', 'reason'])
  expect(typeof body.abandonment.eventSequence).toBe('string')
  expect(JSON.stringify(body)).not.toContain(workspace.WorkspacePath)
  await expectRecordedRendered()
  expect(inspectionRequests).toEqual([{ method: 'GET', path: firstPath, search: '', body: null }])

  const reloaded = inspectionAnswered(first.Id)
  await inspection.getByRole('button', { name: 'Reload recorded abandonment', exact: true }).click()
  const reloadedAnswer = await reloaded
  expect(reloadedAnswer.status()).toBe(200)
  expect(await reloadedAnswer.json()).toEqual(expectedRecorded)
  await expectRecordedRendered()
  expect(inspectionRequests).toHaveLength(2)

  await inspection.getByRole('button', { name: 'Hide recorded abandonment', exact: true }).click()
  await expectCold()
  expect(inspectionRequests).toHaveLength(2)

  // Selection replacement: the newer objective's own region is cold too, and its one read is NotRecorded, never the earlier decision.
  await toggle.click()
  await expectRecordedRendered()
  expect(inspectionRequests).toHaveLength(3)
  await historyRegion.getByRole('button', { name: 'Inspect run #2' }).click()
  await expect(selected).toContainText(SECOND_OBJECTIVE)
  await expectCold()
  await expect(inspection).not.toContainText(REASON)
  expect(inspectionRequests).toHaveLength(3)
  const newer = inspectionAnswered(second.Id)
  await toggle.click()
  const newerAnswer = await newer
  expect(newerAnswer.status()).toBe(200)
  expect(await newerAnswer.json()).toEqual(expectedNotRecorded)
  await expect(inspection).toContainText('found no recorded abandonment for this run')
  await expect(inspection).not.toContainText(REASON)
  await expect(inspection.getByRole('button')).toHaveText(['Hide recorded abandonment', 'Reload recorded abandonment'])
  expect(inspectionRequests).toHaveLength(4)
  expect(inspectionRequests[3]).toEqual({ method: 'GET', path: `/api/runs/${lower(second.Id)}/abandonment-inspection`, search: '', body: null })

  // A to B to A: the earlier run's region starts collapsed again, shows nothing of its earlier answer and reads nothing.
  await historyRegion.getByRole('button', { name: 'Inspect run #1' }).click()
  await expect(selected).toContainText(FIRST_OBJECTIVE)
  await expectCold()
  expect(inspectionRequests).toHaveLength(4)

  // After a real page reload the cockpit follows the newer run, the history is collapsed and nothing was read; the recorded abandonment
  // of the earlier run is cold when it is selected again.
  const requestsBeforeReload = inspectionRequests.length
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await expect(historyRegion.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
  expect(inspectionRequests).toHaveLength(requestsBeforeReload)
  await historyRegion.getByRole('button', { name: 'Show run history' }).click()
  await expect(historyRows).toHaveCount(2, { timeout: 30_000 })
  await historyRegion.getByRole('button', { name: 'Inspect run #1' }).click()
  await expect(selected).toContainText(FIRST_OBJECTIVE)
  await expectCold()
  expect(inspectionRequests).toHaveLength(requestsBeforeReload)

  // The inspection added no command, no invocation and no named durable change.
  expect(posts.businessSince(postMark)).toEqual([])
  expect(abandonRequests).toHaveLength(1)
  expect(durableBaseline()).toEqual(baseline)
  expect(runs()).toEqual(all)
})
