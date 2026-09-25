interface AgentInvocationTimeBudgetBannerProps {
  maximumMilliseconds: number | null | undefined
  reservedMilliseconds: number | null | undefined
  remainingMilliseconds: number | null | undefined
  isLegacyUnknown: boolean | null | undefined
  evidenceInvalid: boolean | null | undefined
}

function formatMinutes(milliseconds: number): string {
  const minutes = milliseconds / 60000
  const rounded = Math.round(minutes * 10) / 10
  return `${rounded} min`
}

/**
 * The run-wide Agent invocation-TIME budget — independent of, and never combined with, the
 * count-based AgentClaimBudgetBanner above it. A legacy run (one that predates this budget) never
 * shows a fabricated "0 minutes remaining"; it shows a distinct, explicit "not tracked" state
 * instead. Reserved time is never described as elapsed time: it is only what claimed Agent
 * attempts have permanently committed to reserving.
 */
export function AgentInvocationTimeBudgetBanner({
  maximumMilliseconds,
  reservedMilliseconds,
  remainingMilliseconds,
  isLegacyUnknown,
  evidenceInvalid,
}: AgentInvocationTimeBudgetBannerProps) {
  if (isLegacyUnknown) {
    return (
      <div className="dc-agent-invocation-time-budget-banner dc-agent-invocation-time-budget-banner--legacy">
        Legacy run — time budget not tracked. This run was created before the Agent invocation-time
        budget existed and carries no reservation ceiling.
      </div>
    )
  }

  if (evidenceInvalid) {
    return (
      <div className="dc-agent-invocation-time-budget-banner" role="alert">
        This run's prior Agent invocation-time evidence is missing or invalid, so its reserved
        invocation time cannot be safely shown.
      </div>
    )
  }

  if (maximumMilliseconds == null || reservedMilliseconds == null || remainingMilliseconds == null) {
    return null
  }

  const exhausted = remainingMilliseconds <= 0

  return (
    <div className="dc-agent-invocation-time-budget-banner" role={exhausted ? 'alert' : undefined}>
      {exhausted ? (
        <>
          This run has reached its maximum reserved Agent invocation time of{' '}
          {formatMinutes(maximumMilliseconds)} ({formatMinutes(reservedMilliseconds)} already
          reserved). No further Agent attempt whose configured timeout would exceed the remaining
          time can be claimed.
        </>
      ) : (
        <>
          Reserved Agent invocation time: {formatMinutes(reservedMilliseconds)} of{' '}
          {formatMinutes(maximumMilliseconds)} ({formatMinutes(remainingMilliseconds)} remaining).
        </>
      )}
    </div>
  )
}
