import { expect, test } from '@playwright/test'
import type { Locator } from '@playwright/test'
import { JourneyData, sameId, withJourneyDb } from './journey/journeyDb'
import { createJourneyRepository, markInvocations, readInvocations, readLaunchTargetVerdict } from './journey/journeyEnv'
import { PostInventory } from './journey/requestInventory'
import { injectJourneySession, registerProject, selectProject } from './journey/journeySupport'

// The browser-driven proof of the recorded planning inspection (ADR-0068), through the rendered controls of the production frontend over
// the real generated client, MVC authentication, mediator, SQLite, the planning coordinator and ALL supervisors. Only the provider
// executables are deterministic owned doubles. The database and the doubles' invocation log are only READ. Real planning through ONE
// rendered "Arm planning sequence" click records the whole five-step sequence (Planner, a challenging review, a Resolver, a second
// challenging review and a second Resolver, ending at the existing Human escalation); once that setup has settled, every request below is
// an explicit read. The journey opens the collapsed "Inspect recorded planning" region from the current run and again from Run history,
// reads the recorded index and one step's association, then reaches the existing Agent evidence: the metadata read, one recorded input
// and one recorded output (explicit content reads), one sealed artifact window and the step's input Proposal evidence. Nothing is
// implemented, verified, committed or published, and the inspection adds no business POST, no provider invocation and no durable change.
// A page reload is not a host restart; the file-backed Application and API tests prove host and database reopening separately. This is
// not provider reliability or proof that a real plan is good.

const PROJECT = 'Planning inspection journey fixture'
const OBJECTIVE = 'Inspect the recorded planning of the ledger total'
const ARM_PATH = /\/api\/runs\/[^/]+\/planning-sequence$/
const ROLES = ['Planner', 'CriticalReviewer', 'Resolver', 'CriticalReviewer', 'Resolver']
const PROVIDERS = ['Codex', 'ClaudeCode', 'Codex', 'ClaudeCode', 'Codex']
const CONTRACTS = ['Proposal', 'CriticalReview', 'ChallengeResolution', 'CriticalReview', 'ChallengeResolution']
const REGION = 'Inspect recorded planning'
const INDEX_MEMBERS = ['runId', 'sequence', 'status', 'steps']
const HEADER_MEMBERS = [
  'armedAtUtc', 'checkpointId', 'endedAtUtc', 'fingerprintSha256', 'reasonCode', 'recordedState', 'revision', 'sequenceId',
  'stopRequestedAtUtc', 'updatedAtUtc', 'workspaceId',
]
const STEP_MEMBERS = [
  'attemptId', 'inputProposalMessageId', 'ordinal', 'predecessorAttemptId', 'preparationDeadlineUtc', 'preparationReservedAtUtc', 'provider',
  'reasonCode', 'recordedState', 'responseContract', 'revision', 'role', 'stepId', 'updatedAtUtc',
]
const STEP_ANSWER_MEMBERS = ['agent', 'runId', 'sequenceId', 'status', 'step', 'stepId']
const AGENT_MEMBERS = ['attemptNumber', 'id', 'provider', 'responseContract', 'role']
const INPUT_MESSAGE_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts\/([^/]+)\/inputs\/([^/]+)$/i
const OUTPUT_MESSAGE_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts\/([^/]+)\/outputs\/([^/]+)$/i
const ARTIFACT_WINDOW_ROUTE = /\/api\/runs\/([^/]+)\/agent-attempts\/([^/]+)\/evidence\/artifact-window\//i
const MESSAGE_EVIDENCE_ROUTE = /\/api\/runs\/([^/]+)\/collaboration-messages\/([^/]+)\/evidence$/i

const data = new JourneyData(PROJECT)
const lower = (value: string) => value.toLowerCase()

interface SequenceRow {
  Id: string
  State: string
  ReasonCode: string | null
  Revision: number
  WorkspaceId: string
  CheckpointId: string
  FingerprintSha256: string
}
interface StepRow {
  Id: string
  Ordinal: number
  Role: string
  State: string
  Revision: number
  AttemptId: string | null
  PredecessorAttemptId: string | null
  InputProposalMessageId: string | null
}
interface ObservationRow {
  AttemptId: string
  Revision: number
  ClosedAtUtc: string | null
}

