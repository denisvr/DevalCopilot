# Agent collaboration invocation configuration

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the Codex model and reasoning-effort catalog contract and the
owner-requested invocation configuration: explicit Codex model and
reasoning-effort requests, explicit Claude model-alias and effort requests, and
the optional Claude agentic-turn limit for mutation attempts. The catalog is a
read-only observation, never model or effort selection or a guarantee that a
listed model remains available at dispatch. Each request is a run-scoped owner
preference that a later claim snapshots into the new attempt; dispatch reads
only that attempt's immutable snapshot, never the mutable run value. A request
is never an observed or effective model, effort or turn count. The catalog
reuses the session mechanics of the account-allowance contract in the
[account usage](agent-collaboration-account-usage.md) companion, which is what
the catalog's unlinked "allowance contract" references mean. The sections
below, except the last, are moved verbatim; the protocol's original headings
forward here, and their directional "above" and "below" references and their
two in-page section links mean sections in this companion. The last section
relocates the provider configuration observations from the end of the
protocol's former Review correction body with one wording change; its own
opening paragraph states its historical scope. Provider token-usage evidence
and token-activity controls are in the
[usage evidence](agent-collaboration-usage-evidence.md) and
[token-activity controls](agent-collaboration-token-activity-controls.md)
companions; review correction and the claim and time budgets are in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion; runtime preflight is in the
[provider contracts](agent-collaboration-provider-contracts.md#provider-runtime-configuration)
companion. The last section's opening paragraph and link were written when that
host-runtime-preflight projection stayed in the protocol; the link reaches the
protocol's retained provider runtime configuration heading, which forwards to
it. Every other subject not moved to a companion stays in the protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Codex model and reasoning-effort catalog contract

**Codex — read-only, picker-visible model catalog observation, distinct from
both the [provider token-usage contracts](agent-collaboration-usage-evidence.md#provider-token-usage-contracts) and the [provider account-allowance contracts](agent-collaboration-account-usage.md#provider-account-allowance-contracts).** The official Codex App Server `model/list` method
(`https://learn.chatgpt.com/docs/app-server#list-models-modellist`) returns a
`data` array of client/account-specific models, each with `id`, `displayName`,
an optional `hidden` flag, `supportedReasoningEfforts` (an array of
`{reasoningEffort, description}` objects), and an optional
`defaultReasoningEffort`, plus an opaque `nextCursor` for bounded pagination.
The documented request accepts `includeHidden` and a page-size `limit`; the
installed `codex-cli 0.158.0-alpha.2.1` build's own generated App Server schema
(`ClientRequest.json` for the request envelope, `v2/ModelListParams.json` for
bounded cursor paging and `includeHidden`, and `v2/ModelListResponse.json` for
the model/effort fields) confirms cursor-based paging for that installed build.
The documentation explicitly states that "available models, reasoning efforts,
and defaults depend on the client and account" — its examples are illustrative,
never a permanent or exhaustive catalog, and a listed model is never inferred
to remain available, authenticated, or eligible to invoke at dispatch time.

This reuses the exact same launch-target revalidation, `--stdio` handshake, and
process-tree-cleanup mechanics the [account-allowance contract](agent-collaboration-account-usage.md#provider-account-allowance-contracts)
established, factored into one shared session so neither contract's wire
behavior changed when the second was added. The read/write handle used after
the handshake is a closed, method-specific surface — never a general-purpose
JSON-RPC escape hatch or an arbitrary raw-JSON write path: it exposes exactly
the two reviewed read-only methods this application ever sends
(`account/rateLimits/read` and `model/list`), each building its own fixed
request JSON internally from validated primitive parameters. `includeHidden:
false` is always requested; any entry the provider still marks `hidden: true`
is discarded defensively rather than trusted into the picker-visible
projection. Pages are followed only through a bounded number of `nextCursor`
continuations, each itself bounded to a small page size and a bounded total
entry count across every page — a provider that still claims more pages exist
once that bound is reached is never presented as a silently truncated but
otherwise "complete" catalog: the whole observation reports `Unknown` instead.

A model's own `id` is treated like the allowance contract's limit id: bounded,
restricted to a safe identifier character set, and required to be unique
across every page — a missing, oversized, malformed, or duplicate id fails the
whole catalog closed, never just that one entry. `displayName` is descriptive
data, not an identifier: it falls back to the model's own already-validated id
whenever it is missing, oversized, blank, or carries a control or
bidirectional-formatting character (for example a Unicode right-to-left
override, which could otherwise make a rendered name visually misrepresent
itself) — never failing the entry over display text. `supportedReasoningEfforts`
is read as a whole: a malformed or duplicate individual effort makes the
entire field Unknown rather than presenting a partial list with the bad
element silently dropped, since a partial list would misrepresent what the
provider actually reported; a genuinely excessive list still fails the whole
catalog closed, and an absent or explicitly empty list is a valid, non-Unknown
empty result. `defaultReasoningEffort` is projected only when it is itself a
bounded, valid identifier *and* a member of that same entry's own known
(non-Unknown) `supportedReasoningEfforts` — an internally inconsistent or
unverifiable default is Unknown rather than an unchecked claim. A JSON-RPC
`error` on the handshake or the `model/list` method, a missing vetted Codex
launch target, a timeout, cancellation, or any process failure all resolve to
the same `Unknown` state, exactly like the allowance contract. This is catalog
evidence only — never model or effort selection, invocation arguments, attempt
assignment, account authentication, or a guarantee that a listed model remains
available at dispatch.

## Explicit Codex model and reasoning-effort requests

A run-scoped, durable, explicit owner preference (`RequestedCodexModel`,
optional `RequestedCodexEffort`) may be set or cleared through one protected
command and cockpit control, populated from the catalog above. Saving a
non-null preference requires one fresh bounded catalog observation: the model
must be a currently visible observed id, and a non-null effort must belong to
that entry's own known (non-Unknown) supported-effort set. The catalog's own
suggested default effort is never auto-selected. An `Unknown` or unavailable
catalog observation never validates a new selection; clearing the preference
(a `null` requested model) never reads the catalog at all. An effort is never
accepted without a model, enforced identically in three places: `Run.SetRequestedCodexAssignment`,
each of the three Codex `ClaimAgent*WithAssignment` factories below, and the
shared invoker's own boundary further below — never only at the outermost
layer. The command that sets or clears this preference is a manual-transaction
command (mirroring the existing `CreateCodexPlanningAttemptCommand` shape):
its bounded external catalog observation runs with no EF transaction open and
no tracked entity pending a write, and only after that call completes does it
read the Run afresh, inside one short, tightly scoped write that re-validates
the run's lifecycle and commits the preference and its change event together.
That short write is itself guarded against a lifecycle transition landing in
the narrow gap between this fresh read and its own single `SaveChangesAsync`
call, with no further I/O of the handler's own in between to re-check
against: `Run.Lifecycle` is configured as an EF concurrency token, so the
`UPDATE` this save produces requires the exact `Lifecycle` value the fresh
read observed, and a concurrent transition committed in that gap makes the
`UPDATE` match zero rows and throws `DbUpdateConcurrencyException` — caught
and reported identically to the ordinary in-memory lifecycle rejection, with
neither the preference nor its event persisted, rather than closed with an
explicit multi-statement transaction. This preference is
provider-neutral policy stored on `Run`, never on `Attempt` — it is completely
separate from each attempt's own immutable requested assignment
(`AgentRequestedModel`/`AgentRequestedEffort`, already defined by
[ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md))
and from any provider-observed fact. Changing the Run's preference while an
attempt is already claimed never mutates that claimed attempt; only a later
claim reads the Run's then-current value. At each of the three current Codex
claim paths (Planner, Challenge Resolver, Code Reviewer), the Run's requested
pair is copied once, at claim time, into the new attempt's own immutable
assignment fields — the same fields ADR-0009 already reserved. Every
supervisor and adapter dispatches from that claimed attempt's own snapshot,
including after a host restart; none of them ever reads the Run's own
(possibly since-changed) mutable preference at dispatch time.

Each of the three `Create*Attempt` claim handlers loads its own tracked `Run`
once, at the very start of the request, long before the external Git evidence
capture and artifact-sealing
work that follows — potentially the longest external round trip in the whole
claim. The pair copied into the new attempt's assignment fields is never read
from that early-loaded instance: a shared
`CurrentCodexAssignmentPreference.ReadAsync` helper issues a genuinely fresh,
untracked query for only the two preference columns, called in all three
handlers as late as possible — after both the external Git evidence capture
and the artifact-sealing work have already completed.

That read alone does not guard the narrower gap between itself and the
claim's own durable commit, and `Run.Lifecycle`'s own concurrency token (see
above) guards nothing here either, since these two columns are never part of
that unrelated check. A companion
`CurrentCodexAssignmentPreference.ConfirmUnchangedAsync` guard — one
`ExecuteUpdateAsync` statement whose `WHERE` clause requires the Run's two
preference columns to still exactly match the pair `ReadAsync` returned, with
a deliberate no-op `SET` (writing a column back to its own current value)
whose only purpose is the database-enforced compare — is *atomic only for its
own single statement*. An earlier version of this fix relied on that guard
alone, immediately before `SaveChangesAsync`; a review round correctly
identified that nothing then prevented a preference-only change from
committing in the still-open gap between the guard and the Attempt's own
insert moments later, since these remained two independently-atomic
statements rather than one operation.

The gap is closed by making the guard and the claim's own durable commit one
genuinely atomic database operation: each handler opens an explicit
`IDevalCopilotDbContext.BeginTransactionAsync` only after all external work
has completed, performs the guard and the Attempt/artifact/input-message/
verification-evidence inserts and `SaveChangesAsync` inside that same
transaction, then commits. Each of these four steps — acquiring the
transaction, the guard, the save, and the commit — has its own bounded
failure path, and every one deletes the already-sealed manifest artifact
unless an `IAttemptDurabilityProbe` check confirms the Attempt durably
persisted despite the failure. The guard's own zero-affected-rows outcome is
the only case reported as `agent_attempts.assignment_preference_changed`; a
raw provider failure at any of the four steps
(`System.Data.Common.DbException`, covering a bounded lock-wait timeout) is
never described as a confirmed preference change — it is reported as
`attempts.persistence_failed`, the same code the existing
`DbUpdateException` recovery already falls back to for an unclassified
cause. The connection's own SQLite lock-wait is bounded by
Microsoft.Data.Sqlite's own unconfigured default (30 seconds) — confirmed by
direct, out-of-process measurement to be a genuine bound, not an indefinite
wait as an earlier, too-short-patience test run had wrongly concluded — so a
genuine conflict fails within that bound rather than waiting forever. An
explicit shorter timeout configured globally in `Program.cs`'s connection
string was tried and reverted: several existing integration test fixtures
compute their own literal connection string for pool cleanup, and the
rewritten string not matching it byte-for-byte left database files locked
across roughly 238 unrelated tests.

The commit- and save-time failure paths do not resolve that ambiguity with a
query against the claim's own `IDevalCopilotDbContext` — a review round
found that a query against the same connection whose own transaction a
best-effort rollback had just failed to close cleanly is not a reliable
answer, since that connection's transaction state is exactly what is
uncertain. `RollbackBestEffortAsync` now both attempts the rollback and
releases (disposes) the transaction, swallowing either step's own failure,
before an `IAttemptDurabilityProbe` is asked — a small, dedicated
`Application.Data` port implemented in Infrastructure as
`AttemptDurabilityProbe`, which opens a brand-new `DevalCopilotDbContext`
(its own independent connection to the same database) and reads whether the
Attempt row exists, bounded by its own 5-second timeout distinct from the
claim connection's own 30-second lock-wait bound. The probe returns one of
three outcomes: `Persisted` (the commit actually succeeded — report
success, delete nothing), `NotPersisted` (the commit genuinely never landed
— clean up and report `attempts.persistence_failed`, continuing into the
existing race-classification checks for a `DbUpdateException`), or
`Unresolved` — the independent probe's own bounded read itself failed or
timed out, most often because the same lock contention that made the
original outcome ambiguous is also blocking the probe's own read. On
`Unresolved`, the handler asserts neither success nor failure: it preserves
the sealed manifest exactly as it would for a confirmed `Persisted` outcome,
since the Attempt may still be durably referenced, and reports
`attempts.persistence_unresolved` rather than guessing.

Each of the same four steps also has its own
`catch (OperationCanceledException)`, separate from its `DbException`/
`DbUpdateException` catch: cancellation of the caller's own token is never
converted into a `Result` — it always propagates as cancellation — but the
sealed manifest artifact's ownership is still resolved first, using the same
`IAttemptDurabilityProbe` check and an unconditional token (the caller's own
is already cancelled), so a cancellation that raced with an already-completed
commit never deletes a file the database still references; `Unresolved`
here too means the file is preserved, never deleted, before the
cancellation is rethrown.

All three `Create*Attempt` claim commands are manual-transaction commands, so
none of their external Git evidence capture or artifact-sealing work ever
runs inside the mediator's automatic per-command EF transaction.
`CreateCodeReviewAttemptCommand` was found to be the exception — a plain
`ICommand`, meaning that work had been running inside that automatic
transaction the whole time, a pre-existing defect unrelated to this
increment's own preference-request work. Corrected to
`IManualTransactionCommand`, matching its two siblings exactly; its handler's
existing explicit `SaveChangesAsync` call and `DbUpdateException` cleanup
needed no change.

The shared `CodexProcessInvoker` extends its otherwise fixed argument list
with exactly two conditional flags, added only when the claimed attempt's own
requested value is non-null: `--model <id>` and `--config
model_reasoning_effort=<effort>`. The official
[Codex developer commands](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
document `codex exec --model/-m <value>` as overriding the configured model for
the run, and a repeatable `-c/--config <key=value>` inline configuration
override; the official
[configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
documents `model_reasoning_effort` as a string key whose available levels
"depend on the model and client." Both were independently confirmed against
the live documentation for this slice; the installed `codex-cli
0.158.0-alpha.2.1` build's own local `codex exec --help` (cited in the
planner's own selection record) independently lists `--model`, `--config`,
`--ignore-user-config`, and `--ephemeral` as accepted flags on that build, a
carried-over observation this executor's environment has no local CLI to
reproduce directly. With no requested override, the exact existing argument
list, sandbox, `--ephemeral`, `--ignore-user-config`, output schema, stdin
delivery, and output/time bounds are unchanged byte-for-byte — proven by the
unmodified passing exact-argument-list regression tests already covering the
override-free path. Each requested value is independently revalidated against
the same bounded, safe identifier character set immediately before it is ever
placed on the command line, regardless of where it was validated earlier in
the chain — never a raw, unchecked pass-through. The invoker also independently
re-enforces effort-requires-model as its own boundary-level guard: an
effort-only request fails the whole invocation closed before any process
starts, never trusting that the claimed attempt it was given already enforced
this upstream. This is invocation-argument
policy only: it never infers, claims, or records an *observed* model or
effort — `AgentObservedModel`/`AgentObservedEffort` remain null unless
authoritative provider output itself reports them, exactly as before. In that
Codex-only delivery, Claude paths and their own fixed arguments were entirely
unaffected; the later Claude model-alias request slice is described in
["Explicit Claude model-alias requests"](#explicit-claude-model-alias-requests).

## Explicit Claude model-alias requests

A run-scoped, durable, explicit owner preference (`Run.RequestedClaudeModel`) may
be set or cleared through one protected MVC operation
(`POST /api/runs/{runId}/claude-model-preference`) and a cockpit control. It
applies to the three current Claude roles only: CriticalReviewer, Implementer
(initial implementation), and ReviewCorrection. The selectable values are the
closed, case-sensitive set `sonnet`, `opus`, and `haiku` (`ClaudeModelAlias`),
which the official
[Claude CLI reference](https://code.claude.com/docs/en/cli-reference) documents
for `--model`; the installed `claude` 2.1.276 `--help` independently lists
`--model <model>` and alias examples (it names `fable`, `opus`, and `sonnet`, not
`haiku`, as examples; the reference is the authority for the closed set). This is
request syntax, not live model discovery: nothing here proves an alias is
available to the signed-in account, no catalog is read, and a CLI default is never
inferred to be an observed or effective model. `null` means no override.

- **Persistence.** The additive `AddClaudeModelPreference` migration adds one
  nullable `runs.RequestedClaudeModel` column with no default and no backfill, so
  every historical Run stays `NULL` (no request recorded). Historical Attempts
  are not backfilled: their `AgentRequestedModel` stays as recorded.
- **Set/clear.** `SetClaudeModelPreferenceCommand` is a manual-transaction command
  (its one `SaveChangesAsync` owns the implicit transaction that commits the Run
  change and its `run.claude_model_preference_changed` event together; under the
  mediator's ambient transaction a failed concurrency-checked UPDATE could leave
  the event INSERT behind). The validator and `Run.SetRequestedClaudeModelRequest` both
  reject anything outside the closed set before any write (HTTP 400), and a
  terminal Run is rejected (HTTP 422, `claude_model.run_not_editable`). The
  handler forces the Run UPDATE even when the alias is unchanged, so the
  concurrency tokens below always guard the lifecycle.
- **Concurrency.** `Run.Lifecycle` and `Run.RequestedClaudeModel` are EF
  concurrency tokens. A lifecycle transition committed between the handler's read
  and its save makes the UPDATE match zero rows: the failed save rolls back both
  the Run change and the event, and the handler reports a terminal run as not
  editable, or another preference change as a retryable conflict
  (`claude_model.concurrent_change`, HTTP 409).
- **Claim-time snapshot.** Each of the three claim handlers calls
  `CurrentClaudeModelPreference.ReadAndGuardAsync` as late as possible — after its
  external Git evidence capture and manifest sealing, immediately before the
  Attempt is constructed. It reads the column afresh (untracked), snapshots that
  value into the new Attempt's existing immutable `AgentRequestedModel` (via
  `ClaimAgentCriticalReviewWithModelRequest`, `ClaimAgentReviewCorrectionWithModelRequest`,
  and the existing `ClaimAgentImplementationWithAssignment`), and marks the tracked
  Run's `RequestedClaudeModel` modified with that value as its original value. The
  claim's single `SaveChangesAsync` therefore includes
  `UPDATE runs ... WHERE Id, Lifecycle, RequestedClaudeModel = <snapshot>` in the same
  transaction as the Attempt insert, so an Attempt can never commit a snapshot that
  was no longer current at commit. A change committed after the read rolls the whole
  batch back; the handler removes the sealed manifest it wrote and returns
  `agent_attempts.run_changed_during_claim` (HTTP 409, retry re-reads). A change
  committed during external work is simply reflected in the snapshot. Requested
  effort is `null` unless the later effort extension below applies. The claim paths' assignment
  validation, permission profiles, budgets, one-running-attempt rule, Git/workspace
  checks, and authorization rules are unchanged.
- **Dispatch.** The eligible-attempt queries project the Attempt's own
  `AgentRequestedModel` into `CriticalReviewInvocationRequest`,
  `ImplementationInvocationRequest`, and `ReviewCorrectionInvocationRequest`
  (`RequestedClaudeModel`), which the three supervisors pass unchanged. The mutable
  Run value is never read at dispatch, so a later preference change cannot alter a
  claimed attempt's invocation, including after a restart.
- **Adapters.** `ClaudeModelRequestArguments` is the one place the three adapters
  translate the snapshot: a `null` request appends nothing, leaving each role's
  argument list byte-for-byte unchanged (proven by the existing exact-argument-list
  tests); a member of the closed set appends exactly `--model <alias>` as two
  discrete arguments after the fixed list; anything else fails the invocation closed
  before any process starts. Each role passes exactly its own snapshot. The effort argument added by [the later slice](#explicit-claude-effort-requests) is the only extension; no
  permission, tool, session, settings, or schema argument changes.
- **Provider rejection.** If the provider rejects the alias, the invocation ends as
  the ordinary recorded failure (`ProviderInvocationFailed`); there is no retry,
  fallback model, or automatic clear. Attempt-level `AgentObservedModel` stays
  `null` unless authoritative provider output reports one.
- **Read model.** The cockpit projection adds `requestedClaudeModel` (the Run's
  current request for future attempts) and `latestAgentAttempt.requestedModel` (that
  attempt's own immutable snapshot). Both are requests; no observed or effective
  model is exposed.

## Explicit Claude effort requests

The model-alias request above is extended by one optional **requested effort**
(`Run.RequestedClaudeEffort`) for the same three Claude roles. The closed,
case-sensitive levels are `low`, `medium`, and `high` (`ClaudeEffortLevel`), and
`ClaudeModelRequest.IsValid` is the single pair rule shared by the Run, the
Attempt claim factories, the validator, and the adapter argument boundary: an
effort is accepted **only** with an explicitly requested `sonnet` or `opus`
alias; `haiku` or no model is valid only with a `null` effort. The
[Claude CLI reference](https://code.claude.com/docs/en/cli-reference) documents
`--effort`, and the [model configuration](https://code.claude.com/docs/en/model-config)
page documents model-dependent support and that organization or model limits can
alter the level applied, including silently in JSON output mode. The installed `claude`
2.1.276 `--help` independently lists `--effort <level>` (it also names `xhigh` and `max`,
which this slice deliberately does not expose). The pair rule
is therefore request syntax under the currently documented support, not
capability discovery: no catalog is read, no CLI default is inferred, and neither
account eligibility nor any *effective* or *observed* effort is claimed. This slice does
not derive an observed or effective effort from the `--effort` request and adds no
observed-effort field; it neither records nor displays one. (Provider-reported
observed assignment facts that other paths already record, such as
`ImplementationAttemptStatusResponse.ObservedEffort`, are separate and unchanged.)
The provider may reject or adjust a request; a rejection is the
ordinary recorded failed invocation with no retry or fallback, and a silent
adjustment is a documented limitation that this design cannot observe.

- **Persistence.** The additive `AddClaudeEffortPreference` migration adds one
  nullable `runs.RequestedClaudeEffort` column with no default and no backfill; a
  historical Run keeps `NULL` and its stored model request unchanged. Attempts
  reuse the existing immutable `AgentRequestedEffort` column (no new Attempt
  column); it stays `NULL` for every earlier Claude attempt.
- **Set/clear.** The existing `POST /api/runs/{runId}/claude-model-preference`
  operation accepts `requestedModel` and `requestedEffort` as one pair
  (`SetClaudeModelPreferenceCommand`). The validator rejects an unknown level, and
  an effort without `sonnet` or `opus` (HTTP 400), before any write; a terminal
  Run is still HTTP 422. The pair and one `run.claude_model_preference_changed`
  event (payload `{requestedModel, requestedEffort}`) commit in the handler's
  single `SaveChangesAsync`. Clearing removes both values in that one event.
- **Concurrency.** `RequestedClaudeEffort` is an EF concurrency token alongside
  `Lifecycle` and `RequestedClaudeModel`, and the handler forces both preference
  properties into its UPDATE, so a competing change to *either* value (including an
  effort-only change) or a lifecycle transition makes the save match zero rows and
  roll back the Run change and its event together (retryable HTTP 409, or 422 for a
  now-terminal run). A token-warning threshold save shares the Run row and can
  therefore also return a retryable 409 during a concurrent preference change.
- **Claim-time snapshot.** `CurrentClaudeModelPreference.ReadAndGuardAsync` now reads
  both columns in one untracked query as late as possible in each of the three claim
  paths (after external Git and manifest work, before the single claim commit),
  returns the immutable pair, and resets both tracked Run properties' original values
  to it. The claim's `UPDATE runs ... WHERE Id, Lifecycle, RequestedClaudeModel,
  RequestedClaudeEffort` therefore requires the exact pair the Attempt snapshotted; a
  change of either value after the read rolls the whole batch back, the handler
  deletes the sealed manifest it wrote, and `agent_attempts.run_changed_during_claim`
  (HTTP 409) is returned. `ClaimAgentCriticalReviewWithModelRequest`,
  `ClaimAgentReviewCorrectionWithModelRequest`, and
  `ClaimAgentImplementationWithAssignment` validate the pair and store it in
  `AgentRequestedModel`/`AgentRequestedEffort`. Budgets, eligibility, permission
  profiles, and authorization are unchanged.
- **Dispatch.** The eligible-attempt queries project the Attempt's own
  `AgentRequestedEffort` into the three invocation requests
  (`RequestedClaudeEffort`), which the supervisors pass unchanged; the mutable Run is
  never read at dispatch or restart replay.
- **Adapters.** `ClaudeModelRequestArguments.TryAppend` validates the pair with
  `ClaudeModelRequest.IsValid` and appends, as discrete arguments after the
  existing list, `--model <alias>` and then `--effort <level>` only for a valid
  non-null value. A `null` request and a model-only request keep exactly their
  previous argument lists; a malformed persisted pair (bad level, effort without
  `sonnet`/`opus`, effort without a model) fails the invocation closed before any
  process starts. No permission, tool, schema, session, or settings argument changes,
  and the Codex paths are untouched.
- **Read model.** The cockpit adds `requestedClaudeEffort` (the Run's current request
  for future attempts) and `latestAgentAttempt.requestedEffort` (that attempt's own
  claim-time snapshot). Both are requests; these new fields never carry an observed or
  effective effort. The frontend shows the attempt's effort line only for a Claude attempt.

## Optional Claude agentic-turn limit for mutation attempts

The two Claude paths that can edit the worktree — the initial Implementer and review correction — accept one
optional, owner-requested agentic-turn limit, passed as `--max-turns N`. The
[Claude CLI reference](https://code.claude.com/docs/en/cli-reference) documents `--max-turns` as a print-mode
limit on agentic turns that exits with an error when reached, and the
[headless contract](https://code.claude.com/docs/en/headless) describes non-zero exits for failure and invalid
flags. No installed-version or authenticated runtime observation is claimed: the argument is the documented
explicit one, and the tests use deterministic process doubles, which do not prove real provider enforcement. The
limit is a request for a provider-loop guardrail beside the existing host timeout. It is never a measured turn
count, a token, cost, or account ceiling, a host-enforced resource limit, a replacement for any budget or stop, or an
invocation-eligibility claim. `CriticalReviewer` keeps exactly `--max-turns 1`; no Codex invocation changes.

- **Request and validation.** `Run.RequestedClaudeMaxTurns` is `null` (no DevalCopilot override, the value for every
  new and historical run) or a whole number from 1 through 100 inclusive (`ClaudeMutationTurnLimit`, the single rule
  shared by the Run, the Attempt factories, the validator, and the adapters). The protected operation
  `POST /api/runs/{runId}/claude-mutation-turn-limit` (`SetClaudeMutationTurnLimitCommand`, body
  `{ "maxTurns": N | null }`) sets or clears it for a Created or Running run. The member must be present (an omitted
  member is HTTP 400, not a clear); zero, negative, above 100, fractional, string, and overflowing values are HTTP 400
  before any write; a terminal run is HTTP 422 (`claude_turn_limit.run_not_editable`); an unknown run is HTTP 404. The
  request and one human-authored `run.claude_mutation_turn_limit_changed` event (payload `{maxTurns}`) commit in the
  handler's single `SaveChangesAsync`, with no provider call. `Run.Lifecycle` and `Run.RequestedClaudeMaxTurns` are EF
  concurrency tokens and the UPDATE is forced even for a same-value set, so a lifecycle transition or competing change
  rolls back the change and its event together (retryable HTTP 409 `claude_turn_limit.concurrent_change`, or 422 for a
  now-terminal run). A change affects only later claims, including while an earlier attempt is still running.
- **Immutable claim snapshot.** `Attempt.AgentRequestedMaxTurns` (one additive nullable column; the
  `AddClaudeMutationTurnLimit` migration adds it and `runs.RequestedClaudeMaxTurns` with no default and no backfill)
  records the value the claim decided against. `CurrentClaudeMutationTurnLimit.ReadAndGuardAsync` reads the column
  afresh in both mutation claim handlers, after external Git evidence and manifest sealing and before the single claim
  commit, and resets the tracked Run property's original value to it, so the claim's `UPDATE runs ... WHERE` requires
  that exact value. A change committed after the read rolls the whole batch back (attempt, ordered inputs, manifest
  artifact, and any consumed review-correction authorization), the handler deletes the sealed manifest it wrote, and
  `agent_attempts.run_changed_during_claim` (HTTP 409) is returned. A stored value outside the accepted range is refused
  with `agent_attempts.claude_turn_limit_invalid` (before any authorization is consumed, with the manifest removed),
  never clamped or claimed without its limit. The ordered correction inputs and guidance, authorization consumption, the
  count, time, and token gates, leases, and the one-running-attempt invariant are unchanged.
- **Persisted-state integrity.** Both columns are a field-only EF property (beside the `Run.RequestedClaudeMaxTurns` and
  `Attempt.AgentRequestedMaxTurns` accessors) in an INTEGER-affinity column, exposed to the Domain as one string whose form
  identifies the SQLite storage class (`ExactStoredIntegerTextTypeMapping`, used for these two columns only). The
  representation is disjoint because every non-integer class carries a one-letter tag that is never the first character of
  a canonical integer: an `integer` is its canonical digits; a `real` is `r:` plus the 16 hex digits of its exact IEEE-754
  bits (so every finite value, both infinities, and negative zero are preserved without decimal rounding); a `blob` is `b:`
  plus its exact bytes in hex; an actual `text` is `t:` plus the text verbatim, so TEXT that merely looks like another form
  (`blob:37`, `b:37`, `r:...`, digits, or an empty string) is still TEXT; any other class is `?:` plus its type name. The
  original value is bound back with its actual type and content (an integer as INTEGER, a real from its bits, a BLOB from its
  bytes, TEXT after its tag); nothing is inferred from untagged text, and a malformed suffix never throws (an untagged or
  ill-formed string is bound as plain text). The SQL literal form uses the same decoding. `ClaudeMutationTurnLimit.Read`
  accepts only the canonical digits of a whole number from 1 through 100. Anything else (a fraction such as `3.5`, a number
  too large for an `int`, any TEXT, any REAL, any BLOB including digits, zero, a negative, above 100, a sign, a leading zero,
  or padding) is **malformed**: it is never truncated, decoded, read as a different number, null, or zero, and never allowed
  to overflow the materialization of a healthy sibling row. A whole-valued REAL written to the column is converted to INTEGER
  by the column's own affinity at write time, before any read. The accessors return only a valid request or none and throw
  for a malformed value, so a malformed value cannot be mistaken for "no request"; the exact readings
  (`ReadRequestedClaudeMaxTurns`, `ReadAgentRequestedMaxTurns`) expose the malformed state. A malformed Run value is
  refused at the claim with `agent_attempts.claude_turn_limit_invalid` (manifest removed, correction authorization
  unconsumed), reported as `Unknown` by the role status and the cockpit, and replaced by setting a new valid request:
  the stored text is the concurrency-token original value, so a malformed value still round-trips exactly and neither
  the setter nor any unrelated save of the Run is blocked by it. A malformed Attempt snapshot is never dispatched,
  fails a role status with `agent_attempts.invalid_assignment`, invalidates that attempt's history identity, and is
  `Unknown` in the cockpit. No `CHECK` constraint was added: the read and dispatch boundaries are authoritative and are
  what the tests exercise with corrupted rows, and a later constraint would need a table rebuild.
- **Dispatch gate.** The invocation request carries only the cap and the contract version, so the complete tuple is
  checked before any process can start, in both the eligibility feeds and the authoritative
  `MarkAgentAttemptDispatched` gate (`agent_attempts.invalid_agent_contract`). An attempt that recorded a request is
  dispatchable only with a well-formed request and the persisted Claude provider (both feeds check the attempt's actual
  stored provider, not a constant), the Implementer role, the response contract of a
  mutation path, the `WorkspaceEditOnly` permission profile, and that path's exact version 2 contract
  (`ClaudeMutationAdapterContract.IsDispatchCoherent`). An attempt that recorded none (coherent v1/null and v2/null) is
  unaffected, so historical dispatch eligibility is unchanged, and restart replay still uses the attempt's stored
  values. An attempt that fails the gate stays claimed and undispatched, is never invoked, and is not retried.
- **Versioned invocation contract.** Every newly claimed initial implementation uses `claude-implementation-v2` and every
  newly claimed correction uses `claude-review-correction-v2`, even when the limit is `null`. Version 1 is the historical
  invocation with no override and stays valid history: an already-claimed v1 attempt with a null limit dispatches with its
  original argument list. The Domain factories (`ClaimAgentImplementationWithAssignment`,
  `ClaimAgentReviewCorrectionWithModelRequest`) reject a non-null limit beside v1, an unknown version, or a permission
  profile other than `WorkspaceEditOnly`; the convenience overloads remain v1 with no limit. `ClaudeMutationAdapterContract`
  holds the four exact versions and classifies a stored attempt by exact version-aware mappings.
- **Dispatch and adapters.** The eligible-attempt queries project the Attempt's own `AgentRequestedMaxTurns` and
  `AdapterContractVersion` into the two invocation requests, which the supervisors pass unchanged; the mutable Run is never
  read at dispatch or restart replay, and an undispatched attempt replays from its stored configuration and sealed
  manifest. `ClaudeMutationTurnLimitArguments.TryAppend` appends, after the model and effort arguments, exactly
  `--max-turns` and the invariant-culture integer when a limit is present; nothing for a null request (a v1 attempt and a
  v2 attempt with no limit keep their exact argument lists); and fails the invocation closed before any process starts for
  an out-of-range value or a version other than the path's own exact v2. No argument is dropped and retried unrestricted.
  Every other argument, the model and effort rules, authentication, tool and permission restrictions, stdin delivery,
  stream limits, timeout, and cancellation are unchanged.
- **Failure.** A cap-induced or unsupported-flag error uses the existing failed-invocation path. The system does not infer
  that the cap was reached from an exit code, stderr text, missing output, or the configuration alone, and adds no
  limit-reached outcome. Both mutation supervisors still capture fresh post-invocation Git evidence, retain the available
  process and artifact evidence, mark a suspected mutation `NeedsAttention`, and produce no success checkpoint,
  ExecutionReport, or RevisionResponse from a failed invocation. There is no automatic retry, rollback, resume, or
  fallback, and the failed attempt keeps its one claimed budget slot.
- **Read model.** The two role status views add `runTurnLimitRequest` (the run's current saved request) and
  `attemptTurnLimit` (that attempt's immutable record); the cockpit adds `claudeMutationTurnLimit` and
  `latestAgentAttempt.maxTurns`; the historical Agent attempt evidence adds `maxTurns`. Each is `{state, maxTurns}` with
  `state` exactly one of `Requested` (the saved or snapshotted request `maxTurns`; it describes the request, including for an
  undispatched attempt or a run with no attempt yet, and says nothing about dispatch, which the versioned adapter decides), `NotRequested` (a coherent version 2 attempt, or a run, with
  no request), `NotRecorded` (a legacy version 1 attempt, never an observed unlimited capacity), or `Unknown` (the stored
  facts disagree, for example a limit beside a version that cannot carry it). The status views accept exactly the v1 and v2
  versions of their own path for the existing configured facts; an unknown version still shows none. A malformed
  out-of-range attempt value fails a role status with the existing `agent_attempts.invalid_assignment` error and makes a
  history entry's identity invalid, without disclosing the stored value.

## Provider configuration observations

This section relocates, as written at the protocol baseline, the provider
configuration observations formerly at the end of the protocol's Review
correction section, whose correction contract and budgets are in the
[correction and budgets](agent-collaboration-correction-and-budgets.md#review-correction)
companion. It is historical wording, not a current configuration contract.
The sections above and the
[account usage](agent-collaboration-account-usage.md) and
[token-activity controls](agent-collaboration-token-activity-controls.md)
companions describe requests and controls separately, and this relocation
reconciles or endorses none of the statements below, such as null requested
and observed model and effort, unrecorded context and account usage, or
nothing yet tracked or enforced. Its one wording change replaces
"host-runtime-preflight projection above" with a link to that projection's
section, the protocol's provider runtime configuration, because the
projection stays in the protocol.

Each agent attempt is intended to eventually record requested and effective
provider configuration:

- provider and provider-session identifier;
- model and reasoning effort;
- permission mode;
- context-manifest revision;
- reported context usage and compaction outcome when available.

Every Agent attempt records only a subset of this today: the provider, role,
and response contract are each fixed by the attempt's own dedicated factory
(never caller-supplied), and a provider-session identifier is captured on a
best-effort basis only when the provider's own JSON output reports one — never
required, never trusted for anything beyond this closed, cosmetic field, and
not currently exposed through any API response. The initial Implementer
attempt additionally persists durable assignment facts: requested and observed
model slots, requested and observed effort slots, a permission profile
(`WorkspaceEditOnly`), and an adapter contract version
(`claude-implementation-v1` for history and `claude-implementation-v2` for every new claim, see
"Optional Claude agentic-turn limit for mutation attempts"). In practice its requested and observed model
and effort currently remain null: no model or effort is requested, and the
Claude adapter does not authoritatively report either, so nothing is
inferred. There is still no provider selection — the initial Implementer
remains fixed to Claude Code, no manual provider selection exists, and Gemini
remains deferred by ADR-0011. Context usage and account usage are not recorded
by any Agent attempt; they remain `Unknown` exactly as the
[host-runtime-preflight projection](agent-collaboration-protocol.md#provider-runtime-configuration) already reports them, and no doc,
response, or stored fact should be read as tracking or enforcing them yet.

Adapters expose capabilities and supported values through typed queries. The
application does not assume that Codex and Claude Code use equivalent names or
offer identical controls. Unsupported or unavailable values remain explicit.

A configuration change is persisted as run intent and applies only when the
orchestrator creates the next eligible attempt. Provider permission mode cannot
expand DevalCopilot policy. Manual context compaction is a typed between-turn
operation that creates a new context-manifest revision; it never removes events,
decisions, evidence, or raw artifacts from durable history.

Provider account-usage snapshots are observation evidence rather than agent
claims. A configured hard threshold participates in attempt eligibility and
prevents a new invocation for only the affected provider.
