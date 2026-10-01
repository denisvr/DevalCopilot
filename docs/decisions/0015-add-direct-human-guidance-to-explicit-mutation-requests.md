# ADR-0015: Add direct human guidance to explicit mutation requests

Status: Accepted

## Context

The only human text that can reach an implementation-class attempt today is the rationale of the authorization of an
extra review correction after the ordinary budget is exhausted
([ADR-0010](0010-add-review-correction-response-contract.md) and the bounded-guidance contract in the
[protocol](../architecture/agent-collaboration-protocol.md#bounded-human-guidance-for-one-authorized-review-correction)).
The two explicit mutation requests, the initial implementation of a resolved plan and an ordinary review correction, carry
only a source ID. A human who sees a misreading coming therefore cannot clarify the work before spending a correction
slot, and must wait for exhaustion, an escalation, and an authorization. This decision is additive. It reverses none of
ADR-0004, ADR-0005, ADR-0009, ADR-0010, ADR-0012, ADR-0013, or ADR-0014, and it adds no authority, attempt, budget,
permission, provider argument, output schema, or recovery path.

## Decision

**Optional request input.** Both existing protected operations gain an optional `guidance` member and keep their routes,
source requirements, and response shapes: `POST /api/runs/{runId}/agent-attempts/implementation`
(`{ planProposalMessageId, guidance? }`) and `POST /api/runs/{runId}/agent-attempts/review-correction`
(`{ implementationReviewAttemptId, guidance? }`). Omitted or `null` guidance is exactly the previous behavior. Each
request body is capped at 8 KiB. The text is advisory clarification of work the authoritative plan or the complete findings
already authorize; it is never an instruction to the host and cannot change the objective, inputs, schema, permissions,
working directory, verification, Git or network restrictions, model, turn limit, budgets, or approval.

**One shared bounded-text policy.** `DirectHumanGuidance.Normalize` and the authorization's `ReviewCorrectionGuidance.Normalize`
share one deterministic policy (`BoundedGuidanceText`): Unicode form C, line endings to `\n`, surrounding whitespace
trimmed; non-blank valid Unicode of at most 600 UTF-16 code units; no control character except `\n`; and the existing
bounded-summary content screen, a best-effort filter that does not guarantee a secret is absent. Only the authorization keeps
its reserved default rationale: that exact sentence stays rejected as authorization guidance and remains acceptable as direct
guidance, because the two are stored separately and can never share bytes. Supplied invalid text (including empty or
whitespace-only text) is refused before any read or external work as `400 agent_attempts.direct_guidance_invalid` by a
validator on each command, repeated by the handler, with a fixed message that never echoes the input.

**One immutable snapshot on the claimed attempt.** `attempts.AgentDirectHumanGuidance` is a nullable TEXT column added by
`AddDirectHumanGuidance` with no default and no backfill, so every historical attempt is null; nothing is inferred from
artifacts, messages, or authorizations, and null never proves that no human guidance existed. Only the two mutation claims
assign it, in the same single commit as the attempt, its ordered input rows, its manifest metadata, and the existing budget
reservation, so a refusal or rollback leaves no claim, input, artifact, reservation, or event. The attempt factories accept
only already-normalized text and only with the version 2 mutation contract and the workspace-edit profile. The column is read
as an exact value: absent, valid (exactly its own normalized form), or malformed; malformed text is never accepted, never
treated as absent, and preserved by unrelated saves. There is no run-wide setting, draft entity, separate table, new message
type, or generic instruction framework.

**Ordinary correction budget only.** Direct guidance is available only within the ordinary review-correction budget. At
exhaustion a request that carries guidance is refused whole with `409 agent_attempts.direct_guidance_unavailable`, even when an
extra authorization exists. That refusal sits inside the existing exhaustion branch, after every existing gate and before any
escalation is created or authorization is read, so it creates no escalation, consumes no authorization, and claims nothing.
Requests without guidance, the escalation, both authorization operations, and the authorization's own guidance and
`humanGuidance` manifest member are unchanged. The two forms of guidance are never merged, replaced, or reinterpreted.

**Sealed context.** The manifests of both implementation forms and of ordinary corrections gain, only when guidance exists, a
fixed host-authored `directHumanGuidanceBoundary` and one `directHumanGuidance` object holding the exact accepted text once
(`{ "text": "…" }`), placed before `untrustedEvidenceBoundary`. A manifest without direct guidance is byte-identical to before,
including an authorized correction's `humanGuidance`. The 32 KiB bound is unchanged; the existing evidence-fitting policy
shrinks repository evidence to make room, and the guidance is never truncated.

**Consistency before a provider can run.** Both eligibility feeds exclude an attempt whose snapshot is malformed or whose
provider, role, response contract, permission profile, or exact version 2 contract is incompatible with recorded guidance;
historical unguided and authorized attempts stay eligible. `MarkAgentAttemptDispatched` applies the same coherence test to the
snapshot and the complete assignment tuple it is judged against (response contract, role, provider, permission profile, adapter
contract version), all read afresh and untracked in one statement inside its own transaction without touching pending tracked
writes; the two mutation supervisors pass the snapshot their
feed projected, and any difference, or a recorded snapshot with no stated expectation, is refused as
`409 agent_attempts.direct_guidance_mismatch`, so an earlier projection or a stale tracked entity confers no authority. The
internal invocation requests carry the expected snapshot, and both Claude adapters check, through the existing sealed-artifact
verification boundary, that the sealed manifest is a parseable JSON object that agrees with it exactly (an unparseable text or a
non-object root never proves absence; guidance requires each member once, the fixed boundary, only the accepted
text, and exactly one untrusted-evidence boundary after the guidance) before any process starts; any disagreement is an ordinary failed invocation with zero
process starts. Accepted context is never reconstructed from current settings, and no CLI argument, tool, environment,
permission, session, output schema, or provider version changes.

**Projections and UI.** The implementation status, the review-correction status, the attempt evidence (the history drill-down),
and the cockpit's latest Agent attempt expose `directGuidance { state, text }`: `NotRecorded` (no direct guidance was recorded; this
is the neutral state of historical and new unguided attempts alike and never says that none was submitted), `Provided` (the
accepted text), or `Unknown` for malformed or incoherent stored facts (no text, no
throw). It is labelled separately from the extra-correction authorization and states what the host supplied, never that a
provider followed it. The cockpit's implementation and correction actions gain an optional guidance editor beside the plain
button; drafts, busy and error state, and continuations are owned by run, source, and the committed interaction lifetime, and
the editor is not offered at exhaustion (the server gate stays authoritative).

## Consequences

The fact proves what the host sealed into the attempt's context, not that a provider read or followed it. A guided correction
cannot be requested at exhaustion; the human must use the existing authorization flow, which keeps its own guidance. The
sealed-manifest agreement is checked at the invocation boundary, after dispatch has been marked, so a snapshot that was altered
out of band ends as a failed attempt that consumed its slot rather than as a never-dispatched one; the dispatch seam guards the
drift between a feed projection and the commit, and no protection is claimed against every hostile write after that boundary.
A BLOB-class value written out of band into the TEXT column is decoded as text by the database driver and judged by the same exact
rule. Scheduling, automatic advancement, replacement, generic chat, budget overrides, and other stages remain deferred.
