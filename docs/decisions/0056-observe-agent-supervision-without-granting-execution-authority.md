# ADR-0056: Observe Agent supervision without granting execution authority

Status: Accepted

## Context

The seven Agent response contracts already have explicit claims, a shared
single-use dispatch marker and role-owned supervisors. ADR-0055 coordinates
only the three read-only planning contracts. Neither an attempt's `Running`
status nor its dispatch timestamp proves current execution. The planning
sequence has observation deadlines, but the other Agent families have no
equivalent current operational disclosure. Process evidence arrives after the
adapter returns; it cannot answer a current supervision question.

The Increment 5 target includes executor leases, heartbeats and a guardian.
Implementing an execution lease, takeover or automatic mutation now would need
additional authority and recovery decisions. Conversely, another historical
browser would not identify a currently stalled host operation. There is a
useful common boundary across all existing Agent contracts: observation of the
host's own supervision, without making that observation execution authority.

## Decision

### One complete operational observation slice

AGENT-SUP-01 adds durable current supervision observations for all seven Agent
response contracts, including their existing repair paths, manually requested
attempts and ADR-0055 steps. It changes no role assignment, provider permission,
claim, budget, dispatch, result, source, repository lease or approval rule.
ADR-0009, ADR-0014 and ADR-0055 retain their authority and sequencing boundaries.
This is a partial operational-evidence capability, not the complete Increment 5
executor lease or autonomy guardian, and completes no increment.

The observation says which host observer last reported which phase of one
already claimed attempt. A heartbeat proves only that the host observation
loop reported; it proves neither provider progress, a live child process,
useful work nor an absence of external effects. No new surface may call this
evidence `Working` or autonomous `Running`.

### Receipt and writer identity

Add one feature-owned `AgentSupervision` record per observed Agent attempt,
with a unique restrictive AttemptId relationship. Store the exact run/attempt
association, a host-instance identifier generated once per host launch, a fresh
observer identifier for this supervision invocation, a revision, start and last
heartbeat times, current phase and its absolute deadline, optional first
attention reason/time and optional closure reason/time. Identity fields are
immutable; only the matching original observer in the original live host may
advance phases or heartbeat an open record. An identical registration replays;
a competing identity never replaces or adopts it. These identifiers are
correlation facts, not credentials, leases or dispatch tickets.

Registration validates the fresh coherent Agent attempt/run relationship and
its nonterminal status. No historical record is backfilled. A missing receipt
means `NotObserved`, never a synthesized heartbeat. Persist observations through
feature-owned mediator commands and the existing DbContext/short serialized
write boundary. EF configurations, constraints and an additive migration own
relational coherence, revision concurrency and immutable identity. Retain
records for the run lifetime; this slice adds no deletion or purge.

Material observation transitions and their bounded, fixed-schema run events
commit atomically. A heartbeat updates the receipt without appending a timeline
event on every tick. SignalR remains a post-commit hint. Event payloads contain
safe identifiers, phases and reason codes only, never objective text, paths,
process identifiers, invocation arguments or provider output.

### Observation phases, freshness and deadlines

The closed phases are `Preparing`, `AwaitingAdapterResult` and
`RecordingResult`. Registration happens at entry to each supervisor's existing
per-attempt execution, before Git/account preparation. AwaitingAdapterResult is
recorded immediately before its existing adapter call, after the ordinary
dispatch gate succeeded. It does not claim that a process started. RecordingResult
begins when that call returns or throws and includes the existing post-source,
artifact and result-recording work. A refusal before invocation closes the
observation from the actual durable attempt state when available. All early
returns, exceptions and host cancellation end the observation loop in finally.

