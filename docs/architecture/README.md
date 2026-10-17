# Architecture documentation

This section describes the product-specific architecture that specializes the
shared engineering contract.

- [System overview](system-overview.md) defines runtime boundaries and
  responsibility ownership.
- [Workflow model](workflow-model.md) defines lifecycle dimensions,
  transitions, gates, and recovery behavior.
- [Agent collaboration protocol](agent-collaboration-protocol.md) defines typed
  dialogue, challenges, decisions, and bounded iteration.
- [Agent collaboration provider contracts](agent-collaboration-provider-contracts.md)
  holds the protocol's Claude Code and deferred Gemini CLI safety contracts.
- [Agent collaboration execution evidence](agent-collaboration-execution-evidence.md)
  holds the protocol's per-attempt execution evidence and run-wide Agent
  process-duration summary.
- [Agent collaboration evidence inspection](agent-collaboration-evidence-inspection.md)
  holds the protocol's recorded collaboration-input provenance, sealed
  Agent-artifact window inspection, and Agent-attempt history and evidence
  inspection.
- [Agent collaboration format repair](agent-collaboration-format-repair.md)
  holds the protocol's manual read-only format repairs of the Codex Planner and
  the remaining read-only stages.
- [Agent collaboration usage evidence](agent-collaboration-usage-evidence.md)
  holds the protocol's provider token-usage contracts and Claude-reported model
  context limits.
- [Agent collaboration token-activity controls](agent-collaboration-token-activity-controls.md)
  holds the protocol's advisory per-provider run token-activity warnings and
  per-provider run token-activity stop at Agent claim.
- [Agent collaboration account usage](agent-collaboration-account-usage.md)
  holds the protocol's provider account-allowance contracts, run-scoped Codex
  account-usage stop at claim and dispatch, and run-scoped advisory Codex
  account-usage warning.
- [Agent collaboration invocation configuration](agent-collaboration-invocation-configuration.md)
  holds the protocol's Codex model and reasoning-effort catalog contract,
  explicit Codex model and reasoning-effort requests, explicit Claude
  model-alias and effort requests, optional Claude agentic-turn limit for
  mutation attempts, and relocated provider configuration observations.
- [Agent collaboration guidance and escalation](agent-collaboration-guidance-and-escalation.md)
  holds the protocol's bounded human guidance for one authorized review
  correction, optional direct human guidance for mutation requests, optional
  second challenge round and escalation, explicit human authorization of one
  escalated-plan implementation, and explicit verification failure diagnosis.
- [Agent collaboration correction and budgets](agent-collaboration-correction-and-budgets.md)
  holds the protocol's review correction with its correction budget and
  escalation, the run-wide Agent claim-count and reserved invocation-time
  budgets, and their manual-intake choice.
- [Agent collaboration source evidence](agent-collaboration-source-evidence.md)
  holds the protocol's bounded untracked-file previews, bounded tracked-hunk
  evidence and attested tracked-change text in Agent manifests, attested
  checkpoint comparison for human inspection, and project instruction context
  in Agent manifests.
- [Agent collaboration local delivery](agent-collaboration-local-delivery.md)
  holds the protocol's complete-set Human approval for local delivery and
  recorded local-delivery receipt.
- [Data and recovery](data-and-recovery.md) defines durable entities, event
  journal invariants, artifacts, and startup reconciliation.
- [GitHub and CI integration](github-ci-integration.md) defines remote
  publication, check monitoring, and CI correction loops.
- [GitHub branch publication execution evidence](github-branch-publication-execution-evidence.md)
  is evidence for a pending planner decision about the first approved normal
  branch push. It is not an accepted execution contract.
- [GitHub publication source-write denial feasibility](github-publication-source-write-denial-feasibility.md)
  is evidence for a pending planner decision about ADR-0050 P9. It selects no
  mechanism and is not an accepted contract.
- [GitHub publication retained-capture cross-check](github-publication-retained-capture-crosscheck.md)
  is retained-capture consistency evidence for the P9 run. It is not an
  accepted execution contract or P9 proof.
- [GitHub publication native exit and pipe-read semantics](github-publication-native-io-semantics.md)
  is documentation-to-source evidence about the P9 host's exit query and pipe
  reads. It is not an accepted execution contract or P9 proof, and it
  validates no native measurement.

Accepted material choices are recorded separately under
[`docs/decisions`](../decisions/README.md). Architecture documents explain how
those decisions work together and may evolve without rewriting decision
history.
