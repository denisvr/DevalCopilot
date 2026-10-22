# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: LOCAL-QUEUE-01 queued local execution (ADR-0062)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**Delivery phase: correction 1 returned by the Claude executor after review 1 NO-GO, unstaged, uncommitted and unpushed. No GO exists.**
Baseline: `main`, `HEAD`, local `origin/main` and the live ref were `c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1`, divergence 0/0, real index
empty; the three planner inputs (handoff, `decisions/README.md`, new ADR-0062) were preserved and not edited. LOCAL-INSPECT-01 below is
historical and was not reopened. ADR-0050 stays Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every standing
restriction stays.

### What exists

- **Domain / schema.** Admission mode, the `WaitingForAdmission` state, `LocalExecutionAdmission` (Waiting, Reserved, Consumed, Retired,
  Unresolved), a feature-owned guarded Int64 counter, database guards and the migration `AddLocalExecutionQueue` (with manual SQLite rebuild).
- **Application.** Separate queue availability and intake with their own confirmation, replay and conflict by mode, the 64-live-cycle bound,
  keyset observation, the global evaluator, atomic reservation creating the ordinary Reserved step and token, ownership predicates counting
  reserved and unresolved admissions in every entrance, transfer in the claim's own save, retire-or-hold for Stop, deadline and startup.
- **API.** Two authorized MVC operations (queue availability GET with the single strict query key, queue POST with the strict 1 KiB body), mode
  and queue disclosure in the receipt, status and inspection header, regenerated client (build-stable).
- **Frontend.** Mode-aware decoders, hooks and panel (both offers read separately, the mode is part of draft, visit and request owner), truthful
  waiting disclosure with no promised start, event labels, the workspace owner status `PreparationReserved`, the inspection mode.

### Evidence (logs under `logs/executor/local-queue-01/`, git-ignored)

- **Fresh, final matrix:** Domain 1,757; Architecture 192; Application full 7,871 passed and 1 failed; Infrastructure full 3,360 passed, 4 skipped, 3
  failed; Api full 3,260; Vitest 7,398; lint, build and strict e2e `tsc` pass; the generated client hash is unchanged after a non-incremental
  Api build (0 warnings, 0 errors); `git diff --check` clean; `dotnet format` has 22 findings, 21 in files this slice does not touch (recorded as
  pre-existing) and one in the new `LocalExecutionQueueHost.cs`, fixed and verified.
- **Original failures retained:** the 1 Application failure (event-type allow-list lacked the two new events) and the 3 Infrastructure failures
  (migration Down table lists lacked the two new tables) were expected drift, fixed in tests only, and pass on targeted reruns; the earlier five
  Api drift failures are in `api-existing-1.log`. A first frontend build failed on type errors in a new test (Vitest does not typecheck), fixed.
- **Journeys, run one at a time and each green:** local-execution (33 s), planning-sequence (20 s), collaboration (1.9 m) and the new
  `local-execution-queue.journey.ts` (38 s: one queue POST, twelve consumed admissions in order, no Human approval or commit). The queue journey's
  first attempt failed because the provider double accepts the planner root only for a repository named `local-execution-source`; it uses that name.
- **Four compiling controls (`controls.log`; each mutant killed, hashes restored, touched, rebuilt):** 1 omit Reserved from ownership, 17 of 71
  queue tests failed; 2 remove the admission consumption in the claim save, 20 of 71 failed (the first attempt was an invalid CS9113 mutant,
  retained as `control-2-INVALID-attempt-CS9113.log`); 3 startup leaves a waiting admission, the startup test failed 1 of 5; 4 the availability
  visit key ignores the mode, 1 of 4 hook tests failed. Control 4 is the nearest honest equivalent of the requested ended frontend owner guard.
  The first rebuild after the controls failed on a parallel clean build and on a transient file lock (CS2012; the compiler named Microsoft Defender as a possible holder, which is unproven); the retry was clean and
  the queue, startup and hook tests passed again afterwards.
- **Not run:** no real provider, native, profile, helper or credential experiment; no quarantine release, process cancellation or Human approval.

### Correction 1 (Codex review 1 NO-GO: LCQ-R1 P2, LCQ-R2 P2, LCQ-R3 P2, LCQ-R4 P3, LCQ-V1 P2)

Returned unstaged, uncommitted and unpushed; no GO exists. ADR-0062's authority, budgets, quarantine and frozen-experiment boundaries are unchanged.

- **LCQ-R1 fixed.** One shared identity predicate (`LocalExecutionReservationOwnership.Coherent` / `IdentityHoldsAsync` / `KindMappingHolds`) now proves run→project,
  workspace, pinned and source checkpoint with fingerprint, the complete admission/step tuple and the closed kind mapping, and is used by ownership counting,
  retirement (Stop, preparation expiry, startup, settled failure) and `ProveOwnAsync`, which additionally proves every ticket-carried identity (owner token,
  reserved operation, source checkpoint, report, subject attempt, recipe, ordinal, kind). Contradictions stay held/Unavailable; no external observation or
  current eligibility is required. Permanent: `LocalExecutionQueueIdentityTests` (25). With the new checks removed 7 fail (`correction-1/r1-red.log`).
- **LCQ-R2 fixed.** `AddLocalExecutionQueue.Down` first aborts through the named CHECK `held_reservations_block_downgrade` when any Reserved or Unresolved
  admission exists, before any guard, row, table or history change; no release mechanism. Tests prove table, guards, counter and history are preserved, that a
  downgrade is allowed again once nothing is held, and the existing waiting-only reversal still passes (`LocalExecutionQueueMigrationTests`, 35).
- **LCQ-R3 fixed.** The advancing feed carries a fixed high-water (`AdvanceableLocalExecutionSequencesPage`, `LocalExecutionAdvancingSweep` used by the
  coordinator). `LocalExecutionAdvancingSweepTests` drives 34 old records, then 64 arrivals across sweeps, and proves the finite range, the revisit of the oldest
  record and its elapsed two-hour deadline ending it NeedsAttention.
- **LCQ-R4 fixed.** `RowExtensions` moved to `RowExtensions.cs`, unchanged.
- **LCQ-V1 done.** `e2e/local-execution-queue.journey.ts` now sets the limit to one through its rendered control, holds a second project's rendered manual
  verification with the owned gate file `fixture/verify.hold` (verification double only, two pinned contract tests, no production hook, no SQL seeding), queues
  through the rendered confirmation, proves WaitingForAdmission/WaitingForCapacity with zero invocations and no steps over a held interval, releases only the
  gate, observes host progression to Awaiting Human approval with exactly one browser request after queueing, and reloads. The journey exposed a real UI defect,
  fixed: workspace/capacity withholding disabled the queue offer; `queueWithheldReason` now withholds queueing only for a conflicting planning sequence (panel test added).
  Permanent no-resume regression: `LocalExecutionQueueStartupNoResumeTests`.
- **Controls (`correction-1/controls.log`, hashes restored, touched, rebuilt):** 5 startup leaves waiting authority live (1 of 2 fail); 6 advancing sweep ignores the
  high-water (1 of 3); 7 downgrade refusal removed (4 of 35). The earlier controls 1-4 stand (retirement omission is supplementary; mode-visit-key accepted).

**Fresh final evidence (runner durations):** Architecture 192 (8 s wall); Infrastructure Persistence plus transaction classes 750 passed (2 m 3 s); full Application 7,902 passed
(15 m 53 s); full Api 3,262 passed (11 m 9 s); whole Vitest 7,399 (296 files); lint, build, strict e2e `tsc` and `test:harness` pass; generated client hash unchanged
(`ba3b20f58e4aa132`); journeys run one at a time: local-execution 34 s, planning-sequence 22 s, collaboration 116 s, queue/contention 52 s, all green; changed-file format clean.
**Retained (unaffected):** Domain 1,757. **Not run:** full Infrastructure adapters (migration change scoped to Persistence), Domain rerun.
**Historical failed attempts retained:** the first-return full Application (1 failed) and Infrastructure (3 failed) runs; five Api drift failures; CS9113 invalid control;
parallel-build and CS2012 build failures (Defender is only a possible attribution); contention journey attempts 1-4 (`journey-contention-1..4.log`: missing host binaries,
queue offer withheld by the capacity gate, an Authorize-button assertion that was wrong at the limit, a wrong invocation count). The reviewer's temporary probe is not counted.


