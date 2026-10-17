# Agent collaboration execution evidence

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the per-attempt execution evidence contract, which keeps an Agent
attempt's semantic outcome apart from host-measured and provider-reported
facts, and the run-wide Agent process-duration summary built on that evidence.
The sections below are moved verbatim except two links that now point back to
the protocol; the protocol's original headings forward here. The summary's
references to process evidence "above" mean the preceding section. Provider
token usage and model context limits are in the [usage evidence](agent-collaboration-usage-evidence.md)
companion; the run-wide claim-count and reserved-time budgets are in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion; provenance,
sealed artifact windows and attempt history are in the [evidence inspection](agent-collaboration-evidence-inspection.md) companion. Mapping: [protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Execution evidence versus semantic outcome

Every Agent attempt keeps five kinds of facts apart, and none of them stands in
for another:

- **Semantic outcome** — `AgentOutcome` is the workflow classification of the
  attempt (`Proposed`, `Challenged`, `Implemented`, `ProviderInvocationFailed`,
  `SourceChanged`, and so on). It is decided by the role's own recording
  handler from validated output and fresh Git evidence.
- **Host-measured execution evidence** — how the provider child process
  actually ended, measured by the host rather than reported by the provider:
  `Exited`, `TimedOut`, or `Cancelled`; an exit code only for `Exited`; and a
  non-negative duration, persisted at full tick resolution. It is recorded
  once, atomically with the terminal outcome, and only for a dispatched
  attempt whose process produced a real result. Every semantic success
  outcome and `InvalidStructuredOutput` requires a clean exit (`Exited` with
  code `0`) — as do `NoChangesProduced`, `ImplementationHeadChanged`,
  `CorrectionNoChangesProduced`, and `CorrectionHeadChanged`, since each is
  only ever classified once the process itself is already known to have
  exited cleanly. A clean exit does not imply a semantic success — for
  example, a zero exit with a rejected response is still
  `ProviderInvocationFailed`. Every *other* failure outcome that can genuinely
  follow a real invocation (`ProviderInvocationFailed`, `SourceChanged`,
  `CheckpointEvidenceUnavailable`) may carry any real result, so a timeout, a
  cancellation, or a non-zero exit remains distinguishable. `WorkspaceNoLongerEligible`
  and every "input already handled" race outcome (`InputAlreadyReviewed`,
  `InputAlreadyResolved`, `InputAlreadyImplemented`, `InputAlreadyCodeReviewed`,
  `InputAlreadyCorrected`) are different in kind: each is always detected
  before the provider is ever invoked, so none of them may ever carry process
  evidence, regardless of the attempt's dispatch state. `SourceChanged` is
  deliberately not in that group — it can be detected either before or after
  a real invocation, so it may carry a real result when one exists.
- **Provider-reported observations** — the observed model, observed effort,
  and provider-session identifier are values the provider itself reported.
  They are independent of process evidence and are never derived from it.
- **Provider-reported token usage** — `AgentTokenUsageEvidence`: input and
  output token counts, optional cache-creation and cache-read input token
  counts, and an internal parsing-contract schema version. Unlike process
  evidence it is reported by the provider, not measured by the host, and it is
  best-effort observation only: it is never required for any outcome (there is
  no clean-exit rule), and it may accompany a success, an
  `InvalidStructuredOutput`, or a provider failure of a dispatched attempt,
  since a provider can report the usage it consumed before failing. It is
  recorded once, atomically with the terminal outcome and any process evidence,
  and — by reusing the same closed pre-invocation outcome set as process
  evidence rather than a second copy — never for an undispatched attempt or a
  pre-invocation outcome. It is never inferred from output length, configured
  limits, CLI defaults, or another provider's fields.
- **Provider-reported model context limits** — `AgentModelContextLimitsEvidence`: the model identifiers Claude listed in its own
  result and the context-window and maximum-output limits it reported for each, under a fixed parsing-contract source tag. Like
  token usage it is reported by the provider, best-effort, independent of the outcome, the process evidence and the usage, and
  recorded once in the same transaction for a dispatched Claude attempt only; it is never remaining context or capacity (see
  [Claude-reported model context limits](agent-collaboration-protocol.md#claude-reported-model-context-limits)).
- **Truthful absence** — an attempt that was never dispatched, whose invocation
  failed before any process result existed, that a restart reconciled as
  `Interrupted`, or that predates this evidence has no process evidence at all.
  It is projected as unknown and is never fabricated or backfilled. The same
  holds for token usage: interrupted, legacy, and pre-migration attempts, and
  every attempt whose provider has no proven usage contract, read as unknown.

The status API of each role and the run cockpit expose the evidence as a
separate `processExecution` object beside the semantic `outcome`: its
`outcome`, `exitCode`, `durationMilliseconds`, and the attempt's configured
`timeoutMilliseconds`. It never includes an executable path, argument,
environment value, output, context manifest, session identifier, or
credential. Raw output is not a usage metric, and this evidence is not a
token, cost, or account-usage measurement.

This per-attempt projection shares the same fail-closed rule the provider
[token-usage contracts](agent-collaboration-protocol.md#provider-token-usage-contracts) describe, and shares it through a single
Domain-level source of truth rather than a duplicated check:
`Attempt.GetAgentProcessExecutionEvidence()` itself reports unknown process
evidence whenever the attempt is still `Running` or has never been dispatched
(`AgentDispatchedAtUtc` is `null`), even when its persisted row already
carries seemingly well-formed process fields (for example a corrupted or
prematurely-populated row) — every one of the per-role status endpoints
(planner, critical review, challenge resolution, code review, implementation,
review correction), the run cockpit's own `latestAgentAttempt.processExecution`,
and the collaboration-message evidence drill-down call this same method, so
the guarantee holds identically everywhere a single attempt's process
evidence is projected. The attempt's configured `timeoutMilliseconds` is a
separate, always-known-at-claim-time value and is never gated behind this
rule — it remains visible even while `outcome`/`exitCode`/`durationMilliseconds`
are unknown. The run-wide process-duration summary below is unaffected by
this rule: it reads each attempt's own `Status` directly from its bounded
per-row projection rather than through `GetAgentProcessExecutionEvidence()`,
so a still-running attempt was already, and remains, correctly bucketed as
pending there.

## Run-wide Agent process-duration evidence summary

The run cockpit additionally exposes a bounded, read-only, run-wide summary of
the host-measured process evidence above — pure telemetry, never a budget, and
strictly distinct from the two Agent budgets ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)'s
count budget and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)'s
reservation-time budget, which reasons only about *configured* `AgentTimeout`
values, never measured duration). It answers only "what has the host actually
measured so far", derived exclusively from this run's own dispatched Agent
attempts' `AgentProcessOutcome`/`AgentProcessExitCode`/`AgentProcessDuration`
evidence, and it enforces nothing.

The summary is one closed evidence-state classification
(`AgentProcessDurationEvidenceStatus`) plus bounded counts and an optional
exact total:

- `NoDispatchedAttempts` — this run has dispatched no Agent attempt at all.
- `Complete` — every dispatched Agent attempt has reached a terminal result
  and every one of them carries valid process evidence. Only this state
  carries a non-null `TotalMeasuredDuration` (the exact tick-resolution sum),
  so a real zero-duration measurement is always visibly distinct from "no
  measurement is available yet" (which is always `null`).
- `PendingEvidence` — at least one dispatched attempt is still running, and
  every terminal attempt observed so far has valid evidence.
- `PartialEvidence` — at least one terminal attempt has valid evidence and at
  least one other terminal attempt has missing or malformed evidence (with or
  without attempts still pending). The total is never shown for this state,
  since a partial sum would understate the run's real usage; the malformed
  attempt is never silently dropped or treated as zero — it is counted
  separately in `MalformedEvidenceCount`.
- `MalformedEvidence` — one or more terminal attempts exist, but none of them
  carries valid evidence — with or without attempts still pending. Never
  conflated with a real zero-duration measurement, which is `Complete` with a
  zero total instead, and never described as if every dispatched attempt had
  reached a terminal result when `PendingAttemptCount` is nonzero.
- `UnrepresentableTotal` — every terminal attempt observed so far carries
  valid evidence (no malformed evidence at all) — with or without attempts
  still pending — but the exact running sum of those valid durations
  overflows what a `TimeSpan` can represent. This is deliberately distinct
  from `MalformedEvidence`: the individual measurements are real and valid,
  only their total is unrepresentable, so this state is never described using
  malformed/invalid wording. `TotalMeasuredDuration` stays `null`, matching
  every other non-`Complete` state.

Summation is overflow-safe and honest about which fact failed: when every
individual duration observed is itself valid but their running sum overflows
what a `TimeSpan` can represent, the result is `UnrepresentableTotal`, not
`MalformedEvidence` — the individual measurements are never discredited
merely because their sum cannot be represented. Malformed evidence still
dominates the classification exactly as before: a run with any malformed
terminal attempt is classified as `MalformedEvidence`/`PartialEvidence`
(per the valid-evidence count) even if the valid subset's own sum would
independently overflow, since the malformed evidence is the more important
fact to surface. The read side streams a minimal per-attempt projection
(terminal status plus process-evidence fields) through a one-pass,
constant-memory accumulator — the same shape the existing token-usage
accumulator uses — rather than materializing full `Attempt` entities. The API
projection (`AgentProcessDurationSummaryResponse`) carries only the status
string, an optional `totalMeasuredMilliseconds`, and bounded counts
(`dispatchedAttemptCount`, `pendingAttemptCount`, `validEvidenceCount`,
`malformedEvidenceCount`) — never a path, argument, output, manifest, session
identifier, or credential. The Domain-level sum itself remains exact — it is
computed from real `TimeSpan`/tick values with checked, overflow-safe integer
arithmetic, so `TotalMeasuredDuration` is exactly correct wherever it is
representable at all (that is, wherever the state is not
`UnrepresentableTotal`). `totalMeasuredMilliseconds` is projected from that
exact sum as a `double` (`TimeSpan.TotalMilliseconds`), not a truncated
integer: a truncating integer cast would silently collapse a genuinely
positive sub-millisecond total (fewer than 10,000 ticks) down to exactly
zero, making it indistinguishable from "nothing measured". The `double`
projection reliably preserves that zero-versus-positive distinction, but it
is not a promise of full tick-level precision in the wire format at every
magnitude — a `double` cannot exactly represent every possible tick-derived
millisecond value — so it serves the zero/non-zero and coarse display
purposes this summary is for, not exact reproduction of the underlying
ticks.
