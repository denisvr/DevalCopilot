import { expect, test } from '@playwright/test'
import { TEST_LAUNCH_SECRET } from '../playwright.config'
import { createPlanningAuthorizationFixture } from './planningAuthorizationFixture'
import { injectTestSession, selectProject } from './support'

// A bounded Chromium interaction with the real host and the real frontend over an owned metadata fixture: the cockpit shows the
// escalated final plan and the one human decision, records it, and shows the server's own Available state and the exact
// reason, including after a reload. No provider is ever started and the separate implementation request is never made.

const REASON = 'I reviewed both rounds and accept the final plan.'

test('the owner authorizes the escalated final plan and the cockpit shows only what the server recorded', async ({ page }) => {
  test.setTimeout(90_000)
  const fixture = await createPlanningAuthorizationFixture('planning-authorization-ui')

  await injectTestSession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await selectProject(page, fixture.projectName)

  const panel = page.getByLabel('Human decision on the final plan')
  await expect(panel).toBeVisible({ timeout: 20_000 })
  await expect(panel).toContainText(/exactly one implementation claim/)
  await expect(panel).toContainText(/does not start an implementation/)
  await expect(page.getByLabel('Proposal lineage')).toContainText(/explicit human authorization/)

  // Before the decision there is no implementation action and no provider review or resolution of the final plan.
  await expect(page.getByRole('button', { name: 'Implement the human-authorized final plan with Claude' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)

  // A blank reason is never sent.
  await page.getByRole('button', { name: 'Authorize one implementation claim' }).click()
  await expect(panel).toContainText('A reason is required.')

  await page.getByLabel(/Your reason for authorizing this final plan/).fill(REASON)
  await page.getByRole('button', { name: 'Authorize one implementation claim' }).click()

  // Only the server read says Available: the recorded reason appears, the decision form is gone, and the separate
  // implementation action is now offered for exactly this plan (it is deliberately not used here).
  await expect(panel).toContainText('Authorized by a human', { timeout: 15_000 })
  await expect(panel).toContainText(`Recorded reason: ${REASON}`)
  await expect(page.getByRole('button', { name: 'Authorize one implementation claim' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Implement the human-authorized final plan with Claude' })).toBeVisible()
  await expect(page.getByRole('button', { name: /Request Claude review/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resolve challenges with Codex' })).toHaveCount(0)

  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  expect(await page.evaluate(() => JSON.stringify(window.localStorage))).not.toContain(TEST_LAUNCH_SECRET)

  await page.reload()
  await selectProject(page, fixture.projectName)
  const reloaded = page.getByLabel('Human decision on the final plan')
  await expect(reloaded).toContainText('Authorized by a human', { timeout: 20_000 })
  await expect(reloaded).toContainText(`Recorded reason: ${REASON}`)
  await expect(page.getByRole('button', { name: 'Implement the human-authorized final plan with Claude' })).toBeVisible()
})
