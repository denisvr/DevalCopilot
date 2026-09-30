import type { ClaudeMutationTurnLimitResponse } from '../../../api/clients'
import { describeClaudeAttemptTurnLimit, describeClaudeRunTurnLimit } from '../describeClaudeTurnLimit'

interface ClaudeTurnLimitFactsProps {
  /** The attempt's immutable claim-time fact; null/undefined for a non-Claude-mutation attempt or no attempt. */
  attemptFact?: ClaudeMutationTurnLimitResponse | null
  /** The run's current saved request, shown for comparison; omitted when not supplied. */
  runRequest?: ClaudeMutationTurnLimitResponse | null
  className?: string
}

/**
 * Presents the Claude turn-limit request facts as requests only: never an enforced resource limit,
 * a measured turn count, or an "unlimited" capacity.
 */
export function ClaudeTurnLimitFacts({ attemptFact, runRequest, className }: ClaudeTurnLimitFactsProps) {
  const attemptText = describeClaudeAttemptTurnLimit(attemptFact)
  if (attemptText === null && !runRequest) {
    return null
  }

  return (
    <>
      {attemptText !== null && (
        <p className={className}>
          Claude turn limit for this attempt: {attemptText}
          {attemptText.startsWith('Requested') ? ' (a request only; the turns used are not measured)' : ''}
        </p>
      )}
      {runRequest && (
        <p className={className}>Current run request (applies to future attempts): {describeClaudeRunTurnLimit(runRequest)}</p>
      )}
    </>
  )
}
