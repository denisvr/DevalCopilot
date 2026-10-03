# ADR-0020: Correct the escalation explanation and accept its two canonical forms

Status: Accepted

## Context

The one host-constructed `Escalation` that a successful second challenge-resolution round records told a reader that the revised
proposal "is not implementable through this lineage", recommended a new planning request, and mentioned neither the explicit human
authorization nor the separate implementation request that
[ADR-0016](0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md) added. The statement stopped being true
when ADR-0016 shipped, and a person reading it could not learn from the record that the exact final plan could be authorized once.

ADR-0016 also requires the source of an authorization to carry exactly the summary and content "the second resolution writes
(recomputed from the identifiers)", a single serialized text form, compared as whole text. Replacing the text the writer records
would therefore make every escalation already recorded in the original form unusable as a source, and with it every grant,
sealed manifest and downstream report chain that proves its own source again (the claim, the dispatch gate, the report-chain
eligibility and the cockpit read all recompute it). This decision corrects the explanation without invalidating that history.

## Decision

**Narrow supersession.** ADR-0020 supersedes only the single-source-text-form requirement of ADR-0016's source validation: the
escalation must carry exactly one of two complete canonical serializations, defined below, instead of exactly one. ADR-0016's
historical text is unchanged, and every other decision of it (the separate authorization and implementation operations, the
required bounded rationale, the one `HumanInstruction` and one relation, single permanent consumption, the exact ordered inputs, every
fresh authority gate, the sealed manifest form and the cockpit) stands, as do ADR-0017's through ADR-0019's.

**The new form, and the only form written from now on.** `PlanningEscalation` keeps protocol 1.0, the host-constructed attemptless
Orchestrator-to-Human shape replying to the final Proposal, the unchanged summary, the five content field names, the
identifier-derived `evidence` sentence and the unchanged `unresolvedDecision`. Its `options`, `consequences` and `recommendedChoice`
now state, as fixed text, that a human may inspect the final Proposal and the second-round Decisions and then either separately
authorize one implementation of that exact plan and explicitly request it, or request a new plan through a new explicit planning
request; that the record itself selects nothing, grants nothing and approves nothing; that only a durably committed implementation
claim consumes an authorization (a refused request, or a claim that definitely does not commit, consumes nothing, and a committed
claim stays consumed even if its execution later fails); and that a third critical review or resolution is not available. This
states ADR-0016's existing consumption boundary and changes none of it. Nothing about its effect changes: it is a
statement that a human decision is needed, never an Acceptance, an authorization or a selection.

**The legacy form, recognized and never written.** The serialization written before this decision is kept exactly, constant for
constant, in a separate internal type that the writer never references, so a later change to the writer cannot alter what a
historical record is compared against.

**One whole-form rule.** From the verified identifiers and the ordered second-round Challenges of the validated lineage, the host
recomputes the complete current serialization and the complete legacy serialization. The stored content is a source only if it is
ordinal-equal to one of those two whole strings. There is no field-wise or mixed form, no semantic JSON equivalence (member order,
whitespace and escaping are part of the form), no fuzzy or version-based wording, no arbitrary version, no caller-selected form, and no
inference from time, text, a CLI default or a successful click. Every other source and grant check is unchanged and mandatory: the
summary, protocol version, authorship, provenance, reply link, attemptlessness, uniqueness of the escalation of the final Proposal,
the coherent complete lineage, currency of the workspace, checkpoint and fingerprint, the absence of a newer independent Planner
Proposal, the grant's binding, its consumption coherence, and the downstream chain. The rule lives in the one
Application-owned source policy that the authorization, the implementation claim, the dispatch gate, result recording,
report-chain eligibility and the cockpit read share, so no consumer can disagree about it.

**Forward only.** No historical message, grant, attempt, approval, artifact or sealed manifest is rewritten, backfilled, migrated or
re-hashed. A historical escalation in the legacy form authorizes and is consumed exactly once like a current one; a legacy grant
that is unconsumed stays available, and one that is consumed stays consumed, with its downstream chain valid; an authorized manifest
sealed earlier replays its exact bytes after a restart. New escalations use only the current form. There is no schema, migration,
dependency, API shape, provider flag, profile, permission or budget change, and no grant renewal, refund, revocation or additional
round.

## Consequences

A person who reaches the escalation can learn from the record itself what the available decisions are and that choosing one is a
separate, explicit act. The two serializations are both fully pinned by tests
independent of the writer, so neither can drift silently. A third wording would require a further decision that names it. The
compatibility rule is deliberately a source-text rule only: it widens nothing a human may do and changes no budget, and a stale,
edited, mixed or forged text still fails closed without echoing it.
