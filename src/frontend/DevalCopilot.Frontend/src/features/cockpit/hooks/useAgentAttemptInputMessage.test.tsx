import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { agentAttemptInputMessageClient, agentAttemptInputsClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { attemptIdFor } from '../agentAttemptHistoryFixture'
import { inputIdFor, recordedInputsFor } from '../agentAttemptInputsFixture'
import { recordedInputMessageFor, unavailableInputMessageFor } from '../agentAttemptInputMessageFixture'
import { useAgentAttemptInputs } from './useAgentAttemptInputs'
import type { AgentAttemptInputsTarget } from './useAgentAttemptInputs'
import { useAgentAttemptInputMessage } from './useAgentAttemptInputMessage'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptInputsClient: vi.fn(),
  agentAttemptInputMessageClient: vi.fn(),
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

type ReadInputs = (runId: string, attemptId: string, limit?: number) => Promise<unknown>
type ReadMessage = (runId: string, attemptId: string, messageId: string) => Promise<unknown>

function installInputs(read: ReadInputs) {
  const readAgentAttemptInputs = vi.fn(read)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs } as unknown as ReturnType<typeof agentAttemptInputsClient>)
  return readAgentAttemptInputs
}

function installMessage(read: ReadMessage) {
  const readAgentAttemptInputMessage = vi.fn(read)
  vi.mocked(agentAttemptInputMessageClient).mockReturnValue(
    { readAgentAttemptInputMessage } as unknown as ReturnType<typeof agentAttemptInputMessageClient>,
  )
  return readAgentAttemptInputMessage
}

const expectationFor = (runId: string, attemptNumber: number): AgentAttemptIdentityExpectation => ({
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner',
  provider: 'Codex',
  responseContract: 'Proposal',
})

const A: AgentAttemptInputsTarget = { selectionKey: 'selection-a', runId: 'run-1', expected: expectationFor('run-1', 2) }
const B: AgentAttemptInputsTarget = { selectionKey: 'selection-b', runId: 'run-1', expected: expectationFor('run-1', 1) }

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const positionOf = (messageId: string) => Number(messageId.split('-').pop())
const inputsAnswer: ReadInputs = (runId, attemptId) => Promise.resolve(recordedInputsFor(runId, numberOf(attemptId), 12, {}, 64))
const messageAnswer: ReadMessage = (runId, attemptId, messageId) =>
  Promise.resolve(recordedInputMessageFor(runId, numberOf(attemptId), positionOf(messageId)))

/** The section's composition: the inputs hook owns the opening and the visit; the message hook reads the chosen visit. */
function useComposed(target: AgentAttemptInputsTarget) {
  const inputs = useAgentAttemptInputs(target)
  const message = useAgentAttemptInputMessage({ openingKey: inputs.openingKey, runId: target.runId, expected: target.expected, selection: inputs.selection })
  return { inputs, message }
}

async function opened(target: AgentAttemptInputsTarget = A) {
  const rendered = renderHook((props: AgentAttemptInputsTarget) => useComposed(props), { initialProps: target })
  act(() => rendered.result.current.inputs.show())
  await act(async () => undefined)
  return rendered
}

const idOf = (target: AgentAttemptInputsTarget, position: number) => inputIdFor(target.runId, target.expected.attemptNumber, position)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'input-message-test-trace' })

