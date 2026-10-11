import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localDeliveryReceiptClient, projectRunHistoryClient, collaborationHistoryClient } from '../../../api/clients'
import type { GetCollaborationHistoryResponse } from '../../../api/generated/api-client'
import { availableFor } from '../localDeliveryReceiptFixture'
import { deliveredEntryFor, entryFor, pageOf } from '../projectRunHistoryFixture'
import { descendingSequences, collaborationPageOf } from '../collaborationHistoryFixture'
import { ProjectRunHistoryPanel } from './ProjectRunHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
  collaborationHistoryClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type MessageRead = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetCollaborationHistoryResponse>

function installMessages(read: MessageRead) {
  const getCollaborationHistory = vi.fn(read)
  vi.mocked(collaborationHistoryClient).mockReturnValue({ getCollaborationHistory } as unknown as ReturnType<typeof collaborationHistoryClient>)
  return getCollaborationHistory
}

const pageFor = (runId: string, sequences: readonly number[], hasMore = false) => collaborationPageOf(sequences, { runId, hasMore })

const detail = () => screen.getByRole('region', { name: 'Selected run' })
const messages = () => within(detail()).getByRole('region', { name: 'Recorded collaboration' })
const rows = () => within(messages()).queryAllByRole('listitem')
const inspect = (number: number) => fireEvent.click(screen.getByRole('button', { name: `Inspect run #${number}` }))
const showMessages = () => fireEvent.click(within(detail()).getByRole('button', { name: 'Show recorded collaboration' }))

describe('the recorded collaboration of a historical run detail', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    const getProjectRunHistory = vi.fn(() =>
      Promise.resolve(pageOf([], { entries: [deliveredEntryFor(3), entryFor(2), entryFor(1)] })),
    )
    vi.mocked(projectRunHistoryClient).mockReturnValue({ getProjectRunHistory } as unknown as ReturnType<typeof projectRunHistoryClient>)
    const getLocalDeliveryReceipt = vi.fn((runId: string) =>
      Promise.resolve(
        availableFor({ runId, operationId: 'operation-3', commitSha: 'c'.repeat(40), checkpointId: 'checkpoint-3', checkpointNumber: 3 }),
      ),
    )
    vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })
  })

  it('is collapsed inside the selected run, requests nothing until opened, and keeps the receipt and metadata beside it', async () => {
    const get = installMessages((runId) => Promise.resolve(pageFor(runId, [9, 8])))

    inspect(3)

    expect(messages()).toBeInTheDocument()
    expect(within(messages()).getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(get).not.toHaveBeenCalled()
    expect(await within(detail()).findByRole('region', { name: 'Local delivery receipt' })).toBeInTheDocument()
    expect(within(detail()).getByText('Objective 3')).toBeInTheDocument()
    expect(within(detail()).getByRole('button', { name: 'Close selected run' })).toBeInTheDocument()

    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-3', null, 5)
  })

  it('reads the messages of exactly the selected run and shows a replaced selection closed and empty (A to B)', async () => {
    const slowA = deferred<GetCollaborationHistoryResponse>()
    const get = installMessages((runId) => (runId === 'run-3' ? slowA.promise : Promise.resolve(pageFor(runId, [7, 6]))))
    inspect(3)
    showMessages()
    expect(within(messages()).getByText('Loading recorded collaboration…')).toBeInTheDocument()

    inspect(2)

    expect(within(messages()).getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    slowA.resolve(pageFor('run-3', [99, 98]))
    await Promise.resolve()
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)

    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(get).toHaveBeenLastCalledWith('run-2', null, 5)
    expect(within(messages()).queryByText(/Sequence 99/)).toBeNull()
  })

  it('starts a returned run (A to B to A) collapsed and fresh, never showing the earlier A rows, and ignores its stale read', async () => {
    const staleA = deferred<GetCollaborationHistoryResponse>()
    let aReads = 0
    const get = installMessages((runId) => {
      if (runId === 'run-3') {
        return ++aReads === 1 ? staleA.promise : Promise.resolve(pageFor('run-3', [30, 29]))
      }

      return Promise.resolve(pageFor(runId, [7, 6]))
    })
    inspect(3)
    showMessages()
    inspect(2)
    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))

    inspect(3)

    expect(within(messages()).getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    staleA.resolve(pageFor('run-3', [99, 98]))
    await Promise.resolve()
    expect(rows()).toHaveLength(0)
    expect(within(messages()).queryByText('Loading recorded collaboration…')).toBeNull()

    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(rows()[0]).toHaveTextContent('Sequence 30')
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-3', 'run-2', 'run-3'])
  })

  it('discards the messages when the selected run is closed and shows the same run collapsed and fresh when it is selected again', async () => {
    let reads = 0
    const get = installMessages((runId) => Promise.resolve(pageFor(runId, ++reads === 1 ? [5, 4] : [8, 7, 6])))
    inspect(3)
    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close selected run' }))
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    inspect(3)

    expect(within(messages()).getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)
    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(3))
    expect(rows()[0]).toHaveTextContent('Sequence 8')
  })

  it('pages, retries and reloads inside the detail without touching the run history rows or the receipt', async () => {
    let olderCalls = 0
    const get = installMessages((runId, before) => {
      if (before === null) {
        return Promise.resolve(collaborationPageOf(descendingSequences(30, 26), { runId, hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(collaborationPageOf(descendingSequences(25, 21), { runId }))
    })
    inspect(3)
    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(5))

    fireEvent.click(within(messages()).getByRole('button', { name: 'Load older messages' }))
    expect(await within(messages()).findByRole('alert')).toBeInTheDocument()
    fireEvent.click(within(messages()).getByRole('button', { name: 'Try the older messages again' }))
    await waitFor(() => expect(rows()).toHaveLength(10))

    fireEvent.click(within(messages()).getByRole('button', { name: 'Reload newest messages' }))
    await waitFor(() => expect(rows()).toHaveLength(5))

    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '26', '26', null])
    expect(screen.getAllByRole('listitem').filter((item) => item.textContent?.startsWith('Run #'))).toHaveLength(3)
    expect(within(detail()).getByRole('region', { name: 'Local delivery receipt' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inspect run #3' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('ends the selection and its messages with a history reload, starting the next selection closed', async () => {
    const get = installMessages((runId) => Promise.resolve(pageFor(runId, [5, 4])))
    inspect(3)
    showMessages()
    await waitFor(() => expect(rows()).toHaveLength(2))

    fireEvent.click(screen.getByRole('button', { name: 'Reload latest runs' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })

    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    inspect(3)
    expect(within(messages()).getByRole('button', { name: 'Show recorded collaboration' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)
  })
})
