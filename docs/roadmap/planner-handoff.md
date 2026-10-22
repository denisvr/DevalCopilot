# Planner/reviewer handoff

## Current action: LOCAL-QUEUE-01

Codex selected **LOCAL-QUEUE-01**, resolved in accepted
[ADR-0062](../decisions/0062-queue-explicitly-authorized-local-execution-before-admission.md).
Correction 3 is **reviewed GO (review 4)** for the exact approved content.
Publish in the **same LOCAL-QUEUE-01 Claude executor chat**, then return the
publication report for independent verification. The LOCAL-INSPECT-01 chat and slice are
complete; do not reopen them.
Codex performed architecture, selection and final planning judgment itself;
no subagent or executor research decided them. The publication prompt's exact
content identity and preconditions bound this GO; selection alone grants no commit/push.

### Verified delivery and planner inputs

LOCAL-INSPECT-01 is independently verified published on main as
**c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1**, tree
`895631a0be5cbd5fdb0aa22ca39b283a2dbe4c66`, sole parent
`2912490aed8b3e116fb2b0ef744bf80ad6c62a03`. HEAD, local origin/main and fresh
live refs/heads/main matched; divergence 0/0, real index and working tree clean.
Its exact 100 paths (74 A, 26 M) match the review-2 GO inventory and tree.
Records: `logs/planner-review/local-inspect-01/publication-verification.json`
and `.md`. LCI-R1 and LCI-R2 are resolved. Do not reopen it or make a closure
commit. Historical checkpoint wording records its preparation phase.

Review 1 preflight confirmed main, HEAD and local origin/main at that baseline,
divergence 0/0 and real index empty. The incoming diff has **149 paths: 101
modified and 48 new**, including the preserved planner inputs (this handoff,
docs/decisions/README.md and ADR-0062). Final reviewer edits are documentation
and ignored evidence only. Temporary tests were retained by matching hash and
removed before recompiling permanent tests. No product correction, staging,
commit or push occurred in review.

Codex continuity is **CONTINUE_THIS_CHAT**: observed token_count at
2026-10-22T14:48:49.232Z, last context 119598 / declared window 475000 = 25.18%.
Source: `logs/planner-review/local-inspect-01/continuity-after-publication.json`.
This is not account quota or cumulative session usage. Older percentages are
historical; Claude continuity is separate. No self-addressed prompt is needed.

### Review 4: correction 3 GO, publication pending

All findings are resolved: LCQ-R1 through LCQ-R4, LCQ-V1 and LCQ-V2. The final
SweepCompletion helper establishes loop completion, snapshots the inventory and
observation Tasks and awaits their terminal completion, including scope disposal.
Missing or unexpected bookkeeping fails explicitly. Both stopped sweeps use it;
the scope counter is supplementary. The gated permanent regression and control 9
cover the earlier false zero-count proof. No production shutdown change occurred.

Final independent completion/contention tests rebuild and pass 7/7 (36 s).
Executor final affected supervision/fixture classes pass 250/250 and Architecture
192/192. The complete clean API result 3265/3265 is retained at its actual
pre-completion-follow-up test scope under review 3's explicit proportional matrix
revision; do not present it as a full run of the new completion tests. Application
7905/7905, Persistence/transactions 750/750, Domain 1757, frontend 7399/tooling
and the four serial journeys retain their recorded scopes and limitations.

Verified prepublication: main; HEAD, local origin/main and live refs/heads/main
all c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1; 0/0; real index empty; 167 paths
(107 modified, 60 added), all explained by the selected slice and corrections.
No reviewer temporary test remains. Hygiene checked 245 local link targets in
10 pending documents, no missing targets or conflict markers; diff check passes.
The approved path list and tree identity are external to this tracked content
under `logs/planner-review/local-queue-01/review-04/` and in the GO prompt.

