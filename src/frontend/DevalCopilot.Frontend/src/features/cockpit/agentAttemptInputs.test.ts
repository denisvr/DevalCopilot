import { describe, expect, it } from 'vitest'
import { toAgentAttemptInputs } from './agentAttemptInputs'
import {
  emptyInputsFor,
  inputReferenceFor,
  inputsExpectationFor,
  recordedInputsFor,
  unavailableInputsFor,
} from './agentAttemptInputsFixture'

const RUN = 'run-1'
const expected = inputsExpectationFor(RUN, 2)
const resolverExpected = { ...expected, role: 'Resolver' as const, provider: 'Codex' as const, responseContract: 'ChallengeResolution' as const }

/** A recorded answer whose entry at `position` is patched. */
const withEntry = (count: number, position: number, patch: Record<string, unknown>) => {
  const answer = recordedInputsFor(RUN, 2, count)
  answer.inputMessages[position] = { ...answer.inputMessages[position], ...patch }
  return answer
}

describe('toAgentAttemptInputs', () => {
  it('accepts the three closed shapes for exactly the selected attempt', () => {
    expect(toAgentAttemptInputs(unavailableInputsFor(RUN, 2), expected)).toEqual({ status: 'Unavailable' })
    expect(toAgentAttemptInputs(emptyInputsFor(RUN, 2), expected)).toEqual({ status: 'Empty' })

    const recorded = toAgentAttemptInputs(recordedInputsFor(RUN, 2, 3), expected)

    expect(recorded).toEqual({
      status: 'Recorded',
      totalCount: 3,
      omitted: false,
      inputs: [0, 1, 2].map((position) => {
        const reference = inputReferenceFor(RUN, 2, position)
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

  it.each([
    [1, false],
    [10, false],
    [11, true],
    [64, true],
  ])('accepts %i recorded inputs with the first ten and omission exactly past ten', (count, omitted) => {
    const answer = toAgentAttemptInputs(recordedInputsFor(RUN, 2, count), expected)

    expect(answer).toMatchObject({ status: 'Recorded', totalCount: count, omitted })
    expect(answer?.status === 'Recorded' && answer.inputs.length).toBe(Math.min(count, 10))
  })

  // ADR-0053: the prefix length and the omission flag are bound to the limit actually requested; the default stays ten.
  it.each([
    [1, 1],
    [1, 11],
    [10, 11],
    [11, 11],
    [64, 1],
    [64, 10],
    [64, 11],
    [64, 64],
  ])('with limit %i accepts %i recorded inputs with exactly that prefix and omission past it', (limit, count) => {
    const answer = toAgentAttemptInputs(recordedInputsFor(RUN, 2, count, {}, limit), expected, limit)

    expect(answer).toMatchObject({ status: 'Recorded', totalCount: count, omitted: count > limit })
    expect(answer?.status === 'Recorded' && answer.inputs.map((input) => input.position)).toEqual(
      Array.from({ length: Math.min(count, limit) }, (_, position) => position),
    )
  })

  it.each([
    ['a ten-reference answer to a 64 request', recordedInputsFor(RUN, 2, 11), 64],
    ['a 64-reference answer to the default request', recordedInputsFor(RUN, 2, 11, {}, 64), 10],
    ['an omitted answer whose prefix fits the request', recordedInputsFor(RUN, 2, 11, { inputMessagesOmitted: true }, 64), 64],
    ['a one-reference answer to an eleven request', recordedInputsFor(RUN, 2, 11, {}, 1), 11],
  ])('refuses %s', (_, body, limit) => {
    expect(toAgentAttemptInputs(body, expected, limit)).toBeNull()
  })

  it.each([0, -1, 65, 1.5, Number.NaN])('refuses to judge any answer against an impossible requested limit %s', (limit) => {
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 1), expected, limit)).toBeNull()
    expect(toAgentAttemptInputs(unavailableInputsFor(RUN, 2), expected, limit)).toBeNull()
  })

  it('keeps an exact sequence above 2^53 as its own text, never a rounded number', () => {
    const answer = toAgentAttemptInputs(withEntry(2, 1, { collaborationSequence: '9007199254740993' }), expected)

    expect(answer?.status === 'Recorded' && answer.inputs[1].collaborationSequence).toBe('9007199254740993')
    expect(
      toAgentAttemptInputs(withEntry(2, 1, { collaborationSequence: '9223372036854775807' }), expected),
    ).not.toBeNull()
  })

  it.each([
    ['another run', { runId: 'run-9' }],
    ['another attempt', { attemptId: 'run-1-attempt-3' }],
    ['no run', { runId: undefined }],
    ['an unknown status', { status: 'Partial' }],
    ['a lowercase status', { status: 'recorded' }],
    ['no list', { inputMessages: undefined }],
    ['a list that is not an array', { inputMessages: { length: 0 } }],
    ['no omission flag', { inputMessagesOmitted: undefined }],
    ['an omission flag that is not boolean', { inputMessagesOmitted: 0 }],
  ])('refuses an answer for %s', (_, patch) => {
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 3, patch), expected)).toBeNull()
    expect(toAgentAttemptInputs(unavailableInputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it.each([null, undefined, 'Recorded', 7, [], [recordedInputsFor(RUN, 2, 1)]])('refuses a body that is not an answer object: %j', (body) => {
    expect(toAgentAttemptInputs(body, expected)).toBeNull()
  })

  it.each([
    ['a header', { attempt: emptyInputsFor(RUN, 2).attempt }],
    ['a falsy header the generated model would drop', { attempt: false }],
    ['a zero header', { attempt: 0 }],
    ['a missing header member', { attempt: undefined }],
    ['a count', { inputMessageTotalCount: 0 }],
    ['a missing count member', { inputMessageTotalCount: undefined }],
    ['a reference', { inputMessages: [inputReferenceFor(RUN, 2, 0)] }],
    ['an omission', { inputMessagesOmitted: true }],
  ])('refuses Unavailable that carries %s', (_, patch) => {
    expect(toAgentAttemptInputs(unavailableInputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it.each([
    ['no header', { attempt: null }],
    ['a count of one', { inputMessageTotalCount: 1 }],
    ['no count', { inputMessageTotalCount: null }],
    ['a reference', { inputMessages: [inputReferenceFor(RUN, 2, 0)] }],
    ['an omission', { inputMessagesOmitted: true }],
  ])('refuses Empty with %s', (_, patch) => {
    expect(toAgentAttemptInputs(emptyInputsFor(RUN, 2, patch), expected)).toBeNull()
  })

  it('refuses Empty for an attempt that is not a Planner, even when the header claims Planner', () => {
    const resolverHeader = { id: expected.attemptId, attemptNumber: 2, role: 'Resolver', provider: 'Codex', responseContract: 'ChallengeResolution' }

    expect(toAgentAttemptInputs(emptyInputsFor(RUN, 2, { attempt: resolverHeader }), resolverExpected)).toBeNull()
    expect(toAgentAttemptInputs(emptyInputsFor(RUN, 2), resolverExpected)).toBeNull()
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 1, { attempt: resolverHeader }), resolverExpected)).not.toBeNull()
  })

  it.each([
    ['identity', { id: 'run-1-attempt-3' }],
    ['number', { attemptNumber: 3 }],
    ['number as text', { attemptNumber: '2' }],
    ['role', { role: 'Resolver' }],
    ['provider', { provider: 'ClaudeCode' }],
    ['response contract', { responseContract: 'ChallengeResolution' }],
  ])('refuses a header whose %s is not the selected attempt', (_, patch) => {
    const header = { ...emptyInputsFor(RUN, 2).attempt, ...patch }

    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 2, { attempt: header }), expected)).toBeNull()
    expect(toAgentAttemptInputs(emptyInputsFor(RUN, 2, { attempt: header }), expected)).toBeNull()
  })

  it.each([
    ['zero', 0],
    ['past the inspected bound', 65],
    ['fractional', 2.5],
    ['text', '2'],
    ['null', null],
    ['negative', -1],
  ])('refuses a recorded count that is %s', (_, count) => {
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 2, { inputMessageTotalCount: count }), expected)).toBeNull()
  })

  it('refuses an omission flag or a list length that disagrees with the count', () => {
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 11, { inputMessagesOmitted: false }), expected)).toBeNull()
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 10, { inputMessagesOmitted: true }), expected)).toBeNull()
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 3, { inputMessageTotalCount: 4 }), expected)).toBeNull()
    expect(toAgentAttemptInputs(recordedInputsFor(RUN, 2, 3, { inputMessageTotalCount: 2 }), expected)).toBeNull()
    const tooMany = recordedInputsFor(RUN, 2, 11)
    tooMany.inputMessages.push(inputReferenceFor(RUN, 2, 10))
    expect(toAgentAttemptInputs(tooMany, expected)).toBeNull()
  })

  it.each([
    ['a position out of order', { sequence: 2 }],
    ['a position as text', { sequence: '1' }],
    ['no position', { sequence: undefined }],
    ['a reused identity', { collaborationMessageId: inputReferenceFor(RUN, 2, 0).collaborationMessageId }],
    ['a blank identity', { collaborationMessageId: '  ' }],
    ['no identity', { collaborationMessageId: null }],
    ['an unknown type', { type: 'Overlord' }],
    ['a lowercase type', { type: 'challenge' }],
    ['a reused sequence', { collaborationSequence: inputReferenceFor(RUN, 2, 0).collaborationSequence }],
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
  ])('refuses the whole answer for an entry with %s, even after the first', (_, patch) => {
    expect(toAgentAttemptInputs(withEntry(3, 1, patch), expected)).toBeNull()
  })

  it.each([null, 'reference', 3, []])('refuses an entry that is not an object: %j', (item) => {
    const answer = recordedInputsFor(RUN, 2, 2)
    ;(answer.inputMessages as unknown[])[1] = item
    expect(toAgentAttemptInputs(answer, expected)).toBeNull()
  })

  it('never repairs or trims an answer and returns only the validated values', () => {
    const answer = recordedInputsFor(RUN, 2, 1)
    answer.inputMessages[0] = { ...answer.inputMessages[0], summary: 'hidden text' } as never

    const accepted = toAgentAttemptInputs(answer, expected)

    expect(accepted?.status === 'Recorded' && accepted.inputs[0]).not.toHaveProperty('summary')
    expect(JSON.stringify(accepted)).not.toContain('hidden text')
  })
})
