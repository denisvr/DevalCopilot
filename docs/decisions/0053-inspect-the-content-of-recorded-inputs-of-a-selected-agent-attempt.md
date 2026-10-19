# ADR-0053: Inspect the content of recorded inputs of a selected Agent attempt

Status: Accepted

## Context

ADR-0051 exposes the first ten recorded collaboration-input references of a
selected historical Agent attempt after validating its entire bounded set.
`SelectedAttemptRecordedInputs` deliberately renders only text references and
loads no referenced content. The owner must search separate collaboration
pages to read a referenced proposal, challenge, decision or instruction, and
cannot reach references beyond the ten-entry prefix from this inspector.

The data already exists in `AttemptInputMessage` and `CollaborationMessage`.
ADR-0035/0036 define bounded, guarded admission of stored collaboration
content. This decision extends ADR-0051's reference-prefix contract and its
selected-attempt UI only; its existing default response and the message-linked
reader remain unchanged. It does not make an input set a complete prompt,
context manifest, resumable session, immutable snapshot or replay.

## Decision

### One bounded inspection outcome

HIST-INPC-01 makes every reference in an admissible set of at most 64 inputs
reachable and lets the owner explicitly read one referenced message at a time.
Ship the reference-limit extension, selected content reader, refusal states
and lifetime isolation together. No content is prefetched or read in bulk.

### Existing reference operation: an explicit prefix limit

Extend `GET /api/runs/{runId}/agent-attempts/{attemptId}/inputs` and
`GetAgentAttemptInputsQuery` with optional `limit`: default 10, range 1..64.
An absent or single blank value keeps the default; otherwise require canonical
ASCII decimal integer text, rejecting signs, padding, leading zeroes, fractions,
exponents, duplicates and overflow. Use shared validation Problem Details with
`validation.invalid_limit`, a fixed detail, `#/limit` and trace ID, never the
rejected value. Enforce the bounds in Application too; never clamp.

Every request still applies the unchanged scalar attempt-identity policy and
reads at most 65 ordered scalar input rows, inspecting at most 64. Validate
the complete inspected set and every referenced message identity before any
prefix is returned. The response graph and Unavailable/Empty rules stay the
same. Recorded returns exactly `min(totalCount, effectiveLimit)` references,
with omission exactly when totalCount exceeds that prefix. Requests without
the new parameter retain ADR-0051's ten-reference behavior. The inspection
bound is neither claim admission nor a stored-input limit.

The selected-attempt UI explicitly requests `limit=64` once per opening. It
initially displays ten references and offers manual local disclosure in chunks
of ten until the returned set is visible, without another HTTP request. Label
local disclosure separately from host omission or an unavailable set; do not
describe a 65-input refusal as no inputs. Closing the section resets disclosure.
The legacy message-linked input reader is not extended.

### One exact input message

Add the protected, bodyless GET
`/api/runs/{runId}/agent-attempts/{attemptId}/inputs/{messageId}` through
`GetAgentAttemptInputMessageQuery`, its handler and one MVC endpoint class.
An unknown run, missing/foreign/non-Agent attempt or requested message absent
from a fully admitted input set returns the same fixed 404
`agent_attempt_input_message.not_found`, without rejected identities.

Every content request independently revalidates the complete bounded input set,
including all 64 rows when present, before reading any summary or content.
An unprovable identity or malformed/oversized set returns 200 Unavailable,
without headers, references or partial content. A coherent empty Planner set
has no member and therefore the requested message is not found. A malformed
set is not filtered to make a requested member appear valid.

Only after membership is proved, read that exact message in that same run with
the unchanged `CollaborationHistoryRows` guarded projection/admission used by
ADR-0035/0036: SQL byte guards for summary/content (1800/15000), in-memory
600/5000 UTF-16 bounds, classified enums/participants, provenance coherence,
safe summary and the existing closed Domain content policy. Select no other
message's summary or JSON. Compare the reread identity, type, version, exact
sequence and timestamp with the admitted selected reference; a disagreement
returns root Unavailable, not a substituted or repaired target. Separate reads
do not claim an atomic historical snapshot or claim-time content attestation.

A narrow feature-owned extraction of the input-set reader for these two queries
is allowed with default ADR-0051 equivalence proved. Keep
`ScalarAgentAttemptIdentity` and `CollaborationHistoryRows` behavior unchanged;
reuse the latter's admission rather than inventing another parser. No handler
calls another handler, no generic repository or history infrastructure, and no
raw SQL is added to Application. Faults, cancellation and malformed identity/
timestamp storage retain the normal fault pipeline; do not mask them broadly.

