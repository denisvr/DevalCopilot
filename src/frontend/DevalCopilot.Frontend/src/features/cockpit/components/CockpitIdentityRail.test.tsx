// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CodexAccountUsageStopResponse, CodexAccountUsageWarningSettingResponse } from '../../../api/generated/api-client'
import { attemptEvidenceFor, attemptIdFor, attemptPageOf } from '../agentAttemptHistoryFixture'
import * as local from '../localExecutionInspectionFixture'
import * as planning from '../planningSequenceInspectionFixture'
import { PLANNING_INSPECTION_RELOAD_LABEL } from '../planningSequenceInspectionCopy'
import { UsageEvidenceRail } from './UsageEvidenceRail'

// The three run inspectors of the Usage & Evidence rail are siblings, so a parent update, an account or model observation settling, a
// conditional control appearing or the run changing must treat each by its own identity: the current run's open and selected
// inspection survives an ordinary update, and only a run replacement, a collapse or an unmount ends it. The real generated clients
// and transport run against a fake network, so every request the rail sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const RUN = local.RUN
const OTHER = local.guidOf('r', 2)
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const ALLOWANCE = `${BASE}/api/environment/codex-account-allowance`
const CATALOG = `${BASE}/api/environment/codex-model-catalog`
const localIndex = (run: string) => `${BASE}/api/runs/${run}/local-execution-sequence/inspection`
const planningIndex = (run: string) => `${BASE}/api/runs/${run}/planning-sequence/inspection`
const historyPrefix = (run: string) => `${BASE}/api/runs/${run}/agent-attempts?`

interface Network {
  requests: { method: string; url: string }[]
  routes: { matches: (url: string) => boolean; reply: () => Promise<Response> }[]
}

function installNetwork(): Network {
  const network: Network = { requests: [], routes: [] }
  vi.spyOn(window, 'fetch').mockImplementation((input, init) => {
    const url = String(input)
    network.requests.push({ method: init?.method ?? 'GET', url })
    const route = network.routes.find((candidate) => candidate.matches(url))
    return route === undefined ? Promise.resolve(json({ status: 404, errors: [{ code: 'unrouted' }], traceId: 'trace' }, 404)) : route.reply()
  })
  return network
}

// A later route for the same address replaces an earlier one, so a read can be answered at once first and held back afterwards.
function respond(network: Network, url: string | { prefix: string }, body: unknown) {
  const matches = typeof url === 'string' ? (candidate: string) => candidate === url : (candidate: string) => candidate.startsWith(url.prefix)
  network.routes.unshift({ matches, reply: () => Promise.resolve(json(body)) })
}

function hold(network: Network, url: string) {
  let release!: (body: unknown) => void
  const answer = new Promise<unknown>((resolve) => {
    release = resolve
  })
  // Every read after the release gets its own response of the released body; a Response can be read only once.
  network.routes.unshift({ matches: (candidate) => candidate === url, reply: () => answer.then((body) => json(body)) })
  return release
}

const UNKNOWN_ALLOWANCE = { status: 'Unknown', buckets: [] }
const OBSERVED_ALLOWANCE = {
  status: 'Observed',
  retrievedAtUtc: '2026-10-28T12:00:00.0000000Z',
  buckets: [{ limitId: 'codex', primary: { usedPercent: 42, windowDurationMins: 300 } }],
}
const UNKNOWN_CATALOG = { status: 'Unknown', models: [] }

function routesFor(network: Network, run: string) {
  respond(network, ALLOWANCE, UNKNOWN_ALLOWANCE)
  respond(network, CATALOG, UNKNOWN_CATALOG)
  respond(network, { prefix: historyPrefix(run) }, attemptPageOf(run, [2, 1]))
  respond(network, `${BASE}/api/runs/${run}/agent-attempts/${attemptIdFor(run, 2)}/evidence`, attemptEvidenceFor(run, 2))
  respond(network, localIndex(run), local.recordedIndexFor(run, [local.stepFor(0), local.stepFor(1)]))
  respond(network, `${localIndex(run)}/steps/${local.stepIdFor(1)}`, { ...local.verificationStepAnswerFor(1), runId: run })
  respond(
    network,
    planningIndex(run),
    planning.recordedIndexFor(run, [planning.FIVE_STEPS[0], planning.FIVE_STEPS[1], planning.reservedStepFor(2)], {
      sequence: planning.headerFor({ recordedState: 'PreparingStep', reasonCode: null, endedAtUtc: null }),
    }),
  )
  respond(network, `${planningIndex(run)}/steps/${planning.stepIdFor(2)}`, { ...planning.refusedStepAnswerFor(2, 'NoLinkedAttempt'), runId: run })
}

