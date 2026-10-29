# Planner/reviewer handoff

## Current action: RUN-ABANDON-INSPECT-01

**Review 2 GO for exact-content publication (2026-10-29): RABI-R1 and RABI-V1
are resolved; publication is pending.** [ADR-0071](../decisions/0071-inspect-recorded-run-abandonment-in-history.md)
resolves the complete contract. Planner-owned inputs are that ADR, the decision
index, the MVP delivery plan and this handoff; preserve all four during execution.
No next implementation was dispatched before this selection.

### Review 2 acceptance and publication boundary

Planner confirmed main, HEAD, local origin/main and fresh live main at
`a3f394d6dfee770a378035631f63538471cc60c7`, empty index and 59 pending paths
(15 modified tracked, 44 individually untracked). Against review-01's inventory,
only the owning copy, two existing new component test files, current-work and
the cockpit specification changed, and CopySemantics was added. All other
reviewed content and all four planner inputs matched before deliberate review
edits to this handoff, the decision index and roadmap. The reviewer also
qualified the command-list heading in current-work; these document edits are
included in the external approved identity.

**RABI-R1 resolved.** Common notes are generic/conditional, Unavailable asserts
neither presence nor absence, and positive abandonment/within-read consistency
statements belong only to Recorded. Pending/read-failure/timeout/invalid copy
also avoids presupposing a decision. Only product copy changed; route, statuses,
decoder, hook, lifetime, fields, backend and generated client are unchanged.
Seven permanent cases exercise the real region/transport over a fake network,
including a positive Recorded control. Accept the bounded control B (six
negative cases fail, positive passes) and C (Unavailable alone fails) at those
scopes; control A mostly fails on changed wording and does not isolate the
semantic assertions. Each restored copy hash matches the corrected source.

Independent corrected frontend: **169/169**, eight files, including the seven
permanent regressions and both original reviewer diagnostics with unchanged
archived bytes. The temporary source copy was removed after a hash comparison.
Evidence: `logs/planner-review/run-abandon-inspect-01/review-02/`.
Accept executor corrected frontend **394/394**, 22 files, lint/build and
document checks. The additional cockpit-spec paragraph correction is accepted:
it removes the same inaccurate presence claim from this slice's own description,
aligns it with ADR-0071 and grants no new scope or authority.

**RABI-V1 resolved as a reporting correction; historical evidence loss remains
a nonblocking limitation, not restored compliance or recovered stdout.** The
record now identifies filtered captures, the lost fork build output, additional
unretained development failures and unproved setup causation, and distinguishes
160 executed parameterized frontend cases from the withdrawn title count.
The copied surviving temporary files are post-failure artifacts, with their
manifest checked, never original failure logs. Preserve every captured excerpt
and unknown. The original final-validation tool-use inputs are retained in
review-02/retained-final-validation-invocations.json; current-work labels its
abbreviated list honestly rather than calling placeholders verbatim commands.
Passing cockpit regressions on the published five-key fix remain expected.

Retain reviewer freshly built Application **155/155**, explicit API generation
and full current DLL comparisons, and executor Domain **54**, Application
**273**, API **124**, Architecture **232**, changed-C# format, strict-e2e and
the original headed journey at their unchanged-input scopes. Client SHA-256:
`929e360ba18546d5d9ef74528a5f46fd14f6efd499548dbad3b052e9759c19ba`.
The journey predates this copy correction; it is not a run of the corrected
frontend. No repeat browser/backend/generation run or whole-suite claim is
needed. The fingerprint covers named fields/files, not complete database/file
byte identity; page reload is not host restart. Current on-disk equality does
not authenticate images loaded by a past process.

All review-1 interpretation dispositions below stand within their stated limits.
The shared lone-surrogate parser defect remains open and outside this slice;
no shared-reader, intake/replay or authority change is granted. Preserve the
development/control failures, filtered/lost evidence, first control's limited
sensitivity, five SignalR observations and every prior qualification. There is
no remaining blocking finding for this exact substantive diff.

