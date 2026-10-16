# Agent collaboration usage evidence

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the provider-reported usage evidence contracts: the proven Claude Code and
Codex per-attempt token-usage contracts with their status, cockpit and run-wide
summary projections, and the Claude-reported model context limits recorded as
historical attempt evidence. The sections below are moved verbatim except one
link to the Claude Code CLI safety contract in the
[provider contracts](agent-collaboration-provider-contracts.md) companion; the
protocol's original headings forward here. The two sections' references to each
other "above" and "below" mean the neighboring section in this companion.
Token-activity warnings and stops are in the
[token-activity controls](agent-collaboration-token-activity-controls.md)
companion; account allowances, account-usage stops and warnings, model and
effort controls, budgets and every other subject not moved to a companion stay
in the protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Provider token-usage contracts

**Claude Code — proven.** The token-usage contract was verified from the
installed `@anthropic-ai/claude-code@2.1.276` package's native `claude.exe`
using the same evidence method as [the Claude Code CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract):
literal strings embedded in the compiled binary, never an authenticated model
invocation. The binary's `--output-format json` result-envelope schema declares
`usage` as a sibling of `is_error`, `result`, `session_id`, `subtype`,
`duration_ms`, and `total_cost_usd` on the same envelope object, including the
`--json-schema` structured-output mode every Claude adapter uses. Its embedded
usage schema documentation names exactly `input_tokens`, `output_tokens`,
`cache_creation_input_tokens`, and `cache_read_input_tokens` as numbers, and
its default usage literal initializes all four to `0`, so they are always
present rather than optional. The three Claude adapters therefore read only
those four members, with the schema version `claude-cli-usage-v1`, from the
same stdout envelope they already validate. Parsing fails closed to "no usage"
— never to an exception or an invented value — when `usage` is missing or not
an object, or when any member is missing, not a JSON integer, negative, or
outside the 32-bit range; a missing or malformed `usage` never rejects an
otherwise valid review, report, or correction. A single unambiguous `usage`
object is required: duplicate `usage` members or duplicate
required count members make usage unknown without rejecting the business result.
The Domain accepts this evidence only for the exact provider/schema pair
`ClaudeCode` + `claude-cli-usage-v1`; even a well-formed persisted row with
a mismatched provider or schema projects as unknown, not as known usage.
Usage is read only from a
structurally valid envelope of a clean process exit whose stdout was not
truncated (a valid `is_error: true` envelope still carries its usage);
`total_cost_usd`, `duration_api_ms`, `stop_reason`, and every
other field are ignored here (`modelUsage` is read separately, see "Claude-reported model context limits" below).

**Codex — proven.** The token-usage contract was verified from the installed
Codex CLI (`codex-cli 0.155.0-alpha.16.4`) using two independent read-only
sources, never an authenticated model invocation: the CLI's own `codex exec
--help` output, which documents `--json` as "Print events to stdout as
JSONL", and the official non-interactive-mode documentation
(`https://developers.openai.com/codex/noninteractive`), which documents the
JSONL event stream's `thread.started`, `turn.started`, `turn.completed`,
`turn.failed`, `item.*`, and `error` event types and shows the exact
terminal-turn shape
`{"type":"turn.completed","usage":{"input_tokens":24763,"cached_input_tokens":24448,"output_tokens":122,"reasoning_output_tokens":0}}`.
The installed executable's own embedded strings independently confirm every
one of those event tags and usage member names, alongside a richer internal
`TokenUsage`/`TokenUsageInfo` shape this documented stream does not expose.
The three Codex adapters therefore read `input_tokens` and `output_tokens`
from the `usage` object of the stream's unique terminal `turn.completed`
event, with the schema version `codex-cli-usage-v1`, from the same
already-captured JSONL stdout `CodexProcessInvoker` already scans for the
provider session identifier. `cached_input_tokens` and
`reasoning_output_tokens` are read only to confirm the documented shape is
complete, then discarded: `cached_input_tokens` has no proven correspondence
to Claude's separate `cache_creation_input_tokens`/`cache_read_input_tokens`
breakdown, so Codex usage never carries a cache-creation or cache-read count
— both stay null. Parsing fails closed to "no usage" — never an exception or
an invented value — for malformed JSONL, a missing, duplicated, or
contradictory terminal turn event (more than one `turn.completed`, or a
`turn.completed` alongside a `turn.failed`), a missing or malformed `usage`
object, or any member that is missing, not a JSON integer, negative,
duplicated, or outside the 32-bit range. A single unreadable or ambiguous
line anywhere in the captured stdout — one that is not valid JSON, is not a
JSON object, does not declare `type` exactly once, or declares `type` as
anything other than a string — makes the *entire* capture untrustworthy, not
just that one line: this reader can never confirm such a line was not itself
concealing, duplicating, or replacing the real terminal event (for example a
single event object that declares `type` twice, once as `turn.completed` and
once as `turn.failed` — a naive single-value property read could silently
select either declaration and never surface the other). Only a line that
parses as a well-formed, unambiguously single-typed event of a kind this
reader does not otherwise recognize is safely ignored. Usage is read only
from a clean process exit whose stdout was not truncated. The Domain accepts
this evidence only for the exact provider/schema pair `Codex` +
`codex-cli-usage-v1`; even a well-formed persisted row with a mismatched
provider or schema (for example a stray `claude-cli-usage-v1` value on a
Codex-produced row) projects as unknown, not as known usage.

