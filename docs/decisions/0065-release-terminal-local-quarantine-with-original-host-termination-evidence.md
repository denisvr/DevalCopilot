# ADR-0065: Release terminal local quarantine with original-host termination evidence

Status: Accepted

## Context and owner decision

LOCAL-QUEUE-OVERVIEW-01 is independently verified published as 569c6b1.
The bounded local cycle, queue and recorded inspectors are delivered; a linked
dispatched operation without ADR-0060's admitted Exited tuple can still hold its
workspace and global mutating capacity indefinitely after a terminal result.
The adapter's existing timeout/cancellation path can observe process exit after
its termination request, but returns TimedOut/Cancelled with no semantic exit
code. Reclassifying that result as Exited would corrupt verification/review facts.

On 2026-10-23 the owner explicitly authorized architecture resolution and
implementation selection for Human-confirmed release of a terminal local
operation's quarantine, only with durable termination evidence from its original
executing host. Missing or ambiguous evidence retains the hold. The grant excludes
reopening results/cycles, cancellation, retry/resume/takeover, release of Unresolved
pre-claim reservations, automatic approval/commit/publication and renewed native,
profile, credential or real-provider experiments. Exited remains proof about the
invoked process only, not every descendant.

Select LOCAL-QUARANTINE-RELEASE-01 as one outcome: capture that distinct physical
fact, preserve its original dispatch provenance, confirm an exact Human release
and apply it consistently in ownership and fences. This narrowly supersedes
ADR-0060/0062's absence of supported release for this case and extends
ADR-0057/0058/0059's owner classification only. All other authority stays unchanged.
ADR-0056 observation freshness/heartbeat never becomes a release proof.

## Decision

### Three distinct durable facts

Add three feature-owned relational facts, retained for the run lifetime:

1. A dispatch witness, created atomically with the existing single-use dispatch
   marker, only for an exact operation linked to a local execution sequence.
2. A process-termination observation, recorded by that witness's original host
   from its actual returned process result, atomically with the ordinary result.
3. A Human ownership-release receipt and its one Human RunEvent, committed
   atomically only after fresh revalidation of the complete chain.

Witness, observation and release are append-only; no update, delete, overwrite,
replacement, backfill or runtime seeding. Each observation and release names one
unique witness. Use restrictive typed foreign keys for Agent and Verification
operations (exactly one family), unique step/operation/witness/event associations,
closed versions/vocabularies and exact stored numeric constraints. No generic
recovery ledger, repository, outbox or settings system is added.

The witness pins the sequence/run/project/workspace, step and full step tuple
(kind/contract, ordinal/pass/recipe membership, predecessor, source checkpoint/
fingerprint, report and correction subject), actual/reserved operation identity,
queued admission identity when applicable, committed dispatch marker/time,
original host-launch identity and a private single-invocation nonce.
The step must be uniquely claimed and linked; a queued admission is exactly
Consumed, never a pre-claim reservation. Cross-family UUID collisions, duplicate,
foreign or incomplete links refuse the witness/release; UUID equality alone
does not prove an association.

Create a feature-owned host-launch identity and an opaque in-memory dispatch
ticket. Do not repurpose AgentSupervision as authority. Both marker operations
return a ticket only after their ordinary commit succeeds; all source/account/
workspace/capacity/deadline gates remain. A refused or uncertain commit returns
no usable ticket and invokes nothing. No later tick/host reconstructs a ticket.
Nonlocal/planning/manual operations retain their old behavior and create no
witness. No client, provider reply or environment variable supplies these facts.

### Physical termination is independent of result success

Extend ProcessExecutionResult with optional host-produced termination evidence:
the observed physical exit code and nonnegative monotonic elapsed duration at
the observation. Populate it only after WaitForExitAsync of the actual owned
Process instance successfully completed, either normally or in the existing
bounded post-timeout/cancellation wait, and its exit code was obtained.
Keep the existing timeout, cancellation, process-tree request and wait duration;
add no wait, poll, termination request, PID lookup, attachment or background drain.

A launch/adapter exception, unsuccessful post-kill wait, missing result, stream
EOF, elapsed deadline or heartbeat supplies no such proof. Do not infer it from
Outcome, a terminal enum, process absence or a Human assertion. Losing a result
or its recording still leaves unsupported quarantine; no asynchronous late-exit
collector or exception-recovery campaign is selected.

TimedOut/Cancelled and their null semantic ExitCode remain unchanged even when
physical termination was observed. AgentProcessEvidence forwards the optional
fact from the process result; IsCleanExit, Domain process-result invariants,
verification pass, review, final approval and coordinator classification do not
use it as successful Exited evidence. Provider stdout/JSON is never its source.

