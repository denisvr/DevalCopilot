import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { collaborationHistoryClient, collaborationReplyClient } from '../../../api/clients'
import type { GetCollaborationHistoryReplyResponse, GetCollaborationHistoryResponse } from '../../../api/generated/api-client'
import { collaborationPageOf, entryFor, fieldsFor, replyFor, unavailableEntryFor } from '../collaborationHistoryFixture'
import { CollaborationHistoryPanel } from './CollaborationHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  collaborationHistoryClient: vi.fn(),
  collaborationReplyClient: vi.fn(),
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

type ReplyRead = (runId: string, messageId: string) => Promise<GetCollaborationHistoryReplyResponse>

function install(pages: (before: string | null | undefined) => Promise<GetCollaborationHistoryResponse>, reply: ReplyRead) {
  const getCollaborationHistory = vi.fn((_run: string, before: string | null | undefined) => pages(before))
  const getCollaborationHistoryReply = vi.fn(reply)
  vi.mocked(collaborationHistoryClient).mockReturnValue({ getCollaborationHistory } as unknown as ReturnType<typeof collaborationHistoryClient>)
  vi.mocked(collaborationReplyClient).mockReturnValue({ getCollaborationHistoryReply } as unknown as ReturnType<typeof collaborationReplyClient>)
  return { getCollaborationHistory, getCollaborationHistoryReply }
}

const region = () => screen.getByRole('region', { name: 'Recorded collaboration' })
const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show recorded collaboration' }))
const inspectors = () => within(region()).queryAllByRole('region', { name: 'Recorded reply' })
const inspectButtons = () => within(region()).queryAllByRole('button', { name: 'Inspect recorded reply' })
const rowOf = (sequence: number) => within(region()).getByText(`Sequence ${sequence}`).closest('li') as HTMLElement

// Sequences 30 and 20 are Acceptances whose recorded parents are Proposals; the older page holds sequence 10.
const accepted = (sequence: number) => entryFor(sequence, { type: 'Acceptance', fields: fieldsFor('Acceptance') })
const page = () => collaborationPageOf([], {
  entries: [entryFor(50), entryFor(40), accepted(30), accepted(20), entryFor(15)],
  hasMore: true,
  next: '15',
})
const olderPage = () => collaborationPageOf([], { entries: [entryFor(10)], hasMore: false })

const parentOf = (sequence: number, patch: Record<string, unknown> = {}) => entryFor(sequence, { type: 'Proposal', fields: fieldsFor('Proposal'), ...patch })
const answer = (sequence: number, parent = parentOf(sequence - 10)) =>
  replyFor({ id: `message-${sequence}`, sequence }, 'Available', parent)

async function opened(reply: ReplyRead) {
  const mocks = install((before) => Promise.resolve(before ? olderPage() : page()), reply)
  const { unmount } = render(<CollaborationHistoryPanel runId="run-1" />)
  open()
  await screen.findByText('Sequence 30')
  return { ...mocks, unmount }
}

