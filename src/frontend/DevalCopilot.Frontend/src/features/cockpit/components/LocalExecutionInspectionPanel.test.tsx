import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  RUN,
  SEQUENCE,
  agentStepAnswerFor,
  guidOf,
  operationIdFor,
  recordedIndexFor,
  refusedStepAnswerFor,
  refusedWindowFor,
  stepFor,
  stepIdFor,
  streamFor,
  verificationFor,
  verificationStepAnswerFor,
  windowAnswerFor,
  notRecordedIndexFor,
  unavailableIndexFor,
} from '../localExecutionInspectionFixture'
import { INSPECTION_NOTE, INSPECTION_NOT_RECORDED, INSPECTION_UNAVAILABLE } from '../localExecutionInspectionCopy'
import { LocalExecutionInspectionPanel } from './LocalExecutionInspectionPanel'

// The real generated clients and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const INSPECTION = `${BASE}/api/runs/${RUN}/local-execution-sequence/inspection`

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

const stepUrl = (ordinal: number) => `${INSPECTION}/steps/${stepIdFor(ordinal)}`
const windowUrl = (ordinal: number, stream: string, query = 'fromOffset=0&maxBytes=16384') => `${stepUrl(ordinal)}/artifact-window/${stream}?${query}`

async function open(network: Network) {
  render(<LocalExecutionInspectionPanel runId={RUN} />)
  fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
  await screen.findByText(/Recorded state/)
  return network
}

