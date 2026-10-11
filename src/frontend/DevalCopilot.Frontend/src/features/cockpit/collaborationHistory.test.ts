import { describe, expect, it } from 'vitest'
import { CollaborationHistoryEntryResponse, CollaborationHistoryFieldResponse } from '../../api/generated/api-client'
import {
  MAX_FIELD_VALUE_LENGTH,
  MAX_SUMMARY_LENGTH,
  MESSAGE_SCHEMAS,
  messageTypeLabel,
  participantLabel,
  protocolVersionLabel,
  provenanceLabel,
  toCollaborationHistoryPage,
} from './collaborationHistory'
import type { CollaborationHistoryRequest, KnownMessageType } from './collaborationHistory'
import {
  agentFor,
  collaborationPageOf,
  descendingSequences,
  entryFor,
  fieldsFor,
  MESSAGE_RUN,
  participantFor,
  unavailableEntryFor,
} from './collaborationHistoryFixture'

const first: CollaborationHistoryRequest = { runId: MESSAGE_RUN, beforeSequence: null, limit: 5 }
const sequences = (page: ReturnType<typeof toCollaborationHistoryPage>) => page?.entries.map((entry) => entry.sequence)
const pageOfEntries = (...entries: CollaborationHistoryEntryResponse[]) => collaborationPageOf([], { entries })
const TYPES = Object.keys(MESSAGE_SCHEMAS) as KnownMessageType[]

