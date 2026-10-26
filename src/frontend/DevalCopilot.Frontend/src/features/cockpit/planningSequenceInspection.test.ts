import { describe, expect, it } from 'vitest'
import { planningInspectionContractOf, toPlanningInspectionIndex, toPlanningStepAnswer } from './planningSequenceInspection'
import type { PlanningInspectionStep } from './planningSequenceInspection'
import {
  FIVE_STEPS,
  RUN,
  SEQUENCE,
  agentFor,
  attemptIdFor,
  guidOf,
  headerFor,
  instantAt,
  notRecordedIndexFor,
  proposalFor,
  recordedIndexFor,
  refusedStepAnswerFor,
  reservedStepFor,
  stepAnswerFor,
  stepFor,
  stepIdFor,
  unavailableIndexFor,
} from './planningSequenceInspectionFixture'

const decode = (index: unknown) => toPlanningInspectionIndex(index, RUN)
const withSteps = (steps: readonly Record<string, unknown>[], header: Record<string, unknown> = {}) =>
  recordedIndexFor(RUN, steps, { sequence: headerFor(header) })

function indexedStep(ordinal: number, patch: Record<string, unknown> = {}): PlanningInspectionStep {
  const index = decode(withSteps([...FIVE_STEPS.slice(0, ordinal), stepFor(ordinal, patch)]))
  if (index?.status !== 'Recorded') {
    throw new Error('the indexed step fixture must decode')
  }

  return index.steps[ordinal]
}

