/// <reference types="node" />
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { GetRunCockpitResponse, ParticipantIdentityResponse, RunEventResponse } from '../../../api/generated/api-client'
import { runCockpitClient, runEventsClient } from '../../../api/clients'
import { createRunNotificationConnection } from '../../../api/runNotifications'
import { useRunCockpit } from './useRunCockpit'

vi.mock('../../../api/clients', () => ({
  runCockpitClient: vi.fn(),
  runEventsClient: vi.fn(),
}))
vi.mock('../../../api/runNotifications', () => ({
  createRunNotificationConnection: vi.fn(),
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

function cockpitFixture(runId: string): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId,
    projectId: 'project-1',
    projectName: 'DevalCopilot',
    executionNumber: 1,
    objective: 'Prove the walking skeleton',
    lifecycle: 'Running',
    stage: 'Plan',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 1,
    latestSequence: 0,
    stageMap: [],
    canPause: false,
    canStop: false,
  })
}

function eventFixture(sequence: number): RunEventResponse {
  return new RunEventResponse({
    sequence,
    id: `event-${sequence}`,
    attemptId: undefined,
    eventType: 'codex.proposal',
    actor: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    payloadJson: JSON.stringify({ summary: `step ${sequence}` }),
    occurredAtUtc: new Date('2026-01-01T00:00:00Z'),
  })
}

// A fake HubConnection that never dispatches an event until start() resolves, matching the
// real SignalR client: there is no live transport to deliver a notification on before then.
function createFakeHub() {
  const handlers: Record<string, ((...args: unknown[]) => void)[]> = {}
  const startSignal = deferred<void>()
  return {
    on: (event: string, handler: (...args: unknown[]) => void) => {
      ;(handlers[event] ??= []).push(handler)
    },
    onreconnecting: vi.fn((handler: () => void) => {
      ;(handlers.reconnecting ??= []).push(handler)
    }),
    onreconnected: vi.fn((handler: () => void) => {
      ;(handlers.reconnected ??= []).push(handler)
    }),
    onclose: vi.fn((handler: () => void) => {
      ;(handlers.close ??= []).push(handler)
    }),
    start: vi.fn(() => startSignal.promise),
    stop: vi.fn(() => Promise.resolve()),
    emit(event: string, ...args: unknown[]) {
      handlers[event]?.forEach((handler) => handler(...args))
    },
    resolveStart() {
      startSignal.resolve()
    },
  }
}

