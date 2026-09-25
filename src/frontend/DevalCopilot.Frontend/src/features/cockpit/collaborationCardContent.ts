import type { CollaborationTimelineCard } from './types'

export interface CollaborationCardSection {
  label: string
  value: string
}

const TYPED_CARD_FIELDS: Record<string, readonly CollaborationCardSection[]> = {
  Challenge: [
    { label: 'Disputed item', value: 'disputedItem' },
    { label: 'Material impact', value: 'materialImpact' },
    { label: 'Reasoning', value: 'reasoning' },
    { label: 'Alternative or question', value: 'alternativeOrQuestion' },
  ],
  Decision: [
    { label: 'Resolution', value: 'resolution' },
    { label: 'Rationale', value: 'rationale' },
    { label: 'Resulting plan changes', value: 'resultingPlanChanges' },
    { label: 'Next action', value: 'nextAction' },
  ],
  ReviewFinding: [
    { label: 'Severity', value: 'severity' },
    { label: 'Category', value: 'category' },
    { label: 'Evidence', value: 'evidence' },
    { label: 'Required change', value: 'requiredChange' },
  ],
  RevisionResponse: [
    { label: 'Disposition', value: 'disposition' },
    { label: 'Evidence', value: 'evidence' },
    { label: 'Resulting source changes', value: 'resultingSourceChanges' },
  ],
}

const REVIEW_SEVERITIES: Record<string, string> = {
  low: 'Low',
  medium: 'Medium',
  high: 'High',
  critical: 'Critical',
}

const REVIEW_CATEGORIES: Record<string, string> = {
  correctness: 'Correctness',
  security: 'Security',
  standards: 'Standards',
  testCoverage: 'Test coverage',
  design: 'Design',
}

// Mirrors ChallengeResolutionOutputSchema's own closed `resolution` enum exactly
// (AcceptedResolution / PartiallyAcceptedResolution / RejectedResolution) — never a superset.
const DECISION_RESOLUTIONS: Record<string, string> = {
  accepted: 'Accepted',
  partiallyAccepted: 'Partially accepted',
  rejected: 'Rejected',
}

// Closed value maps for fields whose real backend schema restricts them to an enum. A field
// absent from this table is rendered as free text (already bounded to a non-empty string by the
// caller); a field present here that holds any value outside its map is unknown and must fail
// closed, never render as if it were a valid protocol value.
const CLOSED_FIELD_VALUES: Record<string, Record<string, Record<string, string>>> = {
  Decision: { resolution: DECISION_RESOLUTIONS },
  ReviewFinding: { severity: REVIEW_SEVERITIES, category: REVIEW_CATEGORIES },
}

const TYPED_CARD_TYPES = new Set(Object.keys(TYPED_CARD_FIELDS))

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * Parses only the closed, project-owned fields for the four typed cards. Unknown or malformed
 * content returns null so the UI never invents a semantic value from a legacy payload.
 */
export function parseCollaborationCardContent(card: CollaborationTimelineCard): CollaborationCardSection[] | null {
  if (!TYPED_CARD_TYPES.has(card.type) || !card.structuredContentJson) {
    return null
  }

  try {
    const parsed: unknown = JSON.parse(card.structuredContentJson)
    if (!isRecord(parsed)) {
      return null
    }

    const fields = TYPED_CARD_FIELDS[card.type]
    const expectedNames = fields.map((field) => field.value)
    const actualNames = Object.keys(parsed)
    if (actualNames.length !== expectedNames.length || expectedNames.some((name) => !actualNames.includes(name))) {
      return null
    }

    const sections: CollaborationCardSection[] = []
    for (const field of fields) {
      const value = parsed[field.value]
      if (typeof value !== 'string' || value.trim().length === 0) {
        return null
      }

      const closedValues = CLOSED_FIELD_VALUES[card.type]?.[field.value]
      const displayValue = closedValues ? (closedValues[value] ?? null) : value
      if (!displayValue) {
        return null
      }

      sections.push({ label: field.label, value: displayValue })
    }

    return sections
  } catch {
    return null
  }
}

export function isTypedCollaborationCard(card: CollaborationTimelineCard): boolean {
  return TYPED_CARD_TYPES.has(card.type)
}
