import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { emptyQueueFor, recordedQueueFor, waitingEntryFor } from '../localExecutionQueueInspectionFixture'
import { LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS, useLocalExecutionQueueInspection } from './useLocalExecutionQueueInspection'

// The outstanding queue admission read owns its actual transport request (ADR-0064). These cases drive the real hook, the real
// operation-scoped wire client, the generated operation and the authenticated transport down to `fetch`, which is a cancellation-aware
// fake: it records every request with its AbortSignal and settles it only when answered or aborted. Counting resolver calls alone could
// not show that an abandoned request was really cancelled or that the ten-second bound really aborts. A clearly fictitious session.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

interface Flight {
  signal: AbortSignal | null
  settled: boolean
  aborted: boolean
  respond: (body: unknown) => void
}

function fakeResponse(body: unknown): Response {
  return { status: 200, headers: new Headers({ 'Content-Type': 'application/json' }), text: () => Promise.resolve(JSON.stringify(body)) } as unknown as Response
}

function cancellationAwareTransport(honorsAbort = true) {
  const flights: Flight[] = []
  const fetch = vi.spyOn(window, 'fetch').mockImplementation((_input: RequestInfo | URL, init?: RequestInit) =>
    new Promise<Response>((resolve, reject) => {
      const flight: Flight = {
        signal: init?.signal ?? null,
        settled: false,
        aborted: false,
        respond: (body) => {
          if (!flight.settled) {
            flight.settled = true
            resolve(fakeResponse(body))
          }
        },
      }
      flights.push(flight)
      const abort = () => {
        if (!flight.settled) {
          flight.settled = true
          flight.aborted = true
          reject(new DOMException('The operation was aborted.', 'AbortError'))
        }
      }

      if (honorsAbort && flight.signal !== null) {
        if (flight.signal.aborted) {
          abort()
        } else {
          flight.signal.addEventListener('abort', abort, { once: true })
        }
      }
    }),
  )

  return { fetch, flights, inFlight: () => flights.filter((flight) => !flight.settled).length }
}

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useLocalExecutionQueueInspection owns its actual transport request', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('sends nothing until opened, then one authenticated bodyless GET that carries the read signal', async () => {
    const transport = cancellationAwareTransport()
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    await flush()
    expect(transport.fetch).not.toHaveBeenCalled()

    act(() => result.current.show())
    await flush()

    expect(transport.flights).toHaveLength(1)
    expect(transport.flights[0].signal).not.toBeNull()
    const [url, init] = transport.fetch.mock.calls[0]
    expect(String(url)).toBe('http://127.0.0.1:5099/api/local-execution-queue/inspection')
    expect(init?.method).toBe('GET')
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
  })

  it('aborts the actual request when the ten-second bound elapses, not only its own timer, and reads once more only on an explicit reload', async () => {
    const transport = cancellationAwareTransport()
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    act(() => result.current.show())
    await flush()
    expect(transport.flights[0].signal?.aborted).toBe(false)

    await tick(LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS - 1)
    expect(transport.flights[0]).toMatchObject({ settled: false, aborted: false })
    expect(result.current.pending).toBe(true)
    await tick(1)

    expect(LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS).toBe(10_000)
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(result.current).toMatchObject({ overview: null, failure: 'timeout', pending: false })

    await tick(LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS * 6)
    expect(transport.flights).toHaveLength(1)

    act(() => result.current.reload())
    await flush()
    expect(transport.flights).toHaveLength(2)
    await act(async () => {
      transport.flights[1].respond(emptyQueueFor())
    })
    await flush()
    expect(result.current).toMatchObject({ failure: null, pending: false })
    expect(result.current.overview?.status).toBe('Empty')
  })

  it('shows a timeout and ignores the late answer of a transport that ignores the abort', async () => {
    const transport = cancellationAwareTransport(false)
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    act(() => result.current.show())
    await flush()

    await tick(LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS)

    expect(transport.flights[0].signal?.aborted).toBe(true)
    expect(result.current).toMatchObject({ overview: null, failure: 'timeout' })
    await act(async () => {
      transport.flights[0].respond(recordedQueueFor([waitingEntryFor(1)]))
    })
    await flush()
    expect(result.current).toMatchObject({ overview: null, failure: 'timeout' })
    expect(transport.flights).toHaveLength(1)
  })

  it('stops the ten-second timer when an answer arrives, so it never aborts or overwrites a settled read', async () => {
    const transport = cancellationAwareTransport()
    const { result } = renderHook(() => useLocalExecutionQueueInspection())
    act(() => result.current.show())
    await flush()
    await act(async () => {
      transport.flights[0].respond(recordedQueueFor([waitingEntryFor(1)]))
    })
    await flush()

    await tick(LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS * 3)

    expect(result.current.overview?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
    expect(transport.flights[0].signal?.aborted).toBe(false)
    expect(transport.flights).toHaveLength(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('aborts the request when the region is closed, reloaded or unmounted, and ends its timer each time', async () => {
    const transport = cancellationAwareTransport()
    const { result, unmount } = renderHook(() => useLocalExecutionQueueInspection())

    act(() => result.current.show())
    await flush()
    act(() => result.current.close())
    await flush()
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(vi.getTimerCount()).toBe(0)

    act(() => result.current.show())
    await flush()
    act(() => result.current.reload())
    await flush()
    expect(transport.flights[1]).toMatchObject({ settled: true, aborted: true })
    expect(transport.flights[2].settled).toBe(false)

    unmount()
    expect(transport.flights[2]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
    expect(transport.flights).toHaveLength(3)
  })

  it('shows a failed request as a read failure and an incoherent success as invalid, never any row', async () => {
    const failing = vi.spyOn(window, 'fetch').mockRejectedValue(new Error('down'))
    const failed = renderHook(() => useLocalExecutionQueueInspection())
    act(() => failed.result.current.show())
    await flush()
    expect(failed.result.current).toMatchObject({ overview: null, failure: 'read', pending: false })
    failing.mockRestore()

    vi.spyOn(window, 'fetch').mockResolvedValue(fakeResponse({ ...recordedQueueFor([waitingEntryFor(1)]), extra: true }))
    const invalid = renderHook(() => useLocalExecutionQueueInspection())
    act(() => invalid.result.current.show())
    await flush()
    expect(invalid.result.current).toMatchObject({ overview: null, failure: 'invalid', pending: false })
  })
})
