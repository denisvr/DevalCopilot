import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionSequenceStatusClient } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { localNoneStatusFor, localStatusFor, localStepFor } from '../localExecutionSequenceFixture'
import {
  LOCAL_EXECUTION_MAX_CONSECUTIVE_FAILURES,
  LOCAL_EXECUTION_POLL_INTERVAL_MS,
  LOCAL_EXECUTION_READ_TIMEOUT_MS,
  useLocalExecutionSequence,
} from './useLocalExecutionSequence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionSequenceStatusClient: vi.fn(),
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

const signals: AbortSignal[] = []

function install(read: (runId: string) => Promise<unknown>) {
  const readLocalExecutionSequence = vi.fn(read)
  vi.mocked(localExecutionSequenceStatusClient).mockImplementation((signal?: AbortSignal) => {
    if (signal !== undefined) {
      signals.push(signal)
    }

    return { readLocalExecutionSequence } as unknown as ReturnType<typeof localExecutionSequenceStatusClient>
  })
  return readLocalExecutionSequence
}

const mounted = (runId: string | null, hint?: number) =>
  renderHook<ReturnType<typeof useLocalExecutionSequence>, { runId: string | null; hint?: number }>(
    (props) => useLocalExecutionSequence(props.runId, props.hint),
    { initialProps: { runId, hint } },
  )

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useLocalExecutionSequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    signals.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads once on mount, is unknown while pending and then exposes the validated status and its control', async () => {
    const pending = deferred<unknown>()
    const read = install(() => pending.promise)

    const { result } = mounted('run-1')

    expect(read).toHaveBeenCalledTimes(1)
    expect(result.current).toMatchObject({ loading: true, status: null, control: 'unknown', failure: null })

    await act(async () => {
      pending.resolve(localNoneStatusFor('run-1'))
      await pending.promise
    })

    expect(result.current).toMatchObject({ loading: false, control: 'none', failure: null, runId: 'run-1' })
    expect(result.current.status?.status).toBe('None')
  })

  it('reads nothing and states no fence while no run is selected', () => {
    const read = install(() => Promise.resolve(localNoneStatusFor('run-1')))

    const { result } = mounted(null)

    expect(read).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ runId: null, status: null, control: 'none', loading: false, polling: false })
  })

  it('polls at most once per five seconds while authority is unresolved and never otherwise', async () => {
    const read = install(() => Promise.resolve(localStatusFor('run-1', 'AwaitingStepResult')))
    const { result } = mounted('run-1')
    await flush()

    expect(result.current).toMatchObject({ control: 'blocked', polling: true })
    expect(read).toHaveBeenCalledTimes(1)

    await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS - 1)
    expect(read).toHaveBeenCalledTimes(1)
    await tick(1)
    expect(read).toHaveBeenCalledTimes(2)
    await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('does not poll a released or empty status', async () => {
    const read = install(() => Promise.resolve(localNoneStatusFor('run-1')))
    const { result } = mounted('run-1')
    await flush()

    expect(result.current.polling).toBe(false)
    await tick(60_000)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('keeps polling a terminal sequence that still owns an unresolved operation', async () => {
    const raw = localStatusFor(
      'run-1',
      'NeedsAttention',
      {},
      { steps: [localStepFor(0, 'quarantined')], reasonCode: 'exit_evidence_missing', reasonText: 'A dispatched operation ended without exit evidence.' },
    )
    expect(raw.manualFence).toBe('Blocked')
    const read = install(() => Promise.resolve(raw))
    const { result } = mounted('run-1')
    await flush()

    expect(result.current).toMatchObject({ control: 'blocked', polling: true })
    await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('aborts a hung read after ten seconds, masks the earlier enabling reading and queues no second poll', async () => {
    let call = 0
    const read = install(() => {
      call += 1
      return call === 1 ? Promise.resolve(localNoneStatusFor('run-1')) : new Promise<unknown>(() => undefined)
    })
    const { result, rerender } = mounted('run-1', 0)
    await flush()
    expect(result.current.control).toBe('none')

    rerender({ runId: 'run-1', hint: 1 })
    await flush()
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.control).toBe('none')

    await tick(LOCAL_EXECUTION_READ_TIMEOUT_MS)

    expect(signals[1].aborted).toBe(true)
    expect(result.current).toMatchObject({ status: null, control: 'unknown', failure: 'timeout' })
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('masks the earlier reading on a transport failure and reads again only by its one timer', async () => {
    let call = 0
    const read = install(() => {
      call += 1
      return call === 1 ? Promise.resolve(localStatusFor('run-1', 'AwaitingStepResult')) : Promise.reject(new Error('network'))
    })
    const { result } = mounted('run-1')
    await flush()
    expect(result.current.control).toBe('blocked')

    await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS)
    expect(result.current).toMatchObject({ status: null, control: 'unknown', failure: 'read' })
    expect(read).toHaveBeenCalledTimes(2)

    await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS)
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('stops polling after the maximum number of consecutive failures', async () => {
    const read = install(() => Promise.reject(new Error('network')))
    const { result } = mounted('run-1')
    await flush()
    for (let index = 0; index < 10; index += 1) {
      await tick(LOCAL_EXECUTION_POLL_INTERVAL_MS)
    }

    expect(read).toHaveBeenCalledTimes(LOCAL_EXECUTION_MAX_CONSECUTIVE_FAILURES)
    expect(result.current).toMatchObject({ polling: false, control: 'unknown' })

    await act(async () => result.current.refresh())
    expect(read).toHaveBeenCalledTimes(LOCAL_EXECUTION_MAX_CONSECUTIVE_FAILURES + 1)
  })

  it('reports a missing run, an incoherent answer and an unavailable fence as unknown', async () => {
    const answers: [unknown, string][] = [
      [Promise.reject(new ApiException('x', 404, '', {}, null)), 'not-found'],
      [Promise.resolve({ nonsense: true }), 'invalid'],
      [Promise.resolve(localNoneStatusFor('run-1', { manualFence: 'Unavailable' })), 'unavailable'],
    ]
    for (const [answer, failure] of answers) {
      const read = install(() => answer as Promise<unknown>)
      const { result, unmount } = mounted('run-1')
      await flush()
      expect(read).toHaveBeenCalled()
      expect(result.current).toMatchObject({ status: null, control: 'unknown', failure })
      unmount()
    }
  })

  it('coalesces every trigger that arrives while a read is pending into exactly one further read', async () => {
    const first = deferred<unknown>()
    let call = 0
    const read = install(() => {
      call += 1
      return call === 1 ? first.promise : Promise.resolve(localNoneStatusFor('run-1'))
    })
    const { result, rerender } = mounted('run-1', 0)

    rerender({ runId: 'run-1', hint: 1 })
    rerender({ runId: 'run-1', hint: 2 })
    await act(async () => {
      result.current.refresh()
      result.current.refresh()
    })
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      first.resolve(localNoneStatusFor('run-1'))
      await first.promise
    })
    await flush()

    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current.control).toBe('none')
  })

  it('never exposes another run or an earlier visit of the same run, and ignores their late answers', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<unknown>>[]>()
    const read = install((runId) => {
      const next = deferred<unknown>()
      answers.set(runId, [...(answers.get(runId) ?? []), next])
      return next.promise
    })
    const { result, rerender } = mounted('run-a')
    await act(async () => {
      answers.get('run-a')![0].resolve(localNoneStatusFor('run-a'))
    })
    expect(result.current.control).toBe('none')

    rerender({ runId: 'run-b' })
    expect(result.current).toMatchObject({ status: null, control: 'unknown', loading: true, runId: 'run-b' })

    rerender({ runId: 'run-a' })
    expect(result.current).toMatchObject({ status: null, control: 'unknown', loading: true, runId: 'run-a' })

    await act(async () => {
      answers.get('run-b')![0].resolve(localNoneStatusFor('run-b'))
    })
    expect(result.current.status).toBeNull()

    await act(async () => {
      answers.get('run-a')![1].resolve(localStatusFor('run-a', 'Armed'))
    })
    expect(result.current).toMatchObject({ control: 'blocked', runId: 'run-a' })
    expect(read).toHaveBeenCalledTimes(3)
  })

  it('aborts the pending request when the visit ends', async () => {
    install(() => new Promise<unknown>(() => undefined))
    const { unmount } = mounted('run-1')
    expect(signals[0].aborted).toBe(false)

    unmount()

    expect(signals[0].aborted).toBe(true)
  })

  it('masks the status from a command until a read that began after its transport settled has settled', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    let call = 0
    const read = install(() => {
      call += 1
      return call === 1 ? first.promise : second.promise
    })
    const { result } = mounted('run-1')
    await act(async () => {
      first.resolve(localNoneStatusFor('run-1'))
      await first.promise
    })
    expect(result.current.control).toBe('none')

    let ticket: ReturnType<typeof result.current.beginCommand> = null
    await act(async () => {
      ticket = result.current.beginCommand()
    })
    expect(ticket).not.toBeNull()
    expect(result.current).toMatchObject({ status: null, control: 'unknown', confirming: true })
    expect(result.current.beginCommand()).toBeNull()

    await act(async () => ticket!.settled())
    expect(read).toHaveBeenCalledTimes(2)
    expect(result.current).toMatchObject({ status: null, control: 'unknown', confirming: true })

    await act(async () => {
      second.resolve(localStatusFor('run-1', 'Armed'))
      await second.promise
    })
    expect(result.current).toMatchObject({ control: 'blocked', confirming: false })
    expect(result.current.beginCommand()).not.toBeNull()
  })

  it('ignores a read that began before the command settled and reads once more after it', async () => {
    const early = deferred<unknown>()
    const late = deferred<unknown>()
    let call = 0
    const read = install(() => {
      call += 1
      return call === 1 ? early.promise : late.promise
    })
    const { result } = mounted('run-1')

    let ticket: ReturnType<typeof result.current.beginCommand> = null
    await act(async () => {
      ticket = result.current.beginCommand()
    })
    await act(async () => ticket!.settled())
    expect(read).toHaveBeenCalledTimes(1)

    await act(async () => {
      early.resolve(localNoneStatusFor('run-1'))
      await early.promise
    })
    expect(result.current).toMatchObject({ status: null, control: 'unknown', confirming: true })
    expect(read).toHaveBeenCalledTimes(2)

    await act(async () => {
      late.resolve(localNoneStatusFor('run-1'))
      await late.promise
    })
    expect(result.current).toMatchObject({ control: 'none', confirming: false })
  })

  it('keeps a failed confirmation masked and gives an ended visit no ticket', async () => {
    install(() => Promise.reject(new Error('network')))
    const { result, rerender } = mounted('run-1')
    await flush()
    const stale = result.current.beginCommand

    let ticket: ReturnType<typeof result.current.beginCommand> = null
    await act(async () => {
      ticket = result.current.beginCommand()
    })
    await act(async () => ticket!.settled())
    await flush()
    expect(result.current).toMatchObject({ control: 'unknown', confirming: false })

    rerender({ runId: 'run-2' })
    expect(stale()).toBeNull()
  })
})
