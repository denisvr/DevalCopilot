import { derivePlanningSequenceControl, toPlanningSequenceStatus } from './planningSequence'
import type { PlanningSequenceState } from './planningSequence'
import type { UsePlanningSequenceResult } from './hooks/usePlanningSequence'

/**
 * Test-only builders of the parsed planning-sequence answers the host writes (ADR-0055). They write exactly the members the contract
 * names, with explicit nulls, and every field can be replaced through a patch to build a contradiction. They never write an answer the
 * production decoder would refuse unless the test patches it in.
 */
export const planningGuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`

export const SEQUENCE_ID = planningGuid(1)
export const WORKSPACE_ID = planningGuid(2)
export const CHECKPOINT_ID = planningGuid(3)
export const FINGERPRINT = 'ab'.repeat(32)

const at = (minute: number, second = 0) =>
  `2026-10-09T${String(10 + Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}:${String(second).padStart(2, '0')}.1234567Z`

export const ROLES = ['Planner', 'CriticalReviewer', 'Resolver', 'CriticalReviewer', 'Resolver'] as const
export const PROVIDERS = ['Codex', 'ClaudeCode', 'Codex', 'ClaudeCode', 'Codex'] as const

type Raw = Record<string, unknown>

/** One step. `reserved`: not yet claimed. `claimed`: linked to a running attempt. `concluded`: linked to a completed attempt. */
export function stepFor(ordinal: number, kind: 'reserved' | 'claimed' | 'concluded' | 'revoked' | 'unresolved', patch: Raw = {}): Raw {
  const deadline = `2026-10-09T10:${String(ordinal * 10 + 2).padStart(2, '0')}:00.1234567Z`
  const base: Raw = {
    stepId: planningGuid(100 + ordinal),
    ordinal,
    role: ROLES[ordinal],
    provider: PROVIDERS[ordinal],
    state: 'Reserved',
    reasonCode: null,
    attemptId: null,
    attemptNumber: null,
    attemptStatus: null,
    attemptOutcome: null,
    dispatched: null,
    preparationReservedAtUtc: `2026-10-09T10:${String(ordinal * 10).padStart(2, '0')}:00.1234567Z`,
    preparationDeadlineUtc: deadline,
    claimedAtUtc: null,
    resultDeadlineUtc: null,
  }
  switch (kind) {
    case 'claimed':
      return {
        ...base,
        state: 'Claimed',
        attemptId: planningGuid(200 + ordinal),
        attemptNumber: ordinal + 1,
        attemptStatus: 'Running',
        dispatched: false,
        claimedAtUtc: at(ordinal * 10, 30),
        resultDeadlineUtc: at(ordinal * 10 + 40),
        ...patch,
      }
    case 'concluded':
      return {
        ...base,
        state: 'Concluded',
        attemptId: planningGuid(200 + ordinal),
        attemptNumber: ordinal + 1,
        attemptStatus: 'Completed',
        attemptOutcome: ordinal === 0 ? 'Proposed' : ordinal % 2 === 1 ? 'Challenged' : 'Resolved',
        dispatched: true,
        claimedAtUtc: at(ordinal * 10, 30),
        resultDeadlineUtc: at(ordinal * 10 + 40),
        ...patch,
      }
    case 'revoked':
      return { ...base, state: 'Revoked', reasonCode: 'stopped_before_claim', ...patch }
    case 'unresolved':
      return { ...base, state: 'Unresolved', reasonCode: 'preparation_unresolved', ...patch }
    default:
      return { ...base, ...patch }
  }
}

const NEXT_ACTION: Record<PlanningSequenceState, [string, string]> = {
  Armed: ['wait_or_stop_after_current_step', 'Wait, or choose Stop after current step.'],
  PreparingStep: ['wait_or_stop_after_current_step', 'Wait, or choose Stop after current step.'],
  AwaitingStepResult: ['wait_or_stop_after_current_step', 'Wait, or choose Stop after current step.'],
  Stopping: ['wait_for_current_step_to_finish', 'Wait for the current step to finish.'],
  ReadyForImplementation: [
    'review_accepted_plan_then_request_implementation_separately',
    'Review the accepted plan. Implementation requires its own separate explicit request.',
  ],
  AwaitingHumanDecision: ['decide_on_the_recorded_escalation', 'Review the recorded escalation and decide separately.'],
  Stopped: ['continue_with_explicit_manual_work', 'Continue with explicit manual requests.'],
  NeedsAttention: ['inspect_and_reconcile', 'Inspect the recorded attempts and reconcile before any further work.'],
  Interrupted: ['inspect_and_reconcile', 'Inspect the recorded attempts and reconcile before any further work.'],
}

const REASON_TEXT: Record<string, string> = {
  plan_accepted: 'A critical review accepted the plan. Planning is complete; implementation needs its own separate explicit request.',
  second_round_escalated: 'The second challenge-resolution round finished and needs a Human decision.',
  stopped_before_claim: 'Stopped before the next step was claimed; nothing was claimed or consumed.',
  stopped_after_step: 'Stopped after the claimed step finished; no successor was started.',
  preparation_refused: "The next step's claim was refused; the sequence stopped without retrying.",
  preparation_expired: 'The next step was not claimed within two minutes of its reservation; nothing was claimed.',
  preparation_failed: 'Preparing the next step failed; nothing was retried.',
  preparation_unresolved: 'Whether the next step was claimed could not be confirmed; nothing was retried.',
  result_deadline_elapsed:
    "The result deadline elapsed. No process was cancelled; the step's attempt stays visible until it is reconciled.",
  step_failed: 'A claimed step did not complete as expected; no successor was started.',
  step_outcome_invalid: "A step's recorded result could not be proved; no successor was started.",
  authority_changed: 'The pinned source, workspace or objective changed; the sequence did not continue.',
  host_restarted: 'The host restarted; the sequence was interrupted and never resumes automatically.',
}

/** A sequence in `state`, with the steps and reason the contract records with it; patch any member to build a contradiction. */
export function sequenceFor(state: PlanningSequenceState, patch: Raw = {}): Raw {
  const live = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping'].includes(state)
  const defaults: Partial<Record<PlanningSequenceState, { steps: Raw[]; reason: string | null }>> = {
    Armed: { steps: [], reason: null },
    PreparingStep: { steps: [stepFor(0, 'reserved')], reason: null },
    AwaitingStepResult: { steps: [stepFor(0, 'claimed')], reason: null },
    Stopping: { steps: [stepFor(0, 'claimed')], reason: null },
    ReadyForImplementation: { steps: [stepFor(0, 'concluded'), stepFor(1, 'concluded', { attemptOutcome: 'Accepted' })], reason: 'plan_accepted' },
    AwaitingHumanDecision: {
      steps: [0, 1, 2, 3, 4].map((ordinal) => stepFor(ordinal, 'concluded')),
      reason: 'second_round_escalated',
    },
    Stopped: { steps: [stepFor(0, 'revoked')], reason: 'stopped_before_claim' },
    NeedsAttention: { steps: [stepFor(0, 'unresolved')], reason: 'preparation_unresolved' },
    Interrupted: { steps: [], reason: 'host_restarted' },
  }
  const chosen = defaults[state] ?? { steps: [], reason: null }
  const [nextActionCode, nextActionText] = NEXT_ACTION[state]
  const final =
    state === 'ReadyForImplementation'
      ? {
          finalProposalMessageId: planningGuid(300),
          acceptanceMessageId: planningGuid(301),
          escalationMessageId: null,
          provingAttemptId: planningGuid(201),
        }
      : state === 'AwaitingHumanDecision'
        ? {
            finalProposalMessageId: planningGuid(310),
            acceptanceMessageId: null,
            escalationMessageId: planningGuid(311),
            provingAttemptId: planningGuid(204),
          }
        : null
  // The linked attempt that is still Running is the outstanding one; it awaits reconciliation once the state holds no authority.
  const steps = (patch.steps as Raw[] | undefined) ?? chosen.steps
  const outstanding = (steps.find((step) => step.attemptStatus === 'Running')?.attemptId as string | undefined) ?? null
  const effective = (patch.state as string | undefined) ?? state
  return {
    sequenceId: SEQUENCE_ID,
    workspaceId: WORKSPACE_ID,
    checkpointId: CHECKPOINT_ID,
    fingerprintSha256: FINGERPRINT,
    state,
    recordedState: state,
    observedBeforePersisted: false,
    reasonCode: chosen.reason,
    reasonText: chosen.reason === null ? null : REASON_TEXT[chosen.reason],
    detailCode: null,
    nextActionCode,
    nextActionText,
    revision: String(1 + chosen.steps.length * 2),
    armedAtUtc: at(0),
    updatedAtUtc: at(30),
    stopRequestedAtUtc: state === 'Stopping' || state === 'Stopped' ? at(25) : null,
    endedAtUtc: live ? null : at(30),
    awaitingReconciliation: outstanding !== null && !['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping'].includes(effective),
    outstandingAttemptId: outstanding,
    maximumSteps: 5,
    steps: chosen.steps,
    final,
    ...patch,
  }
}

/** The status of a run with a sequence in `state`; the fence agrees with the recorded authority unless patched. */
export function statusFor(runId: string, state: PlanningSequenceState, patch: Raw = {}, sequencePatch: Raw = {}): Raw {
  const sequence = sequenceFor(state, sequencePatch)
  const steps = sequence.steps as Raw[]
  const live = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping'].includes(sequence.recordedState as string)
  const running = steps.some((step) => step.attemptStatus === 'Running')
  return {
    runId,
    status: sequence.state,
    sequence,
    arming: null,
    manualFence: live || running ? 'Blocked' : 'Released',
    latestEventSequence: '12',
    ...patch,
  }
}

/** A run with no sequence whose recorded admission facts name an exact candidate tuple. */
export const armableStatusFor = (runId: string, patch: Raw = {}, armingPatch: Raw = {}): Raw => ({
  runId,
  status: 'None',
  sequence: null,
  arming: {
    available: true,
    refusalCode: null,
    refusalText: null,
    workspaceId: WORKSPACE_ID,
    checkpointId: CHECKPOINT_ID,
    fingerprintSha256: FINGERPRINT,
    ...armingPatch,
  },
  manualFence: 'Released',
  latestEventSequence: '0',
  ...patch,
})

/** A run with no sequence whose arming is refused with a fixed code and text. */
export const refusedStatusFor = (runId: string, code = 'planning_sequence.run_not_pristine', text = 'Arming is refused for a fixed reason.'): Raw =>
  armableStatusFor(runId, {}, { available: false, refusalCode: code, refusalText: text, workspaceId: null, checkpointId: null, fingerprintSha256: null })

/** The host could not establish the fence (a stored value this build cannot read): no sequence, no arming, Unavailable. */
export const unavailableStatusFor = (runId: string): Raw => ({
  runId,
  status: 'None',
  sequence: null,
  arming: null,
  manualFence: 'Unavailable',
  latestEventSequence: '0',
})

export const armReceiptFor = (runId: string, patch: Raw = {}): Raw => ({
  sequenceId: SEQUENCE_ID,
  runId,
  workspaceId: WORKSPACE_ID,
  checkpointId: CHECKPOINT_ID,
  fingerprintSha256: FINGERPRINT,
  state: 'Armed',
  armedAtUtc: at(0),
  revision: '1',
  replayed: false,
  ...patch,
})

export const stopAnswerFor = (runId: string, patch: Raw = {}): Raw => ({
  sequenceId: SEQUENCE_ID,
  runId,
  state: 'Stopping',
  reasonCode: null,
  revision: '4',
  changed: true,
  ...patch,
})

/** The exact tuple the candidate and the receipts above carry. */
export const TUPLE = { workspaceId: WORKSPACE_ID, checkpointId: CHECKPOINT_ID, fingerprintSha256: FINGERPRINT }

/**
 * What `usePlanningSequence` returns for a validated answer of the run, for tests that replace the hook. The control is derived from
 * the decoded status exactly as the hook derives it; an answer that does not decode is a fixture error, never an unknown status.
 */
export function hookResultFor(raw: unknown, runId: string, refresh: () => void = () => undefined): UsePlanningSequenceResult {
  const status = toPlanningSequenceStatus(raw, runId)
  if (status === null) {
    throw new Error('The planning-sequence fixture is not a valid status.')
  }

  const control = derivePlanningSequenceControl(status)
  return { status, loading: false, failure: null, control, ownership: 'known', polling: false, refresh }
}

/** What `usePlanningSequence` returns while nothing is validated: unknown ownership. */
export const unknownHookResult = (refresh: () => void = () => undefined, loading = false): UsePlanningSequenceResult => ({
  status: null,
  loading,
  failure: loading ? null : 'read',
  control: 'unknown',
  ownership: 'unknown',
  polling: false,
  refresh,
})
