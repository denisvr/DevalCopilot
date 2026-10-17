# Agent collaboration correction and budgets

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the review-correction contract and the run-wide Agent budgets beside it:
the first review-guided correction loop (eligibility, ordered inputs, result,
correction context, its two-claim budget, the escalation at exhaustion and the
one human-authorized additional claim), the run-wide claim-count budget
(ADR-0012), the run-wide reserved Agent invocation-time budget (ADR-0013) and
the owner's immutable choice of both at manual intake (ADR-0028). These are
claim-time limits: every Agent claim consumes one slot and reserves its
configured timeout, whatever happens afterward. None of them is measured
duration (the run-wide process-duration summary is in the
[execution evidence](agent-collaboration-execution-evidence.md) companion),
token activity (the
[token-activity controls](agent-collaboration-token-activity-controls.md)
companion) or account usage (the
[account usage](agent-collaboration-account-usage.md) companion). The section
below is moved verbatim; the protocol's original Review correction heading
forwards here, and the section's directional "above" and "below" references
mean paragraphs within it. Its statements are as written at the baseline,
including its counts of Agent-claiming paths and claim handlers and its
statement of which controls remain deferred; the move reconciles or endorses
none of them. Guidance for the one additional correction, escalation and
diagnosis are in the
[guidance and escalation](agent-collaboration-guidance-and-escalation.md)
companion. The provider configuration observations formerly at the end of the
same protocol section are in the invocation configuration companion's
[provider configuration observations](agent-collaboration-invocation-configuration.md#provider-configuration-observations).
Mapping: [protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Review correction

The first review-guided correction loop is represented by a separate
`AgentResponseContract.ReviewCorrection`, mapped to the existing
`AgentRole.Implementer` and `AgentEffectKind.WorkspaceMutating`. This is why a
contract lookup is keyed by response contract rather than role: one role may
perform more than one bounded job while role remains the authorization
dimension.

A correction claim is eligible only when the applicable implementation review
completed with `ReviewChangesRequested`, has at least one bounded finding, and
the reviewed ExecutionReport and result checkpoint still belong to the same
run, project, workspace, and current checkpoint chain. Its durable ordered
input identity is exactly the previous ExecutionReport followed by every
ReviewFinding in collaboration-timeline order. A duplicate dispatch gate uses
bounded bulk candidate loading and exact ordered comparison; partial,
reordered, foreign, or cross-run input sets never match.

The bounded correction result contains one `RevisionResponse` for every input
finding, each replying to its exact finding, plus exactly one new
`ExecutionReport`. A valid mutation requires fresh Git evidence, unchanged
`HEAD`, a changed fingerprint, non-empty observed paths, and an exact changed
path match with the report. The resulting immutable checkpoint, workspace
advance, collaboration messages, artifacts, and events are committed
atomically. A no-change result fails truthfully without a checkpoint; an
unexpected `HEAD` change flags the workspace for attention.

The correction context is a versioned manifest of structured evidence only.
Complete provider transcripts are never persisted or replayed. The current
Claude adapter is concrete provenance and is not an end-to-end guarantee that
another provider can safely substitute. Initial implementation and
ReviewCorrection currently use Claude Code. Gemini remains deferred behind its
administrator-provisioned policy prerequisite. Assignment facts are persisted
on the Attempt and do not authorize fallback, parallel executors, or automatic
re-review.

Each run persists a default maximum of two claimed review-correction attempts.
Claims consume the budget even when dispatch, execution, or later result
recording fails or is interrupted. Exhaustion creates one durable escalation
for the unresolved review instead of silently retrying or invoking a provider.
The human may explicitly authorize exactly one additional claim; that
authorization is recorded as a HumanInstruction and atomically consumed with
the correction attempt claim. This is explicit continuation, not generic
pause/resume, provider fallback, Gemini support, or automatic orchestration.
Token and account-usage enforcement, measured wall-clock duration, and the
remaining Increment 4 controls remain deferred. The run-wide Agent
invocation-*time* reservation budget described immediately below is not one
of these: it is delivered and enforced today (see
[ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)),
though it reasons only about each attempt's own *configured* timeout, never
measured duration.

Independently, each run also persists a default maximum of 16 claimed Agent
attempts — a run-wide ceiling spanning all six Agent-claiming paths (Planner
proposal, Claude critical review, Codex challenge resolution, Claude
implementation, Codex implementation review, Claude review correction), never
combined with the narrower review-correction budget above. Every claimed
Agent attempt permanently consumes one slot regardless of role, provider,
dispatch, result, or interruption; Simulated and Process attempts never
consume it. Unlike the review-correction budget, this ceiling has no human
override — exhaustion is a hard stop for the run's remaining Agent-claiming
paths, though attempts already claimed may still finish, and the run itself
is not automatically terminated. See
[ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md).

Independently again, each run also persists a fixed default maximum of 120
minutes of reserved Agent invocation time
(`Run.MaximumAgentInvocationTime`) — a second run-wide ceiling, enforced
alongside (never instead of) the 16-claim count budget above, on every one of
the same six Agent-claiming paths, at the same point each already checks the
count budget. Every claimed Agent attempt permanently reserves its own
configured `AgentTimeout`, regardless of role, provider, dispatch, result, or
interruption; Simulated and Process attempts never consume it. An
over-budget claim is rejected with `agent_attempts.time_budget_exceeded`,
kept distinct from a merely lost `(RunId, AgentBudgetSlot)` slot race
(`agent_attempts.budget_slot_conflict`) the same way ADR-0012 already
distinguishes a lost slot race from genuine count exhaustion. Unlike
ADR-0012's own historical backfill, a Run recorded before this decision keeps
`MaximumAgentInvocationTime` truthfully `NULL` — no time-budget policy at
all, never a fabricated ceiling — and every Agent-claiming handler skips this
check entirely for such a run. The `GetRunCockpit` projection exposes this as
its own bounded `AgentInvocationTimeBudget` object (maximum, reserved,
remaining, a legacy/unknown flag, and a fail-closed evidence-invalid flag),
never combined with the count budget's own fields. See
[ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md).

Neither ceiling has to be the default for a NEW manual run
([ADR-0028](../decisions/0028-let-the-owner-choose-immutable-run-budgets-at-manual-intake.md)): `POST /api/runs/manual` accepts the
optional nullable integers `maximumAgentAttempts` (1 through 16) and `maximumAgentInvocationMinutes` (1 through 120 whole minutes), each
independently defaulting to 16 and 120 when omitted or null, and refuses any other explicit value (including a quoted or fractional
number) without clamping, rounding or writing anything. The effective values are stored in the same two columns by the same atomic
creation and are immutable afterwards. No claim algorithm changes: all eight Agent claim handlers, their repair variants and race
rechecks keep reading the persisted values, every claim keeps consuming one slot and reserving its configured timeout whether it later
fails or is interrupted, and a ceiling below a role's configured timeout refuses that claim before any probe, capture, seal or provider
invocation. The range is a creation rule only; a historical or domain-created run (above 16 claims, or with no time policy) is read and
enforced exactly as before, and the simulated operation keeps the fixed defaults.
