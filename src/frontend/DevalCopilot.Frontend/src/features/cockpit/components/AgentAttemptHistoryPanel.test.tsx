// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
} from '../../../api/clients'
import { AgentAttemptEvidenceWireClient } from '../agentAttemptEvidenceWire'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

vi.mock('../../../api/clients', () => ({
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
}))

const entry = (attemptNumber: number, overrides: Record<string, unknown> = {}) => ({
  attemptId: `attempt-${attemptNumber}`,
  attemptNumber,
  status: 'Failed',
  claimedAtUtc: new Date('2026-09-29T10:00:00Z'),
  identityValid: true,
  role: 'Implementer',
  provider: 'ClaudeCode',
  responseContract: 'ImplementationReport',
  ...overrides,
})

const evidenceFor = (attemptNumber: number, purposes: string[] = ['AgentStandardError', 'AgentFinalResponse']) => ({
  identityValid: true,
  attemptId: `attempt-${attemptNumber}`,
  attemptNumber,
  attemptStatus: 'Failed',
  claimedAtUtc: new Date('2026-09-29T10:00:00Z'),
  role: 'Implementer',
  provider: 'ClaudeCode',
  responseContract: 'ImplementationReport',
  artifacts: purposes.map((purpose) => ({ purpose, byteLength: 12, truncated: false, captureOutcome: 'Captured' })),
})

/** `count` coherent rows numbered downward from `highest`, as one page carries them. */
const entriesFrom = (highest: number, count: number) => Array.from({ length: count }, (_, index) => entry(highest - index))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

let history: ReturnType<typeof vi.fn>
let evidence: ReturnType<typeof vi.fn>
let windowRead: ReturnType<typeof vi.fn>

beforeEach(() => {
  history = vi.fn()
  evidence = vi.fn()
  windowRead = vi.fn()
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: history } as never)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidence } as never)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({ getAgentAttemptArtifactWindow: windowRead } as never)
})

afterEach(() => vi.restoreAllMocks())

function openHistory() {
  fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
}

