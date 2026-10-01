import { expect, test } from '@playwright/test'
import { TEST_LAUNCH_SECRET } from '../playwright.config'
import { createFixtureRepository, injectTestSession, readRunSummaries, registerProjectViaUi, selectProject } from './support'

const PROJECT = 'Manual intake fixture'
const OBJECTIVE = 'Record the ledger migration plan for review'

test('a typed objective becomes a durable manual run that stays waiting, including after a reload', async ({ page }) => {
  await injectTestSession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)

  await registerProjectViaUi(page, PROJECT, createFixtureRepository('manual-intake'))
  await selectProject(page, PROJECT)

  // First run: the objective form is offered, with the simulation labelled as a demo.
  await expect(page.getByText('Demo only')).toBeVisible()
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()

  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Manual Agent run').first()).toBeVisible()
  await expect(page.getByText('Waiting for an explicit planning request')).toBeVisible()
  await expect(page.getByText('Created · Intake')).toBeVisible()
  await expect(
    page.getByText('A new objective can be recorded only after every run of this project has finished.'),
  ).toBeVisible()

  const [before] = (await readRunSummaries(page)).filter((summary) => summary.projectName === PROJECT)
  expect(before.executionMode).toBe('ManualAgent')
  expect(before.lifecycle).toBe('Created')
  expect(before.canCreateRun).toBe(false)

  // The simulator polls every 200 ms: several cycles later nothing has started the run.
  await page.waitForTimeout(3_000)
  await expect(page.getByText('Created · Intake')).toBeVisible()
  await expect(page.locator('article.dc-card')).toHaveCount(0)
  await expect(page.getByText('Completed · Completed')).toHaveCount(0)

  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  expect(await page.evaluate(() => JSON.stringify(window.localStorage))).not.toContain(TEST_LAUNCH_SECRET)

  await page.reload()
  // A reload selects the first registered project, which may belong to another specification: select this fixture again.
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Manual Agent run').first()).toBeVisible()
  await expect(page.getByText('Waiting for an explicit planning request')).toBeVisible()
  await expect(page.getByText('Created · Intake')).toBeVisible()
  await expect(page.locator('article.dc-card')).toHaveCount(0)

  const [after] = (await readRunSummaries(page)).filter((summary) => summary.projectName === PROJECT)
  expect(after.runId).toBe(before.runId)
  expect(after.executionMode).toBe('ManualAgent')
  expect(after.lifecycle).toBe('Created')
})
