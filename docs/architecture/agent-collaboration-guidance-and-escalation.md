# Agent collaboration guidance and escalation

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the human guidance, escalation and diagnosis contracts: bounded guidance
for one authorized review correction, optional direct human guidance for
mutation requests (including diagnosis-origin corrections), the optional
second challenge round and escalation, explicit human authorization of one
escalated-plan implementation and explicit verification failure diagnosis.
Guidance, whether carried by the one review-correction authorization or
supplied directly with a mutation request, is advisory clarification only; it
grants no authority, attempt, budget, permission or approval. Each human
authorization is a separately recorded grant of exactly one claim; the
review-correction and escalated-plan authorizations are distinct, and neither
is guidance. An escalation records that a human decision is needed and grants
nothing by itself. A diagnosis is a read-only finding source that never
produces a review approval. None of these is a review or local-delivery
approval. The ordinary review-correction contract (eligibility, inputs,
result, its correction budget and the escalation at exhaustion) is in the
[Review correction](agent-collaboration-correction-and-budgets.md#review-correction)
section of the correction and budgets companion, as are the run-wide
claim-count and reserved-time budgets; this companion holds the guidance that
may accompany that correction and its one extra authorization. Token stops,
model, effort and turn-limit requests and
format repair are in the
[token-activity controls](agent-collaboration-token-activity-controls.md),
[invocation configuration](agent-collaboration-invocation-configuration.md)
and [format repair](agent-collaboration-format-repair.md) companions. The
sections below are moved verbatim except one link that now points back to the
protocol's role-first rule; the protocol's original headings forward here, and
their other directional "above", "below" and "next" references and their
in-page section links mean sections in this companion. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Bounded human guidance for one authorized review correction

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

## Optional direct human guidance for mutation requests

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

## Optional second challenge round and escalation

The existing Proposal → Challenge → Decision → revised Proposal chain supports **one optional second
review-and-resolution round** for a proposal lineage. It is explicit and manual end to end — every review and
every resolution is a separately requested attempt — and it adds no provider capability, CLI argument, session
resume, automatic loop, human override, or account-usage input. ADR-0004's bounded challenge rounds and
ADR-0009's role-first authority are applied unchanged; nothing in an accepted ADR is reversed.

- **Lineage, from durable identity only.** `PlanningLineage` derives the chain from same-run,
  provider-observed messages, their exact owning Agent attempts (role, response contract, and provider
  coherent with the message's recorded `Actor`, per [role-first rule](agent-collaboration-protocol.md#role-first-message-authorization-and-eligibility)), the ordered attempt inputs,
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

## Explicit human authorization of one escalated-plan implementation

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

## Explicit verification failure diagnosis

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
