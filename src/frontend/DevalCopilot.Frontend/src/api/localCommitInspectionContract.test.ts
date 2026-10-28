import { afterEach, describe, expect, it, vi } from 'vitest'
import { toLocalCommitInspection } from '../features/cockpit/localCommitInspection'
import {
  LOCAL_COMMIT_RUN,
  localCommitInspectionOperationFor,
  notRecordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionFor,
} from '../features/cockpit/localCommitInspectionFixture'
import { LocalCommitInspectionWireClient } from '../features/cockpit/localCommitInspectionWire'
import { localCommitInspectionClient } from './clients'
import {
  GetLocalCommitInspectionEndpointClient,
  GetLocalCommitInspectionResponse,
  LocalCommitInspectionOperationResponse,
  LocalCommitInspectionReasonResponse,
} from './generated/api-client'

// The recorded local-commit read (ADR-0070): the factory is a parsed-body facade over the generated operation, wired to the launch
// session base URL and the authenticated transport, and it carries its own AbortSignal into the actual request. The generated graph is
// the reproducible NSwag output of the API-owned closed response. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const TARGET = { runId: LOCAL_COMMIT_RUN }
const ROUTE = `http://127.0.0.1:5099/api/runs/${LOCAL_COMMIT_RUN}/local-commit-inspection`

afterEach(() => vi.restoreAllMocks())

describe('the generated recorded local commit inspection operation', () => {
  it('declares exactly the closed members of the contract', () => {
    const operation = LocalCommitInspectionOperationResponse.fromJS(localCommitInspectionOperationFor())

    expect(Object.keys(operation.toJSON()).sort()).toEqual(Object.keys(localCommitInspectionOperationFor()).sort())
    expect(Object.keys(LocalCommitInspectionReasonResponse.fromJS({ kind: 'None', code: null }).toJSON()).sort()).toEqual(['code', 'kind'])
    expect(Object.keys(GetLocalCommitInspectionResponse.fromJS(recordedLocalCommitInspectionFor()).toJSON()).sort()).toEqual([
      'operation',
      'runId',
      'status',
    ])
  })

  it('rebuilds the graph: an unknown member vanishes and an explicit null becomes undefined, which is why the decoder reads the raw body', () => {
    const body = { ...recordedLocalCommitInspectionFor(), extra: 'kept' }

    const generated = GetLocalCommitInspectionResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toLocalCommitInspection(body, TARGET)).toBeNull()
    expect(GetLocalCommitInspectionResponse.fromJS(notRecordedLocalCommitInspectionFor()).operation).toBeUndefined()
  })
})

describe('localCommitInspectionClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET to exactly the documented route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedLocalCommitInspectionFor()), 200))
    const client = localCommitInspectionClient()

    expect(client).toBeInstanceOf(LocalCommitInspectionWireClient)
    expect(client).toBeInstanceOf(GetLocalCommitInspectionEndpointClient)
    await client.readOperation(LOCAL_COMMIT_RUN)

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

    const read = localCommitInspectionClient(controller.signal).readOperation(LOCAL_COMMIT_RUN)
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(notRecordedLocalCommitInspectionFor()), 200))

    await localCommitInspectionClient().readOperation(LOCAL_COMMIT_RUN)

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping explicit nulls and unknown members the generated model would drop', async () => {
    const body = { ...recordedLocalCommitInspectionFor(), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localCommitInspectionClient().readOperation(LOCAL_COMMIT_RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetLocalCommitInspectionResponse)
    expect(toLocalCommitInspection(read, TARGET)).toBeNull()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(recordedLocalCommitInspectionFor()), 200))
    const exact = await localCommitInspectionClient().readOperation(LOCAL_COMMIT_RUN)
    expect((exact as { operation: { reason: { code: unknown } } }).operation.reason.code).toBeNull()
    expect(toLocalCommitInspection(exact, TARGET)).toMatchObject({ status: 'Recorded' })
  })

  it('resolves an empty or non-JSON success as undefined, which the decoder refuses', async () => {
    for (const text of ['', 'not json', '<html>']) {
      vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

      const read = await localCommitInspectionClient().readOperation(LOCAL_COMMIT_RUN)

      expect(read).toBeUndefined()
      expect(toLocalCommitInspection(read, TARGET)).toBeNull()
    }
  })

  it('leaves every non-200 answer to the generated operation: a problem document is thrown with its status', async () => {
    for (const status of [400, 404]) {
      vi.spyOn(window, 'fetch').mockResolvedValue(
        answer(JSON.stringify({ status, errors: [{ code: 'local_commit_inspection.not_found', description: 'x' }], traceId: 't' }), status),
      )

      const caught = await localCommitInspectionClient()
        .readOperation(LOCAL_COMMIT_RUN)
        .catch((error: unknown) => error as { status?: number })

      expect(caught).toMatchObject({ status })
    }
  })

  it('refuses to build a request for a missing identity instead of sending one', () => {
    const fetch = vi.spyOn(window, 'fetch')

    expect(() => localCommitInspectionClient().readOperation(undefined as unknown as string)).toThrow()
    expect(fetch).not.toHaveBeenCalled()
  })
})