describe('useRunCockpit', () => {
  it("ignores a stale generation's response and never lets its cleanup unblock a newer generation's in-flight fetch", async () => {
    const getRunCockpit = vi.fn()
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)
    vi.mocked(createRunNotificationConnection).mockImplementation(
      () => createFakeHub() as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    const genOneCockpit = deferred<GetRunCockpitResponse>()
    const genOneEvents = deferred<RunEventResponse[]>()
    const genTwoEvents1 = deferred<RunEventResponse[]>()
    const genTwoEvents2 = deferred<RunEventResponse[]>()

    getRunCockpit
      .mockReturnValueOnce(genOneCockpit.promise)
      .mockReturnValue(Promise.resolve(cockpitFixture('run-b')))
    getRunEvents
      .mockReturnValueOnce(genOneEvents.promise) // generation 1's initial fetch
      .mockReturnValueOnce(genTwoEvents1.promise) // generation 2's initial fetch
      .mockReturnValueOnce(genTwoEvents2.promise) // generation 2's queued follow-up pass

    const { result, rerender } = renderHook(({ runId }) => useRunCockpit(runId), {
      initialProps: { runId: 'run-a' as string | null },
    })

    // Generation 1 started a fetch that never resolves before the run changes.
    expect(getRunEvents).toHaveBeenCalledTimes(1)

    rerender({ runId: 'run-b' })

    // Generation 2's own initial fetch is now in flight.
    expect(getRunEvents).toHaveBeenCalledTimes(2)

    // A notification for generation 2 arrives while its fetch is still in flight: it must
    // be coalesced into `pending`, never fire a third, overlapping call immediately.
    const hub = vi.mocked(createRunNotificationConnection).mock.results[1]?.value as ReturnType<typeof createFakeHub>
    act(() => {
      hub.emit('runAdvanced', { runId: 'run-b', latestSequence: 5 })
    })
    expect(getRunEvents).toHaveBeenCalledTimes(2)

    // Generation 1's stale fetch now resolves. Its result must be discarded, and its
    // `finally` block must not disturb generation 2's still-in-flight coordination state:
    // no third call must appear as a side effect of generation 1 settling.
    await act(async () => {
      genOneCockpit.resolve(cockpitFixture('run-a'))
      genOneEvents.resolve([eventFixture(1)])
    })
    expect(getRunEvents).toHaveBeenCalledTimes(2)
    expect(result.current.cockpit?.runId).not.toBe('run-a')

    // A second notification arrives for generation 2 while its own fetch is STILL pending
    // (genTwoEvents1 unresolved). If generation 1's finally block above wrongly cleared
    // generation 2's inFlight flag, this fires an immediate, overlapping third call right
    // now — before generation 2's own first fetch has even resolved.
    act(() => {
      hub.emit('runAdvanced', { runId: 'run-b', latestSequence: 6 })
    })
    expect(getRunEvents).toHaveBeenCalledTimes(2)

    // Generation 2's own fetch resolves; because a notification arrived mid-flight, exactly
    // one queued follow-up pass must run next — not zero, and not more than one.
    await act(async () => {
      genTwoEvents1.resolve([eventFixture(2)])
    })
    await waitFor(() => expect(getRunEvents).toHaveBeenCalledTimes(3))

    await act(async () => {
      genTwoEvents2.resolve([eventFixture(3)])
    })
    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([2, 3]))
    expect(getRunEvents).toHaveBeenCalledTimes(3)
    expect(result.current.cockpit?.runId).toBe('run-b')
  })

  it('runs an authoritative catch-up after the SignalR connection comes up, closing the initial query/connect gap', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

    const hub = createFakeHub()
    vi.mocked(createRunNotificationConnection).mockReturnValue(
      hub as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    // The initial query (before the connection exists) only sees the first event. A second
    // event is "committed" while the connection is still negotiating — nothing can notify
    // the client of it, since no transport is live yet.
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1)]))
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(2)]))

    const { result } = renderHook(() => useRunCockpit('run-a'))

    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1]))
    expect(result.current.connection).not.toBe('live')
    expect(getRunEvents).toHaveBeenCalledTimes(1)

    await act(async () => {
      hub.resolveStart()
    })

    await waitFor(() => expect(getRunEvents).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1, 2]))
    expect(result.current.connection).toBe('live')
  })

  it('deduplicates overlapping event ranges by sequence number', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)
    vi.mocked(createRunNotificationConnection).mockImplementation(
      () => createFakeHub() as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    // Two overlapping responses: [1,2,3] then [3,4] again, as could happen if a duplicated
    // notification ever triggered a second fetch covering an already-seen range.
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1), eventFixture(2), eventFixture(3)]))
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(3), eventFixture(4)]))

    const { result } = renderHook(() => useRunCockpit('run-a'))

    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1, 2, 3]))

    const hub = vi.mocked(createRunNotificationConnection).mock.results[0]?.value as ReturnType<typeof createFakeHub>
    await act(async () => {
      hub.emit('runAdvanced', { runId: 'run-a', latestSequence: 4 })
    })

    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1, 2, 3, 4]))
  })

  it('does not become live when the post-connect catch-up fails, and exposes a sync error', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

    const hub = createFakeHub()
    vi.mocked(createRunNotificationConnection).mockReturnValue(
      hub as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1)]))
    getRunEvents.mockImplementationOnce(() => Promise.reject(new Error('cockpit query unavailable')))

    const { result } = renderHook(() => useRunCockpit('run-a'))

    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1]))

    await act(async () => {
      hub.resolveStart()
    })

    await waitFor(() => expect(result.current.syncError).toBe('cockpit query unavailable'))
    // A connected transport is not the same as synchronized durable state: it must never
    // be reported as live when the authoritative catch-up that followed connecting failed.
    expect(result.current.connection).not.toBe('live')
    // The data already loaded before the connection came up must not be discarded.
    expect(result.current.cards.map((card) => card.sequence)).toEqual([1])
  })

  it('does not become live when a reconnect catch-up fails, and exposes a sync error', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

    const hub = createFakeHub()
    vi.mocked(createRunNotificationConnection).mockReturnValue(
      hub as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1)]))
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(2)]))

    const { result } = renderHook(() => useRunCockpit('run-a'))

    await act(async () => {
      hub.resolveStart()
    })
    await waitFor(() => expect(result.current.connection).toBe('live'))

    // A reconnect fires; this time the catch-up it triggers fails.
    getRunEvents.mockImplementationOnce(() => Promise.reject(new Error('reconnect sync failed')))
    act(() => {
      hub.emit('reconnected')
    })

    await waitFor(() => expect(result.current.syncError).toBe('reconnect sync failed'))
    expect(result.current.connection).not.toBe('live')
  })

  it('handles a notification-triggered catch-up rejection without an unhandled rejection', async () => {
    const unhandled: unknown[] = []
    const onUnhandledRejection = (reason: unknown) => unhandled.push(reason)
    process.on('unhandledRejection', onUnhandledRejection)

    try {
      const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
      const getRunEvents = vi.fn()
      vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
      vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

      const hub = createFakeHub()
      vi.mocked(createRunNotificationConnection).mockReturnValue(
        hub as unknown as ReturnType<typeof createRunNotificationConnection>,
      )

      getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1)]))
      getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(2)]))

      const { result } = renderHook(() => useRunCockpit('run-a'))

      await act(async () => {
        hub.resolveStart()
      })
      await waitFor(() => expect(result.current.connection).toBe('live'))

      getRunEvents.mockImplementationOnce(() => Promise.reject(new Error('notification sync failed')))
      await act(async () => {
        hub.emit('runAdvanced', { runId: 'run-a', latestSequence: 3 })
        // Give the rejected promise's microtasks a chance to surface as unhandled if the
        // hook were not catching them.
        await Promise.resolve()
        await Promise.resolve()
      })

      await waitFor(() => expect(result.current.syncError).toBe('notification sync failed'))
      expect(result.current.connection).not.toBe('live')
      expect(unhandled).toEqual([])
    } finally {
      process.off('unhandledRejection', onUnhandledRejection)
    }
  })

  it('recovers to live and clears the sync error once a later catch-up succeeds', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

    const hub = createFakeHub()
    vi.mocked(createRunNotificationConnection).mockReturnValue(
      hub as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    // The post-connect catch-up fails first.
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(1)]))
    getRunEvents.mockImplementationOnce(() => Promise.reject(new Error('cockpit query unavailable')))

    const { result } = renderHook(() => useRunCockpit('run-a'))

    await act(async () => {
      hub.resolveStart()
    })
    await waitFor(() => expect(result.current.syncError).toBe('cockpit query unavailable'))
    expect(result.current.connection).not.toBe('live')

    // A later notification's catch-up succeeds.
    getRunEvents.mockReturnValueOnce(Promise.resolve([eventFixture(2)]))
    act(() => {
      hub.emit('runAdvanced', { runId: 'run-a', latestSequence: 2 })
    })

    await waitFor(() => expect(result.current.connection).toBe('live'))
    expect(result.current.syncError).toBeNull()
    expect(result.current.cards.map((card) => card.sequence)).toEqual([1, 2])
  })

  it('classifies a coalesced post-connect failure as a sync error, judged by the pass that actually failed', async () => {
    const getRunCockpit = vi.fn().mockResolvedValue(cockpitFixture('run-a'))
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)

    const hub = createFakeHub()
    vi.mocked(createRunNotificationConnection).mockReturnValue(
      hub as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    const initialEvents = deferred<RunEventResponse[]>()
    getRunEvents.mockReturnValueOnce(initialEvents.promise) // the pre-connect initial fetch
    getRunEvents.mockImplementationOnce(() => Promise.reject(new Error('post-connect sync failed'))) // the queued pass

    const { result } = renderHook(() => useRunCockpit('run-a'))

    // The SignalR connection comes up WHILE the initial pre-connect fetch is still pending,
    // so it only queues a marksLive=true pass rather than running one immediately.
    await act(async () => {
      hub.resolveStart()
    })
    expect(getRunEvents).toHaveBeenCalledTimes(1)

    // The initial pass — the one this test's assertions must not be blamed on — now
    // succeeds and renders the cockpit.
    await act(async () => {
      initialEvents.resolve([eventFixture(1)])
    })
    await waitFor(() => expect(result.current.cards.map((card) => card.sequence)).toEqual([1]))
    // The queued post-connect pass now runs on its own and fails.
    await waitFor(() => expect(getRunEvents).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.syncError).toBe('post-connect sync failed'))

    // The fatal `error` belongs to the initial load, which succeeded — it must stay clear,
    // and the cockpit it already rendered must not be discarded.
    expect(result.current.error).toBeNull()
    expect(result.current.cockpit?.runId).toBe('run-a')
    expect(result.current.cards.map((card) => card.sequence)).toEqual([1])
    expect(result.current.connection).not.toBe('live')
  })

  it("a stale connection from a previous run cannot alter the current run's state", async () => {
    const getRunCockpit = vi.fn()
    const getRunEvents = vi.fn()
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)
    vi.mocked(createRunNotificationConnection).mockImplementation(
      () => createFakeHub() as unknown as ReturnType<typeof createRunNotificationConnection>,
    )

    getRunCockpit.mockImplementation((runId: string) => Promise.resolve(cockpitFixture(runId)))
    getRunEvents.mockResolvedValue([])

    const { result, rerender } = renderHook(({ runId }) => useRunCockpit(runId), {
      initialProps: { runId: 'run-a' as string | null },
    })

    const hubA = vi.mocked(createRunNotificationConnection).mock.results[0]?.value as ReturnType<typeof createFakeHub>
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-a'))

    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-b'))

    const before = {
      connection: result.current.connection,
      error: result.current.error,
      syncError: result.current.syncError,
      cockpit: result.current.cockpit,
      cards: result.current.cards,
    }

    // Every callback the old (run-a) connection can still fire after the switch — none of
    // them may touch the current (run-b) state.
    await act(async () => {
      hubA.emit('reconnecting')
      hubA.emit('reconnected')
      hubA.emit('close')
      hubA.emit('runAdvanced', { runId: 'run-a', latestSequence: 999 })
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(result.current.connection).toBe(before.connection)
    expect(result.current.error).toBe(before.error)
    expect(result.current.syncError).toBe(before.syncError)
    expect(result.current.cockpit).toBe(before.cockpit)
    expect(result.current.cards).toBe(before.cards)
  })
})
