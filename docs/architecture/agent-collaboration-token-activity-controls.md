# Agent collaboration token-activity controls

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the two owner-configured, run-scoped, per-provider token-activity controls
for Codex and Claude Code, both over locally recorded, provider-reported usage.
The warning is advisory only: its state is never an eligibility input. The stop
is enforced, but only as a retrospective local guardrail on that provider's next
Agent claim; it cannot bound an invocation already in progress. Neither is an
account allowance. The per-attempt token-usage contracts whose persisted
evidence both count are in the
[usage evidence](agent-collaboration-usage-evidence.md) companion. The sections
below are moved verbatim; the protocol's original headings forward here, and
the stop's "advisory warning above" means the warning section in this
companion. Account-usage stops and warnings and account allowances are in the
[account usage](agent-collaboration-account-usage.md) companion; model and
effort controls and the Claude agentic-turn limit are in the
[invocation configuration](agent-collaboration-invocation-configuration.md)
companion; the claim-count and reserved-time budgets are in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion; every other subject not moved to a companion stays in the
protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Per-provider run token-activity warnings

An owner may set or clear an optional, positive, advisory **token-activity warning threshold** for
each of Codex and Claude Code on an active Run (`Run.CodexTokenWarningThreshold`,
`Run.ClaudeTokenWarningThreshold`), through one protected MVC operation
(`POST /api/runs/{runId}/token-warning-threshold`, body `{ provider, thresholdTokens }`). It is a
warning about locally recorded, provider-reported usage. It is never a hard token budget, an account
allowance, a cost, or an eligibility rule: nothing in the six Agent claim paths, dispatch, provider
adapters, budgets, model settings, or permission arguments reads it, and no provider is contacted to set
or evaluate it. No threshold exists for a historical or new run until the owner configures one.

- **Persistence.** The additive `AddProviderTokenWarningThresholds` migration adds two nullable
  `runs` integer columns with no default and no backfill. A non-null value is between 1 and
  `Run.MaxTokenWarningThreshold` (10^12); zero, negative, over-cap, or beyond-`long` values are rejected
  (HTTP 400) and `provider` is exactly `Codex` or `ClaudeCode`. Setting one provider never touches the other.
- **Lifecycle and concurrency.** `SetTokenWarningThresholdCommand` is a manual-transaction command whose
  single `SaveChangesAsync` commits the Run change and its `run.token_warning_threshold_changed` event
  (payload: provider and new threshold, or null) together. It forces the Run UPDATE even for an unchanged
  value so that the existing `Run.Lifecycle` concurrency token always guards it: a transition to a terminal
  lifecycle committed after the read rolls back both the value and the event and is reported as
  `token_warning.run_not_editable` (HTTP 422); a terminal run is rejected up front the same way. Other
  concurrency conflicts (e.g. the Claude model-request token) are a retryable `token_warning.concurrent_change`
  (HTTP 409). The threshold columns are deliberately **not** concurrency tokens: a threshold write must never
  make an Agent claim's own Run UPDATE fail. SQLite serializes write transactions, EF updates only the modified
  column, and each write commits its value and event atomically, so concurrent writes to different providers both
  persist and concurrent writes to the same provider leave a final value matching the last committed event.
- **Evidence and formulas.** The projection (`RunCockpitTokenWarningProjection`, Application-owned) consumes
  the same dispatched-Agent-attempt evidence as the other cockpit token projections, in the cockpit query's
  existing single pass. Each persisted row is reconstructed through `AgentTokenUsageEvidence.FromPersisted`,
  which validates the persisted usage shape and the proven provider/schema pair (anything else is unknown
  evidence). Separately, the warning accumulator excludes a still-running attempt: it is only ever pending,
  even if its row already carries valid-looking usage. A concluded attempt contributes exactly once:
  Codex counts `inputTokens + outputTokens` (its `cached_input_tokens` is already inside input and is not
  added; the Codex schema cannot carry a cache breakdown); Claude Code counts `inputTokens +
  cacheCreationInputTokens + cacheReadInputTokens + outputTokens`. A persisted Claude row missing either cache
  count is **insufficient** for this warning (never treated as zero) even though the unchanged raw usage view
  still shows its input/output. Sums are 64-bit. Undispatched attempts never invoked a provider and are
  excluded; a still-running attempt is pending and never counted; a concluded attempt with missing, malformed,
  or unsupported-schema/provider usage is insufficient; a dispatched attempt with no known provider is
  unattributed, never assigned to either provider, and counts as an evidence gap for both.
