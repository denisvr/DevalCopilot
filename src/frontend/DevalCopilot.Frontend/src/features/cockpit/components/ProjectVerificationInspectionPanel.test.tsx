import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  PROJECT,
  executionFor,
  executionIdFor,
  inspectionFor,
  itemFor,
  notRecordedStream,
  pageFor,
  recordedStreamFor,
  refusedWindowFor,
  unavailableInspectionFor,
  unavailablePageFor,
  windowFor,
  emptyPageFor,
  FINGERPRINT,
  OTHER_FINGERPRINT,
  CHECKPOINT,
  WORKSPACE,
} from '../projectVerificationInspectionFixture'
import {
  PROJECT_VERIFICATION_EMPTY,
  PROJECT_VERIFICATION_EXECUTION_UNAVAILABLE,
  PROJECT_VERIFICATION_NOTE,
  PROJECT_VERIFICATION_PAGE_UNAVAILABLE,
} from '../projectVerificationInspectionCopy'
import { ProjectVerificationInspectionPanel } from './ProjectVerificationInspectionPanel'

// The real generated clients and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const HISTORY = `${BASE}/api/projects/${PROJECT}/verification-history`
const detailUrl = (number: number) => `${HISTORY}/${executionIdFor(number)}`
const windowUrl = (number: number, stream: 'stdout' | 'stderr', from = 0, max = 16384) =>
  `${detailUrl(number)}/artifact-window/${stream}?fromOffset=${from}&maxBytes=${max}`
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

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
const urls = (network: Network) => network.requests.map((request) => request.url)

const REGION = 'Inspect recorded verifications'
const openButton = () => screen.getByRole('button', { name: REGION })

