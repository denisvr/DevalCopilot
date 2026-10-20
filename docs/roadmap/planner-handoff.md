# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and the [workflow](development-workflow.md) apply.
Codex remains planner/reviewer and performed selection, architecture and
judgment itself. WORKSPACE-EXCL-01 is published and independently verified.
GLOBAL-ADMIT-01 correction 1 is reviewed GO below. GAD-R1 and GAD-R2 are
resolved. Publication belongs to its current replacement Claude executor chat,
limited to the exact approved tree, parent and paths in the external review-2
approval. The account-credit recovery remains historical. Codex implemented
no product correction; independent publication verification is still pending.

## Verified publication and executor preflight

On 2026-10-20, main, HEAD, local origin/main and a freshly queried live
refs/heads/main all equal **`b3d53bfc3c604711228ade769284911f9d9c2b4b`**.
WORKSPACE-EXCL-01's exact tree is `b11cb488519202f681849f32b8fcd255471643cd`,
sole parent `f95e337484d6d50f502647b4abf725be2d7ae184`. Exact GO path set:
110 files, 64 modified and 46 added. Independent tree, parent, path-set,
inventory-hash, fresh-live-ref and clean-state comparisons are retained in
`logs/planner-review/workspace-exclusion-01/publication-verification.json`
and `.md`. Divergence was 0/0, index empty and worktree clean including all
nonignored untracked paths. Executor success was independently checked.

The published checkpoints retain their implementation/review phase wording.
This neither reopens WORKSPACE-EXCL-01 nor requires a closure commit. WEX-R1
through R4 are resolved. WEX-C1 is accepted nonblocking: obsolete prop comments
still describe the removed test exception; actual required inputs and runtime
gates withhold. Preserve its initial API binary-provenance uncertainty, lost
overwritten correction failure log, qualified controls, accepted direct planted
diagnosis evidence, error precedence, validation adjustments, initial failures,
format findings and all prior limits. Review details: this file at `b3d53bf`,
`logs/planner-review/workspace-exclusion-01/review-01/` and `review-02/`.

Prior independently verified deliveries remain AGENT-SUP-01 `f95e337` (141
paths, 43 M/98 A; ASUP-R1 through R5 resolved, ASUP-C1 nonblocking) and
PLAN-COORD-01 `a78e572c` (176 paths, 66 M/110 A; PCO-R1 through R5 resolved,
PCO-C1 nonblocking). Their publication records under the corresponding
`logs/planner-review/` directories and committed checkpoints preserve the
exact identities, unexplained failures, missing originals and restrictions.
Later passes recover or explain none of them.

Continuity: **CONTINUE_THIS_CHAT**. Current-chat local token_count metadata at
2026-10-20T12:36:56.789Z reports last total 127671 / context 475000 = 26.88%.
This is observed metadata, not an asserted UI meter. Earlier percentages are
historical. No threshold above 60% or concrete reason for a transition exists.
The completed WORKSPACE-EXCL-01 executor chat is not reused for another slice.

Selection changed only this handoff, `docs/decisions/README.md` and new
ADR-0058. Original selection-time preflight (superseded for recovery below): same branch/HEAD/local/live baseline, divergence
0/0, real index empty, those two tracked documentation changes and one
untracked ADR, with no other nonignored paths. Preserve these planner paths
for delivery with the implementation. Codex created no commit or push.

## Remaining MVP critical path

Increments 4/5/6 remain incomplete; ADR-0050 Proposed, P9 unproven, AI Memory
deferred. No completion percentage, provider qualification or date is claimed.