- **States.** Per provider: `NotConfigured` (neutral; counts still reported), `ThresholdReached` (known count
  >= threshold, including exact equality, valid even as a lower bound with gaps), `NoEvidence` (configured, no
  dispatched attempt: not a known zero), `BelowThresholdComplete` (below, and no pending, insufficient, or
  unattributed attempts; a known zero has `countedAttempts > 0`), and `Indeterminate` (below, but a gap exists:
  explicitly not an all-clear). Changing a threshold changes only an input to this pure derivation, so existing
  evidence is re-evaluated on the next cockpit read with no provider invocation. Because the threshold
  endpoint emits no run event (so no SignalR `runAdvanced` notification), the cockpit UI explicitly refreshes
  after a successful save or clear through `useRunCockpit().refresh`, which reuses the hook's existing
  generation-guarded, coalescing catch-up loop: a response for a run that is no longer current is discarded,
  a pass queued behind an already-in-flight one guarantees a fetch that starts after the change, and a failed
  refresh yields a fixed safe synchronization message rather than a stale-looking success. The cockpit response carries
  `tokenWarnings`, always two entries (Codex, then ClaudeCode) with the threshold, state, known count, and the
  counted/pending/insufficient/unattributed attempt counts; the existing run-wide and provider-separated raw
  usage fields are unchanged.
- **Basis.** The formulas rest on the existing locally versioned usage parsers (`codex-cli-usage-v1`,
  `claude-cli-usage-v1`) and the documented Codex cached-input breakdown and Claude cache-token semantics; this
  slice adds no adapter, provider invocation, allowance read, or provider-session resume. The two providers'
  counts are provider-specific and are never summed into a cross-provider figure.

## Per-provider run token-activity stop at Agent claim

An owner may set or clear an optional, positive **token-activity stop threshold** for each of Codex and
Claude Code on an active Run (`Run.CodexTokenStopThreshold`, `Run.ClaudeTokenStopThreshold`), through one
protected MVC operation (`POST /api/runs/{runId}/token-stop-threshold`, body `{ provider, thresholdTokens }`).
Unlike the advisory warning above, a configured stop is **enforced**, but only as a retrospective local claim
guardrail: when a provider's locally recorded usage has reached its threshold (or staying below it cannot be
proved), the next Agent claim for that provider does not commit. An attempt already claimed may dispatch and
finish. It is not an account allowance, a per-attempt cap, a token reservation, a cost or rate limit, or a
guarantee about an invocation in progress, and it does not cancel, fall back, or override anything. With no
stop configured for the claim's provider, claim behavior is unchanged and the gate issues no query. The stop
and the advisory warning are independent settings and independent projections: the warning's state is never an
eligibility input, and setting either never configures the other.

- **Persistence.** The additive `AddRunTokenStopThresholds` migration adds two nullable `runs` integer columns
  with no default and no backfill, so every historical run has no stop. The range is the warning's existing
  1 to `Run.MaxTokenStopThreshold` (10^12); zero, negative, over-cap, or beyond-`long` values are rejected
  (HTTP 400) and `provider` is exactly `Codex` or `ClaudeCode`. Setting one provider never touches the other.
