import type { ClaudeCriticalReviewAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { isAgentClaimPathTimeFitBlocking } from '../deriveAgentClaimPathTimeFit'

interface ClaudeCriticalReviewRepairActionProps {
  status: ClaudeCriticalReviewAttemptStatusResponse | null
  statusLoading: boolean
  /** True while an ordinary critical review request is in flight, so the two actions never overlap. */
  ordinaryRequesting: boolean
  repairRequesting: boolean
  repairError: string | null
  onRequestRepair: (sourceAttemptId: string) => void
  /** A known global Agent-claim hard stop; a repair consumes the same budgets as any claim. */
  globalClaimBlock: GlobalAgentClaimBlock | null
  /** The advisory time-fit result for THIS claim path, exactly as the ordinary action receives it. */
  timeFit: AgentClaimPathTimeFit
}

/**
 * The one manual critical review format repair. The button is only a suggestion, offered for the latest
 * critical review attempt when its recorded outcome is `InvalidStructuredOutput` and it is not itself a
 * repair; the server alone decides eligibility and returns a fixed safe message when it refuses.
 * A repair is a fresh review request with a fixed format reminder — never a correction of the
 * earlier response — and the ordinary critical review action stays available beside it. The lineage line
 * reports provenance only, never that a repair fixed or preserved anything.
 */
export function ClaudeCriticalReviewRepairAction({
  status,
  statusLoading,
  ordinaryRequesting,
  repairRequesting,
  repairError,
  onRequestRepair,
  globalClaimBlock,
  timeFit,
}: ClaudeCriticalReviewRepairActionProps) {
  const isActive = status?.status === 'Running'
  const attemptId = status?.attemptId
  const isRepair = Boolean(status?.repairSourceAttemptId)
  const suggestRepair =
    !isActive
    && !isRepair
    && Boolean(attemptId)
    && status?.outcome === 'InvalidStructuredOutput'
    && !globalClaimBlock
    && !isAgentClaimPathTimeFitBlocking(timeFit)
  const lineage =
    isRepair && status
      ? status.repairSourceAttemptNumber
        ? `Attempt #${status.attemptNumber} is the one repair request for attempt #${status.repairSourceAttemptNumber}.`
        : `Attempt #${status.attemptNumber} is a repair request for an earlier attempt.`
      : null

  if (!suggestRepair && !lineage && !repairError) {
    return null
  }

  return (
    <section className="dc-claude-critical-review-action dc-claude-critical-review-repair" aria-label="Claude critical review repair">
      {suggestRepair && attemptId && (
        <>
          <p className="dc-claude-critical-review-status">
            The last critical review response failed structural validation. You can request one fresh review attempt
            with a format reminder. It uses one Agent attempt and reserved time, and it does not correct or reuse the
            earlier response.
          </p>
          <button
            type="button"
            className="dc-claude-critical-review-request"
            disabled={repairRequesting || ordinaryRequesting || statusLoading}
            onClick={() => onRequestRepair(attemptId)}
          >
            {repairRequesting ? 'Requesting repair…' : 'Request one format-repair critical review'}
          </button>
        </>
      )}
      {lineage && (
        <p className="dc-claude-critical-review-status">
          {lineage} A repair is a fresh review and is not repaired again; you can still request an ordinary review.
        </p>
      )}
      {repairError && (
        <p className="dc-claude-critical-review-error" role="status">
          {repairError}
        </p>
      )}
    </section>
  )
}