Use one Domain-owned policy: a five-second heartbeat interval, staleness at
thirty seconds since the last committed heartbeat, two minutes for Preparing,
the attempt's actual reserved invocation timeout plus two minutes for
AwaitingAdapterResult, and two minutes for RecordingResult. Each phase deadline
is fixed at phase entry, in checked arithmetic; heartbeat never moves it. A
phase may move forward only, once, and cannot replay to extend a deadline.
At the exact deadline/staleness boundary the observation is stale. Invalid,
overflowed or future/backward-clock chronology is unavailable, never fresh.
Use TimeProvider and monotonic timer scheduling; UTC facts are not event order.
Before accepting a heartbeat or a phase advance, evaluate staleness and the
previous phase's deadline against the fresh stored receipt under the write
boundary. Record any first attention before updating its heartbeat/phase. A
late writer cannot erase a gap merely because it beat the independent sweep.

An independent bounded host sweep observes open receipts even while a role
supervisor, Git preparation, adapter or result recorder is blocked. Its
bounded inventory must eventually visit every open receipt, without starvation.
It records the first fixed attention reason once: stale heartbeat, phase
deadline elapsed, observer returned without a durable terminal result, or host
interruption. The read projection applies the same policy immediately before
the sweep persists it and distinguishes effective from recorded attention.
Healthy heartbeats cannot mask a phase deadline. Later phases or heartbeats
cannot erase attention. Observation closure retains its first attention facts.

An actual terminal Attempt takes precedence in the current display and is
shown with its real status/outcome; a late durable result remains valid only
under its unchanged result contract. Do not turn a timely terminal result into
a late execution failure because the sweep observed it later. A receipt left
open when its worker returned cannot imply that supervision continues. Close
from a fresh durable read, not from a successful method return or a local
assumption that result persistence committed.

### Observations never control execution

The receipt and sweep never claim or dispatch an attempt, retry an invocation,
cancel a process, mark an Attempt terminal, alter a Run or workspace, refund a
budget, release ownership, approve a checkpoint or admit another writer. The
existing attempt/dispatch/recovery rules remain the only authorities for those
facts. In particular, an attention observation does not prove that an earlier
authorized operation stopped: it may still produce effects and finish later.

Observation failures are bounded and fail closed for disclosure: no successful
supervision status is invented. They must not skip the original operation's
safety gates, discard a known result or prevent the original result recorder
from running. Registration/phase/heartbeat/closure writes use independent short
scopes, not the role's tracked DbContext. An observation error is not an Agent
outcome and never becomes provider failure. Repeated heartbeat attempts are
observation only, never a retry of the underlying work. Do not swallow an
original cancellation or exception while disposing observation resources.
Bound each observation call and disposal wait to two seconds, independently of
the underlying operation's token. Allow at most one pending heartbeat write per
observer, with no accumulating timed-out tasks or queued ticks. Late writes
remain revision/identity/closure fenced. An unavailable observation may lose
detail, but never holds the original safety/result path indefinitely.

Use one small host-owned observation-lifetime collaborator across the seven
supervisors: those consumers intentionally share this observation policy. Keep
their eligibility, preparation, invocation, parsing and result ownership
separate. Do not merge the supervisors or change provider request/result
contracts, CLI arguments, schemas, process transport or artifact delivery.

After the existing startup artifact/attempt reconciliation and before hosted
workers start, close previous host observations as interrupted, preserving
their facts and existing attempt decisions. Never adopt or renew an old host's
receipt. A current read also rejects old-host freshness before persistence.
Preserve existing startup failure behavior and side-effect-free compositions;
no background observation work runs in OpenApiGeneration or the existing
side-effect-free test/qualification compositions.

### Protected current-state surface and cockpit

Add one protected, bodyless, queryless
`GET /api/runs/{runId}/agent-supervision`, through one mediator query. Unknown
runs return non-disclosing 404. It selects the unique running Agent attempt,
otherwise the latest recorded Agent attempt by attempt number. Multiple active
Agents or incoherent relationship/contract facts yield `Unavailable`, never an
arbitrary winner. Simulated/Process attempts do not acquire Agent observations.

