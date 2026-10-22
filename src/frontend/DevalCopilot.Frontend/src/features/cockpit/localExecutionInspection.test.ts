import { describe, expect, it } from 'vitest'
import {
  decideLocalExecutionWindowAnswer,
  toLocalExecutionInspectionIndex,
  toLocalExecutionStepAnswer,
} from './localExecutionInspection'
import {
  CHECKPOINT,
  FINGERPRINT,
  RUN,
  SEQUENCE,
  absentStream,
  agentFor,
  agentStepAnswerFor,
  guidOf,
  headerFor,
  indexedStep,
  notRecordedIndexFor,
  operationIdFor,
  recordedIndexFor,
  refusedStepAnswerFor,
  refusedWindowFor,
  stepFor,
  stepIdFor,
  streamFor,
  unavailableIndexFor,
  verificationFor,
  verificationStepAnswerFor,
  windowAnswerFor,
} from './localExecutionInspectionFixture'

const REPORT_ID = guidOf('m', 1)

const index = (raw: unknown) => toLocalExecutionInspectionIndex(raw, RUN)

describe('toLocalExecutionInspectionIndex', () => {
  it('accepts the whole recorded cycle as the closed graph it is', () => {
    const accepted = index(recordedIndexFor())

    expect(accepted?.status).toBe('Recorded')
    if (accepted?.status !== 'Recorded') throw new Error('unreachable')
    expect(accepted.sequence.revision).toBe('9')
    expect(accepted.steps.map((step) => step.kind)).toEqual(['Implementation', 'Verification', 'CodeReview'])
    expect(accepted.steps[1].recipeName).toBe('Unit <b>tests</b>')
  })

  it('accepts NotRecorded and Unavailable only with a null header and an empty list, and a zero-step sequence', () => {
    expect(index(notRecordedIndexFor())).toEqual({ status: 'NotRecorded' })
    expect(index(unavailableIndexFor())).toEqual({ status: 'Unavailable' })
    expect(index({ ...notRecordedIndexFor(), steps: [stepFor(0)] })).toBeNull()
    expect(index({ ...unavailableIndexFor(), sequence: headerFor() })).toBeNull()
    expect(index(recordedIndexFor(RUN, [], {}))?.status).toBe('Recorded')
  })

  it.each([
    ['another run', recordedIndexFor(guidOf('r', 2))],
    ['an unknown status', { ...recordedIndexFor(), status: 'Recording' }],
    ['an extra member', { ...recordedIndexFor(), extra: 1 }],
    ['a missing member', (() => { const { steps: _steps, ...rest } = recordedIndexFor(); return rest })()],
    ['a recorded status without a header', { ...recordedIndexFor(), sequence: null }],
    ['an undefined answer', undefined],
    ['a non-object', 'Recorded'],
    ['an array', []],
  ])('refuses %s', (_name, raw) => {
    expect(index(raw)).toBeNull()
  })

  it.each([
    ['an unknown state', { recordedState: 'Bogus' }],
    ['a live state with a reason', { recordedState: 'Armed', reasonCode: 'review_approved' }],
    ['an ended state without a reason', { reasonCode: null }],
    ['an ended state with an unknown reason', { reasonCode: 'made_up' }],
    ['an ended state without an end time', { endedAtUtc: null }],
    ['a live state with an end time', { recordedState: 'Armed', reasonCode: null }],
    ['a revision that is a number', { revision: 9 }],
    ['a revision with a leading zero', { revision: '09' }],
    ['a revision of zero', { revision: '0' }],
    ['a revision past Int64', { revision: '9223372036854775808' }],
    ['a revision past the safe integer', { revision: '9007199254740993' }],
    ['a recipe count of zero', { recipeCount: 0 }],
    ['a recipe count of 33', { recipeCount: 33 }],
    ['a recipe count that is a fraction', { recipeCount: 1.5 }],
    ['an allowance of 3', { correctionAllowance: 3 }],
    ['an unknown authority', { planAuthority: 'Everyone' }],
    ['an uppercase fingerprint', { fingerprintSha256: FINGERPRINT.toUpperCase() }],
    ['a short fingerprint', { fingerprintSha256: 'a'.repeat(63) }],
    ['a non-guid sequence identity', { sequenceId: 'sequence-1' }],
    ['the empty guid', { planProposalMessageId: '00000000-0000-0000-0000-000000000000' }],
    ['a time with an offset', { armedAtUtc: '2026-10-20T09:00:00.0000000+00:00' }],
    ['a time with six fractional digits', { updatedAtUtc: '2026-10-20T09:00:50.000000Z' }],
    ['an update before the arming', { updatedAtUtc: '2026-10-19T09:00:00.0000000Z' }],
  ])('refuses a header with %s', (_name, patch) => {
    expect(index({ ...recordedIndexFor(), sequence: headerFor(patch) })).toBeNull()
  })

  it.each([
    ['an unknown kind', stepFor(1, { kind: 'Replay' })],
    ['an unknown state', stepFor(1, { recordedState: 'Running' })],
    ['a pass of 4', stepFor(1, { passNumber: 4 })],
    ['a verification without a recipe', stepFor(1, { recipeOrdinal: null, recipeName: null })],
    ['a recipe beyond the recipe count', stepFor(1, { recipeOrdinal: 1 })],
    ['a blank recipe name', stepFor(1, { recipeName: '   ' })],
    ['a recipe name of 201 units', stepFor(1, { recipeName: 'x'.repeat(201) })],
    ['a recipe on a non-verification step', stepFor(2, { recipeOrdinal: 0, recipeName: 'x' })],
    ['a claimed step without its link', stepFor(1, { operationId: null })],
    ['a concluded step without its claim time', stepFor(1, { claimedAtUtc: null })],
    ['a deadline before the claim', stepFor(1, { resultDeadlineUtc: '2026-10-20T09:00:11.0000000Z' })],
    ['a reason on a concluded step', stepFor(1, { reasonCode: 'operation_failed' })],
    ['a step reserved with a link', stepFor(1, { recordedState: 'Reserved' })],
    ['an implementation with a report', stepFor(0, { reportMessageId: REPORT_ID })],
    ['a step after the implementation without a report', stepFor(1, { reportMessageId: null })],
    ['a correction without a subject', stepFor(2, { kind: 'ReviewCorrection' })],
    ['a subject on a review', stepFor(2, { subjectAttemptId: operationIdFor(1) })],
    ['a preparation deadline before the reservation', stepFor(1, { preparationDeadlineUtc: '2026-10-20T09:00:00.0000000Z' })],
    ['a non-lowercase source fingerprint', stepFor(1, { sourceFingerprintSha256: 'A'.repeat(64) })],
    ['an implementation not on the starting checkpoint', stepFor(0, { sourceCheckpointId: guidOf('c', 2) })],
  ])('refuses a step with %s', (_name, step) => {
    expect(index(recordedIndexFor(RUN, step.ordinal === 0 ? [step] : [stepFor(0), step]))).toBeNull()
  })

  it('refuses a list that skips an ordinal, repeats an identity or a link, or leaves an earlier step outstanding', () => {
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(2)]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1, { stepId: stepIdFor(0) })]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1, { operationId: operationIdFor(0) })]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0, { recordedState: 'Claimed' }), stepFor(1)]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1), stepFor(2, { passNumber: 1, kind: 'Verification', recipeOrdinal: 0, recipeName: 'x' })]))).toBeNull()
  })

  it('refuses more steps than the recipe count allows', () => {
    const steps = Array.from({ length: 10 }, (_, ordinal) => stepFor(ordinal === 0 ? 0 : 2, { ordinal, stepId: stepIdFor(ordinal), operationId: operationIdFor(ordinal), passNumber: ordinal === 0 ? 0 : 1 }))
    expect(index(recordedIndexFor(RUN, steps))).toBeNull()
  })

  it('accepts a correction that names its earlier linked review and refuses an arbitrary or wrong-origin subject', () => {
    const review = stepFor(2, { passNumber: 1 })
    const correction = stepFor(3, { kind: 'ReviewCorrection', subjectAttemptId: review.operationId, passNumber: 1 })
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1), review, correction]))?.status).toBe('Recorded')
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1), review, { ...correction, subjectAttemptId: operationIdFor(0) }]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0), stepFor(1), review, { ...correction, kind: 'DiagnosisCorrection' }]))).toBeNull()
  })

  it('accepts a revoked or unresolved last step with its fixed reason and no link', () => {
    const revoked = stepFor(1, {
      recordedState: 'Revoked', reasonCode: 'host_restarted', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null,
    })
    expect(index(recordedIndexFor(RUN, [stepFor(0), revoked]))?.status).toBe('Recorded')
    expect(index(recordedIndexFor(RUN, [stepFor(0), { ...revoked, reasonCode: null }]))).toBeNull()
    expect(index(recordedIndexFor(RUN, [stepFor(0), { ...revoked, reasonCode: 'made_up' }]))).toBeNull()
  })
})

