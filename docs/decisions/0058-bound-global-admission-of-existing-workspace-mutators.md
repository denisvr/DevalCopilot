# ADR-0058: Bound global admission of existing workspace mutators

Status: Accepted

## Context

ADR-0006 requires bounded concurrency across repositories. WORKSPACE-EXCL-01
implements ADR-0057's reciprocal workspace exclusion, but distinct workspaces
still admit independent mutating operations without a global count. At
`b3d53bf`, a planner reproduction uses the production verification claim on
three distinct projects/workspaces in one file-backed SQLite database: all
three claims persist. No verification process, provider or Git command runs;
this proves recorded admission, not concurrent physical writes.

An automatic local delivery coordinator would require a new execution grant;
ADR-0055's grant ends at Accepted planning or Human escalation. A durable queue
also needs a pinned pending-request, expiry, revalidation and recovery contract.
Neither authority is inferred here. This slice is the largest common admission
prerequisite that can narrow existing explicit operations without adding either.

## Decision

### One conservative global admission ceiling

GLOBAL-ADMIT-01 adds one shared Application policy under
`Features/Runs/Policies/ExecutionCapacity`, accessed directly through the
existing DbContext. Its intentionally shared consumers are existing mutating
Agent claims/dispatch/feed paths, verification claim/dispatch/feed paths and
local-commit admission/execution. Preserve their separate contracts and all
ADR-0057 workspace, physical identity, lineage, mode, budget and approval gates.

The ceiling is **two recorded potentially mutating execution owners per
authoritative database**, reserved from claim/admission, including before
dispatch. It is not two providers, two tasks in a supervisor, two open run
lifecycles or two prepared workspaces. Derive it from existing durable facts:

- unresolved Agent attempts whose coherent response contract/role has
  `AgentEffectKind.WorkspaceMutating` in Domain's `AgentAttemptContract`;
  ImplementationReport and ReviewCorrection currently qualify, including
  authorized implementation and diagnosis-origin correction;
- unresolved verification executions, since configured commands may mutate;
- ADR-0029's existing local-commit reservation, tied to its one coherent open
  operation and Committing workspace, or NeedsAttention with that open operation.

Use database-side exclusion of recognized terminal sets. Unknown possibly
active status is never treated as finished. Never materialize unknown enum
values to guess an effect. A missing/unrecognized/incoherent active Agent
contract-role pair is Unavailable, rather than implicitly ReadOnly. Classify
known effects from Domain's table, never provider name. A recognized ReadOnly
Agent attempt consumes no capacity; neither do simulated/standalone Process
attempts, Human approval waits, idle prepared workspaces or observation rows.

Count each coherent owner once, not once per join or lease row. Two owner
families or duplicate owners on one workspace are untrustworthy for this
reading, not one convenient coalesced slot. Missing/foreign run-project-workspace
associations, malformed local-commit reservation/operation association, orphan
open commits and unreadable ownership fail closed as Unavailable. Do not drop
unresolved work merely because its run ended, its lease changed or its workspace
is no longer Ready. Those facts do not prove the owner stopped. Terminal
history is retained and excluded by its recognized status only; existing
workspace/recovery gates still decide whether anything may execute there.

At zero or one coherent owners, a new mutating claim may pass this additional
gate. At two or more it is refused, with a fixed Conflict code
`execution_capacity.limit_reached`; an unsafe reading uses
`execution_capacity.unavailable`. Neither result grants execution. Existing
over-capacity records remain intact: do not choose winners, cancel, terminalize,
rewrite history or release ownership to make the count fit.

This is an additive conservative admission prerequisite to ADR-0006, not its
complete scheduler. Configurable reduction to one, durable queue/fairness,
executor leases and process-aware recovery remain undelivered. No configuration
input, table, migration, reservation counter or slot-release command is added.
It narrowly extends ADR-0057/ADR-0029's prior unrelated-repository independence:
unrelated mutators share this ceiling; eligible ReadOnly work remains independent
of global mutating capacity, subject to every existing gate.

### Fresh atomic admission and exact self-exclusion

Check before external preparation and again inside the existing short global
SQLite write boundary immediately before each mutating claim/admission commits.
Every mutating entrance participates: ordinary/authorized Implementation,
ReviewCorrection (including Human-authorized rounds), DiagnosisCorrection,
verification and local commit. Do not make a workspace-local lock or a cached
count stand in for database-wide serialization. No Git, provider work or
manifest preparation moves inside a transaction.

