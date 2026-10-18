import { describe, expect, it, vi } from 'vitest'
import { ApiException, GetAgentAttemptEvidenceEndpointClient } from '../../api/generated/api-client'
import { toAgentAttemptExecutionFacts } from './agentAttemptExecutionFacts'
import { toAgentAttemptEvidence } from './agentAttemptHistory'
import type { AgentAttemptIdentityExpectation } from './agentAttemptHistory'
import { AgentAttemptEvidenceWireClient, toWireInstant, withoutRootTimestamps } from './agentAttemptEvidenceWire'

// Every case goes through the real generated operation (URL, request, status handling, JSON parsing and model construction) with a raw
// JSON body injected at the transport, so the facade is judged on what the host would actually send, never on a prebuilt Date.
const EXPECTED: AgentAttemptIdentityExpectation = {
  attemptId: 'attempt-1',
  attemptNumber: 1,
  role: 'Implementer',
  provider: 'ClaudeCode',
  responseContract: 'ImplementationReport',
}

const body = (overrides: Record<string, unknown> = {}) => ({
  identityValid: true,
  attemptId: 'attempt-1',
  attemptNumber: 1,
  attemptStatus: 'Failed',
  claimedAtUtc: '2026-10-09T10:00:00.1234567+00:00',
  completedAtUtc: '2026-10-09T10:01:05+00:00',
  provider: 'ClaudeCode',
  role: 'Implementer',
  responseContract: 'ImplementationReport',
  outcome: 'ProviderInvocationFailed',
  dispatchedAtUtc: '2026-10-09T10:00:02.5+00:00',
  processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 900000 },
  tokenUsage: { inputTokens: 1200, outputTokens: 80, cacheCreationInputTokens: 0, cacheReadInputTokens: 30 },
  artifacts: [{ purpose: 'AgentStandardError', byteLength: 12, truncated: false, captureOutcome: 'Captured' }],
  ...overrides,
})

/** A transport answering one raw text, and the facade (or, for contrast, the bare generated client) reading through it. */
function answering(text: string, status = 200) {
  const fetch = vi.fn((_url: RequestInfo, _init?: RequestInit) =>
    Promise.resolve(new Response(text, { status, headers: { 'Content-Type': status === 200 ? 'application/json' : 'application/problem+json' } })),
  )
  return { fetch, client: new AgentAttemptEvidenceWireClient('http://host.invalid', { fetch }) }
}

const read = (overrides: Record<string, unknown>) => answering(JSON.stringify(body(overrides))).client.getAgentAttemptEvidence('run-1', 'attempt-1')
const isInvalidDate = (value: unknown) => value instanceof Date && Number.isNaN(value.getTime())

