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

## Latest delivered baseline (2026-09-26): recorded collaboration-input provenance

- Branch: `main`. Delivered commit: resolve its exact SHA with `git log -1
  --format=%H -- docs/roadmap/current-work.md` (a commit cannot embed its own
  SHA). Its parent is `c4eb8528e4902fac29d18460710b09972bae395c` (`docs:
  approve collaboration input provenance slice`).
- Owner-authorized, bounded, read-only slice: extends the existing
  collaboration-message attempt-evidence drill-down to show the ordered,
  durable `AttemptInputMessage` references for the exact producing Agent
  attempt, labeled everywhere as **recorded collaboration inputs** — never
  the complete prompt, complete context manifest, or a resumable provider
  session. No new persistence, adapter, native-session resume, or
  execution-policy change, exactly as scoped.
- Scope delivered (Application, `Features/Runs/Queries/GetCollaborationMessageEvidence/`):
  `AttemptInputMessageCoherence.IsGaplessFromZero` (pure gap/duplicate check)
  and `GetCollaborationMessageEvidenceQueryHandler`'s new
  `ResolveInputMessagesAsync` resolve the exact attempt's own
  `AttemptInputMessage` rows, preserve their stored order, and require every
  referenced `CollaborationMessage` to resolve within the SAME run — a gap, a
  duplicate, or a missing/foreign reference fails the entire set closed
  (`AttemptInputMessageEvidenceStatus.Invalid`) rather than presenting a
  partial subset. A legitimate attempt with none (e.g. a Planner) reports
  `Empty`. A coherent set is bounded at 10 entries with an honest
  `InputMessagesOmitted` signal, mirroring the existing artifact cap. Exposed
  as new `inputMessagesStatus`/`inputMessages`/`inputMessagesOmitted`/
  `inputMessageTotalCount` fields on `CollaborationMessageEvidenceResponse`
  (API: `AttemptInputMessageEvidenceResponse`, `type` crosses the wire as a
  plain string, no generated TS enum). Every existing `evidenceStatus`
  (`HasEvidence`/`NoAgentEvidence`/`AttemptLinkBroken`) and artifact behavior
  is unchanged.
- Frontend: new `AttemptInputMessages` component and
  `describeAttemptInputMessages.ts` render the three states honestly (`Empty`,
  `Invalid`, and the ordered `Recorded` list), reusing
  `AgentCollaboration.tsx`'s own already-loaded-timeline map (the same one
  that verifies reply-parent relationships) to show a referenced message's
  real summary only when it is actually present in the caller's currently
  loaded window — otherwise stating "not present in the currently loaded
  timeline" rather than implying visibility. `CollaborationEvidenceDrilldown`
  and `AgentCollaboration` now thread that map through; a small shared
  `collaborationMessageTypeLabel.ts` module holds the message-type label
  table so importing it does not affect either component's fast-refresh
  boundary (oxlint `react(only-export-components)`).
- See the new "Recorded collaboration-input provenance" subsection of
  [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md).
