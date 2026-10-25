# Agent collaboration evidence inspection

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the read-only evidence inspection contracts: an Agent attempt's recorded
collaboration inputs, the sealed Agent-artifact text window with its
containment, integrity and disclosure limits, and the historical Agent-attempt
history, evidence and identity rules. The sections below were moved verbatim
except three mechanical reference repairs; the protocol's original headings
forward here. The cockpit navigation, lifetime and selected-attempt
execution-fact paragraphs that close the attempt-history section, and the
window-acceptance paragraph that closes the sealed-window section, were added
later. Per-attempt execution evidence is in the
[execution evidence](agent-collaboration-execution-evidence.md) companion;
provider token usage and model context limits are in the
[usage evidence](agent-collaboration-usage-evidence.md) companion.
Mapping: [protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Recorded collaboration-input provenance

The collaboration-message evidence drill-down (see the shared
[`Attempt.GetAgentProcessExecutionEvidence()` rule](agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome)) additionally exposes
the exact, ordered `AttemptInputMessage` set the producing Agent attempt was
launched against — labeled everywhere as **recorded collaboration inputs**,
never as the complete prompt, complete context manifest, or a resumable
provider session: it is a durable reference list, not a transcript. Zero
recorded rows is role-aware, never a blanket "empty" default: only a
`Planner` attempt legitimately starts from no prior collaboration fact and
is reported as `Empty`. Every other current role (`CriticalReviewer`,
`Resolver`, `Implementer`, `CodeReviewer`) always persists at least one
required input row through its own claim path, so zero rows observed for any
of them reports `Invalid` instead — an empty set there is indistinguishable
from a lost or corrupted one, and must never be presented as the same
innocent "started from none" state a genuine Planner attempt reports. This
role-aware read is only ever reached once the resolved attempt's own role and
provider are proven coherent with the message's own actor AND both are
proven defined: an attempt/message pair whose role and provider are both
undefined is never treated as a trivially "equal" (null-equals-null) match —
it fails closed to the same `AttemptLinkBroken` status any other incoherent
link produces, never a thrown error and never a role dereferenced from an
undefined value.

Resolution is strict: every referenced `CollaborationMessage` must resolve
within the same run the drill-down was requested for — a cross-run or
dangling reference is never followed — and the attempt's own stored 0-based
sequence must be gapless and duplicate-free. Any incoherence (a gap, a
duplicate, a missing/foreign message, or the role-aware zero-rows case above)
fails the *entire* set closed (`Invalid`) rather than presenting only the
resolvable subset, which would misrepresent the attempt's real ordered input
identity — the same "a partially trustworthy read is not trustworthy"
principle this drill-down already applies to its process and token-usage
evidence. A coherent set is capped at a small, explicit bound with an honest
omission signal, mirroring the existing bounded artifact list: unlike that
cap, this one is a real, reachable limit today, not merely defensive — a
review-correction (`Implementer`/`ReviewCorrection`) attempt's own Execution
report plus up to `ReviewCorrectionOutputSchema.MaximumFindings` (10) Review
findings is 11 rows at the bound.

Each returned entry carries only its own recorded sequence, the referenced
message's durable id, its `CollaborationMessageType`, its own timeline
sequence, and its recorded time — never that message's summary, structured
content, or any other payload. The cockpit cross-references the referenced id
against its own already-loaded collaboration timeline (the same map that
already verifies reply-parent relationships) to show that message's
already-displayed summary; a reference the currently loaded timeline window
does not contain is described as such — "not present in the currently loaded
timeline" — never rendered as if it were visible there.

## Sealed Agent-artifact window inspection

The collaboration-message evidence drill-down's bounded artifact list
is a metadata-only inventory — purpose, byte length, truncation, and
capture outcome, never content. A separate, additive operation
(`GetSealedAgentArtifactWindowQuery`/`GetSealedAgentArtifactWindowEndpoint`)
lets the owner open a bounded, integrity-verified text window of the sealed
bytes one of those metadata rows describes, restricted to a closed four-purpose
allowlist: `AgentContextManifest`, `AgentStandardOutput`, `AgentStandardError`,
and `AgentFinalResponse` — never the two Process-attempt purposes
`GetProcessAttemptOutputEndpoint` already serves, and never a live, still-being
-written partial capture (only a sealed row is ever served here). Resolution
repeats the identical message-to-attempt coherence rule the evidence
drill-down itself already enforces — the same `ProviderObserved` provenance
check, the same `AttemptId` foreign-key-only resolution, and the same
role/provider coherence check between the message's actor and the resolved
`Attempt` row. Each operation still owns its own request shape, identity proof, and failure projection; only the artifact-row filter and sealed read are shared, through
`SealedAgentArtifactWindowReader` (see [Agent-attempt history and evidence inspection](#agent-attempt-history-and-evidence-inspection)). An
`Artifact` row is then resolved by matching **all three** of `AttemptId`,
`RunId`, and the requested `Purpose` together; a purpose outside the closed
allowlist is rejected at both the API route (an unrecognized route segment is
a safe 404, mirroring `GetProcessAttemptOutputEndpoint`'s own `stream` mapping)
and, independently, inside the query handler itself (defense in depth for any
future non-HTTP caller).

Every requested window is served through
`IArtifactStore.VerifyAndReadSealedAsync`, which enforces two containment
proofs and one integrity proof, all case-sensitive and all failing closed to
`Missing` or `IntegrityMismatch`:

- **Lexical containment.** A stored relative path that is empty, absolute, or
  that resolves through `..` segments to anything not strictly under the
  artifact root — including a case-distinct sibling spelling of the root — is
  rejected before any file is opened.
- **Physical containment (Windows).** The candidate file is opened exactly
  once. On Windows, that open handle's own real, fully reparse-point-resolved
  path (`GetFinalPathNameByHandleW`) must lie under the artifact root's
  freshly resolved real path, again compared case-sensitively. A junction or
  symbolic link on the root's descendants, or on the sealed file itself, that
  redirects outside the root is therefore rejected even when the target's
  length and hash match. A root that is itself redirected is accepted only
  while the opened file remains within that root's resolved target. On other
  platforms only the lexical proof runs; physical containment is not proven
  there.
- **Integrity.** The whole file's length and SHA-256 are verified against the
  `Artifact` row through that same open handle before any byte is returned,
  and the bounded window is read from that same handle — nothing is reopened
  by path in between.

None of these outcomes discloses a storage path, hash, or raw diagnostic
across the API boundary. The response never resolves the attempt's other
artifacts, other attempts, or any other run — only the one sealed row this
exact `(AttemptId, RunId, Purpose)` triple names. Residual limits: the proofs
describe the file at open time and do not defend against a privileged actor
mutating the store's files in place after the open; sealing, capture, partial
reads, and cleanup are outside this contract. The bounded-window UTF-8 cursor
semantics (a byte offset, a byte cap, and a never-split multi-byte codepoint
boundary deferred whole to the next window) are the same ones
`GetProcessAttemptOutputEndpoint` established; this operation adds only a
closed set of purposes and a new resolution path to reach it.

The frontend renders every returned window as literal text (React's default
text-node rendering; never `dangerouslySetInnerHTML` or any other HTML
interpretation) inside the existing evidence drill-down, offering purpose
selection only for a purpose actually present in that attempt's own already-
loaded, bounded artifact list — never every allowlisted purpose
unconditionally — and a manual "load next window" action rather than
automatic paging. Selecting a different purpose, or a different run/message,
or closing and reopening the drill-down drawer, all discard any previously
fetched text and cursor state before the next fetch begins. Every window is
labeled as historical, with a caveat matched to how that exact purpose is
actually produced — never one blanket claim asserted for all four: the
context manifest is host-constructed from already-curated durable fields,
never raw provider output, while standard output, standard error, and final
response are provider output passed through a fixed, best-effort redaction
pattern list before capture. Neither classification is a live tail, and
neither guarantees the complete absence of sensitive content — an unusual
identifier in a host-constructed field, or an unrecognized secret format in
redacted provider output, may still remain. No window is ever rendered or
persisted anywhere but this one on-demand view (no browser storage, no URL,
no log).

**Window requests and acceptance (both routes).** The cockpit offers only the
distinct purposes the metadata names as one of the four allowlisted recorded
names (an own key of the allowlist, never an inherited name such as `toString`),
and both the message-linked and the attempt-history request map that recorded
name to the route's closed segment (`context-manifest`, `stdout`, `stderr`,
`final-response`); any other value starts no request. A closed non-`Ok` status
maps to its own disclosure and nothing else of its envelope is used or shown (an
integrity mismatch legitimately reports the observed file length). An `Ok`
window is accepted only whole: `text` is a string and `truncated` a boolean or
null; `nextOffset` and `totalLengthSoFar` are present, nonnegative safe integers
with requested offset <= `nextOffset` <= `totalLengthSoFar`; the advance is at
most the requested byte cap and positive while bytes remain (an empty file or a
read at the end advances by zero); zero advance carries no text and a positive
one some text, never more UTF-16 code units than bytes consumed; and the sealed
length equals the one the lifetime's first accepted window established. Cursors
stay the host's UTF-8 byte offsets: the text is never re-encoded to recompute or
require the advance, because a split codepoint is deferred whole and malformed
bytes are decoded with replacement. Anything else is one fixed error that
contributes no text or cursor; the accepted text is kept internally and the
manual retry requests that same offset, appending once after a valid answer.

## Agent-attempt history and evidence inspection

The message-linked evidence and artifact-window routes require a collaboration
message's `AttemptId`, and the six role status routes select only their own
role's latest attempt, so a failed or interrupted historical Agent attempt that
emitted no `ProviderObserved` message had no navigation path to its sealed
output. Three additive, protected, read-only MVC operations close that gap
without changing any provider, workflow, schema, or the message-linked routes:

- **History.** `GET /api/runs/{runId}/agent-attempts` (`GetAgentAttemptHistoryQuery`)
  returns one page of the run's `Kind == Agent` attempts (never Process attempts,
  never another run's) in strictly descending `AttemptNumber`, which the unique
  `(RunId, AttemptNumber)` index makes a total order. `beforeAttemptNumber` is an
  exclusive cursor, so a page boundary is stable while newer attempts are appended,
  and a run may hold more than the current 16-claim default (verified with 45
  attempts). `limit` defaults to 10 and is hard-capped at 20; a non-positive
  `limit` or cursor is a 400 and an unknown run a 404. The handler reads one extra
  row to compute `hasMore` and returns `nextBeforeAttemptNumber` (the last returned
  number) only when `hasMore` is true. Rows carry identity and lifecycle facts only, plus — for an attempt that was
  requested as a manual format repair — `repairSourceAttemptId` and `repairSourceAttemptNumber`, present only when the
  source is proved (an earlier Agent attempt of the same run) and null otherwise; the evidence route adds the same two
  fields. They are provenance and never a claim that the source was fixed.
- **Evidence metadata.** `GET …/agent-attempts/{attemptId}/evidence`
  (`GetAgentAttemptEvidenceQuery`) resolves by the exact `(RunId, AttemptId)` pair and
  `Kind == Agent`; an unknown, foreign-run, or non-Agent attempt is a safe 404
  (`agent_attempts.not_found`). It returns identity, lifecycle, host-measured process
  evidence, provider-reported token evidence, for a concluded Claude attempt the additive nullable `modelContextLimits`
  member (the models Claude listed with their reported limits; see
  [Claude-reported model context limits](agent-collaboration-protocol.md#claude-reported-model-context-limits)), and metadata (purpose, byte length,
  truncation, capture outcome) for only the four allowlisted purposes, filtered by
  the artifact row's independently stored `RunId`, `AttemptId`, and purpose, so a
  cross-run row or a Process-purpose row never appears.
- **Sealed text window.** `GET …/agent-attempts/{attemptId}/evidence/artifact-window/{purpose}`
  (`GetAgentAttemptArtifactWindowQuery`) serves the same closed four-purpose
  allowlist (`context-manifest`, `stdout`, `stderr`, `final-response`; anything else
  is a 404) with the same byte cap and UTF-8 cursor. It delegates to the shared
  `SealedAgentArtifactWindowReader`, which the message-linked handler now also calls
  for its artifact-row filter and `IArtifactStore.VerifyAndReadSealedAsync` read, so
  both routes return identical bytes and statuses (`Ok`, `ArtifactNotFound`,
  `Missing`, `IntegrityMismatch`) and inherit the store's containment and integrity
  guarantees unchanged. Text is never returned unless the whole sealed file first
  verified. Each route still owns how it proves the attempt's identity: the message
  route keeps its `ProviderObserved` and message-actor checks untouched.

**Identity rule.** `AgentAttemptIdentity.IsCoherent` is the single read-side
provenance check for history and evidence: Agent kind; defined status, role,
provider, and response contract; a role/provider pair the product actually launches
(Codex Planner, Resolver, CodeReviewer; Claude Code CriticalReviewer, Implementer);
a response contract that belongs to that role; and a well-formed immutable
assignment snapshot. A history row that fails is still listed by number and times
(with its status when readable), but its role, provider, contract, and outcome are
withheld and it is not offered for evidence (`identityValid: false`); the evidence
route then returns 200 with `identityValid: false` and only the non-enum facts, and
the window route returns the new `AttemptIdentityInvalid` status (only ever produced
by this route) with no text. This is provenance validation only — it is not a claim
about provider capability, account allowance, session resumability, or current
workflow authority.

**Unreadable persisted strings.** Status, role, provider, and response contract are
stored as strings and converted by EF while an `Attempt` is materialized, so a
corrupted or legacy value would throw before the identity rule could run.
`AgentAttemptRead` therefore reads only non-enum columns first (id, number, claim,
completion, and dispatch times) to establish existence, run ownership, ordering, and
the page, then materializes each full row inside a guard that catches any
`InvalidOperationException` raised during full-row materialization (database and
cancellation failures are not caught). The guard cannot prove the exception came
specifically from enum conversion, so it treats the row as unreadable whatever the
cause of that exception type. An unreadable row is handled exactly like an
incoherent one: listed in place with `identityValid: false` and a null status,
evidence `identityValid: false` with a null `attemptStatus`, and the window
`AttemptIdentityInvalid` with no text — never the exception, the stored string, or
artifact content. The other rows of the same page are unaffected. The existing EF
model is unchanged; no raw SQL or schema change is involved, and the guard does not
repair or rewrite the stored value.

**Disclosure.** The metadata routes (history and evidence) and the envelope of a window
response carry no storage path, content hash, provider session identifier, prompt,
adapter contract version, executable path, argument, or working directory, and the tests
assert that none is added as a field. A verified window's `text` is different: it is the
captured sealed content, returned exactly as recorded, so it can contain whatever the
provider or host wrote — including text that resembles a prompt, session identifier, path,
or hash — after only the best-effort redaction applied at capture. The context manifest
is host-constructed content and the other three purposes are best-effort-redacted provider
output that may still contain sensitive text, exactly as for the message-linked route. No route
makes a provider call, opens
a session, replays or retries an attempt, mutates workflow state, or reads a live
partial capture.

**Cockpit navigation and lifetimes.** One read-only "Agent attempt history" region serves the current run in the
usage rail and, as an independent run-level access path, a run selected in Run history
([ADR-0033](../decisions/0033-browse-project-run-history-and-locate-prior-delivery-receipts.md)), beside its receipt,
event headers and collaboration messages; ADR-0037's attribution inspector is unchanged and does not lead here. The
region is collapsed for every committed run and reads nothing until opened; opening reads one page of 10, and older
pages and the retry of a failed page are explicit actions with the host's exact cursor. Evidence is read only for a
selected identity-valid row, and sealed text only after a purpose present in that evidence is chosen and loaded, with
further windows on request. Window requests and acceptance follow the
[shared rule](#sealed-agent-artifact-window-inspection) of both routes: each allowlisted recorded purpose maps to the route's
own segment and nothing is sent for any other value.

A page is accepted only whole and only when coherent for exactly its request and the rows already retained: no more
entries than requested; unique identities and numbers that are safe positive integers, strictly descending, below the
cursor and the last retained row; valid instants; a verified row naming a recorded status (`Running`, `Completed`,
`Failed` or `Interrupted`) and a coherent identity by the identity rule above (a supported role/provider pair and a
response contract that belongs to that role, restated in the cockpit; the Domain is authoritative); an unverified row
carrying no role, provider, contract or outcome; a repair source only as an identity with an earlier number; and a
continuation that is a full page whose cursor is its last number, or none. Anything else fails the whole page; earlier
valid rows stay and the same cursor is read again only on request. The response names no run, so the run is the one
the owning lifetime requested. A selected row carries its identity, number and immutable role, provider and response
contract into the evidence read. Evidence is used only when it names that identity and number, carries an explicit
identity verdict and, when verified, a recorded status, the same role, provider and response contract as the row, a
claim time and distinct allowlisted purposes; its status and times are fresh observations and may have advanced since
the page. Otherwise it is one error and no metadata, purpose or text from it is shown. An unverified row offers no
evidence, and an unverified evidence answer keeps the identity-invalid disclosure.

Reads and selections belong to lifetimes built on the shared owned-lifetime hooks, with no key, remount or reset
effect: the committed run, each opening of it, each selected attempt (another attempt, a return from B to A, or
selecting again after a close is a new selection) and each chosen purpose inside that selection. Another run or
project, closing or reopening the region or the selected run, a Run history reload and unmounting end every
subordinate read, selection, cursor and accumulated text; the replacement derives its empty first frame in the same
render, a late answer settles nothing, and a retained retry, close or next-window callback starts or closes nothing.
The shared sealed-window hook follows the same rule for both routes, beside its window acceptance and same-offset retry. Loading, empty, unavailable, missing, integrity-mismatch,
identity-invalid and error states with manual retry stay explicit, and the purpose-matched caveats and literal text
rendering above are unchanged. Nothing is polled, retried automatically or written to browser storage, a URL or a
log; the historical region mounts no mutation, live, eligibility or configuration control and changes neither the
current project and run selection nor intake.

**Selected-attempt recorded inputs.** [ADR-0051](../decisions/0051-inspect-recorded-inputs-of-a-selected-agent-attempt.md)
adds the protected, bodyless `GET /api/runs/{runId}/agent-attempts/{attemptId}/inputs`, independently of any message the
attempt produced or of its evidence/artifact reads. Unknown runs and missing, foreign or non-Agent attempts share the
fixed `agent_attempt_inputs.not_found` 404. An existing Agent attempt is judged by the unchanged scalar identity and
assignment rules, using the same guarded projection as ADR-0037; no complete entity, message content, prompt, session,
artifact or chain is read. Stored enums are classified before materialization and assignment strings are byte-bounded.

The query reads at most 65 ordered input rows through the existing attempt/sequence index. At most 64 can be inspected;
a 65th yields `Unavailable`, with no count or partial list, not a declaration that the stored set is invalid. Before
showing any prefix it proves the whole inspected set: gapless positions from zero, unique nonempty references, every
message in the same run, a known protocol/type and an exact positive timeline sequence. Malformed stored positions are
refused before integer conversion can hide them. Unexpected database/cancellation and malformed identity/timestamp
storage faults retain the normal fault pipeline.

The API owns the whole response. `Unavailable` has no attempt or count, no references and no omission; `Empty` names a
coherent Planner, with count zero and no references or omission (zero inputs for another role are unavailable).
`Recorded` names the immutable attempt identity/number/role/provider/contract, exact count 1-64 and first at most ten
references; omission is true exactly above ten. References contain only their zero-based input position, message ID,
recognized type, canonical positive Int64 timeline sequence **string**, and invariant UTC round-trip timestamp
**string** (`yyyy-MM-ddTHH:mm:ss.fffffffZ`). No JavaScript numeric sequence or generated Date conversion precedes
validation. This is an inspected prefix, not paging or a complete-history claim.

The selected attempt has a collapsed "Recorded collaboration inputs" section. Opening reads once, retry is explicit,
and plain-text references carry the recorded-input caveat: they are not the complete prompt, context manifest or a
resumable session. Referenced content is not loaded or linked. A facade preserves the parsed successful body for the
whole-answer decoder while URL, authentication and non-200 handling remain generated. The decoder binds the exact
run/attempt and selected immutable header, validates the status shape, counts, order, uniqueness, recognized types,
exact sequences and timestamp text, and copies only admitted fields. A malformed answer is one fixed error with no
partial list. Both selection and each opening use the existing owned-lifetime primitives: closing/reopening,
attempt/run/project changes, A-to-B-to-A, parent reload and unmount discard old work before the replacement's first
frame; late responses and retained callbacks cannot affect successors. Nothing is polled, automatically retried,
logged or persisted in the browser or URL. Existing message-linked input and attribution contracts stay unchanged.

**Complete references and selected input content.**
[ADR-0053](../decisions/0053-inspect-the-content-of-recorded-inputs-of-a-selected-agent-attempt.md) extends the operation
above with an optional `limit` and adds one content read; both share one feature-owned input-set policy
(`Policies/AgentAttemptInputs`) that holds the ADR-0051 proof unchanged. `limit` is absent or one blank value (the ten-reference
default) or one canonical ASCII decimal from 1 to 64; signs, padding, leading zeroes, fractions, exponents, non-ASCII digits,
overflow and any repeated value (even repeated blanks) are the shared `validation.invalid_limit` Problem Details at `#/limit`
with a fixed detail and no rejected value, refused again by the Application validator and never clamped. The whole set is
proved whatever the limit; `Recorded` returns exactly `min(count, limit)` references and omission exactly when the count exceeds
them, so a request without `limit` is the ADR-0051 answer.

The protected, bodyless `GET /api/runs/{runId}/agent-attempts/{attemptId}/inputs/{messageId}` proves the whole set again, then
requires the message to be a member of it. An unknown run, a missing, foreign or non-Agent attempt and a nonmember of a proved
set (including any message for a coherent empty Planner set) share the fixed `agent_attempt_input_message.not_found` 404; an
unprovable, oversized or incoherent set is `Unavailable` with no attempt, input or message, whichever message was requested, and
is never filtered to make a member appear valid. Only then is exactly that message of the same run read through the unchanged
`CollaborationHistoryRows` projection and admission (SQL byte guards 1800/15000 on summary and content, in-memory 600/5000,
classified enums and participants, provenance coherence, safe summary and the closed Domain content policy); no other message's
text is selected. The reread identity, version, type, exact sequence and time must equal the admitted reference, otherwise the
answer is `Unavailable`, never a substitute. The separate reads claim no atomic snapshot or claim-time content attestation.

The API owns the closed graph: root `runId`, `attemptId`, `collaborationMessageId`, `status`, `attempt`, `input`, `message`;
`input` is exactly the selected reference; `message` is `runId`, `id`, `sequence`, `occurredAtUtc`, `protocolVersion`, `type`,
`provenance`, `actor`, `recipient` (`kind`, `role`, `provider`), `contentStatus`, `summary` and `fields` (`name`, `value`), with
the ADR-0035 fixed `Unrecognized` disclosures and bare unavailable content (null summary, no fields). Both sequences are canonical
Int64 **strings** and both times invariant UTC round-trip **strings**. No reply, producing attempt, raw JSON, artifact or session
is returned, and unavailable content neither invalidates the reference nor hides its siblings.

The section requests `limit=64` once per opening, reveals ten references and further loaded references in local chunks of ten
only on request, without a request; local disclosure is labeled apart from host omission and from an unavailable set. Each
revealed reference has an explicit "Inspect input" action into one inspector that reads only the chosen message, with Close and
a failure-only retry. A facade keeps the parsed body; the decoder judges exact keys at every level, the status tuple, the
selected immutable header and reference (position, ID, type, Int64 text, raw time text before any Date conversion) and then the
unchanged history validation of participants, provenance and content schema. Content unavailable, an unavailable set, not
found, an incoherent answer and a read failure are distinct fixed states; fields render as literal text in the closed schema's
order with fixed labels. The opening owns its disclosure and input choice; every choice is a numbered visit decided against the
queued choice, so choosing the current input is a no-op, Close then reselection (even in one batch) starts a fresh visit, and a
retained Close, retry or show-more of an ended visit or opening acts on nothing. The content read is keyed by
`(opening, run, attempt, message, visit)`, so another input, attempt, run or project, A-to-B-to-A, reload and unmount show
nothing of an earlier answer. Nothing is prefetched, polled, retried automatically, linked, exported or persisted.

**Recorded outputs and selected output content.**
[ADR-0054](../decisions/0054-inspect-recorded-outputs-of-a-selected-agent-attempt.md) adds the messages a selected Agent attempt
itself produced, found by the message-level `AttemptId` that result handlers record atomically and by nothing else: no event, reply,
role, number, time or outcome is used, and a host-constructed escalation (which has no message-level attempt reference) is not found
here. Two protected, bodyless MVC GET operations dispatch through `IApplicationMediator`, share one feature-owned reader
(`Policies/AgentAttemptOutputs`) and reuse the unchanged scalar identity evaluation and `CollaborationMessageAuthorPolicy`:

- `GET /api/runs/{runId}/agent-attempts/{attemptId}/outputs` (`agent_attempt_outputs.not_found` for an unknown run and a missing,
  foreign or non-Agent attempt) returns the complete admitted references: no `limit`, cursor or omission.
- `GET /api/runs/{runId}/agent-attempts/{attemptId}/outputs/{messageId}` (`agent_attempt_output_message.not_found` for the same
  cases and for a nonmember of a proved set) reads one admitted member's guarded content.

The reader selects at most 65 ordered scalar message headers whose `AttemptId` is the attempt, through the existing attempt-reference
index, and inspects at most 64; a 65th is `Unavailable` with no prefix or count. It selects by `AttemptId` before the run is judged and
filters nothing: every inspected row must be in the selected run with a unique nonempty ID, a strictly increasing positive sequence,
protocol 1.0, a known type the attempt's role may author, ProviderObserved provenance and an Agent actor whose role and provider equal the
attempt's, and a nonempty set also requires a recorded dispatch time. One inconsistent row makes the whole set `Unavailable` before any
reference or text is returned; zero rows is `Empty` for any admitted role, dispatched or not, and states only that no associated
message was recorded in this read (never that the provider produced nothing, or whether the attempt succeeded). Enums are classified in
SQL before materialization, no summary, JSON, reply pointer or unrelated attempt/message is selected, and unexpected database,
cancellation and malformed identity/timestamp storage faults keep the normal fault pipeline.

The API owns both closed graphs. The list is `runId`, `attemptId`, `status` (`Unavailable`: no attempt or count and no reference;
`Empty`: the immutable header and count 0; `Recorded`: the header, count 1-64 and every reference), `attempt`
(`id`, `attemptNumber`, `role`, `provider`, `responseContract`), `outputMessageTotalCount` and `outputMessages`, each reference being
`position` (the zero-based ordinal in this read's sequence order, not a persisted sequence), `collaborationMessageId`, `type`,
`collaborationSequence` (canonical positive Int64 **string**) and `occurredAtUtc` (UTC round-trip **string**). The content answer is
`runId`, `attemptId`, `collaborationMessageId`, `status` (`Unavailable` withholds the three nullable members), `attempt`, `output` (exactly
the selected reference) and `message` (exactly the ADR-0053 message graph with string sequence and time). The content operation proves
the whole set again, then reads only the selected member constrained to its run **and** `AttemptId` through the unchanged
`CollaborationHistoryRows` projection and rechecks identity, exact sequence, `EqualsExact` time, version, type, provenance and actor
against the proved reference: a missing or disagreeing reread is root `Unavailable`, whereas content refused by the unchanged
admission leaves the proved reference and its siblings (unavailable content versus an unavailable relationship). An unprovable set is
`Unavailable` before membership is decided, even for a stranger message ID. Separate reads claim no atomic snapshot; they are stored
messages attributed to the attempt as read now, not its complete provider response, proof of what was generated or seen, a current
approval or execution success.

The selected attempt has its own collapsed "Recorded collaboration outputs" section beside inputs and verification, independent of the
evidence answer. Opening requests the list once; ten references are shown first and further loaded references in local groups of ten on
request without a request (local disclosure, never host truncation). Each shown reference has "Inspect output N" into one replaceable
inspector with Close and a failure-only retry; no content is prefetched. Operation-scoped facades keep the parsed raw body; the decoders
validate exact keys at every level, the status tuples, the selected immutable header, every position, unique ID, strictly increasing
exact sequence, canonical time text and the role/type table, and then selected-member equality (the message provenance must be
provider-observed and its actor exactly the attempt) before anything is displayed, reusing the unchanged frontend content admission
without Date revival. The existing owned-lifetime primitives scope selection, opening, disclosure and each numbered visit exactly as the
inputs do, including queued Close and reselection, retained callbacks, A-to-B-to-A, parent reload and unmount. Nothing is polled,
retried automatically, linked, followed, exported, logged or persisted.

**Selected-attempt recorded verification evidence.**
[ADR-0052](../decisions/0052-inspect-recorded-verification-evidence-of-a-selected-agent-attempt.md) adds two protected,
bodyless GETs, `/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence` and
`.../verification-evidence/{executionId}/artifact-window/{stream}` (`stdout` or `stderr`), independent of any message the
attempt produced and of its evidence, input and Agent-artifact reads. Both are used for a CodeReviewer attempt of
`ImplementationReview` or `VerificationDiagnosis` only. An unknown run, a missing, foreign or non-Agent attempt and, for a
window, a nonmember execution or unsupported stream share the fixed `agent_attempt_verification.not_found` 404 that repeats no
value. The two operations share one deliberately co-evolving read policy; the unchanged scalar identity rules of ADR-0037/0051
judge the attempt, and nothing else consults a current recipe, latest execution, current checkpoint, eligibility, applicability or
authority reader or reads a membership digest.

The membership is at most 33 scalar `AttemptVerificationEvidence` rows read in stored order through the attempt/sequence
index, of which 32 are inspected: a 33rd, none, or any incoherence makes the answer `Unavailable` with no count and no partial
list, not a declaration that stored data is invalid. Before any member is shown the whole set is proved: gapless positions from
zero, unique nonempty command and execution identities, each execution present by its recorded identity with the recorded
command, project, workspace and checkpoint, the attempt's recorded workspace and checkpoint belonging together to the run's
project, and well-formed equal claim/completion fingerprints of the attempt, checkpoint and every execution. The workspace need
not be active nor the checkpoint latest. An ordinary review admits only Passed/Exited/0; a diagnosis also Failed/Exited/nonzero,
with at least one failure. Stored enums are classified and numbers and strings guarded by the database before conversion can hide
malformed SQLite storage; the command name comes only from the execution's immutable snapshot. These are this inspection's
disclosure bounds, not new write rules.

The API owns the response. `Recorded` names the immutable attempt header, the recorded checkpoint, the exact count 1-32 and every
member: zero-based position, command and execution identity, positive execution number, snapshot command name, Passed/Failed,
Int32 exit code, invariant UTC round-trip claim/dispatch/completion **strings** and two stream descriptors. A descriptor is
`NotRecorded` (no row for that execution and stream), `Unavailable` (metadata that cannot be admitted) or `Recorded` (artifact
identity, safe-integer byte length and either a known-truncation capture with a Boolean or a recovered capture with unknown
`null` truncation, kept distinct); an unusable stream hides neither its sibling nor the members. The list reads no artifact path,
hash or byte. A window re-proves the whole membership, resolves only the requested stream's row, admits its guarded relative
path and exact `sha256:`-plus-64-lowercase-hex hash and passes them unchanged to `IArtifactStore.VerifyAndReadSealedAsync`;
`Ok`, `Unavailable`, `NotRecorded`, `Missing` and `IntegrityMismatch` are distinct, non-Ok answers carry no artifact, text or
observed length, and an offset past the recorded length or outside 0..2^53-1, or a cap outside 64..65536 (default 16 KiB), is a
shared validation refusal that is never clamped. This is recorded membership and referenced rows as read now: it does not prove
which bytes the Agent saw, that a diagnosis still matches its claim-time snapshot, or any current approval.

The selected attempt has a collapsed "Recorded verification evidence" section, independent of the sections above. Opening reads
the metadata once; selecting a Recorded stream, "Load", "Load next window" and retry after a failure are explicit and no read
repeats by itself. A facade preserves the parsed bodies for whole-answer decoders that bind the requested identities and selected
immutable header, closed shapes, counts, positions, unique identities, raw timestamps and capture tuples, and, for a window, the
selected descriptor, its stable artifact and length and the byte cursor (`requestedOffset <= nextOffset <= length`, advance within
the cap, progress while bytes remain, empty text exactly when there is none, `text.length <= advance`); decoded UTF-8 is never
re-encoded to infer consumed bytes. A view retains at most 256 KiB of source bytes per member/stream selection, requests are
capped by what remains and none is sent below 64 bytes; this local view limit is disclosed as such, never as the end of the capture
or the file. Attempt selection, each opening and each member/stream selection visit use the existing owned-lifetime primitives:
close/reopen, another attempt/run/project, A-to-B-to-A, Hide then reselection of the same stream (even in one batch), parent reload
and unmount discard old work before the replacement's first frame, and late responses and retained callbacks, including a Hide of
an earlier visit, cannot affect successors. Output is literal text; nothing is polled, retried
automatically, logged, downloaded, exported or persisted in the browser or URL.

**Selected-attempt execution facts.** The accepted evidence answer of a verified attempt also shows, from that same
answer and with no further read, its response contract, its claim, dispatch and end times, its semantic `outcome`, the
host-measured `processExecution` result, the configured timeout and the provider-reported `tokenUsage` counts, as
separate facts (see [execution evidence](agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome)
and [usage evidence](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)). Each is admitted on its own
before it reaches the shared process and token lines. The generated client coerces any truthy timestamp value with
`new Date(String(value))` and drops falsy ones, so a `Date` alone cannot prove what the host sent. The evidence operation
is therefore read through a handwritten facade over the generated operation (`AgentAttemptEvidenceWireClient`, wired by
its factory only): for a successful answer it judges the raw claim, dispatch and completion values from its own parse
of the body, lets the generated processing parse the same text with a stateless reviver that removes those three root
members (so no raw timestamp, including an object whose own `toString` is not callable, reaches the generated
conversion or can reject the whole answer), then gives each its verdict. A missing or null value stays absent; a string of exactly the `DateTimeOffset`
wire shape (`yyyy-MM-ddTHH:mm:ss`, up to seven fractional digits, `Z` or a `±HH:mm` offset within 14 hours), naming a real
calendar day and time inside the `DateTimeOffset` range, becomes the instant computed from those fields (milliseconds kept);
anything else (a number, boolean, array, object, other text or an impossible date that JavaScript would normalize) becomes
an invalid `Date`, which stays unavailable and is never read as absent. The request, URL, authentication, model
construction and every non-success answer remain the generated operation's own. Only a valid `Date` is an instant here,
the claim time stays mandatory, and a running attempt that names an end time contradicts itself. Dispatch and status
decide first, as in the Domain getters: an undispatched attempt has no measured result or usage, a running one has none yet, and an unreadable
dispatch time trusts neither, whatever numbers the answer carries. For a concluded, dispatched attempt, all members
absent is unknown, never zero. Otherwise the process result needs a closed outcome (`Exited`, `TimedOut`, `Cancelled`),
an int32 exit code exactly for `Exited` and a nonnegative safe-integer duration in whole milliseconds; usage needs
nonnegative int32 input and output counts, each cache count independently absent or one too, with no default and no
derivation between members. The configured timeout is independent of dispatch and measurement and must be a positive
safe integer. Anything else makes only that section unavailable; it is never clamped or invented and leaves valid
siblings, metadata and artifact access usable. A clean exit is not a semantic success, and the counts are not cost,
quota, remaining capacity, readiness or approval. The browser checks shape and range only; the provider schema and
provenance remain the host's proof.

**Recorded local execution inspection.**
[ADR-0061](../decisions/0061-inspect-the-recorded-steps-of-one-local-execution-sequence.md) adds three protected, bodyless GETs under
`/api/runs/{runId}/local-execution-sequence/inspection`: the recorded index, `.../steps/{stepId}` (one selected operation) and
`.../steps/{stepId}/artifact-window/{stream}` (`stdout` or `stderr` of the step's verification). They answer from bounded, untracked
scalar facts only (at most 103 step and 33 pinned-recipe rows, the actual recipe-dependent step bound, stored enums classified, numbers
and text guarded in the database), never invoke an admission, ownership, eligibility, dispatch or recorder handler, and read no
latest or current row, Git, filesystem, process, provider or account state. A corrupt, incomplete or concurrently changed (sequence
revision read before and after) structural set is `Unavailable` as a whole, never a partial index. An unknown run, a missing or foreign
step, a non-verification step or unsupported stream of a window share the fixed `local_execution_inspection.not_found` 404;
`body_not_allowed` and `query_not_allowed` are fixed 400s raised before dispatch (the window admits only the exact ordinal keys
`fromOffset` and `maxBytes`, once each; their values keep ADR-0052's bounds and are refused, never clamped).

A selected Agent step proves the exact linked attempt (reserved/actual equality, Agent kind, run, pinned workspace, source checkpoint
and fingerprint, the contract of the step's kind) and returns only the existing immutable header; the existing explicit attempt
evidence, input, output, verification and sealed-window readers keep owning those reads. A selected verification step proves the
exact execution against its pinned recipe (snapshot name, executable, exact ordered arguments, timeout; the arguments JSON and path
read only within 2 MiB and 128 KiB internal guards, for the selected recipe only) and the recorded status/outcome/time shapes, and
returns ADR-0052's two stream descriptors. Every window re-proves the whole structure and that association before
`IArtifactStore.VerifyAndReadSealedAsync`, reads only the selected stream, and an unproved association (including a coherent step
with no actual link) has a null `executionId` and reads no byte. The frontend uses the shared windowing machine
(`useRecordedStreamWindow`: 16 KiB windows, 256 KiB of source bytes per stream visit) and strict whole-answer decoders.

**Recorded queue admission inspection.**
[ADR-0063](../decisions/0063-inspect-recorded-local-execution-queue-admissions.md) adds a fourth protected, bodyless and queryless GET,
`/api/runs/{runId}/local-execution-sequence/inspection/admissions`, behind the same body/query guard and the same fixed
`local_execution_inspection.not_found` 404. It answers one of four closed statuses: `NotRecorded` (a known run with no sequence),
`NotQueued` (a coherent Immediate sequence with no admission row; an Immediate sequence with any admission is `Unavailable`),
`Recorded` (a coherent queued sequence with its complete nonempty ledger in admission order, plus the stored sequence header) or
`Unavailable` (a present but unprovable root, structure or link, never a partial list or count).

The root, the ledger and every historical fact its proof needs come from **one** provider-neutral EF statement
(`LocalExecutionAdmissionHistoryStatement`): the run, at most two candidate sequences, at most 103 admission rows, and left joins
to each admission's linked step, source checkpoint, report message and pinned recipe and to the sequence's workspace and initial
checkpoint, plus bounded counts of the pinned recipes (33) and recorded steps (103), with the same database-classified enums and
guarded numbers and text as ADR-0061. Because there is no separate binding, ledger or step read and no revision read before and
after, the answer cannot be torn by a transition between reads (a waiting-reason change updates only the admission revision, so a
sequence revision alone could not detect it); it takes no write lock, transaction or retry and writes nothing. The pure proof
(`LocalExecutionAdmissionHistoryProof`) requires the ManualAgent run's one sequence with its pinned project, workspace and initial
checkpoint and fingerprint, a defined admission mode, the recipe-dependent step bound, strictly increasing positive order numbers,
unique gapless ordinals, Domain's closed kind, pass, recipe, predecessor, report and correction-subject shapes, each source checkpoint
in the pinned workspace with its recorded fingerprint, and for a linked step the whole admission/step tuple (sequence, ordinal, kind,
pass, recipe, predecessor, source checkpoint and fingerprint, report and subject). A Waiting admission has no step; Reserved links a
Reserved step with no operation; Consumed links a Claimed or Concluded step with its exact reserved operation link; Unresolved links
the Unresolved no-link step (retained on an ended sequence); Retired has no step (closed while waiting) or its exact Revoked no-link
step. Only Consumed admissions with a Concluded step precede the last, the last must fit the sequence's recorded state, and every
recorded step of the sequence must have been linked by exactly one admission. It consults no live eligibility, ownership,
`SourceHolds` or admission policy and never substitutes a latest row. The order number is the recorded admission order, never an
exact global queue position, and a waiting reason is the last recorded observation with its own time. The endpoint exposes no
reserved operation identity, preparation token, path, digest or message content, and reads no artifact: a linked `stepId` is recorded
structure, and the existing selected-step operation inspection (ADR-0061) remains the only proof of an operation.

The frontend (`localExecutionAdmissionHistory.ts`, `useLocalExecutionAdmissionHistory`, `LocalExecutionAdmissionPanel`) decodes the
whole closed answer, mirrors the host's structural rules and offers the ledger only inside the inspector of a recorded queued
sequence, collapsed until the owner opens it. Opening or reloading reads the ledger only; closing, reloading the inspector, another
run, a return to an earlier run and unmounting end the ledger's request owner, so a late result or retained callback populates,
selects and opens nothing of another visit. A linked-step action delegates to the inspector's own selected-step selection and works
only for a step in that inspector's currently accepted index of the same sequence; otherwise the row says the recorded sequence must be
reloaded, and nothing is searched, selected in its place or read for another run.

**Recorded outstanding queue admission overview.**
[ADR-0064](../decisions/0064-inspect-outstanding-local-execution-queue-admissions-across-projects.md) adds a fifth protected,
bodyless and queryless GET, `/api/local-execution-queue/inspection`, behind the same body/query guard and fixed errors. It lists, for
the whole authoritative database and independent of any project, run or latest row, every admission that is Waiting, Reserved or
Unresolved (including an Unresolved reservation retained on an ended sequence) in strictly increasing recorded admission order, each
with its registered project's identity and name, its run, the stored sequence header and the admission, which must be the proved last
admission of that sequence. The closed statuses are `Empty` (no such admission is recorded, which is neither an idle host, no active
cycle, no owner nor free capacity: a cycle whose latest admission is Consumed, an Immediate cycle and a manual operation are not
listed), `Recorded` (one through 68 entries) and `Unavailable` (any candidate, reference, storage, count, bound or historical-proof
defect, with no partial list or count). The entry's sequence and admission have exactly the ADR-0063 header and row shapes.

Membership starts from the admissions and excludes only a recognized Consumed or Retired state, classified by the database, so an
unknown, null or malformed stored state, an orphan admission or a missing run, project or sequence stays a candidate that makes the
answer `Unavailable` instead of disappearing as an absence. At most 69 candidates are read in admission-number order, so an overflow
is detected and never truncated into an apparently complete list: at most 64 candidates may sit on a sequence that has not ended, at
most four may be held (Reserved or Unresolved), and the order numbers, sequences and runs must be distinct. One provider-neutral EF
statement (`LocalExecutionQueueInspectionStatement`) reads the candidates, each candidate's sequence together with any other sequence of
the same run (so a duplicate is seen), its registered project label (the stored name is bounded to 800 bytes before conversion, then
must be nonblank and at most 200 UTF-16 units; the repository path is never read), the bounded ledger (at most 103 rows per sequence,
and 69 x 104 + 1 rows in all, so join expansion proves itself) and every historical reference. The projection of
`LocalExecutionAdmissionHistoryProjection` is the one ADR-0063 reads per run, extracted without changing that reader, and
`LocalExecutionQueueInspectionProof` runs the unchanged `LocalExecutionAdmissionHistoryProof` over each candidate's rows: the answer
is one database snapshot, with no split read, N+1 enumeration, revision sandwich, write lock, transaction or retry. A recorded deadline
that passed changes neither membership nor stored state, and the read consults no live ownership, eligibility, capacity, Git,
filesystem, process, provider or artifact state and proves no operation; the recorded reservation is bookkeeping, never a live
process or ownership proof or a release action.

The frontend (`localExecutionQueueInspection.ts`, `LocalExecutionQueueInspectionWireClient`, `useLocalExecutionQueueInspection`,
`LocalExecutionQueueInspectionRegion`) decodes the whole closed answer with the same header and admission decoders and mirrors the
host's membership, order and bound rules. The one collapsed region belongs to the authenticated host session, beside the global
recorded-execution limit; opening or reloading performs one GET carrying a real `AbortSignal` and a ten-second bound, closing performs
none, and nothing polls, retries, prefetches or follows the selected project. Each opening, overview visit and selection owns its
callbacks and results: closing, reloading (which discards the prior rows and selection at once, so a failed reload never shows old
rows as fresh), selecting another cycle, a return to an earlier one and unmounting end the earlier lifetime and abort its request, and
a retained callback of an ended lifetime populates and selects nothing. Selecting a row rechecks its exact admission, sequence, run
and project in the visit's validated overview and renders the existing local execution inspector for that run, whose own explicit open,
ledger, step and window actions stay required; the two reads are independent snapshots, not an atomic replay.

**Recorded quarantine release receipt.**
[ADR-0067](../decisions/0067-inspect-recorded-local-quarantine-release-receipts.md) adds a sixth protected, bodyless and queryless GET,
`/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/ownership-release`, behind the same body/query guard and the same
fixed non-echoing inspection 400 and 404 (an absent run, a run without a sequence and a foreign or missing step are one answer). It
reuses the ADR-0065 `LocalExecutionReleaseStatement` (one bounded provider-neutral untracked statement) and
the complete historical proof in `LocalExecutionReleaseProof`, with the additional raw-storage admission described below.
LRI-R1 strengthened the statement's shared projection: every numeric member of the typed intent
snapshot (and the observation and Agent tick members the proof compares) is admitted as an exact integer of its supported range before the provider
can convert it, so a numeric prefix with trailing text, a fraction, plain text or an out-of-range number is Unavailable here (and in the candidate
reading and a replay), never a plausible old value. A nullable numeric fact (the observation, attempt and execution exit codes, the witness, admission and
intent recipe ordinals and the intent semantic exit code) keeps three cases apart: a fresh raw-storage validity flag, computed in the same statement
and never persisted, says each is genuinely NULL or exactly a supported integer, and the proof refuses a row with unsupported nonnull storage as Unavailable
before eligibility, replay or any chain comparison, so it can never collapse into its legal null or read as an absence. The reader does not call the release candidate query or handler, mints no confirmation, takes no clock or
transaction and reads no Git, file, process, provider, account, artifact or current readiness state. The closed answer is the run, the
step, a status and a nullable receipt: `Recorded` is a fully proved existing release (snapshot, terminal result, original host witness
and observation, actual Human event, chronology, exact lifetime and digest); `NotRecorded` needs both receipt associations (keyed by the
step and by the witness) to be absent and a supported proof result (Available or NotEligible), and says only that this read proved no
receipt; every present receipt that fails a proof, including one beside a revoked or unlinked step, and every unreadable, ambiguous,
incomplete or unsupported row is `Unavailable` with no partial payload. The proof orders eligibility before a stored receipt for a step
that names no operation, so the reader decides absence from the same rows and never maps NotEligible blindly to absence. A stored value this
build cannot materialize is Unavailable, never absence. The receipt discloses only the release identity, time and Human event number;
the sequence, mode, step, operation and pinned source; the unchanged recorded result and process outcome; and the distinct physical
facts (witness and observation identity, dispatch and observation times, the exact Int32 physical exit code and nonnegative Int64 tick
strings, zero included). No nonce, host launch identity, ticket, process identifier, path, executable, argument, output, confirmation
version, digest or expiry and no candidate is returned.

The frontend (`localExecutionReleaseInspection.ts`, `LocalExecutionReleaseInspectionWireClient`, `useLocalExecutionReleaseInspection`,
`LocalExecutionReleaseInspectionRegion`) decodes the whole closed answer or none, verifies the run and step, and for a Recorded receipt
the selected sequence, mode, step kind, ordinal, pass, operation family and identity and the pinned source; the structural rules
(family with kind, status with process outcome, no semantic exit code, an observation no longer than its process, dispatch before
observation before release, compared as fixed-width canonical texts at tick precision) are enforced, not just shapes. One collapsed
**Inspect recorded quarantine release** region sits inside the selected-step detail of a proved linked Agent or verification step, so the
current run's rail and the selected historical run share it. Choosing the step reads no receipt; opening or reloading performs one GET
carrying a real `AbortSignal` and a ten-second bound. The step visit, each opening and each read visit own their callbacks and results:
closing, reloading (which discards the earlier answer at once, so a failed reload never shows an old receipt as fresh), another step
visit, a return to an earlier one and unmounting end the earlier lifetime and abort its request, one request is in flight at a time and
releases only its own guard, and a retained callback of an ended lifetime opens, reloads and closes nothing. Fixed copy explains a past
Human decision, the unchanged failed result and ended cycle, that the physical evidence concerns the invoked process only, and that
NotRecorded and Unavailable promise neither a held nor a released owner (the live workspace and capacity readings remain the authority),
including ADR-0066's checksum-only mismatch, which stays Unavailable here while the exact relational grant can still release ownership.
The receipt is relationally consistent within its own statement, not signed history; the separately read index, operation evidence and
live status are not one atomic replay. No release action, candidate query or confirmation handler is mounted in either inspector.
