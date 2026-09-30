// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException } from '../../../api/generated/api-client'
import { setClaudeMutationTurnLimitClient } from '../../../api/clients'
import { useSetClaudeMutationTurnLimit } from './useSetClaudeMutationTurnLimit'

vi.mock('../../../api/clients', () => ({
  setClaudeMutationTurnLimitClient: vi.fn(),
}))

function mockClient(impl: (...args: unknown[]) => Promise<unknown>) {
  const setClaudeMutationTurnLimit = vi.fn(impl)
  vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({ setClaudeMutationTurnLimit } as never)
  return setClaudeMutationTurnLimit
}

const serialized = (mock: ReturnType<typeof vi.fn>, call = 0) => JSON.stringify(mock.mock.calls[call][1])

beforeEach(() => {
  vi.mocked(setClaudeMutationTurnLimitClient).mockReset()
})

describe('useSetClaudeMutationTurnLimit', () => {
  it('sets a limit and serializes the number', async () => {
    const client = mockClient(async () => ({ maxTurns: 12 }))
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    let ok = false
    await act(async () => {
      ok = await result.current.save('12')
    })

    expect(ok).toBe(true)
    expect(client).toHaveBeenCalledWith('run-1', expect.anything())
    expect(serialized(client)).toBe('{"maxTurns":12}')
    expect(result.current.saving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('clears with an explicit JSON null, never a dropped member', async () => {
    const client = mockClient(async () => ({}))
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    await act(async () => {
      await result.current.clear()
    })
    await act(async () => {
      await result.current.save('')
    })

    expect(serialized(client, 0)).toBe('{"maxTurns":null}')
    expect(serialized(client, 1)).toBe('{"maxTurns":null}')
  })

  it.each(['0', '-1', '101', '3.5', '1e2', '+5', '  ', 'abc', '007'])('rejects %j locally without calling the server', async (draft) => {
    const client = mockClient(async () => ({}))
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    let ok = true
    await act(async () => {
      ok = await result.current.save(draft)
    })

    expect(ok).toBe(false)
    expect(client).not.toHaveBeenCalled()
    expect(result.current.error).toBe('Enter a whole number from 1 to 100.')
  })

  it('is pending during the call and ignores a double submit', async () => {
    let release: (value: unknown) => void = () => undefined
    const client = mockClient(() => new Promise((resolve) => (release = resolve)))
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    let first: Promise<boolean> = Promise.resolve(false)
    let second = true
    await act(async () => {
      first = result.current.save('5')
      second = await result.current.save('6')
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
    [400, 'The Claude turn limit must be a whole number from 1 to 100.'],
    [404, 'This run could not be found.'],
    [409, 'The Claude turn limit request changed concurrently. Reload the run and retry.'],
    [422, "This run's Claude turn limit request can no longer be changed."],
    [500, 'The Claude turn limit request could not be saved for this run.'],
  ])('maps HTTP %i to a fixed safe message and never echoes server text', async (status, message) => {
    mockClient(async () => {
      throw new ApiException('sensitive server text', status, '{"errors":[{"detail":"sensitive server text"}]}', {}, null)
    })
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    let ok = true
    await act(async () => {
      ok = await result.current.save('5')
    })

    expect(ok).toBe(false)
    expect(result.current.error).toBe(message)
    expect(result.current.error).not.toContain('sensitive')
    expect(result.current.saving).toBe(false)
  })

  it('uses the generic message for a non-API failure', async () => {
    mockClient(async () => {
      throw new Error('sensitive detail')
    })
    const { result } = renderHook(() => useSetClaudeMutationTurnLimit('run-1'))

    await act(async () => {
      await result.current.save('5')
    })

    await waitFor(() => expect(result.current.error).toBe('The Claude turn limit request could not be saved for this run.'))
  })

  it('drops the error and pending state of the previous run on a run switch', async () => {
    mockClient(async () => {
      throw new Error('boom')
    })
    const { result, rerender } = renderHook(({ runId }) => useSetClaudeMutationTurnLimit(runId), {
      initialProps: { runId: 'run-1' },
    })

    await act(async () => {
      await result.current.save('5')
    })
    expect(result.current.error).not.toBeNull()

    rerender({ runId: 'run-2' })

    expect(result.current.error).toBeNull()
    expect(result.current.saving).toBe(false)
  })
})
