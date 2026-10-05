# Run cockpit functional specification

## Status

Approved MVP design baseline. The interactive reference is
[`prototypes/cockpit-v1`](../../prototypes/cockpit-v1/index.html).

The prototype demonstrates intended behavior and information hierarchy. It is
not an implementation contract for literal sample values, hard-coded provider
models, simulated events, or visual dimensions.

## Purpose

The run cockpit is the primary supervision surface for a DevalCopilot run. It
must answer five questions without requiring the user to open a raw log:

1. Which project and objective are active?
2. Which agent or tool is working now?
3. What workflow state has been reached, including repeated attempts?
4. What budget, provider allowance, or human decision can stop progress?
5. What evidence supports the current status?

The cockpit is not a shared free-form chat client. It is a structured view of a
durable workflow whose conversation, actions, and evidence are owned by the
DevalCopilot orchestrator.

## Information hierarchy

The desktop layout has four levels:

1. a collapsible global navigation rail;
2. a project and run switcher across the top;
3. a compact run header with objective, state, autonomous time, and primary
   controls;
4. a three-panel workspace with collapsible Workflow and Usage & Evidence
   rails around the responsive Agent Collaboration surface.

The collaboration surface is the visual focus. Collapsing either contextual
rail gives the released width to collaboration cards rather than leaving empty
space. At the supported minimum desktop width, contextual rails become drawers
and collaboration remains usable.

## Project and run switcher

The top switcher shows registered projects with their most relevant active run.
Each item contains only:

- project name;
- concise state such as `Running`, `Review`, `Waiting`, or `Paused`;
- execution number and current stage.

Selecting an item changes the entire cockpit atomically to that run. Data from
two runs must never be visually combined. A loading or stale state remains
visible until the selected run projection catches up.

Atomic means each committed frame, not only the settled state. The frame that selects another project or run shows that
selection's own loading state or its own error, and never the previous selection's header, stages, event cards, connection
or sync status, paths, checkpoints, diffs, command and verification recipes, execution and review lists, drafts, selected
output, or action targets. This holds for a null selection and for a return to an earlier one (each selection is a new
lifetime), and an answer that names another run is refused rather than shown. The project-level candidate-workspace evidence
follows the same rule: metadata, errors and pending flags belong to the selected project, and inspected files and diff
additionally belong to the exact checkpoint inspected, so a newer checkpoint never shows or accepts an older checkpoint's.
Overlapping reads are ordered, so an older answer never overwrites a newer one. A request the host already accepted
(workspace preparation, an identity recheck, a checkpoint capture, a verification command change, a verification start or a
review decision) stays real when the selection changes; its late completion neither reports on, refreshes, nor clears a draft
or pending operation of the replacement, and a handler retained from the earlier selection starts no work for the new one.
The loading, refresh, catch-up coalescing and error-recovery behavior of an unchanged selection is not affected.

`Execution 18` identifies the eighteenth durable run for that project. It is
not a message, interaction, attempt, or event count.

Multiple projects may have active runs concurrently. The UI must distinguish:

- actively consuming an execution slot;
- waiting for an agent, command, CI, approval, or concurrency slot;
- paused by the user or a provider guardrail;
- terminal.

## Run intake and execution mode

A project with no run, or whose runs are all terminal, shows an objective form (nonblank, at most 2,000 characters) that
records a **manual Agent run** through the protected `POST /api/runs/manual` operation
([ADR-0014](../decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)). Recording creates the run
in `Created`/`Intake` and starts nothing: the cockpit states "Waiting for an explicit planning request", and the existing six
stage actions are the only way forward. The simulated walking skeleton remains a separately labelled **demo** action with a
fixed objective. While any run of the project is unfinished the form is replaced by a fixed reason; the availability shown
is only a hint derived from all runs, and the command re-checks on every submission. The form's pending, error, draft, and
success belong to the selected project and its committed interaction; an accepted request that finished after the project
was left remains a real server operation and is neither undone nor resubmitted.

The cockpit and project summaries disclose the run's durable execution mode: "Manual Agent run", "Simulated demo run",
"Legacy run — execution mode was not recorded", or "Unrecognized execution mode". The Agent request actions are offered only
for `ManualAgent` and `Legacy` runs; a simulated or unrecognized run shows a fixed note instead. A manual run can stay
nonterminal after its stages or after its budgets are exhausted; budget copy never promises that a new run replaces it.

## Run header

The primary header shows:

- project name and objective;
- execution number;
- lifecycle and active stage;
- accumulated autonomous session time;
- context-sensitive `Start`, `Pause` or `Resume`, and `Stop` controls.

Autonomous session time measures time during which the run is allowed to
advance. It excludes time while the run is paused or terminal. Detailed Git
fingerprints, event sequence numbers, provider session identifiers, and attempt
metadata belong in Evidence rather than the primary header.

`Pause` prevents a new external attempt from starting. `Stop` requires
confirmation and becomes terminal only after active external state is
reconciled. Controls must never imply immediate cancellation when the provider
cannot prove it.

## Agent runtime strip

Codex and Claude each have one compact, single-line runtime control. The line
contains:

- provider identity;
- `Working`, `Waiting`, `Paused`, `Blocked`, `Unavailable`, or `Stopped`;
- selected model and effort;
- context-window percentage when available.

The active participant receives a high-contrast border, a visible working
label, and restrained motion. Status never depends on animation or color alone,
and reduced-motion preferences disable nonessential animation.

Selecting the model summary opens provider-specific settings for model, effort,
and permission mode. Options are discovered through the provider adapter and
must not be hard-coded as a permanent product catalog. A change applies at the
next safe attempt boundary and never silently changes an active invocation.

Provider permission modes are subordinate to DevalCopilot policy. Selecting a
more permissive provider mode cannot grant filesystem, process, Git, GitHub, or
publication authority that the run policy does not already allow.

Selecting the context indicator opens:

- consumed and available context when reported;
- automatic compaction policy;
- a manual `Compact` action when supported;
- freshness and an explicit `Unknown` state when unsupported.

Manual compaction occurs only between turns. It preserves the durable run
record and creates a new context-manifest revision referencing decisions and
evidence that remain authoritative. It does not rewrite run history.

## Collaboration timeline

Codex cards are aligned to the left and Claude cards to the right. Orchestrator,
tool, CI, and human events are centered. The alternating layout expresses the
exchange without pretending that both providers share one native chat.

Supported card types include:

- proposal and plan revision;
- acceptance or material challenge;
- decision and challenge resolution;
- execution report and active work;
- review finding and revision response;
- verification, Git, CI, approval, and human-intervention events.

Collapsed cards show a decision-relevant summary. Challenge, Decision,
ReviewFinding, and RevisionResponse cards additionally expose their bounded
structured fields with readable labels. A reply is described as verified only
when its parent message is both present in the currently loaded timeline and
an earlier, protocol-compatible parent for the reply's own type; a matching id
that fails either check is described only as an observed reference, never as a
verified relationship, and a parent absent from the loaded timeline is never
asserted to be outside the API's bounded window — only that it is not present
in what is currently loaded. The active card is visually tied to the
participant marked `Working`.

A `ProviderObserved` card whose message links to a real Agent attempt offers an
"Attempt evidence" drill-down exposing that attempt's own bounded historical
metadata (status, timing, checkpoints, recorded process/token-usage facts, and
an artifact-metadata inventory) — never the attempt's raw output. From that
same drill-down, the owner may additionally open a bounded, integrity-verified
text window of one of that attempt's own sealed artifacts (see "Sealed
Agent-artifact window inspection" below); every other card type retains only
the bounded legacy-details disclosure described above.

The timeline is reconstructed from durable events. It does not rely on the
continued availability of either provider's native conversation history.

## Provider sessions

A DevalCopilot run may use several Codex threads and Claude Code sessions. Each
agent attempt records its provider session identifier when one exists so the
adapter can continue an eligible session and the UI can offer provider-native
open or resume actions when supported.

Provider-native sessions are secondary views:

- Codex contains only Codex-side prompts, responses, and actions;
- Claude Code contains only Claude-side prompts, responses, and actions;
- DevalCopilot contains the complete interleaved collaboration, workflow,
  decisions, and evidence.

Provider UI discovery is a convenience, not a recovery dependency. The product
must not read or mutate undocumented provider storage to manufacture shared
history.

## Workflow rail

The Workflow rail is a projection of persisted stages, tasks, gates, attempts,
and checkpoints. It is not extracted from a Markdown checklist.

The rail shows the ordered stage map and the current state of each stage. A
stage can contain multiple immutable attempts and can be revisited by a bounded
correction loop. Re-entry adds an attempt and event; it does not duplicate the
stage or erase prior outcomes.

When collapsed, the rail retains a compact progress and attention indicator.
Selecting a stage filters collaboration and evidence while preserving visible
awareness of the whole run.

No loop is infinite. Loop count, elapsed time, token budget, provider allowance,
and policy can stop advancement and produce a precise escalation.

## Usage and budget controls

Usage controls occupy the top of the right rail, above evidence. Codex and
Claude are always displayed separately because provider accounting and reset
windows are not interchangeable.

### Run budgets

Run budgets are DevalCopilot-owned enforcement limits. The UI shows observed
input, output, and cached tokens per provider when available, plus stage and run
limits. Estimated values must be labeled as estimates.

Crossing a warning threshold raises visible attention. Exhausting a hard budget
prevents a new attempt and creates a durable escalation; it never borrows
silently from another provider or run.

### Known global claim block

Each of the cockpit's six Agent-claim controls (Codex planning, Claude
critical review, Codex challenge resolution, Claude implementation, Codex code
review, and Claude review correction) consults one small, pure, additive
derivation — `deriveGlobalAgentClaimBlock` — computed from the same run-wide
count budget ([ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md))
and invocation-time budget
([ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md))
projections the `AgentClaimBudgetBanner` and `AgentInvocationTimeBudgetBanner`
already display. When it finds a known hard stop already visible in the
loaded projection — the count budget exhausted, the invocation-time budget's
remaining time at zero or below, or that budget's own prior evidence marked
invalid — the control withholds its request button and shows a short message
naming the run-wide budget as the cause, instead of offering an action the
server is already known to reject. This is a UI-honesty check only, never a
grant of permission: a control showing no known block still submits its
request to the backend, which independently re-verifies every budget and
every role-specific eligibility rule in full before acting. The derivation is
never a full eligibility calculator — a positive remaining-time figure is
never read as proof that any one role's own configured timeout will fit
within it, and a legacy run with no time-budget policy at all
(`isLegacyUnknown`) is never treated as blocked on time grounds. A missing,
malformed, or stale projection — including one still describing a previously
selected run — is treated as unavailable and therefore blocking, never as a
permissive default, and a run switch recomputes this derivation synchronously
from the newly selected run's own `runId`, so a previous run's block state is
never shown for even one render frame. The review-correction control's own
separate human-authorization budget ([ADR-0010](../decisions/0010-add-review-correction-response-contract.md))
is a distinct mechanism scoped to that one role; an available or granted
authorization is never presented as overriding this global block, and its
authorize affordance is withheld with the same run-wide-budget message while
a global block is present. Creating a human escalation is ALSO withheld under
a global block: `CreateReviewCorrectionAttemptCommandHandler` checks the
ADR-0012/ADR-0013 global budgets before it ever reaches its own escalation
branch, so an escalation request submitted while a global block is present
cannot actually succeed — the control withholds that button too, for the
same known-certain-rejection reason as every other Agent-claim control,
rather than offering an action that would only be rejected server-side.

