# ADR-0010: Add a role-scoped review-correction response contract

Status: Accepted

## Context

The first implementation review can request changes. The correction attempt is
still authorized by the Implementer role, but it performs a different job from
the initial implementation attempt and must have a different durable input and
result contract.

A corrected checkpoint must be independently verified before it can be
reviewed again.

## Decision

Add `AgentResponseContract.ReviewCorrection` and map it to
`AgentRole.Implementer` with `AgentEffectKind.WorkspaceMutating`. The contract
consumes exactly one previous `ExecutionReport` followed by every applicable
`ReviewFinding` in deterministic collaboration-timeline order. Its valid
successful result contains exactly one `RevisionResponse` for each finding and
one new `ExecutionReport`, all bound to the same run and implementation chain.

The correction is claimed before external execution, dispatch is protected by
an exact ordered-input duplicate check, and a successful result creates a new
immutable Git checkpoint atomically with the durable messages and events. The
provider adapter remains a replaceable Infrastructure concern, but provider
substitution is not end-to-end safe until an equivalent hardened mutation
contract and empirical boundary evidence exist for that provider.

An explicit user-requested re-review may target either the initial successful
implementation report or the newest successfully applied correction report.
The re-review requires fresh Passed verification for the new checkpoint and
uses a bounded manifest containing the previous report, findings, revision
responses, proposal, checkpoint, verification, and Git evidence. Re-review is
never automatic, and a successful re-review may expose another explicit
correction request.

No raw provider transcript is persisted or injected as workflow state. Gemini,
assignment snapshots, fallback, parallel executors, and automatic correction
orchestration remain deferred. Correction-round exhaustion policy remains a
later increment and is not enforced by this decision.

## Consequences

One role may own multiple response contracts, so contract lookup is keyed by
`AgentResponseContract`, while role remains the collaboration-authorization
dimension. The public API exposes bounded correction status and safe outcomes,
not prompts, paths, credentials, or provider payloads.
