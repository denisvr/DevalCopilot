import { describe, expect, it, vi } from 'vitest'
import {
  ApiException,
  GetCodexAccountUsageWarningEndpointClient,
  GetRunCockpitResponse,
  SetCodexAccountUsageWarningEndpointClient,
  SetCodexAccountUsageWarningRequest,
} from './generated/api-client'

// The host answers the warning operations with the exact wire shapes its API tests assert. The generated client is the only way the
// cockpit speaks to them, so it must send the strict one-member body and a bodiless GET to the one route, and read the answers into
// the typed values the cockpit derives its states from.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, setClient: new SetCodexAccountUsageWarningEndpointClient('http://host.invalid', { fetch }), getClient: new GetCodexAccountUsageWarningEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('SetCodexAccountUsageWarningEndpointClient', () => {
  it.each([
    [80, '{"percent":80}'],
    [null, '{"percent":null}'],
  ])('POSTs the strict one-member body for %j to the one route', async (percent, body) => {
    const { setClient, fetch } = answering(json({ percent }))

    const accepted = await setClient.setCodexAccountUsageWarning('run-1', SetCodexAccountUsageWarningRequest.fromJS({ percent }))

    expect(accepted.percent ?? null).toBe(percent)
    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/codex-account-usage-warning')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(body)
  })

  it('rejects a refusal as an API exception, never as an acceptance', async () => {
    const { setClient } = answering(json({ errors: [{ code: 'codex_account_usage_warning.run_not_editable' }] }, 422))

    const refusal = await setClient.setCodexAccountUsageWarning('run-1', SetCodexAccountUsageWarningRequest.fromJS({ percent: 5 })).then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(422)
  })
})

describe('GetCodexAccountUsageWarningEndpointClient', () => {
  const wire = {
    state: 'Reached',
    reason: 'ThresholdReached',
    thresholdPercent: 80,
    observedAtUtc: '2026-10-05T12:00:00.0000000+00:00',
    windows: [
      { bucketId: 'codex', window: 'Primary', usedPercent: 80, reachedThreshold: true },
      { bucketId: null, window: 'Secondary', usedPercent: 5, reachedThreshold: false },
    ],
    providerReportedLimitReached: false,
  }

  it('GETs the one route without a body and reads the host answer into typed values', async () => {
    const { getClient, fetch } = answering(json(wire))

    const answer = await getClient.getCodexAccountUsageWarning('run-1')

    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/codex-account-usage-warning')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(answer.state).toBe('Reached')
    expect(answer.reason).toBe('ThresholdReached')
    expect(answer.thresholdPercent).toBe(80)
    expect(answer.observedAtUtc).toEqual(new Date('2026-10-05T12:00:00.000Z'))
    expect(answer.providerReportedLimitReached).toBe(false)
    expect(answer.windows?.map((window) => [window.bucketId ?? null, window.window, window.usedPercent, window.reachedThreshold])).toEqual([
      ['codex', 'Primary', 80, true],
      [null, 'Secondary', 5, false],
    ])
  })

  it('reads an unavailable answer without a date or windows', async () => {
    const { getClient } = answering(
      json({ state: 'Unavailable', reason: 'EvidenceUnavailable', thresholdPercent: 80, observedAtUtc: null, windows: [], providerReportedLimitReached: false }),
    )

    const answer = await getClient.getCodexAccountUsageWarning('run-1')

    expect(answer.state).toBe('Unavailable')
    expect(answer.observedAtUtc).toBeUndefined()
    expect(answer.windows).toEqual([])
  })
})

describe('the cockpit projection of the saved warning', () => {
  it('reads the separate saved-setting fact beside the stop, and a missing fact stays absent', () => {
    const withBoth = GetRunCockpitResponse.fromJS({
      codexAccountUsageStop: { state: 'Configured', percent: 95 },
      codexAccountUsageWarning: { state: 'Configured', percent: 80 },
    })
    const without = GetRunCockpitResponse.fromJS({})

    expect(withBoth.codexAccountUsageWarning?.state).toBe('Configured')
    expect(withBoth.codexAccountUsageWarning?.percent).toBe(80)
    expect(withBoth.codexAccountUsageStop?.percent).toBe(95)
    expect(without.codexAccountUsageWarning).toBeUndefined()
  })
})
