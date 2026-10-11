import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationHistoryClient } from '../../../api/clients'
import type { GetCollaborationHistoryResponse } from '../../../api/generated/api-client'
import {
  agentFor,
  collaborationPageOf,
  descendingSequences,
  entryFor,
  fieldsFor,
  participantFor,
  unavailableEntryFor,
} from '../collaborationHistoryFixture'
import { CollaborationHistoryPanel } from './CollaborationHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationHistoryClient: vi.fn(),
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

type Read = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetCollaborationHistoryResponse>

function install(read: Read) {
  const getCollaborationHistory = vi.fn(read)
  vi.mocked(collaborationHistoryClient).mockReturnValue({ getCollaborationHistory } as unknown as ReturnType<typeof collaborationHistoryClient>)
  return getCollaborationHistory
}

const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show recorded collaboration' }))
const region = () => screen.getByRole('region', { name: 'Recorded collaboration' })
const items = () => within(region()).queryAllByRole('listitem')

describe('CollaborationHistoryPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is collapsed by default and requests nothing until it is opened', () => {
    const get = install(() => Promise.resolve(collaborationPageOf([1])))
    render(<CollaborationHistoryPanel runId="run-1" />)

    expect(screen.getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByText(/not live status/i)).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with one request, states what the messages are not, and lists them newest first with fixed labels and plain text', async () => {
    const read = deferred<GetCollaborationHistoryResponse>()
    const get = install(() => read.promise)
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    expect(within(region()).getByText('Loading recorded collaboration…')).toBeInTheDocument()
    const note = within(region()).getByText(/lists the collaboration messages the host stored/)
    for (const sentence of [/recorded facts/i, /not live status/i, /not a current approval/i, /not proof that any provider succeeded/i, /newest first/i]) {
      expect(note).toHaveTextContent(sentence)
    }

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-1', null, 5)

    read.resolve(
      collaborationPageOf([], {
        entries: [
          entryFor('9007199254740993', { type: 'Question', fields: fieldsFor('Question'), summary: 'Which scope?' }),
          entryFor('40', {
            type: 'HumanInstruction',
            provenance: 'HumanSubmitted',
            actor: participantFor({ kind: 'Human' }),
            fields: fieldsFor('HumanInstruction'),
          }),
          entryFor('7', { provenance: 'Simulated', actor: participantFor({ kind: 'Agent', provider: 'ClaudeCode' }), recipient: agentFor() }),
        ],
      }),
    )

    await waitFor(() => expect(items()).toHaveLength(3))
    expect(items()[0]).toHaveTextContent('Sequence 9007199254740993')
    expect(items()[0]).toHaveTextContent('Question')
    expect(items()[0]).toHaveTextContent('Protocol 1.0')
    expect(items()[0]).toHaveTextContent('Provider-observed')
    expect(items()[0]).toHaveTextContent('2026-10-09T10:00:')
    expect(items()[0]).toHaveTextContent('From Agent · Planner role · Codex to Orchestrator')
    expect(items()[0]).toHaveTextContent('Summary: Which scope?')
    expect(within(items()[0]).getByText('Why it matters')).toBeInTheDocument()
    expect(within(items()[0]).getByText('question text')).toBeInTheDocument()
    expect(items()[0]).toHaveTextContent('Message message-9007199254740993')
    expect(items()[1]).toHaveTextContent('Human instruction')
    expect(items()[1]).toHaveTextContent('Human-submitted')
    expect(items()[1]).toHaveTextContent('From Human to Orchestrator')
    expect(items()[2]).toHaveTextContent('From Agent · role not recorded · Claude Code to Agent · Planner role · Codex')
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows an unavailable message by its header only and keeps its healthy neighbors', async () => {
    install(() =>
      Promise.resolve(
        collaborationPageOf([], {
          entries: [
            entryFor(9),
            unavailableEntryFor(8, {
              type: 'Unrecognized',
              provenance: 'Unrecognized',
              protocolVersion: 'Unrecognized',
              actor: participantFor({ kind: 'Unrecognized' }),
            }),
            entryFor(7),
          ],
        }),
      ),
    )
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    await waitFor(() => expect(items()).toHaveLength(3))
    expect(items()[0]).toHaveTextContent('Summary: Summary 9')
    expect(items()[2]).toHaveTextContent('Summary: Summary 7')
    expect(items()[1]).toHaveTextContent('Sequence 8')
    expect(items()[1]).toHaveTextContent('Unrecognized message type')
    expect(items()[1]).toHaveTextContent('Unrecognized provenance')
    expect(items()[1]).toHaveTextContent('Unrecognized protocol version')
    expect(items()[1]).toHaveTextContent('Unrecognized participant')
    expect(items()[1]).toHaveTextContent('Content unavailable')
    expect(items()[1]).not.toHaveTextContent('Summary:')
    expect(within(items()[1]).queryAllByRole('term')).toHaveLength(0)
    expect(within(region()).queryByRole('alert')).toBeNull()
  })

  it('shows an empty run explicitly and offers only the reload', async () => {
    install(() => Promise.resolve(collaborationPageOf([])))
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByText('No collaboration messages have been recorded for this run.')).toBeInTheDocument()
    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(within(region()).getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Hide recorded collaboration',
      'Reload newest messages',
    ])
  })

  it('offers no selection, reply, evidence or mutation control, only plain text rows', async () => {
    install(() => Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true })))
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(5))

    expect(within(region()).getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Hide recorded collaboration',
      'Reload newest messages',
      'Load older messages',
    ])
    expect(within(region()).queryAllByRole('textbox')).toHaveLength(0)
    expect(within(region()).queryAllByRole('link')).toHaveLength(0)
    expect(within(items()[0]).queryAllByRole('button')).toHaveLength(0)
  })

  it('renders hostile identities, summaries and field values strictly as text', async () => {
    const hostile = '<img src=x onerror=alert(1)>&lt;b&gt;<script>window.hacked=1</script>'
    install(() =>
      Promise.resolve(
        collaborationPageOf([], {
          entries: [
            entryFor(9, {
              id: hostile,
              summary: hostile,
              fields: fieldsFor('Proposal').map((field, index) => (index === 0 ? Object.assign(field, { value: hostile }) : field)),
            }),
          ],
        }),
      ),
    )
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByText(`Message ${hostile}`)).toBeInTheDocument()
    expect(within(region()).getByText(`Summary: ${hostile}`)).toBeInTheDocument()
    expect(within(region()).getByText(hostile)).toBeInTheDocument()
    expect(region().querySelector('img')).toBeNull()
    expect(region().querySelector('b')).toBeNull()
    expect(region().querySelector('script')).toBeNull()
    expect((window as unknown as { hacked?: number }).hacked).toBeUndefined()
  })

  it('shows a failed first page as an explicit error with no rows and reads again only on reload', async () => {
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.reject(new Error('SECRET-DETAIL')) : Promise.resolve(collaborationPageOf([4, 3]))))
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByRole('alert')).toHaveTextContent('The recorded collaboration could not be read.')
    expect(region()).not.toHaveTextContent('SECRET-DETAIL')
    expect(items()).toHaveLength(0)
    expect(within(region()).queryByText('No collaboration messages have been recorded for this run.')).toBeNull()
    expect(within(region()).queryByRole('button', { name: /older messages/i })).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest messages' }))
    await waitFor(() => expect(items()).toHaveLength(2))

    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
    expect(get).toHaveBeenLastCalledWith('run-1', null, 5)
  })

  it('refuses an incoherent page whole and explains it without showing any of it', async () => {
    install(() => Promise.resolve(collaborationPageOf([], { entries: [entryFor(9), entryFor(8, { summary: 'x'.repeat(601) })] })))
    render(<CollaborationHistoryPanel runId="run-1" />)

    open()

    expect(await within(region()).findByRole('alert')).toHaveTextContent('not coherent, so none of it is shown')
    expect(items()).toHaveLength(0)
    expect(region()).not.toHaveTextContent('Sequence 9')
  })

  it('loads older messages explicitly with the exact cursor, appends them, and the control disappears at the end', async () => {
    const older = deferred<GetCollaborationHistoryResponse>()
    const get = install((_run, before) =>
      before === null ? Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true })) : older.promise,
    )
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(5))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))

    expect(within(region()).getByText('Loading older messages…')).toBeInTheDocument()
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()
    expect(items()).toHaveLength(5)
    expect(get).toHaveBeenLastCalledWith('run-1', '26', 5)

    older.resolve(collaborationPageOf(descendingSequences(25, 21)))
    await waitFor(() => expect(items()).toHaveLength(10))

    expect(items()[5]).toHaveTextContent('Sequence 25')
    expect(items()[9]).toHaveTextContent('Sequence 21')
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('keeps the loaded messages after a failed older page and retries the same cursor on the explicit retry', async () => {
    let olderCalls = 0
    const get = install((_run, before) => {
      if (before === null) {
        return Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(collaborationPageOf(descendingSequences(25, 21)))
    })
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(5))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))

    expect(await within(region()).findByRole('alert')).toHaveTextContent('The older messages could not be read. The messages already shown are unchanged.')
    expect(items()).toHaveLength(5)
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()

    fireEvent.click(within(region()).getByRole('button', { name: 'Try the older messages again' }))
    await waitFor(() => expect(items()).toHaveLength(10))

    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', '26'])
  })

  it('refuses an incoherent older page without touching the loaded messages and offers the same retry', async () => {
    let olderCalls = 0
    install((_run, before) => {
      if (before === null) {
        return Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { hasMore: true }))
      }

      return Promise.resolve(++olderCalls === 1 ? collaborationPageOf([26, 24]) : collaborationPageOf([20]))
    })
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(5))

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))

    expect(await within(region()).findByRole('alert')).toHaveTextContent('older messages that are not coherent, so none of that page is shown')
    expect(items()).toHaveLength(5)
    fireEvent.click(within(region()).getByRole('button', { name: 'Try the older messages again' }))
    await waitFor(() => expect(items()).toHaveLength(6))
  })

  it('reloads the newest messages as a fresh lifetime that discards the earlier rows and errors', async () => {
    let reads = 0
    install((_run, before) => {
      if (before !== null) {
        return Promise.reject(new Error('offline'))
      }

      return Promise.resolve(++reads === 1 ? collaborationPageOf(descendingSequences(30, 26), { hasMore: true }) : collaborationPageOf([90, 89]))
    })
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(5))
    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))
    await within(region()).findByRole('alert')

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest messages' }))

    await waitFor(() => expect(items()).toHaveLength(2))
    expect(items()[0]).toHaveTextContent('Sequence 90')
    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(within(region()).queryByRole('button', { name: /older messages/i })).toBeNull()
  })

  it('collapses with the toggle, discards everything and reads a fresh newest page when reopened', async () => {
    let reads = 0
    const get = install(() => Promise.resolve(collaborationPageOf(++reads === 1 ? [5, 4] : [8, 7, 6])))
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await waitFor(() => expect(items()).toHaveLength(2))

    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded collaboration' }))

    expect(screen.queryByRole('list')).toBeNull()
    open()
    await waitFor(() => expect(items()).toHaveLength(3))
    expect(items()[0]).toHaveTextContent('Sequence 8')
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('has no run to read for a null run', () => {
    const get = install(() => Promise.resolve(collaborationPageOf([1])))
    render(<CollaborationHistoryPanel runId={null} />)

    open()

    expect(screen.getByRole('button', { name: 'Show recorded collaboration' })).toBeInTheDocument()
    expect(get).not.toHaveBeenCalled()
  })

  it('shows the messages of a replaced run as closed and empty and ignores the late answer of the previous run', async () => {
    const slowA = deferred<GetCollaborationHistoryResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(collaborationPageOf([7, 6], { runId }))))
    const { rerender } = render(<CollaborationHistoryPanel runId="run-a" />)
    open()
    expect(within(region()).getByText('Loading recorded collaboration…')).toBeInTheDocument()

    rerender(<CollaborationHistoryPanel runId="run-b" />)

    expect(screen.getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(items()).toHaveLength(0)
    slowA.resolve(collaborationPageOf([99, 98], { runId: 'run-a' }))
    await Promise.resolve()
    expect(items()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)

    open()
    await waitFor(() => expect(items()).toHaveLength(2))
    expect(get).toHaveBeenLastCalledWith('run-b', null, 5)
  })
})
