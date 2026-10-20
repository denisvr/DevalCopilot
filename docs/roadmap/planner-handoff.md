# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and the [workflow](development-workflow.md) apply.
Codex remains planner/reviewer and performed planning and judgment itself.
WORKSPACE-EXCL-01 correction 1 is reviewed GO below for the exact recorded
content. Continue publication in its current Claude executor chat, following
the external approval identity. Codex implemented no product fix; publication
and independent verification are still pending.

## Verified baseline and prior publication

AGENT-SUP-01 is published and independently verified. On 2026-10-19, main,
HEAD, local origin/main and a fresh live refs/heads/main all equal
`f95e337484d6d50f502647b4abf725be2d7ae184`, tree
`9171fa7dd286329c1f7f342fd5b33c0a087cafd0`, sole parent
`a78e572c84e2e7a9a86ac90c1e1b286e6001973e`, divergence 0/0. Exact GO path set:
141 files, 43 modified and 98 added. The real index, tracked worktree and
nonignored untracked inventory were clean at independent verification.
`logs/planner-review/agent-sup-01/publication-verification.json` and `.md`
retain the independent tree/parent/path/live-ref comparisons. The approval
inventory bytes still match their original hash; the executor's reported
line-ending comparison difference affects no approval identity.

ASUP-R1 through R5 are resolved. ASUP-C1 is accepted nonblocking: duplicate-key
journey counts rose, with no proven cause; pre-existing sibling keys were
identified, not proven to explain the increase. Review 1/2, validation
adjustments, temporary-probe removal, full-suite failures and isolated passes,
negative-control provenance, tooling failures and missing original evidence
remain as recorded in this file at `f95e337` and its ignored review records.
The committed current-work remains an implementation-for-review record, not a
request for a closure commit. Publication reopens no completed slice.

PLAN-COORD-01 remains independently verified at `a78e572c` (176 exact approved
paths, 66 M and 110 A), with its records under
`logs/planner-review/plan-coord-01/`. PCO-R1 through R5 remain resolved;
PCO-C1 remains accepted nonblocking. All earlier restrictions and evidence
losses remain; later checks establish no missing original output or cause.

Continuity at publication: **CONTINUE_THIS_CHAT**. Current-session metadata at
2026-10-19T21:50:21.154Z reported last total tokens 141294 and context window
475000 (29.75%). This is an observed local metadata ratio, not an asserted UI
meter reading; prior 27.16% and 61% figures remain historical. No transition
threshold above 60% was observed. The completed AGENT-SUP-01 executor chat is
not reused for a different slice.

This selection changes only this handoff, the decision index and new ADR-0057.
Executor preflight: the same main/HEAD/local/live baseline, empty real index,
these two tracked documentation modifications and one untracked ADR, with no
other nonignored paths. Codex created no commit or push and implemented no
product fix. Preserve the three planner documents for review with the slice.

## Remaining MVP critical path against code

Increments 4/5/6 remain incomplete. ADR-0050 stays Proposed, P9 unproven and
AI Memory deferred; no delivery percentage or date is claimed.

| Front | Actual baseline and remaining dependency |
|---|---|
| Planning | ADR-0055 coordinates at most five existing read-only planning attempts. Accepted planning or Human escalation ends that grant; no mutating successor is authorized. |
| Operational evidence | ADR-0056 now records current host Agent phases, heartbeat and absolute deadlines for all seven contracts. It proves neither provider progress nor liveness and supplies no execution authority. Verification, standalone Process and local commit retain separate lifecycles. |
| Existing workspace exclusion | Running Agent attempts are unique per run; Running verification is unique per workspace. These separate sets permit concurrent claim/dispatch on one Ready leased workspace. The selected slice closes that reciprocal safety gap under existing explicit requests. |
| Local execution loop | Existing implementation, verification, review, diagnosis/correction and local-commit commands execute explicit requests; no complete stage coordinator exists. A single authorization for automatic workspace mutation requires a new owner authority decision and resolved contracts, not inference from ADR-0055/0056. |
| Scheduling and recovery | Global two-mutating-run capacity, visible queue, finite execution leases and process-aware recovery remain undelivered. This slice adds no queue, slot, takeover, lease release or ambiguous-write resolution. |
| Provider controls | Claude account usage, eligible resume, live context capacity, manual compaction and other permission profiles require contracts/evidence. Raw Git and non-Windows boundaries remain. Real-provider qualification/experiments are unauthorized. |
| Remote/self-hosting | GitHub access/binding/consent are neither branch-push authority nor publication. P9/source-write denial and separate Git/GitHub/GCM qualification remain open. The stable packaged complete demonstration depends on the remaining local and remote capabilities. |

