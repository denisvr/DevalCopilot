import { describe, expect, it } from 'vitest'
import { INVOCATION_DIAGNOSTIC_COPY, describeInvocationDiagnostic, hasIncompleteCapture } from './describeInvocationDiagnostic'

const CATEGORIES = [
  'InvocationUnavailable',
  'EnvironmentUnavailable',
  'TimedOut',
  'Cancelled',
  'CaptureDeadlineExceeded',
  'CaptureFault',
  'AuthenticationRequired',
  'ProviderLimitReached',
  'ProviderReportedError',
  'NonZeroExit',
  'InvalidEnvelope',
  'StructuredOutputMissing',
  'InvalidStructuredOutput',
]

describe('describeInvocationDiagnostic (ADR-0072)', () => {
  it('has fixed copy for exactly the thirteen closed categories', () => {
    expect(Object.keys(INVOCATION_DIAGNOSTIC_COPY).sort()).toEqual([...CATEGORIES].sort())
  })

  it.each(CATEGORIES)('shows the fixed copy for a recorded %s', (category) => {
    expect(describeInvocationDiagnostic({ state: 'Recorded', category, version: 'agent-invocation-diagnostic-v1' })).toEqual({
      status: 'recorded',
      category,
      copy: INVOCATION_DIAGNOSTIC_COPY[category],
    })
  })

  it('keeps historical absence as not recorded', () => {
    expect(describeInvocationDiagnostic(undefined)).toEqual({ status: 'not-recorded' })
    expect(describeInvocationDiagnostic({ state: 'NotRecorded' })).toEqual({ status: 'not-recorded' })
  })

  it.each([
    [{ state: 'Recorded', category: 'SomethingNew', version: 'agent-invocation-diagnostic-v1' }],
    [{ state: 'Recorded', category: 'TimedOut', version: 'agent-invocation-diagnostic-v9' }],
    [{ state: 'Unknown' }],
    [{ state: 'NotRecorded', category: 'TimedOut' }],
    [{ state: 'Bogus' }],
  ])('never guesses an unreadable value (%j)', (wire) => {
    expect(describeInvocationDiagnostic(wire)).toEqual({ status: 'unavailable' })
  })

  it('reports incomplete capture only from recorded facts', () => {
    expect(hasIncompleteCapture(undefined)).toBe(false)
    expect(hasIncompleteCapture({})).toBe(false)
    expect(hasIncompleteCapture({ standardOutputCompletion: 'Complete', standardErrorCompletion: 'Complete', standardInputDelivery: 'NotRedirected' })).toBe(false)
    expect(hasIncompleteCapture({ standardOutputCompletion: 'Complete', standardErrorCompletion: 'Complete', standardInputDelivery: 'Complete' })).toBe(false)
    expect(hasIncompleteCapture({ standardOutputCompletion: 'DeadlineExceeded', standardErrorCompletion: 'Complete', standardInputDelivery: 'Complete' })).toBe(true)
    expect(hasIncompleteCapture({ standardOutputCompletion: 'Complete', standardErrorCompletion: 'Complete', standardInputDelivery: 'Faulted' })).toBe(true)
  })
})
