# ADR-0012: Add a durable run-wide Agent claim budget

Status: Accepted

## Context

Every real invocation of an external agent provider is a claimed `Attempt` with
`Kind = Agent`, created through exactly six Domain factories: the Planner
proposal, the Claude critical review, the Codex challenge resolution, the
Claude implementation, the Codex implementation review, and the Claude review
correction. Each of those six paths is independently bounded by its own
eligibility rules, and the review-correction path additionally carries a
small, human-overridable budget
([ADR-0010](0010-add-review-correction-response-contract.md) and the
[delivery plan](../roadmap/mvp-delivery-plan.md)'s Increment 4 review-
correction control). Nothing bounds the total number of Agent attempts a
single run can ever claim across all six paths combined. A run stuck cycling
through repeated planning, review, resolution, or correction attempts has no
durable, run-wide ceiling independent of any one path's own local rule.

## Decision

Every new run is assigned a fixed, immutable default maximum of 16 claimed
Agent attempts (`Run.MaximumAgentAttempts`). Every claimed Agent attempt —
created by any of the six Agent-claiming Domain factories, regardless of
workflow role, provider, dispatch outcome, or interruption — permanently
consumes exactly one slot of this budget. A Simulated or Process attempt never
consumes it. Unlike the review-correction budget, this ceiling has no human
override: once exhausted, no further Agent attempt can ever be claimed for
that run, and reaching it does not itself terminate the run.

Each claimed Agent attempt records the permanent, 1-based
`Attempt.AgentBudgetSlot` it consumed, unique per `RunId`. The primary defense
against a claimed slot exceeding the budget is an application-level
count-and-compare check performed before any provider-availability probe or
external evidence capture, so an exhausted run never invokes a provider and
never consumes a review-correction human authorization. The check alone does
not close the race between two concurrent claim requests for the same run;
the database backstop is a unique index on `(RunId, AgentBudgetSlot)`,
mirroring the existing filtered unique index that already enforces at most one
`Running` attempt per run.

A lost race on that index is not automatically reported as exhaustion: after
the loss, each of the six claim handlers re-derives, from fresh, untracked
state, whether a persisted Agent attempt now occupies the exact slot this
request had computed. When it does, the run's real Agent-attempt count is
recomputed and compared to its maximum a second time. Below the maximum, this
is `agent_attempts.budget_slot_conflict` — a safe, retryable conflict: only
this one slot number was lost to a faster concurrent claim, and the run still
has room for another attempt. Only when that recount has reached the maximum
is the loss truthfully reported as `agent_attempts.budget_exhausted`. Neither
code is ever inferred from the exception alone; both are derived from what the
database actually holds at the moment of failure, matching this codebase's
existing convention (see `attempts.run_has_active_attempt` vs.
`attempts.persistence_failed`) of never reporting a conflict that would
misdescribe what really happened.

The additive migration backfills every historical Agent attempt a
deterministic slot (ordered by `AttemptNumber` within its run) and raises each
historical run's `MaximumAgentAttempts` to at least its own existing
Agent-attempt count, so no historical run is retroactively presented as having
exceeded a budget it was never bound by.

The used/maximum/exhausted state is exposed through the existing run cockpit
projection (`GetRunCockpit`), never combined with the distinct
review-correction budget already exposed by `GetReviewCorrectionAttemptStatus`.
The cockpit renders a clear human next action once exhausted: review the
evidence gathered so far, or start a new run.

## Consequences

- A run can never claim an unbounded number of Agent attempts, independent of
  any single path's own local eligibility rule.
- The six Agent-claiming Domain factories now require an explicit, validated
  `agentBudgetSlot`; no factory silently defaults or infers one.
- Historical runs remain truthfully described: a run that already claimed more
  than 16 Agent attempts before this migration is never shown as having
  violated a budget, and its slots are backfilled in the exact order those
  attempts were actually claimed.
- This budget has no authorization-override path, unlike the review-correction
  budget; exhaustion is a hard stop for that run's Agent-claiming paths, and
  existing claimed attempts may still finish normally.
- A lost race for one budget slot is distinguishable from genuine exhaustion:
  callers that lose a `agent_attempts.budget_slot_conflict` race may retry the
  same claim, while `agent_attempts.budget_exhausted` means no further Agent
  attempt can ever be claimed for that run.
- Automatic cancellation, retry, or provider fallback in response to
  exhaustion remain out of scope for this decision.

## Alternatives considered

### Reuse the review-correction budget's count-and-compare-only enforcement

Rejected: that budget is scoped to one response contract per run and tolerates
a narrower race window. A run-wide ceiling spanning all six claim paths
deserves the stronger, already-proven filtered-unique-index pattern used for
the one-`Running`-attempt-per-run invariant, not a second copy of a
count-only check.

### Give this budget the same human-authorization override as review correction

Rejected for this slice: the task is a hard run-wide ceiling, not a
renegotiable per-path budget. Adding an override would require its own
escalation/authorization entities and human decision flow, duplicating
ADR-0010's mechanism for a different, broader scope than this slice defines.

### Enforce the budget only at dispatch time, not at claim time

Rejected: claiming an Agent attempt is the durable commitment this budget
protects against, per the review-correction budget's own precedent ("every
claim consumes the budget, including claims that later fail or are
interrupted"). Deferring enforcement to dispatch would let more than the
budgeted number of Agent attempts exist as claimed, durable rows.