## Selected slice: WORKSPACE-EXCL-01 reciprocal workspace execution exclusion

Implement accepted
[ADR-0057](../decisions/0057-serialize-agent-and-verification-execution-within-an-owned-workspace.md).
An existing explicit Agent request and an existing explicit verification request
cannot acquire or dispatch competing execution on the same owned workspace.
Both cockpit and workspace controls explain the same recorded blocker. A
terminal actual owner returns control only through unchanged workspace,
source, budget and approval gates; an observation never releases it.

**Sizing judgment.** A one-way verification guard still permits the reverse
race. A claim-only or UI-only check leaves stale dispatch and direct API gaps.
All seven Agent contracts, repairs and coordinated claims intentionally share
this exclusion; splitting by role/provider leaves another entrance. Include
verification, local-commit interoperability, protected disclosure and both UI
regions in one functional outcome. Full scheduling/automatic local delivery
requires different authority and recovery contracts. Legacy Process attempts
lack an owned-workspace association, so including them requires an identity
and effect decision rather than deriving ownership from a path string. These
are concrete authority/lifecycle boundaries, not arbitrary file limits.

**Independent baseline evidence.** The production verification claim and
single-use dispatch both succeeded beside a persisted dispatched Running
Implementer on the exact same owned Ready workspace. One focused SQLite
assertion failed for that gap (2 s test duration). No provider/process/hosted
worker or Git command was started; the Agent was seeded with Domain factories,
not through a complete implementation lineage. Evidence and the source hash:
`logs/planner-review/workspace-exclusion-01/selection-evidence.md`,
`baseline-probe.cs` and `baseline-probe-02.log`. The original compiler attempt
failed MSB3026/CS2012 before testing. After a recorded build-server shutdown,
serial build with shared compilation disabled reached the intended assertion;
that later result does not establish a cause of the original lock. The probe
was copied with matching hashes, removed and the API test project rebuilt
with zero warnings/errors; no reviewer test remains in sources or binaries.

**Resolved architecture.** One Application-owned policy under
Projects/Policies/WorkspaceExecutions derives competing ownership from existing
Agent/verification rows and ADR-0029's reservation. No new table or migration.
Fresh early checks plus final write-locked checks in every claim; final dispatch
excludes only the exact operation kind/ID, never its whole run. Verification's
SourceChanged pre-dispatch decision retains the same authority check. Existing
local commit may execute its own valid Committing reservation and no other.
Conflicting/malformed owners fail closed without choosing, terminalizing or
releasing anything. Result recording and AgentSupervision keep their contracts.

One protected bodyless/queryless project GET, owned DTOs and closed whole-wire
validation disclose only project, exact latest workspace, status and reason.
Selected-project owned state with bounded refresh/cancellation feeds both UI
regions as an additional withholding hint; Idle grants no action. Stop after
current step, evidence inspection, refresh and metadata-only abandonment keep
their existing controls. Full tuples and timing behavior are in ADR-0057.

**Allowed changes.** Affected claim/dispatch/feed operations across all seven
Agent contracts and repair variants, verification and its pre-dispatch
SourceChanged check; local-commit admission/execution only for the same
exclusion contract while preserving complete approval identity; new project
query/MVC/API-owned graph/client/decoder/hook and cockpit/workspace composition;
feature-owned tests and narrow factual architecture/product/roadmap/current-work
updates. Adapt a claim's final transaction if necessary, preserving its external
preparation outside transactions and all existing atomic associations. Do not
merge supervisors, result contracts, generic pipelines or unrelated readers.

## Proportional validation matrix

First add permanent named regressions for the reproduced direction, the
reciprocal direction and final-seam races, using production claim lineage
where it is under test. Then fix at the narrowest shared boundary. Retain
commands, filters, original failures, elapsed times and source identity under
`logs/executor/workspace-excl-01/`; distinguish fresh/retained/not-run evidence.

