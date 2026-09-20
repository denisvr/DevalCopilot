import { describe, expect, it } from 'vitest'
import type { CollaborationTimelineCard } from './types'
import { selectLatestExecutionReportMessageId } from './selectLatestExecutionReport'

function card(overrides: Partial<CollaborationTimelineCard> = {}): CollaborationTimelineCard {
  return {
    sequence: 1,
    id: 'message-1',
    attemptId: null,
    actor: { kind: 'Agent', role: 'Implementer', provider: 'ClaudeCode' },
    recipient: { kind: 'Agent', role: null, provider: 'Codex' },
    type: 'ExecutionReport',
    inReplyToMessageId: 'proposal-1',
    summary: 'Implemented the plan.',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-09-20T12:00:00Z',
    ...overrides,
  }
}

describe('selectLatestExecutionReportMessageId', () => {
  it('selects an Implementer report independently of its provider', () => {
    expect(selectLatestExecutionReportMessageId([
      card({ actor: { kind: 'Agent', role: 'Implementer', provider: 'Codex' } }),
    ])).toBe('message-1')
  })

  it('rejects provider-observed output authored under another role', () => {
    expect(selectLatestExecutionReportMessageId([
      card({ actor: { kind: 'Agent', role: 'CriticalReviewer', provider: 'ClaudeCode' } }),
    ])).toBeNull()
  })

  it('rejects simulated output even when the role matches', () => {
    expect(selectLatestExecutionReportMessageId([card({ provenance: 'Simulated' })])).toBeNull()
  })
})
