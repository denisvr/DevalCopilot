# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-03): trustworthy explicit local verification

### Current review decision (2026-10-03): GO for the complete corrected diff

- Codex accepts R1–R3 and gives publication GO for the complete corrected explicit-
  local-verification slice: 26 modified tracked files and nine untracked files,
  exactly 35, including this planner record and current-work.md. Claude publishes
  in the same executor chat. No next slice is selected; Increment 4 remains open.
- Independently verified main, HEAD, local origin/main and live refs/heads/main at
  4ec68bd9143d426ccb2a746732db4a6fa22e1b1d; empty index. The executor preserved
  the preceding NO-GO at SHA-256
  61755ef9b5b217045bc86e14aaa51065a02f39f637d5cbe869c1aa2bd185fdf0.
  Raw-file hash comparison proves that the correction changed only the panel,
  cockpit specification and current-work entry, and added its ownership test.
  Backend, backend tests, generated client and browser contracts are unchanged.
- Claim transaction/fresh counter, dispatch snapshot/authority and guarded
  SourceChanged agree with the selected architecture. Accept gitWorkspaceId on
  both execution and Git evidence responses; the latter is a necessary additive
  ownership projection. No blocking functional or architectural finding remains.
- R1: a confirmed earlier workspace's Running fact remains visible as history but
  no longer disables its recipe or labels its control Pending/Running. Unknown
  ownership stays conservative; current-workspace Running still blocks every recipe.
- R2: synchronous duplicate protection belongs to the committed lifetime and the
  exact request. Unkeyed A-to-B-to-A can start its own request; A's obsolete finally
  cannot release B's guard or refresh/report for B. Same-lifetime duplicates send once.
- R3: the unknown-outcome boundary uses the generation current when the failure
  becomes known. Earlier settled or pending refreshes cannot discharge it; only
  a later explicit successful read does. Acknowledgment is recorded so a later
  pending/failed read does not revive it. Accepted/refused outcomes remain truthful.
- The executor's 16 permanent ownership cases failed 10/16 against the submitted
  panel, with six positive controls, then passed. They cover the exact three
  independently reproduced defects, including parent-key independence and obsolete
  completion ordering. Preserve both this red/green evidence and earlier failures.
- Fresh independent correction checks: full Vitest 1786/1786 in 132 files;
  typecheck and production build clean; lint nine warnings/zero errors; harness
  40/40; canonical test:e2e:all Chromium 11/11 and guided journey 1/1 after harness,
  normal authentication/composition/all supervisors, no skips or reruns. The browser
  command ran outside the sandbox for the known Windows Event Log requirement,
  with no host or authentication bypass. SignalR negotiation and chunk notices remain.
- Prior independent backend evidence is retained on the byte-identical backend:
  solution build/NSwag zero warnings/errors, Application claim/seam/dispatch filter
  94/94, Infrastructure transaction boundary 7/7, Api supervisor/endpoints 40/40,
  Architecture 9/9. Full backend suites, audits and formatter comparison remain
  separately labelled executor evidence. No formatter cleanliness or real-provider
  reliability is claimed. Changed-file text hygiene and local file links are clean.
- Generated api-client.ts SHA-256 remains
  13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.
  Codex edited only this planner record during re-review; implementation, tests
  and current-work.md were unchanged. The publication prompt supplies final
  planner/current-work hashes and the exact reviewed inventory.
- GO covers one bounded publication sequence: verify this baseline, empty index,
  exact 35-file inventory and supplied hashes; stage only those reviewed paths,
  inspect staged diff/hygiene, commit with parent 4ec68bd, push main normally to
  origin/main, fetch and verify HEAD, local origin/main and live refs/heads/main
  equal the substantive SHA, with a clean checkout. Preserve this GO byte-for-byte.
- Run the publication prompt's checks sequentially on that SHA. Stop on failure,
  changed implementation/refs, a failed push or material change, and preserve the
  evidence. No force push, amend, history reconciliation or unreviewed correction.
- Only after all publication checks pass, make a separate factual closure touching
  current-work.md alone: substantive SHA, verified publication, commands/results,
  fresh versus retained evidence and remaining limits. Preserve earlier failures;
  embed no closure self-SHA. Push normally and verify all three refs and clean tree
  again; report both SHAs and parents. Material post-GO changes return for review.
  This GO grants no next-slice work or Increment 4 completion.
### Verified baseline and judgment

- Codex independently verified main, HEAD, local origin/main and live
  refs/heads/main at 4ec68bd9143d426ccb2a746732db4a6fa22e1b1d, with an empty
  index and clean working tree before this selection. The preceding substantive
  commit is f8408d7af722f432ee3fd3c419f95af735b475ce, parent b38947507e4fc018190152bfd69bef8b3a2fbe3f.
  Closure 4ec68bd changes only current-work.md. All 21 files outside that factual
  closure match the prior GO's raw-file hash manifest. Publication is verified;
  the preceding GO is spent. Its reported post-publication checks are recorded
  in current-work.md, separately from retained evidence.
