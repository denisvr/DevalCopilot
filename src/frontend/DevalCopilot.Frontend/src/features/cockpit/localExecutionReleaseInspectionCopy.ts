import type { LocalExecutionReleaseInspectionFailure } from './hooks/useLocalExecutionReleaseInspection'
import type { LocalExecutionReleaseInspectionProcessOutcome } from './localExecutionReleaseInspection'

// Fixed English copy of the recorded quarantine-release inspection (ADR-0067). Nothing here is taken from a host answer or an exception:
// each text is chosen from a closed code, so an unrecognized value can never be worded as a known one.

export const RELEASE_INSPECTION_REGION_LABEL = 'Inspect recorded quarantine release'
export const RELEASE_INSPECTION_HIDE_LABEL = 'Hide recorded quarantine release'
export const RELEASE_INSPECTION_RELOAD_LABEL = 'Reload recorded quarantine release'
export const RELEASE_INSPECTION_READING = 'Reading the recorded quarantine release of this operation…'

export const RELEASE_INSPECTION_NOTE = [
  'This reads the recorded Human decision, if one exists, to release this operation\'s retained ownership quarantine, together with the physical termination facts the original host recorded for it. It was read when you asked and offers no release action.',
  'A release is a past decision. It did not change this operation\'s failed result or the ended cycle, it does not show whether the workspace or capacity is available now, it approved nothing and it started nothing.',
  'Physical termination is evidence about the process the host invoked only. It says nothing about any descendant that process may have started.',
  'The record is internally consistent within one database read, not signed or authenticated history. The operation, the sequence and the live status above are read separately and are not one atomic replay.',
] as const

export const RELEASE_INSPECTION_FAILURES: Readonly<Record<LocalExecutionReleaseInspectionFailure, string>> = {
  read: 'The recorded quarantine release could not be read. Nothing was changed.',
  'not-found': 'The host no longer knows this run or step. Nothing was changed.',
  timeout: 'The host did not answer in time, so no recorded release is shown. Reload to read it again.',
  invalid: 'The host answered with a recorded release that is not coherent for this step, so none of it is shown.',
}

export const RELEASE_INSPECTION_NOT_RECORDED =
  'This read proved no recorded quarantine release for this step. That says nothing about whether a quarantine exists, whether the process was observed terminated, whether a release is possible, or whether this operation\'s ownership is held or was released. The live workspace and capacity readings show what is held.'

export const RELEASE_INSPECTION_UNAVAILABLE =
  'The recorded quarantine release of this step could not be proved as a whole, so none of it is shown. That does not say whether this operation\'s ownership is held or was released, and a receipt whose recorded checksum alone cannot be verified is shown this way too; it is neither inferred nor repaired here. The live workspace and capacity readings show what is held.'

export const RELEASE_INSPECTION_RECORDED_LEAD =
  'A Human released this operation\'s retained quarantine. The decision is recorded as one Human run event.'

export const RELEASE_INSPECTION_RESULT_NOTE =
  'The operation\'s recorded result below is unchanged by the release: it stays failed or stopped, and the cycle stays ended.'

export const RELEASE_INSPECTION_PHYSICAL_NOTE =
  'The original host observed the process it invoked terminated. This is the physical fact, distinct from the recorded result, and it covers the invoked process only.'

export const RELEASE_INSPECTION_OUTCOME_LABEL: Readonly<Record<LocalExecutionReleaseInspectionProcessOutcome, string>> = {
  TimedOut: 'Timed out (the host stopped it)',
  Cancelled: 'Cancelled (the host stopped it)',
}