describe('AgentAttemptEvidenceWireClient', () => {
  it('rejects the reproduced numeric dispatch value that the generated client alone revives as 2001-01-01', async () => {
    const raw = JSON.stringify(body({ dispatchedAtUtc: 1 }))
    const fetch = vi.fn(() => Promise.resolve(new Response(raw, { status: 200 })))
    const generated = await new GetAgentAttemptEvidenceEndpointClient('http://host.invalid', { fetch }).getAgentAttemptEvidence('run-1', 'attempt-1')
    expect(generated.dispatchedAtUtc?.toISOString()).toBe('2001-01-01T00:00:00.000Z')

    const answer = await answering(raw).client.getAgentAttemptEvidence('run-1', 'attempt-1')
    expect(isInvalidDate(answer.dispatchedAtUtc)).toBe(true)

    const accepted = toAgentAttemptEvidence(answer, EXPECTED)
    expect(accepted?.status).toBe('success')
    const facts = toAgentAttemptExecutionFacts(answer)
    expect(facts.dispatched).toEqual({ status: 'unavailable' })
    expect(facts.process).toEqual({ status: 'unavailable' })
    expect(facts.tokenUsage).toEqual({ status: 'unavailable' })
    expect(facts.timeout).toEqual({ status: 'known', milliseconds: 900000 })
    expect(facts.claimed.status).toBe('recorded')
  })

  it.each<[string, unknown]>([
    ['zero', 0],
    ['false', false],
    ['an empty string', ''],
    ['true', true],
    ['a negative number', -1],
    ['a fractional number', 1.5],
    ['a large epoch-like number', 1_760_000_000_000],
    ['an array', ['2026-10-09T10:00:02+00:00']],
    ['an empty array', []],
    ['an object', { value: '2026-10-09T10:00:02+00:00' }],
    ['an empty object', {}],
  ])('keeps a dispatch value that is %s present and invalid, never absent or plausible', async (_label, dispatchedAtUtc) => {
    const answer = await read({ dispatchedAtUtc })

    expect(isInvalidDate(answer.dispatchedAtUtc)).toBe(true)
    expect(toAgentAttemptExecutionFacts(answer).dispatched).toEqual({ status: 'unavailable' })
    expect(toAgentAttemptExecutionFacts(answer).process).toEqual({ status: 'unavailable' })
  })

  // Values whose own `toString` is not callable: the generated conversion `new Date(value.toString())` would throw on them and lose the
  // whole answer, so they must never reach it.
  const UNCONVERTIBLE: readonly [string, unknown][] = [
    ['an object whose toString is 0', { toString: 0 }],
    ['an object whose toString is null', { toString: null }],
    ['an object whose toString and valueOf are not callable', { toString: 0, valueOf: 0 }],
    ['an array holding such an object', [{ toString: 0 }]],
    ['a nested array holding such an object', [[{ toString: null }]]],
  ]

  it('shows that the generated client alone loses the whole answer on such a value', async () => {
    const raw = JSON.stringify(body({ dispatchedAtUtc: { toString: 0 } }))
    const fetch = vi.fn(() => Promise.resolve(new Response(raw, { status: 200 })))
    const caught = await new GetAgentAttemptEvidenceEndpointClient('http://host.invalid', { fetch })
      .getAgentAttemptEvidence('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(TypeError)
  })

  it.each(UNCONVERTIBLE)('keeps an optional dispatch value that is %s unavailable, with every independent fact intact', async (_label, dispatchedAtUtc) => {
    const answer = await read({ dispatchedAtUtc })
    const facts = toAgentAttemptExecutionFacts(answer)

    expect(isInvalidDate(answer.dispatchedAtUtc)).toBe(true)
    expect(toAgentAttemptEvidence(answer, EXPECTED)?.status).toBe('success')
    expect(facts.dispatched).toEqual({ status: 'unavailable' })
    expect(facts.process).toEqual({ status: 'unavailable' })
    expect(facts.tokenUsage).toEqual({ status: 'unavailable' })
    expect(facts.claimed).toEqual({ status: 'recorded', at: new Date('2026-10-09T10:00:00.123Z') })
    expect(facts.completed).toEqual({ status: 'recorded', at: new Date('2026-10-09T10:01:05.000Z') })
    expect(facts.timeout).toEqual({ status: 'known', milliseconds: 900000 })
    expect(answer.outcome).toBe('ProviderInvocationFailed')
    expect(answer.artifacts?.map((artifact) => [artifact.purpose, artifact.byteLength])).toEqual([['AgentStandardError', 12]])
  })

  it.each(UNCONVERTIBLE)('keeps an optional end value that is %s unavailable and nothing else', async (_label, completedAtUtc) => {
    const answer = await read({ completedAtUtc })
    const facts = toAgentAttemptExecutionFacts(answer)

    expect(isInvalidDate(answer.completedAtUtc)).toBe(true)
    expect(facts.completed).toEqual({ status: 'unavailable' })
    expect(facts.dispatched.status).toBe('recorded')
    expect(facts.process.status).toBe('known')
    expect(facts.tokenUsage.status).toBe('known')
    expect(toAgentAttemptEvidence(answer, EXPECTED)?.status).toBe('success')
  })

  it.each(UNCONVERTIBLE)('answers a claim value that is %s without throwing, and the selected attempt rejects it whole', async (_label, claimedAtUtc) => {
    const answer = await read({ claimedAtUtc })

    expect(isInvalidDate(answer.claimedAtUtc)).toBe(true)
    expect(toAgentAttemptEvidence(answer, EXPECTED)).toBeNull()
  })

  it('parses every other member from the original text exactly as the generated client does, nested timestamps included', async () => {
    // An overflowing literal parses to Infinity; re-serializing the body would have turned it into null (absent). Members named like the
    // timestamps below the root and nested timestamps are left to the generated conversion.
    const raw = JSON.stringify(
      body({
        dispatchedAtUtc: { toString: 0 },
        accountUsageDecision: { state: 'Recorded', retrievedAtUtc: '2026-10-09T10:00:01+00:00', windows: [] },
      }),
    ).replace('"inputTokens":1200', '"inputTokens":1e400')
    const answer = await answering(raw).client.getAgentAttemptEvidence('run-1', 'attempt-1')

    expect(answer.tokenUsage?.inputTokens).toBe(Number.POSITIVE_INFINITY)
    expect(toAgentAttemptExecutionFacts({ ...answer, dispatchedAtUtc: new Date('2026-10-09T10:00:02Z') } as typeof answer).tokenUsage).toEqual({
      status: 'unavailable',
    })
    expect(answer.accountUsageDecision?.retrievedAtUtc?.toISOString()).toBe('2026-10-09T10:00:01.000Z')
    // The reviver removes only the root's three members; the same names below the root, and a member literally named '', are kept.
    expect(
      JSON.parse('{"nested":{"dispatchedAtUtc":1,"claimedAtUtc":2},"":{"x":3},"dispatchedAtUtc":4,"claimedAtUtc":5,"completedAtUtc":6,"y":7}', withoutRootTimestamps),
    ).toEqual({ nested: { dispatchedAtUtc: 1, claimedAtUtc: 2 }, '': { x: 3 }, y: 7 })
    expect(JSON.parse('[{"dispatchedAtUtc":1}]', withoutRootTimestamps)).toEqual([{ dispatchedAtUtc: 1 }])
  })

  it.each([
    ['plain words', 'yesterday'],
    ['a numeric string', '1'],
    ['a date only', '2026-10-09'],
    ['a text that JavaScript parses', 'Fri Oct 09 2026 10:00:02 GMT+0000'],
    ['a space instead of T', '2026-10-09 10:00:02+00:00'],
    ['lowercase separators', '2026-10-09t10:00:02z'],
    ['surrounding whitespace', ' 2026-10-09T10:00:02Z '],
    ['missing seconds', '2026-10-09T10:00Z'],
    ['no offset', '2026-10-09T10:00:02'],
    ['an offset without a colon', '2026-10-09T10:00:02+0000'],
    ['eight fractional digits', '2026-10-09T10:00:02.12345678Z'],
    ['an empty fraction', '2026-10-09T10:00:02.Z'],
    ['February 30 (JavaScript would roll it into March)', '2026-02-30T00:00:00Z'],
    ['February 29 of a common year', '2025-02-29T00:00:00Z'],
    ['February 29 of a century common year', '1900-02-29T00:00:00Z'],
    ['April 31', '2026-04-31T00:00:00Z'],
    ['month 13', '2026-13-01T00:00:00Z'],
    ['month 00', '2026-00-10T00:00:00Z'],
    ['day 00', '2026-10-00T00:00:00Z'],
    ['hour 24', '2026-10-09T24:00:00Z'],
    ['minute 60', '2026-10-09T10:60:00Z'],
    ['second 60', '2026-10-09T10:00:60Z'],
    ['year 0000', '0000-01-01T00:00:00Z'],
    ['an offset beyond 14 hours', '2026-10-09T10:00:02+14:01'],
    ['offset minutes 60', '2026-10-09T10:00:02+05:60'],
    ['an instant before the DateTimeOffset range', '0001-01-01T00:00:00+01:00'],
    ['an instant after the DateTimeOffset range', '9999-12-31T23:59:59-01:00'],
  ])('rejects %s before revival', async (_label, text) => {
    expect(toWireInstant(text)).toBeNull()
    const answer = await read({ dispatchedAtUtc: text, completedAtUtc: text })

    expect(isInvalidDate(answer.dispatchedAtUtc)).toBe(true)
    expect(isInvalidDate(answer.completedAtUtc)).toBe(true)
  })

  it.each([
    ['UTC as Z', '2026-10-09T10:00:02Z', '2026-10-09T10:00:02.000Z'],
    ['UTC as +00:00', '2026-10-09T10:00:02+00:00', '2026-10-09T10:00:02.000Z'],
    ['one fractional digit', '2026-10-09T10:00:02.5+00:00', '2026-10-09T10:00:02.500Z'],
    ['three fractional digits', '2026-10-09T10:00:02.123Z', '2026-10-09T10:00:02.123Z'],
    ['seven fractional digits, truncated to milliseconds', '2026-10-09T10:00:02.1239999+00:00', '2026-10-09T10:00:02.123Z'],
    ['a positive offset', '2026-10-09T15:30:02+05:30', '2026-10-09T10:00:02.000Z'],
    ['a negative offset across midnight', '2026-10-08T22:00:02-12:00', '2026-10-09T10:00:02.000Z'],
    ['the largest offset', '2026-10-10T00:00:02+14:00', '2026-10-09T10:00:02.000Z'],
    ['a leap day', '2024-02-29T23:59:59.999Z', '2024-02-29T23:59:59.999Z'],
    ['a four-century leap day', '2000-02-29T00:00:00Z', '2000-02-29T00:00:00.000Z'],
    ['the first instant of year 0001', '0001-01-01T00:00:00+00:00', '0001-01-01T00:00:00.000Z'],
    ['the last instant of year 9999', '9999-12-31T23:59:59.9999999+00:00', '9999-12-31T23:59:59.999Z'],
  ])('accepts %s as the exact instant it names', async (_label, text, iso) => {
    const at = toWireInstant(text)
    expect(at?.toISOString()).toBe(iso)

    const answer = await read({ dispatchedAtUtc: text })
    expect(answer.dispatchedAtUtc?.getTime()).toBe(at?.getTime())
  })

  it('keeps missing and null optional timestamps absent and the claim time mandatory', async () => {
    const missing = body()
    delete (missing as Record<string, unknown>).dispatchedAtUtc
    delete (missing as Record<string, unknown>).completedAtUtc
    const absent = await answering(JSON.stringify(missing)).client.getAgentAttemptEvidence('run-1', 'attempt-1')
    expect(absent.dispatchedAtUtc).toBeUndefined()
    expect(absent.completedAtUtc).toBeUndefined()
    expect(toAgentAttemptExecutionFacts(absent).dispatched).toEqual({ status: 'absent' })
    expect(toAgentAttemptExecutionFacts(absent).process).toEqual({ status: 'not-dispatched' })

    const nulls = await read({ dispatchedAtUtc: null, completedAtUtc: null })
    expect(nulls.dispatchedAtUtc).toBeUndefined()
    expect(nulls.completedAtUtc).toBeUndefined()

    // The claim time is required: missing, null or malformed makes the whole answer incoherent for the selected attempt.
    expect(toAgentAttemptEvidence(await read({ claimedAtUtc: null }), EXPECTED)).toBeNull()
    expect(toAgentAttemptEvidence(await read({ claimedAtUtc: 0 }), EXPECTED)).toBeNull()
    expect(toAgentAttemptEvidence(await read({ claimedAtUtc: '2026-02-30T00:00:00Z' }), EXPECTED)).toBeNull()
    expect(toAgentAttemptEvidence(await read({}), EXPECTED)?.status).toBe('success')
  })

  it('makes a malformed end time unavailable without touching the dispatch, process or usage facts', async () => {
    const answer = await read({ completedAtUtc: 20261009 })
    const facts = toAgentAttemptExecutionFacts(answer)

    expect(facts.completed).toEqual({ status: 'unavailable' })
    expect(facts.dispatched.status).toBe('recorded')
    expect(facts.process.status).toBe('known')
    expect(facts.tokenUsage.status).toBe('known')
  })

  it('leaves every other member exactly as the generated client builds it, and keeps the identity binding', async () => {
    const raw = JSON.stringify(body())
    const fetch = vi.fn(() => Promise.resolve(new Response(raw, { status: 200 })))
    const generated = await new GetAgentAttemptEvidenceEndpointClient('http://host.invalid', { fetch }).getAgentAttemptEvidence('run-1', 'attempt-1')
    const answer = await answering(raw).client.getAgentAttemptEvidence('run-1', 'attempt-1')

    expect(answer.constructor).toBe(generated.constructor)
    expect({ ...answer, claimedAtUtc: undefined, dispatchedAtUtc: undefined, completedAtUtc: undefined }).toEqual({
      ...generated,
      claimedAtUtc: undefined,
      dispatchedAtUtc: undefined,
      completedAtUtc: undefined,
    })
    expect(answer.dispatchedAtUtc?.toISOString()).toBe('2026-10-09T10:00:02.500Z')
    expect(toAgentAttemptEvidence(answer, EXPECTED)?.status).toBe('success')
    expect(toAgentAttemptEvidence(answer, { ...EXPECTED, attemptId: 'attempt-2' })).toBeNull()
    expect(toAgentAttemptEvidence(answer, { ...EXPECTED, responseContract: 'ReviewCorrection' })).toBeNull()
  })

  it('sends one bodyless GET to the generated URL and keeps the generated failure handling', async () => {
    const { fetch, client } = answering(JSON.stringify(body()))
    await client.getAgentAttemptEvidence('run 1', 'attempt/1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://host.invalid/api/runs/run%201/agent-attempts/attempt%2F1/evidence')
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()

    for (const status of [404, 401, 500]) {
      const failing = answering('{"title":"refused"}', status)
      const caught = await failing.client.getAgentAttemptEvidence('run-1', 'attempt-1').catch((error: unknown) => error)
      expect(ApiException.isApiException(caught)).toBe(true)
      expect((caught as ApiException).status).toBe(status)
      expect((caught as ApiException).response).toBe('{"title":"refused"}')
      expect(failing.fetch).toHaveBeenCalledTimes(1)
    }
  })

  it('answers an empty or non-object body exactly as the generated client does', async () => {
    for (const text of ['', 'null', '[]', '"text"']) {
      const fetch = vi.fn(() => Promise.resolve(new Response(text, { status: 200 })))
      const generated = await new GetAgentAttemptEvidenceEndpointClient('http://host.invalid', { fetch }).getAgentAttemptEvidence('run-1', 'attempt-1')
      expect(await answering(text).client.getAgentAttemptEvidence('run-1', 'attempt-1')).toEqual(generated)
    }

    const generatedError = await new GetAgentAttemptEvidenceEndpointClient('http://host.invalid', {
      fetch: () => Promise.resolve(new Response('{not json', { status: 200 })),
    })
      .getAgentAttemptEvidence('run-1', 'attempt-1')
      .catch((error: unknown) => error)
    const facadeError = await answering('{not json').client.getAgentAttemptEvidence('run-1', 'attempt-1').catch((error: unknown) => error)
    expect(facadeError).toBeInstanceOf(SyntaxError)
    expect((facadeError as Error).constructor).toBe((generatedError as Error).constructor)
  })
})
