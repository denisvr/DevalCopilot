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

## Latest delivered baseline (2026-09-26): candidate-specific invocation-time fit

- Branch: `main`. Delivered commit: `feat: add candidate-specific
  invocation-time fit to the cockpit`. Resolve its exact SHA with
  `git log -1 --format=%H -- docs/roadmap/current-work.md` — a commit
  cannot embed its own SHA without changing that SHA. Its parent is
  `ba186ab22a0b5a3846192eaa971493ffa77eb2a2` (`feat: add known global
  agent-claim block to the cockpit`).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex-approved (GO) after two correction rounds, both folded into the
  final behavior described below rather than restated round by round — Git
  history retains the full narrative if it's ever needed. Codex approved
  this slice; it has not approved a next one.
- Scope: a bounded, additive slice closing the known gap the prior slice's
  own `deriveGlobalAgentClaimBlock` deliberately left open — it never knows
  any one role's own configured invocation timeout, so it cannot tell
  whether a *positive* remaining reserved time actually fits a *specific*
  claim path's own configured duration. Adds: (1) `AgentClaimPath` and
  `AgentClaimPathPolicy` (`src/backend/DevalCopilot.Application/Features/Runs/`)
  centralizing the six previously-hardcoded-per-handler `InvocationTimeout`
  values, timeout unchanged, now the single source both the six
  `Create*AttemptCommandHandler`s and the new projection read; (2) an
  additive per-claim-path advisory fit projection in `GetRunCockpit`
  (`RunCockpitAgentClaimPathTimeFitEntry.cs`,
  `RunCockpitAgentClaimPathTimeFitProjection.cs`), reusing the existing
  exact `TimeSpan`/tick-level reserved-time computation with no extra
  database query, exposed as `agentClaimPathTimeFits` on
  `GetRunCockpitResponse` (new `AgentClaimPathTimeFitResponse`, plain
  strings, zero generated TS enums, carrying only claim path and fit — no
  role/provider mapping); (3) a new frontend derivation
  `deriveAgentClaimPathTimeFit` (+ tests), wired into all six cockpit
  action components (including both of Review Correction's controls)
  alongside — never in place of — the existing `globalClaimBlock`; both
  signals are shown together when both apply. See the "Candidate-specific
  invocation-time fit" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md)
  for the full behavior contract.
- `AgentClaimPath`/`AgentClaimPathPolicy` live at
  `src/backend/DevalCopilot.Application/Features/Runs/` (namespace
  `DevalCopilot.Application.Features.Runs`), the same flat placement as the
  sibling cross-cutting Application helper `AgentInvocationTimeBudget.cs` —
  not Domain (claim-path timeout is Application-owned execution
  configuration, not a Domain invariant) and not on `AgentAttemptContract`/
  `AgentRole` (per ADR-0009's separation of role/effect/provider-assignment
  concerns; Implementer alone spans two distinct claim paths). The six
  configured timeout values (10/10/10/20/10/20 minutes) are unchanged from
  each handler's own prior inline value — a pure centralization. The fit
  DTO (`RunCockpitAgentClaimPathTimeFitEntry`/`AgentClaimPathTimeFitResponse`)
  carries only claim path and fit — no role/provider mapping, since no
  present consumer reads it (the six cockpit action components already know
  their own claim path statically).
- `deriveAgentClaimPathTimeFit.ts` fails the entire six-entry projection
  closed (`ProjectionUnavailable`) — never just the affected path — on: a
  run-selection mismatch; any unrecognized claim-path value; a missing or
  duplicated (agreeing or contradictory) entry for any of the six known
  paths; an unrecognized fit value on ANY of the six entries, including one
  the caller did not ask about; or ANY of the six entries' fit value
  contradicting the run-wide `agentInvocationTimeBudget.isLegacyUnknown`/
  `evidenceInvalid` state `deriveGlobalAgentClaimBlock` already reads (both
  are derived from the same underlying budget data and must never
  contradict). A requested path whose own entry looks perfectly coherent in
  isolation still fails closed if a *different* path's entry is wrong — a
  projection that can be wrong for one path cannot be trusted for any path.
  The derivation never performs client-side millisecond/tick arithmetic to
  re-derive the fit conclusion — it only trusts the backend's own closed fit
  state once every entry is proven coherent. `deriveGlobalAgentClaimBlock.ts`
  and its own tests were not touched by this slice.
- Checks actually run (final state): `dotnet format --verify-no-changes`
  clean; Release build 0 warnings/0 errors; Domain 524/524; Application
  956/956; Infrastructure.IntegrationTests 440/441 (one pre-existing
  platform-capability skip); Api.IntegrationTests 288/288; Architecture
  9/9; `dotnet ef migrations has-pending-model-changes` reported no pending
  changes (read/refactor only, no schema change); NSwag regeneration
  byte-identical across two builds with zero `export enum` occurrences and
  the leaner `AgentClaimPathTimeFitResponse` shape; frontend 545/545 tests,
  `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing warnings
  (0 new), `vite build` production build passed; `git diff --check`
  reported no whitespace errors (only the pre-existing CRLF-normalization
  notice on `api-client.ts`).
- No migration, no ADR change, no change to `deriveGlobalAgentClaimBlock.ts`
  or its own tests, no change to the six configured timeout values, no
  change to any already-delivered persisted `Attempt.Role`/`Attempt.Provider`
  field or its historical provenance. This remains advisory-only: it adds no
  new server-side enforcement — ADR-0013's own reservation-time check
  (already delivered) is unchanged.
- Next action: this baseline is delivered and Codex-reviewed (GO). The Codex
  planner/reviewer selects and approves the next bounded Increment 4 slice
  from the [roadmap](mvp-delivery-plan.md); no next slice is approved by
  this handoff.

## Prior delivered baseline (2026-09-25): known global Agent-claim block for the six claim controls

- Commit `ba186ab` (`feat: add known global agent-claim block to the
  cockpit`) on `main`, Codex-approved (GO). Added `deriveGlobalAgentClaimBlock`,
  a pure frontend veto signal (never a grant) surfacing exhausted count
  budget, zero/negative/invalid remaining time, or an unavailable
  projection across all six cockpit Agent-claim controls, including both
  of Review Correction's own controls. See the "Known global claim block"
  section of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md)
  for the delivered contract.

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
