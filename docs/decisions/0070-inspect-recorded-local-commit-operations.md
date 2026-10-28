# ADR-0070: Inspect recorded local-commit operations

Status: Accepted

## Context

ADR-0029 records at most one local-commit operation per run. The live cockpit
shows it through GetLocalCommitStatus, an operation that also decides current
eligibility when no operation exists. ADR-0033's historical run browser locates
only a completed delivery source; ADR-0032 reconstructs only a completed
delivery receipt. A historical run with a Failed, Interrupted or NeedsAttention
operation therefore has no dedicated recorded operation view. Absence of a
receipt source deliberately says nothing about that operation.

The planner selects LOCAL-COMMIT-INSPECT-01: one recorded read and its current
and historical run surfaces. All six recorded operation states belong to this
outcome. Splitting by successful versus unsuccessful operation would leave the
same visibility gap; adding recovery or a general final-evidence summary would
cross different physical-effect and authority-proof boundaries.

## Decision

### One protected recorded query

Add bodyless `GET /api/runs/{runId}/local-commit-inspection`, one MVC endpoint
and action, dispatched as GetLocalCommitInspectionQuery through
IApplicationMediator. The complete transport graph is API-owned and generated
through the existing NSwag configuration. This is independent of the existing
live status, request, execution, recovery, receipt and run-history queries.

Reject positive content length, any transfer encoding and readable undeclared
body bytes before dispatch with fixed 400 code
`local_commit_inspection.body_not_allowed`. Reject every supplied query key,
including blank, unknown, repeated and differently cased names, with fixed 400
`local_commit_inspection.query_not_allowed`. Use the shared Problem Details
contract and never echo input. Validate a nonempty run identity in Application.

An unknown run or one whose registered project is absent receives fixed 404
`local_commit_inspection.not_found`. A registered run/project pair receives
exactly `{ runId, status, operation }`:

- NotRecorded, operation null: no operation is associated with that exact run
  in this read. It says nothing about eligibility or a previous request.
- Unavailable, operation null: an associated operation exists but its selected
  facts cannot be admitted, ownership cannot be proved or more than one
  associated row exists. Never convert a damaged present operation to absence.
- Recorded, one complete operation: the facts below pass the bounded proof.

### Selected facts and proof

Read the exact run and registered project, its run-keyed operation association,
and that operation's named workspace and checkpoint in **one bounded scalar
EF statement**, using no-tracking reads and at most two result rows. A missing
workspace or checkpoint must retain the present-operation distinction. Prove
absence from the association, never from failure to parse an operation Id or
from a filtered-out damaged row. The unique run index remains the access path;
no history scan, aggregate materialization, collection include or count of all
operations is needed. Guard storage before provider conversions where needed;
do not catch database errors and relabel them Unavailable.

Recorded operation fields are exactly:

- operationId, projectId, workspaceId, checkpointId, checkpointNumber and
  checkpointFingerprintSha256;
- status: Prepared, Executing, Completed, Failed, Interrupted or NeedsAttention;
- branchName, parentCommitSha, treeSha, nullable commitSha;
- changedPathCount, totalBytes;
- createdAtUtc, nullable executionStartedAtUtc, nullable completedAtUtc;
- reason: exactly `{ kind, code }`, as specified below.

The operation must belong to the requested run and its registered project;
the run's stored execution mode must admit exactly ManualAgent. The named
workspace belongs to that project and has exactly the operation's branch.
The named checkpoint belongs to that workspace and has exactly the pinned
positive Int32 number, fingerprint and parent HEAD. Compare those recorded
values ordinally. A newer run, workspace, checkpoint, review or recipe cannot
substitute for a named record. Current workspace state, latest checkpoint,
lease state and current run lifecycle/stage do not decide this recorded read.

Admit canonical nonempty stored GUIDs in the provider writer's uppercase
36-character form; lowercase 40-hex object IDs and 64-hex fingerprint;
nonblank branch text of at most 512 characters without control characters;
exact INTEGER checkpoint number 1..Int32.MaxValue, changedPathCount 1..128
and totalBytes 0..4194304. Unsupported numeric storage, overflow, truncated
conversion, malformed identity or unknown status is Unavailable. Do not
default, clamp, normalize or partially display damaged facts. The HTTP GUID
binder's existing accepted route spellings do not relax stored-fact admission.

All supplied times must be nondefault UTC in the provider writer's supported
stored form and preserve tick precision. Enforce this complete tuple:

| Recorded status | Execution started | Completed | Reason |
|---|---|---|---|
| Prepared | null | null | absent |
| Executing | present, >= created | null | absent |
| Completed | present, >= created | present, >= started | absent |
| Failed or Interrupted | optional; if present, >= created and <= completed | present, >= created | present |
| NeedsAttention | optional; if present, >= created | null | present |

Failed/Interrupted can be recorded before the single-use execution marker;
NeedsAttention can likewise originate from a Prepared operation. Do not
invent a dispatch or completion time for either. A recorded Executing marker
is no observation of an executing process. NeedsAttention is not terminal,
success, failure or permission to retry.

