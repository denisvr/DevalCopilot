import { describe, expect, it } from 'vitest'
import type { AgentAttemptEvidenceResponse } from '../../api/generated/api-client'
import { toAgentAttemptExecutionFacts } from './agentAttemptExecutionFacts'
import { attemptEvidenceFor } from './agentAttemptHistoryFixture'

const CLAIMED = new Date('2026-10-09T10:00:00Z')
const DISPATCHED = new Date('2026-10-09T10:00:02Z')
const ENDED = new Date('2026-10-09T10:01:05Z')
const INT32_MAX = 2_147_483_647
const INT32_MIN = -2_147_483_648

/** A concluded, dispatched attempt whose answer carries `processExecution` and `tokenUsage` exactly as given (never revived or coerced). */
function concluded(overrides: Record<string, unknown> = {}): AgentAttemptEvidenceResponse {
  return {
    ...attemptEvidenceFor('run-1', 1),
    attemptStatus: 'Failed',
    outcome: 'ProviderInvocationFailed',
    claimedAtUtc: CLAIMED,
    dispatchedAtUtc: DISPATCHED,
    completedAtUtc: ENDED,
    processExecution: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234, timeoutMilliseconds: 900_000 },
    tokenUsage: { inputTokens: 1200, outputTokens: 80, cacheCreationInputTokens: 0, cacheReadInputTokens: 30 },
    ...overrides,
  } as unknown as AgentAttemptEvidenceResponse
}

const processOf = (processExecution: unknown) => toAgentAttemptExecutionFacts(concluded({ processExecution })).process
const tokensOf = (tokenUsage: unknown) => toAgentAttemptExecutionFacts(concluded({ tokenUsage })).tokenUsage
const timeoutOf = (timeoutMilliseconds: unknown) =>
  toAgentAttemptExecutionFacts(concluded({ processExecution: { outcome: null, exitCode: null, durationMilliseconds: null, timeoutMilliseconds } })).timeout
const unknownProcess = { outcome: null, exitCode: null, durationMilliseconds: null, timeoutMilliseconds: 900_000 }
const unknownUsage = { inputTokens: null, outputTokens: null, cacheCreationInputTokens: null, cacheReadInputTokens: null }