describe('AgentAttemptHistoryPanel', () => {
  it('fetches nothing until the owner opens the history, then requests one bounded first page', async () => {
    history.mockResolvedValue({ items: [entry(2), entry(1)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)

    expect(history).not.toHaveBeenCalled()
    openHistory()

    expect(await screen.findByText(/Attempt #2 · Implementer · ClaudeCode · Failed/)).toBeTruthy()
    expect(history).toHaveBeenCalledTimes(1)
    expect(history).toHaveBeenCalledWith('run-1', null, 10)
    expect(evidence).not.toHaveBeenCalled()
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('shows an explicit empty state', async () => {
    history.mockResolvedValue({ items: [], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)

    openHistory()

    expect(await screen.findByText('No Agent attempts have been recorded for this run.')).toBeTruthy()
  })

  it('does not offer inspection for an attempt whose identity could not be verified', async () => {
    history.mockResolvedValue({
      items: [entry(2, { identityValid: false, role: undefined, provider: undefined, responseContract: undefined }), entry(1)],
      hasMore: false,
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)

    openHistory()

    expect(await screen.findByText(/Attempt #2 · Failed · identity could not be verified/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Inspect attempt 2' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Inspect attempt 1' })).toBeTruthy()
  })

  it('pages older attempts with the server cursor and never duplicates or skips a row', async () => {
    history
      .mockResolvedValueOnce({ items: entriesFrom(12, 10), hasMore: true, nextBeforeAttemptNumber: 3 })
      .mockResolvedValueOnce({ items: [entry(2), entry(1)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    await screen.findByText(/Attempt #12/)

    fireEvent.click(screen.getByRole('button', { name: 'Load older attempts' }))

    await screen.findByText(/Attempt #1 /)
    expect(history).toHaveBeenNthCalledWith(2, 'run-1', 3, 10)
    const rows = screen.getAllByRole('listitem').map((row) => row.textContent ?? '')
    expect(rows.map((row) => Number(/Attempt #(\d+)/.exec(row)?.[1]))).toEqual([12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1])
    expect(screen.queryByRole('button', { name: 'Load older attempts' })).toBeNull()
  })

  it('retries a failed first page and a failed later page at the same cursor while keeping loaded rows', async () => {
    history
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ items: entriesFrom(12, 10), hasMore: true, nextBeforeAttemptNumber: 3 })
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ items: [entry(2)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()

    expect(await screen.findByText('The Agent attempt history could not be loaded.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByText(/Attempt #3 /)
    expect(history).toHaveBeenNthCalledWith(2, 'run-1', null, 10)

    fireEvent.click(screen.getByRole('button', { name: 'Load older attempts' }))
    expect(await screen.findByText(/The older attempts could not be loaded\. The attempts already shown are unchanged\./)).toBeTruthy()
    expect(screen.getByText(/Attempt #3 /)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Load older attempts' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await screen.findByText(/Attempt #2 /)
    expect(history).toHaveBeenNthCalledWith(3, 'run-1', 3, 10)
    expect(history).toHaveBeenNthCalledWith(4, 'run-1', 3, 10)
    expect(screen.getAllByRole('listitem')).toHaveLength(11)
  })

  it('rejects a malformed page whole, keeps the valid rows and retries the same cursor without duplication', async () => {
    history
      .mockResolvedValueOnce({ items: entriesFrom(12, 10), hasMore: true, nextBeforeAttemptNumber: 3 })
      // Out of order: 1 before 2 — none of this page may be shown.
      .mockResolvedValueOnce({ items: [entry(1), entry(2)], hasMore: false })
      .mockResolvedValueOnce({ items: [entry(2), entry(1)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    await screen.findByText(/Attempt #12/)

    fireEvent.click(screen.getByRole('button', { name: 'Load older attempts' }))

    expect(await screen.findByText(/older attempts that are not coherent, so none of that page is shown/)).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(10)
    expect(screen.queryByText(/Attempt #1 /)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByText(/Attempt #1 /)
    expect(history.mock.calls.map((call) => call[1])).toEqual([null, 3, 3])
    expect(screen.getAllByRole('listitem')).toHaveLength(12)
  })

  it.each([
    ['a continuation that is not a full page', { items: [entry(4), entry(3)], hasMore: true, nextBeforeAttemptNumber: 3 }],
    ['a continuation cursor that is not the last number', { items: entriesFrom(12, 10), hasMore: true, nextBeforeAttemptNumber: 2 }],
    ['a duplicated attempt identity', { items: [entry(2), { ...entry(1), attemptId: 'attempt-2' }], hasMore: false }],
    ['a duplicated attempt number', { items: [entry(2), { ...entry(2), attemptId: 'attempt-other' }], hasMore: false }],
    ['more rows than requested', { items: entriesFrom(11, 11), hasMore: false }],
    ['a verified row without a role', { items: [entry(1, { role: undefined })], hasMore: false }],
    ['an unverified row that still names its provider', { items: [entry(1, { identityValid: false, role: undefined, responseContract: undefined })], hasMore: false }],
  ])('shows nothing of a first page with %s', async (_label, page) => {
    history.mockResolvedValue(page)
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()

    expect(await screen.findByText('The host answered with an Agent attempt history that is not coherent, so none of it is shown.')).toBeTruthy()
    expect(screen.queryAllByRole('listitem')).toHaveLength(0)
    expect(screen.queryByRole('button', { name: /Inspect attempt/ })).toBeNull()
    expect(evidence).not.toHaveBeenCalled()
  })

  it('discards the loaded history when it is closed and refetches when it is reopened', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    await screen.findByText(/Attempt #1/)

    fireEvent.click(screen.getByRole('button', { name: 'Hide Agent attempt history' }))
    expect(screen.queryByText(/Attempt #1/)).toBeNull()
    openHistory()

    await screen.findByText(/Attempt #1/)
    expect(history).toHaveBeenCalledTimes(2)
  })

  it('fetches evidence only when an attempt is selected and artifact text only after a purpose is chosen', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockResolvedValue({ status: 'Ok', text: 'boom', nextOffset: 4, totalLengthSoFar: 4, truncated: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    await screen.findByText(/Attempt #1/)
    expect(evidence).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))

    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    expect(evidence).toHaveBeenCalledWith('run-1', 'attempt-1')
    expect(windowRead).not.toHaveBeenCalled()

    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    expect(windowRead).not.toHaveBeenCalled()
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))

    expect(await within(detail).findByText('boom')).toBeTruthy()
    // The attempt route answers only its own closed purpose segments.
    expect(windowRead).toHaveBeenCalledWith('run-1', 'attempt-1', 'stderr', 0, 16384)
  })

  it.each([
    ['AgentContextManifest', 'context-manifest'],
    ['AgentStandardOutput', 'stdout'],
    ['AgentStandardError', 'stderr'],
    ['AgentFinalResponse', 'final-response'],
  ])('requests the %s window through the route segment %s', async (purpose, segment) => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1, [purpose]))
    windowRead.mockResolvedValue({ status: 'Ok', text: 'window', nextOffset: 6, totalLengthSoFar: 6, truncated: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: purpose } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))

    expect(await within(detail).findByText('window')).toBeTruthy()
    expect(windowRead).toHaveBeenCalledWith('run-1', 'attempt-1', segment, 0, 16384)
  })

  it.each([
    ['another attempt identity', { ...evidenceFor(1), attemptId: 'attempt-9' }],
    ['another attempt number', { ...evidenceFor(1), attemptNumber: 9 }],
    ['no identity verdict', { ...evidenceFor(1), identityValid: undefined }],
    ['a purpose outside the allowlist', evidenceFor(1, ['ProcessStandardOutput'])],
    ['a prototype-shaped purpose', evidenceFor(1, ['toString'])],
    ['a repeated purpose', evidenceFor(1, ['AgentStandardError', 'AgentStandardError'])],
  ])('fails closed on evidence that names %s and offers no artifact from it', async (_label, answer) => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(answer)
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })

    expect(await within(detail).findByText(/attempt evidence that is not coherent for this attempt, so none of it is shown/)).toBeTruthy()
    expect(within(detail).queryByRole('combobox')).toBeNull()
    expect(within(detail).queryByText(/bytes/)).toBeNull()
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('never shows the evidence of an earlier selection that answers after another attempt was selected', async () => {
    const slow = deferred<unknown>()
    history.mockResolvedValue({ items: [entry(2), entry(1)], hasMore: false })
    evidence.mockImplementation((_runId: string, attemptId: string) =>
      attemptId === 'attempt-2' ? slow.promise : Promise.resolve(evidenceFor(1, ['AgentFinalResponse'])),
    )
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Final response: 12 bytes/)

    slow.resolve(evidenceFor(2, ['AgentStandardError']))
    await Promise.resolve()

    expect(within(detail).queryByText(/Standard error/)).toBeNull()
    expect(within(detail).getByText(/Attempt #1 · Implementer/)).toBeTruthy()
  })

  it('never appends the late window of an earlier purpose to the purpose that replaced it', async () => {
    const slowStderr = deferred<unknown>()
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockImplementation((_runId: string, _attemptId: string, segment: string) =>
      segment === 'stderr' ? slowStderr.promise : Promise.resolve({ status: 'Ok', text: 'final text', nextOffset: 10, totalLengthSoFar: 10, truncated: false }),
    )
    const { container } = render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentFinalResponse' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('final text')

    slowStderr.resolve({ status: 'Ok', text: 'stderr text', nextOffset: 11, totalLengthSoFar: 11, truncated: false })
    await Promise.resolve()

    expect(container.querySelector('pre')?.textContent).toBe('final text')
  })

  it('renders markup-shaped artifact text literally and never as elements', async () => {
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script> **not bold**'
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    // One byte per ASCII character: a coherent window never decodes to more code units than the bytes it consumed.
    windowRead.mockResolvedValue({ status: 'Ok', text: hostile, nextOffset: hostile.length, totalLengthSoFar: hostile.length, truncated: false })
    const { container } = render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))

    const text = await waitFor(() => {
      const pre = container.querySelector('pre.dc-artifact-window-text')
      expect(pre).not.toBeNull()
      return pre!
    })
    expect(text.textContent).toBe(hostile)
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('script')).toBeNull()
  })

  it('reconstructs multiple windows and retries a failed later window at the same offset without duplication', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead
      .mockResolvedValueOnce({ status: 'Ok', text: 'AAAA', nextOffset: 4, totalLengthSoFar: 12, truncated: false })
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ status: 'Ok', text: 'BBBB', nextOffset: 8, totalLengthSoFar: 12, truncated: false })
      .mockResolvedValueOnce({ status: 'Ok', text: 'CCCC', nextOffset: 12, totalLengthSoFar: 12, truncated: false })
    const { container } = render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('AAAA')

    fireEvent.click(within(detail).getByRole('button', { name: 'Load next window' }))
    await within(detail).findByText('This artifact window is unavailable.')
    fireEvent.click(within(detail).getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(container.querySelector('pre')?.textContent).toBe('AAAABBBB'))
    fireEvent.click(within(detail).getByRole('button', { name: 'Load next window' }))

    await waitFor(() => expect(container.querySelector('pre')?.textContent).toBe('AAAABBBBCCCC'))
    expect(windowRead.mock.calls.map((call) => call[3])).toEqual([0, 4, 4, 8])
    expect(within(detail).queryByRole('button', { name: 'Load next window' })).toBeNull()
  })

  it('clears accumulated text when the purpose changes and when the attempt is closed', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockResolvedValue({ status: 'Ok', text: 'stderr text', nextOffset: 11, totalLengthSoFar: 11, truncated: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('stderr text')

    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentFinalResponse' } })
    expect(screen.queryByText('stderr text')).toBeNull()

    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('stderr text')
    fireEvent.click(within(detail).getByRole('button', { name: 'Close attempt' }))
    expect(screen.queryByText('stderr text')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected Agent attempt evidence' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    const reopened = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(reopened).findByText(/Standard error: 12 bytes/)
    expect(screen.queryByText('stderr text')).toBeNull()
  })

  it('resets history, selection, and text when the run changes', async () => {
    history
      .mockResolvedValueOnce({ items: [entry(1)], hasMore: false })
      .mockResolvedValueOnce({ items: [entry(7)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockResolvedValue({ status: 'Ok', text: 'run one text', nextOffset: 12, totalLengthSoFar: 12, truncated: false })
    const { rerender } = render(<AgentAttemptHistoryPanel key="run-1" runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('run one text')

    rerender(<AgentAttemptHistoryPanel key="run-2" runId="run-2" />)

    expect(screen.queryByText('run one text')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected Agent attempt evidence' })).toBeNull()
    expect(screen.queryByText(/Attempt #1/)).toBeNull()
    openHistory()
    await screen.findByText(/Attempt #7/)
    expect(history).toHaveBeenLastCalledWith('run-2', null, 10)
  })

  it('never applies a late response for a run that is no longer shown', async () => {
    let resolveFirst: (value: unknown) => void = () => undefined
    history
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockResolvedValueOnce({ items: [entry(7)], hasMore: false })
    const { rerender } = render(<AgentAttemptHistoryPanel key="run-1" runId="run-1" />)
    openHistory()
    rerender(<AgentAttemptHistoryPanel key="run-2" runId="run-2" />)
    openHistory()
    await screen.findByText(/Attempt #7/)

    resolveFirst({ items: [entry(1)], hasMore: false })
    await Promise.resolve()

    expect(screen.queryByText(/Attempt #1 /)).toBeNull()
    expect(screen.getByText(/Attempt #7/)).toBeTruthy()
  })

  it('shows retry for an evidence failure and safe messages for an unverifiable or missing attempt', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence
      .mockRejectedValueOnce(new Error('sensitive detail'))
      .mockResolvedValueOnce({ identityValid: false, attemptId: 'attempt-1', attemptNumber: 1, artifacts: [] })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))

    expect(await screen.findByText('The attempt evidence could not be loaded.')).toBeTruthy()
    expect(screen.queryByText(/sensitive detail/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText(/recorded identity could not be verified, so its evidence is not shown/)).toBeTruthy()
  })

  it('maps a 404 evidence response to a not-available state', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockRejectedValue({ status: 404 })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))

    expect(await screen.findByText('This attempt is no longer available.')).toBeTruthy()
  })

  it('shows verified-failure states without any text', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1, ['AgentStandardOutput', 'AgentStandardError', 'AgentFinalResponse']))
    windowRead
      .mockResolvedValueOnce({ status: 'IntegrityMismatch', text: '', nextOffset: 0, totalLengthSoFar: 0 })
      .mockResolvedValueOnce({ status: 'Missing', text: '', nextOffset: 0, totalLengthSoFar: 0 })
      .mockResolvedValueOnce({ status: 'AttemptIdentityInvalid', text: '', nextOffset: 0, totalLengthSoFar: 0 })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard output: 12 bytes/)

    for (const [purpose, message] of [
      ['AgentStandardOutput', /failed integrity verification/],
      ['AgentStandardError', /could not be verified and is not shown/],
      ['AgentFinalResponse', /recorded identity could not be verified, so its artifacts are not shown/],
    ] as const) {
      fireEvent.change(within(detail).getByRole('combobox'), { target: { value: purpose } })
      fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
      expect(await within(detail).findByText(message)).toBeTruthy()
      expect(detail.querySelector('pre')).toBeNull()
    }
  })

  it('shows purpose-specific historical sensitivity caveats', async () => {
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1, ['AgentContextManifest', 'AgentStandardOutput']))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Context manifest: 12 bytes/)

    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentContextManifest' } })
    expect(within(detail).getByText(/composed entirely by DevalCopilot/)).toBeTruthy()
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardOutput' } })
    expect(within(detail).getByText(/best-effort redaction/)).toBeTruthy()
    expect(within(detail).getByText(/Historical evidence for a past attempt/)).toBeTruthy()
  })

  it('never writes artifact text to browser storage', async () => {
    const local = vi.spyOn(Storage.prototype, 'setItem')
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockResolvedValue({ status: 'Ok', text: 'stored?', nextOffset: 7, totalLengthSoFar: 7, truncated: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
    const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    await within(detail).findByText(/Standard error: 12 bytes/)
    fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
    fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
    await within(detail).findByText('stored?')

    expect(local).not.toHaveBeenCalled()
    expect(window.location.href).not.toContain('stored')
  })

  describe('repair lineage provenance', () => {
    it('shows the source number, a generic fallback for an id only, and nothing when the link is unknown', async () => {
      history.mockResolvedValue({
        items: [
          entry(3, { repairSourceAttemptId: 'attempt-2', repairSourceAttemptNumber: 2 }),
          entry(2, { repairSourceAttemptId: 'attempt-1' }),
          entry(1),
        ],
        hasMore: false,
      })
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()

      const rows = (await screen.findAllByRole('listitem')).map((row) => row.textContent ?? '')
      expect(rows[0]).toContain('Repair request for attempt #2')
      expect(rows[1]).toContain('Repair request for an earlier attempt')
      expect(rows[1]).not.toContain('#undefined')
      expect(rows[2]).not.toContain('Repair request')
      for (const row of rows) {
        expect(row).not.toMatch(/fixed|corrected|preserved/i)
      }
    })

    it('shows provenance in the selected attempt evidence only when a link is recorded', async () => {
      history.mockResolvedValue({ items: [entry(2), entry(1)], hasMore: false })
      evidence.mockResolvedValueOnce({
        ...evidenceFor(2),
        repairSourceAttemptId: 'attempt-1',
        repairSourceAttemptNumber: 1,
      })
      evidence.mockResolvedValueOnce(evidenceFor(1))
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()
      await screen.findByText(/Attempt #2/)

      fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
      const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
      expect(await within(detail).findByText(/Repair request for attempt #1/)).toBeTruthy()
      expect(detail.textContent).not.toMatch(/fixed|corrected|preserved/i)

      fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
      await within(await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })).findByText(
        /Standard error: 12 bytes/,
      )
      expect(screen.getByRole('region', { name: 'Selected Agent attempt evidence' }).textContent).not.toContain(
        'Repair request',
      )
    })
  })

  describe('recorded execution facts', () => {
    const DISPATCHED = new Date('2026-09-29T10:00:02Z')
    const ENDED = new Date('2026-09-29T10:01:05Z')
    const cleanExit = { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 900_000 }
    const claudeUsage = { inputTokens: 1200, outputTokens: 80, cacheCreationInputTokens: 0, cacheReadInputTokens: 30 }
    const concluded = (attemptNumber: number, overrides: Record<string, unknown> = {}) => ({
      ...evidenceFor(attemptNumber),
      attemptStatus: 'Failed',
      outcome: 'ProviderInvocationFailed',
      dispatchedAtUtc: DISPATCHED,
      completedAtUtc: ENDED,
      processExecution: cleanExit,
      tokenUsage: claudeUsage,
      ...overrides,
    })

    async function factsOf(answer: Record<string, unknown>) {
      history.mockResolvedValue({ items: [entry(1)], hasMore: false })
      evidence.mockResolvedValue(answer)
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
      const detail = await screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
      return within(detail).findByRole('region', { name: 'Recorded execution facts' })
    }

    const line = (facts: HTMLElement, selector: string) => facts.querySelector(selector)?.textContent

    it('shows a clean process exit beside a failed semantic result as separate facts, with the contract and recorded times', async () => {
      const facts = await factsOf(concluded(1))

      expect(within(facts).getByText('Response contract: ImplementationReport')).toBeTruthy()
      expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, dispatched 2026-09-29 10:00:02 UTC, ended 2026-09-29 10:01:05 UTC.')).toBeTruthy()
      expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: ProviderInvocationFailed')
      expect(line(facts, '.dc-process-evidence')).toBe('Process exited with code 0 after 1.2 s')
      expect(line(facts, '.dc-attempt-history-timeout')).toBe('Configured timeout: 15m 00s (set when the attempt was claimed; not a measurement)')
      expect(line(facts, '.dc-token-usage')).toBe('Tokens: 1,200 input · 80 output · 0 cache write · 30 cache read')
      expect(facts.textContent).toMatch(/A clean exit does not mean the attempt succeeded/)
      expect(facts.textContent).toMatch(/not cost, quota, remaining capacity, readiness or approval/)
      expect(facts.textContent).not.toMatch(/total|budget|remaining tokens|%/i)
    })

    it.each([
      ['a nonzero exit', { outcome: 'Exited', exitCode: 3, durationMilliseconds: 250, timeoutMilliseconds: 900_000 }, 'Process exited with code 3 after 250 ms', 'Exited'],
      ['a timeout', { outcome: 'TimedOut', exitCode: null, durationMilliseconds: 900_000, timeoutMilliseconds: 900_000 }, 'Process timed out after 15m 00s', 'TimedOut'],
      ['a cancellation', { outcome: 'Cancelled', exitCode: null, durationMilliseconds: 42_000, timeoutMilliseconds: 900_000 }, 'Process cancelled after 42.0 s', 'Cancelled'],
      ['a zero duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: 0, timeoutMilliseconds: 900_000 }, 'Process exited with code 0 after 0 ms', 'Exited'],
    ])('shows %s as host-measured evidence with the configured timeout on its own line', async (_label, processExecution, text, outcome) => {
      const facts = await factsOf(concluded(1, { processExecution }))

      expect(line(facts, '.dc-process-evidence')).toBe(text)
      expect(facts.querySelector('.dc-process-evidence')?.getAttribute('data-process-outcome')).toBe(outcome)
      expect(line(facts, '.dc-attempt-history-timeout')).toMatch(/^Configured timeout: 15m 00s/)
    })

    it('shows known zero counts as zero and omits each cache count the provider contract does not report', async () => {
      const zero = await factsOf(concluded(1, { tokenUsage: { inputTokens: 0, outputTokens: 0, cacheCreationInputTokens: null, cacheReadInputTokens: 0 } }))

      expect(line(zero, '.dc-token-usage')).toBe('Tokens: 0 input · 0 output · 0 cache read')
      expect(zero.querySelector('.dc-token-usage')?.getAttribute('data-token-usage')).toBe('Known')
    })

    it('shows Codex usage without any cache count', async () => {
      history.mockResolvedValue({ items: [entry(1, { role: 'Planner', provider: 'Codex', responseContract: 'Proposal' })], hasMore: false })
      evidence.mockResolvedValue(
        concluded(1, {
          role: 'Planner',
          provider: 'Codex',
          responseContract: 'Proposal',
          outcome: 'Proposed',
          tokenUsage: { inputTokens: 24763, outputTokens: 122, cacheCreationInputTokens: null, cacheReadInputTokens: null },
        }),
      )
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
      const facts = await screen.findByRole('region', { name: 'Recorded execution facts' })

      expect(line(facts, '.dc-token-usage')).toBe('Tokens: 24,763 input · 122 output')
      expect(within(facts).getByText('Response contract: Proposal')).toBeTruthy()
    })

    it('keeps a concluded attempt with nothing recorded unknown, never zero', async () => {
      const facts = await factsOf(
        concluded(1, {
          attemptStatus: 'Interrupted',
          outcome: null,
          processExecution: { outcome: null, exitCode: null, durationMilliseconds: null, timeoutMilliseconds: 900_000 },
          tokenUsage: { inputTokens: null, outputTokens: null, cacheCreationInputTokens: null, cacheReadInputTokens: null },
        }),
      )

      expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: not recorded')
      expect(line(facts, '.dc-process-evidence')).toBe('Process evidence unknown')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage unknown')
      expect(line(facts, '.dc-attempt-history-timeout')).toMatch(/^Configured timeout: 15m 00s/)
      expect(facts.textContent).not.toMatch(/\b0 (ms|input|output)/)
    })

    it('shows a configured timeout as not recorded when the host has none, and nothing for missing evidence objects', async () => {
      const facts = await factsOf(concluded(1, { processExecution: undefined, tokenUsage: undefined }))

      expect(line(facts, '.dc-process-evidence')).toBe('Process evidence unknown')
      expect(line(facts, '.dc-attempt-history-timeout')).toBe('Configured timeout: not recorded')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage unknown')
    })

    it('makes invalid numbers unavailable in their own section while metadata and artifact access stay usable', async () => {
      windowRead.mockResolvedValue({ status: 'Ok', text: 'kept', nextOffset: 4, totalLengthSoFar: 4, truncated: false })
      const facts = await factsOf(
        concluded(1, {
          processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: -5, timeoutMilliseconds: 900_000 },
          tokenUsage: { inputTokens: 1.5, outputTokens: 80, cacheCreationInputTokens: null, cacheReadInputTokens: null },
        }),
      )
      const detail = screen.getByRole('region', { name: 'Selected Agent attempt evidence' })

      expect(line(facts, '.dc-process-evidence')).toBe('Process evidence unavailable: the recorded values are not coherent, so no measured result is shown')
      expect(facts.querySelector('.dc-process-evidence')?.getAttribute('data-process-outcome')).toBe('Unavailable')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage unavailable: the recorded counts are not coherent, so none is shown')
      expect(line(facts, '.dc-attempt-history-timeout')).toMatch(/^Configured timeout: 15m 00s/)
      expect(facts.textContent).not.toMatch(/after 0 ms|\b0 input|\b1 input|exited with code/)
      expect(within(detail).getByText(/Standard error: 12 bytes/)).toBeTruthy()
      fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
      fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
      expect(await within(detail).findByText('kept')).toBeTruthy()
    })

    it('makes an invalid configured timeout unavailable without touching the measured result', async () => {
      const facts = await factsOf(concluded(1, { processExecution: { ...cleanExit, timeoutMilliseconds: 0 } }))

      expect(line(facts, '.dc-attempt-history-timeout')).toBe('Configured timeout unavailable: the recorded value is not coherent, so none is shown')
      expect(line(facts, '.dc-process-evidence')).toBe('Process exited with code 0 after 1.2 s')
    })

    it('never shows a malformed dispatch or end time as a date and trusts no measured fact behind an unreadable dispatch', async () => {
      const facts = await factsOf(concluded(1, { dispatchedAtUtc: new Date(Number.NaN), completedAtUtc: 0 }))

      expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, dispatch time unavailable, end time unavailable.')).toBeTruthy()
      expect(line(facts, '.dc-process-evidence')).toMatch(/^Process evidence unavailable/)
      expect(line(facts, '.dc-token-usage')).toMatch(/^Token usage unavailable/)
      expect(facts.textContent).not.toMatch(/1970|NaN|Invalid/)
    })

    it('shows no measured result or known usage for a still-running attempt whose answer carries plausible numbers', async () => {
      const facts = await factsOf(concluded(1, { attemptStatus: 'Running', outcome: null, completedAtUtc: undefined }))

      expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, dispatched 2026-09-29 10:00:02 UTC.')).toBeTruthy()
      expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: not yet recorded (the attempt is still running)')
      expect(line(facts, '.dc-process-evidence')).toBe('Process result not yet recorded')
      expect(facts.querySelector('.dc-process-evidence')?.getAttribute('data-process-outcome')).toBe('Unknown')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage not yet recorded')
      expect(line(facts, '.dc-attempt-history-timeout')).toMatch(/^Configured timeout: 15m 00s/)
      expect(facts.textContent).not.toMatch(/exited|1,200|1\.2 s/)
    })

    it('shows no measured result or known usage for an undispatched attempt whose answer carries plausible numbers', async () => {
      const facts = await factsOf(concluded(1, { outcome: 'AccountUsageStopReached', dispatchedAtUtc: undefined }))

      expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, not dispatched, ended 2026-09-29 10:01:05 UTC.')).toBeTruthy()
      expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: AccountUsageStopReached')
      expect(line(facts, '.dc-process-evidence')).toBe('Process not started')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage: none (provider not invoked)')
      expect(facts.querySelector('.dc-token-usage')?.getAttribute('data-token-usage')).toBe('Unknown')
      expect(facts.textContent).not.toMatch(/exited|1,200|1\.2 s/)
    })

    it('renders a markup-shaped recorded outcome literally', async () => {
      const facts = await factsOf(concluded(1, { outcome: '<img src=x onerror="alert(1)">' }))

      expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: <img src=x onerror="alert(1)">')
      expect(document.querySelector('img')).toBeNull()
    })

    it('replaces A with B with contrasting facts and never shows a late answer of A', async () => {
      const slowA = deferred<unknown>()
      history.mockResolvedValue({ items: [entry(2), entry(1)], hasMore: false })
      evidence
        .mockImplementationOnce(() => slowA.promise)
        .mockResolvedValueOnce(
          concluded(1, {
            outcome: 'Implemented',
            processExecution: { outcome: 'TimedOut', exitCode: null, durationMilliseconds: 900_000, timeoutMilliseconds: 900_000 },
            tokenUsage: { inputTokens: null, outputTokens: null, cacheCreationInputTokens: null, cacheReadInputTokens: null },
          }),
        )
        .mockResolvedValueOnce(concluded(2, { processExecution: { ...cleanExit, exitCode: 9 }, tokenUsage: { ...claudeUsage, inputTokens: 777 } }))
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()
      fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
      fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
      const facts = await screen.findByRole('region', { name: 'Recorded execution facts' })

      expect(line(facts, '.dc-process-evidence')).toBe('Process timed out after 15m 00s')
      expect(line(facts, '.dc-token-usage')).toBe('Token usage unknown')
      slowA.resolve(concluded(2, { processExecution: { ...cleanExit, exitCode: 9 }, tokenUsage: { ...claudeUsage, inputTokens: 777 } }))
      await Promise.resolve()
      expect(screen.getByRole('region', { name: 'Recorded execution facts' }).textContent).not.toMatch(/code 9|777/)

      fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
      await waitFor(() => expect(line(screen.getByRole('region', { name: 'Recorded execution facts' }), '.dc-process-evidence')).toBe('Process exited with code 9 after 1.2 s'))
      const replaced = screen.getByRole('region', { name: 'Recorded execution facts' })
      expect(line(replaced, '.dc-token-usage')).toBe('Tokens: 777 input · 80 output · 0 cache write · 30 cache read')
      expect(replaced.textContent).not.toMatch(/timed out|Token usage unknown|Implemented/)
      expect(evidence.mock.calls.map((call) => call[1])).toEqual(['attempt-2', 'attempt-1', 'attempt-2'])
    })

    describe('through the evidence facade with a raw host answer', () => {
      const rawAnswer = (dispatchedAtUtc: unknown) =>
        JSON.stringify({
          identityValid: true,
          attemptId: 'attempt-1',
          attemptNumber: 1,
          attemptStatus: 'Failed',
          claimedAtUtc: '2026-09-29T10:00:00+00:00',
          completedAtUtc: '2026-09-29T10:01:05.1234567+00:00',
          provider: 'ClaudeCode',
          role: 'Implementer',
          responseContract: 'ImplementationReport',
          outcome: 'ProviderInvocationFailed',
          dispatchedAtUtc,
          processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 900000 },
          tokenUsage: { inputTokens: 1200, outputTokens: 80, cacheCreationInputTokens: 0, cacheReadInputTokens: 30 },
          artifacts: [
            { purpose: 'AgentStandardError', byteLength: 12, truncated: false, captureOutcome: 'Captured' },
            { purpose: 'AgentFinalResponse', byteLength: 12, truncated: false, captureOutcome: 'Captured' },
          ],
        })

      async function factsThroughFacade(text: string) {
        const transport = vi.fn(() => Promise.resolve(new Response(text, { status: 200, headers: { 'Content-Type': 'application/json' } })))
        vi.mocked(agentAttemptEvidenceClient).mockReturnValue(new AgentAttemptEvidenceWireClient('http://host.invalid', { fetch: transport }) as never)
        history.mockResolvedValue({ items: [entry(1)], hasMore: false })
        render(<AgentAttemptHistoryPanel runId="run-1" />)
        openHistory()
        fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 1' }))
        const facts = await screen.findByRole('region', { name: 'Recorded execution facts' })
        return { facts, transport }
      }

      it.each<[string, unknown]>([
        ['the number 1 (revived as 2001-01-01 by the generated client alone)', 1],
        ['zero (dropped as absent by the generated client alone)', 0],
        ['false', false],
        ['an impossible calendar date that JavaScript rolls forward', '2026-02-30T00:00:00Z'],
        ['a text that JavaScript parses', 'Tue Sep 29 2026 10:00:02 GMT+0000'],
        ['an object whose own toString is 0 (the generated conversion would throw)', { toString: 0 }],
        ['an object whose own toString is null', { toString: null }],
        ['an array holding such an object', [{ toString: 0 }]],
      ])('shows a raw dispatch time that is %s as unavailable, with no measured result or known usage', async (_label, dispatchedAtUtc) => {
        windowRead.mockResolvedValue({ status: 'Ok', text: 'still here', nextOffset: 10, totalLengthSoFar: 10, truncated: false })
        const { facts, transport } = await factsThroughFacade(rawAnswer(dispatchedAtUtc))
        const detail = screen.getByRole('region', { name: 'Selected Agent attempt evidence' })

        expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, dispatch time unavailable, ended 2026-09-29 10:01:05 UTC.')).toBeTruthy()
        expect(line(facts, '.dc-attempt-history-semantic-result')).toBe('Semantic result: ProviderInvocationFailed')
        expect(line(facts, '.dc-process-evidence')).toMatch(/^Process evidence unavailable/)
        expect(line(facts, '.dc-token-usage')).toMatch(/^Token usage unavailable/)
        expect(line(facts, '.dc-attempt-history-timeout')).toMatch(/^Configured timeout: 15m 00s/)
        expect(facts.textContent).not.toMatch(/2001|2026-03-02|not dispatched|Process not started|exited|1,200/)
        expect(within(detail).getByText(/Standard error: 12 bytes/)).toBeTruthy()
        fireEvent.change(within(detail).getByRole('combobox'), { target: { value: 'AgentStandardError' } })
        fireEvent.click(within(detail).getByRole('button', { name: 'Load' }))
        expect(await within(detail).findByText('still here')).toBeTruthy()
        // The one evidence request went to the selected attempt's route; the explicit load used only the window route.
        expect(transport).toHaveBeenCalledTimes(1)
        expect(transport.mock.calls[0]).toEqual(['http://host.invalid/api/runs/run-1/agent-attempts/attempt-1/evidence', expect.anything()])
        expect(windowRead).toHaveBeenCalledTimes(1)
        expect(windowRead).toHaveBeenCalledWith('run-1', 'attempt-1', 'stderr', 0, 16384)
        expect(history).toHaveBeenCalledTimes(1)
        expect(evidence).not.toHaveBeenCalled()
      })

      it('shows the known facts of a valid raw answer, and an absent dispatch time as not dispatched', async () => {
        const { facts } = await factsThroughFacade(rawAnswer('2026-09-29T12:00:02.5+02:00'))

        expect(within(facts).getByText('Claimed 2026-09-29 10:00:00 UTC, dispatched 2026-09-29 10:00:02 UTC, ended 2026-09-29 10:01:05 UTC.')).toBeTruthy()
        expect(line(facts, '.dc-process-evidence')).toBe('Process exited with code 0 after 1.2 s')
        expect(line(facts, '.dc-token-usage')).toBe('Tokens: 1,200 input · 80 output · 0 cache write · 30 cache read')
      })

      it('shows a null raw dispatch time as not dispatched, never as unavailable', async () => {
        const { facts } = await factsThroughFacade(rawAnswer(null))

        expect(within(facts).getByText(/, not dispatched, ended /)).toBeTruthy()
        expect(line(facts, '.dc-process-evidence')).toBe('Process not started')
        expect(line(facts, '.dc-token-usage')).toBe('Token usage: none (provider not invoked)')
      })
    })

    it('starts no request, storage write or log for showing the facts', async () => {
      const local = vi.spyOn(Storage.prototype, 'setItem')
      const log = vi.spyOn(console, 'log')
      const facts = await factsOf(concluded(1))
      expect(line(facts, '.dc-process-evidence')).toBe('Process exited with code 0 after 1.2 s')
      await Promise.resolve()

      expect(history).toHaveBeenCalledTimes(1)
      expect(evidence).toHaveBeenCalledTimes(1)
      expect(windowRead).not.toHaveBeenCalled()
      expect(local).not.toHaveBeenCalled()
      expect(log).not.toHaveBeenCalled()
    })
  })

  describe('Codex account-usage stop evidence', () => {
    // A Codex attempt is a Planner with the Proposal contract, a Claude attempt an Implementer: the evidence repeats the selected row's identity.
    const identityOf = (provider: string) =>
      provider === 'Codex'
        ? { role: 'Planner', provider, responseContract: 'Proposal' }
        : { role: 'Implementer', provider, responseContract: 'ImplementationReport' }
    const stoppedEvidence = (overrides: Record<string, unknown>, provider = 'Codex') => ({
      ...evidenceFor(1),
      ...identityOf(provider),
      attemptStatus: 'Failed',
      outcome: 'AccountUsageStopReached',
      ...overrides,
    })

    async function openDetail(provider = 'Codex') {
      history.mockResolvedValue({ items: [entry(1, identityOf(provider))], hasMore: false })
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      openHistory()
      await screen.findByText(/Attempt #1/)
      fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
      return screen.findByRole('region', { name: 'Selected Agent attempt evidence' })
    }

    it('shows the stored decision with the retrieval time and windows for a stopped Codex attempt', async () => {
      const retrievedAtUtc = new Date('2026-10-01T12:30:00Z')
      evidence.mockResolvedValue(
        stoppedEvidence({
          accountUsageStop: { state: 'Configured', percent: 80 },
          accountUsageDecision: {
            state: 'Recorded',
            decision: 'Reached',
            reason: 'ThresholdReached',
            thresholdPercent: 80,
            retrievedAtUtc,
            windows: [{ bucketId: 'codex', window: 'Primary', usedPercent: 85 }],
          },
        }),
      )

      const detail = await openDetail()
      const fact = await within(detail).findByRole('region', { name: 'Codex account-usage stop' })

      expect(within(fact).getByText('Claimed with account-usage stop: 80% used')).toBeTruthy()
      expect(within(fact).getByText('Not started: a reported usage window reached the configured stop (80%).')).toBeTruthy()
      expect(within(fact).getByText(`Host retrieval time: ${retrievedAtUtc.toLocaleString()}`)).toBeTruthy()
      expect(within(fact).getByText('codex primary window: 85% used')).toBeTruthy()
    })

    it('shows no decision line for an attempt without a stored decision', async () => {
      evidence.mockResolvedValue(stoppedEvidence({ outcome: 'Proposed', accountUsageStop: { state: 'NotConfigured' } }))

      const detail = await openDetail()
      const fact = await within(detail).findByRole('region', { name: 'Codex account-usage stop' })

      expect(within(fact).getByText('Claimed with no account-usage stop')).toBeTruthy()
      expect(fact.textContent).not.toMatch(/Not started/)
    })

    it('shows an unverifiable message for an Unavailable decision', async () => {
      evidence.mockResolvedValue(stoppedEvidence({ accountUsageDecision: { state: 'Unavailable', windows: [] } }))

      const detail = await openDetail()

      expect(await within(detail).findByText('The stored account-usage decision could not be verified.')).toBeTruthy()
    })

    it('shows nothing for a Claude attempt', async () => {
      evidence.mockResolvedValue(
        stoppedEvidence({ accountUsageStop: { state: 'Configured', percent: 80 }, accountUsageDecision: { state: 'Unavailable', windows: [] } }, 'ClaudeCode'),
      )

      const detail = await openDetail('ClaudeCode')
      await within(detail).findByText(/Attempt #1 · Implementer · ClaudeCode/)

      expect(within(detail).queryByRole('region', { name: 'Codex account-usage stop' })).toBeNull()
    })
  })
})
