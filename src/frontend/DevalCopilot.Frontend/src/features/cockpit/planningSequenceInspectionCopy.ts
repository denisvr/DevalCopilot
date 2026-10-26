import type { PlanningSequenceInspectionFailure } from './hooks/usePlanningSequenceInspection'
import type { PlanningSequenceReasonCode } from './planningSequence'

// Fixed English copy of the recorded planning sequence inspection (ADR-0068). Nothing here is taken from a host answer or an exception:
// each text is chosen from a closed code. No host text is shown by this surface other than closed codes and identifiers, all rendered
// as literal text by the components.

export const PLANNING_INSPECTION_REGION_LABEL = 'Inspect recorded planning'
export const PLANNING_INSPECTION_HIDE_LABEL = 'Hide planning inspection'
export const PLANNING_INSPECTION_RELOAD_LABEL = 'Reload recorded planning'

export const PLANNING_INSPECTION_NOTE =
  'This is a recorded inspection: what the host stored for this run\'s planning sequence, read when you asked. A recorded state or link is '
  + 'bookkeeping, not proof that a plan was accepted, that an attempt ran or is running, that the source is current, or authority to do '
  + 'anything. It is not a coherent replay of every historical instant. While a sequence is still advancing, a later reload may show a later '
  + 'recorded result. This region has no control that arms, stops, implements, approves or publishes.'

export const PLANNING_INSPECTION_READING = 'Reading the recorded planning sequence…'
export const PLANNING_INSPECTION_NOT_RECORDED = 'No planning sequence is recorded for this run.'
export const PLANNING_INSPECTION_UNAVAILABLE =
  'The recorded planning sequence is present but its structure could not be proved, or it changed while it was being read, so none of it is shown. Reload to read it again.'

export const PLANNING_INSPECTION_FAILURES: Readonly<Record<PlanningSequenceInspectionFailure, string>> = {
  read: 'The recorded planning sequence could not be read.',
  'not-found': 'The host no longer knows this run.',
  invalid: 'The host answered with a recorded planning sequence that is not coherent for this run, so none of it is shown.',
}

export const PLANNING_STEP_FAILURES: Readonly<Record<PlanningSequenceInspectionFailure, string>> = {
  read: 'This step\'s recorded attempt association could not be read.',
  'not-found': 'This step is no longer available.',
  invalid: 'The host answered with an association that is not coherent for this step, so none of it is shown.',
}

export const PLANNING_STEP_NO_LINKED_ATTEMPT =
  'No attempt link is recorded for this step. That says only that no link was recorded; it does not show that preparation or execution did not happen, and a step that is not proved absent stays uncertain.'
export const PLANNING_STEP_UNAVAILABLE =
  'This step\'s recorded structure or linked attempt could not be proved, or the sequence changed while it was being read, so none of it is shown. This does not mean no attempt exists.'

export const PLANNING_STEP_AGENT_NOTE =
  'The linked Agent attempt is shown with its own recorded evidence below. That evidence describes this one attempt only; a link or a claim is not proof that it ran or finished, and an attempt without messages is still shown.'
export const PLANNING_STEP_INPUT_NOTE =
  'The Proposal this step names as its input is opened only on request, through the existing message inspector.'

export const PLANNING_RECORDED_REASON_PHRASE: Readonly<Record<PlanningSequenceReasonCode, string>> = {
  plan_accepted: 'recorded as plan accepted (a record, not a proof)',
  second_round_escalated: 'recorded as escalated after the second round',
  stopped_before_claim: 'recorded as stopped before the next step was claimed',
  stopped_after_step: 'recorded as stopped after the claimed step',
  preparation_refused: 'recorded as the next step claim refused',
  preparation_expired: 'recorded as the next step not claimed in time',
  preparation_failed: 'recorded as preparing the next step failed',
  preparation_unresolved: 'recorded as unresolved: whether the next step was claimed could not be confirmed',
  result_deadline_elapsed: 'recorded as the result deadline elapsed',
  step_failed: 'recorded as a claimed step not completing as expected',
  step_outcome_invalid: 'recorded as a step result that could not be proved',
  authority_changed: 'recorded as the pinned source or objective changed',
  host_restarted: 'recorded as the host restarted',
}

export const PLANNING_PINNED_NOTE =
  'This is the source the sequence was armed against. A later change of the workspace is not shown here and does not alter this record.'

export const PLANNING_NOT_RECORDED = 'Not recorded'
