# ADR-0034: Browse the recorded event headers of a historical run

Status: Accepted

## Context

[ADR-0003](0003-use-a-durable-sqlite-event-journal.md) keeps every run's events durable, ordered by a monotonic integer sequence, and
expects the UI to be able to show run history. [ADR-0033](0033-browse-project-run-history-and-locate-prior-delivery-receipts.md) lets an
owner reach an older run and its delivery receipt, but the selected older run still shows none of its recorded events. The existing
readers do not fit: `GET /api/runs/{runId}/events` reads every later event without a bound and returns each event's payload, and the
cockpit timeline is a live-event-driven reader of the latest collaboration messages. `RunEvent.Sequence` is a database-assigned
signed 64-bit key that is global across runs, so one run's sequences have gaps and can exceed what a browser number represents exactly.
The current record also lacks several envelope fields the data model describes (a schema version, a persistence time, causation and
correlation identities); this decision invents none of them. This is a first journal browser, not a replay.

## Decision

**One protected, read-only page of one run's event headers.** `GET /api/runs/{runId}/event-history` (`GetRunEventHistoryQuery` through
`IApplicationMediator`, one MVC endpoint, bodyless, protected like every run operation) answers one page of that run's persisted event
headers in descending `Sequence`. An unknown run is a safe 404 (`runs.not_found`); an existing run with no event is a successful empty
page. The transport types are API-owned records and the generated TypeScript client is regenerated reproducibly from the OpenAPI
document. The existing events route, its payload contract, the live notifier and the collaboration timeline are not changed or reused.

**Cursor paging over exact text, never clamping.** `limit` defaults to 20 and accepts 1 to 50. `beforeSequence` is an optional exclusive
cursor that is a string, because a sequence can exceed 2^53: absent or blank means absent, otherwise it must be canonical ASCII
decimal digits, positive, without a sign, padding, leading zero, fraction or exponent, and at most 9223372036854775807. It is parsed as
exact text for the database predicate and never passes through a floating-point value. Both scalars are bound by feature-local binders
that add no framework model-state error: a malformed, overflowing, signed, padded or repeated value is refused by the query's
validator through the shared Problem Details contract (`validation.invalid_limit` or `validation.invalid_cursor`, a fixed detail, a
JSON pointer such as `#/beforeSequence`, the trace identifier) and never repeats the rejected value. The operation declares
`ApiProblemDetails` for 400, 404 and 409 so the OpenAPI document and generated client carry the typed refusals; no global MVC behavior
and no other route changes. The page reads only this run's rows through the existing `(RunId, Sequence)` index, newest first, with an
exclusive `Sequence < beforeSequence` predicate and `Take(limit + 1)`: the extra row gives an exact `HasMore` without a count, and
`NextBeforeSequence` is the last admitted sequence (as a decimal string) only when older rows remain. There is no offset, count,
aggregate, full-entity load or automatic draining of pages. Events appended between two reads can neither repeat nor skip an older row,
and a reload reads the current newest headers: pages are not an atomic snapshot of the run and claim no frozen total. Gaps between one
run's sequences are valid, and timestamps never define order.

**What an entry contains.** Only the run and event identities, the exact sequence as a canonical decimal string, the occurrence UTC
time, the event type and an actor disclosure (kind, nullable role, nullable provider). The event type is admitted only by exact,
case-sensitive match to an existing `RunEventType` constant, decided by the database; any other stored value keeps its row and is
disclosed only as the fixed `Unrecognized`, never exposed. The three actor columns are classified by the database without
materializing a stored enum value or constructing a `ParticipantIdentity`: `None`, `Orchestrator` and `Human` carry neither role nor
provider, an `Agent` carries a known provider and an optional role (a valid Agent with no recorded role is preserved exactly as
recorded), and an unknown kind, role or provider or an inconsistent combination is `Unrecognized` with a null role and provider. A
healthy neighboring row is never affected. No role, provider, authority or provider success is ever inferred. The query does not
select, parse or return `PayloadJson`, `AttemptId`, a message, an artifact or any current-state enrichment.

**One fixed refusal for an unusable header.** If a selected stored event (including the extra row that proves `HasMore`) has an empty
identity or a nonpositive sequence, the whole read is the single fixed 409 `run_event_history.unavailable`; a partial page is never
returned. Other faults, including a database error, keep the established error pipeline and are not masked as unavailable.

**Read-only by construction.** The query reads persisted rows untracked, writes nothing and invokes no Git, filesystem, provider,
process, notifier or mutation action. It needs no migration or persistence change and changes no admission, recovery, approval,
digest or receipt decision.

**Cockpit.** The selected historical run detail adds a **Recorded events** region, collapsed by default. Opening it requests one
page (ten headers); "Load older events", "Try the older events again" (the same cursor after a failed older page) and "Reload newest
events" are explicit actions, and nothing is polled, timed, retried automatically or stored in the browser. Headers show newest first
with the exact sequence text, a fixed label for the event type, the actor as recorded (an Agent without a recorded role reads "role not
recorded"), the UTC time and the event identity, all as plain text. A fixed note says only recorded headers are shown, event details
are omitted, and these are recorded facts, not live status, not a current approval and not a complete replay. The region offers no
selected-event action, payload viewer, playback clock or live control and mounts no live cockpit hook; the existing receipt and
metadata of the detail are unchanged.

**Ownership and validation.** The reads belong to committed lifetimes built on the shared owned-lifetime hooks: the committed run
(collapsed for every run; another run, a return to an earlier one (A to B to A), the detail's close and reselection or unmounting ends
it) and each open or reload of it (a fresh newest-page read with no rows and no error; closing and reopening end it too). A replacement
derives its own empty first frame in the same render, an older or replaced read can neither overwrite nor settle a newer one, and the
callbacks of an ended lifetime start nothing; no key or remount workaround is used. A small decoder accepts a page only whole and only
when it is coherent for exactly its request and the rows already retained: the answered run, no more entries than requested, nonempty
unique event identities, canonical positive 64-bit sequence texts compared as exact integers (never as Numbers) in strictly descending
order and below the cursor and the last retained row (gaps are valid), valid instants, only the closed event type and actor
disclosures with a coherent role and provider, and a continuation that is a full page whose next cursor is exactly its last sequence
(or none). Anything else is an explicit invalid-page failure, never a plausible partial page; a failed older page keeps only the valid
rows of its own lifetime and retries the same cursor.

## Consequences

- An owner can inspect the recorded journal headers of an older run, in bounded pages and exact order, also after a reload or restart,
  without any mutation or live work and without the payload of any event.
- The headers are as good as the recorded rows: an unrecognized stored value is disclosed, not repaired, and an unusable header is one
  fixed refusal rather than a guess.
- Admitting a new `RunEventType` constant requires the closed label set of the cockpit and its pinned test list to learn it
  deliberately; until then the cockpit refuses a page that names it rather than showing an unexplained value.
- The contract is additive. It is not a replay, a payload or artifact viewer, a timeline clock, an export, a retention or cleanup
  mechanism, a remote publication, an autonomous coordinator or evidence of provider reliability, and it does not complete the
  durable-history increment or the MVP.
- Full replay, envelope completion (versions, persistence times, causation and correlation), payload and artifact inspection and
  generic history infrastructure remain separate decisions.
