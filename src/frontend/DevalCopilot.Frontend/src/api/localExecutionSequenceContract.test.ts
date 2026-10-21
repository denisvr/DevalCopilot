import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ArmLocalExecutionSequenceWireClient,
  LocalExecutionSequenceAvailabilityWireClient,
  LocalExecutionSequenceStatusWireClient,
  StopLocalExecutionSequenceWireClient,
} from '../features/cockpit/localExecutionSequenceWire'
import {
  toLocalExecutionArmReceipt,
  toLocalExecutionSequenceAvailability,
  toLocalExecutionSequenceStatus,
  toLocalExecutionStopAnswer,
} from '../features/cockpit/localExecutionSequence'
import {
  LOCAL_PLAN_ID,
  LOCAL_SEQUENCE_ID,
  LOCAL_TUPLE,
  localArmReceiptFor,
  localAvailabilityFor,
  localNoneStatusFor,
  localStatusFor,
  localStopAnswerFor,
} from '../features/cockpit/localExecutionSequenceFixture'
import {
  armLocalExecutionSequenceClient,
  localExecutionSequenceAvailabilityClient,
  localExecutionSequenceStatusClient,
  stopLocalExecutionSequenceClient,
} from './clients'
import {
  ApiProblemDetails,
  ArmLocalExecutionSequenceEndpointClient,
  GetLocalExecutionSequenceAvailabilityEndpointClient,
  GetLocalExecutionSequenceEndpointClient,
  GetLocalExecutionSequenceResponse,
  StopLocalExecutionSequenceEndpointClient,
} from './generated/api-client'

// The local-execution-sequence operations (ADR-0060): the generated operations coerce explicit nulls and drop unknown members, so the
// cockpit's factories are parsed-body facades over them, wired to the launch session's base URL and the authenticated transport exactly as
// every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const RUN = 'run-1'

afterEach(() => vi.restoreAllMocks())

function expectAuthenticated(init: RequestInit | undefined, url: unknown, method: string) {
  expect(init?.method).toBe(method)
  const headers = new Headers(init?.headers)
  expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
  expect(String(url)).not.toContain('not-a-real-session')
  expect(String(url)).not.toContain('?')
}

describe('the generated local-execution-sequence operations', () => {
  it('lose the explicit nulls and unknown members the decoder needs, which is why it reads the raw body', () => {
    const body = { ...localNoneStatusFor(RUN), sequence: null, extra: 'kept' }

    const generated = GetLocalExecutionSequenceResponse.fromJS(body)

    expect(generated.sequence).toBeUndefined()
    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toLocalExecutionSequenceStatus(body, RUN)).toBeNull()
  })
})

describe('localExecutionSequenceStatusClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(localNoneStatusFor(RUN)), 200))
    const client = localExecutionSequenceStatusClient()

    expect(client).toBeInstanceOf(LocalExecutionSequenceStatusWireClient)
    expect(client).toBeInstanceOf(GetLocalExecutionSequenceEndpointClient)
    await client.readLocalExecutionSequence(RUN)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence')
    expectAuthenticated(init, url, 'GET')
    expect(init?.body).toBeUndefined()
  })

  it('hands on the parsed body itself and keeps 19-digit Int64 texts for the decoder', async () => {
    const body = localStatusFor(RUN, 'Armed', { latestEventSequence: '9223372036854775807' }, { revision: '9223372036854775807' })
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localExecutionSequenceStatusClient().readLocalExecutionSequence(RUN)

    expect(read).toEqual(body)
    expect(toLocalExecutionSequenceStatus(read, RUN)).toMatchObject({ latestEventSequence: '9223372036854775807' })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await localExecutionSequenceStatusClient().readLocalExecutionSequence(RUN)

    expect(read).toBeUndefined()
    expect(toLocalExecutionSequenceStatus(read, RUN)).toBeNull()
  })

  it('rejects a non-200 answer as the shared problem, never as a body', async () => {
    const problem = { type: 'x', title: 't', status: 404, detail: 'd', traceId: 't', errors: [{ code: 'local_execution_sequence.not_found', detail: 'x' }] }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem), 404))

    await expect(localExecutionSequenceStatusClient().readLocalExecutionSequence(RUN)).rejects.toBeInstanceOf(ApiProblemDetails)
  })

  it('cancels its actual request through the signal it was given', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(
      (_url, init) => new Promise((_, reject) => init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))),
    )
    const controller = new AbortController()

    const pending = localExecutionSequenceStatusClient(controller.signal).readLocalExecutionSequence(RUN)
    controller.abort()

    await expect(pending).rejects.toBeDefined()
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

describe('localExecutionSequenceAvailabilityClient', () => {
  it('sends one authenticated bodyless GET for exactly the run and plan', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(localAvailabilityFor(RUN)), 200))
    const client = localExecutionSequenceAvailabilityClient()

    expect(client).toBeInstanceOf(LocalExecutionSequenceAvailabilityWireClient)
    expect(client).toBeInstanceOf(GetLocalExecutionSequenceAvailabilityEndpointClient)
    const read = await client.readAvailability(RUN, LOCAL_PLAN_ID)

    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(`http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/availability/${LOCAL_PLAN_ID}`)
    expectAuthenticated(init, url, 'GET')
    expect(init?.body).toBeUndefined()
    expect(toLocalExecutionSequenceAvailability(read, RUN, LOCAL_PLAN_ID)).not.toBeNull()
  })
})

describe('armLocalExecutionSequenceClient', () => {
  it('sends one authenticated POST whose body is exactly the five members of the tuple, and hands on the receipt', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(localArmReceiptFor(RUN)), 200))
    const client = armLocalExecutionSequenceClient()

    expect(client).toBeInstanceOf(ArmLocalExecutionSequenceWireClient)
    expect(client).toBeInstanceOf(ArmLocalExecutionSequenceEndpointClient)
    const read = await client.armLocalExecutionSequenceRaw(RUN, LOCAL_TUPLE)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence')
    expectAuthenticated(init, url, 'POST')
    expect(JSON.parse(String(init?.body))).toEqual(LOCAL_TUPLE)
    expect(toLocalExecutionArmReceipt(read, RUN, LOCAL_TUPLE)).not.toBeNull()
  })
})

describe('stopLocalExecutionSequenceClient', () => {
  it('sends one authenticated bodyless POST for exactly the run and sequence, and hands on the answer', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(localStopAnswerFor(RUN)), 200))
    const client = stopLocalExecutionSequenceClient()

    expect(client).toBeInstanceOf(StopLocalExecutionSequenceWireClient)
    expect(client).toBeInstanceOf(StopLocalExecutionSequenceEndpointClient)
    const read = await client.stopLocalExecutionSequenceRaw(RUN, LOCAL_SEQUENCE_ID)

    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(`http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/${LOCAL_SEQUENCE_ID}/stop`)
    expectAuthenticated(init, url, 'POST')
    expect(init?.body).toBeUndefined()
    expect(toLocalExecutionStopAnswer(read, RUN, LOCAL_SEQUENCE_ID)).not.toBeNull()
  })
})
