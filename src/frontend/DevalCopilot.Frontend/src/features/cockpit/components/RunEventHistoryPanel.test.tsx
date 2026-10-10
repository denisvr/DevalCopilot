import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { runEventHistoryClient } from '../../../api/clients'
import type { GetRunEventHistoryResponse } from '../../../api/generated/api-client'
import { actorFor, descendingSequences, eventPageOf, headerFor } from '../runEventHistoryFixture'
import { RunEventHistoryPanel } from './RunEventHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  runEventHistoryClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

type Read = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetRunEventHistoryResponse>

function install(read: Read) {
  const getRunEventHistory = vi.fn(read)
  vi.mocked(runEventHistoryClient).mockReturnValue({ getRunEventHistory } as unknown as ReturnType<typeof runEventHistoryClient>)
  return getRunEventHistory
}

const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show recorded events' }))
const region = () => screen.getByRole('region', { name: 'Recorded events' })
const items = () => within(region()).queryAllByRole('listitem')

describe('RunEventHistoryPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is collapsed by default and requests nothing until it is opened', () => {
    const get = install(() => Promise.resolve(eventPageOf([1])))
    render(<RunEventHistoryPanel runId="run-1" />)

    expect(screen.getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByText(/not live status/i)).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with one request, states what the headers are not, and lists them newest first with fixed labels and exact text', async () => {
    const read = deferred<GetRunEventHistoryResponse>()
    const get = install(() => read.promise)
    render(<RunEventHistoryPanel runId="run-1" />)

    open()

    expect(screen.getByRole('button', { name: 'Hide recorded events' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(region()).getByText('Loading recorded events…')).toBeInTheDocument()
    const note = within(region()).getByText(/Only each header is shown/)
    for (const sentence of [/details of an event are omitted/i, /not live status/i, /not a current approval/i, /not a complete replay/i, /newest first/i]) {
      expect(note).toHaveTextContent(sentence)
    }

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', null, 10)

    read.resolve(
      eventPageOf([], {
        entries: [
          headerFor('9007199254740993', { eventType: 'run.completed', actor: actorFor({ kind: 'Human' }) }),
          headerFor('9007199254740992', { eventType: 'agent.attempt_completed', actor: actorFor({ kind: 'Agent', role: 'Planner', provider: 'Codex' }) }),
          headerFor('40', { eventType: 'Unrecognized', actor: actorFor({ kind: 'Unrecognized' }) }),
          headerFor('7', { actor: actorFor({ kind: 'Agent', provider: 'ClaudeCode' }) }),
        ],
      }),
    )

    await waitFor(() => expect(items()).toHaveLength(4))
    expect(items()[0]).toHaveTextContent('Sequence 9007199254740993')
    expect(items()[0]).toHaveTextContent('Run completed')
    expect(items()[0]).toHaveTextContent('Human')
    expect(items()[0]).toHaveTextContent('2026-10-09T10:00:')
    expect(items()[0]).toHaveTextContent('Event event-9007199254740993')
    expect(items()[1]).toHaveTextContent('Sequence 9007199254740992')
    expect(items()[1]).toHaveTextContent('Agent attempt completed')
    expect(items()[1]).toHaveTextContent('Agent · Planner role · Codex')
    expect(items()[2]).toHaveTextContent('Unrecognized event type')
    expect(items()[2]).toHaveTextContent('Unrecognized actor')
    expect(items()[3]).toHaveTextContent('Collaboration message recorded')
    expect(items()[3]).toHaveTextContent('Agent · role not recorded · Claude Code')
    expect(within(region()).queryByText('Loading recorded events…')).toBeNull()
    expect(within(region()).queryByRole('button', { name: 'Load older events' })).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows an empty run explicitly and offers only the reload', async () => {
    install(() => Promise.resolve(eventPageOf([])))
    render(<RunEventHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByText('No events have been recorded for this run.')).toBeInTheDocument()
    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(within(region()).getAllByRole('button').map((button) => button.textContent)).toEqual(['Hide recorded events', 'Reload newest events'])
  })

  it('offers no selected-event, payload, playback or live control, only plain text rows', async () => {
    install(() => Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true })))
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(10))

    expect(within(region()).getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Hide recorded events',
      'Reload newest events',
      'Load older events',
    ])
    expect(within(region()).queryAllByRole('textbox')).toHaveLength(0)
    expect(within(region()).queryAllByRole('link')).toHaveLength(0)
    expect(within(items()[0]).queryAllByRole('button')).toHaveLength(0)
  })

  it('renders hostile identities and text strictly as text', async () => {
    const hostile = '<img src=x onerror=alert(1)>&lt;b&gt;'
    install(() => Promise.resolve(eventPageOf([], { entries: [headerFor(9, { id: hostile })] })))
    render(<RunEventHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByText(`Event ${hostile}`)).toBeInTheDocument()
    expect(region().querySelector('img')).toBeNull()
    expect(region().querySelector('b')).toBeNull()
  })

  it('shows a failed first page as an explicit error with no rows and reads again only on reload', async () => {
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.reject(new Error('SECRET-DETAIL')) : Promise.resolve(eventPageOf([4, 3]))))
    render(<RunEventHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByRole('alert')).toHaveTextContent('The recorded events could not be read.')
    expect(region()).not.toHaveTextContent('SECRET-DETAIL')
    expect(items()).toHaveLength(0)
    expect(within(region()).queryByText('No events have been recorded for this run.')).toBeNull()
    expect(within(region()).queryByRole('button', { name: /older events/i })).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest events' }))
    await waitFor(() => expect(items()).toHaveLength(2))

    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', null, 10)
  })

  it('refuses an incoherent page whole and explains it without showing any of it', async () => {
    install(() => Promise.resolve(eventPageOf([], { entries: [headerFor(9), headerFor(9, { id: 'event-dup' })] })))
    render(<RunEventHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByRole('alert')).toHaveTextContent('not coherent, so none of them are shown')
    expect(items()).toHaveLength(0)
    expect(region()).not.toHaveTextContent('Sequence 9')
  })

  it('loads older events explicitly with the exact cursor, appends them, and the load-older control disappears at the end', async () => {
    const older = deferred<GetRunEventHistoryResponse>()
    const get = install((_run, before) =>
      before === null ? Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true })) : older.promise,
    )
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(10))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older events' }))

    expect(within(region()).getByText('Loading older events…')).toBeInTheDocument()
    expect(within(region()).queryByRole('button', { name: 'Load older events' })).toBeNull()
    expect(items()).toHaveLength(10)
    expect(get).toHaveBeenLastCalledWith('run-1', '21', 10)

    older.resolve(eventPageOf(descendingSequences(20, 11)))
    await waitFor(() => expect(items()).toHaveLength(20))

    expect(items()[10]).toHaveTextContent('Sequence 20')
    expect(items()[19]).toHaveTextContent('Sequence 11')
    expect(within(region()).queryByRole('button', { name: 'Load older events' })).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('keeps the loaded events after a failed older page and retries the same cursor on the explicit retry', async () => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(eventPageOf(descendingSequences(20, 11)))
    })
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(10))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older events' }))

    expect(await within(region()).findByRole('alert')).toHaveTextContent('The older events could not be read. The events already shown are unchanged.')
    expect(items()).toHaveLength(10)
    expect(within(region()).queryByRole('button', { name: 'Load older events' })).toBeNull()

    fireEvent.click(within(region()).getByRole('button', { name: 'Try the older events again' }))
    await waitFor(() => expect(items()).toHaveLength(20))

    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', '21'])
  })

  it('refuses an incoherent older page without touching the loaded events and offers the same retry', async () => {
    let olderCalls = 0
    install((_run, before) => {
      if (before === null) {
        return Promise.resolve(eventPageOf(descendingSequences(30, 21), { hasMore: true }))
      }

      return Promise.resolve(++olderCalls === 1 ? eventPageOf([25, 24]) : eventPageOf([20]))
    })
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(10))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older events' }))

    expect(await within(region()).findByRole('alert')).toHaveTextContent('older events that are not coherent, so none of that page is shown')
    expect(items()).toHaveLength(10)
    fireEvent.click(within(region()).getByRole('button', { name: 'Try the older events again' }))
    await waitFor(() => expect(items()).toHaveLength(11))
  })

  it('reloads the newest events as a fresh lifetime that discards the earlier rows and errors', async () => {
    let reads = 0
    install((_run, before) => {
      if (before !== null) {
        return Promise.reject(new Error('offline'))
      }

      return Promise.resolve(++reads === 1 ? eventPageOf(descendingSequences(30, 21), { hasMore: true }) : eventPageOf([90, 89]))
    })
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(10))
    fireEvent.click(within(region()).getByRole('button', { name: 'Load older events' }))
    await within(region()).findByRole('alert')

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest events' }))

    await waitFor(() => expect(items()).toHaveLength(2))
    expect(items()[0]).toHaveTextContent('Sequence 90')
    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(within(region()).queryByRole('button', { name: /older events/i })).toBeNull()
  })

  it('collapses with the toggle, discards everything and reads a fresh newest page when reopened', async () => {
    let reads = 0
    const get = install(() => Promise.resolve(eventPageOf(++reads === 1 ? [5, 4] : [8, 7, 6])))
    render(<RunEventHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(2))

    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded events' }))

    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    open()
    await waitFor(() => expect(items()).toHaveLength(3))
    expect(items()[0]).toHaveTextContent('Sequence 8')
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('has no run to read for a null run', () => {
    const get = install(() => Promise.resolve(eventPageOf([1])))
    render(<RunEventHistoryPanel runId={null} />)

    open()

    expect(screen.getByRole('button', { name: 'Show recorded events' })).toBeInTheDocument()
    expect(get).not.toHaveBeenCalled()
  })

  it('shows the events of a replaced run as closed and empty and ignores the late answer of the previous run', async () => {
    const slowA = deferred<GetRunEventHistoryResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(eventPageOf([7, 6], { runId }))))
    const { rerender } = render(<RunEventHistoryPanel runId="run-a" />)
    open()
    expect(within(region()).getByText('Loading recorded events…')).toBeInTheDocument()

    rerender(<RunEventHistoryPanel runId="run-b" />)

    expect(screen.getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(items()).toHaveLength(0)
    slowA.resolve(eventPageOf([99, 98], { runId: 'run-a' }))
    await Promise.resolve()
    expect(items()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)

    open()
    await waitFor(() => expect(items()).toHaveLength(2))
    expect(get).toHaveBeenLastCalledWith('run-b', null, 10)
  })
})
