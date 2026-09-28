# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md) for the standing review and publication rules,
[current-work.md](current-work.md) for delivery facts, and the
[roadmap](mvp-delivery-plan.md) and accepted [ADRs](../decisions/README.md)
for product and architecture decisions. Verify this checkpoint against Git and
code before relying on it; older decision detail remains in Git.

## Current decision (2026-09-28): selected Codex model and effort catalog observation

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
