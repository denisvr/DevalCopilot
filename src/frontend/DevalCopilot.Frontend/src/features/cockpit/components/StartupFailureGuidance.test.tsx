// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { toStartupFailure } from '../../../api/startupDiagnostics'
import { StartupFailureGuidance } from './StartupFailureGuidance'
import { StartupWarningSummary } from './StartupWarningSummary'

describe('StartupFailureGuidance (ADR-0072)', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows storage guidance, the limits and the copyable safe diagnostic without any session or request', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    const failure = toStartupFailure({ kind: 'startup-failed', stage: 'StorageSchema', reason: 'StorageUnavailable', action: 'RestoreStorageAccess' })

    render(<StartupFailureGuidance failure={failure!} />)

    expect(screen.getByText(/free disk space, then reopen the application once/)).toBeTruthy()
    expect(screen.getByText(/Do not edit or delete the database/)).toBeTruthy()
    const field = screen.getByLabelText('Safe startup diagnostic') as HTMLInputElement
    expect(field.value).toBe('DCSF1-StorageSchema-StorageUnavailable-RestoreStorageAccess')
    expect(field.readOnly).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Copy diagnostic' }))
    expect(await screen.findByText('Copied.')).toBeTruthy()
    expect(writeText).toHaveBeenCalledWith('DCSF1-StorageSchema-StorageUnavailable-RestoreStorageAccess')
    expect(fetchSpy).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /retry|repair|reset|release/i })).toBeNull()
  })

  it('asks for a reviewed repair for incoherent state and never offers an automatic action', () => {
    const failure = toStartupFailure({ kind: 'startup-failed', stage: 'AgentAttempts', reason: 'Unclassified', action: 'RequestReviewedRepair' })

    render(<StartupFailureGuidance failure={failure!} />)

    expect(screen.getByText(/Keep the application data and every project worktree exactly as they are/)).toBeTruthy()
    expect(screen.getAllByRole('button')).toHaveLength(1)
  })

  it('summarizes closed startup warnings once and shows nothing without any', () => {
    const { rerender, container } = render(<StartupWarningSummary warnings={[]} />)
    expect(container.textContent).toBe('')

    rerender(<StartupWarningSummary warnings={['OptionalOutputDeferred', 'SupervisionObservationsUnclosed']} />)
    expect(screen.getByRole('status', { name: 'Startup warnings' })).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText(/Nothing was released or marked ready/)).toBeTruthy()
  })
})
