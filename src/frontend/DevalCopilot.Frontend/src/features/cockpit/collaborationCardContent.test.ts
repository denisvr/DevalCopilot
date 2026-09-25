import { describe, expect, it } from 'vitest'
import type { CollaborationTimelineCard } from './types'
import { parseCollaborationCardContent } from './collaborationCardContent'

function card(overrides: Partial<CollaborationTimelineCard>): CollaborationTimelineCard {
  return {
    sequence: 1,
    id: 'message-1',
    attemptId: null,
    actor: { kind: 'Agent', role: 'CodeReviewer', provider: 'Codex' },
    recipient: { kind: 'Agent', role: 'Implementer', provider: 'ClaudeCode' },
    type: 'ReviewFinding',
    inReplyToMessageId: 'execution-1',
    summary: 'A finding',
    details: [],
    provenance: 'ProviderObserved',
    occurredAtUtc: '2026-09-25T12:00:00Z',
    ...overrides,
  }
}

describe('parseCollaborationCardContent', () => {
  it('maps closed ReviewFinding values to readable labels', () => {
    expect(
      parseCollaborationCardContent(
        card({
          structuredContentJson: '{"severity":"critical","category":"testCoverage","evidence":"Evidence","requiredChange":"Change"}',
        }),
      ),
    ).toEqual([
      { label: 'Severity', value: 'Critical' },
      { label: 'Category', value: 'Test coverage' },
      { label: 'Evidence', value: 'Evidence' },
      { label: 'Required change', value: 'Change' },
    ])
  })

  it('maps every closed Decision resolution value to its readable label', () => {
    for (const [resolution, label] of [
      ['accepted', 'Accepted'],
      ['partiallyAccepted', 'Partially accepted'],
      ['rejected', 'Rejected'],
    ] as const) {
      expect(
        parseCollaborationCardContent(
          card({
            type: 'Decision',
            structuredContentJson: JSON.stringify({
              resolution,
              rationale: 'Rationale',
              resultingPlanChanges: 'Changes',
              nextAction: 'Next action',
            }),
          }),
        ),
      ).toEqual([
        { label: 'Resolution', value: label },
        { label: 'Rationale', value: 'Rationale' },
        { label: 'Resulting plan changes', value: 'Changes' },
        { label: 'Next action', value: 'Next action' },
      ])
    }
  })

  // ChallengeResolutionOutputSchema's own `resolution` enum is closed to exactly "accepted",
  // "partiallyAccepted", and "rejected" (see docs/architecture backend schema). A well-formed
  // Decision payload whose resolution is any other value — including a plausible-looking one like
  // "approved" — must never be treated as a valid typed Decision.
  it('fails closed for a well-formed Decision whose resolution is not a protocol closed value', () => {
    expect(
      parseCollaborationCardContent(
        card({
          type: 'Decision',
          structuredContentJson: JSON.stringify({
            resolution: 'approved',
            rationale: 'Rationale',
            resultingPlanChanges: 'Changes',
            nextAction: 'Next action',
          }),
        }),
      ),
    ).toBeNull()
  })

  it('fails closed for malformed JSON, missing fields, and unknown closed values', () => {
    expect(parseCollaborationCardContent(card({ structuredContentJson: '{' }))).toBeNull()
    expect(parseCollaborationCardContent(card({ structuredContentJson: '{"severity":"high"}' }))).toBeNull()
    expect(
      parseCollaborationCardContent(
        card({
          structuredContentJson: '{"severity":"urgent","category":"correctness","evidence":"Evidence","requiredChange":"Change"}',
        }),
      ),
    ).toBeNull()
  })
})
