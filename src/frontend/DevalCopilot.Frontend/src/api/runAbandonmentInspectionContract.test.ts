import { afterEach, describe, expect, it, vi } from 'vitest'
import { toRunAbandonmentInspection } from '../features/cockpit/runAbandonmentInspection'
import {
  ABANDONMENT_RUN,
  ABANDONMENT_TARGET,
  runAbandonmentRecordedFor,
  notRecordedRunAbandonmentInspectionFor,
  recordedRunAbandonmentInspectionFor,
} from '../features/cockpit/runAbandonmentInspectionFixture'
import { RunAbandonmentInspectionWireClient } from '../features/cockpit/runAbandonmentInspectionWire'
import { runAbandonmentInspectionClient } from './clients'
import {
  GetRunAbandonmentInspectionEndpointClient,
  GetRunAbandonmentInspectionResponse,
  RunAbandonmentInspectionRecordedResponse,
} from './generated/api-client'

// The recorded abandonment read (ADR-0071): the factory is a parsed-body facade over the generated operation, wired to the launch
// session base URL and the authenticated transport, and it carries its own AbortSignal into the actual request. The generated graph is
// the reproducible NSwag output of the API-owned closed response. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const TARGET = ABANDONMENT_TARGET
const ROUTE = `http://127.0.0.1:5099/api/runs/${ABANDONMENT_RUN}/abandonment-inspection`

afterEach(() => vi.restoreAllMocks())

describe('the generated recorded abandonment inspection operation', () => {
  it('declares exactly the closed members of the contract', () => {
    const abandonment = RunAbandonmentInspectionRecordedResponse.fromJS(runAbandonmentRecordedFor())

    expect(Object.keys(abandonment.toJSON()).sort()).toEqual(Object.keys(runAbandonmentRecordedFor()).sort())
    expect(Object.keys(GetRunAbandonmentInspectionResponse.fromJS(recordedRunAbandonmentInspectionFor()).toJSON()).sort()).toEqual([
      'abandonment',
      'runId',
      'status',
    ])
  })

  it('rebuilds the graph: an unknown member vanishes and an explicit null becomes undefined, which is why the decoder reads the raw body', () => {
    const body = { ...recordedRunAbandonmentInspectionFor(), extra: 'kept' }

    const generated = GetRunAbandonmentInspectionResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toRunAbandonmentInspection(body, TARGET)).toBeNull()
    expect(GetRunAbandonmentInspectionResponse.fromJS(notRecordedRunAbandonmentInspectionFor()).abandonment).toBeUndefined()
  })
})

describe('runAbandonmentInspectionClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET to exactly the documented route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedRunAbandonmentInspectionFor()), 200))
    const client = runAbandonmentInspectionClient()

    expect(client).toBeInstanceOf(RunAbandonmentInspectionWireClient)
    expect(client).toBeInstanceOf(GetRunAbandonmentInspectionEndpointClient)
    await client.readAbandonment(ABANDONMENT_RUN)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(ROUTE)
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    expect(String(url)).not.toContain('?')
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('carries its own AbortSignal to the actual authenticated request, so aborting it cancels that request', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(
      (_url, init) =>
        new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')), { once: true })
        }),
    )
    const controller = new AbortController()

    const read = runAbandonmentInspectionClient(controller.signal).readAbandonment(ABANDONMENT_RUN)
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedRunAbandonmentInspectionFor()), 200))

    await runAbandonmentInspectionClient().readAbandonment(ABANDONMENT_RUN)

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping explicit nulls and unknown members the generated model would drop', async () => {
    const body = { ...recordedRunAbandonmentInspectionFor(), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await runAbandonmentInspectionClient().readAbandonment(ABANDONMENT_RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetRunAbandonmentInspectionResponse)
    expect(toRunAbandonmentInspection(read, TARGET)).toBeNull()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(recordedRunAbandonmentInspectionFor()), 200))
    const exact = await runAbandonmentInspectionClient().readAbandonment(ABANDONMENT_RUN)
    expect((exact as { abandonment: { eventSequence: unknown } }).abandonment.eventSequence).toBe('42')
    expect(toRunAbandonmentInspection(exact, TARGET)).toMatchObject({ status: 'Recorded' })
  })

  it('resolves an empty or non-JSON success as undefined, which the decoder refuses', async () => {
    for (const text of ['', 'not json', '<html>']) {
      vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

      const read = await runAbandonmentInspectionClient().readAbandonment(ABANDONMENT_RUN)

      expect(read).toBeUndefined()
      expect(toRunAbandonmentInspection(read, TARGET)).toBeNull()
    }
  })

  it('leaves every non-200 answer to the generated operation: a problem document is thrown with its status', async () => {
    for (const status of [400, 404]) {
      vi.spyOn(window, 'fetch').mockResolvedValue(
        answer(JSON.stringify({ status, errors: [{ code: 'run_abandonment_inspection.not_found', description: 'x' }], traceId: 't' }), status),
      )

      const caught = await runAbandonmentInspectionClient()
        .readAbandonment(ABANDONMENT_RUN)
        .catch((error: unknown) => error as { status?: number })

      expect(caught).toMatchObject({ status })
    }
  })

  it('refuses to build a request for a missing identity instead of sending one', () => {
    const fetch = vi.spyOn(window, 'fetch')

    expect(() => runAbandonmentInspectionClient().readAbandonment(undefined as unknown as string)).toThrow()
    expect(fetch).not.toHaveBeenCalled()
  })
})
