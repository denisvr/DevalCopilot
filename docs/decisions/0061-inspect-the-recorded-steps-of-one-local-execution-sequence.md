# ADR-0061: Inspect the recorded steps of one local execution sequence

Status: Accepted

## Context

ADR-0060 now coordinates a bounded local cycle. Its status panel shows step
bookkeeping and final identifiers, but cannot open an operation directly from
its step. Run history exposes attempts and messages separately. Understanding
a failed verification before a diagnosis exists, or an interrupted sequence,
still requires correlating identifiers across panels. The next outcome is an
inspectable recorded cycle, supporting Increment 5's evidence and replay work.
It is not a complete run replay or another execution grant.

ADR-0035/0036/0037 and ADR-0051 through ADR-0054 already own message and Agent
inspection, including sealed artifacts and exact recorded input/output sets.
They stay authoritative. ADR-0052's verification windows require a review or
diagnosis membership; they cannot inspect an earlier verification for which
no such attempt was claimed. A sequence-owned verification window closes
that specific gap without substituting a project's latest execution.

The planner selects this read-only outcome within existing development
authority. It changes no execution or quarantine decision and needs no new
owner execution grant. Durable queue admission and process-aware release
remain separately unresolved; no supersession of ADR-0060 is selected.

## Decision

Ship the recorded sequence index, one selected step's operation inspection,
Agent/message drill-down reuse, sealed verification windows and current/run-
history integration together in LOCAL-INSPECT-01. No new table, migration,
event, background worker, provider adapter or dependency is needed.

### Protected operations

Three bodyless MVC GET operations dispatch through IApplicationMediator:

- `/api/runs/{runId}/local-execution-sequence/inspection`
  (`GetLocalExecutionSequenceInspectionQuery`).
- `/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}`
  (`GetLocalExecutionStepInspectionQuery`).
- `/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/artifact-window/{stream}`
  (`GetLocalExecutionVerificationOutputWindowQuery`), stdout/stderr only.

Unknown run, missing/foreign step and a non-verification step or unsupported
stream on the window route return the same fixed shared Problem Details 404
`local_execution_inspection.not_found`, without rejected values. The index
alone returns NotRecorded when a known run has no sequence. Do not turn a
present but unreadable sequence into NotRecorded.

Reject request bodies before mediator dispatch, including transfer encoding
and readable bytes without a positive content length, using fixed 400
`local_execution_inspection.body_not_allowed`. Index/step accept no query keys;
the window accepts only `fromOffset` and `maxBytes` with the existing ADR-0052
validation: defaults 0 and 16384; offsets 0..9007199254740991; byte caps
64..65536. Reject rather than clamp. Application validates bounds too.
Use API-owned response graphs, shared errors and generated NSwag clients.
Query keys are admitted after normal URL decoding with exact ordinal names;
unknown or differently cased keys and repeated values for either window key
are refused before dispatch with fixed 400
`local_execution_inspection.query_not_allowed`. Offset/cap value errors keep
the existing shared validation contract and repeat no rejected value.

### Recorded sequence index

Read only persisted untracked facts; never invoke an admission, ownership,
eligibility or mutation handler. No current lease, latest workspace/checkpoint,
live Git, filesystem, process, provider, account or environment probe is read.

Index response: runId, status (NotRecorded, Recorded or Unavailable), nullable
sequence header and steps. Non-Recorded has null header and an empty list.
Recorded header contains only sequenceId, planProposalMessageId, planAuthority,
workspaceId, initial checkpointId/fingerprintSha256, recordedState, reasonCode,
revision, recipeCount, correctionAllowance, armedAtUtc, updatedAtUtc and
endedAtUtc. It describes stored bookkeeping, not effective live control.
It adds no final approval/commit proof and exposes no preparation owner token.

