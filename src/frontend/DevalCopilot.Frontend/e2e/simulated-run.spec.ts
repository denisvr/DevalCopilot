import { expect, test } from '@playwright/test'
import { TEST_LAUNCH_SECRET } from '../playwright.config'
import { createFixtureRepository, injectTestSession, registerProjectViaUi, selectProject } from './support'

test('an explicitly labelled simulated demo run shows Codex/Claude collaboration and survives a reload', async ({
  page,
}) => {
  await injectTestSession(page)
  await page.goto('/')

  // Authenticated initial load: the "no session" state must not appear.
  await expect(page.getByText('No launch session is available')).toHaveCount(0)

  await registerProjectViaUi(page, 'Simulation demo fixture', createFixtureRepository('simulation-demo'))
  // Registration does not change the selected project: select the new one (a distinct project from any other test).
  await selectProject(page, 'Simulation demo fixture')

  await expect(page.getByText('Demo only')).toBeVisible()
  await page.getByRole('button', { name: 'Start simulated run' }).click()

  // Visible Codex and Claude collaboration, reconstructed from durable events.
  for (const type of ['Proposal', 'Challenge', 'Decision', 'ExecutionReport']) {
    await expect(page.locator(`article.dc-card[data-type="${type}"]`)).toBeVisible({ timeout: 15_000 })
  }

  // Deterministic terminal completion.
  await expect(page.getByText('Completed · Completed')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Simulated demo run').first()).toBeVisible()

  const objective = await page.getByRole('heading', { name: 'Prove the walking skeleton' }).textContent()

  // No credential in the URL or in browser storage.
  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  const storageSnapshot = await page.evaluate(() => JSON.stringify(window.localStorage))
  expect(storageSnapshot).not.toContain(TEST_LAUNCH_SECRET)

  // Persisted-run retrieval: reload against the same on-disk SQLite file.
  await page.reload()
  // After a reload the first registered project is selected again; select this fixture.
  await selectProject(page, 'Simulation demo fixture')
  await expect(page.getByText('Completed · Completed')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('heading', { name: objective ?? 'Prove the walking skeleton' })).toBeVisible()
})
