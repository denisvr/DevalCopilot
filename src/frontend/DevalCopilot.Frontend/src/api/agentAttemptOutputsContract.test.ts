import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentAttemptOutputsWireClient } from '../features/cockpit/agentAttemptOutputsWire'
import { toAgentAttemptOutputs } from '../features/cockpit/agentAttemptOutputs'
import { outputsExpectationFor, recordedOutputsFor } from '../features/cockpit/agentAttemptOutputsFixture'
import { agentAttemptOutputsClient } from './clients'
import { ApiException, ApiProblemDetails, GetAgentAttemptOutputsEndpointClient, GetAgentAttemptOutputsResponse } from './generated/api-client'

// The recorded-output read (ADR-0054): the generated operation keeps the exact sequence and the time as strings (no Date revival), and
// the cockpit's factory is the parsed-body facade over it, wired to the launch session's base URL and the authenticated transport
// exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the generated recorded-output operation', () => {
  it('keeps the exact sequence and the occurred time as the strings the host wrote', async () => {
    const body = recordedOutputsFor('run-1', 2, 1)
    body.outputMessages[0] = { ...body.outputMessages[0], collaborationSequence: '9007199254740993' }
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(body), 200)))

    const generated = await new GetAgentAttemptOutputsEndpointClient('http://host.invalid', { fetch }).getAgentAttemptOutputs('run-1', 'attempt-2')

    expect(generated).toBeInstanceOf(GetAgentAttemptOutputsResponse)
    expect(generated.outputMessages?.[0].collaborationSequence).toBe('9007199254740993')
    expect(generated.outputMessages?.[0].occurredAtUtc).toBe(body.outputMessages[0].occurredAtUtc)
    expect(typeof generated.outputMessages?.[0].occurredAtUtc).toBe('string')
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
  })
})

describe('agentAttemptOutputsClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run and attempt', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"status":"Unavailable"}', 200))
    const client = agentAttemptOutputsClient()

    expect(client).toBeInstanceOf(AgentAttemptOutputsWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptOutputsEndpointClient)
    await client.readAgentAttemptOutputs('run-1', 'attempt-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/outputs')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-session')
    expect(String(url)).not.toContain('?')
  })

  it('hands on the parsed body itself, keeping what the generated model would drop or rebuild', async () => {
    const body = {
      runId: 'run-1',
      attemptId: 'attempt-1',
      status: 'Unavailable',
      attempt: false,
      outputMessageTotalCount: null,
      outputMessages: [],
      extra: 'kept',
    }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptOutputsClient().readAgentAttemptOutputs('run-1', 'attempt-1')

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentAttemptOutputsResponse)
    // The generated model would have turned `attempt: false` into an absent header and dropped the unknown member; the decoder sees both.
    expect(GetAgentAttemptOutputsResponse.fromJS(body).attempt).toBeUndefined()
    expect(JSON.stringify(GetAgentAttemptOutputsResponse.fromJS(body).toJSON())).not.toContain('extra')
    expect(toAgentAttemptOutputs(read, { ...outputsExpectationFor('run-1', 1), attemptId: 'attempt-1' })).toBeNull()
  })

  it('carries an exact recorded answer, with every reference of a long set, through to the decoder', async () => {
    const body = recordedOutputsFor('run-1', 2, 64)
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptOutputsClient().readAgentAttemptOutputs('run-1', body.attemptId)

    expect(toAgentAttemptOutputs(read, outputsExpectationFor('run-1', 2))).toMatchObject({ status: 'Recorded', totalCount: 64 })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentAttemptOutputsClient().readAgentAttemptOutputs('run-1', 'attempt-1')

    expect(read).toBeUndefined()
    expect(toAgentAttemptOutputs(read, outputsExpectationFor('run-1', 1))).toBeNull()
  })

  it('rejects the declared 404 as the typed shared problem with its fixed code after one request', async () => {
    const problem = {
      type: 'urn:devalente:problem:not-found',
      title: 'fixed',
      status: 404,
      detail: 'See the errors property for details.',
      traceId: '00-abc-def-00',
      errors: [{ code: 'agent_attempt_outputs.not_found', detail: 'fixed', pointer: null, parameters: null }],
    }
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem), 404))

    const caught = await agentAttemptOutputsClient()
      .readAgentAttemptOutputs('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(404)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual(['agent_attempt_outputs.not_found'])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"errors":[{"code":"agent_attempt_outputs.not_found"}]}', status))

    const caught = await agentAttemptOutputsClient()
      .readAgentAttemptOutputs('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