function runId(): string {
  return data.runId()
}
function sequenceOf(id: string): SequenceRow {
  const rows = withJourneyDb(
    (db) =>
      db
        .prepare('select Id, State, ReasonCode, Revision, WorkspaceId, CheckpointId, FingerprintSha256 from planning_sequences where RunId = ?')
        .all(id) as unknown as SequenceRow[],
  )
  expect(rows).toHaveLength(1)
  return rows[0]
}
function stepsOf(sequenceId: string): StepRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select Id, Ordinal, Role, State, Revision, AttemptId, PredecessorAttemptId, InputProposalMessageId '
            + 'from planning_sequence_steps where SequenceId = ? order by Ordinal',
        )
        .all(sequenceId) as unknown as StepRow[],
  )
}
function observationsOf(id: string): ObservationRow[] {
  return withJourneyDb(
    (db) =>
      db
        .prepare(
          'select s.AttemptId, s.Revision, s.ClosedAtUtc from agent_supervisions s join attempts a on a.Id = s.AttemptId '
            + 'where s.RunId = ? order by a.AttemptNumber',
        )
        .all(id) as unknown as ObservationRow[],
  )
}
function count(sql: string, ...parameters: string[]): number {
  return withJourneyDb((db) => (db.prepare(sql).get(...parameters) as { N: number }).N)
}

/**
 * The durable fingerprint of the inspection segment. It covers exactly: the run's event, collaboration message, attempt and attempt-input
 * row counts; each attempt's status and outcome; the sequence row's state, reason code and revision; each step row's state, revision and
 * attempt link; and each host observation's revision and closure marker. It is a selected-field and row-count comparison, not a byte
 * identity of every stored row, of the artifact files or of any other run's rows.
 */
function durableState(id: string): string {
  const sequence = sequenceOf(id)
  return JSON.stringify({
    events: count('select count(*) as N from events where RunId = ?', id),
    messages: count('select count(*) as N from collaboration_messages where RunId = ?', id),
    attempts: count('select count(*) as N from attempts where RunId = ?', id),
    inputs: count('select count(*) as N from attempt_input_messages where AttemptId in (select Id from attempts where RunId = ?)', id),
    attemptFacts: data.attempts().map((attempt) => [attempt.Id, attempt.Status, attempt.AgentOutcome]),
    sequence: [sequence.State, sequence.ReasonCode, sequence.Revision],
    steps: stepsOf(sequence.Id).map((step) => [step.Id, step.State, step.Revision, step.AttemptId]),
    observations: observationsOf(id).map((observation) => [observation.AttemptId, observation.Revision, observation.ClosedAtUtc]),
  })
}

