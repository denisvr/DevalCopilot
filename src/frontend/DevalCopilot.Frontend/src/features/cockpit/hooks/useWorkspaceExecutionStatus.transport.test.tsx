import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OTHER_PROJECT_ID, PROJECT_ID, idleWire, wireFor } from '../workspaceExecutionFixture'
import {
  WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS,
  WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS,
  useWorkspaceExecutionStatus,
} from './useWorkspaceExecutionStatus'

// The workspace execution read owns its actual transport request (ADR-0057). These cases drive the real hook, the real operation-scoped
// wire client, the generated operation and the authenticated transport down to `fetch`, which is a cancellation-aware fake: it records
// every request with its AbortSignal, settles it only when answered or aborted, and measures how many requests of one project are in
// flight at once. Counting resolver calls alone could not show that an abandoned request was really cancelled. A clearly fictitious session.
vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

interface Flight {
  projectId: string
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
  const inFlight = (projectId: string) => flights.filter((flight) => flight.projectId === projectId && !flight.settled).length
  const inFlightTotal = () => flights.filter((flight) => !flight.settled).length

  const fetch = vi.spyOn(window, 'fetch').mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const projectId = /\/api\/projects\/([^/]+)\/workspace-execution-status$/.exec(String(input))?.[1] ?? 'unknown'
    return new Promise<Response>((resolve, reject) => {
      const flight: Flight = {
        projectId,
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
      peak.set(projectId, Math.max(peak.get(projectId) ?? 0, inFlight(projectId)))
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

  return { fetch, flights, inFlight, inFlightTotal, peak: (projectId: string) => peak.get(projectId) ?? 0 }
}

const mounted = (projectId: string | null) =>
  renderHook((props: { projectId: string | null }) => useWorkspaceExecutionStatus(props.projectId), { initialProps: { projectId } })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))

describe('useWorkspaceExecutionStatus owns its actual transport request', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('sends one authenticated request for exactly the selected project, carrying the read signal', async () => {
    const transport = cancellationAwareTransport()
    mounted(PROJECT_ID)
    await flush()

    expect(transport.flights).toHaveLength(1)
    expect(transport.flights[0].projectId).toBe(PROJECT_ID)
    expect(transport.flights[0].signal).not.toBeNull()
    const [url, init] = transport.fetch.mock.calls[0]
    expect(String(url)).toBe(`http://127.0.0.1:5099/api/projects/${PROJECT_ID}/workspace-execution-status`)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
  })

  it('aborts the actual request when the ten-second bound elapses, not only its own timer', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted(PROJECT_ID)
    await flush()

    expect(transport.flights[0].signal?.aborted).toBe(false)

    await tick(WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight(PROJECT_ID)).toBe(0)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
  })

  it('recovers on the next read after a timeout with never more than one request of the project in flight', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mounted(PROJECT_ID)
    await flush()
    await tick(WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS)
    expect(result.current.failure).toBe('timeout')

    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    expect(transport.flights).toHaveLength(2)
    expect(transport.flights[1].signal?.aborted).toBe(false)
    await act(async () => {
      transport.flights[1].respond(idleWire())
    })
    await flush()

    expect(result.current).toMatchObject({ failure: null })
    expect(result.current.reading?.status).toBe('Idle')
    expect(transport.peak(PROJECT_ID)).toBe(1)
  })

  it('never starts a second request while a transport that ignores the abort is still unsettled, and ignores its late answer', async () => {
    const transport = cancellationAwareTransport(false)
    const { result } = mounted(PROJECT_ID)
    await flush()
    await tick(WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS)

    expect(transport.flights[0].signal?.aborted).toBe(true)
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    await tick(WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 6)
    expect(transport.flights).toHaveLength(1)

    await act(async () => {
      transport.flights[0].respond(idleWire())
    })
    await flush()

    // The late answer of the abandoned request settles nothing (an Idle must never revive); the waiting trigger reads afresh only after it settled.
    expect(result.current).toMatchObject({ reading: null, failure: 'timeout' })
    expect(transport.flights).toHaveLength(2)
    expect(transport.peak(PROJECT_ID)).toBe(1)
  })

  it('aborts the request of a replaced selection and keeps one request per project across A-B-A', async () => {
    const transport = cancellationAwareTransport()
    const { result, rerender } = mounted(PROJECT_ID)
    await flush()

    rerender({ projectId: OTHER_PROJECT_ID })
    await flush()
    expect(transport.flights[0]).toMatchObject({ projectId: PROJECT_ID, aborted: true })

    rerender({ projectId: PROJECT_ID })
    await flush()
    expect(transport.flights[1]).toMatchObject({ projectId: OTHER_PROJECT_ID, aborted: true })
    expect(transport.flights[2]).toMatchObject({ projectId: PROJECT_ID, settled: false })
    expect(result.current).toMatchObject({ loading: true, reading: null })

    await act(async () => {
      transport.flights[2].respond(wireFor('AgentAttemptActive'))
    })
    await flush()

    expect(result.current.reading).toMatchObject({ projectId: PROJECT_ID, status: 'AgentAttemptActive' })
    expect(transport.peak(PROJECT_ID)).toBe(1)
    expect(transport.peak(OTHER_PROJECT_ID)).toBe(1)
  })

  it('does not let the answer of an obsolete request, from a transport that ignores the abort, settle the new selection', async () => {
    const transport = cancellationAwareTransport(false)
    const { result, rerender } = mounted(PROJECT_ID)
    await flush()

    rerender({ projectId: OTHER_PROJECT_ID })
    await flush()
    await act(async () => {
      transport.flights[0].respond(idleWire())
    })
    await flush()

    expect(result.current).toMatchObject({ loading: true, reading: null })
  })

  it('aborts the in-flight request on unmount and starts nothing afterwards', async () => {
    const transport = cancellationAwareTransport()
    const { unmount } = mounted(PROJECT_ID)
    await flush()

    unmount()
    await flush()
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })

    await tick(WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS + WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS * 3)
    expect(transport.flights).toHaveLength(1)
    expect(transport.inFlightTotal()).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
  })
})
