# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: WORKSPACE-EXCL-01 workspace execution exclusion (ADR-0057)

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

**State: correction 1 (review 1 NO-GO, WEX-R1 to R4) prepared, returned unstaged, uncommitted and unpushed. No GO exists.**

**Latest prior delivered baseline.** AGENT-SUP-01 `f95e337484d6d50f502647b4abf725be2d7ae184` (sole parent `a78e572c`, 141 paths) passed
independent publication verification (`logs/planner-review/agent-sup-01/publication-verification.json` and `.md`); its record,
committed with pre-review wording, is this file at `f95e337`. Everything below is development work on top of that unchanged published
baseline, not published evidence. At the start of the correction `main`, `HEAD`, local `origin/main` and a fresh live `refs/heads/main`
all equaled `f95e337`, divergence 0/0, real index empty, 99 distinct pending paths (57 modified, 42 new) equal to the reviewed
inventory (`logs/planner-review/workspace-exclusion-01/review-01/reviewed-paths.txt`). The planner documents and every evidence file
were preserved. After the correction there are 110 distinct pending paths (the original 99, four new test-support files and seven
edited frontend test files); nothing is staged, committed, pushed, reset, cleaned or checked out. No schema, dependency, generator
configuration, real-provider experiment, automatic coordination or lease release was added.

### Correction 1: finding dispositions

- **WEX-R1 (foreign association, P2): fixed.** `WorkspaceExecutionPolicy` now also reads a potentially active verification execution or
  open local-commit operation of the requested project that names a workspace the project does not own as an untrusted association
  (`Unavailable`), in addition to another project's row naming the requested workspace. Valid older workspaces of the same project,
  unrelated repositories, terminal rows, exact self-exclusion and the existing commit-reservation rule are unchanged. Regressions added:
  `WorkspaceExecutionPolicyTests` (both families in both directions, a finished foreign row that is history, an older workspace of the
  same project, an unrelated third project), the admission cases
  `ClaimVerificationExecutionWorkspaceExecutionTests.A_verification_of_the_project_naming_a_workspace_of_another_project_refuses_the_claim_as_unavailable`
  and `CreateImplementationAttemptCommandHandlerTests.A_verification_of_the_project_naming_a_workspace_of_another_project_refuses_the_claim_as_unavailable`,
  and four real-endpoint cases in `WorkspaceExecutionStatusEndpointTests` (verification and open commit, each in both directions).
  Red first: the four Application cases failed against the unfixed policy for the finding's reason (`r1-red-application.log`; the
  verification claim case ended in a duplicate-number `DbUpdateException` because the claim proceeded). The four endpoint cases were
  authored before the fix but were first run after it, so their red state was not observed.
- **WEX-R2 (absent UI state, P2): fixed.** `workspaceExecution` is now a required prop of `CandidateWorkspacePanel` and `RunCockpitView`
  (`WorkspaceExecutionState | null`); the gate is always derived and the guidance always shown when withheld. Absent (null or, from a
  JavaScript caller, undefined), pending, failed, replaced and unknown-status states all withhold, an unknown status or failure is
  worded as the incoherent answer, and no refresh button appears without a state. Inspection, Refresh evidence, Stop after current step,
  result recording and abandonment are unchanged. The two tests that pinned "omission enables Run" were replaced by tests that pin the
  opposite (null and omitted, panel and every start scenario of the run cockpit). 211 existing compositions of the two regions in eight
  test files were given an explicit coherent Idle state (`idleStateFor(projectId)` in `workspaceExecutionFixture.ts`), with one
  composition's project and one transient-selection test adjusted (below). `App` already passed the prop: this was a composition
  defect, not an authentication or backend bypass.
