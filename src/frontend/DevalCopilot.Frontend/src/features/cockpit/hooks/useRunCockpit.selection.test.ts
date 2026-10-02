/// <reference types="node" />
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

function cockpitFixture(runId: string, objective = `objective of ${runId}`, latestSequence = 0): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId,
    projectId: `project-of-${runId}`,
    projectName: `Project of ${runId}`,
    executionNumber: 1,
    objective,
    lifecycle: 'Running',
    stage: 'Plan',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 1,
    latestSequence,
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

type Hub = ReturnType<typeof createFakeHub>

interface Frame {
  selected: string | null
  cockpitRunId: string | undefined
  objective: string | undefined
  latestSequence: number | undefined
  cardIds: string[]
  loading: boolean
  error: string | null
  syncError: string | null
  connection: string
}

interface Setup {
  hubs: Hub[]
  getRunCockpit: ReturnType<typeof vi.fn>
  getRunEvents: ReturnType<typeof vi.fn>
}

function setup(): Setup {
  const getRunCockpit = vi.fn()
  const getRunEvents = vi.fn()
  const hubs: Hub[] = []
  vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
  vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as unknown as ReturnType<typeof runEventsClient>)
  vi.mocked(createRunNotificationConnection).mockImplementation(() => {
    const hub = createFakeHub()
    hubs.push(hub)
    return hub as unknown as ReturnType<typeof createRunNotificationConnection>
  })
  return { hubs, getRunCockpit, getRunEvents }
}

// Records every committed render so a frame that shows the previous selection's evidence cannot
// hide behind a later, correct state.
function renderRecorded(initial: string | null) {
  const frames: Frame[] = []
  const rendered = renderHook(
    ({ runId }: { runId: string | null }) => {
      const result = useRunCockpit(runId)
      frames.push({
        selected: runId,
        cockpitRunId: result.cockpit?.runId,
        objective: result.cockpit?.objective,
        latestSequence: result.cockpit?.latestSequence,
        cardIds: result.cards.map((card) => card.id),
        loading: result.loading,
        error: result.error,
        syncError: result.syncError,
        connection: result.connection,
      })
      return result
    },
    { initialProps: { runId: initial } },
  )
  return { ...rendered, frames }
}

function framesFor(frames: Frame[], runId: string | null, from = 0) {
  return frames.slice(from).filter((frame) => frame.selected === runId)
}