- Application: new WorkspaceExecution tests plus each affected claim, repair,
  dispatch, eligibility, planning-step and local-commit class. Real file-backed
  SQLite and independent connections prove one winner across the two families,
  stale tracked-state refusal, old conflicting rows, exact self-exclusion,
  different-run/same-workspace blocking, unrelated-repository progress, no
  losing budget/counter/grant/artifact/link consumption, and known-result
  persistence despite a conflict. Run the complete Application project once
  on stable sources: shared admission semantics change in this slice.
- API: protected MVC auth/body/query/404/wire and authorization inventory;
  all seven real supervisor compositions, repairs/coordinated paths and held
  verifier/Agent overlap with unchanged invocation counts; verification and
  local-commit reciprocal claim/dispatch races. Run the complete API project
  once on stable sources because affected shared admission consumers are broad.
  Preserve all original failures and any isolated diagnostic passes. Neither
  untouched tests nor later passes explain failures or prove irrelevance.
- Infrastructure/Domain: real SQLite proofs above exercise the actual new
  persistence boundary. Retain unchanged migration/provider evidence; run
  affected policy/mapping/transaction tests only if their inputs change. A need
  for a new schema or generic transaction pipeline is a scope stop, not an
  automatic full-infrastructure expansion. Full architecture remains required.
- Frontend: new whole-answer wire/decoder/hook plus both UI regions, global
  claim withholding, existing planning/commit controls and project A-B-A/remount,
  pending/stale/invalid/timeouts and actual transport cancellation. Run whole
  Vitest once, lint and production build (including tsc -b) because selected
  project/cockpit composition changes. Strict e2e TypeScript expands its input
  files explicitly. Regenerate NSwag after a successful API build and prove
  byte-identical reproduction; do not change generator configuration.
- Browser: collaboration.journey.ts and planning-sequence.journey.ts, serial on
  stable binaries, retaining explicit stage requests, provider cardinality,
  approval/local-commit and coordinator fences. Extend selected-workspace
  disclosure/Idle recovery through the real authenticated host. Held hosted
  and UI tests prove the transient busy cases; do not modify shared fixture
  invocation contracts to stretch a fast browser phase. Other nine journeys
  stay retained unless an actual shared input change invalidates them.
- Two serial compiling negative controls: omit the Agent-side final fence,
  omit the verification-side final fence. The focused independent-connection
  regressions must fail; hash-verify restoration and rebuild before the final
  matrix. Compile failures/no-ops do not count as kills. No broad campaign.
- Affected formatting/analyzers/build, dependency/security checks without new
  dependencies, local/inbound Markdown links, sensitive-data scan and full
  diff. One writer to shared build/generation outputs; no routine clean builds
  or server shutdown without a demonstrated reason. A prior observed compiler
  lock permits the documented serial/shared-compilation workaround, not an
  assertion that every future failure has that cause.

## WORKSPACE-EXCL-01 review 2: GO

Codex independently reviewed the correction on 2026-10-20. Main, HEAD/local
origin/main/fresh live main remain `f95e337484d6d50f502647b4abf725be2d7ae184`,
divergence 0/0, real index empty. The complete reviewed delivery has **110
paths: 64 modified and 46 new**. The exact paths, full judgment, checks and
external publication identity are under
`logs/planner-review/workspace-exclusion-01/review-02/` (`reviewed-paths.txt`,
`review-02.md`, `approval.json`). Review 1 remains retained under `review-01/`;
its NO-GO is superseded by this GO for the exact final reviewed content only.
Codex implemented no product correction and created no real staging, commit or
push. The isolated temporary-index tree preparation leaves the real index empty.

- **WEX-R1 [P2], resolved:** verification and open-commit ownership now check
  both foreign association directions. Permanent policy, admission and real
  endpoint regressions preserve older same-project workspaces, unrelated
  repositories, terminal history and exact typed self-exclusion. Four
  Application cases have red evidence; the endpoint cases were first run green.
- **WEX-R2 [P2], resolved:** both state props are required, the two gates always
  apply, and null/omitted/unknown input withholds starts. Explicit Idle test
  compositions replace the runtime test exception. Refresh, inspection, Stop
  after current step and abandonment retain their controls. The changed
  transient-selection expectation observes the earlier pending workspace veto.