Propagate the optional fact and original ticket through the four local Agent
supervisors (five Agent step kinds) and VerificationExecutionSupervisor (the
sixth kind) to their
ordinary result recorders. The original host must still possess the ticket and
match the durable witness and dispatch marker under the result transaction.
Store one observation with its witness, physical fact and host recording time
only in the same commit as the actual terminal result. Its timestamp is a host
recording time, not an exact process-exit timestamp. Missing optional evidence
preserves existing result recording; a supplied contradictory/foreign observation
refuses the transaction, never downgrades into trustworthy absence.
Require nonnegative observation duration no greater than the returned process
duration, recording time not before dispatch, and no contradiction with an
ordinary Exited result's exit code. Nonlocal result paths persist no new fact
and retain their existing validation and behavior.
Retain the matching returned process outcome, semantic exit code and duration
in the observation for durable revalidation, distinct from its physical exit
code/duration; they agree with the operation's stored process tuple where present.

A second host cannot generate an observation, import partial output or complete
an old operation. A previously committed coherent observation may be read after
restart for a new explicit Human release; this adopts no process or result.
Existing rows have no retrofitted witness/observation and gain no release path.

### Exact eligibility and one Human mutation

Only an operation of a ManualAgent local sequence recorded Stopped,
NeedsAttention or Interrupted may be released. Require its recognized immutable
terminal result, dispatch, unique full link and source tuple, complete coherent
original-host observation, and absence of any previous release. A Claimed or
Concluded linked step is allowed; Reserved/Unresolved preparations, nonterminal
operations, live sequences, foreign/unknown/malformed facts and observations
without original dispatch provenance are ineligible/unavailable. This does not
repair corrupt authority or terminalize a still-Running row.
Validate terminal result/process consistency under the existing result policies,
not merely membership in a terminal-status vocabulary.
The terminal operation must actually retain ADR-0060 quarantine because its
ordinary semantic Exited proof is absent. A normal Exited operation already
released by the existing rule is NotEligible, not a new Human release. Validate
an existing receipt for replay before this first-release eligibility check.

Require exact historical workspace/source identity, not current readiness,
latest checkpoint, live Git fingerprint or a renewed lease. A failed mutating
operation can legitimately leave NeedsAttention or changed source. Release
changes none of those facts; every subsequent entrance rechecks its own gates.
ReadOnly releases remove workspace exclusion only; mutating classification is
Domain-derived and never client/stored-effect input.

Provide a versioned canonical confirmation of the entire witness/observation,
terminal result, sequence/step/source and admission tuple. Include a UTC expiry
with at most two minutes remaining; recompute from fresh facts under the final
write lock and reject stale/mismatched/expired candidates without retargeting.
This digest is an exact-intent check, not a credential or process proof.

A successful explicit command appends one immutable release and one appended
RunEventType.LocalExecutionOwnershipReleased, Actor Human, with fixed bounded
metadata. Require the actual matching event header (identity, run, sequence,
type and actor) and unique association; payload is audit data, not authority.
Record no free-text rationale, token, PID, executable, path or environment.
An identical normalized request replays its coherent receipt, including after
expiry, with no write/event; a changed tuple conflicts. An invalid stored release
is Unavailable, never a successful replay or silent new release.

Update no operation outcome, verification pass, artifact, sequence, admission,
workspace status, lease, approval or budget. Ended cycles never resume or gain
successors. Removing this hold may allow ALREADY authorized queued work to pass
its own fresh gates; it grants that work no new authority and promises no start.
The dedicated release command may write its own proved metadata while the hold
exists; do not grant any other command a manual-fence exemption.

### One effective-ownership definition, including database fences

Keep unresolved ordinary rows owning. For a recognized terminal linked operation,
ADR-0060's ordinary Exited proof OR this exact coherent Human release can remove
its retained quarantine. Keep LocalExecutionExitProof's semantic meaning unchanged;
release is a separate alternative for ownership only.

Use the same fresh database-side relational release proof in shared workspace,
capacity, manual-control, abandonment and limit-lowering consumers and their
final/dispatch gates. Count each owner once. A receipt ID, observation alone,
Human event alone or client flag cannot subtract an owner. Malformed/foreign/
duplicate release chains produce Unavailable, never zero. ReadOnly never consumes
a global mutating unit; all unrelated owners/reservations remain counted.

Add an additive migration for the three tables/guards and narrowly recreate the
affected SQLite live-fence triggers with the same complete release predicate.
Do not edit historical migrations or weaken queue, claim/dispatch, reservation,
source/settings or result-checkpoint guards. The SQL guard and Application
predicates must agree at the write seam; tests include direct guarded writes.

Upgrade/reopen changes no historical grant/result and backfills nothing. Down
aborts on a named CHECK before changing any table, guard, row or migration history
if ANY new witness, observation or release exists. Empty-feature downgrade restores
the prior guards and removes only the new tables. This deliberately prevents
downgrade from discarding used provenance or Human decisions; no purge is selected.

### Protected API and current-run control

Add two authorized MVC operations through IApplicationMediator:
GET and POST
`/api/runs/{runId}/local-execution-sequence/steps/{stepId}/ownership-release`.

