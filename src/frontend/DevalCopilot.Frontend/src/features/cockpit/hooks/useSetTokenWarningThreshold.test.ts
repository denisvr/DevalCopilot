// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SetTokenWarningThresholdResponse } from '../../../api/generated/api-client'
import { setTokenWarningThresholdClient } from '../../../api/clients'
import { useSetTokenWarningThreshold } from './useSetTokenWarningThreshold'

vi.mock('../../../api/clients', () => ({
  setTokenWarningThresholdClient: vi.fn(),
}))

describe('useSetTokenWarningThreshold', () => {
  it('saves one providers threshold', async () => {
    const setTokenWarningThreshold = vi.fn().mockResolvedValue(new SetTokenWarningThresholdResponse({ provider: 'Codex', thresholdTokens: 10 }))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    const { result } = renderHook(() => useSetTokenWarningThreshold('run-1'))

    let succeeded = false
    await act(async () => {
      succeeded = await result.current.save('run-1', 'Codex', 10)
    })

    expect(succeeded).toBe(true)
    expect(setTokenWarningThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: 10 }))
    expect(result.current.saving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('clears by sending undefined', async () => {
    const setTokenWarningThreshold = vi.fn().mockResolvedValue(new SetTokenWarningThresholdResponse({ provider: 'ClaudeCode' }))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    const { result } = renderHook(() => useSetTokenWarningThreshold('run-1'))

    await act(async () => {
      await result.current.save('run-1', 'ClaudeCode', null)
    })

    expect(setTokenWarningThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'ClaudeCode', thresholdTokens: undefined }))
  })

  it('reports a safe error message without leaking the underlying failure', async () => {
    const setTokenWarningThreshold = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
    const { result } = renderHook(() => useSetTokenWarningThreshold('run-1'))

    let succeeded = true
    await act(async () => {
      succeeded = await result.current.save('run-1', 'Codex', 5)
    })

    expect(succeeded).toBe(false)
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.error).not.toContain('sensitive detail')
  })
})
