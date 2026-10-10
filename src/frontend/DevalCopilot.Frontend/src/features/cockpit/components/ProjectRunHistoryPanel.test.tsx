import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localDeliveryReceiptClient, projectRunHistoryClient } from '../../../api/clients'
import type { GetLocalDeliveryReceiptResponse, GetProjectRunHistoryResponse } from '../../../api/generated/api-client'
import { availableFor, stateOnly } from '../localDeliveryReceiptFixture'
import type { ReceiptSource } from '../localDeliveryReceipt'
import { deliveredEntryFor, descending, entryFor, pageOf, sourceResponseFor } from '../projectRunHistoryFixture'
import { ProjectRunHistoryPanel } from './ProjectRunHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
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

type HistoryRead = (projectId: string, before: number | null | undefined, limit: number | null | undefined) => Promise<GetProjectRunHistoryResponse>

function installHistory(read: HistoryRead) {
  const getProjectRunHistory = vi.fn(read)
  vi.mocked(projectRunHistoryClient).mockReturnValue({ getProjectRunHistory } as unknown as ReturnType<typeof projectRunHistoryClient>)
  return getProjectRunHistory
}

const sourceOf = (executionNumber: number): ReceiptSource => ({
  runId: `run-${executionNumber}`,
  operationId: `operation-${executionNumber}`,
  commitSha: 'c'.repeat(40),
  checkpointId: `checkpoint-${executionNumber}`,
  checkpointNumber: 3,
})

function installReceipts(read: (runId: string) => Promise<GetLocalDeliveryReceiptResponse> = (runId) =>
  Promise.resolve(availableFor(sourceOf(Number(runId.replace('run-', '')))))) {
  const getLocalDeliveryReceipt = vi.fn(read)
  vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
  return getLocalDeliveryReceipt
}

const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
const region = () => screen.getByRole('region', { name: 'Run history' })