### One Agent claim slot remaining warning

A separate, additive, read-only warning — `deriveOneAgentClaimSlotRemainingWarning`
and its `OneAgentClaimSlotRemainingWarning` presentation — appears exactly one
claimed Agent attempt before the run-wide count budget above is fully
exhausted, so the owner has a chance to review the evidence gathered so far
before spending the last claim. `AgentClaimBudgetBanner` itself is unchanged
and continues to speak only once the budget is actually exhausted; this
warning is a distinct, earlier signal shown alongside it, never a replacement
for its copy or behavior. It reads `maximumAgentAttempts` (the actual
persisted ceiling, including any historically raised maximum — never a
hardcoded default) and `agentAttemptsUsed` directly from the cockpit
projection's own nullable fields, never through the `?? 0`/`?? false`
presentation fallbacks `RunCockpitView` applies only for
`AgentClaimBudgetBanner`'s own always-rendering props: a missing or
inconsistent value here means "unknown", never a coincidentally-matching
zero or false. It shows only when the maximum and used count are coherent,
positive/non-negative finite integers, `agentBudgetExhausted` is the literal
`false`, and `agentAttemptsUsed` equals exactly `maximumAgentAttempts - 1` —
never for an exhausted, over-budget, unknown, or malformed state, and never
for a projection still describing a previously selected run. Its own copy
states plainly that other controls may still block the next attempt even
though a budget slot remains; it never grants or vetoes any action, and the
backend remains the sole authority for every claim.

### Candidate-specific invocation-time fit

`deriveGlobalAgentClaimBlock` above deliberately never references any one
role's own configured invocation timeout, so it cannot say whether a
*positive* remaining time actually fits a *specific* claim path's own
configured duration (e.g. exactly 10 minutes remaining fits a 10-minute-
configured claim path but not a 20-minute one). A separate, additive
derivation — `deriveAgentClaimPathTimeFit` — closes this gap using a new,
backend-computed per-claim-path projection (`agentClaimPathTimeFits` on the
cockpit response) that reuses the exact `TimeSpan`/tick-level reserved-time
computation already behind the invocation-time budget, compared against each
of the six claim paths' own centrally configured timeout (`AgentClaimPath`/
`AgentClaimPathPolicy`, an Application-layer execution-configuration concern
owned by the same six command handlers that already read it — not a Domain
invariant). Each entry on the wire carries only its claim path and its fit
outcome; it carries no role/provider mapping, since each of the six cockpit
action components already knows its own claim path statically and needs
nothing further from the server to look up its own entry. Each of the six
Agent-claim controls consults its own claim path's entry and shows a message
attributing a block to "insufficient reserved invocation time for this
specific request" — wording kept strictly distinct from
`describeGlobalAgentClaimBlock`'s run-wide budget copy, so a reader never
conflates a candidate-specific refusal with a global one. A candidate timeout
exactly equal to the remaining time still fits ("fits with zero slack"); a
legacy run with no time-budget policy at all is reported as
`LegacyUnknown`, never as a fit or no-fit answer, and never treated as a
block; a run whose prior invocation-time evidence is invalid, or a
projection that is missing, duplicated (whether or not the duplicates
agree — a contradiction is never resolved by trusting one of two answers),
unknown-shaped (including an unrecognized claim-path value anywhere in the
list), or still describes a previously selected run, fails closed and
blocks. The frontend also cross-checks each claim path's fit state against
the run-wide invocation-time budget's own `isLegacyUnknown`/`evidenceInvalid`
fields (the same fields behind the global block above): both signals are
derived from the same underlying budget data, so a claim-path entry
reporting `Fits`/`DoesNotFit` while the run-wide summary reports
`isLegacyUnknown` or `evidenceInvalid` (or the reverse) is incoherent and
fails closed instead of trusting either side. The frontend never re-derives
or double-checks the fit conclusion itself with client-side millisecond or
tick arithmetic — it only ever trusts the backend's own closed fit state
once it has proven the received shape is coherent; the backend remains the
sole authority for the fit comparison itself. This signal is purely
advisory, exactly like the global block: a `Fits` result is never presented,
worded, or implied as the server having granted or pre-approved the claim,
and it is combined with — never a replacement for — the global block above;
when both apply to the same control, both messages are shown, since neither
withholding reason should hide the other. The review-correction control's
plain request and its "Create human escalation" affordance are both
withheld together on this signal too, for the same reason they are already
withheld together under a global block:
`CreateReviewCorrectionAttemptCommandHandler`'s time-budget check precedes
its escalation branch, so a request that does not fit cannot succeed either
way. Like the global block, this is UI honesty only — the backend remains
the sole authority for every claim, and the run switch guard is identical:
the derivation is recomputed synchronously from the newly selected run's own
`runId`, so a previous run's fit state is never shown for even one render
frame.

### Run-wide token-usage evidence summary

A separate, read-only summary sums provider-reported token usage across every
Agent attempt this run has dispatched — pure evidence, never a budget or
guardrail. It reports one of four states: no attempt dispatched yet; a
complete total (every dispatched attempt has concluded and reported known
usage); attempts still running with no complete picture yet, shown as a
partial count that is not yet the total (nothing has failed to report usage,
some attempts simply have not finished); or a genuine partial gap, where at
least one attempt that has already concluded reported no usable usage. Only
the complete state is ever labeled a total. A dispatched attempt that is
still running never contributes its usage to any sum or "known" count, even
if a corrupted or prematurely-populated record already shows token values for
it — only a concluded attempt's usage is ever trusted. This lets the cockpit
tell a reader "still counting" apart from "some attempts never reported
usage," which the run-total label alone cannot convey.

When a run's gap spans both still-running and concluded-without-usage
attempts at once, the cockpit names each count separately rather than folding
them into one phrase — "N attempts concluded without usable token-usage
evidence" is always distinguished from "N attempts still running," and both
are shown together only when both are genuinely present. A dispatched attempt
is never described as a "terminal usage gap" merely because it has not
concluded yet. When no attempt has reported known usage, the cockpit shows no
numeric token count at all, rather than displaying an unpopulated zero as if
the provider had actually reported it; once at least one attempt's usage is
known, its sum is shown exactly as reported, including a genuine zero. See
the "Provider token-usage contracts" section of the
[agent-collaboration-protocol](../architecture/agent-collaboration-protocol.md)
for its exact evidence-state contract.

### Provider-separated token-usage projection

A separate, read-only projection splits the same dispatched-Agent-attempt
evidence the run-wide summary above sums into one independent summary per
provider bucket — Codex, Claude Code, and Unattributed — so a reader can see
each provider's own usage without losing the existing run-wide total, which
this projection never changes or replaces. It uses only already-recorded,
already-trusted per-attempt evidence (the same recorded provider and
provider-reported usage every other cockpit token-usage read path already
trusts); it observes no new provider surface, adds no new adapter, and
enforces no threshold or guardrail — pure additional evidence, exactly like
the run-wide summary it sits beside.

The cockpit always shows exactly three buckets, even when a bucket has no
dispatched attempts at all, so a reader can always find Codex and Claude Code
in a stable position. Unattributed is the fail-closed bucket for a dispatched
Agent attempt whose recorded provider is missing or not one of the two known
providers — never silently dropped from the projection and never guessed
into the wrong provider's bucket. Each bucket follows exactly the same
completeness rules, "never trust a still-running attempt's usage" rule, and
partial-vs-total labeling as the run-wide summary above, independently and
scoped only to that bucket's own dispatched attempts.

This same "never trust a still-running or undispatched attempt's usage" rule
applies uniformly wherever a single attempt's own token usage is shown, not
only in this run-wide sum: each role's own status view and the cockpit's
latest-attempt panel show that attempt's usage as not-yet-recorded (or, when
undispatched, as no usage at all) whenever the attempt has not concluded,
even if the underlying record already carries seemingly well-formed token
values — the presentation logic checks the attempt's own running/dispatched
state before it ever consults the usage record's shape, so a malformed or
tampered record cannot be mistaken for genuine usage just because it looks
complete.

### Codex account-allowance observation (read-only)

