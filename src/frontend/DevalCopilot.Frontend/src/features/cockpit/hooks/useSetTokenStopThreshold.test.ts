// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiException, SetTokenStopThresholdResponse } from '../../../api/generated/api-client'
import { setTokenStopThresholdClient } from '../../../api/clients'
import { useSetTokenStopThreshold } from './useSetTokenStopThreshold'

vi.mock('../../../api/clients', () => ({
  setTokenStopThresholdClient: vi.fn(),
}))

describe('useSetTokenStopThreshold', () => {
  it('saves one providers threshold', async () => {
    const setTokenStopThreshold = vi.fn().mockResolvedValue(new SetTokenStopThresholdResponse({ provider: 'Codex', thresholdTokens: 10 }))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    const { result } = renderHook(() => useSetTokenStopThreshold())

    let succeeded = false
    await act(async () => {
      succeeded = await result.current.save('run-1', 'Codex', 10)
    })

    expect(succeeded).toBe(true)
    expect(setTokenStopThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: 10 }))
    expect(result.current.saving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('clears by sending undefined', async () => {
    const setTokenStopThreshold = vi.fn().mockResolvedValue(new SetTokenStopThresholdResponse({ provider: 'ClaudeCode' }))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    const { result } = renderHook(() => useSetTokenStopThreshold())

    await act(async () => {
      await result.current.save('run-1', 'ClaudeCode', null)
    })

    expect(setTokenStopThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'ClaudeCode', thresholdTokens: undefined }))
  })

  it('surfaces only the servers fixed problem detail for a refused change', async () => {
    const response = JSON.stringify({ errors: [{ code: 'token_stop.run_not_editable', detail: "This run's token stop thresholds can no longer be changed." }] })
    const setTokenStopThreshold = vi.fn().mockRejectedValue(new ApiException('Unprocessable', 422, response, {}, null))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    const { result } = renderHook(() => useSetTokenStopThreshold())

    await act(async () => {
      await result.current.save('run-1', 'Codex', 5)
    })

    expect(result.current.error).toBe("This run's token stop thresholds can no longer be changed.")
  })

  it('reports a safe error message without leaking the underlying failure', async () => {
    const setTokenStopThreshold = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    const { result } = renderHook(() => useSetTokenStopThreshold())

    let succeeded = true
    await act(async () => {
      succeeded = await result.current.save('run-1', 'Codex', 5)
    })

    expect(succeeded).toBe(false)
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.error).not.toContain('sensitive detail')
  })
})
