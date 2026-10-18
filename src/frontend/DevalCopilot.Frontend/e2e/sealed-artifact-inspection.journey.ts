import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import {
  createJourneyRepository,
  journeyRoot,
  markInvocations,
  readInvocations,
  readLaunchTargetVerdict,
  repositorySnapshot,
} from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of ART-WIN-01: an owner opens the evidence of a provider-observed collaboration message in the live cockpit
// and manually loads its sealed artifact text through the message-linked window route. Everything runs through the rendered controls of
// the production frontend over the generated client, real MVC authentication, mediator, SQLite and all supervisors; only the provider
// executables are the deterministic owned doubles. The message is produced by existing supported behavior only (one Codex planning
// attempt through its rendered control); the database, the artifact root, the source repository and the doubles' invocation log are
// only READ, never seeded.
//
// It proves: each purpose recorded for the message's own attempt is offered once and loaded through that route's closed segment for exactly
// this run and message (the recorded purpose name itself is a 404 there); the loaded text equals the integrity-verified sealed bytes on
// disk, with an empty sealed file shown as empty; closing and reopening the evidence discards the text; and the inspection invokes no
// provider, sends no non-GET request and changes no workflow row, artifact, Git state or source file. Multi-window, non-ASCII and
// malformed-answer permutations are proven by the decoder, hook and viewer tests, not here. It is not replay or provider reliability.

const PROJECT = 'Sealed artifact inspection journey fixture'
const OBJECTIVE = 'Plan the ledger total and inspect its sealed evidence'
const WINDOW_ROUTE = /\/api\/runs\/([^/]+)\/collaboration-messages\/([^/]+)\/evidence(?:\/artifact-window\/([^/]+))?$/i
const ROUTE_SEGMENTS: Record<string, string> = {
  AgentContextManifest: 'context-manifest',
  AgentStandardOutput: 'stdout',
  AgentStandardError: 'stderr',
  AgentFinalResponse: 'final-response',
}
const PURPOSE_LABELS: Record<string, string> = {
  AgentContextManifest: 'Context manifest',
  AgentStandardOutput: 'Standard output',
  AgentStandardError: 'Standard error',
  AgentFinalResponse: 'Final response',
}

const data = new JourneyData(PROJECT)
const lower = (value: string) => value.toLowerCase()

interface ArtifactFact {
  Purpose: string
  ContentHash: string
  ByteLength: number
  RelativeStoragePath: string
}

function artifactsOfAttempt(attemptId: string): ArtifactFact[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Purpose, ContentHash, ByteLength, RelativeStoragePath from artifacts where AttemptId = ? order by Purpose')
        .all(attemptId) as unknown as ArtifactFact[],
  )
}

/** The sealed bytes of one artifact, read-only from the owned artifact root, after proving they still match the recorded length and hash. */
function sealedText(artifact: ArtifactFact): string {
  const bytes = readFileSync(join(journeyRoot().root, 'artifacts', artifact.RelativeStoragePath))
  expect(bytes.length).toBe(artifact.ByteLength)
  expect(`sha256:${createHash('sha256').update(bytes).digest('hex')}`).toBe(lower(artifact.ContentHash))
  return new TextDecoder('utf-8').decode(bytes)
}

/** Everything the inspection must leave exactly as it was. */
function durableState(repositoryPath: string, attemptId: string) {
  return {
    totals: data.projectTotals(),
    runs: data.runs(),
    attempts: data.attempts(),
    messages: data.messages(),
    artifacts: artifactsOfAttempt(attemptId).map((artifact) => ({ ...artifact, text: sealedText(artifact) })),
    repository: repositorySnapshot(repositoryPath),
  }
}

/** Selects one purpose, loads it and every further window on request, and returns what the page shows for it. */
async function loadPurpose(page: Page, evidence: Locator, purpose: string, requests: () => { method: string; path: string }[]) {
  const before = requests().length
  await evidence.getByRole('combobox').selectOption(purpose)
  await expect(evidence.getByRole('button', { name: 'Load', exact: true })).toBeVisible()
  // Choosing a purpose shows its caveat and reads nothing until Load.
  expect(requests().slice(before).filter((request) => /artifact-window/.test(request.path))).toEqual([])

  const answered = page.waitForResponse((response) => /artifact-window/.test(new URL(response.url()).pathname), { timeout: 30_000 })
  await evidence.getByRole('button', { name: 'Load', exact: true }).click()
  const response = await answered
  expect(response.status()).toBe(200)
  expect(new URL(response.url()).pathname.split('/').pop()).toBe(ROUTE_SEGMENTS[purpose])

  const text = evidence.locator('pre.dc-artifact-window-text')
  const empty = evidence.getByText('This sealed artifact is empty (0 bytes).')
  await expect(text.or(empty)).toBeVisible({ timeout: 15_000 })
  const nextWindow = evidence.getByRole('button', { name: 'Load next window', exact: true })
  while ((await nextWindow.count()) > 0) {
    const shown = (await text.textContent()) ?? ''
    await nextWindow.click()
    await expect.poll(async () => ((await text.textContent()) ?? '').length, { timeout: 15_000 }).toBeGreaterThan(shown.length)
  }

  await expect(evidence.getByText('This artifact window is unavailable.')).toHaveCount(0)
  return (await empty.count()) > 0 ? { empty: true, text: '' } : { empty: false, text: (await text.textContent()) ?? '' }
}

