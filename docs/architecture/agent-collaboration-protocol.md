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
never as a fixed value compared to authorize the role. The same correction
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
additional review-correction claim. It is HumanSubmitted, addressed to the
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
requested attempt described next.

### Resolution

This stage is also real today, bound to one durable attempt per requested
resolution of one specific Challenged review. Codex receives that review's
original Proposal and its complete, ordered Challenge set, and resolves every
challenge explicitly as `accepted`, `partiallyAccepted`, or `rejected`; a
partially accepted or rejected challenge must include reasoning. Resolving
never edits the repository. Changes become a new plan revision — one revised
Proposal replying to the original — rather than silently editing history, and
that revision can re-enter Critical review above like any other Proposal.
Never a duplicated resolution of the exact same review: a resolution already
recorded for the identical ordered Proposal-plus-Challenge-set supersedes any
further attempt at claim or dispatch time.

### Execution and review

The initial Implementer currently uses Claude Code and implements only the
resolved plan, recording unexpected discoveries as challenges or questions.
Gemini is deferred until its administrator-provisioned policy prerequisite is
met. This stage is real today, bound to one durable
attempt per requested implementation of one specific resolved plan (an
accepted original Proposal, or a resolved revised Proposal); execution never
edits anything outside the run's owned worktree, and it never runs Git, a
verification command, or any network operation itself. It records only the
implementation's own Execution report and the new Git checkpoint its
independently observed changes produced; it never claims automatic
verification, review, or publication of those changes.

Codex reviewing the implementation is also real today, bound to one durable
attempt per requested review of one specific Execution report. Codex receives
that report, the resolved plan it claims to satisfy, fresh bounded Git
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
Execution from a changes-requested review.

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
provision exists only as a **human-requested** repair of a Codex Planner
Proposal attempt; see
[One manual Codex Planner format repair](#one-manual-codex-planner-format-repair).
There is no automatic retry and no repair of any other role.

## Context assembly

Agent input is assembled from selected durable records:

- objective and current task;
- applicable project instructions;
- current plan revision and unresolved challenges;
- relevant decisions and findings;
- Git fingerprint and bounded diff summary;
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
- **No `--max-turns`**: implementation is inherently multi-step (read, edit, re-read), unlike a
  single-turn critical review; the actual bound remains the process-level timeout, cancellation,
  and process-tree termination already established for every other provider adapter.

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
(`claude-implementation-v1`). In practice its requested and observed model
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
  number) only when `hasMore` is true. Rows carry identity and lifecycle facts only.
- **Evidence metadata.** `GET …/agent-attempts/{attemptId}/evidence`
  (`GetAgentAttemptEvidenceQuery`) resolves by the exact `(RunId, AttemptId)` pair and
  `Kind == Agent`; an unknown, foreign-run, or non-Agent attempt is a safe 404
  (`agent_attempts.not_found`). It returns identity, lifecycle, host-measured process
  evidence, provider-reported token evidence, and metadata (purpose, byte length,
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
`modelUsage`, `total_cost_usd`, `duration_api_ms`, `stop_reason`, and every
other field are ignored.

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
  fingerprint, the unchanged expected Proposal schema, and instruction references, with no human instruction and
  no prior decisions, exactly as an ordinary claim — plus one fixed host-authored
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

## Token efficiency

- Build a context manifest for each attempt and include only inputs required by
  the current role and decision.
- Retrieve project documentation and source progressively instead of attaching
  the entire repository or documentation set.
- Reuse one durable summary of stable facts and link it to primary evidence;
  do not ask each agent to restate the same background.
- Send changed hunks, unresolved findings, and relevant neighboring code before
  considering a complete diff or file.
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

Repository files, comments, issue text, test output, CI logs, generated files,
and prior agent responses are quoted or delimited as untrusted evidence. They
cannot grant permissions, alter the protocol, request credentials, change
budgets, or bypass project instructions.

## Protocol evolution

The envelope is versioned. Additive optional fields may remain compatible.
Changing message meaning, authority, required fields, or resolution behavior
requires a protocol version and migration plan.
