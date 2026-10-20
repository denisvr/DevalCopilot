# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and the [workflow](development-workflow.md) apply.
Codex remains planner/reviewer and performed publication verification,
critical-path assessment, selection and architectural judgment itself.
GLOBAL-ADMIT-01 is published and independently verified. GLOBAL-LIMIT-01
correction 1 is reviewed GO below; GLL-R1 and GLL-R2 are resolved. Publication
belongs to its same Claude executor chat, limited to the exact external
review-2 tree, parent and approved paths. Codex implemented no product
correction; independent publication verification is still pending.

## Verified publication and executor preflight

Independent verification on 2026-10-20 confirms branch main and HEAD, local
origin/main and freshly queried live refs/heads/main all equal
**`1747fb2b1d5a4a4a3050fc6b2e2a583473d8a934`**. Its approved tree is
`e8e8e541a52a56e3951e7c30d836972a358058c1`, sole parent
`b3d53bfc3c604711228ade769284911f9d9c2b4b`, with exactly **93 paths, 51
modified and 42 added**. Inventory SHA-256:
`feba202d39c81d767c0b8b3c5e1607aa01f0cd45958f1252bc299778f69ab00b`.
Divergence 0/0; index empty; worktree clean including nonignored untracked
paths. Exact comparisons are in
`logs/planner-review/global-admission-01/publication-verification.json` and
`.md`. Executor success was independently checked against the GO identity.

GAD-R1 and GAD-R2 are resolved: the dispatch handler reconciles its current
stored and tracked role/contract before role branches and capacity exemption;
the test support types have matching files. Review 2 independently passed
Application 324/324, API 74/74 and architecture 185/185. The first architecture
attempt aborted on testhost connection timeout; the retry does not establish
its cause. Earlier full Application 6957 and API 2978 remain pre-correction
product-source results; affected correction consumers and both journeys ran
again. Keep every phase, warning, skip, failure and qualified control in the
review records and current-work; no broad final full-rerun claim is added.

The published checkpoints retain their preparation/review wording. This
neither reopens GLOBAL-ADMIT-01 nor requires a closure commit. Its detailed
review/selection/recovery record is this file at `1747fb2` and the ignored
per-slice logs. The account-credit replacement executor completed that slice;
its recovery exception is not an instruction to reuse it for a different one.

Prior independently verified baselines remain WORKSPACE-EXCL-01 `b3d53bf`
(110 paths, 64 M/46 A), AGENT-SUP-01 `f95e337` (141, 43 M/98 A) and
PLAN-COORD-01 `a78e572c` (176, 66 M/110 A). WEX-R1 through R4, ASUP-R1
through R5 and PCO-R1 through R5 are resolved. WEX-C1, ASUP-C1 and PCO-C1
remain accepted nonblocking. Preserve WEX's possible initial stale-mutant
API provenance and overwritten correction failure log, earlier unexplained
full-suite failures, advisory/audit qualifications and all missing originals.
Later passes reconstruct or explain none of them.

Continuity: **CONTINUE_THIS_CHAT**. After publication verification, local
current-chat token_count metadata at 2026-10-20T16:44:34.854Z reports last
80787 / model window 475000 = **17.01%**. Source:
`logs/planner-review/global-admission-01/context-after-publication.json`.
This is observed metadata, not an asserted UI meter; earlier readings are
historical. No measured threshold above 60% or concrete transition reason
exists. Claude continuity is separate: this slice and its corrections remain in its
current executor chat; no older executor runs in parallel.

A fresh selection preflight again found the same branch/HEAD/local/live main,
0/0, empty real index and clean worktree. Selection changes only this file,
`docs/decisions/README.md`, ADR-0058's partial-supersession status and the new
ADR-0059. Those four planner paths are
expected pending input to the executor and must be preserved. No source,
test, dependency, commit or push was changed by Codex during selection.
That four-path preflight is historical; the 70-path review inventory below
now prevails. Current-work is this slice's implementation-for-review record.

## Remaining MVP critical path

Increments 4/5/6 remain incomplete; ADR-0050 Proposed, P9 unproven, AI Memory
deferred. No completion percentage, provider qualification or date is claimed.

