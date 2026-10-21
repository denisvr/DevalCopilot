# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and the [workflow](development-workflow.md) apply.
Codex remains planner/reviewer and performed architecture, selection and review
judgment itself. LOCAL-COORD-01 correction 2 passed **review 3: GO** for the
exact approved content identified in the external review-03 publication record
and complete GO prompt. LCO-R1 through LCO-R4 and LCO-V1 are resolved.
Publication remains pending in the same Claude executor chat; the reviewed
diff is unstaged, uncommitted and unpushed at this checkpoint. Codex made
only review-document edits, with no product correction. Temporary reviewer tests were compiled, retained under
ignored review evidence and removed from the test project before a fresh build.
No provider experiment, staging, commit or push was performed in this chat.

## Verified baseline and executor preflight

Startup reconciliation confirms **main**, HEAD and local origin/main
**`7c78c849fdd556ecf3a539dce479e31fc3cc501b`**, tree
`136936b465d48753b4e96e6ba3557d527179acd1`, sole parent
`1747fb2b1d5a4a4a3050fc6b2e2a583473d8a934`, divergence 0/0, empty real
index and initially clean working tree including nonignored untracked paths.
A fresh `git ls-remote origin refs/heads/main` confirms that same published
commit. The first sandboxed network read failed with getaddrinfo; the permitted
read outside the sandbox succeeded. No Git reconciliation was needed.

GLOBAL-LIMIT-01 is published and independently verified, not reopened. Exact
70 paths (45 M, 25 A), approved inventory SHA-256
`894d05914e8174fb82d96a8a39813aa699cfcda4699586c528f1f5383ec9751f`, tree and
parent are retained in `logs/planner-review/global-limit-01/publication-verification.json`
and `.md`. The committed implementation/review wording records its preparation
phase; it requires no closure edit or commit. Full prior review decisions are
this file at `7c78c84` and the ignored per-slice records, not repeated here.

The original selection supplied these five planner inputs, preserved by the
executor (this handoff is now updated by Codex for review 3):

- this file;
- `docs/decisions/README.md`;
- ADR-0057 and ADR-0058 status lines only;
- new accepted ADR-0060.

The five-path selection preflight is historical. Independent review preflight
now confirms main, the same HEAD/local/live origin/main, divergence 0/0 and an
empty real index, with **313 distinct pending paths: 124 modified, 189 added**.
All are under the
selected docs/src/tests areas. Exact submitted inventory:
`logs/planner-review/local-coord-01/review-03/reviewed-paths.txt`. The final
approved path set and expected tree are in that directory's external GO record;
they are not embedded in this tracked file. The temporary reviewer tests are absent.
Before publication, the same executor verifies the exact GO preconditions and
identity once and investigates a discrepancy without resetting or cleaning.
Selection and review evidence live under `logs/planner-review/local-coord-01/`.
No local logs are publication authority or authentication of historical binaries.

## Continuity and owner authority

This is the new Codex chat required after GLOBAL-LIMIT-01 publication: the
outgoing chat's observed 2026-10-20T20:43:30.586Z token_count metadata was
303895 / 475000 = 63.98%, above the mandatory 60% threshold. It selected no
next slice. Its older 17.01% checkpoint observation is historical. No current
context percentage is available to this chat; none is inferred from those
readings. No new publication occurred here to trigger another continuity gate.

On 2026-10-20, after the planner identified the missing mutation authority, the
owner explicitly answered **Authorize that bounded local cycle**: design a
single opt-in authorization for an accepted plan, implementation, the pinned
verification set, review and at most two shared corrections, including eligible
failure diagnosis, ending at approved review or escalation. Commit/publication
remain separate. This is the new authority decision required by the transition,
not an inference from planning, supervision, admission or settings. It grants no
real-provider run or experiment in this chat and no repository publication GO.
The completed GLOBAL-LIMIT-01 Claude chat must not be reused; LOCAL-COORD-01
starts in a new Claude executor chat and keeps its corrections there.

## Remaining MVP critical path

