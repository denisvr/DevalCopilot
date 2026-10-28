# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: COCKPIT-IDENTITY-01 correct sibling component identity in the current cockpit

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git and the slice's independent publication-verification record, when available. This record alone grants no
publication authority.

**Delivery phase: first return to Codex for review, by the new Claude executor chat; the complete diff (including the two planner inputs) is
unstaged, uncommitted and unpushed. No GO exists.** Review and publication are separate later events; nothing here claims either.
Baseline: `main`, `HEAD`, local `origin/main` and a live `git ls-remote` read of `origin/main` equalled `a9e06ac634927b097d49f36a7064bd94f0cc46db`
(LOCAL-COMMIT-INSPECT-01, independently verified published; neither repeated, republished nor reopened, and no closure commit was made). The index was
empty, no file was untracked and exactly the two planner inputs were modified. `planner-handoff.md` (`1d849c07…67ef`) and `mvp-delivery-plan.md`
(`33e708a4…5f9`) equalled the dispatch SHA-256 values at the start and again at the return and were not edited. ADR-0050 stays Proposed, P9 unproven, AI
Memory deferred, Increments 4/5/6 incomplete; every standing restriction in the handoff stays. No frozen profile, native, provider or credential
experiment, real run, recovery, cleanup, dependency, backend, schema, generated-client, hook, decoder or transport change was made, and no commit or push.

### What exists

Two sibling groups gave different components the same `key={runId}`, so React tied their state to one identity. Each key now names its component and keeps
the run, so a run replacement still ends the state and an ordinary parent update keeps it. Nothing else in either source changed.

- **`UsageEvidenceRail.tsx`:** `AgentAttemptHistoryPanel`, `LocalExecutionInspectionPanel` and `PlanningSequenceInspectionPanel` now use
  `agent-attempt-history:${runId}`, `local-execution-inspection:${runId}` and `planning-sequence-inspection:${runId}`.
- **`RunCockpitView.tsx`:** `LocalCommitPanel` and `AbandonRunPanel` now use `local-commit:${runId}` and `abandon-run:${runId}`.
- **Inspected and left unchanged (no collision shown):** the rail's `account-usage-stop:${runId}:${state}:${percent}` control (its state and percent are the
  deliberate version identity), its `EVIDENCE_TABS` map (its own array) and the two index-keyed line lists; in `RunCockpitView.tsx` the fragment-level keys
  `${runId}:${model}:${effort}`, `turn-limit:${runId}:…`, `token-stop:${runId}` and the bare `key={runId}` of `TokenWarningPanel`, which cannot equal either of the
  first two (each carries a suffix) and has no same-keyed sibling. `App.tsx` keys the whole `RunCockpitView` by run, so a run replacement in the real
  application remounts everything regardless; the panel keys matter for ordinary updates and for the unkeyed composition.
- **New `CockpitIdentityRail.test.tsx` (6 tests):** the real rail, generated clients and transport over a counted fake network. All three inspectors are
  opened and given a selection, then: the account observation held pending and resolved, the model observation held pending and resolved, same-run parent
  updates with the stop and warning controls appearing, changing and disappearing, a run replacement with held late answers and an A to B to A return, a
  collapse and re-expand, and an unmount with a pending read. Each stable-run case asserts exactly one instance of each inspector, still open with its
  selection, no inspection read beyond the baseline, only GET requests and no duplicate-key diagnostic; each lifetime case asserts the inspectors open cold and
  closed and that a late answer changes nothing.
- **New `CockpitIdentityCommandPanels.test.tsx` (5 tests):** the real `RunCockpitView` with the real `LocalCommitPanel` and `AbandonRunPanel`, their real hooks and
  four mocked clients whose command methods must never be called. It asserts the same two form elements (by identity) keep their drafts through same-run parent
  updates, a projection refresh and a lifecycle change that swaps the Agent-action siblings before them, with no added status read; that both end when the manual
  mode ends and start cold when it returns; that a run replacement and an A to B to A return start cold; and that an unmount with a pending read acts on nothing.
- **Local console observer:** both files spy on `console.error` without replacing it (every diagnostic still reaches the original) and only judge messages
  matching "two children with the same key".
- **`e2e/local-execution-queue.journey.ts` (extended):** a `console` listener registered before the first navigation collects duplicate-key diagnostics and
  retains every other error or warning (printed at the end, not judged); phase 10b, at the settled current-run inspection (open ledger, selected step 2),
  clicks the rail's account and model refreshes and the project's Refresh evidence, each time re-asserting the open inspector, the 12-row ledger, the pressed
  step and its detail, and afterwards that the inspection GET list, the POST list, Agent-stage invocations (`agentStageInvocations`), attempts, executions and
  admission rows are unchanged and that no duplicate-key diagnostic occurred; the same check closes the journey. Existing queue and historical assertions are untouched.

### Evidence (logs under `logs/executor/cockpit-identity-01/`, git-ignored; `00-validation-summary.txt` indexes every raw output; commands run from `src/frontend/DevalCopilot.Frontend`)

Fresh on the final sources unless stated; elapsed times are wall clock.

- **Original failing files, unchanged by me, original sources:** `vitest run` of `LocalExecutionAdmissionSurfaces` and `LocalExecutionReleaseInspectionSurfaces`: 10 failed / 10 passed (20.0 s), the planner's result reproduced.
  After the correction the same two files pass 20/20 inside the final set below (their sources are byte-identical to `HEAD`).
- **Red control with the new tests (hash-guarded):** the five keys were put back to run-only, the original hashes were proven (`UsageEvidenceRail.tsx` `862f764c…b31e`,
  `RunCockpitView.tsx` `39184369…61c4`), and `CockpitIdentityRail`, `CockpitIdentityCommandPanels` and the two original files ran: 21 failed (rail 6/6, command panels 5/5, original files 2 and 8), 11.0 s;
  the fixed sources were restored and their hashes verified (`7a4933b1…6805`, `271d7e5d…24c7`). The command-panel failures include "Found multiple elements" for the "Local commit" region and a "Local commit" region that
  stays mounted after the mode ends, besides the duplicate-key diagnostic (see the challenges below).
- **Final frontend set, once, stable sources:** `vitest run UsageEvidenceRail RunCockpitView RunCockpitSelectionOwnership RunEvidenceRefreshComposition LocalExecutionAdmissionSurfaces LocalExecutionReleaseInspectionSurfaces PlanningSequenceInspectionSurfaces AgentAttemptHistoryPanel LocalExecutionInspectionPanel PlanningSequenceInspectionPanel LocalCommitPanel AbandonRunPanel LocalCommitInspectionSurfaces layoutBoundary App.test.tsx App.queueInspection CockpitIdentity`:
  20 files (every filter matched at least one; `vitest list --filesOnly` shows both new files), 1054 tests passed, 14.1 s, no duplicate-key text and no stderr in the log.
- `npm run lint`: exit 0 in 1.1 s with nine existing `react(set-state-in-effect)`/`purity` warnings, all in untouched hooks and none in a changed file.
  `npm run build` (`tsc -b && vite build`, so the product typecheck): exit 0, 6.9 s, with the existing chunk-size notice.
  Strict e2e TypeScript check (the recorded flag set) over the current 54-file e2e/config inventory: exit 0, 5.3 s.
- **Journey host provenance:** the executor reported the four `DevalCopilot.*.dll` of the host bin (written 2026-10-26) hash-equal to the then-present `src/backend` outputs (written 2026-10-28 20:39). The first
  incremental serial `dotnet build` of `BrowserJourneyHost.csproj` **failed** (15.4 s, `CS2012`, reporting `VBCSCompiler` and Defender as possible lock owners; kept as `11a-…`), but it had already
  rewritten the four `src/backend` outputs with different hashes; the single retry succeeded (1.8 s) and the host bin and `src/backend` outputs are hash-equal again (`11c-host-provenance.txt`). No backend or test source
  differs from `HEAD`, and the successful build supplies fresh current-source evidence for the later journey. Hash changes alone do not establish which sources produced the earlier binaries or why bytes changed. The earlier equality is an executor report, not a separately retained before-build hash record; lock causation also remains unestablished.
- **Headed journey, once:** `npx playwright test -c playwright.journey.config.ts e2e/local-execution-queue.journey.ts --headed --trace off --workers 1`: 1 passed, 61.4 s (test 53.3 s); trace, video and screenshots
  off; no Inspector, debug, UI or manual Resume. It observed zero duplicate-key diagnostics from before the first navigation to the end. The other diagnostics it retained (not judged) were one 404 and five 409 resource
  failures and three SignalR "connection was stopped during negotiation" errors (also relayed by Vite), verbatim in `12-journey-local-execution-queue.txt`; none is explained or attributed here.
- **Hygiene:** `git diff --check` exit 0; the two new test files carry no CR or tab (their only non-ASCII bytes are the existing `…` of the product copy they match); the sensitive-pattern scan over the diff and new
  files found nothing; the existing `check-links.ps1` of the previous slice (reused, not re-proved against negative controls) over the three changed documents checked 64 links and fragments with 0 failures
  (`13-doc-links.txt`); the renamed previous-slice heading has no inbound reference; both planner inputs equal their dispatch hashes at the return.

Retained, not rerun and not run: the planner's baseline, control and identity logs under `logs/planner-review/cockpit-identity-01/` keep their unchanged-input scope. Not run: the whole Vitest suite, every backend suite,
client regeneration, the other journeys, the standalone harness tests and any mutation campaign (no schema, producer, ownership, transport, authentication or hook code changed). No earlier slice's evidence is fresh evidence here.

### Preserved development attempts and failures

All failed output is kept; none was rerun blindly.

- `02-rail-red-original-sources.txt`: my first rail test against the original sources failed all six at opening the history, because the rail re-rendered (the allowance and catalog reads resolved) and remounted the panel.
- `03-rail-fixed-sources-dev1.txt`: against the fixed sources one rail test failed on my own helper, which replayed an already-consumed `Response`; the helper now builds a fresh response per read.
- `06a-control-ABORTED-stderr-under-Stop.txt` (103 bytes): the first control run was aborted by my script's `Stop` error preference turning vitest's stderr into an exception before any result; its `finally` had restored the fixed sources
  (hashes in `06-control-identity.txt`) and the control was rerun once without that preference (`06b`).
- `11a-host-build-FAILED-CS2012-first-attempt.txt`: the host build failure above, retried once and not called flaky; the cause of the lock is not established.

### Challenges, limits and risks for review

- **The second collision is behavioral, not only diagnostic (new evidence for the planner).** The handoff claims none for `LocalCommitPanel` and `AbandonRunPanel`. With the original keys the new composition test shows a duplicated "Local commit" region and that region left mounted
  after the manual mode ends, which is the duplicated and omitted children React documents for non-unique keys. This holds in my harness (the run-scoped hooks and unrelated children replaced); I did not reproduce it in the real application, which keys the whole cockpit by run,
  so the real-user effect of this collision is unestablished.
- **Journey sensitivity is unproved by a control.** The journey passed on the fixed sources; no second journey was run on the original keys (the policy permits one run), so the new duplicate-key and preservation assertions are exercised against a fix but not shown red. Their sensitivity rests on the component tests above.
  Earlier slices recorded duplicate-key console errors in journeys on the earlier frontend; that observation is not a control for this journey, and it does not explain every earlier occurrence.
- **Test scope.** The rail test uses real clients over a fake network but observes the rail alone; the command-panel test mocks about 25 cockpit hooks and four clients and mounts neither the rail nor the real transport. Run replacement is proved at the component level by re-rendering without the app's outer key.
- **Journey 10b relies on Refresh evidence being a read.** It asserts no POST, no Agent-stage invocation and unchanged durable rows after it, and passed; account and model reads are the provider double's capability observations, not stages, and are excluded from that invocation check by `agentStageInvocations`.
- **Not in scope and not done:** the remaining sibling groups were only inspected; no repository-wide key cleanup, no new summary, replay, scheduling, recovery, provider or publication capability, and no selection of the next slice.

## Previous slice (independently verified published as a9e06ac; historical record): LOCAL-COMMIT-INSPECT-01 inspect recorded local-commit operations (ADR-0070)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git and the slice's independent publication-verification record, when available. This record alone grants no
publication authority.

