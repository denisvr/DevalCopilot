# ADR-0037: Inspect the recorded Agent-attempt attribution of a historical collaboration message

Status: Accepted

## Context

[ADR-0003](0003-use-a-durable-sqlite-event-journal.md), [ADR-0004](0004-use-a-structured-agent-collaboration-protocol.md) and
[ADR-0009](0009-separate-agent-roles-effects-and-provider-assignments.md) make each provider-observed collaboration message the output of one
specific Agent attempt, and [ADR-0035](0035-browse-recorded-collaboration-messages-of-a-historical-run.md) and
[ADR-0036](0036-inspect-the-recorded-reply-parent-of-a-historical-collaboration-message.md) let an owner read an older run's messages and their
reply parents. The history still cannot say which attempt produced a message. The existing evidence reader
(`GetCollaborationMessageEvidence`) loads whole message and attempt entities and more evidence than this view needs, and `AgentAttemptRead`
materializes a full attempt; neither is a bounded projection for a historical surface, and an unknown stored enum or an oversized stored value
can make materialization fail or read far more than the view discloses. `CollaborationMessage.AttemptId` is the durable reference; the attempt
identity and assignment snapshot rules already exist (`AgentAttemptIdentity.IsCoherent`, `Attempt.GetAssignmentSnapshot`). No persistence is needed.
This closes one attribution access gap, not complete replay or any increment.

## Decision

**One protected, read-only inspection of one message's own attempt reference.**
`GET /api/runs/{runId}/collaboration-history/{messageId}/attempt` (`GetCollaborationHistoryAttemptQuery` through `IApplicationMediator`, one MVC
endpoint, bodyless, protected like every run operation, API-owned response types, the generated client regenerated reproducibly) answers the
attribution of exactly that message. An unknown run and a missing or foreign source are the same fixed 404 (`collaboration_attempt.not_found`); a
source with an empty stored identity or a nonpositive stored sequence is one fixed 409 (`collaboration_attempt.unavailable`). Both are the shared
Problem Details contract and repeat no value. Other faults, including a database error, propagate and are never masked as `Unavailable`. The
ADR-0035 page, the ADR-0036 reply route, the timeline and the evidence readers are unchanged in behavior.

**What is read.** Three untracked, indexed selects and no raw SQL in Application: the run's existence; the source by exact run and message
identity (sequence, identity, SQL-classified version match, type, provenance, actor kind, role and provider, and `AttemptId`; never the summary or
content JSON); and, only for a provider-observed source with a recognized Agent actor and a nonempty `AttemptId`, at most one attempt selected by
that identity within the same run as a bounded scalar projection. No latest, same-role, same-number or neighboring attempt is ever substituted, and no
repair, input or reply chain is followed.

**What it returns.** The run and message identities, the source's exact sequence as a canonical decimal string, a closed status and a nullable
attempt header:

- `NoAgentAttempt`: the source has a known version-1.0 type and a recognized provenance that is not `ProviderObserved` (`Simulated`,
  `HostConstructed`, `HumanSubmitted`); its reference is read with the source metadata, but no attempt row is resolved. It means only that this contract attributes no Agent attempt to the
  message (a simulated message can still have a simulated attempt); it is not absence of every attempt in the run.
- `Unavailable`: everything that cannot be shown as a coherent attribution, with no target: an unknown version, type or provenance; a
  provider-observed message without a recognized Agent actor (known role and provider), without a nonempty reference, or whose attempt is missing,
  foreign to the run, not an Agent attempt, has a nonpositive number, belongs to a different role or provider than the actor, or fails the identity
  rule below.
- `Available`: the stored attempt exists as above, its role and provider are exactly the message's actor, its number is positive, and
  `AgentAttemptIdentity.IsCoherent` accepts it.

The header contains only the attempt identity and number, the role, provider and response contract, the recorded status and the recorded claimed,
dispatched and completed UTC times (the last two nullable). It never carries an outcome, model, effort or profile text, a provider session, a
checkpoint, raw JSON, a prompt, an argument, a path, an artifact, token or account data or an input reference, and it describes recorded attribution,
not current execution, an approval, provider capability or reliability, or the attempt's sealed evidence.

Malformed identity-column text surfaces through the established fault pipeline, as in the prior readers; it is not reclassified as `Unavailable`.