| Front | Actual delivery and remaining boundary |
| --- | --- |
| Planning | ADR-0055 coordinates at most five existing ReadOnly attempts and ends at Accepted planning or Human escalation. It grants no mutating successor. |
| Supervision | ADR-0056 records host observations for seven Agent contracts, not liveness, an executor lease, cancellation, retry, takeover or release. |
| Exclusion/admission | ADR-0057 enforces reciprocal workspace ownership; published ADR-0058 adds a global fixed-two recorded-mutator ceiling across Agent, verification and commit. |
| Capacity configuration | The ADR-0059 delivery implements durable one/two choice, fresh shared checks and UI control. Correction 1 is reviewed GO with GLL-R1/R2 resolved; publication remains pending independent verification. |
| Queue/recovery | Pinned pending intent, expiry, revalidation, replay, fairness, finite execution leases and process-aware recovery remain separate contracts. Observation expiry or terminal rows prove no process death. |
| Local coordination | Explicit implementation, verification, review, diagnosis, correction and commit exist. Automatic workspace mutation still needs a new owner authority decision, never inference from planning or settings. |
| Providers/remote | Claude account usage, resume, live context, manual compaction, permission profiles, raw Git/non-Windows proof and separate Git/GitHub/GCM qualification remain open. No real-provider experiment is authorized; access/consent is not push permission. |

## Selected slice: GLOBAL-LIMIT-01 configurable recorded admission

Implement accepted
[ADR-0059](../decisions/0059-configure-the-recorded-mutating-admission-limit.md).
The operator can explicitly select one or two globally, see the persisted
choice after restart, and have every existing mutating entrance obey it.
Default two and every ADR-0058 independent authority/safety gate remain.

**Sizing judgment.** A setting without all final gates leaves direct/racing
entrances; a backend-only control lacks the operator outcome; a nonpersistent
choice changes after restart; blindly lowering with two admitted owners can
strand both undispatched owners. Include durable revisions, atomic safe
updates, fresh policy/fence/feed/query reads, closed wire and one host-owned
control with both regions' correct guidance as one outcome. Queueing and
full local coordination are larger authority/lifecycle changes, not implicit
extensions. Other provider contracts need their own evidence and no experiment
is renewed. This is a common Increment 5 prerequisite under ADR-0006, not a
claim to complete the scheduler or the local delivery loop.

Static selection evidence and exact source references are retained in
`logs/planner-review/global-limit-01/selection-evidence.md`. No product probe,
provider, process, Git workspace mutation or implementation was performed.

## Resolved architecture and scope

ADR-0059 owns the complete contract; these are its dispatch boundaries:

- One Domain-owned immutable configuration-revision type and additive table,
  positive Int64 revision and limit one/two, migration seed revision one/two,
  constraints, upgrade/reopen/down. Latest revision is current. No runtime
  seeding, cache, older-row fallback or second settings framework. Missing or
  malformed current configuration fails closed. Preserve historical rows.
- One explicit command under the existing SQLite-wide write lock. Unavailable
  configuration/owners refuse unchanged. Same value is a no-op. A different
  limit below recorded ownership refuses fixed
  execution_capacity.limit_below_recorded_owners Conflict; otherwise append
  exactly one revision atomically. No stranded pending reduction, chosen
  winner, termination, cancellation or release.
- Read the current persisted limit fresh at every early and final mutating
  claim, exact-owner dispatch/execution and advisory feed. Preserve GAD-R1,
  all mutating entrances, ReadOnly independence and result recording. A race
  after preparation re-decides under the write boundary and consumes nothing
  when refused. Changing the limit starts no work.
- GET retains exactly limit/recordedOwners/status/reasonCode. Valid limit is
  one/two, null only when configuration is unavailable. Available count is
  below that limit; AtCapacity is at/above it; Unavailable has null count.
  Add protected, queryless PUT /api/execution-capacity/limit, exactly one JSON
  integer limit field, 512-byte body bound, closed validation, fixed errors
  and the same response shape. No extra metadata/history endpoint or secret.
- One App-owned host-session control visible without a project, with explicit
  Apply/Refresh and fixed copy; both regions share its capacity state.
  Preserve polling/real abort/coalescing/failure limits. One PUT at a time,
  actual ten-second abort, no auto retry. Mask old Available from submission
  until a new post-write GET settles, including stale reads/lifetimes and
  ambiguous timeout. No optimistic success, rollback or automatic resubmit.

Touch feature-owned Domain/Application/API/Infrastructure/frontend code,
necessary focused tests, generated client/migration and directly affected
architecture/product/engineering/roadmap contracts only. No generic pipeline,
transaction/supervisor rewrite, broad fixture caching, unrelated type split,
new package or standards change. Challenge a material flaw before dependent
work; discussion is not additional authority.

