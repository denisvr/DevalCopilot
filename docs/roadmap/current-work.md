# Current work and cross-chat handoff

This is the short entry point for resuming DevalCopilot after compaction or in
another chat. It is a checkpoint, not a substitute for the
[delivery plan](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
[architecture](../architecture/README.md), or
[accepted decisions](../decisions/README.md). Repository and agent reports are
evidence, not authority. Git history and the code itself are the authoritative
record of past delivered slices — this page does not restate it.

**Before editing:** compare `HEAD`, branch, and staged/unstaged/untracked
changes against this page. Investigate any discrepancy; Git and code prevail
over a stale summary. Never reset work merely to match this page.

## Latest delivered baseline (2026-09-25): run-wide Agent process-duration evidence summary

- Branch: `main`. Delivered commit: `feat: add run-wide agent
  process-duration evidence summary`. Resolve its exact SHA with
  `git log -1 --format=%H -- docs/roadmap/current-work.md` — a commit cannot
  embed its own SHA without changing that SHA. Its parent is
  `ebd9350acb07aa9f09fda558797a3b78d6776a08` (`feat: record Codex CLI token
  usage evidence`; see the prior delivered slice's own commit for that
  history).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex-approved (GO) after one correction round (overflow-safe summation
  classified as a distinct `UnrepresentableTotal` state rather than
  `MalformedEvidence`, and honest messaging when malformed evidence coexists
  with still-pending attempts) and a documentation-precision correction pass
  (the `double` millisecond projection preserves only the zero-vs-positive
  distinction, not full tick-level precision at every magnitude — the exact
  sum lives at the Domain level as checked `TimeSpan`/tick arithmetic).
- Scope: a bounded, read-only, run-wide summary of HOST-MEASURED Agent
  process-duration evidence in `GetRunCockpit`, alongside the existing
  token-usage summary and both Agent budgets ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md),
  [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)).
  Pure telemetry — never a budget, never enforcing anything. One closed
  evidence-state enum (`AgentProcessDurationEvidenceStatus`: `NoDispatchedAttempts`,
  `Complete`, `PendingEvidence`, `PartialEvidence`, `MalformedEvidence`,
  `UnrepresentableTotal`) plus bounded counts and an optional exact total,
  non-null only for `Complete`. No ADR added or edited; no migration needed
  (pure read projection).
- Files: `AgentProcessDurationEvidenceStatus.cs`,
  `RunCockpitAgentProcessDurationSummary.cs`,
  `RunCockpitAgentProcessDurationAccumulator.cs`, `GetRunCockpitQueryHandler.cs`,
  `GetRunCockpitQueryResult.cs` (Application);
  `AgentProcessDurationSummaryResponse.cs`, `GetRunCockpitResponse.cs`,
  `GetRunCockpitEndpoint.cs` (Api); `AgentProcessDurationSummaryBanner.tsx`
  (+ `.test.tsx`), `RunCockpitView.tsx`, regenerated `api-client.ts`
  (frontend). Docs: the "Run-wide Agent process-duration evidence summary"
  section of [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  and the "Host-measured Agent process-duration summary" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  Tests: `RunCockpitAgentProcessDurationSummaryTests.cs` (Application, 13
  cases: 11 original + 2 new for `UnrepresentableTotal`),
  `AgentProcessDurationSummaryEndpointTests.cs` (Api, 9 cases: 5 original + 4
  new), `AgentProcessDurationSummaryBanner.test.tsx` (12 cases: 7 original +
  5 new).
- Checks actually run (most recent pass): `dotnet format
  DevalCopilot.slnx --verify-no-changes` clean; Release build 0
  warnings/0 errors; Domain 519/519; Application 930/930 (917 base + 13 in
  the touched test file); Infrastructure.IntegrationTests 435/436 (one
  pre-existing platform-capability skip); Api.IntegrationTests 278/278 (269
  base + 9 new); Architecture 9/9; `dotnet ef migrations
  has-pending-model-changes` reported no pending changes; frontend 447/447
  tests (442 base + 5 net new), `tsc --noEmit` clean, production build
  passed, `oxlint` exited 0 with the same 20 pre-existing warnings (0 new);
  `git diff --check` clean (only the pre-existing CRLF-normalization notice
  on `api-client.ts`). No automated test invokes a real provider or model.
  These are the same checks Codex reviewed before approval; none needed
  rerunning to close this slice, since no file changed after that pass.
- Open risks specific to this slice: pure telemetry, not a budget or
  account-usage measurement; never guarantees a provider process cannot
  outlive its own configured timeout (same exclusion ADR-0013 states);
  a still-pending attempt never contributes a partial duration to the total
  by design; `PartialEvidence`/`PendingEvidence` deliberately show no
  partial/labeled sum at all.
- Next action: this baseline is delivered and Codex-reviewed (GO). The Codex
  planner/reviewer selects and approves the next bounded Increment 4 slice
  from the [roadmap](mvp-delivery-plan.md); no next slice is approved by
  this handoff.

## Open risks (carried forward)

- Codex CLI token usage remains `Unknown`-only on cache breakdown; Claude
  usage is best-effort, not account usage; token-budget enforcement and
  account-usage stop guardrails remain open; Increment 4 is not complete.
- Gemini execution remains disabled by [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- The ADR-0012 count-based Agent claim budget and the ADR-0013
  invocation-time budget have no human-authorization override. Neither
  budget measures actual wall-clock provider duration, enforces a token
  limit, enforces a provider account-usage limit, or guarantees a provider
  process cannot outlive its own configured timeout.
- The cockpit has no expand/collapse interaction beyond the bounded legacy-
  details disclosure and the collaboration-evidence drill-down; linked
  evidence and raw artifacts beyond that remain aspirational specification
  (see the [cockpit specification](../product/run-cockpit-specification.md)).
- Reply verification in the collaboration timeline is a display-only
  re-check of the backend's closed reply policy; a parent absent from the
  currently loaded timeline window is shown as "not present," never as
  confirmed missing.

## Closure rule for future slices

A slice is not complete until this handoff is updated in its substantive
commit with a resolvable delivered commit, remaining uncommitted work, checks
actually run, open risks, and the next action. Keep the page short and link to
contracts, ADRs, and tests instead of copying conversation logs. On every
resume, compare HEAD, branch, and staged/unstaged/untracked files with this
page. Investigate discrepancies; Git and code prevail over a stale handoff.
