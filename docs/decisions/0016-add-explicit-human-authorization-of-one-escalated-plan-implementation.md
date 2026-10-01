# ADR-0016: Add explicit human authorization of one escalated-plan implementation

Status: Accepted

## Context

A proposal lineage allows at most one optional second challenge round. The second resolution produces a depth-two revised
Proposal and one host-constructed `Escalation` asking for a human decision
([protocol](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation)). Until now that
escalation could only be answered by starting a new planning lineage: every implementation path refused a depth-two Proposal
unconditionally (`proposal_lineage_exhausted`), so an owner who read both rounds and accepted the final plan had to spend a
whole new lineage restating it. This decision is additive and narrow. It reverses none of ADR-0004, ADR-0005, ADR-0009,
ADR-0010, ADR-0012, ADR-0013, ADR-0014, or ADR-0015, and it raises neither the review and resolution caps nor any budget.

## Decision

**One explicit human exception.** A human may authorize exactly one initial implementation claim for the final (depth-two)
Proposal of a completed second round. The automated lineage still ends at depth two: `PlanningLineage.MaximumDepth`,
`MaximumReviewableDepth`, and `MaximumImplementableDepth` are unchanged, a third review or resolution remains impossible with or
without an authorization, and no other plan form is affected. The authorization records a real human decision; it is not a
provider Decision, not an Acceptance, not an approval of any publication, and not a prerequisite the human can skip.
Authorizing and requesting the implementation are two separate operations.

**Operations.** `POST` and `GET /api/runs/{runId}/planning-escalations/{escalationMessageId}/implementation-authorization`
(protected MVC operations in `Features/Runs`). The `POST` body is only `{ "rationale": "…" }`. The instruction is fixed host
text. The rationale is required and bounded by the same compatible policy as the other human guidance
(`BoundedGuidanceText`: Unicode form C, LF line endings, trim, non-blank valid Unicode, at most 600 UTF-16 code units, no control
character except LF, the best-effort bounded-summary screen); invalid text is `400 planning_authorizations.rationale_invalid`
before any read, never echoed. The final Proposal is derived from the persisted escalation, never from a caller.

**Source validation, fail closed and non-echoing.** The escalation must be a host-constructed, attemptless, protocol-1.0
Orchestrator-to-Human `Escalation` replying to a complete, coherent two-round lineage (every Challenge, Decision, owner, role,
reply link, and the exact workspace, checkpoint and fingerprint, evaluated by the shared `PlanningLineage` rule), the only
escalation answering that final Proposal, with exactly the summary and content the second resolution writes (recomputed from the
identifiers). The run, its stored execution mode, its workspace, active lease, current checkpoint, and a fresh Git fingerprint
must be current, and no newer independent provider-observed Planner Proposal may exist (a later root replaces the whole lineage).
Unknown or foreign escalations are `404`; forged, ambiguous, stale, unreadable, or incoherent evidence is a fixed `409` that
repeats no stored text.

**One relation, one message.** `planning_implementation_authorizations` binds the run, the escalation message, the final
Proposal, and the exact workspace, starting checkpoint and fingerprint to one canonical `HumanInstruction` message
(`HumanSubmitted`, attemptless, Human to Orchestrator, replying to the escalation, with its own fixed summary, the fixed
instruction, and the normalized rationale in the existing two-field protocol shape). The rationale is stored only in the message.
The relation owns the only authoritative nullable `ConsumedByAttemptId` (with `ConsumedAtUtc`) as an EF concurrency token, with
unique indexes on the escalation, the final Proposal, the instruction message, and (filtered) the consuming attempt. The additive
migration `AddPlanningImplementationAuthorization` fabricates no grant: a historical escalation, and a historical
review-correction authorization, stay exactly as they were. There is no renewal, revocation, second grant, or deletion operation.
The existing review-correction `HumanInstruction` factory and authorization are untouched.

**Short serialized commit.** Bounded Git capture runs first, outside any transaction. The command then opens a short transaction
whose first statement (the atomic execution-mode confirmation) takes the database write lock; the run, lifecycle, workspace,
lease, checkpoint, fingerprint, lineage snapshot, and any existing grant are all read afresh and untracked inside it, so a commit by
another connection before BEGIN is seen and no tracked entity confers stale authority. Identical retries return the recorded
authorization without another message or event; a different rationale is `409 rationale_conflict`; a stale or consumed grant is
never revived. Authorization claims no Agent attempt, reserves no budget, seals no manifest, and starts no provider.

