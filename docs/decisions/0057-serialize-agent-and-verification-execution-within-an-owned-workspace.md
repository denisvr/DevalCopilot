# ADR-0057: Serialize Agent and verification execution within an owned workspace

Status: Accepted

## Context

ADR-0006 and ADR-0008 require one writer per owned worktree. Agent claims
serialize Running attempts per run, while verification claims serialize
Running verification executions per workspace. These are different sets.
An active repository lease and a Ready workspace do not distinguish them.

Independent inspection and a file-backed SQLite reproduction at `f95e337`
show that the production verification claim and dispatch handlers both admit
a verification beside a seeded, already dispatched, Running Implementer
attempt on the exact same workspace. The reproduction starts no provider or
verification process; it demonstrates the missing authority check, not two
observed physical writes. The reciprocal Agent claim and dispatch paths also
lack a verification-execution check. Existing checkpoint comparisons can
detect some drift later but cannot provide mutual exclusion before dispatch.

Closing this existing ownership gap is a prerequisite to future local
coordination. It narrows admission under existing explicit authorizations;
it grants neither automatic stage advancement nor broader mutation authority.

## Decision

### One existing execution owner per workspace

WORKSPACE-EXCL-01 makes workspace execution exclusion one shared Application
policy under Projects/Policies/WorkspaceExecutions. Its consumers intentionally
share this rule: all seven Agent contracts and their manual repairs, the
existing coordinated planning claims, verification execution, and the
existing local-commit reservation/execution boundary. Keep their separate
eligibility, effects, budgets, manifests, dispatch and result contracts.

Derive ownership from existing durable rows, not AgentSupervision:

- a Running Agent attempt associated with the workspace reserves execution
  from its claim, including before dispatch and after dispatch while unresolved;
- a Running verification execution reserves that same workspace from its claim,
  regardless of recipe, dispatch marker or later recipe edits;
- an admitted nonterminal local-commit operation and its Committing workspace
  retain ADR-0029's existing exclusive reservation.

Agent readers are also excluded from verification: configured verification
commands may mutate the source they read. This is conservative serialization
of existing execution within one workspace, not a new effect classification.
Two Agent attempts of different runs must not share that execution slot merely
because each run separately has no other attempt. A terminal owner no longer
blocks by Running status, but existing workspace NeedsAttention, lease,
reconciliation and approval gates remain authoritative; terminal status is
never a new proof of process death or permission to release ownership.

Use only fresh, untracked ownership and relationship facts. A missing or
incoherent ownership association, an unrecognized potentially active state,
or conflicting owners fails closed. Include project-associated active Agent
facts whose missing or foreign workspace association prevents a trustworthy
idle judgment. Do not infer a winner from timestamps, identifiers, a heartbeat,
an expired observation deadline or the selected UI run. Known terminal history
is not reinterpreted or rewritten.

This policy adds no ownership table, renewable lease, migration, slot counter,
queue, pending request or automatic release. Preserve the existing physical
repository lease and its alias checks. Distinct owned workspaces in unrelated
repositories remain independent. The global two-mutating-run scheduler remains
undelivered; this slice does not claim that limit is implemented.

### Claims and dispatch decide atomically

Apply the fence before expensive external preparation, then again after taking
the existing short SQLite write boundary and immediately before committing
each claim. Every affected claim participates in this serialization: checking
only one family, one direction, one provider or an eligibility feed is
insufficient. Existing single-save claim paths may be adapted to a short
explicit final transaction when necessary; no Git, provider observation or
artifact preparation moves into that transaction. Preserve atomic budget,
artifact metadata, input membership, repair and planning-step associations.

An early or final conflict records no competing attempt/execution, consumes
no claim number, budget or otherwise unused Human authorization, and leaves
no orphan artifact. Preserve the planning sequence's existing reserved-ticket,
refused-preparation and atomic step-link rules. Preserve uncertain-commit readback and artifact
retention rules; a failed response never proves the claim was absent.
Use stable feature-owned Conflict errors (HTTP 409 at existing MVC commands),
with fixed safe descriptions. A busy refusal is not workspace loss, provider
failure or a new terminal Agent outcome.

The single-use Agent and verification dispatch gates re-read the same policy
under their write lock. Exclude only the exact operation kind and ID being
dispatched, never every operation of its run or contract or an ID in another
table. If another owner appeared, or
pre-existing records conflict, commit no marker and invoke no adapter. The
eligibility feeds and early supervisor decisions should avoid proposing known
conflicts, but remain advisory. Verification's pre-dispatch SourceChanged
decision keeps sharing the dispatch authority check. Preserve the existing
local-commit admission and execution gates and prove reciprocal races with
them: the exact commit operation may execute its own Committing reservation,
but cannot ignore any other owner or weaken its existing approval gates.

Do not terminalize an attempt, cancel a process, retry a claim, clear a marker,
change an outcome or release a lease to resolve a conflict. Do not add this
fence to recording an already known result or observation: those paths must
still persist their original outcomes under their unchanged contracts. Existing
startup reconciliation remains the only existing recovery authority.

