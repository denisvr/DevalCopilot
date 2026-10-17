# Agent collaboration dialogue

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the protocol's typed dialogue: the project-owned message envelope,
role-first message authorization and eligibility, the message types, the
version 1.0 reply semantics, the default interaction from planning through
execution and review, the authority matrix and structured output failure. The
current durable ledger boundary, run execution mode and isolation and explicit
abandonment are in the [ledger](agent-collaboration-ledger.md) companion, and
the envelope's versioning rule stays under the protocol's
[protocol evolution](agent-collaboration-protocol.md#protocol-evolution)
heading. The sections below are moved in their baseline order with their
heading levels, and the protocol's original headings forward here. Their seven
in-page links to sections outside this companion now name the protocol, whose
retained headings of the same titles forward to the owning companions; no other
moved text changed. Their directional references ("described next", "Critical
review above", "Planning and Resolution above") still mean sections of this
companion, and "this protocol" means the agent collaboration protocol as a
whole. Their statements are as written at the baseline; the move reconciles or
endorses none of them. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

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
[Optional second challenge round and escalation](agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation)).
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
[Explicit human authorization of one escalated-plan implementation](agent-collaboration-protocol.md#explicit-human-authorization-of-one-escalated-plan-implementation));
the fixed instruction text distinguishes the two. It is HumanSubmitted, addressed to the
Orchestrator, and carries the fixed authorization instruction plus either the
fixed default rationale or, only for an explicit guided authorization, one
bounded, normalized guidance text (see
[Bounded human guidance for one authorized review correction](agent-collaboration-protocol.md#bounded-human-guidance-for-one-authorized-review-correction)).
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
[Optional second challenge round and escalation](agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation)).

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
(see [Explicit verification failure diagnosis](agent-collaboration-protocol.md#explicit-verification-failure-diagnosis)).

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
[One manual Codex Planner format repair](agent-collaboration-protocol.md#one-manual-codex-planner-format-repair)) and the Claude
CriticalReviewer, Codex Resolver, and Codex CodeReviewer stages (see
[One manual format repair of the remaining read-only stages](agent-collaboration-protocol.md#one-manual-format-repair-of-the-remaining-read-only-stages)).
There is no automatic retry, and no repair of a mutating Implementer or ReviewCorrection attempt.
