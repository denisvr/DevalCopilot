// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  LOCAL_COMMIT_INSPECTION_COMMIT_NOTE,
  LOCAL_COMMIT_INSPECTION_FAILURES,
  LOCAL_COMMIT_INSPECTION_NOTE,
  LOCAL_COMMIT_INSPECTION_NOT_RECORDED,
  LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE,
  LOCAL_COMMIT_INSPECTION_NO_START,
  LOCAL_COMMIT_INSPECTION_PREPARATION_NOTE,
  LOCAL_COMMIT_INSPECTION_REASON_EXPLANATION,
  LOCAL_COMMIT_INSPECTION_REASON_UNRECOGNIZED,
  LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION,
  LOCAL_COMMIT_INSPECTION_UNAVAILABLE,
} from '../localCommitInspectionCopy'
import {
  LOCAL_COMMIT_OPERATION,
  LOCAL_COMMIT_OTHER_RUN,
  LOCAL_COMMIT_PARENT,
  LOCAL_COMMIT_PROJECT,
  LOCAL_COMMIT_RESULT,
  LOCAL_COMMIT_RUN,
  LOCAL_COMMIT_TREE,
  notRecordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionOf,
  unavailableLocalCommitInspectionFor,
} from '../localCommitInspectionFixture'
import { LocalCommitInspectionRegion } from './LocalCommitInspectionRegion'

// The real generated client and transport run against a fake network, so every request the region sends is counted exactly.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const inspectionUrl = (run: string) => `${BASE}/api/runs/${run}/local-commit-inspection`

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
const OPEN = { name: 'Inspect recorded local commit' }
const HIDE = { name: 'Hide recorded local commit' }
const RELOAD = { name: 'Reload recorded local commit' }

const renderRegion = (run = LOCAL_COMMIT_RUN) => render(<LocalCommitInspectionRegion runId={run} projectId={LOCAL_COMMIT_PROJECT} />)

