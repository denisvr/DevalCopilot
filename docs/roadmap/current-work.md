# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: GLOBAL-LIMIT-01 configurable recorded mutating admission limit (ADR-0059)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**State: implemented by the Claude executor; review 1 was NO-GO (GLL-R1, GLL-R2) and correction 1 is prepared below. Returned unstaged, uncommitted and unpushed. No GO exists.** Baseline: `main`, `HEAD`,
local `origin/main` and the verified live ref all `1747fb2b1d5a4a4a3050fc6b2e2a583473d8a934` (GLOBAL-ADMIT-01, published and
independently verified), divergence 0/0, real index empty. The four planner inputs (the handoff, `decisions/README.md`, ADR-0058's
partial-supersession status and the new ADR-0059) were preserved. Nothing is staged, committed, pushed, reset or cleaned. No dependency,
queue, execution grant, lease, retry, takeover, release, provider experiment or credential operation was added. GAD-R1/GAD-R2 stay
resolved; PCO-C1, ASUP-C1 and WEX-C1 stay accepted. Increments 4/5/6 stay incomplete, ADR-0050 Proposed, P9 unproven, AI Memory deferred.

### What exists

- **Domain / persistence.** `ExecutionCapacityConfigurationRevision` (positive Int64 revision, limit one or two, default two) and the
  additive table `execution_capacity_configurations` (migration `AddExecutionCapacityConfiguration` seeds revision one with limit two;
  the key and check constraints also require the INTEGER storage class; Down drops only the table).
- **Application.** `ExecutionCapacityPolicy.ReadAsync` reads the latest revision fresh and untracked first (missing or malformed latest
  row: Unavailable with no limit, never an older row, a default or a reseed) and judges owners against it; the reading now carries
  limit and revision. `ExecutionCapacityFence` compares against the persisted limit at every early and final claim, dispatch and
  execution check; the feeds use the fence. `SetExecutionCapacityLimitCommand` (manual transaction): unavailable data refuses
  `execution_capacity.unavailable`; the same value is a no-op; a different value below the recorded owners refuses
  `execution_capacity.limit_below_recorded_owners`; otherwise exactly one next revision is appended; revision overflow refuses without
  writing. The fixed `limit_reached` message no longer says two. The `ExecutionCapacityPolicy.Limit` constant is gone.
- **API.** `GET /api/execution-capacity` keeps its four members (`limit` is null only when the configuration cannot be validated).
  `PUT /api/execution-capacity/limit` (queryless, exactly one plain JSON integer `limit` of 1 or 2, 512-byte bound including undeclared
  length, 400/413/415/409 as in ADR-0059) with API-owned request/response, a strict body filter and the regenerated client.
- **Frontend.** The decoder is limit-aware. `useExecutionCapacity` gained `applyLimit` and a change barrier (applying, confirming):
  one PUT at a time, ten-second abort and abort with the lifetime, never retried, old reading masked from submission until a read that
  began after the write transport settled has settled, write answers never trusted as the reading. `ExecutionCapacityLimitControl` is
  shown once below the top bar without a project (current limit, one/two, Apply, Refresh, fixed feedback, "starts no work" disclosure).
  Region guidance names limit one; a new `changing` guidance withholds during the barrier.
- Documentation: ADR-0059 (planner), `engineering-context.md`, `architecture/system-overview.md`, `architecture/data-and-recovery.md`,
  `product/run-cockpit-specification.md`, `roadmap/mvp-delivery-plan.md` and this record.

### Regressions and red evidence

Written before the implementation of the behavior they judge. The original Application red log (`red-application-1.txt`, kept whole) has
**21 failures and 55 passes of 76**: **18 behavioral failures** against the unchanged fixed-two fence and **three setup failures** (a text
`x`, real `5.5` and blob `[5]` revision inserted into the INTEGER PRIMARY KEY rowid alias: SQLite datatype mismatch before any capacity
assertion; not valid red or mutation evidence, removed as impossible; representable malformed-limit tests remain). The decoder/gate
regressions had 22 of 142 failures against the unchanged decoder (`red-frontend-decoder.txt`). The new
hook, wire, control and App tests import modules that did not exist yet, so they had no meaningful red run; the third control proves the
barrier regression. Test repairs (narrow supersession, assertions kept otherwise): `An_empty_database_has_a_limit_of_two...` reads the
seeded limit instead of the removed constant; the route inventory test now expects the GET and the limit PUT; the "limit is exactly 2"
decoder test became "exactly 1 or 2, null only for Unavailable"; two migration `Down` tests list the new table among later tables;
fixtures gained a limit parameter and `FaultInjectingDbContext` the new DbSet.

### Evidence (fresh on final restored sources unless stated)

All logs: `logs/executor/global-limit-01/`.

