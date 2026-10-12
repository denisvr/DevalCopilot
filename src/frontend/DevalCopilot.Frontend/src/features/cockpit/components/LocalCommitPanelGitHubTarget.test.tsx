// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubObservationTargetClient, localCommitStatusClient, localDeliveryReceiptClient, recordGitHubObservationTargetClient } from '../../../api/clients'
import { GetLocalCommitStatusResponse } from '../../../api/generated/api-client'
import { availableFor, completedOperation, sourceFor } from '../localDeliveryReceiptFixture'
import { LocalCommitPanel } from './LocalCommitPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localCommitStatusClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
  requestLocalCommitClient: vi.fn(),
  gitHubObservationTargetClient: vi.fn(),
  recordGitHubObservationTargetClient: vi.fn(),
}))

const statusWith = (operation: ReturnType<typeof completedOperation>) =>
  new GetLocalCommitStatusResponse({ eligible: false, refusalCode: 'local_commit.operation_exists', latestEventSequence: 9, operation })

function install(operation: ReturnType<typeof completedOperation> | null) {
  vi.mocked(localCommitStatusClient).mockReturnValue({
    getLocalCommitStatus: vi.fn(() =>
      Promise.resolve(
        operation
          ? statusWith(operation)
          : new GetLocalCommitStatusResponse({ eligible: false, refusalCode: 'local_commit.agent_approval_missing', latestEventSequence: 1 }),
      ),
    ),
  } as unknown as ReturnType<typeof localCommitStatusClient>)
  vi.mocked(localDeliveryReceiptClient).mockReturnValue({
    getLocalDeliveryReceipt: vi.fn(() => Promise.resolve(availableFor(sourceFor('a')))),
  } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
  const get = vi.fn((runId: string) =>
    Promise.resolve({
      runId,
      state: 'NotRecorded',
      source: { operationId: 'operation-a', commitSha: 'c'.repeat(40), branchName: 'devalcopilot/run-1' },
    }),
  )
  const record = vi.fn(() => Promise.reject(new Error('not expected')))
  vi.mocked(gitHubObservationTargetClient).mockReturnValue({ getGitHubObservationTarget: get } as unknown as ReturnType<typeof gitHubObservationTargetClient>)
  vi.mocked(recordGitHubObservationTargetClient).mockReturnValue(
    { recordGitHubObservationTarget: record } as unknown as ReturnType<typeof recordGitHubObservationTargetClient>,
  )
  return { get, record }
}

const commitRegion = () => screen.getByRole('region', { name: 'Local commit' })
const targetRegion = () => screen.getByRole('region', { name: 'GitHub observation target' })

describe('LocalCommitPanel with the GitHub observation target region', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('offers a collapsed region beside, not inside, a Completed operation, and reads nothing until asked', async () => {
    const { get, record } = install(completedOperation('a'))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await screen.findByText('Backend tests 1')
    expect(within(targetRegion()).getByRole('button', { name: 'Show GitHub observation target' })).toBeInTheDocument()
    expect(commitRegion().contains(targetRegion())).toBe(false)
    expect(within(commitRegion()).queryAllByRole('button')).toHaveLength(0)
    expect(get).not.toHaveBeenCalled()
    expect(record).not.toHaveBeenCalled()

    await act(async () => {
      fireEvent.click(within(targetRegion()).getByRole('button', { name: 'Show GitHub observation target' }))
    })
    await act(async () => {
      fireEvent.click(within(targetRegion()).getByRole('button', { name: 'Load GitHub observation target' }))
    })

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    expect(within(targetRegion()).getByText('devalcopilot/run-1')).toBeInTheDocument()
    expect(within(targetRegion()).getByText('c'.repeat(40))).toBeInTheDocument()
    expect(within(targetRegion()).getByLabelText('GitHub owner')).toHaveValue('')
    expect(record).not.toHaveBeenCalled()
  })

  it.each(['Prepared', 'Executing', 'Failed', 'Interrupted', 'NeedsAttention'])('offers no region for a %s operation', async (status) => {
    install(completedOperation('a', { status, commitSha: undefined }))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await waitFor(() => expect(within(commitRegion()).getByText(status)).toBeInTheDocument())
    expect(screen.queryByRole('region', { name: 'GitHub observation target' })).toBeNull()
  })

  it('offers no region while no operation is recorded', async () => {
    install(null)

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await waitFor(() => expect(commitRegion().textContent).not.toContain('Reading the local-commit status'))
    expect(screen.queryByRole('region', { name: 'GitHub observation target' })).toBeNull()
  })

  it('cannot select a target for a Completed operation that lacks the facts that identify its source', async () => {
    const { get } = install(completedOperation('a', { branchName: undefined }))

    render(<LocalCommitPanel runId="run-a" latestSequence={9} />)

    await waitFor(() => expect(within(commitRegion()).getByText('Completed')).toBeInTheDocument())
    expect(within(targetRegion()).getByRole('button', { name: 'Show GitHub observation target' })).toBeDisabled()
    expect(get).not.toHaveBeenCalled()
  })
})
