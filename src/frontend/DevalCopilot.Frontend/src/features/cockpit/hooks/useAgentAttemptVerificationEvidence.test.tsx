import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptVerificationEvidenceClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { attemptIdFor } from '../agentAttemptHistoryFixture'
import type { VerificationContract } from '../agentAttemptVerificationFixture'
import {
  executionIdFor,
  recordedStream,
  recordedVerificationFor,
  unavailableStream,
  unavailableVerificationFor,
  verificationMemberFor,
} from '../agentAttemptVerificationFixture'
import { useAgentAttemptVerificationEvidence } from './useAgentAttemptVerificationEvidence'
import type { AgentAttemptVerificationEvidenceTarget } from './useAgentAttemptVerificationEvidence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptVerificationEvidenceClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type Read = (runId: string, attemptId: string) => Promise<unknown>

function install(read: Read) {
  const readVerificationEvidence = vi.fn(read)
  vi.mocked(agentAttemptVerificationEvidenceClient).mockReturnValue(
    { readVerificationEvidence } as unknown as ReturnType<typeof agentAttemptVerificationEvidenceClient>,
  )
  return readVerificationEvidence
}

const expectationFor = (runId: string, attemptNumber: number, contract: 'ImplementationReview' | 'VerificationDiagnosis' = 'ImplementationReview'): AgentAttemptIdentityExpectation => ({
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'CodeReviewer',
  provider: 'Codex',
  responseContract: contract,
})

const A: AgentAttemptVerificationEvidenceTarget = { selectionKey: 'selection-a', runId: 'run-1', expected: expectationFor('run-1', 2) }
const B: AgentAttemptVerificationEvidenceTarget = { selectionKey: 'selection-b', runId: 'run-1', expected: expectationFor('run-1', 1) }
const answerFor = (target: AgentAttemptVerificationEvidenceTarget, statuses: readonly ('Passed' | 'Failed')[] = ['Passed', 'Passed']) =>
  recordedVerificationFor(target.runId, target.expected.attemptNumber, statuses, target.expected.responseContract as VerificationContract)

function mounted(target: AgentAttemptVerificationEvidenceTarget) {
  return renderHook((props: AgentAttemptVerificationEvidenceTarget) => useAgentAttemptVerificationEvidence(props), { initialProps: target })
}

const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'verification-test-trace' })

