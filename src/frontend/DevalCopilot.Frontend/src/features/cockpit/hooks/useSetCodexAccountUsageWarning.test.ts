// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException } from '../../../api/generated/api-client'
import { setCodexAccountUsageWarningClient } from '../../../api/clients'
import { ACCOUNT_USAGE_WARNING_VALIDATION_MESSAGE, useSetCodexAccountUsageWarning } from './useSetCodexAccountUsageWarning'

vi.mock('../../../api/clients', () => ({
  setCodexAccountUsageWarningClient: vi.fn(),
}))

function mockClient(impl: (...args: unknown[]) => Promise<unknown>) {
  const setCodexAccountUsageWarning = vi.fn(impl)
  vi.mocked(setCodexAccountUsageWarningClient).mockReturnValue({ setCodexAccountUsageWarning } as never)
  return setCodexAccountUsageWarning
}

const serialized = (mock: ReturnType<typeof vi.fn>, call = 0) => JSON.stringify(mock.mock.calls[call][1])

beforeEach(() => {
  vi.mocked(setCodexAccountUsageWarningClient).mockReset()
})

describe('useSetCodexAccountUsageWarning', () => {
  it('exports the validation message', () => {
    expect(ACCOUNT_USAGE_WARNING_VALIDATION_MESSAGE).toBe('Enter a whole number from 1 to 100.')
  })

  it('sets a percentage and serializes the number', async () => {
    const client = mockClient(async () => ({ percent: 80 }))
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    let ok = false
    await act(async () => {
      ok = await result.current.save('80')
    })

    expect(ok).toBe(true)
    expect(client).toHaveBeenCalledWith('run-1', expect.anything())
    expect(serialized(client)).toBe('{"percent":80}')
    expect(result.current.error).toBeNull()
  })

  it('clears with an explicit JSON null, never a dropped member', async () => {
    const client = mockClient(async () => ({}))
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    await act(async () => {
      await result.current.clear()
    })
    await act(async () => {
      await result.current.save('')
    })

    expect(serialized(client, 0)).toBe('{"percent":null}')
    expect(serialized(client, 1)).toBe('{"percent":null}')
  })

  it.each(['0', '-1', '101', '3.5', '1e2', '+5', '  ', 'abc', '007'])('rejects %j locally without calling the server', async (draft) => {
    const client = mockClient(async () => ({}))
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    let ok = true
    await act(async () => {
      ok = await result.current.save(draft)
    })

    expect(ok).toBe(false)
    expect(client).not.toHaveBeenCalled()
    expect(result.current.error).toBe(ACCOUNT_USAGE_WARNING_VALIDATION_MESSAGE)
  })

  it('is pending during the call and ignores a duplicate submission', async () => {
    let release: (value: unknown) => void = () => undefined
    const client = mockClient(() => new Promise((resolve) => (release = resolve)))
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    let first: Promise<boolean> = Promise.resolve(false)
    let second = true
    await act(async () => {
      first = result.current.save('50')
      second = await result.current.save('60')
    })

    expect(result.current.saving).toBe(true)
    expect(second).toBe(false)
    expect(client).toHaveBeenCalledTimes(1)

    await act(async () => {
      release({})
      await first
    })
    expect(result.current.saving).toBe(false)
  })

  it.each([
    [400, 'The account-usage warning must be a whole number from 1 to 100.'],
    [404, 'This run could not be found.'],
    [409, 'The run changed concurrently, or it does not allow this. Reload the run and retry.'],
    [422, "This run's Codex account-usage warning can no longer be changed."],
    [500, 'The Codex account-usage warning could not be saved for this run.'],
  ])('maps HTTP %i to a fixed safe message and never echoes server text', async (status, message) => {
    mockClient(async () => {
      throw new ApiException('sensitive server text', status, '{"errors":[{"detail":"sensitive server text"}]}', {}, null)
    })
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    let ok = true
    await act(async () => {
      ok = await result.current.save('50')
    })

    expect(ok).toBe(false)
    expect(result.current.error).toBe(message)
    expect(result.current.error).not.toContain('sensitive')
  })

  it('uses the generic message for a non-API failure', async () => {
    mockClient(async () => {
      throw new Error('sensitive detail')
    })
    const { result } = renderHook(() => useSetCodexAccountUsageWarning('run-1', 'a'))

    await act(async () => {
      await result.current.save('50')
    })

    await waitFor(() => expect(result.current.error).toBe('The Codex account-usage warning could not be saved for this run.'))
  })

  it('drops the error of the previous run on a run switch', async () => {
    mockClient(async () => {
      throw new Error('boom')
    })
    const { result, rerender } = renderHook(({ runId }) => useSetCodexAccountUsageWarning(runId, 'a'), {
      initialProps: { runId: 'run-1' },
    })

    await act(async () => {
      await result.current.save('50')
    })
    expect(result.current.error).not.toBeNull()

    rerender({ runId: 'run-2' })

    expect(result.current.error).toBeNull()
    expect(result.current.saving).toBe(false)
  })
})