describe('toPlanningInspectionIndex', () => {
  it('accepts zero through five recorded steps with their exact members', () => {
    for (let count = 0; count <= 5; count += 1) {
      const steps = FIVE_STEPS.slice(0, count)
      const index = decode(withSteps(steps, count === 0 ? { recordedState: 'Armed', reasonCode: null, endedAtUtc: null } : {}))
      expect(index?.status, `${count} steps`).toBe('Recorded')
      expect(index?.status === 'Recorded' ? index.steps : null).toEqual(steps)
    }

    const full = decode(recordedIndexFor())
    expect(full?.status === 'Recorded' ? full.sequence : null).toEqual(headerFor())
  })

  it('accepts NotRecorded and Unavailable only with a null header and no steps', () => {
    expect(decode(notRecordedIndexFor())).toEqual({ status: 'NotRecorded' })
    expect(decode(unavailableIndexFor())).toEqual({ status: 'Unavailable' })
    expect(decode({ ...notRecordedIndexFor(), sequence: headerFor() })).toBeNull()
    expect(decode({ ...unavailableIndexFor(), steps: [stepFor(0)] })).toBeNull()
    expect(decode({ ...recordedIndexFor(), status: 'Other' })).toBeNull()
  })

  it('refuses a sixth step wholly', () => {
    expect(decode(withSteps([...FIVE_STEPS, stepFor(5)]))).toBeNull()
    expect(decode(withSteps([...FIVE_STEPS, stepFor(4, { stepId: guidOf('t', 9), attemptId: guidOf('a', 9) })]))).toBeNull()
  })

  it('refuses every wrong role, provider and contract of an ordinal', () => {
    for (const ordinal of [0, 1, 2, 3, 4]) {
      const steps = FIVE_STEPS.slice(0, ordinal)
      expect(decode(withSteps([...steps, stepFor(ordinal, { role: 'Implementer' })]))).toBeNull()
      expect(decode(withSteps([...steps, stepFor(ordinal, { role: ordinal === 0 ? 'Resolver' : 'Planner' })]))).toBeNull()
      expect(decode(withSteps([...steps, stepFor(ordinal, { provider: ordinal % 2 === 0 ? 'ClaudeCode' : 'Codex' })]))).toBeNull()
      expect(decode(withSteps([...steps, stepFor(ordinal, { responseContract: 'ImplementationReport' })]))).toBeNull()
    }

    expect(planningInspectionContractOf(3)).toBe('CriticalReview')
    expect(planningInspectionContractOf(5)).toBeNull()
  })

  it('refuses gaps, repeats and reordering of ordinals, duplicate steps and duplicate attempts', () => {
    expect(decode(withSteps([FIVE_STEPS[0], FIVE_STEPS[2]]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[1]]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[1], FIVE_STEPS[0]]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { stepId: stepIdFor(0) })]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { attemptId: attemptIdFor(0) })]))).toBeNull()
  })

  it('applies the state, link and reason shape', () => {
    expect(decode(withSteps([FIVE_STEPS[0], reservedStepFor(1)]))?.status).toBe('Recorded')
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { recordedState: 'Claimed' })]))?.status).toBe('Recorded')
    expect(decode(withSteps([stepFor(0, { recordedState: 'Revoked', attemptId: null, reasonCode: 'preparation_expired' })]))?.status).toBe('Recorded')
    expect(decode(withSteps([stepFor(0, { recordedState: 'Unresolved', attemptId: null, reasonCode: 'preparation_unresolved' })]))?.status).toBe(
      'Recorded',
    )

    expect(decode(withSteps([reservedStepFor(0, { attemptId: attemptIdFor(0) })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { attemptId: null })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Claimed', attemptId: null })]))).toBeNull()
    expect(decode(withSteps([reservedStepFor(0, { reasonCode: 'step_failed' })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { reasonCode: 'step_failed' })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Revoked', attemptId: null, reasonCode: null })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Revoked', attemptId: null, reasonCode: 'made_up' })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Revoked', attemptId: attemptIdFor(0), reasonCode: 'step_failed' })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Unknown' })]))).toBeNull()
  })

  it('lets only the last step be outstanding, revoked or unresolved', () => {
    expect(decode(withSteps([reservedStepFor(0), reservedStepFor(1)]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { recordedState: 'Claimed' }), stepFor(1)]))).toBeNull()
    expect(
      decode(withSteps([stepFor(0, { recordedState: 'Revoked', attemptId: null, reasonCode: 'preparation_refused' }), stepFor(1)])),
    ).toBeNull()
  })

  it('names the exact predecessor attempt and input Proposal', () => {
    expect(decode(withSteps([stepFor(0, { predecessorAttemptId: attemptIdFor(1) })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { inputProposalMessageId: proposalFor(1) })]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { predecessorAttemptId: null })]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { inputProposalMessageId: null })]))).toBeNull()
    expect(decode(withSteps([FIVE_STEPS[0], stepFor(1, { predecessorAttemptId: guidOf('a', 77) })]))).toBeNull()
    // A Resolver names the very Proposal of the review before it; a reviewer may name another.
    expect(decode(withSteps([...FIVE_STEPS.slice(0, 2), stepFor(2, { inputProposalMessageId: guidOf('p', 77) })]))).toBeNull()
    expect(decode(withSteps([...FIVE_STEPS.slice(0, 3), stepFor(3, { inputProposalMessageId: guidOf('p', 77) })]))?.status).toBe('Recorded')
  })

  it('applies the sequence state, reason and time shape', () => {
    expect(decode(withSteps([], { recordedState: 'Armed', reasonCode: null, endedAtUtc: null }))?.status).toBe('Recorded')
    expect(decode(withSteps([], { recordedState: 'Armed', reasonCode: 'plan_accepted', endedAtUtc: null }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Armed', reasonCode: null, endedAtUtc: instantAt(50) }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Stopped', reasonCode: null, stopRequestedAtUtc: instantAt(40) }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Stopped', reasonCode: 'stopped_before_claim', stopRequestedAtUtc: instantAt(40), endedAtUtc: null }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Stopped', reasonCode: 'made_up', stopRequestedAtUtc: instantAt(40) }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Bogus' }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'NeedsAttention', reasonCode: 'step_failed' }))?.status).toBe('Recorded')
  })

  it('requires a stop request exactly for Stopping and Stopped, or an ended state', () => {
    const live = { recordedState: 'Stopping', reasonCode: null, endedAtUtc: null }
    expect(decode(withSteps([stepFor(0, { recordedState: 'Claimed' })], { ...live, stopRequestedAtUtc: instantAt(30) }))?.status).toBe('Recorded')
    expect(decode(withSteps([stepFor(0, { recordedState: 'Claimed' })], { ...live, stopRequestedAtUtc: null }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Armed', reasonCode: null, endedAtUtc: null, stopRequestedAtUtc: instantAt(30) }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'Stopped', reasonCode: 'stopped_after_step', stopRequestedAtUtc: null }))).toBeNull()
    expect(decode(withSteps([], { recordedState: 'NeedsAttention', reasonCode: 'step_failed', stopRequestedAtUtc: instantAt(30) }))?.status).toBe(
      'Recorded',
    )
  })

  it('orders times at tick precision with the canonical text, not the millisecond instant', () => {
    const stamp = (second: number, ticks: string) => `2026-10-20T09:00:${String(second).padStart(2, '0')}.${ticks}Z`
    const ended = (updated: string, ended: string) => withSteps([], { updatedAtUtc: updated, endedAtUtc: ended })
    expect(decode(ended(stamp(50, '0000002'), stamp(50, '0000002')))?.status).toBe('Recorded')
    expect(decode(ended(stamp(50, '0000001'), stamp(50, '0000002')))).toBeNull()
    expect(decode(withSteps([], { armedAtUtc: stamp(10, '0000002'), updatedAtUtc: stamp(10, '0000001'), endedAtUtc: stamp(10, '0000001') }))).toBeNull()
    expect(decode(withSteps([stepFor(0, { preparationDeadlineUtc: stamp(5, '0000001'), preparationReservedAtUtc: stamp(5, '0000001') })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { preparationDeadlineUtc: stamp(5, '0000002'), preparationReservedAtUtc: stamp(5, '0000001') })]))?.status).toBe(
      'Recorded',
    )
    expect(decode(withSteps([stepFor(0, { updatedAtUtc: stamp(0, '0000000'), preparationReservedAtUtc: stamp(0, '0000001'), preparationDeadlineUtc: stamp(9, '0000000') })]))).toBeNull()
  })

  it('refuses malformed identifiers, fingerprints, times and Int64 revisions', () => {
    const upper = SEQUENCE.toUpperCase()
    expect(decode(withSteps([], { sequenceId: upper }))).toBeNull()
    expect(decode(withSteps([], { sequenceId: '00000000-0000-0000-0000-000000000000' }))).toBeNull()
    expect(decode(withSteps([], { workspaceId: 'not-a-guid' }))).toBeNull()
    expect(decode(withSteps([], { fingerprintSha256: 'A'.repeat(64) }))).toBeNull()
    expect(decode(withSteps([], { fingerprintSha256: 'a'.repeat(63) }))).toBeNull()
    expect(decode(withSteps([], { armedAtUtc: '2026-10-20T09:00:00Z' }))).toBeNull()
    expect(decode(withSteps([], { updatedAtUtc: '2026-10-20T09:00:50.0000000+00:00' }))).toBeNull()
    for (const revision of ['0', '-1', '01', '1.0', ' 1', '1 ', '', 'x', 9007199254740992, 1, null, '9007199254740992', '9223372036854775808']) {
      expect(decode(withSteps([], { revision })), String(revision)).toBeNull()
      expect(decode(withSteps([stepFor(0, { revision })])), `step ${String(revision)}`).toBeNull()
    }

    expect(decode(withSteps([], { revision: '9007199254740991' }))?.status).toBe('Recorded')
  })

  it('refuses unexpected, missing and mistyped members at every level, and another run', () => {
    expect(decode({ ...recordedIndexFor(), extra: 1 })).toBeNull()
    expect(decode({ ...recordedIndexFor(), runId: guidOf('r', 2) })).toBeNull()
    expect(decode({ ...recordedIndexFor(), steps: null })).toBeNull()
    expect(decode({ ...recordedIndexFor(), sequence: { ...headerFor(), extra: 1 } })).toBeNull()
    expect(decode({ ...recordedIndexFor(), sequence: { ...headerFor(), preparationOwnerToken: guidOf('o', 1) } })).toBeNull()
    expect(decode(withSteps([{ ...stepFor(0), extra: 1 }]))).toBeNull()
    const { attemptId: _omitted, ...missing } = stepFor(0)
    expect(decode(withSteps([missing]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { ordinal: '0' })]))).toBeNull()
    expect(decode(withSteps([stepFor(0, { reasonCode: undefined })]))).toBeNull()
    for (const answer of [undefined, null, 'x', 1, [], {}]) {
      expect(decode(answer)).toBeNull()
    }
  })
})

