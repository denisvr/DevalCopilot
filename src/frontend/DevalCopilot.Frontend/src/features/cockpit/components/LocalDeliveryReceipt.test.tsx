// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localDeliveryReceiptClient } from '../../../api/clients'
import type { GetLocalDeliveryReceiptResponse } from '../../../api/generated/api-client'
import { availableFor, sourceFor, stateOnly, verificationMember } from '../localDeliveryReceiptFixture'
import { LocalDeliveryReceipt } from './LocalDeliveryReceipt'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
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

function install(read: (runId: string) => Promise<GetLocalDeliveryReceiptResponse>) {
  const getLocalDeliveryReceipt = vi.fn(read)
  vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
  return getLocalDeliveryReceipt
}

const region = () => screen.getByRole('region', { name: 'Local delivery receipt' })

describe('LocalDeliveryReceipt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('names itself, explains that it is historical and local only, and is reading before the first answer', async () => {
    const read = deferred<GetLocalDeliveryReceiptResponse>()
    install(() => read.promise)
    render(<LocalDeliveryReceipt source={sourceFor()} />)

    expect(within(region()).getByRole('heading', { name: 'Local delivery receipt' })).toBeTruthy()
    expect(region().textContent).toContain('Reading the local delivery receipt')
    expect(region().textContent).toContain('historical record')
    expect(region().textContent).toContain('not the current workspace')
    expect(region().textContent).toContain('Nothing was pushed')
    expect(screen.queryByText('Backend tests 1')).toBeNull()

    await act(async () => read.resolve(availableFor(sourceFor())))
    await waitFor(() => expect(region().textContent).not.toContain('Reading the local delivery receipt'))
  })

  it('shows the exact recorded commit, checkpoint, approvals and every verification member in recorded order', async () => {
    const source = sourceFor()
    install(() => Promise.resolve(availableFor(source)))
    render(<LocalDeliveryReceipt source={source} />)

    await screen.findByText('Backend tests 1')
    const text = region().textContent ?? ''
    expect(text).toContain(source.commitSha)
    expect(text).toContain('a'.repeat(40))
    expect(text).toContain('b'.repeat(40))
    expect(text).toContain('devalcopilot/run-1')
    expect(text).toContain('Implement the ledger')
    expect(text).toContain('#3')
    expect(text).toContain('f'.repeat(64))
    expect(text).toContain('report-1')
    expect(text).toContain('review-attempt-1')
    expect(text).toContain('approval-1')
    expect(text).toContain('human-review-1')
    expect(text).toContain('Approved')
    const members = within(region()).getAllByRole('listitem')
    expect(members).toHaveLength(2)
    expect(members[0].textContent).toContain('Backend tests 1')
    expect(members[0].textContent).toContain('execution-0')
    expect(members[0].textContent).toContain('Passed')
    expect(members[0].textContent).toContain('exit code 0')
    expect(members[1].textContent).toContain('Backend tests 2')
    expect(members[1].textContent).toContain('execution-1')
    expect(text).toContain('2026-10-09T12:00:00.000Z')
    expect(text).toContain('2026-10-09T13:00:00.000Z')
  })

  it('renders every untrusted value as text and never as markup', async () => {
    const source = sourceFor()
    const hostile = '<img src=x onerror="window.__receiptPwned=true"><script>window.__receiptPwned=true</script>'
    install(() =>
      Promise.resolve(
        availableFor(source, {
          objective: hostile,
          branchName: `${hostile}-branch`,
          verification: [verificationMember(0, { commandName: hostile }), verificationMember(1, { commandName: `**${hostile}**` })],
        }),
      ),
    )
    render(<LocalDeliveryReceipt source={source} />)

    await waitFor(() => expect(region().textContent).toContain(hostile))
    expect(region().querySelector('img')).toBeNull()
    expect(region().querySelector('script')).toBeNull()
    expect((window as unknown as Record<string, unknown>).__receiptPwned).toBeUndefined()
  })

  it('offers no action and no input for an available receipt', async () => {
    const source = sourceFor()
    install(() => Promise.resolve(availableFor(source)))
    render(<LocalDeliveryReceipt source={source} />)
    await screen.findByText('Backend tests 1')

    expect(within(region()).queryAllByRole('button')).toHaveLength(0)
    expect(within(region()).queryAllByRole('textbox')).toHaveLength(0)
  })

  it('shows a failed read as a fixed alert that never echoes the failure and reads again only on request', async () => {
    const source = sourceFor()
    const get = install(() => Promise.reject(new Error('internal secret detail')))
    render(<LocalDeliveryReceipt source={source} />)

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('could not be read')
    expect(alert.textContent).toContain('recorded local commit')
    expect(region().textContent).not.toContain('internal secret detail')
    expect(screen.queryByText('Backend tests 1')).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)

    get.mockImplementationOnce(() => Promise.resolve(availableFor(source)))
    fireEvent.click(within(region()).getByRole('button', { name: 'Read the receipt again' }))
    await screen.findByText('Backend tests 1')
    expect(get).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('refuses an inconsistent answer without showing any part of it', async () => {
    const source = sourceFor()
    install(() => Promise.resolve(availableFor({ ...source, runId: 'run-other' })))
    render(<LocalDeliveryReceipt source={source} />)

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('does not match')
    expect(region().textContent).not.toContain(source.commitSha)
    expect(screen.queryByText('Backend tests 1')).toBeNull()
  })

  it('says that no receipt can be reconstructed for an unavailable one and shows no success-looking summary', async () => {
    install(() => Promise.resolve(stateOnly('Unavailable')))
    render(<LocalDeliveryReceipt source={sourceFor()} />)

    await waitFor(() => expect(region().textContent).toContain('cannot be reconstructed'))
    expect(region().textContent).toContain('recorded local commit')
    expect(screen.queryByText('Backend tests 1')).toBeNull()
    expect(region().textContent).not.toContain('Approved')
    expect(region().textContent).not.toContain('Passed')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('states an operation without identifying facts is not read at all', () => {
    const get = install(() => Promise.resolve(availableFor(sourceFor())))
    render(<LocalDeliveryReceipt source={null} />)

    expect(region().textContent).toContain('does not carry the facts')
    expect(get).not.toHaveBeenCalled()
  })

  it('belongs to its source: a replacement never shows the previous receipt and a late older answer is ignored', async () => {
    const slowA = deferred<GetLocalDeliveryReceiptResponse>()
    const get = install((runId) => (runId === 'run-a' ? slowA.promise : Promise.resolve(availableFor(sourceFor('b')))))
    const view = render(<LocalDeliveryReceipt source={sourceFor('a')} />)

    view.rerender(<LocalDeliveryReceipt source={sourceFor('b')} />)
    expect(region().textContent).toContain('Reading the local delivery receipt')
    expect(region().textContent).not.toContain('operation-a')
    await screen.findByText('Backend tests 1')
    await act(async () => slowA.resolve(availableFor(sourceFor('a'), { objective: 'Older objective' })))

    expect(region().textContent).not.toContain('Older objective')
    expect(get.mock.calls.map(([runId]) => runId)).toEqual(['run-a', 'run-b'])
    view.unmount()
  })
})
