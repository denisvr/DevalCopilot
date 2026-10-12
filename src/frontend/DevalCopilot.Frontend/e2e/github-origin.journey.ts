import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { expect, test } from '@playwright/test'
import { withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, markInvocations, readInvocations, repositorySnapshot } from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the explicit, repository-local GitHub origin observation (ADR-0039) through the production frontend,
// generated client, MVC authentication, mediator, the stored project identity, the discovery snapshot of the REAL Git executable,
// the REAL origin adapter and the REAL process executor, over two real disposable repositories. No remote, network, GitHub account
// or credential is involved and no provider double is invoked for this feature: it proves configured scope only, never remote
// identity, permission, CI success or publication authority.
//
// Repository A carries a URL rewrite and a push destination that Git itself would honor; the answer must still be exactly its
// configured origin. Nothing reads until the owner presses Inspect, a second project is isolated from the first, and neither the
// database, the repositories' Git directories, their working trees nor any provider double changes.

const FIRST = 'Origin alpha fixture'
const SECOND = 'Origin beta fixture'
const ENDPOINT = /\/api\/projects\/[^/]+\/github-origin$/
const UNAVAILABLE = /^Unavailable: the repository-local origin could not be observed right now/

function git(path: string, ...args: string[]) {
  execFileSync('git', ['-C', path, ...args], { stdio: 'pipe' })
}

/** Every file below `root` by relative path and content hash, so any write of any kind changes the result. */
function treeFingerprint(root: string): Record<string, string> {
  const result: Record<string, string> = {}
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory)) {
      const full = join(directory, entry)
      if (statSync(full).isDirectory()) {
        result[`${relative(root, full)}/`] = ''
        walk(full)
      } else {
        result[relative(root, full)] = createHash('sha256').update(readFileSync(full)).digest('hex')
      }
    }
  }
  walk(root)
  return result
}

/** Every row of every table except the host's own background capability snapshots, as text. */
function databaseFingerprint(): string {
  return withJourneyDb((db) => {
    const tables = (db
      .prepare("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' and name <> 'host_capability_snapshots' order by name")
      .all() as { name: string }[]).map((row) => row.name)
    return tables
      .map((name) => {
        const statement = db.prepare(`select * from "${name}"`)
        statement.setReadBigInts(true)
        return `${name}:${JSON.stringify(statement.all(), (_key, value) => (typeof value === 'bigint' ? value.toString() : value))}`
      })
      .join('\n')
  })
}