Read at most 103 step rows and 33 pinned recipe rows. A reachable extra row
or any malformed structural set makes the index Unavailable, with no partial
list/count or invented repair. Enforce ADR-0060's actual recipe-dependent step
bound, 1..32 recipes, unique gapless recipe positions and step ordinals, valid
kind/pass/recipe shapes, one predecessor chain, fixed reason codes and the
reserved/claimed link shape. The implementation is ordinal/pass zero; other
steps have pass 1..3. Claimed/Concluded link their reserved operation; Reserved,
Revoked and Unresolved have no actual operation link. Zero steps is valid.

Prove ManualAgent execution mode, the run/sequence project, pinned workspace/project and initial checkpoint/
workspace/fingerprint relationships. The named plan must be a Proposal of
this run; its content and acceptance semantics are not reconstructed by this
index. Each source checkpoint belongs to the pinned workspace and matches
its step fingerprint. Every named report is an ExecutionReport of this run.
A correction's subject names its earlier linked review/diagnosis attempt of
the matching origin, not an arbitrary Agent attempt. Root/list incoherence
withholds the whole index. This proves reference structure, not provider
obedience, output applicability or a fresh approval.
Never substitute a current recipe, message or checkpoint. Later legitimate
workspace changes or a newer run do not invalidate coherent recorded facts.
Read the sequence revision before and after the bounded structural projection.
A change during that read returns Unavailable with no partial index or automatic
retry; explicit reload may read the later revision. This is not a new read lock
or a claim that independently inspected operation evidence is one atomic replay.

Each index member contains stepId, ordinal, kind, passNumber, nullable
recipeOrdinal/name, recordedState/reasonCode, source checkpointId/fingerprint,
nullable reportMessageId/subjectAttemptId/operationId and preparation/claim/
result-deadline timestamps. The operation identifier here is a recorded link,
not proof of the operation or any outcome. Those require selected inspection.

Only known closed enums, nonempty UUIDs, lowercase SHA-256, positive safe
integer revisions/numbers, valid UTC timestamps and bounded fixed text are
disclosed. Recipe names are nonblank, at most 200 UTF-16 units, selected under
a 600-byte storage guard. Classify enums and guard stored bytes/numeric values
in the database before materialization, following the existing historical
inspectors. Unknown/corrupt data must not become a plausible default. Genuine
database and cancellation faults keep the normal pipeline; no broad catch.

### Selected operation

Re-prove the bounded index identity and exact selected step on each request.
An unavailable structural set gives Unavailable; a step outside that run's
sequence is the fixed 404. Response always names runId, sequenceId and stepId,
status (Recorded, NoOperation or Unavailable), nullable step, nullable agent
and nullable verification. Non-Recorded has null payloads. NoOperation is only
the coherent Reserved/Revoked/Unresolved no-link shape; never guess whether a
reserved operation may have started. The ordinary status/fence retains that
separate authority and quarantine disclosure.

For a linked Agent, prove reserved/actual operation equality, Agent kind, run,
workspace, source checkpoint/fingerprint and the contract matching the step.
Require the defined coherent role/provider/contract identity admitted by the
existing selected-attempt readers. Return only that existing immutable header
(identity, number, role, provider, response contract), plus the step facts.
Do not copy raw messages, artifact metadata or process/usage facts into this
new contract. The existing explicit Agent evidence/input/output/verification
and sealed-window operations independently revalidate and own those reads.
They describe the selected Agent attempt, not a second attestation of the
whole sequence. A linked operation without messages remains inspectable.

For verification, prove exact reserved/actual link, project/workspace/source,
and its pinned recipe identity, snapshot name, executable, exact ordered
arguments and timeout using bounded scalar reads. For the selected recipe comparison only, guard
each stored arguments JSON to 2 MiB and executable path to 128 KiB before
materialization; these are internal read limits, not new write limits or
disclosures. The argument list must also satisfy the existing 64-member and
4096-UTF-8-byte-per-member bounds. Other pinned recipes' arguments/paths are
not materialized. The exact comparison is by literal ordered values.
Validate supported recorded status/process/time shapes and source/completion
fingerprints wherever completion supplies one. Running,
failed, refused and interrupted/quarantined bookkeeping is not promoted to a
clean exit. Return executionId/positive number, snapshot command name, status,
nullable outcome/exitCode, claim/dispatch/completion times and two sealed stream
descriptors. Never expose paths, executable, arguments, hashes or raw output.
An incoherent operation makes this selected inspection Unavailable, not a
different operation or a partial verification. It need not invalidate other
independently selected steps' evidence.