describe('ProjectRunHistoryPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installReceipts()
  })

  it('is collapsed by default and requests nothing until it is opened', () => {
    const get = installHistory(() => Promise.resolve(pageOf([1])))
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    const toggle = screen.getByRole('button', { name: 'Show run history' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByText(/not live progress/i)).toBeNull()
    expect(get).not.toHaveBeenCalled()
  })

  it('opens with one request, says plainly that it is a recorded snapshot, and lists the rows newest first', async () => {
    const read = deferred<GetProjectRunHistoryResponse>()
    const get = installHistory(() => read.promise)
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    open()

    expect(screen.getByRole('button', { name: 'Hide run history' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(region()).getByText('Loading run history…')).toBeInTheDocument()
    expect(within(region()).getByText(/not live progress/i)).toBeInTheDocument()
    expect(within(region()).getByText(/current workspace/i)).toBeInTheDocument()
    expect(within(region()).getByText(/remote publication/i)).toBeInTheDocument()
    expect(get).toHaveBeenCalledTimes(1)

    await act(async () => read.resolve(pageOf([], { entries: [entryFor(3, { lifecycle: 'Running' }), deliveredEntryFor(2), entryFor(1, { executionMode: 'Simulated' })] })))

    const items = within(screen.getByRole('list')).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('Run #3')
    expect(items[0]).toHaveTextContent('Objective 3')
    expect(items[0]).toHaveTextContent('Running')
    expect(items[1]).toHaveTextContent('Completed')
    expect(items[2]).toHaveTextContent('Simulated demo run')
    expect(screen.queryByText('Loading run history…')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Load older runs' })).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('says so for a project with no recorded run', async () => {
    installHistory(() => Promise.resolve(pageOf([])))
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    open()

    expect(await screen.findByText('No runs have been recorded for this project.')).toBeInTheDocument()
    expect(screen.queryByRole('list')).toBeNull()
  })

  it('discloses a row whose lifecycle, stage or mode this version does not recognize instead of hiding or guessing it', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [entryFor(2, { lifecycle: 'Unrecognized', stage: 'Unrecognized', executionMode: 'Unrecognized' }), entryFor(1)] })))
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    open()

    const items = await screen.findAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Unrecognized lifecycle')
    expect(items[0]).toHaveTextContent('Unrecognized stage')
    expect(items[0]).toHaveTextContent('Unrecognized execution mode')
    expect(items[1]).toHaveTextContent('Manual Agent run')
  })

  it('shows an explicit read failure with an explicit reload, and no automatic retry', async () => {
    let calls = 0
    const get = installHistory(() => (++calls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(pageOf([2, 1]))))
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    open()

    expect(await screen.findByRole('alert')).toHaveTextContent('The run history could not be read.')
    expect(get).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'Reload latest runs' }))

    expect(await screen.findByText('Objective 2')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('shows an incoherent page as an explicit error and none of its rows', async () => {
    installHistory(() => Promise.resolve(pageOf([3, 2], { projectId: 'project-2' })))
    render(<ProjectRunHistoryPanel projectId="project-1" />)

    open()

    expect(await screen.findByRole('alert')).toHaveTextContent('not coherent')
    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByText('Objective 3')).toBeNull()
  })

  it('pages older runs only on request and says when an older page failed, keeping the loaded rows and retrying the same cursor', async () => {
    let olderCalls = 0
    const get = installHistory((_project, before) => {
      if (before === null) {
        return Promise.resolve(pageOf(descending(12, 3), { hasMore: true }))
      }

      return ++olderCalls === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(pageOf(descending(2, 1)))
    })
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    expect(await screen.findByText('Objective 12')).toBeInTheDocument()
    expect(get).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'Load older runs' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The older runs could not be read.')
    expect(screen.getAllByRole('listitem')).toHaveLength(10)
    fireEvent.click(screen.getByRole('button', { name: 'Try the older runs again' }))

    expect(await screen.findByText('Objective 1')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(12)
    expect(screen.queryByRole('button', { name: 'Load older runs' })).toBeNull()
    expect(get.mock.calls.map(([, before]) => before)).toEqual([null, 3, 3])
  })

  it('collapses the region and discards every row and selection, and reopening reads a first page again', async () => {
    const get = installHistory(() => Promise.resolve(pageOf([3, 2])))
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))
    expect(screen.getByRole('region', { name: 'Selected run' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Hide run history' }))

    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    open()
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    expect(await screen.findByText('Objective 3')).toBeInTheDocument()
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('shows the recorded metadata of a selected run and, for a located source, the existing receipt region read once', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [entryFor(3), deliveredEntryFor(2)] })))
    const receipts = installReceipts()
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #2' }))

    const detail = screen.getByRole('region', { name: 'Selected run' })
    expect(within(detail).getByText('run-2')).toBeInTheDocument()
    expect(within(detail).getByText('Objective 2')).toBeInTheDocument()
    expect(within(detail).getByText(/recorded when the run was last advanced/i)).toBeInTheDocument()
    expect(within(detail).getAllByText('c'.repeat(40), { exact: false }).length).toBeGreaterThan(0)
    const receipt = await within(detail).findByRole('region', { name: 'Local delivery receipt' })
    expect(await within(receipt).findByText(/Backend tests 1/)).toBeInTheDocument()
    expect(receipts).toHaveBeenCalledTimes(1)
    expect(receipts).toHaveBeenCalledWith('run-2')
  })

  it('does not infer anything from a run without a located source and reads no receipt for it', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [entryFor(3, { lifecycle: 'Completed', stage: 'Completed' })] })))
    const receipts = installReceipts()
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))

    const detail = screen.getByRole('region', { name: 'Selected run' })
    expect(within(detail).getByText(/No completed delivery source is available in this view/)).toBeInTheDocument()
    expect(within(detail).getByText(/does not show that no local commit operation exists, that a delivery failed, or that one succeeded/)).toBeInTheDocument()
    expect(within(detail).queryByRole('region', { name: 'Local delivery receipt' })).toBeNull()
    expect(receipts).not.toHaveBeenCalled()
  })

  it('preserves the receipt component states: unavailable, refused and failed reads with an explicit retry', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(4), deliveredEntryFor(3), deliveredEntryFor(2)] })))
    let failedReads = 0
    const receipts = installReceipts((runId) => {
      if (runId === 'run-4') {
        return Promise.resolve(stateOnly('Unavailable'))
      }

      if (runId === 'run-3') {
        return Promise.resolve(availableFor({ ...sourceOf(3), commitSha: 'd'.repeat(40) }))
      }

      return ++failedReads === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(availableFor(sourceOf(2)))
    })
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #4' }))
    expect(await screen.findByText(/cannot be reconstructed from the evidence recorded/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #3' }))
    expect(await screen.findByText(/does not match this operation/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #2' }))
    expect(await screen.findByText(/could not be read/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Read the receipt again' }))
    expect(await screen.findByText(/Backend tests 1/)).toBeInTheDocument()
    expect(receipts.mock.calls.map(([runId]) => runId)).toEqual(['run-4', 'run-3', 'run-2', 'run-2'])
  })

  it('never shows a previous row in the first frame of a new selection and ignores the late receipt of the old one', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(4), deliveredEntryFor(3)] })))
    const slow = deferred<GetLocalDeliveryReceiptResponse>()
    installReceipts((runId) => (runId === 'run-4' ? slow.promise : Promise.resolve(availableFor(sourceOf(3)))))
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #4' }))

    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #3' }))

    const detail = screen.getByRole('region', { name: 'Selected run' })
    expect(within(detail).getByText('run-3')).toBeInTheDocument()
    expect(within(detail).queryByText('run-4')).toBeNull()
    await act(async () => slow.resolve(availableFor(sourceOf(4))))
    await waitFor(() => expect(within(detail).getByText(/Backend tests 1/)).toBeInTheDocument())
    expect(within(detail).queryByText(/operation-4/)).toBeNull()
  })

  it('closes the selected run without touching the list and reads again when the same run is chosen again', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(3)] })))
    const receipts = installReceipts()
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))
    expect(await screen.findByText(/Backend tests 1/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close selected run' }))

    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    expect(screen.getByText('Objective 3')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #3' }))
    expect(await screen.findByText(/Backend tests 1/)).toBeInTheDocument()
    expect(receipts).toHaveBeenCalledTimes(2)
  })

  it('shows a replaced project collapsed and empty in its first frame, never the previous project rows or selection', async () => {
    installHistory((projectId) => Promise.resolve(pageOf([], { projectId, entries: [entryFor(3)] })))
    const { rerender } = render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))

    rerender(<ProjectRunHistoryPanel projectId="project-2" />)

    expect(screen.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Objective 3')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
  })

  it('renders every recorded value as plain text, never as markup or a link', async () => {
    const hostile = '<img src=x onerror=alert(1)><a href="https://example.invalid">click</a>'
    installHistory(() => Promise.resolve(pageOf([], { entries: [entryFor(3, { objective: hostile })] })))
    const { container } = render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))

    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('a')).toBeNull()
    expect(screen.getAllByText(hostile).length).toBeGreaterThan(0)
  })

  it('offers no mutation, configuration or live-progress control and never polls', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const get = installHistory(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(3), entryFor(2)] })))
      const receipts = installReceipts()
      const { container } = render(<ProjectRunHistoryPanel projectId="project-1" />)
      open()
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #3' }))
      await screen.findByText(/Backend tests 1/)

      const names = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent)
      expect(names).toEqual([
        'Hide run history',
        'Reload latest runs',
        'Inspect run #3',
        'Inspect run #2',
        'Close selected run',
      ])
      expect(container.querySelector('form, input, textarea, select, [role="textbox"], [role="combobox"]')).toBeNull()
      expect(container.querySelector('[role="progressbar"], progress, time')).toBeNull()

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10 * 60 * 1000)
      })

      expect(get).toHaveBeenCalledTimes(1)
      expect(receipts).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('shows a selected run whose source changes only through its own lifetime: another source, another read', async () => {
    installHistory(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { operationId: 'operation-5' }) }), deliveredEntryFor(4)] })))
    const receipts = installReceipts()
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #5' }))
    expect(await screen.findByText(/Backend tests 1/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #4' }))
    await waitFor(() => expect(receipts).toHaveBeenCalledTimes(2))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect run #5' }))
    await waitFor(() => expect(receipts).toHaveBeenCalledTimes(3))

    expect(receipts.mock.calls.map(([runId]) => runId)).toEqual(['run-5', 'run-4', 'run-5'])
  })
})

describe('ProjectRunHistoryPanel rendering hygiene', () => {
  it('opens, pages and selects without a React warning such as a duplicate key', async () => {
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    try {
      installReceipts()
      installHistory((_project, before) =>
        Promise.resolve(before === null ? pageOf([], { entries: descending(12, 3).map((number) => deliveredEntryFor(number)), hasMore: true, next: 3 }) : pageOf(descending(2, 1))),
      )
      render(<ProjectRunHistoryPanel projectId="project-1" />)
      open()
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect run #7' }))
      await screen.findByText(/Backend tests 1/)
      fireEvent.click(screen.getByRole('button', { name: 'Load older runs' }))
      await screen.findByText('Objective 1')

      expect(errors).not.toHaveBeenCalled()
    } finally {
      errors.mockRestore()
    }
  })
})
