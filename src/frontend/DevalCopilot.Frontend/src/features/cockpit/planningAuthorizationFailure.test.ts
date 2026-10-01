import { describe, expect, it } from 'vitest'
import { ApiException } from '../../api/generated/api-client'
import { describePlanningAuthorizationFailure } from './planningAuthorizationFailure'

function problem(code: unknown, detail = 'SERVER-WORDING with SECRET-TEXT') {
  return new ApiException('x', 409, JSON.stringify({ errors: [{ code, detail }] }), {}, null)
}

describe('describePlanningAuthorizationFailure', () => {
  it.each([
    'planning_authorizations.rationale_invalid',
    'planning_authorizations.rationale_conflict',
    'planning_authorizations.already_consumed',
    'planning_authorizations.source_stale',
    'planning_authorizations.source_invalid',
    'planning_authorizations.source_not_found',
    'planning_authorizations.context_not_current',
    'planning_authorizations.recorded_invalid',
  ])('maps %s to fixed copy that never echoes the server wording', (code) => {
    const message = describePlanningAuthorizationFailure(problem(code))

    expect(message.length).toBeGreaterThan(20)
    expect(message).not.toMatch(/SERVER-WORDING|SECRET-TEXT/)
  })

  it('gives distinct copy for distinct codes', () => {
    const messages = [
      'planning_authorizations.rationale_invalid',
      'planning_authorizations.rationale_conflict',
      'planning_authorizations.already_consumed',
    ].map((code) => describePlanningAuthorizationFailure(problem(code)))

    expect(new Set(messages).size).toBe(3)
  })

  it('falls back to one generic message for unknown codes, malformed bodies, prototype keys, and non-API errors', () => {
    const generic = describePlanningAuthorizationFailure(new Error('SECRET-TEXT'))

    expect(describePlanningAuthorizationFailure(problem('something.else'))).toBe(generic)
    expect(describePlanningAuthorizationFailure(problem('toString'))).toBe(generic)
    expect(describePlanningAuthorizationFailure(problem(42))).toBe(generic)
    expect(describePlanningAuthorizationFailure(new ApiException('x', 500, 'not json SECRET-TEXT', {}, null))).toBe(generic)
    expect(generic).not.toMatch(/SECRET-TEXT/)
  })
})
