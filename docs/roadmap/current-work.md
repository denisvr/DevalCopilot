# Current work and cross-chat handoff

This is the short delivery checkpoint, not a transcript or an execution
approval. [The roadmap](mvp-delivery-plan.md),
[engineering context](../engineering-context.md),
[architecture](../architecture/README.md), and
[accepted ADRs](../decisions/README.md) retain their own authority. Git and
code prevail over a stale summary here. At a new chat or slice, compare branch,
`HEAD`, the local `origin/main` tracking ref, and all staged, unstaged, and
untracked changes before editing; do not reset work to match this page.

## Current checkpoint (2026-09-26)

- Latest accepted delivery: the bounded, frontend-only one-Agent-claim-slot-
  remaining warning in `e79f03e891bb3a5a2f0fea06f670432f5fedcb02`,
  parent `b3cc8ec856b30b4385daa1e14a829aa97d899e2c` (the planner's
  approval commit). Branch `main`, checkout clean at delivery and Codex
  review; Codex recorded **GO** in [planner-handoff.md](planner-handoff.md).
- Delivered behavior: a new, additive `OneAgentClaimSlotRemainingWarning`
  (backed by the pure `deriveOneAgentClaimSlotRemainingWarning`) shows exactly
  one claimed Agent attempt before the existing run-wide count budget
  ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md))
  is exhausted, so the owner can review evidence before the last claim.
  `AgentClaimBudgetBanner`'s own exhausted-state copy, claim-button vetoes,
  time-fit signals, and server-side enforcement are all unchanged — this
  warning is shown alongside, never in place of, the existing banner. It reads
  `maximumAgentAttempts` (the actual persisted ceiling, including any
  historically raised maximum) and `agentAttemptsUsed` directly from the
  cockpit projection's own nullable fields — never through the `?? 0`/
  `?? false` fallbacks `RunCockpitView` applies only for
  `AgentClaimBudgetBanner`'s own always-rendering props — and requires
  `agentBudgetExhausted === false` (the literal boolean, not a coalesced
  default) plus `agentAttemptsUsed === maximumAgentAttempts - 1` on coherent,
  finite, non-negative integers for the currently selected run; it shows
  nothing for an exhausted, over-budget, unknown, malformed, or stale-run
  projection. Its own copy states that other controls may still block the
  next attempt and never implies eligibility. See the new "One Agent claim
  slot remaining warning" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
- Checks actually run: frontend 605/605 tests (+27 new: derivation coherence/
  edge cases, component rendering, and `RunCockpitView` wiring proving both
  banners coexist and the exhausted banner's own copy is untouched), `tsc -b`
  clean, `oxlint` exited 0 with the same 20 pre-existing warnings (0 new),
  `vite build` production build passed, `git diff --check` reported no
  whitespace errors. No backend/API file was touched by this slice, so its
  suites were not rerun; NSwag/migrations are consequently unaffected.
- No API, persistence, adapter, threshold, authorization override, claim
  eligibility, or provider-account-usage change of any kind. The
  provider-allowance evidence slice remains closed without implementation:
  neither CLI offered a proven safe account-allowance observation contract.
  Do not revive it as `Unknown`-only scaffolding or direct authenticated API
  access without a new approved decision.
- Next action: Codex investigates provider-supported runtime-control
  contracts against the remaining Increment 4 roadmap before proposing
  another bounded slice. No next execution slice is approved by this GO.

## Open risks

- Provider account-usage thresholds and token budgets remain unenforced;
  provider-reported per-attempt token usage is not account allowance or cost.
  The provider-session-resume, runtime-control, and context-window/compaction
  roadmap items also remain open. See [Increment 4](mvp-delivery-plan.md).
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  enforce claim-count and reserved-time ceilings, not actual provider wall
  time, token, or account allowance; neither has a human override.
- Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
  Raw linked artifact inspection beyond the bounded collaboration-evidence
  drill-down is not implemented; see [the cockpit specification](../product/run-cockpit-specification.md).

## History and closure rule

Older delivery details remain in Git (for example `git show
679071b:docs/roadmap/current-work.md`) and in the linked product contracts;
they are intentionally not copied into this entry point. The previous
provider-separated token-usage delivery is `e3805a8` with correction
`281b3e1`. Every future substantive slice commit updates this page with its
resolvable delivery identity, remaining uncommitted work, checks actually run,
open risks, and next action. The executor reports delivery facts; only the
planner/reviewer records acceptance and future slice decisions.
