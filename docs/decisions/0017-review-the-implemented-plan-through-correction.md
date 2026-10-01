# ADR-0017: Review the implemented plan through correction

Status: Accepted

## Context

A Claude implementation consumes one specific Proposal as its first ordered input (sequence zero) and records an
`ExecutionReport` replying to it. For an ordinary first Resolver revision, `ImplementerExecutionReportEligibility`
nevertheless resolved the chain to the Planner root only, and `CreateCodeReviewAttempt` sealed that root as the review's
`resolvedPlan`. The Codex review therefore judged the work against the superseded scope that the accepted challenge resolution
had replaced. [ADR-0016](0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md) separated the two
identities for a human-authorized depth-two plan, but deliberately preserved the former review target (and manifest bytes) of
every ordinary form. This decision removes that preservation for newly claimed reviews only.

## Decision

**Narrow supersession.** ADR-0017 supersedes only the part of ADR-0016 that preserves the ordinary first-revision review target
and its future review-manifest bytes. ADR-0016's historical text is unchanged and its authorization, claim, manifest,
dispatch, and cockpit decisions all stand. ADR-0010's correction inputs, reply identities, budgets, and extra-correction
authorization are unchanged.

**Two identities.** The report-chain result carries `OriginalProposal`, the actual validated Planner root (the historical lineage
identity and the reply target of every correction report), and `ImplementedPlan`, the initial implementation's exact
sequence-zero Proposal. Both are derived from the complete validated input chain, never from a report's reply alone. Every
initial `ExecutionReport` must reply to that first input; a report replying to the root instead of its revised first input,
to an unrelated same-run root or revision, or with a missing or foreign first input is not a valid chain, and malformed evidence never falls back to a root. A
Resolver root comes from the existing validated `PlanningLineage`.

**Supported forms.** The accepted Planner root (both identities equal the root), a first Resolver revision with its complete
ordered Decisions, that revision with its exact optional Acceptance, and the human-authorized final depth-two revision. Every
existing eligibility restriction is preserved, including the refusal of challenged first revisions and unauthorized depth-two plans.

**Correction.** `ImplementedPlan` is carried unchanged through every valid correction link. Correction reports still reply to the
root, revision responses still reply to their findings, and correction inputs stay exactly the previous report followed by all
applicable findings in timeline order. No plan input or plan text is added to the correction manifest and no planning grant is
consumed.

**Review manifests.** A newly claimed initial review, a newly requested format repair (including one of an older failed source,
which keeps its source's exact report and verification inputs and its one-repair eligibility), a correction re-review, and a format
repair of that re-review seal the implemented Proposal's identifier, summary, and structured content as `resolvedPlan`. The
builders, correction evidence, 32 KiB bound, evidence fitting, fixed instructions, evidence boundaries, output schemas, and
verification selection are unchanged; only the selected plan values differ, and the plan is never truncated.

**Forward only.** Nothing historical is rewritten: no message, outcome, approval, artifact, hash, or sealed manifest. A review
claimed and sealed before this decision and not yet dispatched replays its existing sealed bytes, including a manifest naming the
root, and is neither rebuilt nor rejected because the target rule changed. A review that already concluded keeps its recorded
outcome: no approval is revoked and no entitlement to another review of the same evidence arises.

## Consequences

A review of an ordinary revised plan evaluates the accepted challenge resolution it actually implemented instead of findings
against an abandoned plan. A historical review of such a plan remains a review against the root, which is a documented, accepted
limitation of history, not something this decision repairs. No API operation, public DTO, migration, dependency, provider flag,
permission, tool, adapter version, output contract, or frontend behavior changes.
