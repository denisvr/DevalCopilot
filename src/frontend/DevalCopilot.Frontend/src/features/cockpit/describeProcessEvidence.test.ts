import { describe, expect, it } from 'vitest'
import { describeProcessEvidence, formatMilliseconds, hasTrustedProcessEvidence } from './describeProcessEvidence'

const terminal = { dispatched: true, running: false }

describe('describeProcessEvidence', () => {
  it('describes a clean exit with its exit code, host-measured duration, and configured timeout', () => {
    expect(
      describeProcessEvidence(
        { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1250, timeoutMilliseconds: 600_000 },
        terminal,
      ),
    ).toBe('Process exited with code 0 after 1.3 s · timeout 10m 00s')
  })

  it('describes a non-zero exit code exactly', () => {
    expect(describeProcessEvidence({ outcome: 'Exited', exitCode: 137, durationMilliseconds: 42 }, terminal)).toBe(
      'Process exited with code 137 after 42 ms',
    )
  })

  it('describes a timeout without any exit code', () => {
    expect(
      describeProcessEvidence({ outcome: 'TimedOut', durationMilliseconds: 600_123, timeoutMilliseconds: 600_000 }, terminal),
    ).toBe('Process timed out after 10m 00s · timeout 10m 00s')
  })

  it('describes a cancellation without any exit code', () => {
    expect(describeProcessEvidence({ outcome: 'Cancelled', durationMilliseconds: 4000 }, terminal)).toBe(
      'Process cancelled after 4.0 s',
    )
  })

  it('states truthful absence instead of inventing a result', () => {
    expect(describeProcessEvidence({ timeoutMilliseconds: 1_200_000 }, { dispatched: false, running: true })).toBe(
      'Process not started · timeout 20m 00s',
    )
    expect(describeProcessEvidence({ timeoutMilliseconds: 1_200_000 }, { dispatched: true, running: true })).toBe(
      'Process result not yet recorded · timeout 20m 00s',
    )
    expect(describeProcessEvidence({ timeoutMilliseconds: 1_200_000 }, terminal)).toBe(
      'Process evidence unknown · timeout 20m 00s',
    )
    expect(describeProcessEvidence(undefined, terminal)).toBe('Process evidence unknown')
  })

  it('never trusts an unrecognized outcome or an Exited outcome without an exit code', () => {
    expect(describeProcessEvidence({ outcome: 'Crashed', durationMilliseconds: 10 }, terminal)).toBe('Process evidence unknown')
    expect(describeProcessEvidence({ outcome: 'Exited', durationMilliseconds: 10 }, terminal)).toBe('Process evidence unknown')
  })

  // Regression: dispatch/running state must be checked BEFORE the evidence object's own shape. A
  // well-formed-looking object (exactly what a tampered or prematurely-populated persisted row
  // would look like) must never be described as measured evidence while the attempt is still
  // running or was never dispatched — mirrors describeTokenUsage's own ordering fix.
  it('never describes a well-formed-looking object as measured evidence for a running or undispatched attempt', () => {
    const wellFormed = { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1250, timeoutMilliseconds: 600_000 }

    expect(describeProcessEvidence(wellFormed, { dispatched: true, running: true })).toBe(
      'Process result not yet recorded · timeout 10m 00s',
    )
    expect(describeProcessEvidence(wellFormed, { dispatched: false, running: false })).toBe(
      'Process not started · timeout 10m 00s',
    )
  })
})

describe('hasTrustedProcessEvidence', () => {
  const wellFormed = { outcome: 'Exited', exitCode: 0, durationMilliseconds: 1250 }

  it('never trusts well-formed-looking evidence for a running or undispatched attempt', () => {
    expect(hasTrustedProcessEvidence(wellFormed, { dispatched: true, running: true })).toBe(false)
    expect(hasTrustedProcessEvidence(wellFormed, { dispatched: false, running: false })).toBe(false)
    expect(hasTrustedProcessEvidence(wellFormed, { dispatched: false, running: true })).toBe(false)
  })

  it('trusts well-formed evidence for a genuinely dispatched, terminal attempt, including nonzero exit, TimedOut, and Cancelled', () => {
    expect(hasTrustedProcessEvidence(wellFormed, terminal)).toBe(true)
    expect(hasTrustedProcessEvidence({ outcome: 'Exited', exitCode: 137 }, terminal)).toBe(true)
    expect(hasTrustedProcessEvidence({ outcome: 'TimedOut', durationMilliseconds: 600_000 }, terminal)).toBe(true)
    expect(hasTrustedProcessEvidence({ outcome: 'Cancelled', durationMilliseconds: 4000 }, terminal)).toBe(true)
  })

  it('never trusts an absent, unrecognized, or malformed-Exited object even when dispatched and terminal', () => {
    expect(hasTrustedProcessEvidence(null, terminal)).toBe(false)
    expect(hasTrustedProcessEvidence(undefined, terminal)).toBe(false)
    expect(hasTrustedProcessEvidence({ outcome: 'Crashed' }, terminal)).toBe(false)
    expect(hasTrustedProcessEvidence({ outcome: 'Exited' }, terminal)).toBe(false)
  })
})

describe('formatMilliseconds', () => {
  it('formats sub-second, sub-minute, and longer durations', () => {
    expect(formatMilliseconds(0)).toBe('0 ms')
    expect(formatMilliseconds(999)).toBe('999 ms')
    expect(formatMilliseconds(1000)).toBe('1.0 s')
    expect(formatMilliseconds(59_949)).toBe('59.9 s')
    expect(formatMilliseconds(65_000)).toBe('1m 05s')
    expect(formatMilliseconds(-5)).toBe('0 ms')
  })
})
