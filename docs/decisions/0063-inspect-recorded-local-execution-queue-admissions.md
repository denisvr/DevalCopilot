# ADR-0063: Inspect recorded local execution queue admissions

Status: Accepted

## Context

LOCAL-QUEUE-01 is independently verified published as 8b9a824. ADR-0062's live
status exposes the outstanding admission, and ADR-0061 inspects recorded
operation steps. Neither exposes the complete admission ledger: a waiting
admission may retire without creating a step, and consumed admissions no longer
appear in the live queue disclosure. The operator cannot inspect that recorded
history from the current run and run history without correlating database rows.

Select LOCAL-QUEUE-INSPECT-01 as one read-only outcome: the bounded admission
ledger, exact step links and existing operation-evidence drill-down in both run
surfaces. This advances Increment 5's transition explanation and evidence work,
not its recovery or execution authority. As with ADR-0061, the planner selects
inspection under existing development authority; no new owner execution grant
is inferred or required. This extends ADR-0061's inspection and ADR-0062's
recorded disclosure only. Their execution, ownership and quarantine rules stand.

## Decision

### One protected recorded-history operation

Add bodyless MVC GET
`/api/runs/{runId}/local-execution-sequence/inspection/admissions`, dispatched
through IApplicationMediator as GetLocalExecutionAdmissionHistoryQuery.
Use an API-owned response graph and the generated NSwag client. It accepts no
query keys. Reuse ADR-0061's strict body/query rejection before dispatch,
including readable bytes/transfer encoding and ordinal decoded query names,
with its fixed non-echoing Problem Details. Unknown run uses the same fixed
`local_execution_inspection.not_found` 404. Authentication remains mandatory.

Return runId, status, nullable sequence header and admissions. Status is closed:

- NotRecorded: a known run has no recorded sequence; null header, empty list.
- NotQueued: a coherent Immediate sequence has no admission rows; null header,
  empty list. An Immediate sequence with admission rows is Unavailable.
- Recorded: a coherent WaitForAdmission sequence and its complete bounded
  nonempty ledger; header and all rows in ordinal order.
- Unavailable: present but unprovable root, structure, storage or linkage; null
  header and empty list. Never turn corrupt storage into absence or a partial
  count/list. Genuine database/cancellation faults keep the normal pipeline.

The header contains sequenceId, admissionMode, recordedState, fixed reasonCode,
sequenceRevision, armedAtUtc, advancementDeadlineUtc, updatedAtUtc and endedAtUtc.
Each row contains admissionId, admissionNumber, ordinal, kind, passNumber,
nullable recipeOrdinal/name, recordedState, fixed waitingReason and its
recorded timestamp, enqueuedAtUtc, updatedAtUtc, revision, fixed nullable
reasonCode and nullable stepId. Use canonical positive Int64 strings for
admissionNumber and revisions; bounded exact integers for other numbers.

Do not expose a reserved operation identity, preparation token, executable,
arguments, workspace path, confirmation/settings digest or message contents.
stepId is recorded structure, never proof of an operation or its outcome.
Use the existing selected-step operation inspector for those proofs; this
endpoint reads no artifact, filesystem, Git, provider, account or process.

### Bounded historical proof and coherent reading

Read untracked, guarded scalar projections of persisted facts, not current
eligibility. Bound the ledger to Domain's recipe-dependent MaximumSteps;
read at most the absolute maximum plus one (103 admission rows) to detect
overflow. Bound any recipe/step projection to the existing 33/103 guards too.
No pagination or client limits are needed for this fixed run-lifetime ledger.
Never materialize an unbounded historical set or obtain a whole entity merely
to reject an oversized text/blob afterwards.

Prove the recorded run/sequence project, ManualAgent mode, pinned workspace/
project, initial checkpoint/workspace/fingerprint, defined admission mode and
valid sequence/recipe bounds. For each admission prove its sequence, distinct
positive order number, unique gapless ordinal, closed kind/pass/recipe shape,
pinned recipe membership/name, source checkpoint/workspace/fingerprint, report
run and ExecutionReport kind, predecessor and matching correction-origin subject.
Reuse Domain's mapping; never a stored/client effect flag. Later legitimate
workspace, settings, recipe or run changes do not invalidate coherent history;
never substitute latest inputs or call SourceHolds/admission/ownership policies.