describe('LocalExecutionInspectionPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the index', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    render(<LocalExecutionInspectionPanel runId={RUN} />)

    expect(screen.getByRole('button', { name: 'Inspect local execution' })).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toEqual([])

    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)

    expect(network.requests).toEqual([{ method: 'GET', url: INSPECTION }])
    expect(screen.getByText(INSPECTION_NOTE)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Inspect step/ })).toHaveLength(3)
  })

  it('renders every host value as literal text, including a recipe name that looks like markup', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    await open(network)

    expect(screen.getAllByText(/Unit <b>tests<\/b>/).length).toBeGreaterThan(0)
    expect(document.querySelector('b')).toBeNull()
  })

  it('shows NotRecorded and Unavailable as themselves and offers no step', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, notRecordedIndexFor())
    render(<LocalExecutionInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    expect(await screen.findByText(INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Inspect step/ })).toBeNull()

    respond(network, INSPECTION, unavailableIndexFor())
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded sequence' }))
    expect(await screen.findByText(INSPECTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(INSPECTION_NOT_RECORDED)).toBeNull()
  })

  it('shows nothing from an incoherent answer and never an empty success', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, { ...recordedIndexFor(), extra: 1 })
    render(<LocalExecutionInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('not coherent')
    expect(screen.queryByText(/Recorded state/)).toBeNull()
    expect(screen.queryByText(INSPECTION_NOT_RECORDED)).toBeNull()
  })

  it('reads only the selected verification step, then a stream window only after View and Load, with literal text', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), verificationStepAnswerFor(1))
    const out = streamFor(guidOf('a', 1), 120)
    respond(network, windowUrl(1, 'stdout'), windowAnswerFor(1, 'stdout', out, '<script>alert(1)</script> failed', 0, { totalLength: 120 }))
    await open(network)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)
    expect(network.requests.map((request) => request.url)).toEqual([INSPECTION, stepUrl(1)])
    expect(screen.getByText(/Failed · process outcome Exited · exit code 1/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'View standard output of this step' }))
    expect(network.requests).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of this step' }))
    await screen.findByText(/<script>alert\(1\)<\/script> failed/)

    expect(network.requests.map((request) => request.url)).toEqual([INSPECTION, stepUrl(1), windowUrl(1, 'stdout')])
    expect(document.querySelector('script')).toBeNull()
    expect(screen.getByText(/bytes read/)).toBeInTheDocument()
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
  })

  it('shows each refusal state of a window and never text', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), verificationStepAnswerFor(1))
    respond(network, windowUrl(1, 'stdout'), refusedWindowFor(1, 'stdout', 'IntegrityMismatch', 0))
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)
    fireEvent.click(screen.getByRole('button', { name: 'View standard output of this step' }))

    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of this step' }))

    expect(await screen.findByText(/does not match its recorded length and hash/)).toBeInTheDocument()
  })

  it('offers a stream control only for a Recorded descriptor', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(
      network,
      stepUrl(1),
      verificationStepAnswerFor(1, {
        verification: verificationFor(1, { standardError: { status: 'NotRecorded', artifactId: null, byteLength: null, captureOutcome: null, truncated: null } }),
      }),
    )
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)

    expect(screen.getByRole('button', { name: 'View standard output of this step' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'View standard error of this step' })).toBeNull()
    expect(screen.getByText('Standard error: not recorded.')).toBeInTheDocument()
  })

  it('shows the no-operation shape without any guess, and an unavailable step as unavailable', async () => {
    const reserved = stepFor(1, { recordedState: 'Reserved', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null })
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor(RUN, [stepFor(0), reserved]))
    respond(network, stepUrl(1), refusedStepAnswerFor(1, 'NoOperation'))
    render(<LocalExecutionInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))

    expect(await screen.findByText(/Nothing is assumed about whether a reserved operation started/)).toBeInTheDocument()
    respond(network, stepUrl(0), refusedStepAnswerFor(0, 'Unavailable'))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 1' }))
    expect(await screen.findByText(/could not be proved, so none of it is shown/)).toBeInTheDocument()
  })

  it('mounts the existing selected-attempt detail for an Agent step with its own explicit sections collapsed and reads no stream', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(0), agentStepAnswerFor(0))
    await open(network)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 1' }))
    await screen.findByLabelText('Selected Agent attempt evidence')
    await waitFor(() => expect(network.requests).toHaveLength(3))

    const urls = network.requests.map((request) => request.url)
    expect(urls.slice(0, 2)).toEqual([INSPECTION, stepUrl(0)])
    // Only the existing attempt-evidence read follows, as it does on any attempt selection; every other section waits for its own click.
    expect(urls).toHaveLength(3)
    expect(urls[2]).toBe(`${BASE}/api/runs/${RUN}/agent-attempts/${operationIdFor(0)}/evidence`)
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    expect(within(screen.getByLabelText('Selected Agent attempt evidence')).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('reload ends the selection and the step before reading again', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    respond(network, stepUrl(1), verificationStepAnswerFor(1))
    await open(network)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect step 2' }))
    await screen.findByText(/Recorded status/)

    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded sequence' }))

    expect(screen.queryByLabelText('Selected local execution step')).toBeNull()
    await screen.findByText(/Recorded state/)
    expect(screen.queryByLabelText('Selected local execution step')).toBeNull()
    expect(network.requests.map((request) => request.url)).toEqual([INSPECTION, stepUrl(1), INSPECTION])
  })

  it('discards a late answer after close and sends no request after unmount', async () => {
    const network = installNetwork()
    let release!: () => void
    network.routes.set(INSPECTION, () => new Promise<Response>((resolve) => { release = () => resolve(json(recordedIndexFor())) }))
    const { unmount } = render(<LocalExecutionInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    fireEvent.click(screen.getByRole('button', { name: 'Hide local execution inspection' }))

    release()
    await waitFor(() => expect(network.requests).toHaveLength(1))
    expect(screen.queryByText(/Recorded state/)).toBeNull()
    unmount()
    expect(network.requests).toHaveLength(1)
  })

  it('is disabled without a run and sends no mutation request in any state', async () => {
    const network = installNetwork()
    respond(network, INSPECTION, recordedIndexFor())
    const { rerender } = render(<LocalExecutionInspectionPanel runId={null} />)
    expect(screen.getByRole('button', { name: 'Inspect local execution' })).toBeDisabled()

    rerender(<LocalExecutionInspectionPanel runId={RUN} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect local execution' }))
    await screen.findByText(/Recorded state/)

    expect(network.requests.every((request) => request.method === 'GET' && !/\/(stop|arm|claim|commit)/.test(request.url))).toBe(true)
    expect(SEQUENCE).toBeTruthy()
  })
})
