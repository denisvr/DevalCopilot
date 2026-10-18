import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import {
  createJourneyRepository,
  markInvocations,
  readInvocations,
  readLaunchTargetVerdict,
  readSealedManifestText,
  repositorySnapshot,
} from './journey/journeyEnv'
import { endAccountUsageScript, startAccountUsageScript } from './journey/accountUsage'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of HIST-ATT-01: an owner reaches the Agent attempts of an OLDER run through the read-only Run history and
// inspects a failed attempt that emitted no collaboration message, its evidence and its sealed context-manifest text. Everything runs
// through the rendered controls of the production frontend over the generated client, real MVC authentication, mediator, SQLite and all
// supervisors; only the provider executables are the deterministic owned doubles. The scene is produced by existing supported behavior
// only: the run-scoped Codex account-usage stop (ADR-0025) stops a claimed planning attempt before dispatch (the Codex double answers
// the scripted observations), the run is abandoned through its form (ADR-0031) and a newer objective is recorded through the intake.
// The database, the artifact root, the source repository and the doubles' invocation log are only READ, never seeded.
//
// It proves: the historical attempt control is collapsed and reads nothing until opened; every request names the older run and the
// selected attempt, never the current run; the evidence and the exact sealed bytes are shown; a reload keeps the same access; and the
// inspection itself invokes no provider, changes no workflow row, Git state or source file, and leaves the current selection as it was.
// It is not replay, provider reliability or a claim about any other attempt. HIST-MET-01 adds the stopped attempt's recorded execution facts:
// a truthful absence of any measured process result or usage beside its semantic result and configured timeout, as stored. HIST-INP-01
// adds its recorded collaboration inputs: the genuine empty set of a planner that recorded none, read only when opened.

const PROJECT = 'Historical attempts journey fixture'
const FIRST_OBJECTIVE = 'Plan the ledger total, stopped before dispatch'
const SECOND_OBJECTIVE = 'Record a newer objective after the stopped one'
const REASON = 'The stopped plan is superseded by a newer objective.'
const STOP_PATH = /\/api\/runs\/[^/]+\/codex-account-usage-stop$/
const ATTEMPT_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts(?:\/([^/]+)\/evidence(?:\/artifact-window\/([^/]+))?)?$/i
const INPUTS_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts\/([^/]+)\/inputs$/i

const data = new JourneyData(PROJECT)
const lower = (value: string) => value.toLowerCase()

interface AttemptFact {
  Id: string
  RunId: string
  AttemptNumber: number
  Status: string
  AgentRole: string
  AgentProvider: string
  AgentOutcome: string | null
  AgentDispatchedAtUtc: string | null
}

function attemptsOfProject(): AttemptFact[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select a.Id, a.RunId, a.AttemptNumber, a.Status, a.AgentRole, a.AgentProvider, a.AgentOutcome, a.AgentDispatchedAtUtc from attempts a join runs r on r.Id = a.RunId where r.ProjectId = ? order by r.ExecutionNumber, a.AttemptNumber',
        )
        .all(data.projectId()) as unknown as AttemptFact[],
  )
}

function messagesOfAttempt(attemptId: string): number {
  return withJourneyDb(
    (db) => (db.prepare('select count(*) as Count from collaboration_messages where AttemptId = ?').get(attemptId) as { Count: number }).Count,
  )
}

function inputRowCount(attemptId: string): number {
  return withJourneyDb(
    (db) => (db.prepare('select count(*) as Count from attempt_input_messages where AttemptId = ?').get(attemptId) as { Count: number }).Count,
  )
}

function artifactsOfProject(): { AttemptId: string; Purpose: string; ContentHash: string; ByteLength: number; RelativeStoragePath: string }[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select f.AttemptId, f.Purpose, f.ContentHash, f.ByteLength, f.RelativeStoragePath from artifacts f join runs r on r.Id = f.RunId where r.ProjectId = ? order by f.AttemptId, f.Purpose',
        )
        .all(data.projectId()) as unknown as { AttemptId: string; Purpose: string; ContentHash: string; ByteLength: number; RelativeStoragePath: string }[],
  )
}