| Front | Verified code and remaining boundary |
| --- | --- |
| Planning | ADR-0055 coordinates at most five existing ReadOnly attempts. Accepted planning or Human escalation ends that grant; no mutating successor is authorized. |
| Supervision | ADR-0056 records current host phase, heartbeat and deadlines for seven Agent contracts. It proves neither provider progress nor liveness and grants no lease, retry, cancellation, takeover or release. |
| Workspace exclusion | ADR-0057 now serializes Agent/verification/local-commit ownership per workspace at claim and dispatch. Distinct workspaces have no global count. |
| Global admission | Domain owns ReadOnly/WorkspaceMutating classification; explicit mutating Agent claims, verification and local commit have independent entrances. Three production verification claims persist across three workspaces. Selected work closes their common recorded-admission gap. |
| Queue and recovery | A durable pending request needs exact pinned intent, expiry, revalidation, replay and recovery contracts. Configurable capacity, queue/fairness, finite execution leases and process-aware recovery remain undelivered. A terminal row or expired observation is no new proof of process death. |
| Local coordination | Explicit implementation/verification/review/diagnosis/correction and local commit exist, with no complete stage coordinator. Automatic workspace mutation needs a new owner authority decision, never inference from ADR-0055/0056. |
| Providers and remote | Claude account usage, resume, live context, manual compaction, permission profiles, raw Git/non-Windows proof and separate Git/GitHub/GCM qualification remain open. No real-provider experiment is authorized. Access/consent is not push permission; P9 and ADR-0050 remain unresolved. |

## Selected slice: GLOBAL-ADMIT-01 global mutating admission

Implement accepted
[ADR-0058](../decisions/0058-bound-global-admission-of-existing-workspace-mutators.md).
The observable result is that at most two coherent potentially mutating owners
can be newly admitted across one authoritative database, and both cockpit
regions explain a capacity refusal while eligible ReadOnly work can proceed
under its unchanged gates. Capacity is an additional veto, never a grant.

**Sizing judgment.** A gate for only Agent, verification or local commit leaves
another writer entrance; a claim-only check leaves stale dispatch; a UI-only
hint leaves direct API races. Include all mutating entrances, their feeds and
final dispatch/execution seams, exact own-owner exclusion and the protected
shared UI disclosure as one outcome. ReadOnly classifications are intentional
non-consumers, not omitted providers. A durable queue would retain new authority
for later preparation and needs pinned-request and recovery decisions; a full
lease/guardian or automatic local delivery needs separate contracts/authority.
The fixed ceiling is a prerequisite, not the complete scheduler or configurable
limit of ADR-0006. These are concrete authority/lifecycle boundaries.

**Independent baseline evidence.** Production verification claims admit three
distinct registered projects' Ready leased workspaces on real file-backed
SQLite. The expected third-refusal assertion fails (2 s). No provider,
verification process, Git command or hosted supervisor runs. Domain factories
seed the scene; the three actual claims use their production transaction and
counter/execution save. Evidence/source/hash:
`logs/planner-review/global-admission-01/selection-evidence.md`,
`baseline-probe.cs`, `baseline-probe.log`. The source was hash-verified, removed
and the project rebuilt without it (zero warnings/errors, 10.35 s); no reviewer
probe remains in source or binaries. This proves recorded admission only.

**Resolved contract.** One fresh Application policy under
Runs/Policies/ExecutionCapacity counts existing coherent unresolved mutating
Agent owners (Domain effect, including both correction origins), verification
owners and local-commit reservations. Fixed limit two; recognized terminal
history does not count. ReadOnly Agent/simulation/standalone Process/approval
waits/idle prepared workspaces do not consume it. Unknown active contract-role,
unreadable or malformed ownership is Unavailable. Unknown possibly active
status is never finished. Do not drop an unresolved owner because its run or
lease ended, or conflate several owners on one workspace into one slot.
No new table, migration, configuration, queue or counter.

All mutating claims check before external preparation and under the existing
SQLite write lock immediately before commit; dispatch and local-commit
execution re-decide with exact coherent kind-and-ID self-exclusion. One owner
may execute beside one other; two others withhold dispatch. Preserve losing
cleanup/cancellation, grants, counters, memberships and ambiguous-save readback;
known result recording and startup recovery keep their existing authority.
Use fixed 409 codes `execution_capacity.limit_reached` and
`execution_capacity.unavailable`. No winner, terminalization or release.