## Proportional validation and acceptance

Plan exact filters before implementation; use regressions first and build
incrementally. No simultaneous writers to shared build/generator/mutation
outputs. Preserve complete failed logs and actual source phases.

- New Domain configuration invariants; real SQLite policy/command/history
  cases, missing/invalid current rows, tracked-state freshness, no-op/refusal
  with no side effects. Independent connections prove both configuration/
  claim orderings, limit-one admission at every mutating entrance, exact own
  dispatch for all families and unchanged ReadOnly/known-result paths.
- Complete new/existing capacity MVC protection, closed input/output and
  error matrix, representative production mediator/persistence/race and
  hosted compositions. Cheap invalid requests need no full Git lineage.
- Full Application/API once on stable final product/test sources because the
  shared capacity read/fence changes every consumer. All persistence/migration
  classes; no unrelated full Infrastructure process/Git suite by default.
  Focused Domain tests, full architecture. Broaden for actual changed inputs,
  failures or uncertain shared impact; mandatory CI/release gates stand.
- Whole Vitest, lint/build (includes product typecheck), strict e2e tsc and
  reproducible generated client. Prove decoder tuples, both regions and one
  shared lifetime, real PUT cancellation, stale pre-change responses,
  ambiguous success/timeouts, project-independent ownership and remounts.
- Collaboration and planning-sequence journeys serially after stable binaries;
  extend collaboration with explicit one/two change and shared disclosure.
  No other nine-journey/native/provider campaign by default.
- Three compiling controls at named regressions: fixed-two instead of the
  persisted final comparison; omitted below-owner update refusal; stale
  pre-change Available admitted across the UI confirmation barrier. Retain
  failures, verify restored hashes, touch restored sources, rebuild and run
  final checks. Do not claim source hashes prove historical binaries. Earlier
  controls remain qualified at their phases unless inputs need another run.

Update current-work with outcome, source-phased fresh/retained/not-run checks,
failures, challenges and Git state. It remains an implementation-for-review
record, not a self-issued GO. Planner documents are preserved in the delivered
slice. No exact new test count is prescribed or invented before execution.

## Stop boundaries and next action

Publish only GLOBAL-LIMIT-01's exact approved content in its same Claude
executor chat using the complete GO prompt. First reconcile branch, HEAD/
local/live main, divergence, empty real index and the exact 70-path inventory.
Stage only that set, compare the staged tree, then the committed tree, sole
parent and path set before a normal fast-forward push of main. A mismatch,
failed push, remote divergence or material content change stops publication;
no amend, force-push, silent recomputation, broader staging or reconciliation.
No implementation, documentation or evidence edit belongs to publication.

The implementation/correction-for-review wording in current-work records its
preparation phase. Preserve it; no closure edit or closure commit is needed.
No next slice is selected. After the executor report, Codex independently
checks the published identity, fresh live ref and clean state, then current
context usage and the matching continuity/next-step prompt.

This GO grants no new execution authority, automatic local mutation, queue/
fairness, limit above two, per-provider/project settings, generic configuration
or event infrastructure, executor lease, cancel/retry/resume/takeover/release,
process-death inference, dependency, provider/native/security experiment,
permission/credential change or remote-publication mechanism. Standing limits
below remain; discussion grants none of those capabilities.

## GLOBAL-LIMIT-01 review 2: GO

Independent reconciliation confirms main, HEAD/local origin/main and freshly
queried live main `1747fb2b1d5a4a4a3050fc6b2e2a583473d8a934`, divergence
0/0 and real index empty. The exact set is unchanged: **70 distinct paths,
45 modified and 25 added** (64 grouped status entries). Inventory, complete
review, checks and exact publication identity:
`logs/planner-review/global-limit-01/review-02/reviewed-paths.txt`,
`review-02.md` and `approval.json`. The external tree is computed after all
planner edits; no tracked file embeds its own future tree/commit identity.

- **GLL-R1 [P2], resolved.** GET starts now defer while PUT is applying as
  well as while a GET is busy. Poll/Refresh/hints coalesce into one trigger;
  pre-write answers neither restore the reading nor rearm polling. PUT
  completion consumes the trigger immediately or behind an existing GET,
  with exactly one new confirming read. confirmAfter still rejects old
  responses. No second reader or durable queue was added. Red-before-fix
  evidence has six failures/23 passes: five newly failing cases plus the old
  timeout test now pinning exactly two reads. The expanded change set is 29.
