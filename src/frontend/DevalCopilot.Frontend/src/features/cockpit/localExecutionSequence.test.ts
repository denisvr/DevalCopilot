import { describe, expect, it } from 'vitest'
import {
  LOCAL_EXECUTION_SEQUENCE_STATES,
  deriveLocalExecutionSequenceControl,
  toLocalExecutionArmReceipt,
  toLocalExecutionSequenceAvailability,
  toLocalExecutionSequenceStatus,
  toLocalExecutionStopAnswer,
} from './localExecutionSequence'
import type { LocalExecutionSequenceStateName } from './localExecutionSequence'
import {
  LOCAL_PLAN_ID,
  LOCAL_TUPLE,
  localArmReceiptFor,
  localAvailabilityFor,
  localGuid,
  localNoneStatusFor,
  localRecipe,
  localRefusedAvailabilityFor,
  localSequenceFor,
  localStatusFor,
  localStepFor,
  localStopAnswerFor,
} from './localExecutionSequenceFixture'

const RUN = 'run-1'
type Raw = Record<string, unknown>
const decode = (raw: unknown) => toLocalExecutionSequenceStatus(raw, RUN)
const withSequence = (state: LocalExecutionSequenceStateName, sequencePatch: Raw, patch: Raw = {}) => localStatusFor(RUN, state, patch, sequencePatch)

describe('toLocalExecutionSequenceStatus: accepted answers', () => {
  it.each(LOCAL_EXECUTION_SEQUENCE_STATES)('accepts a coherent %s status exactly as the host wrote it', (state) => {
    const raw = localStatusFor(RUN, state)
    const status = decode(raw)
    expect(status).not.toBeNull()
    expect(status?.status).toBe(state)
    expect(status?.sequence?.state).toBe(state)
    expect(status?.sequence?.revision).toBe((raw.sequence as Raw).revision)
  })

  it('keeps raw Int64 and time texts exactly as written', () => {
    const status = decode(localStatusFor(RUN, 'Armed', { latestEventSequence: '9223372036854775807' }, { revision: '9007199254740993' }))
    expect(status?.latestEventSequence).toBe('9223372036854775807')
    expect(status?.sequence?.revision).toBe('9007199254740993')
    expect(status?.sequence?.armedAtUtc).toBe('2026-10-09T10:00:00.1234567Z')
  })

  it('accepts no sequence with a released or an unavailable fence', () => {
    expect(decode(localNoneStatusFor(RUN))).toMatchObject({ status: 'None', sequence: null, manualFence: 'Released' })
    expect(decode(localNoneStatusFor(RUN, { manualFence: 'Unavailable' }))).toMatchObject({ manualFence: 'Unavailable' })
  })

  it('accepts a quarantined terminal operation with a blocked fence and the no-recovery next action', () => {
    const raw = localStatusFor(
      RUN,
      'NeedsAttention',
      {},
      {
        steps: [localStepFor(0, 'quarantined')],
        reasonCode: 'exit_evidence_missing',
        reasonText: 'A dispatched operation ended without recorded process exit evidence.',
      },
    )
    const status = decode(raw)
    expect(status?.manualFence).toBe('Blocked')
    expect(status?.sequence?.awaitingReconciliation).toBe(true)
    expect(status?.sequence?.nextActionCode).toBe('no_supported_recovery_action_in_this_version')
    expect(status?.sequence?.steps[0].quarantined).toBe(true)
  })

  it('accepts an effective NeedsAttention that the host observed before it persisted the deadline', () => {
    const raw = withSequence('AwaitingStepResult', {
      state: 'NeedsAttention',
      observedBeforePersisted: true,
      reasonCode: 'result_deadline_elapsed',
      reasonText: 'The result deadline elapsed. No process was cancelled.',
      nextActionCode: 'no_supported_recovery_action_in_this_version',
      nextActionText: 'No supported recovery action in this version.',
      awaitingReconciliation: true,
    }, { status: 'NeedsAttention' })
    expect(decode(raw)?.sequence?.observedBeforePersisted).toBe(true)
  })
})

