import type {
  LocalExecutionReleaseCandidate,
  LocalExecutionReleaseProcessOutcome,
  LocalExecutionReleaseReasonCode,
} from './localExecutionOwnershipRelease'
import { problemOf } from './planningSequenceCopy'

// Fixed English copy of the ownership release surface (ADR-0065). Nothing here is taken from a host answer or an exception: each text is
// chosen from a closed code, so an unrecognized value can never be worded as a known one.

export const RELEASE_REGION_LABEL = 'Release terminal operation quarantine'
export const RELEASE_HIDE_LABEL = 'Hide quarantine release'

export const RELEASE_EXPLANATION = [
  'A terminal operation that was dispatched but has no recorded process exit evidence stays owned and excluded. This window can release that retained quarantine only when the original host recorded, while it still owned the process, that the process it invoked was observed terminated, and only after you confirm it separately.',
  'Physical termination is evidence about the invoked process only. It is not proof about any descendant it may have started, not a new approval, not a result and not a retry.',
  'The release changes nothing else: the failed result, the ended cycle, the workspace and its source gates, approvals, artifacts and budgets stay exactly as recorded. A release does not restart or resume anything.',
  'Releasing may let already authorized work that was waiting, including a queued cycle, proceed, but only through its own fresh checks at the time it asks.',
] as const

export const RELEASE_SELECT_LABEL = 'Operations that still hold or were released from a quarantine'
export const RELEASE_NO_TARGETS = 'No operation of this cycle holds a retained quarantine.'
export const RELEASE_STATUS_UNKNOWN = 'The recorded status is not available right now, so no release can be read or confirmed. Nothing was sent.'
export const RELEASE_READING = 'Reading the recorded release evidence of the selected operation…'
export const RELEASE_READ_AGAIN_LABEL = 'Read the release evidence again'
export const RELEASE_CONFIRM_LABEL = 'Confirm and release quarantine'
export const RELEASE_SUBMITTING_LABEL = 'Releasing…'
export const RELEASE_CLEAR_LABEL = 'Clear selection'

export const RELEASE_READ_FAILURES = {
  read: 'The release evidence could not be read. Nothing was changed.',
  'not-found': 'The host no longer knows this run or operation. Nothing was changed.',
  invalid: 'The host answered with release evidence that is not coherent for this operation, so none of it is shown and nothing can be confirmed.',
} as const

export const RELEASE_EXPIRED =
  'The confirmation expired and was discarded. Nothing was sent. Read the release evidence again to review it and confirm.'

export const RELEASE_UNKNOWN_OUTCOME =
  'The outcome is unknown: the host may have recorded the release. Nothing was resent and no rollback is assumed. The recorded status is being read again; read the release evidence again to see what was recorded.'

export const RELEASE_BUSY = 'Another local execution request is still being confirmed. Nothing was sent.'

export const RELEASE_REASON_COPY: Readonly<Record<LocalExecutionReleaseReasonCode, string>> = {
  step_not_linked: 'This step names no operation, so there is nothing to release.',
  operation_not_terminal: 'This operation is still recorded as running. Nothing about a running operation is released.',
  not_dispatched: 'This operation was never dispatched, so it never owned the workspace.',
  already_free: 'This operation already has recorded process exit evidence, so it already releases the workspace without a Human release.',
  sequence_not_ended: 'The cycle has not ended in a stopped, interrupted or needs-attention state, so no quarantine is released.',
  evidence_missing:
    'The original host did not record that it observed this operation\'s process terminated. There is no supported recovery action; the live workspace and capacity readings show what is held.',
  evidence_incoherent:
    'The recorded termination evidence of this operation is incomplete, foreign or contradicts the operation, so it cannot be trusted and this answer says nothing about whether ownership is held or was released. There is no supported recovery action; the live workspace and capacity readings show what is held.',
}

export const RELEASE_OUTCOME_LABEL: Readonly<Record<LocalExecutionReleaseProcessOutcome, string>> = {
  TimedOut: 'Timed out (the host stopped it)',
  Cancelled: 'Cancelled (the host stopped it)',
}

/** The fixed lines that say what confirming one candidate does and does not do. */
export function describeReleaseConfirmation(candidate: LocalExecutionReleaseCandidate): string[] {
  return [
    `Confirming records that the original host observed the invoked process of this ${candidate.operationFamily === 'Verification' ? 'verification run' : 'agent attempt'} terminated, and releases its retained quarantine.`,
    `The recorded result stays "${candidate.operationStatus}" with process outcome ${RELEASE_OUTCOME_LABEL[candidate.processOutcome].toLowerCase()}; it is not rewritten, and the ended cycle stays ended.`,
    'This confirmation is an exact-intent check bound to the evidence shown below. It is not a credential, not proof about descendants and it expires.',
  ]
}

export const RELEASED_LINE =
  'Released. The retained quarantine of this operation was released by a Human confirmation. The failed result and the ended cycle are unchanged.'

export const RELEASED_ALREADY_LINE =
  'This operation\'s quarantine was already released by a recorded Human confirmation. Nothing is changed by reading it again.'

const REFUSAL_COPY: Readonly<Record<string, string>> = {
  'local_execution_ownership_release.confirmation_stale':
    'The confirmation expired, or the recorded facts changed after you read them. It was consumed and nothing was released; read the release evidence again.',
  'local_execution_ownership_release.not_eligible': 'This operation no longer retains a quarantine this control may release. Nothing was released.',
  'local_execution_ownership_release.unavailable':
    'The recorded original-host termination evidence is missing, incomplete or cannot be trusted. This request recorded nothing; the live workspace and capacity readings show what is held.',
  'local_execution_ownership_release.confirmation_conflict':
    'This operation\'s quarantine was already released under a different confirmation. Read the release evidence again to see the recorded release.',
  'local_execution_ownership_release.not_found': 'The host no longer knows this run or operation. Nothing was released.',
  'local_execution_ownership_release.invalid_request': 'The request was not accepted by the host. Nothing was released.',
  'local_execution_ownership_release.body_too_large': 'The request was not accepted by the host. Nothing was released.',
}

const GENERIC_REFUSAL = 'The release request could not be completed. Nothing was released. The recorded status is being read again.'

export type LocalExecutionReleaseFailure = { kind: 'unknown' } | { kind: 'refused'; code: string | null; message: string }

/** Raised by the action when the host's success answer is not exactly the Released answer for the request: the host may have recorded it. */
export class LocalExecutionReleaseAnswerInvalid extends Error {
  constructor() {
    super('The ownership release answer was not valid for the request.')
    this.name = 'LocalExecutionReleaseAnswerInvalid'
  }
}

/**
 * A failure with no HTTP response, an aborted or timed-out request, an answer that failed validation or a 5xx has an unknown outcome:
 * the host may have recorded the release, so the status is read again and the Human reads the evidence again. Any other HTTP answer is
 * a definite refusal worded from its stable code (or its status) with fixed copy; server text is never echoed.
 */
export function classifyLocalExecutionReleaseFailure(caught: unknown): LocalExecutionReleaseFailure {
  const problem = problemOf(caught)
  if (problem === null || problem.status === undefined || problem.status >= 500 || problem.status < 100) {
    return { kind: 'unknown' }
  }

  if (problem.code !== null && Object.hasOwn(REFUSAL_COPY, problem.code)) {
    return { kind: 'refused', code: problem.code, message: REFUSAL_COPY[problem.code] }
  }

  return { kind: 'refused', code: problem.code, message: GENERIC_REFUSAL }
}