test('the recorded planning inspection journey: a finished five-step sequence is inspected from the current run and run history, step by step, without writing anything', async ({
  page,
}) => {
  test.setTimeout(540_000)
  const mark = markInvocations()
  const repository = createJourneyRepository('planning-inspection-source')
  // Every request the page issues. The run notification hub (LongPolling) posts at moments no business action controls; the inventory keeps
  // that traffic in its raw record but never counts it as a business command.
  const posts = new PostInventory()
  const gets: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST') {
      posts.record(path)
    }
    if (request.method() === 'GET') {
      gets.push(path)
    }
  })

  await injectJourneySession(page)
  await page.goto('/')
  await expect(page.getByText('No launch session is available')).toHaveCount(0)
  await expect.poll(() => readLaunchTargetVerdict()?.verified, { timeout: 60_000 }).toBe(true)

  // 1. Setup through the rendered controls: the project, its workspace and checkpoint, a manual run and ONE arming of real planning.
  await registerProject(page, PROJECT, repository.path)
  await selectProject(page, PROJECT)
  await page.getByRole('button', { name: 'Recheck identity' }).click()
  await page.getByRole('button', { name: 'Prepare workspace' }).click()
  await page.getByRole('button', { name: 'Capture checkpoint' }).click()
  await expect(page.getByRole('region', { name: 'Source evidence' })).toContainText('Checkpoint #1 · 0 changed files', { timeout: 30_000 })
  await page.getByRole('textbox', { name: 'Objective' }).fill(OBJECTIVE)
  await page.getByRole('button', { name: 'Record manual run' }).click()
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 15_000 })

  const planning = page.getByRole('region', { name: 'Planning sequence' })
  await expect(planning).toContainText('No planning sequence is armed', { timeout: 30_000 })
  const armed = page.waitForResponse((response) => response.request().method() === 'POST' && ARM_PATH.test(new URL(response.url()).pathname), {
    timeout: 30_000,
  })
  await planning.getByRole('button', { name: 'Arm planning sequence' }).click()
  expect((await armed).status()).toBeLessThan(300)
  await expect(planning.getByRole('status', { name: 'Planning sequence state' })).toHaveAttribute('data-state', 'AwaitingHumanDecision', {
    timeout: 240_000,
  })

  // 2. The five-step scene as the host recorded it, and the setup settled: every host observation is closed, so nothing below can be
  // changed by a late write of the setup itself.
  const id = runId()
  const sequence = sequenceOf(id)
  expect(sequence.State).toBe('AwaitingHumanDecision')
  const steps = stepsOf(sequence.Id)
  expect(steps.map((step) => step.Role)).toEqual(ROLES)
  expect(steps.every((step) => step.State === 'Concluded')).toBe(true)
  const attempts = data.attempts()
  expect(attempts.map((attempt) => attempt.AgentRole)).toEqual(ROLES)
  expect(attempts.every((attempt) => attempt.Status === 'Completed')).toBe(true)
  steps.forEach((step, index) => {
    expect(sameId(step.AttemptId, attempts[index].Id)).toBe(true)
    if (index > 0) {
      expect(sameId(step.PredecessorAttemptId, attempts[index - 1].Id)).toBe(true)
    }
  })
  await expect.poll(() => observationsOf(id).filter((observation) => observation.ClosedAtUtc !== null).length, { timeout: 60_000 }).toBe(5)
  const messages = data.messages()

  // 3. The baseline, taken after the setup settled: every business POST, every provider invocation and the durable fingerprint (its exact
  // coverage is documented on `durableState`). The inspection segment below must add none of them.
  const durableBefore = durableState(id)
  const postsBefore = posts.mark()
  const businessBefore = posts.business().length
  const invocationsBefore = readInvocations(mark).filter((entry) => entry.kind !== 'probe').length
  expect(invocationsBefore).toBe(5)
  const getsBefore = gets.length
  const indexPath = `/api/runs/${lower(id)}/planning-sequence/inspection`
  const stepPath = (stepId: string) => `${indexPath}/steps/${lower(stepId)}`
  const inspectionReads = (start: number) => gets.slice(start).filter((path) => path.includes('/planning-sequence/inspection'))

  // The cold closed state: the region is collapsed and nothing of the inspection or of any attempt was read to render the page.
  const rail = page.getByRole('complementary', { name: 'Usage and evidence' })
  const current = rail.getByRole('region', { name: REGION })
  await expect(current.getByRole('button', { name: REGION, exact: true })).toHaveAttribute('aria-expanded', 'false')
  expect(inspectionReads(getsBefore)).toEqual([])

  // The live cockpit reads its own kind-named eligibility lists (agent-attempts/codex-plan and so on); only a read of one attempt by its
  // identity, or of a collaboration message, is attempt or message evidence.
  const evidenceReads = (start: number) =>
    gets.slice(start).filter((path) => /\/agent-attempts\/[0-9a-f-]{36}(\/|$)|\/collaboration-messages\//i.test(path) && path.includes(lower(id)))

  // Opens the region inside `scope`, awaits the actual index response and checks the whole body and what was rendered for it.
  const openIndex = async (scope: Locator) => {
    const start = gets.length
    const region = scope.getByRole('region', { name: REGION })
    const toggle = region.getByRole('button', { name: REGION, exact: true })
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(inspectionReads(start)).toEqual([])
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === indexPath,
      { timeout: 30_000 },
    )
    await toggle.click()
    const response = await answered
    expect(response.status()).toBe(200)
    const body = (await response.json()) as Record<string, unknown> & { sequence: Record<string, unknown>; steps: Record<string, unknown>[] }
    await expect(region.getByRole('button', { name: /^Inspect planning step \d+$/ })).toHaveCount(5, { timeout: 30_000 })
    expect(inspectionReads(start)).toEqual([indexPath])
    return { region, body, start }
  }

  const assertIndex = async (opened: Awaited<ReturnType<typeof openIndex>>) => {
    const { region, body } = opened
    expect(Object.keys(body).sort()).toEqual(INDEX_MEMBERS)
    expect(body.status).toBe('Recorded')
    expect(sameId(String(body.runId), id)).toBe(true)
    expect(Object.keys(body.sequence).sort()).toEqual(HEADER_MEMBERS)
    expect(body.sequence).toMatchObject({
      recordedState: 'AwaitingHumanDecision',
      reasonCode: 'second_round_escalated',
      revision: String(sequence.Revision),
      fingerprintSha256: sequence.FingerprintSha256,
      stopRequestedAtUtc: null,
    })
    expect(sameId(String(body.sequence.sequenceId), sequence.Id)).toBe(true)
    expect(sameId(String(body.sequence.workspaceId), sequence.WorkspaceId)).toBe(true)
    expect(sameId(String(body.sequence.checkpointId), sequence.CheckpointId)).toBe(true)
    expect(body.steps).toHaveLength(5)
    body.steps.forEach((step, index) => {
      expect(Object.keys(step).sort()).toEqual(STEP_MEMBERS)
      expect(step).toMatchObject({
        ordinal: index,
        role: ROLES[index],
        provider: PROVIDERS[index],
        responseContract: CONTRACTS[index],
        recordedState: 'Concluded',
        reasonCode: null,
        revision: String(steps[index].Revision),
      })
      expect(sameId(String(step.stepId), steps[index].Id)).toBe(true)
      expect(sameId(String(step.attemptId), attempts[index].Id)).toBe(true)
      expect(sameId(step.predecessorAttemptId as string | null, steps[index].PredecessorAttemptId)).toBe(index > 0)
      expect(sameId(step.inputProposalMessageId as string | null, steps[index].InputProposalMessageId)).toBe(index > 0)
    })
    // The rendered identity is the host's own answer: the sequence, its recorded state and reason, and every step with its fixed role.
    await expect(region).toContainText(sequence.Id.toLowerCase())
    await expect(region).toContainText('Awaiting a Human decision')
    await expect(region).toContainText('recorded as escalated after the second round')
    await expect(region).toContainText(`revision ${sequence.Revision}`)
    await expect(region).toContainText('bookkeeping, not proof that a plan was accepted')
    for (const [index, role] of ['Planner', 'Critical review', 'Resolver', 'Critical review', 'Resolver'].entries()) {
      await expect(region.locator('ol.dc-planning-inspection-steps > li').nth(index)).toContainText(`Step ${index + 1} · ${role}`)
    }
    // No arming, stop, implementation, recovery, approval or publication control exists inside the region.
    await expect(region.getByRole('button', { name: /arm|stop|implement|recover|approve|publish|commit|release|retry/i })).toHaveCount(0)
  }

  // Selects one step and returns its answer with the region it rendered. Only that step's association is read.
  const inspectStep = async (opened: Awaited<ReturnType<typeof openIndex>>, ordinal: number) => {
    const { region } = opened
    const start = gets.length
    const step = steps[ordinal]
    const answered = page.waitForResponse(
      (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === stepPath(step.Id),
      { timeout: 30_000 },
    )
    await region.getByRole('button', { name: `Inspect planning step ${ordinal + 1}`, exact: true }).click()
    const response = await answered
    expect(response.status()).toBe(200)
    const body = (await response.json()) as Record<string, unknown> & { step: Record<string, unknown>; agent: Record<string, unknown> }
    expect(Object.keys(body).sort()).toEqual(STEP_ANSWER_MEMBERS)
    expect(body.status).toBe('Recorded')
    expect(sameId(String(body.runId), id)).toBe(true)
    expect(sameId(String(body.sequenceId), sequence.Id)).toBe(true)
    expect(sameId(String(body.stepId), step.Id)).toBe(true)
    expect(Object.keys(body.step).sort()).toEqual(STEP_MEMBERS)
    expect(Object.keys(body.agent).sort()).toEqual(AGENT_MEMBERS)
    expect(body.agent).toMatchObject({
      attemptNumber: attempts[ordinal].AttemptNumber,
      role: ROLES[ordinal],
      provider: PROVIDERS[ordinal],
      responseContract: CONTRACTS[ordinal],
    })
    expect(sameId(String(body.agent.id), attempts[ordinal].Id)).toBe(true)
    const detail = region.getByRole('region', { name: 'Selected planning step' })
    await expect(detail).toContainText(`Recorded attempt link`)
    await expect(detail).toContainText(attempts[ordinal].Id.toLowerCase())
    await expect(detail.getByRole('region', { name: 'Selected Agent attempt evidence' })).toBeVisible({ timeout: 30_000 })
    // Exactly this one step was read from the inspection; no other step was prefetched.
    expect(inspectionReads(start)).toEqual([stepPath(step.Id)])
    return { detail, body }
  }

  // 4. THE CURRENT RUN: open, read the index, select the first review, then reach the existing Agent evidence explicitly.
  const fromRail = await openIndex(rail)
  await assertIndex(fromRail)

  const reviewStart = gets.length
  const review = await inspectStep(fromRail, 1)
  const reviewAttempt = attempts[1]
  const reviewStep = steps[1]
  // Selecting the step read only the association and the Agent detail's ordinary metadata (the existing evidence read), no content.
  expect(evidenceReads(reviewStart).filter((path) => /\/(inputs|outputs|artifact-window)/i.test(path))).toEqual([])
  expect(evidenceReads(reviewStart).filter((path) => !new RegExp(`/agent-attempts/${lower(reviewAttempt.Id)}/evidence$`, 'i').test(path))).toEqual([])

  // The reviewer's recorded input is the Proposal its step names: an explicit content read of exactly that message.
  const inputs = data.inputsOf(reviewAttempt.Id).sort((left, right) => left.Sequence - right.Sequence)
  expect(sameId(inputs[0].CollaborationMessageId, reviewStep.InputProposalMessageId)).toBe(true)
  const inputsSection = review.detail.getByRole('region', { name: 'Recorded collaboration inputs' })
  await inputsSection.getByRole('button', { name: 'Recorded collaboration inputs', exact: true }).click()
  await expect(inputsSection.getByRole('button', { name: 'Inspect input 1', exact: true })).toBeVisible({ timeout: 30_000 })
  const contentReadsBefore = gets.filter((path) => INPUT_MESSAGE_ROUTE.test(path)).length
  expect(contentReadsBefore).toBe(0)
  const inputAnswered = page.waitForResponse((response) => INPUT_MESSAGE_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
  await inputsSection.getByRole('button', { name: 'Inspect input 1', exact: true }).click()
  const inputResponse = await inputAnswered
  expect(inputResponse.status()).toBe(200)
  expect(lower(new URL(inputResponse.url()).pathname)).toBe(
    `/api/runs/${lower(id)}/agent-attempts/${lower(reviewAttempt.Id)}/inputs/${lower(reviewStep.InputProposalMessageId!)}`,
  )
  const proposal = messages.find((message) => sameId(message.Id, reviewStep.InputProposalMessageId))!
  expect(proposal.Type).toBe('Proposal')
  expect(sameId(proposal.AttemptId, attempts[0].Id)).toBe(true)
  await expect(inputsSection.getByRole('region', { name: 'Selected recorded input' })).toContainText(`Summary: ${proposal.Summary}`, {
    timeout: 15_000,
  })

  // The reviewer's recorded output: the first Challenge it produced, again an explicit read of exactly that message.
  const outputsOfReview = messages.filter((message) => sameId(message.AttemptId, reviewAttempt.Id)).sort((left, right) => left.Sequence - right.Sequence)
  expect(outputsOfReview.length).toBeGreaterThanOrEqual(1)
  const outputsSection = review.detail.getByRole('region', { name: 'Recorded collaboration outputs' })
  await outputsSection.getByRole('button', { name: 'Recorded collaboration outputs', exact: true }).click()
  await expect(outputsSection.getByRole('button', { name: 'Inspect output 1', exact: true })).toBeVisible({ timeout: 30_000 })
  const outputAnswered = page.waitForResponse((response) => OUTPUT_MESSAGE_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
  await outputsSection.getByRole('button', { name: 'Inspect output 1', exact: true }).click()
  const outputResponse = await outputAnswered
  expect(outputResponse.status()).toBe(200)
  expect(lower(new URL(outputResponse.url()).pathname)).toBe(
    `/api/runs/${lower(id)}/agent-attempts/${lower(reviewAttempt.Id)}/outputs/${lower(outputsOfReview[0].Id)}`,
  )
  await expect(outputsSection.getByRole('region', { name: 'Selected recorded output' })).toContainText(`Summary: ${outputsOfReview[0].Summary}`, {
    timeout: 15_000,
  })

  // One sealed artifact of the same attempt: selecting a purpose reads nothing until Load, then exactly one verified window.
  const artifactSelect = review.detail.getByRole('combobox')
  await expect(artifactSelect).toBeVisible({ timeout: 30_000 })
  await artifactSelect.selectOption('AgentFinalResponse')
  expect(gets.slice(reviewStart).filter((path) => ARTIFACT_WINDOW_ROUTE.test(path))).toEqual([])
  const windowAnswered = page.waitForResponse((response) => ARTIFACT_WINDOW_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
  await review.detail.getByRole('button', { name: 'Load', exact: true }).click()
  const windowResponse = await windowAnswered
  expect(windowResponse.status()).toBe(200)
  expect(lower(new URL(windowResponse.url()).pathname)).toContain(`/agent-attempts/${lower(reviewAttempt.Id)}/evidence/artifact-window/`)
  const windowBody = (await windowResponse.json()) as { text?: string }
  await expect(review.detail.locator('pre.dc-artifact-window-text')).toHaveText(windowBody.text ?? '', { timeout: 15_000 })
  expect(gets.slice(reviewStart).filter((path) => ARTIFACT_WINDOW_ROUTE.test(path))).toHaveLength(1)

  // The step's input Proposal opens only on request through the existing message inspector: its evidence names the Planner attempt.
  const inputPanel = review.detail.locator('.dc-planning-inspection-input')
  await expect(inputPanel).toContainText(String(reviewStep.InputProposalMessageId).toLowerCase())
  expect(gets.slice(reviewStart).filter((path) => MESSAGE_EVIDENCE_ROUTE.test(path))).toEqual([])
  const proposalEvidence = page.waitForResponse((response) => MESSAGE_EVIDENCE_ROUTE.test(new URL(response.url()).pathname), { timeout: 30_000 })
  await inputPanel.getByText('Attempt evidence', { exact: true }).click()
  const proposalResponse = await proposalEvidence
  expect(proposalResponse.status()).toBe(200)
  expect(lower(new URL(proposalResponse.url()).pathname)).toBe(
    `/api/runs/${lower(id)}/collaboration-messages/${lower(reviewStep.InputProposalMessageId!)}/evidence`,
  )
  const proposalBody = (await proposalResponse.json()) as { attemptId?: string; attemptNumber?: number }
  expect(sameId(proposalBody.attemptId, attempts[0].Id)).toBe(true)
  expect(proposalBody.attemptNumber).toBe(attempts[0].AttemptNumber)
  await expect(inputPanel.locator('dt').first()).toHaveText('Attempt number', { timeout: 15_000 })
  await expect(inputPanel.locator('dd').first()).toHaveText(String(attempts[0].AttemptNumber))
  await expect(inputPanel).toContainText('Planner')

  // 5. An explicit reload replaces the read: exactly one new index request, whose own response is awaited from before the click, carries
  // the same durable answer and is the one the region renders; the selection and every content of the earlier visit end with it.
  const reloadStart = gets.length
  const reloadAnswered = page.waitForResponse(
    (response) => response.request().method() === 'GET' && lower(new URL(response.url()).pathname) === indexPath,
    { timeout: 30_000 },
  )
  await fromRail.region.getByRole('button', { name: 'Reload recorded planning', exact: true }).click()
  const reloadResponse = await reloadAnswered
  expect(reloadResponse.status()).toBe(200)
  expect(await reloadResponse.json()).toEqual(fromRail.body)
  await expect(fromRail.region.getByText('Reading the recorded planning sequence')).toHaveCount(0, { timeout: 30_000 })
  await expect(fromRail.region.getByRole('region', { name: 'Selected planning step' })).toHaveCount(0)
  await expect(fromRail.region.getByRole('region', { name: 'Selected Agent attempt evidence' })).toHaveCount(0)
  await assertIndex({ ...fromRail, body: (await reloadResponse.json()) as typeof fromRail.body })
  expect(inspectionReads(reloadStart)).toEqual([indexPath])

  // 6. THE SELECTED HISTORICAL RUN: the same recorded sequence through Run history, again only on explicit actions.
  const history = page.getByRole('region', { name: 'Run history' })
  await history.getByRole('button', { name: 'Show run history' }).click()
  await history.getByRole('button', { name: 'Inspect run #1', exact: true }).click()
  const selectedRun = history.getByRole('region', { name: 'Selected run' })
  await expect(selectedRun).toContainText(OBJECTIVE, { timeout: 30_000 })
  const fromHistory = await openIndex(selectedRun)
  await assertIndex(fromHistory)
  expect(fromHistory.body).toEqual(fromRail.body)
  // The last step (the second Resolver) names the second review's Proposal and links the fifth attempt.
  const resolver = await inspectStep(fromHistory, 4)
  await expect(resolver.detail).toContainText('Recorded input Proposal this step names')
  await selectedRun.getByRole('button', { name: 'Close selected run' }).click()
  await expect(selectedRun).toHaveCount(0)

  // 7. A page reload is not a host restart, but it does start the page cold: marked BEFORE the reload, the reload and the closed state
  // read no inspection route and no attempt or message evidence, so an empty read cannot come from a mark taken after the page loaded.
  const postsBeforeReload = posts.mark()
  const getsBeforeReload = gets.length
  await page.reload()
  await selectProject(page, PROJECT)
  await expect(page.getByRole('heading', { name: OBJECTIVE })).toBeVisible({ timeout: 30_000 })
  const reloadedRegion = page.getByRole('complementary', { name: 'Usage and evidence' }).getByRole('region', { name: REGION })
  await expect(reloadedRegion.getByRole('button', { name: REGION, exact: true })).toHaveAttribute('aria-expanded', 'false', {
    timeout: 30_000,
  })
  expect(inspectionReads(getsBeforeReload)).toEqual([])
  expect(evidenceReads(getsBeforeReload)).toEqual([])
  expect(posts.businessSince(postsBeforeReload)).toEqual([])
  const afterReload = await openIndex(page.getByRole('complementary', { name: 'Usage and evidence' }))
  await assertIndex(afterReload)
  expect(afterReload.body).toEqual(fromRail.body)
  expect(inspectionReads(getsBeforeReload)).toEqual([indexPath])

  // 8. The whole inspection segment is read-only: no business POST beyond the one arming of the setup, no provider invocation, and the
  // durable fingerprint (counts and the selected fields named on `durableState`) is exactly what it was when the setup settled.
  expect(posts.businessSince(postsBefore)).toEqual([])
  expect(posts.business()).toHaveLength(businessBefore)
  expect(posts.business().filter((path) => ARM_PATH.test(path))).toHaveLength(1)
  expect(readInvocations(mark).filter((entry) => entry.kind !== 'probe')).toHaveLength(invocationsBefore)
  expect(durableState(id)).toBe(durableBefore)
  expect(sequenceOf(id).State).toBe('AwaitingHumanDecision')
  expect(stepsOf(sequence.Id)).toHaveLength(5)
  expect(data.attempts()).toHaveLength(5)
})
