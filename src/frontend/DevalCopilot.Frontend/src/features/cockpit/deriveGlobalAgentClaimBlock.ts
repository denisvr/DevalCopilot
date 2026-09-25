import type { GetRunCockpitResponse } from '../../api/clients'

/**
 * The exact, distinct reason a new Agent claim is known to be blocked by a run-wide budget
 * already exposed by the cockpit projection. Never a positive permission: only ever a veto
 * signal the UI already has enough evidence to show, before ever submitting a request the
 * server would certainly reject.
 */
export type GlobalAgentClaimBlockReason =
  /** ADR-0012's run-wide count budget has no slots left. */
  | 'CountBudgetExhausted'
  /** ADR-0013's reserved invocation-time budget has zero or negative time remaining. */
  | 'TimeBudgetExhausted'
  /** ADR-0013's own prior Agent-attempt evidence is missing or malformed for this run. */
  | 'TimeBudgetEvidenceInvalid'
  /** The budget projection for the currently selected run is missing, incoherent, or still
   * belongs to a previously selected run — never trusted as proof a claim would be allowed. */
  | 'BudgetProjectionUnavailable'

export interface GlobalAgentClaimBlock {
  reason: GlobalAgentClaimBlockReason
}

/**
 * Derives the known, already-visible global reason a new Agent claim is blocked for the
 * CURRENTLY SELECTED run, from the two durable, run-wide budgets the cockpit projection already
 * exposes: ADR-0012's count-based Agent claim budget and ADR-0013's Agent invocation-time
 * budget. This is a pure, additive UI-honesty check, never an authorization decision — the
 * backend remains the sole authority for every claim, and this derivation's job is only to stop
 * the UI from visibly offering an action that is already known, from data the cockpit itself
 * loaded, to be certainly rejected.
 *
 * Returning `null` means only "no known global hard stop was found here." It is NOT the same as
 * "this claim is allowed": the server may still reject it for a role-specific reason this
 * derivation does not know about, and a stale UI snapshot may still race with a real claim.
 *
 * A legitimate legacy run with no time-budget policy at all (`isLegacyUnknown`) is never treated
 * as a block on time grounds — that state means the time budget simply does not apply, not that
 * zero time remains. A positive time remainder is never read as proof that any specific role's
 * own configured timeout would fit within it: this derivation deliberately never references or
 * hardcodes a per-role timeout duration, and only catches the known zero/negative/invalid cases
 * above.
 *
 * A missing, incoherent, or stale projection — including one still describing a previously
 * selected run — is treated as `BudgetProjectionUnavailable` rather than a permissive default:
 * the UI must never offer a new claim based on budget data it cannot prove is current and
 * coherent for the run actually selected right now.
 */
