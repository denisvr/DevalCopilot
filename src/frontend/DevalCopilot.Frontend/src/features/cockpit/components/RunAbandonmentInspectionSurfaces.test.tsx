// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ABANDONMENT_EVENT,
  ABANDONMENT_OTHER_RUN,
  ABANDONMENT_PROJECT,
  ABANDONMENT_REASON,
  ABANDONMENT_RUN,
  notRecordedRunAbandonmentInspectionFor,
  recordedRunAbandonmentInspectionFor,
} from '../runAbandonmentInspectionFixture'
import { RUN_ABANDONMENT_INSPECTION_FAILURES, RUN_ABANDONMENT_INSPECTION_NOT_RECORDED } from '../runAbandonmentInspectionCopy'
import type { RunHistoryEntry } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'

// A selected historical run mounts the recorded abandonment region in its detail (ADR-0071), whatever its listed lifecycle and with or
// without a located delivery receipt source. The real generated clients and transport run against a fake network, so every request is
// counted exactly: the region reads only its own recorded abandonment on an explicit open or reload, never the live abandonment status,
// eligibility or command, and sends nothing but GETs.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const inspectionUrl = (run: string) => `${BASE}/api/runs/${run}/abandonment-inspection`
const OPEN = { name: 'Inspect recorded abandonment' }
const RECORDED = 'Recorded reason'

const historyEntry = (runId: string, executionNumber: number, withSource: boolean, lifecycle: RunHistoryEntry['lifecycle'] = 'Abandoned'): RunHistoryEntry => ({
  projectId: ABANDONMENT_PROJECT,
  runId,
  executionNumber,
  objective: `Objective ${executionNumber}`,
  lifecycle,
  stage: 'Execute',
  executionMode: 'ManualAgent',
  createdAtUtc: new Date(Date.UTC(2026, 9, 9, 10, executionNumber, 0)),
  lastAdvancedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, executionNumber, 0)),
  receiptSource: withSource
    ? { runId, operationId: ABANDONMENT_EVENT, commitSha: '3'.repeat(40), checkpointId: ABANDONMENT_EVENT, checkpointNumber: 2 }
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
])('the recorded abandonment of a selected historical run %s', (_name, withSource) => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it.each(['Created', 'Running', 'Completed', 'Abandoned'] as const)(
    'mounts the collapsed region once for a listed %s run and reads nothing for it until it is opened',
    async (lifecycle) => {
      const network = installNetwork()
      respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
      mount(historyEntry(ABANDONMENT_RUN, 3, withSource, lifecycle))

      expect(screen.getAllByRole('region', { name: 'Inspect recorded abandonment' })).toHaveLength(1)
      expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
      await new Promise((resolve) => setTimeout(resolve, 0))
      expect(urlsOf(network, '/abandonment-inspection')).toEqual([])

      fireEvent.click(screen.getByRole('button', OPEN))
      await screen.findByText(RECORDED)

      expect(urlsOf(network, '/abandonment-inspection')).toEqual([inspectionUrl(ABANDONMENT_RUN)])
      expect(screen.getByText(RECORDED).nextSibling).toHaveTextContent(ABANDONMENT_REASON)
    },
  )

  it('never mounts the live abandonment panel and never reads the live status, eligibility or command', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    mount(historyEntry(ABANDONMENT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded abandonment' }))
    await screen.findByText(RECORDED)

    expect(screen.queryByRole('region', { name: 'Abandon run' })).toBeNull()
    expect(screen.queryByRole('button', { name: /^abandon/i })).toBeNull()
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    expect(network.requests.map((request) => request.url).filter((url) => /abandonment(?!-inspection)|\/abandon\b/.test(url))).toEqual([])
    expect(urlsOf(network, '/abandonment-inspection')).toHaveLength(2)
  })

  it('reads and shows a NotRecorded newer run beside an older recorded one, each by its own run, discarding the answer on selection change', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    respond(network, inspectionUrl(ABANDONMENT_OTHER_RUN), notRecordedRunAbandonmentInspectionFor({ runId: ABANDONMENT_OTHER_RUN }))
    const view = mount(historyEntry(ABANDONMENT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(ABANDONMENT_OTHER_RUN, 4, false, 'Running')} onClose={() => undefined} />)

    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urlsOf(network, '/abandonment-inspection')).toEqual([inspectionUrl(ABANDONMENT_RUN)])
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(urlsOf(network, '/abandonment-inspection')).toEqual([inspectionUrl(ABANDONMENT_RUN), inspectionUrl(ABANDONMENT_OTHER_RUN)])

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(ABANDONMENT_RUN, 3, withSource)} onClose={() => undefined} />)
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.queryByText(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(urlsOf(network, '/abandonment-inspection')).toHaveLength(2)
  })

  it('ignores the late answer of a replaced selection', async () => {
    const network = installNetwork()
    let release!: () => void
    network.routes.set(
      inspectionUrl(ABANDONMENT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(recordedRunAbandonmentInspectionFor())))),
    )
    const view = mount(historyEntry(ABANDONMENT_RUN, 3, withSource))
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Reading what the host recorded for this run/)

    view.rerender(<ProjectRunHistoryDetail entry={historyEntry(ABANDONMENT_OTHER_RUN, 4, false)} onClose={() => undefined} />)
    release()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.queryByText(/Reading what the host recorded for this run/)).toBeNull()
  })

  it('shows the fixed failure copy for a refused answer without disturbing the rest of the detail', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), { ...recordedRunAbandonmentInspectionFor(), extra: 1 })
    mount(historyEntry(ABANDONMENT_RUN, 3, withSource))

    fireEvent.click(screen.getByRole('button', OPEN))

    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Selected run' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close selected run' })).toBeInTheDocument()
  })
})
