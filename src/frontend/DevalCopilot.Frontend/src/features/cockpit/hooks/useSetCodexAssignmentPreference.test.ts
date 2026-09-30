// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SetCodexAssignmentPreferenceResponse } from '../../../api/generated/api-client'
import { setCodexAssignmentPreferenceClient } from '../../../api/clients'
import { useSetCodexAssignmentPreference } from './useSetCodexAssignmentPreference'

vi.mock('../../../api/clients', () => ({
  setCodexAssignmentPreferenceClient: vi.fn(),
}))

describe('useSetCodexAssignmentPreference', () => {
  it('saves a model and effort, then triggers onSaved', async () => {
    const setCodexAssignmentPreference = vi.fn().mockResolvedValue(
      new SetCodexAssignmentPreferenceResponse({ requestedModel: 'gpt-6-sol', requestedEffort: 'high' }),
    )
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)
    const onSaved = vi.fn()

    const { result } = renderHook(() => useSetCodexAssignmentPreference('run-1', onSaved))

    let succeeded = false
    await act(async () => {
      succeeded = await result.current.save('run-1', 'gpt-6-sol', 'high')
    })

    expect(succeeded).toBe(true)
    expect(setCodexAssignmentPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: 'gpt-6-sol', requestedEffort: 'high' }),
    )
    expect(onSaved).toHaveBeenCalledTimes(1)
    expect(result.current.saving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('clears the preference by sending undefined for both fields', async () => {
    const setCodexAssignmentPreference = vi.fn().mockResolvedValue(
      new SetCodexAssignmentPreferenceResponse({ requestedModel: undefined, requestedEffort: undefined }),
    )
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)

    const { result } = renderHook(() => useSetCodexAssignmentPreference('run-1', () => {}))

    await act(async () => {
      await result.current.save('run-1', null, null)
    })

    expect(setCodexAssignmentPreference).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ requestedModel: undefined, requestedEffort: undefined }),
    )
  })

  it('reports a safe error message without leaking the underlying failure', async () => {
    const setCodexAssignmentPreference = vi.fn().mockRejectedValue(new Error('sensitive detail'))
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)
    const onSaved = vi.fn()

    const { result } = renderHook(() => useSetCodexAssignmentPreference('run-1', onSaved))

    let succeeded = true
    await act(async () => {
      succeeded = await result.current.save('run-1', 'gpt-6-sol', null)
    })

    expect(succeeded).toBe(false)
    expect(onSaved).not.toHaveBeenCalled()
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.error).not.toContain('sensitive detail')
    expect(result.current.saving).toBe(false)
  })
})
