// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SetClaudeModelPreferenceResponse } from '../../../api/generated/api-client'
import { setClaudeModelPreferenceClient } from '../../../api/clients'
import { useSetClaudeModelPreference } from './useSetClaudeModelPreference'

vi.mock('../../../api/clients', () => ({
  setClaudeModelPreferenceClient: vi.fn(),
}))

describe('useSetClaudeModelPreference', () => {
  it('saves an alias', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue(new SetClaudeModelPreferenceResponse({ requestedModel: 'opus' }))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)

    const { result } = renderHook(() => useSetClaudeModelPreference())

    let succeeded = false
    await act(async () => {
      succeeded = await result.current.save('run-1', 'opus')
    })

    expect(succeeded).toBe(true)
    expect(setClaudeModelPreference).toHaveBeenCalledWith('run-1', expect.objectContaining({ requestedModel: 'opus' }))
    expect(result.current.saving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('clears the request by sending undefined', async () => {
    const setClaudeModelPreference = vi.fn().mockResolvedValue(new SetClaudeModelPreferenceResponse({ requestedModel: undefined }))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)

    const { result } = renderHook(() => useSetClaudeModelPreference())

    await act(async () => {
      await result.current.save('run-1', null)
    })

    expect(setClaudeModelPreference).toHaveBeenCalledWith('run-1', expect.objectContaining({ requestedModel: undefined }))
  })

  it('reports a safe error message without leaking the underlying failure', async () => {
    const setClaudeModelPreference = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference } as never)

    const { result } = renderHook(() => useSetClaudeModelPreference())

    let succeeded = true
    await act(async () => {
      succeeded = await result.current.save('run-1', 'sonnet')
    })

    expect(succeeded).toBe(false)
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.error).not.toContain('sensitive detail')
    expect(result.current.saving).toBe(false)
  })
})
