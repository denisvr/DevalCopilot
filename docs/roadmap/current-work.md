# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-29)

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

- Provider account-usage and token thresholds remain unenforced. Per-attempt
  token usage is not account allowance or cost. Provider-session resume,
  runtime controls, and context-window/compaction work remain open under
  [Increment 4](mvp-delivery-plan.md).
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