// Every read of the three inspected surfaces, in order; an ordinary update must add none to it.
const inspectionReads = (network: Network) =>
  network.requests.map((request) => request.url).filter((url) => /\/agent-attempts|\/local-execution-sequence\/|\/planning-sequence\//.test(url))

// Observes the console without suppressing it: every diagnostic still reaches the original, and only duplicate-key ones are inspected here.
function observeConsole() {
  const spy = vi.spyOn(console, 'error')
  const messages = () => spy.mock.calls.map((call) => call.map(String).join(' '))
  return { duplicateKeys: () => messages().filter((message) => /two children with the same key/i.test(message)) }
}

const HISTORY = 'Agent attempt history'
const LOCAL = 'Inspect local execution'
const PLANNING = 'Inspect recorded planning'
const regionOf = (name: string) => screen.getByRole('region', { name })
const toggleOf = (name: string) => within(regionOf(name)).getAllByRole('button')[0]

async function openAll(network: Network) {
  fireEvent.click(toggleOf(HISTORY))
  fireEvent.click(await within(regionOf(HISTORY)).findByRole('button', { name: 'Inspect attempt 2' }))
  await within(regionOf(HISTORY)).findByLabelText('Selected Agent attempt evidence')

  fireEvent.click(toggleOf(LOCAL))
  await within(regionOf(LOCAL)).findByText(/Recorded state/)
  fireEvent.click(within(regionOf(LOCAL)).getByRole('button', { name: 'Inspect step 2' }))
  await waitFor(() => expect(within(regionOf(LOCAL)).getByRole('button', { name: 'Inspect step 2' })).toHaveAttribute('aria-pressed', 'true'))

  fireEvent.click(toggleOf(PLANNING))
  await within(regionOf(PLANNING)).findByText(/Recorded state/)
  fireEvent.click(within(regionOf(PLANNING)).getByRole('button', { name: 'Inspect planning step 3' }))
  await within(regionOf(PLANNING)).findByText(/No attempt link is recorded for this step/)
  await waitFor(() => expect(screen.queryByText('Codex model catalog: loading…')).toBeNull())
  await waitFor(() => expect(screen.queryByText('Codex account usage: loading…')).toBeNull())
  // Selecting a step and opening an attempt are the reads of interest; none is pending when the baseline is taken.
  await act(async () => undefined)
  return inspectionReads(network)
}

// Exactly one instance of each inspector, each still open with its selection, and nothing read or sent beyond the baseline.
function expectInspectionsPreserved(network: Network, baseline: string[]) {
  for (const name of [HISTORY, LOCAL, PLANNING]) {
    expect(screen.getAllByRole('region', { name })).toHaveLength(1)
    expect(toggleOf(name)).toHaveAttribute('aria-expanded', 'true')
  }
  expect(within(regionOf(HISTORY)).getByRole('button', { name: 'Inspect attempt 2' })).toHaveAttribute('aria-pressed', 'true')
  expect(within(regionOf(HISTORY)).getAllByLabelText('Selected Agent attempt evidence')).toHaveLength(1)
  expect(within(regionOf(LOCAL)).getByRole('button', { name: 'Inspect step 2' })).toHaveAttribute('aria-pressed', 'true')
  expect(within(regionOf(PLANNING)).getByRole('button', { name: 'Inspect planning step 3' })).toHaveAttribute('aria-pressed', 'true')
  expect(within(regionOf(PLANNING)).getByText(/No attempt link is recorded for this step/)).toBeInTheDocument()
  expect(inspectionReads(network)).toEqual(baseline)
  expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
}

function expectColdAndClosed(network: Network, baseline: string[]) {
  for (const name of [HISTORY, LOCAL, PLANNING]) {
    expect(screen.getAllByRole('region', { name })).toHaveLength(1)
    expect(toggleOf(name)).toHaveAttribute('aria-expanded', 'false')
  }
  expect(screen.queryByLabelText('Selected Agent attempt evidence')).toBeNull()
  expect(screen.queryByText(/Recorded state/)).toBeNull()
  expect(screen.queryByRole('button', { name: /^Inspect (planning )?step / })).toBeNull()
  expect(screen.queryByRole('button', { name: /^Inspect attempt / })).toBeNull()
  expect(inspectionReads(network)).toEqual(baseline)
}

const stop = (percent: number) => new CodexAccountUsageStopResponse({ state: 'Configured', percent })
const warning = (percent: number) => new CodexAccountUsageWarningSettingResponse({ state: 'Configured', percent })

describe('the Usage & Evidence rail keeps its three run inspectors across ordinary updates', () => {
  let network: Network
  let console_: ReturnType<typeof observeConsole>

  beforeEach(() => {
    vi.restoreAllMocks()
    console_ = observeConsole()
    network = installNetwork()
    routesFor(network, RUN)
    routesFor(network, OTHER)
  })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  it('keeps every open and selected inspection while the account observation is pending and when it resolves', async () => {
    render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)

    const release = hold(network, ALLOWANCE)
    fireEvent.click(screen.getByRole('button', { name: 'Refresh Codex account usage' }))
    expect(await screen.findByText('Codex account usage: loading…')).toBeInTheDocument()
    expectInspectionsPreserved(network, baseline)

    await act(async () => release(OBSERVED_ALLOWANCE))
    expect(await screen.findByText(/Primary: 42%/)).toBeInTheDocument()
    expectInspectionsPreserved(network, baseline)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('keeps every open and selected inspection while the model observation is pending and when it resolves', async () => {
    render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)

    const release = hold(network, CATALOG)
    fireEvent.click(screen.getByRole('button', { name: 'Refresh Codex model catalog' }))
    expect(await screen.findByText('Codex model catalog: loading…')).toBeInTheDocument()
    expectInspectionsPreserved(network, baseline)

    await act(async () => release(UNKNOWN_CATALOG))
    await waitFor(() => expect(screen.queryByText('Codex model catalog: loading…')).toBeNull())
    expectInspectionsPreserved(network, baseline)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('keeps every open and selected inspection through parent updates of the same run, with the account-usage controls appearing and disappearing', async () => {
    const view = render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)

    view.rerender(<UsageEvidenceRail runId={RUN} onAccountUsageStopSaved={async () => true} />)
    expectInspectionsPreserved(network, baseline)

    view.rerender(<UsageEvidenceRail runId={RUN} codexAccountUsageStop={stop(80)} codexAccountUsageWarning={warning(70)} />)
    expectInspectionsPreserved(network, baseline)

    view.rerender(<UsageEvidenceRail runId={RUN} codexAccountUsageStop={stop(95)} codexAccountUsageWarning={null} accountUsageStopEditable />)
    expectInspectionsPreserved(network, baseline)

    view.rerender(<UsageEvidenceRail runId={RUN} codexAccountUsageStop={null} codexAccountUsageWarning={warning(70)} />)
    expectInspectionsPreserved(network, baseline)

    view.rerender(<UsageEvidenceRail runId={RUN} />)
    expectInspectionsPreserved(network, baseline)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('ends every inspection on a run replacement and an A to B to A return, and a late answer of the earlier run changes nothing', async () => {
    const view = render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)
    const releaseLocal = hold(network, localIndex(RUN))
    const releasePlanning = hold(network, planningIndex(RUN))
    fireEvent.click(within(regionOf(LOCAL)).getByRole('button', { name: 'Reload recorded sequence' }))
    fireEvent.click(within(regionOf(PLANNING)).getByRole('button', { name: PLANNING_INSPECTION_RELOAD_LABEL }))
    const withPendingReloads = inspectionReads(network)
    expect(withPendingReloads).toEqual([...baseline, localIndex(RUN), planningIndex(RUN)])

    view.rerender(<UsageEvidenceRail runId={OTHER} />)
    expectColdAndClosed(network, withPendingReloads)
    await act(async () => {
      releaseLocal(local.recordedIndexFor(RUN, [local.stepFor(0), local.stepFor(1)]))
      releasePlanning(planning.recordedIndexFor(RUN))
    })
    expectColdAndClosed(network, withPendingReloads)
    expect(inspectionReads(network).filter((url) => url.includes(OTHER))).toEqual([])

    view.rerender(<UsageEvidenceRail runId={RUN} />)
    expectColdAndClosed(network, withPendingReloads)
    fireEvent.click(toggleOf(LOCAL))
    await within(regionOf(LOCAL)).findByText(/Recorded state/)
    expect(inspectionReads(network)).toEqual([...withPendingReloads, localIndex(RUN)])
    expect(within(regionOf(LOCAL)).getByRole('button', { name: 'Inspect step 2' })).toHaveAttribute('aria-pressed', 'false')
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('ends every inspection when the rail is collapsed and opens each cold when it is expanded again', async () => {
    render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)

    fireEvent.click(screen.getByRole('button', { name: 'Collapse usage and evidence rail' }))
    for (const name of [HISTORY, LOCAL, PLANNING]) {
      expect(screen.queryByRole('region', { name })).toBeNull()
    }
    fireEvent.click(screen.getByRole('button', { name: 'Expand usage and evidence rail' }))

    expectColdAndClosed(network, baseline)
    expect(console_.duplicateKeys()).toEqual([])
  })

  it('starts nothing for an unmounted rail and lets a pending inspection read change nothing', async () => {
    const view = render(<UsageEvidenceRail runId={RUN} />)
    const baseline = await openAll(network)
    const release = hold(network, localIndex(RUN))
    fireEvent.click(within(regionOf(LOCAL)).getByRole('button', { name: 'Reload recorded sequence' }))
    const reads = inspectionReads(network)
    expect(reads).toEqual([...baseline, localIndex(RUN)])

    view.unmount()
    await act(async () => release(local.recordedIndexFor(RUN)))

    expect(inspectionReads(network)).toEqual(reads)
    expect(console_.duplicateKeys()).toEqual([])
  })
})