Publish only the external approved 59-path set with its expected tree and sole
parent in the complete GO. No further source/document edit, build, generation,
browser, cleanup or closure commit during publication. Any mismatch, material
change, failed push or divergence returns to Codex; no recomputation, broader
staging, force-push or history reconciliation. Planner independently verifies
publication afterwards and then checks current chat context usage. No current
context percentage was observed in this correction review; the selection's
earlier observation is not a current percentage or a new continuity decision.

### Review 1 decision and correction boundary

Historical NO-GO, superseded by Review 2 above. Both identifiers and every
evidence limitation remain retained; the old correction instruction is no
longer the current action.

Planner reconciled main, HEAD, local origin/main and fresh live main at
`a3f394d6dfee770a378035631f63538471cc60c7`, empty index and 58 pending paths
(15 modified tracked, 43 individually untracked). All four planner inputs
matched dispatch hashes before this deliberate review edit; only this handoff's
selection hash is superseded. The other three inputs remain unchanged.
Evidence: `logs/planner-review/run-abandon-inspect-01/review-01/`.

**RABI-R1, P2: common and Unavailable copy assert facts not established by the
answer.** The common note says abandonment ended this run's objective and that
the record is internally consistent, while it is mounted for pending, failed,
NotRecorded and Unavailable reads too. A newer Created run with NotRecorded
therefore receives an affirmative abandonment statement. Unavailable copy says
abandonment facts are recorded, but a never-abandoned run with an unrecognized
mode also returns Unavailable without any abandonment column or event present
(existing open-run damage tests prove that case). Keep common explanations
generic/conditional and reserve positive historical assertions for Recorded.
Unavailable must not assert either presence or absence of a decision.

Two independent component diagnostics reproduce both errors (2 failed).
Their exact source is archived as RunAbandonmentInspection.review.test.tsx;
the temporary source copy was removed after a byte/hash comparison. Correct
the owning copy and add permanent component regressions for negative/common
states plus the positive Recorded control. Preserve the existing route,
status semantics, evidence fields, lifetime behavior and journey assertions;
do not fix this by hiding or changing a backend status.

**RABI-V1, P3: qualify retained evidence and execution history accurately.**
Current-work's "All failed output is kept" overstates the supplied retention.
The first Application 19/93 failure and Architecture 3/232 failure were captured
through grep/head pipelines; the fork's first build was displayed through
tail/cut from temporary files. Exact retained tool-result excerpts and their
invocations were located in the executor/fork transcripts and preserved in
retained-executor-failure-tool-results.json and
retained-frontend-failure-tool-results.json. They are not complete unfiltered
process logs, and later passes cannot reconstruct discarded output. State
which originals actually remain available, preserve them if present, and
explicitly record missing/overwritten/unretained originals rather than inventing
them or rerunning a historical failure as a replacement. Retain all excerpts.
Also record exact final check commands/flags and distinguish title counts from
executed parameterized cases. The previously fixed ten cockpit failures remain
published correction evidence at that bounded scope, not unexplained new
failures merely because they pass on this corrected baseline.

Independent frontend feature tests: **160/160** across six files, before the
separate two failing reviewer diagnostics. Independent freshly built
Application feature tests: **155/155**, including the raw storage, full-tick,
default/offset, bounded statement, missing-project, fault and cancellation
cases. An explicit fresh API build/generation passed with 0 warnings/errors
and reproduced the identical client SHA-256
`929e360ba18546d5d9ef74528a5f46fd14f6efd499548dbad3b052e9759c19ba`.
API-test and journey-host references set SkipNSwag=True; unchanged client
snapshots after those builds are not additional generation invocations.
Current full four-DLL hashes match the fresh source outputs; original retained
host comparison printed only hash prefixes. Current equality supports current
on-disk provenance, not authentication of images loaded by a past process.

