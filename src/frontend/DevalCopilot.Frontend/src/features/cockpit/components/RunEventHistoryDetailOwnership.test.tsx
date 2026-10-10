import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localDeliveryReceiptClient, projectRunHistoryClient, runEventHistoryClient } from '../../../api/clients'
import type { GetRunEventHistoryResponse } from '../../../api/generated/api-client'
import { availableFor } from '../localDeliveryReceiptFixture'
import { deliveredEntryFor, entryFor, pageOf } from '../projectRunHistoryFixture'
import { descendingSequences, eventPageOf } from '../runEventHistoryFixture'
import { ProjectRunHistoryPanel } from './ProjectRunHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
  runEventHistoryClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type EventRead = (runId: string, before: string | null | undefined, limit: number | null | undefined) => Promise<GetRunEventHistoryResponse>

function installEvents(read: EventRead) {
  const getRunEventHistory = vi.fn(read)
  vi.mocked(runEventHistoryClient).mockReturnValue({ getRunEventHistory } as unknown as ReturnType<typeof runEventHistoryClient>)
  return getRunEventHistory
}

const pageFor = (runId: string, sequences: readonly number[], hasMore = false) => eventPageOf(sequences, { runId, hasMore })

const detail = () => screen.getByRole('region', { name: 'Selected run' })
const events = () => within(detail()).getByRole('region', { name: 'Recorded events' })
const rows = () => within(events()).queryAllByRole('listitem')
const inspect = (number: number) => fireEvent.click(screen.getByRole('button', { name: `Inspect run #${number}` }))
const showEvents = () => fireEvent.click(within(detail()).getByRole('button', { name: 'Show recorded events' }))

describe('the recorded events of a historical run detail', () => {
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
    const get = installEvents((runId) => Promise.resolve(pageFor(runId, [9, 8])))

    inspect(3)

    expect(events()).toBeInTheDocument()
    expect(within(events()).getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(get).not.toHaveBeenCalled()
    expect(await within(detail()).findByRole('region', { name: 'Local delivery receipt' })).toBeInTheDocument()
    expect(within(detail()).getByText('Objective 3')).toBeInTheDocument()
    expect(within(detail()).getByRole('button', { name: 'Close selected run' })).toBeInTheDocument()

    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-3', null, 10)
  })

  it('reads the events of exactly the selected run and shows a replaced selection closed and empty (A to B)', async () => {
    const slowA = deferred<GetRunEventHistoryResponse>()
    const get = installEvents((runId) => (runId === 'run-3' ? slowA.promise : Promise.resolve(pageFor(runId, [7, 6]))))
    inspect(3)
    showEvents()
    expect(within(events()).getByText('Loading recorded events…')).toBeInTheDocument()

    inspect(2)

    expect(within(events()).getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    slowA.resolve(pageFor('run-3', [99, 98]))
    await Promise.resolve()
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)

    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(get).toHaveBeenLastCalledWith('run-2', null, 10)
    expect(within(events()).queryByText(/Sequence 99/)).toBeNull()
  })

  it('starts a returned run (A to B to A) collapsed and fresh, never showing the earlier A rows, and ignores its stale read', async () => {
    const staleA = deferred<GetRunEventHistoryResponse>()
    let aReads = 0
    const get = installEvents((runId) => {
      if (runId === 'run-3') {
        return ++aReads === 1 ? staleA.promise : Promise.resolve(pageFor('run-3', [30, 29]))
      }

      return Promise.resolve(pageFor(runId, [7, 6]))
    })
    inspect(3)
    showEvents()
    inspect(2)
    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))

    inspect(3)

    expect(within(events()).getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    staleA.resolve(pageFor('run-3', [99, 98]))
    await Promise.resolve()
    expect(rows()).toHaveLength(0)
    expect(within(events()).queryByText('Loading recorded events…')).toBeNull()

    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))
    expect(rows()[0]).toHaveTextContent('Sequence 30')
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-3', 'run-2', 'run-3'])
  })

  it('discards the events when the selected run is closed and shows the same run collapsed and fresh when it is selected again', async () => {
    let reads = 0
    const get = installEvents((runId) => Promise.resolve(pageFor(runId, ++reads === 1 ? [5, 4] : [8, 7, 6])))
    inspect(3)
    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close selected run' }))
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    inspect(3)

    expect(within(events()).getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)
    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(3))
    expect(rows()[0]).toHaveTextContent('Sequence 8')
  })

  it('pages, retries and reloads inside the detail without touching the run history rows or the receipt', async () => {
    let olderCalls = 0
    const get = installEvents((runId, before) => {
      if (before === null) {
        return Promise.resolve(eventPageOf(descendingSequences(30, 21), { runId, hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(eventPageOf(descendingSequences(20, 11), { runId }))
    })
    inspect(3)
    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(10))

    fireEvent.click(within(events()).getByRole('button', { name: 'Load older events' }))
    expect(await within(events()).findByRole('alert')).toBeInTheDocument()
    fireEvent.click(within(events()).getByRole('button', { name: 'Try the older events again' }))
    await waitFor(() => expect(rows()).toHaveLength(20))

    fireEvent.click(within(events()).getByRole('button', { name: 'Reload newest events' }))
    await waitFor(() => expect(rows()).toHaveLength(10))

    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, '21', '21', null])
    expect(screen.getAllByRole('listitem').filter((item) => item.textContent?.startsWith('Run #'))).toHaveLength(3)
    expect(within(detail()).getByRole('region', { name: 'Local delivery receipt' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inspect run #3' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('ends the selection and its events with a history reload, starting the next selection closed', async () => {
    const get = installEvents((runId) => Promise.resolve(pageFor(runId, [5, 4])))
    inspect(3)
    showEvents()
    await waitFor(() => expect(rows()).toHaveLength(2))

    fireEvent.click(screen.getByRole('button', { name: 'Reload latest runs' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })

    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    inspect(3)
    expect(within(events()).getByRole('button', { name: 'Show recorded events' })).toHaveAttribute('aria-expanded', 'false')
    expect(rows()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)
  })
})
