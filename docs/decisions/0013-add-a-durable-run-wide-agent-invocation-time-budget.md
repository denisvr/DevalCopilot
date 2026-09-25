# ADR-0013: Add a durable run-wide Agent invocation-time budget

Status: Accepted

## Context

[ADR-0012](0012-add-a-durable-run-wide-agent-claim-budget.md) bounds the total
*number* of Agent attempts a run can ever claim, across all six Agent-claiming
Domain factories. It says nothing about how much invocation time those
claimed attempts have collectively committed to. Each Agent-claiming command
handler already configures a fixed `AgentTimeout` per claim shape (for
example, ten minutes for a Codex planning attempt, twenty minutes for a
Claude implementation attempt); `Attempt.AgentTimeout` durably records it at
claim time. Sixteen claimed Agent attempts at the longer configured timeouts
could still commit a run to several hours of reserved invocation time with no
independent ceiling on that total — a distinct resource dimension from a mere
attempt count.

## Decision

Every newly recorded Run (`Run.RecordIntent`) is assigned a fixed, immutable
default maximum of 120 minutes of reserved Agent invocation time
(`Run.MaximumAgentInvocationTime`, a nullable `TimeSpan`). Every claimed
Agent attempt — created by any of the six Agent-claiming Domain factories,
regardless of workflow role, provider, dispatch outcome, or interruption —
permanently reserves exactly its own configured `Attempt.AgentTimeout`
against this budget, mirroring ADR-0012's own permanent-consumption rule for
its count budget. A Simulated or Process attempt never consumes it. This
budget is independent of, and enforced alongside — never instead of —
ADR-0012's count budget: both are checked on every claim, at the same point
in each of the six Agent-claiming command handlers (before any
provider-availability probe, Git evidence capture, or artifact creation; for
`CreateReviewCorrectionAttemptCommandHandler` specifically, before its own
ADR-0010 human-authorization consumption).

A claim is allowed only when the sum of `AgentTimeout` across the run's
already-claimed Agent attempts, plus the candidate claim's own configured
`AgentTimeout`, does not exceed `MaximumAgentInvocationTime`. The
already-reserved sum is computed fresh on every claim
(`AgentInvocationTimeBudget.ComputeReservedAsync`, backed by the pure
`AgentInvocationTimeReservation.ComputeReserved` Domain helper) — never
cached or denormalized. If any of the run's own claimed Agent attempts
carries missing or non-positive `AgentTimeout` evidence, the computation
fails closed (returns "unknown," never zero) and the claim is rejected with
`agent_attempts.time_budget_evidence_invalid`, a `Failure` rather than a
`Conflict`, since it signals a data-integrity concern rather than a legitimate
race or exhaustion.

An over-budget claim is rejected with a new, fixed, safe error code,
`agent_attempts.time_budget_exceeded`, and persists nothing. This is
deliberately distinct from a mere concurrent-slot collision: after a
handler's own `SaveChangesAsync` loses the ADR-0012 `(RunId, AgentBudgetSlot)`
unique-index race, the handler re-derives both the run's real Agent-attempt
count and its real reserved invocation time from fresh, untracked state —
mirroring ADR-0012's own count-exhaustion-vs-slot-conflict reclassification
exactly, with the same priority order (count exhaustion, then time
exhaustion, then a safe, retryable slot conflict).

Unlike ADR-0012's backfill, the additive migration
(`AddAgentInvocationTimeBudget`) that adds the nullable
`runs.MaximumAgentInvocationTime` column performs **no backfill and no
synthesis**: every historical Run — one recorded before this migration —
keeps this column truthfully `NULL`, meaning "no time-budget policy exists
for this run at all," never a fabricated 120-minute ceiling it was never
actually bound by. A `NULL` value causes every Agent-claiming command handler
to skip this check entirely for that run; only `Run.RecordIntent` ever
assigns the real, non-null default, to a newly created Run.

