import { describe, expect, it } from 'vitest'
import { MESSAGE_SCHEMAS } from './collaborationHistory'
import type { KnownMessageType } from './collaborationHistory'
import { toCollaborationReply } from './collaborationReply'
import type { CollaborationReplySource } from './collaborationReply'
import { entryFor, fieldsFor, replyFor, unavailableEntryFor } from './collaborationHistoryFixture'

const source = (overrides: Partial<CollaborationReplySource> = {}): CollaborationReplySource => ({
  runId: 'run-1',
  messageId: 'message-20',
  sequence: '20',
  type: 'Acceptance',
  protocolVersion: '1.0',
  ...overrides,
})

const parentOf = (type: KnownMessageType, sequence: string | number = 10, patch: Record<string, unknown> = {}) =>
  entryFor(sequence, { type, fields: fieldsFor(type), ...patch })

const available = (observed: CollaborationReplySource, parent = parentOf('Proposal')) =>
  replyFor({ id: observed.messageId, sequence: observed.sequence, runId: observed.runId }, 'Available', parent)

// The version-1.0 reply table, restated independently of the module under test.
const ALLOWED: Record<KnownMessageType, KnownMessageType[]> = {
  Proposal: ['Proposal'],
  Acceptance: ['Proposal'],
  Challenge: ['Proposal'],
  Decision: ['Proposal', 'Challenge'],
  ExecutionReport: ['Decision', 'Proposal'],
  ReviewFinding: ['ExecutionReport'],
  ReviewApproval: ['ExecutionReport'],
  RevisionResponse: ['ReviewFinding'],
  Question: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding'],
  Escalation: ['Proposal', 'Challenge', 'Decision', 'ExecutionReport', 'ReviewFinding', 'RevisionResponse', 'Question'],
  HumanInstruction: ['Escalation'],
}
const TYPES = Object.keys(MESSAGE_SCHEMAS) as KnownMessageType[]