- **Set and clear.** `SetTokenStopThresholdCommand` is a manual-transaction command whose single
  `SaveChangesAsync` commits the Run change and its `run.token_stop_threshold_changed` event (payload: provider
  and new threshold, or null) together; a terminal run is `token_stop.run_not_editable` (HTTP 422). The Run
  UPDATE is forced even for an unchanged value so its concurrency tokens always guard it. Here the two stop
  columns **are** EF concurrency tokens (the advisory warning columns deliberately are not): a committed
  change to the value of any configured concurrency token after the read (the lifecycle, the Claude model and
  effort request, or either stop threshold) makes the UPDATE match zero rows, so the save
  rolls back both the value and its event and is reported as `token_stop.run_not_editable`
  for a terminal run or a retryable `token_stop.concurrent_change` (HTTP 409) otherwise. Two concurrent
  writers therefore never silently overwrite each other.
- **Evidence and formulas.** The stop reuses the persisted, versioned per-attempt usage evidence and the
  advisory warning's provider-specific count through one shared rule (`AgentTokenActivityFormula`): Codex counts
  `inputTokens + outputTokens` (cached input is already inside input and is not added again); Claude Code
  counts `inputTokens + cacheCreationInputTokens + cacheReadInputTokens + outputTokens` and a persisted row
  missing either cache count is insufficient, never zero-filled. Each row is reconstructed through
  `AgentTokenUsageEvidence.FromPersisted` (usage shape plus the proven provider/schema pair); the stop's own
  accumulator (`AgentTokenStopAccumulator`) then treats a still-running attempt as pending, a concluded attempt
  with missing, malformed, or unsupported evidence as insufficient, and a dispatched attempt with no known
  provider as unattributed (a gap for both providers, never assigned to either). Only dispatched Agent attempts
  count: an undispatched attempt never invoked a provider. The two providers' counts are never combined.
- **Untrusted persisted evidence.** `Status`, `AgentProvider`, and `AgentRole` are stored as strings, and EF's
  conversion throws on a value it does not recognize, so neither the gate nor the cockpit stop projection
  materializes them. `PersistedAgentAttemptStopEvidence` has the database compare them against the known
  names and returns booleans: running, defined terminal (`Completed`, `Failed`, `Interrupted`), and a coherent
  provider/role pair (Codex with Planner, Resolver, or CodeReviewer; Claude Code with CriticalReviewer or
  Implementer, which includes review correction). A row whose status is neither running nor a defined terminal
  status (including an undefined number such as `99`), whose provider string is unrecognized, or whose provider
  and role are not a coherent pair is untrusted: it is counted as an unattributed gap for both providers, never as
  a concluded attempt and never with its usage assigned to the provider it names. Sound rows keep exact provider
  separation and the known-count-at-threshold precedence, so a reached count from sound rows still refuses as
  reached beside an untrusted row, and otherwise the claim refuses as indeterminate before any external work. No
  stored string or exception text reaches an error or projection. The cockpit's latest-attempt card is the one
  place that still loads a full entity; if that single row cannot be materialized the card is omitted instead of
  failing the cockpit. The advisory and raw usage projections read the same server-side flags (an unrecognized
  status is pending and never counted; an unrecognized provider is unattributed) and otherwise behave as before.
  Other, older reads of a corrupted row elsewhere in the system are unchanged.
- **Decision.** Per provider, `AgentTokenStopState` is: `NotConfigured`; `ThresholdReached` (known count
  >= threshold, including exact equality, and even when other evidence is incomplete, since the count is then a
  lower bound); `EvidenceIndeterminate` (below the threshold, or not representable, but a pending, missing,
  malformed, unsupported, or unattributed attempt, or a 64-bit sum overflow, means staying below cannot be
  proved); `NoDispatchedHistory` (no dispatched attempt of any provider and none unattributed, so the first claim
  is permitted; deliberately not a measured zero); or `BelowThresholdComplete` (below, with complete evidence).
  Only `ThresholdReached` and `EvidenceIndeterminate` refuse a claim, with distinct safe errors that carry no
  count or evidence: `agent_attempts.token_stop_reached` (HTTP 409) and
  `agent_attempts.token_stop_evidence_indeterminate` (HTTP 422). Overflow cannot arise from the bounded
  per-attempt integers under the 16-claim budget; it is nonetheless handled without wrapping and is reported as
  indeterminate rather than as a number. A permitting state says only that this stop does not refuse a claim; it
  never asserts provider availability or account allowance.
