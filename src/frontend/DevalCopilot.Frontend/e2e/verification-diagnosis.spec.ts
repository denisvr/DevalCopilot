import { expect, test } from '@playwright/test'
import { TEST_LAUNCH_SECRET } from '../playwright.config'
import { createVerificationDiagnosisFixture } from './verificationDiagnosisFixture'
import { injectTestSession, selectProject } from './support'

// A bounded, display-and-interaction-only Chromium regression with the real host and the real frontend over an owned
// RAW-SQL evidence fixture (see verificationDiagnosisFixture.ts): the cockpit shows the completed diagnosis of a failed local
// verification, its finding count, the pinned verification list, the correction control with its distinct copy, and the same
// state after a reload. No provider is ever started and, deliberately, no control that claims an attempt is clicked.

test('the cockpit shows a completed verification diagnosis, distinct from a code review, and the same state after a reload', async ({ page }) => {
  test.setTimeout(90_000)
  const fixture = await createVerificationDiagnosisFixture('verification-diagnosis-ui')

  await injectTestSession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await selectProject(page, fixture.projectName)

  const section = page.getByRole('region', { name: 'Verification diagnosis' })
  await expect(section).toBeVisible({ timeout: 20_000 })
  await expect(section).toContainText('Diagnosis of failed local verification (Codex, read-only)')
  await expect(section).toContainText('This is not a code review')
  await expect(section).toContainText('Last diagnosis #4: Diagnosis findings recorded.')
  await expect(section).toContainText('Codex recorded 2 findings')

  // The pinned verification membership is the host's own, in order, with status and exit code.
  const members = section.getByRole('list', { name: 'Verification evidence pinned by this diagnosis' })
  for (const member of fixture.pinnedVerification) {
    await expect(members).toContainText(member)
  }

  // The findings are ordinary ReviewFinding cards of the collaboration timeline.
  for (const summary of fixture.findingSummaries) {
    await expect(page.getByText(summary).first()).toBeVisible()
  }

  // The correction is a separate, explicit control with its own copy; it is only inspected, never clicked here. The
  // allowance is shared with ordinary review corrections, and nothing offers an authorization.
  await expect(section.getByRole('button', { name: 'Correct the diagnosed findings with Claude' })).toBeEnabled()
  await expect(section).toContainText('shared with ordinary review corrections')
  await expect(section.getByRole('button', { name: /authoriz/i })).toHaveCount(0)
  // The diagnosis of an already-diagnosed verification is not requested again.
  await expect(section.getByRole('button', { name: 'Diagnose failed verification with Codex' })).toHaveCount(0)
  // A diagnosis never presents itself as an approval or as a passed review.
  expect((await section.innerText()).toLowerCase()).not.toMatch(/approv|review passed/)

  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  expect(await page.evaluate(() => JSON.stringify(window.localStorage))).not.toContain(TEST_LAUNCH_SECRET)

  await page.reload()
  await selectProject(page, fixture.projectName)
  const reloaded = page.getByRole('region', { name: 'Verification diagnosis' })
  await expect(reloaded).toContainText('Codex recorded 2 findings', { timeout: 20_000 })
  await expect(reloaded.getByRole('list', { name: 'Verification evidence pinned by this diagnosis' })).toContainText(
    fixture.pinnedVerification[0],
  )
  await expect(reloaded.getByRole('button', { name: 'Correct the diagnosed findings with Claude' })).toBeEnabled()
})
