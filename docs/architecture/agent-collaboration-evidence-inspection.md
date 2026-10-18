# Agent collaboration evidence inspection

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the read-only evidence inspection contracts: an Agent attempt's recorded
collaboration inputs, the sealed Agent-artifact text window with its
containment, integrity and disclosure limits, and the historical Agent-attempt
history, evidence and identity rules. The sections below were moved verbatim
except three mechanical reference repairs; the protocol's original headings
forward here. The cockpit navigation and lifetime paragraphs that close the
attempt-history section, and the window-acceptance paragraph that closes the
sealed-window section, were added later. Per-attempt execution evidence is in the
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
