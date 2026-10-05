# ADR-0028: Let the owner choose immutable run budgets at manual intake

Status: Accepted

## Context

[ADR-0012](0012-add-a-durable-run-wide-agent-claim-budget.md) gives every run a durable ceiling on claimed Agent attempts and
[ADR-0013](0013-add-a-durable-run-wide-agent-invocation-time-budget.md) a durable ceiling on reserved invocation time. Both are
enforced by all Agent claim handlers from the persisted `Run` columns, but every new run was created with the fixed defaults (16 claims,
120 minutes) because [ADR-0014](0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)'s manual creation accepted only
an objective. An owner who wants a smaller, enforceable commitment for one objective (for example four claims and 40 minutes) could not
state it.

## Decision

**Two optional choices at manual creation only.** `POST /api/runs/manual` and its one `CreateManualRunCommand` accept the optional
nullable integers `maximumAgentAttempts` (1 through 16) and `maximumAgentInvocationMinutes` (1 through 120 whole minutes). A missing or
null member independently selects the existing default (16, 120). An explicit value outside the range, or one that is not a JSON
integer (a fraction, a quoted number, an overflow, a boolean), is refused with a validation or model-binding failure and creates
nothing; it is never clamped, rounded, or read as a default. The members are read with strict number handling because the MVC default
would otherwise convert a quoted number. No other public creation or update operation gains these inputs, and the simulated operation
keeps the fixed defaults.

**The same atomic creation.** The effective values (the validated whole minutes as exactly `TimeSpan.FromMinutes`) pass through
`RunIntentRecorder` to `Run.RecordClassifiedIntent` and are stored in the existing `MaximumAgentAttempts` and
`MaximumAgentInvocationTime` columns. The run, its intent event, and the project execution-number reservation remain one serialized
save; admission, conflict mapping, and event semantics are unchanged. There is no migration, backfill, new event kind, new column, or
second budget store.

**Immutable, and enforced by the existing claims.** Neither ceiling has a setter, clear, increase, reset, refund, override, policy
version, or per-attempt snapshot. All eight Agent claim handlers (and their repair, race-recheck and read-only variants) already read
`run.MaximumAgentAttempts` and `run.MaximumAgentInvocationTime`; no claim algorithm changes. Every claim still permanently consumes one
slot and reserves its configured timeout whether the attempt later fails or is interrupted. An Architecture test pins that all eight
handlers compare against the persisted values and never a constant, and that only the creation operations name the defaults.

**Narrow transport range.** The 1..16 and 1..120 limits are a rule for creating a NEW manual run. They do not limit a historical or
domain-created run, whose persisted ceilings (including a run above 16 claims or with no time policy) are read and enforced exactly as
before. Existing runs are never normalized.

**Meaning.** The ceilings are reservations and permanent claims, not measured elapsed time, a token, cost, or account ceiling, or any
assurance that an objective can finish. A ceiling below the first role's configured timeout is valid and prevents that claim (a 9 minute
reservation refuses the 10 minute Codex planning claim before any probe, evidence capture, seal, or provider invocation); neither the
timeout nor the choice is adjusted. Exhaustion still neither finishes a run nor permits replacing it.

**Intake form and display.** Manual intake shows two whole-number drafts (initially 16 and 120) beside the objective, with copy that
claims stay consumed after a failure or interruption, that time is reserved from configured timeouts rather than measured, that neither
ceiling can be changed after recording, and that a small ceiling can prevent the first claim. The objective and both drafts are one
project-owned submission snapshot: every edit of any of them, including editing back to an identical value, advances its version, and an
accepted completion resets only an unchanged snapshot of its own current project lifetime. A replacement, an A to B to A switch, an
unmount, or a late completion cannot overwrite a newer snapshot, notice, error, or guard, and an accepted server operation still stands.
The separate simulated demo ignores the drafts. The cockpit shows the actual immutable claim ceiling and usage, and the reserved-time
ceiling, reservation, and remainder, before exhaustion as well as at it, keeps the unknown, invalid, and exhausted states distinct, and
never reads a count or a positive remainder as proof that a role can be claimed.

## Consequences

- An owner can bound one objective's Agent claims and reserved invocation time at creation, and the existing enforcement honors those
  exact persisted values after a reload and across failures and interruptions.
- The fixed defaults remain for the simulated demo, for omitted choices, and for every historical run. The older ADRs are unchanged;
  this decision narrowly advances only their fixed-default creation behavior for new manual runs.
- A very small ceiling can make a run unable to start any role. The owner then needs a new objective after this run finishes; nothing
  here raises, resets, or replaces a ceiling.
- Not decided here: changing a ceiling on an existing run, an owner-chosen default, larger ceilings, a per-role allowance, measured
  duration, token or account policy, and any scheduler or terminalization. Any of those needs a superseding ADR.
