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

The installed `@anthropic-ai/claude-code` package (version `2.1.276` at the time this contract
was evidenced) ships a native `claude.exe` as its declared `bin` entry — a `DirectExecutable`
launch target, never a Node-hosted script. Every fact below was proven from authoritative local
evidence only (`claude --version`, `claude --help`, the package's own `package.json` and bundled
`cli-wrapper.cjs`, and embedded literal strings in the compiled binary) — never by invoking a real
authenticated model to discover a flag. A Claude critical-review attempt invokes this exact,
fixed argument list, with the context manifest delivered over stdin and no argument ever
containing prompt text, repository content, or a credential:

```
--print
--input-format text
--output-format json
--json-schema <inline JSON Schema for the Acceptance/Challenge union>
--safe-mode
--restricted
--disable-slash-commands
--no-chrome
--permission-prompts none
--prompt-suggestions false
--tools ""
--strict-mcp-config
--permission-mode plan
--no-session-persistence
--session-id <fresh random GUID>
--max-turns 1
```

- **Non-interactive**: `--print` runs one bounded turn and exits; it is never given a TTY.
- **Stdin, never argv**: `--input-format text` (the default) reads the prompt/context from
  stdin; the CLI's own embedded strings confirm a bounded stdin-wait and UTF-8 stdin handling.
- **Structured output**: `--output-format json` wraps the result in one JSON envelope on stdout
  (fields observed in the binary's own string table: `is_error`, `result`, `session_id`,
  `subtype`, `num_turns`, `total_cost_usd`, `duration_ms`); `--json-schema` constrains the
  provider's own `result` field to this slice's fixed Acceptance/Challenge schema. Unlike Codex,
  Claude's print mode has no file-based final-response flag — the adapter extracts `result` from
  this envelope and writes it to the same sealed final-response artifact Codex's CLI writes
  directly, so every later parsing/recording step is identical between both providers.
- **Fully isolated from local customizations and indirect execution**: `--tools ""` and
  `--strict-mcp-config` alone disable built-in tools and non-declared MCP servers, but the
  installed CLI's own `--help` proves neither one disables hooks, plugins, skills, CLAUDE.md
  auto-discovery, Claude-in-Chrome, or user/project/local settings files — a `SessionStart` hook
  or a project-local setting could otherwise still cause a command to run with no built-in tool
  ever invoked. `--safe-mode` disables CLAUDE.md, skills, plugins, hooks, MCP servers, custom
  commands/agents, output styles, workflows, themes, and keybindings outright; `--restricted`
  independently removes command/code-running tools and WebFetch and ignores user/project/local
  settings files (a second, independent path to the same guarantee); `--disable-slash-commands`
  disables all skills; `--no-chrome` disables the Claude-in-Chrome integration entirely;
  `--permission-prompts none` denies anything that would otherwise prompt, independent of the
  permission mode; and `--permission-mode plan` is the strongest available permission mode — it
  never executes an action, only plans one, even if every tool were somehow still reachable.
  `--prompt-suggestions false` suppresses the provider's own predicted-next-prompt side output.
  `--bare` is deliberately never passed: the installed version's own `--help` states it changes
  the authentication contract itself (Anthropic auth becomes strictly
  `ANTHROPIC_API_KEY`/`apiKeyHelper`; OAuth and keychain are never read), which would silently
  break this slice's reliance on existing local CLI authentication.
- **No session resume**: `--no-session-persistence` plus a fresh, per-attempt random
  `--session-id` guarantee this invocation can never continue or fork an unrelated prior session;
  `--continue`, `--resume`, and `--fork-session` are never passed.
- **Bounded turns**: `--max-turns 1` bounds the agentic loop to a single turn; `--max-turns` was
  confirmed present in the compiled binary's embedded string table (alongside a corroborating
  `error_max_turns` result subtype) even though it is not listed in the CLI's own abbreviated
  `--help` output.
- **Cancellation and process-tree compatibility**: proven generically by the same process-tree
  termination infrastructure already established for Codex — a `DirectExecutable` launch target
  is handled identically regardless of which provider resolved to it.

The stdout envelope itself is also validated, not merely parsed: `is_error` must be present and a
genuine JSON boolean (only a literal `false` is ever treated as success — a missing or
non-boolean value fails the whole envelope closed), `result` must be present, and a present
`session_id` must be a well-shaped bounded string (a genuine JSON `null` is tolerated exactly like
an absent field, since `--no-session-persistence` means the provider may legitimately have no
session to report; anything else malformed rejects the whole envelope, never just that field, on
the reasoning that a provider which cannot even shape its own bookkeeping field correctly is not
a source whose `result` should be trusted either).

One material limitation, honestly disclosed rather than assumed away: the exact shape of the
`result` field when `--json-schema` is supplied (a JSON string containing schema-conformant text,
versus the schema-conformant JSON value directly) was inferred from embedded evidence and general
knowledge of the CLI's public contract, not observed from a real authenticated invocation — the
adapter therefore normalizes either shape defensively, and the downstream parser fails closed
(`InvalidStructuredOutput`, never a false Acceptance) if that inference is ever wrong.

### Claude Code implementation CLI safety contract

The Claude implementation attempt invokes the same installed `claude.exe` `DirectExecutable`
launch target as the critical-review attempt above, using a dedicated argument list — never the
critical-review adapter reused by changing flags, since this role's tool allowlist and permission
mode differ in kind (mutating repository edits) from every other Claude usage in this protocol.
The context manifest is delivered over stdin exactly as above:

```
--print
--input-format text
--output-format json
--json-schema <inline JSON Schema for the implementation report>
--safe-mode
--restricted
--disable-slash-commands
--no-chrome
--permission-prompts none
--prompt-suggestions false
--tools "Read,Edit,Write,Glob,Grep"
--strict-mcp-config
--permission-mode acceptEdits
--no-session-persistence
--session-id <fresh random GUID>
```

Every hardening flag shared with the critical-review contract above (`--safe-mode`,
`--restricted`, `--disable-slash-commands`, `--no-chrome`, `--permission-prompts none`,
`--prompt-suggestions false`, `--strict-mcp-config`, `--no-session-persistence`, and the same
never-`--bare`/`--continue`/`--resume`/`--fork-session`/`--dangerously-skip-permissions` set)
applies identically. Two differences are deliberate:

- **An explicit tool allowlist, not the empty one**: `--tools "Read,Edit,Write,Glob,Grep"` grants
  only file-reading and file-editing tools. The installed CLI's own `--help` states this option
  supplies "the list of available tools" — never "additional" tools, the wording `--add-dir` uses
  for its own genuinely additive option — and its three parallel forms (`""` disables all tools,
  `"default"` restores every tool, an explicit list names exactly the available set) are only
  coherent under replacement semantics: `""` could not mean "no tools" if a passed list were
  merely added to an existing default set. The compiled binary's own embedded implementation
  corroborates this: the CLI computes a `baseToolsCli`/`getAllBaseTools` tool universe distinct
  from the separate `allowedToolsCli`/`disallowedToolsCli` permission-grant layer used by the
  unrelated `--allowedTools`/`--disallowedTools` flags this adapter never passes, and applies it
  through a coordinator tool filter (`applyCoordinatorToolFilter`). Every name in the allowlist —
  and every name deliberately absent from it (Bash, WebFetch, WebSearch, NotebookEdit, Task/Agent,
  ExitPlanMode) — is confirmed present as a literal, capitalized tool identifier in the installed
  CLI binary: this attempt never runs a shell, script, or arbitrary process, never browses the
  web, and never spawns a sub-agent. `--restricted`'s own `--help` text independently confirms
  Bash/PowerShell/REPL/code-running tools and WebFetch are removed "unless `--tools` names them"
  (this allowlist never does) and that it "confines the file tools to the working directories". No
  Git command, verification command, commit, push, package install, or network operation is ever
  reachable through this allowlist.
- **`--permission-mode acceptEdits`, not `plan`**: the strongest available mode still compatible
  with real file mutation, since `plan` never edits anything and `bypassPermissions` is explicitly
  refused by `--restricted`. Beyond the enum name and the corroborating embedded string
  "auto-accept edits", the compiled binary contains an actual `realpathSync`-backed, cached
  path-resolution utility (`realpathSync(path) { val = lazyFs().realpathSync(path); }`) together
  with a `blockReadsOutsideWorkingDirectories` permission concept and a `checkPathSafetyForAutoEdit`
  function named specifically for this mode's own auto-applied-edit path — real implementation
  evidence, not merely a flag description, that edits accepted under this mode are checked against
  a working-directory boundary using symlink/junction-resolving real-path comparison, consistent
  with `--restricted`'s own textual confinement guarantee above. The one point that remains a
  disclosed inference rather than a directly observed behavior is the mode's precise interactive
  consequence (that it truly never blocks on a prompt for an in-boundary edit) — the CLI ships no
  bundled documentation asserting this in so many words, and confirming it further would require
  an authenticated invocation this project's evidence discipline forbids. Mirrors the
  `result`-field-shape limitation the critical-review contract above already discloses in kind.
