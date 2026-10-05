import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { JourneyData } from './journey/journeyDb'
import { createJourneyRepository, journeyRoot, markInvocations, readInvocations, readLaunchTargetVerdict, repositorySnapshot } from './journey/journeyEnv'
import {
  agentStageInvocations,
  endAccountUsageScript,
  observationIndices,
  startAccountUsageScript,
  stoppedAttemptProblems,
} from './journey/accountUsage'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the run-scoped Codex account-usage stop (ADR-0025), through the rendered controls of the production
// frontend over the real generated client, MVC authentication, mediator, SQLite, ALL supervisors, the real strict account-usage
// observation (a real child process speaking the closed App Server contract), the real Codex adapters, process executor and Git
// evidence. Only the provider executables are deterministic owned doubles (tests/DevalCopilot.Api.IntegrationTests/BrowserJourney):
// the Codex double's closed App Server mode answers the observations from a script THIS journey writes into the owned root, one read
// per launch. No real provider and no raw SQL shortcut configures the policy: the setting is saved through the rendered control; the
// database and the doubles' log are only READ. This proves the assembled local workflow, not provider reliability, and not that an
// account stayed below any threshold after the observation.

const PROJECT = 'Account-usage stop journey fixture'
const OBJECTIVE = 'Plan the ledger total under an account-usage stop'
const STOP_PATH = /\/api\/runs\/[^/]+\/codex-account-usage-stop$/
const REFUSAL = "This run's configured Codex account-usage stop was reached, so no Codex attempt was started."

const data = new JourneyData(PROJECT)
let mark = 0

const events = () => readInvocations(mark)
const stages = () => agentStageInvocations(events())

function rail(page: Page) {
  return page.getByRole('complementary', { name: 'Usage and evidence' })
}

function control(page: Page) {
  return rail(page).getByRole('group', { name: 'Codex account-usage stop' })
}

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
  return { evidence: await response.json(), detail }
}

test.afterAll(() => {
  // A later journey must never find this journey's script; the double refuses an observation without one.
  endAccountUsageScript()
})

