import { afterEach, describe, expect, it, vi } from 'vitest'
import { RUN, stepIdFor } from '../features/cockpit/localExecutionInspectionFixture'
import { toLocalExecutionReleaseInspection } from '../features/cockpit/localExecutionReleaseInspection'
import {
  notRecordedReleaseInspectionFor,
  recordedReleaseInspectionFor,
  releaseInspectionTargetFor,
} from '../features/cockpit/localExecutionReleaseInspectionFixture'
import { LocalExecutionReleaseInspectionWireClient } from '../features/cockpit/localExecutionReleaseInspectionWire'
import { localExecutionReleaseInspectionClient } from './clients'
import {
  GetLocalExecutionReleaseInspectionEndpointClient,
  GetLocalExecutionReleaseInspectionResponse,
  LocalExecutionReleaseInspectionReceiptResponse,
} from './generated/api-client'

// The recorded quarantine-release receipt read (ADR-0067): the factory is a parsed-body facade over the generated operation, wired to
// the launch session base URL and the authenticated transport, and it carries its own AbortSignal into the actual request. The generated
// graph is the reproducible NSwag output of the API-owned closed response. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const TARGET = releaseInspectionTargetFor(0)
const ROUTE = `http://127.0.0.1:5099/api/runs/${RUN}/local-execution-sequence/inspection/steps/${stepIdFor(0)}/ownership-release`

afterEach(() => vi.restoreAllMocks())

describe('the generated recorded release inspection operation', () => {
  it('declares exactly the closed receipt members of the contract', () => {
    const receipt = LocalExecutionReleaseInspectionReceiptResponse.fromJS(recordedReleaseInspectionFor().receipt)

    expect(Object.keys(receipt.toJSON()).sort()).toEqual(Object.keys(recordedReleaseInspectionFor().receipt as object).sort())
    expect(Object.keys(GetLocalExecutionReleaseInspectionResponse.fromJS(recordedReleaseInspectionFor()).toJSON()).sort()).toEqual(
      ['receipt', 'runId', 'status', 'stepId'],
    )
  })

  it('rebuilds the graph: an unknown member vanishes and an explicit null becomes undefined, which is why the decoder reads the raw body', () => {
    const body = { ...recordedReleaseInspectionFor(), extra: 'kept' }

    const generated = GetLocalExecutionReleaseInspectionResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toLocalExecutionReleaseInspection(body, TARGET)).toBeNull()
    expect(GetLocalExecutionReleaseInspectionResponse.fromJS(notRecordedReleaseInspectionFor(0)).receipt).toBeUndefined()
  })
})

describe('localExecutionReleaseInspectionClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET to exactly the documented route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedReleaseInspectionFor(0)), 200))
    const client = localExecutionReleaseInspectionClient()

    expect(client).toBeInstanceOf(LocalExecutionReleaseInspectionWireClient)
    expect(client).toBeInstanceOf(GetLocalExecutionReleaseInspectionEndpointClient)
    await client.readReceipt(RUN, stepIdFor(0))

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

    const read = localExecutionReleaseInspectionClient(controller.signal).readReceipt(RUN, stepIdFor(0))
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedReleaseInspectionFor(0)), 200))

    await localExecutionReleaseInspectionClient().readReceipt(RUN, stepIdFor(0))

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping explicit nulls and unknown members the generated model would drop', async () => {
    const body = { ...recordedReleaseInspectionFor(), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localExecutionReleaseInspectionClient().readReceipt(RUN, stepIdFor(0))

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetLocalExecutionReleaseInspectionResponse)
    expect(toLocalExecutionReleaseInspection(read, TARGET)).toBeNull()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(recordedReleaseInspectionFor()), 200))
    const exact = await localExecutionReleaseInspectionClient().readReceipt(RUN, stepIdFor(0))
    expect((exact as { receipt: { semanticExitCode: unknown } }).receipt.semanticExitCode).toBeNull()
    expect(toLocalExecutionReleaseInspection(exact, TARGET)).toMatchObject({ status: 'Recorded' })
  })

  it('resolves an empty or non-JSON success as undefined, which the decoder refuses', async () => {
    for (const text of ['', 'not json', '<html>']) {
      vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

      const read = await localExecutionReleaseInspectionClient().readReceipt(RUN, stepIdFor(0))

      expect(read).toBeUndefined()
      expect(toLocalExecutionReleaseInspection(read, TARGET)).toBeNull()
    }
  })

  it('leaves every non-200 answer to the generated operation: a problem document is thrown with its status', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(
      answer(JSON.stringify({ status: 404, errors: [{ code: 'local_execution_inspection.not_found', description: 'x' }], traceId: 't' }), 404),
    )

    const caught = await localExecutionReleaseInspectionClient()
      .readReceipt(RUN, stepIdFor(0))
      .catch((error: unknown) => error as { status?: number })

    expect(caught).toMatchObject({ status: 404 })
  })

  it('refuses to build a request for a missing identity instead of sending one', () => {
    const fetch = vi.spyOn(window, 'fetch')

    expect(() => localExecutionReleaseInspectionClient().readReceipt(undefined as unknown as string, stepIdFor(0))).toThrow()
    expect(() => localExecutionReleaseInspectionClient().readReceipt(RUN, undefined as unknown as string)).toThrow()
    expect(fetch).not.toHaveBeenCalled()
  })
})
