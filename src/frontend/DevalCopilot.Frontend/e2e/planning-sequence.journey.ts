import { expect, test } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the explicitly authorized read-only planning sequence (ADR-0055), through the rendered controls of the
// production frontend over the real generated client, MVC authentication, mediator, SQLite, the planning coordinator and ALL
// supervisors. Only the provider executables are deterministic owned doubles. The database and the doubles' invocation log are only
// READ. A single rendered "Arm planning sequence" click is the only request that starts anything: the host then claims the Planner,
// the CriticalReviewer and the Resolver steps itself, and the fixture's second review challenges again, so the sequence ends in the
// existing depth-two Human escalation (AwaitingHumanDecision) after exactly five attempts. Nothing is implemented, verified,
// committed or published. This is not provider reliability or proof that a real plan is good.

const PROJECT = 'Planning sequence journey fixture'
const OBJECTIVE = 'Plan the ledger total through one authorized sequence'
const ARM_PATH = /\/api\/runs\/[^/]+\/planning-sequence$/
const ROLES = ['Planner', 'CriticalReviewer', 'Resolver', 'CriticalReviewer', 'Resolver']

const data = new JourneyData(PROJECT)

interface RunRow {
  Id: string
  Lifecycle: string
  Stage: string
}
interface SequenceRow {
  Id: string
  State: string
  ReasonCode: string | null
}
interface StepRow {
  Ordinal: number
  Role: string
  State: string
  AttemptId: string | null
  ReservedAttemptId: string
  PredecessorAttemptId: string | null
}
interface AttemptRow {
  Id: string
  AttemptNumber: number
  Status: string
  AgentRole: string
}

function runs(): RunRow[] {
  return withJourneyDb(
    (db) => db.prepare('select Id, Lifecycle, Stage from runs where ProjectId = ? order by ExecutionNumber').all(data.projectId()) as unknown as RunRow[],
  )
}
function sequencesOf(runId: string): SequenceRow[] {
  return withJourneyDb(
    (db) => db.prepare('select Id, State, ReasonCode from planning_sequences where RunId = ?').all(runId) as unknown as SequenceRow[],
  )
}
function stepsOf(sequenceId: string): StepRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Ordinal, Role, State, AttemptId, ReservedAttemptId, PredecessorAttemptId from planning_sequence_steps where SequenceId = ? order by Ordinal')
        .all(sequenceId) as unknown as StepRow[],
  )
}
function attemptsOf(runId: string): AttemptRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare('select Id, AttemptNumber, Status, AgentRole from attempts where RunId = ? order by AttemptNumber')
        .all(runId) as unknown as AttemptRow[],
  )
}

test('the planning sequence journey: arm once, the host runs every read-only step, and the Human escalation ends it', async ({ page }) => {
  test.setTimeout(300_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('planning-sequence-source')
  const armRequests: string[] = []
  const claimRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() !== 'POST') {
      return
    }
    const path = new URL(request.url()).pathname
    if (ARM_PATH.test(path)) {
      armRequests.push(path)
    } else if (/\/api\/runs\/[^/]+\/agent-attempts\//.test(path)) {
      claimRequests.push(path)
    }
  })

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

  const [run] = runs()
  expect(sequencesOf(run.Id)).toEqual([])
  expect(attemptsOf(run.Id)).toEqual([])

  // The single explicit authorization.
  const panel = page.getByRole('region', { name: 'Planning sequence' })
  await expect(panel).toContainText('No planning sequence is armed', { timeout: 30_000 })
  const armed = page.waitForResponse((response) => response.request().method() === 'POST' && ARM_PATH.test(new URL(response.url()).pathname), {
    timeout: 30_000,
  })
  await panel.getByRole('button', { name: 'Arm planning sequence' }).click()
  expect((await armed).status()).toBeLessThan(300)

  // The host alone advances; the page only observes. The fixture's second review challenges again: the existing Human escalation.
  const state = panel.getByRole('status', { name: 'Planning sequence state' })
  await expect(state).toHaveAttribute('data-state', 'AwaitingHumanDecision', { timeout: 240_000 })

  const [sequence] = sequencesOf(run.Id)
  expect(sequencesOf(run.Id)).toHaveLength(1)
  expect(sequence.State).toBe('AwaitingHumanDecision')
  const steps = stepsOf(sequence.Id)
  expect(steps.map((step) => step.Role)).toEqual(ROLES)
  expect(steps.every((step) => step.State === 'Concluded')).toBe(true)
  const attempts = attemptsOf(run.Id)
  expect(attempts.map((attempt) => attempt.AgentRole)).toEqual(ROLES)
  expect(attempts.every((attempt) => attempt.Status === 'Completed')).toBe(true)
  steps.forEach((step, index) => {
    expect(sameId(step.AttemptId!, attempts[index].Id)).toBe(true)
    expect(sameId(step.ReservedAttemptId, attempts[index].Id)).toBe(true)
    if (index > 0) {
      expect(sameId(step.PredecessorAttemptId!, attempts[index - 1].Id)).toBe(true)
    }
  })

  // One arming request and no per-stage or implementation request from the browser; the provider doubles ran exactly the five steps.
  expect(armRequests).toHaveLength(1)
  expect(claimRequests).toEqual([])
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(5)
  expect(runs()[0].Lifecycle).not.toBe('Completed')

  // A reload reads the durable result back and never resumes or re-arms anything.
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('region', { name: 'Planning sequence' }).getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute(
    'data-state',
    'AwaitingHumanDecision',
    { timeout: 30_000 },
  )
  expect(armRequests).toHaveLength(1)
  expect(attemptsOf(run.Id)).toHaveLength(5)
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(5)
})
