# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md) for the standing review and publication rules,
[current-work.md](current-work.md) for delivery facts, and the
[roadmap](mvp-delivery-plan.md) and accepted [ADRs](../decisions/README.md)
for product and architecture decisions. Verify this checkpoint against Git and
code before relying on it; older decision detail remains in Git.

## Current selection (2026-09-30): run-isolated asynchronous cockpit controls

- Published baseline independently verified: branch `main`; `HEAD`, local `origin/main`, and live
  `origin/main` equal `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6`, with nothing staged, unstaged, or
  untracked. Substantive delivery `85d0822bb4479a38c84aad56be48fb499e86ce88` has parent
  `a75d524b42306818acd139a4d00f58234d0e29d5` and exactly 56 modified/43 added files, including the
  planner GO. The closure has that substantive commit as parent and changes only `current-work.md`.
  The parent chain is fast-forward and the published inventory matches the reviewed slice.
  Independently repeated on the published code: solution build with `--no-restore
  -p:UseSharedCompilation=false -m:1`, 0 errors/0 warnings; Domain 807/807; Application repair filter
  271/271; Api repair plus the three ordinary read-only hosted supervisors 94/94; Architecture 9/9;
  frontend 1052/1052 and `npm run build` including typecheck (existing chunk-size notice). No skips
  occurred in these tests. Generated-client SHA-256 remains
  `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386`. Full backend validation
  remains the independent corrected-tree evidence in the previous review; post-publication lint is
  executor evidence. Its baseline warnings have mixed categories, not only `set-state-in-effect`.
  The accepted formatter limitation includes newly added parameter comments, not only old lines.
  After this planner edit expect the same branch/HEAD/refs, zero staged files, only this handoff
  modified/unstaged, and nothing untracked.
- Select exactly one bounded Increment 4 stabilization outcome: isolate every existing asynchronous
  Agent action and run-setting control in the cockpit to its current run and mounted interaction
  lifetime. Switching projects/runs must not import pending/error/saved/draft state, complete another
  run's UI action, or refresh the wrong run. An old response cannot release a newer request's controls.
  This makes the existing manual collaboration and runtime controls safe to use while navigating.
- Evidence and alternatives: ordinary planning, critical-review, resolution, implementation, and
  code-review hooks hold unqualified pending/error state. The Planner repair tags state only with
  `runId`; correction request/authorization guard completions but preserve old state and mutate refs
  during render. Four setters hold unqualified state; the turn-limit setter tags it but retains old
  errors and identifies in-flight work by run alone. Setter components also continue after `await`,
  updating saved values/drafts or awaiting refresh outside hook guards. Two independent temporary
  regressions failed on published code: A's ordinary planning completion set B's pending flag false,
  and a turn-limit error reappeared after A -> B -> A. Both probes were removed and the clean tree
  verified before selection. The three newly delivered repair hooks provide useful prior race
  coverage, but do not establish this contract for the remaining controls or their callers.
  A single coherent audit and correction across these existing controls is preferable to thirteen
  isolated hook handoffs. Further context sampling is lower priority than this reproduced user-flow
  defect. Account allowance, provider-session resume, and context compaction remain unproven without
  safe provider contracts; observation is not threshold enforcement or invocation eligibility. A live
  provider proof is still valuable but is not required to establish or fix this host-owned defect.
- Precise scope: the six existing ordinary request hooks (Planner, CriticalReviewer, Resolver,
  Implementer, CodeReviewer, ReviewCorrection), all four format-repair hooks, review-correction
  authorization (plain and guided), and the five setters (Codex model/effort, Claude model/effort,
  token warning, token stop, Claude mutation turn limit). Include their immediate cockpit callers,
  preference/threshold/turn-limit controls and guidance entry, and focused tests. Preserve public
  HTTP contracts, generated request serialization, validation limits, safe error mappings, advisory
  versus enforced wording, source/target IDs and provider separation. Existing frontend hook signatures
  may change to bind the current run explicitly; update all immediate callers/tests together.
  The already-correct three repair hooks need only compatibility coverage or changes required by the
  common lifecycle contract, not unrelated rewrites.
