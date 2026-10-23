import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ADMISSIONS_NOTE,
  ADMISSIONS_NOT_QUEUED,
  ADMISSIONS_NOT_RECORDED,
  ADMISSIONS_UNAVAILABLE,
  ADMISSION_NO_STEP_RETIRED,
  ADMISSION_NO_STEP_WAITING,
  ADMISSION_STEP_NOT_INDEXED,
  ADMISSION_STEP_OTHER_SEQUENCE,
} from '../localExecutionAdmissionHistoryCopy'
import {
  admissionFor,
  guidOf,
  notQueuedHistoryFor,
  notRecordedHistoryFor,
  recordedHistoryFor,
  reservedHistoryFor,
  retiredWithStepHistoryFor,
  stoppedWhileWaitingHistoryFor,
  unavailableHistoryFor,
  unresolvedHistoryFor,
} from '../localExecutionAdmissionHistoryFixture'
import {
  RUN,
  headerFor,
  recordedIndexFor,
  stepFor,
  stepIdFor,
  verificationStepAnswerFor,
} from '../localExecutionInspectionFixture'
import { LocalExecutionInspectionPanel } from './LocalExecutionInspectionPanel'

// The real generated clients and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const INSPECTION = `${BASE}/api/runs/${RUN}/local-execution-sequence/inspection`
const ADMISSIONS = `${INSPECTION}/admissions`
const stepUrl = (ordinal: number) => `${INSPECTION}/steps/${stepIdFor(ordinal)}`

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

/** A queued sequence whose index lists the two steps its ledger links (the third admission is still waiting and has no step). */
const queuedIndex = (steps = [stepFor(0), stepFor(1)]) =>
  recordedIndexFor(RUN, steps, {
    sequence: headerFor({ admissionMode: 'WaitForAdmission', recordedState: 'WaitingForAdmission', reasonCode: null, endedAtUtc: null }),
  })

async function openInspector(network: Network, index: unknown = queuedIndex()) {
  respond(network, INSPECTION, index)
  const view = render(<LocalExecutionInspectionPanel runId={RUN} />)
  fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
  await screen.findByText(/Recorded state/)
  return view
}

const openLedger = async () => {
  fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
  await screen.findByText(/Queued sequence/)
}

const ledgerRequests = (network: Network) => network.requests.filter((request) => request.url === ADMISSIONS)

describe('LocalExecutionAdmissionPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is offered only for a recorded queued sequence and is collapsed, reading nothing, until the owner opens it', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    await openInspector(network)

    const toggle = screen.getByRole('button', { name: 'Inspect queue admissions' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toEqual([{ method: 'GET', url: INSPECTION }])

    fireEvent.click(toggle)
    await screen.findByText(/Queued sequence/)

    expect(network.requests).toEqual([
      { method: 'GET', url: INSPECTION },
      { method: 'GET', url: ADMISSIONS },
    ])
    expect(screen.getByText(ADMISSIONS_NOTE)).toBeInTheDocument()
  })

  it('is not offered for an Immediate sequence, a run without a sequence or an unavailable index', async () => {
    const network = installNetwork()
    await openInspector(network, recordedIndexFor())
    expect(screen.queryByRole('button', { name: 'Inspect queue admissions' })).toBeNull()
    expect(ledgerRequests(network)).toHaveLength(0)
  })

  it('lists every admission in order with its state, recorded observation and no operation for a wait', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    await openInspector(network)
    await openLedger()

    const items = within(screen.getByRole('region', { name: 'Inspect queue admissions' })).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('Admission 1 · recorded order 11')
    expect(items[0]).toHaveTextContent('Consumed')
    expect(items[1]).toHaveTextContent('Admission 2 · recorded order 13')
    expect(items[1]).toHaveTextContent('recipe #1 Unit <b>tests</b>')
    expect(items[2]).toHaveTextContent('Admission 3 · recorded order 15')
    expect(items[2]).toHaveTextContent('Waiting (recorded; owns no workspace, slot or step)')
    expect(items[2]).toHaveTextContent('Last recorded admission check: Waiting for the workspace')
    expect(items[2]).toHaveTextContent(ADMISSION_NO_STEP_WAITING)
    expect(within(items[2]).queryByRole('button')).toBeNull()
    expect(document.querySelector('b')).toBeNull()
  })

  it('never promises a start, a rank or current capacity and mounts no queue, execution or recovery control', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    await openInspector(network)
    await openLedger()

    const region = screen.getByRole('region', { name: 'Inspect queue admissions' })
    const buttons = within(region).getAllByRole('button').map((button) => button.textContent)
    expect(buttons.every((text) => /Hide queue admissions|Reload queue admissions|Open step evidence/.test(text ?? ''))).toBe(true)
    expect(region.textContent).not.toMatch(/position \d|will start|starts at|estimated|expected to start/i)
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
  })

  it('opens the existing selected-step evidence for a linked step explicitly, reading only that step', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    respond(network, stepUrl(1), verificationStepAnswerFor(1))
    await openInspector(network)
    await openLedger()
    const before = network.requests.length

    fireEvent.click(screen.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' }))

    await waitFor(() => expect(network.requests.slice(before)).toEqual([{ method: 'GET', url: stepUrl(1) }]))
    expect(await screen.findByText(/linked verification run/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Inspect step 2' })).toHaveAttribute('aria-pressed', 'true')
    expect(ledgerRequests(network)).toHaveLength(1)
    expect(network.requests.filter((request) => request.url.includes('artifact-window'))).toHaveLength(0)
  })

  it('opens no operation for a retired admission that created no step and shows why', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, stoppedWhileWaitingHistoryFor())
    await openInspector(network)
    await openLedger()

    const items = within(screen.getByRole('region', { name: 'Inspect queue admissions' })).getAllByRole('listitem')
    expect(items[2]).toHaveTextContent('Retired (closed without becoming an operation)')
    expect(items[2]).toHaveTextContent(ADMISSION_NO_STEP_RETIRED)
    expect(within(items[2]).queryByRole('button')).toBeNull()
    expect(screen.getByText(/Stopped/)).toBeInTheDocument()
  })

  it('shows a retired reservation with its revoked step and an unresolved one held, each opening only the recorded step', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, retiredWithStepHistoryFor())
    await openInspector(network)
    await openLedger()
    expect(screen.getByText(/revoked unused/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open linked step 2 evidence from admission 2' })).toBeInTheDocument()

    respond(network, ADMISSIONS, unresolvedHistoryFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText(/held unresolved/)).toBeInTheDocument()
    expect(screen.getByText(/Unresolved \(a reservation that could not be proved unused or claimed; held, not released\)/)).toBeInTheDocument()

    respond(network, ADMISSIONS, reservedHistoryFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText(/Reserved \(its preparation was reserved; nothing is claimed\)/)).toBeInTheDocument()
  })

  it('requires an explicit index reload when the linked step is not in the validated sequence index, and searches nothing', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    await openInspector(network, queuedIndex([stepFor(0)]))
    await openLedger()
    const before = network.requests.length

    expect(screen.getByText(ADMISSION_STEP_NOT_INDEXED)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open linked step 2 evidence from admission 2' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Open linked step 1 evidence from admission 1' })).toBeInTheDocument()
    expect(network.requests).toHaveLength(before)

    // The reload is a new inspector visit that ends the ledger of the earlier one; the owner opens the ledger again to link the step.
    respond(network, INSPECTION, queuedIndex())
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded sequence' }))
    await screen.findByText(/Recorded state/)
    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(ledgerRequests(network)).toHaveLength(1)
    await openLedger()
    expect(await screen.findByRole('button', { name: 'Open linked step 2 evidence from admission 2' })).toBeInTheDocument()
    expect(ledgerRequests(network)).toHaveLength(2)
  })

  it('offers no link into an index of another recorded sequence', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, {
      ...recordedHistoryFor(),
      sequence: { ...(recordedHistoryFor().sequence as Record<string, unknown>), sequenceId: guidOf('s', 9) },
    })
    await openInspector(network)
    await openLedger()

    expect(screen.getAllByText(ADMISSION_STEP_OTHER_SEQUENCE).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: /Open linked step/ })).toBeNull()
  })

  it('shows NotRecorded, NotQueued and Unavailable as themselves and never as an empty queue', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, notRecordedHistoryFor())
    await openInspector(network)
    await openLedger().catch(() => undefined)
    fireEvent.click(screen.queryByRole('button', { name: 'Inspect queue admissions' }) ?? screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText(ADMISSIONS_NOT_RECORDED)).toBeInTheDocument()

    respond(network, ADMISSIONS, notQueuedHistoryFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText(ADMISSIONS_NOT_QUEUED)).toBeInTheDocument()
    expect(screen.queryByText(ADMISSIONS_NOT_RECORDED)).toBeNull()

    respond(network, ADMISSIONS, unavailableHistoryFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText(ADMISSIONS_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(ADMISSIONS_NOT_QUEUED)).toBeNull()
    expect(screen.queryByRole('listitem', { name: /Admission/ })).toBeNull()
  })

  it('shows nothing from an incoherent answer and a request failure as its own fixed message', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, { ...recordedHistoryFor(), extra: 1 })
    await openInspector(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    expect(await screen.findByText(/not coherent for this run/)).toBeInTheDocument()
    expect(screen.queryByText(/Queued sequence/)).toBeNull()

    respond(network, ADMISSIONS, { status: 404, errors: [{ code: 'local_execution_inspection.not_found' }] }, 404)
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText('The host no longer knows this run.')).toBeInTheDocument()

    respond(network, ADMISSIONS, { errors: [{ code: 'boom' }] }, 500)
    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    expect(await screen.findByText('The recorded queue admissions could not be read.')).toBeInTheDocument()
  })

  it('reloads the ledger with exactly one more request and closing ends it, so a reopening starts collapsed and clean', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    await openInspector(network)
    await openLedger()

    fireEvent.click(screen.getByRole('button', { name: 'Reload queue admissions' }))
    await waitFor(() => expect(ledgerRequests(network)).toHaveLength(2))

    fireEvent.click(screen.getByRole('button', { name: 'Hide queue admissions' }))
    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Inspect queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(ledgerRequests(network)).toHaveLength(2)

    // Closing the whole inspector ends the ledger too: reopening it shows the ledger collapsed again.
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await screen.findByText(/Queued sequence/)
    fireEvent.click(screen.getByRole('button', { name: 'Hide local execution inspection' }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)
    expect(screen.getByRole('button', { name: 'Inspect queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(ledgerRequests(network)).toHaveLength(3)
  })

  it('ignores a ledger that settles after the region was closed', async () => {
    const network = installNetwork()
    let release: (response: Response) => void = () => undefined
    network.routes.set(ADMISSIONS, () => new Promise<Response>((resolve) => { release = resolve }))
    await openInspector(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect queue admissions' }))
    await waitFor(() => expect(ledgerRequests(network)).toHaveLength(1))

    fireEvent.click(screen.getByRole('button', { name: 'Hide queue admissions' }))
    release(json(recordedHistoryFor()))
    await Promise.resolve()
    await Promise.resolve()

    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Inspect queue admissions' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('does not offer the link when the same admission is retained by another run: a run change shows a closed, empty ledger', async () => {
    const network = installNetwork()
    respond(network, ADMISSIONS, recordedHistoryFor())
    const view = await openInspector(network)
    await openLedger()

    const other = guidOf('r', 2)
    view.rerender(<LocalExecutionInspectionPanel runId={other} />)

    expect(screen.queryByText(/Queued sequence/)).toBeNull()
    expect(screen.queryByRole('button', { name: /Open linked step/ })).toBeNull()
    expect(admissionFor(0).stepId).toBe(stepIdFor(0))
  })
})
