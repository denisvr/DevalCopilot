import { afterEach, describe, expect, it, vi } from 'vitest'
import { ExecutionCapacityWireClient } from '../features/cockpit/executionCapacityWire'
import { toExecutionCapacityReading } from '../features/cockpit/executionCapacity'
import { availableCapacityWire, capacityWireFor } from '../features/cockpit/executionCapacityFixture'
import { executionCapacityClient } from './clients'
import { ApiException, ApiProblemDetails, GetExecutionCapacityEndpointClient, GetExecutionCapacityResponse } from './generated/api-client'

// The execution capacity read (ADR-0058): the generated operation coerces explicit nulls, drops unknown members and rebuilds the graph, so
// the cockpit's factory is a parsed-body facade over it, wired to the launch session's base URL and the authenticated transport exactly as
// every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

const problem = (status: number, code: string) => ({
  type: 'urn:devalente:problem:validation',
  title: 'fixed',
  status,
  detail: 'See the errors property for details.',
  traceId: '00-abc-def-00',
  errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
})

describe('the generated execution capacity operation', () => {
  it('rebuilds the graph: an unknown member vanishes, which is why the decoder reads the raw body', () => {
    const body = { ...availableCapacityWire(), extra: 'kept' }

    const generated = GetExecutionCapacityResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toExecutionCapacityReading(body)).toBeNull()
  })

  it('answers with the generated model, so only the facade can show an explicit null from a missing key', async () => {
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(capacityWireFor('Unavailable')), 200)))

    const generated = await new GetExecutionCapacityEndpointClient('http://host.invalid', { fetch }).getExecutionCapacity()

    expect(generated).toBeInstanceOf(GetExecutionCapacityResponse)
  })
})

describe('executionCapacityClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET with no project in its path', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(availableCapacityWire()), 200))
    const client = executionCapacityClient()

    expect(client).toBeInstanceOf(ExecutionCapacityWireClient)
    expect(client).toBeInstanceOf(GetExecutionCapacityEndpointClient)
    await client.readExecutionCapacity()

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/execution-capacity')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('?')
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

    const read = executionCapacityClient(controller.signal).readExecutionCapacity()
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(availableCapacityWire()), 200))

    await executionCapacityClient().readExecutionCapacity()

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping the explicit nulls and unknown members the generated model would lose', async () => {
    const body = { ...capacityWireFor('Unavailable'), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await executionCapacityClient().readExecutionCapacity()

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetExecutionCapacityResponse)
    expect(toExecutionCapacityReading(read)).toBeNull()
  })

  it('carries an exact answer, with its explicit null count and reason, through to the decoder', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(capacityWireFor('Unavailable')), 200))

    const read = (await executionCapacityClient().readExecutionCapacity()) as Record<string, unknown>

    expect(read.recordedOwners).toBeNull()
    expect(read.reasonCode).toBe('execution_capacity_unavailable')
    expect(toExecutionCapacityReading(read)?.status).toBe('Unavailable')
  })

  it('keeps a missing key missing, so the decoder can tell it from an explicit null', async () => {
    const { reasonCode: _removed, ...withoutReason } = availableCapacityWire()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(withoutReason), 200))

    const read = await executionCapacityClient().readExecutionCapacity()

    expect(Object.hasOwn(read as object, 'reasonCode')).toBe(false)
    expect(toExecutionCapacityReading(read)).toBeNull()
  })

  it('carries a validated at-capacity answer of the host', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(capacityWireFor('AtCapacity', {}, 3)), 200))

    const read = await executionCapacityClient().readExecutionCapacity()

    expect(toExecutionCapacityReading(read)).toMatchObject({ status: 'AtCapacity', recordedOwners: 3, limit: 2 })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await executionCapacityClient().readExecutionCapacity()

    expect(read).toBeUndefined()
    expect(toExecutionCapacityReading(read)).toBeNull()
  })

  it.each([
    [400, 'execution_capacity.query_not_allowed'],
    [400, 'execution_capacity.body_not_allowed'],
  ])('rejects the declared %s (%s) as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await executionCapacityClient()
      .readExecutionCapacity()
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(status)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 404, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await executionCapacityClient()
      .readExecutionCapacity()
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
