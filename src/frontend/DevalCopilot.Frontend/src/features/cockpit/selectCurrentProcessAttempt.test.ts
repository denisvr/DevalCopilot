import { describe, expect, it } from 'vitest'
import type { CollaborationCard } from './types'
import { selectCurrentProcessAttemptId } from './selectCurrentProcessAttempt'

function card(overrides: Partial<CollaborationCard>): CollaborationCard {
  return {
    sequence: 0,
    id: 'event-0',
    attemptId: null,
    eventType: 'run.started',
    actor: { kind: 'Orchestrator', role: null, provider: null },
    summary: '',
    occurredAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('selectCurrentProcessAttemptId', () => {
  it('returns null for an empty card list', () => {
    expect(selectCurrentProcessAttemptId([])).toBeNull()
  })

  it('returns null when no card is a process.output_captured event', () => {
    const cards = [
      card({ sequence: 1, eventType: 'run.started' }),
      card({ sequence: 2, eventType: 'codex.proposal' }),
    ]

    expect(selectCurrentProcessAttemptId(cards)).toBeNull()
  })

  it('ignores a process.output_captured card with no attempt id', () => {
    const cards = [card({ sequence: 1, eventType: 'process.output_captured', attemptId: null })]

    expect(selectCurrentProcessAttemptId(cards)).toBeNull()
  })

  it('returns the attempt id of the single matching card', () => {
    const cards = [card({ sequence: 1, eventType: 'process.output_captured', attemptId: 'attempt-1' })]

    expect(selectCurrentProcessAttemptId(cards)).toBe('attempt-1')
  })

  it('deterministically picks the highest-sequence match, regardless of array order', () => {
    const cards = [
      card({ sequence: 5, eventType: 'process.output_captured', attemptId: 'attempt-2' }),
      card({ sequence: 1, eventType: 'process.output_captured', attemptId: 'attempt-1' }),
      card({ sequence: 3, eventType: 'process.output_captured', attemptId: 'attempt-3' }),
    ]

    expect(selectCurrentProcessAttemptId(cards)).toBe('attempt-2')
  })

  it('ignores non-matching cards interleaved with the matching one', () => {
    const cards = [
      card({ sequence: 1, eventType: 'run.started' }),
      card({ sequence: 2, eventType: 'process.output_captured', attemptId: 'attempt-1' }),
      card({ sequence: 3, eventType: 'claude.challenge' }),
    ]

    expect(selectCurrentProcessAttemptId(cards)).toBe('attempt-1')
  })
})
