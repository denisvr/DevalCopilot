# Agent collaboration account usage

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the provider account-allowance contracts and the two owner-configured,
run-scoped Codex account-usage controls. The Codex allowance observation and
both controls read the Codex App Server `account/rateLimits/read` method, with
different authority. The allowance is display-only: a host-timestamped
snapshot, never eligibility or an enforceable threshold. The stop is a strict,
enforced local guard at Agent claim and again before dispatch, never over the
display-only projection. The warning is advisory: an explicit check whose
setting no claim, dispatch or stop path reads. Neither a below-threshold stop
result nor a warning check is eligibility or remaining quota. None of them is
token activity: the token-activity warnings and stop are in the
[token-activity controls](agent-collaboration-token-activity-controls.md)
companion, and the per-attempt token-usage contracts are in the
[usage evidence](agent-collaboration-usage-evidence.md) companion. Claude has
no account-allowance observation and no account-usage threshold; Claude
account usage stays open. The sections below are moved verbatim except one
link to the Codex per-attempt token-usage contract; the protocol's original
headings forward here. Model and effort controls and the Claude agentic-turn
limit are in the
[invocation configuration](agent-collaboration-invocation-configuration.md)
companion; claim and time budgets and every other subject not moved to a
companion stay in the protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Provider account-allowance contracts

**Codex — read-only account observation, distinct from the Codex per-attempt
[token-usage contract](agent-collaboration-usage-evidence.md#provider-token-usage-contracts).** The Codex App Server documents a JSON-RPC
protocol over explicit `--stdio` JSONL with a required `initialize`/`initialized`
handshake before any other method is accepted
(`https://learn.chatgpt.com/docs/app-server`). Its `account/rateLimits/read`
method and `rateLimitsByLimitId`/legacy `rateLimits` response shape were
confirmed from the installed `codex-cli 0.158.0-alpha.2.1`
build's own generated App Server schema (`ClientRequest.json` and
`v2/GetAccountRateLimitsResponse.json`) — protocol evidence for that installed
build, not a live authenticated result and not a claim about every installed
version. A dedicated Infrastructure adapter speaks this handshake and sole
read method directly against the same already-vetted local Codex CLI launch
target every other Codex path uses; it never opens a listening socket, reads a
CLI auth file, supplies a token, starts a thread/turn, or sends any other App
Server method. The shared, one-shot `CodexProcessInvoker` contract cannot
express this: the App Server is a long-running duplex peer this adapter must
itself terminate once its one bounded exchange completes, so a second, equally
bounded (finite timeout, restricted environment, bounded capture, process-tree
cleanup) Infrastructure process boundary exists for it alone.

Only a complete JSON object whose `id` matches the outstanding request is
trusted; every notification and every reply for a different id is ignored.
More than one distinct reply observed for the same id before a decision is
made is never resolved by picking one — the whole read-only exchange reports
`Unknown` instead, since a single invocation has exactly one true reply per
request. `rateLimitsByLimitId` maps each metered limit id to a separate
snapshot with its own `primary` and `secondary` windows; legacy `rateLimits`
is one snapshot. The map is preferred when present and never combined with the
legacy view. At most 16 buckets with bounded, validated identifiers are
projected; malformed, duplicate, or excessive keys fail the observation
closed rather than silently hiding a bucket. Each window requires an integer
`usedPercent` in [0, 100]; nullable `windowDurationMins` and `resetsAt` are
independently optional. The latter is Unix seconds, converted to a reset
instant only when valid. Missing or malformed optional fields remain Unknown
without erasing a valid percentage. A response with no usable window reports
`Unknown` rather than an `Observed` snapshot with nothing to show. A JSON-RPC `error` on
either the handshake or the read method, a missing vetted Codex launch target,
a timeout, cancellation, or any process failure all resolve to the same
`Unknown` state — never zero, never an exception the caller must handle
(cancellation propagated from the caller is the sole exception to that: it is
rethrown, not swallowed). This is a read-only, host-clock-timestamped snapshot
only — never resume eligibility, invocation eligibility, an enforceable
threshold, or a claim about a specific invocation's current eligibility to
start.

**Claude — no equivalent contract yet.** No safe, machine-readable Claude Code
account-allowance observation has been established; the cockpit continues to
show its existing "not yet collected" placeholder for Claude.

## Run-scoped Codex account-usage stop at claim and dispatch

An owner may set or clear an optional integer from 1 to 100 on an active Run (`Run.CodexAccountUsageStopPercent`) through one
protected MVC operation (`POST /api/runs/{runId}/codex-account-usage-stop`, strict body `{ "percent": integer | null }`; one human
event per change). Null disables the guard and adds no provider read. A new Codex attempt (planning, challenge resolution, code
review and re-review, verification diagnosis, and their format repairs) snapshots the value immutably
(`AgentCodexAccountUsageStopPercent`); later changes apply only to later claims, and historical rows stay null. A stored value that
is not the canonical text of an integer in range refuses new Codex claims with a fixed error; it is never treated as disabled.

Enforcement is a local guard over a strict, read-only provider observation
([ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)), never over the display-only
allowance projection:

1. The observation (the provider-neutral Application port `IAccountUsageObserver`, implemented in Infrastructure by the Codex adapter:
   initialize, initialized, one `account/rateLimits/read` through the vetted launch
   target) is all-or-nothing. A duplicate, a missing or invalid window, an out-of-range percentage, an invalid duration or reset, an
   unsafe bucket identifier, more than 16 buckets or an empty or invalid bucket map is unavailable. No provider text is carried.
