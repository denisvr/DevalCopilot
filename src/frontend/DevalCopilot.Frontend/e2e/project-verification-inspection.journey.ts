import { createHash } from 'node:crypto'
import { readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, journeyRoot, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { injectJourneySession, readJson, registerProject, selectProject } from './journey/journeySupport'
import { PostInventory } from './journey/requestInventory'

// The browser-driven proof of the recorded project verification inspection (ADR-0069), through the rendered controls of the production
// frontend over the real generated client, MVC authentication, mediator, SQLite and artifact store. Only the verification executable is
// the deterministic owned double. The database, the artifact files and the double's invocation log are only READ. Setup uses the rendered
// controls alone: one project, its workspace and checkpoint #1, one recipe, ONE manual verification (execution #1: no run, no Agent
// attempt and no local execution sequence exists when it is claimed), a manual run and a newer checkpoint #2 recorded afterwards, and
// twenty more manual verifications, so that the live latest-20 list no longer holds execution #1. Once that setup has settled, every
// request below is an explicit read: the page opens the collapsed region (newest 20 of the project), asks for the older page, selects
// execution #1 (metadata only) and loads each of its two sealed streams explicitly. A page reload returns to the cold closed state, and
// the explicit "Reload recorded verifications" returns to the newest page. Every awaited response is compared with the database and the
// sealed bytes on disk, and the inspection adds no business POST, no executable invocation and no change to the named durable facts.
// A page reload is not a host restart; the file-backed Application and API tests prove database and host reopening separately. This is
// not proof that the verification double reflects a real project, and an execution is never attributed to a run here.

const PROJECT = 'Project verification inspection journey fixture'
const OBJECTIVE = 'A newer manual run beside the older verification'
const RECIPE = 'Inspection check'
const CLAIM = /\/verification-commands\/[^/]+\/executions$/
const REGION = 'Inspect recorded verifications'
const TOTAL = 21
const HISTORY_MEMBERS = ['hasMore', 'items', 'nextBeforeExecutionNumber', 'projectId', 'status']
const ITEM_MEMBERS = ['claimedAtUtc', 'commandName', 'executionId', 'executionNumber']
const DETAIL_MEMBERS = ['execution', 'executionId', 'projectId', 'status']
const EXECUTION_MEMBERS = [
  'checkpointId', 'checkpointNumber', 'claimFingerprintSha256', 'claimedAtUtc', 'commandId', 'commandName', 'completedAtUtc',
  'completionFingerprintSha256', 'dispatchedAtUtc', 'executionNumber', 'exitCode', 'outcome', 'standardError', 'standardOutput', 'status',
  'workspaceId',
]
const STREAM_MEMBERS = ['artifactId', 'byteLength', 'captureOutcome', 'status', 'truncated']
const WINDOW_MEMBERS = ['artifact', 'executionId', 'nextOffset', 'projectId', 'status', 'stream', 'text', 'totalLength']

const data = new JourneyData(PROJECT)
const lower = (value: string) => value.toLowerCase()

interface ExecutionFacts {
  Id: string
  ExecutionNumber: number
  VerificationCommandId: string
  CommandName: string
  GitWorkspaceId: string
  GitCheckpointId: string
  CheckpointFingerprintSha256: string
  CompletionFingerprintSha256: string | null
  Status: string
  Outcome: string | null
  ExitCode: number | null
  ClaimedAtUtc: string
  DispatchedAtUtc: string | null
  CompletedAtUtc: string | null
}
interface OutputFacts {
  Id: string
  Purpose: string
  RelativeStoragePath: string
  ContentHash: string
  ByteLength: number
  Truncated: number | null
  CaptureOutcome: string
}

function executionFacts(): ExecutionFacts[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, ExecutionNumber, VerificationCommandId, CommandName, GitWorkspaceId, GitCheckpointId, CheckpointFingerprintSha256, '
            + 'CompletionFingerprintSha256, Status, Outcome, ExitCode, ClaimedAtUtc, DispatchedAtUtc, CompletedAtUtc '
            + 'from verification_executions where ProjectId = ? order by ExecutionNumber',
        )
        .all(data.projectId()) as unknown as ExecutionFacts[],
  )
}
function outputsOf(executionId: string): OutputFacts[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome '
            + 'from verification_output_artifacts where VerificationExecutionId = ? order by Purpose',
        )
        .all(executionId) as unknown as OutputFacts[],
  )
}
function count(sql: string, ...parameters: string[]): number {
  return withJourneyDb((db) => (db.prepare(sql).get(...parameters) as { N: number }).N)
}