- Checks actually run: `dotnet format --verify-no-changes` clean (after one
  `dotnet format` pass fixing doc-comment line wrapping); Release build 0
  warnings/0 errors; Domain 524/524; Application 978/978 (+14 new);
  Infrastructure.IntegrationTests 440/441 (one pre-existing platform-
  capability skip, unrelated); Api.IntegrationTests 294/294 (+3 new);
  Architecture 9/9; `dotnet ef migrations has-pending-model-changes` reported
  no pending changes; NSwag regeneration byte-identical across two builds
  with zero `export enum` occurrences; frontend 578/578 tests (+15 new),
  `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing warnings (0
  new), `vite build` production build passed; `git diff --check` reported no
  whitespace errors (only the pre-existing CRLF-normalization notices).
- No migration, no ADR change, no new adapter, no native-session resume, and
  no change to any existing evidence status, artifact, process, or
  token-usage behavior — this slice does not close Increment 4's
  provider-session-resume or account-usage exit criteria; those remain open
  exactly as recorded below.
- Review follow-up: Codex recorded **NO-GO** in
  [planner-handoff.md](planner-handoff.md) for two findings, corrected in
  commit (resolve its exact SHA with `git log -1 --format=%H --
  docs/roadmap/current-work.md`), parent `8195d2ab2216de05085fbbad5237a506af6030fa`
  (the planner's NO-GO decision commit). Both corrections stay within this
  approved, read-only slice:
  1. **Role-aware zero-input handling.** `ResolveInputMessagesAsync` now takes
     the producing attempt's own `AgentRole`. Only `AgentRole.Planner`
     legitimately records zero `AttemptInputMessage` rows and reports `Empty`;
     every other current role (`CriticalReviewer`, `Resolver`, `Implementer`,
     `CodeReviewer`) always persists at least one required input row through
     its own claim path, so zero rows for any of them now reports `Invalid` —
     never the same innocent "started from none" state, since it is otherwise
     indistinguishable from a lost or corrupted input set. Every existing
     `HasEvidence`/`NoAgentEvidence`/`AttemptLinkBroken` status and the
     coherent/gap/cross-run/cap behaviors from the original delivery are
     unchanged.
  2. **Evidence correction.** The `MaxInputMessages` doc comment previously
     claimed no current shape can exceed 10 inputs; corrected to name the real
     11-row bound (a review-correction attempt's Execution report plus up to
     `ReviewCorrectionOutputSchema.MaximumFindings` (10) Review findings). The
     bounded-overflow test previously used 10 Challenges on one critical
     review — a shape no real attempt can produce, since the real Challenge
     bound is one to five — and now uses that real 11-input review-correction
     shape instead.
  - Regression coverage added: a `[Theory]` proving `CriticalReviewer`,
    `Resolver`, `Implementer`, and `CodeReviewer` each report `Invalid` (never
    `Empty`) for zero recorded inputs (Application), plus one focused API
    regression for the same `CriticalReviewer` case.
  - Checks actually run for the correction: `dotnet format` clean (after one
    `dotnet format` pass); Release build 0/0; Domain 524/524, Application
    982/982 (+4 new), Infrastructure.IntegrationTests 440/441 (one
    pre-existing, unrelated skip), Api.IntegrationTests 295/295 (+1 new),
    Architecture 9/9; `dotnet ef migrations has-pending-model-changes`
    reported no pending changes; NSwag regeneration byte-identical to the
    prior delivery's own hash (the API contract shape is unchanged), zero
    `export enum` occurrences; `git diff --check` reported no whitespace
    errors. No frontend file changed for this correction, so the frontend
    suite was not rerun.
  - Review follow-up: Codex reviewed `a546318` and recorded a further **NO-GO**
    in [planner-handoff.md](planner-handoff.md) for one remaining edge,
    corrected in commit (resolve its exact SHA with `git log -1 --format=%H --
    docs/roadmap/current-work.md`), parent
    `057b4885c5df0e25e806564f06c1721bec504616` (the planner's NO-GO decision
    commit). Also within this same approved, read-only slice:
    the evidence query's attempt/message role-and-provider coherence check
    (`attempt.AgentRole != message.ActorAgentRole`, likewise for provider)
    accepted a pair of null roles/providers as "equal", so a corrupted or
    historical `ProviderObserved`/`Agent` pair with both sides undefined would
    reach `attempt.AgentRole!.Value` and throw instead of returning the
    existing `AttemptLinkBroken` fail-closed status.
    `GetCollaborationMessageEvidenceQueryHandler`'s coherence check now
    explicitly rejects a null `AgentRole`/`AgentProvider` on the attempt side
    BEFORE the equality comparison, so an undefined pair (equal or not) always
    fails closed to `AttemptLinkBroken` — never a null-reference throw, and
    never a coherent-looking match. Every valid `HasEvidence` shape, every
    existing `AttemptLinkBroken`/`NoAgentEvidence` path, and the API response
    contract are unchanged.
  - Regression coverage added: a focused SQLite-persisted test
    (`GetCollaborationMessageEvidenceQueryHandlerTests`) that corrupts both a
    real attempt's and its linked message's role/provider columns to `NULL`
    via raw SQL (a shape no Domain factory can produce) and proves the result
    is `AttemptLinkBroken` with `attemptId` null and every input-evidence
    field at its default (`Empty`/empty list/not omitted/zero count) — never a
    thrown exception.
  - Checks actually run for this correction: `dotnet format --verify-no-changes`
    clean; Release build 0/0; Domain 524/524, Application 983/983 (+1 new),
    Infrastructure.IntegrationTests 440/441 (one pre-existing, unrelated
    skip), Api.IntegrationTests 295/295 (unchanged), Architecture 9/9;
    `dotnet ef migrations has-pending-model-changes` reported no pending
    changes; NSwag regeneration byte-identical to the prior delivery's own
    hash (the API contract shape is unchanged), zero `export enum`
    occurrences; `git diff --check` reported no whitespace errors. No
    frontend file changed for this correction, so the frontend suite was not
    rerun.
  - Claude reports this correction; it does not approve the slice, make the
    three owner-requested process improvements, or start another slice. Codex
    retains final acceptance in the [planner handoff](planner-handoff.md).

## Prior delivered baseline (2026-09-26): provider-separated run token-usage projection

- Branch: `main`. Delivered commit:
  `e3805a887068a3b7707594eef6a2d52602a00401`, parent
  `63bd7191caaa45b8747f13b2d4fce8828a19c21f` (`docs: close blocked
  provider allowance slice`).
- Owner-authorized, bounded slice (replacing the closed Provider allowance
  evidence attempt): a read-only, provider-separated run token-usage
  projection in the cockpit, built entirely from existing trusted per-attempt
  evidence — no new provider adapter, no account-allowance observation, no
  threshold, no claim-eligibility change, and no migration, exactly as scoped.
- Scope delivered: `RunCockpitProviderTokenUsageProjection` (Application,
  `Features/Runs/Queries/GetRunCockpit/`) partitions the same dispatched-
  Agent-attempt evidence the existing run-wide `RunCockpitTokenUsageSummary`
  sums into three fixed buckets — Codex, ClaudeCode, and Unattributed (a
  dispatched attempt with a missing or unrecognized recorded provider) — in
  one single-pass, constant-memory accumulator fed from
  `GetRunCockpitQueryHandler`'s existing bounded projection loop (no
  additional database round trip). Exposed as the new
  `ProviderTokenUsageSummaries` field on `GetRunCockpitQueryResult` /
  `GetRunCockpitResponse` (API: `RunCockpitProviderTokenUsageEntryResponse`,
  attribution crosses the wire as a plain string, no generated TS enum), and
  rendered by the new `ProviderTokenUsageSummaries` frontend component
  alongside — never in place of — the existing `RunTokenUsageSummary`. The
  existing run-wide summary and its own tests are unchanged.
- Each bucket reuses the exact same completeness/fail-closed rules as the
  run-wide summary (a still-`Running` attempt's usage is never trusted;
  only a `Complete` bucket may be shown as that provider's total), scoped
  independently to only that bucket's own dispatched attempts. See the new
  "Provider-separated token-usage projection" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
- Checks actually run: `dotnet format --verify-no-changes` clean; Release
  build 0 warnings/0 errors; Domain 524/524; Application 964/964 (+8 new);
  Infrastructure.IntegrationTests 440/441 (one pre-existing platform-
  capability skip, unrelated); Api.IntegrationTests 291/291 (+3 new);
  Architecture 9/9; `dotnet ef migrations has-pending-model-changes` reported
  no pending changes; NSwag regeneration byte-identical across two builds
  with zero `export enum` occurrences; frontend 556/556 tests (+11 new),
  `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing warnings (0
  new), `vite build` production build passed; `git diff --check` reported no
  whitespace errors (only the pre-existing CRLF-normalization notice on
  `api-client.ts`).
