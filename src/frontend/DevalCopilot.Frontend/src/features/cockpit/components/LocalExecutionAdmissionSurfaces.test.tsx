// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { recordedHistoryFor, stoppedWhileWaitingHistoryFor } from '../localExecutionAdmissionHistoryFixture'
import { RUN, guidOf, headerFor, recordedIndexFor, stepFor, stepIdFor, verificationStepAnswerFor } from '../localExecutionInspectionFixture'
import type { RunHistoryEntry } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'
import { UsageEvidenceRail } from './UsageEvidenceRail'

// The real generated clients and transport run against a fake network, so every request each surface sends is counted exactly. The
// current run's Usage & Evidence rail and a selected historical run mount the same inspector, so both must show the same ledger.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const OTHER = guidOf('r', 2)
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const historyEntry = (executionNumber: number, runId: string): RunHistoryEntry => ({
  projectId: 'project-1',
  runId,
  executionNumber,
  objective: `Objective ${executionNumber}`,
  lifecycle: 'Created',
  stage: 'Intake',
  executionMode: 'ManualAgent',
  createdAtUtc: new Date(Date.UTC(2026, 9, 9, 10, executionNumber, 0)),
  lastAdvancedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, executionNumber, 0)),
  receiptSource: null,
})
const inspection = (run: string) => `${BASE}/api/runs/${run}/local-execution-sequence/inspection`

const queuedIndex = (run: string) =>
  recordedIndexFor(run, [stepFor(0), stepFor(1)], {
    sequence: headerFor({ admissionMode: 'WaitForAdmission', recordedState: 'WaitingForAdmission', reasonCode: null, endedAtUtc: null }),
  })

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
    return route === undefined ? Promise.resolve(json({ errors: [{ code: 'unrouted' }] }, 404)) : route()
  })
  return network
}

const respond = (network: Network, url: string, body: unknown, status = 200) => network.routes.set(url, () => Promise.resolve(json(body, status)))
const routesFor = (network: Network, run: string, history: unknown) => {
  respond(network, inspection(run), queuedIndex(run))
  respond(network, `${inspection(run)}/admissions`, history)
  respond(network, `${inspection(run)}/steps/${stepIdFor(1)}`, verificationStepAnswerFor(1))
}
const inspectionRequests = (network: Network) => network.requests.filter((request) => request.url.includes('/local-execution-sequence/'))

describe('the queue admission ledger on both inspection surfaces', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows the ledger of the current run in Usage & Evidence, reads only when asked and opens a linked step explicitly', async () => {
    const network = installNetwork()
    routesFor(network, RUN, recordedHistoryFor(RUN))
    render(<UsageEvidenceRail runId={RUN} />)

    expect(inspectionRequests(network)).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    expect(inspectionRequests(network)).toEqual([{ method: 'GET', url: inspection(RUN) }])

    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await screen.findByText(/Queued sequence/)
    expect(inspectionRequests(network).map((request) => request.url)).toEqual([inspection(RUN), `${inspection(RUN)}/admissions`])

    fireEvent.click(screen.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' }))
    await waitFor(() => expect(inspectionRequests(network)).toHaveLength(3))
    expect(inspectionRequests(network)[2]).toEqual({ method: 'GET', url: `${inspection(RUN)}/steps/${stepIdFor(1)}` })
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
  })

  it('ends the current run ledger when the run changes and never shows the earlier run ledger for the next', async () => {
    const network = installNetwork()
    routesFor(network, RUN, recordedHistoryFor(RUN))
    routesFor(network, OTHER, stoppedWhileWaitingHistoryFor(OTHER))
    const view = render(<UsageEvidenceRail runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await screen.findByText(/Queued sequence/)

    view.rerender(<UsageEvidenceRail runId={OTHER} />)

    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(screen.queryByRole('button', { name: /Open linked step/ })).toBeNull()
    expect(screen.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
    expect(inspectionRequests(network).filter((request) => request.url.includes(OTHER))).toEqual([])
  })

  it('shows the ledger of a selected historical run, a wait retired without a step included, and reads nothing for the unselected one', async () => {
    const network = installNetwork()
    routesFor(network, RUN, stoppedWhileWaitingHistoryFor(RUN))
    routesFor(network, OTHER, recordedHistoryFor(OTHER))
    const view = render(<ProjectRunHistoryDetail entry={historyEntry(3, RUN)} onClose={() => undefined} />)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await screen.findByText(/Queued sequence/)

    expect(screen.getByText(/retired before any step was created/)).toBeInTheDocument()
    expect(inspectionRequests(network).map((request) => request.url)).toEqual([inspection(RUN), `${inspection(RUN)}/admissions`])

    // Selecting another history entry is another run: the first one's ledger ends and nothing is read for the second until asked.
    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(2, OTHER)} onClose={() => undefined} />)

    expect(screen.queryByText(/retired before any step was created/)).toBeNull()
    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(inspectionRequests(network).filter((request) => request.url.includes(OTHER))).toEqual([])
    expect(inspectionRequests(network)).toHaveLength(2)
  })

  it('opens the linked step of a historical run through the existing selected-step read without changing the selected run', async () => {
    const network = installNetwork()
    routesFor(network, RUN, recordedHistoryFor(RUN))
    const onClose = vi.fn()
    render(<ProjectRunHistoryDetail entry={historyEntry(3, RUN)} onClose={onClose} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await screen.findByText(/Queued sequence/)

    fireEvent.click(screen.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' }))

    expect(await screen.findByText(/linked verification run/i)).toBeInTheDocument()
    expect(inspectionRequests(network).map((request) => request.url)).toEqual([
      inspection(RUN),
      `${inspection(RUN)}/admissions`,
      `${inspection(RUN)}/steps/${stepIdFor(1)}`,
    ])
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('region', { name: 'Selected run' })).toBeInTheDocument()
  })
})