The failed/interrupted attempts remain historical evidence. The old Git burst
and database lock holder remain unidentified; the six-error build had no captured
log. Formatter debt, full Infrastructure/provider/native limits and CI/security/
release gates are unchanged. No content edit or closure commit is part of the
authorized publication. After the normal fast-forward push, independently verify
commit/tree/sole parent/path inventory/live ref/clean state and assess chat
continuity from current observed usage before selecting another slice.

### Review 3: correction 2 dispositions and remaining completion proof (historical)

Git remains main/c1e9392, local origin/main equal, 0/0, index empty. There are
165 pending paths (107 modified, 58 new); the five additions to the pending set
since review 2 are the expected stdin-helper and supervision-test changes.

LCQ-R1 through LCQ-R4 and LCQ-V1 remain resolved. LCQ-V2's stdin helper is
resolved: output drains start first, early refusal remains observable, and the
permanent successful-invocation negative case rejects undelivered stdin.
The full API run is now complete and green, 3265/3265. Independent fixture/
contention classes pass 219/219; after removing the temporary reviewer probe,
the permanent affected tests rebuild and pass 6/6. Preserve the unproven prior
Git burst/database holder and the unlogged six-error build as qualifications.

**LCQ-V2 remains NO-GO only for the sweep-call completion proof.** A gated
reviewer test starts a real sweep, pauses scope acquisition, stops the loop and
observes its still-pending inventory Task. Active is zero and WhenIdleAsync
returns completed while that Task is held. TrackingScopeFactory counts scopes
after inner.CreateScope returns; a call queued before entering CreateScope is
also outside that count. The counter is useful evidence for created scopes,
but zero does not prove every pending call has ended. Moving the increment
earlier covers only acquisition, not the queued-task case.

Use a test-owned completion helper: establish the sweep loop's ExecuteTask has
ended, snapshot its pending inventory and observation Tasks, then await those
Tasks (which include scope disposal) under a separate finite teardown bound.
Test-only reflection of these private Task fields is accepted here to observe
owned work without adding a production API or changing shutdown semantics;
missing/unexpected fields must fail the test, never fall back to count zero.
Adapt the retained gated probe into a permanent regression: draining must stay
pending while the gate is held, then complete after release. Cover both stopped
sweeps before deleting the harness database. Keep the stop stopwatch/contract,
event/Attempt assertions and conservative production behavior unchanged.

The exact probe, failing output and removal hash are under review-02's
correction2 files; review-03/review-03.md records this decision. The probe was
removed and permanent API binaries rebuilt. No production correction occurred.

**Explicit validation revision:** because correction 2 now has a clean full API
run on unchanged production sources, this remaining test-only completion fix
needs permanent red/green gate coverage, the complete affected supervision
classes and Architecture. Retain API 3265/3265 at its actual pre-follow-up test
source scope and the already valid Application, Persistence, frontend and
journey evidence. Do not repeat the entire API matrix for this narrow follow-up
unless shared production/executable harness changes or new failures justify it.
This supersedes review 2's requirement for another full API run after each
test-only correction; CI/security/release gates remain. Return unstaged,
uncommitted and unpushed in the same executor chat; no GO exists.

### Review 2: correction 1 dispositions and LCQ-V2 boundary (historical)

Review 2 confirms main, HEAD and local origin/main at c1e9392, divergence 0/0,
empty index and 160 pending paths (104 modified, 56 new). The additional new
file since the 55-file checkpoint is LocalExecutionStepTicketFactory.cs; it
is expected, not unexplained inventory. ADR-0062 retains its reviewed hash.

**LCQ-R1, LCQ-R2, LCQ-R3, LCQ-R4 and LCQ-V1 are resolved.** The shared
historical identity proof now gates retirement and exact self-exclusion. The
recipe omission is refused, and both reservation paths use the factory that
fills the pinned recipe for Verification and null for Agent steps. Downgrade
refuses held admissions before persistent changes. The advancing feed has a
fixed high-water, RowExtensions is split, and the owned-gate browser journey
proves waiting behind a real owner, zero cycle invocations while waiting,
release-driven progress and reload without step-start requests. Startup's
no-resume control and the accepted mode-visit-key control cover their hazards.

