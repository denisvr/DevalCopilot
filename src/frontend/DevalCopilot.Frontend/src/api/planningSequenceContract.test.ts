import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ArmPlanningSequenceWireClient,
  PlanningSequenceStatusWireClient,
  StopPlanningSequenceWireClient,
} from '../features/cockpit/planningSequenceWire'
import { toPlanningSequenceArmReceipt, toPlanningSequenceStatus, toPlanningSequenceStopAnswer } from '../features/cockpit/planningSequence'
import {
  SEQUENCE_ID,
  TUPLE,
  armReceiptFor,
  armableStatusFor,
  statusFor,
  stopAnswerFor,
} from '../features/cockpit/planningSequenceFixture'
import { armPlanningSequenceClient, planningSequenceStatusClient, stopPlanningSequenceClient } from './clients'
import {
  ApiException,
  ApiProblemDetails,
  ArmPlanningSequenceEndpointClient,
  ArmPlanningSequenceRequest,
  ArmPlanningSequenceResponse,
  GetPlanningSequenceEndpointClient,
  GetPlanningSequenceResponse,
  StopPlanningSequenceEndpointClient,
  StopPlanningSequenceResponse,
} from './generated/api-client'

// The planning-sequence operations (ADR-0055): the generated operations keep the revision, event sequence and times as strings but
// coerce explicit nulls, drop unknown members and rebuild the graph, so the cockpit's factories are parsed-body facades over them,
// wired to the launch session's base URL and the authenticated transport exactly as every other operation is. A clearly fictitious
// session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const RUN = 'run-1'

afterEach(() => vi.restoreAllMocks())

function expectAuthenticated(init: RequestInit | undefined, url: unknown, method: string) {
  expect(init?.method).toBe(method)
  const headers = new Headers(init?.headers)
  expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
  expect(headers.get('Accept')).toBe('application/json')
  expect(String(url)).not.toContain('not-a-real-session')
  expect(String(url)).not.toContain('?')
}

const problem = (status: number, code: string) => ({
  type: 'urn:devalente:problem:conflict',
  title: 'fixed',
  status,
  detail: 'See the errors property for details.',
  traceId: '00-abc-def-00',
  errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
})

describe('the generated planning-sequence operations', () => {
  it('rebuild the graph: an explicit null arming and an unknown member vanish, which is why the decoder reads the raw body', () => {
    const body = { ...armableStatusFor(RUN), arming: null, extra: 'kept' }

    const generated = GetPlanningSequenceResponse.fromJS(body)

    expect(generated.arming).toBeUndefined()
    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toPlanningSequenceStatus(body, RUN)).toBeNull()
  })

  it('keep the revision and every time as the strings the host wrote', async () => {
    const body = statusFor(RUN, 'Armed', {}, { revision: '9007199254740993' })
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(body), 200)))

    const generated = await new GetPlanningSequenceEndpointClient('http://host.invalid', { fetch }).getPlanningSequence(RUN)

    expect(generated).toBeInstanceOf(GetPlanningSequenceResponse)
    expect(generated.sequence?.revision).toBe('9007199254740993')
    expect(typeof generated.sequence?.armedAtUtc).toBe('string')
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
  })

  it('answer a receipt and a stop as generated models', async () => {
    const fetch = vi.fn((_: unknown, init?: RequestInit) =>
      Promise.resolve(answer(JSON.stringify(init?.method === 'POST' && init.body ? armReceiptFor(RUN) : stopAnswerFor(RUN)), 200)),
    )

    const arm = await new ArmPlanningSequenceEndpointClient('http://host.invalid', { fetch }).armPlanningSequence(RUN, new ArmPlanningSequenceRequest(TUPLE))
    const stop = await new StopPlanningSequenceEndpointClient('http://host.invalid', { fetch }).stopPlanningSequence(RUN, SEQUENCE_ID)

    expect(arm).toBeInstanceOf(ArmPlanningSequenceResponse)
    expect(stop).toBeInstanceOf(StopPlanningSequenceResponse)
  })
})

