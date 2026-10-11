# ADR-0035: Browse the recorded collaboration messages of a historical run

Status: Accepted

## Context

[ADR-0003](0003-use-a-durable-sqlite-event-journal.md) and [ADR-0004](0004-use-a-structured-agent-collaboration-protocol.md) make the
collaboration between agents a durable, bounded, structured record, and the delivery plan requires that conversation to be reachable after
a restart. [ADR-0033](0033-browse-project-run-history-and-locate-prior-delivery-receipts.md) and
[ADR-0034](0034-browse-recorded-event-headers-of-a-historical-run.md) let an owner reach an older run, its delivery receipt and its
event headers, but the selected older run still shows none of its recorded proposals, challenges, decisions or reviews. The existing
reader does not fit: `GET /api/runs/{runId}/collaboration-timeline` returns only the latest 100 messages, is driven by live events,
returns numeric sequences and the raw `StructuredContentJson`, and materializes stored enums and `ParticipantIdentity` values that an
unknown stored value can make throw. `CollaborationMessage.Sequence` is a database-assigned signed 64-bit key that is global across
runs, so one run's sequences have gaps and can exceed what a browser number represents exactly. SQLite `HasMaxLength` does not enforce
the Domain bounds on stored rows, so the bounds must be applied by the read itself. The record still has no reply or attempt resolution
in this view; full linked replay remains open.

## Decision

**One protected, read-only page of one run's collaboration messages.** `GET /api/runs/{runId}/collaboration-history`
(`GetCollaborationHistoryQuery` through `IApplicationMediator`, one MVC endpoint, bodyless, protected like every run operation) answers
one page of that run's persisted messages in descending `Sequence`. An unknown run is a safe 404 (`runs.not_found`); an existing run
with no message is a successful empty page. The transport types are API-owned records and the generated TypeScript client is
regenerated reproducibly from the OpenAPI document. The timeline route, the event-history route, the live notifier and their types are
neither changed nor reused.

**Cursor paging over exact text, never clamping.** `limit` defaults to 10 and accepts 1 to 20. `beforeSequence` is an optional
exclusive cursor that is a string, because a sequence can exceed 2^53: absent or blank means absent, otherwise it must be canonical
ASCII decimal digits, positive, without a sign, padding, leading zero, fraction or exponent, and at most 9223372036854775807, parsed as
exact text and never through a floating-point value. Feature-local binders add no framework model-state error, so a malformed,
overflowing, signed, padded or repeated value is refused by the query's validator through the shared Problem Details contract
(`validation.invalid_limit` or `validation.invalid_cursor`, a fixed detail, a JSON pointer, the trace identifier) and never repeats the
rejected value. The operation declares `ApiProblemDetails` for 400, 404 and 409. The page reads only this run's rows, untracked,
through the existing `(RunId, Sequence)` index, newest first, with an exclusive `Sequence < beforeSequence` predicate and
`Take(limit + 1)`: the extra row gives an exact `HasMore` without a count, and `NextBeforeSequence` is the last admitted sequence only
when older rows remain. There is no offset, count, aggregate, full-entity load or draining of pages. Messages appended between two reads
can neither repeat nor skip an older row; pages are not an atomic snapshot and claim no total. Sequence gaps are valid and timestamps
never define order.

**One fixed refusal for an unusable row.** If a selected stored message (the extra row included) has an empty identity or a nonpositive
sequence, the whole read is the single fixed 409 `collaboration_history.unavailable`; a partial page is never returned. Other faults,
including a database error, keep the established pipeline and are never masked as unavailable.

**What an entry contains.** The run and message identities, the exact sequence string, the UTC time, the protocol version, the message
type and provenance, the actor and the recipient (kind, nullable role, nullable provider), a content status, a nullable summary and a
list of name/value fields. The version is admitted only as exactly `1.0`; the type and provenance only as the exact members of their
closed enums; anything else keeps its row and is disclosed only as the fixed `Unrecognized`, never exposed. The enum columns of both
participants are classified by the database without materializing a stored enum or constructing a `ParticipantIdentity`: Orchestrator
and Human carry neither role nor provider, an Agent carries a known provider and an optional role (a valid Agent without a recorded role
is preserved as recorded), and `None`, an unknown kind, role or provider, or an inconsistent combination is an unrecognized
participant. Nothing is inferred: no attempt, reply target, artifact, current state, approval or provider success is looked up or
implied, and no `AttemptId` or reply field is returned.

