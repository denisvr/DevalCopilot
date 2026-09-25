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

## Latest delivered baseline (2026-09-25): process-evidence lifecycle fail-closed fix, extended from token usage

- Branch: `main`. Delivered commit: `feat: fail-close host-measured process
  evidence for running and undispatched attempts`. Resolve its exact SHA
  with `git log -1 --format=%H -- docs/roadmap/current-work.md` — a commit
  cannot embed its own SHA without changing that SHA. Its parent is
  `81fa6f7b146a219bcce2c0830eafbedbc0c36371` (`feat: add
  pending-vs-terminal-unknown token-usage evidence`; see the prior delivered
  slice's own commit for that history).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex-approved (GO).
- Scope: the prior slice's own flagged open risk — `Attempt.GetAgentProcessExecutionEvidence()`
  had the identical latent status-blind gap as `GetAgentTokenUsageEvidence()`
  before it was fixed, and was explicitly left unfixed as out of scope for
  that slice. This slice closes it, for process evidence instead of token
  usage, mirroring the exact same fix pattern: `Attempt.GetAgentProcessExecutionEvidence()`
  (`src/backend/DevalCopilot.Domain/Features/Runs/Attempt.cs`) now also
  returns `null` whenever `Status == AttemptStatus.Running` or
  `AgentDispatchedAtUtc is null`, even when the persisted
  `AgentProcessOutcome`/`AgentProcessExitCode`/`AgentProcessDuration` fields
  already look shape-valid (a corrupted or prematurely populated row). A
  genuinely dispatched, terminal attempt's evidence is unaffected, including
  a nonzero exit code, `TimedOut`, and `Cancelled`. No recording rule changed
  — this is a read-time trust fix only.
- Three read paths call this single shared method and are therefore fixed
  together: every per-role status endpoint's own `processExecution`, the run
  cockpit's `latestAgentAttempt.processExecution`, and the
  collaboration-message evidence drill-down endpoint
  (`GetCollaborationMessageEvidenceQueryHandler`). Confirmed unaffected by
  diff: the run-wide process-duration summary
  (`RunCockpitAgentProcessDurationAccumulator`/`RunCockpitAgentProcessDurationSummary`)
  already reads each attempt's `Status` directly from its own bounded
  per-row projection in `GetRunCockpitQueryHandler`, never through
  `GetAgentProcessExecutionEvidence()`, so it needed no change and shows
  zero diff.
- Frontend: `describeProcessEvidence.ts` and `ProcessEvidenceLine.tsx`
  (`src/frontend/DevalCopilot.Frontend/src/features/cockpit/`) reordered to
  check dispatched/running state before ever consulting the process object's
  own shape — a new `hasTrustedProcessEvidence` helper mirrors the prior
  slice's own `hasTrustedTokenUsage`. `ProcessEvidenceLine`'s
  `data-process-outcome` test-hook attribute now always agrees with the
  rendered text (previously it read the raw, untrusted `outcome` value
  directly). The configured timeout remains a separate, always-known value
  shown regardless of trust state. Found one direct-render bypass —
  `CollaborationEvidenceDrilldown.tsx` renders `processExecution.outcome`/
  `durationMilliseconds` directly rather than through `ProcessEvidenceLine`
  — and gave it the same guard by reusing `hasTrustedProcessEvidence` rather
  than duplicating the check inline.
- New/extended tests: Domain
  (`AgentProcessExecutionEvidenceTests.cs` — running/undispatched-tampered
  via reflection on private setters mirroring `AgentAssignmentTests`'s own
  pattern, plus a genuinely-dispatched-terminal confirmation covering
  nonzero exit/`TimedOut`/`Cancelled`); Infrastructure (new
  `AgentProcessExecutionEvidenceTrustMigrationTests.cs`, mirroring
  `AgentTokenUsageMigrationTests.cs`'s raw-SQL-tampered-row-against-real-SQLite
  pattern); Api (new `AgentProcessExecutionEvidenceTrustEndpointTests.cs`,
  covering a representative per-role status endpoint, the cockpit
  `latestAgentAttempt`, and the collaboration-evidence drill-down, each
  against a tampered Running/undispatched row); frontend
  (`describeProcessEvidence.test.ts`, `ProcessEvidenceLine.test.tsx`,
  `CollaborationEvidenceDrilldown.test.tsx` — a well-formed-looking object
  paired with a running/undispatched context proves both the text and the
  outcome attribute report the not-yet-known state).
- Explicitly unchanged, confirmed by diff scope: ADR-0012, ADR-0013; no
  migration; no public API response shape/field change (only which values
  are null when); token-usage's own already-committed files
  (`AgentTokenUsageEvidence.cs`, `describeTokenUsage.ts`, `TokenUsageLine.tsx`)
  untouched.
- Checks run for this pass (all suites rerun in full for this slice, exact
  counts): `dotnet format DevalCopilot.slnx --verify-no-changes` clean;
  Release build 0 warnings/0 errors; Domain 524/524 (519 baseline + 5 new);
  Application 939/939 (unaffected — no Application file touched by this
  slice); Infrastructure.IntegrationTests 440/441 (437/438 baseline + 3 new,
  the same one pre-existing platform-capability skip); Api.IntegrationTests
  288/288 (286 baseline + 2 new); Architecture 9/9; `dotnet ef migrations
  has-pending-model-changes` reported no pending changes; NSwag regeneration
  confirmed byte-identical across two consecutive builds with zero
  `export enum` occurrences (no public response shape/field changed);
  frontend 467/467 vitest tests, `tsc --noEmit` clean, `oxlint` exited 0 with
  the same 20 pre-existing warnings (0 new, 0 in files this slice touched),
  `vite build` production build passed; `git diff --check` reported no
  whitespace errors (only the pre-existing CRLF-normalization notice on
  `api-client.ts`). No automated test invokes a real provider or model; all
  Infrastructure/Api tests use disposable file-backed SQLite, never the real
  local database.
- Open risks specific to this slice: this fix is read-time only and does not
  change what evidence is ever recorded, add a measured-duration or
  token-usage enforcement mechanism, or alter ADR-0013's existing
  invocation-time reservation budget (already in place and unaffected — see
  [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)).
  No new open risk beyond what the carried-forward list below already
  states.
- Next action: this baseline is delivered and Codex-reviewed (GO). The Codex
  planner/reviewer selects and approves the next bounded Increment 4 slice
  from the [roadmap](mvp-delivery-plan.md); no next slice is approved by
  this handoff.

## Prior delivered baseline (2026-09-25): pending-vs-terminal-unknown token-usage evidence

- Commit `81fa6f7` (`feat: add pending-vs-terminal-unknown token-usage
  evidence`) on `main`, Codex-approved (GO). Tightened the run-wide
  `tokenUsageSummary` and every per-attempt token-usage read path so a still-
  `Running` or undispatched attempt is never counted as known usage. See the
  token-usage sections of [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  and [run-cockpit-specification.md](../product/run-cockpit-specification.md)
  for the delivered contract. Its own flagged open risk — the identical
  status-blind gap in `Attempt.GetAgentProcessExecutionEvidence()` — is
  resolved by the baseline above and is no longer open.

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