/** The attempt's stored execution evidence: dispatch marker, process result, timeout (milliseconds) and the two required counts. */
interface ExecutionFacts {
  AgentDispatchedAtUtc: string | null
  AgentProcessOutcome: string | null
  AgentProcessDuration: number | null
  AgentTimeout: number | null
  AgentInputTokens: number | null
  AgentOutputTokens: number | null
}

function executionFactsOf(attemptId: string): ExecutionFacts {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select AgentDispatchedAtUtc, AgentProcessOutcome, AgentProcessDuration, AgentTimeout, AgentInputTokens, AgentOutputTokens from attempts where Id = ?')
        .get(attemptId) as unknown as ExecutionFacts,
  )
}

/** An independent oracle of the cockpit's millisecond wording: below a second, below a minute, then whole minutes and seconds. */
function durationText(milliseconds: number): string {
  if (milliseconds < 1000) {
    return `${milliseconds} ms`
  }
  if (milliseconds < 60_000) {
    return `${(milliseconds / 1000).toFixed(1)} s`
  }
  const seconds = Math.floor(milliseconds / 1000)
  return `${Math.floor(seconds / 60)}m ${String(seconds % 60).padStart(2, '0')}s`
}

function rail(page: Page) {
  return page.getByRole('complementary', { name: 'Usage and evidence' })
}

/** Everything the historical inspection must leave exactly as it was. */
function durableState(repositoryPath: string) {
  return {
    totals: data.projectTotals(),
    runs: data.runs(),
    attempts: attemptsOfProject(),
    artifacts: artifactsOfProject(),
    repository: repositorySnapshot(repositoryPath),
  }
}

test.afterAll(() => {
  // A later journey must never find this journey's script; the double refuses an observation without one.
  endAccountUsageScript()
})

