import { deriveLocalExecutionSequenceControl, toLocalExecutionSequenceStatus } from './localExecutionSequence'
import type { LocalExecutionSequenceStateName } from './localExecutionSequence'
import type { UseLocalExecutionSequenceResult } from './hooks/useLocalExecutionSequence'

/**
 * Test-only builders of the parsed local-execution-sequence answers the host writes (ADR-0060). They write exactly the members the
 * contract names, with explicit nulls, and every field can be replaced through a patch to build a contradiction. They never write an
 * answer the production decoder would refuse unless the test patches it in. One pinned recipe is the default, so a sequence has at most
 * nine steps.
 */
export const localGuid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`

export const LOCAL_SEQUENCE_ID = localGuid(1)
export const LOCAL_WORKSPACE_ID = localGuid(2)
export const LOCAL_CHECKPOINT_ID = localGuid(3)
export const LOCAL_PLAN_ID = localGuid(4)
export const LOCAL_FINGERPRINT = 'ab'.repeat(32)
export const LOCAL_CONFIRMATION = 'cd'.repeat(32)

const at = (minute: number, second = 0) =>
  `2026-10-09T${String(10 + Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}:${String(second).padStart(2, '0')}.1234567Z`

type Raw = Record<string, unknown>

const LIVE = ['Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping']

const DEFAULT_KINDS = ['Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis']

export type StepShape = 'reserved' | 'claimed' | 'concluded' | 'revoked' | 'unresolved' | 'quarantined'

/** One step. Ordinal 0 is the implementation, 1 the verification of the one recipe, 2 the code review and 3 a diagnosis. */
export function localStepFor(ordinal: number, shape: StepShape, patch: Raw = {}, kind: string = DEFAULT_KINDS[Math.min(ordinal, 3)]): Raw {
  const agent = kind !== 'Verification'
  const base: Raw = {
    stepId: localGuid(100 + ordinal),
    ordinal,
    kind,
    passNumber: kind === 'Implementation' ? 0 : 1,
    recipeOrdinal: agent ? null : 0,
    recipeName: agent ? null : 'Unit tests',
    state: 'Reserved',
    reasonCode: null,
    operationId: null,
    operationStatus: null,
    operationOutcome: null,
    dispatched: null,
    exitRecorded: null,
    quarantined: false,
    preparationReservedAtUtc: `2026-10-09T10:${String(ordinal * 10).padStart(2, '0')}:00.1234567Z`,
    preparationDeadlineUtc: `2026-10-09T10:${String(ordinal * 10 + 2).padStart(2, '0')}:00.1234567Z`,
    claimedAtUtc: null,
    resultDeadlineUtc: null,
  }
  const linked = { operationId: localGuid(200 + ordinal), claimedAtUtc: at(ordinal * 10, 30), resultDeadlineUtc: at(ordinal * 10 + 40) }
  switch (shape) {
    case 'claimed':
      return { ...base, ...linked, state: 'Claimed', operationStatus: 'Running', dispatched: false, exitRecorded: false, ...patch }
    case 'concluded':
      return {
        ...base,
        ...linked,
        state: 'Concluded',
        operationStatus: agent ? 'Completed' : 'Passed',
        operationOutcome: agent ? (kind === 'CodeReview' ? 'ReviewApproved' : kind === 'VerificationDiagnosis' ? 'DiagnosisEscalated' : 'Implemented') : 'Exited',
        dispatched: true,
        exitRecorded: true,
        ...patch,
      }
    case 'quarantined':
      return {
        ...base,
        ...linked,
        state: 'Concluded',
        operationStatus: agent ? 'Interrupted' : 'Interrupted',
        operationOutcome: null,
        dispatched: true,
        exitRecorded: false,
        quarantined: true,
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

const NEXT_ACTION: Record<string, [string, string]> = {
  wait: ['wait_or_stop_after_current_operation', 'Wait, or choose Stop after current operation.'],
  drain: ['wait_for_current_operation_to_finish', 'Wait for the current operation to finish.'],
  approval: [
    'review_and_decide_on_human_approval_separately',
    'Review the approved report. Human approval and commit each need their own separate explicit request.',
  ],
  escalation: ['decide_on_the_recorded_escalation', 'Review the recorded escalation and decide separately.'],
  manual: ['continue_with_explicit_manual_work', 'Continue with explicit manual requests.'],
  inspect: ['inspect_and_reconcile', 'Inspect the recorded operations and reconcile before any further work.'],
  recovery: ['no_supported_recovery_action_in_this_version', 'No supported recovery action in this version.'],
}

const REASON_TEXT: Record<string, string> = {
  review_approved: 'An ordinary review approved the exact linked report. The cycle is complete; Human approval and commit remain separate.',
  allowance_reached: 'The automatic correction allowance was reached with unresolved findings; a Human decision is required.',
  diagnosis_escalated: 'The verification diagnosis escalated; a Human decision is required.',
  stopped_before_claim: 'Stopped before the next operation was claimed; nothing was claimed or consumed.',
  stopped_after_operation: 'Stopped after the claimed operation finished; no successor was started.',
  preparation_unresolved: 'Whether the next operation was claimed could not be confirmed; nothing was retried.',
  result_deadline_elapsed: 'The result deadline elapsed. No process was cancelled; the operation stays visible and owned until it is reconciled.',
  exit_evidence_missing:
    'A dispatched operation ended without recorded process exit evidence. Its exact operation stays owned: no supported recovery action in this version.',
  host_restarted: 'The host restarted; the sequence was interrupted and never resumes automatically.',
}

const isOutstanding = (step: Raw) => step.operationId !== null && (step.operationStatus === 'Running' || step.quarantined === true)

/** A sequence in `state` with the steps and reason the contract records with it; patch any member to build a contradiction. */
export function localSequenceFor(state: LocalExecutionSequenceStateName, patch: Raw = {}): Raw {
  const live = LIVE.includes(state)
  const defaults: Record<string, { steps: Raw[]; reason: string | null; next: string }> = {
    Armed: { steps: [], reason: null, next: 'wait' },
    PreparingStep: { steps: [localStepFor(0, 'reserved')], reason: null, next: 'wait' },
    AwaitingStepResult: { steps: [localStepFor(0, 'claimed')], reason: null, next: 'wait' },
    Stopping: { steps: [localStepFor(0, 'claimed')], reason: null, next: 'drain' },
    ReadyForHumanApproval: {
      steps: [localStepFor(0, 'concluded'), localStepFor(1, 'concluded'), localStepFor(2, 'concluded')],
      reason: 'review_approved',
      next: 'approval',
    },
    HumanEscalation: {
      steps: [localStepFor(0, 'concluded'), localStepFor(1, 'concluded'), localStepFor(2, 'concluded', {}, 'VerificationDiagnosis')],
      reason: 'diagnosis_escalated',
      next: 'escalation',
    },
    Stopped: { steps: [localStepFor(0, 'revoked')], reason: 'stopped_before_claim', next: 'manual' },
    NeedsAttention: { steps: [localStepFor(0, 'unresolved')], reason: 'preparation_unresolved', next: 'inspect' },
    Interrupted: { steps: [], reason: 'host_restarted', next: 'inspect' },
  }
  const chosen = defaults[state]
  const steps = (patch.steps as Raw[] | undefined) ?? chosen.steps
  const counted = steps.filter((step) => step.state !== 'Revoked')
  const outstanding = (steps.find(isOutstanding)?.operationId as string | undefined) ?? null
  const effective = (patch.state as string | undefined) ?? state
  const awaitingReconciliation = outstanding !== null && !LIVE.includes(effective)
  const [nextActionCode, nextActionText] = awaitingReconciliation && !LIVE.includes(effective) ? NEXT_ACTION.recovery : NEXT_ACTION[chosen.next]
  const final =
    state === 'ReadyForHumanApproval'
      ? { reportMessageId: localGuid(300), reviewAttemptId: localGuid(202), escalationMessageId: null }
      : state === 'HumanEscalation'
        ? { reportMessageId: localGuid(310), reviewAttemptId: null, escalationMessageId: localGuid(311) }
        : null
  return {
    sequenceId: LOCAL_SEQUENCE_ID,
    planProposalMessageId: LOCAL_PLAN_ID,
    planAuthority: 'AcceptedRoot',
    workspaceId: LOCAL_WORKSPACE_ID,
    checkpointId: LOCAL_CHECKPOINT_ID,
    fingerprintSha256: LOCAL_FINGERPRINT,
    currentCheckpointId: LOCAL_CHECKPOINT_ID,
    currentFingerprintSha256: LOCAL_FINGERPRINT,
    state,
    recordedState: state,
    observedBeforePersisted: false,
    reasonCode: chosen.reason,
    reasonText: chosen.reason === null ? null : REASON_TEXT[chosen.reason],
    detailCode: null,
    nextActionCode,
    nextActionText,
    revision: String(1 + steps.length * 2),
    recipeCount: 1,
    maximumSteps: 9,
    correctionAllowance: 2,
    correctionsClaimed: counted.filter((step) => step.kind === 'ReviewCorrection' || step.kind === 'DiagnosisCorrection').length,
    passesStarted: new Set(counted.filter((step) => step.kind === 'Verification').map((step) => step.passNumber)).size,
    agentClaims: counted.filter((step) => step.kind !== 'Verification').length,
    armedAtUtc: at(0),
    advancementDeadlineUtc: at(120),
    updatedAtUtc: at(30),
    stopRequestedAtUtc: state === 'Stopping' || state === 'Stopped' ? at(25) : null,
    endedAtUtc: live ? null : at(30),
    awaitingReconciliation,
    outstandingOperationId: outstanding,
    steps,
    final,
    ...patch,
  }
}

/** The status of a run with a sequence in `state`; the fence agrees with the recorded authority unless patched. */
export function localStatusFor(runId: string, state: LocalExecutionSequenceStateName, patch: Raw = {}, sequencePatch: Raw = {}): Raw {
  const sequence = localSequenceFor(state, sequencePatch)
  const steps = sequence.steps as Raw[]
  const blocked =
    LIVE.includes(sequence.recordedState as string) || steps.some((step) => step.state === 'Reserved' || step.state === 'Unresolved' || isOutstanding(step))
  return { runId, status: sequence.state, sequence, manualFence: blocked ? 'Blocked' : 'Released', latestEventSequence: '12', ...patch }
}

/** A run with no sequence and an established fence. */
export const localNoneStatusFor = (runId: string, patch: Raw = {}): Raw => ({
  runId,
  status: 'None',
  sequence: null,
  manualFence: 'Released',
  latestEventSequence: '0',
  ...patch,
})

export const localRecipe = (ordinal: number, patch: Raw = {}): Raw => ({
  ordinal,
  commandId: localGuid(400 + ordinal),
  commandNumber: ordinal + 1,
  name: ordinal === 0 ? 'Unit tests' : `Recipe ${ordinal + 1}`,
  timeoutSeconds: 300,
  ...patch,
})

const NO_DISCLOSURE: Raw = {
  codexModel: null,
  codexEffort: null,
  claudeModel: null,
  claudeEffort: null,
  claudeMaxTurns: null,
  codexAccountUsageStopPercent: null,
  codexTokenStopThreshold: null,
  claudeTokenStopThreshold: null,
}

/** An available arming candidate for `planId`. */
export const localAvailabilityFor = (runId: string, planId = LOCAL_PLAN_ID, patch: Raw = {}): Raw => ({
  runId,
  planProposalMessageId: planId,
  available: true,
  refusalCode: null,
  refusalText: null,
  planAuthority: 'AcceptedRoot',
  workspaceId: LOCAL_WORKSPACE_ID,
  checkpointId: LOCAL_CHECKPOINT_ID,
  fingerprintSha256: LOCAL_FINGERPRINT,
  confirmationSha256: LOCAL_CONFIRMATION,
  correctionAllowance: 2,
  maximumSteps: 9,
  recipes: [localRecipe(0)],
  disclosure: { ...NO_DISCLOSURE },
  ...patch,
})

/** An arming refusal with its fixed code and text and no candidate. */
export const localRefusedAvailabilityFor = (
  runId: string,
  code = 'local_execution_sequence.project_busy',
  planId = LOCAL_PLAN_ID,
  text = 'Arming is refused for a fixed reason.',
): Raw => ({
  runId,
  planProposalMessageId: planId,
  available: false,
  refusalCode: code,
  refusalText: text,
  planAuthority: null,
  workspaceId: null,
  checkpointId: null,
  fingerprintSha256: null,
  confirmationSha256: null,
  correctionAllowance: null,
  maximumSteps: null,
  recipes: null,
  disclosure: null,
})

/** The exact tuple the candidate and the receipts above carry. */
export const LOCAL_TUPLE = {
  planProposalMessageId: LOCAL_PLAN_ID,
  workspaceId: LOCAL_WORKSPACE_ID,
  checkpointId: LOCAL_CHECKPOINT_ID,
  fingerprintSha256: LOCAL_FINGERPRINT,
  confirmationSha256: LOCAL_CONFIRMATION,
}

export const localArmReceiptFor = (runId: string, patch: Raw = {}): Raw => ({
  sequenceId: LOCAL_SEQUENCE_ID,
  runId,
  planProposalMessageId: LOCAL_PLAN_ID,
  workspaceId: LOCAL_WORKSPACE_ID,
  checkpointId: LOCAL_CHECKPOINT_ID,
  fingerprintSha256: LOCAL_FINGERPRINT,
  confirmationSha256: LOCAL_CONFIRMATION,
  state: 'Armed',
  recipeCount: 1,
  correctionAllowance: 2,
  armedAtUtc: at(0),
  advancementDeadlineUtc: at(120),
  revision: '1',
  replayed: false,
  ...patch,
})

export const localStopAnswerFor = (runId: string, patch: Raw = {}): Raw => ({
  sequenceId: LOCAL_SEQUENCE_ID,
  runId,
  state: 'Stopping',
  reasonCode: null,
  revision: '4',
  changed: true,
  ...patch,
})

/**
 * What `useLocalExecutionSequence` returns for a validated answer of the run, for tests that replace the reader. The control is derived
 * from the decoded status exactly as the hook derives it; an answer that does not decode is a fixture error, never an unknown status.
 */
export function localReaderFor(raw: unknown, runId: string, overrides: Partial<UseLocalExecutionSequenceResult> = {}): UseLocalExecutionSequenceResult {
  const status = toLocalExecutionSequenceStatus(raw, runId)
  if (status === null) {
    throw new Error('The local-execution-sequence fixture is not a valid status.')
  }

  return {
    runId,
    status,
    loading: false,
    failure: null,
    control: deriveLocalExecutionSequenceControl(status),
    polling: false,
    confirming: false,
    refresh: () => undefined,
    beginCommand: () => ({ settled: () => undefined }),
    ...overrides,
  }
}

/** A reader with an established fence and no sequence recorded: the state every ordinary gate test composes under. */
export const noneLocalReader = (runId = 'run-1', overrides: Partial<UseLocalExecutionSequenceResult> = {}): UseLocalExecutionSequenceResult =>
  localReaderFor(localNoneStatusFor(runId), runId, overrides)

/** A reader with a live sequence holding automatic authority. */
export const blockedLocalReader = (runId = 'run-1', overrides: Partial<UseLocalExecutionSequenceResult> = {}): UseLocalExecutionSequenceResult =>
  localReaderFor(localStatusFor(runId, 'AwaitingStepResult'), runId, overrides)

/** A reader while nothing is validated: unknown. */
export const unknownLocalReader = (runId = 'run-1', overrides: Partial<UseLocalExecutionSequenceResult> = {}): UseLocalExecutionSequenceResult => ({
  runId,
  status: null,
  loading: false,
  failure: 'read',
  control: 'unknown',
  polling: false,
  confirming: false,
  refresh: () => undefined,
  beginCommand: () => ({ settled: () => undefined }),
  ...overrides,
})
