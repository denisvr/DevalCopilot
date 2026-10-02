import { expect, test as base } from '@playwright/test'
import type { Page } from '@playwright/test'
import { TEST_LAUNCH_SECRET } from '../playwright.config'
import { CockpitGate, provideCockpitGate } from './harness/cockpitGate'
import { createFixtureRepository, injectTestSession, readRunSummaries, registerProjectViaUi, selectProject } from './support'

// Two owned projects with their own manual runs, selected explicitly, against the real host and the real frontend over its
// generated client and normal authentication. The authenticated cockpit responses are real: the page's own request is
// forwarded to the host and only the delivery of its answer is held or replaced, so the frames that the selection commits while a
// response is outstanding are the ones a user would see. No provider is ever started.

const ALPHA = 'Selection fixture alpha'
const BRAVO = 'Selection fixture bravo'
const OBJECTIVE_ALPHA = 'Rotate the alpha signing keys before the audit'
const OBJECTIVE_BRAVO = 'Compact the bravo archive index nightly'

// The gate has an explicit shutdown lifecycle (see harness/cockpitGate.ts). It is provided as a fixture that depends on `page`, so
// its teardown — release every hold, unregister interception, await the active handlers — runs before the page and context are
// closed, whether the test passed or failed.
const test = base.extend<{ gate: CockpitGate }>({
  gate: async ({ page }, use) => {
    await provideCockpitGate(page, use)
  },
})

async function recordManualRun(page: Page, objective: string) {
  await page.getByRole('textbox', { name: 'Objective' }).fill(objective)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: objective })).toBeVisible({ timeout: 15_000 })
}

async function watchForLeaks(page: Page, leaked: string, whileSelected: string) {
  await page.evaluate(
    ([text, project]) => {
      const w = window as unknown as { __leaks: string[] }
      w.__leaks = []
      new MutationObserver(() => {
        const chip = Array.from(document.querySelectorAll('button.dc-project-chip')).find((node) =>
          node.textContent?.startsWith(project),
        )
        if (chip?.getAttribute('data-selected') === 'true' && document.body.textContent?.includes(text)) {
          w.__leaks.push(text)
        }
      }).observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true })
    },
    [leaked, whileSelected],
  )
}

async function leaks(page: Page) {
  return page.evaluate(() => (window as unknown as { __leaks: string[] }).__leaks.length)
}

test('selecting another project never shows the previous run while its cockpit loads, fails or completes late', async ({ page, gate }) => {
  test.setTimeout(120_000)
  await injectTestSession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)

  await registerProjectViaUi(page, ALPHA, createFixtureRepository('selection-alpha'))
  await selectProject(page, ALPHA)
  await recordManualRun(page, OBJECTIVE_ALPHA)
  await registerProjectViaUi(page, BRAVO, createFixtureRepository('selection-bravo'))
  await selectProject(page, BRAVO)
  await recordManualRun(page, OBJECTIVE_BRAVO)

  const summaries = await readRunSummaries(page)
  const alphaRun = summaries.find((summary) => summary.projectName === ALPHA)?.runId
  const bravoRun = summaries.find((summary) => summary.projectName === BRAVO)?.runId
  expect(alphaRun).toBeTruthy()
  expect(bravoRun).toBeTruthy()

  await gate.attach(page)

  const headingAlpha = page.getByRole('heading', { name: OBJECTIVE_ALPHA })
  const headingBravo = page.getByRole('heading', { name: OBJECTIVE_BRAVO })
  const planningAction = page.getByLabel('Codex planning')

  // A is selected and fully loaded, with its header, stages and Agent action.
  await selectProject(page, ALPHA)
  await expect(headingAlpha).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Created · Intake')).toBeVisible()
  await expect(planningAction).toBeVisible()
  await expect(page.getByRole('complementary', { name: 'Workflow' })).toBeVisible()

  // B is selected while its authenticated cockpit response is outstanding: nothing of A is shown as B.
  await watchForLeaks(page, OBJECTIVE_ALPHA, BRAVO)
  gate.modes.set(bravoRun!, 'hold')
  await selectProject(page, BRAVO)
  await expect.poll(() => gate.heldCount(bravoRun!)).toBeGreaterThanOrEqual(1)
  await expect(page.getByText('Loading run…')).toBeVisible()
  await expect(headingAlpha).toHaveCount(0)
  await expect(headingBravo).toHaveCount(0)
  await expect(planningAction).toHaveCount(0)
  await expect(page.getByRole('complementary', { name: 'Workflow' })).toHaveCount(0)
  await expect(page.getByText('Created · Intake')).toHaveCount(0)
  await page.waitForTimeout(500)
  await expect(headingAlpha).toHaveCount(0)

  gate.release(bravoRun!)
  await expect(headingBravo).toBeVisible({ timeout: 15_000 })
  await expect(headingAlpha).toHaveCount(0)
  expect(await leaks(page)).toBe(0)

  // Returning to A while A's answer is held, then moving on to B again: A's late completion belongs to nothing.
  await watchForLeaks(page, OBJECTIVE_ALPHA, BRAVO)
  gate.modes.set(alphaRun!, 'hold')
  await selectProject(page, ALPHA)
  await expect.poll(() => gate.heldCount(alphaRun!)).toBeGreaterThanOrEqual(1)
  await expect(page.getByText('Loading run…')).toBeVisible()
  await expect(headingBravo).toHaveCount(0)
  await expect(headingAlpha).toHaveCount(0)

  gate.modes.set(bravoRun!, 'pass')
  await selectProject(page, BRAVO)
  await expect(headingBravo).toBeVisible({ timeout: 15_000 })
  gate.release(alphaRun!)
  await page.waitForTimeout(750)
  await expect(headingBravo).toBeVisible()
  await expect(headingAlpha).toHaveCount(0)
  expect(await leaks(page)).toBe(0)

  // B's cockpit fails to load: its own failure is shown, never A's evidence.
  gate.modes.set(alphaRun!, 'pass')
  await selectProject(page, ALPHA)
  await expect(headingAlpha).toBeVisible({ timeout: 15_000 })
  await watchForLeaks(page, OBJECTIVE_ALPHA, BRAVO)
  gate.modes.set(bravoRun!, 'fail')
  await selectProject(page, BRAVO)
  await expect(page.getByText('Loading run…')).toHaveCount(0, { timeout: 15_000 })
  await expect(headingAlpha).toHaveCount(0)
  await expect(headingBravo).toHaveCount(0)
  await expect(planningAction).toHaveCount(0)
  await expect(page.getByRole('complementary', { name: 'Workflow' })).toHaveCount(0)
  expect(await leaks(page)).toBe(0)

  // Recovery: a later successful selection of the same project shows its own cockpit.
  gate.modes.set(bravoRun!, 'pass')
  await selectProject(page, ALPHA)
  await expect(headingAlpha).toBeVisible({ timeout: 15_000 })
  await selectProject(page, BRAVO)
  await expect(headingBravo).toBeVisible({ timeout: 15_000 })
  await expect(headingAlpha).toHaveCount(0)

  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  expect(await page.evaluate(() => JSON.stringify(window.localStorage))).not.toContain(TEST_LAUNCH_SECRET)
})
