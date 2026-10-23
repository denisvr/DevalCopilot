# ADR-0064: Inspect outstanding local execution queue admissions across projects

Status: Accepted

## Context and authority

LOCAL-QUEUE-INSPECT-01 is independently verified published as 1e58dda.
ADR-0063 exposes one run's complete admission history, including retired waits.
ADR-0062's live control exposes one run's outstanding admission. Neither lets
the operator discover outstanding queued work across registered projects without
opening each project and run. The global recorded-owner count is not that list:
Waiting owns nothing, and Consumed admissions may have an outstanding operation.

Select LOCAL-QUEUE-OVERVIEW-01: one bounded recorded overview, exact run selection
and reuse of the existing recorded inspector. This is read-only inspection under
the existing development authority, as in ADR-0061/0063; it needs no new owner
execution grant. It adds the separate global membership/snapshot contract deferred
by ADR-0063. All execution, ownership, quarantine and queue authority stays with
ADR-0060/0062 and the existing gates. No increment is completed.

## Decision

### Exact membership and bounds

The overview covers this authoritative database, independent of project selection.
Its membership is the admissions stored as Waiting, Reserved or Unresolved.
Include Unresolved on an ended sequence. Do not filter on a live run, non-ended
sequence, current project/workspace, deadline, apparent eligibility or a latest
row. Such filters could hide a retained reservation. A passed deadline changes
neither membership nor stored state during this read.

Consumed and Retired admissions are outside this outstanding-admission view.
Immediate cycles, manual operations and cycles whose only tail is Consumed are
therefore not listed. Empty never means no active cycle, no owner or free capacity.
The complete run history remains ADR-0063's separate explicit read.

Start membership from admissions, with closed database-side state classification.
Exclude only a recognized Consumed or Retired state; unknown, null or malformed
stored state must remain a candidate that makes the answer Unavailable. Do not
drop an orphan admission through an inner join or filter to known good roots.

ADR-0062 bounds non-ended queued cycles to 64 and held Reserved/Unresolved
admissions to four, including ended cycles. Thus at most 68 distinct outstanding
admissions can be coherent. Read at most 69 candidates in recorded admission-number
order to detect overflow; never truncate into an apparently complete 68-row list.
Enforce at most 64 non-ended candidate sequences and four held candidates.
Require distinct admission numbers and one candidate per sequence/run. These
checks do not certify the unrelated live cycles whose tail is already Consumed,
or audit every excluded historical row.

### One coherent, bounded database reading

Use one provider-neutral, untracked EF statement for candidate membership,
registered project identity/name, root, complete bounded ledger and all historical
references needed by ADR-0063's proof. No per-project enumeration, N+1 readers,
split query, before/after revision read, new transaction, write lock, retry or
current ownership/eligibility call. Admission-only changes need not advance a
sequence revision. A later explicit drill-down is a separate observation.

Reuse the pure ADR-0063 whole-ledger proof for each candidate's sequence, requiring
Recorded, its exact candidate as the last admission, and the matching run/project.
Require exactly one recorded sequence for that run, retaining the existing
two-root duplicate guard; joining only the candidate's sequence must not hide a
second sequence of the same run.
Its root, source, recipe, predecessor/correction-origin, lifecycle, all step-tuple
comparisons and stored-value guards remain intact. A contradictory Unresolved
record makes the whole overview Unavailable; displaying Unresolved never repairs
or proves ownership. Existing typed GUID/time and monotonic-clock qualifications
remain. A registered project's current display name is descriptive metadata,
not pinned authority: require its exact project identity, bound stored bytes
before CLR conversion (800 bytes, then nonblank and at most 200 UTF-16 units),
and render it as text. Do not read its repository path.

A narrow extraction of the existing guarded historical scalar projection for
these two queries is allowed; keep the per-run reader's behavior and proof
unchanged. Do not introduce a generic query framework or persistence port.
The new query's membership and global bounds belong to its own operation.

Keep the existing per-sequence 103-admission overflow guard and 33/103 recipe/step
guards, enforcing the smaller recipe-dependent maximum. Materialization must also
have an explicit finite total bound, with an overflow sentinel and a completeness
check against the bounded candidate set. One permitted relational shape starts
with the 69-candidate subquery, left joins each root and ledger, and reuses the
guarded projection; a final ceiling of 69 * 104 + 1 = 7,177 rows detects expansion.
No omission or join multiplicity may pass as a complete candidate/ledger set.
Empty membership needs no historical materialization. Reject oversized individual
ledgers even when the aggregate would fit. Do not load unbounded entities or text.
If SQLite/EF cannot translate the coherent shape, challenge with evidence and a
bounded alternative before implementing a weaker consistency contract.

Any candidate, reference, storage, count, bound or historical-proof defect makes
the entire response Unavailable, with no entries or partial count. An invalid
candidate must not disappear through grouping, joins, paging or sanitization.
Genuine database and cancellation faults keep the normal host pipeline.

### Protected API and closed response

Add one bodyless/queryless authorized MVC GET:
`/api/local-execution-queue/inspection`, dispatched as
GetLocalExecutionQueueInspectionQuery through IApplicationMediator.
Reuse the strict inspection request guard and its fixed non-echoing 400 errors;
do not change authentication, binding or the existing inspection routes.