| Check | Result | Log |
| --- | --- | --- |
| Application, full project | 7061 / 7061 (9 m 47 s) | `final-application-full.txt` |
| API integration, full project | 3050 / 3050 (9 m 11 s) | `final-api-full.txt` |
| Persistence/migration classes (Infrastructure) | 468 / 468; first run 466 / 468, the two hard-coded later-table lists of `AddDiagnosisCorrectionEscalation` and `AddLocalCommit` migration tests (repaired, first log retained) | `final-persistence.txt`, `final-persistence-FIRST-2-failures.txt` |
| Domain, focused new tests | 17 / 17 | `final-domain-focused.txt` |
| Architecture, full | 185 / 185 | `final-architecture.txt` |
| Vitest, whole | 279 files, 6835 tests passed | `final-vitest.txt` |
| Lint / `npm run build` / strict e2e tsc | exit 0 (existing warnings only) / 0 / 0 | `final-lint.txt`, `final-build-fe.txt`, `final-e2e-tsc.txt` |
| NSwag (client deleted, API rebuilt `--no-incremental`) | byte-identical, SHA-256 `23fb89bd…488bb` | `final-nswag-before.txt`, `final-nswag-after.txt` |
| Solution build, non-incremental | 0 warnings, 0 errors | `final-build-solution.txt` |
| Collaboration journey (extended: one/two change, shared disclosure) then planning-sequence journey, serial | both passed (1.9 m, 21 s) | `final-journey-collaboration.txt`, `final-journey-planning-sequence.txt` |
| Changed-file `dotnet format --verify-no-changes` | exit 0 (before the two migration-test list repairs; those edits only add a string) | `format-changed-01.txt` |
| Control 1: persisted comparison replaced by fixed two (compiling) | 33 of 53 filtered tests failed (every limit-one entrance and race) | `control-1-fixed-two-tests.log` |
| Control 2: below-recorded-owners refusal omitted (compiling) | 5 of 23 failed, including the claim-then-lower race | `control-2-no-below-owners-refusal-tests.log` |
| Control 3: stale pre-change read accepted as the confirmation (compiling) | 1 of 22 failed (`does not accept a read that began before the write settled...`); first attempt did not compile (unused variable, retained `attempt1-control-3-build-FAILED-unused-variable.log`) | `control-3-stale-read-across-barrier-tests.log` |

Controls ran serially with a hash guard; baseline and restored hashes matched (`control-baseline.txt`, `run-controls-output.txt`,
`run-control-3-output.txt`); restored sources were touched and the solution rebuilt non-incrementally before the final matrix. Source
hashes were not used as binary proof. Control 1 replaces the single shared fence comparison, so the early and the final comparison
cannot be mutated separately; the late-seam race regressions are what judge the final one.

Not run: no Infrastructure full project beyond the persistence/migration classes, no other nine journeys, no provider or packaged-shell
check, no whole-solution format verify, no new dependency audit. The Application full run and the API full run precede only the two
migration-test string repairs (no Application or API test consumes them).

### Correction 1 (review 1 NO-GO)

- **GLL-R1 (P2, reads not coalesced around the PUT): fixed in the existing hook lifetime.** The read effect now also defers while the
  write is applying (it coalesces polling, Refresh and hints into one queued trigger); a read already pending at submission may settle
  but neither confirms, restores nor rearms polling; when the write transport settles, the queued trigger is consumed by exactly one
  confirming read (behind an older read still in flight when there is one), without a duplicate. Barrier, ten-second aborts, one PUT,
  non-overlapping reads, ordinary polling, failure stop, project independence and stale-lifetime protection are unchanged. New permanent
  regressions (written first; `correction1-red-hook.txt`: 6 of 29 failed, the five new coalescing cases plus the timeout test whose old
  commentary endorsed polling during the write, now asserting exactly two reads): no poll while unsettled and nothing rearmed, poll plus
  repeated Refresh and hints become one confirming read, a pending-at-submission read, an older read in flight at settlement, timeout
  and unmount around the write, ordinary polling afterwards. No new reader, coordinator or queue.
- **GLL-R2 (P3, red accounting): corrected** in the Regressions paragraph above (18 behavioral failures, three invalid setup failures);
  the original log is untouched and no rerun was needed.

Correction evidence (fresh, `logs/executor/global-limit-01/correction1-*`):

| Check | Result | Log |
| --- | --- | --- |
| Ten-file capacity frontend set (two wire contracts, decoder/gates, hook normal/transport/change, control, candidate gate, RunCockpitView, App) | 958 / 958 | `correction1-capacity-set.txt` |
| Whole hooks directory during development | 1602 / 1602 | not retained as a separate log |
| Lint / `npm run build` / strict e2e tsc | exit 0 / 0 / 0 | `correction1-lint.txt`, `correction1-build.txt`, `correction1-e2e-tsc.txt` |
| Control 3 rerun on the corrected hook (compiling) | killed by 1 of 29 (`does not accept a read that began before the write settled...`); baseline and restored hashes matched | `control-correction1-3-stale-read-across-barrier-tests.log`, `correction1-control-baseline.txt`, `run-correction1-control-3-output.txt` |
| Restored hook touched, `tsc -b` and change tests | exit 0; 29 / 29 | `correction1-restored-tsc.txt` |
| Collaboration journey, then planning-sequence journey (serial, unchanged host) | both passed (1.9 m, 20.5 s) | `correction1-journey-collaboration.txt`, `correction1-journey-planning-sequence.txt` |

Retained at their phases (inputs unchanged): full Application 7061, full API 3050, persistence 468 (and its first 466/468 failure),
Domain 17, architecture 185, NSwag byte-identical reproduction, format, controls one and two, the original control-three results and its
invalid first compile attempt. Not run: no whole Vitest, no full Application/API/Infrastructure repeat (hook-only correction), no other
journeys.

### Limits and notes for review

- The count still discloses only recorded facts, not liveness. There is no pending reduction, queue or release; a refused lowering is
  tried again explicitly.
- While a change is in flight the control shows the limit as unknown (the reading is masked). Reads are coalesced around the write (correction 1).
- `PUT` answers are only checked for coherence; the confirming read is the displayed truth, so a write that succeeded but reads back a
  different limit is worded as such.

**Latest independently verified published baseline.** GLOBAL-ADMIT-01 `1747fb2b1d5a4a4a3050fc6b2e2a583473d8a934` (verification:
`logs/planner-review/global-admission-01/publication-verification.json`; this file at `1747fb2` holds its record with pre-review
wording).

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
