import { expect, test } from '@playwright/test'
import { ApiException, RequestCodexPlanningAttemptEndpointClient } from '../src/api/generated/api-client'
import { JourneyData } from './journey/journeyDb'
import {
  JOURNEY_API_URL,
  createJourneyRepository,
  journeySecret,
  markInvocations,
  readInvocations,
  readLaunchTargetVerdict,
  repositorySnapshot,
} from './journey/journeyEnv'
import { agentStageInvocations, observationIndices } from './journey/accountUsage'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the owner-selected immutable run budgets at manual intake (ADR-0028), through the rendered intake form
// of the production frontend over the real generated client, MVC authentication, mediator, SQLite and ALL supervisors. Only the provider
// executables are deterministic owned doubles; no real provider runs, and the run is created through the rendered form (no raw SQL
// substitute). The database and the doubles' allowlisted invocation log are only READ.
//
// A 9-minute reservation is smaller than the Codex planning timeout, so the first planning claim cannot fit: the page withholds the
// request control with the time-fit block, and an EXPLICIT request through the generated client is refused by the host before any
// probe, evidence capture, attempt, slot or provider invocation. This proves the assembled enforcement of the chosen ceilings; it is
// not provider reliability, measured elapsed time, or proof that any larger ceiling lets an objective finish.

const PROJECT = 'Manual budgets journey fixture'
const OBJECTIVE = 'Plan the ledger total under chosen run budgets'
const MANUAL_PATH = '/api/runs/manual'
const CHOSEN_CLAIMS = 4
const CHOSEN_MINUTES = 9
const MINUTE_TICKS = 60 * 10_000_000

const data = new JourneyData(PROJECT)

const authorizedHttp = {
  fetch(url: RequestInfo, init?: RequestInit): Promise<Response> {
    const headers = new Headers(init?.headers)
    headers.set('Authorization', `Bearer ${journeySecret()}`)
    return fetch(url, { ...init, headers })
  },
}

test('the manual budgets journey: choose smaller budgets in the form, confirm them after reload, and refuse a planning claim that cannot fit', async ({
  page,
}) => {
  test.setTimeout(240_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('manual-budgets-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project with a prepared workspace and checkpoint, so that only the chosen budget can refuse the planning claim.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  const sourceBefore = repositorySnapshot(repository.path)

  // 2. The form starts at the defaults and states what the ceilings do and do not mean.
  const claims = page.getByRole('spinbutton', { name: 'Agent claim ceiling' })
  const minutes = page.getByRole('spinbutton', { name: 'Reserved invocation minutes' })
  await expect(claims).toHaveValue('16')
  await expect(minutes).toHaveValue('120')
  await expect(page.getByTestId('run-intake-budget-note')).toContainText('not measured elapsed time')

  // 3. Choose smaller budgets through the rendered form: the generated client sends the exact numbers and the host accepts them.
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await claims.fill(String(CHOSEN_CLAIMS))
  await minutes.fill(String(CHOSEN_MINUTES))
  const created = page.waitForResponse(
    (response) => response.request().method() === 'POST' && new URL(response.url()).pathname === MANUAL_PATH,
    { timeout: 30_000 },
  )
  await page.getByRole('button', { name: 'Record manual run' }).click()
  const answer = await created
  expect(answer.status()).toBe(200)
  const sent = answer.request().postDataJSON() as Record<string, unknown>
  expect(sent.maximumAgentAttempts).toBe(CHOSEN_CLAIMS)
  expect(sent.maximumAgentInvocationMinutes).toBe(CHOSEN_MINUTES)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })

  // 4. The cockpit shows the actual immutable ceilings before any claim, and the host stored exactly those numbers.
  const budgetFacts = async () => {
    await expect(page.getByText('Agent claims: 0 of 4 used.')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByText(/Reserved Agent invocation time: 0 min of 9 min \(9 min remaining\)/)).toBeVisible()
    await expect(page.getByText('Blocked: insufficient reserved invocation time remains for this specific request.').first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Request Codex plan' })).toHaveCount(0)
  }
  await budgetFacts()
  expect(data.runBudgets()).toEqual({ MaximumAgentAttempts: CHOSEN_CLAIMS, MaximumAgentInvocationTime: CHOSEN_MINUTES * MINUTE_TICKS })
  expect(data.attempts()).toEqual([])

  // 5. A reload shows the same recorded values.
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  await budgetFacts()
  expect(data.runBudgets()).toEqual({ MaximumAgentAttempts: CHOSEN_CLAIMS, MaximumAgentInvocationTime: CHOSEN_MINUTES * MINUTE_TICKS })

  // 6. An explicit planning request through the generated client over real HTTP is refused by the host: no attempt, no slot, no Agent
  // stage and no account-usage observation of the double, and the chosen ceilings and their usage are unchanged.
  const observationsBefore = observationIndices(readInvocations(mark)).length
  const stagesBefore = agentStageInvocations(readInvocations(mark)).length
  const planning = new RequestCodexPlanningAttemptEndpointClient(JOURNEY_API_URL, authorizedHttp)
  let refusal: ApiException | null = null
  try {
    await planning.requestCodexPlanningAttempt(data.runId())
  } catch (caught) {
    if (ApiException.isApiException(caught)) {
      refusal = caught
    } else {
      throw new Error('Expected an ApiException from the generated client.')
    }
  }
  expect(refusal).not.toBeNull()
  expect(refusal!.status).toBe(409)
  expect(refusal!.response).toContain('agent_attempts.time_budget_exceeded')
  expect(data.attempts()).toEqual([])
  expect(agentStageInvocations(readInvocations(mark)).length).toBe(stagesBefore)
  expect(observationIndices(readInvocations(mark)).length).toBe(observationsBefore)
  await page.reload()
  await selectProject(page, PROJECT)
  await budgetFacts()
  expect(data.runBudgets()).toEqual({ MaximumAgentAttempts: CHOSEN_CLAIMS, MaximumAgentInvocationTime: CHOSEN_MINUTES * MINUTE_TICKS })

  // The source repository was never touched by any of it.
  expect(repositorySnapshot(repository.path)).toEqual(sourceBefore)
})