A refusal consumes no attempt/execution/operation identity, budget, grant,
repair slot, number, membership, artifact row or event. Clean up only the losing
operation's definitely unreferenced prepared files. Preserve ambiguous-save
readback, retention and cleanup on failed/cancelled final reads.

Agent and verification dispatch and local-commit single-use execution gates
decide again under their write lock. Ignore only their exact coherent owner:
operation kind/table and immutable ID, with its associations validated before
exclusion. Never ignore another row because its run/workspace/ID looks similar.
A local commit excludes its reservation only when that exact operation owns it;
an orphan or ambiguous reservation is never ignored. An admitted owner may
execute beside one other owner; if two others already exist, it cannot dispatch.
ReadOnly dispatch remains subject to its unchanged gates and consumes no global
capacity. Verification's pre-dispatch SourceChanged decision retains shared
dispatch authority. Advisory feeds avoid repeated external preparation for known
capacity refusals but never replace the final check.

Known result/outcome recording, source evidence, approvals, observations and
existing startup recovery retain their authority. Never add this gate to saving
an already known result, or use heartbeat/deadline expiry as capacity release.
No new polling retry, pending request or automatic successor is introduced.

### Protected disclosure and precise UI withholding

Add authenticated bodyless/queryless `GET /api/execution-capacity` through one
MVC endpoint and the mediator. Return exactly four API-owned fields:

| status | limit | recordedOwners | reasonCode |
| --- | --- | --- | --- |
| Available | 2 | integer 0 or 1 | null |
| AtCapacity | 2 | integer 2 through Int32.MaxValue | execution_capacity_limit_reached |
| Unavailable | 2 | null | execution_capacity_unavailable |

No project/owner IDs, paths, provider details, payloads, timestamps or request
queue are returned. Unsafe/unrecognized ownership gives the closed Unavailable
answer, not a fabricated count. Expected database faults use the existing
Problem Details boundary; they do not become Available. Counts disclose only
this policy's recorded facts, not simultaneous process activity, liveness,
remaining provider allowance or a frozen cross-family snapshot.

App owns one authenticated host-session lifetime for this read, independent of
project selection. It refreshes on mount, explicit refresh, coalesced durable
run/evidence hints and every five seconds; aborts actual transport after ten
seconds and on unmount; never overlaps reads or accumulates tasks; after three
consecutive failures stops polling until explicit refresh, remount or a hint.
Drop stale responses/callbacks across unmount/remount. A failed, timed-out or
invalid read masks an earlier Available; a pending reread may retain its last
validated answer as a hint. No browser persistence/global cross-session cache.

Validate the entire closed wire tuple. Both cockpit regions require the state;
missing/null/unrecognized/invalid/failed state withholds new mutating starts.
Show one fixed English guidance per affected region with a refresh-only control.
An Available answer enables nothing by itself. Withhold implementation,
review/diagnosis-origin corrections, verification Run and local-commit start,
including their relevant callback fences; do not apply this gate to planning,
critique, resolution, code review or verification diagnosis. A panel containing
both diagnosis and correction must gate those actions separately. Do not gate
metadata-only authorizations/approvals, inspection, evidence refresh, Stop after
current step, abandonment or recording a known result. Existing independent
workspace/planning/budget fences continue to apply to all their own consumers.

## Evidence and boundaries

Use real file-backed SQLite and independent connections to prove no third
owner at simultaneous final seams across Agent/verification/commit families,
including all mutation entrances and exact self-exclusion; prove readonly
progress while capacity is full, unrelated workspaces, over-capacity legacy
facts, malformed/unknown ownership, stale tracked state, terminal transitions,
no losing consumption and result recording at capacity. Prove MVC protection,
body/query rejection, closed wire, actual cancellation and both UI action sets.

The shared admission change requires full Application/API projects once on
stable sources, full architecture, whole frontend tests/lint/build/strict e2e
tsc, generated-client reproducibility and serial collaboration/planning journeys.
Two compiling negative controls omit (1) the mutating Agent final capacity gate
and (2) the verification final capacity gate; independent-connection races must
kill them. Touch restored sources and verify hashes before rebuilding; source
hash equality alone proves no restored binary identity. No broad campaign.

No dependency, provider experiment, generic pipeline/supervisor, new execution
grant, queue, lease, cancellation, retry, takeover, release, remote publication,
P9, packaged-shell qualification or AI Memory work is authorized. Increments
4/5/6 remain incomplete. ADR-0050 remains Proposed. All earlier evidence losses,
qualified validation and accepted nonblocking limitations stay recorded.
