import { describe, expect, it } from 'vitest'
import { ApiException } from '../../api/generated/api-client'
import {
  ABANDONED_NOTE,
  ABANDON_EXPLANATION,
  ABANDON_MAX_REASON_BYTES,
  ABANDON_UNKNOWN_OUTCOME_MESSAGE,
  abandonIdentityKey,
  classifyAbandonFailure,
  describeAbandonmentRefusal,
  validateAbandonReason,
} from './describeRunAbandonment'

const problem = (status: number, code?: string) =>
  new ApiException('raw server text that must never be shown', status, JSON.stringify({ errors: [{ code, detail: 'server detail' }] }), {}, null)

describe('validateAbandonReason', () => {
  it.each([
    ['plain text', 'Superseded.', 'Superseded.'],
    ['outer whitespace', '  Superseded by a smaller change \n', 'Superseded by a smaller change'],
    ['CRLF line endings', 'First\r\nSecond', 'First\nSecond'],
    ['blank lines inside', 'First\n\nSecond', 'First\n\nSecond'],
    ['non-ASCII text', 'Plan é漢😀', 'Plan é漢😀'],
  ])('accepts %s and sends the normalized text', (_name, draft, expected) => {
    expect(validateAbandonReason(draft)).toEqual({ valid: true, reason: expected })
  })

  it.each(['', '   ', '\r\n \n'])('refuses a blank reason (%j)', (draft) => {
    expect(validateAbandonReason(draft)).toEqual({ valid: false, problem: 'empty' })
  })

  it.each([
    ['a lone carriage return', 'a\rb'],
    ['a tab', 'a\tb'],
    ['a NUL', 'a\u0000b'],
    ['a bell', 'a\u0007b'],
    ['DEL', 'a\u007fb'],
    ['a C1 control', 'a' + String.fromCharCode(0x85) + 'b'],
    ['a line separator', 'a' + String.fromCharCode(0x2028) + 'b'],
    ['a paragraph separator', 'a' + String.fromCharCode(0x2029) + 'b'],
    ['a zero-width space', 'a' + String.fromCharCode(0x200b) + 'b'],
    ['a bidi override', 'a' + String.fromCharCode(0x202e) + 'b'],
    ['an unpaired high surrogate', 'a' + String.fromCharCode(0xd83d) + 'b'],
    ['an unpaired low surrogate', 'a' + String.fromCharCode(0xde00) + 'b'],
  ])('refuses %s', (_name, draft) => {
    expect(validateAbandonReason(draft)).toEqual({ valid: false, problem: 'control_character' })
  })

  it('counts UTF-8 bytes, not characters, against the 2048-byte bound', () => {
    expect(validateAbandonReason('a'.repeat(ABANDON_MAX_REASON_BYTES)).valid).toBe(true)
    expect(validateAbandonReason('a'.repeat(ABANDON_MAX_REASON_BYTES + 1))).toEqual({ valid: false, problem: 'too_long' })
    expect(validateAbandonReason('漢'.repeat(682)).valid).toBe(true)
    expect(validateAbandonReason('漢'.repeat(683))).toEqual({ valid: false, problem: 'too_long' })
  })

  it('accepts a correctly paired surrogate', () => {
    expect(validateAbandonReason('ok ' + String.fromCharCode(0xd83d, 0xde00)).valid).toBe(true)
  })

  // Supplementary-plane Format characters are valid surrogate pairs; the host classifies the decoded scalar and so must the client,
  // so the two never disagree about what is sent.
  it.each([0xe0001, 0xe0020, 0xe007f, 0x1d173, 0x110bd])('refuses the supplementary format character U+%s wherever it appears', (codePoint) => {
    const scalar = String.fromCodePoint(codePoint)
    for (const draft of ['before' + scalar + 'after', scalar + 'leading', 'trailing' + scalar, '😀' + scalar + '😀']) {
      expect(validateAbandonReason(draft)).toEqual({ valid: false, problem: 'control_character' })
    }
  })

  it.each([0x1f600, 0x10000, 0x1d11e, 0x20000, 0xe0100])('keeps ordinary supplementary text U+%s exactly', (codePoint) => {
    const reason = 'before ' + String.fromCodePoint(codePoint) + ' after'
    expect(validateAbandonReason(reason)).toEqual({ valid: true, reason })
  })

  it('counts a supplementary scalar as four UTF-8 bytes against the bound', () => {
    expect(validateAbandonReason('😀'.repeat(512)).valid).toBe(true)
    expect(validateAbandonReason('😀'.repeat(513))).toEqual({ valid: false, problem: 'too_long' })
  })
})

