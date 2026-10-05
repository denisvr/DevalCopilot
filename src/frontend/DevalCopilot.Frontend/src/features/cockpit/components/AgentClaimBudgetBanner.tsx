interface AgentClaimBudgetBannerProps {
  maximumAgentAttempts: number | null | undefined
  agentAttemptsUsed: number | null | undefined
  agentBudgetExhausted: boolean | null | undefined
}

function isSafeCount(value: number | null | undefined, minimum: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum
}

/**
 * The run-wide Agent claim budget — distinct from, and never combined with, the
 * review-correction-specific budget shown by ReviewCorrectionAction. It trusts the projection only
 * when it is coherent: a safe positive integer ceiling, a safe non-negative integer usage, and a
 * boolean exhaustion flag that agrees with `used >= maximum`. Then it shows the run's actual
 * immutable ceiling and usage: a quiet fact before exhaustion (the ceiling may be one the owner
 * chose, or a historical one above the intake range) or the existing alert once exhausted (usage
 * above the ceiling is still coherent). Anything else shows a distinct unknown or invalid note with
 * no count and no reached-limit assertion. A shown count never means a particular claim is available.
 */
export function AgentClaimBudgetBanner({
  maximumAgentAttempts,
  agentAttemptsUsed,
  agentBudgetExhausted,
}: AgentClaimBudgetBannerProps) {
  if (
    isSafeCount(maximumAgentAttempts, 1) &&
    isSafeCount(agentAttemptsUsed, 0) &&
    typeof agentBudgetExhausted === 'boolean' &&
    agentBudgetExhausted === agentAttemptsUsed >= maximumAgentAttempts
  ) {
    if (agentBudgetExhausted) {
      return (
        <div className="dc-agent-claim-budget-banner" role="alert">
          This run has reached its maximum of {maximumAgentAttempts} claimed Agent attempts ({agentAttemptsUsed}/
          {maximumAgentAttempts} used). No further Agent attempt can be claimed for this run — review the evidence gathered
          so far. Exhausting the budget does not finish this run or authorize replacing it; a new objective can be
          recorded only after every run of this project has finished.
        </div>
      )
    }

    return (
      <div className="dc-agent-claim-budget-fact">
        Agent claims: {agentAttemptsUsed} of {maximumAgentAttempts} used. The ceiling was fixed when the run was recorded
        and cannot be changed; a claim stays consumed after a failure or interruption.
      </div>
    )
  }

  return (
    <div className="dc-agent-claim-budget-fact">
      The Agent claim ceiling or usage is unknown or invalid, so no count is shown. This does not prove any claim is
      available.
    </div>
  )
}