**The identity rule is unchanged and shared.** Two narrow extractions let a bounded scalar reader apply the existing rules without materializing an
`Attempt`, with no change to any write, claim or dispatch rule:
`AgentAssignmentSnapshotPolicy` (Domain) holds the assignment read-validation that `Attempt.GetAssignmentSnapshot` previously held inline (the
128-character nonblank identifier rule, a defined provider, the exact turn-limit reading, the adapter contract version and an absent-or-defined
permission profile), and `Attempt.GetAssignmentSnapshot` now delegates to it; `AgentAttemptIdentity` gains a scalar `IsCoherent` overload that its
`Attempt` overload now delegates to. Equivalence is proved against an independent restatement of the original rules over every single-factor
variation and a deterministic 20,000-combination sample for each, and every existing caller keeps its behavior.

**Bounded, exact storage reads.** Every stored enum (kind, status, role, provider, contract, permission profile; the message's version, type,
provenance and actor) is classified by the database and never materialized, so an unknown string is `Unavailable`, never an exception. Every
assignment-validation string (requested and observed model, requested and observed effort, adapter contract version) is selected only under a
`length(CAST(column AS BLOB))` guard of three bytes per admitted character, because `length()` of TEXT stops at an embedded NUL, and a flag
distinguishes rejected oversized storage from a legitimate null, so it is never read as absent; a value within the guard but over 128 characters is
still rejected by the exact rule in memory. The requested-max-turns column keeps its exact storage-class mapping (INTEGER versus TEXT, REAL and BLOB,
which SQLite's INTEGER affinity decides on write) and is selected only under a small byte guard, with the same flag; a malformed or oversized storage
is never coerced into a valid or an absent request. If Entity Framework could not have preserved those semantics inside the bounded projection this
decision would have stopped rather than weaken the identity rule or add raw SQL.

**Cockpit.** Each message in the Recorded collaboration region gains one explicit "Inspect recorded attempt" action beside "Inspect recorded
reply", with one attempt inspector, "Close recorded attempt" and, after a failed read, a same-source "Try the recorded attempt again". The two
inspectors are independent. Nothing is requested on render or for rows, polled, retried automatically or cached; the loaded pages and their cursor are
untouched. The inspector shows a fixed note that this is recorded attribution only, then `NoAgentAttempt` and `Unavailable` as distinct fixed statements
that name no target, or the header with fixed labels (attempt, attempt number, role, provider, response contract, recorded status, claimed, dispatched,
completed; "Not recorded" for an absent time) as plain text, with no mutation, evidence or live control.

**Ownership and validation.** The inspector belongs to the committed run's open history lifetime and to each source selection, exactly as the reply
inspector does: closing, reopening, reloading, another run or unmounting ends it, a different source and closing then inspecting the same one are new
lifetimes, a read for one can never settle another, replacement derives its first frame in the same render, and there is no key, remount or reset
effect. `close` is bound to the inspector that created it and its selection update is guarded by the selection serial, so a retained callback cannot
close its successor even when the replacement is queued in the same React batch. A decoder accepts an answer only whole and only when coherent for
exactly the observed source: the answered run and message, the observed exact sequence text, a closed status with a matching shape, `NoAgentAttempt`
only for a recognized non-provider-observed source, `Available` only for a recognized provider-observed source with a recognized Agent actor and a
header whose identity, positive bounded number, supported role/provider pair, contract-to-role agreement, status and timestamps are valid and whose
role and provider are exactly that actor's. Anything else is one whole-inspection error and none of it is shown.

## Consequences

- An owner can see which recorded Agent attempt produced a historical provider-observed message, including one from a run whose attempt history is
  long, in minimal, bounded and exact form, without a transcript, artifact, raw JSON or mutation.
- A missing, foreign, mismatched or unsupported attribution is reported as unavailable without naming a target. Oversized assignment-validation
  storage is rejected without being read in full; malformed identity-column materialization keeps the fault pipeline described above.
- The assignment read rule and the identity rule now have one implementation each, reachable from entity and scalar readers; changing either changes
  every reader deliberately.
- The cockpit restates the role/provider pairs and the contract-to-role table; admitting a new one requires the Domain and the cockpit to learn it
  together, and until then the answer is `Unavailable` (host) or refused (cockpit).
- The contract is additive. It is not replay, a thread view, evidence or artifact inspection, an approval, a statement of provider success, an export
  or a remote action, and it does not complete the durable-history increment or the MVP. Message evidence, attempt history, repair and input lineage
  and generic history infrastructure remain separate decisions.
