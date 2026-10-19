import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptOutputMessageClient, agentAttemptOutputsClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { attemptIdFor } from '../agentAttemptHistoryFixture'
import { outputIdFor, recordedOutputsFor } from '../agentAttemptOutputsFixture'
import { recordedOutputMessageFor, unavailableOutputMessageFor } from '../agentAttemptOutputMessageFixture'
import { useAgentAttemptOutputs } from './useAgentAttemptOutputs'
import type { AgentAttemptOutputsTarget } from './useAgentAttemptOutputs'
import { useAgentAttemptOutputMessage } from './useAgentAttemptOutputMessage'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptOutputsClient: vi.fn(),
  agentAttemptOutputMessageClient: vi.fn(),
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

type ReadOutputs = (runId: string, attemptId: string) => Promise<unknown>
type ReadMessage = (runId: string, attemptId: string, messageId: string) => Promise<unknown>

function installOutputs(read: ReadOutputs) {
  const readAgentAttemptOutputs = vi.fn(read)
  vi.mocked(agentAttemptOutputsClient).mockReturnValue({ readAgentAttemptOutputs } as unknown as ReturnType<typeof agentAttemptOutputsClient>)
  return readAgentAttemptOutputs
}

function installMessage(read: ReadMessage) {
  const readAgentAttemptOutputMessage = vi.fn(read)
  vi.mocked(agentAttemptOutputMessageClient).mockReturnValue(
    { readAgentAttemptOutputMessage } as unknown as ReturnType<typeof agentAttemptOutputMessageClient>,
  )
  return readAgentAttemptOutputMessage
}

const expectationFor = (runId: string, attemptNumber: number): AgentAttemptIdentityExpectation => ({
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner',
  provider: 'Codex',
  responseContract: 'Proposal',
})

const A: AgentAttemptOutputsTarget = { selectionKey: 'selection-a', runId: 'run-1', expected: expectationFor('run-1', 2) }
const B: AgentAttemptOutputsTarget = { selectionKey: 'selection-b', runId: 'run-1', expected: expectationFor('run-1', 1) }

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const positionOf = (messageId: string) => Number(messageId.split('-').pop())
const outputsAnswer: ReadOutputs = (runId, attemptId) => Promise.resolve(recordedOutputsFor(runId, numberOf(attemptId), 12))
const messageAnswer: ReadMessage = (runId, attemptId, messageId) =>
  Promise.resolve(recordedOutputMessageFor(runId, numberOf(attemptId), positionOf(messageId)))

/** The section's composition: the outputs hook owns the opening and the visit; the message hook reads the chosen visit. */
function useComposed(target: AgentAttemptOutputsTarget) {
  const outputs = useAgentAttemptOutputs(target)
  const message = useAgentAttemptOutputMessage({
    openingKey: outputs.openingKey,
    runId: target.runId,
    expected: target.expected,
    selection: outputs.selection,
  })
  return { outputs, message }
}

async function opened(target: AgentAttemptOutputsTarget = A) {
  const rendered = renderHook((props: AgentAttemptOutputsTarget) => useComposed(props), { initialProps: target })
  act(() => rendered.result.current.outputs.show())
  await act(async () => undefined)
  return rendered
}

const idOf = (target: AgentAttemptOutputsTarget, position: number) => outputIdFor(target.runId, target.expected.attemptNumber, position)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'output-message-test-trace' })

