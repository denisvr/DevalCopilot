# ADR-0071: Inspect recorded run abandonment in history

Status: Accepted

## Context

ADR-0031 records a Human abandonment reason, time and matching run event. Its
current-run panel already shows that decision. ADR-0033's selected historical
run shows lifecycle and stage, while ADR-0034 deliberately omits event payloads.
After another objective replaces an abandoned run, history therefore cannot
show why that earlier objective ended. Mounting the live abandonment reader
would import present eligibility and project-wide blocker reads into history.

Select RUN-ABANDON-INSPECT-01 within existing functional-development authority:
one bounded recorded read and one historical inspection region. All supported
abandonments, including closure from Created without a workspace and closure
from Running at its preserved stage, belong together. The current panel needs
no duplicate surface. This adds no command or authority, supersedes no accepted
decision and completes no increment or semantic replay.

## Decision

### One protected recorded operation

Add bodyless, queryless `GET /api/runs/{runId}/abandonment-inspection`, one MVC
endpoint/action dispatching GetRunAbandonmentInspectionQuery through
IApplicationMediator. Validate a nonempty run identity in Application. Use the
shared Result/Problem Details boundary and a complete API-owned response graph;
regenerate the existing NSwag client reproducibly.

Refuse positive content length, any transfer encoding or a readable undeclared
body byte before dispatch: fixed 400 `run_abandonment_inspection.body_not_allowed`.
Refuse every supplied query key, including blank, repeated and differently cased
names: fixed 400 `run_abandonment_inspection.query_not_allowed`. Echo no input.
An unknown run or missing registered project returns fixed non-disclosing 404
`run_abandonment_inspection.not_found`.

For a registered run/project, answer exactly `{ runId, status, abandonment }`:

- NotRecorded, null payload: recognized mode and non-Abandoned lifecycle, both
  abandonment columns SQL NULL, and no exactly associated run.abandoned event.
- Unavailable, null payload: recorded abandonment markers or events cannot pass
  the selected proof, including Abandoned with missing evidence, stray facts
  beside another lifecycle, unsupported root disclosures or duplicate events.
- Recorded, one complete payload: exactly projectId, reason, abandonedAtUtc,
  eventId and eventSequence. Wire UUIDs are lowercase D-format, abandonedAtUtc
  is UTC round-trip text with seven fractional digits, and eventSequence is
  a canonical positive decimal Int64 string, never a JavaScript number.

Negative statuses report only this read. They assert no present eligibility,
coherence verdict for other consumers, ability to record another objective,
ownership, cancellation, recovery or execution result.

### One bounded consistency proof

Use one no-tracking scalar EF statement rooted in the exact run and registered
project, left-associated with that run's exact stored-key run.abandoned events.
Materialize at most two rows: zero associated events, one candidate or detected
excess. Prove presence separately from parsing identifiers or payloads; damage
must not become absence. Use existing run/project keys and the run/sequence
event index, with no whole-table scan, collection Include, count of all events,
entity materialization or query of other runs' histories. An indexed search may
examine this run's event headers to establish event-type absence/uniqueness;
the two-row cap bounds returned material, not all examined index entries.

Select only root identities, recognized stored execution mode and lifecycle,
the two abandonment columns, last-advanced time, the no-active-participant
comparison, and each matched event's identity, sequence, run association,
Human/run-scoped comparison, payload and occurrence time. Do not read objective,
stage, budgets, attempts, messages, verification, workspaces, leases, commits,
artifacts, current eligibility, filesystem, Git, processes or provider state.

Bound variable storage before materializing it: canonical stored UUIDs within
36 bytes, time representations within 64 bytes, reason within 2048 UTF-8 bytes
and event payload within 16384 UTF-8 bytes. Retain null versus present-but-
unsupported distinctions. Admit exact integer sequence 1..Int64.MaxValue before
unsafe conversion; SQLite's rowid constraints need not be bypassed to invent
impossible storage tests. Refuse malformed UTF-8/text, oversized values and
unsupported identities/times without a plausible partial answer. Honor the
existing abandonment column's TEXT-only tolerant mapping, including refusal of
a BLOB holding otherwise valid timestamp bytes; change no mapping or schema.