A bounded, read-only fact in the usage rail: each reported Codex ChatGPT
account-allowance bucket has its own `primary`/`secondary` windows
(`usedPercent`, optional `windowDurationMins`, optional `resetsAt`), obtained on demand through the already-
vetted local Codex CLI launch target and the documented Codex App Server
`account/rateLimits/read` protocol. It reports the host's own retrieval time
alongside the snapshot. Unavailable data — no vetted Codex target, missing
account authentication, an unsupported protocol method, a malformed or absent
window, a timeout, or a process failure — is shown as an explicit `Unknown`,
never a zero-valued window. A valid reported percentage remains visible when
duration or reset time is unavailable; the missing field is labeled Unknown.
Buckets are displayed separately, without an invented aggregate. A genuinely
reported zero is shown once a window is known. This is a snapshot only: it carries no enforceable stop
threshold, warning configuration, or claim/dispatch policy, and it never
implies a specific invocation is currently eligible to start. It is not
scheduled or polled automatically — the rail requests one fresh snapshot on
mount and again only when the reader explicitly asks for a refresh. Claude
account usage has no equivalent observation yet and continues to show its
existing "not yet collected" placeholder. See the
["Provider account-allowance contracts"](../architecture/agent-collaboration-protocol.md#provider-account-allowance-contracts)
section of the agent-collaboration-protocol for the exact wire evidence.

### Codex model and reasoning-effort catalog observation (read-only)

A second bounded, read-only fact in the usage rail, beside the Codex
account-allowance line: a fresh, picker-visible catalog of Codex models, each
with its display name, the reasoning-effort identifiers it supports, and the
provider's own suggested default effort, obtained on demand through the same
already-vetted local Codex CLI launch target and the documented Codex App
Server `model/list` protocol. It reports the host's own retrieval time
alongside the catalog. Unavailable data — no vetted Codex target, an
unsupported protocol method, a malformed or excessive response, a timeout, or a
process failure — is shown as an explicit `Unknown`, never an empty-looking
success. Models are listed individually, without an invented aggregate; a
model missing, or reporting an unsafe (control-character or bidirectional-
formatting-character), display name falls back to showing its own id rather
than hiding the entry or rendering text that could visually misrepresent
itself. A model's supported reasoning-effort identifiers are shown only as a
whole trustworthy list: if any individual reported effort was malformed or
duplicated, the whole field is shown as an explicit Unknown rather than a
partial list with the bad entry silently dropped; a model that genuinely
reports no supported efforts shows that field as empty, not Unknown. A
model's suggested default effort is shown only when it is itself one of that
model's own known supported efforts; otherwise it is shown as Unknown rather
than an unverified claim. This is catalog evidence only: it carries no model or reasoning-effort
selection, no run intent, no attempt-assignment or invocation argument, no
account-authentication claim, and it never implies a listed model remains
available, authenticated, or eligible to invoke at dispatch time. It is not
scheduled or polled automatically — the rail requests one fresh catalog on
mount and again only when the reader explicitly asks for a refresh, exactly
like the account-allowance line beside it. See the
["Codex model and reasoning-effort catalog contract"](../architecture/agent-collaboration-protocol.md#codex-model-and-reasoning-effort-catalog-contract)
section of the agent-collaboration-protocol for the exact wire evidence.

### Explicit Codex model and reasoning-effort requests

A cockpit control, using the catalog above, lets the owner explicitly request
a Codex model and optional reasoning effort for this run's *later* Planner,
Challenge Resolver, and Code Reviewer claims. Nothing is auto-selected,
including the catalog's own suggested default: the control starts on an
explicit "No preference" choice. Saving a chosen model requires the catalog to
be currently loaded and the model to be one of its visible entries; the effort
select is populated only from that model's own supported efforts and is
disabled until a model is chosen. Saving is rejected — with a safe, generic
message, never the underlying failure detail — when the catalog is loading,
unavailable, or the chosen pair is invalid; clearing the preference back to
"No preference" is always available and needs no catalog read. The control
shows its own three states plainly: the currently *requested* preference (or
its explicit absence), a loading state while the catalog is being read, and a
save-failure message that never discards what was already saved. This is a
future-claim request only — it is never labeled as effective, observed, or an
invocation-eligibility guarantee, and changing it while an attempt is already
running never affects that attempt. See the
["Explicit Codex model and reasoning-effort requests"](../architecture/agent-collaboration-protocol.md#explicit-codex-model-and-reasoning-effort-requests)
section of the agent-collaboration-protocol for the durable request/claim/
invocation-argument semantics.

### Explicit Claude model-alias requests

A second cockpit control lets the owner request one Claude model alias — `sonnet`,
`opus`, or `haiku` — for this run's *later* Critical reviewer, Implementer, and
review-correction attempts. The select starts on an explicit "No preference"
choice and never auto-selects an alias; Save and Clear are always available (Clear
only when a request exists). The control states plainly that the value is a
**request only** and that the model actually used is not observed: an alias is
request syntax, not proof that it is enabled for the signed-in account, so the
provider may reject it. A rejection is recorded as an ordinary failed invocation;
no other model is substituted. A change never affects an attempt that has already
been claimed and takes effect only at a later claim. A terminal run or a save
failure shows a safe, generic message and keeps the previously displayed request.

The latest Claude agent attempt also shows its own claim-time request separately
from the run's current one: "Model requested at claim: <alias>", or "No model
request recorded for this attempt" when its snapshot is null. The null case covers
both an attempt claimed with no request and one that predates this feature; the
cockpit does not distinguish them and never infers a default or effective model
from a null. No observed-model value is shown. See the
["Explicit Claude model-alias requests"](../architecture/agent-collaboration-protocol.md#explicit-claude-model-alias-requests)
section of the agent-collaboration-protocol for the durable request, claim-snapshot,
and invocation-argument semantics.

### Explicit Claude effort requests

The Claude request control also carries an optional **effort** request (`low`,
`medium`, or `high`) that is offered only together with an explicitly chosen
`sonnet` or `opus` alias. The effort select starts on "No effort request", is
disabled for "No preference" and `haiku`, and is cleared automatically when the
model changes to one that cannot carry it, so the UI never submits an invalid pair
(the server validates again). Save and Clear submit the model and effort as one
pair; Clear removes both. The control states that the value is a **request only**:
the effort actually applied is not observed, and the provider may reject or adjust
it (organization or model limits can change the applied level, including silently),
so this slice derives no observed or effective effort from the request, and its new fields
and lines display requests only. (Provider-reported observed facts already shown elsewhere,
such as the implementation status view's "Effort observed", are separate and unchanged.)

After a successful Save or Clear the cockpit explicitly re-queries the
authoritative projection, because this operation emits no run notification. That
refresh is generation-safe: a response for a run that is no longer selected is
discarded, and a failed or stale refresh shows the fixed message "Saved, but the
cockpit could not be refreshed; the displayed request may be out of date." rather
than a raw error. A failed save shows its fixed safe message and triggers no
refresh.

The run's current request and the latest Claude attempt's own claim-time request
are separate lines: "Requested Claude effort for future attempts: <level>" (or
"No Claude effort requested"), and on the latest attempt "Effort requested at
claim: <level>" or "No effort request recorded for this attempt". A null covers
both an attempt claimed without an effort and one that predates this feature; the
cockpit does not distinguish them or infer a default. A non-Claude attempt shows no
effort line even though its stored Codex effort may be present. See
["Explicit Claude effort requests"](../architecture/agent-collaboration-protocol.md#explicit-claude-effort-requests)
for the durable request, claim-snapshot, and argument semantics.

### Optional Claude turn limit for implementation and correction

A "Claude turn limit" control lets the owner request one whole-number agentic-turn guardrail (1 through 100) for the
run's **future** Claude implementation and review-correction attempts. It is labelled as a **request**: not an account,
token, or cost limit, not enforced by the host, and never a measured turn count ("the turns actually used are not
measured"). It is shown as "Current run request: Not requested | N turns | Unknown", offers a numeric field with Save and
Clear, and is editable only while the run is Created or Running; otherwise it states that the request can no longer be
changed, and the backend's HTTP 422 remains the real guard. The draft is validated locally and never clamped: an empty
field clears only through Clear (Save on an empty field asks for a number), and only canonical ASCII whole numbers 1
through 100 are sent (no sign, spaces, fraction, exponent, or leading zero). Clear sends an explicit `null`. The control is
disabled and a second submit is ignored while a save is pending, and a failed save shows a fixed message chosen by the
HTTP status only (never server text); a conflict tells the owner to reload the run and retry. After a successful Save
or Clear the cockpit explicitly re-queries the authoritative projection, with the same generation-safe refresh and fixed
"Saved, but the cockpit could not be refreshed" message as the model request, and a run switch discards the draft, pending
state, and error.

The latest Agent attempt, the implementation and review-correction status blocks, and the historical attempt evidence
show that attempt's own immutable fact as "Claude turn limit for this attempt: <text>" and nothing for an attempt that is
not a Claude implementation or correction attempt. The four texts are exact and distinct: **Requested: N turns** (the
saved or snapshotted request, shown as a request only and not as proof that a provider received or honored it), **Not requested** (a coherent version 2 attempt
that recorded no request), **Not recorded** (a legacy version 1 attempt, which is never read as an observed unlimited
capacity), and **Unknown** (recorded facts disagree, the stored value is invalid, or the state is unrecognized; never a
number). The two role blocks also show "Current run request (applies to future attempts): <text>" so the owner can compare
the run's saved request with the attempt's immutable fact. Nothing here implies eligibility, a configured capability, or
that a provider honored the request; a turn-limit error from the provider is an ordinary failed invocation. The
implementation action recognizes exactly the adapter contract versions `claude-implementation-v1` and
`claude-implementation-v2`, and shows any other value as unknown. See
["Optional Claude agentic-turn limit for mutation attempts"](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts)
for the durable request, claim-snapshot, versioning, and argument semantics.

### Codex account-usage warning (advisory, explicit check)

Two separate groups in the usage rail, shown after the allowance and model-catalog lines and before the Claude placeholder,
carry the run's Codex account-usage controls: the **Codex account-usage stop** group
([ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)) and, directly after it,
the **Codex account-usage warning** group
([ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)). They are never merged, have no
required ordering, and neither reads the other. Both appear only when the cockpit projection carries the run's setting.

The warning group shows "Current run setting: Not configured | N% used | Unknown" (Unknown for an invalid stored value or an
unrecognized projection, never a number), a fixed advisory note, and, while the run is Created or Running, a numeric field
with Save and Clear (otherwise it states the warning can no longer be changed; the backend's HTTP 422 remains the real guard).
The draft is validated locally and never clamped: an empty Save asks for a number (only Clear clears), and only canonical ASCII
whole numbers 1 through 100 are sent; Clear sends an explicit `null`. A save is disabled while pending, a failure shows a fixed
message chosen by HTTP status only (never server text), and an accepted save or clear re-queries the authoritative cockpit
with the same fixed "Saved, but the cockpit could not be refreshed" message. Saving or clearing contacts no provider and never
starts a check; the cockpit read performs no provider work either.

A **"Check Codex account warning"** action exists only while a valid warning is saved. It is the only thing that reads the
account: not mounting, saving, clearing, selecting a run, cockpit catch-up, polling, collapsing and expanding the rail, or the
allowance Refresh. One activation makes one request (`GET` of the same route, no caller-supplied threshold, executable or
prior observation); a second activation while pending is ignored. The result area is a status region with exactly one state:
**Not checked in this view**; **Checking the Codex account…** (pending); **The Codex account could not be checked. Try again.**
(failure); an **unavailable** answer with fixed copy (the evidence could not be read, was no longer current, or the saved
warning or launch target changed during the check, or the saved warning changed); or a dated **Below** ("No reported usage
window had reached the N% warning when the Codex account was observed.") or **Reached** ("A reported usage window had reached
the N% warning …", or "The provider reported that a usage limit had been reached …") result with the host retrieval time and
each reported window ("<bucket> primary|secondary window: P% used", marking "(warning reached)"). A result is classified
only when it is dated, internally consistent and for exactly the saved percentage shown; anything else is "could not be
verified", never Below. While a check is pending, and after a failed one, no earlier classification is shown. Windows are
never summed, and a result never says an account is eligible, ready, has remaining capacity or live usage; it describes the
host's Codex account, not usage attributable to this run.

Ownership: the draft, pending state, errors, duplicate protection and last observation belong to the committed run plus the
authoritative saved warning (state and percent), not to a parent `key`. A different run or a different saved warning ends the
owner and starts a new one with no observation; returning to an earlier run and warning (A to B to A) is a new owner too; a
retained handler of an ended owner is inert; a late or overlapping completion of an ended owner is ignored (the accepted
server operation still stands and the next authoritative read shows it); an unmount drops a pending check without a state
update. A reload keeps the saved setting and the stop but not the observation, and reads nothing by itself. The warning is
advisory: it changes no claim, dispatch, budget, grant, permission, stop or invocation behavior, and no event, attempt or
observation history is recorded for a check. Claude account usage keeps its "not yet collected" placeholder. See
["Run-scoped advisory Codex account-usage warning"](../architecture/agent-collaboration-protocol.md#run-scoped-advisory-codex-account-usage-warning).

### Per-provider token-activity warnings

A "Token-activity warnings (advisory)" panel gives Codex and Claude Code each their own optional threshold
control (a positive whole number of tokens, Save and Clear; Clear only when one is set) and their own status.
It says plainly that the warning concerns locally recorded, provider-reported token activity only: it does not
limit or block any attempt and is not an account allowance or a cost, and the two providers' counts (whose exact
formulas are printed beside each control) are never combined. Non-numeric, zero, negative, fractional, or
oversized input is rejected locally with a generic message; a save failure shows a safe generic message and
keeps the prior state.

Each provider is in exactly one visibly distinct state. **Neutral** when no threshold is set (no warning and no
all-clear). A **prominent alert** (`role="alert"`) when the known count is at or above the threshold, stating the
count is a lower bound when other evidence is missing. **Below threshold, complete evidence**, or a **known zero**
(stated as such, with the number of concluded attempts) when every dispatched attempt contributed usable
evidence. **"Not an all-clear"** when the count is below the threshold but attempts are still running, lack
usable usage evidence (for Claude Code, all four counts are required), or cannot be attributed to a provider —
naming each gap and that the real count may be higher. And **no evidence** when a threshold is set but no attempt
has been dispatched, which is never shown as zero. Changing a threshold re-evaluates already-recorded evidence
immediately: after a successful save or clear the panel re-queries the authoritative cockpit (the change emits no
run event), and if that refresh fails it says so plainly ("Saved, but the cockpit could not be refreshed; the
displayed warning may be out of date.") instead of leaving a stale state unexplained. The local range check is
the backend's inclusive 1 to 1,000,000,000,000. See the
["Per-provider run token-activity warnings"](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-warnings)
section of the agent-collaboration-protocol for the exact counting and concurrency rules.

### Per-provider token-activity stops

A separate "Token-activity stops (enforced at claim)" panel, shown directly after the advisory warnings and
never merged with them, gives Codex and Claude Code each their own optional stop threshold control (a positive
whole number of tokens, Save and Clear; Clear only when one is set) and their own status. Unlike a warning, a
configured stop refuses the next Agent attempt for that provider. The panel says plainly that it is checked only
when an attempt is claimed, that it is not an account allowance, that it does not cap or cancel an attempt
already claimed, and that it does not show that a provider is available; each provider's exact count formula is
printed beside its control and the two counts are never combined. Non-numeric, zero, negative, fractional, or
oversized input (including 10^12 + 1) is rejected locally with a generic message and no request; a save failure
shows a safe generic message, keeps the prior state, and does not refresh.

Each provider is in exactly one visibly distinct state, taken from the server's `tokenStops` projection and
never inferred locally. **Neutral** when no stop is set ("a new attempt is not limited by a token stop"). A
**blocking alert** (`role="alert"`) when the known count is at or above the stop, naming the count, the
concluded attempts, the threshold, that new attempts for that provider are refused until the threshold is raised
or cleared, that an already claimed attempt is unaffected, and, when other evidence is missing, that the count is
a lower bound with each gap named. A second **blocking alert** when staying below the stop cannot be proved,
naming every cause (attempts still running, attempts without usable usage evidence, for Claude Code all four
counts being required, attempts not attributable to a provider, or a total that is not representable) and that
new attempts are refused. A **permitting** state when the stop does not refuse a claim: either the run has no
dispatched attempt yet (stated as not a measured zero) or the count is below the stop with complete evidence (a
known zero is stated as such); both say the stop does not show that the provider is available, and the panel
never says a provider is eligible or safe to call. Changing a stop re-evaluates already-recorded evidence
immediately: after a successful save or clear the panel re-queries the authoritative cockpit (the change emits no
run event) through the same generation-guarded refresh as the warnings, discards a response for a run that is no
longer current, and if that refresh fails says so plainly ("Saved, but the cockpit could not be refreshed; the
displayed stop state may be out of date."). A claim action that the server refuses because of a stop shows the
server's fixed safe message, which carries no count or evidence. The local range check is the backend's
inclusive 1 to 1,000,000,000,000. See the
["Per-provider run token-activity stop at Agent claim"](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim)
section of the agent-collaboration-protocol for the exact counting, enforcement, and concurrency rules.

### Optional second challenge round

The cockpit follows one proposal lineage — the run's newest provider-observed Planner Proposal and its Resolver
revisions — and offers the optional second review and resolution of the
[second challenge round](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation).
It is derived entirely in the browser from data already loaded (the collaboration timeline cards' type, actor
role, provenance, sequence, and reply link, and the latest review and resolution attempt statuses); no new
endpoint, projection, or generated-client change exists. Everything here is a display hint: the backend
re-decides every review, resolution, and implementation request from durable identity, so an unverifiable or
ambiguous chain only ever **withholds** an action and never grants one.

- **Selection.** The chain is read by reply links: a Resolver-authored, provider-observed Proposal replying to the
  root (recorded after it) is the first revision, and one replying to the first revision is the second revision;
  a host-constructed, Orchestrator-authored Escalation replying to the second revision is the escalation. A
  self-reply, a foreign parent, a Simulated card, a non-Resolver author, a revision that precedes its parent, or
  more than one candidate for the same parent is not followed (two candidates are an *ambiguous* chain, which
  withholds review and implementation). A newer independent Planner root replaces the whole lineage.
- **Review action.** It targets the root before any revision, then the first revision — labelled "Request
  Claude review of the revised proposal" — and nothing once a second revision exists. It is withheld while a
  review of that exact Proposal is running or already Accepted/Challenged, or under a known global or path time
  block, exactly like the original-proposal review.
- **Resolution action.** Unchanged: it is offered when the latest review is Challenged and that exact review has
  no resolution yet, so it serves the second review's challenges too, and it disappears once they are Resolved.
- **Implementation action.** The original root is offered only after an Accepted review of exactly that root. The
  first revision is offered when its own review did not Challenge and is not running — with no second review it
  is the direct path; with an Accepted one it is the acceptance-bound path — and is withheld while the review
  status is loading or failed, so a hidden Challenged review is never overlooked. This automatic selection never
  offers a second revision or an ambiguous chain; the second revision appears only through the explicit human
  authorization described in
  [Human decision on the final plan](#human-decision-on-the-final-plan).
- **Lineage summary.** A short "Proposal lineage" region appears once a revision exists and states the stage in
  fixed text: an optional second review is available (and the revision may be implemented directly); the second
  review is running, accepted, or challenged (implementation blocked, one last resolution available), did not
  conclude, or its status is unavailable; and, at the end of the lineage, that the second challenge round is
  resolved, there is no further review or automatic resolution, the final revised proposal cannot be
  implemented through the lineage without an explicit human authorization (which permits at most one implementation
  claim of it), and a human decision is required. The escalation's own summary is shown as
  "Human decision required: …" with the statement that the record is not an approval (the record's own fixed text names
  the options, and recognizes both the current and the original canonical serialization of an escalation, per
  [ADR-0020](../decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md), so the region and
  the decision below behave identically for an escalation recorded earlier); if the escalation is not
  in the loaded timeline the region says so instead of implying it exists. It never shows raw artifact text,
  and it never states that any plan was approved or implemented.
- **Loading, errors, run switches, and late responses.** The review status's own loading and error states are
  shown by the review control and withhold the first-revision implementation; the status hooks already mask a
  previous run's status, error, and loading state synchronously and drop a late response for a run that is no
  longer selected, and the lineage is recomputed during render from the currently selected run's own timeline,
  so a previous run's lineage, escalation, or blocked state is never shown for the new run, even for one render
  frame.

### Human decision on the final plan

When the loaded lineage is one unambiguous chain ending in its second revision and that revision's host escalation, the
cockpit adds a "Human decision on the final plan" region after the lineage summary, for a run whose execution mode admits
Agent work ([ADR-0016](../decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md)). It reads
the server's own facts for that escalation (`GET …/planning-escalations/{escalationMessageId}/implementation-authorization`,
re-read whenever the run advances) and shows only what they say.

- **Identity and consequence.** It names the final revised plan and the escalation (short identifiers) and the number and
  identifiers of the second-round decisions the server returns. For an absent authorization it states the consequence: authorizing
  records the human's decision and permits exactly one implementation claim of this plan; it does not start an implementation,
  reserve any budget, or contact a provider; the implementation is requested separately and provider availability and budgets are
  checked then; the claim spends the authorization even if the implementation later fails; it cannot be renewed or revoked.
- **Decision form.** A required reason (at most 600 characters, no control characters except line breaks, advisory, recorded in the
  timeline and sent to the Implementer as context, not screened for secrets) and one button, "Authorize one implementation claim".
  A blank or visibly invalid reason is never sent; the server stays authoritative and its refusals map to fixed local copy by
  problem code (never the submitted text or the server's own wording).
- **States.** `Absent` shows the form; `Available` states that a human authorized exactly one claim, that no implementation has been
  claimed with it, and the exact recorded reason; `Consumed` states that the authorization was used by an implementation claim and
  cannot be reused whatever that attempt's outcome; `Stale` states that it no longer matches the run's current plan, checkpoint, or
  fingerprint and is unusable; `Invalid` states that the recorded evidence could not be validated; an unrecognized state offers
  nothing. While the facts are unread, or after a failed read, only a loading or "could not be read, so no decision is shown" line is
  shown and no decision or implementation is offered: nothing is inferred from an earlier click or an earlier read. Facts belong to the
  committed run-and-escalation lifetime and the exact read that fetched them: after switching to another run or escalation and back,
  the earlier `Available`, `Consumed`, or unavailable record is not shown again, not even for one frame, and no implementation is
  offered until a fresh read for the returned-to interaction supplies usable facts (a failed fresh read shows only the unavailable line).
  While a newer read of the same interaction is pending, its last facts stay shown so an open form keeps its state. If the server
  names a different final plan than the timeline shows, nothing is offered.
- **Implementation, separately.** Only for an `Available` authorization naming exactly the timeline's final revision, the existing
  implementation action is offered for that plan with the label "Implement the human-authorized final plan with Claude", keeping its
  guidance editor, budget and time-fit blocks, status, and all other behavior. After `Consumed` the same action shows the plan's last
  attempt and a fixed note that the authorization was used, and offers no request. A stale, invalid, absent, unread, or mismatched
  authorization offers none. Provider review and resolution of the final revision remain unavailable in every state.
- **Ownership.** The draft, its edit version, handlers, pending and error state, and the request and refresh continuations belong to
  (run, escalation, final plan) and the mounted lifetime: switching the run, escalation, or final plan resets the draft during render
  (A to B to A is a new interaction), a late accepted request stays a server operation but cannot update another interaction, mark it
  busy, refresh it, or clear a newer draft, an unmount drops every continuation, a synchronous second submit while one is in flight is
  ignored, and an accepted submission clears the draft only if it is still the same interaction, no newer submission began, and it was
  not edited since (an edit yielding identical text counts). The draft is never written to browser storage or the URL.

### One manual Codex plan format repair

Beside the Codex planning action, a separate "Codex plan repair" panel appears only when the latest Planner
attempt's recorded outcome is `Codex returned an invalid structured response` (`InvalidStructuredOutput`), that
attempt is not itself a repair, and no known global budget or time-fit block applies. It offers one button,
"Request one format-repair plan", and says plainly that the last response failed structural validation, that the
repair is a fresh plan attempt with a format reminder that uses one Agent attempt and reserved time, and that it
does not correct or reuse the earlier response. The button is only a suggestion: the server decides eligibility
and its fixed, safe refusal (for example, "A repair was already requested for this attempt.") is shown in the
panel; a failure that is not a backend message shows a generic one. The button is disabled while the repair or an
ordinary plan request is in flight or the status is loading, and the ordinary "Request Codex plan" button stays
available and is disabled while a repair request is in flight. After a repair is claimed the Planner status
refreshes; the panel then shows lineage ("Attempt #N is the one repair request for attempt #M.", or "…a repair
request for an earlier attempt." if the source number is unknown), that a repair is not repaired again, and that
an ordinary plan can still be requested — never that the repair fixed or preserved anything. A repair's request
state and error belong to the run that requested it and never appear on another run after a switch; the status
that offers the button is masked during a run switch by the existing status hook. See
["One manual Codex Planner format repair"](../architecture/agent-collaboration-protocol.md#one-manual-codex-planner-format-repair)
for the eligibility, claim, manifest, and persistence contract.

### One manual format repair of a critical review, challenge resolution, or code review

Beside each of the three remaining read-only stage actions, a separate repair panel appears with the same rules as
the Codex plan repair above: "Claude critical review repair", "Challenge resolution repair", and "Code review
repair". Each shows only when that stage's latest attempt has the recorded outcome `InvalidStructuredOutput`, is not
itself a repair, is not running, and no known global budget or time-fit block for that stage's own claim path
applies. Its one button ("Request one format-repair critical review", "…challenge resolution", "…code review") comes
with plain wording: the last response failed structural validation; a repair is a **fresh** attempt with a format
reminder that uses one normal Agent attempt and reserved time; and it does not correct, reuse, or fix the earlier
response. The button is only a suggestion — the server alone decides eligibility (the source's exact inputs, and for
the code review the enabled and latest passed verification selection, must still be current), and its fixed, safe
refusal is shown in the panel while any other failure shows a generic message. The repair button is disabled while
that repair or the stage's ordinary request is in flight or the status is loading, and the ordinary button is
disabled while the repair is in flight, so the two never overlap; the other stages are unaffected. The pending state and
error belong to the run that requested them and never appear on another run after a switch, including a later return
to the original run (A to B to A never resurrects A's earlier error or pending state). Each request has a generation, and a
completion that is no longer the latest request of the current run is ignored: it neither overwrites the current pending or
error state nor triggers a refresh. Ignoring a stale completion does not cancel a request the server already accepted; the
next authoritative status read shows its real outcome. On success the stage's
status refreshes, and the panel then shows lineage ("Attempt #N is the one repair request for attempt #M.", or the
generic form if the source number is unknown), that a repair is not repaired again, and that an ordinary request
remains possible — whether the repair later succeeds or fails, and never that it fixed or preserved anything. The
attempt history and its evidence detail show a provenance line ("Repair request for attempt #M") only when the source
is proved; otherwise they show nothing. See
["One manual format repair of the remaining read-only stages"](../architecture/agent-collaboration-protocol.md#one-manual-format-repair-of-the-remaining-read-only-stages)
for the contract.

### Diagnosis of failed local verification

Beneath the review-correction controls, a section "Diagnosis of failed local verification (Codex, read-only)" states
that it is **not a code review** and never approves anything. It is driven by its own status read
(`GET /api/runs/{runId}/agent-attempts/verification-diagnosis`) and is offered only when the host reports a
diagnosable current implementation report: every enabled verification command has a coherent latest execution on the
current checkpoint and at least one failed. Otherwise the section names, in fixed copy, why a diagnosis cannot be
requested (missing, still running, timed out or cancelled, source drifted, or nothing failed). "Diagnose failed
verification" sends the report identity the host named; it is explicit, subject to the existing run-wide Agent
budgets, active-attempt and global-block rules, and is refused when the identical report and verification were already
diagnosed.

A completed diagnosis shows its outcome, the pinned verification list in order (command name, execution number,
status, exit code), and either the recorded finding count (the findings themselves appear in the collaboration
timeline) or the bounded escalation. When the findings still apply to the current source, "Request correction from
findings" sends the diagnosis attempt identity and is bounded by the one shared review-correction allowance. Where that
allowance is exhausted, "Record human escalation" records one durable Orchestrator escalation and states that it
grants no authority; there is no authorize control for this source. Later verification, a changed checkpoint, or a
successful correction removes the correction control. Beside the unchanged "Request correction from findings" button the cockpit
offers the optional guided form described under ["Optional direct guidance with a diagnosis correction"](#optional-direct-guidance-with-a-diagnosis-correction).
After a correction the ordinary code review still requires a new
passing verification of every enabled command. No copy claims approval, and no stage runs automatically. Requests
use the generated clients and the run-scoped owned lifetimes, so a response for one run selection never updates
another.

### Optional guidance with a review-correction authorization

Where a review correction's budget is exhausted, its escalation exists, no authorization is available yet, and no
global budget or time-fit block applies — exactly where "Authorize one additional correction" is offered — a form
"Authorize with guidance" appears beneath that unchanged button. It has one multi-line field (with a counter of the
normalized length out of 600) and states that the text is sent to the Implementer as advisory context, is recorded
in the collaboration timeline, cannot change the objective, findings, permissions, tools, or budgets, and is **not
screened for secrets**, so credentials or sensitive data must not be entered. Blank or over-long drafts get a local
message and no request; the server remains authoritative for length, content, and eligibility. While the request is
in flight the field and both authorize buttons are disabled ("Authorizing…"). A server refusal — including the fixed
conflict "An authorization with different guidance already exists for this escalation." — is shown in the existing
correction status line without echoing the draft, and the draft is kept for editing. Success refreshes the
correction status, after which an authorization is available and the form disappears. The draft lives only in the
form's state: it is never written to browser storage, cookies, or the URL, is discarded when the escalation or run
changes or the form unmounts, and is cleared after an accepted submit. See
["Bounded human guidance for one authorized review correction"](../architecture/agent-collaboration-protocol.md#bounded-human-guidance-for-one-authorized-review-correction)
for the normalization, conflict, linkage, and manifest rules.

### Optional direct guidance with an implementation or correction request

Beside the unchanged "Implement the resolved plan with Claude" and "Request review correction" buttons, a form appears
exactly where the plain button is offered (no active attempt, no global budget or time-fit block): "Implement with guidance"
and "Request correction with guidance". Each has one multi-line field with a counter of the normalized length out of 600 and
states that the text is sent with the request as advisory context and recorded with the attempt, cannot change the objective,
permissions, tools, or budgets, is not a statement that the provider will follow it, and is **not screened for secrets**. The
plain button never sends guidance and ignores the draft. The guided submit is enabled only for a valid, non-blank draft (not
blank after trimming, at most 600 UTF-16 code units after Unicode form C and line-feed normalization, no control character
except a line break), so blank or whitespace-only guidance is never sent; over-long and control-character drafts get a local
message. The server stays authoritative for content and eligibility, and a refusal never echoes the draft: the two guidance
refusals map to fixed text selected only by the problem code (`agent_attempts.direct_guidance_invalid`,
`agent_attempts.direct_guidance_unavailable`).

At correction-budget exhaustion, and while an unconsumed extra authorization exists, the correction form is not offered and the
action says "Direct guidance is available only within the ordinary correction budget." The separate "Authorize one additional
correction" and "Authorize with guidance" controls are unchanged. Availability in the UI never overrides the server gate: a
guided request at exhaustion is still refused with a 409 and creates nothing.

Drafts, busy and error state, handlers, and the request and refresh continuations are owned by the committed interaction
lifetime of **run and source** (the plan proposal message for an implementation, the reviewed attempt for a correction).
Replacing the source in the same run, switching runs, returning to an earlier run or source (a new identity), and unmounting
all end the old lifetime, reset the draft during render without relying on a parent `key`, and reject a retained handler or a
second synchronous submission of the same control. Every edit, including one that yields identical text, increments a draft
version; after an accepted request that is still current, the draft is cleared only if it was not edited since it was sent, so
a draft typed while the request or the status refresh is pending is never wiped. A request accepted for a replaced source or
run remains a real server operation: its completion is ignored for UI state (never cancelled, retried, or read as a refusal) and
becomes visible on that run's next status read. The draft lives only in component state and is never written to browser
storage, cookies, or the URL.

The latest Agent attempt, the implementation and review-correction status blocks, and the historical attempt evidence show that
attempt's own immutable fact, labelled separately from any extra-correction authorization: **Provided** shows the accepted text
as plain text (never markup) with line breaks preserved and the note that whether the provider followed it is not observed;
**Not recorded** (no direct guidance was recorded; the neutral state of historical and new unguided attempts alike, shown as "none
recorded" and never as "none was submitted"); and **Unknown** (recorded facts disagree or the stored value
is invalid; never any text). Nothing is shown for an attempt outside the two Claude mutation paths or when there is no attempt.
See ["Optional direct human guidance for mutation requests"](../architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests)
for the request, normalization, budget, snapshot, manifest, and dispatch rules.

### Optional direct guidance with a diagnosis correction

Beside the unchanged "Correct the diagnosed findings with Claude" button, the diagnosis section offers one form,
"Correct the diagnosed findings with guidance" (the same `DirectGuidanceEditor` as the two forms above), with the field
"Direct guidance for this diagnosis correction", a counter of the normalized length out of 600, the same advisory,
not-a-statement-that-the-provider-will-follow-it and **not screened for secrets** note, and the submit button
"Correct the diagnosed findings with guidance" ("Requesting with guidance…" while it is in flight). The plain button never
sends guidance and ignores the draft. The form is offered exactly where the plain correction button is actionable: the
diagnosis recorded findings, the host reports them as still applying exactly to the current source, no diagnosis or
correction is running, no global Agent-claim block or correction time-fit block applies, and the shared correction
allowance is not exhausted. The submit is enabled only for a valid, non-blank draft under the rules of the section above
and sends the diagnosis attempt identity the status names together with the raw draft; the server normalizes accepted text. The two guidance refusals
map to the same fixed text selected only by the problem code, and the draft is kept and never echoed.

The draft, busy and error state, handlers, and the request and refresh continuations are owned by the committed interaction
lifetime of **run and diagnosis source** (the run plus the diagnosis attempt named by the status), with every ownership
behavior of the section above: replacing the diagnosis in the same run, switching runs, returning to an earlier run or
diagnosis (a new identity), and unmounting end the old lifetime and start an empty draft, retained handlers and a second
synchronous submission are rejected, an accepted request for a replaced source or run is ignored for UI state but remains
a real server operation, and a draft typed while the request or the status refresh is pending is kept. A request is
always bound to an explicit run, so a response for one run never updates another.

While a diagnosis status read is pending, including the one the project's explicit "Refresh evidence" action starts, the
plain correction button, the escalation button and the guided submit are all disabled, the form and its draft stay in
place, and the refresh itself issues no request that changes Agent work. If that read **fails**, the cockpit shows
"Verification diagnosis status is unavailable." and withholds every correction and escalation control, including the guided
form; the unsent draft is discarded with the form, because the host can no longer vouch that the diagnosis it was written
for still applies. A failed read never revives an earlier status. After a later successful read that again reports an
applicable correction, the form is offered again with an empty draft; nothing is restored from the failed lifetime.

The diagnosis status carries `correctionDirectGuidance { state, text }`, with the existing fact semantics: absent (null)
when there is no correction attempt, otherwise that correction attempt's own immutable fact, labelled separately from the
diagnosis, any escalation and any authorization. **Provided** shows the accepted text as plain text with line breaks
preserved and the note that whether the provider followed it is not observed; **Not recorded** is the neutral state shown as
"none recorded", never as "none was submitted"; **Unknown** never shows text. It agrees with the same attempt's fact in the
attempt evidence and the cockpit's latest-attempt block.

Guidance is available only within the shared allowance and grants no additional authorization. At exhaustion the form is not
offered, the note "Direct guidance is available only within the shared correction allowance." is shown, and the only
action is the unchanged, unguided "Record human escalation", which records one durable Orchestrator escalation that grants no
authority; there is no authorize control for this source. Availability in the UI never overrides the server: a guided
request at exhaustion is still refused with `409 agent_attempts.direct_guidance_unavailable` and creates nothing. See
["Correction guidance"](../architecture/agent-collaboration-protocol.md#explicit-verification-failure-diagnosis) and
[ADR-0019](../decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md) for the request, snapshot,
manifest, and dispatch rules.

### Explicit local verification and the evidence refresh

The verification panel's **Run** sends the one explicit claim request (HTTP 202, see the
[workflow model](../architecture/workflow-model.md#current-verification-configuration-and-execution-boundary)). The project's
"Refresh evidence" generation is passed to the panel's execution read as well as to its source evidence read, and Run needs **settled,
successful** source evidence and execution evidence of the displayed generation (no read in flight, so each poll of a running
verification also withholds it until it settles) and **no running verification in the current workspace**, whichever recipe it belongs
to; the host's execution list and the evidence read now carry the workspace identity (`gitWorkspaceId`), so a running execution of
another, historical workspace never blocks the current one and no ownership is inferred from a recipe or a checkpoint. A recipe's own
control follows the same rule: only an execution of the current workspace labels it Pending or Running or disables it, while an execution
of a confirmed different workspace stays visible as history ("Last run: Running (earlier workspace)") without controlling Run; a missing
workspace identity (on the execution or on the evidence) is treated conservatively as the current workspace's. The same conditions are
enforced in the click handler, which decides from the newest authority even when it was retained from an earlier render. Duplicate
protection belongs to the committed lifetime of the project and to the exact request: a second synchronous activation in that lifetime
sends nothing, while another lifetime's request (a replaced project, or an earlier visit to the same one) neither blocks it nor releases
its protection, whether or not a parent remounts the panel. While the status read is pending the panel says so and keeps the cached history
visible as non-authoritative; after a failed read it keeps the history, the existing fixed failure copy and Run withheld until a later
successful refresh. The refresh itself issues reads only.

A start request that ended without a definite answer (no response, a server fault or an unreadable answer) may or may not have been
recorded: the panel shows fixed copy ("The verification request may or may not have been recorded. Use Refresh evidence to check the
verification status before running again."), never a server detail, never retries and never invents an outcome, and withholds Run until a
later explicit refresh read the status successfully. The boundary begins when the unknown outcome is known, not when the request was
sent: a refresh that settled, or is still in flight, when the failure arrives cannot discharge it, polling cannot, and only a refresh begun
afterwards whose reads succeeded does, after which it is cleared for good and no later pending or failed read revives it. It belongs to
the project lifetime that sent the request. A definite refusal (a 4xx answer) keeps the existing "This verification could not
be started." copy and Run stays available. An accepted claim stays real: it is reported as requested even when the follow-up status read
fails, and after the project is replaced its continuation neither reports on nor blocks the replacement.

### Manual checkpoint review and the explicit evidence refresh

The project's candidate-workspace "Checkpoint review" panel records one explicit manual `CheckpointReview` fact (a reviewer
choice of Human or Future agent; Pending, Changes requested, Escalate or Approve) for the exact current source checkpoint. It is
independent of every Agent stage: it creates no attempt, message, claim, authorization, budget use, publication or run
completion, and it grants no Agent or provider authority. The ordinary Codex approval records its own `FutureAgent` review fact
for the same checkpoint; a manual fact is a second, distinct, immutable record and never replaces or rewrites it.

The host answers the protected `POST /api/projects/{projectId}/reviews` with **201 Created**, a `Location` for the review list and
the review identity and decision. The generated client treats exactly that status as success, so an accepted decision is never
reported as a failed submission and is never sent again. A known refusal keeps its Problem Details (`reviews.not_found`,
`reviews.workspace_not_ready`, `reviews.checkpoint_not_current`, `reviews.evidence_not_found`, `reviews.evidence_not_terminal`,
`reviews.approval_requires_passed_verification`, `reviews.pending_cannot_include_evidence`).

The project's explicit "Refresh evidence" action (one generation per click, owned by the project's lifetime) reads the current
checkpoint, the verification executions and the review list again; the refresh itself issues no review, verification, capture or
Agent request and does not remount the panel. A decided action needs **settled, successful** source evidence **and** execution
evidence of the displayed generation: in the first frame, while either read is pending and after either read failed, the decided
actions are withheld (the panel says that verification evidence is being read, or could not be refreshed and that Refresh evidence
retries) and a cached Passed execution is never offered. **Pending** names no execution, so it needs only current source
evidence. A later successful explicit refresh restores the actions. "Settled" also means no read is in flight: while any verification-execution or review read is pending (a refresh, a poll of a running verification, or the read that follows an accepted decision) the previously read lists stay visible only as non-authoritative history, so the decided actions are withheld during each pending poll and offered again when it settles. The review list shows each historical fact, but its
"Current checkpoint" or "Historical review" applicability, and the warning about a previous approval, are shown only after the
review read of the displayed generation succeeded; while it is pending the panel says the applicability is being confirmed, and
after a failure that it could not be confirmed. A decision the host accepted and a review read that then fails are distinct: the
panel reports that the review evidence could not be loaded, never that the decision could not be recorded. Per-project lifetimes
(including A to B to A), unmount, newest-read ordering, retained callbacks, the reviewer choice, the selection bound to its
checkpoint and the verification list's single one-second polling chain behave as for the other project panels; a request the host
accepted stays real when the selection changes.

At the commit the host reads the project, the latest workspace, its readiness and active lease, the exact current checkpoint and the
selected execution afresh inside one short write-locked transaction (see the
[workflow model](../architecture/workflow-model.md#current-verification-configuration-and-execution-boundary)); a change committed
after the panel's read, or while the host observed Git, is refused rather than recorded against a newer checkpoint or workspace.

### Run-isolated asynchronous controls

Every asynchronous control that changes a run's Agent work or settings — the six ordinary requests (Planner,
CriticalReviewer, Resolver, Implementer, CodeReviewer, ReviewCorrection), the four format repairs, plain and guided
review-correction authorization, and the five settings (Codex model/effort, Claude model/effort, token warning, token
stop, Claude turn limit) — belongs to **one run's mounted interaction lifetime**. The server stays the only authority for
claims, authorizations and accepted settings; this contract governs only what the browser shows.

- **Lifetime and identity.** A lifetime begins when the control commits for a run and ends when the run changes or the
  control unmounts; returning to an earlier run is a new lifetime (A to B to A never restores A's pending state, error,
  saved label, draft, or synchronization warning). Every committed render binds its handlers to its own lifetime, so a
  handler retained from an earlier render stays rejected even after the control returns to the same run. A request is
  bound to an explicit run, that lifetime, and one submission. Lifetimes are activated and ended in effects, never by
  mutating refs during render, so StrictMode's extra mount/cleanup pair and abandoned renders cannot own or leak one.
- **Before the API.** A handler captured for an obsolete lifetime, or an explicitly supplied run id that is not the
  current run, is rejected before any request is sent or any state is written (including a local validation message), and
  without touching a valid current request. A second
  submission of the same control while one is in flight is ignored synchronously (no second request); independent
  controls and the two providers stay usable together, and there is no global UI mutation lock.
- **After every asynchronous boundary.** Ownership is re-checked before writing pending or error state, refreshing,
  consuming a refresh result, updating a saved label, clearing a draft, or reporting success. The resolved boolean is
  true only for an accepted request whose lifetime is still current, so a component never treats a late result as
  permission to continue. Drafts and truthful refresh-failure feedback are preserved within the current interaction.
- **Flows and owning identity.** A component flow (save or clear, local updates, then the authoritative refresh) is an
  operation owned by the committed identity that owns the component's local state: the run plus the authoritative value
  (or, for guidance, the escalation). Its continuations stay valid only while that identity is current and no newer flow of
  the same control began, so an older flow never clears a newer draft or writes a stale synchronization warning, including
  when the authoritative value changes in the same run during the request or the refresh, and returning to an earlier
  identity is a new one; this holds for every control that re-derives local state from an authoritative value, including
  the Codex model/effort control's save and clear. A guidance submission additionally clears the draft only if it was not edited since it was sent
  (an edit that yields identical text still counts), and never writes to another escalation's or a remounted entry. A draft or saved value is updated
  as soon as the server accepted the change, so text typed while the refresh is pending is kept. When a control's owning
  identity changes (the run, the authoritative value, or the escalation) its selections, drafts, validation, and
  synchronization messages are re-derived from the new identity; a parent's `key` is not required for that.
- **Stale completions are not cancellations.** A request the server accepted remains a real operation when the UI has
  moved on. Ignoring its completion does not cancel, undo, retry, or reinterpret it as refused; the next authoritative
  status or cockpit read shows its real outcome.
- **Scope.** The shared lifetime is `useRunActionLifetime`/`useRunScopedAction`; each concrete typed operation stays in
  its own hook and keeps its HTTP operation, serialization, validation bounds, safe error mapping, and wording.
  Read-only fetching, project registration, run creation, and verification-recipe management are unchanged.

### Provider account usage guardrails

Account usage is a provider-reported, time-windowed allowance snapshot.
Unavailable data is shown as `Unknown`, never as zero.

**Implemented today (Codex only, explicit and null by default).** A run may carry an optional, run-scoped Codex
account-usage **stop** ([ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md))
and, independently, an optional advisory **warning**
([ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)), each a whole percentage from 1
through 100 that is absent unless the owner saves it. The stop is enforced at claim and again before dispatch and ends a
claimed attempt before dispatch with a recorded decision; the warning is shown only when the owner explicitly checks it
and never refuses, stops or reorders anything. Neither has a default, and neither is an eligibility, readiness or
capacity claim. See
["Codex account-usage warning (advisory, explicit check)"](#codex-account-usage-warning-advisory-explicit-check).

**Not implemented (target end state).** The following remains future behavior and nothing in the cockpit does it today:
Claude account-usage observation, warning and stop thresholds (no bounded machine-readable observation exists for this
host); an 80% warning and 90% stop **default** for every reported window; thresholds applied to every reported window
as a provider-wide policy rather than a per-run setting; a provider-guardrail waiting state for affected runs;
resumption on a fresh allowance snapshot; and a scoped human override. When any hard threshold is reached the target
behavior is:

- no new attempt starts for the affected provider;
- the active attempt may finish by default, unless a stricter cancellation
  policy is configured and supported safely;
- affected runs enter a provider-guardrail waiting state;
- another provider may continue only when the workflow has independent eligible
  work and policy permits it;
- resumption requires a fresh allowance snapshot below the stop threshold or an
  explicit, scoped human override.

Provider account usage does not replace run budgets. A run may have budget left
while its provider allowance is exhausted, or the reverse.

### Host-measured Agent process-duration summary

A separate, read-only summary shows how much Agent process time the host has
actually measured for this run so far — pure telemetry, never a budget, never
combined with the run budgets above. It reports one of six evidence states: no
Agent attempt dispatched yet; a complete measured total (shown even when that
total is a real zero); attempts still running with no complete picture yet;
partial evidence (some attempts measured, at least one terminal attempt
missing evidence); evidence unavailable (terminal attempts exist but none of
them produced usable evidence); or every observed measurement is valid but
their combined total is too large to display. The measured total is shown
only in the complete state, so it can never be mistaken for an exhaustive
figure while evidence is still partial, pending, unavailable, or
unrepresentable, and a genuine zero-duration measurement is never confused
with "nothing measured yet" — it displays as an explicit zero, distinct from
a positive value too small to show as a whole millisecond (shown as "less
than 1 ms" rather than rounded down to zero). A run with any missing or
malformed evidence is never described as though every dispatched attempt had
already concluded when attempts are, in fact, still running. See the
"Run-wide Agent process-duration evidence summary" section of the
[agent-collaboration-protocol](../architecture/agent-collaboration-protocol.md)
for its exact evidence-state contract.

This same "never trust a still-running or undispatched attempt's own record"
rule also applies to that attempt's individual process-execution evidence
(outcome, exit code, measured duration) shown beside its semantic outcome —
distinct from, and unaffected by, this run-wide summary above, which already
reads each attempt's status directly rather than through the per-attempt
projection. Each role's own status view, the cockpit's latest-attempt panel,
and the collaboration-message evidence drill-down all show that attempt's
process outcome/exit code/duration as unknown whenever the attempt has not
concluded, even if the underlying record already carries seemingly
well-formed process fields — the presentation logic checks the attempt's own
running/dispatched state before it ever consults the record's shape, exactly
as it already does for token usage. The attempt's configured timeout is a
separate, always-known value shown regardless of this check.

### Configured Claude Implementer permission mode

A bounded, read-only fact on the Claude Implementer attempt status — the
`acceptEdits` CLI permission mode that this role's single supported adapter
contract fixes for every invocation. It states what the adapter is configured
to pass, never what the provider actually honored, never that another mode is
available, and never that another invocation is eligible; it is shown beside
the existing provider, role, permission profile, and adapter contract facts on
the implementation action, never as a replacement for any of them. It is
populated only when this attempt's own provider, role, permission profile, and
adapter contract version all agree with the current, single supported
implementation path (`ClaudeCode`, `Implementer`, `WorkspaceEditOnly`, and exactly
`claude-implementation-v1` or `claude-implementation-v2`) — for example, a historical attempt recorded
against a superseded adapter contract version shows `Unknown` here exactly
like every other assignment fact, never the current adapter's mode by
assumption. It carries no runtime-control, authorization, or claim-eligibility
meaning of its own; the backend remains the sole authority for every claim and
dispatch decision.

### Configured Claude Implementer session persistence

A second, sibling bounded fact on the same attempt status — whether the
current `ClaudeImplementationAdapter`'s fixed `--no-session-persistence`
argument configures the Claude CLI's own provider-session persistence as
`Disabled`. It states what this attempt's adapter contract is configured to
pass, never a provider-observed result, and it is populated under exactly the
same coherence rule and the same `Unknown`/`null` fallback as the configured
permission mode above: no attempt yet, and a valid but historical or
mismatched assignment, both show `Unknown`/`null` here, never the current
adapter's configuration by assumption. An invalid or otherwise unparseable
assignment is a different case entirely — it is not a success value with
this fact reported as `Unknown`; it remains the existing fail-closed
`agent_attempts.invalid_assignment` error for the whole status, exactly as it
already is for every other assignment fact on this attempt. This fact is entirely distinct from
DevalCopilot's own durable attempt history: every attempt's status, outcome,
checkpoints, and evidence shown elsewhere on this page remain fully durable
and queryable regardless of this flag, which only concerns whether the
provider's own CLI process retains a resumable session of its own. It does
not add, change, or expose a provider-session identifier, a resume/open/fork
control, or any other session-management capability, and it does not change
the host-wide `ProviderRuntimePreflight.Sessions` fact shown elsewhere. It
carries no runtime-control, authorization, or claim-eligibility meaning of its
own; the backend remains the sole authority for every claim and dispatch
decision.

### Configured Claude Implementer permission confirmations

A third, sibling bounded fact on the same attempt status — whether the
current `ClaudeImplementationAdapter`'s fixed `--permission-prompts none`
argument configures the Claude CLI's interactive permission-confirmation
prompts as denied in print mode, shown as `None`. It states what this
attempt's adapter contract is configured to pass, never a provider-observed
result and never invocation eligibility, and it is populated under exactly
the same coherence rule and the same `Unknown`/`null` fallback as the two
configured facts above: no attempt yet, and a valid but historical or
mismatched assignment, both show `Unknown`/`null` here, never the current
adapter's configuration by assumption; an invalid or otherwise unparseable
assignment remains the existing fail-closed `agent_attempts.invalid_assignment`
error for the whole status, exactly as it already is for every other
assignment fact on this attempt. It carries no runtime-control,
authorization, or claim-eligibility meaning of its own; the backend remains
the sole authority for every claim and dispatch decision.

### Configured Claude Implementer resume eligibility

A fourth, sibling bounded fact on the same attempt status — whether the
current `ClaudeImplementationAdapter`'s fixed `--no-session-persistence`
argument makes this attempt's Claude CLI provider session ineligible for
resume, shown as `Ineligible`. The
[Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
documents that a session started under this flag cannot be resumed; this
fact states that documented consequence for this attempt's own fixed adapter
configuration, never a provider-observed result, never a host-wide provider
capability assessment, and never invocation eligibility of any kind. It is
populated under exactly the same coherence rule and the same `Unknown`/`null`
fallback as the three configured facts above: no attempt yet, and a valid but
historical or mismatched assignment, both show `Unknown`/`null` here, never
the current adapter's configuration by assumption; an invalid or otherwise
unparseable assignment remains the existing fail-closed
`agent_attempts.invalid_assignment` error for the whole status, exactly as it
already is for every other assignment fact on this attempt. It does not add,
change, or expose a provider-session identifier, a resume/open/fork control,
or any other session-management capability, and it does not change the
host-wide `ProviderRuntimePreflight.Sessions` fact shown elsewhere. It
carries no runtime-control, authorization, or claim-eligibility meaning of
its own; the backend remains the sole authority for every claim and dispatch
decision.

### Configured Claude Implementer built-in tools

A fifth, sibling bounded fact on the same attempt status — the current
`ClaudeImplementationAdapter`'s fixed `--tools` allowlist, shown as
`Read,Edit,Write,Glob,Grep`. The
[Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
defines `--tools` as restricting which built-in tools the CLI process may
use, and explicitly states it does not affect MCP tools. This fact states
only that this attempt's adapter contract is configured to pass that
allowlist argument — never an observation of the provider's actual effective
tool access, never a complete security or sandbox boundary, never an MCP
tool restriction, and never invocation eligibility of any kind. It is
populated under exactly the same coherence rule and the same `Unknown`/`null`
fallback as the four configured facts above: no attempt yet, and a valid but
historical or mismatched assignment, both show `Unknown`/`null` here, never
the current adapter's configuration by assumption; an invalid or otherwise
unparseable assignment remains the existing fail-closed
`agent_attempts.invalid_assignment` error for the whole status, exactly as it
already is for every other assignment fact on this attempt. It carries no
runtime-control, authorization, or claim-eligibility meaning of its own; the
backend remains the sole authority for every claim and dispatch decision.

### Configured Codex Planner, Resolver, and CodeReviewer command sandbox and rollout persistence

Two bounded, read-only facts on each of the three current Codex read-only
roles' own attempt status — Codex planning, Codex challenge resolution, and
Codex code review — mirroring the Claude Implementer's own configured facts
above but for the shared `CodexProcessInvoker`'s fixed arguments:
`configuredCommandSandbox`, shown as `read-only` (the invoker's fixed
`--sandbox read-only` argument), and `configuredRolloutPersistence`, shown as
`Disabled` (the invoker's fixed `--ephemeral` argument). The
[Codex CLI reference](https://developers.openai.com/codex/cli/reference)
documents `--sandbox read-only` as the sandbox policy applied to
model-generated commands, and `--ephemeral` as running without persisting
session rollout files to disk. Both facts state only what this attempt's
adapter contract is configured to pass — never the provider's actual
effective isolation, never a complete access-control boundary, never
provider-session resume eligibility, and never invocation eligibility of any
kind. Each role's own attempt claims a concrete, non-Unknown
`AgentPermissionProfile.ReadOnly` and a role-specific, fixed adapter contract
version (`codex-planning-v1` for the Planner, `codex-challenge-resolution-v1`
for the Resolver, `codex-implementation-review-v1` for the CodeReviewer) at
claim time; both configured facts are populated only when that attempt's own
provider, role, permission profile, and role-specific adapter contract
version all agree with the current, single supported path for that role —
no attempt yet, and a valid but historical or mismatched assignment (for
example a legacy attempt claimed before these two columns were ever
populated for its role), both show `Unknown`/`null` here, never the current
adapter's configuration by assumption. An invalid or otherwise unparseable
assignment fails the whole status closed with the existing
`agent_attempts.invalid_assignment` error, exactly as it already does for the
Claude Implementer role, rather than returning a success value reporting
`Unknown`. These facts carry no runtime-control, authorization, or
claim-eligibility meaning of their own; the backend remains the sole
authority for every claim and dispatch decision.

### Configured Claude CriticalReviewer and Implementer ReviewCorrection permission mode, session persistence, permission confirmations, resume eligibility, and built-in tools

Five bounded, read-only facts — the same set already shown for the Claude
Implementer's initial implementation path above — on each of the two
remaining current Claude Code paths' own attempt status: the CriticalReviewer
review and the Implementer's own review-correction attempt. Each states only
what that attempt's fixed adapter contract is configured to pass: `plan`
(CriticalReviewer) or `acceptEdits` (ReviewCorrection) for
`configuredPermissionMode`; `Disabled` for `configuredSessionPersistence`;
`None` for `configuredPermissionPrompts`; `Ineligible` for
`configuredResumeEligibility`; and `None` (the CriticalReviewer adapter's
explicit empty `--tools` argument) or `Read,Edit,Write,Glob,Grep`
(ReviewCorrection) for `configuredBuiltInTools`. The current
`ClaudeCriticalReviewAdapter` and `ClaudeReviewCorrectionAdapter` pass all of
these arguments explicitly, and the
[Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
documents them exactly as it does for the Implementer's own facts above,
including that `--no-session-persistence` prevents resume and that `--tools`
governs built-in tools but never MCP tools. None of these facts are observed
effective access, an MCP or complete security boundary, model or effort, or
invocation eligibility of any kind. Each attempt claims a concrete,
non-Unknown permission profile — `ReadOnly` for CriticalReviewer,
`WorkspaceEditOnly` for ReviewCorrection — and a distinct, fixed adapter
contract version (`claude-critical-review-v1` for CriticalReviewer and
`claude-review-correction-v1` or, for every new claim, `claude-review-correction-v2` for ReviewCorrection) at claim time; all five facts are
populated only when that attempt's own provider, role, response contract,
permission profile, and adapter contract version all agree with the current,
single supported path for that role — no attempt yet, and a valid but
historical or mismatched assignment, both show `Unknown`/`null` here, never
the current adapter's configuration by assumption. An invalid or otherwise
unparseable assignment fails the whole status closed with the existing
`agent_attempts.invalid_assignment` error, exactly as it already does for
every other role, rather than returning a success value reporting `Unknown`,
and never discloses the raw malformed value. Review correction's own
current-review lineage and budget/escalation semantics are entirely
unaffected by these facts — they are resolved exactly as before, and these
five facts are attached only after that resolution, never in place of it.
These facts carry no runtime-control, authorization, or claim-eligibility
meaning of their own; the backend remains the sole authority for every claim
and dispatch decision.

### Sealed Agent-artifact window inspection

From within the collaboration-message evidence drill-down (see "Collaboration
timeline" above), an owner may select one of the four evidence purposes
actually present in that exact attempt's own bounded artifact-metadata list —
context manifest, standard output, standard error, or final response — and
load a bounded, integrity-verified text window of that sealed artifact, with a
manual "load next window" action for further windows rather than automatic or
continuous paging. Selection is offered only for a purpose this attempt's own
metadata actually names; no other artifact purpose, other attempt, or other
run is ever reachable from this control. Selecting a different purpose,
switching to a different run or collaboration message, or closing and
reopening the drill-down each discard any previously loaded text before the
next window is fetched — no stale text from a prior selection is ever shown
alongside a new one.

Every window is rendered as plain text, never interpreted as HTML or Markdown,
so content shaped like markup is shown exactly as captured rather than
executed or formatted. Each view carries an explicit caveat matched to how
that exact purpose is actually produced, never one blanket claim asserted for
all four: the context manifest is composed entirely by DevalCopilot's own
application code from already-curated durable fields, never raw, unexamined
provider output, while standard output, standard error, and final response
are raw provider text passed through a fixed, best-effort redaction pattern
list before capture. Neither caveat is a guarantee that no sensitive content
remains — an unusual identifier in a host-constructed field, or an
unrecognized secret format in redacted provider text, may still appear — and
neither purpose's text is ever a live tail of a running attempt or evidence
about the run's current source state. An artifact with no
recorded row for the selected purpose, a recorded artifact whose sealed file
cannot be verified (missing or failed integrity check), and a genuinely
unavailable evidence link (mirroring the drill-down's own existing
`NoAgentEvidence`/`AttemptLinkBroken` states) each show their own distinct,
non-alarming message — never a generic error, and never partial or
unverified text. Fetched text is never written to browser storage, a URL, or
any log.

### Agent attempt history and evidence

The cockpit's Usage &amp; Evidence rail offers a collapsed-by-default "Agent attempt history"
for the selected run, so a failed or interrupted Agent attempt that produced sealed output but
no collaboration message can still be inspected. Nothing is fetched until the owner opens it.

- **History list.** Opening it requests one bounded page (10 attempts, server cap 20) of the
  run's Agent attempts, newest first, showing attempt number, role, provider, status, and result.
  "Load older attempts" follows the server's before-number cursor; rows are appended without
  duplicates or gaps. An empty run shows an explicit empty state. A failed page shows a fixed
  message with Retry; already-loaded rows stay visible and Retry re-requests the same cursor. An
  attempt whose recorded identity could not be verified is listed by number and status only, says so,
  and offers no Inspect action.
- **Selected attempt.** "Inspect" on a row fetches that attempt's evidence metadata (status,
  claim and end times, and the size and truncation of each recorded artifact among context manifest,
  standard output, standard error, and final response) and shows one attempt at a time under a
  clear "historical evidence" caveat that says nothing about the run's current state. Loading,
  retryable error, "no longer available", and "identity could not be verified" states are explicit.
- **Claude-reported model limits.** For a Claude attempt, the selected detail also shows a "Claude-reported model limits" section
  (a historical fact stored when the attempt concluded; see
  ["Claude-reported model context limits"](../architecture/agent-collaboration-protocol.md#claude-reported-model-context-limits)
  and [ADR-0023](../decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md)). It lists each
  model identifier Claude reported, as plain text, with its context-window tokens and its maximum output tokens, ordered by
  identifier, and says the entries are models listed by Claude and not independently proven to have been used, and that remaining
  context and the capacity of the next invocation were not measured. An attempt with no recorded evidence shows "Not recorded",
  never zero. The section never shows a percentage, a fullness meter, an estimate derived from token usage, a readiness or
  eligibility claim, or an action, and a Codex attempt shows no such section. It is replaced with the selection and discarded with
  the history, so a limit of one attempt or run is never shown for another.
- **Artifact text.** Only after the owner chooses one of the purposes actually present does the
  viewer offer "Load"; further windows load only on "Load next window". Text is rendered as plain
  text, never HTML or Markdown, with the same purpose-specific caveat as the message-linked viewer
  (host-composed manifest versus best-effort-redacted provider output that may still contain
  secrets). The text is shown exactly as captured, so it may itself contain prompt-, session-, path-, or
  hash-like content; only the metadata and status fields are guaranteed free of such data. A window that is missing, fails integrity verification, or belongs to an unverifiable
  attempt shows a fixed message and no text. A failed later window retries at the same offset
  without duplicating text.
- **Resets and storage.** Closing the history, closing the attempt, choosing another purpose, choosing
  another attempt, or switching runs discards the loaded rows, the selection, and all accumulated
  artifact text, and a late response for a previous run or attempt is never applied. Artifact text is
  never written to browser storage or the URL and is never logged.

This surface neither invokes nor resumes a provider and grants no eligibility: it does not change
claims, dispatch, controls, or workflow state. See
["Agent-attempt history and evidence inspection"](../architecture/agent-collaboration-protocol.md#agent-attempt-history-and-evidence-inspection)
for the routes, paging, and identity rules.

## Evidence surface

The right rail provides contextual evidence categories:

- Changes;
- Local verification;
- Review findings;
- GitHub CI;
- Artifacts;
- Approvals.

Technical data deliberately omitted from the primary header lives here,
including branch, commit, Git fingerprint, event sequence, attempt identifiers,
provider session links, timestamps, and freshness.

Every success or approval identifies the exact source fingerprint it proves.
Stale evidence remains visible as history but cannot appear valid for current
source. When the rail is collapsed, it retains badges for failures, pending
approvals, and unverified changes.

## Live output

The bottom drawer streams bounded stdout and stderr for the active attempt. It
identifies the provider or tool and attempt number. Complete output is retained
according to artifact limits; the live surface may truncate without losing the
artifact reference.

The drawer is not a general shell. Input is exposed only for a typed adapter
interaction that explicitly supports it.

## Concurrent run behavior

The MVP supports bounded concurrent runs across distinct approved projects.
The default global limit is two active mutating runs and is configurable down to
one. Waiting on CI or human approval does not consume an agent execution slot.

Concurrency invariants are:

- at most one mutating run for a canonical repository at a time;
- exactly one writer for a run worktree;
- provider and command processes are owned by one immutable attempt;
- SQLite state changes continue through the serialized ingestion boundary;
- project switching never changes execution state;
- pause, stop, budget, and provider-allowance decisions are scoped to the
  intended run or provider and do not silently affect unrelated projects.

A conflicting run is queued with an explicit reason. Read-only history,
evidence inspection, and external CI observation remain available.

## Themes, density, and persistence

Dark Navy and Light are supported themes. Theme choice and panel layout may be
stored as non-sensitive local UI preferences. Run state, approvals, provider
credentials, and session secrets must never be stored as UI preferences.

Both themes use a vivid orange for Claude, warnings, and active attention with
contrast sufficient against their backgrounds. Meaning is reinforced through
text, shape, and iconography.

Navigation and contextual rails are collapsible, keyboard operable, and
resizable where practical. The collaboration column grows responsively. The
application preserves the user's layout preferences without changing the
durable run projection.

## Required read models

The frontend consumes generated contracts for these conceptual projections:

- `ProjectRunSummary`: project identity, execution number, stage, lifecycle,
  wait reason, and freshness;
- `RunCockpit`: objective, autonomous duration, active participant, stage map,
  available commands, and latest sequence;
- `AgentRuntime`: provider health, active attempt, model, effort, permission
  mode, provider session reference, context usage, and compaction capability;
- `BudgetStatus`: provider-specific stage and run usage, limits, confidence,
  and enforcement state;
- `ProviderAllowanceStatus`: provider, window, consumed percentage, reset time,
  thresholds, freshness, and availability;
- `CollaborationCard`: typed summary, actor, related stage and attempt, evidence
  references, and expansion capability;
- `EvidenceSummary`: changes, checks, findings, artifacts, approvals, Git
  fingerprint, and staleness;
- `LiveAttemptOutput`: bounded chunks, cursor, stream, attempt, and truncation
  state.

SignalR only announces that durable state advanced. Initial load, project
switching, reconnection, and missed-event recovery query authoritative read
models and journal cursors through the local API.

## MVP acceptance criteria

- The selected project, objective, lifecycle, stage, and active participant are
  understandable within seconds.
- Workflow and Evidence can collapse independently, and collaboration uses the
  released space.
- A real challenge and its resolution are visually connected.
- Model, effort, and permission-mode changes apply only at safe boundaries.
- Context usage and compaction never replace or erase durable history.
- Codex and Claude run budgets and account allowances remain separate.
- Reaching a configured provider stop threshold prevents a new invocation.
- Two different projects can progress concurrently without creating concurrent
  writers for one repository or worktree.
- Evidence exposes the exact source fingerprint and marks stale results.
- Restart and reconnect reconstruct the same cockpit from durable state.
- Dark Navy and Light themes retain readable status and attention contrast.
- The complete self-hosting demonstration can be supervised without opening a
  provider UI or copying a message manually.
