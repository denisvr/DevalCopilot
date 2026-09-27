import { describe, expect, it } from 'vitest'
import { describeCodexAccountAllowance, describeCodexAccountAllowanceRetrievedAt } from './describeCodexAccountAllowance'

describe('describeCodexAccountAllowance', () => {
  it('renders an explicit Unknown for a null snapshot', () => {
    expect(describeCodexAccountAllowance(null)).toEqual(['Codex account usage: Unknown'])
  })

  it('renders an explicit Unknown for an Unknown status, never a zero-valued window', () => {
    expect(
      describeCodexAccountAllowance({
        status: 'Unknown',
        buckets: [{ primary: { usedPercent: 0, windowDurationMins: 300, resetsAtUtc: new Date() } }],
      }),
    ).toEqual(['Codex account usage: Unknown'])
  })

  it('renders an explicit Unknown when Observed carries no buckets', () => {
    expect(describeCodexAccountAllowance({ status: 'Observed', buckets: [] })).toEqual(['Codex account usage: Unknown'])
  })

  it('renders one line per bucket, without an invented aggregate across buckets', () => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [
        { limitId: 'codex', primary: { usedPercent: 42, windowDurationMins: 300, resetsAtUtc: new Date() } },
        { limitId: 'gpt-5', primary: { usedPercent: 7, windowDurationMins: 300, resetsAtUtc: new Date() } },
      ],
    })

    expect(lines).toHaveLength(2)
    expect(lines[0]).toContain('codex')
    expect(lines[0]).toContain('42%')
    expect(lines[1]).toContain('gpt-5')
    expect(lines[1]).toContain('7%')
  })

  it('labels the single unlabeled legacy bucket without a limit id', () => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [{ primary: { usedPercent: 5, windowDurationMins: 300, resetsAtUtc: new Date() } }],
    })

    expect(lines[0]).toMatch(/^Codex —/)
  })

  it('renders both windows of one bucket', () => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [
        {
          primary: { usedPercent: 42, windowDurationMins: 300, resetsAtUtc: new Date() },
          secondary: { usedPercent: 10, windowDurationMins: 10080, resetsAtUtc: new Date() },
        },
      ],
    })

    expect(lines[0]).toContain('Primary: 42% used; 5h window; resets ')
    expect(lines[0]).toContain('Secondary: 10% used; 168h window; resets ')
  })

  it('renders Unknown for one window the bucket did not carry, even while Observed overall', () => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [{ primary: { usedPercent: 42, windowDurationMins: 300, resetsAtUtc: new Date() } }],
    })

    expect(lines[0]).toContain('Primary: 42%')
    expect(lines[0]).toContain('Secondary: Unknown')
  })

  it('renders Unknown for a missing windowDurationMins without rejecting the whole window', () => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [{ primary: { usedPercent: 42 } }],
    })

    expect(lines[0]).toContain('42%')
    expect(lines[0]).toContain('window: Unknown')
    expect(lines[0]).toContain('reset: Unknown')
  })

  it.each([-1, 101, Number.NaN])('rejects an out-of-range usedPercent (%s) rather than clamping it', (usedPercent) => {
    const lines = describeCodexAccountAllowance({
      status: 'Observed',
      buckets: [{ primary: { usedPercent, windowDurationMins: 300, resetsAtUtc: new Date() } }],
    })

    expect(lines[0]).toContain('Primary: Unknown')
    expect(lines[0]).not.toContain('100%')
    expect(lines[0]).not.toContain('0%')
  })
})

describe('describeCodexAccountAllowanceRetrievedAt', () => {
  it('returns null when the snapshot is Unknown', () => {
    expect(describeCodexAccountAllowanceRetrievedAt({ status: 'Unknown' })).toBeNull()
  })

  it('returns null when the snapshot has no retrieval time', () => {
    expect(describeCodexAccountAllowanceRetrievedAt({ status: 'Observed' })).toBeNull()
  })

  it('formats a real retrieval time for an Observed snapshot', () => {
    const result = describeCodexAccountAllowanceRetrievedAt({ status: 'Observed', retrievedAtUtc: '2026-09-27T12:00:00Z' })
    expect(result).toMatch(/^Retrieved /)
  })
})