The API-owned response has exactly status and entries:

- Empty: no outstanding candidate; empty entries.
- Recorded: one through 68 coherent entries in strictly increasing admission order.
- Unavailable: an unprovable reading; empty entries.

Each entry contains exactly projectId, projectName, runId, sequence and admission.
The sequence and admission have the same complete transport shapes as ADR-0063's
header and row; reuse those API-owned shapes deliberately. The admission's state
is only Waiting, Reserved or Unresolved, and it is the proved tail of that
sequence. No whole ledger is returned by this global operation.

Keep canonical positive Int64 strings, bounded integers, explicit nullable
members, fixed vocabularies and UTC round-trip times. Return no reserved operation,
token, path, command arguments, settings/confirmation digest, report contents,
artifact or new live availability fact. A step link remains bookkeeping.
The generated client and strict raw-wire whole-answer decoder own this new
boundary. Reject extra/missing keys, invalid cross-field shapes, duplicates,
order errors and excess entries rather than coercing or partially accepting them.

### One global explicit inspection region

Mount one collapsed "Inspect outstanding queue admissions" region near the
existing global execution-capacity control, available without a selected project.
It belongs to the authenticated host session, not the selected project. Opening
or explicit reload performs one overview GET; closing performs none. No polling,
SignalR refresh, per-project fan-out, automatic retry, background prefetch,
browser persistence or capacity-reader reuse is added.

Explain that this is a recorded snapshot of outstanding admissions, not every
active cycle, an exact queue position, current eligibility, process activity,
free capacity or a start-time promise. Distinguish recorded Waiting, Reserved and
Unresolved and show sequence state, fixed reasons and their recorded times.
Expired deadlines are recorded deadlines, not permission to end or release work.
The existing live capacity/control readers alone keep their existing fences.

A "Select recorded cycle" action on a currently validated entry renders the
existing LocalExecutionInspectionPanel for exactly its run. Its existing explicit
open/index/ledger/selected-step/window actions remain required; selection itself
reads no operation or artifact and changes no active project/run. No new routing,
history search or command controls are needed. The overview and inspector reads
are independent snapshots, not an atomic replay or an operation ownership proof.

Own each opening/reload and each nested selection visit. Closing, reload, session
loss, unmount or selecting another entry ends the old selection and inspector.
A-B-A and retained callbacks cannot revive it. Recheck exact entry membership
(admission, sequence, run and project) when a callback executes. Project switching
alone leaves the same global visit intact and starts no read. Reload clears the
prior graph and selection immediately; failed/invalid reload cannot display old
rows as a fresh answer. Late success/failure cannot repopulate an ended visit.
Use existing owned-lifetime primitives without changing their shared semantics.
The new overview transport carries a real AbortSignal with a ten-second bound;
ending its visit aborts it as well as rejecting stale completion. No retrofit of
the older inspection transports is selected.

### Scope and required evidence

Ship membership, coherent protected read, strict client, global region, exact
selection and reused evidence inspection together. Splitting them would leave
either an undiscoverable API or a UI without the consistency boundary it promises.
Execution/recovery work has a separate authority prerequisite and is excluded.

No Domain policy, schema/migration, event, scheduler, coordinator, startup,
ownership/capacity gate, counter, reservation, stop/reorder/rearm/retry/resume/
release/approval/commit/publication action, dependency or provider/native
experiment changes. ADR-0050 stays Proposed; P9 unproven; AI Memory deferred;
Increments 4/5/6 incomplete. Existing qualification and evidence limits stand.

File-backed SQLite with independent connections must prove exact membership,
Waiting/Reserved/ended-Unresolved, excluded states, expired rows, complete bounds
including 64 waits plus four ended held reservations, overflow, unknown/orphan
candidates, duplicate/foreign/malformed data and unchanged historical proofs.
Use production-created representative states; boundary fixtures need no repeated
provider workflow per row. Label test-only guard bypasses and storage refusals.
Prove one statement includes membership and every reference it claims, writers
committed just before it are seen whole, and concurrent membership/reason/link
changes cannot mix snapshots. Require the race hooks to fire. Reads create no
write/event, claim, budget consumption, artifact/Git/process/provider call.

MVC tests prove authentication, strict request rejection, closed graphs and
representative refusal/wiring; pure permutations stay at the cheaper boundary.
Frontend tests prove decoder, actual transport abort/deadline, explicit reads,
project-independent ownership, selection membership and all ended lifetimes.
Extend the authenticated queue journey: discover the held waiting cycle globally,
select its recorded inspection without changing the current project, and explicitly
reload the overview after progression to show that its Consumed tail is absent.
Keep the current/history ledger and linked-operation assertions. No new provider
behavior, seeded ownership or production test hook is required.

Two compiling controls must kill named permanent regressions: exclude ended
sequences from overview membership; remove an ended overview/selection owner guard.
Keep failed/invalid attempts, restore verified hashes, touch and rebuild before
final passing evidence. The planner handoff supplies the proportional final
matrix. Return unstaged, uncommitted and unpushed for review; selection is no GO.
