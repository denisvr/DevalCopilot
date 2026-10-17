# Agent collaboration protocol

## Purpose

The protocol enables constructive disagreement and evidence-based resolution
without creating an unbounded free-form conversation. It separates durable
workflow semantics from provider-specific session formats.

Codex and Claude Code have primary roles, not absolute authority. The
orchestrator owns authority and enforces the resolved plan, policies, budgets,
and gates.

## Message envelope

Every accepted agent message maps to a project-owned envelope:

```json
{
  "protocolVersion": "1.0",
  "messageId": "msg_01...",
  "runId": "run_01...",
  "taskId": "task_01...",
  "attemptId": "attempt_01...",
  "actor": { "kind": "Agent", "role": "Resolver", "provider": "Codex" },
  "recipient": { "kind": "Agent", "role": null, "provider": "ClaudeCode" },
  "type": "challenge",
  "inReplyTo": "msg_01...",
  "summary": "The proposed transaction boundary includes a long-running process.",
  "content": "Persist intent before launching the process.",
  "evidenceRefs": ["artifact_01..."],
  "suggestedActions": ["revise-plan"],
  "createdAtUtc": "2026-09-11T12:00:00Z"
}
```

Provider session identifiers and raw response locations are metadata on the
attempt. They do not replace the project envelope.

## Role-first message authorization and eligibility

Per ADR-0009, both a real Agent attempt's own message-authoring authority
(write side) and every later attempt's upstream-input eligibility (read side)
are decided by immutable role, never by which provider produced an attempt.
The attempt's `AgentRole` is resolved once, at claim time, and never changes
afterward; `CollaborationMessageAuthorPolicy` maps that role to the closed set
of message types the role may author. Provider identity (`AgentProvider`) is
provenance only. Durable participant references use a neutral
`ParticipantIdentity`: `Kind` is one of None, Orchestrator, Agent, or Human,
while an Agent carries its role and provider as separate fields when those
facts are known. Runs, events, and collaboration messages persist those fields
separately and expose the same structured shape through the API. Provider
grants no authoring or eligibility authority and is never consulted when
deciding whether a role may author a message type or consume one as input.

Real terminal protocol output — a Proposal, an Acceptance or Challenge, a
Decision and revised Proposal, an Execution report, or a Review approval or
finding — is written only by that role's own atomic result handler
(`RecordAgentAttemptResult`, `RecordClaudeCriticalReviewResult`,
`RecordChallengeResolutionResult`, `RecordImplementationResult`,
`RecordImplementationReviewResult`), alongside the terminal Domain transition,
cardinality, artifact, checkpoint, and review-evidence invariants that
handler alone enforces. The generic collaboration-recording command cannot
write this output for a real Agent attempt: it rejects one outright, so a
caller can never append an extra message, record output with no corresponding
terminal outcome, or bypass a role's cardinality or evidence rules through
that path.

Human-authored, Orchestrator-authored, and the deterministic Simulated
walking-skeleton sequence's messages retain the legacy participant-kind
policy (`ValidateActorForType`) — these have no owning Agent attempt, or no
role at all, so no role-based authorization can apply to them. That legacy
policy is unchanged and unweakened by this stabilization.

