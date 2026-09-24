import { describe, expect, it } from 'vitest'
import { describeRunTokenUsage, describeTokenUsage, formatTokenCount, hasKnownTokenUsage } from './describeTokenUsage'

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

  it('never labels a partial sum as a total and states how much it covers', () => {
    const text = describeRunTokenUsage({
      completeness: 'Partial',
      attemptsWithKnownUsage: 1,
      attemptsWithUnknownUsage: 2,
      inputTokens: 1200,
      outputTokens: 345,
    })

    expect(text).toBe(
      'Partial token count: 1,200 input · 345 output from 1 of 3 dispatched attempts'
        + ' — 2 attempts have no usage evidence, so this is not the run total',
    )
    expect(text).not.toMatch(/total:/i)
  })

  it('uses singular wording for a single unknown attempt', () => {
    expect(
      describeRunTokenUsage({
        completeness: 'Partial',
        attemptsWithKnownUsage: 0,
        attemptsWithUnknownUsage: 1,
        inputTokens: 0,
        outputTokens: 0,
      }),
    ).toBe(
      'Partial token count: 0 input · 0 output from 0 of 1 dispatched attempt — 1 attempt has no usage evidence, so this is not the run total',
    )
  })

  it('shows a neutral empty state before any attempt is dispatched', () => {
    expect(describeRunTokenUsage({ completeness: 'NoDispatchedAttempts', inputTokens: 0, outputTokens: 0 })).toBe(
      'No token usage data yet — no agent attempt has been dispatched',
    )
  })

  it('never presents an unrecognized completeness as a total', () => {
    expect(describeRunTokenUsage({ completeness: 'Something', inputTokens: 5, outputTokens: 5 })).toBe(
      'Token usage summary unavailable',
    )
    expect(describeRunTokenUsage(undefined)).toBe('Token usage summary unavailable')
  })
})