describe('toAgentAttemptExecutionFacts', () => {
  it('admits a clean exit beside a failed semantic result without merging them', () => {
    const facts = toAgentAttemptExecutionFacts(concluded())

    expect(facts).toEqual({
      claimed: { status: 'recorded', at: CLAIMED },
      dispatched: { status: 'recorded', at: DISPATCHED },
      completed: { status: 'recorded', at: ENDED },
      process: { status: 'known', value: { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1234 } },
      timeout: { status: 'known', milliseconds: 900_000 },
      tokenUsage: { status: 'known', value: { inputTokens: 1200, outputTokens: 80, cacheCreationInputTokens: 0, cacheReadInputTokens: 30 } },
    })
  })

  it.each<[string, { outcome: string; exitCode?: number; durationMilliseconds: number }]>([
    ['a nonzero exit', { outcome: 'Exited', exitCode: 1, durationMilliseconds: 50 }],
    ['the lowest int32 exit code', { outcome: 'Exited', exitCode: INT32_MIN, durationMilliseconds: 50 }],
    ['the highest int32 exit code', { outcome: 'Exited', exitCode: INT32_MAX, durationMilliseconds: 50 }],
    ['a timeout', { outcome: 'TimedOut', durationMilliseconds: 900_000 }],
    ['a cancellation', { outcome: 'Cancelled', durationMilliseconds: 12 }],
    ['a zero duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: 0 }],
    ['the largest safe duration', { outcome: 'Cancelled', durationMilliseconds: Number.MAX_SAFE_INTEGER }],
  ])('admits %s as known process evidence', (_label, value) => {
    expect(processOf({ ...value, exitCode: value.exitCode ?? null, timeoutMilliseconds: 900_000 })).toEqual({ status: 'known', value })
  })

  it.each([
    ['all members null', unknownProcess],
    ['all members missing', { timeoutMilliseconds: 900_000 }],
    ['no process object', null],
    ['an undefined process object', undefined],
  ])('reads a concluded attempt with %s as unknown process evidence, never zero', (_label, processExecution) => {
    expect(processOf(processExecution)).toEqual({ status: 'unknown' })
  })

  it.each<[string, unknown]>([
    ['a negative duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: -1 }],
    ['a fractional duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1.5 }],
    ['a nonfinite duration', { outcome: 'TimedOut', durationMilliseconds: Number.POSITIVE_INFINITY }],
    ['a NaN duration', { outcome: 'TimedOut', durationMilliseconds: Number.NaN }],
    ['an unsafe duration', { outcome: 'TimedOut', durationMilliseconds: 2 ** 53 }],
    ['a textual duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: '1234' }],
    ['an outcome without a duration', { outcome: 'Exited', exitCode: 0, durationMilliseconds: null }],
    ['a duration without an outcome', { outcome: null, exitCode: null, durationMilliseconds: 10 }],
    ['an exit code without an outcome', { outcome: null, exitCode: 0, durationMilliseconds: null }],
    ['an exit without an exit code', { outcome: 'Exited', exitCode: null, durationMilliseconds: 10 }],
    ['a fractional exit code', { outcome: 'Exited', exitCode: 0.5, durationMilliseconds: 10 }],
    ['an exit code above int32', { outcome: 'Exited', exitCode: INT32_MAX + 1, durationMilliseconds: 10 }],
    ['an exit code below int32', { outcome: 'Exited', exitCode: INT32_MIN - 1, durationMilliseconds: 10 }],
    ['a textual exit code', { outcome: 'Exited', exitCode: '0', durationMilliseconds: 10 }],
    ['a timeout with an exit code', { outcome: 'TimedOut', exitCode: 0, durationMilliseconds: 10 }],
    ['a cancellation with an exit code', { outcome: 'Cancelled', exitCode: 137, durationMilliseconds: 10 }],
    ['an unrecognized outcome', { outcome: 'Crashed', durationMilliseconds: 10 }],
    ['a prototype-shaped outcome', { outcome: 'toString', durationMilliseconds: 10 }],
    ['a non-string outcome', { outcome: 0, durationMilliseconds: 10 }],
    ['a process object that is text', 'Exited'],
    ['a process object that is an array', [{ outcome: 'Exited', exitCode: 0, durationMilliseconds: 10 }]],
  ])('makes %s unavailable instead of clamping or inventing it', (_label, processExecution) => {
    expect(processOf(processExecution)).toEqual({ status: 'unavailable' })
  })

  it.each([
    ['input and output with both cache counts', { inputTokens: 5, outputTokens: 7, cacheCreationInputTokens: 11, cacheReadInputTokens: 13 }],
    ['input and output without cache counts (a provider with no cache breakdown)', { inputTokens: 5, outputTokens: 7 }],
    ['only a cache-read count', { inputTokens: 5, outputTokens: 7, cacheReadInputTokens: 13 }],
    ['only a cache-creation count', { inputTokens: 5, outputTokens: 7, cacheCreationInputTokens: 11 }],
    ['zero counts', { inputTokens: 0, outputTokens: 0, cacheCreationInputTokens: 0, cacheReadInputTokens: 0 }],
    ['the highest int32 counts', { inputTokens: INT32_MAX, outputTokens: INT32_MAX, cacheReadInputTokens: INT32_MAX }],
  ])('admits %s as known usage, keeping each absent cache count absent', (_label, value) => {
    expect(tokensOf({ cacheCreationInputTokens: null, cacheReadInputTokens: null, ...value })).toEqual({ status: 'known', value })
  })

  it.each([
    ['all members null', unknownUsage],
    ['all members missing', {}],
    ['no usage object', null],
    ['an undefined usage object', undefined],
  ])('reads a concluded attempt with %s as unknown usage, never zero', (_label, tokenUsage) => {
    expect(tokensOf(tokenUsage)).toEqual({ status: 'unknown' })
  })

  it.each<[string, unknown]>([
    ['a negative input count', { inputTokens: -1, outputTokens: 7 }],
    ['a fractional output count', { inputTokens: 5, outputTokens: 7.5 }],
    ['an input count above int32', { inputTokens: INT32_MAX + 1, outputTokens: 7 }],
    ['a nonfinite output count', { inputTokens: 5, outputTokens: Number.POSITIVE_INFINITY }],
    ['a NaN input count', { inputTokens: Number.NaN, outputTokens: 7 }],
    ['a textual input count', { inputTokens: '5', outputTokens: 7 }],
    ['input without output', { inputTokens: 5, outputTokens: null }],
    ['output without input', { inputTokens: null, outputTokens: 7 }],
    ['cache counts without input and output', { cacheCreationInputTokens: 1, cacheReadInputTokens: 2 }],
    ['a negative cache-creation count', { inputTokens: 5, outputTokens: 7, cacheCreationInputTokens: -1 }],
    ['a fractional cache-read count', { inputTokens: 5, outputTokens: 7, cacheReadInputTokens: 0.25 }],
    ['a textual cache-read count', { inputTokens: 5, outputTokens: 7, cacheReadInputTokens: '2' }],
    ['a cache count above int32', { inputTokens: 5, outputTokens: 7, cacheCreationInputTokens: INT32_MAX + 1 }],
    ['a usage object that is a number', 5],
    ['a usage object that is an array', [5, 7]],
  ])('makes %s unavailable instead of clamping, defaulting or inventing it', (_label, tokenUsage) => {
    expect(tokensOf(tokenUsage)).toEqual({ status: 'unavailable' })
  })

  it.each<[string, unknown, unknown]>([
    ['a positive whole timeout', 900_000, { status: 'known', milliseconds: 900_000 }],
    ['the largest safe timeout', Number.MAX_SAFE_INTEGER, { status: 'known', milliseconds: Number.MAX_SAFE_INTEGER }],
    ['a missing timeout', null, { status: 'unknown' }],
    ['a zero timeout', 0, { status: 'unavailable' }],
    ['a negative timeout', -1, { status: 'unavailable' }],
    ['a fractional timeout', 1.5, { status: 'unavailable' }],
    ['an unsafe timeout', 2 ** 53, { status: 'unavailable' }],
    ['a nonfinite timeout', Number.POSITIVE_INFINITY, { status: 'unavailable' }],
    ['a textual timeout', '900000', { status: 'unavailable' }],
  ])('reads %s as the configured timeout fact', (_label, timeoutMilliseconds, expected) => {
    expect(timeoutOf(timeoutMilliseconds)).toEqual(expected)
  })

  it('keeps the configured timeout independent of the measured evidence and of dispatch', () => {
    const malformed = toAgentAttemptExecutionFacts(
      concluded({ processExecution: { outcome: 'Exited', exitCode: null, durationMilliseconds: -5, timeoutMilliseconds: 600_000 } }),
    )
    expect(malformed.process).toEqual({ status: 'unavailable' })
    expect(malformed.timeout).toEqual({ status: 'known', milliseconds: 600_000 })

    const undispatched = toAgentAttemptExecutionFacts(concluded({ dispatchedAtUtc: undefined }))
    expect(undispatched.timeout).toEqual({ status: 'known', milliseconds: 900_000 })
    expect(toAgentAttemptExecutionFacts(concluded({ processExecution: null })).timeout).toEqual({ status: 'unknown' })
    expect(toAgentAttemptExecutionFacts(concluded({ processExecution: 'x' })).timeout).toEqual({ status: 'unavailable' })
  })

  it('keeps each section independent: a malformed one discredits neither sibling', () => {
    const facts = toAgentAttemptExecutionFacts(concluded({ tokenUsage: { inputTokens: -1, outputTokens: 7 } }))

    expect(facts.tokenUsage).toEqual({ status: 'unavailable' })
    expect(facts.process.status).toBe('known')
    expect(facts.timeout.status).toBe('known')
    expect(toAgentAttemptExecutionFacts(concluded({ processExecution: { outcome: 'Lost', durationMilliseconds: 1, timeoutMilliseconds: 1 } })).tokenUsage.status).toBe(
      'known',
    )
  })

  it('exposes no measured result or known usage for a still-running attempt, whatever its answer carries', () => {
    const facts = toAgentAttemptExecutionFacts(concluded({ attemptStatus: 'Running', outcome: undefined, completedAtUtc: undefined }))

    expect(facts.process).toEqual({ status: 'running' })
    expect(facts.tokenUsage).toEqual({ status: 'running' })
    expect(facts.timeout).toEqual({ status: 'known', milliseconds: 900_000 })
    expect(facts.completed).toEqual({ status: 'absent' })
  })

  it('exposes no measured result or known usage for an undispatched attempt, whatever its answer carries', () => {
    for (const attemptStatus of ['Failed', 'Interrupted', 'Running']) {
      const facts = toAgentAttemptExecutionFacts(concluded({ attemptStatus, dispatchedAtUtc: null }))

      expect(facts.dispatched).toEqual({ status: 'absent' })
      expect(facts.process).toEqual({ status: 'not-dispatched' })
      expect(facts.tokenUsage).toEqual({ status: 'not-dispatched' })
    }
  })

  it('keeps a dispatched, interrupted or legacy attempt without recorded evidence unknown', () => {
    const facts = toAgentAttemptExecutionFacts(concluded({ attemptStatus: 'Interrupted', outcome: null, processExecution: unknownProcess, tokenUsage: unknownUsage }))

    expect(facts.process).toEqual({ status: 'unknown' })
    expect(facts.tokenUsage).toEqual({ status: 'unknown' })
  })

  it.each<[string, unknown]>([
    ['an invalid Date', new Date(Number.NaN)],
    ['ISO text that was never revived', '2026-10-09T10:00:02Z'],
    ['a number', 0],
    ['an object', { value: '2026-10-09T10:00:02Z' }],
  ])('never turns a dispatch time that is %s into a date, and trusts no measured fact behind it', (_label, dispatchedAtUtc) => {
    const facts = toAgentAttemptExecutionFacts(concluded({ dispatchedAtUtc }))

    expect(facts.dispatched).toEqual({ status: 'unavailable' })
    expect(facts.process).toEqual({ status: 'unavailable' })
    expect(facts.tokenUsage).toEqual({ status: 'unavailable' })
    expect(facts.timeout).toEqual({ status: 'known', milliseconds: 900_000 })
  })

  it('never turns a malformed or contradictory claim or end time into a date', () => {
    expect(toAgentAttemptExecutionFacts(concluded({ completedAtUtc: new Date(Number.NaN) })).completed).toEqual({ status: 'unavailable' })
    expect(toAgentAttemptExecutionFacts(concluded({ completedAtUtc: 1 })).completed).toEqual({ status: 'unavailable' })
    expect(toAgentAttemptExecutionFacts(concluded({ completedAtUtc: undefined })).completed).toEqual({ status: 'absent' })
    expect(toAgentAttemptExecutionFacts(concluded({ claimedAtUtc: '2026-10-09' })).claimed).toEqual({ status: 'unavailable' })
    // Only a concluded attempt is ever given a completion time.
    expect(toAgentAttemptExecutionFacts(concluded({ attemptStatus: 'Running', completedAtUtc: ENDED })).completed).toEqual({ status: 'unavailable' })
  })
})
