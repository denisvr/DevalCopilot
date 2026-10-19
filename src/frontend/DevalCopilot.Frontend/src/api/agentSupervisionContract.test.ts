import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentSupervisionWireClient } from '../features/cockpit/agentSupervisionWire'
import { toAgentSupervisionReading } from '../features/cockpit/agentSupervision'
import { needsAttentionWire, noAgentAttemptWire, observationWire, observedWire } from '../features/cockpit/agentSupervisionFixture'
import { agentSupervisionClient } from './clients'
import { ApiException, ApiProblemDetails, GetAgentSupervisionEndpointClient, GetAgentSupervisionResponse } from './generated/api-client'

// The Agent supervision read (ADR-0056): the generated operation keeps the revision and every time as strings but coerces explicit
// nulls, drops unknown members and rebuilds the graph, so the cockpit's factory is a parsed-body facade over it, wired to the launch
// session's base URL and the authenticated transport exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const RUN = 'run-1'

afterEach(() => vi.restoreAllMocks())

const problem = (status: number, code: string) => ({
  type: 'urn:devalente:problem:validation',
  title: 'fixed',
  status,
  detail: 'See the errors property for details.',
  traceId: '00-abc-def-00',
  errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
})

describe('the generated Agent supervision operation', () => {
  it('rebuilds the graph: an explicit null observation and an unknown member vanish, which is why the decoder reads the raw body', () => {
    const body = { ...noAgentAttemptWire(RUN), extra: 'kept' }

    const generated = GetAgentSupervisionResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toAgentSupervisionReading(body, RUN)).toBeNull()
  })

  it('keeps the revision and every time as the strings the host wrote, while a number would silently lose precision', async () => {
    const body = observedWire(RUN, { observation: observationWire({ revision: '9007199254740993' }) })
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(body), 200)))

    const generated = await new GetAgentSupervisionEndpointClient('http://host.invalid', { fetch }).getAgentSupervision(RUN)

    expect(generated).toBeInstanceOf(GetAgentSupervisionResponse)
    expect(generated.observation?.revision).toBe('9007199254740993')
    expect(typeof generated.observedAtUtc).toBe('string')
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
  })
})

describe('agentSupervisionClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET for exactly the run', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(observedWire(RUN)), 200))
    const client = agentSupervisionClient()

    expect(client).toBeInstanceOf(AgentSupervisionWireClient)
    expect(client).toBeInstanceOf(GetAgentSupervisionEndpointClient)
    await client.readAgentSupervision(RUN)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-supervision')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('?')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('carries its own AbortSignal to the actual authenticated request, so aborting it cancels that request (ASUP-R2)', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation((_url, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')), { once: true })
    }))
    const controller = new AbortController()

    const read = agentSupervisionClient(controller.signal).readAgentSupervision(RUN)
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given, exactly as before', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(observedWire(RUN)), 200))

    await agentSupervisionClient().readAgentSupervision(RUN)

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping the explicit nulls and unknown members the generated model would lose', async () => {
    const body = { ...needsAttentionWire(RUN), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentSupervisionClient().readAgentSupervision(RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentSupervisionResponse)
    expect(toAgentSupervisionReading(read, RUN)).toBeNull()
  })

  it('carries an exact answer, with a 19-digit revision, through to the decoder', async () => {
    const body = observedWire(RUN, { observation: observationWire({ revision: '9223372036854775807' }) })
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentSupervisionClient().readAgentSupervision(RUN)

    expect(toAgentSupervisionReading(read, RUN)?.observation?.revision).toBe('9223372036854775807')
  })

  it('carries an explicit null attention and observation through, so the decoder can judge the closed relationships', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(noAgentAttemptWire(RUN)), 200))

    const read = (await agentSupervisionClient().readAgentSupervision(RUN)) as Record<string, unknown>

    expect(read.attempt).toBeNull()
    expect(read.observation).toBeNull()
    expect(read.attention).toBeNull()
    expect(toAgentSupervisionReading(read, RUN)?.status).toBe('NoAgentAttempt')
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentSupervisionClient().readAgentSupervision(RUN)

    expect(read).toBeUndefined()
    expect(toAgentSupervisionReading(read, RUN)).toBeNull()
  })

  it.each([
    [400, 'agent_supervision.query_not_allowed'],
    [400, 'agent_supervision.body_not_allowed'],
    [404, 'agent_supervision.not_found'],
  ])('rejects the declared %s (%s) as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await agentSupervisionClient()
      .readAgentSupervision(RUN)
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(status)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await agentSupervisionClient()
      .readAgentSupervision(RUN)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
