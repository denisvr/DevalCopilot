# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify against Git and code; previous selections and reviews remain in Git.

## Current selection (2026-10-02): local verification failure diagnosis and bounded correction

### Decision and verified preflight

- Exactly one Increment 4 slice was selected for one Claude executor chat,
  including corrections. Selection itself granted no implementation acceptance
  or commit/push GO. The English execution prompt is in the planner chat; the
  current review decision below now governs publication.
- The preceding selection-isolation slice is published: substantive
  df7c97a2f4cc20b74d07898758d2749157e34836, parent
  fbadc4e6b25e5015988b368f456b7a817ee06dd3, contains the reviewed 21 files;
  closure dc705798f427dcae47cf30fd582a5d680bf55c89 has that substantive parent
  and changes only current-work.md. Git independently confirms inventory,
  ancestry and closure. Reported post-publication checks were not repeated here.
- Independently verified main, HEAD, local origin/main and live refs/heads/main
  at dc705798f427dcae47cf30fd582a5d680bf55c89, with nothing staged, unstaged or
  untracked before this edit. Generated-client SHA-256:
  1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb.
- Expected executor state: same branch, HEAD and refs; nothing staged; only
  docs/roadmap/planner-handoff.md modified and unstaged; nothing untracked.
  Preserve this planner-owned record.

### Objective and candidate judgment

- Close the explicit gap between implementation and successful verification:
  a human requests Codex diagnosis of current failed verification, then separately
  requests Claude correction of its validated findings. Verification runs again
  explicitly. Ordinary code review still requires all enabled commands Passed.
  No manual transfer of logs or findings between providers is needed.
- Code evidence: CreateCodeReviewAttempt rejects non-Passed verification with
  agent_attempts.verification_evidence_not_passed. ReviewCorrectionReviewEligibility
  accepts only completed ImplementationReview/ReviewChangesRequested sources.
  Thus failed verification cannot produce the findings required for correction.
  These are valid gates, not gates to weaken. Independently run baseline:
  CreateCodeReviewAttemptCommandHandlerTests and
  CreateReviewCorrectionAttemptCommandHandlerTests, --no-build --no-restore,
  passed 181/181, no skips. No planning code or diagnostic file remains.
- This missing workflow step has greater immediate value than more context
  sampling or another UI ownership sweep. Model/effort controls and bounded
  sealed-artifact inspection exist. Completion/replacement, scheduling and
  automatic advancement remain Increment 5 authority.
