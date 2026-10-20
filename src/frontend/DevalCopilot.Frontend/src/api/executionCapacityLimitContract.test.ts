import { afterEach, describe, expect, it, vi } from 'vitest'
import { ExecutionCapacityLimitWireClient } from '../features/cockpit/executionCapacityWire'
import { toExecutionCapacityReading } from '../features/cockpit/executionCapacity'
import { availableCapacityWire, capacityWireFor } from '../features/cockpit/executionCapacityFixture'
import { executionCapacityLimitClient } from './clients'
import { ApiException, ApiProblemDetails, SetExecutionCapacityLimitEndpointClient } from './generated/api-client'

// The explicit limit change (ADR-0059): the generated operation rebuilds its answer as a model, so the cockpit's factory is a parsed-body
// facade over it, wired to the launch session's base URL and the authenticated transport like every other operation, and it carries the
// change's own AbortSignal to the actual request. A clearly fictitious session is used.
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

describe('executionCapacityLimitClient', () => {
  it('is the facade over the generated operation and sends exactly one authenticated PUT of the one limit member to the global route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(availableCapacityWire(0, 1)), 200))
    const client = executionCapacityLimitClient()

    expect(client).toBeInstanceOf(ExecutionCapacityLimitWireClient)
    expect(client).toBeInstanceOf(SetExecutionCapacityLimitEndpointClient)
    await client.changeExecutionCapacityLimit(1)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/execution-capacity/limit')
    expect(init?.method).toBe('PUT')
    expect(init?.body).toBe('{"limit":1}')
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(String(url)).not.toContain('?')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it.each([1, 2] as const)('serializes only the exact integer %i, with no other member', async (limit) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(availableCapacityWire(0, limit)), 200))

    await executionCapacityLimitClient().changeExecutionCapacityLimit(limit)

    expect(JSON.parse(String(fetch.mock.calls[0][1]?.body))).toStrictEqual({ limit })
  })

  it('carries its own AbortSignal to the actual authenticated request, so aborting it cancels that request', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(
      (_url, init) =>
        new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')), { once: true })
        }),
    )
    const controller = new AbortController()

    const change = executionCapacityLimitClient(controller.signal).changeExecutionCapacityLimit(1)
    controller.abort()
    const caught = await change.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch.mock.calls[0][1]?.signal).toBe(controller.signal)
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('hands on the parsed body itself, so the decoder judges the original types, explicit nulls and members', async () => {
    const body = { ...capacityWireFor('Unavailable', { limit: null }), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await executionCapacityLimitClient().changeExecutionCapacityLimit(1)

    expect(read).toEqual(body)
    expect(toExecutionCapacityReading(read)).toBeNull()
  })

  it('carries an exact limit-one reading through to the decoder', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(capacityWireFor('AtCapacity', {}, 1, 1)), 200))

    const read = await executionCapacityLimitClient().changeExecutionCapacityLimit(1)

    expect(toExecutionCapacityReading(read)).toMatchObject({ status: 'AtCapacity', recordedOwners: 1, limit: 1 })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await executionCapacityLimitClient().changeExecutionCapacityLimit(2)

    expect(read).toBeUndefined()
    expect(toExecutionCapacityReading(read)).toBeNull()
  })

  it.each([
    [400, 'execution_capacity.invalid_request'],
    [409, 'execution_capacity.limit_below_recorded_owners'],
    [409, 'execution_capacity.unavailable'],
    [413, 'execution_capacity.body_too_large'],
  ])('rejects the declared %s (%s) as the typed shared problem with its fixed code after exactly one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await executionCapacityLimitClient()
      .changeExecutionCapacityLimit(1)
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(status)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 415, 500])('rejects an undeclared %s answer with the generated ApiException after exactly one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await executionCapacityLimitClient()
      .changeExecutionCapacityLimit(1)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
