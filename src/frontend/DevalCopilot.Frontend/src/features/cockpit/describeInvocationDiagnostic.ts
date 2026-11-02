/**
 * ADR-0072: fixed, actionable copy for the closed Claude invocation diagnostic recorded with a failed attempt. The text is chosen from
 * the category alone; nothing the provider printed is ever shown. A category says what was observed or reported, never a verified
 * cause, and no copy promises that a new attempt will succeed or offers an in-app repair, retry or sign-in action.
 */
export const INVOCATION_DIAGNOSTIC_COPY: Readonly<Record<string, string>> = {
  InvocationUnavailable:
    'Claude Code could not be started for this attempt. Check that the selected Claude Code installation is still present before requesting a new attempt.',
  EnvironmentUnavailable:
    'The required Windows environment could not be derived, so no process was started. Restore normal access to the Windows system directory before requesting a new attempt.',
  TimedOut: 'The invocation exceeded its time limit and was asked to stop.',
  Cancelled: 'The invocation was cancelled and was asked to stop.',
  CaptureDeadlineExceeded:
    'The output did not finish within the bounded window after the process ended; another process may still have held it. The partial output is not evidence.',
  CaptureFault: 'The output could not be read or stored completely. The partial output is not evidence.',
  AuthenticationRequired:
    'Claude Code reported an authentication error (HTTP 401). Sign in to Claude Code outside DevalCopilot before requesting a new attempt.',
  ProviderLimitReached:
    'Claude Code reported a usage or rate limit (HTTP 429). Wait until the limit resets before requesting a new attempt.',
  ProviderReportedError: 'Claude Code reported an error without a more specific documented signal.',
  NonZeroExit: 'Claude Code exited with a non-zero code without an interpretable error report.',
  InvalidEnvelope: 'Claude Code’s output was not one well-formed, consistent result report.',
  StructuredOutputMissing: 'Claude Code finished without the required structured response.',
  InvalidStructuredOutput: 'Claude Code’s structured response was not a JSON object.',
}

export const INVOCATION_DIAGNOSTIC_CAVEAT = 'This describes what was observed or reported; it is not a verified cause.'

export type InvocationDiagnosticView =
  | { status: 'recorded'; category: string; copy: string }
  | { status: 'not-recorded' }
  | { status: 'unavailable' }

interface DiagnosticWire {
  state?: string | null
  category?: string | null
  version?: string | null
}

const KNOWN_VERSION = 'agent-invocation-diagnostic-v1'

/** An unknown state, category or version is unavailable; absence is not-recorded (every earlier attempt and every success). */
export function describeInvocationDiagnostic(wire: DiagnosticWire | null | undefined): InvocationDiagnosticView {
  if (wire === null || wire === undefined || wire.state === 'NotRecorded') {
    return wire === null || wire === undefined || (wire.category == null && wire.version == null)
      ? { status: 'not-recorded' }
      : { status: 'unavailable' }
  }

  if (wire.state !== 'Recorded' || wire.version !== KNOWN_VERSION || typeof wire.category !== 'string') {
    return { status: 'unavailable' }
  }

  const copy = INVOCATION_DIAGNOSTIC_COPY[wire.category]
  return copy === undefined ? { status: 'unavailable' } : { status: 'recorded', category: wire.category, copy }
}

const COMPLETE_STREAM = 'Complete'
const COMPLETE_INPUT: readonly string[] = ['Complete', 'NotRedirected']

/** True only when recorded I/O facts exist and at least one says the stream or input did not complete. */
export function hasIncompleteCapture(processExecution: {
  standardOutputCompletion?: string | null
  standardErrorCompletion?: string | null
  standardInputDelivery?: string | null
} | null | undefined): boolean {
  if (!processExecution) {
    return false
  }

  const { standardOutputCompletion, standardErrorCompletion, standardInputDelivery } = processExecution
  if (standardOutputCompletion == null && standardErrorCompletion == null && standardInputDelivery == null) {
    return false
  }

  return standardOutputCompletion !== COMPLETE_STREAM
    || standardErrorCompletion !== COMPLETE_STREAM
    || !COMPLETE_INPUT.includes(standardInputDelivery ?? '')
}
