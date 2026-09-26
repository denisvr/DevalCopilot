import type { GetRunCockpitResponse } from '../../api/clients'

/**
 * The six currently supported Agent-claiming operations, matching the backend's
 * `AgentClaimPath` (Application layer) exactly. Kept as a closed union so a caller can never ask
 * for a fit result the backend does not know how to compute.
 */
export type AgentClaimPath =
  | 'CodexPlanning'
  | 'ClaudeCriticalReview'
  | 'ChallengeResolution'
  | 'Implementation'
  | 'CodeReview'
  | 'ReviewCorrection'

/**
 * The closed, honest reason describing one specific claim path's own candidate-fit result — a
 * SEPARATE, ADDITIVE signal from `GlobalAgentClaimBlockReason` (`deriveGlobalAgentClaimBlock`).
 * Never a positive permission: `Fits` and `LegacyUnknown` mean only "not a known reason to
 * withhold this control on time-fit grounds", never that the server has authorized or will accept
 * the claim — the server remains the sole authority for every claim.
 */
export type AgentClaimPathTimeFitReason =
  /** This claim path's own configured invocation timeout is less than or equal to the run's
   * currently remaining reserved invocation time. "Fits with zero slack" still fits. */
  | 'Fits'
  /** This claim path's own configured invocation timeout exceeds the run's currently remaining
   * reserved invocation time. */
  | 'DoesNotFit'
  /** This run predates the invocation-time budget policy entirely — truthfully not a fit or
   * no-fit answer, never treated as a block on time grounds. */
  | 'LegacyUnknown'
  /** This run has a real time-budget policy, but its prior Agent invocation-time evidence is
   * missing or malformed, so this claim path's own fit cannot be confirmed. */
  | 'EvidenceInvalid'
  /** The per-claim-path projection for the currently selected run is missing, duplicated,
   * unknown-shaped, internally contradictory (including contradicting the run-wide budget's own
   * legacy/evidence-invalid state), or still describes a previously selected run — never trusted
   * as proof this claim would fit. Fails closed, exactly like `BudgetProjectionUnavailable` in
   * `deriveGlobalAgentClaimBlock`. */
  | 'ProjectionUnavailable'

export interface AgentClaimPathTimeFit {
  reason: AgentClaimPathTimeFitReason
}

const KNOWN_FIT_REASONS: ReadonlySet<string> = new Set(['Fits', 'DoesNotFit', 'LegacyUnknown', 'EvidenceInvalid'])

const KNOWN_CLAIM_PATHS: ReadonlySet<string> = new Set<AgentClaimPath>([
  'CodexPlanning',
  'ClaudeCriticalReview',
  'ChallengeResolution',
  'Implementation',
  'CodeReview',
  'ReviewCorrection',
])

/**
 * Derives the candidate-specific advisory time-fit result for one claim path of the CURRENTLY
 * SELECTED run, from the per-claim-path projection the cockpit response already carries
 * (`agentClaimPathTimeFits`). This is genuinely additive to, and never a replacement for,
 * `deriveGlobalAgentClaimBlock`: the global derivation already knows about count-budget
 * exhaustion and a zero/negative/invalid run-wide time remainder, but it deliberately never
 * references any one role's own configured timeout — this derivation is the only place that
 * does, for exactly the claim path asked for.
 *
 * Mirrors `deriveGlobalAgentClaimBlock`'s exact run-switch guard: a projection that does not
 * belong to `selectedRunId` is never read as this run's own fit state, even for one render frame.
 *
 * This derivation only ever TRUSTS the backend's own closed `Fits`/`DoesNotFit`/`LegacyUnknown`/
 * `EvidenceInvalid` state value at face value once it has proven the received shape is coherent —
 * it never re-derives or double-checks the fit conclusion itself with client-side millisecond or
 * tick arithmetic (the backend remains the sole authority for the fit comparison). Coherence
 * requires, in order:
 *  1. The projection belongs to the currently selected run.
 *  2. Every entry's own claim path is one of the six known values — an unrecognized claim-path
 *     value anywhere in the list makes the whole projection untrustworthy.
 *  3. Each of the six known claim paths appears in the list EXACTLY once — a missing path, or a
 *     duplicated path (whether or not the duplicates agree), makes the whole projection
 *     untrustworthy rather than silently picking one of two possibly-contradictory answers.
 *  4. EVERY ONE of the six entries' own fit value is one of the four known reasons -- not only the
 *     requested claim path's entry. An unrecognized fit value on an entry the caller did not even
 *     ask about still makes the whole projection untrustworthy, including for the requested path.
 *  5. EVERY ONE of the six entries' fit value is consistent with the run-wide invocation-time
 *     budget's own `isLegacyUnknown`/`evidenceInvalid` state -- both are derived from the same
 *     underlying budget data, so any claim-path entry reporting `Fits`/`DoesNotFit` while the
 *     run-wide summary reports `isLegacyUnknown` or `evidenceInvalid` (or the reverse) is
 *     incoherent and fails the WHOLE projection closed instead of trusting either side -- even
 *     when the incoherent entry belongs to a claim path other than the one requested. A
 *     projection that can be wrong for one path cannot be trusted for any path, including one
 *     that happens to look fine in isolation.
 *
 * This is strictly additive to the prior round's checks: nothing above loosens or replaces the
 * run-switch, unknown-claim-path, or missing/duplicate-entry checks -- it only widens the
 * fit-value and budget-coherence checks (points 4 and 5) from "the requested entry only" to "all
 * six entries". No tick- or millisecond-level arithmetic is reconstructed anywhere here: only the
 * closed `fit` strings and the budget's own boolean flags are compared.
 */
