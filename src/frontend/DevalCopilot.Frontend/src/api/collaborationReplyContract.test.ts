import { describe, expect, it, vi } from 'vitest'
import { toCollaborationReply } from '../features/cockpit/collaborationReply'
import { ApiException, ApiProblemDetails, GetCollaborationHistoryReplyEndpointClient } from './generated/api-client'

// The protected reply inspection goes through the generated client only: a bodyless GET with both identifiers encoded into the path (no
// query, no body), exact string sequences end to end, typed 404 and 409 problems that keep their fixed codes, and a parent that the
// decoder accepts only against the observed source.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, replyClient: new GetCollaborationHistoryReplyEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const parent = {
  runId: 'run-1',
  id: 'message-1',
  sequence: '9007199254740992',
  occurredAtUtc: '2026-10-09T10:00:00+00:00',
  protocolVersion: '1.0',
  type: 'Proposal',
  provenance: 'ProviderObserved',
  actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
  recipient: { kind: 'Orchestrator', role: null, provider: null },
  contentStatus: 'Unavailable',
  summary: null,
  fields: [],
}

describe('GetCollaborationHistoryReplyEndpointClient', () => {
  it('reads one reply with a bodyless GET addressed only by its path and resolves the exact sequences', async () => {
    const { replyClient, fetch } = answering(
      json({ runId: 'run-1', messageId: 'message-2', sourceSequence: '9007199254740993', status: 'Available', parent }),
    )

    const reply = await replyClient.getCollaborationHistoryReply('run-1', 'message-2')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/collaboration-history/message-2/reply')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(reply.sourceSequence).toBe('9007199254740993')
    expect(reply.parent?.sequence).toBe('9007199254740992')
    expect(reply.parent?.occurredAtUtc).toBeInstanceOf(Date)
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
    expect(
      toCollaborationReply(reply, {
        runId: 'run-1',
        messageId: 'message-2',
        sequence: '9007199254740993',
        type: 'Acceptance',
        protocolVersion: '1.0',
      }),
    ).toMatchObject({ status: 'Available', parent: { sequence: '9007199254740992' } })
  })

  it('resolves None and Unavailable without a parent', async () => {
    for (const status of ['None', 'Unavailable']) {
      const { replyClient } = answering(json({ runId: 'run-1', messageId: 'message-2', sourceSequence: '5', status, parent: null }))

      const reply = await replyClient.getCollaborationHistoryReply('run-1', 'message-2')

      expect(reply.status).toBe(status)
      expect(reply.parent ?? null).toBeNull()
    }
  })

  it('encodes both identifiers instead of splicing them into the request', async () => {
    const { replyClient, fetch } = answering(json({ runId: 'x', messageId: 'y', sourceSequence: '1', status: 'None' }))

    await replyClient.getCollaborationHistoryReply('r/../x?y=1', 'm/../z#f')

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/runs/r%2F..%2Fx%3Fy%3D1/collaboration-history/m%2F..%2Fz%23f/reply')
  })

  it.each([
    [404, 'urn:devalente:problem:not-found', 'collaboration_reply.not_found'],
    [409, 'urn:devalente:problem:conflict', 'collaboration_reply.unavailable'],
  ])('rejects a %i as the typed shared problem with its fixed code', async (status, type, code) => {
    const { replyClient } = answering(
      json(
        {
          type,
          title: 'fixed',
          status,
          detail: 'See the errors property for details.',
          traceId: '00-abc-def-00',
          errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
        },
        status,
      ),
    )

    const refusal = await replyClient.getCollaborationHistoryReply('run-1', 'message-2').then(
      () => null,
      (caught: unknown) => caught as ApiProblemDetails,
    )

    expect(refusal).toBeInstanceOf(ApiProblemDetails)
    expect((refusal as ApiProblemDetails).status).toBe(status)
    expect((refusal as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
  })

  it('rejects an undeclared status as an ApiException, never as a reply', async () => {
    const { replyClient } = answering(json({ errors: [{ code: 'host.unavailable' }] }, 503))

    const refusal = await replyClient.getCollaborationHistoryReply('run-1', 'message-2').then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(503)
  })
})
