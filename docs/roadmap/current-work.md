# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: LOCAL-COORD-01 explicitly authorized local execution sequence (ADR-0060)

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