Protected bodyless/queryless `GET /api/execution-capacity` returns only
limit/recordedOwners/status/reasonCode; exact tuples and timing are in ADR-0058.
App owns one host-session lifetime, bounded polling/actual abort/coalescing and
stale-remount fencing, feeding both regions as required additional state.
Apply the hint only to mutating starts: split diagnosis from its correction
and metadata-only authorization from execution. Preserve all other gates,
ReadOnly requests, approvals, Refresh, inspection, Stop and abandonment.

**Allowed changes.** New capacity policy/query/MVC-owned graph/generated
client/decoder/hook/guidance; ordinary/authorized Implementation,
ReviewCorrection, DiagnosisCorrection, verification, relevant dispatch/feed and
local-commit admission/execution readers; narrow UI composition and feature-owned
regressions; factual engineering/architecture/product/roadmap/current-work
updates. Domain's effect contract remains authoritative and is not expanded.
Do not merge role supervisors, genericize request pipelines, broaden controls
or clean unrelated WEX-C1/ASUP-C1 warnings.

## Proportional validation and acceptance

Add permanent named regressions first, then implement the narrow shared gate.
Retain commands, source phases, full failures, outcomes and elapsed times under
`logs/executor/global-admit-01/`. Enumerate actual classes/methods: partial-class
filenames do not select tests by FullyQualifiedName.

- Application: policy permutations, every affected claim/authorization/
  correction/dispatch/feed/local-commit class and unchanged ReadOnly consumers.
  Real SQLite with independent connections must prove simultaneous final-seam
  races from one free place across Agent/verification/commit combinations leave
  no third owner; losing budgets/numbers/grants/artifacts/membership unchanged;
  exact self-exclusion across tables; two admitted owners may both dispatch;
  cancelled/failed new capacity reads at each final seam clean up only the
  operation's unreferenced preparation and preserve authorized durable settlement;
  old over-capacity facts cannot dispatch another owner and remain unchanged;
  malformed/unknown relationships and stale tracking fail closed; known results
  persist at capacity and genuine terminal transitions permit a fresh request
  only under unchanged workspace/source gates. Prove ReadOnly progress at full
  capacity and no provider/observation-based classification. Run full
  Application once on stable sources because shared admission changes.
- API: complete auth/body/query/wire matrix and MVC authorization inventory for
  the new GET; real held mutating Agent, verification and local-commit host
  compositions with invocation counts; direct command/race admissions; unchanged
  ReadOnly planning/diagnosis behavior and complete local-commit approval gates.
  Run full API once on stable sources. Use existing production lineage when
  proving claim/commit authority; no new fixture cache or weakened assertions.
- Domain/Infrastructure: retain unchanged table/migration/provider evidence;
  focused Domain effect-classification tests and real SQLite policy/transaction
  tests above. Full architecture. Broaden only for actual shared mapping,
  mediator/transaction/lifecycle or provider-input changes. New schema, slot
  storage or process-liveness mechanisms are scope stops, not automatic work.
- Frontend: whole-wire tuples, host-owned hook and actual transport abort,
  failure/remount/stale callbacks, required absent-state withholding, both
  regions and exact action distinction (ReadOnly/metadata versus mutation).
  Whole Vitest once, lint, production build including tsc -b, strict e2e tsc.
  Regenerate NSwag after successful API build and prove byte-identical
  reproduction; no routine client deletion or duplicate typecheck.
- Browser: collaboration and planning-sequence journeys serially on stable
  binaries; extend the former to assert the host's global disclosure and recovery
  without changing provider counts or explicit authorizations. Controlled hosted
  and UI tests prove full-capacity behavior; do not stretch shared fixture timing
  to make a transient phase appear in a browser. Other nine journeys retained
  unless a changed shared input invalidates them.
- Two serial compiling negative controls omit the Agent final capacity gate and
  the verification final capacity gate. Named independent-connection races must
  fail. Hash-check and touch restored sources, then rebuild before final checks;
  do not infer binary restoration from source hashes. No broad campaign.
- Changed formatting/analyzers, build, applicable security/dependency checks
  without dependency changes, Markdown/inbound links and sensitive-data scan,
  complete diff. One writer to shared outputs; preserve all failed logs rather
  than overwriting. Routine incremental builds; forced rebuild only for a
  demonstrated stale-binary concern. Mandatory CI/release gates remain intact.