Independent reviewer checks: Application queue 104/104; protected API queue/
startup 17/17; migration 35/35; queue panel 6/6. Source, logs and restoration
records are under `logs/planner-review/local-queue-01/review-01/` (correction1
files); the decision is under `review-02/review-02.md`. The temporary API probe
is retained by hash and removed; permanent API tests were rebuilt afterwards.

**LCQ-V2 (P2), final API validation: NO-GO.** final3-api.log is a complete
failed run (3260 passed, 2 failed), not a clean final matrix. A deterministic
reviewer probe reproduces the stdin helper defect: an ownership refusal occurs
before stdin is read, and a write larger than the pipe buffer raises IOException
in ProviderFixtureContractTests.Run instead of returning the refusal result.
Correct the test helper so expected early refusal remains observable, preserving
exit/output/no-effect assertions and exposing unexpected stdin delivery failure
on successful invocations. Do not make the fixture accept or read unowned input.

The other failure is database deletion during planning-harness teardown in
AgentSupervisionContentionTests. The source permits sweep-owned calls to outlive
bounded StopAsync; the test does not prove all such calls/scopes have settled
before deletion. This is an identified lifecycle gap, not proof of the failed
run's exact lock holder. Diagnose with owned-work/completion evidence, then fix
test lifetime if confirmed. Keep the production stop bound and every durable
event/outcome assertion. Do not hide deletion failures, add blind sleeps or
change production shutdown/ownership semantics. If evidence shows a different
cause, report it before broadening the correction.

This is a bounded test-validation correction of the same slice, not a new
product feature. Preserve the interrupted Git-failure attempt, its unproven
cause, the standalone LocalCommit 787/787 diagnostic and the three isolated
passes of the two failing tests. Passing alone does not resolve the lifecycle
gaps. Add permanent deterministic regressions, run affected classes, Architecture
and one full API suite on final sources. Retain final Application 7905/7905,
Persistence/transaction 750/750, frontend and the four final journeys if their
inputs remain unchanged; repeat journeys only if executable harness changes
invalidate them. No blanket matrix restart, temp cleanup, skip or publication.

### Review 1: NO-GO and correction boundary (historical)

- **LCQ-R1 (P2), reservation identity.** Retirement does not prove full run/
  project/workspace/checkpoint ownership: moving only runs.ProjectId to another
  valid project makes Stop, expiry and startup retire the reservation despite
  the mismatch. Prove coherence and preserve unresolved ownership instead.
  ProveOwnAsync also ignores ticket source/input identity: an otherwise exact
  ticket with a different source gets Idle self-exclusion. Correct ownership,
  retirement and exact self-exclusion together, with permanent regressions.
  No successful forged operation dispatch is claimed; ordinary claim validation
  is a separate defense and does not make this proof complete.
- **LCQ-R2 (P2), unsafe downgrade.** Down drops admissions unconditionally.
  Independent tests show successful downgrade with the table gone for Reserved
  and Unresolved reservations. Reject before dropping guards or durable changes
  while held/untrustworthy authority cannot be represented by the old schema;
  add no release action. Prove unchanged data/guards/history on refusal and keep
  safe waiting/historical reversals.
- **LCQ-R3 (P2), finite advancing sweep.** Waiting observation has a high-water,
  but GetAdvanceableLocalExecutionSequencesQuery/advanceCursor do not. Continuous
  arrivals can keep pages full and prevent revisiting older preparing/claimed/
  stopping records. Carry a fixed high-water through this sweep too and test
  revisits/deadlines with arrivals, not just a finite page of more than 32 rows.
