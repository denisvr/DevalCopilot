import { ApiException, ApiProblemDetails } from '../../api/generated/api-client'
import type { GlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import type {
  PlanningSequenceControl,
  PlanningSequenceDetails,
  PlanningSequenceProvider,
  PlanningSequenceReasonCode,
  PlanningSequenceRole,
  PlanningSequenceState,
  PlanningSequenceStepState,
} from './planningSequence'

// Fixed English copy of the planning-sequence surface (ADR-0055). Nothing here is taken from a host answer, a payload or an exception:
// each text is chosen from a closed code, so an unrecognized value can never be worded as a known one.

export const PLANNING_SEQUENCE_STOP_LABEL = 'Stop after current step'
export const PLANNING_SEQUENCE_ARM_LABEL = 'Arm planning sequence'

export const PLANNING_SEQUENCE_ARM_EXPLANATION: readonly string[] = [
  'Arming lets the host claim up to five read-only steps one after another without further requests from you: Planner (Codex), Critical review (Claude Code), Resolver (Codex) and, only when a review challenges the plan, a second Critical review (Claude Code) and a second Resolver (Codex).',
  'The second review is included in this authorization. Acceptance by a review ends planning; a challenge after the second Resolver ends planning at a Human decision.',
  'Implementation is a separate explicit decision. This control never starts it, and a finished plan grants no implementation approval.',
  'Arming starts no provider and consumes no budget. The host keeps advancing the sequence if this window is closed; use Stop after current step to prevent a successor step.',
]

export const PLANNING_SEQUENCE_READING = 'Reading the planning sequence status…'
export const PLANNING_SEQUENCE_UNAVAILABLE =
  'The planning sequence status could not be read or validated. Arming and every manual request for this run are withheld until it can.'
export const PLANNING_SEQUENCE_STOP_NOTE =
  'Stop after current step is not process cancellation, run Stop or Pause: a step that is already claimed may still finish, and only successor steps are prevented.'
export const PLANNING_SEQUENCE_NEVER_RESUMES = 'A sequence is never resumed, rearmed or retried by this window; later work is an explicit manual request.'
export const PLANNING_SEQUENCE_UNKNOWN_OUTCOME =
  'The outcome of the request is unknown: the host may have recorded it. The recorded status is being read again and nothing was resent.'

const BLOCKED_NOTE =
  'Withheld: a planning sequence holds automatic authority over this run or its workspace. Use Stop after current step and wait for it to drain.'
const UNKNOWN_NOTE =
  'Withheld until the planning sequence status is confirmed: whether a planning sequence holds automatic authority over this run could not be established.'

/** The fixed reason a manual mutation of the run is withheld by the planning-sequence fence, or null when this fence withholds nothing. */
export function describePlanningSequenceWithholding(control: PlanningSequenceControl): string | null {
  return control === 'unknown' ? UNKNOWN_NOTE : control === 'blocked' ? BLOCKED_NOTE : null
}

/**
 * The manual-action veto of the fence in the shape every Agent claim control already honours. It is only ever a veto: `released` and
 * `none` return null, which lifts this fence and enables nothing the ordinary gates disable.
 */
export function derivePlanningSequenceClaimBlock(control: PlanningSequenceControl): GlobalAgentClaimBlock | null {
  return control === 'unknown'
    ? { reason: 'PlanningSequenceStatusUnknown' }
    : control === 'blocked'
      ? { reason: 'PlanningSequenceAutomaticAuthority' }
      : null
}

export const PLANNING_SEQUENCE_CLAIM_BLOCK_MESSAGES = { PlanningSequenceAutomaticAuthority: BLOCKED_NOTE, PlanningSequenceStatusUnknown: UNKNOWN_NOTE } as const

export const PLANNING_SEQUENCE_STATE_LABEL: Readonly<Record<PlanningSequenceState, string>> = {
  Armed: 'Armed',
  PreparingStep: 'Preparing the next step',
  AwaitingStepResult: 'Awaiting a step result',
  Stopping: 'Stopping after the current step',
  ReadyForImplementation: 'Planning complete',
  AwaitingHumanDecision: 'Awaiting a Human decision',
  Stopped: 'Stopped',
  NeedsAttention: 'Needs attention',
  Interrupted: 'Interrupted',
}

export const PLANNING_SEQUENCE_ROLE_LABEL: Readonly<Record<PlanningSequenceRole, string>> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical review',
  Resolver: 'Resolver',
}

export const PLANNING_SEQUENCE_PROVIDER_LABEL: Readonly<Record<PlanningSequenceProvider, string>> = {
  Codex: 'Codex',
  ClaudeCode: 'Claude Code',
}