describe('the recorded reply inspector', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('offers one explicit action per message and requests nothing until it is used', async () => {
    const { getCollaborationHistoryReply } = await opened(() => Promise.resolve(answer(30)))

    expect(inspectButtons()).toHaveLength(5)
    expect(inspectors()).toHaveLength(0)
    expect(getCollaborationHistoryReply).not.toHaveBeenCalled()
  })

  it('reads one source once and shows the parent metadata and admitted fields as exact plain text', async () => {
    const parent = parentOf(20, { summary: '<b>Parent summary</b> & more', fields: fieldsFor('Proposal').map((field) => ({ ...field, value: `<i>${field.name}</i>` })) })
    const errors = vi.spyOn(console, 'error')
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const { getCollaborationHistoryReply } = await opened(() => read.promise)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))

    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)
    expect(getCollaborationHistoryReply).toHaveBeenCalledWith('run-1', 'message-30')
    expect(within(rowOf(30)).getByText('Loading the recorded reply…')).toBeInTheDocument()
    expect(within(rowOf(30)).getByRole('region', { name: 'Recorded reply' })).toHaveTextContent('Recorded reply of sequence 30')
    expect(within(rowOf(20)).getByRole('button', { name: 'Inspect recorded reply' })).toBeInTheDocument()

    read.resolve(answer(30, parent))
    const inspector = await within(rowOf(30)).findByRole('region', { name: 'Recorded reply' })

    expect(inspector).toHaveTextContent('Recorded reply parent')
    expect(inspector).toHaveTextContent('Sequence 20')
    expect(inspector).toHaveTextContent('Proposal')
    expect(inspector).toHaveTextContent('Protocol 1.0')
    expect(inspector).toHaveTextContent('Provider-observed')
    expect(inspector).toHaveTextContent('Summary: <b>Parent summary</b> & more')
    expect(inspector.querySelector('b, i')).toBeNull()
    expect(inspector.querySelector('a, button.link')).toBeNull()
    expect(within(inspector).getAllByRole('term').map((node) => node.textContent)).toEqual([
      'Scope',
      'Implementation steps',
      'Risks',
      'Verification plan',
      'Escalation points',
    ])
    expect(within(inspector).getAllByRole('definition').map((node) => node.textContent)).toEqual([
      '<i>scope</i>',
      '<i>implementationSteps</i>',
      '<i>risks</i>',
      '<i>verificationPlan</i>',
      '<i>escalationPoints</i>',
    ])
    expect(inspector).toHaveTextContent('Message message-20')
    expect(within(inspector).queryByRole('button', { name: 'Inspect recorded reply' })).toBeNull()
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)
    // Neither the list nor the inspector emits a React warning (for example a duplicated key).
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('shows a header-only parent as an available relationship whose content is unavailable', async () => {
    await opened(() => Promise.resolve(answer(30, unavailableEntryFor(20, { type: 'Proposal' }))))

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    const inspector = await within(rowOf(30)).findByRole('region', { name: 'Recorded reply' })

    await waitFor(() => expect(inspector).toHaveTextContent('Recorded reply parent'))
    expect(inspector).toHaveTextContent('Content unavailable')
    expect(inspector).toHaveTextContent('Sequence 20')
    expect(within(inspector).queryAllByRole('term')).toHaveLength(0)
  })

  it('states a root proposal and an unavailable reference without naming any target', async () => {
    const mocks = install(
      () => Promise.resolve(collaborationPageOf([], { entries: [entryFor(30, { type: 'Proposal' }), entryFor(20, { type: 'Acceptance', fields: fieldsFor('Acceptance') })] })),
      (_run, id) => Promise.resolve(id === 'message-30' ? replyFor({ id, sequence: 30 }, 'None') : replyFor({ id, sequence: 20 }, 'Unavailable')),
    )
    render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await screen.findByText('Sequence 30')

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    expect(await within(rowOf(30)).findByText(/No reply parent is recorded/)).toBeInTheDocument()

    fireEvent.click(within(rowOf(20)).getByRole('button', { name: 'Inspect recorded reply' }))
    expect(await within(rowOf(20)).findByText(/Reply parent unavailable/)).toBeInTheDocument()
    expect(inspectors()).toHaveLength(1)
    expect(within(rowOf(30)).queryByText(/No reply parent is recorded/)).toBeNull()
    expect(mocks.getCollaborationHistoryReply).toHaveBeenCalledTimes(2)
  })

  it('keeps one inspector: another message replaces it and a late answer for the first shows nothing', async () => {
    const reads: Record<string, ReturnType<typeof deferred<GetCollaborationHistoryReplyResponse>>> = {
      'message-30': deferred(),
      'message-20': deferred(),
    }
    await opened((_run, id) => reads[id].promise)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    fireEvent.click(within(rowOf(20)).getByRole('button', { name: 'Inspect recorded reply' }))

    expect(inspectors()).toHaveLength(1)
    expect(within(rowOf(20)).getByRole('region', { name: 'Recorded reply' })).toHaveTextContent('Recorded reply of sequence 20')
    expect(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' })).toBeInTheDocument()

    reads['message-30'].resolve(answer(30))
    await waitFor(() => expect(within(rowOf(20)).getByText('Loading the recorded reply…')).toBeInTheDocument())
    expect(within(region()).queryByText('Recorded reply parent')).toBeNull()

    reads['message-20'].resolve(answer(20))
    expect(await within(rowOf(20)).findByText('Recorded reply parent')).toBeInTheDocument()
  })

  it('refuses a contradictory answer, shows none of it and retries the same source only on request', async () => {
    const answers = [
      () => Promise.resolve(replyFor({ id: 'message-30', sequence: 99 }, 'Available', parentOf(20, { summary: 'FORGED parent text' }))),
      () => Promise.reject(new Error('down')),
      () => Promise.resolve(answer(30)),
    ]
    const { getCollaborationHistoryReply } = await opened(() => answers[getCollaborationHistoryReply.mock.calls.length - 1]())

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    expect(await within(rowOf(30)).findByRole('alert')).toHaveTextContent('not coherent for this message')
    expect(region()).not.toHaveTextContent('FORGED')
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Try the recorded reply again' }))
    expect(await within(rowOf(30)).findByRole('alert')).toHaveTextContent('could not be read')
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(2)
    expect(getCollaborationHistoryReply).toHaveBeenLastCalledWith('run-1', 'message-30')

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Try the recorded reply again' }))
    expect(await within(rowOf(30)).findByText('Recorded reply parent')).toBeInTheDocument()
    expect(within(rowOf(30)).queryByRole('alert')).toBeNull()
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(3)
  })

  it('closes the inspector and restores the action, and a late answer after closing shows nothing', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const { getCollaborationHistoryReply } = await opened(() => read.promise)
    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Close recorded reply' }))
    read.resolve(answer(30))
    await Promise.resolve()

    expect(inspectors()).toHaveLength(0)
    expect(inspectButtons()).toHaveLength(5)
    expect(region()).not.toHaveTextContent('Recorded reply parent')
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(2)
  })

  it('leaves the loaded pages and cursor untouched while inspecting, and older paging keeps the inspector', async () => {
    const { getCollaborationHistory } = await opened(() => Promise.resolve(answer(30)))
    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    await within(rowOf(30)).findByText('Recorded reply parent')
    expect(getCollaborationHistory).toHaveBeenCalledTimes(1)

    fireEvent.click(within(region()).getByRole('button', { name: 'Load older messages' }))
    await screen.findByText('Sequence 10')

    expect(getCollaborationHistory).toHaveBeenCalledTimes(2)
    expect(getCollaborationHistory).toHaveBeenLastCalledWith('run-1', '15', 5)
    expect(inspectors()).toHaveLength(1)
    expect(within(rowOf(30)).getByText('Recorded reply parent')).toBeInTheDocument()
    expect(within(region()).queryByRole('button', { name: 'Load older messages' })).toBeNull()
  })

  it('ends the inspector when the history is reloaded or hidden, and a reopened history starts without one', async () => {
    const { getCollaborationHistoryReply } = await opened(() => Promise.resolve(answer(30)))
    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    await within(rowOf(30)).findByText('Recorded reply parent')

    fireEvent.click(within(region()).getByRole('button', { name: 'Reload newest messages' }))
    await screen.findByText('Sequence 30')
    expect(inspectors()).toHaveLength(0)
    expect(region()).not.toHaveTextContent('Recorded reply parent')

    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    await within(rowOf(30)).findByText('Recorded reply parent')
    fireEvent.click(screen.getByRole('button', { name: 'Hide recorded collaboration' }))
    open()
    await screen.findByText('Sequence 30')

    expect(inspectors()).toHaveLength(0)
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(2)
  })

  it('drops the inspector with its run: another run shows none of it and the first run does not return one', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    install((before) => Promise.resolve(before ? olderPage() : page()), () => read.promise)
    const { rerender } = render(<CollaborationHistoryPanel runId="run-1" />)
    open()
    await screen.findByText('Sequence 30')
    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))

    rerender(<CollaborationHistoryPanel runId="run-2" />)
    read.resolve(answer(30))
    await Promise.resolve()

    expect(screen.queryByRole('region', { name: 'Recorded reply' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Show recorded collaboration' })).toBeInTheDocument()
    rerender(<CollaborationHistoryPanel runId="run-1" />)
    expect(screen.queryByRole('region', { name: 'Recorded reply' })).toBeNull()
    expect(screen.queryByText('Recorded reply parent')).toBeNull()
  })

  it('exposes no follow, auto-read or mutation control and unmounts with a read pending without a warning', async () => {
    const read = deferred<GetCollaborationHistoryReplyResponse>()
    const { getCollaborationHistoryReply, unmount } = await opened(() => read.promise)
    fireEvent.click(within(rowOf(30)).getByRole('button', { name: 'Inspect recorded reply' }))
    const buttons = within(region()).getAllByRole('button').map((button) => button.textContent)
    expect([...new Set(buttons)].sort()).toEqual(
      ['Close recorded reply', 'Hide recorded collaboration', 'Inspect recorded reply', 'Load older messages', 'Reload newest messages'].sort(),
    )
    expect(region().querySelector('a')).toBeNull()

    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    unmount()
    read.resolve(answer(30))
    await Promise.resolve()

    expect(errors).not.toHaveBeenCalled()
    expect(getCollaborationHistoryReply).toHaveBeenCalledTimes(1)
    errors.mockRestore()
  })
})