export function deriveAgentClaimPathTimeFit(
  cockpit: GetRunCockpitResponse | null,
  selectedRunId: string,
  claimPath: AgentClaimPath,
): AgentClaimPathTimeFit {
  if (!cockpit || cockpit.runId !== selectedRunId) {
    return { reason: 'ProjectionUnavailable' }
  }

  const entries = cockpit.agentClaimPathTimeFits
  if (!entries || !Array.isArray(entries)) {
    return { reason: 'ProjectionUnavailable' }
  }

  // Any entry whose own claim path is not one of the six known values means the projection is not
  // the closed shape this backend can produce — untrustworthy as a whole, not just for the
  // unrecognized entry itself.
  if (entries.some((entry) => typeof entry?.claimPath !== 'string' || !KNOWN_CLAIM_PATHS.has(entry.claimPath))) {
    return { reason: 'ProjectionUnavailable' }
  }

  // Exactly one entry per claim path is the only shape this backend can produce
  // (`RunCockpitAgentClaimPathTimeFitProjection.Compute` always emits exactly six, one per member
  // of `AgentClaimPath`). Zero matches (missing) or more than one (duplicated — whether or not the
  // duplicates agree) means the projection cannot be trusted at face value: a contradiction must
  // never resolve to `Fits` by silently picking one of two answers. While checking counts, also
  // collect each path's own single fit value for the coherence pass below.
  const fitByClaimPath = new Map<AgentClaimPath, string>()
  for (const knownPath of KNOWN_CLAIM_PATHS) {
    const matches = entries.filter((entry) => entry.claimPath === knownPath)
    if (matches.length !== 1) {
      return { reason: 'ProjectionUnavailable' }
    }
    fitByClaimPath.set(knownPath as AgentClaimPath, matches[0].fit as string)
  }

  // Cross-check against the run-wide invocation-time budget's own legacy/evidence-invalid state
  // (the same fields `deriveGlobalAgentClaimBlock` already reads). Both signals are derived from
  // the same underlying budget data on the server and must never contradict each other.
  const timeBudget = cockpit.agentInvocationTimeBudget
  if (!timeBudget) {
    return { reason: 'ProjectionUnavailable' }
  }

  // Validate EVERY ONE of the six entries' fit value — not only the requested path's own entry.
  // An unrecognized fit value, or a fit value incoherent with the run-wide budget's own state, on
  // ANY of the six entries makes the WHOLE projection untrustworthy: a projection that can be
  // wrong for one path cannot be trusted for any path, including the requested one, even when the
  // requested path's own entry looks perfectly valid in isolation.
  for (const knownPath of KNOWN_CLAIM_PATHS) {
    const entryFit = fitByClaimPath.get(knownPath as AgentClaimPath)!
    if (typeof entryFit !== 'string' || !KNOWN_FIT_REASONS.has(entryFit)) {
      return { reason: 'ProjectionUnavailable' }
    }

    if (timeBudget.isLegacyUnknown) {
      if (entryFit !== 'LegacyUnknown') {
        return { reason: 'ProjectionUnavailable' }
      }
      continue
    }

    if (entryFit === 'LegacyUnknown') {
      // The run-wide summary says this run is NOT legacy-unknown, so a claim-path entry claiming
      // LegacyUnknown contradicts it.
      return { reason: 'ProjectionUnavailable' }
    }

    if (timeBudget.evidenceInvalid) {
      if (entryFit !== 'EvidenceInvalid') {
        return { reason: 'ProjectionUnavailable' }
      }
      continue
    }

    if (entryFit === 'EvidenceInvalid') {
      // The run-wide summary says the evidence is valid, so a claim-path entry claiming
      // EvidenceInvalid contradicts it.
      return { reason: 'ProjectionUnavailable' }
    }
  }

  // All six entries are individually valid and coherent with the run-wide budget state, so the
  // requested path's own (already-validated) fit value can now be trusted at face value.
  const fit = fitByClaimPath.get(claimPath)!
  return { reason: fit as AgentClaimPathTimeFitReason }
}

/** `true` only for a reason that should withhold the control on time-fit grounds. `Fits` and
 * `LegacyUnknown` never block; every other reason fails closed and does block. */
export function isAgentClaimPathTimeFitBlocking(fit: AgentClaimPathTimeFit): boolean {
  return fit.reason !== 'Fits' && fit.reason !== 'LegacyUnknown'
}

const TIME_FIT_BLOCK_MESSAGE: Partial<Record<AgentClaimPathTimeFitReason, string>> = {
  DoesNotFit: 'Blocked: insufficient reserved invocation time remains for this specific request.',
  EvidenceInvalid:
    "Blocked because this run's prior Agent invocation-time evidence is missing or invalid, so this specific request's time fit cannot be confirmed.",
  ProjectionUnavailable:
    'Blocked until this specific request’s time-fit status is confirmed: current data is not yet available.',
}

/**
 * Plain, attributable copy for a candidate-fit block, explicitly naming THIS SPECIFIC REQUEST as
 * the cause — never conflated with `describeGlobalAgentClaimBlock`'s run-wide count/time-budget
 * copy, and never worded as a role-specific ineligibility. Returns `null` when the fit does not
 * block (`Fits` or `LegacyUnknown`).
 */
export function describeAgentClaimPathTimeFitBlock(fit: AgentClaimPathTimeFit): string | null {
  return TIME_FIT_BLOCK_MESSAGE[fit.reason] ?? null
}
