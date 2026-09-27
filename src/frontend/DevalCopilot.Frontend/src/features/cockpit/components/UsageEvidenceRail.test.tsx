// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CodexAccountAllowanceResponse, CodexAllowanceBucketResponse, CodexAllowanceWindowResponse } from '../../../api/generated/api-client'
import { codexAccountAllowanceClient } from '../../../api/clients'
import { UsageEvidenceRail } from './UsageEvidenceRail'

vi.mock('../../../api/clients', () => ({
  codexAccountAllowanceClient: vi.fn(),
}))

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

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText(/codex/)).toBeTruthy())
    expect(screen.getByText(/gpt-5/)).toBeTruthy()
  })

  it('shows an explicit Unknown, never a zero, when the snapshot is unavailable', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    render(<UsageEvidenceRail />)

    await waitFor(() => expect(screen.getByText('Codex account usage: Unknown')).toBeTruthy())
    expect(screen.queryByText(/0%/)).toBeNull()
  })

  it('the refresh control requests a fresh snapshot', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    render(<UsageEvidenceRail />)
    await waitFor(() => expect(getCodexAccountAllowance).toHaveBeenCalledTimes(1))

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

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

    render(<UsageEvidenceRail />)
    await waitFor(() => expect(screen.getByText(/Primary: 42%/)).toBeTruthy())
    expect(screen.getByText(/^Retrieved /)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(screen.getByText('Codex account usage: Unknown')).toBeTruthy())
    expect(screen.queryByText(/^Retrieved /)).toBeNull()
    expect(screen.getByLabelText('Usage and evidence').querySelector('[data-status="Observed"]')).toBeNull()
  })

  it('still shows the Claude account-usage placeholder unchanged', async () => {
    const getCodexAccountAllowance = vi.fn().mockResolvedValue(
      new CodexAccountAllowanceResponse({ status: 'Unknown', retrievedAtUtc: undefined, buckets: [] }),
    )
    vi.mocked(codexAccountAllowanceClient).mockReturnValue({ getCodexAccountAllowance } as never)

    render(<UsageEvidenceRail />)

    expect(await screen.findByText('Claude account usage: not yet collected in this increment.')).toBeTruthy()
  })
})