describe('toLocalExecutionSequenceStatus: refused answers', () => {
  const refused: [string, unknown][] = [
    ['undefined', undefined],
    ['null', null],
    ['an array', []],
    ['another run', localStatusFor('run-2', 'Armed')],
    ['an extra member', { ...localStatusFor(RUN, 'Armed'), extra: 1 }],
    ['a missing member', (() => { const raw = localStatusFor(RUN, 'Armed'); delete raw.manualFence; return raw })()],
    ['an unknown fence', localStatusFor(RUN, 'Armed', { manualFence: 'Open' })],
    ['a lifted fence over a live state', localStatusFor(RUN, 'Armed', { manualFence: 'Released' })],
    ['a lifted fence over a running operation', localStatusFor(RUN, 'ReadyForHumanApproval', { manualFence: 'Released' }, { steps: [localStepFor(0, 'concluded'), localStepFor(1, 'claimed')] })],
    ['a lifted fence over a quarantine', localStatusFor(RUN, 'NeedsAttention', { manualFence: 'Released' }, { steps: [localStepFor(0, 'quarantined')], reasonCode: 'exit_evidence_missing', reasonText: 'x' })],
    ['a blocked fence without a sequence', localNoneStatusFor(RUN, { manualFence: 'Blocked' })],
    ['a numeric event sequence', localStatusFor(RUN, 'Armed', { latestEventSequence: 12 })],
    ['a non-canonical event sequence', localStatusFor(RUN, 'Armed', { latestEventSequence: '012' })],
    ['an overflowing event sequence', localStatusFor(RUN, 'Armed', { latestEventSequence: '9223372036854775808' })],
    ['an unknown status', localStatusFor(RUN, 'Armed', { status: 'Running' })],
    ['a status that disagrees with the state', localStatusFor(RUN, 'Armed', { status: 'Stopping' })],
    ['a None status with a sequence', localNoneStatusFor(RUN, { sequence: localSequenceFor('Armed') })],
    ['an unknown state', withSequence('Armed', { state: 'Running', recordedState: 'Running' }, { status: 'Running' })],
    ['a zero revision', withSequence('Armed', { revision: '0' })],
    ['a numeric revision', withSequence('Armed', { revision: 1 })],
    ['a non-round-trip time', withSequence('Armed', { armedAtUtc: '2026-10-09T10:00:00Z' })],
    ['an advancement deadline that is not two hours', withSequence('Armed', { advancementDeadlineUtc: '2026-10-09T11:00:00.1234567Z' })],
    ['an upper-case GUID', withSequence('Armed', { workspaceId: 'AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA' })],
    ['a bad fingerprint', withSequence('Armed', { fingerprintSha256: 'xyz' })],
    ['an unknown plan authority', withSequence('Armed', { planAuthority: 'Anyone' })],
    ['a wrong maximum step count', withSequence('Armed', { maximumSteps: 5 })],
    ['a recipe count of zero', withSequence('Armed', { recipeCount: 0, maximumSteps: 6 })],
    ['an allowance above two', withSequence('Armed', { correctionAllowance: 3 })],
    ['a correction counter that disagrees with the steps', withSequence('Armed', { correctionsClaimed: 1 })],
    ['a pass counter that disagrees with the steps', withSequence('Armed', { passesStarted: 1 })],
    ['an agent claim counter that disagrees with the steps', withSequence('Armed', { agentClaims: 1 })],
    ['a reason on a live state', withSequence('Armed', { reasonCode: 'review_approved', reasonText: 'x' })],
    ['a reason that does not belong to the state', withSequence('Stopped', { reasonCode: 'review_approved', reasonText: 'x' })],
    ['reason text without a reason', withSequence('Armed', { reasonText: 'x' })],
    ['an unsafe reason text', withSequence('Stopped', { reasonText: 'bad\u0007text' })],
    ['a detail on a live state', withSequence('Armed', { detailCode: 'x' })],
    ['a wrong next action', withSequence('Armed', { nextActionCode: 'continue_with_explicit_manual_work', nextActionText: 'x' })],
    ['a missing end time on an ended state', withSequence('Stopped', { endedAtUtc: null })],
    ['an end time on a live state', withSequence('Armed', { endedAtUtc: '2026-10-09T10:30:00.1234567Z' })],
    ['a stopping state without a stop time', withSequence('Stopping', { stopRequestedAtUtc: null })],
    ['a stop time on an armed state', withSequence('Armed', { stopRequestedAtUtc: '2026-10-09T10:25:00.1234567Z' })],
    ['an observed deadline over an ended state', withSequence('Stopped', { observedBeforePersisted: true })],
    ['an outstanding operation that is not the running one', withSequence('AwaitingStepResult', { outstandingOperationId: localGuid(999) })],
    ['a missing reconciliation flag', withSequence('NeedsAttention', { steps: [localStepFor(0, 'claimed')], reasonCode: 'operation_failed', reasonText: 'x', awaitingReconciliation: false })],
    ['an armed state with steps', withSequence('Armed', { steps: [localStepFor(0, 'reserved')], agentClaims: 1 })],
    ['too many steps', withSequence('Armed', { steps: Array.from({ length: 10 }, (_, index) => localStepFor(index, 'concluded')) })],
    ['final references over a live state', withSequence('Armed', { final: { reportMessageId: localGuid(300), reviewAttemptId: localGuid(301), escalationMessageId: null } })],
    ['final references naming both', withSequence('ReadyForHumanApproval', { final: { reportMessageId: localGuid(300), reviewAttemptId: localGuid(202), escalationMessageId: localGuid(5) } })],
    ['final references naming an unrelated review', withSequence('ReadyForHumanApproval', { final: { reportMessageId: localGuid(300), reviewAttemptId: localGuid(999), escalationMessageId: null } })],
    ['final references with an extra member', withSequence('ReadyForHumanApproval', { final: { reportMessageId: localGuid(300), reviewAttemptId: localGuid(202), escalationMessageId: null, x: 1 } })],
  ]
  it.each(refused)('refuses %s', (_name, raw) => {
    expect(decode(raw)).toBeNull()
  })
})

