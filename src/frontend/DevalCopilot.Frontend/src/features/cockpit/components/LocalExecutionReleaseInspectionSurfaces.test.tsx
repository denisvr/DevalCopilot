// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactElement } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  RUN,
  agentStepAnswerFor,
  guidOf,
  recordedIndexFor,
  refusedStepAnswerFor,
  stepFor,
  stepIdFor,
  verificationStepAnswerFor,
} from '../localExecutionInspectionFixture'
import { RELEASE_INSPECTION_NOT_RECORDED } from '../localExecutionReleaseInspectionCopy'
import {
  notRecordedReleaseInspectionFor,
  recordedReleaseInspectionFor,
  recordedVerificationReleaseInspectionFor,
} from '../localExecutionReleaseInspectionFixture'
import type { RunHistoryEntry } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'
import { UsageEvidenceRail } from './UsageEvidenceRail'

// The current run's Usage & Evidence rail and a selected historical run mount the same step inspector (ADR-0061), so both must offer the
// same collapsed recorded quarantine-release region (ADR-0067). The real generated clients and transport run against a fake network, so
// every request each surface sends is counted exactly: selecting a step reads no receipt, opening the region reads only the receipt, and
// nothing here ever sends a command, a release candidate read or an artifact read.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const OTHER = guidOf('r', 2)
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const historyEntry = (runId: string): RunHistoryEntry => ({
  projectId: 'project-1',
  runId,
  executionNumber: 3,
  objective: 'Objective 3',
  lifecycle: 'Created',
  stage: 'Intake',
  executionMode: 'ManualAgent',
  createdAtUtc: new Date(Date.UTC(2026, 9, 9, 10, 3, 0)),
  lastAdvancedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, 3, 0)),
  receiptSource: null,
})
const inspection = (run: string) => `${BASE}/api/runs/${run}/local-execution-sequence/inspection`
const stepUrl = (run: string, ordinal: number) => `${inspection(run)}/steps/${stepIdFor(ordinal)}`
const receiptUrl = (run: string, ordinal: number) => `${stepUrl(run, ordinal)}/ownership-release`

interface Network {
  requests: { method: string; url: string }[]
  routes: Map<string, () => Promise<Response>>
}

function installNetwork(): Network {
  const network: Network = { requests: [], routes: new Map() }
  vi.spyOn(window, 'fetch').mockImplementation((input, init) => {
    const url = String(input)
    network.requests.push({ method: init?.method ?? 'GET', url })
    const route = network.routes.get(url)
    return route === undefined ? Promise.resolve(json({ status: 404, errors: [{ code: 'unrouted' }], traceId: 'trace' }, 404)) : route()
  })
  return network
}

const respond = (network: Network, url: string, body: unknown, status = 200) => network.routes.set(url, () => Promise.resolve(json(body, status)))
// The rail also reads unrelated environment facts on mount; only the local execution requests are of interest here.
const urls = (network: Network) => network.requests.map((request) => request.url).filter((url) => url.includes('/local-execution-sequence/'))

function routesFor(network: Network, run: string) {
  respond(network, inspection(run), recordedIndexFor(run, [stepFor(0), stepFor(1), stepFor(2), stepFor(3, { recordedState: 'Reserved', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null })]))
  respond(network, stepUrl(run, 0), { ...agentStepAnswerFor(0), runId: run })
  respond(network, stepUrl(run, 1), { ...verificationStepAnswerFor(1), runId: run })
  respond(network, stepUrl(run, 3), { ...refusedStepAnswerFor(3, 'NoOperation'), runId: run })
  respond(network, receiptUrl(run, 0), { ...recordedReleaseInspectionFor(), runId: run })
  respond(network, receiptUrl(run, 1), { ...recordedVerificationReleaseInspectionFor(), runId: run })
}

const SURFACES: readonly [string, (run: string) => ReactElement][] = [
  ['the current run in Usage & Evidence', (run) => <UsageEvidenceRail runId={run} />],
  ['a selected historical run', (run) => <ProjectRunHistoryDetail entry={historyEntry(run)} onClose={() => undefined} />],
]

const OPEN = { name: 'Inspect recorded quarantine release' }