const answerFor = (raw: unknown, ordinal: number, indexed = indexedStep(ordinal)) =>
  toLocalExecutionStepAnswer(raw, { runId: RUN, sequenceId: SEQUENCE, step: indexed })

describe('toLocalExecutionStepAnswer', () => {
  it('accepts an agent header only for the linked attempt of the contract of the step kind', () => {
    const accepted = answerFor(agentStepAnswerFor(0), 0)
    expect(accepted).toMatchObject({ kind: 'agent', agent: { attemptId: operationIdFor(0), attemptNumber: 3, role: 'Implementer', responseContract: 'ImplementationReport' } })
  })

  it.each([
    ['the wrong linked identity', { agent: agentFor(0, { id: operationIdFor(5) }) }],
    ['a contract of another kind', { agent: agentFor(0, { responseContract: 'ImplementationReview', role: 'CodeReviewer', provider: 'Codex' }) }],
    ['an incoherent role and provider', { agent: agentFor(0, { provider: 'Codex' }) }],
    ['an attempt number of zero', { agent: agentFor(0, { attemptNumber: 0 }) }],
    ['an extra agent member', { agent: { ...agentFor(0), status: 'Running' } }],
    ['both an agent and a verification', { verification: verificationFor(0) }],
    ['no agent', { agent: null }],
    ['another run', { runId: guidOf('r', 2) }],
    ['another sequence', { sequenceId: guidOf('s', 2) }],
    ['another step', { stepId: stepIdFor(7) }],
    ['an unknown status', { status: 'Done' }],
    ['a different indexed fact', { step: stepFor(0, { sourceFingerprintSha256: 'b'.repeat(64) }) }],
    ['a step that moved backwards', { step: stepFor(0, { recordedState: 'Reserved', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null }) }],
    ['an extra member', { extra: true }],
  ])('refuses an agent answer with %s', (_name, patch) => {
    expect(answerFor(agentStepAnswerFor(0, patch), 0).kind).toBe('incoherent')
  })

  it('accepts a verification that is exactly the linked execution and never promotes its status', () => {
    const accepted = answerFor(verificationStepAnswerFor(1), 1)
    expect(accepted.kind).toBe('verification')
    if (accepted.kind !== 'verification') throw new Error('unreachable')
    expect(accepted.verification).toMatchObject({ status: 'Failed', outcome: 'Exited', exitCode: 1, commandName: 'Unit <b>tests</b>' })
    expect(accepted.verification.standardOutput).toMatchObject({ status: 'Recorded', byteLength: 120 })
  })

  it.each([
    ['another execution', { executionId: operationIdFor(9) }],
    ['a name that is not the recipe name', { commandName: 'Other' }],
    ['a blank name', { commandName: ' ' }],
    ['an unknown status', { status: 'Succeeded' }],
    ['a passed status with a nonzero exit code', { status: 'Passed', exitCode: 1 }],
    ['a failed status with a zero exit code', { exitCode: 0 }],
    ['an exit code without an Exited outcome', { outcome: 'TimedOut', status: 'TimedOut' }],
    ['an Exited outcome without an exit code', { exitCode: null }],
    ['a running status that completed', { status: 'Running', outcome: null, exitCode: null }],
    ['a refusal that was dispatched', { status: 'LocalExecutionSequenceDispatchRefused', outcome: null, exitCode: null }],
    ['an interrupted status with an outcome', { status: 'Interrupted' }],
    ['a completion before the claim', { completedAtUtc: '2026-10-20T09:00:11.0000000Z' }],
    ['a dispatch before the claim', { dispatchedAtUtc: '2026-10-20T09:00:11.0000000Z' }],
    ['an execution number of zero', { executionNumber: 0 }],
    ['a fractional exit code', { exitCode: 1.5 }],
    ['an exit code past Int32', { exitCode: 2 ** 31 }],
    ['an unknown outcome', { outcome: 'Crashed' }],
    ['a stream with an incoherent capture tuple', { standardOutput: { ...streamFor(guidOf('a', 1), 1), truncated: null } }],
    ['an extra member', { path: 'C:\\x' }],
  ])('refuses a verification with %s', (_name, patch) => {
    expect(answerFor(verificationStepAnswerFor(1, { verification: verificationFor(1, patch) }), 1).kind).toBe('incoherent')
  })

  it('accepts the other statuses in the shapes the host can record', () => {
    const shapes: Record<string, unknown>[] = [
      { status: 'Running', outcome: null, exitCode: null, completedAtUtc: null },
      { status: 'Passed', outcome: 'Exited', exitCode: 0 },
      { status: 'Failed', outcome: 'Failed', exitCode: null },
      { status: 'TimedOut', outcome: 'TimedOut', exitCode: null },
      { status: 'Cancelled', outcome: 'Cancelled', exitCode: null },
      { status: 'Interrupted', outcome: null, exitCode: null },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 0 },
      { status: 'SourceChanged', outcome: null, exitCode: null, dispatchedAtUtc: null },
      { status: 'LocalExecutionSequenceDispatchRefused', outcome: null, exitCode: null, dispatchedAtUtc: null },
    ]
    for (const shape of shapes) {
      expect(answerFor(verificationStepAnswerFor(1, { verification: verificationFor(1, shape) }), 1).kind).toBe('verification')
    }
  })

  it('accepts a step that advanced since the index and refuses one whose link changed', () => {
    const reserved = indexedStep(1, { recordedState: 'Reserved', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null })
    expect(answerFor(verificationStepAnswerFor(1), 1, reserved).kind).toBe('verification')
    expect(answerFor(verificationStepAnswerFor(1), 1, indexedStep(1, { operationId: operationIdFor(8) })).kind).toBe('incoherent')
  })

  it('accepts Unavailable and, only for an unlinked indexed step, NoOperation, each with null payloads', () => {
    const unlinked = indexedStep(1, { recordedState: 'Reserved', operationId: null, claimedAtUtc: null, resultDeadlineUtc: null })
    expect(answerFor(refusedStepAnswerFor(1, 'Unavailable'), 1).kind).toBe('unavailable')
    expect(answerFor(refusedStepAnswerFor(1, 'NoOperation'), 1, unlinked).kind).toBe('no-operation')
    expect(answerFor(refusedStepAnswerFor(1, 'NoOperation'), 1).kind).toBe('incoherent')
    expect(answerFor({ ...refusedStepAnswerFor(1, 'Unavailable'), step: stepFor(1) }, 1).kind).toBe('incoherent')
    expect(answerFor({ ...refusedStepAnswerFor(1, 'NoOperation'), agent: agentFor(1) }, 1, unlinked).kind).toBe('incoherent')
  })
})