describe('toLocalExecutionSequenceStatus: refused steps', () => {
  const steps: [string, Raw][] = [
    ['a step with an extra member', localStepFor(0, 'reserved', { extra: 1 })],
    ['an out-of-sequence ordinal', localStepFor(0, 'reserved', { ordinal: 3 })],
    ['an unknown kind', localStepFor(0, 'reserved', { kind: 'Deploy' })],
    ['an unknown step state', localStepFor(0, 'reserved', { state: 'Running' })],
    ['a pass number of zero on a verification step', localStepFor(0, 'reserved', { passNumber: 0 }, 'Verification')],
    ['an implementation step in a verification pass', localStepFor(0, 'reserved', { passNumber: 1 })],
    ['a pass number beyond the bound', localStepFor(0, 'reserved', { passNumber: 4 }, 'Verification')],
    ['a recipe on an agent step', localStepFor(0, 'reserved', { recipeOrdinal: 0, recipeName: 'x' })],
    ['a verification step without a recipe', localStepFor(0, 'reserved', { recipeOrdinal: null }, 'Verification')],
    ['a recipe ordinal beyond the pinned set', localStepFor(0, 'reserved', { recipeOrdinal: 1 }, 'Verification')],
    ['a preparation window that is not two minutes', localStepFor(0, 'reserved', { preparationDeadlineUtc: '2026-10-09T10:05:00.1234567Z' })],
    ['a reason on a reserved step', localStepFor(0, 'reserved', { reasonCode: 'stopped_before_claim' })],
    ['a revoked step without a reason', localStepFor(0, 'revoked', { reasonCode: null })],
    ['a revoked step with an unknown reason', localStepFor(0, 'revoked', { reasonCode: 'free_text' })],
    ['an operation on a reserved step', localStepFor(0, 'reserved', { operationId: localGuid(200) })],
    ['a claimed step without an operation', localStepFor(0, 'claimed', { operationId: null })],
    ['a claimed step without a result deadline', localStepFor(0, 'claimed', { resultDeadlineUtc: null })],
    ['a partial linked row', localStepFor(0, 'claimed', { dispatched: null })],
    ['an operation status that is not a name', localStepFor(0, 'claimed', { operationStatus: '<b>Running</b>' })],
    ['a running operation with an outcome', localStepFor(0, 'claimed', { operationOutcome: 'Implemented' })],
    ['a concluded step that is still running', localStepFor(0, 'concluded', { operationStatus: 'Running', operationOutcome: null })],
    ['a quarantine flag that disagrees with the evidence', localStepFor(0, 'concluded', { quarantined: true })],
    ['a dispatched terminal row without exit evidence that is not quarantined', localStepFor(0, 'quarantined', { quarantined: false })],
    ['a quarantined reserved step', localStepFor(0, 'reserved', { quarantined: true })],
  ]
  it.each(steps)('refuses %s', (_name, step) => {
    const state = (step.state as string) === 'Reserved' ? 'PreparingStep' : 'NeedsAttention'
    const raw = localStatusFor(RUN, state as LocalExecutionSequenceStateName, {}, { steps: [step], agentClaims: step.kind === 'Verification' ? 0 : 1 })
    expect(decode(raw)).toBeNull()
  })

  it('refuses a non-final step that is not concluded', () => {
    const raw = localStatusFor(RUN, 'AwaitingStepResult', {}, { steps: [localStepFor(0, 'claimed'), localStepFor(1, 'claimed')], agentClaims: 1, passesStarted: 1 })
    expect(decode(raw)).toBeNull()
  })

  it('refuses a duplicate step identity', () => {
    const raw = localStatusFor(RUN, 'AwaitingStepResult', {}, {
      steps: [localStepFor(0, 'concluded'), localStepFor(1, 'claimed', { stepId: localGuid(100) })],
      passesStarted: 1,
    })
    expect(decode(raw)).toBeNull()
  })
})