describe('LocalCommitInspectionRegion', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is collapsed and sends nothing until the owner opens it, then reads only the recorded operation once', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor())
    renderRegion()

    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getAllByRole('button')).toHaveLength(1)
    expect(network.requests).toEqual([])

    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(network.requests).toEqual([{ method: 'GET', url: inspectionUrl(LOCAL_COMMIT_RUN) }])
    expect(screen.getByRole('button', HIDE)).toHaveAttribute('aria-expanded', 'true')
    for (const line of LOCAL_COMMIT_INSPECTION_NOTE) {
      expect(screen.getByText(line)).toBeInTheDocument()
    }
  })

  it('offers only the region toggle and the explicit reload: no request, retry, recovery, release, approval, commit, publication or export action', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('NeedsAttention'))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Hide recorded local commit', 'Reload recorded local commit'])
    expect(screen.queryByRole('button', { name: /request|retry|recover|release|approve|commit now|publish|export|resume/i })).toBeNull()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('shows a Completed operation with its recorded labels, result commit and preparation facts', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Completed'))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(screen.getByText('Completed')).toBeInTheDocument()
    expect(screen.getByText('Recorded result commit').nextSibling).toHaveTextContent(LOCAL_COMMIT_RESULT)
    expect(screen.getByText(`parent commit ${LOCAL_COMMIT_PARENT} · tree ${LOCAL_COMMIT_TREE}`)).toBeInTheDocument()
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.Completed)).toBeInTheDocument()
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_PREPARATION_NOTE)).toBeInTheDocument()
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_COMMIT_NOTE)).toBeInTheDocument()
    expect(screen.queryByText(LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE)).toBeNull()
    expect(screen.getByText('Recorded reason').nextSibling).toHaveTextContent('No reason was recorded.')
    expect(screen.getByText(/operation .*cccccccc/)).toBeInTheDocument()
    expect(screen.getByText(/3 changed paths · 1234 bytes/)).toBeInTheDocument()
    expect(screen.getByText(/#2 ·/)).toBeInTheDocument()
    expect(screen.getByText('devalcopilot/workspace/x/1')).toBeInTheDocument()
  })

  it.each([
    ['Prepared', 'Prepared'],
    ['Executing', 'Executing'],
    ['Failed', 'Failed'],
    ['Interrupted', 'Interrupted'],
    ['NeedsAttention', 'Needs attention'],
  ])('shows a %s operation with its fixed historical description and never discloses a commit identity', async (status, label) => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf(status))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(screen.getByText('Recorded status').nextSibling).toHaveTextContent(label)
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION[status as keyof typeof LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION])).toBeInTheDocument()
    expect(screen.getByText('Recorded result commit').nextSibling).toHaveTextContent('not disclosed')
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE)).toBeInTheDocument()
    expect(screen.queryByText(LOCAL_COMMIT_INSPECTION_COMMIT_NOTE)).toBeNull()
    expect(document.body.textContent).not.toContain(LOCAL_COMMIT_RESULT)
    expect(screen.getByText(`parent commit ${LOCAL_COMMIT_PARENT} · tree ${LOCAL_COMMIT_TREE}`)).toBeInTheDocument()
  })

  it('describes Prepared and Executing as markers and NeedsAttention as neither terminal, success, failure nor permission to retry', () => {
    expect(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.Prepared).toMatch(/recorded marker, not an observation/)
    expect(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.Executing).toMatch(/recorded marker, not an observation of a running process/)
    expect(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.NeedsAttention).toMatch(/not terminal, not a success, not a failure and not permission to retry/)
    expect(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.Completed).toMatch(/not a new delivery receipt, a live Git observation/)
    expect(LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION.Completed).toMatch(/remote publication/)
  })

  it('shows no start time and says so when an outcome was recorded without an execution marker, and invents none', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Failed', {}, { executionStartedAtUtc: null }))
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(screen.getByText(/execution marker not recorded/)).toBeInTheDocument()
    expect(screen.getByText(LOCAL_COMMIT_INSPECTION_NO_START)).toBeInTheDocument()
  })

  it('explains a known reason with its fixed text and an unrecognized reason without any raw code', async () => {
    const network = installNetwork()
    respond(
      network,
      inspectionUrl(LOCAL_COMMIT_RUN),
      recordedLocalCommitInspectionOf('NeedsAttention', {}, { reason: { kind: 'Known', code: 'local_commit.unknown_index_lock' } }),
    )
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')
    expect(screen.getByText('Recorded reason').nextSibling).toHaveTextContent(
      `local_commit.unknown_index_lock · ${LOCAL_COMMIT_INSPECTION_REASON_EXPLANATION['local_commit.unknown_index_lock']}`,
    )

    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Unrecognized', code: null } }))
    fireEvent.click(screen.getByRole('button', RELOAD))
    await waitFor(() => expect(screen.getByText('Recorded reason').nextSibling).toHaveTextContent(LOCAL_COMMIT_INSPECTION_REASON_UNRECOGNIZED))
    expect(view.container.textContent).not.toContain('local_commit.')
  })

  it('refuses a raw reason string, so arbitrary stored reason text can never render', async () => {
    const network = installNetwork()
    respond(
      network,
      inspectionUrl(LOCAL_COMMIT_RUN),
      recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Unrecognized', code: 'C:\\secret\\path <b>x</b>' } }),
    )
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(LOCAL_COMMIT_INSPECTION_FAILURES.invalid)

    expect(view.container.textContent).not.toContain('secret')
    expect(view.container.querySelector('b')).toBeNull()
  })

  it('renders every host value as inert text', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor({}, { branchName: '<img src=x onerror=alert(1)>' }))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    expect(view.container.querySelector('img')).toBeNull()
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeInTheDocument()
  })

  it('shows the fixed NotRecorded and Unavailable copy and no operation', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), notRecordedLocalCommitInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(LOCAL_COMMIT_INSPECTION_NOT_RECORDED).toMatch(/says nothing about whether this run is eligible/)
    expect(screen.queryByText('Recorded status')).toBeNull()

    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), unavailableLocalCommitInspectionFor())
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_UNAVAILABLE)).toBeInTheDocument()
    expect(screen.queryByText(LOCAL_COMMIT_INSPECTION_NOT_RECORDED)).toBeNull()
    expect(screen.queryByText('Recorded status')).toBeNull()
  })

  it('shows fixed failure copy and no partial operation for a failed, missing or inconsistent answer', async () => {
    const network = installNetwork()
    network.routes.set(inspectionUrl(LOCAL_COMMIT_RUN), () => Promise.reject(new Error('network down C:\\secret')))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_FAILURES.read)).toBeInTheDocument()
    expect(view.container.textContent).not.toContain('secret')

    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), { status: 404, errors: [{ code: 'local_commit_inspection.not_found' }], traceId: 't' }, 404)
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_FAILURES['not-found'])).toBeInTheDocument()

    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_OTHER_RUN }))
    fireEvent.click(screen.getByRole('button', RELOAD))
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.queryByText(LOCAL_COMMIT_OPERATION)).toBeNull()
  })

  it('refuses the whole answer for any incoherent member and shows none of it', async () => {
    const network = installNetwork()
    for (const body of [
      recordedLocalCommitInspectionFor({}, { status: 'Completed', commitSha: null }),
      recordedLocalCommitInspectionOf('Prepared', {}, { commitSha: LOCAL_COMMIT_RESULT }),
      recordedLocalCommitInspectionFor({}, { changedPathCount: 129 }),
      { ...recordedLocalCommitInspectionFor(), extra: 1 },
    ]) {
      respond(network, inspectionUrl(LOCAL_COMMIT_RUN), body)
      const view = renderRegion()
      fireEvent.click(view.getByRole('button', OPEN))
      expect(await view.findByText(LOCAL_COMMIT_INSPECTION_FAILURES.invalid)).toBeInTheDocument()
      expect(view.queryByText('Recorded status')).toBeNull()
      view.unmount()
    }
  })

  it('shows the pending line while the read is in flight and nothing else', async () => {
    const network = installNetwork()
    network.routes.set(inspectionUrl(LOCAL_COMMIT_RUN), () => new Promise<Response>(() => undefined))
    renderRegion()

    fireEvent.click(screen.getByRole('button', OPEN))

    expect(await screen.findByText(/Reading the recorded local commit of this run/)).toBeInTheDocument()
    expect(screen.queryByText('Recorded status')).toBeNull()
  })

  it('clears the old answer at once on reload, reads once more and shows only the new answer', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor())
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')
    let release!: () => void
    network.routes.set(
      inspectionUrl(LOCAL_COMMIT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(notRecordedLocalCommitInspectionFor())))),
    )

    fireEvent.click(screen.getByRole('button', RELOAD))

    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(await screen.findByText(/Reading the recorded local commit of this run/)).toBeInTheDocument()
    release()
    expect(await screen.findByText(LOCAL_COMMIT_INSPECTION_NOT_RECORDED)).toBeInTheDocument()
    expect(network.requests.map((request) => request.url)).toEqual([inspectionUrl(LOCAL_COMMIT_RUN), inspectionUrl(LOCAL_COMMIT_RUN)])
  })

  it('discards the answer on closing, ignores a late answer and reads again on the next opening', async () => {
    const network = installNetwork()
    let release!: () => void
    network.routes.set(
      inspectionUrl(LOCAL_COMMIT_RUN),
      () => new Promise<Response>((resolve) => (release = () => resolve(json(recordedLocalCommitInspectionFor())))),
    )
    renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText(/Reading the recorded local commit of this run/)

    fireEvent.click(screen.getByRole('button', HIDE))
    release()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor())
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')
    expect(network.requests).toHaveLength(2)
  })

  it('ends with the run: another run (and a return to the first) shows no answer and reads nothing until asked', async () => {
    const network = installNetwork()
    respond(network, inspectionUrl(LOCAL_COMMIT_RUN), recordedLocalCommitInspectionFor())
    respond(network, inspectionUrl(LOCAL_COMMIT_OTHER_RUN), notRecordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_OTHER_RUN }))
    const view = renderRegion()
    fireEvent.click(screen.getByRole('button', OPEN))
    await screen.findByText('Recorded status')

    view.rerender(<LocalCommitInspectionRegion runId={LOCAL_COMMIT_OTHER_RUN} projectId={LOCAL_COMMIT_PROJECT} />)
    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')

    view.rerender(<LocalCommitInspectionRegion runId={LOCAL_COMMIT_RUN} projectId={LOCAL_COMMIT_PROJECT} />)
    expect(screen.queryByText('Recorded status')).toBeNull()
    expect(screen.getByRole('button', OPEN)).toHaveAttribute('aria-expanded', 'false')
    expect(network.requests).toHaveLength(1)
  })
})
