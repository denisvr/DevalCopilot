# ADR-0009: Separate agent roles, effects, and provider assignments

## Status

Accepted

## Context

The first real collaboration slices intentionally implemented one provider for
each workflow responsibility: Codex plans, resolves challenges, and reviews
implementations, while Claude Code performs critical review and implementation.
This proved the durable attempt, evidence, recovery, and adapter boundaries, but
some current names and policies still use provider identity as a proxy for
workflow role or execution authority.

Adding another provider by copying those provider-specific paths would make the
domain increasingly depend on the initial Codex and Claude Code assignment. It
would also make it difficult to answer historically which provider, model,
effort, permission profile, and adapter contract executed an attempt.

Provider choice, workflow responsibility, response validation, and repository
mutation are related but distinct concerns. They must be separated before a
third provider or fallback policy is introduced. This separation must preserve
the existing fail-closed dispatch, immutable evidence, restart reconciliation,
and repository/worktree exclusivity guarantees.

Parallel candidate executors are a separate problem. The current model
deliberately permits one running attempt per run, one mutating run per physical
repository, and one writer per owned worktree. Weakening those invariants merely
to compare two providers would expand the scheduler, lease, checkpoint, review,
and selection models beyond the MVP need.

## Decision

### Use role-first attempt semantics

Agent attempt policy is defined by a closed, Domain-owned contract whose
semantic dimensions are:

- workflow role;
- response contract;
- execution effect;
- outcomes that produce completed attempt status.

Provider identity is not part of that semantic contract and does not grant
workflow, Git, shell, filesystem, network, approval, or publication authority.
Response parsing and validation remain role- and contract-specific rather than
being collapsed into a generic result with nullable fields.

Failure and dedicated pre-dispatch classifications (a race already resolved by
another attempt, a source or workspace change detected before dispatch, a
provider or evidence failure) remain validated by their own operation-specific
handlers and Domain transitions. The bounded stabilization does not attempt to
centralize every failure classification into this contract.

The first execution-effect classification is deliberately small:

- `ReadOnly`;
- `WorkspaceMutating`.

Mutation leases, workspace eligibility, interruption handling, reconciliation,
and future fallback safety depend on the effect classification, not on a
provider name or an incidental role check. A more granular effect is added only
when an implemented capability demonstrates that these two values are
insufficient.

### Preserve provider identity as execution provenance

The selected provider remains an immutable fact of an attempt, but it is
execution provenance rather than semantic authority. Collaboration policy and
product surfaces become role-first while retaining enough provenance to render
truthful descriptions such as `Implementer · Claude Code` or
`Code Reviewer · Codex`.

Human and orchestrator authorship remain distinct from agent authorship. The
architecture-stabilization work will define the smallest closed participant
model and a truthful migration for existing provider-named message rows. It
must not replace closed identities with an arbitrary actor string or invent
historical role/provider facts that were not recorded.

Provider-specific runtime readiness, authentication observation, executable
resolution, and CLI adaptation remain provider-specific Infrastructure
responsibilities. Workflow APIs, cockpit actions, status projections, and
collaboration authorization become role-first where their behavior is owned by
the role rather than the provider.

### Stabilize before adding another workflow stage or provider

Delivery proceeds in this order:

1. complete and commit the current Codex implementation-review slice;
2. perform a bounded role-first architecture stabilization;
3. implement the Claude correction/revision loop;
4. complete the remaining Increment 4 controls;
5. complete the Increment 5 local supervised delivery loop;
6. demonstrate a working end-to-end local MVP;
7. add Gemini as an alternative Implementer together with authoritative
   assignment history and manual provider selection;
8. add manual fallback as a separate policy slice;
9. add automatic fallback only after its safe classifications, budgets, and
   recovery rules are explicit;
10. consider parallel candidate executors only in response to demonstrated
    product demand and under a separate decision.

The stabilization is bounded. It does not introduce a generic workflow engine,
an arbitrary DAG, dynamic role definitions, a plugin system, fallback, Gemini,
or parallel execution. Existing role-specific supervisors and validators are
not merged merely because their durable execution patterns look similar.

