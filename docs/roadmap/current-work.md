# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: GLOBAL-ADMIT-01 global mutating admission (ADR-0058)

This document records the implementation delivered for review. Consult [planner-handoff.md](planner-handoff.md) for the review decision.
Reconcile publication with Git. This record alone grants no publication authority.

**State: correction 1 (review 1 NO-GO, GAD-R1 and GAD-R2) prepared by the replacement executor, returned unstaged, uncommitted and
unpushed. No GO exists.** The pending set at the start of the correction was the 90 reviewed paths
(`logs/planner-review/global-admission-01/review-01/reviewed-paths.txt`); the correction adds
`AgentDispatchStaleClassificationTests.cs`, `OwnerFamily.cs` and `PlacedOwner.cs` and edits the dispatch handler, `ExecutionCapacitySeed.cs`
and this record.

### Correction 1: finding dispositions

- **GAD-R1 (P2, stale tracked ReadOnly classification at dispatch): fixed.** `MarkAgentAttemptDispatched` already read the persisted
  assignment afresh and untracked; it now also requires the tracked attempt's role and response contract to equal that current pair
  before the guidance check, any role-specific branch or the capacity exemption. A mismatch returns the existing fixed
  `agent_attempts.invalid_agent_contract` Conflict, whatever the occupancy; the tracked entity is only read, so pending tracked writes
  are untouched. The exemption therefore rests on a currently established coherent ReadOnly pair. Regressions (new class
  `AgentDispatchStaleClassificationTests`, written first): stale ReadOnly over a current mutating pair at full and at free capacity,
  the reciprocal drift (stale mutating over a current ReadOnly pair), a role-only drift, and the positive cases (a current ReadOnly
  pair dispatches at full capacity; a current mutating pair still judges capacity). Each refusal asserts the fixed code, unchanged
  tracked state, no dispatch marker, no outcome, an unchanged stored tuple and no new event. Red first: against the unfixed handler four
  of the six failed for the finding's reason (`gad-r1-red-02.log`; the first attempt `gad-r1-red-01.log` did not compile because of a
  wrong type in my test); after the fix 6 / 6 (`gad-r1-green-01.log`). The reviewer's reproduction (`reproduction-stale-classification.cs`)
  was not copied into the suite; its capacity-code assertion does not prescribe this correction's mismatch code.
- **GAD-R2 (P3, one top-level type per file): fixed.** `OwnerFamily` and `PlacedOwner` moved to `OwnerFamily.cs` and `PlacedOwner.cs`
  with unchanged namespace, visibility and behavior; `ExecutionCapacitySeed.cs` holds only `ExecutionCapacitySeed`. Compilation and the
  isolated per-test databases are preserved; no mirror test.

Accepted decisions applied: the six test repairs stand with their assertions. Corrected rationale: the two neighboring-owner tests
(`A_competing_owner_in_another_repository_...`, `Terminal_history_and_another_repository_never_refuse_the_claim`) had an Agent and a
verification owner on one neighboring workspace (conflicting owners), not merely too many owners; the mode and guidance cases test their
assertions in two recorded-state rounds. The direct terminal-status updates in the new tests prove database facts, not real process
termination.

**Latest independently verified published baseline.** WORKSPACE-EXCL-01 `b3d53bfc3c604711228ade769284911f9d9c2b4b` (this file at `b3d53bf`
holds its record with pre-review wording; verification: `logs/planner-review/workspace-exclusion-01/publication-verification.json`).
At recovery `main`, `HEAD`, local `origin/main` and live `main` all equaled `b3d53bf`, divergence 0/0, real index empty, 73 pending
paths (36 modified, 37 new) equal to `logs/planner-review/global-admission-01/recovery-01-paths.txt`
(SHA-256 `05d91171…e8835`). Everything below is unpublished development work. The original executor ran out of credits mid-slice;
only the replacement continued (`recovery-01.md`). Nothing is staged, committed, pushed, reset or cleaned. No schema, dependency,
configuration, queue, execution grant, lease, retry, takeover, release or real-provider experiment was added. The earlier WEX-C1,
ASUP-C1 and PCO-C1 acceptances, the earlier evidence losses and all standing restrictions are unchanged. Increments 4/5/6 stay
incomplete, ADR-0050 Proposed, P9 unproven, AI Memory deferred.

### What exists