/** The stored provider time text as the host's own UTC round-trip text (seven fraction digits, Z). */
function roundTrip(stored: string): string {
  const match = /^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?\+00:00$/.exec(stored)
  expect(match, `stored time ${stored}`).not.toBeNull()
  return `${match![1]}T${match![2]}.${(match![3] ?? '').padEnd(7, '0')}Z`
}

/** The sealed bytes of one stream, read-only from the owned artifact root, after proving they match the recorded length and hash. */
function sealedBytes(output: OutputFacts): Buffer {
  const path = join(journeyRoot().root, 'artifacts', output.RelativeStoragePath)
  const bytes = readFileSync(path)
  expect(statSync(path).size).toBe(output.ByteLength)
  expect(`sha256:${createHash('sha256').update(bytes).digest('hex')}`).toBe(output.ContentHash)
  return bytes
}

/**
 * The durable fingerprint of the inspection segment. It covers exactly: the project's execution, checkpoint, run, attempt, event,
 * collaboration-message and output-artifact row counts; every execution's number, status, outcome, exit code, checkpoint, completion
 * fingerprint and dispatch and completion times; every output row's purpose, length, content hash and capture facts; and the SHA-256 of
 * the two sealed files of execution #1. It is a selected-field and row-count comparison, not a byte identity of every stored row, of
 * any other artifact file or of another project's rows.
 */
function durableState(): string {
  const projectId = data.projectId()
  const executions = executionFacts()
  return JSON.stringify({
    executions: count('select count(*) as N from verification_executions where ProjectId = ?', projectId),
    checkpoints: count('select count(*) as N from git_checkpoints where WorkspaceId in (select Id from git_workspaces where ProjectId = ?)', projectId),
    runs: count('select count(*) as N from runs where ProjectId = ?', projectId),
    attempts: count('select count(*) as N from attempts where RunId in (select Id from runs where ProjectId = ?)', projectId),
    events: count('select count(*) as N from events where RunId in (select Id from runs where ProjectId = ?)', projectId),
    messages: count('select count(*) as N from collaboration_messages where RunId in (select Id from runs where ProjectId = ?)', projectId),
    outputs: count(
      'select count(*) as N from verification_output_artifacts where VerificationExecutionId in (select Id from verification_executions where ProjectId = ?)',
      projectId,
    ),
    facts: executions.map((execution) => [
      execution.Id, execution.ExecutionNumber, execution.Status, execution.Outcome, execution.ExitCode, execution.GitCheckpointId,
      execution.CompletionFingerprintSha256, execution.DispatchedAtUtc, execution.CompletedAtUtc,
      outputsOf(execution.Id).map((output) => [output.Purpose, output.ByteLength, output.ContentHash, output.Truncated, output.CaptureOutcome]),
    ]),
    sealed: outputsOf(executions[0].Id).map((output) => createHash('sha256').update(sealedBytes(output)).digest('hex')),
  })
}