- **LCQ-R4 (P3), one type per file.** Move RowExtensions out of the new
  LocalExecutionQueueMigrationTests.cs into its matching file, preserving
  namespace/behavior. It is the sole multi-type file found among 115 pending
  C# files; separate partial declarations are not violations.
- **LCQ-V1 (P2), selected contention evidence.** The single-cycle journey never
  waits behind an owner. Complete the dispatched held-owner/zero-waiting-
  invocation/release/progression/reload journey, using a production-created
  verification owner in another owned test project at limit one if convenient.
  Keep the fixed provider-root name and isolated invocation; neither prevents
  this proof. Also add a compiling startup control leaving a queued sequence
  advanceable across startup. Omitting retirement while the sequence still
  becomes Interrupted cannot exercise resumption; retain that control as valid
  supplementary evidence.

Independent results: permanent Application queue **71/71**, protected API queue/
startup **17/17**, frontend queue/sequence consumers **291/291**. Six reviewer
probes fail on R1/R2. Their initial compile failure was an incorrect reviewer
import (zero tests); it stays separate. Exact retained probe, failed outputs,
restoration identity and passing fresh recompilation without the probe are under
`logs/planner-review/local-queue-01/review-01/`.

Executor challenges: accept the mode-visit-key mutation as the frontend ownership
control; no replacement needed. Accept separate Playwright invocations and
targeted reruns after test-only list fixes at their actual scope. These waive
neither the held-owner journey nor startup resumption control. Full Application
7871/7872 and Infrastructure 3360 passed / 3 failed / 4 skipped remain failed
original runs, not clean full reruns. Keep invalid CS9113 and build failures;
CS2012 names a possible Defender lock, not a proven cause. Formatter debt and
every earlier qualification remain untouched.

Correct in the same chat. Start with permanent red/green regressions. Shared
proof/gate changes require full Application/API once on final sources; migration
changes require full Persistence plus affected transaction classes, not every
Infrastructure adapter. Run Architecture and changed frontend/harness consumers,
the contention journey and serial coexistence journeys when the shared harness
changes. Retain unaffected evidence at its scope; record exact commands/timings
and original failures. Return unstaged, uncommitted and unpushed. No GO exists.

### Owner authority and coherent outcome

On 2026-10-22 the owner explicitly answered **Authorize that bounded local
queue**: design and select an opt-in queue of existing local cycles, pinning
plan/source/verification/settings/budgets, revalidating after workspace/capacity
waiting within the original two hours, with restart closure and no operation
resume/redispatch. Quarantine release, process cancellation, extra corrections,
Human approval, commit and publication remain excluded. This is design and
implementation-selection authority, not running a product cycle or an experiment.

The user can confirm one cycle now, see why its next step is waiting, stop it
before a claim, and have the original host admit it when the exact authority
and ordinary guards hold. A mere visible queue would leave the reservation-to-
claim race unresolved. Intake, pending descriptors, fair eligible selection,
pre-claim reservation/atomic transfer, lifecycle and both-region disclosure are
therefore one coherent slice. Process-aware recovery, all-manual-operation
queueing and provider/session work require different proofs and stay outside it.

### Resolved implementation boundary

- Preserve Immediate behavior and confirmations. Add explicit queue availability
  and POST routes from ADR-0062 with separate mode-bound confirmation and strict
  wire guards. Queue only the existing ManualAgent accepted-plan cycle; at most
  64 live queued cycles per database. Recorded source is checked physically
  when preparation starts, never observed through a competing workspace writer.
- Persist mode, pending admissions and stable order. Use a feature-owned
  coordination counter and bounded keyset host observation; no unbounded
  historical loads or oldest-32 starvation. FIFO is among eligible queued
  steps; blocked projects do not block independent ones. Each successor joins
  the tail. Waiting consumes no operation, budget or workspace/global slot.
- Before external preparation, atomically reserve the exact workspace and,
  only for a potentially mutating kind, one current global unit. Acquire a
  preparation worker first; at most four queued preparations. Every entrance
  obeys the shared reservation ownership. Exact internal ticket proof permits
  self-exclusion; transfer reservation to operation in the claim's own save.
  No claim/dispatch retry, extra operation step or repeated preparation.
