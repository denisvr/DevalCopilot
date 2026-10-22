# ADR-0062: Queue explicitly authorized local execution before admission

Status: Accepted

Selected architecture for LOCAL-QUEUE-01; not implemented or publication GO.

## Context and owner decision

LOCAL-COORD-01 and LOCAL-INSPECT-01 are independently published. The host can
advance and inspect one confirmed bounded cycle, but an admission conflict
refuses arming or ends progression. ADR-0006's visible queue is still missing.
Changing a failed claim into a polling retry would violate ADR-0060's single-use
preparation and uncertain-commit rules. Admission also needs protection between
selecting work and its ordinary claim; a browser-visible queue alone cannot
provide it.

On 2026-10-22 the owner explicitly authorized designing and selecting a bounded
opt-in queue for existing local cycles: pin plan, source, verification, settings
and budgets, wait for workspace/capacity, revalidate before advancing within the
original two hours, and close on host restart without resuming or redispatching.
The grant excludes quarantine release, process cancellation, extra corrections,
Human approval, commit and publication. It authorizes this product contract and
implementation selection, not running a product cycle in the planner chat or
any real-provider/native experiment.

## Decision

Deliver queue intake, durable admission records, fair selection among eligible
queued steps, pre-claim reservations, atomic transfer into the existing claims,
stop/deadline/startup handling and truthful cockpit disclosure together.

This narrowly extends ADR-0060 for a new explicitly confirmed admission mode;
its existing immediate mode keeps its behavior. Extend ADR-0057/0058/0059 only
to account for proved pre-claim reservations, and ADR-0061 only for the new mode
and recorded waiting state. No execution lease, process-aware recovery, general
scheduler, manual-operation queue or continuation of ADR-0055 planning is added.

### Intake and immutable authority

Keep the existing immediate arming operation and confirmation encoding unchanged.
Add protected MVC operations through IApplicationMediator:

- GET `/api/runs/{runId}/local-execution-sequence/queue/availability`, with the
  single required `planProposalMessageId` query key, once, exact ordinal spelling.
- POST `/api/runs/{runId}/local-execution-sequence/queue`, accepting exactly
  the immediate operation's five string members and its 1-KiB body bound.

Reject body/query/unknown/repeated/escaped-body-member and invalid identity
shapes before dispatch, using the existing strict-filter patterns and fixed
non-echoing feature errors. Unknown run is fixed 404. The new GET is bodyless;
the POST admits no query. Responses are complete API-owned graphs.

The route selects WaitForAdmission; no client supplies scheduling priority,
deadline, budget, reservation, effect, host token or operation identifier.
Use a separately versioned confirmation domain binding the mode, this queue
policy and ADR-0060's whole candidate. Immediate confirmation cannot authorize
queueing or vice versa. An identical normalized tuple replays its one receipt,
even after termination; another tuple/mode conflicts. One sequence per run
lifetime remains the rule; never adopt an old immediate sequence into the queue.

Keep ManualAgent/Running, exact eligible accepted-plan lineage, no prior
mutating claim, latest Ready owned workspace, active physical repository lease,
latest recorded checkpoint, complete enabled recipes and all pinned settings,
stop policies and budget rules. Queue intake may admit coherent workspace busy
or global AtCapacity readings. Conflicted/unavailable ownership, malformed
configuration, an unready workspace or any other authority defect refuses it.
Do not broadly ignore a project-busy error without proving its precise cause.

Intake observes no Git or process while another owner may be writing. It pins
the recorded source and tells the Human that source must still match when work
starts. Physical source freshness is mandatory during admitted preparation and
at the ordinary dispatch boundary. Drift ends the cycle; it never retargets it.
All intake state, pins, initial waiting admission and one Human event commit
atomically under the existing short SQLite write-lock pattern.

At most 64 live WaitForAdmission cycles per authoritative database, including
their waiting, preparing, claimed and stopping phases. This bounds pending work;
it is not an execution limit. An ended cycle with quarantined ownership still
consumes the ordinary workspace/global capacity. Queueing consumes no Agent
claim, invocation-time reservation or correction allowance. The original two-hour
advancement deadline starts at this intake and never resets while waiting.

### Pending steps and order

Append WaitingForAdmission to the sequence vocabulary and persist immutable
admission mode (Immediate by default for existing records). A queued cycle
starts waiting before implementation. After a coherent result, classification
either ends the cycle normally or atomically concludes that step and records
one pending next step. Apply exhaustion/escalation before creating more work.
The pending descriptor pins exact kind/pass/recipe position, predecessor,
source, report and correction subject; no latest input is selected later.

