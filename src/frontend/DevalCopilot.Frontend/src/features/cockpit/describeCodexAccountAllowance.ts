/** One reported allowance window. Structurally matches the generated `CodexAllowanceWindowResponse`.
 * `windowDurationMins` and `resetsAtUtc` are independently optional per the documented response. */
export interface CodexAllowanceWindowView {
  usedPercent?: number
  windowDurationMins?: number
  resetsAtUtc?: string | Date
}

/** One reported allowance bucket. Structurally matches the generated `CodexAllowanceBucketResponse`.
 * `limitId` is `undefined` for the single legacy (unlabeled) snapshot. */
export interface CodexAllowanceBucketView {
  limitId?: string
  primary?: CodexAllowanceWindowView
  secondary?: CodexAllowanceWindowView
}

/** The host-scoped Codex account-allowance snapshot. Structurally matches the generated
 * `CodexAccountAllowanceResponse`. `status` is `'Observed'` or `'Unknown'` — every unavailable
 * case (no vetted launch target, missing authentication, an unsupported protocol, a malformed
 * response, a timeout, or a process failure) is folded into the same `'Unknown'` value, never
 * displayed as a zero-valued window. */
export interface CodexAccountAllowanceView {
  status?: string
  retrievedAtUtc?: string | Date
  buckets?: CodexAllowanceBucketView[]
}

/**
 * A reported percentage is either trusted exactly as given or rejected outright — never clamped
 * into a plausible-looking value a provider never actually reported.
 */
function isValidUsedPercent(usedPercent: number | undefined): usedPercent is number {
  return typeof usedPercent === 'number' && Number.isInteger(usedPercent) && usedPercent >= 0 && usedPercent <= 100
}

function describeWindowDuration(windowDurationMins: number | undefined): string {
  if (typeof windowDurationMins !== 'number' || !Number.isSafeInteger(windowDurationMins) || windowDurationMins < 0) {
    return 'window: Unknown'
  }

  const hours = windowDurationMins / 60
  return Number.isInteger(hours) ? `${hours}h window` : `${windowDurationMins}min window`
}

function describeWindow(label: string, window: CodexAllowanceWindowView | undefined): string {
  if (!window || !isValidUsedPercent(window.usedPercent)) {
    return `${label}: Unknown`
  }

  const reset = window.resetsAtUtc ? new Date(window.resetsAtUtc) : null
  const resetLabel = reset && !Number.isNaN(reset.getTime()) ? `resets ${reset.toLocaleString()}` : 'reset: Unknown'
  return `${label}: ${window.usedPercent}% used; ${describeWindowDuration(window.windowDurationMins)}; ${resetLabel}`
}

function describeBucket(bucket: CodexAllowanceBucketView): string {
  const label = bucket.limitId ? `Codex (${bucket.limitId})` : 'Codex'
  return `${label} — ${describeWindow('Primary', bucket.primary)}; ${describeWindow('Secondary', bucket.secondary)}`
}

/**
 * One line per reported bucket — buckets are never combined or averaged into one invented
 * aggregate. Never displays an unpopulated zero as if the provider had actually reported it: an
 * `Unknown` status, a status this reader does not recognize, or no buckets at all always renders
 * as a single explicit "Unknown" line.
 */
export function describeCodexAccountAllowance(allowance: CodexAccountAllowanceView | null | undefined): string[] {
  if (!allowance || allowance.status !== 'Observed' || !allowance.buckets || allowance.buckets.length === 0) {
    return ['Codex account usage: Unknown']
  }

  return allowance.buckets.map(describeBucket)
}

export function describeCodexAccountAllowanceRetrievedAt(
  allowance: CodexAccountAllowanceView | null | undefined,
): string | null {
  if (!allowance || allowance.status !== 'Observed' || !allowance.retrievedAtUtc) {
    return null
  }

  const retrievedAt = new Date(allowance.retrievedAtUtc)
  return Number.isNaN(retrievedAt.getTime()) ? null : `Retrieved ${retrievedAt.toLocaleString()}`
}
