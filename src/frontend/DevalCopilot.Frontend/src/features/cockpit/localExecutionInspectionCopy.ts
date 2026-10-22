import type { LocalExecutionInspectionFailure } from './hooks/useLocalExecutionInspection'
import type { LocalExecutionVerificationStatus } from './localExecutionInspection'

// Fixed English copy of the recorded local execution inspection (ADR-0061). Nothing here is taken from a host answer or an exception:
// each text is chosen from a closed code. Recipe and command names are the only untrusted text and are rendered inert by the components.

export const INSPECTION_REGION_LABEL = 'Inspect local execution'

export const INSPECTION_NOTE =
  'This is a recorded inspection: what the host stored for this run\'s local execution sequence, read when you asked. It is not a coherent '
  + 'replay of every historical instant, not process liveness, not proof that the source is current, not a new approval, not readiness and '
  + 'not authority to execute anything. While a cycle is still advancing, a later reload may show a later recorded result.'

export const INSPECTION_READING = 'Reading the recorded local execution sequence…'
export const INSPECTION_NOT_RECORDED = 'No local execution sequence is recorded for this run.'
export const INSPECTION_UNAVAILABLE =
  'The recorded sequence is present but its structure could not be proved, or it changed while it was being read, so none of it is shown. Reload to read it again.'

export const INSPECTION_FAILURES: Readonly<Record<LocalExecutionInspectionFailure, string>> = {
  read: 'The recorded local execution sequence could not be read.',
  'not-found': 'The host no longer knows this run.',
  invalid: 'The host answered with a recorded sequence that is not coherent for this run, so none of it is shown.',
}

export const STEP_FAILURES: Readonly<Record<LocalExecutionInspectionFailure, string>> = {
  read: 'This step\'s recorded operation could not be read.',
  'not-found': 'This step is no longer available.',
  invalid: 'The host answered with an operation that is not coherent for this step, so none of it is shown.',
}

export const STEP_NO_OPERATION =
  'No operation is recorded for this step: it is reserved, revoked or unresolved and linked to nothing. Nothing is assumed about whether a reserved operation started.'
export const STEP_UNAVAILABLE =
  'This step\'s recorded structure or operation could not be proved, so none of it is shown. This does not mean no operation exists.'

export const AGENT_STEP_NOTE =
  'The linked Agent attempt is shown with its own recorded evidence below. That evidence describes this one attempt only, not the whole sequence.'
export const VERIFICATION_STEP_NOTE =
  'These are the recorded facts of the linked verification run, as read now. A recorded status is bookkeeping: it is never a clean exit unless '
  + 'it says Passed, and a historical Passed is never a current approval.'

export const VERIFICATION_STATUS_LABEL: Readonly<Record<LocalExecutionVerificationStatus, string>> = {
  Running: 'Running (recorded; not proof of a live process)',
  Passed: 'Passed',
  Failed: 'Failed',
  TimedOut: 'Timed out',
  Cancelled: 'Cancelled',
  Interrupted: 'Interrupted',
  SourceChanged: 'Source changed',
  LocalExecutionSequenceDispatchRefused: 'Dispatch refused (no process ran)',
}

export const STREAM_LABELS = { stdout: 'Standard output', stderr: 'Standard error' } as const

export const NOT_RECORDED = 'Not recorded'