| Front | Delivery and remaining boundary |
| --- | --- |
| Planning | Published ADR-0055 coordinates at most five ReadOnly attempts, ending at Accepted planning or Human escalation. Its grant still starts no mutation. |
| Supervision | Published ADR-0056 reports host observations for seven contracts. It proves neither process liveness nor provider progress and remains an eligibility non-input. |
| Exclusion/capacity | Published ADR-0057/58/59 provide reciprocal workspace exclusion and a fresh durable operator-selected global limit of one/two. They provide no queue, execution grant or process-aware recovery. |
| Local coordination | The explicit primitives exist, but the host advances no implementation/verification/review/correction chain. LOCAL-COORD-01 now selects that bounded observable outcome under the owner's new decision. |
| Queue/recovery | Durable pending admission, queue pinning/revalidation/fairness, finite execution leases, cancellation/retry/takeover and process-aware recovery remain separate unresolved contracts. This slice fails closed instead of guessing release. |
| Providers/remote | Claude account usage, session resume, live context, manual compaction, permission profiles, raw Git/non-Windows proof and separate Git/GitHub/GCM qualification remain open. ADR-0050 Proposed, P9 unproven, AI Memory deferred. |

Increments 4/5/6 remain incomplete; no completion percentage or qualification
is claimed. Accepted upstream findings remain resolved: PCO-R1-R5, ASUP-R1-R5,
WEX-R1-R4, GAD-R1/R2 and GLL-R1/R2. PCO-C1, ASUP-C1 and WEX-C1 remain
accepted nonblocking limitations, never new authority.

## Selected slice: LOCAL-COORD-01

Implement accepted [ADR-0060](../decisions/0060-coordinate-one-explicitly-authorized-local-execution-sequence.md):
one Human authorization of one accepted plan advances initial implementation,
whole pinned verification passes, ordinary review or eligible failure diagnosis,
and up to two shared correction claims, until exact approved review awaiting
Human approval or a bounded escalation/attention outcome. No automatic approval,
commit or remote action occurs.

**Sizing judgment.** Both correction origins share the same report lineage,
verification snapshots and allowance; separating them would leave one ordinary
failure branch requiring manual stage transfer. Include their persistence,
atomic claim links, hosted observation, cockpit and conservative restart safety
in one reviewable outcome. A backend-only or implementation-only slice fails
that outcome. Queueing, full leases/recovery and publication require different
process/admission/authority evidence and are excluded for those concrete
reasons, not to meet a file or task count.

ADR-0060 owns the resolved details. Dispatch essentials:

- At most one lifetime sequence per ManualAgent run, no prior mutating Agent
  claim, exact Accepted plan (or separately Human-authorized depth-two plan),
  latest owned source, 1-32 complete enabled recipes and explicit confirmation
  binding full recipe/run-setting snapshots. Arming starts/claims nothing.
- Immutable relational membership, unique predetermined operation IDs and
  original-host preparation tokens. Existing claims through the mediator link
  step/operation/inputs/artifacts/budgets/grants atomically. One outstanding
  operation, no claim-then-link, adopted manual attempt or retry after ambiguity.
- Every pass executes every pinned recipe once in order. All Passed routes to
  ordinary review; only ADR-0018's coherent nonzero-exit set routes to diagnosis.
  Exact whole findings select the matching correction origin. At most two
  shared corrections, three passes, six Agent claims and 102 steps; existing
  smaller budgets and guards remain decisive. Two-hour advancement deadline.
- Manual coexistence fences all claim/repair entrances and competing checkpoint,
  recipe/run-setting mutations at their final seams. Original result recording
  stays available. Stop after current operation never means process cancellation.
- ADR-0057/58's narrow ownership extension counts a linked dispatched operation
  without coherent host Exited proof even if a terminal row exists. Use one
  shared reading, deduplicate the ordinary owner, retain ReadOnly workspace
  exclusion and mutating global count, preserve ADR-0059 freshness/safe lowering.
- Close continuation before startup recovery. Never redispatch/adopt; preserve
  unresolved dispatched rows, ownership and possibly live output instead of
  blindly interrupting/importing them. No supported recovery/release exists
  for ambiguity. Exited proof is about the invoked process, not every descendant.
- Four protected operations (availability, arm, status, stop), closed exact wire,
  one run-owned status/fence across both regions, confirmation barriers and
  stale visit/lifetime protection. No Running indicator from lifecycle or host
  observations. No browser advancement or persisted browser authority.

