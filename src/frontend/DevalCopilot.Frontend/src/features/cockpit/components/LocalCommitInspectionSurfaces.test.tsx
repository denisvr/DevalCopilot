// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LOCAL_COMMIT_INSPECTION_FAILURES, LOCAL_COMMIT_INSPECTION_NOT_RECORDED } from '../localCommitInspectionCopy'
import {
  LOCAL_COMMIT_CHECKPOINT,
  LOCAL_COMMIT_OPERATION,
  LOCAL_COMMIT_OTHER_RUN,
  LOCAL_COMMIT_RESULT,
  LOCAL_COMMIT_RUN,
  notRecordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionOf,
} from '../localCommitInspectionFixture'
import type { RunHistoryEntry } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'

// A selected historical run mounts the recorded local-commit region in its detail (ADR-0070), with and without a located delivery
// receipt source. The real generated clients and transport run against a fake network, so every request is counted exactly: the region
// reads only its own recorded operation on an explicit open or reload, never the live local-commit status or eligibility, and the receipt
// keeps its own independent read and placement.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const inspectionUrl = (run: string) => `${BASE}/api/runs/${run}/local-commit-inspection`
const OPEN = { name: 'Inspect recorded local commit' }

const historyEntry = (runId: string, executionNumber: number, withSource: boolean): RunHistoryEntry => ({
  projectId: 'project-1',
  runId,
  executionNumber,
  objective: `Objective ${executionNumber}`,
  lifecycle: 'Completed',
  stage: 'Completed',
  executionMode: 'ManualAgent',
  createdAtUtc: new Date(Date.UTC(2026, 9, 9, 10, executionNumber, 0)),
  lastAdvancedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, executionNumber, 0)),
  receiptSource: withSource
    ? {
        runId,
        operationId: LOCAL_COMMIT_OPERATION,
        commitSha: LOCAL_COMMIT_RESULT,
        checkpointId: LOCAL_COMMIT_CHECKPOINT,
        checkpointNumber: 2,
      }
    : null,
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
    return route === undefined ? Promise.resolve(json({ status: 404, errors: [{ code: 'unrouted' }], traceId: 'trace' }, 404)) : route()
  })
  return network
}

const respond = (network: Network, url: string, body: unknown, status = 200) => network.routes.set(url, () => Promise.resolve(json(body, status)))
const urlsOf = (network: Network, fragment: string) => network.requests.map((request) => request.url).filter((url) => url.includes(fragment))
const mount = (entry: RunHistoryEntry) => render(<ProjectRunHistoryDetail entry={entry} onClose={() => undefined} />)

describe.each([
  ['without a located receipt source', false],
  ['with a located receipt source', true],
])('the recorded local commit of a selected historical run %s', (_name, withSource) => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mounts the collapsed region and reads nothing for it until it is opened', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Completed'))
    mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(urlsOf(network, '/local-commit-inspection')).toEqual([])

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(urlsOf(network, '/local-commit-inspection')).toEqual([inspectionUrl(LOCAL_COMMIT_RUN)])
  })

  it('never mounts the live local-commit panel or reads the live status or eligibility', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Completed'))
    mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded local commit' }))
    await screen.findByText('Recorded status')

    expect(screen.queryByRole('region', { name: 'Local commit' })).toBeNull()
    expect(network.requests.map((request) => request.url).filter((url) => url.endsWith('/local-commit'))).toEqual([])
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    expect(urlsOf(network, '/local-commit-inspection')).toHaveLength(2)
  })

  it('keeps the receipt placement: the source line and receipt only when a source exists, and the region does not duplicate them', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Completed'))
    mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))

    expect(screen.queryByText('Recorded local commit source') !== null).toBe(withSource)
    expect(screen.queryByText(/No completed delivery source is available in this view/) !== null).toBe(!withSource)
    await waitFor(() => expect(urlsOf(network, '/local-delivery-receipt').length > 0).toBe(withSource))
    const receiptReads = urlsOf(network, '/local-delivery-receipt').length

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    // Opening the region adds no receipt read, and the region carries none of the receipt's own facts.
    expect(urlsOf(network, '/local-delivery-receipt')).toHaveLength(receiptReads)
    expect(screen.queryByText('Source execution report')).toBeNull()
    expect(screen.queryByText('CodeReviewer approval')).toBeNull()
  })

  it('reads and shows a NotRecorded run beside an older recorded one, each by its own run, discarding the answer on selection change', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Completed'))
    respond(network, inspectionUrl(LOCAL_COMMIT_OTHER_RUN), notRecordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_OTHER_RUN }))
    const view = mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(LOCAL_COMMIT_OTHER_RUN, 4, false)} onClose={() => undefined} />)

    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urlsOf(network, '/local-commit-inspection')).toEqual([inspectionUrl(LOCAL_COMMIT_RUN)])
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(urlsOf(network, '/local-commit-inspection')).toEqual([inspectionUrl(LOCAL_COMMIT_RUN), inspectionUrl(LOCAL_COMMIT_OTHER_RUN)])

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(LOCAL_COMMIT_RUN, 3, withSource)} onClose={() => undefined} />)
    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.queryByText(LOCAL_COMMIT_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urlsOf(network, '/local-commit-inspection')).toHaveLength(2)
  })

  it('ignores the late answer of a replaced selection', async () => {
    const network = installNetwork()
    let release!: () => void
    network.routes.set(
      inspectionUrl(LOCAL_COMMIT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(recordedLocalCommitInspectionOf('Completed'))))),
    )
    const view = mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Reading the recorded local commit of this run/)

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(LOCAL_COMMIT_OTHER_RUN, 4, false)} onClose={() => undefined} />)
    release()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.queryByText(/Reading the recorded local commit of this run/)).toBeNull()
  })

  it('shows the fixed failure copy for a refused answer without disturbing the rest of the detail', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), { ...recordedLocalCommitInspectionOf('Completed'), extra: 1 })
    mount(historyEntry(LOCAL_COMMIT_RUN, 3, withSource))

    fireEvent.click(screen.getByRole('button', OPEN))

    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Selected run' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close selected run' })).toBeInTheDocument()
  })
})