describe('deriveLocalExecutionSequenceControl', () => {
  it('derives unknown, blocked, released and none from one validated status only', () => {
    expect(deriveLocalExecutionSequenceControl(null)).toBe('unknown')
    expect(deriveLocalExecutionSequenceControl(decode(localNoneStatusFor(RUN, { manualFence: 'Unavailable' })))).toBe('unknown')
    expect(deriveLocalExecutionSequenceControl(decode(localStatusFor(RUN, 'AwaitingStepResult')))).toBe('blocked')
    expect(deriveLocalExecutionSequenceControl(decode(localStatusFor(RUN, 'Stopped')))).toBe('released')
    expect(deriveLocalExecutionSequenceControl(decode(localNoneStatusFor(RUN)))).toBe('none')
  })
})

describe('toLocalExecutionSequenceAvailability', () => {
  const decodeAvailability = (raw: unknown) => toLocalExecutionSequenceAvailability(raw, RUN, LOCAL_PLAN_ID)

  it('accepts the exact candidate with its recipes and disclosure', () => {
    const answer = decodeAvailability(
      localAvailabilityFor(RUN, LOCAL_PLAN_ID, {
        recipes: [localRecipe(0), localRecipe(1)],
        maximumSteps: 12,
        disclosure: {
          codexModel: 'gpt-x', codexEffort: 'high', claudeModel: 'opus', claudeEffort: null, claudeMaxTurns: 20,
          codexAccountUsageStopPercent: 90, codexTokenStopThreshold: '9223372036854775807', claudeTokenStopThreshold: '0',
        },
      }),
    )
    expect(answer?.candidate?.recipes.map((recipe) => recipe.name)).toEqual(['Unit tests', 'Recipe 2'])
    expect(answer?.candidate?.disclosure.codexTokenStopThreshold).toBe('9223372036854775807')
    expect(answer?.candidate).toMatchObject({ workspaceId: LOCAL_TUPLE.workspaceId, checkpointId: LOCAL_TUPLE.checkpointId, confirmationSha256: LOCAL_TUPLE.confirmationSha256, correctionAllowance: 2 })
  })

  it('accepts a fixed refusal without a candidate', () => {
    expect(decodeAvailability(localRefusedAvailabilityFor(RUN))).toMatchObject({ available: false, refusalCode: 'local_execution_sequence.project_busy', candidate: null })
  })

  const refused: [string, unknown][] = [
    ['undefined', undefined],
    ['another run', localAvailabilityFor('run-2')],
    ['another plan', localAvailabilityFor(RUN, localGuid(77))],
    ['an extra member', { ...localAvailabilityFor(RUN), extra: 1 }],
    ['an available answer with a refusal', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { refusalCode: 'local_execution_sequence.project_busy', refusalText: 'x' })],
    ['an available answer without a confirmation', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { confirmationSha256: null })],
    ['a malformed confirmation', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { confirmationSha256: 'ABC' })],
    ['an allowance above two', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { correctionAllowance: 3 })],
    ['a maximum that disagrees with the recipes', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { maximumSteps: 5 })],
    ['no recipes', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: [], maximumSteps: 6 })],
    ['more than thirty-two recipes', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: Array.from({ length: 33 }, (_, index) => localRecipe(index)), maximumSteps: 105 })],
    ['a duplicate command number', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: [localRecipe(0), localRecipe(1, { commandNumber: 1 })], maximumSteps: 12 })],
    ['recipes out of order', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: [localRecipe(0, { commandNumber: 5 }), localRecipe(1, { commandNumber: 2 })], maximumSteps: 12 })],
    ['a recipe with an extra member', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: [localRecipe(0, { executablePath: 'C:/x.exe' })] })],
    ['a recipe with a zero timeout', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { recipes: [localRecipe(0, { timeoutSeconds: 0 })] })],
    ['a numeric threshold', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { disclosure: { codexModel: null, codexEffort: null, claudeModel: null, claudeEffort: null, claudeMaxTurns: null, codexAccountUsageStopPercent: null, codexTokenStopThreshold: 5, claudeTokenStopThreshold: null } })],
    ['a non-canonical threshold', localAvailabilityFor(RUN, LOCAL_PLAN_ID, { disclosure: { codexModel: null, codexEffort: null, claudeModel: null, claudeEffort: null, claudeMaxTurns: null, codexAccountUsageStopPercent: null, codexTokenStopThreshold: '05', claudeTokenStopThreshold: null } })],
    ['a refusal with a candidate member', localRefusedAvailabilityFor(RUN) && { ...localRefusedAvailabilityFor(RUN), workspaceId: localGuid(2) }],
    ['a refusal with a code outside the namespace', localRefusedAvailabilityFor(RUN, 'other.code')],
    ['a refusal without text', { ...localRefusedAvailabilityFor(RUN), refusalText: null }],
  ]
  it.each(refused)('refuses %s', (_name, raw) => {
    expect(decodeAvailability(raw)).toBeNull()
  })
})

