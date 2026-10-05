// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  CodexAccountAllowanceResponse,
  CodexAccountUsageStopResponse,
  CodexAllowanceBucketResponse,
  CodexAllowanceWindowResponse,
  CodexModelCatalogResponse,
  CodexModelCatalogEntryResponse,
} from '../../../api/generated/api-client'
import { codexAccountAllowanceClient, codexModelCatalogClient } from '../../../api/clients'
import { UsageEvidenceRail } from './UsageEvidenceRail'

vi.mock('../../../api/clients', () => ({
  codexAccountAllowanceClient: vi.fn(),
  codexModelCatalogClient: vi.fn(),
}))

function stubUnknownCatalog() {
  const getCodexModelCatalog = vi.fn().mockResolvedValue(
    new CodexModelCatalogResponse({ status: 'Unknown', retrievedAtUtc: undefined, models: [] }),
  )
  vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)
  return getCodexModelCatalog
}

describe('UsageEvidenceRail', () => {
  it('shows a real, positive Codex account-allowance snapshot with its retrieval time', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({
        status: 'Observed',
        retrievedAtUtc: new Date('2026-09-27T12:00:00Z') as never,
        buckets: [
          new CodexAllowanceBucketResponse({
            limitId: 'codex',
            primary: new CodexAllowanceWindowResponse({ usedPercent: 42, windowDurationMins: 300, resetsAtUtc: new Date() as never }),
            secondary: new CodexAllowanceWindowResponse({ usedPercent: 10, windowDurationMins: 10080, resetsAtUtc: new Date() as never }),
          }),
        ],
      }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText(/Primary: 42%/)).toBeTruthy())
    expect(screen.getByText(/Secondary: 10%/)).toBeTruthy()
    expect(screen.getByText(/^Retrieved /)).toBeTruthy()
  })

  it('shows one line per bucket without combining them', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({
        status: 'Observed',
        retrievedAtUtc: new Date() as never,
        buckets: [
          new CodexAllowanceBucketResponse({
            limitId: 'codex',
            primary: new CodexAllowanceWindowResponse({ usedPercent: 42 }),
          }),
          new CodexAllowanceBucketResponse({
            limitId: 'gpt-5',
            primary: new CodexAllowanceWindowResponse({ usedPercent: 7 }),
          }),
        ],
      }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText(/codex/)).toBeTruthy())
    expect(screen.getByText(/gpt-5/)).toBeTruthy()
  })

  it('shows an explicit Unknown, never a zero, when the snapshot is unavailable', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText('Codex account usage: Unknown')).toBeTruthy())
    expect(screen.queryByText(/0%/)).toBeNull()
  })

  it('the refresh control requests a fresh snapshot', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)
    await waitFor(() => expect(getCodexAccountAllowance).toHaveBeenCalledTimes(1))

    fireEvent.click(screen.getByRole('button', { name: 'Refresh Codex account usage' }))

    await waitFor(() => expect(getCodexAccountAllowance).toHaveBeenCalledTimes(2))
  })

  it('removes old observed data and retrieval time when a refresh fails', async () => {
    const observed = new CodexAccountAllowanceResponse({
      status: 'Observed',
      retrievedAtUtc: new Date('2026-09-27T12:00:00Z') as never,
      buckets: [new CodexAllowanceBucketResponse({
        limitId: 'codex',
        primary: new CodexAllowanceWindowResponse({ usedPercent: 42 }),
      })],
    })
    const getCodexAccountAllowance = vi.fn().mockResolvedValueOnce(observed).mockRejectedValueOnce(new Error('private detail'))
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)
    await waitFor(() => expect(screen.getByText(/Primary: 42%/)).toBeTruthy())
    expect(screen.getByText(/^Retrieved /)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Refresh Codex account usage' }))
    await waitFor(() => expect(screen.getByText('Codex account usage: Unknown')).toBeTruthy())
    expect(screen.queryByText(/^Retrieved /)).toBeNull()
    expect(screen.getByLabelText('Usage and evidence').querySelector('[data-status="Observed"]')).toBeNull()
  })

  it('shows a real, positive Codex model catalog with supported and default reasoning effort', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    const getCodexModelCatalog = vi.fn().mockResolvedValue(
      new CodexModelCatalogResponse({
        status: 'Observed',
        retrievedAtUtc: new Date('2026-09-28T12:00:00Z') as never,
        models: [
          new CodexModelCatalogEntryResponse({
            id: 'gpt-6-sol',
            displayName: 'GPT-6 Sol',
            supportedReasoningEfforts: ['medium', 'high'],
            defaultReasoningEffort: 'medium',
          }),
        ],
      }),
    )
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText(/GPT-6 Sol/)).toBeTruthy())
    expect(screen.getByText(/medium, high/)).toBeTruthy()
    expect(screen.getByText(/default: medium/)).toBeTruthy()
  })

  it('shows an explicit Unknown for the model catalog, never an empty-looking success', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText('Codex model catalog: Unknown')).toBeTruthy())
  })

  it('the model catalog refresh control requests a fresh catalog', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    const getCodexModelCatalog = stubUnknownCatalog()

    render(<UsageEvidenceRail />)
    await waitFor(() => expect(getCodexModelCatalog).toHaveBeenCalledTimes(1))

    fireEvent.click(screen.getByRole('button', { name: 'Refresh Codex model catalog' }))

    await waitFor(() => expect(getCodexModelCatalog).toHaveBeenCalledTimes(2))
  })

  it('still shows the Claude account-usage placeholder unchanged', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)
    stubUnknownCatalog()

    render(<UsageEvidenceRail />)

    expect(await screen.findByText('Claude account usage: not yet collected in this increment.')).toBeTruthy()
  })

  it('offers the Agent attempt history only for a run, collapsed by default, and drops it when the rail collapses', async () => {
    const getCodexModelCatalog = stubUnknownCatalog()
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({
      getCodexAccountAllowance: vi.fn().mockResolvedValue(new CodexAccountAllowanceResponse({ status: 'Unknown', buckets: [] })),
    } as never)
    const { rerender } = render(<UsageEvidenceRail />)
    await waitFor(() => expect(getCodexModelCatalog).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: 'Show Agent attempt history' })).toBeNull()

    rerender(<UsageEvidenceRail runId="run-1" />)
    expect(screen.getByRole('button', { name: 'Show Agent attempt history' }).getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(screen.getByRole('button', { name: 'Collapse usage and evidence rail' }))
    expect(screen.queryByRole('button', { name: 'Show Agent attempt history' })).toBeNull()
  })

  describe('Codex account-usage stop control', () => {
    const stopSetting = new CodexAccountUsageStopResponse({ state: 'Configured', percent: 80 })

    function stubAllowance(status: 'Unknown' | 'Observed') {
      vi.mocked(codexAccountAllowanceClient).mockReturnValue({
        getCodexAccountAllowance: vi.fn().mockResolvedValue(
          new CodexAccountAllowanceResponse({
            status,
            retrievedAtUtc: status === 'Observed' ? (new Date('2026-09-27T12:00:00Z') as never) : undefined,
            buckets: [],
          }),
        ),
      } as never)
      stubUnknownCatalog()
    }

    it('mounts only with a run and a setting, separately from the allowance and the catalog', async () => {
      stubAllowance('Unknown')
      const { rerender } = render(<UsageEvidenceRail runId="run-1" />)
      await screen.findByText('Codex account usage: Unknown')
      expect(screen.queryByRole('group', { name: 'Codex account-usage stop' })).toBeNull()

      rerender(<UsageEvidenceRail codexAccountUsageStop={stopSetting} />)
      expect(screen.queryByRole('group', { name: 'Codex account-usage stop' })).toBeNull()

      rerender(<UsageEvidenceRail runId="run-1" codexAccountUsageStop={stopSetting} accountUsageStopEditable />)
      const group = screen.getByRole('group', { name: 'Codex account-usage stop' })
      expect(group.closest('.dc-codex-account-allowance')).toBeNull()
      expect(group.closest('.dc-codex-model-catalog')).toBeNull()
      expect(screen.getByText('Current run setting: 80% used')).toBeTruthy()
      const allowance = document.querySelector('.dc-codex-account-allowance') as Element
      const placeholder = screen.getByText('Claude account usage: not yet collected in this increment.')
      expect(allowance.compareDocumentPosition(group) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(group.compareDocumentPosition(placeholder) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })

    it('is not mounted while the rail is collapsed', async () => {
      stubAllowance('Unknown')
      render(<UsageEvidenceRail runId="run-1" codexAccountUsageStop={stopSetting} accountUsageStopEditable />)
      expect(screen.getByRole('group', { name: 'Codex account-usage stop' })).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: 'Collapse usage and evidence rail' }))

      expect(screen.queryByRole('group', { name: 'Codex account-usage stop' })).toBeNull()
    })

    it.each(['Unknown', 'Observed'] as const)('is not enabled or disabled by the %s allowance observation', async (status) => {
      stubAllowance(status)
      render(<UsageEvidenceRail runId="run-1" codexAccountUsageStop={stopSetting} accountUsageStopEditable />)
      await waitFor(() => expect((screen.getByRole('button', { name: 'Refresh Codex account usage' }) as HTMLButtonElement).disabled).toBe(false))

      expect((screen.getByLabelText('Codex account-usage stop percentage') as HTMLInputElement).disabled).toBe(false)
      expect((screen.getByRole('button', { name: 'Save Codex account-usage stop' }) as HTMLButtonElement).disabled).toBe(false)
    })

    it('is read-only unless the caller says it is editable', () => {
      stubAllowance('Unknown')
      render(<UsageEvidenceRail runId="run-1" codexAccountUsageStop={stopSetting} />)

      expect(screen.queryByLabelText('Codex account-usage stop percentage')).toBeNull()
      expect(screen.getByText("This run's Codex account-usage stop can no longer be changed.")).toBeTruthy()
    })
  })
})
