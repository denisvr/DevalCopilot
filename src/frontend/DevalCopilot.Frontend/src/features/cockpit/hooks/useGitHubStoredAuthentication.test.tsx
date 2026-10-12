import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubStoredAuthenticationClient } from '../../../api/clients'
import type { GitHubStoredAuthenticationResponse } from '../../../api/clients'
import { useGitHubStoredAuthentication } from './useGitHubStoredAuthentication'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubStoredAuthenticationClient: vi.fn(),
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

const AT = new Date('2026-10-12T09:00:00.000Z')

const answer = (status: string, observed: Date | null = AT) =>
  ({ host: 'github.com', context: 'StoredDefaultConfig', status, observedAtUtc: observed ?? undefined }) as unknown as GitHubStoredAuthenticationResponse

function install(read: () => Promise<GitHubStoredAuthenticationResponse>) {
  const getGitHubStoredAuthentication = vi.fn(read)
  vi.mocked(gitHubStoredAuthenticationClient).mockReturnValue(
    { getGitHubStoredAuthentication } as unknown as ReturnType<typeof gitHubStoredAuthenticationClient>,
  )
  return getGitHubStoredAuthentication
}

describe('useGitHubStoredAuthentication', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing on mount, on opening, on rerender or on a timer', () => {
    vi.useFakeTimers()
    try {
      const get = install(() => Promise.resolve(answer('Authenticated')))
      const { result, rerender } = renderHook(() => useGitHubStoredAuthentication())

      act(() => result.current.show())
      rerender()
      act(() => {
        vi.advanceTimersByTime(10 * 60 * 1000)
      })

      expect(get).not.toHaveBeenCalled()
      expect(result.current.open).toBe(true)
      expect(result.current.phase).toEqual({ kind: 'idle' })
    } finally {
      vi.useRealTimers()
    }
  })

  it('does nothing when checked while collapsed', () => {
    const get = install(() => Promise.resolve(answer('Authenticated')))
    const { result } = renderHook(() => useGitHubStoredAuthentication())

    act(() => result.current.check())

    expect(get).not.toHaveBeenCalled()
  })

  it('reads once per explicit check, shows pending without the earlier answer, and ignores a second activation in flight', async () => {
    const first = deferred<GitHubStoredAuthenticationResponse>()
    const get = install(() => first.promise)
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())

    act(() => result.current.check())
    act(() => result.current.check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(result.current.phase).toEqual({ kind: 'pending' })

    await act(async () => first.resolve(answer('Authenticated')))

    expect(result.current.phase).toEqual({ kind: 'result', answer: { status: 'Authenticated', observedAtUtc: AT } })
  })

  it('a recheck starts a new read, hides the previous answer while pending and replaces it', async () => {
    const second = deferred<GitHubStoredAuthenticationResponse>()
    const get = install(() => Promise.resolve(answer('NoActiveAccount')))
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    await act(async () => result.current.check())
    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'NoActiveAccount' } })

    get.mockReturnValueOnce(second.promise)
    act(() => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(answer('IssueReported')))

    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'IssueReported' } })
  })

  it('a rejected read is a read failure, a non-coherent answer is an invalid failure, and neither keeps an answer', async () => {
    const get = install(() => Promise.reject(new Error('boom')))
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())

    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'read' })

    get.mockResolvedValueOnce(answer('Authenticated', null))
    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockResolvedValueOnce(answer('Unknown', AT))
    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
  })

  it('closing while pending discards the late answer, and a reopened lifetime starts unchecked', async () => {
    const late = deferred<GitHubStoredAuthenticationResponse>()
    install(() => late.promise)
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    act(() => result.current.check())

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    act(() => result.current.show())
    expect(result.current.phase).toEqual({ kind: 'idle' })

    await act(async () => late.resolve(answer('Authenticated')))

    expect(result.current.phase).toEqual({ kind: 'idle' })
  })

  it('a late answer or failure of a closed lifetime never settles the check of the reopened one', async () => {
    const stale = deferred<GitHubStoredAuthenticationResponse>()
    const current = deferred<GitHubStoredAuthenticationResponse>()
    const get = install(() => stale.promise)
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    act(() => result.current.check())
    act(() => result.current.close())
    act(() => result.current.show())
    get.mockReturnValueOnce(current.promise)
    act(() => result.current.check())

    await act(async () => stale.reject(new Error('late')))
    expect(result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => current.resolve(answer('IssueReported')))

    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'IssueReported' } })
  })

  it('a late settlement of a closed lifetime never clears the in-flight guard of the reopened one', async () => {
    const stale = deferred<GitHubStoredAuthenticationResponse>()
    const current = deferred<GitHubStoredAuthenticationResponse>()
    const get = install(() => stale.promise)
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    act(() => result.current.check())
    act(() => result.current.close())
    act(() => result.current.show())
    get.mockReturnValueOnce(current.promise)
    act(() => result.current.check())
    expect(get).toHaveBeenCalledTimes(2)

    await act(async () => stale.resolve(answer('Authenticated')))
    act(() => result.current.check())

    expect(get).toHaveBeenCalledTimes(2)
    await act(async () => current.resolve(answer('NoActiveAccount')))
    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'NoActiveAccount' } })
  })

  it('a retained close or check of an ended lifetime starts and closes nothing of its successor', async () => {
    const get = install(() => Promise.resolve(answer('Authenticated')))
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    const retained = result.current
    act(() => result.current.close())
    act(() => result.current.show())

    act(() => retained.close())
    expect(result.current.open).toBe(true)

    act(() => retained.check())
    expect(get).not.toHaveBeenCalled()
  })

  it('a retained close queued in the same batch as a reopening cannot close the reopened lifetime', () => {
    install(() => Promise.resolve(answer('Authenticated')))
    const { result } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    const retained = result.current

    act(() => {
      retained.close()
      result.current.show()
      retained.close()
    })

    expect(result.current.open).toBe(true)
  })

  it('unmounting while pending leaves the late settlement inert', async () => {
    const late = deferred<GitHubStoredAuthenticationResponse>()
    const get = install(() => late.promise)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { result, unmount } = renderHook(() => useGitHubStoredAuthentication())
    act(() => result.current.show())
    act(() => result.current.check())

    unmount()
    await act(async () => late.resolve(answer('Authenticated')))

    expect(get).toHaveBeenCalledTimes(1)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})
