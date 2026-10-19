import { describe, expect, it } from 'vitest'
import { isAuthoredBy, toAgentAttemptOutputs } from './agentAttemptOutputs'
import {
  emptyOutputsFor,
  outputReferenceFor,
  outputsExpectationFor,
  outputsHeaderFor,
  recordedOutputsFor,
  unavailableOutputsFor,
} from './agentAttemptOutputsFixture'

const RUN = 'run-1'
const expected = outputsExpectationFor(RUN, 2)
const resolverExpected = { ...expected, role: 'Resolver' as const, provider: 'Codex' as const, responseContract: 'ChallengeResolution' as const }

const without = (value: Record<string, unknown>, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))

/** A recorded answer whose entry at `position` is patched. */
const withEntry = (count: number, position: number, patch: Record<string, unknown>) => {
  const answer = recordedOutputsFor(RUN, 2, count)
  answer.outputMessages[position] = { ...answer.outputMessages[position], ...patch }
  return answer
}

describe('toAgentAttemptOutputs', () => {
  it('accepts the three closed shapes for exactly the selected attempt', () => {
    expect(toAgentAttemptOutputs(unavailableOutputsFor(RUN, 2), expected)).toEqual({ status: 'Unavailable' })
    expect(toAgentAttemptOutputs(emptyOutputsFor(RUN, 2), expected)).toEqual({ status: 'Empty' })

    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 3), expected)).toEqual({
      status: 'Recorded',
      totalCount: 3,
      outputs: [0, 1, 2].map((position) => {
        const reference = outputReferenceFor(RUN, 2, position)
        return {
          position,
          collaborationMessageId: reference.collaborationMessageId,
          type: reference.type,
          collaborationSequence: reference.collaborationSequence,
          occurredAtUtc: reference.occurredAtUtc,
        }
      }),
    })
  })

  it.each([1, 10, 11, 12, 63, 64])('accepts every one of %i recorded outputs, without a prefix or an omission', (count) => {
    const answer = toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, count), expected)

    expect(answer).toMatchObject({ status: 'Recorded', totalCount: count })
    expect(answer?.status === 'Recorded' && answer.outputs.map((output) => output.position)).toEqual(
      Array.from({ length: count }, (_, position) => position),
    )
  })

  it('accepts an empty set for every role, since a set is empty only as read now', () => {
    for (const [role, provider, responseContract] of [
      ['Planner', 'Codex', 'Proposal'],
      ['CriticalReviewer', 'ClaudeCode', 'CriticalReview'],
      ['Resolver', 'Codex', 'ChallengeResolution'],
      ['Implementer', 'ClaudeCode', 'ImplementationReport'],
      ['CodeReviewer', 'Codex', 'ImplementationReview'],
    ] as const) {
      const header = outputsHeaderFor(RUN, 2, { role, provider, responseContract })
      expect(toAgentAttemptOutputs(emptyOutputsFor(RUN, 2, { attempt: header }), { ...expected, role, provider, responseContract })).toEqual({
        status: 'Empty',
      })
    }
  })

  it('keeps an exact sequence above 2^53 as its own text, never a rounded number', () => {
    const answer = toAgentAttemptOutputs(withEntry(2, 1, { collaborationSequence: '9007199254740993' }), expected)

    expect(answer?.status === 'Recorded' && answer.outputs[1].collaborationSequence).toBe('9007199254740993')
    expect(toAgentAttemptOutputs(withEntry(2, 1, { collaborationSequence: '9223372036854775807' }), expected)).not.toBeNull()
  })

  it.each([
    ['another run', { runId: 'run-9' }],
    ['another attempt', { attemptId: 'run-1-attempt-3' }],
    ['no run', { runId: undefined }],
    ['an unknown status', { status: 'Partial' }],
    ['a lowercase status', { status: 'recorded' }],
    ['no list', { outputMessages: undefined }],
    ['a list that is not an array', { outputMessages: { length: 0 } }],
    ['an extra root member', { outputMessagesOmitted: false }],
    ['an input-style root member', { inputMessages: [] }],
  ])('refuses an answer for %s', (_, patch) => {
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 3, patch), expected)).toBeNull()
    expect(toAgentAttemptOutputs(unavailableOutputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it.each([null, undefined, 'Recorded', 7, [], [recordedOutputsFor(RUN, 2, 1)]])('refuses a body that is not an answer object: %j', (body) => {
    expect(toAgentAttemptOutputs(body, expected)).toBeNull()
  })

  it.each(['attempt', 'outputMessageTotalCount', 'outputMessages', 'status', 'runId', 'attemptId'])('refuses an answer without the %s member', (member) => {
    expect(toAgentAttemptOutputs(without(recordedOutputsFor(RUN, 2, 2), member), expected)).toBeNull()
    expect(toAgentAttemptOutputs(without(unavailableOutputsFor(RUN, 2), member), expected)).toBeNull()
  })

  it.each([
    ['a header', { attempt: outputsHeaderFor(RUN, 2) }],
    ['a falsy header the generated model would drop', { attempt: false }],
    ['a zero header', { attempt: 0 }],
    ['an undefined header', { attempt: undefined }],
    ['a count', { outputMessageTotalCount: 0 }],
    ['an undefined count', { outputMessageTotalCount: undefined }],
    ['a reference', { outputMessages: [outputReferenceFor(RUN, 2, 0)] }],
  ])('refuses Unavailable that carries %s', (_, patch) => {
    expect(toAgentAttemptOutputs(unavailableOutputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it.each([
    ['no header', { attempt: null }],
    ['a count of one', { outputMessageTotalCount: 1 }],
    ['no count', { outputMessageTotalCount: null }],
    ['a reference', { outputMessages: [outputReferenceFor(RUN, 2, 0)] }],
  ])('refuses Empty with %s', (_, patch) => {
    expect(toAgentAttemptOutputs(emptyOutputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it.each([
    ['identity', { id: 'run-1-attempt-3' }],
    ['number', { attemptNumber: 3 }],
    ['number as text', { attemptNumber: '2' }],
    ['role', { role: 'Resolver' }],
    ['provider', { provider: 'ClaudeCode' }],
    ['response contract', { responseContract: 'ChallengeResolution' }],
    ['an extra member', { status: 'Completed' }],
  ])('refuses a header whose %s is not the selected attempt', (_, patch) => {
    const header = { ...outputsHeaderFor(RUN, 2), ...patch }

    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 2, { attempt: header }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(emptyOutputsFor(RUN, 2, { attempt: header }), expected)).toBeNull()
  })

  it('refuses a header with a missing member', () => {
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 2, { attempt: without(outputsHeaderFor(RUN, 2), 'role') }), expected)).toBeNull()
  })

  it.each([
    ['zero', 0],
    ['past the inspected bound', 65],
    ['fractional', 2.5],
    ['text', '2'],
    ['null', null],
    ['negative', -1],
  ])('refuses a recorded count that is %s', (_, count) => {
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 2, { outputMessageTotalCount: count }), expected)).toBeNull()
  })

  it('refuses a list length that disagrees with the count, so no prefix or omission is ever accepted', () => {
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 3, { outputMessageTotalCount: 4 }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 3, { outputMessageTotalCount: 2 }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 11, { outputMessageTotalCount: 64 }), expected)).toBeNull()
    const sixtyFive = recordedOutputsFor(RUN, 2, 64, { outputMessageTotalCount: 65 })
    sixtyFive.outputMessages.push(outputReferenceFor(RUN, 2, 64))
    expect(toAgentAttemptOutputs(sixtyFive, expected)).toBeNull()
  })

  it.each([
    ['a position out of order', { position: 2 }],
    ['a position as text', { position: '1' }],
    ['no position', { position: undefined }],
    ['an input-style position member', { sequence: 1 }],
    ['a reused identity', { collaborationMessageId: outputReferenceFor(RUN, 2, 0).collaborationMessageId }],
    ['a blank identity', { collaborationMessageId: '  ' }],
    ['no identity', { collaborationMessageId: null }],
    ['an unknown type', { type: 'Overlord' }],
    ['a lowercase type', { type: 'question' }],
    ['a human instruction', { type: 'HumanInstruction' }],
    ['a type the Planner may not author', { type: 'Decision' }],
    ['a reused sequence', { collaborationSequence: outputReferenceFor(RUN, 2, 0).collaborationSequence }],
    ['a lower sequence', { collaborationSequence: '99' }],
    ['a numeric sequence', { collaborationSequence: 101 }],
    ['a zero sequence', { collaborationSequence: '0' }],
    ['a negative sequence', { collaborationSequence: '-5' }],
    ['a zero-padded sequence', { collaborationSequence: '0101' }],
    ['a padded sequence', { collaborationSequence: ' 101' }],
    ['a fractional sequence', { collaborationSequence: '101.0' }],
    ['a sequence past Int64', { collaborationSequence: '9223372036854775808' }],
    ['an exponent sequence', { collaborationSequence: '1e3' }],
    ['an impossible date', { occurredAtUtc: '2026-02-30T10:00:00.0000000Z' }],
    ['an hour past the day', { occurredAtUtc: '2026-10-09T24:00:00.0000000Z' }],
    ['an offset instead of UTC', { occurredAtUtc: '2026-10-09T10:00:00.0000000+00:00' }],
    ['fewer fractional digits', { occurredAtUtc: '2026-10-09T10:00:00.123Z' }],
    ['no fraction', { occurredAtUtc: '2026-10-09T10:00:00Z' }],
    ['a lowercase separator', { occurredAtUtc: '2026-10-09t10:00:00.0000000Z' }],
    ['a date only', { occurredAtUtc: '2026-10-09' }],
    ['a number time', { occurredAtUtc: 1 }],
    ['a Date object', { occurredAtUtc: new Date('2026-10-09T10:00:00Z') }],
    ['no time', { occurredAtUtc: undefined }],
    ['an extra member', { summary: 'hidden text' }],
  ])('refuses the whole answer for an entry with %s, even after the first', (_, patch) => {
    expect(toAgentAttemptOutputs(withEntry(3, 1, patch), expected)).toBeNull()
  })

  it('judges the last of sixty-four references as strictly as the first', () => {
    expect(toAgentAttemptOutputs(withEntry(64, 63, { type: 'Decision' }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(withEntry(64, 63, { position: 62 }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(withEntry(64, 63, { collaborationSequence: '100' }), expected)).toBeNull()
    expect(toAgentAttemptOutputs(withEntry(64, 63, {}), expected)).not.toBeNull()
  })

  it.each([null, 'reference', 3, []])('refuses an entry that is not an object: %j', (item) => {
    const answer = recordedOutputsFor(RUN, 2, 2)
    ;(answer.outputMessages as unknown[])[1] = item
    expect(toAgentAttemptOutputs(answer, expected)).toBeNull()
  })

  it('judges every reference against the role table of the selected attempt', () => {
    const resolverHeader = outputsHeaderFor(RUN, 2, { role: 'Resolver', provider: 'Codex', responseContract: 'ChallengeResolution' })
    const decision = recordedOutputsFor(RUN, 2, 1, { attempt: resolverHeader })
    decision.outputMessages[0] = { ...decision.outputMessages[0], type: 'Decision' }

    expect(toAgentAttemptOutputs(decision, resolverExpected)).toMatchObject({ status: 'Recorded' })
    expect(toAgentAttemptOutputs(decision, expected)).toBeNull()
    decision.outputMessages[0] = { ...decision.outputMessages[0], type: 'Acceptance' }
    expect(toAgentAttemptOutputs(decision, resolverExpected)).toBeNull()
  })

  it('states the Domain author policy exactly', () => {
    const table = {
      Planner: ['Proposal', 'Question', 'Escalation'],
      CriticalReviewer: ['Acceptance', 'Challenge', 'Question', 'Escalation'],
      Resolver: ['Decision', 'Proposal', 'Question', 'Escalation'],
      Implementer: ['ExecutionReport', 'RevisionResponse', 'Question', 'Escalation'],
      CodeReviewer: ['ReviewApproval', 'ReviewFinding', 'Question', 'Escalation'],
    } as const
    const every = [
      'Proposal', 'Acceptance', 'Challenge', 'Question', 'Decision', 'ExecutionReport', 'ReviewFinding', 'RevisionResponse', 'Escalation',
      'ReviewApproval', 'HumanInstruction',
    ]

    for (const [role, allowed] of Object.entries(table)) {
      expect(every.filter((type) => isAuthoredBy(role as keyof typeof table, type))).toEqual(every.filter((type) => allowed.includes(type as never)))
    }
    expect(isAuthoredBy('Overlord' as never, 'Proposal')).toBe(false)
    expect(isAuthoredBy('toString' as never, 'Proposal')).toBe(false)
    expect(isAuthoredBy('Planner', undefined)).toBe(false)
  })

  it('never repairs or trims an answer and returns only the validated values', () => {
    const accepted = toAgentAttemptOutputs(recordedOutputsFor(RUN, 2, 1), expected)

    expect(accepted?.status === 'Recorded' && Object.keys(accepted.outputs[0]).sort()).toEqual(
      ['collaborationMessageId', 'collaborationSequence', 'occurredAtUtc', 'position', 'type'],
    )
    expect(JSON.stringify(accepted)).not.toContain('runId')
  })
})
