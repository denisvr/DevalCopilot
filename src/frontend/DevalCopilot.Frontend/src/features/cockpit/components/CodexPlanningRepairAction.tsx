import type { AgentAttemptStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { isAgentClaimPathTimeFitBlocking } from '../deriveAgentClaimPathTimeFit'

interface CodexPlanningRepairActionProps {
  status: AgentAttemptStatusResponse | null
  statusLoading: boolean
  /** True while an ordinary planning request is in flight, so the two actions never overlap. */
  ordinaryRequesting: boolean
  repairRequesting: boolean
  repairError: string | null
  onRequestRepair: (sourceAttemptId: string) => void
  /** A known global Agent-claim hard stop; a repair consumes the same budgets as any claim. */
  globalClaimBlock: GlobalAgentClaimBlock | null
  timeFit: AgentClaimPathTimeFit
}

/**
 * The one manual Codex Planner format repair. The button is only a suggestion, offered for the
 * latest planning attempt when its recorded outcome is `InvalidStructuredOutput` and it is not
 * itself a repair; the server alone decides eligibility and returns a fixed safe message when it
 * refuses. A repair is a fresh planning request with a fixed format reminder — never a correction
 * of the earlier response — and the ordinary plan action stays available beside it. The lineage
 * line reports provenance only, never that a repair fixed or preserved anything.
 */
export function CodexPlanningRepairAction({
  status,
  statusLoading,
  ordinaryRequesting,
  repairRequesting,
  repairError,
  onRequestRepair,
  globalClaimBlock,
  timeFit,
}: CodexPlanningRepairActionProps) {
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
    <section className="dc-codex-planning-action dc-codex-planning-repair" aria-label="Codex plan repair">
      {suggestRepair && attemptId && (
        <>
          <p className="dc-codex-planning-status">
            The last plan response failed structural validation. You can request one fresh plan attempt with a format
            reminder. It uses one Agent attempt and reserved time, and it does not correct or reuse the earlier
            response.
          </p>
          <button
            type="button"
            className="dc-codex-planning-request"
            disabled={repairRequesting || ordinaryRequesting || statusLoading}
            onClick={() => onRequestRepair(attemptId)}
          >
            {repairRequesting ? 'Requesting repair…' : 'Request one format-repair plan'}
          </button>
        </>
      )}
      {lineage && (
        <p className="dc-codex-planning-status">
          {lineage} A repair is a fresh plan and is not repaired again; you can still request an ordinary plan.
        </p>
      )}
      {repairError && (
        <p className="dc-codex-planning-error" role="status">
          {repairError}
        </p>
      )}
    </section>
  )
}
