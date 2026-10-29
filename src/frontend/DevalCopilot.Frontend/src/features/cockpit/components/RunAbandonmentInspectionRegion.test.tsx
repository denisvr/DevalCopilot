// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ABANDONMENT_AT,
  ABANDONMENT_EVENT,
  ABANDONMENT_OTHER_PROJECT,
  ABANDONMENT_OTHER_RUN,
  ABANDONMENT_PROJECT,
  ABANDONMENT_REASON,
  ABANDONMENT_RUN,
  notRecordedRunAbandonmentInspectionFor,
  recordedRunAbandonmentInspectionFor,
  unavailableRunAbandonmentInspectionFor,
} from '../runAbandonmentInspectionFixture'
import {
  RUN_ABANDONMENT_INSPECTION_FAILURES,
  RUN_ABANDONMENT_INSPECTION_NOTE,
  RUN_ABANDONMENT_INSPECTION_NOT_RECORDED,
  RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE,
  RUN_ABANDONMENT_INSPECTION_UNAVAILABLE,
} from '../runAbandonmentInspectionCopy'
import { RunAbandonmentInspectionRegion } from './RunAbandonmentInspectionRegion'

// The real generated client and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const inspectionUrl = (run: string) => `${BASE}/api/runs/${run}/abandonment-inspection`

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
const OPEN = { name: 'Inspect recorded abandonment' }
const HIDE = { name: 'Hide recorded abandonment' }
const RELOAD = { name: 'Reload recorded abandonment' }
const RECORDED = 'Recorded reason'

const renderRegion = (run = ABANDONMENT_RUN, project = ABANDONMENT_PROJECT) =>
  render(<RunAbandonmentInspectionRegion runId={run} projectId={project} />)