Allowed: feature-owned Domain/Application/API/Infrastructure/frontend code,
necessary tests/migration/generated client, narrowly affected startup/shared
ownership consumers and directly affected product/architecture/security/context/
roadmap contracts. Preserve this planner selection and accepted decisions in
the returned diff. No generic workflow/settings/recovery engine, transaction or
supervisor throughput rewrite, broad fixture cache, dependency or standards edit.

## Proportional validation and acceptance

First identify exact focused classes/filters and record them before editing.
Use the cheapest boundary for detailed permutations; no simultaneous build,
generator or mutation writers. Full logs retain failures and actual phases.

| Changed boundary | Focused development evidence | Final stable-source matrix |
| --- | --- | --- |
| Domain/claims/sequence/ownership | New `LocalExecutionSequence` classes, affected implementation/review/diagnosis/correction and verification claims, dispatch, stop, source lineage and both capacity orderings; real SQLite independent connections | Full Application and API projects once because all entrances, startup and shared owner policy change; focused Domain and full architecture |
| Migration/transaction/startup | Feature migration constraints, upgrade/reopen/down; atomic link, budget/grant/artifact rollback and uncertainty; every startup boundary, unchanged fingerprint with unresolved dispatched owner, terminal-without-exit quarantine | All Infrastructure `Persistence` classes and affected transaction-boundary classes; broaden to adapters/process/Git only if their inputs change or evidence raises a concern |
| HTTP/wire/UI | Four complete endpoint contracts/auth inventory, raw-wire whole-tuple decoding, exact confirmation and both regions, stale A-B-A/remount, stop/arm/deadline races and actual cancellation | Generated client reproducibility, whole Vitest, lint, build (includes product typecheck), strict explicit-source e2e TypeScript including both Playwright configs |
| Hosted outcome/coexistence | Production claim/result paths for no correction, each correction origin, mixed origins/exhaustion, independent projects, zero forbidden calls | New authenticated `local-execution-sequence.journey.ts`, then existing collaboration and planning-sequence journeys serially on stable binaries |

Expected command families: `dotnet test` for the named test projects with focused
`FullyQualifiedName~LocalExecutionSequence` and affected existing class filters,
then full Application/API/architecture projects; Infrastructure final filter
`FullyQualifiedName~Persistence` plus the enumerated affected transaction classes.
Build current inputs before any `--no-build --no-restore` run. Frontend final
`npm test`, `npm run lint`, `npm run build`; strict e2e compiler uses the existing
explicit-source ES2023/ESNext/bundler/DOM/node-vite/JSX configuration with
`--ignoreConfig`, not a nonexistent tsconfig.e2e.json. Existing receipt:
`logs/planner-review/global-limit-01/review-02/e2e-final-receipt.json`.
Journeys use `npx playwright test -c playwright.journey.config.ts <one file>`.
Formatter/analyzers, applicable audit/secret checks, changed-document links,
complete diff and mandatory CI/security/release gates remain.

Four serial compiling controls must kill named regressions: atomic operation
link, exact pass/recipe membership, original-host/startup quarantine and stale
frontend confirmation/fence. Retain invalid/failed controls; restore hashes,
touch restored sources and rebuild before final evidence. No source hash
proves a historical binary. No new exact test count or performance promise.

Update current-work as implementation-for-review with exact commands, timings,
source-phased fresh/retained/not-run results, failures/challenges, blockers and
Git state. Return everything unstaged, uncommitted and unpushed for Codex review.
No GO exists for LOCAL-COORD-01; material design challenges pause dependent work.

## LOCAL-COORD-01 review 3: GO, publication pending

Correction 2 was independently reviewed on 2026-10-21. LCO-R2 is resolved:
both Agent final proofs compare the step's exact source fingerprint, and the
provider diagnosis Escalation uses the same exact report-input and recorded
ordered-pass proof as approval. Coordinator classification and later disclosure
share it. The immutable recorded result remains unchanged when later evidence
cannot be proved. LCO-R1, LCO-R3 and LCO-R4 remain resolved.

