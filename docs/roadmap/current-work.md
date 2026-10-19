# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: PLAN-COORD-01 bounded read-only planning coordination

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** completion of the first correction (Codex review 1, NO-GO), unstaged, uncommitted and unpushed; no GO exists.
  Preflight matched the planner's: `main`, HEAD and `origin/main` `1f22a37`, divergence 0/0, empty index, 171 changed files listed in
  `logs/planner-review/plan-coord-01/reviewed-paths.txt`. The planner's documents and ADR-0055 were not rewritten here.
- **Outcome.** One explicit, single-use authorization lets the host itself claim the existing read-only Planner, CriticalReviewer and
  Resolver steps (at most five attempts) until Accepted planning (`ReadyForImplementation`) or the existing depth-two Human escalation
  (`AwaitingHumanDecision`). It never starts implementation, verification, commit or publication
  ([ADR-0055](../decisions/0055-coordinate-one-explicitly-authorized-read-only-planning-sequence.md)).
- **Domain/persistence.** `PlanningSequence` and `PlanningSequenceStep` with fixed reason codes and `PlanningSequencePolicy` (roles,
  contracts, 2-minute preparation and result deadlines); `AgentOutcome.PlanningSequenceDispatchRefused`; six `planning_sequence.*`
  events; additive migration `AddPlanningSequence` with eight database guards (attempt, verification, local-commit and abandonment
  fences, arming, tuple coherence, sequence and step immutability).
- **Application.** Arm, Stop-after-current-step, Observe, Claim (composed through `IApplicationMediator` with an internal ticket so the
  step link commits in the claiming handler's own transaction), preparation resolution, startup interruption and dispatch-refusal
  commands; advanceable and status queries; an independent-connection probe resolves ambiguous commits. Manual Agent claims,
  verification, local commit and abandonment are fenced in both directions while a sequence is live or its attempt is Running.
- **Host.** `PlanningSequenceCoordinator` hosted service; all three supervisors record `PlanningSequenceDispatchRefused`; startup
  interrupts every formerly advancing sequence after attempt reconciliation and before any worker starts. Three protected MVC
  operations (arm, status, stop); the generated client was regenerated and reproduced byte-identically by a non-incremental build.
- **Frontend.** Operation-scoped strict wire clients, owned-lifetime hooks, a Planning sequence panel in the cockpit, and withholding of
  manual actions while a sequence holds the run.
- **Review 1 findings and dispositions (NO-GO; correction of the same slice).** Review: [planner-handoff.md](planner-handoff.md)
  and `logs/planner-review/plan-coord-01/review-01.md` (ignored evidence).
  - **PCO-R1 [P1] late terminal results bypassed the result deadline: corrected.** `PlanningSequencePolicy.IsResultLate` is the one
    deadline test. The observation now judges a Completed attempt by its own recorded completion time before any classification, so a
    completion at or after the deadline (the exact boundary is late) ends the sequence `NeedsAttention` /
    `result_deadline_elapsed` with the step concluded and the terminal attempt and outputs retained; a completion one tick earlier
    stays timely even when observed long after the deadline; a Stop that won stays `Stopped`. The status query reads a late
    completion as attention before persistence.
  - **PCO-R2 [P2] preparation blocked its own deadline observation: corrected.** The coordinator holds `inFlight` only while the
    observation command runs; the one preparation of a reserved step runs on its own task keyed by step, still one claimant, still
    bounded by the four preparation slots, atomic linkage and no retry. A later poll expires a blocked preparation durably; its late
    return loses the claim.
  - **PCO-R5 [P2] deadline status contradicted the frontend contract: corrected.** The query result's top-level state is the
    effective state, so the endpoint's `status` equals `sequence.state` (`recordedState` stays separate and
    `observedBeforePersisted` true). The frontend decoder is unchanged and still strict.
  - **PCO-R3 [P2] routed documentation: corrected.** `engineering-context.md`, `workflow-model.md` (intake and correction
    wording), the ledger, the MVP delivery plan, the protocol and the cockpit specification now state the narrow ADR-0055 capability and
    keep inert intake, immutable modes, no mutating coordination and every incomplete increment/provider boundary. No accepted ADR
    was rewritten.
  - **PCO-R4 [P2] validation provenance: corrected below.**
  - **PCO-C1 accepted nonblocking limitation:** no Resolver production change and no mirror test; the hosted production-mediator
    paths remain the boundary.