describe('RunAbandonmentInspectionRegion', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the recorded abandonment once', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    renderRegion()

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getAllByRole('button')).toHaveLength(1)
    expect(network.requests).toEqual([])

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    expect(network.requests).toEqual([{ method: 'GET', url: inspectionUrl(ABANDONMENT_RUN) }])
    expect(screen.getByRole('button', HIDE)).toHaveAttribute('aria-expanded', 'true')
    for (const line of RUN_ABANDONMENT_INSPECTION_NOTE) {
      expect(screen.getByText(line)).toBeInTheDocument()
    }
  })

  it('offers only the region toggle and the explicit reload: no abandon, cancel, repair, release, retry, intake or export action', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Hide recorded abandonment', 'Reload recorded abandonment'])
    expect(screen.queryByRole('button', { name: /abandon run|cancel|repair|release|retry|intake|record another|export|publish|resume/i })).toBeNull()
    expect(screen.queryByRole('link')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
  })

  it('shows the recorded reason, the full UTC instant and the supporting event identity and sequence, and what the read does not prove', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    expect(screen.getByText(RECORDED).nextSibling).toHaveTextContent(ABANDONMENT_REASON)
    expect(screen.getByText('Recorded abandoned at (UTC)').nextSibling).toHaveTextContent(ABANDONMENT_AT)
    expect(screen.getByText('Supporting event').nextSibling).toHaveTextContent(`${ABANDONMENT_EVENT} · sequence 42`)
    expect(screen.getByText('Recorded project').nextSibling).toHaveTextContent(ABANDONMENT_PROJECT)
    expect(screen.getByText(RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE)).toBeInTheDocument()
    expect(screen.getByText(RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE)).toHaveTextContent(/ended the objective of this run without successful completion/)
    expect(screen.getByText(/proves no cancellation, workspace repair, ownership release, current permission to record another objective, or publication/)).toBeInTheDocument()
  })

  it('shows the exact Int64 sequence text without rounding', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor({}, { eventSequence: '9223372036854775807' }))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    expect(screen.getByText('Supporting event').nextSibling).toHaveTextContent('sequence 9223372036854775807')
  })

  it('renders a multiline reason literally, preserving line feeds and blank lines and interpreting nothing', async () => {
    const reason = 'First line\n\nThird line <b>bold</b> & more\n  indented'
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor({}, { reason }))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    const value = screen.getByText(RECORDED).nextSibling as HTMLElement
    expect(value.textContent).toBe(reason)
    expect(value.style.whiteSpace).toBe('pre-wrap')
    expect(value.children).toHaveLength(0)
    expect(view.container.querySelector('b, script, img')).toBeNull()
  })

  it('renders every host value as inert text', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor({}, { reason: '<img src=x onerror=alert(1)>' }))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    expect(view.container.querySelector('img')).toBeNull()
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeInTheDocument()
  })

  it('shows the fixed NotRecorded and Unavailable copy and no recorded facts', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), notRecordedRunAbandonmentInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED).toMatch(/says nothing about whether this run can be abandoned now/)
    expect(screen.queryByText(RECORDED)).toBeNull()

    respond(network, inspectionUrl(ABANDONMENT_RUN), unavailableRunAbandonmentInspectionFor())
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(new Set([RUN_ABANDONMENT_INSPECTION_NOT_RECORDED, RUN_ABANDONMENT_INSPECTION_UNAVAILABLE]).size).toBe(2)
  })

  it('shows fixed failure copy and no partial answer for a failed, missing or inconsistent answer', async () => {
    const network = installNetwork()
    network.routes.set(inspectionUrl(ABANDONMENT_RUN), () => Promise.reject(new Error('network down C:\\secret')))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_FAILURES.read)).toBeInTheDocument()
    expect(view.container.textContent).not.toContain('secret')

    respond(network, inspectionUrl(ABANDONMENT_RUN), { status: 404, errors: [{ code: 'run_abandonment_inspection.not_found' }], traceId: 't' }, 404)
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_FAILURES['not-found'])).toBeInTheDocument()

    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor({ runId: ABANDONMENT_OTHER_RUN }))
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.queryByText(ABANDONMENT_EVENT, { exact: false })).toBeNull()
  })

  it('refuses the whole answer for any incoherent member and shows none of it', async () => {
    const network = installNetwork()
    for (const body of [
      recordedRunAbandonmentInspectionFor({}, { eventSequence: '0' }),
      recordedRunAbandonmentInspectionFor({}, { reason: ' padded' }),
      recordedRunAbandonmentInspectionFor({}, { projectId: ABANDONMENT_OTHER_PROJECT }),
      recordedRunAbandonmentInspectionFor({}, { abandonedAtUtc: '2026-10-08T12:00:00Z' }),
      { ...recordedRunAbandonmentInspectionFor(), extra: 1 },
    ]) {
      respond(network, inspectionUrl(ABANDONMENT_RUN), body)
      const view = renderRegion()
      fireEvent.click(view.getByRole('button', OPEN))
      expect(await view.findByText(RUN_ABANDONMENT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
      expect(view.queryByText(RECORDED)).toBeNull()
      view.unmount()
    }
  })

  it('shows the pending line while the read is in flight and nothing else', async () => {
    const network = installNetwork()
    network.routes.set(inspectionUrl(ABANDONMENT_RUN), () => new Promise<Response>(() => undefined))
    renderRegion()

    fireEvent.click(screen.getByRole('button', OPEN))

    expect(await screen.findByText(/Reading what the host recorded for this run/)).toBeInTheDocument()
    expect(screen.queryByText(RECORDED)).toBeNull()
  })

  it('clears the old answer at once on reload, reads once more and shows only the new answer', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)
    let release!: () => void
    network.routes.set(
      inspectionUrl(ABANDONMENT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(notRecordedRunAbandonmentInspectionFor())))),
    )

    fireEvent.click(screen.getByRole('button', RELOAD))

    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(await screen.findByText(/Reading what the host recorded for this run/)).toBeInTheDocument()
    release()
    expect(await screen.findByText(RUN_ABANDONMENT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(network.requests.map((request) => request.url)).toEqual([inspectionUrl(ABANDONMENT_RUN), inspectionUrl(ABANDONMENT_RUN)])
  })

  it('discards the answer on closing, ignores a late answer and reads again on the next opening', async () => {
    const network = installNetwork()
    let release!: () => void
    network.routes.set(
      inspectionUrl(ABANDONMENT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(recordedRunAbandonmentInspectionFor())))),
    )
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Reading what the host recorded for this run/)

    fireEvent.click(screen.getByRole('button', HIDE))
    release()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)
    expect(network.requests).toHaveLength(2)
  })

  it('ends with the run or the project: another one (and a return to the first) shows no answer and reads nothing until asked', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(ABANDONMENT_RUN), recordedRunAbandonmentInspectionFor())
    respond(network, inspectionUrl(ABANDONMENT_OTHER_RUN), notRecordedRunAbandonmentInspectionFor({ runId: ABANDONMENT_OTHER_RUN }))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)

    view.rerender(<RunAbandonmentInspectionRegion runId={ABANDONMENT_OTHER_RUN} projectId={ABANDONMENT_PROJECT} />)
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')

    view.rerender(<RunAbandonmentInspectionRegion runId={ABANDONMENT_RUN} projectId={ABANDONMENT_PROJECT} />)
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(RECORDED)
    view.rerender(<RunAbandonmentInspectionRegion runId={ABANDONMENT_RUN} projectId={ABANDONMENT_OTHER_PROJECT} />)
    expect(screen.queryByText(RECORDED)).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toHaveLength(2)
  })
})