describe('toCollaborationHistoryPage', () => {
  it('accepts a coherent first page and keeps exact values, order, instants and the schema order of the fields', () => {
    const reversed = [...MESSAGE_SCHEMAS.Proposal].reverse().map(([name]) => name)
    const page = toCollaborationHistoryPage(
      pageOfEntries(entryFor(40, { fields: fieldsFor('Proposal', reversed) }), entryFor(12), entryFor(9)),
      first,
    )

    expect(sequences(page)).toEqual(['40', '12', '9'])
    expect(page).toMatchObject({ hasMore: false, nextBeforeSequence: null })
    const entry = page!.entries[0]
    expect(entry).toMatchObject({ runId: MESSAGE_RUN, id: 'message-40', type: 'Proposal', protocolVersion: '1.0' })
    expect(entry.occurredAtUtc).toBeInstanceOf(Date)
    expect(entry.actor).toEqual({ kind: 'Agent', role: 'Planner', provider: 'Codex' })
    expect(entry.recipient).toEqual({ kind: 'Orchestrator', role: null, provider: null })
    expect(entry.content).toEqual({
      status: 'Available',
      summary: 'Summary 40',
      fields: MESSAGE_SCHEMAS.Proposal.map(([name, label]) => ({ name, label, value: `${name} text` })),
    })
  })

  it.each(TYPES)('accepts the complete closed schema of %s and only that schema', (type) => {
    const entry = entryFor(7, { type, fields: fieldsFor(type) })
    expect(toCollaborationHistoryPage(pageOfEntries(entry), first)?.entries[0].content.status).toBe('Available')

    const names = MESSAGE_SCHEMAS[type].map(([name]) => name)
    const missing = entryFor(7, { type, fields: fieldsFor(type, names.slice(1)) })
    const extra = entryFor(7, { type, fields: [...fieldsFor(type), new CollaborationHistoryFieldResponse({ name: 'unknownField', value: 'x' })] })
    const swapped = entryFor(7, { type, fields: fieldsFor(type, [...names.slice(1), 'unknownField']) })
    for (const bad of [missing, extra, swapped]) {
      expect(toCollaborationHistoryPage(pageOfEntries(bad), first)).toBeNull()
    }
  })

  it('accepts an empty ended page, a full page with a continuation, gaps and an absent continuation field', () => {
    expect(toCollaborationHistoryPage(collaborationPageOf([]), first)).toEqual({ entries: [], hasMore: false, nextBeforeSequence: null })

    const full = toCollaborationHistoryPage(collaborationPageOf(descendingSequences(9, 5), { hasMore: true }), first)
    expect(full?.hasMore).toBe(true)
    expect(full?.nextBeforeSequence).toBe('5')

    const response = collaborationPageOf([1000, 20, 3])
    response.nextBeforeSequence = null as unknown as undefined
    expect(sequences(toCollaborationHistoryPage(response, first))).toEqual(['1000', '20', '3'])
  })

  it('compares sequences exactly as Int64 text, never through Number', () => {
    const around = toCollaborationHistoryPage(
      collaborationPageOf(['9223372036854775807', '9007199254740993', '9007199254740992', '9007199254740991']),
      first,
    )
    expect(sequences(around)).toEqual(['9223372036854775807', '9007199254740993', '9007199254740992', '9007199254740991'])

    const request = { ...first, beforeSequence: '9007199254740993' }
    expect(sequences(toCollaborationHistoryPage(collaborationPageOf(['9007199254740992']), request))).toEqual(['9007199254740992'])
    expect(toCollaborationHistoryPage(collaborationPageOf(['9007199254740993']), request)).toBeNull()
    expect(toCollaborationHistoryPage(collaborationPageOf(['9007199254740994']), request)).toBeNull()
  })

  it.each([
    ['a missing response', null],
    ['an undefined response', undefined],
    ['another run', collaborationPageOf([3, 2], { runId: 'run-2' })],
    ['a non-boolean hasMore', Object.assign(collaborationPageOf([3]), { hasMore: 'false' })],
    ['entries that are not a list', Object.assign(collaborationPageOf([3]), { entries: 'x' })],
    ['more entries than requested', collaborationPageOf(descendingSequences(6, 1))],
  ])('refuses %s', (_name, response) => {
    expect(toCollaborationHistoryPage(response as never, first)).toBeNull()
  })

  it.each([
    ['an entry of another run', entryFor(3, { runId: 'run-2' })],
    ['a blank identity', entryFor(3, { id: '  ' })],
    ['a missing identity', entryFor(3, { id: undefined })],
    ['a numeric sequence', entryFor(3, { sequence: 3 as unknown as string })],
    ['an empty sequence', entryFor(3, { sequence: '' })],
    ['a zero sequence', entryFor(3, { sequence: '0' })],
    ['a leading-zero sequence', entryFor(3, { sequence: '03' })],
    ['a signed sequence', entryFor(3, { sequence: '+3' })],
    ['a fractional sequence', entryFor(3, { sequence: '3.0' })],
    ['a padded sequence', entryFor(3, { sequence: ' 3' })],
    ['a non-ASCII digit sequence', entryFor(3, { sequence: '٣' })],
    ['an Int64 overflow', entryFor(3, { sequence: '9223372036854775808' })],
    ['an invalid instant', entryFor(3, { occurredAtUtc: new Date(Number.NaN) })],
    ['a string instant', entryFor(3, { occurredAtUtc: '2026-10-09T10:00:00Z' as unknown as Date })],
    ['an unlisted type', entryFor(3, { type: 'Overlord' })],
    ['a differently cased type', entryFor(3, { type: 'proposal' })],
    ['an inherited property name as a type', entryFor(3, { type: 'constructor' })],
    ['a missing type', entryFor(3, { type: undefined })],
    ['an unlisted provenance', entryFor(3, { provenance: 'Forged' })],
    ['an inherited property name as provenance', entryFor(3, { provenance: 'toString' })],
    ['an unlisted version', entryFor(3, { protocolVersion: '2.0' })],
    ['a padded version', entryFor(3, { protocolVersion: '1.0 ' })],
    ['a missing actor', entryFor(3, { actor: undefined })],
    ['a missing recipient', entryFor(3, { recipient: undefined })],
    ['a None participant', entryFor(3, { recipient: participantFor({ kind: 'None' }) })],
    ['an unknown participant kind', entryFor(3, { actor: participantFor({ kind: 'Robot' }) })],
    ['an unknown role', entryFor(3, { actor: participantFor({ kind: 'Agent', role: 'Janitor', provider: 'Codex' }) })],
    ['an unknown provider', entryFor(3, { actor: participantFor({ kind: 'Agent', role: 'Planner', provider: 'Gemini' }) })],
    ['an Agent without a provider', entryFor(3, { recipient: participantFor({ kind: 'Agent', role: 'Planner' }) })],
    ['a Human with a role', entryFor(3, { actor: participantFor({ kind: 'Human', role: 'Planner' }) })],
    ['an Orchestrator with a provider', entryFor(3, { recipient: participantFor({ kind: 'Orchestrator', provider: 'Codex' }) })],
    ['an unrecognized participant with a provider', entryFor(3, { actor: participantFor({ kind: 'Unrecognized', provider: 'Codex' }) })],
    ['an unknown content status', entryFor(3, { contentStatus: 'Partial' })],
    ['a missing content status', entryFor(3, { contentStatus: undefined })],
  ])('refuses the whole page when it carries %s', (_name, bad) => {
    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(9), bad as CollaborationHistoryEntryResponse, entryFor(1)), first)).toBeNull()
  })

  it.each([
    ['a missing summary', entryFor(3, { summary: undefined })],
    ['a null summary', entryFor(3, { summary: null as unknown as undefined })],
    ['a blank summary', entryFor(3, { summary: '   ' })],
    ['a numeric summary', entryFor(3, { summary: 3 as unknown as string })],
    ['an oversized summary', entryFor(3, { summary: 's'.repeat(MAX_SUMMARY_LENGTH + 1) })],
    ['a missing field list', entryFor(3, { fields: undefined })],
    ['a duplicate field', entryFor(3, { fields: [...fieldsFor('Proposal').slice(0, 4), fieldsFor('Proposal')[0]] })],
    ['a blank field value', entryFor(3, { fields: fieldsFor('Proposal').map((f, i) => (i === 0 ? new CollaborationHistoryFieldResponse({ name: f.name, value: ' ' }) : f)) })],
    ['a non-string field value', entryFor(3, { fields: fieldsFor('Proposal').map((f, i) => (i === 0 ? new CollaborationHistoryFieldResponse({ name: f.name, value: 3 as unknown as string }) : f)) })],
    ['an oversized field value', entryFor(3, { fields: fieldsFor('Proposal').map((f, i) => (i === 0 ? new CollaborationHistoryFieldResponse({ name: f.name, value: 'v'.repeat(MAX_FIELD_VALUE_LENGTH + 1) }) : f)) })],
    ['a field of another type', entryFor(3, { fields: fieldsFor('Acceptance') })],
    ['an inherited property name as a field', entryFor(3, { fields: [...fieldsFor('Proposal').slice(0, 4), new CollaborationHistoryFieldResponse({ name: 'constructor', value: 'x' })] })],
    ['available content with an unrecognized type', entryFor(3, { type: 'Unrecognized' })],
    ['available content with an unrecognized version', entryFor(3, { protocolVersion: 'Unrecognized' })],
    ['available content with an unrecognized provenance', entryFor(3, { provenance: 'Unrecognized' })],
    ['available content with an unrecognized participant', entryFor(3, { actor: participantFor({ kind: 'Unrecognized' }) })],
    ['unavailable content that still has a summary', unavailableEntryFor(3, { summary: 'leaked text' })],
    ['unavailable content that still has fields', unavailableEntryFor(3, { fields: fieldsFor('Proposal') })],
    ['unavailable content without a field list', unavailableEntryFor(3, { fields: undefined })],
  ])('refuses the whole page when it carries %s', (_name, bad) => {
    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(9), bad, entryFor(1)), first)).toBeNull()
  })

  it('keeps a well-formed unavailable entry beside healthy neighbors, without any text', () => {
    const page = toCollaborationHistoryPage(
      pageOfEntries(
        entryFor(9),
        unavailableEntryFor(8, { type: 'Unrecognized', provenance: 'Unrecognized', protocolVersion: 'Unrecognized', actor: participantFor({ kind: 'Unrecognized' }) }),
        unavailableEntryFor(7),
        entryFor(6),
      ),
      first,
    )

    expect(page?.entries.map((entry) => entry.content.status)).toEqual(['Available', 'Unavailable', 'Unavailable', 'Available'])
    expect(page?.entries[1]).toMatchObject({ type: 'Unrecognized', provenance: 'Unrecognized', protocolVersion: 'Unrecognized' })
    expect(page?.entries[1].content).toEqual({ status: 'Unavailable' })
    expect(page?.entries[1].actor).toEqual({ kind: 'Unrecognized', role: null, provider: null })
  })

  it('refuses a non-object entry, equal or ascending sequences and duplicate identities', () => {
    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(9), null as never), first)).toBeNull()
    for (const order of [['5', '5'], ['4', '5'], ['6', '4', '5'], ['9007199254740992', '9007199254740993']]) {
      const entries = order.map((sequence, index) => entryFor(sequence, { id: `message-${index}` }))
      expect(toCollaborationHistoryPage(pageOfEntries(...entries), first)).toBeNull()
    }

    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(9, { id: 'same' }), entryFor(8, { id: 'same' })), first)).toBeNull()
    const retained = toCollaborationHistoryPage(collaborationPageOf(descendingSequences(9, 5), { hasMore: true }), first)!.entries
    const request = { ...first, beforeSequence: '5' }
    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(4, { id: 'message-9' })), request, retained)).toBeNull()
    expect(sequences(toCollaborationHistoryPage(collaborationPageOf([4, 3]), request, retained))).toEqual(['4', '3'])
  })

  it('refuses an entry that is not older than the request cursor or than the last retained row', () => {
    const request = { ...first, beforeSequence: '5' }
    expect(toCollaborationHistoryPage(collaborationPageOf([5]), request)).toBeNull()
    expect(toCollaborationHistoryPage(collaborationPageOf([6, 4]), request)).toBeNull()
    expect(sequences(toCollaborationHistoryPage(collaborationPageOf([4]), request))).toEqual(['4'])

    const retained = toCollaborationHistoryPage(collaborationPageOf([40, 30]), first)!.entries
    expect(toCollaborationHistoryPage(collaborationPageOf([30]), first, retained)).toBeNull()
    expect(toCollaborationHistoryPage(collaborationPageOf([35]), first, retained)).toBeNull()
    expect(sequences(toCollaborationHistoryPage(collaborationPageOf([29]), first, retained))).toEqual(['29'])
  })

  it.each([
    ['a short page that claims more', collaborationPageOf([9, 8], { hasMore: true })],
    ['a full page whose cursor is not its last sequence', collaborationPageOf(descendingSequences(9, 5), { hasMore: true, next: '6' })],
    ['a full page without a cursor', collaborationPageOf(descendingSequences(9, 5), { hasMore: true, next: undefined })],
    ['a full page with a non-canonical cursor', collaborationPageOf(descendingSequences(9, 5), { hasMore: true, next: '05' })],
    ['an ended page that still names a cursor', collaborationPageOf([9, 8], { hasMore: false, next: '8' })],
  ])('refuses a broken continuation: %s', (_name, response) => {
    expect(toCollaborationHistoryPage(response, first)).toBeNull()
  })

  // The host admits Available content only for distinct participants that agree with the provenance; a page that says otherwise is
  // contradictory transport, never a partial page. Each contradiction sits between healthy neighbors.
  it.each([
    ['identical participants', entryFor(3, { recipient: agentFor() })],
    ['identical Orchestrator participants', entryFor(3, { provenance: 'HostConstructed', actor: participantFor(), recipient: participantFor() })],
    ['provider-observed from an Orchestrator', entryFor(3, { actor: participantFor(), recipient: agentFor() })],
    ['provider-observed from a Human', entryFor(3, { actor: participantFor({ kind: 'Human' }) })],
    ['provider-observed from an Agent without a recorded role', entryFor(3, { actor: participantFor({ kind: 'Agent', provider: 'Codex' }) })],
    ['host-constructed from an Agent', entryFor(3, { provenance: 'HostConstructed' })],
    ['host-constructed from a Human', entryFor(3, { provenance: 'HostConstructed', actor: participantFor({ kind: 'Human' }), recipient: participantFor() })],
    ['human-submitted from an Agent', entryFor(3, { provenance: 'HumanSubmitted' })],
    ['human-submitted from an Orchestrator', entryFor(3, { provenance: 'HumanSubmitted', actor: participantFor(), recipient: agentFor() })],
  ])('refuses the whole page when available content has %s', (_name, bad) => {
    expect(toCollaborationHistoryPage(pageOfEntries(entryFor(9), bad, entryFor(1)), first)).toBeNull()
  })

  it('accepts every provenance-consistent available shape, and keeps a damaged envelope as a header-only unavailable row', () => {
    const page = toCollaborationHistoryPage(
      pageOfEntries(
        entryFor(9, { provenance: 'Simulated', actor: participantFor({ kind: 'Agent', provider: 'ClaudeCode' }), recipient: agentFor() }),
        entryFor(8, { provenance: 'HostConstructed', actor: participantFor(), recipient: participantFor({ kind: 'Human' }) }),
        entryFor(7, {
          type: 'HumanInstruction',
          provenance: 'HumanSubmitted',
          actor: participantFor({ kind: 'Human' }),
          fields: fieldsFor('HumanInstruction'),
        }),
        unavailableEntryFor(6, { recipient: agentFor() }),
        unavailableEntryFor(5, { provenance: 'HostConstructed', actor: participantFor({ kind: 'Human' }), recipient: participantFor({ kind: 'Human' }) }),
        entryFor(4),
      ),
      { ...first, limit: 6 },
    )

    expect(page?.entries.map((entry) => entry.content.status)).toEqual([
      'Available', 'Available', 'Available', 'Unavailable', 'Unavailable', 'Available',
    ])
  })

  it('discloses valid participant shapes without inventing any role and does not alias the response', () => {
    const response = pageOfEntries(
      entryFor(9, {
        provenance: 'Simulated',
        actor: participantFor({ kind: 'Agent', role: undefined, provider: 'ClaudeCode' }),
        recipient: agentFor(),
      }),
      entryFor(8, {
        provenance: 'HumanSubmitted',
        actor: participantFor({ kind: 'Human' }),
        type: 'HumanInstruction',
        fields: fieldsFor('HumanInstruction'),
      }),
    )
    const page = toCollaborationHistoryPage(response, first)

    expect(page?.entries[0].actor).toEqual({ kind: 'Agent', role: null, provider: 'ClaudeCode' })
    expect(page?.entries[1].actor).toEqual({ kind: 'Human', role: null, provider: null })
    expect(page?.entries).not.toBe(response.entries)
    expect(response.entries?.[0].sequence).toBe('9')
  })
})