- Select exactly one bounded outcome: an explicit local verification request
  creates one freshly eligible durable snapshot atomically, launches only after
  a fresh single-use dispatch decision agrees with the process inputs, and shows
  current execution evidence after the project's explicit refresh. Claude uses
  one new executor chat; Codex retains architecture and acceptance. No commit/push
  GO was granted by selection; the current review decision above governs publication.
- The manual claim currently reads tracked authority, captures Git externally,
  then saves without a transaction or commit-seam recheck. Dispatch checks only
  execution status/marker; the supervisor launches from an earlier feed tuple.
  The verification panel forwards the evidence-refresh generation to Git metadata
  but omits it from its execution read and ignores that read's currency for Run.
- Independent temporary file-backed SQLite probes reproduced both production
  gaps: a second connection disabled the recipe during CaptureAsync and the
  claim still succeeded; a released lease still allowed a durable dispatch
  marker. Both corrected probes passed 2/2 as positive bug reproductions.
  The first dispatch probe's final assertion mistakenly read a tracked entity,
  not the updated database row; that was a probe error, not a product refusal.
  Probe sources were restored byte-for-byte; the original handler class passed
  6/6 and Git was clean before this planner edit.
- This directly hardens the verification evidence consumed by diagnosis,
  correction and approval. It is more valuable now than further context sampling
  or another browser-only proof. The escalated-plan authorization journey/copy
  remains a separate candidate; changing its canonical escalation text requires
  historical compatibility because grants validate exact recorded content.
  Account observations confer no threshold enforcement or invocation eligibility.
  Claude allowance, provider resume and compaction remain unproven.

### Objective, boundaries and exclusions