- **WEX-R3 [P2], resolved:** the three ordinary final reads now clean up their
  own manifest on cancellation/database failure before inserting anything;
  cancellation propagates, existing persistence errors remain, and disposal
  rolls back the transaction. Authorized implementation retains its existing
  durable settlement and original exception. Other seams remain inside their
  catches; ambiguous-save readback/retention is unchanged. Eight regressions
  inject the fault at the final policy read. Tests followed the fix; the
  compiling six-kill cleanup control supplies sensitivity, not red-first history.
- **WEX-R4 [P3], resolved:** WorkspaceExecutionScene and CompetingOwner have
  matching files; the two new fault types also satisfy one top-level type per
  file, with no unrelated split or fixture-cache change.

**Independent checks:** forced Application/API test project rebuilds from
restored sources, zero warnings/errors (12.09 s / 14.01 s); complete enumerated
affected Application classes 940/940 (2 m 5 s); API protected status, all seven
hosted exclusion paths, verification supervisor, planning and complete diagnosis
classes 114/114 (55 s); architecture with build 185/185 (2 s); affected frontend
wire/decoder/hook/transport/both regions/App 669/669 (9.93 s). No new reviewer
probes, product edits, provider/mutation/journey/generation campaign or full
project repeat by Codex. Nine current mutation-target hashes match the recorded
originals; generated client hash remains `978e2c30...14dd9`.

**Executor evidence and challenge accepted:** correction full Application
6818/6818 and API 2944/2944, whole Vitest 6386/6386, lint/build/strict e2e tsc,
changed-C# format and both serial journeys pass at their recorded scope.
Repeating the full backend projects was justified by the shared authority-read
change and discovered binary-provenance defect. Touching restored sources fixes
the control harness's stale-timestamp problem. The two compiling aggregate
controls have 36 Agent and four verification kills; the Agent aggregate covers
all eight handler classes, not an independent mutant per file. R3's six-kill
control and its earlier noncompiling attempts retain their actual limits.

The initial full API 2938/2940 and repaired class 22/22 remain historical with
possible mutant-assembly provenance, superseded as current evidence by the
fresh correction full run. Review 1's results are not retrospectively
binary-authenticated by later builds. The executor reports an early correction
run's six stale-mutant failures, but overwrote that log; accept this evidence
loss explicitly, without reconstructing or independently certifying the missing
output or cause. The initial 32-failure Application run is development evidence
on slice inputs, not a pristine `f95e337` baseline comparison. Initial Vitest
failures/fixture adjustment and format failures in 20 unchanged paths remain.

Review 1's direct planted diagnosis evidence, narrow busy-error precedence and
transaction-or-save test seam remain accepted with their original limits. No
concurrent-verification authority, lineage bypass or fixture cache follows.
**WEX-C1, accepted nonblocking:** two obsolete prop JSDoc paragraphs still
describe the removed test exception; required signatures, actual gates and
tests enforce withholding. Those comments grant no exception. Preserve the
approved content during publication. The project-owned latest-workspace hint
still has no independent workspace-metadata/source binding; Idle grants nothing.
Existing console/lint/build warnings, ASUP-C1, PCO-C1, unexplained failures,
missing original evidence and all earlier restrictions remain.

The executor's current-work retains its implementation-for-review wording.
This does not reopen review, require a closure edit or prove publication.
No next slice is selected. Publication is the sole next action; independent
verification and current-context assessment follow the actual push.

## Stop boundaries and next action

In the existing WORKSPACE-EXCL-01 Claude executor chat, verify the exact GO
identity and parent, stage only its approved paths, commit, verify tree/sole
parent/path set, push main normally to origin/main, and verify the live ref.
Stop on any mismatch, intervening change, failed push or remote divergence; no
force-push or history reconciliation. Do not change reviewed content or select
a next slice. Return the publication report for Codex's independent verification.
The AGENT-SUP-01 executor chat is done.

A material Git discrepancy, missing/contradictory architecture, inability to
preserve existing claim/result/commit authority, new schema or generic pipeline,
shared fixture/cache/invocation change, provider/credential/permission operation,
lease release, automatic claim/retry/cancel/takeover or broader mutation stops
dependent work for a bounded evidence-based discussion. Independent authorized
work may continue. An executor proposal is evidence, never architecture
acceptance. Codex decides any material challenge itself before dispatch changes.


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
  tests are selected only for WORKSPACE-EXCL-01 under ADR-0057; PLAN-COORD-01 and
  AGENT-SUP-01 are published. This strengthens existing exclusion only, not
  broader mutation or automatic coordination authority. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