- **Correction regressions (permanent).** `PlanningSequenceLateResultTests` (Application; Proposal, Acceptance and Resolution at the
  exact deadline and one tick before it, observation and status, Stop precedence); hosted
  `A_blocked_preparation_is_expired_by_an_independent_observation_and_its_late_return_claims_nothing` and
  `An_expired_deadline_is_reported_with_one_coherent_effective_status_and_the_recorded_state_kept` (both preparation and result
  expiry through the real query and endpoint mapping); a frontend decoder case for a late completed attempt read before persistence;
  the pinned status test now asserts the effective top-level state. Compiling mutants of the observation check, the coordinator
  decoupling and the effective top-level state each failed their regression and were restored by hash.
- **Validation provenance (distinguished).**
  - *Original executor runs, reported to Codex but not saved as log files:* the whole Application run (6515 tests) had **one
    failure**, `GetRunEventHistoryQueryHandlerTests.Every_known_event_type_is_disclosed_exactly_and_the_allow_list_names_every_constant`;
    after the pinned list was updated only the 93-test `GetRunEventHistoryQueryHandlerTests` class was rerun (passed). **No complete
    Application rerun on the final first-delivery inputs was run**, so the first record's "fresh final inputs" wording was wrong. The
    Infrastructure `PlanningSequence|Migration` run first failed 3 tests (two table-set assertions and one schema diff of earlier
    migration tests) before those tests were updated. The Domain, API (410), Architecture, Vitest, lint, build, strict `tsc`, 11-journey,
    NSwag and format results and the four mutation controls were executed in the executor session; their only retained originals are the
    session transcript (`C:\Users\denis\.claude\projects\C--Users-denis-Desktop-AIProjects-DevalCopilot\873cb431-1909-4ac7-b740-dc68b3c4f2cf.jsonl`)
    and, for the controls, the retained copies in
    `logs/executor/plan-coord-01-correction-01/retained-original-control-outputs/` (the first predecessor-control output, which showed that
    mutant surviving the Application tests, was overwritten by the second hosted run and is lost; the loss is not reconstructed). Later passes
    do not authenticate these originals.
  - *Independent Codex review evidence (retained, ignored):* `logs/planner-review/plan-coord-01/` (Application 121/123 with the two
    reproductions failing, API 36/37, Infrastructure 15, frontend 546, Architecture 172, wire reproductions 2 failing).
  - *Fresh correction checks (2026-10-19, on the corrected sources, logs in `logs/executor/plan-coord-01-correction-01/`):* Application
    `--filter FullyQualifiedName~PlanningSequence` first run 127/128 with one failure (the pinned status test asserting the old recorded
    top-level state; `application-planningsequence-01-one-failure-pinned-status-test.log`), then after updating it 128/128
    (`application-planningsequence-02.log`); API `PlanningSequence` 39/39 (`api-planningsequence-02.log`); Infrastructure
    `PlanningSequence` 15/15; Architecture 172/172 (`architecture.log`); Vitest of the seven planning-sequence files 344/344
    (`frontend-planningsequence.log`); `planning-sequence` and `collaboration` journeys 2/2
    (`journey-planning-collaboration.log`); `dotnet format --verify-no-changes --include` over the edited sources clean.
  - *Retained, not rerun:* Domain 1507 predates the additive `IsResultLate` helper (covered by the correction's Application regressions;
    no new full Domain pass is claimed). Unchanged covered inputs: Infrastructure migration/guard, Vitest outside the planning-sequence files,
    strict e2e `tsc`, the other nine journeys, the generated client (byte-identical, no API contract change). *Not run:* the wide API
    regression filter and a full Application rerun after the correction, any real provider, P9 or live-clock wait.
- **Limits.** No real provider, P9 or live-clock deadline wait (deadlines use controllable clocks, the hosted blocked-capture case an
  injected clock). Local-commit handler early refusals are covered by the database trigger, not a handler-level test. Increments 4/5/6
  remain incomplete, ADR-0050 stays Proposed, P9 unproven and AI Memory deferred.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
