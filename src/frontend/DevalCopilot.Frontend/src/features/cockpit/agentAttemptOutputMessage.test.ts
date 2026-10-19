import { describe, expect, it } from 'vitest'
import { toAgentAttemptOutputMessage } from './agentAttemptOutputMessage'
import {
  outputFieldValue,
  outputMessageExpectationFor,
  outputMessageFor,
  recordedOutputMessageFor,
  unavailableOutputMessageFor,
} from './agentAttemptOutputMessageFixture'
import { MESSAGE_SCHEMAS } from './collaborationHistory'

const RUN = 'run-1'
// Position 1 of the reference fixture is a Question.
const expected = outputMessageExpectationFor(RUN, 2, 1)

const recorded = (patch: Record<string, unknown> = {}, messagePatch: Record<string, unknown> = {}) =>
  recordedOutputMessageFor(RUN, 2, 1, patch, messagePatch)

const without = (value: Record<string, unknown>, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))

describe('toAgentAttemptOutputMessage', () => {
  it('accepts a recorded answer for exactly the selected reference with its fields in schema order and fixed labels', () => {
    const answer = toAgentAttemptOutputMessage(recorded(), expected)

    expect(answer).toEqual({
      status: 'Recorded',
      output: expected.reference,
      message: {
        id: expected.reference.collaborationMessageId,
        sequence: expected.reference.collaborationSequence,
        occurredAtUtc: expected.reference.occurredAtUtc,
        protocolVersion: '1.0',
        type: 'Question',
        provenance: 'ProviderObserved',
        actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
        recipient: { kind: 'Orchestrator', role: null, provider: null },
        content: {
          status: 'Available',
          summary: 'Recorded summary of output 1.',
          fields: MESSAGE_SCHEMAS.Question.map(([name, label]) => ({ name, label, value: outputFieldValue(name, 1) })),
        },
      },
    })
    expect(typeof (answer?.status === 'Recorded' && answer.message.occurredAtUtc)).toBe('string')
  })

  it('returns the fields in the schema order even when the host lists them in another order', () => {
    const fields = [...outputMessageFor(RUN, 2, 1).fields].reverse()

    const answer = toAgentAttemptOutputMessage(recorded({}, { fields }), expected)

    expect(answer?.status === 'Recorded' && answer.message.content.status === 'Available' && answer.message.content.fields.map((field) => field.name))
      .toEqual(MESSAGE_SCHEMAS.Question.map(([name]) => name))
  })

  it('accepts the bare Unavailable answer and nothing more', () => {
    expect(toAgentAttemptOutputMessage(unavailableOutputMessageFor(RUN, 2, 1), expected)).toEqual({ status: 'Unavailable' })
  })

  it.each([
    ['unavailable content', { contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['an unrecognized recipient', { recipient: { kind: 'Unrecognized', role: null, provider: null }, contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['an incoherent envelope', { recipient: { kind: 'Agent', role: 'Planner', provider: 'Codex' }, contentStatus: 'Unavailable', summary: null, fields: [] }],
  ])('keeps the reference with %s as Content unavailable', (_, messagePatch) => {
    const answer = toAgentAttemptOutputMessage(recorded({}, messagePatch), expected)

    expect(answer?.status).toBe('Recorded')
    expect(answer?.status === 'Recorded' && answer.message.content).toEqual({ status: 'Unavailable' })
    expect(answer?.status === 'Recorded' && answer.output).toEqual(expected.reference)
  })

  it.each([
    ['unrecognized provenance', { provenance: 'Unrecognized', contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['host-constructed provenance', { provenance: 'HostConstructed', contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['an unrecognized actor', { actor: { kind: 'Unrecognized', role: null, provider: null }, contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['another actor role', { actor: { kind: 'Agent', role: 'Resolver', provider: 'Codex' } }],
    ['another actor provider', { actor: { kind: 'Agent', role: 'Planner', provider: 'ClaudeCode' } }],
    ['an Orchestrator actor', { actor: { kind: 'Orchestrator', role: null, provider: null }, recipient: { kind: 'Human', role: null, provider: null } }],
  ])('refuses a Recorded answer whose message has %s, since the host proves the author before it answers Recorded', (_, messagePatch) => {
    expect(toAgentAttemptOutputMessage(recorded({}, messagePatch), expected)).toBeNull()
  })

  it('keeps an exact Int64 sequence above 2^53 as its own text', () => {
    const reference = { ...expected.reference, collaborationSequence: '9007199254740993' }
    const answer = toAgentAttemptOutputMessage(
      recorded({ output: { ...recorded().output, collaborationSequence: '9007199254740993' } }, { sequence: '9007199254740993' }),
      { ...expected, reference },
    )

    expect(answer?.status === 'Recorded' && answer.message.sequence).toBe('9007199254740993')
    expect(toAgentAttemptOutputMessage(recorded({}, { sequence: '9007199254740993' }), expected)).toBeNull()
  })

  it('never lets Date coercion admit an impossible time text that equals the selected reference', () => {
    const impossible = '2026-02-30T10:00:00.1234567Z'
    const reference = { ...expected.reference, occurredAtUtc: impossible }
    const body = recorded({ output: { ...recorded().output, occurredAtUtc: impossible } }, { occurredAtUtc: impossible })

    expect(new Date(impossible).getTime()).not.toBeNaN()
    expect(toAgentAttemptOutputMessage(body, { ...expected, reference })).toBeNull()
  })

  it.each([null, undefined, 'Recorded', 7, [], [recorded()]])('refuses a body that is not an answer object: %j', (body) => {
    expect(toAgentAttemptOutputMessage(body, expected)).toBeNull()
  })

  it.each([
    ['an extra root member', recorded({ extra: 'x' })],
    ['an input-style root member', recorded({ input: recorded().output })],
    ['a missing root member', without(recorded(), 'message')],
    ['another run', recorded({ runId: 'run-2' })],
    ['another attempt', recorded({ attemptId: 'attempt-x' })],
    ['another requested message', recorded({ collaborationMessageId: 'message-x' })],
    ['an unknown status', recorded({ status: 'Partial' })],
    ['a lowercase status', recorded({ status: 'recorded' })],
    ['Unavailable with an attempt', unavailableOutputMessageFor(RUN, 2, 1, { attempt: recorded().attempt })],
    ['Unavailable with an output', unavailableOutputMessageFor(RUN, 2, 1, { output: recorded().output })],
    ['Unavailable with a message', unavailableOutputMessageFor(RUN, 2, 1, { message: recorded().message })],
    ['Unavailable with undefined members', without(unavailableOutputMessageFor(RUN, 2, 1), 'output')],
    ['Recorded without a message', recorded({ message: null })],
    ['Recorded without an output', recorded({ output: null })],
    ['Recorded without an attempt', recorded({ attempt: null })],
  ])('refuses %s', (_, body) => {
    expect(toAgentAttemptOutputMessage(body, expected)).toBeNull()
  })

  it.each([
    ['id', 'attempt-x'],
    ['attemptNumber', 3],
    ['attemptNumber', '2'],
    ['role', 'Resolver'],
    ['provider', 'ClaudeCode'],
    ['responseContract', 'ChallengeResolution'],
  ])('refuses an attempt header whose %s is %j', (field, value) => {
    expect(toAgentAttemptOutputMessage(recorded({ attempt: { ...recorded().attempt, [field]: value } }), expected)).toBeNull()
  })

  it('refuses an attempt header with an extra or a missing member', () => {
    expect(toAgentAttemptOutputMessage(recorded({ attempt: { ...recorded().attempt, status: 'Completed' } }), expected)).toBeNull()
    expect(toAgentAttemptOutputMessage(recorded({ attempt: without(recorded().attempt, 'role') }), expected)).toBeNull()
  })

  it.each([
    ['position', 2],
    ['position', '1'],
    ['collaborationMessageId', 'message-x'],
    ['type', 'Proposal'],
    ['collaborationSequence', '102'],
    ['collaborationSequence', 101],
    ['occurredAtUtc', '2026-10-09T10:01:00.123Z'],
    ['occurredAtUtc', '2026-10-09T10:01:00.1234567+00:00'],
    ['extra', 'x'],
  ])('refuses a selected output whose %s is %j', (field, value) => {
    expect(toAgentAttemptOutputMessage(recorded({ output: { ...recorded().output, [field]: value } }), expected)).toBeNull()
  })

  it.each([
    ['runId', 'run-2'],
    ['id', 'message-x'],
    ['sequence', '102'],
    ['sequence', 101],
    ['occurredAtUtc', '2026-10-09T10:01:00.123Z'],
    ['occurredAtUtc', '2026-10-09T10:01:00.1234567+00:00'],
    ['occurredAtUtc', '2026-10-09T10:02:00.1234567Z'],
    ['protocolVersion', 'Unrecognized'],
    ['protocolVersion', '2.0'],
    ['type', 'Proposal'],
    ['type', 'Unrecognized'],
    ['provenance', 'Forged'],
    ['contentStatus', 'available'],
    ['extra', 'x'],
    ['attemptId', 'attempt-2'],
    ['inReplyToMessageId', 'message-0'],
  ])('refuses a message whose %s is %j', (field, value) => {
    expect(toAgentAttemptOutputMessage(recorded({}, { [field]: value }), expected)).toBeNull()
  })

  it.each(['summary', 'fields', 'actor', 'recipient', 'contentStatus'])('refuses a message without %s', (field) => {
    expect(toAgentAttemptOutputMessage(recorded({ message: without(outputMessageFor(RUN, 2, 1), field) }), expected)).toBeNull()
  })

  it.each([
    ['an extra participant member', { actor: { kind: 'Agent', role: 'Planner', provider: 'Codex', session: 's' } }],
    ['a missing participant member', { recipient: { kind: 'Orchestrator', role: null } }],
    ['an Agent without a provider', { actor: { kind: 'Agent', role: 'Planner', provider: null } }],
    ['an Orchestrator with a role', { recipient: { kind: 'Orchestrator', role: 'Planner', provider: null } }],
    ['identical participants', { recipient: { kind: 'Agent', role: 'Planner', provider: 'Codex' } }],
    ['a summary past its bound', { summary: 's'.repeat(601) }],
    ['a blank summary', { summary: '   ' }],
    ['a missing field', { fields: outputMessageFor(RUN, 2, 1).fields.slice(1) }],
    ['an extra field', { fields: [...outputMessageFor(RUN, 2, 1).fields, { name: 'scope', value: 'x' }] }],
    ['a duplicated field', { fields: [outputMessageFor(RUN, 2, 1).fields[0], ...outputMessageFor(RUN, 2, 1).fields.slice(0, -1)] }],
    ['a field past its bound', { fields: outputMessageFor(RUN, 2, 1).fields.map((field, index) => (index === 0 ? { ...field, value: 'v'.repeat(901) } : field)) }],
    ['a field with an extra member', { fields: outputMessageFor(RUN, 2, 1).fields.map((field, index) => (index === 0 ? { ...field, raw: '{}' } : field)) }],
    ['unavailable content with a summary', { contentStatus: 'Unavailable', summary: 'x', fields: [] }],
    ['unavailable content with fields', { contentStatus: 'Unavailable', summary: null }],
  ])('refuses %s', (_, messagePatch) => {
    expect(toAgentAttemptOutputMessage(recorded({}, messagePatch), expected)).toBeNull()
  })

  it('never repairs, trims or keeps unvalidated members', () => {
    const answer = toAgentAttemptOutputMessage(recorded(), expected)

    expect(JSON.stringify(answer)).not.toContain('runId')
    expect(toAgentAttemptOutputMessage(recorded({}, { summary: ' Recorded summary of output 1. ' }), expected)).toMatchObject({
      message: { content: { summary: ' Recorded summary of output 1. ' } },
    })
  })
})
