# ADR-0036: Inspect the recorded reply parent of a historical collaboration message

Status: Accepted

## Context

[ADR-0004](0004-use-a-structured-agent-collaboration-protocol.md) makes every non-root collaboration message a reply to exactly one earlier
message, and [ADR-0035](0035-browse-recorded-collaboration-messages-of-a-historical-run.md) lets an owner read an older run's recorded
messages in bounded pages. The page deliberately returns no reply target, so an owner can read a challenge, a decision or a review but not
see which message it answered, and the parent is often on an older page that has not been loaded. The live timeline cannot help: it
resolves only parents inside its latest-100 window, returns raw JSON and materializes stored enums. `CollaborationMessage.InReplyToMessageId`
and `CollaborationMessageReplyPolicy` already hold the durable identity and the protocol meaning; no persistence is needed. This is a
bounded step towards linked replay, not replay: attempts, evidence, chains and generic traversal remain out of scope.

## Decision

**One protected, read-only inspection of one message's own recorded parent.**
`GET /api/runs/{runId}/collaboration-history/{messageId}/reply` (`GetCollaborationHistoryReplyQuery` through `IApplicationMediator`, one
MVC endpoint, bodyless, protected like every run operation, API-owned response types, the generated client regenerated reproducibly)
answers the relationship of exactly that message. An unknown run, and a source that is missing or belongs to another run, are the same
fixed 404 (`collaboration_reply.not_found`) of the shared Problem Details contract, which repeats no value. A source whose stored identity
is empty or whose stored sequence is not positive is one fixed 409 (`collaboration_reply.unavailable`). Other faults, including a
database error, keep the established pipeline and are never masked as `Unavailable`. The ADR-0035 page, the timeline and the event routes
are unchanged in behavior.

**What is read.** Three untracked, indexed selects and nothing else: the run's existence; the source by exact run and message identity,
reading only its sequence, identity, version match, database-classified type and `InReplyToMessageId` (never a text column); and, only
when the source stores a plausible reference, at most one parent selected by that stored identity within the same run, with a strictly
smaller sequence and protocol version exactly `1.0`, through the ADR-0035 projection. No latest, same-role or same-type message is ever
substituted, no page is drained, no chain is followed, and nothing is written.

**What it returns.** The run and source identities, the source's exact sequence as a canonical decimal string (it can exceed 2^53), a
closed status and a nullable parent entry:

- `None`: the source is a recognized version-1.0 Proposal with no stored reference. It implies nothing else.
- `Unavailable`: anything that cannot be shown as a valid recorded relationship, without naming why or any target: an unknown source type
  or version, a message that requires a reference and stores none, an empty, self, missing, foreign-run, forward (not strictly earlier)
  or non-version-1.0 target, an unrecognized parent type, or a parent type the existing reply policy rejects for the source's type.
- `Available`: the stored parent exists as above and `CollaborationMessageReplyPolicy.Evaluate(sourceType, parentType)` accepts the pair.

The parent is non-null exactly for `Available`. A reference that cannot be parsed as an identity at all is a corrupted column outside the
ledger's own writers; like the ADR-0035 page it surfaces as a database fault rather than being disguised as a status.

**Relationship and content are distinct.** `Available` describes the recorded protocol relationship only. Its parent uses the ADR-0035
disclosure unchanged and shares its code (one small history-owned extraction of the projection and the entry builder, so both readers admit
a message identically): database enum classification without materializing a stored value, byte guards on the summary and content JSON
(`length(CAST(column AS BLOB))`, at most 1800 and 15000 bytes), the exact character limits, the existing content policy and header-only
`Unavailable` content. A parent whose own content or metadata is unavailable is still the recorded parent: its header is shown and no text
is.

**Cockpit.** Every message in the Recorded collaboration region gains one explicit "Inspect recorded reply" action. There is one
inspector: using the action on another message replaces it, and it offers "Close recorded reply" and, after a failed read, a same-source
"Try the recorded reply again". Nothing is requested on render or for rows, followed recursively, polled, retried automatically or cached;
the loaded pages and their cursor are untouched. The parent is shown as plain text with the same fixed labels as a message, without an
inspect action of its own, beside a fixed note that this is the recorded relationship only, not a current approval or provider success.
`None` and `Unavailable` are distinct fixed statements and never name a target.

**Ownership and validation.** The inspector belongs to the committed run's open history lifetime (closing, reopening, reloading, another
run or unmounting ends it) and to each source selection: a different source, and closing then reinspecting the same one, are new
lifetimes, so an answer for A can never show for B or a later A. Replacement derives its own first frame in the same render, an ended or
replaced read can neither overwrite nor settle a newer one, and the callbacks of an ended lifetime start nothing; there is no key or
remount and no effect that resets state. A decoder accepts an answer only whole and only when coherent for exactly the observed source:
the answered run and message, the observed exact sequence text, a closed status with a matching target shape, `None` only for a
recognized Proposal, an unrecognized source only `Unavailable`, and an `Available` parent that is distinct from the source, a recognized
version-1.0 message of a type the restated reply table allows for the source's type, strictly earlier by exact integer comparison, and
valid under every unchanged ADR-0035 entry rule. Anything else is an explicit failure and none of it is shown.

## Consequences

- An owner can see exactly which recorded message another message replied to, including a parent on a page that was never loaded, in
  bounded, exact form and without mutation, live work, raw JSON or a transcript.
- A damaged, foreign or forward reference is reported as unavailable instead of resolving to a neighbor or failing the page.
- The reply table exists in the Domain policy and, restated, in the cockpit decoder; admitting a new type or pair requires both to learn it
  deliberately, and until then the answer is `Unavailable` (host) or refused (cockpit).
- The contract is additive. It is not replay, a chain or thread view, attempt or evidence resolution, an approval, a statement of provider
  success, an export or a remote action, and it does not complete the durable-history increment or the MVP.
- Chain traversal, attempt and artifact linkage and generic history infrastructure remain separate decisions.
