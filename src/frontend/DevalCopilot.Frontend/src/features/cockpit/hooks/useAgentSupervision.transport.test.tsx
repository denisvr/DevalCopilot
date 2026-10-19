import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { observedWire } from '../agentSupervisionFixture'
import { AGENT_SUPERVISION_REFRESH_INTERVAL_MS, AGENT_SUPERVISION_REQUEST_TIMEOUT_MS, useAgentSupervision } from './useAgentSupervision'

// ASUP-R2: the Agent supervision read owns its actual transport request. These cases drive the real hook, the real operation-scoped
// wire client, the generated operation and the authenticated transport down to `fetch`, which is a cancellation-aware fake: it records
// every request with its AbortSignal, settles it only when answered or aborted, and measures how many requests of one run are in flight
// at once. Counting resolver calls alone could not show that an abandoned request was really cancelled. A clearly fictitious session.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

interface Flight {
  runId: string
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
  const peak = new Map<string, number>()
  const inFlight = (runId: string) => flights.filter((flight) => flight.runId === runId && !flight.settled).length

  const fetch = vi.spyOn(window, 'fetch').mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const runId = /\/api\/runs\/([^/]+)\/agent-supervision$/.exec(String(input))?.[1] ?? 'unknown'
    return new Promise<Response>((resolve, reject) => {
      const flight: Flight = {
        runId,
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
      peak.set(runId, Math.max(peak.get(runId) ?? 0, inFlight(runId)))
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
    })
  })

  return { fetch, flights, inFlight, peak: (runId: string) => peak.get(runId) ?? 0 }
}

const mounted = (runId: string | null) =>
  renderHook((props: { runId: string | null }) => useAgentSupervision(props.runId), { initialProps: { runId } })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useAgentSupervision owns its actual transport request (ASUP-R2)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('aborts the actual request when the ten-second bound elapses, not only its own timer', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted('run-1')
    await flush()

    expect(transport.flights).toHaveLength(1)
    expect(transport.flights[0].signal).not.toBeNull()
    expect(transport.flights[0].signal?.aborted).toBe(false)

    await tick(AGENT_SUPERVISION_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight('run-1')).toBe(0)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
  })

  it('recovers on the next read after a timeout with never more than one request of the run in flight', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted('run-1')
    await flush()
    await tick(AGENT_SUPERVISION_REQUEST_TIMEOUT_MS)
    expect(result.current.failure).toBe('timeout')

    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    expect(transport.flights).toHaveLength(2)
    expect(transport.flights[1].signal?.aborted).toBe(false)
    await act(async () => {
      transport.flights[1].respond(observedWire('run-1'))
    })
    await flush()

    expect(result.current).toMatchObject({ failure: null })
    expect(result.current.reading?.status).toBe('Observed')
    expect(transport.peak('run-1')).toBe(1)
  })

  it('never starts a second request while a transport that ignores the abort is still unsettled, and ignores its late answer', async () => {
    const transport = cancellationAwareTransport(false)
    const { result } = mounted('run-1')
    await flush()
    await tick(AGENT_SUPERVISION_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0].signal?.aborted).toBe(true)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    await tick(AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 6)
    expect(transport.flights).toHaveLength(1)

    await act(async () => {
      transport.flights[0].respond(observedWire('run-1'))
    })
    await flush()

    // The late answer of the abandoned request settles nothing; the waiting trigger reads afresh only after it settled.
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(transport.flights).toHaveLength(2)
    expect(transport.peak('run-1')).toBe(1)
  })

  it('aborts the request of a replaced selection and keeps one request per run across A-B-A', async () => {
    const transport = cancellationAwareTransport()
    const { result, rerender } = mounted('run-1')
    await flush()

    rerender({ runId: 'run-2' })
    await flush()
    expect(transport.flights[0]).toMatchObject({ runId: 'run-1', aborted: true })

    rerender({ runId: 'run-1' })
    await flush()
    expect(transport.flights[1]).toMatchObject({ runId: 'run-2', aborted: true })
    expect(transport.flights[2]).toMatchObject({ runId: 'run-1', settled: false })
    expect(result.current).toMatchObject({ loading: true, reading: null })

    await act(async () => {
      transport.flights[2].respond(observedWire('run-1'))
    })
    await flush()

    expect(result.current.reading).toMatchObject({ runId: 'run-1', status: 'Observed' })
    expect(transport.peak('run-1')).toBe(1)
    expect(transport.peak('run-2')).toBe(1)
  })

  it('aborts the in-flight request on unmount and starts nothing afterwards', async () => {
    const transport = cancellationAwareTransport()
    const { unmount } = mounted('run-1')
    await flush()

    unmount()
    await flush()
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })

    await tick(AGENT_SUPERVISION_REQUEST_TIMEOUT_MS + AGENT_SUPERVISION_REFRESH_INTERVAL_MS * 3)
    expect(transport.flights).toHaveLength(1)
    expect(vi.getTimerCount()).toBe(0)
  })
})
