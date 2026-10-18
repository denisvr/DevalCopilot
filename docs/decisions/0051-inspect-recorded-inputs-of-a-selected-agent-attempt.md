# ADR-0051: Inspect recorded inputs of a selected Agent attempt

Status: Accepted

## Context

The existing recorded-input inspection resolves an attempt through a message it
produced. HIST-ATT-01 and HIST-MET-01 make an older attempt independently
inspectable, including one that emitted no message, but its recorded input
references remain inaccessible there. These references already exist in
`AttemptInputMessage`; they are not the full prompt or a resumable session.

This is an additive read contract under ADR-0003/0004/0009 and ADR-0033. It
changes neither ADR-0037's minimal attribution inspector nor the existing
message-linked input reader. Acceptance selects a contract, not delivery or
publication, and does not complete an increment.

## Decision

**One protected, bodyless, read-only operation.**
`GET /api/runs/{runId}/agent-attempts/{attemptId}/inputs`
(`GetAgentAttemptInputsQuery`, one MVC endpoint through `IApplicationMediator`)
resolves only that exact Agent attempt of that run. An unknown run, missing or
foreign attempt, or non-Agent attempt is the same fixed 404 using shared Problem
Details (`agent_attempt_inputs.not_found`, no rejected values). An existing
Agent attempt whose identity cannot be proved returns 200 `Unavailable`.

**Identity and bounded storage.** Apply the unchanged
`AgentAttemptIdentity.IsCoherent` scalar rule and assignment-snapshot policy
using the guarded scalar approach of ADR-0037: classify stored enums in SQL,
guard assignment strings by stored byte length, preserve exact max-turns storage
semantics and reject oversized storage rather than treating it as absent. Do
not materialize complete Attempt or CollaborationMessage entities, prompts,
content JSON, summaries, artifacts, source files or provider session fields.
No raw SQL is added to Application. A narrow extraction of the same attempt
projection/identity evaluation is permitted only with unchanged ADR-0037
behavior proved; actor-to-attempt comparison stays with that operation.

**Input-set inspection bound.** Read at most 65 ordered scalar input rows for
the selected attempt, through its existing attempt/sequence index. At most 64
rows can be inspected; observing a 65th returns `Unavailable`, without a count
or partial list. This is a bound on this new inspection, not a limit on claims,
stored data or the old reader, and does not prove the stored set invalid. Resolve
at most those 64 distinct referenced message identities inside the same run.
Validate the entire inspected set before showing any prefix: unique nonempty
references, gapless positions from zero, every reference present in this run,
known protocol version/type and positive exact message sequence. No missing,
foreign, unknown or incoherent reference is omitted or replaced. Resolve no
reply, repair or input chain and read no message content.

**Closed response.** The API owns the entire response graph:

- Always: `runId`, `attemptId`, `status`, nullable `attempt`, `inputMessages`,
  nullable `inputMessageTotalCount`, and `inputMessagesOmitted`.
- `Unavailable`: null attempt and count, empty list, omitted false. It covers
  unprovable attempt identity, an oversized inspection or an incoherent set,
  without disclosing which stored value failed.
- `Empty`: a coherent Planner attempt with zero recorded inputs; its immutable
  header, count 0, empty list, omitted false. Zero rows for any other supported
  role are `Unavailable`, following the existing role-aware empty rule.
- `Recorded`: a coherent nonempty set; the immutable header, exact inspected
  count 1-64, its first at most 10 entries in stored order, and omitted exactly
  when count exceeds 10. The header contains only attempt ID/number, role,
  provider and response contract. Each entry contains only zero-based input
  `sequence`, `collaborationMessageId`, recognized `type`, canonical positive
  Int64 decimal-string `collaborationSequence`, and `occurredAtUtc` as an
  invariant UTC round-trip **string** (not a generated Date property).

The timestamp string is validated before display using the proven wire-instant
rule. The exact sequence is never coerced to a JavaScript number. The prefix
and omission disclosure are not pagination or a complete-history claim.
Malformed identity/timestamp storage and unexpected database/cancellation
failures keep the normal fault pipeline; no broad exception is masked as
`Unavailable`.

**Cockpit and ownership.** Inside the selected attempt, a collapsed
"Recorded collaboration inputs" control reads once only when opened; retry is
explicit. It needs no producing message, including for failed/interrupted
attempts. Display the returned references as plain text with the existing
recorded-input caveat, never the complete prompt/context or session. Do not
fetch referenced content or navigate chains. This is independent of the facts
and artifact viewers: opening it changes neither and reads no artifact.

Reads, results and callbacks belong to the selected-attempt lifetime and each
opening, using existing owned-lifetime primitives. Closing/reopening, changing
attempt/run/project, returning A-to-B-to-A, parent reload and unmount invalidate
prior work before the replacement's first frame; late answers and retained
callbacks cannot affect successors. Validate the entire answer against the
exact requested run/attempt and selected immutable header, closed shapes,
counts, ordered positions, unique references, known types, exact sequences and
timestamp text before display. A malformed answer is one fixed error with no
partial list. Nothing is polled, automatically retried, logged or persisted in
the browser or URL.

## Consequences and validation

An owner can inspect the recorded input identity of an older attempt without a
producing message. This adds no persistence, claim, mutation, Git/provider/
filesystem invocation, budget, approval, autonomy, replay or publication
authority. Existing message-linked input, attribution, evidence and artifact
contracts retain their behavior. An unavailable set is not an empty prompt or
proof of a failed invocation. ADR-0050 stays Proposed; P9 authority is unchanged.

Prove bounded scalar queries and malformed-storage refusal against real
file-backed SQLite, endpoint authentication/refusals/OpenAPI and exact Int64
wire values, no mutation/external effects, whole-answer frontend validation and
lifetime isolation, and an authenticated deterministic-provider browser path
with a nonempty set plus the existing no-message Planner empty case. Regenerate
the NSwag client reproducibly. Do not create new shared database fixtures or
extend ADR-0046 snapshot reuse.
