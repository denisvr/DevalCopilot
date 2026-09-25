interface AgentProcessDurationSummaryBannerProps {
  status: string | null | undefined
  totalMeasuredMilliseconds: number | null | undefined
  dispatchedAttemptCount: number | null | undefined
  pendingAttemptCount: number | null | undefined
  validEvidenceCount: number | null | undefined
  malformedEvidenceCount: number | null | undefined
}

// Formats a non-negative measured duration without ever collapsing a genuinely positive value down
// to a display that reads as "nothing measured". A real TimeSpan.Zero still displays as an explicit
// "0 ms" (distinct from "< 1 ms" and distinct from the null "no measurement yet" states, which this
// component never reaches — see the non-Complete branches below). A positive value under 1 ms
// (representable because the API projects TotalMeasuredMilliseconds as an untruncated double) shows
// as "< 1 ms" rather than rounding down to zero.
function formatDuration(milliseconds: number): string {
  if (milliseconds === 0) {
    return '0 ms'
  }

  if (milliseconds < 1) {
    return '< 1 ms'
  }

  if (milliseconds < 1000) {
    return `${Math.round(milliseconds)} ms`
  }

  const seconds = milliseconds / 1000
  const rounded = Math.round(seconds * 10) / 10
  return `${rounded} s`
}

/**
 * A run-wide, read-only summary of HOST-MEASURED Agent process duration — pure telemetry over
 * already-dispatched Agent attempts' own recorded process evidence. Independent of, and never
 * combined with, AgentClaimBudgetBanner's count budget or AgentInvocationTimeBudgetBanner's
 * reservation-time budget above it: this never shows a limit, a "remaining" amount, or an
 * "exhausted" state, only what has actually been measured so far. A terminal attempt with missing
 * or malformed evidence is always surfaced distinctly (Partial/Malformed) rather than silently
 * excluded or counted as zero, and a real zero-duration measurement (Complete with an explicit
 * "0 ms" total) is always visibly distinct from "no measurement is available yet"
 * (Pending/NoDispatchedAttempts, which show no total at all). A valid-but-unrepresentable total sum
 * (UnrepresentableTotal) is shown distinctly from a run with genuinely malformed evidence
 * (MalformedEvidence) — the individual measurements are real in both the Complete and
 * UnrepresentableTotal cases.
 */
export function AgentProcessDurationSummaryBanner({
  status,
  totalMeasuredMilliseconds,
  dispatchedAttemptCount,
  pendingAttemptCount,
  validEvidenceCount,
  malformedEvidenceCount,
}: AgentProcessDurationSummaryBannerProps) {
  if (status == null || status === 'NoDispatchedAttempts') {
    return null
  }

  const dispatched = dispatchedAttemptCount ?? 0
  const pending = pendingAttemptCount ?? 0
  const valid = validEvidenceCount ?? 0
  const malformed = malformedEvidenceCount ?? 0

  if (status === 'MalformedEvidence') {
    return (
      <div className="dc-agent-process-duration-summary-banner" role="alert">
        None of this run's terminal Agent attempts carries valid host-measured process-duration
        evidence ({malformed} of {dispatched} dispatched attempt(s) terminal with missing/malformed
        evidence{pending > 0 ? `; ${pending} attempt(s) are still running` : ''}). This is not a
        zero-duration measurement — the measured total cannot be shown.
      </div>
    )
  }

  if (status === 'UnrepresentableTotal') {
    return (
      <div className="dc-agent-process-duration-summary-banner" role="alert">
        {valid} of {dispatched} dispatched Agent attempts carry valid host-measured
        process-duration evidence, but their exact sum is too large to represent
        {pending > 0 ? `, and ${pending} attempt(s) are still running` : ''}. Every individual
        measurement is valid — only the total cannot be shown.
      </div>
    )
  }

  if (status === 'PartialEvidence') {
    return (
      <div className="dc-agent-process-duration-summary-banner" role="alert">
        Partial host-measured process-duration evidence: {valid} of {dispatched} dispatched Agent
        attempts carry valid evidence, {malformed} terminal attempt(s) have missing or malformed
        evidence{pending > 0 ? `, and ${pending} attempt(s) are still running` : ''}. The measured
        total is not shown because it would understate this run's real usage.
      </div>
    )
  }

  if (status === 'PendingEvidence') {
    return (
      <div className="dc-agent-process-duration-summary-banner">
        {pending} of {dispatched} dispatched Agent attempts are still running; the remaining{' '}
        {valid} have valid host-measured process-duration evidence so far. The measured total will
        be shown once every dispatched attempt reaches a terminal result.
      </div>
    )
  }

  if (status === 'Complete' && totalMeasuredMilliseconds != null) {
    return (
      <div className="dc-agent-process-duration-summary-banner">
        Measured Agent process duration: {formatDuration(totalMeasuredMilliseconds)} across{' '}
        {dispatched} dispatched attempt(s). Pure telemetry — not a budget or a limit.
      </div>
    )
  }

  return null
}