export const PLANNING_SEQUENCE_STEP_STATE_LABEL: Readonly<Record<PlanningSequenceStepState, string>> = {
  Reserved: 'Reserved, not yet claimed',
  Claimed: 'Claimed',
  Concluded: 'Concluded',
  Revoked: 'Revoked before any claim',
  Unresolved: 'Unresolved: not proved claimed or absent',
}

/** The exact disclosure of the attempt outcome a sequence records when it no longer authorized dispatching a claimed step (ADR-0055). */
export const PLANNING_SEQUENCE_DISPATCH_REFUSED_LABEL =
  'Not started: the planning sequence no longer authorized dispatching it (no provider was invoked, no output recorded, no budget refunded)'

export const PLANNING_SEQUENCE_DEADLINE_ELAPSED_NOTE = 'Deadline elapsed, not yet persisted by the host.'

/** The fixed statement of what a state means; the host's own reason and next-action texts are shown beside it, as literal text. */
export function describePlanningSequenceState(sequence: PlanningSequenceDetails): string {
  const outstanding = sequence.steps.some((step) => step.state === 'Claimed')
  switch (sequence.state) {
    case 'Armed':
      return 'Armed: nothing is reserved yet. No step has been claimed and no provider was started.'
    case 'PreparingStep':
      return 'Preparing the next step: it is reserved but not yet claimed. Nothing has been dispatched for it.'
    case 'AwaitingStepResult':
      return outstanding
        ? 'A step is claimed, awaiting its result. A recorded claim or dispatch marker is not proof that a provider process is running.'
        : 'Between steps: the previous step is concluded and the next one is not yet reserved.'
    case 'Stopping':
      return 'Stopping: Stop after current step was requested. The already claimed step may still finish; no successor will be started.'
    case 'ReadyForImplementation':
      return 'Planning complete: an accepted plan is recorded. No implementation or current-source approval is granted; implementation is a separate explicit request.'
    case 'AwaitingHumanDecision':
      return 'Planning ended at a recorded escalation and awaits a Human decision. Nothing is approved for implementation.'
    case 'Stopped':
      return 'Stopped: no successor step will be started.'
    case 'NeedsAttention':
      return 'Needs attention: automatic advancement is closed and nothing is retried.'
    case 'Interrupted':
      return 'Interrupted by a host restart. It never resumes automatically.'
  }
}

/** The short fixed clause the run event summary uses for a closed reason code. */
export const PLANNING_SEQUENCE_REASON_PHRASE: Readonly<Record<PlanningSequenceReasonCode, string>> = {
  plan_accepted: 'a critical review accepted the plan',
  second_round_escalated: 'the second challenge-resolution round needs a Human decision',
  stopped_before_claim: 'stopped before the next step was claimed',
  stopped_after_step: 'stopped after the claimed step finished',
  preparation_refused: 'the next step claim was refused',
  preparation_expired: 'the next step was not claimed in time',
  preparation_failed: 'preparing the next step failed',
  preparation_unresolved: 'whether the next step was claimed could not be confirmed',
  result_deadline_elapsed: 'the result deadline elapsed',
  step_failed: 'a claimed step did not complete as expected',
  step_outcome_invalid: 'a recorded step result could not be proved',
  authority_changed: 'the pinned source or objective changed',
  host_restarted: 'the host restarted',
}

const FAILURE_COPY: Readonly<Record<string, string>> = {
  'planning_sequence.not_found': 'This planning sequence was not found for this run.',
  'planning_sequence.run_not_manual': 'Only a manual Agent run can be armed for a planning sequence.',
  'planning_sequence.run_not_pristine':
    'Only a created run with no attempt, collaboration message or local commit can arm a planning sequence.',
  'planning_sequence.project_busy':
    'An attempt, verification, local commit or workspace of this project is active or in an unknown state, so nothing was armed.',
  'planning_sequence.workspace_not_ready': 'The latest isolated workspace of this project must be ready and owned to arm a planning sequence.',
  'planning_sequence.lease_not_active': 'An active workspace lease is required to arm a planning sequence.',
  'planning_sequence.checkpoint_missing': 'A current Git checkpoint is required to arm a planning sequence.',
  'planning_sequence.tuple_stale':
    'The workspace, checkpoint and fingerprint shown are no longer the current source of this project. Nothing was armed; the status is being read again.',
  'planning_sequence.tuple_conflict': 'This run was already armed for a different workspace, checkpoint or fingerprint. Nothing was changed.',
  'planning_sequence.manual_control_blocked':
    'A planning sequence still holds automatic authority; use Stop after current step and wait for it to drain.',
  'planning_sequence.invalid_request': 'The request was not accepted by the host.',
  'planning_sequence.body_too_large': 'The request was not accepted by the host.',
  'planning_sequence.body_not_allowed': 'The request was not accepted by the host.',
  'planning_sequence.persistence_failed': 'The planning sequence change could not be recorded. The status is being read again.',
}

