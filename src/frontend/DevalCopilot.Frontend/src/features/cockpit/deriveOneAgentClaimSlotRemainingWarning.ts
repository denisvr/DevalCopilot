import type { GetRunCockpitResponse } from '../../api/clients'

/**
 * Whether the cockpit should show a one-slot-remaining warning for the run-wide, count-based
 * Agent claim budget ([ADR-0012]) — read-only presentation, never a grant or veto of any claim.
 * `AgentClaimBudgetBanner` already speaks once the budget is fully exhausted; this is the earlier,
 * additive warning shown exactly one claim before that point, so the owner has a chance to review
 * the evidence gathered so far before spending the last claim.
 *
 * Mirrors `deriveGlobalAgentClaimBlock`'s own fail-closed shape: a missing, stale (still
 * describing a previously selected run), or incoherent projection never shows the warning. The
 * two budget fields are read directly from the cockpit response's own nullable properties —
 * `RunCockpitView` separately coalesces them with `?? 0`/`?? false` only for
 * `AgentClaimBudgetBanner`'s own always-rendering props; that coalesced presentation value is
 * never trusted as evidence here; a genuinely missing value must read as "unknown", never as a
 * false zero/false that could accidentally satisfy this warning's own arithmetic.
 *
 * `agentBudgetExhausted` must be the literal boolean `false` (read from the original nullable
 * field, not a fallback default) — a run already known to be exhausted, or one whose exhausted
 * flag is itself unknown, never shows this "one left" warning.
 */
export function deriveOneAgentClaimSlotRemainingWarning(
  cockpit: GetRunCockpitResponse | null,
  selectedRunId: string,
): boolean {
  if (!cockpit || cockpit.runId !== selectedRunId) {
    return false
  }

  const { maximumAgentAttempts, agentAttemptsUsed, agentBudgetExhausted } = cockpit
  if (maximumAgentAttempts == null || agentAttemptsUsed == null || agentBudgetExhausted == null) {
    return false
  }

  if (agentBudgetExhausted !== false) {
    return false
  }

  // Coherence, exactly like `deriveGlobalAgentClaimBlock`: `Run.MaximumAgentAttempts` is always a
  // fixed positive ceiling (including any historically raised value — this reads the same
  // already-current persisted field, never a hardcoded default), and `agentAttemptsUsed` is a
  // plain non-negative count. A `NaN`, `Infinity`, fractional, non-positive, or negative value
  // crossing the API boundary as malformed JSON can defeat the TypeScript type alone, so both are
  // checked again here at runtime rather than trusted from the declared type.
  if (
    !Number.isInteger(maximumAgentAttempts)
    || !Number.isInteger(agentAttemptsUsed)
    || maximumAgentAttempts <= 0
    || agentAttemptsUsed < 0
  ) {
    return false
  }

  // Never shown for an already-exhausted or over-budget count, even if `agentBudgetExhausted`
  // itself were somehow (incoherently) reported false — exact equality is the only "one left"
  // state; anything else (including `agentAttemptsUsed >= maximumAgentAttempts`) is out of scope
  // for this specific warning.
  return agentAttemptsUsed === maximumAgentAttempts - 1
}
