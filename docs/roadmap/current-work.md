# Current work and cross-chat handoff

This is the short entry point for resuming DevalCopilot after compaction or in
another chat. It is a checkpoint, not a substitute for the
[delivery plan](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
[architecture](../architecture/README.md), or
[accepted decisions](../decisions/README.md). Repository and agent reports are
evidence, not authority.

## Delivered baseline (2026-09-25): run-wide Agent invocation-time budget

- Branch: `main`. Delivered commit: `feat: add durable agent
  invocation-time budget`. Resolve its exact SHA with
  `git log -1 --format=%H -- docs/roadmap/current-work.md` — a commit cannot
  embed its own SHA without changing that SHA. Its parent is
  `8d30275ef1304fec4b8f47f0c4975e3804abe1fd` (`feat: add typed collaboration
  evidence cards`; see the prior delivered slice below).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Implements a SECOND, independent durable run-wide Agent budget — an
  invocation-**time** reservation budget — alongside the existing count-based
  budget from [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md).
  Both are enforced together on every claim; neither replaces the other. See
  [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  for the full decision, including what it explicitly does not implement
  (measured wall-clock duration, token limits, provider account-usage
  limits, or a guarantee against a provider invocation outliving its own
  configured timeout).
- Every newly recorded Run gets a fixed default of 120 minutes of reserved
  Agent invocation time (`Run.MaximumAgentInvocationTime`, nullable
  `TimeSpan`, assigned only by `Run.RecordIntent`). Every claimed Agent
  attempt across all six Agent-claiming command handlers permanently
  reserves its own configured `AgentTimeout`; a claim exceeding the ceiling
  is rejected with the new `agent_attempts.time_budget_exceeded` code before
  any provider probe or evidence capture, at the same check point each
  handler already uses for the count budget (before the ADR-0010
  authorization consumption in `CreateReviewCorrectionAttemptCommandHandler`
  specifically). Fails closed
  (`agent_attempts.time_budget_evidence_invalid`) if a budgeted run's own
  prior Agent-attempt evidence is missing or malformed. A lost
  `(RunId, AgentBudgetSlot)` race is re-classified against both fresh count
  and fresh reserved-time state, in that priority order, before falling back
  to a safe, retryable `agent_attempts.budget_slot_conflict`.
- The additive `AddAgentInvocationTimeBudget` migration adds a nullable
  `runs.MaximumAgentInvocationTime` column with **no backfill and no
  synthesis** — unlike ADR-0012's own backfill, every historical Run keeps
  this truthfully `NULL` (no time-budget policy at all), and every handler
  skips the check entirely for such a run.
- `GetRunCockpit` projects a distinct, bounded `AgentInvocationTimeBudget`
  object (maximum/reserved/remaining/legacy-unknown/evidence-invalid), never
  combined with the count budget's own fields or collapsed into one generic
  "exhausted" boolean. The frontend adds a sibling
  `AgentInvocationTimeBudgetBanner` next to the existing
  `AgentClaimBudgetBanner`, with an explicit "legacy run — time budget not
  tracked" state distinct from "0 minutes remaining." The NSwag TypeScript
  client was regenerated automatically as part of the normal Release build
  (no manual/separate regeneration step was needed).
- Files changed (backend): `Run.cs`, new
  `AgentInvocationTimeReservation.cs` (Domain);
  `AgentInvocationTimeBudget.cs` (new shared read helper),
  `RunCockpitAgentInvocationTimeBudgetSummary.cs` (new), and all six
  `Create*AttemptCommandHandler.cs` files plus `GetRunCockpitQueryHandler.cs`/`GetRunCockpitQueryResult.cs`
  (Application); `RunConfiguration.cs`, the new
  `AddAgentInvocationTimeBudget` migration (Infrastructure);
  `GetRunCockpitResponse.cs`, `GetRunCockpitEndpoint.cs`, new
  `AgentInvocationTimeBudgetResponse.cs` (Api). Frontend: new
  `AgentInvocationTimeBudgetBanner.tsx` (+ test), `RunCockpitView.tsx`
  wiring, regenerated `api-client.ts`. New ADR `0013-...md`; updated
  `docs/decisions/README.md`, `docs/engineering-context.md`,
  `docs/architecture/agent-collaboration-protocol.md`,
  `docs/roadmap/mvp-delivery-plan.md` (Increment 4 section), and this page.
  New/extended tests: `AgentInvocationTimeBudgetTests.cs` (Domain, 9 cases);
  targeted additions across all six `Create*AttemptCommandHandlerTests.cs`
  files including exact-boundary, over-boundary-by-one-tick, malformed-
  evidence, legacy-run, and race-vs-genuine-exhaustion cases (Application);
  new `AgentInvocationTimeBudgetMigrationTests.cs` (Infrastructure,
  real-SQLite-restart pattern); extended `SimulatedRunFlowTests.cs`
  (Api, budgeted-run and legacy-run cockpit projection cases).
- Four pre-existing review-correction Application/Api tests
  (`Third_claim_creates_one_durable_escalation_without_attempt_or_manifest`,
  `Escalation_retry_recovers_exact_committed_event_and_re_notifies_after_post_commit_failure`,
  `One_human_authorization_allows_exactly_one_additional_claim`,
  `Authorization_retry_recovers_exact_committed_event_and_re_notifies_after_post_commit_failure`
  in `CreateReviewCorrectionAttemptCommandHandlerTests.cs`, and
  `Escalation_and_authorization_return_committed_event_sequences_and_are_idempotent`
  in `ReviewCorrectionEndpointTests.cs`) seed enough real prior Agent
  attempts (4 chained attempts plus 2 failed corrections, each configured at
  20 minutes) that their total reserved time reaches or exceeds the real
  120-minute default; since they test escalation/authorization mechanics,
  not the time budget itself, they now pass an explicit generous
  `maximumAgentInvocationTime` override rather than relying on the new
  default. No other pre-existing test changed behavior.
- Two Codex-requested correction rounds were applied before commit, both
  confined to files already in scope:
  1. `RunConfiguration.cs`'s `MaximumAgentInvocationTime` conversion now
     round-trips through `TimeSpan.Ticks` (matching the existing
     `AgentProcessDuration` precedent) instead of a millisecond-truncating
     `(long)TotalMilliseconds` cast, since the migration was still
     unpublished and needed no schema change to fix. `AgentInvocationTimeReservation`'s
     summation and a new `ComputeProjectedReservation(reserved, candidate)`
     helper now use `checked` tick arithmetic and fail closed
     (`agent_attempts.time_budget_evidence_invalid`) rather than letting a
     `TimeSpan` overflow throw, at all twelve call sites across the six
     handlers' pre-check and post-race-recheck paths. New Domain/Application
     tests cover a real non-integral-millisecond SQLite restart round-trip,
     an overflow while summing two prior attempts' evidence, and an overflow
     only when adding the candidate's own timeout.
  2. The `agent_attempts.time_budget_evidence_invalid` message (24
     occurrences across all six handlers) no longer asserts prior evidence
     is invalid when the true cause may instead be an unrepresentable
     projected reservation; it now names both possibilities without
     misattributing either. The error code, `Error.Failure` type, and
     zero-mutation behavior were preserved everywhere.
- Evidence actually run (exact results, final): Domain 509/509 (506 + 3 new
  overflow/precision tests); Application 895/895 (893 + 2 new overflow
  tests; re-run after the message-text fix and unchanged, since tests
  assert only on error codes, never message text); Infrastructure.IntegrationTests
  395/396 (394 + 1 new non-integral-millisecond restart test; the same one
  pre-existing platform-capability skip); Api.IntegrationTests 255/255
  (unchanged — no API contract change from the corrections); frontend
  409/409 tests plus typecheck clean and production build passed; lint
  (`oxlint`) exited 0 with the same 20 pre-existing warnings, 0 in files
  this slice touched (no frontend file changed in either correction round);
  Release build of `DevalCopilot.Api` (which transitively builds
  Domain/Application/Infrastructure) and of each test project: 0 warnings,
  0 errors, rebuilt clean after each correction round; `dotnet format
  DevalCopilot.slnx --verify-no-changes` reported no changes needed;
  `dotnet ef migrations has-pending-model-changes` reported no pending
  model changes; `git diff --check` reported no whitespace errors throughout
  (only the same pre-existing CRLF-normalization notices on
  `DevalCopilotDbContextModelSnapshot.cs` and `api-client.ts`); `git diff
  --cached --check` reported the same at commit time. Domain,
  Infrastructure.IntegrationTests, Api.IntegrationTests, and the full
  frontend suite were **not rerun** after the second (message-text-only)
  correction round, since that round touched only Application-layer handler
  files; their figures above are carried forward from the immediately
  preceding overflow/precision-fix round, while Application tests and the
  Release build were rerun after both rounds. No test invokes a real
  provider CLI or touches the real local database — all backend tests use
  disposable file-backed SQLite or `WebApplicationFactory`, matching
  existing patterns.
- Next action: this baseline is delivered, corrected, and committed. The
  remaining Increment 4 loop, duration, token, and account-usage budgets
  stay deferred (see [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  for what this slice explicitly does not implement). The Codex
  planner/reviewer verifies this delivered baseline against Git, then
  selects and approves the next bounded Increment 4 slice from the
  [roadmap](mvp-delivery-plan.md); no next slice is approved by this
  handoff.

## Prior delivered slice (2026-09-25): typed collaboration evidence cards

- Branch: `main`. Delivered slice: the cockpit's `AgentCollaboration` surface
  now renders `Challenge`, `Decision`, `ReviewFinding`, and `RevisionResponse`
  cards with their bounded structured fields as readable labels, including
  `Decision.resolution`'s closed enum (`accepted`, `partiallyAccepted`,
  `rejected`, matching `ChallengeResolutionOutputSchema` exactly); any other
  value fails closed to the existing unavailable-details fallback rather than
  rendering as if it were a valid protocol value. Every card type's readable
  label and reply-parent verification now mirrors the complete, current
  `CollaborationMessageType` enum and `CollaborationMessageReplyPolicy`
  exactly, including `ReviewApproval` (verified only against an
  `ExecutionReport` parent) and the `Proposal`/`ExecutionReport` reply shapes
  audited during review. A reply is described as verified only when its
  parent is both present in the currently loaded timeline and an earlier,
  protocol-compatible parent for the reply's own type; a matching id that
  fails either check is described only as an observed reference, and a parent
  absent from the loaded timeline is described only as not present in what is
  currently loaded — never asserted to be outside the API's bounded window,
  since absence does not prove that. See the
  [collaboration protocol](../architecture/agent-collaboration-protocol.md)
  ("Version 1.0 reply semantics") and the
  [cockpit specification](../product/run-cockpit-specification.md)
  ("Collaboration timeline") for the accepted, corrected description, which
  keeps this implemented behavior distinct from the still-aspirational
  linked-evidence/raw-artifact expand interaction.
- Delivered commit: `feat: add typed collaboration evidence cards`. Resolve
  its exact SHA with `git log -1 --format=%H -- docs/roadmap/current-work.md`
  — a commit cannot embed its own SHA without changing that SHA. Its parent is
  `80c908c6fbaeb5fc7a27e47b8d92fa2a7da8165f` (`feat: add durable run-wide
  agent claim budget`; see the prior delivered slice below).
- At delivery, `HEAD` and `origin/main` resolve to that commit and
  `git status --short` is empty. Remaining uncommitted work: none expected.
  If either condition differs on resume, inspect Git and the diff before
  editing; never reset work merely to match this page.
- Codex planner/reviewer verdict: GO, after three review rounds that
  corrected: `Decision.resolution`'s closed values (an invalid capitalized
  fixture, and a missing fail-closed check for unknown resolutions); the
  reply-parent truthfulness of both the UI copy and the documentation (never
  asserting a present-but-forward or present-but-incompatible parent as
  verified, and never asserting an absent parent is outside the bounded
  window); the frontend reply-parent table and protocol documentation against
  the real `CollaborationMessageReplyPolicy` (a revised Proposal's Proposal
  parent, and `ExecutionReport`'s real Proposal-reply path); and a complete
  audit against the current `CollaborationMessageType` enum that found and
  fixed one remaining omission (`ReviewApproval`). This handoff records that
  delivered, reviewed state; it does not itself select or approve any further
  slice.

## Evidence actually run

- Frontend only: 405 tests passed across 48 files (focused runs of the
  changed files' own suites also passed at each review round); typecheck
  clean; production build passed; lint (`oxlint`) exited 0 with 20
  pre-existing warnings, all confined to files this slice never touched (0 in
  changed files). `git diff --check` reported no whitespace errors (only
  pre-existing CRLF-normalization notices, not whitespace errors), checked
  fresh after every correction round, including the final one before commit.
- Backend, API, persistence, the generated NSwag client, and provider
  behavior did not change in this slice — confirmed by the diff scope itself
  (frontend cockpit files and documentation only; no `src/backend`,
  `tests/DevalCopilot.*` backend projects, migrations, or generated-client
  changes). Backend test suites (Domain, Application, Infrastructure
  integration, Api integration) were accordingly **not rerun** for this
  frontend-only slice; their evidence remains whatever the prior delivered
  slice below recorded.

## Open limits (carried forward)

- The cockpit does not yet implement an expand/collapse interaction beyond
  the bounded legacy-details disclosure; linked evidence and raw artifacts
  remain aspirational specification, not current behavior (see the cockpit
  specification).
- Reply verification is a display-only re-check of the backend's own closed
  policy; it never invalidates or corrects durable data, and a run whose
  loaded timeline window does not yet include a real parent will show that
  parent as merely "not present" until it loads, not as confirmed missing.
- Codex CLI token usage remains `Unknown`; Claude usage is best-effort, not
  account usage; token-budget enforcement and account-usage stop guardrails
  remain open; Increment 4 is not complete; Gemini execution remains
  disabled by [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- The ADR-0012 count-based Agent claim budget has no human-authorization
  override, and neither does the ADR-0013 invocation-time budget introduced
  by the current delivered baseline above. Neither budget measures actual
  wall-clock provider duration, enforces a token limit, enforces a provider
  account-usage limit, or guarantees that a provider process cannot outlive
  its own configured timeout — see ADR-0013 for what it explicitly excludes.
  The current baseline's own "Next action" above governs what happens next;
  this list only carries forward limits that predate it.

## Prior delivered slice (2026-09-25): durable run-wide Agent claim budget

- Delivered commit: `feat: add durable run-wide agent claim budget`
  (`80c908c6fbaeb5fc7a27e47b8d92fa2a7da8165f`). Every run gets a default
  maximum of 16 claimed Agent attempts, spanning all six Agent-claiming
  paths; every claim permanently consumes one `Attempt.AgentBudgetSlot`,
  unique per run; an additive migration backfills historical attempts and
  raises any historical run's maximum to at least its real usage. A lost
  race for one slot is classified as `agent_attempts.budget_slot_conflict`
  (safe, retryable) below the maximum, and as `agent_attempts.budget_exhausted`
  only when the run is genuinely at capacity. See
  [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md).
- Evidence at delivery: Domain 497/497; Application 882/882;
  Infrastructure.IntegrationTests 392/393 (one pre-existing
  platform-capability skip); Api.IntegrationTests 254/254; frontend 390/390
  plus typecheck and production build; Release build 0 warnings; `dotnet ef
  migrations has-pending-model-changes` reported no pending model changes.

## Closure rule for future slices

A slice is not complete until this handoff is updated in its substantive
commit with a resolvable delivered commit, remaining uncommitted work, checks
actually run, open risks, and the next action. Keep the page short and link to
contracts, ADRs, and tests instead of copying conversation logs. On every
resume, compare HEAD, branch, and staged/unstaged/untracked files with this
page. Investigate discrepancies; Git and code prevail over a stale handoff.