- **WEX-R3 (cleanup around the new final reads, P2): fixed.** The post-`BeginTransactionAsync` `CheckClaimAsync` reads in the ordinary
  Implementation, CriticalReview and ReviewCorrection paths are now inside cleanup catches: a cancellation deletes only this
  operation's prepared manifest and propagates unchanged, a database failure deletes it and returns the existing
  `attempts.persistence_failed`, and the uncommitted transaction is disposed (rolled back) in both. The authorized Implementation claim
  holds its own transaction, so its cancellation or read failure is settled from durable state by the existing outer handler (manifest
  removed after a definite not-persisted probe, grant unconsumed, original exception propagated). Every other claim seam
  (Challenge, CodeReview, Planning, VerificationDiagnosis, DiagnosisCorrection, the critical-review repair and step seams) was inspected
  and already reads inside its cleanup catches. Nothing moves external work into a transaction, no other worker's file is deleted, and
  the uncertain-commit readback and retention paths are untouched. Regressions:
  `A_failed_or_cancelled_final_workspace_read_...` in the Implementation, CriticalReview and ReviewCorrection classes and the authorized
  `A_failed_or_cancelled_final_workspace_read_of_an_authorized_claim_...` (Cancel and DatabaseFailure each; the new
  `FenceReadFaultInterceptor` fails exactly the policy's lease read inside the claim transaction and records that it fired). The fix was
  written before these tests, so a red-first run is not claimed; instead a targeted control disabled the three new catches (compiling)
  and the six ordinary regressions failed (`r3-control.log`; the authorized variants are not expected to fail it). The reviewer's own
  cancellation probe (cancel at transaction start) fell under the same catch.
- **WEX-R4 (structure, P3): fixed.** `WorkspaceExecutionScene` moved to `WorkspaceExecutionScene.cs` and `CompetingOwner` to
  `CompetingOwner.cs` with unchanged namespaces and behavior; the new `FenceReadFault` and `FenceReadFaultInterceptor` are likewise one
  type per file. No unrelated split was made.

Planner decisions applied without change: `PlantNewerFailedVerificationAsync` is controlled out-of-band evidence alteration for the two
defensive diagnosis tests (their outcome, invocation and evidence assertions and the real post-terminal verification are preserved; it
does not represent an authorized concurrent execution); the narrower busy-error precedence in three Application test files; the
transaction-or-save `BeforeFirstSaveInterceptor`; the API result retained as the original 2938/2940 full run plus the repaired class's
22/22; the 20 unchanged whole-solution formatting failures preserved.

### What was built (unchanged by the correction except as above)

- One shared Application policy, `Features/Projects/Policies/WorkspaceExecutions` (`WorkspaceExecutionPolicy`, `Fence`, `Feed`,
  `Assessment`, `Status`, `Errors`), derived only from existing rows (Agent attempts, verification executions, the local-commit
  reservation, workspace status). No table, migration, lease, slot counter, queue, Git, provider or process access, and no write.
- Early check before expensive preparation and a final check under the write lock in all eight Agent claim handlers (the seven
  contracts plus the planning claim, including the planning repair and the critical-review ordinary, repair and coordinated-step
  seams) and in the verification claim. The Agent dispatch gate (`MarkAgentAttemptDispatched`) and the verification dispatch authority
  (`VerificationDispatchAuthority`, shared with the pre-dispatch SourceChanged decision) re-check under their lock and exclude only the
  exact operation kind and ID. The seven Agent eligibility feeds and the verification feed only filter known conflicts; they never decide.
- Local-commit interoperability in `LocalCommitAuthorityReader`: preparation requires an Idle workspace and the Committing step requires
  CommitReserved.
- Conflicting or incoherent ownership fails closed with safe Conflict errors (`workspace_execution.*`). Nothing is terminalized,
  cancelled, retried or released, and recording a known result is never gated.
- `GET /api/projects/{projectId}/workspace-execution-status`: protected, bodyless and queryless, API-owned DTO with exactly
  `projectId`, `workspaceId`, `status`, `reasonCode`, closed statuses and reason tuples, non-disclosing 404.
- Frontend: whole-answer decoder, an owned-lifetime hook (5 s refresh, 10 s abort, coalescing, three-failure stop, stale-owner fencing)
  feeding `CandidateWorkspacePanel` and `RunCockpitView` as an additional withholding hint. Idle grants nothing; inspection, Refresh,
  Stop after current step, result recording and abandonment stay enabled.
- Documentation: `engineering-context.md`, `architecture/system-overview.md`, `architecture/data-and-recovery.md`,
  `product/run-cockpit-specification.md`, `roadmap/mvp-delivery-plan.md`, and this record.

Changed paths: `git status --short`; the C# paths are listed in `logs/executor/workspace-excl-01/changed-cs-files.txt`.

### Evidence

All logs are under `logs/executor/workspace-excl-01/`. **Phase** says which sources a run saw: *initial* is the first delivery
(before review 1), *correction 1* is the current sources. Nothing here is published evidence; the published baseline is AGENT-SUP-01.

| Check | Phase | Result | Log |
| --- | --- | --- | --- |
| Application project, full | correction 1, fresh | 6818 / 6818 passed (9 m 15 s) | `corr1-application-full.log` |
| Application project, full | initial, retained | 6804 / 6804 (precedes R1 to R4) | `final-application-full.log` |
| Application, enumerated affected classes (940 tests: policy, claim, dispatch, repair, planning, commit, every final-seam claim class, cancellation, ambiguity, authorization) | correction 1, fresh | 940 / 940 | `corr1-application-targeted.log`, filter `corr1-application-filter.txt` |
| API integration project, full | correction 1, fresh | 2944 / 2944 passed (9 m 39 s); run because the shared authority read changed and because of the stale-binary limit below | `corr1-api-full.log` |
| API integration project, full | initial, retained | 2938 / 2940 (two failures, below); the repaired class re-ran 22 / 22; no clean full pass is claimed from this phase | `final-api-full.log`, `final-api-diagnosis-rerun.log` |
| API targeted (protected status including both foreign directions for verification and open commit, all seven hosted exclusion paths, verification supervisor, planning-sequence hosted behavior, planning request endpoint, complete `VerificationDiagnosisHostedTests`) | correction 1, fresh | 114 / 114 | `corr1-api-targeted.log`, filter `corr1-api-filter.txt` |
| Architecture, full | correction 1, fresh | 185 / 185 | `corr1-architecture.log` |
| `dotnet format --verify-no-changes`, 73 changed C# files | correction 1, fresh | exit 0 | `corr1-format-changed.log` |
| Whole-solution format verify | initial, retained | exit 2, whitespace findings only in 20 paths this slice never touched; not changed | `final-format.log` |
| Vitest, whole, once | correction 1, fresh | 271 files / 6386 tests passed | `corr1-vitest-whole.log` |
| Vitest, whole | initial, retained | 6370 / 6370 on the second run (the first run failed, below) | `final-vitest-whole.log`, `final-vitest-whole-02.log` |
| Lint / production build (`tsc -b` plus `vite build`) / strict e2e tsc | correction 1, fresh | exit 0 (the same nine pre-existing warnings) / exit 0 / exit 0 | `corr1-lint.log`, `corr1-build.log`, `corr1-e2e-tsc.log` |
| Collaboration journey, then planning-sequence journey, serial | correction 1, fresh | both passed (1.7 m, 21 s); the journey host built first without a CS2012 lock | `corr1-journey-host-build.log`, `corr1-journey-collaboration.log`, `corr1-journey-planning-sequence.log` |
| NSwag reproducibility (client deleted, API rebuilt with `--no-incremental`, identical SHA-256) | initial, retained | no API contract changed in the correction; the client is byte-identical (SHA-256 `978e2c30…14dd9`) after the correction's rebuilds | `nswag-before.txt`, `nswag-after.txt` |
| Negative control 1, every Agent final fence omitted (8 compiled mutant files) | correction 1, fresh | killed by 36 tests, with kills in every one of the eight handlers' classes | `negative-controls.log`, `control-1-*.test-output.log` |
| Negative control 2, verification final fence omitted | correction 1, fresh | killed by 4 tests | `negative-controls.log`, `control-2-*.test-output.log` |
| WEX-R3 control, the three new cleanup catches disabled | correction 1, fresh | killed by the 6 ordinary regressions | `r3-control.log`, `r3-control.test-output.log` |

Not run: no live provider, no packaged desktop run, no generator or migration change (none was needed), no broader mutation campaign,
no repeat of the NSwag deletion proof.

Control restoration hashes were verified before and after each control. The controls' filter was widened to the enumerated classes
because a name match on `WorkspaceExecution` does not select the partial-class tests of the claim handlers; the earlier narrow-filter
control run (8 and 4 kills) is retained as `negative-controls-corr1-narrow-filter.log` and `negative-controls-initial.log`.

### Failures, tooling errors and evidence limits retained

- *Baseline full Application run (32 failures, initial phase).* Ordinary-path drift tests hit 30 s lock timeouts because the test hook
  fired at the first save instead of at transaction start; fixed in `ClaudeModelPreferenceTestSupport.BeforeFirstSaveInterceptor` (now
  also a transaction interceptor). Four precedence tests changed expectation (below).
- *First whole Vitest run (initial phase, 5 `RunCockpitView` ADR-0056 failures, "stale reading").* A fixture hard-coded
  `receivedAtMilliseconds: 0` against a 15 s staleness window; `agentSupervisionFixture.ts` now uses `performance.now()`. Original log
  retained in `final-vitest-whole.log`.
- *First full API run (initial phase, two failures).* `VerificationDiagnosisHostedTests` started a real verification while a diagnosis
  attempt was running, which ADR-0057 correctly refuses (`workspace_execution.agent_attempt_active`). The two tests plant the newer
  finished execution directly (`PlantNewerFailedVerificationAsync`); the fresh-diagnosis tail runs a real newer verification after the
  attempt ended.
- *Stale mutant binaries after the initial controls (tooling error, discovered in the correction).* The initial controls restored the
  original bytes with their old timestamps, so the following "clean rebuild" took under a second and did not recompile: MSBuild kept
  the mutant assemblies. The kills themselves were real (the mutants compiled and failed tests). The correction's own first targeted
  Application run after its R3 control definitely ran the mutant assembly (6 failures of the new cleanup regressions; its log
  `corr1-application-targeted.log` was overwritten by the passing rerun). The initial-phase full API run (`final-api-full.log`) started
  after the initial controls and may likewise have used mutant Application assemblies; that cannot now be proved or excluded (the
  later NSwag rebuild with `--no-incremental` rebuilt everything). Both harnesses now `touch` the restored files, the mutated sources were touched and
  rebuilt, and all correction-phase evidence above ran after that. The initial API evidence is therefore retained with this limit, and
  the correction-phase full API run replaces it as fresh evidence.
- *Reviewer tooling error (retained from review 1).* The first review probe build failed on an omitted namespace import; not a product result.
- *Earlier correction runs.* A first R3 control attempt did not compile (a definitely-assigned local, then an unreachable-code
  diagnostic) and was not a kill; the compiling form is the recorded one.

### Precedence changes (intended, documented in the tests)

A running or undispatched competing owner is now refused before older drift or corruption evidence in four existing tests:
`CreateVerificationDiagnosisAttemptCommandHandlerTests` (running execution), `DiagnosisCorrectionChainTests` (Running), and two in
`PlanningLineageIntegrityTests` (`UnreadableParticipatingRow`; `AssertFixedRefusal` gained a corruption parameter). Accepted by review 1.

One further expectation changed in the correction: `RunCockpitView`'s "never carries a previously selected run's global claim block"
test now sees the workspace gate's pending guidance (instead of the budget-unconfirmed wording) on the transient frame whose cockpit
projection still belongs to the previous run, because that frame has no project to judge and the gate is the earlier veto. Nothing is
offered on that frame, as before.

### Subagent work

The frontend work of the initial delivery was delegated to a subagent whose report is untrusted evidence; its output was verified by the
whole Vitest run, lint, build, strict e2e tsc and the journeys above. The correction was done directly in this chat.

### Limits

- Process attempts are not part of the exclusion; the policy covers Agent attempts, verification and the local-commit reservation only.
- There is no scheduler or queue: a refused claim is a safe Conflict and nothing is retried or released on its behalf.
- A pending re-read keeps the last answer (Idle grants nothing); the claim-time checks, not the hint, decide.
- The UI does not bind the status to a workspace id (a project-owned point-in-time latest-workspace hint); the extended collaboration
  journey asserts the selected workspace instead. This is neither source nor workspace attestation nor permission.
- The disclosure is a required prop of the two regions; absence withholds. The hint is additional withholding only and never authorizes
  an action.
- The authorized Implementation claim surfaces a raw database failure of the final read as the original exception (after the outer
  handler's durable settlement), as it already does for its other late reads, rather than as a Failure result.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