- No migration, no ADR change, no change to the existing run-wide
  `RunCockpitTokenUsageSummary`/`RunCockpitTokenUsageAccumulator` or their own
  tests, no new adapter, and no account-allowance observation of any kind —
  this slice does not address Increment 4's open account-usage exit
  criterion; that remains open exactly as recorded below.
- Review follow-up: Codex recorded **NO-GO** for two narrow frontend
  presentation defects in the [planner handoff](planner-handoff.md), corrected
  in commit `281b3e135405a01a15f966d79180854731f84025`, parent
  `8cc01510cd90344fc39ab1234454aff5c958c35f` (the planner's NO-GO
  decision commit).
  Frontend-only correction, no backend/API/adapter/persistence/threshold
  change:
  1. `describeProviderAttribution` (`describeTokenUsage.ts`) now looks up its
     label through a `Map` instead of a plain object, so an `attribution`
     value colliding with an inherited `Object.prototype` member name
     (`toString`, `constructor`, `__proto__`, `hasOwnProperty`, `valueOf`)
     resolves to the honest "Unrecognized provider" fallback instead of
     leaking an inherited property.
  2. Provider-bucket wording is now scoped to that bucket's own provider
     label via a shared `describeUsageSummary(summary, wording)` helper
     (`totalLabel`/`notTotalPhrase`/`notTotalYetPhrase`), so a bucket reads
     e.g. `"Codex token total: ..."` / `"...so this is not Codex's total"`
     rather than reusing the run-wide `"Run token total"` / `"the run
     total"` copy. `describeRunTokenUsage`'s own wording, exported signature,
     and every completeness rule are unchanged (still `"Run token total"` /
     `"the run total"` verbatim); no existing run-wide test needed updating
     beyond the two provider-bucket fixture tests that asserted the old,
     incorrect shared wording.
  - Checks actually run for the correction: frontend 563/563 tests (+7 new:
    5 own-property regression cases in `describeTokenUsage.test.ts` and
    `ProviderTokenUsageSummaries.test.tsx`, 2 provider-vs-run-wide wording
    cases), `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing
    warnings (0 new), `vite build` production build passed, `git diff
    --check` reported no whitespace errors. Backend was not rebuilt or
    retested for this frontend-only change.
  - Codex independently inspected the correction diff and ran frontend
    563/563 tests, TypeScript/production build, lint (exit 0 with existing
    warnings), and diff check; backend tests were not rerun for the
    frontend-only correction. Codex recorded **GO** in the
    [planner handoff](planner-handoff.md).
- Next action: the owner approved the bounded collaboration-input provenance
  execution slice in [planner-handoff.md](planner-handoff.md). Claude may
  implement that slice; Codex retains review and final acceptance. Later
  planning needs no separate permission, but later execution does.
- Remaining uncommitted work at the correction handoff: none. Remote
  synchronization remains separate and must be verified before relying on
  `origin/main` as this baseline.

## Prior delivered baseline (2026-09-26): shared instructions and planner handoff

- Branch: `main`. Delivered baseline commit:
  `c914c19ba6b9d8d8f61cbbdaad220a5d46884172` (`docs: unify agent
  instructions and add planner handoff`). Later handoff-only decision commits
  do not change the product baseline.
- Documentation-only follow-up: [AGENTS.md](../../AGENTS.md) is the shared
  project-instruction source; [CLAUDE.md](../../CLAUDE.md) imports it while
  retaining the engineering-context and delivery-handoff imports. The new
  [planner/reviewer handoff](planner-handoff.md) records the separate decision
  state and new-chat resume procedure without copying delivery history.
  The [README](../../README.md) indexes this arrangement.
- At creation, the working tree is clean and there is no remaining uncommitted
  work. The commit is local; remote synchronization must be checked on resume.
  No backend, frontend, migration, adapter, or product behavior changed.
- Checks actually run: staged diff and whitespace audits, changed-document
  relative-link and Claude import-target checks. Code test, build, EF, and
  generated-client checks were not rerun for this documentation-only change.
- Subsequent decision: the owner-authorized Provider allowance evidence slice
  stopped at its provider-contract gate without implementation, tests, or
  acceptance. The current checkout contains no uncommitted code, tests, or
  migrations from that slice. See the [planner handoff](planner-handoff.md)
  for the decision and remaining blocker. Provider account-usage observation
  and guardrails remain open.
- Next action: Codex proposes a different bounded Increment 4 slice for owner
  approval. No execution slice is currently approved; Claude must not resume
  the stopped slice or build `Unknown`-only scaffolding from its old prompt.

## Prior delivered baseline (2026-09-26): candidate-specific invocation-time fit

- Commit: `17254808e0920743b0cc214900c57a6a3d100c4d` (`feat: add
  candidate-specific invocation-time fit to the cockpit`). Its parent is
  `ba186ab22a0b5a3846192eaa971493ffa77eb2a2` (`feat: add known global
  agent-claim block to the cockpit`).
- At that delivery, `HEAD` and `origin/main` resolved to the candidate-fit
  commit and `git status --short` was empty. Remaining uncommitted work: none.
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
