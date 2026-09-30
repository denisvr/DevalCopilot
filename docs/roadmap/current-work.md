# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-30)

- Earlier published delivery: `a75d524b42306818acd139a4d00f58234d0e29d5` (parent `c662a4d0915fbf4ece0304bcfd58b17eab964778`) is
  the factual closure of the Claude mutation turn-limit slice (its delivered SHA and post-publication checks; no code or
  product contract change). At the start of this slice, `main`, local `origin/main`, and the live remote matched it, nothing
  was staged or untracked, and only `docs/roadmap/planner-handoff.md` (the planner's slice selection) was modified.
- Published delivery: `85d0822bb4479a38c84aad56be48fb499e86ce88` (parent `a75d524b42306818acd139a4d00f58234d0e29d5`)
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
- Current delivery, based on verified parent `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6` (branch `main`; `HEAD`, local and
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

Older delivery and review details remain in Git. The executor updates this
page with actual delivery evidence; Codex records acceptance and next-slice
decisions in [planner-handoff.md](planner-handoff.md).
