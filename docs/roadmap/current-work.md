# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Race-safe first-use initialization of local-commit control storage (2026-10-09)

- Status at executor return (historical after the Codex review below): implemented and validated as an UNSTAGED, UNCOMMITTED and UNPUSHED working-tree diff awaiting Codex review (GO/NO-GO); no staging, commit, push or next slice is granted or taken. One production method changed (`LocalCommitStorage.TryEnsureHooksDirectoryEmpty`) plus tests; no Domain/Application behavior, public port, endpoint, schema, migration, generated client, frontend, package, Git argument, native/ref/index/lock protocol, reservation, artifact layout, cleanup, recovery, janitor or provider-policy change, no retry, sleep, cached-success bypass or serialization, and no new ADR (ADR-0029/0030/0031 unchanged). Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `34f56b918a7eb6812282ed095d6ef52f37462754`; empty index; only `docs/roadmap/planner-handoff.md` modified; nothing untracked; planner record SHA-256 `0bb02cf34fc9e702b59a1c39df487b2a4eac28443ca22f7a937039bb594671fa` and generated client SHA-256 `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64` as announced; both are unchanged at this return. The selected contract is in the [planner record](planner-handoff.md).
- Production: initialization now creates the root and hooks directory, then performs ONE `new FileStream(EmptyConfigPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)` and accepts the configuration only if that opened handle is seekable with `Length == 0`, then requires the hooks directory to hold no entry. The previous `File.Exists` followed by `File.WriteAllBytes` (which truncated and held a `FileShare.Read` write handle, so a second first-user hit a sharing violation that the `IOException` catch turned into `false`, surfaced by the preparer as `HooksDirectoryNotEmpty`) is gone. `OpenOrCreate` never truncates or rewrites; a nonempty file, a directory or other unopenable object at the path, an exclusive or otherwise inaccessible file, any hooks entry, and any I/O or access failure still return `false` and preserve the occupied bytes and entries. An existing empty read-only-attribute file remains accepted because the open requests only read access. Every call revalidates both the configuration and the hooks directory (no cached state), and the same `IOException`/`UnauthorizedAccessException` catch and the `bool` signature and all callers (preparation, inspection, both index-effect acquisitions) are unchanged. Added `internal Action<string>? InitializationObserver` (phases `directories_ready`, `configuration_opening`, `configuration_opened`, the last while the handle is held and before the length is read), the same kind of internal, authority-free observation seam as `LocalCommitGit.RefTransactionObserver`; it is invisible to the Api test assembly and production never sets it.
- Failing first: before changing the algorithm I added only the observer to the OLD code (same check-then-write behavior) and wrote 20 tests (`LocalCommitStorageInitializationTests` 16 facts, `LocalCommitInvalidControlStateTests` 2 theories x 2 states, ie 4 cases). Run against the old algorithm: 8 failed, 12 passed (excerpts in `failfirst.txt`, outside the repository). The 8 failures were the released-together creation boundary (`round 0: False,True`), the creator that still holds the new file open (`Assert.True() Failure`), a nonempty existing configuration accepted, a nonempty configuration accepted after earlier success, a configuration that became nonempty while held, an exclusively held configuration assumed usable (`Assert.False() Failure`) and the two `configuration` cases of the invalid-state adapter tests (`Assert.Equal() Failure: Values differ`, expected `HooksDirectoryNotEmpty`, actual `OwnershipNotProven`: the old code accepted the nonempty configuration and went on to the Git-based ownership proof, which the recording adapter refused). The 12 that already passed on old code are guards for behavior that must not regress (directory at the path, file at the hooks or root path, any hooks entry, hook insertion, existing empty file, deletion and recreation, fresh creation, a second initializer that arrives after creation). After the algorithm change one of my own tests failed because it read the file while its simulated creator still held a write handle (a test defect, not production); I fixed the test to close the creator first and no assertion on the product was changed.
- Controlled interleavings (real files, independent `LocalCommitStorage` instances over one root, bounded 30 s waits, holds released by disposal even when an assertion fails): (1) an initializer parked after deciding to create, while another creator holds the new file open with the previous write flags (`FileMode.Create`, write, `FileShare.Read`), succeeds; (2) an initializer parked while holding the opened file does not fail a second complete initializer, and itself succeeds; (3) an initializer parked at the creation boundary while another instance creates the file keeps the empty file and succeeds; (4) the content a competitor writes while an initializer holds the handle is refused, never trusted, with the competitor's bytes preserved; (5) 40 fresh roots in which two instances are released together at the creation boundary all return true. (5) is a bounded supplement, not the proof; (1)-(4) are deterministic. Plus fresh creation; an existing empty file (including read-only) accepted with bytes, timestamp and attribute unchanged; nonempty file refused with bytes and timestamp unchanged; directory at the path left in place; exclusive holder refused then accepted once released; any hooks file or directory refused, unchanged, and accepted again after removal (the refusal is not remembered); a file where the hooks directory or root belongs refused unchanged; after success, a nonempty configuration, a directory in place of the file or a hook inserted later is refused on the next call by the same and by an independent instance, and a deleted file is recreated empty.
- Git is never invoked on invalid state (`LocalCommitInvalidControlStateTests`): the adapter is composed over a process adapter that records and refuses every request. With a nonempty configuration or a hook (a script that would write a marker file), `PrepareAsync` returns `HooksDirectoryNotEmpty` with zero process requests, the sentinel bytes unchanged, the marker absent and no scratch or artifact leaf. After a real successful preparation, the same corruption makes `ExecuteAsync` return `NotPromoted` (`local_commit.index_acquisition_unproven`), `InspectAsync` return `Unprovable` and `AcquirePendingIndexEffectsAsync` return null, again with zero process requests, the sentinel unchanged and unexecuted, the branch at the baseline, the index bytes unchanged, no index lock and the prepared artifact intact.
- Api competition fixture: `StartCompetitionAsync` no longer calls the production initializer; after the host starts and the lineage is seeded and before either request is sent it asserts that the owned storage root does not exist, so the two preparations are the first use. Both actual `Prepared` results, captured identities and the exact admission, real cleanup, inert-sibling bytes, execution and restart assertions in both orders, the induced-refusal controls and the failure-safe gate release are untouched. The fixture is real in-process concurrency (the host shares one storage instance); the controlled interleavings are in the Infrastructure tests because the Api assembly cannot see the seam. Evidence that the cold fixture detects the defect: with the old algorithm restored as a mutant, one run of the two competition classes (9 tests) failed 2 (`A_restart_recovers_the_successful_preparation_and_the_refused_one_never_owned_an_artifact(refusedReleasedFirst: False)` and `A_restart_after_the_competition_recovers_the_admitted_operation_and_removes_only_its_recorded_artifact(admitFirstCaptured: False)`; the second reported `both real preparations must be Prepared: #1:HooksDirectoryNotEmpty, #2:Prepared`); this is one probabilistic sample, not a rate.
- Mutations (rebuilt non-incrementally and non-shared; baseline hash `9191fdde4072801828ea16c38a70decd994f0866d4f092d3819aa8fac9719f03` checked before and after every mutant and restored from a byte copy; a failed build is never counted; `mutate.sh`, outside the repository): 14 of 15 killed, the target byte-identical at the end. Killed: destructive `FileMode.Create` with write access (7 failed), accepting any length (5), `FileShare.None` (4), `FileShare.Read` (2), the old check-then-write algorithm (9), `FileMode.Open` without creation (14), read-write access (3, including the read-only file), skipped hooks validation (4), cached overall success (9), cached configuration validation (5), cached hooks validation (4), no hooks-directory creation (15), swallowing a failure as success (4) and `Length >= 0` (5). Surviving M05 (`FileShare.ReadWrite` without `FileShare.Delete`): no test or product path deletes or renames the configuration while an initializer holds it open for those microseconds, so it is behaviorally equivalent for this slice; I kept `Delete` sharing to match the other read-handle sharing in this feature and did not add a test that would only assert incidental Windows delete semantics.
- Validation of the final tree (`final/`, outside the repository; sequential script, build servers shut down first, explicit exit check after every step and stop at the first failure; every exit was 0): `dotnet build DevalCopilot.slnx --no-restore --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; full Infrastructure `--no-build --no-restore` 1669 passed, 0 failed, 4 skipped (all four are pre-existing host-capability skips for file symbolic links that this host cannot create without elevation: `FilesystemArtifactStoreTests`, `UntrackedFilePreviewReaderTests`, `PackageEntrypointResolverTests`, `GitWorkspaceInstructionContextTests`; none touches this slice); full Api 1402 passed, 0 failed, 0 skipped (the historical 1378/1 result stays recorded for its own run and is not relabelled); full Architecture 47/47. Affected checks ran first, before the matrix and on the same test code: 40 Infrastructure tests (the executor-reported affected selection) and 117 Api tests (competition, refusal, restart, gate, scripted-fixture, safe-release and seam classes), all passed. `dotnet format DevalCopilot.slnx --verify-no-changes --include` over the four changed C# files: exit 0. The generated client is byte-identical, `git diff --check` is clean, and each changed or new C# file has no CR, NUL, non-ASCII, trailing whitespace or missing final newline (byte counts) and its nested helper types leave exactly one top-level type per file.
- Fresh versus retained: fresh in this slice are the failing-first run, the affected runs, the mutation matrix and its Api mutant run, the build, the full Infrastructure, Api and Architecture suites, the formatter and the hygiene checks. Retained from the published delivery with their recorded scope and not rerun: the Domain and Application suites (no change in those layers), the frontend Vitest, typecheck, lint, build, harness and Chromium/journey run, the NuGet and `npm audit` results and the repository formatter baseline. No new browser suite and no real provider were run or needed.
- Codex review (2026-10-09): GO for the frozen six-path snapshot, including the planner review record and two factual ledger corrections (empty read-only files are accepted; the executor-reported 40-test selection is not assigned an unverified class composition). The selection hash above is historical after the planner review edit. Independent checks, each once and sequentially: solution build `--no-restore -p:UseSharedCompilation=false -m:1` (0 warnings, 0 errors); Infrastructure filter `LocalCommitStorageInitializationTests|LocalCommitInvalidControlStateTests|LocalCommitGitPreparationTests|LocalCommitStorageOwnershipTests` (57 passed, 0 failed, 0 skipped); Api filter `LocalCommitCompetingPreparationTests|LocalCommitRefusedPreparationTests|LocalCommitRestartRecoveryTests` (41 passed, 0 failed, 0 skipped); full Architecture (47 passed, 0 failed, 0 skipped), tests with `--no-build --no-restore`. Codex inspected the executor final full-suite and mutation logs; those remain executor evidence, not independent full-suite reruns. Source, tests, client and ADRs are unchanged by review; historical failures remain preserved. Publication and factual closure are still pending.
- Inventory: 6 paths: 4 modified (`docs/roadmap/planner-handoff.md` as supplied and byte-unchanged, this file, `src/backend/DevalCopilot.Infrastructure/Features/Runs/LocalCommitStorage.cs`, `tests/DevalCopilot.Api.IntegrationTests/Features/Runs/LocalCommit/LocalCommitTestBase.Support.cs`) and 2 new (`tests/DevalCopilot.Infrastructure.IntegrationTests/Features/Runs/LocalCommitStorageInitializationTests.cs`, `LocalCommitInvalidControlStateTests.cs` in the same folder).
- Remaining limits: this removes the demonstrated first-use contention only; the control files are still not frozen against hostile replacement after the final check, a refusal still maps to the coarse `HooksDirectoryNotEmpty` name (unchanged), the interleavings cover this process's handle semantics on this Windows host rather than every platform, the 40-round stress and the Api fixture are samples of a probabilistic race, the historical failed Api run (1378 passed, 1 failed) still has an unproven cause and the predecessor entry below keeps its initialized-storage limitation as historical evidence (this entry resolves that limitation in code, it does not retroactively change that result), the inert artifact of an unadmitted preparation still stays with no janitor, and no increment or MVP completion is claimed and no next slice is selected.

## Outcome-aware proofs of competing local-commit preparations (2026-10-09)

- Status (historical until the publication bullets at the end of this entry, which record the delivered state): implemented and validated as an UNSTAGED, UNCOMMITTED and UNPUSHED working-tree diff awaiting Codex review (GO/NO-GO); no staging, commit, push or next slice is granted or taken. Test support and tests only: no production code, port, migration, API, client, frontend, dependency, Git, native, lock, artifact-layout, authorization, reservation, cleanup-policy or recovery change, and no new ADR. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `c12c7dd5b9c53064be66bd998d966659fdbd9822`; empty index; only `docs/roadmap/planner-handoff.md` modified; nothing untracked; planner record SHA-256 `a2bff9e4b1d88d148027308deeb3103eeb291c361991db23b0809673753bc913` and generated client `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64` as announced (both hashes are historical: the planner record `a2bff9e4…` is the pre-review state, and the frozen publication manifest carries its reviewed bytes, `6b4155c178d81f102a85692d7bc9c558ae13daf35ccb5289bce2bc19398faaea`). Both were unchanged at the executor return, as were ADR-0029/0030/0031; the subsequent planner-owned review edit is recorded in planner-handoff.md.
- Corrected test contract (see the [planner selection](planner-handoff.md)): the competing-preparation proofs no longer assume that the preparation they park was `Prepared`. `ScriptedLocalCommitPreparer` captures every result the real preparer returns, before any callback or signal, as a `LocalCommitPreparationCompletion` (capture sequence, the request the real preparer saw, the exact result); `IsPrepared` means `Prepared` AND facts, and only such a completion supplies an artifact path, so a refusal, failure or self-contradicting result supplies no artifact authority. `AfterPrepare` still runs for every returned outcome but now receives the completion (four call sites changed mechanically to `_ =>`). Method entry signals nothing; a call that throws is counted in `Faulted` and is never a completion. `WhenPreparedAsync(n)` and `WhenCompletedAsync(n)` complete only from captured results. A test-owned `PreparationGate` parks each completion until the test releases that exact capture sequence (bounded at 60 s; `ReleaseAll` is the failure-safe release and also frees later parks). `ScriptedLocalCommitRepository` now reports each real cleanup (`CleanupReports`, `CleanupReturned`) only after it returned or ended, with its own result (null when it threw or was cancelled), operation and artifact. `LocalCommitScene.ArtifactLeaves()` lists only the operations folder, so artifact accounting never includes transient observation scratch.
- Successful competition (real preparer, real Git, real host, supervisor on; each case in both admission orders): both real preparations run concurrently and are asserted `Prepared` with distinct artifact paths and both files on disk, and the artifact leaves equal exactly those two captured paths, before any request is released. The test then releases one captured result (first-captured or second-captured, never method entry, UUID order or a timer); only that request answers (200, the operation), the other stays parked. The admitted row is that result's own (`PreparedIndexRelativePath`, `PreparedIndexSha256`, `IndexPreimageSha256`, `TreeSha`, `CommitSha`; parent the baseline, branch tip, tree and `git diff --cached` of the workspace equal, no index lock), one operation and one admission event, run `Completed`, workspace `Ready`, source checkout fingerprint unchanged. Cleanup is observed through its own signal (the durable `Completed` precedes it): it addressed the admitted operation and its recorded artifact, returned success, removed exactly that leaf, and left the other successful artifact unchanged (names and SHA-256, and the artifact set equals that one leaf). The late parked request is then refused `409 local_commit.run_not_eligible` and neither admits anything nor touches either artifact; exactly one cleanup ever ran.
- Restart (supervisor off, both orders): the same two-success setup; the admitted operation stays `Prepared` with its own recorded artifact, the other request is the existing replay of that operation (200), nothing else is admitted and neither artifact changes; after a restart recovery reconciles it to `Interrupted` (`local_commit.interrupted_before_execution`) at the baseline tip with the workspace `Ready`, its own real cleanup (observed) addressed the recorded artifact only, and the other successful artifact remains inert and byte-identical.
- Refusal controls (`LocalCommitRefusedPreparationTests`): the refusal is INDUCED by giving the selected call a checkpoint fingerprint that cannot be current, so the production preparer itself returns `CheckpointNotCurrent` with no facts and removes its own leaf; no result is faked, and this is neither real-provider evidence nor a reconstruction of the historical failure. With one refusal, in either release order: the refusal answers `409 local_commit.refused.checkpoint_not_current` and admits nothing, the successful request is admitted and executes (supervisor) or recovers (restart) exactly as above, the refused completion has no artifact, and the empty artifact set after the recorded artifact's cleanup is exact because no other successful artifact existed. With both refused: two 409s with that code, zero operations, no `local_commit.` event, run `Running`, workspace `Ready`, branch at the baseline, index bytes and source checkout unchanged, no cleanup, no artifact. The strict two-success artifact assertions were not weakened and no count was relaxed.
- Bounded regressions with blocking fakes (no host or Git): `ScriptedLocalCommitPreparerTests` (9 cases: entry and outstanding calls satisfy nothing; a refusal is a completion but never Prepared; three self-contradicting results; capture order with exact own artifacts and a boundary that needs both Prepared; an exception is `Faulted`, not a completion; capture and signal precede a parked callback; induced refusal touches only the selected request), `PreparationGateTests` (4: release only by own sequence, early release not lost, failure-safe release frees later parks, bounded park) and `ScriptedLocalCommitCleanupObserverTests` (3: entry signals nothing, `false` is not success, an exception is reported without a result and still propagates). Hosts are stopped by the fixture's existing failure-safe disposal; `LocalCommitCompetition.Dispose` only releases the gates and adopts or removes no lock, checkout or directory.
- **Finding for planner judgment (production, not fixed, not selected):** an unexpected real refusal appeared in my first complete run of the new controls: the NON-induced preparation of `LocalCommitRefusedPreparationTests.A_restart_recovers_the_successful_preparation_and_the_refused_one_never_owned_an_artifact(refusedReleasedFirst: False)` returned `HooksDirectoryNotEmpty` with no facts (preserved: `affected-1.txt`, outside the repository; it failed with `Assert.Single() ... contained 2 matching items`, the harness reporting two refusals). Mechanism, isolated without changing production code: `LocalCommitStorage.TryEnsureHooksDirectoryEmpty` does `File.Exists(EmptyConfigPath)` then `File.WriteAllBytes(...)`, and its `IOException` catch returns false, which the preparer reports as `HooksDirectoryNotEmpty`. A scratch probe outside the repository (`storage-probe`, referencing the unmodified Infrastructure project) that called it from two simultaneous threads against a fresh root saw a false result in 12 of 300 iterations, and never in a later sequential call. So two concurrent FIRST preparations on a fresh storage root can race and one is refused for a reason unrelated to the hooks directory. This is a demonstrated mechanism that can produce the observed outcome, NOT a proven cause of the historical failed run (its outcomes were not captured) and not a claim about its frequency in production. I did not repair it. The competition fixture initializes the owned storage once and sequentially with the production method before the race (`StartCompetitionAsync`), so these proofs test competition, artifact ownership and cleanup rather than first-use initialization; first-use concurrency of the storage root is therefore NOT covered by this slice. After that, eight consecutive runs of the 25 affected tests all passed (`affected-loop-1..8.txt`).
- Failing first (`failing-first-naive.txt`, outside the repository): against deliberately naive support (every result counted as Prepared; the cleanup signalled on entry) the 25 new or rewritten tests ran 14 failed and 11 passed: all five refusal controls, the preparer and cleanup-observer regressions (including the three self-contradicting cases), the capture-order, induced-refusal and entry-signal cases; the four all-success integration cases passed because they only needed the support to be correct. The correct support was then written and the same set passed except for the finding above.
- Mutations (rebuilt non-shared, every changed path hashed against a verified baseline before and after each mutant and restored from a byte copy; a failed build is never counted; `mutate.sh`, outside the repository): 10 of 10 killed, the manifest byte-identical at the end. M1 refusal treated as Prepared (11 failed: refusal controls and preparer unit cases), M1b `Prepared` outcome without facts trusted (1), M1c the Prepared boundary counting a refusal (10), M1d a refusal's facts supplying an artifact (2), M2 cleanup signalled before the real cleanup returns (3, only the cleanup-observer unit cases), M2b a failed cleanup reported successful (2, only the observer unit cases), M3a the gate releasing the wrong completion (10: competition, refusal and gate cases), M3b the second completion reusing the first result (10), M3c the cleanup report naming the wrong artifact (10), M4 the Prepared boundary satisfied by outstanding calls (16). Honest limit: the premature-cleanup-signal mutants M2 and M2b are detected only by the unit regressions, not by the real-Git integration cases, because the real cleanup is synchronous and its removal is visible as soon as the signal fires.
- Validation of the final tree (`final/`, outside the repository; sequential script that stops at the first non-zero exit; build servers shut down first): serial non-incremental `dotnet build DevalCopilot.slnx --no-restore --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; full Api `--no-build` 1402 passed, 0 failed, 0 skipped (the previously recorded full Api result, 1378 passed and 1 failed, stays recorded for its own run and is not relabelled; this result belongs to this tree and does not establish the cause of that historical failure); full Architecture 47/47. Affected checks ran first (25 new or rewritten tests, then the 156 tests of the LocalCommit namespace: all passed). `dotnet format --verify-no-changes --include` over the 16 changed C# files: exit 0 (no new finding; the repository's recorded baseline in 17 older files is untouched). The generated client is byte-identical (`22074a06…`), `git diff --check` is clean, and every changed or new C# file has no CR, NUL, non-ASCII, trailing whitespace or missing final newline and exactly one top-level type.
- Fresh versus retained: fresh in this slice are the build, full Api, full Architecture, the formatter and hygiene checks, the affected tests and the mutation matrix above. Retained from the published delivery, with their recorded scope and not rerun (test-support-only change; no new browser journey or real-provider invocation was run or needed): Domain, Application and Infrastructure suites, the frontend Vitest, typecheck, lint, build, harness and canonical Chromium/journey run, the NuGet and `npm audit` results and the repository formatter baseline.
- Inventory: 18 paths in total: 10 modified (`docs/roadmap/planner-handoff.md` as supplied and unchanged, this file, and in `tests/DevalCopilot.Api.IntegrationTests/Features/Runs/LocalCommit/` `LocalCommitAbandonmentTests`, `LocalCommitCompetingPreparationTests` (rewritten), `LocalCommitCompleteSetTests`, `LocalCommitScene`, `LocalCommitSeamTests`, `LocalCommitTestBase.Support`, `ScriptedLocalCommitPreparer`, `ScriptedLocalCommitRepository`) and 8 new (`LocalCommitPreparationCompletion`, `LocalCommitCleanupReport`, `PreparationGate`, `LocalCommitCompetition`, `LocalCommitRefusedPreparationTests`, `ScriptedLocalCommitPreparerTests`, `PreparationGateTests`, `ScriptedLocalCommitCleanupObserverTests`). The three mechanical call-site files differ by one line each; `StartScripted` gained an optional preparer decorator.
- Remaining limits: the proofs use a real preparer and real Git but an induced (owned-fixture) refusal, not a naturally occurring one; first-use concurrency of the storage root is not covered and the race above stays open for planner judgment; the inert artifact of an unadmitted preparation still stays and there is no janitor (the tests now prove it is unchanged, not that it is removed); the historical failed run's cause and event order remain unproven, and nothing here claims they were found or fixed; Windows-only physical proof is unchanged. No increment or MVP completion is claimed and no next slice is selected.
- Publication and post-publication closure (this factual closure changes only this entry of this file; it does not name its own commit). Codex's explicit GO covered the frozen 18-path reviewed manifest (ignored file `reviewed-manifest.json`, SHA-256 `6fbcafd797080e36cf743e562c9920faaae34c3f0afd2ae58dc5fb0a604c4aee`), which includes Codex's final planner and factual-ledger edits. Before staging I verified its own hash, the exact Git inventory (18 paths, 10 modified tracked and 8 untracked, none extra, missing or duplicated) and, for every entry, the Git status, raw SHA-256 and byte size, with no discrepancy; the generated client was `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64`. Preflight: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `c12c7dd5b9c53064be66bd998d966659fdbd9822`; empty index. Only those 18 paths were staged: 10 modified and 8 added with `--no-renames`, and `git diff --cached --check` was clean. The substantive commit is `ffd0be0f8b16558e13fe4e481b23217a568386cb` (`test: prove outcome-aware concurrent local commit preparations`, 18 files), whose parent is the verified baseline `c12c7dd5b9c53064be66bd998d966659fdbd9822`. `main` was pushed normally (a fast-forward from `c12c7dd` to `ffd0be0`, no amend, no force); after `git fetch`, an independent `git ls-remote origin refs/heads/main` returned `ffd0be0f8b16558e13fe4e481b23217a568386cb`, equal to `HEAD` and to the local `origin/main`, with a clean checkout.
- Fresh post-publication checks on the substantive commit, run once, sequentially, from fresh output files with an explicit exit-code check after every step and stopping at the first failure (all exits were 0; complete outputs are kept outside the repository): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; full `DevalCopilot.Api.IntegrationTests` with `--no-build --no-restore` 1402 passed, 0 failed, 0 skipped; full `DevalCopilot.Architecture.Tests` with `--no-build --no-restore` 47 passed, 0 failed, 0 skipped. The generated client is still `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64` and the checkout is clean. Nothing was rerun to obtain a result.
- Evidence scope after publication: fresh on the published commit are the three checks above. Retained from the reviewed tree, not rerun after publication, with their recorded scope: the failing-first run (14 of 25 failed on naive support), the eight consecutive passes of the 25 affected tests, the 156 LocalCommit-namespace tests, the 10-of-10 rebuilt mutation matrix (premature-cleanup-signal mutants detected only by the unit regressions), the formatter and hygiene checks and the first complete run that showed the real `HooksDirectoryNotEmpty` refusal. Retained from the previously published delivery, not rerun (test-support-only change): the Domain, Application and Infrastructure suites, the frontend Vitest, typecheck, lint, build, harness and canonical Chromium/journey run, the dependency audits and the repository formatter baseline. No browser journey or real-provider invocation was run or needed.
- Limits unchanged by this closure: every limit above still stands. The first-use initialization race in `LocalCommitStorage.TryEnsureHooksDirectoryEmpty` (concurrent first preparations on a fresh storage root can return a spurious `HooksDirectoryNotEmpty`) remains an unresolved production finding for planner judgment; the competition fixture still initializes the owned storage sequentially before the race, so first-use concurrency is not covered. The refusal controls use an induced refusal, the inert artifact of an unadmitted preparation stays with no janitor, and the cause and event order of the historical failed Api run (1378 passed, 1 failed) remain unproven; the 1402-passed result belongs to this tree and neither explains nor relabels it. This delivery does not complete any increment or the MVP and selects no next slice.

## Explicit abandonment of an inactive manual run (2026-10-08)

- Status (historical until the publication closure bullets below; they record the delivered state): first return, the R1 to R4 correction round and the R3 storage-class correction (both below) implemented and validated as an UNSTAGED, UNCOMMITTED and UNPUSHED working-tree diff awaiting Codex re-review (GO/NO-GO); no staging, commit, push or next slice is granted or taken. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `3603cea9ba0d6e11888e99d4974ce338e4fe34ad`; empty index; only `docs/roadmap/planner-handoff.md` modified; nothing untracked; planner record SHA-256 `ce42b149f7ba2085033e54b0ed8210e80136361441c1caa4537bf14510afce63` and generated-client baseline `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac` as announced. The planner record is untouched (same hash at the end); ADR-0029 (`cf9a28d4…`) and ADR-0030 are unchanged. The planner selection and ADR-0014/0029/0030 were read first.
- Delivered outcome ([ADR-0031](../decisions/0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md)): a human can explicitly end a `Created` or `Running` `ManualAgent` run as `Abandoned` through the protected `POST /api/runs/{runId}/abandon` (one `AbandonManualRunCommand`; a required normalized reason of at most 2 KiB UTF-8, trimmed, CRLF to LF, no control, format or separator character other than LF, well-formed text; an 8 KiB body proven on real Kestrel), see its reason, time and history after a restart, and record another objective through normal intake. `RunLifecycle.Abandoned = 5` is appended; the `Stage` is preserved, the active participant cleared, the accumulated time frozen (time spent `Created` is not counted), and the immutable `AbandonmentReason` and `AbandonedAtUtc` are saved with exactly one Human-authored `run.abandoned` event in one short transaction. One focused migration, `AddRunAbandonment`, adds the two nullable `TEXT` columns with no default and no backfill.
- **Correction round R1 to R4 (Codex NO-GO, same executor chat; the three Codex lifecycle guards were architecturally accepted and are unchanged).** Round preflight verified once: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` `3603cea9ba0d6e11888e99d4974ce338e4fe34ad`; empty index; 84 status paths (35 modified, 49 untracked); planner record `d3711253beb3197282f123a58277a214c37c085cd9b414fd64995adb30535f71` (Codex's NO-GO edit, preserved byte-identical, also at the end), ledger `76cfe037…`, generated client `22074a06…`. ADR-0029 and ADR-0030 are unchanged.
  - **R1 (stale tracked Run).** Under the write lock the handler now reloads exactly the Run it mutates (`dbContext.Entry(run).ReloadAsync`, the precedent used by the local-commit recorder), so the transition and EF's original concurrency values are the committed ones; the rest of the tracker is untouched. Failing first (`r1/red-stale-tracker.txt`): 8 of 16 new populated-Run cases failed on the pre-fix code (the participant of a newer committed Agent was left behind on an Abandoned row, a Created-to-Running advance was closed with the stale lifecycle and clock) and the other 8 (current copy, `None` competing) passed as controls. Green: `AbandonManualRunStaleTrackerTests` (16): the reviewer's stale `None` to Agent reproduction, Created to Running with its 90 s counted, same-lifecycle advances with five competing participants in both directions, several stage advances, a current-copy control, a stale Running run that ended (refused, nothing written) and one already abandoned (replay, one event). Each success asserts coherent closure through the shared reader and the status query, the newest stage kept, a cleared participant, the frozen clock, exactly one Human event and a successful ordinary intake.
  - **R2 (Unicode scalars).** `RunAbandonmentPolicy` classifies each Unicode scalar (`Rune.DecodeFromUtf16`, `Rune.IsControl`, `Rune.GetUnicodeCategory`) instead of skipping valid surrogate pairs, so U+E0001, U+E0020, U+E007F, U+1D173 and U+110BD are refused wherever they appear, unpaired surrogates stay refused, ordinary supplementary text (U+1F600, U+10000, U+1D11E, U+20000, U+E0100, U+10FFFD) is kept exactly, and a four-byte scalar counts four bytes against the 2 KiB bound. Failing first on the old loop: Domain 5 failed (`r1/red-supplementary-format.txt`), command 3 failed (`red-r2-application.txt`), real host 5 failed (`red-r2-api.txt`). Green: Domain tests, command refusals and acceptance with nothing written, the endpoint through JSON surrogate-pair escapes (400, nothing written, only the seeded event) and the accepted text recorded exactly, and the frontend agreement (`describeRunAbandonment.test.ts`: the client already rejected these through its Unicode-aware format-category pattern and keeps valid supplementary text; its tests now lock that agreement; no frontend production file changed).
  - **R3 (damaged stored time).** One column-specific mapping, `StoredAbandonmentTimeConverter` (Infrastructure, internal, applied only to `runs.AbandonedAtUtc`): it writes exactly the provider's text form (`yyyy-MM-dd HH:mm:ss.FFFFFFFzzz`, identical to `LastAdvancedAtUtc` for the same instant, offsets preserved) and reads only that form; anything else reads as `null`, never a valid or default time, so the abandonment is incoherent for that one run only. **Judgment call for you to confirm or replace with a reader-only read:** I chose the mapping over a reader-level try/catch because the cockpit and every other Run load materialize the full entity and would otherwise still throw on that row (the reviewer's `FormatException` was only the first symptom); the reader itself only lost its nested type. No older timestamp mapping, the column, the model snapshot or the migration changed (`HasPendingModelChanges` is false, the column is still nullable `TEXT` with no default). Failing first (`r1/red-malformed-time.txt`): 54 of 62 failed on the default mapping with `FormatException`. Green: `AbandonmentMalformedTimeTests` (62; ten damaged forms: unparsable, empty, impossible date, impossible offset, no offset, truncated, padded, integer, real, BLOB) prove GET (incoherent code, no abandonment, raw value untouched), the reading (null, never default), same-reason and different-reason replays with a populated tracker (incoherent, nothing written), intake blocked with nothing created, a healthy sibling project (coherent reading, `CanCreateRun`, its own intake and status) and the project list, and the cockpit of the damaged run still loading with no added time; a valid-but-different time stays incoherent and whole-second and fractional times read back coherent. Infrastructure (+10): no pending model change, provider-identical text for five instants and offsets, damaged values read as null while the rest of the row loads tracked and untracked, converter round trip. Api (+3, one theory): GET, replay, conflict, cockpit, intake, project list and healthy sibling through the real host.
  - **R4 (organization and ADR).** One top-level type per file: `RunAbandonmentFacts` and `RunAbandonmentEventFacts` (Domain, public as before), `RunAbandonmentReading` (Application, internal as before), `ManualRunAbandonmentView` (Application) and `ManualRunAbandonmentResponse` (Api) beside their result and response, and `AbandonmentSnapshot` (tests); the remaining nested types are private test helpers. Transport names and the generated client are unchanged (byte-identical reproduction, `22074a06…`). ADR-0031 now states the two lifecycle-refusal codes (`runs.not_active` for planning, `runs.not_running` for resolution and review), that native `DROP COLUMN` avoids EF's drop, recreate and rename of the table while SQLite still rewrites the table content (with the primary SQLite link), the R1 reload, the scalar classification and the tolerant time mapping; the protocol and data-and-recovery documents carry the same narrow edits.
  - **Mutations, rebuilt and hash-guarded** (`mutation/r1-backend*`, outside the repository; every target hashed before and after each mutant and restored from a byte copy, each mutant rebuilt non-shared, an invalid build never counted): N01 reload removed, N02 supplementary Format not classified, N03 unpaired surrogate admitted, N04 separators admitted, N05 controls admitted, N06 unreadable time read as a default, N07 mapping not applied, N08 different text form written, N09 lenient parse: all 9 killed. My first N02 to N09 attempt produced invalid builds (a multi-project build argument in my own harness, `MSB1008`, preserved in `r1-backend/N0*-build.txt`); they were never counted as killed and were rerun with solution builds (`r1-backend-n`). The 31 earlier backend mutants (B, D, A, R, C, L, M, E, Q) were also rebuilt and rerun against the corrected tree: 31 of 31 killed, and all targets were byte-identical at the end of both matrices. The frontend mutants (16 of 16) are retained from the first return: no frontend production file changed in this round.
  - **Fresh validation of the corrected tree** (`final2/`, outside the repository; the same sequential stop-on-first-failure script, every exit code checked, non-incremental serial build with 0 warnings and the generated client reproduced byte-identical): Domain 1331/1331 (was 1319), Application 4909/4909 (was 4827), Architecture 47/47, Infrastructure 1640 passed + 4 existing skips (was 1630), Api 1377/1377 (was 1368), frontend Vitest 2458/2458 (was 2447), typecheck, lint (the same existing warnings), production build, strict e2e `tsc`, `test:harness` 87/87, then one canonical `test:e2e:all`: Chromium 13/13 and journeys 5/5, exit 0, no retry. `dotnet format --verify-no-changes` reports exactly the recorded baseline (153 diagnostics in the same 17 pre-existing files, none in this diff); NuGet has no vulnerable package; `npm audit` reports only the existing `source-map-js` advisory; all 304 local Markdown links resolve; hygiene over all 93 changed paths reports only the two pre-existing CR-ending files (planner record, generated client) and the client's missing final newline. Affected regressions were run first (`r1/affected`: Domain 54, Application 223, Architecture 7, Infrastructure 16, Api 36). No full-suite run failed in this round, so there is no new failure to preserve; the earlier-round failures below stand as disclosed. **Fresh** in this round: everything in this bullet. **Retained** from the first return and not repeated: the frontend mutation matrix and the failing-first artifacts `red-intake.txt` and `red-planning-race.txt`; the eight-path race proofs and the production-written journey are not retained, they ran again inside the full Application, Api and e2e runs above.
- **R3 storage-class correction (Codex NO-GO of 2026-10-09; R1, R2, R4 and the column-specific mapping approach were accepted; the three Codex lifecycle guards, the planner record and ADR-0029/0030 are unchanged).** Round preflight verified once: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` `3603cea9ba0d6e11888e99d4974ce338e4fe34ad`; empty index; 93 status paths (35 modified, 58 untracked); planner record `0986797b…` (Codex's second review edit, unchanged by this round and at its end), ledger `0afaf2c7…`, generated client `22074a06…`.
  - **Defect.** The converter received a string only after SQLite's reader had already decoded the stored value, so a BLOB holding the UTF-8 bytes of the exact matching timestamp (`CAST(AbandonedAtUtc AS BLOB)`, `typeof` = `blob`) was admitted as a recorded time: GET showed a coherent abandonment, the same-reason replay succeeded and normal intake created another run. The earlier BLOB test used arbitrary bytes, which is why it did not see this.
  - **Correction (local to `runs.AbandonedAtUtc`).** One new internal type, `StoredAbandonmentTimeTypeMapping` (Infrastructure, the provider-side half of the mapping, composed with the unchanged `StoredAbandonmentTimeConverter`): it follows the repository's existing storage-class precedent (`ExactStoredIntegerTextTypeMapping`) and customizes the data-reader expression so that only a value SQLite stores as TEXT reaches the converter as text; a BLOB, INTEGER or REAL reads as null whatever bytes it holds, so that run's abandonment is incoherent. `RunConfiguration` wires it with one `SetTypeMapping(...WithComposedConverter(...))` call. Writing, precision and offsets are unchanged (same converter, same TEXT written; the existing provider-identical-text test still passes), nothing is repaired, and no column, snapshot, migration, Application port, claim, Git or recovery behavior changed (`HasPendingModelChanges` is still false). ADR-0031 and the data-and-recovery document state that the storage class is preserved at the read boundary and that a damaged value is only read, never rewritten.
  - **Failing first** (`r3/red-storage-class.txt`, unfixed mapping): `AbandonmentStorageClassTests` 30 cases, 20 failed and 10 passed. All 20 BLOB cases failed (four instants: whole seconds, fractional seconds, positive offset, negative fractional offset; each across the reader and status, the same/different-reason replay with a populated tracker, blocked intake, the whole-row cockpit with an unrelated save, and the healthy sibling and project list); the four identical-TEXT controls and the six other storage classes (NULL, INTEGER, REAL, unrelated BLOB, empty BLOB, the exact text plus a trailing NUL) passed. Infrastructure and Api on the old wiring (`red-infra.txt`, `red-api.txt`): the four BLOB-of-valid-time cases and the one BLOB Api case failed as expected. `red-infra.txt` also shows five further failures that were a defect of my own new test, not of the code: its raw `UPDATE` matched the run by a lowercase id text while SQLite stores the id uppercase, so it changed zero rows; I fixed the test to bind the id and assert one affected row, then reran (`green-infra.txt`).
  - **Green.** `AbandonmentStorageClassTests` (30): GET (incoherent code, no abandonment), the reading (null, never default), same-reason and different-reason replays with a populated tracker (incoherent, nothing written, class and bytes retained), blocked intake with nothing created, the whole-row cockpit, tracked and untracked loads and an unrelated save of the same row leaving the BLOB's class and hex unchanged, a healthy sibling, a TEXT control project and the project list unaffected, and identical TEXT as the control for every instant (coherent reading, replay with the original time, conflict, intake allowed). Infrastructure (+9, `AddRunAbandonmentMigrationTests` now 25): BLOB-of-valid-time reads as null while the identical TEXT reads as the time with its offset (four instants), same bytes in both rows, class and hex retained after an unrelated tracked save, and NULL/INTEGER/REAL/empty and unrelated BLOB read as null keeping their class. Api (+2, one theory, `AbandonManualRunEndpointTests` now 30): the real host with TEXT (replay 200, conflict, intake 200) and BLOB (status incoherent, replay and conflict 409 `abandonment_incoherent`, intake 409, project list blocked, cockpit 200), class and bytes unchanged.
  - **Mutations** (`mutation/r3-backend*`; rebuilt, hash-guarded, restored): S01 storage-class admission removed (a BLOB decodes and is admitted), S02 mapping not wired to the column, S03 BLOB admitted alongside TEXT, S04 neither converter nor mapping applied: all four killed by the application, migration and Api suites; N06, N08 and N09 (converter mutants) rebuilt and killed again after the converter's comment changed. N07's find text no longer exists after the rewiring (the matrix reported it INVALID, never counted); S04 is its equivalent. Every target was byte-identical to its baseline after each matrix. Retained from the previous round: N01 to N05 and the 31 earlier backend mutants, and the 16 frontend mutants.
  - **Validation of the final tree** (`final3/`; the same sequential stop-on-first-failure script, non-incremental serial build, 0 warnings, generated client reproduced byte-identical `22074a06…`; affected checks first, `r3/affected`: Domain 54, Application 253, Architecture 7, Infrastructure 25, Api 38, all passed). Fresh: Domain 1331/1331; Application 4939/4939; Architecture 47/47; Infrastructure 1649 passed + 4 existing skips; `test:harness` 87/87; then one canonical `test:e2e:all`: Chromium 13/13 and journeys 5/5, exit 0, no retry. `dotnet format --verify-no-changes`: the recorded baseline (153 diagnostics, the same 17 pre-existing files, none in this diff); NuGet: no vulnerable package; Markdown links 304/304; hygiene over all 95 changed paths: only the two pre-existing CR-ending files and the generated client's missing final newline.
  - **Api suite: one failure, preserved and also reported on unchanged HEAD; historical cause unproven.** The full Api run (`final3-failed-api/06-api.txt`) was 1378 passed and 1 failed: `LocalCommitCompetingPreparationTests.The_admitted_operation_executes_from_its_own_artifact_while_the_other_preparation_stays_inert` (`Assert.Single(Scene.StorageLeaves())`: the collection was empty), an earlier slice's test of the inert artifact of the competing local-commit preparation; it is not touched by this slice and passed in the two earlier full runs of this slice. I did not repeat the chain to obtain green; instead I ran that test class alone: six times on this tree (4 failed, 2 passed, `r3/flake/run1..6.txt`) and six times on a clean checkout of `HEAD` in a temporary worktree with none of this slice's changes (6 of 6 failed, `r3/flake/head-run1..6.txt`; the worktree is removed). The same assertion was reproduced without this diff; the cause and event order of the historical failure remain unproven. I did not investigate or change it (out of scope). The chain stopped there, so steps 07 to 11 did not run in `final3`; steps 12 and 13 were run as a separate sequence (`final3/summary-rest.txt`) after the failure was diagnosed.
  - **Retained, not rerun in this round** (no frontend file was edited in this round, and the Api surface and generated client are byte-identical): frontend Vitest 2458/2458, typecheck, lint (the existing warnings), production build, strict e2e `tsc`, `npm audit` (only the existing `source-map-js` advisory) and the frontend mutation matrix, from the previous round's `final2`. Fresh in this round: everything listed under Validation of the final tree and the mutations above.
- **Codex final review (2026-10-09).** R1-R4 including the R3 storage-class correction are accepted, and publication of the frozen 95-path snapshot is authorized by the planner record. Independently fresh: serial build 0 warnings/errors, Application abandonment 253/253, Infrastructure migration/mapping 25/25, Api abandonment 38/38 and full Architecture 47/47 (no skips); the real SQLite reviewer probe now refuses GET/replay/intake for a date-shaped BLOB while the valid TEXT control stays coherent. The full Api result above remains 1378 passed and 1 failed, never a clean full-suite claim. Codex treats that specific existing final inert-artifact assertion as non-blocking for this bounded slice: the test parks any preparer result without establishing it was Prepared; a controlled real-host/Git first-preparation refusal produced Completed for the admitted operation, HTTP 409 for the parked request and zero leaves, whereas two Prepared results produced one inert leaf. This establishes a deficient test premise and a safe counterexample, not the cause or event order of the historical failed run. All earlier failure evidence is retained, no production/test source was changed by Codex, and no blanket test or CI waiver, next slice, increment or MVP completion is granted. The isolated diagnostic setup first failed at its own content root, then corrected that setting; its cached restore omitted audit and makes no audit-cleanliness claim. The approved implementation is still unstaged, uncommitted and unpushed at this review; publication and factual closure remain to be performed under the planner's single publication instruction.
- Publication and post-publication closure (this factual closure changes only this entry of this file; it does not name its own commit). Codex's publication GO covered the frozen 95-path snapshot of this slice, including this file and the planner record as reviewed. The ignored publication manifest `reviewed-manifest.json` (SHA-256 `424bb3b089d061dcfa9e5a6b999857c3cce717aeb2a3ac7dd09609f999a7d16b`) was verified before staging: its own hash, the exact Git inventory (95 paths, 35 modified tracked and 60 untracked, no extra, missing or duplicate path) and, for every entry, the Git status, the raw SHA-256 and the byte size matched with no discrepancy; the final raw hashes of the planner record `a7fb32a079407178aed4525a01041a03117e41ea5e47d5f3cbb02d7674d9fbf3`, of this file `337c74fe2c27df3a46e0ba3c26ee6a2a74308af7cf1f7b11c3a654a3fa5e4377` and of the generated client `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64` also matched. Only those 95 paths were staged; the staged inventory with `--no-renames` was 35 modified and 60 added paths and `git diff --cached --check` passed. The substantive commit is `f381c0c4af00b726c0d4ef7bbe6fbf2af32447c8` (`feat: abandon an inactive manual run through an explicit human decision`, 95 files), whose parent is the verified baseline `3603cea9ba0d6e11888e99d4974ce338e4fe34ad`. `main` was pushed normally (a fast-forward from `3603cea` to `f381c0c`, no amend, no force); after `git fetch`, an independent `git ls-remote origin refs/heads/main` returned `f381c0c4af00b726c0d4ef7bbe6fbf2af32447c8`, equal to `HEAD` and to the local `origin/main`, with a clean checkout.
- Fresh post-publication checks on the substantive commit, run sequentially from fresh output files with an explicit exit-code check after every step, stopping at the first failure (all exits were 0; complete outputs are kept outside the repository): serial `dotnet build --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; with `--no-build --no-restore`: Domain `RunAbandonmentTests` 54 passed, Application `Abandon` 253 passed, Infrastructure `AddRunAbandonmentMigrationTests` 25 passed, Api `Abandon` 38 passed, full Architecture 47 passed (no skips in any selection). Frontend: full Vitest 2458 passed in 158 files, `npm run typecheck` 0, `npm run lint` exit 0 with the same 9 baseline warnings in unrelated hooks, `npm run build` 0, `npm run test:harness` 87 passed; then exactly one canonical `npm run test:e2e:all` after the harness passed, with normal authentication and composition: Chromium 13 passed and journeys 5 passed, exit 0, no retry, no skip, no failure. The generated client is still `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64`, `git diff --check` is clean and the checkout is clean. This execution left no fixture root behind: the two older `devalcopilot-e2e-` roots present before the browser run are the only ones present after it, and they were not touched.
- Retained, not rerun after publication, with their recorded scope: the full backend suites of the reviewed tree (Domain 1331, Application 4939, Architecture 47, Infrastructure 1649 passed + 4 skipped, and the Api suite as 1378 passed and 1 failed, never relabelled as green), the dependency audits, the formatter baseline and every mutation matrix recorded in this entry (backend N01 to N09, S01 to S04 and the 31 earlier backend mutants, all killed; frontend 16 of 16). The Api failure stays exactly as recorded above and in the Codex final review bullet: the existing competing-preparation test's final inert-artifact assertion (`Assert.Single(Scene.StorageLeaves())`), reproduced on an unchanged `HEAD` in six class runs and intermittent on this tree; Codex judged that specific assertion non-blocking for this bounded slice and identified a deficient test premise (the parked preparer result is assumed to be Prepared), but the cause and event order of the historical failing run remain unproven, and nothing here claims it is fixed or that the full Api suite is green. No unchanged failure was rerun to obtain a green result, and every earlier failure and disclosure in this entry is preserved.
- Limits unchanged by this closure: the remaining limits of every round above still stand (among them: abandonment is metadata only and cancels, pauses, releases, repairs and deletes nothing; a damaged abandonment time keeps that one run incoherent and is only read, never repaired; a verification claimed after the closure remains project work; the inert artifact of an unadmitted preparation stays and there is no janitor; the browser proof uses owned provider doubles; Windows-only physical proof is unchanged). This delivery does not claim real-provider reliability, does not complete any increment or the MVP, and selects no next slice; the inert-preparation fixture premise remains a bounded follow-up candidate for planner selection.
- Decision under the write lock: the transaction's first statement is a no-op write that takes the SQLite write lock (a structural test proves it is the first statement executed); fresh untracked reads then require exact stored `ManualAgent`, lifecycle `Created` or `Running`, no attempt of any run of the project that is not `Completed`, `Failed` or `Interrupted` (a `Running` or unrecognized status blocks), no project verification execution that is not terminal or is unrecognized, no local-commit operation that is not terminal (`Prepared`, `Executing`, `NeedsAttention` and unrecognized all block) and no `Preparing`, `Committing` or unrecognized workspace; a missing workspace is valid and every other known workspace state, its lease, ref, index and files are untouched. The same normalized reason on a coherent abandonment returns the original result (no second event or time), a different reason conflicts, and incoherent facts answer neither. One coherence rule (`RunAbandonmentPolicy.IsCoherent`: exact mode, canonical reason, time equal to the last advance, no participant, exactly one Human run-scoped event with the same reason and time) is read by one shared reader for intake, replay, the project summary and the status. `RunIntentRecorder` treats only a coherent abandonment as terminal and nothing else changed; a new run is distinct with normal fresh budgets and inherits nothing.
- **Claim-guard gap found and closed (report for planner judgment; no redesign).** The stop gate asked to report an existing claim-guard gap. Failing-first evidence (`red-planning-race.txt`, outside the repository): with a `Running` run, a Codex planning claim that decided before an abandonment committed its Attempt and manifest on the ABANDONED run (the test's `Assert.True(result.IsFailure)` failed), and for a `Created` run it failed only incidentally through the `Run.Claim` update (`attempts.persistence_failed`). Cause: the Codex planning, challenge-resolution and code-review claims (and their repairs) commit through guards that are compare-and-update statements on other Run columns, and none reads `Lifecycle`; the lifecycle concurrency token only protects a Run UPDATE, which a Codex claim of a `Running` run never emits. The Claude critical-review, implementation and review-correction claims and the dispatch marker commit through a Run UPDATE that includes the token and already lose; the verification-diagnosis and diagnosis-correction claims already re-read the lifecycle in their transaction. The correction is one fresh untracked lifecycle read (`CurrentRunLifecycle.IsStillActiveAsync`) in the three Codex handlers, inside their existing short claim transaction after the first guard write has taken the write lock and before any insert; it refuses with the codes those handlers already use for an ended run (`runs.not_active` for planning, `runs.not_running` for the other two), rolls back and removes the sealed manifest. No claim or dispatch infrastructure, budget, reservation, recovery or Git behavior changed. A claim-path source guard is in the new Architecture boundary tests.
- Surfaces: protected read-only `GET /api/runs/{runId}/abandonment` (`GetManualRunAbandonmentQuery`: advisory eligibility with a fixed code and, for a coherent abandonment, its reason and time; writes nothing); the project summary lists the `Abandoned` lifecycle and offers intake only for coherent facts; the cockpit adds an "Abandon run" panel (generated client only; form offered only from a settled, successful eligible status read; draft, request guard, error and continuations owned by the committed run and eligible-form lifetime and the draft version; an unknown outcome is reconciled only by reading the status and never retried; a recorded abandonment refreshes the status and project list before the ownership check and stays real if a later refresh fails; the recorded reason and UTC time are shown as plain text with a not-completed note), a neutral header tone, no Agent request actions for an abandoned run, and a fixed summary for the event.
- Production inventory: Domain `RunLifecycle`, `Run.Abandon`, `RunEventType.RunAbandoned`, `RunAbandonmentPolicy`, `RunLifecycleAdmission`; Application `AbandonManualRun` (command, handler, validator, result), `GetManualRunAbandonment` (query, handler, result), `RunAbandonmentErrors`, `RunAbandonmentAuthority`, `RunAbandonmentReader`, `CurrentRunLifecycle`, the three Codex claim handlers (one read and one refusal each), `RunIntentRecorder`, `GetProjectRunSummariesQueryHandler`; Infrastructure `RunConfiguration`, the `AddRunAbandonment` migration and snapshot; Api the two endpoints with their request and response types; the regenerated client (272 added lines, none removed) and `clients.ts`; frontend `AbandonRunPanel`, `describeRunAbandonment`, `useManualRunAbandonment`, `useAbandonManualRun`, `RunCockpitView`, `RunHeader`, `App`, `parseEventSummary`/`useRunCockpit`, CSS; ADR-0031, the ADR index, engineering context, protocol, workflow model, data and recovery, cockpit specification and roadmap.
- Failing-first and tests (all on real file-backed SQLite unless noted). `red-intake.txt`: before any production edit an inactive `Created` and `Running` manual run each blocked another objective (`runs.intent_blocked`); `red-planning-race.txt` is the claim-guard gap above. Domain (`RunAbandonmentTests`, 42): enum values appended, reason matrix (CRLF, controls, separators and format characters, unpaired surrogates built in code, UTF-8 bytes not characters), `Abandon` for `Created` and `Running`, every terminal and non-manual refusal, frozen time, preserved stage, the coherence matrix and the admission table. Application: `AbandonManualRunCommandHandlerTests` (72: closure, frozen clock, normalization, validation, 22 refusal scenarios with a before/after snapshot proving nothing is written, retained workspace states, terminal operations, replay, conflict, 13 incoherent variants, overlapping duplicates, untracked-read proofs against a stale tracker, a competing admission and a verification claim at the seam, rollback by an injected failure in either table, the first-statement lock proof, the write-lock exclusion of a competing writer, notification); `RunIntakeAfterAbandonmentTests` (18: both operations, distinct identity and number, fresh and chosen budgets, nothing inherited, history unchanged, ten incoherent variants block, another run or project); `AbandonmentReadModelTests` (12: status, project summary, frozen cockpit clock); `AgentDispatchAbandonmentTests` (12); and the race proofs below. Migration (`AddRunAbandonmentMigrationTests`, 6): old runs of every lifecycle keep NULL and all data, the schema objects are otherwise identical, round trip, the stale-token loss, and Down drops exactly the two columns (this test found that the provider's `DropColumn` table rebuild fails against the `attempts` guard trigger, so `Down` uses native `ALTER TABLE ... DROP COLUMN`). Four older migration `Down` tests list the new columns beside their own. Api (`AbandonManualRunEndpointTests`, 19, one on real Kestrel for 413 above 8 KiB; `LocalCommitAbandonmentTests`, 8, real Git through the production host): authentication, validation, closure visible everywhere, replay and conflict, refusals, restart persistence, the local-commit race in both orders, an ambiguous `NeedsAttention` operation never overridden, unchanged workspace, lease, branch, index and source bytes, and a verification claimed before the closure blocks it while one claimed after remains project work. Architecture (`RunAbandonmentBoundaryTests`, 7). Frontend (89 new Vitest cases and edits): fixed copy and validation, the status hook, the request hook (unknown outcome, A to B to A, same-text draft edits, unmount, a recorded abandonment after an obsolete owner), the panel (settlement, ownership, reconciliation), composition in `RunCockpitView`, `RunHeader`, `App` and the event summary.
- Race proofs (deterministic seam signals, no sleeps or retries): for each of the eight Agent claim handlers, both directions. Abandonment winning at the claim seam persists no attempt, input, artifact or authorization and removes the sealed manifest (Codex paths through `BeforeBeginTransaction`, Claude paths through the save interceptor, plus the evidence-capture window for critical review): planning (`Created` and `Running`), critical review, challenge resolution, implementation, code review, review correction, verification diagnosis and diagnosis correction; and the four repair forms (planning, critical review, challenge resolution, code review). A claim winning first makes the abandonment refuse (`active_attempt`) and leaves the run, its attempt and its absence of an event exactly as the claim left them. Dispatch: against an `Abandoned` run it is refused for all six dispatch paths, and a dispatch that decided earlier loses to the lifecycle token at its save. Local commit: an abandonment after preparation makes the locked admission refuse (`run_not_eligible`) and the reservation rolls back; an admission that wins leaves `Committing` and a `Prepared` operation that the abandonment refuses; `NeedsAttention` also refuses. Verification seam: a claimed execution at the seam or before blocks; one claimed after the closure is project work.
- Browser (`e2e/abandon-run.journey.ts`, production-written through rendered controls, owned provider doubles, only reads of the database, workspace, artifacts and log): register, prepare workspace, capture checkpoint, record an objective, one process-double Codex planning attempt, abandon through the rendered form (the generated client sent exactly `{reason}`; HTTP 200), see the recorded reason, UTC time and the not-completed note without a reload, normal intake offered again; the database shows `Abandoned`, the kept stage, a cleared participant, one Human event, the attempt history intact and no provider invocation; a reload shows the same facts with no second request and no invocation; a second objective is a distinct execution 2 run with defaults and no attempt; the workspace row, lease, workspace bytes, the sealed manifest bytes and the source repository snapshot are identical, also after a final reload.
- Mutation evidence (guarded harness outside the repository: every target hashed before and after each mutant and restored from a byte copy, each mutant rebuilt non-shared first, a failed build invalid, all targets byte-identical at the end). Backend: 31 mutants, 31 killed after one added test. Killed: guards omitted, event lost, event duplicated, replay of a different reason, mode not checked, arbitration skipped, Created time counted, stage advanced to Completed, participant kept, multiple events accepted, CRLF not normalized, a one-byte bound error, event reason not compared, unrecognized attempt/verification/operation/workspace states passing, project scope lost, incoherent facts admitting intake, an Abandoned run still blocking intake, the summary offering intake for incoherent facts, each of the three Codex claim guards removed (C01 to C03 rerun after the final refusal-code adjustment), local-commit admission accepting an Abandoned run, the migration Down by table rebuild, the removed body bound, the status reporting incoherent facts, and an attempt-bearing event accepted. B05 (the locking write replaced by a read) first SURVIVED: SQLite's rollback journal also blocks a competing commit after any read, so the lock-exclusion test could not distinguish the two; a structural test of the first executed statement was added and B05 is then killed. Frontend: 16 mutants, 16 killed (form identity ignoring eligibility, owner key ignoring the draft version or the identity, submit allowed while a read is pending, no status refresh after acceptance, unknown outcome not reconciled, an old overlapping read writing, no project refresh, raw draft sent, Agent actions kept for an abandoned run, panel for every mode, a reason from any event type, no header tone, no project refresh wiring, the byte bound as characters, a 5xx as a definite refusal). No equivalent survivor is claimed.
- First-return validation (superseded by the corrected-tree validation in the correction-round bullets above; kept as the record of that return) (complete outputs outside the repository), run sequentially from a script that stops at the first failing step and checks every exit code: `dotnet build-server shutdown`, the generated client deleted and a serial non-incremental `dotnet build DevalCopilot.slnx --no-restore --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors with the client reproduced byte-identical (SHA-256 `22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64`, the working copy with its generator line endings; versus HEAD it only adds the new operation); then `--no-build`: Domain 1319/1319 (was 1277), Application 4827/4827 (was 4686), Architecture 47/47 (was 40), Infrastructure 1630 passed + 4 existing skips (was 1624 + 4), Api 1368/1368 (was 1341); frontend Vitest 2447/2447 (was 2346), typecheck, lint (the existing baseline warnings only), production build; strict e2e `tsc` (`--ignoreConfig --noEmit --strict --target es2023 --lib es2023,dom --module esnext --moduleResolution bundler --skipLibCheck --allowImportingTsExtensions --verbatimModuleSyntax --types node --noUnusedLocals --noUnusedParameters --erasableSyntaxOnly` over every e2e file and both Playwright configurations) 0; `npm run test:harness` 87/87; then ONE canonical `npm run test:e2e:all`: Chromium 13/13 and journeys 5/5 including the new one, exit 0, no retry. After the matrix a clean non-incremental rebuild and the abandonment subsets were run again on the final tree (Domain 42, Application 141, Api 27, migration and Infrastructure subset 205, Architecture 7; all passed). `dotnet format DevalCopilot.slnx --verify-no-changes` reports exactly the recorded baseline (153 diagnostics in the same 17 pre-existing files) and none in this diff. NuGet: no vulnerable package; `npm audit`: only the existing `source-map-js` advisory. Hygiene over all 84 changed paths: no NUL, trailing whitespace, missing final newline (other than the generated client) or blank final line; the generated client and the planner record carry their existing CRLF working-copy endings that Git normalizes (`git diff --check` reports nothing beyond that notice). All local Markdown links of the changed documents resolve.
- Failures and disclosures, preserved: (1) the first full run stopped at Application: two existing repair-seam tests (`CreateChallengeResolutionRepairAttemptTests`, `CreateCodeReviewRepairAttemptTests`) expect `runs.not_running` for a run that ended at the claim seam and my new guard answered `runs.not_active`; the two handlers keep their existing code (`CurrentRunLifecycle.NotRunning`), only my own assertions changed (`final-run1-failed`). (2) The second run stopped at Infrastructure: four older migration `Down` tests list exactly the columns their migration drops after migrating to the latest; they now also list the two new columns, the convention earlier slices followed (`final-run2-failed`). The third run is the one above and every step passed. (3) My first `migrations remove --no-build` deleted the committed `AddLocalCommitAttentionExclusion` migration files because the stale build did not contain the new one; the two files were restored from Git (the Migrations folder shows only the intended additions and the snapshot's seven added lines) and the empty migration was deleted by hand. (4) The very first Planning race test used the seeding context for the handler (a stale tracked mode) and showed a misleading failure; the tests now seed with one context and run the handler on a fresh one, as production does.
- Inventory (as updated by the correction round): 95 paths in total after the storage-class correction (93 after the first correction round): 35 modified tracked and 60 untracked; the first correction round added nine files (`RunAbandonmentFacts`, `RunAbandonmentEventFacts`, `RunAbandonmentReading`, `ManualRunAbandonmentView`, `ManualRunAbandonmentResponse`, `StoredAbandonmentTimeConverter`, `AbandonManualRunStaleTrackerTests`, `AbandonmentMalformedTimeTests` and `AbandonmentSnapshot`); the storage-class correction added two more (`StoredAbandonmentTimeTypeMapping` and `AbandonmentStorageClassTests`) and edited `RunConfiguration`, the converter's comment, `AddRunAbandonmentMigrationTests`, `AbandonManualRunEndpointTests`, ADR-0031 and the data-and-recovery document; the first return counted 84 paths as 35 modified and 49 untracked (the planner record is one of the modified paths and is unchanged by this slice). Production and persistence: the Domain, Application, Infrastructure and Api files named above, the migration pair, the regenerated client and `clients.ts`. Frontend: ten new files (including the journey) and eleven modified source, test or generated files. Tests added or changed: Domain 1, Application 14 (eleven new classes and support files, the race tests added as partial classes beside each handler's tests), Api 2, Architecture 1, Infrastructure 1 new and 4 edited. Documents: ADR-0031 and the six narrow updates named above plus this file.
- Remaining limits (not solved here): abandonment is not a process cancellation, pause, lease release, workspace repair, lock adoption, commit recovery or deletion; an active or ambiguous attempt, verification or local commit must first end by the existing mechanisms; there is no undo or edit of an abandonment and no automatically created replacement run; a verification execution claimed after the closure remains project work and can leave a `Running` project fact that has nothing to do with the run; an inert prepared artifact of an unadmitted preparation stays as ADR-0029 states; the migration `Down` is destructive for the two columns; the browser proof uses owned provider doubles and proves the assembled path, not provider reliability; the existing two Vitest and Playwright baseline warnings and the `source-map-js` advisory predate this diff; Windows-only physical proof and every other earlier limit stand.

## Complete human verification-set approval for local delivery (2026-10-08)

- Publication and post-publication closure (this factual closure changes only this entry of this file; it does not name its own commit). Codex's publication GO covered the frozen 75-path snapshot of this slice, including this file and the planner record as reviewed. The ignored publication manifest `complete-set-publication-manifest.json` (SHA-256 `fb494414a49f76a7e7d6fbee9a0332e46810b3ea4208db0f561f857830ee3cb4`) was verified before staging: its hash, the exact Git inventory (75 paths, 46 modified tracked and 29 untracked) and, for every entry, the Git status, the raw SHA-256 and the byte size matched with no discrepancy; the reviewed hashes of the planner record `d920b9159032ca3f0a033bb66c1deed563ff0827840fb9daa964ea9d0d536113`, of this file `a29ba8d55c298143d06093df21bbb0d2d1bdeca4cb6ec728db6ba35647bad643` and of the generated client `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac` also matched. Only those 75 paths were staged; the staged inventory with `--no-renames` was 46 modified and 29 added paths and `git diff --cached --check` passed. The substantive commit is `5a36784577358fc31a624220b4264493081e0eb2` (`feat: approve the complete verification set as one human decision`, 75 files), whose parent is the verified baseline `426463717c0e724780b511763681630c1a5b63a7`. `main` was pushed normally (a fast-forward from `4264637` to `5a36784`); after `git fetch` and an independent `git ls-remote`, `HEAD`, local `origin/main` and live `refs/heads/main` all equaled that commit with a clean checkout. Nothing was amended or forced.
- Fresh post-publication checks on the substantive commit, run sequentially from fresh output files with an explicit exit-code check after every step (all exits were 0; complete outputs are kept outside the repository, the complete browser output in `11-e2e-all.txt`): serial `dotnet build --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; Application `RecordCheckpointReviewCompleteSetTests` plus `GetCheckpointApprovalEvidenceQueryHandlerTests` 82 passed, 0 skipped; Infrastructure `LocalCommitGitPreparationIsolationTests` plus `LocalCommitStorageOwnershipTests` 29 passed, 0 skipped; Api `Features.Runs.LocalCommit` plus `CheckpointApprovalEvidenceEndpointTests` 147 passed, 0 skipped; full Architecture 40 passed, 0 skipped (all with `--no-build --no-restore`). Frontend: full Vitest 2346 passed in 153 files, `npm run typecheck` 0, `npm run lint` exit 0 with the same 9 baseline warnings in unrelated hooks, `npm run build` 0, `npm run test:harness` 87 passed, 0 skipped; then exactly one canonical `npm run test:e2e:all` after the harness passed, with normal authentication, host composition and all supervisors: Chromium 13 passed and journeys 4 passed, exit 0, no retry, no skip, no failure. The generated client is still `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac`, `git diff --check` is clean and the checkout is clean.
- Retained, not rerun after publication, with their recorded scope: the full backend suites of the reviewed tree (Domain 1277, Application 4686, Infrastructure 1624 passed + 4 skipped, Api 1341), the dependency audits, the formatter baseline, and every mutation matrix recorded in this entry (frontend 30 of 32 with two redundant-defense survivors, the first-return backend review-set matrix 19 of 20, the preparation-ownership matrix 13 of 14 with its surviving collision-guard mutant not represented as equivalent, and the R4 fixture matrix 8 of 8). Every inventory, count and planner or current-work hash written earlier in this entry (45, 51, 67, 69, 73 and 75 paths; planner hashes `8c3e8c33…`, `47c84942…`, `41aa6002…`, `3c38e4f9…`, `9c8e9dec…`; this file's earlier hashes) is a historical pre-publication snapshot; the delivered snapshot is the one named above. Every earlier failure stays preserved exactly as recorded (the Api HTTP 500 and the Infrastructure cancellation failure of the first return with their diagnoses, Codex's failed independent Api selection of 134 passed and 1 failed, and the unproven native outcome of that run), and no unchanged failure was rerun for a green result.
- Limits unchanged by this closure: the remaining limits of every round above still stand (among them: a set approval goes stale after any rerun or recipe change; no Agent set approval; more than 32 enabled recipes cannot be approved as a set; unadmitted preparations leave inert artifacts and there is no janitor; pre-launch cancellation semantics are characterized, not changed; Windows-only physical proof is unchanged). This delivery does not claim real-provider reliability, does not complete any increment or the MVP, and selects no next slice.
- Status (first return; its NO-GO is answered by the correction-round bullets that follow): implemented and validated as an UNSTAGED, UNCOMMITTED and UNPUSHED working-tree diff awaiting Codex GO/NO-GO; no commit, push or next slice is granted. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `426463717c0e724780b511763681630c1a5b63a7`; empty index; only `docs/roadmap/planner-handoff.md` modified; nothing untracked; planner SHA-256 `8c3e8c3347a71b28d3fd602119d721c97b19a4adc3a126b3dc9a39a1aa4dd65e` and ADR-0029 `cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294` (both unchanged at the end; neither file was edited) and the previous generated client `e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969`. The generated client was deleted and regenerated by a clean `--no-incremental` solution build to `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac`; the diff is purely additive (the optional `verificationExecutionIds` and the new operation) and the clean rebuild reproduced those exact bytes.
- Preparation ownership correction (after Codex's second NO-GO, "Correction review (2026-10-08): NO-GO; isolate concurrent preparation artifacts" in `planner-handoff.md`, which this slice does not edit; implementation authorization only, no staging, commit, push or next slice). Still an UNSTAGED, UNCOMMITTED and UNPUSHED diff. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `426463717c0e724780b511763681630c1a5b63a7`; empty index; 53 paths (30 modified tracked, 23 untracked); planner record `41aa6002fbe971e804995e00c25b30f0d9c2c6b150afb66113dc4c06a72314d9`, ADR-0029 `cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294`, generated client `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac`; no discrepancy. At the end the planner record and ADR-0029 are byte-identical to those hashes, the client was deleted and regenerated by a clean `dotnet build --no-incremental -m:1` to the same bytes, and nothing is staged. The 150-run diagnosis of R3a below is what this correction answers.
- Production change (Infrastructure only; the preparation port, `RequestLocalCommitCommandHandler`, database admission, idempotent replay, authority gates, commit construction, hashes, the prepared ref transaction, HEAD protection, native index effects and lock/recovery authority are unchanged; no migration and no port-shape change). `LocalCommitStorage` now names two leaves per preparation invocation by a fresh preparation identifier that is independent of the operation identifier: the scratch leaf `work/<preparation>` and the artifact leaf `operations/<preparation>/prepared.index`. `PrepareOnWindowsAsync` generates that identifier per call, creates only those leaves, creates the artifact exclusively (`File.Copy(..., overwrite: false)`; an existing file is a Git-failed refusal, never a replacement), records the exact relative path through the existing `PreparedIndexRelativePath` fact, and in its `finally` removes its own scratch leaf and, when it did not prepare, its own artifact leaf, through `TryDeleteOwnedLeaf`, which accepts only a direct child of the `work` or `operations` folder inside the owned root. `ObserveControlledAsync` takes a scratch directory instead of an operation identifier: the preparation passes its own leaf (the observation nests inside it), and the execution and inspection callers allocate and remove a fresh leaf of their own, so no observation shares a directory with another. Terminal cleanup (`CleanupAsync`) now addresses the recorded artifact only (`TryRemoveRecordedArtifact`: the file, then its own directory only when empty, never recursively, and only when the recorded path resolves to a file strictly inside a leaf of the operations folder), instead of recursively deleting an operation-named directory. A mutex or mapping the failure to a conflict was not used, as the planner required. The former operation-named helpers survive only as `LegacyOperationDirectory` and `LegacyPreparedIndexRelativePath`, used by the compatibility tests.
- Failing-first evidence, preserved (`red-preparation-isolation.txt`, outside the repository): the new real-Git interleavings were first run against the unchanged production code and 7 of the 8 failed: the parked preparation's scratch was gone after the other one's cleanup (all three pause points, the refusal and the cancellation cases), the terminal cleanup of the admitted artifact removed the in-flight preparation's leaf, the legacy-path case collided with the old layout (`IOException`: the former current path equals the legacy path), and only the different-operation-identifier control passed. After the change the same cases pass (`green-preparation-isolation.txt`).
- Tests. `LocalCommitGitPreparationIsolationTests` (9; two `LocalCommitGit` instances share one storage root, a gating process adapter parks one preparation at an exact Git command and the other runs to its end, so every interleaving is deterministic and uses real Git): identical operation identifiers parked at the isolated tree build, the controlled observation and the promotion index (distinct leaves and distinct artifact paths, equal tree and commit, exact SHA-256 of each recorded artifact, scratch independent while the other finishes, no scratch left, the admitted one still passes `InspectAsync`, executes and leaves the main checkout byte-identical, and its terminal cleanup removes only its own leaf); terminal cleanup of the admitted artifact while another preparation is in flight; a refusal and a cancellation of one preparation while another is in flight; competing operation identifiers (a control); overlapping observations of one operation that never remove each other's scratch; and a legacy recorded path (`operations\<operation>\prepared.index`) that is readable, executes and is cleaned without touching a current-layout artifact. `LocalCommitStorageOwnershipTests` (20 cases): current and legacy removal leave siblings, a non-empty directory is never removed recursively, nine recorded paths (escaping the root, absolute, inside `work` or `hooks-empty`, a nested or foreign `operations` folder, or directly in the folder) remove nothing, and six leaf paths (the root, `work`, `operations`, a grandchild, `hooks-empty`, outside) are never an owned leaf. Api (real host, real Git, file-backed SQLite) `LocalCommitCompetingPreparationTests` (2): two identical requests prepare together, one is parked after its own preparation and the other is admitted; the admitted operation executes from its own recorded artifact to `Completed` (the late parked request gets a defined answer, OK or Conflict, never a failure) and the main checkout is unchanged, and after a restart recovery interrupts the never-executed operation and removes only its recorded artifact leaf, leaving the other preparation's artifact inert. Existing assertions on the old operation directory were replaced by the stricter recorded-artifact or `StorageLeaves()` checks (no scratch or artifact leaf remains after a refusal or a terminal cleanup); `LocalCommitEndpointTests.Concurrent_identical_and_competing_requests_admit_exactly_one_operation` keeps its assertions unchanged (OK or Conflict only, one durable operation and one admission event).
- Comparative evidence (not proof of ownership; the interleavings and the mutation matrix are the proof): the same concurrent endpoint body run as six parallel classes of 25 (`diag-concurrent-loop-after-fix.txt`) passed 150 of 150, against 141 passed and 9 HTTP 500 before this correction (`diag-concurrent-loop-1.txt`, the original diagnosis). The temporary loop harness was removed.
- Mutation evidence (guarded: each target hashed before and after and restored from a byte copy, each mutant rebuilt non-shared first, a failed build is invalid and its tests are never run, all four targets byte-identical at the end, then the clean rebuild above): 14 mutants, 13 killed. Killed: preparation leaves named by the operation (7 failing tests), the artifact leaf shared by operation (7), the scratch leaf shared (6), scratch cleanup deleting the whole work folder (7), failed-preparation cleanup deleting the whole operations folder (2), the observation scratch shared (1), terminal cleanup by operation-level ancestor (5) or deleting the whole operations folder (5), recorded leaf removed recursively (1), the owned-folder check always true (7), the recorded parent's name (1) or folder (1) not checked, and the owned-leaf parent not checked (6). ONE SURVIVED: replacing `overwrite: false` with `overwrite: true` at the artifact copy. This matrix never arranged an already-existing destination, so it does not prove that guard or establish an equivalent mutation. Fresh preparation identifiers and the ownership regressions cover the demonstrated shared-directory race; the exclusive creation remains in the inspected production code, with its collision refusal not separately proven by this matrix (`mutation-results-backend-prep.txt`, outside the repository).
- Fresh validation of this correction, on the final tree. Affected first: Infrastructure local-commit tests 216/216 before the last test additions, then the three new classes 29/29, Api local-commit tests 112 of 113 on the first run (the one failure was my own new test asserting that the parked request is always OK; a late request after completion is the existing Conflict arbitration, so the assertion was corrected to OK or Conflict and the pair passed 2/2). Then `dotnet build --no-incremental -m:1` 0 warnings/0 errors with the client regenerated byte-identical, and sequentially with `--no-build`: Domain 1277/1277, Application 4686/4686, Architecture 40/40, Infrastructure 1624 passed + 4 skipped (the same privilege-dependent skips; was 1595), Api 1329/1329 (was 1327). Frontend (no frontend file changed in this round; rerun as required): `npx vitest run` 2346/2346 in 153 files, `npm run typecheck` 0, `npm run lint` exit 0 with the same 9 baseline warnings, `npm run build` 0, strict e2e `tsc` 0, `npm run test:harness` 87/87, then ONE canonical `npm run test:e2e:all`: Chromium 13/13 and journeys 4/4, exit 0, no retry (`e2e-all-p-round.txt`). `dotnet format --verify-no-changes`: the recorded baseline (153 diagnostics in the same 17 pre-existing files), none in this diff. `npm audit`: only the existing `source-map-js` advisory; `dotnet list package --vulnerable --include-transitive`: none. Whitespace/NUL/EOF/CR scan of 68 changed files: clean apart from the generated client's NSwag CRLF form without a final newline, as at HEAD; 288 local Markdown links in 9 changed documents: 0 broken.
- Inventory of this round (67 paths besides the planner record and this file: 41 modified and 26 added; the previous return had 51 slice paths). Production: `LocalCommitStorage`, `LocalCommitGit.Preparer`, `LocalCommitGit.Observation`, `LocalCommitGit.Repository` and the one call in `LocalCommitGit.IndexEffects`. Tests added: `LocalCommitGitPreparationIsolationTests`, `LocalCommitStorageOwnershipTests` (Infrastructure) and `LocalCommitCompetingPreparationTests` (Api). Tests adjusted for the new layout without weakening any assertion: both `LocalCommitScene` helpers (an optional preparer and token, `ArtifactLeaf`, `StorageLeaves`), `LocalCommitGitExecutionTests`, `LocalCommitGitObservationTests`, and the Api `LocalCommitEndpointTests`, `LocalCommitPersistenceFaultTests`, `LocalCommitRestartRecoveryTests` and `LocalCommitSafeReleaseTests`. Documentation: `docs/architecture/data-and-recovery.md` (preparation ownership, legacy paths, the inert-artifact limit). ADR-0029 is untouched.
- Retained versus fresh. Fresh in this round: everything listed above. Retained, not rerun: the frontend mutation matrix (30 of 32 killed, two equivalent survivors) and the backend review-set mutation matrix (19 of 20, one equivalent) of the earlier rounds, because no file those matrices target changed; the original failing runs of the first return (the Api HTTP 500 in two full runs and the Infrastructure cancellation failure) stay preserved in the first-return section, with their diagnoses in R3a and R3b. This round's passing full Api and Infrastructure runs are not offered as proof of the race being closed.
- Remaining limits of this correction. A preparation that is not admitted (the loser of a race, a refusal after a successful preparation, an abandoned request) leaves its prepared artifact behind: it is inert (no row names it, nothing reads it, no authority) and there is deliberately no janitor or deletion authority for it, so such files accumulate under `operations/` until an operator removes them. A crash between creating a leaf and its cleanup leaves inert scratch under `work/` as before. Cleanup of a recorded path never removes anything but the recorded file and its directory when empty, so foreign files in a legacy operation-named directory stay. A late identical request that reaches admission after its operation completed still gets the existing Conflict arbitration instead of a replay; that was not changed. The earlier limits of the first return and of R1-R3 still apply.
- Independent Codex correction review (2026-10-08; publication remains NO-GO). Build passed with 0 warnings/errors and identical client; Application selected 82/82, Infrastructure selected 69/69, Architecture 40/40 and the three focused frontend files 109/109 passed. The Api selection (Features.Runs.LocalCommit plus CheckpointApprovalEvidenceEndpointTests, --no-build --no-restore) failed: 134 passed, 1 failed, 0 skipped of 135 in 4m45s. Restart_after_each_boundary_decides_only_from_exact_recorded_evidence(AfterRefBeforePlan) expected the recorded proposed commit but observed a different tip at AssertFlightAsync:86; cleanup then threw IOException because prepared.index was still open at LocalCommitScene.Dispose:261. No unchanged rerun was made. CrashAtAsync currently waits only for method entry plus an 800 ms delay; the scripted decorator records entry before the real effect completes, so that wait does not establish the selected loss boundary. The actual native result of the failed run was not captured; its cause is not claimed proven. The planner authorizes bounded test-boundary synchronization and failure-safe fixture-owned capability release, with production Git/native/recovery authority unchanged. The executor full-suite and browser logs were inspected and their reported passing counts remain separate evidence; they do not erase this fresh failure. Codex corrected the P6 mutation wording above to distinguish an unexercised existing-destination case from an equivalent mutation; no executable code changed.
- R4, deterministic restart boundary and fixture cleanup (after the review above, "Latest correction review (2026-10-08): NO-GO on restart-boundary evidence" in `planner-handoff.md`, which this slice does not edit; implementation authorization only, no staging, commit, push or next slice). Still an UNSTAGED, UNCOMMITTED and UNPUSHED diff. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `426463717c0e724780b511763681630c1a5b63a7`; empty index; 69 paths (43 modified tracked, 26 untracked); planner record `3c38e4f9c5726559b8b3c51d3e6e74ab2a31e702b42cd22f8602e6f52fa06ecb`, this file `94f68824b0fa216b440112b8d2b699bd3d18e3f1a018fe2f963ea8fc12426d87` (Codex's factual edits above are kept verbatim), ADR-0029 `cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294` and the generated client `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac`; no discrepancy. At the end the planner record and ADR-0029 are unchanged, the client was deleted and regenerated by a clean `dotnet build --no-incremental -m:1` to the same bytes, and nothing is staged. No production file changed in R4: it is a test-fixture correction only.
- R4 failed selection, preserved. Codex's independent Api selection (`Features.Runs.LocalCommit` plus `CheckpointApprovalEvidenceEndpointTests`, `--no-build --no-restore`) failed once: 134 passed, 1 failed, 0 skipped of 135 (see the Codex bullet above): `Restart_after_each_boundary_decides_only_from_exact_recorded_evidence(AfterRefBeforePlan)`, expected recorded commit `901a6a8b…`, observed tip `c9087bb3…`, then a teardown `IOException` on the still-open `prepared.index`. That result was not rerun for a green outcome, and it is not claimed to be a production ref or recovery defect, nor to be disproven: the native result of that run was never captured. My own earlier full Api runs (1322, 1327, 1329) passed and do not erase it. The synchronization defect that the code establishes: `CrashAtAsync` waited for the scripted repository's record of the method ENTRY plus a fixed 800 ms, while the decorator recorded `PromoteRefAsync` before awaiting the real effect, so the host could be stopped while the real reference effect was still outstanding.
- R4 fix (tests and the scripted repository helper only). `ScriptedLocalCommitRepository` gains `ArmLoss(LossPoint)` with five points (`BeforeAcquire`, `BeforePromoteRef`, `AfterPromoteRef`, `BeforePromoteHeldIndex`, `AfterPromoteHeldIndex`) and a `LossReached` task. The loss is signalled at the selected point itself and only there: a before-effect point at the hook that precedes the real effect (the effect is never started), an after-effect point only after the REAL effect has returned and its result was captured. The `LossReport` says whether the point was satisfied (an after-effect point needs the real result to be `Promoted`; a refusal or failure is reported as not satisfied with its bounded outcome and reason, at most 200 characters, and never treated as a promoted state) and how many real effects were still in flight (`OutstandingEffects`, counted from just before an effect to just after it returns). Method entry signals nothing. Each effect is wrapped so the count is exact, the loss then unwinds with the same `OperationCanceledException` as before, and any decorator call made after the loss is listed in `CallsAfterLoss`. `CrashAtAsync` now maps each of the six boundaries to its loss point (`BeforeMarker` has no execution and needs none), waits for `LossReached` with a 60-second bound and fails loudly with the calls seen if it never arrives, requires `Satisfied` (the failure message carries the bounded diagnostic) and zero outstanding effects, and after stopping the host requires zero outstanding effects and an empty `CallsAfterLoss`, so no effect or further call exists when the restart begins. The 800 ms delay, the entry-based wait and the old `Lose`/`LoseAfter` helpers are gone; no sleep, retry, skip, serialization or weakened assertion was added, and every ref, index, receipt, journal and recovery assertion of the six boundaries and of the other restart tests is unchanged.
- R4 failure-safe cleanup. `LocalCommitTestBase` now remembers every scripted repository (`StartScripted`) and, in `Dispose` after the hosts have been stopped, releases each retained capability through the repository's own release API (`ReleaseHeldIndexEffectsAsync` via `ReleaseRetainedAsync`), so a failed assertion can no longer leave the lock and prepared-artifact handles open and make the scene teardown throw a second time. That API deletes a lock only through the handle that created it and refuses (returns false, touches nothing) a capability that is not held, was already released or whose lock was replaced; nothing is adopted and nothing is deleted by pathname. The simulated foreign locks that the restart tests place are never touched by it, and they stay in place across the restart assertions until the scene is removed.
- R4 regressions. `ScriptedLocalCommitRepositoryTests` (10 cases, no host and no Git: a blocking fake repository makes each interleaving deterministic): method entry alone never satisfies either after-effect boundary while the real effect is outstanding (the call is recorded, one effect is in flight, nothing is signalled, then finishing the effect signals with zero outstanding), a refusal or a failure of the real effect (`NotPromoted` and `Ambiguous` at both points) is reported with its reason and is not a reached boundary, the three before-effect points signal at the hook and never start the effect, and a call made after the loss is listed. In `LocalCommitRestartRecoveryTests`: `Failure_cleanup_releases_the_retained_capability_so_teardown_cannot_fail_a_second_time` (a second fixture instance is crashed at `AfterAcquireBeforeRef`, holds the live lock, and is torn down without any explicit release; the teardown must not throw and the scene must be gone) and `Releasing_the_retained_capability_releases_only_what_the_fixture_owns` (a foreign reference lock beside the owned index lock is left exactly as it was, the second release is refused, and a lock that appears at the same pathname afterwards is neither adopted nor removed). `Restart_after_each_boundary_decides_only_from_exact_recorded_evidence` still covers all six boundaries and all other restart tests are unchanged except for the shared helper.
- R4 mutation evidence (guarded: the three targets hashed before and after each mutant and restored from a byte copy, each mutant rebuilt non-shared first, a failed build is invalid and its tests are never run, all hashes restored at the end): 8 mutants, 8 killed. Completed-boundary signalling replaced by entry signalling for the reference effect (3 failing tests) and for the held-index effect (3), a refusal counted as a reached boundary (4), in-flight effects not counted (8), the failure-safe cleanup removed (the teardown regression), the release ignoring ownership (2), the after-reference boundary placed before the effect (the `AfterRefBeforePlan` restart case), and calls after the loss not recorded (1). Results: `mutation-results-backend-r4.txt`, outside the repository.
- R4 validation. Affected first: the scripted-repository and restart classes 42/42, including all six boundaries (the first run after the fix). Then `dotnet build --no-incremental -m:1` 0 warnings/0 errors with the client regenerated byte-identical, Architecture 40/40, and the full Api suite 1341/1341 (was 1329; plus the 10 helper cases and the 2 cleanup cases), `dotnet format --verify-no-changes` at the recorded baseline (153 diagnostics in the same 17 pre-existing files, none in this diff). Retained, not rerun, with exact scope: Domain 1277, Application 4686, Infrastructure 1624 passed + 4 skipped, the frontend Vitest 2346, typecheck, lint (9 baseline warnings), build, strict e2e `tsc`, harness 87 and the canonical `test:e2e:all` (Chromium 13, journeys 4), the dependency audits, and every mutation matrix of the earlier rounds, because R4 changed no production, Domain, Application, Infrastructure or frontend file and only Api test-support files and tests. The 1341-test Api run is one run on this tree; the earlier Codex failure is a different run that is preserved above, and a repeated green is not offered as proof of the boundary, which the signalling regressions and the mutation matrix establish.
- R4 inventory (this round touched four existing Api test-support or test files and added one, plus this file): `ScriptedLocalCommitRepository.cs`, `LocalCommitTestBase.cs` (the failure-safe release) and its partial `LocalCommitTestBase.Support.cs` (scripts registered) are newly modified, `LocalCommitRestartRecoveryTests.cs` was already modified and is changed again, and `ScriptedLocalCommitRepositoryTests.cs` is added. The slice now has 73 status paths (46 modified tracked and 27 untracked), 71 of them slice paths besides the planner record and this file. Production, ADR-0029, the planner record and the generated client are unchanged.
- R4 remaining limits. The cause of Codex's one failed run is still unproven: with the boundary now synchronized the native outcome would be reported if the real effect did not return `Promoted`, but no production ref or recovery defect is claimed fixed or disproven. If a future run reports an unsatisfied boundary, the bounded reason in that failure is the evidence to hand to the planner. The release at teardown is best effort for `IOException`, `ObjectDisposedException` and `UnauthorizedAccessException` only, and a retained capability that production code itself already disposed is simply refused. Every other limit above stands.
- R5, one top-level C# type per file (mechanical organization correction after the review "R5: one top-level C# type per file" in `planner-handoff.md`, which this slice does not edit; ENGINEERING.md requires one top-level type per file; implementation authorization only, no staging, commit, push or next slice). Still an UNSTAGED, UNCOMMITTED and UNPUSHED diff. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `426463717c0e724780b511763681630c1a5b63a7`; empty index; 73 paths (46 modified tracked, 27 untracked); planner record `9c8e9dec170b28e1669745b02b5d71288b8c9646726ace88e466ad895dd5356d`; no discrepancy. `LossPoint` and `LossReport`, which R4 had declared beside the decorator in `ScriptedLocalCommitRepository.cs`, now each live in their own file in the same folder: `LossPoint.cs` (`public enum LossPoint`) and `LossReport.cs` (`internal sealed record LossReport`). Documentation comments, namespace, visibility, declarations and members are moved verbatim; the decorator stays in its existing file and no other type moved. A byte comparison confirmed that the two new files plus the remaining decorator file reproduce the previous file exactly (the only difference being the removal of the two blocks), and the unchanged logic was not touched. Fresh validation of this split, run sequentially: `dotnet build --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors (the test assembly was rebuilt after the split), the `ScriptedLocalCommitRepositoryTests` plus `LocalCommitRestartRecoveryTests` selection 42/42 (0 skipped, all six restart boundaries), Architecture 40/40, tracked and untracked whitespace/NUL/EOF hygiene clean, and the planner record, ADR-0029 and the generated client verified against their protected hashes. Retained, not rerun, with their recorded scope, because the split changes no logic: the full Api run 1341/1341, the eight-mutant R4 matrix and every earlier validation and mutation result above. No failure occurred and nothing was retried. Inventory after R5: 75 status paths (46 modified tracked and 29 untracked), 73 of them slice paths besides the planner record and this file; `LossPoint.cs` and `LossReport.cs` are the two new files. The R4 inventory above is the earlier snapshot.
- Correction round R1-R3 (after Codex's NO-GO of the first return; the planner decision is in `planner-handoff.md`, which this slice does not edit). Still an UNSTAGED, UNCOMMITTED and UNPUSHED diff; no publication authority exists. Preflight verified once before editing: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `426463717c0e724780b511763681630c1a5b63a7`; empty index; 47 paths (26 modified tracked, 21 untracked); `planner-handoff.md` `47c84942495982e9dc44a58b9982d05d77b84d6f57e17b9cbd8322bb42fac571`, ADR-0029 `cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294` and the generated client `46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac`; no discrepancy. At the end the planner record and ADR-0029 are byte-identical to those hashes and the client was deleted and regenerated by `dotnet build --no-incremental -m:1` to the same bytes (a first parallel `--no-incremental` attempt failed with `CS0006`, a project-reference race; it was an invalid build, nothing was run from it, and it was repeated serially).
- R1, the action is owned by the committed bundle's lifetime (`useCheckpointApprovalEvidence`). The source lifetime keeps owning the reads. A second lifetime, derived from the source key and an epoch, owns the approval: its `approving`, `error`, `accepted`, the pending guard and the continuation. The epoch advances when a read commits a bundle whose key differs from the newest committed one, so a replacement ends the old lifetime and a return to an earlier member set (A to B to A) begins a new one (equal keys alone never revive an obsolete callback), while a refresh that reads equal members keeps it. An obsolete completion writes no error onto, clears no guard of and refreshes nothing for its replacement; ownership is checked again before the deferred review-list refresh (microtask); the pending guard and the accepted set are keyed by lifetime object, so an older completion cannot clear a newer guard; a handler, including a retained callback, never submits a bundle whose approval it already had accepted, and an accepted server operation stays real (nothing is retried and no failure is reported for it). The earlier `committed.bundleKey` comparison became redundant with the lifetime check and was removed rather than kept as an unprovable guard.
- R2, the full source identity. `ApprovalBundleSource` now carries project, workspace, checkpoint, checkpoint number and fingerprint; `toApprovalSource` builds it from the observed evidence (`gitWorkspaceId`, `checkpointId`, `checkpointNumber`, `fingerprintSha256`) and returns null when any fact is missing, blank or malformed, so nothing is read, offered or approved and the panel says the source identity is incomplete. `toApprovalBundle` requires the response to repeat every one of those facts exactly (the previous decoder ignored `workspaceId` and accepted any positive checkpoint number); the lifetime key and the bundle key contain all of them. Nothing is repaired or retargeted. Existing fixtures were upgraded to complete valid contracts (workspace and checkpoint number on the evidence and on the bundle).
- Regressions (ordinary repository tests; the reviewer's ignored scratch harness is not part of the delivery). `useCheckpointApprovalEvidence.test.tsx` (22 to 40): obsolete pending failure and obsolete accepted completion across member replacement, A to B to A with a retained callback, an obsolete A completion while the newer A is pending, a newer submission while an older one completes, the accepted bundle through a retained callback (also after an equal refresh), the deferred refresh ownership (unmount between the acceptance and the callback), unchanged-owner controls (an equal refresh keeps the pending approval; an unchanged failure stays visible and an explicit retry works), unmount, and the identity cases (foreign and absent workspace, checkpoint number mismatch and absence, replaced workspace or checkpoint number, valid control). `checkpointApprovalBundle.test.ts` (identity, `toApprovalSource`, key sensitivity to every fact), `CheckpointReviewApproveAll.test.tsx` (19 to 32: the same identity cases through the real panel, an observed source without workspace or number reading no bundle, member replacement writing nothing onto the replacement, a retained earlier bundle after A to B to A). Failing-first, stated plainly: the reviewer's independent harness (retained, output `reviewer-output-r3.txt` under the ignored `node_modules/.cache/codex-approval-review-20261008/`) reproduced six failing cases against the first return; I did not rerun my converted tests against the old code (the old hook cannot consume the new source type). Their discriminating power is shown instead by the mutation matrix below, which reintroduces each old behavior and requires a failing test.
- R3a, the local-commit HTTP 500 is diagnosed (no production change; this is the stop gate). `ServerFailureDiagnostics.cs` adds `CapturedServerErrors`, an `ILoggerProvider` registered by `LocalCommitHost` that keeps at most 6 server errors of at most 1800 characters (type, message and the top of the stack; the launch secret, the scene, database and temp roots and unknown absolute directories are scrubbed), and `DescribeAsync(response)` (status plus the first 1200 sanitized characters of the body plus those errors). `Concurrent_identical_and_competing_requests_admit_exactly_one_operation` reports any unexpected status with that evidence before its unchanged assertions (counts of OK and Conflict, one operation, one event); nothing was serialized, retried, delayed or weakened. `ServerFailureDiagnosticsTests` (5) prove the scrubbing, the bounds and, through the real host with a throwing preparer, a real 500 described from its response and logged exception without the secret. A temporary harness (removed; output `diag-concurrent-loop-1.txt` outside the repository) ran the same body 150 times as six parallel classes: 141 passed and 9 failed. Every failure was exactly one HTTP 500 on one of the two requests that carry the SAME operation identifier (0 of 300 requests with distinct identifiers failed), and every captured exception is `System.IO.DirectoryNotFoundException` from `File.Copy` at `LocalCommitGit.Preparer.cs:219` (8 times `work\<operation>\promotion.index`, once `operations\<operation>\prepared.index`). Cause: `RequestLocalCommitCommandHandler` prepares before its transaction, `LocalCommitGit.PrepareOnWindowsAsync` derives its `work` and `operations` directories from the operation identifier alone and its `finally` deletes the work directory (and the operation directory when not prepared), so two concurrent identical requests share those directories and one request's cleanup removes a directory the other is still copying into; the unhandled exception becomes the 500. Exactly one operation is still admitted (the test's own count assertion held in every run), so nothing is corrupted, but an identical double submission can answer 500. The preparer, the admission handler and the test are unchanged by this slice (Git shows no change under `src/backend/DevalCopilot.Infrastructure` or `src/backend/DevalCopilot.Application/Features/Runs`), so this is pre-existing. A fix was a production Git change outside this slice and was left for the planner at that point; the planner then authorized the bounded correction (isolated per-preparation leaves), recorded in the preparation-ownership bullets above. Until that correction that test could fail intermittently under load; a passing full Api run (1327/1327 in the R-round) did not prove otherwise (about 94 percent of runs passed).
- R3b, the Infrastructure cancellation failure is diagnosed and the test made deterministic (no production change). The original test armed `CancelAfter(1s)` before its own preparation (manifest sealing, script write, manifest read, scratch directory, schema write). The retained stack ended at `CodexProcessInvoker.cs:141`, the schema write, before any child exists; the isolated passes never proved a started child. Temporary diagnostics (removed; `diag-cancellation-phase.txt` outside the repository) reproduced that exact site deterministically: cancelling the token right after the manifest read, through a forwarding artifact store, throws `TaskCanceledException` at `CodexProcessInvoker.cs:141` with zero process launches, and a one-second timer with a 1.5-second preparation does the same at the manifest read. So the failed run was the pre-existing preparation phase (cancellation before launch throws and produces no process evidence), not a cancelled running process. The test now writes a start marker from the real child (new fixture mode `sleep-after-marker`, a path relative to the working directory) and cancels only after a bounded, event-driven `FileSystemWatcher` wait sees it, so its Cancelled evidence belongs to a started process. Removing that gate reproduces the original `TaskCanceledException` (hash-guarded mutation, restored byte-identical), and the gated test passed 12 isolated reruns and the full suite. Two characterization tests state the other phase without inventing evidence: an already-cancelled token, and cancellation between the manifest read and the launch (the failed run's exact phase), both throw `OperationCanceledException`, launch no process and carry no evidence. Whether pre-launch cancellation should instead be reported as Cancelled evidence is a product decision this slice does not make.
- Documentation. ADR-0030 no longer claims relational members are persisted in `CommandNumber` order: the rows carry no sequence, the host derives the canonical presentation (the bundle and the review-history projection order by command number, then execution number) and no sequence column or migration is added; the same correction is in the workflow model and the collaboration protocol. ADR-0030, the cockpit specification and the engineering context now describe the two lifetimes (reads by the source, the approval action by the committed bundle), the full source identity and the re-check before the deferred refresh.
- Correction-round validation, fresh unless marked. Affected checks first: cockpit Vitest (147 files, 2313 tests at that point), `AgentProcessEvidenceAdapterTests` 12/12, `ServerFailureDiagnosticsTests` 5/5. Then `dotnet build --no-incremental -m:1` 0 warnings/0 errors, and sequentially with `--no-build`: Domain 1277/1277, Application 4686/4686, Architecture 40/40, Infrastructure 1595 passed + 4 skipped (the same privilege-dependent skips; the previous return had 1592 passed + 4 skipped + 1 failed, which is preserved in the first-return section below), Api 1327/1327 (was 1322 plus the five new diagnostics tests). Frontend: `npx vitest run` 2346/2346 in 153 files, `npm run typecheck` 0, `npm run lint` exit 0 with the same 9 baseline warnings in unrelated hooks, `npm run build` 0, the strict e2e `tsc` 0, `npm run test:harness` 87/87, then ONE canonical `npm run test:e2e:all`: Chromium 13/13 and journeys 4/4, exit 0 (`e2e-all-r-round.txt`, outside the repository). `dotnet format --verify-no-changes`: the recorded baseline (153 diagnostics in 17 pre-existing files), none in this diff. `npm audit`: only the existing `source-map-js` advisory; `dotnet list package --vulnerable --include-transitive`: none. Whitespace/NUL/EOF/CR scan of 52 changed files and `git diff --check`: clean apart from the generated client's NSwag CRLF form without a final newline, as at HEAD; 287 local Markdown links in 8 changed documents: 0 broken.
- Correction-round mutation evidence (frontend, rebuilt control then each mutant, target hashed before and after and restored from a byte copy): 32 mutants, 30 killed, 2 survivors that are equivalent. Killed: action owned by the source only (9 failing tests), member replacement never ending the lifetime (9), an equal refresh ending it (3), an accepted bundle resubmittable (2), the acceptance never recorded (2), the deferred refresh not re-checked (1), the retained callback's lifetime not checked (2), a completion clearing a newer guard (1), the pending guard removed (31), a foreign workspace accepted (8), a checkpoint number mismatch accepted (9), the lifetime ignoring the workspace (4), the checkpoint number (3), the fingerprint (3) or the checkpoint (3), an absent workspace (4) or checkpoint number (5) admitted, the panel ignoring the incomplete-identity message (4), and the earlier set (stale read, overlapping reads, member cap, command order, same bundle approved twice, unsettled bundle offered, pending approval not blocking decisions, follow-up failure failing the approval, replaced lifetime continuing, scalar sent with the set). Survivors: the panel's own workspace comparison (the decoder already refuses a foreign workspace) and the Human check of `canApproveAll` (the group renders only for a Human reviewer). No backend production code changed in this round, so the first-return backend matrix (20 mutants, 19 killed, one equivalent) is retained, not rerun. The Infrastructure test gate was mutation-checked as described under R3b. Results: `mutation-results-frontend-r1r2-final.txt` (outside the repository).
- Correction-round limits. The identical-operation preparation race (R3a) was diagnosed here and is addressed by the preparation-ownership correction above. Pre-launch cancellation semantics are characterized, not changed. The two equivalent frontend survivors are redundant defenses. Everything in the remaining limits of the first return still applies.
- Delivered outcome ([ADR-0030](../decisions/0030-approve-the-complete-verification-set-as-one-human-decision.md)): a project with two or more enabled verification recipes can obtain ONE explicit Human approval of the complete current verification set through the existing protected review operation, then use the existing explicit local commit unchanged, and retain the exact memberships after a reload. Before this slice that case was refused with `local_commit.membership_mismatch`: the manual request, command and panel could submit only one execution while the Agent review and the local-commit authority require every enabled recipe.
  - `POST /api/projects/{projectId}/reviews` accepts the optional `verificationExecutionIds` (at most 32 unique nonempty UUIDs; an 8 KiB body through `[RequestSizeLimit]`, proven on real Kestrel). Null or omitted keeps the legacy scalar form unchanged. A scalar plus an array (even an empty one) is refused as `reviews.evidence_forms_ambiguous`, never merged, deduplicated or truncated; more than 32, a duplicate or an empty UUID is `reviews.evidence_selection_invalid`; a non-Human reviewer is `reviews.evidence_set_requires_human`; a decided review needs 1 to 32, an empty array is only `Pending`, and a non-empty `Pending` stays the existing `reviews.pending_cannot_include_evidence`. A non-UUID, a null element or a non-array value is a model-binding 400. The new refusals are Problem Details (422 for the request-shape codes, like the existing `Error.Failure`), no refusal echoes a submitted value, and every refusal persists nothing.
  - `Approved` in the set form requires exactly one latest, coherent clean `Passed` execution for every currently enabled recipe (same project, workspace and workspace path; the current checkpoint and fingerprint; exit 0, dispatched and completed; completion fingerprint equal to the checkpoint's; command snapshot equal to the current recipe), else `reviews.approval_requires_complete_verification_set` (or the existing `reviews.approval_requires_passed_verification` when a submitted member is not Passed). `ChangesRequested` and `Escalated` may cite a coherent terminal set with at most one execution per recipe. One review and all of its members are saved by the existing manual transaction; Git is observed before it, and after the write lock the source, the selected executions and the enabled recipes with their latest executions are re-read untracked. A changed selection is refused, never retargeted. The shared policies are the internal `CheckpointReviewSource` (the existing source gates, now used by both operations), `CompleteVerificationSet` (the rules above, per recipe through the latest-execution query, never a history page) and `CheckpointReviewEvidenceSelection` (request shape).
  - Stored members have no order and EF inserts same-table rows by key, so a persisted-row order is not a contract (an early version of my own test asserted `ORDER BY rowid` and was flaky, see the disclosures). The canonical `CommandNumber` order is therefore applied where it is observable: the bundle, and now the existing review-history projection (`GetProjectCheckpointReviewsQueryHandler`, one extra bounded read of the command numbers, evidence ordered by command number then execution number).
  - `GET /api/projects/{projectId}/checkpoints/{checkpointId}/approval-evidence` (`GetCheckpointApprovalEvidenceQuery`, protected, read-only) returns the source identities and fingerprint and, per enabled recipe in command order, its command and execution identities, recipe label and numbers, or a fixed refusal (`approval_evidence.no_enabled_recipes`, `.too_many_recipes` for more than 32, `.verification_incomplete`, or the review source codes). It observes Git once outside any transaction, reads the source and set again afterwards, writes nothing, never opens a transaction and authorizes nothing.
  - Cockpit: the "Complete verification set" group of the checkpoint-review panel (Human reviewer only) shows the exact bundle and "Approve all enabled checks". It is offered only for settled, successful source and bundle reads naming the same project, workspace, checkpoint, checkpoint number and fingerprint (any missing or mismatching fact withholds it and is never repaired); pending, failed, malformed or inconsistent reads, a refresh in flight, another pending decision, a non-Human reviewer and an already accepted bundle withhold it. `useCheckpointApprovalEvidence` owns the reads by the committed source lifetime (A to B to A, unmount, overlapping reads) and the approval action, its pending guard, error, continuation and accepted result by the narrower committed bundle lifetime (replaced members end it, a return to an earlier member set is a new one, an equal refresh keeps it); a retained handler of an earlier or an already accepted bundle starts no request; an accepted POST is never reported as failed because the follow-up review read failed and is never retried; `checkpointApprovalBundle.ts` validates and orders the response. The legacy single-run buttons are unchanged and the panel states that Approve covers only the selected run.
- Inventory (correction round: 51 paths besides the planner record and this file, 28 modified and 23 added; the first return had 45, 24 and 21. The correction round additionally touches `LocalCommitHost.cs`, `LocalCommitEndpointTests.cs`, the new `ServerFailureDiagnostics.cs` and `ServerFailureDiagnosticsTests.cs` in the Api tests, `AgentProcessEvidenceAdapterTests.cs` and the `ProcessExecutionFixture` `Program.cs` in the Infrastructure tests, and edits the frontend hook, bundle module, panel, their tests, ADR-0030 and the four specification documents above). Backend: `RecordCheckpointReviewCommand`/`Handler`, the new `CheckpointReviewEvidenceSelection`, `CheckpointReviewSource`, `CompleteVerificationSet`, the new `GetCheckpointApprovalEvidence` query slice (query, handler and two result records), the Api endpoint, request and the new endpoint with its two response records, and `GetProjectCheckpointReviewsQueryHandler` (ordering). Frontend: regenerated `api-client.ts` (additive), `clients.ts`, `checkpointApprovalBundle.ts`, `useCheckpointApprovalEvidence.ts`, `CheckpointReviewPanel.tsx` and their tests, the generated-client contract test, the e2e `checkpointReviewFixture.ts`, `wire-checkpoint-review.spec.ts`, `journey/journeyDb.ts`, `collaboration.journey.ts`, `escalated-plan.journey.ts` (exact legacy `Approve` locator only) and the harness fixture schema. Tests: Application `RecordCheckpointReviewCompleteSetTests` and `GetCheckpointApprovalEvidenceQueryHandlerTests`, Infrastructure `RecordCheckpointReviewTransactionBoundaryTests` (four added), Api `CheckpointApprovalEvidenceEndpointTests`, `LocalCommitCompleteSetTests`, `LocalCommitLineage` (complete-set seeding) and a comment correction in `LocalCommitGateTests`. Documentation: ADR-0030, the ADR index, `engineering-context.md` (ADR-0030, and ADR-0029 is no longer described as selected-but-unimplemented), `mvp-delivery-plan.md` (the local commit is no longer described as selected work), the cockpit specification, the workflow model and the collaboration protocol.
- Failing-first evidence: `LocalCommitCompleteSetRedTests` (temporary, removed after the run) seeded two enabled recipes with the only Human approval the previous contract could express and asserted that the run is eligible for the local commit; it failed with `local_commit.membership_mismatch` (output kept outside the repository as `red1-membership-mismatch.txt`). It is replaced by `LocalCommitCompleteSetTests`, whose positive cases fail without the contract, while the legacy single-member case is kept as a refused control. A first red attempt (`dotnet test`) hit a transient `CS2012` lock on a project output; it was repeated after shutting down the build servers.
- Test evidence added: Application (real file-backed SQLite, populated tracker, both race moments DuringCapture and BeforeBegin): the complete approval, the one-recipe set equal to the scalar, history beyond 20 executions, subset, extra, disabled, repeated, missing, older-pass-behind-newer-failure, running and non-Passed members, foreign and other-checkpoint executions, ambiguity, 33 identifiers, duplicate, empty identifier, empty decided set, non-Human set, recipe enabled, disabled or reconfigured, newer execution, changed completion fingerprint, workspace path, project, deleted member, reserved workspace, newer checkpoint, failed save, failed commit and a database-refused second member rolling the whole review back. Query: bundle shape and order, the 20-window case (a recipe whose only pass is behind 30 newer runs of another), no writes, no transaction, drift, each refusal code, 32 versus 33 recipes, and fixed refusal text. Infrastructure: the real mediator pipeline outside any ambient transaction, newer-execution and recipe-enabled races from another connection, and member-refusal rollback. Api (real host): authentication, binding, count, duplicate, empty, 33, body bound on real Kestrel (a free port, not the shared default), foreign project, subset, the legacy scalar, the bundle shape; and through the real host and real Git: two enabled recipes, the real CodeReviewer lineage, the complete approval from the bundle, the real local commit and a restart (a second host over the same database and repository), history beyond 20, one-recipe equivalence, a later Human ChangesRequested or Pending set review still blocking delivery, a second-member change at the locked seam (`verification_not_current`, `active_work`, `membership_mismatch`), a recipe disabled after approval, a second approved set at the locked admission seam (`authority_changed`) and reserved-workspace refusal of a set review, the bundle and a new recipe. Frontend: `checkpointApprovalBundle` (identity, 1 to 32, order, uniqueness, malformed), the hook (settled read, malformed and inconsistent responses, overlapping reads, a new generation, A to B to A, a changed checkpoint or fingerprint of the same project, unmount, replacement mid-request, pending guard, no automatic retry, accepted result across a throwing, rejecting or never-resolving follow-up) and the real panel (exact bundle submitted, withheld states, busy exclusion both ways, accepted-but-refresh-failed, replacement and A to B to A) and the generated-client contract.
- Browser: the existing production-written `collaboration.journey.ts` now configures a second distinct recipe through the rendered form, runs each recipe through its own Run control (four verification executions: both fail, then both pass after the Codex diagnosis and the guided Claude correction), obtains the real Codex double's approval of both, approves both through the rendered "Approve all enabled checks" (the page shows `#1 Candidate total check · execution #3`, `#2 Candidate lint check · execution #4`; HTTP 201), requests the existing explicit local commit (HTTP 200, `Completed. Local commit only — not pushed.`) and reloads. It reads back the two relational Human members and the two Agent members (same recipes and executions), the two `local_commit_authority_members` verification rows in command order, the unchanged source repository, refs and files, and the exact doubles' log (`verify:failed` twice then `verify:passed` twice). The wire spec adds the generated client over real HTTP (bundle, refusals, the complete approval) against the actual host. No raw-SQL approval and no fake Git substitutes for any of it; the wire fixture is the existing owned raw-SQL evidence fixture (its executions now carry the prepared workspace's own path so the complete-set coherence check applies).
- First-return validation (superseded by the correction-round validation above and kept as history): `dotnet build DevalCopilot.slnx --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors after deleting the generated client (reproduced byte-identical). `--no-build --no-restore`, sequentially: Domain 1277/1277 (unchanged), Application 4686/4686 (was 4604), Architecture 40/40, Infrastructure 1592 passed + 4 skipped + 1 FAILED of 1597 (was 1589 + 4 skipped; the failure is below), Api 1322/1322 on the final rerun (was 1286; see the failures below for the earlier runs). Frontend: `npx vitest run` 2300/2300 in 153 files (was 2232), `npm run typecheck` 0, `npm run lint` exit 0 with the 9 baseline warnings in unrelated hooks, `npm run build` 0 (existing chunk-size notice), strict e2e `tsc` (`--ignoreConfig --noEmit --strict --target es2023 --lib es2023,dom --module esnext --moduleResolution bundler --skipLibCheck --allowImportingTsExtensions --verbatimModuleSyntax --types node --noUnusedLocals --noUnusedParameters --erasableSyntaxOnly` over the 39 e2e files and both Playwright configurations) 0, `npm run test:harness` 87/87 with 0 skipped, then ONE canonical `npm run test:e2e:all`: Chromium 13/13 (was 12; the new wire test) and journeys 4/4, exit 0, no retry (`e2e-all-canonical.txt`, outside the repository). `dotnet format DevalCopilot.slnx --verify-no-changes` reports exactly the recorded baseline (153 diagnostics in the same 17 pre-existing files) and none in this diff. `npm audit`: only the existing `source-map-js` advisory that predates this diff; `dotnet list package --vulnerable --include-transitive`: none. `git diff --check`, a scan of every tracked and untracked changed file for trailing whitespace, NUL bytes, stray CR, final newline and blank line at the end (the generated client keeps its NSwag CRLF form and has no final newline, as at HEAD), and the 193 local Markdown links of the changed documents: clean. The `planner-handoff.md` and ADR-0029 hashes are unchanged.
- First-return failures and disclosures (preserved, not hidden; the Api 500 and the Infrastructure cancellation failure are diagnosed in R3a and R3b above): (1) the first Api test attempt hit a transient `CS2012` output lock (build servers shut down, then green). (2) My first Application ordering test asserted `ORDER BY rowid` and passed only by chance (3 of 6 isolated reruns failed): EF inserts same-table rows by key, so stored order is not observable. Found through the mutation matrix's strange positive-test failure; corrected as described above (history read model ordered by command number; tests assert that, 8 of 8 stable). (3) The first full Api run failed `SetCodexAccountUsageWarningEndpointTests.The_real_host_still_sets_clears_authenticates_and_enforces_the_strict_percent_converter` with `address already in use 127.0.0.1:5080`: my new Kestrel body-bound test used the same default port as the older Kestrel tests and runs in parallel with them. My test now uses an operating-system-chosen free port; the three Kestrel classes then passed together 3 of 3. (4) In that same first full run, and again in the first rerun, `LocalCommitEndpointTests.Concurrent_identical_and_competing_requests_admit_exactly_one_operation` saw one of its four concurrent requests answered 500 (the response status only; the body and the server exception were not captured). It passes alone 6 of 6, passes in the whole LocalCommit namespace with my new class (106/106), and passes in the whole Api suite WITHOUT my two new classes (1286/1286, the prior baseline); the full Api suite WITH them then passed three consecutive times (the first two of those runs also included a temporary diagnostic copy of that test that captured server logs and never failed; it was removed, and the third run is the one reported above). The root cause is UNPROVEN: no admission code changed, and the symptom is consistent with an existing latent race that my extra parallel load makes more likely, but I did not establish that and I did not change the test or the handler. (5) The full Infrastructure run failed `AgentProcessEvidenceAdapterTests.A_real_cancelled_codex_process_reports_cancelled_evidence_rather_than_throwing` with a `TaskCanceledException` from the real process adapter; it is unrelated code, passed 5 of 5 alone, and I did not rerun the whole Infrastructure suite to obtain a green number. (6) The output of both failing runs is kept outside the repository (`suite-Infrastructure.IntegrationTests.txt`, `suite-Api.IntegrationTests.txt`, `suite-Api-rerun.txt`).
- First-return mutation evidence (the backend results still apply because no backend production code changed in the correction round; the frontend matrix was rerun above) (each backend mutant rebuilt non-shared before its run, a failed build is an invalid mutant and its tests are never run, the target hashed before and after, restored from a byte copy, then the clean rebuild above): backend 20 mutants, 19 killed: complete-set check weakened to a superset (3 failed) and skipped (17), one member omitted (7), reservation check removed (6), binding to checkpoint and fingerprint removed (5), fresh source re-read skipped (4), observation fingerprint skipped in the command (2) and in the query (1), older execution trusted (15), snapshot timeout not compared (3), exit code not required (1), disabled recipes included (16), a latest-20 window (2), ambiguous forms merged (2), bound off by one (1), duplicates silently deduplicated (1), the history member order reversed (2), the endpoint ignoring the set (23) and the body bound removed (1). ONE SURVIVED and is equivalent, not a gap: reversing the handler's own member ordering changes nothing observable because the stored rows have no order and EF inserts them by key; the observable order is the read model's, which is killed. Frontend 17 mutants, 16 killed: a stale-bundle handler, a pending-read handler, the replaced lifetime continuing, the lifetime key ignoring the fingerprint or the checkpoint (killed only after I strengthened the lifetime test; the first matrix had two survivors there), a foreign fingerprint or checkpoint accepted, the pending set approval not blocking decisions, an unsettled bundle offered (killed only after I strengthened the panel test; the first matrix had it surviving because the source read was also pending), a follow-up failure failing the approval, the double-submit guard removed, overlapping reads unordered, the member cap, the command order and the same bundle approved twice, and the scalar sent with the set. ONE SURVIVED and is equivalent: removing the Human check from `canApproveAll` changes nothing because the whole group renders only for a Human reviewer. Results: `mutation-results-backend-final.txt`, `mutation-results-frontend-final.txt` (outside the repository).
- First-return retained versus fresh (the correction-round validation above is fresh on the final tree): everything above was run fresh on the final tree except the Infrastructure suite, which was not rerun after its one failure (no production or Infrastructure code changed after it), and the first full Api run and rerun, whose failures are kept as evidence rather than replaced.
- Remaining limits and risks: the complete approval is a point-in-time Human decision; a rerun, a recipe change or a newer execution makes it stale and the host refuses a stale set, the owner refreshes and approves again. Approving the same unchanged set twice records two Human facts (the second is not combined, and a second approved Human set after local-commit admission is the existing `authority_changed`). More than 32 enabled recipes cannot be approved as a set. The legacy scalar Approve is unchanged and, with several recipes, cannot deliver. A set review is Human only; no Agent approval is synthesized. Source observation is point-in-time; the reservation, recovery, Git and lock behavior, the mixed-Human-decision gate and all ADR-0029 limits are untouched, including Windows physical proof only, no remote publication and no autonomy. The `source-map-js` audit advisory predates this diff. The two unrelated test failures above are unexplained beyond what is stated. Increment 4 is not complete and no next slice is selected.

## Explicit local commit (2026-10-07)

- Status (second correction round, 2026-10-08, R6-R8 after Codex's NO-GO): review-ready candidate for Codex GO/NO-GO of the same unstaged, uncommitted and unpushed ADR-0029 slice. Nothing is staged, committed or pushed, no publication manifest was created and no next slice was selected. This round's executor preflight verified `main`, `HEAD`, local `origin/main` and live `refs/heads/main` at `a9e0e2b0b3b05bd54d8d45aec6258c035a02e930`, an empty index, **142 paths (29 modified tracked, 113 untracked)**, the planner handoff at `1d2c9262cd7cda48676372174cd83fddcc7de9985a83267e081e4fbb92760d4f` and ADR-0029 at `cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294` (both still byte-identical at the end; the generated client was deleted and regenerated by the Api build to the identical SHA-256 `e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969`). The final inventory is **149 paths: 29 modified tracked and 120 untracked**; the 7 additional paths are listed in the R6-R8 entry below. The remainder of this bullet is the PREVIOUS (2026-10-07) round, retained as history: nothing is staged, committed or pushed, and no acceptance, publication, next-slice selection or Increment 4/5/MVP completion is claimed. The executor preflight verified `main`, `HEAD`, local `origin/main` and live `refs/heads/main` (`git ls-remote`) at `a9e0e2b0b3b05bd54d8d45aec6258c035a02e930`, an empty index, and 111 paths (27 modified tracked, 84 untracked). The protected planner handoff (SHA-256 `ee9e967c0a0e6a907c540f56528a799e11dcc08dd1b21e6b059a33f038191a1c`) and ADR-0029 (`cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294`) are byte-identical; the generated client was deleted and regenerated by the Api build (NSwag) to identical bytes, SHA-256 `e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969`. The final inventory is **142 paths: 29 modified tracked and 113 untracked** (the 31 additional paths are this correction's types, tests, fixture change and documentation; the only previously tracked files added to the diff are the `ProcessExecutionFixture` program and `docs/architecture/data-and-recovery.md`).
- What is delivered: the protected `POST /api/runs/{runId}/local-commit` and read-only status, the focused operation/authority-member entities and four migrations (storage, seven write-seam guards of the `Committing` reservation, physical receipts, and the recreation of those guards over the whole open reservation, see R6), fresh untracked authority reads at pre-admission, the locked admission seam, the single-use execution marker and the execution seam, hosted execution, startup recovery before workspace reconciliation, exactly-once sequenced events, the completed-head chain, and the cockpit panel. Git work is an Infrastructure adapter behind the two existing Application ports (unchanged): an isolated index and raw blobs for the exact tree, a fixed converter-free observation view (`core.filemode=false`, `core.quotePath=true`, private Git directory, immutable attribute source), exclusive physically held index handles with no-replace renames, and a prepared Git reference transaction.
- Reference transaction (R1): `LocalCommitRefTransaction` owns one `update-ref --no-deref --stdin` child. The protocol is the exact LF/ASCII form: raw stdin bytes through the actual pipe, closed (real EOF) after the terminal command; stdout read as bytes with a 128-byte line and 1 KiB total limit where a CR, NUL, other control or non-ASCII byte, an overlong or unterminated line, EOF inside a line, a wrong acknowledgement, unsolicited output or a drained stderr over 4 KiB is a distinct, phase-labelled fault (`commit:ack_malformed`, `prepare:ack_timeout`, `commit:stderr_overflow`, ...). One 30-second transaction deadline covers launch, every exchange and ALL prepared-lock proof reads (the proof receives one disposed scope, never `CancellationToken.None`); each exchange has a 5-second phase budget; abort, close, termination and reaping run under an independent 5-second cleanup budget that the deadline and the caller cannot shorten (two fifths for the abort exchange, three quarters for the exit wait, the rest for reaping). Only the owned child and its tree are terminated; `commit` is sent at most once, never after the deadline or cancellation, never resent; a start failure proves no mutation only when `commit` was never sent and the child ended by itself. The proof under the prepared lock re-proves ownership, the live physical handles (the administrative HEAD is held read-only so Git's lock-and-rename rewrite of HEAD, raw writes, deletion and rename fail), the literal HEAD binding, a direct (non-symbolic) owned ref, and the exact parent and commit object, every Git read through the explicitly proven administrative directory (never rediscovered through the workspace `.git` file). Handles stay held through the ref effect, the index promotion and the final converter-free observation.
- Evidence classes. Historical failing evidence, retained: the original R1 red (`Expected: Not Promoted; Actual: Promoted` for the one-shot `update-ref`); the earlier private-view `CheckpointNotCurrent` failure; the first native test setup's `ERROR_ACCESS_DENIED` and the native terminator symptoms (success with a missing index; error 123); the earlier executor all-Infrastructure run whose summary was not captured (never counted); and this correction's owner baseline: a transitional owner (the new test surface over the previous internals) failed 30 of 33 protocol tests, accepting CR/CRLF acknowledgements, stderr overflow and unsolicited output, with seven tests stopped by their 25-30 second guards because their waits were unbounded. Incident, disclosed: a first mutation matrix was stopped while one mutant (CR stripping) was applied and that mutant stayed in the working tree; the next full Infrastructure run failed exactly its three protocol tests, which exposed it. The source was restored to its previously recorded SHA-256 (`120d6d55…`), every result of that matrix and of that suite run was discarded, and both were rerun from a hash-verified clean tree.
- Early-refusal control versus fresh race evidence: `Early_refusal_control_a_redirect_installed_before_execution_is_refused_before_any_transaction_starts` installs the redirect before `ExecuteAsync`, so the initial recursive HEAD read refuses before `LocalCommitRefTransaction` exists (it asserts no transaction phase was reached); it is a control, not evidence of the prepared-lock defense. The race is `A_redirect_installed_after_the_initial_checks_is_caught_by_the_prepared_direct_ref_proof`: an observer places the redirect at `before_start`, after the initial and acquisition checks, and it asserts the phases `before_start, launched, start_sent, start_acknowledged, update_sent, prepare_sent, prepared, proof_started, proof_fault:ref_not_direct, proof_failed, abort_sent, abort_acknowledged, aborted`, no `commit_sent`, the foreign redirect and main's HEAD/ref/index/files intact, and no leftover lock. The normal-success control asserts all twelve phases through `committed`. Companion real-Git cases: a stale parent between the checks and prepare (provider-refused, retained), competing Git writers to the branch and HEAD refused while prepared and the commit still succeeding, HEAD change attempts refused before, during and after the reference effect and during the final observation, a workspace `.git` file redirected mid-proof that proof reads ignore (and that the index effect then refuses), a stalled proof read bounded by the deadline then aborted, and caller cancellation while prepared.
- Previous round (2026-10-07), fresh results, superseded by the final results of the R6-R8 correction below and retained as history: `dotnet build DevalCopilot.slnx` 0 warnings, 0 errors; Domain 1277 passed (68 new); Application 4549 passed (61 new); Architecture 40 passed; Infrastructure 1494 passed, 4 skipped (the pre-existing symbolic-link and reparse-point cases that need a privilege this session lacks, unchanged), 1498 total; Api 1268 passed. Frontend: typecheck and lint exit 0 (the 9 existing `react(set-state-in-effect)` warnings are in unrelated hooks), Vitest 2232 passed, build exit 0, harness 87/87, Chromium 12/12 and journeys 4/4 in one canonical `test:e2e:all` whose collaboration journey ends in the rendered Commit locally control, a real host commit and a reload. `dotnet format --verify-no-changes` reports only the 17 pre-existing files outside this diff (the three diff files it initially flagged were whitespace-formatted); `dotnet list package --vulnerable` reports none; `npm audit` reports one pre-existing high advisory in the transitive `source-map-js` (no manifest changed).
- New or extended suites by risk: owner protocol through the repository's deterministic `ProcessExecutionFixture` (33: healthy commit/abort with real EOF, exactly-once commands, every malformed, overlong, missing-newline, EOF, stderr, extra-output, exit-code, stalled-exit, cancellation, deadline and cleanup case, launch and start/prepare faults, descendant reaping); real-Git transaction (10); physical handles (17: held HEAD/index/artifact/lock refusals, hard-link and oversize refusals, literal HEAD binding, 128-bit identity form, no-replace promotion, an occupied quarantine, a foreign index in the vacant interval, handle-only deletion); converter-free observation (5); migrations (12: up, guards one by one for the reserved and an unrelated project, rollback, down steps); and the production file-backed API host with real Git: refusal matrix of every gate, both reservation race directions, populated-tracker and persistence faults injected as real SQLite triggers at the receipt, plan, terminal-status and event writes, restart after every external-effect boundary, external interference before acquisition and at the reference effect, an authority change after admission (including a digest-only change), notifier failure, exactly-once events across restarts, and a second delivery extending the completed chain. Application tests pin recovery decisions, the head chain, the recorder (populated tracker, concurrency token, notifier) and the reservation write seams of the four competing writers.
- Previous round (2026-10-07), mutation evidence, superseded by the full rebuilt matrix of the R6-R8 correction below and retained as history (each rebuilt, run against its focused tests and restored; all 142 paths were byte-identical before and after): 36 mutants, 35 detected and one equivalent survivor. Detected: bypassing the direct-ref proof (it changes the named fault from `ref_not_direct` to `pre_mutation_state` because the recursive HEAD read and exact-parent proof also refuse a redirect), bypassing it together with the recursive HEAD read (`Expected: NotPromoted; Actual: Promoted`), never closing stdin, CR stripping, ignoring stderr overflow, `CancellationToken.None` for proof reads, a cleanup budget derived from the expired deadline, committing after the deadline, never terminating the child, a HEAD or index handle sharing write/delete, ignoring the proven Git directory, releasing handles before the final observation, a truncated physical identity, adopting a matching foreign lock, overwrite-enabled rename, a rename buffer without its terminator, dropping either fixed observation setting, observing through the real common directory, trusting a populated tracker, always failing a refusal, ignoring an extant lock in safe release, recovery interrupting regardless of Git state, every seam guard inert, only the recipe-delete guard inert, no execution-time digest comparison, no admission digest comparison, completion without the execution marker, a failing notifier, never cleaning a failed operation's artifacts, ignoring the completed chain, repeating attention events, and mis-mapping the reservation conflict. The survivor removes the `next.Count != 1` clause of the head-chain walk: every fork leaves an edge unused, so the used-edge count check already refuses it and the clause is redundant.
- Behavior corrections found while proving R4/R5: safe release is now proven for every refusal path including a failed acquisition (and its artifacts are removed only after a recorded failure); recipe configure/update/delete and checkpoint capture map a database reservation guard refusal to `workspaces.committing` and persist nothing; recovery completes a promoted operation whose commit, tip, binding, ownership and index are exact even when the working files changed afterwards (the workspace stays under attention instead of the operation staying undecided); the local-commit handlers treat a notification failure after a durable commit as non-fatal; the recorder and receipt writers refresh tracked rows; `LocalCommitLockState` no longer has a byte-match ownership value.
- Limits, stated plainly. With two or more enabled verification recipes this feature refuses (`local_commit.membership_mismatch`) because a human approval records one selected execution. `NeedsAttention` for a pending index promotion is sticky and a lock capability never survives a restart: an extant `index.lock` or a prepared reference lock left by a killed child is never adopted or removed by path or by matching bytes (a pending index is finished only when the lock is absent at the first restart, by a new exclusive acquisition). The HEAD handle is live protection only (not in the receipt), and arbitrary writers that bypass Windows sharing or Git's own locks are not prevented. On this NTFS host the upper 64 bits of the 128-bit file id are zero, so a 64-bit truncation of `FILE_ID_INFO` is not distinguishable here (the form and length are pinned); the wrong-name negative of the post-rename namespace confirmation cannot be produced without corrupting the native call (the terminator mutation covers that primitive). Ownership proof and preparation configuration reads still use the existing worktree discovery by design. Windows is the only admitted host; Increment 4 provider requirements and remote publication remain open.
- Correction R6-R8 (after Codex's second NO-GO; the reviewer's diagnostics were read outside the repository and are evidence, not validation). **R6, an ambiguous operation lost write exclusion:** `CommitReservation` and all seven `AddLocalCommitSeamGuards` triggers tested only `Committing`, so after `NeedsAttention` (operation and run still open) recipe create/update/delete were accepted. The exclusion is now the whole admitted, nonterminal reservation: the workspace is `Committing`, or `NeedsAttention` while a `local_commit_operations` row of that exact workspace and project is not `Completed`, `Failed` or `Interrupted`. One predicate (`CommitReservation.IsProjectReservedAsync`) serves the early refusal and the conflict mapping, and the new migration `AddLocalCommitAttentionExclusion` (generated by EF, then hand-written SQL) drops and recreates the same seven triggers over the same predicate, with an exact `Down` that restores the Committing-only definitions byte for byte (a test compares `sqlite_master`). A historical `NeedsAttention` workspace, a terminal operation, a `Prepared` operation whose workspace is still `Ready`, and every other project are never blocked, and a proven terminal release (Delivered, proven-unpromoted Failed or Interrupted, or a delivery whose source changed afterwards) ends the exclusion. The conflict code stays `workspaces.committing` (its message now says the workspace is held until the commit completes or its outcome is resolved).
- R6 evidence. Application tests `LocalCommitAttentionExclusionTests` (47) against the real migrated SQLite: fresh create/update/delete refused while the operation requires attention (reached through the production recorder), every nonterminal operation status over an attention workspace, a populated tracker that already holds the Committing rows, the early check passing on a Ready read followed by a reservation that turns ambiguous before the writer's save (writers and checkpoint capture), each of the seven guards refusing a stale writer after `Committing` becomes `NeedsAttention` with the whole save rolled back (a sibling insert is absent), and the controls (an unrelated project, another project's ambiguous operation, a historical attention workspace with a terminal operation, a Prepared operation on a Ready workspace, three proven terminal releases and a delivery with a changed source). Real host (`LocalCommitSeamTests`, 9): after the production foreign-index-lock refusal every competing write is refused (HTTP 409 for the three recipe writes) with nothing persisted and the foreign lock untouched, and a proven release accepts a recipe request again. RED first against the submitted behavior: 20 of the 47 Application tests failed (every refusal case; the 27 controls passed); the migration test pair was extended afterwards.
- **R7, unknown ref/HEAD locks did not prevent release or recovery effects:** `InspectAsync` observed `index.lock` only. Authorized minimal addition to the existing inspection facts: the enum `LocalCommitReferenceLockState { Unproven = 0, Clear, Present }` and a trailing `ReferenceLocks` member of `LocalCommitInspection` whose default is `Unproven`, so a default or historical shape never implies clear locks. Infrastructure interprets the paths in `LocalCommitReferenceLocks.Probe`: the lock of `refs/heads/<branch>` in the proven common Git directory and `HEAD.lock` in the proven administrative directory, only through attribute reads (never open, read, create, rename or delete), through ancestors that must be plain directories (a reparse point or a non-directory is unproven; a definitely absent ancestor proves the lock cannot exist), with the branch mapped only from a conservative allowlist (an overlong, overdeep, dot, `..`, `.lock`, or otherwise unrepresentable name is unproven), and any read failure other than a definite not-found is unproven. A file or even a directory under a lock name is `Present`, whatever its bytes or age. The probe is read last in `InspectAsync`. `LocalCommitRecoveryPolicy` returns Attention (`local_commit.unknown_reference_lock` or `local_commit.reference_lock_unproven`) for anything but `Clear` before every release, completion and recorded index promotion (so no new acquisition happens), after the earlier facts keep their own reasons; `IsSafeUnpromoted` requires `Clear` for normal NotPromoted release. No lock is adopted, removed or renamed, nothing is inferred from bytes, process id or age, no CAS is retried and no takeover exists. The decision is recomputed from fresh evidence at each restart, so a foreign lock that later disappears lets the exact proven state decide again (it is not a latch).
- R7 evidence. Actual-startup tests (`LocalCommitRestartRecoveryTests`) place one foreign lock (the exact owned ref lock, or `HEAD.lock` of the linked-worktree administrative directory) after a production-written operation stopped before the marker, after the marker, after promotion, and with a recorded index plan whose index lock vanished, then start the real host: operation `NeedsAttention` (`local_commit.unknown_reference_lock`), run `Running`, workspace `NeedsAttention`, exactly one `local_commit.needs_attention` event and no interrupted or completed event, foreign bytes, branch tip, index bytes and the main checkout unchanged, and no new index acquisition. Controls: lock-free restarts still Interrupt, Complete or finish the recorded plan, and a vanished lock lets fresh proof decide again. Execution safe release (`LocalCommitSafeReleaseTests`): a foreign ref or HEAD lock that makes Git refuse the prepared transaction is `NeedsAttention` (`local_commit.release_unproven`), never Failed or Ready, with the host's own index lock released by handle. Unit and boundary: policy tests for every decision shape and the default shape (Application), and `LocalCommitReferenceLocksTests` (Infrastructure, 81) for clear, present (any bytes or age, directory, read-only/hidden), unrepresentable branches, unfaulted and injected read faults at every probed path with four exception kinds, redirected (junction) ancestors and roots, non-mutation, and real-Git inspection (clear control, both locks present and untouched, a redirected namespace unproven). RED first against the submitted behavior: 12 of 16 new Api tests failed with `Interrupted`, `Completed` or `Failed` while the foreign lock was preserved; the 4 lock-free controls passed. The reviewer's ACL experiment was not usable for the unreadable case here: denying read attributes does not fault `GetFileAttributes`, so the read is injectable (`readAttributes`) and that is how unreadable paths are proven.
- **R8, a failed final stream proof counted as success:** `HasTrailingBytesAsync` returned `null` for a failed or cancelled read but only `true` was rejected, and `DrainStandardErrorAsync` swallowed faults so a completed task stood for EOF. Now a commit or abort is confirmed only by the exact acknowledgement, a clean zero exit, a real stdout EOF and a real stderr EOF: `null` is `:stdout_unconfirmed`; a drain that ended by fault, disposal or cancellation is `:stderr_unconfirmed`; an unfinished drain stays `:stderr_unfinished`. Independent bounded cleanup, actual stdin closure, one terminal send, owned-child termination and reaping, and no retry are unchanged. The reader comment that described `null` as false now states the three outcomes.
- R8 evidence. Production-owner tests through `ProcessExecutionFixture` (now 8 new): after the commit and the abort acknowledgement, a disposed real stdout pipe and a cancelled standard-error drain (an instant fault hook at `*_acknowledged`), a descendant that keeps only stdout open (the fixture makes its own stderr non-inheritable and the descendant gets a private stderr) and one that keeps both pipes open, each bounded by the cleanup budget with the child reaped and exactly one terminal send; plus `LocalCommitRefProtocolReaderTests` (5: real EOF false, pending bytes true, fault, disposed and cancelled reads null and the reader unusable). The healthy commit and abort controls of the earlier owner tests still pass. RED first against the submitted behavior: 6 of the new owner tests failed (`Acknowledged` / `Confirmed` where `Uncertain` / `Unconfirmed` was required). Disclosed: the first stderr fault attempt disposed the stderr stream; a pending pipe read survives disposal and later returns the child's real EOF, so the owner rightly confirmed it, and that case was replaced by the cancelled drain, whose red evidence is the mutation restoring permissive handling (M58, M59 and M61 fail it).
- Mutation evidence for this round (every mutant rebuilt, run against its focused tests and restored; the baseline manifest of the 22 mutation targets was verified clean before and after each mutant, and all 148 paths other than this file were byte-identical before and after, as was the planner record): 64 mutants, 62 detected and 2 survivors. All 36 earlier mutants were rebuilt (M28 and M28b now target the new migration, M26 drops only the index-lock clause, M37 and M38 follow the rename). New: Committing-only guards in the migration (M39 Application 11 failures, M39b migration definitions 2), the early refusal Committing-only (M40, 13), no exact workspace/project binding (M41, 3; M44 in SQL, 3), terminal operations counted as open (M42, 7; M43 in SQL, 14), recovery ignoring the reference-lock proof (M45 unit 7; M45b actual startup 10), safe release ignoring it (M46, 2), the probe skipping HEAD.lock (M47, 17) or the ref lock (M48, 64), an unreadable path treated as absent (M49, 20), a redirected ancestor accepted (M50, 6), an overlong branch, a double-dot component or a `.lock` component mapped to a path (M51, M52, M53), inspection reporting Clear (M54, 3; M54b actual host, 10), a default inspection or default enum value of Clear (M55, M56), the unproven stdout read accepted (M57, 4), a drain that did not reach EOF accepted (M58), a faulted or cancelled drain reporting EOF (M59), an unfinished drain accepted (M60), and all three permissive R8 behaviors together (M61, 6), and the early refusal Committing-only against the real host (M40b, 1). The survivors: M31 (equivalent, unchanged: the used-edge count already refuses a fork) and M39c, which restores Committing-only database guards and runs only the real-host seam tests: there the early check, which stays attention-aware, refuses the write before any trigger fires, so that suite cannot reach the guard (defense in depth); the same mutation is detected by M39 and M39b, which exercise the triggers directly. M45 and M45b first failed to BUILD (the mutant produced unreachable code, an artifact of the mutation, not a test result) and were redefined and rerun.
- Disclosed incident. The first validation run after the matrix ran sequentially while the build had failed with `CS2012` (the Application output was locked by a leftover build process), so Domain 1277 passed and Application (7 failures), Infrastructure (7) and Api (11) ran against the last mutant's stale binaries; every result of that run was discarded (the logs are kept outside the repository as `contaminated-run`). After shutting down the build servers, the tree was verified byte-identical, the generated client was deleted and the solution rebuilt with `--no-incremental -p:UseSharedCompilation=false -m:1`, and the whole validation was rerun from that clean build.
- Fresh results, final tree, sequential after the clean rebuild: `dotnet build DevalCopilot.slnx --no-incremental` 0 warnings, 0 errors; Domain 1277 passed; Application 4604 passed (55 new this round); Architecture 40 passed; Infrastructure 1589 passed, 4 skipped, 1593 total (95 new; the same four pre-existing symbolic-link and reparse-point cases that need a privilege this session lacks); Api 1286 passed (18 new). Frontend in `src/frontend/DevalCopilot.Frontend`: `npm run typecheck` 0, `npm run lint` exit 0 (the same 9 `react(set-state-in-effect)` warnings in unrelated hooks), `npx vitest run` 2232 passed in 149 files, `npm run build` 0, `npm run test:harness` 87/87 with 0 skipped, then ONE canonical `npm run test:e2e:all`: Chromium 12/12 and journeys 4/4, exit 0, no skip or rerun (the Vite SignalR "stopped during negotiation" console lines are the existing reload noise). Generated client regenerated by the build after deleting it: SHA-256 `e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969`, byte-identical to the earlier client. `dotnet format --verify-no-changes` over the 123 changed C# files (generated designers excluded): no finding; `dotnet list package --vulnerable --include-transitive` none; `npm audit` still reports the one pre-existing high advisory in the transitive `source-map-js` and no manifest changed; `git diff --check` clean apart from the existing CRLF notices; the untracked C# and Markdown files have no trailing whitespace or NUL bytes and have a final newline; the planner scan found mixed LF/CRLF endings in the two AddLocalCommitIndexEffectReceipts migration files (including its generated designer), so uniform LF endings are not claimed for those files; Markdown links in the two changed documents: 94 checked, 0 problems. No earlier result is carried over as fresh.
- Inventory, final: **149 paths, 29 modified tracked and 120 untracked**, nothing staged; the planner record and ADR-0029 are unchanged. The 7 additional paths are `Application/.../Ports/LocalCommitReferenceLockState.cs`, `Infrastructure/.../Runs/LocalCommitReferenceLocks.cs`, the migration `20261008090914_AddLocalCommitAttentionExclusion` and its designer (generated, snapshot unchanged), and the tests `LocalCommitAttentionExclusionTests` (Application), `LocalCommitReferenceLocksTests` and `LocalCommitRefProtocolReaderTests` (Infrastructure). Edited existing files of this slice: `CommitReservation`, the three recipe handlers (rename), `LocalCommitInspection`, `LocalCommitRecoveryPolicy`, `ExecuteLocalCommitCommandHandler`, `LocalCommitGit.Repository`, `LocalCommitRefTransaction`, `LocalCommitRefProtocolReader`, the `ProcessExecutionFixture` program, the migration, API seam, restart and safe-release tests, the protocol, policy and migration tests, and `docs/architecture/data-and-recovery.md`.
- Limits added by this round, stated plainly. The reference-lock proof covers the two locks a prepared `update-ref` takes for this transaction (the owned ref lock and `HEAD.lock`); it does not examine `packed-refs.lock` or reflog files, and it is a point-in-time read taken last in the inspection, so an update that begins and finishes between the reads is not seen. A `.lock` left by a killed owned child is, by design, indistinguishable from a foreign lock and keeps the operation under attention until it disappears. Branch names outside the conservative allowlist (letters, digits, `.`, `_`, `-`, with `/` separators) are unproven, which is correct for the generated workspace branch but would refuse a branch with other characters. A repository whose common or administrative directory is itself redirected is unproven. The unreadable-path case is proven through an injected read, not a real access denial, because denying attributes does not fault the read on this host. The exclusion is bound to the open operation's workspace and project; it does not block other projects or unrelated attention workspaces, and the first-round limits above remain.
- Publication and post-publication closure (this factual closure changes only this entry of this file; it does not name its own commit). Codex's publication GO covered the frozen 149-path snapshot. The approved manifest `local-commit-approved-149.json` (SHA-256 `ea23a9f113dbd581339d296403630450290a8d002f9b1044107c08c1930f1f41`, not regenerated) matched before staging: branch `main`; `HEAD`, local `origin/main` and live `refs/heads/main` (`git ls-remote`) all `a9e0e2b0b3b05bd54d8d45aec6258c035a02e930`; empty index; exactly 149 paths (29 modified tracked, 120 untracked), and every path's status, raw SHA-256 and byte size equalled the manifest with no extra or missing path. The raw hashes at that moment were: planner record `28d90336…41fa`, this file `fa093eea…3583` (it already carried Codex's factual correction, which was preserved), ADR-0029 `cf9a28d4…b294` and the generated client `e215388b…9969`. Two receipt migration files with mixed LF/CRLF endings are the recorded exception and were not normalized. Only the 149 manifest paths were staged; with `--no-renames` the staged inventory was exactly 29 modified and 120 added paths and equalled the manifest, and `git diff --cached --check` passed (only the existing CRLF conversion notices). Substantive commit `5319975b1b80988aeb1c2cba8c570fe2197ef2a8` ("feat: deliver approved checkpoints as explicit local commits"), parent `a9e0e2b0b3b05bd54d8d45aec6258c035a02e930`, 149 files changed, pushed with a normal fast-forward push (`a9e0e2b..5319975`). After `git fetch` and an independent `git ls-remote origin refs/heads/main`, `HEAD`, local `origin/main` and the live ref all equal the substantive commit with a clean checkout. The wording above that describes the tree as unstaged, uncommitted or unpushed, awaiting GO, the 142- and 149-path inventories and the planner hashes `ee9e967c…`, `1d2c9262…` and the earlier current-work hashes are HISTORICAL snapshots of the earlier review rounds; the published tree is the 149-path manifest, and the planner record's published hash is `28d90336…41fa`.
- Fresh post-publication checks on commit `5319975b…`, run sequentially from fresh output files with an explicit exit-code check after every step (a failed build would have stopped the sequence before any `--no-build` test; all exits were 0): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; with `--no-build --no-restore`: Domain full 1277 passed, 0 skipped; Application filter `FullyQualifiedName~LocalCommit|FullyQualifiedName~ReconcileWorkspacesLocalCommitChain` 116 passed, 0 skipped; Infrastructure filter `FullyQualifiedName~LocalCommit|FullyQualifiedName~WindowsIndexEffectHandles` 211 passed, 0 skipped; Api filter `FullyQualifiedName~Features.Runs.LocalCommit` 92 passed, 0 skipped; Architecture full 40 passed, 0 skipped. In `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 2232 passed in 149 files, `npm run typecheck` 0, `npm run lint` exit 0 with the 9 existing `react(set-state-in-effect)` warnings, `npm run build` 0, `npm run test:harness` 87/87 with 0 skipped, then, only after the harness passed, ONE `npm run test:e2e:all` with normal authentication, `Program` composition and all supervisors: Chromium 12/12 and journeys 4/4, exit 0, no skip, flaky test or rerun (the Vite SignalR "stopped during negotiation" console lines are the existing reload noise). The counts equal the reviewed ones. The generated client is unchanged (SHA-256 `e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969`), `git diff --check` is clean and the checkout was clean after the checks. No test fixture root was created by this run that remained afterwards; the unrelated older roots, including `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr`, were left untouched.
- Retained, not rerun after publication: the complete Application (4604), Infrastructure (1589 passed, 4 skipped for the pre-existing symbolic-link and reparse-point cases that need a privilege this session lacks) and Api (1286) suites outside the filters above, the 64-mutant matrix (62 detected, the equivalent M31 and the redundant-guard M39c surviving), `dotnet format`, `dotnet list package --vulnerable` and `npm audit` (the one pre-existing high `source-map-js` advisory) results from the pre-publication validation of this entry; the earlier failure evidence, including the discarded contaminated validation run, remains exactly as recorded above. The remaining limits above are unchanged. Increment 4 is not complete, nothing here claims Increment 4, 5 or MVP completion, and no next slice is selected or authorized.


## Owner-selected immutable budgets at manual run intake (2026-10-05)

- Status: implemented and validated as an UNSTAGED, UNCOMMITTED and UNPUSHED working-tree diff awaiting Codex GO/NO-GO; no commit, push or next slice is granted. Preflight verified once before editing: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `e0ab52676c70e8d9c9d0760343e5607ec4aed8f4`, empty index, only `docs/roadmap/planner-handoff.md` modified, nothing untracked, planner SHA-256 `866e6e55…db8` (unchanged at the end; the file was never edited) and the previous generated client `e1540273…89bc`.
- Delivered outcome ([ADR-0028](../decisions/0028-let-the-owner-choose-immutable-run-budgets-at-manual-intake.md)): `POST /api/runs/manual` and its one `CreateManualRunCommand` accept the optional nullable integers `maximumAgentAttempts` (1..16) and `maximumAgentInvocationMinutes` (1..120 whole minutes). Each missing or null member independently selects the existing default (16, 120); an explicit value outside the range, or not a JSON integer (fraction, quoted number, exponent form, overflow, boolean), is refused (`validation.invalid` or model binding, HTTP 400) and writes nothing. The request members use strict number handling because the MVC default converts a quoted number (found by the first red test run). The effective values (`TimeSpan.FromMinutes` of the minutes) pass through `RunIntentRecorder` to `Run.RecordClassifiedIntent` into the existing `MaximumAgentAttempts`/`MaximumAgentInvocationTime` columns in the same serialized save (run, intent event, execution number); the simulated operation passes the same fixed defaults through the shared recorder. `Run.DefaultMaximumAgentAttempts = 16` names the former literal default. No migration, setter, event kind, column or claim-algorithm change. The cockpit now always shows "Agent claims: N of M used" as a quiet fact before exhaustion (the exhausted alert text is unchanged; a missing, non-integer or inconsistent claim projection shows a distinct unknown/invalid note with no count) and the time banner states reserved-not-elapsed and that a positive remainder proves no role can be claimed. The manual form has two numeric drafts (16 and 120) with the required explanation; the objective and both drafts are one per-project snapshot whose version advances on every edit of any field; the demo caption states that the manual fields are ignored.
- Inventory (32 paths besides the planner record and this file: 24 modified, 8 added). Backend: `Run` (constant and default parameters), `RunIntentRecorder`, `CreateManualRun` command/handler/validator plus new `ManualRunBudgetRange`, `StartSimulatedRunCommandHandler`, the endpoint and request. Frontend: regenerated `api-client.ts` (SHA-256 `c45dde6e9217a6bc1803c0993168c80dc571172120ef23e9b47cf35253d145db`, regenerated by the build after deleting the file), `runIntake.ts`, `useRunIntake.ts`, `RunIntakeForm.tsx`, `AgentClaimBudgetBanner.tsx`, `AgentInvocationTimeBudgetBanner.tsx`, `RunCockpitView.tsx`, the e2e `journeyDb.ts` read-only `runBudgets()` reader and the new `manual-budgets.journey.ts`. Tests: new `ManualRunBudgetCreationTests` (Application, 26), `CreateManualRunBudgetEndpointTests` (Api), `ManualRunBudgetBoundaryTests` (Architecture, 4), `RunIntakeBudgetDrafts.test.tsx`, `ChosenBudgetDisplays.test.tsx`; extended `ManualRunHostedTests` (3 hosted tests on production-created budgeted runs) and two existing frontend tests whose expectation intentionally changed (`AgentClaimBudgetBanner.test.tsx`: no longer "renders nothing" before exhaustion; `RunIntakeForm.test.tsx`: the 400 refusal copy). Docs: ADR-0028, ADR index, engineering context, cockpit specification, agent protocol, roadmap, and this file.
- Regressions first: the new Application/Api tests were run before the handler honored the choices (19 Application and 21 Api/hosted failures, all for ignored non-default choices or missing range checks; the transport-binding cases already refused). The two quoted-number cases then failed until strict number handling was added. Frontend tests were written first and failed 42 before the implementation.
- Claim enforcement evidence (audit, not a new algorithm): all eight Agent claim handlers (planning, critical review, challenge resolution, implementation, code review, review correction, verification diagnosis, diagnosis correction) and their repair/race-recheck variants already compare against `run.MaximumAgentAttempts`/`run.MaximumAgentInvocationTime`; an Architecture test pins that each of the eight handler files does so and never names `DefaultMaximumAgent*`, that only the two creation handlers and `Run.cs` name the defaults, that only `RunIntentRecorder` and `Run.cs` create a classified run or assign the ceilings, and that only the manual creation slice accepts the minutes choice. The existing per-handler suites (count exhaustion, time refusal, equality, races, malformed evidence) seed non-default ceilings through the same Domain factory; a new bridge test proves a production-created run (4, 40) is property-for-property identical to the factory run with those ceilings. On production-created runs through the real Api host with the real supervisors and only the Codex adapter and Git evidence doubled: a 9-minute reservation refuses the 10-minute planning claim (`agent_attempts.time_budget_exceeded`) with no attempt, probe or provider call; a 1-claim/10-minute run accepts the exactly fitting claim, shows 1/1 and 10 of 10 minutes, and refuses a second claim (`agent_attempts.budget_exhausted`); an unrelated default run is unaffected. DISCLOSED LIMIT: permanent consumption after failure or interruption and the other seven paths on a production-created row are proved by the existing suites through the factory-equivalence bridge, not re-run on production rows; nothing in this slice changed those paths.
- Browser: the new owned `manual-budgets.journey.ts` (journey configuration, normal authenticated composition, all supervisors, the owned provider doubles; the older roots untouched) registers a project, prepares the workspace and checkpoint through the rendered controls, records a run through the rendered form with 4 claims and 9 minutes (the page sends exactly those numbers; HTTP 200), reads "Agent claims: 0 of 4 used", "0 min of 9 min (9 min remaining)" and the time-fit block, confirms the stored 4 and 9 minutes in ticks, reloads and confirms them again, then makes an explicit planning request through the generated client over real HTTP, which is refused with 409 `agent_attempts.time_budget_exceeded` with no attempt, no Agent stage invocation and no account-usage observation in the doubles' log, and the repository untouched. It passed alone, then in the canonical run.
- Fresh validation on this tree: `dotnet build DevalCopilot.slnx --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors after deleting the generated client (NSwag regenerated it); `--no-build --no-restore`: Domain 1209/1209 (unchanged), Application 4488/4488 (was 4462), Infrastructure 1378 passed + 4 skipped (unchanged and the same four environment-gated skips), Api 1194/1194 (was 1156), Architecture 40/40 (was 36). Frontend: `npx vitest run` 2134/2134 in 144 files (was 2090 in 142), `npm run typecheck` 0, `npm run lint` exit 0 with the 9 baseline warnings, `npm run build` 0 (existing chunk-size notice), strict e2e `tsc` (the command recorded in the earlier entries) over 41 files 0, `npm run test:harness` 87/87, then ONE canonical `npm run test:e2e:all` with normal authentication and composition: Chromium 12/12 and journeys 4/4 including the new journey, exit 0, no skip or rerun (full output kept outside the repository as `e2e-all-canonical.txt` in the executor scratch directory; the Vite SignalR "stopped during negotiation" console lines are the existing reload noise). `dotnet format --verify-no-changes` 153 findings, the recorded baseline, none in a changed file. `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none. `git diff --check` clean apart from the existing CRLF notices of the generated client and the planner record; the 8 new files have LF endings, a single final newline, no trailing whitespace and no BOM; 291 Markdown file and anchor links over the 7 changed or new Markdown files and the planner record, 0 problems; the planner record hash is unchanged.
- Restored mutations (each backend mutant rebuilt before its run, restored byte-for-byte afterwards, then the restored tree rebuilt): claim ceiling replaced by the default in the handler: 7 Application and 7 Api/hosted tests failed; minutes replaced by the default: 7 and 7; transport range constants widened to `int.MaxValue`: 5 and 5; the minutes draft omitted from the form's version bump: 2 frontend tests failed; the claims draft omitted: 2 failed.
- Retained versus fresh: everything above was run fresh on the final tree except that no Domain or Infrastructure source changed (both suites were nevertheless rerun fresh) and the retained historical behavior of the other seven claim paths on production rows noted in the limit above.
- Remaining limits and risks: the ceilings are reservations and permanent claims, not measured elapsed time, token or account policy, or proof that an objective can finish; a ceiling below a role's configured timeout can make the first claim impossible and nothing raises it for that run; the 1..16 and 1..120 range applies only to NEW manual runs; no existing run, default, historical policy or claim algorithm changed. Claude account usage, session resume, manual compaction and the remaining Increment 4 loop, token and account-usage controls remain open.
- Correction R1 (after Codex's NO-GO; local to `AgentClaimBudgetBanner`, its tests and the cockpit specification; no backend, contract, generated-client, claim-authority or action-block change): the banner displayed a count or asserted exhaustion from an unvalidated projection. It now trusts the projection only when the ceiling is a safe positive integer, the usage a safe non-negative integer, and the exhausted flag a boolean equal to `used >= maximum`; otherwise it renders the existing distinct unknown/invalid note with no count and no reached-limit claim. Historical ceilings above 16, coherent usage above the ceiling, the ordinary exhausted alert and the quiet pre-exhaustion fact are preserved (no intake-range restriction on display). RED first against the submitted component: the new `AgentClaimBudgetBannerCoherence.test.tsx` (28 cases) failed 14 (the three reported reproductions: 4/0 exhausted, missing ceiling exhausted, ceiling 9007199254740992 not exhausted; plus null/missing/zero/negative/fractional/NaN/infinite/unsafe variants under exhausted=true), the 14 coherent or already-invalid controls passed. GREEN after the change: those 28, the earlier banner and cockpit-view tests (169 in the four affected files). Fresh after R1: full `npx vitest run` 2162/2162 in 145 files, `npm run typecheck` 0, `npm run lint` exit 0 with the same 9 baseline warnings, `npm run build` 0, strict e2e `tsc` over 41 files 0, `npm run test:harness` 87/87, then ONE canonical `npm run test:e2e:all`: Chromium 12/12 and journeys 4/4, exit 0, no rerun (`r1-e2e-all-canonical.txt` in the executor scratch directory). RETAINED from the first return because no backend code, backend test or generated-client byte changed (client still `c45dde6e…145db`): solution build, Domain 1209, Application 4488, Infrastructure 1378 + 4 skipped, Api 1194, Architecture 40, the backend mutations, formatter baseline 153 and the audits; the frontend mutations (draft omitted from versioning) are retained for the same reason, as `RunIntakeForm` is unchanged. Hygiene on the new test, the banner and the specification: final newline, no trailing whitespace, no NUL; `git diff --check` clean apart from the existing CRLF notices; local Markdown links 132 checked, 0 problems; planner record SHA-256 `960f77f1…b3b` unchanged by me. The tree is now 26 modified (including the planner record and this file) and 9 untracked paths, nothing staged.
- Publication and post-publication closure (this factual closure changes only this entry of this file). The approved manifest (SHA-256 `ab0757d6ed4a2bceb757bc7bb0d07aa316cbae9b50c0706226a9cd27a0d3bd2e`) matched before staging: branch `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `e0ab52676c70e8d9c9d0760343e5607ec4aed8f4`; empty index; 35 paths (26 modified, 9 untracked), every status, raw SHA-256 and byte size, no extra or missing path; planner record `98e382d2…7096`, `current-work.md` `a3cde5e5…9f28`, generated client `c45dde6e…145db`. Only the 35 manifest paths were staged and the staged inventory equalled the manifest; `git diff --cached --check` clean. Substantive commit `40ab3e84502f1c22311e56e6979957d0d4477388`, parent `e0ab52676c70e8d9c9d0760343e5607ec4aed8f4`, pushed with a normal fast-forward push (`e0ab526..40ab3e8`); after `git fetch` and an independent `git ls-remote origin refs/heads/main`, `HEAD`, local `origin/main` and the live ref all equal it with a clean checkout. The wording above that describes the tree as UNSTAGED, UNCOMMITTED and UNPUSHED, "awaiting Codex GO/NO-GO", the path counts (32 plus this file and the planner record) and the planner hashes are historical wording of earlier snapshots; the published tree is the 35-path manifest including the planner record, which Codex's review decision updated.
- Fresh post-publication checks on commit `40ab3e84…`, run sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; Application filter `ManualRunBudgetCreationTests|RunIntentCreationTests` 49/49; Api filter `CreateManualRunBudgetEndpointTests|ManualRunHostedTests|CreateManualRunEndpointTests` 63/63; full Architecture 40/40 (all `--no-build --no-restore`); in `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 2162/2162 in 145 files, `npm run typecheck` 0, `npm run lint` exit 0 with the 9 baseline warnings, `npm run build` 0 (existing chunk-size notice), `npm run test:harness` 87/87 with 0 skipped, then ONE `npm run test:e2e:all`: Chromium 12/12 and journeys 4/4, exit 0, no skip, flaky test or rerun (full output and exit code kept outside the repository as `pp-e2e-all-canonical.txt` in the executor scratch directory). No observed skip in any of these. Generated client unchanged (`c45dde6e…145db`), `git diff --check` clean, checkout clean after the checks, and the run's own owned e2e roots were removed; `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` were left untouched.
- Retained, not rerun after publication: the full Domain 1209, Application 4488, Infrastructure 1378 + 4 skipped (the same four environment-gated skips) and Api 1194 suites, the restored backend and frontend mutation results, `dotnet format --verify-no-changes` baseline 153 and the `npm audit`/`dotnet list package --vulnerable` results from the pre-publication validation above; the disclosed limits (production-row proof of consumption after failure or interruption for the other seven claim paths comes from the existing suites through the factory-equivalence bridge) and the remaining risks above are unchanged. Increment 4 is not complete; no next slice is selected or authorized.

## Attested checkpoint comparison for human inspection (2026-10-05)

- Status: published (substantive commit `af2a221c035c39f25e2e5642ddada2d8cf576230`, parent `7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f`, verified in the closure below) after Codex's GO for the frozen snapshot following correction R1; no next slice granted. The wording below that describes the tree as UNSTAGED, UNCOMMITTED and UNPUSHED, the 46-path count excluding the planner record and the planner-record hashes (`2d410897…`, `57239c90…`) are historical wording of earlier snapshots; the published tree is 47 paths including the planner record (27 modified, 1 deleted by the move, 19 added), with the planner record at `63d5e345c644d905bca4dc659444c308752f8082d5249e42139da073c83ccbb5`. Original return wording: returned for Codex GO/NO-GO on the published base `7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f` (branch `main`; `HEAD`, local `origin/main` and live `refs/heads/main` were all that SHA before editing, nothing staged or untracked, and `docs/roadmap/planner-handoff.md` was the only modified file; its SHA-256 `2d410897d31673b3f0776ab1f086623a3f31615cc8bd9346e29f80aba3569c0e` is unchanged by the executor). This entry carries no publication SHA. No next slice is selected and Increment 4 completion is not claimed.
- Publication and post-publication closure (this factual closure changes only this entry of this file). The approved manifest (SHA-256 `7f396c6947f4b40b13db81fac95b323dd18981e88b301c53b62ca3ee123b8838`) matched before staging: branch `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f`; empty index; 47 paths, every status, raw SHA-256 and byte size, no extra or missing path; `current-work.md` `e6635427…c639` and the generated client `e1540273…89bc` as approved. Only the manifest paths were staged (with `--no-renames`: 27 modified, 19 added, 1 deleted; `git diff --cached --check` clean) and committed as `af2a221c035c39f25e2e5642ddada2d8cf576230` with exactly that parent; a normal fast-forward push (`7552cbe..af2a221`) was followed by `git fetch` and an independent `git ls-remote origin refs/heads/main`, and `HEAD`, local `origin/main` and live `main` all equal the commit with a clean checkout; nothing was amended, forced or reconciled. FRESH post-publication checks on that commit, run sequentially with new output files: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` exit 0, 0 warnings/0 errors; `--no-build --no-restore`: Application selection 215/215, Infrastructure selection 88/88, Api `CheckpointComparisonEndpointTests` 12/12, full Architecture 36/36 (none skipped); `npx vitest run` 2090/2090 in 142 files, `npm run typecheck` exit 0, `npm run lint` exit 0 (9 baseline warnings, 0 errors), `npm run build` exit 0, `npm run test:harness` 87/87, then one canonical `npm run test:e2e:all` (normal authentication and composition, all supervisors) exit 0 with Chromium 12/12 and journeys 3/3, no failure or skip; the generated client hash is unchanged and `git diff --check` and the checkout are clean. The two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched and the run left no root of its own. RETAINED, not repeated on the published commit (identical code and tests): the full Domain 1209, Application 4462, Infrastructure 1378 + 4 skipped and Api 1156 suites, the backend and frontend mutation sets, the formatter baseline (153 findings; formatter cleanliness is not claimed), the audits and the hygiene/link checks. Unchanged and still disclosed: the first-run Infrastructure cancellation-test failure with its cause unproven, the surviving equivalent mutant (the redundant `typeof isComplete` guard), and the remaining limits below. No real-provider reliability and no Increment 4 completion is claimed, and no next slice is selected here.
- Delivered outcome ([ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md)): the protected checkpoint-diff `GET` and its one mediator query keep project/checkpoint membership, a Ready workspace, an active lease, a fresh coherent capture and exact fingerprint agreement, but ask only for the new explicit `IGitWorkspaceEvidenceReader.CaptureForCheckpointInspectionAsync`: the same bracket and fingerprint with the ADR-0024 attested tracked facts, no raw patch (`CompleteDiff` null), no instruction context and no untracked previews, and the root instruction names NOT reserved. A reader without attestation yields `not_attested` omissions, never its patch. The query re-derives every tracked path from the immutable facts (`AttestedTrackedComparison` plus the unchanged `TrackedComparison` algorithm, now owned by Projects and called with an explicit `TrackedSourcePurpose`: `AgentDelivery` still reserves `AGENTS.md`/`CLAUDE.md`, `HumanInspection` compares a physically proven one as inert text) and fits whole file blocks in ordinal order into 512 KiB of UTF-8 (`CheckpointComparisonFit`; an over-budget block is omitted whole as `comparison_limit`). The response replaces `completeDiff` with `comparisonText`, `isComplete`, `trackedPathCount`, `comparedPathCount`, the fixed `limitation` and `omissions` (path and fixed reason, ordinal); every tracked changed path is compared or omitted exactly once and untracked files stay in the changed-files list. The generated client was regenerated by the build; the hook exposes `comparison` only for a coherently accounted response (see correction R1) and the panel states partial and all-omitted results, lists omitted paths with reasons and the limitation as plain text, and shows "No tracked diff." only for a complete capture with no tracked change.
- Inventory (46 paths besides the planner record: 26 modified, 1 removed by the move, 19 added): Application `Projects/Policies` (`AttestedTrackedComparison`, `CheckpointInspectionProjection`, `TrackedSourcePurpose`, `TrackedComparison` moved from `Runs/Policies` with only its namespace changed), `Runs/Policies/TrackedChangeEvidence` (now a thin Agent consumer of the shared policy), the port and result documentation, `GetGitCheckpointDiff` query/handler/result/omission/`CheckpointComparisonFit`; Infrastructure `GitWorkspaceEvidenceReader` (a private capture purpose; no fingerprint code touched) and `.TrackedFiles` (reservation now a parameter); Api response, omission response and endpoint; frontend `checkpointComparison.ts`, `useProjectGitEvidence`, `WorkspaceEvidencePanel`, CSS, the regenerated `api-client.ts` and the run-owned e2e fixture and spec; tests; ADR-0027 and the ADR index, engineering context, protocol, cockpit specification and roadmap text; this file.
- Failing-first evidence (preserved in the session scratchpad, `red-run-1.txt`): the new real-Git, real-hard-link, authenticated-HTTP tests were first run against the unchanged production handler: 9 of 11 failed, the unsafe-path scenarios because the outside sentinel `OUTSIDE-SENTINEL-5e91` was present in the response body (`+OUTSIDE-SENTINEL-5e91` inside `completeDiff`) and the new contract properties did not exist; the stale-checkpoint and textconv guards passed by design. One scenario first failed for a test-design reason (a minimal raw patch that exceeded the 512 KiB raw capture limit); it was reshaped so only first and last lines differ, which keeps Git's patch small and the host's replacement hunk large.
- Fresh validation on this tree (the last source or test edit, before this documentation entry, restructured one statement of one Application test to satisfy the formatter without changing what it asserts; after it I reran the solution build, the full Application suite (4462/4462 again) and the formatter, while Domain, Infrastructure, Api, Architecture, the frontend checks and the canonical e2e ran just before it on otherwise identical code and are not affected by that Application test edit): `dotnet build DevalCopilot.slnx --no-incremental -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors after deleting the generated client, which NSwag regenerated byte-identically (SHA-256 `e1540273dab05124e8e7ca8f62863171efd6944c8c1fab0e53d6295074cc89bc`, was `3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`; a first parallel `--no-incremental` attempt failed with a build-order `CS0006` race and was repeated with `-m:1`). `--no-build --no-restore`: Domain 1209/1209, Application 4462/4462 (was 4431), Infrastructure 1378 passed + 4 skipped (the same four environment-gated link/reparse skips; was 1371 + 4), Api 1156/1156 (was 1144), Architecture 36/36 (was 33). Frontend: Vitest 2066/2066 in 142 files (was 2057), `npm run typecheck` 0, `npm run lint` exit 0 with the 9 baseline warnings, `npm run build` 0 (existing chunk-size notice), strict e2e `tsc` over the 40 files (the command recorded in the earlier entries) 0, `npm run test:harness` 87/87, then one canonical `npm run test:e2e:all` (normal authentication, Program composition, all supervisors): Chromium 12/12 including the new `checkpoint-inspection.spec.ts`, journeys 3/3, exit 0, no skip or rerun, full output kept as `e2e-all-canonical.txt`. `dotnet format --verify-no-changes` 153 findings, the recorded baseline (the first run found 154: one in my new handler test, fixed and rechecked; none in any changed file now). `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none. Hygiene over the 45 existing changed or new paths other than the planner record (tracked and new files: NUL, final newline, blank line at EOF, trailing whitespace and BOM/CRLF on new files), run after this entry was written, clean apart from the generated client's standard CRLF and missing final newline; `git diff --check` clean; Markdown file and anchor links in the 8 changed or new Markdown files, the planner record included, 282 checked, 0 problems; the moved `TrackedComparison.cs` differs from the original only in its namespace line; the planner record hash is unchanged.
- Transient failure preserved, not hidden: in the first full Infrastructure run one test, `AgentProcessEvidenceAdapterTests.A_real_cancelled_codex_process_reports_cancelled_evidence_rather_than_throwing`, threw `TaskCanceledException` (1 failed, 1377 passed, 4 skipped; output kept as `full-Infrastructure.IntegrationTests.FIRST-RUN-1-failure.txt`). It passed 3 of 3 times alone and the whole suite then passed (1378 + 4); no path it exercises (provider adapters, process evidence) is changed by this slice. It looks like a cancel-after-1-second timing race under load, but its cause was not proven.
- Mutation evidence (each applied to the exact source, the solution rebuilt, the affected tests run and the files restored byte-for-byte, hashes compared, then rebuilt): raw-patch fallback (reader keeps the patch and the query returns it) detected by Application 3, Infrastructure 4, Api 8, Architecture 1 and the Chromium spec; lost omission accounting in the response fit detected by Application 4 and Api 2, and in the derivation by Application 101 and Api 10; weakened single-name proof (`LinkCount != 1` removed) detected by Infrastructure 8, Api 2 and the Chromium spec (not by Application, correctly); weakened source bounds in the shared policy detected by Application 3 only (the reader enforces them upstream, so HTTP stays green); Agent reservation dropped in the shared policy detected by Application 32 and in the reader by Infrastructure 2 and Api 14; human purpose replaced by the Agent purpose detected by Application 1, Api 1 and Architecture 2; inspection using the ordinary capture detected by Application 10, Api 8 and Architecture 1; comparison limit effectively removed detected by Application 2 and Api 1; completeness always true detected by Application 5 and Api 3; frontend all-omitted read as compared (2 failed), a late overlapping inspection answer accepted (1 failed, caught only by the new test) and a missing completeness flag read as complete (3 failed). Three first attempts (two `if (false)` forms and one limit form) did not compile (unreachable-code error) and are not counted; they were rewritten as compilable mutants. No mutation survived.
- Review correction R1 (frontend normalization/presentation only; the backend, the backend tests and the generated client `e1540273…89bc` are byte-unchanged, and the planner record `57239c90efd786678dbd1458d4490e2a58e882ecd65bd458b6fe7c4f25caa75a` is preserved): `toCheckpointComparison` defaulted missing counts to zero and trusted `isComplete`, so `{ isComplete: true }` rendered "No tracked diff." and `{ isComplete: true, trackedPathCount: 3, comparedPathCount: 0, comparisonText: '', omissions: [] }` rendered "All 3 tracked files compared." It now returns null unless every member is present, both counts are safe non-negative integers, compared plus omitted equals tracked, `isComplete` equals "no omission", each omission has a distinct non-empty path and reason, and the text is empty exactly when no path was compared (the text itself is never parsed); a missing limitation text is tolerated because it is not coverage. The hook refuses a null result through the existing failed-inspection path (inspection cleared, the fixed message "The host response could not be confirmed as a valid checkpoint comparison." on that checkpoint) and the panel's unconfirmed branch was removed. No generic decoder or backend change. Valid zero-tracked, complete, partial and all-omitted responses still render. Existing tests that built responses without `omissions`, or with empty text for a compared path, were corrected to valid contracts, and two superseded panel tests were replaced by the admission matrix. The cockpit specification states the rule.
- R1 red/green: before the change, the 20-case refusal matrix plus the clear-on-refusal test through the real hook and rendered panel ran against the old normalizer: 21 failed and 10 passed (both reviewer reproductions; missing counts, omissions, text and flag; NaN, infinite, negative, fractional, string and unsafe counts; non-adding accounting; flag/omission contradictions; text/compared contradictions; empty-path and duplicate omissions; the four valid controls passed; output kept as `r1-red.txt`). After: the panel file 31/31, then the cockpit directory 2062/2062; a new hook test covers refusal, cleared state and recovery. Frontend mutations on the new code: dropping the accounting equation (3 failed), the text/compared check (2 failed) and the safe-integer check (1 failed) were detected; removing the `typeof isComplete` guard survived because `isComplete !== (no omission)` already rejects a non-boolean (an equivalent, redundant guard). Files restored byte-for-byte.
- R1 fresh checks (no other file changed): `npx vitest run` 2090/2090 in 142 files (was 2066), `npm run typecheck` 0, `npm run lint` exit 0 with the 9 baseline warnings, `npm run build` 0, strict e2e `tsc` over 40 files 0, `npm run test:harness` 87/87, then one canonical `npm run test:e2e:all`: Chromium 12/12 and journeys 3/3, exit 0, no skip or rerun (`r1-e2e-all-canonical.txt`); the two older roots remain untouched and no new root was left. RETAINED from the first return because no backend, backend test or generated-client byte changed: solution build, Domain 1209, Application 4462, Infrastructure 1378 + 4 skipped, Api 1156, Architecture 36, the backend mutation set, formatter baseline 153 and the audits. The first-run Infrastructure cancellation-test failure (`TaskCanceledException`, cause unproven, passed 3 of 3 alone and in the later full run) is unchanged and still disclosed above.
- Remaining limits: this closes the human-inspection route that delivers tracked text through HTTP and the UI, not every filesystem read. The raw Git observation behind the fingerprint (`status`, `diff`, per-path hashing) still reads named paths and can read an outside hard link; files and link topology can change after observation; only Windows has the physical proof (elsewhere every tracked path is `containment_unproven`). The comparison is not Git's minimal or filter-normalized patch and compares no modes or renames, and a legitimately multiply linked tracked file is a false refusal. On this machine the system `core.autocrlf=true` makes a host-prepared workspace CRLF while the hardened capture ignores system configuration, so a fresh workspace shows line-ending-only tracked differences (pre-existing, stated by the fixed limitation; the new tests pin `core.autocrlf=false` in their repositories). No real provider was invoked and none is claimed reliable. Existing SignalR negotiation console noise in the journey host output is unchanged.
- Not changed: fingerprints, checkpoint persistence, raw internal observation, historical artifacts, Agent manifests and replay, provider arguments, budgets, account controls, claims, authorizations, review applicability, lifecycle, leases and supervisor routing; no migration, dependency or real provider call. The two older owned e2e roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched and the canonical run left no root of its own.

- Codex final review: R1 accepted; publication GO applies to the frozen 47-path snapshot including the planner-owned GO record (27 modified, one deleted by the namespace-only move, 19 new). The 46-path executor count excludes that record. Codex independently ran the four corrected frontend panel/ownership files (89/89) and typecheck (exit 0), and inspected the fresh full Vitest and canonical browser logs recorded above without rerunning the browser. Earlier independent solution build, Application 215/215, Infrastructure 88/88, Api 12/12 and Architecture 36/36 remain evidence on the unchanged backend. The redundant-guard mutant is equivalent, and the earlier cancellation failure remains disclosed with cause unproven. Planner hashes above describe historical dispatch/NO-GO states, not the final GO record. Publication is pending; the approved raw-file manifest is external, including the required absence of the moved-away source. No next slice or Increment 4 completion is granted.

## Explicit advisory Codex account-usage warning (2026-10-05)

- Status: published (substantive commit `167f212fbd1714bc9e7cca3e5cd70de80263bc89`, verified in the closure below) after Codex's GO for the frozen snapshot following corrections R1 and R2; no next slice granted. The wording below that describes the tree as UNSTAGED, UNCOMMITTED and UNPUSHED, the 80-path count and the planner-record hashes (`1285a458…`, `0176aba8…`) are historical wording of earlier snapshots; the published tree is 81 paths (35 modified, 46 added) with the planner record at `2ee8b55c88adb991656808a214b7f383f575a453b53fba0a8e94359b3a9ca4f6`.
- Publication and post-publication closure (this factual closure changes only this entry of this file). The approved manifest (SHA-256 `a3d6121084fdb2500f6ac71a0a18547a0d5dbfaf4d234bc16b610e13789fbd31`) matched the working tree before staging: 81 paths, every status, raw SHA-256 and byte size, no extra or missing path; preflight `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `8d18f72314ac32598788d37027f679a59cfa535b`, empty index. After a normal fast-forward push (`8d18f72..167f212`), `git fetch` and an independent `git ls-remote origin refs/heads/main` returned the same full SHA as local `HEAD` and `origin/main`, parent `8d18f72…`, clean checkout. Post-publication checks, all newly run sequentially on that commit with fresh logs and no rerun: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings and 0 errors; with `--no-build --no-restore` and filter `FullyQualifiedName~CodexAccountUsageWarning` Domain 47/47, Application 89/89, Infrastructure 24/24, Api 46/46; full Architecture 33/33; frontend `npx vitest run` 2057/2057 in 142 files, `npm run typecheck` exit 0, `npm run lint` exit 0 with 9 warnings (baseline), `npm run build` exit 0, `npm run test:harness` 87/87, then, after the harness, one canonical `npm run test:e2e:all` with complete output retained and exit 0: Chromium 11/11 and journeys 3/3 (the extended account-usage journey among them), no failure, skip or rerun. Generated client unchanged (`3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`), `git diff --check` clean, checkout clean, and the two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` untouched. RETAINED, not rerun at publication: the full .NET suite counts of the correction round (Application 4431, Api 1144, Domain 1209, Infrastructure 1371 passed + 4 skipped, Architecture 33), the formatter baseline (153 findings in 17 files), `npm audit`/vulnerable-package results, the mutation results and the red-first evidence of R1. Earlier evidence about the pre-correction canonical run remains as recorded; no failure occurred in this publication run. Remaining limits are unchanged: a warning check is one read at one instant of the host's Codex account, real-provider reliability is not claimed, and Claude account usage, session resume and manual compaction remain open. Preflight matched once before editing: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `8d18f72314ac32598788d37027f679a59cfa535b`, nothing staged, only the planner record modified (SHA-256 `1285a458a902c2ced2b0683b3b01e9481fda2a90b88dfe95fe16620e83b43cab`), nothing untracked, generated client `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417`. No next slice is granted.
- Delivered outcome ([ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)): an optional run-scoped advisory integer 1..100 (`Run.CodexAccountUsageWarningPercent`, migration `AddCodexAccountUsageWarning`, nullable INTEGER, no default or backfill, exact-stored-text mapping, NOT a concurrency token) set or cleared by protected `POST /api/runs/{runId}/codex-account-usage-warning` (strict `{percent}` body, 8 KiB limit proven through real Kestrel, one atomic human event, fresh execution-mode admission, lifecycle-guarded even for a same value, malformed storage reads as Unknown and is repaired by a valid set/clear). The cockpit carries the saved setting only. A separate protected `GET` at the same route performs one explicit check: no setting, invalid setting, unadmitted run or no vetted launch makes no observation; otherwise exactly one observation through the existing `IAccountUsageObserver` outside transactions, then setting, mode and launch are re-read (replaced authority is Unavailable/ConfigurationChanged). A dedicated pure policy evaluates every window (equality and a provider-reported reached state warn; invalid/partial/unavailable/expired evidence is Unavailable; 30 s, read-interval, future and passed-reset rules). The check writes nothing and reuses neither the stop's gate, facts or decisions nor the display allowance. Usage & Evidence shows the setting and "Check Codex account warning" beside the stop control with fixed copy and run-plus-setting ownership (no read on mount, save, clear, selection, catch-up or polling). No claim handler, feed, gate, supervisor, stop recording, budget, lease, permission, invocation argument, session or context path, Attempt schema, RPC method or dependency changed; `CodexAccountUsageStopGate.cs` is byte-identical to HEAD.
- Inventory: Domain `CodexAccountUsageWarning`, `CodexAccountUsageWarningReading`, `Run`, `RunEventType`; Application set command (4 files), `CodexAccountUsageWarningErrors`, nine `Policies/CodexAccountUsageWarning*` files, the `GetCodexAccountUsageWarning` query (3 files) and the cockpit query result/handler; Api set (4) and get (3) operation files, `CodexAccountUsageWarningSettingResponse`, cockpit endpoint/response; Infrastructure `RunConfiguration`, migration and snapshot; frontend client wiring, `describeCodexAccountUsageWarning`, two hooks, `CodexAccountUsageWarningControl`, rail and cockpit wiring, CSS, generated client; tests (Domain, Application policy/handler/query and the four Codex claim classes plus a Claude claim, Infrastructure migration and the four older migration down-tests, Api set/get, Architecture, Vitest hooks/describe/control/rail/wire, e2e judge and the extended journey); ADR-0026 and the index, engineering context, roadmap and protocol updates.
- Initial submission checks (historical; retained where specified in the correction evidence below): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, repeated with `--no-incremental`; generated client deleted, rebuilt and byte-identical (`3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`); `--no-build`: Domain 1209/1209 (was 1162), Application 4416/4416 (was 4314), Infrastructure 1371 passed + 4 skipped of 1375 (was 1347), Api 1144/1144 (was 1098), Architecture 33/33 (was 27). Frontend: `npx vitest run` 2057/2057 in 142 files (was 1938), `npm run typecheck` 0, strict standalone `tsc --ignoreConfig --strict --module esnext --moduleResolution bundler --allowImportingTsExtensions --types node` over the 38 tracked e2e and Playwright config files 0, `npm run lint` 0 with 9 warnings (baseline), `npm run build` 0, `npm run test:harness` 87/87 (was 82), then one canonical `npm run test:e2e:all` with a fresh unique log: Chromium 11/11 and journeys 3/3 (the extended account-usage journey among them), no failure, skip or rerun. One earlier development run of the journey alone passed. `dotnet format --verify-no-changes` 153 findings in 17 files (baseline), `npm audit` 0, `dotnet list package --vulnerable` none, no blank line at EOF or trailing whitespace on added lines, only the existing CRLF notices for generated/EF files.
- Failing-first and mutation evidence: new tests passed on first run, so red behavior comes from targeted mutations, each detected and then restored to the exact bytes (hashes compared) before a `--no-incremental` rebuild: equality `>=` to `>` (6 failures); invalid observation read as Below (5); read interval ignored (1); automatic read on mount (4); lost owner identity (8); stale completion allowed to write (1); warning refusing claims (4 claim classes plus 2 architecture); warning made a concurrency token (architecture, 2 migration, 1 handler); request-size limit removed (2 through real Kestrel); forced UPDATE removed (1).
- Review correction R1-R2 for the warning slice (after Codex's NO-GO; preflight matched: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` `8d18f72314ac32598788d37027f679a59cfa535b`, empty index, 34 modified and 46 untracked including the planner record, planner record `0176aba8…3a1f` and generated client `3e4ae6cc…b447` as expected). R1: `GetCodexAccountUsageWarningQueryHandler` now reads the exact stored execution-mode representation freshly and untracked before the observation (requiring admission), retains it, reads it again afterwards and requires both admission and exact equality; ManualAgent replaced by Legacy (or the reverse) is now Unavailable/ConfigurationChanged with no windows and no observation time. It is local to the warning query (read-only use of the existing `CurrentRunExecutionMode.ReadStoredAsync`; no shared helper, claim, gate, supervisor or persistence change). Red first on real file-backed SQLite: of 38 query-handler tests 4 failed on the previous production code (both admitted-mode replacements during the observer call and both populated-tracker variants); the unadmitted/malformed replacements (Simulated, 7, -1, 2.5, 'two', X'02', 4294967298), the same-value rewrite controls for both modes, and the unadmitted-at-start case already passed and stay as controls; after the fix all 38 passed, asserting one observation, no events or attempts, no Save and no windows or timestamp. R2: `docs/product/run-cockpit-specification.md` gained "Codex account-usage warning (advisory, explicit check)" (placement beside the stop, labels, valid/Unknown/cleared setting, local validation, explicit check only, pending/failed/unavailable/dated Below and Reached states with prior classification hidden, run-plus-authoritative-setting ownership, accepted saves, reload, advisory independence) and its "Provider account usage guardrails" paragraph now separates what is implemented (the explicit, null-by-default Codex stop and warning, ADR-0025/ADR-0026) from the future-only Claude controls, 80/90 defaults, provider-wide policy, waiting state, resumption and override; ADR-0026 and the protocol wording name the exact-mode comparison. No behavior or default was introduced for the documentation.
- Correction evidence: freshly run after the correction: solution `dotnet build` with `--no-incremental` 0 warnings and 0 errors (generated client unchanged `3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`); Application 4431/4431 (was 4416), Api 1144/1144, Architecture 33/33; `dotnet format --verify-no-changes` still 153 findings in 17 files (baseline, none in the changed files); hygiene scan over all 80 paths clean (no blank line at EOF, no trailing whitespace on added lines, only the existing CRLF/BOM notices of generated and EF files); the account-usage journey alone, once as a development run, passed (24.5 s) and cleaned only its own root. RETAINED from the first return and not rerun because nothing they cover changed: Domain 1209, Infrastructure 1371 passed + 4 skipped, Vitest 2057/2057, typecheck, strict e2e `tsc`, lint (9 warnings), build, harness 87/87, npm audit and vulnerable-package results, the ten mutation results, and the canonical `npm run test:e2e:all` (Chromium 11/11, journeys 3/3), which ran on the previous production handler and is not claimed for the corrected one; the two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched.
- Remaining limits: the check is one read at one instant of the host's Codex account, not usage attributable to the run, eligibility or capacity; real-provider reliability is not claimed (owned closed-contract double only); the browser journey configures and observes the warning through the rendered page and the double's log, with no raw SQL for it. Claude account usage, session resume and manual compaction remain open. The strict e2e `tsc` command is a reconstruction (the earlier one was not recorded). Existing SignalR negotiation console noise in the journey host output is unchanged.
- Codex final review: R1-R2 accepted; the full snapshot including the planner-owned GO record is 81 paths (35 modified, 46 new). Independent checks on the corrected code: solution build 0 warnings/0 errors, warning-focused Application 89/89 and Api 46/46, full Architecture 33/33, no selected failure or skip. Earlier independent Domain 47/47, Infrastructure 24/24 and focused Vitest 115/115 remain evidence on unchanged code. The original canonical browser counts remain retained; the corrected account-usage journey run is separately recorded above. The quoted planner hashes are historical dispatch/review states, not the final GO-record hash. Publication remains pending; the approved raw-file inventory is external so neither this entry nor the planner record embeds its own hash.

## Explicit Codex account-usage stop at claim and dispatch (2026-10-05)

- Status: published (substantive commit `ebaffc018021ac0a8880fec34b9646b738ea67b6`, verified in the publication closure below) after Codex's renewed GO for the corrected snapshot. The bullets that describe the tree as UNSTAGED, UNCOMMITTED and UNPUSHED, and the pre-publication planner hashes, are historical wording of the earlier snapshots; no next slice granted. Preflight matched once before editing: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `1c169de350f59f3bc5622cc9f92925b3376d29d4`, empty index, only `docs/roadmap/planner-handoff.md` modified (SHA-256 `7926f768299cd4f4e139bdbd1ae21e6d74aed777fcd458f98824ce1b2c569294`, verified unchanged at the end). The generated client baseline was `d283da05...b0819`; it changed only through NSwag generation, and its final hash is `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417`, reproduced after deleting the file and rebuilding.
- Publication and post-publication closure (this factual closure changes only this entry of this file; the pre-publication wording below stays as historical record). The substantive commit is `ebaffc018021ac0a8880fec34b9646b738ea67b6` (parent `1c169de350f59f3bc5622cc9f92925b3376d29d4`), published on `main` after Codex's renewed GO for the corrected snapshot, which superseded the earlier manifest authorization. Verified publication: after a normal fast-forward push (`1c169de..ebaffc0`), `git fetch` and an independent `git ls-remote origin refs/heads/main` returned the same full SHA as local `HEAD` and local `origin/main`, with a clean checkout. The commit changes exactly the 168 reviewed paths (77 modified, 91 added; 12454 insertions and 304 deletions), including this file and the planner record.
  - Publication preflight (run before the commit): branch `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `1c169de...`; empty index; 168 paths (77 modified, 91 untracked). The renewed approved manifest SHA-256 `8e372d75ea1ebdba4df8e8fc5a472164469e25a4f052759142b8297b60351894` matched, and every one of its 168 entries matched the Git inventory, raw SHA-256, byte size and status (no extra, missing or duplicated path). Key hashes at commit: planner record `e30f18bf02a5e9946ec6c72851750e8cadbd19fef7a0296fc967839056119596`, this file `bcb4c46d12f998f1ff9088531d908e8eb08decc95a6afe3b66ea30bae93e81c0`, `useSetCodexAccountUsageStop.test.ts` `f508d6371b6815d0270e7a1dcad3235bd8b95c6debc5c4e98eb0f0e21b490554`, generated client `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417`. The planner hashes quoted elsewhere in this entry (`7926f768...`, `161e7247...`, `c0d5da0b...`, and this file's `ae4588b7...`) are historical values of earlier snapshots. An earlier publication attempt was stopped, with nothing committed and the index reset, because `git diff --cached --check` reported one extra trailing blank line at the end of `useSetCodexAccountUsageStop.test.ts`; Codex removed that single LF byte (no executable change) and renewed the GO; `git diff --cached --check` then passed on the staged 168 paths.
  - Post-publication checks, all NEWLY run on the published tree, sequentially, each once, with no failed check rerun: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings and 0 errors; then `--no-build --no-restore` Domain `FullyQualifiedName~CodexAccountUsage` 102/102, Application `FullyQualifiedName~AccountUsage|FullyQualifiedName~ReconcileInterruptedAgentAttempts` 117/117, Infrastructure `FullyQualifiedName~CodexAccountUsage` 114/114, Api `FullyQualifiedName~AccountUsage` 47/47, Api (separately) the five hosted-scenario name filters 20/20 across the four supervisors, Architecture full suite 27/27; no skips in these selections. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1938/1938 in 137 files, `npm run typecheck` exit 0, `npm run lint` exit 0 with 9 warnings (the recorded baseline) and no errors, `npm run build` exit 0 (the existing chunk-size notice), `npm run test:harness` 82/82, and then, only after the harness passed, one `npm run test:e2e:all` with normal authentication, normal host composition, all supervisors and `reuseExistingServer: false`: Chromium 11 passed, then the three native-double journeys passed (the account-usage journey among them), exit 0, no failure, skip or rerun; its complete output (85 lines) is retained. The generated client hash is unchanged, `git diff --check` is clean and the checkout is clean. The two older roots (`devalcopilot-e2e-2Jzi46`, `devalcopilot-e2e-ok3LGr`) were left untouched, and these runs left no root of their own behind. A first launch of the check script on the published tree never executed (a stale output file from an earlier slice made the monitor report completion; no check had run, and its numbers were ignored); the script was then run once from the start, and its results are the ones above.
  - Retained (not re-run at publication): the full-suite counts of the correction round (Domain 1162, Application 4314, Infrastructure 1347 passed + 4 skipped, Api 1098, Architecture 27), the red-first evidence of R1-R5, the eleven mutation results and their equivalent-mutation note, the formatter baseline (153 findings in 17 files), `npm audit` and vulnerable-package checks, and the earlier Chromium failure. The post-publication run used the focused selections above for .NET, so it does not replace those full-suite counts.
  - Preserved earlier failures: the first canonical run of the first snapshot failed `e2e\project-selection.spec.ts:54` with `route.fulfill: Route is already handled!` (`harness\cockpitGate.ts:86`); its cause is still UNPROVEN, the passing repeat runs do not establish a cause or eliminate it, and no route error was swallowed and no retry or sleep was added. Two earlier full-run failures that were stale mutated binaries (8 tests) and one introduced formatter finding were corrected and are recorded above.
  - Remaining limits: the guard is a local check over a provider-reported percentage at two instants and does not prove that a later invocation will succeed; real-provider reliability is not claimed (the contract was checked against the installed codex-cli 0.160.0 schema and an owned closed-contract double). The process-local block of an attempt whose terminal recording never committed is reused by a restarted supervisor within the same host process, but it is not durable: a normal full host restart applies the existing startup interruption authority (`ReconcileInterruptedAgentAttempts`, whose selection is part of the 117 above) and no account-usage decision exists for such an attempt, because none was ever committed; this slice did not change that authority and no decision is invented for it. The Chromium route failure above is not eliminated. Claude account usage, warning thresholds, overrides, quota reservation, session resume and compaction remain out of scope, and this slice does not complete Increment 4.
- Review correction R1-R5 (historical correction-round preflight after Codex NO-GO of the first snapshot, before the current publication GO; no next slice granted). Preflight matched once: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `1c169de350f59f3bc5622cc9f92925b3376d29d4`, empty index, 77 modified and 82 untracked (159) including this file and the planner record, planner record SHA-256 `161e7247bc0052a00dc7fae574f7eacd41df89786dcbb31818ab72d4625b12ca` (preserved byte for byte to the end), client `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417` (unchanged by the corrections). Regressions were written first and run red against the submitted behaviour (retained red evidence, this round) before each fix:
  - R1, fresh persisted authority and fail closed: `RecordCodexAccountUsageStopCommand` is now an `IManualTransactionCommand`; its handler takes the write lock with a self-referential no-op update, loads the attempt, refreshes it with `ReloadAsync` (a populated tracker is never trusted), reads kind, provider, status, dispatch marker and the threshold snapshot from that fresh state, records the decision and the event with one save and commits (notification only after the commit by the caller). Facts prepared for another threshold or launch tuple now resolve as unavailable evidence for the actual snapshot (no windows attributed); facts for another attempt are still refused. `CodexAccountUsageDispatchGuard` blocks (process-local, never cleared) an attempt whose refusal could not be recorded, including a thrown durable-write failure, and the four Codex supervisors skip a blocked attempt before any capture, observation or dispatch; the blocker is logged at error level and nothing recovers it automatically. Red first: Application seam 5 of 32 failed (threshold change after preparation, stale tracked snapshot, stale tracked dispatch marker, threshold-mismatched facts, explicit transaction), and four real-supervisor hosted regressions (a real SQLite trigger makes the completion event fail; later below-threshold observations; a restarted supervisor after the fault is removed) failed on all four supervisors. Now green: zero Agent invocations, no re-observation (2 observations only), no dispatch marker, no event, and the existing hosted scenarios (one event, inert repeated polling and restart after a committed terminal completion, healthy and null controls) still pass. The block is process-local: recreating a supervisor within that host reuses it. A normal full host restart runs the existing startup recovery and read-only Agent reconciliation before supervisors; the still-Running attempt and its Run become Interrupted, without another account observation or Agent invocation for that attempt. No account-usage decision is invented when its recording never committed. This is existing interruption authority, not automatic resume or a new recovery policy.
  - R2, provenance: `Attempt.GetAgentAccountUsageDecision` now also requires agreement with the attempt's own threshold snapshot (an ordinary decision needs the actual equal valid threshold, `ThresholdUnusable` needs a really malformed snapshot, an absent snapshot confirms nothing); the recording transition and the projection share one predicate. Contradictions project as the existing unavailable fact with nothing of the decision exposed; no historical row is rewritten. Red first: Domain 3 of 4 new cases failed, API evidence 4 of 4 new cases failed (decision threshold 50, snapshot absent, snapshot changed, snapshot malformed, over real SQLite).
  - R3, frontend ownership (executed by a delegated frontend subagent, verified here by a fresh rerun): `useSetCodexAccountUsageStop(runId, settingIdentity)` owns the request guard, pending, error and refresh by the committed Run plus the authoritative setting identity, so a replacement setting, A to B to A, a retained callback, an obsolete finally and an unmount cannot touch a later owner (accepted server operations stay real); the parent key no longer provides correctness. The public note and hook comment now say that changing the setting never alters the threshold an already-claimed attempt recorded, but a claimed attempt is still checked before it starts and can be stopped. Red first (reported by the subagent): 9 new tests failed against the old hook and control (5 hook, 4 component; the fifth hook failure name was not captured).
  - R4, body bound: the set operation has `[RequestSizeLimit(8 KiB)]`. Red first through real Kestrel (`WebApplicationFactory.UseKestrel`): a 32 KiB unknown member returned 200; now 413 with no setting and no event written. Controls through the same real host: 401 without credentials, 400 for a string or fractional percent, set and clear with one event each.
  - R5, contracts: the provider-neutral port `IAccountUsageObserver` and its owned facts `AccountUsageObservation`, `AccountUsageBucket` and `AccountUsageWindow` are in `Runs/Ports`; the Codex-specific claim gate, policy, guard facts, launch tuple, evaluation and claim guard are in `Runs/Policies`; wire and RPC details stay in the Infrastructure adapter. The query result is `GetCodexAccountUsageStopPlanQueryResult` in its own file; the dispatch guard's nested `Preparation` is `CodexAccountUsageDispatchPreparation`; the grouped public test helper types (`HostedAttemptView`, `AccountUsageHostedHarness`, `AccountUsageClaimOutcome`, `AccountUsageEvidenceReader`, `AccountUsageClaimHarness`, `StubAccountUsageAdapter`, `AdjustableTimeProvider`) have their own files. The mechanical rename touched 37 existing or new files. No unrelated type was reorganized.
  - Older tests changed in this round: `Facts_that_do_not_belong_to_the_attempt_and_its_threshold_are_refused_without_a_change` became `Facts_that_belong_to_another_attempt_are_refused_without_a_change` (threshold-mismatched facts now resolve as unavailable evidence by design; covered by a new test); every harness constructor of the four hosted account-usage test files gained the SQL fault hook; the Architecture boundary test follows the new paths and names.
  - FRESH validation after the corrections (the last executable edit preceded all of these; documentation was finalized afterwards): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, repeated with `--no-incremental` after the mutation restores (the earlier stale-binary lesson), and again after deleting the generated client (regenerated, hash unchanged); sequentially with `--no-build --no-restore`: Domain 1162/1162 (was 1158), Application 4314/4314 (was 4309), Infrastructure 1347 passed + 4 skipped of 1351, Api 1098/1098 (was 1088), Architecture 27/27; these ran once before the mutation runs and again after the clean rebuild with the same results. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1938/1938 in 137 files (was 1926), `npm run typecheck` exit 0, the strict standalone `tsc` over every `e2e` file and both Playwright configurations exit 0, `npm run lint` exit 0 with 9 warnings (baseline), `npm run build` exit 0, `npm run test:harness` 82/82, then, after the harness, one canonical `npm run test:e2e:all` with the complete output retained: Chromium 11/11 (including `project-selection.spec.ts`) and the native-double journeys 3/3 (the account-usage journey among them), no failure, skip or rerun. `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files (the recorded baseline), `npm audit` 0 vulnerabilities, `dotnet list package --vulnerable` none, `git diff --check` only the existing CRLF notices, documentation links resolve, and the two older e2e roots are untouched.
  - Mutation checks after the corrections (each applied to the final source, rebuilt and run, restored from backup with equal source hashes, then a clean rebuild): equality changed failed 10 Application tests; historical evidence accepted (freshness limit removed) failed 4; the final dispatch guard skipped failed 12; the launch-tuple check removed failed 2; the recorder's refresh from the database removed failed 2; the recorder refusing threshold-mismatched facts again (the submitted behaviour) failed 2; the observation accepting a valid subset failed 6 Infrastructure tests; the display-only allowance referenced from the guard failed the Architecture test; the process-local block disabled failed all 4 hosted fail-closed regressions; the request-size limit removed failed the real-Kestrel regression; the decision/snapshot coherence check removed failed 3 Domain tests. One mutation was NOT detected and is reported as equivalent: removing only the recorder's own `facts.ThresholdPercent != threshold` test passes, because `CodexAccountUsageStopPolicy.Evaluate` independently returns no-evidence for threshold-mismatched facts; the recorder's check is redundant defence in depth.
  - Earlier Chromium failure (retained, attribution still UNPROVEN): the first canonical run of the first snapshot failed `e2e\project-selection.spec.ts:54` with `route.fulfill: Route is already handled!` at `harness\cockpitGate.ts:86` (the complete first-run output is retained in the scratchpad file `e2e-run1-failed-retained.txt`, 88 lines, with the stack, and shows only that spec failing: 10 passed, 1 failed). The spec then passed 3 of 3 alone and the whole canonical run passed once when repeated; neither establishes the cause, and no timing issue is claimed. No route error was swallowed, no retry or sleep was added to the gate, and the correction round's canonical run (above) passed on its first and only run.
  - Codex re-review factual clarification (2026-10-05): independently rebuilt the solution with zero warnings/errors and ran Domain CodexAccountUsage 102, Application AccountUsage plus ReconcileInterruptedAgentAttempts 117, Api AccountUsage 47, a separate 20 hosted tests across all four Codex supervisors, Infrastructure CodexAccountUsage 114, and Architecture 27, all passed, no skips; four focused frontend files passed 115. These are focused checks, not full suites. A separate temporary hosted probe used a real SQLite completion-event fault, disposed the first service provider, constructed a new provider with an empty guard block list, ran the existing startup recovery/reconciliation, and started the real planning supervisor: the attempt was Interrupted, account reads stayed at two, and both provider adapters had zero invocations. The probe passed, was removed, its file restored byte-for-byte, and the test project rebuilt. This establishes the relevant fresh-container startup boundary; it is not a new full Program browser run. Codex corrected the restart description in this entry, ADR-0025 and the protocol, one misleading test comment, and mixed line endings in the setter hook test; no executable production/test behavior changed. Final reviewer hygiene covered all 168 files and 227 local Markdown links with no issues. The executor canonical browser output was inspected and contains both 11 Chromium passes and 3 journey passes. The earlier route failure remains unproven and preserved.
- Publication preflight whitespace correction (2026-10-05, before commit): the executor verified the approved 168-path manifest and stopped when the real staged check found one new blank line at EOF in `useSetCodexAccountUsageStop.test.ts`. It cleared the index; no commit or push occurred. Codex independently verified the original manifest and every file, removed exactly that final extra LF byte, preserving the required final newline, and updated the review decision and external manifest. No executable behavior changed; earlier test results remain applicable. The full snapshot, including all new files, is checked through a separate temporary index before renewed publication GO. The earlier unstaged diff check did not cover this untracked EOF finding.
- Inventory after the corrections (supersedes the first snapshot's counts below): 168 working-tree paths = 77 modified + 91 new, nothing staged; excluding the planner record (modified, preserved), 167 executor paths including this file. Production: Domain 12 (5 modified, 7 new), Application 35 (10 modified, 25 new), Infrastructure 7 (3 modified, 4 new), Api 18 (9 modified, 9 new). Frontend: `src` 27 (18 modified, 9 new), `e2e` 5 (3 modified, 2 new). Tests 57 (23 modified, 34 new; the review's count of 50 was at the first snapshot, before the new regressions and the split helper files). Documentation 7 (6 modified: the protocol, the ADR index, the engineering context, the roadmap plan, this file and the planner record; 1 new: ADR-0025).
- What it delivers: a human can set or clear an optional run-scoped Codex account-usage percentage stop (integer 1..100; null disables and adds no provider read) through `POST /api/runs/{runId}/codex-account-usage-stop`. It is snapshotted immutably on every new Codex attempt (planning, challenge resolution, code review and re-review, verification diagnosis, and their format repairs), enforced at the claim and again immediately before dispatch from a strict, all-or-nothing, read-only App Server observation behind a provider-neutral port (never the display-only allowance projection), and a pre-dispatch refusal terminates the claimed attempt atomically with a bounded canonical decision and one completion event (`AccountUsageStopReached` or `AccountUsageEvidenceUnavailable`): no dispatch marker, no Agent invocation, budgets and grants stay spent, no retry. The cockpit and attempt evidence show the setting and the historical decision separately from token activity and the allowance. [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md) (additive; changes no existing authority decision).
- Contract facts: equality stops; every bucket and window is evaluated; invalid, partial, duplicated, unavailable or expired evidence refuses (30-second host freshness, in-interval retrieval instant, no future dating, a passed reset is expired); the observation is made outside EF transactions, the claim seam re-reads and compares inside the short claim transaction, and `MarkAgentAttemptDispatchedCommand` independently validates the pre-dispatch facts against the stored snapshot and the vetted launch tuple; `RecordCodexAccountUsageStopCommand` is the one terminal recorder and refuses to stop an attempt the policy permits. The reply schema was checked against the locally generated schema of the installed codex-cli 0.160.0. The decision text is versioned (`codex-account-rate-limits-v1`, at most 8192 bytes) and read back strictly. Migration `AddCodexAccountUsageStop` adds the nullable Run column, the Attempt snapshot column and the Attempt decision column (exact-stored-text integer mapping, strict storage-class repair, concurrency token).
- FIRST-SNAPSHOT inventory (superseded by the inventory after the corrections; its test count of 54 was wrong, the reviewed count was 50): Inventory: 158 executor paths (76 modified, 82 new; nothing staged), plus `planner-handoff.md` modified and preserved (not counted) and this file. Domain 12 (5 modified, 7 new); Application 34 (10 modified, 24 new, including the claim gate, the policy, the observation and the three new command/query folders); Infrastructure 7 (3 modified, 4 new: the strict adapter, the migration and its designer, the snapshot is modified); Api 17 (9 modified, 8 new, including the set-stop endpoint, `CodexAccountUsageDispatchGuard` and the four supervisors); frontend `src` 27 (18 modified, 9 new: control, decision fact, hook, describe/label helpers and their tests, the generated client); frontend `e2e` 5 (3 modified, 2 new: `account-usage-stop.journey.ts` and `journey/accountUsage.ts`); tests 54 (Domain 2 new, Application 4 modified and 10 new, Infrastructure 6 modified and 3 new, Api 13 modified and 11 new including the owned closed-contract Codex double role, Architecture 1 new); documentation 6 (ADR-0025 new; the ADR index, the engineering context, the protocol, the roadmap plan and this file modified).
- Disclosed older-test changes (all additive consequences of the new columns and members, none weakens an assertion): three Infrastructure migration tests had their Down expectations extended and three historical seeds were routed through `HistoricalEntityRow` (`AddClaudeMutationTurnLimit`, `AddDiagnosisCorrectionEscalation`, `AddDirectHumanGuidance`, `AddPlanningImplementationAuthorization`, `AddRunExecutionMode` migration tests); the Api `AgentModelContextLimitsEndpointTests` member-order assertion now lists the two new trailing evidence members.
- Regressions first: the claim-seam tests were written before the control and were red (72 of 88 failed against an inert wiring) before the real wiring made them pass (retained from the build-up of this slice, not rerun red). The final suites below are FRESH.
- FIRST-SNAPSHOT validation (retained, superseded by the fresh validation after the corrections): FRESH validation on the final tree (the last production edit was a one-character whitespace correction in `CreateCodexPlanningAttemptCommandHandler`, after which the solution build, the 217 affected Application tests, the formatter count and the client hash were rerun; the five full suites ran once on the same tree immediately before that edit, on a clean `--no-incremental` build after the mutation restores): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors (also after deleting the generated client; hash unchanged); Domain 1158/1158, Application 4309/4309, Infrastructure 1347 passed + 4 skipped of 1351 (the existing environment-gated skips), Api 1088/1088, Architecture 27/27. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1926/1926 (137 files), `npm run typecheck` exit 0, the strict standalone `tsc` over every `e2e` file and both Playwright configurations exit 0, `npm run lint` exit 0 with 9 warnings (the baseline), `npm run build` exit 0, `npm run test:harness` 82/82, then one canonical `npm run test:e2e:all`: Chromium 11/11 and the native-double journeys 3/3 (the new account-usage journey among them), full output captured. A FIRST canonical run failed one unrelated Chromium test (`project-selection.spec.ts` "Route is already handled!" in `cockpitGate.ts`, failure attribution unproven); that spec then passed 3 of 3 alone and the whole canonical run was repeated once clean. `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the recorded baseline (one extra finding I introduced was fixed). `npm audit` 0 vulnerabilities, `dotnet list package --vulnerable` none, `git diff --check` no whitespace errors (only the existing CRLF notices), documentation links of ADR-0025 resolve, and the two older e2e roots (`devalcopilot-e2e-2Jzi46`, `devalcopilot-e2e-ok3LGr`) are untouched.
- FIRST-SNAPSHOT mutation checks (retained as earlier evidence; the corrections have their own mutation results): Mutation checks on the final production source (each applied, rebuilt and run, detected, restored from backup, source hash equal before and after, then a clean rebuild): an observation that accepts a valid subset (an invalid secondary window read as absent) failed 6 Infrastructure adapter tests; equality changed (`>=` to `>`) failed 10 Application tests; the final dispatch guard skipped failed 11; the launch-tuple check removed failed 2; stale/historical evidence accepted (freshness limit removed) failed 4; display-only allowance projection referenced from the guard failed the Architecture boundary test. All mutations were detected.
- Browser scenario (real Chromium, authenticated Program composition, all supervisors, generated-client configuration, the real strict adapter against the owned closed-contract double): configure 80, a claim-time read of 90 is refused with no attempt, a below-then-reaching read stops the claimed attempt before dispatch with no Agent invocation and one canonical decision shown with its host retrieval time and windows, the decision survives a reload, a later allowed request runs once with the same snapshot, Claude is unaffected, and clearing the setting makes the next Codex stage observe nothing. The journey waits for the rail's display-only allowance read to settle before scripting, because that read shares the double.
- Open risks and limits: the guard is a local check over a provider-reported percentage at two instants; it does not prove a later invocation will succeed and never cancels dispatched work. Two additional App Server reads occur per configured Codex attempt. Real-provider reliability is not claimed (the strict contract was verified against the installed schema and an owned double, not a live account). Claude account usage, warning thresholds, overrides, quota reservation, session resume and compaction are not addressed. Next slice selection belongs to the planner/reviewer.

## Attested tracked-change text in new Agent manifests (2026-10-04)

- Status: published (substantive commit `65ab7fe156f4b7d91337b764ebfbfdf4ce885ada`, verified below) after Codex's GO for the reviewed R1-R2 corrected snapshot. The bullets below that describe the tree as UNSTAGED, UNCOMMITTED and UNPUSHED, and every planner-record hash other than the one named in the closure bullet, are the historical pre-publication state. The executor selected no next slice and claims no Increment 4 completion.
- Publication and post-publication closure (this factual closure changes only this file): after the GO the executor verified the approved manifest's SHA-256 (`d7eefee82fe11a3de8116635baffbd40e29ceb4629b843f8acac8f4644cc80ff`), the exact Git inventory (69 paths, no extra or missing) and every file's raw SHA-256, size and status, staged only those 69 paths (staged set equal to the manifest, `git diff --cached --check` clean), and made no edit before the commit. Substantive commit `65ab7fe156f4b7d91337b764ebfbfdf4ce885ada` has parent `1d61092e981e714e7a3bc5ac139423b587247bec` and contains exactly the 69 reviewed paths, including the planner's GO record (raw SHA-256 `4e4effb6a96c5da27c8c744afa4fc3872a534d5c220016e99336b8e7de670dc9`; the earlier `ae9a4e20…` and `3340b566…` hashes are historical). A normal fast-forward push (`1d61092..65ab7fe`) was followed by fetch and verification: HEAD, local `origin/main` and live `refs/heads/main` all equal the commit, with a clean checkout; nothing was amended, forced or reconciled. FRESH post-publication checks on that commit, run sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; Application `Tracked|AgentEvidenceProjection|InstructionContext` 629/629; Infrastructure `TrackedFileSourceReaderTests|GitWorkspaceTracked` 84/84; Api `InstructionDeliveryProjectionTests|UntrackedPreviewClaimDeliveryTests|UntrackedPreviewFormsDeliveryTests|ClaudeCriticalReviewSupervisorHostedTests` 106/106; full Architecture 20/20 (all `--no-build --no-restore`, none skipped); Vitest 1802/1802, typecheck exit 0, lint exit 0 (the baseline warnings only), production build exit 0 (the existing chunk-size notice), harness 76/76, then one canonical `npm run test:e2e:all` (normal authentication, host composition, all supervisors) exit 0 with the two native-double journeys passed (the tail I captured shows the journeys and the existing vite client-connection negotiation console noise; the Chromium count line was not in it, only the exit code 0 stands as evidence for the Chromium specs). The generated client stayed `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819`; `git diff --check` and the checkout were clean; the run left no owned root of its own and the two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched. RETAINED, not repeated on the published commit (identical code and tests): the full Domain 1056, Application 4133, Infrastructure 1233 + 4 skipped and Api 1020 suites, the ownership and retention mutation set (O1-O5, R-a to R-e) and the first submission's mutations M1-M12, the strict e2e `tsc`, the formatter baseline (153 findings), audits and hygiene/link checks. All failures preserved above and their corrections stand; none occurred in the post-publication run. The remaining limits and risks below are unchanged. No next slice is selected here and Increment 4 completion is not claimed.
- Review correction R1-R2 (Codex NO-GO, same tree, still UNSTAGED, UNCOMMITTED and UNPUSHED; no commit/push GO and no next slice granted). Preflight matched: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` all `1d61092e981e714e7a3bc5ac139423b587247bec`, empty index, 47 modified and 18 untracked paths, planner record SHA-256 `3340b56646d2279bff125109f95e66718e6a127628ba9d55ac77f9cd1c271e95` (preserved; unchanged at the end), client `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819` (unchanged after the rebuild).
  - R1, owned immutable facts: `GitWorkspaceEvidenceResult` now copies whatever collection it receives into a new Application type `GitWorkspaceTrackedFiles` (private array copy; implements only `IReadOnlyList`, so it is not an array, not a `List` and not an `IList`; a null element is refused; an already-owned snapshot is reused). The copy happens in the constructor and in the `with` replacement (`init`), so the caller's list or array, a `with` source, a projected copy and a deconstructed value are all independent of what the result holds, and no cast of the returned collection reaches a write path. The per-file record and every builder-side validation are unchanged.
  - R2, retained budget: baseline text enters the observer's cache only when its file is admitted (where the facts already hold the same string), never when it is read; a result without text (binary, unverified, unavailable) is still remembered. A baseline whose file is omitted afterwards (no content difference, invalid current bytes, too many lines) leaves no text alive, so the cache can never exceed the observation's accounted facts nor the 512 KiB limit. Processing order, omission reasons, identity checks, the pre-read aggregate check, both observations and the bounds are unchanged; nothing was raised or removed. For the test seam only, `BaselineRead`, `ObserveTrackedFilesAsync` and the observation class became `internal` (no behavior change).
  - Regressions written first, red on the submitted logic (retained red evidence): Application `TrackedFactsOwnershipTests` 16 of 17 failed (caller list and array mutation, every cast and mutation route on the returned collection, `with`, projected copy, deconstruction, null fact, sealed manifests of all seven variants showing the forged text); Infrastructure `GitWorkspaceTrackedFactsOwnershipTests` (the real reader's capture cast to `IList` and emptied) 1 of 1 failed; `GitWorkspaceTrackedRetentionTests` 5 of 9 failed with the retention oracle (the cache's source bytes after each observation; the reproduction showed 819200 bytes for four distinct 200 KiB files in real `MM` state), including invalid-UTF-8 and line-limit omissions (307200 bytes) and both observations; the other four (shared admitted blob, shared blob omitted beside a changed sibling, exact limit, one byte over) are guards that pass on both trees. A first draft of the retention tests had setup mistakes of my own (a later commit swept the earlier staged change into HEAD, and the ordinary capture's own 512 KiB raw cap refused the large-file cases), corrected before the red evidence above was taken; the red run is the corrected one.
  - Mutations on the FINAL tree (each applied alone, both affected selections rebuilt and run, file restored from a backup, hash compared identical, timestamp renewed): O1 snapshot aliases a caller array: 1 Application failure; O5 null fact accepted: 1; O2 property keeps the caller collection: 16 Application and 1 Infrastructure; O3 `with` replacement not snapshotted: 1; O4 copy is a mutable list: 13 and 1; R-a baseline text cached at read: 4 Infrastructure; R-b admitted baseline never cached: 5; R-c pre-read retained check removed: 2; R-d retained never accumulates: 2; R-e baseline cached before the later omissions: 4. All killed, all files restored identical, then the solution was rebuilt before every later check.
  - FRESH validation after the last production edit: `dotnet build DevalCopilot.slnx -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors (twice: before the suites and after the mutation restores; client hash unchanged); affected first (the new classes), then sequentially `--no-build`: Domain 1056, Application 4133 (was 4116, +17), Infrastructure 1233 passed + 4 skipped (the same environment gates; was 1224, +9), Api 1020, Architecture 20; after the mutation restores the Tracked/AgentEvidenceProjection Application selection 538/538 and the tracked Infrastructure selection 84/84. Frontend: Vitest 1802/1802, typecheck exit 0, lint 9 warnings (baseline)/0 errors, build exit 0, harness 76/76, then one canonical `npm run test:e2e:all` (normal authentication, Program composition, all supervisors): the journeys 2/2 passed and exit 0 (its tail shows the existing client-connection negotiation console noise the vite server logs on restart; no failure, skip or rerun). `dotnet format --verify-no-changes` 153 findings, the baseline, none in a file or line this correction wrote; hygiene over 64 changed or new `.cs`/`.md` files (no CR, NUL, trailing whitespace or missing final newline) 0 problems; `git diff --check` clean apart from the preserved planner record's line-ending notice. RETAINED from the first submission, not rerun because no frontend, API, client, dependency or provider file changed in this correction: the strict standalone e2e `tsc` check, `npm audit`, `dotnet list package --vulnerable` and the first submission's mutation set M1-M12; they remain valid for code this correction did not touch.
  - Inventory after the correction: 68 executor paths (46 modified, 22 untracked; `planner-handoff.md` additionally modified and preserved). New versus the earlier inventory: production `GitWorkspaceTrackedFiles.cs`; tests `TrackedFactsOwnershipTests` (Application), `GitWorkspaceTrackedFactsOwnershipTests` and `GitWorkspaceTrackedRetentionTests` (Infrastructure); modified in addition: `IGitWorkspaceEvidenceReader.cs` (result record, already counted), `GitWorkspaceEvidenceReader.TrackedFiles.cs` (already counted), ADR-0024 and the protocol (already counted). Remaining disclosed: the result's other collections (changed paths, untracked previews) are still caller-owned lists as before; this correction covered the tracked facts only.
- Scope and parent: the single bounded Increment 4 slice selected by Codex, prepared on parent `1d61092e981e714e7a3bc5ac139423b587247bec` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` all matched it before editing; empty index; only `docs/roadmap/planner-handoff.md` modified, SHA-256 `ae9a4e20b0eb3608cbb9fc5628874da73dcc7ec2cdd491cbe7aadabfd7185b16` (CRLF, preserved byte for byte; re-verified at the end); no untracked file). Generated client SHA-256 `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819`, unchanged.
- What it delivers: a NEWLY claimed Agent manifest delivers tracked-file diff, whole hunks and changed-line samples only from physically proven current bytes and the exact blob of the captured HEAD. Git's working-path patch, which reopens repository pathnames and so could carry the text of a file that another name (inside or outside the worktree) reaches, is no longer delivered to any role, the sealed manifest, the provider input or the sealed-artifact viewer. Unsafe or unprovable files are explicit fixed-reason omissions and their safe siblings stay useful. [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md) (narrowly advances ADR-0021 and ADR-0022 for NEW tracked delivery; changes no authority decision).
  - Capture and facts: `CaptureForAgentContextAsync` still takes the raw status/HEAD/diff/hash observation for the fingerprint (fingerprint, serialization, `CaptureAsync`, `CaptureWithUntrackedPreviewsAsync` and the ordinary checkpoint diff query are unchanged and still carry the raw patch), but its returned capture has `CompleteDiff` null and a new `TrackedFiles`: one owned, immutable, provider-neutral `GitWorkspaceTrackedFile` (attested before/after text or one fixed `GitWorkspaceTrackedOmission`) per tracked changed path in ordinal order. Observed inside the existing bracket and again after it; a source whose bytes did not hold is `RepositoryChangedDuringCapture`.
  - Current side (new `TrackedFileSourceReader`, reusing `WindowsFinalPathResolver` and `WindowsHandleFileFacts` and the existing facts seam): one held handle; before any length or byte the final path must be exactly (case-sensitively) the resolved root plus the reported relative path and the handle a regular, non-device, non-reparse, single-link file; bounded read; the same proof again; Git hashes exactly those bytes on stdin (`hash-object --no-filters --stdin`, no repository pathname given to Git); the same handle is reread and proven once more, and Git's identity, the host's blob computation and the bytes must agree. A deletion needs the immediate parent physically proven as the owned directory before and after a failed open (a case variant or a link standing there is unproven, the exact reported name is a change); an access failure is never absence. Other hosts omit.
  - Baseline: fixed typed plumbing only. `ls-tree -z -l <full commit> --` with `--literal-pathspecs` (batches of at most 32) gives mode, type, object identity and size; `cat-file blob <id>` runs only for an acceptable current side; both with `--no-replace-objects`, `GIT_NO_REPLACE_OBJECTS=1` and `GIT_NO_LAZY_FETCH=1` (the installed Git 2.53.0.windows.1 was shown here to refuse a lazy fetch with that variable and to perform it without), the existing hardening, no filter, textconv, shell or object write. Size and type are checked before content; the decoded process string is accepted only when its re-encoded bytes equal the recorded size and the object identity (NUL means binary first). The baseline is cached per object for the second observation.
  - Host comparison (`TrackedComparison`, Application): linear common-prefix and common-suffix scans, one complete replacement hunk for the middle, at most three unchanged context lines per edge, exact terminators and final-newline state (the standard marker), canonical quoted paths, numeric ranges, no raw header, function text, mode or index line, no dependence on the repository's diff prefix, filter, textconv or EOL settings. The fixed limitation (not Git's minimal or filter-normalized patch; unchanged middle lines can appear replaced; line-ending-only differences stay visible; modes and renames not compared) is stated in every manifest with a tracked path as `changeEvidence.trackedComparison` (`host_prefix_suffix_v1`; a shorter form of the notice survives the counts-only reduction). No optimizer, temporary file or raw fallback exists.
  - Supported and omitted: edits, staged additions (`A `, `AM`), deletions (` D`, `D `, `MD`) and empty files are compared; unmerged, type changes, intent-to-add, a path also untracked, symbolic links, submodules and other modes, binary, invalid UTF-8, unencodable paths, unproven/unreadable/non-regular sources, files outside the bounds, identical bytes (a mode-only change) and the two reserved root names are explicit omissions (18 fixed reader reasons, plus the builder-derived `not_attested` and `attestation_incoherent`). Bounds: 256 KiB and 8192 lines per source, 512 KiB retained per observation in ordinal path order (`aggregate_limit`), the existing 128 paths, 512 KiB raw capture and 10 s timeout; nothing is truncated into apparently complete text.
  - Builders re-derive: `TrackedChangeEvidence.Derive` (Application) is the only way text reaches `ChangeEvidenceManifest` and all six change-evidence builders (seven variants and nineteen forms; planning carries none) take it instead of the old raw `string? completeDiff`. It accounts for every tracked path once; no facts means no text and `not_attested`; facts for a path that is not a tracked change are ignored; the porcelain state (`GitWorkspaceTrackedStatus`, shared with the reader) decides which sides may be absent; bounds and the retained budget are checked again; duplicates and omissions that still carry text are incoherent; the reserved names are always omitted. Omitted paths are merged into the existing parsed file list as omitted files, so counts, items, whole-hunk selection, 8 KiB selection, incomplete samples and the 32 KiB ladder are unchanged; `diffSelection.omissionReasons` (reason to count) survives every reduction step. Planning carries no change evidence.
  - Forward only: sealed manifests replay their exact bytes (a manifest in the old raw shape is delivered unchanged through the real adapter); nothing rebuilds, filters or reseals at dispatch.
  - Not changed (exclusions held): no HTTP, DTO or client change, no database or migration, no dependency, provider argument or permission, budget, authorization, lifecycle, scheduling, recovery or browser control; no real provider invoked; planner-handoff untouched.
  - Documentation: ADR-0024 and its index entry; the engineering context; the protocol (a new "Attested tracked-change text in Agent manifests" section, a lead-in to the tracked-hunk section, the untracked-preview limits bullet and the instruction-context delivery-projection bullet, whose raw-header cutting and `reserved_instruction_diff_withheld` are now described as superseded for new delivery); the roadmap's one gap statement; this record.
- Inventory (64 paths: 46 tracked modified, 18 untracked; nothing staged; `planner-handoff.md` is additionally modified and preserved, and is not counted). Production, 25 (18 modified, 7 new): Application 21 (16 modified: the reader port `IGitWorkspaceEvidenceReader`, `AgentEvidenceProjection`, `ChangeEvidenceManifest`, the six change-evidence manifest builders and the seven claim handlers that capture change evidence; 5 new: `GitWorkspaceTrackedFile`, `GitWorkspaceTrackedOmission`, `GitWorkspaceTrackedStatus`, `TrackedChangeEvidence`, `TrackedComparison`); Infrastructure 4 (`GitWorkspaceEvidenceReader` modified, `UntrackedFilePreviewReader` modified only to make `IsPlainRelativePath` internal, new `GitWorkspaceEvidenceReader.TrackedFiles.cs` and `TrackedFileSourceReader.cs`). Documentation 6 (5 modified: the protocol, the ADR index, the engineering context, the roadmap plan and this record; 1 new: ADR-0024). Frontend e2e 4 (modified: `collaboration.journey.ts`, `escalated-plan.journey.ts` and `harness/journeyHarness.test.ts`; new `journey/trackedDelivery.ts`). Tests 29 (20 modified, 9 new): Application 19 (14 modified, including the rewritten `AgentEvidenceProjectionTests`; 5 new: `TrackedComparisonTests`, `TrackedChangeEvidenceTests`, `TrackedAttestedManifestTests`, `PatchReconstruction`, `TrackedFixture`); Infrastructure 4 (1 modified; 3 new: `GitWorkspaceTrackedAttestationTests`, `TrackedFileSourceReaderTests`, `GitWorkspaceTrackedParentRegressionTests`); Api 5 (all modified; the shared untracked-preview scene gained tracked fixtures); Architecture 1 new (`TrackedAttestationBoundaryTests`).
- Tests written first against the unchanged parent (disclosed, retained red evidence): `GitWorkspaceTrackedParentRegressionTests` (two real NTFS cases: a tracked file replaced by hard links to an outside file at a root and a nested path, and a tracked file with a second name inside the worktree) failed 2 of 2 on the parent for the stated reason, `Assert.DoesNotContain() Failure: Sub-string found` for the outside sentinel in the serialized Agent capture (the raw patch carried `+TOP SECRET TEXT FROM OUTSIDE THE WORKTREE`), and pass after the change. They are kept as regressions.
- Coverage (real boundaries; expected texts are independent literals): Application: the comparison against a separate independent patch applier for 27 before/after cases (middle, first and last line, insertion, deletion, whole replacement, additions and deletions, empty files, adjacent edits, terminators and missing final newline in every combination, CRLF, lone CR, BOM, non-ASCII and surrogates, repeated lines, header-looking and marker-looking content) proving the accepted whole comparison rebuilds exactly the intended after bytes from exactly the before bytes, exact context and range literals, 11 quoted-path forms decoded back by the parser, a path that spells a header cannot forge a file or hunk, linear time at 8192 lines; the derivation (modified/added/deleted/staged states, no attestation, ignored foreign and untracked facts, duplicates, wrong absent side, 10 unmerged/unsupported states, every omission reason beside a safe sibling, reserved names in four spellings, bounds inclusive and exceeded, the retained budget, identical text, unencodable paths, every path accounted once); every builder variant (seven) and the diagnosis builder for complete inlining with the stated limitation, missing attestation, mixed omissions with truthful counts, a reduction sweep to the counts-only form with the accounting and limitation surviving every step and the ceiling held, oversized hunk plus incomplete sample, whole hunks in ordinal order, no tracked path, forged boundaries; the review-correction claim handler with and without attestation next to a raw-patch sentinel; a real-Git manifest (binary, mode-only, quoted path, no final newline, staged rewrite sampled). Infrastructure (real Git, real NTFS hard links and junctions, no skip): `TrackedFileSourceReaderTests` (26: single-name proof, outside link, inside second name, unavailable/reparse/device facts, directory, missing, junction component, case variant, path syntax tricks, length bound inclusive, proof before length, a first refusal stops everything, locked file, second name during the read, before verification, same-length and shorter rewrites, absence proof under owned parent, missing parent, junction parent, case variant, present file) and `GitWorkspaceTrackedAttestationTests` (39 methods, 47 cases: ordinary edits, additions, deletions, empty files and exact line endings in ordinal order, only tracked changes, determinism, ordinary captures unchanged, non-ASCII/space paths, BOM and executable mode, outside links at root and nested with no identity or object read for them, inside second names, unavailable facts, no containment proof, junction parent, second name during the read, after the identity call, before the second observation, bytes changed after identification, a one-time change retried without mixing, a change seen only by the second observation, a reappearing deleted file, Git identity wrong/empty/failed, deleted directory, binary and invalid text on both sides, intent-to-add and symbolic-link baseline, unmerged, gitlink replaced by a file, corrupted loose object caught by the raw observation, failed baseline reads, a failed batch listing retried by name, truncated/appended/same-length/empty baseline bytes, a baseline the stream cannot reproduce, replacement objects, argument and environment hardening, diff prefix settings, attributes/diff-driver function text/EOL settings, bounds inclusive and exceeded on both sides, the retained budget, 128/129 paths, no raw patch or outside text in the returned capture). Api (real Git, hard links, real artifact store, REAL adapters, no skip): the 19 builder forms over a real worktree with tracked hard links to an outside file (no sentinel in the whole returned capture, the sealed manifest or the adapter's actual stdin; sibling comparison present exactly once; unsafe paths counted `containment_unproven`), a manifest in the historical raw shape delivered byte for byte, the real claim handlers for review, resolution and implementation, a restart replay where the sealed tracked manifest replays its exact bytes while a fresh claim omits the newly aliased file, and the existing hosted restart test now sealed from attested facts. Architecture: `CompleteDiff` is read by no Runs source and only by the ordinary checkpoint-diff endpoint and handler; the derivation is called only by the claim handlers; composed evidence is constructed only by its own derivation; no supervisor, endpoint or adapter names the attestation types; only the capture reader constructs facts in Infrastructure.
- Browser journeys (existing native-double journeys extended; no new framework, no real provider): `journey/trackedDelivery.ts` (a pure judge with its own independent patch applier) is called by both journeys over the sealed manifests read back read-only. Stages before the first edit must carry no change evidence (planning none at all); VerificationDiagnosis and ReviewCorrection must carry exactly the host comparison of the committed `src/Feature.cs` against the defect, ImplementationReview against the correction; each patch is applied to the text the journey itself committed and must rebuild the expected file, the last one the worktree's own bytes; headers must be exactly the host's (no `index` line, bare numeric hunk headers with no function text), the limitation must be stated and the evidence complete. Seven new harness regressions cover accept, raw patch, function-text header, missing/altered statement, wrong rebuild, unchanged-stage evidence, wrong path, missing manifest and non-JSON.
- Mutation checks (each applied alone to the production file, the focused selections run on a fresh build, then restored by the runner and the final full suites rebuilt and run; red counts are failed tests in the selection): pre-proof removed (M1) 3 failed; post-read recheck removed (M2) 2; verification reread removed (M3a) survived at first because a rewrite also changed the length, so the same-length rewrite test was added and then 1 failed; verification proof removed (M3b) 2; byte agreement between Git's and the host's identity removed (M4a) 1; baseline identity removed (M4b) 1; raw fallback by keeping `CompleteDiff` (M5a) 10; raw fallback by treating a capture with no attestation as its raw patch (M5b) 2; omission dropped instead of counted (M6a) 90; the per-reason counts removed (M6b) 37; omissions not merged into the selection (M6c) 28; replay rebuilding at dispatch in the real adapter (M7) 3; derivation no longer overrides unsupported or unmerged states (M8) 11; reserved names no longer withheld (M9) 28; replacement objects not disabled (M10a) 1 and lazy fetching not disabled (M10b) 1; retained budget unenforced (M11) 1; a deletion needing no proven owned parent (M12, both checks removed) 3. Two mutants survive and are disclosed: removing only the baseline length comparison (M4c) is equivalent because the SHA-1 identity subsumes it, and removing only the parent proof before the failed open survives because the proof after it gives the same answer for any stable parent (it matters only for a parent that flaps within the open). Several first-run build errors of mutation definitions (an unreachable-code mutant) were corrected and rerun; they are not counted.
- Failures preserved (all explained; none retried unchanged into green): first Application run after the production change: 14 failures (two exact-shape assertions across seven variants did not list the new `trackedComparison` member), corrected by updating the expected member list; first Infrastructure runs of the new class: 3 test errors (two assertions assumed one `ls-tree` call although the two observations make two, and a fingerprint equality that cannot hold under `diff.noprefix` because the raw patch is the fingerprint's input), corrected; a real-Git manifest expected `bin.dat` as `binary` but the baseline carried NUL and invalid UTF-8 and was reported `baseline_unverified`, which led to the one production refinement that a NUL in the decoded baseline is classified binary first; the Architecture class first matched the documentation comment that names `CompleteDiff`, so it now ignores comment lines; the full Infrastructure run after the change had 1 failure, `GitWorkspaceInstructionContextTests.Ordinary_captures_carry_no_instruction_context_and_the_fingerprint_does_not_depend_on_it`, which compared the ordinary capture's raw patch with the Agent capture's (now deliberately absent), updated and rerun; an Api assertion looked for `+` in JSON that the serializer escapes as `+`, corrected. No other failure occurred.
- Changes to earlier accepted tests (disclosed): the raw-header projection tests of `AgentEvidenceProjectionTests` (about ten tests of `WithholdReservedDiff`, undecodable `diff.noprefix`/`diff.mnemonicprefix`/rename/bad-quote/CR headers and the whole-diff withholding `reserved_instruction_diff_withheld`) were removed with that production code and replaced by tests of the new projection and of per-path reserved omissions at every reduction step; the three `InstructionDeliveryProjectionTests` cases that asserted the whole-diff withholding or that the Agent capture's diff equals the raw diff were rewritten for the new contract (the reserved path is one omission, unrelated changes are delivered, repository prefix settings have no influence, the ordinary observation is untouched); the two existing exact-shape tests gained the `trackedComparison` member; text-level selector, sampler, parser and fitting tests keep their fixtures through an internal test seam (`TrackedDiffFixture.Composed`, which builds the internal `TrackedChangeEvidence` from host-comparison text and is unreachable from production); `TrackedDiffRealGitTests` and `ClaudeCriticalReviewSupervisorHostedTests.After_a_restart_...` now seal from the real attested capture and attested facts; `GitWorkspaceInstructionContextTests` changed one assertion as described; the shared `UntrackedPreviewScene` gained tracked fixtures. No assertion about untracked previews, instruction context, token, model-limit or budget behavior was weakened.
- Executor FRESH validation on the final tree (the last production edit preceded all of these; documentation was finalized afterwards): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors, and again after deleting the generated client (regenerated by NSwag; hash unchanged); affected selections first (the new Application, Infrastructure, Api and Architecture classes), then sequentially with `--no-build --no-restore`: Domain 1056/1056 (unchanged), Application 4116/4116 (was 3958), Infrastructure 1224 passed + 4 skipped of 1228 (was 1149 + 4; the skips are the existing environment-gated file-symlink and reparse-point tests), Api 1020/1020 (was 997), Architecture 20/20 (was 13). From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1802/1802, `npm run typecheck` exit 0, the standalone strict `npx tsc --ignoreConfig --noEmit --strict --target es2023 --lib es2023,dom --module esnext --moduleResolution bundler --skipLibCheck --allowImportingTsExtensions --verbatimModuleSyntax --types node --noUnusedLocals --noUnusedParameters --erasableSyntaxOnly` over every `e2e` file and both Playwright configurations exit 0, `npm run lint` exit 0 with 9 warnings (the baseline) and 0 errors, `npm run build` exit 0 (existing chunk-size notice), `npm run test:harness` 76/76 (was 69), then, only after the harness passed, one canonical `npm run test:e2e:all` with normal authentication, Program composition and all supervisors: Chromium 11/11 and the native-double journeys 2/2, no failure, skip or rerun. `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the recorded baseline (the two changed files among them, `CodeReviewContextManifestBuilder` and `UntrackedFilePreviewReader`, carry their pre-existing findings on lines this change did not write); `dotnet list package --vulnerable --include-transitive` none and `npm audit` 0 vulnerabilities (both newly run, no dependency changed); local Markdown links in the changed Markdown files and file hygiene (no NUL, carriage return, trailing whitespace or missing final newline in the 63 changed or new files other than the preserved planner record and the generated client) 0 problems; `HEAD` unchanged, nothing staged; the two older owned roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched and the run left none of its own. RETAINED and not rerun: nothing from earlier slices is relied on; the process-double journeys do not prove real-provider reliability.
- Remaining limits and risks: (1) the raw Git observation behind the checkpoint fingerprint (`status`, `diff`, the per-path `hash-object`) and the ordinary checkpoint diff still read named paths and can read an outside hard link; this slice does not claim to harden them, so the checkpoint diff shown by the existing authenticated query still carries that route. (2) Committed blob content is not confidential by inference, and admitted content is unredacted in the provider input and the sealed-artifact viewer. (3) Link topology and content can change after observation; only the bracketed observations and the checks around the identity operation are proven. (4) Only Windows has the physical proof; other hosts omit every file. (5) The conservative comparison is not Git's minimal patch: unchanged lines inside a replaced middle appear as removed and added, so a scattered edit in a large file is verbose and may be reduced to a sample, which the manifest states. (6) A deletion is proven only under an existing immediate parent: a deleted directory's files are `containment_unproven` (honest and conservative, not a minimal-diff or parent-chain feature). (7) The deterministic doubles prove the assembled local workflow, not provider reliability; no real provider was invoked. (8) Spawn cost: one `ls-tree` batch per 32 paths, one `cat-file` per distinct baseline blob and one `hash-object` per current file per observation, bounded by the 128 paths and the 10 s per-call timeout.
- Next action: planner/reviewer inspects the uncommitted diff and the evidence above and gives GO or an actionable NO-GO. The executor has not committed, pushed or selected another slice and will keep any correction in this chat and uncommitted.

## Claude-reported model context limits in historical attempt evidence (2026-10-04)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `122d4ebaa81f4d5899cf5e69e916de33bc67318c` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing; empty index; only `planner-handoff.md` modified, no untracked file). The original Claude submission preserved the selection record at SHA-256 `6d68f53e6bf4f3e234daec8342b6e4f999878c55563f142ecf3281eecaddadd5`; that is a historical submission hash. After the planner NO-GO and Claude credit exhaustion, the owner explicitly assigned Codex execution of the current correction and next-slice planning. Codex completed the bounded correction and final review in this chat; this is not independent review of its own correction. The complete substantive diff was prepared for publication on that parent; the publication and bounded correction are recorded below. No next slice is implemented and no Increment 4 completion is claimed.
- What it delivers: for a concluded Claude attempt, the existing historical attempt evidence shows the model identifiers Claude listed in its own result and each entry's reported context-window and maximum-output token limits, stored durably so it survives a restart. It is provider-reported historical observation, not remaining context, a live capability, eligibility or proof that a listed model was used ([ADR-0023](../decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md), additive).
  - Observation: the three real Claude adapters (critical review, implementation, review correction, so every repair, guided, authorized and diagnosis-origin variant) read the optional `modelUsage` map through one shared `ClaudeCliModelContextLimits`, at exactly the boundary the token-usage reader uses (structurally valid, clean-exit, untruncated envelope; a valid `is_error` envelope still carries it). Only each key, `contextWindow` and `maxOutputTokens` are kept; admission is all-or-unknown (one `modelUsage` object, 1 to 16 unique ordinal ASCII identifiers matching `[A-Za-z0-9][A-Za-z0-9._-]*` up to 128 characters, one occurrence of each required member, positive Int32 integers, output not above the window). Invocation arguments, observed model and effort, token usage and its accounting are unchanged, and a missing limits map keeps valid usage while missing usage keeps valid limits.
  - Carriage and storage: a provider-neutral immutable `AgentModelContextLimits` value rides the three invocation results, supervisors and recording commands; the existing completion transaction records it once (also for unsuccessful semantic outcomes) as one nullable `TEXT` column `AgentModelContextLimitsSnapshot` on `Attempt` (migration `20261004160723_AddAgentModelContextLimits`): the canonical project-owned `{"version":1,"source":"claude-cli-model-usage-v1","models":[…]}` snapshot, entries ordinal, at most 4 KiB. The Domain (`AgentModelContextLimitsEvidence`, its policy and violation enum) validates and serializes the project shape and rejects another provider, an undispatched attempt, a pre-invocation outcome and a second write; only Infrastructure parses provider field names. Stored text is read only when it re-serializes to exactly itself as valid evidence for the attempt's provider; anything else, and a Running, undispatched, non-Agent or identity-incoherent attempt, projects absent without throwing. Old rows stay `NULL`; nothing backfills or reparses an artifact.
  - API and UI: only `GET /runs/{runId}/agent-attempts/{attemptId}/evidence` gains the additive nullable `modelContextLimits` (API-owned `AgentModelContextLimitsResponse` and `AgentModelContextLimitResponse`; the source tag and version are never exposed); the generated client was regenerated, never edited. The selected historical attempt detail shows, for a Claude attempt, a "Claude-reported model limits" section (`ModelContextLimitsFact`): each identifier as plain text with its context-window and maximum-output tokens (thousands separators, so `200,000`), a line saying the entries are models listed by Claude and not independently proven used, and the fixed statement that remaining context and next-invocation capacity were not measured; "Not recorded" (never zero) when absent. A Codex attempt shows no section. No percentage, meter, estimate, global panel, polling, probe or action.
  - Documentation: ADR-0023, its index entry, the engineering context, the protocol (a new "Claude-reported model context limits" section, the evidence bullet, the history/evidence route and the token-contract sentence that used to say `modelUsage` was ignored), the cockpit specification's attempt-evidence list and the roadmap's context-visibility line.
- Substantive review inventory (80 paths: 48 tracked files modified, 32 untracked; nothing staged, committed or pushed at final review; counts include this record and the planner record). Production, 35 (23 modified, 12 new): Domain 5 (`Attempt.cs` modified; new `AgentModelContextLimit`, `AgentModelContextLimitsEvidence`, `AgentModelContextLimitsEvidencePolicy`, `AgentModelContextLimitsEvidenceViolation`); Application 14 (the three invocation results, the three recording commands and handlers, the evidence query result and handler modified; new `AgentModelContextLimits`, `AgentModelContextLimitEntry`, `AgentModelContextLimitsRecording`); Api 7 (the three supervisors, the evidence response and endpoint modified; new `AgentModelContextLimitsResponse` and `AgentModelContextLimitResponse`); Infrastructure 6 (the three adapters, `ClaudeCliTokenUsage.cs` (comment only) and `AttemptConfiguration.cs` modified, new `ClaudeCliModelContextLimits`) plus the migration `20261004160723_AddAgentModelContextLimits` with its designer (new) and the model snapshot (modified), counted among the 35. Frontend 11 (8 modified, 3 new): `AgentAttemptHistoryPanel.tsx`, `clients.ts`, `cockpit.css`, the regenerated `api-client.ts`, the journey, the harness test, `journeyDb.ts` and the existing `VerificationRunRefresh.test.tsx` synchronization fix; new `ModelContextLimitsFact.tsx`, its Vitest class and the journey expectations module. Documentation 8 (7 modified, 1 new): this record, the preserved planner record, the ADR index, the engineering context, the protocol, the cockpit specification and the roadmap plan; new ADR-0023. Tests 26 (10 modified, 16 new): Domain 1; Application 10 (seven new, including `AgentModelContextLimitsBoundaryTests`; the three recording-handler test classes became `partial` to host the new cases, assertions unchanged); Infrastructure 7 (`ClaudeModelContextLimitsAdapterTests`, `AgentModelContextLimitsMigrationTests` and `HistoricalAgentAttemptRow` new; the four older migration test classes modified); Api 8 (three hosted classes, `AgentModelContextLimitsEndpointTests` and `ClaudeEnvelopeProcessDouble` new; the hosted implementation and review-correction classes and the native double `ClaudeRole.cs` modified). The existing critical-review hosted class was already `partial`.
- Tests written first, failing for the stated reason against unchanged adapter behavior (with the types and optional parameters added so they compile, never counting a compile failure): Infrastructure `ClaudeModelContextLimitsAdapterTests` failed 8 of 74 (`Expected: AgentModelContextLimits, Actual: null` for every positive case; the 66 negative cases pass vacuously on the parent and are the ones the mutations below prove), and the three real-adapter hosted classes failed 12 of 21 (`Expected` the canonical snapshot, `Actual: null`; the 9 "unusable map keeps the outcome and usage" controls passed). All turned green only by the production change.
- Coverage: all three real adapters over process doubles with invocation arguments and standard input compared unchanged against a map-less run; single and multiple entries, 128-character and 16-entry bounds, Int32 limits; duplicate keys (literal and escaped) and duplicate numeric members in either order; zero, negative, fractional, exponent, string, null, boolean, overflow and contradictory limits; unsupported identifiers (empty, leading punctuation, non-ASCII, space, bracket suffix, over-length); malformed entries beside valid ones (no subset); duplicate root maps; truncation, non-clean exits, timeouts, cancellation, no process result, invalid envelopes; valid `is_error`; invalid structured business output; independence of usage and limits in both directions; `ObservedModel` stays null. Domain (96 tests): shape, bounds, ordinal order, canonical text, strict `FromPersisted`, policy over every outcome, the three transitions, write-once, no mutation on rejection. Application: the recording policy and the three handlers (success, unsuccessful outcome, absent, rejection with no mutation) and the evidence query (recorded, absent, malformed, Running, unprovable identity). Infrastructure (real file-backed SQLite and the production migrations): nullable upgrade of a populated prior schema with every fact intact and the column `TEXT`/nullable/no default, exact round trip in ordinal order surviving a pool clear, immutable recording, rollback, 36 untrusted stored texts each beside a healthy sibling, and Running, undispatched, wrong-provider and Process-kind rows. Api (real authenticated MVC): the success shape and absence member, 401 for no and wrong credentials, a foreign-run and unknown attempt 404 with no disclosure, malformed and unprovable stored evidence, no other response carrying the member, the additive record shape and the generated client text; three hosted flows with the real supervisors, the real adapters over a process double, the real mediator and recording and a file-backed database (12 recorded or absent cases). Frontend: 16 Vitest cases (recorded, multiple, absent, partial, non-Claude, markup as text, nothing derived, selection and run replacement, unverifiable identity). Browser: the existing native-double collaboration journey now makes the double list models (reverse ordinal order, per-contract values), inspects attempts 2, 4 and 6 through the rendered history, compares the evidence the generated client received on the wire and the rendered rows with independent literals, shows Codex attempt 1 has none, reads the host's own snapshot column read-only for all seven attempts, and repeats the inspection of attempts 4 and 6 after the journey's reload against the same evidence. No raw SQL proves production recording anywhere; SQL appears only to build historical or tampered rows.
- Changes to earlier accepted tests (disclosed): the new nullable `attempts` column broke five older migration tests that stop at an older migration. `AddDirectHumanGuidanceMigrationTests` and `AddClaudeMutationTurnLimitMigrationTests` now also expect the later column on the way down (the convention the latter already used for earlier later columns); `AddPlanningImplementationAuthorizationMigrationTests` (two tests) and `AddDiagnosisCorrectionEscalationMigrationTests` insert their one historical Agent attempt through the new `HistoricalAgentAttemptRow` because Entity Framework would write the new column into an older schema. After R3, that helper writes every mapped scalar fact applicable to the actual old table, using the model's type mappings and parameters, and omits only absent columns. Latest-schema seeding uses ordinary EF. Explicit non-vacuous comparisons now prove every supplied fact survived the upgrade or fresh round trip, strengthening the prior assertions. The existing hosted tests for review correction and implementation gained optional seed parameters (a launch path and a manifest text) and the implementation class became `partial`.
- Mutation checks (each applied alone to the production file(s), the affected Domain, Application, Infrastructure and Api selections run on a fresh build, then restored; the SHA-256 of every restored file was compared and identical, and the restored file's timestamp renewed so no mutated binary survived). A first pass was discarded: restored files kept an old timestamp so MSBuild reused a mutated assembly, which I found because Domain failures were identical under unrelated mutations; the harness now renews the timestamp and a baseline of the clean tree (96/116/115/33 passing) is run first. Results of the valid pass: (M1a) the three supervisors stop passing the limits to the command: Api 12 failures; (M1b) the three handlers stop passing them to the transition: Application 7, Api 12; (M1c) the three adapters drop them from the invocation result: Infrastructure 7, Api 9; (M2) duplicate `contextWindow` or `maxOutputTokens` admitted (last wins): Infrastructure 6, Api 3; (M3) a default 200,000/32,000 substituted for the reported values: Infrastructure 5, Api 12; (M4) a valid subset of a malformed map kept: Infrastructure 23; (M5) stored text trusted without the canonical re-serialization: Domain 7, Infrastructure 8, Api 1; (M8, frontend) zero shown for the absent record and for a missing field: Vitest 6 failures. Restored file hashes: supervisors `4E3113CD…`, `99E94F67…`, `CC3FAE0E…`; handlers `1D0549CA…`, `AC8CB643…`, `AA17D41E…`; adapters `DE47412B…`, `CB3A97FA…`, `18772C8B…`; parser `71B38B46…`; evidence `69D8EEDB…`; component `9E424650…`.
- Original Claude submission validation (historical, superseded in final counts below; the last test edit then was the older-migration tests; everything below ran after it unless stated): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors, and again after deleting the generated client (NSwag regenerated it; SHA-256 `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819` both times, was `13c9d116…c44a`); sequentially `dotnet test --no-build`: Domain 1048/1048 (was 952, +96), Application 3928/3928 (was 3880, +48), Infrastructure 1148 passed + 4 skipped of 1152 (was 1033 + 4; the same four environment-gated file-symbolic-link and reparse-point skips; the first full run failed 5 older migration tests, corrected as described above and the full suite rerun), Api 997/997 (was 964, +33), Architecture 13/13 (Domain, Application, Api and Architecture ran on the identical production binaries before the test-only migration edits and are not affected by them). Frontend: Vitest 1802/1802 in 133 files (was 1786, +16), `npm run typecheck` exit 0, `npm run lint` 9 warnings and 0 errors (the baseline), `npm run build` exit 0 (the existing chunk-size notice only), strict e2e check exit 0 (`npx tsc --ignoreConfig --noEmit --strict --target es2023 --lib es2023,dom --module esnext --moduleResolution bundler --skipLibCheck --allowImportingTsExtensions --verbatimModuleSyntax --types node --noUnusedLocals --noUnusedParameters --erasableSyntaxOnly` over every `e2e` file and both Playwright configurations), `npm run test:harness` 69/69 (was 65), then one canonical `npm run test:e2e:all` with normal authentication, Program composition and all supervisors: 11 real-host specs and 2 journeys passed, no skips. `dotnet format --verify-no-changes` 153 findings in 17 files, identical to the baseline (the first run found 155 because of a documentation comment I added to the response record; I removed it). `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none, run fresh (no dependency, project file or lock file changed). Markdown file and anchor links 316 checked, 0 problems (was 303). Whitespace, trailing-space and NUL checks over every changed or new file found none; the only note is that the NSwag-generated client has CRLF line endings and no final newline, as its generator writes it.
- Review correction R1-R3 and ownership takeover: R1 gives Domain evidence and the provider-neutral port their own immutable snapshots; caller collection edits or public-surface casts cannot change validated, serialized or recorded facts. R2 refuses null collections and entries without throwing or mutating completion, and rejects a counted collection above 16 before reading any entry. An uncounted sequence is read only through its first excess entry (17), never copied without a bound. All three completion handlers exercise malformed-input refusal with unchanged persisted state. R3 restores ordinary EF for latest-schema seeds and faithfully preserves every applicable scalar fact through historical migration seeds. Claude had left the immutable and fixture corrections in progress when its credits ended; Codex inspected and completed them. A newly tightened R2 regression was red on that takeover tree (1 failed, 10 passed; expected zero entry reads, actual 17), then green after the counted collection was refused before traversal; an infinite uncounted sequence proves the 17-entry ceiling. The planner record was unchanged at takeover SHA-256 `22fa8e77d13e6f5b36ed8064abe478e58062e61b7d83b97469bff92acfeec3bd` until the final GO record.
- Preserved correction-run failures: the first full Vitest run had 1 failure and 1801 passes in an existing verification-panel test: its command-list read had not settled when an immediate disabled-button assertion searched for the button. The execution read was already explicitly held. Codex retained the same assertion and awaited the independently asynchronous command render; focused 16/16 and the final full suite passed. The first full Infrastructure run under the filesystem sandbox had 87 failures, 1060 passes and 6 skips because normal Agent scratch creation under `%LOCALAPPDATA%\DevalCopilot\agent-scratch` was denied. The full suite was then run with the normal required filesystem permissions, with no production, authentication, host-composition or provider-contract workaround: 1149 passed and the four existing environment skips remained. This was an environment correction, not an unexplained unchanged retry into green.
- Codex correction validation before the count-interface refinement (historical counts, superseded only for Application below): affected Domain 104/104, Application 46/46, migration selections 57/57 and the repaired frontend test 16/16 first; solution build `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors, NSwag client unchanged at `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819`; sequential full backend suites with `--no-build --no-restore`: Domain 1056/1056, Application 3954/3954, Infrastructure 1149 passed + 4 skipped of 1153 (the existing file-symlink/reparse privilege gates), Api 997/997, Architecture 13/13. Frontend: `npx vitest run` 1802/1802 in 133 files, typecheck and the strict e2e command above exit 0, lint 9 baseline warnings/0 errors, production build clean with its existing chunk-size notice, harness 69/69. After the harness passed, one canonical `npm run test:e2e:all` under normal filesystem permissions, authentication, Program composition and all supervisors passed Chromium 11/11 and both native-double journeys 2/2, without a browser failure, skip or rerun. Formatter remains 153 findings in 17 files, the recorded baseline; no correction source file introduces a finding. Final hygiene covered all 80 changed/new paths, eight changed Markdown files and 244 local file/anchor links with zero issues (the narrower checker scope differs from the original 316-link report). The generated client retains its standard CRLF/no-final-newline form. RETAINED, not rerun in this correction: the original clean npm/NuGet audits (no dependency changed) and original parser/propagation/storage/display mutation set; those runs are evidence on the pre-correction tree, not new mutation runs on the immutable correction.
- Substantive publication and fresh post-publication evidence: commit `53b2eb69a7ea5677b13db4a4735c476003f6454b`, parent `122d4ebaa81f4d5899cf5e69e916de33bc67318c`, contains exactly the approved 80-path snapshot (48 modified, 32 added; external inventory SHA-256 `03c6184f4667296bb9388f101b39666be7a8c17a8cba62deffe134520c89a6ba`). The normal fast-forward push was followed by fetch and independent live-ref verification: HEAD, local origin/main and live refs/heads/main all matched with a clean checkout. Checks on that commit: build 0 warnings/errors; Domain model-limit filter 104/104; Application model-limit and all three recording-handler classes 142/142; Infrastructure model-limit and four affected older migration classes 158/158; Api evidence endpoint 12/12 plus the three actual partial hosted-supervisor classes 99/99; full Architecture 13/13. Frontend: full Vitest 1802/1802, typecheck, lint (9 baseline warnings), production build, harness 69/69, then one canonical test:e2e:all, Chromium 11/11 and journeys 2/2. No selected check failed or was skipped; the client was unchanged and only the two older owned roots remained. These filtered backend runs do not repeat the full suites above.
- Post-publication count-admission refinement (reviewed and published below): final inspection found that an excessive collection with Count but no index (IReadOnlyCollection or ICollection) still traversed 17 entries before refusal. The copy was already bounded and malformed evidence still refused, but this did not meet R2's zero-entry-read rule for counted inputs. Four regressions were added first: both excessive forms were red (expected 0 reads, actual 17), and the two valid-size controls passed; the complete boundary class was 2 failed/13 passed. The port now checks both count interfaces before allocation or traversal, while retaining bounded streaming for genuinely uncounted sequences. Fresh corrective validation: affected Application model-limit selection 50/50; solution build 0 warnings/errors, full Application 3958/3958, Infrastructure adapters/new and affected older migrations 158/158, Api endpoint and actual hosted-supervisor classes 111/111, Architecture 13/13. All selected tests passed with no skips. Formatter remains 153 findings at baseline and neither corrective source file has a finding. The generated client is unchanged. Domain, full Infrastructure/Api and frontend validation remain retained from the prior runs, because the correction changes only the counted-input admission and four Application regression cases. No parser, persistence, provider, API or frontend change is involved; the original substantive commit is neither amended nor rewritten. The separately reviewed corrective inventory is exactly four modified paths: the port, its boundary tests and the two roadmap records, with no staged or untracked files before its GO. That was the pre-publication review state; the corrective publication and final closure evidence follow.
- Remaining limits and risks: the contract is evidenced from the official documentation and the installed 2.1.276 public schema, not from a provider invocation, so a real provider may use identifiers outside the closed ASCII shape (a bracket suffix such as a 1M-context marker is the plausible case); the whole map is then absent ("Not recorded"), by design, until a new evidenced contract version admits it. A listed model is not proven used, and values are as accurate as the provider's report. Recording a malformed limits value that reached the command would reject the whole completion exactly as invalid token usage already does; the adapters cannot produce one, and the Domain keeps it from being written. An `Interrupted` attempt that somehow holds a canonical snapshot would project it, the same coherence rule token usage has. The section is shown only for a Claude attempt. The tracked-diff and raw Git hash hard-link disclosure risk recorded earlier remains open and untouched, and nothing in this change reads or hashes files.
- Final publication closure: corrective commit `432b70ac75ec4f7a9caf8deb2d773c2a22affe8f` has parent `53b2eb69a7ea5677b13db4a4735c476003f6454b` and contains exactly the four reviewed existing paths (external corrective inventory SHA-256 `f699ec4abd33eeead27a7c9c585786670f71193f2350572483aa8ad2dfcab724`). Its normal fast-forward push was followed by fetch/live verification: HEAD, local origin/main and live refs/heads/main all matched it with a clean checkout. Fresh post-publication checks on the corrective commit: solution build 0 warnings/errors; affected Application model-limit selection 50/50 with no skip; harness 69/69, then one canonical test:e2e:all, Chromium 11/11 and native-double journeys 2/2, normal authentication/Program composition/all supervisors, no failure, skip or rerun. The client stayed `d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819`; git diff --check and the checkout were clean. Each run removed its own root; the two older roots remain untouched. Retained, not repeated on this commit: the preceding full Application 3958, filtered Infrastructure 158/Api 111/Architecture 13 and formatter baseline on the identical corrective code, earlier full Domain/Infrastructure/Api and full frontend suites, audits, mutation evidence and hygiene/link checks. Failures and their corrections above remain preserved. Neither substantive commit was amended, force-pushed or reconciled. This current-work-only factual closure changes no implementation, embeds no own SHA, preserves all remaining limits and claims neither Increment 4 completion nor next-slice implementation.

## Untracked-file previews are physically proven (2026-10-04)

- Scope and parent: the substantive change for the selected Increment 4 security slice, prepared on parent `5c9dace0f976025ad71dfad11bb73c9468343236` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing; empty index; only `planner-handoff.md` modified, no untracked file). The planner record is preserved byte-for-byte (SHA-256 `cec73f36a10240a05f29c4eb45204698cb656835e20080a4fb3b3b9c3bccfd72` at dispatch and at executor return, before Codex's review edit; a historical state of the record, because the record that was published is Codex's GO record, raw SHA-256 `0d1bb15c27411b126f342979f3d4fc3ee821c7f226b70af0d8366cf00eb4efc9`, see "Publication and post-publication status"); the generated client SHA-256 `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a` is unchanged, including after deleting the file and rebuilding. It was first presented as an uncommitted, unstaged and unpushed diff for Codex's GO/NO-GO (the wording of this and the following bullets describes that pre-publication tree, whose `HEAD` was the parent and whose index was empty), and was published only after Codex's explicit GO (see "Publication and post-publication status"). No next slice is selected and Increment 4 is not claimed complete.
- Inventory (16 paths: 11 tracked modified, 5 untracked). Production, 2 modified: `UntrackedFilePreviewReader` and `GitWorkspaceEvidenceReader` (an internal constructor seam only). Documentation, 7 (6 modified, 1 new): this record, the preserved planner record (modified as the planner left it), the ADR index, the engineering context, the protocol, the roadmap, and new ADR-0022. Test code, 7 (3 modified, 4 new): `UntrackedFilePreviewReaderTests`, `GitWorkspaceUntrackedPreviewTests`, `RealAdapterInstructionProbe` (modified); `HardLinkSupport` (Infrastructure tests), `UntrackedPreviewScene`, `UntrackedPreviewFormsDeliveryTests` and `UntrackedPreviewClaimDeliveryTests` (Api tests, new). No frontend, HTTP, DTO, generated-client, schema, migration, dependency, provider, permission, authentication, budget, grant, scheduler or lifecycle file changed.
- What it delivers: [ADR-0022](../decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md) (additive; ADR-0021's root instruction contract is unchanged). The Projects-owned `UntrackedFilePreviewReader`, which both preview entry points use (`CaptureWithUntrackedPreviewsAsync` and `CaptureForAgentContextAsync` with previews requested, the second being how the seven preview-requesting claim handlers reach it), admits bytes only from a regular, non-reparse, non-device file with exactly one link, proven on the held handle through the existing `WindowsHandleFileFacts` before any byte or length is read, together with the existing exact ordinal final-handle path proof, and asked again on the same handle after the bounded read, before the preview is accepted. Unavailable facts, a link count other than one and a reparse point are `containment_unproven` with no text and no size; a directory or device keeps `not_regular_file`; a file with several links is refused even when every known name is inside the worktree, and aliases are never enumerated. Path validation, the redirected-root rule, the unsupported-host omission, identity comparison, encoding, ordering, the 64 KiB verification bound, the 4 KiB per-file, 16 KiB aggregate and 32 KiB manifest bounds are unchanged, and an omission spends no text budget and hides no healthy sibling. Newly sealed manifests carry the truthful omission; sealed manifests replay their exact bytes.
  - The only structural addition is a test seam: `ReadAll` and an internal `GitWorkspaceEvidenceReader` constructor take an optional function for the operating system's answer about an open handle (production passes none and gets `WindowsHandleFileFacts.TryGet`). It cannot read, open or admit anything by itself; the tests use it to withhold the answer and to create a real second name between the two questions.
  - Documentation: ADR-0022, its index entry, the engineering context, the protocol's preview passage and the roadmap. The stale preview-passage claims were corrected narrowly: the caller (seven of the eight claim handlers request previews through `CaptureForAgentContextAsync`; planning requests none; `CaptureWithUntrackedPreviewsAsync` is the same capture without the instruction context), the number of manifest families that carry previews (the verification-diagnosis manifest does too), and the claim that the text exists only in the manifest and provider input (the existing authenticated sealed-artifact viewer also shows it, unredacted). The historical ledger and ADR-0021 are not rewritten.
- Tests written first (all against the unchanged admission logic, with only a signature-only seam added so they compile), and failing for the stated reason: 15 of 54 selected Infrastructure tests failed (`UntrackedFilePreviewReaderTests` and `GitWorkspaceUntrackedPreviewTests`), among them the root and nested outside hard link at the reader (`Expected: ContainmentUnproven, Actual: null`: the outside sentinel was admitted as a preview, with its independent expectation that the linked path really reads as the outside bytes and that Git computes the outside file's identity for it), an inside second link, the lost-other-name control, both entry points for each of those, facts unavailable, a second name appearing during the bounded read, and admission facts asked twice. After the change the same selection passed (81 passed + 2 skipped, the skips being the two file-symbolic-link tests that need elevation), and the later reader-level cases (facts classification, recheck variants, call count) pass in the full suites below.
- Real boundaries: every hard link is a real NTFS link made with `mklink /H` on this host, with no mock or environment skip; expected bytes and identities are independent literals. Infrastructure: real Git and filesystem at both entry points for a root and a nested outside link with a safe sibling and a single-name control, an inside second link under both names, unavailable facts, a real second name created between the two facts questions, junction and redirected-root controls, and the unsupported-host omission with a hard link. Api (the narrowest shared boundaries, no fake reader): the real capture over a real worktree with hard links feeds every preview-carrying builder form (19: critical review, resolution, accepted/revised/reviewed/authorized implementation, each with or without direct guidance where it exists, code review and correction, review correction in its four forms, verification diagnosis, each with its format-repair flavor where one exists), the real artifact store and the real adapter of that form's role (Claude critical review, implementation and review correction; Codex resolution, implementation review and diagnosis): the outside sentinel is absent from the entire returned evidence, the entire sealed manifest and the adapter's actual standard input, the safe sibling and control appear exactly once, and each linked path is named `containment_unproven` with neither text nor size. Real claims through the production mediator, EF pipeline and artifact store with the real reader: planning (requests no previews: none delivered, the sentinel and the sibling absent, the root `AGENTS.md` once in its own section), a Claude critical-review claim, a Codex resolution claim and a Claude implementation claim, each delivered through its real adapter; the reserved root name keeps `reserved_instruction_file` accounting; a manifest sealed before a second name appeared outside the worktree (same bytes, identity and fingerprint) replays its exact bytes while a later fresh claim carries the omission and still delivers the siblings.
- Mutation checks (each applied to `UntrackedFilePreviewReader`, run against the Infrastructure reader and entry-point classes and the Api delivery classes, then restored byte-identically; the restored file's SHA-256 `7b1f187e402c8fa1b344c0a669449edcef505bd868c5e45a0ce92838878a1d0e` was compared after every mutation): (M1) bypassed initial facts proof: 10 Infrastructure failures (facts asked twice, change during read, both entry points), the Api classes still pass because the final recheck refuses a pre-existing link; (M2) bypassed final recheck: the same 10 Infrastructure failures, the Api classes still pass because the initial proof refuses a pre-existing link; (M3) bypassed link-count proof: 15 Infrastructure and 22 of 24 Api failures; (M4) every handle admitted, equivalent to the parent: 31 Infrastructure and 22 of 24 Api failures. No test-only production bypass exists.
- Executor FRESH validation on the final implementation (delivery documentation finalized afterwards): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, and again after deleting the generated client (regenerated, hash unchanged); affected classes first, then sequentially `dotnet test`: Infrastructure 1033 passed + 4 skipped of 1037 (was 998 + 4; the skips are the environment-gated file-symbolic-link and reparse-point tests, which cannot create a link here without elevation and are separate from the hard-link evidence), Application 3880/3880 (unchanged), Api 964/964 (was 940; +24), Architecture 13/13; from `src/frontend/DevalCopilot.Frontend`: `npm run typecheck` exit 0, `npm run build` exit 0 (the usual chunk-size notice), a standalone strict `tsc` over every e2e source and both Playwright configs exit 0, `npm run test:harness` 65/65, then, after it, one canonical `npm run test:e2e:all` with normal authentication, Program composition and all supervisors: the Chromium suite 11/11 and the native-double journeys 2/2, no failure and nothing rerun (the vite client logged SignalR "connection was stopped during negotiation" console lines during navigation, as page transitions interrupt the connection; no assertion was affected); `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the recorded baseline (the four in `UntrackedFilePreviewReader` are the pre-existing ones on lines this change did not write; the other changed files have none); Markdown links 303 checked with 0 problems; no NUL, lone CR, trailing whitespace, missing final newline or mixed line ending in any changed or new file except the planner record, which is preserved; `HEAD` `5c9dace0…` and `origin/main` unchanged, nothing staged; the two older owned roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched and the run left none of its own. RETAINED, not rerun because no Domain, frontend, dependency or production-React file changed: Domain 952/952, Vitest 1786/1786, lint (9 baseline warnings), `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none. No check failed, and none was rerun to obtain a pass.
- Remaining limits and risks: this closes generic untracked-preview delivery, not every filesystem read. The raw Git observation behind the checkpoint fingerprint (`status` and the per-path `hash-object` of untracked files) and the tracked diff, hunk and changed-line sample selection read named paths and can still read the content of a file that another name links to outside the worktree (a tracked file hard-linked outside can have its changed content appear as tracked diff); hardening them is a separate, unselected decision, because Git reopens the path after any check the host could make. Files and link topology can change after observation. A legitimately multiply linked file inside the worktree is omitted from the preview (conservative by design; the path and fixed reason are still delivered). Content admitted normally is unredacted and visible through the existing authenticated sealed-artifact viewer. Windows is the only host with the proof (elsewhere every preview is omitted). The two file-symbolic-link tests are skipped on this host, separately from the hard-link evidence. Not a real-provider test; a provider's obedience to the boundary is not observed.
- Publication and post-publication status (2026-10-04): Codex granted publication GO for the exact reviewed 16-path snapshot (11 modified tracked, 5 untracked; inventory manifest SHA-256 `71105170a67fe76e9e8505a144a1331de6b59a39830ad901ed06f8db99e504a8`, including Codex's GO edit of the planner record, `planner-handoff.md` raw SHA-256 `0d1bb15c27411b126f342979f3d4fc3ee821c7f226b70af0d8366cf00eb4efc9`, and Codex's two wording edits to this entry, `current-work.md` raw SHA-256 `9c3da1e373077f501f08d57222cd09145a99eaf1708b407e1d6b3a1ecf21bcc5`; the generated client `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a` is unchanged and outside the inventory). Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `5c9dace0f976025ad71dfad11bb73c9468343236`; empty index; exactly 11 modified and 5 untracked paths; the manifest's own hash, the exact 16-path inventory (nothing missing, extra or duplicated) and every approved file's raw SHA-256 and size matched. Exactly those 16 paths were staged (5 added, 11 modified), the staged inventory equalled the manifest and `git diff --cached --check` passed; they were committed on that parent as the substantive commit `ba5797a0be898a7a50c7a400056ed891ce3c47d5` (16 files changed) and pushed with a normal fast-forward push (`5c9dace..ba5797a`; no force, no amend, no history reconciliation). After a fetch, `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to that SHA with a clean checkout. The planner record was committed exactly as Codex left it and is not edited by this closure.
  - Post-publication checks, run once on that commit with normal authentication, the normal host composition and all supervisors, .NET commands sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; with `--no-build --no-restore` and the filters Codex specified: Infrastructure (`UntrackedFilePreviewReaderTests`, `GitWorkspaceUntrackedPreviewTests`, `GitWorkspaceInstructionContextTests`) 96 passed and 2 skipped of 98, the skips being the environment-gated file-symbolic-link tests (`A_file_symbolic_link_to_an_outside_file_is_refused_without_returning_its_text` and `A_symbolic_link_source_is_never_followed_to_content_outside_the_worktree`), which cannot create a link here without elevation and are separate from the hard-link evidence; Application (`UntrackedFileManifestTests`, `ProjectInstructionContext`, `AgentEvidenceProjectionTests`, `CreateCodexPlanningAttemptInstructionContextTests`) 185/185, no skip; Api (`UntrackedPreview`, `InstructionDeliveryProjectionTests`, `ProjectInstructionContextReplayHostedTests`, `HumanAuthorizedPlanHostedTests`, `VerificationDiagnosisHostedTests`, `ProviderFixtureInstructionEvidenceTests`) 84/84, no skip; Architecture 13/13. From `src/frontend/DevalCopilot.Frontend`: `npm run test:harness` 65/65 (0 skipped), then, only after it passed, `npm run test:e2e:all`: the Chromium suite 11/11 and the native-double journeys 2/2, with no failure and nothing rerun (the vite client again logged SignalR "connection was stopped during negotiation" console lines during navigation; no assertion was affected). The generated client hash was unchanged, `git diff --check` was clean and the checkout was clean after the checks; the run left no owned root of its own and `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched. No check failed or was rerun.
  - RETAINED, not rerun on the published commit (recorded above from the reviewed tree, which the commit contains): the full Infrastructure 1033 passed + 4 skipped of 1037, Application 3880/3880 and Api 964/964 suites, the four mutation sets (M1 to M4), the Domain 952/952 suite, Vitest 1786/1786, typecheck, build, strict e2e `tsc`, lint (9 baseline warnings), `npm audit` 0 vulnerabilities, `dotnet list package --vulnerable --include-transitive` none, `dotnet format --verify-no-changes` 153 findings in 17 files (the baseline), the Markdown link check (303) and the whitespace/NUL checks. The filtered runs above are new evidence for the published commit and do not replace those full-suite results. The earlier failure evidence (the 15 failing regressions on the unchanged admission logic) is preserved unchanged in the bullets above.
  - Remaining limitations are those in the limits bullet above and are unchanged: this closes generic untracked-preview delivery, not every filesystem read (raw Git hashing and the tracked diff, hunk and sample selection can still read an outside hard link); files and link topology can change after observation; a legitimately multiply linked file is omitted from the preview; admitted content is unredacted and visible through the authenticated sealed-artifact viewer; Windows is the only host with the proof; the two file-symbolic-link tests are skipped on this host; this is not a real-provider test. This closure does not claim that Increment 4 is complete and selects no next slice.

## Project instruction context reaches every Agent stage (2026-10-04)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `3b8450c48e247846944204ba8f2eab1b04bc635b` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing and again before each correction below; empty index). Codex's first review of this tree was NO-GO with two blocking findings, R1 and R2, corrected in this same slice (see "Review correction"); R1 was then accepted and a second review left one remaining R2 finding, corrected in this same slice too (see "Second review correction"). The planner record is preserved byte-for-byte: SHA-256 `01077c5eca8e1e8372dd25ed90e713cf19639b8f3ef1e0dc1fd25f813872b4ce` at dispatch, `a33f5cef7ac88b0ebdef1a8757c4985c74e3f2f09f44bf16663a09c5e4cf65b0` after Codex's first review edit and `70e3b82d1bd8f802255b8842c670c07bfd2ef5baae0145866cfba98efe3d5bf9` after Codex's second review edit (all three are historical states of the record during review; the record that was published is the GO record `25e4ea9e388966dcfd845bbd015f91c73b260fc0d1caaa9053aa787941f65349`, see "Publication and post-publication status"); the generated client SHA-256 `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a` is unchanged, including after deleting the file and rebuilding. It was first presented as an uncommitted, unstaged and unpushed diff for Codex's GO/NO-GO (the review rounds below describe that pre-publication tree, whose `HEAD` was the parent and whose index was empty), and was published only after Codex's explicit GO (see "Publication and post-publication status"). No next slice is selected and Increment 4 is not claimed complete.
- Inventory (84 paths: 60 tracked files modified, 24 untracked). Production, 31 (22 modified, 9 new): the evidence-reader port; the seven manifest builders and the eight claim handlers (the planning, critical-review, resolution, implementation, code-review, review-correction, diagnosis-correction and verification-diagnosis handlers; the review-correction builder serves two of them); `ChangeEvidenceManifest` (now also the reserved-path withholding and its accounting), `UntrackedFileManifestSection`, `TrackedDiffParser` (`TryParsePath` made `internal`), `GitWorkspaceUntrackedOmission` (one value); `GitWorkspaceEvidenceReader` (now `partial`; the Agent-context capture projects and no longer previews the reserved names; the process helper accepts standard input); `UntrackedFilePreviewReader` (one helper made `internal`); new: the four port types `GitWorkspaceInstructionContext` (with the reserved-name predicate), `GitWorkspaceInstructionFile`, `GitWorkspaceInstructionStatus` and `GitWorkspaceInstructionOmission`, `AgentEvidenceProjection`, `ProjectInstructionContextManifest`, `AgentInstructionFileReader` (rewritten as acquire-then-verify on one held handle), `WindowsHandleFileFacts` and `GitWorkspaceEvidenceReader.AgentInstructions.cs`. Documentation, 8 (7 modified, 1 new): this record, the ADR index, the engineering context, the protocol, the roadmap and the workflow model, new ADR-0021, and `planner-handoff.md`, which is modified only as the planner left it. Journey support, 6 (5 modified, 1 new): both journeys, `journeyEnv.ts`, `journeyDb.ts`, the harness test, and new `instructionDelivery.ts`. Test code, 39 (26 modified, 13 new): the new files are `ProjectInstructionContextRegressionTests`, `ProjectInstructionContextManifestTests`, `ProjectInstructionContextFormsTests`, `AgentEvidenceProjectionTests`, `InstructionContextTestSupport` and `CreateCodexPlanningAttemptInstructionContextTests` (Application), `GitWorkspaceInstructionContextTests` (Infrastructure), `InstructionContextCaptureBoundaryTests` (Architecture), and in the Api project `InstructionDeliveryProjectionTests`, `ProjectInstructionContextReplayHostedTests`, `RealAdapterInstructionProbe`, `ProviderFixtureInstructionEvidenceTests` and the double's `InstructionEvidence`; the modified ones update manifest-shape expectations, the shared doubles and the two hosted chains, the fixture roles and log allowlist. The generated client is not among them. The second review correction changed only paths already in this inventory (`AgentEvidenceProjection`, `ChangeEvidenceManifest`, `AgentEvidenceProjectionTests`, `InstructionDeliveryProjectionTests`, ADR-0021, the protocol and this record), so the counts above are unchanged (60 modified, 24 untracked).
- What it delivers: every newly claimed Agent stage receives the exact root `AGENTS.md` and `CLAUDE.md` of its own project's owned worktree as one versioned, identity-verified `projectInstructionContext` section after a fixed host boundary ([ADR-0021](../decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md), additive; no existing authority decision changes). The fixed DevalCopilot `instructionReferences` are removed from new manifests (one consistent choice: omitted, not derived).
  - Capture (`IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync`, called only by the eight claim handlers; ordinary captures and the checkpoint fingerprint are unchanged and return no context): fixed hardened Git questions with literal pathspecs (`ls-files -v` and an ignored-others listing), then, on Windows, the file is opened and proven first: a regular, single-name, non-reparse file (`GetFileInformationByHandle` attributes and link count, a small interop helper) whose open handle's final path is exactly the resolved root plus the fixed name, within the 8 KiB bound. Only the bounded bytes read through that proven handle reach the identity operation (R1): Git hashes them on standard input (`hash-object --no-filters --stdin`; no repository pathname is ever given to Git for this identity), the same held handle is read again, and Git's identity, the host's own blob computation and, for an untracked file, the checkpoint fingerprint's identity must agree. Presence, classification, identity and text are observed inside the capture bracket and again after it; any difference or identity mismatch discards all text as `RepositoryChangedDuringCapture`. Another host omits both files as `containment_unproven`. The raw observation behind the fingerprint (its status, full diff and the per-path `hash-object` of untracked files) is the unchanged, pre-existing one and is not claimed to have this containment guarantee.
  - Delivery projection (R2): for NEW Agent delivery the two root names are reserved to the section. Their generic untracked preview and tracked diff, hunk and changed-line sample content are never delivered, whatever the section says (Complete, any Omitted reason including the budget reasons, Absent) and for a safe, unsafe, clean, dirty or untracked source. The capture does not preview a reserved untracked path and returns it as omitted (`reserved_instruction_file`); its returned diff has the reserved files' blocks removed structurally (cut at `diff --git` file boundaries by each block's own decoded header path; only the default `a/`/`b/` header with one identical path is decoded, and a block whose header is anything else (a repository with `diff.noprefix` or `diff.mnemonicprefix`, a rename, a malformed quote) is unknown, never "not reserved": when a reserved path changed, the whole generic diff, unrelated hunks included, is withheld, with the fixed reason `reserved_instruction_diff_withheld` and a fixed notice in `diffSelection` and every changed path still listed; no other prefix is guessed and the ordinary observation and fingerprint are not normalized; never a substring redaction of repository text); the builders apply the same projection to any reader's capture. Changed paths stay, `diffSelection.reservedInstructionFiles` names a changed reserved path at every reduction step, the diff is then never `diffTruncated: false` or `complete`, and unrelated evidence is delivered as before. A capture with no reserved change produces the same manifest as before. Names match ignoring case, only the root path is reserved.
  - Section: both files always accounted for in fixed order as `Complete` (exact text, verified length and SHA-256), `Absent`, or `Omitted` with a fixed reason (`ignored`, `index_flag`, `unmerged`, `not_regular_file`, `containment_unproven`, `unreadable`, `content_identity_mismatch`, `too_large`, `binary`, `invalid_utf8`, `section_budget`, `manifest_budget`, `not_captured`); `not_captured` is never `Absent`. The Application re-derives what makes a file `Complete`. At most 8 KiB per source and 12 KiB for the section (whole entries, fixed order); in the 32 KiB manifest fit, whole texts are omitted latest first only after every existing optional reduction (change evidence and diagnosis excerpts) is exhausted, never a semantic input; the existing `context_manifest_too_large` refusal, orphan cleanup and non-consumption are unchanged.
  - Coverage: planning, critical review, resolution, implementation (accepted original, resolved revision with and without second review, human-authorized, each guided or not), code review (initial and correction), ordinary, guided and diagnosis-origin correction, verification diagnosis, and every format-repair form.
  - Replay: nothing recaptures or rebuilds a sealed manifest (an Architecture test pins that only the eight handlers name the capture or the section); a manifest sealed before this change replays its own bytes, references included, and a later fresh claim captures fresh bytes.
- Verified against official contracts: `git hash-object --stdin --no-filters` ([git-scm](https://git-scm.com/docs/git-hash-object): reads the content from standard input, no filter applied without `--path`; the documentation states no stdin size limit, and the host sends at most 8 KiB), `git ls-files` `-v`/`--others --ignored --exclude-standard` ([git-scm](https://git-scm.com/docs/git-ls-files); also exercised on the installed Git 2.53 for assume-unchanged `h` and skip-worktree `S`) and `BY_HANDLE_FILE_INFORMATION` ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information); member order and `nNumberOfLinks`, and a real hard link is rejected in a test).
- Tests (written first: four focused regressions, run against the unchanged production code and failing for the stated reasons, then evolved into the passing form): Application `ProjectInstructionContextRegressionTests` (two real worktrees, foreign reference names), `…ManifestTests` (23: accounting, over-claim re-validation, 12 KiB inclusive bound, reverse omission, injection stays one string), `…FormsTests` (64: all 21 builder forms, ordered inputs unchanged, repository text cannot change members, fitting sweep with a necessity check), planning handler tests (ordinary, repair, not captured, which capture), the authorized-claim theory (whole texts dropped before authority evidence is ever shortened); the shared doubles now hand instruction context only to the Agent-context capture, so a handler that did not ask for it fails. Infrastructure `GitWorkspaceInstructionContextTests` (22, real Git and filesystem: two projects, tracked clean/modified/untracked, absent, deleted, empty, ignored and index flags, unmerged, 8192/8193 bytes and multibyte bound, BOM and CRLF, binary and invalid UTF-8, directory and junction, hard link, case-distinct spelling, redirected root, a change before the read, a one-time change retried, an untracked change, a change caught only by the second observation, ordinary captures unchanged, unsupported host, fixed hardened arguments); one test (a file symbolic link) is skipped on this host because it cannot create one without elevation. Api: every claim of the full authorized-plan chain and of the diagnosis chain is fed from its own sealed manifest through the REAL adapter of its role over a recording process double (all seven roles, both providers, the code-review format repair, ordinary and diagnosis-origin correction; stdin equals the sealed bytes and carries the project's conventions once), a replay class (two unrelated projects; a claim sealed before the files changed replays its exact bytes after a restart and a later fresh repair claim captures the new bytes; a historical manifest with the old references replays unchanged), and fixture-contract tests for what the doubles log. Architecture: 4. Journeys: both native-double journeys track root instructions in their owned repositories (distinct per repository) and independently assert, for every stage, what each double observed (identity facts only; the log never holds text) and the sealed manifests read back read-only, against the files the journey itself committed; the pure judge has seven harness regressions. No SQL seeding, intermediate reload, new scenario engine, production React or claim-count change.
- Review correction (NO-GO R1 and R2). Regressions were written first against the unfixed code and failed for the stated reasons: R1, four Infrastructure tests (the identity call named a repository path and sent no bounded stdin, a tracked hard link to an outside file was hashed by Git before the proof, a substituted file and a lying Git were not refused), and R2, sixteen of twenty Api tests (real Git, real Windows hard links, the real capture reader, the real critical-review builder, a sealed artifact and the REAL critical-review adapter; both names; clean tracked, dirty tracked and untracked unsafe roots; a too-large, a binary, an ignored and a budget-omitted safe root; a mixed-evidence control; an ordinary-capture control; each asserting the whole returned capture, the whole sealed manifest, every manifest member outside the section and the adapter's actual stdin). The corrected tree passes them. Added or kept: instrumentation of the identity operation (every `hash-object` is `--stdin`, names no file, carries at most 8,192 bytes equal to the proven file, and a proof failure or oversize file produces no `hash-object` at all), substitution between proof and identity (one that persists, one restored before the second observation so that only re-reading the held handle can notice it, and Git answering a different identity), and 32 Application tests for the projection (structural cut at every position, nested names kept, header-imitating content that cannot move a boundary, unrecognized diff withheld whole, idempotence, every builder variant and every reduction step, the no-reserved-change manifest unchanged, oversized reserved hunks never sampled). Mutations restoring each bypass fail: a filename-based identity after the proof (4), a filename identity before the proof (4), the held-handle re-read skipped (1), Git's identity not compared (1), the capture not projecting (10 Api failures), the builders not withholding the diff (15), the reserved untracked entry delivered (14), the accounting removed (15 Application, 8 Api), the projection leaving untracked entries (2) or diff blocks (1 Application, 10 Api), and exact-case reservation (2).
- Second review correction (remaining R2 finding, found by Codex's second review; R1 accepted and preserved). Reproduction: `AgentEvidenceProjection.WithholdReservedDiff` treated `TrackedDiffParser.TryParsePath(header) == null` as a non-reserved block (`keepBlock` stayed true). In a real repository configured with `diff.noprefix=true` or `diff.mnemonicprefix=true`, replacing a tracked root instruction file with a hard link to an outside file made the ordinary Git observation return `diff --git AGENTS.md AGENTS.md` (or `c/`/`w/` prefixes) with the outside text, and the capture returned that text in `CompleteDiff` although the section said `Omitted`/`containment_unproven` and the capture was `Success`; the builders rejected the unsupported format later, which Codex did not reproduce as manifest disclosure, but the capture itself must not return unsafe text. Regressions were written first against the unfixed code and failed for the stated reason (the outside sentinel in the returned `CompleteDiff`): 21 of the 55 `AgentEvidenceProjectionTests` Application tests and 10 of the 32 `InstructionDeliveryProjectionTests` Api tests. Correction (`AgentEvidenceProjection`, `ChangeEvidenceManifest`; nothing else in production): a file block whose header cannot be decoded and classified with certainty now withholds the entire generic diff whenever a reserved path changed. Unknown never means non-reserved; no other prefix is guessed; the ordinary Git observation, its fingerprint, the reader's file access and sealed replay are untouched. Unrelated blocks are preserved exactly when every boundary is proven (the existing cut, mixed-evidence and no-reserved-change tests). When uncertainty also withholds unrelated hunks, the manifest states it with fixed truthful metadata: `diff: null`, `diffTruncated: true`, `diffSelection.complete: false`, `diffSelection.reservedInstructionFiles`, `reason: reserved_instruction_diff_withheld` and a fixed notice (a tracked reserved path with no delivered diff text; it never fires for an untracked-only reserved path), and `changedPaths` and unrelated untracked evidence stay. Tests: Application `AgentEvidenceProjectionTests` (+23: six undecodable formats × both names at every position, a decodable block followed by an undecodable header, the control that an undecodable diff without a reserved change is untouched, the projection's accounting, and every builder variant across every reduction step with the fixed reason), and Api `InstructionDeliveryProjectionTests` (+12: real Git with `diff.noprefix` and `diff.mnemonicprefix`, both names, with and without an unrelated change, a real Windows hard link, the real reader, builder, sealed artifact and the REAL critical-review adapter, asserting the whole returned delivery evidence, the whole manifest and the adapter's actual stdin; controls that an undecodable diff with no reserved change is delivered unchanged and that the ordinary capture, the preview capture and the checkpoint fingerprint are not normalized). Mutations, each applied alone and restored byte-identical: restoring `keepBlock = true` for an unknown path fails 21 Application and 10 Api tests; dropping only the unknown block fails 21 and 10; removing the fixed reason and notice fails 7 and 8.
- Mutation checks (each applied alone, tests run, then restored): foreign DevalCopilot references reintroduced (4 Application and 1 Architecture failures), a section bound to another workspace (1), a file read from the parent directory (14), the final-path containment proof skipped (1: the case-distinct spelling; the file-symlink test is skipped here), the identity proof skipped (1), the single-name check skipped (1), the second observation skipped (2), the Application accepting an over-claiming entry (5), a binary file admitted (1), an oversized file admitted (1 Infrastructure and 1 Application), the diagnosis claim and the planning claim no longer asking for context (1 and 3, plus Architecture), the correction builder dropping the section (16), and a supervisor recapturing or an adapter naming the instruction type (Architecture). A journey-level mutation (the diagnosis claim using the plain capture) failed both journeys naming `VerificationDiagnosis` stage and `Omitted,Omitted`. The mutation runs were made on the tree just before a behavior-preserving restructuring of `AgentInstructionFileReader` and `VerificationDiagnosisContextManifestBuilder` (and a formatter normalization); the containment and parent-directory mutations were repeated on the final code; everything else was not repeated and is labeled retained.
- FRESH validation on the second correction's tree (this round; the tree is the one described by this record): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, and again after deleting the generated client (regenerated, hash `13c9d116…c44a` unchanged); affected classes first, then sequentially `dotnet test`: Application 3880/3880 (3857 + 23), Infrastructure 998 passed + 4 skipped of 1002 (unchanged), Api 940/940 (928 + 12), Architecture 13/13; from `src/frontend/DevalCopilot.Frontend`: `npm run test:harness` 65/65, then, after it, one canonical `npm run test:e2e:all` (normal authentication, Program composition and all supervisors): Chromium 11/11 and the native-double journeys 2/2, no failure and nothing rerun; `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the recorded baseline (the two changed files among them are the unrelated pre-existing ones, and neither file touched in this round has a finding); Markdown links 294 checked with 0 problems; no NUL, lone CR, trailing whitespace, missing final newline or mixed line ending in any of the 84 changed or new files except the planner record's own CRLF; planner record SHA-256 `70e3b82d…5bf9` unchanged; `HEAD` `3b8450c4…` and `origin/main` unchanged, nothing staged; the two older owned roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched and the run created none. RETAINED from the preceding fresh validation below (the production and test files it covers did not change in this round, so these still apply): Domain 952/952, Vitest 1786/1786, typecheck, strict e2e `tsc`, lint (9 baseline warnings, 0 errors) and the production build of the frontend, `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none (no dependency changed), and the first-submission and first-correction mutation sets. SUPERSEDED by this round: the counts in the next bullet for Application, Api, the planner hash and the wording that a diff that cannot be cut is withheld without saying that an undecodable header counts as uncertain.
- Earlier fresh validation, after the first correction (R1 and R2 as first corrected; kept as evidence and superseded in counts by the bullet above): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, and again after deleting the generated client (NSwag regenerated it; hash `13c9d116…c44a` unchanged); affected checks first (the new Infrastructure, Application and Api classes), then sequentially `dotnet test`: Domain 952/952, Application 3857/3857 (3825 + 32), Infrastructure 998 passed + 4 skipped of 1002 (the 3 existing skips + the file-symlink skip; +6), Api 928/928 (908 + 20), Architecture 13/13; from `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1786/1786 (132 files), `npm run typecheck` exit 0, a standalone strict `tsc` over every e2e source exit 0, `npm run lint` exit 0 with 9 warnings (the baseline) and 0 errors, `npm run build` exit 0, `npm run test:harness` 65/65, then, after the harness, one `npm run test:e2e:all` with normal authentication, Program composition and all supervisors: the Chromium suite 11/11 and the native-double journeys 2/2; `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the recorded baseline (the two changed files among them, `UntrackedFilePreviewReader` and `CodeReviewContextManifestBuilder`, carry the same pre-existing findings on lines this change did not write); the planner record and client hashes as stated above; no NUL byte, lone CR, trailing whitespace, missing final newline or mixed line ending in any of the 84 changed or new files except the planner record's own CRLF; `HEAD` unchanged, nothing staged; the runs removed their own owned roots and `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched. No check failed, was skipped beyond the four Infrastructure skips, or was rerun to obtain a pass. Retained from the first submission and still true because the files they describe did not change: `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none (no dependency changed), the Markdown link check is repeated at the end of this entry's preparation, and the earlier mutation set (foreign references, parent-directory read, skipped containment and identity proofs, false Complete, lost stage coverage including a journey-level mutation, rebuilt replay) was run on the first-submission tree. SUPERSEDED by the fresh results above: the first submission's counts (Application 3825, Infrastructure 992 + 4 skipped, Api 908, harness 65, journeys and Chromium 13) and its statement that the identity was a filename `hash-object` and that only the section needed to be inspected for leaks.
- Remaining limits and risks: only the two root files are read (no imports or other instruction files), so this is not a complete project contract; the file may change after the second observation and before the provider runs; Windows is the only host with the physical proof (elsewhere both files are omitted); file content, including anything sensitive in it, reaches the provider as written, is not redacted, and is visible through the existing authenticated sealed-artifact viewer, which already serves manifest content in the browser (no new endpoint or viewer; the earlier wording that the text exists only in the manifest and provider input was wrong and is corrected in ADR-0021, the protocol and the engineering context); the section adds up to about 12 KiB to a manifest, so large plans can now lose whole instruction texts (accounted for) before change evidence is touched again; the checkpoint fingerprint's raw observation (status, full diff and the per-path `hash-object` of untracked files) and the older untracked-file preview and tracked-diff paths still read named repository paths and do not reject a hard link to an outside file, which is unchanged and not claimed to be hardened: only the two reserved root names are kept out of the generic delivery, and general hard-link hardening of other paths remains outside this slice; reserved names match ignoring case and a nested file with the same name is an ordinary file; a cross-reader defense exists (the builders project any capture), but a diff with any header this host does not decode (including every repository configured with `diff.noprefix` or `diff.mnemonicprefix`) is withheld whole when a reserved path changed, which hides that capture's unrelated hunks too (stated in the manifest with the fixed reason `reserved_instruction_diff_withheld`; changed paths and unrelated untracked evidence remain), and the ordinary observation is deliberately not normalized to avoid that; the file-symbolic-link test is skipped on this host. Not a real-provider test, and a provider's obedience to the boundary is not observed.
- Publication and post-publication status (2026-10-04): Codex granted publication GO for the exact reviewed 84-path snapshot (60 modified tracked, 24 untracked; inventory manifest SHA-256 `fb438594f2ef5f5d0096626a3346472373370420a84fb9e994b6cdcb6c54f302`, including Codex's GO edit of the planner record, `planner-handoff.md` `25e4ea9e388966dcfd845bbd015f91c73b260fc0d1caaa9053aa787941f65349`, `current-work.md` `7271ac59f3d98e64ec160542655ea04fd622723d7e664457724257a27443b79b` as reviewed and the generated client `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a`). Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `3b8450c48e247846944204ba8f2eab1b04bc635b`; empty index; exactly 60 modified and 24 untracked paths; the manifest's own hash, the exact 84-path inventory (no path missing or extra, none duplicated) and every approved file's raw SHA-256 and size matched. Exactly those 84 paths were staged, the staged inventory equalled the manifest (24 added, 60 modified; `git diff --cached --check` clean) and they were committed on that parent as the substantive commit `4f3e31ab70c2dda9220e48eded8c4b0920daf73a` (84 files changed), then pushed with a normal fast-forward push (`3b8450c..4f3e31a`; no force, no amend, no history reconciliation). After a fetch, `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to that SHA with a clean checkout. The planner record was committed exactly as Codex left it and is not edited by this closure.
  - Post-publication checks, run once on that commit with normal authentication, the normal host composition and all supervisors, .NET commands sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; with `--no-build --no-restore`: Application (`AgentEvidenceProjection`, `ProjectInstructionContext` and `CreateCodexPlanningAttemptInstructionContext` classes) 146/146, no skip; Infrastructure (`GitWorkspaceInstructionContextTests`) 27 passed and 1 skipped of 28, the skip being the environment-gated file-symbolic-link test (`A_symbolic_link_source_is_never_followed_to_content_outside_the_worktree`), which cannot create a link here without elevation; Api (`InstructionDeliveryProjectionTests`, `ProjectInstructionContextReplayHostedTests`, `HumanAuthorizedPlanHostedTests`, `VerificationDiagnosisHostedTests` and `ProviderFixtureInstructionEvidenceTests`) 60/60, no skip; Architecture 13/13. From `src/frontend/DevalCopilot.Frontend`: `npm run test:harness` 65/65 (0 skipped), then, only after it passed, `npm run test:e2e:all`: the Chromium suite 11/11 and the native-double journeys 2/2, with no browser failure and nothing rerun (the run removed its own owned root; `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched). The generated client hash was unchanged (`13c9d116…c44a`), `git diff --check` was clean and the checkout was clean after the checks. No check failed or was rerun.
  - RETAINED, not rerun on the published commit (recorded above from the reviewed tree, which is byte-identical to the commit for the 84 approved paths): the full Domain 952/952, Application 3880/3880, Infrastructure 998 passed + 4 skipped of 1002 and Api 940/940 suites, Vitest 1786/1786, typecheck, strict e2e `tsc`, lint (9 baseline warnings), the frontend build, `npm audit` 0 vulnerabilities, `dotnet list package --vulnerable --include-transitive` none, `dotnet format --verify-no-changes` 153 findings in 17 files (the baseline count), the mutation sets, and the Markdown link and whitespace/NUL checks. The earlier failure evidence is preserved unchanged in the review-correction bullets above (the first submission's NO-GO findings R1 and R2, their failing regressions, and the second review's remaining R2 finding with its reproduction and its 21 + 10 failing regressions).
  - Remaining limitations are those in the limits bullet above and are unchanged: only the two root files are read; the file may change after the second observation; Windows is the only host with the physical proof; content is not redacted and is visible through the existing authenticated sealed-artifact viewer; the older preview and diff paths and the fingerprint's raw observation do not reject outside hard links; a diff with an undecodable header is withheld whole when a reserved path changed; the file-symlink test is skipped on this host; this is not a real-provider test. This closure does not claim that Increment 4 is complete and selects no next slice.

## An escalated final plan reaches a reviewed candidate (2026-10-03)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `7cc361886eaf7bd6fad07292da401b98ff1c91a4` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing; nothing staged or untracked; only `planner-handoff.md` modified, SHA-256 `8038d8769ccc718005abc9f1a1fa3a559b6b5f5b596ec364dec170b99357daaf`, preserved byte-for-byte; generated client SHA-256 `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a` before and after normal regeneration, reproduced after deleting the file and rebuilding). It is presented as an uncommitted, unstaged and unpushed diff for Codex's GO/NO-GO; nothing is committed or pushed. No next slice is selected and Increment 4 is not claimed complete.
- Changed files: 26 tracked files modified (the preserved planner record, this record, the ADR index, the engineering context, the protocol, workflow model, cockpit specification and roadmap; the two production files; the journey support and ordinary journey, the harness tests and the SQL-seeded escalation fixture's comment; the six provider-fixture files; and the fixture contract, hosted, downstream and test-support tests) and 8 untracked (ADR-0020, `Policies/PlanningEscalationLegacyForm`, the escalated journey, `HumanAuthorizedPlan`, `ProviderFixtureAuthorizedPlanContractTests`, `PlanningEscalationFormTests`, `PlanningEscalationForms` and `EscalationForm`) after correction R1–R2 below (26 and 7 as first submitted). The generated client is not among them.
- What it delivers (one outcome, two halves that ship together): (1) [ADR-0020](../decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md) corrects the fixed text `PlanningEscalation` writes and keeps every historical escalation valid; (2) a second native-double browser journey proves, through the rendered cockpit, that a person can understand the escalation after two challenge rounds, authorize exactly one implementation of its exact final Proposal, request it separately, and carry that same plan through failed verification, Codex diagnosis, one guided Claude correction, Passed verification, ordinary Codex approval and a separate Human checkpoint approval, with one final reload.
- Production change (exactly the bounded writer and source-compatibility scope; no schema, migration, dependency, API shape, generated-client, provider flag, profile, permission, budget, lifecycle, scheduler or recovery change):
  - `PlanningEscalation` still writes protocol 1.0, the unchanged summary, the five content field names, the `unresolvedDecision` and the identifier-derived `evidence` sentence and the host-constructed attemptless Orchestrator-to-Human reply to the final Proposal. Its `options`, `consequences` and `recommendedChoice` now say that a human may inspect the final Proposal and the second-round Decisions and then either separately authorize one implementation of that exact plan and explicitly request it, or request a new plan; that the record selects nothing, grants nothing and approves nothing; that only a durably committed implementation claim consumes the authorization (a refused request or a claim that definitely does not commit consumes nothing, and a committed claim stays consumed even if its execution later fails); and that a third critical review or resolution is not available. The old "not implementable through this lineage" text is no longer written.
  - `Policies/PlanningEscalationLegacyForm` (new, internal, never written, never referenced by the writer) keeps the original serialization constant for constant. `PlanningEscalation.IsCanonicalContent` recomputes the complete current and the complete legacy serialization from the same verified identifiers and ordered second-round Challenges and accepts only ordinal equality with one whole form; `PlanningImplementationAuthorizationEvidence.EvaluateSource`, the one Application-owned source policy that the authorization, claim, dispatch gate, result recording, report-chain eligibility and cockpit read share, calls it. There is no mixed form, semantic JSON equivalence, other wording or version, caller-selected form or inference; every lineage, authorship, provenance, summary, uniqueness, currency, grant, consumption and downstream-chain check is unchanged. No historical message, grant, attempt, approval, artifact or sealed manifest is touched, and new escalations use only the new form.
  - Documentation: ADR-0020 (narrowly supersedes only ADR-0016's single-source-text-form requirement; ADR-0016 text unchanged), the ADR index, the engineering context, the protocol (escalation record and authorization source rule), the workflow model, the cockpit specification, and the stale roadmap paragraph that described every depth-two plan as neither reviewable nor implementable.
- Browser journey (`e2e/escalated-plan.journey.ts`, beside the unchanged ordinary journey, same host, database, supervisors, authentication, verified native-double destinations, `reuseExistingServer: false` and owned-root cleanup): manual intake and owned workspace/checkpoint; Planner root; first Challenge and its resolution; explicit second critical review with a materially different Challenge; second resolution and exactly one host escalation (current wording asserted from the host's own row); the cockpit shows the lineage statement and the decision panel while no implement, third-review or resolve control exists; a blank reason is refused locally; the required reason is typed and one authorization POST (200) records exactly one `HumanInstruction` and one relation and changes no claim, reservation, manifest, invocation, checkpoint or approval; the separate implementation request consumes that grant once (inputs: final Proposal, the second-round Decision, the instruction) and the grant stays Consumed through correction, review and the reload; Failed verification, review refused with 409, Codex diagnosis, one explicitly guided correction, Passed verification, ordinary Codex approval, separate Human approval of checkpoint 3, one final reload. No intermediate reload, SQL lineage seed, automatic advancement or supervisor suppression; every workflow mutation is a rendered control over the generated client. Observed: nine Agent claims in slots 1 through 9, 50 min reserved after the second round and 110 min of 120 at the end, one shared correction claim, two verification executions Failed then Passed, checkpoints 1, 2 and 3, distinct Agent and Human approvals of checkpoint 3 and no run completion.
  - Plan identity: three distinct Proposals with distinguishable content (`ROOT-PLAN`, `REVISED-PLAN`, `FINAL-PLAN` and different summaries); each resolution decides exactly its own challenges in order; implementation, diagnosis and review received the final Proposal, never the root or the first revision; the implementation report replies to the final Proposal and the correction report keeps its Planner-root reply identity. The journey asserts these from the host's rows and the doubles' allowlisted log through the pure `escalatedLineageProblems` judge, independently of what the doubles accepted.
  - Scoping: the journeys share one database and log, so `JourneyData` reads only the rows of the journey's own project, run and workspace, and the log is read only from the journey's own invocation interval (`markInvocations`). Both journeys passed together in the default order, together in the reverse order (the file was temporarily renamed to sort first, then restored) and alone, without a database reset.
- Fixture (`tests/…/BrowserJourney/ProviderFixture`; closed manifest-derived cases for root, first revision and final revision, no scenario framework, no production adapter contract change): the Claude review serves the root (first challenge) and the first revision (second challenge) and refuses any other proposal; the Codex resolution serves the root (first revision) and the first revision (final revision) for exactly the challenges it received and refuses the final plan and anything unmarked; diagnosis and review accept only a first or final revision. The authorized implementation case (`HumanAuthorizedPlan`) validates, before any edit, output or successful log entry, the fixed human-plan boundary (the fixture's own copy, compared to production in a test), the distinct resolution form, the exact final-plan identity, the three authorization identifiers, the fixed instruction, a non-blank rationale and the complete second-round Decisions (count and exact content); anything else exits 66/67 with nothing edited, written or logged. Only identity, count and hash facts are logged (`authorizationId`, `escalationMessageId`, `instructionMessageId`, `rationaleSha256`, `decisionCount`, `decisionChallengeIds`); never the rationale, guidance, a manifest or a transcript.
- Evidence written first, red then green. Against the parent production files (`PlanningEscalation.cs` and `PlanningImplementationAuthorizationEvidence.cs` restored from `HEAD` and the new legacy file removed, the new tests kept) `PlanningEscalationFormTests` ran 59 with 5 failed (the writer's exact text and every current-form authorization, claim, staleness and tamper case) and 54 passed: every legacy case and every rejection case passes on the parent, which pins the old serialization independently of the new writer; on the final tree the class is 60/60. (An earlier red run also showed two failures caused by a wrong assertion in my own new test about the second claim's error code; it was corrected before the green run and is not counted above.)
  Targeted mutations, each restored and verified byte-identical afterwards: dropping the legacy branch failed 9 cases (every legacy authorization, feed, dispatch-gate and report-chain case); ordinal-ignore-case comparison failed 4; semantic-JSON comparison failed 4; field-wise mixed acceptance failed at least 10 (the output was cut at 10 lines); making the diagnosis and the code review judge the Planner root instead of the implemented plan failed the escalated browser journey at the diagnosis (the host refused the report chain); the fixture accepting any plan marker for an authorized implementation failed 2 cases; the fixture skipping the boundary check failed 3.
- Tests added or changed: `PlanningEscalationFormTests` (60: both complete forms pinned byte for byte independently of production, the writer's retained facts, the explanation's content, authorization and single consumption for the writer, legacy and current forms, staleness, 34 whole-form deviations over both forms including mixed fields, edited wording, case, whitespace, member order, duplicate and extra members, uppercase, swapped, reordered, missing and foreign identifiers and another wording, a text edited after authorization, forged authorship and provenance, and two canonical escalations of different forms as ambiguous), `PlanningAuthorizationDownstreamTests` (the feed, dispatch gate and the three report-chain cases now also run over the legacy and current forms), `HumanAuthorizedPlanHostedTests` (the complete chain through verification, review, correction and re-review, and the restart replay of the sealed manifest, now each run for a historical escalation as well: exact sealed bytes, one grant, no second consent), `ProviderFixtureContractTests` plus the new partial `ProviderFixtureAuthorizedPlanContractTests` (98 real-child-process cases: 34 new for the three plans, the rounds and the authorization contract including 23 authorization defects), and 12 new harness regressions (the escalated-lineage judge detecting root and intermediate substitution in each plan-bearing stage, wrong content, missing, extra, reordered or repeated stages, a wrong round proposal or challenge count, each wrong authorization fact, a fact reaching another stage and indistinct plans; the journey data scope over two projects; the invocation interval). The existing detailed seam, race and budget tests were reused, not duplicated.
- Validation on the final tree (everything below was run fresh on it; nothing is retained from earlier): `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, and again after deleting the generated client (NSwag regenerated it; hash unchanged); sequentially `dotnet test`: Domain 952/952, Application 3725/3725 (3660 + 65), Infrastructure 971 passed + 3 existing skips of 974, Api 899/899 (863 + 36), Architecture 9/9; from `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 1786/1786, `npm run typecheck` exit 0 (the e2e sources, which that project does not include, were also checked with an explicit strict `tsc` run: clean), `npm run lint` exit 0 with 9 warnings (the unchanged baseline) and 0 errors, `npm run build` exit 0 (existing chunk-size notice only), `npm run test:harness` 52/52 (40 + 12); then, after the harness passed, `npm run test:e2e:all`: the real-host Chromium suite 11/11 (including `planning-authorization.spec.ts` and `wire-planning-authorization.spec.ts`, whose SQL-seeded escalation deliberately stays in the original form and so proves a historical escalation through the real host) and the native-double journeys 2/2; `npm audit` 0 vulnerabilities; `dotnet list package --vulnerable --include-transitive` none; `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the same count as the recorded baseline and none in a changed or new file; local Markdown links 284 checked, 0 problems; `git diff --check` clean (Git's CRLF notice for the planner record only) and no trailing whitespace or NUL byte in any modified or untracked file; the generated client and planner record hashes unchanged; the runs removed their own owned roots and `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched. No final-tree check failed, was skipped beyond the 3 existing Infrastructure skips, or was rerun to obtain a pass.
- Failures and noise preserved: a first compile of the new Application tests failed on an internal enum used in a public test signature (made public); a solution build with `--no-incremental` failed once with CS0006 (a missing reference assembly while projects built in parallel) and an ordinary rebuild succeeded; the Playwright output still carries the existing SignalR "connection was stopped during negotiation" lines; the escalated journey passed on its first complete run with no production change beyond the bounded writer and compatibility scope, so no production blocker was found or worked around.
- Limits and open risks: the doubles prove reachability and agreement with the adapters' contracts, never real-provider reliability; the fixture cannot see the second-round Challenge identities (the manifest carries only the Decisions), so it checks their count and exact content and logs their ids, and the journey compares the ordered ids with the host's rows; the invocation interval depends on the journeys running one after the other (`workers: 1`) because the log carries no run identity; the cockpit still renders only the escalation's summary (the five content fields are not shown as a card), so the options are conveyed there by the lineage statement and the decision panel, which this slice did not change; a Human checkpoint approval and an ordinary Codex approval remain distinct facts and neither completes the run.
  Not selected or implemented: a third round, wider authorization, grant refund, lifecycle or recovery authority, a scheduler, publication, provider-session resume, an account threshold or any real-provider invocation. Increment 4 is not claimed complete and no next slice is selected.

- Correction R1–R2 after Codex's NO-GO (2026-10-03; same slice, same parent `7cc361886eaf7bd6fad07292da401b98ff1c91a4`, still uncommitted, unstaged and unpushed; `HEAD`, local `origin/main` and live `refs/heads/main` matched it, nothing staged, 26 modified and 7 untracked before editing; the updated planner record, SHA-256 `adc1a8f4be9e2d9b83ec6a1bdd1a7c8ae861c6999350dfa7ad3e670b36de6015`, is preserved byte-for-byte including CRLF; generated client hash unchanged). No publication is claimed and no future SHA is recorded.
  - R1, final behavior: the new canonical `consequences` no longer says that requesting the implementation spends the authorization. It now says that authorizing permits exactly one implementation claim of that exact final plan; that only a durably committed implementation claim consumes the authorization; that a refused request, or a claim that definitely does not commit, consumes nothing; that a committed claim stays consumed even if its execution later fails; and, unchanged, that a new plan replaces the lineage, the record selects, grants and approves nothing, and a third critical review or resolution is not available. This states ADR-0016's existing consumption boundary; no claim, persistence, durability-resolution, budget or consumption behavior changed. Authorization and implementation remain separate explicit operations. The legacy serialization is byte-for-byte unchanged, and the host accepts exactly the legacy form and the corrected current form: the wording of the first submission is not a third supported form, and a new `unpublished-submitted-wording` case (for both base forms) proves it is refused as a source. ADR-0020 and the protocol passage were corrected to match; the cockpit panel's existing "the claim spends the authorization even if the implementation later fails" copy and ADR-0016 already agree with the boundary and were not touched. The independent oracle in `PlanningEscalationForms`, the pinned literal in `PlanningEscalationFormTests`, the explanation assertions (the consumption sentences present, "requesting it spends" absent) and the escalated journey's assertions on the host's own escalation row were updated; the existing refusal, rollback, race and committed-claim regressions (`CreateImplementationAttemptHumanAuthorizationTests`, `HumanAuthorizedPlanHostedTests`) were reused for the behavior and nothing was duplicated.
  - R2, organization: `PlanningEscalationLegacyForm` moved from the feature root to `Application/Features/Runs/Policies/` with its namespace and the writer's import matched, and the public `EscalationForm` enum moved out of `PlanningEscalationForms.cs` into its own `EscalationForm.cs`, visibility and behavior unchanged. No pre-existing type was moved.
  - Inventory after the correction (`git status`): 26 tracked files modified (the same 26 paths as first submitted) and 8 untracked (ADR-0020, `Policies/PlanningEscalationLegacyForm.cs`, `escalated-plan.journey.ts`, `HumanAuthorizedPlan.cs`, `ProviderFixtureAuthorizedPlanContractTests.cs`, `PlanningEscalationFormTests.cs`, `PlanningEscalationForms.cs`, `EscalationForm.cs`). The correction edited `PlanningEscalation.cs`, `docs/decisions/0020-…`, `docs/architecture/agent-collaboration-protocol.md`, this record, `PlanningEscalationForms.cs`, `PlanningEscalationFormTests.cs` and `escalated-plan.journey.ts`, and moved/split the two files above.
  - FRESH on the corrected tree: affected Application tests (`PlanningEscalationFormTests`, the authorization, downstream and second-round classes) 249/249 and Architecture 9/9 first; then `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, and again after deleting the generated client (NSwag regenerated it; hash `13c9d116…c44a` unchanged); sequentially `dotnet test`: Application 3727/3727 (3725 + 2 new rejection cases), Api 899/899 (it includes the fixture contract and hosted authorized-plan tests), Architecture 9/9; `npm run test:harness` 52/52; `npm run typecheck` exit 0, the strict `tsc` run over the e2e sources clean and `npm run lint` exit 0 (9 baseline warnings, 0 errors); `npm run test:e2e:all` (normal authentication, host composition and all supervisors): the first run failed in the Chromium suite (10 passed, 1 failed: `project-selection.spec.ts` "Tearing down \"gate\" exceeded the test timeout of 120000ms" after 2.1 min; because the script chains the two suites the journeys did not run in that attempt), and an unchanged full rerun of the same command then passed Chromium 11/11 and the journeys 2/2. At that point the failing spec, its gate fixture and all frontend production code were outside this diff and its cause was not established; the unchanged rerun passing does not explain it. Both runs' output is preserved, and R3 below records the mechanism later demonstrated and the correction. `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings in 17 files, the baseline count, none in a changed or new file; local Markdown links 285 checked (file and heading-anchor targets), 0 problems; `git diff --check` clean (Git's CRLF notice for the planner record only) and no NUL byte, trailing whitespace, lone CR, missing final newline or mixed line ending in any of the 34 modified or untracked files; `HEAD` unchanged, nothing staged; the runs removed their own owned roots and `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched.
  - RETAINED from the first submitted tree, not rerun on the corrected one (no Domain, Infrastructure or frontend production code, frontend unit test, dependency or lockfile change since): Domain 952/952, Infrastructure 971 passed + 3 existing skips of 974, `npx vitest run` 1786/1786, `npm run build`, `npm audit` 0 vulnerabilities and `dotnet list package --vulnerable --include-transitive` none, and the targeted mutation results (legacy branch dropped 9, ignore-case 4, semantic-JSON 4, field-wise mixed at least 10, root judged for the implemented plan, fixture plan marker 2, fixture boundary 3) recorded above; those mutations targeted logic this correction did not change, but they were not repeated against the corrected wording. The earlier failure and noise evidence in this entry is preserved unchanged.
  - Remaining limits: unchanged from the limits above (doubles prove agreement, not provider reliability; the fixture checks the second-round Decisions' count and content rather than the Challenge identities; the invocation interval assumes serial journeys; the cockpit renders the escalation's summary, not its five fields). Increment 4 is not claimed complete and no next slice is selected.

- Correction R3 after Codex's second review (2026-10-03; R1 and R2 accepted and preserved; same slice and parent `7cc361886eaf7bd6fad07292da401b98ff1c91a4`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it, nothing staged, 26 modified and 8 untracked before editing; the updated planner record, SHA-256 `db9ebde79c70adf2ae30358082799cab022634463ce9bc377a6f54daf67ae83a`, and the generated client, `13c9d116…c44a`, are preserved; still uncommitted, unstaged and unpushed, no publication claimed):
  - Observed facts (nothing more is claimed): the first canonical `npm run test:e2e:all` of correction R1–R2 failed in the Chromium suite, `project-selection.spec.ts`, "Tearing down \"gate\" exceeded the test timeout of 120000ms" after 2.1 min (10 passed, 1 failed; the chained journeys did not run). An unchanged full rerun passed Chromium 11/11 and the journeys 2/2. Both outputs are preserved (the first as `e2e-FAILED-run1.txt`). The historical cause and event order of that timeout were not reconstructed and are not claimed.
  - Mechanism shown, in the installed Playwright 1.63.0 (its bundled client source, Codex's local probe and a probe of mine, not the historical failure): a route settles only when its handler returned AND the route itself was handled (a successful `fulfill`, or `fallback`); a handler that throws settles it at once; and `unrouteAll({ behavior: 'wait' })` waits for that whole settlement for the routes that had not already thrown when it was called. `CockpitGate` caught and swallowed every error of its final `route.fulfill`, so a fulfill that failed after shutdown began returned normally with the route still unhandled and left that wait pending, with no hold and no recorded gate error. The unchanged gate reproduced exactly that against real Chromium (outcome `pending` at the bounded wait).
  - No expected abandoned-delivery exception exists to retain: with real Chromium and the installed Playwright, `route.fulfill` of a held answer succeeded without error after the page aborted the request, reloaded, navigated away and was closed (scratch probe), and the real "cancelled" scenario below drains with no recorded failure. So the swallow was removed outright and no error class is excluded, nothing is ignored, retried, slept on or given a longer timeout.
  - Change (`e2e/harness/cockpitGate.ts`, test support only): the `catch` around the final `route.fulfill` is gone; a failure propagates, `handle` records it as before, Playwright's wait settles, and `shutdown` still rethrows the recorded failures after releasing holds, unrouting, and awaiting every handler, all before page and context disposal. Late-hold refusal, release of existing holds and the normal and cancelled delivery are unchanged. The gate's structural types were narrowed to Playwright's public signatures (`fulfill` body `Buffer | string`, `body()` returns `Buffer`, `route()` returns `Promise<unknown>`); this is type-only, and it makes the existing spec's use of the gate type-check under strict `tsc` (that spec had not been in my earlier strict `tsc` runs, which covered the journeys, their support code and the harness, so those earlier "clean" statements did not cover it). Playwright itself drops a rejected handler's promise, so a failed fulfill is also reported by Playwright's own handling in the real runner; no global error observer was added to the fixture or the runner.
  - Regressions written first, red then green. The fake page in `cockpitGate.test.ts` now models the installed settlement and `wait` semantics (the earlier fake modelled only callback completion, which could not show this). Three new fake cases: a fulfill failure after shutdown starts settles teardown with that failure while it stays recorded and Playwright's handling sees it; an injected-answer fulfill failure stays observable; a successful held delivery records nothing. Three new real-Chromium scenarios (`gateRealScenario.ts`, one child process each, started with the Node option `--unhandled-rejections=warn`): delivery (drains, nothing recorded, the page receives the host answer), cancelled (the page aborted the held request; drains, nothing recorded) and fulfill-failure (teardown settles with Playwright's error, one failure recorded, no hold left, and Playwright's own report on stderr). Against the unchanged gate 10 of the 12 gate tests passed (the unchanged controls included the delivery and cancelled scenarios) and 2 failed: the new fake case never settled and the real fulfill-failure scenario ended `pending`. With the correction the 12 pass.
  - FRESH on the corrected tree: the gate tests 12/12; `npm run test:harness` 58/58 (52 + 6); strict `tsc` over every modified or new e2e source plus `project-selection.spec.ts` and `planning-authorization.spec.ts`, exit 0; `npm run typecheck` exit 0; `npm run lint` exit 0 (9 baseline warnings, 0 errors); one canonical `npm run test:e2e:all` with normal authentication, host composition and all supervisors: Chromium 11/11 (including `project-selection.spec.ts`) and the journeys 2/2, no failure and no rerun; the run removed its own root and `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched.
  - RETAINED, not rerun for this test-support-only change (no backend, generated-client, frontend production code, dependency or lockfile change since): the R1–R2 correction's solution build, Application 3727/3727, Api 899/899, Architecture 9/9, `dotnet format` 153 findings (baseline), and the generated-client hash; and from the first submission Domain 952/952, Infrastructure 971 passed + 3 existing skips, `npx vitest run` 1786/1786, `npm run build`, `npm audit`, the vulnerable-package check and the targeted mutation results. None of these was produced on this R3 tree.
  - Inventory after R3 (`git status`): 28 tracked files modified (the 26 of R1–R2 plus `e2e/harness/cockpitGate.ts` and `e2e/harness/cockpitGate.test.ts`) and 9 untracked (the 8 of R1–R2 plus `e2e/harness/gateRealScenario.ts`).
  - Remaining uncertainty: the cause of the historical timeout is unknown, and it is not shown that it was a fulfill failure; one passing canonical run is not evidence that it cannot recur. If a fulfill failure does occur it now fails loudly at teardown with Playwright's error instead of leaving shutdown pending. Not changed by R3: the original slice boundaries and stop gates; Increment 4 is not claimed complete and no next slice is selected.

- Publication and post-publication status (2026-10-04): Codex granted publication GO for the exact reviewed 37-file snapshot (28 modified tracked, 9 untracked; manifest SHA-256 `66536c129b5a3cad5de5cb5f803b1bfb02061934f5f1643f00e9c8c1680ccd23`, including the final planner edit, `planner-handoff.md` `52761f19fbec8694aa30c9ed65e88027354970faa11ee4385a171608f71bd97e`, `current-work.md` `1d05ab5f2b736143f1443b3fbe0c6431114fcafa3508a0279cc93f786ae7358b` and the generated client `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a`). Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `7cc361886eaf7bd6fad07292da401b98ff1c91a4`; empty index; the manifest hash, the exact 37-path inventory (no path missing or extra) and every approved raw file hash and size matched. Exactly those 37 paths were staged and the staged inventory equalled the manifest (`git diff --cached --check` clean apart from Git's CRLF notice for the planner record), and they were committed on that parent as the substantive commit `33c0cae9c6f358e1838ccf541c972fab5fed3569` (37 files changed), then pushed with a normal fast-forward push (no force, no amend, no history reconciliation). After a fetch, `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to that SHA with a clean checkout. The planner record was committed exactly as reviewed and is not edited by this closure.
  - Post-publication checks, run once on that commit with normal authentication, the normal host composition and all supervisors, .NET commands sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; with `--no-build --no-restore`: Application (`PlanningEscalationFormTests`, `PlanningAuthorizationDownstreamTests`, `AuthorizePlanningImplementation`, `GetPlanningImplementationAuthorization` and `CreateImplementationAttemptHumanAuthorizationTests`) 197/197; Api (`HumanAuthorizedPlanHostedTests` and `ProviderFixtureContractTests`) 113/113; Architecture 9/9; no test skipped in any of these runs. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 132 files, 1786/1786; `npm run typecheck` exit 0; `npm run lint` exit 0 with 9 warnings (the unchanged baseline), 0 errors; `npm run build` exit 0; the strict standalone `tsc` over every tracked e2e source plus `project-selection.spec.ts` and `planning-authorization.spec.ts` exit 0; `npm run test:harness` 58/58; then `npm run test:e2e:all`: the Chromium suite 11/11 and the native-double journeys 2/2 (the run removed its own owned root; `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` remain untouched). The generated client hash was unchanged (`13c9d116…c44a`), `git diff --check` was clean and the checkout was clean after the checks. No check failed, was skipped or was rerun.
  - RETAINED, not rerun on the published commit (all recorded above from the reviewed tree, which is byte-identical to the commit for the 37 approved paths): the full Domain 952/952, Application 3727/3727, Infrastructure 971 passed + 3 existing skips of 974 and Api 899/899 suites, `npm audit` 0 vulnerabilities, `dotnet format --verify-no-changes` 153 findings in 17 files (the baseline count, none in a changed file), `dotnet list package --vulnerable --include-transitive` none, the targeted mutation results, and the Markdown link and whitespace/NUL checks. The earlier failure evidence is preserved unchanged: the first canonical `npm run test:e2e:all` of correction R1–R2 failed in `project-selection.spec.ts` ("Tearing down \"gate\" exceeded the test timeout of 120000ms"), an unchanged rerun then passed, and R3 corrected the gate's swallowed-fulfill shutdown mechanism that was later demonstrated.
  - Remaining limitations: the cause and event order of the historical teardown timeout were not reconstructed and it is not shown that it was a fulfill failure; a passing run, including those above, does not show that it cannot recur, and a fulfill failure now fails loudly at teardown instead of leaving shutdown pending. The doubles prove reachability and agreement with the adapters' contracts, never real-provider reliability. The fixture checks the second-round Decisions' count and content rather than the Challenge identities; the invocation interval assumes the journeys run one after the other; the cockpit renders the escalation's summary, not its five content fields. This closure does not claim that Increment 4 is complete and selects no next slice.

## Trustworthy explicit local verification (2026-10-03)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `4ec68bd9143d426ccb2a746732db4a6fa22e1b1d` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing; nothing staged or untracked; only the planner-owned
  `planner-handoff.md` modified, SHA-256 `bb33c3d07dd86af0cd05560219c17799e93271096bf43bc040e1afc07f5201e8`, preserved byte-for-byte; generated client SHA-256 `35fabf7e14b14a3d314d1b73d1dffb96b4be8f6c82e84b02516b2e0f8f850903` before the slice and
  `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a` after normal regeneration, reproduced after deleting the file and rebuilding). It was presented as an uncommitted diff for Codex's GO/NO-GO and published after the GO recorded below. Increment 4 remains open.
- Changed files: 26 tracked files modified (the planner record is preserved unchanged; this record, the workflow model, the cockpit specification, the claim, dispatch-marker and SourceChanged commands and handlers, the eligibility feed, the execution and evidence projections and their endpoints, the supervisor comment and
  call sites, the generated client, the verification panel, the journey, and the existing claim-handler, endpoint and review-refresh tests that follow the changed signatures and reads) and 9 untracked (the Run ownership test, the dispatch snapshot and the shared dispatch authority, the claim seam, dispatch-decision, hosted-supervisor and mediator-boundary backend tests, the Run composition test and the real-HTTP wire specification); `git status --short` is authoritative.
- What it does (the existing explicit local verification boundary, restored; no schema/migration, dependency, provider or CLI contract, permission, budget, authorization, lifecycle, scheduler or recovery change and no new ADR; the protected POST, its HTTP 202 body and recipe configuration are unchanged):
  - Claim boundary: `ClaimVerificationExecutionCommandHandler` (still a manual-transaction command) reads the recipe, latest workspace and selected checkpoint untracked and applies the existing gates in their existing order, observes Git outside any transaction, then opens one short transaction through the existing `BeginTransactionAsync`. Its first statement is an
    atomic `ExecuteUpdate` incrementing the project's stored `NextVerificationExecutionNumber`, which both takes the SQLite write lock and reserves the number, so a stale tracked Project can neither supply the number nor overwrite the counter. It then re-reads the same authority untracked and requires the recipe (identity, name, executable, ordered arguments, timeout, enabled), the workspace (identity, path) and the checkpoint (identity,
    number, fingerprint) to equal what the capture was taken against, builds the execution from those fresh rows and saves and commits it with the counter. No latest-checkpoint-number rule was added: an older selected checkpoint with the same current fingerprint stays eligible. Existing refusal codes are reused (`not_found`, `disabled`, `workspace_not_ready`, `already_running`, `checkpoint_not_current`); the one new code is
    `409 verification.recipe_changed` ("The verification recipe changed while the request was being prepared."). Refusal, cancellation, a failed acquisition, save or commit leave no execution and no consumed number (the transaction is disposed; nothing is caught into success). A failure reported after the durable commit still propagates and the execution then exists, which the mediator-boundary test records as the uncertainty case.
  - Dispatch boundary: `EligibleVerificationExecution` still only proposes candidates and now also carries the project, workspace, checkpoint and recipe identities, and `ToSnapshot()` builds the operation-owned `VerificationDispatchSnapshot` (ownership identities, workspace path, fingerprint, executable, ordered arguments, timeout; list-aware equality). `MarkVerificationExecutionDispatchedCommand` and
    `RecordVerificationExecutionSourceChangedCommand` now take that snapshot and are manual-transaction commands: one short write-locked transaction (a self-referential no-op write as its first statement), an untracked re-read of the execution (not found, not Running, already dispatched keep their existing codes), then the shared `VerificationDispatchAuthority` requires snapshot agreement with the durable execution
    (`verification.execution_snapshot_changed`) and that the execution's checkpoint is still on the project's latest workspace, which is `Ready`, at the recorded path, with an active lease (`verification.execution_not_current`), before the single-use marker (or the pre-dispatch `SourceChanged` classification) is committed with a conditional update. Ownership loss or disagreement records no fingerprint or outcome, a refused dispatch invokes zero processes and leaves
    the claim pending, and genuine physical drift keeps its existing `SourceChanged` classification. The supervisor passes the feed's snapshot; Git, process and artifact I/O, output sealing, protected reads and restart reconciliation are unchanged and stay outside every transaction. The durable snapshot stays authoritative after the claim: a live recipe edit or disable neither cancels nor retargets it.
  - Visible evidence: `VerificationExecutionResponse` and `GetProjectGitEvidenceResponse` carry an additive `gitWorkspaceId` (the evidence one is a minimal extension of the planner's stated execution-projection field: the cockpit needs the current workspace identity to compare against, and no ownership is inferred from a recipe or checkpoint identity); the client is regenerated by the normal build. `VerificationCommandsPanel` passes the existing refresh
    generation to its execution read; Run needs settled, successful source and execution reads (so each poll also withholds it while pending) and no `Running` execution in the evidence's workspace whatever its recipe (an execution of another workspace never blocks), in the button and in the handler, which decides from the newest authority through a ref even when retained from an earlier render and ignores a second synchronous activation.
    Cached history stays visible with "Reading verification status…" while pending and the existing failure copy afterwards. A start whose outcome is unknown (a transport failure, a 5xx or an unreadable answer, i.e. anything other than a 4xx `ApiException`) shows fixed copy, never server detail, never retries, and blocks Run until a later explicit refresh read the status successfully; a 4xx keeps "This verification could not be started." An accepted claim stays real after a
    failed follow-up read or a project replacement. The explicit refresh issues reads only.
  - Journey: after each explicit Refresh evidence the existing guided journey now also asserts, without clicking, that the verification panel shows `Last run: Failed` and the manual review panel offers Changes requested but not Approve (Failed), and later `Last run: Passed` with Approve offered (Passed) and no manual review yet; the sole final reload, seven Agent claims, normal authentication, all supervisors, the real clients and owned cleanup are unchanged and nothing is written outside the rendered controls.
- Evidence written first, red then green. Against the parent (the parent source extracted with `git archive` into the scratchpad, with the new tests copied in): the claim seam class ran 45 with 36 failed (9 controls passed); the hosted supervisor class ran 26 with 18 failed (12 authority/snapshot-change cases and 6 obsolete-drift cases launched a process or recorded a classification; 8 controls passed); the mediator-boundary class ran 7 with 1 failed
  (a lease released by a second connection during the capture was still claimed); the Run composition test ran 16 with 13 failed against the parent panel. The dispatch-decision class cannot compile against the parent (it needs the new snapshot signature), so its red evidence is the hosted class plus four deliberate mutations on the final tree, each of which fails the tests that pin it: dropping the recipe comparison fails 10 seam cases; dropping the snapshot
  agreement fails 23 decision and 7 hosted cases; dropping the ownership check fails 12 decision and 9 hosted cases; dropping the authority check from the SourceChanged recording fails 13 decision and 6 hosted cases. All pass on the final tree.
- Tests added: `ClaimVerificationExecutionCommitSeamTests` (45 over real file-backed SQLite with the claim context alive and populated, including the stale tracked Project, and a second connection committing during `CaptureAsync` or immediately before `BEGIN` through the existing `FaultInjectingDbContext`: recipe disabled and each invocation field and reordered arguments, newer workspace, status, path, lease, checkpoint fingerprint, number and ownership drift, a competing Running
  execution of another recipe, fresh counter reservation (the stored counter is used and not overwritten), refusals consuming no number, concurrent claims creating one Running execution and one number, capture once before BEGIN and never again, no BEGIN after a refused observation, failed save, commit, acquisition and cancellation leaving nothing, the older matching checkpoint, a historical Running execution of another workspace, and a live recipe edit after the claim);
  `VerificationDispatchDecisionTests` (43: marker once and duplicate refusal, each durable invocation field and the ordered arguments, each ownership change, an expected snapshot disagreeing with an unchanged row for every field including identities, terminal and unknown executions, the stale tracked execution, six concurrent decisions marking once, acquisition/commit/cancellation failures, and the SourceChanged recording for genuine drift, obsolete observations, ownership loss, an already dispatched execution and a failed commit);
  `VerificationExecutionSupervisorHostedTests` (26 through the real supervisor, mediator, EF pipeline, file-backed SQLite and sealed output store with only the process and the Git observation as doubles: exactly one launch with the claimed ordered invocation, working directory, timeout and sealed output; the original snapshot after a live recipe edit; a mediator claim; twelve authority and snapshot changes applied during the supervisor's observation launching zero processes and leaving the claim pending with nothing
  fabricated; a refused observation; genuine SourceChanged without a process or invented outcome; six obsolete-drift cases recording nothing; two racing supervisors launching once; a dispatched but unrecorded execution never relaunched and reconciled as Interrupted; a process failure still recorded as Failed after one call);
  `ClaimVerificationExecutionTransactionBoundaryTests` (7 through the real mediator and EF Core SQLite with provider interceptors: a competing commit during the capture, one execution with the counter, transaction-start/save/pre-commit failures leaving nothing, a failure after the durable commit leaving the execution recorded, and cancellation); four endpoint cases in `VerificationCommandsEndpointTests` (protected 202 with the typed body, the additive workspace identity in both reads, a safe `409 already_running` and `404 not_found`); the existing claim-handler tests follow the new command signatures;
  the real-HTTP wire specification `wire-verification-claim.spec.ts` (the generated client resolves the 202 with its typed identity and number, both reads carry the workspace identity, and refusals keep safe Problem Details; the project, workspace, checkpoint and recipe come from public operations and the host's own supervisor ran the Node executable); and `VerificationRunRefresh.test.tsx` (16 over the real owner, panel and hooks: first frame, pending, failed and recovered execution reads with cached history,
  polling with another running recipe, a historical workspace's running execution, the handler enforced even when retained, a double activation, an accepted claim followed by a failed read, a refused claim, uncertain outcomes (transport and server fault) with fixed copy, no retry and refresh-gated recovery, replacement and A→B→A, unmount, and overlapping reads). The existing review-refresh tests now expect the verification panel's own execution read in an explicit refresh (two reads, one per panel).
- Checks (Windows; sequential .NET; final tree unless marked retained): normal `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, the client reproduced identically after deleting it (the delta is only the additive `gitWorkspaceId` members of the two response classes and interfaces); Domain 952/952; Application 3660/3660 (3572 + 88); Infrastructure 971 passed + 3 existing skips of 974 (+7); Api 863/863 (+30); Architecture 9/9;
  frontend `npx vitest run` 131 files, 1770/1770 before the R1–R3 correction (1754 + 16; 132 files, 1786/1786 after it); `npm run typecheck` clean; `npm run lint` exit 0 with 9 warnings (the unchanged baseline), 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 40/40; then `npm run test:e2e:all` after the harness passed: Chromium 11/11 (10 + the new wire specification) and the guided journey 1/1; `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings, the same count as the recorded baseline and none in a changed file
  (a first run showed 156 because of three layout findings in my new mediator-boundary test, fixed before the final run); `dotnet list package --vulnerable --include-transitive` none; whitespace/NUL hygiene, `git diff --check` (CRLF notices only) and local Markdown links (file and anchor) of the changed documents clean; the planner record and generated-client hashes as above.
- Correction R1–R3 (Codex NO-GO, frontend, tests and documentation only; no backend, generated-client or contract change): (R1) the per-recipe `isRunning` flag ignored the workspace identity, so a historical Running execution of another workspace still disabled its own recipe; the control state now follows the same rule as the workspace-wide gate (only an execution of the current
  workspace labels a recipe Pending/Running or disables it; a confirmed different workspace is shown as "Last run: Running (earlier workspace)" and never controls Run; a missing identity on the execution or on the evidence stays conservative). (R2) the duplicate-activation guard was a component-wide boolean ref, so an unkeyed replacement left A's pending request blocking B; it is now an object owned by the committed lifetime and the exact request,
  checked by lifetime and released only by the request that set it, so an old request neither blocks the replacement nor releases its protection, while two synchronous activations in one lifetime still send once. (R3) uncertainty was stamped with the generation at POST start, so a refresh that settled (or was in flight) before the unknown failure arrived discharged it at once; it is now stamped with the generation current when the
  failure is known, only a later explicit refresh whose status and source reads succeeded discharges it, and the discharge is committed to the lifetime's state so a later pending or failed read cannot revive it. Evidence written first: `VerificationRunOwnership.test.tsx` (16 over the directly rendered panel with no parent key: R1 historical display for the same and other recipes, a start from an enabled recipe, the positive current-workspace block and the
  unknown-ownership cases; R2 A→B, A→B→A, A completing while B's own POST is pending with a retained B handler rejecting a second activation and no refresh or report for B from A, and the preserved double-activation guard; R3 an earlier refresh settled and one still in flight when the failure arrives, a failed and a successful later refresh, no revival, replacement and a late failure of a replaced project, and the preserved refusal path) failed 10 of 16
  against the submitted panel (the other six were controls) and passes 16/16 after the correction. Checks after the correction, on the final tree: affected panel tests 36/36 across the three verification-panel files; full `npx vitest run` 132 files, 1786/1786 (1770 before); `npm run typecheck` clean; `npm run lint` exit 0 with 9 warnings, 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 40/40;
  `npm run test:e2e:all` after the harness passed: Chromium 11/11 and the guided journey 1/1 (no skip, no rerun). RETAINED unchanged from before the correction (no backend, generated-client or contract change since): the build/NSwag, Domain, Application, Infrastructure, Api and Architecture results above, `dotnet format` (153, baseline) and the vulnerable-package check; the generated client hash is still `13c9d116…c44a`. The cockpit specification now states the
  per-recipe, lifetime-owned and uncertainty-boundary rules.
- Failures and noise preserved: `dotnet build` failed intermittently three times with CS2012 (the Api `obj` assembly locked by the shared compiler server or a parallel project build) and succeeded on rerun, so later builds used `-p:UseSharedCompilation=false -m:1`; a first copy of one endpoint-test seed lost its backslashes through a scripting escape and was corrected before it ran; a handler-bypass test first assumed React would run a click handler on a control whose props say disabled (it does not), so retained handlers are now invoked directly;
  SignalR negotiation console lines remain in the Playwright output. The two older owned roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` noted in the planner record were left untouched; the runs of this slice removed their own roots.
- Limits and open risks: a refused dispatch leaves the claim pending and the supervisor re-evaluates it on every poll, so an ineligible claim can remain pending indefinitely (no cancel, retry, refund, terminal status or recovery was added); the eligibility feed still lists any `Ready` workspace with an active lease and the final decision alone enforces the project's latest workspace; the filesystem is not frozen between the physical observation, the claim commit, the dispatch commit and the process
  start, and snapshot agreement is not cryptographic protection against out-of-band tampering with the database; acquisition, save and commit failures propagate as request failures and a failure after the durable commit cannot prove nothing was recorded; the uncertainty classification treats every non-4xx failure as unknown; `gitWorkspaceId` on the evidence response is a small additive extension beyond the stated execution projection field; the doubles prove local agreement, not real-provider reliability.
  Not selected or implemented: session persistence/resume/compaction, account allowance, new providers, recipe or permission changes, ambiguous-process and no-change recovery, lifecycle completion, scheduling and publication. Increment 4 is not claimed complete and no next slice is selected.

- Publication (2026-10-03): Codex granted publication GO for the complete corrected 35-file diff (26 modified tracked, 9 new), including correction R1–R3 and the planner-owned GO record. Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `4ec68bd9143d426ccb2a746732db4a6fa22e1b1d`; nothing staged; the Codex manifest
  (SHA-256 `20cf3854153a11065c57fc8e2b7bc90619b69fb1074cd1eec854ad1ee85bdcaa`) listed exactly the 35 changed paths and every raw-file SHA-256 matched, including `planner-handoff.md` `fd97fd474a5c0d4ee30547ea8666317020b6b17242fc4453149fed9dbbc94926`, `current-work.md` `62274dfdd27acf529a1ea699452758393bcba909736c7ffeb795c13200b27db7` and the generated client
  `13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a`. Exactly those 35 paths were staged (no manifest, database, build output, probe or test result; `git diff --cached --check` clean apart from Git's CRLF notices) and committed on that parent as `4e58c031660602fb257b068ba1d7988b5643125e`, then pushed with a normal fast-forward push (no force). After a fetch, `HEAD`, local `origin/main` and live
  `refs/heads/main` were verified equal to that SHA, with no staged, unstaged or untracked file. The planner record was committed exactly as reviewed and is not edited by this closure.
  - Post-publication checks, run on that commit with normal authentication, the normal host composition and all supervisors, .NET commands sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; with `--no-build --no-restore`: Application (`ClaimVerificationExecutionCommitSeamTests`, `VerificationDispatchDecisionTests`,
    `ClaimVerificationExecutionCommandHandlerTests`) 94/94; Infrastructure (`ClaimVerificationExecutionTransactionBoundaryTests`) 7/7; Api (`VerificationExecutionSupervisorHostedTests`, `VerificationCommandsEndpointTests`) 40/40; Architecture 9/9; no test skipped in any of these runs. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 132 files, 1786/1786; `npm run typecheck` exit 0; `npm run lint` exit 0 with 9 warnings
    (the unchanged baseline), 0 errors; `npm run build` exit 0; `npm run test:harness` 40/40; then `npm run test:e2e:all`: the Chromium suite 11/11 and the guided collaboration journey 1/1 (they removed their own owned roots; the two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` noted in the planner record remain untouched). The generated client hash was unchanged (`13c9d116…c44a`), `git diff --check` clean and
    the checkout clean after the checks. No check failed, was skipped or was rerun.
  - RETAINED, not rerun on the published commit: the full Domain 952/952, Application 3660/3660, Infrastructure 971 passed + 3 existing skips of 974 and Api 863/863 suites, `npm audit` 0 vulnerabilities, `dotnet format --verify-no-changes` 153 findings identical to the recorded baseline, and `dotnet list package --vulnerable --include-transitive` none, all recorded above from the pre-publication tree (the R1–R3 correction touched only
    frontend code, tests and documentation). The earlier failure, noise and correction evidence recorded in this entry is preserved unchanged.
  - Remaining limitations: unchanged from the limits above. A refused dispatch leaves its claim pending and re-evaluated on every poll; the eligibility feed lists any `Ready` workspace with an active lease and only the final decision enforces the latest workspace; the filesystem is not frozen between observation, claim commit, dispatch commit and process start; snapshot agreement is not protection against out-of-band tampering; failures on the
    database seams propagate and a failure after the durable commit cannot prove nothing was recorded; the uncertainty classification treats every non-4xx failure as unknown; the doubles prove local agreement, not real-provider reliability. This closure does not claim that Increment 4 is complete and selects no next slice.

## Trustworthy manual checkpoint review (2026-10-03)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `b38947507e4fc018190152bfd69bef8b3a2fbe3f` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it before editing; nothing staged or untracked; only the
  planner-owned `planner-handoff.md` modified, SHA-256 `c091d5ee71703d270ea588b60daff902942fae7ff24ee46c120ee0afdeac001b`, preserved byte-for-byte, then `c2d698c2b4ab291613ac33deab665c521c28a734469a38a89b91a0b9c87f079c` after Codex's own NO-GO review edit, which is likewise preserved byte-for-byte; generated client SHA-256 `1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2` before the slice and
  `35fabf7e14b14a3d314d1b73d1dffb96b4be8f6c82e84b02516b2e0f8f850903` after normal regeneration, reproduced after deleting the file and rebuilding; the two values differ only by the one regenerated 201 branch below). Presented as an uncommitted, unstaged,
  unpushed diff for Codex's GO/NO-GO; no publication is claimed. Increment 4 remains open.
- Changed files: 15 tracked files modified (the planner record was preserved by the executor and is included in the reviewed publication; this record, the workflow model, the cockpit specification, the review endpoint and handler, the generated client, the panel and its two hooks and
  panel test, the journey and its read-only database helper, and the Api and Infrastructure test files) and 7 untracked (the Application seam test class, the hook, settlement and composition frontend tests, the generated-client contract test, the wire specification and its fixture); `git status --short` is authoritative.
- What it does (three connected parts of the one existing manual `POST /api/projects/{projectId}/reviews` operation; no schema, dependency, provider, permission, budget, lifecycle or recovery change and no new ADR; global NSwag settings and every other endpoint untouched):
  - HTTP and generated client: `RecordCheckpointReviewEndpoint` declares `[ProducesResponseType<RecordCheckpointReviewResponse>(201)]`, so the normal build regenerates `processRecordCheckpointReview` with a `status === 201` success branch (it accepted only 200 while the host answers
    `Created`); the protected POST, request fields, response body and `Location` are unchanged. An accepted decision therefore resolves with its typed review identity and decision, is sent once and no longer appears failed; refusals stay `ApiException`s with their Problem Details.
  - Fresh atomic commit boundary: `RecordCheckpointReviewCommandHandler` keeps the manual-transaction command and the Git capture outside any transaction, but now decides the source from untracked reads before the capture and, after it, opens one short transaction through the existing
    `BeginTransactionAsync`. Its first statement is a self-referential no-op `ExecuteUpdate` of the project row that takes the SQLite write lock (the repository's existing pattern); inside it the project, latest workspace (status and path), active lease and exact current checkpoint (identity, number,
    fingerprint) are re-read untracked, must equal what the capture was taken against (never silently retargeting a newer workspace or checkpoint; an unequal source is `reviews.checkpoint_not_current`), and the selected execution is re-read untracked (project, workspace, checkpoint and
    fingerprint ownership, then Running, Approved-requires-Passed) before the review and its member are built from those fresh rows, saved and committed together. Existing refusal codes are reused; stored terminal evidence that is not internally coherent (the domain factory rejects it) is refused as
    `reviews.evidence_not_terminal` instead of failing the request. Refusal, cancellation, a failed save, commit or acquisition leave no review or member (the transaction is disposed and nothing is caught into success; database failures still propagate). No Git, filesystem or provider work occurs
    under the lock. Policy is unchanged: Pending includes no execution, ChangesRequested/Escalated may cite any terminal execution of the exact checkpoint (not necessarily the newest, no recipe coverage required), Approved requires the selected execution Passed, Human/FutureAgent are stored as given, and
    recorded reviews stay immutable. The Git observation does not atomically freeze the working tree through the commit (documented in the workflow model).
  - Manual-panel refresh: `useProjectVerificationExecutions` and `useProjectCheckpointReviews` accept the existing `refreshGeneration` (default 0, so every other consumer is unchanged), read it when a read begins (through a ref, so a continuation retained from an earlier generation that begins a
    read after the owner asked again still counts for the present one), re-read when it advances, and expose `current` (no read is in flight and the newest accepted read, begun for the displayed generation, succeeded), `loading` and `readFailed`. The polling hook keeps its single owned one-second chain
    (the generation read runs inside it; the effect cleanup clears the one timer). `CheckpointReviewPanel` offers decided actions only with current source evidence and current execution evidence for the displayed generation (first frame, pending and failed reads withhold them, with fixed copy and no cached
    Passed evidence), keeps Pending execution-free and available whenever the source evidence is current, shows each review's applicability and the previous-approval warning only after the review read of the displayed generation succeeded (otherwise "Confirming applicability…" or "could not be
    confirmed"), and reports an accepted decision followed by a failed review read as "Review evidence could not be loaded." and never as a failed submission. The refresh sends no review, verification, capture or Agent request; no remount, bus, cache or automatic progression was added;
    `VerificationCommandsPanel` and the git-evidence hook are untouched.
  - Journey: after the ordinary Codex approval, an explicit Refresh evidence (no reload) and the rendered manual panel's Approve for the exact current checkpoint #3 and its Passed execution #2, the host records one Human Approved fact over HTTP 201 (asserted from the rendered POST's status); the page then shows
    `Checkpoint #3 · verification #2 · Human` and `Current checkpoint`, and the same facts persist after the journey's only reload. Read-only database assertions show the ordinary approval had already recorded its own `FutureAgent` Approved fact (unchanged afterwards) and the manual fact is a second, distinct row on
    checkpoint 3 whose single evidence member is execution #2 (Passed, exit 0, checkpoint 3's fingerprint); the manual review created no attempt, message, invocation, claim or lifecycle change (seven Agent claims, Failed then Passed verification, three checkpoints, the normal host composition,
    all supervisors, normal authentication and the real generated clients are unchanged; no raw-SQL workflow mutation and no real provider).
- Evidence written first, red then green: against the parent handler the new Application seam class plus the existing handler tests ran 58 with 31 failed (the 12 existing tests and 15 of the new ones, the controls, passed); the mediator-level regression reproducing Codex's finding (a second connection commits
  checkpoint 2 during `CaptureAsync`) failed because Pending was recorded against checkpoint 1; against the parent panel and hooks 26 of the 33 new frontend tests failed (7 controls passed); the 201 contract test failed against the parent generated client and the real-HTTP wire specification failed with "An unexpected server error occurred."
  for the Created answer; with the parent generated client the rendered journey failed at the new step because the panel reported "This review decision could not be recorded." although the host had accepted the Human fact. All pass on the final tree. Two deliberate mutations each fail the tests that pin them: dropping the in-transaction source
  equality check fails 4 seam cases, and dropping the selected execution's fingerprint predicate fails 2.
- Tests added: `RecordCheckpointReviewCommitSeamTests` (46 over real file-backed SQLite with the claim context alive and populated and a second connection committing either during `CaptureAsync` or immediately before `BEGIN` through the existing `FaultInjectingDbContext` seam: checkpoint replaced for Pending and Approved,
  newer workspace, workspace status, path, lease and checkpoint-fingerprint drift, selected-execution project/workspace/checkpoint/fingerprint drift, deletion, Passed to Failed, back to Running and incoherent terminal data, each refused with its existing code and zero review or member rows; a still-eligible execution recorded from its fresh state; Pending, Approved, ChangesRequested
  and Escalated positive controls including selecting the older Passed execution while a newer one failed and Human/FutureAgent; a foreign execution; capture once before BEGIN and never again, no BEGIN after a refused capture or an early gate; failed save, failed commit, cancelled save and failed acquisition leaving nothing);
  one `RecordCheckpointReviewTransactionBoundaryTests` case through the real mediator pipeline and EF transaction behavior (a second connection commits a checkpoint during the capture, which proves no lock is held across the Git read; the review is refused with no rows); the existing Api endpoint test now also asserts the `Location`, the persisted Human review and its member,
  plus a conflict Problem Details case; frontend `evidenceRefreshGeneration.test.tsx` (18: first-frame, pending, failed and recovered generations for both hooks, newest-read ordering and late failures, retained callbacks, A→B→A, unmount, the single polling chain across generations, accepted POST then failed read, refused POST, retained `record` of a replaced project),
  `CheckpointReviewRefresh.test.tsx` (15 over the real owner, panel and hooks: first frames, stale Passed withheld while pending and after failure, Pending available, no submission from a pending read, source-pending, review applicability pending and failed, accepted-then-unreadable, refused decision, no mutation requested by the refresh, overlapping refreshes, A→B→A, a returning project's
  own read, unmount, reviewer choice preserved), `recordCheckpointReviewContract.test.ts` (2: the generated client resolves the 201 with typed identity and one request, a 409 keeps its Problem Details), the real-HTTP wire specification `wire-checkpoint-review.spec.ts` (its terminal executions are an owned raw-SQL evidence fixture like the other wire specifications; no process is started), and the extended journey.
  The existing panel test's mocks gained the three new hook fields.
- Validation evidence (Windows; sequential .NET): backend/build/NSwag, audit and formatter results in this bullet are retained from before R1; the final frontend results were rerun after R1 as detailed below. Normal `dotnet build DevalCopilot.slnx` 0 warnings/0 errors, the client reproduced identically after deleting it; Domain 952/952; Application 3572/3572 (3526 + 46); Infrastructure 964 passed + 3 existing skips of 967 (+1); Api 833/833 (+1); Architecture 9/9;
  frontend `npx vitest run` 130 files, 1754/1754 (1707 + 47, after correction R1; 1742 before it); `npm run typecheck` clean; `npm run lint` 9 warnings (the unchanged baseline), 0 errors; `npm run build` clean (only the existing chunk-size notice); `npm audit` 0 vulnerabilities; `npm run test:harness` 40/40; then `npm run test:e2e:all` outside the red experiments: Chromium 10/10 (9 + the new wire specification) and the guided journey 1/1;
  `dotnet format DevalCopilot.slnx --verify-no-changes` 153 findings, the same count as the recorded baseline and none in a changed file; `dotnet list package --vulnerable --include-transitive` none; whitespace/NUL hygiene of all 22 changed or new files, `git diff --check` (CRLF notices only) and local Markdown links (file and anchor) of the 4 changed documents (including this record) all clean; the planner record and generated-client hashes as above.
  After the deliberate mutations were reverted the affected suites were rerun on the restored tree (Application filter 58/58, Infrastructure boundary 2/2, Api endpoint class 10/10).
- Correction R1 (Codex NO-GO, frontend, tests and documentation only; no backend, generated-client or contract change): `useProjectVerificationExecutions` and `useProjectCheckpointReviews` derived `current` from the read generation and the failure flag but not `loading`, so once a generation had
  succeeded any further read of it (a retained or manual refresh, each executions poll, and the reviews read that follows every accepted decision) was `current` while pending and the panel kept certifying cached review applicability. `current` now also requires `!loading`, i.e. a settled successful read; an older overlapping
  completion still cannot clear the newer pending state (only the newest accepted read ends loading) and cannot restore currency, and a failed read stays not current until a later success. The cached lists remain visible as non-authoritative history. In `CheckpointReviewPanel` decided actions still require current source and execution
  evidence, Pending still needs only current source evidence, and review applicability and the stale-approval warning are withheld ("Confirming applicability…" or "could not be confirmed") whenever the review read is not settled; the verification-evidence selector now keeps showing the cached eligible executions while an execution read
  is in flight (so it does not disappear and reappear on every poll of a running verification) but offers no decision from them. The polling hook's single one-second chain and the accepted-POST semantics are unchanged (the read failing after an accepted decision is still reported as a read failure). Consequently, while a verification is Running
  the decided actions are disabled during each pending poll and enabled again as it settles. Evidence written first: the new `evidenceReadSettlement.test.tsx` (8 hook cases: same-generation refresh with a previously Passed list, polling with an older Passed and a Running execution, the read after an accepted decision, overlapping reads in both completion
  orders, failed-read behavior and recovery, for both hooks) and four new composition cases in `CheckpointReviewRefresh.test.tsx` (cached applicability and the stale-approval warning during the read after an accepted Pending decision; the failed read after it; an older overlapping review read; decided actions withheld and Pending kept during a pending poll)
  ran against the submitted hooks and panel with 8 of the 12 failing (6 hook cases and 2 composition cases; the 2 failed-read recoveries, the failed-read-after-acceptance case and the overlapping-review case passed as controls), then 12/12 after the correction. The first-frame case of the existing composition file was
  intermittently failing (it failed once in three runs) because its source read could settle before the assertion; it now gates the source read and passed five consecutive runs. Checks after R1, on the final tree: affected files 47/47; full `npx vitest run` 130 files, 1754/1754; `npm run typecheck` clean; `npm run lint` 9 warnings, 0 errors; `npm run build` clean;
  `npm run test:harness` 40/40; then `npm run test:e2e:all` Chromium 10/10 and the guided journey 1/1 (no skip, no rerun). RETAINED unchanged from before R1 (no backend, generated-client or contract change since): the build/NSwag, Domain, Application, Infrastructure, Api and Architecture suites, `npm audit`, `dotnet format` and the vulnerable-package results above; the generated client hash is still `35fabf7e…0903`.
- Failures and noise preserved: the first `dotnet build` after the endpoint change failed once with CS2012 (the Api `obj` assembly was in use by another process) and the identical command succeeded on the next run; three of the 33 new frontend tests needed corrections of my own test assumptions (buttons render only once a checkpoint exists, the panel's executions are a separate read from the verification panel's, and the default
  selection is the first eligible execution) before passing; the first journey run failed because the ordinary approval already records its own `FutureAgent` review fact (the assertion was corrected, not the product); SignalR negotiation console lines remain in the Playwright output as in earlier slices. The two older owned roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` noted in the planner record were left untouched; the runs of this slice removed their own roots.
- Limits and open risks: the Git observation does not freeze the working tree through the commit, so a source change after the observation leaves a correctly recorded review a stale historical fact; the transaction acquisition, commit or save failures propagate as request failures rather than a known refusal (nothing is recorded); the manual review grants no Agent or provider authority, consumes no claim or grant, authorizes no publication and does not complete a run;
  `VerificationCommandsPanel` and the other status hooks keep their own refresh rules, so an explicit refresh re-reads the manual panel's executions but not the verification panel's; the doubles prove local agreement, not real-provider reliability. Not selected or implemented: session persistence/resume/compaction, account allowance, new providers, recipe or permission changes, ambiguous-process and no-change recovery,
  lifecycle completion, scheduling and publication. Increment 4 is not claimed complete and no next slice is selected.
- Publication (2026-10-03): Codex granted publication GO for the complete reviewed 22-file diff (15 modified tracked, 7 new), including correction R1 and Codex's two factual clarifications in this record (the planner record is part of the publication; the backend, build/NSwag, audit and formatter evidence
  is retained from before R1). Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `b38947507e4fc018190152bfd69bef8b3a2fbe3f`; nothing staged; SHA-256 `planner-handoff.md` `7268afa9aa05eadc385c3803e6ddf84540492896bda3deabd9d99dade7bdedd1`, `current-work.md`
  `87183a5b887435659e8d46c8b4c7debd2506b2262552dfc28a8f0d02724a3fc2`, generated client `35fabf7e14b14a3d314d1b73d1dffb96b4be8f6c82e84b02516b2e0f8f850903`. Exactly the 22 authorized paths were staged (no probes, databases, build output or test results; `git diff --cached --check` clean apart from Git's CRLF notices) and committed on that parent as
  `f8408d7af722f432ee3fd3c419f95af735b475ce`, then pushed with a normal fast-forward push (no force). After a fetch, `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to that SHA, with a clean checkout. The planner record was committed exactly as reviewed and is not edited by this closure.
  - Post-publication checks, run on that commit with normal authentication and the normal host composition, sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; with `--no-build --no-restore`: Application filtered (`RecordCheckpointReview`) 58/58; Infrastructure
    filtered (`RecordCheckpointReviewTransactionBoundaryTests`) 2/2; Api filtered (`VerificationCommandsEndpointTests`) 10/10; Architecture 9/9; no test skipped in any of these runs. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 130 files, 1754/1754; `npm run typecheck` exit 0; `npm run lint` exit 0 with 9 warnings (the unchanged
    baseline), 0 errors; `npm run build` exit 0; `npm run test:harness` 40/40; then `npm run test:e2e:all`: Chromium 10/10 and the guided collaboration journey 1/1 (it removed its own owned roots; the two older roots `devalcopilot-e2e-2Jzi46` and `devalcopilot-e2e-ok3LGr` noted in the planner record remain untouched). The generated client hash was unchanged
    (`35fabf7e…0903`), `git diff --check` clean and the checkout clean after the checks. No check failed, was skipped or was rerun.
  - RETAINED, not rerun on the published commit: the full Domain 952/952, Application 3572/3572, Infrastructure 964 passed + 3 existing skips of 967 and Api 833/833 suites, `npm audit` 0 vulnerabilities, `dotnet format --verify-no-changes` 153 findings identical to the recorded baseline, and `dotnet list package --vulnerable --include-transitive` none, all recorded above from the pre-publication tree
    (their code is unchanged by R1, which touched only frontend code, tests and documentation). The earlier failure and noise evidence recorded in this entry is preserved unchanged.
  - Remaining limitations: unchanged from the limits above. The Git observation does not freeze the working tree through the commit; transaction acquisition, save and commit failures propagate as request failures with nothing recorded; the verification panel keeps its own refresh rules; the doubles prove local agreement, not real-provider reliability. This closure does not claim that Increment 4 is complete and selects no next slice.

## Direct human guidance for verification-diagnosis-origin corrections (2026-10-03)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent `681b7509da28aa91825fe7ca209096234ddf9804` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it; nothing staged or
  untracked; only the planner-owned `planner-handoff.md` modified, SHA-256 `5b8dd893feb36a6b6bdac92b7d1a848a2d8a3a0f5759c0691a914a13ee4796d1` at dispatch and, after the planner's later update, `e7f67f2cee0e341f87aed7ea1ecc29fff52671fef1ac7a1f8932a0e4e34f4306`, preserved byte-for-byte; generated client SHA-256
  `8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6` before this slice, `1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2` after normal regeneration, reproduced after deleting the file and
  rebuilding). Presented as an uncommitted, unstaged, unpushed diff for Codex's GO/NO-GO; no publication is claimed. Increment 4 remains open.
- Changed files: 36 tracked files modified (the planner record, this record, the protocol, workflow model, cockpit specification, decision index, engineering context and roadmap plan, the two Api diagnosis endpoints and their request/response types, the
  correction command and handler, the diagnosis status query result and handler, the generated client, the diagnosis action, run cockpit and correction hook, and the Application, Api, Infrastructure and browser tests and fixtures that follow
  them) and 8 untracked (ADR-0019, the command validator, three Application test classes, the endpoint test class, the guidance component test and the journey guidance helper); `git status --short` is authoritative.
- What it does: an owner can submit optional advisory direct human guidance with the one explicit diagnosis correction request. [ADR-0019](../decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md) narrowly
  extends ADR-0015's request scope and ADR-0018's correction request (the accepted bodies are unchanged; the decision index and engineering context state the relationships). It grants no extra correction, authorization, permission, source or
  budget authority, and it proves what the host sealed into the correction's context, never that a provider followed it.
  - Backend: `CreateDiagnosisCorrectionAttemptCommand` gains optional `Guidance`; a new validator (the one shared ADR-0015 normalization, 600-code-unit bound, content screen and fixed non-echoing `400
    agent_attempts.direct_guidance_invalid`) runs before the handler, which repeats the check before any read, Git work or sealing; the endpoint accepts `{ verificationDiagnosisAttemptId, guidance? }` under an 8 KiB request-body limit that is newly added to this endpoint (it had none), matching the other guided requests.
    The normalized text is assigned to the existing immutable `AgentDirectHumanGuidance` snapshot in the same single claim commit as the attempt, its ordered inputs (report then findings), manifest metadata and budget reservation, and is
    sealed once into the existing envelope after the diagnosis source notice and before the untrusted-evidence boundary; an unguided manifest, the source/applicability/budget/lease/checkpoint gates and the 32 KiB ceiling are unchanged
    (guidance is never truncated). A valid guided request at shared-allowance exhaustion is refused whole with the existing `409 agent_attempts.direct_guidance_unavailable` after the existing authority gates, before anything is sealed and again at
    the locked claim seam, where the unused seal is removed; an unguided request keeps its idempotent escalation (also when the allowance is spent between the early check and the lock). The review-correction eligibility feed, the fresh dispatch
    snapshot guard and the Claude adapter's sealed-manifest agreement are source-agnostic and unchanged. The diagnosis status adds `correctionDirectGuidance` (the existing fact/response types): null without a correction, otherwise that
    correction's own `NotRecorded`, `Provided` or `Unknown`; the attempt evidence, cockpit latest-attempt and status agree. No migration, dependency, CLI flag, provider schema or permission change.
  - Frontend: `useRequestDiagnosisCorrection.request(runId, diagnosisId, guidance?)` (fixed copy for the two guidance refusals via the existing mapper); `VerificationDiagnosisAction` offers the existing `DirectGuidanceEditor` beside the unguided
    correction button only while a correction is applicable, not running or blocked, and within the allowance (disabled while the status is loading, withheld while the status read has failed, which discards an unsent draft with it), keeps
    "Record human escalation" unguided and explains that guidance is available only within the shared allowance, and shows the recorded fact as supplied context. Request state and drafts belong to the run plus the diagnosis source and the
    committed lifetime (edit versions, newer-flow protection, A→B→A, unmount, retained callbacks and overlapping requests as for the other guided requests). The explicit Refresh evidence connection and its pending/failure guards are unchanged.
  - Journey: the rendered collaboration journey submits harmless guidance through the new form; it asserts the normalized text on exactly one attempt row (read-only), that the native Claude double received the exact sealed value inside the host's fixed boundary
    (the double logs only the guidance's SHA-256 and whether the fixed boundary framed it, using its own copy of the boundary, and refuses any malformed direct-guidance shape before any edit), that the text never reached its log, and the unchanged seven Agent claims,
    Failed then Passed verification, refused review on Failed, checkpoint counts and the sole final persistence reload. This proves local agreement with the doubles, not real-provider reliability.
- Evidence written first, red then green: the Application claim tests were run against a handler that ignored guidance (13 of 20 failed; the 7 controls for validator, null compatibility, ordinary correction and source/budget gates passed), then green; the frontend
  guidance tests failed 16 of 19 on the baseline action and hook, then passed. After the change three deliberate mutations each fail exactly the tests that pin them: removing the seam refusal (1), removing the early refusal (3), and not recording the
  snapshot (1). Projection tests fail 9 of 11 when the mapping is nulled. The generated-client request/response over real HTTP, the dispatch and sealed-manifest protection and the restart replay are covered below.
- Tests added (real file-backed SQLite for every backend claim, dispatch and projection test): `CreateDiagnosisCorrectionAttemptDirectGuidanceTests` (20: exact normalized persistence and ordered inputs, manifest placement and nothing else changed,
  null/unguided compatibility, 7 invalid texts with no reader or seal call and no echo, validator, source and run-budget gates first, competing-correction race, exhaustion before sealing, idempotent unguided escalation and guided refusals around it,
  seam exhaustion with orphan cleanup, ordinary correction unaffected); `DiagnosisCorrectionDirectGuidanceDispatchTests` (20: feed projection, matching and mismatched expectations, altered-after-feed snapshot, malformed text and incoherent
  provenance never eligible or dispatchable and a healthy sibling unharmed, evidence drift still refused); `DiagnosisCorrectionDirectGuidanceProjectionTests` (11: null/Provided/NotRecorded/Unknown, status and attempt-evidence agreement, no
  fact on a diagnosis); `RequestDiagnosisCorrectionDirectGuidanceEndpointTests` (12: protected, response shape, no echo, projections, null/omitted, invalid and over-long, oversized body, foreign diagnosis, exhaustion); two hosted tests through the real
  supervisor and real Claude adapter (exact sealed text reaches stdin once, and restart replay of the sealed bytes); 7 adapter cases for a diagnosis-origin manifest; 9 fixture-contract cases for the guidance logging and malformed shapes; the
  generated-client wire spec over real authenticated HTTP (request serialization, refusals, revival of the three states in the diagnosis status, attempt evidence and cockpit); and frontend: `DiagnosisCorrectionGuidance.test.tsx` (19), two hook
  cases, two composition cases (draft and subtree preserved across Refresh evidence with the refresh issuing no request; the editor withheld while the refresh has failed and offered again afterwards) and two journey-helper cases.
- Checks (Windows; sequential). The build, backend, frontend and browser results in this bullet were produced on the implementation and are RETAINED unchanged by the later documentation-only correction below, which touches no code,
  generated client or test: normal `dotnet build DevalCopilot.slnx` 0 warnings/0 errors; Domain 952/952; Application 3526/3526 (3475 + 51); Infrastructure 963 passed + 3 existing skips of 966 (+7); Api 832/832 (+23); Architecture 9/9;
  frontend `npx vitest run` 126 files, 1707/1707 (1684 + 23); `npm run typecheck` clean; `npm run lint` 9 warnings (unchanged), 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 40/40 (38 + 2); `npm run test:e2e:all`: the existing
  Chromium suite 9/9 (8 + the new guidance wire spec) then the guided journey 1/1; `dotnet format --verify-no-changes` 153 findings, identical to the recorded baseline and none in a changed file; `dotnet list package --vulnerable --include-transitive` none;
  `git diff --check` clean apart from Git's CRLF notices; no NUL and no trailing whitespace in any changed or new file; local Markdown links (file and anchor) of the 7 changed documents resolve; the planner record's SHA-256 is unchanged (these hygiene, link and Git results were rerun after the documentation correction; see below). The generated-client delta is the three new `guidance` members of the request class, the `correctionDirectGuidance` member of the diagnosis status response (field, revival and
  serialization) and its interface, plus NSwag moving the existing `DirectHumanGuidanceResponse` class earlier in the file (a pure relocation).
- Documentation correction R1 (documentation only, no production code, test, client or draft-behavior change): `docs/product/run-cockpit-specification.md` gains "Optional direct guidance with a diagnosis correction" (and a pointer from the
  diagnosis section) describing the exact guided form and where it is offered, ownership by run plus diagnosis source, the disabled submission while a diagnosis read (including Refresh evidence) is pending, the failed read removing the form and its unsent
  draft with the recovery to an empty draft, `correctionDirectGuidance` and its existing fact semantics, and guidance being available only within the shared allowance with the unguided escalation as the sole exhaustion action and no extra
  authorization; the other controls' contracts are untouched. This record also now says the 8 KiB request-body limit is newly added to the correction endpoint (it had none), matching the other guided requests, and counts 36 tracked modified files
  and 8 untracked. Checks rerun for this correction: documentation links (file and anchor), whitespace/NUL hygiene, Git state, and the unchanged planner record (SHA-256 `e7f67f2cee0e341f87aed7ea1ecc29fff52671fef1ac7a1f8932a0e4e34f4306`) and generated client (`1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2`). The backend, frontend and browser suites were
  not rerun and their results above are retained.
- Limits and open risks: the doubles prove reachability and agreement of the sealed context, never real-provider reliability, and the fact states what the host supplied, not that the provider read or followed it. The guidance is intentionally
  visible in the sealed manifest and the projections; the content screen is best-effort and does not guarantee a secret is absent. As with ADR-0015, the snapshot-versus-manifest agreement is checked at the invocation boundary after dispatch has
  been marked. A failed diagnosis-status read removes the editor and so an unsent draft. A guided correction cannot be requested at exhaustion; only the unguided escalation, which grants no authority, remains. The manual `RecordCheckpointReview`
  HTTP 201 against a generated client that accepts only 200 remains a deferred, untouched product mismatch, and the other run status hooks keep their own refresh rules. Not selected or implemented: session persistence/resume/compaction, account
  allowance, new providers, recipe or permission changes, ambiguous-process and no-change recovery, lifecycle completion, scheduling and publication. No next slice is selected.
- Publication (2026-10-03): Codex granted publication GO for the complete reviewed 44-file diff (36 modified tracked, 8 new), including documentation correction R1, Codex's one-sentence correction in the cockpit specification (the form sends the raw
  draft and the server normalizes accepted text) and the final planner record. Preflight matched before staging: `main`; `HEAD`, local `origin/main` and live `refs/heads/main` all `681b7509da28aa91825fe7ca209096234ddf9804`; nothing staged; SHA-256
  `planner-handoff.md` `efaeb50b5b48efd694514c3da562a4596962b94d49eec2ccaeaab09da189e4de`, `current-work.md` `01f1fad672110da32bff0aa9084b9d2279e1ab299d16a90385a2e18ba50c6173`, generated client
  `1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2`. Exactly those 44 files were staged (no probes, databases, build output, secrets or test results; `git diff --cached --check` clean apart from Git's CRLF notices) and committed on that parent as
  `edb664be27cdfbc7b471c8da647d1f05d73d69d0`, then pushed with a normal fast-forward push (no force). After a fetch, `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to that SHA, with a clean checkout. The planner record was
  committed exactly as reviewed and is not edited by this closure.
  - Post-publication checks, run on that commit with normal authentication and the normal host composition, .NET commands sequentially: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; Application
    filtered (`DiagnosisCorrection|CreateDiagnosisCorrectionAttempt|DirectHumanGuidance`) 247/247; Infrastructure filtered (`DirectHumanGuidanceAdapterTests`) 55/55; Api filtered (`VerificationDiagnosisHostedTests|RequestDiagnosisCorrection|ProviderFixtureContractTests`) 91/91;
    Architecture 9/9, and no test skipped in any of these runs. From `src/frontend/DevalCopilot.Frontend`: `npx vitest run` 126 files, 1707/1707; `npm run build` exit 0 (only the existing chunk-size notice); `npm run lint` exit 0 with 9 warnings (the unchanged baseline), 0 errors;
    `npm run test:harness` 40/40; then `npm run test:e2e:all`: the Chromium suite 9/9 and the guided collaboration journey 1/1. The generated client hash was unchanged (`1caa1d42…0ef2`), `git diff --check` clean (CRLF notices only), and the checkout clean after the checks.
    No check failed, was skipped or was rerun.
  - RETAINED, not rerun on the published commit (their code and tests are unchanged since the implementation and the later changes are documentation only): the full Domain 952/952, Application 3526/3526, Infrastructure 963 passed + 3 existing skips of 966 and Api 832/832 suites, `npm run typecheck`,
    `npm audit` 0 vulnerabilities, `dotnet format --verify-no-changes` 153 findings identical to the recorded baseline, and `dotnet list package --vulnerable --include-transitive` none, all recorded above from the pre-publication tree. The earlier failure and flake evidence recorded in the older sections of this file is preserved unchanged.
  - Remaining limitations: unchanged from the limits above. The doubles prove agreement of the sealed context, not real-provider reliability; the content screen is best-effort; the manual `RecordCheckpointReview` HTTP 201 against a client that accepts only 200 remains a deferred product mismatch. This closure does not claim that
    Increment 4 is complete and selects no next slice.

## Browser-driven local collaboration proof (2026-10-02)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `4748b83a618f9be761c2c1f47352364d2f9471c1` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it; nothing staged or untracked; only the
  planner-owned `planner-handoff.md` modified at the start, SHA-256 `c412a46f…475c`, later `0dda395f…6fae` after the planner's continuation decision, finally
  `79e8a86d62ac4c58d944a2f7f3088db217207e4c30892bb6c306c66aa1c70498` after the first NO-GO review, `3f848b28ae89f0fcf37134621073a5cad0c621e559fe1c73088bc74041b19e18` after the second (R6) and `af441ab2d1aee1d874859520c19286e83b360375241cad76ec53786d5559d645` after the third (R6-A/R6-B), byte-identical at the end of the round; generated client SHA-256
  `bd99dc6a31e0f72fc6051730165b1565c33a95f0f41c602425c28720b286994d` until the authorized R1 regeneration, then `8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6`).
  Presented as an uncommitted, unstaged, unpushed diff for Codex's GO/NO-GO (the first presentation received a NO-GO with five corrections R1–R5, the second R6, the third R6-A/R6-B, all recorded below) until the publication GO recorded in the next bullet;
  `planner-handoff.md` was not edited by the executor.
- Publication (2026-10-03): Codex granted publication GO for the exact reviewed 77-file substantive diff (29 modified tracked, 48 new). Preflight matched before staging: `main`, `HEAD`, local `origin/main` and live `refs/heads/main` at
  `4748b83a618f9be761c2c1f47352364d2f9471c1`, nothing staged, planner record SHA-256 `aa6d719444a3aa374b09dfd015015c76aa7fbf2093bdf2e50158b5e19800b993` (the planner GO record, preserved as reviewed), this record
  `94fcc4c1bcdfda8783c37704eb35689250a67e0356f5d98990b520b43455dea9` before this closure, generated client `8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6`. Exactly those 77 files were staged (inventory and `git diff --cached --check` clean
  apart from Git's CRLF notices; no probe, database, build output or test result) and committed as the substantive commit `8c4ea7ace0a077df3dcdc1147938883c920d578e` (parent `4748b83a618f9be761c2c1f47352364d2f9471c1`; 77 files changed, 7077 insertions,
  370 deletions) and pushed to `origin/main` with a normal fast-forward (`4748b83..8c4ea7a`); after `git fetch`, `HEAD`, local `origin/main` and live `refs/heads/main` all equal `8c4ea7ace0a077df3dcdc1147938883c920d578e` with a clean checkout.
  Post-publication checks, run on that commit with normal authentication, every supervisor and the normal host composition: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors; `dotnet test --no-build --no-restore`
  Application `CodexAgentFeedRoutingTests` 12/12; Api `ProviderFixtureContractTests|HostWritableDestinationTests|CodexSupervisorRoutingHostedTests` 65/65; Architecture 9/9 (sequential); from the frontend directory `npx vitest run` 125 files, 1684/1684;
  `npm run build` (including typecheck) clean; `npm run lint` 9 warnings, 0 errors; `npm run test:harness` 38/38 (0 skipped); then, after the harness passed, `npm run test:e2e:all`: the existing Chromium suite 8/8 and the journey 1/1 (the journey's only reload is the final
  persistence reload); the generated-client hash is unchanged, `git diff --check` is clean and the checkout was clean after the checks. Owned roots: each run removed its own `devalcopilot-e2e-*` root; the one remaining `devalcopilot-e2e-*` directory predates this slice
  (created 2026-10-02, an earlier killed run of the existing suite) and was left untouched. The sandboxed review attempt could not start the host because Windows Event Log access was denied; the unchanged command passes outside the sandbox, and no production composition was changed
  to bypass that restriction. RETAINED, not rerun on the published commit (their code, tests and dependencies are unchanged since): the full Domain 952/952, Application 3475/3475, Infrastructure 956 + 3 existing skips and Api 809/809 suites, `dotnet format --verify-no-changes`
  153 findings (the baseline), `dotnet list package --vulnerable --include-transitive` none, and `npm audit` 0 vulnerabilities, all recorded in the correction rounds above. The earlier failure and blocked evidence recorded in this section is preserved, not erased; the manual
  `RecordCheckpointReview` HTTP 201 against a generated client that accepts only 200 remains a deferred product mismatch. This publishes the browser-driven proof only: it is not completion of Increment 4, and no next slice is selected.
- Changed files: 29 tracked files modified and 48 untracked at the end of R6-A/R6-B (R6-A/R6-B added `CodeReviewAction.tsx` and its test, rewrote `useCodeReviewAttemptStatus.ts` and added its ownership test; R6 had 27 and 47: it added `App.tsx` and its test, `RunCockpitView.tsx` and its test, the diagnosis-status, code-review-status, run-scoped-action and two request hooks, plus the new connection hook and its three test files, all frontend; the R1–R5 inventory follows). The R1–R5 inventory was 18 tracked files modified (the planner-owned `planner-handoff.md`, this record, `DevalCopilot.slnx` with the two fixture projects, `package.json` scripts, the
  two eligibility handlers, the verification-claim endpoint and the generated client (R1), the candidate-workspace, source-evidence, verification-commands and checkpoint-review
  components with the git-evidence hook and the two component tests that follow their contracts (R2), and the Api test project file and its lock file) and 43 untracked (the host and
  fixture projects with their lock files and the seven fixture-owned schema copies, the journey configuration, specification and helpers, the cleanup and registration-retry helpers and the
  harness test, and the routing, hosted-routing, fixture-contract, host-destination, client-contract, evidence-refresh and hook-refresh tests); `git status --short` is authoritative (`git diff --name-only` and `git ls-files --others --exclude-standard` count the files).
- What it proves and does not: ONE explicit manual journey through the rendered controls of the production frontend, over the real generated client, MVC
  authentication, mediator, SQLite, every normal supervisor, the real provider adapters, `ChildProcessExecutionAdapter`, Git/worktree evidence and artifact stores.
  Only the external executables are deterministic doubles. It proves the assembled local workflow for this chain, not real-provider reliability, and not completion of
  Increment 4. Approval is shown not to be run-lifecycle completion.
- Delivered test architecture (all under `tests/` and `src/frontend/.../e2e`; no shipped executable, dependency or version added):
  - `tests/DevalCopilot.Api.IntegrationTests/BrowserJourney/Host` (`BrowserJourneyHost`): an executable on `WebApplicationFactory<Program>` with real Kestrel
    (`UseKestrel`) and normal `Program` composition (authentication, CORS, SignalR, migrations, reconciliation, capability discovery, all supervisors). It differs only in
    disposable SQLite, an ephemeral launch secret, and three registrations pointing at one verified disposable root (`WorkspaceRootPathProvider(root)`, one
    `FilesystemArtifactStore(root)` for both artifact ports). It verifies the root against its ownership token, installs the fixture under three names, requires the owned
    `bin` first on its own PATH, and writes a read-only verdict that the actual provider launch targets are the owned doubles before any agent request. `Program` reads the CORS
    origin and listen address while it builds, so those two reach it as process environment values.
  - `.../BrowserJourney/ProviderFixture`: one executable run as `codex.exe`, `claude.exe` and `verify.exe`. It derives every trusted path from its own location (an owned
    root with the harness marker), accepts only the adapters' fixed argv/schema/stdin contracts (anything else exits 64), edits only `src/Feature.cs` in an owned, alias-checked
    worktree (exit 65 otherwise), refuses a plan that is not the revised Proposal (exit 66), and logs only allowlisted facts (role, kind, contract, identities). The
    verification executable passes only when the file holds the corrected statement; there is no call counter.
  - `playwright.journey.config.ts`, `e2e/collaboration.journey.ts`, `e2e/journey/*`: normal authentication, `reuseExistingServer: false`, an in-memory secret, the owned-root
    cleanup also after a failed run, journey-local helpers (it never imports `playwright.config.ts`, so no second root or secret). `npm run test:e2e:all` runs the existing
    eight-spec suite and then the journey, stopping on the first failure.
- Production exception (authorized by the planner after the first stop): the Codex PLANNING eligibility feed (`GetEligibleAgentAttemptsQueryHandler`) filtered only on
  provider, so `AgentAttemptSupervisor` could claim, dispatch and parse as a Proposal any undispatched Codex attempt, including a valid Resolver attempt. It now requires
  `AgentRole.Planner` and `AgentResponseContract.Proposal`; `GetEligibleChallengeResolutionAttemptsQueryHandler` additionally requires
  `AgentResponseContract.ChallengeResolution`. Every other gate, ordering and projection is unchanged; the ordinary-review and diagnosis feeds already distinguished their
  contracts. This is a routing defect, not a malformed provider response. Earlier executor evidence (not rerun here): with the unfixed predicates the resolution step
  failed in 8 of 9 browser runs, and temporary markers showed the planning supervisor processing the Resolver attempt in the runs that were instrumented.
- Routing regressions: `CodexAgentFeedRoutingTests` (real SQLite; 12 cases) prove each of the four Codex feeds selects exactly its own partition (ordinary and format-repair
  attempts of each family stay eligible), the Resolver feed needs its exact contract, and incoherent, null and unknown role/contract values enter no feed and break no healthy
  sibling. `CodexSupervisorRoutingHostedTests` runs the planning and resolution supervisors together with the real feeds, mediator, dispatch and recording commands and a
  counting provider boundary: an observing mediator counts completed planning-feed reads, so the planning supervisor is given a genuine pre-dispatch opportunity (three reads)
  while a valid Resolver is undispatched; it neither consumes nor invokes it, and the resolution supervisor then completes it exactly once. Red/green: 12 of 12 Application
  cases failed against the unfixed predicates and pass with the fix; removing only the planning partition fails the hosted test (planning feed offered the Resolver) and the
  Application cases.
- Journey regressions: `ProviderFixtureContractTests` (17, real child processes started with a cleared environment) cover the closed Codex/Claude/verification contracts and
  unsupported invocations, the extra-flag and weaker-sandbox refusals, ownership and alias refusal (executable outside an owned root, non-worktree working directory, result path
  outside the artifact tree, a directory junction), the Planner-root/no-plan refusals for implementation, diagnosis and review, verification failing without the correction
  edit however often it runs and passing only after it, and the allowlisted log. Mutations that make verification pass regardless, accept the root plan, or ignore reparse points
  each fail exactly one of them. `journeyHarness.test.ts` (node) covers the plan-identity judge (a Planner-root substitution for the revised Proposal is detected in each
  plan-bearing stage), cleanup of the owned root after a failing run (and no deletion of a wrong-token root or prefix-matching sibling), and the disposable repository.
- The journey asserts, in order, with every mutation from a rendered control: registration/selection, physical-identity recheck, real worktree preparation and checkpoint, the
  verification recipe and a ManualAgent objective; Codex plan, Claude challenge, Codex resolution (revised Proposal differing from the root), Claude implementation (defect
  introduced in the real candidate file), explicit verification (real failed process, captured and sealed stderr inspected on the page), a refused ordinary review (HTTP 409 from the
  server's Passed gate, no attempt created), Codex diagnosis (findings inspected), explicit diagnosis-origin correction, fresh verification Passed on the corrected checkpoint, and
  ordinary review approval; then a reload keeps the results attached to the same run. It reads back: attempts and contracts after each step (no automatic advancement), the
  implemented Proposal rather than the Planner root in implementation, diagnosis and review, exact report/finding/revision-response lineage, three distinct checkpoints and
  fingerprints with verification bound to the checkpoint it judged, seven Agent claims within the unchanged 16, 90 minutes reserved of the unchanged 120, one spent shared
  correction slot, no Git commit in the worktree and the original repository's HEAD, status and files unchanged, and the launch secret absent from the page, storage and files.
- NO-GO correction round (2026-10-03), five bounded corrections on the same diff; each regression was written first and shown red against the unfixed code, then green:
  - R1 verification acceptance contract. `ClaimVerificationExecutionEndpoint` now declares `[ProducesResponseType<ClaimVerificationExecutionResponse>(StatusCodes.Status202Accepted)]`; the runtime
    `Accepted`/202, DTOs, authentication and claim behavior are unchanged. The client was regenerated by the normal solution build only, never edited: the exact delta is one operation, 5 lines
    (`status === 200` → `status === 202` and its `result200`/`resultData200` locals), new SHA-256 `8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6`, reproduced after
    deleting the file and rebuilding. Red: `claimVerificationExecutionContract.test.ts` (the generated client given a 202 JSON answer threw "An unexpected server error occurred.") and the panel
    test (the accepted claim reported "is pending."); green after regeneration and the wording change to "Verification #N was requested." (no pending claim outlives a terminal result). A
    refusal still rejects as an API exception. The journey proves success over real authenticated HTTP: the rendered Run observes a 202 and then "Last run: Failed" / "Last run: Passed" without a reload
    and without "This verification could not be started.".
  - R2 explicit current-evidence refresh. `CandidateWorkspacePanel` owns a project-lifetime refresh generation (`useOwnedLifetime`/`useOwnedState`, so another project or a return to an earlier one starts at zero)
    and a rendered "Refresh evidence" button (Ready/NeedsAttention workspace; R6 extends it to the run, below). The generation is passed as a prop to Source evidence, Verification commands and Checkpoint review, which re-read the existing GET through
    `useProjectGitEvidence`; a successful explicit capture reports `onCaptured` so the same consumers refresh. The hook exposes `current` (the read of the present generation succeeded, none pending, no capture in
    flight), and Run and every review decision are disabled, and refuse to submit, while it is false (pending or failed refresh; the previous metadata stays visible). A successful read clears the previous
    evidence-wide error. No remounting, global event bus, cache, polling or automatic stage progression; drafts, the reviewer choice and an inspection of an unchanged checkpoint survive a refresh; an inspection
    requested for a replaced checkpoint is dropped. Red: 9 of 9 `CandidateWorkspaceEvidenceRefresh.test.tsx` cases failed on the old code (no action; capture left Verification on the old checkpoint); green: they
    pass with 4 hook-level cases (`useProjectGitEvidenceRefresh.test.tsx`) covering initial capture, refresh after implementation/correction, pending and failed refresh, overlapping reads, stale inspection and project A→B→A.
    Mutations (Run ignoring `current`; the generation not distinguishing a read) each fail a case.
  - R3 registration retry. The retry decision moved to `e2e/harness/registrationRetry.ts` behind a driver, used by `journeySupport.registerProject` over the rendered form and the observed POST: only HTTP 409
    whose first error is exactly `projects.git_unavailable` is submitted again (the same name and path, the repository built once by the journey), within 30 attempts; an accepted answer is confirmed once and never
    resubmitted however late it renders; every other refusal, transport failure and rendering failure surfaces at once with a fixed message that carries no response, request or page text. Six `journeyHarness.test.ts`
    cases (known refusal then success, unrelated refusals, transport failure, accepted-but-delayed and never-rendered, exhaustion); a mutation that retries everything fails five of them. The old Playwright-bound loop had
    no seam, so its red evidence is the legacy behavior reproduced behind the same driver.
  - R4 fixture schema validation. The doubles read the supplied schema (Codex: the invocation-owned scratch file, read-only and bounded; Claude: the inline argument), refuse anything above 16 KiB, that is not JSON,
    not an object or not structurally equal to the fixture's own copy of the selected role's contract (`ProviderFixture/Schemas/*.schema.json`, embedded; object property order and string-array order ignored; no dependency or generic
    schema engine; the copies were captured from the adapters' schemas once and are not read from the production builders at run time), before any edit, final response or log write, with fixed messages that never echo
    the schema. 12 Codex and 11 Claude refusal cases (empty, whitespace, non-JSON, array, `{}`, oversized, wrong contract, open `additionalProperties`, dropped required/property, widened bound/enum), a missing or directory
    schema file, and acceptance of the actual adapter schema of all seven contracts in original and reversed property order. Tests no longer use `{}` as a success schema. Red: 26 of 55 fixture-contract cases failed on the unfixed
    fixture (after correcting two test-setup flaws in the first red run, which showed 28); green 55/55. A production schema change now fails the fixture contract tests by design.
  - R5 writable-path isolation. Every writable destination is checked for a reparse point on the leaf and on every existing ancestor beneath the owned root, and refused when it is an existing directory, before it is created,
    appended or copied: the invocation log (including the version probes), final-response sinks (leaf and ancestors), the candidate file (already checked on every segment), and, in the host, the layout directories, the installed
    binaries (all destinations before the first copy) and the launch-target verdict (`OwnedRootGuard.RequirePlainDestination`/`WriteFixtureStateFileAsync`, reusing the fixture's `OwnedLocation` alias check). Real-process regressions
    use real directory junctions and assert exit code 65 with the outside sentinel directory unchanged, plus a normal-path success; host regressions run on the real file system (the two small host helper files are compiled into the Api test
    assembly). Red: 8 of 9 host cases and 6 of the new fixture alias cases failed before; green after. Limits: only reparse points are detected (a hard link to an outside file is not), and Windows path semantics are assumed.
  - R6 run-status refresh without a reload (second NO-GO, 2026-10-03). The Verification diagnosis panel and the Code review request read their run-scoped status from the host only when a run event advances or one of their own requests refreshes it,
    and a completed verification is project-scoped and emits no run event, so after a failed verification the diagnosis control was absent until a reload. `Refresh evidence` now also reaches the selected run through one narrow connection:
    `useEvidenceRefreshConnection(projectId, runId)` (in `App`) owns a generation per committed project AND run lifetime (`useOwnedLifetime`/`useOwnedState`; another project or run, A→B→A and unmount start or end it, and a retained callback
    does nothing); `CandidateWorkspacePanel` tells it once per explicit refresh (`onEvidenceRefreshRequested`, not for a capture or a mount); `RunCockpitView` receives the generation (`evidenceRefreshGeneration`) and hands it only to
    `useVerificationDiagnosisStatus` and `useCodeReviewAttemptStatus`, which read their existing GETs again when it advances and report loading in the very render that carries it, and to the two request hooks, whose reported refusals are
    not shown once the evidence was re-read (`useRunScopedAction` epoch; a pending submission, a draft and an accepted operation are untouched). The code-review request is also unavailable while the diagnosis status that names its report is
    loading. A pending refresh keeps the previous status visible with its requests disabled; a failed one drops it with a safe message and offers nothing. No run event or sequence is manufactured, no eligibility is derived from local verification rows,
    nothing is requested automatically, and there is no remount, bus, cache, polling, backend change, new endpoint or dependency; the five other run status hooks keep their signatures.
    Red: `useEvidenceRefreshConnection.test.tsx` (module absent), `runStatusEvidenceRefresh.test.tsx` (4 of 5 failed: no read for a new generation, a loading gap in the first frame, no safe failure) and `RunEvidenceRefreshComposition.test.tsx` (9 cases over the real
    `CandidateWorkspacePanel` + `RunCockpitView` + connection: 7 of 9 failed, the other two being ownership cases that hold by construction), `App.test.tsx` and the panel notification case; green after the change: failed verification → Refresh evidence → the
    diagnosis is offered from the fresh status (reads only: no diagnosis, correction, review, claim or capture request); passed verification → Refresh evidence → the obsolete refusal goes and the review request is offered but never made; pending and
    failed refresh; overlapping reads; same-project run replacement, A→B→A, stale callbacks and unmount; drafts, the mounted subtree and an accepted pending request preserved. Mutations (refusal not expiring, review loading not derived from the generation,
    ownership keyed by project only) each fail a case. At journey level, pointing `App` at a constant generation makes the journey fail at the diagnosis control after a 30 s wait. The journey now clicks Refresh evidence after each terminal verification and
    asserts the run's current display before the next explicit request; the diagnosis reload is gone and only the final persistence reload remains. Seven explicit Agent claims, Failed then Passed verification, the refused review on Failed, checkpoint counts (2 then 3)
    and all supervisors are unchanged. `RecordCheckpointReview` (201 against a client that accepts only 200) is deferred and untouched.
  - R6-A partial refresh failures (third review, 2026-10-03). The first R6 failure test rejected both reads and left the review-correction fallback empty, which hid two cases: (1) diagnosis succeeds and only the code-review read fails: the safe error
    showed but Request code review stayed enabled; (2) only the diagnosis read fails, the review read succeeds and `reviewCorrectionAttemptStatus` names a reviewable report: the null diagnosis result revived that fallback report and enabled the request.
    Now the ordinary-review target is `null` whenever the diagnosis read failed (the review-correction report is the fallback only when the diagnosis status was actually read and named none, so the legitimate historical targeting is kept),
    `CodeReviewAction` withholds the request while its status read has failed (a failed read is not "no review yet"), and the request handler itself refuses unless both reads are settled and neither failed. Safe messages remain and a successful explicit
    refresh recovers. Server authority and the fallback are otherwise unchanged; no eligibility is derived from local verification rows. Red on the submitted tree: 3 of the 4 new composition cases failed (code-review read alone; diagnosis read alone with a
    populated fallback; both reads with a populated fallback; the fourth, the legitimate fallback still targeting `fallback-report`, passes as a control), with refresh itself asserted to send no diagnosis, correction, review, claim or capture request in each;
    green after. Removing the three guards restores exactly those three failures. A `CodeReviewAction` case pins the withheld request.
  - R6-B run-lifetime ownership of `useCodeReviewAttemptStatus` (third review). Its `refresh` was bound to nothing: read A, retain refresh, switch to B and settle, call it, and the reads were [A, B, B] (and [A, B, A, A] across A→B→A). The hook was rewritten on the
    existing `useOwnedLifetime`/`useOwnedState` pattern already used by `useVerificationDiagnosisStatus` (same public signature and result): status, error, the refresh count and the pending read belong to the committed run lifetime, the read key
    (event sequence, refresh count, evidence generation) derives loading in the very render that carries it, `refresh` retained from a replaced or unmounted lifetime starts no request, and only the newest read of the current lifetime commits. Failed reads
    still mask the status with the same safe message. The other status hooks were not migrated. Red on the submitted tree: the two retained-callback cases failed with [A, B, B] and [A, B, A, A]; 7 cases in `useCodeReviewAttemptStatusOwnership.test.tsx`
    now pin current refresh, old callbacks inert after replacement, A→B→A and unmount, late and overlapping answers, first-frame loading per evidence generation, and no run. The 14 earlier hook tests pass unchanged. A side effect: the rewrite removes the
    hook's `react(set-state-in-effect)` lint warning, so lint reports 9 warnings instead of 10.
- Findings returned to the planner, not changed (no other production change is authorized): (1) R1/R2 resolve the two earlier UI observations (the accepted claim shown as a failure; Source evidence stale after a mutation); R6 resolves the run-scoped
  diagnosis/review staleness after a completed verification that the first round had to leave as one reload. (2) The same defect class exists for the manual `RecordCheckpointReview` operation (`Created`/201 against a generated client that accepts only 200); it is
  unused by the journey and untouched (deferred by the planner). (3) The other run-scoped status hooks (planning, critical review, resolution, implementation, review correction) keep their own refresh and ownership rules and are not driven by Refresh evidence; only the code-review and diagnosis hooks follow the run lifetime pattern. (4) Fixture-environment
  causes found and handled in the fixture, not in product code: a machine-wide `core.autocrlf` makes the host's cleared-environment Git report CRLF worktree files as changed (the journey repository sets it false locally), and the host must receive the CORS origin
  and listen address as environment values.
- Checks, first presentation (before the NO-GO; retained, superseded by the final-tree results below): Domain 952, Application 3475, Infrastructure 956 + 3 skips, Api 762, Architecture 9, vitest 1635, harness 32, existing Chromium suite 8/8 then the
  journey 1/1, with the client byte-identical to the old hash; the failed and blocked evidence above is retained, not rerun.
- Checks of the R1–R5 round (superseded by the R6 final-tree frontend results below where they overlap; the backend results are RETAINED because no backend, backend-test or dependency file changed after them, verified by file timestamps) (Windows; sequential, `--no-build` after one normal `dotnet build DevalCopilot.slnx`, 0 warnings/0 errors, which regenerated `api-client.ts` to the new hash, reproduced after deleting the file):
  Domain 952/952; Application 3475/3475; Infrastructure 956 passed + 3 existing skips of 959; Api 809/809 (762 earlier + 47 net new: the fixture-contract suite grew to 55 cases and 9 host-destination cases were added); Architecture 9/9; frontend `npx vitest run` 121 files,
  1651/1651; `npm run typecheck` clean; `npm run lint` 10 warnings (the baseline), 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 38/38 (0 skipped); `npm run test:e2e:all`: the existing Chromium suite 8/8 then the
  journey 1/1 (at that time the journey kept one intermediate reload before the diagnosis step, since removed by R6); `dotnet format --verify-no-changes` 153 findings, identical to the recorded baseline, none in a
  changed file; `dotnet list package --vulnerable --include-transitive` none; `git diff --check` clean apart from Git's CRLF notices; no NUL and no trailing whitespace in any new file; the planner record's SHA-256 is unchanged. Earlier focused red/green runs: see each
  correction above. A passing run does not prove reliability.
- Checks of the first R6 round (superseded by the R6-A/R6-B final-tree frontend results below where they overlap; RETAINED backend results are unchanged) (Windows; sequential): normal `dotnet build DevalCopilot.slnx` 0 warnings/0 errors with `api-client.ts` at the verified `8234339f…771bd6` (unchanged); affected frontend checks first (the new connection, status-hook,
  composition, App and panel suites); then full frontend `npx vitest run` 124 files, 1673/1673 (1651 + 22 new); `npm run typecheck` clean; `npm run lint` 10 warnings (baseline), 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 38/38;
  `npm run test:e2e:all`: the existing Chromium suite 8/8, then the journey 1/1 with no intermediate reload (the final persistence reload only); `git diff --check` clean apart from Git's CRLF notices; no NUL and no trailing whitespace in any new file; the planner record
  (`3f848b28…19e18`) and the generated client are byte-identical to the preflight. RETAINED from the R1–R5 round, not rerun, because no backend, backend-test, project-file or dependency file changed since (find by timestamp against the suite log: none): Domain 952/952,
  Application 3475/3475, Infrastructure 956 + 3 existing skips, Api 809/809, Architecture 9/9, `dotnet format --verify-no-changes` 153 findings (the baseline), `dotnet list package --vulnerable --include-transitive` none. A passing run does not prove reliability.
- Checks actually run on the final combined tree after R6-A/R6-B (Windows; sequential): normal `dotnet build DevalCopilot.slnx` 0 warnings/0 errors with `api-client.ts` at the verified `8234339f…771bd6` (unchanged); affected checks first (the composition, code-review
  ownership, status-hook, connection and `CodeReviewAction` suites, then all cockpit and App tests); full frontend `npx vitest run` 125 files, 1684/1684 (1673 + 11 new: 7 ownership, 3 net composition, 1 `CodeReviewAction`); `npm run typecheck` clean; `npm run lint`
  9 warnings (one fewer than the baseline 10, see R6-B), 0 errors; `npm run build` clean; `npm audit` 0 vulnerabilities; `npm run test:harness` 38/38; `npm run test:e2e:all`: the existing Chromium suite 8/8, then the journey 1/1 with the final persistence reload as the
  only reload; `git diff --check` clean apart from Git's CRLF notices; no trailing whitespace in any new file; the planner record (`af441ab2…d645`) and the generated client are byte-identical to the preflight. RETAINED from the R1–R5 round, not rerun, because no backend,
  backend-test, project-file or dependency file changed since (find by timestamp against that suite log: none): Domain 952/952, Application 3475/3475, Infrastructure 956 + 3 existing skips, Api 809/809, Architecture 9/9, `dotnet format --verify-no-changes` 153 findings
  (the baseline), `dotnet list package --vulnerable --include-transitive` none. A passing run does not prove reliability.
- Limits and open risks: doubles prove reachability and agreement with the adapters' contracts, never real-provider reliability, authentication, or session behavior; the version,
  model, session and usage values they emit are fixture reports. The journey is one chain, not a permutation matrix. The 201 of the manual review record (finding (2)) remains a deferred product question. Refresh evidence is explicit: nothing refreshes the run's diagnosis or review status automatically when a verification completes, and the other run-scoped status hooks are not driven by it (finding (3)). A
  stale `devalcopilot-e2e-*` root from an earlier, killed run of the existing suite was already present in the temp directory and was left untouched. Not selected or implemented:
  session persistence/resume/compaction, account allowance, new providers, recipe or permission changes, ambiguous-process and no-change recovery, lifecycle completion, scheduling,
  publication. No next slice is selected.

## Local verification failure diagnosis and bounded correction (2026-10-02)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `dc705798f427dcae47cf30fd582a5d680bf55c89` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it, nothing staged or
  untracked, only the planner-owned `planner-handoff.md` modified; generated client baseline SHA-256
  `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`). Presented as an uncommitted, unstaged, unpushed diff for Codex's GO/NO-GO, after one NO-GO correction round (R1-R5, below).
  `planner-handoff.md` was not edited by the executor (content and CRLF endings preserved). The initial review made no publication claim; subsequent publication and corrective work are recorded below.
- Delivered behavior ([ADR-0018](../decisions/0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md), a narrow extension of
  ADR-0010's eligible finding source; ADR-0017's distinction between the implemented plan and the Planner root is kept):
  - A closed `VerificationDiagnosis` contract (CodeReviewer, ReadOnly, currently Codex, adapter `codex-verification-diagnosis-v1`) whose only
    valid answers are one to ten `ReviewFinding`s or exactly one bounded `Escalation`, each replying to the exact `ExecutionReport`; it never
    approves and records no `CheckpointReview`. Enum members were appended (`AgentResponseContract.VerificationDiagnosis`, outcomes
    `DiagnosisFindingsRecorded`, `DiagnosisEscalated`, `InputAlreadyDiagnosed`, `VerificationEvidenceChanged`). It reuses `CodexProcessInvoker`;
    no CLI permission, tool, flag, authentication or session behavior changed.
  - `POST /api/runs/{runId}/agent-attempts/verification-diagnosis` (run + report) derives the complete latest-execution selection of every enabled
    command (coherent Passed/Exited/0 or Failed/Exited/nonzero, at least one failure) and persists ordered `AttemptVerificationEvidence`. The sealed manifest
    carries the implemented plan, report, bounded Git evidence, verification metadata and verified redacted failure excerpts (`IArtifactStore`
    verification, at most 2 KiB per stream and 12 KiB in total, truncation/shortening/empty/budget omission labeled separately; invalid UTF-8 is
    still capped per stream), with fixed instructions before the untrusted boundary and no executable path, argument, storage path or hash.
  - Each diagnosis membership row carries a nullable, versioned snapshot SHA-256 (command, execution, and both failed-output row facts; ordinary review rows keep null; a diagnosis row without a valid digest fails closed) that is compared at the claim seam, dispatch, result recording, and correction authority. Authority is re-read untracked in the claim transaction, at dispatch and before result recording; drift ends as `VerificationEvidenceChanged`
    (retaining process and artifact evidence, recording no finding or escalation). One successful diagnosis per exact identity; failed invocations may be explicitly retried within the existing budgets, with no automatic retry or diagnosis format repair.
  - `POST .../verification-diagnosis/correction` (diagnosis attempt, canonical `CreateDiagnosisCorrectionAttemptCommandResult` with the same HTTP variants) uses the existing Implementer/ReviewCorrection contract and Claude adapter with
    inputs `[report, every finding in timeline order]` and a fixed source notice. It shares the run-wide budgets and the one ReviewCorrection
    allowance; the claim and the exhaustion escalation each re-read lifecycle, workspace, lease, checkpoint, the exact diagnosis/findings/snapshot, the run-wide gates and the allowance untracked inside one short write-locked transaction after the Git/artifact work. At exhaustion one idempotent Orchestrator escalation (`diagnosis_correction_escalations`, unique per diagnosis, migration
    `AddDiagnosisCorrectionEscalation`) is recorded with no attempt and no grant, and ordinary grants cannot authorize it. Its text names manual review and an
    explicit human decision and offers no supported continuation. The ordinary correction endpoint and the Passed approval gate are unchanged.
  - `GET .../verification-diagnosis` status, a `VerificationDiagnosisSupervisor`, and cockpit diagnosis/findings/escalation/correction controls with
    fixed English copy (`provider_not_observed` means the provider runtime is not currently observed as available, distinct from a report without
    provider provenance; the three real token-stop codes have fixed copy; other problems fall back to the server's detail, then a generic sentence).
- Correction round (Codex NO-GO R1-R5), all within this slice:
  - R1 atomic correction authority: `CreateDiagnosisCorrectionAttemptCommandHandler` now opens a real transaction (new `IAttemptDurabilityProbe` dependency) after the Git/artifact
    work, takes the write lock (guarded no-op writes of execution mode and token-stop policy), reads the Claude model/effort/turn-limit guards, and re-reads lifecycle, workspace,
    lease, current checkpoint, run-wide gates, the diagnosis with its findings and verification snapshot, the allowance, the Claude runtime and competing corrections untracked
    inside it before inserting, saving and committing; the exhaustion escalation does the same in its own transaction. Cancellation, commit-ambiguity (probe / escalation
    re-read), idempotency and orphan-manifest cleanup are preserved. The permissive "claim may succeed, dispatch then refuses" test was removed (a competing writer can no longer commit
    inside the save); replaced by populated-tracker seam tests (8 drifts x claim and x exhausted escalation, a competing claim, a committed claim, and the dispatch refusal for drift
    after a completed claim).
  - R2 process evidence: `RecordVerificationDiagnosisResult` validates the requested semantic outcome's clean-exit proof before any drift reclassification (missing, nonzero,
    timed-out, cancelled evidence is refused without mutation for findings and escalation even with simultaneous verification and Git drift; a valid clean exit is still downgraded).
  - R3 status lifetime: `useVerificationDiagnosisStatus` now uses `useOwnedLifetime`/`useOwnedState` with a lifetime-bound refresh and ordered reads (8 new tests; 3 red against the
    previous hook: A-B-A resurrection, previous-run error in a frame, retained refresh issuing a request).
  - R4 snapshot integrity: `AttemptVerificationEvidence.SnapshotSha256` (nullable, <=64) with `RecordDiagnosisSnapshot`; `VerificationDiagnosisSnapshot` (version 1 canonical
    text, never sent to a provider); compared in `Selection.SameAs` (claim seam) and `VerificationDiagnosisApplicability` (dispatch, recording, correction authority and status).
    The unpublished migration `AddDiagnosisCorrectionEscalation` was regenerated (new timestamp, LF) to add the column; ADR-0018 and the protocol document were updated. The
    Chromium raw-SQL fixture reproduces the digest. 38 new tests change 11 facts (output hash/length/path/truncation/capture, execution exit code/workspace/timeout, command
    name/timeout) with identical identifiers and require refusal at the seam, dispatch, applicability and correction authority, plus 3 fail-closed digest cases.
  - R5 ownership: the shared diagnosis policies/schema/evidence (`Applicability`, `Eligibility`, `Evidence`, `InputIdentity`, `OutputSchema`, new `Snapshot`) moved to
    `Features/Runs/Policies/VerificationDiagnosis`; the operation-internal `ReportValidation` and `FailureExcerpts` moved into `CreateVerificationDiagnosisAttempt`; matching
    namespaces; `CreateDiagnosisCorrectionAttemptCommandResult` is the operation's own result. Older feature-root types were not moved. `InternalsVisibleTo` for
    `DevalCopilot.Api.IntegrationTests` was added so the API seed can compute the same digest.
  - Red/green evidence: R2 8/8 red with the guard disabled, green restored; R4 27/38 red with the digest comparison disabled, green restored; R1 10/19 red with the in-transaction
    applicability disabled, green restored; R3 3 red against the old hook.
- Changed files: 32 tracked files modified (including the planner-owned `planner-handoff.md` and this record) and 93 untracked, 125 in all (`git status --short`); highlights are
  Domain `Attempt`/contract/outcome/escalation/policy/`AttemptVerificationEvidence`, Application `Features/Runs/Policies/VerificationDiagnosis/*`, `CreateVerificationDiagnosisAttempt`
  (with its report validation and excerpts), `CreateDiagnosisCorrectionAttempt`, `RecordVerificationDiagnosisResult`, dispatch refusal, two query folders and the dispatch/eligibility extensions,
  Infrastructure adapter/configuration/regenerated migration/snapshot, three Api endpoint folders and the supervisor, the regenerated `api-client.ts`
  (SHA-256 `bd99dc6a31e0f72fc6051730165b1565c33a95f0f41c602425c28720b286994d`, unchanged by the correction round), frontend hooks/components/failure map/tests, two Chromium specs and a fixture,
  ADR-0018, and the protocol/workflow/cockpit/engineering-context documents.
- Checks actually run against the final corrected tree (normal solution build, then sequential `--no-build` suites; `--no-incremental -m:1` rebuild reproduced the client byte-identical):
  Domain 952/952, Application 3463/3463, Infrastructure 956 passed + 3 skipped (existing skips) of 959, Api 744/744, Architecture 9/9; frontend `npx vitest run` 118 files, 1635/1635,
  `tsc -b` clean, `npm run build` clean, oxlint 10 warnings (the baseline), 0 errors, `npm run test:harness` 19/19, `npm audit` 0 vulnerabilities; full Chromium 8/8 on two consecutive
  runs (one earlier run of the same tree failed `project-selection.spec.ts` once; it passed alone and in both full reruns, and I did not isolate the cause, so treat it as an unexplained flake);
  `dotnet list package --vulnerable --include-transitive` none; `dotnet format --verify-no-changes` 153 findings, identical to the recorded baseline and none in a file this slice created or
  changed; `git diff --check` clean (only Git's CRLF notices); a node check of every untracked file found no NUL, carriage return or trailing whitespace; local Markdown links of the changed
  documents (175 in 8 files) resolve; `planner-handoff.md` SHA-256 `f325ee3d7acae00e5c9da5e3d1cf9feb28cea654ea15eb0cffd7d60a801274e1` unchanged.
- Proof (earlier, retained): discriminating mutations by the test author (oldest-instead-of-latest execution, tracked report read, revalidation skipped, dispatch drift check removed,
  record-time applicability removed, allowance ignored, finding-source alternative removed), each red then restored. A hosted process-double journey (production-written planning,
  implementation, real failed verification with sealed output, diagnosis, correction, new Passed verification, ordinary approval) also covers a revised plan, diagnosing a corrected
  report, shared-allowance escalation, escalation, retry after an invalid answer, drift before dispatch and during the provider call, and restart replay. Endpoint tests and the wire spec
  use raw-SQL fixtures and are labeled as such; they are not production-written evidence.
- Publication and post-publication status: the substantive slice was published as `0122ebac7e88771e074231fbcf906d0cfd683245` (parent
  `dc705798f427dcae47cf30fd582a5d680bf55c89`; `HEAD`, local `origin/main` and live `refs/heads/main` verified equal, clean checkout). Post-publication checks on that commit:
  solution build 0 warnings/0 errors; the filtered .NET suites (Domain 66/66, Application 549/549, Infrastructure 13/13, Api 26/26, each a filter, not a full suite) and the full
  Architecture suite 9/9 passed; frontend vitest 118 files 1635/1635, build, lint (10 warnings, the baseline, 0 errors) and harness 19/19 passed; **the full Chromium run failed**
  (7 passed, 1 failed): `project-selection.spec.ts:83` with `apiResponse.body: Response has been disposed`. That spec was not modified by the slice; it had also failed once in an earlier
  full run of the pre-publication tree (2 of 5 full executor runs reported) and passed alone and in other full runs. Closure was therefore held until the fixture correction below was published and re-verified.
- Post-publication fixture correction (accepted by Codex and published as `cf25429ddfd6c974b691297b070511cd5789063c`, parent `0122ebac7e88771e074231fbcf906d0cfd683245`; test support only): it addresses a demonstrated lifecycle gap: the spec's `CockpitGate` can leave route handlers (fetch/body work and held
  answers) alive when Playwright disposed the context. The gate now lives in `e2e/harness/cockpitGate.ts` with an explicit shutdown lifecycle — stop new holds, release existing holds,
  `page.unrouteAll({ behavior: 'wait' })`, await the handlers it started — provided to the spec as a fixture that depends on `page` so teardown runs before the page/context fixtures close,
  also when an assertion fails. A hold is registered synchronously after a `closing` check, so a handler finishing its fetch/body after shutdown began never waits forever. Fetch/body
  failures are recorded and rethrown by shutdown (no blanket catch, `ignoreErrors`, retry or sleep added); the pre-existing narrow catch around `route.fulfill` is unchanged.
  Six deterministic harness regressions (`e2e/harness/cockpitGate.test.ts`, added to `npm run test:harness`, fake page/context with explicit ordering) cover releasing held work, shutdown
  during fetch/body with a would-be late hold, the disposed-response failure when the context is closed first versus not when shutdown comes first, cleanup after a failing test body, a
  genuine forwarding failure staying observable, and the injected failure mode. Red evidence: removing the closing check fails the late-hold test (timeout), removing the release fails
  two tests (timeouts), and removing the shutdown from the fixture finally-path fails the cleanup test; all restored. The mechanism is established with Codex's independent real-HTTP
  reproduction and the controlled-order fake; the exact event order of the historical failure was not reconstructed.
- Correction runs before publication (this tree only; backend suites were not repeated for a test-support-only change, so the backend evidence above is retained from the substantive publication): `npm run test:harness` 25/25; `npx vitest run` 118 files 1635/1635; `npm run typecheck`
  clean; `npm run lint` first reported one `react-hooks/rules-of-hooks` error for a parameter named `use` (renamed `provide`), then 10 warnings (baseline) and 0 errors; `npm run build`
  clean; focused `project-selection.spec.ts` 1/1; harness 25/25 again, then one full Chromium run 8/8 (normal authentication, `reuseExistingServer: false`). One green full run does not
  prove the earlier intermittent failure is gone. Generated-client SHA-256 unchanged (`bd99dc6a…994d`); `planner-handoff.md` untouched by the executor; no production, backend, provider,
  generated-client, dependency or permission change.
- Corrective publication and post-publication checks: `HEAD`, local `origin/main` and live `refs/heads/main` were verified equal to the corrective commit `cf25429ddfd6c974b691297b070511cd5789063c` with a clean
  checkout (the six reviewed files only). Run against that published commit from `src/frontend/DevalCopilot.Frontend`: `npm run test:harness` 25/25 (0 skipped); `npm run typecheck` clean;
  `npm run lint` 10 warnings (the baseline) and 0 errors; `npm run build` clean (only the existing chunk-size notice); then one full Chromium run, `npx playwright test --reporter=line`, 8/8 with normal
  authentication and `reuseExistingServer: false` (the original failed run is preserved above). Generated-client SHA-256 unchanged (`bd99dc6a…994d`); `git diff --check` clean apart from Git's CRLF
  notices. The full vitest run (118 files, 1635/1635) and the backend suites are retained evidence from the earlier runs, not repeated for this test-support-only commit. Cleanup residue: only
  ignored run-owned artifacts (`playwright-smoke.db*`, `test-results/`, build output); nothing tracked or untracked remained.
- Limits and open risks: process doubles prove reachability and sealed-form agreement, never real-provider reliability; no real provider was invoked. Redaction of excerpts is best
  effort. The snapshot digest proves the stored facts are unchanged; it does not prove the sealed output files on disk (those are verified by `IArtifactStore` at claim). A diagnosis
  membership created by a raw-SQL writer without the digest is refused (fail closed) by design. The correction escalation's commit-ambiguity classification re-reads the escalation
  row on the same connection and reports `attempts.persistence_unresolved` if that read also fails. The intermittent `project-selection.spec.ts` Chromium failure above, whose fixture correction is published and was re-verified by one green full run (which cannot prove an intermittent failure is gone); the teardown mechanism was reproduced, but the historical event order was not reconstructed. Explicitly out of scope and unchanged:
  session persistence/resume/compaction, allowance thresholds, new providers, Gemini/fallback, permission changes, recipe mutation, ambiguous-process and no-change recovery, lifecycle
  completion, scheduling, publication. No next slice is selected.

## Atomic project/run switching for cockpit and workspace evidence (2026-10-02)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `fbadc4e6b25e5015988b368f456b7a817ee06dd3` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it, nothing staged or
  untracked, only the planner-owned `planner-handoff.md` modified at the start; the generated client SHA-256 matched
  `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`). Presented as an uncommitted, unstaged, unpushed diff for Codex's
  GO/NO-GO (after one NO-GO correction round, R1-R3, below); `planner-handoff.md` was not edited by the executor (its content and CRLF line endings were preserved). Frontend only: no backend production or test,
  HTTP contract, generated client, schema, migration, dependency, provider or workflow change.
- Delivered behavior: selecting another project or run now shows, in the very frame that commits the selection, only that selection's own loading
  or error state. A small feature-owned utility (`useOwnedLifetime`: an `OwnedLifetime` created per committed owner key, ended in a layout-effect
  cleanup, so a return to an earlier key is a new lifetime, plus `useOwnedState`, which exposes a state frame only to the lifetime that wrote it and
  lets a continuation write only while its lifetime is active) now backs the six scoped hooks.
  - `useRunCockpit`: snapshot, event cards, connection, loading, error and sync error belong to the selection lifetime (A to B to A, `null`,
    unmount). A cockpit answer naming another run is refused with a fixed error and never becomes the selection's cockpit. The per-run catch-up state,
    cursor, coalescing, post-connect/reconnect catch-up and refresh waiters are unchanged; `refresh` is now bound to its lifetime, so a retained
    callback of the previous run resolves false and starts no request. Because the cockpit and cards are no longer inherited, the downstream hooks
    never receive the previous run's `latestSequence` or process attempt id.
  - `useProjectWorkspace`, `useProjectGitEvidence`, `useProjectVerificationCommands`, `useProjectVerificationExecutions`,
    `useProjectCheckpointReviews`: metadata, lists, errors and pending flags belong to the project lifetime (nothing is owned for a `null` project, or
    for evidence that is disabled). Overlapping reads are ordered per lifetime, so an older answer never overwrites a newer one, nor clears its pending
    flag or error. Git evidence additionally owns the inspected files and diff by the exact checkpoint: a newer checkpoint drops them, a late inspection
    of an older checkpoint is discarded, an inspection failure stays on its checkpoint, and the inspecting flag belongs to the checkpoint being
    inspected. The execution polling chain ends with its lifetime and is still bounded to while an execution is `Running`.
  - Accepted operations stay real: preparation, identity recheck, checkpoint capture, command configure/update/remove, verification start and review
    decision are sent exactly as before, with the same project, command, checkpoint and execution identifiers and the literal argument array. Their
    continuations are guarded: a handler retained from a replaced lifetime starts no request, and an obsolete completion neither refreshes, reports,
    clears a flag nor resolves true (`configure` and `record` resolve false then). Nothing is undone or retried.
  - Components: `VerificationCommandsPanel` keeps its form draft, pending start, start message and selected output in project-owned state; a saved
    draft is cleared only when it is still the draft that was saved. `CheckpointReviewPanel` keeps the reviewer and chosen verification evidence in
    project-owned state, the evidence choice additionally tied to its checkpoint. `useVerificationExecutionOutput` owns text, final flag and error by
    project, execution, stream and enabled state, so another execution starts from empty text in its first frame. `App` keys `CandidateWorkspacePanel`
    by project and `RunCockpitView` by run, and `CandidateWorkspacePanel` keys its three sub-panels by project; the keys are an additional reset, not a
    substitute for the lifetime guards. Bare-hook and RunCockpitView regressions run without App's selection keys; CandidateWorkspacePanel regressions exercise its keyed child composition.
  - Correction round (Codex NO-GO R1-R3, same slice): (R1) the command draft carries a version that advances on every edit, even one that yields identical
    values; a successful save clears the form only if the version is still the one it sent (an edit and a restore to the saved values keeps the draft), and
    only for its own project lifetime. (R2) execution polling is governed only by the newest accepted read of the project lifetime: a Running answer, from
    the initial read or a refresh, starts or restarts the single one-second chain; a terminal, empty or failed answer ends it; a superseded answer can neither
    start nor keep it alive; replacement and unmount clear it. (R3) every catch-up entry point (refresh, notification, reconnect, post-connect, the loop after
    each await) first requires the lifetime to be active, because the owner ends in the replacement commit's layout cleanup, before the passive cleanup that
    retires the generation; a discarded refresh resolves false and a waiter is released truthfully. The scoped verification-output polling applies the same
    rule before and after each read.
  - Behavior notes for review: a `null` selection reports `loading: false`; the initial read of the workspace and verification-execution hooks starts directly in the
    effect, while the evidence, command and review hooks still start theirs in a microtask (as before), so for those a selection replaced before it runs requests nothing; read errors keep their existing same-owner clearing behavior; the
    cockpit's cards restart with the cursor if React Strict Mode re-runs the effect of the same lifetime.
- Inventory (against the parent, excluding `planner-handoff.md`): production, 11 modified (`App`, `CandidateWorkspacePanel`, `CheckpointReviewPanel`,
  `VerificationCommandsPanel`, `useRunCockpit`, `useProjectWorkspace`, `useProjectGitEvidence`, `useProjectVerificationCommands`,
  `useProjectVerificationExecutions`, `useProjectCheckpointReviews`, `useVerificationExecutionOutput`) and 1 new (`useOwnedLifetime`); tests, 6 new
  (`useRunCockpit.selection.test`, `projectOwnership.test`, `layoutBoundary.test`, `ProjectSelectionOwnership.test`, `RunCockpitSelectionOwnership.test` and the Chromium
  `e2e/project-selection.spec.ts`); documentation, 2 modified (this file and the run cockpit specification, whose switcher section now states the
  per-frame ownership rule). No existing test was changed.
- Checks run on the **final tree** (all fresh, none retained): `npx vitest run` 113 files, 1531/1531 (was 108 files, 1470; +61 tests in 5 files: 11
  run-selection hook, 33 project/output/polling hook, 2 layout-boundary, 12 project-panel and 3 run-view tests); `npm run typecheck` (`tsc -b`) clean; `npm run build` clean (the existing chunk-size
  notice only); `npm run lint` 10 warnings and 0 errors (the recorded baseline was 12; the remaining ones are existing `set-state-in-effect`/`purity`
  findings in files this slice did not touch); `npm audit` 0 vulnerabilities; `npm run test:harness` 19/19; the full Chromium suite
  (`npx playwright test`, real host in the `PlaywrightSmoke` environment, `reuseExistingServer: false`) 6/6, and the new specification alone passed 6 consecutive
  runs after two flakes were fixed in its own fixture (see below); `dotnet build DevalCopilot.slnx --no-incremental -m:1` 0 warnings, 0 errors with
  `api-client.ts` deleted first and regenerated byte-identical (SHA-256 `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`, no change in Git);
  `git diff --check` clean (only Git's CRLF notice for the planner-owned file); every changed and new file other than the preserved CRLF planner-owned `planner-handoff.md` was checked for NUL, trailing whitespace and
  carriage returns (none; the planner file keeps its CRLF endings and was not otherwise inspected for whitespace); the added documentation contains no new relative links. Backend test suites, `dotnet format` and the vulnerable-package scan were
  not repeated because no backend or package file changed; the recorded results of the previous delivery for them are retained and unverified here.
- Correction-round proof (fresh): the R1-R3 regressions were written first and were red against the pre-correction tree (the restored-draft case; the two polling
  reproductions and the obsolete-Running case; the layout-effect refresh/notification/reconnect case giving requests [A, A, B] instead of [A, B]; the output timer
  firing before passive cleanup), then green after the fix, together with unchanged-draft success, a single bounded chain, and stop on replacement and unmount.
  The full final-tree results above were re-run after the correction (the solution build and client hash after the last production edit; the later edits were tests only).
- Proof: the regressions were written before the production change and run against the parent's production files. The three confirmed failures reproduced
  red: after A loaded, the pending B still exposed A's cockpit; A to `null` still exposed A's cockpit; and Git evidence for B followed by A's late answer
  ended at A's checkpoint (`expected 'checkpoint-a' to be 'checkpoint-b'`). Against the parent, 33 of the 48 hook and component tests written by then were red (the rest
  assert unchanged same-owner behavior and so pass on both), and later runs reproduced red for the run-view specification (2 of 3) and the Chromium
  specification. Committed frames are observed, not eventual state: the hook tests record every render; the component tests record `document.body`
  text from a `Profiler` after every commit of the subtree (including the commits of state updates), so a frame that shows the previous selection cannot hide
  behind a later correct one; the Chromium specification adds a `MutationObserver` that fails on any mutation showing A's objective while B is selected.
  Coverage: A to B to A, `null`, disabled evidence, unmount/remount, stale success and failure of every project hook and of the cockpit, stale notifications,
  reconnects, refresh callbacks and waiters, older/newer overlapping reads (including B answering before the older A), a response naming another run, C1 to C2
  during inspection (hook and visible panel), exact request identifiers and literal arguments (workspace, evidence, files and diff, claim, configure, update,
  remove, record, output offsets), an accepted preparation/capture/configure/claim/record that completes after the selection changed (no report, refresh,
  pending flag, draft clearing or message for the replacement), a retained handler starting no work, drafts, selected output and chosen evidence not
  carried over, a typed-while-saving draft kept, and same-owner success, coalesced catch-up, deduplication and recoverable failure still working. Materially
  different project, run, checkpoint, command, execution and review identifiers are used throughout.
- Chromium regression (`e2e/project-selection.spec.ts`): two owned fixture repositories registered through the public operation, each with its own manual
  run and distinct objective, selected explicitly. `page.route` forwards the page's real authenticated `GET /api/runs/{id}/cockpit` to the host and only holds
  (or, for the failure case, replaces) its delivery. While B is outstanding the page shows `Loading run…` and none of A's heading, stage, workflow rail or
  Codex planning action; a returning A held while B is shown and released late changes nothing; a failing B shows no A evidence; recovery shows B's own
  cockpit. Fixture isolation, the owned root and cleanup are the existing ones. Two flakes were fixed in the specification itself, not in the assertions
  about A and B: the held-request count is now "at least one" (Strict Mode and a catch-up can issue two requests), and the held answer is buffered before it is
  delivered (a fetched response could be disposed when the page had dropped the request).
- Remaining limitations and risks: no real provider and no native Tauri shell were involved; the Chromium run uses the Vite dev server in Strict Mode, not
  the packaged shell. The other read hooks of the cockpit (agent attempt status, timeline, evidence drill-downs and so on) were deliberately not rewritten and keep
  their earlier per-run contracts; the run view's reset on selection also relies on them and on the `RunCockpitView` key. `WorkspaceEvidencePanel`, `VerificationCommandsPanel`
  and `CheckpointReviewPanel` each still hold their own independent evidence read of the same project (as before), so they can show different recorded checkpoint snapshots after a capture until
  each next reads. The Chromium observer is a `MutationObserver` batch check beside the explicit assertions, not a per-commit hook. SignalR connection console lines
  still appear in browser runs. This is not Increment 4 completion and no next slice is selected here.
- Published delivery: `df7c97a2f4cc20b74d07898758d2749157e34836` (parent `fbadc4e6b25e5015988b368f456b7a817ee06dd3`) was committed with the reviewed
  21-file slice (14 modified tracked files including `planner-handoff.md` with Codex's GO record and this file, and 7 new; the staged inventory matched the reviewed
  file list and `git diff --cached --check` was clean first), pushed to `origin/main` as a normal fast-forward, and verified after `git fetch`: `HEAD`, local
  `origin/main` and live `refs/heads/main` all equal that commit with a clean checkout. Post-publication checks against that commit, all run fresh:
  `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors, generated-client SHA-256 unchanged
  (`1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`); `npx vitest run` 113 files, 1531/1531; `npm run build` (`tsc -b` and the production build) clean;
  `npm run lint` 10 warnings and 0 errors; `npm run test:harness` 19/19; the full Chromium suite (`npx playwright test`, normal authentication,
  `reuseExistingServer: false`) 6/6; `git diff --check` clean; clean checkout; no `devalcopilot-e2e-*` owned root left under the temp directory. Retained
  pre-publication evidence, not repeated here: `npm audit` 0 vulnerabilities, the `--no-incremental` client regeneration, and the hygiene and link checks of the
  reviewed tree; backend suites, `dotnet format` and the package scan were not run for this frontend-only change. No next slice is selected.

## Implemented-plan identity through review and correction (2026-10-01)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `bfb6392074901588639de50633cbb115e8ff77c5` (`main`; `HEAD`, local `origin/main` and live `refs/heads/main` matched it, nothing staged or
  untracked, only the planner-owned `planner-handoff.md` modified at the start; the generated client SHA-256 matched
  `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`). Presented as an uncommitted, unstaged, unpushed diff for Codex's
  GO/NO-GO; `planner-handoff.md` was not edited.
- Published delivery: `c13b6f086be3b08db2bfa5382a7709269c64be91` (parent `bfb6392074901588639de50633cbb115e8ff77c5`) was committed with the reviewed slice
  (13 files: 11 modified tracked including `planner-handoff.md` with Codex's GO, and 2 new; the staged inventory and `git diff --cached --check` were verified
  first), pushed to `origin/main` as a normal fast-forward, and verified after `git fetch`: `HEAD`, local `origin/main` and live `refs/heads/main` all equal that
  commit with a clean checkout. Post-publication verification against that commit: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1`
  0 warnings, 0 errors; filtered Application tests (the new implemented-plan tests, the downstream, repair, code-review claim and correction-status classes) 212/212;
  filtered Api tests (`HumanAuthorizedPlanHostedTests`) 13/13; full Architecture 9/9; no skips or failures; generated-client SHA-256 unchanged
  (`1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`); `git diff --check` clean; clean checkout. The Application and Api runs are filtered and
  are not repeats of the full suites. The full-suite results, frontend, lint, harness, audit and formatter evidence recorded below are retained pre-publication evidence
  from the identical tree, and no new Chromium run was made. No next slice is selected.
- Delivered ([ADR-0017](../decisions/0017-review-the-implemented-plan-through-correction.md), which narrowly supersedes only ADR-0016's
  preservation of the ordinary first-revision review target and manifest bytes; ADR-0016's text is unchanged): every newly claimed
  CodeReviewer attempt judges the exact Proposal the initial implementation consumed. `ImplementerExecutionReportEligibility.Result`
  now has a required `ImplementedPlan` (the implementation's sequence-zero input) beside `OriginalProposal` (the actual validated Planner
  root). Every initial ExecutionReport must reply to that first input; the root comes from the accepted Planner proposal or the validated
  `PlanningLineage` (never from the report's reply), and malformed evidence no longer falls back to a root. The accepted root, a first
  Resolver revision with its Decisions, that revision with its exact optional Acceptance, and the authorized depth-two final plan are all
  covered with every existing restriction unchanged (challenged first revisions and unauthorized depth-two plans stay refused).
  `ImplementedPlan` is carried unchanged through every correction link; correction reports still reply to the root, revision responses to
  their findings, and correction inputs and manifests are unchanged. The initial review, a new format repair (including of an older failed
  source), a correction re-review and a format repair of that re-review seal the implemented Proposal's identifier, summary and content as
  `resolvedPlan` through the unchanged `CreateCodeReviewAttempt` and builders. No operation, DTO, migration, dependency, provider flag,
  permission, tool, adapter version, output contract or frontend behavior changed; the only production file touched is
  `ImplementerExecutionReportEligibility`. **Forward only**: nothing historical is rewritten, and an already-claimed undispatched review
  replays its existing sealed bytes (an older manifest may name the root) because replay never rebuilds a manifest; reviews that already
  concluded keep their outcome, with no approval revoked and no new entitlement to another review.
- Inventory (against the parent, excluding `planner-handoff.md`): 1 production file modified (`ImplementerExecutionReportEligibility`); tests: 4
  modified (`HumanAuthorizedPlanHostedTests` now also hosting the ordinary-plan flows and a shared `SeedRootAsync`, `PlanningAuthorizationDownstreamTests`,
  `PlanningLineageSeeder` whose revised proposal now has its own steps, `RepairTestScene` with plan forms) and 1 new (`CreateCodeReviewImplementedPlanTests`);
  documentation: 5 modified (this file, the protocol, the workflow model, the decision index and the engineering context) and 1 new (ADR-0017).
- Checks run on the **final tree** (all fresh, none retained): `dotnet build DevalCopilot.slnx --no-incremental -m:1` 0 warnings, 0 errors, with `api-client.ts`
  deleted and regenerated byte-identical (SHA-256 `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`); sequential `dotnet test --no-build`:
  Domain 903/903, Application 2914/2914 (was 2881), Infrastructure 943 passed + 3 skipped (the existing skips) of 946, Api 718/718 (was 714), Architecture 9/9;
  frontend `npx vitest run` 108 files, 1470/1470, `npm run typecheck` clean, `npm run build` clean, `npm run lint` 12 warnings (the baseline) and 0 errors,
  `npm run test:harness` 19/19, `npm audit` 0 vulnerabilities; `dotnet list package --vulnerable --include-transitive` none; `dotnet format --verify-no-changes`
  153 findings in 17 files, identical to the recorded baseline and none in a file this slice touched; `git diff --check` clean (only Git's CRLF notices), a node check of
  every changed and new file found no NUL, no trailing whitespace and no carriage return in an untracked file, and the local Markdown links of the changed documents
  resolve. No new browser specification was run because no browser behavior changed (the last full Chromium run belongs to the previous delivery).
- Proof: regressions were written after the production fix; red evidence was then obtained by restoring the parent's eligibility code
  (16 Application tests and the 2 new hosted flows red there, restored green).
  Real file-backed SQLite with the real claim handler and substantively different root and revised summaries and steps: each of the three ordinary forms
  (initial and corrected) seals the implemented Proposal's id, summary and content, for an initial review, a new format repair of an older failed source, and a re-review,
  with no superseded root text for revised plans; the chain keeps the root as `OriginalProposal`; reports replying to the root instead of their revised first input,
  to an unrelated same-run root or revision, or with a missing first input or one replaced by another lineage's Proposal,
  first input, and corrections replying to the revision or to an unrelated root, are not valid chains and a claim over them is refused without an attempt, a seal or an artifact;
  an authority change committed before BEGIN at the existing repair seam (report reply to root, first input removed) is refused with orphan cleanup despite a populated
  context. The builder test shows a different plan changes only the `resolvedPlan` values and pins the member orders. The hosted deterministic flow (real supervisors, mediator,
  EF pipeline and Claude adapters; doubles only at the process and Codex-review boundaries, the review adapter capturing the sealed manifest it is handed) runs a
  production-written first challenge resolution through implementation, verification, an invalid-format review, manual repair to changes requested, two ordinary correction
  links (inputs, root replies and finding replies checked, the correction stdin free of both plans), a re-review, and an invalid-format re-review repaired to approval, all
  reviewing the revision; an accepted root and an Accepted first revision are reviewed as their own plans; an older root-target sealed review, fabricated by rewriting a
  claimed review's sealed manifest and artifact row at test level, replays its exact bytes after a restart; the authorized depth-two flow stays green.
- Focused red/green mutations, each restored and re-run green: the root substituted as the implemented plan (3 hosted and 12 Application tests red), `ImplementedPlan` replaced by
  the root across a correction link (2 hosted and 6 Application red), and the initial reply/input equality removed (6 Application red).
- Remaining limitations and risks: Process doubles prove reachability and sealed-form agreement, not real-provider reliability; no real provider ran. History is not repaired: a review
  that concluded before this change keeps the root target it was judged against, and a review sealed earlier replays that target. Manifest bytes for the accepted root and the authorized
  final plan are unchanged because the builders were not touched, but they are pinned by structure and by the equality of plan values, not by a byte-for-byte golden. The unrelated
  limitations of the previous deliveries stand (the escalation copy that predates ADR-0016, raw-SQL Playwright fixtures, the post-dispatch manifest-agreement check, `Available` not a live
  fingerprint check, SignalR console lines in browser runs). This is not Increment 4 completion and no next slice is selected here.

## Explicit human authorization of one escalated-plan implementation (2026-10-01)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `3fe5f08cb648f3726e385d14be554ae4e1a4fda0` (`main`; `HEAD`, local `origin/main`, and live `refs/heads/main` matched it, nothing staged
  or untracked, and only the planner-owned `planner-handoff.md` modified at the start; the generated client SHA-256 matched
  `44afe84a4aa14ce6f9307f7a248dab4e5b31977eecd34b812da47ebf8475f44b`). Presented as an uncommitted, unstaged, unpushed diff for Codex's
  GO/NO-GO; `planner-handoff.md` was not edited.
- Published delivery: `47e141cd50ba3d05b26bf76476f576c83650f57d` (parent `3fe5f08cb648f3726e385d14be554ae4e1a4fda0`) was committed with the reviewed slice
  (83 files: 34 modified tracked including `planner-handoff.md` with Codex's GO, and 49 new; the staged inventory and `git diff --cached --check` were verified
  first), pushed to `origin/main` as a normal fast-forward, and verified after `git fetch`: `HEAD`, local `origin/main` and live `refs/heads/main` all equal that
  commit with a clean checkout. The full-suite results recorded below are the earlier pre-publication evidence on the identical tree. The post-publication reruns
  against that commit were: a clean `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` (0 warnings, 0 errors); Domain full 903/903;
  filtered runs only, not repeats of the full suites: Application (authorization command, claim, query, downstream, manifest, CreateCodeReviewAttempt and
  GetReviewCorrectionAttemptStatus filters) 207/207, Infrastructure (adapter and migration tests) 19/19, Api (hosted chain and endpoint tests) 20/20; Architecture
  full 9/9; frontend full vitest 108 files 1470/1470, production build clean, lint 12 warnings (the baseline) and 0 errors; harness 19/19; full Chromium 5/5 with
  `reuseExistingServer: false` and no leftover `devalcopilot-e2e-*` directory; generated-client SHA-256 unchanged
  (`1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`); `git diff --check` clean; clean checkout. No skips in the reruns. The remaining
  limitations below stand and no next slice is selected.
- Delivered ([ADR-0016](../decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md)): a human may
  authorize exactly one initial implementation claim for the final (depth-two) Proposal of a completed second challenge round.
  `PlanningLineage.MaximumDepth`, `MaximumReviewableDepth` and `MaximumImplementableDepth` are unchanged and a third review or
  resolution stays refused with and without a grant. **Operations**: protected `POST` and `GET
  /api/runs/{runId}/planning-escalations/{escalationMessageId}/implementation-authorization` (body only `{ rationale }`, 8 KiB cap,
  the shared 600-code-unit normalized-text policy, `400 planning_authorizations.rationale_invalid` from a validator repeated by the
  handler, never echoed). **Source**: a host-constructed, attemptless, protocol-1.0 Orchestrator-to-Human escalation that is the only
  escalation replying to a complete coherent two-round lineage's final Proposal and equals the canonical summary and content the second
  resolution writes (`PlanningEscalation.BuildStructuredContentJson`, now shared); current run, stored mode, workspace, active lease,
  checkpoint and a fresh Git fingerprint; no newer provider-observed Planner Proposal. **Record**: table
  `planning_implementation_authorizations` (migration `AddPlanningImplementationAuthorization`, additive, no backfill) binding run,
  escalation, final Proposal, workspace, starting checkpoint and fingerprint to one canonical `HumanInstruction`
  (`CollaborationMessage.RecordPlanningImplementationAuthorization`, own summary and fixed instruction; the review-correction factory and
  authorization are untouched), with unique indexes on the escalation, the final Proposal, the instruction message and (filtered) the
  consuming attempt, and `ConsumedByAttemptId` as the EF concurrency token. The authorization is one short transaction after the Git
  capture whose first statement (the atomic execution-mode confirmation) takes the write lock, with every authority read afresh and
  untracked inside it; identical retries reuse the record without another message or event, a different rationale is `409
  rationale_conflict`, stale and consumed grants are never revived. **Claim**: the existing `POST …/agent-attempts/implementation`
  (body unchanged) recognizes only the authorized final Proposal, validates before external work, and again inside a short transaction
  opened after manifest sealing (mode confirmation, fresh run/workspace/lease/checkpoint/fingerprint, lineage and grant reads), and
  consumes the grant in the same save as the Attempt, its ordered inputs (final Proposal, every second-round Decision in collaboration
  order, the authorization `HumanInstruction`), the manifest artifact and the ordinary budget reservation; refusals, lost races, a failed
  save or commit consume nothing and delete the orphan manifest; a committed claim spends the grant permanently. **Manifest**: the
  distinct form `humanAuthorizedEscalatedProposal` (complete decisions and the exact `humanAuthorization` identifiers and rationale)
  behind a fixed `humanPlanAuthorizationBoundary`; the 32 KiB ceiling shrinks repository evidence only and evidence that cannot fit is
  refused whole before sealing; earlier forms are byte-identical (their member order is pinned by a test). **Dispatch**: the eligibility
  feed excludes, and the fresh dispatch gate refuses (`agent_attempts.planning_authorization_mismatch`), an attempt whose consumed grant,
  source, consumption owner or exact inputs disagree with the durable facts (`PlanningImplementationAuthorizationEvidence.ClassifyAttempt`
  over a fresh untracked snapshot); the supervisor's projected `PlanningImplementationAuthorizationFact` rides the internal request
  solely for the Claude adapter's sealed-form agreement (`PlanningImplementationAuthorizationManifest.Agrees`) before any process; result
  recording re-checks the identity. No provider flag, tool, permission, output schema or contract version changed. **Downstream**:
  `ImplementerExecutionReportEligibility` validates the authorized chain against the implementation's starting checkpoint and returns the
  actual Planner root across both revisions as the original proposal (the report must reply to the first input) and, separately, the
  implemented final Proposal as `ImplementedPlan`; verification, CodeReviewer, ordinary correction and re-review run unchanged, with
  the initial review, its format repair and the correction re-review judging the final Proposal (identifier and content), never the
  root or the first revision. **Cockpit**: `PlanningImplementationAuthorizationPanel` and
  `PlanningAuthorizationForm` after the lineage summary (server-read states Absent/Available/Consumed/Stale/Invalid with the exact recorded
  reason; nothing inferred from a click; failed reads show only an honest unavailable line), the existing implementation action offered
  separately and only for the exact final plan with an Available authorization (`selectAuthorizedPlanMessageId`, label and withheld note
  props on `ImplementationAction`), ownership by run, escalation and final plan with the existing owned-flow/run-scoped-action primitives.
  The TypeScript client was regenerated by the build, not edited.
- Review correction round (Codex NO-GO R1-R3, same slice, still uncommitted): **R1** the code-review target of an authorized chain was
  the Planner root; `ImplementerExecutionReportEligibility.Result.ImplementedPlan` now carries the final Proposal through the
  authorized report and the whole correction chain (`CreateCodeReviewAttempt` uses it as `resolvedPlan`), while `OriginalProposal`
  stays the root for lineage and ADR-0010 reply identities and every earlier plan form keeps its review target and manifest bytes. The
  hosted chain now seeds substantively different root, first-revision and final plans, makes the review adapter capture the sealed
  manifest it is handed, and asserts the final plan's identifier and content (and the absence of the superseded plans' text) in the
  initial review, a manual format repair and the correction re-review, plus the correction evidence and the root-replying correction
  report. **R2** `usePlanningImplementationAuthorization` gave settled facts and request tokens reusable identities; it now tags every
  settled read with a committed, never-reused identity lifetime and read id (created during render when the run, escalation, sequence or
  refresh changes), so a return to an earlier run or escalation awaits a fresh read and an earlier Available, Consumed or error record
  never authorizes it, while a same-identity refresh still keeps its last facts (the open form keeps its state). Controlled-promise hook
  and real-cockpit regressions cover run and escalation A to B to A, earlier Available, Consumed and error, pending and failed
  replacement reads, obsolete completions and a null round trip. **R3** the authorized claim's post-seal exits left orphan manifests on
  cancellation. `HandleAsync` now wraps the claim in a scope armed once an authorized manifest is sealed; any exception after that
  (late read, save, commit, cancellation) rolls the transaction back and releases it, asks an independent connection with a never-cancelled
  token (`IAttemptDurabilityProbe`, an optional constructor parameter that DI resolves from the existing registration) whether the attempt
  is durable, deletes the manifest only for a definite non-commit, and keeps it for a committed claim (one spent grant) or an unanswered probe
  (`attempts.persistence_unresolved`). The save/commit failure branches of the authorized claim use the same probe instead of a read on the
  claim's own context. Regressions: cancellation at begin, a late read, before save, after save and before commit remove the orphan and
  consume nothing; cancellation after commit keeps the manifest and exactly one consumed grant; commit failures with a failing rollback settle
  from durable state; a raw late-read failure; an unanswered probe keeps the file.
- Existing tests adjusted deliberately: `GetReviewCorrectionAttemptStatusQueryHandlerTests` fixed query count 6 to 7 (the shared run
  snapshot now reads the run's authorizations in one bounded query, not per candidate); `SecondChallengeRoundTestSupport.SeedSceneAsync`
  now reserves checkpoint number 1 on the workspace (so a real handler's result checkpoint is number 2); `FaultInjectingDbContext` forwards the
  new set; the claim-handler test helper passes an optional durability probe; `RunCockpitView.test.tsx` mocks the two new hooks beside the others. The lineage summary copy keeps its "cannot be implemented
  through this lineage" sentence and adds "without an explicit human authorization, which permits at most one implementation claim of it".
- Inventory (the working tree against the parent, excluding `planner-handoff.md`): 33 modified tracked and 49 new files. Backend source: 17 modified
  (`IDevalCopilotDbContext`, `DevalCopilotDbContext`, the EF model snapshot, `CollaborationMessage`, `ImplementationSupervisor`,
  `CreateImplementationAttemptCommandHandler`, `CreateCodeReviewAttemptCommandHandler`, `ImplementationContextManifestBuilder`, `MarkAgentAttemptDispatchedCommand` and handler,
  `RecordImplementationResultCommandHandler`, `ImplementerExecutionReportEligibility`, `PlanningEscalation`, `ImplementationInvocationRequest`,
  `EligibleImplementationAttempt` and its query handler, `ClaudeImplementationAdapter`) and 24 new (Domain 2: `PlanningImplementationAuthorization`,
  `PlanningImplementationInstruction`; Application 14: the command, handler, validator and result, the query, handler, result and state, five Policies
  `PlanningImplementationAuthorizationEvidence`/`Context`/`Attempt`/`Fact`/`Manifest`, and `Errors/PlanningImplementationAuthorizationErrors`;
  Api 5: two endpoints, request and two responses; Infrastructure 3: the entity configuration, the migration and its designer). Tests: 3 modified and 11 new
  (Domain 1; Application 6: support, command, query, claim, downstream and manifest tests; Infrastructure 2: migration and adapter tests; Api 2: endpoint and
  hosted-chain tests). Frontend: 7 modified (`clients.ts`, the regenerated `api-client.ts`, `ImplementationAction`, `PlanningLineageSummary`, `RunCockpitView`
  and its test, `derivePlanningLineage`) and 13 new (the panel, form, two hooks, failure mapper, three unit tests plus the ownership test, and three Playwright files:
  the fixture, the wire specification and the UI specification). Documentation: 6 modified (this file, the decision index, the engineering context, the protocol,
  the workflow model, the cockpit specification) and 1 new (ADR-0016).
- Checks run on the **final tree**: `dotnet build DevalCopilot.slnx` 0 warnings, 0 errors; `api-client.ts` deleted and regenerated by `dotnet build
  --no-incremental` byte-identical (SHA-256 `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`, was `44afe84a…` at the parent);
  sequential `dotnet test --no-build`: Domain 903/903 (was 881), Application 2881/2881 (was 2729), Infrastructure 943 passed + 3 skipped (the existing
  Windows-only/symlink skips) of 946 (was 924 + 3), Api 714/714 (was 694), Architecture 9/9; frontend `npx vitest run` 108 files, 1470/1470 (was 103
  files, 1394), `npm run typecheck` clean, `npm run lint` 12 warnings (the baseline) and 0 errors with none in a file this slice created, `npm run build`
  clean (usual chunk-size notice), `npm run test:harness` 19/19, `npm audit` 0 vulnerabilities, real-host Chromium `npx playwright test` with
  `reuseExistingServer: false`: 5/5 (manual intake, simulation, the direct-guidance wire specification, the new authorization wire specification over the
  generated client, and the new authorization UI specification), no `devalcopilot-e2e-*` directory left; `dotnet list package --vulnerable
  --include-transitive` none; `dotnet format DevalCopilot.slnx --verify-no-changes`: 153 findings in 17 files, identical to the parent's recorded baseline and
  none in a file this slice created; `git diff --check` clean (only git's CRLF notice for the generated client), a node check of the 49 untracked and 34
  modified files (the planner record included) found no NUL, no trailing whitespace and no carriage return in an untracked file (EF writes CRLF; the migration, designer and snapshot were
  normalized to LF, the repository's `eol=lf`), a secret-pattern scan of the diff found only the intentionally fictitious test sentinels, and the local
  Markdown links of the changed documents resolve.
- Proof of the acceptance matrix (real file-backed SQLite, deterministic doubles, no real provider): migration from the parent schema with a historical
  depth-two escalation and review-correction authorization preserved and no grant fabricated, unique-index and consumption-concurrency backstops, and
  reversal dropping only the table; source and provenance validation (unknown, foreign, non-escalation, forged summary/content/recipient/actor/reply/attempt/
  protocol, ambiguous second escalation, depth-one source, missing decision, duplicated root), newer-root and checkpoint staleness, fingerprint and
  run/workspace/lease/mode gates, normalization and the 600 boundary, idempotency, conflicting rationale, consumed and stale retries, corrupted record,
  four concurrent identical requests recording one authorization; populated-context seam tests committing a competing planner root, checkpoint, lifecycle,
  mode, lease, identical or conflicting authorization, instruction tampering, or a competing claim from another connection before BEGIN (authorization and
  claim) with fresh reads proven, capture-before-begin ordering, a failed save, and a commit that failed after or before landing; single consumption by
  four simultaneous claims, a lost race at seal time, rollback with orphan cleanup, seven existing hard gates leaving the grant unconsumed, a failed
  attempt still spending the grant, and evidence too large for the manifest refused whole; tampered grants and inputs (wrong consumption owner, unconsumed
  grant, missing/extra/reordered input, changed instruction, grant checkpoint) excluded by the feed, refused by the dispatch gate (including from a populated
  context), refused at result recording, and invalidating the report chain, with 14 adapter cases and 4 hosted cases starting no process; depth-two
  review and resolution refused with and without a grant. The hosted deterministic chain (real supervisors, mediator, EF pipeline and Claude adapters;
  doubles only at the process and Codex-review boundaries) proves production-written second resolution, authorization, explicit claim, exact ordered inputs, the
  implementation's stdin, unchanged tools/permission arguments, checkpoint and ExecutionReport replying to the final plan, local verification, code review
  (its manifest names the final plan, in the initial review, a format repair and the re-review), ordinary correction (inputs, revision-response and report replies to the actual root) and re-review to approval, the
  budget slot, and a restart replay of an undispatched claim with the same sealed bytes and one consumed grant. Frontend: controlled-promise ownership across
  A to B to A, run/escalation/final-plan replacement, unmount, synchronous duplicates, edits during the request and during the refresh, late acceptance and late
  rejection of an ended interaction, an accepted request whose refresh fails, stale reads, and per-code refusal copy; and the cockpit states for every
  authorization state.
- Focused red/green mutations, each restored and re-run green: `IsSuperseded` always false (6 tests red: authorization, claim, query and seam freshness);
  an unauthorized depth-two plan treated as eligible (3 red); the grant not consumed in the claim save (the claim, downstream, feed and chain tests red) and the
  last ordered input dropped (red likewise); `MaximumReviewableDepth` raised to 2 (4 red: depth-two review and resolution); the original proposal returned as the
  depth-one parent instead of the Planner root (1 red); the form clearing a draft without the ownership check (2 red) and without the identity reset (3 red); correction round: code review given the root instead
  of the implemented plan (the hosted chain red), the authorization read hook restored to its reusable identities (4 hook and 2 real-cockpit tests red), the
  post-seal cleanup disabled (5 cancellation and late-exit regressions red) and the cleanup deleting a committed claim's manifest (1 red).
- Remaining limitations and risks: Process doubles prove reachability and sealed-form agreement, not real-provider reliability; no real provider ran. The
  manifest agreement is checked after the dispatch marker is committed (as for direct guidance), so an out-of-band change ends as a failed attempt that consumed
  its slot and grant. `Available` means recorded, coherent, unconsumed and bound to the database's current checkpoint; the claim still re-checks the live fingerprint
  and every other gate. Observed, deliberately unchanged: an ordinary first-revision (depth-one) implementation still resolves its report chain to the Planner root, so its code
  review targets the root exactly as before this slice (earlier plan forms keep their behavior and bytes); only the authorized final plan now reviews the plan
  it implemented, and the planner may want the depth-one case revisited separately. The host's escalation text
  ("not implementable through this lineage") predates this decision and is not rewritten; the cockpit panel states the new option. The Playwright specifications create
  attempt, input and message rows with raw SQL (the public operations need a provider) and never request an implementation, because the smoke host could otherwise start a
  real Claude; the canonical escalation JSON is reproduced in the fixture from the documented contract. The authorization row is not removed with its run by a deletion
  operation because none exists (the run cascade exists as for the other relations). SignalR "connection was stopped during negotiation" console lines appear in browser
  runs as before (their baseline was not separately measured). This is not Increment 4 completion and no next slice is selected here.

## Direct human guidance for mutation requests (2026-10-01)

- Published delivery: `c57439ebec2463de900fa6d4fb66831952b4254c` (parent `371c3a6b81ddc65bc8de7057d8bd8ce2b388296e`) was committed with the
  reviewed slice (116 files: 81 modified tracked including `planner-handoff.md` with Codex's GO, and 35 new; the staged inventory and
  `git diff --cached --check` were verified before the commit), pushed to `origin/main` as a normal fast-forward, and verified with `git fetch
  origin main` and `git ls-remote origin refs/heads/main`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a
  clean tree. Post-publication verification on that commit (`--no-build --no-restore` for tests, run sequentially after `dotnet build
  DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1`, 0 warnings, 0 errors): Domain 881/881; Application filter
  `DirectHumanGuidance` 99/99; Infrastructure filter `DirectHumanGuidance|AddDirectHumanGuidance` 52/52; Api filter
  `DirectGuidance|DirectHumanGuidance|ReviewCorrectionGuidanceReplay` 36/36; Architecture 9/9; none skipped. Frontend: `npm run test:harness`
  19/19, `npx vitest run` 103 files 1394/1394, `npm run build` clean (usual chunk-size notice), `npm run lint` 12 warnings (the baseline) and 0
  errors, then `npx playwright test --reporter=line` 3/3 with `reuseExistingServer: false`; no `devalcopilot-e2e-*` directory remained;
  `api-client.ts` SHA-256 stayed `44afe84a4aa14ce6f9307f7a248dab4e5b31977eecd34b812da47ebf8475f44b`; `git diff --check` and the checkout were
  clean. Retained evidence, not rerun in this verification: the full Application (2729), Infrastructure (924 + 3 skipped), and Api (694) suites,
  the formatter result (153 findings in 17 files, the recorded baseline), and the vulnerable-package result, all from the final reviewed tree
  before publication. Limits are those listed in the delivery entry below (no real-provider reliability, the manifest agreement is checked
  after dispatch is marked, raw-SQL fixture rows in the wire specification, unmeasured SignalR console-line baseline). This is not Increment 4
  completion and no next slice is selected here.

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `371c3a6b81ddc65bc8de7057d8bd8ce2b388296e` (`main`; `HEAD`, local `origin/main`, and live `refs/heads/main` matched it, nothing staged or
  untracked, and only the planner-owned `planner-handoff.md` modified at the start; the generated client SHA-256 matched
  `7253732aa5f7bd4c98198f31e2bb701c8c4b74ca2368c1d2c24d4ee78e67f2c8`). Presented as an uncommitted, unstaged, unpushed diff for Codex's
  GO/NO-GO, then corrected in the same executor chat after Codex's first review (R1–R4 below). `planner-handoff.md` was not edited. This
  entry records the delivered SHA only after publication.
- Delivered ([ADR-0015](../decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md)): the two explicit mutation requests
  (`POST …/agent-attempts/implementation` and `…/review-correction`) accept an optional `guidance` (routes, source requirements, and response
  shapes unchanged; omitted or `null` is the previous behavior; each body capped at 8 KiB). `DirectHumanGuidance.Normalize` shares one
  policy with the authorization's guidance (`BoundedGuidanceText`: form C, LF, trim, non-blank valid Unicode, at most 600 UTF-16 code units,
  no control except LF, the bounded-summary screen) while only the authorization keeps its reserved default rationale; invalid text is
  `400 agent_attempts.direct_guidance_invalid` from a validator on each command (repeated by the handler) before any read or external work,
  never echoed. Migration `AddDirectHumanGuidance` adds one nullable TEXT `attempts.AgentDirectHumanGuidance` (no default, no backfill, no
  marker column; historical rows stay null, nothing inferred); only the two mutation claims assign it in the same commit as the attempt,
  ordered inputs, manifest artifact, and budget reservation; the factories accept only normalized text with the version 2 contract and the
  workspace-edit profile; a reading is absent, valid, or malformed. At correction-budget exhaustion a request carrying guidance is refused
  `409 agent_attempts.direct_guidance_unavailable` inside the existing exhaustion branch (after every existing gate, before escalation or
  authorization logic), even when an extra authorization exists, creating no escalation and consuming no authorization. Both implementation
  manifest forms and ordinary corrections gain a fixed `directHumanGuidanceBoundary` and one `directHumanGuidance { text }` before
  `untrustedEvidenceBoundary` only when guidance exists (otherwise byte-identical, including an authorized correction's separate
  `humanGuidance`; the implementation serializer moved from an anonymous type to an order-preserving dictionary and is compared byte for
  byte with a copy of the former serializer); the 32 KiB bound is kept by shrinking evidence, never the guidance. Both eligibility feeds
  exclude a malformed or incoherent snapshot (historical unguided and authorized attempts stay eligible); `MarkAgentAttemptDispatched`
  reads the snapshot **and the whole assignment tuple** (response contract, role, provider, permission profile, adapter contract version)
  afresh and untracked in one statement, judges coherence on those fresh facts, and compares the snapshot with the one the supervisor's feed
  projected (`ExpectedDirectHumanGuidance`; a difference, or a recorded snapshot with no stated expectation, is
  `409 agent_attempts.direct_guidance_mismatch`; incoherent fresh facts are `agent_attempts.invalid_agent_contract`), never touching
  pending tracked writes; both invocation requests carry the expected snapshot and both Claude adapters require the sealed manifest (read
  through the existing sealed-artifact verification) to be a **parseable JSON object** that agrees with it before any process starts: without
  guidance neither direct member may exist, with guidance each member exactly once, the fixed boundary, only the accepted text, and exactly one
  non-empty untrusted-evidence boundary after the guidance. No CLI argument, tool, environment, permission, session, output schema, or
  provider version changed. The implementation and review-correction statuses, attempt evidence (and so the history drill-down), and the
  cockpit's latest attempt expose `directGuidance { state, text }` with three states: `NotRecorded` (a coherent mutation attempt with a null
  snapshot: **no direct guidance was recorded**, the same neutral statement for historical and new unguided attempts, never "none was
  submitted" and never dated by the contract version), `Provided` (the accepted text), and `Unknown` (no text); the contract
  `DirectHumanGuidanceResponse` lives in `Api/Features/Runs/Contracts`. The cockpit's two actions gain an optional guidance editor beside the
  unchanged plain buttons, with drafts, busy and error state, handlers, and request and refresh continuations owned by run, source, and
  interaction lifetime (A→B→A, unmount, stale handlers, synchronous duplicates, a draft version incremented by every edit including identical
  text, clearing only an unchanged submitted draft after a current success); the editor is withheld at exhaustion and while an authorization
  is available, with the separate authorization UI unchanged and the server gate authoritative. The generated client was regenerated by the
  build, not edited.
- Review correction (Codex's first NO-GO: R1–R4, all reproduced red by the reviewer, then fixed with permanent regressions): **R1** the
  dispatch gate judged a fresh snapshot against the tracked attempt's tuple, so a provider, profile, or version change committed before BEGIN
  with an unchanged snapshot still dispatched; the whole tuple is now read fresh (regressions: 18 competing-assignment cases over both paths,
  pending tracked writes preserved and tracking not disabled, a compatible competing change still dispatching). **R2** a null snapshot on a
  version 2 attempt was reported `NotProvided` / "none was submitted", inferring submission history; the state and its copy are collapsed into
  the neutral `NotRecorded` in Domain, projections, UI, the migration expectations, ADR, protocol, and cockpit specification (a legacy
  `NotProvided` from an older server is shown as unknown by the UI). **R3** the manifest check treated an unparseable or non-object text as
  agreement with a null snapshot and a guided object without an untrusted-evidence boundary as agreeing; both now disagree, and the existing
  adapter test fixtures that used opaque text now use valid bounded JSON objects (regressions: 13 new disagreement rows per path through the
  actual Claude adapters with zero process starts, including malformed JSON and an array holding the direct members, and missing, empty,
  non-string, duplicated, and misplaced evidence boundaries). **R4** `DirectHumanGuidanceResponse` moved out of the operation-bearing feature
  root into `Api/Features/Runs/Contracts` (namespace and imports updated; unrelated contracts untouched). Also added: a Playwright specification
  (`e2e/wire-direct-guidance.spec.ts`) that drives the **generated TypeScript client over real HTTP** against the real host and run-owned
  database with the launch secret as an Authorization header: guidance serialization for both operations (400 validation with fixed copy
  and no echo, versus the same domain 409 for valid and omitted guidance), and revival of `Provided` → `Unknown` → `NotRecorded` through the
  status, evidence, and cockpit responses (the attempt rows are inserted directly into the owned database because no public operation can
  create them without a provider).
- Second review correction (Codex: R1–R4 resolved; R5–R6 open; tests, test support, and documentation only, no production change). **R5**
  the browser specifications depended on execution order: registration does not select the new project and a reload selects the first
  project, so the manual-intake specification failed ("Demo only" never appeared) when the wire specification ran before it, and the earlier
  claim that the file name's lexical order isolated them was wrong and is removed. `e2e/support.ts` gains `selectProject`, which clicks the
  specification's own fixture chip and asserts `data-selected="true"`; `manual-run.spec.ts` selects its project after registration and again
  after the reload, and `simulated-run.spec.ts` uses the same helper at both points. No database reset, production hook, provider, or
  lifecycle/deletion authority was added; authentication, `reuseExistingServer: false`, and verified owned-root cleanup are unchanged.
  Red/green: the previous manual specification preceded by a temporary copy of the wire specification named to sort first failed at the
  "Demo only" assertion; with the helper all of these passed with real Chromium (`npx playwright test <files> --reporter=line`): wire,
  manual, simulated (3/3); manual, wire, simulated (3/3); manual, simulated, wire (3/3, the real file name); and each specification alone
  (1/1 each); the temporary copies were removed and no `devalcopilot-e2e-*` directory remained after any run. **R6** the wire specification
  built its fixture repository inside the retry loop, so a second pass repeated `git commit` of unchanged content and failed before another
  API request. The repository is now built once before the request, and `e2e/harness/readinessRetry.ts` retries only a 409 whose first
  problem error is exactly `projects.git_unavailable`, with a finite budget (45 attempts at 1 s inside a 60 s test timeout); any other
  failure is surfaced at once as a fixed message with the status only (never the transport detail, which can carry the Authorization
  header), and exhaustion fails with a fixed message. Deterministic regressions in `e2e/harness/harness.test.ts` (4 new, run by
  `npm run test:harness`, now 19/19): refusal recognition (only 409 plus that exact first code), first refusal then success with the
  fixture built exactly once and the same path reused, an unexpected failure surfaced after one request without the secret, and a finite
  budget. Red: a second `git commit` of unchanged content in a fresh repository fails ("nothing to commit"), which is what the old loop
  did; the generated-client-over-real-HTTP proof itself is retained unchanged.
- Inventory of this change (the working tree against the parent, excluding `planner-handoff.md`): 80 modified tracked and 35 new files —
  backend 43 modified + 13 new (Domain 3 + 4 new: `BoundedGuidanceText`, `DirectHumanGuidance`, `DirectHumanGuidanceEvidence`,
  `DirectHumanGuidanceReading`; Application 22 + 6 new: `DirectHumanGuidanceManifest`, `DirectHumanGuidanceFact`,
  `DirectHumanGuidanceErrors`, `ExpectedDirectHumanGuidance`, two validators; Api 14 + 1 new `Contracts/DirectHumanGuidanceResponse`;
  Infrastructure 4 modified including the EF snapshot, plus the `AddDirectHumanGuidance` migration and designer), tests 7 modified + 12 new
  (Domain 2, Application 4, Infrastructure 2, Api 4), frontend 24 modified (the regenerated `api-client.ts`, 10 existing unit-test files
  adjusted for the new signatures, and for R5 the browser `support.ts`, `harness.test.ts`, and the two UI specifications) + 9 new (editor,
  fact display, description helpers, failure mapping, three unit test files, the real-HTTP Playwright specification, and
  `e2e/harness/readinessRetry.ts`), documentation 6 modified (this file, the decision index, the engineering context, the protocol, the
  workflow model, the cockpit specification) + 1 new (ADR-0015). Existing backend tests adjusted deliberately: the turn-limit migration `Down_…`
  test now also expects the later `AgentDirectHumanGuidance` column to be dropped; the guided-replay hosted test allows exactly the new
  `DirectHumanGuidance` property on `ReviewCorrectionInvocationRequest` while still forbidding any authorization or authorized-guidance member;
  and, for R3, five Claude adapter test classes (implementation, review correction, turn limit, process-evidence, token-usage) seed valid bounded
  JSON object manifests instead of opaque text (assertions on the sent bytes follow).
- Checks run on the **final tree** (after the R1–R4 corrections): `dotnet build DevalCopilot.slnx` 0 warnings, 0 errors; `api-client.ts` deleted
  and regenerated by `dotnet build --no-incremental` byte-identical to the pre-correction client (SHA-256
  `44afe84a4aa14ce6f9307f7a248dab4e5b31977eecd34b812da47ebf8475f44b`, was `7253732a…` at the parent; the move of the response type did not
  change the client); sequential `dotnet test --no-build`: Domain 881/881 (was 839), Application 2729/2729 (was 2597), Infrastructure 924
  passed + 3 skipped (the existing Windows-only/symlink/case-sensitivity skips) of 927 (was 872 + 3 of 875), Api 694/694 (was 646),
  Architecture 9/9; frontend `npx vitest run` 103 files, 1394/1394 (was 1337), `npm run typecheck` clean, `npm run lint` 12 warnings (the
  baseline) and 0 errors with none in a file this slice created, `npm run build` clean (usual chunk-size notice), `npm run test:harness`
  19/19 (15 before this round plus the 4 retry regressions), `npm audit` 0 vulnerabilities; real-host Chromium `npx playwright test` with `reuseExistingServer: false`: 3/3 (manual intake with
  reload, labelled simulation demo, and the new generated-client wire specification), no `devalcopilot-e2e-*` directory left (the browser runs
  print SignalR "connection was stopped during negotiation" console lines on reload; their baseline was not separately measured);
  `dotnet list package --vulnerable --include-transitive` reports none; `git diff --check` clean (only git's CRLF notice for the generated
  client and the EF snapshot), no NUL byte and no trailing whitespace in the untracked files, a secret-pattern scan of the diff empty,
  Provenance of these results: the frontend, browser, harness, audit, solution-build, and generated-client results were rerun on the final tree
  after the second correction. The .NET test suites (Domain, Application, Infrastructure, Api, Architecture) and the formatter and
  vulnerable-package results are RETAINED from the first correction round: no backend or backend-test file changed afterwards (the
  second correction touched only `src/frontend/**/e2e` files and documentation), the solution rebuilt with 0 warnings and 0 errors, and the
  regenerated client hash is unchanged.
- Proof of the acceptance matrix (real SQLite files, deterministic doubles, no real provider): migration from the parent schema (historical
  v1/v2 implementation and correction rows stay null and read `NotRecorded`, no artifacts or messages created, unrelated saves preserve even a
  malformed out-of-band value, down drops only the column); both claims and both implementation forms, normalization (CRLF, NFD, trim),
  null/omitted, invalid text refused with no read, evidence call, or seal; duplicates and lost races; rollback and orphan-manifest cleanup
  after a commit race; exhaustion with and without an escalation and with an available authorization (nothing created or consumed, then an
  unguided retry still returns the escalation or consumes the authorization once); both feeds; the dispatch gate including a populated tracked
  context whose snapshot **or assignment tuple** is changed by another connection before the dispatch transaction (refused); malformed and
  incompatible snapshots beside healthy siblings; the actual adapters refusing every disagreement before any process (zero starts); hosted
  supervisors with the real adapters and a stdin-recording process double: exact accepted text once, unchanged source inputs, tool and
  permission arguments and working directory, validated result persisted, sealed replay of an undispatched claim after a restart despite a
  later request and a run change, a historical unguided implementation, and a historical authorized correction whose own `humanGuidance` is
  intact while a guided request at exhaustion is refused; HTTP endpoint tests (authentication, shapes, no echo, 400/409/413-class refusals,
  status/cockpit/evidence projections with the full correction lineage); projections for guided, unguided, legacy, malformed, incompatible,
  and non-mutation attempts without throwing; and the generated TypeScript client over real HTTP (above).
- Mutations and red/green evidence. Correction round, on the final code (each restored byte-identically, hash checked, then the affected suites
  rerun green): using the tracked attempt's tuple instead of the fresh one at dispatch failed 20 of 68 dispatch-boundary tests (the 18
  competing-assignment cases and the 2 pending-write cases); restoring the previous tolerant agreement check failed
  20 of 48 actual-adapter tests and 7 of 31 manifest tests. The R2 assertions (the removed `NotProvided`) are compile-level regressions: the
  previous classification cannot satisfy them. Retained from the first round (the code involved is unchanged since): disabling the exhaustion
  refusal failed 3 of 5 Application exhaustion tests and the 2 Api tests covering it; making the manifest check always succeed failed 16 of 24
  adapter tests and 6 of 14 hosted tests; replacing the fresh snapshot read with the tracked entity failed the 4 tracked-drift cases. Frontend
  (each restored, checked by hash; retained from the first round): removing the source from the request lifetime owner failed 3 tests; removing
  the draft-version check failed 2; removing the synchronous-duplicate guard failed 1; removing the editor's own identity check failed none (it
  is redundant with the render-time reset and the lifetime, and was kept as defense in depth).
- Formatter: `dotnet format DevalCopilot.slnx --verify-no-changes` is not clean — 153 findings in 17 files, the same totals as the recorded
  baseline, none in a new file (final tree). The five record declarations where a parameter was appended carry no inline comment on it, which
  keeps the totals at the baseline; five pre-existing findings sit on lines this change touched. The baseline findings were not fixed.
- Limits and open risks: doubles and jsdom do not prove real-provider reliability or that a provider honors guidance (the recorded fact
  states only what the host supplied); the sealed-manifest agreement is enforced at the invocation boundary, after dispatch was marked, so an
  out-of-band snapshot change ends as a failed attempt that consumed its slot rather than a never-dispatched one, and no protection is claimed
  against every hostile write after the dispatch boundary; a BLOB written out of band into the TEXT column is decoded as text by the driver and
  judged by the same exact rule (the storage class is not distinguished, unlike the turn-limit columns); the strict agreement now also refuses
  an unguided attempt whose sealed text is not a JSON object, which is the intended fail-closed behavior (every production manifest is a JSON
  object; only test fixtures were opaque); the real-HTTP specification inserts attempt rows with raw SQL into the run-owned database, so it
  proves wire serialization, revival, and error mapping, not that a provider ran; the 8 KiB body cap is new on the two existing endpoints; the
  correction status resolves a current attempt only through its review lineage, so a bare attempt shows no attempt (existing behavior; the
  status fact is proved with a full lineage); the frontend files were written by a delegated sub-agent and then reviewed and verified by the
  executor; read-only fetching was not changed; a manual run can remain nonterminal; generic chat, scheduling, automatic advancement,
  terminalization, pause/stop, budget overrides, fallback, account allowance, resume, Git publication, and CI remain out of scope. Not
  Increment 4 completion; no next slice is selected here.

## Manual Agent run intake with durable execution-mode isolation (2026-10-01)

- Scope and parent: the substantive change for the selected Increment 4 slice, prepared on parent
  `325316a00d31be0bd05dfacba7a6c073ff327251` (`main`, local and live `origin/main` matched at the start and after the first
  review correction round). Published: `506653539727799f87e15a85dab7db4f7fcbf419` (parent
  `325316a00d31be0bd05dfacba7a6c073ff327251`) was committed with the 116 reviewed files (66 modified tracked, 50 new; this file and
  the planner-owned `planner-handoff.md` with Codex's GO included unchanged), pushed as a normal fast-forward to `origin/main`, and
  verified with `git fetch origin main` and `git ls-remote origin refs/heads/main`: local `HEAD`, local `origin/main`, and the live
  remote all matched that SHA with a clean checkout. `git diff --cached --check` was clean before the commit.
- Delivered ([ADR-0014](../decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)): immutable
  `RunExecutionMode` (`Legacy = 0`, `Simulated = 1`, `ManualAgent = 2`) with migration `AddRunExecutionMode` (existing rows become
  `Legacy`, never inferred); the column is read as its exact stored form through the storage-class-preserving mapping, so only the
  exact integers 0, 1, and 2 are recognized (a REAL that truncates to a valid mode, an integer that overflows 32 bits, text, and BLOBs
  are unrecognized, refused by every check, never thrown on, and preserved by unrelated saves); protected `POST /api/runs/manual`
  (`CreateManualRun`) persisting only the `Created`/`Intake` run, its monotonic number, default budgets, and the intent event; one
  shared creation admission with serialization through the `Project.NextExecutionNumber` concurrency token; mode isolation at the
  claims (six Agent paths and four repair variants), feeds, commit seams, final dispatch, simulation, and Process; `CompleteSimulatedRun`
  requires a Simulated attempt; the project list stays available when a run has an unrecognized lifecycle (that project stays
  visibly blocked, its lifecycle never exposed or treated as terminal); mode and a creation hint in summaries and the cockpit; the
  frontend intake form (draft clearing bound to an edit version, so editing back to identical text cannot be cleared by an older
  completion), mode disclosure, and Agent actions withheld for Simulated/unrecognized modes; a fix for the real client reviving
  timestamps as `Date` objects that crashed the cockpit in a real browser; and a hardened browser harness (a run-owned root verified by
  a marker and token before any deletion, no working-directory fallback, a sanitizing authorized request helper, Playwright limited to
  `*.spec.ts`, `npm run test:harness`).
- Inventory of this change (the working tree against the parent, excluding `planner-handoff.md`): 65 modified tracked and 50 new
  files — backend 38 modified + 17 new (Domain 5 including the mode, admission, storage, and lifecycle-admission types; Application:
  creation recorder, `CreateManualRun`, `CurrentRunExecutionMode`, claim/feed/dispatch/simulation/summary handlers; Api endpoint and
  projections; Infrastructure mapping, `AddRunExecutionMode` migration and snapshot), tests 6 modified + 17 new, frontend 11 modified +
  10 new (including the regenerated `api-client.ts`), browser harness 4 modified + 5 new, documentation 6 modified + 1 new (ADR-0014).
- Checks run on the final tree: `dotnet build DevalCopilot.slnx` 0 warnings, 0 errors; `api-client.ts` deleted and regenerated by
  `dotnet build --no-incremental` byte-identical (SHA-256 `7253732aa5f7bd4c98198f31e2bb701c8c4b74ca2368c1d2c24d4ee78e67f2c8`);
  sequential `dotnet test --no-build`: Domain 839/839, Application 2597/2597, Infrastructure 872 passed + 3 skipped (the existing
  Windows-only/symlink/case-sensitivity skips) of 875, Api 646/646, Architecture 9/9; frontend `npx vitest run` 100 files, 1337/1337,
  `npm run typecheck` clean, `npm run lint` 12 warnings (the baseline) and 0 errors, `npm run build` clean (usual chunk-size
  notice), `npm run test:harness` 15/15, `npm audit` 0 vulnerabilities; real-host Chromium `npx playwright test` with
  `reuseExistingServer: false`, after the harness tests passed: 2/2 (manual intake with reload, and the labelled simulation demo),
  with no `devalcopilot-e2e-*` directory left in the temp directory; `git diff --check` clean, 50 untracked files without trailing
  whitespace or NUL, a secret-pattern scan of the diff empty, documentation links resolve. Dependencies did not change (only a
  `package.json` script was added), so the earlier `dotnet list package --vulnerable` result (none) is retained.
- Mutations run on the final tree (each restored, then the affected suites rerun green, 310 tests): removing the simulation feed
  filter and claim gate fails the feed tests and both hosted proofs; removing the final dispatch gate fails 19 tests; removing the
  `NextExecutionNumber` concurrency token fails the 3 deterministic competing-commit tests (the 8-way parallel test still passes
  because SQLite serializes writers); widening the stored-form read fails 12 malformed-storage tests (a narrow probe, not a proof
  of every coercion). Retained from earlier in this round, on the working tree before the final full-suite rerun: the malformed-storage
  regressions failed 165 of 274 against the code before the correction (two literals that SQLite numeric affinity stores as the
  integer 2 were then removed from the malformed set); the unrecognized-lifecycle endpoint regression fails when the summary lifecycle
  filter is removed; the draft-version regressions fail when the version check is removed; the sanitization regression fails when the
  raw transport error is rethrown.
- Formatter: `dotnet format DevalCopilot.slnx --verify-no-changes` is not clean — 153 findings in 17 files — and none of those files
  is touched by this change (the findings are pre-existing and were not fixed). A comparison against a parent worktree was inconclusive
  (checkouts with different line endings produced different counts) and is not used as evidence.
- Limits and open risks: doubles and jsdom do not prove real-provider reliability; the real host in the browser run still executes its
  capability probes (`--version` of installed tools) and creates empty scratch directories under `%LOCALAPPDATA%\DevalCopilot\agent-scratch`
  and may read artifacts and workspaces there, which is not configurable without a production hook (the proof creates no artifact or
  workspace, and the database is run-owned); the review established early-admission and disclosure defects, not a provider invocation
  bypass, and the separate SQL and dispatch defenses are retained; a manual run can stay nonterminal, which blocks further intake for
  that project until a later decision adds completion or replacement authority; a `Legacy` run keeps every historical capability; the
  harness ownership proof covers the temp-directory root, marker, and token, not hostile concurrent writers. Not Increment 4 completion;
  no next slice is selected here.
- Post-publication verification of `506653539727799f87e15a85dab7db4f7fcbf419` (`--no-build --no-restore` for tests, sequential): `dotnet build
DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; Domain `RunExecutionModeTests` 32/32; Application
`ExecutionMode|RunIntentCreation|StartSimulatedRun` 381/381; Infrastructure `AddRunExecutionModeMigrationTests|ExactStoredIntegerTextStorageTests|
AddClaudeMutationTurnLimitMigrationTests` 183/183; Api `CreateManualRunEndpointTests|ManualRunHostedTests` 25/25; Architecture 9/9; none
skipped. Frontend: `npm run test:harness` 15/15, `npx vitest run` 100 files 1337/1337, `npm run build` clean (usual chunk-size notice),
`npm run lint` 12 warnings (the baseline) and 0 errors, then `npx playwright test --reporter=line` 2/2 with `reuseExistingServer: false`,
normal authentication, and the run-owned fixtures (it ran without the Windows Event Log blocking seen in the review environment); no
`devalcopilot-e2e-*` directory remained in the temp directory. `api-client.ts` SHA-256 stayed
`7253732aa5f7bd4c98198f31e2bb701c8c4b74ca2368c1d2c24d4ee78e67f2c8`, `git diff --check` was clean, and the checkout was clean after the
checks. Formatter cleanliness and real-provider reliability are not claimed. No next slice is selected here.

## Current checkpoint (2026-09-30)

- Earlier published delivery: `a75d524b42306818acd139a4d00f58234d0e29d5` (parent `c662a4d0915fbf4ece0304bcfd58b17eab964778`) is
  the factual closure of the Claude mutation turn-limit slice (its delivered SHA and post-publication checks; no code or
  product contract change). At the start of this slice, `main`, local `origin/main`, and the live remote matched it, nothing
  was staged or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was modified.
- Published delivery: `9d583d57a025f034f4fd2715f2c712a8f24f0072` (parent `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6`)
  was committed with the reviewed run-isolated asynchronous cockpit controls slice (42 modified and 6 new files, including
  this file, the cockpit specification, and `planner-handoff.md` with Codex's GO, plus the two factual corrections to this
  file that Codex authorized), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main`
  and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a clean tree. The
  staged diff was checked with `git diff --cached --check` (only git's CRLF notice for the generated client). Post-publication
  checks against that commit: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings,
  0 errors; frontend `npx vitest run` 96 files, 1285/1285; `npm run build` (including `tsc -b`) clean with the usual chunk-size
  notice; `npm run lint` 12 warnings and 0 errors (the baseline was 20; none in a file this slice created);
  `api-client.ts` SHA-256 `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386` unchanged; `git diff --check`
  and the checkout clean. Backend suites were not repeated (no backend file changed). Limits: frontend-only behavior verified
  in jsdom, not against a real provider or browser; lifetime and flow ownership are per hook or component instance; read-only
  fetching was not changed; layout-effect versus passive-effect timing was not distinguished under jsdom. No next slice is
  selected here; selection remains with Codex.
- Earlier published delivery: `85d0822bb4479a38c84aad56be48fb499e86ce88` (parent `a75d524b42306818acd139a4d00f58234d0e29d5`)
  was committed with the reviewed manual format recovery slice for the critical review, challenge resolution, and code
  review stages and its review corrections (56 modified and 43 new files, including this file and `planner-handoff.md`
  with Codex's corrected-diff GO), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin
  main` and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a clean tree.
  The staged diff was checked with `git diff --cached --check` (only git's CRLF notice for the generated client).
  Post-publication checks against that commit (.NET sequential, `--no-build --no-restore` for tests): `dotnet build
  DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings, 0 errors; Domain 807/807; Application
  `FullyQualifiedName~Repair` 271/271; Api (`Repair` plus the Claude critical review, challenge resolution, and
  implementation review hosted supervisor suites) 94/94; Architecture 9/9; frontend `npm test -- --run` 1052/1052,
  `npm run build` clean, `npm run lint` 20 warnings (the baseline count, all `react(set-state-in-effect)`); `api-client.ts`
  SHA-256 `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386` unchanged; `git diff --check` and the checkout
  clean; nothing skipped in these runs. Limits: no real-provider reliability is proven (process doubles only); the
  documented `dotnet format` whitespace findings in older record declarations remain and no formatter cleanliness is
  claimed; the Planner repair hook keeps its earlier stale-completion behavior; next-slice selection remains with Codex.
- Delivery, based on verified parent `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6` (branch `main`; `HEAD`, local and
  live `origin/main` matched it and only the planner-owned `planner-handoff.md` was modified at the start):
  **run-isolated asynchronous cockpit controls** (Increment 4 stabilization, frontend only), including three review-correction
  round after Codex's NO-GO. See [planner-handoff.md](planner-handoff.md) for the selection and
  ["Run-isolated asynchronous controls"](../product/run-cockpit-specification.md#run-isolated-asynchronous-controls) for the
  contract. No backend, endpoint, generated-client, schema, dependency, provider, budget, or ADR change; HTTP operations,
  serialization, validation bounds, safe error mappings, and authority wording are unchanged.
  - Reproduced red on published code first: (1) planning for A, switch, planning for B, then A completing set B's
    `requesting` to false while B was pending; (2) a turn-limit save error on A reappeared after A to B to A. Both are
    covered (green) by the shared suite below.
  - Mechanism: a small cockpit-owned set in `hooks/`. `useRunActionLifetime` gives every committed render that binds a run its
    own lifetime instance (activated and ended in an effect, never by render-time ref mutation). Handlers capture that
    instance, so a handler retained from an earlier render stays rejected after A to B to A (a new lifetime) instead of
    acquiring the new one; `begin(runId, control)` also rejects a foreign run id and a second in-flight submission of the same
    control, and `capture()`/`isActive()` are bound to the same instance. `useRunScopedAction` holds pending/error state tagged
    with the run, dropped when the lifetime ends, and resolves true only for an accepted request whose lifetime is still
    current; its local-validation path (`reportError`) is rejected for an obsolete lifetime and never releases a pending
    submission. `useOwnedFlow(runId, identity)` adds operation ownership for a component's multi-step flow (save, local updates,
    refresh): a flow's continuations stay valid only while the committed identity that owns the component's local state (run
    plus authoritative value, or the escalation) is current and no newer flow of the same control began, a change of identity
    during either the request or the refresh ends the flow, returning to an earlier identity is a new one, and a handler of an
    ended identity never supersedes the current flow. Lifetimes are ended in a layout effect so they end in the commit that
    replaces them. The six ordinary request hooks, the four repair hooks,
    `useAuthorizeReviewCorrection` (plain and guided) and the five setters are thin wrappers that keep their typed client
    calls, serialization, and safe error text; hook signatures now take the current run. The controls (Codex and Claude model,
    turn limit, token warning/stop) and the guidance entry additionally re-derive their selections, drafts, saved values, and
    validation/synchronization messages during render when their owning identity (run plus authoritative value, or the
    escalation) changes, so nothing is carried between runs even without the parent's `key`; the draft and saved value are
    updated as soon as the server accepted a change, so a draft typed while the refresh is pending is never wiped by it, and
    the guidance entry clears only the text that was accepted. A stale accepted request remains a real server operation; its
    completion is ignored, never cancelled, retried, or read as a refusal. The three repair hooks from the previous slice were
    folded into the common contract; their "older completion while a newer request is pending" test became a
    duplicate-submission plus remount test because a second in-flight submission of one control is now rejected.
  - Review correction (Codex NO-GO, three findings, all reproduced red first, 6/6 failing on the prior tree): (1) the stable
    lifetime API read the current lifetime at invocation, so a retained run A planning handler succeeded (with an API request)
    after A to B to A and a retained turn-limit save handler with an invalid draft cleared run B's pending flag; (2) without
    parent keys the Claude model control kept A's opus selection and the turn-limit control kept A's draft 5 over B's
    authoritative 7; (3) an older turn-limit Clear's deferred refresh cleared a draft entered after a newer Save. Fixed as
    described above; the earlier suites missed them because they exercised hooks through fresh handlers, always used the
    parent's key, and typed the next run's draft by hand instead of asserting the authoritative value.
  - Second correction round (Codex NO-GO, two findings, both reproduced red first): (1) an unkeyed guidance entry switched
    from escalation A (deferred accepted submission) to B, with the same text typed for B, had B's draft erased when A's
    result completed, because string equality and the parent's key were the only protection; the entry now binds the completion
    to its escalation lifetime and to a draft version bumped by every edit (including identical text), so it clears only an
    unedited draft of the current target and never touches the accepted server authorization; (2) an unkeyed turn-limit
    control whose Clear awaited a deferred refresh restored a stale warning when the authoritative request changed 3 to 7 in
    the same run, because `useOwnedFlow` checked only the run lifetime; flow ownership now follows the same committed identity
    the component resets by, and the Claude model, token warning and token stop consumers use it too. The earlier suites missed
    both because they changed the target or identity only together with a manual edit, or never changed the authoritative value
    while a flow was pending.
  - Third correction round (Codex NO-GO, one finding, reproduced red first, 3 of 4 new tests failing): the Codex model/effort
    control reset its local state by run plus authoritative pair but its Save and Clear continuations wrote after the await
    using only the run-scoped setter result, so an old accepted request completing after the authoritative pair changed in the
    same run reverted the saved label to the old pair (Save) or erased the new selections and label (Clear). Both continuations
    now use `useOwnedFlow(runId, identity)` like the other controls; an accepted request stays a real server operation. The
    earlier suites missed it because the Codex control was only tested for a run switch, never for an authoritative change in
    the same run while a request was pending.
  - Tests: `runScopedActions.test.tsx` runs one controlled-promise contract over 16 controls (195 tests: current success,
    safe refusal and non-echoed exceptions, late success/failure after a switch, run B pending while A completes, A to B to A,
    unmount/remount, duplicate submission, a same-run rerender with StrictMode, foreign run id and stale handler rejection
    preserving a current pending request, and two independent controls); `AsyncControlIsolation.test.tsx` uses real hooks in
    the token stop panel, turn-limit control, Claude and Codex model controls, and a guided authorization form without the
    parent's key, with distinct authoritative values per run; `OwnershipCorrections.test.tsx` holds the red/green regressions
    for the three findings plus older-flow/newer-flow cases for the token stop, Claude model, and guidance entry;
    `ContinuationOwnership.test.tsx` covers the second and third rounds (Codex Save and Clear with a changed authoritative pair, a return to the
    earlier pair, and a current unchanged success; guidance A to B, A to B to A, unmount and remount, identical-text
    edits and a clean current success; turn limit 3 to 7 during the refresh, during the API phase, returning to 3, and a
    same-run rerender that keeps a truthful failure; Claude model pair change; token warning and stop threshold change);
    `RunCockpitView.test.tsx` gains real-hook isolation tests for ordinary versus repair (CodeReviewer) and for the
    Implementer request across a run switch, each asserting pending controls, per-run API calls, and that the old run's late
    completion does not release the new run's control or refresh.
  - Mutation evidence reported by the executor: on the final reviewed tree, removing the Codex control's ownership checks failed 3 tests, and ignoring the authoritative flow identity failed 7. Earlier-tree runs are retained as earlier evidence, not final-tree reruns: shared lifetime 208 failures; removed flow ownership 5; removed identity reset 4; unguarded reportError 1; ignored authoritative identity 4; removed newer-flow check 3; removed guidance draft-version check 2. Each reported mutation was restored. Removing only the guidance lifetime check failed no tests in that round; it remains defense in depth alongside draft-version protection. Layout-effect versus passive-effect timing was not distinguished under jsdom.
  - Checks run on the final tree (all frontend; `npm` from `src/frontend/DevalCopilot.Frontend`): `npx vitest run` 96 files,
    1285/1285 (was 1052 at the parent); `tsc -b` clean; `npm run build` clean (the usual chunk-size notice); `npm run lint` 12
    warnings (was 20 at the parent; none in a file this slice created, the rest the older baseline warnings); `npm audit`
    0 vulnerabilities; `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false -m:1` 0 warnings/0 errors
    with `api-client.ts` SHA-256 unchanged (`4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386`); local
    document links resolve; `git diff --check` and a trailing-whitespace scan of the untracked files clean. Backend suites
    were not rerun: no backend file changed, so the published evidence of the previous slice still applies.
  - Inventory: 48 files against the baseline: 42 modified tracked files (including the planner-owned `planner-handoff.md`,
    not edited here, this file, and the cockpit specification) and 6 new (`useRunActionLifetime.ts`, `useRunScopedAction.ts`,
    `runScopedActions.test.tsx`, `AsyncControlIsolation.test.tsx`, `OwnershipCorrections.test.tsx`,
    `ContinuationOwnership.test.tsx`).
  - Open risks and limits: read-only status and timeline fetching was not changed, so it keeps its own (already
    run-tagged) handling; the lifetime and flow ownership are per hook or component instance, so two separate instances of one
    control would not share an in-flight guard (the cockpit mounts one of each); each lifetime is created with `useMemo`,
    which React may in principle recompute, in which case handlers of the discarded instance are rejected (fail-safe, never
    unsafe); ignoring a stale completion means a request accepted just before a switch shows its outcome only on the next
    status read of its own run; nothing here is verified against a real provider or a real browser beyond the jsdom tests.
- Delivery, based on verified parent `a75d524b42306818acd139a4d00f58234d0e29d5`: **manual format recovery for the
  remaining read-only collaboration stages** (Increment 4). See [planner-handoff.md](planner-handoff.md) for the selection
  and ["One manual format repair of the remaining read-only stages"](../architecture/agent-collaboration-protocol.md#one-manual-format-repair-of-the-remaining-read-only-stages)
  and ["One manual format repair of a critical review, challenge resolution, or code review"](../product/run-cockpit-specification.md#one-manual-format-repair-of-a-critical-review-challenge-resolution-or-code-review)
  for the contract. ADR-0004, ADR-0009, ADR-0010, ADR-0012, and ADR-0013 were read and remain intact; no ADR is added or
  reversed, and no dependency, migration, column, provider argument, permission, model control, budget, Git, or
  publication change was made. The existing Planner repair is behaviorally unchanged.
  - Behavior: a user can request **one** fresh, ordinarily validated invocation of the same read-only role after the
    latest attempt of that role failed with exactly `InvalidStructuredOutput`: the Claude **CriticalReviewer**
    (`…/agent-attempts/{sourceAttemptId}/critical-review-repair`), the Codex **Resolver**
    (`…/challenge-resolution-repair`), and the Codex **CodeReviewer** (`…/code-review-repair`). Each is a protected,
    bodyless MVC operation that sends one command through `IApplicationMediator` (`…Command.ForRepair(runId,
    sourceAttemptId)`, a `RepairSourceAttemptId` added to the three existing commands, whose target ids became nullable
    and are never supplied for a repair); the existing role-specific claim handlers derive the target from the source's
    persisted inputs. No generic workflow or repair framework was added. The repair neither transforms nor preserves the
    failed response and adds no challenge round, implementation authority, correction authorization, automatic
    follow-up, or success inference.
  - Source rule: `ReadOnlyFormatRepairPolicy` (Domain) pins the path's exact provider, role, response contract,
    expected message type, protocol `1.0`, `ReadOnly` profile, and current known v1 adapter contract and accepts only a
    `Failed`, dispatched, concluded `InvalidStructuredOutput` attempt with clean-exit process evidence and a well-formed
    assignment that is not itself a repair. `ReadOnlyFormatRepairSource` (Application) adds the cross-row facts: same run
    (unknown and foreign-run sources are the same fixed 404), no durable semantic message from the source (a source that
    recorded a result did not fail structurally), not already repaired, the run's latest Agent attempt, and the exact
    workspace, checkpoint, and fingerprint. An unreadable enum, assignment, process, or input row fails closed as
    `agent_attempts.repair_source_ineligible` without echoing a stored value; only `InvalidOperationException` from
    materialization counts as unreadable, so database and cancellation failures propagate.
  - Exact inputs: `ReadOnlyFormatRepairInputs` reads the source's recorded inputs from non-enum columns only and requires
    the contract's shape (one Proposal; the Proposal plus one or more Challenges in contiguous order; one ExecutionReport plus
    contiguous, distinct verification (command, execution) pairs). The Resolver derives the challenged review from the
    first Challenge's owner and requires the ordinary validation plus an **ordered** equality of the Proposal and
    Challenges; the CodeReviewer runs the ordinary report-chain and verification validation and requires the currently
    enabled/latest `Passed` selection to equal the recorded set exactly (a rerun, enabled-command change, reorder, or
    replacement is `agent_attempts.repair_source_inputs_mismatch`). Nothing is substituted, and the source rows are
    never authority on their own.
  - Claim and commit seam: every ordinary gate is unchanged and runs in the same order; an invalid repair source is refused
    before provider probing, Git capture, and manifest sealing (and before the running-attempt check, so the race loser is
    told "already requested"). At the durable boundary the source, exact inputs, report chain, verification selection,
    reviewed lineage, and execution context (run active, workspace ready, lease active, selected checkpoint still the
    latest) are **re-read inside the same short transaction after a guard statement has run in it**, before the linked
    Attempt, inputs, verification rows, artifact metadata, and lifecycle change are persisted and committed. The Resolver
    and CodeReviewer extend their existing transaction; the CriticalReviewer gains a repair-only transaction
    (`CommitRepairAsync`, with the stop-policy compare-and-set as its guard and the Claude preference read after it)
    while its ordinary request keeps its single concurrency-token save (the handler now takes an
    `IAttemptDurabilityProbe`). Observation worth recording: on SQLite, Microsoft.Data.Sqlite's default serializable
    transaction takes the write lock when it begins (a competing writer inside the seam blocked), so the change-before-begin
    seam is the real race window and is what the tests reproduce. The global unique index
    `ix_attempts_agent_repair_source` is the at-most-one backstop; no column, migration, default, or backfill exists. Refusal
    or rollback deletes the proven-orphan sealed manifest; cancellation propagates; an ambiguous save or commit is resolved
    by the independent durability probe (persisted → success, not persisted → cleanup, unresolved → manifest preserved and
    `attempts.persistence_unresolved`).
  - Manifest, dispatch, and result: each ordinary bounded manifest (both CodeReviewer forms) gains one fixed
    `formatRepairNotice` inserted before `untrustedEvidenceBoundary`; nothing of the source's output, diagnostic,
    artifact path, identity, or outcome is included and the schema is unchanged. `MarkAgentAttemptDispatched` fails
    closed with `agent_attempts.invalid_repair_link` (no provider process) when the repair's link, exact tuple, source, or
    either input identity is incoherent, without re-applying the claim-time "latest"/"no repair" tests; the ordinary
    duplicate-input classifications are unchanged and the Planner repair has no such gate. Restart replay uses the sealed
    manifest and claimed assignment; the unchanged adapters, arguments, parsers, and result handlers record either the
    ordinary validated result or the repair's own outcome (an invalid repair records no semantic message and cannot be
    repaired again).
  - Read model and cockpit: the three role status responses add `repairSourceAttemptId`/`repairSourceAttemptNumber`; the
    history entries and attempt evidence add the same two fields for every repair (including the Planner's), proved
    through scalar columns and null when the source cannot be proved (another run, not earlier, or unreadable). The
    generated client was regenerated by the build (not edited). Three repair hooks and panels
    (`ClaudeCriticalReviewRepairAction`, `ChallengeResolutionRepairAction`, `CodeReviewRepairAction`) mirror the Planner
    repair: suggestion only, fresh-invocation and normal-budget wording, pending/error handling, authoritative status
    refresh, no overlap with the ordinary request, run-switch reset, lineage after success or failure, and no claim that
    the source was fixed; the history panel shows a provenance line only when the source is proved.
  - Tests (process doubles, file-backed SQLite): `ReadOnlyFormatRepairPolicy` Domain tests; per-role Application suites
    (`CreateClaudeCriticalReviewRepairAttemptTests`, `CreateChallengeResolutionRepairAttemptTests`,
    `CreateCodeReviewRepairAttemptTests`) covering root/revised, first/second-round, and initial/correction inputs, the
    manifest shape, every source corruption (profile, version, tuple, process evidence, unreadable enums, semantic
    message), unknown/foreign/nonlatest/already-repaired/repair-of-repair/checkpoint mismatch, partial/reordered/extended/
    gapped/replaced inputs and verification changes, the count, time, and token gates, every commit-seam change
    (competing repair, newer attempt, input and verification changes, semantic message, new checkpoint, lease, run end,
    running process attempt, preference and stop-policy change) with complete rollback and orphan cleanup, real
    concurrent claims (exactly one repair), begin/commit/save failures, cancellation before and after commit, the
    unresolved-durability branch, and source deletion versus run cascade; `MarkAgentAttemptDispatchedRepairLinkTests`;
    `ReadOnlyFormatRepairResultTests` (ordinary valid results, invalid repair, no repair-of-repair, exactly-once
    escalation); `ReadOnlyFormatRepairLineageProjectionTests`; Api `RequestReadOnlyFormatRepairEndpointTests` (auth,
    identical 404, golden path, body ignored, at-most-one, non-echoing conflicts, status/history/evidence lineage) and
    hosted supervisor replay tests for all three roles and the second round (invalid source → repair → restart replay from
    the sealed manifest → ordinary result; an invalid repair; a dispatched repair interrupted by a restart is never
    re-invoked); and frontend hook, action, cockpit, and history tests.
  - Mutation evidence (each applied to the final code, failing the targeted tests, then restored byte-identically):
    comparing the CodeReviewer verification set as an unordered overlap failed 8 (rerun, reorder, disable, add, partial,
    and the seam variants); comparing the Resolver challenge set as an overlap failed 5; removing only the
    already-repaired check failed 7; also removing the latest-attempt check failed 14 (the unique index alone then
    classifies only some races); removing the CriticalReviewer in-transaction revalidation failed 8 seam tests; removing the
    dispatch link gate failed 28 of 33.
  - Checks run (final tree; affected tests first, .NET commands one at a time with `-m:1` and
    `-p:UseSharedCompilation=false`, no compiler lock hit): `dotnet build DevalCopilot.slnx --no-restore` 0 errors/0 warnings;
    Domain 807/807 (was 782); Application 2203/2203 before the correction round (was 1967); Infrastructure 867 passed, 3 skipped (the existing
    environment-gated symlink/reparse-point skips; no Infrastructure test was added or changed, and the model is unchanged
    so no migration exists); Api 621/621 (was 581); Architecture 9/9; frontend `vitest` 1042/1042 before the correction round (was 974), `tsc -b`
    clean, `npm run build` clean (the usual chunk-size notice), and `npm run lint` 20 warnings, the same count as the
    parent and none in a file this slice created or changed (all `react(set-state-in-effect)`-style warnings in older
    hooks); `api-client.ts` regenerated by the build (SHA-256
    `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386`, was `b5f82c9b…`) and byte-identical across repeated
    builds; `dotnet list package --vulnerable --include-transitive` and `npm audit` report nothing (no dependency
    changed); `git diff --check` clean (only git's CRLF-normalization notice for the generated client) and a
    trailing-whitespace scan of the untracked files clean; local links in the changed and handoff documents (137)
    resolve. `dotnet format --verify-no-changes` on the changed C# files reports whitespace diagnostics only in
    the record declarations whose parameters are separated by documentation comments (the six role-status
    query-result and response records and the history and evidence response records; the parent reports the same pattern for some of
    them) and at three lines of `CodeReviewContextManifestBuilder.BuildForCorrection` (a comment between `=>` and its
    body, unchanged code); formatting findings in the new files were fixed and they are otherwise clean. Baseline versus
    new: no failing test or warning is new; the 3 Infrastructure skips and the 20 lint warnings are the baseline.
    Two harness defects of my first tests, not code defects: the test helper seam hook ran after the transaction
    began (SQLite's write lock is taken at `BEGIN`, so a competing writer blocked for 30 s and failed), and the first
    corruption tests read tracked, stale entities; both were test-harness fixes.
  - Review correction (Codex NO-GO, same slice, still uncommitted): (1) the repair commit-seam revalidation in all three
    paths now reads every authority row untracked (`asNoTracking` on the shared ordinary validators, used only by the repair
    call sites; tracking is not disabled globally and the tracker holding the Run and pending claim writes is untouched).
    The earlier suites missed this because their seam tests either changed rows read through untracked snapshots or
    detached the context, so a stale tracked instance never mattered. Seven new regressions keep the claim context alive and
    populated and commit the competing change before BEGIN: owning challenged review failed, challenge actor provider and
    reply target changed, verification execution Passed to Failed, execution-report actor provider and implementer result
    checkpoint changed, and the critical-review proposal owner failed; each asserts the ordinary refusal code, no linked
    Attempt, input, verification or artifact rows, and orphan-manifest cleanup. Red evidence: with the repair call sites
    switched back to tracked reads, the six Resolver/CodeReviewer regressions failed 6/6 (the Critical one already read
    untracked and passed); restored and green. (2) The three repair hooks now use a request generation that is bumped by
    every request, run switch and unmount, and reset state on a run switch; a stale completion neither writes pending or
    error state nor calls the refresh. Stale completions cannot cancel a server request already accepted. Earlier hook tests
    covered only a single run switch and missed the stale-generation cases. Nine new hook tests (late success/failure
    across a switch, A to B to A, older completion while a newer request is pending; three per hook) and one cockpit test
    with the real hook failed 9/9 and 1/1 with the generation guards removed, and pass with them. (3) The five repair
    helper types moved to `Application/Features/Runs/Policies/FormatRepair` with matching namespaces and imports; no
    behavior change. Re-run after the correction: build 0 errors; Domain 807; Application 2210 (was 2203); Infrastructure 867
    passed, 3 skipped (baseline); Api 621; Architecture 9; frontend vitest 1052 (was 1042), `tsc -b`, `npm run build` clean,
    lint warnings unchanged with none in changed files; `npm audit` clean; `api-client.ts` SHA-256 unchanged
    (`4ac246f0…6386`). Inventory: 56 modified and 43 untracked files, none staged.
  - Open risks and limits: nothing proves real provider behavior; the three adapters are unchanged and tested with
    deterministic process doubles only, so a provider that fails again is simply an ordinary invalid attempt. The repair is
    a fresh invocation and cannot guarantee a valid response. A committed repair consumes the source's one repair even if
    it is interrupted or never dispatched. The seam tests reproduce the race by committing a change from an independent
    connection immediately before the transaction begins; the serialization that follows relies on SQLite's write lock as
    observed, not on a separate proof of the provider's documented isolation. `ReadOnlyFormatRepairInputs` bounds inputs at
    64 rows, far above the protocol's cardinalities. The hosted tests cover the Claude and Codex read-only roles; a mutating
    Implementer or ReviewCorrection attempt still has no repair. Planned post-publication verification: fetch and
    `ls-remote` to confirm local `HEAD`, `origin/main`, and the live remote agree on the delivered commit with a clean tree,
    then the focused Domain, Application, Api, and frontend suites, `api-client.ts` hash, and a solution build against that
    commit.
- Published delivery: `c662a4d0915fbf4ece0304bcfd58b17eab964778` (parent `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`) was
  committed with the reviewed optional Claude mutation agentic-turn-limit slice (61 modified and 46 new files, including this
  file and `planner-handoff.md`), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main`
  and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a clean tree. Before
  staging, only the XML `<para>` of `ClaudeMutationTurnLimit.cs` was corrected to describe the type-preserving representation
  (no executable code, test, wire contract, or migration changed). Staged `git diff --cached --check` was clean and local
  documentation links (143) resolved. Post-publication checks against that commit: solution build
  (`--no-restore -p:UseSharedCompilation=false -m:1`) 0 errors/0 warnings; Domain turn-limit and adapter-contract tests
  112/112; Application setter, both claims, dispatch, eligibility, status, history, evidence, and cockpit tests 437/437;
  Infrastructure adapters, migration, and exact-storage matrix 335/335; Api setter, malformed-storage and projection tests and
  both hosted mutation-supervisor suites, including sealed replay and failed-invocation recovery, 139/139; frontend `vitest`
  974/974 and `tsc -b` clean; no test was skipped in those runs; `api-client.ts` SHA-256
  `b5f82c9b030fa1259c5456628b2196fea28e578d4193a9333a27a6e2e8ed424e` unchanged and the working tree clean afterward. This
  closure records the delivered SHA and those checks only; no code or product contract changed after publication.
- Published delivery: `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944` (parent `2c0c1be7895f31399db11d6e2320fb089da39296`) is
  the factual closure of the changed-line sample slice (its delivered SHA and checks; no code or product contract
  change). At the start of this slice, `main`, local `origin/main`, and the live remote matched it, nothing was staged
  or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was modified.
- Current delivery, based on verified parent `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`: **optional, immutable Claude
  agentic-turn limit for initial implementation and review correction**. See [planner-handoff.md](planner-handoff.md)
  for the selection and ["Optional Claude agentic-turn limit for mutation attempts"](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts)
  and ["Optional Claude turn limit for implementation and correction"](../product/run-cockpit-specification.md#optional-claude-turn-limit-for-implementation-and-correction)
  for the contract. ADR-0004, ADR-0009, ADR-0010, ADR-0012, and ADR-0013 were read and remain intact (ADR-0009's flat
  immutable assignment facts on `Attempt` are extended, not replaced). No ADR is added or reversed, and no new
  dependency, Codex change, provider or model discovery, session, or account-allowance change was made.
  - Behavior: `Run.RequestedClaudeMaxTurns` (none, or 1 through 100; `ClaudeMutationTurnLimit` is the one rule) is set or
    cleared by the protected `POST /api/runs/{runId}/claude-mutation-turn-limit` (`{ "maxTurns": N | null }`, the member
    required; a strict converter rejects a missing or duplicate member, a string, a fraction or exponent, and overflow
    with HTTP 400; terminal run 422; unknown run 404; competing change or lifecycle race 409). One `SaveChangesAsync`
    persists the Run change and a human-authored `run.claude_mutation_turn_limit_changed` event, including for a
    same-value set, with no provider call. The one additive migration `AddClaudeMutationTurnLimit` adds nullable INTEGER
    `runs.RequestedClaudeMaxTurns` and `attempts.AgentRequestedMaxTurns` with no default and no backfill. Both mutation
    claim handlers take the limit through `CurrentClaudeMutationTurnLimit` as a late fresh read after Git evidence and
    manifest sealing, guarded through the Run's concurrency token, refuse a malformed stored value with
    `agent_attempts.claude_turn_limit_invalid` (manifest removed, authorization unconsumed), and roll back the attempt,
    inputs, artifact, authorization consumption, and event on a racing change. Every new initial implementation claims
    `claude-implementation-v2` and every new correction `claude-review-correction-v2`, even with a null limit; the
    Domain factories reject a limit beside v1, an unknown version, or another permission profile, and the v1
    convenience overloads and stored v1 history are unchanged. The eligible queries project the attempt's own limit and
    contract version into the two invocation requests, so dispatch and undispatched restart replay never read the Run.
    `ClaudeMutationTurnLimitArguments` appends exactly `--max-turns` and the invariant-culture integer after the model
    and effort arguments, nothing for a null request, and fails closed before any process starts for an invalid value
    or a version other than the path's own exact v2; `CriticalReviewer` keeps exactly `--max-turns 1` and no Codex path
    changed. A cap or unsupported-flag error is the ordinary failed invocation (no inferred "limit reached", no retry
    without the flag, no extra budget slot), and both supervisors still capture fresh post-invocation Git evidence,
    flag a suspected mutation or unavailable evidence `NeedsAttention`, and record no success from failed output.
    The status views accept exactly the v1 and v2 versions of their own path for the existing configured facts, and
    both role statuses, the cockpit, and historical evidence expose `{state, maxTurns}` facts (`Requested`,
    `NotRequested`, `NotRecorded`, `Unknown`; legacy null is never an observed unlimited capacity). The cockpit adds
    a "Claude turn limit" control (request wording, local validation, pending/conflict/error messages, authoritative
    refresh, run-switch reset) and the attempt and run facts in the latest attempt, both role blocks, and the history
    view; the generated client was regenerated by the build, not edited.
  - Correction round (review NO-GO on three findings): (1) The invocation request carries only the cap and the contract
    version, so a cap beside an incompatible persisted profile or provenance was still eligible and dispatchable.
    `ClaudeMutationAdapterContract.IsDispatchCoherent` now requires, for any attempt that recorded a request, a
    well-formed request and the Claude provider, the Implementer role, a mutation response contract, `WorkspaceEditOnly`,
    and that path's exact v2 contract; it is applied in both eligibility feeds and in the authoritative
    `MarkAgentAttemptDispatched` gate (`agent_attempts.invalid_agent_contract`), so no provider process starts. An
    attempt that recorded none (coherent v1/null and v2/null) is unaffected and restart replay still uses stored values.
    (2) The turn-limit columns were mapped as `int?`, so SQLite truncated `3.5` to a valid `3` and `4294967297` overflowed
    during materialization, breaking the whole candidate query. Both columns are now field-only EF properties holding the
    exact stored text in the same INTEGER-affinity column (no schema change), read strictly by
    `ClaudeMutationTurnLimit.Read` (canonical whole number 1 through 100 only); anything else is malformed, never
    truncated, clamped, or read as null or zero, and never breaks a healthy sibling. The accessors return a valid request
    or none and throw for a malformed one; `ReadRequestedClaudeMaxTurns` and `ReadAgentRequestedMaxTurns` expose the
    state. A malformed Run value is refused at the claim, `Unknown` in the status and cockpit, preserved exactly by
    unrelated saves, and replaced by setting a valid request (the stored text is the concurrency-token original). A
    malformed Attempt snapshot is never dispatched, fails a role status with `agent_attempts.invalid_assignment`,
    invalidates its history identity, and is `Unknown` in the cockpit. No `CHECK` constraint was added: the read and
    dispatch boundaries are authoritative and are what the corrupted-row tests exercise, and a later constraint would
    need a table rebuild. (3) Wording: the evidence enum, the API response documentation, the protocol, and the cockpit
    specification no longer say a `Requested` number "was passed"; they state the saved or snapshotted request (which
    also describes an undispatched attempt and a Run with no attempt), and the versioned adapter's argument behavior is
    described separately.
  - Second and third correction rounds (review NO-GO on the storage boundary, twice): (1) The string mapping lost SQLite's
    storage class: `GetString` decodes a BLOB (`X'37'`) into `"7"` before `ClaudeMutationTurnLimit.Read` validates its
    digits, so a digit BLOB was read as the request `7` by both eligibility feeds and passed `MarkAgentAttemptDispatched`.
    The first fix tagged only BLOBs with an unescaped `blob:` prefix, which was still ambiguous: an actual TEXT `blob:37`
    and the BLOB `X'37'` both read as `blob:37`, the TEXT original was rebound as a BLOB (false concurrency), TEXT
    `blob:ZZ` threw during parameter preparation, and a REAL `+Infinity` was read and rebound as text. The two columns now
    use `ExactStoredIntegerTextTypeMapping` (these two columns only) with a type-preserving, disjoint representation: an
    `integer` is its canonical digits (the only form accepted as a request, and only 1 through 100); a `real` is `r:` plus
    the 16 hex digits of its exact IEEE-754 bits (finite values, both infinities, and negative zero without decimal
    rounding); a `blob` is `b:` plus its exact hex bytes; an actual `text` is `t:` plus the text verbatim, so TEXT that
    resembles any other form is still TEXT; any other class is `?:` plus its type name. Binding the original back uses its
    actual type and content (INTEGER, a double from its bits, BLOB bytes, TEXT after its tag), infers nothing from untagged
    text, and never throws on a malformed suffix (an ill-formed string is bound as plain text); the SQL literal form uses
    the same decoding. Claims (late read and guard), both eligibility feeds, the dispatch gate, the status, history,
    evidence, and cockpit projections, and Run intent all read through it, so every non-integer class is malformed
    everywhere: a Run claim is refused with `agent_attempts.claude_turn_limit_invalid` (manifest removed, authorization
    unconsumed), an Attempt is never dispatched and the provider is invoked zero times, a healthy sibling is unaffected, and
    a tampered Run row still accepts unrelated saves, a repair by a protected set or clear, and a genuine concurrent-change
    refusal without a false one. A whole-valued REAL is converted to INTEGER by the column's own affinity at write time, so
    it is an integer by the time it is read (for example `-0.0` and `1.0`). No storage `CHECK` constraint was added (the
    type-preserving read makes the boundary authoritative without rejecting the out-of-band writes that the regression tests
    use to prove it). (2) `GetEligibleReviewCorrectionAttempts` supplied the constant `ClaudeCode` to `IsDispatchCoherent`
    without reading the persisted provider; it now projects the attempt's actual provider (as a SQL comparison, so an
    unreadable provider string cannot break the query), so a cap-bearing correction attempt that persisted another
    provider is excluded by the feed as well as by the authoritative gate. Historical eligibility of an attempt with no
    request is unchanged (no new filter).
  - Red evidence for the second and third rounds (each captured before the change, then green): the digit-BLOB cases and
    the correction provider case failed 6 of 56 Application tests (both feeds, `MarkAgentAttemptDispatched`, both claims,
    and the correction authorization claim), 2 of 17 storage tests, and 7 of 48 Api tests (history, cockpit, status, the
    repair through the API, and both hosted supervisors). For the representation, the new storage matrix against the
    previous marker scheme failed 38 of 131 (TEXT `blob:37`, `blob:`, `blob:ZZ`, `blob:0g` in the repair, unrelated-save,
    clear, and concurrent-change cases; REAL `+Infinity` and `-Infinity` in the same cases; and the representation and SQL
    literal expectations). After the change all pass: the matrix covers TEXT that resembles markers with valid, invalid,
    and empty hex suffixes, finite, high-precision, and non-finite REALs, integers and null, and digit, empty, arbitrary,
    and marker-spelled BLOBs, with storage class and content read directly from SQLite after an unrelated Run save, a
    successful set and clear repair, and a concurrent change; the same matrix of values runs against the Attempt column,
    both claims, both feeds, the dispatch gate, the hosted supervisors, and the projections. Mutations of the final code,
    each failing the targeted tests and then restored: the correction feed using the constant provider again failed 1;
    plain `GetString` failed 5; reading a REAL as decimal text failed 11; leaving TEXT untagged failed 15; binding the
    original back as untyped text failed 76.
  - Red evidence for the correction (captured against the pre-correction code, then green): the new dispatch-boundary
    and claim theories failed 34 of 49 (every profile, provenance, and non-integer case through both feeds and
    `MarkAgentAttemptDispatched`, and `3.5` and `4294967297` through both claims), matching the reported defects. After
    the correction all pass. Mutations of the corrected code, each failing the targeted tests and then restored:
    `IsDispatchCoherent` always true failed 28 of 49; removing only the `MarkAgentAttemptDispatched` check failed 28;
    removing only the eligibility filter failed 14; a lenient truncating storage read failed 5. The first submission's
    mutations (supervisor passing null, claim snapshotting null, adapter omitting the flag, eligible query dropping the
    limit, reading the early tracked value or not resetting the original value) still apply; removing only the
    `IsModified` line fails nothing because the other claim guards already force the same Run UPDATE and the original-value
    reset is the operative guard. The first submission's tests were written after the implementation; the strict
    request converter exists because the first endpoint test run found string `"5"` and duplicate members accepted.
  - Checks run (final tree): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0
    warnings; Domain 782/782 (was 670); Application 1967/1967 (was 1783); Infrastructure 867 passed, 3 skipped (the
    existing environment-gated skips; was 625); Api 581/581 (was 464); Architecture 9/9; frontend `vitest` 974/974 and
    `tsc -b` clean on the final tree (no frontend file changed in either correction round; the first submission's
    `npm run lint` showed 20 warnings, all in files this slice did not touch, and `npm run build` was clean);
    `api-client.ts` regenerated by the build (SHA-256 `b5f82c9b…`, was `b3e1c836…`) and byte-identical across repeated
    builds and both corrections; no dependency changed, so the earlier `dotnet list package --vulnerable
    --include-transitive` and `npm audit` results (nothing found) are unchanged; `git diff --check` clean; local links in
    the changed and handoff documents (143) resolve. Affected tests were run first and .NET commands ran one at a time (a
    first build after a change sometimes hit the known `csc` file lock; a rebuild cleared it and no stale binary was
    tested). `dotnet format --verify-no-changes` on the changed C# files reports whitespace diagnostics only in four record
    files with documentation comments between parameters, a pattern it also reports for two of them at the parent; no
    other file is reported. An Api disclosure check in the malformed-storage tests had matched short values (a GUID can
    contain `abc`); it now applies only to long values.
  - Open risks and limits: nothing here proves real provider enforcement: the `--max-turns` argument follows the official
    CLI reference and headless contract, but no installed-version or authenticated run was observed, and the adapters
    are tested with deterministic process doubles only. A provider that **rejects** the flag (an unsupported or invalid
    argument) produces the ordinary failed invocation and is recorded as such; a provider that **ignores** it is neither
    rejected nor detected, because no turn count is observed and no limit-reached or ignored-flag classification
    exists, so enforcement of an accepted flag is not proven. The limit applies only to claims made after it is set;
    the turns actually used are not measured; it is a request for a provider-loop guardrail and not a token, cost,
    account, or host-enforced ceiling. A setting race surfaces as the existing generic
    `agent_attempts.run_changed_during_claim` conflict, not a turn-limit-specific one; the commit-time guard relies on
    the Run row's concurrency tokens and was proven at a deterministic seam, not with a truly interleaved commit. The
    request is one value shared by both mutation paths. The `ClaudeMutationTurnLimitRequestConverter` applies to this
    one request type only, so the other setters keep their existing lenient parsing. An attempt whose cap-bearing
    provenance fails the dispatch gate stays claimed and undispatched (the same handling as the existing invalid
    agent-contract refusal) until an operator resolves it; no new recovery authority was added. The columns are not
    protected by a storage `CHECK` constraint, so a row written out of band is handled at read and dispatch time, not
    prevented. The cockpit shows a saved run request that can differ from an earlier attempt's immutable fact by design.
  - Post-publication verification (completed; see the published-delivery entry above): rerun the focused turn-limit Domain, Application,
    Infrastructure adapter, storage and migration, Api endpoint, malformed-storage and hosted supervisor, and frontend
    turn-limit tests, and confirm the generated client is unchanged, against the delivered commit.
- Published delivery: `2c0c1be7895f31399db11d6e2320fb089da39296` (parent `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`) was
  committed with the reviewed bounded changed-line sample slice (7 modified, 2 new files, including this file and
  `planner-handoff.md`), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main` and
  `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a clean tree.
  Staged `git diff --cached --check` was clean and local documentation links (111) resolved; no code, test, or document
  changed between the reviewed diff and the commit. Post-publication checks against that commit: solution build 0
  errors/0 warnings; Application `TrackedDiff` tests 240/240; hosted critical-review supervisor tests, including the
  sealed restart replay, 17/17; no test was skipped in those runs; `api-client.ts` SHA-256 `b3e1c836…` unchanged and
  the working tree clean afterward. This closure records the delivered SHA and those checks only; no code or product
  contract changed after publication.
- Published delivery: `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54` (parent `b19413fac3f82f400c00ea6ab787d45c2898f1ae`) is
  the factual closure of the tracked-hunk evidence slice (its delivered SHA and checks; no code or product contract
  change). At the start of this slice, `main`, local `origin/main`, and the live remote matched it, nothing was staged
  or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was modified.
- Current delivery, based on verified parent `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`: **bounded changed-line
  samples for oversized tracked hunks**. See [planner-handoff.md](planner-handoff.md) for the selection and the
  ["Bounded tracked-hunk evidence in Agent manifests"](../architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests)
  section (bullet "Changed-line samples of oversized hunks") for the contract. No ADR, migration, API or generated-client
  change, Git call, capture command, fingerprint, changed-path list, provider, permission, budget, claim, or frontend
  change.
  - Defect shown first: a valid text hunk whose header plus hunk exceeds 8 KiB was reported `hunk_too_large` and
    contributed no text anywhere. With the new section unwired, 93 of the 115 new cases failed (the 22 that passed are
    negative cases); wired, all pass.
  - Behavior: new `TrackedDiffSampler` (`Features/Runs/Policies`), called by `ChangeEvidenceManifest` for all seven builder
    entry points, builds `diffSelection.samples` from the already parsed diff only. `diff` still holds only complete
    headers and hunks and `includedHunks` counts only whole hunks; a small or fully supported diff, an empty diff, an
    absent diff, and any manifest without an eligible hunk are byte-identical to before (no `samples` member).
    Eligible: a recognized text hunk with header + hunk > 8 KiB, independent of the fitting step; binary,
    metadata-only, malformed, unsupported, unrecognized, and merely unselected (`diff_budget`) hunks never sample.
    Each sample carries `path`, 1-based `hunk`, `changedLines {total, shown}`, and actual `+`/`-` lines
    (`side`, `text`, `shortened`, `originalBytes` if cut, `noNewlineAtEnd`); the section states `complete: false`,
    `patch: false`, a fixed incomplete/non-patch/untrusted notice, its limits, and `hunks {eligible, sampled,
    unsampled}`. Limits: 16 sampled hunks, 8 lines per hunk (removed and added alternate, shown in original order),
    192 UTF-8 bytes per line cut at scalar boundaries, and 4 KiB for the whole serialized section. Hunk slots and
    then lines are granted round-robin across files; a hunk that gets no line counts as unsampled. Fitting steps now
    carry a sample budget (4, 4, 2, 1, 0, 0 KiB); at 0 the section is counts-only with `manifest_budget`.
    Protocol and `engineering-context.md` updated.
  - Checks run (final tree): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0
    warnings; Domain 670/670; Application 1783/1783 (was 1668); Infrastructure 625 passed, 3 skipped (unchanged symlink
    skips); Api 464/464 (hosted critical-review supervisor tests 17/17, including the sealed restart replay, which now
    also asserts the sealed sample); Architecture 9/9; frontend not touched, so not run; `api-client.ts` SHA-256
    `b3e1c836…` unchanged; `git diff --check` clean; local links in the changed and handoff documents (117) resolve
    (ad hoc script). Focused `TrackedDiff` tests (240) were run first.
  - New tests: `TrackedDiffSampleTests` (across all seven entry points: large single hunk, huge early line beside a
    later file, multiple files and hunks with ordinals, additions/deletions/both sides, Unicode and JSON escaping with
    scalar-safe cuts, no-newline markers, round-robin fairness, the 16-hunk cap, the aggregate byte limit, determinism,
    small and complete-hunk compatibility, the 8 KiB eligibility boundary at exactly 8192/8193 bytes, binary/
    malformed/unsupported/unrecognized exclusion, tight fitting across 47 paddings, the mandatory-only oversize
    result with counts, and the untrusted boundary); a real-Git assertion in `TrackedDiffRealGitTests` that the staged
    300-line rewrite yields a sample whose lines appear verbatim in the captured diff; and the hosted restart replay.
  - Mutation checks (each failed the targeted tests, then restored): hunk slots in diff order instead of round-robin
    failed the fairness test (7); appending sample text to `diff` failed the real-Git and multi-hunk tests; removing
    the aggregate budget check failed the tight-fitting and fairness tests; removing the per-line limit failed the
    huge-line and Unicode tests.
  - Open risks and limits: the counts-only form adds about 90 bytes, so a claim whose manifest already sat within that
    of 32 KiB would now get the existing `context_manifest_too_large` refusal; with many eligible hunks the 4 KiB
    bound means some hunks get no line (counted as unsampled) and later lines of a hunk may be cut short; a hunk's
    first changed lines (not the most relevant) are sampled; a line is shown without context or hunk header, so its
    position inside the hunk is not stated; the parser's reliance on hunk counts is unchanged, so a self-consistent
    but misleading diff is still only untrusted evidence.
  - Post-publication verification (completed; see the published-delivery entry above): rerun the focused tracked-diff, sample, real-Git, and
    hosted restart-replay tests against the delivered commit.
- Published delivery: `b19413fac3f82f400c00ea6ab787d45c2898f1ae` (parent `882c707252a3ea6da1302b67bbe65eefb36c331b`) was
  committed with the reviewed bounded tracked-hunk evidence slice (11 modified, 1 deleted, 14 new files, including this
  file and `planner-handoff.md`), pushed as a normal fast-forward to `origin/main`, and verified with
  `git fetch origin main` and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that
  SHA with a clean tree. Staged `git diff --cached --check` was clean and local documentation links (117) resolved; no
  code, test, or document changed between the reviewed diff and the commit. Post-publication checks against that
  commit: solution build 0 errors/0 warnings; Application tracked-diff parser, selector, builder, short-input, and
  real-Git tests, the untracked-manifest tests, and the five handler test classes plus review-correction guidance tests
  420/420; hosted critical-review supervisor tests, including the sealed restart replay, 17/17; no test was skipped
  in those runs. This closure records the delivered SHA and those checks only; no code or product contract changed
  after publication.
- Published delivery: `882c707252a3ea6da1302b67bbe65eefb36c331b` (parent `6367e359674799fb1f41d8a919c61ed56f15b4cd`) is
  the factual closure of the untracked-file-context slice (its delivered SHA and checks; no code or product contract
  change). At the start of the next slice, `main`, local `origin/main`, and the live remote matched it, nothing was
  staged or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was modified.
- Current delivery, based on verified parent `882c707252a3ea6da1302b67bbe65eefb36c331b`: **bounded tracked-hunk
  evidence across Agent manifests**. See [planner-handoff.md](planner-handoff.md) for the selection and
  ["Bounded tracked-hunk evidence in Agent manifests"](../architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests)
  for the contract. No ADR is added or reversed; no migration, API route or contract, generated-client, Git capture
  command, fingerprint, changed-path list, claim-eligibility, provider, permission, budget, or frontend change.
  - Defect proven first: the five manifest builders copied `CompleteDiff[..8192]` (UTF-16 characters). A deterministic
    fixture (`TrackedDiffPrefixDefectTests`) with a large first file and a later small change failed against the old
    code (`Not found: "+new value"`), and a real disposable worktree (`TrackedDiffRealGitTests`) shows the first 8,192
    characters contain neither the later file's header nor its change. Both now pass, and the fixture test remains as
    the regression.
  - Correction round (review NO-GO on the small-diff shortcut): the exact-string path had required only a size within 8 KiB
    and no binary marker, so a short non-header string, a small block with a malformed hunk, an unsupported header
    line, or an unparseable path was inlined verbatim and marked `diffTruncated: false`, i.e. complete. It now requires an
    empty diff or a recognized parse in which every file is a supported text or metadata-only block with no binary patch.
    `TrackedDiffShortInputTests` covers the four short cases across all seven builder entry points (28 cases) and the
    valid small text plus metadata-only diff (7); before the fix the 28 failed and the 7 passed, and all 35 pass now.
    Application is now 1668 (35 more); Domain, Infrastructure, Api, Architecture, the generated client, links, and
    `git diff --check` were rerun on the final tree with the results below.
  - Behavior: one shared policy in `Features/Runs/Policies` (`ChangeEvidenceManifest`, `TrackedDiffParser`,
    `TrackedDiffSelector`, and small records; the previous slice's `UntrackedFileManifestSection` moved there from the
    feature root and now only shapes the untracked section) replaces the prefix in all seven builder entry points
    (critical review, challenge resolution, implementation accepted and revised, implementation review and its correction
    variant, review correction). It reads only the captured `CompleteDiff` and `ChangedPaths`. An empty diff
    stays `""` and an absent diff stays `null`; a non-empty diff keeps its exact string, `diffTruncated: false`, and
    the historical member set only when it fits 8 KiB of UTF-8 and its parse is recognized with every file a supported
    text or metadata-only block and no binary patch. Otherwise the diff is split into blocks and whole hunks (boundaries are
    certain because content lines carry a prefix, and a hunk ends where its header counts are consumed; a
    `\ No newline at end of file` marker stays in its hunk), and complete headers and hunks are selected in rounds, at
    most one further hunk per file per round, in original order, within 8 KiB of UTF-8, so no hunk and no scalar is
    split, every file gets a first hunk before any gets a second, and an oversized hunk never blocks a later small one.
    `diffTruncated` is true and `diffSelection` states `complete: false`, that the text is not the full or an
    applyable patch, file and hunk counts, and `items` for every partial or absent file with a fixed reason
    (`hunk_too_large`, `diff_budget`, `binary`, `unsupported_format`, `malformed_hunk`, `header_unparseable`,
    `manifest_budget`), never source text, a payload, a path in a reason, or an exception. Metadata-only changes are
    included whole; binary payloads are never sent; quoted (octal-escaped) paths decode one way; only `a/` and `b/`
    prefixes with equal paths are accepted, and anything else is reported, not guessed. A text that does not start with
    a git file header, even a short one, is `unsupported_format` with `diff: null`, and a short block with a malformed
    hunk, an unsupported header line, or an unparseable path is never inlined as an exact complete string: it is a
    recognized, omitted file item with its fixed reason. Fitting measures the serialized manifest against 32 KiB
    and reduces tracked hunks and untracked previews together (8/16, 8/8, 4/4, 2/2, 1/1, 0/0 KiB), then replaces tracked
    `items` with counts, then the untracked section with its omitted-file count; plan/review inputs, all changed
    paths, and the accounting are never dropped, and if they alone exceed 32 KiB the builder returns them and the
    handler's existing `context_manifest_too_large` refusal applies.
  - Checks run (final tree): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0
    warnings; Domain 670/670; Application 1668/1668 (was 1543); Infrastructure 625 passed, 3 skipped (unchanged: the
    two existing symlink skips and the previous slice's file-symbolic-link case, because this host cannot create a
    file symlink without elevation); Api 464/464 (was 463); Architecture 9/9; frontend not touched, so no frontend
    check was run; `api-client.ts` byte-identical after the build (SHA-256 `b3e1c836…`, unchanged); `git diff --check`
    clean; local links in the changed and handoff documents resolve (script, including the new anchor). Focused tests
    were run first.
  - New tests: `TrackedDiffEvidenceTests` (parser: empty and unrecognized text, whole-hunk splitting with exact
    round trip, missing-final-newline markers on either side, binary/mode-only/empty-file classification, quoted
    octal, space, and tab paths, six malformed or unsupported forms with fixed reasons, header-looking body lines;
    selector: oversized-first-hunk, first-hunk-per-file fairness, whole-hunk/ordered/deterministic/UTF-8 byte bound
    at five budgets, a hunk crossing the old cutoff, nontext accounting; and, for all seven builder entry points,
    small-diff exact compatibility, empty and absent diffs, large-first-file with omissions reported, non-ASCII hunks
    and quoted paths, unsupported format, coexistence with untracked previews, tight fitting across 25 paddings with
    mandatory inputs and every accounting entry retained and both tracked and untracked text reduced, a mandatory-only
    manifest that is returned oversize for the handler's refusal, determinism, and repository text only under
    `changeEvidence`); `TrackedDiffRealGitTests` (a disposable worktree with staged and unstaged changes, a large
    first file, a later small change, a non-ASCII quoted path, a path with a space, a missing final newline, a binary
    file, a mode-only change, and an untracked file: identical fingerprint and diff for identical input and equal to an
    independent `git diff` run, the old prefix defect, parse without unsupported files, the selection, and byte-identical
    manifests across captures); and one hosted test through the real command chain and artifact store in which a fresh
    container (a host restart) with different Git evidence dispatches the sealed selection, whose hash equals the
    artifact row recorded before the restart. The previous slice's builder test was updated only for the new namespace
    and shared variant table.
  - Mutation checks (each failed the targeted tests, then restored): letting a hunk be cut at the byte budget failed
    dozens of tests (all seven builders' large-file, non-ASCII, coexistence, and boundary tests, the selector budget
    test, and the prefix-defect regression); removing the one-hunk-per-file-per-round limit failed the fairness test.
  - Open risks and limits: a repository configured with `diff.noprefix` or `diff.mnemonicPrefix` reports its files as
    `header_unparseable` (accounted, not guessed) because the capture command is fixed; renames and copies are not
    captured (`--no-renames`) and a rename-format block would be `unsupported_format`; invalid UTF-8 inside tracked
    text reaches the selector already replaced by the process reader; only complete hunks are shown, so a single hunk
    over 8 KiB (for example a 300-line rewrite) is omitted entirely as `hunk_too_large` rather than shown in part,
    which is deliberate but can hide the most relevant change of a very large file; hunk selection is fair by round,
    not by relevance; the mandatory-only refusal is proven at the builder (the returned size) and by the unchanged
    handler bound, not by a new handler-level test; and the parser trusts hunk counts as a boundary, so a diff
    engineered to be self-consistent but misleading is still only untrusted evidence.
  - Post-publication verification: after a GO and publication, rerun the focused tracked-diff parser, selector,
    builder, real-Git, handler, and hosted restart-replay tests against the delivered commit.
- Published delivery: `6367e359674799fb1f41d8a919c61ed56f15b4cd` (parent `87e42a06f2f82f5cfde05939d8e923730bcbe8a1`) was
  committed with the reviewed bounded untracked-file context slice (23 modified, 9 new files, including this file and
  `planner-handoff.md`), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main` and
  `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched that SHA with a clean tree. Before
  staging, only this file's counts were corrected (Infrastructure 625 passed, 3 skipped; 35 new Infrastructure tests,
  34 ran) and the case-distinct-sibling regression was named in its test description; no code, test, wire, schema, or
  product contract changed, and the routed EngineeringStandards documents were read in full without a required change.
  Staged `git diff --cached --check` was clean and local documentation links (112) resolved. Post-publication checks
  against that commit: solution build 0 errors/0 warnings; Infrastructure untracked-preview reader, real-Git preview,
  and evidence-reader tests 35 passed, 1 skipped (the file-symbolic-link case, which this host cannot create without
  elevation); Application manifest and the five handler test classes 295/295; hosted critical-review supervisor tests,
  including the real-store sealed-manifest replay, 16/16. This closure records the delivered SHA and those checks only;
  no code or product contract changed after publication.
- Published delivery: `87e42a06f2f82f5cfde05939d8e923730bcbe8a1` (parent `38df8aba5837bb168d43c2f4db0f3474134210f1`) is
  the factual closure of the second-planning-challenge-round slice (its delivered SHA and checks; no code or product
  contract change). At the start of the next slice, `main`, local `origin/main`, and the live remote matched it,
  nothing was staged or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was
  modified.
- Current delivery, based on verified parent `87e42a06f2f82f5cfde05939d8e923730bcbe8a1`: **bounded untracked-file
  context in Agent manifests**. See [planner-handoff.md](planner-handoff.md) for the selection and
  ["Bounded untracked-file previews in Agent manifests"](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests)
  for the contract. No ADR is added or reversed; no migration, API route or contract, generated-client, provider
  argument, permission, budget, token-stop, fingerprint, or frontend change.
  - Behavior: the checkpoint fingerprint already covered every `??` path and its raw-content hash, but the tracked
    diff never printed a new file's text. `IGitWorkspaceEvidenceReader` gains `CaptureWithUntrackedPreviewsAsync` (a
    default interface method that falls back to `CaptureAsync`; only the five Agent claim handlers call it) and
    `GitWorkspaceEvidenceResult.UntrackedFiles` (`GitWorkspaceUntrackedFile`: path, omission reason, size, text,
    `ContentComplete`). `GitWorkspaceEvidenceReader` reads previews inside its existing status/diff observation
    bracket, only for the `??` paths and hashes of the same capture, through the new Windows-only
    `UntrackedFilePreviewReader`: a file is admitted only when it is opened as a regular file, the open handle's final
    path (`GetFinalPathNameByHandle`, the existing `WindowsFinalPathResolver`) equals the resolved worktree root plus the
    Git-reported relative path exactly and case-sensitively (so a link, junction, swapped component, case-distinct spelling, or
    junction into a case-distinct sibling directory of the root on a case-sensitive parent is refused without reading), its length is at most 64 KiB, its bytes hash to the fingerprint's git blob identity, and it is
    NUL-free valid UTF-8. A junctioned worktree root is accepted against its resolved identity; a host other than
    Windows omits every file as `containment_unproven` (an internal constructor seam tests it) and never uses lexical
    containment. Limits: 4 KiB per file and 16 KiB in total at the reader, deterministic ordinal path order, character-safe
    cuts. One shared `UntrackedFileManifestSection` builds `changeEvidence.untrackedFiles` for all five builders
    (critical review, challenge resolution, implementation accepted and revised, implementation review and its
    correction variant, review correction): every `??` path appears once, with `included`/`omitted`, a fixed
    `omissionReason`, `sizeBytes`, `contentComplete` (true only for the entire file), `allFilesComplete`, and a fixed
    notice; the whole manifest is measured as serialized and the preview budget is halved (16 to 0 KiB) until it fits
    32 KiB, then a small summary states that everything was omitted. A capture with no untracked path serializes
    byte-identically to before (`untrackedFiles` is absent), and the section sits under each manifest's untrusted
    boundary. Fingerprint, tracked diff, status, 8 KiB tracked-diff preview, claim and dispatch checks, the 32 KiB
    handler refusal, and sealed-manifest replay are unchanged; the text exists only in the sealed manifest and provider
    input.
  - Checks run (final tree): `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0
    warnings (one earlier build hit the known `csc` file-lock error; a rebuild cleared it); Domain 670/670; Application
    1543/1543 (was 1504); Infrastructure 625 passed, 3 skipped (was 591 passed and 2 skipped; the three skips are the two
    existing host-capability symlink skips and the new file-symbolic-link case, because this host cannot create a file
    symlink without elevation or Developer Mode); Api 463/463 (was 462); Architecture 9/9; frontend not touched, so no
    frontend check was run; `api-client.ts` byte-identical after the build (SHA-256 `b3e1c836…`, unchanged);
    `git diff --check` clean; local links in the changed and handoff documents resolve (script, including the new
    anchor). Focused tests were run first.
  - New tests: Infrastructure, real Git and the real Windows filesystem in disposable workspaces (35; 34 ran):
    the pre-change omission (plain capture: tracked diff lacks the text and `UntrackedFiles` is null) versus the exact
    bounded text after, with the same fingerprint, tracked diff, head, and paths; one and several files; ordinal
    ordering (upper case first, nested paths) and repeat determinism; clean and tracked-only captures; exactly 4096
    versus 4097 bytes; a two-byte and a four-byte character cut at the boundary; a byte-order mark and CRLF preserved;
    exactly 64 KiB versus 64 KiB + 1 (`too_large` with its size); the 16 KiB aggregate limit; binary (NUL), invalid
    UTF-8, and an ignored file that never appears; a file rewritten after its identity was captured (omitted, the
    fingerprint keeps the captured identity, the new text never appears); a tracked change during capture
    (`RepositoryChangedDuringCapture`, no previews); a legitimate junctioned root; a junction to an outside directory
    inside the worktree (real Git and direct); the no-containment-proof host; and, on a case-sensitive parent, a junction from `Artifacts/link` into the case-distinct sibling `ARTIFACTS/link` (refused although its blob hash is correct), with an ordinary contained file and a junctioned root still previewed. Direct reader cases give a correct
    identity for outside or aliased content so only containment can refuse it: junction to an outside directory,
    junction to another place inside the worktree, case-distinct spelling, nine path-form escapes (`..`, `.`, rooted,
    drive, stream, backslash, empty segment, empty), directory, trailing-slash, directory junction, missing,
    replaced-by-different-content, same-size replacement, replaced-by-directory, and an unresolvable root. Application:
    39 builder tests over all seven entry points (valid JSON, exact and shortened previews, omission reasons, ordinal
    order, `not_captured`, untrusted framing with repository text found only under `changeEvidence`,
    byte-identical tracked-only output, determinism, ceiling fitting, character-safe cuts, the summary form, an
    empty file); the seven handler success tests now use previews and assert the sealed partial manifest. Api: one
    hosted test through the real production command chain and the real artifact store reads the sealed manifest
    with integrity verification twice and finds the previews byte-stable.
  - Mutation checks (each failed the targeted tests, then restored): removing the final-path containment comparison
    failed 4 (three direct cases and the real-Git junction test); removing the identity comparison failed 3 (the replaced or swapped
    case, the same-size replacement, and the mid-capture race); passing `null` instead of `evidence.UntrackedFiles` in the five
    handlers failed all 7 handler tests.
  - Open risks and limits: the file-symbolic-link refusal is exercised only where the host can create one (skipped
    here), although it rests on the same final-path rule whose removal the junction tests catch; a hard link inside
    the worktree is a regular file and is previewed as the bytes at that path; an untracked file whose content
    changes after its hash and before the final status is not seen by status or diff (the existing fingerprint
    behavior), and its preview is omitted as `content_identity_mismatch` so a preview is never attributed to content
    it does not match; directory entries that reach the reader (for example a nested repository shown as `dir/`) are
    omitted as `not_regular_file`, but whether Git's `hash-object` step accepts such an entry first was not exercised
    and that step is unchanged; the summary form can add roughly 100 bytes, so a claim whose manifest without the
    section already sat within about 100 bytes of 32 KiB would now get the existing `context_manifest_too_large`
    refusal; up to 128 files of at most 64 KiB are read synchronously inside a capture; long (over 260 character)
    paths were not exercised; a reader that does not implement previews leaves
    every untracked path marked `not_captured`; the Codex planning manifest is unchanged and does not carry the
    section; behavior on a non-Windows host is proven only through the internal seam on Windows.
  - Correction round (review NO-GO on one containment gap): the resolved-root prefix comparison in
    `UntrackedFilePreviewReader.ReadOne` used `OrdinalIgnoreCase`, so on a case-sensitive parent a junction from
    `Artifacts/link` to a distinct `ARTIFACTS/link` produced a final path whose root prefix matched only ignoring case
    and whose suffix matched, and the outside file was accepted when given its correct blob hash. New regression
    (`UntrackedFilePreviewReaderTests`, the repository's `RequiresCaseSensitiveDirectorySupportFact`, which ran here):
    case-distinct `Artifacts` and `ARTIFACTS` siblings, that junction, the outside file's correct git blob hash. It failed
    before the fix (the file was accepted, `Omission` null) and passes after the comparison became `Ordinal`; the
    outside text and size are never returned. The same fixture confirms an ordinary contained file and a junctioned
    worktree root still preview. The root and the handle are resolved by the same call, so the same directory always
    spells identically and the stricter comparison rejects nothing legitimate. ADR-0004, ADR-0009, and the routed
    standards (adapters, testing, integration tests, security, secure coding, verification, AI-assisted development,
    C# style, foundations) were read after the first review: the secure-coding rule to verify the final canonical path
    stays inside the root is what this fix completes, and no further correction is required by them. Reruns on the
    final tree: solution build 0 errors/0 warnings; Domain 670; Application 1543; Infrastructure 625 passed, 3 skipped
    (the same three symlink skips); Api 463; Architecture 9; `api-client.ts` unchanged; `git diff --check` clean.
  - Post-publication verification: after a GO and publication, rerun the focused untracked-preview reader,
    builder, handler, and hosted sealed-manifest tests against the delivered commit.
- Published delivery: `38df8aba5837bb168d43c2f4db0f3474134210f1` (parent
  `f22325000682caf1d3bd8c6b6384807e03199e79`) was committed with the reviewed second-planning-challenge-round slice
  (20 modified, 13 new files, including this file and `planner-handoff.md`), pushed as a normal fast-forward to
  `origin/main`, and verified with `git fetch origin main` and `git ls-remote`: local `HEAD`, local `origin/main`,
  and the live remote all matched that SHA with a clean tree. Before staging, only the wording that described the
  unreadable-row guard was made precise (it catches `InvalidOperationException`, which cannot prove an
  enum-conversion cause and could have another origin; `DbException` and cancellation exceptions are not caught by
  it); no behavior, test, wire contract, or migration changed. Staged `git diff --check` and the local documentation
  links (136) were clean. Post-publication checks against that commit: solution build 0 errors/0 warnings; focused
  Application lineage, persisted-identity integrity, review/resolution/implementation claim, result and escalation
  tests 233/233; hosted-supervisor tests (second round, challenge resolution, critical review) 26/26; frontend
  lineage, summary, cockpit, review-action, and selector tests 114/114. The full suites recorded below were run
  before publication. This closure records the SHA and those checks only; no code or product contract changed
  after publication.
- Published delivery: `f22325000682caf1d3bd8c6b6384807e03199e79` (parent
  `210f4e8699dad670aadf0816ea99f9b85ff8627f`) is the factual closure of the token-activity stop slice (its delivered
  SHA and checks; no code or product contract change). At the start of the next slice, `main`, local `origin/main`,
  and the live remote all matched it, nothing was staged or untracked, and only `docs/roadmap/planner-handoff.md`
  (the planner's slice selection) was modified.
- Current delivery, based on verified parent `f22325000682caf1d3bd8c6b6384807e03199e79`: **optional second planning
  challenge round and escalation**. See [planner-handoff.md](planner-handoff.md) for the selection and the
  ["Optional second challenge round and escalation"](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation)
  and ["Optional second challenge round"](../product/run-cockpit-specification.md#optional-second-challenge-round)
  sections for the contract. No ADR is added or reversed; no migration, API contract, generated-client, provider
  argument, permission, budget, or token-stop change.
  - Behavior: one shared, snapshot-based `PlanningLineage` rule derives the Proposal → Challenge → Decision →
    revised Proposal chain from durable identity only (same-run provider-observed messages, exact owning attempts
    with coherent role, contract, and provider, ordered inputs, reply links, completed outcomes, exact
    workspace/checkpoint/fingerprint; bounded, cycle-safe, fixed non-echoing refusals). A Planner root is depth 0;
    a Resolver revision whose inputs are a valid parent plus that parent's complete, sole Challenged review, with
    one Decision per Challenge, is one level deeper (depth 1, then 2; deeper is invalid). Review claims accept a
    root or a depth-one revision and refuse a depth-two Proposal (`proposal_lineage_exhausted`) before the provider
    probe, Git capture, or sealing; resolution claims evaluate the reviewed Proposal the same way. A successful
    *second* resolution (reviewed Proposal at depth one) records, in the same single save as its Decisions and
    depth-two Proposal, exactly one bounded `Escalation` message (`HostConstructed`, Orchestrator to Human,
    attemptless, replying to the depth-two Proposal, fixed text plus the root, first-revision, second-revision, and
    resolved-challenge ids and a count; no provider or artifact text) with its own run event as the result's newest
    event. The result boundary re-decides the lineage first (reviewed Proposal valid at depth <= 1, the sole
    Challenged review's complete challenge set equals the attempt's inputs, no competing exact resolution) and
    refuses with nothing recorded otherwise. Implementation keeps the accepted-original path, keeps the direct
    first-revision path with its complete Decision evidence, binds a first revision whose own review was Accepted
    to that exact Acceptance (inputs: revision, Decisions, Acceptance; manifest `acceptedSecondReview`), refuses a
    first revision whose own review Challenged (`plan_challenged`, also while the second resolution is pending,
    failed, or succeeded, never falling back to an earlier Proposal), and refuses a depth-two Proposal even with
    its escalation. Lineage refusals are decided before Git capture or sealing (see the correction round). The review and
    implementation claims re-evaluate eligibility, competing attempts, and the Running slot as their last read
    before the commit (deleting the sealed manifest on refusal), and the resolution claim re-checks inside its
    existing write-locked claim transaction; the unique Running-attempt and budget-slot indexes and every existing
    budget, reserved-time, token-stop, dispatch, and sealed-manifest rule are unchanged. The downstream
    review/correction input-chain validator (`ImplementerExecutionReportEligibility`) now uses the same lineage
    rule, accepts a first revision with or without its trailing Acceptance, and rejects a depth-two plan. The
    lineage-wide cap (two review-and-resolution rounds per lineage, counted by validated identity) is deliberately
    more conservative than the roadmap's per-material-issue wording, which is now documented in the workflow
    model and delivery plan.
  - Correction round (review NO-GO on two integrity gaps): (1) the lineage trusted `ResolveSnapshotOwningAttempt`,
    which accepts any defined provider that matches the message actor, so a persisted Planner/Claude Code,
    Resolver/Claude Code, or CriticalReviewer/Codex attempt with a forged matching actor was accepted, and an
    undefined response contract could throw. `PlanningLineage` now resolves every owner through its own strict
    rule that requires `AgentAttemptIdentity.IsCoherent` (defined role, provider, and contract, a supported
    role/provider pair, a contract belonging to the role, a well-formed assignment) before the existing
    role/actor checks; the same coherence is required of the matched Challenged review, the resolving attempt at
    the result boundary, the challenged review at the resolution claim, and the Planner and review attempts of the
    accepted-original implementation path. (2) `LoadSnapshotAsync` materializes every Attempt of the run, so one
    unparseable stored enum string threw before a refusal could be returned. Each lineage read now catches only
    the `InvalidOperationException` materialization failure and returns one fixed refusal
    (`proposal_lineage_not_valid`), for an unreadable participating row and for an unrelated unreadable row beside
    a healthy lineage: the review claim, resolution claim (including its in-transaction re-check), implementation
    claim and its early check, and the result boundary. Neither the stored string nor the exception is surfaced. The
    `InvalidOperationException` guard cannot prove an enum-conversion cause and could have another origin;
    `DbException` and cancellation exceptions are not caught by it. Other consumers of the shared snapshot are unchanged. The
    early no-external-work refusal now covers every lineage refusal for a Proposal not owned by a Planner attempt
    (implementation) and every lineage refusal except an unknown proposal (review), so a corrupt lineage does no
    Git capture or sealing on those paths. Three earlier tests that proved role-first authority by substituting
    an unsupported provider (Planner/Claude Code, Resolver/Claude Code, CriticalReviewer/Codex) were reversed to
    assert the fixed refusal. New `PlanningLineageIntegrityTests` (17 SQLite-persisted cases: forged planner,
    resolver, and reviewer pairs with matching forged actors, undefined role and contract, unreadable
    participating and unrelated rows) prove review, implementation (including the accepted-original path),
    resolution-claim, and result-boundary refusals with a fixed code, no stored string or exception text, zero Git
    captures and zero sealed manifests where the path refuses early, no created attempt, and nothing recorded.
    Mutations: removing the coherence requirement failed 4 of them; letting the materialization exception escape
    failed the 4 unreadable review/implementation/result cases. Inventory reconciliation: the earlier "17
    modified/13 new" was a counting slip; `git status --short -uall` is 18 modified (including
    `current-work.md`, added after that count, and the planner's `planner-handoff.md`) and 12 untracked, now 20
    modified (two more test files reversed for the unsupported-pair cases) and 13 untracked (the new integrity test file).
  - Cockpit: `derivePlanningLineage` reads the newest Planner root, its first and second Resolver revisions, and
    the escalation from the loaded timeline by reply links (ambiguous, foreign, simulated, self-referential, or
    out-of-order links are not followed). The review action targets the root, then the first revision ("Request
    Claude review of the revised proposal"), and nothing after a second revision; implementation is offered for
    the Accepted root or a first revision whose own review did not Challenge and is not running, and withheld
    while that review status is loading or failed and for a second revision. A "Proposal lineage" region states
    the stage in fixed text, ends with "human decision required, not an approval", and says so when the
    escalation is not in the loaded timeline. No new endpoint or generated-client change.
  - Test fixtures: the implementation-claim and result-recording tests previously seeded revised plans with
    unrealistic evidence (challenges owned by the planning attempt, random input ids, no review attempt); they now
    use a shared `PlanningLineageSeeder` that builds real lineage evidence, because the claim and result
    boundaries now decide the lineage instead of trusting it.
  - Checks run: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0 warnings;
    Domain 670/670; Application 1504/1504 (was 1438; new `PlanningLineageTests` 19 including the corruption
    theory and the downstream implementation-chain shapes, `SecondChallengeRoundClaimTests` 22,
    `SecondChallengeRoundResultTests` 8, and the correction's `PlanningLineageIntegrityTests` 17); Infrastructure 591 passed, 2 skipped (the existing host-capability
    skips); Api 462/462 (was 459; three hosted-supervisor tests through the real production command chain: a
    claimed second resolution runs from its sealed manifest and records one escalation, a restart interrupts a
    claimed second resolution without invoking the provider and the interrupted run accepts no further claim, and a
    completed lineage stays exhausted across a restart with review, resolution, and implementation all refused);
    Architecture 9/9; frontend `vitest` 856/856 (was 825; `derivePlanningLineage` 18, `PlanningLineageSummary` 6,
    and seven cockpit wiring tests for selection, blocking, exhaustion, loading and error withholding, and run
    switch), `tsc -b` and `npm run build` clean, and `oxlint` with no warning from a changed file (only the
    pre-existing hook warnings); the regenerated `api-client.ts` is byte-identical across repeated Api builds
    (SHA-256 `b3e1c836…`, unchanged from the previous delivery); `git diff --check` clean; local links in the
    changed and handoff documents (136) resolve, including the two new anchors. Focused suites were run first.
    After the correction these were rerun in full against the final tree: the solution build (0 errors/0 warnings),
    Domain 670, Application 1504, Infrastructure 591 passed/2 skipped, Api 462, Architecture 9, frontend `vitest`
    856/856, `tsc -b`, `oxlint` (no warning from a changed file), `npm run build`, the generated-client hash
    (unchanged), local links (all resolved), and `git diff --check`. The correction changed no frontend or API
    contract code, so the earlier frontend and hosted-supervisor results were unchanged by it; the earlier
    mutation results for the claim races still describe the unchanged race code.
  - Added limit from the correction: the unreadable-row guard catches `InvalidOperationException`, which cannot prove
    the cause was enum conversion (the same documented limit as `AgentAttemptRead`), and one unreadable row of
    a run refuses the run's revision lineage paths (not the original Planner path) until repaired.
  - Red-before/green-after evidence (mutations of the delivered code, restored afterward): reinstating the old
    "Planner root only" review rule failed 5 of 22 claim tests (first-revision review, second review of the same
    revision, depth-two refusal, the seal-time race); removing the challenged-revision implementation gate failed
    6 (Challenged in each second-resolution state, the accepted-review binding and its broken link, and the race);
    removing the depth-two implementation refusal failed 1; disabling the last-read re-checks failed the three
    seal-time race tests (the Running-slot race is still refused by the unique index).
  - Open risks and limits: the review and implementation claims decide their last re-check as a read immediately
    before one `SaveChangesAsync` rather than inside an explicit transaction, so the residual window would need a
    competing attempt to be claimed, dispatched, run, and recorded inside it — the unique Running-attempt index
    refuses any such claim that has not finished, and the resolution claim is atomic in its own transaction.
    `MarkAgentAttemptDispatched` gained no new lineage re-check: lineage evidence is append-only and only one
    attempt can be Running per run, so a claimed attempt's lineage cannot change before dispatch except by direct
    store corruption, which only the resolution result boundary would still catch; a new dispatch-time terminal
    outcome was out of scope. Startup reconciliation interrupts every Running read-only attempt, dispatched or
    not (verified by the new restart test), which is looser than the "replay after a restart" wording of the older
    planning-repair section; that wording was not changed. HTTP-level endpoint tests were not added for the new
    codes (they map through the shared Result convention); the mediator-level hosted tests cover them. The
    cockpit lineage is a hint read from the loaded timeline window and cannot see a message outside it. A
    duplicated root Proposal is refused only on the implementation paths, as before.
- Published delivery: `210f4e8699dad670aadf0816ea99f9b85ff8627f` (parent
  `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`) was committed with the reviewed 67-file token-activity stop slice
  (33 modified, 34 new, including this file and `planner-handoff.md`), pushed as a normal fast-forward to
  `origin/main`, and verified with `git fetch origin main` and `git ls-remote`: local `HEAD`, local `origin/main`, and
  the live remote all matched the delivered SHA, with a clean working tree. Before the commit, only wording was
  corrected: the set-handler comment and the architecture section now say a committed change to the value of a
  configured concurrency token (lifecycle, Claude model and effort request, either stop threshold) causes the
  conflict, not any Run change, and this file records both CRLF notices. Staged `git diff --cached --check` reported
  only CRLF notices (the model snapshot, the generated client, and the two new generated migration files) and local
  documentation links (143) resolved. Focused checks rerun against that commit after a solution build (0 errors, 0
  warnings), all passing: Application 441 (stop accumulator, gate, policy guard, untrusted-evidence, cockpit
  projection, set handler, and all six claim-path suites), Api 46 (stop endpoint, planning request endpoint, and hosted
  supervisor including the claimed-before-stop and restart-replay tests), Infrastructure 2 (migration), and frontend
  84 (stop panel, refresh, hook, and cockpit wiring). This closure records the delivered SHA and those checks only; no
  code or product contract changed after publication.
- Current delivery, based on verified parent `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`: **run-scoped, provider-separated
  token-activity stop at Agent claim**. See [planner-handoff.md](planner-handoff.md) for the selection and the
  ["Per-provider run token-activity stop at Agent claim"](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim)
  and
  ["Per-provider token-activity stops"](../product/run-cockpit-specification.md#per-provider-token-activity-stops)
  sections for the contract. No ADR is added or reversed; `engineering-context.md` now states that this one token
  control is enforced while account-usage limits are not.
  - Behavior: an owner sets or clears one nullable stop threshold per provider (`Run.CodexTokenStopThreshold`,
    `Run.ClaudeTokenStopThreshold`; 1..10^12; `AddRunTokenStopThresholds` adds two nullable columns with no default and
    no backfill) through the protected `POST /api/runs/{runId}/token-stop-threshold` (`SetTokenStopThresholdCommand`, one
    `SaveChangesAsync` for the Run change and its `run.token_stop_threshold_changed` event; terminal run 422, concurrent
    change 409). Both columns are EF concurrency tokens (the advisory warning columns still are not), so concurrent
    writers never silently overwrite each other. Each of the six Agent claim paths calls `AgentTokenStopGate` for its
    fixed provider after the run-wide count and reserved-time budgets (their precedence is unchanged) and before any
    provider-availability check, Git capture, manifest sealing, or review-correction escalation and authorization
    consumption. Unconfigured, the gate reads nothing and behavior is unchanged. The count is the advisory warning's
    provider-specific formula through one shared rule (Codex input + output with cached input already inside input;
    Claude input + cache creation + cache read + output, both cache counts required) over dispatched Agent attempts only;
    a running attempt is pending, and missing, malformed, unsupported, or unattributed evidence, an untrusted persisted row, and a 64-bit overflow are
    gaps, never zero. A known count at or above the threshold (including equality) refuses with
    `agent_attempts.token_stop_reached` (409) even beside gaps; otherwise an unprovable state refuses with
    `agent_attempts.token_stop_evidence_indeterminate` (422); no dispatched history permits the first claim. The advisory
    warning's state is never read, and the providers are never combined. At the durable claim boundary the Claude paths
    mark both stop columns modified so their single Run UPDATE requires the loaded policy, and the Codex paths run an
    `ExecuteUpdate ... WHERE` compare beside the existing model-preference compare inside their explicit transaction;
    either mismatch fails with `agent_attempts.token_stop_policy_changed` (409), rolls back the Attempt, inputs,
    artifact, and any authorization consumption, deletes the sealed manifest, and a retry re-decides. A lost budget-slot
    race re-evaluates the stop before reporting a slot conflict. A change after a claim commits is prospective. The
    cockpit adds a separate `tokenStops` projection (threshold, state, `claimBlocked`, known count, and the counted,
    pending, insufficient, and unattributed counts, from the same accumulator as the gate) and a separate "Token-activity
    stops (enforced at claim)" panel with per-provider Save and Clear, local 1..10^12 validation, an authoritative
    refresh after a successful save or clear, a safe synchronization message on refresh failure, distinct blocking and
    permitting states, and no statement that a provider is available. The cockpit's warning panel and the `tokenWarnings`
    projection are unchanged apart from sharing the count formula. No CLI argument, provider adapter, usage parser,
    account-allowance, session, context, or cancellation change; no real provider is called.
  - Correction round (review NO-GO on the persisted-evidence boundary): `Status`, `AgentProvider`, and `AgentRole` are
    string-converted enums, so an unrecognized stored string made the gate and the cockpit throw during
    materialization, an undefined number such as `99` was counted as a concluded attempt merely because it was not
    `Running`, and a valid provider on a role/provider-incoherent row was trusted. First added SQLite-persisted
    regression cases (unparseable `Status` and `AgentProvider`, `Status = '99'`, empty status, unrecognized role, and
    incoherent role/provider pairs with otherwise plausible usage, each beside a healthy attempt) for the gate, the
    cockpit, and the Claude implementation claim (zero Git, sealing, or attempt work, no stored string in the error);
    they failed (16 of 27 with materialization exceptions, undefined status counted, and incoherent attribution
    trusted). The fix classifies in the database through `PersistedAgentAttemptStopEvidence` (booleans compared against
    the known names, never materializing the strings): an untrusted row is an unattributed gap for both providers,
    sound rows keep provider separation and the known-count-at-threshold precedence, and the cockpit's other
    accumulators receive an unrecognized status as pending and an unrecognized provider as unattributed. The
    latest-attempt card, which loads a full entity, is omitted if that one row cannot be materialized. All 27 pass.
    Applies only to a configured stop; an unconfigured claim still issues no query. No adapter, parser, or
    allowance change. `mvp-delivery-plan.md` now describes the local stop in the present tense and keeps the provider
    account-usage stop-threshold exit criterion and the remaining token and account-usage controls open. Remaining
    limit: other older reads that materialize a corrupted attempt row elsewhere in the system (for example other
    status queries) are unchanged and out of this correction.
  - Checks run: `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false` 0 errors/0 warnings;
    Domain 670/670 (was 657); Application 1438/1438 (was 1269); Infrastructure 591 passed, 2 skipped (the existing
    host-capability skips); Api 459/459 (was 440); Architecture 9/9; frontend `vitest` 825/825 (was 795), `tsc -b` and
    `npm run build` clean, and `oxlint` with no warning from a changed file (only the pre-existing hook warnings);
    the regenerated `api-client.ts` is byte-identical across repeated Api builds (SHA-256 `b3e1c836…`, regenerated for
    the new operation and `tokenStops`); `git diff --check` clean apart from the two known CRLF notices (the migration model snapshot and the generated client);
    local links in the changed and handoff documents, now including the delivery plan (143), resolve, including the two new anchors. The
    migration was generated with `dotnet ef` after a rebuild of the startup project (an earlier attempt from a stale
    startup build produced an empty migration and its `migrations remove` deleted the previous migration; both were
    reverted from Git and the tree contains exactly the one new migration and the snapshot change).
  - New tests: Domain set, clear, independence from the warning, bounds, undefined provider, and terminal-run rules;
    Application accumulator (both formulas, equality, no-history versus unknown, running, missing, malformed, cache
    missing, unattributed, undefined provider, provider separation, overflow through an internal seam, error mapping),
    persisted-row gate tests (corrupted usage columns, unsupported schema, unattributed, cache breakdown on Codex,
    undispatched, unconfigured reads nothing), the policy guard in isolation, set/clear handler (validation, lifecycle
    race, both concurrent-writer orders), cockpit projection (states, warning independence, re-derivation on change,
    agreement with the gate); for each of the six claim paths a reached refusal and an indeterminate refusal before any
    Git capture, sealing, or provider probe (also with the runtime unobserved), unconfigured and other-provider stop
    unaffected, count and time budget precedence, a stop change during external work as a policy-changed refusal with
    orphan cleanup, a retry that re-decides, the other provider's change also refusing, and a stop set after a claim
    leaving the claimed attempt; review correction additionally proves no escalation is created and an available
    authorization stays unconsumed, then is consumed once after the stop is cleared; planning repair, a Created run, and
    a repair source without usage; migration (nullable, no default, no backfill, concurrency-token metadata); Api auth,
    404, validation, terminal run, exact-equality projection, per-change event, and the 409 and 422 mappings on the
    planning request endpoint; a hosted test where a claim committed before the stop still dispatches and finishes and a
    hosted restart replay where a fresh container refuses the claim from persisted state with zero provider invocations;
    frontend hook, panel (every state, formulas, local validation including 10^12 + 1, safe errors, unrepresentable
    total, no eligibility wording), refresh (into and out of a block without any run event, sync failure, failed save
    without refresh, run change), and cockpit wiring (a sibling-key collision that kept a stale panel was found and fixed).
    Mutation checks (each failed the targeted tests, then restored): the Codex commit-time compare set to always-true
    failed both Codex race tests; changing `>=` to `>` and removing the gate calls in the planning and review-correction
    handlers failed 26 tests. Removing only the explicit Claude `Guard` call did not fail a claim test because the existing
    model-preference guard already forces the same Run UPDATE whose WHERE carries every concurrency token; the guard is
    therefore proven on its own by `CurrentTokenStopPolicyTests`. Tests use deterministic doubles.
  - Remaining risks and limits: a local, retrospective guardrail on best-effort provider-reported usage, not an account
    allowance, per-attempt cap, reservation, or guarantee about an invocation in progress; a provider or attempt that
    recorded no usage leaves the provider unable to clear (indeterminate) until the owner raises or clears the stop, with
    no override; a change to either provider's threshold makes an in-flight claim of either provider retry (the guard
    is the pair), which is conservative; a review-correction request that would only create the human escalation is also
    refused while the Claude stop blocks; the Codex commit-time atomicity relies on the existing explicit-transaction
    write lock and the Claude atomicity on the Run UPDATE's concurrency tokens, both proven at a deterministic seam
    rather than with a truly interleaved commit; overflow cannot be reached with real bounded inputs and is proven
    through an internal seam; the UI shows a blocking state but does not disable claim actions (the server refuses with a
    fixed safe message); Codex account-allowance observations are not read.
  - Post-publication verification: after a GO and publication, rerun the focused stop Application, claim-path, Api
    endpoint, hosted, migration, and frontend stop tests against the delivered commit.
- Published delivery: `725476e54d9f7bcf437f3fc920bd375dae1fed67` (parent
  `2b12bae263e9d9eff0c18157e8dd0c5aaa37863a`) was committed with the reviewed 30-file bounded human-guidance slice
  (18 modified, 12 new), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main` and
  `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched the delivered SHA, with a clean
  working tree. Solution build 0 errors/0 warnings. Before the commit, only comments and documentation that claimed
  `ReviewCorrectionGuidance.Normalize` runs "once" were reworded (it is deterministic, called by both the validator and
  the handler, and always before persistence). Focused checks rerun against that commit, all passing: Domain 32
  (guidance normalization, reserved value, and canonical-content tests), Application 82 (review-correction claim and
  guidance tests), Api 32 (review-correction endpoint including guided-authorization tests, and hosted supervisor
  tests including guided restart replay), and frontend 108 (guidance entry, review-correction action, authorization hook,
  and cockpit wiring). This closure records the delivered SHA and those checks only; no code or product contract changed
  after publication.
- Current delivery, based on verified parent `2b12bae263e9d9eff0c18157e8dd0c5aaa37863a`: **bounded human guidance for one
  authorized review correction**. See [planner-handoff.md](planner-handoff.md) for the selection and the
  ["Bounded human guidance for one authorized review correction"](../architecture/agent-collaboration-protocol.md#bounded-human-guidance-for-one-authorized-review-correction)
  and
  ["Optional guidance with a review-correction authorization"](../product/run-cockpit-specification.md#optional-guidance-with-a-review-correction-authorization)
  sections for the contract.
  - Behavior: the bodyless `POST …/review-correction-escalations/{id}/authorize` is unchanged. A new protected
    `POST …/authorize-with-guidance` (body `{ guidance }`, request body capped at 8 KiB) sends the same
    `AuthorizeReviewCorrectionCommand` with `Guidance`. `ReviewCorrectionGuidance.Normalize` is deterministic and is validated before persistence (Unicode form C,
    `\n` line endings, trimmed; non-blank, at most 600 characters, no control characters other than `\n`, no unpaired
    surrogate, not the reserved default rationale, and the ledger's existing best-effort unsafe-text screen, which also
    rejects ordinary words such as "environment" and does not guarantee secrets are absent). Rejections are a 400 from
    `AuthorizeReviewCorrectionCommandValidator` (handler repeats it) with a fixed message that never echoes the input. The
    accepted text is carried in the existing `HumanInstruction`'s `rationale` (the `instruction` field stays the fixed
    authorization; a bodyless authorization keeps the byte-identical historical default rationale, and that exact
    text is reserved: submitting it as guidance is rejected, so a guided and a bodyless authorization can never share
    stored bytes), so there is no ledger schema, ADR, or migration change. An identical retry (after normalization) is idempotent; different guidance against an
    existing unconsumed authorization, including bodyless-after-guided and guided-after-bodyless, is a 409
    `guidance_conflict` that echoes neither value, and a concurrent insert loser is resolved the same way; a retry first validates the
    existing authorization's whole persisted chain with the same resolver a claim uses and fails closed as
    `instruction_invalid` on any incoherence, never as an idempotent success. Bodyless-after-guided returning a conflict is a deliberate
    consequence of "different guidance never reports somebody else's authorization" and only arises in a state that could
    not exist before this slice. At claim time the shared `ReviewCorrectionAuthorizationInstruction` verifies, before any manifest is
    sealed, that the authorization, escalation, escalation message, and `HumanInstruction` share one run and escalation,
    that the escalation message is a host-constructed, attemptless, protocol-1.0 Orchestrator-to-Human `Escalation`, that
    the `HumanInstruction` has the expected type, provenance, participants, protocol version, attemptless shape, and reply
    linkage, and that its stored content is exactly the canonical bytes written for the default rationale or for a
    rationale that is its own normalization (length bound, screen, control characters, Unicode, form C, trim); otherwise 409
    `instruction_invalid` with nothing consumed or sealed. The manifest gains, only for guided authorizations, a fixed
    `humanGuidanceBoundary` and one `humanGuidance` (message id and exact text) after the fixed instruction and evidence
    boundary; an unguided manifest is byte-identical to before. Ordered `AttemptInputMessage` rows remain exactly the
    ExecutionReport then ReviewFindings. Claim budgets, atomic one-time consumption (concurrency token), orphan-manifest
    cleanup, and sealed-manifest restart replay are unchanged; the run-wide budgets are still checked first. The cockpit
    shows "Authorize with guidance" only where "Authorize one additional correction" is offered (not when blocked by a
    global budget or time fit), with a length counter, a not-screened-for-secrets warning, local blank/length feedback,
    pending and safe server-refusal states, and a draft held only in component state, reset when the escalation or run
    changes; nothing is written to storage or the URL. No provider flag, adapter, permission, session, fallback, retry, or
    allowance-enforcement change.
  - Checks run: solution build 0 errors/0 warnings (one build hit the known `csc` file-lock error and a rebuild
    cleared it); Domain 657/657; Application 1269/1269; Infrastructure 589 passed, 2 skipped (the existing
    host-capability skips); Api 440/440; Architecture 9/9; frontend `vitest` 795/795, `tsc -b`, `npm run build`
    clean, and `oxlint` with no new warnings (the four pre-existing `useAuthorizeReviewCorrection.ts` render-ref warnings
    only moved lines); repeated Api builds left `api-client.ts` byte-identical (SHA-256 `0cca6807…`, regenerated for
    the new operation and request type). New tests: Domain normalization, bounds, control/unsafe/surrogate rejection, and
    content round trip; Application bodyless unchanged (fixed message and manifest keys), exact persisted guidance and
    non-echoing metadata, exact manifest content with input identity unchanged, bounds, identical and conflicting
    retries, concurrent identical and conflicting submissions, corrupt persisted message, four incoherent-link cases, the reserved default rationale rejected as guidance (and never a silent
    bodyless success), and twenty stored-content and envelope corruption kinds (overlong, unsafe, control, padded, CRLF,
    non-NFC, reordered/spaced/default-spaced JSON, and wrong provenance, actor, recipient, protocol, attempt, or reply on
    the instruction and wrong provenance, actor, recipient, protocol, or attempt on the escalation message), each refused on
    both an idempotent retry and a claim without echo, consumption, or sealing,
    global-budget refusal leaving the authorization unconsumed, one-time consumption, a claim losing the authorization
    race with orphan cleanup, and bounded evidence with maximum guidance; Api auth, 404s, no-body, invalid and oversized
    guidance without echo, success, idempotent and conflicting retries, and bodyless-after-guided; a hosted restart-replay
    test through the real handlers and supervisor; frontend hook, entry component, gating, reset, error, and cockpit
    wiring tests. Mutation checks (each failed the targeted tests, then restored): disabling the claim-time linkage
    check failed the four incoherent-link cases, and disabling the existing-authorization guidance comparison failed the
    conflict, corrupt-message, and concurrent-conflict tests; disabling the canonical-content and escalation-envelope checks failed
    eleven corruption cases. Tests use deterministic doubles; no real provider is called.
  - Remaining risks: guidance is human text sent to a provider and stored unredacted in the ledger and sealed manifest;
    the lexical screen is best-effort, rejects some ordinary words, and cannot prove secrets are absent; a
    prompt-injecting guidance can still only ask, since the fixed host instruction, boundary, tools, and permissions
    govern the provider, but a provider may not honor that boundary; the guided-claim manifest could exceed its 32 KiB
    bound only if other evidence already nearly did (the existing check then refuses the claim); the concurrent-claim
    race is proven deterministically at the sealed-artifact seam rather than with a truly interleaved commit; a guided
    request against an already-guided authorization with only a whitespace difference is idempotent by design; the
    canonical-form rule means an authorization message written by any other tool or an earlier variant of this code is
    refused rather than repaired, and text equal to the reserved default rationale cannot be submitted as guidance.
  - Post-publication verification: after a GO and publication, rerun the focused guidance Domain, claim, endpoint,
    hosted-replay, and frontend tests against the delivered commit.
- Published delivery: `2872224f2271b4d8dde284c7c1fe5fc603664c47` (parent
  `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`) was committed with the reviewed 38-file Codex Planner format-repair
  slice (24 modified, 14 new), pushed as a normal fast-forward to `origin/main`, and verified with `git fetch origin main`
  and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all matched the delivered SHA, with a clean
  working tree. Solution build 0 errors/0 warnings. Focused checks rerun against that commit, all passing: Application 80
  (repair claim and Planner status lineage tests), Domain 13 (repair factory and eligibility), Infrastructure 6
  (migration, unique index, foreign key, and cascade tests), Api 22 (repair endpoint plus hosted-supervisor restart
  replay tests), and frontend 72 (repair panel, repair hook, ordinary request hook, and cockpit wiring). Before the
  commit, only the `repairSourceAttemptNumber` XML comment and the entry below were corrected: the source id stays
  present, and the number is null when no source of the same run is found. This closure records the delivered SHA and
  those checks only; no code or product contract changed after publication.
- Current delivery, based on verified parent `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`: **one manual Codex Planner
  format-repair attempt** (ADR-0004's optional bounded repair, Planner/Proposal only). See
  [planner-handoff.md](planner-handoff.md) for the selection and the
  ["One manual Codex Planner format repair"](../architecture/agent-collaboration-protocol.md#one-manual-codex-planner-format-repair)
  and
  ["One manual Codex plan format repair"](../product/run-cockpit-specification.md#one-manual-codex-plan-format-repair)
  sections for the contract.
  - Behavior: protected `POST /api/runs/{runId}/agent-attempts/{sourceAttemptId}/codex-plan-repair` (no body) sends
    the existing `CreateCodexPlanningAttemptCommand` with a `RepairSourceAttemptId`, so a repair is the ordinary
    Planner claim (workspace, lease, fresh Git checkpoint, provider observation, run model/effort snapshot, one
    Running attempt, count and reserved-time budgets, sealed-manifest cleanup) plus a source check. The source must be
    in the run, a dispatched Codex Planner/Proposal attempt with status `Failed` and outcome exactly
    `InvalidStructuredOutput` (`Attempt.IsEligiblePlanningRepairSource`; a persisted `Completed` row with that outcome is
    refused), not itself a repair, not already repaired, the run's latest Agent attempt, and made against exactly the
    workspace, checkpoint, and fingerprint the repair claim selects (a newer valid checkpoint is refused; ordinary requests
    cover it); each refusal is a fixed safe error (404 not found for unknown and foreign-run ids; 409 ineligible,
    repair-of-repair, already requested, not latest, checkpoint mismatch). Eligibility is evaluated before the
    running-attempt check at the request and again inside the claim's own transaction after the preference guard's
    write, so a stale or competing source rolls the claim back and deletes the sealed manifest. Additive migration
    `AddCodexPlanningRepairLink`: nullable `attempts.AgentRepairSourceAttemptId`, a `NO ACTION` self-foreign-key (a referenced source cannot be deleted alone, while the existing run cascade still
    deletes source and repair together), and the
    filtered unique index `ix_attempts_agent_repair_source` (the database backstop, mapped to "already requested").
    The repair manifest is the ordinary planning manifest plus one fixed `formatRepairNotice`; it contains no source
    response, parser detail, path, attempt id or outcome, or human text. Dispatch, restart replay, the read-only
    adapter arguments, parser, result recording, and the one-Proposal ledger rule are unchanged (no adapter,
    supervisor, or provider code changed). Planner status adds `repairSourceAttemptId` (present for every repair)
    and `repairSourceAttemptNumber` (null when no source of the same run is found; an invalid enum on the source does not by itself null this scalar projection) lineage. The cockpit adds a separate "Codex plan repair" panel (suggestion for the latest invalid non-repair
    attempt, lineage line, safe errors, run-scoped request state); the ordinary request stays available.
  - Checks run: solution build 0 errors/0 warnings (a transient `MSB3026` copy-lock warning on one incremental build
    cleared on rebuild; a `--no-incremental` parallel build hit a metadata-file race that a normal rebuild cleared);
    Domain 625/625; Application 1221/1221; Infrastructure 589 passed, 2 skipped (the existing host-capability skips);
    Api 428/428; Architecture 9/9; frontend `vitest` 776/776, `tsc -b`, `npm run build` clean, and `oxlint` with no
    warnings in new or touched files (pre-existing warnings elsewhere unchanged); repeated Api builds left
    `api-client.ts` byte-identical (SHA-256 `d535bb6f…`, regenerated for the new operation and response fields).
    New tests: Domain factory and eligibility; Application claim success, manifest shape and secrecy, fail-closed
    cases (unknown, foreign run, wrong outcome, Claude source, nonlatest, newer running Agent attempt, other-kind
    running attempt, repair of a repair, already repaired, count and time budgets, provider, lease, workspace, stale
    checkpoint, a newer valid checkpoint that differs from the source's, a persisted Completed row with the invalid outcome), preference change during external work, source made stale during the claim, competing repair during
    the claim, and a concurrent claim pair (six consecutive passes); Infrastructure migration data/index preservation,
    filtered unique index, concurrent insert race, foreign key, run cascade versus blocked source-only delete (the cascade test fails if the foreign key is RESTRICT), and down/up round trip; Api endpoint auth, 404/409
    mapping, success, no leakage, ordinary request unaffected, status lineage; hosted-supervisor restart replay and
    interrupted-repair reconciliation; frontend hook, component, and cockpit wiring including a run switch. Mutation
    checks (each failed the targeted tests, then restored): disabling the commit-boundary source re-check failed the
    stale-source test, and disabling the latest-attempt check failed three tests. Tests use deterministic doubles;
    no real provider is called. `git diff --check` and local documentation links are recorded in the review report.
  - Remaining risks: a repair spends a real Agent budget slot and reserved time and can fail like any claim; the
    "one repair" and "no chain" rules are enforced by the handler, the Domain rule, and the unique index but repair
    chains have no database constraint of their own; a source whose row cannot be materialized is treated as
    ineligible rather than diagnosed; the Application concurrent-pair test usually rejects the loser at the request
    check, so the interleaved commit-boundary case is covered by the deterministic injected-race tests and the index
    backstop; the source-identity check compares immutable values against the same selected workspace and checkpoint at the request and at the claim boundary, so the boundary repeat guards code paths rather than a changing input; a run whose interrupted repair is reconciled leaves the run not active, so no further claim is
    possible; the source response remains inspectable only through the existing history and is never sent to the
    provider, so a repair can only ever be a fresh attempt, not a semantic fix.
  - Post-publication verification: after a GO and publication, rerun the focused repair claim, migration, endpoint,
    hosted-supervisor replay, and frontend repair tests against the delivered commit.
- Published delivery: `998c05f98558cfccc849cf2845b3348d97584b3d` (parent
  `391317193575d111a5e27afd76140010ce3bd21b`) was committed with the reviewed 44-file Agent-attempt history and
  evidence inspector slice (14 modified, 30 new), pushed as a normal fast-forward to `origin/main`, and verified
  with `git fetch origin main` and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all
  matched the delivered SHA, with a clean working tree. Solution build 0 errors/0 warnings (no build lock recurred).
  Focused checks rerun against that commit, all passing: Api 35 (history, evidence, and window endpoint tests plus
  the unchanged message-linked sealed-window tests), Application 20 (attempt-identity and sealed-window tests), and
  frontend 17 (`AgentAttemptHistoryPanel`). This closure records the delivered SHA and those checks only; no code or
  product contract changed after publication.
- Current delivery, based on verified parent `391317193575d111a5e27afd76140010ce3bd21b`: a read-only
  **Agent-attempt history and evidence inspector**. See [planner-handoff.md](planner-handoff.md) for the
  selection and the
  ["Agent-attempt history and evidence inspection"](../architecture/agent-collaboration-protocol.md#agent-attempt-history-and-evidence-inspection)
  and
  ["Agent attempt history and evidence"](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence)
  sections for the contract.
  - Behavior: three protected, read-only MVC operations that need no collaboration message —
    `GET /api/runs/{runId}/agent-attempts` (Agent attempts only, strictly descending `AttemptNumber`,
    exclusive `beforeAttemptNumber` cursor, default 10 / hard cap 20, explicit `hasMore` and
    `nextBeforeAttemptNumber`), `…/{attemptId}/evidence` (bounded metadata for the four allowlisted
    artifact purposes, filtered by the artifact's own run, attempt, and purpose), and
    `…/{attemptId}/evidence/artifact-window/{purpose}` (bounded, integrity-verified sealed text). Unknown,
    foreign-run, and non-Agent attempts are 404; `AgentAttemptIdentity.IsCoherent` (defined status/role/
    provider/contract, a launched role/provider pair, contract belongs to role, well-formed assignment)
    gates disclosure: an incoherent or unreadable row (a persisted status/role/provider/contract string EF cannot convert is read through non-enum columns first, then guarded) is listed in place without identity, evidence returns `identityValid: false`,
    and the window returns a new `AttemptIdentityInvalid` status with no text. The window route shares one
    `SealedAgentArtifactWindowReader` (artifact-row filter plus `IArtifactStore.VerifyAndReadSealedAsync`) with
    the unchanged message-linked route, so bytes, statuses, containment, and integrity behavior are identical.
    Metadata and the window envelope carry no path, hash, session identifier, or prompt (tests assert no such field); a verified window's `text` is the captured content returned exactly, so it may itself contain such-looking text. The cockpit's Usage & Evidence rail adds a
    collapsed-by-default history (paged, retry-safe), a single selected-attempt drill-down fetched on demand,
    and a purpose-gated viewer with manual next-window loading, literal text rendering, purpose-specific
    caveats, and full reset on close, run, attempt, or purpose change. No provider call, CLI argument,
    session/resume, control, claim/dispatch, workflow, schema/migration, or Process artifact changed.
  - Checks run: final solution build 0 errors/0 warnings (three build file-locks were observed and each cleared on
    rebuild: two Api-project locks on earlier initial builds and one `VBCSCompiler`/Application lock in the final correction round); Domain 612/612; Application 1197/1197; Infrastructure 583
    passed, 2 skipped (existing host-capability skips); Api 419/419; Architecture 9/9; frontend `vitest`
    757/757, `tsc -b`, `npm run build` clean, `oxlint` with no warnings in new or touched files (the
    pre-existing `useAgentAttemptStatus` warning is unchanged); repeated Api builds left `api-client.ts`
    byte-identical (SHA-256 `ac34b466…`, regenerated because status fields became nullable); local documentation links resolve; `git diff --check` clean apart from
    the known generated-file line-ending notices. Mutation checks (each failed the targeted tests, then
    restored): cursor `<` to `<=`, coherence check bypass in the window handler, dropped `RunId` in the shared
    artifact filter, dropped `RunId` in the evidence artifact filter; the malformed-enum tests (unrecognized persisted status, role, provider, or contract string on all three routes) failed before the read guard existed and pass after. Tests use deterministic doubles and the
    real artifact store; no real provider is called.
  - Remaining risks: history and evidence describe locally recorded past attempts only and infer no
    provider capability, account allowance, or resumability; sealed text may still contain secrets the
    best-effort redaction missed; the artifact store's documented containment limits (non-Windows lexical
    containment, no write-path hardening) are unchanged; an `AgentAttemptIdentity` rule stricter than a
    future role/provider pair would hide that attempt until the rule is updated; the read guard catches any `InvalidOperationException` raised during full-row materialization and cannot prove it came specifically from enum conversion, so it cannot distinguish a corrupt value from another materialization fault of the same type.
  - Post-publication verification: after a GO and publication, rerun the focused history, evidence, and
    window endpoint tests, the identity tests, the message-linked sealed-window tests, and the frontend
    history panel tests against the delivered commit.
- Published delivery: `f2ec6155db28777bd164a33f7646677eef4e0c49` (parent
  `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`) was committed with the reviewed 73-file Claude effort-request
  slice (67 modified, 6 new), pushed as a normal fast-forward to `origin/main`, and verified with
  `git fetch origin main` and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all
  matched the delivered SHA, with a clean working tree. Solution build 0 errors/0 warnings (the earlier
  unexplained build error did not recur). Focused checks rerun against that commit, all passing: Domain 238,
  Application 243, Infrastructure 95, Api 108 (Claude claim snapshot and commit-window race, adapter
  argument, supervisor replay, set/clear endpoint, and migration tests). This closure records the delivered
  SHA and those checks only; no code or product contract changed after publication.
- Current delivery, based on verified parent
  `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`: an optional, explicit, run-scoped Claude
  **effort request** (`low`, `medium`, `high`) paired with the existing model-alias request for the
  CriticalReviewer, Implementer, and ReviewCorrection roles. See [planner-handoff.md](planner-handoff.md)
  for the selection and the
  ["Explicit Claude effort requests"](../architecture/agent-collaboration-protocol.md#explicit-claude-effort-requests)
  and
  ["Explicit Claude effort requests"](../product/run-cockpit-specification.md#explicit-claude-effort-requests)
  sections for the contract.
  - Behavior: nullable `Run.RequestedClaudeEffort` (additive migration `AddClaudeEffortPreference`, no
    default or backfill) and a shared `ClaudeModelRequest.IsValid` pair rule: an effort is valid only with
    an explicit `sonnet` or `opus`; `haiku` or no model requires a null effort; case-sensitive closed sets.
    The existing protected `POST /api/runs/{runId}/claude-model-preference` accepts `requestedModel` and
    `requestedEffort` as one pair (bad pair 400, terminal run 422, concurrent change 409) and persists it
    with one `run.claude_model_preference_changed` event whose payload now carries both values. The effort
    column is an EF concurrency token like the model column, so an effort-only race also rolls back. In all
    three claim paths `CurrentClaudeModelPreference.ReadAndGuardAsync` reads both values late (after Git
    evidence and manifest work) and guards them at the single claim commit; the pair is snapshotted into the
    existing immutable `Attempt.AgentRequestedModel`/`AgentRequestedEffort` (no new Attempt column), and a
    change between read and commit rolls the claim back and deletes the sealed manifest. Dispatch and
    restart replay use the Attempt snapshot only. `ClaudeModelRequestArguments` appends discrete `--model`
    then `--effort` arguments only for a valid non-null snapshot; null and model-only argument lists are
    unchanged and an invalid persisted pair fails before any process starts. The cockpit adds
    `requestedClaudeEffort` and `latestAgentAttempt.requestedEffort`; the control has an effort select
    (disabled unless sonnet/opus), Save/Clear send the pair, and a successful Save or Clear refreshes the
    authoritative cockpit through `useRunCockpit().refresh` (stale run or failed refresh shows a fixed safe
    message). All new fields are labelled requests; this slice derives no observed or effective effort from
    `--effort` (existing provider-reported observed facts elsewhere are unchanged). Claude's
    documented `--effort` support depends on model and organization limits, and a provider may reject or
    silently adjust a request; that is a limitation, never an observation. No Codex path, permission, tool,
    schema, session argument, claim budget, token warning, model catalog, or fallback was changed.
  - Checks run: solution build 0 errors/0 warnings; Domain 612/612; Application 1186/1186;
    Infrastructure 583 passed, 2 skipped (the two pre-existing host-capability skips); Api 400/400;
    Architecture 9/9; frontend `vitest` 739/739, `tsc -b`, `npm run build`, and `oxlint` with no warnings in
    touched files (pre-existing warnings elsewhere are unchanged); repeated API builds left `api-client.ts`
    byte-identical (SHA-256 `1faf2f68…267b`); `git diff --check` clean apart from the known generated-file
    line-ending notices; local documentation links resolve. Mutation checks: removing the effort concurrency
    token failed the four effort-only race tests (set handler and the three claim handlers), and dropping the
    effort original-value refresh failed the three capture-window snapshot tests; both restored. Tests use
    deterministic doubles and never call a real provider.
  - Remaining risks: effort support by model, account, and organization is unproven and not discovered; a
    silent provider adjustment is unobservable; a concurrent preference change can make a token-warning
    threshold save return a retryable 409 (shared Run concurrency tokens).
  - Post-publication verification: after a GO and publication, rerun the focused Claude claim, adapter,
    supervisor replay, set/clear endpoint, and migration tests against the delivered commit.
- Published delivery: `46c33900089eb4ad4d29f6fc440fe96a9158fe7c` (parent
  `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`) was committed with the reviewed 48-file
  per-provider token-activity warnings slice (22 modified, 26 new), pushed as a normal fast-forward to
  `origin/main`, and verified with `git fetch origin main` and `git ls-remote`: local `HEAD`, local
  `origin/main`, and the live remote all matched the delivered SHA, with a clean working tree. Focused
  checks rerun against that commit, all passing: Domain 11, Application 42 (threshold set/clear and
  concurrency, warning projection, persisted-evidence cockpit, and claim-unaffected tests), Infrastructure
  1 (migration), Api 13 (endpoint), and frontend 84 across the warning panel, threshold hook, cockpit
  refresh, and cockpit view. This closure records the delivered SHA and those checks only; no code or
  product contract changed after publication.
- Current delivery, based on verified parent
  `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`: per-provider, run-scoped advisory
  token-activity warnings. See [planner-handoff.md](planner-handoff.md) for the selection and the
  ["Per-provider run token-activity warnings"](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-warnings)
  and
  ["Per-provider token-activity warnings"](../product/run-cockpit-specification.md#per-provider-token-activity-warnings)
  sections for the contract.
  - Behavior: nullable, independent `Run.CodexTokenWarningThreshold` and
    `Run.ClaudeTokenWarningThreshold` (additive migration `AddProviderTokenWarningThresholds`,
    no default or backfill); protected `POST /api/runs/{runId}/token-warning-threshold` sets or
    clears one provider (positive, at most 10^12; bad provider/value 400; terminal run 422) with an
    atomic `run.token_warning_threshold_changed` event, guarded by the existing `Run.Lifecycle`
    concurrency token (the threshold columns are deliberately not tokens, so a threshold write cannot
    fail an Agent claim). The cockpit adds `tokenWarnings` (two entries) from the existing dispatched-
    attempt evidence: Codex counts input + output (cache not re-added); Claude Code counts input +
    cache-creation + cache-read + output and treats a row missing either cache count as insufficient
    (the raw usage view is unchanged). At or above the threshold warns (equality included, as a lower
    bound with gaps); below with pending, insufficient, or unattributed attempts is `Indeterminate`
    ("not an all-clear"); a known zero and `NoEvidence` are distinct; undispatched attempts are
    excluded and unattributed ones are a gap for both providers. Cockpit shows a per-provider control
    and prominent warning states; because the endpoint emits no run event, a successful save or clear
    explicitly refreshes the cockpit through a new generation-safe `useRunCockpit().refresh` (stale-run
    responses discarded; a failed refresh shows a fixed safe message), and the local input check mirrors
    the backend's inclusive 1..10^12 range. No claim path, dispatch, adapter, budget, model setting, or
    permission argument was touched (none of those files changed).
  - Checks run: solution build 0 errors/0 warnings; Domain 579/579; Application 1152/1152;
    Infrastructure 552 passed, 2 skipped (the file-leaf symlink test and the reparse-point entrypoint
    test, host-capability skips that pre-date this slice); Api 388/388; Architecture 9/9; frontend
    `vitest` 724/724, `tsc -b`, and `npm run build` clean, and `oxlint` with no warnings in new files (the
    one warning in a touched file is the `set-state-in-effect` in `useRunCockpit.ts` that the pre-change
    file also has); two consecutive API builds left `api-client.ts` byte-identical (SHA-256 `b268965e…2f8e`);
    local documentation links resolve; `git diff --check` clean apart from the known generated-file
    line-ending notices. Mutation check: forcing the handler's Run UPDATE off failed the
    lifecycle-race, other-provider, and same-provider concurrency tests; restored. Tests use
    deterministic doubles and never call a real provider.
  - Remaining risks: advisory only, over locally recorded usage; it does not see sessions or usage the
    host never recorded. Any dispatched attempt without a known provider keeps both providers
    `Indeterminate` (deliberately conservative). A concurrent Claude model-request change can make a
    threshold save return a retryable 409. Provider usage accuracy rests on the existing versioned
    parsers.
  - Post-publication verification (done): see the published-delivery entry above.
- Published delivery: `8ddc34284c3c5461510090c06a895cce9871966d` (parent
  `7cc091dcaa032740def1cca214c3ad83c09a5cd2`) was committed with the reviewed
  73-file Claude model-alias request slice (51 modified, 22 new), pushed as a
  normal fast-forward to `origin/main`, and verified with `git fetch origin main`
  and `git ls-remote`: local `HEAD`, local `origin/main`, and the live remote all
  matched the delivered SHA, with a clean working tree. Focused checks rerun
  against that commit, all passing: Domain 22, Application 32, Infrastructure 25,
  Api 20 (Claude claim-snapshot and commit-window race, adapter argument, set/clear
  endpoint, migration, and supervisor replay/no-fallback tests). This closure
  records the delivered SHA and those checks only; no code or product contract
  changed after publication.
- Current delivery, based on verified parent
  `7cc091dcaa032740def1cca214c3ad83c09a5cd2`: explicit, run-scoped Claude
  model-alias requests (`sonnet`, `opus`, `haiku`) for the CriticalReviewer,
  Implementer, and ReviewCorrection roles. See [planner-handoff.md](planner-handoff.md)
  for the selection and the
  ["Explicit Claude model-alias requests"](../architecture/agent-collaboration-protocol.md#explicit-claude-model-alias-requests)
  and
  ["Explicit Claude model-alias requests"](../product/run-cockpit-specification.md#explicit-claude-model-alias-requests)
  sections for the contract.
  - Behavior: nullable `runs.RequestedClaudeModel` (additive migration, no default
    or backfill; historical runs stay `NULL`); protected
    `POST /api/runs/{runId}/claude-model-preference` sets or clears it (closed-set
    validation, terminal runs rejected, durable `run.claude_model_preference_changed`
    event); each of the three claim handlers snapshots the value into the Attempt's
    existing immutable `AgentRequestedModel` as its last step before its single
    commit, guarded by the Run's `RequestedClaudeModel`/`Lifecycle` concurrency
    tokens so a change after the read rolls the claim back
    (`agent_attempts.run_changed_during_claim`, sealed manifest removed); the
    eligible-attempt queries, invocation requests, and supervisors carry the
    Attempt's snapshot, never the Run's current value; each adapter appends
    `--model <alias>` only for a non-null, closed-set snapshot (anything else fails
    closed before any process starts). Cockpit shows the run's request and the
    latest Claude attempt's own snapshot, labeled as requests, never observed.
    Provider rejection stays an ordinary recorded failure (no fallback). Codex
    behavior, Claude permission/tool/schema arguments, budgets, and authorization
    are unchanged; no `--effort`, catalog, or historical backfill.
  - Checks run: solution build 0 errors/0 warnings; Domain 568/568; Application
    1110/1110; Infrastructure 551 passed, 2 skipped (the file-leaf symlink test and
    the reparse-point entrypoint test, both host-capability skips that pre-date
    this slice); Api 375/375; Architecture 9/9; frontend `vitest` 693/693,
    `tsc -b`, `oxlint` (no warnings in touched files), and `npm run build` clean;
    two consecutive API builds left `api-client.ts` byte-identical
    (SHA-256 `92d40878…4929`); local documentation links (65) resolve;
    `git diff --check` clean apart from the known generated-client line-ending
    notice. Mutation check: removing the guard's `IsModified` line failed the three
    commit-window race tests and the set-handler tests; restored. In one full-solution
    run a `ChildProcessExecutionAdapterTests` process-tree test failed while the
    frontend suite ran concurrently; it passed 3/3 in isolation and in a clean full
    Infrastructure rerun (unrelated, timing-sensitive). Tests use deterministic
    doubles and never call a real provider.
  - Observations and risks: the installed `claude` 2.1.276 `--help` documents
    `--model <model>` with alias examples `fable`, `opus`, `sonnet` (not `haiku`);
    the closed set follows the official CLI reference and no alias is proven
    available to this account, so a provider rejection is an expected, recorded
    failure. A null attempt snapshot means "no request recorded" for both a
    request-free claim and a pre-feature attempt; the cockpit cannot and does not
    distinguish them. No real provider invocation was made.
  - Post-publication verification (done): see the published-delivery entry above.
- Published delivery: `447a650636eb2d0e26e34a356b379045a1181e52`
  (parent `26ae6bcb8df98d3d589b0ab5264a0be135353da6`) was committed with the
  reviewed 8-file slice, pushed as a normal fast-forward to `origin/main`, and
  verified against the live remote with `git fetch origin main`: local `HEAD`,
  local `origin/main`, and fetched `origin/main` matched the delivered SHA, with
  a clean working tree. This closure records the delivered SHA only; no code or
  product contract changed after publication.
- Current delivery, based on verified parent
  `26ae6bcb8df98d3d589b0ab5264a0be135353da6`: physical, case-sensitive
  containment hardening of `FilesystemArtifactStore.VerifyAndReadSealedAsync`,
  the sealed-store read boundary the sealed Agent-artifact inspection below
  exercises. See [planner-handoff.md](planner-handoff.md) for the selection and
  the [architecture contract](../architecture/agent-collaboration-protocol.md#sealed-agent-artifact-window-inspection).
  - Code path: the candidate file is opened once and that one handle is used for
    the containment proof, the whole-file length/SHA-256, and the bounded
    window; nothing is reopened by path. On Windows the handle's real path
    (`GetFinalPathNameByHandleW`, via the new internal `WindowsFinalPathResolver`)
    must lie under the root's freshly resolved real path. Both this comparison
    and the lexical `ResolveWithinRoot` comparison are now ordinal
    (case-sensitive), so a case-distinct sibling of the root fails closed. A
    legitimately redirected root is accepted when the opened file remains within
    its resolved target. The `IArtifactStore` contract, `Missing`/
    `IntegrityMismatch` meanings, cancellation, byte cap, and cursor behavior
    are unchanged; capture, sealing, cleanup, partial reads, schema, routes, UI
    and provider behavior are untouched.
  - Tests executed here: `FilesystemArtifactStoreTests` 26 passed, 1 skipped.
    New and passing: ordinary Agent artifact read; multi-window read
    reconstructing the exact hashed content; intermediate-directory junction to
    an outside sentinel with matching length/hash returns `Missing`; legitimately
    junctioned root still reads; `..` route into an upper-cased spelling of the
    root returns `Missing`; and a junction to a real case-distinct sibling
    directory (host supports per-directory case sensitivity) returns `Missing`.
    Both case tests were confirmed to fail when the comparisons are temporarily
    reverted to case-insensitive, then restored. **Skipped here:** the sealed-
    file-leaf symlink test (this host cannot create a file symlink without
    elevation/Developer Mode), so that path is covered by code-path reasoning
    only, not by an executed test.
  - Full validation: solution build 0 warnings/0 errors; Domain 547/547;
    Application 1078/1078; Infrastructure 526 passed/2 skipped (the symlink test
    above and the pre-existing `PackageEntrypointResolverTests` skip); Api
    355/355 (includes the two API callers of this boundary, safe statuses, no
    path/hash leakage); Architecture 9/9; generated client unchanged;
    `git diff --check` clean. No frontend change, so frontend suites were not
    rerun. Tests never call a real provider.
  - Remaining risk: physical containment is proven only on Windows; other
    platforms rely on the lexical proof. The proofs describe the file at open
    time and do not defend against a privileged actor altering files in place
    afterward. Other Increment 4 items in [the roadmap](mvp-delivery-plan.md)
    are unchanged.
  - Post-publication verification: confirm `main`, local `origin/main`, and the
    live remote match the delivered commit with a clean tree, then rerun
    `FilesystemArtifactStoreTests` against it.
- Published delivery: `77cae0a4bbdb811f65625c7552f18f88dc7ee96e`
  (parent `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`) was committed with
  the reviewed 18-file slice, pushed as a normal fast-forward to `origin/main`,
  and verified against the live remote with `git fetch origin main`: local
  `HEAD`, local `origin/main`, and fetched `origin/main` matched the delivered
  SHA, with a clean working tree. This closure records the delivered SHA only;
  no code or product contract changed after publication. (Historical: at this
  closure's own time, the planner had not yet selected another slice; it has
  since selected the sealed-store physical-containment hardening slice
  recorded at the top of this checkpoint.)
- Published delivery: `fc06b348ff6824a72fc06ade9d45e09a59c00501`
  (parent `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`) was committed with
  the reviewed 78-file slice, pushed as a normal fast-forward to `origin/main`,
  and verified against the live remote with `git fetch origin main`: local
  `HEAD`, local `origin/main`, and fetched `origin/main` matched the delivered
  SHA, with a clean working tree. This closure records the delivered SHA only;
  no code or product contract changed after publication. (Historical: at this
  closure's own time, no next slice was selected; the planner has since
  selected the sealed Agent-artifact inspection slice recorded at the top of
  this checkpoint.)
- Current delivery, based on verified parent
  `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`: bounded, integrity-verified
  inspection of a sealed Agent-attempt artifact's own text, extending the
  existing collaboration evidence drill-down. See
  [planner-handoff.md](planner-handoff.md) for the selection record, and the
  ["Sealed Agent-artifact window inspection"](../architecture/agent-collaboration-protocol.md#sealed-agent-artifact-window-inspection)
  and
  ["Sealed Agent-artifact window inspection"](../product/run-cockpit-specification.md#sealed-agent-artifact-window-inspection)
  sections for the exact contract and product semantics.
  - New Application query `GetSealedAgentArtifactWindowQuery`/
    `GetSealedAgentArtifactWindowQueryHandler` resolves the requested
    `(RunId, MessageId)` collaboration message exactly like
    `GetCollaborationMessageEvidenceQueryHandler` (same `ProviderObserved`
    provenance check, same `AttemptId`-foreign-key-only resolution, same
    role/provider coherence check — repeated as this operation's own rule,
    not an extracted shared helper), then resolves an `Artifact` row by
    matching `AttemptId`, `RunId`, and the requested `Purpose` together,
    restricted server-side to a closed four-value allowlist
    (`AgentContextManifest`, `AgentStandardOutput`, `AgentStandardError`,
    `AgentFinalResponse` — never the two Process-attempt purposes). Reuses
    the existing `IArtifactStore.VerifyAndReadSealedAsync` boundary
    unchanged: the entire sealed file's length/hash is verified before any
    byte of the requested window is returned, and its existing path
    resolution already rejects a missing file and a stored path that is
    absolute or that lexically resolves, through `..` segments, outside the
    artifact root — no sealed-store hardening was needed for this slice, and
    this newly reachable window carries no new threat this shared boundary
    did not already face from its existing callers. Every non-content
    outcome (no agent evidence, a broken attempt
    link, a disallowed purpose, no recorded artifact, a missing sealed file,
    a failed integrity check) is its own explicit, distinct status; a
    genuinely unknown run/message pair fails with the existing
    `collaboration_messages.not_found` error. No storage path, content hash,
    or other raw diagnostic ever crosses the API boundary.
  - New protected `GET
    api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/{purpose}`
    endpoint (`GetSealedAgentArtifactWindowEndpoint`), restricting `purpose`
    to the same four route segments (`context-manifest`, `stdout`, `stderr`,
    `final-response`) with an unrecognized segment as a safe 404, and reusing
    `GetProcessAttemptOutputEndpoint`'s exact `fromOffset`/`maxBytes`
    clamping (16 KiB default, 64 KiB hard cap, 64-byte floor). Additive-only
    NSwag client regeneration (`GetSealedAgentArtifactWindowEndpointClient`,
    `SealedAgentArtifactWindowResponse`).
  - New `useSealedAgentArtifactWindow` hook (on-demand fetch, accumulates
    text across manually requested windows, resets whenever
    `(runId, messageId, purpose)` changes — mirroring
    `useCollaborationMessageEvidence`'s own render-time reset pattern) and a
    new `AttemptArtifactWindowViewer` component added to the existing
    `CollaborationEvidenceDrilldown`: a purpose selector populated only from
    that attempt's own already-loaded, bounded artifact-metadata list, a
    manual "Load"/"Load next window" action, and every fetched window
    rendered as literal React text content (never HTML/Markdown
    interpretation). `CollaborationEvidenceDrilldown`'s own `<details>`
    element is now a controlled component (`isOpen` state) so the viewer can
    be keyed on the open/closed transition — closing and reopening the
    drawer remounts it, discarding any prior purpose selection and fetched
    text, alongside the existing run/message reset. Never written to
    `localStorage`/`sessionStorage`/the URL/logs.
  - **Corrected in review**: `useSealedAgentArtifactWindow`'s retry action
    for a failed later window (one requested after at least one window had
    already loaded) previously always retried at offset 0, which would have
    re-fetched and re-appended the already-accumulated earlier text on top
    of itself rather than actually retrying the window that failed. A new
    `lastRequestedOffsetRef` records the offset most recently requested,
    independent of `state`, and retry now re-requests that exact offset; a
    failure never touches the accumulated text in the first place. A new
    deterministic test loads a first window, fails the second, retries, and
    asserts the exact sequence of requested offsets (`0`, then the failed
    offset again on retry — never `0` again) and the exact reconstructed
    text (no duplication, no omission).
  - **Corrected in review**: the viewer's single sensitivity caveat
    previously described every one of the four purposes as "best-effort
    redacted provider text," which is false for `AgentContextManifest` — it
    is composed entirely by DevalCopilot's own application code from
    already-curated durable fields (`Artifact.Sensitivity.HostConstructedContent`,
    set at the point `CreateCodexPlanningAttemptCommandHandler` and its
    sibling claim handlers record it), never raw provider output. The
    viewer, and both new specification sections, now state two distinct,
    purpose-matched caveats — a host-constructed caveat for the context
    manifest and a best-effort-redacted-provider-text caveat for the other
    three (`Artifact.Sensitivity.RedactedBestEffort`, set in
    `RecordAgentAttemptResultCommandHandler`) — chosen from the already-known,
    closed-allowlist purpose string the component already holds; no new
    sensitivity API, wire field, or schema was added. Both caveats
    explicitly warn that sensitive content may still remain, never that
    either purpose is proven safe.
  - **Corrected in review**: the architecture and this checkpoint previously
    described the sealed-store path resolution as "containment-checked"
    without qualification. `FilesystemArtifactStore`'s path check is a
    purely lexical/textual operation (`Path.GetFullPath` plus a string-prefix
    comparison) — proven, by existing tests, to reject an absolute or
    `..`-escaping stored relative path, but never proven, and not capable
    without a filesystem probe, to detect a reparse point (a symlink or
    junction) along the resolved path that the operating system would
    actually follow outside the artifact root. The architecture doc now
    states precisely what is proven and records the reparse-point gap as an
    open risk below, rather than an implied guarantee. This is an existing
    limitation of the shared sealed-store boundary every other sealed-read
    caller already depends on unchanged; this slice reads through that same
    boundary and introduces no new write path or new artifact-root threat,
    so no store hardening was added — broadening this correction into a
    sealed-store fix was judged out of this slice's bounded scope rather
    than reported as unsafe to proceed without.
  - Checks actually run: Application.Tests focused
    `GetSealedAgentArtifactWindowQueryHandlerTests` 9/9, full 1078/1078;
    Api.IntegrationTests focused `GetSealedAgentArtifactWindowEndpointTests`
    16/16 (all four purposes; a genuine multi-window UTF-8 split boundary
    with a monotonic cursor reconstructing the exact original text; an
    unrecognized purpose segment; a negative offset; an unknown message; a
    message on a different run; a non-`ProviderObserved` message; an
    incoherent role link; no recorded artifact for the requested purpose; a
    recorded artifact whose sealed file was never written; a path-escaping
    stored relative path; a tampered recorded byte length; and that no
    response ever discloses a storage path, content hash, or exception
    detail), full 355/355; Domain.Tests 547/547 (unchanged; no Domain
    change in this slice); Infrastructure.IntegrationTests 520/521 (1
    pre-existing, unrelated skip; unchanged — no Infrastructure change in
    this slice, `IArtifactStore` was reused as-is); Architecture.Tests 9/9.
    Frontend focused viewer and drill-down tests (11 new
    `AttemptArtifactWindowViewer` cases, including the retry regression
    added in review, plus 2 new `CollaborationEvidenceDrilldown` cases),
    full suite 679/679; `tsc -b` clean; `oxlint` exited 0 with the same 20
    pre-existing warnings (0 new); production build (`vite build`) passed.
    NSwag client regenerated by `dotnet build src/backend/DevalCopilot.Api`
    with an identical SHA-256 hash on a repeat build (no drift). `git diff
    --check` is clean apart from the existing generated-client
    CRLF-normalization warning. Local documentation links/anchors
    (`#sealed-agent-artifact-window-inspection` in both new specification
    sections) resolve. Automated tests never call a real provider. Only the
    frontend hook, its test, the viewer, and documentation changed in the
    review-correction round; the backend query/handler/endpoint were
    unchanged, so the full backend suites above are carried over from the
    same round that produced them and were not rerun again for this
    correction.
  - Remaining risk: the sealed-store path check
    `FilesystemArtifactStore.ResolveWithinRoot` performs is purely
    lexical/textual (`Path.GetFullPath` plus a string-prefix comparison
    against the artifact root) and is proven only to reject an absolute or
    `..`-escaping stored relative path — it does not detect, and cannot
    detect without an explicit filesystem probe, a reparse point (a symlink
    or junction) along the resolved path that the operating system would
    actually follow to a location outside the artifact root. This is a
    pre-existing limitation of the shared sealed-store boundary every
    other sealed-read caller (`GetProcessAttemptOutputEndpoint`,
    `GetVerificationExecutionOutputQueryHandler`) already depends on
    unchanged, not one this slice introduces; this slice's own review
    judged the newly reachable window carries no new threat against that
    same, unchanged artifact-root threat model, so no store hardening was
    added here. Closing the reparse-point gap itself remains open under
    [Increment 4](mvp-delivery-plan.md), alongside every other open item
    there.
  - Post-publication verification action: after this delivery is committed
    and pushed, confirm branch `main`, local `origin/main`, and the live
    remote (`git fetch origin main`) all point to the delivered commit with
    a clean working tree, then reconfirm the focused
    `GetSealedAgentArtifactWindowQueryHandlerTests` (9/9) and
    `GetSealedAgentArtifactWindowEndpointTests` (16/16) suites against that
    published commit.
- Current delivery, based on verified parent `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`:
  explicit, run-scoped Codex model and reasoning-effort requests for future
  Codex Planner, Challenge Resolver, and Code Reviewer attempts. See
  [planner-handoff.md](planner-handoff.md) for the selection record, and the
  ["Explicit Codex model and reasoning-effort requests"](../architecture/agent-collaboration-protocol.md#explicit-codex-model-and-reasoning-effort-requests)
  sections of the agent-collaboration-protocol and
  [run-cockpit-specification.md](../product/run-cockpit-specification.md#explicit-codex-model-and-reasoning-effort-requests)
  for the exact durable/invocation and product semantics.
  - New `Run.RequestedCodexModel`/`RequestedCodexEffort` (nullable strings,
    bounded at 128 characters, no default, no backfill — a historical Run
    truthfully has no explicit preference) with a new `Run.SetRequestedCodexAssignment`
    method (the first-ever post-construction mutation of a non-lifecycle Run
    field): permitted only while `Lifecycle` is `Created` or `Running`,
    rejects an effort without a model, and performs no I/O or catalog
    validation itself — that business rule is the calling command handler's
    responsibility, since Domain never performs I/O. New EF Core migration
    `AddCodexAssignmentPreference` adds the two nullable, bounded columns
    additively.
  - New protected `SetCodexAssignmentPreferenceCommand`/Handler/Validator
    validate a non-null pair against one fresh, bounded observation from the
    existing `ICodexModelCatalogAdapter` (the same vetted launch target and
    catalog contract the read-only catalog slice established): the model must
    be a currently visible observed id, and a non-null effort must belong to
    that entry's own known supported set. The catalog's suggested default is
    never auto-selected. An `Unknown`/unavailable catalog, an invisible model,
    or an unsupported effort each fail closed with their own stable error
    code; clearing (`null` model) never reads the catalog. A durable
    `run.codex_assignment_preference_changed` event (new `RunEventType`
    constant) records only the new requested pair, actor `ParticipantIdentity.ForHuman()`.
    New `POST api/runs/{runId}/codex-assignment-preference` endpoint and
    additive NSwag client regeneration.
  - **Corrected in review**: the command is `IManualTransactionCommand`
    (mirroring the existing `CreateCodexPlanningAttemptCommand` precedent),
    never a plain `ICommand` — the mediator's automatic EF transaction
    behavior must never wrap a bounded external Codex App Server process
    round trip. The handler now performs an untracked pre-check read (advisory
    only: avoids a wasted catalog round trip for an already-missing or
    already-terminal run), then the catalog observation with no tracked
    entity and no pending database write, then reads the Run afresh (a
    genuinely new tracked query, never the untracked pre-check row) inside
    one short, tightly scoped write that re-validates the same terminal-
    lifecycle race the pre-check cannot close by itself, applies the
    preference, appends its event, and commits both together with a single
    explicit `SaveChangesAsync` call — the handler owns this call itself,
    exactly like every other `IManualTransactionCommand` handler in this
    codebase. Proven by a new mediator-level, real-file-backed-SQLite
    `SetCodexAssignmentPreferenceTransactionBoundaryTests` (mirroring the
    established `CreateReviewCorrectionAttemptTransactionBoundaryTests`
    pattern): a fake catalog adapter observes the actual handler `DbContext`
    and records whether `Database.CurrentTransaction` was null at the moment
    of its own invocation, and a fresh, separate read afterward confirms the
    Run's preference and its event both persisted together.
  - **Corrected in review again**: the fresh read and the single
    `SaveChangesAsync` above still left one narrower gap open — a lifecycle
    transition committed by a wholly different transaction in the interval
    between that read and this handler's own save, with no further I/O of the
    handler's own in between to re-observe it. Rather than wrapping that short
    gap in an explicit multi-statement transaction, `Run.Lifecycle` is now
    configured as an EF concurrency token (`RunConfiguration`, migration
    `MarkRunLifecycleAsConcurrencyToken`, an empty-body migration since a
    concurrency token is metadata-only and changes no column): the `UPDATE`
    this handler's save produces now requires the exact `Lifecycle` value its
    own fresh read observed, so a concurrent transition committed in that gap
    makes the `UPDATE` match zero rows and throws
    `DbUpdateConcurrencyException`, caught and reported identically to the
    ordinary in-memory lifecycle rejection — the failed save rolls back the
    whole batch, so neither the preference nor its event persists. Proven by
    two new `SetCodexAssignmentPreferenceTransactionBoundaryTests` cases: one
    drives the real mediator with a fake catalog adapter that, from inside its
    own `ObserveAsync`, opens a second `DevalCopilotDbContext` against the
    same file-backed database and commits a real `Run.Fail` transition before
    returning — modeling a transition landing during the catalog round trip,
    which the handler's own post-observation fresh read already correctly
    rejects; the other drives two separate `DevalCopilotDbContext` instances
    directly (no catalog-adapter hook exists inside the narrower gap itself)
    to prove the concurrency token, not an explicit transaction, is what makes
    a transition landing between the authoritative read and the save throw
    `DbUpdateConcurrencyException` instead of silently overwriting the newer
    state. Both assert neither the preference nor its event persisted.
  - `GetRunCockpitQueryResult`/`GetRunCockpitResponse` gained
    `RequestedCodexModel`/`RequestedCodexEffort` (the Run's own current
    preference — never an effective or observed value) alongside the existing
    projection fields.
  - `Attempt` gained three new `...WithAssignment` factory overloads
    (`ClaimAgentWithAssignment`, `ClaimAgentChallengeResolutionWithAssignment`,
    `ClaimAgentCodeReviewWithAssignment`), mirroring the existing
    `ClaimAgentImplementationWithAssignment` precedent exactly: each validates
    the requested model/effort with the existing shared
    `ValidateAssignmentIdentifier`, and — **corrected in review** — each also
    now enforces the same effort-requires-model invariant
    `Run.SetRequestedCodexAssignment` already enforces, via a new shared
    private `ValidateRequestedAssignmentPair` helper (deliberately not applied
    to the existing, out-of-scope `ClaimAgentImplementationWithAssignment`),
    before setting `AgentRequestedModel`/`AgentRequestedEffort` on the new
    immutable attempt. The original three factories
    (`ClaimAgent`, `ClaimAgentChallengeResolution`, `ClaimAgentCodeReview`) are
    now thin convenience overloads delegating to the new ones with
    `requestedModel: null, requestedEffort: null` — every existing call site
    and test is unchanged. The three `Create*Attempt` command handlers pass
    the Run's requested model/effort into the new claim overloads; the
    existing generic `RecordAgentObservedAssignment` method already covers
    recording a genuinely provider-observed model/effort later and needed no
    change.
  - Claim-time assignment freshness and its atomic claim boundary, delivered
    facts as they stand now: each of the three `Create*Attempt` handlers
    (`CreateCodexPlanningAttemptCommandHandler`,
    `CreateChallengeResolutionAttemptCommandHandler`,
    `CreateCodeReviewAttemptCommandHandler`) reads the Run's requested Codex
    model/effort pair via a shared `CurrentCodexAssignmentPreference.ReadAsync`
    helper (a fresh, untracked query) only after all external work — Git
    evidence capture and artifact sealing — has completed, immediately before
    building the claimed `Attempt`, never from the stale `Run` instance each
    handler loaded at the top of the method. From that point, one short,
    explicit `IDevalCopilotDbContext.BeginTransactionAsync` transaction —
    opened only after the external work, so it never spans it — covers the
    rest of the claim boundary as a single atomic operation: a
    `CurrentCodexAssignmentPreference.ConfirmUnchangedAsync` guard (one
    `ExecuteUpdateAsync` statement whose `WHERE` clause re-checks the pair
    against the Run's current row), the Attempt/artifact/input-message/
    verification-evidence inserts, `SaveChangesAsync`, and the transaction's
    own commit. A concurrent preference-only change can therefore no longer
    land between the guard and the Attempt's own durable commit.
  - Every step of that boundary — acquiring the transaction, the guard
    statement, `SaveChangesAsync`, and the commit — has its own bounded
    failure path, and every one of them deletes the already-sealed manifest
    artifact unless an `IAttemptDurabilityProbe` check confirms the Attempt
    durably persisted despite the failure: a transaction-acquisition failure
    or a raw guard-statement failure (both `System.Data.Common.DbException`)
    reports `attempts.persistence_failed` — a raw, unrelated database failure
    is never described as a confirmed preference change, which is reported
    only when the guard actually observes zero matching rows
    (`agent_attempts.assignment_preference_changed`). A `SaveChangesAsync`
    failure (`DbUpdateException`) keeps the existing race-classification
    logic (a competing Running attempt, a lost budget-slot race, or an
    unclassified `attempts.persistence_failed`) unchanged. A commit failure
    is resolved the same way a `SaveChangesAsync` failure already was, via
    the same probe. **Corrected in review**: that probe never queries the
    claim's own `IDevalCopilotDbContext` — the same connection whose
    transaction a best-effort rollback may have just failed to close cleanly
    is not a reliable read. `RollbackBestEffortAsync` now also releases
    (disposes) the transaction, swallowing either step's own failure, and the
    ambiguous-outcome question is instead answered by
    `AttemptDurabilityProbe` (`Application.Data.IAttemptDurabilityProbe`,
    implemented in Infrastructure): a brand-new `DevalCopilotDbContext` on its
    own independent connection, reading whether the Attempt row exists,
    bounded by its own 5-second timeout. The probe reports `Persisted`
    (success, delete nothing), `NotPersisted` (clean up, report
    `attempts.persistence_failed`), or `Unresolved` — its own bounded read
    itself failed or timed out, most often the same contention that made the
    original outcome ambiguous also blocking the probe. `Unresolved` asserts
    neither outcome: the sealed manifest is preserved exactly as for a
    confirmed `Persisted` result, and `attempts.persistence_unresolved` is
    reported rather than guessed. Rollback itself is attempted best-effort
    and never lets a secondary failure mask the primary one.
  - The connection's own SQLite lock-wait is bounded by Microsoft.Data.Sqlite's
    own unconfigured default (30 seconds, confirmed by direct out-of-process
    measurement) rather than an app-wide connection-string change, which was
    tried and reverted after it broke unrelated `Api.IntegrationTests` pool
    cleanup.
  - `CreateCodeReviewAttemptCommand` is `IManualTransactionCommand` (a
    pre-existing defect found and fixed this slice: it had been a plain
    `ICommand`, running its external Git evidence capture and artifact-sealing
    work inside the mediator's automatic per-command EF transaction), matching
    its two sibling claim commands exactly.
  - Each of the four boundary steps (transaction acquisition, guard,
    `SaveChangesAsync`, commit) also has its own `catch (OperationCanceledException)`,
    distinct from its `DbException`/`DbUpdateException` catch: cancellation is
    never converted into a `Result` (a business failure) and is always
    rethrown, but the already-sealed manifest artifact's ownership is still
    resolved first via the same `IAttemptDurabilityProbe` check and an
    unconditional token (the caller's own is already cancelled) — deleted
    only on a definite `NotPersisted`, retained on `Persisted` or
    `Unresolved` — before the cancellation is rethrown.
  - Tests: a claim-time-freshness test in all three handlers' test files
    (a race injected during external Git evidence capture, reusing the
    existing `RaceInjectingEvidenceReader` precedent, proves the claimed
    Attempt embeds the pair current at the claim boundary, and that a further
    preference change after the claim never reaches the already-claimed
    Attempt's own immutable snapshot); a post-read-interleaving test in all
    three files (drives `ConfirmUnchangedAsync` directly against an
    already-committed differing pair, asserting it returns `false` with no
    side effect); eight fault-injection tests in all three files
    (`FaultInjectingDbContext`, a decorator over the real `DevalCopilotDbContext`
    that can simulate, at transaction acquisition or at commit, a raw
    provider failure, a cancellation, or — for commit specifically — either
    before or after the underlying commit actually completes, each
    independently combinable with the handler's own best-effort rollback of
    that same transaction also throwing) proving the correct
    artifact-cleanup-or-retain decision through the real, independent
    `AttemptDurabilityProbe` — reading against the fixture's own database file
    via a genuinely separate `DevalCopilotDbContext`, never a fake — even when
    the rollback that precedes it fails, and, for the cancellation cases,
    that the exception genuinely propagates rather than being converted into
    a `Result`; and one mediator-independent
    `ClaimTimeAssignmentPreferenceGuardTests` proving the guard-and-commit
    transaction genuinely excludes a competing write. That test starts the
    competing write on a background `Task` — never synchronously awaited
    while the claim-side transaction holds its own lock — with a
    `DbCommandInterceptor` attached only to that competing connection. The
    interceptor signals one of two `TaskCompletionSource`s filtered by the
    intercepted command's own text: one for the competing connection's
    initial `SELECT` (its `SingleAsync` read), one for its later `UPDATE`
    (the actual preference write) — **corrected in review**: an earlier
    version signaled unconditionally on every intercepted command, so it
    could not prove the `SELECT` itself never triggered the write-attempt
    wait. The test now gates the racing task between its `SELECT` and its
    `UPDATE`, asserts the write-attempt signal is not yet completed once the
    `SELECT` alone has run, then releases the gate — so the filtering is
    itself an asserted behavior, not merely an inspected implementation
    detail. Because the `UPDATE` signal still fires immediately before that
    command runs, the test also waits a short, generously-margined
    confirmation window (200 ms, against a normal unblocked write's low
    single-digit milliseconds) before asserting the write is still
    incomplete — confirmed to fail without that confirmation window in a
    scenario with no real lock held, and confirmed to fail without the
    transaction fix itself in a scenario with one. Only after its own commit
    does the test await the competing write and assert it applied strictly
    afterward. A second test,
    `A_preference_only_write_with_no_competing_claim_transaction_completes_promptly`,
    is this methodology's own negative control: the identical interceptor,
    signal, and 200 ms confirmation window, but with no competing claim
    transaction ever opened, asserting the write *does* complete within the
    window — proving the positive test's own blocked-assertion is a genuine,
    two-sided discriminator rather than one that would trivially pass no
    matter what. `CreateCodeReviewAttemptTransactionBoundaryTests` proves the
    manual-transaction fix: a fake evidence reader observes
    `Database.CurrentTransaction` at its own invocation and asserts it null.
  - The three `Eligible*Attempt` read-model projections
    (`EligibleAgentAttempt`, `EligibleChallengeResolutionAttempt`,
    `EligibleCodeReviewAttempt`) and the three `*InvocationRequest` port
    records (`CodexPlanningInvocationRequest`, `ChallengeResolutionInvocationRequest`,
    `ImplementationReviewInvocationRequest`) gained `RequestedModel`/`RequestedEffort`
    (optional, defaulting to `null`, so no existing positional test call site
    needed updating). The three supervisors
    (`AgentAttemptSupervisor`, `ChallengeResolutionSupervisor`,
    `ImplementationReviewSupervisor`) now pass the claimed attempt's own
    snapshot fields into the invocation request — never the Run's own
    (possibly since-changed) mutable preference, including after a host
    restart, since the supervisors always dispatch from the durably claimed
    `Attempt` row.
  - `CodexProcessInvoker.Request` gained optional `RequestedModel`/`RequestedEffort`
    (defaulting to `null`, preserving every existing positional test call
    site). Each non-null value is independently revalidated against the same
    bounded, safe identifier character set the model-catalog adapter already
    enforces immediately before being placed on the command line — a
    defense-in-depth revalidation, never a raw pass-through, mirroring the
    existing launch-target revalidation. **Corrected in review**: the invoker
    now also independently re-enforces effort-requires-model as a second,
    boundary-level guard — an effort-only request (`RequestedModel: null`,
    `RequestedEffort` non-null) fails the whole invocation closed before any
    process starts, never trusting that the claimed attempt it was given
    already enforced this upstream; the previous positive "effort-only still
    appends only the config flag" test was replaced with this rejection
    coverage. With both null, the exact existing
    argument list, `--sandbox read-only`, `--ephemeral`, `--ignore-user-config`,
    schema, stdin delivery, and output/time bounds are byte-for-byte
    unchanged — proven by the existing exact-argument-list regression tests,
    all still green unmodified. With a non-null model and/or effort, exactly
    `--model <id>` and/or `--config model_reasoning_effort=<effort>` are
    appended before the trailing `-`, verified against the official
    [Codex developer commands](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
    (fetched directly in this slice: `--model/-m` and repeatable `-c/--config
    key=value`) and [configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
    (`model_reasoning_effort`, levels depend on model/client) — the installed-
    build `codex exec --help` citation for `--model`/`--config`/`--ignore-user-config`/
    `--ephemeral` is carried over from the planner's own selection record
    (this executor's environment has no local `codex` CLI to reproduce that
    check independently). Claude paths and arguments are entirely untouched.
  - New `useSetCodexAssignmentPreference` hook and `CodexAssignmentPreferenceControl`
    cockpit component (using the existing `useCodexModelCatalog` hook) added
    to `RunCockpitView`: a model select (starting on an explicit "No
    preference," never auto-selecting the catalog's suggested default), an
    effort select populated only from the selected model's own supported
    efforts, Save and Clear controls, and explicit loading/Unknown/save-
    failure states that never discard the last successfully saved preference.
  - Excluded, per the selected slice's boundary: Claude model/effort
    selection, permission-mode changes, arbitrary CLI config, profile/user-
    config loading, CLI-default inference, automatic choice of the catalog's
    suggested default, provider capability/preflight claims, account-
    allowance thresholds, invocation-eligibility guarantees, retry/fallback,
    provider-session resume, and context/compaction. No observed model or
    effort is ever inferred from the requested pair, the catalog, or a
    process exit.
  - Checks actually run (full suites, current totals): Domain.Tests 547/547;
    Infrastructure.IntegrationTests 520/521 (1 pre-existing, unrelated skip) —
    including `SetCodexAssignmentPreferenceTransactionBoundaryTests` (no
    ambient transaction wraps the catalog observation; the preference and its
    event persist together; a lifecycle transition during the catalog
    observation or between the authoritative read and the save is rejected
    via the `Lifecycle` concurrency token with nothing persisted),
    `CreateCodeReviewAttemptTransactionBoundaryTests` (no ambient transaction
    wraps its external Git evidence capture; its own DI container now also
    registers `IAttemptDurabilityProbe`, alongside every other hand-built
    `ServiceCollection` in `Api.IntegrationTests` that seeds through one of
    the three `Create*Attempt` handlers), and two
    `ClaimTimeAssignmentPreferenceGuardTests` (the guard-and-commit
    transaction excludes a background competing write, and its own no-lock
    negative control); Application.Tests 1069/1069 — including
    `SetCodexAssignmentPreferenceCommandHandlerTests`
    (not-found, clear-without-catalog-read, set-valid, model-not-visible,
    effort-not-supported, catalog-Unknown, no-vetted-target, terminal-
    lifecycle rejection), and, across the three `Create*Attempt` handlers'
    test files: preference-copied-into-claimed-attempt and null-preference
    cases; a claim-time-assignment-freshness case per handler (a preference
    change injected during external Git evidence capture is what the claimed
    attempt embeds; a further change after the claim never reaches the
    already-claimed attempt's own snapshot); a post-read-interleaving case per
    handler (`ConfirmUnchangedAsync` returns `false` with no side effect
    against an already-committed differing pair); eight fault-injection cases
    per handler through the real, independent `AttemptDurabilityProbe`
    (transaction-acquisition failure; commit failure without persisting;
    commit failure that did persist; the same two commit outcomes again with
    the handler's own best-effort rollback of that same transaction also
    throwing; and the three `OperationCanceledException` equivalents —
    cancellation during transaction acquisition, cancellation during commit
    without persisting, cancellation during commit that did persist) —
    together proving the correct artifact-cleanup-or-retain and
    error-classification outcome even when the preceding rollback itself
    fails, and, for the cancellation cases, that the exception genuinely
    propagates from `HandleAsync` rather than being converted into a
    `Result`; Api.IntegrationTests 339/339 — including
    `SetCodexAssignmentPreferenceEndpointTests`
    (401, 404, structural validation, catalog-unavailable-closed, clear,
    set-and-reflected-in-cockpit, model-not-visible, effort-not-supported,
    no-leak); Architecture.Tests 9/9. Frontend focused hook and component
    tests full suite 666/666; `tsc -b` clean; `oxlint` exited 0 with the same
    20 pre-existing warnings (0 new); production build (`vite build`) passed
    — none of this slice's review corrections touched a frontend file or an
    API-visible
    contract, so the frontend suite, `tsc`, `oxlint`, and the production build
    were not rerun again in this round; their results above remain
    applicable, confirmed by the NSwag repeat-build hash below staying byte-
    identical throughout. Two migrations exist: `AddCodexAssignmentPreference`
    (additive, the two nullable bounded preference columns) and
    `MarkRunLifecycleAsConcurrencyToken` (empty-body — required only to keep
    the EF model snapshot in sync with `Run.Lifecycle`'s concurrency-token
    metadata, since a concurrency token changes no column); nothing in this
    slice's guard-transaction or `CreateCodeReviewAttemptCommand` fixes needed
    a further migration (both schema-free). NSwag client regenerated via
    `dotnet build src/backend/DevalCopilot.Api` with an identical SHA-256 hash
    on every repeat build (no drift). `git diff --check` is clean apart from
    the pre-existing
    generated-client CRLF-normalization warning and the same warning now also
    on the additively-updated EF model snapshot file (0 actual CRLF bytes in
    either). Local documentation links resolve. Automated tests never call a
    real provider.
  - Remaining risk: the `--model`/`--config model_reasoning_effort` invocation
    shape is verified from the official documentation (fetched directly) and,
    for the installed-build `codex exec --help` confirmation specifically,
    from the planner's own carried-over observation — this executor's
    environment has no local `codex` CLI to reproduce that specific check.
    An incompatible installed CLI version, or one that rejects these flags in
    combination with the existing fixed arguments, fails the whole invocation
    closed (the shared invoker's existing non-zero-exit handling), never
    silently ignores the request. Provider account-usage thresholds,
    warning/stop enforcement, Gemini/manual-fallback provider selection, and
    context/compaction remain open under [Increment 4](mvp-delivery-plan.md).
- Published delivery: `cf5b8d64b7d41fe1b258ad919f75051e8425a408`
  (parent `c2e5023e3c4a653856b5691d52c1d27e07844826`) was committed with
  the reviewed 31-file slice, pushed as a normal fast-forward to `origin/main`,
  and verified against the live remote with `git fetch origin main`: local
  `HEAD`, local `origin/main`, and fetched `origin/main` matched the delivered
  SHA, with a clean working tree. This closure records the delivered SHA only;
  no code or product contract changed after publication. (Historical: at this
  closure's own time, the planner had not yet selected another slice; it has
  since selected the explicit Codex model/effort request slice recorded at
  the top of this checkpoint.)
- Current delivery, based on verified parent `c2e5023e3c4a653856b5691d52c1d27e07844826`:
  read-only Codex model and reasoning-effort catalog observation. See
  [planner-handoff.md](planner-handoff.md) for the selection record, and the
  ["Codex model and reasoning-effort catalog contract"](../architecture/agent-collaboration-protocol.md#codex-model-and-reasoning-effort-catalog-contract)
  and
  ["Codex model and reasoning-effort catalog observation (read-only)"](../product/run-cockpit-specification.md#codex-model-and-reasoning-effort-catalog-observation-read-only)
  sections for the exact wire evidence and product contract.
  - New Application query `GetCodexModelCatalogQuery` (returning a plain
    projection, never `Result<T>` — no expected failure outcome) reads the
    same durable, already-vetted Codex launch target
    `GetCodexLaunchTargetQueryHandler` and `GetCodexAccountAllowanceQueryHandler`
    each read (the same small, deliberate duplication of that five-line
    `HostCapabilitySnapshot` lookup) and, only when one currently resolves
    successfully, asks the new `ICodexModelCatalogAdapter` port for one fresh
    catalog observation. No vetted target and no adapter observation both
    collapse to the same explicit `CodexModelCatalogStatus.Unknown` projection.
  - New Infrastructure `CodexModelCatalogAdapter` speaks the documented Codex
    App Server `model/list` method — confirmed against the official
    `https://learn.chatgpt.com/docs/app-server#list-models-modellist` page
    (fetched directly in this slice; the executor's environment has no local
    `codex` CLI, so the installed-build generated-schema citation
    (`ClientRequest.json`, `v2/ModelListParams.json`,
    `v2/ModelListResponse.json` for `codex-cli 0.158.0-alpha.2.1`) is carried
    over from the planner's own selection record in planner-handoff.md, exactly
    as the prior account-allowance slice's precedent for an absent local CLI).
    Always requests `includeHidden: false`; any entry the provider still marks
    `hidden: true` is discarded defensively. Bounded cursor paging follows
    `nextCursor` for at most 8 pages of at most 50 entries each, capped at 200
    total processed entries across every page; a provider that still claims
    more pages past that bound fails the whole observation closed rather than
    presenting a silently truncated catalog as complete. A model's own `id` is
    treated like the allowance adapter's limit id: bounded, restricted to a
    safe identifier character set, and required to be unique across every
    page — a missing, oversized, malformed, or duplicate id fails the whole
    catalog closed. `displayName` falls back to the model's own (already-
    validated) id whenever it is missing, oversized, blank, or carries a
    control or bidirectional-formatting character (for example a Unicode
    right-to-left override) — rejecting those characters rather than
    rendering a name that could visually misrepresent itself. This is
    descriptive data, not an identifier, so it never fails the whole catalog.
    `supportedReasoningEfforts` is read as a whole: a malformed or duplicate
    individual effort makes the entire field for that entry `null` (Unknown)
    rather than presenting a partial list with the bad element silently
    dropped, and a genuinely excessive list still fails the whole catalog
    closed; an absent or explicitly empty list projects as an empty (non-null)
    list. `defaultReasoningEffort` is projected only when it is itself a
    bounded, valid identifier *and* a member of that same entry's own known
    (non-Unknown) `supportedReasoningEfforts` — an internally inconsistent or
    unverifiable default is `null` (Unknown) rather than an unchecked claim.
  - The launch/handshake/correlated-read/process-tree-cleanup mechanics the
    account-allowance adapter established were narrowly extracted into a new
    shared internal `CodexAppServerSession`, reused by both
    `CodexAccountAllowanceAdapter` (refactored to call it, with no wire-level
    behavior change) and the new `CodexModelCatalogAdapter`. The exchange
    handle used after the handshake, `CodexAppServerChannel`, is its own file
    and is a closed, method-specific surface — not a general-purpose JSON-RPC
    escape hatch and not an arbitrary raw-JSON write path: it exposes exactly
    the two reviewed read-only methods this application ever sends,
    `SendAccountRateLimitsReadAsync` and `SendModelListAsync`, each building
    its own fixed request JSON internally from validated primitive parameters
    (a request id, and, for `model/list`, a bounded page size and an
    already-validated cursor) and returning the correlated reply directly; no
    caller can write an arbitrary JSON payload through it. The existing
    `CodexProcessInvoker` one-shot contract remains unsuitable for the same
    reason documented for the allowance adapter: the App Server is a
    long-running duplex peer neither one-shot contract can express.
  - New protected `GET api/environment/codex-model-catalog` endpoint (mirrors
    `GetCodexAccountAllowanceEndpoint`'s shape) and its NSwag client
    regeneration (additive only). New `useCodexModelCatalog` hook (fetches
    once on mount plus an explicit `refresh()` — never a recurring interval)
    and a new `describeCodexModelCatalog` view-model/formatter feed a second
    section added to the existing `UsageEvidenceRail` cockpit panel, beside the
    Codex account-allowance line, with its own retrieval time and manual
    Refresh control (the two Refresh buttons are now distinguished by
    `aria-label` — "Refresh Codex account usage" / "Refresh Codex model
    catalog" — since both render the same visible "Refresh" text). An explicit
    "Unknown" (never an empty-looking success) is shown when unavailable, and a
    failed refresh clears prior observed data and its timestamp, exactly like
    the allowance line.
  - Excluded, per the selected slice's boundary: model/effort selection, run
    intent, persistence/migrations, attempt assignment or invocation
    arguments, claim/dispatch gating, provider-preflight capability claims,
    account-allowance threshold/stop policy, a Claude model catalog,
    context/compaction, provider-session resume, thread/turn calls, direct
    provider HTTP, auth-file reads, and any generic RPC escape hatch. No CLI
    default is inferred and no account authentication or invocation
    eligibility is guaranteed from this catalog response.
  - Checks actually run: Infrastructure.IntegrationTests focused
    `CodexModelCatalogAdapterTests` 25/25 (18 original + 7 added across review
    corrections: exact outbound `model/list` request JSON including `limit`, a
    malformed/duplicate individual effort projecting the whole
    `supportedReasoningEfforts` field as Unknown, a default effort absent from
    the known supported set projecting as Unknown, and a displayName carrying
    a control or bidirectional-formatting character — including U+061C ARABIC
    LETTER MARK alongside LRM/RLM/embedding/override/isolate characters —
    falling back to the model's id) and `CodexAccountAllowanceAdapterTests`
    30/30 (confirms the channel extraction and its closed method-specific
    surface preserve the allowance adapter's exact wire behavior), full 506
    passed / 1 pre-existing skip. The bidi/control characters under test are
    now expressed as literal `\uXXXX` escapes in both the adapter's own
    character table and the test file, not the actual invisible characters, so
    the source remains reviewable. Application.Tests focused
    `GetCodexModelCatalogQueryHandlerTests`
    6/6, full 1028/1028; Api.IntegrationTests focused
    `GetCodexModelCatalogEndpointTests` 4/4, full 330/330; Domain.Tests
    529/529; Architecture.Tests 9/9. Frontend focused hook, describe, and rail
    tests (4 + 11 + 3 new, including the `None`-vs-Unknown effort-list
    distinction added in review correction) 657/657 full suite (frontend types
    were unaffected by the backend nullability correction —
    `supportedReasoningEfforts` was already an optional generated-client
    field); `tsc -b` clean; `oxlint`
    exited 0 with the same 20 pre-existing warnings (0 new); production build
    (`vite build`) passed. NSwag client regenerated by
    `dotnet build src/backend/DevalCopilot.Api`; the resulting `api-client.ts`
    diff is additive-only new client/DTO types, with a stable hash on repeat
    build. `git diff --check` is clean apart from the existing generated-client
    CRLF-normalization warning. The deterministic compiled App Server fixture
    (reused unmodified from the allowance slice) exercises multi-page combined
    models with effort/default mapping, hidden filtering, an empty catalog,
    oversized/malformed/duplicate/conflicting responses (including the
    corrected whole-field-Unknown effort-list behavior), unsolicited
    notifications, a bounded-page-count overflow, timeout/cancellation, and
    parent/child process termination; automated tests never call a real
    provider.
  - Remaining risk: the `model/list` request/response shape is verified from
    the official documentation page (fetched directly) and, for cursor-based
    paging specifically, from the planner's own installed-build generated
    schema citation carried over into this slice — the executor's own
    environment has no local `codex` CLI to independently reproduce that
    schema generation. An incompatible installation, or an account/client for
    which the documented shape differs, fails closed to `Unknown`. Provider
    account-usage thresholds, warning/stop enforcement, model/effort
    selection, and Claude's own catalog observation remain open under
    [Increment 4](mvp-delivery-plan.md).
- Published delivery: `3c120b41b3366de70f221479b2545183ebb79fda`
  (parent `ba6fdd096b217dfe1e46d6eb5541853325fce6c6`) was committed with
  the reviewed 32-file slice, pushed as a normal fast-forward to `origin/main`,
  and verified against the live remote with `git fetch origin main`: local
  `HEAD`, local `origin/main`, and fetched `main` matched the delivered SHA,
  with a clean working tree. Focused Infrastructure 30/30, Application 7/7,
  API 4/4, and frontend 24/24 tests were reconfirmed against that published
  commit. This closure records the delivered SHA only; no code or product
  contract changed after publication. (Historical: at this checkpoint's own
  closure, the planner had not yet selected another slice; it has since
  selected the Codex model-catalog slice recorded at the top of this
  checkpoint.)
- Current delivery, based on verified parent `ba6fdd096b217dfe1e46d6eb5541853325fce6c6`:
  read-only Codex ChatGPT account-allowance observation. See
  [planner-handoff.md](planner-handoff.md) for the selection record, and the
  ["Provider account-allowance contracts"](../architecture/agent-collaboration-protocol.md#provider-account-allowance-contracts)
  and
  ["Codex account-allowance observation (read-only)"](../product/run-cockpit-specification.md#codex-account-allowance-observation-read-only)
  sections for the exact wire evidence and product contract.
  - New Application query `GetCodexAccountAllowanceQuery` (returning a plain
    projection, never `Result<T>` — this query has no expected failure
    outcome) reads the same durable, already-vetted Codex launch target
    `GetCodexLaunchTargetQueryHandler` reads (a small, deliberate duplication
    of that five-line `HostCapabilitySnapshot` lookup — this operation owns
    its own read rather than depending on another operation's result type),
    and, only when one currently resolves successfully, asks the new
    `ICodexAccountAllowanceAdapter` port for one fresh snapshot. No vetted
    target and no adapter observation both collapse to the same explicit
    `CodexAccountAllowanceStatus.Unknown` projection.
  - New Infrastructure `CodexAccountAllowanceAdapter` speaks the documented
    Codex App Server JSON-RPC protocol directly: explicit `--stdio` JSONL, the required
    `initialize`/`initialized` handshake, then the sole `account/rateLimits/read`
    read method — confirmed from the installed `codex-cli 0.158.0-alpha.2.1`
    build's own generated App Server schema (`ClientRequest.json`,
    `v2/GetAccountRateLimitsResponse.json`), which is protocol evidence for
    that installed build, not a live authenticated result or a claim about
    every installed version. This could not reuse the shared, one-shot
    `CodexProcessInvoker` (it writes stdin once, closes it, then waits for
    natural exit); the App Server is a long-running duplex peer this adapter
    must itself terminate, so a second, narrow Infrastructure process
    boundary exists for it alone — preserving the same launch-target
    revalidation, no-shell/no-PATH-search argument passing, restricted
    environment allowlist, finite timeout, bounded output capture (a new
    `BoundedJsonLineScanner`, 64 KiB total / 16 KiB per line), cancellation
    propagation, and process-tree cleanup as every other Codex process path.
    `rateLimitsByLimitId` is read as a map of limit ids to independent
    snapshots, each with its own `primary`/`secondary` windows; the legacy
    `rateLimits` single snapshot is used only when the map is absent or null.
    The adapter caps the map at 16 buckets and validates every identifier;
    malformed, duplicate, or excessive maps fail closed. An integer
    `usedPercent` in [0, 100] establishes a window; nullable duration and
    Unix-seconds reset time remain independently Unknown when absent or
    invalid. The scanner enforces 64 KiB total and 16 KiB per complete or
    split line without trusting the suffix of an oversized line. Conflicting
    replies already received for one id, malformed response shapes, timeout,
    process failure, and failed process cleanup yield Unknown; caller
    cancellation propagates after process-tree cleanup. Registered as a
    singleton in `Program.cs` beside the other host-scoped capabilities.
  - New protected `GET api/environment/codex-account-allowance` endpoint
    (mirrors `GetProviderRuntimePreflightEndpoint`'s shape) and its NSwag
    client regeneration (additive only). New
    `useCodexAccountAllowance` hook (fetches once on mount plus an explicit
    `refresh()` — never a recurring interval) feeds the existing
    `UsageEvidenceRail` cockpit usage rail, which now shows a separate line
    for every bounded Codex bucket, its known percentage/duration/reset
    fields, and retrieval time beside the
    still-unchanged Claude "not yet collected" placeholder, with an explicit
    "Unknown" (never a zero) when unavailable and a manual Refresh control.
    A failed refresh clears prior observed data and its timestamp.
  - Excluded, per the selected slice's boundary: Claude allowance, threshold
    configuration, warning/stop enforcement, claim/dispatch gating, persisted
    allowance schema, scheduled polling, any provider-preflight `AccountUsage`
    change (`ProviderRuntimePreflightProjector` still hardcodes `Unknown` for
    every capability, untouched), model/effort selection, session resume,
    context/compaction, and direct authenticated HTTP.
  - Checks actually run after correction: Infrastructure.IntegrationTests
    focused `CodexAccountAllowanceAdapterTests` 30/30, full 481 passed / 1
    pre-existing skip; Application.Tests focused 7/7, full 1022/1022;
    Api.IntegrationTests focused 4/4, full 326/326; Domain.Tests 529/529;
    Architecture.Tests 9/9. Frontend focused hook, description, and rail
    tests 24/24, full 639/639; `tsc -b` clean; `oxlint` exited 0 with the
    same 20 pre-existing warnings (0 new); production build passed. NSwag
    client regenerated by `dotnet build src/backend/DevalCopilot.Api` with a
    stable hash on repeat build; local documentation links resolve.
    `git diff --check` is clean apart from the existing generated-client
    CRLF-normalization warning. The deterministic compiled App Server fixture
    exercises documented multi-bucket and legacy shapes, malformed data,
    output limits, timeout/cancellation, and parent/child process termination;
    automated tests never call a real provider.
  - Remaining risk: the wire shape is documented and verified from one
    installed CLI build's generated schema, but no live authenticated
    response was tested. An incompatible installation fails closed to
    `Unknown`. Provider account-usage thresholds,
    warning/stop enforcement, and Claude's own allowance observation remain
    open under [Increment 4](mvp-delivery-plan.md).
- Previously accepted delivery: Codex provider-session correlation repair for the
  three current read-only roles (Planner, Resolver, CodeReviewer), in
  `c04cebf59d85483af8bcfa3f280bd440598a7d76` (parent
  `c481aead3ca652cf06fffaa509bb68f411f483ff`), published to `origin/main`
  and verified against the live remote. See
  [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
  - The shared `CodexProcessInvoker.TryExtractProviderSessionId` no longer
    reads an arbitrary `session_id` property. It now parses only a complete
    JSON object, on its own line within the existing 4,096-character bounded
    scan prefix (a character count, not a byte-accurate limit, despite the
    `MaxSessionIdScanBytes` constant name that predates this slice and is
    unchanged here), whose `type` is exactly `"thread.started"` and whose
    `thread_id` is a nonblank string within the existing 256-character storage
    bound — the event and field the current official
    [Codex non-interactive contract](https://learn.chatgpt.com/docs/non-interactive-mode)
    documents. Every other event shape, including the previous invented
    `session_meta`/`session_id` shape and a line that is malformed or
    truncated by the scan prefix itself, is ignored without interrupting the
    scan of the remaining lines. If the bounded window contains more than one
    distinct valid `thread_id`, extraction now fails closed to `null`
    (`Unknown`) rather than selecting one, since a single invocation has
    exactly one provider thread and disagreement means the value cannot be
    trusted; the same valid id repeated on multiple lines is still surfaced.
    This is unchanged as a provider-reported correlation reference recorded on
    `Attempt.AgentProviderSessionId` through the existing adapter result flow
    and result commands — never resume capability, an observation of
    effective access, or an inferred capability from CLI defaults. No CLI
    argument, invocation, capture bound, redaction, success-only recording,
    lifecycle, or authorization changed; no schema, migration,
    API/generated-client/frontend field, raw-ID disclosure, resume/open/fork
    action, or other provider adapter changed.
  - `CodexPlanningAdapterTests`, `CodexChallengeResolutionAdapterTests`, and
    `CodexImplementationReviewAdapterTests` each replace their previous
    invented-event test with the documented `thread.started`/`thread_id`
    positive case, and each adds a case proving an unrelated legacy
    `session_id` event is ignored. `CodexPlanningAdapterTests` — chosen as the
    single representative adapter for the shared extractor's full negative
    matrix, since `CodexProcessInvoker` is exercised identically regardless of
    which adapter invokes it — additionally covers a `thread.started` event
    missing `thread_id`, a non-string `thread_id`, a `thread_id` exceeding the
    256-character storage bound, a `thread.started` event pushed entirely past
    the 4,096-character scan prefix by a preceding filler line, a
    `thread.started` event whose own line is cut off mid-object by that same
    scan prefix (an incomplete JSON fragment that must fail to parse safely
    even though it already contains a valid-looking `thread_id` before the
    cut), a malformed (non-JSON) line that does not prevent a later valid
    event on its own line from being surfaced, two distinct valid
    `thread_id`s failing closed to `Unknown`, and the same valid `thread_id`
    repeated on two lines still being surfaced. The pre-existing non-success
    exit case (`Assert.Null(result.ProviderSessionId)` on a non-zero exit) is
    unchanged in all three files.
  - `RecordChallengeResolutionResultCommandHandlerTests` and
    `RecordImplementationReviewResultCommandHandlerTests` each gained the same
    two focused durable-persistence cases already covering
    `RecordAgentAttemptResultCommandHandlerTests` (Planner), corrected to
    exercise each role's actual successful result path — `Resolved` with a
    valid resolution for Resolver, `ReviewApproved` with a valid review for
    CodeReviewer — since `CodexProcessInvoker` only ever emits a thread id on
    a clean process exit, never on `ProviderInvocationFailed`: a `null`/empty/
    whitespace `ProviderSessionId` is not durably recorded, and a present one
    is durably recorded on `Attempt.AgentProviderSessionId`, reloaded from a
    fresh `DbContext` after `SaveChangesAsync`. Both use the existing
    `TestProcessEvidence.ReportedCleanExit` fixture already used by each
    file's own pre-existing successful-outcome test, so no new seeding helper
    was needed.
  - Checks actually run: Infrastructure.IntegrationTests focused
    `CodexPlanningAdapterTests` + `CodexChallengeResolutionAdapterTests` +
    `CodexImplementationReviewAdapterTests` 46/46, full
    Infrastructure.IntegrationTests 451/452 (the same one pre-existing
    unrelated skip); Application.Tests focused
    `RecordAgentAttemptResultCommandHandlerTests` +
    `RecordChallengeResolutionResultCommandHandlerTests` +
    `RecordImplementationReviewResultCommandHandlerTests` 71/71, full
    Application.Tests 1015/1015; full Api.IntegrationTests 322/322 (unchanged,
    confirming no API-visible change); Domain.Tests 529/529 and
    Architecture.Tests 9/9 (unchanged; no Domain/API-shape change in this
    slice); `git diff --check` reported no errors. The full Api.IntegrationTests,
    Domain.Tests, and Architecture.Tests results are carried over unchanged
    from the immediately preceding round of this same slice — this correction
    touched only the Infrastructure adapter tests and the two Application
    persistence tests, neither of which those three suites cover. Frontend,
    typecheck, lint, production build, and NSwag regeneration were not run —
    no API response DTO, generated client, or frontend file changed in this
    slice. Automated tests never call a real provider.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open. This
    correlation reference still grants no resume eligibility or capability.
  - Post-publication, `main`, `HEAD`, local `origin/main`, and live
    `origin/main` were confirmed at the delivered commit with a clean working
    tree, and focused Infrastructure.IntegrationTests
    `CodexPlanningAdapterTests` + `CodexChallengeResolutionAdapterTests` +
    `CodexImplementationReviewAdapterTests` (46/46) and Application.Tests
    `RecordAgentAttemptResultCommandHandlerTests` +
    `RecordChallengeResolutionResultCommandHandlerTests` +
    `RecordImplementationReviewResultCommandHandlerTests` (71/71) were
    reconfirmed against it.
  - Next action: no next Increment 4 slice is selected yet; Codex selects and
    dispatches the next bounded slice.
- Previously accepted delivery: immutable assignment provenance for the two
  remaining current Claude Code paths (CriticalReviewer and Implementer
  ReviewCorrection) at claim time, plus five configured adapter facts on
  each path's existing attempt status and cockpit action, in
  `caf45d33396617ca640cb766fe5bc984c93b488c` (parent
  `6312438046e9da0a43e2231ecc76f3e8eee62ac6`), published to `origin/main`
  and verified against the live remote. See
  [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
  - `Attempt.ClaimAgentCriticalReview` now persists a concrete
    `AgentPermissionProfile.ReadOnly` and the fixed adapter contract version
    `claude-critical-review-v1`; `Attempt.ClaimAgentReviewCorrection` now
    persists `AgentPermissionProfile.WorkspaceEditOnly` and the fixed adapter
    contract version `claude-review-correction-v1` — both using the existing
    nullable `AgentPermissionProfile` / `AgentAdapterContractVersion`
    columns already shared with the Implementer and Codex roles; no
    migration was needed. Requested/observed model and effort remain `null`
    for both paths. The `AgentPermissionProfile.ReadOnly` XML comment was
    made provider-neutral (assigned read-only workspace intent, never a
    proven or observed effective isolation boundary, and never that shell,
    network, or MCP actions are absent) since it is now shared by a Claude
    Code path, not only Codex roles. Legacy rows with `null`
    permission-profile/adapter-contract-version columns continue to project
    as `Unknown`/`null` via the existing `Attempt.GetAssignmentSnapshot()`
    degrade-to-Unknown behavior — never retroactively treated as coherent.
  - `GetClaudeCriticalReviewAttemptStatusQueryHandler` now calls
    `Attempt.GetAssignmentSnapshot()`, fails the whole status closed with the
    existing `agent_attempts.invalid_assignment` error for malformed
    assignment metadata (new for this role, mirroring the Implementer
    handler's own try/catch and null-snapshot guard), and discloses five
    facts — `configuredPermissionMode: "plan"`,
    `configuredSessionPersistence: "Disabled"`,
    `configuredPermissionPrompts: "None"`,
    `configuredResumeEligibility: "Ineligible"`, and
    `configuredBuiltInTools: "None"` (the adapter's explicit empty `--tools`
    argument) — only for a coherent current assignment.
    `GetReviewCorrectionAttemptStatusQueryHandler` gained the identical five
    facts for its own path (`configuredPermissionMode: "acceptEdits"`,
    `configuredBuiltInTools: "Read,Edit,Write,Glob,Grep"`, the same three
    other values), added as a pure addition strictly after the existing
    current-review lineage and budget/escalation computation: its own
    `ImplementerExecutionReportEligibility.LoadSnapshotAsync` call is now
    wrapped to fail closed on a corrupt persisted assignment enum anywhere
    in the run (previously an unhandled exception, now the same fail-closed
    `agent_attempts.invalid_assignment` result used elsewhere), and the
    resolved correction attempt's own `GetAssignmentSnapshot()` gates the
    five facts — neither change alters which attempt is resolved as
    "current" or how the budget/escalation facts are computed; both
    existing handler tests covering that lineage remain green unchanged.
    Both status handlers preserve strict `null`/`Unknown` fallback for no
    attempt or a valid historical/mismatched assignment, exactly mirroring
    the Implementer and Codex-role precedent. All facts describe fixed CLI
    arguments only — never observed effective access, an MCP or complete
    security boundary, model or effort, or invocation eligibility.
  - The two response contracts remain role-specific, each gaining exactly
    the same five new fields, mapped through their existing endpoints; no
    raw provider/role/permission-profile/adapter-contract-version field was
    added to either. `ClaudeCriticalReviewAction.tsx` and
    `ReviewCorrectionAction.tsx` each render a new "Configured permission
    mode: … · Configured provider-session persistence: … · Configured
    permission confirmations: … · Configured resume eligibility: … ·
    Configured built-in tools: …" line, whitelist-comparing the single
    recognized literal per fact and rendering `Unknown` for anything else —
    never a provider-supplied value verbatim. Documented in the new
    "Configured Claude CriticalReviewer and Implementer ReviewCorrection
    permission mode, session persistence, permission confirmations, resume
    eligibility, and built-in tools" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  - Two pre-existing shared cross-role disclosure-guard test files
    (`AgentTokenUsageEndpointTests` and
    `AgentProcessExecutionEvidenceEndpointTests`) scan every role route's raw
    response body for the substring "session"; both already carried a
    narrow, route-gated exception for the Implementer's own
    `configuredSessionPersistence` fact. That exception is now also opted in
    for the CriticalReviewer route (`route is "agent-attempts/implementation"
    or "agent-attempts/claude-critical-review"`), since this attempt status
    now legitimately carries the same fact; every other disclosure check in
    both files, and the exception's own scoping (root-level only, single
    occurrence, exactly one of the two coherent values), is unchanged. No
    equivalent carve-out was needed for the two new endpoint test files
    themselves (`GetClaudeCriticalReviewAttemptStatusEndpointTests`,
    `ReviewCorrectionEndpointTests`) or the frontend, since neither has a
    pre-existing blanket "prompt"/"session" scan.
  - `ClaudeCriticalReviewAdapterTests` and `ClaudeReviewCorrectionAdapterTests`
    were extended with comments and (for ReviewCorrection) new assertions
    tying their existing exact-argument indices to the newly disclosed
    facts; no adapter argument changed.
  - Correction round: `GetClaudeCriticalReviewAttemptStatusQueryHandler`'s
    coherence condition now also requires
    `attempt.AgentResponseContract == AgentResponseContract.CriticalReview`,
    proven by a new focused test that persists an otherwise-fully-coherent
    attempt with a different, valid response contract and asserts all five
    facts remain `null`.
    `GetReviewCorrectionAttemptStatusQueryHandler`'s own coherence condition
    now also states `attempt.AgentResponseContract ==
    AgentResponseContract.ReviewCorrection` explicitly — the attempt
    resolved above was already exclusively selected by that same contract in
    the lineage query, so this is a defensive, explicit restatement of an
    already-guaranteed fact, never a change to lineage selection; both
    existing lineage/budget tests remain green unchanged. The handler's own
    comment on its `LoadSnapshotAsync` try/catch was corrected: it now
    states plainly that this fail-closed path can fire from an unparseable
    assignment enum on *any* attempt in the run, not only the eventual
    review-correction candidate, and that it is a broader run-wide guard
    layered above, not a replacement for, the later per-attempt
    `GetAssignmentSnapshot()` check on the resolved correction attempt
    itself. The cockpit specification already described the coherence rule
    as covering "provider, role, response contract, permission profile, and
    adapter contract version," so no documentation change was needed there.
  - Before the substantive commit, a stale sentence in
    `GetReviewCorrectionAttemptStatusQueryHandler`'s later per-attempt
    `GetAssignmentSnapshot()` check comment (claiming an unrelated corrupt
    attempt on the run could never fail this status) was corrected —
    comment-only, no behavior change — to state that this case is instead
    caught earlier, at the `LoadSnapshotAsync` call, exactly as the
    surrounding correction round already described.
  - Checks actually run: Application.Tests focused
    `GetClaudeCriticalReviewAttemptStatusQueryHandlerTests` 9/9 (1 new
    mismatched-response-contract case) and
    `GetReviewCorrectionAttemptStatusQueryHandlerTests` 6/6 unchanged, full
    Application.Tests 1007/1007; Api.IntegrationTests focused
    `GetClaudeCriticalReviewAttemptStatusEndpointTests` +
    `ReviewCorrectionEndpointTests` 19/19 unchanged; `git diff --check`
    reported no new errors. Domain.Tests 529/529, full Api.IntegrationTests
    322/322, Architecture.Tests 9/9, Infrastructure.IntegrationTests 440/441
    (one pre-existing unrelated skip), frontend 615/615, `tsc -b`, `oxlint`,
    and `vite build` were not rerun in this correction — they are untouched
    by comment-only and coherence-condition-only backend changes with no
    response-shape or frontend change; their results from the immediately
    preceding round remain applicable. The NSwag client was not regenerated
    in this round — no response DTO changed; its prior additive-only diff
    (40 insertion lines) is unchanged. Automated tests never call a real
    provider. Post-publication, `main`, `HEAD`, local `origin/main`, and
    live `origin/main` were confirmed at the delivered commit with a clean
    working tree, and focused
    `GetClaudeCriticalReviewAttemptStatusQueryHandlerTests` +
    `GetReviewCorrectionAttemptStatusQueryHandlerTests` (15/15 combined)
    were reconfirmed against it.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action: no next Increment 4 slice is selected yet; Codex selects and
    dispatches the next bounded slice.
- Previously accepted delivery: immutable assignment provenance for the three
  current Codex read-only roles (Planner, Resolver, CodeReviewer) at claim
  time, plus two configured adapter facts on each role's existing attempt
  status and cockpit action, in `1f69319614252ad4fb9e6bf1a8e93d61b33ac05d`
  (parent `01ccd3b4639d4ded8311226bea13bca922a0717e`), published to
  `origin/main` and verified against the live remote. See
  [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
  - `Attempt.ClaimAgent` (Planner), `Attempt.ClaimAgentChallengeResolution`
    (Resolver), and `Attempt.ClaimAgentCodeReview` (CodeReviewer) each now
    persist a concrete `AgentPermissionProfile.ReadOnly` and a distinct,
    fixed adapter contract version — `codex-planning-v1`,
    `codex-challenge-resolution-v1`, and `codex-implementation-review-v1`
    respectively — using the existing nullable `AgentPermissionProfile` /
    `AgentAdapterContractVersion` columns already shared with the Claude
    Implementer role; no migration was needed. Requested/observed model and
    effort remain `null` for all three roles — neither adapter arguments nor
    provider output establish those facts, so none are invented. A new
    `AgentPermissionProfile.ReadOnly = 2` member was added (appended, never
    renumbered); existing persisted rows with `null` permission
    profile/adapter-contract-version columns (claimed before this slice)
    continue to project as `AgentPermissionProfile.Unknown`/`null` via the
    existing `Attempt.GetAssignmentSnapshot()` degrade-to-Unknown behavior —
    they are never retroactively treated as coherent.
  - Each role's existing attempt-status query handler
    (`GetAgentAttemptStatusQueryHandler`, `GetChallengeResolutionAttemptStatusQueryHandler`,
    `GetCodeReviewAttemptStatusQueryHandler`) now calls
    `Attempt.GetAssignmentSnapshot()`, fails the whole status closed with the
    existing `agent_attempts.invalid_assignment` error for malformed
    assignment metadata (mirroring the Claude Implementer handler's own
    try/catch and null-snapshot guard, added here for the first time), and
    discloses two new facts — `configuredCommandSandbox: "read-only"` and
    `configuredRolloutPersistence: "Disabled"` — only when that attempt's own
    provider, role, permission profile, and role-specific adapter contract
    version all agree with the current path. No attempt, or a valid
    historical/mismatched assignment, yields `null`/`Unknown` for both facts.
    These mirror the shared `CodexProcessInvoker`'s existing, unchanged
    `--sandbox read-only` and `--ephemeral` arguments, which the
    [Codex CLI reference](https://developers.openai.com/codex/cli/reference)
    documents as the sandbox policy for model-generated commands and as
    running without persisting session rollout files, respectively — stated
    only as configured CLI arguments, never observed effective isolation, a
    complete access-control boundary, provider-session resume eligibility, or
    invocation eligibility. No CLI argument, invocation, claim/dispatch
    authorization or budget, repository/worktree policy, schema, other
    role/provider, session identifier, resume/open/fork action, model/effort
    selection, context/compaction, account allowance, or fallback changed.
  - The three response contracts remain role-specific (no shared assignment
    DTO): `AgentAttemptStatusResponse`, `ChallengeResolutionAttemptStatusResponse`,
    and `CodeReviewAttemptStatusResponse` each gained exactly the same two
    new fields, mapped through their existing endpoints. Only these two
    facts are disclosed — not the raw provider/role/permission-profile/
    adapter-contract-version fields the Implementer status already exposes.
    `CodexPlanningAction.tsx`, `ChallengeResolutionAction.tsx`, and
    `CodeReviewAction.tsx` each render a new "Configured command sandbox: …
    · Configured rollout persistence: …" line, whitelist-comparing the
    single recognized literal per fact and rendering `Unknown` for anything
    else — never a provider-supplied value verbatim. Documented in the new
    "Configured Codex Planner, Resolver, and CodeReviewer command sandbox and
    rollout persistence" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  - Checks actually run: Domain.Tests 527/527 (3 new/updated
    `AgentAssignmentTests` cases proving each role's concrete claim-time
    assignment and a legacy-null-column case), Application.Tests 999/999
    (new/extended `GetAgentAttemptStatusQueryHandlerTests`,
    `GetChallengeResolutionAttemptStatusQueryHandlerTests`, and a new
    `GetCodeReviewAttemptStatusQueryHandlerTests` covering coherent-current,
    no-attempt, historical/mismatched, and malformed-assignment cases per
    role), Api.IntegrationTests 318/318 (extended endpoint tests per role,
    including 422 fail-closed and disclosure checks), Architecture.Tests 9/9,
    Infrastructure.IntegrationTests 440/441 (one pre-existing unrelated
    skip; the three existing exact-argument adapter tests —
    `CodexPlanningAdapterTests`, `CodexChallengeResolutionAdapterTests`,
    `CodexImplementationReviewAdapterTests` — remain green, confirming
    `--sandbox read-only`/`--ephemeral` are unchanged) — all green. Frontend
    611/611 (6 new cases across the three action components), `tsc -b`
    clean, `oxlint` exited 0 with the same 20 pre-existing warnings (0 new),
    `vite build` production build passed, `git diff --check` reported no new
    errors (only the pre-existing CRLF-normalization warning on the
    generated client file). The NSwag client was regenerated via
    `dotnet build src/backend/DevalCopilot.Api`; the resulting
    `api-client.ts` diff is additive only (24 insertion lines: two new
    optional fields × three response types × 4 lines each). Automated tests
    never call a real provider. Post-publication, `main`, `HEAD`, local
    `origin/main`, and live `origin/main` were confirmed at the delivered
    commit with a clean working tree, and focused
    `GetAgentAttemptStatusQueryHandlerTests`,
    `GetChallengeResolutionAttemptStatusQueryHandlerTests`, and
    `GetCodeReviewAttemptStatusQueryHandlerTests` (25/25 combined) were
    reconfirmed against it.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action after this slice: superseded by the Claude CriticalReviewer
    and ReviewCorrection assignment-provenance slice recorded at the top of
    this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer built-in tool list fact in
  `90c84b79917aa681c25f04182ca485defdfb3357` (parent
  `8f52522a35419045229f27962f8523cf41a5149d`), published to `origin/main` and
  verified against the live remote. See [planner-handoff.md](planner-handoff.md)
  for the selection and review record.
  - The implementation-attempt status and cockpit now show a new
    `ConfiguredBuiltInTools` / `configuredBuiltInTools` fact alongside the
    existing `ConfiguredPermissionMode`, `ConfiguredSessionPersistence`,
    `ConfiguredPermissionPrompts`, and `ConfiguredResumeEligibility` facts —
    `"Read,Edit,Write,Glob,Grep"` (the current `ClaudeImplementationAdapter`'s
    existing, unchanged `--tools` argument, which the
    [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
    documents as restricting built-in tools and explicitly states does not
    affect MCP tools) only when the attempt's own provider, role, permission
    profile, and adapter contract version all agree with the current
    supported implementation path; otherwise `null`/`Unknown`, with the
    existing fail-closed `agent_attempts.invalid_assignment` error preserved
    for invalid or absent assignment metadata (never a success value
    reporting `Unknown`). This is a configured adapter argument fact about
    this attempt, never an observation of effective access, a complete
    security boundary, an MCP tool restriction, or invocation eligibility.
    No CLI argument, invocation or tool policy, preflight, claim/dispatch,
    authorization, other role, model/effort selection, session behavior,
    context/compaction, account usage, or persistence schema changed.
    Labeled "Configured built-in tools" in the UI. Documented in the new
    "Configured Claude Implementer built-in tools" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
    The new fact's value/label do not contain the substring "prompt", so no
    equivalent to the existing root-level `configuredPermissionPrompts`
    disclosure-guard exception in `GetImplementationAttemptStatusEndpointTests`
    was needed; that pre-existing guard and its five negative tests are
    unchanged.
  - Checks actually run: Application.Tests focused
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8, full
    Application.Tests 986/986, Api.IntegrationTests focused
    `GetImplementationAttemptStatusEndpointTests` 9/9, full
    Api.IntegrationTests 312/312, Architecture.Tests 9/9, Infrastructure
    focused `ClaudeImplementationAdapterTests` 6/6 (confirms the adapter's
    `--tools` argument is unchanged) — all green. Frontend focused
    `ImplementationAction.test.tsx` 19/19, full frontend suite 605/605,
    `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing warnings
    (0 new), `vite build` production build passed, `git diff --check`
    reported no new errors (only the pre-existing CRLF-normalization warning
    on the generated client file, consistent with prior slices). The NSwag
    client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
    the resulting `api-client.ts` diff is additive only (one new optional
    field, 4 insertion lines). Domain.Tests and full
    Infrastructure.IntegrationTests were not rerun in this slice beyond the
    focused adapter test above — they are untouched by this change; their
    last confirmed results remain in Git history for the prior delivered
    slice. Post-publication, focused Application
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8 and focused Api
    `GetImplementationAttemptStatusEndpointTests` 9/9 were reconfirmed against
    the delivered commit with a clean working tree.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action after this slice: superseded by the Codex read-only role
    assignment-provenance slice recorded at the top of this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer resume-eligibility fact in
  `1ec9ac5f1c9eb3a25301cd37684c368db1ef24f6` (parent
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`), published to `origin/main` and
  verified against the live remote. Checkout clean at delivery and after
  publication. Codex reviewed the diff across two correction rounds and
  recorded GO before commit/push, then independently verified publication;
  see [planner-handoff.md](planner-handoff.md) for the review decision
  record.
  - The implementation-attempt status and cockpit now show a new
    `ConfiguredResumeEligibility` / `configuredResumeEligibility` fact
    alongside the existing `ConfiguredPermissionMode`,
    `ConfiguredSessionPersistence`, and `ConfiguredPermissionPrompts`
    facts — `"Ineligible"` (the current `ClaudeImplementationAdapter`'s
    existing, unchanged `--no-session-persistence` argument, which the
    [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
    documents as making a session started under this flag unable to be
    resumed) only when the attempt's own provider, role, permission profile,
    and adapter contract version all agree with the current supported
    implementation path; otherwise `null`/`Unknown`, with the existing
    fail-closed `agent_attempts.invalid_assignment` error preserved for
    invalid or absent assignment metadata (never a success value reporting
    `Unknown`). This is a configured adapter fact about this attempt, never a
    provider-observed result, a host-wide capability assessment, or
    invocation eligibility of any kind. No CLI argument, invocation,
    claim/dispatch policy, authorization, provider preflight, other role,
    model/effort selection, context/compaction, session identifier,
    resume/open/fork action, usage limit, or persistence schema changed.
    Documented in the new "Configured Claude Implementer resume eligibility"
    subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
    The new fact's name/label ("resume eligibility") does not contain the
    substring "prompt", so it needed no equivalent to the existing
    root-level `configuredPermissionPrompts` disclosure-guard exception in
    `GetImplementationAttemptStatusEndpointTests`; that pre-existing guard and
    its five negative tests are unchanged.
  - Checks actually run: Application.Tests focused
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8, full
    Application.Tests 986/986, Api.IntegrationTests focused
    `GetImplementationAttemptStatusEndpointTests` 9/9, full
    Api.IntegrationTests 312/312, Architecture.Tests 9/9 — all green. Frontend
    focused `ImplementationAction.test.tsx` 19/19, full frontend suite
    605/605, `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing
    warnings (0 new), `vite build` production build passed, `git diff --check`
    reported no new errors (only the pre-existing CRLF-normalization warning
    on the generated client file, consistent with prior slices). The NSwag
    client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
    the resulting `api-client.ts` diff is additive only (one new optional
    field, 4 insertion lines). Domain.Tests and
    Infrastructure.IntegrationTests were not rerun in this slice — they are
    untouched by this change; their last confirmed results remain in Git
    history for the prior delivered slice. Codex independently verified
    `main`, `HEAD`, local `origin/main`, and live `origin/main` at the
    delivered commit, a clean checkout, and the exact reviewed 12-file list,
    then independently passed post-publication Application 986/986, API
    312/312, Architecture 9/9, frontend 605/605, typecheck, production build,
    lint (20 pre-existing warnings, 0 new), and `git diff --check`.
  - Remaining risks: none newly introduced. Provider-session resume,
    runtime controls, and the other open items below remain unchanged and
    open.
  - Next action after this slice: superseded by the configured built-in
    tools slice recorded at the top of this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer permission-confirmations fact in
  `01f1777af877dd0374d7e3d7e461127a02f40d73` (parent
  `c2b0155f26f07bbe09ca67a9c5234228e274a703`), published to `origin/main` and
  verified against the live remote. Checkout clean at delivery and after
  publication. Codex reviewed the diff across two correction rounds and
  recorded GO before commit/push; see [planner-handoff.md](planner-handoff.md).
- The implementation-attempt status and cockpit now show a new
  `ConfiguredPermissionPrompts` / `configuredPermissionPrompts` fact
  alongside the existing `ConfiguredPermissionMode` and
  `ConfiguredSessionPersistence` facts — `"None"` (the current
  `ClaudeImplementationAdapter`'s existing, unchanged `--permission-prompts
  none` argument, which the
  [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents as denying interactive permission-confirmation prompts in print
  mode) only when the attempt's own provider, role, permission profile, and
  adapter contract version all agree with the current supported
  implementation path; otherwise `null`/`Unknown`, with the existing
  fail-closed `agent_attempts.invalid_assignment` error preserved for invalid
  or absent assignment metadata (never a success value reporting `Unknown`).
  No CLI argument, invocation, claim/dispatch policy, authorization, provider
  preflight, other role, model/effort selection, context/compaction, session
  behavior, usage limit, or persistence schema changed. Documented in the new
  "Configured Claude Implementer permission confirmations" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  The UI label reads "Configured permission confirmations" (not "…prompts")
  to avoid colliding with `ImplementationAction.test.tsx`'s existing blanket
  `/prompt/i` disclosure check on rendered text; the API field name itself
  does use `PermissionPrompts`, matching the CLI flag name.
- One pre-existing, single-file API disclosure guard
  (`GetImplementationAttemptStatusEndpointTests`'s own coherent-assignment
  test, which asserts the response body contains no `"prompt"` substring)
  needed the same narrow, JSON-root-verified, single-occurrence exception
  already applied twice to the two shared cross-role guards for
  `configuredSessionPersistence` — scoped to exactly this one root-level
  property with one of its two coherent values. Five focused tests prove the
  exception still rejects a nested occurrence, a duplicate occurrence, an
  unexpected value, and a genuine leaked prompt alongside the safe fact, and
  permits only the two coherent values. Every other disclosure check in this
  file and the two shared guard files is unchanged.
- Checks actually run: Domain.Tests 524/524, Application.Tests 986/986,
  Infrastructure.IntegrationTests 440/441 (one pre-existing unrelated skip),
  Api.IntegrationTests 312/312 (307 existing + 5 new guard-negative/positive
  cases), Architecture.Tests 9/9 — all green. Frontend 605/605 tests
  (existing `ImplementationAction` assertions extended for the valid,
  absent, and mismatched-sentinel cases), `tsc -b` clean, `oxlint` exited 0
  with the same 20 pre-existing warnings (0 new), `vite build` production
  build passed, `git diff --check` reported no whitespace errors. The NSwag
  client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
  the resulting `api-client.ts` diff is additive only (one new optional
  field). Codex independently passed the full API suite (312/312), focused
  Application (8/8), focused frontend (19/19), typecheck, and diff check
  across its review rounds.
- No adapter argument, invocation, claim/dispatch policy, authorization,
  provider preflight, other role, model/effort selection, context/compaction,
  session behavior, usage limit, or persistence schema changed. The
  account-allowance evidence candidate remains closed without delivery; no
  safe CLI observation contract was established.
- Next action after this slice: superseded by the configured resume-eligibility
  slice recorded at the top of this checkpoint.

## Open risks

- Provider account-usage limits remain unenforced. The only enforced token
  control is the owner-configured, run-scoped token-activity stop at Agent
  claim, a retrospective guardrail on locally recorded usage; it is not an
  account allowance, a per-attempt cap, or a cost limit, and the advisory
  warning remains separate. Per-attempt token usage is not account allowance
  or cost. Provider-session resume, runtime controls, and context-window/
  compaction work remain open under [Increment 4](mvp-delivery-plan.md).
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  bound claim count and reserved time, not actual wall time or provider usage;
  neither has a human override. Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- Raw linked artifact inspection beyond the bounded collaboration-evidence
  drill-down remains open; see the cockpit specification.
- The raw Git observation behind the checkpoint fingerprint (`status`, `diff`,
  per-path hashing) still reads named paths and can read the content of a file
  another name links to. [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)
  and [ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md)
  close the delivery of tracked text to Agents and to the human inspection,
  not this observation, and only Windows has the physical proof.

Older delivery and review details remain in Git. The executor updates this
page with actual delivery evidence; Codex records acceptance and next-slice
decisions in [planner-handoff.md](planner-handoff.md).
