# ADR-0052: Inspect recorded verification evidence of a selected Agent attempt

Status: Accepted

## Context

The selected-attempt history can show execution facts, collaboration inputs and
sealed Agent artifacts. It cannot show the verification executions that an
ImplementationReview or VerificationDiagnosis attempt recorded as inputs.
Those ordered memberships already exist in `AttemptVerificationEvidence`.
The project verification list selects only its latest 20 executions, and its
output viewer automatically drains windows. Neither is the historical reader
of one attempt's exact membership. The local-delivery receipt (ADR-0032) names
only the passing checks of a completed delivery, not a failed diagnosis.

This additive inspection builds on ADR-0003/0009/0018/0033/0051. It changes no
claim, diagnosis snapshot, review, receipt or authority rule. Acceptance selects
a contract, not implementation, publication or increment completion.

## Decision

**One complete inspection outcome.** A selected CodeReviewer attempt can show
its recorded verification membership and manually read the sealed stdout or
stderr of a member. Ship metadata, bounded windows, refusal states and lifetime
isolation together. Neither a live project list nor the polling output viewer
is used. Existing routes and viewers keep their behavior.

Two protected, bodyless MVC GET operations dispatch through
`IApplicationMediator`, with API-owned response graphs and reproducible NSwag:

- `/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence`
  (`GetAgentAttemptVerificationEvidenceQuery`).
- `/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence/{executionId}/artifact-window/{stream}`
  (`GetAgentAttemptVerificationOutputWindowQuery`), only `stdout` or `stderr`.

An unknown run, missing/foreign/non-Agent attempt, and (on the second route) a
nonmember execution or unsupported stream share one fixed shared Problem
Details 404, `agent_attempt_verification.not_found`, without rejected values.
The window accepts `fromOffset` (default 0) and `maxBytes` (default 16384).
Reject, never clamp, offsets outside 0..9007199254740991 and byte caps outside
64..65536 with shared validation errors. Enforce bounds in Application too.

### Membership and metadata

Apply the unchanged bounded scalar attempt identity/assignment evaluation from
ADR-0037/0051. An unprovable identity or a contract other than CodeReviewer /
ImplementationReview or CodeReviewer / VerificationDiagnosis is `Unavailable`.
The frontend offers this control only for those two accepted selected headers.

Read at most 33 scalar membership rows in stored order through the existing
attempt/sequence index; inspect at most 32. A 33rd makes the inspection
Unavailable without a count or partial list, not a declaration of invalid
stored data or a new claim bound. Zero rows for either supported contract are
Unavailable, never Empty. Prove the entire set before showing any member:
positions exactly 0..count-1, nonempty unique command and execution identities,
each execution present by its recorded identity, and its command identity equal
to the membership. No current recipe, latest execution or neighboring record
is substituted.

Prove ownership from bounded scalar facts of this run, its selected attempt's
recorded workspace/checkpoint and those exact records: the run and workspace
belong to the same project, the checkpoint belongs to that workspace, and every
execution belongs to that project, workspace and checkpoint. The attempt,
checkpoint and execution claim/completion fingerprints must be well-formed and
equal. Require positive Int32 checkpoint/execution numbers. Never require the
workspace to be active, the checkpoint to be latest, a current lease or live Git.

For ImplementationReview every referenced row must be Passed / Exited / 0.
For VerificationDiagnosis each is Passed / Exited / 0 or Failed / Exited /
nonzero, with at least one Failed. Dispatch and completion times must be
present. Unknown enums, contradictory facts, missing/foreign rows or oversized
disclosed text make the entire membership Unavailable. The command name comes
only from the execution's immutable snapshot, admitted when nonblank and at
most 200 UTF-16 units, selected under a 600-byte storage guard. These are this
inspection's disclosure bounds, not new write rules. Classify enums and guard
numeric storage before conversion can hide malformed SQLite values, following
ADR-0051's approach; do not add raw SQL to Application.

The metadata response always contains `runId`, `attemptId`, `status`, nullable
`attempt`, nullable `checkpoint`, nullable `memberTotalCount`, and `members`.
Unavailable has null headers/count and an empty list. Recorded contains the
same immutable attempt header as ADR-0051, checkpoint ID/number, count 1..32,
and all members in recorded order, without pagination or omission. Each member
contains only its zero-based `sequence`, `commandId`, `executionId`, positive
`executionNumber`, snapshot `commandName`, Passed/Failed `status`, Int32
`exitCode`, and claim/dispatch/completion UTC round-trip strings. The outcome
is known Exited from the admitted shape and is not another public field.

Each member also contains exactly two descriptors, `standardOutput` and
`standardError`. A descriptor has `status`, nullable `artifactId`, nullable
`byteLength`, nullable `captureOutcome` and nullable `truncated`. NotRecorded
means no row for that exact execution and allowlisted purpose; Unavailable
means its metadata cannot be admitted. Both withhold all remaining descriptor
fields.
Recorded requires a nonempty artifact ID, a nonnegative safe-integer byte
length, and either CapturedWithKnownTruncation with a Boolean truncation or
RecoveredAfterHostInterruption with null truncation. Preserve false, true and
unknown distinctly. An unusable stream does not hide coherent verification
members or its sibling stream. Resolve at most the 64 allowlisted metadata
rows; unknown purposes are never read as supported streams.

