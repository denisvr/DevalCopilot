import { expect, test } from '@playwright/test'
import { GetGitCheckpointDiffEndpointClient } from '../src/api/generated/api-client'
import { API_BASE_URL, TEST_LAUNCH_SECRET } from '../playwright.config'
import { OUTSIDE_SENTINEL, createCheckpointInspectionFixture } from './checkpointInspectionFixture'
import { authorizedHttp } from './planningAuthorizationFixture'
import { injectTestSession, selectProject } from './support'

// ADR-0027 through the real stack: the authenticated Chromium page, the generated client over real HTTP and the actual host. A
// tracked file of the isolated workspace is replaced by a real hard link to a fictitious file OUTSIDE it. Inspecting the checkpoint
// must show the healthy sibling's comparison and an explicit omission for the unsafe path, and neither any response the host
// sent to the page nor the rendered page may ever contain the outside text. The fixture is owned by this run (see
// checkpointInspectionFixture.ts); no provider is started.

test('inspecting a checkpoint renders the safe comparison and an explicit omission and never the text of a file outside the workspace', async ({ page }) => {
  test.setTimeout(120_000)
  const fixture = await createCheckpointInspectionFixture('checkpoint-inspection')

  // Every response body the page receives from the host is checked, not only the one for the diff route.
  const bodies: string[] = []
  page.on('response', (response) => {
    if (response.url().startsWith(API_BASE_URL)) {
      void response.text().then((text) => bodies.push(text), () => undefined)
    }
  })

  await injectTestSession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await selectProject(page, fixture.projectName)

  const source = page.getByRole('region', { name: 'Source evidence' })
  await expect(source).toContainText('Checkpoint #1 · 2 changed files', { timeout: 30_000 })
  const diffResponse = page.waitForResponse((response) => response.url().endsWith(`/checkpoints/${fixture.checkpointId}/diff`))
  await source.getByRole('button', { name: 'Inspect files & diff' }).click()
  expect((await diffResponse).status()).toBe(200)

  const comparison = source.getByRole('group', { name: 'Checkpoint comparison' })
  await expect(comparison).toBeVisible()
  await expect(comparison.getByRole('status')).toHaveText('Partial comparison: 1 of 2 tracked files compared, 1 omitted.')
  await expect(comparison.getByRole('list', { name: 'Files without comparison' })).toContainText(fixture.unsafePath)
  await expect(comparison.getByRole('list', { name: 'Files without comparison' })).toContainText('not proven to be this file inside the owned workspace')
  await expect(comparison).toContainText('+BETA EDITED IN THE WORKSPACE')
  await expect(comparison).toContainText('diff --git a/src/safe.txt b/src/safe.txt')
  await expect(comparison).toContainText('not Git\'s minimal or filter-normalized patch')
  await expect(page.getByText('No tracked diff.')).toHaveCount(0)

  // Nothing of the outside file is rendered anywhere, not even as a fragment.
  expect(await page.evaluate(() => document.documentElement.outerHTML)).not.toContain('OUTSIDE-SENTINEL')
  expect(await page.evaluate(() => document.body.innerText)).not.toContain('fictitious bytes')
  await expect.poll(() => bodies.length).toBeGreaterThan(0)
  for (const body of bodies) {
    expect(body).not.toContain('OUTSIDE-SENTINEL')
    expect(body).not.toContain(OUTSIDE_SENTINEL)
  }

  // The same route through the generated client over real HTTP: the typed, bounded contract and no raw patch.
  const typed = await new GetGitCheckpointDiffEndpointClient(API_BASE_URL, authorizedHttp).getGitCheckpointDiff(fixture.projectId, fixture.checkpointId)
  expect(typed.isComplete).toBe(false)
  expect(typed.trackedPathCount).toBe(2)
  expect(typed.comparedPathCount).toBe(1)
  expect(typed.omissions?.map((omission) => [omission.path, omission.reason])).toEqual([[fixture.unsafePath, 'containment_unproven']])
  expect(JSON.stringify(typed)).not.toContain('OUTSIDE-SENTINEL')

  expect(page.url()).not.toContain(TEST_LAUNCH_SECRET)
  expect(await page.evaluate(() => JSON.stringify(window.localStorage))).not.toContain(TEST_LAUNCH_SECRET)
})
