// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SetClaudeModelPreferenceResponse } from '../../../api/generated/api-client'
import { setClaudeModelPreferenceClient } from '../../../api/clients'
import { ClaudeModelPreferenceControl } from './ClaudeModelPreferenceControl'

vi.mock('../../../api/clients', () => ({
  setClaudeModelPreferenceClient: vi.fn(),
}))

const NO_REQUEST_TEXT =
  'No Claude model requested for future attempts: no model argument will be passed. The model actually used is not observed.'

describe('ClaudeModelPreferenceControl', () => {
  it('offers exactly the closed aliases plus an explicit no-preference start, and labels the value a request', () => {
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel={null} />)

    const select = screen.getByLabelText('Requested Claude model') as HTMLSelectElement
    expect(select.value).toBe('')
    expect(Array.from(select.options).map((option) => option.value)).toEqual(['', 'sonnet', 'opus', 'haiku'])
    expect(screen.getByText(NO_REQUEST_TEXT)).toBeTruthy()
  })

  it('shows the current run request as a request only, never as an effective or observed model', () => {
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel="opus" />)

    expect(
      screen.getByText(
        'Requested Claude model for future attempts: opus. This is a request only; the model actually used is not observed.',
      ),
    ).toBeTruthy()
    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('opus')
  })

  it('saves a selected alias', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue(new SetClaudeModelPreferenceResponse({ requestedModel: 'haiku' }))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel={null} />)

    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'haiku' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.getByText(/Requested Claude model for future attempts: haiku\./)).toBeTruthy())
    expect(setClaudeModelPreference).toHaveBeenCalledWith('run-1', expect.objectContaining({ requestedModel: 'haiku' }))
  })

  it('clears a previously requested alias by sending no model', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue(new SetClaudeModelPreferenceResponse({ requestedModel: undefined }))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel="sonnet" />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(screen.getByText(NO_REQUEST_TEXT)).toBeTruthy())
    expect(setClaudeModelPreference).toHaveBeenCalledWith('run-1', expect.objectContaining({ requestedModel: undefined }))
    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('')
  })

  it('has nothing to clear when no request is set', () => {
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel={null} />)

    expect((screen.getByRole('button', { name: 'Clear' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('shows a safe error without leaking the failure and keeps the prior displayed request', async () => {
    const setClaudeModelPreference = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)
    render(<ClaudeModelPreferenceControl runId="run-1" requestedClaudeModel="sonnet" />)

    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'opus' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.getByText('The Claude model request could not be saved for this run.')).toBeTruthy())
    expect(screen.queryByText(/sensitive detail/)).toBeNull()
    expect(screen.getByText(/Requested Claude model for future attempts: sonnet\./)).toBeTruthy()
  })
})
