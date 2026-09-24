/** Provider-reported token usage the API exposes beside an attempt's semantic outcome and process
 * evidence. Structurally matches the generated `AgentTokenUsageResponse`. */
export interface TokenUsageView {
  inputTokens?: number
  outputTokens?: number
  cacheCreationInputTokens?: number
  cacheReadInputTokens?: number
}

export interface TokenUsageContext {
  /** Whether the provider was durably dispatched for this attempt. */
  dispatched: boolean
  /** Whether the attempt is still running (no terminal result recorded yet). */
  running: boolean
}

/** The run-level aggregate. Structurally matches the generated `RunTokenUsageSummaryResponse`. */
export interface RunTokenUsageSummaryView {
  completeness?: string
  attemptsWithKnownUsage?: number
  attemptsWithUnknownUsage?: number
  inputTokens?: number
  outputTokens?: number
  cacheCreationInputTokens?: number
  cacheReadInputTokens?: number
}

export function formatTokenCount(count: number): string {
  return Math.max(0, Math.trunc(count)).toLocaleString('en-US')
}

function describeCounts(usage: TokenUsageView): string {
  const parts = [`${formatTokenCount(usage.inputTokens ?? 0)} input`, `${formatTokenCount(usage.outputTokens ?? 0)} output`]
  if (typeof usage.cacheCreationInputTokens === 'number') {
    parts.push(`${formatTokenCount(usage.cacheCreationInputTokens)} cache write`)
  }
  if (typeof usage.cacheReadInputTokens === 'number') {
    parts.push(`${formatTokenCount(usage.cacheReadInputTokens)} cache read`)
  }
  return parts.join(' · ')
}

export function hasKnownTokenUsage(usage: TokenUsageView | null | undefined): usage is TokenUsageView {
  return typeof usage?.inputTokens === 'number' && typeof usage.outputTokens === 'number'
}

/**
 * Describes one attempt's provider-reported token usage as plain text, separate from both the
 * semantic outcome and the host-measured process evidence. Absent usage is stated truthfully —
 * never inferred from output length, limits, or another provider's report.
 */
export function describeTokenUsage(usage: TokenUsageView | null | undefined, context: TokenUsageContext): string {
  if (hasKnownTokenUsage(usage)) {
    return `Tokens: ${describeCounts(usage)}`
  }
  if (!context.dispatched) {
    return 'Token usage: none (provider not invoked)'
  }
  return context.running ? 'Token usage not yet recorded' : 'Token usage unknown'
}

function plural(count: number, singular: string): string {
  return `${count} ${singular}${count === 1 ? '' : 's'}`
}

/**
 * Describes the run-level token-usage summary. Only a `Complete` summary is ever labeled a total;
 * a `Partial` sum is always labeled partial and names how many dispatched attempts it covers, so
 * a reader can never mistake it for the run's true total.
 */
export function describeRunTokenUsage(summary: RunTokenUsageSummaryView | null | undefined): string {
  const known = summary?.attemptsWithKnownUsage ?? 0
  const unknown = summary?.attemptsWithUnknownUsage ?? 0
  switch (summary?.completeness) {
    case 'Complete':
      return `Run token total: ${describeCounts(summary)} (all ${plural(known, 'dispatched attempt')} reported usage)`
    case 'Partial':
      return (
        `Partial token count: ${describeCounts(summary)} from ${known} of ${plural(known + unknown, 'dispatched attempt')}`
        + ` — ${plural(unknown, 'attempt')} ${unknown === 1 ? 'has' : 'have'} no usage evidence, so this is not the run total`
      )
    case 'NoDispatchedAttempts':
      return 'No token usage data yet — no agent attempt has been dispatched'
    default:
      return 'Token usage summary unavailable'
  }
}