**Delivery phase: documented recovery of existing unreviewed output (see [Recovery phase](#recovery-phase-2026-10-28)); the earlier first-return text below is
retained as the prior session's unreviewed claim, whose authoring session is not authenticated. The complete diff (including the four planner inputs) is
unstaged, uncommitted and unpushed. No GO exists.** Review and publication are separate later events; nothing here claims either.
Baseline: `main`, `HEAD` and local `origin/main` equalled `e7bd118b65dba6c9fde2a3f07e1fda773bc25b08` (PROJECT-VER-INSPECT-01, independently
verified published and neither repeated, republished nor reopened); divergence and the index were verified once before editing (the live remote was
not re-read by the executor). The four planner inputs (`decisions/README.md`, `mvp-delivery-plan.md`, `planner-handoff.md`, ADR-0070) equalled their
`logs/planner-review/local-commit-inspect-01/selection.txt` SHA-256 values at the start and were not edited. ADR-0050 stays Proposed, P9 unproven,
AI Memory deferred, Increments 4/5/6 incomplete; every standing restriction in the handoff stays. No frozen profile, native/provider/credential
experiment, real run, recovery, cleanup, commit or push was touched or performed; deterministic doubles remained the validation boundary.

### Recovery phase (2026-10-28)

A documented recovery transition decided by Codex ([planner-handoff.md](planner-handoff.md)), executed in the executor chat that had stopped at its first preflight. The
preflight matched `logs/planner-review/local-commit-inspect-01/recovery-01/preflight.json` exactly (`HEAD` and local `origin/main` `e7bd118b65dba6c9fde2a3f07e1fda773bc25b08`, empty
index, 17 modified and 52 untracked individually listed files, every one of the 69 SHA-256 values equal; the handoff is at its superseding `bf8160f1…0513` and the other three
planner inputs at their selection hashes; all four preserved unchanged). The existing code, the report below and the logs were treated as unreviewed evidence: their authoring
session (the "this chat" and "forked implementer" statements below) is not authenticated, and no result is attributed to a source or binary by timestamp alone. **No GO exists; nothing
is staged, committed or pushed; this recovery record is not acceptance.** New recovery output is under `logs/executor/local-commit-inspect-01/recovery-01/`; every earlier log and failure is unchanged.

**Audit result.** The complete tracked and untracked diff was read against ADR-0070 (one bounded five-table statement with `Take(2)`; association by the run key; the six-state tuple;
commit identity only for Completed; the thirteen-code allowlist; guard filter and fixed errors; closed API graph; raw-wire decoder, hook lifetimes and region; both mounts; journey and
journeyDb additions; documents). No divergence from the contract was found in the product code; two corrections were made (finding IDs stay in this chat):

- **R-1, historical copy accuracy.** The `local_commit.head_not_bound` explanation said the workspace head "was not bound to the recorded parent". The code (`LocalCommitGit.HeadBoundToBranchAsync`
  compares `symbolic-ref HEAD` with `refs/heads/<owned branch>`; the recovery policy also refuses a missing branch tip) is about HEAD naming the owned branch, not the parent. It now says "HEAD did not name
  the owned branch, or that the branch tip could not be read" (`localCommitInspectionCopy.ts`; no test asserted the old text). Observations left unchanged and out of scope: the existing live copy
  (`describeLocalCommit.ts`) carries the same loose `head_not_bound` wording, and the `authority_changed` ("the approval authority changed") and `unpromoted_state_unknown` explanations are narrower or looser than
  the code (the execution seam also compares the authority digest, workspace, lease and expected parent) but not false.
- **R-2, acceptance gap.** ADR-0070 requires tick precision, but only the frontend decoder and the journey exercised it. New `LocalCommitInspectionTickPrecisionTests` (11 cases, migrated file-backed SQLite):
  seven-digit ticks returned exactly, equal instants admitted, the writer's trimmed fractions, and a one-tick ordering violation Unavailable for Completed and for a Failed outcome recorded without a marker.
  A bounded compiling control (T1, `recovery-01/control-T1.ps1`, hash-guarded: Completed ordering compared at millisecond precision) was killed by 4 of the 11 cases and the file was restored byte-identical
  (SHA-256 `D98048594E6A5A76B50E41E8BF44EE81A9D225F762188FBC6EFF51BF269F41F5`, equal to the earlier controls' baseline). Its scope is that one edit.

**Named questions (interpretations remain proposals for the planner, none accepted).**

1. *Exact stored run key.* The operation is associated by the stored `RunId` text equal to the run's stored text (the unique `ix_local_commit_operations_run`, BINARY comparison). An operation whose stored `RunId`
   is lowercase, malformed or another run's therefore reads `NotRecorded` (cases `op_run_key_lowercase_is_not_the_association`, `op_run_key_of_another_run_is_not_the_association`), while a damaged
   operation `Id`, `ProjectId`, workspace or checkpoint identity reads `Unavailable`. This resolves a tension between two ADR clauses (absence is proved from the association only; the unique run index is the sole access
   path, no scan): a case-insensitive association would need a scan and is not what is implemented. The retained plan shows `SEARCH l USING INDEX ix_local_commit_operations_run (RunId=?)`. The production writer
   stores uppercase text, so only external corruption produces the other forms. Uncertainty: such corruption is indistinguishable from absence.
2. *Uppercase stored versus lowercase wire.* Stored identifiers are admitted only as uppercase 36-character text; `System.Text.Json` writes lowercase, and the decoder requires lowercase D-format and the
   requested run by strict comparison. A hypothetical uppercase run identity held by a caller would be refused as `invalid`; every identity in the product's own flows is a lowercase wire value.
3. *Numeric NULL, left joins and Int32 bytes.* The CLR `int`/`long` properties map to `NOT NULL INTEGER` columns (migration snapshot). The captured statement adds `OR column IS NULL` inside an AND chain whose first
   comparison is already NULL for an absent left-joined row, so the CASE yields NULL (never an admitted value); an absent workspace or checkpoint therefore reaches the reader as null and is Unavailable, and the
   present-operation flag is `l.Id IS NOT NULL`. The exact-INTEGER guards use a same-typed modulus plus direct maximum compares (44 numeric cases, extrema admitted, REAL/TEXT/BLOB/overflow refused). `totalBytes` is a
   `checked((int)...)` of a value already bounded to 0..4194304, an Int32 in the generated client and OpenAPI.
4. *The two collateral journey edits.* `{ name: 'Local commit', exact: true }` is required by this diff: Playwright name matching is a case-insensitive substring, which now also matches the new "Inspect recorded
   local commit" region (no other e2e file names that region). The added `'Inspect recorded planning'` entry in the selected-run button list is independent of this diff: `ProjectRunHistoryDetail` at `HEAD` already mounts
   `PlanningSequenceInspectionPanel` (commit `7949f37`) while `HEAD`'s journey list omitted its toggle. That is inferred from source, not from a journey run at `HEAD`; the fresh passing journey confirms the corrected list against current sources.
5. *The ten broadened frontend failures.* `LocalExecutionAdmissionSurfaces` (2) and `LocalExecutionReleaseInspectionSurfaces` (8) "current run in Usage & Evidence" cases wait for `/Recorded state/` and never find it. Retained
   `baseline-head-surfaces.txt` (a pristine `HEAD` worktree, which used this tree's `node_modules`; the worktree no longer exists) and `parent-vitest-consumers.txt` fail the **same ten test names** (compared in this recovery
   after whitespace normalization), and the same ten fail again on the current tree (below). A baseline reproduction proves failure on that baseline; it does not establish the cause and does not exclude a contribution
   by this diff to anything beyond those names. The cause is not established and nothing was changed for them.
6. *The first journey's 404.* Run 1 (`journey-run-1.txt`) failed at the first awaited inspection response, 404 instead of 200. The claim that the prebuilt journey host held a stale binary is **supported but not proven**:
   the preceding backend builds (`final-raw/api-tests-build.txt`, `api-build-*.txt`) do not build the separate `BrowserJourneyHost` project; run 2 followed an explicit host build and passed. No artifact or process evidence of the old
   host binaries survives (the build overwrote them), the 404 body was not captured and no other cause was excluded.

**Evidence, retained versus new versus unverifiable.**

- **New in this recovery, on current sources** (`recovery-01/`; the first four serial after `dotnet build-server shutdown`, non-incremental `-m:1`, 0 warnings, 0 errors): Api build 13.5 s with the generated client SHA-256
  `c601a846c31d5d3440abfbc924ca111814498627cd3db08cc73c15afe62d880d` after it and after the test builds, equal to the two retained builds' value (additive, 246 insertions, 0 deletions); Application dispatched filter **269 passed**
  (258 retained-scope tests plus the 11 new), 58.4 s; Api dispatched filter **139 passed**, 126.4 s; whole Architecture **225 passed**, 4.7 s. The four product assemblies of that fresh build are **byte-identical** to the
  copies in the `BrowserJourneyHost` output (built 2026-10-26 21:55; .NET builds are deterministic on equal inputs). That establishes byte equality of the current on-disk host assemblies with a fresh build of these sources; it does
  not authenticate the images a past journey process loaded (the first run's 404 stays unexplained), so it is not historical process evidence. `dotnet format --verify-no-changes` over the 40 changed
  C# files exit 0 (29.8 s); every new C# file has one top-level type matching its name. Frontend (the dispatched list, 21 files): **1074 passed** (31.9 s); `npm run lint` exit 0 (nine existing warnings, none in the new files); `npm run
  build` exit 0 (includes `tsc -b`; the chunk-size note is the existing stderr); strict e2e `tsc` over 54 files exit 0. Broadened consumers (4 files): **10 failed, 21 passed**, the ten named above. Extended collaboration journey
  rerun once, announced and headed (`--headed --trace off --workers 1`; no Inspector, `--debug`, `--ui`, `PWDEBUG` or manual Resume), **1 passed, 2.0 m**, `journey-run.txt`; the React duplicate-key console error (1355 occurrences) and
  14 stopped SignalR negotiations are the noise recorded for the earlier runs, their cause not investigated. Hygiene (`recovery-01/check-links.txt`): 282 links and fragments over the nine changed or planner
  documents, 0 failures (a negative control proved the checker detects a missing file and fragment); `git diff --check` clean apart from the generated client's CRLF notice; the 53 new files hold no CR or tab byte; the
  sensitive-pattern scan found only the clearly fictitious `not-a-real-session` test value in four new tests; the four planner inputs equal their recorded hashes at the return.
- **Retained, not rerun, at its original scope:** the twelve compiling controls C1 to C12 (`controls.log`: all killed, files restored; the six edited sources are byte-identical now to the baseline hashes that log recorded);
  the backend matrix of 21:46 to 21:53 (258/139/225, superseded for the current sources by the fresh run); the first format failure (two whitespace findings, 2026-10-26); the implementer's frontend matrix, the first failed
  build and the two history-detail test failures; the broadened and baseline logs; both earlier journey runs.
- **Not run:** the other journeys (they were not selected; none of them names the new region), the whole Application, Api, Infrastructure, Domain and Vitest suites, any real-provider, native or recovery check.
- **Unverifiable:** the authoring session of the existing output; whether any source changed between the earlier logs and now beyond what the byte-equal binaries and hashes above show; the cause of the ten failures and
  of the first journey's 404 (above); the earlier frontend run against its source, the dev server having left no artifact (superseded by the fresh frontend matrix and journey).

### Correction 1 (Codex review 1 NO-GO: LCMI-R1, P2)

Planner finding **LCMI-R1** (distinct from the executor-reported R-1 and R-2 above, neither renumbered nor dropped; open until Codex's next review): `localCommitInspection.ts` admitted the default UTC instant
`0001-01-01T00:00:00.0000000Z` through the shared shape-only `isUtcRoundTrip`, so a Prepared, or a Completed with otherwise ordinary later times, operation carrying it as `createdAtUtc` decoded as Recorded although
the backend refuses the stored default. Correction, frontend decoder only: an operation-owned `isRecordedInstant` (the shared parser result, and not the default instant) now guards `createdAtUtc` and every supplied
`executionStartedAtUtc` and `completedAtUtc`; canonical seven-digit formatting, full-tick string ordering, nullable-marker semantics and legitimate year-0001 values (`0001-01-01T00:00:00.0000001Z`) are unchanged, no
millisecond `Date` is involved, and the shared parser, the backend and the other inspection contracts are untouched. Four permanent regressions in `localCommitInspection.test.ts` (adapted from the archived reviewer
diagnostic): the Prepared reproduction, the Completed reproduction, default markers/completions that would otherwise be coherent (all-default Completed, Failed and NeedsAttention without a marker) and the one-tick positive boundary
with tick ordering. A bounded control (`recovery-01/correction-01/control-default-instant.txt`: the decoder's default check removed, file restored byte-identical) fails the three refusal regressions and keeps the positive one.

- **Fresh on the corrected source** (`recovery-01/correction-01/`): focused `localCommitInspection.test` **43 passed** (2 files by the filter), 2.1 s; the dispatched frontend selection (21 files) **1078 passed** (1074 plus the four regressions), 14.3 s;
  `npm run lint` exit 0 (the same nine existing warnings, none in the new or changed files); `npm run build` exit 0 (includes the product `tsc -b`). Hygiene is recorded below.
- **Retained, with reasoning:** backend (Application 269, Api 139, Architecture 225, format, client SHA-256 `c601a846…`) covers sources that this correction does not touch (no `.cs` file, schema, API contract or generated client
  changed; the client equals its fresh-build hash); strict e2e `tsc` over 54 files covers `e2e/` and the two Playwright configs, which are unchanged, and the corrected module is not among them; the fresh recovery journey ran
  against the previous decoder. It never rendered a default instant (its real operation carries a nondefault time), so the correction cannot change what it asserted, but it is **not** evidence of the corrected frontend and no
  journey was rerun. The reviewer's original ENOENT suite-load failure, its interrupted restricted Application attempt and their later actual results stand as recorded in `review-01/`; later success establishes no cause for those tool failures.
- **Not run:** a routine backend matrix, client regeneration, mutation campaign beyond the control above, and any browser rerun.

### What exists

- **Application.** `GetLocalCommitInspectionQuery` (run id only) with its handler, a validator (a nil run is the fixed `validation.invalid_run`) and a
  closed result (`NotRecorded`, `Unavailable`, `Recorded` with one operation), plus one operation-owned policy folder
  (`Policies/RecordedLocalCommit`). **One** bounded no-tracking EF statement (`RecordedLocalCommitReader`, at most two rows, `Take(2)`) reads the registered
  run and project (inner joins), the operation associated by the unique run key and that operation's named workspace and named checkpoint (left joins),
  so an associated operation whose workspace or checkpoint is missing is `Unavailable`, never absent; no operation is `NotRecorded`; an unknown run or a run
  whose registered project is absent is the fixed `local_commit_inspection.not_found`. The guarded scalar projection (`RecordedLocalCommitProjection`)
  carries every identifier and time as raw stored bytes within their bound (parsed by `RecordedLocalCommitStorage`: canonical uppercase 36-character
  identifiers, the provider writer's UTC time text), classifies the status in the database, guards the checkpoint number (1 to Int32.MaxValue, the maximum compared
  directly), changed-path count (1 to 128) and total bytes (0 to 4194304) as exact stored INTEGERs, bounds every text by its stored bytes and selects the commit
  SHA only while the stored status is Completed. `RecordedLocalCommitReader.Prove` requires a ManualAgent run, the operation's own run and project, the workspace in
  the project with exactly the operation's branch, the checkpoint in that workspace with exactly the pinned number, fingerprint and parent head, lowercase 40/64-hex
  identities and a branch of at most 512 characters without control characters; `RecordedLocalCommitTuple` enforces the complete status/time/reason/commit tuple of
  ADR-0070 (Failed, Interrupted and NeedsAttention may lack the execution marker and no time is invented), and `RecordedLocalCommitReasons` maps the thirteen
  allowlisted codes to Known and any other bounded nonblank reason to Unrecognized with no code. Nothing is written, no authority member, digest, message, author,
  physical receipt, artifact, lease, Git, file, process or provider is read, and no exception is swallowed. The existing physical `LocalCommitInspection` port
  model, the live status query, the receipt and run-history discovery are untouched.
- **API.** One protected bodyless MVC GET, `GET /api/runs/{runId}/local-commit-inspection` (`GetLocalCommitInspectionEndpoint`, one class and one action behind
  `RunsBaseEndpoint`, dispatching through `IApplicationMediator`), behind `LocalCommitInspectionGuardFilter`, which refuses before binding and dispatch any query
  key or text (`local_commit_inspection.query_not_allowed`) and a declared length, any transfer encoding or a readable undeclared byte
  (`local_commit_inspection.body_not_allowed`). API-owned closed response graph (`GetLocalCommitInspectionResponse`, `LocalCommitInspectionOperationResponse`,
  `LocalCommitInspectionReasonResponse`; times as UTC round-trip strings, `totalBytes` an Int32). NSwag regenerates the client reproducibly, additive only (246
  insertions, 0 deletions).
- **Frontend** (implemented by a forked implementer, reviewed and re-run by this chat). `localCommitInspection.ts` (whole-answer raw-wire decoder: exact keys at every
  level, requested run, lowercase wire GUIDs, exact bounds, the full tuple, the allowlist; any violation refuses the whole answer), `localCommitInspectionWire.ts`
  (parsed-body wire client with a real abort signal), `useLocalCommitInspection` (run/opening/read-visit lifetimes over the unchanged `useOwnedLifetime` /
  `useOwnedState`, a real ten-second abort, read only on open or explicit reload, a reload clears the old answer at once), `localCommitInspectionCopy.ts` and the
  collapsed **Inspect recorded local commit** region (`LocalCommitInspectionRegion`), mounted once beside `LocalCommitPanel` in `RunCockpitView` (same ManualAgent
  condition) and once in `ProjectRunHistoryDetail` for every selected run, after the receipt line, with or without a receipt source. `LocalCommitPanel`, the receipt and
  history discovery are unchanged; the generated client is registered in `clients.ts`.
- **Journey.** `e2e/collaboration.journey.ts` is extended at its completed-delivery phase and at its later historical-run phase (details under Evidence); two small
  collateral edits are described under Challenges. `e2e/journey/journeyDb.ts` gains one additive read of the stored operation facts.
- **Documents.** The explicit-local-commit recovery section of data-and-recovery, the cockpit specification (new "Recorded local commit inspection" section and one
  pointer in Run history), the security plan (one boundary row), the engineering context and this record. The four planner inputs are included unedited.

### Evidence (logs under `logs/executor/local-commit-inspect-01/`, git-ignored; commands run at the repository root unless a frontend script)

Backend matrix: `final-matrix.ps1`, `final-matrix.log`, raw outputs in `final-raw/`. Fresh on the final stable sources unless stated:

- **Build and generation.** Build server shut down, then two serial non-incremental Api builds (`-m:1`) produced byte-identical `api-client.ts` (SHA-256
  `c601a846c31d5d3440abfbc924ca111814498627cd3db08cc73c15afe62d880d`, `identical True`; `git diff --numstat` 246 insertions, 0 deletions). The Application, Api integration and Architecture test projects built non-incrementally with 0 warnings and 0 errors. Nothing was edited in the generated file.
- **Application**, the dispatched filter (`LocalCommitInspection|LocalCommitOutcomeRecorderTests|LocalCommitRecoveryPolicyTests|GetProjectRunHistoryQueryHandlerTests`):
  **258 passed**, 60 s (203 new, in `Features.Runs.RecordedLocalCommitInspection`; 55 unchanged existing). Real migrated file-backed SQLite: all nine recorded tuples (Prepared, Executing,
  Completed, Failed/Interrupted/NeedsAttention each with and without the execution marker) with exact identities, numbers, times and the commit identity only for Completed; the thirteen
  allowlisted reasons and ten unrecognized ones (prefix lookalike, trailing/leading blank, upper case, native detail with a path, 128 characters); NotRecorded beside a recorded run;
  fixed 404 for an unknown run and for an absent registered project; **96 named damage cases** (operation, workspace, checkpoint and run identities lowercase, blank, oversized,
  embedded NUL, braced, nil; foreign project, workspace and checkpoint; missing workspace and checkpoint rows; run modes Legacy, Simulated, text, out of range, fractional and oversized;
  branch, parent, tree, fingerprint and commit-SHA shapes; created, started and completed times; every status/time/reason/commit tuple violation; unknown, lower-case, empty and numeric
  status; a second operation for one run with the unique index dropped) each proved Recorded first, then exactly Unavailable, NotRecorded or the fixed 404; **44 exact-number cases** (Int32.MaxValue
  and 1 for the checkpoint number, 1/128 for the path count, 0/4194304 for the bytes admitted; zero, negative, overflow, a truncated 64-bit value, REAL, TEXT and BLOB refused); an operation of
  another registered project, a foreign workspace/checkpoint and a damaged operation of another run; survival of later work (newer checkpoint, workspace `MissingExternally`, an abandoned
  run) and of a file-backed reopen; durable fingerprint unchanged in every shape. **Read proof:** exactly one statement per read (also for NotRecorded and 404), SELECT only, `LIMIT @p = 2`,
  exactly the five tables `runs`, `projects`, `local_commit_operations`, `git_workspaces`, `git_checkpoints`, none of 40 forbidden column/table names, EXPLAIN QUERY PLAN driven by `ix_local_commit_operations_run`
  with no scan or sort, a damaged commit SHA of a non-Completed operation never disclosed, the handler's only dependency the database context, and injected database faults on each table propagating.
  The captured statement and plan are kept in `recorded-statement-and-plan.txt`.
- **Api**, the dispatched filter (`LocalCommitInspection|LocalDeliveryReceipt|ProjectRunHistory|LocalCommitEndpointTests|LaunchSessionAuthentication`): **139 passed** (35 new, 104 existing). Run twice: 139/139 in
  127 s, then once more after a whitespace-only test-helper edit (`final-raw/api-matrix-2.txt`, 139/139). The new class covers authentication (anonymous, wrong secret, query token), the four wrong verbs, the
  route inventory (exactly one authorized GET and no anonymous route under `api/runs`), three body shapes plus the undeclared readable byte through `TestServer` (refused before dispatch even for a nil run),
  thirteen query shapes (blank, unknown, repeated, differently cased, token-like) refused before dispatch with fixed text that repeats nothing, a lone `?` read normally, the fixed 404 for an unknown run and an
  absent registered project (identical bodies), the nil-run validation, the closed wire of all nine recorded shapes (member order, canonical times, commit only for Completed, reasons), an unrecognized reason with
  no raw text, NotRecorded, Unavailable over damaged storage, a real database fault (dropped table) answered by the host's 500 failure boundary and none of the three statuses, zero durable change and no Git
  adapter call (tripwired ports), and the actual OpenAPI contract (one path, `runId` only, no request body, 200/400/404, closed schemas, `totalBytes` int32).
- **Architecture**, whole project: **225 passed** (219 before; six new boundary facts for ADR-0070: read-only five tables, no live policy or latest row, no swallowed fault or forbidden column, one statement with the
  commit only behind Completed, endpoint shape and guard).
- **Format and hygiene.** `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 39 new C# files>`: the first run **failed** (exit 2: two WHITESPACE findings in my API test helper,
  `final-raw/format-verify.txt`, kept); after the fix the same command is clean (exit 0, 29.6 s, `final-raw/format-verify-2.txt`). Every new C# file has exactly one top-level type and a matching file name (checked, 39/39). Changed-document links
  and fragments: see below. No dependency, package, configuration, migration or schema file changed.
- **Compiling controls** (`controls.ps1`, `controls.log`; serial, hash-guarded, every restored file byte-identical, run before the final matrix; not a mutation campaign): twelve controls, **all killed**: C1 commit identity projected for
  every status (66 failed), C2 ManualAgent no longer required (2), C3 unparseable operation Id read as absence (6), C4 branch compared ignoring case (1), C5 second associated row ignored (10), C6 Int32.MaxValue extremum dropped (1),
  C7 exact-INTEGER guard dropped for total bytes (2), C8 Completed time order relaxed (1), C9 Executing start order dropped (1), C10 allowlist case-insensitive (1), C11 lowercase identifier admitted (3), C12 undeclared-body peek removed from the guard (1, Api).
  Their scope is only these twelve edits; masking by a second defense (for example the reader's own commit gate behind the projection's) was deliberately not tested.
- **Frontend** (fresh, run once by this chat after the forked implementer's own matrix, which agrees): the six new LocalCommitInspection files plus the dispatched existing files (the substring filters also select `LocalCommitPanelReceipt`,
  `LocalCommitPanelGitHubTarget`, `localDeliveryReceiptContract` and `ProjectRunHistoryPanelSelection`): **21 files, 1074 passed**, 17 s (87 new tests: decoder 20, contract, hook, transport with a real abort and fake timers, region, and the
  two-surface composition with and without a receipt source). `npm run lint` exit 0 (only pre-existing warnings), `npm run build` exit 0 (includes the product `tsc -b`), the separate strict e2e `tsc` over 54 files exit 0
  (inventory in `parent-strict-e2e-inventory.txt`). Broadened shared-consumer run (`parent-vitest-consumers.txt`; 14 files that render the changed views): **185 passed, 10 failed** — the same ten
  `LocalExecutionAdmissionSurfaces` / `LocalExecutionReleaseInspectionSurfaces` "current run in Usage & Evidence" cases that also fail identically on a pristine worktree of `e7bd118` (`baseline-head-surfaces.txt`);
  not caused by this slice and not explained here (see Challenges).
- **Journey**, announced to the owner before the run, then run headed with `npx playwright test -c playwright.journey.config.ts e2e/collaboration.journey.ts --headed --trace off --workers 1` (no Inspector, `--debug`, `--ui`, `PWDEBUG` or manual
  Resume; trace, video and screenshots off). **Run 1 failed** at the first awaited inspection response (404 instead of 200, `journey-run-1.txt`, 40 s): the journey host executable
  (`BrowserJourney/Host/bin/Debug/net10.0/BrowserJourneyHost.exe`) is a prebuilt binary and I had not yet rebuilt that project, so it did not contain the route; this is a validation-order mistake of mine, not a product failure. After a
  serial non-incremental build of the journey host (`journey-host-build.txt`, 0 warnings, 0 errors) **run 2 passed: 1 passed, 1.9 m** (`journey-run-2.txt`), the only browser run on the final binaries. It
  used the production-written owned fixture, normal authentication and supervisors and the deterministic executables. After the delivery settled it baselined the business POSTs (`PostInventory`), the stage-executable invocations and a durable fingerprint, then at the completed-delivery phase: cold collapsed
  region (one button, `aria-expanded=false`, no read), open (awaited exact 200 response equal to the stored operation row: Completed, the delivered commit, branch, parent, tree, counts, checkpoint, creation, marker and completion times, no reason), the rendered
  facts compared with the same row, an explicit reload (one more exact response, identical), close (answer gone, nothing read) and the same operation as the delivery receipt reports (identity, commit, parent, tree); and at the later historical-run phase the same exact answer for the older run inside Run history
  beside its receipt source (no receipt request, no live Local commit panel), the newer run with no receipt source answering NotRecorded, a collapsed A-to-B-to-A return that read nothing, and a collapsed region after a real page reload with no read.
  Every inspection added no business POST, no executable invocation and no durable change. **The fingerprint covers exactly:** the project's row totals (runs, attempts, messages, events, executions, operations, reviews, checkpoints), the operation's stored disclosed facts, the invocation count of the doubles,
  the worktree HEAD and status and the source repository snapshot; it is not a byte identity of every stored row or file. A page reload is not a host restart (file-backed reopening is proved at the Application layer). The passing run also confirms the formerly stale expectation that the selected run's
  button list lacked "Inspect recorded planning". The React duplicate-key console errors (one repeated key, 1382 occurrences in run 2, 938 in the failed run 1) appear in this journey as in earlier journeys; whether the new region contributes is not established (its own lists key on unique strings) and was not
  investigated; one SignalR negotiation was stopped while the page was closing.
- **Remaining hygiene.** Changed-document links and fragments (`check-links.ps1`, negative controls proved it detects a missing file and fragment): 280 links and fragments over the nine changed or planner documents, 0 failures; the one heading renamed in this record (the previous slice's) has no inbound reference. `git diff --check` exit 0 (the generated client's CRLF notice only); the new files carry no CR or tab; the sensitive-pattern scan found only the existing
  `journeySecret()` identifier; the four planner inputs equal their `selection.txt` SHA-256 values again at the return.
- **Planner inputs.** The four planner inputs equal their `selection.txt` SHA-256 values at the start (verified before editing); the final check is recorded below.

Retained, not rerun and not run: the whole Application, Api, Infrastructure and Domain suites, the whole Vitest suite, the other journeys and the standalone harness tests (no schema, producer, ownership, shared transport,
authentication or lifetime code changed). No earlier slice's evidence is fresh evidence for this one.

### Preserved development attempts and failures

All failed output is kept; none was rerun blindly.

- First Application build of the new classes was green and its first run passed 18 of 18, which looked too easy: the controls above were added to show the tests are sensitive.
- First full Application run of the new classes: 201 of 203; both failures were defects of my tests (`ExecuteSqlRawAsync` treats `{`/`}` as format placeholders in the braced-identifier case; two workspaces of two projects collided on `WorkspaceNumber`).
  First Architecture run: one of six new facts failed because its window did not include the null guard that precedes the Completed check (fact corrected, no product change).
- First Api run: 15 of 33 failed on my own assumption that the Problem Details error member is `description` (it is `detail`); one more failed because the shared Problem Details `instance` member names the request path (the check now excludes it).
  A first attempt at the database-fault test asserted too much about the developer failure page and was narrowed to the 500 status and the absence of any of the three statuses.
- The first control-script launch was refused by the permission classifier (it used `-ExecutionPolicy Bypass`); it was relaunched without altering the policy.
- Frontend (implementer): the first build failed on `RunCockpitView.tsx(667,56)` TS2322 (`string | undefined`), fixed with `cockpit.projectId ?? null`; the first matrix had two failures in existing tests that enumerate the history detail's buttons (they correctly saw the new toggle; both expected lists
  were updated, original output kept in `final-vitest.txt`).

### Challenges, limits and risks for review

- **No material challenge to ADR-0070.** Interpretations put to the planner:
  1. *Association by exact stored run key.* An operation whose stored `RunId` is not that run's exact stored (uppercase) form is not associated with the run, so the read is `NotRecorded` for it (case named
     `op_run_key_lowercase_is_not_the_association`). A case-insensitive join would abandon the unique `ix_local_commit_operations_run` access path ADR-0070 requires. A differently cased operation `Id`, `ProjectId`,
     `GitWorkspaceId` or `GitCheckpointId` is Unavailable.
  2. *Wire GUIDs are lowercase.* ADR-0070's "uppercase 36-character form" is the stored form; the host serializes GUIDs in lowercase and the decoder (like the sibling inspection decoders) requires lowercase D-format and refuses uppercase.
  3. *`totalBytes` is an Int32 on the wire* (the admitted range 0 to 4194304 fits; the Domain value stays a long).
  4. *Generated null guards.* The translator adds `OR column IS NULL` to the numeric guards of left-joined columns; that only affects the absent-entity branch (a stored column is never NULL) and is visible in `recorded-statement-and-plan.txt`.
- **Not a receipt, replay or authentication.** A Recorded operation is unsigned relational consistency; matching rewrites of the recorded rows stay outside the assurance. Recorded Completed is bookkeeping, not a live Git observation, current source applicability or publication; it does not prove the admission lineage.
- **Collateral journey edits (please review).** The existing `getByRole('region', { name: 'Local commit' })` became `exact: true` (Playwright names are case-insensitive substrings and would now also match "Inspect recorded local commit"); the existing selected-run button list in the journey lacked the "Inspect recorded planning" toggle that
  an earlier slice mounted (the Vitest list already had it) and now lists it with the new toggle. Whether the latter was really stale is confirmed only by the journey run.
- **Ten pre-existing frontend failures** in two broadened files (above) fail identically on a pristine `e7bd118` worktree; their cause is not established and nothing was changed for them.
- **Known-reason copy** is a fixed literal restatement of each code ("the host recorded that ..."); the wording is for the planner's review.
- **Journey scope.** It proves the Recorded (Completed) path of one operation and the NotRecorded run through the browser; the other five states, damaged storage, tuples, timeouts and lifecycle races are proved at the Application, Api and component boundaries. Its durable fingerprint covers named fields and counts, not a byte identity of every row or file; a page reload is not a host restart.
- **Not in scope and not done:** recovery, retry, release, approval, commit and publication behavior, a real-provider or real-run read, any frozen-experiment renewal, polling, prefetch, export or browser persistence.

## Previous slice (independently verified published as e7bd118; historical record): PROJECT-VER-INSPECT-01 inspect recorded project verification executions (ADR-0069)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git and the slice's independent publication-verification record, when available. This record alone grants no
publication authority.

**Delivery phase: first return to Codex for review, by the new Claude executor chat (superseded by [Correction 1](#correction-1-codex-review-1-no-go-pvi-r1-p2-and-pvi-r2-p3)); the complete diff (including the four planner inputs) is
unstaged, uncommitted and unpushed. No GO exists.** Review and publication are separate later events; nothing here claims either.
Baseline: `main`, `HEAD`, local `origin/main` and the live ref equalled `7949f37668641e9f5cd3468d12947a32f9fc6ff8` (PLAN-INSPECT-01, independently
verified published and neither repeated nor reopened) at the start and, for `HEAD` and the live ref, again at the return; divergence 0/0 and an empty
index were verified once before editing. The four planner inputs (`decisions/README.md`, `mvp-delivery-plan.md`, `planner-handoff.md`, ADR-0069) equal
their `logs/planner-review/project-ver-inspect-01/selection.txt` SHA-256 values at the start and at the return and were not edited. ADR-0050 stays
Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every standing restriction in the handoff stays. No frozen profile,
native/provider/credential experiment, real run, recovery, cleanup or commit/push was touched or performed; deterministic doubles remained the
validation boundary.

### What exists

- **Application.** Three queries (`GetProjectVerificationHistoryQuery`, `GetProjectVerificationInspectionQuery`, `GetProjectVerificationOutputWindowQuery`)
  with closed results, operation-owned validators and one policy folder (`Policies/ProjectVerificationInspection`). The page reads at most 21 scalar rows
  through the unique `(ProjectId, ExecutionNumber)` index (`ORDER BY ExecutionNumber DESC LIMIT 21`), admits the 20 displayed rows (identity, positive
  Int32 number, snapshot name, claim time) and only the identity and number of the lookahead row, and refuses the whole page when any of them cannot be
  admitted. The selected execution is a database membership predicate over the stored identities (an unknown project, an absent or foreign execution
  is the fixed `project_verification_inspection.not_found`) followed by **one** bounded EF statement over the execution, its workspace and its named
  checkpoint (at most two rows) and one pure proof: workspace in the project, checkpoint in that workspace with a positive Int32 number and the claim
  fingerprint, 64 lowercase hex fingerprints, nondefault UTC times with claim <= dispatch <= completion at tick precision, and the complete supported
  status/outcome/exit-code/completion-fingerprint tuple (`ProjectVerificationExecutionTuple`, including SourceChanged's differing completion fingerprint).
  Every identifier and time is raw stored bytes within its bound parsed by one strict parser (canonical uppercase 36-character identifier, the writer's
  UTC time text), every enum is classified by the database and every number is an exact stored INTEGER in range (the three Int32 extrema compared
  directly), so malformed storage is Unavailable and never a fault. The two sealed streams are resolved per exact execution and purpose with at most two
  rows and admitted by the **unchanged** `RecordedVerificationOutputAdmission`; the window re-proves the execution first, reads only its requested stream's
  row, applies the unchanged storage admission and an offset check, and calls the unchanged `IArtifactStore.VerifyAndReadSealedAsync`. Metadata and page
  handlers take no artifact store. No current recipe, run, attempt, Agent membership, sequence, lease, eligibility, Git, filesystem, process or provider
  reader is consulted, nothing is written and no exception is swallowed. No table, migration, event, producer, worker or dependency was added; the Agent
  and sequence handlers and their shared projections are unchanged.
- **API.** Three protected, bodyless MVC GETs, `/api/projects/{projectId}/verification-history`, `.../{executionId}` and
  `.../{executionId}/artifact-window/{stream}`, one class and one action each behind `ProjectsBaseEndpoint`, dispatching through `IApplicationMediator`
  behind one guard filter (abstract base plus three tiny sealed key lists) that runs before binding: body refusal (declared length, any transfer encoding,
  a readable undeclared byte) `project_verification_inspection.body_not_allowed`; query refusal `.query_not_allowed` for anything but the exact ordinal,
  once-only keys of the operation (history: `beforeExecutionNumber`; window: `fromOffset`, `maxBytes`; detail: none). Operation-owned model binders refuse a
  blank, non-canonical or overflowing value (and the validators the out-of-range ones) with the shared validation problem, never clamping or echoing.
  API-owned closed response graphs; NSwag regenerates the client reproducibly, additive only (569 insertions, 0 deletions).
- **Frontend.** `projectVerificationInspection.ts` (whole raw-wire decoders: exact keys, requested identity, canonical identifier/time/fingerprint forms,
  descending unique page keys with the cursor, the host's result tuple, the selected answer bound to the indexed number, snapshot name and claim time, and
  ADR-0052's byte-cursor invariants through the shared descriptor decoder), `projectVerificationInspectionWire.ts` (parsed-body wire clients with a real
  abort signal), `useProjectVerificationInspection` (project, opening, page-visit, execution-visit and stream-visit lifetimes over the unchanged owned-lifetime
  primitives, ten-second bounded reads, the shared `useRecordedStreamWindow` machine with its 16 KiB manual windows and 256 KiB view cap), fixed copy and the
  collapsed **Inspect recorded verifications** region (`ProjectVerificationInspectionPanel`/`...Detail`) mounted once in `CandidateWorkspacePanel`, right after
  `VerificationCommandsPanel` and outside it. The live list, its polling and `VerificationExecutionOutputViewer` and every execution/configuration control are
  unchanged.
- **Journey.** `e2e/project-verification-inspection.journey.ts` (new, one authenticated owned-fixture journey, details under Evidence).
- **Documents.** The evidence-inspection architecture document (new section "Project verification history and inspection"), the cockpit specification
  ("Inspect recorded verifications"), the security plan (one boundary row), the engineering context and this record. The four planner inputs are included
  unedited; ADR-0069's status line and the roadmap wording of the planner were not touched by the executor.

### Evidence (logs under `logs/executor/project-ver-inspect-01/`, git-ignored; commands run at the repository root unless a frontend script)

One concise log per matrix: `final-matrix.log` (backend; raw outputs in `final-raw/`) and `final-frontend.log` (frontend; `final-frontend-raw/`); the journey
output is `journey-run-1.txt`. Fresh on the final stable sources unless stated:

- **Build and generation.** Two non-incremental Api builds (`-m:1`, serial, build server shut down first) produced byte-identical `api-client.ts`
  (SHA-256 `cd5d0b82a4318f546573d657e5bfbe6b4e18416f68f296e69ed4a68953b215ef`; `git diff --numstat` 569 insertions, 0 deletions, additive only); the
  Application, Api integration, Architecture and journey-host projects built non-incrementally with 0 warnings, 0 errors. Nothing was edited in the generated file.
- **Application**, `--filter "FullyQualifiedName~ProjectVerificationHistory|FullyQualifiedName~ProjectVerificationInspection|FullyQualifiedName~GetAgentAttemptVerification|FullyQualifiedName~LocalExecutionVerificationOutputWindow"`:
  **456 passed**, 106 s (260 new in `Features.Projects.ProjectVerificationHistory` and `...ProjectVerificationInspection`; the other 196 are the unchanged ADR-0052/0061
  regression of the shared stream primitives). Real migrated file-backed SQLite: empty, partial and multiple pages, every cursor/lookahead boundary (20/21/40/41,
  before 1/2/21/22/Int32.Max), cursor walks of 45, an older manual execution beyond the latest 20 beside a newer run/workspace/checkpoint/execution with no
  Agent, attempt or sequence row, another project, snapshot names surviving recipe rename/disable/removal, reopening, every Domain result shape (13), the Int32 exit-code
  extrema, a different SourceChanged completion fingerprint, 24 history and 86 selected-execution damage cases (identifiers, times, numbers, fingerprints, names,
  workspace/checkpoint ownership and the contradictory result tuples), 15 descriptor and 24 window-storage defects, sibling isolation, multiwindow UTF-8, missing/tampered
  files, offset and cap bounds, NotFound/Unavailable before any store call, per-window re-proof, SELECT-only, forbidden-column, statement-count, EXPLAIN QUERY PLAN
  (index-driven, no sort or scan) and zero-write proofs, database faults and store faults propagating.
- **Api**, `--filter "FullyQualifiedName~ProjectVerificationHistory|FullyQualifiedName~ProjectVerificationInspection|FullyQualifiedName~AgentAttemptVerification|FullyQualifiedName~LocalExecutionInspectionEndpoint|FullyQualifiedName~LaunchSessionAuthentication"`:
  **228 passed**, 76 s (93 new: complete authentication and method matrix for all three routes, the explicit MVC authorization inventory (exactly three routes, each an authorized
  GET, no anonymous endpoint), three body shapes, 29 query-key shapes, 36 rejected values, fixed 404 identity matrix, closed wire graphs of pages, metadata and windows,
  Unavailable over damaged storage, the older-than-twenty path, no write or artifact read, a second host over the same database and the actual OpenAPI graph).
  Existing project routes (`FullyQualifiedName~Features.Projects`, the shared consumer of the `api/projects` route family): **288 passed**, 62 s.
- **Architecture**, whole project: **219 passed** (210 before; nine new boundary facts for ADR-0069).
- **Frontend** (fresh, run once by this chat on the final sources after the forked implementer's own matrix, which agrees): the five new test files
  **77 passed** (decoder 26, contract 6, hook 24, panel 17, composition 4); the planner-named existing `VerificationCommandsPanel`, `VerificationRunRefresh`,
  `VerificationRunOwnership`, `CandidateWorkspacePanel` and `layoutBoundary` tests **45 passed**; `projectOwnership -t "useVerificationExecutionOutput ownership"`
  **3 passed** (33 skipped by the name filter); `npm run lint` exit 0 (only pre-existing warnings, none in this slice's files); `npm run build` (includes the product `tsc -b`)
  exit 0; the separate strict e2e `tsc` (`npx tsc --ignoreConfig --noEmit --strict --target es2023 --module esnext --moduleResolution bundler --skipLibCheck --noUnusedLocals
  --noUnusedParameters --verbatimModuleSyntax --allowImportingTsExtensions --erasableSyntaxOnly --types node` over the 52 e2e files of `e2e/*.ts`, `e2e/journey/*.ts` and `e2e/harness/*.ts`
  plus `playwright.config.ts` and `playwright.journey.config.ts`, which now include the new journey; the true exit status was captured, inventory in `strict-e2e-inventory.txt`) exit 0.
  The implementer's broadened shared-consumer run (the seven other `CandidateWorkspacePanel` component tests plus `App` and `App.queueInspection`, 149 passed) is retained evidence of
  its own earlier matrix (`frontend-matrix.log`), not repeated here.
- **Format and hygiene.** `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 66 new or modified C# files>` clean (exit 0, 29 s); `git diff --check` exit 0 (the generated
  client's CRLF notice only); the new files carry no CR or tab; non-ASCII appears only in UI copy and in multibyte test inputs; no dependency, package or configuration file changed.
- **Journey**, one automatic headed run on the final binaries (`playwright test -c playwright.journey.config.ts e2e/project-verification-inspection.journey.ts --headed --trace off --workers 1`;
  no Inspector, `--debug`, `--ui`, `PWDEBUG` or manual Resume; trace, video and screenshots off; the owner was told before the run in the executor's message): **1 passed**, 51.6 s (58.5 s wall).
  Setup used rendered controls only: project, workspace, checkpoint #1, one recipe and manual verification #1 (no run, Agent attempt or sequence exists then), a manual run and checkpoint #2, then 20
  more manual verifications (21 in all; the live latest-20 list, read once, no longer holds #1). After the setup settled it baselined the business POSTs, the 21 executable invocations and a durable
  fingerprint, then: cold collapsed region with no history read; open (awaited newest-page response: 20 items #21 to #2, `hasMore`, cursor 2, every member compared with the database); **Older verifications**
  (awaited response for `beforeExecutionNumber=2`: #1 only, page replaced); select #1 (awaited metadata response compared with the database: Failed/Exited/1, checkpoint #1 not the latest, fingerprints,
  times, both descriptors; no window read); explicit View and Load of each stream (awaited windows equal to the sealed bytes on disk, whose SHA-256 equals the recorded hash); a page reload (cold closed
  state, no read until opened); reopen, older, select and the explicit **Reload recorded verifications** (one new response, selection and content cleared). The inspection added no business POST, no
  invocation and no durable change. **The fingerprint covers exactly:** the project's execution, checkpoint, run, attempt, event, message and output row counts; every execution's number, status, outcome,
  exit code, checkpoint, completion fingerprint and dispatch/completion times; every output row's purpose, length, hash and capture facts; and the SHA-256 of execution #1's two sealed files. It is not a byte
  identity of every stored row, of other artifact files or of another project's rows. A page reload is not a host restart; the file-backed Application and API tests prove reopening separately.
- **Compiling controls** (`dev-controls.log`, `dev-controls-2.log`; serial, hash-guarded, every restored file byte-identical, targeted regressions only, not a mutation campaign): C1 history stops admitting the
  lookahead row **killed** (4 cases); C2 SourceChanged no longer requires a differing completion fingerprint **killed**; C3 exit-code guard drops the `-int.MaxValue` comparison **killed** (the extremum case);
  C4 no workspace-in-project check **killed**; C5 lowercase identifier admitted **killed** (4 cases); C6b no offset-past-length refusal **killed**; C7 membership ignores the project **killed** (3);
  C8b no incoherent-execution refusal before the stream read **killed** (2); C9 no lookahead row read **killed** (7); **C10 survived** (the descriptor's in-memory execution-identity check). That is the demonstrated scope: the same statement's database predicate also restricts rows to the exact execution, so the mutation changed no tested
  outcome; no broader equivalence proof is claimed (a differently cased stored `VerificationExecutionId` fails that predicate and reads as `NotRecorded`; see Correction 1).
  The frontend implementer's own five controls are in `fe-dev-controls.log` (three killed; two survived as guard removals that the owned-state commit and the derived selection also enforce, at that demonstrated scope only and without a broader equivalence proof).
- **Planner inputs.** The four planner inputs equal their `selection.txt` SHA-256 values at the start and at the return; the 82 non-document paths of the delivery are listed with their SHA-256 in
  `final-source-manifest-before-docs.sha256`.

Retained, not rerun and not run: the whole Application, Api, Infrastructure and Domain suites, the whole Vitest suite, the other journeys and the standalone harness tests (no schema, producer, ownership,
shared transport, authentication or lifetime code changed; the shared Agent/sequence handlers and `RecordedVerificationProjection` are byte-unchanged and covered by their named regressions). No earlier
slice's evidence is fresh evidence for this one.

### Preserved development attempts and failures

All failed output is kept (`logs/executor/project-ver-inspect-01/`); none was rerun blindly.

- A shell heredoc of the first Application query files failed to parse on an apostrophe; nothing was written and the files were then created with the Write tool.
- First Application run of the new tests (`app-dev-1.txt`): 259 of 260 passed; the failure was my own fault-injection test, which looked for `FROM "git_checkpoints"` while the join statement names it as
  `JOIN`; the predicate now matches the quoted table name (no product change).
- First Architecture run (`arch-dev-1.txt`): four of my new boundary facts failed on my own counts and an over-broad forbidden token (`ProjectVerification` appears in the shared projection's method
  `ProjectVerificationOwnership`, and `LocalExecutionSequenceDispatchRefused` is a legitimate status name); the facts were narrowed (`arch-dev-2.txt`: 219 passed). A sed attempt to change two counts
  silently did not apply and the Edit tool did.
- First controls script (`dev-controls-attempt-1-aborted-stderr-under-stop.log`, `dev-controls-attempt-1-raw/`, `dev-controls-attempt-1-task-output.txt`): a native-command stderr line terminated the script
  under `$ErrorActionPreference = 'Stop'` during C1; the `finally` restore ran and all seven controlled files were verified byte-identical before the rerun. In the second script C6 and C8 did not compile
  (`if (false)` made the guarded block unreachable code; output retained under `dev-controls-raw/`), so C6b and C8b were run with compiling always-false/never-true predicates.
- API test development: two compile errors of mine (a variable named `out`, an internal record in a public base type) before the first run; the first run passed 93 of 93.
- Frontend (implementer): the first matrix failed `npm run build` on an unused test parameter and two lint warnings, five failures were defects of the implementer's own tests (details in `frontend-matrix.log`,
  `*-failed-original.txt`); the superseded first matrix is retained.
- Journey: the first run passed. The React duplicate-key console errors (one repeated key, 149 occurrences) appear in this journey as in earlier journeys; whether the new region contributes is not established
  (its list keys are unique execution identities) and it was not investigated; one SignalR negotiation was stopped while the page was being closed.

### Challenges, limits and risks for review

- **No material challenge to ADR-0069.** Everything it requires fit the additive read-only contract. Two interpretations were put to the planner and accepted in Review 1: (1) a stored identifier is admitted only in the provider's
  canonical uppercase form (the form the database compares); a differently cased stored `VerificationExecutionId` fails the exact database predicate (so the stream reads as `NotRecorded`), and a matching artifact row whose own `Id` is
  lowercase is `Unavailable` (the broader first wording is corrected here); (2) a blank supplied query value (`fromOffset=`) is rejected and only an absent key defaults; the older Agent binders are unchanged.
- **Not a replay or authentication.** Pages, metadata and windows are separate recorded reads; matching rewrites of the recorded rows stay outside the assurance (as the ADR states). A recorded Passed or
  exit code is bookkeeping, never approval, source currency or a physical-exit proof, and no run is attributed.
- **SourceChanged tuple.** SourceChanged requires a completion fingerprint that differs from the claim; a SourceChanged row with an equal fingerprint is Unavailable. This already follows ADR-0069's supported tuple (the first return's
  "beyond the ADR text" was wrong); the sibling local-sequence proof states the same rule.
- **Window timeout display.** A window read that times out shows as "could not be loaded", the same as any other read error (the shared window state has no timeout state, and the shared hook was not changed).
- **Journey scope.** It proves the Recorded path through the browser with 21 real manual verifications of one project; damaged storage, every result tuple, Unavailable pages and the lifecycle races are proved below
  the browser boundary. The "newer run/checkpoint" is a manual run and checkpoint #2 of the same unchanged source (identical fingerprint), so what separates it from #1 is its identity and number, not different source content.
- **Not in scope and not done:** run attribution, recovery, retry, release, approval, commit and publication, a real-provider or real-run read, any frozen-experiment renewal, polling, prefetch, export or browser persistence.

### Correction 1 (Codex Review 1 NO-GO: PVI-R1 P2 and PVI-R2 P3)

**Delivery phase: correction 1 returned by the same Claude executor chat, complete diff (including the reviewed handoff), unstaged, uncommitted and unpushed. No GO exists.**
The first return above stays as written except where this section or an inline correction says otherwise; the fresh evidence below supersedes the Application, Api and Architecture runs it repeats, at the scopes stated.
Baseline at the start of the correction: `main`, `HEAD` and local `origin/main` equal `7949f37668641e9f5cd3468d12947a32f9fc6ff8` (the live ref as observed fresh by the planner; the executor did not re-read the remote), divergence 0/0, empty
index, exactly 91 pending paths (11 modified, 80 untracked) equal to `logs/planner-review/project-ver-inspect-01/review-01/correction-paths-status.txt` (compared ignoring that file's CR line endings), the reviewed `planner-handoff.md`
at `5dbf6324…ee7ac0` (preserved, not edited), ADR-0069, the decision index and the roadmap at their selection hashes, and all 82 entries of `final-source-manifest-before-docs.sha256` matching, before any edit. The reviewer's `review.json`
and complete handoff were read. The two reviewer diagnostic sources are archived (hashes in `review-01/reviewer-diagnostics.sha256`) and are not in the test tree.

- **PVI-R1 (P2): an unknown project still disclosed selected evidence. Corrected.** `ProjectVerificationInspectionReader` proved only the execution's `ProjectId`, so executions, workspace, checkpoint and sealed output retained after the project row
  was deleted (foreign keys off, damage setup only) returned Recorded metadata and served the sealed file, while history answered the fixed 404. The actual project is now part of both database proofs: the membership predicate joins the
  registered project, and the single bounded ownership/result statement (`ProjectExecution`) joins it first, so an execution with no project row selects nothing, is `project_verification_inspection.not_found` before any stream metadata or file access, and
  never reaches the descriptor reads or the artifact store. A present project with a damaged member stays `Unavailable`, an absent or foreign execution stays the fixed 404, the exact historical workspace and checkpoint ownership proof, the one
  bounded relational statement, zero writes and the absence of any transaction, retry or shared-reader change are preserved. No store member, shared projection or handler of Agent/sequence inspection changed.
- **Permanent regressions** (adapted from the reviewer's archived diagnostics, which are not delivered): `ProjectVerificationInspectionMissingProjectTests` (Application, 7 cases over disposable migrated SQLite; real sealed file and a recording store) and
  `ProjectVerificationInspectionMissingProjectEndpointTests` (API, 6 cases; the real artifact store over a class-owned root with a counting wrapper). For history, detail and window with the project row deleted but every child and the real sealed output
  retained they assert the fixed non-echoing 404 (code, fixed description, no project or execution identity, no output text, one shared body across the three routes), zero stream metadata reads (the statement log never selects
  `verification_output_artifacts`, a path or a hash), zero artifact-store calls, SELECT-only statements and an unchanged durable fingerprint and file inventory; controls: the same scene with the project present is Recorded and serves the sealed bytes with exactly
  one store call, a foreign execution of a present project is the fixed 404 without a store call, a damaged member of a present project is `Unavailable` before any stream row or file read, and deleting only the project row after a successful read stops the next
  window and metadata read. First run: Application 7/7, API 6/6 (`correction-1/app-pvir1-1.txt`, `api-pvir1-1.txt`).
- **Red evidence** (one hash-guarded compiling control, `correction-1/control-*.txt`; original files restored byte-identical, SHA-256 verified): with the project join removed from both the membership predicate and the ownership statement the same 7 Application cases
  run **3 failed, 4 passed** (the absent-project detail and window cases and the delete-after-read case fail; the history case and the controls pass), reproducing the reviewer's diagnostics at the Application level. This is one targeted control, not a mutation campaign.
- **PVI-R2 (P3): multiple top-level types in four test files. Corrected.** `ExecutionShape` and `SeededExecution` now have their own files (from the seed), `DamageScene` its own (from the base damage class), `StoreCall`,
  `ProjectVerificationRecordingStore` and `ProjectVerificationThrowingStore` their own (the multi-type `ProjectVerificationStores.cs` is removed) and `ApiSeededExecution` its own (from the API test base). Names, accessibility, behavior, isolation and every
  assertion are unchanged; no shared fixture or abstraction was added. A scan of every new C# file for more than one top-level type now finds none.
- **Record precision (accepted decisions, Review 1).** Uppercase canonical identifier admission is accepted and is stated precisely above (a differently cased stored `VerificationExecutionId` fails the exact database predicate and reads as `NotRecorded`; a matching
  artifact row with a lowercase artifact `Id` is `Unavailable`). Blank supplied query values are rejected and only absent keys default. SourceChanged requiring a differing completion fingerprint already follows ADR-0069 (corrected above). The shared generic
  window read-failure display for a timeout is accepted and the real ten-second abort is retained. The journey's newer checkpoint with the same fingerprint is accepted as proof of later identity and number, not of changed source content. The new project-verification section of the cockpit specification no longer says
  "never a clean exit unless Passed": even Passed is recorded bookkeeping, not proof of a physical exit. The final strict e2e inventory is **54 files: 52 e2e files plus the two configuration files**. C10 and the frontend guard-removal survivors are preserved at their
  demonstrated scopes only (the three "equivalent" wordings above were softened accordingly).

**Fresh evidence on the corrected stable inputs** (`correction-1/final-matrix.log`, raw outputs in `correction-1/final-raw/`; non-incremental serial builds, build server shut down first; the Api build regenerated the client again):

- Application, the original filter `FullyQualifiedName~ProjectVerificationHistory|FullyQualifiedName~ProjectVerificationInspection|FullyQualifiedName~GetAgentAttemptVerification|FullyQualifiedName~LocalExecutionVerificationOutputWindow`: **463 passed** (456 before; 7 new), 102 s.
- Api, the original filter `FullyQualifiedName~ProjectVerificationHistory|FullyQualifiedName~ProjectVerificationInspection|FullyQualifiedName~AgentAttemptVerification|FullyQualifiedName~LocalExecutionInspectionEndpoint|FullyQualifiedName~LaunchSessionAuthentication`: **234 passed** (228 before; 6 new), 73 s.
- Architecture, whole project: **219 passed**; the reader fact now also requires the registered project in both reads (six untracked table reads, three joins in the ownership statement, two project reads).
- `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 74 new or modified C# files>`: clean (exit 0, 27 s); `git diff --check` exit 0 (the generated client's CRLF notice only); the NSwag client SHA-256 is unchanged (`cd5d0b82…b215ef`, 569 insertions, 0 deletions).
- The non-document manifest was recomputed (`correction-1/final-source-manifest-before-docs.sha256`, now 90 paths): against the first return exactly sixteen entries differ, all intended (changed: the reader, the projection, the architecture test, the seed, the base damage class and
  the API test base; added: the two missing-project test classes and the seven split type files; removed: `ProjectVerificationStores.cs`); no frontend, journey or generated-client entry changed.

**Retained, not rerun and not run:** the frontend evidence (77 new tests, 45 + 3 existing, lint, build and the strict e2e check over 54 files), client-generation reproducibility (two builds in the first return, identical bytes again now), the one headed journey (no frontend,
wire or journey source changed; the correction changes only the backend missing-project proof, test files and a documentation sentence, and the journey's project is present), the `FullyQualifiedName~Features.Projects` route run (288) and the nine first-return controls.
The whole Application, API, Infrastructure, Domain and Vitest suites remain not run for this slice; no such result is claimed as retained. No browser journey, mutation campaign or whole-suite run was needed or performed in this correction.

**Preserved, unchanged by this correction:** every first-return failure and limit above (the first controls script abort and non-compiling C6/C8, the Application first-run fault-test predicate, the Architecture first-run count failures, the frontend development failures and the duplicate-key
uncertainty). The reviewer's first restricted Application run aborted after a 90-second testhost connection failure and produced no test results; the permitted run is separate evidence and establishes no cause. The reviewer's tool limits (the first manifest parser misreading `*`, two guessed
sealed-read type paths, the failed patch deletion of an archived source, 637 retained frontend temporary files) are recorded in the reviewed handoff and attributed to no product behavior.

## Previous slice (independently verified published as 7949f37; historical record): PLAN-INSPECT-01 inspect the recorded steps of one planning sequence (ADR-0068)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git and the slice's independent publication-verification record, when available. This record alone grants no
publication authority.

**Delivery phase: first return to Codex for review, by the new Claude executor chat (superseded by [Correction 1](#correction-1-codex-review-1-no-go-pli-r1-and-pli-r2-both-p2)); the complete diff (including the four planner inputs) is
unstaged, uncommitted and unpushed. No GO exists.** Review and publication are separate later events; nothing here claims either.
Baseline: `main`, `HEAD`, local `origin/main` and the live ref equalled `8b7085649cb8c67085f3583705789e8d8af532c4` (LOCAL-RELEASE-INSPECT-01,
independently verified published and neither repeated nor reopened) as stated by the dispatch; divergence 0/0 and an empty index were
re-verified by the executor once before editing and again at the return (the live remote was not re-read by the executor). The four planner
inputs (`decisions/README.md`, `mvp-delivery-plan.md`, `planner-handoff.md`, ADR-0068) equal their `logs/planner-review/plan-inspect-01/selection.txt`
SHA-256 values at the start and at the return. ADR-0050 stays Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every
standing restriction in the handoff stays. No frozen profile, native/provider/credential experiment, real run, recovery, cleanup or
commit/push was touched or performed; deterministic doubles remained the validation boundary.

### What exists

- **Application.** Two queries (`GetPlanningSequenceInspectionQuery`, `GetPlanningStepInspectionQuery`) with closed results (`Recorded` /
  `NotRecorded` / `Unavailable`; `Recorded` / `NoLinkedAttempt` / `Unavailable`) and one shared structural reader,
  `Policies/PlanningSequenceInspection`. It reads at most six step rows (a sixth is detected), through bounded untracked scalar projections in
  which every identifier and time is the raw stored bytes within their bound, parsed by one strict parser (36-character D-format nonempty UUID,
  the provider's UTC time text with a zero offset), every enum is classified by the database, every number is guarded to an exact stored INTEGER
  in its range (revisions 1 to 2^53 - 1) and every text is byte-bounded; a corrupt value is therefore an explicit refusal and never a conversion
  fault. It proves the ManualAgent run, the sequence's own run and project, the pinned workspace's project, the pinned checkpoint's workspace and
  fingerprint, ADR-0055's fixed progression (roles, providers and contracts by ordinal, gapless ordinals, unique identities, fixed reasons, the
  claimed/unlinked link shape, every earlier step Concluded, the predecessor chain, every input Proposal a Proposal of this run attributed to the
  predecessor attempt for a reviewer and equal to its review's for a Resolver) and the stop/end/reason/time shape of the sequence. It does not
  require an ended sequence's last attempt to be terminal. The revision is read with the header and compared after the structural reads (and
  again after the selected association proof); a change withholds the answer with no retry. The selected step re-proves the structure and the
  exact membership (decided by a database join of stored identities, so damage elsewhere cannot make a foreign step a member), then, for a
  linked step only, proves the exact attempt of this run (reserved identity, Agent kind, the fixed role, provider and contract, a coherent
  assignment identity by the unchanged `ScalarAgentAttemptIdentity`, and the pinned workspace, checkpoint and fingerprint recorded at claim) and
  returns only the existing immutable header. The Reserved, Revoked and Unresolved shapes are `NoLinkedAttempt` and nothing is discovered by a
  reserved identity. No live status, arming, classifier, lineage, fence, lease, message content, Git, filesystem, process, provider or account
  reader is consulted, nothing is written, no exception is swallowed, no table, migration, event, worker or dependency was added.
- **API.** Two protected, bodyless, queryless MVC GETs, `/api/runs/{runId}/planning-sequence/inspection` and `.../inspection/steps/{stepId}`, one
  class and one action each, dispatching through `IApplicationMediator` behind one new resource filter that refuses any query key or text and
  any body (declared length, any transfer encoding, a readable undeclared byte) with the fixed non-echoing 400 codes
  `planning_sequence_inspection.query_not_allowed` / `.body_not_allowed` before binding; an unknown run and a missing or foreign step are the
  one fixed 404 `planning_sequence_inspection.not_found`. API-owned closed graphs (revisions as canonical Int64 strings, times as UTC
  round-trip strings). NSwag regenerates the client reproducibly; the diff of `api-client.ts` is purely additive (455 insertions, 0 deletions).
- **Frontend.** `planningSequenceInspection.ts` (whole-answer decoders: exact keys, closed vocabularies, bound run/sequence/step identity,
  the fixed role/provider/contract table, chronology compared as canonical texts at tick precision, the host's structural rules, and for the
  selected answer the very indexed step at or after its indexed state with an immutable-fact comparison and the attempt header equal to the
  step's link and fixed identity), parsed-body wire clients, `usePlanningSequenceInspection` (run, opening, index visit, step visit lifetimes
  over the unchanged owned-lifetime primitives; reload ends the selection and its content in the render that starts the read), fixed copy and
  the collapsed **Inspect recorded planning** region (`PlanningSequenceInspectionPanel` and `...StepDetail`) mounted once for the current run
  (`UsageEvidenceRail`) and once for the selected historical run (`ProjectRunHistoryDetail`). A selected linked step mounts the existing
  `AgentAttemptDetail` with the immutable expectation (metadata read; inputs, outputs, verification and sealed artifacts stay their own explicit
  reads) and the explicit existing `CollaborationEvidenceDrilldown` of the step's input Proposal. No arming, stop, implementation, recovery,
  approval or publication control is mounted; no shared Agent rendering or lifetime code changed.
- **Journey.** `e2e/planning-sequence-inspection.journey.ts` (new, one authenticated owned-fixture journey): one rendered arming records the real
  five-step sequence; after the setup settled (every host observation closed) it baselines the business POSTs, the provider invocations and a
  selected durable fingerprint, then inspects the finished sequence from the current rail and from Run history (awaited actual index and step
  responses, whole bodies, rendered identity, the exact inspection request lists), reaches one recorded input, one recorded output, one sealed
  artifact window and the input Proposal evidence explicitly, performs an explicit reload (one new awaited response, the same body, selection and
  content cleared), then a page reload marked before it (cold closed state, no inspection or attempt/message read) and a final read; the segment
  adds no business POST, no provider invocation and no change to the fingerprint.

### Evidence (logs under `logs/executor/plan-inspect-01/`, git-ignored; commands run at the repository root unless a frontend script)

One concise retained log, `final-matrix.log` (exact commands, filters, outcomes, elapsed seconds; raw outputs in `final-raw/`), on the final stable
sources; the changed-path manifest of the 62 non-document paths is `final-source-manifest-before-docs.sha256` (recomputed after the document edit, see
the end of this section). Fresh unless stated:

- **Builds.** Api normal build and a second `--no-incremental` Api build (which regenerated the NSwag client) produced byte-identical
  `src/api/generated/api-client.ts` (SHA-256 `739015aa8986a6ac8c3bdc047c057b33bb74b1d8396d963de40f0487afe4d6f6`; the diff against HEAD is 455 insertions, 0
  deletions); Application, Api integration, Architecture and journey host projects built with 0 warnings, 0 errors. Nothing was edited in the generated file.
- **Application**, `--filter "FullyQualifiedName~PlanningSequenceInspection|FullyQualifiedName~Features.Runs.PlanningSequences"`: **275 passed**, 97 s (new 144
  in namespace `...Runs.PlanningSequenceInspection`: 19 index, 15 selected-step, 110 damage and race cases; the other 131 are the unchanged ADR-0055 regression).
  Real migrated file-backed SQLite; zero to five steps, every fixed role, stopped, stopping, interrupted, attention and unresolved shapes, unlinked
  uncertainty, linked Running, failed, refused, interrupted and message-less attempts, later legitimate facts (a newer checkpoint and run) not invalidating a
  coherent record, 85 structural and 22 association damage cases (the sixth row, unsupported numeric storage, malformed identifiers and times, foreign
  and wrongly typed references, misattributed Proposals, corrupt linked-attempt time columns), three deterministic independent-writer revision races
  (structural read, no-link step and association proof) and the zero-write, SELECT-only, forbidden-column and zero-external-observation proofs
  (`StatementLog`, change fingerprint, capture and seal counters).
- **Api**, `--filter "FullyQualifiedName~PlanningSequenceInspection|FullyQualifiedName~PlanningSequenceEndpointTests|FullyQualifiedName~PlanningSequenceInt64WireTests|FullyQualifiedName~LaunchSessionAuthenticationTests"`:
  **51 passed**, 17 s (new 22: both routes' authentication, MVC authorization inventory, methods, three body shapes, query shapes, fixed 404, closed wire graphs
  of all five real steps, NotRecorded, Unavailable over damaged storage, revision limits, no write or artifact read or provider call, and the actual OpenAPI graph).
  The planner-named existing regression is **one edited assertion**: `PlanningSequenceEndpointTests.Exactly_three_planning_sequence_routes_exist...` counted every
  route containing `planning-sequence` and failed 3 vs 5 once the two inspection routes existed; it now excludes `/inspection` (ADR-0055 still has exactly its three
  operations) and the inspection routes have their own exact inventory. Nothing was weakened.
- **Architecture**, whole project: **210 passed** (204 before; six new boundary facts for ADR-0068).
- **Frontend**, named files (the five new test files and the existing `ProjectRunHistoryPanel*`, `ProjectRunHistoryAgentAttempts`, `PlanningSequencePanel` and
  `PlanningSequenceStaleAction` tests): **166 passed in 10 files** (62 new in the five new test files; this record first said 60, a miscount corrected in Correction 1), 5 s; `npm run lint` exit 0;
  `npm run build` (includes the product `tsc -b`) exit 0; the strict e2e `tsc` check, run separately, exit 0 over 53 files (the 51 files of `e2e/*.ts`,
  `e2e/journey/*.ts` and `e2e/harness/*.ts`, which include the new journey, plus `playwright.config.ts` and `playwright.journey.config.ts`) with
  `npx tsc --ignoreConfig --noEmit --strict --target es2023 --module esnext --moduleResolution bundler --skipLibCheck --noUnusedLocals --noUnusedParameters
  --verbatimModuleSyntax --allowImportingTsExtensions --erasableSyntaxOnly --types node` plus those globs.
- **Format and hygiene.** `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 43 new or modified C# files>`: clean (exit 0, 27 s); `git diff --check` reports
  only the existing CRLF notice of the generated client; the new files have no CR, tab or trailing space; a bounded lexical scan for secrets found only comments and
  clearly fictitious test values; no dependency, package or configuration file changed.
- **Documents.** `check-doc-links.js` (the earlier slice's script, copied unchanged; negative-controlled with a deliberately broken link and anchor, which it reported) over this
  record: 35 relative links, 0 problems; `git diff --check` clean for the documents; the SHA-256 manifest of the 62 non-document paths is identical before and after this document edit
  (`final-source-manifest-before-docs.sha256`, `final-source-manifest-after-docs.sha256`), so no binary, test or browser evidence is invalidated by it.
- **Journey**, one automatic headed run on the final binaries (`playwright test -c playwright.journey.config.ts e2e/planning-sequence-inspection.journey.ts --headed
  --trace off --workers 1`; no Inspector, `--debug`, `--ui`, `PWDEBUG` or manual Resume; trace, video and screenshots off; the owner was told before the run
  in the executor's message): **1 passed**, 17.6 s (24 s wall). It proves the five-step scene (one arming POST; five Concluded steps in the fixed roles with exact
  predecessor links), the current-run and run-history surfaces, each read's awaited actual response (whole body, exact members, rendered identity, exact request
  lists), one recorded input, one recorded output, one sealed artifact window and the input Proposal evidence as explicit reads, the explicit reload (one new awaited
  index response, equal body, selection and content cleared), a page reload marked before it (cold closed state: no inspection or attempt or message read) and a
  final read. After the setup settled it baselined all business POSTs (the one arming), the five provider invocations and a durable fingerprint; the inspection segment
  added no business POST, no provider invocation and no fingerprint change. **The fingerprint covers exactly:** the run's event, collaboration-message, attempt and
  attempt-input row counts; each attempt's status and outcome; the sequence row's state, reason code and revision; each step row's state, revision and attempt link; and
  each host observation's revision and closure marker. It is not a byte identity of every stored row, of the artifact files or of another run's rows.
  A page reload is not a host restart; the file-backed Application and API tests prove database and host reopening separately.
- **Five bounded compiling controls** (`dev-controls.log`, serial, hash-guarded, baseline and restored hashes identical, targeted regressions only): C1 remove the
  association-proof revision recheck, **killed** by `A_revision_change_during_the_selected_association_proof...`; C2 stop reading the sixth row, **killed** by
  `step_sixth_row_past_the_bound`; C3 drop the Resolver same-Proposal rule, **killed** by `step_resolver_input_differs`; C4 drop the reviewer attribution check,
  **killed** by `proposal_attributed_to_another_attempt` and `proposal_unattributed`; C5 drop the linked attempt time admission, **killed** by its four new cases.
  This is not a mutation campaign; surviving scope was not searched.
- **Planner inputs.** The four planner inputs equal their `selection.txt` SHA-256 values at the start and at the return. HEAD, local `origin/main` and the empty index were
  re-verified; the live remote was not re-read by the executor.

Retained, not rerun and not run: Domain, Infrastructure (persistence and process) tests, the whole Application and Api suites, the whole Vitest suite, the other
journeys and the standalone harness tests (no schema, producer, ownership, shared transport or shared Agent rendering or lifetime code changed; two existing test
inventories changed by one line each for the new button, one existing route assertion as stated). No earlier slice's evidence is fresh evidence for this one.

### Preserved development attempts and failures

All failed output is kept (`logs/executor/plan-inspect-01/`): none was rerun blindly.

- A shell heredoc of the first API source files failed to parse (an apostrophe in a comment); the files were then written with the Write tool. Nothing ran.
- Application damage first run, 104 of 106 passed: two cases were invalid setup of mine (a unique index refused the duplicated reserved identity; `UPDATE runs SET ProjectId`
  hit another run of the shared database). The cases now drop the unique index and scope to the sequence's run. Output: `application-damage-dev-attempt-1-failed-original.txt`.
- Api first build failed on analyzer CA2100 for the test's raw SQL text (a pragma with a stated reason was added); that build output was not retained (overwritten by the
  next run). The next focused run failed one test of mine (`'7'` stored in an INTEGER column is converted to the integer 7 by SQLite, so it is a legitimate revision;
  the case now stores non-numeric text): `api-dev-attempt-1-failed-original.txt`.
- Journey development attempts 1 to 3 failed on defects of my script, none in the product: the sealed window route is `.../evidence/artifact-window/...` (attempt 1), the evidence
  dt/dd layout is not `Attempt number: 1` (attempt 2) and the live cockpit's own kind-named eligibility reads (`agent-attempts/codex-plan`...) are not attempt evidence
  (attempt 3): `journey-dev-attempt-{1,2,3}-failed-original.txt`. A fourth run passed on binaries built before the hardening below and is superseded by the final run.
- The first final-matrix run (`final-matrix-superseded-prehardening.log`, `final-raw-superseded-prehardening/`) failed the existing route-inventory assertion above, and its
  strict-`tsc` step failed only because my script passed unexpanded globs (exit 2, `TS6053`); an earlier strict-`tsc` "exit 0" of mine had been the exit status of a `tail` pipe and is not
  claimed. The strict check was then run with the true exit status (above).
- **Hardening found by my own review during that run (product, corrected before the final matrix).** The unchanged shared `ScalarAgentAttemptIdentity` projection converts the attempt's
  `ClaimedAtUtc`, `CompletedAtUtc` and `AgentDispatchedAtUtc`, so a corrupt time on a linked attempt would have faulted the selected read instead of answering Unavailable. The association
  proof now reads those three columns (with the pin) as bounded raw bytes and admits them first; four new cases and control C5 prove it. The shared projection itself is unchanged.
- The React duplicate-key console errors (one repeated key, hundreds of occurrences) appear in this journey as in earlier journeys that mount none of this slice's UI
  (the LOCAL-RELEASE-INSPECT-01 final journey log); they establish an earlier occurrence, not the cause, and were not investigated further; whether this slice contributes to them is not established.

### Challenges, limits and risks for review

- **No material challenge to ADR-0068.** Everything it requires fit the additive read-only contract: no schema, live control, shared ownership or release proof, provider capability
  or semantic replay change was needed, and none was made.
- **Not a replay or authentication.** The index proves structure and identity of recorded facts; the selected read proves the exact Agent association; neither proves acceptance,
  liveness, a dispatch, or that the source is current, and matching rewrites of the recorded rows stay outside the assurance (as ADR-0068 states). A recorded `Concluded` is
  bookkeeping; the Proposal and review content are read only through the existing explicit readers.
- **Frontend cannot prove reviewer attribution.** It needs message rows, so only the host proves that a reviewer's input Proposal is attributed to its predecessor attempt; the decoder checks every
  other rule the wire exposes. The selected-answer comparison additionally requires the step's revision and update time not to run backwards (implied by the Domain, not stated in the ADR).
- **404 for an unparseable sequence identity.** When the sequence row's own identity is not a valid UUID the selected read answers the fixed 404 (no identity can be named), while the
  index answers Unavailable; a step whose own identity is damaged cannot be addressed at all.
- **Existing shared code reused unchanged.** `ScalarAgentAttemptIdentity` and `AgentAttemptDetail`/`CollaborationEvidenceDrilldown` (and so their limits, redaction and window
  behaviour) are unchanged; their own corrupt-storage behavior for other columns is theirs, not this slice's.
- **Journey scope.** It proves the Recorded five-step path through both surfaces and the reload behaviours against a real sequence. "Historical" is run #1 of the only run of its project (as in
  earlier journeys); an older run beside a newer one is proved in the Application tests (a newer checkpoint and run beside a recorded sequence), not in the browser. Reserved, Revoked,
  Unresolved, Stopping, Interrupted, Unavailable and NotRecorded renderings are proved by the component, surfaces, hook and decoder tests and by the Application and Api tests.
- **Other documents.** Only this record changed; the cockpit specification, evidence-inspection architecture document, engineering context, security plan and threat model carry no
  ADR-0068 text (the dispatch named only this record). That is a possible follow-up for the planner, not done here.
- **Not in scope and not done:** semantic planning replay, a final-delivery summary, recovery, queue controls, retry, approval, commit and publication, a real-provider or real-run read,
  any frozen-experiment renewal, polling, prefetch, export or browser persistence.

### Correction 1 (Codex review 1 NO-GO: PLI-R1 and PLI-R2, both P2)

**Delivery phase: correction 1 returned by the same Claude executor chat, complete diff (including the reviewed handoff), unstaged, uncommitted and unpushed. No GO exists.**
The first return above stays as written except where this section corrects it; the fresh evidence below supersedes the Application, Api and Architecture runs it repeats, at the
scopes stated. Baseline at the start of the correction: `main`, `HEAD`, local `origin/main` (and the live ref, as verified by the planner) equal
`8b7085649cb8c67085f3583705789e8d8af532c4`, divergence 0/0, empty index, 67 pending paths; all 67 entries of `logs/planner-review/plan-inspect-01/review-01/correction-preflight.sha256`
matched before editing, including the reviewed `planner-handoff.md` (`290d141b…`), which is preserved. ADR-0068, the decision index and the roadmap still equal the selection hashes.

- **PLI-R1 (P2): the linked attempt number was converted without exact storage admission. Corrected.** `PlanningSequenceInspectionAgentProof` read the shared
  `ProjectScalarIdentity`, which materializes `AttemptNumber` unguarded: the reviewer reproduced REAL 1.75 as Recorded/1, TEXT `'7tail'` as Recorded/7, BLOB `X'31'` as Recorded/1 and
  INTEGER 2147483648 and 4294967297 as an `OverflowException`. The operation now owns the admission: `WhereAttemptNumberSupported` (in the operation's projection) restricts the same
  statement that materializes the identity to a stored INTEGER from 1 to `int.MaxValue` (range check, which excludes TEXT and BLOB; remainder with the column's own `int` type as modulus,
  which excludes a REAL; `int.MaxValue`, the one value that is not its own remainder, compared directly). An unsupported value selects no row and is read as Unavailable with null step and
  agent, while the structural index stays Recorded. `ScalarAgentAttemptIdentity` is unchanged; no catch, schema, provider-specific code or shared-reader change.
- **PLI-R2 (P2): a successful no-link answer was returned without a final revision comparison. Corrected.** The structural reader rechecks the revision before the membership read, so a
  separate writer advancing it during that later `SELECT EXISTS` was missed on the unlinked branch. `GetPlanningStepInspectionQueryHandler` now compares the revision once more after
  membership before returning `NoLinkedAttempt` (otherwise Unavailable with null payloads); the linked association's final check is unchanged. No retry, transaction, write, live status
  or authority change.
- **Permanent regressions.** `PlanningStepInspectionAdmissionTests` (new, 23 cases, adapted from the reviewer's archived diagnostics, whose source is not in the delivery):
  fourteen unsupported stored values (the five demonstrated ones plus 0.5, 1e300, 9223372036854775807, 0, -1, -2147483648, `'seven'`, `''`, `X''`) are Unavailable with null step and agent, the
  index stays Recorded and a sibling step stays Recorded; six valid controls stay Recorded with the stored number (2147483647, 2147483646, 1, 65536, `'7'` and `7.0`, which SQLite's
  INTEGER affinity stores as the integer 7); one fact reads the executed identity statement and asserts the four admission predicates are in it; the membership-seam race uses a deterministic
  independent SQLite writer at the `SELECT EXISTS ... planning_sequence_steps` statement, **asserts the writer fired**, expects Unavailable with null payloads and then, on an explicit reload,
  the coherent `NoLinkedAttempt`; a companion fact proves the membership statement is observed on the unchanged path. The Architecture ordering fact now requires the no-link recheck after the
  membership read and before the association proof, and exactly two revision rechecks in the handler. Protected API evidence (two new facts in
  `PlanningSequenceInspectionEndpointStorageTests`, each over its own recorded five-step sequence): the five demonstrated unsupported numbers answer 200 Unavailable with null step and agent (no
  server error) beside a Recorded index, and `int.MaxValue` answers Recorded; a Reserved, Revoked and Unresolved last step each answer 200 `NoLinkedAttempt` with null payloads beside a Recorded index.
  The reviewer's first membership interceptor never fired (a wrong SQL predicate) and is preserved by the reviewer as invalid setup; the corrected predicate is the one used here.
- **Red evidence (compiling controls, serial, hash-guarded, restored byte-identical; `correction-1/dev-controls.log`).** C6 remove `WhereAttemptNumberSupported`: **killed** (seven of the unsupported
  cases and the statement-shape fact fail; the 0, -1, `''` and similar values were already refused by the shared evaluation and are not claimed to discriminate). C7 return the no-link answer
  without the recheck: **killed** by the membership-seam race. The new regressions were also run first on the corrected source: 23 of 23 and 4 of 4 passed.

**Fresh evidence on the corrected stable sources** (`correction-1/final-matrix.log`, raw outputs in `final-raw/`; non-incremental builds before the tests):

- Application, `--filter "FullyQualifiedName~PlanningSequenceInspection|FullyQualifiedName~Features.Runs.PlanningSequences"`: **298 passed**, 102 s test-runner duration (112 s command wall time; 167 new inspection cases, the unchanged 131 ADR-0055 regression).
- Api, `--filter "FullyQualifiedName~PlanningSequenceInspection|FullyQualifiedName~PlanningSequenceEndpointTests|FullyQualifiedName~PlanningSequenceInt64WireTests|FullyQualifiedName~LaunchSessionAuthenticationTests"`:
  **53 passed**, 27 s test-runner duration (30 s command wall time; 24 new). The Api build is the fourth attempt (see the failures below).
- Architecture, whole project: **210 passed**.
- `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 44 new or modified C# files>`: clean (exit 0, 32 s); `git diff --check` clean apart from the generated client's CRLF notice; the new file has no CR,
  tab or trailing space. The NSwag client bytes are unchanged (`739015aa…`); the SHA-256 manifest of the now 63 non-document paths is in `correction-1/`.

**Retained at their actual scope:** the original complete frontend selection, lint, build and strict e2e `tsc` (no frontend or journey source changed; the executor reports one further test command over the five new files,
which reported **62 passed in 5 files**, not a repeat of the complete frontend selection); client generation (identical bytes above); the headed journey, which ran on the **pre-correction** host (it contains neither the attempt-number admission nor the
no-link recheck; the correction changes only backend admission and rechecking and tests, so no redundant visual run was made and none of the journey's claims depends on these two paths).
The first record's "60 new" frontend tests was a miscount; the reviewer's five-file test run and the executor's reported subset command both give **62** (the 166 total in the first record was accepted at its scope and is not re-claimed here).
Domain, Infrastructure, the whole Application and Api suites, the whole Vitest suite, the other journeys and the harness tests were not run.

**Failures and limits of the correction, preserved:**

- The first correction matrix (`final-matrix-stale-mutant-binaries-superseded.log`, `final-raw-stale-mutant-binaries-superseded/`, `final-app-tests-failed-original.txt`) failed one Application test of 298, the new
  membership-seam race, with the same behavior as control C7. The control script restored the source byte-identically, but the following incremental build did not rebuild (1 s), leaving binary provenance uncertain.
  The executor inferred stale mutant binaries from behavior, without inspecting the binary: the class failed identically three times in isolation until a `--no-incremental` rebuild, after which it passed, and the matrix
  was rerun with non-incremental builds. That supports a provenance concern, not independent proof of the failed binary's contents or cause. Its Api (53) and Architecture (210) results are superseded, not claimed.
- Planner review reconciled **three** failed Api `--no-incremental` builds, followed by success on the **fourth** attempt: `final-raw/api-build.txt` and `api-build-retry.txt` report CS2012 for Domain.dll;
  `api-build-retry-2.txt` reports it for Application.dll; `api-build-retry-3.txt` builds serially with `-m:1` and passes. The three associated failed test commands report that the test assembly was not found,
  not executed test failures. The retry error mentions Microsoft Defender and the second-retry error mentions VBCSCompiler, without establishing cause. A build-server shutdown preceded another failure;
  neither the log's MSBuild/graph hypotheses nor the final pass proves the cause. Original output and retry sections remain unchanged. The Architecture build in the same matrix passed non-incrementally.
- The reviewer's limits stand and are not explained here: the first reviewer build failed with zero reported warnings or errors, the restricted test runner produced no results and was interrupted, and the first
  frontend attempt failed module loading with a temporary-path ENOENT (a workspace TEMP/TMP run passed). The reviewer's corrected diagnostic run was 151 cases, 145 passed and six failed (five numeric cases and the corrected
  membership race), with all 144 permanent inspection cases and the maximum-number control passing; the reviewer's archived source and output are under `logs/planner-review/plan-inspect-01/review-01/`.
- Remaining risks: the admission covers `AttemptNumber`; the other columns of the shared identity projection are guarded by it (strings by byte bounds, enums by database classification) and the three time columns by the
  earlier hardening, but no further column audit of that projection was made. The browser has not exercised the two corrected paths.

## Previous slice (independently verified published as 8b70856; historical record): LOCAL-RELEASE-INSPECT-01 inspect recorded local quarantine release receipts (ADR-0067)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git and the slice's independent publication-verification record, when available. This record alone grants no
publication authority.

**Delivery phase: correction 2 (Codex review 2 NO-GO: LRI-R1 residual open, LRI-V1 resolved) returned by the same Claude executor chat, complete diff
(including the four planner inputs), unstaged, uncommitted and unpushed. No GO exists.** The first return below stays as written except where
[Correction 1](#correction-1-codex-review-1-no-go-lri-r1-p2-and-lri-v1-p3) and [Correction 2](#correction-2-codex-review-2-no-go-lri-r1-residual-nullable-numeric-facts) correct it; their fresh evidence supersedes the Application, API, Architecture and journey runs they repeat, at the scopes stated there.
Planner review edit: `planner-handoff.md` (SHA-256 `a41f0332…`, review 2) is the reviewed version and is preserved; ADR-0067, the decision index and the roadmap still equal the selection hashes.
Baseline: `main`, `HEAD`, local `origin/main` and a freshly read live `refs/heads/main` (`git ls-remote`) were
`7ab7a15eab9ce5682578517f1bb106ca7557db21` (LOCAL-QUARANTINE-RELEASE-01, independently verified published and neither repeated nor reopened),
divergence 0/0 at the start and again at the return; the real index was empty and exactly the three modified planner documents
(`decisions/README.md`, `mvp-delivery-plan.md`, `planner-handoff.md`) and the new ADR-0067 were pending. They are preserved byte-for-byte: their
SHA-256 values equal `logs/planner-review/local-release-inspect-01/selection.txt` at the start and at the return. ADR-0050 stays Proposed,
P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every standing restriction in the handoff stays. No frozen qualification
profile, native/provider/credential experiment, real run, recovery, cleanup or commit/push was touched or performed.

### What exists

- **Application.** `GetLocalExecutionReleaseInspection` (query, handler, closed result `Recorded` / `NotRecorded` / `Unavailable` with a nullable
  receipt). `LocalExecutionReleaseInspectionReader` (policy, beside the ADR-0065 release policies) reads the *unchanged*
  `LocalExecutionReleaseStatement` (one bounded provider-neutral untracked statement; its shared projection gained numeric admission in Correction 1)
  and proves it with the *unchanged* `LocalExecutionReleaseProof`, then classifies from the same rows: `Released` is Recorded; `Available` or `NotEligible` with neither the
  step-keyed nor the witness-keyed receipt association is NotRecorded; everything else, including a present receipt beside a revoked or unlinked
  step (the proof answers NotEligible/StepNotLinked before it looks at a receipt) and old unreleased operations without producer evidence, is
  Unavailable. NotFound is the same proof's absent or foreign run, sequence or step. It does not call the candidate query or handler, mints no
  confirmation, takes no `TimeProvider`, transaction or write, and reads nothing external. The handler keeps ADR-0065's narrow
  `InvalidOperationException or FormatException` translation of a stored value this build cannot materialize, here to Unavailable (never to
  absence); cancellation and database faults keep the normal pipeline. No table, migration, event, worker or dependency; no producer, domain
  eligibility, ownership expression, SQL guard, queue, fence, approval or publication code changed (the candidate and replay readings changed only through the
  shared projection's stricter numeric admission, Correction 1).
- **API.** `GET /api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/ownership-release` (protected, bodyless, queryless) behind the
  existing `LocalExecutionInspectionBodylessGuardFilter` with the fixed non-echoing `local_execution_inspection` 400/404 errors; API-owned closed
  response (`runId`, `stepId`, `status`, `receipt`) and a 23-member receipt (release identity, time and Human event number as a canonical Int64
  string; sequence, state, mode; step kind, ordinal, pass; family and operation; pinned source; the unchanged result status and process outcome with
  a nullable semantic exit code and duration ticks; witness and observation identity, dispatch and observation times, the exact Int32 physical exit
  code and elapsed ticks). No nonce, launch identity, ticket, path, argument, output, confirmation or candidate. NSwag regenerates the client
  reproducibly (hash `71898d8c…` identical across three builds, two of them `--no-incremental`).
- **Frontend.** `localExecutionReleaseInspection.ts` (whole-answer decoder: exact keys, closed vocabularies, canonical identifiers, times and Int64
  texts, family/kind/status/process/nullability rules, an observation no longer than its process, dispatch before observation before release compared as
  fixed-width canonical texts at tick precision, and for Recorded the selected sequence, mode, step kind, ordinal, pass, family, operation and pinned
  source), `LocalExecutionReleaseInspectionWireClient` (parsed body, own `AbortSignal`), `useLocalExecutionReleaseInspection` (step visit,
  opening and read visit lifetimes over the unchanged owned-lifetime primitives; target frozen when the region is opened; real ten-second bound;
  one request in flight that releases only its own guard; reload discards the old answer at once), fixed copy and the collapsed
  `LocalExecutionReleaseInspectionRegion` mounted by `LocalExecutionInspectionStepDetail` for a proved linked Agent or verification step, so the
  current rail and run history share it (`LocalExecutionInspectionPanel` passes the recorded admission mode, the only caller). It mounts no release
  action, candidate read or confirmation handler.
- **Journey.** `e2e/local-quarantine-release.journey.ts` keeps every original step and its exact business-POST inventory, and gains step 13 (inspect the
  recorded decision from the current rail and from Run history: selecting the step reads no receipt, opening reads exactly the receipt, an explicit reload
  reads it again, both surfaces return the identical wire; the segment sends no business POST, release-candidate read or artifact read and leaves the
  selected-field fingerprint of the releases, event types, sequence rows, step states and links, result statuses and exit codes, witness and observation identities
  unchanged; not all-row byte identity, see LRI-V1 in Correction 1) and step 14 (the existing page reload now followed by a fresh
  inspection that returns the same receipt). A browser reload is not a host restart; file-backed tests prove reopening separately.
- **Tests.** Application `LocalExecutionReleaseInspection{Tests,DamageTests}` over real file-backed SQLite (54): all six kinds in both families and both
  admission modes, exactly one read-only SELECT that never selects the nonce or launch identity, NotRecorded for a candidate, an already-free, a not-ended, a running
  and a reserved step, Unavailable for missing producer evidence (observation deleted and a naturally unobserved timeout), the fixed not-found, host
  restart with a cleared connection pool, an older run beside a newer released one and 26 present-receipt damage cases (typed snapshot, step-keyed and
  witness-keyed associations, Human event, chain, authority drift, revoked and unlinked step, a stored value that cannot materialize), with representative valid,
  snapshot-drift and checksum-only ownership asserted independently (workspace, capacity, abandonment and both SQL write seams). API
  `LocalExecutionReleaseInspectionEndpointTests` (13) through the real host and the production release POST. Architecture: one new test of the reuse and
  no-mint rules. Frontend: decoder 102, hook 20, transport 7, region 11, surfaces 16 (rail and history), contract 9.
  Three existing route-inventory tests were updated for the one added route (see below).
- **Documentation.** ADR-0067 (planner-owned, untouched), the inspection architecture contract, `data-and-recovery.md`, `run-cockpit-specification.md`,
  `engineering-context.md`, the security plan, the threat model and this file.

### Evidence (logs under `logs/executor/local-release-inspect-01/`, git-ignored; commands run at the repository root unless a frontend script)

Fresh on the final stable sources (changed-path SHA-256 manifest before and after the whole matrix and the journey identical:
`pre-final-source-manifest.sha256`, `post-final-source-manifest.sha256`, 46 paths; `final/`):

- Serial incremental build of the Application, Api integration, Architecture and journey host projects: 0 warnings, 0 errors; two `--no-incremental` Api builds regenerated the client with the same hash.
- Application `--filter "FullyQualifiedName~LocalExecutionReleaseInspection|FullyQualifiedName~LocalExecutionOwnershipRelease|FullyQualifiedName~LocalExecutionInspection"`:
  **523 passed**, 3 m 36 s (new 54; the existing release suites and the ADR-0061 inspection classes).
- Api `--filter "FullyQualifiedName~LocalExecutionReleaseInspection|FullyQualifiedName~LocalExecutionInspection|FullyQualifiedName~LocalExecutionOwnershipRelease|FullyQualifiedName~LocalExecutionSequenceEndpoint"`:
  **199 passed**, 1 m 12 s (the MVC authorization/route inventories, strict transport, the release and inspection endpoint regressions).
- Architecture, whole suite: **204 passed**.
- Frontend (named files: the six new test files and 15 existing inspector, release, history, rail and contract files, `final/06-frontend-files.txt`):
  **549 passed in 21 files**; `oxlint` exit 0 (existing warnings only, none in a new file); `npm run build` (with product `tsc -b`) passes; the recorded
  strict e2e `tsc` flag set exit 0 over 52 files.
- `dotnet format DevalCopilot.slnx --verify-no-changes --include <the 19 changed C# files>`: clean.
- Journey, headed and automatic (`playwright test -c playwright.journey.config.ts e2e/local-quarantine-release.journey.ts --headed --trace off`, no Inspector,
  `--debug`, `--ui`, `PWDEBUG` or manual Resume; trace, video and screenshots off): **1 passed in 1 m 5 s**, first attempt.
- Two compiling controls (`controls/`, baseline, mutant, restored and re-hashed identical, targets touched and re-run):
  1. remove the receipt operation-identity admission (`raw.operationId !== target.operationId`) from the decoder: compiled; **killed** by decoder `refuses a
     well-formed receipt of another operationId` and `refuses a verification receipt of another recipe operation…`, and hook `refuses an answer for another
     operation even though it is well-formed…` and `refuses an incoherent answer, a request failure…` (4 of 165 failed);
  2. remove an ended-visit guard from the hook's `reload`: **2a** (the `visitOwner.isActive()` guard alone) compiled and **survived**, masked by the independent commit-side
     visit check (`previous.visit === visit`): all 54 tests passed. **2b** (both layers of `reload`) compiled and was **killed** by `lets a reload from an earlier visit start no
     further read for a later one` (1 of 54). Surviving scope: each hook callback is guarded twice (owner liveness and the frame's serial or visit), so a single layer
     cannot be shown necessary by behavior; the retained-callback regressions prove the combined defense.

Retained, not rerun (actual scope unchanged): Domain, Infrastructure (migration and process termination), the whole Application and Api suites, the whole Vitest
suite and the other journeys, all at their earlier revision (no producer, persistence or shared transport changed); the LOCAL-QUARANTINE-RELEASE-01 evidence below
stays at its own scope. No test of the previous slice is fresh evidence for this one.

### Preserved development attempts and failures

- Shell heredoc of the first Application source files failed to parse (an apostrophe inside it); the files were then written with the Write tool. Nothing ran.
- Application first run of the new classes: 53 passed, 1 failed (`A_stored_value_this_build_cannot_materialize_is_unavailable_never_absent`): my first damaged value
  (`IntentPassNumber = 'not-a-number'`) is classified to zero by the guarded projection and coincides with an Implementation's pass 0, so the receipt still proved.
  The case now damages a Guid (`IntentOperationId = 'not-a-guid'`), which materializes and fails. Output kept in `03-application-new-first-run.txt`.
- Api first focused run (193 tests): 2 failures, both wrong assumptions in my new tests and no product defect: the existing guard accepts a bare trailing `?`
  (it carries no query text, only a key or text after the mark is refused), and an unreadable capacity count is the fail-closed `null`, not `1`. Output in `06-api-first-run.txt`.
- Decoder first run: 101 of 102 passed. **Product defect found and corrected before the final matrix:** my first decoder compared the three receipt times as JavaScript
  instants, which have millisecond precision, so a release one tick before its observation was accepted. The chronology is now compared as the fixed-width canonical UTC
  round-trip texts (which order exactly at tick precision). Output in `09-decoder-first-run.txt`.
- Hook and transport first run: 2 failures, both test mistakes (a retained read of an ended step visit is aborted with it, which is correct; the mocked 404 body lacked the
  `status` member a real problem document carries). Region and surfaces first runs failed on a duplicate text query and on unrelated environment reads of the rail; the tests
  were made exact. The React duplicate-key console errors in the journey and rail tests are the existing, unrelated warning recorded for earlier slices.

### Challenges, limits and risks for review

- **Route inventories.** Three existing assertions listed routes exactly and had to change for the one new inspection route: `LocalExecutionSequenceEndpointTests` (12 → 13 routes,
  plus the route), `LocalExecutionInspectionEndpointTests` (four → five inspection routes) and `LocalExecutionOwnershipReleaseEndpointTests` (its `ownership-release` pattern now
  excludes the `/inspection/` route, so it still names exactly the two ADR-0065 operations). No assertion was weakened.
- **SUPERSEDED (inaccurate; corrected in Correction 1).** The first return claimed the host would answer Recorded for a stored receipt of an operation with an ordinary Exited proof. That was wrong: the
  existing terminal-result proof (`ResultConsistent`) already requires TimedOut or Cancelled in both families, so the host answers Unavailable; see [Correction 1](#correction-1-codex-review-1-no-go-lri-r1-p2-and-lri-v1-p3).
- **Frozen target.** The hook judges every answer of an opening against the target captured when the region was opened; a reload therefore reuses it, and a different step visit is a different
  lifetime. The decoder does not compare the receipt's sequence state with the index header: the two are separate reads and an ended sequence cannot change state, but the receipt's state is
  disclosed as "Cycle at release" and is not claimed to be current.
- **Hook guard layering.** See control 2a: each callback has an owner-liveness guard and a frame-level guard, so one is masked by the other. An alternative is a mutation that removes a guard
  and its fallback together, as 2b does; no further control was run.
- **Journey scope.** It proves the Recorded path through both surfaces and a reload against a real cycle. NotRecorded and Unavailable in the rendered region, the checksum-only message and all
  failures are proved by the component, surfaces, hook and transport tests and by the file-backed and API tests, not by the journey. History is reached through run #1 of the first project
  (its only run), so "an older run beside a newer run" is proved in the Application tests, not in the browser.
- **Component API.** `LocalExecutionInspectionStepDetail` gained the required `admissionMode` prop (its only caller passes the accepted header's); this is the one change to an existing component.
- **Not in scope and not done:** recovery, reservation release, cancellation, retry, resume, takeover, approval, a restart-based browser proof, authenticated history, signing, repair of a
  checksum-only mismatch, complete run replay, a real-run read, a frozen-experiment renewal, commit and publication.

### Correction 1 (Codex review 1 NO-GO: LRI-R1 P2 and LRI-V1 P3)

Preflight (once, before editing): `main`, `HEAD`, local `origin/main` and a fresh live `refs/heads/main` were `7ab7a15eab9ce5682578517f1bb106ca7557db21`, divergence 0/0, index empty,
19 modified and 28 untracked files; `planner-handoff.md` had the reviewed SHA-256 `b8a86a05…c1302c9` and ADR-0067, the decision index and the roadmap their selection hashes. The reviewer's
temporary test source (`logs/planner-review/local-release-inspect-01/review-01/LocalExecutionReleaseInspectionReviewerTests.cs`, SHA-256 `bbffc6fe…5752`) is archived there and is not in `tests/`.
The reviewer's initial restricted Application build failed with zero reported warnings and errors and no diagnostic, and the permitted rebuild passed; the cause is unproved and is preserved, not explained.

**LRI-R1 (P2, resolved by this correction; awaiting review): numeric intent snapshot admission.**
- *Defect.* The shared `LocalExecutionReleaseProjection` read the typed intent's numbers as `(int?)column` or `(TimeSpan?)column`, so the provider normalized unsupported storage (a numeric prefix
  followed by text, a fraction, plain text that reads as 0) into a plausible number before the in-memory proof compared it with the facts. The reviewer's six probes (`IntentPassNumber`,
  `IntentOrdinal`, `IntentPhysicalExitCode`, `IntentProcessDuration`, `IntentObservedElapsed`, `IntentVersion` with `junk` appended) read Recorded while the relational ownership expression and the SQL
  fence kept the operation held. My first return's nonnumeric zero-pass failure was this defect, not a wrong expectation, and replacing it with a malformed Guid (which does fail to materialize) hid it.
  Out-of-range exit codes were worse: Int32 overflow escaped the handler's narrow `InvalidOperationException or FormatException` translation as an `OverflowException`.
- *Correction (the only product change).* In `LocalExecutionReleaseProjection`, before provider conversion, in the same one provider-neutral untracked statement and with the complete proof unchanged:
  `IntentVersion` and `IntentConfirmationVersion` are admitted only when equal to their current constants; `IntentOrdinal` and `IntentPassNumber` use the existing exact-integer-in-range guard
  (`>= 0`, below the domain bound, `% Int32Maximum == value`); `IntentPhysicalExitCode` the existing four-way Int32 guard; `IntentRecipeOrdinal` and `IntentSemanticExitCode`, whose null is legal,
  (superseded by Correction 2: they first mapped a malformed stored number to an `int.MinValue` sentinel, which for the exit code was itself a valid Int32; the sentinel is gone and the raw-storage validity flag
  of Correction 2 replaces it); `IntentProcessDuration` and `IntentObservedElapsed` (stored as ticks) use `value >= TimeSpan.Zero && value == (TimeSpan)(object)(long)(object)value`, which the provider translates to
  `col >= 0 AND col = CAST(CAST(col AS INTEGER) AS INTEGER)` (verified in the generated statement): the raw column is compared with its own integer cast, so text, a fraction and out-of-range REAL are
  refused and `long.MaxValue` ticks are admitted. A malformed value yields null (or the sentinel) and the unchanged proof refuses it. Two cheaper translations failed and are kept in the logs: a bare
  `(long)(object)` cast compares the cast with itself (`02-numeric-after-guard-1.txt`: the four tick cases stayed Recorded), and `EF.Property<long>` threw `InvalidCastException`. No domain eligibility,
  ownership expression, SQL guard, migration, hashing function, second statement, generic framework or release authority changed; the candidate and replay readings become stricter for unsupported numeric
  storage, which is the intended consistency correction.
- *Scope note for the planner (a finding of mine, not requested).* The same defect existed at the sibling tick members the one proof also reads, `Observation.ProcessDuration`, `Observation.ObservedElapsed`
  and `Attempt.AgentProcessDuration` (my probe: 6 of 68 cases read Recorded, `04-sibling-probe.txt`). They got the identical guard in the same projection, with permanent cases. If review wants the
  correction limited to the intent members, these three guards and their six cases are separable; the claim that the observation exit codes and every other projected integer already had a sufficient guard was WRONG: their invalid fallback was a legal null (see Correction 2).
- *Permanent regressions* (`LocalExecutionReleaseInspectionNumericIntentTests`, real file-backed SQLite, 68 cases): 12 members (all nine numeric intent members and the three siblings) times five forms
  (numeric prefix plus `junk`, plain text, fraction, above the table range or Int32/Int64, below it) = 60; each proves the inspection is Unavailable with no receipt in one read-only statement, the
  candidate reading is Unavailable with neither candidate nor release, a replay of the earlier confirmation is refused with the fixed unavailable error, the damaged value is unchanged afterwards
  (no repair), no receipt or event is added, and the workspace, capacity and abandonment readings and both SQL write seams are unchanged (and were already held before the reads). Two controls (a
  well-formed receipt stays Recorded, Released and replayable; ADR-0066's checksum-only mismatch stays Unavailable everywhere while ownership stays released) and six valid boundaries
  (`int.MaxValue`, `int.MinValue`, `-int.MaxValue`, and tick pairs including `long.MaxValue` and zero, with the operation, observation, snapshot and digest all carrying them) must stay Recorded and Released.
- *Evidence of the red and green states* (`logs/executor/local-release-inspect-01/correction-1/`): before the guard 22 of the first 53 cases failed (18 read Recorded, 4 threw `OverflowException`;
  `01-numeric-red-first-run.txt`, `01-numeric-red-full.txt`); after the integer guards 4 tick cases remained (`02`); after the tick guard 53 passed (`03`); the sibling probe then failed 6 of 68 (`04`) and
  after their guard all 68 pass (`05`).

**LRI-V1 (P3, resolved; awaiting review): the reload journey oracle.** Test-only change in `e2e/local-quarantine-release.journey.ts`; no product, shared harness or transport change.
- The Set-based reload assertion is replaced: the response is awaited from before the click, its status and body are read, the body is compared with the earlier receipt (it no longer passes
  `fromRail.body` back), the pending text must disappear and the rendered receipt is asserted from that new body, and exactly one matching request is expected (`toEqual([path])`, no deduplication).
- The page-reload marks are taken before `page.reload()`: from that mark until the explicit inspector open (including the cold closed state) the journey proves zero receipt requests and zero
  release-candidate or artifact API reads; the candidate and artifact check counts only `/api/` paths, because the dev server's source-module requests (for example `artifactPurposes.ts`) are page GETs
  that would otherwise match. The existing POST inventory and every original invariant are untouched.
- The durable fingerprint is described by what it observes (selected release columns and event types, sequence rows as selected, step states and links, result statuses and exit codes, witness and
  observation identities), not as all-row byte identity; this record and the journey comment say so.

**Challenge dispositions.** Exited: resolved by the planner and accepted; the existing terminal-result proof already requires TimedOut or Cancelled in both families, so no extra filter was added and my
first return's host/frontend mismatch statement was wrong (superseded above); the reviewer's matching-Agent Exited diagnostic reads Unavailable (its scope is the Agent family). Controls: 2a survived behind
the independent frame guard and 2b removed both layers and killed the named regression; both outcomes are accepted and preserved, and no further mutation run was made. The older candidate decoder's
millisecond chronology stays an out-of-scope retained limit.

**Fresh evidence on the corrected sources** (`correction-1/`): Application `--filter "FullyQualifiedName~LocalExecutionReleaseInspection|FullyQualifiedName~LocalExecutionOwnershipRelease|FullyQualifiedName~LocalExecutionInspection"`
**591 passed**, 4 m 21 s (the 523 of the first return plus the 68 new cases; the shared projection feeds the candidate handler, the release command's replay and the new reader, all in this set); Api
`--filter "...LocalExecutionReleaseInspection|...LocalExecutionInspection|...LocalExecutionOwnershipRelease|...LocalExecutionSequenceEndpoint"` **199 passed**, 1 m 17 s; Architecture whole suite
**204 passed**; strict e2e `tsc` flag set exit 0 over 52 files; `dotnet format --verify-no-changes` over the 21 changed C# files clean; the corrected journey, headed and automatic (`playwright test -c
playwright.journey.config.ts e2e/local-quarantine-release.journey.ts --headed --trace off`, no Inspector or manual Resume, trace, video and screenshots off): **1 passed in 1 m 6 s**, first attempt. The
generated client hash is unchanged (`71898d8c…`; no API change). Incremental builds of the Application, Api integration, Architecture and journey host projects: 0 warnings, 0 errors.
**Preserved development attempts:** the new test class first failed to compile (C# 14 treats `field` as a keyword in a property accessor, and xUnit2013), fixed by renaming and `Assert.Single`; a shell
script that edited the journey died on quoting before writing anything and the edits were redone with the Edit tool; a stray prefix from one Edit was removed from this file.
**Retained, not rerun (actual scope unchanged):** the frontend product checks of the first return (549 Vitest cases in 21 files, `oxlint`, `npm run build`; no frontend product file changed), Domain,
Infrastructure, the whole Application, Api and Vitest suites, the other journeys, and the first return's controls and doc checks. The document link check is rerun at the end of this correction.

### Correction 2 (Codex review 2 NO-GO: LRI-R1 residual nullable numeric facts)

Preflight (once, before editing): `main`, `HEAD`, local `origin/main` and a fresh live `refs/heads/main` were `7ab7a15eab9ce5682578517f1bb106ca7557db21`, divergence 0/0, index empty, 20 modified and
29 untracked files; `planner-handoff.md` had the reviewed SHA-256 `a41f0332…14e33b`; ADR-0067, the decision index and the roadmap still had their selection hashes. The reviewer's temporary test source
(`logs/planner-review/local-release-inspect-01/review-02/LocalExecutionReleaseInspectionReviewerTests.cs`, SHA-256 `51aa711f…2c89`) is archived there and is not in `tests/`. Review 2 resolved LRI-V1
(the corrected journey source and its headed pass are retained) and accepted the three sibling tick guards and their regressions of Correction 1.

**LRI-R1 (P2), residual, resolved by this correction; awaiting review: nullable numeric facts.**
- *Defect.* Correction 1's guards returned the legal null for an unsupported nonnull value, so where the proof compares a legal null with a legal null it counted corrupt storage as supported history:
  `Observation.SemanticExitCode` (both families), `Attempt.AgentProcessExitCode`, `VerificationExecution.ExitCode` and the Agent witness `RecipeOrdinal` (the reviewer's fifteen cases: five sites times
  plain text, 0.5 and 2147483648) all read Recorded and the candidate reading Released while the workspace was Unavailable, abandonment blocked and the SQL fence held. My statement in Correction 1
  that the observation exit codes and every other projected integer already had a sufficient guard was wrong.
- *Audit of every nullable numeric fallback in this release row* (`LocalExecutionReleaseProjection`). Collapsing to null while null is legal: the four reviewed fields, plus two I found: the queued
  `Admission.RecipeOrdinal` (the proof compares it with a step's legal null recipe ordinal) and my own Correction 1 handling of `IntentRecipeOrdinal` and `IntentSemanticExitCode`, whose
  `int.MinValue` sentinel for the exit code was itself a valid Int32 (the reviewer's "no sole sentinel" rule applies; the sentinel is removed). Audited safe, because the proof requires a nonnull
  value or compares it with a nonnull one, so invalid-to-null is already a refusal: `Step`, `Witness` and `Admission` `Ordinal` and `PassNumber`; `Step.RecipeOrdinal` (its separate `HasRecipe`
  flag is true for any nonnull storage); `Observation.PhysicalExitCode`, both observation durations and `Attempt.AgentProcessDuration` (required for an Agent result); `Event.Sequence`,
  `Release.ConfirmationVersion` and `Root.RecipeCount`.
- *Correction (the only product change).* `LocalExecutionReleaseRow` gained an internal `StorageValid`, computed fresh in the same provider-neutral statement from the raw storage as the conjunction, over
  `Observation.SemanticExitCode`, `Attempt.AgentProcessExitCode`, `Execution.ExitCode`, `Release.IntentSemanticExitCode` (each: `NULL`, or exactly an Int32 by the existing four-way guard),
  and `Witness.RecipeOrdinal`, `Admission.RecipeOrdinal`, `Release.IntentRecipeOrdinal` (each: `NULL`, or an exact integer below the recipe bound), of `col IS NULL OR <exact supported integer>`. That keeps
  three cases apart (actual NULL, a supported exact integer, unsupported nonnull storage) without a sentinel: every valid Int32 stays representable and genuine nulls stay nulls. The value projections
  of those members are unchanged (the intent members now simply project null for an invalid value, since the flag refuses the row). `LocalExecutionReleaseProof.Prove` refuses a row with
  `!StorageValid` as Unavailable immediately after the single-row check, before `RootHolds`, first-release eligibility, receipt replay or any chain comparison, so unsupported storage is never classified as
  an absence (NotRecorded), a candidate or a release. The flag is a query-projected check, never persisted or cached, and grants nothing. The refusal is deliberately whole-row: a row with such a fact is
  Unavailable even when the unsupported fact belongs to a not-yet-eligible step. No public response, Domain eligibility, ownership expression, SQL guard, migration, second query, hashing function or
  generic reader changed; the candidate reading, a replay and a first confirmation become stricter in the same way (the release command re-reads through the same reader).
- *Permanent regressions* (`LocalExecutionReleaseInspectionNullableIntegerTests`, real file-backed SQLite, 56 cases): eight sites (observation semantic exit code for an Agent and for a verification, attempt
  exit code, execution exit code, witness recipe ordinal, queued admission recipe ordinal, and the queued Agent observation and attempt representatives) times plain text, 0.5 and 2147483648, each with an existing
  receipt (inspection, candidate and replay) and without one (inspection, candidate and an attempted first confirmation) = 48. Each proves the inspection is Unavailable, never NotRecorded and never Recorded, in one
  read-only statement; the candidate reading is Unavailable; the replay or first confirmation is refused with the fixed unavailable error; no release or event is appended; the selected-field
  fingerprint is unchanged (it does not directly compare the damaged raw cell); and the workspace, capacity and abandonment readings and both SQL seams (held before and after) are unchanged. Controls: actual nulls stay supported (a candidate before and a Recorded
  receipt after the release) and every valid Int32 exit code (`int.MinValue`, `-int.MaxValue`, -1, 0, 1, `int.MaxValue`) stays supported on an already-free Agent operation (still NotRecorded / NotEligible already_free).
  The original six probes, the 68 Correction 1 cases, the valid boundaries, the well-formed and checksum-only controls and the 54 original cases stay and pass.
- *Red and green* (`logs/executor/local-release-inspect-01/correction-2/`). The first red run (`01-nullable-red.txt`, 54 failures of 56) was red for the wrong reason in the no-receipt cases: the test used
  `Tamper.ReadIdsAsync`, which requires a release row, and failed with "Sequence contains no elements" (and the six valid-Int32 controls failed the same way). It is preserved, the tests were corrected to read the
  witness, observation, operation and admission identities directly, and a genuine red state was then recorded with only the proof admission temporarily disabled (the flag still computed; the file was restored to
  the identical corrected hash `f96c371c…`, `proof-hash-before-red.txt`): 48 damaged cases failed (24 receipt cases read Recorded, 20 no-receipt cases read NotRecorded and 4 failed later assertions) and the 8 controls
  passed (`01b-nullable-red-admission-disabled.txt`). With the admission, all 124 residual and numeric cases pass (`02`, `09`).

**Evidence on the final sources** (`correction-2/`): Application `--filter "FullyQualifiedName~LocalExecutionReleaseInspection|FullyQualifiedName~LocalExecutionOwnershipRelease|FullyQualifiedName~LocalExecutionInspection"`
**647 passed** (591 plus the 56 new cases), 4 m 46 s, covering the candidate handler, the release command's replay and the new reader that share the projection and proof; Api (release, inspection and
sequence-route set, same filter as before) **199 passed**; Architecture whole suite **204 passed**; incremental builds 0 warnings, 0 errors; the generated client hash is unchanged (`71898d8c…`);
`dotnet format --verify-no-changes` over the 24 changed C# files clean. The executor reported that the first format check failed on lines 391 to 393 of the projection and attributed it to CRLF handling of a comment
between constructor arguments. The comment moved into the method's documentation and the expression was laid out over short lines. `07-dotnet-format.txt` contains the clean final result, not the original failure;
the CRLF explanation is executor-reported, not independently established by that log. The runs above were repeated on the final source. `git diff --check` clean.
**Retained at actual scope, not rerun:** all frontend product checks (no frontend file changed in Correction 2) and the strict e2e `tsc` and headed journey of Correction 1 (the journey source is unchanged; its host
binary predates this product change, which alters no reachable behavior for valid data and is covered by the 647 file-backed cases through the same statement; no browser run was repeated); Domain,
Infrastructure, the whole Application, Api and Vitest suites and the other journeys; the first return's and Correction 1's mutation controls (no new mutation campaign).
**Inventory.** The 46-entry manifest of the first return and its post-correction recomputation list only those 46 paths: they omit `docs/roadmap/current-work.md`,
`LocalExecutionReleaseProjection.cs` and `LocalExecutionReleaseInspectionNumericIntentTests.cs` and are not an all-source identity of Correction 1. This correction refreshed the inventory from Git
(`correction-2/git-inventory-paths.txt` and `git-inventory.sha256`: every modified and untracked path including those code paths).

**Dispositions.** LRI-V1 resolved by review 2; its journey assertions, exact request count, pre-reload marks, API-scoped artifact and candidate filter and qualified fingerprint are kept and the POST inventory
is untouched. The Exited challenge, the accepted control outcomes (2a masked, 2b killed) and the older candidate decoder's millisecond chronology keep their dispositions. The reviewer's 144-case independent run
(129 passed, 15 failed, the 122 permanent cases, six original probes and the Agent Exited diagnostic passing) is the evidence this correction answers.


## Previous slice (independently verified published as 7ab7a15; historical record): LOCAL-QUARANTINE-RELEASE-01 Human release of a terminal local quarantine (ADR-0065)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority. Git and that slice's independent publication-verification record
(`logs/planner-review/local-quarantine-release-01/publication-verification.json`) show it published as `7ab7a15eab9ce5682578517f1bb106ca7557db21`;
the preparation-phase wording below is historical and grants no pending action.

**Delivery phase: correction 3 (Codex review 3 NO-GO: LQR-V1 only, a test-only browser-oracle defect) returned by the same Claude executor chat, unstaged, uncommitted and unpushed. No GO exists.**
The first return (unchanged below) is followed by [Correction 1](#correction-1-codex-review-1-no-go-lqr-r1-to-lqr-r4), [Correction 2](#correction-2-codex-review-2-no-go-lqr-r1-and-lqr-r2-adr-0066) and [Correction 3](#correction-3-codex-review-3-no-go-lqr-v1-the-journeys-business-post-inventory).
Baseline: `main`, `HEAD`, local `origin/main` and the live `refs/heads/main` were `569c6b13169bd55a259ca4f17d0780a8cc6ef259`
(LOCAL-QUEUE-OVERVIEW-01, independently verified published); the four planner inputs (`decisions/README.md`, `mvp-delivery-plan.md`,
`planner-handoff.md` and ADR-0065) are preserved byte-for-byte (hashes equal `logs/planner-review/local-quarantine-release-01/selection.json`).
ADR-0050 stays Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete; every standing restriction stays. No frozen
qualification profile, native/provider/credential experiment or real run was touched or recovered.

### What exists

- **Domain.** `LocalExecutionDispatchWitness`, `LocalExecutionTerminationObservation`, `LocalExecutionOwnershipRelease` (append-only, one
  writer each), `LocalExecutionOperationFamily`, the confirmation value (`LocalExecutionOwnershipReleaseConfirmation`: version 1, SHA-256
  digest of the canonical facts, expiry at most two minutes truncated to whole seconds) and the `local_execution_sequence.ownership_released`
  Human event type.
- **Producer provenance.** The two dispatch markers (Agent attempt and verification execution) append the witness in the same commit and return a
  private in-memory `LocalExecutionDispatchTicket` (internal constructor, no public member) only after that commit; the four Agent result
  commands and the verification result command carry it, and the result recorders append the termination observation in the ordinary result
  commit (or refuse the whole result with `observation_refused` when the observation contradicts the witness, operation or returned result).
  `ProcessExecutionResult.Termination` is populated by `ChildProcessExecutionAdapter` only after a successful `WaitForExitAsync` of the owned
  process (normal exit, or the existing post-kill wait); timeout, kill, wait, TimedOut/Cancelled and exit-code semantics are unchanged.
  A restarted host, a lost result or an old row holds no ticket and never produces evidence.
- **One ownership definition.** `LocalExecutionReleaseChain` (expression twin of the in-memory proof) is consumed only by
  `LocalExecutionOwnership` and the status reading; the nine SQL live-fence triggers were recreated by the migration with the same release proof, so claims,
  dispatch gates, manual starts, capacity, queue admission, final gates and the live fence see the same release or none.
- **Reading and command.** `GetLocalExecutionOwnershipRelease` is one provider-neutral untracked statement with guarded scalar projection and an
  in-memory proof (statuses NotEligible, Unavailable, Available, Released with closed reason codes); `ReleaseLocalExecutionOwnership` begins a
  manual transaction, takes the runs-row write lock, re-reads, validates replay (exact replay returns the receipt even after expiry), the
  observation identity, the confirmation version, expiry and digest, then appends the release and its one Human event atomically
  (`persistence_failed` / `persistence_unresolved` keep certainty honest). Protected GET and POST
  `/api/runs/{runId}/local-execution-sequence/steps/{stepId}/ownership-release`: closed response, strict four-string 512-byte body, fixed
  non-echoing 400/404/409/413. The status wire gained `ownershipReleased` per step (a released operation is neither quarantined nor exit-recorded).
- **Migration.** `20261023211312_AddLocalExecutionOwnershipRelease` (additive; append-only triggers; live triggers recreated). Down is refused before
  any change by a named CHECK on a temporary table (`used_release_feature_blocks_downgrade`) once any witness, observation or release exists.
- **Frontend.** Strict decoder `localExecutionOwnershipRelease.ts` (shared by GET and POST; answers must match the chosen run, step, sequence, state, mode,
  kind, ordinal, pass and operation), parsed-body wire clients carrying a real `AbortSignal`, `useLocalExecutionOwnershipRelease` (run, opening and
  per-step visit lifetimes over the unchanged owned-lifetime primitives; one read per visit, one confirmation, at most one ten-second submission, no
  retry, a local two-minute confirmation lifetime while the host stays the authority; submission consumes the confirmation; unknown outcome never
  infers rollback and needs an explicit fresh read; unmount aborts the request) and the collapsed `LocalExecutionOwnershipReleasePanel` mounted inside
  the local sequence panel. The hook state lives above the status mask, so an in-flight release and its outcome survive the run barrier; the panel
  marks nothing released itself. Fixed copy explains physical termination versus the failed result, the invoked-process-only limit, unchanged
  workspace and source gates and possible progress of already authorized queued work. Wording that said there is no supported recovery action
  (host reason and next-action text, `workspaceExecution.ts`, `executionCapacity.ts`, step and state copy) now says a separate Human release backed by
  recorded original-host termination evidence can free it and that without that evidence there is none; the next-action *code* is unchanged.
  The pinned event inventories (`runEventHistory`, `parseEventSummary` copy, the backend history test) know the new event type.
- **Journey.** `e2e/local-quarantine-release.journey.ts` (one authenticated owned-fixture journey): limit one; a first project plans for real and its
  authorized cycle's verification is held by the owned gate past a three-second recipe timeout, the host kills and observes the process, the cycle ends
  NeedsAttention and the quarantine STILL holds (manual Run disabled, workspace owner note, no release row); a second project (different repository,
  recognized by a second conventions marker added to the owned provider double `ManifestInfo`) plans and is QUEUED and waits for global capacity with
  zero double invocations; the release control is collapsed and reads nothing, then one chosen step shows the recorded physical evidence, one separate
  confirmation posts once, exactly one release and one Human event are appended and nothing is rewritten, the manual Run is enabled again, and the
  queued cycle reserves, claims and advances through its own gates to Awaiting Human approval with no further browser request.
- **Documentation.** ADR-0065 (planner-owned, untouched), `data-and-recovery.md`, `run-cockpit-specification.md`, `engineering-context.md`, the
  security plan and threat model, and this file.

### Evidence (logs under `logs/executor/local-quarantine-release-01/`, git-ignored; commands run at the repository root)

- **Fresh final matrix on the final restored sources** (`final/summary.txt`; non-incremental serial build, 0 warnings, 0 errors, twice; the regenerated
  client hashed identically after both builds, `235558c7…`):
  - Domain **1791 passed**; Infrastructure.IntegrationTests **3405 passed, 4 skipped** (the pre-existing environment-conditional skips: case-sensitive
    directory support, file symlink and Windows-only facts), 6 m 2 s; Application **8292 passed**, 22 m 25 s; Architecture **203 passed**;
    Api.IntegrationTests **3348 passed**, 11 m 57 s, on the rebuilt sources after the route-inventory fix below (the first run, 3347 passed and 1 failed, is kept in `final/`; build 3 also 0 warnings, 0 errors, client hash unchanged).
  - Frontend: whole Vitest **7634 passed** in 310 files; `oxlint` exit 0 (only existing warnings, none in the new files); `npm run build` (with product
    `tsc`) pass; the recorded strict e2e `tsc` flag set exit 0; `npm run test:harness` pass.
  - Journeys, serially on the same build: release 1.1 m, queue 56 s, local-execution 39 s, planning-sequence 23 s, collaboration 2.0 m, all passed.
  - Focused `LocalExecutionOwnershipRelease` suites: Application 142, Domain 34, Infrastructure 39 (process termination 7, migration 32), Api 41 (hosted 6,
    endpoint 35), Architecture 7; frontend new tests: decoder 60, hook 23, panel 15.
- **Four compiling controls** (`controls/`, each hash-guarded: baseline, mutant, restored hash equal, target touched, rebuilt; the 154 changed files were
  byte-identical to `pre-controls-source-manifest.sha256` afterwards), each killed by a named permanent regression:
  1. omit the Human release from effective ownership (both families in `LocalExecutionOwnership`): **24 of 142** release tests failed (the Entrance,
     Cycle and Restart suites, e.g. `A_manual_claim_is_refused_while_the_quarantine_holds_and_passes_its_own_gates_after_the_release`);
  2. omit a required witness identity comparison (`witness.RunId != root.RunId` in the in-memory proof): `LocalExecutionOwnershipReleaseChainTests
     .One_changed_stored_fact_of_the_chain_is_never_a_release_anywhere(name: "witness-run")` failed (1 of 46). Surviving scope: the SQL and expression twin
     (`LocalExecutionReleaseChain`) keeps its own comparison, so this control proves the reading twin only;
  3. remove the ended-confirmation owner guard in the hook (`isActive` and `now.visitOwner`): `a confirm callback retained from an ended visit sends
     nothing for the newer visit of the same step` failed (1 of 23);
  4. remove the used-feature downgrade refusal (`CHECK (used = 0)` made `used >= 0`): the three `The_reverse_migration_is_refused_before_any_change...`
     cases (witness, observation, release) failed (3 of 32).

### Preserved failures and defects found while testing

- Api full-suite first run (`final/test-Api.IntegrationTests-first-run-route-inventory-failure.log`): `LocalExecutionSequenceEndpointTests` route inventory
  expected 11 and found 12. The committed inventory (nine) already omitted the ADR-0063 `inspection/admissions` route, so the test was stale before this slice
  (earlier slices ran filtered suites); I corrected it to the twelve actual routes (adding that route and the two release operations).
- One real production defect was found by the chain tests and fixed: a release whose `WitnessId` was tampered read Available for an orphan receipt;
  the reading now joins the release by step and by witness and the proof compares the witness-keyed identity. Seven pre-existing migration table-list
  assertions were repaired for the new tables (original output kept in `migration-tests-first-run.log`).

### Challenges, limits and risks for review

- **Next-action code name.** The wire code `no_supported_recovery_action_in_this_version` is now a misnomer for a quarantine whose evidence exists; I kept the code
  (wire and decoder stability) and changed only the host's literal texts. Renaming it is a deliberate wire change for the planner to decide.
- **Confirmation lifetime.** The page ends a confirmation after two local minutes (monotonic timer) and never compares wall clocks; the host's expiry stays the
  authority and refuses a stale confirmation with a fixed 409.
- **Scope of the evidence.** Only sequence-linked operations carry a witness; a manual verification or Agent attempt that ended without exit evidence has none and
  stays unsupported. The control targets the current manual run only; run history and the inspectors stay read-only.
- **Owned double change.** The queued-work journey needed a second repository the provider double recognizes, so `ManifestInfo.IsLocalCycle` accepts a second
  conventions marker; behavior for every other repository is unchanged.
- **Control 2 survivors.** See above: the SQL twin is covered by other chain tests, not by this control.

### Correction 1 (Codex review 1 NO-GO: LQR-R1 to LQR-R4)

Preflight (once): `main`, `HEAD` and local `origin/main` equal `569c6b13169bd55a259ca4f17d0780a8cc6ef259`, divergence 0/0, empty index, 164 pending
paths (80 modified, 84 untracked); `planner-handoff.md` is the planner's updated file (SHA-256 `7a6b8355…`), preserved with the review and the unchanged
ADR, index and MVP inputs. The review's probe sources are `review-01/*.txt`; the first-return source manifest matched before work began.

**What changed (stable finding IDs)**

- **LQR-R1 (P1) one complete ownership expression and SQL fence.** `LocalExecutionReleaseChain` (the expression every shared consumer reaches through
  `LocalExecutionOwnership`, and `HasMalformedReleaseAsync`) and the migration's SQL twin (the nine live-fence triggers) now carry the same complete proof for both
  families: the run is a ManualAgent run of the sequence's own project; the workspace and the step's source checkpoint (workspace and fingerprint) are the
  step's own; the recipe and step counts are sound; an Immediate cycle has no admission for the step; neither family's identity collides with the other; the
  observation never outlasts its process and is recorded no earlier than the dispatch; the receipt is recorded no earlier than the observation and expires after it;
  and the terminal result is consistent (below). The additive migration (unpublished, edited in place with its Designer and the snapshot) also gained the receipt's
  two-minute lifetime as a table CHECK. Permanent file-backed tests (`LocalExecutionOwnershipReleaseChainTests`, 46 to 79 cases) change one stored fact of a
  production-created released chain and assert, independently, the read, the workspace, global capacity, abandonment and the real SQL write-seam guard.
  New cases: source checkpoint fingerprint and workspace, workspace project, run project, ManualAgent mode (three variants), observation before dispatch, receipt
  expiry before release and before the observation, Agent status/outcome/fingerprint inconsistencies, verification status/completion-fingerprint/exit-code
  inconsistencies, an admission present on an Immediate cycle, and attempt/execution identity collisions across the two families (rows cloned through the real
  database). The positive cases are preserved (all earlier release suites pass unchanged).
- **LQR-R2 (P1) complete candidate, locked-command and stored-receipt proofs.** The projection and `LocalExecutionReleaseProof` now read the run mode, the Agent
  checkpoint fingerprint, the Agent outcome class and the verification completion fingerprint, and decide terminal consistency from the existing Domain policies
  (`LocalExecutionReleaseTerminal`: the Agent outcomes are exactly those that neither require a clean exit nor are pre-invocation classifications, derived from
  `AgentProcessEvidencePolicy`; a verification follows `VerificationExecution.Complete`: TimedOut or Cancelled with a null exit code and a matching completion
  fingerprint, or SourceChanged with a differing one). The same proof is the candidate, the fresh locked command and the replay. `ProveReleased` recomputes the
  canonical confirmation digest from the proved facts and the recorded expiry. `LocalExecutionOwnershipReleaseTerminalProofTests` (23): before a release every
  inconsistent terminal result is Unavailable, a confirmed command is refused and no receipt or event exists; SourceChanged, the other post-invocation failure
  outcomes and a source-changed or cancelled verification stay releasable; a substituted digest or expiry reads Unavailable and its replay is refused; the fence
  vocabulary equals the policy-derived set. Protected API regressions: four inconsistent results, a non-manual run and a substituted digest (Api release suites 41 to 47).
- **LQR-R3 (P2) bounded release GET.** The read has the same real ten-second bound as the command: at the bound the request is aborted, a fixed read failure settles once, there is no
  retry or polling, cleanup clears the timer, and a transport that ignores the abort cannot answer late into the visit (tests in the hook and panel suites).
- **LQR-R4 (P2) closed operation-status vocabulary.** The decoder accepts only the statuses the host records for a candidate: an Agent attempt is `Failed`; a verification
  is `TimedOut` (timeout), `Cancelled` (cancellation) or `SourceChanged`; the process outcome is a timeout or cancellation with no semantic exit code (a clean exit
  already frees the operation and is never a candidate). Running, Passed, Completed, Interrupted, unknown and mismatched statuses decode to nothing and the panel offers
  no confirmation (decoder and panel tests; 98 existing feature tests became 129).

**Concrete translation and constraint challenges (raised with the work, not silently resolved)**

1. The SQLite EF provider cannot translate a `DateTimeOffset` comparison (the same documented limitation as the ordering queries elsewhere), so the chronology is compared as the
   stored normalized UTC text (`CAST(x AS TEXT)`); with a trimmed fraction a shorter text sorts before a longer one because `+` sorts before digits, so the text order is the
   chronological order of the values the host writes. The raw SQL twin compares the same text.
2. (Superseded by correction 2: the CHECK is now exact at tick precision.) The exact `expiry - release <= 2 minutes` bound cannot be expressed provider-neutrally with seven-digit ticks, so it is a table CHECK (`julianday` difference, 0.001 s
   tolerance) in the unpublished migration, kept by the release reading, the Domain and the command as well; it makes a violating stored receipt unrepresentable rather than detected.
   Its permanent test inserts 1, 120, 121 and 3600 second lifetimes.
3. (Superseded by correction 2 and ADR-0066: the typed snapshot, not the digest, is compared by every consumer.) SQLite cannot compute SHA-256 and no hashing capability was added, so the receipt's confirmation digest is verified by the release reading, the exact replay and the locked
   command, never by the shared ownership expression or the SQL fence. A direct database write that substitutes only the digest leaves that receipt unreadable (Unavailable) while the
   relational ownership proof, which is the Human release itself, does not depend on it. The digest is an exact-intent check, not authority (ADR-0065), so I judged that
   acceptable, but it is a documented surviving scope, not an untested equivalence.
4. (Superseded by correction 2: abandonment is now asked in the moved run's own scope.) In the run-project tamper case the abandonment assertion is skipped on purpose: abandonment is decided per project of the run, and the tamper moves the run out of that project.

**Planner dispositions applied.** `no_supported_recovery_action_in_this_version` stays a legacy compatibility token with truthful literal copy and is never a capability signal
(no wire rename). Control 2 of the first return is accepted as a reader-only kill; each corrected expression and SQL boundary now has its own control (below). The second conventions
marker of the owned double, the route-inventory repair, sequence-only provenance, current-run UI, invoked-process-only evidence, unsupported old and other quarantines, host-authoritative
expiry, the existing React-key warnings and the exact environmental skips are unchanged.

**Evidence (logs under `logs/executor/local-quarantine-release-01/correction-1/`, git-ignored)**

- **Compiling controls: 17, all killed, all restored.** Each control is hash-guarded (baseline, mutant, restored hash equal, target touched), must compile, and is killed by named permanent
  regressions; the 156 changed files were byte-identical to `pre-controls-source-manifest.sha256` afterwards (`controls-summary.txt` lists the failing test names per control).
  The four first-return controls were re-run on the corrected sources (1 release-in-ownership: 31 of 198 release tests fail; 2 reader witness-run comparison: 1; 3 hook owner guard: 1; 4 downgrade
  refusal: 3). New: 5 expression source-fingerprint (2 cases), 6 SQL source-fingerprint (the same 2 cases, so the SQL guard is decided by its own text), 7 expression chronology (2), 8 SQL chronology (2),
  9 expression Agent outcome vocabulary (4), 10 SQL Agent outcome vocabulary (4), 11 expression verification status consistency (1), 12 reader result consistency (11 of 23), 13 reader digest binding (2),
  14 frontend read bound (4), 15 frontend status vocabulary (12), 16 expression run mode (3), 17 SQL run mode (3). Preserved failed attempts: control 14 (unused import) and 15 (unused constant) did not
  compile and control 17 first did not match its pattern and then produced a SQL syntax error (79 of 79 failures); none counted as a kill, and 14, 15 and 17 were rebuilt and rerun.
- **Fresh final matrix on the restored sources** (`final/summary.txt`; non-incremental serial build twice, 0 warnings, 0 errors; the regenerated client hashed identically both times, `235558c7…`):
  Architecture **203**; Infrastructure.IntegrationTests (full, because the migration and its configuration changed) **3409 passed, 4 skipped**, 6 m; Application: first run **8347 passed, 1 failed** (below), 23 m 54 s,
  and the full rerun on identical sources **8348/8348**, 23 m 36 s; Api.IntegrationTests **3354/3354**, 14 m 45 s; whole Vitest **7665 passed** in 310 files; `oxlint` exit 0; `npm run build`, the strict e2e
  `tsc` and `npm run test:harness` pass. Journeys on the same build, serially: release 1.1 m, queue 57 s, local-execution 39 s, planning-sequence 22 s, collaboration 2.0 m, all passed.
  The four Infrastructure skips are exactly `PackageEntrypointResolverTests.Resolve_rejects_an_entrypoint_that_is_itself_a_reparse_point`, `FilesystemArtifactStoreTests.VerifyAndReadSealedAsync_returns_missing_when_the_sealed_file_itself_is_a_symlink_to_an_outside_sentinel_even_when_length_and_hash_match`,
  `UntrackedFilePreviewReaderTests.A_file_symbolic_link_to_an_outside_file_is_refused_without_returning_its_text` and `GitWorkspaceInstructionContextTests.A_symbolic_link_source_is_never_followed_to_content_outside_the_worktree`:
  this host cannot create a file-level symbolic link without elevation or Developer Mode, so that platform coverage did not execute.
- **The retained failed Application run.** `CreateCodexPlanningAttemptCommandHandlerTests.Two_concurrent_repair_claims_for_one_source_commit_exactly_one_repair` failed once in the first full run
  (the loser answered `workspace_execution.agent_attempt_active` instead of `agent_attempts.repair_already_requested`; log kept as `final/test-Application.Tests-first-run-concurrent-claims-failure.log`).
  It passed in the identical-source rerun, 30 of 30 alone, 48 of 48 in eight parallel processes and 40 of 40 under CPU saturation (`race/`). I did not reproduce it, did not attribute it to a cause and do not
  call it flaky: the loser's early workspace gate can see the winner's already committed Running attempt before its own claim-boundary duplicate check, which is an ordering the test itself races. This change only
  alters whether a retained quarantine owns, never a Running attempt, but I did not run the unchanged `HEAD` to prove the failure pre-existing, so that remains unproven.
- **Retained, not re-run:** Domain (1791 passed, first return; no Domain file changed in this correction). **Fresh and broader than asked:** the full Infrastructure suite. **Not run:** native, provider,
  credential or frozen-qualification activity, and any recovery of the owner's real runs.

### Correction 2 (Codex review 2 NO-GO: LQR-R1 and LQR-R2, ADR-0066)

Preflight (once): `main`, `HEAD` and local `origin/main` equal `569c6b13169bd55a259ca4f17d0780a8cc6ef259`, divergence 0/0, empty index, 167 pending paths; the five frozen planner
inputs (`planner-handoff.md`, `decisions/README.md`, `mvp-delivery-plan.md`, ADR-0065 and ADR-0066) match `logs/planner-review/local-quarantine-release-01/review-02/review-state.json`
byte for byte, before and after the work. Stable dispositions: LQR-R1 and LQR-R2 corrected below (awaiting Codex review 3); LQR-R3 and LQR-R4 resolved and preserved.

**What changed**

- **LQR-R2 (P1) the confirmation binds actual values, not coherence classes.** The canonical facts and the digest now include the concrete `AgentOutcome` or the verification
  `CompletionFingerprintSha256`. The existing release row (no fourth table, no JSON authority, no cache, no hashing capability) gained a versioned typed snapshot of every canonical
  confirmed fact, the confirmation version and the exact expiry (33 `Intent*` columns with named CHECK constraints for version, family, vocabulary, numbers, shape, fingerprints and times),
  captured only from the fresh complete proof under the Human command's write lock, after the digest and expiry validated, in the same receipt and event commit. The release reading
  (`LocalExecutionReleaseProof.IntentEquals`), the ownership expression (`LocalExecutionReleaseChain`) and the nine SQL live-fence triggers each compare the complete current proved tuple
  with that snapshot field for field, including the concrete result and the physical facts. A missing, unsupported or mismatched snapshot keeps ownership held; a stale pre-release
  confirmation writes no receipt and no event; a post-release change of any recorded fact can neither replay nor free ownership. The additive migration, its Designer and the model
  snapshot were edited in place (still unpublished; `has-pending-model-changes` is clean) and its downgrade refusal is unchanged.
- **LQR-R1 (P1) exact lifetime and complete fingerprint proof.** The receipt's two-minute bound is a table CHECK in integer ticks parsed from the stored text
  (`strftime('%s', …) * 10000000 + the seven-digit fraction`), an upper bound only (the lower bound and the chronology stay in the relational proof, so expiry-before-release stays
  representable and detected). It refuses release + 2 minutes + 1 tick in both families; exactly two minutes, one tick less and fractional boundaries are accepted. A completion
  fingerprint that changes to a malformed, oversized, short or uppercase value after release is now a snapshot mismatch in the reader, the ownership expression and the SQL fence.
- **Checksum-only exception (ADR-0066's narrow resolution).** Only a well-shaped substituted digest with the entire typed snapshot, the exact expiry, the physical chain and the Human
  event otherwise intact reads Unavailable and refuses replay (no second receipt or event) without reacquiring ownership, so the workspace, capacity, abandonment and the SQL fence stay
  released. The initial confirmation and every coherent replay still verify the digest. A substituted expiry alone is a snapshot mismatch and keeps ownership held.
- **Neutral copy.** The fixed Unavailable and refusal copy (backend `LocalExecutionOwnershipReleaseErrors.Unavailable`, `RELEASE_REASON_COPY`, `REFUSAL_COPY`) no longer claims that a hold stays
  or was released; it points to the live workspace and capacity readings.

**Permanent regressions added**

- Application: `LocalExecutionOwnershipReleaseSnapshotTests` (21): the reviewer's seven cases (concrete result drift before and after release in both families, the one-tick lifetime in both
  families, a malformed completion fingerprint), more malformed/short/oversized/uppercase fingerprints, a fresh candidate after a pre-release drift that releases exactly once, the receipt storing
  the confirmed facts field for field with an unchanged replay writing nothing, the checksum-only distinction (three kinds, read Unavailable, replay refused, one receipt and one event,
  ownership free in the read, the workspace, capacity, abandonment and both write seams) and the substituted expiry keeping ownership held. `LocalExecutionOwnershipReleaseChainTests` grew from 79 to
  132 cases: every `Intent*` column drifting alone, coherent-but-different authoritative drifts (Agent outcome, sequence state, physical exit code, elapsed time, process duration, dispatch time,
  source fingerprint everywhere it is recorded, completion fingerprint in four malformed or valid-different forms), and five "agreeing snapshot" cases that suspend only the release row's CHECKs
  (`ignore_check_constraints`) so a defense layered behind the snapshot is proved by its own boundary. Every case asserts the read, the workspace, capacity, abandonment and both actual SQL write
  seams (live fence and run abandonment) independently; abandonment is asked in the moved run's own scope (the moved project, and a run-id probe of the real abandonment update).
- Migration (89), Domain (37 release tests), API (the stored-digest test became the checksum-only test with capacity and the exact error, plus a concrete-outcome drift test before and after release; 49 release
  tests) and frontend (an unreadable answer and a refusal with neutral copy; 104 focused tests).

**Evidence (logs under `logs/executor/local-quarantine-release-01/correction-2/`, git-ignored; commands at the repository root)**

- **Compiling controls: 26 runs, 25 distinct killed, 1 retained survivor, all restored.** Each is hash-guarded (baseline, mutant, restored hash equal, target touched, rebuilt), must compile and is killed by named
  permanent regressions (`controls/control-N-tests.log`; the pre- and post-control manifests of every non-doc file match except the two test files I extended deliberately). Retained controls were rerun on the
  corrected sources at their actual scope: 1 release in ownership (47 of 265 release tests fail), 2 reader witness-run comparison (1), 3 hook owner guard (1), 4 downgrade refusal (3), 5 expression and 6 SQL source
  fingerprint (2 each), 12 reader result consistency (11), 13 reader digest verification (1; the checksum-only cases), 14 frontend read bound (4), 15 frontend status vocabulary (12), 16 expression and 17 SQL run mode (3 each).
  New controls: 18 reader snapshot equality (30), 19 expression Agent-outcome snapshot equality (5), 20 expression completion-fingerprint snapshot equality (12), 21 SQL Agent-outcome equality (5),
  22 SQL completion-fingerprint equality (12), 23 exact-tick lifetime CHECK in the Application (2) and 24 in the Infrastructure suite (10), 25 expression and 26 SQL SourceChanged completion-fingerprint consistency.
  **Controls 7, 8, 9 and 10 (expression and SQL chronology, expression and SQL Agent outcome vocabulary) first survived** (`survivors-first-run/`): the snapshot equality and the storage CHECKs shadowed those independent
  defenses, which is the "keep independent defenses intact" risk. I added the five "agreeing snapshot" cases and reran them; all four are now killed. **Control 11** (the expression's Failed verification status) still
  survives: the explicit Intent status clause of the same expression rejects a Failed status too, so the mutant is equivalent inside that expression; I replaced it with controls 25 and 26 on the part of the consistency
  proof nothing duplicates and kept 11's logs rather than hiding the survivor. Not kills, kept as evidence: the first campaign attempt (every control stopped in `mutate.js` on a halved backslash; no mutant was applied,
  `attempt0-mutate-script-error-*`).
- **Fresh final matrix on the restored sources** (`final/summary.txt`; non-incremental serial build twice, 0 warnings, 0 errors; the regenerated client hashed identically, `235558c7…`): Domain **1794**; Architecture **203**;
  Infrastructure.IntegrationTests (full, because the migration and its configuration changed) **3462 passed, 4 skipped** (the same four environment-conditional file-symlink and reparse-point skips as in correction 1),
  5 m 53 s; Application **8422/8422** in one run, 25 m 45 s; Api.IntegrationTests **3356/3356**, 12 m 30 s; whole Vitest **7667 passed** in 310 files; `oxlint` exit 0; `npm run build`, the strict e2e `tsc` and
  `npm run test:harness` pass. Journeys on the same build, serially: release 1.1 m, queue 57 s, local-execution 38 s, planning-sequence 21 s, collaboration 2.0 m, all passed. A whitespace-only formatter fix to one
  Domain test line followed the matrix; the Domain suite was rerun (1794).
- **Repair-race failure.** The correction-1 failure `Two_concurrent_repair_claims_for_one_source_commit_exactly_one_repair` (retained under `correction-1/final/`, with the identical-source rerun and the diagnostics) did not recur in
  this correction's full Application run; I ran no stress campaign, relaxed no assertion and touched no race infrastructure. Its cause and pre-existing status remain unproven.
- **Reviewer probes.** The seven behavioral probes (`review-02/ReviewerLocalExecutionReleaseCorrectionProbeTests.cs.txt`) were red on the corrected-1 tree; their permanent equivalents are green and each boundary has a control.
- **Formatting and hygiene.** `dotnet format --verify-no-changes` over the 135 changed C# files reports only the ten pre-existing whitespace findings of `MarkAgentAttemptDispatchedCommandHandlerTests.cs`; one type per
  file holds (the architecture suite passes); `git diff --check` reports only existing CRLF notices of generated files; the frozen planner inputs are unchanged.
- **Retained, not re-run:** no earlier evidence is claimed fresh beyond the above; correction-1 and first-return logs are preserved as they were. **Not run:** native, provider, credential or frozen-qualification activity,
  and any recovery of the owner's real runs.

**Challenges and limits for review (raised with the work)**

1. **Chronology as text.** The SQLite provider cannot translate a `DateTimeOffset` comparison, so chronology is compared as the stored normalized UTC text (`+` sorts before digits, so the text order is the time order the
   host writes). Unchanged from correction 1; the SQL twin compares the same text.
2. **Lifetime CHECK is an upper bound only** and is integer-tick arithmetic parsed from the stored text, because no provider-neutral exact bound exists. It supersedes correction 1's `julianday` tolerance.
3. **Digest and SQLite.** The digest is still verified only where hashing exists; ADR-0066's snapshot is what the expression and the SQL fence compare. A well-shaped substituted digest is unreadable but does not reacquire the
   owner (the narrow exception). If both expiry columns are rewritten to the same new value the snapshot still agrees; the reader then fails only on the digest, so that tamper is treated as checksum-only. I judged it
   undetectable without a second copy of the expiry and did not add one.
4. **Fingerprint shape is proved transitively.** The expression and the SQL fence do not repeat a lowercase-SHA-256 shape test for the completion fingerprint; the snapshot CHECK (`_intent_fingerprints`) guarantees the stored
   snapshot has that shape and field-for-field equality carries it to the current value. The reader additionally validates it directly.
5. **Drifts that CHECK constraints make unrepresentable.** `IntentVersion`, `IntentConfirmationVersion` and `IntentSemanticExitCode` cannot be set to a drifted value through the real table, so they have no catalog case; the
   migration tests prove the constraints. The "agreeing snapshot" cases use SQLite's `ignore_check_constraints` only to reach states the constraints otherwise forbid.
6. **Survivor of control 11** (above) is an equivalent mutant inside one expression, not a missing test; it is disclosed rather than dropped.

### Codex review 3 disposition

**NO-GO for LQR-V1 (test oracle only); LQR-R1/R2 are resolved and LQR-R3/R4 remain resolved.** The correction-2 report and its failed/green
history above retain their preparation phases. Independent review evidence is under `logs/planner-review/local-quarantine-release-01/review-03/`.
Application release tests plus the seven retained reviewer regressions: 279/279 (272 permanent, seven temporary), 2 m 11 s; temporary coordinated-expiry
limit/invalid-chronology diagnostics: 4/4; full Domain 1794; release Infrastructure 96; protected API/hosted release 49; Architecture 203; frontend release 131.
Reviewer test sources were removed and the Application test assembly rebuilt. The one changed source-manifest entry is exactly the declared Domain-test
whitespace edit: reconstructing its pre-format line in memory matches hash `e90a4e8d…`; the current Domain suite passes. The ten existing formatter findings
are on unchanged lines of the dispatch-marker test file; its actual diff changes only an import and the earlier dispatch-result assertion.

The first independent, automatic headed journey failed in 37.5 s at line 314: the raw POST list contained `/hubs/run` while the test expected none.
`RunNotificationHub` is broadcast-only and the host explicitly uses LongPolling; this is notification transport, not a business command. Line 335 also
indexes the raw list although adjacent assertions already filter `/api/`. A temporary two-assertion diagnostic used the same API-command filtering and
an exact one-release-path assertion; the full headed journey passed (1.1 m). The original test bytes were restored to SHA-256
`2c85131e8b9ead32f9aa84fe31409af17b3c2cf1536f2ab243ecd92cca98c478`. The first run remains failed; the diagnostic pass is not a pass on the returned source.
Its first exit observer attached after the driver ended and recorded no exit code; the actual test log establishes the failure. The diagnostic's own driver
handle records exit 0. Preserve both results and the duplicate React-key/other existing warnings.

Planner challenge dispositions: the intent fingerprint CHECK plus equality is a valid transitive shape proof; version/null drifts the real CHECKs refuse
need no fabricated catalog state; integer upper-bound enforcement plus the separate chronology proof is accepted. Control 11 is equivalent within its
expression because the later Intent-status branch already excludes Failed; it remains a survivor, with controls 25/26 supplying the non-equivalent
consistency kills. Coordinated changes to both expiry copies are a declared limit of relational consistency, not proof that only the checksum changed or
authentication against coherent database rewrites. The normal host has no rewrite, replay still refuses, and no extra grant is created; documentation now
states that limit explicitly. No extra copy, hashing capability or recovery authority is selected.

LQR-V1 correction is restricted to the journey's request oracle and focused regression coverage. Keep all business API writes visible: zero before confirmation,
exactly the release afterwards, no hidden extra command. Add coverage for notification POSTs interleaved with the release and an extra API write remaining
detectable. No product/hub/transport change, retry, sleep, pause, broad fixture redesign or backend rerun is selected. Run the corrected short journey headed
automatically (announce it; owner watches only) and in its original headless mode serially, strict e2e TypeScript and affected harness tests. Retain other
unchanged matrix/journey evidence at its source scope. Return unstaged, uncommitted and unpushed for review 4.

### Correction 3 (Codex review 3 NO-GO: LQR-V1, the journey's business-POST inventory)

Preflight (once): `main`, `HEAD` and local `origin/main` equal `569c6b13169bd55a259ca4f17d0780a8cc6ef259`, divergence 0/0, empty index, 169 pending paths (80 modified, 89 untracked); the five frozen planner
inputs match `logs/planner-review/local-quarantine-release-01/review-03/review-state.json` and the retained original journey equals `2c85131e…` before work began. Stable dispositions: **LQR-V1 corrected below
(awaiting Codex review 4)**; LQR-R1 and LQR-R2 resolved and LQR-R3 and LQR-R4 still resolved by review 3. No production, hub, transport, authentication, backend, generated-client or product-frontend file changed. The review's
qualifications of this file, `data-and-recovery.md` and `threat-model.md` (coordinated expiry rewrites as a declared limit of unsigned relational consistency, UTC chronology qualified to supported normalized host
text, the accepted transitive fingerprint proof and the preserved control-11 survivor) are preserved as written.

**LQR-V1 (P2) the defect and the correction.** The reviewer's unmodified headed run of `e2e/local-quarantine-release.journey.ts` failed at the old line 314 because the raw POST suffix contained `/hubs/run`: the host maps the
run notification hub with LongPolling only, so the browser's SignalR connection posts (negotiate and long-poll messages) at moments no business action controls, and `RunNotificationHub` is broadcast-only with no business
method. The journey's raw `posts` list also made the old line 335 (`posts[postsBeforeRelease]` is the release) depend on transport traffic not shifting an index, while the adjacent assertions already used an `/api/` filter.
My correction is one local collaborator and one consistent use of it:

- New journey test support `e2e/journey/requestInventory.ts` (no production or shared harness change). `PostInventory` keeps the raw record of every POST and classifies the one explicitly named exception, the notification
  transport (`/hubs/run` and anything beneath it, case-insensitive like the host routing), as transport; **every other POST is a business POST on purpose** (it is a deny-list of the one known transport, not an `/api/`
  allow-list), so an unexpected write path also fails loudly. Every index is taken from the raw record by `mark()` and read through `businessSince(mark)`, so transport messages can never shift which request is the release.
- The journey now records every POST through the inventory and asserts: zero business POSTs before the confirmation (`businessSince(postsBeforeRelease)` equals `[]`, the negative assertion kept); exactly the one release path
  afterwards, twice (`toEqual([stepPath])`, replacing the raw-index assertion; the exact path, not a count or a regex on the first entry); the arm, queue and release counts over the business list; and nothing but transport after the
  reload mark. Every additional API POST, duplicate release or unexpected path remains detectable. No assertion was removed or loosened, nothing is filtered for the release path alone, no sleep, retry, transport, hub or
  authentication change, and no error was silenced.
- Focused regressions (a new `describe` in the existing `e2e/harness/journeyHarness.test.ts`, run by `npm run test:harness`; six tests): transport traffic interleaved before the confirmation leaves zero business POSTs while the raw
  suffix is not empty (the original oracle's defect); the release stays the only and first business POST however transport interleaves around it; an extra API POST (queue, approval, another step's release) in three
  interleavings still fails the inventory; a duplicate release, a non-API write and the lookalike paths `/hubs/runner` and `/hubs/run-other` count as business POSTs; transport classification (case, trailing slash, only the hub path
  and what is beneath it, never `/api/hubs/run`); and marks always come from the raw record. Four temporary mutants of the helper (transport counted as business, everything transport, `/api/`-prefix classification, the
  mark ignored) were each killed by named tests (5, 5, 2 and 2 failures; `correction-3/mutants/`), the original restored and hash-verified.

**Evidence (logs under `logs/executor/local-quarantine-release-01/correction-3/`, git-ignored; commands at the repository root unless noted)**

- **Fresh:** `npm run test:harness` **95 passed** (89 before the six new tests); strict e2e `tsc` (the recorded flag set) exit 0; `oxlint` exit 0 with no finding in the changed files.
- **Corrected journey, once headed then headless, serially, on the unchanged built host** (`BrowserJourneyHost.exe` and the four core assemblies hash-identical before and after, `host-binaries-before.sha256`): the owner was told before the
  headed run (a desktop notice and the executor message; the mobile push was not sent because Remote Control was inactive); `playwright test -c playwright.journey.config.ts e2e/local-quarantine-release.journey.ts --headed --trace off`,
  **1 passed in 58.6 s, exit 0** (`journey-headed.log`, `journey-headed.exit`); the identical command without `--headed`, **1 passed in 1.1 m, exit 0** (`journey-headless.log`, `journey-headless.exit`). No `--debug`, `--ui`,
  `PWDEBUG`, Inspector or manual step was used and trace, screenshots and video stayed off. The existing duplicate-React-key console errors in the journey output are unchanged.
- **The failed reviewer journey is preserved separately, not replaced.** The returned-source headed failure (37.5 s) and the reviewer's two-assertion diagnostic (a pass that is not a pass on the returned source) stay untouched in
  `logs/planner-review/local-quarantine-release-01/review-03/` (`visual-journey-20261025-181544/`, `visual-diagnostic-20261025-182218/`, `journey-diagnostic.diff`, `journey-original.bin`). My two passes above are the only evidence for the corrected
  source. My oracle differs from the reviewer's diagnostic: it also treats a non-API, non-transport POST as a business POST and takes indices from the raw record.
- **Retained, not re-run (unchanged scope):** the correction-2 full matrix (Domain 1794, Architecture 203, Infrastructure 3462 passed and 4 skipped, Application 8422, API 3356, whole Vitest 7667, lint, product build, strict e2e, harness
  and the five journeys, where the release journey's earlier pass was on the pre-correction journey source), the 26 controls and the generated client (`235558c7…`); review 3's independent results (Application 279 including seven reviewer
  regressions, four coordinated-expiry diagnostics, Domain 1794, Infrastructure release 96, API release 49, Architecture 203, frontend release 131). The other four journeys (queue, local-execution, planning-sequence, collaboration) are
  unchanged files and were not re-run. **Not run:** a backend rebuild or test, client regeneration, native, provider, credential or frozen-qualification activity, any recovery of the owner's real runs, and the correction-1 stress
  campaign. The historical repair-race failure, every control failure, the equivalent control-11 survivor, the declared coordinated-rewrite limitation, all warnings and the four environment skips stay preserved as recorded above.
- **Hygiene.** The journey file gained only the inventory import and consistent use; the changed set is exactly `e2e/journey/requestInventory.ts` (new), `e2e/harness/journeyHarness.test.ts` (new `describe`) and
  `e2e/local-quarantine-release.journey.ts`; pending paths are 171 (81 modified, 90 untracked) with an empty index.

**Challenge for review.** I classify by deny-listing the single named transport rather than allow-listing `/api/`, which is stricter than the review's wording ("the same `/api/` classification") because a write to any other path
would also fail; if the planner prefers the `/api/` allow-list the change is one line in `isBusinessPost`, and I did not choose it silently. The other journeys keep their own raw POST logs untouched (out of scope).

### Codex review 4 acceptance

All findings are resolved: LQR-R1/R2/R3/R4 and LQR-V1. Exact-content GO is recorded in planner-handoff.md and the external review-04 approval record;
publication is pending, not established by this document. Earlier delivery and NO-GO wording above records those preparation phases.
Codex accepts the notification-namespace exception: unknown API and non-API POSTs stay counted, raw marks remain stable, and the test oracle grants no
server authority. Independent `npm run test:harness` passes 95/95; strict e2e TypeScript passes for 52 files with the exact command recorded under
`logs/planner-review/local-quarantine-release-01/review-04/`. The corrected permanent-source headed/headless logs and exit files were inspected.
No new browser rerun, backend build/test or client generation was selected for this test-only correction. The other matrix/results, failed reviewer
journey, restored diagnostic, formatter qualification, control survivor, warnings/skips and unproven repair-race cause keep their actual scopes.
Only the reviewed tree/path set with sole parent 569c6b1 is authorized for normal publication; no next slice or closure commit is selected.

## Previous slice (independently verified published as 569c6b1; historical record): LOCAL-QUEUE-OVERVIEW-01 outstanding queue-admission overview (ADR-0064)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**Delivery phase: correction 1 returned by the same Claude executor chat after Codex review 1 NO-GO (LQO-R1, P3), unstaged, uncommitted and unpushed.
No GO exists.** The first return (unchanged below) is followed by [Correction 1](#correction-1-codex-review-1-no-go-lqo-r1-p3).
Baseline: `main`, `HEAD`, local `origin/main` and the live `refs/heads/main` were `1e58dda4b1f4a0b3292c56c4cc86fb0f15c49d66`
(LOCAL-QUEUE-INSPECT-01, independently verified published), divergence 0/0, real index empty; exactly the three planner documents
(`planner-handoff.md`, `decisions/README.md`, `mvp-delivery-plan.md`) and the new ADR-0064 were pending and are preserved byte-for-byte, not
edited. LOCAL-QUEUE-INSPECT-01 below is a historical record and was not reopened or republished. ADR-0050 stays Proposed, P9 unproven, AI
Memory deferred, Increments 4/5/6 incomplete; every standing restriction stays.

### What exists

- **Application.** `GetLocalExecutionQueueInspectionQuery` (one query, returned directly, no `Result`) over **one** provider-neutral untracked EF
  statement, `LocalExecutionQueueInspectionStatement`: the candidates are the admissions whose stored state is not a *recognized* Consumed or Retired
  one (`CASE ... = 0`, so unknown, null-like, look-alike or malformed states stay candidates), in `AdmissionNumber`/`Id` order, `Take(69)`;
  each is left-joined to its sequence and to *every* sequence of the same run (a window `ROW_NUMBER() <= 2` over the run, so a duplicate shows as a
  second root), the registered project (identity and a name bounded to 800 stored bytes before conversion; the path is never selected), the run, and
  the bounded ledger (a window `ROW_NUMBER() <= 103` per candidate sequence, the existing per-sequence guard) with every ADR-0063 historical reference
  (step, workspace, initial and source checkpoint, report message, pinned recipe). The final ceiling is `69 * 104 + 1 = 7,177` rows. The two inner
  windows are restricted by `IN (candidate sequence ids)`, so their work follows the candidate set, not the table. `LocalExecutionQueueInspectionProof`
  proves each candidate's rows with the **unchanged** `LocalExecutionAdmissionHistoryProof` (whole ledger, single root, two-root duplicate guard,
  every reference), then requires the candidate to be exactly the proved last admission (identity, order number, state), a candidate state of
  Waiting, Reserved or Unresolved, the run's own project with a nonblank name of at most 200 units, strictly increasing order numbers, distinct
  sequences and runs, at most 64 candidate sequences that have not ended, at most four held, and 68 entries in all (a 69th candidate or any
  count/bound/reference defect is the closed `Unavailable`, never a partial list). Statuses `Empty` / `Recorded` / `Unavailable`.
  The ADR-0063 projection moved, unchanged in meaning, out of `LocalExecutionAdmissionHistoryStatement` into
  `LocalExecutionAdmissionHistoryProjection.RowOf` (one expression, also exposed as `Project`) over the join shape
  `LocalExecutionAdmissionHistoryJoin`; the per-run statement and reader are otherwise untouched. A run that is absent reads as `Guid.Empty`,
  which the proof already refuses. No split read, N+1, revision sandwich, write lock, transaction, retry, ownership/eligibility/capacity read,
  schema, migration, Domain or scheduler change.
- **API.** One authorized bodyless, queryless MVC GET `/api/local-execution-queue/inspection` (`LocalExecutionQueueBaseEndpoint`,
  `GetLocalExecutionQueueInspectionEndpoint`) reusing `LocalExecutionInspectionBodylessGuardFilter` and its fixed non-echoing 400s, with the API-owned
  closed graph `{ status, entries[] }` whose entry is `{ projectId, projectName, runId, sequence, admission }`; `sequence` and `admission` are the
  existing ADR-0063 header and row response types (the endpoint's two mappers became `internal`). Regenerated client: additive, 152 lines, 0 removed.
- **Frontend.** Strict whole-answer decoder `localExecutionQueueInspection.ts` (it reuses the now-exported ADR-0063 header and admission decoders and
  adds the entry, membership, order, 64/4/68 and state-fits-sequence rules), `LocalExecutionQueueInspectionWireClient` (a parsed-body facade that
  carries its own `AbortSignal` into the actual request), `useLocalExecutionQueueInspection` (host-session, opening, visit and selection lifetimes
  over the unchanged owned-lifetime primitives; one GET per open or reload with a real `AbortController` and a ten-second bound that aborts the
  request itself; reload and close discard rows and selection at once and abort the request; `select` rechecks the exact admission, sequence, run and
  project in the visit's validated overview when it runs and selects the held entry, never the caller's object; each selection has its own key, so even
  re-selecting the same entry is a new lifetime) and `LocalExecutionQueueInspectionRegion`, mounted once in `App` right after the global
  limit control, independent of project selection. A row's "Select recorded cycle" renders the **existing** `LocalExecutionInspectionPanel` for
  exactly that run (collapsed; its own explicit actions remain required); "Clear selected recorded cycle" ends a selection. Fixed copy states that it
  is a recorded snapshot, not every active cycle, queue position, eligibility, process activity, free capacity or a start promise, that a passed
  deadline is only a record, and that empty is not idle. No polling, SignalR, retry, prefetch, browser storage or command control.
- **Journey.** `e2e/local-execution-queue.journey.ts` now (5c) selects the other project (which has no run), opens the global region (collapsed, no read
  until opened; exactly one outstanding admission: the Waiting cycle, the held owner's running verification is not an admission), selects the cycle,
  proves selection read nothing and left the selected project untouched, then opens the inspector and its ledger explicitly (exact GETs), switches back
  without a re-read, and (7b) after the cycle progressed to Awaiting Human approval shows the region still holds its earlier snapshot, then reloads it
  explicitly: rows and selection vanish and the empty answer (the Consumed tail is absent) says it does not mean no cycle is active. The existing
  ledger/evidence assertions are unchanged.
- **Documentation.** `agent-collaboration-evidence-inspection.md` (new "Recorded outstanding queue admission overview"),
  `run-cockpit-specification.md` ("Outstanding queue admissions (LOCAL-QUEUE-OVERVIEW-01)"), `engineering-context.md` and this file.

### Evidence (logs under `logs/executor/local-queue-overview-01/`, git-ignored; commands run at the repository root)

- **Fresh, final matrix on the final restored sources (after both controls, hashes restored, targets touched, projects rebuilt, client regenerated twice
  and the test projects rebuilt again; `final-source-manifest-before.sha256` holds the hash of every changed or new non-document file):**
  - Application, `--no-build --no-restore --filter 'FullyQualifiedName~LocalExecutionQueueInspection|FullyQualifiedName~LocalExecutionAdmissionHistory|FullyQualifiedName~LocalExecutionInspection'`:
    **442 passed**, 0 failed, 3 m 11 s (`final-application.log`): 88 new (`LocalExecutionQueueInspectionTests` 13, `...BoundTests` 15, `...DamageTests` 43,
    `...ConsistencyTests` 17) plus the 354 existing ADR-0061/0063 tests. Real file-backed SQLite, production-created cycles, independent connections.
  - Api, same filter: **116 passed** (20 new: `LocalExecutionQueueInspectionEndpointTests` 18 and `...EndpointStorageTests` 2; 96 existing), 1 m 13 s
    (`final-api.log`): route/authorization metadata, 401/405/body/query matrices, closed wire, per-run cross-check, storage damage, actual OpenAPI.
  - Architecture, full project: **196/196** (`final-architecture.log`); 192 existing plus the 4 of the new `LocalExecutionQueueInspectionBoundaryTests`;
    the existing `LocalExecutionInspectionBoundaryTests` (which also scans the extracted projection) still passes unchanged.
  - Frontend: whole Vitest **7,527 passed** in 307 files (`final-vitest.log`; +73 tests in 6 new files: decoder 21, contract 11, hook 19, transport 6, region 10,
    App 6); `oxlint` exit 0 with only the existing warnings, none in the new files (`final-lint.log`); `npm run build` (includes `tsc -b`) pass
    (`final-build-frontend.log`); the recorded strict e2e `tsc` flag set exit 0 (`final-e2e-tsc.log`).
  - Generated client: a non-incremental serial Api build (`--no-incremental -p:UseSharedCompilation=false -m:1`, 0 warnings, 0 errors) reproduced
    `api-client.ts` byte-identically (hash `d10178d9ca4887e9`, 860,484 bytes, before and after; the LOCAL-QUEUE-INSPECT-01 hash was `ed2855a042d5354c`);
    the diff is 152 added lines and none removed (`22-client-reproducibility.log`).
  - Journeys, one Playwright invocation at a time on the final binaries (`final-journey-*.log`): queue **49.9 s passed** (the extended journey; 55.2 s with start-up), existing local-execution **29.0 s passed**, planning-sequence **16.1 s passed**, collaboration **1.9 m passed** (shared-harness consumers: the new region mounts in `App`); the duplicate-React-key console errors are the existing ones. Two earlier development runs of the queue journey are kept (`19-...run-1.log` failed on my own assertion, `20-...run-2.log` passed).
  - Hygiene: `dotnet format --verify-no-changes` clean over all 29 changed or new C# files (`final-dotnet-format.log`); `git diff --check` clean; the new non-document files have no CR, trailing space or tab and only the existing `·` and `…` as non-ASCII (the architecture, product and context documents add none; this record names them once); `check-doc-links.js` (negative-controlled) found 0 broken relative links or fragments in the changed documents; the pre-matrix and post-matrix hash manifests of every changed or new non-document file are byte-identical (`final-source-manifest-before/after.sha256`, 47 files).
- **Compiling controls (`controls/control-1.sh`, `control-2.sh` with their mutation scripts; each refuses a no-op or a changed baseline, mutates, builds or
  typechecks, runs the named tests, restores from a byte backup and the baseline hash is verified equal afterwards; then the targets were touched and
  rebuilt):**
  1. Exclude ended sequences from overview membership (`LocalExecutionQueueInspectionStatement`: an extra `Where` requiring a sequence that has not ended):
     compiled; **8 of the 28 selected tests failed**: `...Tests.An_unresolved_reservation_on_an_ended_sequence_is_listed_with_its_ended_header_and_its_unresolved_step_link`,
     `...BoundTests.The_largest_coherent_answer_is_the_per_run_ledger_tail_of_every_entry_and_names_the_registered_project`,
     `...Overflow_is_detected_even_when_every_one_of_the_sixty_nine_candidates_is_individually_coherent` and five rows of
     `...The_exact_bounds_are_recorded_in_full_and_every_overflow_is_unavailable_never_a_truncated_list` ((64,0,4), (62,2,2), (64,0,5), (60,0,5), (61,3,2))
     (`controls/control-1.result.txt`). Baseline hash `0e571704...` equal before and after.
  2. Remove the ended selection owner scoping of `useLocalExecutionQueueInspection.clearSelection` (the owner-active check and the serial scoping together):
     typechecked; **1 of 19 failed**: `lets a retained clear of an earlier selection do nothing to a newer one, and the current clear end exactly its own`
     (`controls/control-2.result.txt`). Baseline hash `0b1a9bec...` equal before and after. The informational single-layer variant `control-2b`
     (only the owner-active check removed) **survived (19 of 19 passed)**: an equivalent mutant, because the serial comparison still protects a replacement
     selection within the same visit (the shared visit-owned commit protects only an ended visit; see Correction 1). It is reported, not counted as a kill.
- **Retained at their actual scopes (not fresh final runs):** Domain 1,757; Persistence plus transaction classes 750; full Application 7,905 and full Api 3,265
  (LOCAL-QUEUE-01 sources, before LOCAL-QUEUE-INSPECT-01); the last LOCAL-QUEUE-INSPECT-01 focused Application 354/Api 96, Architecture 192 and Vitest 7,454 at
  their own published sources. This slice changed no Domain, schema, migration, scheduler, ownership/capacity gate or shared lifetime primitive, so no
  broadening trigger applied; the existing ADR-0061/0063 suites that share the extracted projection were re-run fresh (the filter above).
- **Not run:** full Application/Api/Infrastructure suites, Domain, Persistence, dependency audit, real provider or native experiment, CI, security and release
  gates. Nothing was committed, pushed, staged or published.


### Preserved development attempts and failures

- Extraction `01-extraction-first-attempt-FAILED-ctor-shape-157-of-157.log`: the first shared-projection attempt joined the entities through a constructor-built
  record; EF could not bind `EF.Property` over a constructor member, so **all 157** existing ADR-0063 tests failed at translation. The join shape became an
  initializer class (`LocalExecutionAdmissionHistoryJoin`) and `02-extraction-regression-admission-history.log` shows the unchanged per-run suite passing
  157 of 157 on it. Per-run semantics were proved, not assumed.
- Bound tests `05-bound-tests-first-run.log`: 11 of 15 failed on **my fixture ordering**, not the product: bulk clones took admission and coordination
  numbers above the production counters (a later production queue then collided and was refused as `prior_mutation`), and clones are recorded owners that
  took the capacity a later production reservation needed (a null reservation). The fixture now creates every production template first and clones after;
  `06-bound-tests-second-run.log` passes 15 of 15.
- Damage tests `07-damage-tests-first-run.log`: 42 of 43 passed; one case (`run_project_differs_from_the_sequence_project`) hit the unique
  `(ProjectId, ExecutionNumber)` index in my SQL and now also sets an unused execution number; `08-damage-tests-second-run.log` passes the 40 damage cases.
- Architecture `10-architecture-first-run-FAILED-contrived-regex.log`: 1 of 196 failed on a contrived multi-line regex of my own boundary test (it
  matched the statement's own legitimate `join run in ...`); removed, `11-architecture-second-run.log` 196 of 196.
- Frontend `14-contract-and-hook-tests-first-run.log`: 2 of 30 failed on my assertions (the generated model keeps an explicit null, so the claimed
  undefined was wrong; a decoded entry is a different object from the one decoded in the test). Fixed in the tests only. `16-region-tests-first-run.log`
  was truncated by me to its first 3000 bytes to drop a huge DOM dump (a mistake against the preserve-output rule, disclosed in the file): the one failure
  was `getByText(/not ended/)` matching two rows.
- Journey `19-queue-journey-development-run-1.log` failed once: my new "no write" assertion also counted the two SignalR `/hubs/run` negotiate POSTs that
  a project switch legitimately opens; it now filters to `/api/` requests like the journey's other assertions. `20-queue-journey-development-run-2.log`
  passed. The `21-final-build-first-attempt-FAILED-msbuild-one-project.log` is a mistyped multi-project `dotnet build` (MSB1008), rerun per project.


### Challenges, limits and risks for review

- **No challenge was needed.** EF Core 10 translated the ADR's permitted shape on SQLite: a 69-row candidate subquery, correlated per-candidate `Take`s
  (as `ROW_NUMBER() ... <= n` windows, never APPLY), and left joins of every reference, in one statement. The race tests prove one SELECT naming every
  joined table, a competing change to each joined fact (including the candidate's own state and the project name) committed just before it read whole,
  and a write committed while it runs never reaches the answer.
- **Work is bounded in result, not in scan.** The candidate predicate is a `CASE` over the stored state (the only way to keep unknown, null-like and
  look-alike states as candidates), so SQLite cannot use `ix_local_execution_admissions_state`; it walks the admission-number index until 69 candidates
  or the end. Admissions are retained for the run lifetime, so with a long history of Consumed rows an explicit read costs a scan of that history even
  when the answer is Empty. This is acceptable for a local, explicit, owner-triggered read and is not hidden by a schema change (none is selected); a
  partial index would be a separate, schema-bearing decision. The candidate subquery appears three times in the statement (membership and the two
  `IN` restrictions); `OrderBy(AdmissionNumber).ThenBy(Id)` makes it a total order, so all three read the identical set from the one snapshot.
- **A candidate whose own sequence row is missing, a missing run, a missing project, a blank, oversized or NUL-padded project name, a second sequence of
  the run, an Immediate sequence with an outstanding admission and a candidate that is not the proved tail are all `Unavailable`**; 40 cases prove each
  (Application damage class), plus the unchanged ADR-0063 proof cases through representative reference damage. An unknown state and its look-alikes
  (`consumed`, `Retired ` with a trailing space, `Consumed` plus a NUL, an integer, a blob, an empty text) are `Unavailable`, never absence. A SQL NULL
  state cannot be stored (the column is NOT NULL, which `ignore_check_constraints` does not relax), so it is covered by the classification expression
  but not by a stored-value test. A project name stored as a non-text value is rendered as its text; it is descriptive metadata, as the ADR says.
- **The second control needed the whole selection scoping removed.** In `useLocalExecutionQueueInspection.clearSelection` the serial comparison protects a
  replacement selection within the same visit, and the shared visit-owned `useOwnedState` commit protects an ended visit; that commit alone does **not**
  protect a newer selection inside a still-active visit. Removing only the owner-active check (informational `control-2b`) therefore survives because the
  serial comparison remains, an equivalent mutant I report rather than count; the compiling control removes the whole selection scoping and is killed by
  the named hook test. (My first wording said the shared commit independently protected a newer selection; that was imprecise and is corrected here and
  in Correction 1.) The shared primitives were not touched.
- **The projection extraction needs an expression binder.** EF cannot zip two projections, so the global statement binds the shared
  `LocalExecutionAdmissionHistoryProjection.RowOf` expression, the candidate and the project label into one row with a small
  `ParameterBinding` expression visitor in `LocalExecutionQueueInspectionProjection`. It is internal to the operation, adds no generic query framework or
  persistence port, and the architecture test pins the reuse.
- **Fixtures are test-only storage writes.** The 64/68/69 counts use one production-created template per state cloned by SQL through an unpooled
  connection that keeps foreign keys, checks and every unique index on and drops only the seven guard triggers a clone (which has no plan message of its
  own) would trip; they are labeled in code. Production-created cycles prove every representative state (Waiting, Reserved, ended Unresolved, Consumed
  and Retired exclusions, expired deadline, changed waiting reason, multi-project order, longer ledgers).
- **A selected inspector and the overview are independent snapshots**: the overview can still list a cycle whose inspector now shows a later state until
  the owner reloads; the region says so, and the journey demonstrates it (7b).
- Existing tests edited: none. Existing production files edited: `LocalExecutionAdmissionHistoryStatement.cs` (projection extracted),
  `GetLocalExecutionAdmissionHistoryEndpoint.cs` (two mappers `internal`), `localExecutionAdmissionHistory.ts` (two decoders exported), `App.tsx`,
  `clients.ts`, `cockpit.css` and the generated client.

### Correction 1 (Codex review 1 NO-GO: LQO-R1 P3)

Preflight (verified before editing): `main`, `HEAD` and local `origin/main` both `1e58dda4b1f4a0b3292c56c4cc86fb0f15c49d66`, index empty, 55 pending
paths (15 modified, 40 untracked files). Codex's review edits to `planner-handoff.md`, `decisions/README.md` and `mvp-delivery-plan.md` are preserved
and were not touched; the original selection hashes describe the earlier dispatch.

- **LQO-R1 (P3): one top-level type per file. Fixed.** `ClonedCycle` moved unchanged (same namespace, `internal sealed record`, same members and XML
  comment) from `LocalExecutionQueueInspectionClones.cs` into the new `ClonedCycle.cs` in the same directory. The fixture class, every test and all
  production code are unchanged; no behavior was touched. A lexical scan of all 30 changed or new C# files (the new file included) finds no file with more
  than one column-0 top-level declaration (negative-controlled on a two-type sample), and each of the two affected files now has one.
- **Fresh correction evidence (`logs/executor/local-queue-overview-01/correction1-*.log`):** incremental rebuild of Application.Tests (0 warnings, 0
  errors); `LocalExecutionQueueInspectionBoundTests` **15 passed** of 15; full Architecture **196 passed** of 196 (the project was rebuilt for the run);
  `dotnet format --verify-no-changes` clean for `ClonedCycle.cs` and `LocalExecutionQueueInspectionClones.cs`; the type scan above; `git diff --check` and
  the changed-document link/fragment check (0 broken) are recorded in this return's report.
- **Retained at their actual scopes, not rerun:** the first-return final matrix (Application 442, Api 116, Architecture 196 before this split, Vitest 7,527,
  lint, build, strict e2e `tsc`, client reproducibility and the four serial journeys), both compiling controls and their hashes, and the manifest of every
  non-document source file at that matrix. This split changed one test file layout only, so no new regression, mutation, separate generated-client
  reproducibility run, full backend or frontend matrix or journey was required or run. The post-matrix manifest is superseded for the two C# files by
  `correction1-05-hashes.txt`; every other manifest entry is unchanged by this correction. **Planner review clarification:** the Architecture build
  did automatically execute NSwag, as `correction1-03-architecture.log` shows; the generated client's complete SHA-256 still equals the prior manifest.
  This was not a separate reproducibility check, and no client content changed.
- **Precise guard explanation (control 2 accepted; 2b is not a kill).** The selection serial comparison in `clearSelection` protects a replacement
  selection within the same visit; the shared visit-owned commit protects an ended visit. That commit alone does not protect a newer selection inside a
  still-active visit. Control 2 removes the whole scoping and is killed by its named same-visit regression; the informational `control-2b` survivor
  (owner-active check removed, serial comparison kept) is an equivalent mutant and is not counted as a kill.
- **Evidence-loss qualification.** `16-region-tests-first-run.log` contains only a fragment of the original output and an executor note; the omitted
  original output is lost. Its original full-run counts and complete diagnosis remain executor-reported (the fragment supports the multiple-match
  error). Current passing runs do not restore it, and the file is preserved unchanged.
- **Scale qualification.** The overview bounds materialized results (69 candidates, 103 rows per ledger, 7,177 rows overall), not the scan of retained
  admission history nor SQLite execution time. No schema, index or performance work is selected, and nothing here is a latency qualification.
- Every earlier failure, warning and evidence qualification stays as recorded (the 157-test extraction failure, the bound, damage, architecture,
  frontend and journey development failures, the first mistyped build, the existing duplicate-React-key browser console errors and the retained
  scopes above). The resolved ADR and scope were not edited or broadened.
- **Not run:** the full Application, Api, Infrastructure, Domain and Persistence suites, whole Vitest, lint, `npm run build`, the strict e2e `tsc`, client
  reproducibility check, the journeys and any control (not required for this unchanged-behavior move), dependency audit, real provider or native experiment, CI,
  security and release gates. Nothing was committed, pushed, staged or published.

## Previous slice (published; historical record): LOCAL-QUEUE-INSPECT-01 recorded queue-admission inspection (ADR-0063)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**Delivery phase (historical): first return by a new Claude executor chat, unstaged, uncommitted and unpushed at that time; the slice is since
published (1e58dda) and independently verified. Consult Git.**
Baseline: `main`, `HEAD`, local `origin/main` and the live `refs/heads/main` were `8b9a824f0a68317432794afed80ab12250b16721` (LOCAL-QUEUE-01,
independently verified published), divergence 0/0, real index empty; exactly the three planner documents (`planner-handoff.md`,
`decisions/README.md`, `mvp-delivery-plan.md`) and the new ADR-0063 were pending and are preserved byte-for-byte, not edited. LOCAL-QUEUE-01
below is a historical record and was not reopened. ADR-0050 stays Proposed, P9 unproven, AI Memory deferred, Increments 4/5/6 incomplete;
every standing restriction stays.

### What exists

- **Application.** `GetLocalExecutionAdmissionHistoryQuery` over one statement: `LocalExecutionAdmissionHistoryStatement` is one provider-neutral EF
  query (the run, at most two candidate sequences, at most 103 admission rows, left joins to each linked step, source checkpoint, report message and
  pinned recipe and to the sequence's workspace and initial checkpoint, and bounded 33/103 counts of the pinned recipes and recorded steps; enums
  classified and numbers/text guarded in the database as ADR-0061 does; admission order numbers and revisions are guarded positive Int64) and
  `LocalExecutionAdmissionHistoryProof` is the pure proof over its rows (root, mode, recipe-dependent bound, order, ordinals, closed kind shape,
  predecessor chain, correction origin, source and report references, the whole admission/step tuple, the per-state link/reason shape, Consumed-only
  before the last and the last fitting the recorded sequence state, every recorded step linked by exactly one admission). Closed statuses
  NotRecorded / NotQueued / Recorded / Unavailable; an unknown run is the existing fixed inspection 404. No split read, second revision read, write
  lock, transaction, retry, live eligibility/ownership/`SourceHolds` reader or latest-row substitution. `LocalExecutionInspectionProjection`'s guard
  constants became `internal` (no behavior change); nothing else shared changed.
- **API.** One authorized bodyless, queryless MVC GET `/api/runs/{runId}/local-execution-sequence/inspection/admissions` reusing the existing
  `LocalExecutionInspectionBodylessGuardFilter`, with an API-owned closed graph (admission order, revisions and the sequence revision as canonical
  Int64 strings, UTC round-trip times, no reserved operation, token, path or digest member). Regenerated client (additive, 278 lines, build-stable).
- **Frontend.** Strict whole-answer decoder `localExecutionAdmissionHistory.ts`, the `LocalExecutionAdmissionHistoryWireClient` facade,
  `useLocalExecutionAdmissionHistory` (run and ledger-visit lifetimes; a retained show/close/reload/linked-step callback of an ended lifetime does
  nothing) and `LocalExecutionAdmissionPanel`, a collapsed "Inspect queue admissions" region offered inside the existing inspector only for a recorded
  queued sequence, so the current run's Usage & Evidence and the selected run history both get it. A linked row's "Open step evidence" delegates to
  the inspector's own `selectStep` and exists only for a step in the inspector's currently accepted index of the same sequence; otherwise the row asks
  for an explicit index reload. Waiting and retired-without-step rows open no operation. Reloading the inspector index ends the ledger visit (the
  region unmounts), so the owner reopens the ledger afterwards.
- **Journey.** `e2e/local-execution-queue.journey.ts` now opens the ledger of the held WAITING cycle (one Waiting admission, no step, recorded
  reason, no link, zero invocations) and, after the release, the consumed twelve-admission ledger from the current run and from Run history with
  explicit linked step evidence (a verification and a review), asserting the exact GETs and no non-GET request or database change.
- **Documentation.** `agent-collaboration-evidence-inspection.md` (new "Recorded queue admission inspection"), `run-cockpit-specification.md`
  ("Inspect queue admissions"), `engineering-context.md` and this file.

### Evidence (logs under `logs/executor/local-queue-inspect-01/`, git-ignored; commands run at the repository root)

- **Fresh, final matrix on the final restored sources (after both controls, hashes restored, touched, rebuilt):**
  - Application, `--filter 'FullyQualifiedName~LocalExecutionAdmissionHistory|FullyQualifiedName~LocalExecutionInspection'`: **354 passed**, 0 failed
    (157 new: `LocalExecutionAdmissionHistoryTests`, `...DamageTests`, `...ConsistencyTests`; 197 existing LocalExecutionInspection), 2 m 15 s
    (`final-application.log`). Real file-backed SQLite, production-created queued cycles for every admission state, both correction origins and recipe
    order, all tuple members, malformed storage, row bounds, one-statement/read-only/no-forbidden-column proofs and independent-connection consistency.
  - Api, same filter: **96 passed** (19 new `LocalExecutionAdmissionHistory` Api tests, 77 existing LocalExecutionInspection), 1 m 8 s (`final-api.log`).
  - Architecture, full project: **192/192** (`final-architecture.log`); `LocalExecutionInspectionBoundaryTests` now includes the new Application and Api
    folders (the DbSet allow-list gains `LocalExecutionAdmissions`; four endpoints).
  - Frontend: whole Vitest **7,454 passed** (301 files; +55 new: 32 decoder/hook, 14 panel, 4 two-surface, 5 contract); `oxlint` exit 0 with only the
    existing warnings (none in the new files); `npm run build` (includes `tsc -b`) pass; the recorded strict e2e `tsc` flag set pass.
  - Generated client: a non-incremental serial Api build (`-p:UseSharedCompilation=false -m:1`, 0 warnings, 0 errors) reproduced `api-client.ts`
    byte-identically (hash `ed2855a042d5354c` before and after; the LOCAL-QUEUE-01 hash was `ba3b20f58e4aa132`).
  - Journeys, run one at a time on the final binaries: queue **48 s passed**, existing local-execution **30 s passed**.
  - Changed C# files: `dotnet format --verify-no-changes` clean (30 files); `git diff --check` clean; new files have no CR, trailing space or stray
    non-ASCII (only the existing `·` and `…`).
- **Compiling controls (`control-1.sh`, `control-2.sh`; each hash-guarded, mutated, proved changed, built, run, restored from a byte backup, hash
  verified, touched, rebuilt):**
  1. Omit the required admission/step source-checkpoint comparison in `LocalExecutionAdmissionHistoryProof.ProveLink`: compiled, exactly one named
     permanent test failed (`...DamageTests.A_damaged_fact_makes_the_whole_ledger_unavailable_with_no_partial_disclosure(tuple_source_checkpoint)`; 156 of
     157 passed). The first run's logs are kept as `first-attempt-control-1*.log`: there a second, unrelated failure was my own test defect (a record
     comparison over a list), fixed in the test and not counted as a kill.
  2. Remove the ended request-owner guard of `useLocalExecutionAdmissionHistory.inspectLinkedStep`: typechecked, three named permanent tests failed
     (never opens evidence for another visit / another run / after unmounting; 31 of 34 passed).
- **Retained at their actual scopes (not fresh final runs):** Domain 1,757; Persistence plus transaction classes 750; full Application 7,905; full Api
  3,265 (all LOCAL-QUEUE-01 evidence on sources before this slice). Their inputs are unchanged except additive new files and the Application guard
  constants' visibility; this slice changed no Domain, schema, migration, execution policy or shared authority, so no broadening trigger applied.
- **Not run:** full Application/Api/Infrastructure suites, Domain, the planning-sequence and collaboration journeys (the shared harness is unchanged),
  dependency audit, real provider or native experiment. Nothing was committed, pushed, staged or published.

### Preserved development attempts and failures

- Application `app-first.log` 3 failed/25 passed (a tick helper that never exposes a waiting successor; a restart test sharing one database across
  global reservation and closure) and `app-second.log` 4 failed (the production database is WAL, not rollback-journal, so a mid-statement writer is
  not blocked and the test now asserts snapshot isolation instead; three damage fixtures hit unique indexes or reused one constant GUID); `app-third.log`
  1 failed (the in-flight write was refused by the admission immutability trigger; it now writes an unguarded table). All were test defects, fixed in tests only.
- Api `api-first.log` 15 failed (the storage-damaging test shared the class database; it now owns its factory), `api-second.log`/`api-third.log` 1 failed each
  (the shared Problem Details has no per-error message; `instance` and `traceId` echo the route, which the existing tests also normalize).
- Journey `journey-queue-1.log` failed once on my wrong assumption that step 3's verification passes; the history leg opens the linked review instead.

### Challenges, limits and risks for review

- **No challenge was needed for the one-statement requirement.** EF Core translated the guarded left-join projection on SQLite, including the bounded
  correlated counts and nested row records, with no APPLY. The race tests prove one SELECT naming every joined table, a competing change to each joined
  fact committed just before it read whole (Unavailable or the new state), and a write committed while the statement runs does not reach the answer.
- The lifecycle matrix (sequence state vs last admission: waiting-waiting, preparing-reserved, awaiting/stopping-consumed, approval/escalation-concluded
  consumed, stopped/attention/interrupted-consumed/retired/unresolved, never an ended sequence ending Waiting or Reserved, queued never Armed) was derived
  from the production handlers and holds for every production-driven scenario (ten whole cycles, stop, expiry, deadline, restart, unresolved). A legitimate
  shape no scenario can reach would read as Unavailable rather than as a partial list; the time-ordering rules (enqueued <= waiting-recorded <= updated)
  share the existing monotonic-clock assumption of the index proof.
- The pinned recipe set is proved by count and for every recipe an admission uses (nonblank name <= 200 units, identity); the full-set properties the
  ADR-0061 index checks (ascending numbers, enabled flag) are not repeated because the ledger needs only membership and name.
- The ledger visit ends when the inspector's index reloads (the region unmounts), a deliberate reading of "unmount ... ends the prior request owner".
- Existing tests edited: the Api inspection route inventory (three to four routes) and the Architecture boundary test (new folders, DbSet, endpoint count).

## Previous slice (published; historical record): LOCAL-QUEUE-01 queued local execution (ADR-0062)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**Delivery phase (historical): correction 1 returned by the Claude executor after review 1 NO-GO, unstaged, uncommitted and unpushed at that time; the
slice is since published (8b9a824) and independently verified. Consult Git.**
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
- **LOCAL-RELEASE-INSPECT-01 (correction 2 returned, unreviewed).** Its two compiling controls prove one decoder admission and the combined
  reload defense of the hook; the single `isActive` layer survived as a masked control (the frame-level visit check is independent). The
  journey proves only the Recorded receipt through both surfaces and a page reload; a browser reload is not a host restart and its
  history run is the only run of its project. The receipt is unsigned relational consistency from one statement; its index, operation and live
  status are separate reads. Corrections 1 and 2 (LRI-R1, LRI-V1) strengthened the shared projection's numeric admission (three sibling tick members beyond the
  intent, then a raw-storage validity of every nullable numeric fact refused before eligibility or replay) and the journey oracle; the browser evidence is the correction-1 run, whose host binary predates the Correction 2 product change (no browser run was repeated); the journey fingerprint is selected fields and counts, not all-row byte identity. Retained Domain, Infrastructure, whole Application/Api/Vitest and other journey evidence keeps its earlier scope.

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