async function openStep(ordinal: number, label: RegExp | string = /Recorded state/) {
  fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
  await screen.findByText(/Recorded state/)
  fireEvent.click(screen.getByRole('button', { name: `Inspect step ${ordinal + 1}` }))
  await screen.findByText(label)
  await screen.findByRole('button', OPEN)
}

describe.each(SURFACES)('the recorded quarantine release on %s', (_name, surface) => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('offers no region until a proved linked step is selected, and selecting the step reads no receipt', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    expect(screen.queryByRole('button', OPEN)).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urls(network)).toEqual([inspection(RUN), stepUrl(RUN, 1)])
  })

  it('reads only the receipt when the region is opened and shows the Human decision of an Agent step', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))
    await openStep(0, /Recorded operation link/)
    const before = urls(network).length

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    expect(urls(network).slice(before)).toEqual([receiptUrl(RUN, 0)])
    expect(screen.getByText(/Agent attempt · step 1 · Implementation \(Claude Code\) · pass 0/)).toBeInTheDocument()
    expect(screen.getByText(/recorded as Human run event #42/)).toBeInTheDocument()
  })

  it('shows a verification receipt and a not-recorded answer for the selected step only', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    respond(network, receiptUrl(RUN, 0), notRecordedReleaseInspectionFor(0))
    render(surface(RUN))
    await openStep(1, /Recorded status/)

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')
    expect(screen.getByText(/Verification run · step 2/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 1' }))
    await screen.findByText(/Recorded operation link/)
    expect(screen.queryByText('Human release')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(RELEASE_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(urls(network).filter((url) => url.endsWith('/ownership-release'))).toEqual([receiptUrl(RUN, 1), receiptUrl(RUN, 0)])
  })

  it('offers no region for a step that names no operation', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 4' }))

    await waitFor(() => expect(urls(network)).toContain(stepUrl(RUN, 3)))
    await screen.findByText(/Nothing is assumed about whether a reserved operation started/)
    expect(screen.queryByRole('button', OPEN)).toBeNull()
    expect(urls(network).some((url) => url.endsWith('/ownership-release'))).toBe(false)
  })

  it('ends the receipt with its step visit: another step, a closed step or a reselected one shows nothing of it and a late answer is ignored', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    let release!: () => void
    network.routes.set(receiptUrl(RUN, 0), () => new Promise<Response>((resolve) => (release = () => resolve(json(recordedReleaseInspectionFor())))))
    render(surface(RUN))
    await openStep(0, /Recorded operation link/)
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Reading the recorded quarantine release/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)
    release()
    await waitFor(() => expect(screen.queryByText(/Reading the recorded quarantine release/)).toBeNull())
    expect(screen.queryByText('Human release')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 1' }))
    await screen.findByText(/Recorded operation link/)
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Human release')).toBeNull()
    expect(urls(network).filter((url) => url === receiptUrl(RUN, 0))).toHaveLength(1)
  })

  it('reads nothing on a reload of the sequence and ends the receipt view with the index visit', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))
    await openStep(0, /Recorded operation link/)
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded sequence' }))
    await screen.findByText(/Recorded state/)

    expect(screen.queryByText('Human release')).toBeNull()
    expect(screen.queryByRole('button', OPEN)).toBeNull()
    expect(urls(network).filter((url) => url.endsWith('/ownership-release'))).toHaveLength(1)
  })

  it('ends with the run: another run shows no receipt and reads none until asked', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    routesFor(network, OTHER)
    const view = render(surface(RUN))
    await openStep(0, /Recorded operation link/)
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    view.rerender(surface(OTHER))

    expect(screen.queryByText('Human release')).toBeNull()
    expect(screen.queryByRole('button', OPEN)).toBeNull()
    expect(urls(network).filter((url) => url.includes(OTHER))).toEqual([])
  })

  it('sends only GETs and no release candidate read, release command or artifact read, and mounts no release control', async () => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))
    await openStep(1, /Recorded status/)
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded quarantine release' }))
    await screen.findByText('Human release')

    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    expect(urls(network).some((url) => /\/local-execution-sequence\/steps\//.test(url))).toBe(false)
    expect(urls(network).some((url) => url.includes('/artifact-window/') || url.includes('/artifacts/'))).toBe(false)
    expect(screen.queryByRole('button', { name: /Confirm and release quarantine/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Release terminal operation quarantine|Read the release evidence again/ })).toBeNull()
  })
})