describe('toLocalExecutionArmReceipt', () => {
  const decodeReceipt = (raw: unknown) => toLocalExecutionArmReceipt(raw, RUN, LOCAL_TUPLE)

  it('accepts a first arming and a replay', () => {
    expect(decodeReceipt(localArmReceiptFor(RUN))).toMatchObject({ state: 'Armed', revision: '1', replayed: false })
    expect(decodeReceipt(localArmReceiptFor(RUN, { replayed: true, state: 'Stopped', revision: '9' }))).toMatchObject({ replayed: true, state: 'Stopped' })
  })

  const refused: [string, Raw][] = [
    ['another run', localArmReceiptFor('run-2')],
    ['another plan', localArmReceiptFor(RUN, { planProposalMessageId: localGuid(77) })],
    ['another workspace', localArmReceiptFor(RUN, { workspaceId: localGuid(77) })],
    ['another checkpoint', localArmReceiptFor(RUN, { checkpointId: localGuid(77) })],
    ['another fingerprint', localArmReceiptFor(RUN, { fingerprintSha256: 'cd'.repeat(32) })],
    ['another confirmation', localArmReceiptFor(RUN, { confirmationSha256: 'ef'.repeat(32) })],
    ['an extra member', { ...localArmReceiptFor(RUN), extra: 1 }],
    ['a first arming that is not armed', localArmReceiptFor(RUN, { state: 'Stopping' })],
    ['a first arming at another revision', localArmReceiptFor(RUN, { revision: '2' })],
    ['a numeric revision', localArmReceiptFor(RUN, { revision: 1 })],
    ['a window that is not two hours', localArmReceiptFor(RUN, { advancementDeadlineUtc: '2026-10-09T11:00:00.1234567Z' })],
    ['an unknown state', localArmReceiptFor(RUN, { state: 'Running', replayed: true })],
    ['a non-boolean replay flag', localArmReceiptFor(RUN, { replayed: 'false' })],
  ]
  it.each(refused)('refuses %s', (_name, raw) => {
    expect(decodeReceipt(raw)).toBeNull()
  })
})

describe('toLocalExecutionStopAnswer', () => {
  const decodeStop = (raw: unknown) => toLocalExecutionStopAnswer(raw, RUN, localGuid(1))

  it('accepts the answers a stop can leave', () => {
    expect(decodeStop(localStopAnswerFor(RUN))).toMatchObject({ state: 'Stopping', changed: true })
    expect(decodeStop(localStopAnswerFor(RUN, { state: 'Stopped', reasonCode: 'stopped_before_claim' }))?.changed).toBe(true)
    expect(decodeStop(localStopAnswerFor(RUN, { state: 'ReadyForHumanApproval', reasonCode: 'review_approved', changed: false }))?.changed).toBe(false)
  })

  const refused: [string, Raw][] = [
    ['another run', localStopAnswerFor('run-2')],
    ['another sequence', localStopAnswerFor(RUN, { sequenceId: localGuid(9) })],
    ['an extra member', { ...localStopAnswerFor(RUN), extra: 1 }],
    ['a state that can still advance', localStopAnswerFor(RUN, { state: 'AwaitingStepResult', changed: false })],
    ['a reason that does not belong to the state', localStopAnswerFor(RUN, { state: 'Stopped', reasonCode: 'review_approved', changed: false })],
    ['a missing reason on an ended state', localStopAnswerFor(RUN, { state: 'Stopped', reasonCode: null, changed: false })],
    ['a change that a stop never makes', localStopAnswerFor(RUN, { state: 'Stopped', reasonCode: 'stopped_after_operation', changed: true })],
    ['a zero revision', localStopAnswerFor(RUN, { revision: '0' })],
  ]
  it.each(refused)('refuses %s', (_name, raw) => {
    expect(decodeStop(raw)).toBeNull()
  })
})
