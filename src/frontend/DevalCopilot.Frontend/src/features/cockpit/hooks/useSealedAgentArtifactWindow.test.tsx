import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { sealedAgentArtifactWindowClient } from '../../../api/clients'
import { SealedAgentArtifactWindowResponse } from '../../../api/generated/api-client'
import { SEALED_WINDOW_MAX_BYTES } from './useSealedArtifactWindow'
import { useSealedAgentArtifactWindow } from './useSealedAgentArtifactWindow'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  sealedAgentArtifactWindowClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type Read = (runId: string, messageId: string, segment: string, fromOffset: number | undefined) => Promise<SealedAgentArtifactWindowResponse>

function install(read: Read) {
  const getSealedAgentArtifactWindow = vi.fn(read)
  vi.mocked(sealedAgentArtifactWindowClient).mockReturnValue({
    getSealedAgentArtifactWindow,
  } as unknown as ReturnType<typeof sealedAgentArtifactWindowClient>)
  return getSealedAgentArtifactWindow
}

const ok = (text: string, nextOffset: number, totalLengthSoFar: number, extra: Record<string, unknown> = {}) =>
  SealedAgentArtifactWindowResponse.fromJS({ status: 'Ok', text, nextOffset, totalLengthSoFar, truncated: false, ...extra })

interface Props {
  runId: string
  messageId: string
  purpose: string | null
}

function mounted(initial: Props) {
  return renderHook((props: Props) => useSealedAgentArtifactWindow(props.runId, props.messageId, props.purpose), { initialProps: initial })
}

const base: Props = { runId: 'run-a', messageId: 'message-1', purpose: 'AgentStandardError' }
const textOf = (state: { status: string; text?: string }) => (state.status === 'loaded' ? state.text : null)

