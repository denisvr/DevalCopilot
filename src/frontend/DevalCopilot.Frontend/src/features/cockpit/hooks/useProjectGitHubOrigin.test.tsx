import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { projectGitHubOriginClient } from '../../../api/clients'
import type { ProjectGitHubOriginResponse } from '../../../api/clients'
import { useProjectGitHubOrigin } from './useProjectGitHubOrigin'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectGitHubOriginClient: vi.fn(),
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

const A = 'aaaaaaaa-0000-0000-0000-00000000000a'
const B = 'bbbbbbbb-0000-0000-0000-00000000000b'
const AT = new Date('2026-10-12T09:00:00.000Z')

const available = (projectId: string, owner = 'acme', repository = 'widgets') =>
  ({
    projectId,
    source: 'RepositoryLocalConfig',
    status: 'Available',
    observedAtUtc: AT,
    target: { host: 'github.com', owner, repository },
  }) as unknown as ProjectGitHubOriginResponse

const notConfigured = (projectId: string) =>
  ({ projectId, source: 'RepositoryLocalConfig', status: 'NotConfigured', observedAtUtc: AT }) as unknown as ProjectGitHubOriginResponse

function install(read: (projectId: string) => Promise<ProjectGitHubOriginResponse>) {
  const getProjectGitHubOrigin = vi.fn(read)
  vi.mocked(projectGitHubOriginClient).mockReturnValue(
    { getProjectGitHubOrigin } as unknown as ReturnType<typeof projectGitHubOriginClient>,
  )
  return getProjectGitHubOrigin
}

const mountFor = (initial: string | null) => renderHook(({ id }: { id: string | null }) => useProjectGitHubOrigin(id), { initialProps: { id: initial } })