describe('planningSequenceStatusClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(armableStatusFor(RUN)), 200))
    const client = planningSequenceStatusClient()

    expect(client).toBeInstanceOf(PlanningSequenceStatusWireClient)
    expect(client).toBeInstanceOf(GetPlanningSequenceEndpointClient)
    await client.readPlanningSequence(RUN)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/planning-sequence')
    expectAuthenticated(init, url, 'GET')
    expect(init?.body).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping the explicit nulls and unknown members the generated model would lose', async () => {
    const body = { ...statusFor(RUN, 'Armed'), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await planningSequenceStatusClient().readPlanningSequence(RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetPlanningSequenceResponse)
    expect(toPlanningSequenceStatus(read, RUN)).toBeNull()
  })

  it('carries an exact answer, with a 19-digit event sequence and revision, through to the decoder', async () => {
    const body = statusFor(RUN, 'Armed', { latestEventSequence: '9223372036854775807' }, { revision: '9223372036854775807' })
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await planningSequenceStatusClient().readPlanningSequence(RUN)

    expect(toPlanningSequenceStatus(read, RUN)).toMatchObject({ latestEventSequence: '9223372036854775807' })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await planningSequenceStatusClient().readPlanningSequence(RUN)

    expect(read).toBeUndefined()
    expect(toPlanningSequenceStatus(read, RUN)).toBeNull()
  })

  it.each([
    [400, 'planning_sequence.body_not_allowed'],
    [404, 'planning_sequence.not_found'],
  ])('rejects the declared %s as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await planningSequenceStatusClient()
      .readPlanningSequence(RUN)
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(status)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await planningSequenceStatusClient()
      .readPlanningSequence(RUN)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

describe('armPlanningSequenceClient', () => {
  it('sends one authenticated POST whose JSON body is exactly the three members of the tuple', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(armReceiptFor(RUN)), 200))
    const client = armPlanningSequenceClient()

    expect(client).toBeInstanceOf(ArmPlanningSequenceWireClient)
    expect(client).toBeInstanceOf(ArmPlanningSequenceEndpointClient)
    const read = await client.armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/planning-sequence')
    expectAuthenticated(init, url, 'POST')
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
    expect(JSON.parse(init?.body as string)).toEqual(TUPLE)
    expect(Object.keys(JSON.parse(init?.body as string)).sort()).toEqual(['checkpointId', 'fingerprintSha256', 'workspaceId'])
    expect(toPlanningSequenceArmReceipt(read, RUN, TUPLE)).toMatchObject({ replayed: false, state: 'Armed' })
  })

  it('hands on the replayed receipt unchanged and an unknown member to the decoder, which refuses it', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValueOnce(answer(JSON.stringify(armReceiptFor(RUN, { replayed: true, state: 'Stopped', revision: '9' })), 200))
    vi.spyOn(window, 'fetch').mockResolvedValueOnce(answer(JSON.stringify({ ...armReceiptFor(RUN), extra: 'kept' }), 200))

    const replay = await armPlanningSequenceClient().armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))
    const extra = await armPlanningSequenceClient().armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))

    expect(toPlanningSequenceArmReceipt(replay, RUN, TUPLE)).toMatchObject({ replayed: true, state: 'Stopped' })
    expect(toPlanningSequenceArmReceipt(extra, RUN, TUPLE)).toBeNull()
  })

  it.each(['', 'not json'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await armPlanningSequenceClient().armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))

    expect(read).toBeUndefined()
    expect(toPlanningSequenceArmReceipt(read, RUN, TUPLE)).toBeNull()
  })

  it.each([
    [400, 'planning_sequence.invalid_request'],
    [404, 'planning_sequence.not_found'],
    [409, 'planning_sequence.tuple_stale'],
    [413, 'planning_sequence.body_too_large'],
  ])('rejects the declared %s as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await armPlanningSequenceClient()
      .armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await armPlanningSequenceClient()
      .armPlanningSequenceRaw(RUN, new ArmPlanningSequenceRequest(TUPLE))
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

describe('stopPlanningSequenceClient', () => {
  it('sends one authenticated bodyless POST for exactly the run and the sequence', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(stopAnswerFor(RUN)), 200))
    const client = stopPlanningSequenceClient()

    expect(client).toBeInstanceOf(StopPlanningSequenceWireClient)
    expect(client).toBeInstanceOf(StopPlanningSequenceEndpointClient)
    const read = await client.stopPlanningSequenceRaw(RUN, SEQUENCE_ID)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(`http://127.0.0.1:5099/api/runs/run-1/planning-sequence/${SEQUENCE_ID}/stop`)
    expectAuthenticated(init, url, 'POST')
    expect(init?.body).toBeUndefined()
    expect(toPlanningSequenceStopAnswer(read, RUN, SEQUENCE_ID)).toMatchObject({ state: 'Stopping', changed: true })
  })

  it('encodes the identifiers it is given into the path, never into a query', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(stopAnswerFor(RUN)), 200))

    await stopPlanningSequenceClient().stopPlanningSequenceRaw('run/1?x', 'seq 1')

    expect(String(fetch.mock.calls[0][0])).toBe('http://127.0.0.1:5099/api/runs/run%2F1%3Fx/planning-sequence/seq%201/stop')
  })

  it.each(['', 'not json'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await stopPlanningSequenceClient().stopPlanningSequenceRaw(RUN, SEQUENCE_ID)

    expect(read).toBeUndefined()
    expect(toPlanningSequenceStopAnswer(read, RUN, SEQUENCE_ID)).toBeNull()
  })

  it.each([
    [400, 'planning_sequence.invalid_request'],
    [404, 'planning_sequence.not_found'],
  ])('rejects the declared %s as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await stopPlanningSequenceClient()
      .stopPlanningSequenceRaw(RUN, SEQUENCE_ID)
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 409, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await stopPlanningSequenceClient()
      .stopPlanningSequenceRaw(RUN, SEQUENCE_ID)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