- **No `--max-turns` by default; an optional owner request adds exactly one**: implementation is
  inherently multi-step (read, edit, re-read), unlike a single-turn critical review, so no turn
  limit is passed unless the attempt's immutable snapshot requested one (see
  ["Optional Claude agentic-turn limit for mutation attempts"](#optional-claude-agentic-turn-limit-for-mutation-attempts));
  otherwise the bound remains the process-level timeout, cancellation, and process-tree
  termination already established for every other provider adapter.

Because each initial Implementer invocation can genuinely mutate the worktree, the
orchestrator always independently re-reads fresh Git evidence after this adapter returns —
regardless of whether the process succeeded, failed, or threw — and never assumes a failed or
cancelled process left the worktree untouched.

### Deferred Gemini CLI execution boundary

Gemini CLI 0.60.0 is not currently selectable or claimable. Its system settings
are administrative configuration that require trusted administrator ownership
and permissions. DevalCopilot must never manufacture that policy in
user-owned attempt scratch state. A future slice must define trusted
installation and policy provisioning, ACL validation, exact semantic policy
validation, readiness reporting, and a real no-model contract test before any
Gemini execution can be enabled.

Gemini authentication material must not be copied into scratch or persistence.
No automatic fallback or parallel execution is authorized. Durable assignment
facts do not authorize automatic verification or publication.

### Review correction

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
host-runtime-preflight projection above already reports them, and no doc,
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

### Bounded human guidance for one authorized review correction

The one explicit human authorization of an additional review correction may carry short **guidance**
clarifying the already-recorded findings. The bodyless `POST …/review-correction-escalations/{escalationId}/authorize`
operation is unchanged and remains the "no guidance" authorization. A separate protected operation,
`POST …/review-correction-escalations/{escalationId}/authorize-with-guidance` with body `{ "guidance": "…" }`,
records guidance. It reuses `AuthorizeReviewCorrectionCommand`, whose optional `Guidance` is `null` for the bodyless
request. No generic instruction system, provider flag, fallback, automatic retry, or session resume is added.

- **Representation.** The existing `HumanInstruction` structured content already has two required bounded string
  fields, `instruction` and `rationale`. Guidance is carried in `rationale`; `instruction` stays the fixed
  authorization text, so no ledger schema, ADR, or migration changes. A bodyless authorization (and every one
  recorded before this feature) has the fixed default rationale, and `ReviewCorrectionGuidance` treats exactly that
  value as "no guidance", so that exact text is reserved and rejected as guidance (a guided and a
  bodyless authorization can never share stored bytes). The recorded message summary is unchanged and never contains the guidance, and the run
  event payload carries only message id, type, and provenance.
- **Bounds and normalization.** `ReviewCorrectionGuidance.Normalize` is deterministic and both the validator and the handler call it, always before persistence: Unicode
  form C, line endings to `\n`, surrounding whitespace trimmed. The result must be non-blank, at most 600
  characters, free of control characters other than `\n`, and pass the ledger's own unsafe-text screen
  (`CollaborationMessageContentPolicy.IsSafeSummary`, which rejects a few marker words and Windows path forms —
  including ordinary words such as "environment"), and differ from the reserved default rationale. That screen is a best-effort filter and does not guarantee that a
  secret or sensitive value is absent; the cockpit says so. The request body is capped at 8 KiB. A rejected request
  is a 400 (`review_correction_authorizations.guidance_invalid`, from `AuthorizeReviewCorrectionCommandValidator`,
  with the handler repeating the check) with a fixed message that never echoes the input.
- **Retries and concurrency.** An escalation has at most one available authorization (the filtered unique index
  `ix_review_correction_authorizations_one_available`). A retry with identical normalized guidance is idempotent and
  returns the same authorization and message. A request whose guidance differs from the existing unconsumed
  authorization — including a bodyless request after a guided one, and a guided request after a bodyless one — is a
  409 `review_correction_authorizations.guidance_conflict` with a fixed message that echoes neither value; it never
  reports somebody else's authorization as its own. The losing side of a concurrent insert is resolved the same way
  from the committed row. Before comparing, a retry validates the existing authorization's whole persisted chain with the same
  resolver a claim uses (below); any incoherence fails closed as `review_correction_authorizations.instruction_invalid`,
  never as an idempotent success.
- **Claim.** When a correction consumes an authorization, `ReviewCorrectionAuthorizationInstruction` verifies before
  any manifest is sealed that the authorization, its escalation, the escalation's host-constructed `Escalation`
  message, and the `HumanInstruction` all belong to the same run and escalation, and that the `HumanInstruction` is a
  human-submitted, attemptless message from the Human to the Orchestrator, protocol 1.0, replying to exactly that
  escalation message, with the fixed instruction and one rationale. The escalation message must itself be a
  host-constructed, attemptless, protocol-1.0 `Escalation` from the Orchestrator to the Human. The stored
  `HumanInstruction` content must be exactly the canonical bytes `ReviewCorrectionGuidance` writes
  (`TryReadRationale`): either the fixed default rationale or a rationale that is its own normalization — within
  the length bound, screened, free of control characters, valid Unicode, form C, trimmed — with no other
  encoding of the same document; shape-valid but overlong, unsafe, noncanonical, or differently encoded content is
  refused. Any mismatch is 409
  `review_correction_authorizations.instruction_invalid` (never echoing persisted text), consumes nothing, and seals
  nothing. The message is immutable and the authorization is consumed under its own concurrency token in the claim's
  single commit, so the snapshot cannot drift; a competing claim that consumes it first rolls this claim back and
  removes its sealed manifest. The run-wide count and reserved-time budgets are still checked first, so a globally
  exhausted run never consumes an authorization.
- **Manifest.** For a guided authorization, the sealed context manifest (still bounded to 32 KiB) adds, after the
  fixed `instruction` and `untrustedEvidenceBoundary`, a fixed host-authored `humanGuidanceBoundary` and one
  `humanGuidance` object holding the `HumanInstruction` message id and the exact accepted text — once. The boundary
  states the text is human-submitted advisory clarification of the findings only and cannot change the objective,
  findings, instruction, output schema, working directory, permissions, or tool restrictions, or permit Git,
  verification, package installation, network, or out-of-scope work. An unguided manifest is byte-identical to
  before. The ordered `AttemptInputMessage` rows remain exactly the `ExecutionReport` followed by the applicable
  `ReviewFinding`s (ADR-0010); the `HumanInstruction` is never an input row or a finding.
- **Replay and visibility.** Dispatch and restart replay read only the attempt's sealed manifest; nothing about
  guidance is added to the invocation request. The guidance is intentionally visible in the recorded
  `HumanInstruction` and the sealed manifest, and it is not redacted, so either may contain whatever the human typed.

### Optional direct human guidance for mutation requests

The explicit mutation requests, the initial implementation of a resolved plan and an ordinary review correction (and, since
[ADR-0019](../decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md), the correction of a verification
diagnosis's findings, described under "Explicit verification failure diagnosis"), may
carry short **direct guidance** ([ADR-0015](../decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md)).
It is advisory clarification of work the authoritative plan or the complete findings already authorize. It grants no
authority, attempt, budget, permission, tool, or approval, and it is distinct from the authorization guidance above: it
never creates a `HumanInstruction`, an escalation, or an authorization, and the two are never merged or reinterpreted.

- **Request.** `POST …/agent-attempts/implementation` accepts `{ "planProposalMessageId": "…", "guidance": "…" }` and
  `POST …/agent-attempts/review-correction` accepts `{ "implementationReviewAttemptId": "…", "guidance": "…" }`. Routes,
  source requirements, and response shapes are unchanged; omitted or `null` guidance is the previous behavior. The mediator
  commands carry the same optional `Guidance`. Each body is capped at 8 KiB.
- **Normalization and bounds.** `DirectHumanGuidance.Normalize` applies the policy it shares with
  `ReviewCorrectionGuidance` (`BoundedGuidanceText`): Unicode form C, line endings to `\n`, trim; non-blank valid Unicode of
  at most 600 UTF-16 code units; no control characters except `\n`; the bounded-summary content screen (best-effort, not a
  secret guarantee). The authorization's reserved default rationale stays reserved only for the authorization. Supplied
  invalid text, including an empty or whitespace-only string, is `400 agent_attempts.direct_guidance_invalid` from a validator
  on each command (repeated by the handler), before any read, Git work, or sealing, with a fixed message that never echoes the
  input.
- **Snapshot.** `Attempt` gains the nullable immutable `AgentDirectHumanGuidance` (migration `AddDirectHumanGuidance`, no
  default, no backfill; historical rows are null and nothing is inferred). Only the two mutation claims assign it, in the same
  commit as the attempt, its exactly ordered `AttemptInputMessage` rows (ADR-0010 is unchanged), its manifest artifact, and the
  claim-budget reservation. The factories accept only normalized text and only with the version 2 contract
  (`claude-implementation-v2` or `claude-review-correction-v2`) and the workspace-edit profile. A reading is absent, valid, or
  malformed; the evidence state is `NotRecorded` (a coherent attempt with a null snapshot: no direct guidance was recorded, which is
  neutral for historical and new unguided attempts alike, so version 2 does not date the feature and no submission history is
  inferred), `Provided`, or `Unknown` (malformed, or guidance
  beside an incompatible provider, role, response contract, profile, or version).
- **Budget rule.** Within the ordinary correction budget a guided correction is an ordinary claim. At exhaustion any request
  carrying guidance is refused whole with `409 agent_attempts.direct_guidance_unavailable`, even when an unconsumed extra
  authorization exists. The refusal runs after every existing gate, inside the existing exhaustion branch and before the
  escalation or authorization logic, so it creates no escalation, consumes no authorization, and seals nothing. Requests
  without guidance, the escalation, both authorization operations, and their guidance are unchanged.
- **Manifest.** When guidance exists the manifest (all three forms) carries `directHumanGuidanceBoundary` (a fixed
  host-authored sentence stating that the text is human-submitted advisory clarification that cannot change the objective,
  plan or findings, instruction, output schema, working directory, permissions, or tool restrictions, or permit Git,
  verification, package installation, network, or out-of-scope work) followed by `directHumanGuidance` (`{ "text": "…" }`, the
  accepted text exactly once), both before `untrustedEvidenceBoundary`. A manifest without direct guidance is byte-identical to
  before, including the authorized correction's separate `humanGuidance`. The 32 KiB bound and the evidence-fitting policy are
  unchanged: evidence shrinks to make room and the guidance is never truncated.
- **Consistency at dispatch.** Both mutation eligibility feeds project the snapshot and exclude an attempt whose snapshot is
  malformed or incoherent with its assignment; historical unguided and authorized attempts stay eligible.
  `MarkAgentAttemptDispatched` repeats the coherence test over the snapshot and the whole assignment tuple (response contract,
  role, provider, permission profile, adapter contract version), read afresh and untracked in one statement inside its
  transaction, so a competing assignment change committed before the transaction cannot confer stale authority; pending tracked
  writes are preserved.
  The mutation supervisors pass the snapshot their feed projected (`ExpectedDirectHumanGuidance`); a different value, or a
  recorded snapshot with no stated expectation, is `409 agent_attempts.direct_guidance_mismatch` with no provider process.
  The internal invocation requests carry the expected snapshot, and `DirectHumanGuidanceManifest.Agrees` checks the sealed
  manifest (read through the existing sealed-artifact verification) against it in both Claude adapters before the process
  starts. The manifest must always be a parseable JSON object (an unparseable text or non-object root never proves that guidance is
  absent); without guidance it carries neither direct member; with guidance it carries each member exactly once, the fixed
  boundary, only the accepted text, and exactly one non-empty untrusted-evidence boundary after the guidance (a missing,
  duplicated, or misplaced boundary disagrees). A mismatch is the
  ordinary failed invocation with zero process starts. Accepted context is never rebuilt from current settings, and no CLI
  argument, tool, environment, permission, session, output schema, or provider version changes. Replay of an undispatched claim
  after a restart reads only the sealed manifest and the persisted snapshot, so later requests or draft edits cannot change it.
- **Read model.** The implementation status, the review-correction status, the attempt evidence (and so the history
  drill-down), and the cockpit's latest Agent attempt expose `directGuidance { state, text }` with the three states above
  (`NotRecorded`, `Provided`, `Unknown`); text appears only for `Provided`, never for `Unknown`, and a malformed
  stored value is rendered as `Unknown` without throwing or disclosure. The fact states what the host sealed into the
  attempt's context and is not evidence that a provider read, understood, or followed it.
- **Limits.** The guidance is intentionally visible in the sealed manifest and the projections and is not redacted. The
  snapshot-versus-manifest agreement is enforced at the invocation boundary, after dispatch has been marked; no protection is
  claimed against every hostile write after the dispatch boundary. A BLOB written out of band into the TEXT column is decoded as
  text by the driver and judged by the same exact rule.

### Execution evidence versus semantic outcome

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
  [Claude-reported model context limits](#claude-reported-model-context-limits)).
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
token-usage contracts describe below, and shares it through a single
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

### Run-wide Agent process-duration evidence summary

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

### Recorded collaboration-input provenance

The collaboration-message evidence drill-down (see the shared
`Attempt.GetAgentProcessExecutionEvidence()` rule above) additionally exposes
the exact, ordered `AttemptInputMessage` set the producing Agent attempt was
launched against — labeled everywhere as **recorded collaboration inputs**,
never as the complete prompt, complete context manifest, or a resumable
provider session: it is a durable reference list, not a transcript. Zero
recorded rows is role-aware, never a blanket "empty" default: only a
`Planner` attempt legitimately starts from no prior collaboration fact and
is reported as `Empty`. Every other current role (`CriticalReviewer`,
`Resolver`, `Implementer`, `CodeReviewer`) always persists at least one
required input row through its own claim path, so zero rows observed for any
of them reports `Invalid` instead — an empty set there is indistinguishable
from a lost or corrupted one, and must never be presented as the same
innocent "started from none" state a genuine Planner attempt reports. This
role-aware read is only ever reached once the resolved attempt's own role and
provider are proven coherent with the message's own actor AND both are
proven defined: an attempt/message pair whose role and provider are both
undefined is never treated as a trivially "equal" (null-equals-null) match —
it fails closed to the same `AttemptLinkBroken` status any other incoherent
link produces, never a thrown error and never a role dereferenced from an
undefined value.

Resolution is strict: every referenced `CollaborationMessage` must resolve
within the same run the drill-down was requested for — a cross-run or
dangling reference is never followed — and the attempt's own stored 0-based
sequence must be gapless and duplicate-free. Any incoherence (a gap, a
duplicate, a missing/foreign message, or the role-aware zero-rows case above)
fails the *entire* set closed (`Invalid`) rather than presenting only the
resolvable subset, which would misrepresent the attempt's real ordered input
identity — the same "a partially trustworthy read is not trustworthy"
principle this drill-down already applies to its process and token-usage
evidence. A coherent set is capped at a small, explicit bound with an honest
omission signal, mirroring the existing bounded artifact list: unlike that
cap, this one is a real, reachable limit today, not merely defensive — a
review-correction (`Implementer`/`ReviewCorrection`) attempt's own Execution
report plus up to `ReviewCorrectionOutputSchema.MaximumFindings` (10) Review
findings is 11 rows at the bound.

Each returned entry carries only its own recorded sequence, the referenced
message's durable id, its `CollaborationMessageType`, its own timeline
sequence, and its recorded time — never that message's summary, structured
content, or any other payload. The cockpit cross-references the referenced id
against its own already-loaded collaboration timeline (the same map that
already verifies reply-parent relationships) to show that message's
already-displayed summary; a reference the currently loaded timeline window
does not contain is described as such — "not present in the currently loaded
timeline" — never rendered as if it were visible there.

### Sealed Agent-artifact window inspection

The collaboration-message evidence drill-down's bounded artifact list (see
above) is a metadata-only inventory — purpose, byte length, truncation, and
capture outcome, never content. A separate, additive operation
(`GetSealedAgentArtifactWindowQuery`/`GetSealedAgentArtifactWindowEndpoint`)
lets the owner open a bounded, integrity-verified text window of the sealed
bytes one of those metadata rows describes, restricted to a closed four-purpose
allowlist: `AgentContextManifest`, `AgentStandardOutput`, `AgentStandardError`,
and `AgentFinalResponse` — never the two Process-attempt purposes
`GetProcessAttemptOutputEndpoint` already serves, and never a live, still-being
-written partial capture (only a sealed row is ever served here). Resolution
repeats the identical message-to-attempt coherence rule the evidence
drill-down itself already enforces — the same `ProviderObserved` provenance
check, the same `AttemptId` foreign-key-only resolution, and the same
role/provider coherence check between the message's actor and the resolved
`Attempt` row. Each operation still owns its own request shape, identity proof, and failure projection; only the artifact-row filter and sealed read are shared, through
`SealedAgentArtifactWindowReader` (see [Agent-attempt history and evidence inspection](#agent-attempt-history-and-evidence-inspection)). An
`Artifact` row is then resolved by matching **all three** of `AttemptId`,
`RunId`, and the requested `Purpose` together; a purpose outside the closed
allowlist is rejected at both the API route (an unrecognized route segment is
a safe 404, mirroring `GetProcessAttemptOutputEndpoint`'s own `stream` mapping)
and, independently, inside the query handler itself (defense in depth for any
future non-HTTP caller).

Every requested window is served through
`IArtifactStore.VerifyAndReadSealedAsync`, which enforces two containment
proofs and one integrity proof, all case-sensitive and all failing closed to
`Missing` or `IntegrityMismatch`:

- **Lexical containment.** A stored relative path that is empty, absolute, or
  that resolves through `..` segments to anything not strictly under the
  artifact root — including a case-distinct sibling spelling of the root — is
  rejected before any file is opened.
- **Physical containment (Windows).** The candidate file is opened exactly
  once. On Windows, that open handle's own real, fully reparse-point-resolved
  path (`GetFinalPathNameByHandleW`) must lie under the artifact root's
  freshly resolved real path, again compared case-sensitively. A junction or
  symbolic link on the root's descendants, or on the sealed file itself, that
  redirects outside the root is therefore rejected even when the target's
  length and hash match. A root that is itself redirected is accepted only
  while the opened file remains within that root's resolved target. On other
  platforms only the lexical proof runs; physical containment is not proven
  there.
- **Integrity.** The whole file's length and SHA-256 are verified against the
  `Artifact` row through that same open handle before any byte is returned,
  and the bounded window is read from that same handle — nothing is reopened
  by path in between.

None of these outcomes discloses a storage path, hash, or raw diagnostic
across the API boundary. The response never resolves the attempt's other
artifacts, other attempts, or any other run — only the one sealed row this
exact `(AttemptId, RunId, Purpose)` triple names. Residual limits: the proofs
describe the file at open time and do not defend against a privileged actor
mutating the store's files in place after the open; sealing, capture, partial
reads, and cleanup are outside this contract. The bounded-window UTF-8 cursor
semantics (a byte offset, a byte cap, and a never-split multi-byte codepoint
boundary deferred whole to the next window) are the same ones
`GetProcessAttemptOutputEndpoint` established; this operation adds only a
closed set of purposes and a new resolution path to reach it.

The frontend renders every returned window as literal text (React's default
text-node rendering; never `dangerouslySetInnerHTML` or any other HTML
interpretation) inside the existing evidence drill-down, offering purpose
selection only for a purpose actually present in that attempt's own already-
loaded, bounded artifact list — never every allowlisted purpose
unconditionally — and a manual "load next window" action rather than
automatic paging. Selecting a different purpose, or a different run/message,
or closing and reopening the drill-down drawer, all discard any previously
fetched text and cursor state before the next fetch begins. Every window is
labeled as historical, with a caveat matched to how that exact purpose is
actually produced — never one blanket claim asserted for all four: the
context manifest is host-constructed from already-curated durable fields,
never raw provider output, while standard output, standard error, and final
response are provider output passed through a fixed, best-effort redaction
pattern list before capture. Neither classification is a live tail, and
neither guarantees the complete absence of sensitive content — an unusual
identifier in a host-constructed field, or an unrecognized secret format in
redacted provider output, may still remain. No window is ever rendered or
persisted anywhere but this one on-demand view (no browser storage, no URL,
no log).

### Agent-attempt history and evidence inspection

The message-linked evidence and artifact-window routes require a collaboration
message's `AttemptId`, and the six role status routes select only their own
role's latest attempt, so a failed or interrupted historical Agent attempt that
emitted no `ProviderObserved` message had no navigation path to its sealed
output. Three additive, protected, read-only MVC operations close that gap
without changing any provider, workflow, schema, or the message-linked routes:

- **History.** `GET /api/runs/{runId}/agent-attempts` (`GetAgentAttemptHistoryQuery`)
  returns one page of the run's `Kind == Agent` attempts (never Process attempts,
  never another run's) in strictly descending `AttemptNumber`, which the unique
  `(RunId, AttemptNumber)` index makes a total order. `beforeAttemptNumber` is an
  exclusive cursor, so a page boundary is stable while newer attempts are appended,
  and a run may hold more than the current 16-claim default (verified with 45
  attempts). `limit` defaults to 10 and is hard-capped at 20; a non-positive
  `limit` or cursor is a 400 and an unknown run a 404. The handler reads one extra
  row to compute `hasMore` and returns `nextBeforeAttemptNumber` (the last returned
  number) only when `hasMore` is true. Rows carry identity and lifecycle facts only, plus — for an attempt that was
  requested as a manual format repair — `repairSourceAttemptId` and `repairSourceAttemptNumber`, present only when the
  source is proved (an earlier Agent attempt of the same run) and null otherwise; the evidence route adds the same two
  fields. They are provenance and never a claim that the source was fixed.
- **Evidence metadata.** `GET …/agent-attempts/{attemptId}/evidence`
  (`GetAgentAttemptEvidenceQuery`) resolves by the exact `(RunId, AttemptId)` pair and
  `Kind == Agent`; an unknown, foreign-run, or non-Agent attempt is a safe 404
  (`agent_attempts.not_found`). It returns identity, lifecycle, host-measured process
  evidence, provider-reported token evidence, for a concluded Claude attempt the additive nullable `modelContextLimits`
  member (the models Claude listed with their reported limits; see
  [Claude-reported model context limits](#claude-reported-model-context-limits)), and metadata (purpose, byte length,
  truncation, capture outcome) for only the four allowlisted purposes, filtered by
  the artifact row's independently stored `RunId`, `AttemptId`, and purpose, so a
  cross-run row or a Process-purpose row never appears.
- **Sealed text window.** `GET …/agent-attempts/{attemptId}/evidence/artifact-window/{purpose}`
  (`GetAgentAttemptArtifactWindowQuery`) serves the same closed four-purpose
  allowlist (`context-manifest`, `stdout`, `stderr`, `final-response`; anything else
  is a 404) with the same byte cap and UTF-8 cursor. It delegates to the shared
  `SealedAgentArtifactWindowReader`, which the message-linked handler now also calls
  for its artifact-row filter and `IArtifactStore.VerifyAndReadSealedAsync` read, so
  both routes return identical bytes and statuses (`Ok`, `ArtifactNotFound`,
  `Missing`, `IntegrityMismatch`) and inherit the store's containment and integrity
  guarantees unchanged. Text is never returned unless the whole sealed file first
  verified. Each route still owns how it proves the attempt's identity: the message
  route keeps its `ProviderObserved` and message-actor checks untouched.

**Identity rule.** `AgentAttemptIdentity.IsCoherent` is the single read-side
provenance check for history and evidence: Agent kind; defined status, role,
provider, and response contract; a role/provider pair the product actually launches
(Codex Planner, Resolver, CodeReviewer; Claude Code CriticalReviewer, Implementer);
a response contract that belongs to that role; and a well-formed immutable
assignment snapshot. A history row that fails is still listed by number and times
(with its status when readable), but its role, provider, contract, and outcome are
withheld and it is not offered for evidence (`identityValid: false`); the evidence
route then returns 200 with `identityValid: false` and only the non-enum facts, and
the window route returns the new `AttemptIdentityInvalid` status (only ever produced
by this route) with no text. This is provenance validation only — it is not a claim
about provider capability, account allowance, session resumability, or current
workflow authority.

**Unreadable persisted strings.** Status, role, provider, and response contract are
stored as strings and converted by EF while an `Attempt` is materialized, so a
corrupted or legacy value would throw before the identity rule could run.
`AgentAttemptRead` therefore reads only non-enum columns first (id, number, claim,
completion, and dispatch times) to establish existence, run ownership, ordering, and
the page, then materializes each full row inside a guard that catches any
`InvalidOperationException` raised during full-row materialization (database and
cancellation failures are not caught). The guard cannot prove the exception came
specifically from enum conversion, so it treats the row as unreadable whatever the
cause of that exception type. An unreadable row is handled exactly like an
incoherent one: listed in place with `identityValid: false` and a null status,
evidence `identityValid: false` with a null `attemptStatus`, and the window
`AttemptIdentityInvalid` with no text — never the exception, the stored string, or
artifact content. The other rows of the same page are unaffected. The existing EF
model is unchanged; no raw SQL or schema change is involved, and the guard does not
repair or rewrite the stored value.

**Disclosure.** The metadata routes (history and evidence) and the envelope of a window
response carry no storage path, content hash, provider session identifier, prompt,
adapter contract version, executable path, argument, or working directory, and the tests
assert that none is added as a field. A verified window's `text` is different: it is the
captured sealed content, returned exactly as recorded, so it can contain whatever the
provider or host wrote — including text that resembles a prompt, session identifier, path,
or hash — after only the best-effort redaction applied at capture. The context manifest
is host-constructed content and the other three purposes are best-effort-redacted provider
output that may still contain sensitive text, exactly as for the message-linked route. No route
makes a provider call, opens
a session, replays or retries an attempt, mutates workflow state, or reads a live
partial capture.

### Provider token-usage contracts

**Claude Code — proven.** The token-usage contract was verified from the
installed `@anthropic-ai/claude-code@2.1.276` package's native `claude.exe`
using the same evidence method as the Claude Code CLI safety contract above:
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

### Claude-reported model context limits

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

### Provider account-allowance contracts

**Codex — read-only account observation, distinct from the Codex per-attempt
token-usage contract above.** The Codex App Server documents a JSON-RPC
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

### Codex model and reasoning-effort catalog contract

**Codex — read-only, picker-visible model catalog observation, distinct from
both contracts above.** The official Codex App Server `model/list` method
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
process-tree-cleanup mechanics the account-allowance contract above
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

### Explicit Codex model and reasoning-effort requests

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

### Explicit Claude model-alias requests

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

### Explicit Claude effort requests

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

### Optional Claude agentic-turn limit for mutation attempts

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

### Per-provider run token-activity warnings

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

### Per-provider run token-activity stop at Agent claim

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

### Run-scoped Codex account-usage stop at claim and dispatch

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

### Run-scoped advisory Codex account-usage warning

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

### One manual Codex Planner format repair

A human may request **one** repair of a Codex Planner attempt whose recorded outcome is exactly
`InvalidStructuredOutput` (a clean-exit provider response that failed the Proposal contract's
validation; the attempt's status is `Failed` and its sealed response stays inspectable through
[Agent-attempt history and evidence inspection](#agent-attempt-history-and-evidence-inspection)). This is
ADR-0004's optional bounded format repair, implemented for the Planner/Proposal contract only. It is a
**fresh, schema-constrained Planner invocation**, never a claim that the original response's meaning was
preserved, transformed, or corrected, and it is a DevalCopilot-owned retry contract, not evidence of model,
account-allowance, session, or invocation eligibility.

- **Operation.** `POST /api/runs/{runId}/agent-attempts/{sourceAttemptId}/codex-plan-repair` (protected, no
  request body, no free text). The ordinary `POST …/agent-attempts/codex-plan` request stays available under
  its own rules. Both send `CreateCodexPlanningAttemptCommand`; the repair carries `RepairSourceAttemptId`, so
  the repair is the same handler and therefore has the same protections.
- **Eligibility (server-owned).** The source must belong to the run (an unknown id and another run's id are the
  same 404 `agent_attempts.repair_source_not_found`), be a dispatched Codex Planner/Proposal Agent attempt with status `Failed` and outcome
  `InvalidStructuredOutput` (the only coherent pair; a persisted `Completed` row with that outcome is refused;
  `Attempt.IsEligiblePlanningRepairSource`, else 409
  `agent_attempts.repair_source_ineligible`), have no `AgentRepairSourceAttemptId` itself (a repair is never
  repaired: `agent_attempts.repair_of_repair_forbidden`), have no existing repair
  (`agent_attempts.repair_already_requested`), and be the run's **latest Agent attempt**
  (`agent_attempts.repair_source_not_latest`; a still-running newer Agent attempt is a newer attempt), and have
  been made against exactly the workspace, checkpoint, and fingerprint the repair claim selects
  (`agent_attempts.repair_source_checkpoint_mismatch`: a newer valid checkpoint is a different context, so use an
  ordinary request; the source identity is immutable, so this is checked at the request and again at the durable
  claim boundary against the same selected values). The
  cockpit may suggest the action but never decides eligibility.
- **Claim.** All ordinary planning-claim gates apply unchanged and in the same order: active run, ready
  workspace, active lease, current checkpoint, no other Running attempt of any kind, the count budget
  (ADR-0012) and reserved-time budget (ADR-0013), an observed Codex capability, and a fresh Git fingerprint
  equal to the checkpoint's. The repair takes the next Agent budget slot and attempt number and snapshots the
  Run's current Codex model/effort exactly like an ordinary claim. Source eligibility is checked at the request
  and **again inside the claim's own database transaction**, immediately after the guard UPDATE of the
  preference check (a write that takes SQLite's write lock), so no other claim can commit between the re-check
  and the insert. A source that stopped being eligible, or a competing repair, rolls the claim back and deletes
  the already-sealed manifest, exactly like a changed preference; the same holds for every other failure path of
  the ordinary claim.
- **Persistence.** The additive `AddCodexPlanningRepairLink` migration adds a nullable
  `attempts.AgentRepairSourceAttemptId` (no default, no backfill), a self-referencing foreign key with
  `NO ACTION` delete behavior (deleting a referenced source on its own is refused, while the existing
  Attempt-to-Run cascade still deletes a run with its source and repair together; SQLite checks it at
  statement end, and an integration test proves both), and the filtered unique index `ix_attempts_agent_repair_source`
  (`WHERE AgentRepairSourceAttemptId IS NOT NULL`), the database backstop for at most one repair per source. If
  that index ever rejects a claim, the handler reports `agent_attempts.repair_already_requested`. The link is
  set only by `Attempt.ClaimAgentPlanningRepair` and is immutable. Repair chains are excluded by the
  handler and by the Domain eligibility rule, not by a database constraint. The new attempt is otherwise an
  ordinary read-only Planner attempt (provider Codex, contract Proposal, `ReadOnly` profile, adapter
  `codex-planning-v1`).
- **Manifest.** The sealed context manifest (`AgentContextManifest`, host-constructed) is the ordinary planning
  manifest for the current verified context — the run objective, project/workspace/checkpoint identities and
  fingerprint, the unchanged expected Proposal schema, and the project instruction context of ADR-0021 captured fresh
  at the repair's own claim, with no human instruction and no prior decisions, exactly as an ordinary claim — plus one fixed host-authored
  `formatRepairNotice` (`ContextManifestBuilder.FormatRepairNotice`): an earlier planning response failed
  structural validation, this is a fresh planning request, and exactly one Proposal satisfying the unchanged
  schema is required. It never contains the source's raw response, parser or validation detail, artifact path,
  attempt identity or outcome, or any human-supplied text.
- **Dispatch and result.** Nothing downstream changed: the supervisor picks the repair up as an ordinary
  Planner attempt (including replay after a restart while it is still `Running` and undispatched), the single
  read-only adapter dispatch and its arguments, timeout, capture limits, parser, result recording, and the
  one-Proposal ledger rule are reused. The repair yields either one ordinarily validated Proposal or its own
  truthful outcome (including `InvalidStructuredOutput` again, after which nothing more can be repaired). A
  dispatched repair that a restart interrupts is reconciled `Interrupted`, is never re-invoked, and consumes the
  source's one repair. The source attempt and its evidence are never modified.
- **Read model.** The Planner status (`GET …/agent-attempts/codex-plan`) adds `repairSourceAttemptId` and
  `repairSourceAttemptNumber` to the latest Planner attempt — lineage only, null for an ordinary attempt. No
  response of either operation carries the source's response, a path, a hash, or a diagnostic.
- **Limits.** The retry consumes a real Agent budget slot and reserved invocation time and can fail like any
  claim. It is not an automatic retry, provider-session resume, schema relaxation, or a way to bypass a budget.

### One manual format repair of the remaining read-only stages

The same accepted, bounded recovery (ADR-0004) exists for the three other read-only collaboration stages:
the Claude Code **CriticalReviewer** (`CriticalReview` contract), the Codex **Resolver** (`ChallengeResolution`), and
the Codex **CodeReviewer** (`ImplementationReview`). A human may request **one** repair of such an attempt whose
recorded outcome is exactly `InvalidStructuredOutput`. Like the Planner's, it is a **fresh, schema-constrained
invocation of the same read-only role** with the ordinarily validated durable context; it neither transforms nor
preserves the meaning of the failed response, replays none of it, and is not evidence of model, account-allowance,
session, or invocation eligibility. The Planner's repair is unchanged. A mutating Implementer or ReviewCorrection
attempt has no repair.

- **Operations.** Three protected, bodyless `POST` operations (no request body, target, prompt, provider, model,
  or free text; a body sent anyway changes nothing):
  `/api/runs/{runId}/agent-attempts/{sourceAttemptId}/critical-review-repair`,
  `…/challenge-resolution-repair`, and `…/code-review-repair`. Each sends one command through
  `IApplicationMediator` — `CreateClaudeCriticalReviewAttemptCommand`, `CreateChallengeResolutionAttemptCommand`,
  or `CreateCodeReviewAttemptCommand`, built by its `ForRepair(runId, sourceAttemptId)` — and the role's existing
  claim handler derives the target from the source's persisted inputs. There is no second orchestration layer and
  no generic repair framework; each role keeps its own validation, manifest builder, schema, result handler, and
  supervisor. The response is `{ attemptId, attemptNumber, repairSourceAttemptId }` and nothing from the source.
- **Source eligibility (server-owned).** The source must belong to the run (an unknown id and another run's id are
  the same 404 `agent_attempts.repair_source_not_found`, message "The selected attempt was not found for this run.")
  and satisfy `ReadOnlyFormatRepairPolicy.IsEligibleSource` for the path's own response contract: an Agent attempt
  with status `Failed` and outcome exactly `InvalidStructuredOutput`, dispatched and concluded, with
  host-measured process evidence proving a clean exit, the path's exact provider, role, response contract,
  expected message type, protocol `1.0`, `ReadOnly` permission profile, and current known v1 adapter contract
  (`claude-critical-review-v1`, `codex-challenge-resolution-v1`, `codex-implementation-review-v1`), and a
  well-formed assignment snapshot; otherwise 409 `agent_attempts.repair_source_ineligible`. It must carry no
  `AgentRepairSourceAttemptId` itself (`agent_attempts.repair_of_repair_forbidden`), have produced **no durable
  collaboration message** (a source that recorded a semantic result did not fail structurally), have no existing
  repair (`agent_attempts.repair_already_requested`), be the run's **latest Agent attempt**
  (`agent_attempts.repair_source_not_latest`), and — with the workspace context known — have been made against
  exactly the selected workspace, checkpoint, and fingerprint (`agent_attempts.repair_source_checkpoint_mismatch`).
  A stored value that cannot be materialized (an unreadable enum, assignment, process, or input row) fails
  closed as `agent_attempts.repair_source_ineligible` with a fixed message that echoes no stored value; only
  `InvalidOperationException` from materializing the source means "unreadable" — database and cancellation
  failures propagate and are never reported as invalid source evidence.
- **Exact retained context.** The repair retains exactly the source's recorded inputs, validated again by the
  ordinary role gates rather than trusted from the source rows:
  - *CriticalReviewer:* the source's one `Proposal` input (sequence 0), the root Proposal or the first Resolver
    revision, through the same bounded-lineage rule as an ordinary review.
  - *Resolver:* the original Proposal and the complete ordered Challenge set. The challenged review is derived from
    the first Challenge's owning attempt, validated as an ordinary request would be (its own provider-observed
    Challenges replying to the Proposal, bounded lineage), and the Proposal plus its ordered Challenges must equal the
    source's recorded ordered inputs — another review, or a merely overlapping, partial, reordered, or extended
    Challenge set, is `agent_attempts.repair_source_inputs_mismatch` (409); a missing, gapped, or otherwise
    malformed recorded input set is `agent_attempts.repair_source_ineligible`.
  - *CodeReviewer:* the ExecutionReport (initial implementation or successfully applied correction report) and the
    ordered (verification command, verification execution) pairs. The report chain is validated as for an
    ordinary review, and the *currently enabled and latest `Passed`* verification selection must still equal the
    source's recorded set: a rerun, an enabled-command change, a reorder, or a replacement is an ordinary new
    review (`agent_attempts.repair_source_inputs_mismatch`), never substituted into the repair. The ordinary
    verification errors (`no_verification_commands_enabled`, `verification_evidence_missing`, `_running`, `_not_passed`)
    still apply.

  Recorded inputs are read from non-enum columns only, so a source with an unreadable enum column is refused rather
  than crashing, and the database's unique indexes make duplicate message or execution identities impossible.
- **Claim.** Every ordinary gate applies unchanged and in the same order: active run (`runs.not_active` for the
  CriticalReviewer, `runs.not_running` for the others), ready workspace, active lease, current checkpoint, no other
  Running attempt of any kind, the count budget (ADR-0012) and reserved-time budget (ADR-0013), the provider
  token stop, an observed provider capability, a fresh Git fingerprint equal to the checkpoint's, and a fresh
  model/effort snapshot. An invalid repair source is refused **before** the provider probe, Git capture, or manifest
  sealing, and the source check precedes the running-attempt check so a request that lost the race to the source's one
  repair is told "already requested". The repair takes the next Agent budget slot and attempt number.
- **Atomic commit seam.** At the durable claim boundary the source, its exact inputs (with the report chain and
  verification selection for the CodeReviewer, the reviewed lineage for the CriticalReviewer and Resolver), and
  the execution context (run still active, workspace still ready, lease still active, selected checkpoint still the
  workspace's latest) are **re-read inside the same short database transaction, after a guard statement has run in
  it, before the linked Attempt, its inputs, verification rows, and artifact metadata are persisted and committed**;
  external Git and artifact work stay outside it. The Resolver and CodeReviewer already used a short claim
  transaction with the Codex preference and stop-policy compare-and-set statements as guards; the CriticalReviewer
  gains a bounded repair-only transaction (its ordinary request keeps the single concurrency-token save) whose guard is the
  stop-policy compare-and-set, followed by the Claude preference read. A late read alone is never the guarantee: on
  SQLite (Microsoft.Data.Sqlite's default serializable transaction) the write lock is taken when the transaction
  begins, so a competing writer cannot commit between those re-reads and the insert. Every authority read at this
  seam (owning attempts, messages, report chains, verification commands and executions, and the reviewed
  lineage, in all three repair paths) is **untracked**: the claim's own context already holds the earlier entity
  instances, and a tracked re-query would silently return them instead of a change committed just before the
  transaction began. Tracking is not disabled globally and the tracker holding the Run and the pending claim
  writes is never cleared. The ordinary request-time validators are unchanged; the repair paths opt into the
  untracked reads. The five repair helper types (`AgentRepairLineage`, `ReadOnlyFormatRepairInputs`,
  `ReadOnlyFormatRepairLink`, `ReadOnlyFormatRepairManifest`, `ReadOnlyFormatRepairSource`) live in the Application
  `Policies/FormatRepair` folder, not in the feature root; orchestration stays in the handlers. A source or input that
  changed refuses the claim with the same codes as the request-time check, rolls back completely, and deletes the
  already-sealed manifest. The global unique index `ix_attempts_agent_repair_source` is the at-most-one backstop
  (`agent_attempts.repair_already_requested` if it ever rejects a claim); no column, migration, default, or backfill
  was needed. Cancellation always propagates, never becoming a `Result`; a failed save or commit is resolved by the
  independent durability probe: persisted → success, not persisted → the manifest is deleted, unresolved → the
  manifest is preserved and `attempts.persistence_unresolved` is reported.
- **Manifest.** The sealed manifest is the ordinary role manifest for the current verified context plus one fixed,
  host-authored `formatRepairNotice` member, placed immediately before `untrustedEvidenceBoundary` so it stays in the
  trusted part of the document; the unchanged expected schema and the untrusted-evidence boundary remain. The
  notices are
  `ClaudeCriticalReviewContextManifestBuilder.FormatRepairNotice` (critical review),
  `ChallengeResolutionContextManifestBuilder.FormatRepairNotice` (challenge resolution), and
  `CodeReviewContextManifestBuilder.FormatRepairNotice` (both the initial and the correction-evidence form): an
  earlier response for this proposal, these challenges, or this implementation failed structural validation, this is a
  fresh request, and exactly one response satisfying the unchanged `expectedOutputSchema` is required. The source's
  raw output, parser or validation diagnostic, artifact path, attempt identity, outcome, and any human text are
  never included; the source identifier is durable provenance and not provider instruction. Restart replay uses the
  sealed manifest and the claimed assignment and never rebuilds from a later source or setting.
- **Dispatch.** `MarkAgentAttemptDispatched` protects the three repair paths against an incoherent link, source, or
  input identity (`agent_attempts.invalid_repair_link`, no provider process): the repair's own exact tuple, a distinct
  earlier same-run source that is still an eligible failed source, the same workspace, checkpoint, and fingerprint,
  no durable message from the source, and the repair's recorded inputs (and verification pairs) equal to the
  source's. It deliberately does not re-apply the claim-time "latest" and "no repair" tests, since the repair now owns
  that slot, and the ordinary duplicate-input classifications (`input_already_reviewed` and its counterparts) stay.
  A committed repair consumes the source's one repair even if it is interrupted or never dispatched.
- **Result.** The role's unchanged adapter, arguments, parser, and result handler record either the ordinary
  validated result (an Acceptance or Challenges; Decisions plus a revised Proposal; a review approval or
  findings) or the repair's own truthful outcome. An invalid repair records no semantic message and cannot be
  repaired again. A repair adds no challenge round, does not bypass the two-round planning limit or the exactly-once
  depth-two escalation (only the repair's valid result can record it), and grants no implementation authority,
  correction authorization, automatic follow-up, or inferred success.
- **Read model.** The CriticalReviewer, Resolver, and CodeReviewer status routes add `repairSourceAttemptId` and
  `repairSourceAttemptNumber` (present for a repair; the number is null only if no source attempt of the run is
  found). The Agent-attempt history entries and evidence add the same two fields for **every** repair, including
  the Planner's, and read null unless the link can be proved (an earlier Agent attempt of the same run, found
  through non-enum columns). All are provenance only — never a claim that the repair fixed or preserved the source.
- **Limits.** The repair consumes a real Agent budget slot and reserved invocation time and can fail like any claim.
  It is not an automatic retry, fallback, or debate, provider-session resume, schema relaxation, or a way around a
  budget, and it never touches Git or publication policy.

### Optional second challenge round and escalation

The existing Proposal → Challenge → Decision → revised Proposal chain supports **one optional second
review-and-resolution round** for a proposal lineage. It is explicit and manual end to end — every review and
every resolution is a separately requested attempt — and it adds no provider capability, CLI argument, session
resume, automatic loop, human override, or account-usage input. ADR-0004's bounded challenge rounds and
ADR-0009's role-first authority are applied unchanged; nothing in an accepted ADR is reversed.

- **Lineage, from durable identity only.** `PlanningLineage` derives the chain from same-run,
  provider-observed messages, their exact owning Agent attempts (role, response contract, and provider
  coherent with the message's recorded `Actor`, per the role-first rule above), the ordered attempt inputs,
  reply links, completed outcomes, and the exact workspace, checkpoint, and fingerprint. It never compares
  natural-language text or guesses that two challenges describe the same issue.
  - **Root (depth 0)**: a completed Planner Proposal (`Proposed`), provider-observed.
  - **First revision (depth 1)**: a completed `Resolved` Resolver attempt's one revised Proposal whose ordered
    inputs are a valid parent Proposal followed by that parent's *complete* Challenged review's Challenges
    (exactly one such review, whose sole input is the parent, bound to the same workspace and checkpoint), with
    one provider-observed Decision per Challenge replying to that Challenge in order, and the revised Proposal
    replying to the parent and recorded after the last Decision.
  - **Second revision (depth 2)**: the same rule applied to a depth-1 parent. Evaluation is bounded, cycle-safe
    (a message is visited once), and a depth beyond two is not a valid lineage level. Missing, foreign-run,
    stale, cyclic, duplicated, malformed, or incoherent evidence fails closed with a fixed message that echoes no
    stored text.
  - **Supported identity and unreadable evidence.** Role still decides which lineage role a message may hold, but
    every attempt the lineage trusts (Planner, Resolver, CriticalReviewer, the accepted-original path's Planner
    and review, and the resolving attempt itself) must also pass `AgentAttemptIdentity.IsCoherent`: defined
    role, provider, and response contract, a role/provider pair DevalCopilot actually launches (Planner,
    Resolver, CodeReviewer with Codex; CriticalReviewer, Implementer with Claude Code), a contract that belongs
    to the role, and a well-formed assignment. A forged pair with a matching message actor, or an undefined role
    or contract, is refused. The lineage reads load the run's attempts and messages through EF string-enum
    converters, so one unparseable stored enum string would otherwise throw; each lineage read catches only the
    materialization failure (`InvalidOperationException`) and refuses with the same fixed error, whether the
    unreadable row participates in the lineage or merely sits beside a healthy one. The exception and the stored
    value are never surfaced. The guard catches `InvalidOperationException`, which cannot prove an enum-conversion
    cause and could have another origin; `DbException` and cancellation exceptions are not caught by it. Other
    consumers of the shared snapshot are unchanged.
- **Bound.** Depth one may receive one explicitly requested critical review. A Challenged review may be resolved
  by one explicitly requested Resolver attempt that decides every challenge of that review and produces the
  depth-two revision. This is the second and final round for the lineage: a depth-two Proposal is neither
  reviewable nor implementable through it, and there is no third review, silent reset, automatic claim, or
  override. A genuinely new Planner root is a separate explicit planning request and starts its own lineage.
  The cap counts validated reply/attempt identity across the whole lineage, so it is deliberately more
  conservative than the roadmap's "two challenge rounds per material issue" wording
  ([workflow model](workflow-model.md#bounded-loops)): the system does not decide whether two natural-language
  challenges are the same issue. Failed or invalid attempts add no revision but still consume the existing
  run-wide claim-count, reserved-time, and token-stop budgets.
- **Review claim.** `CreateClaudeCriticalReviewAttempt` accepts a root or a depth-one revision. A depth-two
  Proposal is refused 409 `agent_attempts.proposal_lineage_exhausted` **before** the provider-availability
  probe, Git capture, or manifest sealing, as is every other lineage refusal except an unknown proposal (which
  keeps its position after those checks); the codes are `agent_attempts.proposal_not_found` (404), `not_provider_observed_planner_proposal`,
  `proposal_attempt_not_valid`, `proposal_checkpoint_stale`, or `proposal_lineage_not_valid` (409). A proposal
  with an earlier successful review is `already_reviewed`. The reviewed Proposal remains the attempt's sole
  input at sequence 0.
- **Resolution claim and result.** `CreateChallengeResolutionAttempt` evaluates the reviewed Proposal with the
  same rule and refuses a depth-two Proposal (`proposal_lineage_exhausted`); the attempt's inputs are the
  reviewed Proposal then its complete ordered Challenge set. `RecordChallengeResolutionResult` decides the
  lineage **again** before mutating anything: the reviewed Proposal must still be a valid depth-≤1 Proposal, its
  Challenged review must still be the sole review whose complete challenge set equals the attempt's inputs, and
  no other attempt may already have resolved that exact ordered set (`already_resolved`); otherwise the result is
  refused with nothing recorded and the attempt unchanged. The existing rule that every Challenge is decided
  exactly once is unchanged.
- **Escalation.** When the result is the *second* resolution (the reviewed Proposal is depth one) and the
  attempt truly completed `Resolved`, the same single save records the Decisions, the depth-two revised
  Proposal, and exactly one `Escalation` collaboration message: `HostConstructed`, authored by the Orchestrator
  and addressed to the Human, attemptless, replying to the depth-two Proposal, with its own
  `collaboration.message_recorded` event (linked to the resolving attempt) as the newest event of that result.
  Its content is fixed text plus bounded identifiers and a count: the root, first-revision, and second-revision
  Proposal ids, and the ids of the second-round challenges each decided once. It never copies provider, artifact,
  path, or credential text. It records that a human decision is needed and says, in its fixed `options`,
  `consequences`, and `recommendedChoice` text, what that decision can be: inspect the final Proposal and the
  second-round Decisions, then either separately authorize exactly one implementation of that exact plan and explicitly
  request it (only a durably committed implementation claim consumes the authorization: a refused request or a claim that
  definitely does not commit consumes nothing, and a committed claim stays consumed even if its execution later fails),
  or request a new plan through a new explicit planning
  request. It selects nothing, grants nothing, and approves nothing, a third critical review or resolution remains
  unavailable, and it is never consulted by the review-correction authorization (which is keyed by its own escalation
  table). Protocol 1.0, the summary, the five content field names, the identifier-derived `evidence` sentence, and the
  participant, provenance, and reply facts are unchanged by [ADR-0020](../decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md),
  which corrected only the explanatory text: escalations recorded earlier carry the original text, which said the final
  revision was "not implementable through this lineage" and is kept recognizable (see the source rule under
  [Explicit human authorization of one escalated-plan implementation](#explicit-human-authorization-of-one-escalated-plan-implementation)). A source-drift downgrade or any refusal records none of the set. Because a result can be
  recorded only for a `Running` attempt, and that state is what the save transitions, a second recording of the
  same attempt is refused and the escalation stays single.
- **Implementation eligibility.** The accepted original Planner Proposal path is unchanged (an `Accepted`
  review of exactly that Proposal, the Proposal plus that Acceptance as inputs). A depth-one revision that was
  never re-reviewed remains implementable with its complete Decision evidence (inputs: the revision, then its
  Decisions). If its optional second review is `Accepted`, implementation binds to that exact acceptance too: a
  completed `CriticalReview` attempt bound to the workspace and checkpoint, whose single output is a
  provider-observed Acceptance replying to the revision (inputs: the revision, its Decisions, then the
  Acceptance, and the sealed manifest carries it as `acceptedSecondReview`); a broken acceptance link is
  `acceptance_not_valid`. If that review is `Challenged`, the revision is refused 409
  `agent_attempts.plan_challenged` — while its second resolution is pending or failed, and after it succeeded —
  so implementation never falls back to an earlier Proposal. A depth-two Proposal, with or without its
  escalation, is refused `proposal_lineage_exhausted` unless a human explicitly authorized exactly one claim of
  that final plan (see
  [Explicit human authorization of one escalated-plan implementation](#explicit-human-authorization-of-one-escalated-plan-implementation));
  the automatic caps themselves are unchanged. For a Proposal not owned by a Planner attempt, every lineage
  refusal (exhausted, challenged, corrupt, unsupported pair, unreadable) is decided before Git capture or sealing;
  the original Planner path and every other refusal keep their existing position.
  The downstream review and correction workflows re-validate the implementation attempt's recorded input chain
  with the same lineage rule (`ImplementerExecutionReportEligibility`), so what may be implemented and what a
  later chain accepts cannot drift apart.
- **Races and replay.** Each of the three claims decides its lineage again as its last read before the durable
  commit, after the external work (Git capture and manifest sealing): the review and implementation claims
  re-evaluate eligibility, any competing review/implementation, and whether another attempt is `Running`, and on
  refusal delete the already-sealed manifest and commit nothing; the resolution claim re-checks inside its
  existing claim transaction, after its two guard writes have taken the write lock, so the check is atomic with
  the insert. The filtered unique index on `(RunId WHERE Status = 'Running')` and the `(RunId, AgentBudgetSlot)`
  index remain the database backstop for concurrent claims, and the existing budget, reserved-time, token-stop,
  single-Running-attempt, dispatch-marker, and sealed-manifest rules are unchanged. A supervisor dispatches a
  claimed attempt from its own sealed manifest only. A host restart interrupts a still-`Running` read-only
  attempt and its run and never re-invokes it, so a restart cannot revive, advance, or re-escalate an exhausted
  lineage: a completed lineage stays exhausted across a restart, and every review, resolution, and
  implementation request against it is refused by the rules above.
- **Read model.** No response contract changed. The existing review, resolution, and implementation status
  endpoints report the latest attempt of each kind (each already carries its exact input Proposal or Challenge
  identities), and the timeline carries the revised Proposals and the escalation with their reply links; the
  cockpit derives the lineage from those (see the
  [run cockpit specification](../product/run-cockpit-specification.md#optional-second-challenge-round)).
- **Not included.** A third round, automatic claims, a generic workflow engine, a human override of the lineage
  other than the one explicit authorization described next, provider-session resume, Claude account allowance,
  account-usage eligibility or thresholds, model or effort inference, and new provider flags. Implementing an
  escalated plan needs either that one explicit authorization or a new explicit planning request.

### Explicit human authorization of one escalated-plan implementation

A human may authorize exactly one initial implementation claim of the final (depth-two) Proposal of a completed second round
([ADR-0016](../decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md)). It is a real human
decision recorded as a `HumanInstruction`; it is not a provider Decision or Acceptance, it keeps the review and resolution caps
and every budget unchanged (a depth-two Proposal stays neither reviewable nor resolvable, with or without a grant), and it is
distinct from the review-correction authorization, which keeps its own table, factory, instruction, and semantics.

- **Authorize.** `POST /api/runs/{runId}/planning-escalations/{escalationMessageId}/implementation-authorization` with body
  `{ "rationale": "…" }` (at most 8 KiB). The final Proposal is derived from the persisted escalation. The rationale is
  required and normalized by `PlanningImplementationInstruction.Normalize` (the shared `BoundedGuidanceText` policy: form C, LF,
  trim, non-blank valid Unicode, at most 600 UTF-16 code units, no control character except LF, the best-effort summary
  screen); invalid text is `400 planning_authorizations.rationale_invalid` from a validator (repeated by the handler) before any
  read, never echoed. The response is identity only: `status`, `authorizationId`, `escalationMessageId`,
  `finalProposalMessageId`, `humanInstructionMessageId`, and `latestEventSequence`.
- **Source and context.** The escalation must be a host-constructed, attemptless, protocol-1.0 Orchestrator-to-Human
  `Escalation` replying to a depth-two Proposal whose complete two-round lineage evaluates (all Challenges, Decisions, owners,
  reply links, workspace, checkpoint, and fingerprint), be the only escalation replying to that Proposal, and carry the
  canonical summary and exactly one of the two complete canonical content serializations ([ADR-0020](../decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md)):
  the current one the second resolution writes, or the original one that earlier records keep. Both are recomputed whole from the
  verified identifiers and the ordered second-round Challenges and compared ordinally; there is no mixed form, no semantic JSON
  equivalence (member order, whitespace, and escaping are part of the form), no other wording or version, and no caller-selected
  form, and the original form is never written again. Every other source and grant check applies to either form unchanged, as
  do the replay of already sealed manifests and the validity of historical grants and report chains; the Planner root must own
  exactly one Proposal. The run must be `Running` with a stored Agent-admitting execution mode, the latest workspace `Ready`
  with an active lease, the latest checkpoint the lineage's, a fresh Git fingerprint equal to it, and no newer provider-observed
  Planner Proposal (`planning_authorizations.source_stale`). An unknown, foreign, or non-escalation message is `404
  source_not_found`; forged, ambiguous, or incoherent evidence is `409 source_invalid`; a non-current context is `409
  context_not_current`. None repeats stored text.
- **Record.** One `planning_implementation_authorizations` row (migration `AddPlanningImplementationAuthorization`, additive, no
  backfill) binds the run, the escalation message, the final Proposal, and the exact workspace, starting checkpoint, and
  fingerprint to one `HumanInstruction` (`HumanSubmitted`, attemptless, Human to Orchestrator, replying to the escalation,
  summary "Human authorized one implementation claim for the final escalated plan.", content `{ "instruction": "Authorize one
  implementation claim for the final escalated plan.", "rationale": "…" }`). The rationale is stored only in the message and
  read back only in that exact canonical form. The relation owns the nullable `ConsumedByAttemptId` and `ConsumedAtUtc` (an EF
  concurrency token), with unique indexes on the escalation, the final Proposal, the instruction message, and, filtered, the
  consuming attempt. The commit is one short transaction opened after Git capture: its first statement is the atomic
  execution-mode confirmation that takes the write lock, and the context, lineage snapshot, and any existing grant are read
  afresh and untracked inside it, so another connection's earlier commit is seen. An identical retry returns the recorded
  authorization and its event without another message or event; a different rationale is `409 rationale_conflict`; a stale
  grant is `source_stale` and a consumed one `already_consumed`, never revived; an incoherent record is `409 recorded_invalid`.
  There is no renewal, revocation, second grant, or delete. Nothing here claims an attempt, reserves budget, seals a manifest,
  or starts a provider.
- **Read.** `GET` on the same path reports `state`: `Absent` (a valid current source with no grant), `Available` (recorded,
  coherent, unconsumed, bound to the checkpoint current in the database), `Consumed` (with the consuming attempt and time),
  `Stale` (the source or grant no longer matches the current lineage, checkpoint, fingerprint, or a newer Planner root replaced
  the lineage), or `Invalid` (incoherent, forged, ambiguous, or unreadable evidence), plus the final Proposal, the ordered
  second-round Decision identifiers, and, only for a validated record, the authorization and instruction identifiers, the exact
  reason, and the times. It performs no Git work, never claims provider readiness or remaining budget, and writes nothing.
- **Claim.** `POST …/agent-attempts/implementation` keeps its body and recognizes only the specifically authorized final
  Proposal. An unauthorized depth-two Proposal is refused `proposal_lineage_exhausted` exactly as before, before any external
  work. Eligibility is decided before the provider probe and Git capture and again after manifest sealing inside a short
  transaction (same write-lock discipline), which also re-reads the run, execution mode, workspace, lease, and checkpoint. The
  grant is bound to the current workspace, checkpoint, and fingerprint, unconsumed, coherent, and not superseded
  (`agent_attempts.planning_authorization_stale`, `…_consumed`, `…_invalid`). The grant is consumed in the very save that inserts
  the Attempt, its ordered inputs (final Proposal, every Decision of the second resolution in collaboration order, then the
  authorization `HumanInstruction`), the manifest artifact, and the ordinary budget reservation; a refusal, a lost race (the
  consumed-by link is a concurrency token), or a failed save or commit consumes nothing and deletes the orphaned sealed
  manifest. Cancellation or a raw failure at any point after sealing is settled from durable state: the transaction is rolled back and
  released, an independent probe with a token that is never cancelled decides, and only a definite non-commit deletes the manifest (a
  committed claim keeps it and its one spent grant; an unanswered probe keeps it as `attempts.persistence_unresolved`). A committed claim spends its grant for good, even if its attempt later fails, is interrupted, or is never dispatched.
  No earlier revision is promoted, no Acceptance is invented, and a review of the final plan makes the record invalid.
- **Sealed manifest.** The distinct `resolutionEvidence.form` is `humanAuthorizedEscalatedProposal`: the complete ordered
  `decisions` and a `humanAuthorization` object with exactly `authorizationId`, `escalationMessageId`,
  `humanInstructionMessageId`, the fixed `instruction`, and the `rationale`, under a fixed host-authored
  `humanPlanAuthorizationBoundary` placed before the untrusted-evidence boundary. The 32 KiB ceiling is kept by shrinking
  repository evidence only; authority evidence and human text are never truncated, and evidence that cannot fit is refused whole
  before sealing. Direct guidance is unaffected. Every earlier form is byte-identical to before.
- **Dispatch, adapter, and result.** The eligibility feed excludes, and the dispatch gate (`MarkAgentAttemptDispatched`)
  refuses with `agent_attempts.planning_authorization_mismatch`, an attempt whose consumed grant, source, consumption owner, or
  exact ordered inputs do not match the durable facts read afresh (an authorized attempt needs exactly the one grant it
  consumed; an ordinary one holds none; the supervisor's projected fact must equal the durable one). The invocation request
  carries the bounded fact solely so the Claude implementation adapter can require the sealed manifest to agree before any
  process starts; no flag, tool, permission, output schema, or contract version changed. Result recording re-checks the same
  identity. Restart replay dispatches the same consumed grant's immutable sealed manifest and consumes nothing again.
- **Downstream chain.** The ExecutionReport replies to the final Proposal. `ImplementerExecutionReportEligibility` validates the
  authorized form against the implementation's own starting checkpoint (not the result checkpoint), requires the report's reply
  target to be the first input, and resolves the original proposal for the chain as the actual Planner root across both
  revisions, so verification selection, CodeReviewer, ordinary correction (ADR-0010's exact inputs, replies, and separate
  extra-correction authorization), and re-review work unchanged. A later independent Planner Proposal never invalidates an
  already authorized and claimed implementation. The chain result keeps the root as the historical lineage and reply identity and also
  carries the implemented final Proposal as a separate `ImplementedPlan`; the initial code review, its format repair, and the correction
  re-review use that final Proposal as `resolvedPlan` (identifier, summary, and content), so a review never judges superseded scope.
  This applies to every ordinary form as well ([ADR-0017](../decisions/0017-review-the-implemented-plan-through-correction.md)):
  each initial ExecutionReport must reply to the implementation's sequence-zero input, the Planner root is derived through the
  validated lineage (never from the report's reply), `ImplementedPlan` is carried unchanged through every valid correction link,
  and correction reports keep replying to the root. The change is forward only: a review already sealed before it replays its
  existing bytes (an older manifest may name the root), history is never rewritten, and no approval is revoked.

### Explicit verification failure diagnosis

[ADR-0018](../decisions/0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md) closes the gap between a failed local verification and the findings a correction needs, without touching the
ordinary review's approval gate. A diagnosis is a separate response contract, `AgentResponseContract.VerificationDiagnosis`,
owned by the existing `AgentRole.CodeReviewer`, `ReadOnly`, currently assigned to Codex through the unchanged
`CodexProcessInvoker` (adapter contract `codex-verification-diagnosis-v1`, its own strict output schema). It never produces a
`ReviewApproval` or any `CheckpointReview`, and the ordinary review's outcomes, input identity, eligibility feed, and status never
include it.

- **Operations.** `POST /api/runs/{runId}/agent-attempts/verification-diagnosis` with `{ "executionReportMessageId" }` claims a
  diagnosis; `GET` the same path returns its bounded status (findings count, pinned verification list, correction, budget, and
  escalation facts, and a display hint of whether the current verification is diagnosable). `POST
  /api/runs/{runId}/agent-attempts/verification-diagnosis/correction` with `{ "verificationDiagnosisAttemptId", "guidance"? }` claims a
  correction or records the diagnosis's one escalation; it accepts optional direct guidance (see "Correction" below) and no
  authorization. The ordinary `review-correction` endpoint keeps its review-source contract. Nothing runs automatically.
- **Eligibility.** A valid current initial or corrected Implementer `ExecutionReport` on the workspace's current checkpoint, a Ready
  workspace with an active lease, and the host-derived complete verification selection: every enabled command's latest execution
  bound to that checkpoint and fingerprint is terminal and coherent (`Passed`/`Exited`/0 or `Failed`/`Exited`/nonzero, with the
  completion fingerprint equal to the checkpoint's) and at least one failed, and each failed execution has both sealed output rows.
  Missing or running evidence, timeouts, cancellations, interruptions, source drift, process-start failures, contradictory data, and
  malformed ownership are refused with fixed codes (`verification_diagnosis.*`). The actual implemented plan is resolved through the
  validated lineage (revised and human-authorized final plans included).
- **Evidence.** The sealed manifest holds the fixed instruction and failure-output notice before the untrusted-evidence boundary; the
  implemented plan; the report; bounded Git evidence; each verification command's name, number, execution number, status, outcome, and
  exit code (never an executable path, argument, storage path, or hash); and, for each failed execution, deterministic UTF-8 prefixes
  of its redacted stdout and stderr read through `IArtifactStore.VerifyAndReadSealedAsync` (at most 2 KiB per stream and 12 KiB in
  total, stepped down to fit the 32 KiB manifest bound). Each stream is labeled `notTruncated`, `truncated`, or `unknown` for the
  capture and `complete`, `shortened`, `empty`, or `omittedByBudget` for the excerpt. A missing or unverifiable failed stream refuses
  the claim. Redaction of captured output is best-effort and may miss sensitive text. Passed executions carry no excerpt.
- **Pinning and fresh authority.** The report is the attempt's sequence-zero input and the ordered (command, execution) pairs are
  `AttemptVerificationEvidence` rows, each carrying a nullable diagnosis-specific snapshot SHA-256 (versioned canonical text of the
  command, execution, and both failed-output row facts; null and unchanged for ordinary review memberships; required and valid for a
  diagnosis, otherwise fail closed) that is recomputed and compared at the claim seam, dispatch, result recording, and correction
  authority. Result recording validates the requested semantic outcome's clean-exit proof before any drift downgrade. The claim re-reads every piece of authority untracked inside its short transaction after external
  work; dispatch re-reads it again; the result recording re-reads it before recording. Eligibility drift before dispatch records
  `WorkspaceNoLongerEligible`; changed enabled set, latest execution, failed output, or report chain records
  `VerificationEvidenceChanged` (before dispatch with no provider, or after the provider ran with its truthful process and artifact
  evidence and no finding or escalation); Git fingerprint drift stays `SourceChanged`.
- **Result.** `DiagnosisFindingsRecorded` (one to ten `ReviewFinding` messages replying to the report), `DiagnosisEscalated` (one
  bounded `Escalation` replying to the report; it grants no authority to change recipes, tools, permissions, or plan scope), or a
  failure outcome. One successful diagnosis (either kind) is permitted per exact report, checkpoint, and ordered verification identity
  (`InputAlreadyDiagnosed` otherwise); a failed invocation may be requested again explicitly. There is no automatic retry and no format
  repair.
- **Budgets.** A diagnosis consumes the run-wide Agent count, reserved invocation time, and Codex token stop with the code-review
  timeout and profile and the current requested Codex model and effort.
- **Correction.** The existing `ReviewCorrection` contract and hardened Claude adapter, with inputs exactly the previous report
  followed by every finding in timeline order, a fixed source notice in the manifest (no raw logs and no plan input), the same run-wide
  budgets and Claude token stop, the current Claude model, effort, and turn-limit requests, and the one shared correction allowance.
  It requires the exact diagnosis and its complete findings to remain applicable with unchanged verification membership; a later
  execution, a new checkpoint, or a successful correction invalidates the source, and dispatch re-checks it. At exhaustion one durable,
  idempotent Orchestrator `Escalation` bound to the diagnosis is recorded in a `DiagnosisCorrectionEscalation` row with no attempt and no
  grant; no extra-correction authorization exists for this source and the ordinary review's grants cannot be used for it. Both the
  claim and the escalation re-read their authority untracked inside one short write-locked transaction.
- **Correction guidance.** The correction request may carry the optional advisory direct guidance of
  [ADR-0015](../decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md), extended to this request by
  [ADR-0019](../decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md), under the same normalization, bounds,
  `400 agent_attempts.direct_guidance_invalid`, 8 KiB body cap, snapshot (`AgentDirectHumanGuidance`, version 2 contract and
  workspace-edit profile only), manifest members (`directHumanGuidanceBoundary` then `directHumanGuidance`, once, after the source
  notice and before `untrustedEvidenceBoundary`; an unguided manifest is byte-identical to before), feed, dispatch guard
  (`agent_attempts.direct_guidance_mismatch`) and sealed-manifest agreement as the ordinary correction. It is available only within the
  shared allowance: at exhaustion a valid guided request is refused whole with `409 agent_attempts.direct_guidance_unavailable`,
  after every existing authority gate, before anything is sealed and again at the locked claim seam (an unused seal is removed), and
  creates no attempt, escalation or authorization change; an unguided request keeps the idempotent escalation above. It grants no
  extra correction, no authorization and no authority over the source, budgets, permissions or scope.
- **Correction guidance read model.** The diagnosis status adds `correctionDirectGuidance { state, text }` with the existing
  semantics: null when there is no correction, otherwise that correction attempt's own `NotRecorded`, `Provided` (text) or
  `Unknown` (no text) fact, separate from the diagnosis, any escalation and any authorization. The attempt evidence, history
  drill-down and cockpit latest-attempt projections carry the same fact and must agree. It states what the host supplied to the
  sealed context, never that a provider followed it.
  The cockpit offers the existing owned guidance editor beside the unguided correction action only while a correction is applicable,
  not running or blocked, and within the allowance; its request, pending and error state and draft belong to the run and the diagnosis
  source and to the committed interaction lifetime, it is disabled while the status is loading and withheld while the status read has
  failed (which discards an unsent draft with it), and the exhausted "Record human escalation" action stays unguided.
- **Re-review.** The corrected report's ordinary code review judges the implemented plan and still requires every enabled command's
  latest execution for the new checkpoint to be Passed.

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