Use one feature-owned admission record per eventual operation step, with states
Waiting, Reserved, Consumed, Retired or Unresolved. Waiting creates no operation
step or preparation token and owns no workspace/global slot. One pending action
per cycle; at most the existing 102 operations/admissions per cycle. Reservation
creates its one ordinary Reserved step; no repeated preparation or claim is
introduced. Keep old step bounds, six Agent claims, three passes and the shared
maximum of two corrections unchanged.

Assign stable, strictly increasing, database-orderable Int64 coordination and
admission numbers under the write lock using a feature-owned counter. Guard
overflow/malformed state; never wrap or coerce it. Backfill only ordering and
Immediate defaults on historical sequences, without granting queue authority.
Use bounded keyset pages for host observation (32 per page, rotating/wrapping),
so an old waiting set cannot starve deadline observation of later live records.
Fix a high-water number per sweep, finish that finite range, then refresh it
on wrap; continuous arrivals must not prevent another visit to older records.
Read at most 65 live queued candidates to enforce the 64-cycle bound; never
materialize all historical records or use an in-memory sort of an unbounded set.

Select the oldest eligible pending admission by its number under the database
write lock. A blocked workspace does not block independent eligible projects;
ReadOnly work does not require a mutating slot. Among queued contenders for
the same workspace or available mutating capacity, do not bypass an older
eligible admission. This is fairness among queued steps, not priority over all
explicit manual/immediate requests or a starvation guarantee against them.
Each successor joins the tail rather than retaining its cycle's old priority.

### Reserve before external preparation, then transfer once

The host acquires available in-process preparation capacity before asking the
mediator to reserve the next eligible admission; never hold a durable reservation
merely while waiting for that semaphore. A short write-locked command chooses
the admission, revalidates its full database authority and current ownership/
limit, creates its exact Reserved step and original-host preparation token, and
marks the admission Reserved atomically. At most four queued preparations may
be held, including Unresolved reservations. Only the worker receiving the
committed ticket may prepare/claim.
No external preparation runs in that transaction.

A Reserved admission excludes other users of its exact workspace. It consumes
one global unit only if its step is Domain-classified WorkspaceMutating;
verification is potentially mutating. Derive effect from the closed step/
contract mapping, never a client or unchecked stored flag. ReadOnly Agent
preparation owns the workspace but consumes no global mutating unit. The
existing two-minute preparation deadline starts on reservation and remains
bounded by the cycle's original deadline; it is not an executor lease.

The ordinary claim accepts exact reservation self-exclusion only through its
internal ticket after full sequence/run/project/workspace/source/kind/contract/
reserved-operation/step/token proof. Its early and final write-locked gates
revalidate that proof. In the claim's OWN save/commit, create and link the
operation and consume the reservation atomically. Count exactly one effective
owner before and after transfer. Missing/foreign/duplicate/incoherent transfer
is unavailable, never a subtraction by UUID, run or workspace alone.

All existing manual Agent contracts, repair/planning claims, verification and
local commit obey reservations at their early, durable and dispatch/execution
gates. They gain no queue or reservation exemption. Include reservations in
workspace, capacity, abandonment and limit-lowering decisions through shared
database-side ownership predicates. Dispatch still uses its exact ordinary
operation owner, retains all safety/source checks and remains single-use.
The supervisors perform no queue retry and receive no authority from a timer,
heartbeat, browser, reservation expiry or mere eligibility read.

After preparation failure, claim uncertainty or an actual claim, never requeue
or try the operation again. Preserve independent exact-link durability probes
and conservative manifest retention. A lower limit or another tightening guard
may still refuse a claim/dispatch; report attention rather than retrying it.

### Stop, expiry and restart

Waiting remains visibly armed/waiting, never executing. Stop while Waiting
retires that admission and ends Stopped atomically, with zero operation claims.
Deadline or changed pinned authority ends NeedsAttention with a fixed reason.
Busy coherent ownership can remain waiting; unknown/conflicted authority cannot
be interpreted as a future free slot. Record events on actual transitions only,
not every observation tick. Stop after a claim retains existing drain semantics.

An unused reservation can be retired on stop, preparation expiry or startup
ONLY under the write lock after proving the full reservation/step identity,
Reserved/unlinked shape, and absence of the reserved operation in both operation
families and any dispatch/process evidence. Fence the old token in the same
commit so a late worker cannot claim. Recheck after any independent probe;
a preceding probe alone cannot authorize retirement. Any contradictory or
uncertain evidence leaves Unresolved admission/ownership and attention.

This retires unused pre-claim capacity; it releases NO claimed or dispatched
operation's ownership. ADR-0060's conservative quarantine and Exited proof are
unchanged, including terminal linked operations. No supported release/cancel/
retry/takeover is added. A reservation or operation on an ended run is not
silently dropped from ownership. Expired/stopped reservations remain counted
until their proved retirement commits; elapsed time alone subtracts nothing.

