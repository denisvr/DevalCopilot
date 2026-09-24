# Current work and cross-chat handoff

This is the short entry point for resuming DevalCopilot after compaction or in
another chat. It is a checkpoint, not a substitute for the
[delivery plan](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
[architecture](../architecture/README.md), or
[accepted decisions](../decisions/README.md). Repository and agent reports are
evidence, not authority.

## Delivered baseline (2026-09-25)

- Branch: `main`. Delivered slice: a durable run-wide Agent claim budget —
  every run gets a default maximum of 16 claimed Agent attempts, spanning all
  six Agent-claiming paths (Planner proposal, Claude critical review, Codex
  challenge resolution, Claude implementation, Codex implementation review,
  Claude review correction). Every claim permanently consumes one
  `Attempt.AgentBudgetSlot`, unique per run; an additive migration backfills
  historical attempts and raises any historical run's maximum to at least its
  real usage. A lost race for one slot is classified as
  `agent_attempts.budget_slot_conflict` (safe, retryable) when the run is not
  actually at its maximum, and as `agent_attempts.budget_exhausted` only when
  it genuinely is — re-derived from fresh persisted state on every claim
  handler, never inferred from the exception alone. The cockpit exposes
  used/maximum/exhausted state and a clear human next action, kept distinct
  from the existing review-correction budget. See
  [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  for the accepted design and its rejected alternatives, and the
  [collaboration protocol](../architecture/agent-collaboration-protocol.md)
  and [delivery plan](mvp-delivery-plan.md) for how it is documented.
- Delivered commit: `feat: add durable run-wide agent claim budget`, the
  commit containing this handoff and the slice above. Resolve its exact SHA
  with `git log -1 --format=%H -- docs/roadmap/current-work.md`. A commit
  cannot embed its own SHA without changing that SHA; the final delivery
  report states the resolved literal hash. Its parent chain runs through
  `a0ce17a71a002f337b8d5167f83dbb1a0995d40d` (`docs: record deferred product
  ideas`) and `f0af8d2` (`feat: record durable agent token usage evidence`).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex planner/reviewer verdict: GO, including the concurrency corrections
  (isolating the `(RunId, AgentBudgetSlot)` index in the race proof, and
  distinguishing a safe below-maximum slot conflict from genuine exhaustion).
  This handoff records that delivered, reviewed state; it does not itself
  select or approve any further slice.

## Evidence actually run

- Backend: Domain 497/497; Application 882/882;
  Infrastructure.IntegrationTests 392/393 (one pre-existing
  platform-capability skip, unrelated to this slice); Api.IntegrationTests
  254/254. Release build passed with 0 warnings. `dotnet format
  --verify-no-changes` passed for every touched backend project.
- Frontend: 390 tests passed, plus typecheck and a production build.
- NSwag regenerated the client as part of the Release build; the additive
  migration was generated with `dotnet ef migrations add` and hand-reviewed
  for its backfill SQL. `dotnet ef migrations has-pending-model-changes`
  reported no pending model changes — migration and model agree.
  `git diff --cached --check` reported no whitespace errors before commit
  (only pre-existing CRLF-normalization notices, not whitespace errors).
- The budget-slot race classification is covered by both a genuine
  two-`DbContext` SQLite race (with distinct `AttemptNumber` values isolating
  the `(RunId, AgentBudgetSlot)` index from the unrelated `(RunId,
  AttemptNumber)` and one-`Running`-attempt indexes) and a canary test proving
  the race would silently succeed without that index, plus, per claim
  handler, deterministic race-injection tests for both the safe-conflict and
  genuine-exhaustion branches. All tests used disposable file-backed SQLite
  and deterministic provider doubles, never the real local database or an
  authenticated provider invocation.

## Open limits and next action

- Codex CLI token usage remains `Unknown`: no authoritative local contract
  was proven. Claude usage is best-effort, not account usage; token-budget
  enforcement and account-usage stop guardrails remain open. Increment 4 is
  not complete. Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- The Agent claim budget delivered here has no human-authorization override
  (unlike review correction): exhaustion is a hard stop for a run's remaining
  Agent-claiming paths. Automatic cancellation, retry, or provider fallback in
  response to exhaustion remain out of scope.
- Next action: the Codex planner/reviewer verifies this delivered baseline
  against Git, then selects and approves a bounded next Increment 4 slice
  from the [roadmap](mvp-delivery-plan.md). No next slice is approved by this
  handoff. Claude implements only a subsequently approved execution prompt;
  it does not set the roadmap or accept its own work.

## Closure rule for future slices

A slice is not complete until this handoff is updated in its substantive
commit with a resolvable delivered commit, remaining uncommitted work, checks
actually run, open risks, and the next action. Keep the page short and link to
contracts, ADRs, and tests instead of copying conversation logs. On every
resume, compare HEAD, branch, and staged/unstaged/untracked files with this
page. Investigate discrepancies; Git and code prevail over a stale handoff.