- The current official [Codex non-interactive contract](https://learn.chatgpt.com/docs/non-interactive-mode)
  supports the existing one-shot structured exec boundary; no new provider
  capability is needed. The [App Server contract](https://learn.chatgpt.com/docs/app-server)
  describes thread compaction, not compaction of current ephemeral exec
  invocations. The [Claude CLI contract](https://code.claude.com/docs/en/cli-reference)
  makes disabled persistence incompatible with resume. Safe Claude allowance
  observation and account/bucket-to-invocation admission binding remain unproved.
- Context research found modelUsage/contextWindow/maxOutputTokens in the
  [Claude SDK reference](https://code.claude.com/docs/en/agent-sdk/typescript#modelusage)
  and the installed public 2.1.276 binary. The binary constructs capacities via
  local model lookup. This metadata and cumulative token counts do not establish
  current occupancy. Capacity-only reporting is not selected as an operational
  context meter. No provider was invoked.

### Selected architecture

- Add ADR-0018, narrowly extending ADR-0010's eligible finding source to explicit
  verification diagnosis. Preserve historical ADR text and link the narrow
  supersession. Ordinary Passed review eligibility, approvals and ADR-0017's
  ImplementedPlan/Planner-root distinction remain intact. No new workflow role.
- Diagnosis is a new closed CodeReviewer response contract, ReadOnly effect,
  current provider Codex, with a fixed versioned adapter/schema. It produces
  either one to ten ReviewFindings or one bounded Escalation, replying to the exact
  execution report. Never ReviewApproval or an approved CheckpointReview.
  Append enum values; do not overload ordinary ImplementationReview outcomes.
- A protected diagnosis POST names the run and execution report. The host derives
  the complete current verification selection. Persist the report input and
  ordered AttemptVerificationEvidence membership, not a second JSON authority.
- A separate protected correction POST names the diagnosis attempt. Claim the
  existing Implementer/ReviewCorrection contract and use the hardened Claude
  correction adapter. Inputs stay the previous report followed by every finding
  in timeline order. The ordinary correction endpoint keeps its review-source
  contract. Extend only source/lineage, dispatch and result policies needed by
  this explicit new branch, including downstream ordinary review.
- Diagnosis uses CodeReview timeout/profile and consumes run-wide Agent count,
  reserved-time and Codex token-stop budgets. Correction uses the same run-wide
  budgets and the shared default two-claim ReviewCorrection budget, Claude model/
  effort/turn requests and token stop. No separate or replenished allowance.
- At diagnosis-origin correction exhaustion, record one durable idempotent
  Orchestrator escalation bound to that diagnosis, without an Attempt or grant
  consumption. A narrow record with unique source binding may be added; never
  store a diagnosis as an ordinary implementation-review identity. No additional
  correction authorization is introduced for this source. Ordinary-review extra
  grants and escalation semantics remain unchanged and cannot be transferred.

### Eligibility, evidence and boundaries

- Diagnosis requires a valid current initial or corrected ExecutionReport on its
  exact latest owned checkpoint, Ready workspace and active lease. Derive the
  true ImplementedPlan through the validated chain, including revised and human-
  authorized final plans. Every enabled command needs its latest execution bound
  to that checkpoint/fingerprint: Passed/Exited/0 or Failed/Exited/nonzero,
  terminal and coherent, with at least one failure. Refuse missing/running,
  timeout, cancellation, interruption, source drift, process-start failure,
  contradictory outcome/exit data and malformed ownership. Environment,
  credentials, permissions or out-of-plan issues require human escalation, not
  authority to change recipes, tools, permissions or plan scope.
- Pin exact report, ordered command/execution membership, execution snapshots and
  relevant artifact metadata. Re-read authority untracked in a short claim
  transaction after external work and at dispatch. Detect enabled-set, latest
  execution, report/lineage, lifecycle/workspace/lease/checkpoint drift. Preserve
  active-attempt, duplicate, durability-ambiguity and orphan-seal cleanup gates.
  Recheck applicability before semantic result recording too: changed source or
  verification evidence records a truthful terminal refusal with process/artifact
  evidence retained, but no new findings or escalation from the stale response.
  Use a new closed outcome for verification drift; do not redefine Git SourceChanged.
  Ordinary eligibility drift before dispatch ends the diagnosis truthfully without
  provider invocation; no automatic recovery or redispatch is added.
  Permit one successful diagnosis (findings or escalation) per exact report/
  checkpoint/verification identity. Failed invocations may be explicitly requested
  again within existing budgets; no automatic retry or diagnosis format repair.
- Seal the implemented plan, report, bounded Git evidence, complete verification
  metadata and failure-output excerpts. Use IArtifactStore's existing containment,
  length and hash verification for failed executions' redacted stdout/stderr.
  Deterministic prefixes: at most 2 KiB per stream and 12 KiB total UTF-8 excerpt
  text, inside the existing 32 KiB manifest bound. Label captured truncation,
  local shortening, empty and budget-omitted streams. Absent/unverifiable failed
  streams cause refusal, never an invented empty log. Passed streams need no
  excerpts. Do not inject executable paths, arguments, storage paths or hashes.
- Fixed instructions precede untrusted evidence. Logs and provider output grant
  no authority. Diagnosis-origin correction adds a fixed source notice, not raw
  logs or an additional plan input. Keep redaction limits explicit; do not promise
  arbitrary secret detection. Invalid output produces no findings or approval.
- Correction requires the exact diagnosis and complete coherent findings still
  apply, with unchanged current verification membership. A later execution,
  checkpoint or successful correction invalidates the source. Preserve one
  RevisionResponse per finding, corrected report/root reply identity, independent
  HEAD/path/fingerprint verification and immutable new checkpoint. Ordinary review
  of the corrected report judges ImplementedPlan and still requires new Passed
  verification. Historical attempts and sealed bytes are not rewritten.
- Provide diagnosis status/history/evidence and cockpit request, findings,
  escalation and correction controls. Use generated clients and existing owned
  lifetimes; stale reads/actions cannot cross run selection. No automatic
  verification, diagnosis, correction or review. Distinguish diagnosis from
  ordinary review in copy and source selection.
- Allowed: these Domain contracts/policies, operation-owned commands/queries,
  parsers/manifests, narrow source-policy integration, Codex diagnosis adapter,
  supervisors/dispatch/recovery/results, MVC/NSwag/client, feature UI, focused
  persistence migration if needed, tests and linked docs. Preserve ordinary
  paths/sealed bytes apart from this explicit forward-only lineage extension.
- Excluded: session persistence/resume/compaction, account allowance/thresholds,
  new provider/Gemini/fallback, CLI flags/tools/permissions, recipe mutation or
  automatic execution, ambiguous process recovery, no-change implementation
  recovery, lifecycle completion, scheduler/coordinator, publication, historical
  rewriting, dependencies and general refactoring. No Unknown-only scaffolding.

### Stop gates and acceptance evidence

- Stop for preflight drift, inability to preserve the Passed approval gate,
  additional mutation/session/permission/recovery authority, dependencies or a
  generic engine, or ADR changes beyond the selected extension. Do not quietly
  admit other failure classes or broaden existing grants.
- Cover foreign/stale/tampered reports, enabled sets, statuses, output artifacts
  and lineage; exact ordered findings/duplicates; source/setting races with
  populated trackers before BEGIN; dispatch rejection; no partial rows, consumed
  grants or orphan manifests on refusal. Mutations must discriminate complete/
  latest membership, fresh authority and dispatch protections.
- Prove shared budgets across both correction sources; exhaustion records one
  escalation and no claim; ordinary grants do not authorize diagnosis correction.
  Preserve ordinary review/correction/format-repair/authorization tests/contracts.
- Hosted process-double journey: production-written plan/implementation, real
  failed verification and output sealing, diagnosis, explicit correction, new
  Passed verification and ordinary approval. Cover a revised implemented plan,
  diagnosis of a corrected report, escalation and restart with sealed replay.
  No real provider is required or authorized as automated test evidence.
- Prove protected MVC contracts and generated-client serialization over real HTTP.
  Frontend tests cover eligibility, errors/refresh, selection ABA, stale completions
  and truthful budget/ambiguity feedback. Add a browser display/interaction
  regression with owned fixtures and normal auth; label raw-SQL/wire evidence
  separately from hosted production-written flows. Do not invoke a real provider
  in Chromium or weaken host composition/authentication.
- Focused checks first, then normal solution build/client regeneration; full
  Domain/Application/Infrastructure/Api/Architecture suites sequentially; full
  frontend vitest, typecheck, lint/build, audit, harness and Chromium. Compare
  formatter with its recorded baseline; run package vulnerability and complete
  tracked/untracked whitespace/NUL/diff/link checks. Distinguish rerun, retained,
  skipped and blocked evidence; doubles prove no real-provider reliability.
- Return the complete unstaged, uncommitted, unpushed diff and commit-ready
  current-work.md, actual commands/results, inventory and limits for Codex
  GO/NO-GO. Preserve this record. No publication or next slice is authorized.
  After a future GO, one instruction covers the reviewed substantive commit,
  normal fast-forward push, live-remote verification and tightly bounded
  current-work.md-only closure. Material changes after GO return for review.

### Implementation clarification (2026-10-02)

- These are bounded architectural clarifications retained through implementation
  and correction. The current review decision below governs publication.
- Safe backend-authored Problem Details `errors[].detail` is an accepted fallback
  for an unmapped stable error code, consistent with the error-localization
  standard. Render it as text; missing/malformed responses and raw exceptions use
  generic copy. Preserve mapped refusals and persistence-ambiguity wording.
- The diagnosis-correction POST may return either AttemptCreated or Escalated.
  The exhausted-budget control may reuse it: the server owns budget evaluation
  and idempotent escalation recording; UI state grants no authority. Exhaustion
  must produce no Attempt, provider invocation or grant consumption, and the UI
  refreshes authoritative status without claiming that an attempt was created.
- Correct `agent_attempts.provider_not_observed` copy: this code means the required
  runtime is not currently observed as available, not that the implementation
  report lacks provider provenance. Keep the provenance refusal distinct.
- Remove the diagnosis escalation's suggestion to change this run's correction
  allowance: MaximumReviewCorrectionAttempts is immutable per run, and no extra
  correction authorization exists for diagnosis sources. Describe manual review
  and an explicit human decision without inventing a supported continuation.
  Cover both copy corrections and real token-stop codes in focused regressions.

### Review decision (2026-10-02): GO for bounded fixture correction and verified factual closure

- The accepted diagnosis implementation is published as
  0122ebac7e88771e074231fbcf906d0cfd683245, parent
  dc705798f427dcae47cf30fd582a5d680bf55c89 (93 added, 32 modified files).
  Codex independently verified main, HEAD, local origin/main and live
  refs/heads/main all equal the substantive commit. No closure exists yet.
- Corrective review state: nothing staged; four modified tracked files
  (planner-handoff.md, current-work.md, frontend package.json and
  e2e/project-selection.spec.ts), two untracked files
  (e2e/harness/cockpitGate.ts and cockpitGate.test.ts), six reviewed files total.
  Generated-client SHA-256 remains
  bd99dc6a31e0f72fc6051730165b1565c33a95f0f41c602425c28720b286994d.
- The original post-publication Chromium run failed 7/8 with a disposed response.
  The exact historical event order remains unproved. Codex's earlier controlled
  real-HTTP/Playwright diagnostic reproduces the disposal mechanism by closing
  the context before the body read. No product regression was established.
- The corrective diff is accepted: a page-dependent gate fixture guarantees
  shutdown before page/context teardown, including after assertion failure.
  Shutdown sets closing, releases held answers, unregisters interception with
  wait behavior and drains handlers. A fetch/body continuation cannot register
  a hold after shutdown. Fetch/body failures remain recorded and observable;
  the pre-existing fulfill catch and all selection assertions remain unchanged.
  Only fixture/test support, the harness script and documentation change.
- Independent Codex checks against the reviewed correction: harness 25/25,
  typecheck clean, lint 10 baseline warnings/0 errors, full Chromium 8/8 with
  normal authentication and reuseExistingServer: false. Known sandbox Event Log
  restrictions required expanded execution permission, without host changes.
  Two additional controlled real-HTTP/Playwright checks used the actual new gate:
  an already-held response and a body read in flight both drained before context
  closure. No temporary source/test files were created. The six-file NUL and
  trailing-whitespace scan and git diff --check are clean except Git's CRLF
  notice. Executor-reported full vitest/build and focused browser results are
  retained evidence, not independent reruns. Backend suites were not repeated
  for this test-support-only correction.
- Codex clarified current-work.md's initial-publication wording and distinguishes
  the reproduced mechanism from an unproved historical causal sequence. The
  original failed check stays recorded. Controlled lifecycle evidence and green
  browser runs support acceptance, not a claim that intermittency is impossible.
- GO authorizes exactly the six reviewed corrective files, committed as a new
  child of the published substantive commit, normal fast-forward push and live
  verification, then the post-publication checks in the single planner-chat
  instruction. Only after those checks pass, make a current-work.md-only factual
  closure recording the original substantive and corrective SHAs, failed and
  successful checks, retained evidence and limits. Preserve this planner record.
  Stop on failed checks, divergence or material changes; do not amend published
  history, force-push, reconcile history or rerun failures away. Material changes
  after GO require review. Closure/publication of this correction is still pending.
  No next slice is selected, and Increment 4 completion is not claimed.