The bounded, API-owned response graph has a closed status vocabulary:
`NoAgentAttempt`, `NotObserved`, `Observed`, `NeedsAttention`, `Ended`,
`Unavailable`. It discloses the selected exact attempt identity/number,
role/provider/contract, actual recorded status/outcome and dispatch/completion
facts; an admitted receipt's observer identities, phase, heartbeat/deadline and
closure; fixed reason/next-action codes; effective versus recorded attention;
and the host observation time and revision. No collection, payload, path,
credential, transcript or live process claim is returned. Nullability and
cross-field relationships are closed and validated as a whole. Use canonical
UTC round-trip strings and decimal-string Int64 values, with an operation-scoped
raw-wire decoder when generated revival loses those guarantees.

The current cockpit owns the read and a bounded five-second refresh while the
selected Agent attempt is nonterminal; it also refreshes on durable hints.
Coalesce reads, stop timers on unmount/selection change, ignore stale A-B-A and
remount responses, and use no browser persistence. Loss of transport freshness
shows unavailable/stale evidence, never a locally renewed heartbeat. Disclosure
is fail-closed while a response is unavailable; each read has a ten-second
timeout so a hung request cannot keep an earlier success current indefinitely.
Show safe inspection/reconciliation guidance; offer no new retry, resume, cancel or
takeover action. Observation status is not a manual-action eligibility input.

For ManualAgent/Legacy Agent runs, the header and new current supervision
region describe the recorded lifecycle/role and the observation separately.
Never derive live activity from lifecycle,
a dispatch marker, an unexpired observation or a heartbeat. Retained duration
is lifecycle elapsed time, not measured Agent activity. Keep simulation visibly
separate and preserve ADR-0055's arming, stop, deadline, manual-control fence and
final-plan identity. Neither a fresh observation nor attention overrules the
sequence's own state. Preserve ProviderRuntimePreflight's version-only evidence
and unknown controls; this slice does not implement the target model/context/
permission runtime strip or new active-card machinery. Historical evidence
readers and collaboration cards keep their existing contracts.

## Alternatives considered

- Full local coordination/scheduling: still requires explicit mutation grants,
  verification-set selection, correction routing and recovery policy. This
  continuation grants none of those; observations cannot supply them.
- A renewable execution lease with automatic expiry/reassignment: deferred.
  Lease expiry cannot prove process death or release a possibly mutating writer.
- Provider-process liveness callbacks: deferred because they change the shared
  process/adapter contract and still do not prove useful Agent progress.
- Planning-only observation: rejected as unnecessarily fragmented. All seven
  contracts share Attempt identity and the same observation semantics; the
  three-role coordinator already has its own separate deadlines.
- A frontend timer or in-memory-only status: rejected as authoritative evidence.
  Neither survives disconnect/restart or exposes the host's last committed fact.
- An independent heartbeat without absolute phase deadlines: rejected because
  its timer can remain healthy while the underlying work is blocked forever.

## Required evidence and limits

Prove all seven production supervisor compositions, including early refusal,
repair and coordinated paths, without real providers. Use file-backed SQLite
and independent connections for identity/revision races, rollback, reopen and
startup. Prove exact deadline boundaries, fresh-heartbeat/hung-phase cases,
blocked preparation/adapter/recorder observation, stale/foreign writers,
late/timely terminal results, failed/ambiguous result persistence and observation
faults while the original safety/result paths still execute. Assert unchanged
provider cardinality, budgets, workflow facts and writer exclusion.

Prove MVC authentication/body/query/error/wire contracts, frontend complete
decoding and owned lifetimes, and one authenticated existing planning journey
showing the new observations from deterministic executables without additional
stage requests. Compiling negative controls target a deadline masked by fresh
heartbeats, a foreign observer update and a stale UI response; no broad campaign.

Keep prior evidence losses and unexplained failures. No real-provider fidelity,
live provider progress, execution lease, full guardian, completed local loop,
remote publication, P9, provider-account/session/context gap or MVP completion
is established by this slice. ADR-0050 remains Proposed and AI Memory deferred.
