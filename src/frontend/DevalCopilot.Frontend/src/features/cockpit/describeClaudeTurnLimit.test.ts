import { describe, expect, it } from 'vitest'
import { ClaudeMutationTurnLimitResponse } from '../../api/generated/api-client'
import {
  describeClaudeAttemptTurnLimit,
  describeClaudeRunTurnLimit,
  parseClaudeTurnLimitDraft,
} from './describeClaudeTurnLimit'

const fact = (state: string, maxTurns?: number) => new ClaudeMutationTurnLimitResponse({ state, maxTurns })

describe('describeClaudeAttemptTurnLimit', () => {
  it.each([
    [fact('NotRecorded'), 'Not recorded'],
    [fact('NotRequested'), 'Not requested'],
    [fact('Requested', 12), 'Requested: 12 turns'],
    [fact('Requested', 1), 'Requested: 1 turn'],
    [fact('Unknown'), 'Unknown'],
    [fact('Requested'), 'Unknown'],
    [fact('Requested', 101), 'Unknown'],
    [fact('Requested', 2.5), 'Unknown'],
    [fact('SomethingNew'), 'Unknown'],
  ])('describes %j as %s', (input, expected) => {
    expect(describeClaudeAttemptTurnLimit(input)).toBe(expected)
  })

  it('returns nothing when the attempt is not a Claude mutation attempt', () => {
    expect(describeClaudeAttemptTurnLimit(null)).toBeNull()
    expect(describeClaudeAttemptTurnLimit(undefined)).toBeNull()
  })

  it('never describes any state as unlimited', () => {
    for (const state of ['NotRecorded', 'NotRequested', 'Requested', 'Unknown']) {
      expect(describeClaudeAttemptTurnLimit(fact(state, 5))).not.toMatch(/unlimited/i)
    }
  })
})

describe('describeClaudeRunTurnLimit', () => {
  it.each([
    [fact('NotRequested'), 'Not requested'],
    [fact('Requested', 30), '30 turns'],
    [fact('Unknown', 30), 'Unknown'],
    [fact('Requested', 0), 'Unknown'],
    [undefined, 'Unknown'],
    [null, 'Unknown'],
  ])('describes %j as %s', (input, expected) => {
    expect(describeClaudeRunTurnLimit(input)).toBe(expected)
  })
})

describe('parseClaudeTurnLimitDraft', () => {
  it('treats the empty string as a clear', () => {
    expect(parseClaudeTurnLimitDraft('')).toEqual({ kind: 'clear' })
  })

  it.each(['1', '7', '42', '99', '100'])('accepts %s', (draft) => {
    expect(parseClaudeTurnLimitDraft(draft)).toEqual({ kind: 'set', maxTurns: Number(draft) })
  })

  it.each(['0', '-1', '101', '999', '1000', '3.5', '1e2', '+5', ' ', '  ', ' 5', '5 ', 'abc', '5a', '007', '٥', '0x10'])(
    'rejects %j without clamping',
    (draft) => {
      expect(parseClaudeTurnLimitDraft(draft)).toEqual({ kind: 'invalid' })
    },
  )
})