Startup closure runs after migration and before coordinators/supervisors. Close
every formerly waiting or advancing queued cycle as Interrupted, retire only
proved unused reservations as above, and preserve the existing exact refusal
proof for undispatched claims. Never resume waiting work, rebuild tickets,
redispatch, import output or adopt a result after restart. Claimed/dispatched
uncertainty keeps ordinary effective ownership. No new host process evidence,
native helper or process-table probe is selected.

### Disclosure and cockpit

Extend the existing sequence status response and its ONE run-owned control
reader with admissionMode and a nullable closed queue disclosure (admission
identity, pending kind/pass/recipe, enqueue time, original deadline and fixed
waiting reason with its recorded time). Start with AwaitingAdmissionCheck.
Distinguish WaitingForWorkspace, WaitingForCapacity,
WaitingForEarlierEligibleWork, WaitingForPreparationCapacity and Unavailable;
do not invent an exact global position or promise a start time. An expired read
must withhold starts immediately without waiting for a mutation tick.
The waiting reason describes the last durably recorded admission observation,
not present worker/slot availability. Record a change when observed, not an
event or timestamp refresh on every unchanged tick; the UI says so. In-process
worker availability is observation only and cannot bypass the database gate.

Append a workspace preparation status for a proved reservation; never call it
an Agent or verification process. Global recorded-owner counts include proved
mutating preparation reservations and limit lowering uses that same count.
Unknown identity/enum/numeric/storage shapes fail closed. Keep tokens, paths,
arguments and hashes private. Guard stored booleans by canonical value/bytes,
never nullable-bool truthiness. Canonical Int64 strings represent exposed counters.

Offer a separate explicit queue confirmation for the exact settled candidate
in both current cockpit regions, explain deferred source validation and the
original deadline, and submit once. Changing run/opening/candidate/mode or
capacity-confirmation lifetime invalidates retained callbacks; A-B-A, unmount,
overlap, reload, late success/failure and already accepted actions obey the
existing owners. One live reader supplies both regions' fences. Pending queue
authority blocks unrelated starts/settings exactly as existing live authority;
it grants no eligibility or UI-side advancement.

Historical ADR-0061 inspection reads the recorded mode/waiting state without
current eligibility, exposing no pending admission as an operation step. Its
selected-operation proofs, bounded step count, sealed admission and window
limits remain unchanged. Waiting, preparing, claimed bookkeeping, quarantine
and Awaiting Human approval must remain distinct.

### Storage and scope limits

Use additive EF migrations and feature-owned mapping, constraints, restrictive
keys/uniqueness, immutable authority/order/link fields and guarded revisions.
Persist admissions and the counter relationally; no event-derived authority,
generic queue/repository or process-wide mutable queue is authoritative. Retain
admissions for the run lifetime; no delete/purge endpoint. Upgrade/reopen/down
must preserve historical operation/output facts and assign no new grants.

This is partial Increment 5 scheduling, not its complete scheduler or recovery
exit criteria. ADR-0050 stays Proposed; P9 unproven; AI Memory deferred and
Increments 4/5/6 incomplete. No dependency, native/profile/security/helper/
credential/provider experiment, remote operation or new publication mechanism.

## Required evidence

Production-driven cycles with file-backed SQLite and independent connections
must prove idle and busy intake, confirmation mode separation, 64-cycle bound,
FIFO among eligible contenders, independent-project progress, both configured
limits and both orderings of limit change versus reservation/claim. Include all
six kinds, both correction origins, full pinned passes and unchanged exhaustion.

Test every manual/existing entrance against a reservation, exact self-exclusion,
atomic transfer, rollback/save/commit uncertainty, malformed/foreign links,
reserved-operation UUID collisions across families and no budget/artifact loss.
Stopping/expiry/startup races cover Waiting, reservation, external preparation,
claim, both dispatch markers and late result. Prove no resumed work, lost
quarantine owner, false Exited evidence or release on an unanswered probe.
Bounded paging must visit more than 32 live records and observe later deadlines.

Prove API auth/body/query/errors/closed graphs and generated-client stability;
frontend exact decoding, both-region fences, queue confirmation and every
request lifetime. One authenticated real-host deterministic journey queues
behind a held owner, proves no waiting invocation, releases only its owned test
gate, observes admission/progression without per-step browser requests and
reloads recorded state. Stop/restart permutations belong in hosted/Application
tests with frontend regression coverage; no production test hook is permitted.
Run existing local-execution, planning and collaboration journeys serially.

Four compiling controls must fail named permanent tests: omit a reservation
from admission ownership; break atomic reservation-to-operation transfer;
permit a startup waiting admission to advance; remove an ended frontend
confirmation/reader owner guard. Keep every failed/invalid attempt separately,
restore exact hashes, touch and rebuild; hashes do not authenticate old binaries.
The planner handoff supplies the proportional final matrix and stop boundaries.