#### LCQ-R1 follow-up (planner-found recipe omission) and final-source validation

- **Finding reproduced, then fixed.** `ProveOwnAsync` accepted `ticket.RecipeId == null` for any step, so an otherwise exact Reserved Verification ticket with its recipe
  omitted bypassed the recipe comparison. New permanent regressions on a real queued Verification reservation (`LocalExecutionQueueIdentityTests`): the exact ticket
  excludes only its own reservation (workspace Idle, zero capacity owners), the omitted-recipe ticket and a substituted-recipe ticket are refused by BOTH the workspace and
  the capacity policy. Control 8 (`correction-1/controls.log`) restores the old predicate: exactly the omission test fails (1 of 28); hash restored.
- **Closed ticket shape and the factory.** Verification requires the exact pinned recipe; Agent steps require null. Evidence that shaped the fix: both production
  reservation sites always built tickets with `RecipeId = null`, so the stricter proof alone would have refused every legitimate queued verification. The new
  `LocalExecutionStepTicketFactory` is now the single creation point (queued `ReserveLocalExecutionAdmission` and Immediate `ObserveLocalExecutionSequence`, whose `Reserve`
  became `ReserveAsync`); it reads the recipe's `VerificationCommandId` for a Verification step. Existing claim-side defenses are unchanged.
- **Stable dispositions:** LCQ-R1 fixed (including the omission case); LCQ-R2 fixed; LCQ-R3 fixed; LCQ-R4 fixed; LCQ-V1 done (contention journey and no-resume regression).

**Final-source results (sources include the factory change):**

- Fresh: Architecture 192/192 (9 s, `final2-architecture.log`); full Application 7,905/7,905 (930 s, exit 0, `final2-application.log`); queue classes 104/104 before that run.
- **Interrupted attempt, preserved:** `final2-api.log` has no terminal result (the session ended mid-run): 158 failed cases, 152 of them `git init -q failed:` with empty stderr, the rest
  `GitInvocationFailed`/conflicts from Git-dependent seeding; all in `LocalCommit` classes, beginning as one burst about 4 m 32 s in. Counts are log observations, not a suite total.
  No runner was active afterwards; `git init` works by hand; no cause was proven. TEMP holds about 7,100 leftover `devalcopilot*` directories (not cleaned here).
- **Bounded diagnostic:** the whole `Features.Runs.LocalCommit` namespace run alone, serially: 787/787 passed in 10 m 17 s (`diag-localcommit-api.log`), no Git failure.
- **Full API on the final sources, serial, new log (`final3-api.log`): FAILED 2, passed 3,260, total 3,262, 13 m 52 s.** The failures are
  `ProviderFixtureContractTests.A_working_directory_that_is_not_an_owned_worktree_is_refused_for_every_mutating_or_reading_role` (`IOException: The pipe is being closed` while the test
  writes stdin to a double that already refused and exited) and `AgentSupervisionContentionTests.The_sweep_stops_within_its_bound_while_a_real_writer_holds_the_database_and_later_records_each_attention_once`
  (teardown could not delete a planning harness database still in use). Both passed 3 of 3 times when rerun alone together with their classes (`diag-two-failures-1..3.log`, 4 tests, 31 s each).
  They touch neither the ticket factory, the ownership proof, nor queue code, but a timing-sensitive race in each is a plausible reading, not a proven cause. **No clean complete API run exists
  on the final sources**; this is a retained limitation, and the earlier clean full API run (3,262) is scoped to the sources before the factory change.
- Journeys, one at a time on the final sources after rebuilding the journey host (`final3-*`): local-execution 50 s, planning-sequence 25 s, collaboration 122 s, queue/contention 58 s, all green. A previous
  `final2` journey leg never ran (interrupted).
- **Retained at recorded scope (unaffected by the factory change):** Persistence plus transaction classes 750/750; Domain 1,757; whole Vitest 7,399, lint, build, strict e2e `tsc`, `test:harness`,
  generated-client hash `ba3b20f58e4aa132`; controls 1-7 and 8 above.
- **Not run:** a clean complete API pass on the final sources; full Infrastructure adapter suites; Domain after the correction.


### Correction 2 (Codex review 2 NO-GO: LCQ-V2 P2, test validation and lifetimes)

Test-only; no production source changed. Returned unstaged, uncommitted and unpushed; no GO exists. Dispositions: LCQ-R1 to LCQ-R4 and LCQ-V1 stay resolved; **LCQ-V2 fixed**.

- **Stdin helper (confirmed defect).** `ProviderFixtureContractTests.Run` wrote stdin before observing the child, but the real fixture checks ownership before reading stdin, so a
  legitimate early refusal closed the pipe first. `Run` now starts the output reads first, records an `IOException` from the stdin write and rethrows it only when the child
  exited with code 0; a refusal (non-zero exit) stays observable with its exit code, output and no-effect assertions. The fixture is unchanged and no IOException is blanket-ignored.
  Permanent regressions (`ProviderFixtureContractTests.EarlyRefusal.cs`): a 1 MiB payload to an early ownership refusal (exit 65, no invocation log) and a passing verification with an
  undelivered megabyte, which must still throw. Red before the helper fix: 1 of 2 failed with `The pipe is being closed` (`correction-2/stdin-red.log`); green after: the
  `ProviderFixture` classes 215/215 (`stdin-green.log`).
- **Contention teardown (source-level lifetime gap, exact holder unproven).** The production sweep deliberately lets a call that outlived its bounded stop keep running in its own scope. New test-owned
  evidence `TrackingScopeFactory` (a decorator counting created and fully disposed scopes, via a `CreateSweep(provider, scopeFactory)` overload) lets the test prove completion by the scopes' own
  disposal, never by a delay. Permanent regressions: `A_stopped_sweeps_pending_call_outlives_the_bounded_stop_and_its_scope_settles_only_once_the_writer_releases` (stop under 3 s, `Active > 0` after stop,
  0 after the writer releases) and the main contention test now awaits both sweeps' scopes before the harness deletes its database. Production shutdown bound, late-write fences, exactly-once
  attention and the Attempt assertions are unchanged; no sleeps, swallowed deletes, repeated deletes or pool clearing were added. A temporary end-of-test diagnostic recorded 0 pending scopes in each of 6
  isolated runs (`pending-at-end.txt`; removed, hash restored), so the failed full run's exact lock holder is not identified and remains unproven; the lifetime gap itself is demonstrated by the source and the new
  regression. The contention class passed 3 of 3 afterwards (`contention-green-1..3.log`, 4 tests each).
- **Final-source validation (sources include both test corrections):** Architecture 192/192 (3 s); `AgentSupervisionContentionTests` plus `ProviderFixture` classes 219/219 (58 s); **one complete full API run,
  serial, new log: 3,265/3,265 passed, 11 m 41 s, exit 0 (`correction-2/final-api.log`).**
- **Retained at recorded scope (inputs unchanged by this correction):** full Application 7,905/7,905; Persistence plus transaction classes 750/750; Domain 1,757; whole Vitest 7,399, lint, build, strict e2e `tsc`,
  `test:harness`, generated-client hash `ba3b20f58e4aa132`; the four serial final-source journeys (local-execution 50 s, planning-sequence 25 s, collaboration 122 s, queue/contention 58 s). The fixture
  executable and the journey harness were not changed in this correction (only test helpers), so the harness checks and journeys were not rerun.
- **Preserved earlier attempts:** the first-return full Application/Infrastructure failures; five Api drift failures; CS9113 invalid control; build-collision and CS2012 failures (Microsoft Defender named by the
  compiler as a possible holder, unproven); contention journey attempts 1-4; the interrupted `final2-api.log` (158 reported failed cases, 152 `git init -q failed:`, no terminal result, cause unknown); the
  `final3-api.log` run (3,260 passed, 2 failed, pre-correction-2 sources) with its isolated reruns; and one transient build with 6 errors during this correction (log not captured; the next build was clean).
