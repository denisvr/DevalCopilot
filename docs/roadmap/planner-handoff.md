# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md) for the standing review and publication rules,
[current-work.md](current-work.md) for delivery facts, and the
[roadmap](mvp-delivery-plan.md) and accepted [ADRs](../decisions/README.md)
for product and architecture decisions. Verify this checkpoint against Git and
code before relying on it; older decision detail remains in Git.

## Current selection (2026-09-29): explicit Claude effort requests at safe attempt boundaries

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