LCO-V1 is resolved within its selected ADR-0043 bounds: the provider-neutral
bounded Concat projection reads both candidate sets in one statement, with
all existing validation preserved. No observer-time transaction, retry, new
lock or port was introduced. Three permanent tests prove a competing commit
at the controlled hook; the fourth explicitly commits between two query reads.
All assert the joint candidate statement and coherent results. The old-reader
control kills four structural single-statement assertions and separately the
retained reviewer's actual unavailable-response regression; these are distinct
forms of sensitivity, not five observed mixed-read responses.

Fresh independent verification: all **six original Application reviewer
regressions now pass** (3 s), including ordinary-EF argument drift, missing
approval, moved-run startup refusal, source fingerprint at classification and
disclosure, and missing diagnosis input. Only the exact reviewer-created files
were removed after comparison with retained source hashes. A fresh subsequent
build passed the permanent final-evidence classes **51/51** (20 s); independent
API binding-consistency/protected final-status classes passed **8/8** (27 s).
No independent rerun of the now-vacuous old API scheduling probe is claimed.

Inspected executor logs establish clean correction-2 full Application
**7589/7589** and API **3170/3170**, plus Domain 1732, architecture 185,
Persistence 631 and seven transaction classes 22. The three required serial
journeys pass, and generated client hashes match. Frontend/control evidence
retains its explicitly unchanged input scope; no duplicate whole-suite run
is required. Build is clean with zero warnings/errors. Final evidence,
commands and the approved publication identity are external under
`logs/planner-review/local-coord-01/review-03/`.

Two additional development setup failures were found in retained correction-2
logs: Application 50/51 (extra evidence reused a unique command key) and API
62/63 (the old combined read-model setup reused project registration identity).
The final fixtures pass; the earlier files remain failed invocations, not
behavioral red controls or reconstructed source snapshots. Codex added those
qualifications and clarified the four scheduling cases in current-work before
approval. The two correction-1 full API failures and all older limits remain.

The whole-solution formatter log was reconciled against the submitted paths:
only the four already accepted unchanged-comment findings intersect the diff;
the other reported files are outside it. No general formatter cleanup is
selected. Local dependency audit, whole unrelated Infrastructure, browser
stop/restart and real-provider qualification remain not run; mandatory
CI/security/release gates are unchanged. Permanent quarantine without supported
release remains an accepted ADR-0060 constraint, including global capacity
consumption by unresolved dispatched mutators. This is local-slice acceptance.

The explicit GO authorizes only staging the approved paths, verifying their
expected tree and sole parent, one commit on main, normal fast-forward push
and live-ref verification in this same executor chat. It authorizes no product
edits, closure-document diff, different slice or provider experiment. A content
or Git mismatch stops publication and returns for review. After the executor
reports publication, Codex must independently verify the live commit/content
and then assess current chat continuity before selecting further work.

## LOCAL-COORD-01 review 2: NO-GO

Historical review-2 decision; review 3 above supplies current dispositions.

Correction 1 was independently reviewed on 2026-10-21. The final ordered
verification-argument comparison, SourceChanged clock composition, common full
undispatched-claim proof and the four type splits resolve LCO-R1, LCO-R3 and
LCO-R4. The malformed escalation fixture correction is justified: a Proposal
is not a linked ExecutionReport. Permanent quarantine and the standing scope
remain unchanged. Complete review and retained probes:
`logs/planner-review/local-coord-01/review-02/review-02.md`.

**LCO-R2 [P2] remains incomplete.** The new LocalExecutionFinalEvidence proves
the actual approval and many relevant relationships, but neither Agent final
proof compares AgentCheckpointFingerprintSha256 with its step's pinned source.
Change only that property through ordinary EF after a real approved review:
the coordinator still enters ReadyForHumanApproval and a completed sequence
still discloses its final. The provider escalation proof also ignores its
AttemptInputMessages: deleting the diagnosis input through ordinary EF leaves
the supposedly proved final escalation disclosed. Three independent regressions
compiled and failed (0 passed); no guard or constraint was disabled. Finish
the exact source and applicable input/pass proof for both Agent final kinds,
before classification and on later disclosure, without changing historical
state, replacing messages or introducing a general graph/replay engine.