const GENERIC_REFUSAL = 'The planning sequence request could not be completed. The status is being read again.'

/** A definite refusal with its fixed copy, or an unknown outcome the recorded status must reconcile; a request is never resent. */
export type PlanningSequenceRequestFailure = { kind: 'unknown' } | { kind: 'refused'; code: string | null; message: string }

/** Raised by an action when the host's success answer is not exactly the answer for the request: the host may still have recorded it. */
export class PlanningSequenceAnswerInvalid extends Error {
  constructor() {
    super('The planning sequence answer was not valid for the request.')
    this.name = 'PlanningSequenceAnswerInvalid'
  }
}

export function problemOf(caught: unknown): { status: number | undefined; code: string | null } | null {
  if (caught instanceof ApiProblemDetails) {
    const code = caught.errors?.[0]?.code
    return { status: typeof caught.status === 'number' ? caught.status : undefined, code: typeof code === 'string' ? code : null }
  }

  if (ApiException.isApiException(caught)) {
    const exception = caught as ApiException
    try {
      const parsed = JSON.parse(exception.response) as { errors?: { code?: unknown }[] }
      const code = parsed.errors?.[0]?.code
      return { status: exception.status, code: typeof code === 'string' ? code : null }
    } catch {
      return { status: exception.status, code: null }
    }
  }

  return null
}

/**
 * A failure with no HTTP response, an answer that failed validation or a 5xx has an unknown outcome: the host may have recorded the
 * request, so only the recorded status is read again. Any other HTTP answer is a definite refusal worded from its stable code (or its
 * status) with fixed copy; server text is never echoed.
 */
export function classifyPlanningSequenceFailure(caught: unknown): PlanningSequenceRequestFailure {
  const problem = problemOf(caught)
  if (problem === null || problem.status === undefined || problem.status >= 500 || problem.status < 100) {
    return { kind: 'unknown' }
  }

  if (problem.code !== null && Object.hasOwn(FAILURE_COPY, problem.code)) {
    return { kind: 'refused', code: problem.code, message: FAILURE_COPY[problem.code] }
  }

  switch (problem.status) {
    case 400:
    case 413:
      return { kind: 'refused', code: problem.code, message: 'The request was not accepted by the host.' }
    case 404:
      return { kind: 'refused', code: problem.code, message: 'This run or planning sequence could not be found.' }
    case 409:
      return { kind: 'refused', code: problem.code, message: 'The planning sequence changed or the request can no longer be accepted. The status is being read again.' }
    default:
      return { kind: 'refused', code: problem.code, message: GENERIC_REFUSAL }
  }
}

/** The fixed message of a failed planning-sequence action; an unknown outcome and a refusal are worded differently. */
export const planningSequenceFailureMessage = (caught: unknown): string => {
  const failure = classifyPlanningSequenceFailure(caught)
  return failure.kind === 'unknown' ? PLANNING_SEQUENCE_UNKNOWN_OUTCOME : failure.message
}

const EVENT_STATE_PHRASE: Readonly<Record<PlanningSequenceState, string>> = {
  Armed: 'armed',
  PreparingStep: 'preparing a step',
  AwaitingStepResult: 'awaiting a step result',
  Stopping: 'stopping after the current step',
  ReadyForImplementation: 'planning complete (ready for a separate implementation decision)',
  AwaitingHumanDecision: 'awaiting a Human decision',
  Stopped: 'stopped',
  NeedsAttention: 'needs attention',
  Interrupted: 'interrupted',
}

export const PLANNING_SEQUENCE_EVENT_SUMMARIES = {
  armed: 'A Human armed a read-only planning sequence. Arming started no provider and consumed no budget.',
  step_reserved: 'The host reserved the next planning step. A reservation is not a claim and consumes no budget.',
  step_claimed: 'The host claimed a planning step. A claim is not proof that a provider process is running.',
  step_concluded: 'The host recorded the end of a claimed planning step.',
  stop_requested: 'A Human chose Stop after current step. This is not process cancellation, run Stop or Pause.',
  ended: 'The planning sequence ended.',
} as const

/** The closed phrase of a recorded sequence state, for the event summary only. */
export const describePlanningSequenceEventState = (state: PlanningSequenceState): string => EVENT_STATE_PHRASE[state]