describe('useAgentAttemptOutputMessage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing without a chosen output: not on render, not on opening and not for any loaded reference', async () => {
    installOutputs(outputsAnswer)
    const read = installMessage(messageAnswer)
    const idle = renderHook(() =>
      useAgentAttemptOutputMessage({ openingKey: 'opening', runId: 'run-1', expected: A.expected, selection: null }))

    const { result } = await opened()

    expect(result.current.outputs.outputs).toMatchObject({ status: 'Recorded', totalCount: 12 })
    act(() => result.current.outputs.showMore())
    expect(result.current.outputs.visibleCount).toBe(12)
    expect(read).not.toHaveBeenCalled()
    expect(agentAttemptOutputMessageClient).not.toHaveBeenCalled()
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    expect(idle.result.current).toMatchObject({ pending: false, answer: null, failure: null })
  })

  it('reads the chosen output exactly once for the selected run, attempt and message and exposes the validated answer', async () => {
    installOutputs(outputsAnswer)
    const pending = deferred<unknown>()
    const read = installMessage(() => pending.promise)
    const { result } = await opened()

    act(() => result.current.outputs.select(idOf(A, 11)))

    expect(result.current.message.pending).toBe(true)
    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1', A.expected.attemptId, idOf(A, 11))
    await act(async () => {
      pending.resolve(recordedOutputMessageFor('run-1', 2, 11))
      await pending.promise
    })

    expect(result.current.message.failure).toBeNull()
    expect(result.current.message.answer).toMatchObject({
      status: 'Recorded',
      output: { position: 11, collaborationMessageId: idOf(A, 11) },
      message: { id: idOf(A, 11), content: { status: 'Available', summary: 'Recorded summary of output 11.' } },
    })

    // Choosing the current output again is a no-op: no new visit and no new read.
    act(() => result.current.outputs.select(idOf(A, 11)))
    expect(result.current.outputs.selection?.visit).toBe(1)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['an unavailable set', () => Promise.resolve(unavailableOutputMessageFor('run-1', 2, 3)), { answer: { status: 'Unavailable' }, failure: null }],
    ['unavailable content', () => Promise.resolve(recordedOutputMessageFor('run-1', 2, 3, {}, { contentStatus: 'Unavailable', summary: null, fields: [] })), { failure: null }],
    ['an answer for another output', () => Promise.resolve(recordedOutputMessageFor('run-1', 2, 4)), { answer: null, failure: 'invalid' }],
    ['no body', () => Promise.resolve(undefined), { answer: null, failure: 'invalid' }],
    ['a failed request', () => Promise.reject(new Error('boom')), { answer: null, failure: 'read' }],
    ['a not-found problem', () => Promise.reject(notFound()), { answer: null, failure: 'not-found' }],
  ] as const)('reports %s as its own state', async (_, respond, state) => {
    installOutputs(outputsAnswer)
    installMessage(respond)
    const { result } = await opened()

    act(() => result.current.outputs.select(idOf(A, 3)))
    await act(async () => undefined)

    expect(result.current.message).toMatchObject({ pending: false, ...state })
    if (state.failure === null && !('answer' in state)) {
      expect(result.current.message.answer).toMatchObject({ status: 'Recorded', message: { content: { status: 'Unavailable' } } })
    }
  })

  it('retries the same visit only after a failure, once per click, and never while pending or after success', async () => {
    installOutputs(outputsAnswer)
    const second = deferred<unknown>()
    const read = installMessage(vi.fn<ReadMessage>().mockRejectedValueOnce(new Error('boom')).mockReturnValueOnce(second.promise))
    const { result } = await opened()
    act(() => result.current.message.retry())
    expect(read).not.toHaveBeenCalled()

    act(() => result.current.outputs.select(idOf(A, 2)))
    await act(async () => undefined)
    expect(result.current.message.failure).toBe('read')

    act(() => {
      result.current.message.retry()
      result.current.message.retry()
    })
    expect(read).toHaveBeenCalledTimes(2)
    expect(read).toHaveBeenLastCalledWith('run-1', A.expected.attemptId, idOf(A, 2))
    expect(result.current.message).toMatchObject({ pending: true, failure: null })
    act(() => result.current.message.retry())
    expect(read).toHaveBeenCalledTimes(2)

    await act(async () => {
      second.resolve(recordedOutputMessageFor('run-1', 2, 2))
      await second.promise
    })
    act(() => result.current.message.retry())
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.message.answer?.status).toBe('Recorded')
  })

  it('another output shows nothing of the first in its first frame, and the late first answer settles nothing', async () => {
    installOutputs(outputsAnswer)
    const lateFirst = deferred<unknown>()
    const read = installMessage((run, attempt, messageId) => (messageId === idOf(A, 0) ? lateFirst.promise : messageAnswer(run, attempt, messageId)))
    const { result } = await opened()
    act(() => result.current.outputs.select(idOf(A, 0)))

    act(() => result.current.outputs.select(idOf(A, 1)))
    expect(result.current.message).toMatchObject({ pending: true, answer: null, failure: null })
    await act(async () => undefined)
    await act(async () => {
      lateFirst.resolve(recordedOutputMessageFor('run-1', 2, 0))
      await lateFirst.promise
    })

    expect(result.current.message.answer).toMatchObject({ output: { position: 1 } })
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('a Close and reselection of the same output in one batch is a fresh visit whose first frame is pending and the first read is ignored', async () => {
    installOutputs(outputsAnswer)
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const read = installMessage(vi.fn<ReadMessage>().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise))
    const { result } = await opened()
    act(() => result.current.outputs.select(idOf(A, 5)))

    act(() => {
      result.current.outputs.clearSelection()
      result.current.outputs.select(idOf(A, 5))
    })

    expect(result.current.outputs.selection?.visit).toBe(2)
    expect(result.current.message).toMatchObject({ pending: true, answer: null })
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      first.resolve(recordedOutputMessageFor('run-1', 2, 5, {}, { summary: 'From the ended visit.' }))
      await first.promise
    })
    expect(result.current.message.answer).toBeNull()
    await act(async () => {
      second.resolve(recordedOutputMessageFor('run-1', 2, 5))
      await second.promise
    })
    expect(result.current.message.answer).toMatchObject({ message: { content: { summary: 'Recorded summary of output 5.' } } })
  })

  it('returning to an output (A-B-A) is a new visit; the first visit answer and its retained retry stay inert', async () => {
    installOutputs(outputsAnswer)
    const firstA = deferred<unknown>()
    let calls = 0
    const read = installMessage((run, attempt, messageId) => {
      calls += 1
      return calls === 1 ? firstA.promise : messageId === idOf(A, 0) ? Promise.reject(new Error('boom')) : messageAnswer(run, attempt, messageId)
    })
    const { result } = await opened()
    act(() => result.current.outputs.select(idOf(A, 0)))
    const firstRetry = result.current.message.retry

    act(() => result.current.outputs.select(idOf(A, 1)))
    await act(async () => undefined)
    act(() => result.current.outputs.select(idOf(A, 0)))
    await act(async () => undefined)
    expect(result.current.outputs.selection?.visit).toBe(3)
    expect(result.current.message.failure).toBe('read')

    await act(async () => {
      firstA.resolve(recordedOutputMessageFor('run-1', 2, 0))
      await firstA.promise
    })
    act(() => firstRetry())
    expect(result.current.message).toMatchObject({ answer: null, failure: 'read' })
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('closing the section ends the visit; reopening shows no earlier content and a retained retry reads nothing for the successor', async () => {
    installOutputs(outputsAnswer)
    const read = installMessage(() => Promise.reject(new Error('boom')))
    const { result } = await opened()
    act(() => result.current.outputs.select(idOf(A, 4)))
    await act(async () => undefined)
    const staleRetry = result.current.message.retry

    act(() => {
      result.current.outputs.close()
      result.current.outputs.show()
    })
    await act(async () => undefined)

    expect(result.current.outputs.selection).toBeNull()
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    act(() => staleRetry())
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('another attempt or a reload of the selection derives an empty frame immediately and a late answer settles nothing', async () => {
    installOutputs(outputsAnswer)
    const late = deferred<unknown>()
    const read = installMessage(() => late.promise)
    const { result, rerender } = await opened()
    act(() => result.current.outputs.select(idOf(A, 7)))

    rerender(B)
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    rerender({ ...A, selectionKey: 'selection-a-reloaded' })
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    await act(async () => {
      late.resolve(recordedOutputMessageFor('run-1', 2, 7))
      await late.promise
    })

    expect(result.current.message.answer).toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('unmounting leaves a pending content read inert and reports no React error', async () => {
    installOutputs(outputsAnswer)
    const pending = deferred<unknown>()
    installMessage(() => pending.promise)
    const consoleError = vi.spyOn(console, 'error')
    const { result, unmount } = await opened()
    act(() => result.current.outputs.select(idOf(A, 1)))
    const retainedRetry = result.current.message.retry

    unmount()
    await act(async () => {
      pending.resolve(recordedOutputMessageFor('run-1', 2, 1))
      await pending.promise
    })
    retainedRetry()

    expect(consoleError).not.toHaveBeenCalled()
    expect(agentAttemptOutputMessageClient).toHaveBeenCalledTimes(1)
    consoleError.mockRestore()
  })
})