For a linked step prove the entire admission/step tuple: sequence, ordinal,
kind, pass, recipe, predecessor, source checkpoint/fingerprint, report and
subject. A step has at most one admission, and every recorded step of a queued
sequence has its exact admission. Waiting has no step. Reserved links a
Reserved step with no actual operation; Consumed links a Claimed or Concluded
step with its exact reserved/actual operation link; Unresolved links the
Unresolved no-actual-link shape. Retired has either no step (waiting closed)
or its exact Revoked no-actual-link step. Only Consumed rows may precede the
last row; there is at most one Waiting, Reserved, Unresolved or Retired tail.
Require the Domain-compatible recorded sequence/admission/step lifecycle shape,
including an Unresolved reservation retained on an ended sequence.
Do not confuse Consumed admission with successful operation completion, or
Unresolved admission with a releasable operation. Validate historical shapes,
not current ability to execute or actual process liveness.

Obtain the root, bounded admissions and the facts needed for these checks in
one provider-neutral EF statement. Do not split into independently observed
binding/row/step reads or depend solely on sequence revision: a waiting-reason
change can update admission revision without updating sequence revision.
Use a single-query projection; no write lock, new transaction/lock port, retry
or mutation. Independently connected race tests must establish the statement
actually ran and covers the joined authority facts. If EF translation prevents
this shape, challenge with a bounded alternative before weakening consistency.
Separate later operation-inspection requests remain independent observations,
not one atomic replay of everything.

Classify enums/reasons and guard exact numeric/storage/byte shapes before CLR
conversion, following ADR-0061 and the existing recorded projection patterns.
Reject unknown states, fractional/oversized counters, malformed null/link shapes,
foreign identities and excessive rows. Keep the accepted typed GUID/time read
conventions; add no repository-wide projection or boolean refactor.

### Current run and run-history inspection

Within the existing local-execution inspector, offer an explicit collapsed
queue-admission inspection for a recorded queued sequence. Current-run Usage &
Evidence and selected run history use the same component. Opening/reloading
reads this ledger only; closing, changing run/selection, A-B-A, unmount or a new
opening ends the prior request owner. A late result or retained callback cannot
populate, select or open evidence for another visit. Closed panels fetch nothing;
there is no polling or automatic operation/artifact read.

Show every admission state and its fixed reason/timestamps truthfully. A waiting
reason is its last recorded observation, not current capacity, rank, eligibility
or a promised start time. The number is recorded admission order, not an exact
global queue position. Waiting and retired-without-step rows open no operation.
An explicit action on a proved linked step reuses ADR-0061's selected-step path
and its operation/window proofs, without searching unrelated history or changing
the active run. Recheck current ownership/membership when callbacks execute.
If the linked step is not in the currently validated sequence index, require
an explicit index reload rather than searching or selecting a substitute.
Expired or ended rows remain historical facts; inspection changes no fence.

Render host values as literal text, accept only complete closed response shapes
and use the existing owned-request hooks. Keep the one live sequence reader as
the sole live control input; this ledger neither enables nor withholds starts.

### Scope and evidence

No table, migration, event, counter, worker, grant, dependency or runtime-policy
change is selected. No queue/reorder/stop/rearm/release/retry/resume/approval/
commit/publication action is added. No global queue census/dashboard, provider
experiment or process-aware recovery is included; those need separate membership
or authority contracts. All admission states, both UI surfaces and exact linked
evidence ship together as one coherent recorded-run inspection outcome.

Use production-created queued cycles over file-backed SQLite for every state,
both correction origins and recipe ordering. Prove waiting/retired rows with no
step, Immediate/absence distinctions, historical stability, all tuple mismatches,
malformed storage, overflow, missing/duplicate rows and all-or-nothing refusal.
Reads create no write/event, claim, budget consumption or external/store call.
Protected API tests cover auth/strict transport/closed graphs and representative
proof failures. Frontend tests cover decoding, both surfaces, explicit reads,
link reuse and every ended request/selection owner. Extend the owned real-host
queue journey to inspect waiting with zero steps and consumed admissions after
progress, use run history and open a linked operation only explicitly.

Two compiling controls must kill named permanent tests: omit a required
admission/step identity comparison; remove the ended frontend request-owner
guard. Preserve invalid/failed attempts, restore hashes, touch and rebuild.
The handoff supplies the proportional final matrix and broadening triggers.
