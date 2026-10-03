# ADR-0019: Add direct human guidance to diagnosis-origin corrections

Status: Accepted

## Context

[ADR-0015](0015-add-direct-human-guidance-to-explicit-mutation-requests.md) lets the two explicit mutation requests that existed
at the time, the initial implementation of a resolved plan and an ordinary review correction, carry short advisory direct human
guidance. [ADR-0018](0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md) later added a third explicit
mutation request: a Claude correction of the findings of a completed verification diagnosis. It uses the same
`ReviewCorrection` contract, Claude adapter, attempt factory, manifest builder, eligibility feed, dispatch gate and shared
correction allowance as the ordinary correction, but it deliberately accepted neither guidance nor an authorization. A human who
reads a diagnosis and sees a correction about to misread a finding therefore cannot clarify the work before spending a correction
slot, although the same human can do so for the equivalent ordinary correction. This decision closes that gap and nothing else. It
is additive: it reverses none of the earlier decisions and adds no authority, attempt, budget, permission, provider argument,
output schema, migration or recovery path.

## Decision

**Narrow extension of two requests.** ADR-0019 extends only ADR-0015's request scope, to include the diagnosis-origin correction, and
only ADR-0018's request contract, to accept optional guidance. The historical text of both stays unchanged. The protected operation
`POST /api/runs/{runId}/agent-attempts/verification-diagnosis/correction` gains an optional `guidance` member
(`{ verificationDiagnosisAttemptId, guidance? }`) and keeps its route, source requirements and response shape; omitted or `null`
guidance is exactly the previous behavior. The request body is capped at 8 KiB, as for the other guided requests. The text is
advisory clarification of the diagnosis findings the correction is already authorized to address. It never changes the objective,
the findings, the sealed instruction and source notice, the output schema, the working directory, permissions, tools, the
verification, Git or network restrictions, the model, the turn limit, the budgets or the approval, and it creates no
`HumanInstruction`, escalation or authorization. There is still no authorization input for this source, and the ordinary review's
extra-correction grants still cannot be used for it.

**One shared policy, no new rules.** `DirectHumanGuidance.Normalize`, its 600-UTF-16-code-unit bound, the control and content
screens, the fixed `400 agent_attempts.direct_guidance_invalid` refusal and its non-echoing message are reused unchanged. A validator
on the command rejects supplied text, including blank text, before the handler runs, and the handler repeats the check before any
read, Git work or sealing, so invalid guidance causes no external work.

**Same snapshot, same envelope.** The normalized text is assigned to the existing immutable `Attempt.AgentDirectHumanGuidance`
snapshot by the same claim, in the same single commit as the attempt, its exactly ordered inputs (the previous report, then every
finding in timeline order), its manifest metadata and the existing budget reservation. The diagnosis-origin manifest keeps its
fixed instruction, its fixed diagnosis source notice and every other member; only when guidance exists does it add the fixed
`directHumanGuidanceBoundary` and one `directHumanGuidance` object holding the exact text once, after the source notice and before
`untrustedEvidenceBoundary`. A manifest without guidance is byte-identical to before, an already sealed attempt replays its own
bytes, and the 32 KiB bound is unchanged: evidence shrinks to make room and guidance is never truncated.

**Shared allowance only.** Direct guidance is available only within the correction allowance this source shares with ordinary review
corrections. At exhaustion a valid guided request is refused whole with `409 agent_attempts.direct_guidance_unavailable`, after every
existing authority gate, so it creates no attempt, no `DiagnosisCorrectionEscalation` and no authorization change. The refusal is
applied before anything is sealed and again at the locked claim seam, where the allowance is read untracked under the write lock; a
manifest sealed for a request that the seam then refuses is removed. An unguided request at exhaustion keeps its idempotent durable
escalation, including when the allowance is spent between the early check and the lock.

**Existing protections apply unchanged.** The review-correction eligibility feed, the fresh dispatch snapshot guard
(`MarkAgentAttemptDispatched`, `agent_attempts.direct_guidance_mismatch`) and the Claude adapter's sealed-manifest agreement are
source-agnostic, so they protect a diagnosis-origin correction exactly as they protect an ordinary one: malformed storage, an
incoherent assignment, a snapshot that differs from what the supervisor expected, or a sealed manifest that disagrees with the
snapshot all fail closed before any provider process starts. No source, applicability, budget, lease or checkpoint gate is weakened.

**Read model.** The diagnosis status gains `correctionDirectGuidance`, using the existing fact and response types: null when the
diagnosis has no correction, otherwise that correction attempt's own `NotRecorded`, `Provided` or `Unknown` fact. It is kept apart
from the diagnosis, from any escalation and from any extra-claim authorization. The attempt evidence, the history drill-down and the
cockpit's latest Agent attempt already expose the same fact for the correction attempt and must agree with it.

**Cockpit.** The existing owned guidance editor is offered beside the existing unguided correction action, and only when a
correction is applicable, not running or blocked, and within the allowance. The "Record human escalation" action at exhaustion
stays unguided and the editor is withheld with an explanation. The request, its pending and error state and the draft belong to the
run plus the diagnosis source and to the committed interaction lifetime, with edit versions, newer-flow protection and handling of
replacement, return and unmount as for the other guided requests. Recorded guidance is presented as supplied context, never as
evidence that a provider read or followed it. The explicit evidence refresh and its partial-failure guards are unchanged: while the
diagnosis status is loading the editor is disabled, and while it has failed the editor is withheld, so a failed read removes an
unsent draft with the editor.

## Consequences

The fact proves what the host sealed into the correction's context, not that a provider read or followed it. A guided diagnosis
correction cannot be requested at exhaustion; the human can only record the unguided escalation, which grants no authority. The
guidance is intentionally visible in the sealed manifest and the projections and is not redacted; the content screen is a
best-effort filter. As with ADR-0015, the snapshot-versus-manifest agreement is checked at the invocation boundary after dispatch has
been marked, and no protection is claimed against every hostile write after that boundary. Scheduling, automatic advancement,
replacement, generic chat, budget overrides and other stages remain deferred.
