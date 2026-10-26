// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  FIVE_STEPS,
  RUN,
  guidOf,
  recordedIndexFor,
  refusedStepAnswerFor,
  reservedStepFor,
  stepAnswerFor,
  stepIdFor,
  headerFor,
} from '../planningSequenceInspectionFixture'
import type { RunHistoryEntry } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'
import { UsageEvidenceRail } from './UsageEvidenceRail'

// The current run's Usage & Evidence rail and a selected historical run mount the same recorded planning inspector (ADR-0068). The real
// generated clients and transport run against a fake network, so every request each surface sends is counted exactly: opening reads only the
// index, choosing a step reads only that association, and nothing here sends a command or reads another surface's evidence.
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
const inspection = (run: string) => `${BASE}/api/runs/${run}/planning-sequence/inspection`
const stepUrl = (run: string, ordinal: number) => `${inspection(run)}/steps/${stepIdFor(ordinal)}`

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
// The rail also reads unrelated environment facts on mount; only the planning inspection requests are of interest here.
const urls = (network: Network) => network.requests.map((request) => request.url).filter((url) => url.includes('/planning-sequence/'))

function routesFor(network: Network, run: string) {
  respond(
    network,
    inspection(run),
    recordedIndexFor(run, [FIVE_STEPS[0], FIVE_STEPS[1], reservedStepFor(2)], {
      sequence: headerFor({ recordedState: 'PreparingStep', reasonCode: null, endedAtUtc: null }),
    }),
  )
  respond(network, stepUrl(run, 1), { ...stepAnswerFor(1), runId: run })
  respond(network, stepUrl(run, 2), { ...refusedStepAnswerFor(2, 'NoLinkedAttempt'), runId: run })
}

const SURFACES: readonly [string, (run: string) => ReactElement][] = [
  ['the current run in Usage & Evidence', (run) => <UsageEvidenceRail runId={run} />],
  ['a selected historical run', (run) => <ProjectRunHistoryDetail entry={historyEntry(run)} onClose={() => undefined} />],
]

const OPEN = { name: 'Inspect recorded planning' }

describe('the recorded planning inspection surfaces', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  it.each(SURFACES)('%s offers one collapsed region that reads nothing until opened', (_name, surface) => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))

    expect(screen.getAllByRole('button', OPEN)).toHaveLength(1)
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urls(network)).toEqual([])
  })

  it.each(SURFACES)('%s reads the index on open, then only the chosen step, and sends no command', async (_name, surface) => {
    const network = installNetwork()
    routesFor(network, RUN)
    render(surface(RUN))

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Recorded state/)
    expect(urls(network)).toEqual([inspection(RUN)])

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 3' }))
    expect(await screen.findByText(/No attempt link is recorded for this step/)).toBeInTheDocument()
    expect(urls(network)).toEqual([inspection(RUN), stepUrl(RUN, 2)])

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByLabelText('Selected Agent attempt evidence')
    expect(urls(network)).toEqual([inspection(RUN), stepUrl(RUN, 2), stepUrl(RUN, 1)])
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
  })

  it.each(SURFACES)('%s starts closed for another run and never shows the earlier run\'s inspection', async (_name, surface) => {
    const network = installNetwork()
    routesFor(network, RUN)
    routesFor(network, OTHER)
    const view = render(surface(RUN))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Recorded state/)

    view.rerender(surface(OTHER))

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(/Recorded state/)).toBeNull()
    expect(urls(network)).toEqual([inspection(RUN)])
  })

  it('the historical detail keeps the existing local execution region and adds its own, without changing the selection', () => {
    const network = installNetwork()
    const onClose = vi.fn()
    render(<ProjectRunHistoryDetail entry={historyEntry(RUN)} onClose={onClose} />)

    expect(screen.getByRole('button', { name: 'Inspect local execution' })).toBeInTheDocument()
    expect(screen.getByRole('button', OPEN)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(onClose).not.toHaveBeenCalled()
    expect(network.requests.filter((request) => request.method !== 'GET')).toEqual([])
  })
})
