// @vitest-environment jsdom
import { fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RUN, operationIdFor, stepIdFor } from '../localExecutionInspectionFixture'
import {
  RELEASE_INSPECTION_FAILURES,
  RELEASE_INSPECTION_NOTE,
  RELEASE_INSPECTION_NOT_RECORDED,
  RELEASE_INSPECTION_PHYSICAL_NOTE,
  RELEASE_INSPECTION_RESULT_NOTE,
  RELEASE_INSPECTION_UNAVAILABLE,
} from '../localExecutionReleaseInspectionCopy'
import {
  OBSERVATION_ID,
  RELEASE_ID,
  WITNESS_ID,
  notRecordedReleaseInspectionFor,
  recordedReleaseInspectionFor,
  recordedVerificationReleaseInspectionFor,
  releaseInspectionTargetFor,
  unavailableReleaseInspectionFor,
} from '../localExecutionReleaseInspectionFixture'
import { LocalExecutionReleaseInspectionRegion } from './LocalExecutionReleaseInspectionRegion'

// The real generated client and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const receiptUrl = (ordinal: number) => `${BASE}/api/runs/${RUN}/local-execution-sequence/inspection/steps/${stepIdFor(ordinal)}/ownership-release`
const KEY = JSON.stringify(['visit', stepIdFor(0)])

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
const OPEN = { name: 'Inspect recorded quarantine release' }

function renderRegion(ordinal = 0, key = KEY) {
  return render(<LocalExecutionReleaseInspectionRegion ownerKey={key} target={releaseInspectionTargetFor(ordinal)} />)
}