describe('useSealedAgentArtifactWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each([
    ['AgentContextManifest', 'context-manifest'],
    ['AgentStandardOutput', 'stdout'],
    ['AgentStandardError', 'stderr'],
    ['AgentFinalResponse', 'final-response'],
  ])('requests %s through the message route segment %s for exactly its run and message', async (purpose, segment) => {
    const get = install(() => Promise.resolve(ok('text', 4, 4)))
    const { result } = mounted({ ...base, purpose })

    expect(get).not.toHaveBeenCalled()
    act(() => result.current.loadNextWindow())

    await waitFor(() => expect(textOf(result.current.state)).toBe('text'))
    expect(get.mock.calls).toEqual([['run-a', 'message-1', segment, 0, SEALED_WINDOW_MAX_BYTES]])
  })

  it.each(['ProcessStandardOutput', 'Unknown', 'toString', '__proto__'])('requests nothing for %s and reports an error', async (purpose) => {
    const get = install(() => Promise.resolve(ok('x', 1, 1)))
    const { result } = mounted({ ...base, purpose })

    act(() => result.current.loadNextWindow())

    await waitFor(() => expect(result.current.state.status).toBe('error'))
    expect(get).not.toHaveBeenCalled()
  })

  it('accumulates non-ASCII windows by the host byte cursor, including a deferred split codepoint and a replacement character', async () => {
    // Bytes: e-acute (2) + euro sign (3), cut before the four-byte emoji; then the emoji (4) and one malformed byte decoded as U+FFFD.
    const get = install((_run, _message, _segment, from) =>
      Promise.resolve(from === 0 ? ok('\u00E9\u20AC', 5, 10) : ok('\u{1F600}\uFFFD', 10, 10)),
    )
    const { result } = mounted(base)

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('\u00E9\u20AC'))
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('\u00E9\u20AC\u{1F600}\uFFFD'))

    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 10, totalLengthSoFar: 10, isComplete: true })
    expect(get.mock.calls.map((call) => call[3])).toEqual([0, 5])
  })

  it.each([
    ['an absent cursor', SealedAgentArtifactWindowResponse.fromJS({ status: 'Ok', text: 'BBBB', totalLengthSoFar: 8, truncated: false })],
    ['a backwards cursor', ok('BBBB', 2, 8)],
    ['an unmoved cursor', ok('', 4, 8)],
    ['an overshooting cursor', ok('BBBB', 9, 8)],
    ['a fractional cursor', ok('BBBB', 7.5, 8)],
    ['a changed sealed length', ok('BBBB', 8, 9)],
    ['text without progress', ok('BBBB', 4, 8)],
    ['an unrecognized status', SealedAgentArtifactWindowResponse.fromJS({ status: 'Partial', text: 'BBBB', nextOffset: 8, totalLengthSoFar: 8 })],
  ])('rejects a later window with %s, shows none of it and retries the same offset, appending once', async (_name, malformed) => {
    let laterReads = 0
    const get = install((_run, _message, _segment, from) => {
      if (from === 0) {
        return Promise.resolve(ok('AAAA', 4, 8))
      }

      laterReads += 1
      return Promise.resolve(laterReads === 1 ? malformed : ok('BBBB', 8, 8))
    })
    const { result } = mounted(base)
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAA'))

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(result.current.state).toEqual({ status: 'error', message: 'This artifact window is unavailable.' }))

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAABBBB'))
    expect(result.current.state).toMatchObject({ nextOffset: 8, totalLengthSoFar: 8, isComplete: true })
    expect(get.mock.calls.map((call) => call[3])).toEqual([0, 4, 4])
  })

  it.each([
    ['no progress', ok('', 0, 8)],
    ['an advance beyond the requested byte cap', ok('A', SEALED_WINDOW_MAX_BYTES + 1, SEALED_WINDOW_MAX_BYTES + 8)],
  ])('rejects a first window with %s and retries offset 0', async (_name, malformed) => {
    let reads = 0
    const get = install(() => {
      reads += 1
      return Promise.resolve(reads === 1 ? malformed : ok('AAAA', 4, 8))
    })
    const { result } = mounted(base)

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(result.current.state.status).toBe('error'))
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAA'))
    expect(get.mock.calls.map((call) => call[3])).toEqual([0, 0])
  })

  it('accepts an empty sealed file as one complete window', async () => {
    install(() => Promise.resolve(ok('', 0, 0)))
    const { result } = mounted(base)

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(result.current.state).toMatchObject({ status: 'loaded', text: '', totalLengthSoFar: 0, isComplete: true }))
    act(() => result.current.loadNextWindow())
    expect(result.current.state.status).toBe('loaded')
  })

  it('shows no text for a closed status reached after an accepted window, and requests nothing more', async () => {
    const get = install((_run, _message, _segment, from) =>
      Promise.resolve(
        from === 0
          ? ok('AAAA', 4, 8)
          : SealedAgentArtifactWindowResponse.fromJS({ status: 'IntegrityMismatch', text: '', nextOffset: 4, totalLengthSoFar: 12, truncated: null }),
      ),
    )
    const { result } = mounted(base)
    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('AAAA'))

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(result.current.state).toEqual({ status: 'integrity-mismatch' }))
    act(() => result.current.loadNextWindow())
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('treats another message, run or purpose as a new lifetime: the predecessor late window settles nothing and its retained callback starts nothing', async () => {
    const slow = deferred<SealedAgentArtifactWindowResponse>()
    const get = install((_run, message) => (message === 'message-1' ? slow.promise : Promise.resolve(ok('other', 5, 5))))
    const { result, rerender } = mounted(base)
    const retained = result.current.loadNextWindow
    act(() => retained())

    rerender({ ...base, messageId: 'message-2' })
    expect(result.current.state).toEqual({ status: 'idle' })
    act(() => retained())
    await act(async () => {
      slow.resolve(ok('late', 4, 4))
      await slow.promise
    })
    expect(result.current.state).toEqual({ status: 'idle' })

    act(() => result.current.loadNextWindow())
    await waitFor(() => expect(textOf(result.current.state)).toBe('other'))
    rerender({ ...base, runId: 'run-b', messageId: 'message-2' })
    expect(result.current.state).toEqual({ status: 'idle' })
    rerender({ ...base, runId: 'run-b', messageId: 'message-2', purpose: 'AgentFinalResponse' })
    expect(result.current.state).toEqual({ status: 'idle' })
    expect(get.mock.calls.map((call) => [call[0], call[1], call[2]])).toEqual([
      ['run-a', 'message-1', 'stderr'],
      ['run-a', 'message-2', 'stderr'],
    ])
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
      slow.resolve(ok('late', 4, 4))
      await slow.promise
    })

    expect(get).toHaveBeenCalledTimes(1)
  })
})
