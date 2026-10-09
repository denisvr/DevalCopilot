# ADR-0032: Show a recorded local-delivery receipt from pinned evidence

Status: Accepted

## Context

[ADR-0029](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md) delivers one explicit, host-executed local
commit and records, in relational rows, exactly what it was admitted against: the checkpoint and its fingerprint, the source execution
report, the CodeReviewer attempt with its actual approval message, the Agent and selected Human checkpoint reviews, and an ordered
authority membership of verification command/execution pairs and Human review rows, each bound by a digest of its own content.
[ADR-0030](0030-approve-the-complete-verification-set-as-one-human-decision.md) lets one Human approval cover every enabled recipe.
The cockpit shows the operation's status, checkpoint number, branch, parent, tree, changed-path count and commit SHA, but not which
recorded checks and decisions supported that delivery. An owner would have to reconstruct them from several current-state panels,
which describe the latest records, not the ones this commit was admitted against. This is a read problem: every fact is already
durable and immutable once recorded.

## Decision

**One protected, read-only historical receipt.** `GET /api/runs/{runId}/local-delivery-receipt` (`GetLocalDeliveryReceiptQuery`
through `IApplicationMediator`, one MVC endpoint, bodyless, protected like every run operation) answers one closed state for an
existing run: `NotRecorded` (no operation), `NotCompleted` (a known, non-completed operation), `Unavailable` (the operation is recorded
as completed but its receipt cannot be reconstructed coherently, or its recorded state is contradictory or unrecognized) or
`Available`. Only `Available` carries a version-1 receipt; every other state carries `null`. An unknown run is a safe 404. The
transport types are API-owned records; no Domain or Application entity is exposed, and the generated TypeScript client is regenerated
reproducibly from the OpenAPI document.

**What a receipt contains.** Only safe historical facts: the run, operation and objective; the local commit SHA, parent, tree, owned
branch and completion time; the checkpoint identity, number, fingerprint and changed-path count; the pinned execution-report message;
the CodeReviewer attempt, its attempt number and its actual approval message; the selected Human review identity and its `Approved`
decision; and the ordered verification members with the recorded command and execution identities, execution numbers, the command
name of the execution's immutable snapshot, `Passed` with exit code 0, and completion times. It carries no executable or workspace
path, argument, author address, commit message, raw message, provider output, artifact, lock receipt or credential, and it offers no
download or export.

**It is reconstructed from pinned rows, never from current state.** Every fact is resolved by the identifier the operation recorded,
never as the latest report, review, checkpoint, enabled recipe or verification execution. The operation, run, project, workspace and
checkpoint must agree on ownership, branch, number, fingerprint and parent, and the pinned run itself must record the completion the
operation records (lifecycle and stage `Completed`, its last advance equal to the operation's completion time; an unrecognized stored
value is simply not a completion). That is historical record consistency: only the pinned run is read, never the latest run, a current
lease, the workspace status or live Git. The recorded counts must be in bounds: 1 to the host maximum of changed paths, and positive
checkpoint, attempt and execution numbers. The report must be a provider-observed Implementer
execution report of a completed Implementer attempt of that workspace whose result is that checkpoint; the CodeReviewer attempt must
have completed with an approved implementation review of that checkpoint and fingerprint, have been launched against exactly that
report, own exactly the recorded approval message (a provider-observed CodeReviewer reply to that report, authored under the same provider as
the attempt, with no finding); a bare
Agent checkpoint review is never the CodeReviewer approval. The implemented plan is not inferred from report replies and historical
root-target reviews are not silently upgraded. The verification membership is the operation's own authority members ordered by their
recorded sequence, 1 to 32 unique, contiguous and zero-based, and it must equal exactly the pinned CodeReviewer's ordered evidence
claim and, as a set, the evidence of both the Agent review and the selected Human review. Each pinned execution must belong to the
recorded project, workspace, checkpoint and command, be `Passed` with a clean exit and coherent dispatch, completion and
completion-fingerprint facts, and match its recorded member digest recomputed from its own immutable command snapshot. The selected
Human decision, its coherence with the checkpoint and its complete evidence must match its recorded digest. Both the Agent review and
the Human review also store their own copy of each execution (execution number, status, outcome, exit code and fingerprint); every
stored copy must equal the pinned execution, because the member digest binds only identities, and a contradiction rejects the receipt
rather than letting the execution row stand in for it. Anything missing,
foreign, duplicated, reordered, over the limit or changed makes the whole receipt `Unavailable`; nothing is truncated, substituted or
defaulted, and no partial receipt exists. The receipt identifies the selected Human decision and the verification set; it is not a
new audit of every historical Human decision and not a new authority snapshot.

**One digest serialization.** The member-digest formats of admission are unchanged. They are extracted, byte for byte, into a pure
`LocalCommitMemberDigests` helper that both the admission-time authority reader and the receipt reader use, with literal digests
pinned in tests. The receipt does not reuse the authority reader's current-eligibility path and changes no admission, execution or
recovery decision.

**Read-only by construction.** The query reads persisted rows untracked. It reads no filesystem, Git, process or provider state,
acquires no lease or lock, saves nothing and appends no event, claim, recovery action or cleanup. It requires no migration, backfill
or persistence change. The stored status and member kind are classified by the database, so an unrecognized stored value can never
be read as a completion and never fails the read. Later recipe edits, disabling or deletion, verification reruns, newer reviews,
checkpoints or runs, and later changes of the workspace or branch neither replace nor invalidate an otherwise intact receipt. The
receipt describes recorded delivery, not current cleanliness, invocation eligibility, remote publication, provider obedience or
verification re-execution; the lease is not required to remain active and no live physical identity is re-proven.

**Cockpit.** A Completed operation shows a **Local delivery receipt** region beside the unchanged operation card, with a fixed
explanation that it is a historical, local-only record and says nothing about the current workspace, whether the checks would pass now
or remote publication. Loading, failed, inconsistent, unavailable and valid states are explicit and none looks like a success. A small
receipt-owned normalizer accepts only a coherent response for exactly the completed operation (run, operation, commit and checkpoint
identities, one of the two states that can describe it, 1 to 32 ordered unique `Passed` members, safe-integer counts and numbers
within the host bounds) and rejects anything else instead of
repairing it. The read belongs to the committed run and operation lifetime: replacements, A-B-A, unmounting, retained callbacks and
overlapping reads can neither fetch for nor display an obsolete owner. The receipt is read once per lifetime; it is never polled,
cached across lifetimes or retried automatically, and only an explicit "Read the receipt again" after a failed read reads again. All
values are rendered as text. A receipt failure neither rewrites the operation's status nor offers any mutation.

## Consequences

- An owner sees, with the delivered commit, the exact recorded checks and decisions that supported it, including after a reload or
  restart, without reconstructing them from current panels.
- The receipt is only as good as the recorded rows: a damaged or inconsistent record yields `Unavailable`, which is a deliberate
  refusal to show a plausible receipt, not a repair.
- The contract is additive. It changes no ADR-0029 authority, reservation, recovery or mixed-decision rule, no ADR-0030 approval rule
  and no stored fact. It is not an export, an audit trail framework, a remote publication, an autonomous coordinator or evidence of
  provider reliability, and it does not complete Increment 4.
- A version-2 receipt, an export or a receipt for failed, interrupted or ambiguous operations would need a new decision.