### Protected disclosure and action hints

Add one protected, bodyless, queryless MVC GET:
`/api/projects/{projectId}/workspace-execution-status`.
It reads the project's latest workspace and the same durable ownership policy,
never Git, a provider, a process table or AgentSupervision. An unknown project
returns the existing non-disclosing 404 form.

Its bounded API-owned response has exactly `projectId`, `workspaceId`, `status`
and `reasonCode`. No path, payload, transcript, owner collection or inferred
liveness is disclosed. The closed tuples are:

| Status | Workspace | Reason code |
|---|---|---|
| NoWorkspace | null | workspace_not_prepared |
| Idle | exact latest workspace ID | null |
| AgentAttemptActive | exact latest workspace ID | agent_attempt_active |
| VerificationActive | exact latest workspace ID | verification_active |
| CommitReserved | exact latest workspace ID | local_commit_reserved |
| Conflicted | exact latest workspace ID | conflicting_execution_owners |
| Unavailable | exact known latest ID, otherwise null | workspace_execution_unavailable |

Idle requires a coherent Ready workspace, active lease and no competing
execution owner. It states only what this read found; it is never permission,
provider readiness, current source attestation or satisfaction of another gate.
Conflicted exposes incompatible ownership without choosing a winner.
Unavailable covers ownership that cannot be judged safely. Preserve local
commit semantics when deciding whether a reservation is coherent.

Use an operation-owned whole-answer decoder; reject unknown members, missing
explicit nulls, malformed IDs, foreign project answers and invalid tuples.
Keep authentication, generated operation and Problem Details behavior intact.

The selected project's owned UI lifetime reads this status and uses it as an
additional withholding hint for starting an Agent, repair, planning sequence,
verification or local commit. Existing action eligibility is still required;
Idle enables nothing by itself. Both CandidateWorkspacePanel and RunCockpitView
must receive the current project's coherent status. Unknown, failed, invalid,
replaced or unavailable readings withhold execution-start actions and show
fixed inspection/refresh guidance. Historical evidence, Refresh, Stop after
current step, existing result recording and metadata-only abandonment retain
their existing controls and must not be accidentally disabled by this hint.

Refresh on project/workspace selection, explicit evidence refresh and relevant
durable hints; use a bounded five-second refresh while the selected project is
mounted because verification transitions need not emit run events. Coalesce
reads, abort the actual request after ten seconds and on selection/unmount,
stop after three consecutive failures until explicit refresh/remount/hint,
and fence stale A-B-A/remount responses. A failed or timed-out read masks a
prior Idle. Store nothing in browser persistence. This adds no global client
cache, shared ownership-hook redesign or transport/authentication change.

### Boundaries and existing authority

This strengthens ADR-0006/0008's existing workspace exclusion. It changes no
accepted role, provider permission, explicit per-stage request, source/result
classification, approval, budget ceiling, planning sequence or local commit
grant. ADR-0055 remains planning-only; ADR-0056 observations remain ineligible
as authority. No automatic implementation, verification, diagnosis, correction,
review, commit or remote publication is authorized.

Standalone legacy Process attempts have no equivalent owned-workspace identity
contract; do not infer it from arbitrary working-directory text or expand their
execution modes. Simulation, arbitrary external writers and process-liveness
proof are outside this slice. A separate identity/recovery contract is required
to include those lifecycles. This is not full scheduler, guardian or Increment
5 completion, and changes no Increment 4/6 qualification requirement.

## Required evidence

First prove the reproduced verification-beside-Implementer gap and the reverse
direction. Then prove claim and final-dispatch refusals for all seven Agent
contracts, manual repairs and coordinated claims, plus local-commit
interoperability. Use real file-backed SQLite, independent connections and
controlled preparation barriers: racing requests must leave only one owner,
with the losing side's budgets, counters, artifacts and authorizations intact.
Prove same-workspace/different-run exclusion, unrelated-repository progress,
pre-existing conflicting rows, tracked-state staleness, terminal transitions,
and result persistence after a conflict is discovered. No provider is needed.

Prove the real protected MVC/wire boundary, owned project selection, refresh,
transport cancellation and consistent withholding in the cockpit and workspace
panel. Controllably held hosted compositions and UI tests prove verification
withheld during implementation and Agent requests withheld during verification.
Extend the authenticated deterministic collaboration journey to bind the new
disclosure to its selected workspace and recover Idle after actual completion,
retaining its invocation counts, explicit Human approval and local commit.
The planning journey retains its original safety assertions. Do not require a
browser to catch every short-lived fixture phase or change a shared fixture
invocation contract merely to lengthen it.

Two compiling negative controls target omission of the Agent-side final fence
and omission of the verification-side final fence; focused race tests must
fail, and restoration is hash-verified. Shared admission changes require the
complete Application and API test projects after focused development checks,
full architecture, frontend checks and the affected serial journeys. Retain
unexplained failures and prior evidence limitations without causal invention.

No real-provider experiment, dependency, fixture cache, broader mutation,
publication, P9, native-shell qualification or AI Memory authority is added.
