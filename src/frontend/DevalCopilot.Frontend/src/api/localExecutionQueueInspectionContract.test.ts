import { afterEach, describe, expect, it, vi } from 'vitest'
import { toLocalExecutionQueueInspection } from '../features/cockpit/localExecutionQueueInspection'
import { LocalExecutionQueueInspectionWireClient } from '../features/cockpit/localExecutionQueueInspectionWire'
import { emptyQueueFor, recordedQueueFor, waitingEntryFor } from '../features/cockpit/localExecutionQueueInspectionFixture'
import { localExecutionQueueInspectionClient } from './clients'
import { GetLocalExecutionQueueInspectionEndpointClient, GetLocalExecutionQueueInspectionResponse } from './generated/api-client'

// The recorded outstanding queue admissions read (ADR-0064): the factory is a parsed-body facade over the generated operation, wired to
// the launch session base URL and the authenticated transport, and unlike the older inspection reads it carries its own AbortSignal into
// the actual request. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the generated outstanding queue admission operation', () => {
  it('rebuilds the graph: an unknown member vanishes, which is why the decoder reads the raw body', () => {
    const body = { ...recordedQueueFor([waitingEntryFor(1)]), extra: 'kept' }

    const generated = GetLocalExecutionQueueInspectionResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toLocalExecutionQueueInspection(body)).toBeNull()
  })
})

describe('localExecutionQueueInspectionClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET to exactly the documented route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(emptyQueueFor()), 200))
    const client = localExecutionQueueInspectionClient()

    expect(client).toBeInstanceOf(LocalExecutionQueueInspectionWireClient)
    expect(client).toBeInstanceOf(GetLocalExecutionQueueInspectionEndpointClient)
    await client.readOutstanding()

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/local-execution-queue/inspection')
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

    const read = localExecutionQueueInspectionClient(controller.signal).readOutstanding()
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(emptyQueueFor()), 200))

    await localExecutionQueueInspectionClient().readOutstanding()

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping explicit nulls and unknown members the generated model would drop', async () => {
    const body = { ...recordedQueueFor([waitingEntryFor(1)]), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localExecutionQueueInspectionClient().readOutstanding()

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetLocalExecutionQueueInspectionResponse)
    expect(toLocalExecutionQueueInspection(read)).toBeNull()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(recordedQueueFor([waitingEntryFor(1)])), 200))
    const exact = await localExecutionQueueInspectionClient().readOutstanding()
    expect((exact as { entries: { admission: { stepId: unknown } }[] }).entries[0].admission.stepId).toBeNull()
    expect(toLocalExecutionQueueInspection(exact)).toMatchObject({ status: 'Recorded' })
  })

  it('keeps a missing key missing, so the decoder can tell it from an explicit null', async () => {
    const { entries: _removed, ...withoutEntries } = emptyQueueFor()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(withoutEntries), 200))

    const read = await localExecutionQueueInspectionClient().readOutstanding()

    expect(Object.hasOwn(read as object, 'entries')).toBe(false)
    expect(toLocalExecutionQueueInspection(read)).toBeNull()
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await localExecutionQueueInspectionClient().readOutstanding()

    expect(read).toBeUndefined()
    expect(toLocalExecutionQueueInspection(read)).toBeNull()
  })

  it('leaves every non-200 answer to the generated operation, as an ApiProblemDetails carrying the status', async () => {
    const problem = JSON.stringify({
      type: 'urn:devalente:problem:validation',
      status: 400,
      errors: [{ code: 'local_execution_inspection.query_not_allowed' }],
    })
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer(problem, 400)))

    await expect(localExecutionQueueInspectionClient().readOutstanding()).rejects.toMatchObject({ status: 400 })
  })

  it('rejects an unexpected server status without a body the decoder could mistake for an answer', async () => {
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('', 500)))

    await expect(localExecutionQueueInspectionClient().readOutstanding()).rejects.toMatchObject({ status: 500 })
  })
})
