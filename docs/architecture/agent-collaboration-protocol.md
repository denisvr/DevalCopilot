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

Repository source evidence (bounded untracked-file previews, bounded
tracked-hunk evidence and attested tracked-change text in Agent manifests, the
attested checkpoint comparison for human inspection and project instruction
context in Agent manifests) is in the
[source evidence](agent-collaboration-source-evidence.md) companion; their
original headings below forward there.

The complete-set Human approval for local delivery and the recorded
local-delivery receipt of the explicit local commit are in the
[local delivery](agent-collaboration-local-delivery.md) companion; their
original headings below forward there. Local delivery is not remote
publication.

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

Moved to [Bounded untracked-file previews in Agent manifests](agent-collaboration-source-evidence.md#bounded-untracked-file-previews-in-agent-manifests)
in the source evidence companion.

### Bounded tracked-hunk evidence in Agent manifests

Moved to [Bounded tracked-hunk evidence in Agent manifests](agent-collaboration-source-evidence.md#bounded-tracked-hunk-evidence-in-agent-manifests)
in the source evidence companion.

### Attested tracked-change text in Agent manifests

Moved to [Attested tracked-change text in Agent manifests](agent-collaboration-source-evidence.md#attested-tracked-change-text-in-agent-manifests)
in the source evidence companion.

### Attested checkpoint comparison for human inspection

Moved to [Attested checkpoint comparison for human inspection](agent-collaboration-source-evidence.md#attested-checkpoint-comparison-for-human-inspection)
in the source evidence companion.

### Complete-set Human approval for local delivery

Moved to [Complete-set Human approval for local delivery](agent-collaboration-local-delivery.md#complete-set-human-approval-for-local-delivery)
in the local delivery companion.

### Recorded local-delivery receipt

Moved to [Recorded local-delivery receipt](agent-collaboration-local-delivery.md#recorded-local-delivery-receipt)
in the local delivery companion.

### Project instruction context in Agent manifests

Moved to [Project instruction context in Agent manifests](agent-collaboration-source-evidence.md#project-instruction-context-in-agent-manifests)
in the source evidence companion.

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