- Preserve the protected verification claim POST, its 202 response/body, recipe
  configuration and selected-checkpoint policy in the
  [workflow contract](../architecture/workflow-model.md#current-verification-configuration-and-execution-boundary).
  A claim requires its project's latest workspace, Ready status, active owned
  lease, an enabled owned recipe, no Running verification in that workspace and
  fresh physical evidence matching the selected owned checkpoint. Do not add
  a latest-checkpoint-number rule: an older selected checkpoint whose fingerprint
  still matches remains eligible. Do not silently substitute another checkpoint,
  workspace, recipe or invocation tuple.
- Read initial authority untracked and perform Git capture outside transactions.
  After capture, use the existing provider-neutral context boundary for a short
  write-locked transaction; re-read all authority fresh, repeat the gates and
  compare the exact recipe/source/ownership facts used before capture. Recipe
  identity, name, executable, ordered literal arguments, timeout and enabled state,
  workspace identity/path and checkpoint identity/number/fingerprint must agree.
  Reserve the execution number from fresh Project state and atomically persist
  the execution and counter. A populated tracker is not a fresh authority read.
  No global tracking changes, generic repositories or persistence framework.
- Serialized competing claims may create only one Running execution per workspace;
  refusal or failure before durable commit leaves no execution or consumed number.
  Reuse operation-owned Result/Problem Details codes where their meaning applies.
  Preserve genuine request failures and durability uncertainty: a failure after a
  durable commit cannot be reported as proof that nothing was recorded.
- Treat the eligibility feed as a candidate list. Pass a bounded, operation-owned
  expected snapshot to the final dispatch decision, carrying the identities and
  exact invocation/source facts actually used for capture and process execution.
  Re-read authority untracked under the existing short transactional boundary,
  validate ownership, Ready/active lease/current workspace and agreement with the
  execution/checkpoint snapshot, and conditionally commit the single-use marker.
  No changed executable, arguments, timeout, workspace path or fingerprint may
  authorize invocation using the stale feed. Duplicate dispatch invokes once.
  Git, process and artifact I/O stay outside database transactions.
- Preserve the claimed recipe's immutable snapshot after a successful claim:
  subsequently editing or disabling the live recipe neither retargets nor cancels
  that execution. Agreement is with the durable execution, not current recipe
  settings. The dispatch comparison detects changes between candidate read and
  decision; it is not cryptographic protection against all out-of-band tampering.
  Guard pre-dispatch SourceChanged recording against the same expected snapshot
  so obsolete observations cannot classify a different execution tuple.
- Keep real fingerprint drift's existing SourceChanged semantics, process outcomes,
  output sealing, protected output reads and restart reconciliation. Ownership or
  snapshot refusal launches zero processes and leaves the undispatched claim
  pending under existing semantics; invent no fingerprint, process outcome or
  terminal status. No new cancel/retry/refund/recovery authority. Document that an
  ineligible claim can remain pending and the filesystem is not frozen through
  claim commit, dispatch commit or process start. No executor lease or scheduler.
- Connect the existing refresh generation to the verification panel's execution
  read. Run needs settled, successful source and execution reads for that generation
  and no Running execution for the current workspace, including another recipe.
  Add GitWorkspaceId to the existing execution query/response projection and
  regenerate the client normally; do not infer workspace ownership from a recipe
  or the selected checkpoint. An old workspace's Running fact must not block the
  current workspace. This is an additive identity field, not a new endpoint.
  Cached execution/output history may remain visible but must not claim currency.
  Check the same conditions in the handler, not just the button. Preserve accepted
  claims after owner replacement; a failed follow-up read cannot erase acceptance.
  For an uncertain POST result, use fixed safe uncertainty copy and require an
  explicit successful status refresh before another local submission. No automatic
  retry, raw server-detail copy or invented success/refusal.
- Preserve project lifetimes, retained-callback and A-to-B-to-A protection,
  overlapping-read ordering, drafts, output identity and the single owned polling
  chain. The explicit refresh issues reads only. Extend the existing guided
  collaboration journey to confirm Failed then Passed in both verification and
  review evidence after refresh, without intermediate reload or extra Agent claims.
  Keep normal host composition, authentication, all supervisors, generated clients
  and owned cleanup. No SQL workflow mutations or production test hooks.
- Update the workflow/cockpit contracts and a commit-ready current-work entry.
  This restores the existing local verification boundary. No migration, dependency,
  provider contract, permission, budget, authorization or lifecycle change;
  no global client/configuration change, historical rewrite or extra UI-hook sweep.

### Stop gates and acceptance evidence

- Stop on baseline/ref discrepancy or a need to change accepted authority rules,
  schema, external process/provider contracts, permissions or recovery/lifecycle
  semantics. Report the gap instead of weakening normal composition or broadening
  scope. External capabilities beyond the preserved adapters require safe official
  contracts; CLI defaults and Unknown-only scaffolding are not evidence.
- Add regressions and prove red on the parent behavior, then green. Use real
  file-backed SQLite with a populated long-lived context and another connection
  committing during capture or immediately before BEGIN. Cover recipe changes,
  workspace/path/status replacement, lease loss, checkpoint ownership/fingerprint
  drift, a competing Running execution, fresh counter reservation and concurrent
  claims. Refusals leave no partial rows/counter changes. Test rollback, cancellation
  and bounded acquisition/save/commit failures through the real mediator boundary.
  Prove capture is outside the transaction and preserve the older matching-
  checkpoint positive case.
- At dispatch, cover authority/snapshot changes after the feed and during capture,
  each invocation field, ordered arguments and ownership, duplicate marker races,
  failed capture, genuine SourceChanged and obsolete observations. Hosted tests
  prove zero process calls on refusal and exactly one on success; a live recipe
  change after claim still runs the original snapshot. Preserve result artifacts
  and restart behavior. Mutation checks must fail when seam/dispatch guards vanish.
- Prove protected real HTTP 202 and typed client resolution plus safe conflict
  mapping. Frontend composition tests use real hooks and cover first-frame loading,
  partial read failures/recovery, polling, another running recipe, explicit refresh,
  retained callbacks, replacement/unmount, uncertain POST and accepted POST followed
  by a failed read. No refresh or stale handler creates work.
- Run affected checks first, then build/NSwag, sequential full backend suites and
  Architecture, full frontend Vitest/typecheck/lint/build, harness and canonical
  test:e2e:all after harness succeeds. Report actual commands/results/skips, client
  hash, audits, formatter baseline comparison, links and tracked/untracked hygiene.
  Distinguish fresh from retained evidence and preserve failures.
- Return the complete unstaged, uncommitted, unpushed diff, unchanged planner
  record and commit-ready current-work entry for Codex GO/NO-GO. Select no next
  slice. A future GO uses one publication instruction covering the reviewed
  substantive commit, normal fast-forward push, live-remote verification and a
  separate tightly bounded current-work-only factual closure. Material changes
  after GO return for review. Selection itself grants no publication authority.

### Initial executor preflight

Expected branch main and HEAD 4ec68bd9143d426ccb2a746732db4a6fa22e1b1d.
Local origin/main and live refs/heads/main must match. After this planner edit:
no staged changes, only docs/roadmap/planner-handoff.md modified, no untracked files.
Generated api-client.ts baseline SHA-256:
35fabf7e14b14a3d314d1b73d1dffb96b4be8f6c82e84b02516b2e0f8f850903.
Preserve this planner-owned record byte-for-byte. The complete English execution
prompt is provided in chat, not duplicated here.