- Stop/deadline/startup may retire only a fully proved unused reservation under
  the write lock with a fenced token. Contradiction/uncertainty stays held.
  Claimed/dispatched ownership and original Exited quarantine proof are unchanged.
  Restart closes all formerly queued work before supervisors; never resumes it.
- Extend the ONE live sequence reader, both-region fences/confirmation and
  current/historical disclosure without presenting waiting/preparation as a
  process. Extend ADR-0061's recorded mode/state only; keep operation/stream
  proofs, truncation admission and window limits. Update the owning system,
  workflow, recovery, evidence, cockpit, context and roadmap documents plus
  current-work as implementation-for-review facts.

Additive Domain/persistence/EF migration and the affected Application claims,
ownership, coordinator/startup, MVC/client and frontend/test changes are selected.
No generic scheduler/queue engine, provider package/flag, new execution mode,
process lease, cancellation, retry, takeover, operation release, automatic
approval/commit/publication, dependency or frozen-experiment change is selected.
Any incompatible proof or migration/compatibility requirement is a challenge
to resolve before dependent work, not permission to weaken this contract.

### Validation and return

Use production-driven file-backed SQLite cycles and independent connections
for queue order, both limits and limit-change orderings, every competing manual
entrance, exact self-exclusion/transfer, rollback/commit uncertainty and malformed
links. Cover all six kinds, complete passes/both correction origins, exhaustion,
stale pins, 64-cycle cap and paging beyond 32 records. Stop/expiry/startup at
each reservation/claim/dispatch seam must preserve conservative ownership and
produce no resumed work or duplicate process. Include old Immediate, manual,
planning, result recording and unrelated-project regressions.

Develop with focused tests first. Once stable: feature Domain tests, full
Architecture, full Infrastructure Persistence and affected transaction-boundary
classes, full Application and full API (shared ownership/all entrances/startup
change), whole Vitest, lint/build and strict e2e TypeScript, client reproducibility,
changed-file format/diff and document/inbound-link checks. Broaden Infrastructure
to adapters/Git/process only if their inputs change or evidence raises a concern.
No real provider/native campaign or automatic whole Infrastructure/Domain run.

One authenticated deterministic real-host queue journey must wait behind a held
owner, show zero waiting invocations and progress after its owned test gate is
released, without browser per-step starts; reload its recorded state. Existing
local-execution, planning and collaboration journeys run serially for coexistence.
Use hosted/Application lifecycle permutations rather than adding production
test hooks. Four serial compiling controls from ADR-0062 kill named regressions;
preserve invalid attempts, restore hashes, touch and rebuild. Keep commands,
timings and original failures in concise logs, with fresh/retained/not-run scope.

Preserve all historical failures/qualifications and the standing restrictions
below. Return the whole slice unstaged, uncommitted and unpushed for Codex review.
The publication GO, if earned, will identify its exact final content separately.


## Historical LOCAL-COORD-01 planning and review record

Everything below records the preceding slice at its preparation/review phases.
The verified publication and current next-slice selection above govern present
workflow state; historical preflights and prompts do not dispatch another slice.

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
  [current-work](current-work.md#open-risks). Production code and necessary tests
  are now selected only for LOCAL-QUEUE-01 under ADR-0062 and the owner's
  bounded-queue decision above. PLAN-COORD-01, AGENT-SUP-01, WORKSPACE-EXCL-01,
  GLOBAL-ADMIT-01, GLOBAL-LIMIT-01, LOCAL-COORD-01 and LOCAL-INSPECT-01 are
  published. The queue grant adds no broader mutation, executor lease, operation
  retry/resume, quarantine release, recovery, approval, commit or publication
  authority. No dependency, experiment, capture-limit or standards edit.
