import { expect, test } from '@playwright/test'
import { markInvocations, readInvocations } from './journey/journeyEnv'
import { injectJourneySession } from './journey/journeySupport'

// The browser-driven proof of the explicit, stored-only GitHub CLI authentication observation (ADR-0038) through the production
// frontend, generated client, MVC authentication, mediator, the discovery snapshot, the REAL stored-authentication adapter and the
// REAL process executor. Only the `gh` executable is the deterministic owned double of the provider fixture: it answers the one
// fixed status contract with an already-filtered authenticated account and logs only that call. This proves the assembled
// process boundary, normal authentication and explicit-only reads; it proves nothing about how an installed GitHub CLI formats or
// filters `--json hosts --jq`, about any real account, or about repository permission or publication authority. No GitHub access,
// account or credential is used.

const ENDPOINT = /\/api\/environment\/github-stored-authentication$/
const UNAVAILABLE = 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.'
const AUTHENTICATED = 'An active stored account is authenticated for github.com.'

test('the stored GitHub authentication journey: nothing reads until the owner checks, then check, recheck, close and reopen', async ({ page }) => {
  const mark = markInvocations()
  let requests = 0
  page.on('request', (request) => {
    if (request.method() === 'GET' && ENDPOINT.test(new URL(request.url()).pathname)) {
      requests += 1
    }
  })
  const statusCalls = () => readInvocations(mark).filter((entry) => entry.role === 'gh' && entry.kind === 'auth-status').length

  await injectJourneySession(page)
  await page.goto('/')

  const region = page.getByRole('region', { name: 'GitHub stored authentication' })
  await region.getByRole('button', { name: 'Show GitHub stored authentication' }).click()
  await expect(region.getByText('Not checked.')).toBeVisible()
  await expect(region.getByText(/not observed, so this is not your complete ambient CLI session/)).toBeVisible()
  await expect(region.getByText(/neither repository permission nor publication authority/)).toBeVisible()

  // Opening, waiting and reloading read nothing.
  await page.waitForTimeout(2_000)
  expect(requests).toBe(0)
  expect(statusCalls()).toBe(0)

  // The host's capability discovery of the double may not have finished yet: only an explicit recheck is repeated, within a budget.
  let checks = 0
  let unavailable = 0
  const deadline = Date.now() + 40_000
  for (;;) {
    const label = checks === 0 ? 'Check stored authentication' : 'Check again'
    await region.getByRole('button', { name: label }).click()
    checks += 1
    await expect(region.getByText(/^(An active stored account|The GitHub CLI is not currently)/)).toBeVisible({ timeout: 15_000 })
    if (await region.getByText(AUTHENTICATED).isVisible()) {
      break
    }

    expect(await region.getByText(UNAVAILABLE).isVisible()).toBe(true)
    unavailable += 1
    expect(Date.now()).toBeLessThan(deadline)
    await page.waitForTimeout(1_000)
  }

  await expect(region.getByText(/^Observed at \d{4}-\d{2}-\d{2}T[\d:.]+Z \(UTC\)\.$/)).toBeVisible()
  expect(requests).toBe(checks)
  expect(statusCalls()).toBe(1)
  expect(unavailable).toBe(checks - 1)

  // An explicit recheck is exactly one more observation; nothing else is read by itself.
  await region.getByRole('button', { name: 'Check again' }).click()
  await expect(region.getByText(AUTHENTICATED)).toBeVisible()
  expect(statusCalls()).toBe(2)
  await page.waitForTimeout(2_000)
  expect(statusCalls()).toBe(2)
  expect(requests).toBe(checks + 1)

  // Closing and reopening is a new lifetime that starts unchecked and reads nothing.
  await region.getByRole('button', { name: 'Close GitHub stored authentication' }).click()
  await region.getByRole('button', { name: 'Show GitHub stored authentication' }).click()
  await expect(region.getByText('Not checked.')).toBeVisible()
  await expect(region.getByText(AUTHENTICATED)).toHaveCount(0)
  expect(requests).toBe(checks + 1)

  // A reload is a new session lifetime: collapsed, unchecked, nothing read.
  await page.reload()
  await expect(region.getByRole('button', { name: 'Show GitHub stored authentication' })).toBeVisible()
  await page.waitForTimeout(1_000)
  expect(statusCalls()).toBe(2)

  // The double logs no credential, account or argument detail.
  const logged = JSON.stringify(readInvocations(mark).filter((entry) => entry.role === 'gh'))
  expect(logged).not.toMatch(/token|login|scope|github\.com/i)
})