- **Remaining limitations:** the exact cause of the 158-case Git burst and of the full-run database lock is unproven; Domain and the full Infrastructure adapter suites were not rerun; about 7,100 leftover
  `devalcopilot*` temporary directories were left untouched.
- **Reconciled inventory:** `main`, `HEAD` and local `origin/main` are `c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1`; index empty; 107 modified and 58 untracked with full expansion. Against review 2's 104/56 the additions
  are the modified `ProviderFixtureContractTests.cs`, `AgentSupervisionContentionTests.cs` and `AgentSupervisionHostedSupport.cs`, and the new `ProviderFixtureContractTests.EarlyRefusal.cs` and `TrackingScopeFactory.cs`.


### Correction 3 (Codex review 3: LCQ-V2 residual, the sweep-call completion proof)

Test-only; no production source changed. Returned unstaged, uncommitted and unpushed; no GO exists. The stdin fix, the clean complete API run, LCQ-R1 to LCQ-R4 and LCQ-V1 are accepted and untouched.

- **Residual finding.** `TrackingScopeFactory` counts scopes only after `inner.CreateScope` returns, so `WhenIdleAsync` could complete while a real sweep call was still paused during scope
  acquisition (or queued before the factory); the reviewer reproduced `Active == 0` with an incomplete `pendingInventory` (retained probe `review-02/PlannerLocalQueueScopeEntryTests.cs`). This shows an
  insufficient teardown oracle, not the cause of the historical database lock.
- **Correction.** New test-owned `SweepCompletion` (`WaitForSettledAsync`, `SnapshotPending`): it requires the sweep loop's own `ExecuteTask` to have ended, snapshots the sweep's `pendingInventory`
  and `pendingObservations` Tasks by narrow reflection that fails explicitly (never a zero count) when a field is missing or of another type, and waits for all of them to settle (their scopes are
  disposed inside those Tasks) under a separate 20 s teardown bound outside the measured stop. A faulted or canceled call counts as settled. Both stopped sweeps of the main contention test and the
  earlier outlives-the-stop regression now use it before harness disposal; scope counting stays only as supplementary evidence. No production API, shutdown drain, sleep, retry or deletion handling was added.
- **Permanent regressions (`SweepCompletionTests`, 3):** with a gate at scope acquisition the helper is invoked directly and is still pending after a real second while the pending inventory is
  incomplete and `Active` is zero; after the gate is released in `finally` it completes and the call is complete; the helper fails explicitly for a never-started sweep or a loop that has not ended;
  and it fails explicitly (naming `pendingInventory`) for a service lacking the private bookkeeping. Control 9 (`correction-3/controls.log`): a helper that ignores the pending calls fails the gated
  regression (1 of 3); hash restored, touched, rebuilt.
- **Fresh results (final test sources):** `SweepCompletionTests` 3/3 (`completion-green-1.log`); `Features.Runs.AgentSupervisions` plus `ProviderFixture` classes 250/250 in 52 s (exit 0, 60 s wall,
  `affected-green.log`); Architecture 192/192; changed-file format findings none.
- **Retained at the actual scope:** the complete clean API run 3,265/3,265 (11 m 41 s) on the sources before this follow-up (test-source scope: before `SweepCompletion` and the helper switch); full Application
  7,905/7,905; Persistence plus transaction classes 750/750; Domain 1,757; whole Vitest 7,399, lint, build, strict e2e `tsc`, `test:harness`, generated-client hash `ba3b20f58e4aa132`; the four serial
  final-source journeys. **Not run:** another full API run (not required for this test-only follow-up), Domain and full Infrastructure adapters.
- **Preserved:** every earlier failed or interrupted attempt and qualification above, including the `final2` Git burst (158 cases, cause unknown), `final3` (2 failed), the CS9113/build-collision/CS2012 attempts
  (the compiler named Microsoft Defender as a possible holder, unproven) and the transient 6-error build.
- **Inventory:** `main`, `HEAD` and local `origin/main` are `c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1`; index empty; 107 modified and 60 untracked with full expansion (new: `SweepCompletion.cs`, `SweepCompletionTests.cs`).


### Risks and notes for the planner

- The queue journey now proves waiting behind a held owner and release; ordering among several queued cycles is proved by the Application tests, not in a browser.
- The queued journey cannot share an invocation with the existing local-execution journey (same fixed repository name).

## Previous slice (historical record): LOCAL-INSPECT-01 recorded local execution inspection (ADR-0061)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**Delivery phase: correction 1 returned by the Claude executor after review 1 NO-GO, unstaged, uncommitted and unpushed. Consult planner-handoff.md for the subsequent review decision and Git for publication.**
Baseline: `main`, `HEAD`, local `origin/main` and the live ref were `2912490aed8b3e116fb2b0ef744bf80ad6c62a03`, divergence 0/0, real index
empty; the three planner inputs (handoff, `decisions/README.md`, new ADR-0061) were preserved. LOCAL-COORD-01 below is historical and was
not reopened. ADR-0050 stays Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every standing restriction stays.

### What exists

- **Application.** Three mediator queries (`GetLocalExecutionSequenceInspection`, `GetLocalExecutionStepInspection`,
  `GetLocalExecutionVerificationOutputWindow`) over one structural reader and one operation proof in
  `Policies/LocalExecutionInspection`. Bounded untracked scalar projections (database-classified enums, guarded numbers and text, 103 step
  and 33 recipe rows, recipe-dependent step bound, bool flag read as stored bytes), the whole structure and historical ownership proved
  per request, the sequence revision read before and after (no retry), Agent proof by exact link/run/workspace/source/contract, verification
  proof against the pinned recipe including exact ordered arguments under 2 MiB / 128 KiB internal guards, and the window re-proving all of
  it before the existing `VerifyAndReadSealedAsync`. The ADR-0052 projections and admission are reused; correction 1 narrowly fixes their
  stored truncation classification. No Domain, schema, execution admission, dispatch, coordinator, recorder, startup or current-status change.
- **API.** Three authorized bodyless MVC GETs, fixed `local_execution_inspection.not_found / body_not_allowed / query_not_allowed`, an index/step
  guard (no query) and a window guard (only the exact ordinal `fromOffset` and `maxBytes`, once), API-owned graphs (revision a canonical Int64
  string; ADR-0052's stream-descriptor shape reused), regenerated client (build-stable).
- **Frontend.** Strict whole-answer decoders (`localExecutionInspection.ts`), operation-scoped wire clients, `useLocalExecutionInspection`
  (run, opening, index visit, step visit and stream visit lifetimes), `useLocalExecutionInspectionWindow`, the collapsed "Inspect local
  execution" region in the evidence rail and the selected run. Narrow extractions: `AgentAttemptDetail` (moved unchanged out of the attempt
  history panel) and `useRecordedStreamWindow` (the one windowing machine; `useAgentAttemptVerificationWindow` is now a thin wrapper, its
  tests unchanged), plus exports of existing wire helpers. The existing live control reader and every fence are untouched.

### Correction 1 (Codex review 1 NO-GO: LCI-R1 P2, LCI-R2 P3)

- **LCI-R1 fixed.** `RecordedVerificationProjection.ProjectDescriptor` and `ProjectStorage` (shared with ADR-0052) now read the stored `Truncated`
  as bytes (bounded to 8, plus a presence flag); `VerificationOutputDescriptorRow.TruncatedCode` recognizes only NULL, the stored digit 1 and
  the stored digit 0, so 2, -1, 7, 1.5, oversized and other malformed storage is Unavailable before any artifact access. No schema, reader
  duplication, UI masking or weakening. Permanent regressions: new inspection step/window theories (positive and negative integers, REAL,
  TEXT, BLOB, oversized; canonical 0/1 Recorded; sibling stream unaffected; zero sealed-store calls), the existing ADR-0052 evidence and window
  theories extended likewise, and an Api wire regression (3 cases). Red before the fix: 20 Application and 3 Api failures
  (`logs/executor/local-inspect-01/lci-r1-red-*.log`); green after (`lci-r1-green-*.log`). Compiling mutation restoring nonzero-as-true killed
  22 tests (hashes restored, rebuilt; `controls.md` control 4).