Each role's status API and the run cockpit expose a separate `tokenUsage`
object beside `outcome` and `processExecution` — `inputTokens`,
`outputTokens`, `cacheCreationInputTokens`, and `cacheReadInputTokens`, each
null while unknown. The schema version is internal provenance and is never
exposed, and no path, argument, environment value, output, session
identifier, or credential is included. This per-attempt projection shares the
same fail-closed rule as the run-wide summary below, and shares it through a
single Domain-level source of truth rather than a duplicated check:
`Attempt.GetAgentTokenUsageEvidence()` itself reports unknown usage whenever
the attempt is still `Running` or has never been dispatched
(`AgentDispatchedAtUtc` is `null`), even when its persisted row already
carries seemingly well-formed token fields (for example a corrupted or
prematurely-populated row) — every one of the per-role status endpoints
(planner, critical review, challenge resolution, code review, implementation,
review correction) and the run cockpit's own `latestAgentAttempt.tokenUsage`
call this same method, so the guarantee holds identically everywhere a single
attempt's usage is projected, not only in the run-wide accumulator.

The run cockpit also exposes a `tokenUsageSummary` across every dispatched
Agent attempt of the run, with `attemptsWithKnownUsage`,
`attemptsWithUnknownUsage`, and sums over the known attempts only. Its
`completeness` is `NoDispatchedAttempts` when nothing has been dispatched,
`Complete` only when every dispatched attempt is terminal and has known usage,
`PendingEvidence` when at least one dispatched attempt is still running and
every terminal attempt observed so far has known usage, and `Partial`
whenever at least one *terminal* attempt lacks known usage (with or without
other attempts still running). Only a `Complete` summary is ever presented as
the run's total; a `Partial` or `PendingEvidence` sum is shown as covering
only the number of attempts it actually includes. `PendingEvidence` is kept
distinct from `Partial`: nothing has failed to report usage in that state,
some dispatched attempts simply have not concluded yet — a still-`Running`
attempt's usage is never trusted, even when its persisted row already
carries seemingly-valid token fields (for example from a corrupted or
prematurely-populated row); only a terminal attempt's usage evidence is ever
summed or counted as known.

`attemptsWithUnknownUsage` keeps its original, broader meaning — every
dispatched attempt without known usage, for any reason — so no existing
reader of that field is broken by this distinction. Two additional bounded
counts split it further without replacing it: `pendingAttemptCount` (still
running; not a failure) and `terminalAttemptsWithUnknownUsage` (concluded
without a trusted usage contract; a genuine gap). `attemptsWithUnknownUsage`
always equals `pendingAttemptCount` plus `terminalAttemptsWithUnknownUsage`.
The read side passes each dispatched attempt's own terminal status alongside
its usage evidence into the same one-pass, constant-memory accumulator used
before; it neither imposes a row cap nor materializes all attempts to compute
the summary.
A Codex-dispatched attempt whose invocation captured a clean, complete,
unambiguous terminal `turn.completed` event now reports known usage like a
Claude-dispatched attempt does; a run's summary reports a terminal-unknown
attempt only when that attempt's provider genuinely reported nothing, or
reported it in a shape this evidence policy does not trust — never merely
because the attempt's provider is Codex, and never because the attempt simply
has not finished yet. This slice records and displays evidence only; it adds
no token budget, threshold, warning, or stop guardrail. Provider-reported
per-invocation tokens are not account usage, cost, or an enforceable token
budget for either provider.

