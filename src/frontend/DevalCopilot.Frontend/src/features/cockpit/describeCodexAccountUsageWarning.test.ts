import { describe, expect, it } from 'vitest'
import {
  CodexAccountUsageWarningSettingResponse,
  CodexAccountUsageWarningWindowResponse,
  GetCodexAccountUsageWarningResponse,
} from '../../api/generated/api-client'
import {
  configuredWarningPercent,
  describeCodexAccountUsageWarningCheck,
  describeRunAccountUsageWarning,
  parseCodexAccountUsageWarningDraft,
} from './describeCodexAccountUsageWarning'

const OBSERVED = new Date('2026-10-05T12:00:00.000Z')

const window = (used: number, reached: boolean, kind = 'Primary', bucketId: string | null = 'codex') =>
  new CodexAccountUsageWarningWindowResponse({ bucketId: bucketId ?? undefined, window: kind, usedPercent: used, reachedThreshold: reached })

function result(init: Partial<GetCodexAccountUsageWarningResponse>) {
  return new GetCodexAccountUsageWarningResponse({
    state: 'Below',
    thresholdPercent: 80,
    observedAtUtc: OBSERVED,
    windows: [window(10, false)],
    providerReportedLimitReached: false,
    ...init,
  })
}

describe('saved warning text', () => {
  it.each([
    [{ state: 'NotConfigured' }, 'Not configured'],
    [{ state: 'Configured', percent: 80 }, '80% used'],
    [{ state: 'Configured', percent: 0 }, 'Unknown'],
    [{ state: 'Configured', percent: 101 }, 'Unknown'],
    [{ state: 'Configured', percent: 3.5 }, 'Unknown'],
    [{ state: 'Configured' }, 'Unknown'],
    [{ state: 'Unknown' }, 'Unknown'],
    [{ state: 'surprise' }, 'Unknown'],
  ])('describes %j as %j', (init, text) => {
    expect(describeRunAccountUsageWarning(new CodexAccountUsageWarningSettingResponse(init))).toBe(text)
  })

  it('treats a missing projection as Unknown and exposes a percent only for a valid Configured setting', () => {
    expect(describeRunAccountUsageWarning(undefined)).toBe('Unknown')
    expect(configuredWarningPercent(null)).toBeNull()
    expect(configuredWarningPercent(new CodexAccountUsageWarningSettingResponse({ state: 'Configured', percent: 100 }))).toBe(100)
    expect(configuredWarningPercent(new CodexAccountUsageWarningSettingResponse({ state: 'Unknown', percent: 50 }))).toBeNull()
    expect(configuredWarningPercent(new CodexAccountUsageWarningSettingResponse({ state: 'Configured', percent: 0 }))).toBeNull()
  })
})

describe('draft parsing', () => {
  it.each([
    ['', { kind: 'clear' }],
    ['1', { kind: 'set', percent: 1 }],
    ['100', { kind: 'set', percent: 100 }],
    ['0', { kind: 'invalid' }],
    ['101', { kind: 'invalid' }],
    ['007', { kind: 'invalid' }],
    ['3.5', { kind: 'invalid' }],
    ['1e2', { kind: 'invalid' }],
    ['+5', { kind: 'invalid' }],
    [' 5', { kind: 'invalid' }],
    ['٥', { kind: 'invalid' }],
  ])('parses %j as %j', (draft, expected) => {
    expect(parseCodexAccountUsageWarningDraft(draft)).toEqual(expected)
  })
})

describe('check classification', () => {
  it('shows a dated Below result with every window and never an eligibility or capacity claim', () => {
    const view = describeCodexAccountUsageWarningCheck(
      result({ windows: [window(10, false), window(20, false, 'Secondary')] }),
      80,
    )

    expect(view.kind).toBe('below')
    const text = JSON.stringify(view)
    expect(view.kind === 'below' && view.observedAt).toBeTruthy()
    expect(view.kind === 'below' && view.details).toEqual([
      'codex primary window: 10% used',
      'codex secondary window: 20% used',
    ])
    expect(text).not.toMatch(/eligible|ready|remaining|capacity available|live usage|safe to/i)
  })

  it('shows a Reached result at equality, marking the window that reached it', () => {
    const view = describeCodexAccountUsageWarningCheck(
      result({ state: 'Reached', reason: 'ThresholdReached', windows: [window(80, true), window(5, false, 'Secondary', null)] }),
      80,
    )

    expect(view.kind).toBe('reached')
    expect(view.kind === 'reached' && view.details).toEqual([
      'codex primary window: 80% used (warning reached)',
      'account secondary window: 5% used',
    ])
  })

  it('shows a provider-reported reached state even when no window reached the percentage', () => {
    const view = describeCodexAccountUsageWarningCheck(
      result({ state: 'Reached', reason: 'ProviderReportedLimitReached', providerReportedLimitReached: true }),
      80,
    )

    expect(view.kind).toBe('reached')
    expect(view.headline).toBe('The provider reported that a usage limit had been reached when the Codex account was observed.')
  })

  it.each([
    ['EvidenceExpired', 'The Codex account observation was no longer current, so the warning could not be checked.'],
    ['ConfigurationChanged', 'The saved warning or the Codex launch target changed during the check. Check again.'],
    ['EvidenceUnavailable', 'Codex account usage could not be read, so the warning could not be checked.'],
  ])('reports Unavailable (%s) with fixed copy and no classification', (reason, headline) => {
    expect(describeCodexAccountUsageWarningCheck(result({ state: 'Unavailable', reason, windows: [] }), 80)).toEqual({
      kind: 'unavailable',
      headline,
    })
  })

  it.each([
    ['a different saved threshold', result({ thresholdPercent: 50 })],
    ['a missing date', result({ observedAtUtc: undefined })],
    ['an invalid date', result({ observedAtUtc: new Date('nope') })],
    ['no windows', result({ windows: [] })],
    ['a window with a bad percent', result({ windows: [window(101, false)] })],
    ['a window with a fractional percent', result({ windows: [window(5.5, false)] })],
    ['an unknown window kind', result({ windows: [window(5, false, 'Tertiary')] })],
    ['an unknown state', result({ state: 'Eligible' })],
    ['a Below that has a reached window', result({ windows: [window(90, true)] })],
    ['a Below that has a window at the threshold though unflagged', result({ windows: [window(80, false)] })],
    ['a Below with a provider reached state', result({ providerReportedLimitReached: true })],
    ['a Reached with nothing reached', result({ state: 'Reached' })],
  ])('never classifies %s', (_name, response) => {
    expect(describeCodexAccountUsageWarningCheck(response, 80).kind).toBe('unavailable')
  })

  it.each([[null], [undefined]])('never classifies %s', (response) => {
    expect(describeCodexAccountUsageWarningCheck(response, 80).kind).toBe('unavailable')
  })

  it('treats a changed or cleared setting reported by the server as not verifiable', () => {
    expect(describeCodexAccountUsageWarningCheck(result({ state: 'NotConfigured' }), 80).kind).toBe('unavailable')
    expect(describeCodexAccountUsageWarningCheck(result({ state: 'SettingInvalid' }), 80).kind).toBe('unavailable')
  })
})
