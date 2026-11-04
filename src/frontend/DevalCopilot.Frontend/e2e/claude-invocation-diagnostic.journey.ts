import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the closed Claude invocation diagnostic (ADR-0072). The Claude double answers this repository's critical
// review, and only it, with the documented error report of a rejected credential (`is_error` true, `api_error_status` 401) on exit code
// 1. Everything else is the production path: the rendered controls, the generated client, MVC authentication, mediator, SQLite, the
// supervisors, the shared envelope reader and the bounded process adapter. The database and the doubles' invocation log are only READ.
//
// It proves: the failed attempt records exactly one AuthenticationRequired diagnostic with its version beside its complete I/O facts; the
// current run's Agent attempt evidence and, after the run is abandoned, the same attempt reached through Run history show the fixed copy
// and caveat; the successful Planner attempt shows none; and the provider's own narrative never reaches the page. It is not a claim about
// any real account, credential or installed Claude Code version.

const PROJECT = 'Claude invocation diagnostic journey fixture'
const OBJECTIVE = 'Plan the ledger total, then meet a reported authentication error'
const SECOND_OBJECTIVE = 'Record a newer objective after the failed review'
const REASON = 'The review could not run; a newer objective replaces it.'
const NARRATIVE = 'FIXTURE-PROVIDER-NARRATIVE-NEVER-SHOWN'
const DIAGNOSTIC_TEXT =
  'Invocation diagnostic (AuthenticationRequired): Claude Code reported an authentication error (HTTP 401). Sign in to Claude Code outside DevalCopilot before requesting a new attempt. This describes what was observed or reported; it is not a verified cause.'

const data = new JourneyData(PROJECT)

interface AttemptRow {
  Id: string
  RunId: string
  AttemptNumber: number
  Status: string
  AgentRole: string
  AgentOutcome: string | null
  AgentProcessOutcome: string | null
  AgentProcessExitCode: number | null
  AgentAdapterContractVersion: string | null
  AgentInvocationDiagnostic: string | null
  AgentInvocationDiagnosticVersion: string | null
  AgentStandardOutputCompletion: string | null
  AgentStandardErrorCompletion: string | null
  AgentStandardInputDelivery: string | null
}

function attemptsOfProject(): AttemptRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select a.Id, a.RunId, a.AttemptNumber, a.Status, a.AgentRole, a.AgentOutcome, a.AgentProcessOutcome, a.AgentProcessExitCode, '
            + 'a.AgentAdapterContractVersion, a.AgentInvocationDiagnostic, a.AgentInvocationDiagnosticVersion, a.AgentStandardOutputCompletion, '
            + 'a.AgentStandardErrorCompletion, a.AgentStandardInputDelivery '
            + 'from attempts a join runs r on r.Id = a.RunId where r.ProjectId = ? order by r.ExecutionNumber, a.AttemptNumber',
        )
        .all(data.projectId()) as unknown as AttemptRow[],
  )
}

function rail(page: Page) {
  return page.getByRole('complementary', { name: 'Usage and evidence' })
}

/** Opens one attempt of an opened attempt history and returns its evidence region once it shows that attempt. */
async function inspectAttempt(attempts: Locator, attemptNumber: number, summary: string): Promise<Locator> {
  await attempts.getByRole('button', { name: `Inspect attempt ${attemptNumber}`, exact: true }).click()
  const detail = attempts.getByRole('region', { name: 'Selected Agent attempt evidence' })
  await expect(detail).toContainText(summary, { timeout: 30_000 })
  return detail
}

async function expectDiagnosticEvidence(attempts: Locator) {
  const failed = await inspectAttempt(attempts, 2, 'Attempt #2 · CriticalReviewer · ClaudeCode · Failed · Result: ProviderInvocationFailed')
  await expect(failed.getByLabel('Invocation diagnostic')).toHaveText(DIAGNOSTIC_TEXT)
  await expect(failed).not.toContainText(NARRATIVE)
  await expect(failed).not.toContainText('output capture did not complete')

  const planned = await inspectAttempt(attempts, 1, 'Attempt #1 · Planner · Codex · Completed')
  await expect(planned.getByLabel('Invocation diagnostic')).toHaveCount(0)
}

test('the Claude invocation diagnostic journey: a reported authentication error is recorded once and shown with fixed copy', async ({ page }) => {
  test.setTimeout(300_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('claude-diagnostic-source')

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)

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
  await page.getByRole('button', { name: 'Request Claude review' }).click()
  await expect(page.getByText('Last attempt #2: Claude could not be invoked.')).toBeVisible({ timeout: 60_000 })

  // The durable record: one closed category with its version, beside the observed non-zero exit and explicitly complete I/O. The
  // successful Planner recorded none.
  const [planner, reviewer] = attemptsOfProject()
  expect(planner).toMatchObject({ AttemptNumber: 1, Status: 'Completed', AgentRole: 'Planner', AgentInvocationDiagnostic: null, AgentInvocationDiagnosticVersion: null })
  expect(reviewer).toMatchObject({
    AttemptNumber: 2,
    Status: 'Failed',
    AgentRole: 'CriticalReviewer',
    AgentOutcome: 'ProviderInvocationFailed',
    AgentProcessOutcome: 'Exited',
    AgentProcessExitCode: 1,
    AgentAdapterContractVersion: 'claude-critical-review-v3',
    AgentInvocationDiagnostic: 'AuthenticationRequired',
    AgentInvocationDiagnosticVersion: 'agent-invocation-diagnostic-v1',
    AgentStandardOutputCompletion: 'Complete',
    AgentStandardErrorCompletion: 'Complete',
    AgentStandardInputDelivery: 'Complete',
  })
  const reviews = readInvocations(mark).filter((entry) => entry.role === 'claude' && entry.kind === 'print')
  expect(reviews.map((entry) => entry.outcome)).toEqual(['reported_api_error_401'])

  // The current run's Agent attempt evidence.
  const current = rail(page).getByRole('region', { name: 'Agent attempt history' })
  await current.getByRole('button', { name: 'Show Agent attempt history' }).click()
  await expectDiagnosticEvidence(current)
  await expect(page.locator('body')).not.toContainText(NARRATIVE)

  // The same attempt reached through Run history once the run is abandoned and a newer objective recorded.
  const abandon = page.getByRole('region', { name: 'Abandon run' })
  await abandon.getByLabel('Reason').fill(REASON)
  await page.getByRole('button', { name: 'Abandon run', exact: true }).click()
  await expect(abandon).toContainText(REASON, { timeout: 30_000 })
  await expect(page.getByRole('textbox', { name: 'Objective' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(SECOND_OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: SECOND_OBJECTIVE })).toBeVisible({ timeout: 15_000 })

  const history = page.getByRole('region', { name: 'Run history' })
  await history.getByRole('button', { name: 'Show run history' }).click()
  await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
  const selectedRun = history.getByRole('region', { name: 'Selected run' })
  await expect(selectedRun).toContainText(OBJECTIVE)
  const historical = selectedRun.getByRole('region', { name: 'Agent attempt history' })
  await historical.getByRole('button', { name: 'Show Agent attempt history' }).click()
  await expectDiagnosticEvidence(historical)
  await expect(page.locator('body')).not.toContainText(NARRATIVE)

  // Inspection is read-only: nothing new was invoked or recorded, and the diagnostic is unchanged.
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(2)
  const after = attemptsOfProject()
  expect(after).toHaveLength(2)
  expect(sameId(after[1].Id, reviewer.Id)).toBe(true)
  expect(after[1].AgentInvocationDiagnostic).toBe('AuthenticationRequired')
})