describe('useAgentAttemptVerificationEvidence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing until the section is opened, with a selection and without one', () => {
    const read = install(() => Promise.resolve(answerFor(A)))

    const { result } = mounted(A)
    mounted({ ...A, selectionKey: null })

    expect(result.current.open).toBe(false)
    expect(result.current.openingKey).toBeNull()
    expect(result.current.evidence).toBeNull()
    expect(read).not.toHaveBeenCalled()
    expect(agentAttemptVerificationEvidenceClient).not.toHaveBeenCalled()
  })

  it('opens with exactly one request for the selected run and attempt and exposes the validated answer', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)
    const { result } = mounted(A)

    act(() => result.current.show())

    expect(result.current.open).toBe(true)
    expect(result.current.pending).toBe(true)
    expect(result.current.evidence).toBeNull()
    expect(result.current.openingKey).not.toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1', A.expected.attemptId)

    await act(async () => {
      pending.resolve(answerFor(A, ['Passed', 'Passed', 'Passed']))
      await pending.promise
    })

    expect(result.current.pending).toBe(false)
    expect(result.current.failure).toBeNull()
    expect(result.current.evidence).toMatchObject({ status: 'Recorded', members: expect.any(Array) })
    act(() => result.current.show())
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('exposes Unavailable as its own state', async () => {
    install(() => Promise.resolve(unavailableVerificationFor('run-1', 2)))
    const { result } = mounted(A)

    act(() => result.current.show())
    await act(async () => undefined)

    expect(result.current.evidence).toEqual({ status: 'Unavailable' })
    expect(result.current.failure).toBeNull()
  })

  it.each([
    ['an answer for another attempt', () => Promise.resolve(answerFor(B)), 'invalid'],
    ['a malformed answer', () => Promise.resolve({ status: 'Recorded' }), 'invalid'],
    ['no body', () => Promise.resolve(undefined), 'invalid'],
    ['a failed request', () => Promise.reject(new Error('boom')), 'read'],
    ['a not-found problem', () => Promise.reject(notFound()), 'not-found'],
  ] as const)('reports %s as one failure with no evidence', async (_, respond, failure) => {
    install(respond)
    const { result } = mounted(A)

    act(() => result.current.show())
    await act(async () => undefined)

    expect(result.current.failure).toBe(failure)
    expect(result.current.evidence).toBeNull()
    expect(result.current.pending).toBe(false)
  })

  it('retries the same attempt only after a failure, once per click, and never while a read is pending or succeeded', async () => {
    const second = deferred<unknown>()
    const read = install(vi.fn<Read>().mockRejectedValueOnce(new Error('boom')).mockReturnValueOnce(second.promise))
    const { result } = mounted(A)
    act(() => result.current.retry())
    expect(read).not.toHaveBeenCalled()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(result.current.failure).toBe('read')

    act(() => {
      result.current.retry()
      result.current.retry()
    })
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.pending).toBe(true)

    await act(async () => {
      second.resolve(answerFor(A))
      await second.promise
    })
    act(() => result.current.retry())
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.evidence?.status).toBe('Recorded')
  })

  describe('member and stream selection', () => {
    const recordedWithStreams = () => {
      const answer = recordedVerificationFor('run-1', 2, ['Passed', 'Passed'])
      answer.members[1] = verificationMemberFor('run-1', 2, 1, 'Passed', {
        standardOutput: unavailableStream(),
        standardError: recordedStream('run-1-attempt-2-artifact-1-stderr', 9, null),
      })
      return answer
    }

    async function opened(target = A) {
      install(() => Promise.resolve(recordedWithStreams()))
      const view = mounted(target)
      act(() => view.result.current.show())
      await act(async () => undefined)
      return view
    }

    it('selects only a Recorded stream of a member of the accepted answer and exposes its descriptor', async () => {
      const { result } = await opened()

      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))
      expect(result.current.selection).toMatchObject({
        executionId: 'run-1-attempt-2-execution-0',
        stream: 'stdout',
        descriptor: { status: 'Recorded', byteLength: 120, truncated: false },
      })

      act(() => result.current.select(executionIdFor('run-1', 2, 1), 'stderr'))
      expect(result.current.selection).toMatchObject({ executionId: 'run-1-attempt-2-execution-1', stream: 'stderr' })
      expect(result.current.selection?.descriptor).toMatchObject({ truncated: null, captureOutcome: 'RecoveredAfterHostInterruption' })

      act(() => result.current.clearSelection())
      expect(result.current.selection).toBeNull()
    })

    it('ignores an unrecorded stream, an unavailable stream and a member the answer does not contain', async () => {
      const { result } = await opened()

      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stderr'))
      act(() => result.current.select(executionIdFor('run-1', 2, 1), 'stdout'))
      act(() => result.current.select('someone-elses-execution', 'stdout'))

      expect(result.current.selection).toBeNull()
    })

    it('ignores a selection before the answer arrived and after a failure', async () => {
      install(() => Promise.reject(new Error('boom')))
      const { result } = mounted(A)
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))
      act(() => result.current.show())
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))
      await act(async () => undefined)
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))

      expect(result.current.selection).toBeNull()
    })

    it('closing and reopening starts without the earlier selection, and a retained select of the ended opening does nothing', async () => {
      const { result } = await opened()
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))
      const staleSelect = result.current.select
      const keyBefore = result.current.openingKey

      act(() => result.current.close())
      expect(result.current.selection).toBeNull()
      act(() => result.current.show())
      expect(result.current.selection).toBeNull()
      expect(result.current.openingKey).not.toBe(keyBefore)
      await act(async () => undefined)

      act(() => staleSelect(executionIdFor('run-1', 2, 0), 'stdout'))
      expect(result.current.selection).toBeNull()
    })

    it('another attempt shows no selection in its first frame, and a return to the first attempt starts without one', async () => {
      const { result, rerender } = await opened()
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))

      rerender(B)
      expect(result.current.open).toBe(false)
      expect(result.current.selection).toBeNull()
      expect(result.current.openingKey).toBeNull()

      rerender({ ...A, selectionKey: 'selection-a-again' })
      expect(result.current.selection).toBeNull()
      act(() => result.current.show())
      await act(async () => undefined)
      expect(result.current.selection).toBeNull()
    })

    it('a retained clearSelection of an ended opening cannot clear the choice of its successor', async () => {
      const { result } = await opened()
      const staleClear = result.current.clearSelection
      act(() => result.current.close())
      act(() => result.current.show())
      await act(async () => undefined)
      act(() => result.current.select(executionIdFor('run-1', 2, 0), 'stdout'))

      act(() => staleClear())

      expect(result.current.selection).toMatchObject({ stream: 'stdout' })
    })

    describe('each member/stream visit owns its Hide', () => {
      const memberA = executionIdFor('run-1', 2, 0)
      const memberB = executionIdFor('run-1', 2, 1)

      it('a retained Hide of A cannot clear B chosen after it, and the current Hide still clears B', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const hideA = result.current.clearSelection
        act(() => result.current.select(memberB, 'stderr'))

        act(() => hideA())
        expect(result.current.selection).toMatchObject({ executionId: memberB, stream: 'stderr' })

        act(() => result.current.clearSelection())
        expect(result.current.selection).toBeNull()
      })

      it('a retained Hide of A cannot clear B queued before it in the same React batch', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const hideA = result.current.clearSelection

        act(() => {
          result.current.select(memberB, 'stderr')
          hideA()
        })

        expect(result.current.selection).toMatchObject({ executionId: memberB, stream: 'stderr' })
      })

      it('a retained Hide of the first A visit cannot clear a later visit of A (A to B to A)', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const hideFirstA = result.current.clearSelection
        const firstVisit = result.current.selection?.visit
        act(() => result.current.select(memberB, 'stderr'))
        act(() => result.current.select(memberA, 'stdout'))

        act(() => hideFirstA())

        expect(result.current.selection).toMatchObject({ executionId: memberA, stream: 'stdout' })
        expect(result.current.selection?.visit).not.toBe(firstVisit)
        act(() => result.current.clearSelection())
        expect(result.current.selection).toBeNull()
      })

      it('Hide then reselect of the same stream in one batch is a new visit that the first Hide cannot end', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const firstVisit = result.current.selection?.visit
        const hideFirst = result.current.clearSelection

        act(() => {
          result.current.clearSelection()
          result.current.select(memberA, 'stdout')
        })

        expect(result.current.selection).toMatchObject({ executionId: memberA, stream: 'stdout' })
        expect(result.current.selection?.visit).not.toBe(firstVisit)
        act(() => hideFirst())
        expect(result.current.selection).toMatchObject({ executionId: memberA, stream: 'stdout' })
      })

      it('choosing the stream that is already chosen keeps its visit', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const visit = result.current.selection?.visit
        const hide = result.current.clearSelection

        act(() => result.current.select(memberA, 'stdout'))

        expect(result.current.selection?.visit).toBe(visit)
        act(() => hide())
        expect(result.current.selection).toBeNull()
      })

      it('a Hide retained across closing and reopening cannot clear a visit of the next opening with the same number', async () => {
        const { result } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const visit = result.current.selection?.visit
        const staleHide = result.current.clearSelection
        act(() => result.current.close())
        act(() => result.current.show())
        await act(async () => undefined)
        act(() => result.current.select(memberA, 'stdout'))
        expect(result.current.selection?.visit).toBe(visit)

        act(() => staleHide())

        expect(result.current.selection).toMatchObject({ executionId: memberA, stream: 'stdout' })
      })

      it('a Hide retained across a parent reload of the selection or an unmount is inert and reports no React error', async () => {
        const consoleError = vi.spyOn(console, 'error')
        const { result, rerender, unmount } = await opened()
        act(() => result.current.select(memberA, 'stdout'))
        const staleHide = result.current.clearSelection

        rerender({ ...A, selectionKey: 'selection-a-reloaded' })
        act(() => result.current.show())
        await act(async () => undefined)
        act(() => result.current.select(memberA, 'stdout'))
        act(() => staleHide())
        expect(result.current.selection).toMatchObject({ executionId: memberA, stream: 'stdout' })

        const currentHide = result.current.clearSelection
        unmount()
        currentHide()
        staleHide()

        expect(consoleError).not.toHaveBeenCalled()
        consoleError.mockRestore()
      })
    })
  })

  it('closing discards the answer and the read in flight; opening again is a new lifetime that the first read cannot settle', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const read = install(vi.fn<Read>().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise))
    const { result } = mounted(A)
    act(() => result.current.show())

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    expect(result.current.pending).toBe(false)
    await act(async () => {
      first.resolve(answerFor(A, ['Passed']))
      await first.promise
    })
    expect(result.current.evidence).toBeNull()

    act(() => result.current.show())
    expect(result.current.pending).toBe(true)
    expect(result.current.evidence).toBeNull()
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      second.resolve(answerFor(A, ['Passed', 'Passed', 'Passed']))
      await second.promise
    })
    expect(result.current.evidence?.status === 'Recorded' && result.current.evidence.members.length).toBe(3)
  })

  it('an older overlapping read of the same opening can never overwrite or settle a newer one', async () => {
    const older = deferred<unknown>()
    const newer = deferred<unknown>()
    const read = install(vi.fn<Read>().mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    // The same opening reads again when its target changes without a new selection (no product path does this today).
    rerender({ ...A, runId: 'run-2', expected: expectationFor('run-2', 2) })
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      newer.resolve(recordedVerificationFor('run-2', 2, ['Passed', 'Passed', 'Passed']))
      await newer.promise
    })
    await act(async () => {
      older.resolve(recordedVerificationFor('run-1', 2, ['Passed']))
      await older.promise
    })

    expect(result.current.evidence?.status === 'Recorded' && result.current.evidence.members.length).toBe(3)
  })

  it('a retained close or retry of an ended opening neither closes nor reads for its successor, even in the same batch', async () => {
    const read = install(() => Promise.reject(new Error('boom')))
    const { result } = mounted(A)
    act(() => result.current.show())
    await act(async () => undefined)
    const staleClose = result.current.close
    const staleRetry = result.current.retry

    act(() => {
      result.current.close()
      result.current.show()
    })
    await act(async () => undefined)
    expect(result.current.open).toBe(true)
    expect(read).toHaveBeenCalledTimes(2)

    act(() => {
      staleClose()
      staleRetry()
    })
    expect(result.current.open).toBe(true)
    expect(read).toHaveBeenCalledTimes(2)

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
  })

  it('another selection (A to B) shows nothing of A in its first frame and no late answer of A settles B', async () => {
    const lateA = deferred<unknown>()
    const read = install((_run, attemptId) => (attemptId === A.expected.attemptId ? lateA.promise : Promise.resolve(answerFor(B))))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    rerender(B)

    expect(result.current.open).toBe(false)
    expect(result.current.pending).toBe(false)
    expect(result.current.evidence).toBeNull()
    await act(async () => {
      lateA.resolve(answerFor(A, ['Passed', 'Passed', 'Passed']))
      await lateA.promise
    })
    expect(result.current.evidence).toBeNull()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(read).toHaveBeenLastCalledWith('run-1', B.expected.attemptId)
    expect(result.current.evidence?.status === 'Recorded' && result.current.evidence.members.length).toBe(2)
  })

  it('returning to A (A to B to A) starts closed and fresh, and the first visit read stays ignored', async () => {
    const firstA = deferred<unknown>()
    let visits = 0
    const read = install((_run, attemptId) =>
      attemptId === A.expected.attemptId && visits++ === 0 ? firstA.promise : Promise.resolve(answerFor(attemptId === A.expected.attemptId ? A : B, ['Passed'])))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())
    rerender(B)
    rerender({ ...A, selectionKey: 'selection-a-again' })

    expect(result.current.open).toBe(false)
    await act(async () => {
      firstA.resolve(answerFor(A, ['Passed', 'Passed', 'Passed']))
      await firstA.promise
    })
    expect(result.current.evidence).toBeNull()

    act(() => result.current.show())
    await act(async () => undefined)
    expect(result.current.evidence?.status === 'Recorded' && result.current.evidence.members.length).toBe(1)
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('a retained show of an ended selection opens nothing for its successor', () => {
    const read = install(() => Promise.resolve(answerFor(A)))
    const { result, rerender } = mounted(A)
    const staleShow = result.current.show

    rerender(B)
    act(() => staleShow())

    expect(result.current.open).toBe(false)
    expect(read).not.toHaveBeenCalled()
  })

  it('ending the selection closes the section and makes every callback inert', () => {
    const read = install(() => new Promise<unknown>(() => undefined))
    const { result, rerender } = mounted(A)
    act(() => result.current.show())

    rerender({ ...A, selectionKey: null })

    expect(result.current.open).toBe(false)
    act(() => {
      result.current.show()
      result.current.retry()
      result.current.select(executionIdFor('run-1', 2, 0), 'stdout')
    })
    expect(result.current.open).toBe(false)
    expect(result.current.selection).toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('unmounting leaves a pending read inert and reports no React error', async () => {
    const pending = deferred<unknown>()
    install(() => pending.promise)
    const consoleError = vi.spyOn(console, 'error')
    const { result, unmount } = mounted(A)
    act(() => result.current.show())
    const retainedRetry = result.current.retry
    const retainedSelect = result.current.select

    unmount()
    await act(async () => {
      pending.resolve(answerFor(A))
      await pending.promise
    })
    retainedRetry()
    retainedSelect(executionIdFor('run-1', 2, 0), 'stdout')

    expect(consoleError).not.toHaveBeenCalled()
    expect(agentAttemptVerificationEvidenceClient).toHaveBeenCalledTimes(1)
    consoleError.mockRestore()
  })
})
