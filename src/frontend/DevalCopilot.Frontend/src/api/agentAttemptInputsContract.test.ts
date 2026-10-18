import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentAttemptInputsWireClient } from '../features/cockpit/agentAttemptInputsWire'
import { toAgentAttemptInputs } from '../features/cockpit/agentAttemptInputs'
import { inputsExpectationFor, recordedInputsFor } from '../features/cockpit/agentAttemptInputsFixture'
import { agentAttemptInputsClient } from './clients'
import { ApiException, ApiProblemDetails, GetAgentAttemptInputsEndpointClient, GetAgentAttemptInputsResponse } from './generated/api-client'

// The recorded-input read (ADR-0051): the generated operation keeps the exact sequence and the time as strings (no Date revival), and the
// cockpit's factory is the parsed-body facade over it, wired to the launch session's base URL and the authenticated transport exactly as
// every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the generated recorded-input operation', () => {
  it('keeps the exact sequence and the occurred time as the strings the host wrote', async () => {
    const body = recordedInputsFor('run-1', 2, 1)
    body.inputMessages[0] = { ...body.inputMessages[0], collaborationSequence: '9007199254740993' }
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(body), 200)))

    const generated = await new GetAgentAttemptInputsEndpointClient('http://host.invalid', { fetch }).getAgentAttemptInputs('run-1', 'attempt-2')

    expect(generated).toBeInstanceOf(GetAgentAttemptInputsResponse)
    expect(generated.inputMessages?.[0].collaborationSequence).toBe('9007199254740993')
    expect(generated.inputMessages?.[0].occurredAtUtc).toBe(body.inputMessages[0].occurredAtUtc)
    expect(typeof generated.inputMessages?.[0].occurredAtUtc).toBe('string')
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
  })
})

describe('agentAttemptInputsClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run and attempt', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"status":"Unavailable"}', 200))
    const client = agentAttemptInputsClient()

    expect(client).toBeInstanceOf(AgentAttemptInputsWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptInputsEndpointClient)
    await client.readAgentAttemptInputs('run-1', 'attempt-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/inputs')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('hands on the parsed body itself, keeping what the generated model would drop or rebuild', async () => {
    const body = {
      runId: 'run-1',
      attemptId: 'attempt-1',
      status: 'Unavailable',
      attempt: false,
      inputMessages: [],
      inputMessageTotalCount: null,
      inputMessagesOmitted: false,
      extra: 'kept',
    }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptInputsClient().readAgentAttemptInputs('run-1', 'attempt-1')

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentAttemptInputsResponse)
    // The generated model would have turned `attempt: false` into an absent header; the decoder sees the false and refuses it.
    expect(GetAgentAttemptInputsResponse.fromJS(body).attempt).toBeUndefined()
    expect(
      toAgentAttemptInputs(read, { ...inputsExpectationFor('run-1', 1), attemptId: 'attempt-1' }),
    ).toBeNull()
  })

  it('carries an exact recorded answer through to the decoder', async () => {
    const body = recordedInputsFor('run-1', 2, 11)
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptInputsClient().readAgentAttemptInputs('run-1', body.attemptId)

    expect(toAgentAttemptInputs(read, inputsExpectationFor('run-1', 2))).toMatchObject({ status: 'Recorded', totalCount: 11, omitted: true })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentAttemptInputsClient().readAgentAttemptInputs('run-1', 'attempt-1')

    expect(read).toBeUndefined()
    expect(toAgentAttemptInputs(read, inputsExpectationFor('run-1', 1))).toBeNull()
  })

  it('rejects the declared 404 as the typed shared problem with its fixed code after one request', async () => {
    const problem = {
      type: 'urn:devalente:problem:not-found',
      title: 'fixed',
      status: 404,
      detail: 'See the errors property for details.',
      traceId: '00-abc-def-00',
      errors: [{ code: 'agent_attempt_inputs.not_found', detail: 'fixed', pointer: null, parameters: null }],
    }
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem), 404))

    const caught = await agentAttemptInputsClient()
      .readAgentAttemptInputs('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(404)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual(['agent_attempt_inputs.not_found'])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"errors":[{"code":"agent_attempt_inputs.not_found"}]}', status))

    const caught = await agentAttemptInputsClient()
      .readAgentAttemptInputs('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