GET is bodyless/queryless with the existing strict inspection guard. POST accepts
no query and exactly four required canonical strings: observationId,
confirmationVersion, confirmationSha256, confirmationExpiresAtUtc; bound the
body to 512 bytes, reject duplicate/unknown/escaped member names and invalid
shape before dispatch. Keep canonical transport/error conventions and fixed
non-echoing feature errors: 400/413/415 structural failures, 409 stale/ineligible/
unavailable confirmation and fixed 404 for absent/foreign run/step.

GET obtains its bounded historical identity, operation, witness, observation,
release and event header in one provider-neutral untracked statement. Keep the
existing two-root and 103/33 historical bounds and guard stored enums/numbers/
bytes before conversion. No external or current eligibility read. If EF translation
prevents a coherent shape, challenge before weakening the contract. POST re-reads
untracked under the existing short SQLite write boundary and performs no I/O.
Keep the repository's accepted typed GUID/time and monotonic-clock conventions
and their qualifications; no repository-wide storage-reader refactor is selected.

Use an API-owned closed graph: runId, stepId, status, reasonCode, candidate, release.
Statuses are NotEligible, Unavailable, Available and Released. Only Available has
a candidate/confirmation; only Released has a receipt; other states have neither.
The candidate names the proved sequence/source/operation kind and identity,
terminal result and process outcome, observation and physical termination facts,
and confirmation. The receipt identifies the released operation, release ID,
time and Human event sequence. Expose no ticket/nonce, host-launch token, PID,
path, arguments or provider output. Use canonical positive Int64 strings for
counters/event sequences and UTC round-trip times. Observed duration ticks are
canonical nonnegative Int64 strings (zero is valid), and physical exit code is
an exact Int32. POST success/replay uses the Released graph.

Mount one explicit collapsed recovery control beside the current run's local
sequence controls. Select exactly one linked step from that run's bounded
recorded list; selection/reload/expiry ends the old confirmation. Read only on
explicit open/selection/reload, never polling. Confirm once with a real AbortSignal,
ten-second transport bound and no automatic mutation retry. Unknown/timeout
feedback acknowledges an uncertain commit and requires an explicit fresh GET.
It must not infer rollback or resubmit. Session/run/opening/step/candidate/mode,
A-B-A, overlap, unmount and retained callbacks cannot act for another visit.
Submission consumes that confirmation; an older GET or late POST cannot restore
it or a newer visit's state. Do not optimistically mark any shared live fence,
workspace or capacity reading as released/available from this panel.

Explain physical termination versus failed result, the invoked-process-only
limit, unchanged workspace/source gates and the possible progression of already
authorized queued work. Update categorical live-status recovery wording
truthfully; missing evidence still has no supported recovery action. The one
existing live control reader remains the sole start fence and refreshes through
the committed event, alongside existing workspace/capacity readers.

Keep existing run-history/global/step inspectors read-only. This first release
control targets the current manual run, which remains blocked from abandonment
while it holds quarantine. A historical API request still needs the same proof;
no historical mutation navigation system is selected.

### Validation and limits

Ship producer provenance, physical observation, exact Human command, all shared
ownership/fence consumers and truthful UI together. A receipt-only slice cannot
demonstrate safe release; a command-only slice would have no admissible proof.
No executor lease, heartbeat authority, force release, reservation recovery,
new cancellation/wait/retry/resume/takeover, dependency or real-provider/native
qualification is included. ADR-0050 stays Proposed; P9 unproven; AI Memory deferred
and Increments 4/5/6 incomplete. Frozen evidence and residual limits remain.

Prove normal/timeout/cancel and absent physical evidence through owned process
fixtures; ordinary result semantics and every adapter remain unchanged. Real
file-backed SQLite and independent connections cover exact dispatch/result
atomicity, both modes/families/all six kinds, uncertainty/no ticket reconstruction,
full malformed/foreign chain, terminal-only eligibility, replay, one event,
restart and old rows, upgrade/reopen/down refusal, every admission entrance and
both orderings of release versus a competing claim/limit change. No fabricated
proof, process re-invocation, output import, budget refund or reopened cycle.

Protect API auth/strict transport/error/closed wire; frontend exact decoding,
actual abort and every confirmation lifetime. One authenticated real-host owned
fixture journey must show terminal quarantine still holds AFTER physical evidence,
Human confirmation alone releases it, no result/cycle is rewritten, and already
authorized independent queued work uses its own gates afterwards. Hosted tests
cover the other family, ReadOnly and failure/restart permutations. No production
test hook is allowed.

Four compiling controls: omit Human release from effective ownership; omit a
required observation/witness identity comparison; remove an ended confirmation
owner guard; remove the used-feature downgrade refusal. Each must kill a named
permanent regression. If an independent defense masks a proposed mutation,
report the surviving scope and challenge with a compiling equivalent alternative;
never count a no-op/compile failure as a kill or weaken production for the control.
Retain failures, restore exact hashes, touch and rebuild. The handoff supplies
the final matrix and stop gates. Owned-fixture recovery tests are selected
validation; no recovery of the owner's real runs is authorized. Return unstaged,
uncommitted and unpushed.