### Closed response and raw wire

The API owns the entire new response graph. Its root contains only `runId`,
`attemptId`, `collaborationMessageId`, `status`, nullable `attempt`, nullable
`input` and nullable `message`. Status is exactly Unavailable or Recorded.
Unavailable withholds all three nullable values. Recorded requires:

- The same immutable attempt header as ADR-0051.
- `input`: exactly the selected reference's `sequence` (input position),
  `collaborationMessageId`, recognized `type`, `collaborationSequence` and
  `occurredAtUtc`, with the existing ADR-0051 meanings.
- `message`: exactly `runId`, `id`, `sequence`, `occurredAtUtc`,
  `protocolVersion`, `type`, `provenance`, `actor`, `recipient`, `contentStatus`,
  nullable `summary` and `fields`. Participants contain exactly `kind`, nullable
  `role` and nullable `provider`; each field contains exactly `name` and `value`.
  Values and content admission follow ADR-0035, including its fixed
  Unrecognized disclosure and unavailable-content header with null summary
  and no fields. The input proof already requires known version/type.

Both collaboration sequence values are canonical positive Int64 decimal
strings. Both timestamps are UTC round-trip strings with seven fractional
digits and `Z`, preserved as text; do not change existing history Date fields.
Message identity, type, sequence and timestamp must equal the selected input's
facts. No reply, producing-attempt pointer, raw JSON, artifact or provider
session is returned. An unavailable message body does not invalidate a proved
input reference, hide siblings or mean the attempt had no inputs.

### UI, acceptance and ownership

Each visible reference offers an explicit Inspect input action. One content
inspector reads only when selected, with explicit Close and same-selection
failure Retry. It displays the selected input identity, recorded message
participants/provenance, summary and admitted fields as literal text in the
existing closed schema's order and fixed labels. Content unavailable, root
Unavailable, not found, incoherent response and read failure are distinct fixed
states. None names a rejected stored value or preserves another selection's
content. Keep the caveat that these are stored collaboration inputs as read
now, not the exact full prompt, all context or proof of what an Agent saw, nor
current approval or provider success.

Validate parsed raw JSON whole before rendering: exact new object keys, status
tuples, immutable selected-attempt header, selected reference position/type/
IDs/sequence/time, participant/provenance coherence and unchanged content
schema/bounds. The reference decoder also binds its prefix length/omission to
the actual requested limit, retaining default-ten behavior for old callers.
No Date coercion can normalize away an invalid new timestamp before validation.

Use existing owned lifetimes for the selected attempt, each section opening
and each input selection visit. Closing/reopening, another input/attempt/run/
project, A-B-A, Hide/Close then reselection in one batch, reload and unmount
invalidate old results and callbacks before a successor's first frame. Decide
visit changes and callback guards against queued state. Selecting the current
input again is a no-op; closing then reselecting it starts a fresh visit. Prior
close/retry callbacks cannot clear or read for a successor. Local disclosure
and its retained callbacks also belong to their opening. No forced remount,
shared lifetime-primitive change, polling, automatic retry, chain traversal,
artifact read, linkification, export, browser persistence or workflow mutation.

## Consequences and validation

The largest coherent bounded outcome is complete reference access plus one
explicit input-body read: reference expansion alone leaves the content gap,
while body reading alone leaves admitted inputs after the tenth unreachable.
Full linked replay and navigation across reply/producing-attempt/artifact chains
need separate contracts. Provider capability, autonomy and product publication
are not advanced by this read-only slice.

Prove default-ten equivalence and 1/10/11/64/65 boundaries, last-member corruption,
exact membership and no pre-admission content read with real file-backed SQLite,
bounded indexed scalar SQL and no writes/external effects. Cover the unchanged
content policy's unavailable cases, large/NUL-hidden storage, Int64 values above
2^53, timestamps, faults/cancellation, protected HTTP/Problem Details/OpenAPI,
raw-wire/decoder/hook/composed/rendered ownership and local disclosure.
Extend the existing authenticated deterministic-provider
`historical-attempt-inputs.journey.ts` to compare an explicitly inspected older
input's body with its stored row after a newer objective, with no extra provider
invocation or mutation. Keep 11/64/65 and corruption below the browser rather
than fabricating journey lineage. Regenerate NSwag reproducibly; run the
affected regression matrix, architecture and one justified final whole-frontend
pass. No dependency, migration, Domain policy or shared fixture-cache change.

Increments 4/5/6 remain incomplete. ADR-0050 stays Proposed, P9 unproven,
AI Memory deferred and all experimental/provider/credential constraints stand.
