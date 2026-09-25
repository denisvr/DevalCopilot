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

/** The run-level aggregate. Structurally matches the generated `RunTokenUsageSummaryResponse`.
 * `attemptsWithUnknownUsage` keeps its original, broader meaning — every dispatched attempt without
 * known usage — and always equals `pendingAttemptCount` plus `terminalAttemptsWithUnknownUsage`: a
 * still-running attempt is pending (not yet trusted, not a failure), while a terminal attempt with no
 * usage evidence is a genuine gap. */
export interface RunTokenUsageSummaryView {
  completeness?: string
  attemptsWithKnownUsage?: number
  attemptsWithUnknownUsage?: number
  pendingAttemptCount?: number
  terminalAttemptsWithUnknownUsage?: number
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
 * Whether `usage` is both shaped like known usage AND safe to trust for display, given the
 * attempt's own dispatch/running state. Status is checked FIRST and is decisive: a still-`running`
 * or not-yet-`dispatched` attempt is never trusted, even when the object handed in already has
 * well-formed-looking `inputTokens`/`outputTokens` fields — for example a malformed or tampered
 * value from an inconsistent persisted row. This mirrors the backend's own fail-closed rule
 * (`Attempt.GetAgentTokenUsageEvidence`), so the same "not yet concluded, therefore not trusted"
 * principle holds all the way to presentation, regardless of what is physically in the object.
 */
export function hasTrustedTokenUsage(
  usage: TokenUsageView | null | undefined,
  context: TokenUsageContext,
): usage is TokenUsageView {
  return context.dispatched && !context.running && hasKnownTokenUsage(usage)
}

/**
 * Describes one attempt's provider-reported token usage as plain text, separate from both the
 * semantic outcome and the host-measured process evidence. Absent usage is stated truthfully —
 * never inferred from output length, limits, or another provider's report. Dispatch/running state
 * is checked BEFORE the usage object's own shape, so a still-running or undispatched attempt is
 * always described as such even when handed a malformed object that superficially looks like known
 * usage (see `hasTrustedTokenUsage`).
 */
export function describeTokenUsage(usage: TokenUsageView | null | undefined, context: TokenUsageContext): string {
  if (!context.dispatched) {
    return 'Token usage: none (provider not invoked)'
  }
  if (context.running) {
    return 'Token usage not yet recorded'
  }
  if (hasKnownTokenUsage(usage)) {
    return `Tokens: ${describeCounts(usage)}`
  }
  return 'Token usage unknown'
}

function plural(count: number, singular: string): string {
  return `${count} ${singular}${count === 1 ? '' : 's'}`
}

/**
 * Describes why a `Partial` or `PendingEvidence` summary's gap attempts have no known usage, naming
 * each component explicitly rather than collapsing a still-running attempt and a concluded-without-
 * evidence attempt into one undifferentiated count. Never phrases a still-`Running` attempt as a
 * "terminal usage gap": the two are always named separately, and both are named together only when
 * both are genuinely present (a three-way mix of known, pending, and terminal-unknown attempts).
 */
function describeUnknownUsageGap(pendingCount: number, terminalUnknownCount: number): string {
  const pendingClause = pendingCount > 0 ? `${plural(pendingCount, 'attempt')} still running` : ''
  const terminalClause =
    terminalUnknownCount > 0
      ? `${plural(terminalUnknownCount, 'attempt')} concluded without usable token-usage evidence`
      : ''

  if (terminalClause && pendingClause) {
    return `${terminalClause} and ${pendingClause}`
  }

  return terminalClause || pendingClause
}

/**
 * Describes the run-level token-usage summary. Only a `Complete` summary is ever labeled a total; a
 * `Partial` sum is always labeled partial and names how many dispatched attempts it covers, so a
 * reader can never mistake it for the run's true total. `PendingEvidence` is kept distinct from
 * `Partial`: nothing has failed to report usage, some dispatched attempts simply have not concluded
 * yet — a still-running attempt's usage is never trusted even if already persisted.
 *
 * When no attempt has known usage yet (`attemptsWithKnownUsage` is zero), no numeric token count is
 * ever rendered — the summary's zero-valued sums in that state are an unpopulated accumulator, not a
 * provider-reported zero, and showing "0 input / 0 output" would misstate a real measurement that
 * does not exist. Conversely, once at least one attempt has known usage, its sum is shown exactly as
 * reported, including a genuine zero — a provider that truthfully reported no tokens is not the same
 * as no measurement at all.
 */
export function describeRunTokenUsage(summary: RunTokenUsageSummaryView | null | undefined): string {
  const known = summary?.attemptsWithKnownUsage ?? 0
  const unknown = summary?.attemptsWithUnknownUsage ?? 0
  const pending = summary?.pendingAttemptCount ?? 0
  const terminalUnknown = summary?.terminalAttemptsWithUnknownUsage ?? 0

  switch (summary?.completeness) {
    case 'Complete':
      return `Run token total: ${describeCounts(summary)} (all ${plural(known, 'dispatched attempt')} reported usage)`
    case 'Partial': {
      const gap = describeUnknownUsageGap(pending, terminalUnknown)
      if (known === 0) {
        return `No token usage recorded yet — ${gap}, so there is no partial count to show`
      }
      return (
        `Partial token count: ${describeCounts(summary)} from ${known} of ${plural(known + unknown, 'dispatched attempt')}`
        + ` — ${gap}, so this is not the run total`
      )
    }
    case 'PendingEvidence': {
      if (known === 0) {
        return `No token usage recorded yet — ${plural(pending, 'attempt')} still running, so this is not the run total yet`
      }
      return (
        `Partial token count so far: ${describeCounts(summary)} from ${known} of ${plural(known + pending, 'dispatched attempt')}`
        + ` — ${plural(pending, 'attempt')} still running, so this is not the run total yet`
      )
    }
    case 'NoDispatchedAttempts':
      return 'No token usage data yet — no agent attempt has been dispatched'
    default:
      return 'Token usage summary unavailable'
  }
}