test('the recorded project verification inspection journey: an older manual verification beyond the live latest twenty is inspected, its streams loaded explicitly, without writing anything', async ({
  page,
}) => {
  test.setTimeout(540_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('project-verification-inspection-source')
  // Every request the page issues. The run notification hub (LongPolling) posts at moments no business action controls; the inventory keeps
  // that traffic in its raw record but never counts it as a business command.
  const posts = new PostInventory()
  const gets: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST') {
      posts.record(path)
    }
    if (request.method() === 'GET') {
      gets.push(path)
    }
  })

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)

  const source = page.getByRole('region', { name: 'Source evidence' })
  const verification = page.getByRole('region', { name: 'Verification commands' })
  const runButton = verification.getByRole('button', { name: 'Run', exact: true }).first()

  // 1. Setup through the rendered controls: the project, its workspace and checkpoint #1, one recipe and the FIRST manual verification.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(source).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByLabel('Verification command name').fill(RECIPE)
  await page.getByLabel('Verification executable path').fill(join(journeyRoot().root, 'bin', 'verify.exe'))
  await page.getByLabel('Verification arguments').fill('check')
  await page.getByRole('button', { name: 'Add command' }).click()
  await expect(verification).toContainText(`#1 ${RECIPE}`)

  const runOnce = async (expected: number) => {
    await expect(runButton).toBeEnabled({ timeout: 60_000 })
    const claim = page.waitForResponse((response) => response.request().method() === 'POST' && CLAIM.test(new URL(response.url()).pathname), {
      timeout: 30_000,
    })
    await runButton.click()
    expect((await claim).status()).toBe(202)
    await expect.poll(() => executionFacts().length, { timeout: 30_000 }).toBe(expected)
    await expect.poll(() => executionFacts().at(-1)!.Status, { timeout: 60_000 }).toBe('Failed')
  }
  await runOnce(1)

  // The older verification is a plain manual one: no run, Agent attempt, local sequence or review membership exists yet or ever links it.
  const first = executionFacts()[0]
  expect(first.ExecutionNumber).toBe(1)
  expect(count('select count(*) as N from runs where ProjectId = ?', data.projectId())).toBe(0)

  // 2. NEWER work: a manual run and checkpoint #2, then twenty more manual verifications against it.
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(source).toContainText('Checkpoint #2 · 0 changed files', { timeout: 30_000 })
  for (let number = 2; number <= TOTAL; number += 1) {
    await runOnce(number)
  }

  // 3. The scene as the host recorded it, and the setup settled: no verification is running and the double is idle.
  await expect.poll(() => executionFacts().every((execution) => execution.Status === 'Failed'), { timeout: 60_000 }).toBe(true)
  const executions = executionFacts()
  const projectId = data.projectId()
  expect(executions).toHaveLength(TOTAL)
  expect(executions.map((execution) => execution.ExecutionNumber)).toEqual(Array.from({ length: TOTAL }, (_, index) => index + 1))
  expect(executions.every((execution) => execution.ExitCode === 1 && execution.Outcome === 'Exited')).toBe(true)
  const checkpoints = data.checkpoints()
  expect(checkpoints.map((checkpoint) => checkpoint.CheckpointNumber)).toEqual([1, 2])
  expect(sameId(executions[0].GitCheckpointId, checkpoints[0].Id)).toBe(true)
  expect(executions.slice(1).every((execution) => sameId(execution.GitCheckpointId, checkpoints[1].Id))).toBe(true)
  const runId = data.runId()
  expect(data.attempts()).toEqual([])
  expect(count('select count(*) as N from attempt_verification_evidence where VerificationExecutionId = ?', first.Id)).toBe(0)
  expect(count('select count(*) as N from local_execution_sequence_steps where OperationId = ?', first.Id)).toBe(0)
  const target = outputsOf(first.Id)
  expect(target.map((output) => output.Purpose)).toEqual(['StandardError', 'StandardOutput'])
  const stdout = target.find((output) => output.Purpose === 'StandardOutput')!
  const stderr = target.find((output) => output.Purpose === 'StandardError')!
  const stdoutBytes = sealedBytes(stdout)
  const stderrBytes = sealedBytes(stderr)
  expect(stdoutBytes.length).toBeGreaterThan(0)
  expect(stderrBytes.length).toBeGreaterThan(0)

  // The live latest-20 list (read here only to show the gap) no longer holds execution #1.
  const live = await readJson<{ executionNumber: number }[]>(page, `/api/projects/${lower(projectId)}/verification-executions`, 'live list')
  expect(live.map((item) => item.executionNumber)).toEqual(Array.from({ length: 20 }, (_, index) => TOTAL - index))

  // 4. The baseline, taken after the setup settled: every business POST, every executable invocation and the durable fingerprint (its
  // exact coverage is documented on `durableState`). The inspection segment below must add none of them.
  const durableBefore = durableState()
  const postsBefore = posts.mark()
  const businessBefore = posts.business().length
  const invocationsBefore = readInvocations(mark).filter((entry) => entry.kind !== 'probe').length
  expect(invocationsBefore).toBe(TOTAL)
  const getsBefore = gets.length
  const base = `/api/projects/${lower(projectId)}/verification-history`
  const detailPath = `${base}/${lower(first.Id)}`
  const windowPath = (stream: string) => `${detailPath}/artifact-window/${stream}`
  const inspectionReads = (start: number) => gets.slice(start).filter((path) => path.includes('/verification-history'))

  // The cold closed state: the region is collapsed and nothing of the inspection was read to render the page.
  const region = page.getByRole('region', { name: REGION })
  const toggle = region.getByRole('button', { name: REGION, exact: true })
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  expect(inspectionReads(getsBefore)).toEqual([])
  await expect(region.getByRole('button', { name: /^Inspect verification #/ })).toHaveCount(0)

  // 5. Open: the newest page of the project, read once.
  const newest = page.waitForResponse(
    (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === base && new URL(response.url()).search === '',
    { timeout: 30_000 },
  )
  await toggle.click()
  const newestResponse = await newest
  expect(newestResponse.status()).toBe(200)
  const newestBody = (await newestResponse.json()) as Record<string, unknown> & { items: Record<string, unknown>[] }
  const expectPage = (body: typeof newestBody, numbers: number[], hasMore: boolean, next: number | null) => {
    expect(Object.keys(body).sort()).toEqual(HISTORY_MEMBERS)
    expect(body.status).toBe('Recorded')
    expect(sameId(String(body.projectId), projectId)).toBe(true)
    expect(body.hasMore).toBe(hasMore)
    expect(body.nextBeforeExecutionNumber).toBe(next)
    expect(body.items.map((item) => item.executionNumber)).toEqual(numbers)
    for (const item of body.items) {
      expect(Object.keys(item).sort()).toEqual(ITEM_MEMBERS)
      const row = executions.find((execution) => execution.ExecutionNumber === item.executionNumber)!
      expect(sameId(String(item.executionId), row.Id)).toBe(true)
      expect(item.commandName).toBe(RECIPE)
      expect(item.claimedAtUtc).toBe(roundTrip(row.ClaimedAtUtc))
    }
  }
  expectPage(newestBody, Array.from({ length: 20 }, (_, index) => TOTAL - index), true, 2)
  await expect(region.getByRole('button', { name: /^Inspect verification #\d+$/ })).toHaveCount(20, { timeout: 30_000 })
  await expect(region.getByRole('button', { name: 'Inspect verification #1', exact: true })).toHaveCount(0)
  await expect(region.getByRole('button', { name: 'Older verifications' })).toBeVisible()
  await expect(region).toContainText('Recorded verifications #21 to #2.')
  await expect(region).toContainText('not attributed to any run')
  expect(inspectionReads(getsBefore)).toEqual([base])

  // 6. Older verifications: ONE explicit read replaces the page (no accumulation) with the single remaining execution, #1.
  const older = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET'
      && lower(new URL(response.url()).pathname) === base
      && new URL(response.url()).search === '?beforeExecutionNumber=2',
    { timeout: 30_000 },
  )
  await region.getByRole('button', { name: 'Older verifications' }).click()
  const olderResponse = await older
  expect(olderResponse.status()).toBe(200)
  expectPage((await olderResponse.json()) as typeof newestBody, [1], false, null)
  await expect(region.getByRole('button', { name: /^Inspect verification #\d+$/ })).toHaveCount(1, { timeout: 30_000 })
  await expect(region.getByRole('button', { name: 'Inspect verification #21', exact: true })).toHaveCount(0)
  await expect(region.getByRole('button', { name: 'Older verifications' })).toHaveCount(0)
  expect(inspectionReads(getsBefore)).toEqual([base, base])

  // 7. Select execution #1: ONE metadata read of exactly that execution, no output read.
  const detailed = page.waitForResponse(
    (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === detailPath,
    { timeout: 30_000 },
  )
  await region.getByRole('button', { name: 'Inspect verification #1', exact: true }).click()
  const detailResponse = await detailed
  expect(detailResponse.status()).toBe(200)
  const detail = (await detailResponse.json()) as Record<string, unknown> & { execution: Record<string, unknown> }
  expect(Object.keys(detail).sort()).toEqual(DETAIL_MEMBERS)
  expect(detail.status).toBe('Recorded')
  expect(sameId(String(detail.projectId), projectId)).toBe(true)
  expect(sameId(String(detail.executionId), first.Id)).toBe(true)
  const body = detail.execution
  expect(Object.keys(body).sort()).toEqual(EXECUTION_MEMBERS)
  expect(body).toMatchObject({
    executionNumber: 1,
    commandName: RECIPE,
    checkpointNumber: 1,
    claimFingerprintSha256: first.CheckpointFingerprintSha256,
    completionFingerprintSha256: first.CompletionFingerprintSha256,
    status: 'Failed',
    outcome: 'Exited',
    exitCode: 1,
    claimedAtUtc: roundTrip(first.ClaimedAtUtc),
    dispatchedAtUtc: roundTrip(first.DispatchedAtUtc!),
    completedAtUtc: roundTrip(first.CompletedAtUtc!),
  })
  expect(sameId(String(body.commandId), first.VerificationCommandId)).toBe(true)
  expect(sameId(String(body.workspaceId), first.GitWorkspaceId)).toBe(true)
  expect(sameId(String(body.checkpointId), checkpoints[0].Id)).toBe(true)
  expect(sameId(String(body.checkpointId), checkpoints[1].Id)).toBe(false)
  for (const [member, output, bytes] of [['standardOutput', stdout, stdoutBytes], ['standardError', stderr, stderrBytes]] as const) {
    const descriptor = body[member] as Record<string, unknown>
    expect(Object.keys(descriptor).sort()).toEqual(STREAM_MEMBERS)
    expect(descriptor).toMatchObject({
      status: 'Recorded',
      byteLength: bytes.length,
      captureOutcome: 'CapturedWithKnownTruncation',
      truncated: false,
    })
    expect(sameId(String(descriptor.artifactId), output.Id)).toBe(true)
  }

  // Rendered facts are the host's own answer, bookkeeping only.
  const shown = region.locator('[aria-label="Recorded verification #1"]')
  await expect(shown).toContainText(`#1 · ${RECIPE}`, { timeout: 30_000 })
  await expect(shown).toContainText('Failed · process exited · exit code 1')
  await expect(shown).toContainText(`#1 (${lower(checkpoints[0].Id)})`)
  await expect(shown).toContainText(lower(first.GitWorkspaceId))
  await expect(shown).toContainText(first.CheckpointFingerprintSha256)
  await expect(shown).toContainText(roundTrip(first.ClaimedAtUtc))
  await expect(shown).toContainText(roundTrip(first.DispatchedAtUtc!))
  await expect(shown).toContainText(roundTrip(first.CompletedAtUtc!))
  await expect(shown).toContainText(`Standard output: ${stdoutBytes.length} bytes · not truncated at capture`)
  await expect(shown).toContainText(`Standard error: ${stderrBytes.length} bytes · not truncated at capture`)
  await expect(shown).toContainText('bookkeeping')
  // Selection alone read no output, no Agent evidence and nothing of another execution.
  expect(inspectionReads(getsBefore)).toEqual([base, base, detailPath])

  // 8. Each stream is viewed and loaded EXPLICITLY, one actual window per Load, and compared with the sealed bytes on disk.
  const loadStream = async (stream: 'stdout' | 'stderr', label: string, output: OutputFacts, bytes: Buffer) => {
    await shown.getByRole('button', { name: `View ${label} of verification #1`, exact: true }).click()
    expect(inspectionReads(getsBefore).filter((path) => path.includes('/artifact-window/'))).toHaveLength(stream === 'stdout' ? 0 : 1)
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === windowPath(stream),
      { timeout: 30_000 },
    )
    await shown.getByRole('button', { name: `Load ${label} of verification #1`, exact: true }).click()
    const response = await answered
    expect(response.status()).toBe(200)
    expect(new URL(response.url()).search).toBe('?fromOffset=0&maxBytes=16384')
    const window = (await response.json()) as Record<string, unknown>
    expect(Object.keys(window).sort()).toEqual(WINDOW_MEMBERS)
    expect(window).toMatchObject({ stream, status: 'Ok', nextOffset: bytes.length, totalLength: bytes.length, text: bytes.toString('utf8') })
    expect(sameId(String(window.projectId), projectId)).toBe(true)
    expect(sameId(String(window.executionId), first.Id)).toBe(true)
    expect(window.artifact).toMatchObject({ status: 'Recorded', byteLength: bytes.length, captureOutcome: 'CapturedWithKnownTruncation', truncated: false })
    expect(sameId(String((window.artifact as Record<string, unknown>).artifactId), output.Id)).toBe(true)
    expect(`sha256:${createHash('sha256').update(Buffer.from(String(window.text), 'utf8')).digest('hex')}`).toBe(output.ContentHash)
    await expect(shown.locator('pre.dc-artifact-window-text')).toHaveText(bytes.toString('utf8'), { timeout: 30_000 })
    await expect(shown).toContainText(`${bytes.length} of ${bytes.length} bytes read.`)
    await expect(shown).toContainText('This is the end of the recorded output.')
  }
  await loadStream('stdout', 'standard output', stdout, stdoutBytes)
  await shown.getByRole('button', { name: 'Hide standard output of verification #1', exact: true }).click()
  await loadStream('stderr', 'standard error', stderr, stderrBytes)
  expect(inspectionReads(getsBefore)).toEqual([base, base, detailPath, windowPath('stdout'), windowPath('stderr')])

  // 9. A page reload is the cold closed state again: collapsed, nothing selected or loaded, and no inspection read until it is opened.
  const afterReload = gets.length
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('region', { name: 'Verification commands' })).toBeVisible({ timeout: 30_000 })
  const coldRegion = page.getByRole('region', { name: REGION })
  await expect(coldRegion.getByRole('button', { name: REGION, exact: true })).toHaveAttribute('aria-expanded', 'false')
  await expect(coldRegion.getByRole('button', { name: /^Inspect verification #/ })).toHaveCount(0)
  await expect(coldRegion.locator('[aria-label^="Recorded verification #"]')).toHaveCount(0)
  await expect(coldRegion.locator('pre.dc-artifact-window-text')).toHaveCount(0)
  expect(inspectionReads(afterReload)).toEqual([])

  // 10. Reopen: the NEWEST page again (not the older one), nothing selected. Select #1 through the older page, then the explicit reload
  // clears the selection and its content and reads the newest page again, once.
  const reopened = page.waitForResponse(
    (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === base && new URL(response.url()).search === '',
    { timeout: 30_000 },
  )
  await coldRegion.getByRole('button', { name: REGION, exact: true }).click()
  expect((await reopened).status()).toBe(200)
  await expect(coldRegion.getByRole('button', { name: /^Inspect verification #\d+$/ })).toHaveCount(20, { timeout: 30_000 })
  await expect(coldRegion.locator('[aria-label^="Recorded verification #"]')).toHaveCount(0)
  await coldRegion.getByRole('button', { name: 'Older verifications' }).click()
  await coldRegion.getByRole('button', { name: 'Inspect verification #1', exact: true }).click()
  await expect(coldRegion.locator('[aria-label="Recorded verification #1"]')).toContainText('Failed · process exited · exit code 1', { timeout: 30_000 })
  const beforeReload = inspectionReads(afterReload).length
  const reloaded = page.waitForResponse(
    (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === base && new URL(response.url()).search === '',
    { timeout: 30_000 },
  )
  await coldRegion.getByRole('button', { name: 'Reload recorded verifications' }).click()
  const reloadedResponse = await reloaded
  expect(reloadedResponse.status()).toBe(200)
  expectPage((await reloadedResponse.json()) as typeof newestBody, Array.from({ length: 20 }, (_, index) => TOTAL - index), true, 2)
  await expect(coldRegion.getByRole('button', { name: /^Inspect verification #\d+$/ })).toHaveCount(20, { timeout: 30_000 })
  await expect(coldRegion.locator('[aria-label^="Recorded verification #"]')).toHaveCount(0)
  expect(inspectionReads(afterReload).slice(beforeReload)).toEqual([base])

  // 11. The whole segment added no business POST, no executable invocation and no change to the named durable facts.
  expect(posts.businessSince(postsBefore)).toEqual([])
  expect(posts.business()).toHaveLength(businessBefore)
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(invocationsBefore)
  expect(durableState()).toBe(durableBefore)
  expect(data.runId()).toBe(runId)
  expect(inspectionReads(getsBefore).every((path) => path.startsWith(base))).toBe(true)
})
