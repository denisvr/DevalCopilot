# Architecture documentation

This section describes the product-specific architecture that specializes the
shared engineering contract.

- [System overview](system-overview.md) defines runtime boundaries and
  responsibility ownership.
- [Workflow model](workflow-model.md) defines lifecycle dimensions,
  transitions, gates, and recovery behavior.
- [Agent collaboration protocol](agent-collaboration-protocol.md) defines typed
  dialogue, challenges, decisions, and bounded iteration.
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

Accepted material choices are recorded separately under
[`docs/decisions`](../decisions/README.md). Architecture documents explain how
those decisions work together and may evolve without rewriting decision
history.
