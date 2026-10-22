import { describe, expect, it } from 'vitest'
import { RunEventHistoryEntryResponse } from '../../api/generated/api-client'
import {
  actorLabel,
  eventTypeLabel,
  KNOWN_EVENT_TYPES,
  toRunEventHistoryPage,
} from './runEventHistory'
import type { RunEventHistoryRequest } from './runEventHistory'
import { actorFor, descendingSequences, EVENT_RUN, eventPageOf, headerFor } from './runEventHistoryFixture'

const first: RunEventHistoryRequest = { runId: EVENT_RUN, beforeSequence: null, limit: 5 }
const sequences = (page: ReturnType<typeof toRunEventHistoryPage>) => page?.entries.map((entry) => entry.sequence)

describe('toRunEventHistoryPage', () => {
  it('accepts a coherent first page and keeps exact values, order and instants', () => {
    const page = toRunEventHistoryPage(eventPageOf([40, 12, 9], { hasMore: false }), first)

    expect(sequences(page)).toEqual(['40', '12', '9'])
    expect(page).toMatchObject({ hasMore: false, nextBeforeSequence: null })
    expect(page?.entries[0]).toMatchObject({ runId: EVENT_RUN, id: 'event-40', eventType: 'collaboration.message_recorded' })
    expect(page?.entries[0].occurredAtUtc).toBeInstanceOf(Date)
    expect(page?.entries[0].actor).toEqual({ kind: 'Orchestrator', role: null, provider: null })
  })

  it('accepts an empty ended page and a full page with a continuation', () => {
    expect(toRunEventHistoryPage(eventPageOf([]), first)).toEqual({ entries: [], hasMore: false, nextBeforeSequence: null })

    const page = toRunEventHistoryPage(eventPageOf(descendingSequences(9, 5), { hasMore: true }), first)
    expect(page?.hasMore).toBe(true)
    expect(page?.nextBeforeSequence).toBe('5')
  })

  it('accepts sequence gaps and treats an absent continuation field like a null one', () => {
    const response = eventPageOf([1000, 20, 3])
    response.nextBeforeSequence = null as unknown as undefined

    expect(sequences(toRunEventHistoryPage(response, first))).toEqual(['1000', '20', '3'])
  })

  it('compares sequences exactly as Int64 text, never through Number', () => {
    // 2^53 + 1 and 2^53 are the same Number; as exact text they are distinct and strictly descending.
    const around = toRunEventHistoryPage(eventPageOf(['9223372036854775807', '9007199254740993', '9007199254740992', '9007199254740991']), first)
    expect(sequences(around)).toEqual(['9223372036854775807', '9007199254740993', '9007199254740992', '9007199254740991'])

    const request = { ...first, beforeSequence: '9007199254740993' }
    expect(sequences(toRunEventHistoryPage(eventPageOf(['9007199254740992']), request))).toEqual(['9007199254740992'])
    expect(toRunEventHistoryPage(eventPageOf(['9007199254740993']), request)).toBeNull()
    expect(toRunEventHistoryPage(eventPageOf(['9007199254740994']), request)).toBeNull()
    const ended = toRunEventHistoryPage(eventPageOf(['9223372036854775806'], { hasMore: true, next: '9223372036854775806' }), {
      ...first,
      beforeSequence: '9223372036854775807',
      limit: 1,
    })
    expect(ended?.nextBeforeSequence).toBe('9223372036854775806')
  })

  it.each([
    ['a missing response', null],
    ['an undefined response', undefined],
    ['another run', eventPageOf([3, 2], { runId: 'run-2' })],
    ['a non-boolean hasMore', Object.assign(eventPageOf([3]), { hasMore: 'false' })],
    ['entries that are not a list', Object.assign(eventPageOf([3]), { entries: 'x' })],
    ['more entries than requested', eventPageOf(descendingSequences(6, 1))],
  ])('refuses %s', (_name, response) => {
    expect(toRunEventHistoryPage(response as never, first)).toBeNull()
  })

  it.each([
    ['an entry of another run', headerFor(3, { runId: 'run-2' })],
    ['a blank identity', headerFor(3, { id: '  ' })],
    ['a missing identity', headerFor(3, { id: undefined })],
    ['a numeric sequence', headerFor(3, { sequence: 3 as unknown as string })],
    ['an empty sequence', headerFor(3, { sequence: '' })],
    ['a zero sequence', headerFor(3, { sequence: '0' })],
    ['a leading-zero sequence', headerFor(3, { sequence: '03' })],
    ['a signed sequence', headerFor(3, { sequence: '+3' })],
    ['a negative sequence', headerFor(3, { sequence: '-3' })],
    ['a fractional sequence', headerFor(3, { sequence: '3.0' })],
    ['an exponent sequence', headerFor(3, { sequence: '3e0' })],
    ['a padded sequence', headerFor(3, { sequence: ' 3' })],
    ['a non-ASCII digit sequence', headerFor(3, { sequence: '٣' })],
    ['an Int64 overflow', headerFor(3, { sequence: '9223372036854775808' })],
    ['a huge sequence', headerFor(3, { sequence: '99999999999999999999999' })],
    ['an invalid instant', headerFor(3, { occurredAtUtc: new Date(Number.NaN) })],
    ['a missing instant', headerFor(3, { occurredAtUtc: undefined })],
    ['a string instant', headerFor(3, { occurredAtUtc: '2026-10-09T10:00:00Z' as unknown as Date })],
    ['an unknown event type', headerFor(3, { eventType: 'run.exploded' })],
    ['a differently cased event type', headerFor(3, { eventType: 'RUN.STARTED' })],
    ['a padded event type', headerFor(3, { eventType: 'run.started ' })],
    ['a missing event type', headerFor(3, { eventType: undefined })],
    ['a missing actor', headerFor(3, { actor: undefined })],
    ['an unknown actor kind', headerFor(3, { actor: actorFor({ kind: 'Robot' }) })],
    ['a lower-case actor kind', headerFor(3, { actor: actorFor({ kind: 'human' }) })],
    ['an unknown role', headerFor(3, { actor: actorFor({ kind: 'Agent', role: 'Janitor', provider: 'Codex' }) })],
    ['an unknown provider', headerFor(3, { actor: actorFor({ kind: 'Agent', role: 'Planner', provider: 'Gemini' }) })],
    ['an Agent without a provider', headerFor(3, { actor: actorFor({ kind: 'Agent', role: 'Planner' }) })],
    ['a Human with a role', headerFor(3, { actor: actorFor({ kind: 'Human', role: 'Planner' }) })],
    ['an Orchestrator with a provider', headerFor(3, { actor: actorFor({ kind: 'Orchestrator', provider: 'Codex' }) })],
    ['a None actor with a role', headerFor(3, { actor: actorFor({ kind: 'None', role: 'Planner' }) })],
    ['an unrecognized actor with a role', headerFor(3, { actor: actorFor({ kind: 'Unrecognized', role: 'Planner' }) })],
    ['an unrecognized actor with a provider', headerFor(3, { actor: actorFor({ kind: 'Unrecognized', provider: 'Codex' }) })],
  ])('refuses the whole page when it carries %s', (_name, bad) => {
    const response = eventPageOf([], { entries: [headerFor(9), bad as RunEventHistoryEntryResponse, headerFor(1)] })

    expect(toRunEventHistoryPage(response, first)).toBeNull()
  })

  it('refuses a non-object entry', () => {
    expect(toRunEventHistoryPage(eventPageOf([], { entries: [headerFor(9), null as never] }), first)).toBeNull()
    expect(toRunEventHistoryPage(eventPageOf([], { entries: ['x' as never] }), first)).toBeNull()
  })

  it.each([
    ['equal sequences', ['5', '5']],
    ['ascending sequences', ['4', '5']],
    ['a repeated sequence out of order', ['6', '4', '5']],
    ['non-descending large sequences', ['9007199254740992', '9007199254740993']],
  ])('refuses %s', (_name, order) => {
    const entries = order.map((sequence, index) => headerFor(sequence, { id: `event-${index}` }))

    expect(toRunEventHistoryPage(eventPageOf([], { entries }), first)).toBeNull()
  })

  it('refuses duplicate identities within a page and against the retained rows', () => {
    const duplicate = [headerFor(9, { id: 'same' }), headerFor(8, { id: 'same' })]
    expect(toRunEventHistoryPage(eventPageOf([], { entries: duplicate }), first)).toBeNull()

    const retained = toRunEventHistoryPage(eventPageOf(descendingSequences(9, 5), { hasMore: true }), first)!.entries
    const request = { ...first, beforeSequence: '5' }
    expect(toRunEventHistoryPage(eventPageOf([], { entries: [headerFor(4, { id: 'event-9' })] }), request, retained)).toBeNull()
    expect(sequences(toRunEventHistoryPage(eventPageOf([4, 3]), request, retained))).toEqual(['4', '3'])
  })

  it('refuses an entry that is not older than the request cursor or than the last retained row', () => {
    const request = { ...first, beforeSequence: '5' }
    expect(toRunEventHistoryPage(eventPageOf([5]), request)).toBeNull()
    expect(toRunEventHistoryPage(eventPageOf([6, 4]), request)).toBeNull()
    expect(sequences(toRunEventHistoryPage(eventPageOf([4]), request))).toEqual(['4'])

    const retained = toRunEventHistoryPage(eventPageOf([40, 30]), first)!.entries
    expect(toRunEventHistoryPage(eventPageOf([30]), first, retained)).toBeNull()
    expect(toRunEventHistoryPage(eventPageOf([35]), first, retained)).toBeNull()
    expect(sequences(toRunEventHistoryPage(eventPageOf([29]), first, retained))).toEqual(['29'])
  })

  it.each([
    ['a short page that claims more', eventPageOf([9, 8], { hasMore: true })],
    ['a full page whose cursor is not its last sequence', eventPageOf(descendingSequences(9, 5), { hasMore: true, next: '6' })],
    ['a full page without a cursor', eventPageOf(descendingSequences(9, 5), { hasMore: true, next: undefined })],
    ['a full page with a non-canonical cursor', eventPageOf(descendingSequences(9, 5), { hasMore: true, next: '05' })],
    ['a full page with a numeric cursor', eventPageOf(descendingSequences(9, 5), { hasMore: true, next: 5 as unknown as string })],
    ['an ended page that still names a cursor', eventPageOf([9, 8], { hasMore: false, next: '8' })],
  ])('refuses a broken continuation: %s', (_name, response) => {
    expect(toRunEventHistoryPage(response, first)).toBeNull()
  })

  it('discloses a fixed Unrecognized event type, actor and valid Agent shapes without inventing any role', () => {
    const page = toRunEventHistoryPage(
      eventPageOf([], {
        entries: [
          headerFor(9, { eventType: 'Unrecognized', actor: actorFor({ kind: 'Unrecognized' }) }),
          headerFor(8, { actor: actorFor({ kind: 'Agent', role: undefined, provider: 'ClaudeCode' }) }),
          headerFor(7, { actor: actorFor({ kind: 'Agent', role: 'CodeReviewer', provider: 'Codex' }) }),
          headerFor(6, { actor: actorFor({ kind: 'Human' }) }),
          headerFor(5, { actor: actorFor({ kind: 'None' }) }),
        ],
      }),
      first,
    )

    expect(page?.entries.map((entry) => [entry.eventType, entry.actor])).toEqual([
      ['Unrecognized', { kind: 'Unrecognized', role: null, provider: null }],
      ['collaboration.message_recorded', { kind: 'Agent', role: null, provider: 'ClaudeCode' }],
      ['collaboration.message_recorded', { kind: 'Agent', role: 'CodeReviewer', provider: 'Codex' }],
      ['collaboration.message_recorded', { kind: 'Human', role: null, provider: null }],
      ['collaboration.message_recorded', { kind: 'None', role: null, provider: null }],
    ])
  })

  it('does not mutate or alias the response it validates', () => {
    const response = eventPageOf([9, 8])
    const page = toRunEventHistoryPage(response, first)

    expect(page?.entries).not.toBe(response.entries)
    expect(response.entries?.[0].sequence).toBe('9')
  })
})