On the read side, every `Create*Attempt` handler that consumes an earlier
role's collaboration output as its own workflow input — the Claude
critical-review attempt consuming a Planner Proposal, the Codex
challenge-resolution attempt consuming a CriticalReviewer Challenged review,
the Claude implementation attempt consuming an accepted or resolved Proposal,
the Codex code-review attempt consuming an Implementer Execution report —
resolves the message's owning attempt and checks its role, response-contract
coherence, and outcome/checkpoint facts; `AgentProvider` participates only as
provenance-integrity evidence against the message's own recorded `Actor`,
never as a fixed value compared to authorize the role. A Proposal that is a
Planner root or a Resolver revision is validated through one shared lineage
rule (`PlanningLineage`, see
[Optional second challenge round and escalation](#optional-second-challenge-round-and-escalation)).
The same correction
applies to every "already reviewed/resolved/implemented/code-reviewed"
deduplication query and its shared identity helper
(`ChallengeResolutionInputIdentity`, `CodeReviewInputIdentity`,
`ImplementationInputIdentity`): a prior successful attempt is found by role,
response contract, outcome, and exact input identity, never narrowed to one
fixed provider first.

Recipient uses the same neutral identity shape. A recipient role remains null
when the historical fact records only the target provider; the system does not
invent a role during migration or projection. Frontend eligibility selectors
use the actor's role and provenance rather than provider-branded participant
values. Provider assignment facts are now durably recorded on the Attempt, but
the current initial Implementer claim path still fixes Claude Code. Provider
substitution and manual provider selection are not enabled. Gemini remains
deferred behind ADR-0011; the provider-neutral workflow owns role, contract,
and outcome semantics independently of provider provenance.

## Current durable ledger boundary

The current implementation persists a project-owned, append-only ledger of
validated `1.0` envelopes for one run, with an optional owning attempt and a
monotonic timeline sequence. It records bounded summaries and typed structured
details only; it does not persist a provider-native transcript, executable
path, environment value, credential, provider configuration, or raw output.
The deterministic walking-skeleton sequence writes envelopes marked
`Simulated`; this label is not evidence of provider observation, authentication,
or invocation.

Codex's Planning step, Claude Code's Critical review step, and Codex's
challenge-resolution step are the first three message-producing stages with a
real, live provider invocation. A durable Agent attempt of either provider
records a bounded context manifest, runs the provider's own CLI as a real
child process, and preserves its raw stdout, stderr, and final-response
content only as bounded sealed artifacts outside the database — never as
ledger or API content. A structurally valid Proposal, Acceptance, Challenge
set, or Decision-set-plus-revised-Proposal is appended to the ledger only
after passing the same protocol/schema validation as any other message; an
invalid, failed, or source-drifted attempt appends none. A Claude
critical-review attempt reviews exactly one already-recorded,
provider-observed Codex Proposal bound to the same run, isolated workspace,
checkpoint, and fresh Git fingerprint; it never edits the repository, and it
returns exactly one Acceptance or one to five Challenge messages, never both
and never neither.

A Codex challenge-resolution attempt is claimed only against one specific,
already-completed Challenged Claude critical-review attempt, and persists the
exact ordered input set that attempt is bound to as `AttemptInputMessage`
rows: the original Proposal at sequence 0, then every Challenge that review
raised, in the same timeline order the review itself recorded them — never a
caller-selected subset, never a different order. Given that fixed context, the
provider resolves every Challenge explicitly and proposes a revision; on
success, the attempt atomically appends one Decision per Challenge (each
replying to the Challenge it resolves) plus exactly one revised Proposal
(replying to the original Proposal) in a single transaction — never a partial
set, never a Decision left unresolved. Before that provider invocation ever
starts, and again in the same short transaction that commits the dispatch
marker, the run/workspace/lease/checkpoint eligibility is revalidated and the
attempt's own exact ordered input set is independently re-checked against
every other already-Resolved challenge-resolution attempt; a resolution of the
identical ordered Proposal-plus-Challenge-set already existing supersedes this
attempt with a truthful `InputAlreadyResolved` outcome before the provider is
ever invoked — never a duplicated resolution, and never silently retried. The
revised Proposal a resolution produces can re-enter the critical-review path
above as an ordinary Proposal; Acceptance remains terminal for that review
cycle.

Claude Code's Implementer step is the fourth message-producing stage with a
real, live provider invocation: it durably implements exactly one
authoritative resolved plan, edited entirely inside the run's owned worktree.
Exactly two resolved-plan forms are eligible, identified only by the plan's
own Proposal message id, never reconstructed from display text: an original,
provider-observed Codex Proposal with a completed, successful Claude Accepted
review; or the provider-observed revised Proposal a completed, successful
Codex Resolver attempt itself produced. The attempt persists the exact
ordered input identity that form is bound to as `AttemptInputMessage` rows —
the Proposal at sequence 0, then either its Acceptance or its complete
ordered Decision set, never both. Claude never runs Git, a verification
command, a commit, a push, a package install, or any network operation during
this attempt; its only capability is reading and editing files inside the
worktree. Because Claude's implementation tool allowlist never includes Git
or any process tool, it can never move `HEAD` itself: after the process
exits, the orchestrator independently re-reads fresh Git evidence and first
checks whether `HEAD` itself still matches the immutable starting
checkpoint's own `HEAD` — a changed `HEAD` is proof of an external or
unauthorized mutation and is recorded as `ImplementationHeadChanged`, never
`Implemented`, regardless of what the report itself claims to have changed.
Only when `HEAD` is unchanged, the process succeeded, and a valid, schema-
conformant implementation report's self-reported changed-path set exactly
matches that independently observed evidence does the attempt record one new
immutable Git checkpoint plus its changed-file rows, mark itself
`Implemented`, and append exactly one Execution report replying to the
implemented Proposal — all atomically, after every external process has
already finished. Any other outcome — a failed invocation, an invalid or
untrustworthy report, a changed `HEAD`, or evidence that could not be
captured — is recorded truthfully instead (`NoChangesProduced`,
`InvalidStructuredOutput`, `ProviderInvocationFailed`,
`ImplementationHeadChanged`, or `CheckpointEvidenceUnavailable`), and the
filesystem is never rolled back or silently discarded; whenever the worktree
may have changed without a verified, trustworthy result, the workspace is
also flagged `NeedsAttention` for a human to inspect, and no further attempt
is auto-retried against it. Every boundary value a recording command
receives — the starting checkpoint reference, the completion `HEAD`/
fingerprint shapes, every independently observed changed path, the sealed
artifacts, and the implementation report itself (even one a caller
constructed by hand rather than through the schema parser) — is
independently re-validated before any mutation, through the same shared
validation both the parser and the recording command call, so the two can
never silently drift apart.
Before that provider invocation ever starts, and again in the same short
transaction that commits the dispatch marker, eligibility is revalidated and
this attempt's exact plan-and-starting-checkpoint identity is independently
re-checked against every other already-`Implemented` attempt; an
implementation of the identical plan and checkpoint already existing
supersedes this attempt with a truthful `InputAlreadyImplemented` outcome
before the provider is ever invoked. A host restart that finds a
dispatched-but-unrecorded implementation attempt never guesses its outcome:
reconciliation independently re-reads fresh Git evidence before marking the
attempt `Interrupted` and, if the fingerprint no longer matches (or that
evidence itself could not be captured), the workspace `NeedsAttention` as
well.

The durable Review-finding, Revision-response, and Codex implementation-review
envelopes are now explicit, bounded workflow facts. Production contains real
Codex and Claude provider adapters and supervisors for the implemented roles;
automated tests use deterministic adapters and doubles and never invoke a real
provider. The durable envelopes do not authorize automatic verification,
publication, fallback, or parallel execution.

The cockpit presents the bounded `Challenge`, `Decision`, `ReviewFinding`, and
`RevisionResponse` fields as typed cards, including `Decision.resolution`'s own
closed enum (`accepted`, `partiallyAccepted`, `rejected` — see
`ChallengeResolutionOutputSchema`); any other value fails closed rather than
rendering as if it were a valid protocol value. A reply is presented as a
verified parent relationship only when the parent message is both present in
the current run's currently loaded timeline and an earlier, protocol-compatible
parent for the reply's own type per the reply-semantics table below; a matching
id that fails either check is reported only as an observed reference, never
reconstructed into an asserted relationship. A parent absent from the loaded
timeline is reported only as not present in what is currently loaded — never
asserted to be outside the API's bounded window, since its absence does not
prove that. The presentation never adds affected paths to the durable
`ReviewFinding` ledger and never treats malformed or unknown structured content
as a semantic fact.

## Run execution mode and isolation

Every run carries an immutable execution mode ([ADR-0014](../decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)):
`Legacy` (0, recorded before modes existed), `Simulated` (1), or `ManualAgent` (2). The deterministic simulator, including
step recording, completion, and Simulated-provenance messages, operates only on `Simulated` and `Legacy` runs; the six Agent
claim paths (with their repair variants), their eligibility feeds, and the final dispatch gate operate only on `ManualAgent`
and `Legacy` runs; standalone Process attempts operate only on `Legacy` runs. The mode is read afresh at the claim, at the
commit seam, and at dispatch, so a stale tracked entity never confers authority, and a stored number outside the set admits
nothing. A manual run is created by recording an objective only; it creates no attempt, manifest, lease, or budget
consumption, and the explicit stage requests keep every existing workspace, checkpoint, provider, contract, source, budget,
and authorization gate. `CompleteSimulatedRun` accepts only a `Simulated` attempt, so it can never conclude an Agent
attempt or a manual run.

### Explicit abandonment and the claim paths

A human may end an inactive manual run as `Abandoned` ([ADR-0031](../decisions/0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md)). The protected
`POST /api/runs/{runId}/abandon` decides only from fresh untracked reads under the write lock and is refused while any attempt of the
project is not `Completed`, `Failed` or `Interrupted` (including an unrecognized status), so no Agent claim can already be in flight
when it commits. The other direction is the claim's own guard: the three Claude claim paths (critical review, implementation, review
correction) and the dispatch marker commit through a Run update that includes the `Lifecycle` concurrency token and therefore lose to a
concurrent abandonment; the Codex planning, challenge-resolution and code-review claims, which can claim a `Running` run without
changing a Run column, read the lifecycle untracked inside their short claim transaction after their first guard write took the write
lock and refuse before any insert (the sealed manifest is removed and nothing is consumed), including their repair variants, with the code
each already uses for an ended run: `runs.not_active` for planning and `runs.not_running` for challenge resolution and code review; the verification-diagnosis and diagnosis-correction claims already re-read it there. No new claim path, budget slot,
authorization or provider invocation is created by an abandonment or by reading its status, and an abandoned run offers no Agent request.

## Message types

### Proposal

Introduces a plan, design, implementation approach, or corrective action. A
proposal states assumptions, affected areas, verification, and known risks.

### Acceptance

Confirms that a proposal is feasible and consistent. Acceptance must include a
short rationale; passive agreement without evaluation is not sufficient.

### Challenge

Disputes a concrete claim, assumption, omission, sequence, or approach. A valid
challenge contains:

- the disputed item;
- impact if left unresolved;
- reasoning or evidence;
- a proposed alternative or a precise question.

Contrarian language without a material consequence is not a challenge.

### Question

Requests information required to continue. It identifies why the answer
changes the work. Questions that require product authority are escalated to the
human rather than guessed.

### Decision

Resolves one or more proposals or challenges as `Accepted`, `PartiallyAccepted`,
`Rejected`, or `Escalated`. It includes rationale, resulting plan changes, and
the exact next action.

### Execution report

Describes completed work and references the actual diff, commands, tests, and
artifacts. It does not assert success beyond the evidence.

### Review finding

Records severity, affected location, expected behavior, evidence, and required
disposition. Findings are stable records, not embedded only in prose.

### Revision response

Maps each finding to `Fixed`, `Disputed`, `Deferred`, or `NeedsHumanDecision`
with evidence and resulting source changes.

### Escalation

Summarizes the unresolved decision, available options, consequences, evidence,
and recommended choice. It contains no hidden default action.

### Human instruction

Records one human authorization replying to an Escalation: authorize one
additional review-correction claim, or, for the escalation that ends a proposal lineage, authorize one implementation claim
of its final plan (see
[Explicit human authorization of one escalated-plan implementation](#explicit-human-authorization-of-one-escalated-plan-implementation));
the fixed instruction text distinguishes the two. It is HumanSubmitted, addressed to the
Orchestrator, and carries the fixed authorization instruction plus either the
fixed default rationale or, only for an explicit guided authorization, one
bounded, normalized guidance text (see
[Bounded human guidance for one authorized review correction](#bounded-human-guidance-for-one-authorized-review-correction)).
It is never a general instruction channel.

## Version 1.0 reply semantics

The durable ledger validates each reply against this closed relationship table
(`CollaborationMessageReplyPolicy`). A root proposal starts a thread with no
reply; a revised proposal instead replies to the exact prior proposal it
supersedes, so a proposal's own reply is optional, never forbidden. Every
other message type must reply to an earlier message in the same run.

| Message type | Allowed parent type |
|---|---|
| Proposal (revised only; a root proposal has no parent) | Proposal |
| Acceptance, Challenge | Proposal |
| Decision | Proposal or Challenge |
| Execution report | Decision or Proposal (the current Increment 4 Implementer path always replies to Proposal; Decision remains the aspirational later full-loop shape) |
| Review finding | Execution report |
| Revision response | Review finding |
| Question | Proposal, Challenge, Decision, Execution report, or Review finding |
| Escalation | Proposal, Challenge, Decision, Execution report, Review finding, Revision response, or Question |
| Review approval | Execution report |
| Human instruction | Escalation |

Question and escalation are therefore bounded by the fact that needs an answer
or cannot be resolved; neither starts an ungrounded side conversation.

## Default interaction

### Planning

Codex receives the objective, project context, baseline, relevant instructions,
and selected evidence. It returns a proposal with scope, sequence, risks,
verification, and escalation points.

### Critical review

Claude Code receives the objective and proposal before implementation. It must
return either:

- an acceptance with feasibility rationale; or
- one or more material challenges.

This stage is real today, bound to one durable attempt per requested review. A
Challenged outcome leaves the plan awaiting an explicit resolution request
rather than looping automatically; resolution itself is a separate, real,
requested attempt described next. A Planner Proposal, or a Resolver's first
revised Proposal, may be reviewed once; the second review and its resolution
are the lineage's only additional round (see
[Optional second challenge round and escalation](#optional-second-challenge-round-and-escalation)).

### Resolution

This stage is also real today, bound to one durable attempt per requested
resolution of one specific Challenged review. Codex receives that review's
original Proposal and its complete, ordered Challenge set, and resolves every
challenge explicitly as `accepted`, `partiallyAccepted`, or `rejected`; a
partially accepted or rejected challenge must include reasoning. Resolving
never edits the repository. Changes become a new plan revision — one revised
Proposal replying to the original — rather than silently editing history, and
that revision can re-enter Critical review above once (the first revision
only). Resolving a challenged review of a first revision produces the final,
depth-two revision and records one human escalation atomically with it; that
revision is not reviewable or implementable and no further round exists.
Never a duplicated resolution of the exact same review: a resolution already
recorded for the identical ordered Proposal-plus-Challenge-set supersedes any
further attempt at claim or dispatch time.

### Execution and review

The initial Implementer currently uses Claude Code and implements only the
resolved plan, recording unexpected discoveries as challenges or questions.
Gemini is deferred until its administrator-provisioned policy prerequisite is
met. This stage is real today, bound to one durable
attempt per requested implementation of one specific resolved plan (an
accepted original Proposal, or a first revised Proposal that no later review
challenged, bound to its exact Acceptance when it was re-reviewed and
accepted — never a second revision); execution never
edits anything outside the run's owned worktree, and it never runs Git, a
verification command, or any network operation itself. It records only the
implementation's own Execution report and the new Git checkpoint its
independently observed changes produced; it never claims automatic
verification, review, or publication of those changes.

Codex reviewing the implementation is also real today, bound to one durable
attempt per requested review of one specific Execution report. Codex receives
that report, the exact plan the implementation consumed (the implemented Proposal;
[ADR-0017](../decisions/0017-review-the-implemented-plan-through-correction.md)), fresh bounded Git
evidence for its exact result checkpoint, and the exact status of every
currently enabled verification command's latest execution against that
checkpoint — every one of which must already be Passed, or the review is
never claimable. Codex never runs a verification command itself, and it
never edits the worktree: this stage is read-only, using the same bounded,
non-interactive invocation contract as Planning and Resolution above. It
returns either:

- an approval with a bounded rationale and residual risks, and zero
  findings; or
- one to ten material findings, each with a closed severity, a closed
  category, evidence, and the required change — never mixed with an
  approval in the same response.

A finding's optional repository-relative affected path is never recorded to
the durable collaboration ledger; it is surfaced only through the sealed,
redacted final-response artifact, exactly like every other bounded field
this protocol keeps out of structured content. Never a duplicated review of
the exact same evidence: a review already recorded for the identical
Execution report plus the identical ordered verification-execution set
supersedes any further attempt at claim or dispatch time — mirroring
Resolution's own exact-input-identity rule one level further down the
protocol. A successful correction appends a new checkpoint and report, but
re-review is an explicit user request only: the new checkpoint must first have
fresh Passed verification, and no state transition automatically re-enters
Execution from a changes-requested review. When the verification itself failed, the
review is not claimable; the human may instead request an explicit read-only diagnosis of that failure
(see [Explicit verification failure diagnosis](#explicit-verification-failure-diagnosis)).

## Authority matrix

| Action | Codex | Claude Code | Orchestrator | Human |
|---|---|---|---|---|
| Propose or challenge a plan | Yes | Yes | Records | May intervene |
| Resolve technical challenge | Primary | May dispute | Enforces bounds | Final escalation authority |
| Modify source in executor stage | Read-only by default | Primary | Grants scoped workspace | May take over |
| Run configured local verification | Recommends | Recommends | Executes | May execute manually |
| Approve review | Primary | Responds | Validates evidence and state | May override explicitly |
| Commit | No direct authority | No direct authority | Executes after gate | Approves policy-sensitive cases |
| Push or create PR | No direct authority | No direct authority | Executes after gate | Approves in MVP |
| Merge, release, deploy | No | No | Not enabled in MVP | External/manual |

## Structured output failure

An adapter validates protocol version, type, identifiers, cardinality, text
limits, and referenced artifacts. Invalid output produces a failed attempt with
the raw response preserved. The system may request one bounded format repair,
but it never invents missing decisions, evidence, or success. Today that
provision exists only as a **human-requested** repair of a read-only stage's
attempt: the Codex Planner Proposal (see
[One manual Codex Planner format repair](#one-manual-codex-planner-format-repair)) and the Claude
CriticalReviewer, Codex Resolver, and Codex CodeReviewer stages (see
[One manual format repair of the remaining read-only stages](#one-manual-format-repair-of-the-remaining-read-only-stages)).
There is no automatic retry, and no repair of a mutating Implementer or ReviewCorrection attempt.

## Context assembly

Agent input is assembled from selected durable records:

- objective and current task;
- the project's own root instruction files, accounted for exactly (see "Project instruction context in Agent
  manifests");
- current plan revision and unresolved challenges;
- relevant decisions and findings;
- Git fingerprint, a bounded selection of complete tracked-file hunks, and bounded previews of eligible untracked
  text files, each marked as complete, shortened, or omitted (see "Bounded tracked-hunk evidence in Agent manifests"
  and "Bounded untracked-file previews in Agent manifests");
- verification evidence and selected log excerpts;
- budgets, permissions, and expected response schema.

The complete historical transcript is not replayed by default. Raw transcripts
are artifacts available for inspection. Summaries carry source references so
the receiving agent can distinguish derived context from primary evidence.

The provider CLI safety contracts (Claude Code critical review and
implementation, and the deferred Gemini CLI boundary) are in the
[provider contracts](agent-collaboration-provider-contracts.md) companion; their
original headings below forward there.

Per-attempt execution evidence (semantic outcome versus host-measured and
provider-reported facts) and the run-wide Agent process-duration summary are in
the [execution evidence](agent-collaboration-execution-evidence.md) companion;
their original headings below forward there.

Read-only evidence inspection (recorded collaboration-input provenance, the
sealed Agent-artifact window, and Agent-attempt history and evidence) is in the
[evidence inspection](agent-collaboration-evidence-inspection.md) companion;
their original headings below forward there.

The manual read-only format repairs (the Codex Planner's and the remaining
read-only stages') are in the [format repair](agent-collaboration-format-repair.md)
companion; their original headings below forward there.

Provider-reported usage evidence (the Claude Code and Codex token-usage
contracts and Claude-reported model context limits) is in the
[usage evidence](agent-collaboration-usage-evidence.md) companion; their
original headings below forward there.

Run-scoped token-activity controls (the advisory per-provider warnings and the
per-provider stop at Agent claim) are in the
[token-activity controls](agent-collaboration-token-activity-controls.md)
companion; their original headings below forward there.

Account-usage observation and controls (the provider account-allowance
contracts, the run-scoped Codex account-usage stop at claim and dispatch and
the advisory Codex account-usage warning) are in the
[account usage](agent-collaboration-account-usage.md) companion; their original
headings below forward there.

Invocation configuration (the Codex model and reasoning-effort catalog
contract, the explicit Codex model and reasoning-effort requests, the explicit
Claude model-alias and effort requests and the optional Claude agentic-turn
limit for mutation attempts) is in the
[invocation configuration](agent-collaboration-invocation-configuration.md)
companion; their original headings below forward there.

Human guidance, escalation and diagnosis (bounded guidance for one authorized
review correction, optional direct human guidance for mutation requests, the
optional second challenge round and escalation, explicit human authorization
of one escalated-plan implementation and explicit verification failure
diagnosis) are in the
[guidance and escalation](agent-collaboration-guidance-and-escalation.md)
companion; their original headings below forward there.

Review correction, with its correction budget and escalation, the run-wide
Agent claim-count and reserved invocation-time budgets and their manual-intake
choice, is in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion, and the provider configuration observations formerly in the same
section are in the
[invocation configuration](agent-collaboration-invocation-configuration.md#provider-configuration-observations)
companion; the original Review correction heading below forwards to both.

## Provider runtime configuration

Host runtime preflight is intentionally narrower than provider runtime configuration, and is
itself layered into four distinct stages that must not be confused with one another:

1. **Executable discovery** — resolving a fixed candidate name to an absolute, existing path
   using only the host's normalized `PATH` and a small set of fixed fallback directories. Never a
   shell, never `PATHEXT`, never a filesystem enumeration.
2. **Launch-target validation** — for a provider whose only local discovery in Increment 2 is a
   native executable, discovery and launch-target validation coincide. Increment 4 adds one
   further, still narrowly bounded discovery path for a provider distributed as an npm package: a
   fixed, catalog-owned package-root candidate's `package.json` is read directly (strict size
   limit, strict parsing, exact expected package identity), and only its own declared `bin` entry
   is resolved, and only when that entrypoint remains beneath the package root. This never
   executes npm, npx, a package-manager shim, `.cmd`, `.bat`, cmd.exe, PowerShell, or any shell.
   More than one independently valid launch target is treated as ambiguous and fails closed —
   never chosen between silently.
3. **Version observation** — a successful direct executable version probe (or, for a validated
   npm-package launch target, a direct, fully qualified Node executable running that validated
   entrypoint) proves only that DevalCopilot observed one local runtime version at a point in
   time.
4. **Authentication/invocation readiness** — everything beyond an observed version: model
   availability, permission mode, context capacity, account usage, session continuity, and
   whether an invocation is actually eligible. These facts remain explicit `Unknown` until a
   later provider-owned adapter observes them through a reviewed contract. A future `Unsupported`
   value requires affirmative provider evidence; it is never inferred from the absence of a
   preflight probe.

A locally installed Codex desktop application's own private, versioned installation layout was
observed once, manually, through read-only inspection outside this discovery contract. That
layout is evidence that a directly executable Codex CLI can exist on a host — it is not an
approved stable discovery contract, and it is never added as an automatic fallback: it belongs to
a different application, is not catalog-owned, and carries no stability guarantee DevalCopilot
can rely on. In particular, an app-private or portable Codex desktop installation layout is never
searched for automatically at attempt-dispatch time; resolving the actual launch target for a new
Codex Planning attempt only reuses the durable `HostCapabilitySnapshot` row's resolved path from
capability discovery's own last successful probe, and only when that snapshot's reason code is
still `None` — it is revalidated, never re-searched, by this narrower dispatch-time read.

### Claude Code critical-review CLI safety contract

Moved to [Claude Code critical-review CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract)
in the provider contracts companion.

### Claude Code implementation CLI safety contract

Moved to [Claude Code implementation CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-implementation-cli-safety-contract)
in the provider contracts companion.

### Deferred Gemini CLI execution boundary

Moved to [Deferred Gemini CLI execution boundary](agent-collaboration-provider-contracts.md#deferred-gemini-cli-execution-boundary)
in the provider contracts companion.

### Review correction

Moved to [Review correction](agent-collaboration-correction-and-budgets.md#review-correction)
in the correction and budgets companion. Its former provider configuration
observations are in
[Provider configuration observations](agent-collaboration-invocation-configuration.md#provider-configuration-observations)
in the invocation configuration companion.

### Bounded human guidance for one authorized review correction

Moved to [Bounded human guidance for one authorized review correction](agent-collaboration-guidance-and-escalation.md#bounded-human-guidance-for-one-authorized-review-correction)
in the guidance and escalation companion.

### Optional direct human guidance for mutation requests

Moved to [Optional direct human guidance for mutation requests](agent-collaboration-guidance-and-escalation.md#optional-direct-human-guidance-for-mutation-requests)
in the guidance and escalation companion.

### Execution evidence versus semantic outcome

Moved to [Execution evidence versus semantic outcome](agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome)
in the execution evidence companion.

### Run-wide Agent process-duration evidence summary

Moved to [Run-wide Agent process-duration evidence summary](agent-collaboration-execution-evidence.md#run-wide-agent-process-duration-evidence-summary)
in the execution evidence companion.

### Recorded collaboration-input provenance

Moved to [Recorded collaboration-input provenance](agent-collaboration-evidence-inspection.md#recorded-collaboration-input-provenance)
in the evidence inspection companion.

### Sealed Agent-artifact window inspection

Moved to [Sealed Agent-artifact window inspection](agent-collaboration-evidence-inspection.md#sealed-agent-artifact-window-inspection)
in the evidence inspection companion.

### Agent-attempt history and evidence inspection

Moved to [Agent-attempt history and evidence inspection](agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection)
in the evidence inspection companion.

### Provider token-usage contracts

Moved to [Provider token-usage contracts](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)
in the usage evidence companion.

### Claude-reported model context limits

Moved to [Claude-reported model context limits](agent-collaboration-usage-evidence.md#claude-reported-model-context-limits)
in the usage evidence companion.

### Provider account-allowance contracts

Moved to [Provider account-allowance contracts](agent-collaboration-account-usage.md#provider-account-allowance-contracts)
in the account usage companion.

### Codex model and reasoning-effort catalog contract

Moved to [Codex model and reasoning-effort catalog contract](agent-collaboration-invocation-configuration.md#codex-model-and-reasoning-effort-catalog-contract)
in the invocation configuration companion.

### Explicit Codex model and reasoning-effort requests

Moved to [Explicit Codex model and reasoning-effort requests](agent-collaboration-invocation-configuration.md#explicit-codex-model-and-reasoning-effort-requests)
in the invocation configuration companion.

### Explicit Claude model-alias requests

Moved to [Explicit Claude model-alias requests](agent-collaboration-invocation-configuration.md#explicit-claude-model-alias-requests)
in the invocation configuration companion.

### Explicit Claude effort requests

Moved to [Explicit Claude effort requests](agent-collaboration-invocation-configuration.md#explicit-claude-effort-requests)
in the invocation configuration companion.

### Optional Claude agentic-turn limit for mutation attempts

Moved to [Optional Claude agentic-turn limit for mutation attempts](agent-collaboration-invocation-configuration.md#optional-claude-agentic-turn-limit-for-mutation-attempts)
in the invocation configuration companion.

### Per-provider run token-activity warnings

Moved to [Per-provider run token-activity warnings](agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-warnings)
in the token-activity controls companion.

### Per-provider run token-activity stop at Agent claim

Moved to [Per-provider run token-activity stop at Agent claim](agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-stop-at-agent-claim)
in the token-activity controls companion.

### Run-scoped Codex account-usage stop at claim and dispatch

Moved to [Run-scoped Codex account-usage stop at claim and dispatch](agent-collaboration-account-usage.md#run-scoped-codex-account-usage-stop-at-claim-and-dispatch)
in the account usage companion.

### Run-scoped advisory Codex account-usage warning

Moved to [Run-scoped advisory Codex account-usage warning](agent-collaboration-account-usage.md#run-scoped-advisory-codex-account-usage-warning)
in the account usage companion.

### One manual Codex Planner format repair

Moved to [One manual Codex Planner format repair](agent-collaboration-format-repair.md#one-manual-codex-planner-format-repair)
in the format repair companion.

### One manual format repair of the remaining read-only stages

Moved to [One manual format repair of the remaining read-only stages](agent-collaboration-format-repair.md#one-manual-format-repair-of-the-remaining-read-only-stages)
in the format repair companion.

### Optional second challenge round and escalation

Moved to [Optional second challenge round and escalation](agent-collaboration-guidance-and-escalation.md#optional-second-challenge-round-and-escalation)
in the guidance and escalation companion.

### Explicit human authorization of one escalated-plan implementation

Moved to [Explicit human authorization of one escalated-plan implementation](agent-collaboration-guidance-and-escalation.md#explicit-human-authorization-of-one-escalated-plan-implementation)
in the guidance and escalation companion.

### Explicit verification failure diagnosis

Moved to [Explicit verification failure diagnosis](agent-collaboration-guidance-and-escalation.md#explicit-verification-failure-diagnosis)
in the guidance and escalation companion.

### Bounded untracked-file previews in Agent manifests

Git evidence had a blind spot: the checkpoint fingerprint covers every untracked (`??`) path and its raw-content hash
(`git hash-object --no-filters`), but `git diff HEAD` does not print a new file's text, so a review stage saw the path
in `changedPaths` and none of its contents. The five Agent manifests that carry `changeEvidence` (critical review,
challenge resolution, implementation, implementation review including the correction variant, and review correction)
now also carry an `untrackedFiles` section inside `changeEvidence`, under the same untrusted-evidence boundary. The
verification-diagnosis manifest carries the same section, so six manifest families carry it.

- **Capture.** Seven of the eight claim handlers (critical review, resolution, implementation, code review, review
  correction, diagnosis-origin correction and verification diagnosis) request previews through
  `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync(includeUntrackedPreviews: true)`; planning requests none.
  `CaptureWithUntrackedPreviewsAsync` is the same preview capture without the instruction context, and a reader that does not
  capture instructions delegates to it. The plain `CaptureAsync` and every other caller are unchanged. Both return
  `UntrackedFiles`, one entry per `??` path of the same capture, in ordinal path order. Previews are read inside the reader's existing status/diff observation bracket
  and only against the hashes the fingerprint is computed from. The fingerprint, tracked diff, status, and changed
  paths are unchanged; a preview never feeds them.
- **Admission (Windows).** A file is previewed only when it is opened as a regular file, the operating system reports
  that the open handle's final path is exactly, and case-sensitively, the resolved worktree root plus the
  Git-reported relative path (`GetFinalPathNameByHandle`, describing what is open rather than racing a separate check, so no link, junction,
  swapped component, or case-distinct spelling redirected the open), the same handle is a regular, non-reparse,
  non-device file with exactly one link (`GetFileInformationByHandle` through `WindowsHandleFileFacts`, asked of the open
  handle before any byte or length is read and asked again of that handle after the bounded read, before the preview is
  accepted), its length is at most 64 KiB, the bytes hash to the fingerprint's git blob identity, contain no NUL byte, and
  are valid UTF-8. A hard link passes the final-path and identity checks because the name that was opened is the exact
  reported one, so the link count is the proof that no other name, inside the worktree or outside it, reaches the file; a
  file with several links is omitted even when every known name is inside the worktree, and aliases are never enumerated.
  Unavailable facts, a link count other than one and a reparse point are `containment_unproven`, with no text and no size
  that was not proven; a directory or device keeps `not_regular_file`. An omission spends none of the 4 KiB per-file or
  16 KiB aggregate preview budget, so it never hides a healthy sibling (see
  [ADR-0022](../decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md)). A legitimately redirected worktree
  root is accepted because the root is resolved with the same call. Path forms that could name something else
  (rooted, drive or stream syntax, dot or empty segments) are refused unopened; a directory is never opened. No
  bytes are read from a handle whose containment is not proven.
- **Other hosts.** There is no equivalent proof, so every untracked file is omitted as `containment_unproven`;
  lexical containment is never used as a substitute.
- **Bounds.** At most 4 KiB of each file and 16 KiB in total at the reader, then measured against the 32 KiB
  manifest ceiling as serialized together with the tracked hunks (see "Bounded tracked-hunk evidence in Agent
  manifests"): the preview budget is reduced until the manifest fits, and a last fixed-size summary (`omittedFileCount`, `omissionReason: manifest_budget`) states that everything was
  omitted. Cuts land on a character boundary.
- **Truthfulness.** Each entry says `preview: included|omitted`, an `omissionReason` (`not_regular_file`,
  `containment_unproven`, `missing`, `unreadable`, `content_identity_mismatch`, `too_large`, `binary`, `invalid_utf8`,
  `aggregate_limit`, `manifest_budget`, or `not_captured` for a path with no captured entry), `sizeBytes` when known,
  and `contentComplete`, which is true only when `text` is the entire file. A prefix is never described as a complete
  file, `allFilesComplete` summarizes it, and a fixed notice states that the list does not prove nothing else
  changed. Ignored files never appear (Git does not list them); a tracked-only or no-change capture produces a
  manifest byte-identical to the previous shape.
- **Boundaries.** Raw source text lives only in the sealed manifest, the provider's standard input and the existing
  authenticated sealed-artifact viewer, which already serves manifest content to the local operator and shows an admitted
  preview unredacted like any other manifest content. It is never written to SQLite, a status response, a log, an error, or
  browser storage, and no reason carries a path or an exception message. A file changed after its identity was captured is omitted as `content_identity_mismatch`; the
  fingerprint keeps the captured identity, so a preview can never be attributed to content it does not match.
- **Forward only.** A manifest sealed before ADR-0022 replays its exact bytes, including a preview the corrected reader would
  now refuse; a later fresh claim captures the truthful omission. Nothing recaptures, reseals or rewrites history.
- **Limits.** This closes generic untracked-preview delivery, not every filesystem read. The raw Git observation behind the
  checkpoint fingerprint (`status`, the per-path `hash-object` of untracked files) reads named paths and can still read the
  content of a file that another name links to outside the worktree; its hardening is a separate decision. The tracked diff, hunk
  and changed-line sample text of a NEWLY claimed stage was a further route and is closed separately (see "Attested tracked-change
  text in Agent manifests" and [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)).
  Files and link topology can change after observation.
- **Not included.** A generic file browser or retrieval tool, tracked-file full text, a per-file request, a provider
  flag, permission or session change, generated summaries, compaction, account or approval semantics, and any change
  to the checkpoint fingerprint, claim, budget, or dispatch rules. The evidence is untrusted context, not approval.

### Bounded tracked-hunk evidence in Agent manifests

> Since [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md) a NEWLY claimed manifest builds
> this selection from the host comparison of attested snapshots (see "Attested tracked-change text in Agent manifests"), not from
> Git's captured working-path patch. The parser, selector, sampler, fitting steps and truthfulness described below are unchanged and
> operate on that comparison text, so statements about repository diff prefixes, `header_unparseable` and raw Git headers describe the
> behavior of sealed history and of the parser's own safety net, not of new delivery.

The five Agent manifests that carry `changeEvidence` used to copy the first 8,192 UTF-16 characters of the captured
tracked diff. That prefix could end inside a hunk or a Unicode scalar and let one large first file hide every later
change. They now share one policy (`ChangeEvidenceManifest`, with `TrackedDiffParser` and `TrackedDiffSelector`, under
`Features/Runs/Policies`) that consumes only the diff and changed paths the reader already captured and bracketed; no
Git call, capture command, fingerprint, changed-path list, claim rule, or replay rule changed.

- **Small valid diffs are unchanged.** An empty diff stays `""` and an absent diff stays `null`. A non-empty diff is
  inlined as the exact string with `diffTruncated: false` and no extra member, as before, only when it fits 8 KiB
  (UTF-8 bytes) and its parse is recognized with every file a supported text or metadata-only block and no binary
  patch. A short non-header string, a malformed hunk, an unsupported header line, or an unparseable path is never
  presented as a complete exact diff: it receives the omission metadata below.
- **Selection.** Otherwise the diff is split into `diff --git` blocks and hunks. A boundary is taken only where the
  format makes it certain: content lines carry a `' '`, `'+'`, `'-'`, or `'\'` prefix, so a line beginning with
  `diff --git ` or `@@ ` is always a header, and a hunk ends exactly where its header's old and new line counts are
  consumed (a trailing `\ No newline at end of file` stays inside its hunk). Complete file headers and complete hunks
  are then chosen in rounds over the files in diff order, at most one further hunk per file per round, so every file
  receives a first hunk before any file receives a second and an oversized hunk never prevents a later smaller hunk of
  the same or another file from being chosen. Output keeps the original file and hunk order, is deterministic, is at
  most 8 KiB of UTF-8, and is assembled only from whole hunks, so no hunk and no Unicode scalar is split.
- **Truthful accounting.** A selected subset sets `diffTruncated: true` and adds `diffSelection`: `complete: false`, a
  fixed notice that the text is not the full diff and not an applyable patch, file counts (`total`, `included`,
  `partial`, `omitted`), hunk counts, and `items` for every file that is not fully present, each with its path, kind
  (`text`, `metadata_only`, `binary`, `unsupported`), selection (`partial`, `omitted`), hunk counts, and a fixed reason
  (`hunk_too_large`, `diff_budget`, `binary`, `unsupported_format`, `malformed_hunk`, `header_unparseable`,
  `manifest_budget`). No reason carries source text, a binary payload, a path, or an exception. Metadata-only changes
  (a mode change, an empty file added or removed) are included whole because their header is the entire change; a
  binary patch's payload is never sent. Quoted paths (octal escapes for non-ASCII bytes) are decoded exactly one way;
  only the default `a/` and `b/` prefixes with the same path on both sides are accepted, so a repository configured
  with `diff.noprefix` or `diff.mnemonicPrefix` reports its files as `header_unparseable` instead of being guessed. A
  text that does not begin with a git file header, or a block whose counts or header lines do not check out, is
  reported as `unsupported_format` or `malformed_hunk` and never truncated by characters.
- **Fitting with untracked previews.** The whole serialized manifest is measured against the 32 KiB ceiling. Optional
  text is reduced together, tracked hunks and untracked previews, through the steps 8 KiB/16 KiB, 8/8, 4/4, 2/2, 1/1,
  0/0; then the tracked `items` are replaced by their counts (`itemsOmitted`); then the untracked section becomes its
  omitted-file count. Authoritative plan, review, and correction inputs, every changed path, the tracked counts, and
  the untracked accounting are never dropped. If those alone exceed the ceiling the builder returns them and the claim
  handler's existing `context_manifest_too_large` refusal applies.
- **Changed-line samples of oversized hunks.** A recognized text hunk whose file header plus hunk exceeds the maximum 8
  KiB selection budget can never be selected whole and used to contribute nothing. Such a hunk now adds a separate
  `diffSelection.samples` section (`TrackedDiffSampler`), never text inside `diff`, which stays composed only of
  complete headers and complete hunks, so `includedHunks` still counts only whole hunks. Eligibility is fixed by the
  hunk itself and the maximum budget, not by which reduced fitting step is in force or what else was selected; a
  hunk that fits alone but lost to other hunks (`diff_budget`) and any binary, metadata-only, malformed, unsupported,
  or unrecognized block never yields a sample. The section states `complete: false`, `patch: false`, a fixed notice
  (an incomplete sample of changed lines, not the full diff, a hunk, or an applyable patch, with shortened lines cut
  and all of it untrusted repository text), its limits, and truthful counts (`eligible`, `sampled`, `unsampled`).
  Each sampled hunk is identified by its file `path` and 1-based `hunk` ordinal in the captured diff, with
  `changedLines.total` and `changedLines.shown`, and lists actual `+`/`-` lines of that parsed hunk as `side`
  (`added`, `removed`), `text` (the line without its prefix or final newline), and `shortened` (with
  `originalBytes` when true); a line followed by `\ No newline at end of file` carries `noNewlineAtEnd: true`, the
  marker itself is never a sample line, and context lines and hunk headers are never shown. Lines alternate removed
  and added (first of each side first, at most 8 per hunk) and are shown in their original order, so both sides
  appear when both exist. Limits: at most 16 sampled hunks, 192 UTF-8 bytes per line (a line is cut only at a
  Unicode-scalar boundary and flagged), and at most 4 KiB for the whole serialized section. Hunk slots go to files
  round-robin (each file's first eligible hunk before any file's second), lines are then granted round by round in the
  same order, and a line that cannot fit the remaining budget even shortened (to at least 16 bytes) closes its hunk,
  so an early file, hunk, or huge line cannot take every opportunity; a hunk left with no line is counted as
  unsampled. Selection is deterministic for identical input. Fitting reduces the section with the other optional
  text (4 KiB, 4, 2, 1, then 0 KiB at the last two steps); at 0 it becomes a counts-only form
  (`omissionReason: manifest_budget`, about 90 bytes) that still reports how many eligible hunks went unsampled, and
  it is absent when no hunk is eligible, so every manifest without an oversized hunk is byte-identical to before. The
  mandatory inputs, the changed paths, and the accounting are unchanged, and a manifest whose mandatory content alone
  exceeds the ceiling is still returned for the handler's refusal; the counts-only form adds those few bytes, so a
  claim within them of the ceiling now gets that existing refusal. Samples use only the already captured and parsed
  diff: no Git call, source-file read, or fingerprint change, and the sealed manifest replays byte for byte.
- **Boundaries.** Repository text stays inside the manifest's existing untrusted-evidence boundary and appears only
  in the sealed manifest and provider input. A sealed manifest replays byte for byte after a restart; the selection is
  never recomputed at dispatch. Not included: raw full-diff or file retrieval, a new Git command, a rename or copy
  format, and any approval or eligibility meaning.

### Attested tracked-change text in Agent manifests

The tracked text of change evidence used to come from the raw patch of `git diff HEAD` as captured for the checkpoint. Git builds
that patch by reopening repository pathnames, so a tracked file with a second name outside the worktree could put the other file's
changed text into every role's manifest, the provider input and the sealed-artifact viewer. Every NEWLY claimed Agent stage now
delivers tracked text only from attested snapshots (see
[ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)); the raw observation, the checkpoint
fingerprint and its serialization and the ordinary captures are unchanged; the authenticated human checkpoint inspection is attested
the same way by [ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md) (see "Attested checkpoint
comparison for human inspection").

- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync` still observes the raw state, but the capture it returns has
  `CompleteDiff` null and `TrackedFiles`: one `GitWorkspaceTrackedFile` per tracked changed path (every path that is not `??`), in
  ordinal path order, each either attested before and after text or one fixed `GitWorkspaceTrackedOmission`. The facts are owned,
  immutable and provider-neutral: `GitWorkspaceEvidenceResult` copies the collection it is given (at construction and on every
  `with` replacement) into a `GitWorkspaceTrackedFiles` snapshot that is neither an array nor a mutable list, so neither the
  caller's collection nor a cast of the returned one can replace text after the proof. They cross the Projects Application boundary
  as data, and the eight claim handlers derive
  `TrackedChangeEvidence` from them (the planning stage carries no change evidence). They are observed inside the same status/diff
  bracket as the instruction context and again after it; any difference, or a source whose bytes did not hold, is
  `RepositoryChangedDuringCapture`.
- **Current side (Windows).** Each current file is read through one held handle (`TrackedFileSourceReader`): before any length or
  byte, the operating system must report that the handle's final path is exactly, case-sensitively, the resolved worktree root plus
  the Git-reported relative path and that it is a regular, non-device, non-reparse file with exactly one link
  (`GetFinalPathNameByHandle`, `GetFileInformationByHandle`); the length is then bounded, the bytes are read and the same facts are
  asked again of the same handle. Git hashes exactly those bytes on standard input (`hash-object --no-filters --stdin`; no repository
  pathname is given to Git), the handle is read again and proven once more, and Git's identity, this host's own blob computation and
  the bytes must agree. A deletion is claimed only for a path that does not open, with its immediate parent directory physically
  proven to be the owned directory before and after the failed open; an access failure is never absence. Another host omits every
  file as `containment_unproven`.
- **Baseline side.** The old text is the blob the captured HEAD records for the literal path: `ls-tree -z -l <commit> -- <paths>`
  (batches of at most 32, literal pathspecs) for mode, type, object identity and size, then `cat-file blob <id>` only after the current
  side is acceptable. Both run with `--no-replace-objects`, `GIT_NO_REPLACE_OBJECTS=1` and `GIT_NO_LAZY_FETCH=1` (so a replacement
  object cannot change the baseline and a missing local object is an omission, never a fetch), the existing hardening and no filter,
  textconv, shell or object write. Only a regular-file blob (`100644`, `100755`) of at most 256 KiB is read, and its content is
  accepted only when the re-encoded bytes equal the recorded size and hash to the object identity (a decoded process string is not
  raw-byte proof by itself); the identity is checked on the first read. Baseline text is cached per object for the second
  observation only when its file was admitted (the facts already hold that text), so the 512 KiB retained-source budget bounds
  everything an observation keeps alive, and a baseline whose file is omitted afterwards is not kept.
- **The host comparison.** The manifest builder composes the patch itself from the two snapshots (`TrackedComparison`): a linear
  common-prefix scan, a linear common-suffix scan that never overlaps it, one complete replacement hunk for the remaining middle
  and at most three unchanged context lines at each edge. A line is its exact text including its terminator, so a different
  terminator or a missing final newline is a difference and is stated with the standard `\ No newline at end of file` marker.
  Headers are `diff --git a/<path> b/<path>` then `---` and `+++` (`/dev/null` for an addition or deletion), with paths in the
  canonical quoted form (bare when no character needs quoting, otherwise C-style quoted with octal escapes for control characters and
  every non-ASCII byte) and `@@ -start,count +start,count @@` numeric ranges; no raw header, function text, mode or index line is copied,
  and the repository's diff prefix, filter, textconv and end-of-line settings have no influence. An empty file added or removed is a
  header with no hunk. The fixed limitation is stated in every manifest that has a tracked changed path as
  `changeEvidence.trackedComparison` (`method` `host_prefix_suffix_v1` and a notice): this is not Git's minimal or filter-normalized
  patch, unchanged lines inside the replaced middle can appear as removed and added, line-ending-only differences remain visible, and
  file modes and renames are not compared (a shorter form of the same notice survives the counts-only reduction).
- **Supported and omitted.** Modified (`" M"`, `"M "`, `"MM"`), added to the index (`"A "`, `"AM"`) and deleted (`" D"`, `"D "`,
  `"MD"`) paths are compared. Fixed omission reasons: `reserved_instruction_file`, `unsupported_status` (a type change, an
  intent-to-add, an added-then-deleted path, a path that is also untracked, a duplicated porcelain entry), `unmerged`,
  `unsupported_mode`, `symbolic_link`, `submodule`, `not_regular_file`, `containment_unproven`, `unreadable`, `too_large`,
  `too_many_lines`, `binary`, `invalid_utf8`, `baseline_unavailable`, `baseline_unverified`, `unencodable_path`, `aggregate_limit`,
  `no_content_difference` (identical bytes, for example a mode-only change), and the two builder-derived reasons `not_attested`
  (no fact for the path) and `attestation_incoherent` (a duplicate, a fact that contradicts the porcelain state, text outside the
  bounds). An omission carries no text and no size.
- **Bounds.** At most 256 KiB and 8192 lines per source (either side), at most 512 KiB of source retained per observation (both sides
  of every attested file, spent in ordinal path order; the next file that would exceed it is `aggregate_limit`), the existing
  128 changed paths, 512 KiB raw capture and 10-second Git timeout; work is linear in those sizes and no source is ever truncated
  into apparently complete text. The 32 KiB manifest ceiling, the 8 KiB tracked selection and the 4 KiB sample section are unchanged.
- **Builders re-derive.** `TrackedChangeEvidence.Derive` is the only way text reaches `ChangeEvidenceManifest`: it accounts for every
  tracked changed path exactly once, ordinal, from the capture's own changed paths and the reader's facts. A reader that returned no
  facts delivers no tracked text and every tracked path is `not_attested`; a fact for a path that is not a tracked change is ignored;
  the porcelain state decides which sides may be absent (`Classify`, shared with the reader); bounds and the retained budget are
  checked again; the two reserved root names are always omitted. There is no raw-diff input to the builders, and a legacy raw patch
  can never be admitted by missing or incoherent facts.
- **Manifest accounting.** The composed text is parsed by the existing `TrackedDiffParser`; omitted paths are merged into the same
  file list as omitted files, so `diffSelection.files`, `hunks`, `items` (kind `unsupported`, the fixed reason), the whole-hunk
  selection and the incomplete samples work unchanged. A comparison with no omission that fits 8 KiB is inlined exactly
  (`diffTruncated: false`, no `diffSelection`); otherwise `diffTruncated: true`, `diffSelection.complete: false` and, whenever any path
  is omitted, `diffSelection.omissionReasons` (reason to count, ordinal), which is a few fixed bytes and so survives every reduction
  step even when the per-file items are replaced by their counts. A manifest with no tracked path has no `trackedComparison` member and
  keeps the historical empty-diff shape.
- **Boundaries and replay.** Source text lives only in the sealed manifest, the provider input and the existing authenticated
  sealed-artifact viewer, unredacted, and is never stored in SQLite, a log or an error. Dispatch and restart read the sealed artifact;
  nothing recaptures, rebuilds, filters or reseals it, and a manifest sealed before this contract (a raw patch under `diff`, no
  `trackedComparison`) replays its exact bytes. Architecture tests keep `CompleteDiff` out of the Runs feature (and, since ADR-0027, out of every production reader), allow the
  derivation only in the claim handlers and keep the attestation types out of supervisors, endpoints and adapters.
- **Limits.** The raw observation behind the fingerprint still reads named paths and is not claimed to
  have this containment guarantee; committed blob content is not confidential by inference; topology can change after observation;
  only Windows has the physical proof; the process doubles used in tests do not establish real-provider reliability. Not included: a
  minimal-diff optimizer, alias enumeration, a deeper parent chain for deletions, a generic file reader, and any provider flag,
  permission, budget, grant, scheduling or lifecycle change.

### Attested checkpoint comparison for human inspection

The protected checkpoint-diff route a human uses to inspect a checkpoint used to return the raw patch of `git diff HEAD` as
`CompleteDiff`, which Git builds by reopening repository pathnames: a tracked file with a second name outside the owned worktree put
the other file's changed text into the response and the rendered page. Since
[ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md) the same attestation as
ADR-0024 backs that route, with the Agent-only concerns left out.

- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForCheckpointInspectionAsync` is the only capture the query asks for. It is the same
  coherent bracket and fingerprint, returns `TrackedFiles` (the immutable facts of every tracked changed path) and returns no raw
  patch, no instruction context and no untracked previews. The two root instruction names are not reserved for it, and no
  instruction file is read to inspect a checkpoint. A reader without attestation support yields the plain capture with the patch
  removed and no facts, so every tracked path is a `not_attested` omission.
- **Shared policy, separate delivery.** `AttestedTrackedComparison` and `TrackedComparison` live in Projects and are the one admission
  and comparison used by both consumers; every call states a `TrackedSourcePurpose`. `AgentDelivery` reserves `AGENTS.md` and
  `CLAUDE.md` to the controlled instruction section (unchanged); `HumanInspection` compares a physically proven tracked root
  instruction file as inert displayed text that imports nothing and grants no authority. Agent manifest fitting stays in Runs and
  the inspection response fitting stays in the query.
- **Response.** `comparisonText`, `isComplete`, `trackedPathCount`, `comparedPathCount`, the fixed `limitation` and
  `omissions` (path and fixed reason, ordinal): every tracked changed path is compared or omitted exactly once, untracked files stay
  in the changed-files route, and `isComplete` means only that no tracked path was omitted. The text is at most 512 KiB of UTF-8;
  whole file blocks are fitted in ordinal order and an over-budget block is omitted as `comparison_limit`, never cut. The existing
  128 changed paths, 256 KiB and 8192 lines per source and 512 KiB retained per observation still apply.
- **Interface.** The inspection hook keeps project and checkpoint ownership and overlapping-read ordering; the panel states a
  partial or all-omitted result, lists the omitted paths and the limitation as plain text, and shows "No tracked diff." only for a
  complete capture with no tracked change.
- **Limits.** The raw observation behind the fingerprint still reads named paths; topology can change after observation; only Windows
  has the physical proof; the comparison is not Git's minimal or filter-normalized patch and does not compare modes or renames. Not
  included: the raw patch, alias enumeration, a minimal-diff optimizer, paging and any change to fingerprints, checkpoints, Agent
  manifests or replay.

### Complete-set Human approval for local delivery

The explicit local commit ([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md))
needs a Human approval whose verification membership equals the CodeReviewer approval's and the complete set of enabled recipes.
[ADR-0030](../decisions/0030-approve-the-complete-verification-set-as-one-human-decision.md) lets the existing manual review operation
carry that membership. It is an authenticated Human decision about the checkpoint and its verification evidence only: it is not
provider output, never synthesizes an Agent approval, claims no attempt or budget and starts no stage.

- **Request.** The optional `verificationExecutionIds` array (at most 32 unique nonempty UUIDs; an 8 KiB request body) is the Human
  execution-set form. The scalar `verificationExecutionId` is the unchanged legacy form; two non-null forms are refused as ambiguous,
  never merged, deduplicated or truncated, a non-Human reviewer cannot use the set form, a decided review needs at least one member and
  `Pending` carries none.
- **Complete approval.** `Approved` requires exactly the latest, coherent clean `Passed` execution of every currently enabled recipe
  for the current checkpoint and fingerprint, with a matching current command snapshot; any subset, extra, disabled, repeated or stale
  member is refused (`reviews.approval_requires_complete_verification_set`). The members are saved with
  the review in one transaction after fresh untracked re-reads under the write lock; no Git or provider work happens inside it. They have
  no stored sequence: the host derives the canonical `CommandNumber` order (then execution number) when it presents them.
- **Bundle.** `GET .../checkpoints/{checkpointId}/approval-evidence` returns the source identities and fingerprint and, per enabled
  recipe in command order, its command and execution identities, recipe label and execution number, or a fixed refusal; it is
  derived from the recipes and their latest executions, not from the bounded execution history, and it authorizes nothing.
- **Unchanged.** Historical rows and sealed manifests, FutureAgent provenance, the mixed-Human-decision gate, the local commit's
  reservation, digest, seam and recovery rules and every other authority decision. A Pending, Changes-requested or Escalated Human
  decision still blocks local delivery.

### Recorded local-delivery receipt

A completed explicit local commit ([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md))
has a protected, read-only historical receipt ([ADR-0032](../decisions/0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md)),
`GET /api/runs/{runId}/local-delivery-receipt`, answering `NotRecorded`, `NotCompleted`, `Unavailable` or `Available` (only the last
carries the version-1 receipt). Every fact is resolved through the identifier the operation recorded: the pinned execution report, the
CodeReviewer attempt (launched against exactly that report, with its single provider-observed approval message and no finding), the
Agent and selected Human checkpoint reviews, and the operation's ordered Verification authority members. Those members must be 1 to
32 unique, contiguous pairs equal to the CodeReviewer's ordered evidence claim and, as sets, to both reviews' evidence; each execution
must belong to the recorded project, workspace, checkpoint and command, be `Passed` with a clean exit, and match its recorded member
digest recomputed from its own immutable command snapshot (the admission digest format, shared and unchanged); the selected Human
decision must match its recorded digest. The pinned run must record the same completion (lifecycle and stage `Completed`, last advance
equal to the operation's completion time), the recorded counts and numbers must be in bounds, the approval message must carry the
attempt's own provider, and both reviews' stored copies of each execution (number, status, outcome, exit code, fingerprint) must equal
the pinned execution. Any gap makes the whole receipt `Unavailable`. The query is read-only (no filesystem, Git,
process, provider, lease, event or write) and never substitutes a later report, review, checkpoint, recipe or execution, so reruns and
recipe edits after delivery leave an intact receipt unchanged. It does not infer the implemented plan from report replies and says
nothing about current workspace state or remote publication.

### Project instruction context in Agent manifests

Every manifest used to carry the same three fixed DevalCopilot documentation names as "instruction references" whatever the
project, without reading them. Every NEW Agent claim now seals the project's own root instructions instead, through one
additive section (see [ADR-0021](../decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md)). The fixed
references are removed from new manifests and not replaced; an old sealed manifest replays its own bytes, references included.

- **Which files.** Exactly the root `AGENTS.md` then `CLAUDE.md` of the claim's own tool-owned worktree. No import, Markdown
  reference, nested or parent file, link, home path or setting is followed, and a reference to a sibling checkout grants no
  read. Tracked (clean or modified) and non-ignored untracked files are eligible; ignored-only files and assume-unchanged or
  skip-worktree entries are omitted because no checkpoint evidence vouches for them.
- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync` is called only by the eight claim handlers (planning,
  critical review, resolution, implementation, code review, review correction, diagnosis-origin correction, verification
  diagnosis), once per claim, beside the untracked previews where the stage has them; every other capture is unchanged and
  returns no instruction context. Git runs only fixed hardened builtin subcommands with literal pathspecs (`ls-files -v`, an
  ignored-others listing). On Windows a file is read only through an open handle whose final path is exactly the resolved
  worktree root plus the fixed name, which is a regular, single-name, non-reparse file within the 8 KiB bound; nothing is hashed
  or sent for a file that fails that proof. The independent raw identity is Git's `hash-object --no-filters --stdin` of exactly
  those bounded bytes (no repository pathname is ever given to Git for it), the same held handle is read again, and Git's
  identity, the host's own blob computation and (for an untracked file) the checkpoint fingerprint's identity must agree.
  Presence, classification, identity and text are observed inside the capture bracket and again after it; any difference or
  identity mismatch discards all text and fails as `RepositoryChangedDuringCapture`. Another host omits both files as
  `containment_unproven`. The raw observation behind the fingerprint (status, the full diff and the per-path `hash-object` of
  untracked files) is the unchanged, pre-existing one and is not claimed to have this containment guarantee.
- **Delivery projection.** The two root names are reserved to this section. In a NEW Agent manifest their text never appears as a
  generic untracked preview or as tracked diff, hunk or sample content, whatever the section says (Complete, any Omitted reason,
  Absent) and for a safe, unsafe, clean, dirty or untracked source. The Agent-context capture does not preview a reserved
  untracked path and returns it as omitted (`reserved_instruction_file`). A reserved TRACKED path is, since ADR-0024, an explicit
  `reserved_instruction_file` omission of the attested tracked evidence (it is never even read for it), counted in
  `diffSelection.omissionReasons` and listed in `diffSelection.items` beside the delivered siblings, whatever a reader's facts
  claim and in every builder; the earlier cut of the raw patch at `diff --git` boundaries, and its whole-diff withholding
  (`reserved_instruction_diff_withheld`) under an undecodable header format, applied only to the raw patch and are no longer part of
  new delivery. Changed paths stay; a changed reserved path is named in `diffSelection.reservedInstructionFiles` at every reduction
  step and the diff is then never `diffTruncated: false` or `complete`; unrelated evidence is delivered as before. Names match
  ignoring case; only the root path is reserved. Ordinary captures, the fingerprint and historical manifests are unchanged.
- **Section.** `projectInstructionContextBoundary` (fixed text) and `projectInstructionContext` (`version` 1, `notice`,
  `sourceGitWorkspaceId`, `sourceGitCheckpointId`, `sourceCheckpointFingerprintSha256`, `sources`). Each of the two `sources`
  has `fileName`, `status` (`Complete`, `Absent`, `Omitted`), `reason`, `byteLength`, `sha256` and `text`; text only for
  `Complete`, length and SHA-256 only when established (a file omitted for budget keeps the ones it has). Reasons: `ignored`,
  `index_flag`, `unmerged`, `not_regular_file`, `containment_unproven`, `unreadable`, `content_identity_mismatch`, `too_large`,
  `binary`, `invalid_utf8`, `section_budget`, `manifest_budget`, `not_captured`. A capture that returned no context is
  `not_captured`, never `Absent`. Placement: directly after `untrustedEvidenceBoundary` where the form has one (so a
  format-repair notice stays in the trusted part), and where the planning form carried the old references.
- **Bounds.** At most 8 KiB of raw UTF-8 per source, valid and without NUL; the complete decoded text is kept byte for byte
  (line endings and a byte order mark included). The serialized section is at most 12 KiB, fitted as whole entries in fixed
  order (`section_budget`). In the 32 KiB whole-manifest fitting the section is reduced last: only after change evidence (and
  the diagnosis failure excerpts) are reduced as far as they can be are whole texts omitted, latest first
  (`manifest_budget`). No plan, challenge, finding, schema or authorization is shortened, and a manifest whose mandatory
  content cannot fit keeps the existing `context_manifest_too_large` refusal with its orphan cleanup and without consuming a
  claim, reservation or grant.
- **Authority.** The fixed boundary states the section is untrusted repository text that may inform the response through
  compatible conventions only; it can never override or extend the authorized plan, the role, the output schema, permissions,
  command restrictions or any human decision and grants no tool, network, approval, authorization, retry, budget, provider
  switch or publication. Provider adapters, arguments, safe-mode and schemas are unchanged.
- **Replay and confidentiality.** Dispatch and restart read the sealed artifact; nothing recaptures or rebuilds it, and a
  manifest sealed before this contract is never rejected for lacking the section. The text is carried by the sealed manifest, the
  provider input and the existing authenticated sealed-artifact viewer, which already serves manifest content in the browser and
  now shows this section like any other manifest content (no new endpoint or viewer); it is never stored in SQLite, a log or an
  error, and is not redacted. Containment is proven at the read, and the file can still change after observation.
- **Not included.** Imports, a directory walk, a third file, provider-side discovery, redaction of repository content, a
  rewrite of historical manifests, or any change to authority, budgets or permissions.

## Token efficiency

- Build a context manifest for each attempt and include only inputs required by
  the current role and decision.
- Retrieve project documentation and source progressively instead of attaching
  the entire repository or documentation set.
- Reuse one durable summary of stable facts and link it to primary evidence;
  do not ask each agent to restate the same background.
- Send changed hunks, unresolved findings, and relevant neighboring code before
  considering a complete diff or file.
- Never present a truncated or omitted file as complete evidence: a preview states its own completeness, and
  every untracked path of a capture is accounted for (see "Bounded untracked-file previews in Agent manifests").
- Prefer deterministic tools for discovery, validation, counting, formatting,
  and status checks instead of spending model tokens inferring their results.
- Do not repeat an agent attempt unless state, instructions, evidence, or the
  expected response changed materially.
- Track input, output, and cached token usage when the provider exposes it.
  This is implemented as `AgentTokenUsageEvidence` for both Claude- and
  Codex-produced attempts, each against its own proven, versioned parsing
  contract (see "Provider token-usage contracts"); a provider without a
  proven contract, or a report in an unrecognized shape, still records as
  unknown rather than an inferred value.
- Stop and escalate when a stage budget is exhausted rather than silently
  borrowing unlimited tokens from the run.
- Select model capability and reasoning depth according to task risk, not as a
  universal maximum setting.

## Untrusted content

Repository files (including the project's own root `AGENTS.md` and `CLAUDE.md`, which are delivered only as the
bounded, fixed-boundary section described in "Project instruction context in Agent manifests"), comments, issue text,
test output, CI logs, generated files, and prior agent responses are quoted or delimited as untrusted evidence. They
cannot grant permissions, alter the protocol, request credentials, change
budgets, or bypass project instructions.

## Protocol evolution

The envelope is versioned. Additive optional fields may remain compatible.
Changing message meaning, authority, required fields, or resolution behavior
requires a protocol version and migration plan.
