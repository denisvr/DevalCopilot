import { describeRunTokenUsage, type RunTokenUsageSummaryView } from '../describeTokenUsage'

interface RunTokenUsageSummaryProps {
  summary: RunTokenUsageSummaryView | null | undefined
}

/**
 * The run-level token-usage aggregate. Its label is driven only by the projection's completeness:
 * a total for `Complete`, an explicitly partial count for `Partial`, and a neutral empty state for
 * `NoDispatchedAttempts`. A partial sum is never rendered under a total label.
 */
export function RunTokenUsageSummary({ summary }: RunTokenUsageSummaryProps) {
  if (!summary) {
    return null
  }

  return (
    <p
      className="dc-run-token-usage"
      aria-label="Run token usage"
      data-completeness={summary.completeness ?? 'Unknown'}
    >
      {describeRunTokenUsage(summary)}
    </p>
  )
}
