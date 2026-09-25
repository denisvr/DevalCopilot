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

## Latest delivered baseline (2026-09-25): pending-vs-terminal-unknown token-usage evidence, extended to every per-attempt read path

- Branch: `main`. Delivered commit: `feat: add pending-vs-terminal-unknown
  token-usage evidence`. Resolve its exact SHA with `git log -1
  --format=%H -- docs/roadmap/current-work.md` — a commit cannot embed its
  own SHA without changing that SHA. Its parent is
  `850c01d598eeacd2ed8a44c44872fb4f17305a02` (`feat: add run-wide agent
  process-duration evidence summary`).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex-approved (GO) after two correction rounds: (1) frontend copy —
  `describeTokenUsage` now names pending vs. terminal-unknown attempts
  explicitly instead of one undifferentiated "no usage evidence" phrase, and
  never renders a bare "0 input / 0 output" when zero attempts have known
  usage (a genuine terminal zero still displays as a real zero); (2) a
  fail-closed gap found beyond the run-wide summary — every per-role
  attempt-status endpoint and the cockpit's own `latestAgentAttempt.tokenUsage`
  read a single attempt's usage through `Attempt.GetAgentTokenUsageEvidence()`,
  which checked only shape, never whether the attempt had concluded. Fixed at
  that single shared source (`src/backend/DevalCopilot.Domain/Features/Runs/Attempt.cs`):
  it now also returns `null` whenever `Status == AttemptStatus.Running` or
  `AgentDispatchedAtUtc is null`, so a corrupted or prematurely populated row
  is never trusted regardless of which of the seven call sites reads it. The
  frontend mirrors this: `describeTokenUsage.ts`/`TokenUsageLine.tsx` now
  check running/dispatch state before checking whether usage fields look
  well-formed, via a new exported `hasTrustedTokenUsage(usage, context)`.
- Scope: tightens the run-wide `tokenUsageSummary` in `GetRunCockpit` so a
  dispatched Agent attempt still `Running` is never counted as known usage,
  even when its persisted row already carries seemingly-valid token fields —
  only a terminal attempt's usage evidence is ever trusted or summed.
  `RunTokenUsageCompleteness` gains one new, appended member,
  `PendingEvidence` (at least one dispatched attempt still running; every
  terminal attempt observed so far has known usage), kept distinct from
  `Partial` (now: at least one *terminal* attempt lacks known usage).
  `RunCockpitTokenUsageSummary`/`RunTokenUsageSummaryResponse` gain two
  additive bounded counts, `PendingAttemptCount`/`pendingAttemptCount` and
  `TerminalAttemptsWithUnknownUsage`/`terminalAttemptsWithUnknownUsage`; the
  pre-existing `AttemptsWithUnknownUsage`/`attemptsWithUnknownUsage` field
  keeps its original, broader meaning (every dispatched attempt without known
  usage) and always equals the sum of the two new counts — an additive split,
  not a breaking change; `Completeness` remains a plain `string` wire value,
  not a generated TS enum. No migration, provider parser, invocation,
  account-usage inference, token threshold, claim/dispatch gate, or automatic
  stop behavior was added. The process-duration summary's own files and both
  ADR-0012/ADR-0013 were not touched.
- Files: `RunTokenUsageCompleteness.cs`, `RunCockpitTokenUsageAccumulator.cs`,
  `RunCockpitTokenUsageSummary.cs`, `GetRunCockpitQueryHandler.cs`,
  `Attempt.cs` (Domain — the shared fail-closed fix) (Application/Domain);
  `RunTokenUsageSummaryResponse.cs` (Api); `describeTokenUsage.ts` and
  `TokenUsageLine.tsx` (both modified production frontend files, not just
  their tests), regenerated `api-client.ts` (frontend). Tests:
  `RunCockpitTokenUsageSummaryTests.cs`, `AgentTokenUsageEndpointTests.cs`,
  `AgentTokenUsageMigrationTests.cs`, `describeTokenUsage.test.ts`,
  `TokenUsageLine.test.tsx` (all extended); new
  `RunTokenUsageSummaryEndpointTests.cs` (Api). Docs: the token-usage
  sections of [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  and [run-cockpit-specification.md](../product/run-cockpit-specification.md).
- Checks actually run (final validation pass, after both correction rounds):
  `dotnet format DevalCopilot.slnx --verify-no-changes` clean; Release build
  0 warnings/0 errors (rebuilt twice, byte-identical `api-client.ts` both
  times, zero `export enum` occurrences — no API response shape changed by
  the second correction round); Domain 519/519; Application 939/939;
  Infrastructure.IntegrationTests 437/438 (one pre-existing
  platform-capability skip); Api.IntegrationTests 286/286; Architecture 9/9;
  `dotnet ef migrations has-pending-model-changes` reported no pending
  changes; frontend 458/458 vitest tests, `tsc --noEmit` clean, `oxlint`
  exited 0 with the same 20 pre-existing warnings (0 new), `vite build`
  production build passed; `git diff --check` reported no whitespace errors
  (only the pre-existing CRLF-normalization notice on `api-client.ts`).
  `git status --short` confirmed zero diff on every process-duration-summary
  file and on ADR-0012/ADR-0013. No automated test invokes a real provider or
  model.
- Open risks specific to this slice: `Attempt.GetAgentProcessExecutionEvidence()`
  has the same latent status-blind gap this slice fixed for token-usage
  evidence (`Attempt.GetAgentTokenUsageEvidence()`), but fixing it was
  explicitly out of scope here — it is process-duration evidence, not
  token-usage evidence, and remains unaddressed. The run-wide Agent
  claim-count budget ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md))
  and invocation-time budget ([ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md))
  are unaffected by and independent of this evidence-only change.
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
- `Attempt.GetAgentProcessExecutionEvidence()` has the same latent
  status-blind gap that `Attempt.GetAgentTokenUsageEvidence()` had before the
  slice above fixed it for token-usage evidence; it has not been corrected
  for process-duration evidence.

## Closure rule for future slices

A slice is not complete until this handoff is updated in its substantive
commit with a resolvable delivered commit, remaining uncommitted work, checks
actually run, open risks, and the next action. Keep the page short and link to
contracts, ADRs, and tests instead of copying conversation logs. On every
resume, compare HEAD, branch, and staged/unstaged/untracked files with this
page. Investigate discrepancies; Git and code prevail over a stale handoff.