Retain the executor's Domain 54/Application 273/API 124/Architecture 232,
frontend 387, lint/build, format and one headed journey at their original
scopes, subject to RABI-R1 and accurate RABI-V1 reporting. No independent API,
whole-suite or browser rerun is claimed. No compiling control was required.
Preserve the five SignalR observations and every earlier failure/limit.

Planner dispositions of the executor's interpretations, within these limits:

1. Accept the narrow InvalidOperationException catch around ReadReason only:
   an undecodable lone-surrogate payload is Unavailable here, while database
   and cancellation faults propagate. The shared consumer defect remains open
   and outside this slice; no shared parser or authority change is granted.
2. Accept TEXT-only abandonment time and raw bounded byte reading of the other
   time columns, including their tested BLOB equivalents. This asserts no
   general storage-class authentication for those other columns.
3. Accept provider-writer time forms with supported offsets/default/ticks;
   other parseable textual encodings can be Unavailable in this inspector.
   The shared coherence policy and other consumers' admission stay unchanged.
4. Accept readable registered-root identities/mode before NotRecorded, and
   uppercase stored/lowercase wire forms. This applies to rows found by the
   exact key lookup; no claim is made to discover every corrupted run key or
   alternate association. Do not add a scan or case-insensitive lookup.
5. Accept the additional architecture-test caller lists: the new reader uses
   the unchanged parser/policy as selected, rather than bypassing their pin.
6. Accept current runtime Unicode classification within the recorded version
   limits; do not claim exhaustive cross-runtime Unicode-data equivalence.
   Backend policy remains authority; the frontend can refuse an answer.
7. Accept the browser's Running/with-workspace and newer NotRecorded scope;
   Created/no-workspace and other refusal/race boundaries are proved below
   the browser. No browser red control or host restart is claimed.

Correction validation: focused permanent RABI-R1 cases first, then the original
dispatched frontend filters, lint and product build once on stable sources.
Retain unchanged backend, format, client, strict-e2e and journey results with
explicit input-scope reasoning; the journey predates the copy correction and
must not be called a run of the corrected frontend. No browser rerun is needed
for these copy cases while its existing assertions and inputs remain valid.
Change only operation copy/tests and the implementation report; preserve this
updated handoff. Broaden only for changed dependencies, failures or unresolved
impact. RABI-V1 requires truthful retention/reporting, not a new historical
result or validation campaign. Return for independent correction review.

### Verified baseline and rationale

Planner independently verified COCKPIT-IDENTITY-01 published on main at
`a3f394d6dfee770a378035631f63538471cc60c7`, tree
`699af72af2d7b192c50e8a62db82ff3f30c1a6c9`, sole parent
`a9e06ac634927b097d49f36a7064bd94f0cc46db`, exactly eight approved paths
(six modified, two added). main, HEAD, local origin/main and fresh live main
matched; divergence 0/0, index/working tree/untracked inventory clean before
selection. Records: `logs/planner-review/cockpit-identity-01/publication-verification.json`,
`continuity-after-publication.json` and `review-01/approval.json`.
Do not repeat review, republish or create a closure commit. Preparation-phase
publication wording in the committed checkpoints does not undo Git delivery.

CONTINUE_THIS_CHAT: session identity matched CODEX_THREAD_ID. Observed context
196963 / 475000 = 41.4659%, event 2026-10-28T22:31:13.798Z in
`C:\Users\denis\.codex\sessions\2026\10\28\rollout-2026-10-28T21-57-11-01a18484-64ff-74c2-bb11-33559dc66c2b.jsonl`.
This is the observed planner context after publication verification, not
cumulative usage, account quota or the executor's capacity. No transition
reason exists. Executor continuity is separate: this next slice starts a new
executor chat after the prior publication.

