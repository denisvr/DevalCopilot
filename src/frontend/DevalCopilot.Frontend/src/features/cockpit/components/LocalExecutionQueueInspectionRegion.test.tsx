// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { headerFor, recordedIndexFor, stepFor } from '../localExecutionInspectionFixture'
import {
  emptyQueueFor,
  recordedQueueFor,
  reservedEntryFor,
  runIdFor,
  unavailableQueueFor,
  unresolvedEntryFor,
  waitingEntryFor,
} from '../localExecutionQueueInspectionFixture'
import { QUEUE_OVERVIEW_EMPTY, QUEUE_OVERVIEW_NOTE, QUEUE_OVERVIEW_UNAVAILABLE } from '../localExecutionQueueInspectionCopy'
import { LocalExecutionQueueInspectionRegion } from './LocalExecutionQueueInspectionRegion'

// The real generated client and transport run against a fake network, so every request the global region sends is counted exactly, and
// the selected cycle mounts the existing local execution inspector unchanged. A clearly fictitious session.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const QUEUE = `${BASE}/api/local-execution-queue/inspection`
const inspection = (run: string) => `${BASE}/api/runs/${run}/local-execution-sequence/inspection`
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const queuedIndex = (run: string) =>
  recordedIndexFor(run, [stepFor(0)], {
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
const urls = (network: Network) => network.requests.map((request) => request.url)
const DEFAULT = [waitingEntryFor(1, { projectName: '<b>Alpha</b> & "quoted"' }), reservedEntryFor(2), unresolvedEntryFor(3)]
const open = () => fireEvent.click(screen.getByRole('button', { name: 'Inspect outstanding queue admissions' }))

describe('the global outstanding queue admission region', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed, reads nothing on mount and offers nothing but its one opening action', () => {
    const network = installNetwork()

    render(<LocalExecutionQueueInspectionRegion />)

    expect(screen.getByRole('button', { name: 'Inspect outstanding queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getAllByRole('button')).toHaveLength(1)
    expect(network.requests).toEqual([])
  })

  it('reads one bodyless GET when opened and shows each recorded state, the sequence state and the recorded times as literal text', async () => {
    const network = installNetwork()
    respond(network, QUEUE, recordedQueueFor(DEFAULT))
    const { container } = render(<LocalExecutionQueueInspectionRegion />)

    open()
    await screen.findByText(/Project <b>Alpha<\/b> & "quoted"/)

    expect(network.requests).toEqual([{ method: 'GET', url: QUEUE }])
    expect(container.querySelector('b')).toBeNull()
    expect(screen.getByText(QUEUE_OVERVIEW_NOTE)).toBeInTheDocument()
    expect(screen.getByText(/Waiting \(recorded; owns no workspace, slot or step\)/)).toBeInTheDocument()
    expect(screen.getByText(/Reserved \(its preparation was reserved; nothing is claimed\)/)).toBeInTheDocument()
    expect(screen.getByText(/Unresolved \(a reservation that could not be proved unused or claimed; held, not released\)/)).toBeInTheDocument()
    expect(screen.getAllByText(/Last recorded admission check: /)).toHaveLength(3)
    expect(screen.getAllByText(/recorded advancement deadline 2026-10-20T11:00:00.0000000Z/)).toHaveLength(3)
    expect(screen.getAllByText(/not ended/)).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /^Select recorded cycle/ })).toHaveLength(3)
  })

  it('shows the Empty answer as not idle, the Unavailable answer as nothing proved, and a failure or incoherent answer as a refusal', async () => {
    const network = installNetwork()
    respond(network, QUEUE, emptyQueueFor())
    const view = render(<LocalExecutionQueueInspectionRegion />)
    open()
    expect(await screen.findByText(QUEUE_OVERVIEW_EMPTY)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Select recorded cycle/ })).toBeNull()
    view.unmount()

    respond(network, QUEUE, unavailableQueueFor())
    render(<LocalExecutionQueueInspectionRegion />)
    open()
    expect(await screen.findByText(QUEUE_OVERVIEW_UNAVAILABLE)).toBeInTheDocument()
  })

  it('refuses an incoherent answer and a failed read with an alert and no row', async () => {
    const network = installNetwork()
    respond(network, QUEUE, { ...recordedQueueFor(DEFAULT), extra: 1 })
    const view = render(<LocalExecutionQueueInspectionRegion />)
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent(/not coherent/)
    expect(screen.queryByText(/Project /)).toBeNull()
    view.unmount()

    respond(network, QUEUE, { errors: [{ code: 'boom' }] }, 500)
    render(<LocalExecutionQueueInspectionRegion />)
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be read/)
    expect(screen.queryByText(/Project /)).toBeNull()
  })

  it('closes without a read, reads again only when reopened or explicitly reloaded, and never refreshes by itself', async () => {
    const network = installNetwork()
    respond(network, QUEUE, recordedQueueFor(DEFAULT))
    render(<LocalExecutionQueueInspectionRegion />)
    open()
    await screen.findByText(/Project 3/)

    fireEvent.click(screen.getByRole('button', { name: 'Hide outstanding queue admissions' }))
    expect(screen.queryByText(/Project 3/)).toBeNull()
    expect(network.requests).toHaveLength(1)

    open()
    await screen.findByText(/Project 3/)
    expect(network.requests).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Reload outstanding queue admissions' }))
    await waitFor(() => expect(network.requests).toHaveLength(3))
    expect(urls(network).every((url) => url === QUEUE)).toBe(true)
  })

  describe('selecting a recorded cycle', () => {
    it('renders the existing inspector for exactly that run, collapsed, reading nothing until its own explicit action', async () => {
      const network = installNetwork()
      respond(network, QUEUE, recordedQueueFor(DEFAULT))
      respond(network, inspection(runIdFor(2)), queuedIndex(runIdFor(2)))
      render(<LocalExecutionQueueInspectionRegion />)
      open()
      await screen.findByText(/Project 2/)

      fireEvent.click(screen.getByRole('button', { name: /^Select recorded cycle: project Project 2/ }))

      const inspector = await screen.findByRole('region', { name: 'Inspect local execution' })
      expect(within(inspector).getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
      expect(screen.getByText(/Selected recorded cycle: project Project 2 · run /)).toBeInTheDocument()
      expect(network.requests).toEqual([{ method: 'GET', url: QUEUE }])

      fireEvent.click(within(inspector).getByRole('button', { name: 'Inspect local execution' }))
      await within(inspector).findByText(/Recorded state/)
      expect(urls(network)).toEqual([QUEUE, inspection(runIdFor(2))])
      expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    })

    it('replaces the inspector when another cycle is selected, never reading the one that was not selected', async () => {
      const network = installNetwork()
      respond(network, QUEUE, recordedQueueFor(DEFAULT))
      respond(network, inspection(runIdFor(1)), queuedIndex(runIdFor(1)))
      respond(network, inspection(runIdFor(3)), queuedIndex(runIdFor(3)))
      render(<LocalExecutionQueueInspectionRegion />)
      open()
      await screen.findByText(/Project 3/)

      fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[0])
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect local execution' }))
      await screen.findByText(/Recorded state/)
      fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[2])

      expect(screen.getAllByRole('region', { name: 'Inspect local execution' })).toHaveLength(1)
      expect(screen.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
      expect(screen.queryByText(/Recorded state/)).toBeNull()
      expect(screen.getByText(/Selected recorded cycle: project Project 3/)).toBeInTheDocument()
      expect(urls(network)).toEqual([QUEUE, inspection(runIdFor(1))])

      fireEvent.click(screen.getByRole('button', { name: 'Clear selected recorded cycle' }))
      expect(screen.queryByRole('region', { name: 'Inspect local execution' })).toBeNull()
      expect(urls(network)).toEqual([QUEUE, inspection(runIdFor(1))])
    })

    it('discards the rows and the selected inspector at once on reload, and a failed reload shows none of them as fresh', async () => {
      const network = installNetwork()
      respond(network, QUEUE, recordedQueueFor(DEFAULT))
      respond(network, inspection(runIdFor(1)), queuedIndex(runIdFor(1)))
      render(<LocalExecutionQueueInspectionRegion />)
      open()
      await screen.findByText(/Project 3/)
      fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[0])
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect local execution' }))
      await screen.findByText(/Recorded state/)

      respond(network, QUEUE, { errors: [{ code: 'boom' }] }, 500)
      fireEvent.click(screen.getByRole('button', { name: 'Reload outstanding queue admissions' }))

      expect(screen.queryByText(/Project 3/)).toBeNull()
      expect(screen.queryByRole('region', { name: 'Inspect local execution' })).toBeNull()
      expect(screen.queryByText(/Recorded state/)).toBeNull()
      expect(await screen.findByRole('alert')).toHaveTextContent(/could not be read/)
      expect(screen.queryByText(/Project /)).toBeNull()
    })

    it('ends the selected inspector when the region is closed, and reopening shows nothing of it', async () => {
      const network = installNetwork()
      respond(network, QUEUE, recordedQueueFor(DEFAULT))
      render(<LocalExecutionQueueInspectionRegion />)
      open()
      await screen.findByText(/Project 3/)
      fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[0])
      expect(await screen.findByRole('region', { name: 'Inspect local execution' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('button', { name: 'Hide outstanding queue admissions' }))
      expect(screen.queryByRole('region', { name: 'Inspect local execution' })).toBeNull()
      open()
      await screen.findByText(/Project 3/)

      expect(screen.queryByRole('region', { name: 'Inspect local execution' })).toBeNull()
      expect(screen.queryByText(/Selected recorded cycle/)).toBeNull()
    })
  })

  it('mounts no queue, execution, retry, release, ordering or mutation control', async () => {
    const network = installNetwork()
    respond(network, QUEUE, recordedQueueFor(DEFAULT))
    render(<LocalExecutionQueueInspectionRegion />)
    open()
    await screen.findByText(/Project 3/)

    const names = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent ?? '')

    expect(names).toEqual([
      'Hide outstanding queue admissions',
      'Reload outstanding queue admissions',
      ...DEFAULT.map((entry) => expect.stringMatching(new RegExp(`^Select recorded cycle: project ${String(entry.projectName).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`))),
    ])
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
  })
})
