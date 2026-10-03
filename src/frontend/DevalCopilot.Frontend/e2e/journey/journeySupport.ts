import { expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import { getAuthorizedJson } from '../harness/authorizedRequest'
import { registerWithReadinessRetry } from '../harness/registrationRetry'
import type { RegistrationDriver } from '../harness/registrationRetry'
import { JOURNEY_API_URL, journeySecret } from './journeyEnv'

// Journey-local UI helpers. They mirror the few helpers of e2e/support.ts that the journey needs, because that module imports
// the real-host configuration (which would create a second owned root and secret when loaded by this configuration).

export async function injectJourneySession(page: Page) {
  await page.addInitScript(
    ([baseUrl, secret]) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ;(window as any).__DEVALCOPILOT_SESSION__ = { baseUrl, secret }
    },
    [JOURNEY_API_URL, journeySecret()],
  )
}

/**
 * Registers the repository through the Add project control, once per accepted registration. The host's Git capability probe may
 * not have completed yet; only that refusal (see registrationRetry.ts) is submitted again, within a finite budget.
 */
export async function registerProject(page: Page, name: string, path: string) {
  const driver: RegistrationDriver = {
    async submit(projectName, repositoryPath) {
      await page.getByRole('button', { name: 'Add project' }).click()
      await page.getByLabel('Project name').fill(projectName)
      await page.getByLabel('Repository path').fill(repositoryPath)
      const answered = page.waitForResponse(
        (response) => response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/projects',
        { timeout: 30_000 },
      )
      await page.getByRole('button', { name: 'Register' }).click()
      const response = await answered
      return { status: response.status(), body: await response.text() }
    },
    async confirmRendered(projectName) {
      await expect(page.getByRole('button', { name: new RegExp(`^${projectName}`) })).toBeVisible({ timeout: 15_000 })
    },
    async dismiss() {
      await page.getByRole('button', { name: 'Cancel' }).click()
    },
  }
  await registerWithReadinessRetry(driver, name, path, { attempts: 30, delayMs: 1_000 })
}

export async function selectProject(page: Page, name: string) {
  const chip = page.getByRole('button', { name: new RegExp(`^${name}`) })
  await chip.click()
  await expect(chip).toHaveAttribute('data-selected', 'true')
}

/** The visible workflow section as text lines (the rendered stage controls and results), for assertions and failure diagnostics. */
export async function workflowLines(page: Page): Promise<string[]> {
  const lines = (await page.locator('body').innerText()).split('\n').filter((line) => line.trim().length > 0)
  const start = lines.findIndex((line) => line === 'WORKFLOW')
  const end = lines.findIndex((line) => line === 'USAGE & EVIDENCE')
  return start >= 0 ? lines.slice(start, end > start ? end : undefined) : lines
}

/** A read-only authenticated GET used only to supplement visible assertions; failures carry no credential. */
export async function readJson<T>(page: Page, path: string, label: string): Promise<T> {
  return getAuthorizedJson<T>(page.request, `${JOURNEY_API_URL}${path}`, journeySecret(), label)
}
