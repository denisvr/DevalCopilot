import type { CodexAccountUsageDecisionResponse, CodexAccountUsageStopResponse } from '../../api/clients'

/** Mirrors the backend's accepted range for the Codex account-usage stop; the server stays authoritative. */
export const CODEX_ACCOUNT_USAGE_STOP_MIN = 1
export const CODEX_ACCOUNT_USAGE_STOP_MAX = 100

function isValidPercent(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= CODEX_ACCOUNT_USAGE_STOP_MIN &&
    value <= CODEX_ACCOUNT_USAGE_STOP_MAX
  )
}

/**
 * Text for the run's current saved stop (applies to new Codex claims only). A missing value, an
 * invalid stored value, or an unrecognised state is "Unknown" and never a number.
 */
export function describeRunAccountUsageStop(fact: CodexAccountUsageStopResponse | null | undefined): string {
  switch (fact?.state) {
    case 'NotConfigured':
      return 'Not configured'
    case 'Configured':
      return isValidPercent(fact.percent) ? `${fact.percent}% used` : 'Unknown'
    default:
      return 'Unknown'
  }
}

/** Text for one attempt's immutable claim-time stop fact, or null when there is no fact to show. */
export function describeAttemptAccountUsageStop(fact: CodexAccountUsageStopResponse | null | undefined): string | null {
  if (!fact) {
    return null
  }
  switch (fact.state) {
    case 'NotConfigured':
      return 'Claimed with no account-usage stop'
    case 'Configured':
      return isValidPercent(fact.percent)
        ? `Claimed with account-usage stop: ${fact.percent}% used`
        : 'Account-usage stop at claim: Unknown'
    default:
      return 'Account-usage stop at claim: Unknown'
  }
}

export type CodexAccountUsageStopDraft = { kind: 'clear' } | { kind: 'set'; percent: number } | { kind: 'invalid' }

/**
 * Parses the owner's draft locally: an empty string clears the stop; otherwise only a canonical
 * whole number 1..100 (ASCII digits, no sign, spaces, fraction, exponent, or leading zero) is
 * accepted. Nothing is trimmed or clamped.
 */
export function parseCodexAccountUsageStopDraft(draft: string): CodexAccountUsageStopDraft {
  if (draft === '') {
    return { kind: 'clear' }
  }
  if (!/^[1-9][0-9]{0,2}$/.test(draft)) {
    return { kind: 'invalid' }
  }
  const value = Number(draft)
  return value >= CODEX_ACCOUNT_USAGE_STOP_MIN && value <= CODEX_ACCOUNT_USAGE_STOP_MAX
    ? { kind: 'set', percent: value }
    : { kind: 'invalid' }
}

const UNVERIFIED = 'The stored account-usage decision could not be verified.'

function percentText(value: unknown): string {
  return isValidPercent(value) ? `${value}%` : 'unknown'
}

function headlineFor(decision: CodexAccountUsageDecisionResponse): string {
  if (decision.state !== 'Recorded') {
    return UNVERIFIED
  }
  switch (decision.reason) {
    case 'ThresholdReached':
      return `Not started: a reported usage window reached the configured stop (${percentText(decision.thresholdPercent)}).`
    case 'ProviderReportedLimitReached':
      return 'Not started: the provider reported that a limit was reached.'
    case 'EvidenceUnavailable':
      return `Not started: account usage could not be read, so the configured stop (${percentText(decision.thresholdPercent)}) could not be checked.`
    case 'EvidenceExpired':
      return 'Not started: the account-usage reading was no longer current when it was checked.'
    case 'ThresholdUnusable':
      return 'Not started: the saved stop was not a valid setting.'
    default:
      return UNVERIFIED
  }
}

function retrievedAtText(value: unknown): string | null {
  if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
    return null
  }
  try {
    return value.toLocaleString()
  } catch {
    return null
  }
}

/**
 * Headline and detail lines for the stored decision of one attempt that was not started because of
 * the account-usage stop, or null when there is no decision (an absent decision is never shown as
 * "below threshold"). Raw provider text is never displayed.
 */
export function describeAccountUsageDecision(
  decision: CodexAccountUsageDecisionResponse | null | undefined,
): { headline: string; details: string[] } | null {
  if (!decision) {
    return null
  }
  const headline = headlineFor(decision)
  const details: string[] = []
  if (decision.state === 'Recorded') {
    const retrieved = retrievedAtText(decision.retrievedAtUtc)
    if (retrieved) {
      details.push(`Host retrieval time: ${retrieved}`)
    }
    for (const window of Array.isArray(decision.windows) ? decision.windows : []) {
      if (!window || !isFinitePercent(window.usedPercent)) {
        continue
      }
      const kind = window.window === 'Primary' ? 'primary' : window.window === 'Secondary' ? 'secondary' : null
      if (!kind) {
        continue
      }
      const bucket = typeof window.bucketId === 'string' && window.bucketId !== '' ? window.bucketId : 'account'
      details.push(`${bucket} ${kind} window: ${window.usedPercent}% used`)
    }
  }
  return { headline, details }
}

function isFinitePercent(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}
