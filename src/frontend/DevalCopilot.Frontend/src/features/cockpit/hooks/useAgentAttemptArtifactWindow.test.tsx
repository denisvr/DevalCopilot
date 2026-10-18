import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptArtifactWindowClient } from '../../../api/clients'
import type { SealedAgentArtifactWindowResponse } from '../../../api/generated/api-client'
import { attemptIdFor, windowOf } from '../agentAttemptHistoryFixture'
import { useAgentAttemptArtifactWindow } from './useAgentAttemptArtifactWindow'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptArtifactWindowClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type Read = (runId: string, attemptId: string, segment: string, fromOffset: number | undefined) => Promise<SealedAgentArtifactWindowResponse>

function install(read: Read) {
  const getAgentAttemptArtifactWindow = vi.fn(read)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({
    getAgentAttemptArtifactWindow,
  } as unknown as ReturnType<typeof agentAttemptArtifactWindowClient>)
  return getAgentAttemptArtifactWindow
}

interface Props {
  selectionKey: string
  runId: string
  attemptNumber: number
  purpose: string | null
}

function mounted(initial: Props) {
  return renderHook(
    (props: Props) =>
      useAgentAttemptArtifactWindow(
        { selectionKey: props.selectionKey, runId: props.runId, attemptId: attemptIdFor(props.runId, props.attemptNumber) },
        props.purpose,
      ),
    { initialProps: initial },
  )
}

const base: Props = { selectionKey: 'selection-1', runId: 'run-a', attemptNumber: 4, purpose: 'AgentContextManifest' }
const textOf = (state: { status: string; text?: string }) => (state.status === 'loaded' ? state.text : null)

describe('useAgentAttemptArtifactWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is idle until loaded explicitly, then requests the selected run, attempt and route segment from offset 0', async () => {
    const get = install(() => Promise.resolve(windowOf('manifest')))
    const { result } = mounted(base)

    expect(result.current.state).toEqual({ status: 'idle' })
    expect(get).not.toHaveBeenCalled()
    act(() => result.current.loadNextWindow())

    await waitFor(() => expect(textOf(result.current.state)).toBe('manifest'))
    expect(get.mock.calls).toEqual([['run-a', attemptIdFor('run-a', 4), 'context-manifest', 0, 16384]])
  })

  it('requests nothing for a purpose outside the allowlist and reports the window unavailable', async () => {
    const get = install(() => Promise.resolve(windowOf('x')))
    const { result } = mounted({ ...base, purpose: 'ProcessStandardOutput' })

    act(() => result.current.loadNextWindow())

    await waitFor(() => expect(result.current.state.status).toBe('error'))
    expect(get).not.toHaveBeenCalled()
  })

  it('sends one request for calls in the same tick and appends later windows without duplication', async () => {
    const get = install((_run, _attempt, _segment, from) =>
      Promise.resolve(from === 0 ? windowOf('AAAA', 0, 8) : windowOf('BBBB', 4, 8)),
    )
    const { result } = mounted(base)

    act(() => {
      result.current.loadNextWindow()
      result.current.loadNextWindow()
    })
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAA'))
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAABBBB'))
    act(() => result.current.loadNextWindow())

    expect(get.mock.calls.map((call) => call[3])).toEqual([0, 4])
  })

  it('starts nothing from a loadNextWindow retained across a purpose change, and the late window of that purpose settles nothing', async () => {
    const slow = deferred<SealedAgentArtifactWindowResponse>()
    const get = install((_run, _attempt, segment) => (segment === 'stderr' ? slow.promise : Promise.resolve(windowOf('final'))))
    const { result, rerender } = mounted({ ...base, purpose: 'AgentStandardError' })
    const retained = result.current.loadNextWindow
    act(() => retained())

    rerender({ ...base, purpose: 'AgentFinalResponse' })
    expect(result.current.state).toEqual({ status: 'idle' })
    act(() => retained())
    expect(get).toHaveBeenCalledTimes(1)

    await act(async () => {
      slow.resolve(windowOf('stderr text'))
      await slow.promise
    })
    expect(result.current.state).toEqual({ status: 'idle' })
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('final'))
    expect(get.mock.calls.map((call) => call[2])).toEqual(['stderr', 'final-response'])
  })

  it('treats another selection of the same attempt and purpose (A to B to A) as a new lifetime with no text and no retained work', async () => {
    const get = install(() => Promise.resolve(windowOf('manifest')))
    const { result, rerender } = mounted(base)
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('manifest'))
    const retained = result.current.loadNextWindow

    rerender({ ...base, selectionKey: 'selection-2', attemptNumber: 3 })
    expect(result.current.state).toEqual({ status: 'idle' })
    rerender({ ...base, selectionKey: 'selection-3' })
    expect(result.current.state).toEqual({ status: 'idle' })
    act(() => retained())

    expect(get).toHaveBeenCalledTimes(1)
  })

  it('starts nothing after unmount and lets no late window settle', async () => {
    const slow = deferred<SealedAgentArtifactWindowResponse>()
    const get = install(() => slow.promise)
    const { result, unmount } = mounted(base)
    const retained = result.current.loadNextWindow
    act(() => retained())

    unmount()
    act(() => retained())
    await act(async () => {
      slow.resolve(windowOf('late'))
      await slow.promise
    })

    expect(get).toHaveBeenCalledTimes(1)
  })

  it('retries a failed later window at the same offset and keeps the earlier text', async () => {
    let reads = 0
    const get = install((_run, _attempt, _segment, from) => {
      reads += 1
      return reads === 2 ? Promise.reject(new Error('offline')) : Promise.resolve(from === 0 ? windowOf('AAAA', 0, 8) : windowOf('BBBB', 4, 8))
    })
    const { result } = mounted(base)
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAA'))
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(result.current.state.status).toBe('error'))

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAABBBB'))
    expect(get.mock.calls.map((call) => call[3])).toEqual([0, 4, 4])
  })
})