async function open(network: Network, page: unknown = pageFor(45, 20)) {
  respond(network, HISTORY, page)
  render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
  fireEvent.click(openButton())
  await screen.findByText(/Recorded verifications #/)
}

describe('ProjectVerificationInspectionPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the newest page', async () => {
    const network = installNetwork()
    respond(network, HISTORY, pageFor(45, 20))
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)

    expect(screen.getByRole('region', { name: REGION })).toBeInTheDocument()
    expect(openButton()).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toEqual([])

    fireEvent.click(openButton())
    await screen.findByText('Recorded verifications #45 to #26.')

    expect(network.requests).toEqual([{ method: 'GET', url: HISTORY, body: undefined }])
    expect(screen.getByText(PROJECT_VERIFICATION_NOTE)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /^Inspect verification #/ })).toHaveLength(20)
    expect(screen.getByRole('button', { name: 'Older verifications' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload recorded verifications' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Hide recorded verifications' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('is disabled without a project and mounts no run, execution, recovery, approval or publication control', async () => {
    const network = installNetwork()
    const { unmount } = render(<ProjectVerificationInspectionPanel projectId={null} />)
    expect(openButton()).toBeDisabled()
    unmount()

    await open(network)
    const labels = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent ?? '')
    for (const label of labels) {
      expect(label).not.toMatch(/\b(run|retry|resume|release|approve|commit|publish|push|cancel|stop|delete|remove|export|download)\b/i)
    }

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(urls(network)).toEqual([HISTORY])
  })

  it('replaces the page by the older one with its cursor, never accumulating, and Reload clears the selection and reads the newest', async () => {
    const network = installNetwork()
    await open(network)
    respond(network, detailUrl(44), inspectionFor(44))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #44' }))
    await screen.findByText(/Claim fingerprint/)

    respond(network, `${HISTORY}?beforeExecutionNumber=26`, pageFor(25, 20))
    fireEvent.click(screen.getByRole('button', { name: 'Older verifications' }))
    await screen.findByText('Recorded verifications #25 to #6.')

    expect(urls(network)).toEqual([HISTORY, detailUrl(44), `${HISTORY}?beforeExecutionNumber=26`])
    expect(screen.queryByRole('button', { name: 'Inspect verification #45' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Inspect verification #44' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /^Inspect verification #/ })).toHaveLength(20)

    respond(network, detailUrl(25), inspectionFor(25))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #25' }))
    await screen.findByText(/Claim fingerprint/)

    fireEvent.click(screen.getByRole('button', { name: 'Reload recorded verifications' }))
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
    await screen.findByText('Recorded verifications #45 to #26.')
    expect(urls(network).at(-1)).toBe(HISTORY)
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
  })

  it('reads only the metadata of a selected execution and renders every recorded fact as plain text', async () => {
    const network = installNetwork()
    await open(network)
    respond(network, detailUrl(44), inspectionFor(44, {}, { checkpointNumber: 9, exitCode: 0 }))

    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #44' }))
    const detail = await screen.findByLabelText('Recorded verification #44')

    expect(urls(network)).toEqual([HISTORY, detailUrl(44)])
    expect(within(detail).getByText(/#44 · Check 44 \(recorded snapshot of the command\)/)).toBeInTheDocument()
    expect(within(detail).getByText(/Passed · process exited · exit code 0\./)).toBeInTheDocument()
    expect(within(detail).getByText(WORKSPACE)).toBeInTheDocument()
    expect(within(detail).getByText(`#9 (${CHECKPOINT})`)).toBeInTheDocument()
    expect(within(detail).getAllByText(FINGERPRINT)).toHaveLength(2)
    expect(within(detail).getByText('2026-10-20T09:00:44.0000000Z')).toBeInTheDocument()
    expect(within(detail).getByText('2026-10-20T09:30:01.0000000Z')).toBeInTheDocument()
    expect(within(detail).getByText('Standard output: 12 bytes · not truncated at capture')).toBeInTheDocument()
    expect(within(detail).getByText('Standard error: 5 bytes · truncated at capture')).toBeInTheDocument()
    expect(within(detail).queryByRole('button', { name: /^Load/ })).not.toBeInTheDocument()
    expect(urls(network).some((url) => url.includes('artifact-window'))).toBe(false)
  })

  it('shows a manual execution beyond the newest page without any run, agent or sequence attribution', async () => {
    const network = installNetwork()
    await open(network)
    respond(network, `${HISTORY}?beforeExecutionNumber=26`, pageFor(25, 20))
    fireEvent.click(screen.getByRole('button', { name: 'Older verifications' }))
    await screen.findByText('Recorded verifications #25 to #6.')
    respond(network, detailUrl(7), inspectionFor(7))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #7' }))
    const detail = await screen.findByLabelText('Recorded verification #7')

    expect(within(detail).getByText(/#7 · Check 7/)).toBeInTheDocument()
    expect(document.body.textContent).not.toMatch(/Agent attempt|local execution sequence|run #/i)
  })

  it('loads a stream only after an explicit stream choice and Load, and appends the next window', async () => {
    const network = installNetwork()
    await open(network)
    const total = 20_000
    const stream = recordedStreamFor(44, 'stdout', { byteLength: total })
    respond(network, detailUrl(44), inspectionFor(44, {}, { standardOutput: stream }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #44' }))
    await screen.findByText(/Claim fingerprint/)

    fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #44' }))
    expect(urls(network).some((url) => url.includes('artifact-window'))).toBe(false)
    expect(screen.getByRole('button', { name: 'Hide standard output of verification #44' })).toHaveAttribute('aria-pressed', 'true')

    respond(network, windowUrl(44, 'stdout'), { ...windowFor(44, 'stdout'), artifact: stream, text: 'a'.repeat(16384), nextOffset: 16384, totalLength: total })
    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #44' }))
    await screen.findByText('16384 of 20000 bytes read.')
    expect(screen.getByText('a'.repeat(16384))).toBeInTheDocument()

    respond(network, windowUrl(44, 'stdout', 16384, 16384), {
      ...windowFor(44, 'stdout'),
      artifact: stream,
      text: 'b'.repeat(3616),
      nextOffset: total,
      totalLength: total,
    })
    fireEvent.click(screen.getByRole('button', { name: 'Load next window' }))
    await screen.findByText(/20000 of 20000 bytes read\. This is the end of the recorded output\./)

    expect(screen.getByText('a'.repeat(16384) + 'b'.repeat(3616))).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load next window' })).not.toBeInTheDocument()
    expect(urls(network).filter((url) => url.includes('artifact-window'))).toEqual([
      windowUrl(44, 'stdout'),
      windowUrl(44, 'stdout', 16384, 16384),
    ])
  })

  it('stops at the 256 KiB source-byte view budget with a local limit, never an end of the output', async () => {
    const network = installNetwork()
    await open(network)
    const total = 300_000
    const stream = recordedStreamFor(44, 'stdout', { byteLength: total })
    respond(network, detailUrl(44), inspectionFor(44, {}, { standardOutput: stream }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #44' }))
    await screen.findByText(/Claim fingerprint/)
    fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #44' }))

    for (let from = 0; from < 262144; from += 16384) {
      network.routes.set(windowUrl(44, 'stdout', from), () =>
        Promise.resolve(json({ ...windowFor(44, 'stdout'), artifact: stream, text: 'x'.repeat(16384), nextOffset: from + 16384, totalLength: total })),
      )
    }

    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #44' }))
    for (let from = 16384; from < 262144; from += 16384) {
      fireEvent.click(await screen.findByRole('button', { name: 'Load next window' }))
      await screen.findByText(`${from + 16384} of ${total} bytes read.`)
    }

    expect(await screen.findByText(/at most 256 KiB of source bytes/)).toBeInTheDocument()
    expect(screen.getByText(/local view limit, not the end of the capture or of the file/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load next window' })).not.toBeInTheDocument()
    expect(screen.queryByText(/This is the end of the recorded output/)).not.toBeInTheDocument()
    expect(urls(network).filter((url) => url.includes('artifact-window'))).toHaveLength(16)
  })

  it('renders hostile host text only as literal text', async () => {
    const network = installNetwork()
    const hostileName = '<img src=x onerror=alert(1)> & <b>bold</b>'
    const page = { ...pageFor(3, 1), items: [itemFor(3, { commandName: hostileName })] }
    await open(network, page)
    const hostileStream = recordedStreamFor(3, 'stdout', { byteLength: 200 })
    respond(network, detailUrl(3), inspectionFor(3, {}, { commandName: hostileName, standardOutput: hostileStream }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #3' }))
    await screen.findByText(/Claim fingerprint/)
    fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #3' }))
    const hostileText = '<script>alert(1)</script>\n**not markdown** [link](http://example.invalid)'
    respond(network, windowUrl(3, 'stdout'), { ...windowFor(3, 'stdout'), artifact: hostileStream, text: hostileText, nextOffset: 200, totalLength: 200 })
    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #3' }))
    await screen.findByText(/not markdown/)

    expect(screen.getAllByText(new RegExp(hostileName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))).length).toBeGreaterThan(0)
    expect(document.querySelector('img')).toBeNull()
    expect(document.querySelector('script')).toBeNull()
    expect(document.querySelector('a')).toBeNull()
    expect(document.querySelector('b')).toBeNull()
    expect(document.querySelector('pre')?.textContent).toBe(hostileText)
  })

  it('keeps an empty history, an unavailable page and an unavailable execution distinct, each with its own fixed copy', async () => {
    const empty = installNetwork()
    respond(empty, HISTORY, emptyPageFor())
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    await screen.findByText(PROJECT_VERIFICATION_EMPTY)
    expect(screen.queryByRole('button', { name: 'Older verifications' })).not.toBeInTheDocument()
  })

  it('shows an unavailable page without any item or way to page, and an unavailable execution without any fact', async () => {
    const network = installNetwork()
    respond(network, HISTORY, unavailablePageFor())
    const { unmount } = render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    expect(await screen.findByText(PROJECT_VERIFICATION_PAGE_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Inspect verification #/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Older verifications' })).not.toBeInTheDocument()
    unmount()

    respond(network, HISTORY, pageFor(5, 3))
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    await screen.findByText(/Recorded verifications #5 to #3/)
    respond(network, detailUrl(4), unavailableInspectionFor(4))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #4' }))
    expect(await screen.findByText(PROJECT_VERIFICATION_EXECUTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^View / })).not.toBeInTheDocument()
  })

  it('shows a SourceChanged completion fingerprint, an interrupted record and a refused dispatch exactly as recorded', async () => {
    const network = installNetwork()
    await open(network, pageFor(9, 3))
    respond(network, detailUrl(9), inspectionFor(9, {}, { status: 'SourceChanged', outcome: 'Exited', exitCode: 0, completionFingerprintSha256: OTHER_FINGERPRINT }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #9' }))
    await screen.findByText(/Source changed · process exited · exit code 0\./)
    expect(screen.getByText(FINGERPRINT)).toBeInTheDocument()
    expect(screen.getByText(OTHER_FINGERPRINT)).toBeInTheDocument()

    respond(network, detailUrl(8), inspectionFor(8, {}, {
      status: 'LocalExecutionSequenceDispatchRefused',
      outcome: null,
      exitCode: null,
      completionFingerprintSha256: null,
      dispatchedAtUtc: null,
      standardOutput: notRecordedStream(),
      standardError: notRecordedStream(),
    }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #8' }))
    await screen.findByText(/Dispatch refused \(no process ran\) · no process outcome recorded · no exit code recorded\./)
    expect(screen.getAllByText('not recorded').length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Standard output: not recorded.')).toBeInTheDocument()
    expect(screen.queryByText(OTHER_FINGERPRINT)).not.toBeInTheDocument()
  })

  it('keeps unknown truncation and an empty output distinct', async () => {
    const network = installNetwork()
    await open(network, pageFor(9, 3))
    const recovered = recordedStreamFor(9, 'stdout', { byteLength: 0, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null })
    respond(network, detailUrl(9), inspectionFor(9, {}, { standardOutput: recovered }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #9' }))
    expect(await screen.findByText('Standard output: 0 bytes · truncation unknown (recovered after a host interruption)')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #9' }))
    respond(network, windowUrl(9, 'stdout'), { ...windowFor(9, 'stdout'), artifact: recovered, text: '', nextOffset: 0, totalLength: 0 })
    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #9' }))
    expect(await screen.findByText('This sealed output is empty (0 bytes).')).toBeInTheDocument()
  })

  it('keeps every refused window state and a read failure distinct', async () => {
    const expected: Record<string, RegExp> = {
      Unavailable: /recorded metadata or storage could not be admitted/,
      NotRecorded: /no longer recorded for this verification run/,
      Missing: /sealed file could not be found/,
      IntegrityMismatch: /does not match its recorded length and hash/,
    }
    for (const [status, pattern] of Object.entries(expected)) {
      const network = installNetwork()
      respond(network, HISTORY, pageFor(9, 3))
      const { unmount } = render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
      fireEvent.click(openButton())
      await screen.findByText(/Recorded verifications #9 to #7/)
      respond(network, detailUrl(9), inspectionFor(9))
      fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #9' }))
      await screen.findByText(/Claim fingerprint/)
      fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #9' }))
      respond(network, windowUrl(9, 'stdout'), refusedWindowFor(9, 'stdout', status as 'Unavailable'))
      fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #9' }))
      expect(await screen.findByText(pattern), status).toBeInTheDocument()
      unmount()
      vi.restoreAllMocks()
    }

    const network = installNetwork()
    respond(network, HISTORY, pageFor(9, 3))
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    await screen.findByText(/Recorded verifications #9 to #7/)
    respond(network, detailUrl(9), inspectionFor(9))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #9' }))
    await screen.findByText(/Claim fingerprint/)
    fireEvent.click(screen.getByRole('button', { name: 'View standard output of verification #9' }))
    network.routes.set(windowUrl(9, 'stdout'), () => Promise.resolve(json({ errors: [] }, 500)))
    fireEvent.click(screen.getByRole('button', { name: 'Load standard output of verification #9' }))
    expect(await screen.findByText('This output window could not be loaded.')).toBeInTheDocument()
    respond(network, windowUrl(9, 'stdout'), { ...windowFor(9, 'stdout'), totalLength: 2 })
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText(/not coherent for this request, so none of it is shown/)).toBeInTheDocument()
    expect(screen.queryByText('hello output')).not.toBeInTheDocument()
  })

  it('reports a page failure with an explicit retry that repeats only that read', async () => {
    const network = installNetwork()
    network.routes.set(HISTORY, () => Promise.resolve(json({ errors: [] }, 500)))
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    expect(await screen.findByRole('alert')).toHaveTextContent('The recorded verifications could not be loaded.')
    expect(screen.queryByRole('button', { name: 'Older verifications' })).not.toBeInTheDocument()

    respond(network, HISTORY, pageFor(5, 2))
    fireEvent.click(screen.getByRole('button', { name: 'Try the recorded verifications again' }))
    await screen.findByText('Recorded verifications #5 to #4.')
    expect(urls(network)).toEqual([HISTORY, HISTORY])
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('refuses an incoherent page, an execution that contradicts the listing and a 404, using none of it', async () => {
    const bad = installNetwork()
    respond(bad, HISTORY, { ...pageFor(5, 2), items: [itemFor(4), itemFor(5)] })
    const first = render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    expect(await screen.findByRole('alert')).toHaveTextContent(/not coherent for this request/)
    expect(screen.queryByRole('button', { name: /^Inspect verification #/ })).not.toBeInTheDocument()
    first.unmount()

    const network = installNetwork()
    respond(network, HISTORY, pageFor(5, 3))
    render(<ProjectVerificationInspectionPanel projectId={PROJECT} />)
    fireEvent.click(openButton())
    await screen.findByText(/Recorded verifications #5 to #3/)
    respond(network, detailUrl(4), inspectionFor(4, {}, { commandName: 'Another name' }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #4' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/not coherent for the verification you selected/)
    expect(screen.queryByText('Another name')).not.toBeInTheDocument()

    respond(network, detailUrl(3), { status: 404, errors: [{ code: 'project_verification_inspection.not_found' }] }, 404)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #3' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('This recorded verification is no longer available.')
  })

  it('returns to a cold, closed region after Hide and after a remount, reading again only on the next explicit open', async () => {
    const network = installNetwork()
    await open(network, pageFor(9, 3))
    respond(network, detailUrl(9), inspectionFor(9))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect verification #9' }))
    await screen.findByText(/Claim fingerprint/)

    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded verifications' }))
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Inspect verification #/ })).not.toBeInTheDocument()
    expect(openButton()).toHaveAttribute('aria-expanded', 'false')
    expect(urls(network)).toEqual([HISTORY, detailUrl(9)])

    fireEvent.click(openButton())
    await screen.findByText(/Recorded verifications #9 to #7/)
    expect(screen.queryByText(/Claim fingerprint/)).not.toBeInTheDocument()
    expect(urls(network)).toEqual([HISTORY, detailUrl(9), HISTORY])
  })

  it('discloses that a page and a window are separate reads and that a historical record proves nothing about the current source', async () => {
    const network = installNetwork()
    await open(network, pageFor(9, 3))
    const note = screen.getByText(PROJECT_VERIFICATION_NOTE)
    expect(note.textContent).toMatch(/not attributed to any run/)
    expect(note.textContent).toMatch(/do not prove that a source is current/)
    expect(note.textContent).toMatch(/not one consistent snapshot/)
    expect(executionFor(9).status).toBe('Passed')
    await waitFor(() => expect(urls(network)).toEqual([HISTORY]))
  })
})