export function deriveGlobalAgentClaimBlock(
  cockpit: GetRunCockpitResponse | null,
  selectedRunId: string,
): GlobalAgentClaimBlock | null {
  if (!cockpit || cockpit.runId !== selectedRunId) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  const { maximumAgentAttempts, agentAttemptsUsed, agentBudgetExhausted } = cockpit
  if (maximumAgentAttempts == null || agentAttemptsUsed == null || agentBudgetExhausted == null) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  // ADR-0012's count budget: `Run.MaximumAgentAttempts` is always a fixed positive ceiling
  // (16 by default, assigned once at `Run.RecordIntent`), and `agentAttemptsUsed` is a plain
  // `CountAsync` result, which can never be negative. Either violation means the projection
  // itself cannot be trusted at face value, so it fails closed rather than reading a corrupted
  // "used" count as headroom. Both are also always genuine integers server-side; a `NaN`,
  // `Infinity`, or fractional value crossing the API boundary as malformed JSON can defeat the
  // type system, so this is checked at runtime too rather than trusted from the TypeScript type
  // alone.
  if (
    maximumAgentAttempts <= 0
    || agentAttemptsUsed < 0
    || !Number.isInteger(maximumAgentAttempts)
    || !Number.isInteger(agentAttemptsUsed)
  ) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  if (agentBudgetExhausted || agentAttemptsUsed >= maximumAgentAttempts) {
    return { reason: 'CountBudgetExhausted' }
  }

  const timeBudget = cockpit.agentInvocationTimeBudget
  if (!timeBudget) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  if (timeBudget.evidenceInvalid) {
    return { reason: 'TimeBudgetEvidenceInvalid' }
  }

  const { maximumMilliseconds, reservedMilliseconds, remainingMilliseconds } = timeBudget

  if (timeBudget.isLegacyUnknown) {
    // `RunCockpitAgentInvocationTimeBudgetSummary.LegacyUnknown()` (Application layer) always
    // constructs `new(null, null, null, IsLegacyUnknown: true, EvidenceInvalid: false)` — all
    // three time fields absent, never populated alongside a `true` legacy flag. A shape that
    // claims `isLegacyUnknown: true` while also carrying a populated time field does not match
    // any real projection this backend can produce, so it cannot be trusted as "no time-policy
    // block" — it fails closed instead of being read as a legitimate legacy run.
    if (maximumMilliseconds != null || reservedMilliseconds != null || remainingMilliseconds != null) {
      return { reason: 'BudgetProjectionUnavailable' }
    }

    return null
  }

  if (maximumMilliseconds == null || reservedMilliseconds == null || remainingMilliseconds == null) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  // ADR-0013's time budget: `RunCockpitAgentInvocationTimeBudgetSummary.Budgeted` is the only
  // non-legacy, evidence-valid factory, and it always sets `Reserved` to a sum of permanently
  // reserved (always non-negative) invocation timeouts, `Maximum` to a fixed positive ceiling
  // (120 minutes by default), and `Remaining` to exactly `Maximum - Reserved` at the `TimeSpan`
  // (tick) level. However, `AgentInvocationTimeBudgetResponse.FromDomain` (Api layer) converts
  // each of `Maximum`, `Reserved`, and `Remaining` to `long` milliseconds INDEPENDENTLY, via a
  // separate truncating `(long)TotalMilliseconds` cast per field — it never computes
  // `RemainingMilliseconds` as `MaximumMilliseconds - ReservedMilliseconds` on the wire. Because
  // truncation is not linear, this can produce a bounded, provable 1ms discrepancy: writing each
  // TimeSpan's tick count as `10000 * whole + remainder` (remainder in [0, 9999]), the
  // independently-truncated `RemainingMilliseconds` is always EITHER exactly
  // `MaximumMilliseconds - ReservedMilliseconds`, OR exactly one less — never more, and never in
  // the other direction, since `Maximum >= Reserved >= 0` always holds for a genuine budgeted
  // projection. Asserting exact equality here would be a precision overclaim the wire contract
  // does not make; the coherence check below allows exactly that proven one-tick-boundary
  // discrepancy and nothing wider.
  const exactRemainder = maximumMilliseconds - reservedMilliseconds
  if (
    maximumMilliseconds <= 0
    || reservedMilliseconds < 0
    || (remainingMilliseconds !== exactRemainder && remainingMilliseconds !== exactRemainder - 1)
  ) {
    return { reason: 'BudgetProjectionUnavailable' }
  }

  if (remainingMilliseconds <= 0) {
    return { reason: 'TimeBudgetExhausted' }
  }

  // A positive remainder only means "not a known zero/negative block" — never proof that any
  // one specific role's own configured timeout would fit within it.
  return null
}

const BLOCK_MESSAGE: Record<GlobalAgentClaimBlockReason, string> = {
  CountBudgetExhausted: 'Blocked by the run-wide Agent claim budget: no further Agent attempt can be claimed for this run.',
  TimeBudgetExhausted:
    'Blocked by the run-wide Agent invocation-time budget: no reserved invocation time remains for this run.',
  TimeBudgetEvidenceInvalid:
    "Blocked because this run's prior Agent invocation-time evidence is missing or invalid.",
  BudgetProjectionUnavailable:
    'Blocked until this run’s budget status is confirmed: current budget data for this run is not yet available.',
}

/**
 * Plain, attributable copy for a known global block — always names the global run-wide budget
 * as the cause, never a role's own local state, so a control's explanation never implies the
 * block is that role's own ineligibility.
 */
export function describeGlobalAgentClaimBlock(block: GlobalAgentClaimBlock): string {
  return BLOCK_MESSAGE[block.reason]
}
