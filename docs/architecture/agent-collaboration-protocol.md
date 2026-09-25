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

Records one fixed human authorization replying to an Escalation: authorize one
additional review-correction claim. It is HumanSubmitted, addressed to the
Orchestrator, and never carries arbitrary instruction text.

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
but it never invents missing decisions, evidence, or success.

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
Token, account-usage, duration, and the remaining Increment 4 controls remain
deferred.

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

**Codex — not proven; recorded as unknown.** No Codex CLI executable was
installed or discoverable in the environment where this contract was
established (the installed Codex desktop application is a different product
without a bundled CLI), so there was no authoritative local artifact to
inspect, and third-party sources describe mutually inconsistent token-usage
event shapes with differently named cache fields. Following the evidence rule
above, no Codex usage is parsed: every Codex-produced attempt (Planner,
Resolver, and Code Reviewer) records token usage as unknown until a future
slice establishes and verifies a real Codex contract.

Each role's status API and the run cockpit expose a separate `tokenUsage`
object beside `outcome` and `processExecution` — `inputTokens`,
`outputTokens`, `cacheCreationInputTokens`, and `cacheReadInputTokens`, each
null while unknown. The schema version is internal provenance and is never
exposed, and no path, argument, environment value, output, session
identifier, or credential is included.

The run cockpit also exposes a `tokenUsageSummary` across every dispatched
Agent attempt of the run, with `attemptsWithKnownUsage`,
`attemptsWithUnknownUsage`, and sums over the known attempts only. Its
`completeness` is `NoDispatchedAttempts` when nothing has been dispatched,
`Complete` only when every dispatched attempt has known usage, and `Partial`
otherwise. Only a `Complete` summary is ever presented as the run's total; a
`Partial` sum is shown as partial with the number of attempts it covers.
The cockpit streams the bounded usage columns of dispatched attempts into a
constant-memory aggregate; it neither imposes a row cap nor materializes
all attempts to compute the summary.
Because Codex usage is currently unknown, any real run that dispatched a Codex
role normally reports `Partial` — expected and truthful, not a defect. This
slice records and displays evidence only; it adds no token budget, threshold,
warning, or stop guardrail.

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
  This is implemented as `AgentTokenUsageEvidence` for Claude-produced
  attempts; it remains pending, and recorded as unknown, for Codex until its
  output contract is proven (see "Provider token-usage contracts").
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
