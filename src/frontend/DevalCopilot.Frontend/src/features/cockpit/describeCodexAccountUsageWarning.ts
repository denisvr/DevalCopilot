import type { CodexAccountUsageWarningSettingResponse, GetCodexAccountUsageWarningResponse } from '../../api/clients'

/** Mirrors the backend's accepted range for the Codex account-usage warning; the server stays authoritative. */
export const CODEX_ACCOUNT_USAGE_WARNING_MIN = 1
export const CODEX_ACCOUNT_USAGE_WARNING_MAX = 100

function isValidPercent(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= CODEX_ACCOUNT_USAGE_WARNING_MIN &&
    value <= CODEX_ACCOUNT_USAGE_WARNING_MAX
  )
}

/**
 * The saved warning percentage a valid `Configured` setting carries, or null for every other state
 * (no warning, an unknown stored value, a missing or malformed projection). Never a guessed number.
 */
export function configuredWarningPercent(setting: CodexAccountUsageWarningSettingResponse | null | undefined): number | null {
  return setting?.state === 'Configured' && isValidPercent(setting.percent) ? setting.percent : null
}

/** Text for the run's current saved warning. A missing value, an invalid stored value, or an unrecognised state is "Unknown". */
export function describeRunAccountUsageWarning(setting: CodexAccountUsageWarningSettingResponse | null | undefined): string {
  switch (setting?.state) {
    case 'NotConfigured':
      return 'Not configured'
    case 'Configured':
      return isValidPercent(setting.percent) ? `${setting.percent}% used` : 'Unknown'
    default:
      return 'Unknown'
  }
}

export type CodexAccountUsageWarningDraft = { kind: 'clear' } | { kind: 'set'; percent: number } | { kind: 'invalid' }

/**
 * Parses the owner's draft locally: an empty string clears the warning; otherwise only a canonical
 * whole number 1..100 (ASCII digits, no sign, spaces, fraction, exponent, or leading zero) is
 * accepted. Nothing is trimmed or clamped.
 */
export function parseCodexAccountUsageWarningDraft(draft: string): CodexAccountUsageWarningDraft {
  if (draft === '') {
    return { kind: 'clear' }
  }
  if (!/^[1-9][0-9]{0,2}$/.test(draft)) {
    return { kind: 'invalid' }
  }
  const value = Number(draft)
  return isValidPercent(value) ? { kind: 'set', percent: value } : { kind: 'invalid' }
}

/** What one explicit warning check shows. `observed` results are dated; `unavailable` never carries a classification. */
export type CodexAccountUsageWarningCheckView =
  | { kind: 'below' | 'reached'; headline: string; observedAt: string; details: string[] }
  | { kind: 'unavailable'; headline: string }

const UNVERIFIED: CodexAccountUsageWarningCheckView = {
  kind: 'unavailable',
  headline: 'The Codex account warning could not be verified for this check.',
}

function unavailable(headline: string): CodexAccountUsageWarningCheckView {
  return { kind: 'unavailable', headline }
}

function observedAtText(value: unknown): string | null {
  if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
    return null
  }
  try {
    return value.toLocaleString()
  } catch {
    return null
  }
}

interface ValidWindow {
  bucket: string
  kind: 'primary' | 'secondary'
  usedPercent: number
  reached: boolean
}

function validWindows(response: GetCodexAccountUsageWarningResponse): ValidWindow[] | null {
  if (!Array.isArray(response.windows) || response.windows.length === 0) {
    return null
  }
  const windows: ValidWindow[] = []
  for (const window of response.windows) {
    const kind = window?.window === 'Primary' ? 'primary' : window?.window === 'Secondary' ? 'secondary' : null
    const usedPercent = window?.usedPercent
    if (
      !kind ||
      typeof usedPercent !== 'number' ||
      !Number.isInteger(usedPercent) ||
      usedPercent < 0 ||
      usedPercent > 100 ||
      typeof window.reachedThreshold !== 'boolean'
    ) {
      return null
    }
    const bucket = typeof window.bucketId === 'string' && window.bucketId !== '' ? window.bucketId : 'account'
    windows.push({ bucket, kind, usedPercent, reached: window.reachedThreshold })
  }
  return windows
}

/**
 * Classifies one explicit warning-check response for the warning the caller currently shows
 * (`expectedPercent`). Only a dated, internally consistent Below or Reached answer for exactly that
 * saved threshold is a classification: anything else (a different threshold, an unknown state, a
 * missing date, an inconsistent window list) is "could not be verified", never "below". The answer
 * describes the host's Codex account at that moment, not usage attributable to this run, and never
 * eligibility, readiness or remaining capacity.
 */
export function describeCodexAccountUsageWarningCheck(
  response: GetCodexAccountUsageWarningResponse | null | undefined,
  expectedPercent: number,
): CodexAccountUsageWarningCheckView {
  if (!response) {
    return UNVERIFIED
  }
  switch (response.state) {
    case 'Unavailable':
      return response.reason === 'EvidenceExpired'
        ? unavailable('The Codex account observation was no longer current, so the warning could not be checked.')
        : response.reason === 'ConfigurationChanged'
          ? unavailable('The saved warning or the Codex launch target changed during the check. Check again.')
          : unavailable('Codex account usage could not be read, so the warning could not be checked.')
    case 'NotConfigured':
    case 'SettingInvalid':
      return unavailable('The saved warning changed or is not a valid setting. Reload the run before checking again.')
    case 'Below':
    case 'Reached':
      break
    default:
      return UNVERIFIED
  }

  const observedAt = observedAtText(response.observedAtUtc)
  const windows = validWindows(response)
  if (response.thresholdPercent !== expectedPercent || !observedAt || !windows) {
    return UNVERIFIED
  }

  const providerReached = response.providerReportedLimitReached === true
  const anyReached = windows.some((window) => window.reached || window.usedPercent >= expectedPercent)
  const details = windows.map(
    (window) => `${window.bucket} ${window.kind} window: ${window.usedPercent}% used${window.reached ? ' (warning reached)' : ''}`,
  )

  if (response.state === 'Below') {
    return anyReached || providerReached
      ? UNVERIFIED
      : {
          kind: 'below',
          headline: `No reported usage window had reached the ${expectedPercent}% warning when the Codex account was observed.`,
          observedAt,
          details,
        }
  }

  if (!anyReached && !providerReached) {
    return UNVERIFIED
  }
  return {
    kind: 'reached',
    headline: anyReached
      ? `A reported usage window had reached the ${expectedPercent}% warning when the Codex account was observed.`
      : 'The provider reported that a usage limit had been reached when the Codex account was observed.',
    observedAt,
    details,
  }
}