- **GLL-R2 [P3], resolved.** current-work separates the original 21 failed/
  55 passed into 18 behavioral failures and three invalid rowid-insertion
  setup failures. The original log remains intact; impossible key cases are
  not counted as valid red/control sensitivity and malformed-limit cases stay.

Independent corrected-source frontend: **958/958 across ten files (10.37 s)**,
lint/build including product typecheck exit 0, strict e2e checking of all 46
roots plus both Playwright configs exit 0. The first reviewer e2e invocation
used a missing config and the second required the compiler's ignoreConfig
flag; both invocation failures are retained, and the corrected explicit-source
check passed without changing any configuration/source. Existing lint, chunk
and journey warnings retain their limits. No new probe or product correction
was introduced by Codex in this round.

Inspected correction evidence: executor capacity set 958, compiling control
three killed 1/29, restored hashes matched/touched, both journeys passed
serially. The volunteered 1602-hook development run has no separate retained
log and is not independently authenticated. Backend inputs are unchanged;
review-1 independent Application 328, API 142, Domain 17, migration classes 36
and architecture 185 retain their phases. Full Application 7061/API 3050,
persistence 468 with its first 466/468 failure, generator and format stay
qualified; whole Vitest 6835 is pre-correction, not a new full frontend run.

Review-1 findings, reproduction, accepted test adjustments and executor
concerns remain in `logs/planner-review/global-limit-01/review-01/review-01.md`.
The shared early/final comparison control has bounded sensitivity, unknown
current limit during masking is truthful, and PUT-coherence/new-GET semantics
state current observations without proving operation causality. Preserve the
first invalid control-three compile attempt, all failed logs, warning/skip
scopes, restored-source versus binary limits and earlier lost/overwritten
originals. Later passes reconstruct or explain none of them.

This GO accepts only ADR-0059's explicit durable one/two admission setting and
its current gates/disclosure. Increments 4/5/6 remain incomplete, ADR-0050
Proposed, P9 unproven, AI Memory deferred; PCO-C1/ASUP-C1/WEX-C1 remain
accepted nonblocking. Publication remains pending independent Git verification.

## Retained review and evidence limits

Active execution-evidence limits:
[current-work](current-work.md#active-evidence-limits). Detail as written:
[WF-04-era limits](planner-handoff-history-e614c79.md#retained-review-and-evidence-limits),
[earlier planner limits](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits),
[execution limits](current-work-history-0a57216.md#prior-published-delivery),
this file at `e55709f`, and per-slice records under ignored
`logs/planner-review/<slice>/` and `logs/executor/<slice>/`. Progressive
reading and condensation waive none of them:
- WF-02A evidence loss remains: derivative, partly abridged chat transcription;
  original failed runs/checker/timings unavailable; run B exit unknown. No
  reconstruction or historical authentication of missing originals.
- Failed, derivative, missing, overwritten and chat-only evidence and all
  earlier stable findings/dispositions stay retained; later passes erase or
  explain none of them. Accepted dispositions (EX-N01, EX-N02, EX-04-02,
  EX-05-01, the RV-04-01 measurement limit, earlier legacy-route
  observations) and retained failures (RV-N01 child streams, planner N
  verify-01/02) stand as recorded.
- Scans/coordinates are lexical; Git/disk/live and independent checks have no
  negative controls. Retained controls and publication phases were inspected,
  not rerun or historically authenticated. Moved counts, ordinals, deferred controls,
  null-model observations and "real today" wording stay unreconciled.
- Records citing the formerly missing
  `planner-handoff.md#verified-transition-baseline` fragment stay historical;
  EX-05-01's CW-A repair is accepted. Archiving does not resolve any
  open issue.

## Historical review routes

Exact earlier review records: WF-05 in this file at `e55709f`;
[WF-04](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go);
WF-03I to WF-03N through the
[route list](planner-handoff-history-e614c79.md#historical-review-routes).
Their old GO/pending statements are not current instructions.

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
  tests are selected only for GLOBAL-LIMIT-01 under ADR-0059; PLAN-COORD-01,
  AGENT-SUP-01, WORKSPACE-EXCL-01 and GLOBAL-ADMIT-01 are published. The one
  configuration table and one/two control narrow the existing admission policy;
  they grant no broader mutation or automatic coordination authority. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
