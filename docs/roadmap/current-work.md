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

## Latest delivered baseline (2026-09-25): known global Agent-claim block for the six claim controls

- Branch: `main`. Delivered commit: `feat: add known global agent-claim
  block to the cockpit`. Resolve its exact SHA with `git log -1
  --format=%H -- docs/roadmap/current-work.md` — a commit cannot embed its
  own SHA without changing that SHA. Its parent is
  `56156e04d65b63eae5ba1b4341a7be419f9850d2` (`feat: fail-close
  host-measured process evidence for running and undispatched attempts`).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex-approved (GO) after two correction rounds, both folded into the
  final behavior described below rather than restated round by round — Git
  history retains the full narrative if it's ever needed. Codex approved
  this slice; it has not approved a next one.
- Scope: frontend-only, additive UI-honesty slice. The cockpit's six
  Agent-claim controls (Codex planning, Claude critical review, Codex
  challenge resolution, Claude implementation, Codex code review, Claude
  review correction) did not check the two existing run-wide budgets
  ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  count budget, [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  invocation-time budget) before offering a request action, even though the
  cockpit projection already carries both. A new pure function,
  `deriveGlobalAgentClaimBlock` (`src/frontend/DevalCopilot.Frontend/src/features/cockpit/deriveGlobalAgentClaimBlock.ts`),
  derives a `GlobalAgentClaimBlockReason` (`CountBudgetExhausted`,
  `TimeBudgetExhausted`, `TimeBudgetEvidenceInvalid`, or
  `BudgetProjectionUnavailable`) — deliberately never named "eligible" or
  "authorized," since it is only ever a veto signal, never a grant. It is
  `null` only to mean "no known global hard stop found here," never "this
  claim is allowed." A genuinely well-formed legacy run (`isLegacyUnknown`
  with `maximumMilliseconds`/`reservedMilliseconds`/`remainingMilliseconds`
  all absent, matching `RunCockpitAgentInvocationTimeBudgetSummary.LegacyUnknown()`
  exactly) is never treated as a block; an `isLegacyUnknown` shape that
  unexpectedly carries any populated time field is incoherent and fails
  closed instead. A positive remaining-time figure is never read as proof
  any one role's configured timeout fits within it. Any missing, malformed
  (including non-integer/`NaN`/`Infinity` count fields), internally
  inconsistent, or previous-run-stale projection fails closed to
  `BudgetProjectionUnavailable` rather than being read as healthy. The
  `remainingMilliseconds` check accepts exactly the one-millisecond
  discrepancy that independent truncation of three separately-rounded
  `TimeSpan` fields can legitimately produce at the API boundary
  (`AgentInvocationTimeBudgetResponse.FromDomain` truncates
  `Maximum`/`Reserved`/`Remaining` independently rather than computing
  `Remaining` from the other two on the wire, even though the Domain layer
  computes it exactly at the tick level) — never a wider or reversed
  discrepancy, and never a claim of tick-level wire precision the contract
  does not make. `RunCockpitView.tsx` computes the block synchronously
  during render from the currently selected `runId` (mirroring the existing
  `cockpit.runId === runId` guard already used by the two budget banners and
  the latest-attempt/token-usage panels), so a previously selected run's
  block state is never shown for the newly selected run, even for one
  frame. All six `*Action.tsx` components take a `globalClaimBlock` prop and
  withhold their request button (showing an attributed, run-wide-budget
  message instead) exactly when it is non-null. `ReviewCorrectionAction.tsx`
  additionally ensures its own separate ADR-0010 human-authorization
  affordances never present themselves as an effective path around this
  global block: `CreateReviewCorrectionAttemptCommandHandler` checks the
  ADR-0012/ADR-0013 global budgets before it ever reaches its own escalation
  branch, so creating a human escalation cannot actually succeed while a
  global block is present either — its "Create human escalation" button is
  withheld under a global block exactly like the plain request button, while
  review-correction-specific budget exhaustion *without* a global block
  still permits it; the authorize button and the "authorized" status line
  are replaced with an explicit "does not override" message while a global
  block is present. No backend, migration, or generated-client
  (`api-client.ts`) file changed at any point in this slice — the derivation
  reads only fields the cockpit projection already returns
  (`maximumAgentAttempts`/`agentAttemptsUsed`/`agentBudgetExhausted`/
  `agentInvocationTimeBudget.*`).