## Stop boundaries and next action

Publish only GLOBAL-ADMIT-01's reviewed content in the same replacement Claude
executor chat using the complete GO prompt and the external approval identity.
Reconcile branch, HEAD/local/live main, divergence, real index and the exact
93-path set first. Stage only that set; verify the staged tree, then the
commit's tree, sole parent and paths before a normal fast-forward push. A
mismatch, failed push, remote divergence or material content change stops
publication; no force-push, amend, broader staging or history reconciliation.
No implementation, documentation or evidence edit belongs to publication.

The implementation-for-review wording in current-work is retained at its
actual preparation phase. It requires no closure commit. No next slice is
selected or dispatched. After the executor's report, Codex independently
verifies the published content and fresh live ref, then checks current chat
context and provides the matching continuity/next-step prompt.

Queue, execution grant, schema/configuration, generic transaction pipeline,
lease, process-death inference, automatic retry/successor/release, provider
experiment, permission/credential operation or broader mutation remains
outside this slice. Discussion and this publication GO grant none of them.

## GLOBAL-ADMIT-01 review 2: GO

Independent reconciliation on 2026-10-20 confirms main, HEAD/local origin/main
and fresh live main `b3d53bfc3c604711228ade769284911f9d9c2b4b`, divergence
0/0, real index empty. The corrected slice has **93 paths: 51 modified and
42 new**. Relative to review 1, only the stale-classification test and the two
split type files add paths. The explicit inventory, independent evidence,
review and exact publication identity are retained under
`logs/planner-review/global-admission-01/review-02/`: `reviewed-paths.txt`,
`review-02.md` and `approval.json`. The external identity is computed after
these planner edits; no file embeds its own future tree/commit identity.

- **GAD-R1 [P2], resolved.** MarkAgentAttemptDispatched compares the freshly
  persisted role/response-contract pair with the tracked pair before guidance,
  role branches and the capacity exemption. A mismatch returns existing
  agent_attempts.invalid_agent_contract Conflict at any occupancy; the guard
  only reads the tracked entity. It neither refreshes nor overwrites pending
  tracking. The dispatch transaction retains the write boundary. Six new
  real-SQLite regressions cover stale ReadOnly/current mutating at full and
  free capacity, reciprocal drift, role-only drift, valid ReadOnly at capacity
  and valid mutating self-exclusion/capacity behavior. Refusals pin the fixed
  code and preserve the stored tuple, marker, outcome and event count. The
  first red attempt did not compile; the corrected red run has four failures
  and two passes, then six pass after the fix. Both red logs stay retained.
- **GAD-R2 [P3], resolved.** OwnerFamily and PlacedOwner now occupy matching
  feature-owned files; ExecutionCapacitySeed holds its one top-level type.
  Namespace, visibility and per-test database isolation remain unchanged.
  The lexical scan finds no multiple top-level types among 31 new C# files.

Independent correction-source checks: affected Application dispatch classes
**324/324 (45 s)**, protected API, real local commit, verification supervisor,
mutating/ReadOnly hosted cases and all seven Agent supervision compositions
**74/74 (58 s)**, architecture **185/185 (2 s)**. The first architecture
attempt aborted after a 90-second testhost connection timeout, without test
results; its log is retained. The retry outside command isolation passed;
that pass does not establish the timeout's cause. No reviewer product edits
or temporary probes were introduced in this round.

Executor correction evidence was inspected: affected Application 868/868,
API 339/339, solution build zero warnings/errors, changed-C# format clean
(55 files), both journeys passing serially. Earlier full Application 6957 and
API 2978 remain pre-correction product-source results, not fresh full reruns.
Unchanged frontend (whole Vitest 6743; review-1 independent focused 859),
Domain, Infrastructure (3155 passed/four skipped), generator and production
audits retain their actual phases. The byte-identical generated-client hash
and the original claim-control targets remain unchanged. Claim controls do
not cover every entrance or the dispatch exemption; the red-before-fix
regressions supply bounded sensitivity for GAD-R1, not a new mutation control.

