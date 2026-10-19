import { describe, expect, it } from 'vitest'
import { toAgentAttemptInputMessage } from './agentAttemptInputMessage'
import {
  inputFieldValue,
  inputMessageExpectationFor,
  inputMessageFor,
  recordedInputMessageFor,
  unavailableInputMessageFor,
} from './agentAttemptInputMessageFixture'
import { MESSAGE_SCHEMAS } from './collaborationHistory'

const RUN = 'run-1'
// Position 1 of the reference fixture is a Challenge.
const expected = inputMessageExpectationFor(RUN, 2, 1)

const recorded = (patch: Record<string, unknown> = {}, messagePatch: Record<string, unknown> = {}) =>
  recordedInputMessageFor(RUN, 2, 1, patch, messagePatch)

const without = (value: Record<string, unknown>, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))

describe('toAgentAttemptInputMessage', () => {
  it('accepts a recorded answer for exactly the selected reference with its fields in schema order and fixed labels', () => {
    const answer = toAgentAttemptInputMessage(recorded(), expected)

    expect(answer).toEqual({
      status: 'Recorded',
      input: expected.reference,
      message: {
        id: expected.reference.collaborationMessageId,
        sequence: expected.reference.collaborationSequence,
        occurredAtUtc: expected.reference.occurredAtUtc,
        protocolVersion: '1.0',
        type: 'Challenge',
        provenance: 'ProviderObserved',
        actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
        recipient: { kind: 'Orchestrator', role: null, provider: null },
        content: {
          status: 'Available',
          summary: 'Recorded summary of input 1.',
          fields: MESSAGE_SCHEMAS.Challenge.map(([name, label]) => ({ name, label, value: inputFieldValue(name, 1) })),
        },
      },
    })
    expect(typeof (answer?.status === 'Recorded' && answer.message.occurredAtUtc)).toBe('string')
  })

  it('returns the fields in the schema order even when the host lists them in another order', () => {
    const fields = [...inputMessageFor(RUN, 2, 1).fields].reverse()

    const answer = toAgentAttemptInputMessage(recorded({}, { fields }), expected)

    expect(answer?.status === 'Recorded' && answer.message.content.status === 'Available' && answer.message.content.fields.map((field) => field.name))
      .toEqual(MESSAGE_SCHEMAS.Challenge.map(([name]) => name))
  })

  it('accepts the bare Unavailable answer and nothing more', () => {
    expect(toAgentAttemptInputMessage(unavailableInputMessageFor(RUN, 2, 1), expected)).toEqual({ status: 'Unavailable' })
  })

  it.each([
    ['unavailable content', { contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['unrecognized provenance', { provenance: 'Unrecognized', contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['an unrecognized actor', { actor: { kind: 'Unrecognized', role: null, provider: null }, contentStatus: 'Unavailable', summary: null, fields: [] }],
    ['an incoherent envelope', { provenance: 'HostConstructed', contentStatus: 'Unavailable', summary: null, fields: [] }],
  ])('keeps the reference with %s as Content unavailable', (_, messagePatch) => {
    const answer = toAgentAttemptInputMessage(recorded({}, messagePatch), expected)

    expect(answer?.status).toBe('Recorded')
    expect(answer?.status === 'Recorded' && answer.message.content).toEqual({ status: 'Unavailable' })
    expect(answer?.status === 'Recorded' && answer.input).toEqual(expected.reference)
  })

  it('keeps an exact Int64 sequence above 2^53 as its own text', () => {
    const reference = { ...expected.reference, collaborationSequence: '9007199254740993' }
    const answer = toAgentAttemptInputMessage(
      recorded({ input: { ...recorded().input, collaborationSequence: '9007199254740993' } }, { sequence: '9007199254740993' }),
      { ...expected, reference },
    )

    expect(answer?.status === 'Recorded' && answer.message.sequence).toBe('9007199254740993')
    expect(toAgentAttemptInputMessage(recorded({}, { sequence: '9007199254740993' }), expected)).toBeNull()
  })

  it('never lets Date coercion admit an impossible time text that equals the selected reference', () => {
    const impossible = '2026-02-30T10:00:00.1234567Z'
    const reference = { ...expected.reference, occurredAtUtc: impossible }
    const body = recorded({ input: { ...recorded().input, occurredAtUtc: impossible } }, { occurredAtUtc: impossible })

    expect(new Date(impossible).getTime()).not.toBeNaN()
    expect(toAgentAttemptInputMessage(body, { ...expected, reference })).toBeNull()
  })

  it.each([null, undefined, 'Recorded', 7, [], [recorded()]])('refuses a body that is not an answer object: %j', (body) => {
    expect(toAgentAttemptInputMessage(body, expected)).toBeNull()
  })

  it.each([
    ['an extra root member', recorded({ extra: 'x' })],
    ['a missing root member', without(recorded(), 'message')],
    ['another run', recorded({ runId: 'run-2' })],
    ['another attempt', recorded({ attemptId: 'attempt-x' })],
    ['another requested message', recorded({ collaborationMessageId: 'message-x' })],
    ['an unknown status', recorded({ status: 'Partial' })],
    ['a lowercase status', recorded({ status: 'recorded' })],
    ['Unavailable with an attempt', unavailableInputMessageFor(RUN, 2, 1, { attempt: recorded().attempt })],
    ['Unavailable with an input', unavailableInputMessageFor(RUN, 2, 1, { input: recorded().input })],
    ['Unavailable with a message', unavailableInputMessageFor(RUN, 2, 1, { message: recorded().message })],
    ['Unavailable with undefined members', without(unavailableInputMessageFor(RUN, 2, 1), 'input')],
    ['Recorded without a message', recorded({ message: null })],
    ['Recorded without an input', recorded({ input: null })],
    ['Recorded without an attempt', recorded({ attempt: null })],
  ])('refuses %s', (_, body) => {
    expect(toAgentAttemptInputMessage(body, expected)).toBeNull()
  })

  it.each([
    ['id', 'attempt-x'],
    ['attemptNumber', 3],
    ['attemptNumber', '2'],
    ['role', 'Resolver'],
    ['provider', 'ClaudeCode'],
    ['responseContract', 'ChallengeResolution'],
  ])('refuses an attempt header whose %s is %j', (field, value) => {
    expect(toAgentAttemptInputMessage(recorded({ attempt: { ...recorded().attempt, [field]: value } }), expected)).toBeNull()
  })

  it('refuses an attempt header with an extra or a missing member', () => {
    expect(toAgentAttemptInputMessage(recorded({ attempt: { ...recorded().attempt, status: 'Completed' } }), expected)).toBeNull()
    expect(toAgentAttemptInputMessage(recorded({ attempt: without(recorded().attempt, 'role') }), expected)).toBeNull()
  })

  it.each([
    ['sequence', 2],
    ['sequence', '1'],
    ['collaborationMessageId', 'message-x'],
    ['type', 'Proposal'],
    ['collaborationSequence', '102'],
    ['collaborationSequence', 101],
    ['occurredAtUtc', '2026-10-09T10:01:00.123Z'],
    ['occurredAtUtc', '2026-10-09T10:01:00.1234567+00:00'],
    ['extra', 'x'],
  ])('refuses a selected input whose %s is %j', (field, value) => {
    expect(toAgentAttemptInputMessage(recorded({ input: { ...recorded().input, [field]: value } }), expected)).toBeNull()
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
    expect(toAgentAttemptInputMessage(recorded({}, { [field]: value }), expected)).toBeNull()
  })

  it.each(['summary', 'fields', 'actor', 'recipient', 'contentStatus'])('refuses a message without %s', (field) => {
    expect(toAgentAttemptInputMessage(recorded({ message: without(inputMessageFor(RUN, 2, 1), field) }), expected)).toBeNull()
  })

  it.each([
    ['an extra participant member', { actor: { kind: 'Agent', role: 'Planner', provider: 'Codex', session: 's' } }],
    ['a missing participant member', { recipient: { kind: 'Orchestrator', role: null } }],
    ['an Agent without a provider', { actor: { kind: 'Agent', role: 'Planner', provider: null } }],
    ['an Orchestrator with a role', { recipient: { kind: 'Orchestrator', role: 'Planner', provider: null } }],
    ['identical participants', { recipient: { kind: 'Agent', role: 'Planner', provider: 'Codex' } }],
    ['available content of an incoherent envelope', { provenance: 'HostConstructed' }],
    ['available content of an unrecognized actor', { actor: { kind: 'Unrecognized', role: null, provider: null } }],
    ['a summary past its bound', { summary: 's'.repeat(601) }],
    ['a blank summary', { summary: '   ' }],
    ['a missing field', { fields: inputMessageFor(RUN, 2, 1).fields.slice(1) }],
    ['an extra field', { fields: [...inputMessageFor(RUN, 2, 1).fields, { name: 'scope', value: 'x' }] }],
    ['a duplicated field', { fields: [inputMessageFor(RUN, 2, 1).fields[0], ...inputMessageFor(RUN, 2, 1).fields.slice(0, -1)] }],
    ['a field past its bound', { fields: inputMessageFor(RUN, 2, 1).fields.map((field, index) => (index === 0 ? { ...field, value: 'v'.repeat(901) } : field)) }],
    ['a field with an extra member', { fields: inputMessageFor(RUN, 2, 1).fields.map((field, index) => (index === 0 ? { ...field, raw: '{}' } : field)) }],
    ['unavailable content with a summary', { contentStatus: 'Unavailable', summary: 'x', fields: [] }],
    ['unavailable content with fields', { contentStatus: 'Unavailable', summary: null }],
  ])('refuses %s', (_, messagePatch) => {
    expect(toAgentAttemptInputMessage(recorded({}, messagePatch), expected)).toBeNull()
  })

  it('never repairs, trims or keeps unvalidated members', () => {
    const answer = toAgentAttemptInputMessage(recorded(), expected)

    expect(JSON.stringify(answer)).not.toContain('runId')
    expect(toAgentAttemptInputMessage(recorded({}, { summary: ' Recorded summary of input 1. ' }), expected)).toMatchObject({
      message: { content: { summary: ' Recorded summary of input 1. ' } },
    })
  })
})