test('the historical attempts journey: stop an attempt before dispatch, record a newer objective, and inspect the older attempt from Run history', async ({
  page,
}) => {
  test.setTimeout(300_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('historical-attempts-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project with a prepared workspace and checkpoint, and the first objective.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(FIRST_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: FIRST_OBJECTIVE })).toBeVisible({ timeout: 15_000 })

  // 2. A failed Agent attempt that emits no collaboration message: the run-scoped account-usage stop, saved through its control, is
  // below at the claim (10) and reached at the pre-dispatch guard (85), so the claimed planning attempt ends without any provider stage.
  const stop = rail(page).getByRole('group', { name: 'Codex account-usage stop' })
  const saved = page.waitForResponse((response) => response.request().method() === 'POST' && STOP_PATH.test(new URL(response.url()).pathname))
  await stop.getByLabel('Codex account-usage stop percentage').fill('80')
  await stop.getByRole('button', { name: 'Save Codex account-usage stop' }).click()
  expect((await saved).status()).toBe(200)
  await expect(stop).toContainText('Current run setting: 80% used', { timeout: 15_000 })
  // The rail's display-only allowance read must settle before the script starts, or it would consume the claim's scripted read.
  await expect(rail(page)).not.toContainText('Codex account usage: loading…', { timeout: 60_000 })
  startAccountUsageScript([{ primary: 10 }, { primary: 85 }])
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Not started: the account-usage stop was reached.')).toBeVisible({ timeout: 60_000 })

  const [olderRun] = data.runs()
  const [stopped] = attemptsOfProject()
  expect(stopped).toMatchObject({ AttemptNumber: 1, Status: 'Failed', AgentRole: 'Planner', AgentProvider: 'Codex', AgentOutcome: 'AccountUsageStopReached' })
  expect(sameId(stopped.RunId, olderRun.Id)).toBe(true)
  expect(stopped.AgentDispatchedAtUtc).toBeNull()
  expect(messagesOfAttempt(stopped.Id)).toBe(0)
  expect(data.collaborationRowsOf(olderRun.Id)).toEqual([])
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe' && entry.kind !== 'account_usage')).toEqual([])
  // The claim sealed the attempt's context manifest before the stop; it is the artifact this journey later reads through the UI.
  const manifests = artifactsOfProject().filter((row) => sameId(row.AttemptId, stopped.Id))
  expect(manifests.map((row) => row.Purpose)).toEqual(['AgentContextManifest'])
  const manifestText = readSealedManifestText(manifests[0].RelativeStoragePath)
  expect(Buffer.byteLength(manifestText, 'utf8')).toBe(manifests[0].ByteLength)

  // 3. The first run is abandoned through its form and a newer objective is recorded through the intake: the live cockpit follows it.
  const abandon = page.getByRole('region', { name: 'Abandon run' })
  await abandon.getByLabel('Reason').fill(REASON)
  await page.getByRole('button', { name: 'Abandon run', exact: true }).click()
  await expect(abandon).toContainText(REASON, { timeout: 30_000 })
  await expect(page.getByRole('textbox', { name: 'Objective' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(SECOND_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  const runs = data.runs()
  expect(runs.map((run) => [run.ExecutionNumber, run.Objective, run.Lifecycle])).toEqual([
    [1, FIRST_OBJECTIVE, 'Abandoned'],
    [2, SECOND_OBJECTIVE, 'Created'],
  ])
  const newerRun = runs[1]

  // Every request the page makes from here on, so the inspection can be proven read-only and addressed to the older run only.
  const requests: { method: string; path: string }[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/')) {
      requests.push({ method: request.method(), path: url.pathname })
    }
  })

  // Opens Run history, selects the older run and inspects its stopped attempt down to the exact sealed manifest bytes, asserting
  // every attempt request it causes. The live cockpit and the current run selection are left as they were.
  const inspectOlderAttempt = async () => {
    const start = requests.length
    const mine = () => requests.slice(start)
    const history = page.getByRole('region', { name: 'Run history' })
    await history.getByRole('button', { name: 'Show run history' }).click()
    await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
    const selectedRun = history.getByRole('region', { name: 'Selected run' })
    await expect(selectedRun).toContainText(FIRST_OBJECTIVE)
    const attempts = selectedRun.getByRole('region', { name: 'Agent attempt history' })
    const show = attempts.getByRole('button', { name: 'Show Agent attempt history' })
    await expect(show).toHaveAttribute('aria-expanded', 'false')
    expect(mine().filter((request) => ATTEMPT_ROUTE.test(request.path))).toEqual([])

    // The older run recorded no collaboration message at all: the stopped attempt is reachable only through the attempt history.
    const collaboration = selectedRun.getByRole('region', { name: 'Recorded collaboration' })
    await collaboration.getByRole('button', { name: 'Show recorded collaboration' }).click()
    await expect(collaboration).toContainText('No collaboration messages have been recorded for this run.', { timeout: 15_000 })

    const listed = page.waitForResponse((response) => ATTEMPT_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
    await show.click()
    const listResponse = await listed
    expect(listResponse.status()).toBe(200)
    expect(lower(new URL(listResponse.url()).pathname)).toBe(`/api/runs/${lower(olderRun.Id)}/agent-attempts`)
    expect(new URL(listResponse.url()).searchParams.get('limit')).toBe('10')
    const rows = attempts.locator('.dc-attempt-history-items > li')
    await expect(rows).toHaveCount(1)
    await expect(rows.first()).toContainText('Attempt #1 · Planner · Codex · Failed · Result: AccountUsageStopReached')

    const evidenceAnswered = page.waitForResponse((response) => /\/agent-attempts\/[^/]+\/evidence$/i.test(new URL(response.url()).pathname), { timeout: 30_000 })
    await attempts.getByRole('button', { name: 'Inspect attempt 1', exact: true }).click()
    const evidenceResponse = await evidenceAnswered
    expect(evidenceResponse.status()).toBe(200)
    expect(lower(new URL(evidenceResponse.url()).pathname)).toBe(`/api/runs/${lower(olderRun.Id)}/agent-attempts/${lower(stopped.Id)}/evidence`)
    const detail: Locator = attempts.getByRole('region', { name: 'Selected Agent attempt evidence' })
    await expect(detail).toContainText('Attempt #1 · Planner · Codex · Failed · Result: AccountUsageStopReached')
    await expect(detail).toContainText(`Context manifest: ${manifests[0].ByteLength} bytes`)
    await expect(detail).toContainText('Historical evidence for a past attempt')
    await expect(detail.getByRole('region', { name: 'Codex account-usage stop' })).toContainText('Claimed with account-usage stop: 80% used')

    // HIST-MET-01: the attempt stopped before dispatch, so its recorded facts are a truthful absence: no measured process result and no
    // usage, never zero, beside its semantic result and its configured timeout, exactly as the host answered from the stored row.
    const stored = executionFactsOf(stopped.Id)
    expect(stored).toMatchObject({ AgentDispatchedAtUtc: null, AgentProcessOutcome: null, AgentProcessDuration: null, AgentInputTokens: null, AgentOutputTokens: null })
    expect(stored.AgentTimeout).toBeGreaterThan(0)
    const answer = (await evidenceResponse.json()) as {
      dispatchedAtUtc?: string | null
      processExecution?: Record<string, unknown> | null
      tokenUsage?: Record<string, unknown> | null
    }
    expect(answer.dispatchedAtUtc ?? null).toBeNull()
    expect(answer.processExecution?.timeoutMilliseconds).toBe(stored.AgentTimeout)
    for (const member of ['outcome', 'exitCode', 'durationMilliseconds']) {
      expect(answer.processExecution?.[member] ?? null).toBeNull()
    }
    for (const member of ['inputTokens', 'outputTokens', 'cacheCreationInputTokens', 'cacheReadInputTokens']) {
      expect(answer.tokenUsage?.[member] ?? null).toBeNull()
    }
    const facts = detail.getByRole('region', { name: 'Recorded execution facts' })
    await expect(facts).toContainText('Response contract: Proposal')
    await expect(facts).toContainText(', not dispatched, ended ')
    await expect(facts.locator('.dc-attempt-history-semantic-result')).toHaveText('Semantic result: AccountUsageStopReached')
    await expect(facts.locator('.dc-process-evidence')).toHaveText('Process not started')
    await expect(facts.locator('.dc-token-usage')).toHaveText('Token usage: none (provider not invoked)')
    await expect(facts.locator('.dc-attempt-history-timeout')).toHaveText(
      `Configured timeout: ${durationText(stored.AgentTimeout!)} (set when the attempt was claimed; not a measurement)`,
    )
    await expect(facts).not.toContainText(/exited|timed out|cancelled|Tokens:|\b0 (ms|input|output)/)

    await detail.getByRole('combobox').selectOption('AgentContextManifest')
    await expect(detail).toContainText('never raw, unexamined provider output')
    expect(mine().filter((request) => /artifact-window/.test(request.path))).toEqual([])
    const windowAnswered = page.waitForResponse((response) => /artifact-window/.test(new URL(response.url()).pathname), { timeout: 30_000 })
    await detail.getByRole('button', { name: 'Load', exact: true }).click()
    const windowResponse = await windowAnswered
    expect(windowResponse.status()).toBe(200)
    expect(lower(new URL(windowResponse.url()).pathname)).toBe(
      `/api/runs/${lower(olderRun.Id)}/agent-attempts/${lower(stopped.Id)}/evidence/artifact-window/context-manifest`,
    )
    // Further windows only on request, until the whole sealed file has been read.
    const nextWindow = detail.getByRole('button', { name: 'Load next window', exact: true })
    const text = detail.locator('pre.dc-artifact-window-text')
    await expect(text).toBeVisible({ timeout: 15_000 })
    while ((await nextWindow.count()) > 0) {
      const before = (await text.textContent()) ?? ''
      await nextWindow.click()
      await expect.poll(async () => ((await text.textContent()) ?? '').length, { timeout: 15_000 }).toBeGreaterThan(before.length)
    }
    expect(await text.textContent()).toBe(manifestText)

    // HIST-INP-01: the stopped planner emitted no message and recorded no input row, so its recorded inputs are the genuine empty set,
    // read once only when opened, through the older run and this attempt, and shown without a list beside the loaded manifest.
    expect(inputRowCount(stopped.Id)).toBe(0)
    const inputs = detail.getByRole('region', { name: 'Recorded collaboration inputs' })
    const inputsToggle = inputs.getByRole('button', { name: 'Recorded collaboration inputs', exact: true })
    await expect(inputsToggle).toHaveAttribute('aria-expanded', 'false')
    expect(mine().filter((request) => INPUTS_ROUTE.test(request.path))).toEqual([])
    const inputsAnswered = page.waitForResponse((response) => INPUTS_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
    await inputsToggle.click()
    const inputsResponse = await inputsAnswered
    expect(inputsResponse.status()).toBe(200)
    expect(lower(new URL(inputsResponse.url()).pathname)).toBe(`/api/runs/${lower(olderRun.Id)}/agent-attempts/${lower(stopped.Id)}/inputs`)
    expect(await inputsResponse.json()).toMatchObject({
      status: 'Empty',
      attempt: { attemptNumber: 1, role: 'Planner', provider: 'Codex', responseContract: 'Proposal' },
      inputMessages: [],
      inputMessageTotalCount: 0,
      inputMessagesOmitted: false,
    })
    await expect(inputs).toContainText('No recorded collaboration inputs — this attempt started from none.')
    await expect(inputs.locator('li')).toHaveCount(0)
    expect(await text.textContent()).toBe(manifestText)
    for (const request of mine().filter((entry) => INPUTS_ROUTE.test(entry.path))) {
      const [, runId, attemptId] = INPUTS_ROUTE.exec(request.path)!
      expect(request.method).toBe('GET')
      expect(sameId(runId, olderRun.Id)).toBe(true)
      expect(sameId(attemptId, stopped.Id)).toBe(true)
    }

    // Every attempt request named the older run and the selected attempt; nothing else was requested for the attempt, and the newer run
    // was never addressed by the inspection.
    const attemptRequests = mine().filter((entry) => ATTEMPT_ROUTE.test(entry.path))
    expect(attemptRequests.length).toBeGreaterThanOrEqual(3)
    for (const request of attemptRequests) {
      const [, runId, attemptId] = ATTEMPT_ROUTE.exec(request.path)!
      expect(request.method).toBe('GET')
      expect(sameId(runId, olderRun.Id)).toBe(true)
      expect(attemptId === undefined || sameId(attemptId, stopped.Id)).toBe(true)
    }
    expect(attemptRequests.some((request) => request.path.toLowerCase().includes(lower(newerRun.Id)))).toBe(false)
    expect(mine().filter((request) => /\/collaboration-messages\/|\/collaboration-history\/[^/]+\/attempt$/.test(request.path))).toEqual([])
  }

  // Reloads the page and selects the project again; the live cockpit (which makes its own display-only reads) settles before a snapshot.
  const reloadAndSettle = async () => {
    await page.reload()
    await selectProject(page, PROJECT)
    await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 30_000 })
    await expect(rail(page)).not.toContainText('Codex account usage: loading…', { timeout: 60_000 })
  }

  // Inspects after the given preparation and proves that the inspection itself added no provider invocation, no non-GET request and no
  // durable change, and left the live cockpit on the newer run.
  const inspectReadOnly = async () => {
    const invocationsBefore = readInvocations(mark).length
    const stateBefore = durableState(repository.path)
    const requestsBefore = requests.length

    await inspectOlderAttempt()

    const added = requests.slice(requestsBefore)
    expect(added.filter((request) => request.method !== 'GET')).toEqual([])
    expect(readInvocations(mark)).toHaveLength(invocationsBefore)
    expect(durableState(repository.path)).toEqual(stateBefore)
    await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible()
    await expect(page.getByRole('heading', { name: FIRST_OBJECTIVE })).toHaveCount(0)
    await expect(page.getByRole('button', { name: new RegExp(`^${PROJECT}`) })).toHaveAttribute('data-selected', 'true')
  }

  // 4. Inspect the older attempt from Run history, then again after a reload: the recorded facts stay reachable and nothing changes.
  await reloadAndSettle()
  await inspectReadOnly()
  await reloadAndSettle()
  await inspectReadOnly()

  // The newer run is untouched: it has no attempt, and the current cockpit still offers its own normal controls.
  expect(attemptsOfProject().filter((row) => sameId(row.RunId, newerRun.Id))).toEqual([])
  await expect(page.getByRole('button', { name: 'Request Codex plan' })).toBeVisible()
})