- `Features/Runs/Policies/ExecutionCapacity` (`ExecutionCapacityPolicy`, `Fence`, `Feed`, `Reading`, `Status`, `Errors`, owner types):
  one fresh untracked reading of coherent unresolved WorkspaceMutating Agent attempts (Domain's table), verification executions and
  local-commit reservations; limit two; Unavailable for unknown, incoherent or foreign ownership; recognized terminal exclusion only.
- Early and write-locked checks in ordinary and authorized Implementation, ReviewCorrection, DiagnosisCorrection, verification and
  local-commit admission; dispatch and execution gates (Agent, verification, local commit) with exact kind-and-ID self-exclusion; the
  Agent, verification and local-commit feeds only filter known refusals. Fixed 409 codes `execution_capacity.limit_reached` and
  `execution_capacity.unavailable`.
- `GET /api/execution-capacity` (protected, bodyless, queryless, four-member closed wire), generated client, frontend decoder, hook
  (host-session lifetime, 5 s poll, 10 s abort, coalescing, three-failure stop, stale-remount fencing) and guidance in both regions;
  only new mutating starts are withheld (diagnosis separated from correction).
- Documentation: ADR-0058 and its index entry, `engineering-context.md`, `architecture/system-overview.md`,
  `architecture/data-and-recovery.md`, `product/run-cockpit-specification.md`, `roadmap/mvp-delivery-plan.md`, the planner handoff
  and this record.

### Work done by the replacement executor

- Reconciled the partial diff with the recovery inventory (identical path set) and read the retained logs. `green-01-application.log`
  (1 failed, 9 passed: `A_database_already_over_capacity_...`) is **not** green. Its failure was an `Assert.Equal` on a snapshot record
  holding `List` members (reference equality). The current test snapshots strings and passes in the final runs below. The other early
  failures stay retained and are not explained beyond their logs.
- Added missing coverage: `LocalCommitGlobalAdmissionTests` (real host and Git: refusal before preparation, locked-seam race,
  unattestable ownership, exact self-exclusion at execution, refusal beside two others leaving the operation Prepared, execution once a
  place is freed); `GlobalAdmissionHostedScenarios` run against the real Implementation and ReviewCorrection supervisors (waits with no
  capture or adapter call, then runs once with the baseline outcome) and the ReadOnly code-review and verification-diagnosis supervisors
  (unchanged at full capacity); two hosted verification-supervisor tests (waits beside two recorded owners and launches once one
  ends; a mediator claim is refused with nothing consumed).
- The first final full Application run exposed six existing tests whose fixtures ADR-0058 now refuses or does not propose (corrected
  rationale: the two neighboring-owner tests put conflicting Agent and verification owners on one neighboring workspace; the feed
  cases claim four or three mutating attempts at once). Repaired in the tests only: `A_competing_owner_in_another_repository_does_not_delay_or_refuse_the_claim`
  and `Terminal_history_and_another_repository_never_refuse_the_claim` (one unrelated owner instead of two),
  `ExecutionModeFeedIsolationTests.Each_agent_feed_hands_out_only_manual_and_legacy_runs` and
  `DirectHumanGuidanceDispatchBoundaryTests.Both_feeds_project_the_claimed_text_or_none_and_a_legacy_attempt_stays_eligible`
  (claims are made in two rounds with the first genuinely ended). Their mode and guidance assertions are preserved.

### Evidence

All logs are under `logs/executor/global-admit-01/`; none overwrote an earlier failure. Phase says which sources a run saw.

| Check | Phase | Result | Log |
| --- | --- | --- | --- |
| Application, full | first final run | 6951 / 6957, 6 failures (the six tests above), retained | `final-application-tests.log` |
| Application, full | final sources (after the six test repairs) | 6957 / 6957 (9 m 27 s) | `final-application-tests-02.log`, `final-application-refix-01.log` |
| Application, 110 capacity and admission tests | after formatting two new test files | 110 / 110 | `final-application-postformat.log` |
| API integration, full | final product sources | 2978 / 2978 (10 m 35 s) | `final-api-integrationtests.log` |
| New API tests | development | local commit 5/5, hosted supervisors 4/4, verification supervisor class 35/35 (a first run had 2 failures from my fixture planting a launchable verification; retained) | `rec-commit-02.log`, `rec-hosted-01.log`, `rec-hosted-02.log`, `rec-hosted-03.log` |
| Architecture, full | final and after doc edits | 185 / 185 both | `final-architecture-tests.log`, `final-architecture-02.log` |
| Domain, full | final | 1547 / 1547 | `final-domain-tests.log` |
| Infrastructure integration, full | final | 3155 passed, 4 skipped | `final-infrastructure-integrationtests.log` |
| Vitest, whole | final frontend sources | 276 files, 6743 tests passed | `rec-vitest-01.log` |
| Lint / `npm run build` (tsc -b and vite) / strict e2e tsc | final frontend sources | exit 0 (nine warnings, all in unchanged files) / 0 / 0 | `rec-lint-01.log`, `rec-build-fe-01.log`, `rec-e2e-tsc-01.log` |
| NSwag reproducibility (client deleted, API rebuilt `--no-incremental`) | final | byte-identical, SHA-256 `be6f7a74…01201` | `final-nswag-before.txt`, `final-nswag-after.txt` |
| Collaboration then planning-sequence journey, serial | final binaries | both passed (1.9 m, 21 s) | `final-journey-collaboration.log`, `final-journey-planning-sequence.log` |
| Control 1: Agent write-locked capacity check omitted (compiling) | same product sources as final | killed by 3 named tests (two last-place-before-seam cases and one cross-family race) | `rec-control-agent-final-gate.log` |
| Control 2: verification locked capacity check omitted (compiling) | same | killed by 2 tests (race, last place taken before the lock) | `rec-control-verification-final-gate.log` |
| Changed-file `dotnet format --verify-no-changes` | final | first run exit 2 for two new test files only (retained); formatted; second run exit 0 | `final-format-changed-01-FAILED.log`, `final-format-changed-02.log` |
| `dotnet list package --vulnerable`, `npm audit --omit=dev` | final | none / 0 vulnerabilities | `final-vuln-dotnet.log`, `final-npm-audit.log` |

Controls: hashes before and after matched, the restored sources were touched and rebuilt before the final matrix, and the produced
assemblies were verified newer than the restored sources. The final full runs followed the restoration. The first whole-solution build
after it failed once on a transient `rjsmrazor.dswa.cache.json` file lock and succeeded on the immediate retry (`final-build-01.log`,
`final-build-02.log`).

Not run: no live provider, no packaged shell, no whole-solution format verify (the earlier 20 unrelated whitespace findings stand), no
repeat of the other nine journeys, no broader mutation campaign. The full API run precedes the formatting-only edit to two Application
test files and the six Application test repairs, which no API test consumes (accepted by review 1).

### Correction 1: validation by phase

Fresh, on the correction sources (`logs/executor/global-admit-01/`):

| Check | Result | Log |
| --- | --- | --- |
| GAD-R1 regressions, red then green | 4 failed / 2 passed before the fix; 6 / 6 after | `gad-r1-red-02.log`, `gad-r1-green-01.log` |
| Affected dispatch consumers, Application project (868 tests: every class invoking the dispatch handler, mode, guidance, turn-limit, account-usage, workspace, abandonment, repair, diagnosis, capacity/policy/feed, claim-time global admission) | 868 / 868 (2 m 7 s) | `gad-affected-Application.Tests.log`, filter `gad-r1-filter.txt` |
| Same filter, API project (all seven real Agent supervisor compositions with their dispatch cases, hosted authorization, verification supervisor, local-commit global admission) | 339 / 339 (1 m 21 s) | `gad-affected-Api.IntegrationTests.log` |
| Solution build | 0 warnings, 0 errors | `gad-build-01.log` |
| Architecture, full | 185 / 185 | `gad-architecture.log` |
| `dotnet format --verify-no-changes`, 55 changed C# files | exit 0 | `gad-format-01.log` |
| Collaboration then planning-sequence journey, serial, rebuilt host | both passed (1.9 m, 22 s) | `gad-journey-collaboration.log`, `gad-journey-planning-sequence.log` |

Retained at their recorded phases (inputs unchanged by the correction): full Application 6957 / 6957 and full API 2978 / 2978 (pre-correction
product sources; the correction changes only the dispatch handler, whose consumers are re-run above), Domain, Infrastructure, whole Vitest,
lint, build, strict e2e tsc, NSwag byte-identical reproduction, audits, and the two claim controls (Agent final check of ordinary
Implementation only, which proves neither every Agent entrance nor this dispatch exemption, and the verification control). Not run: no
full-project repeat of Application or API, no new mutation control for the dispatch exemption. The first-attempt red log and every earlier
failed log remain. Binaries were rebuilt by `dotnet build` from the edited sources; source hashes alone were not used as proof.

### Limits

- The count discloses only this policy's recorded facts, not liveness, simultaneous activity or a frozen cross-family snapshot. There
  is no queue, configurable limit or release, and a refused operation is never retried.
- The earlier failing development logs (`red-01`, `green-01`, compilation, policy-03, first lint and e2e tsc) are original evidence of
  intermediate states; the final results above supersede them only for the sources they ran on.
- The previous executor's work cannot be authenticated beyond its retained logs; missing worker output stays unknown.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