describe('LocalExecutionReleaseInspectionRegion', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the receipt', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getAllByRole('button')).toHaveLength(1)
    expect(network.requests).toEqual([])

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    expect(network.requests).toEqual([{ method: 'GET', url: receiptUrl(0) }])
    expect(screen.getByRole('button', { name: 'Hide recorded quarantine release' })).toHaveAttribute('aria-expanded', 'true')
    for (const line of RELEASE_INSPECTION_NOTE) {
      expect(screen.getByText(line)).toBeInTheDocument()
    }
  })

  it('shows the past Human decision first, then the unchanged failed result, then the distinct physical facts, with identities secondary', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    const text = document.body.textContent ?? ''
    const order = ['Human release', 'Recorded result of the operation (unchanged)', 'Physical termination observed by the original host', 'Recorded identities'].map(
      (heading) => text.indexOf(heading),
    )
    expect(order.every((position) => position >= 0)).toBe(true)
    expect([...order].sort((a, b) => a - b)).toEqual(order)
    expect(screen.getByText(/Released 2026-10-20T09:06:00.1234567Z · recorded as Human run event #42/)).toBeInTheDocument()
    expect(screen.getByText(/Failed · process outcome Timed out \(the host stopped it\) · semantic exit code not recorded · process duration 50000000 ticks/)).toBeInTheDocument()
    expect(screen.getByText(/Physical exit code 137 · observed elapsed 40000 ticks · dispatched 2026-10-20T09:00:30.1234567Z · observation recorded 2026-10-20T09:05:30.1234567Z/)).toBeInTheDocument()
    expect(screen.getByText(/Agent attempt · step 1 · Implementation \(Claude Code\) · pass 0/)).toBeInTheDocument()
    expect(screen.getByText(/NeedsAttention|Needs attention/i)).toBeInTheDocument()
    expect(screen.getByText(RELEASE_INSPECTION_RESULT_NOTE)).toBeInTheDocument()
    expect(screen.getByText(RELEASE_INSPECTION_PHYSICAL_NOTE)).toBeInTheDocument()
    const identities = within(screen.getByText('Recorded identities').closest('details') as HTMLElement)
    for (const id of [RELEASE_ID, operationIdFor(0), WITNESS_ID, OBSERVATION_ID]) {
      expect(identities.getByText(id)).toBeInTheDocument()
    }
  })

  it('explains the release as a past decision with the invoked-process-only limit and the unsigned relational-consistency qualification', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    const note = RELEASE_INSPECTION_NOTE.join(' ')
    expect(note).toMatch(/past decision/)
    expect(note).toMatch(/did not change this operation's failed result or the ended cycle/)
    expect(note).toMatch(/not show whether the workspace or capacity is available now/)
    expect(note).toMatch(/approved nothing and it started nothing/)
    expect(note).toMatch(/process the host invoked only/)
    expect(note).toMatch(/not signed or authenticated history/)
    expect(note).toMatch(/not one atomic replay/)
  })

  it('shows a verification receipt as a verification run', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(1), recordedVerificationReleaseInspectionFor())
    renderRegion(1)
    fireEvent.click(screen.getByRole('button', OPEN))

    expect(await screen.findByText(/Verification run · step 2 · Verification recipe · pass 1/)).toBeInTheDocument()
    expect(screen.getByText(/TimedOut · process outcome Timed out \(the host stopped it\)/)).toBeInTheDocument()
  })

  it('shows NotRecorded and Unavailable as themselves: no receipt and no promise of a held or released owner', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), notRecordedReleaseInspectionFor(0))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(RELEASE_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(screen.queryByText('Human release')).toBeNull()

    respond(network, receiptUrl(0), unavailableReleaseInspectionFor(0))
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded quarantine release' }))
    expect(await screen.findByText(RELEASE_INSPECTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(RELEASE_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(screen.queryByText('Human release')).toBeNull()

    for (const copy of [RELEASE_INSPECTION_NOT_RECORDED, RELEASE_INSPECTION_UNAVAILABLE]) {
      expect(copy).toMatch(/held or was released|held or released/)
      expect(copy).toMatch(/live workspace and capacity readings/)
    }

    expect(RELEASE_INSPECTION_UNAVAILABLE).toMatch(/checksum alone/)
    expect(RELEASE_INSPECTION_UNAVAILABLE).toMatch(/neither inferred nor repaired/)
  })

  it('shows nothing from an incoherent answer, a failed read or a vanished step, each with its fixed copy', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), { ...recordedReleaseInspectionFor(), extra: 1 })
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByRole('alert')).toHaveTextContent(RELEASE_INSPECTION_FAILURES.invalid)
    expect(screen.queryByText('Human release')).toBeNull()

    network.routes.set(receiptUrl(0), () => Promise.reject(new Error('down')))
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded quarantine release' }))
    expect(await screen.findByText(RELEASE_INSPECTION_FAILURES.read)).toBeInTheDocument()

    respond(network, receiptUrl(0), { status: 404, errors: [{ code: 'local_execution_inspection.not_found' }], traceId: 'trace' }, 404)
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded quarantine release' }))
    expect(await screen.findByText(RELEASE_INSPECTION_FAILURES['not-found'])).toBeInTheDocument()
    expect(screen.queryByText(RELEASE_INSPECTION_FAILURES.read)).toBeNull()
  })

  it('replaces the read and its result on an explicit reload and discards the old receipt before the new one is shown', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    let release!: () => void
    network.routes.set(receiptUrl(0), () => new Promise<Response>((resolve) => (release = () => resolve(json(notRecordedReleaseInspectionFor(0))))))
    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded quarantine release' }))

    expect(await screen.findByText(/Reading the recorded quarantine release/)).toBeInTheDocument()
    expect(screen.queryByText('Human release')).toBeNull()
    release()
    expect(await screen.findByText(RELEASE_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(network.requests.filter((request) => request.url === receiptUrl(0))).toHaveLength(2)
  })

  it('hides on close, reads nothing more and shows the closed button again', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded quarantine release' }))

    expect(screen.queryByText('Human release')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toHaveLength(1)
  })

  it('mounts no release, confirmation or candidate control, and sends no POST, candidate GET or artifact read', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Hide recorded quarantine release',
      'Reload recorded quarantine release',
    ])
    expect(screen.queryByRole('button', { name: /confirm/i })).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(network.requests.every((request) => request.method === 'GET')).toBe(true)
    expect(network.requests.map((request) => request.url)).toEqual([receiptUrl(0)])
    expect(network.requests.some((request) => request.url.includes('/artifact-window/') || request.url.includes('/steps/') && !request.url.includes('/inspection/'))).toBe(false)
  })

  it('offers no opening without a proved target', () => {
    render(<LocalExecutionReleaseInspectionRegion ownerKey={KEY} target={null} />)

    expect(screen.getByRole('button', OPEN)).toBeDisabled()
  })

  it('renders every host value as literal text', async () => {
    const network = installNetwork()
    respond(network, receiptUrl(0), recordedReleaseInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Human release')

    expect(document.querySelector('b, script, img, a')).toBeNull()
  })
})