test('the sealed artifact inspection journey: plan, open the message evidence and load each sealed artifact through the message route', async ({ page }) => {
  test.setTimeout(240_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('sealed-artifact-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)
  expect(readLaunchTargetVerdict()).toEqual({ verified: true, codexOwned: true, claudeOwned: true })

  // 1. An ordinary project with a prepared workspace and checkpoint, an objective and one completed Codex planning attempt.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })
  await page.getByRole('button', { name: 'Request Codex plan' }).click()
  await expect(page.getByText('Last attempt #1: Plan proposed.')).toBeVisible({ timeout: 60_000 })

  const runId = data.runId()
  const [attempt] = data.attempts()
  expect(attempt).toMatchObject({ AttemptNumber: 1, Status: 'Completed', AgentRole: 'Planner', AgentOutcome: 'Proposed' })
  const observed = data.messages().filter((message) => message.Provenance === 'ProviderObserved')
  expect(observed).toHaveLength(1)
  const [message] = observed
  expect(sameId(message.AttemptId, attempt.Id)).toBe(true)
  // The completed planning attempt sealed all four purposes; the Codex double writes nothing to standard error, so that sealed file is a
  // legitimate empty one, and every other one has content.
  const artifacts = artifactsOfAttempt(attempt.Id)
  expect(artifacts.map((artifact) => [artifact.Purpose, artifact.ByteLength > 0])).toEqual([
    ['AgentContextManifest', true],
    ['AgentFinalResponse', true],
    ['AgentStandardError', false],
    ['AgentStandardOutput', true],
  ])
  const expectedText = new Map(artifacts.map((artifact) => [artifact.Purpose, sealedText(artifact)]))

  // Every request the page makes from here on, so the inspection can be proven read-only and addressed to this run and message only.
  const requests: { method: string; path: string }[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/')) {
      requests.push({ method: request.method(), path: url.pathname })
    }
  })
  const invocationsBefore = readInvocations(mark).length
  const stateBefore = durableState(repository.path, attempt.Id)

  // 2. Open the message's evidence in the live collaboration timeline: one read of exactly this message.
  const card = page.getByRole('region', { name: 'Agent collaboration' }).locator('article.dc-card').filter({ hasText: message.Summary })
  await expect(card).toHaveCount(1)
  const evidence = card.locator('details.dc-card-evidence')
  const evidenceAnswered = page.waitForResponse((response) => /\/collaboration-messages\/[^/]+\/evidence$/i.test(new URL(response.url()).pathname), {
    timeout: 30_000,
  })
  await evidence.getByText('Attempt evidence', { exact: true }).click()
  const evidenceResponse = await evidenceAnswered
  expect(evidenceResponse.status()).toBe(200)
  expect(lower(new URL(evidenceResponse.url()).pathname)).toBe(`/api/runs/${lower(runId)}/collaboration-messages/${lower(message.Id)}/evidence`)
  await expect(evidence).toContainText('Historical attempt evidence')

  // 3. Each recorded purpose is offered exactly once, and nothing else.
  const options = await evidence.getByRole('combobox').locator('option').allTextContents()
  expect(options[0]).toBe('Select an artifact…')
  expect(options.slice(1).sort()).toEqual(artifacts.map((artifact) => PURPOSE_LABELS[artifact.Purpose]).sort())

  // 4. Every offered purpose loads through the message route and shows exactly the verified sealed text.
  const shown = new Map<string, { empty: boolean; text: string }>()
  for (const artifact of artifacts) {
    shown.set(artifact.Purpose, await loadPurpose(page, evidence, artifact.Purpose, () => requests))
  }
  for (const artifact of artifacts) {
    expect(shown.get(artifact.Purpose)).toEqual({ empty: artifact.ByteLength === 0, text: expectedText.get(artifact.Purpose) })
  }

  // 5. Closing and reopening the evidence discards the loaded text and the purpose selection, without another evidence or window read.
  const readsBeforeToggle = requests.length
  await evidence.getByText('Attempt evidence', { exact: true }).click()
  await evidence.getByText('Attempt evidence', { exact: true }).click()
  await expect(evidence.getByRole('combobox')).toHaveValue('')
  await expect(evidence.locator('pre.dc-artifact-window-text')).toHaveCount(0)
  expect(requests.slice(readsBeforeToggle).filter((request) => WINDOW_ROUTE.test(request.path))).toEqual([])

  // Every evidence and window request named this run and message (one evidence read, at least one window per purpose), and the
  // inspection was read-only end to end.
  const messageRequests = requests.filter((request) => WINDOW_ROUTE.test(request.path))
  expect(messageRequests.filter((request) => request.path.endsWith('/evidence'))).toHaveLength(1)
  expect(messageRequests.length).toBeGreaterThanOrEqual(1 + artifacts.length)
  for (const request of messageRequests) {
    const [, requestedRun, requestedMessage, segment] = WINDOW_ROUTE.exec(request.path)!
    expect(request.method).toBe('GET')
    expect(sameId(requestedRun, runId)).toBe(true)
    expect(sameId(requestedMessage, message.Id)).toBe(true)
    expect(segment === undefined || Object.values(ROUTE_SEGMENTS).includes(segment)).toBe(true)
  }
  expect(requests.filter((request) => request.method !== 'GET')).toEqual([])
  expect(readInvocations(mark)).toHaveLength(invocationsBefore)
  expect(durableState(repository.path, attempt.Id)).toEqual(stateBefore)
})
