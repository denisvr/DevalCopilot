import type { LocalExecutionQueueInspectionFailure } from './hooks/useLocalExecutionQueueInspection'

// Fixed English copy of the recorded outstanding queue-admission inspection (ADR-0064). Nothing here is taken from a host answer or an
// exception: each text is chosen from a closed code. A project name is the only untrusted text and is rendered inert by the component.

export const QUEUE_OVERVIEW_REGION_LABEL = 'Inspect outstanding queue admissions'
export const QUEUE_OVERVIEW_HIDE_LABEL = 'Hide outstanding queue admissions'
export const QUEUE_OVERVIEW_RELOAD_LABEL = 'Reload outstanding queue admissions'
export const QUEUE_OVERVIEW_SELECT_LABEL = 'Select recorded cycle'
export const QUEUE_OVERVIEW_CLEAR_LABEL = 'Clear selected recorded cycle'

export const QUEUE_OVERVIEW_NOTE =
  'This is a recorded snapshot of the outstanding admissions of every queued cycle, read when you asked: each Waiting, Reserved or '
  + 'Unresolved admission, across all registered projects and whatever project is selected. It is not every active cycle (a cycle whose '
  + 'latest admission is already consumed is not listed), an exact queue position, current eligibility, process activity, free capacity '
  + 'or a promised start time. A recorded deadline that has passed is still only a record: this view ends, releases and changes nothing, '
  + 'and nothing here starts, orders, stops, retries or releases anything.'

export const QUEUE_OVERVIEW_READING = 'Reading the recorded outstanding queue admissions…'
export const QUEUE_OVERVIEW_EMPTY =
  'No Waiting, Reserved or Unresolved admission is recorded. This does not mean no cycle is active, no execution owner exists or any capacity is free.'
export const QUEUE_OVERVIEW_UNAVAILABLE =
  'The recorded outstanding admissions could not be proved as a whole, so none of them are shown. Reload to read them again.'

export const QUEUE_OVERVIEW_FAILURES: Readonly<Record<LocalExecutionQueueInspectionFailure, string>> = {
  read: 'The recorded outstanding queue admissions could not be read.',
  timeout: 'The host did not answer in time, so no outstanding queue admissions are shown. Reload to read them again.',
  invalid: 'The host answered with outstanding queue admissions that are not coherent, so none of them are shown.',
}

export const QUEUE_OVERVIEW_SELECTION_NOTE =
  'The existing recorded inspector of this cycle follows. Selecting it read nothing: opening it, its ledger, a step or a window are separate explicit actions.'
