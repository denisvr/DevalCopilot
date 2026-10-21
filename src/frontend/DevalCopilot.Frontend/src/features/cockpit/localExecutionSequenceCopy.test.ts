import { describe, expect, it } from 'vitest'
import { ApiException, ApiProblemDetails } from '../../api/generated/api-client'
import { describeGlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import {
  LOCAL_EXECUTION_REASON_CODES,
  LOCAL_EXECUTION_SEQUENCE_STATES,
} from './localExecutionSequence'
import {
  LOCAL_EXECUTION_EVENT_SUMMARIES,
  LOCAL_EXECUTION_REASON_PHRASE,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_UNKNOWN_OUTCOME,
  classifyLocalExecutionFailure,
  deriveLocalExecutionSequenceClaimBlock,
  describeLocalExecutionEventState,
  describeLocalExecutionRefusalCode,
  describeLocalExecutionSequenceWithholding,
  localExecutionFailureMessage,
} from './localExecutionSequenceCopy'
import { parseEventSummary } from './parseEventSummary'

const problem = (status: number, code: string) => ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

describe('the local-execution fence copy', () => {
  it('withholds for unknown and blocked and nothing else', () => {
    expect(describeLocalExecutionSequenceWithholding('none')).toBeNull()
    expect(describeLocalExecutionSequenceWithholding('released')).toBeNull()
    expect(describeLocalExecutionSequenceWithholding('unknown')).toMatch(/Withheld until the local execution sequence status is confirmed/)
    expect(describeLocalExecutionSequenceWithholding('blocked')).toMatch(/Stop after current operation/)
  })

  it('derives the claim block as only ever a veto with its own two reasons and fixed messages', () => {
    expect(deriveLocalExecutionSequenceClaimBlock('none')).toBeNull()
    expect(deriveLocalExecutionSequenceClaimBlock('released')).toBeNull()
    const unknown = deriveLocalExecutionSequenceClaimBlock('unknown')
    const blocked = deriveLocalExecutionSequenceClaimBlock('blocked')
    expect(unknown).toEqual({ reason: 'LocalExecutionSequenceStatusUnknown' })
    expect(blocked).toEqual({ reason: 'LocalExecutionSequenceAutomaticAuthority' })
    expect(describeGlobalAgentClaimBlock(unknown!)).toBe(describeLocalExecutionSequenceWithholding('unknown'))
    expect(describeGlobalAgentClaimBlock(blocked!)).toBe(describeLocalExecutionSequenceWithholding('blocked'))
  })

  it('words every closed state and reason with fixed copy', () => {
    for (const state of LOCAL_EXECUTION_SEQUENCE_STATES) {
      expect(LOCAL_EXECUTION_STATE_LABEL[state].length).toBeGreaterThan(0)
      expect(describeLocalExecutionEventState(state).length).toBeGreaterThan(0)
    }
    for (const reason of LOCAL_EXECUTION_REASON_CODES) {
      expect(LOCAL_EXECUTION_REASON_PHRASE[reason].length).toBeGreaterThan(0)
    }
  })
})

describe('classifyLocalExecutionFailure', () => {
  it.each([
    ['a failure with no response', new Error('socket')],
    ['an aborted request', new DOMException('aborted', 'AbortError')],
    ['a server fault', problem(500, 'local_execution_sequence.persistence_failed')],
    ['an unreadable answer', new ApiException('x', 502, 'not json', {}, null)],
  ])('treats %s as an unknown outcome', (_name, caught) => {
    expect(classifyLocalExecutionFailure(caught)).toEqual({ kind: 'unknown' })
    expect(localExecutionFailureMessage(caught)).toBe(LOCAL_EXECUTION_UNKNOWN_OUTCOME)
  })

  it.each([
    'local_execution_sequence.already_armed',
    'local_execution_sequence.plan_not_eligible',
    'local_execution_sequence.tuple_stale',
    'local_execution_sequence.confirmation_stale',
    'local_execution_sequence.tuple_conflict',
    'local_execution_sequence.manual_control_blocked',
    'local_execution_sequence.planning_active',
    'local_execution_sequence.workspace_owned',
    'local_execution_sequence.capacity_unavailable',
    'local_execution_sequence.recipes_missing',
    'local_execution_sequence.recipes_too_many',
    'local_execution_sequence.recipes_incoherent',
    'local_execution_sequence.settings_unavailable',
    'local_execution_sequence.not_found',
    'local_execution_sequence.run_not_manual',
    'local_execution_sequence.run_not_running',
    'local_execution_sequence.prior_mutation',
    'local_execution_sequence.plan_not_found',
    'local_execution_sequence.project_busy',
    'local_execution_sequence.workspace_not_ready',
    'local_execution_sequence.lease_not_active',
    'local_execution_sequence.checkpoint_missing',
  ])('words the stable code %s with its fixed copy and never echoes the body', (code) => {
    const failure = classifyLocalExecutionFailure(problem(409, code))
    expect(failure).toMatchObject({ kind: 'refused', code })
    const message = failure.kind === 'refused' ? failure.message : ''
    expect(message.length).toBeGreaterThan(10)
    expect(message).not.toMatch(/server text/)
    expect(describeLocalExecutionRefusalCode(code)).toBe(message)
  })

  it.each([
    [400, 'The request was not accepted by the host.'],
    [404, 'This run or local execution sequence could not be found.'],
    [409, 'The local execution sequence changed or the request can no longer be accepted. The status is being read again.'],
    [418, 'The local execution sequence request could not be completed. The status is being read again.'],
  ])('words an unknown code under status %i from the status alone', (status, message) => {
    expect(classifyLocalExecutionFailure(problem(status, 'something.else'))).toMatchObject({ kind: 'refused', message })
  })

  it('words an unknown arming refusal generically', () => {
    expect(describeLocalExecutionRefusalCode('local_execution_sequence.unheard_of')).toBe('Arming is not available.')
    expect(describeLocalExecutionRefusalCode(null)).toBe('Arming is not available.')
  })
})

describe('the local-execution run events', () => {
  it.each(Object.entries(LOCAL_EXECUTION_EVENT_SUMMARIES))('words local_execution_sequence.%s from its fixed type only', (kind, summary) => {
    expect(parseEventSummary('{"summary":"payload text"}', `local_execution_sequence.${kind}`)).toBe(
      kind === 'ended' ? summary : summary,
    )
  })

  it('words the ended event with its closed state and reason, and as the plain sentence otherwise', () => {
    const plain = LOCAL_EXECUTION_EVENT_SUMMARIES.ended
    expect(parseEventSummary('{"state":"NeedsAttention","reason":"exit_evidence_missing","detail":null}', 'local_execution_sequence.ended')).toBe(
      'The local execution sequence ended: needs attention; a dispatched operation ended without recorded exit evidence (no supported recovery action in this version).',
    )
    expect(parseEventSummary('{"state":"ReadyForHumanApproval","reason":"review_approved"}', 'local_execution_sequence.ended')).toMatch(
      /awaiting Human approval \(not committed, not published\)/,
    )
    expect(parseEventSummary('{"state":"Exploded","reason":"review_approved"}', 'local_execution_sequence.ended')).toBe(plain)
    expect(parseEventSummary('{"state":"Stopped","reason":"<b>x</b>"}', 'local_execution_sequence.ended')).toBe(plain)
    expect(parseEventSummary('not json', 'local_execution_sequence.ended')).toBe(plain)
  })

  it('never echoes payload text of a claim or an unknown local-execution event type', () => {
    expect(parseEventSummary('{"operationId":"x"}', 'local_execution_sequence.step_claimed')).toMatch(/not proof that a process is running/)
    expect(parseEventSummary('{"summary":"payload"}', 'local_execution_sequence.unknown_kind')).toBe('payload')
  })
})
