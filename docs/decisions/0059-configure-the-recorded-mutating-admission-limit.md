# ADR-0059: Configure the recorded mutating admission limit

Status: Accepted

## Context

ADR-0006 requires a default concurrency ceiling of two that the operator can
reduce to one. Published GLOBAL-ADMIT-01 (`1747fb2`) implements ADR-0058's
fixed two-recorded-owner prerequisite, across mutating Agent, verification and
local commit. ExecutionCapacityPolicy, its final fence, query projection and
frontend decoder still assume two; there is no operator setting. Workspace
exclusion, exact-owner dispatch and result recording already have independent
contracts and must retain them.

Automatic local delivery still needs a new owner authority decision. A queue
retains future execution intent and needs its own pinning/recovery contract.
Neither is inferred from read-only planning, supervision or capacity settings.
The next bounded prerequisite is one durable, host-wide choice between one
and two, enforced consistently at every existing admission boundary.

## Decision

GLOBAL-LIMIT-01 narrowly supersedes ADR-0058's fixed ceiling and fixed-limit
wire tuple. All its owner classification, associations, fail-closed rules,
atomic final checks, exact self-exclusion and execution-authority boundaries
remain accepted. The default stays two, the only alternative is one and there
is no setting above two or per-project/provider/run override.

### Durable configuration facts

Add one feature-owned Domain type and one additive SQLite table for immutable
execution-capacity configuration revisions. Each row contains only a positive
Int64 revision and an integer limit (one or two). The migration seeds revision
one with limit two for both existing and new databases. The primary key and
check constraints enforce the stored shape. Preserve every existing row/table,
including attempt, lease, approval, journal and ownership facts. Down removes
only the new table; it neither rewrites nor undoes external work.

A successful explicit change appends exactly one next revision in the same
short database write transaction that makes the change effective. The latest
revision is the current setting; read it fresh and untracked, never from a
host/static/browser cache or a tracked entity. These ordered immutable global
configuration facts retain the change history without inventing a RunId or
putting a host setting into every run's journal. No second current-pointer
table, global settings framework, history endpoint or history UI is needed.

Missing/unreadable configuration or a malformed latest revision is
Unavailable. Do not filter away a malformed latest row, fall back to older
history, seed at runtime, or default to two after an error. Revision overflow
refuses a change without writing. This is authoritative database history, not
tamper detection or historical authentication of externally replaced data.

### Safe updates and fresh admission

Implement one Application command, owned by the capacity feature, using the
existing SQLite-wide write boundary. Validate the requested integer before
database work. Inside the transaction read the current configuration and the
same coherent owner reading ADR-0058 already uses:

1. Unavailable configuration or ownership refuses with existing
   `execution_capacity.unavailable` Conflict. No revision is appended.
2. A request equal to the current limit is a no-op: return the current closed
   reading without a revision. It never repairs or releases owners.
3. A different requested limit below the coherent recorded-owner count
   refuses with fixed `execution_capacity.limit_below_recorded_owners`
   Conflict. Otherwise append the next revision and return the reading under
   the new limit.

In particular, lowering two to one while two owners exist is refused. Lowering
must not strand two admitted but undispatched owners behind each other's
dispatch checks, choose a winner, cancel work or silently create a pending
configuration request. The operator may explicitly try again after recorded
ownership changes. Expanding one to two changes only this additional veto;
it authorizes no invocation. Concurrent authenticated configuration requests
are serialized; the last committed explicit change is current. Repeating the
current value is idempotent, not a second workflow command.

Every mutating Agent entrance (ordinary and authorized Implementation,
ReviewCorrection and DiagnosisCorrection), verification and local commit
uses the current persisted limit at its existing early and final checks.
Read limit and owners again under the final SQLite write boundary; a limit
observed before preparation cannot authorize a later claim. Dispatch/execution
still validates and excludes exactly its own coherent kind-and-ID owner, then
requires other owners to be fewer than the current limit. Advisory feeds use
the same reading. Preserve GAD-R1's fresh stored/tracked classification guard.

Use no hard-coded two in an admission comparison. Recognized ReadOnly work,
result/outcome recording, source evidence, authorizations, approvals, Stop and
abandonment retain their existing capacity independence. Settings never
terminalize, cancel, renew, release, replay or retry an operation. Unknown
ownership still fails closed; changing a limit cannot repair it.

### Protected API and closed disclosure

Keep authenticated, bodyless/queryless `GET /api/execution-capacity` and its
exact four response members. Make `limit` nullable only for unavailable
configuration; otherwise it is exactly one or two:

