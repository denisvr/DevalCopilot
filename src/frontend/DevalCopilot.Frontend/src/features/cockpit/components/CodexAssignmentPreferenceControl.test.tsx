// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  CodexModelCatalogResponse,
  CodexModelCatalogEntryResponse,
  SetCodexAssignmentPreferenceResponse,
} from '../../../api/generated/api-client'
import { codexModelCatalogClient, setCodexAssignmentPreferenceClient } from '../../../api/clients'
import { CodexAssignmentPreferenceControl } from './CodexAssignmentPreferenceControl'

vi.mock('../../../api/clients', () => ({
  codexModelCatalogClient: vi.fn(),
  setCodexAssignmentPreferenceClient: vi.fn(),
}))

function stubObservedCatalog() {
  const getCodexModelCatalog = vi.fn().mockResolvedValue(
    new CodexModelCatalogResponse({
      status: 'Observed',
      retrievedAtUtc: new Date() as never,
      models: [
        new CodexModelCatalogEntryResponse({
          id: 'gpt-6-sol',
          displayName: 'GPT-6 Sol',
          supportedReasoningEfforts: ['medium', 'high'],
          defaultReasoningEffort: 'medium',
        }),
        new CodexModelCatalogEntryResponse({ id: 'gpt-6-mini', displayName: 'GPT-6 Mini', supportedReasoningEfforts: ['low'] }),
      ],
    }),
  )
  vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)
}

describe('CodexAssignmentPreferenceControl', () => {
  it('shows no preference by default, never auto-selecting the catalog default', async () => {
    stubObservedCatalog()
    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel={null} requestedCodexEffort={null} />)

    await waitFor(() => expect(screen.getByLabelText('Requested Codex model')).toBeTruthy())
    expect(screen.getByText('No requested Codex model/effort for future claims.')).toBeTruthy()
    expect((screen.getByLabelText('Requested Codex model') as HTMLSelectElement).value).toBe('')
  })

  it('shows the currently requested model and effort', async () => {
    stubObservedCatalog()
    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel="gpt-6-sol" requestedCodexEffort="high" />)

    await waitFor(() =>
      expect(screen.getByText('Requested for future claims: gpt-6-sol (high).')).toBeTruthy(),
    )
  })

  it('shows an Unknown state when the catalog is unavailable', async () => {
    const getCodexModelCatalog = vi.fn().mockRejectedValue(new Error('unavailable'))
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel={null} requestedCodexEffort={null} />)

    await waitFor(() => expect(screen.getByText('Codex model catalog: Unknown')).toBeTruthy())
    expect(screen.queryByLabelText('Requested Codex model')).toBeNull()
  })

  it('saves a selected model and effort', async () => {
    stubObservedCatalog()
    const setCodexAssignmentPreference = vi.fn().mockResolvedValue(
      new SetCodexAssignmentPreferenceResponse({ requestedModel: 'gpt-6-sol', requestedEffort: 'high' }),
    )
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)

    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel={null} requestedCodexEffort={null} />)
    await waitFor(() => expect(screen.getByLabelText('Requested Codex model')).toBeTruthy())

    fireEvent.change(screen.getByLabelText('Requested Codex model'), { target: { value: 'gpt-6-sol' } })
    fireEvent.change(screen.getByLabelText('Requested Codex reasoning effort'), { target: { value: 'high' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(screen.getByText('Requested for future claims: gpt-6-sol (high).')).toBeTruthy(),
    )
    expect(setCodexAssignmentPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: 'gpt-6-sol', requestedEffort: 'high' }),
    )
  })

  it('clears a previously requested preference', async () => {
    stubObservedCatalog()
    const setCodexAssignmentPreference = vi.fn().mockResolvedValue(
      new SetCodexAssignmentPreferenceResponse({ requestedModel: undefined, requestedEffort: undefined }),
    )
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)

    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel="gpt-6-sol" requestedCodexEffort="high" />)
    await waitFor(() =>
      expect(screen.getByText('Requested for future claims: gpt-6-sol (high).')).toBeTruthy(),
    )

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(screen.getByText('No requested Codex model/effort for future claims.')).toBeTruthy())
    expect(setCodexAssignmentPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: undefined, requestedEffort: undefined }),
    )
  })

  it('shows a safe error message when saving fails, without discarding the prior displayed preference', async () => {
    stubObservedCatalog()
    const setCodexAssignmentPreference = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)

    render(<CodexAssignmentPreferenceControl runId="run-1" requestedCodexModel={null} requestedCodexEffort={null} />)
    await waitFor(() => expect(screen.getByLabelText('Requested Codex model')).toBeTruthy())

    fireEvent.change(screen.getByLabelText('Requested Codex model'), { target: { value: 'gpt-6-sol' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(screen.getByText('The Codex model/effort preference could not be saved for this run.')).toBeTruthy(),
    )
    expect(screen.getByText('No requested Codex model/effort for future claims.')).toBeTruthy()
  })
})