describe('toPlanningStepAnswer', () => {
  const request = (ordinal: number, patch: Record<string, unknown> = {}) => ({ runId: RUN, sequenceId: SEQUENCE, step: indexedStep(ordinal, patch) })

  it('accepts a linked step with the immutable identity of exactly that attempt', () => {
    for (const ordinal of [0, 1, 2, 3, 4]) {
      const answer = toPlanningStepAnswer(stepAnswerFor(ordinal), request(ordinal))
      expect(answer.kind).toBe('agent')
      expect(answer.kind === 'agent' ? answer.agent : null).toEqual({
        attemptId: attemptIdFor(ordinal),
        attemptNumber: ordinal + 1,
        role: agentFor(ordinal).role,
        provider: agentFor(ordinal).provider,
        responseContract: agentFor(ordinal).responseContract,
      })
    }
  })

  it('accepts NoLinkedAttempt only for a step the index showed without a link, and Unavailable always', () => {
    expect(toPlanningStepAnswer(refusedStepAnswerFor(1, 'NoLinkedAttempt'), request(1, { recordedState: 'Reserved', attemptId: null, revision: '1' }))).toEqual({
      kind: 'no-linked-attempt',
    })
    expect(toPlanningStepAnswer(refusedStepAnswerFor(1, 'NoLinkedAttempt'), request(1))).toEqual({ kind: 'incoherent' })
    expect(toPlanningStepAnswer(refusedStepAnswerFor(1, 'Unavailable'), request(1))).toEqual({ kind: 'unavailable' })
    expect(toPlanningStepAnswer({ ...refusedStepAnswerFor(1, 'Unavailable'), step: stepFor(1) }, request(1))).toEqual({ kind: 'incoherent' })
    expect(toPlanningStepAnswer({ ...refusedStepAnswerFor(1, 'NoLinkedAttempt'), agent: agentFor(1) }, request(1, { attemptId: null, recordedState: 'Reserved' }))).toEqual({
      kind: 'incoherent',
    })
  })

  it('refuses another run, sequence or step, an unknown status and unexpected members', () => {
    const base = stepAnswerFor(1)
    for (const answer of [
      { ...base, runId: guidOf('r', 2) },
      { ...base, sequenceId: guidOf('s', 2) },
      { ...base, stepId: stepIdFor(2) },
      { ...base, status: 'Other' },
      { ...base, extra: 1 },
      undefined,
      null,
    ]) {
      expect(toPlanningStepAnswer(answer, request(1))).toEqual({ kind: 'incoherent' })
    }
  })

  it('requires the very indexed step, advancing only forward and never losing or changing its link', () => {
    const claimed = request(1, { recordedState: 'Claimed', revision: '2' })
    expect(toPlanningStepAnswer(stepAnswerFor(1, { step: stepFor(1, { recordedState: 'Concluded', revision: '3' }) }), claimed).kind).toBe('agent')
    expect(toPlanningStepAnswer(stepAnswerFor(1, { step: stepFor(1, { recordedState: 'Claimed', revision: '2' }) }), claimed).kind).toBe('agent')

    const concluded = request(1)
    for (const patch of [
      { recordedState: 'Claimed' },
      { revision: '2' },
      { updatedAtUtc: instantAt(10) },
      { attemptId: guidOf('a', 77) },
      { predecessorAttemptId: guidOf('a', 78) },
      { inputProposalMessageId: guidOf('p', 78) },
      { preparationReservedAtUtc: instantAt(11), preparationDeadlineUtc: instantAt(15) },
      { stepId: guidOf('t', 77) },
    ]) {
      expect(toPlanningStepAnswer(stepAnswerFor(1, { step: stepFor(1, patch) }), concluded), JSON.stringify(patch)).toEqual({ kind: 'incoherent' })
    }

    // A Reserved step may legitimately be linked by the time it is read, but a link must be present in a Recorded answer.
    const reserved = request(1, { recordedState: 'Reserved', attemptId: null, revision: '1' })
    expect(toPlanningStepAnswer(stepAnswerFor(1), reserved).kind).toBe('agent')
    expect(toPlanningStepAnswer(stepAnswerFor(1, { step: reservedStepFor(1) }), reserved)).toEqual({ kind: 'incoherent' })
    expect(toPlanningStepAnswer(stepAnswerFor(1, { step: null }), reserved)).toEqual({ kind: 'incoherent' })
  })

  it('refuses an Agent header that is not the linked attempt in the step\'s fixed identity', () => {
    for (const agent of [
      agentFor(1, { id: attemptIdFor(0) }),
      agentFor(1, { attemptNumber: 0 }),
      agentFor(1, { attemptNumber: 1.5 }),
      agentFor(1, { attemptNumber: '2' }),
      agentFor(1, { role: 'Resolver' }),
      agentFor(1, { provider: 'Codex' }),
      agentFor(1, { responseContract: 'ChallengeResolution' }),
      agentFor(1, { role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ImplementationReport' }),
      { ...agentFor(1), extra: 1 },
      { ...agentFor(1), status: 'Completed' },
      null,
    ]) {
      expect(toPlanningStepAnswer(stepAnswerFor(1, { agent }), request(1)), JSON.stringify(agent)).toEqual({ kind: 'incoherent' })
    }
  })
})
