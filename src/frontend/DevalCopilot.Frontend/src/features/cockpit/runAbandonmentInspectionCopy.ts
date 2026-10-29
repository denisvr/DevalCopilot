import type { RunAbandonmentInspectionFailure } from './hooks/useRunAbandonmentInspection'

// Fixed English copy of the recorded abandonment inspection (ADR-0071). Nothing here is taken from a host answer or an exception: each
// text is chosen from a closed code. Every description is historical (what the host recorded), never a live claim about eligibility,
// ownership, the workspace, another objective or publication. The common notes are shown for every state of an open region (reading,
// failed, not recorded, unavailable, recorded), so they assert nothing about this run; a positive statement that the run was abandoned
// and that the shown facts agree belongs only to the Recorded note, and Unavailable asserts neither that a decision exists nor that none does.

export const RUN_ABANDONMENT_INSPECTION_REGION_LABEL = 'Inspect recorded abandonment'
export const RUN_ABANDONMENT_INSPECTION_HIDE_LABEL = 'Hide recorded abandonment'
export const RUN_ABANDONMENT_INSPECTION_RELOAD_LABEL = 'Reload recorded abandonment'
export const RUN_ABANDONMENT_INSPECTION_READING = 'Reading what the host recorded for this run…'

export const RUN_ABANDONMENT_INSPECTION_NOTE = [
  'This read shows a Human abandonment only if the host recorded one for this run and every fact of it can be proved, read when you asked. It offers no abandon, cancel, repair, release, retry, intake, approval, commit, publication or export action.',
  'Whatever it answers, it proves no cancellation, workspace repair, ownership release, current permission to record another objective, or publication.',
  'This read and the independently loaded history headers are separate reads, not one atomic replay.',
] as const

export const RUN_ABANDONMENT_INSPECTION_FAILURES: Readonly<Record<RunAbandonmentInspectionFailure, string>> = {
  read: 'This read failed, so nothing is shown. Nothing was changed.',
  'not-found': 'The host no longer knows this run. Nothing was changed.',
  timeout: 'The host did not answer in time, so nothing is shown. Reload to read it again.',
  invalid: 'The host answered with something that is not coherent for this run, so none of it is shown.',
}

export const RUN_ABANDONMENT_INSPECTION_NOT_RECORDED =
  'This read found no recorded abandonment for this run. That says nothing about whether this run can be abandoned now, how it ended, or whether another objective may be recorded.'

export const RUN_ABANDONMENT_INSPECTION_UNAVAILABLE =
  'The facts this read needs for this run could not be proved as a whole, so nothing is shown. That says neither that an abandonment was recorded nor that none was, nor how the run ended, and nothing is diagnosed, repaired or recovered here.'

export const RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE =
  'A Human ended this run by explicit abandonment, recorded with the reason and time below and supported by the one run event identified here. The abandonment ended the objective of this run without successful completion. These facts agree with each other within one database read; they are not signed or authenticated history.'
