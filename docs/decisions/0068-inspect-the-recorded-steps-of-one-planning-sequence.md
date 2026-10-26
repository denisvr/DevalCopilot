# ADR-0068: Inspect the recorded steps of one planning sequence

Status: Accepted

## Context

ADR-0055 delivers one bounded read-only planning sequence. Its live panel shows
step receipts and final identifiers, but does not open the evidence of a step.
The historical run detail mounts no planning-sequence surface. The owner must
correlate identifiers with independently browsed attempts and messages, including
after a stopped, failed or interrupted sequence.

ADR-0061 already solves this navigation problem for local execution. The
existing selected Agent detail owns recorded execution facts, inputs, outputs
and sealed artifacts, including an attempt that emitted no message. Reuse that
surface and its contracts rather than introducing another evidence store or
deriving a planning lineage from the visible message window.

The planner selects this additive recorded inspection within existing functional
development authority. It grants no execution or recovery authority and
supersedes no accepted decision. It contributes to Increment 5 evidence
inspection; complete run replay and Increments 4/5/6 remain incomplete.

## Decision

Deliver PLAN-INSPECT-01 as one complete outcome: a bounded recorded sequence
index, one selected step's exact Agent association, explicit existing evidence
drill-downs, and integration in both the current run and selected run history.
Add no table, migration, event, background worker, provider or dependency.

### Protected operations

Two bodyless MVC GETs dispatch one query each through IApplicationMediator:

- `/api/runs/{runId}/planning-sequence/inspection`
  (`GetPlanningSequenceInspectionQuery`).
- `/api/runs/{runId}/planning-sequence/inspection/steps/{stepId}`
  (`GetPlanningStepInspectionQuery`).

Both reject every query key and any request body before mediator dispatch,
including transfer encoding and readable bytes without a positive content
length. Use fixed shared Problem Details 400 codes
`planning_sequence_inspection.body_not_allowed` and
`planning_sequence_inspection.query_not_allowed`, without rejected values.
An unknown run or missing/foreign step uses the same non-disclosing 404
`planning_sequence_inspection.not_found`. A known run without a sequence is
NotRecorded only on the index. Present but unreadable evidence is never absence.
Keep explicit authorization, complete API-owned response graphs and reproducible
NSwag generation; no new anonymous operation or global request-filter change.

### Recorded structural index

Read untracked persisted facts only. Do not call the live planning status,
arming availability, classifier, admission, fence or mutation handlers. Read
no current lease, latest workspace/checkpoint, Git, filesystem, process,
provider, account or environment state. Later legitimate workspace changes and
a newer run do not invalidate coherent recorded source references.

The index contains runId, status (Recorded, NotRecorded or Unavailable), a
nullable sequence header and steps. Non-Recorded has a null header and empty
steps. The header contains sequenceId, workspaceId, checkpointId,
fingerprintSha256, recordedState, reasonCode, revision, armedAtUtc,
updatedAtUtc, stopRequestedAtUtc and endedAtUtc. Omit detailCode, objective
text/hash, host/preparation tokens, effective state, manual fence, eligibility
and final plan/approval references. Recorded state is bookkeeping, not proof
of acceptance or present execution authority.

Read at most six step rows to detect the reachable excess over five. Require
unique nonempty identifiers, gapless ordinals 0..4 and ADR-0055's fixed
Planner/CriticalReviewer/Resolver/CriticalReviewer/Resolver roles and contracts.
Zero steps is supported. Validate known sequence/step states and fixed reason
codes, positive revisions, supported UTC time shapes and ordering, and the
claimed versus unlinked shape. Claimed/Concluded has exactly the reserved
AttemptId; Reserved/Revoked/Unresolved has no actual link. All preceding steps
are Concluded; at most the last step may be outstanding or retired. Do not
require an ended sequence's last claimed attempt to be terminal: attention and
restart can close advancement before that attempt settles.

Prove ManualAgent mode, run/sequence project identity, the pinned workspace's
project and the pinned checkpoint's workspace/fingerprint. Ordinal zero has no
predecessor or input Proposal; each later step names the immediately preceding
step's linked attempt. Every named input is a Proposal of this run. For review
steps it is attributed to that predecessor attempt; a Resolver names the same
Proposal as its preceding review. Do not load message content, whole-run
lineage, ordered attempt inputs or outputs to reconstruct semantic acceptance.
Those are separately inspected under their existing contracts. A malformed root,
step set or structural reference withholds the whole index, never a partial list.

Each member discloses stepId, ordinal, role, provider, responseContract,
recordedState, reasonCode, revision, predecessorAttemptId,
inputProposalMessageId, nullable attemptId, preparationReservedAtUtc,
preparationDeadlineUtc and updatedAtUtc. A link is recorded identity only;
the index supplies no attempt result, liveness or dispatch assertion.