test('the project GitHub origin journey: nothing reads until the owner inspects, then inspect, recheck, isolate projects, close and reopen', async ({ page }) => {
  const mark = markInvocations()
  let requests = 0
  const requested: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'GET' && ENDPOINT.test(url.pathname)) {
      requests += 1
      requested.push(url.pathname)
    }
  })

  const first = createJourneyRepository('origin-alpha')
  git(first.path, 'config', 'remote.origin.url', 'https://github.com/acme/widgets.git')
  git(first.path, 'config', 'url.https://evil.example/.insteadOf', 'https://github.com/')
  git(first.path, 'config', 'remote.origin.pushurl', 'https://github.com/other/destination')
  const second = createJourneyRepository('origin-beta')
  git(second.path, 'config', 'remote.origin.url', 'git@github.com:beta/gadgets')

  await injectJourneySession(page)
  await page.goto('/')
  await registerProject(page, FIRST, first.path)
  await registerProject(page, SECOND, second.path)

  // The observation requires an already verified physical identity and never verifies one itself: each project first goes through
  // the existing explicit identity recheck of the workspace panel (a different feature, before everything observed below).
  for (const name of [FIRST, SECOND]) {
    await selectProject(page, name)
    await page.getByRole('button', { name: 'Recheck identity' }).click()
    await expect(page.getByRole('button', { name: 'Recheck identity' })).toHaveCount(0, { timeout: 15_000 })
  }
  await selectProject(page, FIRST)

  const region = page.getByRole('region', { name: 'Project GitHub origin' })
  await expect(region.getByRole('button', { name: 'Show project GitHub origin' })).toBeVisible()

  // Everything the feature must leave untouched, observed after registration and before the first explicit inspection.
  const databaseBefore = databaseFingerprint()
  const snapshotsBefore = [repositorySnapshot(first.path), repositorySnapshot(second.path)]
  const gitDirectoriesBefore = [treeFingerprint(join(first.path, '.git')), treeFingerprint(join(second.path, '.git'))]
  const workingTreesBefore = [first.path, second.path].map((path) =>
    Object.fromEntries(Object.entries(treeFingerprint(path)).filter(([name]) => !name.startsWith('.git'))))

  await region.getByRole('button', { name: 'Show project GitHub origin' }).click()
  await expect(region.getByText('Not inspected.')).toBeVisible()
  await expect(region.getByText(/only the origin URL stored in this repository's own local Git configuration/)).toBeVisible()
  await expect(region.getByText(/URL rewrite rules and a separate push destination are ignored/)).toBeVisible()
  await expect(region.getByText(/Nothing contacts GitHub/)).toBeVisible()
  await expect(region.getByText(/not a verified remote identity, permission, CI result or publication authority/)).toBeVisible()

  // Opening, waiting, reloading and selecting another project read nothing.
  await page.waitForTimeout(2_000)
  expect(requests).toBe(0)

  // The host's capability snapshot of Git may be stale for a moment: only an explicit recheck is repeated, within a budget.
  let inspections = 0
  const deadline = Date.now() + 40_000
  for (;;) {
    await region.getByRole('button', { name: inspections === 0 ? 'Inspect GitHub origin' : 'Inspect again' }).click()
    inspections += 1
    await expect(region.getByText(/^Observed at |^Unavailable: /)).toBeVisible({ timeout: 15_000 })
    if (await region.getByText('acme').isVisible()) {
      break
    }

    expect(await region.getByText(UNAVAILABLE).isVisible()).toBe(true)
    expect(Date.now()).toBeLessThan(deadline)
    await page.waitForTimeout(1_000)
  }

  // The exact configured origin, as plain text; the rewrite and the push destination never appear.
  await expect(region.locator('dl.dc-github-origin-target')).toHaveText('Hostgithub.comOwneracmeRepositorywidgets')
  await expect(region.getByText(/^Observed at \d{4}-\d{2}-\d{2}T[\d:.]+Z \(UTC\)\.$/)).toBeVisible()
  const text = (await region.innerText()).toLowerCase()
  expect(text).not.toContain('evil.example')
  expect(text).not.toContain('other/destination')
  expect(requests).toBe(inspections)
  expect(new Set(requested).size).toBe(1)

  // An explicit recheck is exactly one more request; nothing else is read by itself.
  await region.getByRole('button', { name: 'Inspect again' }).click()
  await expect(region.locator('dl.dc-github-origin-target')).toHaveText('Hostgithub.comOwneracmeRepositorywidgets')
  const afterRecheck = requests
  expect(afterRecheck).toBe(inspections + 1)
  await page.waitForTimeout(2_000)
  expect(requests).toBe(afterRecheck)

  // Another project is isolated: collapsed, nothing of the first, and its own explicit inspection reads its own origin.
  await selectProject(page, SECOND)
  await expect(region.getByRole('button', { name: 'Show project GitHub origin' })).toBeVisible()
  await expect(region.getByText('acme')).toHaveCount(0)
  expect(requests).toBe(afterRecheck)
  await region.getByRole('button', { name: 'Show project GitHub origin' }).click()
  await expect(region.getByText('Not inspected.')).toBeVisible()
  await region.getByRole('button', { name: 'Inspect GitHub origin' }).click()
  await expect(region.locator('dl.dc-github-origin-target')).toHaveText('Hostgithub.comOwnerbetaRepositorygadgets')
  expect(requests).toBe(afterRecheck + 1)
  expect(new Set(requested).size).toBe(2)

  // Returning to the first project is a new lifetime: collapsed and unchecked, and reading nothing by itself.
  await selectProject(page, FIRST)
  await expect(region.getByRole('button', { name: 'Show project GitHub origin' })).toBeVisible()
  await expect(region.getByText('beta')).toHaveCount(0)
  await region.getByRole('button', { name: 'Show project GitHub origin' }).click()
  await expect(region.getByText('Not inspected.')).toBeVisible()
  await expect(region.getByText('acme')).toHaveCount(0)
  expect(requests).toBe(afterRecheck + 1)

  // Closing and reopening is a new lifetime that starts unchecked and reads nothing.
  await region.getByRole('button', { name: 'Close project GitHub origin' }).click()
  await region.getByRole('button', { name: 'Show project GitHub origin' }).click()
  await expect(region.getByText('Not inspected.')).toBeVisible()
  expect(requests).toBe(afterRecheck + 1)

  // A reload is a new session lifetime: collapsed, unchecked, nothing read.
  await page.reload()
  await page.waitForTimeout(1_000)
  expect(requests).toBe(afterRecheck + 1)

  // Nothing was written: not the database, not either repository's Git directory (index, objects, refs, configuration) or working
  // tree, and no provider double did anything beyond its background capability probe.
  expect(databaseFingerprint()).toBe(databaseBefore)
  expect([repositorySnapshot(first.path), repositorySnapshot(second.path)]).toEqual(snapshotsBefore)
  expect([treeFingerprint(join(first.path, '.git')), treeFingerprint(join(second.path, '.git'))]).toEqual(gitDirectoriesBefore)
  expect([first.path, second.path].map((path) =>
    Object.fromEntries(Object.entries(treeFingerprint(path)).filter(([name]) => !name.startsWith('.git'))))).toEqual(workingTreesBefore)
  expect(readInvocations(mark).every((entry) => entry.kind === 'probe')).toBe(true)
})
