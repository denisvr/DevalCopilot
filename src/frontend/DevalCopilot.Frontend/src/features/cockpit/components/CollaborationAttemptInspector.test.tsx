import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationAttemptClient, collaborationHistoryClient, collaborationReplyClient } from '../../../api/clients'
import type {
  GetCollaborationHistoryAttemptResponse,
  GetCollaborationHistoryReplyResponse,
  GetCollaborationHistoryResponse,
} from '../../../api/generated/api-client'
import { agentFor, attemptAnswerFor, attemptHeaderFor, collaborationPageOf, entryFor, participantFor, replyFor } from '../collaborationHistoryFixture'
import { CollaborationHistoryPanel } from './CollaborationHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationHistoryClient: vi.fn(),
  collaborationReplyClient: vi.fn(),
  collaborationAttemptClient: vi.fn(),
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

type AttemptRead = (runId: string, messageId: string) => Promise<GetCollaborationHistoryAttemptResponse>

function install(
  pages: (before: string | null | undefined) => Promise<GetCollaborationHistoryResponse>,
  attempt: AttemptRead,
  reply: (runId: string, messageId: string) => Promise<GetCollaborationHistoryReplyResponse> = () => Promise.reject(new Error('unused')),
) {
  const getCollaborationHistory = vi.fn((_run: string, before: string | null | undefined) => pages(before))
  const getCollaborationHistoryAttempt = vi.fn(attempt)
  const getCollaborationHistoryReply = vi.fn(reply)
  vi.mocked(collaborationHistoryClient).mockReturnValue({ getCollaborationHistory } as unknown as ReturnType<typeof collaborationHistoryClient>)
  vi.mocked(collaborationAttemptClient).mockReturnValue({ getCollaborationHistoryAttempt } as unknown as ReturnType<typeof collaborationAttemptClient>)
  vi.mocked(collaborationReplyClient).mockReturnValue({ getCollaborationHistoryReply } as unknown as ReturnType<typeof collaborationReplyClient>)
  return { getCollaborationHistory, getCollaborationHistoryAttempt, getCollaborationHistoryReply }
}

const region = () => screen.getByRole('region', { name: 'Recorded collaboration' })
const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show recorded collaboration' }))
const attemptInspectors = () => within(region()).queryAllByRole('region', { name: 'Recorded attempt' })
const attemptButtons = () => within(region()).queryAllByRole('button', { name: 'Inspect recorded attempt' })
const rowOf = (sequence: number) => within(region()).getByText(`Sequence ${sequence}`).closest('li') as HTMLElement
const inspectAttempt = (sequence: number) =>
  fireEvent.click(within(rowOf(sequence)).getByRole('button', { name: 'Inspect recorded attempt' }))

// Every message is a provider-observed Planner Proposal from Codex (the fixture default), so a Planner/Codex/Proposal header agrees.
const page = () => collaborationPageOf([50, 40, 30, 20, 15], { hasMore: true })
const olderPage = () => collaborationPageOf([10])

const answer = (sequence: number, header = attemptHeaderFor({ id: `attempt-${sequence}` })) =>
  attemptAnswerFor({ id: `message-${sequence}`, sequence }, 'Available', header)

async function opened(attempt: AttemptRead, reply?: Parameters<typeof install>[2]) {
  const mocks = install((before) => Promise.resolve(before ? olderPage() : page()), attempt, reply)
  const { unmount } = render(<CollaborationHistoryPanel runId="run-1" />)
  open()
  await screen.findByText('Sequence 50')
  return { ...mocks, unmount }
}