### Sealed windows

Every window re-proves the entire bounded membership and the selected
execution's ownership, then resolves exactly its requested allowlisted
`VerificationOutputArtifact`. It reads no other stream's bytes. Use only
`IArtifactStore.VerifyAndReadSealedAsync`, retaining its existing containment,
whole-file length/hash verification and UTF-8 cursor behavior. Never read a
partial capture or a project/workspace source file. The list reads no artifact
paths, hashes or bytes. The window may read only the requested row's guarded
path/hash internally for the sealed store; neither leaves the host. Admit a
nonblank relative path of at most 1024 UTF-16 units under a 3072-byte storage
guard, and the store's exact `sha256:` plus 64 lowercase hexadecimal characters
under a 71-byte guard; pass the admitted values unchanged to the store's own
proof. Invalid or oversized
storage is unavailable before a file read. Reuse the store unchanged.

The window response always names `runId`, `attemptId`, `executionId`, canonical
`stream`, `status`, nullable `artifact`, `text`, `nextOffset` and `totalLength`.
Statuses are Ok, Unavailable, NotRecorded, Missing and IntegrityMismatch. A
non-Ok answer has null artifact, empty text, unchanged requested nextOffset and
totalLength 0; no observed file length or diagnostic is disclosed. Ok carries
the Recorded descriptor, text, nextOffset and totalLength equal to its admitted
byte length. An offset past that length is a shared validation refusal without
opening the file. Database, malformed identity/timestamp materialization and
cancellation failures keep the normal fault pipeline; no broad catch turns
them into availability states.

### Meaning and cockpit ownership

This is recorded membership and the referenced rows as read now, not proof
that they still match a diagnosis's claim-time snapshot. Do not read or expose
`SnapshotSha256`, recompute it, consult current recipes or call applicability /
authority readers. A seal verifies bytes against the currently recorded
artifact row, not which bytes an Agent saw or whether a review is valid now.
State those limits explicitly; a historical Passed is never current approval.

A collapsed **Recorded verification evidence** section inside the selected
attempt reads metadata once when opened, independently of evidence,
collaboration inputs and Agent artifacts, without a producing message.
It shows fixed Unavailable text or the recorded checkpoint, complete ordered
members and truthful stream capture/truncation disclosures. Only Recorded
descriptors offer stream selection. Explicit Load reads one 16 KiB window;
Load next window appends one window, with a maximum retained 256 KiB of source
bytes per member/stream selection. Cap each request by the remaining view
budget; when fewer than 64 bytes of that budget remain, stop. Disclose this
local-view limit honestly, never as the end of capture or the file. Selecting
another member/stream or closing/reopening starts at zero. Empty sealed
output, missing bytes, integrity mismatch, unavailable metadata and read errors
are distinct. Retry repeats only the failed request explicitly.

Whole-answer admission binds exact requested identities, the selected immutable
attempt header, closed shapes, counts, positions, unique IDs, ranges, supported
statuses, capture tuples and raw timestamp strings before display. Window
admission also binds the selected stream descriptor, stable artifact identity,
length/capture facts and byte cursor: requestedOffset <= nextOffset <= length,
advance <= requested cap, positive progress while bytes remain, empty text iff
zero progress, and text.length <= advance. Do not re-encode decoded UTF-8 to
infer consumed bytes (replacement decoding changes that relationship).

Use existing owned-lifetime primitives for the attempt selection, each opening
and each member/stream selection. Closing/reopening, another attempt/run/project,
A-B-A, parent reload and unmount invalidate results and retained callbacks
before the replacement's first frame. Older/overlapping reads cannot settle a
successor. No automatic paging, polling, retry, browser/URL persistence, logging,
download, export, mutation or new provider/process/Git invocation is added.

## Validation and consequences

Real file-backed SQLite proves bounded/indexed scalar reads, full-set refusal
including defects beyond early members, guarded storage and exact historical
ownership despite newer checkpoints, runs, executions and recipe edits. API
tests prove both routes' full auth/refusal/wire/OpenAPI matrix, no unintended
artifact reads and byte caps. Existing artifact-store containment/integrity/
UTF-8 proofs are reused without changing or renewing platform experiments;
feature tests prove this resolver supplies only the exact admitted sealed row.
Frontend raw-wire, decoder, hook and composition tests cover tuple/range
refusal, literal text, manual byte windows/cap and lifetime replacement.

Extend the authenticated deterministic-provider collaboration journey after
its newer objective: inspect the earlier failed diagnosis and passing ordinary
review, compare all membership metadata with stored rows and requested sealed
stdout/stderr with the fixture's recorded bytes. Inspection starts no
invocation, non-GET request, live output reader or source mutation. Keep
malformed/oversized permutations at SQLite/API/frontend boundaries. No new
shared database fixture, dependency, migration, capture or harness change.

This advances Increment 4's visible verification inputs and Increment 5's
historical evidence access, not full replay, workflow coordination or increment
completion. ADR-0050 remains Proposed, P9 unproven, AI Memory deferred, and
experimental, credential, real-provider and product publication authority
remain unchanged.
