# ADR-0054: Inspect recorded outputs of a selected Agent attempt

Status: Accepted

## Context

The historical attempt inspector exposes execution facts, sealed artifacts,
recorded collaboration inputs (ADR-0051/0053) and recorded verification inputs
(ADR-0052). It cannot locate the collaboration messages the selected attempt
itself produced. The separate collaboration browser can attribute a message
to its attempt (ADR-0037), but finding all messages of an attempt still requires
searching unrelated pages. A Resolver emits Decisions and a revised Proposal;
a review or diagnosis can emit several findings; a correction can emit several
RevisionResponses and an ExecutionReport. A single latest-message shortcut
would lose these outputs.

`CollaborationMessage.AttemptId` already records the relationship, with an
existing index. Role-specific result handlers record provider-observed messages
atomically; host-constructed planning and correction escalations have no
message-level attempt reference, even when a related event names an attempt.
This decision inspects the message relationship only, never inferring it from
an event, reply, role, number, time or semantic outcome.

## Decision

### One complete bounded outcome

HIST-OUT-01 adds all admitted output references and explicit inspection of one
member's guarded content to a selected historical Agent attempt. Reference
disclosure, content, refusal states and asynchronous ownership ship together.
It extends historical access, not the write protocol or result acceptance.
ADR-0035/0037/0051/0052/0053 and all existing readers keep their behavior.

Two protected, bodyless MVC GET operations dispatch through
`IApplicationMediator`, with API-owned response graphs and reproducible NSwag:

- `/api/runs/{runId}/agent-attempts/{attemptId}/outputs`
  (`GetAgentAttemptOutputsQuery`).
- `/api/runs/{runId}/agent-attempts/{attemptId}/outputs/{messageId}`
  (`GetAgentAttemptOutputMessageQuery`).

Missing/foreign/non-Agent attempts, including an unknown run, share the fixed
404 `agent_attempt_outputs.not_found` for the list. The member operation uses
`agent_attempt_output_message.not_found` for those cases and for a nonmember
of a proved set. Errors repeat no rejected value. An unprovable attempt or set
returns 200 Unavailable before membership can be decided, even for a stranger
message ID. Database faults, malformed identity/time materialization and
cancellation keep the normal fault pipeline; no broad exception masking.

### Whole relationship proof before disclosure

Both operations use one feature-owned `Policies/AgentAttemptOutputs` reader,
shared only by these two queries. Read exactly the selected attempt in its run
through unchanged `ScalarAgentAttemptIdentity`; do not materialize the entity.
After identity admission, select at most 65 scalar message headers whose
`AttemptId` equals that attempt, ordered by the exact collaboration sequence.
Read through the existing attempt-reference index and inspect at most 64.
The bound limits inspection only; it changes no stored-output or write limit.
Sixty-five rows mean Unavailable, with no prefix, count or partial references.

Do not filter out malformed associations. Every inspected row must have the
selected RunId and AttemptId, a unique nonempty message ID, a positive strictly
increasing sequence, protocol 1.0, a known message type allowed by unchanged
`CollaborationMessageAuthorPolicy` for the attempt's role, ProviderObserved
provenance and an Agent actor whose role/provider exactly equal the attempt.
Classify stored enums in SQL rather than materializing unknown values; select
no summary, JSON, reply pointer or unrelated attempt/message. A nonempty set
also requires a recorded dispatch time. An inconsistent row invalidates the
whole set; no neighboring/latest message is substituted. Select by AttemptId
before checking RunId so a foreign-run association is refused, not hidden.

Zero associated rows is Empty for any admitted Agent role, dispatched or not.
It means only that no associated message was recorded in this read; it does
not prove that a provider produced no output, that a result succeeded or failed,
or that deleted/missing historical data can be reconstructed. Do not infer
expected cardinality or complete response semantics from the attempt outcome.
Do not join events or follow repair, input or reply relationships.

### Reference and member contracts

The list root contains exactly `runId`, `attemptId`, `status`, nullable
`attempt`, nullable `outputMessageTotalCount` and `outputMessages`:

- Unavailable: null attempt/count and an empty array.
- Empty: admitted immutable attempt header, count zero and an empty array.
- Recorded: that header, count 1..64 and every reference, without omission.

The immutable header contains exactly `id`, `attemptNumber`, `role`, `provider`
and `responseContract`, with ADR-0051 meanings. Each reference contains exactly
`position`, `collaborationMessageId`, `type`, `collaborationSequence` and
`occurredAtUtc`. Position is the zero-based ordinal in this read's sequence
order, not a new persisted sequence. Collaboration sequence is a canonical
positive Int64 decimal string; time is a UTC round-trip string with seven
fractional digits and Z. Preserve text, including values above 2^53. No request
limit, cursor or omission flag is needed for this new complete bounded list.