describe('useRunCockpit selection ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows no cockpit, events or settled state of run A in any committed frame while pending run B is selected', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    const b = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockResolvedValueOnce(cockpitFixture('run-a', 'objective A', 7)).mockReturnValueOnce(b.promise)
    getRunEvents.mockResolvedValueOnce([eventFixture(1), eventFixture(2)]).mockResolvedValueOnce([])

    const { result, rerender, frames } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-a'))
    expect(result.current.cards.map((card) => card.id)).toEqual(['event-1', 'event-2'])

    const mark = frames.length
    rerender({ runId: 'run-b' })

    const bFrames = framesFor(frames, 'run-b', mark)
    expect(bFrames.length).toBeGreaterThan(0)
    for (const frame of bFrames) {
      expect(frame.cockpitRunId).toBeUndefined()
      expect(frame.cardIds).toEqual([])
      expect(frame.loading).toBe(true)
      expect(frame.error).toBeNull()
      expect(frame.connection).toBe('connecting')
    }
    // The new selection's own requests start from its own cursor, never the previous run's.
    expect(getRunCockpit).toHaveBeenLastCalledWith('run-b')
    expect(getRunEvents).toHaveBeenLastCalledWith('run-b', 0)

    b.resolve(cockpitFixture('run-b', 'objective B', 3))
    await waitFor(() => expect(result.current.cockpit?.objective).toBe('objective B'))
    expect(result.current.cards).toEqual([])
  })

  it('exposes no cockpit once the selection becomes null, and none after A is selected again', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    getRunCockpit.mockResolvedValue(cockpitFixture('run-a', 'objective A'))
    getRunEvents.mockResolvedValue([eventFixture(1)])

    const { result, rerender, frames } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-a'))

    const mark = frames.length
    rerender({ runId: null })
    const nullFrames = framesFor(frames, null, mark)
    expect(nullFrames.length).toBeGreaterThan(0)
    for (const frame of nullFrames) {
      expect(frame.cockpitRunId).toBeUndefined()
      expect(frame.cardIds).toEqual([])
      expect(frame.loading).toBe(false)
      expect(frame.error).toBeNull()
    }
    expect(getRunCockpit).toHaveBeenCalledTimes(1)

    const again = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValueOnce(again.promise)
    const markAgain = frames.length
    rerender({ runId: 'run-a' })
    for (const frame of framesFor(frames, 'run-a', markAgain)) {
      // A returning selection is a new lifetime: the earlier snapshot is not resurrected.
      expect(frame.cockpitRunId).toBeUndefined()
      expect(frame.cardIds).toEqual([])
      expect(frame.loading).toBe(true)
    }
    again.resolve(cockpitFixture('run-a', 'objective A, loaded again'))
    await waitFor(() => expect(result.current.cockpit?.objective).toBe('objective A, loaded again'))
  })

  it('ignores a late success of a replaced A after B and the returning A are selected (A to B to A)', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    const firstA = deferred<GetRunCockpitResponse>()
    const firstAEvents = deferred<RunEventResponse[]>()
    const secondA = deferred<GetRunCockpitResponse>()
    getRunCockpit
      .mockReturnValueOnce(firstA.promise)
      .mockResolvedValueOnce(cockpitFixture('run-b', 'objective B'))
      .mockReturnValueOnce(secondA.promise)
    getRunEvents
      .mockReturnValueOnce(firstAEvents.promise)
      .mockResolvedValueOnce([eventFixture(10)])
      .mockResolvedValue([])

    const { result, rerender, frames } = renderRecorded('run-a')
    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.cockpit?.objective).toBe('objective B'))
    rerender({ runId: 'run-a' })
    expect(result.current.cockpit).toBeNull()

    const mark = frames.length
    await act(async () => {
      firstA.resolve(cockpitFixture('run-a', 'stale objective A', 99))
      firstAEvents.resolve([eventFixture(98)])
    })
    expect(result.current.cockpit).toBeNull()
    expect(result.current.cards).toEqual([])
    expect(result.current.loading).toBe(true)
    for (const frame of frames.slice(mark)) {
      expect(frame.objective).toBeUndefined()
    }

    await act(async () => {
      secondA.resolve(cockpitFixture('run-a', 'current objective A', 1))
    })
    await waitFor(() => expect(result.current.cockpit?.objective).toBe('current objective A'))
    expect(result.current.cards).toEqual([])
  })

  it('ignores a late failure of the replaced selection', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    const a = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValueOnce(a.promise).mockResolvedValue(cockpitFixture('run-b', 'objective B'))
    getRunEvents.mockResolvedValue([])

    const { result, rerender } = renderRecorded('run-a')
    rerender({ runId: 'run-b' })
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-b'))

    await act(async () => {
      a.reject(new Error('run A failed late'))
    })
    expect(result.current.error).toBeNull()
    expect(result.current.syncError).toBeNull()
    expect(result.current.cockpit?.objective).toBe('objective B')
  })

  it('does not adopt a response that names another run, and surfaces a truthful error instead', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    getRunCockpit.mockResolvedValue(cockpitFixture('run-a', 'objective A'))
    getRunEvents.mockResolvedValue([eventFixture(1)])

    const { result, frames } = renderRecorded('run-b')
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.cockpit).toBeNull()
    expect(result.current.cards).toEqual([])
    expect(result.current.loading).toBe(false)
    for (const frame of frames) {
      expect(frame.cockpitRunId).toBeUndefined()
      expect(frame.cardIds).toEqual([])
    }
  })

  it('does not carry the previous selection error or sync error into the next selection', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    getRunCockpit.mockRejectedValueOnce(new Error('run A could not be loaded'))
    getRunEvents.mockResolvedValue([])
    const b = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValueOnce(b.promise)

    const { result, rerender, frames } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.error).toBe('run A could not be loaded'))

    const mark = frames.length
    rerender({ runId: 'run-b' })
    for (const frame of framesFor(frames, 'run-b', mark)) {
      expect(frame.error).toBeNull()
      expect(frame.loading).toBe(true)
    }
  })

  it('releases a refresh waiter of the replaced selection with false and starts no request for the replacement', async () => {
    const { getRunCockpit, getRunEvents, hubs } = setup()
    getRunCockpit.mockResolvedValueOnce(cockpitFixture('run-a'))
    getRunEvents.mockResolvedValueOnce([])
    const aRefresh = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValueOnce(aRefresh.promise)
    getRunEvents.mockResolvedValue([])

    const { result, rerender } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-a'))
    act(() => hubs[0].resolveStart())
    await waitFor(() => expect(getRunCockpit).toHaveBeenCalledTimes(2)) // post-connect catch-up in flight
    const staleRefresh = result.current.refresh
    const pending = staleRefresh()

    const b = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValueOnce(b.promise)
    rerender({ runId: 'run-b' })
    await expect(pending).resolves.toBe(false)
    const callsAfterSwitch = getRunCockpit.mock.calls.length

    // A handler retained from run A's render must neither refresh nor wait on run B.
    await expect(staleRefresh()).resolves.toBe(false)
    expect(getRunCockpit).toHaveBeenCalledTimes(callsAfterSwitch)

    await act(async () => {
      aRefresh.resolve(cockpitFixture('run-a', 'stale refresh result'))
    })
    expect(result.current.cockpit).toBeNull()
  })

  it('ignores notifications and reconnects delivered by the replaced selection transport', async () => {
    const { getRunCockpit, getRunEvents, hubs } = setup()
    getRunCockpit.mockResolvedValue(cockpitFixture('run-a'))
    getRunEvents.mockResolvedValue([])
    const { result, rerender } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.cockpit?.runId).toBe('run-a'))
    act(() => hubs[0].resolveStart())
    await waitFor(() => expect(result.current.connection).toBe('live'))

    const b = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValue(b.promise)
    rerender({ runId: 'run-b' })
    await waitFor(() => expect(hubs).toHaveLength(2))
    expect(hubs[0].stop).toHaveBeenCalled()
    const calls = getRunCockpit.mock.calls.length

    act(() => {
      hubs[0].emit('runAdvanced', { runId: 'run-a', latestSequence: 50 })
      hubs[0].emit('reconnected')
      hubs[0].emit('reconnecting')
      hubs[0].emit('close')
    })
    expect(getRunCockpit).toHaveBeenCalledTimes(calls)
    expect(result.current.connection).toBe('connecting')
  })

  it('keeps same-selection catch-up coalescing, deduplication and recoverable failure', async () => {
    const { getRunCockpit, getRunEvents, hubs } = setup()
    getRunCockpit.mockResolvedValueOnce(cockpitFixture('run-a', 'objective A', 1))
    getRunEvents.mockResolvedValueOnce([eventFixture(1)])
    const { result } = renderRecorded('run-a')
    await waitFor(() => expect(result.current.cards).toHaveLength(1))

    getRunCockpit.mockRejectedValueOnce(new Error('transient'))
    act(() => hubs[0].resolveStart())
    await waitFor(() => expect(result.current.syncError).toBe('transient'))
    expect(result.current.cockpit?.runId).toBe('run-a')
    expect(result.current.connection).toBe('disconnected')

    getRunCockpit.mockResolvedValueOnce(cockpitFixture('run-a', 'objective A', 3))
    getRunEvents.mockResolvedValueOnce([eventFixture(1), eventFixture(2), eventFixture(3)])
    act(() => hubs[0].emit('runAdvanced', { runId: 'run-a', latestSequence: 3 }))
    await waitFor(() => expect(result.current.connection).toBe('live'))
    expect(result.current.cards.map((card) => card.id)).toEqual(['event-1', 'event-2', 'event-3'])
    expect(result.current.syncError).toBeNull()
    expect(getRunEvents).toHaveBeenLastCalledWith('run-a', 1)
  })

  it('ends the lifetime on unmount: late completions change nothing and waiters are released', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    const a = deferred<GetRunCockpitResponse>()
    getRunCockpit.mockReturnValue(a.promise)
    getRunEvents.mockResolvedValue([])
    const { result, unmount, frames } = renderRecorded('run-a')
    const waiter = result.current.refresh()
    const rendered = frames.length
    unmount()
    await expect(waiter).resolves.toBe(false)
    await act(async () => {
      a.resolve(cockpitFixture('run-a'))
    })
    expect(frames).toHaveLength(rendered)
  })

  it('remounts as a fresh lifetime', async () => {
    const { getRunCockpit, getRunEvents } = setup()
    getRunCockpit.mockResolvedValue(cockpitFixture('run-a', 'objective A'))
    getRunEvents.mockResolvedValue([])
    const first = renderRecorded('run-a')
    await waitFor(() => expect(first.result.current.cockpit?.runId).toBe('run-a'))
    first.unmount()
    const second = renderRecorded('run-a')
    expect(second.frames[0].cockpitRunId).toBeUndefined()
    expect(second.frames[0].loading).toBe(true)
    await waitFor(() => expect(second.result.current.cockpit?.runId).toBe('run-a'))
  })
})
