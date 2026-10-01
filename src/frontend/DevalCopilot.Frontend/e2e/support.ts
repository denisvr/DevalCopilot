import { execFileSync } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import { API_BASE_URL, E2E_ROOT, TEST_LAUNCH_SECRET } from '../playwright.config'
import { getAuthorizedJson } from './harness/authorizedRequest'

export async function injectTestSession(page: Page) {
  await page.addInitScript(
    ([baseUrl, secret]) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ;(window as any).__DEVALCOPILOT_SESSION__ = { baseUrl, secret }
    },
    [API_BASE_URL, TEST_LAUNCH_SECRET],
  )
}

/** A disposable Git repository under the root owned by this test run, with one commit and a fictitious identity. */
export function createFixtureRepository(name: string): string {
  const path = join(E2E_ROOT, 'repos', name)
  mkdirSync(path, { recursive: true })
  writeFileSync(join(path, 'README.md'), `# ${name}\n`)
  const git = (...args: string[]) => execFileSync('git', ['-C', path, ...args], { stdio: 'pipe' })
  git('init', '--initial-branch', 'main')
  git('add', 'README.md')
  git('-c', 'user.name=Fixture Author', '-c', 'user.email=fixture@example.invalid', 'commit', '-m', 'Initial commit')
  return path
}

/** Registers the repository through the public Add project operation; waits for the host Git probe. */
export async function registerProjectViaUi(page: Page, name: string, path: string) {
  const deadline = Date.now() + 40_000
  for (;;) {
    await page.getByRole('button', { name: 'Add project' }).click()
    await page.getByLabel('Project name').fill(name)
    await page.getByLabel('Repository path').fill(path)
    await page.getByRole('button', { name: 'Register' }).click()
    const chip = page.getByRole('button', { name: new RegExp(`^${name}`) })
    try {
      await expect(chip).toBeVisible({ timeout: 3_000 })
      return
    } catch (error) {
      if (Date.now() > deadline) {
        throw error
      }
      // The Git capability probe of the host may not have completed yet: cancel and retry.
      const cancel = page.getByRole('button', { name: 'Cancel' })
      if (await cancel.isVisible()) {
        await cancel.click()
      }
      await page.waitForTimeout(1_000)
    }
  }
}

export async function readRunSummaries(
  page: Page,
): Promise<{ projectName: string; runId?: string; lifecycle?: string; executionMode?: string; canCreateRun?: boolean }[]> {
  // Sanitized: a transport failure of the authorized request must never reach a report with the credential in it.
  return getAuthorizedJson(
    page.request,
    `${API_BASE_URL}/api/projects/run-summaries`,
    TEST_LAUNCH_SECRET,
    'Run summaries',
  )
}
