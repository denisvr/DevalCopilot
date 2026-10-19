import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentAttemptInputMessageWireClient } from '../features/cockpit/agentAttemptInputMessageWire'
import { toAgentAttemptInputMessage } from '../features/cockpit/agentAttemptInputMessage'
import { inputMessageExpectationFor, recordedInputMessageFor } from '../features/cockpit/agentAttemptInputMessageFixture'
import { agentAttemptInputMessageClient } from './clients'
import {
  ApiException,
  ApiProblemDetails,
  GetAgentAttemptInputMessageEndpointClient,
  GetAgentAttemptInputMessageResponse,
} from './generated/api-client'

// The selected-input content read (ADR-0053): the generated operation keeps both exact sequences and both times as strings (no Date
// revival), and the cockpit's factory is the parsed-body facade over it, wired to the launch session's base URL and the authenticated
// transport exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the generated input-message operation', () => {
  it('keeps both exact sequences and both times as the strings the host wrote', async () => {
    const body = recordedInputMessageFor('run-1', 2, 1)
    body.input = { ...body.input, collaborationSequence: '9007199254740993' }
    body.message = { ...body.message, sequence: '9007199254740993' }
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(body), 200)))

    const generated = await new GetAgentAttemptInputMessageEndpointClient('http://host.invalid', { fetch })
      .getAgentAttemptInputMessage('run-1', 'attempt-2', 'message-1')

    expect(generated).toBeInstanceOf(GetAgentAttemptInputMessageResponse)
    expect(generated.input?.collaborationSequence).toBe('9007199254740993')
    expect(generated.message?.sequence).toBe('9007199254740993')
    expect(typeof generated.input?.occurredAtUtc).toBe('string')
    expect(typeof generated.message?.occurredAtUtc).toBe('string')
    expect(generated.message?.occurredAtUtc).toBe(body.message.occurredAtUtc)
  })
})

describe('agentAttemptInputMessageClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run, attempt and message', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"status":"Unavailable"}', 200))
    const client = agentAttemptInputMessageClient()

    expect(client).toBeInstanceOf(AgentAttemptInputMessageWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptInputMessageEndpointClient)
    await client.readAgentAttemptInputMessage('run-1', 'attempt-1', 'message-7')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/inputs/message-7')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('hands on the parsed body itself, keeping the exact keys and nulls the generated model would rebuild', async () => {
    const body = { ...recordedInputMessageFor('run-1', 2, 1), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptInputMessageClient().readAgentAttemptInputMessage('run-1', body.attemptId, body.collaborationMessageId)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentAttemptInputMessageResponse)
    // The generated model drops the unknown member silently; the decoder sees it and refuses the whole answer.
    expect(JSON.stringify(GetAgentAttemptInputMessageResponse.fromJS(body).toJSON())).not.toContain('extra')
    expect(toAgentAttemptInputMessage(read, inputMessageExpectationFor('run-1', 2, 1))).toBeNull()
  })

  it('carries an exact recorded answer through to the decoder', async () => {
    const body = recordedInputMessageFor('run-1', 2, 1)
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptInputMessageClient().readAgentAttemptInputMessage('run-1', body.attemptId, body.collaborationMessageId)

    expect(toAgentAttemptInputMessage(read, inputMessageExpectationFor('run-1', 2, 1))).toMatchObject({
      status: 'Recorded',
      message: { content: { status: 'Available' } },
    })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentAttemptInputMessageClient().readAgentAttemptInputMessage('run-1', 'attempt-1', 'message-1')

    expect(read).toBeUndefined()
    expect(toAgentAttemptInputMessage(read, inputMessageExpectationFor('run-1', 1, 1))).toBeNull()
  })

  it('rejects the declared 404 as the typed shared problem with its fixed code after one request', async () => {
    const problem = {
      type: 'urn:devalente:problem:not-found',
      title: 'fixed',
      status: 404,
      detail: 'See the errors property for details.',
      traceId: '00-abc-def-00',
      errors: [{ code: 'agent_attempt_input_message.not_found', detail: 'fixed', pointer: null, parameters: null }],
    }
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem), 404))

    const caught = await agentAttemptInputMessageClient()
      .readAgentAttemptInputMessage('run-1', 'attempt-1', 'message-1')
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(404)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual(['agent_attempt_input_message.not_found'])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([400, 401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"errors":[{"code":"agent_attempt_input_message.not_found"}]}', status))

    const caught = await agentAttemptInputMessageClient()
      .readAgentAttemptInputMessage('run-1', 'attempt-1', 'message-1')
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
