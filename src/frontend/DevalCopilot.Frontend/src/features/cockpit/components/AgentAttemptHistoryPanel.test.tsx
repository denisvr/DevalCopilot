// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
} from '../../../api/clients'
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
  artifacts: purposes.map((purpose) => ({ purpose, byteLength: 12, truncated: false, captureOutcome: 'Captured' })),
})

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
      items: [entry(2, { identityValid: false, role: undefined, provider: undefined }), entry(1)],
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
      .mockResolvedValueOnce({ items: [entry(4), entry(3)], hasMore: true, nextBeforeAttemptNumber: 3 })
      .mockResolvedValueOnce({ items: [entry(2), entry(1)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()
    await screen.findByText(/Attempt #4/)

    fireEvent.click(screen.getByRole('button', { name: 'Load older attempts' }))

    await screen.findByText(/Attempt #1 /)
    expect(history).toHaveBeenNthCalledWith(2, 'run-1', 3, 10)
    const rows = screen.getAllByRole('listitem').map((row) => row.textContent ?? '')
    expect(rows.map((row) => /Attempt #(\d+)/.exec(row)?.[1])).toEqual(['4', '3', '2', '1'])
    expect(screen.queryByRole('button', { name: 'Load older attempts' })).toBeNull()
  })

  it('retries a failed first page and a failed later page at the same cursor while keeping loaded rows', async () => {
    history
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ items: [entry(3)], hasMore: true, nextBeforeAttemptNumber: 3 })
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ items: [entry(2)], hasMore: false })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    openHistory()

    expect(await screen.findByText('The Agent attempt history could not be loaded.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByText(/Attempt #3/)
    expect(history).toHaveBeenNthCalledWith(2, 'run-1', null, 10)

    fireEvent.click(screen.getByRole('button', { name: 'Load older attempts' }))
    expect(await screen.findByText('The Agent attempt history could not be loaded.')).toBeTruthy()
    expect(screen.getByText(/Attempt #3/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await screen.findByText(/Attempt #2/)
    expect(history).toHaveBeenNthCalledWith(3, 'run-1', 3, 10)
    expect(history).toHaveBeenNthCalledWith(4, 'run-1', 3, 10)
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
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
    expect(windowRead).toHaveBeenCalledWith('run-1', 'attempt-1', 'AgentStandardError', 0, 16384)
  })

  it('renders markup-shaped artifact text literally and never as elements', async () => {
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script> **not bold**'
    history.mockResolvedValue({ items: [entry(1)], hasMore: false })
    evidence.mockResolvedValue(evidenceFor(1))
    windowRead.mockResolvedValue({ status: 'Ok', text: hostile, nextOffset: 60, totalLengthSoFar: 60, truncated: false })
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
})