commitSha is projected and returned only for Completed, where a valid object
ID is required. In every other state return null; the prepared proposed
commit identity must not escape even when damaged. Parent and tree are always
labelled as recorded preparation facts, never proof of promotion. Completed
is a recorded operation result, not a new receipt or a live Git observation.

An absent reason yields `{ kind: None, code: null }`. A present reason must
be nonblank and at most 128 characters in stored form. Map exact codes through
an operation-owned finite allowlist to `{ kind: Known, code: <fixed code> }`:
`local_commit.authority_changed`, `local_commit.git_unprovable`,
`local_commit.ownership_unprovable`, `local_commit.head_not_bound`,
`local_commit.prepared_state_unexpected`, `local_commit.unpromoted_state_unknown`,
`local_commit.branch_tip_unrecognized`, `local_commit.unknown_index_lock`,
`local_commit.index_state_unrecognized`, `local_commit.unknown_reference_lock`,
`local_commit.reference_lock_unproven`, `local_commit.interrupted_before_execution`
and `local_commit.interrupted_unpromoted`. Every other bounded nonblank reason
yields `{ kind: Unrecognized, code: null }`; it does not invalidate otherwise
coherent operation facts. Arbitrary stored reason text, dynamically constructed
native details and prefix lookalikes are never returned or rendered. Known
codes have fixed historical explanations; Unrecognized says only that a
reason was recorded that this version does not describe. This allowlist does
not change any producer's accepted reasons.

This proof covers only the selected operation facts and recorded ownership.
It does not read or authenticate authority members, approval messages/reviews,
author identity, commit message, request/authority/index digests, lease or
physical index receipts, prepared artifact paths/bytes, Git, files, process,
provider or network state. It grants no current eligibility and makes no claim
about current ownership, index/ref state, workspace cleanliness or publication.
The existing completed delivery receipt retains its stronger, independent
pinned-admission proof and its existing discovery/mounting behavior unchanged.
Its failure never changes this operation's Recorded status or vice versa.

### Current and historical run interface

Add one initially collapsed **Inspect recorded local commit** region beside
the current run's existing local-commit panel and one in the selected run
history detail, including when no receipt source was located. History mounts
only the new recorded inspector; never mount LocalCommitPanel or invoke the
live eligibility query there. Keep existing receipt placement and discovery;
do not duplicate it inside the new region or derive a new receipt source.

Opening reads once. **Reload recorded local commit** explicitly replaces the
answer and clears the old one immediately. Closing, run/project replacement,
A-to-B-to-A return and unmount abort the owned read and discard the answer.
Use the existing owned-lifetime primitives with a real ten-second request
abort, raw-wire admission before generated revival and whole-answer checks
for exact keys, requested run, identities, bounds, reason and status/time/SHA
tuple. A failed, timed-out, inconsistent or Unavailable answer shows fixed
copy and no partial operation. A closed region reads nothing. No polling,
automatic retry, live-event refresh or cache across visits is added.

Render all values as inert text with explicit **recorded** labels. Disclose
that Prepared/Executing are recorded markers, not present activity; an
ambiguous result is not diagnosed or recovered; a completed local record is
not current source applicability or remote publication; this read and any
receipt/history reads are independent, unsigned consistency, not an atomic
replay or authenticated history. No action for request, retry, recovery,
release, approval, commit, publication, export or artifact inspection is added.

### Validation and boundaries

Use migrated file-backed SQLite for every supported status/time tuple,
absence versus present damage, missing registered parents, foreign ownership,
malformed/oversized storage and exact INTEGER boundaries. Prove a historical
operation survives later work/recipe/review changes and reopen. Capture the
one bounded SELECT, forbidden-column exclusions and zero write/external-call
behavior; metadata inspection must open no artifact or repository. Keep the
existing live status, receipt and history regressions.

API evidence covers normal launch authentication, verbs, body/query refusal
before dispatch, fixed 404, all three answer states, representative operation
states/reasons, closed OpenAPI graph and the MVC authorization inventory.
Frontend evidence covers whole-wire refusal, every recorded status, truthful
reason/SHA wording, timeout, stale response/callbacks, reload, closure and both
compositions, including historical runs without a receipt source.

Extend the existing authenticated collaboration journey at its completed
local delivery and later historical-run phases. Run it once automatically
headed after announcing it, using its production-written owned fixture and
deterministic executables, normal authentication and supervisors. Await the
exact new responses, compare rendered operation facts with the database,
prove cold closure, explicit reload and the older run beside a newer run with
NotRecorded. Baseline business POSTs, executable invocations, named durable
facts and repository snapshots after setup settles; inspection adds none.
State fingerprint scope precisely. Page reload is not host restart;
file-backed reopening is proved separately. Other operation statuses belong
to Application/API/component tests; do not manufacture a recovery journey.
Trace/video/screenshots off; no Inspector, debug/UI mode or manual Resume.

No Domain/producer/shared proof/lifetime primitive, schema, migration, worker,
dependency, authentication, scheduling, ownership, approval, commit, recovery
or publication behavior changes. No frozen native/profile/credential/provider
qualification or real-run recovery is renewed. Increments 4, 5 and 6 remain
incomplete; ADR-0050 stays Proposed, P9 unproven and AI Memory deferred.