Descriptors reuse ADR-0052's exact Recorded/NotRecorded/Unavailable semantics,
safe lengths, capture outcome and distinct true/false/unknown truncation.
The metadata reads no artifact path, hash or bytes. Read at most three matching
metadata rows per purpose to detect ambiguity; a bad stream does not hide its
coherent sibling or verification facts. A live partial capture is never a
Recorded sealed stream.

### Verification output windows

Re-prove the entire bounded structural set and selected verification/recipe
association on every window request before opening any artifact. Reuse
IArtifactStore.VerifyAndReadSealedAsync's existing sealed-read boundary
and ADR-0052's descriptor, path/hash storage admission, containment, integrity,
UTF-8 cursor, range and non-Ok response rules unchanged. Share only the already
owned stream admission/read primitives when genuinely identical; no generic
inspection gateway or raw SQL in Application. Read only the selected execution
and requested stdout/stderr artifact, never another stream or live partial.

Response names runId, sequenceId, stepId, nullable executionId, stream, status, nullable
artifact, text, nextOffset and totalLength. Statuses are Ok, Unavailable,
NotRecorded, Missing or IntegrityMismatch. Non-Ok has null artifact, empty
text, unchanged requested offset and totalLength zero. An incoherent selected
association, including a coherent verification step with no actual operation,
returns Unavailable with null executionId and without reading bytes. Only a
proved actual association supplies executionId; Ok always has that identity.
NotRecorded describes the absence of the selected artifact, not a guessed
absence of an execution under a reserved UUID. Unknown/foreign route
identity follows the fixed 404. No artifact path/hash or diagnostic is returned.

### Cockpit and lifetime

One collapsed **Inspect local execution** region is available for the current
run and the selected historical run. It reads the index only on explicit open
or reload, selects one step at a time and reads only that selected operation.
It neither pages through unrelated history to find an identifier nor changes
the current run selection. No poll, automatic retry, prefetch of all steps,
cross-lifetime cache, export, background window drain or implicit content read.

Show the plan and recorded report through the existing explicit guarded
message inspector. Reuse/extract the existing selected Agent detail surface
with its expected immutable header, including manually requested input/output/
verification content and artifacts. Preserve those readers' individual
contracts and existing consumers. A verification step owns explicit stdout/
stderr selection, Read and Read next window controls with the existing bounded
window behavior: at most 256 KiB of source bytes retained per member/stream
visit, and no further read when fewer than 64 bytes of that budget remain.
Selecting a step is not selecting a stream or requesting bytes.

Opening, run, sequence/index visit, step, operation and stream own callbacks
and pending/results. Closing, reload, replacement, A-B-A, unmount, retained
handlers and late/overlapping responses cannot act for or populate a successor.
Reload clears the selected operation and content before reading. Unavailable
or malformed answers clear earlier success and never fabricate empty evidence.
Decode exact response identity, discriminated shapes, safe counts and complete
lists at the frontend boundary; render untrusted values as literal text.

Label this recorded inspection, including while the cycle is still advancing:
it is not a coherent replay of every historical instant, process liveness,
current source freshness, a new approval, readiness or execution authority.
An explicit reload may observe a later recorded result. Keep ADR-0060's one
run-owned live control reader and every mutation fence unchanged. This reader
never enables, disables or dispatches a mutation or replaces the live status.

## Consequences

The owner can follow the bounded local cycle and inspect a verification even
when review/diagnosis never started, without matching raw identifiers manually.
The entire inspection outcome is delivered together; no graph engine or generic
run replay is introduced. Existing source, evidence and sealed-file contracts
remain independent boundaries. Missing/corrupt evidence is refused honestly,
not repaired. Queueing, cancellation, release, retry, takeover, native/provider
experiments, automatic approval/commit/publication and complete MVP/release
qualification remain outside this decision.