### Record assignment history with the first additional provider

The first Gemini execution and the minimum immutable agent-assignment snapshot
belong to the same future vertical slice. The assignment schema and migration
must land before the first Gemini attempt can be claimed, so every multi-provider
attempt is attributable from its creation.

The minimum assignment facts are:

- provider;
- requested model;
- observed model when the provider reports it;
- requested effort;
- observed effort when the provider reports it;
- permission profile;
- adapter contract version.

Unknown observed values remain explicitly unknown. A CLI default is not
recorded as an observed model or effort unless authoritative provider evidence
reports it. Legacy attempts are backfilled with unknown values rather than
inferred history.

The implementation must first evaluate extending the existing immutable
`Attempt` record with flat assignment facts. A one-to-one assignment entity is
justified only by concrete lifecycle, cardinality, or query requirements. The
provider must not have two competing authoritative storage locations.

Manual provider selection is part of the Gemini slice. Manual fallback is not:
it creates a new immutable attempt only after the prior attempt is terminal and
the workspace has been reconciled into a trusted state. Automatic fallback may
later act only on explicitly safe terminal classifications and within attempt,
run, duration, and usage budgets. Ambiguous mutation, source change,
credentials, permissions, policy refusal, or unknown provider outcome requires
human attention and never silently triggers another provider.

### Defer parallel candidate executors

The supported near-term model is one executor selected from multiple possible
providers, not multiple simultaneous executors competing on the same objective.
The following invariants remain intact:

- at most one running attempt per run;
- at most one mutating run per physical repository;
- one writer per owned worktree;
- review and verification bind to one selected checkpoint;
- no candidate-selection aggregate or competing candidate workspace exists.

Parallel candidates would require an explicit candidate-execution model,
separate workspaces and leases, checkpoint comparison and selection policy,
budget allocation, losing-candidate cleanup, and review semantics for the
selected result. That work is not implicit in multi-provider support.

## Consequences

- A role can later be assigned to Codex, Claude Code, Gemini, or another
  approved provider without changing the role's domain meaning.
- Repository mutation policy becomes reviewable independently from provider
  and role names.
- Current provider-specific adapters remain isolated and may continue to use
  different safe CLI contracts.
- Some existing provider-named APIs, UI types, factories, message participants,
  dispatch branches, and reconciliation checks require staged compatibility
  changes during architecture stabilization.
- Adding Gemini requires an assignment migration in the same slice, increasing
  that slice's size but avoiding unattributable multi-provider history.
- Fallback remains observable as a sequence of immutable attempts rather than
  an adapter-internal retry.
- The system intentionally forgoes parallel provider comparison until its
  additional safety and product value are demonstrated.

## Alternatives considered

### Add Gemini before assignment history

Rejected because the first multi-provider executions would not authoritatively
record the model, effort, permission profile, or adapter contract that produced
them, requiring a later migration to guess historical facts.

### Persist a separate assignment entity immediately

Rejected as a premature storage decision. `Attempt` already owns immutable
provider and execution facts. The Gemini slice must compare a flat extension
with a one-to-one entity using concrete lifecycle and query evidence.

### Use provider identity as workflow authorization

Rejected because a provider is an execution mechanism, not a semantic role or
authority boundary. It would require domain changes every time a role receives
another provider.

### Build one generic agent adapter and generic result

Rejected because provider invocation can be shared only below role-specific
request, schema, parsing, and validation boundaries. A nullable union would
weaken closed contracts and fail-closed result handling.

### Add automatic fallback with Gemini

Rejected because provider selection and fallback are different policies.
Fallback requires terminal classification, workspace reconciliation, budgets,
and explicit rules for when another invocation is safe.

### Run Claude and Gemini in parallel worktrees and select a winner

Deferred because it conflicts with current attempt, lease, workspace,
checkpoint, and review invariants and is not required to prove a useful
multi-provider executor.
