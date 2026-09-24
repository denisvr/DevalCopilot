interface AgentClaimBudgetBannerProps {
  maximumAgentAttempts: number
  agentAttemptsUsed: number
  agentBudgetExhausted: boolean
}

/**
 * The run-wide Agent claim budget — distinct from, and never combined with, the
 * review-correction-specific budget shown by ReviewCorrectionAction. Renders nothing until the
 * budget is exhausted: a quiet used/maximum count would be noise on every other run.
 */
export function AgentClaimBudgetBanner({
  maximumAgentAttempts,
  agentAttemptsUsed,
  agentBudgetExhausted,
}: AgentClaimBudgetBannerProps) {
  if (!agentBudgetExhausted) {
    return null
  }

  return (
    <div className="dc-agent-claim-budget-banner" role="alert">
      This run has reached its maximum of {maximumAgentAttempts} claimed Agent attempts (
      {agentAttemptsUsed}/{maximumAgentAttempts} used). No further Agent attempt can be claimed for
      this run — review the evidence gathered so far, or start a new run to continue.
    </div>
  )
}
