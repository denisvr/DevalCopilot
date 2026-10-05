# ADR-0025: Stop new Codex attempts at an explicit account-usage percentage

Status: Accepted

## Context

The host can already refuse a new attempt when locally recorded, provider-reported token activity has reached a human-set threshold
(the token-activity stop), and it can display a read-only snapshot of the Codex account allowance. The display is deliberately not a
guard: it is a projection for a person, it tolerates partial evidence, and it is never consulted when an attempt is claimed or
dispatched. A human who wants the run to stop starting Codex work once the account has used a given share of a reported window had no
way to say so.

## Decision

**One optional, run-scoped threshold.** A Run carries a nullable integer from 1 to 100 (`CodexAccountUsageStopPercent`). Null means the
feature is disabled and adds no allowance read. It is set or cleared through `POST /api/runs/{runId}/codex-account-usage-stop` with a
strict body `{ "percent": integer | null }` bounded to the established 8 KiB MVC request-body limit (proven through real Kestrel, before
any write); the change is a protected, concurrency-checked authority change that records one human event. The column uses the exact-stored-text integer mapping, so a tampered storage class or a non-canonical value is detected on read,
is never treated as disabled, and refuses new Codex claims with a fixed setting-invalid error until the setting is set or cleared. The
value is snapshotted immutably on every new Codex attempt (`AgentCodexAccountUsageStopPercent`); later changes apply only to later
claims and historical rows stay null.

**Scope.** Codex planning, challenge resolution, code review (and re-review) and verification diagnosis, including their format
repairs. Claude work is untouched. Every existing claim and dispatch check is preserved.

**A strict observation behind a provider-neutral port.** `IAccountUsageObserver` (an Application port in `Runs/Ports`, with its owned
facts `AccountUsageObservation`, `AccountUsageBucket` and `AccountUsageWindow`) returns an immutable observation, or the single answer
`Unavailable`. The Codex-specific policy, claim gate and guard facts live in `Runs/Policies`. The Infrastructure adapter reuses the vetted launch target and the
read-only App Server session (`initialize`, `initialized`, `account/rateLimits/read`) but is deliberately separate from the
display-only allowance adapter and projection, which remain unchanged. It is all-or-nothing: a duplicated member, a missing or
invalid window, a non-integer or out-of-range `usedPercent`, an invalid optional duration or reset, an unsafe or oversized bucket
identifier, more than 16 buckets, or an empty or invalid `rateLimitsByLimitId` map makes the whole observation unavailable. Nothing
the provider writes (plan names, credits, account identity, free text) is carried.

**The policy.** Every bucket and every window is evaluated. A used percentage equal to or above the threshold stops the attempt; a
provider-reported reached state with no window above the threshold also stops it. Invalid, partial, unavailable, mismatched or expired
evidence refuses, and evidence is expired when its host-stamped retrieval instant falls outside the read interval, lies in the future,
is more than 30 seconds old at the seam, or when any reported reset time has passed. Below-threshold evidence only satisfies this
local guard: it is not eligibility, remaining quota or live capacity, and the UI never says so.

**Two seams, never inside a transaction.** The provider is observed outside any EF transaction. At the claim seam the handler re-reads
the stored authority and compares the facts inside the short claim transaction (after taking the write lock). Before dispatch, the
host takes a separate observation bound to the attempt, the threshold and the launch tuple, and the dispatch gate
(`MarkAgentAttemptDispatchedCommand`) independently validates those facts against the stored snapshot and the current vetted launch
tuple before it commits the dispatch marker. A missing, mismatched, expired or reached guard can never commit the marker.

**A terminal pre-dispatch outcome.** When the pre-dispatch guard refuses, a dedicated command terminates the claimed attempt atomically
with a bounded canonical decision and one completion event: outcome `AccountUsageStopReached` or `AccountUsageEvidenceUnavailable`,
no dispatch marker, no Agent invocation. The command is a manual-transaction command: one short write-locked transaction takes the lock, refreshes the attempt from the
database (a tracked entity is never trusted), reads the threshold snapshot from that fresh state, re-evaluates, records the decision
and the event with one save and commits, with no external work inside it and the notification only after the commit. Facts prepared
for another threshold or launch tuple resolve as unavailable evidence for the actual snapshot, without their windows; facts for another
attempt are refused; the command refuses to stop an attempt that the policy permits. Consumed budget slots and grants stay spent; there
is no retry, polling, refund or new repair authority, and a restart does not redispatch a committed terminal attempt. If the terminal
recording itself cannot commit, the attempt stays Running and undispatched but is blocked from every later dispatch pass of that host
process (fail closed, surfaced in the host log, never recovered automatically); the block is process-local. A normal full host restart applies the existing startup recovery and read-only Agent reconciliation before supervisors:
the still-Running attempt and its Run become Interrupted, without another account observation or Agent invocation for it. No account-usage
decision is invented when terminal recording never committed. This is existing interruption authority; automatic resume, refund and reauthorization remain excluded. The decision (`AgentAccountUsageDecisionSnapshot`) is a versioned canonical text (source `codex-account-rate-limits-v1`, at
most 8192 bytes, ordered windows) that is read back strictly and must agree with the attempt's own threshold snapshot (an ordinary decision needs the actual equal valid
threshold, the unusable-threshold decision needs a really malformed snapshot); anything else, including a contradiction or an absent
snapshot, is displayed as unknown, never as a decision, and no historical row is rewritten.

**Evidence and display.** The cockpit reports `codexAccountUsageStop` (separately from token activity and from the display-only
allowance) and the attempt evidence reports `accountUsageStop` and `accountUsageDecision`. The UI uses fixed safe copy (changing the setting never alters the
threshold an already-claimed attempt recorded, but a claimed attempt is still checked before it starts), owns its draft, pending,
error and request-guard state by the committed run and the authoritative setting identity, and states a decision truthfully: the host retrieval time and the reported
windows, never eligibility. Generated clients are reproduced by the normal NSwag generation.

## Consequences

- A human can stop new Codex work at a reported usage percentage without a provider login, a scheduler or a quota reservation.
- The guard is local and best effort by construction: it depends on the read-only observation the installed Codex CLI reports at two
  instants, and says nothing about whether a later invocation will succeed. Dispatched work is never cancelled by it.
- Two additional App Server reads can occur per configured Codex attempt; a disabled setting adds none.
- The older token-activity stop, the display-only allowance, the Claude providers, the lifecycle and every earlier replay path are
  unchanged. Historical attempts have no threshold and no decision.
- Not decided here: a warning threshold, Claude allowance, quota reservation, an override, session resume, compaction or provider
  login. A different behaviour needs a superseding ADR.
