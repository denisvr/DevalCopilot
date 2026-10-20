import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { availableCapacityWire, capacityWireFor } from '../executionCapacityFixture'
import {
  EXECUTION_CAPACITY_REFRESH_INTERVAL_MS,
  EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS,
  useExecutionCapacity,
} from './useExecutionCapacity'

// The execution capacity read owns its actual transport request (ADR-0058). These cases drive the real hook, the real operation-scoped
// wire client, the generated operation and the authenticated transport down to `fetch`, which is a cancellation-aware fake: it records
// every request with its AbortSignal, settles it only when answered or aborted, and measures how many requests are in flight at once.
// Counting resolver calls alone could not show that an abandoned request was really cancelled. A clearly fictitious session.
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
  let peak = 0
  const inFlight = () => flights.filter((flight) => !flight.settled).length

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
      peak = Math.max(peak, inFlight())
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

  return { fetch, flights, inFlight, peak: () => peak }
}

const mounted = (enabled: boolean) =>
  renderHook((props: { enabled: boolean }) => useExecutionCapacity(props.enabled), { initialProps: { enabled } })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useExecutionCapacity owns its actual transport request', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('sends one authenticated request for the host, carrying the read signal', async () => {
    const transport = cancellationAwareTransport()
    mounted(true)
    await flush()

    expect(transport.flights).toHaveLength(1)
    expect(transport.flights[0].signal).not.toBeNull()
    const [url, init] = transport.fetch.mock.calls[0]
    expect(String(url)).toBe('http://127.0.0.1:5099/api/execution-capacity')
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
  })

  it('aborts the actual request when the ten-second bound elapses, not only its own timer', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted(true)
    await flush()

    expect(transport.flights[0].signal?.aborted).toBe(false)

    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
  })

  it('recovers on the next read after a timeout with never more than one request in flight', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted(true)
    await flush()
    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
    expect(result.current.failure).toBe('timeout')

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(transport.flights).toHaveLength(2)
    expect(transport.flights[1].signal?.aborted).toBe(false)
    await act(async () => {
      transport.flights[1].respond(availableCapacityWire())
    })
    await flush()

    expect(result.current).toMatchObject({ failure: null })
    expect(result.current.reading?.status).toBe('Available')
    expect(transport.peak()).toBe(1)
  })

  it('never starts a second request while a transport that ignores the abort is still unsettled, and ignores its late answer', async () => {
    const transport = cancellationAwareTransport(false)
    const { result } = mounted(true)
    await flush()
    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0].signal?.aborted).toBe(true)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 6)
    expect(transport.flights).toHaveLength(1)

    await act(async () => {
      transport.flights[0].respond(availableCapacityWire())
    })
    await flush()

    // The late answer of the abandoned request settles nothing (an Available must never revive); the waiting trigger reads afresh only after it settled.
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(transport.flights).toHaveLength(2)
    expect(transport.peak()).toBe(1)
  })

  it('aborts the request of a lifetime that ended and does not let an obsolete answer settle the next one', async () => {
    const transport = cancellationAwareTransport()
    const { result, rerender } = mounted(true)
    await flush()

    rerender({ enabled: false })
    await flush()
    expect(transport.flights[0]).toMatchObject({ aborted: true })

    rerender({ enabled: true })
    await flush()
    expect(transport.flights[1]).toMatchObject({ settled: false })
    expect(result.current).toMatchObject({ loading: true, reading: null })

    await act(async () => {
      transport.flights[1].respond(capacityWireFor('AtCapacity'))
    })
    await flush()

    expect(result.current.reading).toMatchObject({ status: 'AtCapacity', recordedOwners: 2 })
    expect(transport.peak()).toBe(1)
  })

  it('does not let the answer of an obsolete request, from a transport that ignores the abort, settle the new lifetime', async () => {
    const transport = cancellationAwareTransport(false)
    const { result, rerender } = mounted(true)
    await flush()

    rerender({ enabled: false })
    rerender({ enabled: true })
    await flush()
    await act(async () => {
      transport.flights[0].respond(availableCapacityWire())
    })
    await flush()

    expect(result.current).toMatchObject({ loading: true, reading: null })
  })

  it('aborts the in-flight request on unmount and starts nothing afterwards', async () => {
    const transport = cancellationAwareTransport()
    const { unmount } = mounted(true)
    await flush()

    unmount()
    await flush()
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })

    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS + EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 3)
    expect(transport.flights).toHaveLength(1)
    expect(transport.inFlight()).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
  })
})
