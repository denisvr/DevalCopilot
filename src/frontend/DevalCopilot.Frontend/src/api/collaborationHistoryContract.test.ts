import { describe, expect, it, vi } from 'vitest'
import { toCollaborationHistoryPage } from '../features/cockpit/collaborationHistory'
import { ApiError, ApiException, ApiProblemDetails, GetCollaborationHistoryEndpointClient } from './generated/api-client'

// The protected collaboration-history read goes through the generated client only. It resolves the typed page (headers with revived UTC
// times and exact decimal-string sequences), sends the exclusive string cursor and the limit as query parameters, keeps the fixed
// code of a refusal, and never splices the run into the path or sends a sequence through a number.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, historyClient: new GetCollaborationHistoryEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(typeof body === 'string' ? body : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const header = (sequence: string, extra: Record<string, unknown> = {}) => ({
  runId: 'run-1',
  id: `message-${sequence}`,
  sequence,
  occurredAtUtc: '2026-10-09T10:00:00+00:00',
  protocolVersion: '1.0',
  type: 'Question',
  provenance: 'ProviderObserved',
  actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
  recipient: { kind: 'Orchestrator', role: null, provider: null },
  contentStatus: 'Available',
  summary: 'Which scope?',
  fields: [
    { name: 'question', value: 'Which scope applies?' },
    { name: 'why', value: 'It bounds the work.' },
  ],
  ...extra,
})

const unavailable = { contentStatus: 'Unavailable', summary: null, fields: [] }

describe('GetCollaborationHistoryEndpointClient', () => {
  it('reads one page with a bodyless GET and resolves its typed messages, exact sequences and continuation', async () => {
    const { historyClient, fetch } = answering(
      json({
        runId: 'run-1',
        entries: [
          header('9223372036854775807'),
          header('9007199254740993'),
          header('9007199254740992', { type: 'Unrecognized', actor: { kind: 'Unrecognized', role: null, provider: null }, ...unavailable }),
        ],
        hasMore: true,
        nextBeforeSequence: '9007199254740992',
      }),
    )

    const page = await historyClient.getCollaborationHistory('run-1', '9223372036854775808', 3)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/collaboration-history?beforeSequence=9223372036854775808&limit=3')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(page.runId).toBe('run-1')
    expect(page.entries?.map((entry) => entry.sequence)).toEqual(['9223372036854775807', '9007199254740993', '9007199254740992'])
    expect(page.entries?.every((entry) => typeof entry.sequence === 'string')).toBe(true)
    expect(page.entries?.[0].occurredAtUtc).toBeInstanceOf(Date)
    expect(page.entries?.[0].actor).toMatchObject({ kind: 'Agent', role: 'Planner', provider: 'Codex' })
    expect(page.entries?.[2].type).toBe('Unrecognized')
    expect(page.entries?.[2].contentStatus).toBe('Unavailable')
    expect(page.entries?.[2].summary ?? null).toBeNull()
    expect(page.entries?.[0].fields?.map((field) => [field.name, field.value])).toEqual([['question', 'Which scope applies?'], ['why', 'It bounds the work.']])
    expect(page.hasMore).toBe(true)
    expect(page.nextBeforeSequence).toBe('9007199254740992')
  })

  it('hands the decoder exactly the wire values, so Int64 neighbors that Number cannot tell apart stay distinct', async () => {
    const wire = {
      runId: 'run-1',
      entries: [header('9223372036854775807'), header('9007199254740993'), header('9007199254740992'), header('9007199254740991')],
      hasMore: false,
      nextBeforeSequence: null,
    }
    const { historyClient } = answering(json(wire))

    const response = await historyClient.getCollaborationHistory('run-1', null, 20)
    const page = toCollaborationHistoryPage(response, { runId: 'run-1', beforeSequence: null, limit: 20 })

    expect(page?.entries.map((entry) => entry.sequence)).toEqual(wire.entries.map((entry) => entry.sequence))
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
    expect(page?.entries[1].sequence).not.toBe(page?.entries[2].sequence)
  })

  it('omits an absent cursor and limit and resolves the end of history without a next cursor', async () => {
    const { historyClient, fetch } = answering(json({ runId: 'run-1', entries: [], hasMore: false, nextBeforeSequence: null }))

    const page = await historyClient.getCollaborationHistory('run-1', null, undefined)

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/runs/run-1/collaboration-history')
    expect(page.entries).toEqual([])
    expect(page.hasMore).toBe(false)
    expect(page.nextBeforeSequence ?? null).toBeNull()
  })

  it('rejects an undeclared status as an ApiException that keeps its response, never as a page', async () => {
    const { historyClient } = answering(json({ errors: [{ code: 'host.unavailable', detail: 'fixed' }] }, 503))

    const refusal = await historyClient.getCollaborationHistory('run-1', null, null).then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(503)
    expect((refusal as ApiException).response).toContain('host.unavailable')
  })

  it.each([
    [
      400,
      'urn:devalente:problem:validation',
      [
        ['validation.invalid_limit', '#/limit'],
        ['validation.invalid_cursor', '#/beforeSequence'],
      ],
    ],
    [404, 'urn:devalente:problem:not-found', [['runs.not_found', null]]],
    [409, 'urn:devalente:problem:conflict', [['collaboration_history.unavailable', null]]],
  ])('rejects a %i as the typed shared problem with its structured errors', async (status, type, errors) => {
    const { historyClient } = answering(
      json(
        {
          type,
          title: 'fixed',
          status,
          detail: 'See the errors property for details.',
          traceId: '00-abc-def-00',
          errors: errors.map(([code, pointer]) => ({ code, detail: 'fixed', pointer, parameters: null })),
        },
        status,
      ),
    )

    const refusal = await historyClient.getCollaborationHistory('run-1', null, null).then(
      () => null,
      (caught: unknown) => caught as ApiProblemDetails,
    )

    const problem = refusal as ApiProblemDetails
    expect(problem).toBeInstanceOf(ApiProblemDetails)
    expect(problem.status).toBe(status)
    expect(problem.type).toBe(type)
    expect(problem.traceId).toBe('00-abc-def-00')
    expect(problem.errors?.[0]).toBeInstanceOf(ApiError)
    expect(problem.errors?.map((error) => [error.code, error.pointer ?? null])).toEqual(errors)
  })

  it('encodes the run identifier and the cursor instead of splicing them into the request', async () => {
    const { historyClient, fetch } = answering(json({ runId: 'x', entries: [], hasMore: false }))

    await historyClient.getCollaborationHistory('r/../x?y=1', '1&limit=50', null)

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/runs/r%2F..%2Fx%3Fy%3D1/collaboration-history?beforeSequence=1%26limit%3D50')
  })
})
