import { describe, expect, it } from 'vitest'
import { describeRunTokenUsage, describeTokenUsage, formatTokenCount, hasKnownTokenUsage, hasTrustedTokenUsage } from './describeTokenUsage'

const terminal = { dispatched: true, running: false }

describe('describeTokenUsage', () => {
  it('describes known usage with input, output, and the cache breakdown', () => {
    expect(
      describeTokenUsage(
        { inputTokens: 1200, outputTokens: 345, cacheCreationInputTokens: 67, cacheReadInputTokens: 890 },
        terminal,
      ),
    ).toBe('Tokens: 1,200 input · 345 output · 67 cache write · 890 cache read')
  })

  it('omits a cache breakdown the provider did not report instead of inventing zero', () => {
    expect(describeTokenUsage({ inputTokens: 500, outputTokens: 60 }, terminal)).toBe('Tokens: 500 input · 60 output')
  })

  it('describes reported zero usage as zero rather than unknown', () => {
    expect(
      describeTokenUsage({ inputTokens: 0, outputTokens: 0, cacheCreationInputTokens: 0, cacheReadInputTokens: 0 }, terminal),
    ).toBe('Tokens: 0 input · 0 output · 0 cache write · 0 cache read')
  })

  it('states unknown usage truthfully for every absent state', () => {
    expect(describeTokenUsage({}, terminal)).toBe('Token usage unknown')
    expect(describeTokenUsage(undefined, terminal)).toBe('Token usage unknown')
    expect(describeTokenUsage({}, { dispatched: true, running: true })).toBe('Token usage not yet recorded')
    expect(describeTokenUsage({}, { dispatched: false, running: false })).toBe('Token usage: none (provider not invoked)')
  })

  it('treats a partially populated object as unknown rather than partially trusting it', () => {
    expect(hasKnownTokenUsage({ inputTokens: 10 })).toBe(false)
    expect(describeTokenUsage({ inputTokens: 10, cacheReadInputTokens: 5 }, terminal)).toBe('Token usage unknown')
  })

  it('formats counts deterministically', () => {
    expect(formatTokenCount(1234567)).toBe('1,234,567')
    expect(formatTokenCount(-3)).toBe('0')
  })

  // The ordering bug this pass fixes: status must gate trustworthiness BEFORE the object's own
  // shape is consulted, so a malformed/tampered object that already looks like known usage never
  // slips through just because a naive shape check ran first.
  it('never trusts a well-formed-looking usage object while the attempt is still running', () => {
    const tamperedButShapedLikeKnownUsage = { inputTokens: 1200, outputTokens: 345 }

    expect(hasKnownTokenUsage(tamperedButShapedLikeKnownUsage)).toBe(true)
    expect(describeTokenUsage(tamperedButShapedLikeKnownUsage, { dispatched: true, running: true })).toBe(
      'Token usage not yet recorded',
    )
    expect(hasTrustedTokenUsage(tamperedButShapedLikeKnownUsage, { dispatched: true, running: true })).toBe(false)
  })

  it('never trusts a well-formed-looking usage object for an attempt that was never dispatched', () => {
    const tamperedButShapedLikeKnownUsage = { inputTokens: 2400, outputTokens: 120 }

    expect(hasKnownTokenUsage(tamperedButShapedLikeKnownUsage)).toBe(true)
    expect(describeTokenUsage(tamperedButShapedLikeKnownUsage, { dispatched: false, running: false })).toBe(
      'Token usage: none (provider not invoked)',
    )
    expect(hasTrustedTokenUsage(tamperedButShapedLikeKnownUsage, { dispatched: false, running: false })).toBe(false)
  })

  it('trusts a well-formed usage object once the attempt is dispatched and terminal', () => {
    const usage = { inputTokens: 1200, outputTokens: 345 }

    expect(hasTrustedTokenUsage(usage, terminal)).toBe(true)
  })
})