test('the account-usage stop journey: configure, refuse a reaching request, stop before dispatch, run an allowed request, then disable', async ({
  page,
}) => {
  test.setTimeout(240_000)
  mark = markInvocations()
  const repository = createJourneyRepository('account-usage-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project, prepared workspace and checkpoint, and one recorded ManualAgent objective: nothing configured yet.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  // Preparing the workspace adds its own branch to the repository; the stop and everything after it must change nothing further.
  const sourceBefore = repositorySnapshot(repository.path)
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  expect(data.attempts()).toEqual([])

  // 2. Configure the stop through the rendered control, over the generated client: the host accepts it with HTTP 200, stores the exact
  // integer and records one human event, and the control states a threshold for a local guard, never an eligibility or capacity.
  const stop = control(page)
  await expect(stop).toBeVisible()
  await expect(stop).toContainText('Current run setting: Not configured')
  expect(data.runAccountUsageStop()).toBeNull()
  const saved = page.waitForResponse((response) => response.request().method() === 'POST' && STOP_PATH.test(new URL(response.url()).pathname))
  await stop.getByLabel('Codex account-usage stop percentage').fill('80')
  await stop.getByRole('button', { name: 'Save Codex account-usage stop' }).click()
  const answer = await saved
  expect(answer.status()).toBe(200)
  expect(await answer.json()).toEqual({ percent: 80 })
  await expect(stop).toContainText('Current run setting: 80% used', { timeout: 15_000 })
  expect(data.runAccountUsageStop()).toBe(80)
  expect(data.accountUsageStopEvents()).toEqual(['{"percent":80}'])
  // The control's own note names what it does not claim; only claims outside that disclaimer count.
  const text = (await stop.innerText()).toLowerCase().replace('not account access, remaining quota or live capacity', '')
  expect(text).toContain('local guard')
  for (const claim of ['eligible', 'remaining quota', 'capacity available', 'safe to invoke']) {
    expect(text).not.toContain(claim)
  }

  // The scripted reads of the Codex double, one per observation of this scenario: the claim of request 1 sees a window above the stop;
  // request 2 is below at its claim and at the stop (equality is not needed: 85 is above) at its pre-dispatch guard; request 3 is below
  // at both seams and every later read repeats this last one.
  // The display-only allowance read of the rail is made once when the rail mounts, through the same double. It must have settled before
  // the script starts, or it would consume the first scripted read and make the claim's observation index depend on timing.
  await expect(rail(page)).not.toContainText('Codex account usage: loading…', { timeout: 60_000 })
  startAccountUsageScript([{ primary: 90 }, { primary: 10, secondary: 5 }, { primary: 85, secondary: 20 }, { primary: 10 }])

  // 3. A request whose claim-time observation reaches the stop is refused: no attempt, no number, no slot, no provider invocation.
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText(REFUSAL)).toBeVisible({ timeout: 30_000 })
  expect(data.attempts()).toEqual([])
  expect(observationIndices(events())).toEqual([0])
  expect(stages()).toEqual([])

  // 4. Below at the claim, then at the stop at the pre-dispatch guard: the claimed attempt is terminal without a dispatch marker or any
  // provider invocation, the budget slot stays spent, and the page states the outcome truthfully.
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Not started: the account-usage stop was reached.')).toBeVisible({ timeout: 60_000 })
  expect(observationIndices(events())).toEqual([0, 1, 2])
  expect(stages()).toEqual([])
  const [stopped] = data.attempts()
  expect(stopped).toMatchObject({ AttemptNumber: 1, Status: 'Failed', AgentOutcome: 'AccountUsageStopReached', AgentBudgetSlot: 1 })

  const inspected = await inspectAttempt(page, 1)
  const facts = data.accountUsageByAttempt()[0]
  const wire = inspected.evidence as {
    attemptStatus: string
    outcome: string | null
    dispatchedAtUtc: string | null
    accountUsageStop: { state: string; percent: number | null } | null
    accountUsageDecision: {
      state: string
      decision: string | null
      reason: string | null
      thresholdPercent: number | null
      retrievedAtUtc: string | null
      windows: { bucketId: string | null; window: string; usedPercent: number }[]
    } | null
  }
  const problems = stoppedAttemptProblems(
    {
      attemptNumber: 1,
      threshold: 80,
      windows: [
        { bucketId: 'codex', window: 'Primary', usedPercent: 85 },
        { bucketId: 'codex', window: 'Secondary', usedPercent: 20 },
      ],
    },
    {
      status: wire.attemptStatus,
      outcome: wire.outcome,
      dispatchedAtUtc: wire.dispatchedAtUtc,
      storedThreshold: facts.AgentCodexAccountUsageStopPercent,
      storedDecision: facts.AgentAccountUsageDecisionSnapshot,
      stop: wire.accountUsageStop,
      decision: wire.accountUsageDecision,
      renderedText: await inspected.detail.getByRole('region', { name: 'Codex account-usage stop' }).innerText(),
    },
  )
  expect(problems).toEqual([])
  expect(facts.AgentDispatchedAtUtc).toBeNull()

  // No polling retry and no silent restart of the stopped attempt: after several supervisor polls nothing new was observed or invoked,
  // and the terminal outcome and its decision survive a reload of the page.
  await page.waitForTimeout(2_500)
  expect(observationIndices(events())).toEqual([0, 1, 2])
  expect(stages()).toEqual([])
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByText('Last attempt #1: Not started: the account-usage stop was reached.')).toBeVisible({ timeout: 30_000 })
  await expect(control(page)).toContainText('Current run setting: 80% used')
  // Remounting the rail makes display-only allowance reads through the same double (the guard never makes them); let them settle and
  // count only what a request adds from here on.
  await expect(rail(page)).not.toContainText('Codex account usage: loading…', { timeout: 60_000 })
  const before = observationIndices(events()).length
  expect(before).toBeGreaterThanOrEqual(3)

  // 5. A later request that stays below the stop at both seams is claimed with the same snapshot and runs through the real adapter once.
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #2: Plan proposed.')).toBeVisible({ timeout: 60_000 })
  expect(observationIndices(events())).toEqual(Array.from({ length: before + 2 }, (_, index) => index))
  expect(stages().map((entry) => entry.contract)).toEqual(['Proposal'])
  const attempts = data.attempts()
  expect(attempts.map((row) => [row.AttemptNumber, row.AgentOutcome])).toEqual([
    [1, 'AccountUsageStopReached'],
    [2, 'Proposed'],
  ])
  expect(attempts.map((row) => row.AgentBudgetSlot)).toEqual([1, 2])
  const allowed = data.accountUsageByAttempt()[1]
  expect(allowed.AgentCodexAccountUsageStopPercent).toBe(80)
  expect(allowed.AgentDispatchedAtUtc).not.toBeNull()
  expect(allowed.AgentAccountUsageDecisionSnapshot).toBeNull()
  const allowedEvidence = await inspectAttempt(page, 2)
  expect(allowedEvidence.evidence.accountUsageDecision).toBeNull()
  await expect(allowedEvidence.detail.getByRole('region', { name: 'Codex account-usage stop' })).toContainText('Claimed with account-usage stop: 80% used')
  await expect(allowedEvidence.detail.getByRole('region', { name: 'Codex account-usage stop' })).not.toContainText('Not started')

  // 6. Claude is unaffected by the stop, and clearing it through the rendered control disables the guard: the next Codex stage makes no
  // observation at all.
  await page.getByRole('button', { name: 'Request Claude review' }).click()
  await expect(page.getByText('Last attempt #3: Proposal challenged.')).toBeVisible({ timeout: 60_000 })
  expect(observationIndices(events())).toEqual(Array.from({ length: before + 2 }, (_, index) => index))
  const cleared = page.waitForResponse((response) => response.request().method() === 'POST' && STOP_PATH.test(new URL(response.url()).pathname))
  await control(page).getByRole('button', { name: 'Clear Codex account-usage stop' }).click()
  expect((await cleared).status()).toBe(200)
  await expect(control(page)).toContainText('Current run setting: Not configured', { timeout: 15_000 })
  expect(data.runAccountUsageStop()).toBeNull()
  expect(data.accountUsageStopEvents()).toEqual(['{"percent":80}', '{"percent":null}'])
  await page.getByRole('button', { name: 'Resolve challenges with Codex' }).click()
  await expect(page.getByText('Last attempt #4: Challenges resolved.')).toBeVisible({ timeout: 60_000 })
  expect(observationIndices(events())).toEqual(Array.from({ length: before + 2 }, (_, index) => index))
  expect(data.accountUsageByAttempt()[3].AgentCodexAccountUsageStopPercent).toBeNull()
  expect(stages().map((entry) => entry.contract)).toEqual(['Proposal', 'CriticalReview', 'ChallengeResolution'])

  // The source repository was never touched by any of it.
  expect(repositorySnapshot(repository.path)).toEqual(sourceBefore)
  expect(journeyRoot().root.length).toBeGreaterThan(0)
})
