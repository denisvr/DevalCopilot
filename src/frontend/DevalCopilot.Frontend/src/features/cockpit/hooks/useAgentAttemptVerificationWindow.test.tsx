import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptVerificationEvidenceClient, agentAttemptVerificationWindowClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { attemptIdFor } from '../agentAttemptHistoryFixture'
import type { RecordedVerificationStreamDescriptor, VerificationStream } from '../agentAttemptVerification'
import {
  artifactIdFor,
  executionIdFor,
  recordedStream,
  recordedVerificationFor,
  refusedVerificationWindow,
  verificationWindowAnswer,
} from '../agentAttemptVerificationFixture'
import { useAgentAttemptVerificationEvidence } from './useAgentAttemptVerificationEvidence'
import type { AgentAttemptVerificationEvidenceTarget } from './useAgentAttemptVerificationEvidence'
import { useAgentAttemptVerificationWindow } from './useAgentAttemptVerificationWindow'
import type { VerificationWindowTarget } from './useAgentAttemptVerificationWindow'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptVerificationEvidenceClient: vi.fn(),
  agentAttemptVerificationWindowClient: vi.fn(),
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

type Read = (
  runId: string,
  attemptId: string,
  executionId: string,
  stream: VerificationStream,
  fromOffset: number,
  maxBytes: number,
) => Promise<unknown>

function install(read: Read) {
  const readVerificationWindow = vi.fn(read)
  vi.mocked(agentAttemptVerificationWindowClient).mockReturnValue(
    { readVerificationWindow } as unknown as ReturnType<typeof agentAttemptVerificationWindowClient>,
  )
  return readVerificationWindow
}

const RUN = 'run-1'
const descriptorFor = (position: number, stream: VerificationStream, byteLength: number, truncated: boolean | null = false) =>
  recordedStream(artifactIdFor(RUN, 2, position, stream), byteLength, truncated) as RecordedVerificationStreamDescriptor

const targetFor = (
  position: number,
  stream: VerificationStream,
  byteLength = 100,
  openingKey: string | null = 'opening-1',
): VerificationWindowTarget => ({
  openingKey,
  runId: RUN,
  attemptId: attemptIdFor(RUN, 2),
  selection: { executionId: executionIdFor(RUN, 2, position), stream, visit: 1, descriptor: descriptorFor(position, stream, byteLength) },
})

const facts = (target: VerificationWindowTarget) => ({
  runId: target.runId,
  attemptId: target.attemptId,
  executionId: target.selection!.executionId,
  stream: target.selection!.stream,
  descriptor: target.selection!.descriptor,
})

/** The host's honest answer for any request: the next at most `maxBytes` ASCII bytes of an output of the descriptor's length. */
function serving(target: VerificationWindowTarget): Read {
  return (_run, _attempt, _execution, _stream, fromOffset, maxBytes) => {
    const total = target.selection!.descriptor.byteLength
    const length = Math.min(maxBytes, total - fromOffset)
    return Promise.resolve(verificationWindowAnswer(facts(target), 'x'.repeat(length), fromOffset))
  }
}

function mounted(target: VerificationWindowTarget) {
  return renderHook((props: VerificationWindowTarget) => useAgentAttemptVerificationWindow(props), { initialProps: target })
}

const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'verification-window-trace' })

describe('useAgentAttemptVerificationWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing and stays idle until the owner loads, with a selection and without one', () => {
    const read = install(serving(targetFor(0, 'stdout')))

    const { result } = mounted(targetFor(0, 'stdout'))
    const { result: none } = mounted({ ...targetFor(0, 'stdout'), selection: null })
    act(() => none.current.load())

    expect(result.current.state).toEqual({ status: 'idle' })
    expect(none.current.state).toEqual({ status: 'idle' })
    expect(read).not.toHaveBeenCalled()
    expect(agentAttemptVerificationWindowClient).not.toHaveBeenCalled()
  })

  it('does nothing while the section is closed even when a selection is supplied', () => {
    const read = install(serving(targetFor(0, 'stdout')))
    const { result } = mounted(targetFor(0, 'stdout', 100, null))

    act(() => result.current.load())

    expect(read).not.toHaveBeenCalled()
  })

  it('loads the first window explicitly with the member, stream, offset zero and the 16 KiB default, then the next from the cursor', async () => {
    const target = targetFor(0, 'stdout', 40_000)
    const read = install(serving(target))
    const { result } = mounted(target)

    act(() => result.current.load())
    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
    expect(result.current.state).toEqual({ status: 'loading', text: '' })
    await act(async () => undefined)
    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 16384, totalLength: 40_000, isComplete: false, viewLimitReached: false })

    act(() => result.current.load())
    await act(async () => undefined)
    expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 16384, 16384)

    act(() => result.current.load())
    await act(async () => undefined)
    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 40_000, isComplete: true })
    expect((result.current.state as { text: string }).text).toHaveLength(40_000)

    act(() => result.current.load())
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('never pages by itself', async () => {
    const target = targetFor(0, 'stdout', 40_000)
    const read = install(serving(target))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))

    expect(read).toHaveBeenCalledTimes(1)
  })

  it('sends one request for two loads in the same tick and none while loading', async () => {
    const pending = deferred<unknown>()
    const target = targetFor(0, 'stdout', 40_000)
    const read = install(() => pending.promise)
    const { result } = mounted(target)

    act(() => {
      result.current.load()
      result.current.load()
    })
    act(() => result.current.load())

    expect(read).toHaveBeenCalledTimes(1)
    await act(async () => {
      pending.resolve(verificationWindowAnswer(facts(target), 'x'.repeat(16384)))
      await pending.promise
    })
    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 16384 })
  })

  it('reads an empty sealed output as loaded and complete with no text', async () => {
    const target = targetFor(0, 'stderr', 0)
    install(serving(target))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)

    expect(result.current.state).toEqual({
      status: 'loaded',
      text: '',
      nextOffset: 0,
      totalLength: 0,
      isComplete: true,
      viewLimitReached: false,
    })
  })

  it('renders hostile text as the literal characters the host returned', async () => {
    const target = targetFor(0, 'stdout', 36)
    const hostile = '<img src=x onerror=alert(1)> [x](javascript:1) '
    install(() => Promise.resolve(verificationWindowAnswer(facts(target), hostile.slice(0, 36), 0)))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)

    expect((result.current.state as { text: string }).text).toBe(hostile.slice(0, 36))
  })

  describe('the 256 KiB source-byte view budget', () => {
    it('requests exactly 16 windows of 16 KiB, then stops with a local-limit state while the output goes on', async () => {
      const target = targetFor(0, 'stdout', 400_000)
      const read = install(serving(target))
      const { result } = mounted(target)

      for (let window = 0; window < 16; window++) {
        act(() => result.current.load())
        await act(async () => undefined)
      }

      expect(read).toHaveBeenCalledTimes(16)
      expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 262_144, totalLength: 400_000, isComplete: false, viewLimitReached: true })
      expect((result.current.state as { text: string }).text).toHaveLength(262_144)

      act(() => result.current.load())
      expect(read).toHaveBeenCalledTimes(16)
    })

    it('caps the last request by what remains of the budget', async () => {
      const target = targetFor(0, 'stdout', 400_000)
      const calls: number[] = []
      // Windows of 16000 bytes each leave 6144 of the budget after sixteen reads.
      const read = install((_run, _attempt, _execution, _stream, fromOffset, maxBytes) => {
        calls.push(maxBytes)
        const length = Math.min(16_000, maxBytes)
        return Promise.resolve(verificationWindowAnswer(facts(target), 'x'.repeat(length), fromOffset))
      })
      const { result } = mounted(target)

      for (let window = 0; window < 17; window++) {
        act(() => result.current.load())
        await act(async () => undefined)
      }

      expect(read).toHaveBeenCalledTimes(17)
      expect(calls.slice(0, 16)).toEqual(Array(16).fill(16384))
      expect(calls[16]).toBe(262_144 - 256_000)
      expect(result.current.state).toMatchObject({ nextOffset: 262_144, viewLimitReached: true, isComplete: false })
    })

    it.each([
      ['exactly 64 bytes remain, so one more window of exactly 64 is requested', 16_320, false],
      ['63 bytes remain, so nothing more is requested', 16_321, true],
    ])('when %s', async (_, sixteenthAdvance, limitReached) => {
      const target = targetFor(0, 'stdout', 400_000)
      const calls: number[] = []
      const read = install((_run, _attempt, _execution, _stream, fromOffset, maxBytes) => {
        calls.push(maxBytes)
        const length = calls.length === 16 ? sixteenthAdvance : maxBytes
        return Promise.resolve(verificationWindowAnswer(facts(target), 'x'.repeat(length), fromOffset))
      })
      const { result } = mounted(target)

      for (let window = 0; window < 16; window++) {
        act(() => result.current.load())
        await act(async () => undefined)
      }

      expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 245_760 + sixteenthAdvance, viewLimitReached: limitReached })
      act(() => result.current.load())
      await act(async () => undefined)
      if (limitReached) {
        expect(read).toHaveBeenCalledTimes(16)
      } else {
        expect(calls[16]).toBe(64)
        expect(result.current.state).toMatchObject({ nextOffset: 262_144, viewLimitReached: true })
      }
    })

    it('treats an output that ends inside the budget as complete, not as a view limit', async () => {
      const target = targetFor(0, 'stdout', 262_144)
      install(serving(target))
      const { result } = mounted(target)

      for (let window = 0; window < 16; window++) {
        act(() => result.current.load())
        await act(async () => undefined)
      }

      expect(result.current.state).toMatchObject({ isComplete: true, viewLimitReached: false })
    })
  })

  it.each([
    ['Missing', 'missing'],
    ['IntegrityMismatch', 'integrity-mismatch'],
    ['Unavailable', 'unavailable'],
    ['NotRecorded', 'not-recorded'],
  ] as const)('shows %s as its own closed state with no text and offers no further read', async (status, expected) => {
    const target = targetFor(0, 'stdout')
    const read = install(() => Promise.resolve(refusedVerificationWindow(facts(target), status)))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)
    act(() => result.current.load())

    expect(result.current.state).toEqual({ status: expected })
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('a closed status after accepted windows hides their text', async () => {
    const target = targetFor(0, 'stdout', 40_000)
    let first = true
    install((run, attempt, execution, stream, fromOffset, maxBytes) => {
      if (first) {
        first = false
        return serving(target)(run, attempt, execution, stream, fromOffset, maxBytes)
      }

      return Promise.resolve(refusedVerificationWindow(facts(target), 'IntegrityMismatch', fromOffset))
    })
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)
    act(() => result.current.load())
    await act(async () => undefined)

    expect(result.current.state).toEqual({ status: 'integrity-mismatch' })
  })

  it.each([
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(notFound()), 'not-found'],
    ['an incoherent answer', () => Promise.resolve({ status: 'Ok' }), 'invalid'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
  ] as const)('reports %s as one failure that keeps the accepted text and retries the same offset only on request', async (_, failing, failure) => {
    const target = targetFor(0, 'stdout', 40_000)
    let calls = 0
    const read = install((run, attempt, execution, stream, fromOffset, maxBytes) =>
      calls++ === 1 ? failing() : serving(target)(run, attempt, execution, stream, fromOffset, maxBytes))
    const { result } = mounted(target)
    act(() => result.current.load())
    await act(async () => undefined)

    act(() => result.current.load())
    await act(async () => undefined)
    expect(result.current.state).toEqual({ status: 'error', failure, text: 'x'.repeat(16384) })
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(read).toHaveBeenCalledTimes(2)

    act(() => result.current.load())
    await act(async () => undefined)
    expect(read).toHaveBeenCalledTimes(3)
    expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 16384, 16384)
    expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 32768 })
    expect((result.current.state as { text: string }).text).toHaveLength(32768)
  })

  it('a first-window failure retries from offset zero', async () => {
    const target = targetFor(0, 'stdout', 100)
    const read = install(vi.fn<Read>().mockRejectedValueOnce(new Error('boom')).mockImplementation(serving(target)))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)
    expect(result.current.state).toEqual({ status: 'error', failure: 'read', text: '' })
    act(() => result.current.load())
    await act(async () => undefined)

    expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
    expect(result.current.state).toMatchObject({ status: 'loaded', isComplete: true })
  })

  it.each([
    ['another member', (target: VerificationWindowTarget) => verificationWindowAnswer({ ...facts(target), executionId: executionIdFor(RUN, 2, 5) }, 'xx')],
    ['the other stream', (target: VerificationWindowTarget) => verificationWindowAnswer({ ...facts(target), stream: 'stderr' }, 'xx')],
    ['another artifact', (target: VerificationWindowTarget) => verificationWindowAnswer({ ...facts(target), descriptor: descriptorFor(0, 'stderr', 100) }, 'xx')],
    ['another length', (target: VerificationWindowTarget) => verificationWindowAnswer(facts(target), 'xx', 0, { totalLength: 99 })],
    ['a cursor past the cap', (target: VerificationWindowTarget) => verificationWindowAnswer(facts(target), 'xx', 0, { nextOffset: 17000 })],
  ])('refuses an answer for %s as not coherent and uses none of it', async (_, answerOf) => {
    const target = targetFor(0, 'stdout', 100)
    install(() => Promise.resolve(answerOf(target)))
    const { result } = mounted(target)

    act(() => result.current.load())
    await act(async () => undefined)

    expect(result.current.state).toEqual({ status: 'error', failure: 'invalid', text: '' })
  })

  describe('lifetimes', () => {
    const A = targetFor(0, 'stdout', 40_000)
    const B = targetFor(1, 'stderr', 40_000)

    it('another member or stream shows nothing of the first in its first frame and no late answer of it settles the second', async () => {
      const late = deferred<unknown>()
      const read = install((run, attempt, execution, stream, fromOffset, maxBytes) =>
        stream === 'stdout' ? late.promise : serving(B)(run, attempt, execution, stream, fromOffset, maxBytes))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())

      rerender(B)

      expect(result.current.state).toEqual({ status: 'idle' })
      await act(async () => {
        late.resolve(verificationWindowAnswer(facts(A), 'x'.repeat(16384)))
        await late.promise
      })
      expect(result.current.state).toEqual({ status: 'idle' })

      act(() => result.current.load())
      await act(async () => undefined)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 1), 'stderr', 0, 16384)
      expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 16384 })
    })

    it('the other stream of the same member is its own lifetime that starts at offset zero', async () => {
      const read = install((run, attempt, execution, stream, fromOffset, maxBytes) =>
        serving(stream === 'stdout' ? A : { ...A, selection: { ...A.selection!, stream: 'stderr' } })(run, attempt, execution, stream, fromOffset, maxBytes))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())
      await act(async () => undefined)
      act(() => result.current.load())
      await act(async () => undefined)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 16384, 16384)

      rerender({ ...A, selection: { ...A.selection!, stream: 'stderr' } })
      expect(result.current.state).toEqual({ status: 'idle' })
      act(() => result.current.load())
      await act(async () => undefined)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stderr', 0, 16384)
    })

    it('returning to the first selection (A to B to A) starts idle, and the first visit read stays ignored', async () => {
      const first = deferred<unknown>()
      let visits = 0
      const read = install((run, attempt, execution, stream, fromOffset, maxBytes) =>
        execution === executionIdFor(RUN, 2, 0) && visits++ === 0 ? first.promise : serving(execution === executionIdFor(RUN, 2, 0) ? A : B)(run, attempt, execution, stream, fromOffset, maxBytes))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())
      rerender(B)
      rerender({ ...A })

      expect(result.current.state).toEqual({ status: 'idle' })
      await act(async () => {
        first.resolve(verificationWindowAnswer(facts(A), 'x'.repeat(16384)))
        await first.promise
      })
      expect(result.current.state).toEqual({ status: 'idle' })

      act(() => result.current.load())
      await act(async () => undefined)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
      expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 16384 })
    })

    describe('a new visit of the same member and stream', () => {
      const revisit = (target: VerificationWindowTarget): VerificationWindowTarget => ({
        ...target,
        selection: { ...target.selection!, visit: target.selection!.visit + 1 },
      })

      it('starts idle in its first frame without the loaded text, reads nothing by itself and loads from offset zero', async () => {
        const read = install(serving(A))
        const { result, rerender } = mounted(A)
        act(() => result.current.load())
        await act(async () => undefined)
        act(() => result.current.load())
        await act(async () => undefined)
        expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 32768 })

        rerender(revisit(A))

        expect(result.current.state).toEqual({ status: 'idle' })
        await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
        expect(read).toHaveBeenCalledTimes(2)
        act(() => result.current.load())
        await act(async () => undefined)
        expect(read).toHaveBeenCalledTimes(3)
        expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
        expect(result.current.state).toMatchObject({ status: 'loaded', nextOffset: 16384 })
      })

      it('a late answer and a retained load of the ended visit settle and start nothing, even in the same batch', async () => {
        const late = deferred<unknown>()
        const read = install(vi.fn<Read>().mockReturnValueOnce(late.promise).mockImplementation(serving(A)))
        const { result, rerender } = mounted(A)
        act(() => result.current.load())
        const staleLoad = result.current.load

        rerender(revisit(A))
        await act(async () => {
          late.resolve(verificationWindowAnswer(facts(A), 'o'.repeat(16384)))
          await late.promise
        })
        expect(result.current.state).toEqual({ status: 'idle' })
        act(() => staleLoad())
        expect(read).toHaveBeenCalledTimes(1)

        act(() => {
          result.current.load()
          staleLoad()
        })
        await act(async () => undefined)
        expect(read).toHaveBeenCalledTimes(2)
        expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
        expect((result.current.state as { text: string }).text.startsWith('x')).toBe(true)
      })
    })

    it('closing and reopening the section is a new lifetime that starts at zero without the earlier text', async () => {
      const read = install(serving(A))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())
      await act(async () => undefined)
      expect(result.current.state).toMatchObject({ status: 'loaded' })

      rerender({ ...A, openingKey: null, selection: null })
      expect(result.current.state).toEqual({ status: 'idle' })
      rerender({ ...A, openingKey: 'opening-2' })
      expect(result.current.state).toEqual({ status: 'idle' })

      act(() => result.current.load())
      await act(async () => undefined)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
    })

    it('another attempt or run is another lifetime', async () => {
      const read = install(serving(A))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())
      await act(async () => undefined)

      rerender({ ...A, attemptId: attemptIdFor(RUN, 9) })
      expect(result.current.state).toEqual({ status: 'idle' })
      rerender({ ...A, runId: 'run-2' })
      expect(result.current.state).toEqual({ status: 'idle' })
      act(() => result.current.load())
      await act(async () => undefined)

      expect(read).toHaveBeenLastCalledWith('run-2', attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 0), 'stdout', 0, 16384)
    })

    it('a retained load of an ended lifetime starts nothing for its successor, even in the same batch', async () => {
      const read = install(serving(B))
      const { result, rerender } = mounted(A)
      const staleLoad = result.current.load

      rerender(B)
      act(() => staleLoad())
      expect(read).not.toHaveBeenCalled()

      act(() => {
        result.current.load()
        staleLoad()
      })
      expect(read).toHaveBeenCalledTimes(1)
      expect(read).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), executionIdFor(RUN, 2, 1), 'stderr', 0, 16384)
    })

    it('overlapping windows of one lifetime cannot happen and an older one cannot overwrite a newer one across lifetimes', async () => {
      const older = deferred<unknown>()
      const newer = deferred<unknown>()
      install(vi.fn<Read>().mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())
      rerender(B)
      act(() => result.current.load())

      await act(async () => {
        newer.resolve(verificationWindowAnswer(facts(B), 'n'.repeat(16384)))
        await newer.promise
      })
      await act(async () => {
        older.resolve(verificationWindowAnswer(facts(A), 'o'.repeat(16384)))
        await older.promise
      })

      expect((result.current.state as { text: string }).text.startsWith('n')).toBe(true)
    })

    it('ending the selection makes the hook inert', () => {
      const read = install(() => new Promise<unknown>(() => undefined))
      const { result, rerender } = mounted(A)
      act(() => result.current.load())

      rerender({ ...A, selection: null })

      expect(result.current.state).toEqual({ status: 'idle' })
      act(() => result.current.load())
      expect(read).toHaveBeenCalledTimes(1)
    })

    it('unmounting leaves a pending read inert and reports no React error', async () => {
      const pending = deferred<unknown>()
      install(() => pending.promise)
      const consoleError = vi.spyOn(console, 'error')
      const { result, unmount } = mounted(A)
      act(() => result.current.load())
      const retainedLoad = result.current.load

      unmount()
      await act(async () => {
        pending.resolve(verificationWindowAnswer(facts(A), 'x'.repeat(16384)))
        await pending.promise
      })
      retainedLoad()

      expect(consoleError).not.toHaveBeenCalled()
      expect(agentAttemptVerificationWindowClient).toHaveBeenCalledTimes(1)
      consoleError.mockRestore()
    })
  })
})

