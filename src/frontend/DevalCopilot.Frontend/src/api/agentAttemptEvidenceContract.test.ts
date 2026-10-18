import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentAttemptEvidenceWireClient } from '../features/cockpit/agentAttemptEvidenceWire'
import { agentAttemptEvidenceClient } from './clients'
import { ApiException, GetAgentAttemptEvidenceEndpointClient } from './generated/api-client'

// The factory the cockpit uses for the attempt evidence read: the timestamp-judging facade over the generated operation, wired to the
// launch session's base URL and the authenticated transport exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-secret' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('agentAttemptEvidenceClient', () => {
  it('is the facade over the generated operation and sends one authenticated GET to the session host', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(
      answer(
        JSON.stringify({
          identityValid: true,
          attemptId: 'attempt-1',
          attemptNumber: 1,
          claimedAtUtc: '2026-10-09T10:00:00+00:00',
          dispatchedAtUtc: 1,
          artifacts: [],
        }),
        200,
      ),
    )
    const client = agentAttemptEvidenceClient()

    expect(client).toBeInstanceOf(AgentAttemptEvidenceWireClient)
    expect(client).toBeInstanceOf(GetAgentAttemptEvidenceEndpointClient)
    const evidence = await client.getAgentAttemptEvidence('run-1', 'attempt-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5099/api/runs/run-1/agent-attempts/attempt-1/evidence')
    expect(init?.method).toBe('GET')
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-secret')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('not-a-real-secret')
    expect(evidence.claimedAtUtc?.toISOString()).toBe('2026-10-09T10:00:00.000Z')
    expect(Number.isNaN(evidence.dispatchedAtUtc?.getTime())).toBe(true)
  })

  it.each([404, 401, 500])('rejects a %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{"errors":[{"code":"agent_attempts.not_found"}]}', status))

    const caught = await agentAttemptEvidenceClient()
      .getAgentAttemptEvidence('run-1', 'attempt-1')
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