GetManualRunAbandonment combines a recorded reason with live mode/eligibility
and project-wide blocker reads. AbandonRunPanel already renders the current
run's persisted reason/time. ProjectRunHistoryDetail shows only lifecycle/stage
and independent inspectors; recorded event headers deliberately omit payloads.
A newer objective therefore hides the earlier Human closure reason from the
historical product path. Importing the live reader violates that history boundary.

One independent bounded read and its complete selected-history surface are the
largest coherent outcome at this proof boundary. Created/no-workspace and
Running/preserved-stage abandonments stay together. A second current-run panel
would duplicate an existing capability, while broader summaries, replay and
recovery introduce separate proofs/authority. Those are not selected.

### Resolved boundary and implementation scope

ADR-0071 adds exactly one protected bodyless/queryless GET
`/api/runs/{runId}/abandonment-inspection` and a collapsed **Inspect recorded
abandonment** region for every selected historical run, regardless of the
list snapshot's lifecycle/receipt. NotRecorded, Unavailable and Recorded are
independent recorded readings, never live eligibility or a new coherence
verdict for intake/replay. Only Recorded discloses the admitted reason/time
and supporting event identity/Int64 sequence, with the owning project.

One no-tracking scalar statement proves the exact run/project and at most two
exact run-key run.abandoned events. Bound values before materialization, retain
absence/damage markers, and respect the TEXT-only abandonment time mapping.
Reuse the unchanged shared defensive event reason parser and Domain coherence
policy; do not fork, tighten or replace them. Supported full-tick instants
remain supported, including policy-coherent default instants; ADR-0070's
special exclusion is not a shared time policy. The 16 KiB payload cap is a
recorded-inspection bound, not an authority change for other consumers.

Allowed: operation-owned Application/API/frontend types and tests, generated
client registration/regeneration, ProjectRunHistoryDetail and directly affected
history expectations, abandon-run.journey.ts (minimal additive fact read only
if its existing local readers are insufficient), and documentation updates to
data-and-recovery's abandonment section, cockpit specification, security plan,
engineering context and current-work. No shared fixture cache or harness change.
The four planner inputs remain unedited in the review diff.

No Domain/shared abandonment reader/policy, current status/command/panel,
intake/replay, persistence mapping/schema, history paging/source discovery,
shared lifetime/transport, dependency, worker or provider change is selected.
If the proof needs such a change, stop dependent work and present evidence and
a bounded alternative. All standing grants and restrictions below remain.

### Proportional validation matrix

New test classes/names contain RunAbandonmentInspection. Develop against focused
new cases; after stable sources, run this final matrix once:

- Domain: `FullyQualifiedName~RunAbandonmentTests`, retaining the shared rule.
- Application: `FullyQualifiedName~RunAbandonmentInspection|FullyQualifiedName~AbandonmentReadModelTests|FullyQualifiedName~AbandonmentStorageClassTests|FullyQualifiedName~AbandonmentMalformedTimeTests|FullyQualifiedName~GetProjectRunHistoryQueryHandlerTests`.
  Real migrated file-backed SQLite proves production-created closures, every
  status/404 distinction, matching/duplicate/foreign/missing event facts,
  unsupported/oversized storage and exact association, canonical Unicode/LF
  reason and UTF-8 boundary, escaping within the event cap, full-tick equality,
  supported default/offset instants and BLOB timestamp refusal. Capture one
  bounded statement/query plan, forbidden-column and zero-write/external-call
  evidence, later-run/workspace/recipe stability and reopen. Propagate database
  faults; use minimal setup rather than full delivery lineage.
- API: `FullyQualifiedName~RunAbandonmentInspection|FullyQualifiedName~AbandonManualRunEndpointTests|FullyQualifiedName~ProjectRunHistoryEndpointTests|FullyQualifiedName~RunEventHistoryEndpointTests|FullyQualifiedName~LaunchSessionAuthentication`.
  Cover complete auth/verbs/body/query/pre-dispatch/error/wire/OpenAPI matrix,
  protected operation inventory, representative recorded/nonrecorded/unavailable
  answers, missing project/unknown run fixed 404, large exact Int64 string and
  no mutation. Do not duplicate every pure-policy permutation here.
