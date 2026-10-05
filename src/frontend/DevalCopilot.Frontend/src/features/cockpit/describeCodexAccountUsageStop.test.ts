import { describe, expect, it } from 'vitest'
import { CodexAccountUsageDecisionResponse, CodexAccountUsageStopResponse, CodexAccountUsageWindowResponse } from '../../api/generated/api-client'
import {
  CODEX_ACCOUNT_USAGE_STOP_MAX,
  CODEX_ACCOUNT_USAGE_STOP_MIN,
  describeAccountUsageDecision,
  describeAttemptAccountUsageStop,
  describeRunAccountUsageStop,
  parseCodexAccountUsageStopDraft,
} from './describeCodexAccountUsageStop'

const stop = (state: string, percent?: number) => new CodexAccountUsageStopResponse({ state, percent })

describe('constants', () => {
  it('mirrors the accepted range', () => {
    expect([CODEX_ACCOUNT_USAGE_STOP_MIN, CODEX_ACCOUNT_USAGE_STOP_MAX]).toEqual([1, 100])
  })
})

describe('describeRunAccountUsageStop', () => {
  it.each([
    [stop('NotConfigured'), 'Not configured'],
    [stop('Configured', 80), '80% used'],
    [stop('Configured', 1), '1% used'],
    [stop('Configured', 100), '100% used'],
    [stop('Configured'), 'Unknown'],
    [stop('Configured', 0), 'Unknown'],
    [stop('Configured', 101), 'Unknown'],
    [stop('Configured', 2.5), 'Unknown'],
    [stop('Unknown'), 'Unknown'],
    [stop('Surprise', 50), 'Unknown'],
    [undefined, 'Unknown'],
    [null, 'Unknown'],
  ])('describes %j as %s', (fact, text) => {
    expect(describeRunAccountUsageStop(fact)).toBe(text)
  })
})

describe('describeAttemptAccountUsageStop', () => {
  it.each([
    [stop('Configured', 80), 'Claimed with account-usage stop: 80% used'],
    [stop('NotConfigured'), 'Claimed with no account-usage stop'],
    [stop('Unknown'), 'Account-usage stop at claim: Unknown'],
    [stop('Configured'), 'Account-usage stop at claim: Unknown'],
    [stop('Configured', 500), 'Account-usage stop at claim: Unknown'],
    [stop('Other', 5), 'Account-usage stop at claim: Unknown'],
    [null, null],
    [undefined, null],
  ])('describes %j as %s', (fact, text) => {
    expect(describeAttemptAccountUsageStop(fact)).toBe(text)
  })
})

describe('parseCodexAccountUsageStopDraft', () => {
  it('treats an empty draft as a clear', () => {
    expect(parseCodexAccountUsageStopDraft('')).toEqual({ kind: 'clear' })
  })

  it.each(['1', '9', '10', '80', '99', '100'])('accepts %j', (draft) => {
    expect(parseCodexAccountUsageStopDraft(draft)).toEqual({ kind: 'set', percent: Number(draft) })
  })

  it.each(['0', '-1', '101', '999', '1000', '007', '08', '3.5', '1e2', '+5', ' 5', '5 ', '  ', 'abc', '５', '1_0'])(
    'rejects %j',
    (draft) => {
      expect(parseCodexAccountUsageStopDraft(draft)).toEqual({ kind: 'invalid' })
    },
  )
})

const window = (bucketId: string | undefined, kind: string, usedPercent: number) =>
  new CodexAccountUsageWindowResponse({ bucketId, window: kind, usedPercent })

const recorded = (reason: string, extra: Partial<CodexAccountUsageDecisionResponse> = {}) =>
  new CodexAccountUsageDecisionResponse({ state: 'Recorded', decision: 'Reached', reason, windows: [], ...extra })

describe('describeAccountUsageDecision', () => {
  it('returns null without a decision', () => {
    expect(describeAccountUsageDecision(null)).toBeNull()
    expect(describeAccountUsageDecision(undefined)).toBeNull()
  })

  it.each([
    [recorded('ThresholdReached', { thresholdPercent: 80 }), 'Not started: a reported usage window reached the configured stop (80%).'],
    [recorded('ProviderReportedLimitReached'), 'Not started: the provider reported that a limit was reached.'],
    [
      recorded('EvidenceUnavailable', { thresholdPercent: 90 }),
      'Not started: account usage could not be read, so the configured stop (90%) could not be checked.',
    ],
    [recorded('EvidenceExpired'), 'Not started: the account-usage reading was no longer current when it was checked.'],
    [recorded('ThresholdUnusable'), 'Not started: the saved stop was not a valid setting.'],
    [new CodexAccountUsageDecisionResponse({ state: 'Unavailable', decision: 'Unavailable', windows: [] }), 'The stored account-usage decision could not be verified.'],
    [new CodexAccountUsageDecisionResponse({ state: 'Mystery', windows: [] }), 'The stored account-usage decision could not be verified.'],
    [recorded('SomethingNew'), 'The stored account-usage decision could not be verified.'],
  ])('headline for %j', (decision, headline) => {
    expect(describeAccountUsageDecision(decision)?.headline).toBe(headline)
  })

  it('lists the host retrieval time first, then one line per window in order', () => {
    const retrievedAtUtc = new Date('2026-10-01T12:30:00Z')
    const result = describeAccountUsageDecision(
      recorded('ThresholdReached', {
        thresholdPercent: 80,
        retrievedAtUtc,
        windows: [window('codex', 'Primary', 85), window(undefined, 'Secondary', 12), window('other', 'Primary', 3)],
      }),
    )

    expect(result?.details).toEqual([
      `Host retrieval time: ${retrievedAtUtc.toLocaleString()}`,
      'codex primary window: 85% used',
      'account secondary window: 12% used',
      'other primary window: 3% used',
    ])
  })

  it('omits the retrieval time when it is missing or invalid', () => {
    expect(describeAccountUsageDecision(recorded('EvidenceExpired'))?.details).toEqual([])
    expect(describeAccountUsageDecision(recorded('EvidenceExpired', { retrievedAtUtc: new Date('nope') }))?.details).toEqual([])
  })

  it('shows no detail lines for an Unavailable decision', () => {
    const result = describeAccountUsageDecision(
      new CodexAccountUsageDecisionResponse({
        state: 'Unavailable',
        retrievedAtUtc: new Date('2026-10-01T12:30:00Z'),
        windows: [window('codex', 'Primary', 85)],
      }),
    )
    expect(result?.details).toEqual([])
  })

  it('never throws on junk', () => {
    const junk = [
      {},
      { state: 'Recorded' },
      { state: 'Recorded', reason: 'ThresholdReached', windows: 'x', retrievedAtUtc: 'not a date' },
      { state: 'Recorded', reason: 'ThresholdReached', windows: [null, 5, {}, { window: 'Primary' }, { window: 'Tertiary', usedPercent: 4 }] },
    ]
    for (const value of junk) {
      expect(() => describeAccountUsageDecision(value as never)).not.toThrow()
    }
    expect(describeAccountUsageDecision(junk[3] as never)?.details).toEqual([])
    expect(describeAccountUsageDecision(junk[1] as never)?.headline).toBe('The stored account-usage decision could not be verified.')
  })
})