const DESCRIPTOR = streamFor(guidOf('a', 1), 100) as Extract<ReturnType<typeof streamFor>, { status: string }> & {
  status: 'Recorded'; artifactId: string; byteLength: number; captureOutcome: 'CapturedWithKnownTruncation'; truncated: boolean
}
const request = (patch = {}) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(1),
  executionId: operationIdFor(1),
  stream: 'stdout' as const,
  descriptor: DESCRIPTOR,
  fromOffset: 0,
  maxBytes: 64,
  ...patch,
})

describe('decideLocalExecutionWindowAnswer', () => {
  it('accepts an Ok window of exactly the request and the descriptor it was chosen from', () => {
    const raw = windowAnswerFor(1, 'stdout', DESCRIPTOR, 'x'.repeat(64), 0)
    expect(decideLocalExecutionWindowAnswer(raw, request())).toEqual({ kind: 'ok', text: 'x'.repeat(64), nextOffset: 64, totalLength: 100 })
  })

  it.each([
    ['another run', { runId: guidOf('r', 2) }],
    ['another sequence', { sequenceId: guidOf('s', 2) }],
    ['another step', { stepId: stepIdFor(2) }],
    ['another stream', { stream: 'stderr' }],
    ['another execution', { executionId: operationIdFor(2) }],
    ['no execution identity', { executionId: null }],
    ['another descriptor', { artifact: streamFor(guidOf('a', 9), 100) }],
    ['another total length', { totalLength: 99 }],
    ['an advance past the cap', { text: 'x'.repeat(65), nextOffset: 65 }],
    ['no advance with bytes remaining', { text: '', nextOffset: 0 }],
    ['text with no advance', { nextOffset: 0 }],
    ['more text units than bytes advanced', { text: 'x'.repeat(10), nextOffset: 5 }],
    ['a cursor past the length', { nextOffset: 101 }],
    ['an unknown status', { status: 'Fine' }],
    ['an extra member', { path: 'a' }],
    ['a fractional cursor', { nextOffset: 1.5 }],
  ])('refuses an Ok window with %s', (_name, patch) => {
    const raw = { ...windowAnswerFor(1, 'stdout', DESCRIPTOR, 'x'.repeat(64), 0), ...patch }
    expect(decideLocalExecutionWindowAnswer(raw, request()).kind).toBe('incoherent')
  })

  it('maps every refusal for the proved execution and Unavailable also for an unproved one', () => {
    for (const [status, kind] of [['NotRecorded', 'not-recorded'], ['Missing', 'missing'], ['IntegrityMismatch', 'integrity-mismatch'], ['Unavailable', 'unavailable']] as const) {
      expect(decideLocalExecutionWindowAnswer(refusedWindowFor(1, 'stdout', status, 3), request({ fromOffset: 3 }))).toEqual({ kind })
    }

    expect(decideLocalExecutionWindowAnswer(refusedWindowFor(1, 'stdout', 'Unavailable', 3, null), request({ fromOffset: 3 }))).toEqual({ kind: 'unavailable' })
    for (const status of ['NotRecorded', 'Missing', 'IntegrityMismatch'] as const) {
      expect(decideLocalExecutionWindowAnswer(refusedWindowFor(1, 'stdout', status, 3, null), request({ fromOffset: 3 })).kind).toBe('incoherent')
    }
  })

  it('refuses a refusal that carries an artifact, text, a moved offset or a length', () => {
    const base = refusedWindowFor(1, 'stdout', 'Missing', 3)
    for (const patch of [{ artifact: DESCRIPTOR }, { text: 'x' }, { nextOffset: 4 }, { totalLength: 5 }]) {
      expect(decideLocalExecutionWindowAnswer({ ...base, ...patch }, request({ fromOffset: 3 })).kind).toBe('incoherent')
    }
  })

  it('keeps an absent descriptor out of an Ok answer', () => {
    const raw = windowAnswerFor(1, 'stdout', absentStream() as never, '', 0)
    expect(decideLocalExecutionWindowAnswer(raw, request()).kind).toBe('incoherent')
  })

  it('is unaffected by CHECKPOINT being referenced', () => {
    expect(CHECKPOINT).toHaveLength(36)
  })
})

