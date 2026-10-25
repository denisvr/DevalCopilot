import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RUN, stepIdFor } from '../localExecutionInspectionFixture'
import {
  notRecordedReleaseInspectionFor,
  recordedReleaseInspectionFor,
  releaseInspectionTargetFor,
} from '../localExecutionReleaseInspectionFixture'
import { LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS, useLocalExecutionReleaseInspection } from './useLocalExecutionReleaseInspection'

// The recorded release receipt read owns its actual transport request (ADR-0067). These cases drive the real hook, the real
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

function fakeResponse(body: unknown, status = 200): Response {
  return { status, headers: new Headers({ 'Content-Type': 'application/json' }), text: () => Promise.resolve(JSON.stringify(body)) } as unknown as Response
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
const TARGET = releaseInspectionTargetFor(0)
const KEY = JSON.stringify(['visit-1', stepIdFor(0), 1])
const mount = () => renderHook(() => useLocalExecutionReleaseInspection(KEY, TARGET))

describe('useLocalExecutionReleaseInspection owns its actual transport request', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('sends nothing until opened, then one authenticated bodyless GET of exactly the step that carries the read signal', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mount()
    await flush()
    expect(transport.fetch).not.toHaveBeenCalled()

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(transport.flights).toHaveLength(1)
    expect(transport.flights[0].signal).not.toBeNull()
    const [url, init] = transport.fetch.mock.calls[0]
    expect(String(url)).toBe(`http://127.0.0.1:5099/api/runs/${RUN}/local-execution-sequence/inspection/steps/${stepIdFor(0)}/ownership-release`)
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
  })

  it('aborts the actual request when the ten-second bound elapses, not only its own timer, and reads once more only on an explicit reload', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    expect(transport.flights[0].signal?.aborted).toBe(false)

    await tick(LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS - 1)
    expect(transport.flights[0]).toMatchObject({ settled: false, aborted: false })
    expect(result.current.pending).toBe(true)
    await tick(1)

    expect(LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS).toBe(10_000)
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(result.current).toMatchObject({ answer: null, failure: 'timeout', pending: false })

    await tick(LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS * 6)
    expect(transport.flights).toHaveLength(1)

    act(() => result.current.reload())
    await flush()
    expect(transport.flights).toHaveLength(2)
    await act(async () => {
      transport.flights[1].respond(notRecordedReleaseInspectionFor(0))
    })
    await flush()
    expect(result.current).toMatchObject({ failure: null, pending: false })
    expect(result.current.answer?.status).toBe('NotRecorded')
  })

  it('shows a timeout and ignores the late answer of a transport that ignores the abort', async () => {
    const transport = cancellationAwareTransport(false)
    const { result } = mount()
    act(() => result.current.show())
    await flush()

    await tick(LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS)

    expect(transport.flights[0].signal?.aborted).toBe(true)
    expect(result.current).toMatchObject({ answer: null, failure: 'timeout' })
    await act(async () => {
      transport.flights[0].respond(recordedReleaseInspectionFor())
    })
    await flush()
    expect(result.current).toMatchObject({ answer: null, failure: 'timeout' })
    expect(transport.flights).toHaveLength(1)
  })

  it('stops the ten-second timer when an answer arrives, so it never aborts or overwrites a settled read', async () => {
    const transport = cancellationAwareTransport()
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    await act(async () => {
      transport.flights[0].respond(recordedReleaseInspectionFor())
    })
    await flush()

    await tick(LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS * 3)

    expect(result.current.answer?.status).toBe('Recorded')
    expect(result.current.failure).toBeNull()
    expect(transport.flights[0].signal?.aborted).toBe(false)
    expect(transport.flights).toHaveLength(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('aborts the request when the region is closed, reloaded or unmounted, ends its timer each time and keeps one request in flight', async () => {
    const transport = cancellationAwareTransport()
    const { result, unmount } = mount()

    act(() => result.current.show())
    await flush()
    expect(transport.inFlight()).toBe(1)
    act(() => result.current.close())
    await flush()
    expect(transport.flights[0]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(vi.getTimerCount()).toBe(0)

    act(() => result.current.show())
    await flush()
    act(() => result.current.reload())
    await flush()
    expect(transport.flights[1]).toMatchObject({ settled: true, aborted: true })
    expect(transport.flights[2].settled).toBe(false)
    expect(transport.inFlight()).toBe(1)

    unmount()
    expect(transport.flights[2]).toMatchObject({ settled: true, aborted: true })
    expect(transport.inFlight()).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
    expect(transport.flights).toHaveLength(3)
  })

  it('shows a failed request as a read failure, a 404 as not found and an incoherent success as invalid, never any receipt', async () => {
    const failing = vi.spyOn(window, 'fetch').mockRejectedValue(new Error('down'))
    const failed = mount()
    act(() => failed.result.current.show())
    await flush()
    expect(failed.result.current).toMatchObject({ answer: null, failure: 'read', pending: false })
    failing.mockRestore()

    vi.spyOn(window, 'fetch').mockResolvedValue(fakeResponse({ status: 404, errors: [{ code: 'local_execution_inspection.not_found' }], traceId: 'trace' }, 404))
    const missing = mount()
    act(() => missing.result.current.show())
    await flush()
    expect(missing.result.current).toMatchObject({ answer: null, failure: 'not-found', pending: false })

    vi.spyOn(window, 'fetch').mockResolvedValue(fakeResponse({ ...recordedReleaseInspectionFor(), extra: true }))
    const invalid = mount()
    act(() => invalid.result.current.show())
    await flush()
    expect(invalid.result.current).toMatchObject({ answer: null, failure: 'invalid', pending: false })
  })

  it('reads the exact parsed body, so an explicit null receipt and unknown members are judged rather than coerced', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(fakeResponse(notRecordedReleaseInspectionFor(0)))
    const accepted = mount()
    act(() => accepted.result.current.show())
    await flush()
    expect(accepted.result.current.answer?.status).toBe('NotRecorded')

    // A missing `receipt` member is not an explicit null: the generated model would have defaulted it.
    const { receipt: _receipt, ...withoutReceipt } = notRecordedReleaseInspectionFor(0)
    vi.spyOn(window, 'fetch').mockResolvedValue(fakeResponse(withoutReceipt))
    const refused = mount()
    act(() => refused.result.current.show())
    await flush()
    expect(refused.result.current).toMatchObject({ answer: null, failure: 'invalid' })
  })
})
