import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentAttemptVerificationEvidenceWireClient, AgentAttemptVerificationWindowWireClient } from '../features/cockpit/agentAttemptVerificationWire'
import { decideVerificationWindowAnswer, toAgentAttemptVerificationEvidence } from '../features/cockpit/agentAttemptVerification'
import {
  recordedVerificationFor,
  refusedVerificationWindow,
  verificationExpectationFor,
  verificationWindowAnswer,
  verificationWindowRequest,
} from '../features/cockpit/agentAttemptVerificationFixture'
import { agentAttemptVerificationEvidenceClient, agentAttemptVerificationWindowClient } from './clients'
import {
  ApiException,
  ApiProblemDetails,
  GetAgentAttemptVerificationEvidenceEndpointClient,
  GetAgentAttemptVerificationEvidenceResponse,
  GetAgentAttemptVerificationOutputWindowEndpointClient,
  GetAgentAttemptVerificationOutputWindowResponse,
} from './generated/api-client'

// The recorded-verification reads (ADR-0052): the cockpit's factories are the parsed-body facades over the generated operations, wired to
// the launch session's base URL and the authenticated transport exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })
const problem = (status: number, code: string) =>
  JSON.stringify({
    type: `urn:devalente:problem:${status === 404 ? 'not-found' : 'validation'}`,
    title: 'fixed',
    status,
    detail: 'See the errors property for details.',
    traceId: '00-abc-def-00',
    errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
  })

afterEach(() => vi.restoreAllMocks())

describe('agentAttemptVerificationEvidenceClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET for exactly the run and attempt', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"status":"Unavailable"}', 200))
    const client = agentAttemptVerificationEvidenceClient()

    expect(client).toBeInstanceOf(AgentAttemptVerificationEvidenceWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptVerificationEvidenceEndpointClient)
    await client.readVerificationEvidence('run-1', 'attempt-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/verification-evidence')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('hands on the parsed body itself, keeping the explicit nulls and false values the generated model would rebuild', async () => {
    const body = { ...recordedVerificationFor('run-1', 2), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptVerificationEvidenceClient().readVerificationEvidence('run-1', body.attemptId)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentAttemptVerificationEvidenceResponse)
    // The undeclared field survives to the decoder, which refuses the whole answer instead of ignoring it.
    expect(toAgentAttemptVerificationEvidence(read, verificationExpectationFor('run-1', 2))).toBeNull()
  })

  it('carries an exact answer, with its string times and unknown truncation, through to the decoder', async () => {
    const body = recordedVerificationFor('run-1', 2)
    body.members[0] = { ...body.members[0], standardError: { status: 'Recorded', artifactId: 'e-1', byteLength: 3, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null } as never }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptVerificationEvidenceClient().readVerificationEvidence('run-1', body.attemptId)

    const accepted = toAgentAttemptVerificationEvidence(read, verificationExpectationFor('run-1', 2))
    expect(accepted?.status === 'Recorded' && accepted.members[0].standardError).toMatchObject({ truncated: null })
    expect(accepted?.status === 'Recorded' && accepted.members[0].claimedAtUtc).toBe('2026-10-09T10:00:00.1234567Z')
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentAttemptVerificationEvidenceClient().readVerificationEvidence('run-1', 'attempt-1')

    expect(read).toBeUndefined()
    expect(toAgentAttemptVerificationEvidence(read, verificationExpectationFor('run-1', 1))).toBeNull()
  })

  it('rejects the declared 404 as the typed shared problem with its fixed code after one request', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(problem(404, 'agent_attempt_verification.not_found'), 404))

    const caught = await agentAttemptVerificationEvidenceClient().readVerificationEvidence('run-1', 'attempt-1').catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(404)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual(['agent_attempt_verification.not_found'])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await agentAttemptVerificationEvidenceClient().readVerificationEvidence('run-1', 'attempt-1').catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

describe('agentAttemptVerificationWindowClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless GET with the explicit member, stream, offset and cap', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', 200))
    const client = agentAttemptVerificationWindowClient()

    expect(client).toBeInstanceOf(AgentAttemptVerificationWindowWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptVerificationOutputWindowEndpointClient)
    await client.readVerificationWindow('run-1', 'attempt-1', 'execution-1', 'stderr', 6, 64)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(
      'http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/verification-evidence/execution-1/artifact-window/stderr?fromOffset=6&maxBytes=64',
    )
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('sends a zero offset explicitly instead of omitting it', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', 200))

    await agentAttemptVerificationWindowClient().readVerificationWindow('run-1', 'attempt-1', 'execution-1', 'stdout', 0, 16384)

    expect(String(fetch.mock.calls[0][0])).toMatch(/\/artifact-window\/stdout\?fromOffset=0&maxBytes=16384$/)
  })

  it('hands on the parsed body itself and carries an exact window to the decoder', async () => {
    const request = verificationWindowRequest()
    const body = verificationWindowAnswer(request, 'line one\nline two\n', 0, { nextOffset: 18 })
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await agentAttemptVerificationWindowClient().readVerificationWindow(
      request.runId, request.attemptId, request.executionId, 'stdout', 0, 16384,
    )

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetAgentAttemptVerificationOutputWindowResponse)
    expect(decideVerificationWindowAnswer(read, request)).toMatchObject({ kind: 'ok', text: 'line one\nline two\n', nextOffset: 18 })
  })

  it('keeps a refused window null artifact and zero length exactly for the decoder', async () => {
    const request = verificationWindowRequest()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(refusedVerificationWindow(request, 'IntegrityMismatch', 4)), 200))

    const read = await agentAttemptVerificationWindowClient().readVerificationWindow(
      request.runId, request.attemptId, request.executionId, 'stdout', 4, 64,
    )

    expect(decideVerificationWindowAnswer(read, { ...request, fromOffset: 4 })).toEqual({ kind: 'integrity-mismatch' })
  })

  it.each(['', 'not json'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await agentAttemptVerificationWindowClient().readVerificationWindow('run-1', 'a', 'e', 'stdout', 0, 64)

    expect(read).toBeUndefined()
    expect(decideVerificationWindowAnswer(read, verificationWindowRequest())).toEqual({ kind: 'incoherent' })
  })

  it('rejects the declared 400 and 404 as the typed shared problem after one request each', async () => {
    const fetch = vi.spyOn(window, 'fetch')
    fetch.mockResolvedValueOnce(answer(problem(400, 'validation.invalid_offset'), 400))
    fetch.mockResolvedValueOnce(answer(problem(404, 'agent_attempt_verification.not_found'), 404))
    const client = agentAttemptVerificationWindowClient()

    const bad = await client.readVerificationWindow('run-1', 'a', 'e', 'stdout', 99, 64).catch((error: unknown) => error)
    const missing = await client.readVerificationWindow('run-1', 'a', 'e', 'stdout', 0, 64).catch((error: unknown) => error)

    expect(bad).toBeInstanceOf(ApiProblemDetails)
    expect((bad as ApiProblemDetails).errors?.map((error) => error.code)).toEqual(['validation.invalid_offset'])
    expect(missing).toBeInstanceOf(ApiProblemDetails)
    expect((missing as ApiProblemDetails).status).toBe(404)
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await agentAttemptVerificationWindowClient()
      .readVerificationWindow('run-1', 'a', 'e', 'stdout', 0, 64)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