describe('toLocalExecutionInspectionIndex: the recorded admission mode (ADR-0062)', () => {
  const headerOf = (raw: unknown) => {
    const accepted = index(raw)
    return accepted?.status === "Recorded" ? accepted.sequence : null
  }
  const withHeader = (patch: Record<string, unknown>) => ({ ...recordedIndexFor(), sequence: headerFor(patch) })

  it('accepts and exposes the recorded Immediate and queued modes', () => {
    expect(headerOf(recordedIndexFor())?.admissionMode).toBe('Immediate')
    expect(headerOf(withHeader({ admissionMode: 'WaitForAdmission' }))?.admissionMode).toBe('WaitForAdmission')
  })

  it('refuses a missing, unknown or differently cased mode', () => {
    const { admissionMode: _omitted, ...missing } = headerFor()
    expect(index({ ...recordedIndexFor(), sequence: missing })).toBeNull()
    expect(index(withHeader({ admissionMode: 'Queued' }))).toBeNull()
    expect(index(withHeader({ admissionMode: 'waitForAdmission' }))).toBeNull()
  })

  it('refuses a recorded waiting state that is not queued, and accepts it as queued bookkeeping', () => {
    const waiting = { recordedState: 'WaitingForAdmission', reasonCode: null, endedAtUtc: null, revision: '1' }
    expect(index(withHeader({ ...waiting, admissionMode: 'Immediate' }))).toBeNull()
    expect(headerOf({ ...withHeader({ ...waiting, admissionMode: 'WaitForAdmission' }), steps: [] })?.recordedState).toBe('WaitingForAdmission')
  })
})