The `GetRunCockpit` projection exposes this budget as a small, distinct
`AgentInvocationTimeBudget` object — `Maximum`, `Reserved`, `Remaining`,
`IsLegacyUnknown`, `EvidenceInvalid` — never combined with ADR-0012's own
`MaximumAgentAttempts`/`AgentAttemptsUsed`/`AgentBudgetExhausted` fields, and
never collapsed into one generic "exhausted" boolean: exposing `Remaining`
lets a caller determine whether any one particular candidate role's own
configured timeout would still fit, since different roles configure
different timeouts. `IsLegacyUnknown` is a distinct, explicit state from "zero
minutes remaining" — a historical Run is never described as having reached an
exhausted ceiling it was never actually given.

## Consequences

- A run can never commit to an unbounded total of reserved Agent invocation
  time, independent of ADR-0012's own separate ceiling on attempt count.
- Both budgets are evaluated on every claim; either one alone can reject a
  claim, and each is reported with its own distinct, truthful error code.
- A historical Run remains truthfully described: it is never retroactively
  bound by a 120-minute ceiling it predates, and the cockpit shows this as an
  explicit "not tracked" state rather than a fabricated policy or a
  misleading "0 minutes remaining."
- If a run's own historical Agent-attempt evidence is malformed, every
  further Agent claim for that run is blocked (fails closed) until the data
  is corrected — a deliberate, conservative failure mode favoring safety over
  availability.
- This budget has no human-authorization override, exactly like ADR-0012's
  count budget and unlike the review-correction-specific budget: exhaustion
  is a hard stop for that run's Agent-claiming paths.

### What this decision explicitly does NOT implement

- **Measured wall-clock duration.** `Reserved` is the sum of what claimed
  attempts have *committed to reserving* (their own configured
  `AgentTimeout`), never a measurement of how long any provider process
  actually ran. `AgentProcessDuration`/`AgentProcessExecutionEvidence` remain
  the only host-measured execution-time evidence this codebase has, and nothing
  here changes their meaning or feeds them into this budget.
- **Token limits.** This budget has no relationship to
  `AgentTokenUsageEvidence` or any provider-reported token count.
- **Provider account-usage limits.** This budget is a local, run-scoped
  reservation ceiling; it neither reads nor enforces any provider's own
  account-level rate or usage limits.
- **Any guarantee that actual process lifetime cannot exceed the
  reservation.** `AgentTimeout` is the bound each adapter is configured to
  enforce against its own child process (see the existing process-timeout
  and cancellation handling); this budget assumes that enforcement is
  correct and never independently re-verifies or re-bounds an in-flight
  provider invocation itself. A provider invocation that overruns its own
  configured timeout is a defect in that separate enforcement, not something
  this budget detects or corrects.

## Alternatives considered

### Fold this into ADR-0012's existing count budget

Rejected: count and time are genuinely different resource dimensions — a run
could be well within its attempt-count ceiling while still committing to
hours of reserved time, or vice versa on a run using unusually short
timeouts. Conflating them into a single number would hide which dimension
actually constrained a given claim, exactly the kind of ambiguity ADR-0012
itself avoided by keeping its own budget distinct from the pre-existing
review-correction budget.

### Backfill historical runs a synthesized default, matching ADR-0012's own count-budget backfill

Rejected: ADR-0012's backfill assigns each historical run a *count* ceiling
computed from, and never lower than, that run's own real historical usage —
it can never retroactively describe a historical run as having violated a
budget. A synthesized *time* ceiling has no equivalent safe floor to compute
from without first re-deriving every historical attempt's own
`AgentTimeout` and summing it (itself already exactly what
`AgentInvocationTimeReservation.ComputeReserved` does at claim time) and
then deciding an arbitrary margin above it — an invented policy, not a
recovered fact. Leaving the column `NULL` for every historical run is the
only truthful choice.

### Enforce the budget only at dispatch time, not at claim time

Rejected for the same reason ADR-0012 rejected it: claiming an Agent attempt
is the durable commitment this budget protects against. Deferring
enforcement to dispatch would let more reserved time than the budget allows
exist as claimed, durable rows.
