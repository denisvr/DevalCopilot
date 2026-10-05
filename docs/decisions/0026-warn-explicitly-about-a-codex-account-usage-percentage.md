# ADR-0026: Warn explicitly about a Codex account-usage percentage

Status: Accepted

## Context

[ADR-0025](0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md) lets a human stop new Codex work at a
provider-reported account-usage percentage. The roadmap also calls for separate account-usage warning thresholds, and the Usage &
Evidence rail only displays a host-scoped partial allowance. A human who wants to be told, ahead of the stop, that a reported window has
reached a lower percentage had no way to say so or to ask.

## Decision

**One optional, run-scoped, advisory percentage.** A Run carries a nullable integer from 1 to 100 (`CodexAccountUsageWarningPercent`).
Null means no warning. It is set or cleared through `POST /api/runs/{runId}/codex-account-usage-warning` with a strict body
`{ "percent": integer | null }` bounded to the established 8 KiB MVC request-body limit (proven through real Kestrel). The operation
is protected and admits only a Created or Running run whose freshly read execution mode admits Agent work, and it contacts no provider.
The new value and one human change event commit in one `SaveChangesAsync`; the Run lifecycle and execution mode remain concurrency
tokens, so a same-value request is guarded like any other and a run that became terminal is not edited. The column uses the exact-stored-text
integer mapping, so a tampered storage class or non-canonical value reads as malformed (`Unknown` in the cockpit, never disabled,
never coerced) and a valid set or clear repairs it; unrelated saves preserve it exactly. Historical runs keep null: there is no default
and no backfill.

**It is not a concurrency token and no execution path reads it.** Unlike the stop, the advisory column is not an EF concurrency token,
so writing it can never make a claim's own Run UPDATE fail, and concurrent warning writes each commit their own value and event atomically
(SQLite serializes them, so the last committed value and event agree). Claim handlers, eligibility feeds, dispatch gates, supervisors,
stop recording and reconciliation, budgets, leases, permissions, invocation arguments, provider sessions and context neither read nor
write it, and the Attempt schema is unchanged. A warning never refuses, reserves, spends, stops or reorders anything.

**The two thresholds are independent.** There is no required ordering: the warning may be above, below or equal to the stop, and either
can be set, changed or cleared without the other.

**One explicit, separate check.** `GET /api/runs/{runId}/codex-account-usage-warning` (protected, no parameters) is an operation-owned
query. It reads the stored setting, the freshly stored execution mode and the vetted Codex launch afresh and untracked (a populated
change tracker is never trusted). With no setting, a malformed setting, an unadmitted run or no vetted launch it makes no observation.
Otherwise it makes exactly one bounded observation through the existing provider-neutral `IAccountUsageObserver`, outside every
transaction, then re-reads the stored setting, the exact stored execution mode (it must still admit Agent work and be the same representation first read, so ManualAgent replaced by Legacy or the reverse is replaced authority) and the launch: replaced authority yields `Unavailable`
(`ConfigurationChanged`), never an applicable result. The check writes nothing: no event, attempt, reservation, grant, manifest change,
cache or persisted observation history. Nothing a caller supplies (threshold, executable, provider identity, prior observation)
influences it.

**Source facts are separate from enforcement.** The check reuses only the strict immutable observation contract and its all-or-nothing
parser. It does not reuse the stop's gate, facts, launch tuple, decisions or terminal recording, the partial display allowance, or any
cached value. A dedicated pure policy evaluates every bucket and window: a used percentage at or above the saved percentage (equality)
or a provider-reported reached state is `Reached`; otherwise `Below`. Invalid, unavailable, partial, duplicated or expired evidence is
`Unavailable`, never zero and never a valid subset; evidence is expired when its host-stamped retrieval instant lies outside the host's
read interval or in the future, is older than 30 seconds when evaluated, or when any reported reset has passed. The response carries only
the saved percentage, bounded bucket identifiers, window kinds, provider-reported percentages, the reached flag per window, the
provider-reached flag and the host retrieval time.

**Display.** Usage & Evidence shows the saved setting and a "Check Codex account warning" action beside the distinct stop control, with
fixed safe English copy for not-checked, pending, failed, unavailable, and dated below or reached results. No warning read happens on
mount, save, clear, selection, cockpit catch-up or polling, and a cleared warning adds none. A reload keeps the setting, not the
observation. The control owns its drafts, pending state, errors, duplicate protection and last observation by the committed run plus the
authoritative saved warning: a change of either, A to B to A, an unmount, a retained handler or a late completion cannot write or
refresh a newer owner (an accepted server operation still stands), and a pending or failed check shows no earlier classification. It
never calls an account eligible, ready, remaining or live, and states that a check describes the host's Codex account, not usage
attributable to the run.

## Consequences

- A human can be told, on request, that a reported Codex window has reached a lower percentage before the stop refuses work, without any
  login, scheduler, notification system, quota reservation or provider nudge.
- The warning is advisory and best effort: it reflects one read at one instant, says nothing about whether a later invocation will
  succeed and does not change what the stop or any budget does.
- A check costs one App Server read; saving, clearing, reading the cockpit and the existing stop add none.
- The stop, the display-only allowance, Claude, the lifecycle and every earlier replay path are unchanged. No new RPC method, provider
  contract or dependency is added.
- Not decided here: Claude account usage (no bounded machine-readable observation exists for this host), session resume, manual
  compaction, automatic or outbound notification, a stored observation history, or a generic warning and stop framework. A different
  behaviour needs a superseding ADR.
