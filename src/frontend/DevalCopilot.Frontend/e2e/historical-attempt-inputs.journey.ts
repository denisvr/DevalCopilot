import { expect, test } from '@playwright/test'
import type { Locator } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, markInvocations, readInvocations, readLaunchTargetVerdict, repositorySnapshot } from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of HIST-INP-01 (ADR-0051): an owner opens "Recorded collaboration inputs" inside an OLDER attempt selected in
// Run history and sees its durable input references. Everything runs through the rendered controls of the production frontend over the
// generated client, real MVC authentication, mediator, SQLite and all supervisors; only the provider executables are the deterministic
// owned doubles. The scene is produced by existing supported behavior only: a Codex plan, a Claude critical review that challenges it and
// a Codex resolution, whose claims record their real input sets; then the run is abandoned through its form and a newer objective is
// recorded through the intake. The database, the source repository and the doubles' invocation log are only READ, never seeded.
//
// It proves: after a reload, with the newer run still selected, the resolution attempt's inputs are read once opened through exactly the
// older run and that attempt, and the shown positions, types, message sequences, times, identities and count equal the stored rows; the
// review attempt's single input and the planner's genuine empty set are shown the same way; and the inspection adds no provider
// invocation, no non-GET request, no artifact-window read and no durable or source change. Malformed, oversized and non-Planner empty
// sets are proven by the SQLite, API and frontend tests, not here. It is not the complete prompt, replay or session recovery.

const PROJECT = 'Historical attempt inputs journey fixture'
const OBJECTIVE = 'Plan the ledger total and review the plan'
const NEWER_OBJECTIVE = 'Record a newer objective after the reviewed plan'
const REASON = 'The reviewed plan is superseded by a newer objective.'
const INPUTS_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts\/([^/]+)\/inputs$/i
const TYPE_LABELS: Record<string, string> = { Proposal: 'Proposal', Challenge: 'Challenge' }

const data = new JourneyData(PROJECT)
const lower = (value: string) => value.toLowerCase()

interface StoredAttempt {
  Id: string
  RunId: string
  AttemptNumber: number
  AgentRole: string
  AgentProvider: string
  AgentResponseContract: string
}

interface StoredInput {
  Position: number
  MessageId: string
  Type: string
  MessageSequence: number
  OccurredAtUtc: string
  MessageRunId: string
}

function attemptsOfProject(): StoredAttempt[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select a.Id, a.RunId, a.AttemptNumber, a.AgentRole, a.AgentProvider, a.AgentResponseContract from attempts a join runs r on r.Id = a.RunId where r.ProjectId = ? order by r.ExecutionNumber, a.AttemptNumber',
        )
        .all(data.projectId()) as unknown as StoredAttempt[],
  )
}

/** The attempt's stored input rows joined to the messages they reference, in stored order (the oracle for what the page shows). */
function storedInputsOf(attemptId: string): StoredInput[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select i.Sequence as Position, m.Id as MessageId, m.Type, m.Sequence as MessageSequence, m.OccurredAtUtc, m.RunId as MessageRunId from attempt_input_messages i join collaboration_messages m on m.Id = i.CollaborationMessageId where i.AttemptId = ? order by i.Sequence',
        )
        .all(attemptId) as unknown as StoredInput[],
  )
}

function inputRowCount(attemptId: string): number {
  return withJourneyDb(
    (db) => (db.prepare('select count(*) as Count from attempt_input_messages where AttemptId = ?').get(attemptId) as { Count: number }).Count,
  )
}

/** The host's invariant UTC round-trip text of a stored instant (seven fractional digits and Z); the stored form is checked, not assumed. */
function roundTripOf(stored: string): string {
  const parts = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(stored)
  expect(parts, `unexpected stored instant ${stored}`).not.toBeNull()
  return `${parts![1]}T${parts![2]}.${(parts![3] ?? '').padEnd(7, '0')}Z`
}

/** Every durable row of this project and the source repository, which the inspection must leave exactly as they were. */
function durableState(repositoryPath: string) {
  const projectRows = (table: string) =>
    withJourneyDb((db) => db.prepare(`select t.* from ${table} t join runs r on r.Id = t.RunId where r.ProjectId = ? order by t.Id`).all(data.projectId()))
  return {
    totals: data.projectTotals(),
    runs: data.runs(),
    attempts: projectRows('attempts'),
    messages: projectRows('collaboration_messages'),
    artifacts: projectRows('artifacts'),
    inputs: withJourneyDb((db) =>
      db
        .prepare('select i.* from attempt_input_messages i join attempts a on a.Id = i.AttemptId join runs r on r.Id = a.RunId where r.ProjectId = ? order by i.Id')
        .all(data.projectId()),
    ),
    repository: repositorySnapshot(repositoryPath),
  }
}

