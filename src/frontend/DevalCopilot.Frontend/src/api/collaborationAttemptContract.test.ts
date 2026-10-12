import { describe, expect, it, vi } from 'vitest'
import { toCollaborationAttempt } from '../features/cockpit/collaborationAttempt'
import { ApiException, ApiProblemDetails, GetCollaborationHistoryAttemptEndpointClient } from './generated/api-client'

// The protected attempt attribution goes through the generated client only: a bodyless GET with both identifiers encoded into the path (no
// query, no body), exact string sequences end to end, revived UTC times, typed 404 and 409 problems that keep their fixed codes, and a
// header that the decoder accepts only against the observed source.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, attemptClient: new GetCollaborationHistoryAttemptEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const header = {
  id: 'attempt-9',
  attemptNumber: 4,
  role: 'Implementer',
  provider: 'ClaudeCode',
  responseContract: 'ImplementationReport',
  status: 'Completed',
  claimedAtUtc: '2026-10-12T09:00:00+00:00',
  dispatchedAtUtc: '2026-10-12T09:01:02.5+00:00',
  completedAtUtc: null,
}

describe('GetCollaborationHistoryAttemptEndpointClient', () => {
  it('reads one attribution with a bodyless GET addressed only by its path and resolves exact sequences and revived times', async () => {
    const { attemptClient, fetch } = answering(
      json({ runId: 'run-1', messageId: 'message-2', sourceSequence: '9007199254740993', status: 'Available', attempt: header }),
    )

    const answer = await attemptClient.getCollaborationHistoryAttempt('run-1', 'message-2')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/collaboration-history/message-2/attempt')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(answer.sourceSequence).toBe('9007199254740993')
    expect(answer.attempt?.claimedAtUtc).toBeInstanceOf(Date)
    expect(answer.attempt?.dispatchedAtUtc).toBeInstanceOf(Date)
    expect(answer.attempt?.completedAtUtc ?? null).toBeNull()
    expect(Number('9007199254740993')).toBe(Number('9007199254740992'))
    expect(
      toCollaborationAttempt(answer, {
        runId: 'run-1',
        messageId: 'message-2',
        sequence: '9007199254740993',
        type: 'ExecutionReport',
        protocolVersion: '1.0',
        provenance: 'ProviderObserved',
        actor: { kind: 'Agent', role: 'Implementer', provider: 'ClaudeCode' },
      }),
    ).toMatchObject({ status: 'Available', attempt: { id: 'attempt-9', attemptNumber: 4, completedAtUtc: null } })
  })

  it('resolves NoAgentAttempt and Unavailable without an attempt', async () => {
    for (const status of ['NoAgentAttempt', 'Unavailable']) {
      const { attemptClient } = answering(json({ runId: 'run-1', messageId: 'message-2', sourceSequence: '5', status, attempt: null }))

      const answer = await attemptClient.getCollaborationHistoryAttempt('run-1', 'message-2')

      expect(answer.status).toBe(status)
      expect(answer.attempt ?? null).toBeNull()
    }
  })

  it('encodes both identifiers instead of splicing them into the request', async () => {
    const { attemptClient, fetch } = answering(json({ runId: 'x', messageId: 'y', sourceSequence: '1', status: 'Unavailable' }))

    await attemptClient.getCollaborationHistoryAttempt('r/../x?y=1', 'm/../z#f')

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/runs/r%2F..%2Fx%3Fy%3D1/collaboration-history/m%2F..%2Fz%23f/attempt')
  })

  it.each([
    [404, 'urn:devalente:problem:not-found', 'collaboration_attempt.not_found'],
    [409, 'urn:devalente:problem:conflict', 'collaboration_attempt.unavailable'],
  ])('rejects a %i as the typed shared problem with its fixed code', async (status, type, code) => {
    const { attemptClient } = answering(
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

    const refusal = await attemptClient.getCollaborationHistoryAttempt('run-1', 'message-2').then(
      () => null,
      (caught: unknown) => caught as ApiProblemDetails,
    )

    expect(refusal).toBeInstanceOf(ApiProblemDetails)
    expect((refusal as ApiProblemDetails).status).toBe(status)
    expect((refusal as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
  })

  it('rejects an undeclared status as an ApiException, never as an attribution', async () => {
    const { attemptClient } = answering(json({ errors: [{ code: 'host.unavailable' }] }, 503))

    const refusal = await attemptClient.getCollaborationHistoryAttempt('run-1', 'message-2').then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(503)
  })
})