describe('describeRunTokenUsage', () => {
  it('labels only a complete summary as the run total', () => {
    const text = describeRunTokenUsage({
      completeness: 'Complete',
      attemptsWithKnownUsage: 2,
      attemptsWithUnknownUsage: 0,
      inputTokens: 2000,
      outputTokens: 400,
      cacheCreationInputTokens: 70,
      cacheReadInputTokens: 1000,
    })

    expect(text).toBe(
      'Run token total: 2,000 input · 400 output · 70 cache write · 1,000 cache read (all 2 dispatched attempts reported usage)',
    )
  })

  it('never labels a partial sum as a total and names the terminal-unknown gap explicitly (scenario b: terminal-unknown-only)', () => {
    const text = describeRunTokenUsage({
      completeness: 'Partial',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 2,
      pendingAttemptCount: 0,
      terminalAttemptsWithUnknownUsage: 2,
      inputTokens: 1200,
      outputTokens: 345,
    })

    expect(text).toBe(
      'Partial token count: 1,200 input · 345 output from 1 of 3 dispatched attempts'
        + ' — 2 attempts concluded without usable token-usage evidence, so this is not the run total',
    )
    expect(text).not.toMatch(/total:/i)
    expect(text).not.toMatch(/still running/)
  })

  it('shows no numeric token count when zero attempts have known usage yet, even though the sums default to zero', () => {
    const text = describeRunTokenUsage({
      completeness: 'Partial',
      attemptsWithKnownUsage: 0,
      attemptsWithUnknownUsage: 1,
      pendingAttemptCount: 0,
      terminalAttemptsWithUnknownUsage: 1,
      inputTokens: 0,
      outputTokens: 0,
    })

    expect(text).toBe(
      'No token usage recorded yet — 1 attempt concluded without usable token-usage evidence, so there is no partial count to show',
    )
    expect(text).not.toMatch(/0 input/)
    expect(text).not.toMatch(/total:/i)
  })

  it('names both a terminal-unknown gap and a still-running gap together (scenario d: the full three-way mix)', () => {
    const text = describeRunTokenUsage({
      completeness: 'Partial',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 3,
      pendingAttemptCount: 1,
      terminalAttemptsWithUnknownUsage: 2,
      inputTokens: 1200,
      outputTokens: 345,
    })

    expect(text).toBe(
      'Partial token count: 1,200 input · 345 output from 1 of 4 dispatched attempts'
        + ' — 2 attempts concluded without usable token-usage evidence and 1 attempt still running, so this is not the run total',
    )
  })

  it('shows a real reported zero explicitly when at least one attempt has known usage (scenario e)', () => {
    const text = describeRunTokenUsage({
      completeness: 'Partial',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 1,
      pendingAttemptCount: 0,
      terminalAttemptsWithUnknownUsage: 1,
      inputTokens: 0,
      outputTokens: 0,
    })

    expect(text).toBe(
      'Partial token count: 0 input · 0 output from 1 of 2 dispatched attempts'
        + ' — 1 attempt concluded without usable token-usage evidence, so this is not the run total',
    )
  })

  it('names a known-plus-pending mix without a terminal-unknown gap (scenario c)', () => {
    const text = describeRunTokenUsage({
      completeness: 'PendingEvidence',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 2,
      pendingAttemptCount: 2,
      terminalAttemptsWithUnknownUsage: 0,
      inputTokens: 1200,
      outputTokens: 345,
    })

    expect(text).toBe(
      'Partial token count so far: 1,200 input · 345 output from 1 of 3 dispatched attempts'
        + ' — 2 attempts still running, so this is not the run total yet',
    )
  })

  it('shows no numeric token count for an all-pending gap with zero known usage yet (scenario a: pending-only)', () => {
    const text = describeRunTokenUsage({
      completeness: 'PendingEvidence',
      attemptsWithKnownUsage: 0,
      attemptsWithUnknownUsage: 1,
      pendingAttemptCount: 1,
      terminalAttemptsWithUnknownUsage: 0,
      inputTokens: 0,
      outputTokens: 0,
    })

    expect(text).toBe('No token usage recorded yet — 1 attempt still running, so this is not the run total yet')
    expect(text).not.toMatch(/0 input/)
  })

  it('shows a neutral empty state before any attempt is dispatched', () => {
    expect(describeRunTokenUsage({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 })).toBe(
      'No token usage data yet — no agent attempt has been dispatched',
    )
  })

  it('describes pending evidence as not-yet-total, distinct from a genuine partial gap', () => {
    const text = describeRunTokenUsage({
      completeness: 'PendingEvidence',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 1,
      pendingAttemptCount: 1,
      terminalAttemptsWithUnknownUsage: 0,
      inputTokens: 1000,
      outputTokens: 200,
    })

    expect(text).toBe(
      'Partial token count so far: 1,000 input · 200 output from 1 of 2 dispatched attempts'
        + ' — 1 attempt still running, so this is not the run total yet',
    )
    expect(text).not.toMatch(/total:/i)
  })

  it('never presents an unrecognized completeness as a total', () => {
    expect(describeRunTokenUsage({ completeness: 'Something', inputTokens: 5, outputTokens: 5 })).toBe(
      'Token usage summary unavailable',
    )
    expect(describeRunTokenUsage(undefined)).toBe('Token usage summary unavailable')
  })
})