test('the historical attempt inputs journey: plan, review and resolve, record a newer objective, and inspect the older attempts inputs', async ({ page }) => {
  test.setTimeout(300_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('historical-attempt-inputs-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project with a prepared workspace and checkpoint, an objective, a Codex plan, a Claude review that challenges it and a
  // Codex resolution: each claim records its own input set.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Plan proposed.')).toBeVisible({ timeout: 60_000 })
  await page.getByRole('button', { name: 'Request Claude review' }).click()
  await expect(page.getByText('Last attempt #2: Proposal challenged.')).toBeVisible({ timeout: 60_000 })
  await page.getByRole('button', { name: 'Resolve challenges with Codex' }).click()
  await expect(page.getByText('Last attempt #3: Challenges resolved.')).toBeVisible({ timeout: 60_000 })

  const olderRunId = data.runId()
  const [planner, reviewer, resolver] = attemptsOfProject()
  expect([planner, reviewer, resolver].map((attempt) => [attempt.AttemptNumber, attempt.AgentRole, attempt.AgentProvider, attempt.AgentResponseContract])).toEqual([
    [1, 'Planner', 'Codex', 'Proposal'],
    [2, 'CriticalReviewer', 'ClaudeCode', 'CriticalReview'],
    [3, 'Resolver', 'Codex', 'ChallengeResolution'],
  ])
  // The genuine recorded sets: none for the planner, the reviewed Proposal for the review, and that Proposal followed by every Challenge
  // for the resolution, each referencing a message of this same run.
  expect(inputRowCount(planner.Id)).toBe(0)
  const reviewerInputs = storedInputsOf(reviewer.Id)
  const resolverInputs = storedInputsOf(resolver.Id)
  expect(reviewerInputs.map((row) => row.Type)).toEqual(['Proposal'])
  expect(resolverInputs.length).toBeGreaterThanOrEqual(2)
  expect(resolverInputs.map((row) => row.Type)).toEqual(['Proposal', ...Array(resolverInputs.length - 1).fill('Challenge')])
  expect(resolverInputs.map((row) => row.Position)).toEqual(resolverInputs.map((_, index) => index))
  for (const row of [...reviewerInputs, ...resolverInputs]) {
    expect(sameId(row.MessageRunId, olderRunId)).toBe(true)
  }
  expect(inputRowCount(resolver.Id)).toBe(resolverInputs.length)

  // 2. The run is abandoned through its form and a newer objective is recorded through the intake: the live cockpit follows it.
  const abandon = page.getByRole('region', { name: 'Abandon run' })
  await abandon.getByLabel('Reason').fill(REASON)
  await page.getByRole('button', { name: 'Abandon run', exact: true }).click()
  await expect(abandon).toContainText(REASON, { timeout: 30_000 })
  await expect(page.getByRole('textbox', { name: 'Objective' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(NEWER_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: NEWER_OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  const runs = data.runs()
  expect(runs.map((run) => [run.ExecutionNumber, run.Objective, run.Lifecycle])).toEqual([
    [1, OBJECTIVE, 'Abandoned'],
    [2, NEWER_OBJECTIVE, 'Created'],
  ])
  expect(sameId(runs[0].Id, olderRunId)).toBe(true)
  const newerRunId = runs[1].Id

  // 3. A reload, then the project is selected again; the live cockpit (which makes its own display-only reads) settles before a snapshot.
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: NEWER_OBJECTIVE })).toBeVisible({ timeout: 30_000 })

  const requests: { method: string; path: string }[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/')) {
      requests.push({ method: request.method(), path: url.pathname })
    }
  })
  const invocationsBefore = readInvocations(mark).length
  const stateBefore = durableState(repository.path)

  const history = page.getByRole('region', { name: 'Run history' })
  await history.getByRole('button', { name: 'Show run history' }).click()
  await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
  const selectedRun = history.getByRole('region', { name: 'Selected run' })
  await expect(selectedRun).toContainText(OBJECTIVE)
  const attempts = selectedRun.getByRole('region', { name: 'Agent attempt history' })
  await attempts.getByRole('button', { name: 'Show Agent attempt history' }).click()
  await expect(attempts.locator('.dc-attempt-history-items > li')).toHaveCount(3, { timeout: 15_000 })

  /** Selects the attempt, opens its recorded inputs once and returns the host's answer and the opened section. */
  const openInputs = async (attempt: StoredAttempt): Promise<{ answer: Record<string, unknown>; section: Locator }> => {
    await attempts.getByRole('button', { name: `Inspect attempt ${attempt.AttemptNumber}`, exact: true }).click()
    const detail = attempts.getByRole('region', { name: 'Selected Agent attempt evidence' })
    await expect(detail).toContainText(`Attempt #${attempt.AttemptNumber} · ${attempt.AgentRole} · ${attempt.AgentProvider}`, { timeout: 15_000 })
    const section = detail.getByRole('region', { name: 'Recorded collaboration inputs' })
    const toggle = section.getByRole('button', { name: 'Recorded collaboration inputs', exact: true })
    // Collapsed, and nothing read for it, until the owner opens it.
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(requests.filter((request) => INPUTS_ROUTE.test(request.path) && lower(request.path).includes(lower(attempt.Id)))).toEqual([])
    const answered = page.waitForResponse((response) => INPUTS_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
    await toggle.click()
    const response = await answered
    expect(response.status()).toBe(200)
    expect(lower(new URL(response.url()).pathname)).toBe(`/api/runs/${lower(olderRunId)}/agent-attempts/${lower(attempt.Id)}/inputs`)
    await expect(toggle).toHaveAttribute('aria-expanded', 'true')
    await expect(section).toContainText('never the complete prompt, complete context manifest, or a resumable provider session')
    return { answer: (await response.json()) as Record<string, unknown>, section }
  }

  /** The host's answer and the page equal the stored rows: count, order, positions, types, message sequences, times and identities. */
  const expectRecorded = async (attempt: StoredAttempt, stored: StoredInput[]) => {
    const { answer, section } = await openInputs(attempt)
    expect(answer).toMatchObject({
      status: 'Recorded',
      inputMessageTotalCount: stored.length,
      inputMessagesOmitted: false,
      attempt: {
        attemptNumber: attempt.AttemptNumber,
        role: attempt.AgentRole,
        provider: attempt.AgentProvider,
        responseContract: attempt.AgentResponseContract,
      },
    })
    expect(sameId(answer.runId as string, olderRunId)).toBe(true)
    expect(sameId(answer.attemptId as string, attempt.Id)).toBe(true)
    const entries = answer.inputMessages as Record<string, unknown>[]
    expect(entries.map((entry) => [entry.sequence, lower(entry.collaborationMessageId as string), entry.type, entry.collaborationSequence, entry.occurredAtUtc])).toEqual(
      stored.map((row) => [row.Position, lower(row.MessageId), row.Type, String(row.MessageSequence), roundTripOf(row.OccurredAtUtc)]),
    )

    await expect(section).toContainText(stored.length === 1 ? '1 recorded input.' : `${stored.length} recorded inputs.`)
    const items = section.locator('.dc-attempt-recorded-input-items > li')
    await expect(items).toHaveText(
      stored.map(
        (row) =>
          `Input ${row.Position + 1}: ${TYPE_LABELS[row.Type]} · message sequence ${row.MessageSequence} · recorded ${roundTripOf(row.OccurredAtUtc)} · message ${lower(row.MessageId)}`,
      ),
    )
  }

  // 4. The resolution attempt's Proposal and Challenges, the review attempt's single reviewed Proposal and the planner's genuine empty set.
  await expectRecorded(resolver, resolverInputs)
  await expectRecorded(reviewer, reviewerInputs)
  const empty = await openInputs(planner)
  expect(empty.answer).toMatchObject({ status: 'Empty', inputMessageTotalCount: 0, inputMessages: [], inputMessagesOmitted: false })
  await expect(empty.section).toContainText('No recorded collaboration inputs — this attempt started from none.')

  // Every inputs request named the older run and an attempt of it, never the newer run; nothing else changed or was invoked, no artifact
  // text was read, and the newer run stays selected in the live cockpit.
  const inputRequests = requests.filter((request) => INPUTS_ROUTE.test(request.path))
  expect(inputRequests.length).toBeGreaterThanOrEqual(3)
  for (const request of inputRequests) {
    const [, requestedRun, requestedAttempt] = INPUTS_ROUTE.exec(request.path)!
    expect(request.method).toBe('GET')
    expect(sameId(requestedRun, olderRunId)).toBe(true)
    expect([planner, reviewer, resolver].some((attempt) => sameId(attempt.Id, requestedAttempt))).toBe(true)
  }
  expect(inputRequests.some((request) => lower(request.path).includes(lower(newerRunId)))).toBe(false)
  expect(requests.filter((request) => /artifact-window|\/collaboration-messages\//.test(request.path))).toEqual([])
  expect(requests.filter((request) => request.method !== 'GET')).toEqual([])
  expect(readInvocations(mark)).toHaveLength(invocationsBefore)
  expect(durableState(repository.path)).toEqual(stateBefore)
  await expect(page.getByRole('heading', { name: NEWER_OBJECTIVE, exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: OBJECTIVE, exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: new RegExp(`^${PROJECT}`) })).toHaveAttribute('data-selected', 'true')
  expect(attemptsOfProject().filter((attempt) => sameId(attempt.RunId, newerRunId))).toEqual([])
})
