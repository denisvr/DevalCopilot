import type { ProjectVerificationOutcome, ProjectVerificationStatus } from './projectVerificationInspection'

/** The fixed copy of the recorded project verification inspection region (ADR-0069). */
export const PROJECT_VERIFICATION_REGION_LABEL = 'Inspect recorded verifications'
export const PROJECT_VERIFICATION_HIDE_LABEL = 'Hide recorded verifications'
export const PROJECT_VERIFICATION_RELOAD_LABEL = 'Reload recorded verifications'
export const PROJECT_VERIFICATION_OLDER_LABEL = 'Older verifications'

export const PROJECT_VERIFICATION_NOTE =
  'These are the verification executions recorded for this project, newest first, shown as the records read now. They are not '
  + 'attributed to any run, and they do not prove that a source is current, that any check is approved or still applies, that a process '
  + 'is still running, or that anything is released. Each page and each output window is a separate read, not one consistent '
  + 'snapshot; nothing is read until you ask for it.'

export const PROJECT_VERIFICATION_READING = 'Reading recorded verifications…'
export const PROJECT_VERIFICATION_EMPTY = 'No verification execution is recorded for this project.'
export const PROJECT_VERIFICATION_PAGE_UNAVAILABLE =
  'Recorded verifications unavailable: a recorded execution on this page could not be verified, so none of this page is shown. '
  + 'This does not mean older executions are absent or that history is exhausted.'
export const PROJECT_VERIFICATION_EXECUTION_UNAVAILABLE =
  'This recorded verification is unavailable: its recorded ownership or result facts could not be verified, so none of them is shown. '
  + 'It is a recorded execution of this project; this does not mean it recorded nothing.'
export const PROJECT_VERIFICATION_EXECUTION_READING = 'Reading the recorded verification…'
export const PROJECT_VERIFICATION_NOT_RECORDED = 'not recorded'

export const PROJECT_VERIFICATION_PAGE_FAILURES = {
  read: 'The recorded verifications could not be loaded.',
  'not-found': 'This project is no longer available.',
  timeout: 'The host did not answer within ten seconds. Nothing is shown as current.',
  invalid: 'The host answered with recorded verifications that are not coherent for this request, so none of them are shown.',
} as const

export const PROJECT_VERIFICATION_EXECUTION_FAILURES = {
  read: 'This recorded verification could not be loaded.',
  'not-found': 'This recorded verification is no longer available.',
  timeout: 'The host did not answer within ten seconds. Nothing is shown as current.',
  invalid:
    'The host answered with facts that are not coherent for the verification you selected, or that differ from the listed ones, so none of them are shown.',
} as const

export const PROJECT_VERIFICATION_STATUS_LABEL: Record<ProjectVerificationStatus, string> = {
  Running: 'Running (recorded, not observed now)',
  Passed: 'Passed',
  Failed: 'Failed',
  TimedOut: 'Timed out',
  Cancelled: 'Cancelled',
  Interrupted: 'Interrupted',
  SourceChanged: 'Source changed',
  LocalExecutionSequenceDispatchRefused: 'Dispatch refused (no process ran)',
}

export const PROJECT_VERIFICATION_OUTCOME_LABEL: Record<ProjectVerificationOutcome, string> = {
  Exited: 'process exited',
  TimedOut: 'process timed out',
  Cancelled: 'process cancelled',
  Failed: 'process failed',
}

export const PROJECT_VERIFICATION_STATUS_NOTE =
  'A recorded status, outcome and exit code are bookkeeping of what the host recorded; they are not a verdict for the current source, '
  + 'an approval, or proof of a physical exit.'