describe('the recorded attempt inspector', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('offers one explicit action per message and requests nothing until it is used', async () => {
    const { getCollaborationHistoryAttempt } = await opened(() => Promise.resolve(answer(30)))

    expect(attemptButtons()).toHaveLength(5)
    expect(attemptInspectors()).toHaveLength(0)
    expect(getCollaborationHistoryAttempt).not.toHaveBeenCalled()
  })

  it('reads one source once and shows the minimal recorded header with fixed labels as plain text', async () => {
    const errors = vi.spyOn(console, 'error')
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const { getCollaborationHistoryAttempt } = await opened(() => read.promise)

    inspectAttempt(30)

    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledWith('run-1', 'message-30')
    expect(within(rowOf(30)).getByText('Loading the recorded attempt…')).toBeInTheDocument()
    expect(within(rowOf(30)).getByRole('region', { name: 'Recorded attempt' })).toHaveTextContent('Recorded attempt of sequence 30')
    expect(within(rowOf(40)).getByRole('button', { name: 'Inspect recorded attempt' })).toBeInTheDocument()

    read.resolve(answer(30, attemptHeaderFor({ id: '<b>attempt</b>-30', attemptNumber: 12, status: 'Interrupted', dispatchedAtUtc: undefined })))
    const inspector = await within(rowOf(30)).findByRole('region', { name: 'Recorded attempt' })

    await waitFor(() => expect(inspector).toHaveTextContent('Recorded attempt header'))
    expect(within(inspector).getAllByRole('term').map((node) => node.textContent)).toEqual([
      'Attempt',
      'Attempt number',
      'Role',
      'Provider',
      'Response contract',
      'Recorded status',
      'Claimed',
      'Dispatched',
      'Completed',
    ])
    expect(within(inspector).getAllByRole('definition').map((node) => node.textContent)).toEqual([
      '<b>attempt</b>-30',
      '12',
      'Planner',
      'Codex',
      'Proposal',
      'Interrupted',
      '2026-10-12T09:00:00.000Z',
      'Not recorded',
      '2026-10-12T09:07:08.000Z',
    ])
    expect(inspector.querySelector('b, a')).toBeNull()
    expect(inspector).toHaveTextContent('not current execution')
    expect(inspector).toHaveTextContent('not the sealed evidence')
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('states NoAgentAttempt and an unavailable attribution without naming any target', async () => {
    const host = entryFor(30, { provenance: 'HostConstructed', actor: participantFor(), recipient: agentFor() })
    install(
      () => Promise.resolve(collaborationPageOf([], { entries: [host, entryFor(20)] })),
      (_run, id) =>
        Promise.resolve(
          id === 'message-30' ? attemptAnswerFor({ id, sequence: '30' }, 'NoAgentAttempt') : attemptAnswerFor({ id, sequence: '20' }, 'Unavailable'),
        ),
    )
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await screen.findByText('Sequence 30')

    inspectAttempt(30)
    expect(await within(rowOf(30)).findByText(/No Agent attempt is attributed/)).toBeInTheDocument()
    expect(within(rowOf(30)).getByText(/does not say that no attempt exists in this run/)).toBeInTheDocument()

    inspectAttempt(20)
    expect(await within(rowOf(20)).findByText(/Attempt attribution unavailable/)).toBeInTheDocument()
    expect(attemptInspectors()).toHaveLength(1)
    expect(within(rowOf(30)).queryByText(/No Agent attempt is attributed/)).toBeNull()
  })

  it('keeps one inspector: another message replaces it and a late answer for the first shows nothing', async () => {
    const reads: Record<string, ReturnType<typeof deferred<GetCollaborationHistoryAttemptResponse>>> = {
      'message-30': deferred(),
      'message-20': deferred(),
    }
    await opened((_run, id) => reads[id].promise)

    inspectAttempt(30)
    inspectAttempt(20)

    expect(attemptInspectors()).toHaveLength(1)
    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded attempt' })).toHaveTextContent('Recorded attempt of sequence 20')

    reads['message-30'].resolve(answer(30))
    await waitFor(() => expect(within(rowOf(20)).getByText('Loading the recorded attempt…')).toBeInTheDocument())
    expect(within(region()).queryByText('Recorded attempt header')).toBeNull()

    reads['message-20'].resolve(answer(20))
    expect(await within(rowOf(20)).findByText('Recorded attempt header')).toBeInTheDocument()
  })

  it('refuses a contradictory answer, shows none of it and retries the same source only on request', async () => {
    const answers = [
      () => Promise.resolve(attemptAnswerFor({ id: 'message-30', sequence: 99 }, 'Available', attemptHeaderFor({ id: 'FORGED-attempt' }))),
      () => Promise.reject(new Error('down')),
      () => Promise.resolve(answer(30)),
    ]
    const { getCollaborationHistoryAttempt } = await opened(() => answers[getCollaborationHistoryAttempt.mock.calls.length - 1]())

    inspectAttempt(30)
    expect(await within(rowOf(30)).findByRole('alert')).toHaveTextContent('not coherent for this message')
    expect(region()).not.toHaveTextContent('FORGED')
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Try the recorded attempt again' }))
    expect(await within(rowOf(30)).findByRole('alert')).toHaveTextContent('could not be read')
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(2)
    expect(getCollaborationHistoryAttempt).toHaveBeenLastCalledWith('run-1', 'message-30')

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Try the recorded attempt again' }))
    expect(await within(rowOf(30)).findByText('Recorded attempt header')).toBeInTheDocument()
    expect(within(rowOf(30)).queryByRole('alert')).toBeNull()
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(3)
  })

  it('closes the inspector and restores the action, and a late answer after closing shows nothing', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const { getCollaborationHistoryAttempt } = await opened(() => read.promise)
    inspectAttempt(30)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Close recorded attempt' }))
    read.resolve(answer(30))
    await Promise.resolve()

    expect(attemptInspectors()).toHaveLength(0)
    expect(attemptButtons()).toHaveLength(5)
    expect(region()).not.toHaveTextContent('Recorded attempt header')
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)

    inspectAttempt(30)
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(2)
  })

  it('is independent of the reply inspector: both can be open, and closing one leaves the other', async () => {
    const { getCollaborationHistoryAttempt, getCollaborationHistoryReply } = await opened(
      () => Promise.resolve(answer(20)),
      (_run, id) => Promise.resolve(replyFor({ id, sequence: '20' }, 'Unavailable')),
    )

    inspectAttempt(20)
    fireEvent.click(within(rowOf(20)).getByRole('button', { name: 'Inspect recorded reply' }))
    await within(rowOf(20)).findByText('Recorded attempt header')
    await within(rowOf(20)).findByText(/Reply parent unavailable/)

    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded attempt' })).toBeInTheDocument()
    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded reply' })).toBeInTheDocument()
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)

    fireEvent.click(within(rowOf(20)).getByRole('button', { name: 'Close recorded attempt' }))
    expect(attemptInspectors()).toHaveLength(0)
    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded reply' })).toBeInTheDocument()

    inspectAttempt(30)
    expect(within(rowOf(30)).getByRole('region', { name: 'Recorded attempt' })).toBeInTheDocument()
    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded reply' })).toBeInTheDocument()
  })

  it('leaves the loaded pages and cursor untouched while inspecting, and older paging keeps the inspector', async () => {
    const { getCollaborationHistory } = await opened(() => Promise.resolve(answer(30)))
    inspectAttempt(30)
    await within(rowOf(30)).findByText('Recorded attempt header')
    expect(getCollaborationHistory).toHaveBeenCalledTimes(1)

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))
    await screen.findByText('Sequence 10')

    expect(getCollaborationHistory).toHaveBeenCalledTimes(2)
    expect(getCollaborationHistory).toHaveBeenLastCalledWith('run-1', '15', 5)
    expect(attemptInspectors()).toHaveLength(1)
    expect(within(rowOf(30)).getByText('Recorded attempt header')).toBeInTheDocument()
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()
  })

  it('ends the inspector when the history is reloaded or hidden, and a reopened history starts without one', async () => {
    const { getCollaborationHistoryAttempt } = await opened(() => Promise.resolve(answer(30)))
    inspectAttempt(30)
    await within(rowOf(30)).findByText('Recorded attempt header')

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest messages' }))
    await screen.findByText('Sequence 50')
    expect(attemptInspectors()).toHaveLength(0)

    inspectAttempt(30)
    await within(rowOf(30)).findByText('Recorded attempt header')
    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded collaboration' }))
    open()
    await screen.findByText('Sequence 50')

    expect(attemptInspectors()).toHaveLength(0)
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(2)
  })

  it('drops the inspector with its run: another run shows none of it and the first run does not return one', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    install((before) => Promise.resolve(before ? olderPage() : page()), () => read.promise)
    const { rerender } = render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await screen.findByText('Sequence 50')
    inspectAttempt(30)

    rerender(<CollaborationHistoryPanel runId="run-2" />)
    read.resolve(answer(30))
    await Promise.resolve()

    expect(screen.queryByRole('region', { name: 'Recorded attempt' })).toBeNull()
    rerender(<CollaborationHistoryPanel runId="run-1" />)
    expect(screen.queryByRole('region', { name: 'Recorded attempt' })).toBeNull()
    expect(screen.queryByText('Recorded attempt header')).toBeNull()
  })

  it('unmounts with a read pending without a warning', async () => {
    const read = deferred<GetCollaborationHistoryAttemptResponse>()
    const { getCollaborationHistoryAttempt, unmount } = await opened(() => read.promise)
    inspectAttempt(30)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    unmount()
    read.resolve(answer(30))
    await Promise.resolve()

    expect(errors).not.toHaveBeenCalled()
    expect(getCollaborationHistoryAttempt).toHaveBeenCalledTimes(1)
    errors.mockRestore()
  })
})