describe('classifyAbandonFailure', () => {
  it.each([
    ['no response at all', new TypeError('Failed to fetch')],
    ['a 500 response', problem(500)],
    ['a 503 response', problem(503, 'run_abandonment.concurrent_change')],
    ['a status below 100', problem(0)],
  ])('treats %s as an unknown outcome', (_name, caught) => {
    expect(classifyAbandonFailure(caught)).toEqual({ kind: 'unknown' })
  })

  it.each([
    ['run_abandonment.active_attempt', /attempt of this project is still active/],
    ['run_abandonment.local_commit_open', /never used to override an ambiguous local delivery/],
    ['run_abandonment.reason_conflict', /different reason/],
    ['run_abandonment.abandonment_incoherent', /not coherent/],
    ['run_abandonment.concurrent_change', /Read its status again/],
  ])('describes the definite refusal %s with fixed copy and never the server text', (code, copy) => {
    const failure = classifyAbandonFailure(problem(409, code))

    expect(failure.kind).toBe('refused')
    expect((failure as { message: string }).message).toMatch(copy)
    expect((failure as { message: string }).message).not.toContain('server')
  })

  it('falls back to fixed copy by status when no known code is present', () => {
    expect(classifyAbandonFailure(problem(400))).toMatchObject({ kind: 'refused', message: expect.stringContaining('2048 bytes') })
    expect(classifyAbandonFailure(problem(404))).toMatchObject({ kind: 'refused', message: 'This run could not be found.' })
    expect(classifyAbandonFailure(problem(409, 'something.else'))).toMatchObject({ kind: 'refused', message: expect.stringContaining('Reload') })
    expect(classifyAbandonFailure(problem(418))).toMatchObject({ kind: 'refused', message: 'The run could not be abandoned.' })
  })
})

describe('describeAbandonmentRefusal', () => {
  it('maps every host refusal code and its bare form, and never echoes an unknown code', () => {
    for (const code of [
      'run_not_manual',
      'run_not_abandonable',
      'active_attempt',
      'active_verification',
      'local_commit_open',
      'workspace_busy',
      'already_abandoned',
      'reason_conflict',
      'abandonment_incoherent',
      'concurrent_change',
    ]) {
      const prefixed = describeAbandonmentRefusal(`run_abandonment.${code}`)
      expect(prefixed).not.toBe('This run cannot be abandoned right now.')
      expect(describeAbandonmentRefusal(code)).toBe(prefixed)
    }

    expect(describeAbandonmentRefusal('run_abandonment.invented')).toBe('This run cannot be abandoned right now.')
    expect(describeAbandonmentRefusal(null)).toBe('This run cannot be abandoned right now.')
    expect(describeAbandonmentRefusal('totally-unknown <b>')).not.toContain('totally-unknown')
  })
})

describe('fixed copy', () => {
  it('states that abandonment is not a completion, that history stays, and that nothing is cancelled or deleted', () => {
    const text = [...ABANDON_EXPLANATION, ABANDONED_NOTE].join(' ')

    expect(text).toMatch(/not a successful completion/)
    expect(text).toMatch(/kept exactly as they are/)
    expect(text).toMatch(/Nothing is cancelled, repaired, released or deleted/)
    expect(text).toMatch(/normal checks/)
    expect(ABANDON_UNKNOWN_OUTCOME_MESSAGE).toMatch(/recorded status is shown/)
  })

  it('derives the eligible-form identity from the run alone', () => {
    expect(abandonIdentityKey('run-a')).toBe(abandonIdentityKey('run-a'))
    expect(abandonIdentityKey('run-a')).not.toBe(abandonIdentityKey('run-b'))
  })
})
