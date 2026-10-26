# ADR-0069: Inspect recorded project verification executions

Status: Accepted

## Context

The project verification panel reads only the latest 20 executions and its
output viewer drains windows automatically. ADR-0052 inspects executions only
through an Agent's recorded review/diagnosis membership; ADR-0061 requires a
local execution sequence step. A manual verification with neither association
can therefore become inaccessible after newer executions exist, even though
its checkpoint and sealed output remain recorded.

VerificationExecution belongs to a project, workspace and checkpoint. It has
no RunId. Time proximity, the current run and a shared checkpoint cannot prove
that an execution belongs to a run. This outcome is project history, never a
new inferred run history or reconstructed review membership.

The planner selects one read-only vertical slice, PROJECT-VER-INSPECT-01,
within existing development authority. It changes none of the contracts or
execution authority of ADR-0052/0061. Listing, selected metadata, manual sealed
windows and their project UI belong together: a list alone leaves inaccessible
evidence, while reusing the live viewer imports automatic reads and live state.

## Decision

Add three protected bodyless MVC GETs, one class/action per operation, through
IApplicationMediator with API-owned transport graphs and generated NSwag:

- `/api/projects/{projectId}/verification-history`
  (`GetProjectVerificationHistoryQuery`).
- `/api/projects/{projectId}/verification-history/{executionId}`
  (`GetProjectVerificationInspectionQuery`).
- `/api/projects/{projectId}/verification-history/{executionId}/artifact-window/{stream}`
  (`GetProjectVerificationOutputWindowQuery`), stdout/stderr only.

No table, migration, event, producer, worker, dependency, execution command or
current eligibility change is needed. Existing project list/output routes,
their polling/draining behavior and every Agent/sequence inspector stay intact.

### Transport

Unknown project, absent/foreign execution and unsupported window stream use
one fixed non-echoing shared Problem Details 404
`project_verification_inspection.not_found`. A present but incoherent execution
is Unavailable, never absent. Use a database membership predicate before
interpreting potentially damaged selected facts.

Reject all bodies before mediator dispatch: positive content length, any
transfer encoding and readable undeclared bytes. Fixed 400 codes are
`project_verification_inspection.body_not_allowed` and
`project_verification_inspection.query_not_allowed`. Admit exact ordinal query
names after URL decoding; unknown, differently cased and repeated keys are
refused. Detail accepts no queries. History accepts only optional
`beforeExecutionNumber`, a positive Int32; page size is fixed at 20. Windows
accept only `fromOffset` and `maxBytes`, with ADR-0052 defaults 0/16384 and
ranges 0..9007199254740991 and 64..65536. Reject rather than clamp; Application
also enforces value bounds. Use the existing safe shared validation mapping
without echoing rejected input.

### History page

Always return projectId, status (Recorded or Unavailable), items, hasMore and
nullable nextBeforeExecutionNumber. Recorded includes a possibly empty page
in strictly descending execution-number order, at most 20 unique executions.
Read at most 21 scalar rows through the existing unique
(ProjectId, ExecutionNumber) index; the extra row establishes hasMore. Its
page-key identity/number must also be admitted. The next cursor is the last
displayed number only when hasMore; otherwise null. Unavailable has no items,
hasMore false and a null cursor; its false is not an assertion of exhaustion.

Each item contains only executionId, positive Int32 executionNumber, snapshot
commandName and claimedAtUtc. Names are nonblank, at most 200 UTF-16 units and
selected under a 600-byte storage guard. Guard and parse UUIDs and UTC stored
times before provider normalization, and admit integer storage before Int32
conversion. Refuse the whole page if a selected row or lookahead key cannot
be admitted; do not filter damaged rows away or return a partial success.
Do not enumerate all executions, materialize entities or read artifact rows,
paths, arguments, executable paths, live status or source files for the list.

Keyset pages are independent recorded reads, not a stable whole-project
snapshot or proof that every stored row is valid. A new execution appears only
after explicit reload of the newest page. Selection is by exact execution ID;
neither a cursor nor an earlier list answer is proof of its metadata.

### Selected execution

Return projectId, executionId, status (Recorded or Unavailable) and nullable
execution. Recorded exposes only executionNumber, commandId, snapshot
commandName, workspaceId, checkpointId/positive checkpointNumber, claim and
nullable completion fingerprints, recorded status, nullable process outcome
and Int32 exit code, claim/dispatch/completion UTC texts, and exactly two
sealed-stream descriptors. Unavailable has null execution.

Prove the exact execution belongs to the project, its workspace belongs to
that project, and its named checkpoint belongs to that workspace and matches
the claim fingerprint. Validate the complete supported Domain status,
outcome, dispatch/completion time, exit-code and completion-fingerprint tuple,
including running, pre-dispatch refusal, source drift, timeout, cancellation,
failure and interrupted bookkeeping. Preserve a legitimate completion
fingerprint different from the claim for SourceChanged. Do not promote any
terminal or dispatched record to physical exit or successful verification.
Times must be nondefault UTC, with claim <= dispatch <= completion whenever
the latter facts exist, compared without losing tick precision. Fingerprints
are exactly 64 lowercase hexadecimal characters under a 64-byte storage guard.

The command identity is a recorded snapshot reference, not current recipe
equivalence. Never require that recipe to be enabled, unchanged or currently
eligible. Do not read its executable, arguments or current configuration.
Nor require the workspace/checkpoint to be active/latest, a live lease, Git
observation, an Agent membership or a local sequence. Later work or another
run cannot invalidate coherent historical ownership.