**LCO-V1: repair the reproduced existing binding-reader race.** The binding
reader queries binding rows and their events separately outside the final
write transaction. A controlled, successful protected POST can commit the
pair between those reads: the other real handler sees zero binding rows and
one event, falsely returns github_repository_identity_binding.unavailable
and never reaches its locked replay. An independent deterministic probe
compiled and failed with that exact code. The binding handler, reader and
durability test blobs match HEAD; this is a reproduced mechanism in unchanged
code, not a new LOCAL-COORD regression or proof of the exact historical timing.
It is compatible with the concurrent 409 family. Those historical failures
remain separately unexplained at invocation level.

Planner decision: include only this narrow ADR-0043 consistency repair in the
same correction, because it blocks the required API evidence. No new authority
or ADR reversal is needed: coherent replay and atomic pair recording are
already accepted. Obtain the bounded binding/event candidates in one database
statement/snapshot through provider-neutral EF, including orphan/duplicate
detection and every existing byte/actor/payload/identity check. Do not just
reverse the reads, retry until green, serialize or weaken the tests, acquire
a new observation-time lock/transaction, or relax incoherent-evidence refusal.
The observer still runs exactly once when required, outside transactions;
the existing final Run write lock and canonical replay remain unchanged.
Cover the reader's existing GitHub observation/publication consumers as well
as the binding operation. If this cannot be implemented within these bounds,
return a concrete design challenge before changing the contract.

Fresh independent normal checks after retaining and removing only the exact
reviewer-created test files: Application **150/150**, API **115/115**, both
freshly built. The API filter includes the seven durability cases; their
pass does not erase the deterministic failing probe or qualify a whole API
run. Diff whitespace check passes. The correction's complete Application
**7573/7573** log is verified as reported evidence, distinct from the earlier
7503/7504 failure. Its two full API logs remain **3162/3164** (status fixture
plus concurrent binding) and **3163/3164** (different concurrent binding case).
There is no clean complete corrected API result. No full independent rerun,
provider/browser-stop/restart experiment or audit is claimed.

Next action stays in this Claude executor chat: complete LCO-R2 and LCO-V1,
starting with permanent regressions for the retained probes. After focused
proofs, one fresh full Application/API matrix is justified by the common
evidence reader's observation and publication consumers. Retain unchanged
Domain/Persistence/transaction/frontend/generator/controls with explicit input
scope; rerun affected checks and the three required journeys serially on
stable binaries. Update current-work and preserve all failed invocations,
controls and known limitations. Return unstaged, uncommitted and unpushed.
No GO exists; no different slice or publication is authorized.

## LOCAL-COORD-01 review 1: NO-GO (historical; dispositions above supersede)

Independent review on 2026-10-21 reproduced three contract failures through
real file-backed SQLite and production claims/dispatch/query/startup handlers.
None of the probes dropped a guard or disabled a constraint. They exercise
persisted evidence drift, not a public API for tampering or a real provider.
Complete retained source and output: `review-01/PlannerLocalExecutionSequenceReview01Tests.cs`
and `review-01/probes-02.log` under this slice's planner-review directory.

- **LCO-R1 [P2], open:** LocalExecutionDispatchGate's verification comparison
  omits the execution's ordered arguments. After claiming the pinned recipe,
  changing only the execution Arguments before the supervisor's fresh feed
  still lets the real marker dispatch. The ordinary feed/marker snapshot
  comparison merely agrees with that changed execution; it does not bind it
  to the Human-confirmed recipe. Compare the complete executable snapshot,
  including ordinal arguments, against the pinned member at the final gate.
  Add the before-feed drift regression, positive exact-match proof and an
  affected hosted no-process-call proof. Do not weaken ordinary snapshot checks.
- **LCO-R2 [P2], open:** GetLocalExecutionSequence's final-reference proof
  trusts a linked Completed/ReviewApproved enum without reading its approval
  message. A successful cycle still discloses the same supposedly proved
  report/review pair after that ReviewApproval is removed through normal EF.
  Prove the exact actual approval and its report/attempt/role/provenance/input
  relationship before advertising final evidence; validate the applicable
  whole pass and final tuple. Missing or incoherent evidence must fail closed,
  without changing immutable historical outcomes or inventing replacement
  approval. Cover pre-classification and subsequent reads at the cheapest
  boundary, plus representative protected API behavior. Review the matching
  diagnosis/escalation proof for this same identity risk.