- **LCI-R2 fixed.** `CountingArtifactStore`, `RecordingArtifactStore` and `StatementLog` moved to their own files; namespaces and behavior unchanged.
- **Fresh evidence:** Application (reviewer filter) 381 passed in 1m53s; Api (reviewer filter) 152 passed in 1m11s; Architecture full 192 passed;
  changed-file `dotnet format --verify-no-changes`, `git diff --check`. **Retained (unchanged scope):** Vitest 7359, lint, build, strict e2e tsc,
  generated client, and the three journeys (local-execution, planning-sequence, collaboration) from the first return, including their recorded failed
  first attempts. **Not run:** full Application/Api (no wider policy changed; the shared change is one projection read by the ADR-0052 and
  ADR-0061 consumers, all covered above).

### Independent correction review evidence

Codex reconciled the 100-path inventory (26 modified, 74 new): the eight additions to review 1 are three shared production projection/row
files, two existing test files and the three split helpers. No unrelated paths were found. Independent original logs are under
`logs/planner-review/local-inspect-01/review-02/`; commands below run at the repository root:

- `dotnet test tests/DevalCopilot.Application.Tests/DevalCopilot.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~GetAgentAttemptVerificationEvidenceQueryHandlerTests|FullyQualifiedName~GetAgentAttemptVerificationOutputWindowQueryHandlerTests|FullyQualifiedName~LocalExecutionStepInspectionTests|FullyQualifiedName~LocalExecutionVerificationOutputWindowTests'`: 281 passed, runner duration 1 m 30 s (`application-consumers.log`).
- `dotnet test tests/DevalCopilot.Api.IntegrationTests/DevalCopilot.Api.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~LocalExecutionInspectionEndpointStorageTests'`: 8 passed, runner duration 57 s (`api-storage.log`).
- `dotnet test tests/DevalCopilot.Architecture.Tests/DevalCopilot.Architecture.Tests.csproj --no-restore`: 192 passed (`architecture.log`; runner timing in that output).
- A temporary reviewer mutation in `VerificationOutputDescriptorRow.TruncatedCode` restored malformed-value normalization. The compiling filter
  `FullyQualifiedName~A_bad_stream_row_is_unavailable_for_that_stream_only_and_hides_neither_its_sibling_nor_the_verification` failed 7/12 cases;
  exact bytes and SHA-256 were restored and the same `dotnet test` Application command with that filter rebuilt and passed 12/12. Both runs
  report 13 s runner duration; original logs and `mutation-restoration.json` are retained. No lasting product correction was made by Codex.

The executor's correction green logs retain 381 Application, 152 API and 192 Architecture passing outputs; its reported wall timings differ
from runner durations because they include command overhead. Exact original invocation headers are absent. The reported 22/55 compiling
mutation has only its summary locally; the independent control above supplies retained original evidence. First-return complete suite logs
absent locally remain executor-reported results; no originals are reconstructed and no unchanged suite is rerun merely to replace them.

### First-return evidence (historical)

Application: 10 index, 77 step, 24 window and 71 damage-matrix tests over real file-backed SQLite with whole production-driven cycles; Api: 73
tests (matrices over one real hosted cycle plus a storage class); Architecture: 192 (new `LocalExecutionInspectionBoundaryTests`); Vitest:
whole suite; lint, build, strict e2e `tsc`; the existing local-execution journey extended with the inspection section. Three compiling
controls are logged in `logs/executor/local-inspect-01/controls.md` (one equivalent mutant is kept as an invalid attempt). Observation kept
for the planner at that first return: ADR-0052's shared `ProjectDescriptor` read any nonzero stored `Truncated` as true. Correction 1 fixes
that inherited defect as described above. The original report and all failed journey attempts remain retained at their original scope.

## Previous slice (published; historical record): LOCAL-COORD-01 explicitly authorized local execution sequence (ADR-0060)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**State: implemented by the Claude executor (new chat; Codex selected and resolved the slice). Review 1 was NO-GO (LCO-R1 to LCO-R4); correction 1 is below. Returned unstaged, uncommitted and unpushed. No GO exists.**
Baseline: `main`, `HEAD`, local `origin/main` and the planner-verified live ref all `7c78c849fdd556ecf3a539dce479e31fc3cc501b`
(GLOBAL-LIMIT-01, published and independently verified), divergence 0/0, real index empty. The four planner inputs (the handoff,
`decisions/README.md`, ADR-0057 and ADR-0058 status lines) and the new accepted ADR-0060 were preserved. Nothing is staged, committed,
pushed, reset or cleaned. No dependency, standards change, provider experiment, native/helper/profile/credential operation, queue,
executor lease, retry, takeover, release, automatic approval, commit or publication was added. ADR-0050 stays Proposed, P9 unproven, AI Memory
deferred, Increments 4/5/6 incomplete; PCO-C1, ASUP-C1 and WEX-C1 stay accepted.

### What exists

- **Domain / persistence.** `LocalExecutionSequence` (one per run; pinned plan, authority, starting source, confirmation, settings snapshot,
  allowance, plan-input hash, two-hour advancement deadline, state, final references), `LocalExecutionSequenceRecipe` (the whole enabled
  verification set pinned by value) and `LocalExecutionSequenceStep` (kind, pass, recipe position, source, expected report and subject
  attempt, reserved and linked operation, deadlines). Additive migration `AddLocalExecutionSequence` with 15 SQLite guard triggers (live
  fence for attempts, verification, local commit, checkpoints, abandonment, recipe and requested-setting writes, with the exact reserved
  operation exempt; arming, tuple, immutability and bound guards). `AgentOutcome.LocalExecutionSequenceDispatchRefused` (29) and
  `VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused` (7) are appended; the three 32-character status mappings are widened to 64
  in the model (no table rebuild: EF emitted none).
- **Composition.** All six claims accept an internal ticket (`LocalExecutionStepTicket`): predetermined operation identity, confirm-at-seam
  compare-and-set as the first transaction statement, link applied in the claim's own save, independent step probe on uncertainty, manifest
  deleted only when no other worker claimed. Ticket claims refuse at allowance exhaustion before any ordinary exhaustion branch (no escalation
  row, no Human grant read or consumed). Plan eligibility is stricter than an ordinary claim (no unreviewed first revision).
- **Ownership.** One shared `LocalExecutionOwnership` expression (database-side) is used by the workspace policy, the capacity policy and
  the abandonment authority: a linked dispatched operation without coherent host Exited evidence stays an owner although its row is terminal;
  the dispatch refusal status is a recognized terminal; an incoherent link is Unavailable.
- **Coordinator and commands.** Arm, Observe, Claim, Resolve, Stop, InterruptLocalExecutionSequences (startup), the status, availability and
  advanceable queries, `LocalExecutionSequenceCoordinator` (500 ms observation, one worker per sequence, original-process ticket) and the
  dedicated Agent and verification dispatch-refusal recorders handled by all five supervisors. Observation: preparation, result (claim +
  timeout + 2 min, capped) and advancement deadlines; fence-refused undispatched claims end with attention.
- **Startup.** The closure runs right after migration, before any recovery; the generic recoveries exclude linked rows (no seal, import,
  deletion of partials, interruption, run outcome or workspace flag). No supported release exists.
- **API.** Four protected MVC operations with exact-shape filters (arm: five members, 1 KiB), fixed texts, closed responses, safe mapping that
  refuses inconsistent tuples (fixed 422 of the shared contract). Regenerated client reproduced byte-identically.
- **Frontend.** Raw-wire decoders, one run-owned status reader hoisted in `App` and shared by both regions (5 s poll, 10 s abort, coalesced
  reads, change barrier, A-B-A and lifetime protection), availability, arm and stop hooks (single submit, never retried), the confirmation
  panel and status views, the fence on every mutating start in both regions, new outcome/status labels and event summaries, quarantine wording.