The cockpit's presentation of a `Partial` or `PendingEvidence` summary names
`pendingAttemptCount` and `terminalAttemptsWithUnknownUsage` separately rather
than folding them into one undifferentiated gap: a still-running attempt is
always described as "still running," a concluded attempt with no trusted
usage contract is always described as having "concluded without usable
token-usage evidence," and both clauses are shown together only when both
counts are genuinely nonzero (a run with known, pending, and terminal-unknown
attempts at once). When `attemptsWithKnownUsage` is zero, the presentation
shows no numeric token count at all — the summary's zero-valued sums in that
state reflect an unpopulated accumulator, not a provider-reported zero — and
once at least one attempt has known usage its sum is always shown exactly as
reported, including a genuine zero.
budget for either provider.

## Claude-reported model context limits

The same Claude result envelope may carry an optional `modelUsage` map. It was evidenced like the usage contract above, and
additionally from the official [programmatic CLI guide](https://code.claude.com/docs/en/headless) (print mode is the CLI form of the
Agent SDK) and the SDK's [TypeScript reference](https://code.claude.com/docs/en/agent-sdk/typescript), which declares `modelUsage`
with `contextWindow` and `maxOutputTokens`: never from an authenticated invocation, a model catalog, a CLI default, an alias or a
token total. See [ADR-0023](../decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md).

**Observation.** The three Claude adapters read it from exactly the envelope and exit boundary their usage reader uses: a
structurally valid envelope of a clean process exit whose stdout was not truncated (a valid `is_error: true` envelope still carries
it). A non-zero exit, a timeout, a cancellation, an incomplete capture, an invalid outer envelope and an invocation with no process
result supply none. Only each map key (the model identifier) and its `contextWindow` and `maxOutputTokens` are retained; costs, usage
totals, `canonicalModel`, routing and every other member are ignored, and an entry is never interpreted as the main model or a
fallback. The existing observed model and effort and the token usage are unchanged, and no invocation argument, authentication,
permission or session behavior is involved.

**Admission** is bounded and all-or-unknown: exactly one `modelUsage` object; 1 to 16 unique identifiers compared ordinally and never
normalized, each 1 to 128 ASCII characters matching `[A-Za-z0-9][A-Za-z0-9._-]*`; each entry an object holding exactly one occurrence
of each required member; both members positive JSON integers that fit a 32-bit signed integer; and the output limit no greater than the
window. A missing, empty, duplicated, malformed, excessive or unsupported map is absent optional evidence: no valid subset is kept, and
the business result, the process evidence and the token usage are judged independently (missing limits keep valid usage, missing usage
keeps valid limits). One Domain rule judges the shape for both the parser and the recording policy.

**Recording.** A provider-neutral immutable value (the fixed source tag `claude-cli-model-usage-v1` and the entries) travels through the
three invocation results, supervisors and recording commands, and the existing completion transaction records it once with the
outcome, artifacts and other evidence, including for an unsuccessful semantic outcome. It is persisted as one nullable `TEXT` column on
the attempt holding a canonical, versioned, project-owned snapshot (`{"version":1,"source":…,"models":[…]}`, entries ordered
ordinally, compact, at most 4 KiB of UTF-8); only Infrastructure parses provider field names. The Domain accepts it only for a
dispatched `ClaudeCode` attempt with that exact source and an outcome outside the pre-invocation set that process and token evidence
share, and only once. Rows that predate the column stay `NULL`: nothing backfills or reparses an artifact.

**Reading.** `Attempt.GetAgentModelContextLimitsEvidence()` accepts stored text only when it re-serializes to exactly itself as valid
evidence for the attempt's provider; malformed, oversized, reordered, extended, wrong-provider and unknown-version text projects as
absent without throwing and without affecting a sibling, and a `Running`, undispatched, non-Agent or identity-incoherent attempt
exposes none. The read uses stored facts only: no provider probe and no artifact read.

**Exposure.** `GET /runs/{runId}/agent-attempts/{attemptId}/evidence` carries one additive nullable `modelContextLimits` member
(`models`, each with `modelId`, `contextWindowTokens` and `maxOutputTokens`, ordered ordinally). The source tag and snapshot version
are never exposed, and no other status, cockpit or history-list contract changes.

This is historical, provider-reported observation. It is not remaining context, a fullness measure, a live capability, an eligibility
decision, proof that a listed model was used, an account allowance, or a basis for compaction or resume.