- **LCO-R3 [P2], open:** InterruptLocalExecutionSequences' direct refusal path
  proves UUID/link state but not the sequence's owning run. Change the pending
  attempt's RunId to another valid run before startup: closure marks it Failed
  with the new refusal and records a completion event against the other run.
  Refuse to fabricate a terminal result for an unproved association. Require
  complete sequence/run/project/workspace/source/kind/contract identity and
  no dispatch/process evidence before either family's startup refusal; retain
  safe exclusion/unavailability for malformed links. Check the dedicated live
  refusal recorders against the same proof, not just matching UUIDs. Include
  valid undispatched closure, foreign-source/owner and dispatched quarantine
  non-regressions, with no false event or ownership release.
- **LCO-R4 [P3], open:** four new files violate the mandatory one-top-level-type
  rule: ILocalExecutionStepProbe.cs (interface/enum), LocalExecutionCandidate.cs
  (recipe pin/candidate), LocalExecutionVerdict.cs (enum/record) and the API
  LocalExecutionSequenceProcessDouble.cs (two records/class). Split these
  existing types into matching files without behavioral changes or unrelated
  restructuring. Nested private operation details are not this finding.

All three independent regressions compiled and failed for the named assertions
(3 failed, 0 passed). The first sandboxed invocation produced no output and
was interrupted; its empty log remains, with cause unproved. The permitted
second invocation built and ran the probes. After removing only the reviewer
test (identical copy retained), a fresh build and focused Application run passed
**174/174**, including dispatch/ownership/restart/status and the event-type class.
Independent frontend passed **257/257 across nine files**. Complete commands
and qualifications are in `review-01/review-01.md`. No fresh whole-suite, API,
journey, generator, Domain, architecture or Infrastructure claim is made by Codex.

Executor challenges are addressed: widening the refusal-name mappings, the
conservative permanent quarantine, ending rather than queuing a fence-refused
dispatch, early startup ordering, avoiding live output import, fixture changes,
strict Accepted plan forms and refusing before Human-grant/exhaustion branches
are consistent with ADR-0060. They do not excuse the findings. Accepted-root
browser coverage with depth-two coverage at hosted/Application boundaries is
proportional to the unchanged 120-minute budget; do not raise it for a journey.

The original Application 7503/7504 run remains failed evidence, with its
event-list correction and focused verification distinguished. It is not a
clean complete Application run. The four formatter findings concern unchanged
comment text; no unrelated whitespace fix is requested. No dependency changed;
the omitted local dependency audit grants no waiver of mandatory CI/security
or release checks. Preserve the earlier API disposal failure as unexplained,
all journey failures/debug output and the noncompiling first control.

Correction validation: start with the named red regressions and affected
Application/API/hosted classes. Run the original proportional final matrix once
on stable correction sources, with fresh full Application/API because shared
dispatch/startup/evidence consumers change. Infrastructure must include
Persistence and the affected existing transaction classes: ClaimVerificationExecution,
CreateCodeReviewAttempt, CreateReviewCorrectionAttempt, VerificationDiagnosis,
ReconcileInterruptedImplementationAttempts, RecoverInterruptedVerificationOutputArtifacts
and SetCodexAssignmentPreference TransactionBoundaryTests. Retain unchanged
frontend/control evidence with explicit input scope; rerun it if its inputs
change. Run the three required journeys serially after stable host binaries.
Do not automatically replay all unrelated Infrastructure, native or provider
checks. Update current-work, retain findings and return for review in this same
slice/chat. No staging, commit, push or different slice is authorized.

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

- ADR-0050 stays Proposed; P9 unproven; no mechanism selected. Frozen patches
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
  [current-work](current-work.md#open-risks). Production code and necessary
  tests are selected only for LOCAL-COORD-01 under accepted ADR-0060 and the
  owner's new bounded-cycle decision. PLAN-COORD-01, AGENT-SUP-01,
  WORKSPACE-EXCL-01, GLOBAL-ADMIT-01 and GLOBAL-LIMIT-01 are published. No broader
  mutation, queue, lease, retry, recovery, approval, commit or publication
  authority follows. No dependency, experiment, capture-limit or standards edit.
