import { expect, test } from '@playwright/test'
import { API_BASE_URL, TEST_LAUNCH_SECRET } from '../playwright.config'

async function injectTestSession(page: import('@playwright/test').Page) {
  await page.addInitScript(
    ([baseUrl, secret]) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ;(window as any).__DEVALCOPILOT_SESSION__ = { baseUrl, secret }
    },
    [API_BASE_URL, TEST_LAUNCH_SECRET],
  )
}

test('starting a simulated run shows Codex/Claude collaboration and survives a reload against the same database', async ({
  page,
}) => {
  await injectTestSession(page)
  await page.goto('/')

  // Authenticated initial load: the "no session" state must not appear.
  await expect(page.getByText('No launch session is available')).toHaveCount(0)

  await page.getByRole('button', { name: 'Start simulated run' }).click()

  // Visible Codex and Claude collaboration, reconstructed from durable events.
  await expect(page.getByText(/Codex · codex\.proposal/)).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/Claude · claude\.challenge/)).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/Codex · codex\.resolution/)).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/Claude · claude\.execution/)).toBeVisible({ timeout: 15_000 })

  // Deterministic terminal completion.
  await expect(page.getByText('Completed · Completed')).toBeVisible({ timeout: 15_000 })

  const objective = await page.getByRole('heading', { name: 'Prove the walking skeleton' }).textContent()

  // No credential in the URL or in browser storage.
  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  const storageSnapshot = await page.evaluate(() => JSON.stringify(window.localStorage))
  expect(storageSnapshot).not.toContain(TEST_LAUNCH_SECRET)

  // Persisted-run retrieval after a restart: reload the page (a fresh render and a
  // fresh query round trip) against the same on-disk SQLite file the host still has
  // open, and confirm the same completed run is still there.
  await page.reload()
  await expect(page.getByText('Completed · Completed')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('heading', { name: objective ?? 'Prove the walking skeleton' })).toBeVisible()
})