describe('toCollaborationReply', () => {
  it('accepts an Available parent for a coherent source and keeps the parent entry', () => {
    const observed = source()

    const reply = toCollaborationReply(available(observed), observed)

    expect(reply).toMatchObject({ status: 'Available', parent: { id: 'message-10', sequence: '10', type: 'Proposal' } })
    expect(reply?.status === 'Available' && reply.parent.content.status).toBe('Available')
  })

  it.each(TYPES.flatMap((type) => TYPES.map((parent) => [type, parent] as const)))(
    'for a %s source and a %s parent follows the version-1.0 reply table',
    (type, parentType) => {
      const observed = source({ type })

      const reply = toCollaborationReply(available(observed, parentOf(parentType)), observed)

      expect(reply !== null).toBe(ALLOWED[type].includes(parentType))
    },
  )

  it('accepts None only for a recognized version-1.0 Proposal with no parent', () => {
    const proposal = source({ type: 'Proposal' })

    expect(toCollaborationReply(replyFor({ id: proposal.messageId, sequence: '20' }, 'None'), proposal)).toEqual({ status: 'None' })
    for (const other of TYPES.filter((type) => type !== 'Proposal')) {
      const observed = source({ type: other })
      expect(toCollaborationReply(replyFor({ id: observed.messageId, sequence: '20' }, 'None'), observed)).toBeNull()
    }

    for (const unknown of [source({ type: 'Unrecognized' }), source({ type: 'Proposal', protocolVersion: 'Unrecognized' })]) {
      expect(toCollaborationReply(replyFor({ id: unknown.messageId, sequence: '20' }, 'None'), unknown)).toBeNull()
    }

    expect(toCollaborationReply(replyFor({ id: proposal.messageId, sequence: '20' }, 'None', parentOf('Proposal')), proposal)).toBeNull()
  })

  it('accepts Unavailable without a parent for every source, recognized or not, and refuses a parent beside it', () => {
    for (const observed of [source(), source({ type: 'Unrecognized' }), source({ protocolVersion: 'Unrecognized' })]) {
      expect(toCollaborationReply(replyFor({ id: observed.messageId, sequence: '20' }, 'Unavailable'), observed)).toEqual({
        status: 'Unavailable',
      })
      expect(toCollaborationReply(replyFor({ id: observed.messageId, sequence: '20' }, 'Unavailable', parentOf('Proposal')), observed)).toBeNull()
    }
  })

  it('refuses an Available answer without a parent or for an unrecognized source', () => {
    const observed = source()

    expect(toCollaborationReply(replyFor({ id: observed.messageId, sequence: '20' }, 'Available'), observed)).toBeNull()
    for (const unknown of [source({ type: 'Unrecognized' }), source({ protocolVersion: 'Unrecognized' })]) {
      expect(toCollaborationReply(available(unknown), unknown)).toBeNull()
    }
  })

  it.each([
    ['another run', { runId: 'run-2' }],
    ['another message', { messageId: 'message-21' }],
    ['another sequence', { sourceSequence: '21' }],
    ['a padded sequence', { sourceSequence: '020' }],
    ['a fractional sequence', { sourceSequence: '20.0' }],
    ['an empty sequence', { sourceSequence: '' }],
    ['no sequence', { sourceSequence: undefined }],
    ['no status', { status: undefined }],
    ['an unknown status', { status: 'Pending' }],
    ['a lowercase status', { status: 'available' }],
    ['a foreign status', { status: 'Unrecognized' }],
  ])('refuses an answer that names %s', (_name, patch) => {
    const observed = source()
    const response = available(observed)
    Object.assign(response, patch)

    expect(toCollaborationReply(response, observed)).toBeNull()
  })

  it('refuses a missing response and a request whose own observed sequence is not canonical', () => {
    expect(toCollaborationReply(null, source())).toBeNull()
    expect(toCollaborationReply(undefined, source())).toBeNull()
    for (const sequence of ['0', '-1', '007', '9223372036854775808', '1e3', '']) {
      const observed = source({ sequence })
      expect(toCollaborationReply(available(observed), observed)).toBeNull()
    }
  })

  it('refuses a parent that is the source itself, from another run or not strictly earlier', () => {
    const observed = source()

    expect(toCollaborationReply(available(observed, parentOf('Proposal', 10, { id: observed.messageId })), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 10, { runId: 'run-2' })), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 20)), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 21)), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 19)), observed)).not.toBeNull()
  })

  it('orders sequences as exact integers beyond a floating-point number', () => {
    const near = source({ sequence: '9007199254740993', messageId: 'message-near' })
    expect(toCollaborationReply(available(near, parentOf('Proposal', '9007199254740992')), near)).not.toBeNull()
    expect(toCollaborationReply(available(near, parentOf('Proposal', '9007199254740993')), near)).toBeNull()
    expect(toCollaborationReply(available(near, parentOf('Proposal', '9007199254740994')), near)).toBeNull()

    const lower = source({ sequence: '9007199254740992', messageId: 'message-lower' })
    expect(toCollaborationReply(available(lower, parentOf('Proposal', '9007199254740993')), lower)).toBeNull()

    const max = source({ sequence: '9223372036854775807', messageId: 'message-max' })
    expect(toCollaborationReply(available(max, parentOf('Proposal', '9223372036854775806')), max)).not.toBeNull()
    expect(toCollaborationReply(available(max, parentOf('Proposal', '9223372036854775807')), max)).toBeNull()
  })

  it('requires a recognized version-1.0 parent type', () => {
    const observed = source()

    expect(toCollaborationReply(available(observed, parentOf('Proposal', 10, { type: 'Unrecognized' })), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 10, { protocolVersion: 'Unrecognized' })), observed)).toBeNull()
    expect(toCollaborationReply(available(observed, parentOf('Proposal', 10, { type: 'Overlord' })), observed)).toBeNull()
  })

  it('keeps the relationship distinct from the content: a header-only parent is Available', () => {
    const observed = source()

    const reply = toCollaborationReply(available(observed, unavailableEntryFor(10, { type: 'Proposal' })), observed)

    expect(reply).toMatchObject({ status: 'Available', parent: { id: 'message-10', content: { status: 'Unavailable' } } })
  })

  it('applies every recorded-message rule to the parent', () => {
    const observed = source()
    const broken = [
      parentOf('Proposal', 10, { fields: fieldsFor('Proposal').slice(1) }),
      parentOf('Proposal', 10, { summary: '' }),
      parentOf('Proposal', 10, { summary: 'x'.repeat(601) }),
      parentOf('Proposal', 10, { sequence: '010' }),
      parentOf('Proposal', 10, { occurredAtUtc: new Date(Number.NaN) }),
      unavailableEntryFor(10, { type: 'Proposal', summary: 'leaked text' }),
      parentOf('Proposal', 10, { provenance: 'Forged' }),
      parentOf('Proposal', 10, { contentStatus: 'Maybe' }),
    ]

    for (const parent of broken) {
      expect(toCollaborationReply(available(observed, parent), observed)).toBeNull()
    }
  })
})
