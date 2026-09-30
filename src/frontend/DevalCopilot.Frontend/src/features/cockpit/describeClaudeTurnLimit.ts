import type { ClaudeMutationTurnLimitResponse } from '../../api/clients'

/** Mirrors the backend's accepted range for the Claude agentic-turn request; the server stays authoritative. */
export const CLAUDE_TURN_LIMIT_MIN = 1
export const CLAUDE_TURN_LIMIT_MAX = 100

function isValidTurnCount(value: number | undefined | null): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= CLAUDE_TURN_LIMIT_MIN && value <= CLAUDE_TURN_LIMIT_MAX
}

function turnsText(count: number): string {
  return count === 1 ? '1 turn' : `${count} turns`
}

/**
 * Text for one attempt's immutable claim-time turn-limit fact, or `null` when the attempt is not a
 * Claude mutation attempt (nothing to show). A legacy `NotRecorded` fact is never read as
 * "unlimited" or as an observed capacity, and `NotRequested` (a coherent new attempt with no
 * request) stays distinct from both `NotRecorded` and `Unknown`. The value is a request only.
 */
export function describeClaudeAttemptTurnLimit(fact: ClaudeMutationTurnLimitResponse | null | undefined): string | null {
  if (!fact) {
    return null
  }

  switch (fact.state) {
    case 'NotRecorded':
      return 'Not recorded'
    case 'NotRequested':
      return 'Not requested'
    case 'Requested':
      return isValidTurnCount(fact.maxTurns) ? `Requested: ${turnsText(fact.maxTurns)}` : 'Unknown'
    default:
      return 'Unknown'
  }
}

/**
 * Text for the run's current saved request (applies to future attempts only). A missing value, an
 * invalid stored value, or an unrecognised state is "Unknown" and never a number.
 */
export function describeClaudeRunTurnLimit(fact: ClaudeMutationTurnLimitResponse | null | undefined): string {
  switch (fact?.state) {
    case 'NotRequested':
      return 'Not requested'
    case 'Requested':
      return isValidTurnCount(fact.maxTurns) ? turnsText(fact.maxTurns) : 'Unknown'
    default:
      return 'Unknown'
  }
}

export type ClaudeTurnLimitDraft = { kind: 'clear' } | { kind: 'set'; maxTurns: number } | { kind: 'invalid' }

/**
 * Parses the owner's draft locally: an empty string clears the request; otherwise only a canonical
 * whole number 1..100 (ASCII digits, no sign, spaces, fraction, exponent, or leading zero) is
 * accepted. Nothing is trimmed or clamped.
 */
export function parseClaudeTurnLimitDraft(draft: string): ClaudeTurnLimitDraft {
  if (draft === '') {
    return { kind: 'clear' }
  }
  if (!/^[1-9][0-9]{0,2}$/.test(draft)) {
    return { kind: 'invalid' }
  }
  const value = Number(draft)
  return value >= CLAUDE_TURN_LIMIT_MIN && value <= CLAUDE_TURN_LIMIT_MAX ? { kind: 'set', maxTurns: value } : { kind: 'invalid' }
}
