# Planner/reviewer handoff

This is the short decision checkpoint for a new Codex planner/reviewer chat,
not a delivery ledger or an execution authorization. The
[shared handoff](current-work.md) records delivered facts;
[the roadmap](mvp-delivery-plan.md) records planned outcomes; accepted
[ADRs](../decisions/README.md) own architectural decisions. Verify this page
against Git and code before relying on it. Historical decision detail remains
in Git rather than accumulating here.

## Current decision (2026-09-26)

- The owner assigns Codex planning, architecture, review, implementation
  prompts, and final acceptance; Claude is the bounded executor. The owner
  permits Codex to plan later slices without a separate planning request, but
  each executor slice still needs distinct owner authorization. See
  [AGENTS.md](../../AGENTS.md).
- **GO:** recorded collaboration-input provenance delivered in `b812263`,
  corrected in `a546318` and `679071b`. Codex independently inspected the
  diffs and ran focused final-state Application (35/35) and API (4/4) tests.
  The executor's broader matrix and its frontend checks are reported in
  [current-work.md](current-work.md), not independently rerun by Codex.
  The two earlier NO-GO findings (non-Planner zero-input honesty; real
  11-input overflow) and the nullable role/provider fail-closed guard are
  resolved. The review trail is recoverable in commits `8195d2a` and
  `057b488`. This GO accepts only the bounded, read-only evidence slice; it
  does not close Increment 4's provider-session or account-usage criteria.
- **Approved for Claude execution:** the owner authorized the bounded
  one-Agent-claim-slot warning below with "ok próximo" on 2026-09-26. The
  pre-approval code/documentation base was `018156c`. This approval covers
  only that frontend slice; Codex retains review and final acceptance.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new owner-approved architecture decision.

## Approved execution slice: one Agent claim slot remaining

- Objective: warn in the cockpit when the existing durable run-wide Agent
  claim count has exactly one slot left. The current
  `AgentClaimBudgetBanner` speaks only after exhaustion; the cockpit already
  receives `maximumAgentAttempts` and `agentAttemptsUsed`. The warning gives
  the owner a chance to review evidence before spending the last claim.
  It advances Increment 4's bounded-loop human awareness but does not itself
  create a durable escalation or complete the remaining budget criteria.
- Boundaries: frontend-only, read-only presentation using those existing
  fields. Keep the exhausted banner, claim-button vetoes, time-fit signals,
  server-side enforcement, and review-correction-specific budget unchanged.
  No API, persistence, adapter, threshold, authorization override, or
  provider-account usage work. Do not imply the final claim is eligible.
- Safety gates: show the warning only for coherent finite integer values with
  positive maximum, nonnegative used count, and `used == maximum - 1`; do not
  show it for exhausted, over-budget, unknown, or invalid values. Use the
  actual persisted maximum (including historical raised maxima), never a
  hard-coded 16. The copy must say that other controls may still block the
  next attempt; it cannot grant or veto an action. `RunCockpitView` currently
  coalesces missing budget fields to zero/false for the existing exhausted
  banner. The new warning must inspect the original nullable fields and
  require `agentBudgetExhausted === false`, not treat those fallback values
  as trusted evidence.
- Acceptance evidence: focused frontend tests for 15/16, historical 18/19,
  16/16 exhaustion, malformed/inconsistent values, and no change to existing
  claim gating or exhausted copy; frontend typecheck, lint, production build,
  and `git diff --check`. If backend/API files remain untouched, do not claim
  to have rerun their suites. Update `current-work.md` in the substantive
  delivery commit; Codex reviews GO/NO-GO separately.
- Executor prompt (send with the exact post-approval HEAD in its preflight
  header): "Implement the approved frontend-only one-Agent-claim-
  slot-remaining warning in the existing cockpit budget presentation. Follow
  the objective, exclusions, safety gates, and acceptance evidence in
  `docs/roadmap/planner-handoff.md`. Verify the exact expected branch, HEAD,
  and staged/unstaged/untracked state in this prompt before editing; report a
  discrepancy. Do not change claim eligibility or backend contracts. Update
  `docs/roadmap/current-work.md` in the delivery commit and report checks;
  Codex owns review and the next decision."

## Decision and preflight protocol

1. At a new chat or slice, read [AGENTS.md](../../AGENTS.md), the
   [shared handoff](current-work.md), and this page; compare branch, HEAD,
   local `origin/main`, and staged/unstaged/untracked changes. Investigate
   discrepancies; never reset or discard work to match prose. Within an
   uninterrupted review/correction round, reuse already-read instructions.
2. On delivery, inspect the actual diff, relevant code/tests, roadmap, and
   accepted ADRs. State GO or actionable NO-GO and distinguish independent
   checks from executor-reported ones. If NO-GO, keep the current slice open
   for a bounded correction; do not start a different one. After GO, prepare
   the next small, bounded **unapproved** proposal in the same review pass
   when a safe candidate is identifiable. This planning needs no fresh owner
   permission; execution still does.
3. A proposal records objective, exclusions, safety/stop gates, acceptance
   evidence, and a short English executor prompt or a link to its full spec.
   When the owner approves execution, record the decision, commit it, then
   put the **exact post-approval HEAD SHA**, branch, and expected clean/dirty
   staged/unstaged/untracked state in the prompt sent to Claude. The executor
   verifies these once before editing and stops on a material discrepancy.
   The approval commit cannot contain its own SHA; the subsequent prompt can.
   A GO or an unapproved proposal never approves the next executor slice.
4. The executor updates `current-work.md` with delivery facts in the
   substantive commit. The planner updates this decision checkpoint on
   approval or review decisions. Keep both entry points short, with one
   current next action; older details belong in Git and linked contracts.

For a new Codex chat: "Resume as DevalCopilot's planner/reviewer. Follow
AGENTS.md, verify Git against current-work.md, read planner-handoff.md, and
address my request from code and accepted ADRs. Do not treat an executor
report as architecture approval or authority to start another slice."