Use bounded untracked scalar projections, enum classification in the database,
raw byte guards and strict parsing for identifiers/times. Validate every
projected numeric field before conversion, including nullable exit codes and
the valid Int32 extrema; distinguish malformed storage from legitimate null.
Legitimate SQLite INTEGER-affinity conversions stay supported. A fraction,
numeric prefix followed by text, BLOB or overflow must not become a plausible
value or escape as a materialization overflow. Do not add raw SQL to
Application or a broad catch. Real database/cancellation faults retain the
ordinary pipeline.

Build the execution/workspace/checkpoint ownership and status facts from one
bounded relational statement. Metadata artifacts and windows are separate
reads, explicitly not an atomic replay. Re-prove the selected execution on
every window request. There is no revision column, read lock, transaction,
automatic retry or new consistency protocol in this outcome. These proofs are
unsigned consistency of the facts read now, not authenticated history or
resistance to coordinated rewrites.

### Descriptors and windows

Reuse the existing RecordedVerificationOutputAdmission and the unchanged
IArtifactStore.VerifyAndReadSealedAsync boundary. Do not weaken or generalize
the Agent/sequence association proofs to reuse their handlers. New operations
own project association, transport and orchestration; only the already shared
stream admission/storage primitives have shared ownership.

Each descriptor preserves ADR-0052's exact Recorded/NotRecorded/Unavailable
tuple: nonempty artifact ID, safe nonnegative byte length, known capture with
Boolean truncation or recovered capture with unknown truncation. Non-Recorded
withholds the other facts. Resolve bounded metadata per exact execution and
allowlisted purpose, including one extra row to detect ambiguity; a bad stream
does not hide its coherent sibling or execution. Metadata reads no path/hash
or bytes; no live partial stream is a Recorded sealed stream.

For each window first prove the selected execution and then admit only its
requested stream and guarded storage, preserving ADR-0052's path/hash limits,
containment, whole-file integrity, UTF-8 cursor, offsets and byte caps. Read no
other stream or project/workspace file. The response contains projectId,
executionId, stream, status, nullable artifact, text, nextOffset and totalLength.
Statuses are Ok, Unavailable, NotRecorded, Missing and IntegrityMismatch.
Non-Ok has null artifact, empty text, unchanged requested offset and length
zero. Incoherent execution is Unavailable before artifact access. Offset beyond
an admitted length is a safe validation refusal before opening the file.
Only Ok carries the Recorded descriptor and admitted length. No path, hash,
executable, arguments, private process metadata or diagnostic leaves the host.

### Project UI and ownership

Mount one collapsed **Inspect recorded verifications** region in the project
workspace's verification area, outside its current-run controls. Opening reads
only the newest history page. **Older verifications** replaces the page using
its cursor, without background paging or accumulating the entire history;
**Reload recorded verifications** clears selection/content and reads newest.
Select one execution explicitly to read metadata. Selection alone reads no
output. Only an explicit stream selection and Load reads a window; Load next
window appends one window under ADR-0052's 256 KiB source-byte view cap and
manual 16 KiB request policy. Keep unknown truncation, empty output, view cap,
missing file, integrity failure and read error distinct.

Use existing owned-lifetime primitives for project, opening, page visit,
execution visit and stream visit. Close/reopen, page change/reload, project
replacement, A-B-A, remount and unmount invalidate data and retained callbacks
before a successor's first frame. Failed/invalid/Unavailable reads clear prior
success. Bound pending reads with the established ten-second inspector policy;
late or overlapping responses and an old request's finally cannot settle or
unlock its replacement. Retry is explicit and repeats only its failed read.

Decode complete raw-wire answers before generated normalization can hide bad
values: exact keys, requested identity, canonical UUID/time/fingerprint forms,
closed tuples, descending unique page keys and cursor, stable selected identity
and descriptor, and ADR-0052 byte-cursor invariants. Render untrusted values as
literal text. No automatic polling, drain, prefetch, browser/URL persistence,
export, mutation or provider/process/Git/environment call is added.
Bind selected metadata to the indexed ID, number, snapshot name and claim
time. A changed immutable fact is an unavailable read, not a silent replacement.

Label these as recorded project verifications, with no run attribution,
current source applicability, current approval, liveness or release inference.
The inspector does not enable or disable Run or configuration actions.

## Validation and consequences

Real migrated file-backed SQLite must prove empty and multiple pages, exact
cursor boundaries, an older manual execution with no Agent/sequence link,
another project, newer work/checkpoints/runs and changed/disabled recipes,
reopen, all supported result tuples and malformed storage at each conversion
seam. Prove bounded/indexed projections, zero writes and zero external reads;
metadata must open no artifacts. Windows prove exact selected sealed storage,
refusals before byte access and unchanged integrity/UTF-8 boundary reuse.

API proves all three operations' authentication, body/query/refusal/closed-wire
matrix, MVC authorization inventory, OpenAPI and reproducible generated client.
Frontend proves raw-wire refusal, manual paging/windows/cap, literal rendering,
cold closed state, run-independent mounting and lifecycle races.

One announced automatic headed deterministic journey must inspect a manual
execution older than the live latest-20 list, while a newer run/checkpoint is
present, open stdout/stderr, reload and return to a cold closed page. Await
actual responses and compare rendered facts/bytes to fixture evidence. After
setup settles, baseline business POSTs, invocations and named durable facts;
inspection adds no writes or invocations. Describe fingerprint scope precisely:
selected fields/counts are not all-row byte identity; page reload is not host
restart. Keep exhaustive damaged-data cases below the browser boundary.

This advances Increment 5 evidence access without completing any increment.
General recovery, run-attribution schema, final cross-stage evidence summary,
provider qualification and remote publication are separate outcomes. ADR-0050
stays Proposed, P9 unproven, AI Memory deferred and Increments 4/5/6 incomplete.
Frozen profiles, permissions, experiments, grants and evidence remain untouched.
