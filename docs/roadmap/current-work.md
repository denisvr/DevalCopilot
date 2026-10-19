# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: AGENT-SUP-01 current Agent supervision observations

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Correction 1 (review 1 NO-GO, ASUP-R1 to R5), prepared unstaged, uncommitted and unpushed; no GO exists.** Applied in the same
  executor chat on the same uncommitted diff; no Git state was changed (nothing staged, committed, pushed, reset, cleaned or checked
  out), no dependency, generator, shared transport/auth or ADR changed, and no authority was added. The regressions below were added
  first and each failed for the finding's reason against the unfixed code (`logs/executor/agent-sup-01/correction-01/red-*.log`:
  registration 6090 ms, phase entry 6136 ms and a blocked sweep 5547 ms to stop against the two-second bound; Ended/NeedsAttention
  instead of Unavailable; missing registration/phase/clean-closure events; decoder admitting facts after `observedAtUtc`; no
  AbortSignal on the transport), then passed after the fixes.
  - *ASUP-R1.* Every observation write now starts off the caller's thread (`AgentSupervisionObserver.StartWrite`, `Task.Run`), so the
    two-second bound covers the store's synchronous work from its first instruction; the write owns its scope/DbContext until it
    settles and keeps its own two-second token. `AgentObservation` orders its writes in one chain (phase entries and closure wait
    behind the previous write, a heartbeat is dropped while any write is pending), so writes never overlap, reorder or accumulate.
    `AgentSupervisionSweep` runs its inventory and each observation off its loop in owned scopes, tracks at most one pending call per
    observation (and one inventory read), bounds a round to two seconds and stops promptly on host shutdown. The seven supervisors were
    not edited: they reach this behavior only through the shared collaborator. Proof: `AgentSupervisionContentionTests` (real
    file-backed SQLite, a separate connection holding a write transaction for 6 to 8 s): registration, phase entry, closure and a
    blocked sweep's stop each return within 3 s; the abandoned registration wrote nothing; the closure committed after the pending
    phase write, exactly once and last; the attempt stayed Running with no outcome; a later sweep recorded each stale attention once.
  - *ASUP-R2.* `AgentSupervisionWireClient` accepts an optional `AbortSignal` and adds it to the request the generated operation builds
    (operation-scoped; `authenticatedHttp`, other clients and NSwag config unchanged; `agentSupervisionClient(signal?)`).
    `useAgentSupervision` aborts the request when its ten-second bound elapses and when the lifetime ends (selection change,
    unmount), and a further read of the lifetime starts only after the previous request actually settled, keeping coalescing and
    stale-response fences. Proof: `useAgentSupervision.transport.test.tsx` (real hook, wire client, generated operation and
    authenticated transport down to a cancellation-aware fake `fetch`): abort on timeout, timeout then recovery with peak one request,
    a transport that ignores abort never gets a second concurrent request, A-B-A aborts the replaced selections, unmount aborts and
    starts nothing; `agentSupervisionContract.test.ts` proves the signal reaches the authenticated request.
  - *ASUP-R3.* `AgentSupervisionPolicy.Assess` refuses a recorded first attention later than the clock; `AgentSupervisionAttempts`
    returns no judgment instant for a completion later than the clock (no clamping; the exact instant is valid) and its
    `AssessTerminal` checks the current chronology before the historical completion judgment, used by the query's terminal branch and
    by closure. The frontend decoder rejects an Observed, NeedsAttention or Ended answer whose observation, attention or completion time
    lies after its `observedAtUtc`. Proof: `AgentSupervisionChronologyTests` (the reviewer's three rollback cases now Unavailable, a
    refused closure that leaves the observation open, a timely result read hours later, exact-instant boundaries, terminal precedence
    with attention history), two Domain cases in `AgentSupervisionPolicyTests`, the real-endpoint theory
    `A_recorded_attention_or_completion_later_than_the_host_clock_is_served_as_Unavailable_never_clamped`, and the decoder cases in
    `agentSupervision.test.ts`. Two existing fixtures that encoded an attention after their own read clock were given a later
    `observedAtUtc` (`agentSupervision.test.ts`, `AgentSupervisionPanel.test.tsx`), and the endpoint test of attention history now
    seeds an observation started ten minutes earlier; their assertions are unchanged.
  - *ASUP-R4.* Two fixed-schema event types, `agent_supervision.registered` and `agent_supervision.phase_entered` (payload: supervision
    id, attempt id, phase), are appended atomically with registration and each phase entry, and the existing `agent_supervision.closed`
    is now appended on every actual closure, including a clean `attempt_terminal` one; heartbeats stay silent, a replay or refused move
    appends nothing. Pinned lists (backend and frontend), event labels and fixed timeline wording were extended. The former zero-event
    assertions became exact ordered sequences: the shared hosted scenarios used by the seven production supervisors (scoped to each
    session's own attempt, because the diagnosis composition seeds an earlier observed attempt in the same run), the coordinated
    planning hosted test and `planning-sequence.journey.ts` (registration, two phase entries and closure per attempt, 20 events).
    Proof: `AgentSupervisionEventTests` (exactly once per transition, attention before the phase event, and a SQLite trigger rejecting
    each new event rolls the receipt change back). This grants no workflow authority or replay capability. Implementation choice (1)
    below is superseded.
  - *ASUP-R5.* `OffsetClock`, `GateableEvidenceReader`, `AgentSupervisionHostedComposition` and `AdapterHold` moved from
    `AgentSupervisionHostedSupport.cs` into their own files in the same test folder and namespace; content unchanged, no shared fixture
    or cache. The file is in the API integration test project, not under `HostedServices`, and no architecture allow-list entry
    referred to it. Proven by compilation and the unchanged hosted scenarios passing.
  - *Fresh validation on the final sources (`correction-01/final-*.log`).* Solution build (`-m:1`, after `dotnet build-server
    shutdown`) 0 warnings, 0 errors; the generated client is byte-identical (SHA-256 `3ebd7876...d138`, no API shape change).
    Domain `AgentSupervision` 40/40; Application `AgentSupervision|GetRunEventHistoryQueryHandlerTests` 201/201; API matrix (the
    retained filter including `CorsPolicy`, all three AgentSupervision spellings and the seven supervisors) 315/315; Infrastructure
    `AgentSupervision|Migration` 290/290; Architecture 179/179; `dotnet format --verify-no-changes` over the 30 touched C# files 0.
    Frontend: the affected files 518/518, whole Vitest once 266 files 5950/5950, `oxlint` 0 with the same 9 existing warnings (none in
    a changed file), `npm run build` (`tsc -b && vite build`) 0, strict e2e `tsc` 0 (globs expanded explicitly; a first attempt through
    `cmd /c` failed TS6053 because cmd does not expand globs, retained in its log). Journeys, serial, first run, no retry:
    `planning-sequence.journey.ts` 1/1 and `collaboration.journey.ts` 1/1. Their console output again contains duplicate-React-key
    errors (one repeated key per journey; counts 136 and 244 against 72 and 160 in the original delivery) and the same 2 and 14
    SignalR negotiation stops; no cause is attributed to either difference. `git diff --check` reports only the generated client's
    existing CRLF warning. Development failures before the final sources are retained (`dev-*.log`, `build-01-red-tests.log`: an
    EF1003 analyzer error in a new test, fixed).
  - *Not rerun.* Full Domain, Application, API and Infrastructure projects (no shared admission, dispatch, lifecycle or result
    semantics changed, and planner review 1 requires no routine full rerun), the negative controls (their targets did not change),
    `test:harness`, dependency/vulnerability audits (no dependency changed), the other journeys and any real provider, P9 or
    qualification path. The original full-suite failures below remain unexplained.
  - *Inventory.* The slice now has 141 distinct changed paths (the reviewed 133 plus 8 new files of this correction). The reviewer's
    133 distinct paths and the original 94 are the same delivery counted in different units: 94 was the directory-grouped
    `git status --short` inventory, not a distinct-file count.
- **Prepared at** the first complete implementation, unstaged, uncommitted and unpushed; no GO exists. Preflight matched the planner's:
  `main`, HEAD, local `origin/main` and the freshly queried live `refs/heads/main` `a78e572c`, tree `2c62b12f`, sole parent `1f22a373`,
  divergence 0/0, empty index, exactly the two planner-modified documents (`docs/decisions/README.md`, `docs/roadmap/planner-handoff.md`)
  and the untracked ADR-0056, all preserved unedited. Nothing was reset, cleaned or overwritten.
- **Outcome.** The current cockpit shows what the host's own observer last recorded for the run's selected Agent attempt: the phase it
  reported, its last report and the absolute deadline of that phase, a first attention with its fixed reason, and the attempt's real
  recorded status, which governs once it is terminal. An independent bounded sweep exposes a stale or hung observation even while a
  supervisor, Git preparation, adapter or result recorder is blocked. It is never provider progress, process liveness or execution
  authority, and no observation status is an input to any claim, dispatch, source, provider, budget, result or recovery rule
  ([ADR-0056](../decisions/0056-observe-agent-supervision-without-granting-execution-authority.md)).
- **Domain/persistence.** `AgentSupervision` (one receipt per observed Agent attempt: immutable run, attempt, host-instance and
  observer identity, guarded revision, start and last report, phase with its absolute deadline, first attention, closure), the closed
  phase and reason vocabularies and `AgentSupervisionPolicy` (5 s heartbeat, stale at exactly 30 s, 2 min for Preparing and
  RecordingResult, the attempt's reserved invocation timeout plus 2 min for AwaitingAdapterResult, every boundary expired, 2 s call
  bound); two `agent_supervision.*` event types; additive migration `AddAgentSupervision` (one table, three indexes, check constraints
  and two guards: coherent insert for a Running Agent attempt of the same run; immutable identity, forward-only phases, a deadline fixed
  while a phase stays, a retained first attention and an immutable closed observation). No historical backfill.
- **Application.** Internal mediator commands to register, advance the phase, heartbeat and close (each judging the previous deadline and
  freshness under the write lock and recording the first attention before it updates), an independent per-observation sweep command with
  a cursor-windowed inventory query, startup closure of previous-host observations, and one current-state query. Closure reads the durable
  Attempt afresh (a terminal attempt is judged at its own completion time, so a timely result observed late is no attention and a late one
  is kept as history). The read clock is sampled after the receipt, so a heartbeat committed while it ran is a fresh report, not an
  incoherent chronology.
- **Host.** One small `AgentSupervisionObserver`/`AgentObservation` collaborator wired into the seven existing Agent supervisors
  (`await using` at entry to each supervisor's per-attempt execution, two phase reports; their own gates, invocation, parsing and result
  ownership are untouched), an independent `AgentSupervisionSweep` hosted service, and the startup closure after the existing attempt
  reconciliation and before any worker starts. Every observation call and disposal wait is bounded to 2 s in its own scope, at most one
  heartbeat write is pending, and nothing here can throw into, skip a check of, or replace the exception or cancellation of the original
  path. Side-effect-free compositions start no sweep and close nothing. (As first delivered the bound began only after the store's
  synchronous work, ASUP-R1; correction 1 above makes it hold under a synchronously blocking store.)
- **API.** One protected, bodyless, queryless `GET /api/runs/{runId}/agent-supervision` through one mediator query: a closed status
  vocabulary (`NoAgentAttempt`, `NotObserved`, `Observed`, `NeedsAttention`, `Ended`, `Unavailable`) with its nullability
  relationships, canonical UTC strings, a decimal-string Int64 revision, fixed reason and next-action texts, and a non-disclosing 404.
  The generated client was regenerated and reproduced byte-identically by a clean non-incremental build after deleting it.
- **Frontend.** An operation-scoped raw-wire client and whole-answer decoder (closed tuples, exact keys and ticks, and a re-check that an
  `Observed` answer is fresh at its own host clock), an owned-lifetime `useAgentSupervision` hook (coalesced reads, 5 s refresh while the
  attempt is not terminal, 10 s request bound, three-failure stop, fenced selection/A-B-A/remount/stale responses, no browser storage), the
  `AgentSupervisionPanel` and a separate header badge. A failed, timed-out or incoherent read is shown as unavailable and a reading
  unrefreshed for 15 s as stale. For a manual, legacy or unrecognized Agent run the header shows the lifecycle exactly as recorded and
  its duration as lifecycle elapsed time that does not tick; only a simulated run keeps the live treatment. The new events have fixed
  labels and wording. The provider runtime preflight, historical readers and every action gate are unchanged.
- **Documentation.** Engineering context, system overview, data and recovery, cockpit specification and MVP plan describe only this
  exact partial capability; no accepted ADR, frozen snapshot or planner document was rewritten.
- **Implementation choices within ADR-0056 (for review; no challenge was raised).** (1) *Superseded by correction 1 (ASUP-R4, not
  accepted by review 1):* only the first attention and a closure without a terminal result appended a run event; heartbeats,
  registration, phase entry and a clean closure after a terminal result appended none.
  (2) The effective attention is the earlier of the stale and deadline boundaries (the deadline wins a tie); an old host's open
  observation reads `host_interrupted`; a recorded first attention always wins. (3) Startup closes every previous-host observation as
  `host_interrupted` and records that attention when none exists, including one whose attempt restart reconciliation already
  interrupted. (4) The database already allows one Running attempt per run, so several running Agent attempts cannot occur; that read
  branch is defensive and tested by dropping the index. (5) Existing tests changed only where the contract requires it: the pinned event
  lists (backend and frontend), two earlier table-set assertions, the `IDevalCopilotDbContext` test double, an optional
  `configureAdditionalServices` hook on six hosted-test provider builders, mocks of the new hook in the two cockpit composition tests, and
  `RunCockpitView`'s earlier "keeps the legacy live treatment for a run with no recorded sequence" case, which encoded the behavior
  ADR-0056 replaces for a manual Agent run (it now covers a simulated run, with new cases for Agent runs).
- **Validation (exit codes checked; complete original logs, TRX and negative-control outputs in the ignored `logs/executor/agent-sup-01/`).**
  Fresh means executed on the final sources of this return; every failure below is retained with its original log.
  - *Build and generation (fresh).* `dotnet build` of the solution serially (`-m:1`) after `dotnet build-server shutdown` exit 0, 0
    warnings, 0 errors (49 s non-incremental; 28 s after the controls, which also replaced the mutant binaries). The generated client
    was deleted and regenerated by a clean non-incremental build: byte-identical, SHA-256 `3ebd7876...d138` both times (it differs from the
    baseline only by the one new operation and its types, inspected in the diff). `dotnet ef migrations has-pending-model-changes` reports
    none. `dotnet format --verify-no-changes` over the 99 changed or new C# files exit 0.
  - *Backend suites (fresh, `--no-build --no-restore`).* Domain 1545/1545. Application focused (`AgentSupervision` plus the whole
    `GetRunEventHistoryQueryHandlerTests` class) 184/184. Infrastructure focused (`AgentSupervision|Migration`, real file-backed
    SQLite) 290/290. API matrix (the new AgentSupervision tests plus the retained supervisor class-name substrings, PlanningSequence and
    LaunchSessionAuthentication, all seven production supervisors including repair and coordinated paths, blocked preparation, adapter
    and recorder, faults, disposal and restart) 310/310. Architecture 179/179 (seven new boundary tests). Beyond the matrix, full
    Infrastructure 3155 passed + 4 skipped (the existing privilege-dependent symlink skips); full Application 6612/6613; full API
    2895/2897.
  - *Failures kept, none in a changed file or path.* Full Application: one failure,
    `PlanningSequenceStepClaimTests.Two_workers_that_race_the_same_ticket_create_exactly_one_attempt_and_the_loser_never_deletes_the_winners_manifest`
    (`IOException`, `AgentContextManifest.partial` in use by another process, in the unmodified
    `CreateCodexPlanningAttemptCommandHandler`; `final-application-full.ORIGINAL-FAILURE.log`). Full API: two failures,
    `ProviderFixtureContractTests.A_working_directory_that_is_not_an_owned_worktree_is_refused_for_every_mutating_or_reading_role`
    (`IOException`: the pipe is being closed, writing to a fixture child) and
    `ManualQualificationRehearsalTests.The_native_process_table_ends_in_exactly_one_of_the_two_safe_outcomes_of_the_accepted_policy`
    (expected an origin/identity outcome, observed `LeftoverAlive`; native process table) (`final-api-full.ORIGINAL-FAILURE.log`).
    Rerun in isolation on the same binaries, all pass: `PlanningSequenceStepClaimTests` class 24/24
    (`rerun-application-claim-race.log`), `ProviderFixtureContractTests` and `ManualQualificationRehearsalTests` 208/208
    (`rerun-api-two-failures.log`). They occurred under the nine-minute full runs. *Corrected by correction 1:* this record earlier
    called them load- or environment-sensitive because their test files were untouched and they passed in isolation; neither an
    untouched test file nor an isolated pass establishes irrelevance to this slice or load/environment causation. Their cause is
    unexplained, every original failure is retained, and **no clean complete Application or API rerun is claimed**. Earlier development failures, all fixed in source before the final runs: first API matrix 308/310 (two tests ran
    against stale binaries; `api-matrix-dev-01.log`), first whole Vitest 5864/5865 (only the header test whose contract this slice
    changes; `frontend-full-dev-01.log`), plus the intermediate Application, Infrastructure and hosted failures of the development logs
    (`*-dev-*.log`), which are not repeated here.
  - *Frontend (fresh).* Whole Vitest once: 265 files, 5931/5931. `oxlint` exit 0 with the same 9 existing `react` warnings and none in a
    changed file; `npm run typecheck` 0; `npm run build` (`tsc -b && vite build`) 0; `npm run test:harness` 89/89; the strict e2e `tsc`
    (`npx tsc --ignoreConfig --noEmit --strict --target es2023 --module esnext --moduleResolution bundler --skipLibCheck --noUnusedLocals
    --noUnusedParameters --verbatimModuleSyntax --allowImportingTsExtensions --erasableSyntaxOnly --types node e2e/*.ts e2e/journey/*.ts
    e2e/harness/*.ts playwright.config.ts playwright.journey.config.ts`, from `src/frontend/DevalCopilot.Frontend`) 0. No `tsc` emit
    flag was used.
  - *Journeys (fresh, serial, first run, no retry).* `planning-sequence.journey.ts` 1/1 (19 s; the extended read-only database checks
    prove five observations, one per attempt, ended `attempt_terminal`, no attention and no supervision event, and the panel and
    header badge read "Ended"/"attempt ended", again after a reload) and `collaboration.journey.ts` 1/1 (97 s). The existing
    duplicate-React-key console errors appear in the journey host output as before.
  - *Negative controls (fresh, three serial compiling mutants; original outputs and hash-verified restoration in
    `control-*.log`).* (1) A fresh heartbeat masking a phase deadline (`AgentSupervisionPolicy.Derive` deadline branch also required the
    stale boundary) built and failed 3 Domain and 7 Application tests. (2) A foreign observer update (dropping the observer match in
    `AgentSupervisionWrites.LockOpenOwnedAsync`) built and failed
    `A_foreign_observer_cannot_heartbeat_advance_or_close_and_the_receipt_is_untouched`. (3) A stale UI response (the hook's lifetime key
    constant instead of the run id) type-checked and failed the A-B-A and unmounted-refresh hook tests. Each original SHA-256 was verified
    after restoration. The first attempt at control 3 was aborted by my script (a `cd` inside the build command moved its working
    directory, so no build, test or restore step ran and the mutant stayed on disk); the file was restored from the retained backup, the
    hash verified, and the control rerun with subshells (`control-stale-ui-response.ABORTED-cwd-error.log` and `control-stale-ui-response.log`).
  - *Hygiene (fresh).* `git diff --check` clean; nothing staged; no CR, NUL, trailing whitespace or missing final newline in changed or
    new text files other than the generated client (existing CRLF) and the two planner documents; the generated migration files keep
    the generator's BOM like every earlier migration; raw non-ASCII only in the repository's existing `·`, apostrophe and ellipsis copy
    style; no secret pattern beyond the fictitious `Bearer not-a-real-session` test header; no home path in a changed source or
    document. `dotnet list package --vulnerable --include-transitive` none; `npm audit` exit 1 with the one existing dev-only
    `source-map-js` advisory (GHSA-68fv-2mgg-jv7q; `npm audit --omit=dev` finds 0); no dependency or lockfile changed. Local Markdown links
    in the changed documents and fragments resolve (see the final check below).
  - *Not run.* Any real provider or P9 experiment, a live-clock deadline or 30 s staleness wait (all clocks are controllable or
    logical offsets), the native Tauri shell, the 13-spec Chromium real-host suite, and the other nine journeys (no input they cover
    changed besides the generated client and cockpit, which the two journeys above exercise).
- **Limits.** The observation is the host observer's record only: a fresh heartbeat is never provider progress or a live process, and a
  stale or overdue one is attention to inspect, not a verdict, a cancel or permission to retry. No migration backfill: attempts that
  predate it read `NotObserved`. The seven supervisors are wrapped by a shared collaborator; each keeps its own safety checks, and
  disposal is bounded to 2 s so a hung store can delay only that attempt's closure, which the independent sweep and startup closure then
  record. Increments 4/5/6 remain incomplete, ADR-0050 stays Proposed, P9 unproven and AI Memory deferred. Nothing was committed, staged or
  pushed; review 1 was NO-GO and correction 1 awaits Codex review.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