- **Where it is enforced.** All six Agent claim paths (Codex planning, including a manual format repair, Claude
  critical review, Codex challenge resolution, Claude implementation, Codex implementation review, Claude review
  correction) call `AgentTokenStopGate.CheckClaimAsync` with the path's own fixed provider, immediately after the
  existing run-wide count and reserved-time budgets (whose failure precedence is unchanged) and before any
  provider-availability probe, Git evidence capture, manifest sealing, or, for review correction, escalation
  creation and authorization consumption. A refused claim therefore creates no attempt, no artifact, and no
  escalation, and leaves an available authorization unconsumed.
- **Commit-time guard.** Concluded attempts' usage is written by the transition that concludes them and never
  changes, and any Agent attempt committed by a concurrent claim collides on the run's budget slot, so the only
  input that can change between the decision and the commit is the stop policy itself (the pair of thresholds
  the claim's tracked Run loaded). `CurrentTokenStopPolicy` guards it inside each claim's own database-atomic
  unit. The three Claude paths (one `SaveChangesAsync`) mark both stop columns modified so the claim's Run UPDATE
  requires the exact loaded pair; a change makes it match zero rows and rolls the whole batch, including the
  Attempt, its input rows, its artifact, and (for review correction) the authorization consumption, back. The
  three Codex paths (explicit short transaction after all external work) run one `ExecuteUpdate ... WHERE`
  compare on the pair, beside the existing model-preference compare and before any insert; the transaction's first
  write takes the SQLite write lock, so the compares and the Attempt, artifact, and input writes are one serialized
  unit. On either mismatch the claim fails with the
  retryable `agent_attempts.token_stop_policy_changed` (HTTP 409), never a generic persistence error, the sealed
  manifest is deleted, and a retry re-decides against the current policy. A change to the other provider's
  threshold is also a policy change: the guard is the pair, which is conservative and safe to retry. After a lost
  budget-slot race each path re-evaluates the stop before reporting a retryable slot conflict, after the
  existing exhaustion and time-budget classifications. A threshold changed after a claim commits is prospective
  and never revokes that claimed attempt.
- **Cockpit projection.** `GetRunCockpit` adds `tokenStops`, always two entries (Codex, then ClaudeCode) derived
  by the same accumulator in the query's existing single pass, so it agrees with the claim gate on the same
  persisted evidence: provider, threshold, `state`, `claimBlocked`, `knownTokenCount` (a lower bound when any gap
  count is non-zero, saturated when `countOverflowed`), and the counted, pending, insufficient, and unattributed
  attempt counts. The existing `tokenWarnings` and raw usage fields are unchanged. Because the operation emits no
  run event, the cockpit UI refreshes after a successful save or clear exactly as it does for the warning.
- **Restart and replay.** The decision is a pure function of persisted attempts and the persisted thresholds; no
  claim-path handler holds a provider adapter, so a refused claim after a process restart never reaches a
  provider, and changing a threshold only re-evaluates already-recorded evidence on the next claim or cockpit
  read.
- **Known limits.** The count is best-effort local evidence; a provider that reports no usage leaves the stop
  unable to clear (indeterminate) rather than guessing. A run with an unrecorded prior attempt therefore needs
  its stop raised or cleared by the owner; there is no override and no automatic fallback. The stop cannot bound
  an invocation already in progress, does not read Codex account-allowance observations, and adds no provider,
  parser, CLI-argument, session, or context behavior. A review-correction request that would only create the
  human escalation is also refused while the stop blocks Claude, because the stop check precedes that logic.