describe('message labels', () => {
  // Pinned on purpose: the host admits every CollaborationMessageType, and this closed label set must learn a new one deliberately.
  const BACKEND_TYPES = [
    'Proposal', 'Acceptance', 'Challenge', 'Question', 'Decision', 'ExecutionReport', 'ReviewFinding', 'RevisionResponse', 'Escalation',
    'ReviewApproval', 'HumanInstruction',
  ]

  it('names exactly the message types the host admits, each with its own fixed label and a label for every field', () => {
    expect([...TYPES].sort()).toEqual([...BACKEND_TYPES].sort())
    const labels = BACKEND_TYPES.map((type) => messageTypeLabel(type))
    expect(new Set(labels).size).toBe(labels.length)
    expect(messageTypeLabel('Unrecognized')).toBe('Unrecognized message type')
    expect(messageTypeLabel('constructor')).toBe('Unrecognized message type')
    for (const type of TYPES) {
      const fieldLabels = MESSAGE_SCHEMAS[type].map(([, label]) => label)
      expect(new Set(fieldLabels).size).toBe(fieldLabels.length)
    }
  })

  it('words provenance, version and participants only from what was recorded', () => {
    expect(provenanceLabel('ProviderObserved')).toBe('Provider-observed')
    expect(provenanceLabel('Unrecognized')).toBe('Unrecognized provenance')
    expect(provenanceLabel('toString')).toBe('Unrecognized provenance')
    expect(protocolVersionLabel('1.0')).toBe('Protocol 1.0')
    expect(protocolVersionLabel('Unrecognized')).toBe('Unrecognized protocol version')
    expect(participantLabel({ kind: 'Orchestrator', role: null, provider: null })).toBe('Orchestrator')
    expect(participantLabel({ kind: 'Unrecognized', role: null, provider: null })).toBe('Unrecognized participant')
    expect(participantLabel({ kind: 'Agent', role: 'Planner', provider: 'Codex' })).toBe('Agent · Planner role · Codex')
    expect(participantLabel({ kind: 'Agent', role: null, provider: 'ClaudeCode' })).toBe('Agent · role not recorded · Claude Code')
  })
})