Review-1 findings, reproduction and accepted test adjustments remain in
`logs/planner-review/global-admission-01/review-01/review-01.md`. The two
neighboring-owner fixtures had conflicting Agent and verification owners on
one workspace; the mode/guidance repairs split their recorded-state rounds
while retaining assertions. Direct terminal updates prove recorded facts,
not process termination. Keep all initial failures, compile/format/build and
fixture failures, warnings, four skips, unrelated format findings, earlier
binary-provenance uncertainties and missing/overwritten evidence. Production
npm audit does not resolve the retained development-only advisory. Later
passes authenticate or explain none of those historical gaps.

This GO accepts only ADR-0058's fixed two-recorded-owner admission outcome.
It proves no process liveness, configurable scheduler, queue, executor lease,
automatic execution or ownership release. Increments 4/5/6 remain incomplete,
ADR-0050 Proposed, P9 unproven and AI Memory deferred. Publication remains
pending independent Git verification; no next slice is authorized.

## Executor recovery 1: account credit exhaustion

The owner requested continuation in another Claude account after the original
executor hit its credit limit. This is the workflow's documented recovery
exception for the same GLOBAL-ADMIT-01, not a new slice or scope/authority grant.
Only the replacement executor continues; do not resume the old executor in
parallel. Codex retains architecture, review and final GO/NO-GO ownership.

Fresh reconciliation on 2026-10-20 confirms main, HEAD/local origin/main/live
main `b3d53bfc3c604711228ade769284911f9d9c2b4b`, divergence 0/0, real index
empty; **73 pending paths: 36 modified and 37 new**. Preserve the entire partial
diff, planner documents and ignored evidence. The recovery inventory and
checkpoint are `logs/planner-review/global-admission-01/recovery-01-paths.txt`,
`recovery-01-inventory.json` and `recovery-01.md`; none is a GO or an approved
publication identity. Verify current files before continuing, never reset or
recreate them from the original three-path preflight.

The policy/gates/feeds, MVC query/endpoint, generated client, frontend
decoder/hook/composition and several tests exist as unreviewed partial work.
Their existence proves neither completion nor acceptance. Current-work still
describes the prior published slice and has not yet become GLOBAL-ADMIT-01's
implementation record. Inspect actual coverage and complete only the selected
ADR-0058 outcome. No GLOBAL-ADMIT-01 review GO or stable finding IDs exist yet.

Retained development logs under `logs/executor/global-admit-01/` include policy
74/74, claim 23/23, Agent dispatch 17/17, verification dispatch 59/59, feed
8/8 and endpoint 23/23 passes. Earlier compilation/analyzer and policy failures
remain. Despite its name, green-01-application.log has 1 failure and 9 passes;
do not call it green or invent its resolution from later differently filtered
passes. Frontend build and latest lint/strict e2e tsc logs report exit 0;
earlier lint/tsc failures remain. These are development/source-phase observations,
not independent reruns, full-suite results or final acceptance.

The final matrix, controls/restoration, journeys, reproducibility and coverage
must be reconciled against actual retained evidence. No whole Vitest, full
Application/API, full architecture, final mutation-control or journey pass is
established by this recovery check. Preserve missing or unretained worker
output as unknown; the screenshot and task labels are not test evidence.

Continue the existing scope and final validation matrix in this handoff and
ADR-0058. Reuse a passing check only if its source/configuration/fixture inputs
are unchanged and its actual coverage is sufficient. Inspect previous worker
outputs and pending commands before starting another shared-output writer;
do not kill unrelated processes. Return unstaged, uncommitted and unpushed for
Codex review. No new provider experiment, dependency, queue, lease, execution
grant, ownership release, next-slice selection, commit or push is authorized.

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
  tests are selected only for GLOBAL-ADMIT-01 under ADR-0058; PLAN-COORD-01,
  AGENT-SUP-01 and WORKSPACE-EXCL-01 are published. This strengthens existing exclusion only, not
  broader mutation or automatic coordination authority. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
