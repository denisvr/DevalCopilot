// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CodexModelCatalogResponse, CodexModelCatalogEntryResponse } from '../../../api/generated/api-client'
import { codexModelCatalogClient } from '../../../api/clients'
import { useCodexModelCatalog } from './useCodexModelCatalog'

vi.mock('../../../api/clients', () => ({
  codexModelCatalogClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('useCodexModelCatalog', () => {
  it('reports loading before the initial request settles, then the observed catalog', async () => {
    const pending = deferred<CodexModelCatalogResponse>()
    const getCodexModelCatalog = vi.fn().mockReturnValue(pending.promise)
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    const { result } = renderHook(() => useCodexModelCatalog())

    expect(result.current.loading).toBe(true)
    expect(result.current.catalog).toBeNull()

    const response = new CodexModelCatalogResponse({
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
    })

    await act(async () => {
      pending.resolve(response)
      await pending.promise
    })

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.catalog?.status).toBe('Observed')
    expect(result.current.error).toBeNull()
  })

  it('reports a safe error message without leaking the underlying failure, and clears any prior observed catalog', async () => {
    const observedResponse = new CodexModelCatalogResponse({
      status: 'Observed',
      retrievedAtUtc: new Date('2026-09-28T12:00:00Z') as never,
      models: [new CodexModelCatalogEntryResponse({ id: 'gpt-6-sol', displayName: 'GPT-6 Sol' })],
    })
    const getCodexModelCatalog = vi.fn().mockResolvedValueOnce(observedResponse).mockRejectedValueOnce(new Error('sensitive detail'))
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    const { result } = renderHook(() => useCodexModelCatalog())
    await waitFor(() => expect(result.current.catalog?.status).toBe('Observed'))

    act(() => {
      result.current.refresh()
    })

    await waitFor(() => expect(result.current.error).toBe('Codex model catalog is unavailable.'))
    expect(result.current.error).not.toContain('sensitive detail')
    // A failed refresh must never leave the previous Observed status/retrieval time on screen.
    expect(result.current.catalog).toBeNull()
  })

  it('never fetches while not ready', () => {
    const getCodexModelCatalog = vi.fn()
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    renderHook(() => useCodexModelCatalog(false))

    expect(getCodexModelCatalog).not.toHaveBeenCalled()
  })

  it('refresh requests a fresh catalog', async () => {
    const getCodexModelCatalog = vi.fn().mockResolvedValue(
      new CodexModelCatalogResponse({ status: 'Unknown', retrievedAtUtc: undefined, models: [] }),
    )
    vi.mocked(codexModelCatalogClient).mockReturnValue({ getCodexModelCatalog } as never)

    const { result } = renderHook(() => useCodexModelCatalog())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(getCodexModelCatalog).toHaveBeenCalledTimes(1)

    act(() => {
      result.current.refresh()
    })

    await waitFor(() => expect(getCodexModelCatalog).toHaveBeenCalledTimes(2))
  })
})
