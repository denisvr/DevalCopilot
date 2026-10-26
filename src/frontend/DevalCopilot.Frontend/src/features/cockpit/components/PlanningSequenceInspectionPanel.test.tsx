import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  FIVE_STEPS,
  RUN,
  attemptIdFor,
  headerFor,
  notRecordedIndexFor,
  proposalFor,
  recordedIndexFor,
  refusedStepAnswerFor,
  reservedStepFor,
  stepAnswerFor,
  stepIdFor,
  unavailableIndexFor,
} from '../planningSequenceInspectionFixture'
import {
  PLANNING_INSPECTION_NOTE,
  PLANNING_INSPECTION_NOT_RECORDED,
  PLANNING_INSPECTION_UNAVAILABLE,
  PLANNING_STEP_NO_LINKED_ATTEMPT,
} from '../planningSequenceInspectionCopy'
import { PlanningSequenceInspectionPanel } from './PlanningSequenceInspectionPanel'

// The real generated clients and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const INSPECTION = `${BASE}/api/runs/${RUN}/planning-sequence/inspection`

interface Network {
  requests: { method: string; url: string; body: unknown }[]
  routes: Map<string, () => Promise<Response>>
}

function installNetwork(): Network {
  const network: Network = { requests: [], routes: new Map() }
  vi.spyOn(window, 'fetch').mockImplementation((input, init) => {
    const url = String(input)
    network.requests.push({ method: init?.method ?? 'GET', url, body: init?.body })
    const route = network.routes.get(url)
    return route === undefined ? Promise.resolve(json({ errors: [{ code: 'unrouted' }] }, 404)) : route()
  })
  return network
}

const respond = (network: Network, url: string, body: unknown, status = 200) => network.routes.set(url, () => Promise.resolve(json(body, status)))

const stepUrl = (ordinal: number) => `${INSPECTION}/steps/${stepIdFor(ordinal)}`
const evidenceUrl = (ordinal: number) => `${BASE}/api/runs/${RUN}/agent-attempts/${attemptIdFor(ordinal)}/evidence`
const messageUrl = (ordinal: number) => `${BASE}/api/runs/${RUN}/collaboration-messages/${proposalFor(ordinal)}/evidence`

async function open(network: Network) {
  render(<PlanningSequenceInspectionPanel runId={RUN} />)
  fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))
  await screen.findByText(/Recorded state/)
  return network
}

describe('PlanningSequenceInspectionPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the index', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    render(<PlanningSequenceInspectionPanel runId={RUN} />)

    expect(screen.getByRole('button', { name: 'Inspect recorded planning' })).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toEqual([])

    fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))
    await screen.findByText(/Recorded state/)

    expect(network.requests).toEqual([{ method: 'GET', url: INSPECTION, body: undefined }])
    expect(screen.getByText(PLANNING_INSPECTION_NOTE)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Inspect planning step/ })).toHaveLength(5)
    expect(screen.getByRole('button', { name: 'Hide planning inspection' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('mounts no arming, stop, implementation, recovery, approval or publication control', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), stepAnswerFor(1))
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByLabelText('Selected Agent attempt evidence')

    const region = screen.getByLabelText('Inspect recorded planning')
    const names = within(region).getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent ?? '')
    expect(names.filter((name) => /arm|stop|implement|approve|publish|commit|recover|resume|retry|release|authorize/i.test(name))).toEqual([])
    expect(names).toContain('Reload recorded planning')
  })

  it('renders the whole recorded structure as literal text and shows its recorded state as bookkeeping', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    await open(network)

    expect(screen.getByText(/Awaiting a Human decision · recorded as escalated after the second round · revision 12/)).toBeInTheDocument()
    expect(screen.getByText(new RegExp(headerFor().sequenceId))).toBeInTheDocument()
    expect(screen.getByText(/Step 1 · Planner \(Codex\) · Concluded/)).toBeInTheDocument()
    expect(screen.getByText(/Step 5 · Resolver \(Codex\) · Concluded/)).toBeInTheDocument()
  })

  it('shows zero steps, NotRecorded and Unavailable as themselves and offers no step', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor(RUN, [], { sequence: headerFor({ recordedState: 'Armed', reasonCode: null, endedAtUtc: null }) }))
    render(<PlanningSequenceInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))
    expect(await screen.findByText('No step has been reserved.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Inspect planning step/ })).toBeNull()

    respond(network, INSPECTION, notRecordedIndexFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded planning' }))
    expect(await screen.findByText(PLANNING_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(screen.queryByText('No step has been reserved.')).toBeNull()

    respond(network, INSPECTION, unavailableIndexFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded planning' }))
    expect(await screen.findByText(PLANNING_INSPECTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(PLANNING_INSPECTION_NOT_RECORDED)).toBeNull()
  })

  it('shows nothing from an incoherent answer, also one carrying markup, and never an empty success', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor(RUN, FIVE_STEPS, { sequence: headerFor({ reasonCode: '<b>plan_accepted</b>' }) }))
    render(<PlanningSequenceInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('not coherent')
    expect(screen.queryByText(/Recorded state/)).toBeNull()
    expect(screen.queryByText(PLANNING_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(document.querySelector('b')).toBeNull()
    expect(screen.queryByRole('button', { name: /Inspect planning step/ })).toBeNull()
  })

  it('reads only the selected step, then mounts the existing attempt detail; the input Proposal opens only on request', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), stepAnswerFor(1))
    await open(network)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByLabelText('Selected Agent attempt evidence')
    await waitFor(() => expect(network.requests).toHaveLength(3))

    // Only the existing attempt-evidence read follows, as on any attempt selection; every other section and the input Proposal wait.
    expect(network.requests.map((request) => request.url)).toEqual([INSPECTION, stepUrl(1), evidenceUrl(1)])
    expect(network.requests.every((request) => request.method === 'GET' && request.body === undefined)).toBe(true)
    expect(within(screen.getByLabelText('Selected Agent attempt evidence')).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
    expect(screen.getByText(new RegExp(`Recorded input Proposal this step names: ${proposalFor(1)}`))).toBeInTheDocument()

    const detail = screen.getByLabelText('Selected planning step')
    fireEvent.click(within(detail).getByText('Attempt evidence'))
    await waitFor(() => expect(network.requests.map((request) => request.url)).toContain(messageUrl(1)))
    expect(network.requests).toHaveLength(4)
  })

  it('keeps a linked attempt inspectable although its own evidence cannot be read, and fabricates no success', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(0), stepAnswerFor(0))
    await open(network)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 1' }))

    expect(await screen.findByLabelText('Selected Agent attempt evidence')).toBeInTheDocument()
    expect(screen.getByText('Recorded attempt link')).toBeInTheDocument()
    expect(screen.queryByText(/Recorded input Proposal this step names/)).toBeNull()
    expect(screen.getByLabelText('Selected planning step')).toHaveTextContent(attemptIdFor(0))
  })

  it('shows the no-link shape without any guess, an unavailable step as unavailable, and retries only that step', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor(RUN, [FIVE_STEPS[0], reservedStepFor(1)], { sequence: headerFor({ recordedState: 'PreparingStep', reasonCode: null, endedAtUtc: null }) }))
    respond(network, stepUrl(1), refusedStepAnswerFor(1, 'NoLinkedAttempt'))
    render(<PlanningSequenceInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))
    await screen.findByText(/Recorded state/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    expect(await screen.findByText(PLANNING_STEP_NO_LINKED_ATTEMPT)).toBeInTheDocument()
    expect(screen.queryByLabelText('Selected Agent attempt evidence')).toBeNull()

    respond(network, stepUrl(0), refusedStepAnswerFor(0, 'Unavailable'))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 1' }))
    expect(await screen.findByText(/could not be proved, or the sequence changed while it was being read/)).toBeInTheDocument()
    expect(screen.queryByText(PLANNING_STEP_NO_LINKED_ATTEMPT)).toBeNull()

    network.routes.delete(stepUrl(0))
    const before = network.requests.length
    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByText(PLANNING_STEP_NO_LINKED_ATTEMPT)
    expect(network.requests.length).toBe(before + 1)
  })

  it('shows a failed step read and an incoherent association as failures with no content', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), stepAnswerFor(1, { agent: null }))
    await open(network)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    expect(await screen.findByText(/not coherent for this step/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Selected Agent attempt evidence')).toBeNull()

    respond(network, stepUrl(1), stepAnswerFor(1))
    fireEvent.click(screen.getByRole('button', { name: 'Try this step again' }))
    expect(await screen.findByLabelText('Selected Agent attempt evidence')).toBeInTheDocument()
  })

  it('reload ends the selection and its content before reading again, and sends exactly one new index request', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), stepAnswerFor(1))
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByLabelText('Selected Agent attempt evidence')

    let release!: (response: Response) => void
    network.routes.set(INSPECTION, () => new Promise<Response>((resolve) => (release = resolve)))
    const before = network.requests.filter((request) => request.url === INSPECTION).length
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded planning' }))

    expect(screen.queryByLabelText('Selected planning step')).toBeNull()
    expect(screen.queryByLabelText('Selected Agent attempt evidence')).toBeNull()
    expect(screen.queryByRole('button', { name: /Inspect planning step/ })).toBeNull()
    expect(screen.getByText('Reading the recorded planning sequence…')).toBeInTheDocument()
    expect(network.requests.filter((request) => request.url === INSPECTION)).toHaveLength(before + 1)

    release(json(recordedIndexFor()))
    expect(await screen.findAllByRole('button', { name: /Inspect planning step/ })).toHaveLength(5)
    expect(screen.queryByLabelText('Selected planning step')).toBeNull()
  })

  it('Hide closes the region and discards everything; reopening reads again', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), stepAnswerFor(1))
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect planning step 2' }))
    await screen.findByLabelText('Selected Agent attempt evidence')

    fireEvent.click(screen.getByRole('button', { name: 'Hide planning inspection' }))
    expect(screen.queryByLabelText('Selected planning step')).toBeNull()
    expect(screen.queryByText(/Recorded state/)).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded planning' }))
    await screen.findByText(/Recorded state/)
    expect(network.requests.filter((request) => request.url === INSPECTION)).toHaveLength(2)
    expect(screen.queryByLabelText('Selected planning step')).toBeNull()
  })

  it('is disabled and sends nothing without a run', () => {
    const network = installNetwork()
    render(<PlanningSequenceInspectionPanel runId={null} />)

    expect(screen.getByRole('button', { name: 'Inspect recorded planning' })).toBeDisabled()
    expect(network.requests).toEqual([])
  })
})