Admit closed enums, nonempty UUIDs, lowercase SHA-256, reason codes of at most
64 ASCII characters (192 stored bytes before exact admission), canonical UTC,
positive revisions at most 9007199254740991 and exact supported integers before
provider conversion can
coerce or throw on corrupt storage. Unsupported nonnull numeric storage must
not become NULL, zero, a rounded value or a plausible enum. Apply finite
storage guards before materializing variable text. Reuse established projection
techniques without changing shared release/ownership projections or persistence.
Real database/cancellation faults keep the normal pipeline; no broad catch.

Read and recheck the sequence revision around the complete structural read and
selected association proof. A concurrent transition makes that answer
Unavailable, with no automatic retry. Prove this with a deterministic independent
SQLite writer. This is a recorded relational reading, not authenticated history
or an atomic snapshot of independently opened evidence; matching rewrites remain
outside its assurance.

### Selected step and existing evidence

Re-prove the bounded structure and exact step membership on every selected
request. Unknown/foreign membership is the fixed 404; an existing member whose
structure or association cannot be proved is Unavailable. The response names
runId, sequenceId and stepId, status (Recorded, NoLinkedAttempt or Unavailable),
nullable step and nullable agent. Non-Recorded has null payloads.

NoLinkedAttempt is only the coherent Reserved/Revoked/Unresolved shape. It says
no attempt link is recorded, never that preparation or execution could not have
occurred. Do not discover an attempt by a reserved UUID or substitute a later
manual attempt. In particular, Unresolved stays uncertainty, with no release.

For a linked step, prove exact reserved/actual AttemptId equality, Agent kind,
run, fixed ordinal-owned role/provider/contract, coherent supported Agent
identity, and exact pinned workspace/checkpoint/fingerprint. Return only the
existing immutable selected-attempt header (identity, positive number, role,
provider, response contract) with the step. A Running, failed, refused,
interrupted or no-output attempt remains inspectable when that association is
coherent; do not fabricate success or require a producing message.

Reuse AgentAttemptDetail with this expected immutable identity and its existing
evidence/input/output/verification/artifact readers. The selected step's input
Proposal may be opened explicitly through CollaborationEvidenceDrilldown.
Those operations independently validate their own evidence. Keep their limits,
redaction, content admission and window behavior unchanged. Do not infer a
host-constructed escalation as an attempt output, add final-reference semantics,
traverse a graph or replace the live planning reader.

### Frontend ownership

One collapsed **Inspect recorded planning** region is available for the current
run and the selected historical run. Read the index on explicit open/reload;
select one step and read only its association. Selecting it may mount the
existing Agent detail's ordinary metadata read; input/output content and artifact
bytes remain explicit. No polling, prefetch of other steps, automatic retry,
background drain, export, browser persistence or current-run selection change.

Opening, run/sequence/index visit, step and attempt own callbacks, pending
requests and results. Close, reload, replacement, A-B-A, remount, retained
handlers and late/overlapping responses cannot disclose or act for a successor.
Reload clears selection/content before reading. Invalid/Unavailable clears
earlier success. Decode exact identities, closed discriminated shapes, full
bounded sets, canonical UTC strings and decimal Int64 revision strings before
rendering. Render untrusted values as literal text.

Mount no arming, stop, implementation, recovery, approval or publication action.
The inspector neither enables nor disables mutation controls; ADR-0055's live
reader and all existing fences remain their sole owners. Label recorded state
and independent evidence truthfully; neither claim nor dispatch is liveness.

## Alternatives and scope

Reusing the live status in history is rejected: it includes fresh admission,
effective deadlines and a manual-control fence, and is not the bounded recorded
inspection contract. An index-only slice leaves the correlation problem intact;
all five possible steps and both surfaces belong in this outcome.

Full semantic planning replay and a cross-stage final delivery summary span
additional message, verification, approval and commit proofs. They are separate
outcomes, not prerequisites for inspecting exact recorded associations. General
recovery, queue controls and automatic commit/publication change authority;
provider-gap work needs qualification outside this selection. None is implied
by this read-only addition. ADR-0050 remains Proposed, P9 unproven, AI Memory
deferred, and frozen native/profile/credential/provider authority is not renewed.

## Required evidence

Use real file-backed SQLite for bounded structural/association proof, supported
storage versus corrupt/oversized/foreign facts, revision races and absence versus
unreadability. Cover zero through five steps, a sixth row, every fixed role,
stopped/interrupted/attention sequences, unlinked uncertainty and linked attempts
without messages. Inspection performs zero writes or external observations.
Exercise both GETs' full protected MVC/body/query/error/wire matrix and client
reproducibility. Prove frontend decoding, lifetime races, cold closed state and
existing evidence reuse. One authenticated automatically headed journey over
deterministic provider doubles proves current and historical inspection and an
explicit reload; baseline all business POSTs and durable facts before inspection
to prove inspection adds none. Browser reload is not host restart. Keep failed
attempts and earlier evidence scopes under the validation policy.