describe('event type and actor labels', () => {
  // Pinned on purpose: the host admits every RunEventType constant, and this closed label set must learn a new one deliberately.
  const BACKEND_EVENT_TYPES = [
    'run.started', 'codex.proposal', 'claude.challenge', 'codex.resolution', 'claude.execution', 'run.completed',
    'collaboration.message_recorded', 'process.output_captured', 'process.ended_without_output', 'agent.attempt_dispatched',
    'agent.attempt_completed', 'agent.output_recovered', 'run.codex_assignment_preference_changed',
    'run.claude_model_preference_changed', 'run.claude_mutation_turn_limit_changed', 'run.token_warning_threshold_changed',
    'run.token_stop_threshold_changed', 'run.codex_account_usage_stop_changed', 'run.codex_account_usage_warning_changed',
    'local_commit.admitted', 'local_commit.executing', 'local_commit.completed', 'local_commit.failed', 'local_commit.interrupted',
    'local_commit.needs_attention', 'run.abandoned', 'run.github_observation_target_recorded',
    'run.github_repository_identity_bound', 'run.github_branch_publication_approved', 'planning_sequence.armed',
    'planning_sequence.step_reserved', 'planning_sequence.step_claimed', 'planning_sequence.step_concluded',
    'planning_sequence.stop_requested', 'planning_sequence.ended', 'local_execution_sequence.armed',
    'local_execution_sequence.step_reserved', 'local_execution_sequence.step_claimed', 'local_execution_sequence.step_concluded',
    'local_execution_sequence.stop_requested', 'local_execution_sequence.ended', 'local_execution_sequence.admission_queued',
    'local_execution_sequence.admission_waiting', 'agent_supervision.registered', 'agent_supervision.phase_entered',
    'agent_supervision.attention', 'agent_supervision.closed',
  ]

  it('names exactly the event types the host admits, each with its own fixed label', () => {
    expect([...KNOWN_EVENT_TYPES].sort()).toEqual([...BACKEND_EVENT_TYPES].sort())
    const labels = BACKEND_EVENT_TYPES.map((type) => eventTypeLabel(type))
    expect(new Set(labels).size).toBe(labels.length)
    expect(labels.every((label) => label.length > 0 && !label.includes('.') && !label.includes('_'))).toBe(true)
    expect(eventTypeLabel('Unrecognized')).toBe('Unrecognized event type')
  })

  it('words an actor only from what was recorded', () => {
    expect(actorLabel({ kind: 'Orchestrator', role: null, provider: null })).toBe('Orchestrator')
    expect(actorLabel({ kind: 'Human', role: null, provider: null })).toBe('Human')
    expect(actorLabel({ kind: 'None', role: null, provider: null })).toBe('No actor recorded')
    expect(actorLabel({ kind: 'Unrecognized', role: null, provider: null })).toBe('Unrecognized actor')
    expect(actorLabel({ kind: 'Agent', role: 'Planner', provider: 'Codex' })).toBe('Agent · Planner role · Codex')
    expect(actorLabel({ kind: 'Agent', role: null, provider: 'ClaudeCode' })).toBe('Agent · role not recorded · Claude Code')
  })
})
