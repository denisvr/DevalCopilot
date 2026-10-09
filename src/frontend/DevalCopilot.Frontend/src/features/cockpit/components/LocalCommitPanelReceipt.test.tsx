// @vitest-environment jsdom
import { act, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localCommitStatusClient, localDeliveryReceiptClient } from '../../../api/clients'
import { GetLocalCommitStatusResponse } from '../../../api/generated/api-client'
import type { GetLocalDeliveryReceiptResponse } from '../../../api/generated/api-client'
import { availableFor, completedOperation, sourceFor, stateOnly } from '../localDeliveryReceiptFixture'
import { LocalCommitPanel } from './LocalCommitPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localCommitStatusClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
  requestLocalCommitClient: vi.fn(),
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

const statusWith = (operation: ReturnType<typeof completedOperation>) =>
  new GetLocalCommitStatusResponse({
    eligible: false,
    refusalCode: 'local_commit.operation_exists',
    latestEventSequence: 9,
    operation,
  })

function installStatus(read: (runId: string) => Promise<GetLocalCommitStatusResponse>) {
  const getLocalCommitStatus = vi.fn(read)
  vi.mocked(localCommitStatusClient).mockReturnValue({ getLocalCommitStatus } as unknown as ReturnType<typeof localCommitStatusClient>)
  return getLocalCommitStatus
}

function installReceipt(read: (runId: string) => Promise<GetLocalDeliveryReceiptResponse>) {
  const getLocalDeliveryReceipt = vi.fn(read)
  vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
  return getLocalDeliveryReceipt
}

const commitRegion = () => screen.getByRole('region', { name: 'Local commit' })
const receiptRegion = () => screen.getByRole('region', { name: 'Local delivery receipt' })

describe('LocalCommitPanel with the recorded local-delivery receipt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows the receipt region beside a Completed operation without changing the operation card or offering a mutation', async () => {
    const source = sourceFor('a')
    installStatus(() => Promise.resolve(statusWith(completedOperation('a'))))
    const receipts = installReceipt(() => Promise.resolve(availableFor(source)))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await screen.findByText('Backend tests 1')
    expect(within(commitRegion()).getByText('Completed')).toBeInTheDocument()
    expect(within(commitRegion()).getByText('Local commit only — not pushed. This does not describe the state of the rest of the workspace.')).toBeInTheDocument()
    expect(receiptRegion().textContent).toContain(source.commitSha)
    expect(within(commitRegion()).queryByRole('button', { name: 'Commit locally' })).toBeNull()
    expect(within(commitRegion()).queryAllByRole('button')).toHaveLength(0)
    expect(receipts).toHaveBeenCalledTimes(1)
    expect(receipts).toHaveBeenCalledWith('run-a')
  })

  it.each(['Prepared', 'Executing', 'Failed', 'Interrupted', 'NeedsAttention'])(
    'shows no receipt region and reads no receipt for a %s operation',
    async (status) => {
      installStatus(() => Promise.resolve(statusWith(completedOperation('a', { status, commitSha: undefined }))))
      const receipts = installReceipt(() => Promise.resolve(availableFor(sourceFor('a'))))

      render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

      await waitFor(() => expect(within(commitRegion()).getByText(status)).toBeInTheDocument())
      expect(screen.queryByRole('region', { name: 'Local delivery receipt' })).toBeNull()
      expect(receipts).not.toHaveBeenCalled()
    },
  )

  it('shows no receipt region while no operation is recorded', async () => {
    installStatus(() => Promise.resolve(new GetLocalCommitStatusResponse({ eligible: false, refusalCode: 'local_commit.agent_approval_missing', latestEventSequence: 1 })))
    const receipts = installReceipt(() => Promise.resolve(availableFor(sourceFor('a'))))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await waitFor(() => expect(commitRegion().textContent).not.toContain('Reading the local-commit status'))
    expect(screen.queryByRole('region', { name: 'Local delivery receipt' })).toBeNull()
    expect(receipts).not.toHaveBeenCalled()
  })

  it('keeps the Completed status and shows only a fixed failure when the receipt cannot be read', async () => {
    installStatus(() => Promise.resolve(statusWith(completedOperation('a'))))
    installReceipt(() => Promise.reject(new Error('server stack detail')))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    const alert = await within(await screen.findByRole('region', { name: 'Local delivery receipt' })).findByRole('alert')
    expect(alert.textContent).toContain('could not be read')
    expect(within(commitRegion()).getByText('Completed')).toBeInTheDocument()
    expect(commitRegion().textContent).not.toContain('server stack detail')
    expect(commitRegion().textContent).not.toContain('could not be read, so a local commit cannot be requested')
  })

  it('reads again for a new run event only through the status, never the receipt, and never polls', async () => {
    installStatus(() => Promise.resolve(statusWith(completedOperation('a'))))
    const receipts = installReceipt(() => Promise.resolve(availableFor(sourceFor('a'))))
    const view = render(<LocalCommitPanel runId="run-a" latestSequence={9} />)
    await screen.findByText('Backend tests 1')

    view.rerender(<LocalCommitPanel runId="run-a" latestSequence={10} />)
    view.rerender(<LocalCommitPanel runId="run-a" latestSequence={11} evidenceRefreshGeneration={1} />)
    await waitFor(() => expect(vi.mocked(localCommitStatusClient)().getLocalCommitStatus).toHaveBeenCalledTimes(3))
    await act(async () => new Promise((resolve) => setTimeout(resolve, 30)))

    expect(receipts).toHaveBeenCalledTimes(1)
    expect(screen.getByText('Backend tests 1')).toBeInTheDocument()
  })

  it('follows the committed run: a replacement run never shows the previous receipt and a late older answer is ignored', async () => {
    const slowA = deferred<GetLocalDeliveryReceiptResponse>()
    installStatus((runId) => Promise.resolve(statusWith(completedOperation(runId.replace('run-', '')))))
    const receipts = installReceipt((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(availableFor(sourceFor('b'), { objective: 'Objective B' }))))
    const view = render(<LocalCommitPanel key="run-a" runId="run-a" latestSequence={9} />)
    await waitFor(() => expect(receipts).toHaveBeenCalledWith('run-a'))

    view.rerender(<LocalCommitPanel key="run-b" runId="run-b" latestSequence={9} />)
    await screen.findByText('Objective B')
    await act(async () => slowA.resolve(availableFor(sourceFor('a'), { objective: 'Objective A' })))

    expect(receiptRegion().textContent).toContain('Objective B')
    expect(receiptRegion().textContent).not.toContain('Objective A')
    expect(receipts.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b'])
  })

  it('shows an unavailable receipt as such while the completed operation stays unchanged', async () => {
    installStatus(() => Promise.resolve(statusWith(completedOperation('a'))))
    installReceipt(() => Promise.resolve(stateOnly('Unavailable')))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await waitFor(() => expect(receiptRegion().textContent).toContain('cannot be reconstructed'))
    expect(within(commitRegion()).getByText('Completed')).toBeInTheDocument()
    expect(receiptRegion().textContent).not.toContain('Passed')
  })

  it('masks the receipt together with the status when the status read fails', async () => {
    installStatus(() => Promise.reject(new Error('boom')))
    const receipts = installReceipt(() => Promise.resolve(availableFor(sourceFor('a'))))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await screen.findByRole('alert')
    expect(screen.queryByRole('region', { name: 'Local delivery receipt' })).toBeNull()
    expect(receipts).not.toHaveBeenCalled()
  })
})