**Consumption by the ordinary explicit claim.** `POST …/agent-attempts/implementation` keeps its body and recognizes only the
specifically authorized final Proposal. It validates before external work and again inside a short transaction opened after
manifest sealing (same write-lock discipline, fresh untracked reads of context, lineage, and grant), then consumes the grant in
the same save as the Attempt, its ordered inputs, the manifest artifact, and the ordinary budget reservation. A refusal or a lost
race consumes nothing and removes any orphaned sealed manifest. Every exit after the manifest is sealed, including the caller's
cancellation and a raw failure at a late read, at the save, or at the commit, is settled from durable state rather than from the
exception or tracked entities: an uncommitted transaction is rolled back and released first, then an independent connection with a
token that is never cancelled decides. Only a claim that definitely did not commit loses its manifest; a claim that committed keeps its
manifest and its one spent grant, and an unanswered probe keeps the file and reports the outcome as unresolved. The exact ordered inputs are the final Proposal, every Decision of
its second resolution in collaboration order, then the authorization `HumanInstruction`; no earlier revision is promoted, no
Acceptance is invented, and the human message is never flattened into a provider Decision. A committed claim spends its grant
permanently, including a failed, interrupted, or undispatched attempt; there is no refund. Every existing lifecycle, execution-mode,
active-attempt, writer, workspace, lease, checkpoint, duplicate, count and time budget, token-stop, model, turn-limit, and
mutation-recovery gate is unchanged; the authorization satisfies only the plan-decision prerequisite.

**Sealed context.** The manifest gains a distinct bounded `resolutionEvidence` form, `humanAuthorizedEscalatedProposal`, carrying
the complete ordered second-round Decisions and an exact `humanAuthorization` object (authorization, escalation and instruction
identifiers, the fixed instruction, and the rationale), preceded by a fixed host-authored `humanPlanAuthorizationBoundary` that
states what the record is and that its rationale is advisory and cannot widen the plan or any permission. Authority evidence and
human text are never truncated: the 32 KiB ceiling is kept by shrinking repository evidence only, and evidence that cannot fit is
refused whole before sealing, leaving the grant unconsumed. Every earlier manifest form is byte-identical to before.

**Dispatch and downstream.** The eligibility feed excludes, and the fresh dispatch gate refuses, an attempt whose grant, consumption
owner, source, or ordered inputs do not match the durable facts (an authorized attempt needs exactly the one grant it consumed;
an ordinary attempt holds none). The internal invocation expectation carries the bounded authorization facts solely so the actual
Claude implementation adapter can refuse a sealed-form disagreement before any process starts. No provider flag, tool, permission,
output schema, or adapter contract version changed. Result recording validates the same identity. Restart replay uses the same
consumed grant and the immutable sealed manifest and consumes nothing again. The resulting ExecutionReport is valid through local
verification, CodeReviewer, ordinary correction, and re-review: consent is validated against the implementation's starting
checkpoint, and the actual Planner root is resolved across both revisions (the immediate depth-one parent is not the root). The chain
result carries two distinct identities: the Planner root stays the historical lineage and ADR-0010 reply identity (the first report
replies to the final Proposal and every correction report replies to the root), while the implemented plan, which is the final
Proposal, is the plan a code review judges. The initial review, its manual format repair, and the correction re-review all receive the
final Proposal's identifier and content as their `resolvedPlan`; every earlier plan form keeps its review target and manifest bytes
exactly. ADR-0010's exact correction inputs, replies, verification selection, budgets, output contracts, and separate
extra-correction authorization are unchanged.

**Cockpit.** For a loaded lineage that ends in its escalation, a panel states the final plan and second-round decision identities
and the consequence of authorizing, takes the required reason, and shows only the server's own state (`Absent`, `Available`,
`Consumed`, `Stale`, `Invalid`) with the exact recorded reason. The implementation action is offered separately and only for the exact
final plan the server names with an `Available` authorization; provider review and resolution of the final plan stay unavailable.
The facts read are owned by the committed identity and request lifetime that asked for them: returning to an earlier run, escalation,
or event sequence awaits a fresh read, and an earlier Available, Consumed, or error record never authorizes the new interaction, not
even for one frame. Drafts, handlers, pending and error state, and request and refresh continuations are owned by run, escalation, final Proposal,
and the mounted lifetime; a late accepted request stays a server operation but cannot update another interaction or clear a newer
draft, and nothing is inferred from a successful click.

## Consequences

The owner can accept an escalated final plan once without restating it in a new lineage, and the whole evidence chain stays
verifiable. The authorization is consumed by the claim, not by a successful implementation, so a failed attempt needs a new
planning request. `Available` means recorded, coherent, unconsumed, and bound to the current checkpoint; it never means a provider
is available or a budget remains, and the claim re-checks everything. A reviewer of an authorized implementation judges the final
plan that was implemented, never the superseded root or first revision. A human message and the rationale are intentionally visible in the recorded `HumanInstruction` and the
sealed manifest and are not redacted. Scheduling, automatic advancement, a third round, plan editing, root or depth-one overrides,
budget overrides, generic approvals or chat, and publication remain deferred.
