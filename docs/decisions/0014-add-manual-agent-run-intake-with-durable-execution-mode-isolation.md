# ADR-0014: Add manual Agent run intake with durable execution-mode isolation

Status: Accepted

## Context

The only production caller of `Run.RecordIntent` was the simulated-run start, which sent a fixed objective and was
offered only to a project with no run. The deterministic simulator claimed every run in `Created`, so a user-written
objective could not be recorded without being started as a simulation. The six explicitly requested Agent stages
([ADR-0004](0004-use-a-structured-agent-collaboration-protocol.md), [ADR-0009](0009-separate-agent-roles-effects-and-provider-assignments.md),
[ADR-0010](0010-add-review-correction-response-contract.md)) therefore had no normal entry. This is an additive decision
and reverses none of ADR-0002/0003/0004/0005/0006/0008/0009/0010/0012/0013.

## Decision

**Durable execution mode.** `Run.ExecutionMode` is an immutable `RunExecutionMode` whose stored value is the integer:
`Legacy = 0`, `Simulated = 1`, `ManualAgent = 2`. The migration `AddRunExecutionMode` adds a NOT NULL INTEGER column
defaulting to 0, so every existing row is `Legacy`. A mode is never inferred from attempts, providers, lifecycle, or
events, and no historical row is rewritten. Both creation operations assign an explicit positive mode
(`Run.RecordClassifiedIntent` rejects `Legacy` and any undefined number); there is no mode input, setter, or conversion.
`Legacy` keeps the historical admission behavior and is disclosed as "execution mode was not recorded". The column is read
without coercion through the storage-class-preserving mapping that already protects the Claude turn-limit columns, so only
the exact integers 0, 1, and 2 are recognized: a REAL that would truncate to a valid mode, an integer outside the enum
(including one that would overflow 32 bits), text, and a BLOB are all reported as unrecognized, are admitted by nothing,
never throw during materialization, and round-trip unchanged through unrelated saves. The column is an EF concurrency
token.

**Manual creation.** `POST /api/runs/manual` (`CreateManualRun`, protected) accepts a project ID and an objective (nonblank,
at most 2,000 characters) and, in one short transaction, persists a `Created`/`Intake` run, the next monotonic project
execution number, the existing default run budgets, and the orchestrator-authored intent event. It performs no workspace
preparation, checkpoint capture, readiness probe, manifest assembly, attempt claim, provider invocation, lease
acquisition, or Agent budget consumption; the existing stage claims keep all their eligibility checks. A `Created` manual
run means "waiting for an explicit planning request", never autonomous execution or proven provider readiness.

**Creation admission.** Manual and simulated creation share one rule (`RunIntentRecorder`): a project with any `Created`
or `Running` run, or any run whose lifecycle is not a recognized terminal one, refuses a new intent with the safe conflict
`runs.intent_blocked`; no run, or entirely `Completed`/`Failed`/`Interrupted` history, permits it. The check and the number
reservation are one serialized unit: `Project.NextExecutionNumber` is an EF concurrency token, so two requests that both
passed the check cannot both commit; the loser rolls back completely and receives the retryable `runs.intent_conflict`.
No retroactive uniqueness rule is added, so databases holding several historical active runs remain valid. This is
conservative intake admission, not the scheduler or queue deferred by [ADR-0006](0006-support-bounded-concurrent-runs-across-projects.md);
budget exhaustion neither makes a run terminal nor authorizes replacing it.

**Execution isolation.** Simulation (claim, feed, step recording, completion, and Simulated-provenance messages) admits
`Simulated` and `Legacy`; all six Agent claim paths (including their four repair variants), their six eligibility feeds, and
the final `MarkAgentAttemptDispatched` gate admit `ManualAgent` and `Legacy`; standalone Process claims, its feed, and its
dispatch admit `Legacy` only. Every decision reads the stored mode afresh and untracked (a tracked `Run` may be stale and
never confers authority), before external work, and again at the commit seam: Codex-family claims confirm it with an atomic
`UPDATE ... WHERE` inside their claim transaction, and single-save paths mark the mode as a guarded concurrency token so a
competing change rolls the whole claim back, with the sealed manifest removed and no reservation or authorization consumed.
`CompleteSimulatedRun` additionally requires `AttemptKind.Simulated`, so it cannot complete an Agent attempt or a manual
run. An already claimed incompatible attempt invokes no adapter; no recovery authority is added. The mode does not prove
provider capability or invocation eligibility, which keep their existing gates.

**Projections and UI.** Project summaries and the cockpit report the mode (`Legacy`, `Simulated`, `ManualAgent`, or
`Unrecognized`), and summaries carry a creation-availability hint derived from every run of the project; the command always
re-checks. A project that holds a run with an unrecognized lifecycle stays visible and blocked (never treated as terminal,
classified, or disclosed). The cockpit offers the six Agent request actions only for `ManualAgent` and `Legacy`. The simulation stays a
separately labelled demo.

## Consequences

A manual run can remain nonterminal after its stages or after budget exhaustion; because creation requires every run to be
terminal, a project with such a run cannot record another objective until a later decision adds completion or replacement
authority. Doing so, a coordinator, scheduler, automatic stage advancement, pause/stop/retry, and manual terminalization
remain deferred. A migrated `Legacy` run keeps every historical capability, so isolation protects new runs only.