- Docs: ADR-0060 (planner), engineering-context, system-overview, data-and-recovery, protocol, ledger, workflow-model, run-cockpit-specification
  and mvp-delivery-plan. The checkpoint trigger and the planning-family early/save-seam fence were fixed during testing (below).

### Evidence (fresh on the final restored sources unless stated)

All logs: `logs/executor/local-coord-01/` (design notes, controls, journeys, final-*.txt).

| Check | Result | Log |
| --- | --- | --- |
| Solution build, non-incremental | 0 warnings, 0 errors | `final-build-solution.txt` |
| Domain, full | 1732 / 1732 | `final-domain.txt` |
| Architecture, full | 185 / 185 | `final-architecture.txt` |
| Infrastructure `Persistence` | 631 / 631 (468 existing + 163 new) | `final-infrastructure-persistence.txt` |
| Application, full | first run 7503 / 7504: `GetRunEventHistoryQueryHandlerTests.Every_known_event_type...` failed because its pinned list lacked the six new event types (log kept); the pinned list was extended and that class then passed 93 / 93. No clean complete Application rerun is claimed | `final-application-full-FIRST-1-failure.txt` |
| API integration, full | 3160 / 3160 (11 m 8 s) | `final-api-full.txt` |
| Vitest, whole | 288 files, 7205 tests passed | `final-vitest.txt` |
| Lint / `npm run build` / strict e2e tsc | exit 0 (existing warnings only) / 0 / 0 (47 e2e files + both configs) | `final-lint.txt`, `final-build-fe.txt`, `final-e2e-tsc.txt` |
| NSwag (client deleted, API rebuilt `--no-incremental`) | byte-identical, SHA-256 `09a57423…0d38` | `final-nswag-before.txt`, `final-nswag-after.txt` |
| New journey `local-execution-sequence.journey.ts` | passed (37 s): one arm POST, no per-stage request, 12 steps, 2 corrections, 3 passes, zero Human approval, commit or remote call, reload read-back | `final-journey-local-execution-sequence.txt` |
| Collaboration, planning-sequence, escalated-plan journeys, serial | all passed (1.9 m, 21 s, 32 s) | `final-journey-*.txt` |
| Changed-file `dotnet format --verify-no-changes` | clean for my edits; `RecordInterruptedAgentArtifactCommandHandler.cs` reports 4 whitespace findings in an argument comment that is unchanged from HEAD | `format1.txt` (scratch) |
| Doc links of the changed documents | 0 broken | (inline check) |

Focused development counts (retained, earlier phases of the same sources): Application `LocalExecutionSequences` folder 443, API `LocalExecutionSequence*`
87 + hosted 18, Domain `LocalExecutionSequence*` 166, Persistence 631 (as above), journey fixture contract tests 211, Vitest cockpit 6931.

**Negative controls** (serial, hash-guarded, restored hashes matched; restored sources touched and the solution rebuilt non-incrementally before the final matrix):

| Control | Result | Log |
| --- | --- | --- |
| 1 lost atomic link (`stepLink?.Apply` removed) | killed: 161 / 189 filtered tests failed. The first attempt (a comment replacement) did not compile (`control-1-FIRST-ATTEMPT-did-not-compile.log`) and is retained | `control-1b-lost-atomic-link-tests.log` |
| 2 wrong pass/recipe identity (element-wise membership replaced by count) | killed: 1 / 158 (`A_pass_whose_recorded_membership_is_not_the_selected_executions_element_wise_is_refused`) | `control-2-membership-by-count-tests.log` |
| 3 lost startup quarantine (linked exclusion removed from the implementation reconcile) | killed: 3 / 13 restart tests | `control-3-lost-startup-quarantine-tests.log` |
| 4 stale frontend confirmation (confirmation dropped from the arm tuple guard) | killed: 1 / 34 (`sends nothing for another run, another tuple or no candidate`) | `control-4-stale-frontend-confirmation-tests.log` |

Source hashes were not used as binary proof.

### Correction 1 (review 1 NO-GO: LCO-R1 to LCO-R4)

Written for Codex review 1 ([review-01](../../logs/planner-review/local-coord-01/review-01/review-01.md) is an ignored local record). Preflight
verified once before editing: `main`, `HEAD`, local `origin/main` and the planner-verified live ref `7c78c849…`, divergence 0/0, index empty, 296 pending
paths (122 modified, 174 added). No staging, commit or push. The reviewer's three failing regressions are retained unchanged
(`logs/planner-review/local-coord-01/review-01/`, `probes-02.log` is their complete output; `probes-01.log` is the empty interrupted invocation, cause unproved).

