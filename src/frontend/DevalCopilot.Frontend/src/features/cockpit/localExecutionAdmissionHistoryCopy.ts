import type { LocalExecutionAdmissionRecordedState } from './localExecutionAdmissionHistory'
import type { LocalExecutionAdmissionHistoryFailure } from './hooks/useLocalExecutionAdmissionHistory'

// Fixed English copy of the recorded queue-admission inspection (ADR-0063). Nothing here is taken from a host answer or an exception:
// each text is chosen from a closed code. Recipe names are the only untrusted text and are rendered inert by the components.

export const ADMISSIONS_REGION_LABEL = 'Inspect queue admissions'
export const ADMISSIONS_HIDE_LABEL = 'Hide queue admissions'
export const ADMISSIONS_RELOAD_LABEL = 'Reload queue admissions'

export const ADMISSIONS_NOTE =
  'This is the recorded admission ledger of this queued cycle, read when you asked: every admission the host recorded, including a wait '
  + 'that closed without ever creating a step. The number is the recorded admission order, not an exact queue position among all queued '
  + 'work. A waiting reason is the last recorded admission check, not current capacity, rank, eligibility or a promised start time. '
  + 'Nothing here starts, orders, stops, retries or releases anything.'

export const ADMISSIONS_READING = 'Reading the recorded queue admissions…'
export const ADMISSIONS_NOT_RECORDED = 'No local execution sequence is recorded for this run, so there are no admissions.'
export const ADMISSIONS_NOT_QUEUED = 'This run\'s recorded sequence was not queued: it started its steps immediately, so it recorded no admissions.'
export const ADMISSIONS_UNAVAILABLE =
  'The recorded admissions are present but their structure or links could not be proved, so none of them are shown. Reload to read them again.'

export const ADMISSIONS_FAILURES: Readonly<Record<LocalExecutionAdmissionHistoryFailure, string>> = {
  read: 'The recorded queue admissions could not be read.',
  'not-found': 'The host no longer knows this run.',
  invalid: 'The host answered with queue admissions that are not coherent for this run, so none of them are shown.',
}

export const ADMISSION_STATE_LABEL: Readonly<Record<LocalExecutionAdmissionRecordedState, string>> = {
  Waiting: 'Waiting (recorded; owns no workspace, slot or step)',
  Reserved: 'Reserved (its preparation was reserved; nothing is claimed)',
  Consumed: 'Consumed (its operation was claimed; this is not proof the operation succeeded)',
  Retired: 'Retired (closed without becoming an operation)',
  Unresolved: 'Unresolved (a reservation that could not be proved unused or claimed; held, not released)',
}

export const ADMISSION_NO_STEP_WAITING = 'No step was created for this admission, so there is no operation to inspect.'
export const ADMISSION_NO_STEP_RETIRED = 'This admission was retired before any step was created, so there is no operation to inspect.'
export const ADMISSION_STEP_NOT_INDEXED =
  'Its step is not in the loaded sequence index. Reload the recorded sequence first; nothing is searched or substituted.'
export const ADMISSION_STEP_OTHER_SEQUENCE =
  'This ledger belongs to another recorded sequence than the loaded index. Reload the recorded sequence first.'
export const ADMISSION_STEP_RETIRED_NOTE = 'Its step was revoked unused; opening it shows the recorded step, not an operation.'
export const ADMISSION_STEP_UNRESOLVED_NOTE = 'Its step is held unresolved; opening it shows the recorded step, not an operation.'