describe('the member/stream visit composed with the selection hook', () => {
  const opening: AgentAttemptVerificationEvidenceTarget = {
    selectionKey: 'review-selection',
    runId: RUN,
    expected: { attemptId: attemptIdFor(RUN, 2), attemptNumber: 2, role: 'CodeReviewer', provider: 'Codex', responseContract: 'ImplementationReview' },
  }
  const memberA = executionIdFor(RUN, 2, 0)
  const memberB = executionIdFor(RUN, 2, 1)
  const answerFor = (executionId: string, fromOffset: number) =>
    verificationWindowAnswer(
      {
        runId: RUN,
        attemptId: attemptIdFor(RUN, 2),
        executionId,
        stream: 'stdout',
        descriptor: descriptorFor(executionId === memberA ? 0 : 1, 'stdout', 120),
      },
      (executionId === memberA ? 'a' : 'b').repeat(120 - fromOffset),
      fromOffset,
    )

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(agentAttemptVerificationEvidenceClient).mockReturnValue({
      readVerificationEvidence: vi.fn(() => Promise.resolve(recordedVerificationFor(RUN, 2, ['Passed', 'Passed']))),
    } as unknown as ReturnType<typeof agentAttemptVerificationEvidenceClient>)
  })

  async function composed(read: Read = (_run, _attempt, execution, _stream, fromOffset) => Promise.resolve(answerFor(execution, fromOffset))) {
    const windowRead = install(read)
    const view = renderHook(() => {
      const evidence = useAgentAttemptVerificationEvidence(opening)
      const window = useAgentAttemptVerificationWindow({
        openingKey: evidence.openingKey,
        runId: RUN,
        attemptId: attemptIdFor(RUN, 2),
        selection: evidence.selection,
      })
      return { evidence, window }
    })
    act(() => view.result.current.evidence.show())
    await act(async () => undefined)
    return { ...view, windowRead }
  }

  async function loadCurrent(result: { current: { window: { load: () => void } } }) {
    act(() => result.current.window.load())
    await act(async () => undefined)
  }

  it('a retained Hide of A neither clears B nor resets its loaded window', async () => {
    const { result, windowRead } = await composed()
    act(() => result.current.evidence.select(memberA, 'stdout'))
    const hideA = result.current.evidence.clearSelection
    act(() => result.current.evidence.select(memberB, 'stdout'))
    await loadCurrent(result)

    act(() => hideA())

    expect(result.current.evidence.selection?.executionId).toBe(memberB)
    expect(result.current.window.state).toMatchObject({ status: 'loaded', text: 'b'.repeat(120) })
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('a retained Hide of A queued after selecting B in the same batch leaves B chosen with a fresh idle window', async () => {
    const { result, windowRead } = await composed()
    act(() => result.current.evidence.select(memberA, 'stdout'))
    await loadCurrent(result)
    const hideA = result.current.evidence.clearSelection

    act(() => {
      result.current.evidence.select(memberB, 'stdout')
      hideA()
    })

    expect(result.current.evidence.selection?.executionId).toBe(memberB)
    expect(result.current.window.state).toEqual({ status: 'idle' })
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('after A to B to A the first Hide of A leaves the later visit and its loaded window', async () => {
    const { result } = await composed()
    act(() => result.current.evidence.select(memberA, 'stdout'))
    const hideFirstA = result.current.evidence.clearSelection
    act(() => result.current.evidence.select(memberB, 'stdout'))
    act(() => result.current.evidence.select(memberA, 'stdout'))
    await loadCurrent(result)

    act(() => hideFirstA())

    expect(result.current.evidence.selection?.executionId).toBe(memberA)
    expect(result.current.window.state).toMatchObject({ status: 'loaded', text: 'a'.repeat(120) })
  })

  it('a loaded A hidden and reselected in one batch shows a fresh idle window, reads nothing by itself and next reads offset zero', async () => {
    const { result, windowRead } = await composed()
    act(() => result.current.evidence.select(memberA, 'stdout'))
    await loadCurrent(result)
    expect(result.current.window.state).toMatchObject({ status: 'loaded', nextOffset: 120 })

    act(() => {
      result.current.evidence.clearSelection()
      result.current.evidence.select(memberA, 'stdout')
    })

    expect(result.current.evidence.selection?.executionId).toBe(memberA)
    expect(result.current.window.state).toEqual({ status: 'idle' })
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(windowRead).toHaveBeenCalledTimes(1)
    await loadCurrent(result)
    expect(windowRead).toHaveBeenCalledTimes(2)
    expect(windowRead).toHaveBeenLastCalledWith(RUN, attemptIdFor(RUN, 2), memberA, 'stdout', 0, 16384)
  })

  it('a late answer and a retained load of a visit ended by a same-batch Hide and reselect settle and start nothing', async () => {
    const late = deferred<unknown>()
    const { result, windowRead } = await composed(
      vi.fn<Read>().mockReturnValueOnce(late.promise).mockImplementation((_run, _attempt, execution, _stream, fromOffset) =>
        Promise.resolve(answerFor(execution, fromOffset))),
    )
    act(() => result.current.evidence.select(memberA, 'stdout'))
    act(() => result.current.window.load())
    const staleLoad = result.current.window.load
    const staleHide = result.current.evidence.clearSelection

    act(() => {
      result.current.evidence.clearSelection()
      result.current.evidence.select(memberA, 'stdout')
    })
    await act(async () => {
      late.resolve(answerFor(memberA, 0))
      await late.promise
    })
    act(() => {
      staleLoad()
      staleHide()
    })

    expect(result.current.evidence.selection?.executionId).toBe(memberA)
    expect(result.current.window.state).toEqual({ status: 'idle' })
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('the current Hide still ends its visit and its window', async () => {
    const { result } = await composed()
    act(() => result.current.evidence.select(memberA, 'stdout'))
    await loadCurrent(result)

    act(() => result.current.evidence.clearSelection())

    expect(result.current.evidence.selection).toBeNull()
    expect(result.current.window.state).toEqual({ status: 'idle' })
  })
})