The event cap accommodates every production reason even when JSON escapes each
of its at most 2048 UTF-16 units as six ASCII bytes. Oversized or unsupported
external storage makes this inspector Unavailable, without changing any other
consumer's decision. Do not fetch a large value merely to reject it afterwards.

For Recorded, require exactly one admitted matching event with a nonempty
identity and positive exact sequence. Reuse RunAbandonmentReader.ReadReason's
existing defensive JSON interpretation and invoke the unchanged
RunAbandonmentPolicy.IsCoherent on the selected facts: Abandoned, ManualAgent,
already canonical reason, supported abandonment time equal to last advance,
no active participant, and exactly one Human-authored run-scoped event with
the same reason and instant. Do not copy or tighten this business rule, replace
the shared reader, or feed this inspection into an authority decision.

Preserve full tick equality and supported instants/offsets; normalize admitted
instants to UTC only for transport. Do not add ADR-0070's operation-specific
nondefault-instant rule here. Canonical reason follows ADR-0031, including
scalar Unicode classification, allowed LF and the 2048-byte UTF-8 bound. Never
trim, normalize, truncate or repair stored evidence while reading it.

Do not swallow database or cancellation faults as any of the three statuses.
The statement is a read of unsigned relational consistency, not authenticated
history. Independently loaded history headers and this answer are separate
reads, and matching rewrites remain outside the assurance.

### Historical surface and ownership

Add one collapsed **Inspect recorded abandonment** region in
ProjectRunHistoryDetail for every selected run, regardless of the earlier
page's lifecycle or receipt source. Read only on explicit open/reload and close
cold. Keep the current AbandonRunPanel and every live reader/control unchanged.
Do not infer absence from the list row or mount eligibility/command hooks.

Show the recorded reason as literal multiline text, the full UTC instant and
the supporting event identity/sequence. Explain that abandonment ended the
objective without successful completion; this read proves no cancellation,
workspace repair, ownership release, current intake permission or publication.
Unavailable and NotRecorded use fixed independent historical copy.

Decode the complete raw wire before generated Date revival/coercion, checking
exact keys, discriminants, null shapes, requested run, recorded project against
the selected entry, UUIDs, canonical reason, full UTC text and exact Int64 text.
Any violation rejects the whole answer. Use the established operation-owned
wire facade and unchanged owned-lifetime primitives with a real ten-second
abort. Run/project, selection, opening and read visits own callbacks and results;
close, reload, A-B-A, remount and late/retained callbacks cannot populate or act
for successors. Reload immediately clears the old answer. No polling, prefetch,
automatic retry, cross-visit cache, browser persistence, export or mutation.

### Evidence and exclusions

Use migrated file-backed SQLite to prove normal production-written abandonments,
the three statuses and fixed 404, selected damage/bounds, exact event association,
duplicate detection, full-tick equality, supported timestamps and TEXT-only
abandonment time. Prove one bounded statement, no forbidden columns or writes,
unrelated later work/reopen stability and propagation of real database faults.
Exercise the complete MVC auth/verb/body/query/error/wire/OpenAPI matrix and
generated-client reproducibility. Prove frontend decoding, literal rendering,
real timeout and historical selection/lifetime boundaries.

Extend only abandon-run.journey.ts, after its second objective settles, to read
the earlier recorded abandonment and newer NotRecorded run from history, check
exact responses and rendered facts, reload/cold close and selection replacement.
Baseline business POSTs, invocations and named durable/repository observations;
inspection adds none. Run once, announced and automatically headed, using normal
authentication and deterministic executables, captures off. Page reload is not
host restart. The planner handoff owns the proportional final matrix.

No Domain, shared abandonment reader/policy, persistence mapping, migration,
producer, intake/replay, live status, history paging/discovery, shared hook,
worker, dependency or harness redesign is selected. Provider qualification,
general recovery, automatic approval/commit/publication and all frozen grants
stay outside scope. A conflict requiring those changes returns to the planner.