**Content is admitted only through the existing policy.** The summary and the content JSON are selected under database-enforced byte
guards (`length(CAST(column AS BLOB))` at most three bytes for each of the 600 and 5000 admitted characters, because `length()` of TEXT
stops at an embedded NUL and would not bound the stored value), so materialization is bounded even with embedded NUL. A value within the
byte caps can still exceed the character limits; those exact limits stay the Domain policy's and are checked again in memory before parsing.
The cockpit decoder also requires the host's envelope
coherence for Available content: distinct participants, and provider-observed from an Agent with a recorded role, host-constructed from an
Orchestrator, human-submitted from a Human. Content is `Available` only when the version, type and provenance are recognized, the two
participants are recognized, distinct
and consistent with the provenance, the summary passes `CollaborationMessageContentPolicy.IsSafeSummary` and the JSON passes the
existing `CollaborationMessageContentPolicy.Validate` for its type (a closed field set, each field once, a bounded safe string value).
Only that policy's own expected failure (an `ArgumentException`) makes a row unavailable. An unavailable row keeps its header, has a
null summary and no field, and never changes a healthy neighbor; nothing is truncated, repaired or returned raw, and an unknown field
name, malformed JSON or secret text never leaves the host. The fields are returned in the document's own order; the cockpit shows them
in the closed schema's order with fixed labels.

**Read-only by construction.** The query reads untracked rows, writes nothing and invokes no Git, filesystem, provider, process,
notifier or mutation action. It needs no migration, persistence or Domain change and changes no admission, recovery, approval, digest or
receipt decision.

**Cockpit.** The selected historical run detail adds a **Recorded collaboration** region beside the receipt and the recorded events,
collapsed by default. Opening it requests one page of five messages (the host default is ten; the delivered run of the browser journey
records nine messages, so five is the smallest round bound that exercises explicit older paging through rendered controls without
seeding rows); "Load older messages", "Try the older messages again" (the same cursor) and "Reload newest messages" are explicit, and
nothing is polled, timed, retried automatically or stored in the browser. Each message shows the exact sequence, a fixed label for the
type, the version, the provenance, the UTC time, the sender and recipient as recorded, the summary and the labeled fields, or an explicit
"Content unavailable" state, all as plain text. A fixed note says these are recorded facts, not live status, not a current approval and
not proof that any provider succeeded. The region has no selection, reply, evidence or mutation control and mounts no live hook.

**Ownership and validation.** Reads belong to the shared owned lifetimes used by the event-history region: the committed run
(collapsed for every run; another run, a return to an earlier one (A to B to A), closing the selection or unmounting ends it) and each
open or reload. An older or replaced read can neither overwrite nor settle a newer one and the callbacks of an ended lifetime start
nothing. A decoder accepts a page only whole and only when it is coherent for exactly its request and the retained rows: the answered
run, no more entries than requested, nonempty unique identities, canonical 64-bit sequence texts compared as exact integers in strictly
descending order and below the cursor, valid instants, only closed disclosures with coherent participants, content that is either bare
`Unavailable` or complete against the closed schema of its type within the host's bounds, and a continuation that is a full page whose
cursor is exactly its last sequence. A well-formed `Unavailable` row is kept; any other contradiction is a whole-page error.

## Consequences

- An owner can inspect the recorded conversation of an older run, in bounded pages and exact order, also after a reload or restart,
  without mutation, live work, raw JSON or a transcript.
- The content is as good as the recorded rows: an unrecognized, malformed, unsafe or oversized message is disclosed as unavailable, not
  repaired, and a damaged row never hides its neighbors or the whole conversation.
- Admitting a new message type, field or protocol version requires the closed tables of the host policy and of the cockpit to learn it
  deliberately; until then the row is unavailable (host) or the page is refused (cockpit) rather than shown unexplained.
- The contract is additive. It is not a replay, a reply or evidence viewer, an approval, a statement of provider success, an export,
  an autonomous coordinator or a remote publication, and it does not complete the durable-history increment or the MVP.
- Full linked replay (attempt and reply resolution), payload and artifact inspection and generic history infrastructure remain separate
  decisions.