The member operation independently repeats the whole set proof before reading
text, then selects only the requested admitted member in that same run and
with that same AttemptId, through unchanged `CollaborationHistoryRows.Project`
and `ToEntry`. Never call another handler. Recheck the reread ID, sequence,
exact timestamp (`EqualsExact`), version, type, provenance and actor against
the admitted relationship; a missing or disagreeing reread is root Unavailable.
The association predicate and rechecks must survive changes between reads.

Its root contains exactly `runId`, `attemptId`, `collaborationMessageId`,
`status`, nullable `attempt`, nullable `output` and nullable `message`.
Unavailable withholds all three nullable values. Recorded contains the admitted
immutable header, the selected reference, and exactly the ADR-0053 message
graph: `runId`, `id`, `sequence`, `occurredAtUtc`, `protocolVersion`, `type`,
`provenance`, `actor`, `recipient`, `contentStatus`, nullable `summary` and
`fields`; participant and field shapes, schema order, bounds and content
admission are unchanged. Both sequence/time representations are exact strings.
Keep an unavailable body distinct from an unavailable relationship; it hides
neither the proved reference nor its siblings. Summary/JSON byte guards and
the existing secret/content policy are never weakened or duplicated.

Separate reads are evidence as read now, not an atomic or claim-time snapshot.
Header-consistent content may differ between reads. A later changed reference
or ordinal is rejected by the browser against its selected reference; closing
and reopening the section obtains a fresh set. No raw JSON, reply/attempt chain
pointer, transcript, session, artifact, prompt or new content policy is exposed.

### Explicit UI and ownership

Add a collapsed Recorded collaboration outputs section beside inputs and
verification in `AgentAttemptDetail`, independent of the general evidence
answer. Opening reads the list once; show ten references initially and reveal
further local groups of ten explicitly, without another HTTP request. Local
disclosure is not host truncation. Each displayed reference offers Inspect
output; one replaceable inspector reads content only on that explicit action,
with Close and failure-only Retry. Selecting the current message is a no-op;
close then reselect starts a fresh visit. Empty, root Unavailable, unavailable
content, 404, incoherent answer and read failure have distinct fixed copy.

Render identities, participants, provenance, summary and admitted schema fields
as literal text with fixed labels. State that these are stored messages
attributed to this attempt, not its complete provider response, proof of what
was generated or seen, current approval or execution success. Show no live,
eligibility, configuration or mutation control.

Use operation-scoped facades over the generated operations to retain parsed
raw JSON. Whole-answer decoders validate exact keys, selected immutable header,
status tuples, all positions/unique IDs/sequence order and canonical time text,
the role/type table, and selected-member equality before display. The member
decoder reuses unchanged collaboration-content admission in the frontend;
do not let Date revival normalize the new fields or invent absent values.

Use existing owned-lifetime primitives unchanged. Attempt selection, every
section opening and each member visit own results, requests and callbacks.
Close/reopen, another message/attempt/run/project, A-B-A, queued Close/reselect
in one batch, parent reload and unmount invalidate old work before a successor's
first frame. Guard against queued changes, including retained retry, close,
selection and local-disclosure callbacks. No forced remount, prefetch, polling,
automatic retry, chain navigation, linkification, download, export, logging of
content, browser/URL persistence or mutation.

## Consequences and validation

The largest coherent read-only outcome is output discovery plus selected
content: a list alone leaves content inaccessible, and a member endpoint alone
leaves multi-message outputs undiscoverable. Bundle both operations, UI and
tests. Full graph replay requires traversal/navigation policy; automatic stage
coordination requires dispatch, recovery and authority decisions. Neither is
inferred from this additive historical capability. Provider contracts and
GitHub execution retain their existing restrictions.

Prove file-backed SQLite membership for zero/one/multiple/11/64/65 rows,
last-member corruption, foreign associations, role/provider/provenance/type
mismatch, undispatched nonempty sets, exact older-run ownership, bounded indexed
scalar reads and no text before whole-set proof. Prove one selected text read,
reread disagreement including association/actor changes, all existing content
refusals, oversized/NUL-hidden storage, Int64/time text, faults/cancellation,
and no writes, artifacts or external effects. Test MVC authentication,
non-disclosing errors, wire/OpenAPI, NSwag reproducibility, whole raw decoders
and ownership through hooks, composition and rendered controls.

Extend only `historical-attempt-inputs.journey.ts` using its real recorded
Planner/CriticalReviewer/Resolver lineage and older run after a newer objective.
Locate all Resolver outputs by message-level AttemptId, explicitly inspect a
Decision and the revised Proposal, and compare references and safe bodies to
their stored rows. Prove no automatic body read, mutation, artifact access or
additional provider invocation. Keep the prior input assertions. Synthetic
large/corrupt/empty-output cases stay at lower boundaries. No shared harness,
snapshot/cache, Domain, persistence, migration, dependency or provider change.

Increments 4/5/6 remain incomplete; ADR-0050 stays Proposed, P9 unproven and
AI Memory deferred. This decision grants implementation scope only, never
commit, push, real-provider or experimental authority.