describe('useSetCodexAccountUsageWarning owner identity (run plus authoritative setting)', () => {
  function deferred() {
    const calls: { resolve: () => void; reject: (reason: unknown) => void }[] = []
    const client = mockClient(() => new Promise((resolve, reject) => calls.push({ resolve: () => resolve({}), reject })))
    return { calls, client }
  }
  const mount = () => renderHook(({ identity }) => useSetCodexAccountUsageWarning('run-1', identity), { initialProps: { identity: 'a' } })

  it('does not carry pending state to a replacement setting of the same run, and an obsolete rejection stays inert', async () => {
    const { calls } = deferred()
    const { result, rerender } = mount()
    let old: Promise<boolean> = Promise.resolve(true)
    act(() => {
      old = result.current.save('80')
    })
    expect(result.current.saving).toBe(true)

    rerender({ identity: 'b' })
    expect(result.current.saving).toBe(false)

    await act(async () => {
      calls[0].reject(new Error('boom'))
      expect(await old).toBe(false)
    })
    expect(result.current.error).toBeNull()
    expect(result.current.saving).toBe(false)
  })

  it('an obsolete finally cannot release a replacement request', async () => {
    const { calls, client } = deferred()
    const { result, rerender } = mount()
    let old: Promise<boolean> = Promise.resolve(true)
    act(() => {
      old = result.current.save('80')
    })
    rerender({ identity: 'b' })
    let replacement: Promise<boolean> = Promise.resolve(true)
    act(() => {
      replacement = result.current.save('90')
    })
    expect(client).toHaveBeenCalledTimes(2)
    expect(result.current.saving).toBe(true)

    await act(async () => {
      calls[0].resolve()
      expect(await old).toBe(false)
    })
    expect(result.current.saving).toBe(true)
    let duplicate = true
    await act(async () => {
      duplicate = await result.current.save('91')
    })
    expect(duplicate).toBe(false)
    expect(client).toHaveBeenCalledTimes(2)

    await act(async () => {
      calls[1].resolve()
      expect(await replacement).toBe(true)
    })
    expect(result.current.saving).toBe(false)
  })

  it('A -> B -> A is a new owner: the returning owner inherits neither pending nor error', async () => {
    const { calls } = deferred()
    const { result, rerender } = mount()
    let old: Promise<boolean> = Promise.resolve(true)
    act(() => {
      old = result.current.save('80')
    })
    rerender({ identity: 'b' })
    rerender({ identity: 'a' })
    expect(result.current.saving).toBe(false)

    await act(async () => {
      calls[0].reject(new Error('boom'))
      expect(await old).toBe(false)
    })
    expect(result.current.error).toBeNull()
    expect(result.current.saving).toBe(false)
  })

  it('a retained callback from an obsolete owner is inert', async () => {
    const client = mockClient(async () => ({}))
    const { result, rerender } = mount()
    const retained = result.current
    rerender({ identity: 'b' })

    let saved = true
    let cleared = true
    await act(async () => {
      saved = await retained.save('80')
      cleared = await retained.clear()
    })
    expect(saved).toBe(false)
    expect(cleared).toBe(false)
    expect(client).not.toHaveBeenCalled()
    expect(result.current.saving).toBe(false)
  })

  it('a retained validation reporter from an obsolete owner shows nothing on the replacement', async () => {
    mockClient(async () => ({}))
    const { result, rerender } = mount()
    const retained = result.current
    rerender({ identity: 'b' })

    await act(async () => {
      await retained.save('abc')
    })
    expect(result.current.error).toBeNull()
  })

  it('an unmounted owner ignores a late resolution without a state-update warning', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { calls } = deferred()
    const { result, unmount } = mount()
    let old: Promise<boolean> = Promise.resolve(true)
    act(() => {
      old = result.current.save('80')
    })
    unmount()
    await act(async () => {
      calls[0].resolve()
      expect(await old).toBe(false)
    })
    expect(errorSpy).not.toHaveBeenCalled()
    errorSpy.mockRestore()
  })

  it('keeps same-owner duplicate protection and safe failure unchanged', async () => {
    const { calls, client } = deferred()
    const { result } = mount()
    let first: Promise<boolean> = Promise.resolve(false)
    let second = true
    await act(async () => {
      first = result.current.save('50')
      second = await result.current.save('60')
    })
    expect(second).toBe(false)
    expect(client).toHaveBeenCalledTimes(1)
    await act(async () => {
      calls[0].reject(new Error('boom'))
      expect(await first).toBe(false)
    })
    expect(result.current.error).toBe('The Codex account-usage warning could not be saved for this run.')
    expect(result.current.saving).toBe(false)
  })
})