2. Every bucket and window is evaluated. A window at or above the threshold stops (equality stops); so does a provider-reported
   reached state; so do missing, mismatched or expired evidence (a retrieval instant outside the read interval, in the future or more
   than 30 seconds old, or a passed reset).
3. The provider is observed outside any EF transaction. The claim seam re-reads the stored authority and compares the facts inside the
   short claim transaction. Before dispatch a separate observation, bound to the attempt, the threshold and the launch tuple, is
   validated again by `MarkAgentAttemptDispatchedCommand`, which cannot commit the dispatch marker on a missing, mismatched, expired
   or reached guard.
4. A pre-dispatch refusal terminates the claimed attempt through one dedicated command, atomically with a bounded canonical decision
   (`AgentAccountUsageDecisionSnapshot`, version 1, source `codex-account-rate-limits-v1`, at most 8192 bytes) and one completion
   event: outcome `AccountUsageStopReached` or `AccountUsageEvidenceUnavailable`, no dispatch marker, no Agent invocation. Consumed
   budgets and grants stay spent; there is no retry, polling, refund or new repair authority. The command is a manual-transaction
   command (one short write-locked transaction: lock, refresh the attempt from the database, read the threshold snapshot from that
   fresh state, record, one save, commit; the notification follows the commit). Facts prepared for another threshold or launch tuple
   resolve as unavailable evidence for the actual snapshot, without their windows; facts for another attempt are refused. If the
   terminal recording cannot commit, the attempt stays Running and undispatched but is blocked from every later dispatch pass of that
   host process (fail closed, logged; the block is process-local). Recreating only a supervisor reuses that block. A normal full
   host restart runs the existing startup recovery and read-only Agent reconciliation before supervisors: the still-Running attempt and
   its Run become Interrupted, without a new account observation or Agent invocation for that attempt. It does not invent an account-usage
   decision whose recording never committed, and grants no automatic resume or refund.
5. A recorded decision is projected only when it agrees with the attempt's own threshold snapshot; a contradiction or an absent
   snapshot is shown as unavailable evidence with nothing of the decision exposed. The set operation's request body is bounded to
   8 KiB.

The cockpit shows the setting (`codexAccountUsageStop`) separately from token activity and from the display-only allowance, and the
attempt evidence shows `accountUsageStop` and `accountUsageDecision`. Below-threshold evidence is never described as eligibility,
remaining quota or live capacity, and an already dispatched attempt is never cancelled.

## Run-scoped advisory Codex account-usage warning

An owner may set or clear an optional integer from 1 to 100 on an active Run (`Run.CodexAccountUsageWarningPercent`) through one
protected MVC operation (`POST /api/runs/{runId}/codex-account-usage-warning`, strict body `{ "percent": integer | null }` bounded to
8 KiB; one human event per change, committed atomically with the value). Only a Created or Running run whose freshly read execution
mode admits Agent work is editable, a same-value request is lifecycle-guarded, and the operation contacts no provider. The stored text
uses the exact-integer mapping: a malformed value reads as `Unknown`, is never disabled or coerced, and is repaired by a valid set or
clear. Historical runs stay null. The column is deliberately not an EF concurrency token and no claim, gate, dispatch, supervisor,
stop, budget, lease, permission, invocation or context path reads it; the Attempt schema is unchanged. Warning and stop are
independent, with no required ordering. The cockpit exposes the saved setting (`codexAccountUsageWarning`) and performs no provider
work.

A separate protected operation, `GET /api/runs/{runId}/codex-account-usage-warning`, performs one explicit advisory check
([ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)):

1. It reads the stored setting, execution mode and vetted Codex launch afresh and untracked. No setting, a malformed setting, an
   unadmitted run or no launch causes no observation; no caller input influences it.
2. Otherwise it makes exactly one bounded observation through `IAccountUsageObserver` outside every transaction, then re-reads the
   setting, the exact stored mode (still Agent-admitting and unchanged) and launch; replaced authority is `Unavailable`
   (`ConfigurationChanged`) without windows or an observation time, never an applicable result.
3. A dedicated pure policy evaluates every bucket and window: a percentage at or above the saved percentage or a provider-reported
   reached state is `Reached`, otherwise `Below`. Invalid, partial, unavailable or expired evidence (a retrieval instant outside the
   read interval or in the future, older than 30 seconds at evaluation, or a passed reset) is `Unavailable`, with no valid subset,
   credit override, sum or inferred zero.
4. The answer carries only the saved percentage, bounded bucket identifiers, window kinds, reported percentages, per-window reached
   flags, the provider-reached flag and the host retrieval time. The check writes no event, attempt, reservation, grant, manifest
   change, cache or observation history, and reuses neither the stop's gate, facts or decisions nor the display-only allowance.

The Usage & Evidence rail shows the setting and a "Check Codex account warning" action beside the stop control; nothing reads the
account on mount, save, clear, selection, cockpit catch-up or polling. The control owns its draft, pending state, errors, duplicate
protection and last observation by the committed run plus the authoritative saved warning, and a result is never described as
eligibility, readiness, remaining capacity or usage attributable to the run. Claude account usage, session resume and manual
compaction remain open.
