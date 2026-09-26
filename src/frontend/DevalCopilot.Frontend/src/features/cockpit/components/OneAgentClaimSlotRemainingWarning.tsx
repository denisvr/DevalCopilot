import type { GetRunCockpitResponse } from '../../../api/clients'
import { deriveOneAgentClaimSlotRemainingWarning } from '../deriveOneAgentClaimSlotRemainingWarning'

interface OneAgentClaimSlotRemainingWarningProps {
  cockpit: GetRunCockpitResponse | null
  selectedRunId: string
}

/**
 * An additive, read-only warning shown exactly one claim before the run-wide, count-based Agent
 * claim budget ([ADR-0012]) is exhausted — never in place of, and never changing the copy or
 * behavior of, `AgentClaimBudgetBanner`'s own exhausted-state message. Purely informational: it
 * never grants or vetoes a claim, and its own copy says so explicitly, since a different role's
 * own eligibility, the time-fit signal, or a global claim block may still prevent the next Agent
 * attempt even though a budget slot is available.
 *
 * Delegates every coherence and staleness check to `deriveOneAgentClaimSlotRemainingWarning`; once
 * that derivation is true, `cockpit.maximumAgentAttempts`/`cockpit.agentAttemptsUsed` are proven
 * defined, finite, non-negative integers for the currently selected run, so reading them directly
 * here (never through a `?? 0` fallback) is safe.
 */
export function OneAgentClaimSlotRemainingWarning({ cockpit, selectedRunId }: OneAgentClaimSlotRemainingWarningProps) {
  if (!deriveOneAgentClaimSlotRemainingWarning(cockpit, selectedRunId) || !cockpit) {
    return null
  }

  const { maximumAgentAttempts, agentAttemptsUsed } = cockpit
  if (maximumAgentAttempts == null || agentAttemptsUsed == null) {
    return null
  }

  return (
    <div className="dc-one-agent-claim-slot-remaining-warning" role="status">
      Only one Agent claim slot remains for this run ({agentAttemptsUsed}/{maximumAgentAttempts} used).
      Review the evidence gathered so far before claiming the next attempt — other controls may still
      block that attempt even though a budget slot is available.
    </div>
  )
}