describe('useAgentAttemptInputMessage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads nothing without a chosen input: not on render, not on opening and not for any loaded reference', async () => {
    installInputs(inputsAnswer)
    const read = installMessage(messageAnswer)
    const idle = renderHook(() =>
      useAgentAttemptInputMessage({ openingKey: 'opening', runId: 'run-1', expected: A.expected, selection: null }))

    const { result } = await opened()

    expect(result.current.inputs.inputs).toMatchObject({ status: 'Recorded', totalCount: 12 })
    act(() => result.current.inputs.showMore())
    expect(result.current.inputs.visibleCount).toBe(12)
    expect(read).not.toHaveBeenCalled()
    expect(agentAttemptInputMessageClient).not.toHaveBeenCalled()
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    expect(idle.result.current).toMatchObject({ pending: false, answer: null, failure: null })
  })

  it('reads the chosen input exactly once for the selected run, attempt and message and exposes the validated answer', async () => {
    installInputs(inputsAnswer)
    const pending = deferred<unknown>()
    const read = installMessage(() => pending.promise)
    const { result } = await opened()

    act(() => result.current.inputs.select(idOf(A, 11)))

    expect(result.current.message.pending).toBe(true)
    expect(read).toHaveBeenCalledTimes(1)
    expect(read).toHaveBeenCalledWith('run-1', A.expected.attemptId, idOf(A, 11))
    await act(async () => {
      pending.resolve(recordedInputMessageFor('run-1', 2, 11))
      await pending.promise
    })

    expect(result.current.message.failure).toBeNull()
    expect(result.current.message.answer).toMatchObject({
      status: 'Recorded',
      input: { position: 11, collaborationMessageId: idOf(A, 11) },
      message: { id: idOf(A, 11), content: { status: 'Available', summary: 'Recorded summary of input 11.' } },
    })

    // Choosing the current input again is a no-op: no new visit and no new read.
    act(() => result.current.inputs.select(idOf(A, 11)))
    expect(result.current.inputs.selection?.visit).toBe(1)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['an unavailable set', () => Promise.resolve(unavailableInputMessageFor('run-1', 2, 3)), { answer: { status: 'Unavailable' }, failure: null }],
    ['unavailable content', () => Promise.resolve(recordedInputMessageFor('run-1', 2, 3, {}, { contentStatus: 'Unavailable', summary: null, fields: [] })), { failure: null }],
    ['an answer for another input', () => Promise.resolve(recordedInputMessageFor('run-1', 2, 4)), { answer: null, failure: 'invalid' }],
    ['no body', () => Promise.resolve(undefined), { answer: null, failure: 'invalid' }],
    ['a failed request', () => Promise.reject(new Error('boom')), { answer: null, failure: 'read' }],
    ['a not-found problem', () => Promise.reject(notFound()), { answer: null, failure: 'not-found' }],
  ] as const)('reports %s as its own state', async (_, respond, state) => {
    installInputs(inputsAnswer)
    installMessage(respond)
    const { result } = await opened()

    act(() => result.current.inputs.select(idOf(A, 3)))
    await act(async () => undefined)

    expect(result.current.message).toMatchObject({ pending: false, ...state })
    if (state.failure === null && !('answer' in state)) {
      expect(result.current.message.answer).toMatchObject({ status: 'Recorded', message: { content: { status: 'Unavailable' } } })
    }
  })

  it('retries the same visit only after a failure, once per click, and never while pending or after success', async () => {
    installInputs(inputsAnswer)
    const second = deferred<unknown>()
    const read = installMessage(vi.fn<ReadMessage>().mockRejectedValueOnce(new Error('boom')).mockReturnValueOnce(second.promise))
    const { result } = await opened()
    act(() => result.current.message.retry())
    expect(read).not.toHaveBeenCalled()

    act(() => result.current.inputs.select(idOf(A, 2)))
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
      second.resolve(recordedInputMessageFor('run-1', 2, 2))
      await second.promise
    })
    act(() => result.current.message.retry())
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.message.answer?.status).toBe('Recorded')
  })

  it('another input shows nothing of the first in its first frame, and the late first answer settles nothing', async () => {
    installInputs(inputsAnswer)
    const lateFirst = deferred<unknown>()
    const read = installMessage((run, attempt, messageId) => (messageId === idOf(A, 0) ? lateFirst.promise : messageAnswer(run, attempt, messageId)))
    const { result } = await opened()
    act(() => result.current.inputs.select(idOf(A, 0)))

    act(() => result.current.inputs.select(idOf(A, 1)))
    expect(result.current.message).toMatchObject({ pending: true, answer: null, failure: null })
    await act(async () => undefined)
    await act(async () => {
      lateFirst.resolve(recordedInputMessageFor('run-1', 2, 0))
      await lateFirst.promise
    })

    expect(result.current.message.answer).toMatchObject({ input: { position: 1 } })
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('a Close and reselection of the same input in one batch is a fresh visit whose first frame is pending and the first read is ignored', async () => {
    installInputs(inputsAnswer)
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const read = installMessage(vi.fn<ReadMessage>().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise))
    const { result } = await opened()
    act(() => result.current.inputs.select(idOf(A, 5)))

    act(() => {
      result.current.inputs.clearSelection()
      result.current.inputs.select(idOf(A, 5))
    })

    expect(result.current.inputs.selection?.visit).toBe(2)
    expect(result.current.message).toMatchObject({ pending: true, answer: null })
    expect(read).toHaveBeenCalledTimes(2)
    await act(async () => {
      first.resolve(recordedInputMessageFor('run-1', 2, 5, {}, { summary: 'From the ended visit.' }))
      await first.promise
    })
    expect(result.current.message.answer).toBeNull()
    await act(async () => {
      second.resolve(recordedInputMessageFor('run-1', 2, 5))
      await second.promise
    })
    expect(result.current.message.answer).toMatchObject({ message: { content: { summary: 'Recorded summary of input 5.' } } })
  })

  it('returning to an input (A-B-A) is a new visit; the first visit answer and its retained retry stay inert', async () => {
    installInputs(inputsAnswer)
    const firstA = deferred<unknown>()
    let calls = 0
    const read = installMessage((run, attempt, messageId) => {
      calls += 1
      return calls === 1 ? firstA.promise : messageId === idOf(A, 0) ? Promise.reject(new Error('boom')) : messageAnswer(run, attempt, messageId)
    })
    const { result } = await opened()
    act(() => result.current.inputs.select(idOf(A, 0)))
    const firstRetry = result.current.message.retry

    act(() => result.current.inputs.select(idOf(A, 1)))
    await act(async () => undefined)
    act(() => result.current.inputs.select(idOf(A, 0)))
    await act(async () => undefined)
    expect(result.current.inputs.selection?.visit).toBe(3)
    expect(result.current.message.failure).toBe('read')

    await act(async () => {
      firstA.resolve(recordedInputMessageFor('run-1', 2, 0))
      await firstA.promise
    })
    act(() => firstRetry())
    expect(result.current.message).toMatchObject({ answer: null, failure: 'read' })
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('closing the section ends the visit; reopening shows no earlier content and a retained retry reads nothing for the successor', async () => {
    installInputs(inputsAnswer)
    const read = installMessage(() => Promise.reject(new Error('boom')))
    const { result } = await opened()
    act(() => result.current.inputs.select(idOf(A, 4)))
    await act(async () => undefined)
    const staleRetry = result.current.message.retry

    act(() => {
      result.current.inputs.close()
      result.current.inputs.show()
    })
    await act(async () => undefined)

    expect(result.current.inputs.selection).toBeNull()
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    act(() => staleRetry())
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('another attempt or a reload of the selection derives an empty frame immediately and a late answer settles nothing', async () => {
    installInputs(inputsAnswer)
    const late = deferred<unknown>()
    const read = installMessage(() => late.promise)
    const { result, rerender } = await opened()
    act(() => result.current.inputs.select(idOf(A, 7)))

    rerender(B)
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    rerender({ ...A, selectionKey: 'selection-a-reloaded' })
    expect(result.current.message).toMatchObject({ pending: false, answer: null, failure: null })
    await act(async () => {
      late.resolve(recordedInputMessageFor('run-1', 2, 7))
      await late.promise
    })

    expect(result.current.message.answer).toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('unmounting leaves a pending content read inert and reports no React error', async () => {
    installInputs(inputsAnswer)
    const pending = deferred<unknown>()
    installMessage(() => pending.promise)
    const consoleError = vi.spyOn(console, 'error')
    const { result, unmount } = await opened()
    act(() => result.current.inputs.select(idOf(A, 1)))
    const retainedRetry = result.current.message.retry

    unmount()
    await act(async () => {
      pending.resolve(recordedInputMessageFor('run-1', 2, 1))
      await pending.promise
    })
    retainedRetry()

    expect(consoleError).not.toHaveBeenCalled()
    expect(agentAttemptInputMessageClient).toHaveBeenCalledTimes(1)
    consoleError.mockRestore()
  })
})