| status | limit | recordedOwners | reasonCode |
| --- | --- | --- | --- |
| Available | 1 or 2 | integer 0 through limit minus one | null |
| AtCapacity | 1 or 2 | integer limit through Int32.MaxValue | execution_capacity_limit_reached |
| Unavailable | 1, 2 or null | null | execution_capacity_unavailable |

An owner-reading failure may retain a validated limit; an invalid/missing
configuration returns null limit and null owners. Never fabricate a limit.
Database faults retain the existing Problem Details boundary where applicable.

Add authenticated `PUT /api/execution-capacity/limit`, with no query parameters
and one required JSON object containing exactly `limit`, an integer one or two.
Reject missing/null/wrong/extra/duplicate members, malformed JSON, other shapes,
coercions and invalid values before a command can change state. Bound this
small body at 512 bytes, including bodies without Content-Length; retain the
host's authentication, content-type and error conventions. Invalid requests
are 400, excessive body 413 and unsupported media type 415. Expected command
conflicts are 409 with the fixed codes above. Do not return raw storage errors.
Success, including a no-op, is 200 with the same four-field reading, projected
under the write boundary. It is an advisory observation, not a future grant.

Use API-owned contracts, the mediator and the committed generated client. No
provider/process/Git call, project identity, configuration history, raw text or
new credential handling belongs to either operation.

### One host-session UI owner

Extend the existing App-owned authenticated host-session capacity lifetime.
Add one small global capacity control near the top of the app, visible without
a selected project, with current limit, the one/two choice, explicit Apply and
Refresh, and fixed English feedback. Changing projects never applies a value.
Do not add a settings navigation system. The control explains that changing
the recorded-execution limit starts no work. Display lower-limit refusal
truthfully; never imply a saved pending reduction.

Both existing cockpit regions receive the same validated capacity state. Their
guidance uses the validated current limit rather than always saying two.
Only the existing mutating starts are withheld. Preserve every independent
workspace, planning, budget, source, approval and physical-identity gate.

Retain the read lifetime's five-second polling, ten-second actual transport
abort, coalescing, no overlapping reads, three-failure polling stop and stale
session/remount guards. Add an explicit limit-change operation to that owned
lifetime; do not introduce another capacity reader or browser persistence.
Use a real AbortSignal for the PUT, a ten-second bound, at most one in-flight
change and no automatic mutation retry. Session loss/unmount aborts transport
and invalidates its callbacks; project selection does not own it.

From change submission until a subsequent current GET settles, mask the old
capacity reading for mutating-start purposes. An already pending pre-change
GET, late PUT response or stale lifetime callback must not restore an earlier
Available. Serialize/coalesce reads around the write and require a new read
after the actual write transport settles. On timeout/failure the mutation
may already have committed: show unconfirmed feedback and refresh, never
optimistically revert, report success, resubmit or infer rollback. Failed or
invalid confirmation continues withholding until a valid current read.
ReadOnly and metadata controls remain independent during this confirmation.

## Validation and boundaries

Regressions first at the cheapest meaningful boundary. Real file-backed SQLite
proves default/upgrade/reopen/down, revision constraints, history and no-op,
fresh configuration despite tracked state, refusals without side effects and
both orderings of a limit-update/claim race across independent connections.
Exercise limit one across every mutating entrance, Agent/verification/commit
dispatch self-exclusion, feed filtering and unchanged ReadOnly/result paths;
retain limit-two regressions. API tests prove the full protected request/wire
matrix and representative mediator/persistence/race wiring, without copying
every pure policy permutation into API.

The shared policy change needs full Application and API projects once on stable
sources, all persistence/migration classes, focused new Domain tests, full
architecture, whole frontend tests/lint/build/strict e2e tsc, generated-client
reproducibility and serial collaboration/planning-sequence journeys. Extend
collaboration to change one/two and observe the shared disclosure; no new broad
journey or provider campaign. Keep API setup proportional to the tested seam.

Three compiling controls: replace the persisted final capacity comparison with
two; bypass the below-recorded-owners update refusal; admit a pre-change
Available response across the UI's change/confirmation barrier. Named
regressions must kill each. Run serially, preserve raw failures, verify restored
hashes, touch restored sources and rebuild before final checks. Source hashes
alone do not prove historical binary identity. Retain earlier controls at their
actual objective/source phases unless changed inputs require another run.

No new execution grant, automatic stage advance, queue/fairness, configured
value above two, per-run/provider limit, new lease/cancellation/retry/release,
dependency, provider experiment, publication mechanism, P9 or AI Memory work.
The configuration table is the only new persistence capability. Preserve all
earlier evidence losses, qualified validation and accepted nonblocking limits.
Increments 4/5/6 remain incomplete and ADR-0050 stays Proposed. Return the
implementation unstaged, uncommitted and unpushed for Codex review.