- Files changed: new `deriveGlobalAgentClaimBlock.ts` and
  `deriveGlobalAgentClaimBlock.test.ts`; modified `RunCockpitView.tsx`,
  `CodexPlanningAction.tsx`, `ClaudeCriticalReviewAction.tsx`,
  `ChallengeResolutionAction.tsx`, `ImplementationAction.tsx`,
  `CodeReviewAction.tsx`, `ReviewCorrectionAction.tsx`, and their six
  `*.test.tsx` files, plus `ProcessEvidenceLine.test.tsx` and
  `TokenUsageLine.test.tsx` (a `globalClaimBlock: null` default added to
  their shared fixtures) and `RunCockpitView.test.tsx` (a shared
  `healthyAgentClaimBudget` fixture, a run-switch test, and budget fields on
  the two connection-banner fixtures) — all under
  `src/frontend/DevalCopilot.Frontend/src/features/cockpit/`. Documentation:
  this page, `docs/product/run-cockpit-specification.md` (new "Known global
  claim block" subsection), and one unrelated wording fix in
  `docs/architecture/agent-collaboration-protocol.md` (a sentence describing
  provider-reported tokens was missing its final clause, "budget for either
  provider.").
- Checks actually run (final state, frontend-only): full frontend
  `vitest run` — 510/510 passed (baseline before this slice was 467/467);
  `tsc -b` clean; `oxlint` exited 0 with the same 20 pre-existing warnings,
  0 in any file this slice touched; `vite build` production build
  succeeded; `git diff --check` reported no whitespace errors on every
  touched file. Backend suites (Domain, Application,
  Infrastructure.IntegrationTests, Api.IntegrationTests, Architecture) and
  the generated NSwag client were **not rerun at any point in this slice**,
  confirmed by `git status --short` showing no changed file under
  `src/backend`, no migration, and no change to `api-client.ts` throughout
  every round — this slice touched only files under
  `src/frontend/DevalCopilot.Frontend/src/features/cockpit/` and three
  Markdown files.
- Open risks/limitations specific to this slice: this is a UI-honesty
  pre-check only — it never replaces or weakens server-side enforcement, and
  a stale UI snapshot can still race with a real claim (the server remains
  authoritative and independently re-verifies both budgets and every
  role-specific rule). It only catches the three known hard-stop cases
  already visible in the loaded cockpit projection; it is not a full
  eligibility calculator and does not know any role's own configured
  timeout. Carries forward every open risk already listed under the
  delivered baseline below, unchanged.
- Next action: Codex approved this slice (GO); it has not approved a next
  one. The Codex planner/reviewer selects and approves the next bounded
  Increment 4 slice from the [roadmap](mvp-delivery-plan.md).

## Prior delivered baseline (2026-09-25): process-evidence lifecycle fail-closed fix

- Commit `56156e0` (`feat: fail-close host-measured process evidence for
  running and undispatched attempts`) on `main`, Codex-approved (GO).
  Extended the identical read-time fail-closed fix already applied to
  `Attempt.GetAgentTokenUsageEvidence()` to its sibling
  `Attempt.GetAgentProcessExecutionEvidence()`, so a still-`Running` or
  undispatched attempt's process outcome/exit code/duration is never trusted
  by any of its three shared read paths (per-role status endpoints, the
  cockpit's `latestAgentAttempt`, and the collaboration-evidence
  drill-down). See the process-evidence sections of
  [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  and [run-cockpit-specification.md](../product/run-cockpit-specification.md)
  for the delivered contract.

## Prior delivered baseline (2026-09-25): pending-vs-terminal-unknown token-usage evidence

- Commit `81fa6f7` (`feat: add pending-vs-terminal-unknown token-usage
  evidence`) on `main`, Codex-approved (GO). Tightened the run-wide
  `tokenUsageSummary` and every per-attempt token-usage read path so a still-
  `Running` or undispatched attempt is never counted as known usage. See the
  token-usage sections of [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  and [run-cockpit-specification.md](../product/run-cockpit-specification.md)
  for the delivered contract.

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