- Whole Architecture; incremental builds/analyzers; changed-C# format and one
  top-level type per matching file. Build/generate serially and prove additive
  reproducible client output. No whole Application/API/Infrastructure suite by
  default while shared producer/persistence/authentication inputs are unchanged.
- Frontend filters: `AbandonmentInspection ProjectRunHistoryPanel ProjectRunHistoryAgentAttempts useProjectRunHistory PlanningSequenceInspectionSurfaces LocalCommitInspectionSurfaces LocalExecutionAdmissionSurfaces LocalExecutionReleaseInspectionSurfaces AbandonRunPanel useManualRunAbandonment useAbandonManualRun describeRunAbandonment layoutBoundary CockpitIdentity`.
  Include every new test. Prove raw-wire whole-answer admission, exact identities,
  Unicode/surrogate/byte and Int64 boundaries, inert multiline reason, canonical
  full-tick UTC including default, actual ten-second abort, reload/close/selection/
  project ABA, stale callbacks/completions and composition with/without receipt.
  Update expected button lists only for this selected new toggle. Run lint and
  product build (includes tsc), plus separate strict e2e checking over current
  e2e/config inventory. No whole Vitest by default.
- Extend/run only abandon-run.journey.ts once, announced and automatically
  headed after binaries stabilize, normal authentication and deterministic
  executables. After second intake settles, baseline all business POSTs,
  invocations, named durable facts and repository/workspace/sealed evidence.
  Inspect earlier exact recorded decision and newer NotRecorded from history,
  await exact responses and assert rendered values, reload/close cold, A-B-A and
  cold after page reload. No added command, invocation or named durable change.
  Captures off; no Inspector/debug/UI/PWDEBUG/manual Resume or redundant run.
- Changed-document links/inbound fragments, full diff and sensitive-data checks.
  One concise command/outcome/elapsed/source log, original failures retained.
  No mutation campaign or per-slice validation framework. Broaden for changed
  consumers/dependencies, failures or unresolved impact, with planner resolution
  before source expansion. Retained evidence keeps actual input scope.

Challenge material flaws before dependent work. Return unstaged, uncommitted and
unpushed for independent GO/NO-GO; update current-work as implementation facts,
not approval/publication. Selection grants no commit or push.

### Delivered evidence and preserved limits

COCKPIT-IDENTITY-01's five key fixes are delivered. Independent frontend 31/31
and strict e2e 54-file checks; executor frontend 1054, lint/build, component red
control and one headed queue journey retain their scopes. The second collision
is behavioral in its mocked composition harness; synthetic projection changes
are not reachable lifecycle transitions or a reproduced real-user incident.
Browser control sensitivity remains unproved; no extra journey is claimed.
Explicit account/model observations were allowed while no Agent-stage invocation,
command, claim or named durable change was added.

Preserve the test-helper failure, aborted control, first CS2012 build failure
with possible lock owners but unproved cause, journey 404/409/SignalR diagnostics
and all original red outputs. Hash differences do not prove prior source mismatch;
before-build equality is reported without an independently retained before-build
hash record. Successful build/current equality supports the later journey, not
historical process authentication. The executor's garbled PowerShell tree readback
and subsequent clean reads are reported, not independently reconstructed; planner
publication verification directly compared cat-file metadata and the GO identity.

Full preceding selection/review and earlier local-commit inspector qualifications
remain in this file at `a3f394d` and in current-work. No historical failure or
missing evidence is resolved/rebuilt by selection. ADR-0069 verifications have
no RunId; time/checkpoint coincidence cannot establish run attribution.

## Retained review and evidence limits