describe('useProjectGitHubOrigin', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing on mount, on opening, on rerender, on a project change or on a timer', () => {
    vi.useFakeTimers()
    const get = install((id) => Promise.resolve(available(id)))
    const { result, rerender } = mountFor(A)

    act(() => result.current.show())
    rerender({ id: A })
    rerender({ id: B })
    rerender({ id: A })
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(get).not.toHaveBeenCalled()
  })

  it('is collapsed and idle for a newly committed project, and does nothing without a project', () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(null)

    act(() => result.current.show())
    act(() => result.current.check())

    expect(result.current.open).toBe(false)
    expect(result.current.phase).toEqual({ kind: 'idle' })
    expect(get).not.toHaveBeenCalled()
  })

  it('does nothing when checked while collapsed', () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)

    act(() => result.current.check())

    expect(get).not.toHaveBeenCalled()
  })

  it('reads the exact project once per explicit check, hides any answer while pending and ignores a second activation in flight', async () => {
    const first = deferred<ProjectGitHubOriginResponse>()
    const get = install(() => first.promise)
    const { result } = mountFor(A)
    act(() => result.current.show())

    act(() => result.current.check())
    act(() => result.current.check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith(A)
    expect(result.current.phase).toEqual({ kind: 'pending' })

    await act(async () => first.resolve(available(A)))

    expect(result.current.phase).toEqual({
      kind: 'result',
      answer: { projectId: A, status: 'Available', observedAtUtc: AT, target: { owner: 'acme', repository: 'widgets' } },
    })
  })

  it('two activations in the same batch send one request', async () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)
    act(() => result.current.show())

    await act(async () => {
      result.current.check()
      result.current.check()
    })

    expect(get).toHaveBeenCalledTimes(1)
  })

  it('a recheck starts a new read, hides the previous answer while pending and replaces it', async () => {
    const second = deferred<ProjectGitHubOriginResponse>()
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)
    act(() => result.current.show())
    await act(async () => result.current.check())
    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'Available' } })

    get.mockReturnValueOnce(second.promise)
    act(() => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(notConfigured(A)))

    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { status: 'NotConfigured', target: null } })
  })

  it('a rejected read is a read failure and an incoherent answer is an invalid failure; neither keeps an answer', async () => {
    const get = install(() => Promise.reject(new Error('boom')))
    const { result } = mountFor(A)
    act(() => result.current.show())

    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'read' })

    get.mockResolvedValueOnce({ ...available(A), target: undefined } as unknown as ProjectGitHubOriginResponse)
    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockResolvedValueOnce(available(B))
    await act(async () => result.current.check())
    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
  })

  it('closing while pending discards the late answer, and a reopened lifetime starts unchecked and reads nothing', async () => {
    const late = deferred<ProjectGitHubOriginResponse>()
    const get = install(() => late.promise)
    const { result } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    await act(async () => late.resolve(available(A)))
    act(() => result.current.show())

    expect(result.current.open).toBe(true)
    expect(result.current.phase).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('an answer that belongs to an ended opening never settles the reopened one', async () => {
    const first = deferred<ProjectGitHubOriginResponse>()
    const second = deferred<ProjectGitHubOriginResponse>()
    const get = install(() => (get.mock.calls.length === 1 ? first.promise : second.promise))
    const { result } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())
    act(() => result.current.close())
    act(() => result.current.show())
    act(() => result.current.check())

    await act(async () => first.resolve(available(A, 'stale', 'answer')))
    expect(result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => second.resolve(available(A, 'fresh', 'answer')))

    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { target: { owner: 'fresh' } } })
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('a late completion of an ended opening does not release the in-flight guard of its successor', async () => {
    const first = deferred<ProjectGitHubOriginResponse>()
    const second = deferred<ProjectGitHubOriginResponse>()
    const get = install(() => (get.mock.calls.length === 1 ? first.promise : second.promise))
    const { result } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())
    act(() => result.current.close())
    act(() => result.current.show())
    act(() => result.current.check())

    await act(async () => first.resolve(available(A)))
    act(() => result.current.check())

    expect(get).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'pending' })
  })

  it('a check and a close in the same batch leave a closed region and ignore the completion', async () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)
    act(() => result.current.show())

    await act(async () => {
      result.current.check()
      result.current.close()
    })
    expect(result.current.open).toBe(false)
    expect(result.current.phase).toEqual({ kind: 'idle' })
    act(() => result.current.show())

    expect(result.current.phase).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('a close retained from an ended opening closes nothing of its successor', () => {
    install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)
    act(() => result.current.show())
    const retainedClose = result.current.close
    act(() => result.current.close())
    act(() => result.current.show())

    act(() => retainedClose())

    expect(result.current.open).toBe(true)
  })

  it('a check retained from an ended opening starts nothing', () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result } = mountFor(A)
    act(() => result.current.show())
    const retainedCheck = result.current.check
    act(() => result.current.close())

    act(() => retainedCheck())

    expect(get).not.toHaveBeenCalled()
  })

  it('another project starts collapsed and unchecked and a late answer of the previous one is ignored', async () => {
    const late = deferred<ProjectGitHubOriginResponse>()
    const get = install(() => late.promise)
    const { result, rerender } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())

    rerender({ id: B })
    expect(result.current.open).toBe(false)
    expect(result.current.phase).toEqual({ kind: 'idle' })
    await act(async () => late.resolve(available(A)))
    act(() => result.current.show())

    expect(result.current.phase).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith(A)
  })

  it('returning to an earlier project is a new lifetime: collapsed, unchecked, and the first visit never settles it (A-B-A)', async () => {
    const firstVisit = deferred<ProjectGitHubOriginResponse>()
    const secondVisit = deferred<ProjectGitHubOriginResponse>()
    const get = install((id) => (get.mock.calls.length === 1 ? firstVisit.promise : id === A ? secondVisit.promise : Promise.resolve(available(id))))
    const { result, rerender } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())

    rerender({ id: B })
    rerender({ id: A })
    expect(result.current.open).toBe(false)
    expect(result.current.phase).toEqual({ kind: 'idle' })

    act(() => result.current.show())
    act(() => result.current.check())
    await act(async () => firstVisit.resolve(available(A, 'first', 'visit')))
    expect(result.current.phase).toEqual({ kind: 'pending' })
    await act(async () => secondVisit.resolve(available(A, 'second', 'visit')))

    expect(result.current.phase).toMatchObject({ kind: 'result', answer: { target: { owner: 'second' } } })
  })

  it('a check or close retained from another project starts and closes nothing for the current one', () => {
    const get = install((id) => Promise.resolve(available(id)))
    const { result, rerender } = mountFor(A)
    act(() => result.current.show())
    const retainedCheck = result.current.check
    const retainedClose = result.current.close

    rerender({ id: B })
    act(() => result.current.show())
    act(() => retainedCheck())
    act(() => retainedClose())

    expect(get).not.toHaveBeenCalled()
    expect(result.current.open).toBe(true)
  })

  it('an answer for one project is never shown for another even when the host echoes the wrong identity', async () => {
    install(() => Promise.resolve(available(B)))
    const { result } = mountFor(A)
    act(() => result.current.show())

    await act(async () => result.current.check())

    expect(result.current.phase).toEqual({ kind: 'failed', reason: 'invalid' })
  })

  it('unmounting while a read is pending leaves nothing to settle and logs no error', async () => {
    const late = deferred<ProjectGitHubOriginResponse>()
    install(() => late.promise)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { result, unmount } = mountFor(A)
    act(() => result.current.show())
    act(() => result.current.check())

    unmount()
    await act(async () => late.resolve(available(A)))

    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})