| Id | Disposition |
| --- | --- |
| LCO-R1 [P2] | Fixed. `LocalExecutionDispatchGate` compares the claimed execution's exact ordered arguments (JSON) with the pinned recipe at the final write-locked gate, next to command id, name, executable and timeout. The marker's feed-versus-marker snapshot equality is unchanged and independent; the pre-dispatch SourceChanged composition is unchanged. Regressions: `LocalExecutionVerificationArgumentDispatchTests` (8: changed, reordered, appended, removed, emptied refused and then closable with no process call; exact match dispatches; an unlinked execution is still judged by snapshot equality; SourceChanged still recorded) and hosted `LocalExecutionSequenceHostedRefusalTests` (drift before the supervisor reads its snapshot: refused, dedicated status recorded, 0 verification process calls, NeedsAttention, ownership released by the never-dispatched refusal). A defect found by the new tests: `RecordVerificationExecutionSourceChangedCommandHandler` did not pass its injected clock to the dispatch authority (fixed; production clocks coincide). |
| LCO-R2 [P2] | Fixed. New `LocalExecutionFinalEvidence` proves the actual ReviewApproval (existence, run, provider-observed provenance, actor, reply, review inputs, clean exit, source, and the exact recorded pass through its verification evidence rows) and both escalations (provider diagnosis Escalation; host exhaustion Escalation: fixed summary, orchestrator actor, host-constructed provenance, reply to a report a step names). It is used by the coordinator before it classifies Approved or ProviderEscalation (a missing/malformed approval now ends NeedsAttention `operation_outcome_invalid`) and by the status read, which now discloses a final reference only when proved and otherwise reads `final: null` with the immutable state and outcomes unchanged (the UI already says the references could not be proved). Regressions: `LocalExecutionFinalEvidenceTests` (35), hosted `LocalExecutionSequenceHostedStatusTests` (protected API: approval removed -> 200 with `final` null; escalation tampered -> `final` null). `An_escalation_that_answers_no_linked_execution_report_is_not_disclosed_as_final_evidence` replaced the earlier status test that seeded an escalation replying to a Proposal. |
| LCO-R3 [P2] | Fixed. New `LocalExecutionRefusalProof` requires sequence/run/project/workspace/source checkpoint and fingerprint/kind/contract identity, the exact reserved-and-claimed step link and no dispatch/process/outcome evidence before startup closes either family and before both dedicated live recorders write; unproved associations stay Running/owned (ownership policies stay Unavailable) with no outcome, no foreign event and no release. Regressions: `LocalExecutionRefusalProofTests` (26, including the reviewer's moved-RunId case) and the hosted restart case. Storage already refuses the Reserved/Revoked/Unresolved and differing-operation link tampering (`ck_local_execution_sequence_steps_operation_link`); those cases assert that refusal. |
| LCO-R4 [P3] | Fixed. Split without namespace or behavior change: `LocalExecutionStepProbeResult.cs`, `LocalExecutionRecipePin.cs`, `LocalExecutionVerdictKind.cs`, `LocalExecutionSequenceVerificationBehavior.cs`, `LocalExecutionSequenceVerificationCall.cs`; a lexical scan of the new feature files finds no multi-type file. |

**Correction evidence (fresh on the corrected, restored sources; logs `logs/executor/local-coord-01/correction1-*`)**

| Check | Result |
| --- | --- |
| Solution build, non-incremental | 0 warnings, 0 errors |
| Domain, full / Architecture, full | 1732 / 1732; 185 / 185 |
| Infrastructure `Persistence` / affected TransactionBoundary classes (ClaimVerificationExecution, CreateCodeReviewAttempt, CreateReviewCorrectionAttempt, VerificationDiagnosis, ReconcileInterruptedImplementationAttempts, RecoverInterruptedVerificationOutputArtifacts, SetCodexAssignmentPreference) | 631 / 631; 22 / 22 |
| Application, full | **7573 / 7573, 12 m 46 s: the first clean complete Application result** (the earlier 7503 / 7504 run and its log stay retained as a distinct failed result) |
| API integration, full | first correction run 3162 / 3164: the new status test I had left incoherent (fixed as above) and `GitHubRepositoryIdentityBindingDurabilityTests.Concurrent_identical_requests_from_separate_hosts_...` (HTTP 409); a rerun after the test fix, 3163 / 3164: `...Concurrent_different_node_identifiers_...` of the same existing class (409 `github_repository_identity_binding.unavailable`). That handler references nothing this slice changed; the class passed 7 / 7 three times in isolation and 115 / 115 with the new classes, and passed in the earlier full runs. This is the same family as the handoff's unexplained concurrent-replay 409 and is not attributed to or excused by this slice; **no clean complete API run is claimed** (logs `correction1-final-api-full-FIRST-2-failures.txt`, `correction1-final-api-full-RERUN.txt`, `correction1-github-binding-reruns.txt`) |
| NSwag (client deleted, API rebuilt `--no-incremental`) | byte-identical, SHA-256 `09a57423…0d38` |
| Journeys, serial, rebuilt host binaries | local execution passed (34 s), collaboration passed (1.9 m), planning sequence passed (21 s) |
| Controls 1 to 3 rerun on corrected sources (hash-guarded, restored hashes matched) | 161 / 189, 1 / 158, 3 / 13 failed as named; log `run-controls-correction1-stdout.txt` |
| Format | no finding outside the 4 unchanged-comment lines of `RecordInterruptedAgentArtifactCommandHandler.cs` (not repaired, as directed); `git diff --check` clean |

Retained (inputs unchanged): frontend Vitest 7205 / 7205, lint, build and strict e2e tsc (no frontend source changed in this correction), control 4. Not run: Infrastructure beyond `Persistence` and the seven
transaction classes, `npm audit`, the stop/restart browser paths, any real provider. Original journey failures/debug output, the noncompiling first control 1 attempt,
the unexplained earlier API disposal failure and every historical qualification stay as recorded.

### Correction 2 (review 2 NO-GO: LCO-R2 completion and LCO-V1)

Written for Codex review 2 ([review-02](../../logs/planner-review/local-coord-01/review-02/review-02.md) is an ignored local record). Preflight verified once
before editing: `main`, `HEAD`, local `origin/main` `7c78c849…`, divergence 0/0, index empty, 123 modified and 73 grouped untracked entries, the reviewer's
temporary test files absent. No staging, commit or push. LCO-R1, LCO-R3 and LCO-R4 are unchanged. The reviewer's retained probes
(`PlannerLocalExecutionReview02Tests.cs`, `PlannerBindingReadRaceReview02Tests.cs`, their logs) are untouched; the binding probe was copied into the test project only
inside a hash-guarded control and removed again.

| Id | Disposition |
| --- | --- |
| LCO-R2 [P2] (completion) | Fixed. `LocalExecutionFinalEvidence` now compares the linked attempt's `AgentCheckpointFingerprintSha256` with its step's source fingerprint for both Agent finals (review approval and provider diagnosis Escalation), and the provider Escalation proof now shares the approval's input/pass proof (`InputsAndPassHoldAsync`): the attempt's one recorded input is exactly the linked report and its verification evidence is exactly the diagnosis step's recorded pass, one per pinned recipe, in recipe order (missing, substituted, extra, reordered and wrong-pass members fail). The same proof gates the coordinator's classification (invalid evidence ends `NeedsAttention`) and the status disclosure (`final: null`, state/outcomes unchanged); nothing is replayed or replaced. Regressions: Application `LocalExecutionFinalEvidenceSourceTests` (16: approval fingerprint at status and observation, intact two-recipe later-pass diagnosis, diagnosis input removed/substituted/added, evidence removed/substituted/added, fingerprint, classification at observation, reordered, wrong pass) and hosted protected-API `LocalExecutionSequenceHostedStatusTests` (approval fingerprint, diagnosis input removed; both over the real recorded cycle through the protected status route). The three reviewer regressions are covered by equivalent permanent tests. |
| LCO-V1 | Fixed (existing unchanged-code mechanism, not attributed to this slice). `GitHubRepositoryIdentityBindingReader` reads the bounded binding candidates and the run's bounded bound-event candidates in one provider-neutral EF statement (`Concat` of two bounded projections into one private row type), so a competing atomic pair is seen whole or not at all. Every orphan, duplicate, stored-byte, actor-shape, canonical-payload, UTC and pinned-scope check is unchanged; no retry, lock, transaction, port or reordering was added, the observer stays outside transactions and the final Run write lock and canonical replay are untouched. Regression: `GitHubRepositoryIdentityBindingReadConsistencyTests` (4): three tests assert that the competing protected POST's commit hook fired immediately before the reader statement or at the next transaction start after it; the fourth reads NotRecorded, explicitly commits through that protected POST, then reads Available. All four assert that the first statement touching the bindings table also names the events table; the controlled commits assert 200 and exactly one binding/event pair. The other reader consumers (binding GET/POST, check-run output, repository permission, check contexts, CI, branch publication scope) and their corrupt-evidence tests pass in the full API run. |

**Correction evidence (logs `logs/executor/local-coord-01/correction2-*`)**

| Check | Result |
| --- | --- |
| Solution build, non-incremental | exit 0, 0 warnings, 0 errors |
| Domain / Architecture / Infrastructure `Persistence` / seven TransactionBoundary classes (rerun, cheap) | 1732 / 185 / 631 / 22, all passed |
| Application, full | **7589 / 7589, 12 m 32 s** (7573 + the 16 new tests) |
| API integration, full | **3170 / 3170, 11 m 22 s: the first clean complete API run**. The two failed correction-1 logs (3162/3164, then 3163/3164) stay retained as distinct failed results; the reproduced reader race is compatible with their concurrency failures but does not establish their exact interleavings |
| NSwag (client deleted, API rebuilt `--no-incremental`) | byte-identical, SHA-256 `09a57423…0d38` |
| Journeys, serial, stable host binaries | local execution passed (37 s), collaboration passed (1.9 m), planning sequence passed (22 s) |
| Controls (hash-guarded, hashes restored) | c2-1 approval fingerprint ignored: 2 / 16 failed; c2-2 diagnosis inputs and pass ignored: 11 / 16 failed; c2-3 HEAD two-statement reader plus the reviewer's retained probe: 5 / 5 failed (four single-statement contract assertions; one actual `unavailable` response in the reviewer probe; log `run-controls-correction2-output.txt`) |
| Format | the five changed files are clean; `dotnet format` of the whole solution still lists findings in unmodified files (the four unchanged-comment lines of `RecordInterruptedAgentArtifactCommandHandler.cs`, left alone as directed, and pre-existing response/query-result files this slice does not touch); `git diff --check` clean |

Retained (inputs unchanged): frontend Vitest 7205 / 7205, lint, build and strict e2e tsc (no frontend source changed), control 4 and controls 1 to 3 of correction 1 (their
target sources are unchanged). Not run: Infrastructure beyond `Persistence` and the seven transaction classes, `npm audit`, the stop/restart browser paths, any real provider.
The two protected-API additions were not separately mutation-controlled; their Application equivalents are (c2-1, c2-2).

Additional retained development failures, reconciled by the planner during review 3: `correction2-final-evidence-red-green.txt` is a failed
50 / 51 invocation, despite its filename: the `evidence_added` setup violated the unique attempt/verification-command key. The corrected
setup supplies a different command identifier; the later stable full Application result is separate. `correction2-binding-green-1.txt` is
62 / 63, with the earlier combined read-model case failing the unique project registration identity during setup; the final read-model
cases are separate facts, and `correction2-binding-green-2.txt` passes 4 / 4. Neither initial setup failure is a valid behavioral red control
or a passing result. Both logs remain intact; the later full suites do not reconstruct their historical source files.

### Defects found while testing and fixed

- The checkpoint guard trigger blocked the production implementation/correction result recorders (EF saves the Completed attempt before the
  result checkpoint): it now also admits the exact checkpoint the claimed linked attempt names.
- The three planning-family claims had neither the early local fence nor the save-seam classification (a refused save reported
  `attempts.persistence_failed`): both added.
- The panel dropped its availability visit while the arm barrier masked the status (the arm POST was never sent), and the strict decoder
  refused the real Implementation step (`passNumber` 0): both fixed in the frontend; found only by the real-host journey.

### Challenges, limits and risks for review

- **Quarantine blast radius (ADR-deliberate).** A timeout, cancellation, adapter exception or process-start failure of a linked dispatched
  operation leaves it an effective owner with no supported release; a mutating one consumes the global limit of one or two for every project.
  Tested and disclosed in the UI, not mitigated.
- **Status column length.** The 37-character refusal name exceeded the 32-character mappings; widened to 64 (no rebuild).
- **Fence-refused dispatch.** A linked claim that the workspace or capacity gate does not let dispatch ends the sequence with attention and is then
  closed by the dedicated refusal; there is no queue or retry.
- **Startup ordering.** The closure precedes every recovery (planning's stays late); the planning order test still passes.
- Exited is the host's proof about the invoked process only; there is no force release, cancel, retry, takeover or process-death inference.
- The journey uses an Accepted root plan: the 120-minute run budget ceiling cannot host the escalated path plus six cycle attempts. The
  Human-authorized depth-two plan is covered by hosted and Application tests, not the journey.
- Not run: Infrastructure project beyond `Persistence`, `npm audit`/dependency scans (no dependency changed), the stop and restart paths in a
  browser, any real provider, packaged shell or the other nine journeys. `ESLint` reports only pre-existing warnings. One unrelated existing
  AgentSupervision contention test failed once in an earlier full API run (file-lock `IOException` while disposing a temp database) and passed on
  rerun; the final full API run passed. The browser console keeps the pre-existing duplicate-React-key errors.

**Latest independently verified published baseline.** GLOBAL-LIMIT-01 `7c78c849fdd556ecf3a539dce479e31fc3cc501b` (record: this file at `7c78c84`).

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

GLOBAL-LIMIT-01 `7c78c849` (sole parent `1747fb2b`, 70 paths) passed independent publication verification
(`logs/planner-review/global-limit-01/publication-verification.json` and `.md`); its record, committed with pre-review wording, is this
file at `7c78c84`. GLL-R1 and GLL-R2 are resolved.

GLOBAL-ADMIT-01 `1747fb2b` (sole parent `b3d53bfc`, 93 paths) passed independent publication verification
(`logs/planner-review/global-admission-01/publication-verification.json` and `.md`); its record, committed with pre-review wording, is this
file at `1747fb2`. GAD-R1 and GAD-R2 are resolved.

WORKSPACE-EXCL-01 `b3d53bfc` (sole parent `f95e337`, 110 paths) passed independent publication verification
(`logs/planner-review/workspace-exclusion-01/publication-verification.json` and `.md`); its record, committed with pre-review wording,
is this file at `b3d53bf`. WEX-R1 to R4 are resolved; WEX-C1 (obsolete prop comments), the initial API binary-provenance uncertainty
and the overwritten correction failure log stay accepted limits.

AGENT-SUP-01 `f95e337` (sole parent `a78e572c`, 141 paths) passed independent publication verification
(`logs/planner-review/agent-sup-01/publication-verification.json` and `.md`); its record, committed with pre-review wording, is this
file at `f95e337`.

PLAN-COORD-01 `a78e572c` (sole parent `1f22a373`, 176 paths) passed independent publication verification
(`logs/planner-review/plan-coord-01/publication-verification.json` and `.md`); its record, committed with pre-review wording, is this
file at `a78e572`. Its evidence limits and the accepted nonblocking PCO-C1 are kept below under Active evidence limits.

HIST-INPC-01 `459344d3` (sole parent `80a2ec22`, 66 paths) passed independent publication verification
(`logs/planner-review/hist-inpc-01/publication-verification.json` and `.log`); its record, committed with pre-review wording, is
this file at `459344d`.

HIST-VER-01 `80a2ec22` (sole parent `26c3f24a`, 74 paths) passed independent publication verification
(`logs/planner-review/hist-ver-01/publication-verification.json` and `.log`); its record, committed with pre-review wording, is
this file at `80a2ec2`.

HIST-INP-01 `26c3f24a` (sole parent `96a21d73`, 49 paths) passed independent publication verification
(`logs/planner-review/hist-inp-01/publication-verification.log`); its record, committed with pre-review wording, is this file
at `26c3f24`.

HIST-MET-01 `96a21d73` (sole parent `5e588a44`, 16 paths) passed independent
publication verification (`logs/planner-review/hist-met-01/publication-verification.log`).
Its implementation, two HMT-R1 corrections, all failures and validation scopes
are recorded in this file at `96a21d7`; 247 independent focused tests passed.
The 4471-test full-suite result remains correction-1 input evidence, not a
correction-2 full rerun. Known token counts remain outside browser proof.

ART-WIN-01 `5e588a44` (sole parent `75372b18`, 19 paths) passed independent
publication verification
(`logs/planner-review/art-win-01/publication-verification.log`); its record,
committed with pre-review wording, is this file at `5e588a4`. HIST-ATT-01
`75372b18` (sole parent `4b52e1cf`, 26 paths) passed independent
publication verification
(`logs/planner-review/hist-att-01/publication-verification.log`); its record,
committed with pre-review wording, is this file at `75372b1`. WF-06
`4b52e1cf` (sole parent `e55709fe`, five paths) passed independent
publication verification (final section of
`logs/planner-review/wf-06/review.log`); its record, committed with
pre-publication wording, is this file at `4b52e1c`. WF-05 `e55709fe` (sole
parent `e614c795`, six mode-100644 paths) passed independent publication
verification (`logs/planner-review/wf-05/publication-verification.json`); its
record, committed with pre-review wording, is this file at `e55709f`. WF-04
`e614c795` (sole parent `0a57216c`) passed 5/5 (`logs/planner-review/wf-04/`);
its [pre-GO record](current-work-history-e614c79.md#active-slice-wf-04-checkpoint-history-consolidation)
is historical. WF-03N back to WF-02A:
[deliveries](current-work-history-e614c79.md#prior-published-delivery),
[per-slice limits](current-work-history-0a57216.md#prior-published-delivery);
TEST-48-01 `d60d718` and earlier: this file at `a593009` and before.
Excluded-artifact reads grant no retrospective authorization or new read; do
not reopen or republish delivered slices.

## Active evidence limits

Archiving, condensation and progressive reading resolve or waive none of these:
- **PLAN-COORD-01 (published).** Its original executor validation runs were reported to Codex but not saved as log files: the whole
  Application run with one failure,
  `GetRunEventHistoryQueryHandlerTests.Every_known_event_type_is_disclosed_exactly_and_the_allow_list_names_every_constant`, and the
  Infrastructure first-run failures of earlier migration tests. No complete Application rerun on its final first-delivery inputs
  exists, and the first predecessor-control output was overwritten by a later hosted run and is lost. The only retained originals are
  the executor session transcript
  (`C:\Users\denis\.claude\projects\C--Users-denis-Desktop-AIProjects-DevalCopilot\873cb431-1909-4ac7-b740-dc68b3c4f2cf.jsonl`) and
  `logs/executor/plan-coord-01-correction-01/retained-original-control-outputs/`; later passes neither authenticate nor reconstruct
  them. PCO-C1 (no Resolver production change and no mirror test; the hosted production-mediator paths remain the boundary) is accepted
  and nonblocking.
- **AGENT-SUP-01 (review 1 NO-GO; correction 1 prepared, not yet reviewed).** Three tests failed once inside the long full Application
  and API runs and passed in isolation on the same binaries; the originals are retained (`final-application-full.ORIGINAL-FAILURE.log`,
  `final-api-full.ORIGINAL-FAILURE.log`, `rerun-*.log` in `logs/executor/agent-sup-01/`). Their cause is unexplained; untouched test
  files and isolated passes establish neither irrelevance to this slice nor load/environment causation, and no clean complete
  Application or API rerun is claimed (correction 1 did not rerun the full projects). The first run of negative control 3 was aborted
  by a script working-directory error and rerun; both logs are retained. Correction 1's journey console counts differ from the original
  delivery's (duplicate-key errors 136/244 against 72/160) without an established cause.
- **HIST-INPC-01 (published).** The two original EF1002 analyzer failures of its first Application test build have no retained
  output, and later passes neither reconstruct nor authenticate them; its development, final and negative-control failures stay
  as recorded in this file at `459344d` and its `logs/executor/hist-inpc-01/`. The journey console counts of that slice (45
  duplicate React keys, three connection negotiations) and those of HIST-OUT-01 (44 and three) have no HEAD comparison, so cause and
  count equivalence stay unproved.
- **WF-02A evidence loss.**
  `logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a
  partly abridged derivative chat transcription, not recovered or
  independently authenticated output; original failed runs, checker and
  timings are unavailable; run B's exit status is unknown. Never reconstruct.
- **Failed, missing and derivative evidence stays as recorded** in the
  per-slice records: invalid mutation kills, recovered or unretained
  checkers, surviving or no-op mutants, overwritten generator output,
  unrecorded streams, failed planner verifications, checker defects and
  chat-only output. Items through WF-03N:
  [detail](current-work-history-e614c79.md#active-evidence-limits); WF-04
  dev-01, invalid negative-01 and RV-04-01: this file at `e55709f`. WF-05
  dev-01's failing pre-fix checker was not retained or reconstructed.
  HIST-ATT-01's log keeps only filtered excerpts of its expected development
  failures (run 02) and of its first, failed journey-host build (run 13);
  their complete output was not retained. Codex accepted that loss as a
  limitation of this delivery; later passes neither authenticate nor explain
  those failures. Codex's first independent focused run failed to load six
  suites (temporary-module ENOENT); its retry with a workspace TEMP/TMP passed
  (`logs/planner-review/hist-att-01/focused-02.log`). That first failure is
  unexplained and not attributed to product code. ART-WIN-01 keeps complete
  output of every check, including its failures; its first journey-host build
  hit the same compiler file lock (r04, also naming Microsoft Defender) with
  only its own build processes running, and the retry passed. Its cause is
  unproven. HIST-MET-01 keeps complete output of every check too; its first
  journey-host build hit the lock again (r07, naming only `VBCSCompiler`), the
  retry passed, and the cause remains unproven.
- Scans are lexical; Git/disk/live checks lack negative controls; retained
  controls and publisher phases were inspected, not rerun or historically
  authenticated. Ordinals, counts, "real today", EX-G03, EX-H03 and WF-02B
  observations stay unreconciled. Earlier product, test, browser, generation
  and qualification evidence keeps its scope.

## History

[Registered](development-workflow.md#historical-snapshots) exact snapshots:
[ba1d4c2](current-work-history-ba1d4c2.md) (earliest),
[0a57216](current-work-history-0a57216.md) (WF-03N, earlier limits) and
[e614c79](current-work-history-e614c79.md) (WF-04, prior risk wording); later
entries, including WF-05, WF-06, HIST-ATT-01, ART-WIN-01 and HIST-MET-01, are in Git history. Evidence, not current status or
approval.

## Open risks

[Exact prior wording](current-work-history-e614c79.md#open-risks).
- **Account usage.** ADR-0025 and `CodexAccountUsageStopGate` enforce an
  optional run-scoped Codex stop at a provider-reported percentage, at claim
  and before dispatch; below-threshold evidence is not remaining quota. The
  ADR-0026 warning is advisory and separate, as is the
  [token-activity stop](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim).
  No Claude account-usage threshold; Claude account/session limits open.
- Provider-session resume, live context capacity, manual compaction and other
  provider controls stay open under
  [Increment 4](mvp-delivery-plan.md#increment-4-real-codex-and-claude-code-collaboration).
- ADR-0012/0013 bound claim count and reserved time, not wall time, provider
  usage or account availability; no human override. Gemini stays disabled
  (ADR-0011).
- Raw linked artifact inspection beyond the existing bounded views is open.
- **Attempt and artifact inspection limits (HIST-ATT-01, ART-WIN-01,
  HIST-MET-01, HIST-INP-01, HIST-VER-01, HIST-INPC-01, HIST-OUT-01).** All read bounded views only: no replay, export,
  session resume or complete history. HIST-OUT-01 (unpublished, under review) shows at most 64 stored collaboration messages that
  record the attempt as their own attempt and one of them at a time, as read now: not the complete provider response, proof of what was
  generated or seen, a current approval or execution success. A host-constructed escalation has no message-level attempt reference
  and is not found; no outcome, event or reply is used to infer an expected output, and an empty set says only that none was recorded
  in that read. Its set proof and content read are separate reads without an atomic snapshot. Published HIST-INPC-01 shows at most 64
  input references and one stored message at a time as read now, never the exact prompt, all context or what an Agent saw; its
  set proof and content read are
  separate reads without an atomic snapshot. Published HIST-VER-01 shows recorded verification membership and
  sealed output as read now, never which bytes an Agent saw or a current approval; at most 32 members are inspected and a
  view keeps at most 256 KiB of one output. The three window defects HIST-ATT-01
  found are addressed by the published ART-WIN-01; its window acceptance bounds
  the text by the cursor advance but cannot prove that the text is the decoding
  of those bytes; the host remains the authority for content. Published HIST-MET-01
  shows the selected attempt's recorded
  execution facts; the browser checks their shape and range only. Its
  evidence timestamps are judged raw by an operation-scoped facade (HMT-R1);
  other operations still rely on the generated client's unchecked revival,
  which this slice does not change. Within the same evidence answer, the
  generated conversion of other members is also unchanged: a malformed nested
  `accountUsageDecision.retrievedAtUtc` can still make the generated model throw.
  Planner review corrected the reported artifact failure mechanism: the generated
  model guards `artifacts` with `Array.isArray`; a non-array is left absent and
  the existing whole-answer decoder rejects it. These pre-existing whole-read
  refusals are not newly reachable or addressed here. Known token counts are not browser-proven
  (the Codex double reports no usage). A duplicate React key console error seen
  in the journeys (also with the HEAD frontend) and a recurring compiler file
  lock on the first journey-host build remain unexplained. The development
  build's second evidence read is inferred, not proven, to come from
  StrictMode.
- Raw checkpoint Git observation can read named linked content; ADR-0024/0027
  constrain delivered tracked text only. Physical proofs are Windows-only:
  elsewhere previews are omitted and `FilesystemArtifactStore` containment is
  lexical.
- Unexplained: concurrent-replay 409, target-test 500, disposal
  `NullReferenceException`, unnamed Api 2067/2068 and the interrupted reviewer
  synthetic run; later passes establish neither flakiness nor irrelevance.
  TEST-48-01 only bounds what a future failure retains; one pass attributes
  nothing, a hung submission yields no record, and retention beside a disposal
  fault was not exercised.
- **P9 evidence limits:** [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md)
  sections 11-14, [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md#5-unknowns-and-limits)
  and [semantics note](../architecture/github-publication-native-io-semantics.md#6-unknowns-and-consequences)
  (K1-K5): stop-gate defect and protocol deviation, inconclusive A2, capture
  and native-error limits, malformed original receipt, no pre-redaction
  originals, path-bearing derivatives, annotated transcript, unchecked child
  exit, limited profile state, no live ACL seal, no proof against lost output.
- Decision constraints: [planner-handoff.md](planner-handoff.md#standing-decision-constraints).