- Architectural boundary: frontend interaction lifetime owns pending/error and local success; the
  server remains the sole authority for claim, authorization and setting acceptance. Use an explicit
  run/lifetime/request identity, not only string equality with the latest run ID. Invalidate old work
  on switch and unmount; returning to A creates a new lifetime. Before any API call, reject handlers
  belonging to an obsolete lifetime or an explicitly supplied foreign run, without invalidating a
  valid current request. Prevent duplicate in-flight submissions synchronously within the same active
  control; preserve independent controls/providers and do not invent a global UI mutation lock.
  After each asynchronous boundary, require current ownership before changing state, invoking refresh
  callbacks, consuming a refresh result, clearing drafts or reporting UI success. Ignored accepted
  requests remain real server operations; do not cancel, undo, automatically retry or reinterpret them
  as refused. Boolean success consumed by components must not authorize stale local continuations.
  Re-read server state through the existing current-run refresh/status mechanisms when appropriate.
  Respect committed React lifecycles, StrictMode and abandoned renders: do not read/write generation
  refs during render or suppress lint rules. The official [effect lifecycle contract](https://react.dev/reference/react/useEffect)
  and [ref contract](https://react.dev/reference/react/useRef) apply. A small cockpit-owned lifecycle
  hook is permitted for these demonstrated consumers; keep typed API operations in their concrete
  hooks. No global store, query-library migration, generic workflow framework or error-system rewrite.
- Component boundary: audit post-save and post-refresh continuations as well as hook state. A late
  save, clear or guided authorization may not overwrite another lifetime's selected values, draft,
  validation or synchronization warning. Preserve drafts and truthful sync-failure feedback within
  the current interaction. Reset run/target-owned drafts when that identity changes, retaining existing
  authoritative-value resynchronization. Do not use a keyed full-cockpit remount as the sole fix:
  hooks and immediate controls must satisfy their own supported lifecycle contract.
- Exclusions: new backend behavior/endpoints, provider invocation/flags, claim or dispatch changes,
  source/verification authority, schemas/migrations, new budgets/overrides, allowance or resume,
  compaction/context manifests, scheduler/coordinator, pause/stop/takeover, Git/publication controls,
  project registration/run creation, verification-recipe management, and unrelated read-only fetching.
  Existing read hooks may only be adjusted if necessary for the selected controls' refresh ownership;
  stop and report a broader read-layer defect rather than expanding into all server-state fetching.
  No ADR reversal is needed: retain ADR-0002/0004/0009/0010/0012/0013 and the existing authority boundaries.
- Stop gates: isolation requires altering server authority/serialization, cancelling accepted mutations,
  broad state-management replacement or an excluded workflow; stale actions still reach the API or
  wrong lifetime after the proposed mechanism; correctness requires render-time mutable refs or lint
  suppression; generated client or backend behavior drifts. Report the specific gap and keep the
  complete diff unstaged, uncommitted and unpushed for planner judgment.
- Acceptance evidence: reproduce the two planning probes red before correction, then green; cover
  each in-scope hook with controlled promises for late success/failure after switching, A -> B -> A,
  unmount/remount, old completion while the new lifetime is pending, duplicate same-control submission,
  rejected stale/foreign handlers, current success/refusal and safe errors. Prove obsolete refreshes
  never fire, rejected stale calls do not clear current pending, and independent providers/controls
  remain usable. Include representative real-hook cockpit tests for ordinary versus repair pending
  controls, an implementation/correction action, guided authorization, and setters (save/clear and a
  deferred refresh completing after a switch); assert drafts/saved labels/sync warnings stay local.
  Include StrictMode and same-run rerender compatibility. Test the behavior rather than merely mirroring
  generation implementation; targeted mutation evidence should remove an ownership guard and fail.
  Run affected checks first, then full frontend tests, typecheck, lint and production build on the final
  tree, dependency audit, solution build/client-drift verification, local doc links and tracked/untracked
  whitespace checks. Report exact commands/counts and distinguish baseline diagnostics. Backend tests
  need not be repeated solely for frontend ownership changes; cite unchanged published evidence and
  stop if backend changes become necessary. Update the cockpit contract and a commit-ready
  `current-work.md` with actual evidence and limits. The executor does not edit this planner record.
- Handoff: one new Claude executor chat implements this selection and retains all corrections and
  eventual publication. The full English prompt is provided in the planner chat only. Selection grants
  no commit/push GO; return the complete diff for Codex review. After future GO, one publication
  instruction covers the reviewed substantive commit, normal fast-forward push, live-remote verification
  and tightly bounded factual documentation closure. Re-review material post-GO changes.

- Corrected-diff review (2026-09-30): **GO for this reviewed slice's publication only.**
  Codex independently verified `main`; `HEAD`, local `origin/main` and live `origin/main` equal
  `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6`; nothing staged; 42 modified tracked files and six
  untracked files (48 total, including this planner record). The reviewed inventory is confined to
  the selected frontend controls/hooks/tests and three documentation files; no backend, dependency,
  generated-client, API, provider, budget or ADR change. All prior review blockers are resolved:
  obsolete handlers and validation cannot reacquire or release another lifetime; controls reset their
  own run/target state; component flows retain operation and authoritative-identity ownership; guidance
  also protects edits by draft version; Codex Save/Clear now use the same flow ownership. Codex's two
  independent same-run authoritative-pair probes, previously red, both passed and were removed.
  Independent final checks: full frontend 1285/1285 (96 files); solution build with `--no-restore
  -p:UseSharedCompilation=false -m:1`, 0 errors/0 warnings; `npm run build` including typecheck,
  existing chunk-size notice; lint 12 warnings in untouched files (mixed categories); `npm audit`,
  0 vulnerabilities; generated-client SHA-256 unchanged at
  `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386`; tracked/untracked whitespace,
  changed-file NUL scan and 121 local Markdown path targets clean (anchors not checked). Backend
  suites were not repeated for a frontend-only change. Red/green and mutation counts remain executor
  evidence, distinct from these independent checks.
  Accepted limits: read-only fetching is unchanged; ownership is per mounted hook/component instance;
  React may replace a memoized lifetime and its discarded handlers are rejected; browser layout/passive
  timing and provider behavior are not proven by jsdom; the guidance lifetime guard is defense in depth
  alongside the independently effective edit-version guard. These do not establish allowance enforcement,
  invocation eligibility, provider-session resume, or any new provider capability.
  The publication prompt permits only two precise factual pre-commit corrections in `current-work.md`:
  identify three review-correction rounds, and distinguish this final tree's mutation runs (Codex guards
  removed: 3 failures; authoritative identity ignored: 7) from retained earlier-tree mutation evidence.
  No code, test or product-contract change is authorized after this review; any material change returns
  for review before commit. Expected inventory after this planner-only GO edit remains 42 modified/six
  untracked, nothing staged, same branch/HEAD/refs.
  The same executor chat may commit exactly the reviewed 48-file substantive slice, including both
  handoffs and those factual corrections, push `main` normally to `origin/main`, fetch and verify
  local/live refs and clean state, run the bounded post-publication checks in the prompt, then make
  one `current-work.md`-only factual closure commit and normal push with the same remote verification.
  Do not embed a commit's own SHA in its documentation. Stop on push failure, divergence, inventory
  drift or failed checks; no force-push, reconciliation or unreviewed implementation fix. Publication
  is not complete until verified remotely. No next slice is selected or authorized by this GO.

## Previous selection (2026-09-30): manual format recovery for the remaining read-only collaboration stages

- Publication and baseline independently verified: branch `main`; `HEAD`, local `origin/main`, and live
  `origin/main` all equal `a75d524b42306818acd139a4d00f58234d0e29d5`; staged, unstaged, and untracked
  state empty before this selection. The reviewed turn-limit slice is published as
  `c662a4d0915fbf4ece0304bcfd58b17eab964778`, parent `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`;
  its 107-file inventory, permitted XML correction, generated-client SHA-256, and the current-work-only
  factual closure at this baseline match the publication report. The parent chain is fast-forward; no
  history discrepancy was found. The executor's post-publication checks are recorded in
  [current-work.md](current-work.md); they are executor evidence, distinct from Codex's already-recorded
  independent pre-publication checks. After this planner edit, expect the same branch and refs, nothing
  staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, and nothing untracked.
- Selected exactly one bounded Increment 4 outcome: extend the existing **one manual format-repair
  request per failed source attempt** to CriticalReviewer/CriticalReview (Claude Code),
  Resolver/ChallengeResolution (Codex), and CodeReviewer/ImplementationReview (Codex), completing this
  capability across the currently implemented read-only collaboration stages. The existing Planner
  repair remains behaviorally unchanged. The user can recover these stages after a structurally invalid
  response without losing the failure record or making an unlinked retry look like its repair.
- Evidence and alternatives: [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md)
  explicitly permits one bounded format repair after invalid structured output. The current implementation
  provides that end-to-end capability only for Planner/Proposal. The other three read-only claim/status/UI
  paths have ordinary requests and fail-closed results but no repair-source link or repair operation.
  `Attempt.AgentRepairSourceAttemptId`, its self-reference, and filtered unique index already provide
  immutable lineage and a database backstop; the column/index are not role-specific. Resolver already
  rechecks its ordered challenges inside its claim transaction. CodeReviewer owns both ordered inputs
  and ordered `AttemptVerificationEvidence`, with exact duplicate-review comparison, but a repair must
  additionally retain the failed source's exact identity. CriticalReviewer currently makes a late read
  followed by `SaveChangesAsync`; a read alone is insufficient for the repair's cross-row commit guard.
  Completing all three instances of the same accepted recovery capability is more useful than separate
  disclosure slices or further context sampling after the published untracked, whole-hunk, and changed-line
  work. Account-allowance enforcement, Claude allowance, provider-session resume, and context occupancy/
  compaction still lack the necessary safe contracts for a sound selection. Codex allowance observation
  is neither threshold enforcement nor invocation eligibility. No new external provider capability is
  required here: reuse the existing role-specific fresh invocations, arguments, schemas, and parsers.
- Architectural boundary: each repair is a **fresh invocation of the same read-only role**, with a fixed
  host-authored format reminder and the ordinarily validated durable context. It neither transforms nor
  preserves the meaning of the failed output. Add three protected, bodyless, source-anchored request
  operations under `/api/runs/{runId}/agent-attempts/{sourceAttemptId}` with suffixes
  `critical-review-repair`, `challenge-resolution-repair`, and `code-review-repair`. Each endpoint sends one
  command through the mediator; the role's claim handler derives the target from the source's persisted
  inputs. The frontend supplies no replacement Proposal, challenged-review, ExecutionReport, verification
  set, free text, provider, or permission. Extend the existing role claim handlers rather than creating a
  second orchestration layer; keep role-specific validation, manifests, result types, and supervisors.
- Source rules for these three new operations: same run; Agent kind; `Failed` with exactly
  `InvalidStructuredOutput`; dispatched and concluded; coherent host process evidence proving clean exit;
  supported exact role/provider/response-contract/expected-message tuple, `ReadOnly` profile, and that
  path's current known v1 adapter contract. Require a well-formed assignment, no repair source of its own,
  no already-claimed repair, no durable semantic result messages from the failed source, and the run's
  latest Agent attempt. Unknown and foreign-run sources yield the same fixed 404; invalid, stale,
  already-repaired, and repair-of-repair sources yield safe refusals without persisted-value disclosure.
  Unreadable enum/assignment/process/input metadata must fail closed without materialization crashes;
  do not report database or cancellation failures as invalid source evidence.
- Exact context: retain the source's workspace, checkpoint, and fingerprint, and require them to be
  current under the ordinary role gates. CriticalReviewer retains exactly its one Proposal input,
  including the existing supported first-revision review. Resolver retains the original Proposal and
  complete Challenges in recorded order and validates their owning challenged review and bounded lineage.
  CodeReviewer retains exactly the ExecutionReport and ordered verification execution IDs, including
  initial and correction-report review. The current enabled/latest Passed verification selection must
  still equal the source's recorded set: a rerun, configuration change, reorder, or replacement is an
  ordinary new review, not this repair. Reject missing/duplicate/gapped/foreign/partial identities; never
  substitute a merely similar or overlapping set. Revalidate the ordinary target/lineage/verification
  conditions rather than trusting source rows as authority.
- Claim and dispatch: all normal lifecycle, workspace/lease, fresh Git, one-running-attempt, duplicate
  result, count/time budget, provider token-stop, and current model/effort snapshot rules remain mandatory.
  Refuse invalid repair sources before provider probing, Git capture, or sealing. Re-read source and
  exact inputs at the durable claim boundary **inside the same short transaction after a write guard
  has acquired SQLite's write lock**, before persisting the linked Attempt, inputs, verification rows,
  artifact metadata, lifecycle/event changes, and committing. External work stays outside that transaction.
  For CriticalReviewer, add the bounded repair transaction/guard needed to establish this guarantee;
  preserve its ordinary request behavior. The existing global unique repair-source index remains the
  at-most-one backstop; no new column, backfill, or migration is needed. Roll back refused claims and
  remove proven orphan manifests; preserve artifacts when durability is unresolved and propagate
  cancellation using the established durability-probe pattern. Protect the three new repair paths at
  the final dispatch gate against an incoherent link/source/input identity; do not rerun the pre-claim
  latest-source/no-existing-repair tests there, since the repair itself now owns that slot. Preserve the
  ordinary duplicate-input dispatch classifications. A committed repair consumes its one repair even
  if it is later interrupted or never successfully dispatched.
- Context and outcome: append a small, fixed, role-specific `formatRepairNotice` to the ordinary bounded
  manifest, including both CodeReviewer manifest forms. Do not replay the source's raw output,
  diagnostic, artifact path, parser error, or human text; the source identifier belongs to durable
  provenance, not provider instructions. The unchanged schema and untrusted-evidence boundary remain.
  Sealed replay never rebuilds from a later source or setting. Normal result handlers alone can record
  Acceptance/Challenges, complete Decisions plus revised Proposal, or review results/findings. A repair
  does not add a planning challenge round, bypass the depth-two escalation, authorize implementation,
  mark an invalid result successful, or create correction authority. No automatic follow-up occurs.
- User-visible scope: add source ID/number lineage to the three role status contracts, and bounded
  lineage metadata to historical attempt evidence with safe nulls when unprovable (including existing
  Planner links). Add the three manual cockpit actions with pending/error, refresh, run-switch/stale-response
  handling, no overlap with ordinary requests, and disclosure that repair is fresh and spends a normal
  Agent slot/reserved time. The button is a suggestion; server policy decides eligibility. Display lineage
  after success or failure without saying the source was fixed. Regenerate the client normally. Update
  protocol/cockpit contracts and a commit-ready `current-work.md`; do not rewrite unrelated history.
- Exclusions: mutating Implementer/ReviewCorrection repair, raw-output replay, schema relaxation,
  automatic retry/fallback/debate, new provider arguments/authentication/permissions/models, provider
  session resume, account-allowance observation or thresholds, context-window/compaction/sampling,
  new budgets/overrides, generic workflow/repair framework, coordinator/scheduler, Git mutation or
  publication policy, and new recovery authority. Preserve
  [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md),
  [ADR-0010](../decisions/0010-add-review-correction-response-contract.md),
  [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md), and
  [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md).
- Stop gates: inability to prove the source's current exact inputs or clean terminal read-only identity;
  a repair that needs failed-output replay, changed evidence, permissions, provider contract, or mutating
  recovery; inability to make source/input revalidation and claim atomic; unresolved durability treated
  as success or an orphan; dispatch/replay that changes linked context; need for an ADR reversal or any
  excluded authority. Report the specific gap and keep the diff uncommitted instead of widening scope.
- Acceptance evidence: end-to-end invalid-source -> manual repair -> ordinary validated result for all
  three roles using process doubles, including root/revised CriticalReviewer, first/second Resolver
  resolution and one depth-two escalation, and both initial/correction CodeReviewer report paths.
  Prove invalid repair output produces no semantic result and cannot be repaired again. Test unknown/
  foreign/wrong tuple/profile/version/status/outcome, missing/non-clean process evidence, source with
  semantic messages, corruption/unreadable fields, nonlatest/already-repaired/repair-of-repair, changed
  workspace/checkpoint/inputs/verification, ordinary request compatibility, and all existing hard stops.
  Use file-backed SQLite concurrent claims and actual commit-seam source/input changes; show complete
  rollback, correct error classification, uniqueness, orphan cleanup, cascade/source deletion behavior,
  cancellation and ambiguous commit durability handling. Prove zero provider invocations on refusal,
  coherent linked dispatch, unchanged exact adapter arguments, sealed restart replay, dispatched
  interruption without re-invocation, and exactly-once semantic messages/escalation. Include red/green
  or mutation evidence for exact input/verification matching and one-repair enforcement. Cover protected
  API disclosure, safe historical lineage, and cockpit interaction/stale-run behavior. Run affected checks
  first, relevant full backend/build/Architecture/frontend validation, typecheck/lint/production build,
  formatter/analyzers and dependency/security checks required by the routed standards; separate known
  baseline findings from new ones. Check generated-client drift, local documentation links, and
  `git diff --check`; report commands, outcomes, and environmental skips honestly. Use sequential .NET
  execution and `-m:1` builds to avoid the previously observed compiler output locks.
- Handoff: Claude returns the complete **unstaged, uncommitted, unpushed** diff, including this planner
  selection and a commit-ready `current-work.md` with actual checks and remaining risks, for Codex GO/NO-GO.
  The complete English executor prompt is supplied in the planner chat, not duplicated here. No
  commit/push GO is granted by this selection. After a future GO, one publication instruction covers
  the reviewed substantive commit, normal fast-forward push, live-remote verification, post-publication
  evidence, and tightly bounded factual documentation closure. Re-review any material change after GO;
  stop on divergence or push failure without force-push or history reconciliation.

- Corrected diff review (2026-09-30): **GO for publication of this reviewed slice.** This supersedes the
  initial NO-GO below; no next slice is selected. Codex independently verified branch `main`, `HEAD`,
  local `origin/main`, and live `origin/main` at `a75d524b42306818acd139a4d00f58234d0e29d5`, with
  nothing staged, 56 modified tracked files and 43 untracked files (99 total, including this planner
  record and the commit-ready `current-work.md`). The planner edit changes this record only; the
  expected branch, HEAD, and inventory remain the same. No implementation change was made by Codex.
  - All three findings are closed: repair-only validator calls opt into fresh untracked reads for
    owning attempts, messages, verification commands and executions; the shared lineage/report-chain
    snapshots already read untracked. Ordinary requests and tracked claim writes retain their behavior.
    Seven populated-context seam regressions assert safe refusal, no claim/input/verification/artifact
    rows, and orphan cleanup. The three new hooks invalidate request generations on new requests,
    run switches, and unmount, clear old state, and ignore stale state writes and refresh callbacks;
    nine hook regressions and the real-hook cockpit regression cover these races. The five new helper
    types now have explicit ownership under `Application/Features/Runs/Policies/FormatRepair` with
    matching namespaces/imports. The executor's mutation results remain separately reported evidence.
  - Independently repeated on the corrected tree: solution build `--no-restore
    -p:UseSharedCompilation=false -m:1` with 0 errors/0 warnings; full Domain 807/807,
    Application 2210/2210, Api 621/621, Infrastructure 867 passed/3 environment-gated skips, and
    Architecture 9/9; frontend `npm test -- --run` 1052/1052, `npm run build` (including `tsc -b`)
    clean apart from the reported chunk-size notice; `npm run lint` 20 warnings in untouched files;
    `npm audit --audit-level=low` 0 vulnerabilities. The first sandboxed Infrastructure run failed
    on denied scratch-directory/junction access; the permitted rerun outside the sandbox passed.
    Generated-client SHA-256 remains
    `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386` after the build.
    `git diff --check`, all 43 untracked files' trailing whitespace, and 127 local Markdown link path
    targets passed; fragment anchors were not checked. The unchanged-dependency .NET audit remains
    executor evidence from the initial submission, not an independently repeated audit.
  - Accepted, explicitly bounded formatting limitation: `dotnet format --verify-no-changes
    --no-restore --include <changed C# files>` is not green. It reports whitespace around XML comments
    between positional-record parameters in the three role-status query results and five API
    status/history/evidence responses, including newly added comments that repeat the existing
    pattern, plus three unchanged lines in `CodeReviewContextManifestBuilder.BuildForCorrection`.
    No new helper or test file is reported. Do not describe every diagnostic as pre-existing or claim
    a clean formatter; no unrelated formatter rewrite is authorized by this GO.
  - Limits remain accepted within the selection: deterministic process doubles prove host behavior,
    not real-provider reliability; a committed repair consumes its one repair even if never dispatched
    or invalid again; stale UI completions do not cancel accepted server requests. The existing Planner
    repair hook retains its earlier behavior and is outside this slice. No new provider, recovery,
    budget, or publication authority is introduced.
  - Claude may publish exactly this reviewed substantive diff, including both handoffs, through one
    normal fast-forward commit/push sequence and verified live-remote equality, then run bounded
    post-publication checks against that commit. Only the factual `current-work.md` closure described
    in the publication instruction may follow as a separate documentation commit. Re-review material
    changes before committing; stop on an unexpected inventory, divergence, push failure, or failed
    post-publication check. No force-push or history reconciliation. Report verified publication and
    leave next-slice selection to Codex.

- Initial uncommitted diff review (2026-09-30): **NO-GO; bounded corrections of this same slice.** Codex independently
  verified branch `main`, `HEAD`, local `origin/main`, and live `origin/main` at
  `a75d524b42306818acd139a4d00f58234d0e29d5`, nothing staged, 55 modified tracked and 43 untracked
  files (including the planner-owned selection). The inventory matches the executor report. Independent
  checks passed: solution build `--no-restore -p:UseSharedCompilation=false -m:1` with 0 errors/0 warnings;
  Domain 807/807; Application repair-filtered tests 264/264; Api repair and the three ordinary read-only
  hosted supervisor suites 94/94; Architecture 9/9; frontend 1042/1042 and `tsc -b`; generated-client
  SHA-256 `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386` stable across the
  independent build. Full Application/Infrastructure/Api suites, audits, formatter, lint, and production
  build remain executor-reported evidence, not checks independently repeated in this review.
  `git diff --check` and 127 local link path targets across the four changed Markdown files passed;
  fragment anchors were not checked. Final state remains 55 modified tracked, 43 untracked, and zero staged files.
  The following requirements are not met:
  1. **Fresh authority evidence inside the repair claim transaction.** The new Resolver and CodeReviewer
     revalidation calls ordinary validators whose entity queries still track their results. Issuing the
     query again does not refresh an already-tracked entity. Three independent, file-backed SQLite probes
     committed changes from another connection immediately before `BEGIN`: (a) the same claimed
     verification execution changed from `Passed` to `Failed` with exit code 1; (b) the owning challenged
     review changed to `Failed`/`InvalidStructuredOutput`; (c) an existing Challenge's recorded actor
     provider changed from Claude Code to Codex. All three repair claims still returned success; the
     assertions requiring refusal failed. Fix the repair validators so every authority-bearing entity
     read at the seam uses current database evidence, not the earlier tracker state. Audit all three
     new paths; preserve ordinary request behavior and tracked Run writes. Add red/green regressions
     that keep the claim context alive and populated and mutate existing rows, not only insert new
     IDs/remove filtered rows. Prove fixed refusals and complete rollback/orphan cleanup. Keep the
     in-transaction lock/guard; moving the seam before `BEGIN` is appropriate for the current non-deferred
     SQLite transaction, but does not solve stale EF instances. The official
     [EF tracking contract](https://learn.microsoft.com/en-us/ef/core/querying/tracking) explains that
     tracked query results reuse the existing instance without overwriting its values; the
     [SQLite transaction contract](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
     separately describes serializable and explicitly deferred transactions.
  2. **Run-switch reset and stale-response protection in the three new repair hooks.** Their state is
     tagged only with `runId`, and every late completion overwrites it. An independent frontend probe
     started A, switched to B and started B, then completed A: B's `requesting` became false while its
     request was still pending. Another recorded an error in A, switched A -> B -> A, and the old error
     reappeared. Both expected-protection assertions failed; the same implementation pattern is present
     in all three hooks. Track the current run/request generation, reset on switching, and reject stale
     state updates and stale refresh callbacks. Add success and failure races, A -> B -> A, and a late
     old request versus a newer pending request for each hook and representative cockpit action overlap.
  3. **New Application type ownership.** `AgentRepairLineage`, `ReadOnlyFormatRepairInputs`,
     `ReadOnlyFormatRepairLink`, `ReadOnlyFormatRepairManifest`, and `ReadOnlyFormatRepairSource` were
     added directly to `Application/Features/Runs`. The routed CQRS and capabilities standards prohibit
     new types at a feature root that already has operation folders. Place only these new types under
     explicit responsibility ownership with matching namespaces/imports; keep operation orchestration
     in its own handler. Do not move unrelated legacy types or build a generic repair framework.
  All five independent failing probes were temporary and have been removed; the Application test assembly
  was rebuilt after removing the backend probes. No implementation fix was made by Codex. Update the
  protocol/current-work evidence to match the corrected behavior, including the fact that the earlier
  suites missed stale tracked-row and stale request-generation cases. Run affected tests first, then
  relevant full backend/frontend validation for these authority and UI corrections; report exact commands,
  outcomes, inventory, and remaining limits. Return the complete diff unstaged, uncommitted, and unpushed
  for re-review. No commit/push GO or next-slice permission is granted.

## Previous selection (2026-09-30): optional Claude mutation agentic-turn limit

- Verified baseline: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal
  `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`; staged, unstaged, and untracked state empty.
  The prior slice is complete: substantive `2c0c1be7895f31399db11d6e2320fb089da39296`, parent
  `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`; this baseline changes only `current-work.md` for
  factual closure. After this planner edit, the executor expects the same refs, nothing staged,
  only this file modified/unstaged, and nothing untracked. No implementation or publication GO.
- Selected exactly one end-to-end Increment 4 slice: an owner-set optional **Claude mutation
  agentic-turn limit**, shared by initial implementation and review correction. The cockpit records
  intent, each claim snapshots it immutably, and both adapters actually pass `--max-turns N`.
  This bounds the provider's internal multi-step loop independently of host timeout, while retaining
  safe Git reconciliation after a failed invocation that may already have edited files.
- Evidence and comparison: `ClaudeCriticalReviewAdapter` already passes `--max-turns 1`;
  `ClaudeImplementationAdapter` and `ClaudeReviewCorrectionAdapter` omit it intentionally.
  Both mutating supervisors already capture post-invocation Git evidence, and their result handlers
  mark suspected failed mutation `NeedsAttention`. This connects an available explicit control to
  existing recovery rather than adding a detached disclosure. Further context sampling has lower
  marginal value after the published whole-hunk and changed-line slices. Generalized format repair
  remains a possible later recovery improvement, but the current CriticalReviewer request already
  permits a fresh request after unsuccessful review; adding repair provenance there would not add
  this resource bound. Context occupancy cannot be inferred from aggregate token usage
  (`ClaudeCliTokenUsage` deliberately ignores `modelUsage`). Claude account allowance and provider
  resume still lack the required safe contracts; Codex allowance observation alone proves neither
  threshold enforcement nor invocation eligibility. None of those alternatives is selected.
- Current external contract: the [official Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  defines `--max-turns` in print mode as an agentic-turn limit that exits with an error when reached;
  the [headless contract](https://code.claude.com/docs/en/headless) describes failure exits and invalid
  flags. The local `claude` command was unavailable; no installed-version or authenticated runtime
  observation is claimed. This is an explicit provider argument, not an inferred default, measured
  turn count, token/cost/account ceiling, host sandbox, or eligibility claim.
- Architectural decision: nullable run request and flat immutable Attempt snapshot; accepted integer
  range 1..100; null requests no override, including for new runs. Additive migration leaves old data
  null without backfill. A protected set/clear operation records human intent and its event atomically
  for active runs. Fresh late claim reads and a commit guard apply to the two mutation paths; already
  claimed attempts never read a later run request. New contracts are `claude-implementation-v2` and
  `claude-review-correction-v2`; preserve known v1/null invocations and history, reject a non-null cap
  with incompatible provenance, and keep configured-fact disclosures version-specific. Legacy null
  is not an observed unlimited capacity. Existing failure classifications/reconciliation are sufficient;
  do not invent a limit-reached outcome from a generic failure. The optional override refines the
  documented mutation invocation choice without reversing accepted ADRs or replacing hard budgets.
- Scope: run request/setter/event, flat Attempt snapshot, additive migration, both mutation
  claim/dispatch/adapter/status paths, historical inspection, cockpit set/clear control, generated
  client, focused tests, and relevant product/architecture documentation. The executor prompt is
  supplied separately in the planner chat. Preserve ADR-0004/0009's authority/context boundaries,
  ADR-0010's ordered correction inputs,
  ADR-0012/0013's hard count/time ceilings, token stops, sealed replay, and worktree exclusivity.
  No new provider/account/session capability, context feature, generic control framework, automatic
  retry/fallback, or scheduler. Return a complete unstaged, uncommitted, unpushed diff with a
  commit-ready `current-work.md` and actual checks for Codex GO/NO-GO.
- Stop gates: unsafe flag compatibility; guessed v1 history or expanded dispatch eligibility;
  a configuration race, corruption, or replay that can discard/change the immutable cap;
  failure recovery without fresh Git evidence or with fabricated success; new recovery authority,
  an excluded capability, or ADR reversal. Report the evidence gap without expanding scope.
- Acceptance: prove request validation/set/clear and protected API behavior; atomic intent/event
  persistence; migrated SQLite legacy-null/round-trip/upgrade/down and claim-setting/lifecycle
  races; both mutation claims with authorization rollback/orphan cleanup; exact adapter arguments,
  null/v1 compatibility and invalid/version-incoherent refusal; immutable snapshot dispatch and
  restart replay; failure with unchanged/changed/unreadable source and no false success/retry;
  current/legacy/malformed status/history disclosure and cockpit interaction flows. Run affected
  checks first, relevant full backend/frontend/build/architecture validation, generated-client
  stability, formatter/analyzer/security/dependency checks, local links, and `git diff --check`.
  Report commands, results, skips, and limits; doubles are not real provider enforcement evidence.
- Future publication remains one instruction after explicit GO: reviewed substantive commit, normal
  fast-forward push, live-remote verification and agreed checks, then only factual documentation
  closure committed/pushed/verified normally. Material change requires re-review; failed push or
  divergence stops. This selection grants no commit/push GO.


- Uncommitted implementation review (2026-09-30): **NO-GO; correct this same slice and keep the complete
  diff unstaged, uncommitted, and unpushed.** Independently verified `main`; `HEAD`, local `origin/main`,
  and live `origin/main` remain `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`. Nothing staged;
  60 modified tracked files (including this planner record), 41 untracked files. Reconcile the report's
  59-modified count with the complete inventory on return; no substantive commit or push exists.
  The snapshot, race rollback, versioned arguments, and ordinary failure/reconciliation tests are useful
  evidence, but these selected fail-closed and disclosure requirements remain unmet:
  1. **Persisted assignment coherence at dispatch.** The two eligible mutation feeds carry the numeric
     limit and version without proving the permission profile; `MarkAgentAttemptDispatched` checks
     defined role/provider and response-role coherence but not the cap-bearing assignment's complete
     tuple. A reviewer SQLite probe changed a capped v2 implementation's profile to `ReadOnly`: the
     feed still returned it and the final gate succeeded. The adapters receive no permission profile
     and accept that valid cap/version. Reject incompatible persisted cap-bearing role/provider/
     response/profile/version facts before provider invocation in both mutation paths; a factory
     rejection or `Unknown` read-side fact alone is insufficient. Preserve coherent v1/null history
     and replay, and avoid unrelated expansion of historical dispatch eligibility.
  2. **Validate the persisted numeric representation before lossy materialization.** Nullable `int`
     projection is not a safe corruption boundary on SQLite. A reviewer probe stored `3.5` in
     `Attempts.AgentRequestedMaxTurns`: the implementation feed materialized `3` as a valid request,
     which the adapter accepts. Stored `4294967297` instead raised `OverflowException` while reading
     the candidate list. Correct the narrow Run/Attempt turn-limit storage/read boundary so fractional,
     overflowing, or nonnumeric representations cannot become an invented valid request or break
     healthy candidates/projections. Claims must safely refuse malformed Run requests, clean up sealed
     artifacts, and leave correction authorization unconsumed; malformed Attempt snapshots must never
     reach a provider and must retain safe status/history/cockpit behavior. Test persisted SQLite values
     in both paths, including a healthy sibling row; do not merely validate an already-coerced `int` or
     blanket-catch exceptions and drop the cap. Add storage integrity constraints if appropriate, without
     changing null legacy semantics or backfilling invented requests.
  3. **Request provenance is not invocation observation.** The new evidence enum, API response XML,
     protocol, and cockpit specification say `Requested` means the number "was passed"; that fact is
     also returned for undispatched attempts and Run intent before any attempt exists. Describe the
     saved/snapshotted request and the versioned adapter's argument behavior separately. In
     `current-work.md`, distinguish a rejected flag (ordinary failure) from an ignored flag (not proven
     or detected by this implementation); do not claim ignoring necessarily causes failure.
- Independent validation of the submitted tree: solution build with `--no-restore
  -p:UseSharedCompilation=false -m:1` passed, 0 warnings/errors (the first parallel solution build
  encountered an internal output-file collision); Domain 765/765; Application 1904/1904;
  Infrastructure turn-limit/adapter/migration 69/69; API turn-limit/projection and both hosted mutation
  supervisors 75/75; frontend turn-limit/control/display/hook and cockpit 180/180. The three temporary
  reviewer probes failed as described above and were removed; no implementation file was edited by
  the reviewer. Generated client SHA-256 remains `b5f82c9b030fa1259c5456628b2196fea28e578d4193a9333a27a6e2e8ed424e`.
  `git diff --check` has no whitespace errors and emits the generated-client CRLF notice.
  Executor-reported broader checks remain reported evidence, not independently rerun checks.
- Correction stays in the existing Claude executor chat. Add red regression evidence for the findings,
  run affected checks first and relevant full validation after the correction, reconcile the complete
  inventory, and update the commit-ready `current-work.md` with actual checks and limits. Return the
  entire unstaged/uncommitted/unpushed diff for Codex re-review. No publication GO or next selection.

- Corrected uncommitted diff re-review (2026-09-30): **NO-GO; continue this same correction round,
  unstaged, uncommitted, and unpushed.** Independently verified `main`; `HEAD`, local `origin/main`,
  and live `origin/main` still equal `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`; zero staged,
  61 modified tracked files, 44 untracked files. The demonstrated `3.5`/overflow/text reads, known
  incompatible-profile final gate, request-provenance wording, claim rollback, and historical-null
  tests are corrected. The field-only string mapping is a bounded change to the new flat columns,
  but it still loses the SQLite storage class before validating the request:
  1. **Storage-class coercion remains dispatchable.** A reviewer wrote `new byte[] { 0x37 }`
     (SQLite BLOB `X'37'`) through the existing raw-storage test helper. Both mutation feeds returned
     it as cap `7` beside a healthy cap `9`, and separate direct final-gate probes succeeded in both
     paths. Reading a column with `GetString` does not prove that its stored value is an integer or
     text; the BLOB is decoded to digits before `ClaudeMutationTurnLimit.Read` sees it. Reject
     unsupported storage classes before conversion, rather than validating only the converted
     string or declaring a digit BLOB to be a supported request. Apply the narrow storage boundary
     consistently to Run intent, immutable Attempt snapshots, claim reads/guards, dispatch, and
     disclosures. Preserve null and valid integer semantics, migration compatibility, late-read
     atomicity, orphan cleanup, unconsumed correction authorization, and healthy sibling behavior.
     A justified storage constraint is an alternative only with evidence that unsupported writes
     are rejected and migration/read behavior stays safe; do not broaden into a persistence framework.
  2. **The correction feed invents the provider during coherence checking.**
     `GetEligibleReviewCorrectionAttempts` neither filters nor projects the actual provider, then
     passes the constant `ClaudeCode` to `IsDispatchCoherent`. A reviewer changed a cap-bearing
     correction row to provider `Codex`; the feed still returned it. The implementation counterpart
     excluded it, and the corrected final dispatch gate already refuses it, so this finding does
     not claim a provider invocation bypass. Make the cap-bearing correction feed check the actual
     persisted provider without expanding or changing null historical eligibility. Add the missing
     provider-provenance regression and keep documentation consistent with the real guards.
- Independent correction checks: solution build `--no-restore -p:UseSharedCompilation=false -m:1`
  passed with 0 warnings/errors; Domain 782/782; focused Application mutation claims, setter,
  dispatch, status, history/evidence, cockpit and turn-limit tests 187/187; Infrastructure turn-limit,
  adapter, storage and migration 79/79; API turn-limit/projection/malformed-storage and both hosted
  mutation supervisors 115/115. Reviewer BLOB probes failed 2/2 in each of the feed and direct-gate
  runs; the provider-feed probes failed for correction and passed for implementation. Temporary
  probes were removed, and Application was rebuilt and the focused suite rerun afterward. No
  implementation file was edited by the reviewer. Generated-client SHA-256 remains
  `b5f82c9b030fa1259c5456628b2196fea28e578d4193a9333a27a6e2e8ed424e`; `git diff --check` has
  no whitespace errors and emits two CRLF notices (model snapshot and generated client). Broader
  executor checks remain reported evidence; frontend was not independently rerun in this re-review.
- Return the complete corrected diff in the existing Claude chat, with red/green storage-class and
  provider-feed evidence, affected checks followed by relevant full validation, accurate changed-file
  inventory, and a commit-ready `current-work.md`. Update the storage claims in the protocol and
  comments to match the actual contract. No commit/push GO, publication instruction, or next slice.

- Second corrected diff re-review (2026-09-30): **NO-GO for one remaining storage-identity issue;
  keep this same slice unstaged, uncommitted, and unpushed.** Verified `main`; `HEAD`, local
  `origin/main`, and live `origin/main` remain `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`;
  zero staged, 61 modified tracked files, 45 untracked files. The digit-BLOB dispatch bypass and
  correction-feed provider assumption are corrected. The new mapping is confined to the two
  selected columns and is an acceptable architectural location, but its encoding is ambiguous:
  actual TEXT `blob:37` and BLOB `X'37'` both become the same CLR string. `ConfigureParameter`
  then sends either back as a BLOB. A raw TEXT `blob:ZZ` instead throws `FormatException` while
  preparing the original-value parameter. REAL positive infinity is read as text and rebound as
  text, which cannot match the original REAL in the concurrency predicate. Reviewer SQLite
  repair probes failed for TEXT `blob:37`, TEXT `blob:`, TEXT `blob:ZZ`, and REAL positive
  infinity (4 failed/9 total); the first two and infinity produced false concurrency conflicts,
  and the invalid hex text threw. The high-precision finite REAL probe passed; no general finite
  rounding failure is claimed.
- Required bounded architectural correction: the internal representation must distinguish the
  actual SQLite storage classes without collisions. Keep canonical INTEGER requests and null
  semantics, but encode every non-integer class disjointly (including actual TEXT that resembles
  any internal marker), or use an equivalently explicit typed representation. Rebind original
  values with their actual types and content; do not guess BLOB provenance from unescaped text
  or rely on integer affinity to reconstruct every REAL. Keep parameter and literal behavior
  consistent where relevant. This is a correction of the two-column mapping, not a general
  persistence framework, migration redesign, or new recovery authority. Prove unchanged
  storage class/content after unrelated Run saves and successful protected set/clear repair,
  as well as genuine concurrent-change refusal. Include marker-looking TEXT, valid/invalid/empty
  hex suffixes, finite and non-finite REALs, integer/null cases, and digit/empty/arbitrary BLOBs.
  Retain safe malformed claims/projections, zero invocation, healthy siblings, and sealed replay.
- Independent checks: build `--no-restore -p:UseSharedCompilation=false -m:1` passed with
  0 warnings/errors; Domain 782/782; focused Application 192/192; Infrastructure turn-limit,
  adapter/storage/migration 81/81; API turn-limit/projection/malformed-storage and both hosted
  supervisors 123/123. Temporary reviewer probes were removed; Infrastructure was rebuilt and
  its focused suite rerun afterward. No implementation file was edited by the reviewer. The
  generated-client SHA-256 remains `b5f82c9b030fa1259c5456628b2196fea28e578d4193a9333a27a6e2e8ed424e`.
  `git diff --check` has no whitespace errors, with the two known CRLF notices. Broader executor
  checks remain reported evidence. Return the complete corrected diff in the existing Claude
  chat with red/green evidence, actual checks, accurate storage documentation and commit-ready
  `current-work.md`. No commit/push GO or publication instruction is granted.

- Final corrected diff re-review (2026-09-30): **GO for publication of this reviewed slice,
  subject only to the factual XML correction below before staging.** Independently reverified
  `main`; `HEAD`, local `origin/main`, and live `origin/main` all remain
  `5a1f42c3f0a34b5635aa8a0ef1c52fa3aa298944`; zero staged, 61 modified tracked files and
  46 untracked files (107 total, including this planner record and `current-work.md`). The
  representation now distinguishes INTEGER, REAL, TEXT and BLOB without marker collisions,
  preserves REAL bits and original parameter types, and aligns SQL literals with binding.
  The storage matrix proves unrelated saves, set/clear repair, genuine concurrency refusal,
  exact class/content preservation, and integer/null compatibility. Both feeds and the final
  gate reject malformed or incoherent cap-bearing attempts; the correction feed checks the
  actual provider through a SQL comparison. Prior request-provenance, immutable replay,
  claim rollback/orphan/authorization, and fresh-Git failure-recovery requirements remain
  satisfied. This is still the selected optional provider-argument request, not evidence of
  measured turns, real provider enforcement, account allowance, or resume capability.
- Independent final checks: solution build `--no-restore -p:UseSharedCompilation=false -m:1`
  passed with 0 warnings/errors; full Domain 782/782, Application 1967/1967, API 581/581,
  Architecture 9/9 and frontend 974/974; frontend `tsc -b` passed; focused Infrastructure
  turn-limit, adapter, migration and exact-storage tests 242/242. Generated-client SHA-256
  remains `b5f82c9b030fa1259c5456628b2196fea28e578d4193a9333a27a6e2e8ed424e`.
  `git diff --check` has no whitespace errors (two known CRLF notices); 143 local link
  paths resolve. Executor-reported full Infrastructure 867 passed/3 environment skips,
  earlier lint/build/audits and formatter diagnostics remain reported evidence rather than
  independently rerun results. No reviewer probe or unexpected temporary file remains.
- Only permitted pre-staging wording correction: in the XML `<para>` of
  `src/backend/DevalCopilot.Domain/Features/Runs/ClaudeMutationTurnLimit.cs`, replace the
  obsolete claim that a REAL or TEXT value is its own text with the actual type-preserving
  representation (canonical INTEGER digits; tagged REAL bits, TEXT and BLOB bytes; absent
  null). Do not change executable code, tests, wire contracts, migration, or other text as
  part of this correction. This named factual edit needs no separate GO.
- Publish only the reviewed substantive set, including `current-work.md` and this GO record,
  with a normal fast-forward push and exact live-remote verification. The single publication
  instruction is supplied to the owner in chat. After the agreed checks against the substantive
  commit, allow only a factual `current-work.md` closure, committed/pushed/reverified normally.
  A material post-GO change, unexpected file, recurring failure, push failure or divergence
  stops for re-review; no force-push or history reconciliation. This GO selects no new slice.

## Previous selection (2026-09-30): bounded changed-line samples for oversized tracked hunks

- Verified planning baseline: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`; staged, unstaged, and untracked state are empty. The tracked-hunk slice was delivered as `b19413fac3f82f400c00ea6ab787d45c2898f1ae` and factually closed at this baseline. After this planner edit the executor must expect the same branch and SHA, nothing staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, and nothing untracked. Git and code prevail over this record.
- Objective: improve the five Agent-stage context manifests where a valid text hunk is larger than the 8 KiB complete-hunk budget and currently contributes no text at all. From the already parsed, captured diff, add a **separate, bounded changed-line sample** for eligible oversized hunks, with explicit file/hunk identity and incomplete-evidence labels. This advances the roadmap's progressive context and the protocol's changed-hunk guidance without presenting a fragment as a complete or applyable patch. The current `ChangeEvidenceManifest`, `TrackedDiffParser`, and `TrackedDiffSelector` are the integration points; [Git's patch format](https://git-scm.com/docs/diff-format) and [`git diff --binary`](https://git-scm.com/docs/git-diff) define the external input format. ADR-0004's selected context and ADR-0009's role/provider boundary remain intact.
- Boundaries: keep `diff` assembled solely from complete headers and complete hunks, and preserve the exact small, fully supported diff shape (`diffTruncated: false`, no `diffSelection`). Add samples only under `diffSelection`, never inside `diff`, for a recognized, validated text file and a whole parsed hunk that cannot fit the maximum 8 KiB tracked-diff budget. Select actual `+`/`-` change lines from that hunk in deterministic original order, including both sides where present; identify the file and 1-based hunk ordinal, the total versus shown change-line count, and whether each shown line itself was shortened. Label the sample as incomplete, non-patch, and untrusted; a shortened line must end at a Unicode-scalar boundary and must never be labelled complete. Use a small, explicit per-line and aggregate UTF-8/serialized-JSON ceiling (at most 4 KiB aggregate and 16 sampled hunks), with round-robin fairness across files so an oversized first hunk or line cannot hide every later eligible file. The existing 8 KiB `diff` ceiling, 32 KiB serialized-manifest ceiling, full changed-path accounting, untracked previews, fixed omission reasons, and handler refusal remain authoritative. The fitter may reduce and finally omit all optional samples, but must report sampled versus unsampled eligible counts truthfully and retain mandatory context and the prior safe refusal. Reuse the shared policy so all five builders and the implementation-review correction variant receive the same behavior; sealed dispatch/restart replay must use the recorded bytes without recomputation.
- Exclusions: no new Git call, capture-command/fingerprint/checkpoint change, source-file read or generic retrieval route, API/frontend/schema migration, provider CLI/runtime contract, session resume, compaction, generated summary, account allowance/threshold, token or claim policy, approval authority, or broad parser rewrite. Binary, metadata-only, malformed, unsupported, and unrecognized blocks never yield samples. Do not add `Unknown`-only scaffolding or infer provider capability from CLI defaults.
- Stop gates: stop for planner review if a sampled line cannot be tied to a validated hunk and distinguished from full patch text; if malformed or binary input can leak through the sample path; if a sample can exceed its byte/JSON bound, split a Unicode scalar without an incomplete marker, silently lose changed-path or omission accounting, or make a mandatory-only manifest appear to fit when it does not; or if this requires changing Git capture identity, sealed replay, or an accepted ADR. Do not broaden into file reading to work around a missing diff line.
- Acceptance evidence: first demonstrate the current zero-text oversized-hunk case, then prove changed-line samples for a large single hunk, an early huge line beside a later file, multiple eligible files/hunks, additions and deletions, Unicode and JSON escaping, no-newline markers, tight aggregate and 32 KiB fitting, and deterministic repeat capture. Assert exact small-diff and existing complete-hunk compatibility; the `diff` field never contains a partial hunk, `includedHunks` counts only whole hunks, and every sample/omission is truthfully classified. Cover all five builders including both implementation-review forms, malformed/unsupported/binary refusal, mandatory-only oversize refusal, and hosted sealed-manifest restart replay. Prefer targeted red/green or mutation evidence for sample fairness and the no-partial-patch invariant. Run affected tests first, then relevant full backend/build/architecture checks, generated-client stability, frontend only if touched, documentation links, and `git diff --check`; report commands, results, and skips. Update the protocol's context contract and a commit-ready `current-work.md` entry. Return the entire **unstaged, uncommitted, unpushed** diff for Codex GO/NO-GO. This selection grants no commit/push GO. After a future GO, one publication instruction covers the reviewed substantive commit, normal fast-forward push, live-remote verification, and tightly bounded factual documentation closure; re-review any material change after GO.

- First uncommitted-diff review (2026-09-30): **NO-GO; documentation-only correction in this same slice.** Independently verified `main`, `HEAD`, local and live `origin/main` at `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`, zero staged files, seven modified tracked files (including this planner record) and two untracked files. Reviewed the shared sampler/fitter, new tests, real-Git and hosted replay assertions, and protocol/handoff wording; independently passed focused Application sample and real-Git tests 117/117 and hosted critical-review supervisor tests 17/17; `git diff --check` is clean. The reported full suites and mutation checks remain applicable to the present code. The one blocking issue is the `current-work.md` current-delivery sentence ending `nothing staged, committed, or pushed.` That is a temporary checkout status and would become false in the substantive commit, contradicting the required commit-ready handoff. Remove only that clause from the current-delivery description; retain the verified preflight sentence above it and the actual checks/risks. Do not change code, tests, or behavior. Recheck local documentation links and `git diff --check`, then return the whole diff unstaged, uncommitted, and unpushed for re-review. Earlier test results apply to this documentation-only correction. No commit/push GO or next-slice authorization is granted.

- Correction re-review (2026-09-30): **GO for the reviewed oversized-hunk sampling slice only.** Independently verified `main`, `HEAD`, local and live `origin/main` at `f6e26c109ef8679f34ba9f4b1c14e3dc391afb54`, zero staged files, the same seven modified tracked files and two untracked files. The current-delivery description in `current-work.md` now ends at `frontend change.` and preserves the historical preflight, implementation, actual checks, and risks. The documentation-only correction closes the previous NO-GO; independently checked 119 local links/anchors across the four changed/handoff documents with zero failures, and `git diff --check` is clean. The independently passed Application sample/real-Git 117/117 and hosted supervisor 17/17 from the preceding review remain applicable, alongside the executor-reported full suites and mutation evidence. No additional test run is claimed for this documentation-only correction. No next slice is selected.
- Single publication instruction for this GO: before staging, verify `main`, the same exact parent, matching local/live `origin/main`, zero staged files, and exactly the seven modified tracked and two new files reviewed here, including `current-work.md` and this planner record. Stage precisely those nine paths and verify the staged inventory, staged `git diff --check`, and local documentation links. Commit the reviewed substantive slice, push normally as a fast-forward to `origin/main`, fetch and independently verify the live remote equals that commit, and confirm a clean tree. Against that substantive commit, run `dotnet build DevalCopilot.slnx --no-restore -p:UseSharedCompilation=false`, then Application tests with `--no-build --filter "FullyQualifiedName~TrackedDiff"` and Api tests with `--no-build --filter "FullyQualifiedName~ClaudeCriticalReviewSupervisorHostedTests"`; report exact commands, results, and skips and verify the generated client and tree remain unchanged. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and checks actually run, and mark its post-publication action completed. Commit and push that documentation-only closure normally; reverify `HEAD`, local `origin/main`, live remote, and clean tree. Do not embed a commit's own SHA in its own documentation. Any material change after GO, unexpected file, failed check, failed push, or remote divergence stops for Codex re-review/direction; no force-push or history reconciliation. This GO authorizes only this slice's publication and factual closure.

## Previous selection (2026-09-30): bounded tracked-hunk evidence across Agent manifests

- Verified planning baseline: `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `882c707252a3ea6da1302b67bbe65eefb36c331b`; staged, unstaged, and untracked state empty. The preceding untracked-preview slice was delivered in `6367e359674799fb1f41d8a919c61ed56f15b4cd` and factually closed at this baseline. After this planner edit the executor must expect the same branch and SHA, nothing staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, and nothing untracked. Git and code prevail over this record.
- Objective: finish the bounded Git `changeEvidence` context on the tracked side for the five Agent manifest builders already receiving the captured diff. Today `UntrackedFileManifestSection.BuildChangeEvidence` copies the first 8,192 UTF-16 characters of `CompleteDiff`; it can cut a hunk or Unicode scalar and let a large first file hide later small changes. Build one shared, deterministic selection of **complete text hunks across changed tracked files**, with an honest account of omitted evidence and a combined serialized-manifest fitting policy for those hunks and the existing untracked previews. This advances the Increment 4 progressive-context and changed-hunk requirements in [the roadmap](mvp-delivery-plan.md#increment-4-real-codex-and-claude-code-collaboration) and [protocol](../architecture/agent-collaboration-protocol.md#token-efficiency). Git's [patch format](https://git-scm.com/docs/diff-format) and [`git diff --binary` contract](https://git-scm.com/docs/git-diff) are the external format references; the actual captured command and tests remain authoritative for this codebase. ADR-0004's selected durable context and ADR-0009's role/provider boundary remain intact.
- Boundaries: consume only the already bracketed `GitWorkspaceEvidenceResult.CompleteDiff` and `ChangedPaths`; do not issue a new Git call or redefine the checkpoint fingerprint. Preserve the exact small, fully included **text** diff string and existing `diffTruncated: false` shape where it fits and is safe to include. For larger text diffs, select complete file headers and complete hunks with a deterministic, cross-file fair policy under the existing 8 KiB inlined-diff bound measured in UTF-8 bytes; never cut a hunk or Unicode scalar, imply a selected subset is a complete/applyable patch, or let one oversized hunk starve a later small file. Keep all changed-path metadata. Represent omitted files/hunks and nontext, oversized, or safely unparseable portions with bounded fixed-reason metadata, with explicit completeness semantics; never echo source text, binary payload, path, or exception in an omission reason. Keep the existing 32 KiB serialized manifest ceiling and claim-handler refusal: fit tracked selection and untracked previews together, reducing optional text first while preserving authoritative plan/review inputs and truthful omission accounting; if those mandatory fields and accounting cannot fit, preserve the existing safe refusal. The five builders and the review-correction variant share the behavior, under their current untrusted-evidence framing. Place new shared manifest responsibilities in an explicit responsibility folder, and move the previous root-level `UntrackedFileManifestSection` there if needed, in line with the routed architecture standard.
- Exclusions: no raw full-diff/file retrieval, frontend or API route, migration, Git capture command or fingerprint change, provider flag/model/effort/permission change, session resume, compaction, generated summary, account allowance or threshold enforcement, approval or claim-eligibility change, review-authority change, untracked-reader containment/hash rewrite, or broad cleanup of the previous slice's SHA-1 analyzer suppression. Keep the existing sealed-manifest and replay contract. If a format requires a wider Git or security design, stop for planner review rather than guess or add `Unknown`-only scaffolding.
- Stop gates: stop if the selector cannot distinguish whole hunks and their file headers for an encountered format without misrepresenting completeness; if fitting can silently discard mandatory context, lose an untracked-path accounting entry, exceed 32 KiB, or replace an existing safe refusal with an unbounded manifest; if stable fingerprint, snapshot coherence, or sealed replay would need a semantic change; or if accepted ADRs or applicable standards require a material architectural decision outside this bound. Binary patches, metadata-only changes, unusual quoted paths, missing final newline, and malformed or unsupported patch text must be handled truthfully, never by a raw character prefix.
- Acceptance evidence: first prove the current first-8,192-character defect with a deterministic fixture and a real disposable Git worktree: multiple changed tracked files with a large first file, a later small hunk, a hunk crossing the old cut point, UTF-8 boundary cases, staged plus unstaged changes, metadata-only and binary changes. Assert the selected result is deterministic, within the byte/manifest ceilings, includes later useful hunks, and reports omitted material accurately; show the unchanged fingerprint and diff capture for identical input. Cover empty/small diff compatibility, untracked previews beside tracked hunks, tight 32 KiB fitting and mandatory-only refusal, all five Agent builders including implementation review correction, and a sealed-manifest/restart replay path. Use targeted mutation or red/green evidence for hunk integrity and fairness. Run affected tests first, then relevant full backend/build/architecture checks, generated-client stability, frontend only if touched, documentation links, and `git diff --check`; report actual commands, results, and environmental skips. Update the protocol's context section and a commit-ready `current-work.md` entry. Return the complete **unstaged, uncommitted, unpushed** diff for Codex GO/NO-GO. This selection grants no commit/push GO. After a future GO, one publication instruction covers the reviewed substantive commit, normal fast-forward push, live-remote verification, and tightly bounded factual documentation closure; re-review a material change after GO.

- First uncommitted-diff review (2026-09-30): **NO-GO; correct short malformed and unsupported diffs in this same slice.** Verified `main`, `HEAD`, local and live `origin/main` at `882c707252a3ea6da1302b67bbe65eefb36c331b`, zero staged files, 11 modified tracked files plus one tracked deletion, and 13 untracked files, matching the executor's inventory; `git diff --check` is clean. The reported full suites and real-Git evidence support the main hunk-selection path, but `ChangeEvidenceManifest.Evidence` tests only `!parsed.ContainsBinaryPatch` and the UTF-8 byte count before its small-diff fast path. It never requires `parsed.Recognized` or all parsed files to be supported. Thus a short string that does not start with `diff --git`, or a short block with a malformed hunk, unsupported header, or unparseable path, is copied verbatim as `diff` with `diffTruncated: false` and no `diffSelection`, although the parser already classified it as untrustworthy. The existing manifest test for unsupported text uses 64 KiB and misses this fast path. This contradicts the selected stop gate and the new protocol/current-work claims. Gate the exact small-diff compatibility path on a recognized, fully supported text/metadata-only parse (with the empty-diff case handled explicitly), and use truthful bounded omission/selection metadata for short unsupported or malformed input; do not disclose it as a complete diff. Add targeted failing-before/passing-after manifest tests for short non-header text, a short malformed hunk, a short unsupported header and an unparseable path, across the shared policy or relevant builder variants; keep the existing valid-small-diff compatibility case. Correct the protocol/current-work wording, run affected tests first and then relevant full validation because shared manifest behavior changes, and return the whole diff unstaged, uncommitted and unpushed for re-review. No commit/push GO or next-slice authorization is granted.

- Correction re-review (2026-09-30): **GO for the reviewed tracked-hunk slice only.** Independently verified `main`, `HEAD`, local and live `origin/main` at `882c707252a3ea6da1302b67bbe65eefb36c331b`, zero staged files, 11 modified tracked files plus one tracked deletion and 14 untracked files; the added file is `TrackedDiffShortInputTests.cs`. `ChangeEvidenceManifest.IsExactlyInlinable` now admits only an empty string or a within-budget recognized parse whose every file is supported text/metadata-only and whose input has no binary patch. The new 28 short malformed/unsupported cases cover all seven builder entry points; the seven valid small text plus metadata-only cases preserve the exact historical shape. The executor reports red-before/green-after, fresh full Domain 670/670, Application 1668/1668, Infrastructure 625 passed/3 host skips, API 464/464, Architecture 9/9, unchanged generated client, 117 resolved doc links and clean diff check. I independently rebuilt the solution (0 warnings/errors), passed 164 focused Application tracked/untracked manifest tests, one hosted sealed-replay test, and Architecture 9/9; `git diff --check` is clean and the build changed no tracked/generated file. The corrected protocol and commit-ready current-work entry match the code. The prior NO-GO is closed. No next slice is selected.
- Single publication instruction for this GO: before staging, reverify the same `main` parent, local and live `origin/main`, zero staged files, and exactly 11 modified tracked files, one tracked deletion, and 14 new files, including `current-work.md` and this planner record. Stage precisely those 26 reviewed paths; verify the staged inventory, staged `git diff --check`, and local documentation links. Commit the reviewed substantive slice on `main`, push normally as a fast-forward to `origin/main`, fetch and independently verify the live remote equals that commit, and confirm a clean tree. Against that substantive commit, rebuild and rerun focused tracked-diff parser/selector, all builder variants, real-Git, handler, and hosted restart-replay tests; report exact commands, results, and skips. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and checks actually run; commit and push that documentation-only closure normally, then reverify `HEAD`, local `origin/main`, live remote, and clean tree. Do not embed a commit's own SHA in its own documentation. Any material change after GO, unexpected file, failed check, failed push, or remote divergence stops for Codex re-review/direction; no force-push or history reconciliation. This GO authorizes only this slice's publication.

## Previous selection (2026-09-29): bounded untracked-file context in Agent manifests

- Verified planning baseline: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `87e42a06f2f82f5cfde05939d8e923730bcbe8a1`; staged, unstaged, and untracked state empty. The second planning challenge round was delivered as `38df8aba5837bb168d43c2f4db0f3474134210f1` and factually closed at this baseline. After this planner edit, the executor must expect the same branch and SHA, zero staged files, only `docs/roadmap/planner-handoff.md` modified/unstaged, and zero untracked files. Git and code prevail over this record.
- Objective: complete one larger, provider-neutral Increment 4 context slice: give Agent stages that already consume bounded Git `changeEvidence` an honest, bounded text preview of eligible **untracked new files**. Today `GitWorkspaceEvidenceReader` includes `??` paths and their `git hash-object --no-filters` values in the checkpoint fingerprint, but its `git diff --binary HEAD` text does not include their contents; five stage manifests carry `changedPaths` and a truncated tracked diff, so critical review and implementation review can miss the actual text of newly created files. The official [Git diff](https://git-scm.com/docs/git-diff), [hash-object](https://git-scm.com/docs/git-hash-object), and [ls-files](https://git-scm.com/docs/git-ls-files) contracts support the distinction between tracked diff, untracked paths, and raw-content hashes. The slice must make that omission visible and improve the sealed context given to CriticalReview, ChallengeResolution, Implementation, ImplementationReview (including correction review), and ReviewCorrection without changing their workflow authority.
- Boundaries: extend the existing Git evidence port/result with a small, deterministic, per-file and aggregate bounded representation of untracked text or an explicit omission reason. Select only `??` entries from the same bounded capture; preserve status, diff, checkpoint fingerprint, the run's exact workspace/lease/checkpoint checks, existing 8 KiB tracked-diff preview, 32 KiB manifest ceiling, and each role's existing authoritative structured inputs and untrusted-evidence boundary. Admit a preview only when its opened bytes belong to a regular file physically inside the approved worktree on Windows, are valid text, and match the captured raw-content identity; reject symlink/junction escape, path swaps, binary/invalid UTF-8, and unverifiable identity without reading or sending outside content. A legitimate junctioned worktree root may be resolved against its actual root identity. For non-Windows, do not claim a physical-containment proof the host does not have: omit unsafe previews or stop for design review rather than falling back to lexical containment. Keep deterministic ordering and mark every omitted or shortened preview as incomplete; never present a prefix as a complete file or claim the manifest covers every change. No raw source text in SQLite, API metadata, logs, errors, or browser storage. The only new content destination is the existing sealed manifest/provider stdin, where repository text remains untrusted evidence. Do not add a generic file browser or repository retrieval tool.
- Exclusions: no provider CLI/permission/model/effort change, provider-session resume, account allowance or account threshold/eligibility claim, compaction, generated summary, automatic agent retry, checkpoint/fingerprint redefinition, review approval inference, Git mutation, schema/migration, API route, or frontend control. This slice is evidence assembly, not a new budget or claim policy. Preserve existing outcomes for tracked-only and no-change captures. Consult ADR-0004 and ADR-0009, plus routed adapter, file-security, and integration-test standards.
- Stop gates: return for planner review if the preview cannot be proven to belong to the same content identity used by the fingerprint; if a reparse point or race can cause a read outside the resolved worktree; if the candidate implementation requires changing the fingerprint's historical meaning or a provider invocation contract; if omission cannot be represented truthfully within the manifest ceiling; or if a necessary safe non-Windows path would require a broader platform design. Do not silently drop new files while claiming complete review evidence, and do not bypass a gate by adding `Unknown`-only scaffolding.
- Acceptance evidence: demonstrate a real disposable Git workspace with tracked edits plus one and multiple untracked text files, proving the current omission before the change and the exact bounded content/ordering after it; unchanged fingerprint and tracked diff for the same snapshot; zero files and tracked-only behavior; UTF-8 boundaries and per-file/aggregate limits; binary, oversized, ignored, directory, symlink/junction escape, case-distinct sibling, missing/swap and mid-capture mutation paths; a legitimate redirected root; no external text or sensitive paths in metadata/errors/logs; manifest JSON and untrusted boundary for all five affected stage builders, including both review variants, with explicit truncation/omission and stable replay from the sealed manifest. Use deterministic doubles plus real Git/Windows filesystem integration where the boundary matters; report environmental skips honestly. Run focused and relevant full backend suites, solution build, architecture tests, generated-client stability, frontend tests only if touched, documentation links, and `git diff --check`, with actual commands and results. Update the architecture context/token-efficiency contract and a commit-ready `current-work.md` entry. Return the complete **unstaged, uncommitted, unpushed** diff for Codex GO/NO-GO. This selection grants no commit/push GO; a later GO will carry one publication instruction for the reviewed substantive commit, normal fast-forward push, live-remote verification, and tightly bounded factual documentation closure. Re-review any material post-GO change.

- First uncommitted diff review (2026-09-29): **NO-GO; correct the physical-containment proof in this same slice.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `87e42a06f2f82f5cfde05939d8e923730bcbe8a1`, zero staged files, 23 modified tracked and 9 untracked files. The executor's whole-tree inventory matches. I inspected the reader, port, manifest assembly, claim wiring, tests, and delivery wording; independently passed 34 focused Infrastructure tests with one expected file-symlink skip, including the pre-existing case-sensitive artifact-root test, and 39 Application manifest tests. `git diff --check` is clean. The new `UntrackedFilePreviewReader.ReadOne` compares the opened file's resolved-root prefix with `StringComparison.OrdinalIgnoreCase` at line 105. On a case-sensitive parent, `Artifacts` and `ARTIFACTS` can be separate directories. A junction under `Artifacts/link` can target `ARTIFACTS/link`; opening `link/file.txt` resolves to the sibling, yet the case-insensitive prefix and exact remaining `link/file.txt` both pass. With the matching blob hash, this reader would admit text from outside the approved root; whether Git's current untracked traversal supplies that exact path is separate and does not establish the claimed physical-containment guarantee. The existing `FilesystemArtifactStore` explicitly uses `Ordinal` and has a real case-distinct-sibling fixture; the new test checks only a case-distinct file spelling inside one root and does not cover this root-prefix bypass. Fix the comparison to fail closed against the OS-resolved root, add a real Windows case-sensitive-directory regression with a sibling containing the same relative suffix and a correct content hash, prove red before/green after, and confirm an ordinary file and a legitimately junctioned root still preview. Update architecture and handoff wording only as needed. The executor also reported not reading ADR-0004, ADR-0009, or routed standards before editing, contrary to AGENTS.md and the selected prompt; read them now and report any affected design correction, without expanding scope. Run affected tests first, then relevant full validation because this is a sensitive file boundary; report exact reruns, skips, links, and diff check. Keep the whole diff unstaged, uncommitted, and unpushed for re-review. No commit/push GO or next-slice authorization is granted.

- Correction re-review (2026-09-29): **GO for this reviewed slice, subject only to the bounded factual `current-work.md` correction and standards check in the publication instruction below.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `87e42a06f2f82f5cfde05939d8e923730bcbe8a1`, zero staged files, the same 23 modified tracked and 9 untracked files, and clean `git diff --check`. `UntrackedFilePreviewReader` now compares the OS-resolved root prefix with `Ordinal`; the new real Windows case-sensitive-directory fixture uses distinct root/sibling directories, a junction preserving the relative suffix, and the correct outside blob hash. It failed before the correction and now refuses without outside text or size, while ordinary contained text and a junctioned root still pass. I independently built the solution (0 errors/0 warnings) and passed focused Infrastructure 34/34 with one expected file-symlink skip, Application manifest 39/39, and hosted API supervisor 16/16; the generated client remains byte-identical (`b3e1c836…`). The executor reports fresh full Domain 670/670, Application 1543/1543, Infrastructure 625 passed/3 host-capability skips, API 463/463, Architecture 9/9, local links 112/112, and no frontend change. The three skips and other bounded residual limits are stated in `current-work.md`. The code and acceptance evidence close the previous NO-GO. Two older facts in that delivery entry still need a narrow correction: Infrastructure's earlier `624 passed, 3 skipped` must say `625 passed, 3 skipped`, and its new Infrastructure-test count `(34; 33 ran)` must say `(35; 34 ran)` and include the case-distinct-sibling regression. No other behavior correction is authorized by this GO.
- Single publication instruction: before staging, finish reading the applicable routed standards completely (the executor reported only sections of some long standards); if that reveals any necessary material code or architecture change, stop for Codex re-review. Make only the two factual count updates and the new regression mention in the already-modified `current-work.md`, with no code, test, wire, or schema change. Reverify the same baseline and exact 23-modified/9-new inventory, then stage precisely those 32 files, including `current-work.md` and this planner record. Check the staged file list, staged `git diff --check`, and local documentation links. Commit the reviewed substantive diff on `main`, push normally as a fast-forward to `origin/main`, fetch and independently verify the live remote equals that commit, and confirm a clean tree. Against that substantive commit, rebuild and rerun focused untracked-preview reader, manifest, handler, and hosted sealed-manifest tests; report exact commands/results and any skip. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and post-publication checks actually run; commit and push that documentation-only closure normally, reverify local/live `origin/main`, `HEAD`, and a clean tree. No commit embeds its own SHA in its own documentation. A material post-GO change, unexpected file, failing check, failed push, or remote divergence stops for Codex review/direction; no force-push or history reconciliation. This GO authorizes only this slice's publication and selects no next slice.

## Previous selection (2026-09-29): bounded second planning challenge round and escalation

- Verified before selection: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `f22325000682caf1d3bd8c6b6384807e03199e79`; staged, unstaged, and untracked state empty. The provider token-activity stop was published as `210f4e8699dad670aadf0816ea99f9b85ff8627f` and factually closed by `f22325000682caf1d3bd8c6b6384807e03199e79`. After this planner-only edit, the executor's expected preflight is `main` at that exact SHA, nothing staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, and nothing untracked. Git and code prevail if this changes.
- Objective: complete one **optional second planning critique and resolution round** for the same proposal lineage, using the existing durable Proposal → Challenge → Decision → revised Proposal links. Today the cockpit selects only Planner Proposals and `CreateClaudeCriticalReviewAttempt` accepts only a Planner-owned Proposal, although the protocol says a Resolver's revised Proposal may re-enter critical review. Extend the request, claim, supervisor/replay, result, implementation-eligibility, and cockpit paths together. This is a larger end-to-end Increment 4 slice, not automatic orchestration or a provider feature.
- Lineage and bound: a root is a valid provider-observed Codex Planner Proposal. Its first valid Codex Resolver revised Proposal is depth one and may receive one explicit Claude critical review. If that review is Challenged, one explicit Codex Resolver attempt may resolve its complete ordered challenge set and produce a depth-two revised Proposal. This is the second and final challenge-resolution round for that lineage: the successful second resolution atomically records one bounded, orchestrator-authored, durable human escalation linked to that lineage and its resolved challenge evidence. A depth-two Proposal is not reviewable or implementable through this lineage; there is no third review, silent reset, automatic claim, or override. A genuinely new Planner root remains a separate explicit planning request. Count rounds by validated reply/attempt identity, never by guessing whether natural-language challenges describe the same issue; document that the lineage cap is conservative relative to the roadmap's per-material-issue wording. Failed/invalid attempts add no successful revision but still consume existing global claim/time budgets.
- Preserve the current direct path: a first revised Proposal that has not been re-reviewed remains eligible for implementation under its existing complete Decision evidence. If its optional second review is Accepted, implementation must bind to that exact successful acceptance as well as the revision's Decision evidence. If that review is Challenged, implementation of that Proposal must refuse even though the earlier resolution succeeded; while its second resolution is pending or failed, do not fall back to the previous Proposal. An exhausted depth-two Proposal and its escalation cannot be treated as implementation approval. Keep the original accepted Planner Proposal path intact. Server-side checks own eligibility; cockpit affordances are only hints.
- Integrity boundary: derive the lineage from same-run, provider-observed messages, exact owning Agent attempts, roles/contracts/providers, ordered attempt inputs, reply links, workspace/checkpoint/fingerprint, and completed outcomes. Fail closed on missing, corrupt, foreign-run, cyclic, stale, duplicated, or incoherent evidence without echoing provider text or stored exceptions. Recheck at claim/dispatch and at the durable result/implementation boundary where races can change the decision; no sealed manifest leak or partially recorded Decision/Proposal/escalation on refusal. Preserve one Running attempt per run, exact sealed manifest replay, all six Agent-claim budget and token-stop gates, and the existing provider argument/permission contracts. Keep escalation content to bounded identifiers, counts, and safe summaries; never copy raw artifact text or credentials into metadata.
- Exclusions: no automatic debate loop, generic workflow engine, new provider flag, model/effort inference, provider-session resume, Claude account allowance, account-usage threshold or eligibility inference, token cap semantics, new human override/authorization, implementation of an escalated plan, or third challenge round. The [official Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference) is relevant only to preserving the current fixed invocation; its session commands do not establish a safe DevalCopilot resume or allowance contract. Do not add Unknown-only scaffolding or silently reinterpret [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md) or [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md).
- Stop gates: return to the planner without broadening if the existing durable reply/attempt identity cannot distinguish a first revision from a second one safely; if a second challenged review cannot be fully resolved before escalation while preserving ADR-0004; if implementation can race a newly successful challenged review or exhausted-lineage escalation without an atomic/guarded refusal; if replay could dispatch a now-ineligible claim; or if the only implementation would fabricate provider capability or grant implicit human approval. A material reversal of an accepted ADR needs a proposed superseding ADR and planner re-review, not an implementation assumption.
- Acceptance evidence: prove root acceptance and existing direct first-revision implementation remain valid; first-revision review Accepted and Challenged paths, complete second-resolution Decision set, depth-two escalation once and no third review/implementation; exact lineage/ownership/checkpoint/corruption checks; duplicate and concurrent claims/results; no external work or orphaned seal on refusal; restart/replay uses only the claimed manifest and does not revive an exhausted lineage; cockpit selection, pending/error, run-switch and stale-response behavior. Include mutation or equivalent red-before/green-after evidence for the previously missing revised-Proposal review and for the challenged-revision implementation gate. Run focused and relevant full backend/frontend validation, solution build, generated-client stability, typecheck/lint/build, local documentation links, and `git diff --check`; report actual commands, results, and residual limits. Update the architecture and cockpit contracts plus a commit-ready `current-work.md` entry. Return an **unstaged, uncommitted, unpushed** diff for Codex GO/NO-GO. This selection grants no commit/push GO.

- First uncommitted diff review (2026-09-29): **NO-GO; correct this same slice without staging, committing, or pushing.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `f22325000682caf1d3bd8c6b6384807e03199e79`, zero staged files, **18 modified tracked and 12 untracked** files. Reconcile that exact `git status --short -uall` inventory with the executor report's 17 modified/13 new. I independently built and ran the 49 focused Application lineage/claim/result tests, all passing; `git diff --check` is clean. The reported full suites and mutation checks are useful, but two selected integrity requirements are not met:
  1. `PlanningLineage` delegates owner validation to `ImplementerExecutionReportEligibility.ResolveSnapshotOwningAttempt`, which requires a defined provider and matching message actor but never checks that the role/provider pair is one DevalCopilot actually launches. A persisted Planner/Claude Code, Resolver/Claude Code, or CriticalReviewer/Codex attempt with a matching forged actor can therefore be accepted as provider-observed lineage; `AgentAttemptIdentity.IsCoherent` already defines the stricter supported-pair rule. Require a coherent supported pair and defined role/contract before any root, review, challenge, Decision, or revision can authorize the next step. Prove with SQLite-persisted pair/actor corruption through a representative review or resolution claim and implementation, including no external work, no sealed manifest, and fixed non-echoing refusal. Preserve the current valid pair paths and role-first policy.
  2. The new review claim calls `ImplementerExecutionReportEligibility.LoadSnapshotAsync`, which fully materializes **every** Attempt in the run through EF string-enum converters. A persisted unparseable or undefined enum string on any one row can throw before `PlanningLineage` can fail closed; the implementation and result paths also use this snapshot. The same codebase already documents this EF boundary in `AgentAttemptRead` and classifies invalid persisted strings in the token-stop projection. Read/guard the lineage evidence so corrupt or unreadable persisted attempt identity produces a fixed refusal without exception or stored-string disclosure. Test a corrupt participating row and an unrelated corrupt row beside a healthy lineage through the affected claim/result paths. Database and cancellation failures must not be mistaken for bad evidence. Do not broaden this into a rewrite of unrelated consumers of the shared snapshot.
  Run affected tests first, then relevant full validation because these corrections change the authority boundary. Update `current-work.md` to report the corrections and checks actually run, verify documentation links and `git diff --check`, reconcile the changed-file inventory, and return the entire corrected uncommitted diff for re-review. No commit/push GO or next-slice permission is granted.

- Corrected uncommitted diff re-review (2026-09-29): **GO for this reviewed slice, subject only to the factual wording correction in the single publication instruction below.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `f22325000682caf1d3bd8c6b6384807e03199e79`, zero staged, 20 modified tracked and 13 untracked files; the earlier 17/13 inventory was a counting slip, and the correction added two modified test files and one new test file to the verified 18/12 baseline. `PlanningLineage` now requires `AgentAttemptIdentity.IsCoherent` for its owning Planner, Resolver, and CriticalReviewer attempts, including accepted-original and result boundaries. Its guarded snapshot read returns a fixed refusal for unmaterializable persisted rows, including unrelated rows in the same run, without echoing stored strings. The 17 new SQLite integrity cases and the revised pair tests cover the corrected paths. I independently rebuilt and ran 66 focused Application lineage, claim, result, and integrity tests and three hosted API supervisor tests, all passing. The executor reports full Domain 670/670, Application 1504/1504, Infrastructure 591 passed/2 expected skips, API 462/462, Architecture 9/9, frontend 856/856, clean solution build/typecheck/production build, unchanged generated client, 136 resolved documentation links, and lint warnings only in unchanged hooks. `git diff --check` is clean. No further behavioral correction is required on this reviewed diff.
- Single publication instruction after this GO: before staging, correct only the absolute wording in the already-modified `PlanningLineage.cs`, `CreateChallengeResolutionAttemptCommandHandler.cs`, `CreateImplementationAttemptCommandHandler.cs`, `agent-collaboration-protocol.md`, and `current-work.md` that says database failures are not caught. State precisely that the guard catches `InvalidOperationException`, which cannot prove an enum-conversion cause and could have another origin; `DbException` and cancellation exceptions are not caught by that guard. Do not change behavior, tests, wire contract, migration, or other documentation beyond these factual edits and this GO record. Verify the staged set is exactly the reviewed 20 modified plus 13 new files, including `current-work.md` and this planner record; run staged `git diff --check` and local documentation-link checks. Commit the reviewed substantive slice on `main`, push it normally as a fast-forward to `origin/main`, and independently fetch and verify the live remote points to its exact SHA. Against that substantive commit, build and rerun focused lineage/integrity, review/resolution/implementation claim, result/escalation, hosted-supervisor, and frontend lineage/cockpit tests; report exact commands/results and clean tree. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and post-publication checks actually run; commit and push that documentation-only closure normally, then verify the live remote and clean tree again. No commit contains its own SHA in its documentation. Any material post-GO change, unexpected file, recurring build/test failure, push failure, or remote divergence stops for Codex re-review; do not force-push or reconcile history. The named factual edits need no separate GO. This GO authorizes only this slice's publication and selects no next slice.

## Previous selection (2026-09-29): run-scoped provider token-activity stop at Agent claim

- Verified before selection: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`; staged, unstaged, and untracked state empty. The previous guidance slice was published as `725476e54d9f7bcf437f3fc920bd375dae1fed67`, closed factually in `current-work.md` by `797a88872335ddbfafdc0d4888cc41a7cf682796`; the one-line planner-owned ADR link repair is `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`. After this planner-only edit, the expected executor preflight is the same `main` and SHA, nothing staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, nothing untracked. Git and code prevail if this changes.
- Objective: add an owner-configured, **run-scoped, provider-separated stop on locally recorded token activity** for the next Agent claim. It uses the existing versioned, persisted per-attempt usage contracts and the same provider-specific token-count formulas as today's advisory warning, but has its own durable stop thresholds and enforceable claim gate. With no stop threshold configured, current claim behavior is unchanged. At or above a configured threshold, no *new claim* for that provider may commit; a claim already committed may dispatch/finish. This is a local, retrospective claim guardrail, **not** a provider account allowance, per-attempt output cap, token reservation, cost/rate limit, or guarantee that an in-flight invocation cannot exceed the threshold. It advances Increment 4's token controls without claiming its account-allowance exit criterion.
- Boundary: two nullable `Run` stop thresholds (Codex and Claude Code), default/null and historical/null, validated as positive and bounded; additive migration with no invented backfill. A protected set/clear operation persists each change with its event, allows only editable run lifecycles, and is concurrency-safe. Reuse the current warning formula's proven provider/schema attribution, never its advisory state as an eligibility signal: Codex input+output with cached input already included; Claude input+cache creation+cache read+output, requiring both cache counts. Only dispatched, concluded Agent attempts contribute. Undispatched attempts consume no tokens; no dispatched history permits the first claim. Any missing/malformed/unsupported usage, unattributed dispatched Agent attempt, pending evidence, or arithmetic overflow makes a configured candidate provider's under-threshold status unprovable and blocks it with a distinct safe evidence error. A known count at/above threshold blocks with a distinct reached error even if other evidence is missing. One provider's known usage does not consume the other's stop threshold. Do not infer zero or completeness from absence.
- Enforce in all six Agent-claim paths, for each path's fixed provider, before provider availability, Git work, manifest sealing, or review-correction authorization consumption. Preserve the existing run-wide count and reserved-time budgets and their failure precedence. Revalidate/guard the configured stop policy at the durable claim boundary so a concurrent threshold change cannot commit a claim against a stale policy: the policy compare and Attempt/artifact/input writes must be one database-atomic operation, including the three Codex paths with explicit transactions and the three Claude paths. Classify races truthfully, delete orphaned sealed manifests on refused/rolled-back claims, and leave human authorization unconsumed. A threshold changed after a claim commits is prospective; it does not revoke that claimed attempt. Add a separate cockpit stop projection/control showing configured threshold, known local count, evidence state, and exact blocking reason; refresh after set/clear. Do not combine or rename the existing advisory warning or provider account-allowance observation. The UI may withhold a certainly blocked action but must not assert eligibility from a below-threshold local count.
- Exclusions: no CLI/provider argument or parser change, real provider call, account-allowance stop/warning policy, Claude account allowance, provider-session resume, context-window/compaction, generic budget framework, automatic cancellation/fallback, or threshold override. Do not turn Codex's account-allowance observation into a threshold or invocation decision. The official [Claude prompt-cache token breakdown](https://platform.claude.com/docs/en/build-with-claude/prompt-caching) is consistent with the current local formula, but an API document does not itself prove a Claude Code CLI response; the existing versioned local parser remains the authority for this slice. The current [Codex CLI reference](https://learn.chatgpt.com/docs/developer-commands) documents `codex exec --json`, but no new CLI capability is inferred or requested here.
- Stop gates: report to the planner without broadening if the existing persisted provider/schema evidence cannot support a conservative claim decision; if any of the six claims cannot atomically guard the policy and its own durable insert against a threshold write; if the new control would have to claim account-allowance eligibility, a provider-enforced per-attempt cap, or a verified below-threshold state from incomplete evidence; or if a material accepted ADR must be reversed rather than supplemented. No Unknown-only scaffolding.
- Acceptance evidence: set/clear/auth/validation/lifecycle/concurrency and migration tests; exact count formulas, zero/no-history distinction, equality, overflow, running/undispatched/malformed/unsupported/unattributed evidence, provider separation and unchanged advisory warning; all six claim paths blocked before external work at reached/indeterminate states and unaffected when unconfigured; threshold-change races at the actual claim commit, slot/time-budget precedence, no orphan manifest or consumed authorization; cockpit refresh, stale-run and error states; restart/replay from persisted state without a provider call. Run focused and relevant full backend/frontend validation, solution build, NSwag stability, typecheck/lint/build, local documentation links, and `git diff --check`; report exact commands/results and residual limits. Update architecture/cockpit documentation and make `current-work.md` commit-ready. Return an **uncommitted, unpushed, unstaged** diff for Codex GO/NO-GO. This selection grants no commit/push GO.

- Uncommitted diff review (2026-09-29): **NO-GO; keep this slice unstaged, uncommitted, and unpushed.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`, with zero staged files, 32 modified tracked files, and **33** untracked files (the executor report says 26; reconcile the inventory on return). The reported full-suite/build/client/link checks are useful evidence, and `git diff --check` shows only the two known generated-file CRLF notices. The stop read is not yet fail-closed for persisted enum corruption: `AgentTokenStopGate.CheckClaimAsync` and `GetRunCockpitQueryHandler` project `Attempt.Status` and `AgentProvider` through EF's string-enum converter before `AgentTokenStopAccumulator` can classify a gap. An unrecognized stored string can throw during materialization instead of yielding the fixed indeterminate refusal/projection; a defined provider on a role/provider-incoherent row can also be wrongly attributed, and an undefined numeric status must never be treated as concluded merely because it is not `Running`. This repository already handles unreadable persisted enum strings in `AgentAttemptRead` and tests them in `AgentAttemptHistoryEndpointTests`. Correct this narrowly at the stop evidence boundary, without trusting an unreadable or incoherent row as complete evidence or returning its stored text/exception; prove with SQLite-persisted corruption through the gate, a representative claim before external work, and the cockpit, including a healthy row on the same run. Keep provider separation only when attribution is sound. Also make the present-tense `mvp-delivery-plan.md` sentence that all remaining token controls are deferred factually consistent with this selected stop while leaving account-allowance exit criteria open. Reconcile the complete changed-file list, update `current-work.md` to describe the correction and checks actually run, run affected checks first and then relevant full validation, and return the corrected uncommitted diff for re-review. No commit/push GO is granted.

- Corrected uncommitted diff re-review (2026-09-29): **GO for this reviewed slice, subject only to the factual wording edits named in the publication instruction below.** Independently verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at `bc78068cf0f7d7cf2c6e3f5a931f0cd1ea7064c0`; zero staged, 33 modified tracked files, 34 untracked files. The earlier 26-versus-33 discrepancy was the collapsed-directory form of `git status --short`; `-uall` accounts for every file. `PersistedAgentAttemptStopEvidence` now compares persisted enum columns to known names in the database, treating unreadable status/provider/role and incoherent role/provider pairs as unattributed gaps for both providers; sound rows retain provider separation and known-count precedence. The cockpit uses the same stop classification and omits an unreadable latest-attempt card rather than failing the whole projection. The new SQLite regression tests were reported red before the fix (16 of 27) and green after; I independently rebuilt and ran 54 focused Application gate, cockpit, and representative-claim tests, all passing. The executor reports full Domain 670/670, Application 1438/1438, Infrastructure 591 passed/2 expected skips, Api 459/459, Architecture 9/9, frontend 825/825, clean typecheck/build/lint, stable generated client, and 143 resolved links. `git diff --check` has no whitespace errors, only the known migration-snapshot and generated-client CRLF notices. No further behavioral correction is required on this reviewed diff.
- Single publication instruction after this GO: before staging, make only these factual wording corrections in already-modified files: narrow the claim in `SetTokenStopThresholdCommandHandler`'s XML comment and the architecture stop section that **any** Run change after the read causes a concurrency conflict; EF checks changes to configured concurrency-token values, including lifecycle and the two stop thresholds, not arbitrary Run columns or every claim-time write. In `current-work.md`, state that `git diff --check` emits **two** known CRLF notices, for the migration snapshot and generated client, rather than only the generated-client notice. Do not change behavior, tests, wire contract, migration, or any other documentation beyond these corrections and this GO record. Verify the staged set is exactly the reviewed 33 modified plus 34 new files, including `current-work.md` and this planner record; run staged `git diff --check` and local documentation-link checks. Commit the reviewed substantive slice on `main`, push normally as a fast-forward to `origin/main`, and independently fetch and verify the live remote points to its exact SHA. Against that substantive commit, build and rerun focused stop evidence/gate, six-claim, endpoint/hosted replay, migration, and frontend stop tests; report exact commands/results and clean tree. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and post-publication checks actually run; commit and push that documentation-only closure normally, then verify live remote and clean tree again. No commit contains its own SHA in its documentation. Any material post-GO change, unexpected file, recurring build/test failure, push failure, or remote divergence stops for Codex re-review; do not force-push or reconcile history. The named wording edits need no separate GO. This GO authorizes only this slice's publication and does not select another slice.

## Previous selection (2026-09-29): bounded human guidance for one authorized review correction

- Corrected uncommitted diff re-review (2026-09-29): **GO for the reviewed substantive slice, with only the narrow factual wording correction below before staging.** Independently verified branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all `2b12bae263e9d9eff0c18157e8dd0c5aaa37863a`; zero staged files, 18 modified tracked files, and 12 untracked files. The three NO-GO findings are resolved: the reserved bodyless rationale is rejected as guided input; stored rationale must match the exact canonical, bounded, safe representation; and retry and claim share full instruction/escalation-envelope validation. The new 20-kind corruption theory exercises both paths and refuses claim before sealing. I independently built the solution with `--no-restore -p:UseSharedCompilation=false` (0 warnings/errors) and ran focused Application 82/82, Domain 32/32, and API 21/21. An initial test command with restore could not read the sandbox-inaccessible user NuGet.Config; the no-restore build and focused tests succeeded. The executor reports fresh full Domain 657/657, Application 1269/1269, Infrastructure 589 passed/2 expected skips, API 440/440, Architecture 9/9, frontend 795/795 plus typecheck/build/lint, stable generated client, and resolved local documentation links. `git diff --check` reports only the known generated-client CRLF notice. Remaining risks: guidance is unredacted human text in the ledger and sealed manifest; the lexical screen is best-effort; the provider may not honor the advisory boundary; and the claim race is tested at a deterministic sealed-artifact seam.
- Single publication instruction after this GO: before staging, correct only the factual statements that `ReviewCorrectionGuidance.Normalize` "runs once" or normalizes "exactly once" in the new Domain/Application XML or inline comments and the new architecture/current-work sections. The validator and handler both call it; describe deterministic validation before persistence instead. Do not change behavior, tests, wire contract, or any other documentation beyond this GO record. Verify the staged set is exactly the reviewed 18 modified plus 12 new files, including `current-work.md` and this planner record; run staged `git diff --check` and local handoff-link checks. Commit the reviewed substantive slice on `main`, push it normally as a fast-forward to `origin/main`, and independently fetch and verify the live remote points to its exact SHA. Against that substantive commit, rerun focused guidance Domain, correction claim, guided endpoint, hosted replay, and frontend guidance/cockpit tests; report exact commands/results and tree state. Then make only a tightly bounded factual `current-work.md` closure recording the substantive SHA, verified publication, and post-publication checks actually run; commit and push that documentation-only closure normally, then verify live remote and clean tree again. No commit contains its own SHA in its documentation. Any material post-GO change, unexpected file, recurring build/test failure, push failure, or remote divergence stops for Codex re-review; do not force-push or reconcile history. The wording edit named here needs no separate GO. This GO authorizes only this slice's publication and does not select another slice.

- First uncommitted diff review (2026-09-29): **NO-GO; correct this same slice, keep it uncommitted and unpushed.** Independently verified `main`, `HEAD`, local and live `origin/main` at `2b12bae263e9d9eff0c18157e8dd0c5aaa37863a`, zero staged files, 18 modified tracked files (including this planner record and `current-work.md`), and 11 untracked files. The reported full-suite/build/client results are useful evidence, but these contract failures remain:
  1. `ReviewCorrectionGuidance.Normalize` accepts the exact `DefaultRationale` string as guided input, while `IsGuidance` classifies that same persisted value as no guidance. A successful guided authorization can therefore consume a claim whose sealed manifest omits the accepted text and message id. Make the guided and bodyless representations unambiguous (for example, explicitly reject the reserved value before persistence, or use another safe discriminator without changing historical bodyless bytes); test the exact collision through authorization and claim.
  2. `TryReadRationale` checks only JSON shape, the fixed instruction, and nonblank text. It does not reapply the protocol's field bound/safety checks or prove that a guided persisted value is canonical. A corrupted persisted rationale can be carried into the sealed provider manifest despite being ineligible at submission. Fail closed on overlong, unsafe, control-character, invalid-Unicode, noncanonical, or otherwise invalid stored content without echoing it; test both retry and claim behavior, with no authorization consumption or sealed artifact on claim refusal.
  3. The idempotent-retry path in `GuidanceMismatchAsync` reads only same-run, `HumanInstruction` type and JSON, so it can report success for an authorization whose linked message has wrong provenance, participants, protocol, attempt, or escalation reply. The claim resolver also checks the escalation message's run/type/provenance but not its expected actor, recipient, protocol and attemptless shape. Validate the full same-run, same-escalation chain on retry and claim before accepting or sealing; test coherent-looking text with corrupted linkage/envelope, not only malformed JSON or wrong reply.
  Rerun affected Domain/Application/API/frontend checks first. Since the persistence-to-manifest authority boundary changes, rerun the relevant full validation before presenting the corrected diff; verify generated-client stability if wire contracts change, documentation claims/links, and `git diff --check`. Report exact reruns and remaining risk. No publication GO or next-slice permission is granted.

- Verified before selection: branch `main`; `HEAD`, local `origin/main`, and live `origin/main` all equal `2b12bae263e9d9eff0c18157e8dd0c5aaa37863a`. Staged, unstaged, and untracked state were empty. The previous repair slice was published as `2872224f2271b4d8dde284c7c1fe5fc603664c47`, followed by its factual `current-work.md` closure at this baseline. After this planner-only edit, the expected executor preflight is the same branch and SHA, nothing staged, only `docs/roadmap/planner-handoff.md` modified/unstaged, and nothing untracked. Git and code prevail if this changes.
- Objective: extend the **existing, exhausted review-correction escalation** so its one explicit human authorization can optionally carry short guidance clarifying the already-recorded findings. Persist the exact accepted guidance with the linked `HumanInstruction`, make it visible as human-submitted context, and include it once in the sealed manifest of the correction attempt that consumes that authorization. This connects a real control and claim path end to end, rather than adding an unattached instruction surface. The current bodyless authorization remains valid and retains its fixed behavior.
- Boundaries: use a protected, explicit request contract for guidance while retaining the existing bodyless endpoint and idempotent no-guidance behavior. Enforce a small server-side length/content bound before persistence, use the existing structured `HumanInstruction` protocol and its escalation link, and never claim that lexical checks guarantee absence of secrets. A repeated authorization with identical intent may be idempotent; a different guidance value racing with or following an existing unconsumed authorization must return a safe conflict, never silently report that the different guidance was accepted. Resolve the consuming authorization's same-run, same-escalation, coherent HumanInstruction before sealing the manifest; bind its exact accepted text and message id to the manifest under a clearly marked human-submitted advisory boundary. Recheck the relationship at the durable claim boundary where needed, preserve atomic consumption and orphan-manifest cleanup, and make restart replay use only the attempt's sealed snapshot. Show the optional guidance entry only for the actionable escalation in the cockpit, with safe pending/error behavior and no browser/URL persistence. The server remains authoritative for eligibility.
- Contract preservation: [ADR-0010](../decisions/0010-add-review-correction-response-contract.md)'s ordered correction inputs remain exactly the ExecutionReport then ReviewFindings; guidance is separate context, not another `AttemptInputMessage` or a finding. The human text cannot override the run objective, findings, role/effect, workspace, permission/tool restrictions, Git/verification/network prohibitions, claim count, reserved-time budget, or provider assignment. Keep the fixed host instruction and untrusted-evidence boundary in the manifest. No new provider flag, model inference, account-allowance enforcement, session resume, generic human instruction framework, automatic retry, or provider fallback. The [current Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference) supplies no reason to change this slice's invocation contract; do not infer capabilities from `--help` or defaults.
- Stop gates: stop and report to the planner if the existing HumanInstruction schema cannot safely represent both the authorization and optional bounded guidance without protocol/ADR reversal; if a concurrent or corrupted linkage cannot be handled without silently accepting different guidance, consuming the wrong message, or leaking its text in errors; if the guidance would require provider/permission changes or a new authority path; or if budget, sealed-artifact, and replay invariants cannot be preserved. Consult accepted ADRs and the routed engineering/security standards before implementation. Do not broaden into a generic instruction system to bypass a gate.
- Acceptance evidence: tests for old bodyless authorization, guided authorization and exact persisted value, validation/authorization/errors, duplicate and conflicting concurrent submissions, stale/foreign/corrupt message links, global-budget refusal, one-time claim consumption, claim/authorization races with orphan cleanup, exact bounded manifest content and precedence, restart replay from the sealed snapshot, and cockpit entry/reset/error flows. Show that metadata and errors do not echo guidance unexpectedly, while the intentionally visible human message and sealed text remain truthful about possible sensitive content. Run affected backend/frontend tests plus relevant full suites, build/typecheck/lint, generated-client stability, documentation links, and `git diff --check`; report exact commands and results. Update architecture and cockpit contracts and make `current-work.md` commit-ready. Return an uncommitted, unpushed diff for Codex GO/NO-GO; selecting this slice grants no commit/push GO.

## Previous selection (2026-09-29): one manual Codex Planner format-repair attempt

- Corrected uncommitted diff re-review (2026-09-29): **GO for the reviewed
  substantive slice, with the exact factual wording edit below before
  staging.** Independently verified `main`, `HEAD`, local and live
  `origin/main` at `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`, zero
  staged files, 24 modified tracked files and 14 untracked files. The prior
  NO-GO findings are corrected: source workspace/checkpoint/fingerprint are
  compared at request and claim boundaries, a persisted `Completed` plus
  `InvalidStructuredOutput` row is refused, and the migrated SQLite FK is
  `NO ACTION`. The actual migration test proves individual source deletion
  fails while deleting its run cascades through source and repair. I built
  the solution independently (0 warnings, 0 errors) and ran focused
  Infrastructure migration tests 6/6, Application planning-claim tests
  68/68, and API repair-endpoint tests 7/7. An initial filter based on the
  Application test file name matched no tests; the corrected partial-class
  filter ran all 68. `git diff --check` reports only the two known generated
  file CRLF notices. The executor reports fresh complete Domain 625/625,
  Application 1221/1221, Infrastructure 589 passed/2 expected skips, API
  428/428, Architecture 9/9, frontend 776/776 plus typecheck/build/lint,
  stable generated client, and resolved local documentation links; those
  results are applicable to this reviewed diff.
- Single publication instruction after this GO: before staging, correct
  only two factual descriptions of the nullable source attempt number:
  in `AgentAttemptStatusQueryResult.cs` XML comments and the new
  `current-work.md` entry, replace the claim that it becomes null when the
  source row is "unreadable" with the actual scalar-query behavior: the
  repair source id remains present, while its number is null when no
  same-run source row resolves. A malformed enum in an existing source row
  does not by itself make that projected number null. Change no behavior,
  test, wire field, migration, or other documentation beyond this planner
  GO record. Check the exact staged set is the reviewed 24 modified plus
  14 new files, including `current-work.md` and this planner record; run
  `git diff --check` and local handoff-link checks. Commit the reviewed
  substantive slice on `main`, push it normally as a fast-forward to
  `origin/main`, and independently fetch and verify the live remote points
  to its exact SHA. Against that substantive commit, rerun focused repair
  claim, migration/constraint/cascade, API endpoint, hosted-supervisor
  replay, and frontend repair-action tests; report exact commands/results
  and tree state. Then make only a tightly bounded factual
  `current-work.md` closure recording the delivered substantive SHA,
  verified publication, and post-publication checks actually run; commit
  and push that documentation-only closure normally, and verify live remote
  and clean tree again. No commit contains its own SHA in its documentation.
  Any material post-GO change, unexpected staged/worktree file, recurring
  build or test failure, push failure, or remote divergence stops for Codex
  review; do not force-push or reconcile history. The two narrow factual
  edits named here need no separate GO. This GO authorizes only publication
  of this reviewed slice; it does not select another slice.

- First uncommitted diff review (2026-09-29): **NO-GO; correct this same
  slice, keep it uncommitted and unpushed.** Independently verified branch
  `main`, `HEAD`, local and live `origin/main` at
  `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`, zero staged files,
  24 modified tracked files (including this planner record and
  `current-work.md`), and 14 untracked files. The reported full suites and
  generated-client stability are evidence, but the current diff has these
  contract gaps:
  1. `PlanningRepairSource.EvaluateAsync` checks the source's run, role,
     outcome, latest Agent position, and uniqueness but never compares its
     immutable `AgentGitWorkspaceId`, `AgentGitCheckpointId`, and fingerprint
     with the workspace/checkpoint selected for the repair. A newer valid
     checkpoint can therefore be used for a "repair" of an older failed
     response even with a fresh matching Git fingerprint. Enforce exact
     source-to-repair workspace/checkpoint/fingerprint identity at both the
     request and durable claim checks; add a case with an old source, a
     newer valid checkpoint, and fresh evidence matching that newer
     checkpoint. It must refuse without claiming or leaking a manifest.
  2. The new self-reference uses SQLite `ON DELETE RESTRICT`, while the
     existing Attempt-to-Run FK uses `ON DELETE CASCADE` and the mapping
     comment says both source and repair go with their run. With a source
     and linked repair present, `DELETE FROM runs` fails with `FOREIGN KEY
     constraint failed`: reproduced independently in SQLite with this exact
     FK shape. Preserve protection against deleting a referenced source
     individually while allowing the existing run cascade; a `NO ACTION`
     self-FK is one candidate, subject to an actual migrated-database
     integration test proving both operations. Update mapping, migration,
     snapshot, and claims together.
  3. `Attempt.IsEligiblePlanningRepairSource` accepts `Status = Completed`
     alongside `InvalidStructuredOutput`, though `CompleteAgent` records
     this outcome as `Failed`. Require the coherent persisted state, and
     test that a deliberately inconsistent status/outcome row is refused.
     The selection's use of "completed" meant a finished attempt, not the
     `AttemptStatus.Completed` enum value; the correct source status is
     `Failed`.
  4. Make `current-work.md` commit-ready by removing its temporary
     "uncommitted and unpushed until reviewed" sentence and update its
     behavior/risk wording for the corrections. The query-result XML comment
     also says `RepairSourceAttemptId` becomes null if the source row is
     unreadable, while the projection retains the id and only the number
     becomes null; align that comment with the actual wire behavior.
  Rerun affected tests first, then the relevant full backend/frontend
  validation because source eligibility and persistence behavior change;
  verify migration round-trip/index/FK/cascade behavior, generated-client
  stability if the wire changes, documentation links, and `git diff --check`.
  Report exact reruns and any remaining risk with the corrected uncommitted
  diff. No publication GO and no next slice are granted.

- Verified baseline before this planner-only edit: branch `main`; `HEAD`, local
  `origin/main`, and live `origin/main` all equal
  `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`; staged, unstaged, and
  untracked state empty. The Agent-attempt history slice was published as
  `998c05f98558cfccc849cf2845b3348d97584b3d`, followed by the factual
  `current-work.md` closure at this baseline. A sandboxed repeat of
  `git ls-remote` could not connect; an unsandboxed repeat returned this exact
  live SHA. Git and code remain authoritative if this changes.
- Objective: implement the optional, one-bounded-format-repair provision of
  [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md)
  for the **Codex Planner Proposal contract only**. Today a cleanly exited
  Planner attempt with `InvalidStructuredOutput` preserves its sealed response
  but appends no Proposal; the cockpit can request a fresh plan, with no durable
  connection to that failure or one-repair limit. A human-requested repair
  should make one new, explicitly linked, read-only Planner invocation with a
  fixed host-authored format reminder and the current bounded planning context.
  It must either yield one ordinarily validated Proposal or fail closed with
  its own truthful outcome. This is a fresh schema-constrained response, not
  a claim that the original response's meaning was preserved or transformed.
  The source response remains inspectable through
  the existing Agent-attempt history; do not copy its raw text, parser detail,
  or sealed path into the new provider context or an API response.
- Boundaries: add an immutable repair-source relationship on the new Attempt,
  with an additive migration and database-enforced at-most-one repair per
  source. An eligible source is the run's latest Agent attempt, a terminal
  Codex Planner/Proposal attempt whose status is `Failed` and outcome is exactly
  `InvalidStructuredOutput`, which is not itself a repair. Require the same
  run, workspace, checkpoint, fresh fingerprint, active lease, provider
  observation, current model/effort snapshot, no running attempt, and the
  existing count and reserved-time budgets as an ordinary Planner claim.
  Revalidate source identity/eligibility and the source's latest position at
  the durable claim boundary; a concurrent claim or repair must produce a
  safe, retryable or already-used result and no orphaned sealed manifest.
  Preserve the existing Planner dispatch/replay, read-only sandbox, timeout,
  capture limits, parser, result recording, and one-Proposal ledger rule.
  Expose one protected manual request action and truthful source/repair
  lineage in the Planner status/cockpit; the UI may suggest repair only for
  the known invalid latest attempt, while the server alone decides eligibility.
  Keep the existing ordinary planning request available under its own rules.
- Exclusions: no automatic retry; no repair of Claude or other Codex roles;
  no repair chain or second repair of one source; no provider-session resume;
  no refeeding raw output, transcript, secrets, or untrusted diagnostic text;
  no schema relaxation, fabricated Proposal, alternate provider, new CLI
  argument, or budget override. This is a DevalCopilot-owned retry contract,
  not evidence of model, account allowance, session, or invocation
  eligibility. Do not create a generic retry framework or broad HumanInstruction
  control. The accepted ADRs' other contracts remain intact.
- Stop gates: return to planner/reviewer before broadening if a safe
  source-to-repair uniqueness/claim transaction cannot be established; if
  preserving current read-only dispatch, clean-exit validation, and immutable
  source context requires changing provider behavior or revising an accepted
  ADR; or if the real artifact/sensitivity contracts require copying prior
  raw output for a meaningful repair. Do not silently loosen source matching,
  Git evidence, parser, or budget rules to make the path work.
- Acceptance evidence: migration round-trip and unique-index race; Domain
  factory validation; application/API success and fail-closed cases (unknown,
  foreign-run, wrong role/contract/outcome, nonlatest source, repair-of-repair,
  already repaired, active attempt, changed checkpoint/lease, exhausted count
  or time budget, provider unavailable, preference change during claim);
  source/repair lineage and no duplicate Proposal; supervisor replay and
  unchanged read-only adapter arguments; frontend manual action, safe errors,
  run switch, and refresh; no raw response/path/diagnostic leakage. Run the
  routed build, full affected backend/frontend suites, migration/integration
  checks, generated-client stability, doc links, and `git diff --check`.
  Tests must use deterministic doubles and no real provider invocation.
- **Executor prompt (English; send in a new Claude executor chat):**

  > Implement the selected “one manual Codex Planner format-repair attempt”
  > slice recorded at the top of `docs/roadmap/planner-handoff.md`. Before
  > editing, read `AGENTS.md`, `../EngineeringStandards/ENGINEERING.md`,
  > `docs/engineering-context.md`, `docs/roadmap/current-work.md`, this planner
  > handoff, the relevant routed standards, the Increment 4 roadmap, ADR-0004,
  > ADR-0012, ADR-0013, and the relevant current code/tests. Verify branch
  > `main`, exact `HEAD` `ea8ae506d116ac9af812cc0891baa13b6a2ac13f`,
  > local and live `origin/main` at that same SHA, zero staged files, only
  > `docs/roadmap/planner-handoff.md` modified/unstaged by Codex's selection,
  > and zero untracked files. Stop and report any material discrepancy before
  > editing; Git and code prevail over this prompt.
  >
  > Implement one human-requested repair claim for the latest completed Codex
  > Planner/Proposal attempt whose outcome is `InvalidStructuredOutput`. Make
  > the new Attempt durably identify its source, allow at most one direct repair
  > per source through a database backstop, and forbid a repair of a repair.
  > Use the ordinary planning claim's workspace, lease, fresh Git checkpoint,
  > provider observation, model/effort snapshot, concurrency, budget,
  > sealed-manifest cleanup, and short claim-transaction protections. Guard
  > source eligibility again at the commit boundary. The repair manifest may
  > carry only current bounded planning context plus a fixed host-authored
  > statement that the source output failed structural validation and must
  > satisfy the unchanged Proposal schema. Do not include the source's raw
  > response, parser details, artifact path, or arbitrary human text. Reuse
  > only the original objective and current verified context; describe the
  > result as a fresh Proposal, never as a semantic correction of the source.
  > Reuse the existing read-only Planner dispatch, restart replay, parser, result
  > recording, and one-Proposal ledger behavior. Add a protected manual API
  > action and a clear cockpit action/status that distinguish a repair from
  > an ordinary new plan without treating a displayed button as eligibility.
  > Keep the ordinary request available. Preserve response secrecy and use
  > safe fixed errors.
  >
  > Cover the listed acceptance cases with meaningful tests, including a
  > concurrent pair of repair claims, a source made stale during claim, a
  > preference change during claim, and process restart/replay. Update the
  > architecture and cockpit specifications and make a concise, commit-ready
  > `docs/roadmap/current-work.md` entry describing actual behavior, checks,
  > and residual risks, with no future SHA or publication claim. If a stop
  > gate in the selected handoff is reached, stop and report evidence instead
  > of broadening scope. Present the complete **uncommitted, unpushed** diff,
  > exact changed-file/staged/unstaged/untracked state, test commands and
  > outcomes, and any failures for Codex GO/NO-GO review. Do not commit or
  > push; this selection grants no publication GO.

## Previous selection (2026-09-29): inspect historical Agent attempts without a collaboration message

- Final correction re-review (2026-09-29): **GO for the reviewed substantive
  slice, with one exact factual handoff edit described below.** Independently
  verified `main`, `HEAD`, local and live `origin/main` at
  `391317193575d111a5e27afd76140010ce3bd21b`, nothing staged, 14
  modified tracked files (including this planner record and `current-work.md`)
  and 30 untracked files. `AgentAttemptScalars` is now the only top-level type
  in its own file; `AgentAttemptRead` remains alone in its file. The API test
  summary now distinguishes metadata/envelope fields from returned sealed
  text, and the helper/architecture/handoff wording accurately names the
  `InvalidOperationException` catch boundary. Independently built the whole
  solution with shared compilation disabled (0 warnings, 0 errors), then ran
  focused API history/message-window tests 35/35 and Application
  identity/sealed-window tests 20/20 against that build. `git diff --check`
  reports only the known generated-client CRLF notice. The executor reports
  Architecture 9/9 and the prior complete backend/frontend suites and stable
  generated client; those results remain applicable to this round's unchanged
  behavior and type move.
- Single publication instruction after this GO: before staging, make only a
  factual correction to the `current-work.md` checks bullet: close its open
  parenthesis and record all three observed build-lock incidents accurately
  (two Api locks in earlier initial builds and one VBCSCompiler/Application
  lock in this final correction round), each cleared on rebuild, with the
  final solution build at 0 warnings/0 errors. Change no code, tests, wire
  contract, or other documentation beyond this planner GO record. Verify the
  exact staged set is the reviewed 14 modified plus 30 new files, including
  `current-work.md` and this planner record, and run `git diff --check` and
  local handoff-link checks. Commit the reviewed substantive slice on `main`,
  push normally as a fast-forward to `origin/main`, and independently fetch
  and verify that the live remote points to its exact SHA. Run the focused
  history/evidence/window API tests, identity/sealed-window Application tests,
  and frontend history-panel tests against that substantive commit as the
  handoff specifies, and report exact results and tree state. Then make only
  the tightly bounded factual `current-work.md` closure recording the
  substantive SHA, verified publication, and post-publication checks actually
  run; commit and push that documentation-only closure normally and verify the
  live remote and clean tree again. No SHA of a commit belongs in that same
  commit's documentation. Any material post-GO change, unexpected staged or
  worktree file, recurring build/test failure, failed push, or remote
  divergence stops for Codex review/direction; do not force-push or reconcile
  history. The narrow factual edits named here need no separate GO. This GO
  authorizes only publication of this reviewed slice; no next slice is selected.

- Second uncommitted diff review (2026-09-29): **NO-GO; final bounded
  contract/documentation correction in this same slice.** Independently
  verified `main`, `HEAD`, local and live `origin/main` at
  `391317193575d111a5e27afd76140010ce3bd21b`, nothing staged, the same
  14 modified tracked files and 29 untracked files (one new
  `AgentAttemptRead.cs`). Focused API history/message-window tests passed
  35/35 independently; `git diff --check` reports only the known generated
  client CRLF notice. The two prior behavioral findings are addressed: the
  raw text/envelope test now distinguishes content from metadata, and real
  malformed persisted enum strings exercise all three routes and fail closed.
  Three small corrections remain:
  1. `AgentAttemptRead.cs` declares two public top-level types,
     `AgentAttemptScalars` and `AgentAttemptRead`. The engineering contract's
     non-negotiable C# rule is one top-level type per file. Move the scalar
     record unchanged into `AgentAttemptScalars.cs`; preserve the read behavior.
  2. The XML summary atop `AgentAttemptHistoryEndpointTests` still says no
     response discloses a path, hash, session identifier, or prompt. That is
     false for a successful window's verified `text`, as the new regression
     test proves. Limit the summary to metadata and window-envelope fields.
  3. `current-work.md` says the parallel Api build file-lock error was “not
     reproducible,” although the executor reports that it recurred once on a
     subsequent first build. State the two observed incidents and successful
     clean rebuild accurately. In the helper/architecture comments, describe
     the actual catch boundary as any `InvalidOperationException` during full
     row materialization; it is not proven specific to enum conversion, as
     `current-work.md` already records among remaining risks.
  Keep the diff uncommitted and unpushed. Rerun the solution build and affected
  focused tests after the type move, documentation links, and `git diff
  --check`; identify earlier full-suite results that still apply to this
  file move and wording correction. Return the complete corrected diff for
  GO/NO-GO. No commit/push GO is granted.

- First uncommitted diff review (2026-09-29): **NO-GO; two bounded corrections
  in this same slice.** Independently verified `main`, `HEAD`, local and live
  `origin/main` at `391317193575d111a5e27afd76140010ce3bd21b`, nothing
  staged, 14 modified tracked files (including this planner record) and 28
  untracked files. Independently passed focused API history/message-window
  tests 30/30, Application identity tests 11/11, and `git diff --check` (only
  the known generated-client CRLF notice). The run-scoped routes, cursor,
  closed-purpose reader, and lazy UI appear within the selected boundary.
  Correct these two issues before GO:
  1. The new API test's `ReadAsync` applies `AssertNoSensitiveDisclosure` to
     *artifact-window text* as well as metadata. Its blanket assertions that
     no `prompt`, session-shaped value, path, or hash appears in the body are
     false for a successful sealed-text read: that route deliberately returns
     verified, best-effort-redacted text, and the documented residual risk is
     that sensitive-looking content may remain. The new architecture section's
     “None of the three responses carries …” sentence and `current-work.md`'s
     “No path, hash, session identifier, or prompt is returned” make the same
     overclaim. Limit leak assertions and wording to response *metadata* and
     non-text fields. Add a focused API case showing that sensitive-shaped,
     clearly fictitious content inside a sealed artifact is returned exactly
     as verified text while no storage path/hash/session field is added to the
     response envelope. Keep the purpose-specific caveats accurate; do not
     suppress or reinterpret verified artifact bytes.
  2. `AgentAttemptIdentity.IsCoherent` runs only after EF materializes an
     `Attempt`, but `AttemptConfiguration` uses string enum converters. An
     unrecognized persisted status/role/provider/contract string can throw
     during `ToListAsync`/`SingleOrDefaultAsync` before the identity gate and
     thus never produce the claimed safe invalid-identity outcome. Add API
     tests with a deliberately malformed persisted enum string for history,
     evidence, and window routes. Handle that boundary safely without
     disclosing exception detail or artifacts. If such a row cannot be listed
     individually with the existing EF model, return a distinct safe
     unavailable result for the affected read and narrow the documentation's
     “incoherent row is listed” claim to rows that materialize; do not add a
     broad raw-SQL or schema rewrite merely for this edge case. Also narrow
     the existing `identityValid: false and nothing else` wording to the
     actual number/lifecycle fields returned.
  Keep the diff uncommitted and unpushed. Run the affected API and frontend
  checks, full validation relevant to any code change, documentation links,
  generated-client drift check if the wire response changes, and `git diff
  --check`; report what was rerun and what earlier full-suite evidence still
  applies. Return the complete corrected diff for GO/NO-GO. No commit/push GO
  is granted.

- Verified selection baseline: branch `main`; `HEAD`, local `origin/main`, and
  live `origin/main` all equal `391317193575d111a5e27afd76140010ce3bd21b`.
  Staged, unstaged, and untracked state was empty before this planner-only edit.
  The reviewed Claude effort-request slice is published as
  `f2ec6155db28777bd164a33f7646677eef4e0c49`, followed by factual
  `current-work.md` closure `391317193575d111a5e27afd76140010ce3bd21b`.
- Objective: make the existing sealed Agent-attempt evidence inspectable for
  every historical Agent attempt with verifiable identity in a run, including a failed or interrupted
  attempt that emitted no `ProviderObserved` collaboration message. The current
  collaboration evidence and artifact-window routes require such a message's
  `AttemptId`; all six role status routes select only their role's latest
  attempt, and the cockpit exposes only the run's latest Agent attempt. A
  failed earlier attempt can therefore have sealed output but no useful
  historical navigation path. This closes a concrete part of Increment 4's
  protocol-validation/raw-artifact visibility without changing provider or
  workflow behavior. It follows [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md),
  [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md),
  and the existing [sealed Agent-artifact contract](../architecture/agent-collaboration-protocol.md#sealed-agent-artifact-window-inspection).
- Boundaries: add a protected, run-scoped, read-only, descending Agent-attempt
  history query with a stable `AttemptNumber` cursor and a small hard page cap
  (20 or less), including explicit `hasMore`/next-cursor semantics. A selected
  attempt gets on-demand bounded evidence metadata and a separate on-demand
  sealed text-window read for only `AgentContextManifest`,
  `AgentStandardOutput`, `AgentStandardError`, and `AgentFinalResponse`.
  Resolve by exact `(RunId, AttemptId)` and `Kind == Agent`; validate defined,
  coherent role/provider/assignment before disclosing attempt evidence; filter
  each artifact by its independently stored `RunId`, `AttemptId`, and purpose.
  Reuse the existing artifact-store `VerifyAndReadSealedAsync` integrity,
  containment, byte-cap, UTF-8 cursor, and safe missing/mismatch behavior.
  Preserve the message-linked routes' independent provenance/coherence checks.
  The cockpit's Evidence area offers a paged history and a single selected
  attempt drill-down with manual next-window loading, plain-text rendering,
  purpose-specific sensitivity caveats, explicit empty/error/retry states, and
  reset on run/attempt/purpose/close changes. Do not fetch raw text for all
  history rows or retain it in browser storage, URLs, or logs.
- Exclusions: no provider call, session correlation/open/resume, CLI argument,
  model/effort/permission change, account allowance, token threshold or claim
  eligibility, new attempt or message, replay, provider retry, workflow mutation, schema or
  migration, Process/verification artifact, generic file browser, download,
  export, HTML/Markdown rendering, or change to existing sealed-store
  guarantees. Do not add Unknown-only scaffolding. Provider-session resume and
  Claude account allowance remain unproven. A future account observation must
  never be represented as threshold enforcement or invocation eligibility.
- Stop gates: stop and report if the exact run/attempt/assignment identity
  cannot be validated safely, if historical paging cannot remain deterministic
  and bounded, if serving a sealed artifact requires reopening an unverified
  path or weakening the store/provenance boundary, or if the UI would need to
  infer provider capability or current workflow authority from historical
  evidence. Do not silently broaden this slice to fix any such issue.
- Acceptance evidence: exercise ordinary and historical failed/no-message
  attempts across the supported role/provider pairs; descending pagination,
  legacy history beyond 16 attempts, cursor boundaries, and run-switch reset;
  non-Agent, unknown and foreign-run attempts; missing/undefined/incoherent
  assignment; cross-run artifact rows; disallowed purpose; absent sealed row;
  missing file, tampered length/hash, and traversal/reparse containment through
  the existing store; multi-window UTF-8 reconstruction and retry without
  duplicated text; protected API access and no path/hash/session/prompt leakage;
  frontend lazy fetch, purpose switching, close/reset, loading/error, and
  markup-shaped text as literal text. Run affected backend/API/frontend tests,
  solution build, frontend typecheck/lint/build, NSwag regeneration/drift,
  documentation-link review, and `git diff --check`; report exact commands,
  pass/fail/skip counts, and any limitation rather than claiming unrun checks.
- The complete English executor prompt below is the dispatch contract. Return
  the **uncommitted, unpushed** diff, including a concise, commit-ready
  `docs/roadmap/current-work.md` entry based on the verified parent, with
  checks actually run and remaining risks but no future SHA or publication
  claim. Keep corrections in the same executor chat. No commit/push GO is
  granted by this selection.
- After a future GO, give one publication instruction for the exact reviewed
  substantive diff, normal fast-forward push, independent live-remote
  verification, and only a tightly bounded factual `current-work.md` closure.
  Any material change after GO returns for review before committing; a failed
  push or remote divergence stops without force-push or history repair.

### Executor prompt

You are the designated Claude execution agent for one selected Increment 4
slice. Before editing, read `AGENTS.md`, `../EngineeringStandards/ENGINEERING.md`,
`docs/engineering-context.md`, `docs/roadmap/current-work.md`, and this current
selection in `docs/roadmap/planner-handoff.md`; follow the engineering
contract's routing for the detailed standards relevant to backend read models,
protected MVC APIs, generated clients, React, testing, and security. Verify
branch `main`, exact `HEAD` `391317193575d111a5e27afd76140010ce3bd21b`,
local and live `origin/main` at that SHA, nothing staged or untracked, and
exactly one unstaged modified file:
`docs/roadmap/planner-handoff.md` (this planner selection). If any material
fact differs, stop and report it before editing; Git and code prevail.

Implement one read-only vertical slice that lets an owner inspect sealed
evidence from any historical Agent attempt with verifiable identity in a run, especially a failed or
interrupted attempt with no `ProviderObserved` collaboration card. Add a
protected, run-scoped history query ordered by descending unique
`AttemptNumber`, with a strict before-number cursor, a hard page cap of at
most 20, and honest `hasMore`/next-cursor fields. Include only `Kind == Agent`;
do not assume the 16-claim default bounds historical runs. Add an on-demand
selected-attempt evidence read and a separate on-demand sealed text-window
read limited to the four Agent purposes: context manifest, stdout, stderr,
and final response. Resolve every read by exact `(RunId, AttemptId)`; validate
defined, coherent role/provider/assignment before disclosing evidence, and
filter artifacts by their independently stored run, attempt, and purpose.
Keep the existing collaboration-message evidence and window routes and their
own provenance/coherence checks intact. Reuse
`IArtifactStore.VerifyAndReadSealedAsync` and its one-handle integrity,
containment, byte cap, UTF-8 cursor, and safe missing/mismatch semantics; do
not reopen artifact paths or return unverified/partial text. History and
metadata must not disclose storage paths, hashes, raw session identifiers, or
full prompts.

In the cockpit's Evidence area, provide a paged Agent-attempt history and one
selected-attempt drill-down. Fetch metadata on demand and raw text only when
the owner selects a purpose. Load further windows manually. Render text
literally, include the existing purpose-specific sensitivity caveats and a
historical-evidence caveat, and reset accumulated text on run, attempt,
purpose, and close changes. Handle empty, missing, integrity failure,
loading, error, and retry states explicitly. Do not store artifact text in a
URL, browser storage, or logs.

Do not change provider invocation, CLI arguments, session behavior or resume,
model/effort/permission controls, account or token controls, claim/dispatch
eligibility, workflow state, schema, Process or verification artifacts, or
generic file browsing/export. Do not add Unknown-only scaffolding or infer
provider capability. Claude account allowance and provider-session resume
remain unproven; account observation is not threshold enforcement or
invocation eligibility. Stop and report if exact run/attempt/assignment
identity, stable bounded paging, or sealed read containment cannot be
maintained without a broader change; do not silently widen scope.

Prove supported role/provider pairs and a failed no-message attempt; history
pagination including a legacy run beyond 16 Agent attempts; unknown,
non-Agent, foreign-run, and incoherent attempts; cross-run artifact rows;
disallowed/missing/tampered artifacts and existing containment behavior;
multi-window UTF-8 reconstruction and safe UI retry; protected API access
and no path/hash/session/prompt leakage; and frontend lazy fetching,
run/attempt/purpose/close reset, error states, and literal markup-shaped
content. Use focused tests at the narrowest meaningful boundaries. Run
affected backend/API/frontend suites, solution build, frontend typecheck,
lint and production build, NSwag regeneration/drift check, documentation
links, and `git diff --check`; report exact commands and outcomes, including
skips or limits. Update the architecture and cockpit specifications for the
delivered behavior. Make `docs/roadmap/current-work.md` concise and
commit-ready from the verified parent, with actual checks, remaining risks,
and post-publication verification, without a future commit SHA or publication
claim. Return the full uncommitted, unpushed diff, changed-file list, checks,
and blockers to Codex for GO/NO-GO. Keep corrections in this executor chat.
Do not select another slice, commit, or push before explicit Codex GO.

## Previous selection (2026-09-29): explicit Claude effort requests at safe attempt boundaries

- Correction re-review (2026-09-29): **GO for the reviewed substantive diff.**
  Independently verified `main`, `HEAD`, local and live `origin/main` at
  `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`; nothing staged, 67
  modified tracked files (including this planner record) and 6 untracked
  files. `ClaudeModelRequestSnapshot` now has its own file, with the claim
  guard unchanged. The architecture, cockpit spec, and `current-work.md`
  accurately distinguish this slice's request-only fields from pre-existing
  independently provider-observed effort facts. Independently repeated the
  solution build: 0 errors and 0 warnings; the executor's one intermediate,
  undiagnosed build error did not recur. `git diff --check` reports only the
  two known generated-file line-ending notices. The executor reports focused
  Application 243/243, API 138/138, Architecture 9/9, doc links, stable
  generated client, and the earlier full-suite results in `current-work.md`.
  Those results remain applicable because this correction moved one unchanged
  type and changed documentation only. This GO covers only this exact diff;
  no next product slice is selected.
- Publication instruction: commit exactly the reviewed substantive slice on
  `main`, including `current-work.md` and this planner review record; push
  normally as a fast-forward to `origin/main`; independently fetch/verify that
  the live remote points to the delivered commit and report its exact SHA and
  worktree state. Run the focused Claude claim, adapter, supervisor replay,
  set/clear endpoint, and migration checks against that commit as
  `current-work.md` states. Then make only a factual `current-work.md` closure
  recording the substantive SHA, verified publication, and checks actually
  run, commit/push that closure normally, and independently verify the live
  remote and clean tree again. A material post-GO change, unexpected
  staged/worktree change, recurring build or test failure, failed push, or
  remote divergence stops for Codex review/direction; do not force-push or
  reconcile remote history. The narrowly factual closure needs no second GO.

- First uncommitted diff review (2026-09-29): **NO-GO; two bounded
  corrections in this same slice.** Independently verified `main`, `HEAD`,
  local and live `origin/main` at
  `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`; nothing staged, 67
  modified tracked files (including this planner record) and 5 untracked
  files. Focused Domain 235/235, Application 133/133, Infrastructure 94/94,
  API 59/59, and frontend 72/72 passed; the executor reports full validation
  in `current-work.md`. The pair validation, claim guard, Attempt-only dispatch,
  adapter argument boundary, migration, and cockpit refresh match the selected
  scope. Correct these two issues before GO:
  1. `CurrentClaudeModelPreference.cs` now declares both the public top-level
     `ClaudeModelRequestSnapshot` record struct and the public top-level
     `CurrentClaudeModelPreference` class. The engineering contract requires
     exactly one top-level C# type per file. Move the snapshot unchanged into
     its own correctly named file; keep the shared claim guard behavior.
  2. The new architecture section says no observed effort is "stored, or
     displayed," and the new cockpit spec says no observed effort is "ever
     shown." Those blanket claims conflict with the existing
     `ImplementationAttemptStatusResponse.ObservedEffort` field and
     `ImplementationAction`'s "Effort observed" line. Narrow the text to the
     actual guarantee: this slice does not infer an observed/effective value
     from `--effort`, and its new cockpit request fields display only requests.
     Keep any existing independently provider-observed assignment field
     accurately distinguished. Check `current-work.md` for the same wording.
  Keep the diff uncommitted and unpushed. Rerun the build and affected focused
  tests after the file move, documentation links, and `git diff --check`;
  identify earlier full-suite results that still apply. Return the complete
  corrected diff for GO/NO-GO. No commit/push GO is granted.

- Verified publication baseline: branch `main`; `HEAD`, local `origin/main`, and
  live `origin/main` all equal `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`.
  Staged, unstaged, and untracked state was empty before this planner-only edit.
  The prior token-warning delivery is substantive commit
  `46c33900089eb4ad4d29f6fc440fe96a9158fe7c` followed by factual closure
  `bc6e1552f1964aadcb7cb71c1d6c71400cb46e1e`; the reported focused
  post-publication checks passed and the live remote was independently verified.
- Select one coherent Increment 4 slice: extend the existing explicit,
  run-scoped Claude model-alias request with an **optional requested effort**
  for future CriticalReviewer, Implementer, and ReviewCorrection attempts.
  Allow the closed, case-sensitive `low`/`medium`/`high` request values only
  when the owner also explicitly requests `sonnet` or `opus`; `haiku` and an
  absent model have no effort control under the currently documented model
  support. No preference means no `--effort` argument. This is an invocation
  request, not an observed/effective effort, capability discovery, account
  allowance, or eligibility guarantee. The official [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents `--effort`; [model configuration](https://code.claude.com/docs/en/model-config)
  documents model-dependent support and possible silent organization/model
  clamping in JSON mode. Alias resolution and account availability remain
  unproven; the provider may reject or adjust a request without any application
  retry, fallback, or fabricated observed value.
- Boundaries: add one nullable Run effort field and additive no-backfill
  migration; extend the existing protected Claude model-preference set/clear
  operation to validate and atomically persist the model/effort pair with one
  durable change event and safe lifecycle/concurrency handling. Extend the
  existing late claim-time fresh read and EF commit guard to snapshot both
  fields into each of the three Attempt assignments, including a race with a
  preference change after external Git/manifest work. Carry only that immutable
  Attempt snapshot through eligible queries, invocation requests, supervisors,
  and the three Claude adapters. A shared argument boundary appends exactly
  `--effort <level>` for a valid non-null snapshot, preserving current null and
  model-only argument lists; invalid stored values fail closed before process
  start. Add the requested pair to cockpit transport and control, regenerate
  NSwag, and make a successful Save/Clear refresh the authoritative cockpit
  without requiring a run event. Show the current Run request separately from
  the latest Claude attempt's own claim-time snapshot, never as observed.
  Update the architecture/product specs and `current-work.md` truthfully.
- Exclusions: no Claude model catalog, runtime-capability claim, observed
  effort inference, provider-session resume, context compaction, account
  allowance/stop rule, token-warning change, permission/tool/schema change,
  Codex path, additional role, new provider, or generic settings framework.
  Keep the six claim budgets and eligibility rules unchanged. Do not infer
  support from CLI defaults or use a real provider in automated tests.
- Stop gates: return before broadening scope if the configured CLI contract
  cannot safely accept a discrete `--effort` argument; model/effort cannot be
  snapshotted and guarded atomically at commit; any changed adapter argument
  would loosen permission, tools, schema, or session isolation; or the UI
  cannot distinguish a requested value from a provider-observed/effective one.
  Document any provider rejection or silent clamp as a limitation, never a
  successful effective-effort observation.
- Acceptance evidence: exact closed-set and pair validation, terminal-run and
  authenticated API refusals, independent no-backfill migration, atomic
  preference/event persistence, change-during-claim rollback and manifest
  cleanup for all three roles, immutable dispatch/restart replay, exact
  per-adapter arguments for null/model-only/model-plus-effort and invalid
  snapshots with zero process starts, provider failure without application fallback,
  cockpit request-versus-attempt labels, and immediate post-save/clear refresh
  with stale-run and safe-failure behavior. Run focused Domain/Application/
  Infrastructure/API/frontend tests, relevant full solution and frontend
  validation, TypeScript, lint, production build, deterministic NSwag
  regeneration, documentation links, and `git diff --check`; report actual
  commands, outcomes, and skips.
- Expected executor preflight: `main` at the full HEAD above; nothing staged;
  only `docs/roadmap/planner-handoff.md` modified/unstaged by this selection;
  nothing untracked. The first delivery is a complete **uncommitted, unpushed**
  diff with a commit-ready `current-work.md` entry for Codex GO/NO-GO. Slice
  selection gives no commit/push GO. A future GO will use one publication
  instruction for the reviewed substantive commit, normal fast-forward push,
  live-remote verification, and tightly bounded factual documentation closure;
  any material post-GO change returns for review.

## Prior selection (2026-09-28): per-provider run token-activity warnings

- Correction re-review (2026-09-29): **GO for the reviewed substantive diff.**
  Independently verified `main`, `HEAD`, local and live `origin/main` at
  `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`; nothing staged, 22
  modified tracked files (including this planner record) and 26 untracked
  files. The added `useRunCockpit().refresh` queues a post-change read behind
  any in-flight pass, rejects a stale run generation, and reports a fixed safe
  failure; the panel refreshes after successful Save/Clear and mirrors the
  inclusive 1..10^12 server limit. The architecture text now distinguishes
  persisted usage validation from the accumulator's running-attempt exclusion.
  Independently passed focused frontend hook/panel/refresh tests 36/36 and
  `git diff --check` (only the two known generated-file line-ending notices).
  The executor reports full frontend 724/724, TypeScript, build, lint, doc
  links, and the earlier unchanged-backend suites and migration/NSwag/mutation
  evidence in `current-work.md`. The remaining risks there are accepted within
  this advisory, local-evidence scope. This GO covers only this exact diff;
  no next product slice is selected.
- Publication instruction: commit exactly the reviewed substantive slice on
  `main`, including `current-work.md` and this planner review record; push it
  normally as a fast-forward to `origin/main`; independently fetch/verify the
  live remote points to the delivered commit and report its exact SHA and
  worktree state. Run the focused threshold, projection, endpoint, migration,
  and claim-unaffected checks against that commit as `current-work.md` states.
  Then make only a factual `current-work.md` closure recording the substantive
  SHA, verified publication, and checks actually run, commit/push that closure
  normally, and independently verify the live remote and clean tree again.
  A material post-GO change, unexpected staged/worktree change, failing check,
  failed push, or remote divergence stops for Codex review/direction; do not
  force-push or reconcile remote history. The narrowly factual closure needs
  no second GO.

- First uncommitted diff review (2026-09-29): **NO-GO; correct the cockpit's
  immediate post-save state and two accuracy edges in this same slice.**
  Independently verified `main`, `HEAD`, local and live `origin/main` at
  `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`; nothing staged, 20 modified
  tracked files (including this planner record) and 25 untracked files. Focused
  Application 39/39, API 13/13, and frontend warning/hook 20/20 passed; the
  executor reports the full suites and builds in `current-work.md`. The backend
  projection, provider-specific formulas, migration, and advisory-only scope
  are directionally sound. Three corrections remain:
  1. `TokenWarningPanel` updates its local `saved` value after Save/Clear but
     derives its visible status only from the unchanged `warning` prop. The
     new endpoint sends no `runAdvanced` notification, and `useRunCockpit`
     exposes no explicit refresh. A changed threshold therefore does not
     immediately re-evaluate already-recorded evidence in the displayed
     cockpit. Make a successful set/clear cause a generation-safe authoritative
     cockpit refresh, with focused tests proving an existing count crosses
     into/out of `ThresholdReached` without an unrelated run event. Preserve
     stale-run protection and show a safe synchronization failure.
  2. The frontend accepts a positive safe integer above the server's 10^12
     cap and submits it, while the product spec says oversized input is
     rejected locally. Enforce the same cap locally and test 10^12 + 1.
  3. The new architecture section says `AgentTokenUsageEvidence.FromPersisted`
     enforces terminal status. It validates provider/schema and usage shape;
     the cockpit accumulator independently checks the attempt status. State
     those two responsibilities accurately, and keep `current-work.md`
     commit-ready without claiming publication.
  Keep the correction uncommitted and unpushed. Rerun affected frontend tests,
  the relevant full validation if the changed hook/cockpit path affects it,
  documentation links, and `git diff --check`; report exactly what ran and
  return the complete corrected diff for GO/NO-GO. No commit/push GO is granted.

- Verified publication baseline: branch `main`; `HEAD`, local `origin/main`, and
  live `origin/main` all equal `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`.
  Staged, unstaged, and untracked state was empty before this planner-only edit.
  The preceding Claude model-request delivery is substantive commit
  `8ddc34284c3c5461510090c06a895cce9871966d` followed by factual closure
  `8d1afb1b9a597c43eff7fa2aa4a1a7b60153640b`; its reported focused
  post-publication checks passed and the live remote was independently verified.
- Select one larger, coherent Increment 4 slice: let the owner set or clear an
  optional positive **run-scoped warning threshold in reported token-activity
  units for each of Codex and Claude Code**, persist both independently, and
  show a prominent provider-specific warning when trusted, concluded-attempt
  evidence reaches that provider's threshold. This is an advisory warning on
  local attempt evidence, never a hard token budget, account allowance,
  provider cost, or invocation/claim eligibility rule. No threshold is assumed
  for historical or new runs until the owner configures one.
- The warning count for a known Codex attempt is its validated `inputTokens +
  outputTokens`; its `cached_input_tokens` is already within Codex's reported
  input and must not be added again. For a known Claude attempt it is validated
  `inputTokens + cacheCreationInputTokens + cacheReadInputTokens + outputTokens`;
  the current Claude CLI parser requires all four fields. A persisted Claude
  usage row missing either cache field is insufficient for this warning even
  if the existing raw usage projection can still display its input/output
  values; do not change that raw projection's contract. These formulas are
  intentionally provider-specific, never added into a cross-provider budget.
  Reuse the existing `AgentTokenUsageEvidence` provider/schema validation,
  terminal/dispatched check, and the cockpit's same loaded attempt evidence
  and provider attribution; add only the warning derivation needed to detect
  a Claude row whose cache breakdown is absent. A
  missing, malformed, or still-running usage must never be counted as zero;
  an undispatched attempt is excluded because no provider was invoked, and an
  unattributed attempt is never silently assigned to either provider. The
  provider's known count may be a lower bound when its evidence is incomplete;
  reaching the threshold is a valid warning, but being below it with gaps is
  explicitly indeterminate, not an all-clear.
- Current provider-contract basis: the official [Codex non-interactive JSONL
  contract](https://learn.chatgpt.com/docs/non-interactive-mode) documents the
  terminal `turn.completed` usage fields, and the [official Codex usage
  mapping](https://github.com/openai/codex/blob/main/codex-rs/codex-api/src/sse/responses.rs)
  maps cached input as a breakdown of input; the official [Claude Code CLI
  reference](https://code.claude.com/docs/en/cli-reference) documents print-mode
  JSON output, while the existing versioned local Claude parser owns the exact
  four-field envelope proof. The official [Claude cache-token
  contract](https://platform.claude.com/docs/en/build-with-claude/prompt-caching)
  distinguishes ordinary input from cache creation/read input. No new provider
  adapter, authenticated provider invocation, model inference, allowance read,
  or session-resume contract is part of this slice.
- Boundaries: nullable independent Run thresholds with an additive migration
  and no backfill; a protected, validated MVC set/clear operation with safe
  lifecycle and concurrent-update handling plus a durable configuration-change
  event; an Application-owned warning projection using existing per-provider
  evidence and explicit completeness/known-count states; additive cockpit API
  response, regenerated NSwag client, and a cockpit control and warning display
  for each provider. Keep the existing run-wide and provider-separated raw
  evidence displays intact. Update the architecture/product specifications and
  `current-work.md` with truthful semantics. Do not create a general budget or
  provider-settings framework, and do not alter the six Agent claim paths,
  dispatch, adapters, provider assignment, or provider permission arguments.
- Stop gates: report to Codex before broadening scope if the current versioned
  usage evidence cannot support either formula without inventing a missing
  value; a provider/schema or attribution mismatch could yield a false warning;
  a setting race could persist threshold and event inconsistently; or the UI
  cannot distinguish below-threshold partial evidence from a complete below-
  threshold result. Never substitute account-allowance percentages or CLI
  defaults for this evidence. Avoid real provider calls in automated tests.
- Acceptance evidence: unset/clear states are neutral; positive thresholds
  persist independently for Codex and Claude, while zero/negative/overflow
  values and terminal-run updates fail safely; concurrent changes preserve
  threshold/event atomicity; a known zero is distinct from no evidence; exact
  equality warns; Codex cache is not double-counted and Claude cache counts
  exactly once; partial/pending/unknown/malformed/undispatched/unattributed
  evidence cannot produce a false all-clear or false attribution; an
  undispatched attempt is excluded; threshold
  changes immediately re-evaluate already recorded evidence; the warning never
  affects claim or dispatch eligibility. Require focused Domain/Application/
  Infrastructure/API/frontend and migration tests, relevant full backend and
  frontend validation, TypeScript check, lint, production build, deterministic
  NSwag regeneration, documentation links, and `git diff --check`, reporting
  actual outcomes and skips.
- Claude's first delivery must be a complete **uncommitted, unpushed** diff,
  including a concise, commit-ready `current-work.md` entry with actual checks
  and remaining risks, for Codex GO/NO-GO. Expected executor preflight: `main`
  at the full HEAD above, nothing staged, only
  `docs/roadmap/planner-handoff.md` modified/unstaged by this selection, and
  nothing untracked. Selection grants no commit/push GO. After a future GO,
  one publication instruction will cover the reviewed substantive commit,
  normal fast-forward push, live-remote verification, and only tightly bounded
  factual documentation closure. A material post-GO change returns for review.

## Prior selection (2026-09-28): explicit Claude model request across three roles

- Correction re-review (2026-09-28): **GO for the reviewed substantive diff.**
  Independently reverified `main`, `HEAD`, local and live `origin/main` at
  `7cc091dcaa032740def1cca214c3ad83c09a5cd2`, nothing staged, the same 51
  modified tracked files and 22 untracked files, and no commit or push. The
  `current-work.md` delivery entry no longer asserts an unreviewed/unpublished
  state, and the preceding architecture sentence now explicitly refers to the
  earlier Codex-only delivery with a working link to the new Claude section.
  `git diff --check` passed with only the two known generated-file line-ending
  notices. This documentation-only correction leaves the prior focused
  Application 96/96 and API 47/47 results, and the executor-reported full
  build/test/frontend/NSwag evidence, applicable. The reviewed request,
  snapshot, invocation, and cockpit behavior stays inside the selected scope.
- One bounded publication instruction: commit exactly the reviewed substantive
  slice on `main`, including `current-work.md` and this planner-owned review
  record; push normally as a fast-forward to `origin/main`; independently
  fetch/verify that the live remote points to the delivered commit and report
  its exact SHA and worktree state. Reconfirm the focused Claude claim, adapter,
  and supervisor tests against the delivered commit as `current-work.md`
  specifies. Then make only the factual `current-work.md` closure needed to
  record the delivered SHA and verified publication (and those actual focused
  results), commit/push that closure normally, and independently verify the
  live remote and clean state again. No extra review is needed for that
  narrowly factual closure. A material or out-of-scope post-GO change, failed
  push, test failure, or remote divergence stops for Codex review/direction;
  no force-push or history reconciliation is authorized. No next product slice
  is selected by this GO.

- First uncommitted diff review (2026-09-28): **NO-GO; documentation-only
  correction in this same slice.** Independently verified `main`, `HEAD`, local
  and live `origin/main` at
  `7cc091dcaa032740def1cca214c3ad83c09a5cd2`; nothing staged, 51 modified
  tracked files (including this planner record), and 22 untracked files. The
  Claude request, claim snapshot, dispatch, adapter, API, migration, and cockpit
  paths match the selected boundaries on review. Independently passed focused
  Application 96/96, focused API 47/47, and `git diff --check` (only existing
  generated-file line-ending notices); the executor's broader checks remain
  reported in `current-work.md`. Two documentation statements must be fixed:
  `current-work.md` says this delivery is “Not yet reviewed or published,”
  which is not commit-ready; describe it as the current delivery based on the
  verified parent, without preclaiming publication or a future SHA. The prior
  Codex model-request subsection in `agent-collaboration-protocol.md` still
  ends “Claude paths and their own fixed arguments are entirely unaffected,”
  which is false next to the new Claude `--model` behavior; scope that sentence
  explicitly to the historical Codex-only delivery or update it to describe
  the present contract. Keep code, tests, and all other scope unchanged; recheck
  links and `git diff --check`, then return the complete uncommitted, unpushed
  diff for re-review. No commit/push GO is granted.

- Verified publication baseline: branch `main`; `HEAD`, local `origin/main`,
  and live `origin/main` all equal
  `7cc091dcaa032740def1cca214c3ad83c09a5cd2`. Staged, unstaged, and
  untracked state was empty before this planner-only edit. The preceding
  sealed-read delivery is `447a650636eb2d0e26e34a356b379045a1181e52`,
  followed by factual closure `7cc091dcaa032740def1cca214c3ad83c09a5cd2`.
  Its post-publication focused `FilesystemArtifactStoreTests` reconfirmation
  passed on this HEAD: 26 passed, one host-capability skip.
- Select one larger, coherent Increment 4 slice: an owner can explicitly set or
  clear a **run-scoped Claude model-alias request** for future CriticalReviewer,
  Implementer, and ReviewCorrection Agent attempts. Persist that preference on
  the Run, snapshot the request immutably on each new Claude Attempt, carry the
  snapshot through each role's invocation request, and pass `--model <alias>`
  only when that attempt's snapshot is non-null. Expose the current preference
  and each attempt's requested value in the cockpit with accurate “requested”
  language. The existing adapter argument lists, permission/tool isolation,
  response contracts, and provider-assignment provenance otherwise remain
  intact. The requested alias is never an observed/effective model or proof of
  account access. A provider rejection remains an ordinary safely recorded
  failed invocation, not a reason to silently switch models.
- The closed selectable aliases for this slice are `sonnet`, `opus`, and
  `haiku`, which the current official
  [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  explicitly accepts for `--model`. This is a documented request syntax, not
  live model discovery or proof that any alias is enabled on this account.
  Default `null` means no override: preserve the exact old CLI arguments and
  do not infer a model from a CLI default. The same reference documents
  `--effort` but says available levels depend on the model; effort selection
  needs a separate safe validation contract and is excluded here. The current
  official [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode)
  and [App Server](https://learn.chatgpt.com/docs/app-server) contracts do not
  change this Claude-only decision. Accepted [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  requires requested and observed assignment facts to remain distinct; this
  slice extends its existing immutable Attempt fields without reversing it.
- Boundaries: add a nullable Run preference and truthful additive migration
  (`NULL` for historical runs); a validated, authenticated MVC set/clear
  operation with durable event and concurrency/lifecycle handling analogous to
  the existing Codex preference; read projection and cockpit control; snapshot
  in the three Claude claim paths; propagate through the existing Application
  invocation ports and supervisors; narrowly update the three Claude adapters
  and focused tests; regenerate the API client; update the architecture,
  cockpit, and delivery documentation. Keep one role's existing assignment
  validation and attempt ownership rules in force. No new generic provider
  settings framework is authorized.
- Exclude live Claude catalog discovery, model-specific effort support,
  `--effort`, observed-model fabrication, account allowance or threshold
  enforcement, provider-session resume, context compaction, provider fallback,
  permission/tool changes, historical Attempt backfill, Codex behavior, and
  any commit or push. Do not treat this request preference as invocation
  eligibility. Preserve the one-running-attempt, budget, Git/workspace, and
  authorization checks on every existing path.
- Stop gates: report a blocker before broadening scope if the installed vetted
  Claude launch target contradicts the documented `--model` contract; the
  requested alias cannot be safely captured at claim and replayed unchanged at
  dispatch; a selected alias requires inferring account availability or
  model/effort compatibility; the migration would invent historical requests;
  or preserving the existing role isolation would require a new capability
  policy. No provider invocation is required for automated tests.
- Acceptance evidence: default/clear adds no flag; each of the three roles
  snapshots and passes exactly its own explicit alias; changing the Run after
  claim cannot change that Attempt's invocation; invalid aliases and terminal
  Run changes fail safely without a write; current versus historical/unknown
  requested facts are distinguished; concurrent lifecycle transitions cannot
  partially persist preference/event; provider rejection does not trigger
  fallback; current Codex preference and Claude permission/tool flags remain
  unchanged. Require focused Domain/Application/Infrastructure/API/frontend
  tests, the relevant full backend and frontend suites, TypeScript check,
  lint, production build, deterministic NSwag regeneration, documentation
  links, and `git diff --check`, reporting actual results and any skips.
- Claude's first delivery is a complete **uncommitted, unpushed** diff,
  including a concise, commit-ready `current-work.md` entry with checks and
  remaining risks, for Codex GO/NO-GO in this planner chat. The expected
  executor preflight is `main` at the full HEAD above, nothing staged, only
  `docs/roadmap/planner-handoff.md` modified/unstaged by this selection, and
  nothing untracked. Selection grants no commit/push GO. After a future GO,
  one publication instruction will cover the reviewed substantive commit,
  normal fast-forward push, live-remote verification, and only tightly bounded
  factual documentation closure. Material post-GO change returns for review.

## Prior selection (2026-09-28): harden verified sealed-artifact reads

- Correction re-review (2026-09-28): **GO for the reviewed substantive diff.**
  Independently reverified `main`, `HEAD`, local and live `origin/main` at
  `26ae6bcb8df98d3d589b0ab5264a0be135353da6`, nothing staged, six
  modified tracked files and two untracked files, with nothing committed or
  pushed. Both lexical and opened-handle root comparisons now use `Ordinal`;
  the real case-distinct-sibling junction regression and the `..` spelling
  regression passed, and the executor confirmed each failed with the old
  comparison. The architecture now describes the present Windows and
  non-Windows behavior, and `current-work.md` distinguishes code-path
  reasoning from executed tests and is commit-ready. Codex independently
  passed focused Infrastructure 26/26 with one host-capability skip, focused
  Agent-window API 16/16, and `git diff --check`; executor-reported full
  backend suites and build are in `current-work.md`. The file-leaf symlink
  test remains conditional and did not execute on this host; the running
  intermediate-junction and case-sensitive-sibling tests plus the reviewed
  Win32 final-path contract support this bounded Windows read design. This
  GO does not claim non-Windows physical containment, write-path hardening,
  or resistance to privileged in-place file mutation.
- One bounded publication instruction: commit the reviewed substantive diff
  on `main`, including `current-work.md` and this planner-owned review record;
  push normally as a fast-forward to `origin/main`; independently verify the
  live remote points to the delivered commit and report exact SHA and
  worktree state. Then make only the factual `current-work.md` closure needed
  to record the delivered SHA and verified publication, commit/push it
  normally, and independently verify the live remote and clean state again.
  No extra review is needed for that narrowly factual closure. A material or
  out-of-scope change after GO returns for review; a failed push or remote
  divergence stops without force-push or history reconciliation. No next
  product slice is selected by this GO.

- First uncommitted diff review (2026-09-28): **NO-GO; correct one containment
  edge and the evidence/documentation claims in this same slice.** Independently
  reverified `main`, `HEAD`, local and live `origin/main` at
  `26ae6bcb8df98d3d589b0ab5264a0be135353da6`, nothing staged, five
  modified tracked files (including this planner record), and one untracked
  Infrastructure file; nothing committed or pushed. The one-handle read and
  Windows final-path check are directionally sound. Focused
  `FilesystemArtifactStoreTests` independently passed 24/24 with the file-leaf
  symlink case skipped on this host; `git diff --check` passed. The executor's
  broader checks are recorded in `current-work.md`.
- `ResolveWithinRoot` and `IsPhysicallyContainedInRoot` both use
  `OrdinalIgnoreCase` for their root-prefix checks. Windows supports NTFS
  directories that distinguish names only by case, so an outside sibling whose
  name differs from the root only by case can satisfy both checks. Make both
  containment comparisons fail closed for that case (for example, compare
  normalized path components ordinally), and add a focused regression for a
  case-distinct sibling path. Do not assert universal physical containment
  until this edge is closed. See Microsoft's
  [case-sensitivity documentation](https://learn.microsoft.com/en-us/windows/wsl/case-sensitivity).
- The current architecture subsection still says the sealed read uses only a
  lexical check and leaves every planted reparse point open. Update the
  present-tense contract to describe the new Windows opened-handle check, the
  unchanged non-Windows lexical limitation, and remaining root/write-path
  boundaries. Keep the earlier slice's historical delivery facts truthful.
  The new `current-work.md` entry is too long for a delivery checkpoint and
  overstates evidence: an ordinary multi-window read cannot prove the
  absence of a second open because no path substitution occurs; the proof is
  the reviewed single-handle code path unless a deterministic path-swap test
  is added. A junctioned root is intentionally accepted when the file lies
  within its resolved target, so do not claim all root redirects are rejected.
  Condense the entry to actual checks, behavior, platform limits, and residual
  risk. The file-leaf symlink test is present but skipped here; report it as
  unexecuted on this host, not as observed proof. The running intermediate
  junction test and official Win32 final-path contract support the general
  design, but keep the conditional leaf test for a capable Windows host.
- Keep the correction uncommitted and unpushed. Run the affected focused
  filesystem tests and relevant full validation if code changes could affect
  callers; recheck the architecture link and `git diff --check`. Return the
  complete corrected diff for GO/NO-GO. No commit/push GO is granted.

- Publication baseline verified before this selection: branch `main`, `HEAD`,
  local `origin/main`, and live `origin/main` all equal
  `26ae6bcb8df98d3d589b0ab5264a0be135353da6`; staged, unstaged, and
  untracked state is empty. The reviewed sealed Agent-artifact inspection was
  published in substantive commit `77cae0a4bbdb811f65625c7552f18f88dc7ee96e`
  and factual handoff closure `26ae6bcb8df98d3d589b0ab5264a0be135353da6`.
  Git and code, not the executor's publication report, established this
  baseline. The previous handoff's post-publication focused reconfirmation
  passed on this HEAD: Application 9/9 and API 16/16. This planner-only edit
  will be the expected unstaged change when
  the executor begins; no commit/push GO is granted.
- Select one bounded Increment 4 slice: **make the shared sealed-artifact read
  boundary verify and return bytes from one opened file identity, and reject a
  filesystem redirect outside the application-owned artifact root before any
  sealed content is returned.** `FilesystemArtifactStore.VerifyAndReadSealedAsync`
  now checks only lexical containment, hashes one `FileStream`, and opens the
  path again to read the window. A planted reparse point can redirect the
  textual path; a path replacement between the two opens can detach the
  returned window from the bytes whose hash was verified. The newly delivered
  Agent-artifact viewer, existing Process output reader, verification-output
  reader, and provider manifest reads share this boundary. This slice improves
  their existing protection without making a provider call. The accepted
  [ADR-0003](../decisions/0003-use-a-durable-sqlite-event-journal.md) assigns
  large content to a hashed filesystem artifact store; no accepted decision is
  reversed. Current official [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode),
  [Codex App Server](https://learn.chatgpt.com/docs/app-server), and
  [Claude CLI](https://code.claude.com/docs/en/cli-reference) contracts were
  checked; this host-owned file-boundary slice infers no new provider
  capability, account allowance, threshold, invocation eligibility, or
  provider-session resume. Microsoft's [reparse-point](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-operations)
  and [file-handle](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openhandle?view=net-10.0)
  documentation is relevant to the implementation proof.
- Boundaries: change the Infrastructure `FilesystemArtifactStore` sealed-read
  path and its narrow internal collaborators, focused filesystem integration
  tests, and the factual architecture/security and delivery documentation.
  Preserve the `IArtifactStore` method/result contract, byte cap, UTF-8 cursor
  behavior, `Missing` versus `IntegrityMismatch` semantics, cancellation, and
  safe API projections. Validate the database-supplied relative path and the
  physical location of the **opened** sealed file against the artifact root;
  reject root/descendant reparse redirects rather than trusting
  `Path.GetFullPath` or a pre-open check alone. Hash, check length, seek, and
  return the bounded window through the same read handle. Fail closed on
  unreadable or ambiguous paths without exposing a path, hash, or raw
  diagnostic. Do not alter capture, seal, cleanup, partial reads, artifact
  schema, MVC routes, UI, adapters, provider arguments, claim/dispatch policy,
  model/effort selection, context/compaction, account usage, or session actions.
  Do not claim this fixes every artifact-root write/cleanup path or arbitrary
  hostile filesystem races beyond the read guarantee actually proven.
- Stop gates: if the opened file's physical containment cannot be proven with
  a reviewed, supportable Windows-first mechanism and an honest behavior for
  other supported platforms, or if avoiding the second open changes the
  existing read contract, stop and report evidence rather than substituting a
  lexical or check-then-open claim. Stop if tests cannot exercise a planted
  junction/symlink that points to an outside sentinel with **matching**
  recorded length/hash, if a failure leaks content or diagnostics, or if the
  fix requires broad artifact-store write redesign or an ADR reversal.
- Acceptance evidence: ordinary Agent and verification artifacts still serve
  exact text and monotonic UTF-8 windows; absolute and `..`-escaping stored
  paths remain unavailable; a planted intermediate directory junction and a
  sealed-file redirect to an outside sentinel return no text even when the
  supplied length/hash match that sentinel; a tampered ordinary sealed file
  remains `IntegrityMismatch`; missing files remain `Missing`; cancellation
  propagates. Show that the bytes hashed and the returned window use one
  opened identity, not two path opens. Recheck a real API caller's safe
  response and no path/hash/diagnostic leakage. Run focused store and API
  integration checks, relevant full backend validation, build/analyzers, and
  `git diff --check`; report exact commands and outcomes. Update the
  architecture's lexical-containment/open-risk wording only as far as the
  evidence proves, including any residual race or write-path limitation.

### English execution prompt for the designated Claude executor

Implement the selected Increment 4 sealed-artifact read hardening on `main`.
Before editing, verify exact `HEAD` `26ae6bcb8df98d3d589b0ab5264a0be135353da6`,
branch `main`, local and live `origin/main` at that SHA, nothing staged, only
`docs/roadmap/planner-handoff.md` modified but unstaged, and nothing untracked.
Report any material discrepancy and stop. Read the selected boundary and stop
gates above, the shared engineering contract and routed security, C#, adapter,
and integration-test standards, the current implementation and tests, and the
accepted ADRs before changing code.

Harden only `FilesystemArtifactStore.VerifyAndReadSealedAsync` and its narrow
Infrastructure helpers. Prove the opened file remains physically under the
application-owned artifact root and cannot be reached through a planted
root/descendant reparse redirect; use the same opened file identity for the
whole-file length/hash check and bounded UTF-8 window. Keep its public port,
result statuses, safe caller behavior, and existing window semantics intact.
Add deterministic isolated-filesystem tests for normal reads, mismatch,
missing and lexical-escape paths, intermediate and leaf redirects to an
outside sentinel whose bytes deliberately match the supplied hash/length,
and the one-handle guarantee. Recheck an API caller's failure projection.
Do not broaden into artifact writes, provider/runtime behavior, policy,
schema, routes, or UI. If a stop gate is reached, return the evidence and
leave the slice uncommitted instead of claiming containment from lexical
checks or CLI defaults.

Update `docs/roadmap/current-work.md` with a concise, commit-ready delivery
entry based on the verified parent, actual checks, limitations, and a
post-publication verification action. Update the relevant architecture/security
text to match the proven guarantee. Return the complete uncommitted, unpushed
diff, changed files, exact test/build/analyzer commands and results, and open
risks for Codex GO/NO-GO. Keep corrections in this executor chat. Do not
select another slice or commit/push without explicit GO. After a future GO,
one bounded publication instruction will cover the reviewed substantive
commit, normal fast-forward push, live-remote verification, and only a
tightly bounded factual documentation closure. Any material change after GO
returns for re-review.

## Previous selection (2026-09-28): inspect sealed Agent attempt artifacts

- Final correction re-review (2026-09-28): **GO for the reviewed substantive
  sealed Agent-artifact inspection diff.** Independently reverified `main`,
  `HEAD`, local and live `origin/main` at
  `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`, with nothing staged,
  eight modified tracked files and ten untracked files (18 total). The final
  documentation correction changes only the last paragraph of the new
  architecture subsection: it now distinguishes host-constructed context
  manifests from best-effort-redacted provider output and retains the
  sensitive-content warning. Both linked section headings/anchors remain
  present, `current-work.md` is commit-ready, and `git diff --check` passes
  with only the known generated-client line-ending warning. The independent
  focused Application 9/9 and API 16/16 from the first review, and focused
  frontend 25/25 plus typecheck from the correction review, remain applicable
  because this last change is documentation-only. Executor-reported full
  suites and frontend build/lint are recorded in `current-work.md`. The
  existing lexical-only sealed-path containment limitation is explicitly
  recorded as an open risk; this GO does not claim it is fixed.
- One bounded publication instruction for this GO: commit the reviewed
  substantive diff on `main`, including `current-work.md` and this
  planner-owned review record; push normally as a fast-forward to
  `origin/main`; fetch or otherwise independently verify the live remote
  points to the delivered commit and report the exact SHA and working-tree
  state. Then make only the factual `current-work.md` closure needed to
  record the delivered SHA and verified publication, commit/push it normally,
  and verify the live remote and clean state again. No extra review is needed
  for that narrowly factual closure. A material or out-of-scope change after
  GO must return for review; a failed push or remote divergence must stop
  without force-push or history reconciliation. This GO selects no next
  product slice.

- Correction re-review (2026-09-28): **NO-GO, one documentation-only correction
  in this same slice.** Reverified `main`, `HEAD`, local and live
  `origin/main` at `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`, with
  nothing staged, the same eight modified tracked files and ten untracked
  files, and nothing pushed. The later-window retry now preserves the failed
  offset, its deterministic `0, 9, 9` regression test passes, the viewer's
  purpose-specific caveats match the persisted sensitivity classifications,
  the path-containment limitation is stated, and `current-work.md` is
  commit-ready. Independently passed focused frontend 25/25, typecheck, and
  `git diff --check` (only the known generated-client line-ending warning);
  the independently passed Application 9/9 and API 16/16 from the preceding
  review remain applicable because the backend did not change.
- One stale sentence remains at the end of the new "Sealed Agent-artifact
  window inspection" subsection in `docs/architecture/agent-collaboration-protocol.md`:
  "Every window is labeled as historical, best-effort-redacted sealed
  provider text." That still misclassifies `AgentContextManifest`, despite
  the earlier paragraph and viewer correctly distinguishing host-constructed
  context from redacted provider output. Correct only that sentence to describe
  the purpose-specific caveats accurately. Preserve code, tests, the
  commit-ready delivery handoff, and all other documentation. Recheck the
  local link/anchor and `git diff --check`; prior test results still apply to
  this documentation-only change. Return the complete uncommitted, unpushed
  diff for re-review. No commit/push GO is granted.

- First uncommitted diff review (2026-09-28): **NO-GO; correct the viewer's
  later-window retry and make the evidence wording commit-ready and accurate.**
  Reverified `main`, `HEAD`, local `origin/main`, and live `origin/main` at
  `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`; nothing staged, eight
  tracked files modified (including this record), ten untracked files, and
  nothing pushed. The new query and endpoint follow the selected message,
  attempt, purpose, and artifact-row ownership boundaries. Independently
  passed focused Application 9/9, API 16/16, frontend viewer/drill-down 24/24,
  and `git diff --check` (only the known generated-client line-ending warning).
- In `useSealedAgentArtifactWindow`, after a successful first window, a failed
  later-window request leaves earlier text in `accumulatedTextRef` but changes
  state to `error`. `loadNextWindow` then retries from byte offset zero and
  appends that first window to the retained text. Correct the retry so it
  either resumes at the failed offset with its earlier text or clears the
  accumulation before restarting at zero. Add a deterministic second-window
  failure/retry test asserting exact text and offsets, including no duplicate
  or omitted bytes. Keep purpose/run/message/drawer reset behavior intact.
- The viewer and both new specification sections describe every artifact as
  "best-effort redacted before capture." `AgentContextManifest` is recorded as
  `HostConstructedContent`; the other provider-output artifacts are recorded
  as `RedactedBestEffort`. Use a caveat truthful for all four purposes (and
  still warn that sensitive content may remain). Do not expose a false
  per-artifact redaction claim or add schema merely for this label.
- `FilesystemArtifactStore.ResolveWithinRoot` uses `Path.GetFullPath` and
  lexical containment. It rejects absolute and `..`-escaping stored paths,
  which the new API test proves; it does not itself establish containment
  against a reparse-point redirection. Narrow the new documentation's
  categorical "path-escaping"/"safe containment" claims to the protection
  actually tested, and record that existing limitation among open risks unless
  a narrow, independently verified hardening is necessary under the current
  app-owned artifact-root threat model. Do not broaden into a generic file
  access redesign in this correction.
- `current-work.md` still calls this slice uncommitted, unpushed, and awaiting
  GO. Make it concise and commit-ready as the current delivery based on parent
  `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`, with actual checks,
  limitations, and the post-publication verification action, without claiming
  publication or embedding its future SHA. Preserve the selected slice and
  return the complete corrected diff uncommitted and unpushed. Run affected
  checks first, then relevant full validation for code changes; recheck local
  links and `git diff --check`. No commit/push GO is granted.

- Verified selection baseline before editing this record: branch `main`, `HEAD`,
  local `origin/main`, and live `origin/main` all equal
  `b0aa8e4fdad09850e155c2c13d1f8cc4636306d6`; staged, unstaged, and
  untracked state was empty. The preceding explicit Codex assignment slice and
  its factual closure are published; `current-work.md` selects no subsequent
  slice. This planner-owned selection itself is the only expected local edit
  when the executor starts. It grants no commit or push GO.
- Select exactly one Increment 4 slice: **on-demand, read-only inspection of
  sealed artifacts belonging to the exact Agent attempt behind a
  ProviderObserved collaboration card**. The existing evidence drill-down
  resolves that attempt by the message's durable link and displays bounded
  artifact metadata; `ArtifactPurpose` already identifies its context manifest,
  stdout, stderr, and final response. `IArtifactStore.VerifyAndReadSealedAsync`
  already verifies length and SHA-256 before returning a bounded UTF-8 window.
  The [cockpit specification](../product/run-cockpit-specification.md) calls
  for linked raw-artifact inspection, and the [architecture contract](../architecture/agent-collaboration-protocol.md)
  keeps raw provider output in sealed artifacts rather than the database. This
  gives the owner useful failure and provenance evidence without a provider
  call, a new parser, or an inferred capability. The current [Codex CLI
  reference](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
  documents JSONL stdout and a final-response file; the current [Claude CLI
  reference](https://code.claude.com/docs/en/cli-reference) documents JSON
  print output. Neither contract grants session resume, account enforcement,
  or invocation eligibility through this viewer.
- Boundary: add one protected MVC read operation and Application query for a
  closed allowlist of the four Agent artifact purposes. Resolve the requested
  run, ProviderObserved message, and its coherent Agent attempt by the same
  durable-link rules as the existing evidence drill-down; require the Artifact
  row to match **both** run and attempt plus requested purpose. Return only a
  bounded, integrity-verified sealed-text window and safe metadata (status,
  byte offset, total length, captured/truncation state). Never return a storage
  path, hash, raw diagnostic, or content on a missing/incoherent link or failed
  integrity check. Reuse the existing artifact-store boundary and UTF-8 cursor
  semantics; retain a finite per-request byte cap. Add an on-demand text-only
  viewer within the existing collaboration evidence drill-down, with explicit
  purpose choice and manual next-window loading, clear historical/sensitivity
  labeling, and state reset when run, message, purpose, or drawer changes.
  Serve only sealed rows; no speculative live partial file read. If the existing
  sealed-store containment is insufficient for this newly reachable content,
  include a narrowly scoped hardening and regression test or stop for review.
- Exclude CLI arguments and execution, provider protocol parsing, model/effort
  or permission policy, claim/dispatch/authorization policy beyond this read,
  session identifiers/resume, context compaction, account allowance and
  thresholds, token budgets, artifact persistence schema/retention/deletion,
  arbitrary file browsing/export/download, unsealed streams, Process attempts,
  verification outputs, and generic artifact APIs. Do not turn the UI into a
  shell, render provider text as HTML, persist fetched text in browser storage,
  URLs, analytics, or logs, or treat redaction as proof that it contains no
  secret. Existing metadata-only status responses remain metadata-only.
- Stop if exact message-to-attempt and artifact ownership cannot be enforced,
  if the sealed store cannot return an integrity-verified bounded window safely,
  if revealing a purpose requires a broader authority or retention change, or
  if a test would require a real provider. Report the blocker without widening
  the slice. Acceptance evidence must cover all four purposes; valid multi-
  window UTF-8 progress; missing artifact; wrong run/message/attempt/purpose,
  simulated or incoherent provenance; malformed offsets and byte caps;
  missing/tampered/path-escaping sealed files; safe status and no-content
  failures; authentication; no raw path/hash/diagnostic disclosure; and
  frontend on-demand, switching, reset, loading, error, and text-only rendering
  including markup-shaped untrusted content. Run affected tests, relevant full
  backend/frontend suites, NSwag regeneration and drift check, typecheck, lint,
  production build, local documentation links, and `git diff --check`; report
  exact commands and outcomes. Update architecture and cockpit prose only for
  delivered behavior, and make `current-work.md` commit-ready without a future
  SHA or a false claim of publication.

### English execution prompt for the designated Claude executor

```text
Implement the bounded sealed Agent-artifact inspection slice selected by Codex.

Preflight: expected branch main and HEAD b0aa8e4fdad09850e155c2c13d1f8cc4636306d6. Local origin/main and the live remote matched that SHA at selection. Expected staged: empty; unstaged: only docs/roadmap/planner-handoff.md (this planner-owned selection); untracked: empty. Verify once before editing and report a material discrepancy. Preserve the planner-owned selection.

Objective: let the owner open bounded, integrity-verified text windows of the sealed context manifest, stdout, stderr, and final response belonging to the exact Agent attempt linked from a ProviderObserved collaboration card.

Scope: extend the existing collaboration evidence drill-down with a protected, read-only MVC endpoint, one Application query/handler, an additive API response and regenerated NSwag client, a lazy frontend viewer/hook, focused tests, and the two affected architecture/product specifications. Resolve run and message ownership, ProviderObserved provenance, coherent Agent attempt identity, and Artifact.RunId plus Artifact.AttemptId plus allowlisted purpose at the server. Reuse IArtifactStore.VerifyAndReadSealedAsync and its UTF-8 byte cursor; cap every response, verify the entire sealed artifact before revealing a window, and return explicit safe statuses for unavailable or corrupted evidence. Offer purpose selection and manual next-window loading only for artifact metadata actually linked to that attempt. Render untrusted content as plain React text, with historical and best-effort-redaction caveats. Reset content when run/message/purpose/drawer changes. Keep existing metadata-only status contracts metadata-only. Narrowly harden the sealed-store read boundary if needed to enforce safe containment, with a regression test; stop if that cannot be done within this slice.

Exclusions: no provider calls, CLI or adapter argument changes, new parser, claim/dispatch policy, model/effort or permissions, session resume, context compaction, account or token thresholds, schema or retention changes, partial/live Agent file reads, Process or verification artifacts, generic file browsing, download/export, HTML rendering, browser storage, or logging of artifact text. Do not add Unknown-only scaffolding or infer capability from CLI defaults.

Stop gates: report without broadening scope if message-to-attempt coherence, cross-run artifact ownership, bounded sealed integrity, or safe path containment cannot be proven; if a purpose needs new authority or retention policy; or if testing requires a real provider.

Acceptance evidence: all four purposes; multiple windows with a UTF-8 split boundary and monotonic cursor; empty/missing/tampered/path-escaping files; wrong run, message, attempt, purpose, non-ProviderObserved and incoherent links; negative offset and byte bounds; safe 401 and non-disclosing failures; no path/hash/diagnostic leakage; frontend lazy fetch, purpose switching, drawer/run/message reset, loading/error, and markup-shaped content rendered as text. Use deterministic local fixtures. Run affected and relevant full backend/frontend checks, NSwag generation and drift verification, typecheck, lint, production build, local link check, and git diff --check. Report every command and outcome, with short failure excerpts if needed.

Update docs/roadmap/current-work.md as a concise, commit-ready delivery entry based on the verified parent, with actual checks, remaining risks, and post-publication verification, without embedding the future commit SHA or claiming publication already happened. Return the complete uncommitted, unpushed diff, changed-file summary, check outcomes, and blockers to Codex for GO/NO-GO. Keep corrections in this executor chat. Do not select another slice or commit/push before explicit GO.
```

After a future GO, issue one bounded publication instruction covering the
reviewed substantive commit (including `current-work.md`), a normal fast-
forward push of `main` to `origin/main`, live-remote verification, and only a
tightly bounded factual handoff closure for the delivered SHA if needed.
Re-review any material post-GO change; stop on push failure or divergence.

## Prior decision (2026-09-28): GO for explicit Codex model and effort requests

- **GO for the reviewed uncommitted diff.** Independently verified branch
  `main`, `HEAD`, local and live `origin/main` all at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged, 53
  tracked files modified and 25 new untracked files (78 files total), nothing
  committed or pushed. The independent `IAttemptDurabilityProbe` releases the
  claim transaction before checking through a new context and distinguishes
  persisted, absent, and unresolved outcomes. The six new commit-and-rollback
  fault tests exercise both durable outcomes across the three handlers; the
  two-connection test now proves its signal identifies the competing `UPDATE`
  and includes a no-lock negative control. Independently reran those focused
  tests: 6/6 Application and 2/2 Infrastructure. Executor-reported relevant
  full suites: Domain 547/547, Application 1069/1069, Infrastructure 520/521
  (one pre-existing skip), API 339/339, Architecture 9/9; generated client
  unchanged on repeat build. `git diff --check` found no whitespace errors;
  Git reported only the two known line-ending normalization warnings. Earlier
  frontend 666/666, typecheck, lint, and build remain applicable because this
  correction did not change frontend or API contracts. The commit-ready
  `current-work.md` records the delivered mechanism and actual checks.
- Publication instruction for this GO: commit the reviewed substantive slice,
  including `current-work.md` and this planner-owned review record, on `main`;
  push normally to `origin/main` as a fast-forward; independently fetch and
  verify the live remote points to the delivered commit and report the exact
  SHA and remaining working-tree state. Then make only the tightly bounded
  factual documentation closure needed to record the delivered SHA and
  verified publication, commit/push it normally, and verify live remote and
  clean state again. If the substantive diff changes materially, leaves the
  approved scope, the push fails, or remote history diverges, stop and return
  for review. This GO does not select another slice.

## Historical correction reviews (2026-09-28)

- Eighth uncommitted diff review: **NO-GO; make durable-outcome reads
  independent and signal the actual competing UPDATE.** Reverified `main`,
  `HEAD`, local and live `origin/main` at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 46 tracked
  files modified (including this record) and 23 untracked files.
  Independently reran the real-SQLite two-connection test: 1/1 passed. The
  four cancellation catch paths now propagate and handle their tested cases,
  and the transaction still protects the guard plus Attempt commit.
- On ambiguous save/commit failure, each handler calls
  `RollbackBestEffortAsync`, which explicitly swallows a rollback failure,
  then checks `dbContext.Attempts.AsNoTracking()` on the *same context* while
  the transaction object is still in scope. If rollback failed before
  releasing an uncommitted transaction, that connection can see its own
  uncommitted Attempt and report success/retain the sealed file. Disposing
  the transaction afterward can roll it back, leaving no durable Attempt.
  `AsNoTracking` bypasses EF identity tracking, not the connection's active
  transaction. Resolve ambiguous outcomes only after releasing that
  transaction, through an independent database connection/context; if an
  independent durable check is unavailable, preserve the file and surface
  unresolved state rather than assert success or delete it. Add a fault test
  where commit and rollback both throw before commit, and the same check for
  an actually completed commit whose rollback throws.
- `WriteAttemptSignalInterceptor` signals on every Reader/NonQuery/Scalar
  command, including the competing context's initial `Runs.SingleAsync`
  SELECT. Its signal therefore does not prove the UPDATE was attempted, even
  with the 200 ms confirmation window. Filter for the actual preference
  UPDATE (or another precise write-command identity) and assert that a
  SELECT cannot fire the signal; retain bounded waits, the no-lock negative
  control, and the final ordering assertions. Correct the test/docs claim
  accordingly. Return the uncommitted, unpushed diff after affected and
  relevant full checks. No commit/push GO is granted.
- Seventh uncommitted diff review: **NO-GO; close cancellation cleanup and
  make the two-connection assertion observe the database write.** Reverified
  `main`, `HEAD`, local and live `origin/main` at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 46 tracked
  files modified (including this record) and 23 untracked files. Independently
  ran the nine new transaction-acquisition/commit fault-injection tests:
  9/9 passed. The new `DbException` paths correctly classify provider failure
  and resolve simulated commit-before/after ambiguity; the transaction now
  protects the guard and Attempt commit.
- All three handlers seal their manifest before the final transaction, but
  acquisition/guard/commit catch only `DbException`, and the save catch only
  `DbUpdateException`. Cancellation during a bounded lock wait throws
  `OperationCanceledException`, bypassing cleanup of that sealed file; this
  is a realistic path for a canceled request, not a confirmed database
  failure. Preserve cancellation propagation while resolving sealed-file
  ownership in a `finally` or equivalent for every exit after seal; if commit
  outcome is ambiguous, use an independent durable read with a fresh bounded
  token before deleting a potentially referenced file. Add focused
  cancellation fault-injection evidence for the new final boundary.
- In `ClaimTimeAssignmentPreferenceGuardTests`, the `TaskCompletionSource`
  fires immediately *before* `racingContext.SaveChangesAsync`; the test then
  checks `racingWriteTask.IsCompleted` as soon as it wakes. The competing task
  may not yet have entered EF or issued SQL, so a scheduler switch can make
  that assertion pass without any database lock. Signal from a command
  interceptor or equivalent point at the actual attempted SQL write, and
  use bounded synchronization to distinguish blocking from an unscheduled
  task. Keep the two-connection ordering check and avoid awaiting the writer
  to completion while the claim holds the lock. Update the test/docs to
  describe the proven boundary accurately. Run affected then relevant full
  checks and return the complete uncommitted, unpushed diff. No commit/push
  GO is granted.
- Sixth uncommitted diff review: **NO-GO; preserve sealed-artifact ownership
  across the new transaction's failure paths.** Reverified `main`, `HEAD`,
  local and live `origin/main` at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 46 tracked
  files modified (including this record) and 22 untracked files. Independently
  reran the new two-connection guard test: 1/1 passed. The guard and Attempt
  save now share an explicit short transaction after external work, and the
  Code Review manual-command correction remains in place.
- All three handlers seal the manifest before calling
  `dbContext.BeginTransactionAsync`, but that call is outside their error
  handling. Acquiring a transaction can fail or time out under the very
  write contention this change addresses; then the request exits with the
  sealed file orphaned. `CommitAsync` is likewise inside a catch limited to
  `DbUpdateException`; a raw provider/connection failure at commit bypasses
  both existing cleanup and the required persisted-attempt check, where the
  outcome may be ambiguous. Cover begin, guard, save, and commit failures
  with one bounded cleanup/outcome discipline: remove the sealed file only
  when a fresh read confirms the Attempt did not commit; retain it on a
  confirmed commit; never label an unrelated database failure as a proven
  preference change. Preserve cancellation and rollback exceptions without
  accidentally skipping ownership resolution. Add focused fault-injection
  evidence for begin and commit paths, alongside the real-SQLite test.
- The new test uses a 300 ms sleep and checks that the competing task is not
  complete, but never first proves that task has reached its database write;
  on a slow scheduler the assertion passes even without a lock. Add a
  synchronization signal or command interceptor confirming the competitor
  reached the attempted write, then assert serialization with bounded waits
  and no synchronous wait for completion while the claim owns the lock.
  Keep the external work outside the transaction. Shorten `current-work.md`
  to delivered facts and actual checks; remove the abandoned implementation
  transcript and correct claims that cleanup was unchanged. Return the full
  uncommitted, unpushed diff after affected and relevant full validation.
  No commit/push GO is granted.
- Fifth uncommitted diff review: **NO-GO; the new guard still ends before the
  Attempt commit.** Reverified `main`, `HEAD`, local and live `origin/main` at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 44 tracked
  files modified (including this record) and 21 untracked files. Independently
  ran the new Code Review mediator transaction-boundary test: 1/1 passed.
  Converting Code Review to `IManualTransactionCommand` resolves that prior
  finding.
- `CurrentCodexAssignmentPreference.ConfirmUnchangedAsync` now executes one
  conditional no-op `ExecuteUpdateAsync`, then returns; each claim handler
  subsequently adds its Attempt and calls a separate `SaveChangesAsync`.
  [EF Core's ExecuteUpdate transaction contract](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete#transactions)
  says the call does not implicitly open a transaction covering later work.
  A preference change can therefore commit after a successful guard and
  before the Attempt insert. The three new tests only change the preference
  *before* calling the guard and show that it returns false; none exercise
  the post-guard window. The claim's `Run.Lifecycle` token still does not
  guard preference-only changes. The asserted atomicity and commit-time
  freshness remain unproven and false under that interleaving.
- Make the final preference read/guard and Attempt commit one real, short
  atomic or serializable unit after external work, or use an equivalent
  single-statement/database-enforced snapshot. Prove with deterministic
  two-connection testing that a preference-only write attempted after the
  guard/read cannot commit before an Attempt with the old pair; assert the
  winner and safe loser/cleanup, without timing-dependent hangs. A blocked
  second writer while the claim holds a write lock is expected serialization,
  not by itself evidence that a short transaction is impossible. SQLite's
  [BEGIN IMMEDIATE contract](https://www.sqlite.org/lang_transaction.html)
  and the provider's
  [nondeferred transaction API](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
  are candidate mechanisms, not mandated implementation. Keep the transaction
  outside Git/artifact/provider work and bound lock waits. Correct stale
  "atomic guard" prose in source/docs, rerun affected and relevant full
  checks, and return the complete uncommitted, unpushed diff. No commit/push
  GO is granted.
- Fourth uncommitted diff review: **NO-GO; the late read is not an atomic
  claim boundary.** Reverified `main`, `HEAD`, local and live `origin/main` at
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 43 tracked
  files modified (including this record) and 20 untracked files (14 grouped
  `git status --short` entries). The new tests correctly show that a
  preference committed during external Git capture reaches all three claims
  and remains immutable after a later Run change. They do not cover a
  preference committed after `CurrentCodexAssignmentPreference.ReadAsync`
  but before the Attempt insert commits. That helper performs a separate
  untracked SELECT, followed by ordinary entity construction and a later
  `SaveChangesAsync`; no transaction or preference-version guard couples the
  SELECT to the insert. `Run.Lifecycle` is the only new concurrency token,
  and a preference-only change leaves it unchanged. A stale pair can still
  commit across precisely this narrower race. The docs' claim of a "short
  atomic read-and-claim" is therefore inaccurate.
- `CreateCodeReviewAttemptCommand` is still an ordinary `ICommand`, so the
  registered `EfTransactionBehavior` opens an EF transaction before its
  handler's external Git evidence capture and artifact work. The executor's
  new Code Review test constructs the handler directly, bypassing that
  behavior; it does not verify the claimed transaction boundary. Planning and
  Challenge Resolution already use `IManualTransactionCommand`. Move Code
  Review to the same manual boundary, retaining explicit persistence and
  cleanup, and prove via the real mediator that no EF transaction is open
  during its external evidence call.
- For all three roles, make the final authoritative preference read and
  durable Attempt claim one short atomic or guarded operation after external
  work; a concurrent preference-only update between read and commit must
  either be reflected in the claimed Attempt or cause a safe conflict/retry
  without persisting a stale Attempt or leaving an orphan artifact. Add a
  deterministic real-DB interleaving test for that *post-read* window, not
  only a change during evidence capture. Do not rely on the Lifecycle token
  or absence of further external I/O as an atomicity argument. Update
  `current-work.md` and architecture prose, run affected then relevant full
  checks, and return the complete uncommitted, unpushed diff. No commit/push
  GO is granted; the same slice remains selected.
- Third uncommitted diff review: **NO-GO; correct claim-time assignment freshness
  in this same slice.** Reverified `main`, `HEAD`, local and live `origin/main`
  at `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 41
  tracked files modified (including this record) and 19 untracked files (13
  grouped `git status --short` entries). Independently reran the four focused
  real-SQLite transaction/concurrency tests: 4/4 passed. The catalog
  transaction boundary, effort-without-model guards, and lifecycle
  concurrency-token correction now satisfy their respective review findings.
- Full-flow review found a remaining violation of the selected claim-time
  snapshot rule: the Codex Planning, Challenge Resolution, and Code Review
  claim handlers each load a tracked `Run` near handler entry, await external
  Git evidence capture and manifest sealing, then copy that same entity's
  `RequestedCodexModel`/`RequestedCodexEffort` into the new `Attempt`. If the
  owner commits a new preference during that external work, the attempt can
  be durably claimed afterward with the old pair. The new `Run.Lifecycle`
  concurrency token does not detect a preference-only change; Challenge
  Resolution and Code Review do not update the Run at claim at all. Refresh
  the requested pair from authoritative Run state at the final durable claim
  boundary, with a short atomic read/claim or an equivalent guarded write that
  cannot persist a stale pair after a preference change. No EF transaction may
  span external Git/artifact work. Cover a preference change during external
  work for all three roles, plus durable reload/dispatch from the resulting
  Attempt snapshot, preserving each handler's existing failure cleanup and
  null-override path. Update `current-work.md` and affected contract prose;
  run affected checks then relevant full validation. Return an uncommitted,
  unpushed diff for re-review. No commit/push GO is granted.
- Second uncommitted diff review: **NO-GO; one remaining concurrency correction
  in the same selected slice.** Reverified `main`, `HEAD`, local and live
  `origin/main` at `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`, nothing
  staged, 41 tracked files modified (including this planner record), and 17
  untracked files. Independently reran the two new real-SQLite transaction
  boundary tests: 2/2 passed. The external catalog read now correctly runs
  without an ambient EF transaction, and the three Codex claim factories and
  final invoker boundary now reject effort without model.
- The handler's post-catalog `Runs.SingleOrDefaultAsync` read and lifecycle
  check still occur **before** its sole `SaveChangesAsync`. There is no explicit
  write transaction spanning the fresh read and update, and `Run` has no EF
  concurrency token. A concurrent completion/interruption can commit after
  that read but before `SaveChangesAsync`; EF then updates only the preference
  columns by key and appends the event to an already-terminal Run. The
  current tests prove absence of a transaction during observation and atomic
  preference/event saving, but do not close this read-to-write race. Move the
  authoritative fresh read, lifecycle check, preference change, event insert,
  and save into one short write transaction begun only after catalog
  observation, or provide an equally atomic lifecycle-guarded write with the
  event. Add deterministic real-SQLite coverage for a lifecycle transition
  between catalog observation and write, and for the read-to-write race; a
  terminal Run must receive neither a new preference nor a change event.
  Keep the catalog-free clear path and the corrected pair invariant. Update
  `current-work.md` and affected contract prose so they describe the actual
  write boundary. Run affected checks, then relevant full validation; return
  the complete uncommitted, unpushed diff. No commit/push GO is granted.
- First uncommitted diff review: **NO-GO; correct this same selected slice and return
  the complete uncommitted, unpushed diff.** Reverified `main`; `HEAD`, local
  `origin/main`, and live `origin/main` remain
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; nothing staged; 41 tracked
  files modified (including this planner record) and 16 untracked files. Git
  and code, rather than the executor's file-count summary, define the review
  surface. Independently reran the new API endpoint tests: 9/9 passed.
- The new ordinary `ICommand` runs under `AddDevalenteEfCoreTransactions`:
  `EfTransactionScope` begins an EF transaction before the handler. The handler
  then awaits the external, bounded Codex catalog observation inside that
  transaction. This violates the short-transaction boundary in
  [engineering-context](../engineering-context.md) and
  [ADR-0003](../decisions/0003-use-a-durable-sqlite-event-journal.md).
  Use the established manual-command boundary so provider observation occurs
  with no EF transaction open. Recheck the Run after that observation, then
  persist its preference and event atomically in a short write transaction;
  preserve the catalog-free clear path and handle a terminal or changed Run
  without persisting stale state. Test at mediator/real-DB level that no
  transaction is open during observation and that the Run plus event commit
  together. The current API persistence test is green but cannot prove the
  transaction boundary.
- The new Codex `Attempt` factories validate model and effort independently,
  and `CodexProcessInvoker` accepts an effort with no model. Its new test even
  expects `--config model_reasoning_effort=high` without `--model`. That applies
  effort to an unspecified CLI default, contrary to this slice's explicit-pair
  and no-default-inference boundary. Enforce effort-requires-model in all three
  new Codex claim factories and at the invoker's final trust boundary; fail
  closed without launching a process for malformed or legacy data. Replace
  the effort-only success test with deterministic rejection coverage, and
  cover the factory invariant. Preserve null/null argv and the valid model-only
  and model-plus-effort paths.
- Update the commit-ready `current-work.md` and any affected contract text to
  describe the corrected transaction and pair invariants accurately. Run
  affected checks first, then relevant full validation; report exact commands,
  results, file state, and remaining risk. No commit/push GO is granted. The
  selected objective and exclusions below remain in force; no new slice is
  selected.

### Original selection and executor prompt

- Verified publication and selection baseline: branch `main`; `HEAD`, local
  `origin/main`, and live `origin/main` all
  `8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4`; staged, unstaged, and
  untracked state empty. The reviewed read-only Codex catalog slice was
  published as `cf5b8d64b7d41fe1b258ad919f75051e8425a408` (parent
  `c2e5023e3c4a653856b5691d52c1d27e07844826`) and factually closed by
  the baseline. The GO in the prior decision below is historical.
- Select exactly one Increment 4 slice: **explicit Codex model and reasoning-
  effort requests at the next safe Agent attempt boundary**, covering the
  existing Codex Planner, Challenge Resolver, and Code Reviewer paths. The
  existing protected `model/list` observation supplies client/account-specific
  picker-visible suggestions, not an eligibility guarantee. The official
  [Codex developer commands](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
  document `codex exec --model/-m` and a repeatable `--config/-c` override;
  the official [configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
  documents `model_reasoning_effort` and says its levels depend on model and
  client. The installed `codex-cli 0.158.0-alpha.2.1` local `codex exec --help`
  independently lists `--model`, `--config`, `--ignore-user-config`, and
  `--ephemeral`. These are invocation-argument contracts, not proof that any
  catalog entry can start a turn or that the provider honored a requested
  setting. [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  already gives `Attempt` immutable requested-model/effort fields separate
  from provider-observed fields; the current Codex claim paths leave them null
  and the shared `CodexProcessInvoker` passes neither override.
- Objective and boundary: a run-scoped, durable, explicit Codex preference
  (`modelId`, optional `reasoningEffort`) may be set or cleared through one
  protected command/API and cockpit control. Saving a non-null choice requires
  one fresh bounded observation through the already-vetted catalog adapter:
  the model must be one visible observed id, and a non-null effort must be in
  that entry's own known supported set. An Unknown/unavailable catalog never
  validates a new choice; clearing remains possible. No value is auto-selected,
  including the catalog's suggested default. A change while an attempt is
  active affects only later claims. At each of the three Codex claim paths,
  snapshot the run's current requested pair into the existing immutable
  `Attempt` assignment facts and make dispatch use that attempt snapshot,
  including after restart. The shared Codex invoker adds only fixed `--model`
  and `--config model_reasoning_effort=<validated effort>` arguments when
  requested; null values preserve the existing argument list. Existing
  sandbox, ephemeral, ignore-user-config, schema, stdin, limits, and cleanup
  controls remain fixed. The cockpit distinguishes the run's *requested for
  future attempts* setting from each attempt's requested assignment and any
  genuinely provider-observed value; it never labels a request effective.
- Exclude Claude selection, permission-mode changes, arbitrary CLI config,
  profile/user-config loading, CLI-default inference, automatic choice of the
  catalog's suggested default, provider capability/preflight claims, account-
  allowance thresholds, invocation-eligibility guarantees, retry/fallback,
  provider-session resume, context/compaction, and new provider calls or model
  turns for testing. Do not derive an observed model or effort from the
  requested pair, catalog, process exit, or CLI default. Do not broaden the
  run policy or workflow authority through model selection.
- Stop gates: first verify the fixed `--model` and
  `--config model_reasoning_effort` invocation shape against the current
  official documents and the installed-build help evidence. If the executor
  lacks a local CLI, cite the planner's verified installed-build observation
  explicitly as a carried-over limit rather than claiming a local check.
  Stop if a safe bounded catalog recheck cannot validate a new choice; if a
  requested pair cannot be durably captured before dispatch and reused after
  recovery; if current Codex argv/security behavior cannot be preserved for
  null preferences; or if a live authenticated provider invocation becomes
  necessary. Report an evidence or design gap rather than weakening these
  gates. No automated test may contact a real provider.
- Acceptance evidence: cover setting, changing, and clearing a run preference;
  invalid/stale/hidden model, Unknown or duplicate effort list, mismatched
  effort, absent target, and concurrent active-attempt behavior; historical
  runs defaulting to no explicit override; durable event and state consistency;
  claim-time snapshots for all three Codex roles, including a later preference
  change and restart; exact safe argv with and without overrides for every
  Codex adapter; no changes to Claude arguments; API authentication,
  authorization, CSRF, and result mapping; cockpit loading, Unknown, save
  failure, and clear behavior. Run affected and relevant full backend/frontend
  tests, typecheck, lint, build, migration/NSwag drift checks, local links,
  and `git diff --check`; report exact commands and outcomes. Update
  `current-work.md` as a commit-ready delivery entry, making its earlier
  "has not selected another slice" statement historical, with actual checks
  and residual risks, but without claiming publication. Return a complete
  **uncommitted, unpushed** diff for GO/NO-GO. Selection is not commit/push GO.

### Executor prompt for this selected slice

```text
Implement the selected Increment 4 slice: explicit, run-scoped Codex model and reasoning-effort requests for future Codex Agent attempts.

Preflight: expect branch main, HEAD 8f5c2a992e0fa4f3ff47b8bb5919529cbb5f5bd4, local and live origin/main at the same SHA, nothing staged, exactly docs/roadmap/planner-handoff.md modified but unstaged by this planner selection, and nothing untracked. Verify these facts once before editing; stop and report a material discrepancy.

Objective: let the owner explicitly request a Codex model and optional reasoning effort for this run's later Planner, Challenge Resolver, and Code Reviewer attempts. Keep current preference separate from each attempt's immutable requested assignment and from provider-observed facts. Never infer an effective model, effort, capability, account eligibility, or CLI default.

Implement one protected Application command, MVC endpoint, generated NSwag client, durable Run preference and event, and a cockpit control using the existing read-only Codex model catalog. A new non-null preference must be validated server-side against one fresh, bounded observation from the already-vetted launch target: the chosen model is a visible observed id; an optional effort belongs to that model's own known supported set. Do not auto-select the catalog's suggested default. Reject non-null selection when observation is Unknown or the pair is invalid; permit clearing without a provider read. Bound and validate all persisted strings independently of the UI. Changes during an active attempt take effect only at a later claim, with truthful UI wording.

At all three Codex claim paths, copy the Run's requested pair into the existing immutable Attempt assignment fields in the same durable claim. Every supervisor and adapter must use the claimed Attempt's pair, including after restart, never a later mutable Run value. Extend the shared CodexProcessInvoker only with fixed --model <validated id> and --config model_reasoning_effort=<validated effort> arguments when explicitly requested. Preserve the exact existing argv, sandbox, --ephemeral, --ignore-user-config, schema, stdin, output/time bounds, and process cleanup when no override is set. Keep observed assignment null unless an authoritative provider output actually reports it. Preserve Claude paths and permissions.

Before changing the invoker, verify the fixed override shape against the current official Codex developer commands/config reference and the installed codex exec --help evidence cited in planner-handoff.md. If your environment lacks a local CLI, explicitly carry forward the planner's installed-build observation and flag that limit; do not claim local or authenticated verification. Stop and report if catalog validation, claim-time durability/recovery, or the existing safety contract cannot be maintained without a live model turn, generic config escape hatch, or wider policy change.

Test durable set/change/clear and historical null behavior, valid and invalid catalog pairs, no target/Unknown, active-attempt boundary, claim snapshots across all three Codex roles, change-after-claim and restart dispatch, exact argv with and without requested values, unchanged Claude invocations, protected endpoint and CSRF behavior, and cockpit observed/Unknown/save-failure/clear states. Use deterministic local fakes only; never invoke a real provider in automated tests. Run relevant focused and full backend/frontend checks, typecheck, lint, production build, EF migration and NSwag regeneration/drift checks, local documentation links, and git diff --check. Report actual commands and results.

Update architecture/product documentation only for the delivered request semantics. Make docs/roadmap/current-work.md commit-ready from the verified parent with checks and remaining risks, and mark its earlier no-next-slice statement as historical. Present the complete uncommitted, unpushed diff, changed files, test evidence, and blockers to the planner for GO/NO-GO. Keep corrections in this executor chat. Do not select another slice, commit, or push before explicit GO.
```

After a future GO, give one publication instruction covering the reviewed
substantive commit (including `current-work.md`), a normal fast-forward push of
`main` to `origin/main`, live-remote verification at the delivered SHA, and
only any tightly factual documentation closure needed to record that SHA and
verified publication. Re-review material or out-of-scope changes; stop for a
failed push or remote divergence without force-push or history reconciliation.

## Prior decision (2026-09-28): selected Codex model and effort catalog observation

- Third uncommitted diff review: **GO for publication of this reviewed slice.**
  Verified branch `main`, `HEAD`, local `origin/main`, and live `origin/main`
  all at `c2e5023e3c4a653856b5691d52c1d27e07844826`; nothing staged, 11
  modified tracked files (including this planner note), and 20 untracked
  files. The bounded correction now rejects U+061C ARABIC LETTER MARK,
  includes a deterministic fallback case, and expresses bidi characters as
  reviewable `\uXXXX` escapes; an independent read found no raw bidi code
  points in the adapter or its test. `current-work.md` now records the
  executor's actual frontend result, 657/657. Independently reran the focused
  catalog and allowance Infrastructure tests: 55/55 passed. The executor
  reports full Infrastructure 506 passed / 1 pre-existing skip, clean
  Infrastructure and API builds with stable NSwag output, `git diff --check`
  apart from the pre-existing generated-client line-ending warning, and the
  earlier unaffected full backend/frontend checks recorded in
  `current-work.md`. The documented absent-local-CLI and no-live-authenticated-
  call limits remain explicit. The reviewed diff stays within the selected
  read-only observation boundary.
- Publication instruction for this GO: commit the reviewed substantive diff,
  including `current-work.md` and this planner decision, on `main`; push
  `main` to `origin/main` with a normal fast-forward push; verify the live
  remote branch points to the delivered commit and report its SHA, Git state,
  and checks actually run. Then make only any tightly bounded factual
  documentation closure needed to record the delivered SHA and verified
  publication, publish that closure normally, and verify the live remote
  again. If the diff becomes material or leaves the approved scope, return it
  for re-review before committing. If the push fails or the remote diverges,
  stop and report it; do not force-push or reconcile remote history.
- Second uncommitted diff review: **NO-GO; make a narrow correction in this
  same slice without committing or pushing.** Verified `main`, `HEAD`, local
  `origin/main`, and live `origin/main` at
  `c2e5023e3c4a653856b5691d52c1d27e07844826`; nothing staged, 11
  modified tracked files (including this note), and 20 untracked files.
  Independently reran the focused catalog and allowance Infrastructure tests:
  54/54 passed; `git diff --check` has only the pre-existing generated-client
  line-ending warning. The closed method-specific channel, one-type-per-file
  extraction, whole-field Unknown effort semantics, default membership check,
  exact outbound request assertions, and historical handoff wording address
  the first review. One unsafe display-name case remains: U+061C ARABIC LETTER
  MARK is a Unicode bidirectional mark but is absent from
  `BidiFormattingCharacters`, so it passes the current control-character and
  explicit-list checks. Reject U+061C as well, use escaped Unicode code points
  for the bidi set and test inputs so the source is reviewable, and add a
  deterministic U+061C fallback test. Also correct the frontend full-suite
  count in `current-work.md` from 655/655 to the executor's reported 657/657;
  retain only checks actually run. Run the affected tests, then relevant full
  validation for any code behavior that changed, and return the complete
  uncommitted, unpushed diff. No commit/push GO is granted.
- First uncommitted diff review: **NO-GO; correct this same slice without
  committing or pushing.** Verified `main`, `HEAD`, local `origin/main`, and
  live `origin/main` at `c2e5023e3c4a653856b5691d52c1d27e07844826`;
  nothing staged, 11 modified tracked files (including this note), and 19
  untracked files. Inspected the shared App Server extraction, catalog parser,
  Application/API/UI mapping, tests, and handoff; independently passed focused
  `CodexModelCatalogAdapterTests` 18/18 and `git diff --check` (only the
  existing generated-client line-ending warning). Three bounded corrections
  are required. First, `CodexAppServerSession.cs` contains both
  `CodexAppServerSession` and the top-level `CodexAppServerChannel`; the
  engineering contract requires one top-level C# type per file. Move the
  channel to its own file and replace its arbitrary `WriteLineAsync(string)`
  surface with a closed, read-only request path limited to the two reviewed
  methods; an internal raw-JSON writer is still a generic RPC escape hatch.
  Second, the parser
  silently skips malformed `supportedReasoningEfforts` entries and still
  presents the remainder as the model's supported set. Treat an invalid or
  duplicate effort entry as an Unknown effort list or fail the observation
  closed; never show a partial set as complete. A syntactically valid default
  absent from a known supported set must become Unknown or fail closed, never
  appear as a supported suggestion. Third, `displayName` currently accepts
  bounded but unsafe text, including control and bidirectional-formatting
  characters; reject those and fall back to the already validated id (or fail
  closed). Add deterministic negative cases for those conditions and update
  architecture/product/handoff descriptions to match the corrected semantics.
  In `current-work.md`, make the previous checkpoint's "has not selected
  another slice" sentence explicitly historical so it remains true when this
  delivery is committed.
  Also make the model-list outbound request test assert the exact request JSON
  lines, including `limit`, so the sole permitted method and parameters are
  reviewable. Run affected checks first, then relevant full validation because
  the shared session and parser are changing. Return the complete uncommitted,
  unpushed diff for re-review; no commit/push GO is granted. The executor's
  absence of a local Codex CLI is a disclosed evidence limit, not a reason to
  assert an authenticated provider result; the planner's installed-build
  schema evidence remains the narrow protocol basis for this slice.
- Verified selection baseline: branch `main`; `HEAD`, local `origin/main`, and
  live `origin/main` all `c2e5023e3c4a653856b5691d52c1d27e07844826`;
  staged, unstaged, and untracked state empty before this planner-only edit.
  The read-only Codex allowance slice was published in
  `3c120b41b3366de70f221479b2545183ebb79fda` and factually closed by
  this baseline. The GO below is historical, not authority for this slice.
- Select exactly one Increment 4 slice: **read-only Codex model and reasoning-
  effort catalog observation** in the existing cockpit. The official
  [Codex App Server `model/list` contract](https://learn.chatgpt.com/docs/app-server#list-models-modellist)
  returns client/account-specific picker-visible models and each model's
  `supportedReasoningEfforts` and `defaultReasoningEffort`; its examples are
  illustrative, never a permanent catalog. The installed
  `codex-cli 0.158.0-alpha.2.1` generated stable App Server schemas without
  `--experimental`: `ClientRequest.json` requires `id`, `method`, and `params`
  for `model/list`; `v2/ModelListParams.json` permits bounded cursor paging and
  `includeHidden`; `v2/ModelListResponse.json` defines the model/effort fields.
  This is protocol evidence for that build, not an authenticated live result
  or proof that a model can start a specific invocation. The existing
  `CodexAccountAllowanceAdapter` proves a bounded local stdio App Server
  handshake and cleanup pattern, but its allowance data grants no model or
  invocation authority. The reviewed [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents model/effort input flags; it does not establish an equivalent
  safe account-specific model-list read for this slice.
- Objective and boundary: through the same durable, vetted Codex launch target,
  make one fresh, protected, read-only `model/list` observation using explicit
  stdio JSONL, `initialize`/`initialized`, bounded cursor paging, finite time
  and output caps, strict correlated replies, and process-tree cleanup. Expose
  only bounded model ID, display name, supported effort identifiers, suggested
  default effort, and retrieval time through a project-owned Application query,
  one authorized MVC endpoint, generated NSwag client, and a manual-refresh
  cockpit panel. Show explicit Unknown on no vetted target, unsupported method,
  missing auth, malformed/duplicate/conflicting pages, unsafe strings, timeout,
  failed process cleanup, or absent usable entries; caller cancellation must
  propagate after cleanup. Never reuse a stale observed timestamp after a
  failed refresh. Reuse or narrowly extract the
  allowance adapter's vetted process/protocol mechanics, preserving its wire
  behavior and bounds; no general-purpose arbitrary App Server RPC interface.
- Exclude selection, run intent, persistence/migrations, attempt assignment,
  claim/dispatch gating, provider invocation arguments, CLI defaults, account
  allowance thresholds, Claude observation, context/compaction, provider-
  session resume, direct HTTP/auth-file access, and any thread/turn method.
  The UI must label this as a catalog observation, not selected/effective
  configuration, authentication readiness, invocation eligibility, or an
  assurance that a listed model is still available at dispatch.
- Stop gates: if the executor cannot verify the exact non-experimental
  `model/list` wire shape against the official page and installed-build schema
  evidence; if the already-vetted target or bounded process cleanup cannot be
  preserved; if implementation or tests require a live authenticated provider
  call, model turn, arbitrary config/command execution, a security-policy change, or model
  selection; or if the existing allowance contract would regress. Report the
  blocker without widening scope. No automated test may contact a real
  provider.
- Acceptance: deterministic local fake App Server tests prove the exact
  handshake and `model/list` requests, picker-visible entries, effort/default
  mapping, finite paging, empty/partial/malformed/duplicate/conflicting
  replies, notifications, output limits, timeout/cancellation/child cleanup,
  and no sensitive-field disclosure. Application and API tests prove vetted-
  target/Unknown mapping, authorization, and bounded response; frontend tests
  prove observed options, retrieval freshness, Unknown, and failed refresh.
  Regress the unchanged account-allowance adapter if transport is extracted.
  Run affected and relevant full backend/frontend suites, typecheck, lint,
  production build, NSwag drift verification, local link check, and
  `git diff --check`; report exact results. Make `current-work.md` commit-ready
  from the verified parent, with actual checks and remaining risks, but no
  invented delivery SHA. Return the complete **uncommitted, unpushed** diff
  for planner GO/NO-GO. Selection grants no commit/push authority.

### Executor prompt for this selected slice

```text
Implement the selected read-only Codex model and reasoning-effort catalog observation slice.

Preflight: expect branch main and HEAD c2e5023e3c4a653856b5691d52c1d27e07844826. Expect nothing staged, exactly docs/roadmap/planner-handoff.md modified but unstaged by the planner selection, and nothing untracked. Verify branch, HEAD, local and live origin/main, and staged/unstaged/untracked state once before editing; stop and report a material discrepancy.

Objective: show a fresh, bounded, picker-visible Codex model catalog and each model's supported and suggested-default reasoning effort in the existing cockpit, with retrieval time, manual refresh, and explicit Unknown. Treat this as read-only catalog evidence, not selected or effective configuration or invocation eligibility.

Scope: use the same durable vetted Codex CLI launch target and the documented App Server stdio JSONL initialize/initialized handshake, then only model/list with includeHidden false and bounded cursor paging. Validate correlated response IDs and strict response shapes; cap page count, entries, strings, line and total output, and time; preserve cancellation and child-tree cleanup. Reuse or narrowly extract the current allowance adapter's safe process/protocol mechanics without changing its allowance contract. Add a project-owned Application query/port/projection, one protected MVC endpoint and response, regenerated NSwag client, cockpit catalog panel/hook and focused tests. Update the architecture and cockpit specifications for exactly the delivered observation.

Exclusions: do not add model/effort selection, run intent, schema migration, attempt assignment or invocation flags, claim/dispatch policy, provider-preflight capability claims, account threshold/stop policy, Claude catalog, context/compaction, session resume, thread/turn calls, direct provider HTTP, auth-file reads, or a generic RPC escape hatch. Do not infer any CLI default or guarantee account authentication or invocation eligibility from a catalog response.

Stop gates: verify the non-experimental model/list request/response against the official Codex App Server documentation and the installed-build generated schema before implementing the parser. Stop and report if that shape, a vetted local launch target, strict bounded paging, or safe cleanup cannot be established; if implementation or tests require a live authenticated provider call, a model turn, or a policy/invocation change; or if the existing allowance behavior cannot be preserved. Use local fake App Server tests only.

Acceptance evidence: cover exact outbound handshake/read lines; valid multi-page visible models with supported/default effort; hidden, empty, malformed, duplicate, conflicting, unsolicited, oversized, timeout, cancellation, and process-tree cases; no raw provider payload, account, credential, or diagnostic disclosure. Cover Application Unknown and target mapping, API authorization and response bounds, and frontend observed/Unknown/freshness/failed-refresh behavior. Regress allowance tests if shared transport changes. Run the relevant focused and full backend/frontend checks, typecheck, lint, production build, NSwag regeneration/drift check, local documentation link check, and git diff --check, and report the exact commands and outcomes.

Update docs/roadmap/current-work.md as a concise commit-ready delivery entry based on the verified parent, naming actual checks, remaining risks, and post-publication verification without claiming publication or embedding the future commit SHA. Present the complete uncommitted, unpushed diff, changed files, checks, and blockers to Codex for GO/NO-GO. Keep review corrections in this executor chat. Do not choose another slice or commit/push before explicit GO.
```

After a future GO, give one publication instruction covering the reviewed
substantive commit (including `current-work.md`), a normal fast-forward push of
`main` to `origin/main`, live-remote verification at the delivered SHA, and
only any tightly factual documentation closure needed to record that SHA and
verified publication. Re-review material or out-of-scope changes; stop for a
failed push or remote divergence without force-push or history reconciliation.

## Prior decision (2026-09-27): Codex allowance observation GO for publication

- After Claude reached its credit limit, the owner asked Codex to finish this
  correction. Codex completed the same slice and reviewed the complete
  uncommitted diff: **GO for publication**. Before staging, `main`, `HEAD`,
  local `origin/main`, and live `origin/main` remain
  `ba6fdd096b217dfe1e46d6eb5541853325fce6c6`, with 10 modified tracked
  files, 22 untracked files, and nothing staged. The corrected parser now
  follows the documented bucket map and Unix-seconds windows; the positive
  fixture checks the exact three outbound protocol lines. Complete and split
  oversized JSONL lines are discarded; timeout and cancellation tests prove
  the fixture process and its child exit. API/UI tests cover observed buckets,
  Unknown, and failed-refresh freshness. Infrastructure focused 30/30 and
  full 481 passed / 1 pre-existing skip; Application 1022/1022; API 326/326;
  Domain 529/529; Architecture 9/9; frontend 639/639; typecheck and build
  clean, lint exit 0 with 20 pre-existing warnings. NSwag regeneration is
  additive and repeatable, documentation links resolve, and `git diff --check`
  is clean apart from the existing generated-client CRLF warning. Commit the
  complete reviewed diff including this note and commit-ready
  `current-work.md`; normal fast-forward push and verify live `origin/main`.
  A tightly factual `current-work.md` closure may record the delivered SHA
  after publication without another review. Stop for a material diff, failed
  push, or remote divergence. No next slice is selected.

- First uncommitted diff review: **NO-GO; keep this same slice uncommitted and
  unpushed**. Reverified `main`, `HEAD`, local and live `origin/main` at
  `ba6fdd096b217dfe1e46d6eb5541853325fce6c6`; 10 modified tracked files
  (including this planner note), 20 untracked files, nothing staged;
  `git diff --check` clean apart from the existing generated-client CRLF
  warning. The executor's reported suites passed, but their positive fixtures
  use a response shape that contradicts both the official App Server example
  and the installed CLI's generated schema. `rateLimitsByLimitId` maps each
  limit id (for example `codex`) to a snapshot containing `primary` and
  `secondary`; the legacy `rateLimits` is one snapshot. A window's `resetsAt`
  is a nullable Unix-seconds integer, not an ISO string; `windowDurationMins`
  is nullable. The current parser instead looks for windows directly under
  the map and requires an ISO string, so a documented valid response yields
  `Unknown`. Correct the bounded multi-bucket projection, wire-accurate
  fixtures, and related API/UI/docs in this same slice: preserve a valid
  `usedPercent` while representing documented null duration/reset fields as
  unknown, and expose no invented aggregate across buckets. Also enforce the
  scanner's per-line limit for complete and split oversized lines, prevent a
  failed refresh from retaining an observed timestamp/status, reject invalid
  frontend percentages rather than clamp them into plausible values, and test
  actual child-tree termination for timeout and cancellation. Recheck strict
  initialize-response validation and finite cleanup before resubmission.
  Rerun affected checks and the relevant full validation after these material
  corrections. This decision grants no commit or push authority.

- Verified publication baseline before this planner edit: branch `main`,
  `HEAD`, local `origin/main`, and live `origin/main` all
  `ba6fdd096b217dfe1e46d6eb5541853325fce6c6`; staged, unstaged, and
  untracked state empty. The Codex correlation repair was delivered in
  `c04cebf59d85483af8bcfa3f280bd440598a7d76`; this baseline is its
  factual `current-work.md` closure.
- Select one larger but bounded Increment 4 slice: **read-only Codex ChatGPT
  account-allowance observation**. The earlier account-allowance candidate was
  closed because the CLIs lacked a proven safe machine-readable observation
  contract. New primary-source evidence changes the Codex side only: the
  [official Codex App Server protocol](https://learn.chatgpt.com/docs/app-server)
  documents the `account/rateLimits/read` JSON-RPC method, its
  `rateLimitsByLimitId` multi-bucket view and legacy `rateLimits` fallback,
  `primary`/`secondary` windows, `usedPercent`, `windowDurationMins`, and
  `resetsAt`, via CLI-managed authentication. It also documents the required
  `initialize`/`initialized` handshake and stdio JSONL transport. A local
  `codex app-server --help` check showed stdio support on the planner host;
  this is supporting evidence, not a claim about every installed version or
  account. No equivalent safe Claude Code CLI allowance contract was found.
- Executor stop-gate revalidation: Claude's environment lacked a `codex` CLI
  and its documentation retrieval did not show the cited method, so it
  correctly stopped without editing. The planner reopened both
  `https://learn.chatgpt.com/docs/app-server` and
  `https://learn.chatgpt.com/codex/app-server` (which redirects to the former):
  the official page's **Rate limits (ChatGPT)** section explicitly shows
  `{ "method": "account/rateLimits/read", "id": 6 }`, a result with
  `rateLimitsByLimitId`, and `usedPercent`, `windowDurationMins`, and
  `resetsAt` windows. Independently, installed `codex-cli 0.158.0-alpha.2.1`
  on the planner host generated the stable App Server JSON schema with
  `codex app-server generate-json-schema --out <temporary directory>` (no
  `--experimental`): `ClientRequest.json` contains the method with required
  `id`/`method` and optional/null `params`; `v2/GetAccountRateLimitsResponse.json`
  defines the multi-bucket map and window fields. This verifies the wire
  contract for that installed build, not the executor's absent CLI or an
  authenticated account result. Continue the same selected slice using these
  exact sources; retain every installed-target, protocol, auth, and process
  stop gate above. Do not treat the fake-server fixture as proof of a live
  provider result.
- Objective: from an authenticated, protected, read-only MVC query, obtain a
  fresh Codex allowance snapshot through the already-vetted Codex CLI launch
  target and a narrowly bounded Infrastructure App Server adapter; render the
  observed Codex windows, retrieval time, and explicit Unknown state in the
  existing cockpit usage rail. Use an Application-owned provider-neutral
  snapshot model and port; keep JSON-RPC wire types in Infrastructure. Prefer
  `rateLimitsByLimitId` when present, otherwise use the legacy single-bucket
  view, without double-counting. Project only bounded, validated window
  fields needed for display; never copy credentials, account email, credits,
  arbitrary provider payloads, or raw diagnostics into API, logs, storage,
  or UI. Missing auth, unsupported method, absent/malformed windows, timeout,
  and process failure must remain Unknown, never zero or a guessed allowance;
  cancellation must clean up the child and propagate. State clearly that this is a read-only snapshot, not an
  enforceable stop threshold or a guarantee that a specific invocation can
  start.
- Boundaries: one exact local CLI launch target (including the existing
  direct-executable/Node-script distinction), an explicit stdio-only
  `codex app-server` handshake and `account/rateLimits/read` request, finite
  timeout/output caps and child-tree cleanup, strict response-id and shape
  validation, Application query, one protected MVC endpoint/response,
  generated NSwag client, existing cockpit usage rail, focused tests, product
  specification, and commit-ready `current-work.md`. Revalidate the vetted
  target before launch; never search PATH, open a listening socket, use a
  shell, read CLI auth files, supply tokens, start a thread/turn, or send any
  mutating App Server method. Do not broaden the generic one-shot process
  contract to pretend it supports duplex JSON-RPC; a narrow Infrastructure
  process boundary is allowed only if it preserves equivalent path, timeout,
  environment, capture, cancellation, and kill-tree protections.
- Exclude Claude allowance, threshold configuration, warning/stop enforcement,
  claim/dispatch gating, persisted allowance schema, scheduled polling,
  provider preflight capability claims based only on CLI version, model/effort
  selection, session resume, context/compaction, direct authenticated HTTP
  calls, and unrelated workflow policy. Do not set preflight `AccountUsage` to
  Supported merely because a version probe succeeded. Stop and report for
  replanning if the installed target cannot safely perform the documented
  read-only protocol, App Server requires experimental or mutating methods,
  auth isolation would require token access, the process cannot be bounded
  and cleaned up, or truthful display requires a broader architecture/ADR
  change. No Unknown-only scaffolding: a valid fixture must produce visible
  positive allowance windows.
- Acceptance: deterministic local fake App Server tests verify exact handshake
  and sole read method, normal multi-bucket and legacy shapes, nullable
  primary/secondary windows, malformed/duplicate/conflicting replies,
  unsolicited notifications, oversized output, missing auth/unsupported
  method, timeout/cancellation/cleanup, and no sensitive-field disclosure.
  Application/API tests prove authorized snapshot/Unknown mapping; frontend
  tests prove real window display, freshness and Unknown without treating
  missing data as zero. Never call a real provider from automated tests. Run
  affected and relevant full backend/frontend suites, typecheck, lint, build,
  NSwag drift check, and `git diff --check`; report exact outcomes. Make
  `current-work.md` commit-ready from the verified parent with actual checks,
  open risks and no invented SHA. Return the full **uncommitted, unpushed**
  diff for Codex GO/NO-GO. Selection grants no commit/push GO. Following a
  future GO, one publication instruction covers substantive commit, normal
  fast-forward push, live-remote verification, and tightly factual
  documentation closure; stop for material change, failed push or divergence.

## Prior decision (2026-09-27): Codex session correlation repair

- Corrected uncommitted diff review: **GO for publication of this reviewed
  slice**. Reverified `main`, `HEAD`, local `origin/main`, and live
  `origin/main` at `c481aead3ca652cf06fffaa509bb68f411f483ff`; eight
  unstaged tracked modifications including this planner note, with nothing
  staged or untracked. The Resolver and CodeReviewer persistence cases now
  use their valid successful result paths and clean-exit evidence. The shared
  adapter tests now include malformed JSONL and an event cut off mid-object
  at the 4096-character scan prefix; the new documentation and test wording
  correctly distinguish characters from bytes. Codex independently reran
  focused Infrastructure tests 46/46 and Application tests 71/71;
  `git diff --check` passed. Claude reported full Infrastructure 451/452
  (one pre-existing unrelated skip), Application 1015/1015, and applicable
  earlier API 322/322, Domain 529/529, and Architecture 9/9. Include this
  planner note and the commit-ready `current-work.md` in the substantive
  commit, then normal fast-forward push and live-remote verification. A
  narrowly factual `current-work.md` closure may record the delivered SHA
  and verified publication in a follow-up documentation-only commit/push
  without another review. Stop and report if the substantive diff changes
  materially, publication fails, remote history diverges, or closure leaves
  those factual bounds. No next slice is selected here.
- First uncommitted diff review: **NO-GO, bounded correction in the same
  slice**. Verified `main`, `HEAD`, local `origin/main`, and live
  `origin/main` at `c481aead3ca652cf06fffaa509bb68f411f483ff`; no staged
  or untracked files, eight unstaged tracked files including this planner
  note. The shared extractor and three adapter positive cases match the
  documented `thread.started/thread_id` contract, and the reported suites
  are green. The two new result-persistence tests supply a thread ID with
  `ProviderInvocationFailed`, while `CodexProcessInvoker` only emits one on
  successful process exit. Prove durable attachment on each role's real
  successful result path using its valid response and clean-exit evidence;
  keep absent/blank cases focused. The selected negative matrix explicitly
  included malformed and partial JSONL, but the submitted tests cover
  missing/non-string/oversized IDs and an event beyond the scan window, not
  malformed or cut-off event lines; add representative deterministic cases.
  Finally, the existing scan is `standardOutput.Length` over a 4096-character
  prefix, despite the `MaxSessionIdScanBytes` name. Correct the new
  documentation and tests' wording so they do not claim a byte-accurate
  4 KiB limit; preserve the existing scan behavior and bounds in this slice.
  Update the commit-ready `current-work.md` with corrected facts and actual
  checks. Run affected tests and `git diff --check`, then return the full
  uncommitted, unpushed diff. No commit/push GO. Stop for review if a
  successful result cannot carry and durably record the ID without changing
  excluded behavior.
- Verified baseline before this planner edit: branch `main`, `HEAD`, local
  `origin/main`, and live `origin/main` all
  `c481aead3ca652cf06fffaa509bb68f411f483ff`; no staged, unstaged, or
  untracked files. The preceding Claude CriticalReviewer/ReviewCorrection
  delivery is `caf45d33396617ca640cb766fe5bc984c93b488c`; the baseline is
  its factual `current-work.md` closure.
- Select exactly one Increment 4 slice: repair **Codex provider-session
  correlation for the three current read-only roles** (Planner, Resolver,
  CodeReviewer). The shared `CodexProcessInvoker` currently scans successful
  `codex exec --json` stdout for an arbitrary `session_id` property, and all
  three adapter tests use an invented `session_meta` event. The
  [current official Codex non-interactive contract](https://learn.chatgpt.com/docs/non-interactive-mode)
  documents `thread.started` with `thread_id`. Parse that exact event and
  field from the already-captured bounded JSONL, then pass the observed value
  through each existing adapter and result command into the existing
  `Attempt.AgentProviderSessionId` column. This is a provider-reported
  correlation reference for an attempt, never resume eligibility or authority
  to resume. The official [Codex CLI reference](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
  says the configured `--ephemeral` run does not persist rollout files; do not
  infer a stronger resume claim from that flag.
- Boundary: the shared Codex stdout extractor, focused deterministic adapter
  tests for all three roles, focused result-persistence tests where currently
  missing, and concise product/roadmap documentation plus a commit-ready
  `current-work.md`. Keep the existing 4096-character scan and 256-character storage
  bound, and the current success-only recording behavior. Accept only a
  complete JSON object with `type: "thread.started"` and a nonblank bounded
  string `thread_id`; ignore unrelated events and the old arbitrary
  `session_id` shape. If distinct valid IDs appear in the bounded window,
  fail closed to Unknown rather than choosing one. Do not add schema,
  migrations, status/HTTP/generated-client/frontend fields, raw-ID display,
  provider invocation flags, preflight, resume/open/fork actions, claim or
  dispatch policy, other provider adapters, model/effort, context/compaction,
  or account-usage controls. Do not revive the closed account-allowance
  candidate without a newly proven observation contract.
- Stop gates: the official event contract cannot be reconciled with the
  captured/redacted/truncated output contract; safe correlation requires
  broadening the capture or exposing the ID; a result path cannot persist the
  value without changing its existing lifecycle or authorization; or the
  change requires any excluded behavior or an ADR reversal. Report evidence
  and return for replanning at such a gate. Do not fabricate a provider event
  or infer a value from CLI defaults.
- Acceptance evidence: deterministic tests for all three adapters with
  documented `thread.started/thread_id`, and focused negative cases for
  unrelated `session_id`, malformed/partial/oversized IDs, conflicting IDs,
  and non-success exits. Prove the existing result handlers durably attach the
  observed ID to the correct attempt for Resolver and CodeReviewer as well as
  the already-covered Planner, without HTTP disclosure. Run affected tests
  first, then relevant full backend suites and `git diff --check`; report
  exact outcomes and any skipped checks. Keep `current-work.md` commit-ready
  from the verified parent with actual checks, remaining risks, and no
  invented delivery SHA or transient pre-publication wording. Return one
  complete **uncommitted, unpushed** diff for Codex GO/NO-GO. This selection
  grants no commit/push GO. After a future GO, one instruction will cover the
  substantive commit, normal fast-forward push, live-remote verification,
  and narrowly factual `current-work.md` SHA closure; stop for review if the
  change becomes material, the push fails, or the remote diverges.

## Prior decision (2026-09-27): Claude CriticalReviewer/ReviewCorrection

- **GO for publication of the reviewed Claude CriticalReviewer/ReviewCorrection
  slice**, with one bounded comment-only cleanup before the substantive
  commit. Reverified `main`, `HEAD`, local `origin/main`, and live
  `origin/main` at `6312438046e9da0a43e2231ecc76f3e8eee62ac6`; no
  staged or untracked files, 27 tracked modifications including this planner
  note. Both fact guards now require their exact response contract; the new
  persisted CriticalReviewer mismatch test proves all five facts stay null.
  Focused Application status tests passed independently (15/15), and
  `git diff --check` found no errors apart from the existing generated-client
  line-ending warning. The executor reports the other full suites and
  correction checks in `current-work.md`. Immediately before committing,
  remove or rewrite only the stale sentence in the ReviewCorrection handler's
  later per-attempt-check comment that says an unrelated corrupt attempt on
  the run can never fail this status; the earlier run-wide snapshot catch can
  do exactly that. This is a comment-only correction and needs no extra
  functional review if no other substantive code or contract changes occur.
  Include the commit-ready `current-work.md` and this planner note in the
  substantive commit; authorize normal fast-forward push, live-remote
  verification, and a tightly bounded factual `current-work.md` SHA closure.
  Stop for review if code behavior changes materially, the push fails, or the
  remote diverges. No next slice is selected here.

- First uncommitted Claude CriticalReviewer/ReviewCorrection diff: **NO-GO,
  narrow correction in the same slice**. Verified `main`, `HEAD`, local
  `origin/main`, and live `origin/main` at
  `6312438046e9da0a43e2231ecc76f3e8eee62ac6`; no staged or untracked
  files, 27 tracked modifications including this planner note. The reported
  suites are green and `git diff --check` found no errors. CriticalReviewer
  status queries by role but its configured-fact coherence guard omits
  `AgentResponseContract.CriticalReview`, contrary to the selected exact-path
  condition: a valid row with that role and a different response contract can
  receive all five current adapter facts. Include the response-contract guard
  and a focused persisted mismatch test proving all five facts stay null.
  ReviewCorrection already selects only `ReviewCorrection` contract attempts;
  make that exact contract explicit in its fact guard for consistency without
  changing lineage selection. Correct the ReviewCorrection handler comment
  claiming unrelated corrupt attempts cannot fail this status: its new
  `LoadSnapshotAsync` catch returns `invalid_assignment` when any attempt in
  the run has an unparseable assignment enum. Keep documentation aligned if
  affected. Preserve every other implementation and the commit-ready
  `current-work.md`; run affected Application tests, relevant API tests if
  touched, then `git diff --check`. Return the complete uncommitted, unpushed
  diff for review. No commit/push GO.

- Verified next-slice baseline after publication: `main`, `HEAD`, local
  `origin/main`, and live `origin/main` all equal
  `6312438046e9da0a43e2231ecc76f3e8eee62ac6`; staged, unstaged, and
  untracked state was empty before this planner note. The substantive Codex
  read-only-role slice is `1f69319614252ad4fb9e6bf1a8e93d61b33ac05d`;
  its factual `current-work.md` closure is this baseline.
- Selected one coherent, larger Increment 4 slice for the two remaining
  current Claude Code paths: CriticalReviewer and Implementer ReviewCorrection.
  Persist claim-time assignment provenance on each existing `Attempt` using
  existing nullable columns: `ClaudeCode`, `ReadOnly` for CriticalReviewer and
  `WorkspaceEditOnly` for ReviewCorrection, with distinct fixed adapter
  contract versions `claude-critical-review-v1` and
  `claude-review-correction-v1`. The `ReadOnly` enum description must become
  provider-neutral and describe assigned read-only workspace intent without
  claiming effective isolation or absence of shell/network/MCP actions. Keep
  requested and observed model/effort null; do not infer CLI defaults. Legacy
  null-column rows remain Unknown, never backfilled or reinterpreted.
- On each role's existing attempt status and cockpit action, disclose five
  configured adapter facts only for a coherent current assignment of the exact
  response contract: permission mode (`plan` for CriticalReviewer,
  `acceptEdits` for ReviewCorrection); provider-session persistence
  (`Disabled`); permission prompts (`None`); resume eligibility
  (`Ineligible`); and built-in tools (`None` for the CriticalReviewer adapter's
  explicit empty `--tools` argument, `Read,Edit,Write,Glob,Grep` for
  ReviewCorrection). The current adapters pass all these arguments explicitly.
  The [official Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents these flags, including that `--no-session-persistence` prevents
  resume and that `--tools` governs built-in tools but not MCP tools. Values
  describe configured CLI arguments, not observed effective access, an MCP or
  complete security boundary, model/effort, or invocation eligibility. No
  attempt or a valid historical/mismatched assignment produces null/Unknown;
  malformed assignment metadata fails the whole status closed with
  `agent_attempts.invalid_assignment` without leaking raw metadata.
- Boundaries: the two Domain claim factories and the `ReadOnly` enum comment;
  the two role-specific status projections, MVC responses/mappings, generated
  TypeScript client, cockpit actions, focused tests, cockpit specification, and
  two handoffs. Preserve ReviewCorrection's current-review and budget
  semantics. Extend its adapter's existing argument test to assert the fixed
  flags being disclosed; do not change adapter arguments. Keep role-specific
  response contracts and query ownership. Exclude invocation/preflight,
  claim/dispatch authorization and budgets, schema/migrations, other paths,
  session identifiers or resume actions, model/effort discovery or selection,
  context/compaction, account allowance, `--max-turns`, and fallback. Stop if
  the fixed flags or coherent historical/current distinction cannot be proved,
  ReviewCorrection's lineage query cannot fail closed without changing its
  eligibility semantics, or truthful disclosure needs excluded behavior or an
  ADR reversal. Do not add Unknown-only scaffolding.
- Acceptance: current, no-attempt, valid legacy/mismatch, and malformed
  persisted assignment cases for both paths; exact claim-time versions and
  null model/effort; unchanged ReviewCorrection budget/current-review behavior;
  strict API disclosure guards (including the permission-prompts field);
  whitelisted UI literals with Unknown fallback; exact adapter argument tests;
  NSwag drift checked. Run relevant Domain, Application, API, Infrastructure,
  Architecture, and frontend suites, typecheck, lint, build, and
  `git diff --check`, reporting exact outcomes. Make `current-work.md`
  commit-ready from the verified parent with actual checks and open risks,
  without a future SHA or transient pre-publication wording. Claude returns
  an uncommitted, unpushed complete diff for Codex GO/NO-GO. Selection grants
  no commit/push GO. After a future GO, one instruction covers the reviewed
  substantive commit, normal fast-forward push, live-remote verification, and
  tightly bounded factual SHA closure; a material change, failed push, or
  divergence stops for review.

## Prior decision: Codex read-only role assignment provenance

- **GO for publication of the reviewed Codex read-only-role slice**, including
  the narrow correction. `main`, `HEAD`, local `origin/main`, and live
  `origin/main` were reverified at
  `01ccd3b4639d4ded8311226bea13bca922a0717e`; no staged changes, 30
  tracked modifications and one untracked new Application test. The corrected
  permission-profile comment no longer asserts shell absence or effective
  isolation; `current-work.md` has no transient pre-publication claim or
  invented delivery SHA. `git diff --check` is clean apart from the existing
  generated-client line-ending warning, and the new CodeReviewer status
  query tests passed independently (7/7). The executor reports the full
  suites and correction checks recorded in `current-work.md`; the correction
  affected only a comment and handoff prose. Authorize committing this exact
  reviewed slice, a normal fast-forward push to `origin/main`, live-remote
  verification, and a tightly bounded factual `current-work.md` SHA closure.
  Stop for renewed review if the substantive diff changes materially, the
  push fails, or the remote diverges. No next slice is selected here.

- Review decision on the first uncommitted Codex read-only-role diff: **NO-GO,
  narrow correction in the same slice**. Verified `main`, `HEAD`, local
  `origin/main`, and live `origin/main` at
  `01ccd3b4639d4ded8311226bea13bca922a0717e`; no staged files, 30 tracked
  modifications (including this planner note) and one untracked new Application
  test file. The code and test shape matches the selected slice, and
  `git diff --check` found no errors. Correct the new
  `AgentPermissionProfile.ReadOnly` XML comment: its claim that no shell action
  is part of the assignment overstates the fixed Codex `--sandbox read-only`
  flag, which governs model-generated commands. Describe read-only workspace
  effect/configured sandbox without claiming shell absence or effective
  isolation. Make `current-work.md` commit-ready by removing its transient
  assertion that publication and remote verification have not happened yet;
  retain an accurate post-publication verification instruction and do not
  invent a future SHA. Reconcile the executor's 30-file summary with Git's
  30 modified tracked files plus one untracked test. Keep the correction
  bounded to these points, rerun affected checks and `git diff --check`, and
  return the complete uncommitted, unpushed diff for review. No commit/push GO.

- Verified new-slice baseline: `main`, `HEAD`, local `origin/main`, and live
  `origin/main` all point to `01ccd3b4639d4ded8311226bea13bca922a0717e`;
  staged, unstaged, and untracked state was empty before this planner note.
  The preceding configured Claude Implementer built-in-tools delivery is
  `90c84b79917aa681c25f04182ca485defdfb3357`, followed by its factual
  delivery closure at this baseline.
- Selected one larger, coherent Increment 4 slice: establish immutable
  assignment provenance for all three current Codex read-only roles (Planner,
  Resolver, CodeReviewer) at claim time, then disclose their configured command
  sandbox and session-rollout-file persistence on their existing attempt-status
  and cockpit actions. Each role keeps a distinct, fixed adapter contract
  version tied to its current adapter: `codex-planning-v1`,
  `codex-challenge-resolution-v1`, and `codex-implementation-review-v1`,
  respectively. The assignment records provider Codex, a concrete
  `AgentPermissionProfile.ReadOnly`, and null requested/observed model
  and effort because neither adapter arguments nor provider output establish
  those facts. Existing nullable columns support this without a schema change;
  pre-existing attempts retain their historical Unknown/null assignment
  fields. The shared `CodexProcessInvoker` already fixes `--sandbox read-only`
  and `--ephemeral`, and the existing three adapter tests assert those exact
  arguments. The [official Codex CLI reference](https://developers.openai.com/codex/cli/reference)
  documents the former as the sandbox policy for model-generated commands and
  the latter as running without persisting session rollout files to disk.
  Disclose only configured arguments: `configuredCommandSandbox: "read-only"`
  and `configuredRolloutPersistence: "Disabled"` for a coherent current
  assignment of the matching role; otherwise `null`/`Unknown` for no attempt
  or a valid historical/mismatched assignment. Invalid assignment metadata
  fails the status closed rather than returning a success containing Unknown.
  These facts are neither provider-observed effective isolation nor proof of
  provider-session resume eligibility or invocation eligibility.
- Scope: the three Domain claim factories and permission-profile enum; the
  three role-specific status projections, MVC responses/mappings, generated
  TypeScript client, cockpit actions, focused tests, and cockpit specification;
  the two handoffs. Keep role-specific response contracts and query ownership.
  Exclude CLI flags, adapters, invocation, capability preflight, claim/dispatch
  authorization or budgets, repository/worktree policy, schema/migrations,
  other roles/providers, session identifiers and resume/open/fork actions,
  model/effort selection or inferred defaults, context/compaction, account
  allowance, and fallback. Stop if a current claim cannot be distinguished
  from a legacy claim, an exact fixed flag cannot be proved, or truthful
  disclosure requires any excluded behavior or an unreviewed architectural
  reversal. Do not add an Unknown-only surface.
- Acceptance: prove the three new claims persist distinct coherent assignment
  versions without invented model/effort; persisted legacy rows remain Unknown;
  each role's status covers current, no attempt, valid legacy/mismatch, and
  malformed metadata; API disclosure guards remain strict; UI renders only
  recognized fixed values or Unknown; all three adapter argument tests remain
  green; generated-client drift is checked. Run relevant Domain, Application,
  Infrastructure, API, Architecture, and frontend suites, typecheck, lint,
  production build, and `git diff --check`, reporting exact results. Make
  `current-work.md` commit-ready before the first review: state the selected
  slice as the current delivery based on the verified parent, record actual
  checks and open risks, and describe post-publication verification without
  claiming it already happened or embedding the future commit SHA. Claude
  returns a complete uncommitted, unpushed diff for Codex GO/NO-GO. After a
  future GO, one instruction covers the reviewed commit, normal fast-forward
  push, live-remote verification, and only the factual SHA closure; no extra
  review round is needed for that bounded closure. A material change, failed
  push, or divergence stops for review/direction. No commit/push GO is granted.

## Prior decision: configured Claude Implementer built-in tools

- Planning baseline verified before selection: `main`, `HEAD`, local
  `origin/main`, and live `origin/main` all point to
  `8f52522a35419045229f27962f8523cf41a5149d`; staged, unstaged, and
  untracked state was empty. The substantive resume-eligibility delivery is
  `1ec9ac5f1c9eb3a25301cd37684c368db1ef24f6`, followed by the factual
  handoff closure at this planning baseline.
- Selected one bounded Increment 4 slice: disclose the **configured Claude
  Implementer built-in tool list** on the existing implementation-attempt
  status and action. For an attempt whose immutable assignment coherently
  identifies `ClaudeCode`, `Implementer`, `WorkspaceEditOnly`, and
  `claude-implementation-v1`, report the current adapter's fixed `--tools`
  value `Read,Edit,Write,Glob,Grep`; otherwise report `null`/`Unknown` for no
  attempt or a valid historical/mismatched assignment. Preserve the existing
  fail-closed `agent_attempts.invalid_assignment` result for malformed
  assignment metadata. This is a configured argument fact, not an observation
  of effective access, a complete security boundary, or invocation eligibility.
  The [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  defines `--tools` as restricting built-in tools and explicitly says it does
  not affect MCP tools. The existing adapter and focused adapter test both fix
  the exact list; the local `claude` command is unavailable for an installed
  version check at this selection.
- Boundaries: use only the existing implementation status projection, MVC
  response, generated TypeScript client, implementation action, focused tests,
  and cockpit specification. Label the UI fact "Configured built-in tools";
  render only the single fixed list or `Unknown`, not a provider-supplied list.
  Exclude CLI arguments, invocation and tool policy, preflight, claim/dispatch
  and authorization, other roles, model/effort selection, session behavior,
  context/compaction, account usage, and persistence schema. Stop if the fixed
  argument or assignment coherence cannot be proved, or if the disclosure
  requires a policy/invocation change or a claim about effective MCP or host
  access. Acceptance requires current, no-attempt, historical/mismatched, and
  malformed-assignment cases; preserved API disclosure guards; generated-client
  drift verification; relevant backend and frontend tests, typecheck, lint,
  production build, and `git diff --check`.
- Claude first returns the complete **uncommitted, unpushed** diff, including a
  commit-ready `current-work.md` and this planner-owned selection, for Codex
  GO/NO-GO. No commit/push GO is granted. After a future GO, one publication
  instruction may cover the reviewed substantive commit, normal fast-forward
  push to `origin/main`, live-remote verification, and tightly bounded factual
  documentation closure recording the delivered SHA. A material change after
  GO, failed push, or remote divergence stops for review or direction; no
  force-push or history reconciliation is authorized.
- Review decision (2026-09-27): **NO-GO, documentation-only correction of this
  same slice.** Codex verified `main`, `HEAD`, local `origin/main`, and live
  `origin/main` at `8f52522a35419045229f27962f8523cf41a5149d`, with no
  staged or untracked files; inspected the complete 12-file diff; and
  independently passed focused Application 8/8, API 9/9, frontend 19/19,
  and `git diff --check` (only the existing generated-client line-ending
  warning). The code and focused tests match the selected behavior. However,
  `current-work.md` still calls the slice proposed, uncommitted, unpublished,
  and unauthorized for commit/push. Those statements would be false in the
  substantive delivery. Make only that handoff commit-ready: describe the
  selected slice as the current delivery based on parent `8f52522a`, retain
  actual checks and open risks, and name post-publication verification without
  claiming it happened or embedding the future commit SHA. Preserve code,
  tests, and other documentation. Check local links and run `git diff --check`;
  the earlier test results remain applicable to this documentation-only
  correction. Return the complete uncommitted, unpushed diff for re-review.
  No commit/push GO is granted.
- Correction re-review (2026-09-27): **GO** for the reviewed configured
  built-in-tools slice, including the corrected, commit-ready `current-work.md`
  and this planner-owned review record. Codex verified `main`, `HEAD`, local
  `origin/main`, and live `origin/main` at
  `8f52522a35419045229f27962f8523cf41a5149d`, with the same 12 modified
  files and no staged or untracked files; reviewed the corrected handoff and
  previously reviewed implementation diff; and passed `git diff --check`
  (only the existing generated-client line-ending warning). The correction
  changed only `current-work.md`, so the independently passed focused
  Application 8/8, API 9/9, and frontend 19/19 checks from the preceding
  review remain applicable, alongside the executor-reported full suites,
  adapter test, lint, typecheck, and build. Claude may publish this reviewed
  slice under one bounded instruction: commit the reviewed substantive diff,
  push `main` normally to `origin/main`, verify the live remote, then make only
  the factual `current-work.md` closure needed to record the delivered SHA and
  verified publication, commit and push that closure normally, and verify the
  live remote again. No extra review is required for that factual closure;
  any material or out-of-scope change, failed push, or remote divergence must
  stop for Codex review or direction. No next product slice is selected.

## Prior decisions and review record

- Codex owns planning, architecture, slice selection, review, and acceptance;
  Claude is the bounded executor. Routine slice selection needs no owner
  approval; committing and pushing an executor diff still requires Codex GO.
- Verified planning baseline: `main` and `HEAD` are
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`; local `origin/main` and
  live `origin/main` match, and staged, unstaged, and untracked state is empty.
  Selected next bounded Increment 4 slice: disclose **provider-session resume
  eligibility for the current Claude Implementer attempt** as `Ineligible`
  only when its immutable assignment is coherent (`ClaudeCode`, `Implementer`,
  `WorkspaceEditOnly`, `claude-implementation-v1`). The existing adapter passes
  `--no-session-persistence`; the [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  explicitly says sessions under this flag cannot be resumed. No attempt or a
  valid historical/mismatched assignment yields `Unknown`/`null`; invalid
  assignment preserves the existing fail-closed error. This is a configured
  attempt fact, not a host-wide capability or provider-observed result.
  Restrict work to the existing implementation status/API, generated client,
  implementation action, focused tests, and cockpit specification. Exclude
  CLI arguments, invocation, persistence schema, session identifiers and
  resume/open/fork actions, claim/dispatch/authorization policy, preflight,
  other roles, models, effort, context, compaction, and usage limits. Stop if
  assignment coherence or the fixed flag cannot be proved, or if disclosure
  requires a policy or invocation change. Acceptance requires positive,
  no-attempt, historical/mismatch, and invalid-assignment cases; strict API
  disclosure checks; generated-client drift verification; relevant backend
  and frontend tests, typecheck, lint, production build, and diff check.
  Claude must present the complete uncommitted, unpushed diff, including
  `current-work.md`, for Codex GO/NO-GO. No commit/push GO is granted.
- Review decision (2026-09-27): **NO-GO**, documentation correction within
  this same slice. The implementation and focused evidence match the selected
  behavior; Codex independently passed focused Application 8/8, API 9/9,
  frontend 19/19, typecheck, and `git diff --check`. However,
  `current-work.md` still describes this slice as pending, uncommitted, and
  awaiting GO, which would be false when committed with the substantive slice.
  Make that handoff concise and commit-ready: describe the slice as the current
  delivery based on parent `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`,
  record actual checks and open risks, and name post-publication verification
  without asserting it happened or embedding the future commit SHA. Preserve
  code and tests. Review local links and run `git diff --check`; earlier test
  results remain applicable to a documentation-only correction. Return the
  whole uncommitted, unpushed diff for re-review. No commit/push GO.
- Correction re-review (2026-09-27): **GO** for the reviewed substantive
  resume-eligibility diff, including the corrected, commit-ready
  `current-work.md` and this review record. Codex verified `main`, `HEAD`,
  local `origin/main`, and live `origin/main` at
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`, with no staged or
  untracked files; inspected the complete code, test, generated-client, and
  documentation diff; independently passed focused Application 8/8, API
  9/9, frontend 19/19, typecheck, and `git diff --check`; and confirmed the
  touched local documentation links resolve. The correction changed only
  `current-work.md`, so the earlier executor-reported full suites, lint, and
  build remain applicable. Claude may commit this reviewed slice on `main`,
  including `current-work.md` and `planner-handoff.md` in the same commit,
  push normally to `origin/main`, verify that the live remote points to the
  delivered commit, and report the result. A material change after GO
  requires re-review; a failed or divergent push must stop without force-push
  or reconciliation. No next product slice is selected.
- Publication verification (2026-09-27): Claude delivered the reviewed 12-file
  slice as `1ec9ac5f1c9eb3a25301cd37684c368db1ef24f6` (parent
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`). Codex independently
  verified `main`, `HEAD`, local `origin/main`, and live `origin/main` at the
  delivered commit, a clean checkout, and the exact reviewed file list.
  Post-publication Application 986/986, API 312/312, Architecture 9/9,
  frontend 605/605, typecheck/production build, lint (20 pre-existing
  warnings), and `git diff --check` passed; initial parallel .NET build-file
  collisions were resolved by rerunning those suites sequentially. The
  substantive slice is published. The remaining action is a documentation-only
  `current-work.md` closure recording the delivered SHA and completed remote
  verification. The project owner directed one-step completion of this narrow
  closure rather than another review round. Codex inspected the completed
  two-file documentation diff and passed `git diff --check`. **GO** for Claude
  to commit `current-work.md` alongside this planner-owned note on `main`,
  push normally to `origin/main`, and
  verify the live remote. Any additional or material change must stop for
  review; a failed or divergent push must stop without force-push or history
  reconciliation. No next product slice is selected.
- The configured Claude Implementer session-persistence slice was accepted
  after two correction rounds and published as `1d9f6b4876b43b0585ed8422d4de653966e8e8dc`.
  Codex independently passed API 307/307, focused Application 8/8, focused
  frontend 19/19, typecheck, and diff check before GO. The remote was verified;
  broader executor-reported checks are in the shared handoff. This does not
  close Increment 4.
- Previously selected and delivered Increment 4 slice: disclose the Claude
  Implementer adapter's configured permission-prompt handling on the existing
  implementation attempt status and action. For a coherent current assignment (`ClaudeCode`,
  `Implementer`, `WorkspaceEditOnly`, `claude-implementation-v1`), show the fixed
  `--permission-prompts none` configuration as `None`; show `Unknown`/`null` for
  no attempt or a valid historical/mismatched assignment, and preserve the
  current fail-closed invalid-assignment error. This is configuration evidence,
  not an observed provider outcome or authority to invoke. The current adapter
  already passes the flag; the [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents `none` as denying prompts in print mode. The local `claude` command
  was unavailable during planning, so installed-version behavior was not
  observed here.
- Scope is the existing implementation status projection/API contract, generated
  frontend client, implementation action, focused tests, and cockpit
  specification. Exclude adapter arguments, provider preflight, claim/dispatch
  and authorization policy, model/effort discovery, context or compaction,
  session resume, usage limits, persistence schema, and other roles. Stop if
  the fixed argument or assignment coherence cannot be proven, or if a safe
  disclosure requires changing invocation or security policy. Acceptance needs
  positive/current, no-attempt, historical/mismatch, and invalid-assignment
  cases; strict API disclosure guards; generated-client drift check; relevant
  backend/frontend tests, typecheck, lint, production build, and diff check.
  Claude must return the complete uncommitted, unpushed diff including
  `current-work.md` for Codex GO/NO-GO. No commit/push GO has been granted.
- Review decision (2026-09-27): **NO-GO**, correction of this same slice.
  The status projection and UI match the selected behavior, and Codex
  independently passed focused API 4/4, Application 8/8, frontend 19/19, and
  `git diff --check`. The new API test helper that exempts the single
  root-level `configuredPermissionPrompts` fact from its broad `prompt` leak
  scan has no negative tests. Add focused cases proving that a nested property,
  duplicate occurrence, unexpected value, and actual leaked prompt remain
  detectable by that guard; retain the coherent root-level `None`/`null`
  allowance. Keep the diff uncommitted and unpushed, run affected checks and
  relevant full validation, and return it for review. No commit/push GO.
- Correction re-review (2026-09-27): the five focused disclosure-guard cases
  close the prior finding; Codex independently passed the full API suite
  312/312 and `git diff --check`. **NO-GO remains for the delivery handoff only**:
  `current-work.md` still labels this slice pending, uncommitted, and awaiting
  GO, and still names the previous slice as the latest substantive delivery.
  Those statements would be false in the substantive commit. Claude should
  make that page concise and commit-ready: describe this slice as the current
  delivery based on parent `c2b0155f26f07bbe09ca67a9c5234228e274a703`,
  record actual checks, remaining risks, and the post-publication verification
  action, without embedding the new commit's unknown SHA. Preserve the code
  and corrected tests. A documentation-only correction needs `git diff --check`
  and link review; earlier test results remain applicable. Return the whole
  uncommitted diff for GO/NO-GO. No commit/push GO yet.
- Final review (2026-09-27): **GO** for this reviewed substantive diff,
  including the commit-ready `current-work.md`. Codex verified `main`, HEAD,
  and local `origin/main` at `c2b0155f26f07bbe09ca67a9c5234228e274a703`,
  reviewed the full diff and corrected guard tests, independently passed API
  312/312, focused Application 8/8 and frontend 19/19 across review rounds,
  confirmed local handoff links resolve, and passed `git diff --check`.
  Claude may commit this reviewed slice on `main`, push normally to
  `origin/main`, verify the remote points to the delivered commit, and report
  the result. A material change after GO requires re-review; a failed or
  divergent push must stop without force-push or reconciliation.
- Publication and handoff review (2026-09-27): the permission-confirmations
  slice was published as `01f1777af877dd0374d7e3d7e461127a02f40d73`;
  Codex independently verified live `origin/main` at that commit and a clean
  checkout before the handoff edit. **GO** for the documentation-only
  `current-work.md` closure recording that delivery and replacing its stale
  next action. Local links and `git diff --check` passed. Claude may commit
  this reviewed documentation checkpoint and push `main` normally; no next
  product slice is selected.
- Provider account-allowance evidence remains closed without delivery: neither
  local CLI offered a proven safe, authoritative, machine-readable observation
  contract. Do not add `Unknown`-only scaffolding or direct authenticated API
  access without a new accepted decision.