Do not rewrite the published GLOBAL-LIMIT-01 record. Its final independent
frontend evidence is 958/958 plus lint, build/typecheck and strict e2e checking;
two reviewer compiler-invocation failures precede the corrected pass. Full
Application 7061/API 3050 retain unchanged backend-source scope. Persistence 468
retains its first 466/468 failure and accepted table-list repairs. Whole Vitest
6835 is pre-correction evidence, not a fresh whole-suite run. Both corrected
journeys passed serially; the volunteered 1602-hook run has no separate log.

The original Application red run is 21 failures/55 passes: 18 behavioral and
three invalid rowid-insertion setup failures. The original third control's
unused-variable attempt did not compile; its corrected-hook control compiled
and killed its named regression. Preserve their distinct scopes and all
warnings/skips, bounded control sensitivity and source-versus-binary limits.

Detailed prior review and constraints: this file at `7c78c84`,
[current-work](current-work.md#active-evidence-limits),
[WF-04-era limits](planner-handoff-history-e614c79.md#retained-review-and-evidence-limits),
[earlier limits](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits),
and ignored per-slice planner/executor logs. WEX's initial API mutant-provenance
uncertainty and overwritten correction failure log remain. Earlier lost,
derivative, partly retained and chat-only output, invalid/no-op controls,
measurement limits and unexplained failures stay qualified; later passes
reconstruct, authenticate or explain none of them. Frozen snapshots remain
untouched; no historical result is reconstructed by this selection.

## Standing decision constraints

- ADR-0050 stays Proposed; P9 unproven; AI Memory deferred; no mechanism selected. Frozen patches
  A/B/C, C's assertions, write-ahead and sticky-flag alternatives stay
  unapplied/unaccepted. Owner GCM/ancestry-profile and residual name-to-ID risk
  acceptance was design-only; human rearm/resolution is absent.
- One-profile/two-child authority is spent. No native build, launch, profile,
  security, helper, credential or provider operation is renewed. Retained
  profiles, fixtures, DACLs, labels, grants and ignored evidence stay untouched;
  no cleanup, sweep or permission reset. Hashes do not seal live metadata.
- Qualification: `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub
  separate/unstarted; GCM unqualified. Repository access grants no branch push.
- Preserve defects, deviations, lost/pre-redaction output and uncertainty in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md),
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
  [native semantics note](../architecture/github-publication-native-io-semantics.md)
  and [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Earlier excluded-artifact reads grant no new reads. Original/failed evidence
  stays frozen, including the 69-file 11/57 correction.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`,
  unnamed Api 2067/2068 and interrupted reviewer synthetic run stay unexplained.
  Later passes prove neither flakiness nor irrelevance. TEST-48-01 cannot cover
  hung submissions; retention beside disposal failure is unproven.
- Increments 4/5/6 incomplete. Account/context, raw Git read, non-Windows limits:
  [current-work](current-work.md#open-risks). Production code and necessary tests
  are selected only for RUN-ABANDON-INSPECT-01 under ADR-0071's historical
  recorded-abandonment inspection contract; no mutation authority is selected. ADR-0065/0066 retain their delivered release authority.
  PLAN-COORD-01, AGENT-SUP-01, WORKSPACE-EXCL-01, GLOBAL-ADMIT-01,
  GLOBAL-LIMIT-01, LOCAL-COORD-01, LOCAL-INSPECT-01, LOCAL-QUEUE-01,
  LOCAL-QUEUE-INSPECT-01, LOCAL-QUEUE-OVERVIEW-01,
  LOCAL-QUARANTINE-RELEASE-01, LOCAL-RELEASE-INSPECT-01, PLAN-INSPECT-01, PROJECT-VER-INSPECT-01, LOCAL-COMMIT-INSPECT-01 and COCKPIT-IDENTITY-01 are independently verified
  published. Inspection grants no mutation, scheduling,
  executor lease, operation retry/resume, quarantine release, recovery, approval,
  commit or publication authority. ADR-0065/0066 alone own the separately owner-authorized
  terminal-operation release; it renews none of the frozen experiments/grants.
  No dependency, experiment, capture-limit or standards edit is selected.
